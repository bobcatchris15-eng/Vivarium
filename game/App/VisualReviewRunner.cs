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

/// <summary>Opt-in, isolated runtime capture for repeatable Forward+ visual/performance reviews.</summary>
public static class VisualReviewRunner
{
    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = 60;
        world.Clock.Paused = true;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        // The tools layer re-shows the green cursor torus on its next _Process tick, which
        // would photobomb every review frame. Stop that node and hide the overlay outright.
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;
        var output = new Dictionary<string, object>();
        int floraSettleFrames = 0;
        var floraSettleTimer = Stopwatch.StartNew();
        double lastProgressSeconds = 0;
        // The full seeded species roster can publish more slowly than one minute on lower-end GPUs.
        // This bounded wait guarantees captures compare complete flora layers instead of startup progress.
        while (!session.Flora.PopulationReady && floraSettleFrames < 240000 && floraSettleTimer.Elapsed.TotalSeconds < 180)
        {
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            floraSettleFrames++;
            if (floraSettleTimer.Elapsed.TotalSeconds >= lastProgressSeconds + 10)
            {
                lastProgressSeconds = floraSettleTimer.Elapsed.TotalSeconds;
                GD.Print($"VISUAL_REVIEW_FLORA_PROGRESS loaded={session.Flora.LoadedSpeciesCount}/{world.Content.Flora.Count} elapsed_s={lastProgressSeconds:0}");
            }
        }
        if (!session.Flora.PopulationReady)
            throw new InvalidOperationException($"Flora population did not become ready within {floraSettleTimer.Elapsed.TotalSeconds:0.0}s; published {session.Flora.LoadedSpeciesCount}/{world.Content.Flora.Count} species.");
        var shots = SceneBuilder.Build(main, world);
        foreach (var shot in shots)
        {
            session.EnvRig.BenchmarkLightSeconds = shot.Light;
            session.CameraRig.LookAtPoint(shot.Eye, shot.Target);
            var frames = new List<double>(240);
            var timer = Stopwatch.StartNew();
            long last = timer.ElapsedTicks;
            // Let the camera-local detail stream settle before warmup and measurement.
            for (int i = 0; i < 180; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            int settleFrames = 0;
            while (!session.Flora.PopulationReady && settleFrames < 1200)
            {
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                settleFrames++;
            }
            if (!session.Flora.PopulationReady)
                throw new InvalidOperationException($"Flora population did not become ready for {shot.Name} within 1200 frames.");
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            last = timer.ElapsedTicks;
            for (int i = 0; i < 240; i++)
            {
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                long now = timer.ElapsedTicks;
                frames.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                last = now;
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = main.GetViewport().GetTexture().GetImage();
            var eyeUsed = shot.Eye; double blackFrac = BlackFraction(image); int repicks = 0;
            while (blackFrac > 0.15 && repicks < 4)
            {
                repicks++;
                var cand = shot.Target + (eyeUsed - shot.Target) * 1.3f;
                session.CameraRig.LookAtPoint(cand, shot.Target);
                for (int i = 0; i < 40; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var img2 = main.GetViewport().GetTexture().GetImage();
                double f2 = BlackFraction(img2);
                eyeUsed = cand;
                if (f2 < blackFrac) { image = img2; blackFrac = f2; }
            }
            image.SavePng(Path.Combine(directory, shot.Name + ".png"));
            frames.Sort();
            output[shot.Name] = new
            {
                daylight_seconds = shot.Light,
                selection = shot.Note,
                subject_fill = shot.Fill,
                warmup_frames = 180,
                flora_settle_frames = settleFrames,
                measured_frames = frames.Count,
                frame_ms_median = Percentile(frames, .50),
                frame_ms_p95 = Percentile(frames, .95),
                frame_ms_p99 = Percentile(frames, .99),
                fps_median = 1000.0 / Math.Max(.001, Percentile(frames, .50)),
                draw_calls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
                primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
                objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame),
                camera_position = new[] { eyeUsed.X, eyeUsed.Y, eyeUsed.Z },
                near_black_fraction = Math.Round(blackFrac, 4),
                camera_repicks = repicks,
                planned_camera_position = new[] { shot.Eye.X, shot.Eye.Y, shot.Eye.Z },
                camera_target = new[] { shot.Target.X, shot.Target.Y, shot.Target.Z },
                viewport = new[] { image.GetWidth(), image.GetHeight() },
                screenshot = shot.Name + ".png"
            };
        }
        File.WriteAllText(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(new
        {
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            adapter = RenderingServer.GetVideoAdapterName(),
            seed = world.Seed,
            preset = world.Descriptor.Name,
            quality = session.Settings.Quality,
            vsync = "disabled",
            daylight_seconds_default = 60,
            flora_population_ready = session.Flora.PopulationReady,
            flora_settle_frames = floraSettleFrames,
            species_counts = world.Flora.Items.GroupBy(p => p.SpeciesId).ToDictionary(g => g.Key, g => g.Count()),
            timing_scope = "end-to-end frame intervals including CPU scheduling and GPU presentation; not isolated CPU or GPU time",
            residual_animation = "Simulation clock paused; shader TIME animation may continue because this Godot build exposes no global shader time freeze.",
            settings = new { dynamic_shadows = session.Settings.DynamicShadows, global_illumination = session.Settings.GlobalIllumination, ambient_occlusion = session.Settings.AmbientOcclusion, volumetric_mist = session.Settings.VolumetricMist, distance_blur = session.Settings.DistanceBlur, render_scale = session.Settings.RenderScale },
            flora = world.Flora.Count,
            fauna = world.Fauna.Count,
            captures = output
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("VISUAL_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit();
    }

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

    private static double Percentile(IReadOnlyList<double> sorted, double q)
        => Math.Round(sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * q) - 1, 0, sorted.Count - 1)], 3);
}

/// <summary>A fixed review camera. Scenes other than foliage_terrain are chosen from world data, never hardcoded coordinates.</summary>
public sealed record ReviewScene(string Name, Vector3 Eye, Vector3 Target, double Light, string Note, double Fill = 0);

/// <summary>Chooses the visual-review scenes from the generated world (terrain, water, flora, rocks).</summary>
public static class SceneBuilder
{
    private const double DayLight = 60;
    private const double DuskLight = 780; // middle of the 660-900 s dusk phase: low sun

    public static List<ReviewScene> Build(Main main, VivariumWorld world)
    {
        var lens = ReviewFraming.Lens(main);
        double halfVfovDeg = Math.Atan(lens.TanV) * 180.0 / Math.PI;
        float G(double x, double z) => (float)world.GroundHeight(new Vec2(x, z));
        float Gp(Vec2 p) => (float)world.GroundHeight(p);
        bool Dry(Vec2 p) => world.Water.OpenWaterDepth(p) < 0.01;
        // Margins are clamped to the island size (the review islands can be only a few metres across).
        bool Inside(Vec2 p, double r) => world.Domain.ContainsDisc(p, Math.Min(r, world.Domain.Apothem * 0.45));

        var content = world.Content;
        var trees = new List<(Vec2 P, double H)>();
        var lows = new List<Vec2>();
        var all = new List<(Vec2 P, string Id)>();
        foreach (var f in world.Flora.Items)
        {
            var sp = content.FloraById(f.SpeciesId);
            if (sp == null) continue;
            var p = new Vec2(f.X, f.Z);
            all.Add((p, f.SpeciesId));
            if (sp.Woody != null && sp.Height >= 1.5) trees.Add((p, sp.Height));
            else if (sp.Height >= 0.25 && sp.Height <= 1.8 && sp.Woody == null) lows.Add(p);
        }
        double NearestTree(Vec2 p) { double b = double.PositiveInfinity; foreach (var t in trees) b = Math.Min(b, Vec2.Distance(p, t.P)); return b; }
        int CountWithin(List<Vec2> list, Vec2 p, double r) { int n = 0; foreach (var q in list) if (Vec2.Distance(p, q) <= r) n++; return n; }
        List<T> Sample<T>(List<T> src, int max) { if (src.Count <= max) return src; var res = new List<T>(max); double step = (double)src.Count / max; for (int i = 0; i < max; i++) res.Add(src[(int)(i * step)]); return res; }
        double InsidePath(Vec2 p, double yaw, double limit)
        {
            double s = 0;
            while (s < limit && Inside(new Vec2(p.X + Math.Cos(yaw) * (s + 0.5), p.Z + Math.Sin(yaw) * (s + 0.5)), 0.3)) s += 0.5;
            return s;
        }
        Vec2 Fallback = new Vec2(1.5, 1.5);
        double fbD = double.PositiveInfinity;
        foreach (var c in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(c);
            double dd = p.X * p.X + p.Z * p.Z;
            if (dd < fbD && Inside(p, 3) && Dry(p)) { fbD = dd; Fallback = p; }
        }
        // Plants (position, effective radius, height) that could contain or block a camera.
        var plants = new List<(Vec2 P, double R, double H)>();
        foreach (var f in world.Flora.Items)
        {
            var sp = content.FloraById(f.SpeciesId);
            if (sp == null) continue;
            plants.Add((new Vec2(f.X, f.Z), Math.Max(0.12, f.Radius(sp) * 0.8), sp.Height));
        }
        // True when a camera at eyeAbove metres over the ground at p would sit inside or touch a plant taller than ~60% of that height.
        bool Blocked(Vec2 p, double eyeAbove, double margin = 0.5)
        {
            foreach (var (q, r, h) in plants)
            {
                double dx = q.X - p.X, dz = q.Z - p.Z;
                if (dx * dx + dz * dz > (r + margin) * (r + margin)) continue;
                if (h > eyeAbove * 0.85) return true;
            }
            return world.PilotTree != null && world.PilotTree.BlocksTrunk(p, margin + 0.3);
        }
        // The pilot tree trunk is a huge black wall; reject views whose line of sight passes through it.
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
        // True when every frame-edge ray (corners + edge midpoints) lands on terrain inside the island: no sky or void at the borders.
        int EdgeRaysHitIsland(Vector3 eye, Vector3 target)
        {
            int okRays = 0;
            var f = (target - eye).Normalized();
            var right = f.Cross(Vector3.Up).Normalized();
            var up = right.Cross(f);
            for (int sx = -1; sx <= 1; sx++)
                for (int sy = -1; sy <= 1; sy++)
                {
                    if (sx == 0 && sy == 0) continue;
                    var d = (f + right * (float)(sx * lens.TanV * lens.Aspect) + up * (float)(sy * lens.TanV)).Normalized();
                    bool hit = false; 
                    for (double t = 0.2; t < 40; t += 0.1)
                    {
                        var q = eye + d * (float)t;
                        var xz = new Vec2(q.X, q.Z);
                        if (!Inside(xz, 0.15)) break;
                        if (q.Y <= G(q.X, q.Z)) { hit = true; break; }
                    }
                    if (hit) okRays++;
                }
            return okRays;
        }
        int TallNear(Vec2 p, double r, double minH) { int n = 0; foreach (var (q, _, h) in plants) if (h >= minH && Vec2.Distance(p, q) <= r) n++; return n; }

        var scenes = new List<ReviewScene>();

        // overview: whole island, hex outline fills >= 70% of frame width
        {
            // The hex outline lies at the heights of the actual terrain surface (sampled inside the domain), not the skirt below it.
            var pts = new List<Vector3>();
            var cellsAll = world.Grid.DomainCells;
            float sumY = 0; int cnt = 0;
            for (int i = 0; i < cellsAll.Length; i += 6)
            {
                var p = world.Grid.CellCenter(cellsAll[i]);
                float y = (float)world.Terrain.Height(p);
                pts.Add(new Vector3((float)p.X, y, (float)p.Z));
                sumY += y; cnt++;
            }
            float avg = cnt > 0 ? sumY / cnt : 0;
            foreach (var v in world.Domain.Vertices)
            {
                var inner = new Vec2(v.X * 0.97, v.Z * 0.97);
                pts.Add(new Vector3((float)v.X, (float)world.Terrain.Height(inner), (float)v.Z));
            }
            var target = new Vector3(0, avg, 0);
            var eye = ReviewFraming.SolveEye(pts, target, ReviewFraming.Dir(Math.PI / 2, 9), lens, 0.97, 0.80, 4.0);
            var fill = ReviewFraming.Fill(pts, eye, target, lens);
            scenes.Add(new ReviewScene("overview", eye, target, DayLight, $"hex outline fit; domain_radius={world.Domain.Radius:0.00} apothem={world.Domain.Apothem:0.00} diameter={world.Descriptor.Diameter:0.0}; width_fill={fill.W:0.00} height_fill={fill.H:0.00} y_range={pts.Min(q => q.Y):0.0}..{pts.Max(q => q.Y):0.0}", Math.Round(fill.W, 3)));
        }

        // foliage_terrain: unchanged fixed close view
        scenes.Add(new ReviewScene("foliage_terrain", new Vector3(-2, 1.2f, 3.8f), new Vector3(-1, .4f, 1.8f), DayLight, "fixed"));

        // grazing_transition: most species-diverse patch, looked at steeply downward so terrain and vegetation fill the frame.
        // Every frame-edge ray is verified to land on island terrain (no sky/void border).
        {
            var cands = new List<(Vec2 P, double Score)>();
            foreach (var (p, _) in Sample(all, 500))
            {
                if (!Inside(p, 3.5) || !Dry(p)) continue;
                var ids = new HashSet<string>();
                foreach (var (q, id) in all) if (Vec2.Distance(p, q) <= 3.0) ids.Add(id);
                cands.Add((p, ids.Count - TallNear(p, 2.0, 1.2) * 2.0));
            }
            Vector3 bEye = default, bTarget = default; double bScore = double.NegativeInfinity; double bPitch = 0; bool verified = false; int nEye = 0, nLine = 0, bRays = 0;
            foreach (var (p, sc) in cands.OrderByDescending(c => c.Score).Take(60))
            {
                foreach (double pitchDeg in new[] { halfVfovDeg + 30.0, halfVfovDeg + 42.0, 78.0 })
                {
                    double dist = 1.4;
                    double eyeUp = dist * Math.Sin(pitchDeg * Math.PI / 180);
                    for (int k = 0; k < 16; k++)
                    {
                        double yaw = k * Math.PI / 8;
                        var tgt = new Vector3((float)p.X, Gp(p) + 0.1f, (float)p.Z);
                        var eye = tgt + ReviewFraming.Dir(yaw, pitchDeg) * (float)dist;
                        var ep = new Vec2(eye.X, eye.Z);
                        if (!Inside(ep, 0.5)) continue;
                        eye.Y = Math.Max(eye.Y, G(eye.X, eye.Z) + 1.0f);
                        nEye++;
                        if (!LineClear(eye, tgt)) continue;
                        nLine++;
                        int rays = EdgeRaysHitIsland(eye, tgt);
                        double s2 = rays * 10 + sc - TallNear(ep, 2.5, 1.2) * 0.5;
                        if (s2 > bScore) { bScore = s2; bEye = eye; bTarget = tgt; bPitch = pitchDeg; verified = true; bRays = rays; }
                    }
                }
            }
            if (!verified)
            {
                var p = cands.Count > 0 ? cands.OrderByDescending(c => c.Score).First().P : Fallback;
                bPitch = 75;
                bTarget = new Vector3((float)p.X, Gp(p) + 0.1f, (float)p.Z);
                bEye = bTarget + ReviewFraming.Dir(Math.PI / 2, bPitch) * 3.2f;
            }
            scenes.Add(new ReviewScene("grazing_transition", bEye, bTarget, DayLight, $"species-diverse dry patch; pitch={bPitch:0}deg; edge_rays_on_terrain={bRays}/8; cands={cands.Count} eye_ok={nEye} line_ok={nLine}"));
        }

        // shoreline: dry ground meeting open water, eye height looking across the water edge
        {
            Vec2? bestShore = null; Vec2 bestDir = default; double bestScore = double.NegativeInfinity;
            var cells = world.Grid.DomainCells;
            for (int i = 0; i < cells.Length; i += 3)
            {
                var p = world.Grid.CellCenter(cells[i]);
                if (!Inside(p, 1.5) || !Dry(p)) continue;
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4;
                    var dir = new Vec2(Math.Cos(a), Math.Sin(a));
                    Vec2? shore = null;
                    for (double s = 0.25; s <= 3.0; s += 0.25)
                    {
                        var q = p + dir * s;
                        if (world.Water.OpenWaterDepth(q) > 0.02) { shore = q; break; }
                    }
                    if (shore == null) continue;
                    var inland = shore.Value - dir * 2.2;
                    if (!Inside(inland, 0.4) || !Dry(inland) || Blocked(inland, 0.9, 0.35)) continue;
                    if (!LineClear(new Vector3((float)inland.X, Gp(inland) + 0.9f, (float)inland.Z), new Vector3((float)(shore.Value.X + dir.X * 1.5), Gp(inland), (float)(shore.Value.Z + dir.Z * 1.5)))) continue;
                    int water = 0;
                    for (double s = 0.5; s <= 4.0; s += 0.5)
                    {
                        var q = shore.Value + dir * s;
                        if (world.Domain.Contains(q) && world.Water.OpenWaterDepth(q) > 0.02) water++;
                    }
                    double score = water - NearestTree(inland) * -0.0 - Math.Abs(world.Terrain.Normal(inland).Y - 1) * 5;
                    if (score > bestScore) { bestScore = score; bestShore = shore; bestDir = dir; }
                }
            }
            if (bestShore is { } sh)
            {
                var inland = sh - bestDir * 2.2;
                var eye = new Vector3((float)inland.X, Gp(inland) + 0.9f, (float)inland.Z);
                var tp = sh + bestDir * 1.5;
                var target = new Vector3((float)tp.X, (float)Math.Max(world.Water.SurfaceAt(tp), world.GroundHeight(tp)), (float)tp.Z);
                scenes.Add(new ReviewScene("shoreline", eye, target, DayLight, $"shore point ({sh.X:0.00},{sh.Z:0.00}); water samples ahead={bestScore:0.0}"));
            }
            else
            {
                var target = new Vector3((float)Fallback.X, Gp(Fallback), (float)Fallback.Z);
                scenes.Add(new ReviewScene("shoreline", target + new Vector3(2, 1, 2), target, DayLight, "fallback: no shoreline found"));
            }
        }

        // understory_eye_height: densest low-plant stand, camera at eye height looking along the densest direction
        {
            Vec2 best = Fallback; double bestScore = double.NegativeInfinity;
            var cellsU = world.Grid.DomainCells;
            for (int pass = 0; pass < 3 && double.IsNegativeInfinity(bestScore); pass++)
            for (int i = 0; i < cellsU.Length; i += 2)
            {
                var p = world.Grid.CellCenter(cellsU[i]);
                if (!Inside(p, 3.5) || !Dry(p) || ReviewFraming.SlopeDeg(world, p) > 30) continue;
                if (pass == 0 && (Blocked(p, 1.0, 0.3) || Blocked(p, 3.0, 1.0))) continue;
                if (pass == 1 && Blocked(p, 1.0, 0.05)) continue;
                double score = CountWithin(lows, p, 2.5);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            double bestCone = -1, bestYaw = 0;
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                if (InsidePath(best, a, 4.0) < 3.5) continue;
                if (!LineClear(new Vector3((float)best.X, Gp(best) + 1.0f, (float)best.Z), new Vector3((float)(best.X + Math.Cos(a) * 3.0), Gp(best) + 0.6f, (float)(best.Z + Math.Sin(a) * 3.0)))) continue;
                int n = 0;
                foreach (var q in lows)
                {
                    var v = q - best; double l = v.Length;
                    if (l < 1.0 || l > 5.0) continue;
                    double ang = Math.Abs(Math.IEEERemainder(Math.Atan2(v.Z, v.X) - a, Math.PI * 2));
                    if (ang < 0.5) n++;
                }
                if (n > bestCone) { bestCone = n; bestYaw = a; }
            }
            var eye = new Vector3((float)best.X, Gp(best) + 1.0f, (float)best.Z);
            var tp = new Vec2(best.X + Math.Cos(bestYaw) * 3.0, best.Z + Math.Sin(bestYaw) * 3.0);
            var target = new Vector3((float)tp.X, Gp(tp) + 0.6f, (float)tp.Z);
            scenes.Add(new ReviewScene("understory_eye_height", eye, target, DayLight, $"low plants within 2.5 m={bestScore:0}; in view cone={bestCone:0}"));
        }

        // woodland_trunks: tightest tree stand, looking at trunk bases
        {
            Vec2 bestT = Fallback; double bestScore = double.NegativeInfinity; var nearby = new List<Vec2>();
            foreach (var (p, _) in Sample(trees, 300))
            {
                if (!Inside(p, 1.0) || !Dry(p)) continue;
                var near = trees.Where(t => Vec2.Distance(t.P, p) <= 5.0).Select(t => t.P).ToList();
                if (near.Count > bestScore) { bestScore = near.Count; bestT = p; nearby = near; }
            }
            var focus = nearby.OrderBy(q => Vec2.Distance(q, bestT)).Take(3).ToList();
            if (focus.Count == 0) focus.Add(bestT);
            var fc = new Vec2(focus.Average(q => q.X), focus.Average(q => q.Z));
            double dist = 4.0, bestClear = -1, bestYaw = 0;
            for (int k = 0; k < 12; k++)
            {
                double yaw = k * Math.PI / 6;
                var ep = new Vec2(fc.X + Math.Cos(yaw) * dist, fc.Z + Math.Sin(yaw) * dist);
                if (!Inside(ep, 0.4) || !Dry(ep) || Blocked(ep, 1.2, 0.4)) continue;
                if (!LineClear(new Vector3((float)ep.X, Gp(ep) + 1.2f, (float)ep.Z), new Vector3((float)fc.X, Gp(fc) + 0.6f, (float)fc.Z))) continue;
                double clear = Math.Min(NearestTree(ep), 3.0);
                if (clear > bestClear) { bestClear = clear; bestYaw = yaw; }
            }
            var eyeP = new Vec2(fc.X + Math.Cos(bestYaw) * dist, fc.Z + Math.Sin(bestYaw) * dist);
            var eye = new Vector3((float)eyeP.X, Gp(eyeP) + 1.2f, (float)eyeP.Z);
            var target = new Vector3((float)fc.X, Gp(fc) + 0.6f, (float)fc.Z);
            scenes.Add(new ReviewScene("woodland_trunks", eye, target, DayLight, $"trees within 5 m of focus={bestScore:0}; trunks framed={focus.Count}"));
        }

        // rocky_outcrop: rock cluster where stone meets terrain
        {
            Rock? bestRock = null; double bestScore = double.NegativeInfinity;
            foreach (var r in world.Props.Rocks)
            {
                if (!Inside(r.Position, 1.0)) continue;
                int nb = world.Props.Rocks.Count(o => Vec2.Distance(o.Position, r.Position) <= 3.0);
                double score = nb * 2 + Math.Max(r.SizeX, Math.Max(r.SizeY, r.SizeZ)) - (Dry(r.Position) ? 0 : 3);
                if (score > bestScore) { bestScore = score; bestRock = r; }
            }
            if (bestRock is { } rk)
            {
                double size = Math.Max(rk.SizeX, Math.Max(rk.SizeY, rk.SizeZ));
                double dist = Math.Clamp(3.0 * size + 1.5, 2.5, 6.0);
                double bestClear = -1, bestYaw = 0;
                for (int k = 0; k < 12; k++)
                {
                    double yaw = k * Math.PI / 6;
                    var ep = new Vec2(rk.X + Math.Cos(yaw) * dist, rk.Z + Math.Sin(yaw) * dist);
                    if (!Inside(ep, 0.4) || !Dry(ep) || Blocked(ep, 0.9, 0.35)) continue;
                    if (world.Props.DistanceToFeature(ep, "rock") < 0.5) continue;
                    if (!LineClear(new Vector3((float)ep.X, Gp(ep) + 0.9f, (float)ep.Z), new Vector3((float)rk.X, (float)rk.Y, (float)rk.Z))) continue;
                    double clear = Math.Min(NearestTree(ep), 3.0);
                    if (clear > bestClear) { bestClear = clear; bestYaw = yaw; }
                }
                var target = new Vector3((float)rk.X, (float)(rk.Y + rk.SizeY * 0.4), (float)rk.Z);
                var eye = target + ReviewFraming.Dir(bestYaw, 14) * (float)dist;
                eye.Y = Math.Max(eye.Y, G(eye.X, eye.Z) + 0.6f);
                scenes.Add(new ReviewScene("rocky_outcrop", eye, target, DayLight, $"rock at ({rk.X:0.00},{rk.Z:0.00}) size={size:0.00}; cluster score={bestScore:0.0}"));
            }
            else
            {
                var target = new Vector3((float)Fallback.X, Gp(Fallback), (float)Fallback.Z);
                scenes.Add(new ReviewScene("rocky_outcrop", target + new Vector3(2, 1, 2), target, DayLight, "fallback: no rocks"));
            }
        }

        // meadow_observation (+ dusk): open, treeless, plant-rich ground seen from as far as the island allows (target ~20 m)
        {
            Vec2 best = Fallback; double bestScore = double.NegativeInfinity;
            foreach (var c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c);
                if (!Inside(p, 2.0) || !Dry(p) || ReviewFraming.SlopeDeg(world, p) > 12) continue;
                double nt = NearestTree(p);
                double score = Math.Min(nt, 6.0) * 2.0 + CountWithin(lows, p, 3.0) * 0.3 - TallNear(p, 3.0, 1.2) * 0.5;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            double elev = 25.0, bestPath = double.NegativeInfinity, bestYaw = 0;
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                double path = InsidePath(best, a, 20.0);
                double dd = 20.0 * Math.Cos(25.0 * Math.PI / 180);
                var eyeXZ = new Vec2(best.X + Math.Cos(a) * dd, best.Z + Math.Sin(a) * dd);
                if (world.PilotTree != null && world.PilotTree.BlocksTrunk(eyeXZ, 2.0)) continue;
                if (!LineClear(new Vector3((float)eyeXZ.X, Gp(best) + 8.5f, (float)eyeXZ.Z), new Vector3((float)best.X, Gp(best) + 0.2f, (float)best.Z))) continue;
                double awayScore = 0;
                if (world.PilotTree != null) { var av = best - world.PilotTree.Anchor; double al = Math.Max(1e-6, av.Length); awayScore = (av.X * Math.Cos(a) + av.Z * Math.Sin(a)) / al; }
                if (awayScore > bestPath) { bestPath = awayScore; bestYaw = a; }
            }
            double wantDist = 20.0;
            double dist = wantDist;
            var target = new Vector3((float)best.X, Gp(best) + 0.2f, (float)best.Z);
            var eye = target + ReviewFraming.Dir(bestYaw, elev) * (float)dist;
            eye.Y = Math.Max(eye.Y, G(eye.X, eye.Z) + 1.5f);
            string note = $"open meadow; nearest tree={NearestTree(best):0.0} m; observation_distance_m={dist:0.0} (island-limited, wanted {wantDist:0})";
            scenes.Add(new ReviewScene("meadow_observation", eye, target, DayLight, note));
            scenes.Add(new ReviewScene("meadow_observation_dusk", eye, target, DuskLight, note + "; low sun (dusk phase)"));
        }
        return scenes;
    }
}
