using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Repr;

internal partial class ReprTickNode : Node3D
{
    public IReprDemo? Demo;
    public override void _Process(double delta) => Demo?.Tick(delta);
}

/// <summary>
/// Windowed capture + measurement runner for experimental representations.
/// <c>--repr-review ABS_DIR [--repr-demo a,b] [--seed N]</c>. Each demo is built alone (all legacy content hidden),
/// shot with fixed cameras, measured, and reported as JSON.
/// </summary>
public static class ReprReviewRunner
{
    private const ulong DefaultSeed = 804584441UL;
    private const double DefaultSeconds = 60.0;   // fixed mid-morning, same as the other review runners

    private static async Task Frames(Main main, int n)
    {
        for (int i = 0; i < n; i++)
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public static List<IReprDemo> Discover()
    {
        var list = new List<IReprDemo>();
        foreach (var t in typeof(IReprDemo).Assembly.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface || !typeof(IReprDemo).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null) continue;
            list.Add((IReprDemo)Activator.CreateInstance(t)!);
        }
        return list.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
    }

    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    private static double Pct(double[] sorted, double p)
        => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    private static double R(double v, int d = 3) => Math.Round(v, d);

    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory, string? demoArg, string? seedArg)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = DefaultSeconds;
        world.Clock.Paused = true;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;

        // Hide ALL legacy content so a demo is alone. EnvironmentRig (sun, fill, sky, post, probe) is kept untouched.
        foreach (Node3D n in new Node3D[] { session.Flora, session.PilotTree, session.Props, session.AmbientGround, session.SoilDetail,
                     session.Guardian, session.Fauna, session.Coverage, session.Island, session.Water, session.WaterFluid })
            n.Visible = false;

        ulong seed = ulong.TryParse(seedArg, out var sd) ? sd : DefaultSeed;
        var all = Discover();
        List<IReprDemo> demos;
        if (!string.IsNullOrWhiteSpace(demoArg) && !demoArg.Contains(':') && !demoArg.Contains('/') && !demoArg.Contains('\\'))
        {
            demos = new();
            foreach (var name in demoArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                demos.Add(all.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Unknown repr demo '{name}'. Known: {string.Join(", ", all.Select(d => d.Name))}"));
        }
        else demos = all;
        if (demos.Count == 0) throw new InvalidOperationException("No repr demos found.");

        var vp = main.GetViewport();
        var envResult = ReprEnv.Apply(session, vp, Main.ArgAfter("--repr-env"));
        var vpRid = vp.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vpRid, true);
        var cam = session.CameraRig.Cam;
        var log = new List<string>();
        var machine = new
        {
            adapter = RenderingServer.GetVideoAdapterName(),
            resolution = new[] { (int)vp.GetVisibleRect().Size.X, (int)vp.GetVisibleRect().Size.Y },
            window = new[] { DisplayServer.WindowGetSize().X, DisplayServer.WindowGetSize().Y },
            render_scale = vp.Scaling3DScale,
            max_fps = Engine.MaxFps,
            vsync = DisplayServer.WindowGetVsyncMode().ToString(),
            quality = session.Settings.Quality,
            godot = Engine.GetVersionInfo()["string"].ToString(),
        };

        // Managed-allocation baseline of the harness loop itself (await continuations etc.), measured on the bare scene.
        await Frames(main, 30);
        double baseline;
        {
            const int n = 60;
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            await Frames(main, n);
            baseline = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)n;
        }

        var index = new List<object>();
        foreach (var demo in demos)
        {
            string demoDir = Path.Combine(directory, Safe(demo.Name));
            Directory.CreateDirectory(demoDir);
            var ctx = new ReprDemoContext { WorldSeed = seed, World = world, Session = session, OutDir = demoDir, LogSink = log.Add };
            var root = new Node3D { Name = "Repr_" + demo.Name };
            main.AddChild(root);
            root.AddChild(new ReprTickNode { Name = "Tick", Demo = demo });
            var sw = Stopwatch.StartNew();
            demo.Build(root, ctx);
            double buildMs = sw.Elapsed.TotalMilliseconds;
            ctx.Log($"{demo.Name}: built in {buildMs:0.0} ms");

            // Let shaders / streaming settle: at least 30 frames, then until frames are quick for 8 in a row (cap 900).
            session.EnvRig.BenchmarkLightSeconds = DefaultSeconds;
            cam.Fov = demo.Shots.Count > 0 ? demo.Shots[0].FovDeg : 40f;
            if (demo.Shots.Count > 0) session.CameraRig.LookAtPoint(demo.Shots[0].Eye, demo.Shots[0].Target);
            int settle = 0, quick = 0;
            long last = Stopwatch.GetTimestamp();
            while (settle < 900 && (settle < 30 || quick < 8))
            {
                await Frames(main, 1);
                long now = Stopwatch.GetTimestamp();
                quick = Stopwatch.GetElapsedTime(last, now).TotalMilliseconds < 100 ? quick + 1 : 0;
                last = now; settle++;
            }

            string? shotArg = Main.ArgAfter("--repr-shots");
            var requestedShots = string.IsNullOrWhiteSpace(shotArg) ? null :
                shotArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedShots = requestedShots == null ? demo.Shots : demo.Shots.Where(s => requestedShots.Contains(s.Name)).ToArray();
            if (selectedShots.Count == 0) throw new InvalidOperationException($"No shots selected for {demo.Name}: {shotArg}");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shotsOut = new List<object>();
            foreach (var shot in selectedShots)
            {
                demo.ApplyVariant(shot.Variant);
                double seconds = shot.DayFraction is float f ? Math.Clamp(f, 0f, 0.99999f) * DayNightClock.CycleSeconds : DefaultSeconds;
                bool timeChanged = Math.Abs(session.EnvRig.BenchmarkLightSeconds!.Value - seconds) > 1e-6;
                session.EnvRig.BenchmarkLightSeconds = seconds;
                cam.Fov = shot.FovDeg;
                session.CameraRig.LookAtPoint(shot.Eye, shot.Target);
                // Auto exposure adapts over a few hundred ms: give a time-of-day change extra frames.
                await Frames(main, timeChanged ? 90 : 20);
                string file = Safe(shot.Name + (shot.Variant != null ? "_" + shot.Variant : ""));
                string unique = file; int dup = 2;
                while (!used.Add(unique)) unique = file + "_" + dup++;
                string png = Path.Combine(demoDir, unique + ".png");
                await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(png);

                // Measure.
                int N = Math.Max(1, shot.MeasureFrames);
                var ms = new double[N]; var gpu = new double[N]; var draws = new double[N]; var prims = new double[N]; var objs = new double[N];
                double vram = 0;
                long ts = Stopwatch.GetTimestamp();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < N; i++)
                {
                    await Frames(main, 1);
                    long now = Stopwatch.GetTimestamp();
                    ms[i] = Stopwatch.GetElapsedTime(ts, now).TotalMilliseconds; ts = now;
                    gpu[i] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(vpRid);
                    draws[i] = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                    prims[i] = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
                    objs[i] = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
                }
                double allocPer = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)N;
                vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);
                var sorted = (double[])ms.Clone(); Array.Sort(sorted);
                var gpuNz = gpu.Where(g => g > 0).ToArray();
                double? gpuMean = gpuNz.Length > 0 ? R(gpuNz.Average()) : null;
                var rec = new
                {
                    shot = unique, name = shot.Name, variant = shot.Variant, png = unique + ".png",
                    eye = new[] { shot.Eye.X, shot.Eye.Y, shot.Eye.Z }, target = new[] { shot.Target.X, shot.Target.Y, shot.Target.Z },
                    fov_deg = shot.FovDeg, day_fraction = shot.DayFraction, cycle_seconds = R(seconds, 2),
                    frames = N,
                    fps_actual = R(N * 1000.0 / Math.Max(1e-6, ms.Sum()), 2),
                    gpu_busy_fraction = gpuMean is double gm ? R(gm * (N * 1000.0 / Math.Max(1e-6, ms.Sum())) / 1000.0, 4) : (double?)null,
                    frame_ms_p50 = R(Pct(sorted, 0.50)), frame_ms_p95 = R(Pct(sorted, 0.95)), frame_ms_max = R(sorted[^1]),
                    gpu_frame_ms_mean = gpuMean, gpu_samples_nonzero = gpuNz.Length,
                    draw_calls = R(draws.Average(), 1), primitives = R(prims.Average(), 0), objects = R(objs.Average(), 1),
                    video_mem_mb = R(vram, 1),
                    managed_bytes_per_frame_raw = R(allocPer, 0),
                    managed_bytes_per_frame_net = R(Math.Max(0, allocPer - baseline), 0),
                };
                shotsOut.Add(rec);
                ctx.Log($"{demo.Name}/{unique}: p50={rec.frame_ms_p50} p95={rec.frame_ms_p95} max={rec.frame_ms_max} gpu={(gpuMean?.ToString() ?? "null")} draws={rec.draw_calls} prims={rec.primitives} objs={rec.objects} vram={rec.video_mem_mb}MB alloc/frame={rec.managed_bytes_per_frame_net}");
            }

            demo.ApplyVariant(null);
            var demoReport = new
            {
                demo = demo.Name, seed, machine, env_overrides = envResult, build_ms = R(buildMs, 1), settle_frames = settle,
                managed_bytes_per_frame_baseline = R(baseline, 0), shots = shotsOut,
            };
            File.WriteAllText(Path.Combine(demoDir, "report.json"), JsonSerializer.Serialize(demoReport, new JsonSerializerOptions { WriteIndented = true }));
            index.Add(new { demo = demo.Name, dir = Safe(demo.Name), shots = shotsOut.Count, build_ms = R(buildMs, 1) });

            root.QueueFree();
            await Frames(main, 10);
        }

        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new { seed, machine, env_overrides = envResult, demos = index, log }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("REPR_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit();
    }
}
