using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Camera framing helpers shared by the review runners (review-only code).</summary>
public static class ReviewFraming
{
    public static (double TanV, double Aspect) Lens(Main main)
    {
        var cam = main.GetViewport().GetCamera3D();
        double half = (cam != null ? cam.Fov : 75.0) * Math.PI / 360.0;
        var sz = main.GetViewport().GetVisibleRect().Size;
        double aspect = sz.X / Math.Max(1.0, sz.Y);
        double tanV = Math.Tan(half);
        if (cam != null && cam.KeepAspect == Camera3D.KeepAspectEnum.Width) tanV /= aspect;
        return (tanV, aspect);
    }

    /// <summary>Fraction of frame height / width (0..1) spanned by the points seen from eye toward target.</summary>
    public static (double H, double W) Fill(IReadOnlyList<Vector3> pts, Vector3 eye, Vector3 target, (double TanV, double Aspect) lens)
    {
        var f = (target - eye).Normalized();
        var right = f.Cross(Vector3.Up).Normalized();
        var up = right.Cross(f);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var p in pts)
        {
            var v = p - eye;
            double z = v.Dot(f);
            if (z < 0.02) return (10, 10);
            double y = v.Dot(up) / (z * lens.TanV);
            double x = v.Dot(right) / (z * lens.TanV * lens.Aspect);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return ((maxY - minY) / 2.0, (maxX - minX) / 2.0);
    }

    public static (Vector3 Min, Vector3 Max, Vector3 Center) Bounds(IReadOnlyList<Vector3> pts)
    {
        var mn = pts[0]; var mx = pts[0];
        foreach (var p in pts) { mn = mn.Min(p); mx = mx.Max(p); }
        return (mn, mx, (mn + mx) * 0.5f);
    }

    /// <summary>Smallest distance along dirToEye from target at which height fill &lt;= fillH and width fill &lt;= fillW.</summary>
    public static Vector3 SolveEye(IReadOnlyList<Vector3> pts, Vector3 target, Vector3 dirToEye, (double TanV, double Aspect) lens,
        double fillH, double fillW, double minDist = 0.25, double maxDist = 250)
    {
        dirToEye = dirToEye.Normalized();
        bool Ok(double d)
        {
            var e = target + dirToEye * (float)d;
            var (h, w) = Fill(pts, e, target, lens);
            return h <= fillH && w <= fillW;
        }
        if (Ok(minDist)) return target + dirToEye * (float)minDist;
        double lo = minDist, hi = maxDist;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) * 0.5;
            if (Ok(mid)) hi = mid; else lo = mid;
        }
        return target + dirToEye * (float)hi;
    }

    public static Vector3 Dir(double yawRad, double elevDeg)
    {
        double el = elevDeg * Math.PI / 180.0;
        return new Vector3((float)(Math.Cos(yawRad) * Math.Cos(el)), (float)Math.Sin(el), (float)(Math.Sin(yawRad) * Math.Cos(el)));
    }

    public static double SlopeDeg(VivariumWorld world, Vec2 p)
        => Math.Acos(Math.Clamp(world.Terrain.Normal(p).Y, -1.0, 1.0)) * 180.0 / Math.PI;
}

/// <summary>
/// Automated guardian diagnostic, visual capture, locomotion &amp; contact verification runner.
/// Invoked via <c>--guardian-review &lt;output_dir&gt; [--guardian-species a,b] [--guardian-slope]</c>.
/// </summary>
public static class GuardianReviewRunner
{
    private sealed record Img(string file, string shot, int frame, double slope_deg, double[] camera_position, double[] camera_target, double fill_height, double t_s);

