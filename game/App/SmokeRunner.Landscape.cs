using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Godot;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

// Opt-in extension of --perf-test. Legacy perf/reference formats remain readable.
public partial class SmokeRunner
{
    private async Task LandscapeAsync()
    {
        string scenario = System.Environment.GetEnvironmentVariable("VIVARIUM_LANDSCAPE_SCENARIO") ?? "frozen";
        string[] allowed = { "frozen", "live", "understory", "colony", "traversal", "sculpt", "wet", "density" };
        if (scenario == "density") throw new InvalidOperationException("Pinned worst-density fixture not established; density gate is unsupported, never aliased to live.");
        if (!allowed.Contains(scenario)) throw new ArgumentException("Unknown landscape scenario: " + scenario);
        int duration = int.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_SECONDS"), out var seconds) ? seconds : 60;
        if (duration < 60) throw new ArgumentException("Landscape measurements require at least 60 seconds.");
        Session.EnvRig.BenchmarkLightSeconds = 420;
        Session.Settings.Quality = 2; Session.ApplySettings();
        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false;
        W.Clock.Paused = true;
        // Exactly the same deterministic biological preparation as the reference library.
        long preparationTick = W.Clock.Tick;
        string preparation = System.Environment.GetEnvironmentVariable("VIVARIUM_LANDSCAPE_PREPARATION") ?? "mature";
        if (preparation is not ("initial" or "mature")) throw new ArgumentException("Unknown preparation fixture: " + preparation);
        double preparationBioDays = preparation == "mature" ? ReferenceBioDays : 0;
        while (W.Clock.BioDays < preparationBioDays && W.Clock.Tick - preparationTick < 2_000_000)
        {
            W.Step(200); await Frames(1);
        }
        var scenes = BuildReferenceScenes();
        string selected = scenario switch { "understory" => "ground_vegetation", "colony" => "dense_colony", "wet" => "water_margin", _ => "overview" };
        var pose = scenes.FirstOrDefault(s => s.Name == selected) ?? throw new InvalidOperationException("Required scene absent: " + selected);
        Session.CameraRig.LookAtPoint(pose.Eye, pose.Target);
        await Seconds(10); // retained startup/compile cost belongs to launcher logs, never to steady-state frame data
        string before = WorldSerializer.Digest(W);
        long tickBefore = W.Clock.Tick;
        int floraBefore = W.Flora.Count, faunaBefore = W.Fauna.Count;
        bool evolves = scenario is "live" or "density";
        W.Clock.Paused = !evolves;
        // Paused is serialized; the digest contract starts after setting the measurement mode.
        before = WorldSerializer.Digest(W);
        FrameProfiler.TakeStructuredReport();
        var frames = new List<double>();
        var windows = new List<object>();
        var memory = new List<object>();
        var cpu = new List<object>();
        var captures = new List<object>();
        var counters = new List<object>();
        ulong start = Time.GetTicksUsec(), windowStart = start, previousFrame = start;
        int windowFrames = 0, captureIndex = 0, sculptIndex = 0;
        double cpuPeak = 0;
        while ((Time.GetTicksUsec() - start) / 1_000_000.0 < duration)
        {
            double t = (Time.GetTicksUsec() - start) / 1_000_000.0;
            if (scenario == "wet") Session.EnvRig.BenchmarkLightSeconds = 420 + t * 10;
            // Trajectory is analytic in elapsed presentation seconds; world query poses have stable ID ties.
            if (scenario is "frozen" or "live" or "density" or "traversal" or "sculpt")
            {
                float angle = (float)t * (scenario == "traversal" ? 1.5f : 0.3f);
                Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(angle) * 9, 3.5f, Mathf.Cos(angle) * 9), Vector3.Zero);
            }
            if (scenario == "sculpt" && (int)(t * 10) > sculptIndex)
            {
                sculptIndex = (int)(t * 10);
                Vivarium.Sim.World.TerrainEditing.Sculpt(W, new Vivarium.Sim.Core.Vec2(Math.Sin(t) * 2, Math.Cos(t * 0.7) * 2), 0.6, (sculptIndex & 1) == 0 ? 0.01 : -0.01, Vivarium.Sim.World.SculptMode.Raise);
            }
            await Frames(1);
            ulong frameEnd = Time.GetTicksUsec();
            frames.Add((frameEnd - previousFrame) / 1000.0); previousFrame = frameEnd; windowFrames++;
            cpuPeak = Math.Max(cpuPeak, Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            ulong now = Time.GetTicksUsec();
            if (now - windowStart >= 1_000_000)
            {
                double elapsed = (now - windowStart) / 1_000_000.0;
                windows.Add(new { start_s = (windowStart - start) / 1_000_000.0, duration_s = elapsed, frames = windowFrames, fps = windowFrames / elapsed });
                memory.Add(new { elapsed_s = (now - start) / 1_000_000.0, working_set_bytes = System.Environment.WorkingSet, vram_bytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) });
                cpu.Add(new { elapsed_s = (now - start) / 1_000_000.0, process_peak_ms = cpuPeak, simulation_last_ms = W.Scheduler.LastAdvanceMs, scope_max_ms = FrameProfiler.TakeStructuredReport() });
                counters.Add(new { elapsed_s = (now - start) / 1_000_000.0, draw_calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame), flora_visible = Session.Flora.Visible_, flora_triangles = Session.Flora.TrianglesDrawn, fauna_drawn = Session.Fauna.Drawn, coverage_tiles = Session.Coverage.TileCount, coverage_instances = Session.Coverage.InstanceCount, coverage_triangles_built = Session.Coverage.TrianglesBuilt, sculpt_updates = sculptIndex, tick = W.Clock.Tick });
                windowStart = now; windowFrames = 0; cpuPeak = 0;
            }
            // Capture only after measurement: readback stalls must not be hidden from the sample stream.
        }
        double measuredSeconds = (Time.GetTicksUsec() - start) / 1_000_000.0;
        // Retain timings before captures/replay/provenance can fail. Never overwrite an attempt.
        using (var raw = new FileStream(Path.Combine(OutDir, "raw_measurement.json"), FileMode.CreateNew))
            JsonSerializer.Serialize(raw, new { duration_seconds = measuredSeconds, samples_ms = frames, windows, memory, cpu, counters, digest_before = before }, new JsonSerializerOptions { WriteIndented = true });
        string after = WorldSerializer.Digest(W);
        long tickAfter = W.Clock.Tick;
        int floraAfter = W.Flora.Count, faunaAfter = W.Fauna.Count;
        W.Clock.Paused = true;
        // Frozen counterpart for live/sculpt: digest remains invariant during camera/light/quality changes.
        string renderOnlyBefore = WorldSerializer.Digest(W);
        foreach (var s in scenes)
        {
            Session.CameraRig.LookAtPoint(s.Eye, s.Target); await Screenshot(s.Name);
            captures.Add(new { name = s.Name, path = s.Name + ".png", eye = new[] { s.Eye.X, s.Eye.Y, s.Eye.Z }, target = new[] { s.Target.X, s.Target.Y, s.Target.Z }, light_seconds = Session.EnvRig.Clock.Seconds });
            captureIndex++;
        }
        // Invariant includes subsequent motion, lighting and quality operations.
        // 12 seconds of movement, sampled images; separate from the 60s timing run.
        ulong motionStart = Time.GetTicksUsec();
        int motionIndex = 0;
        while (motionIndex < 12)
        {
            double t = (Time.GetTicksUsec() - motionStart) / 1_000_000.0;
            float a = (float)t * 0.3f;
            Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(a) * 9, 3.5f, Mathf.Cos(a) * 9), Vector3.Zero);
            await Frames(1);
            if (t >= motionIndex + 1)
            {
                string name = "motion_" + motionIndex.ToString("D2");
                await Screenshot(name);
                captures.Add(new { name, path = name + ".png", light_seconds = Session.EnvRig.Clock.Seconds });
                motionIndex++;
            }
        }
        foreach (int tier in new[] { 0, 1, 2 }) { Session.Settings.Quality = tier; Session.ApplySettings(); await Frames(2); }
        Session.EnvRig.BenchmarkLightSeconds = 900; await Frames(2);
        Session.EnvRig.BenchmarkLightSeconds = 420; await Frames(2);
        string renderOnlyAfter = WorldSerializer.Digest(W);
        var replay = WorldSerializer.Deserialize(W.Content, WorldSerializer.Serialize(W));
        replay.CoverageSystem.AsyncMode = false;
        W.CoverageSystem.AsyncMode = false;
        string reloadDigest = WorldSerializer.Digest(replay);
        Check("prepared state reload digest invariant", reloadDigest == renderOnlyAfter);
        for (int i = 0; i < 3; i++)
        {
            W.Step(1); replay.Step(1);
            Session.Settings.Quality = i; Session.ApplySettings();
            Session.EnvRig.BenchmarkLightSeconds = 420 + i * 240;
            Session.CameraRig.LookAtPoint(new Vector3(i, 5, 9), Vector3.Zero);
            await Frames(2);
        }
        string replayExpected = WorldSerializer.Digest(replay), replayActual = WorldSerializer.Digest(W);
        Check("render settings/camera/light preserve same-tick ecology", replayExpected == replayActual);
        Session.Settings.Quality = 2; Session.ApplySettings(); Session.EnvRig.BenchmarkLightSeconds = 420;
        var sorted = frames.OrderBy(x => x).ToArray();
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * q))];
        var provenancePath = System.Environment.GetEnvironmentVariable("VIVARIUM_RENDER_PROVENANCE") ?? throw new InvalidOperationException("Benchmark launcher provenance missing.");
        var provenance = JsonDocument.Parse(File.ReadAllText(provenancePath)).RootElement.Clone();
        var gameCode = AssemblyReceipt(typeof(SmokeRunner).Assembly);
        var simCode = AssemblyReceipt(typeof(Vivarium.Sim.World.VivariumWorld).Assembly);
        Check("executing assembly matches build module", gameCode.Matches && simCode.Matches);
        var report = new
        {
            schema_version = 1, provenance,
            executing_code = new { game_assembly = gameCode.Path, game_assembly_sha256 = gameCode.Sha256, game_assembly_mvid = gameCode.Mvid, game_assembly_mvid_matches = gameCode.Matches, sim_assembly_sha256 = simCode.Sha256, sim_assembly_mvid = simCode.Mvid, sim_assembly_mvid_matches = simCode.Matches },
            world = new { seed = W.Seed, tick_before = tickBefore, tick_after = tickAfter, flora = floraBefore, fauna = faunaBefore, flora_after = floraAfter, fauna_after = faunaAfter, preparation_bio_days = preparationBioDays },
            scenario = new { id = scenario, library_version = 1, preparation, light_seconds = 420, wind_phase = "engine TIME unpinned; no byte-identical wind claim", selected_scene = selected, evolves, sculpts = scenario == "sculpt", warmup_seconds = 10, duration_seconds = measuredSeconds, scenes = captures },
            render_settings = new { width = GetWindow().Size.X, height = GetWindow().Size.Y, renderer = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName(), quality = Session.Settings.Quality, vsync = DisplayServer.WindowGetVsyncMode().ToString(), msaa = GetViewport().Msaa3D.ToString(), screen_aa = GetViewport().ScreenSpaceAA.ToString(), scale = GetViewport().Scaling3DScale, flora_tiers = "existing projected-size hysteresis", flags = CaptureRenderFlags(), display = DisplayServer.GetName() },
            cpu_timing = cpu, gpu_timing = (object?)null,
            frame_times = new { samples_ms = frames, windows, p50_ms = P(.5), p95_ms = P(.95), p99_ms = P(.99), p999_ms = P(.999), worst_ms = sorted[^1] },
            counters = new { samples = counters, memory, unsupported = new[] { "instance-upload bytes", "dirty tile count", "geometry queue length", "separate leaf/branch triangle counts", "GPU draw/lighting/synchronization timing" } },
            digest_before = before, digest_after = after, render_only_digest_before = renderOnlyBefore, render_only_digest_after = renderOnlyAfter,
            invariants = new { reload_digest = reloadDigest, replay_expected_digest = replayExpected, replay_actual_digest = replayActual, ticks = 3 },
            artifacts = captures, warnings = new[] { "CPU scope maxima are overlapping, not additive GPU timings.", "GPU timing unavailable; process-frame timing is wall time.", "Presentation light fixed to 420 seconds; shader TIME wind is unpinned, so byte-identical image reproduction is not claimed.", "20 minute resource soak and exhaustive growth/death event bursts require follow-up; this run cannot establish those gates." }
        };
        // Fail on collisions; launcher makes run directories unique and never overwrites evidence.
        using var stream = new FileStream(Path.Combine(OutDir, "render_report.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
        Check("frozen render-only digest invariant", renderOnlyBefore == renderOnlyAfter);
        if (!evolves && scenario != "sculpt") Check("measurement digest invariant", before == after);
        Check("landscape captures available", DisplayServer.GetName() != "headless" && captureIndex > 0);
    }

    // Godot loads managed assemblies from bytes, so Assembly.Location may be empty.
    // Verify the candidate file's module ID against the executing module before hashing it.
    private static (string Path, string Sha256, string Mvid, bool Matches) AssemblyReceipt(Assembly assembly)
    {
        string path = assembly.Location;
        if (string.IsNullOrEmpty(path))
            path = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/" + assembly.GetName().Name + ".dll");
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        var metadata = pe.GetMetadataReader();
        Guid fileMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        Guid executingMvid = assembly.ManifestModule.ModuleVersionId;
        return (path, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), executingMvid.ToString(), fileMvid == executingMvid);
    }

    private static Dictionary<string, string> CaptureRenderFlags() => System.Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>().Where(e => e.Key.ToString()!.StartsWith("VIVARIUM_", StringComparison.Ordinal))
        .Where(e => !new[] { "VIVARIUM_RENDER_PROVENANCE", "VIVARIUM_BENCHMARK_USER_DIR", "VIVARIUM_GODOT", "VIVARIUM_LANDSCAPE_SCENARIO", "VIVARIUM_LANDSCAPE_PREPARATION", "VIVARIUM_PERF_SECONDS" }.Contains(e.Key.ToString()))
        .OrderBy(e => e.Key.ToString()).ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString() ?? "");
}
