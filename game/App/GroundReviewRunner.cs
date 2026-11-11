using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Deterministic ground close-up capture set for ground/soil/litter/rock/cut-face work.
/// Invoked via <c>--ground-review &lt;ABS_DIR&gt; [--seed N] [--ground-bare]</c>. Cameras are chosen from world data only.
/// Default captures both variants (<c>name.png</c> with everything, <c>name_bare.png</c> with flora/coverage/ambient/fauna/guardian hidden);
/// <c>--ground-bare</c> captures only the bare variant.
/// </summary>
public static class GroundReviewRunner
{
    private const double DayLight = 60; // noon
    private const int Width = 1600, Height = 900;

    private sealed record GShot(string Name, Vector3 Eye, Vector3 Target, string Note, Vector3? Focus = null);

    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        var win = main.GetWindow();
        win.Size = new Vector2I(Width, Height);
        session.EnvRig.BenchmarkLightSeconds = DayLight;
        world.Clock.Paused = true;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;
        for (int i = 0; i < 10; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);

        bool bareOnly = Main.UserArgs.Contains("--ground-bare");
        var sw = Stopwatch.StartNew();

        var timer = Stopwatch.StartNew();
        while (!session.Flora.PopulationReady && timer.Elapsed.TotalSeconds < 180)
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!session.Flora.PopulationReady)
            throw new InvalidOperationException("Flora population did not become ready within 180 s.");

        var shots = Plan(world);

        void SetVegetation(bool on)
        {
            session.Flora.Visible = on;
            session.Coverage.SetProcess(on);
            session.Coverage.Visible = on;
            session.AmbientGround.Visible = on;
            if ((object)session.Fauna is Node3D f) f.Visible = on;
            if ((object)session.Guardian is Node3D g) g.Visible = on;
        }

        var results = new List<object>();
        var variants = bareOnly ? new[] { true } : new[] { false, true };
        foreach (bool bare in variants)
        {
            SetVegetation(!bare);
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (var s in shots)
            {
                session.EnvRig.BenchmarkLightSeconds = DayLight;
                session.CameraRig.LookAtPoint(s.Eye, s.Target);
                for (int i = 0; i < 120; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = main.GetViewport().GetTexture().GetImage();
                string file = s.Name + (bare ? "_bare" : "") + ".png";
                image.SavePng(Path.Combine(directory, file));
                double black = BlackFraction(image);
                results.Add(new
                {
                    file,
                    shot = s.Name,
                    variant = bare ? "bare" : "full",
                    camera_position = new[] { R(s.Eye.X), R(s.Eye.Y), R(s.Eye.Z) },
                    camera_target = new[] { R(s.Target.X), R(s.Target.Y), R(s.Target.Z) },
                    near_black_fraction = Math.Round(black, 4),
                    viewport = new[] { image.GetWidth(), image.GetHeight() },
                    note = s.Note,
                    focus_px = s.Focus is { } fp && main.GetViewport().GetCamera3D() is { } cam ? new[] { R(cam.UnprojectPosition(fp).X), R(cam.UnprojectPosition(fp).Y) } : null,
                });
                GD.Print($"GROUND_REVIEW_SHOT {file} black={black:0.000}");
            }
        }
        SetVegetation(true);

        File.WriteAllText(Path.Combine(directory, "ground_review.json"), JsonSerializer.Serialize(new
        {
            seed = world.Seed,
            preset = world.Descriptor.Name,
            daylight_seconds = DayLight,
            bare_only = bareOnly,
            adapter = RenderingServer.GetVideoAdapterName(),
            capture_seconds = Math.Round(sw.Elapsed.TotalSeconds, 1),
            shots = results,
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("GROUND_REVIEW_OK " + directory);
        main.GetTree().Quit(0);
    }

    private static double R(double v) => Math.Round(v, 4);

    private static double BlackFraction(Image img)
    {
        int w = img.GetWidth(), h = img.GetHeight(), n = 0, dark = 0;
        for (int y = 0; y < h; y += 6)
            for (int x = 0; x < w; x += 6)
            {
                var c = img.GetPixel(x, y);
                n++;
                if (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B < 0.04f) dark++;
            }
        return n == 0 ? 0 : (double)dark / n;
    }

    private static List<GShot> Plan(VivariumWorld world)
    {
        float G(double x, double z) => (float)world.GroundHeight(new Vec2(x, z));
        float Gp(Vec2 p) => (float)world.GroundHeight(p);
        bool Dry(Vec2 p) => world.Water.OpenWaterDepth(p) < 0.01;
        bool Inside(Vec2 p, double r) => world.Domain.ContainsDisc(p, Math.Min(r, world.Domain.Apothem * 0.45));
        double Slope(Vec2 p) => ReviewFraming.SlopeDeg(world, p);

        // Plant buckets (1 m cells) for cheap proximity queries.
        var content = world.Content;
        var buckets = new Dictionary<(int, int), List<(Vec2 P, double H)>>();
        foreach (var f in world.Flora.Items)
        {
            var sp = content.FloraById(f.SpeciesId);
            if (sp == null) continue;
            var p = new Vec2(f.X, f.Z);
            var key = ((int)Math.Floor(p.X), (int)Math.Floor(p.Z));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new();
            list.Add((p, sp.Height));
        }
        int PlantCount(Vec2 p, double r, double minH)
        {
            int n = 0, span = (int)Math.Ceiling(r);
            int cx = (int)Math.Floor(p.X), cz = (int)Math.Floor(p.Z);
            for (int dx = -span; dx <= span; dx++)
                for (int dz = -span; dz <= span; dz++)
                    if (buckets.TryGetValue((cx + dx, cz + dz), out var l))
                        foreach (var (q, h) in l)
                            if (h >= minH && Vec2.Distance(p, q) <= r) n++;
            return n;
        }
        bool TrunkNear(Vec2 p, double m) => world.PilotTree != null && world.PilotTree.BlocksTrunk(p, m);
        bool LineClear(Vector3 eye, Vector3 target)
        {
            if (world.PilotTree == null) return true;
            for (int i = 1; i < 24; i++)
            {
                var q = eye.Lerp(target, i / 24f);
                if (world.PilotTree.BlocksTrunk(new Vec2(q.X, q.Z), 0.35)) return false;
            }
            return true;
        }
        // Ray from eye through target and 'beyond' metres further must not pass through the pilot tree trunk (a black wall in frame).
        bool RayClear(Vector3 eye, Vector3 target, double beyond = 8.0)
        {
            if (world.PilotTree == null) return true;
            var dir = target - eye; float len = dir.Length(); if (len < 1e-4f) return true;
            dir /= len;
            for (double t = 0.2; t < len + beyond; t += 0.25)
            {
                var q = eye + dir * (float)t;
                if (world.PilotTree.BlocksTrunk(new Vec2(q.X, q.Z), 1.0)) return false;
            }
            return true;
        }
        bool RockNear(Vec2 p, double d) => world.Props.DistanceToFeature(p, "rock") < d;
        bool EyeOk(Vec2 e, double clearR = 0.4, double minH = 0.3)
            => Inside(e, 0.4) && Dry(e) && !TrunkNear(e, 0.6) && PlantCount(e, clearR, minH) == 0;

        Vec2 fallback = new Vec2(1.5, 1.5);
        double fbD = double.PositiveInfinity;
        foreach (var c in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(c);
            double dd = p.X * p.X + p.Z * p.Z;
            if (dd < fbD && Inside(p, 3) && Dry(p)) { fbD = dd; fallback = p; }
        }

        // Pick eye yaw (of 12, ascending) with the clearest camera spot; returns first best.
        double PickYaw(Func<double, Vec2> eyeXZ, Func<double, bool> extra, Vector3 tgt, float eyeH)
        {
            double bestYaw = 0; int bestScore = int.MaxValue;
            for (int k = 0; k < 12; k++)
            {
                double yaw = k * Math.PI / 6;
                var e = eyeXZ(yaw);
                if (!Inside(e, 0.4) || !Dry(e) || TrunkNear(e, 1.5) || !extra(yaw)) continue;
                if (!RayClear(new Vector3((float)e.X, Gp(e) + eyeH, (float)e.Z), tgt)) continue;
                int score = PlantCount(e, 0.5, 0.15);
                if (score < bestScore) { bestScore = score; bestYaw = yaw; }
            }
            return bestYaw;
        }

        var shots = new List<GShot>();

        // ---- flat litter spots (close, mid) ----
        Vec2 flat1 = fallback, flat2 = fallback;
        {
            double best1 = double.NegativeInfinity;
            foreach (var c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c);
                if (!Inside(p, 1.5) || !Dry(p) || TrunkNear(p, 1.5) || RockNear(p, 1.2)) continue;
                double sl = Slope(p);
                if (sl > 5) continue;
                double score = -PlantCount(p, 1.0, 0.0) * 3.0 - Math.Abs(Math.Sqrt(p.X * p.X + p.Z * p.Z) - 3.2) * 0.3 - sl * 0.5;
                if (score > best1) { best1 = score; flat1 = p; }
            }
            double best2 = double.NegativeInfinity;
            foreach (var c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c);
                if (!Inside(p, 3.0) || !Dry(p) || TrunkNear(p, 2.0) || RockNear(p, 1.5)) continue;
                if (Vec2.Distance(p, flat1) < 3.0) continue;
                double sl = Slope(p);
                if (sl > 5) continue;
                double score = -PlantCount(p, 1.5, 0.0) * 2.0 - Math.Abs(Math.Sqrt(p.X * p.X + p.Z * p.Z) - 4.5) * 0.3 - sl * 0.5;
                if (score > best2) { best2 = score; flat2 = p; }
            }
            if (best2 == double.NegativeInfinity) flat2 = flat1;
        }
        {
            // close: 0.8 m above ground, 42 deg down
            double hAbove = 0.8, pitch = 48, off = hAbove / Math.Tan(pitch * Math.PI / 180);
            double yaw = PickYaw(a => new Vec2(flat1.X + Math.Cos(a) * off, flat1.Z + Math.Sin(a) * off), a => true, new Vector3((float)flat1.X, Gp(flat1), (float)flat1.Z), (float)hAbove);
            var e2 = new Vec2(flat1.X + Math.Cos(yaw) * off, flat1.Z + Math.Sin(yaw) * off);
            var target = new Vector3((float)flat1.X, Gp(flat1), (float)flat1.Z);
            var eye = new Vector3((float)e2.X, Math.Max(Gp(flat1) + (float)hAbove, Gp(e2) + 0.5f), (float)e2.Z);
            eye = new Vector3((float)e2.X, eye.Y, (float)e2.Z);
            shots.Add(new GShot("litter_flat_close", eye, target, $"flat cell ({flat1.X:0.00},{flat1.Z:0.00}) slope={Slope(flat1):0.0} eye {eye.Y - target.Y:0.00} m above, {pitch} deg down"));
        }
        {
            double dist = 3.4, pitch = 45;
            double off = dist * Math.Cos(pitch * Math.PI / 180);
            double yaw = PickYaw(a => new Vec2(flat2.X + Math.Cos(a) * off, flat2.Z + Math.Sin(a) * off), a => true, new Vector3((float)flat2.X, Gp(flat2), (float)flat2.Z), (float)(dist * Math.Sin(pitch * Math.PI / 180)));
            var target = new Vector3((float)flat2.X, Gp(flat2), (float)flat2.Z);
            var eye = target + ReviewFraming.Dir(yaw, pitch) * (float)dist;
            shots.Add(new GShot("litter_flat_mid", eye, target, $"flat cell ({flat2.X:0.00},{flat2.Z:0.00}) slope={Slope(flat2):0.0} dist {dist} m, {pitch} deg down"));
        }

        // ---- meadow close: open ground (no tall plants within 2.5 m), most low plants within 1.2 m ----
        {
            Vec2 best = fallback; double bestScore = double.NegativeInfinity;
            foreach (var c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c);
                if (!Inside(p, 1.5) || !Dry(p) || TrunkNear(p, 1.5) || RockNear(p, 1.0)) continue;
                if (Vec2.Distance(p, flat1) < 2.0 || Slope(p) > 12) continue;
                double score = Math.Min(PlantCount(p, 1.2, 0.0), 10) - PlantCount(p, 2.5, 1.5) * 2.0 - Slope(p) * 0.1;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            double hAbove = 0.9, pitch = 48, off = hAbove / Math.Tan(pitch * Math.PI / 180);
            double yaw = PickYaw(a => new Vec2(best.X + Math.Cos(a) * off, best.Z + Math.Sin(a) * off), a => true, new Vector3((float)best.X, Gp(best), (float)best.Z), (float)hAbove);
            var e2 = new Vec2(best.X + Math.Cos(yaw) * off, best.Z + Math.Sin(yaw) * off);
            var target = new Vector3((float)best.X, Gp(best), (float)best.Z);
            var eye = new Vector3((float)e2.X, Math.Max(Gp(best) + (float)hAbove, Gp(e2) + 0.5f), (float)e2.Z);
            shots.Add(new GShot("meadow_close", eye, target, $"open cell ({best.X:0.00},{best.Z:0.00}) plants<1.2m={PlantCount(best, 1.2, 0):0} score={bestScore:0.0}"));
        }

        // ---- slope close: steepest walkable neighbourhood-averaged slope ----
        {
            Vec2 best = fallback; double bestScore = double.NegativeInfinity, bestDeg = 0;
            foreach (var c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c);
                if (!Inside(p, 2.0) || !Dry(p) || TrunkNear(p, 1.4) || RockNear(p, 0.9)) continue;
                double sum = Slope(p); bool ok = world.Terrain.Normal(p).Y >= 0.6;
                foreach (var o in new[] { new Vec2(0.7, 0), new Vec2(-0.7, 0), new Vec2(0, 0.7), new Vec2(0, -0.7) })
                {
                    var q = p + o;
                    if (world.Terrain.Normal(q).Y < 0.6) ok = false;
                    sum += Slope(q);
                }
                if (!ok) continue;
                double avg = sum / 5.0;
                double score = avg - PlantCount(p, 1.0, 0.3) * 2.0;
                if (score > bestScore) { bestScore = score; best = p; bestDeg = avg; }
            }
            var n3 = world.Terrain.Normal(best);
            double down = Math.Atan2(n3.Z, n3.X); // horizontal normal component points downhill
            // camera sits downhill of the target looking up-slope
            double pitch = 35, dist = 1.1;
            double yaw = PickYaw(a => new Vec2(best.X + Math.Cos(a) * dist * Math.Cos(pitch * Math.PI / 180), best.Z + Math.Sin(a) * dist * Math.Cos(pitch * Math.PI / 180)),
                a => Math.Cos(a - down) > 0.5, new Vector3((float)best.X, Gp(best), (float)best.Z), (float)(dist * Math.Sin(pitch * Math.PI / 180)));
            var target = new Vector3((float)best.X, Gp(best), (float)best.Z);
            var eye = target + ReviewFraming.Dir(yaw, pitch) * (float)dist;
            var exz = new Vec2(eye.X, eye.Z);
            eye.Y = Math.Max(eye.Y, Gp(exz) + 0.45f);
            shots.Add(new GShot("slope_close", eye, target, $"steepest walkable ({best.X:0.00},{best.Z:0.00}) avg slope={bestDeg:0.0} deg"));
        }

        // ---- rocks ----
        {
            Rock? biggest = null; double bigScore = double.NegativeInfinity;
            Rock? clusterRock = null; double clusterScore = double.NegativeInfinity;
            foreach (var r in world.Props.Rocks)
            {
                var rp = new Vec2(r.X, r.Z);
                if (!Inside(rp, 1.0) || TrunkNear(rp, 3.0)) continue;
                double size = Math.Max(r.SizeX, Math.Max(r.SizeY, r.SizeZ));
                double dryPen = Dry(rp) ? 0 : 3;
                double s1 = size - dryPen;
                if (s1 > bigScore) { bigScore = s1; biggest = r; }
                int nb = world.Props.Rocks.Count(o => Vec2.Distance(new Vec2(o.X, o.Z), rp) <= 3.0);
                double s2 = nb * 2 + size - dryPen;
                if (s2 > clusterScore) { clusterScore = s2; clusterRock = r; }
            }
            if (biggest is { } rk)
            {
                double rr = (rk.SizeX + rk.SizeZ) * 0.5, dist = Math.Max(0.4, rr * 1.4);
                double yaw = PickYaw(a => new Vec2(rk.X + Math.Cos(a) * (rr * 0.8 + dist), rk.Z + Math.Sin(a) * (rr * 0.8 + dist)),
                    a => !RockNear(new Vec2(rk.X + Math.Cos(a) * (rr * 0.8 + dist), rk.Z + Math.Sin(a) * (rr * 0.8 + dist)), 0.35),
                    new Vector3((float)(rk.X + 0), Gp(new Vec2(rk.X, rk.Z)) + 0.12f, (float)rk.Z), 0.15f);
                var contact = new Vec2(rk.X + Math.Cos(yaw) * rr * 0.8, rk.Z + Math.Sin(yaw) * rr * 0.8);
                var target = new Vector3((float)contact.X, Gp(contact) + 0.12f, (float)contact.Z);
                var eye = target + ReviewFraming.Dir(yaw, 18) * (float)dist;
                eye.Y = Math.Max(eye.Y, G(eye.X, eye.Z) + 0.15f);
                shots.Add(new GShot("rock_contact", eye, target, $"largest rock ({rk.X:0.00},{rk.Z:0.00}) half-extents=({rk.SizeX:0.00},{rk.SizeY:0.00},{rk.SizeZ:0.00}) base contact, low angle", new Vector3((float)rk.X, (float)(rk.Y + rk.SizeY), (float)rk.Z)));
            }
            else
            {
                var t = new Vector3((float)fallback.X, Gp(fallback), (float)fallback.Z);
                shots.Add(new GShot("rock_contact", t + new Vector3(1.0f, 0.4f, 0), t, "fallback: no rocks"));
            }
            if (clusterRock is { } cr)
            {
                var cp = new Vec2(cr.X, cr.Z);
                var near = world.Props.Rocks.Where(o => Vec2.Distance(new Vec2(o.X, o.Z), cp) <= 3.0).ToList();
                double cx = near.Average(o => o.X), cz = near.Average(o => o.Z);
                double ext = near.Max(o => Vec2.Distance(new Vec2(o.X, o.Z), new Vec2(cx, cz)) + Math.Max(o.SizeX, o.SizeZ));
                double dist = Math.Clamp(ext * 1.8 + 1.2, 2.5, 3.8), pitch = 24;
                var c2 = new Vec2(cx, cz);
                var target = new Vector3((float)cx, Gp(c2) + 0.2f, (float)cz);
                double yaw = PickYaw(a => new Vec2(cx + Math.Cos(a) * dist * Math.Cos(pitch * Math.PI / 180), cz + Math.Sin(a) * dist * Math.Cos(pitch * Math.PI / 180)),
                    a => LineClear(target + ReviewFraming.Dir(a, pitch) * (float)dist, target), target, (float)(dist * Math.Sin(pitch * Math.PI / 180)));
                var eye = target + ReviewFraming.Dir(yaw, pitch) * (float)dist;
                eye.Y = Math.Max(eye.Y, G(eye.X, eye.Z) + 0.6f);
                shots.Add(new GShot("rock_cluster_mid", eye, target, $"cluster centre ({cx:0.00},{cz:0.00}) rocks={near.Count} dist={dist:0.0}", new Vector3((float)cr.X, (float)(cr.Y + cr.SizeY), (float)cr.Z)));
            }
            else
            {
                var t = new Vector3((float)fallback.X, Gp(fallback), (float)fallback.Z);
                shots.Add(new GShot("rock_cluster_mid", t + new Vector3(3, 1.5f, 0), t, "fallback: no rocks"));
            }
        }

        // ---- cut face: edge with highest ground at mid-edge ----
        {
            var dom = world.Domain;
            double bottom = world.Terrain.Bottom;
            int bestK = 0; double bestG = double.NegativeInfinity;
            for (int pass = 0; pass < 2 && bestG == double.NegativeInfinity; pass++)
                for (int k = 0; k < 6; k++)
                {
                    var m = dom.EdgeNormals[k] * (dom.Apothem - 0.05);
                    double g = Gp(m);
                    var eo = new Vector3((float)(dom.EdgeNormals[k].X * (dom.Apothem + 1.5)), (float)(g - 0.7), (float)(dom.EdgeNormals[k].Z * (dom.Apothem + 1.5)));
                    var wo = new Vector3((float)(dom.EdgeNormals[k].X * dom.Apothem), (float)(g - 0.7), (float)(dom.EdgeNormals[k].Z * dom.Apothem));
                    if (pass == 0 && !RayClear(eo, wo, 10.0)) continue;
                    if (g > bestG) { bestG = g; bestK = k; }
                }
            var n = dom.EdgeNormals[bestK];
            double gTop = bestG;
            double mid = (gTop + bottom) * 0.5;
            float ty = (float)Math.Max(gTop - 0.7, mid);
            var wall = new Vector3((float)(n.X * dom.Apothem), ty, (float)(n.Z * dom.Apothem));
            var eyeS = new Vector3((float)(n.X * (dom.Apothem + 1.5)), ty, (float)(n.Z * (dom.Apothem + 1.5)));
            shots.Add(new GShot("cutface_side", eyeS, wall, $"edge {bestK} mid, surface y={gTop:0.00} bottom y={bottom:0.00}, horizontal at 1.5 m"));

            int bestV = 0; double bestVG = double.NegativeInfinity;
            for (int pass = 0; pass < 2 && bestVG == double.NegativeInfinity; pass++)
                for (int k = 0; k < 6; k++)
                {
                    var v = dom.Vertices[k];
                    double g = Gp(v * 0.97);
                    var uu = v * (1.0 / Math.Sqrt(v.X * v.X + v.Z * v.Z));
                    var eo = new Vector3((float)(v.X + uu.X * 1.8), (float)(g - 0.7), (float)(v.Z + uu.Z * 1.8));
                    var wo = new Vector3((float)v.X, (float)(g - 0.7), (float)v.Z);
                    if (pass == 0 && !RayClear(eo, wo, 10.0)) continue;
                    if (g > bestVG) { bestVG = g; bestV = k; }
                }
            var vert = dom.Vertices[bestV];
            var u = vert * (1.0 / Math.Sqrt(vert.X * vert.X + vert.Z * vert.Z));
            float cty = (float)Math.Max(bestVG - 0.7, (bestVG + bottom) * 0.5);
            var ct = new Vector3((float)vert.X, cty, (float)vert.Z);
            var ce = new Vector3((float)(vert.X + u.X * 1.8), cty, (float)(vert.Z + u.Z * 1.8));
            shots.Add(new GShot("cutface_corner", ce, ct, $"vertex {bestV}, surface y={bestVG:0.00}, horizontal diagonal at 1.8 m"));

            // ---- far ground: grazing view across the island from the first usable edge side ----
            bool placed = false;
            for (int k = 0; k < 6 && !placed; k++)
            {
                var nk = dom.EdgeNormals[k];
                var e = nk * (dom.Apothem - 0.9);
                var t = nk * (-dom.Apothem * 0.6);
                if (!EyeOk(e, 0.4, 0.3)) continue;
                var eye = new Vector3((float)e.X, Gp(e) + 0.35f, (float)e.Z);
                var tgt = new Vector3((float)t.X, Gp(t) + 0.3f, (float)t.Z);
                if (!RayClear(eye, tgt, 2.0)) continue;
                shots.Add(new GShot("far_ground", eye, tgt, $"edge {k} side, eye 0.35 m above ground looking across island"));
                placed = true;
            }
            if (!placed)
            {
                var e = dom.EdgeNormals[0] * (dom.Apothem - 0.9);
                var t = dom.EdgeNormals[0] * (-dom.Apothem * 0.6);
                shots.Add(new GShot("far_ground", new Vector3((float)e.X, Gp(e) + 0.35f, (float)e.Z), new Vector3((float)t.X, Gp(t) + 0.3f, (float)t.Z), "fallback edge 0"));
            }
        }

        return shots;
    }
}