    private static async Task Frames(Main main, int n)
    {
        for (int i = 0; i < n; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void CollectBones(Node n, List<Vector3> res, ref Vector3? head)
    {
        if (n is Skeleton3D s)
        {
            var gt = s.GlobalTransform;
            for (int i = 0; i < s.GetBoneCount(); i++)
            {
                var pt = gt * s.GetBoneGlobalPose(i).Origin;
                res.Add(pt);
                string bn = s.GetBoneName(i);
                if (head == null && bn.Contains("head", StringComparison.OrdinalIgnoreCase)) head = pt;
                foreach (var key in FootKeys)
                    if (bn.Contains(key, StringComparison.OrdinalIgnoreCase)) { _lastFeet.Add(pt); break; }
            }
        }
        foreach (var c in n.GetChildren()) CollectBones(c, res, ref head);
    }

    private static Vector3? _lastHead;
    private static readonly List<Vector3> _lastFeet = new();
    private static readonly string[] FootKeys = { "foot", "toe", "paw", "hand", "claw", "talon", "ankle", "heel", "sole" };

    /// <summary>World-space skeleton bone positions (outliers dropped), padded so body thickness is included.</summary>
    private static List<Vector3> BodyPoints(Node root, Vector3 fallback)
    {
        var raw = new List<Vector3>();
        Vector3? head = null;
        _lastFeet.Clear();
        CollectBones(root, raw, ref head);
        _lastHead = head;
        if (raw.Count == 0)
        {
            raw.Add(fallback + new Vector3(0, 0.1f, 0));
            raw.Add(fallback + new Vector3(0.3f, 0.1f, 0));
            raw.Add(fallback + new Vector3(-0.3f, 0.1f, 0));
        }
        float Med(IEnumerable<float> v) { var a = v.OrderBy(x => x).ToArray(); return a[a.Length / 2]; }
        var med = new Vector3(Med(raw.Select(p => p.X)), Med(raw.Select(p => p.Y)), Med(raw.Select(p => p.Z)));
        var pts = raw.Where(p => p.DistanceTo(med) <= 8.0f).ToList(); // drop only absurd helper bones
        if (pts.Count == 0) pts = raw;
        return Inflate(pts, null);
    }

    private static List<Vector3> Inflate(List<Vector3> pts, float? padOverride)
    {
        var b = ReviewFraming.Bounds(pts);
        var ext = b.Max - b.Min;
        float pad = padOverride ?? Math.Max(0.04f, 0.05f * Math.Max(ext.X, Math.Max(ext.Y, ext.Z)));
        var res = new List<Vector3>(pts.Count * 7);
        foreach (var p in pts)
        {
            res.Add(p);
            res.Add(p + new Vector3(0, pad, 0));
            res.Add(p - new Vector3(0, pad, 0));
            res.Add(p + new Vector3(pad, 0, 0));
            res.Add(p - new Vector3(pad, 0, 0));
            res.Add(p + new Vector3(0, 0, pad));
            res.Add(p - new Vector3(0, 0, pad));
        }
        return res;
    }

    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory, string? speciesFilter)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = 60;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;

        bool slopeMode = Array.IndexOf(Main.UserArgs, "--guardian-slope") >= 0;

        var requested = string.IsNullOrWhiteSpace(speciesFilter) || speciesFilter.Contains("test-output", StringComparison.OrdinalIgnoreCase)
            ? null
            : new HashSet<string>(speciesFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        var roster = GuardianCatalog.All
            .Where(d => requested == null || requested.Contains(d.Id) || requested.Contains(d.Archetype))
            .ToList();

        Vec2 stageCenter = FindStageSpot(world, preferSlope: false);
        Vec2 steepSpot = FindSteepSpot(world, out double steepDeg);
        var lens = ReviewFraming.Lens(main);

        var report = new Dictionary<string, object>();
        var allImages = new List<(string Species, Img Rec)>();

        foreach (var def in roster)
        {
            GD.Print($"GUARDIAN_REVIEW_BEGIN species={def.Id} archetype={def.Archetype}");
            var files = new List<string>();

            void SetVegetation(bool on)
            {
                session.Flora.Visible = on;
                session.Coverage.SetProcess(on);
                session.Coverage.Visible = on;
                session.AmbientGround.Visible = on;
            }

            async Task<object> RunStage(Vec2 spot, string suffix)
            {
                bool slopeStage = suffix.Length > 0;
                double spotSlope = Math.Round(ReviewFraming.SlopeDeg(world, spot), 1);
                world.Clock.Paused = true;
                var (walkHeading, pathLen) = PickWalkDir(world, spot, steep: slopeStage);

                var g = world.GuardianSystem.InitializeGuardian(def.Id)
                    ?? throw new InvalidOperationException($"Failed to initialize guardian '{def.Id}'.");
                double sy = world.GroundHeight(spot);
                g.Position = new Vec3(spot.X, sy, spot.Z);
                g.Heading = walkHeading;
                var tgt = new Vec2(spot.X + Math.Cos(walkHeading) * pathLen, spot.Z + Math.Sin(walkHeading) * pathLen);
                g.TargetPosition = new Vec3(tgt.X, world.GroundHeight(tgt), tgt.Z);
                g.Scale = 1.0;
                g.Activity = GuardianActivity.Rest;
                g.ActivityDuration = 10.0;
                session.Guardian.Build(world);
                await Frames(main, 12);
                await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await Frames(main, 12);

                Vector3 Fallback() => new Vector3((float)g.X, (float)world.GroundHeight(g.PositionXZ), (float)g.Z);
                float GroundAt(Vector3 p) => (float)world.GroundHeight(new Vec2(p.X, p.Z));

                async Task Shot(string shot, Func<List<Vector3>, (Vector3 Eye, Vector3 Target)> plan)
                {
                    Vector3 eye = default, target = default;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var pts = BodyPoints(session.Guardian, Fallback());
                        (eye, target) = plan(pts);
                        eye.Y = Math.Max(eye.Y, GroundAt(eye) + 0.05f);
                        session.CameraRig.LookAtPoint(eye, target);
                        await Frames(main, 8);
                    }
                    var fin = BodyPoints(session.Guardian, Fallback());
                    var fill = ReviewFraming.Fill(fin, eye, target, lens);
                    await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string file = $"{def.Archetype}_{shot}{suffix}.png";
                    main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, file));
                    files.Add(file);
                    allImages.Add((def.Id, new Img(file, shot, 0, spotSlope, new double[] { eye.X, eye.Y, eye.Z }, new double[] { target.X, target.Y, target.Z }, Math.Round(fill.H, 3), 0)));
                }

                double yawF = g.Heading;
                // 1. closeup: three-quarter, animal fills ~65% of frame height
                await Shot("closeup", pts =>
                {
                    var c = ReviewFraming.Bounds(pts).Center;
                    var d = ReviewFraming.Dir(yawF + 0.62, 10);
                    var e = ReviewFraming.SolveEye(pts, c, d, lens, 0.65, 0.95, 0.3);
                    if (ReviewFraming.Fill(pts, e, c, lens).H >= 0.55) return (e, c);
                    // Long body: the whole animal cannot fill 55% of frame height, so frame head and forebody.
                    var fwd = new Vector3((float)Math.Cos(yawF), 0, (float)Math.Sin(yawF));
                    var hp = _lastHead ?? pts.OrderByDescending(p => p.Dot(fwd)).First();
                    float dmax = pts.Max(p => new Vector2(p.X - hp.X, p.Z - hp.Z).Length());
                    var sub = pts.Where(p => new Vector2(p.X - hp.X, p.Z - hp.Z).Length() <= 0.38f * dmax).ToList();
                    if (sub.Count < 3) return (e, c);
                    var sc = ReviewFraming.Bounds(sub).Center;
                    return (ReviewFraming.SolveEye(sub, sc, d, lens, 0.65, 0.95, 0.3), sc);
                });
                // 2. side: true profile at body height (flat stage only)
                if (!slopeStage)
                {
                    await Shot("side", pts =>
                    {
                        var c = ReviewFraming.Bounds(pts).Center;
                        var e = ReviewFraming.SolveEye(pts, c, ReviewFraming.Dir(yawF + Math.PI / 2, 0), lens, 0.65, 0.92, 0.3);
                        return (e, c);
                    });
                }
                // 3. feet: low camera on the foot/ground contact region. Ground-cover foliage hides the
                // feet at this height, so flora/ambient clutter render layers are hidden for feet and motion shots.
                SetVegetation(false);
                await Frames(main, 3);
                await Shot("feet", pts =>
                {
                    var b = ReviewFraming.Bounds(pts);
                    float cutoff = b.Min.Y + 0.2f * (b.Max.Y - b.Min.Y) + 0.03f;
                    var feet = _lastFeet.Count >= 2 ? _lastFeet.ToList() : pts.Where(p => p.Y <= cutoff).ToList();
                    if (feet.Count == 0) feet = pts;
                    var withGround = new List<Vector3>(feet);
                    foreach (var p in feet) withGround.Add(new Vector3(p.X, GroundAt(p), p.Z));
                    var fp = Inflate(withGround, 0.06f);
                    var c = ReviewFraming.Bounds(fp).Center;
                    var e = ReviewFraming.SolveEye(fp, c, ReviewFraming.Dir(yawF + 1.25, 6), lens, 0.62, 0.92, 0.3);
                    return (e, c);
                });

                // 4. motion: real locomotion, clock unpaused, fixed side camera, 6 frames ~0.25 s apart
                g.Activity = GuardianActivity.Explore;
                g.ActivityDuration = 40.0;
                world.Clock.Paused = false;
                var frameTimes = new List<double>(256);
                var sw = Stopwatch.StartNew();
                long lastTicks = sw.ElapsedTicks;
                bool manual = false;
                async Task Tick()
                {
                    if (manual)
                    {
                        world.Step(VivariumWorld.Cadence.FaunaBehaviour);
                        session.Guardian._Process(1.0 / 30.0);
                    }
                    await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                    long now = sw.ElapsedTicks;
                    frameTimes.Add((now - lastTicks) * 1000.0 / Stopwatch.Frequency);
                    lastTicks = now;
                }
                async Task Until(double t) { while (sw.Elapsed.TotalSeconds < t) await Tick(); }

                Vector3 pa = default, pb = default;
                double speed = 0;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    double t0 = sw.Elapsed.TotalSeconds;
                    await Until(t0 + 0.8);
                    pa = session.Guardian.DisplayPosition;
                    double ta = sw.Elapsed.TotalSeconds;
                    await Until(ta + 0.4);
                    pb = session.Guardian.DisplayPosition;
                    speed = new Vector2(pb.X - pa.X, pb.Z - pa.Z).Length() / Math.Max(0.05, sw.Elapsed.TotalSeconds - ta);
                    if (speed >= 0.05) break;
                    manual = true; // scheduler is not advancing the guardian on its own: step it explicitly
                }
                var hv = new Vector2(pb.X - pa.X, pb.Z - pa.Z);
                Vector2 hdir = hv.Length() > 1e-4f ? hv.Normalized() : new Vector2((float)Math.Cos(walkHeading), (float)Math.Sin(walkHeading));
                var hdir3 = new Vector3(hdir.X, 0, hdir.Y);
                var ptsNow = BodyPoints(session.Guardian, Fallback());
                var union = new List<Vector3>(ptsNow.Count * 2);
                foreach (var p in ptsNow)
                {
                    union.Add(p + hdir3 * (float)(speed * 0.3));
                    union.Add(p + hdir3 * (float)(speed * 1.75));
                }
                var sideYaw = Math.Atan2(hdir.X, -hdir.Y);
                var mc = ReviewFraming.Bounds(union).Center;
                var meye = ReviewFraming.SolveEye(union, mc, ReviewFraming.Dir(sideYaw, 2), lens, 0.6, 0.94, 0.3);
                meye.Y = Math.Max(meye.Y, GroundAt(meye) + 0.05f);
                session.CameraRig.LookAtPoint(meye, mc);
                await Frames(main, 4);
                double tStart = sw.Elapsed.TotalSeconds + 0.1;
                var imgs = new List<(Image Img, double T, Vector3 Pos)>();
                for (int k = 0; k < 6; k++)
                {
                    await Until(tStart + 0.25 * k);
                    await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    imgs.Add((main.GetViewport().GetTexture().GetImage(), sw.Elapsed.TotalSeconds - tStart, session.Guardian.DisplayPosition));
                }
                var pf = ReviewFraming.Fill(union, meye, mc, lens);
                for (int k = 0; k < imgs.Count; k++)
                {
                    string file = $"{def.Archetype}_motion{suffix}_{k}.png";
                    imgs[k].Img.SavePng(Path.Combine(directory, file));
                    files.Add(file);
                    allImages.Add((def.Id, new Img(file, "motion", k, spotSlope, new double[] { meye.X, meye.Y, meye.Z }, new double[] { mc.X, mc.Y, mc.Z }, Math.Round(pf.H, 3), Math.Round(imgs[k].T, 3))));
                }
                double travelled = new Vector2(imgs[^1].Pos.X - imgs[0].Pos.X, imgs[^1].Pos.Z - imgs[0].Pos.Z).Length();

                SetVegetation(true);
                // 5. feeding / foraging pose (flat stage only)
                if (!slopeStage)
                {
                    g.Activity = GuardianActivity.Feed;
                    g.ActivityDuration = 8.0;
                    await Until(sw.Elapsed.TotalSeconds + 0.8);
                    double yawNow = g.Heading;
                    await Shot("feed", pts =>
                    {
                        var c = ReviewFraming.Bounds(pts).Center;
                        var e = ReviewFraming.SolveEye(pts, c, ReviewFraming.Dir(yawNow + Math.PI / 2 + 0.3, 8), lens, 0.65, 0.92, 0.3);
                        return (e, c);
                    });
                }
                world.Clock.Paused = true;

                frameTimes.Sort();
                double medianMs = frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] : 0;
                var legContact = session.Guardian.LegContact;
                var appContact = session.Guardian.AppendageContact;
                var skinContact = session.Guardian.SkinContact;
                GD.Print($"GUARDIAN_REVIEW_STAGE species={def.Id} suffix='{suffix}' slope_deg={spotSlope} speed_mps={speed:0.00} travelled_m={travelled:0.00} manual_step={manual}");
                return new
                {
                    slope_deg = spotSlope,
                    walk_speed_mps = Math.Round(speed, 3),
                    motion_travelled_m = Math.Round(travelled, 3),
                    manual_step = manual,
                    frame_ms_median = Math.Round(medianMs, 3),
                    fps_median = Math.Round(1000.0 / Math.Max(0.001, medianMs), 1),
                    leg_ik_present = legContact != null,
                    leg_skin_samples = legContact?.SkinFootSampleCount ?? 0,
                    leg_max_sole_penetration_m = Math.Round(legContact?.MaximumSolePenetration ?? 0f, 4),
                    leg_max_skin_penetration_m = Math.Round(legContact?.MaximumSkinFootPenetration ?? 0f, 4),
                    leg_max_stance_gap_m = Math.Round(legContact?.MaximumStanceFootGap ?? 0f, 4),
                    body_max_skin_penetration_m = Math.Round(legContact?.MaximumSkinBodyPenetration ?? 0f, 4),
                    appendage_present = appContact != null,
                    appendage_max_penetration_m = Math.Round(appContact?.MaximumGroundPenetration ?? 0f, 4),
                    appendage_max_skin_penetration_m = Math.Round(appContact?.MaximumSkinTailPenetration ?? 0f, 4),
                    skin_contact_valid = skinContact?.Valid ?? false,
                    skin_contact_diag = skinContact?.Diagnostic ?? "none",
                };
            }

            var flat = await RunStage(stageCenter, "");
            object? slopeStats = null;
            if (slopeMode) slopeStats = await RunStage(steepSpot, "_slope");

            report[def.Archetype] = new
            {
                species_id = def.Id,
                display_name = def.Name,
                locomotion_mode = def.Locomotion.ToString(),
                flat,
                slope = slopeStats,
                slope_spot_deg = slopeMode ? Math.Round(steepDeg, 1) : (double?)null,
                screenshots = files.ToArray()
            };
            GD.Print($"GUARDIAN_REVIEW_END species={def.Id} images={files.Count}");
        }

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "guardian_review.json"), JsonSerializer.Serialize(report, opts));
        File.WriteAllText(Path.Combine(directory, "guardians.json"), JsonSerializer.Serialize(new
        {
            slope_mode = slopeMode,
            image_count = allImages.Count,
            images = allImages.Select(a => new
            {
                a.Rec.file, species = a.Species, a.Rec.shot, frame = a.Rec.frame, a.Rec.slope_deg,
                a.Rec.camera_position, a.Rec.camera_target, a.Rec.fill_height, a.Rec.t_s
            }).ToList(),
        }, opts));
        GD.Print("GUARDIAN_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit(0);
    }

    /// <summary>Choose the walk direction (of 16) with the longest clear dry path; flat prefers low slope, steep prefers high slope.</summary>
    private static (double Heading, double Length) PickWalkDir(VivariumWorld world, Vec2 spot, bool steep)
    {
        double bestScore = double.NegativeInfinity, bestH = 0, bestLen = 3;
        for (int k = 0; k < 16; k++)
        {
            double a = k * Math.PI * 2 / 16;
            double len = 0, sum = 0; int n = 0;
            for (double s = 0.5; s <= 7.0; s += 0.5)
            {
                var p = new Vec2(spot.X + Math.Cos(a) * s, spot.Z + Math.Sin(a) * s);
                if (!world.Domain.ContainsDisc(p, 1.0)) break;
                if (world.Water.OpenWaterDepth(p) > 0.01) break;
                if (world.PilotTree != null && world.PilotTree.BlocksTrunk(p, 1.0)) break;
                if (world.Props.DistanceToFeature(p, "rock") < 0.5 || world.Props.DistanceToFeature(p, "log") < 0.5) break;
                len = s; sum += ReviewFraming.SlopeDeg(world, p); n++;
            }
            double mean = n > 0 ? sum / n : 0;
            double score = Math.Min(len, 7.0) * 2.0 + (steep ? mean : -mean * 0.5);
            if (score > bestScore) { bestScore = score; bestH = a; bestLen = Math.Max(len, 3.0); }
        }
        return (bestH, bestLen);
    }

    /// <summary>Steepest walkable (&lt;= ~53 deg) neighbourhood-averaged terrain, dry and clear of props, with room to walk.</summary>
    private static Vec2 FindSteepSpot(VivariumWorld world, out double slopeDeg)
    {
        Vec2 best = new Vec2(2.5, 2.5);
        double bestScore = double.NegativeInfinity;
        slopeDeg = 0;
        foreach (var c in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(c);
            if (!world.Domain.ContainsDisc(p, 2.5)) continue;
            if (world.Water.OpenWaterDepth(p) > 0.01) continue;
            if (world.PilotTree != null && world.PilotTree.BlocksTrunk(p, 1.4)) continue;
            if (world.Props.DistanceToFeature(p, "rock") < 0.9 || world.Props.DistanceToFeature(p, "log") < 0.9) continue;
            double sum = ReviewFraming.SlopeDeg(world, p);
            bool ok = world.Terrain.Normal(p).Y >= 0.6;
            foreach (var o in new[] { new Vec2(0.7, 0), new Vec2(-0.7, 0), new Vec2(0, 0.7), new Vec2(0, -0.7) })
            {
                var q = p + o;
                if (world.Terrain.Normal(q).Y < 0.6) ok = false;
                sum += ReviewFraming.SlopeDeg(world, q);
            }
            if (!ok) continue;
            double avg = sum / 5.0;
            double score = avg - Math.Abs(Math.Sqrt(p.X * p.X + p.Z * p.Z) - 3.0) * 0.1;
            if (score > bestScore) { bestScore = score; best = p; slopeDeg = ReviewFraming.SlopeDeg(world, p); }
        }
        return best;
    }

    private static Vec2 FindStageSpot(VivariumWorld world, bool preferSlope)
    {
        Vec2 best = new Vec2(2.5, 2.5);
        double bestScore = double.NegativeInfinity;
        foreach (var c in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(c);
            if (!world.Domain.ContainsDisc(p, 1.5)) continue;
            if (world.Water.OpenWaterDepth(p) > 0.01) continue;
            if (world.PilotTree != null && world.PilotTree.BlocksTrunk(p, 1.4)) continue;
            if (world.Props.DistanceToFeature(p, "rock") < 0.9 || world.Props.DistanceToFeature(p, "log") < 0.9) continue;

            double ny = world.Terrain.Normal(p).Y;
            double distCenter = Math.Sqrt(p.X * p.X + p.Z * p.Z);
            double score = preferSlope
                ? (1.0 - ny) * 20.0 - Math.Abs(distCenter - 4.0) * 0.5
                : ny * 10.0 - Math.Abs(distCenter - 3.2) * 0.5;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best;
    }
}
