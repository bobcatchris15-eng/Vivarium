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
using Vivarium.Game.Camera;
using Vivarium.Game.Render;

namespace Vivarium.Game.App;

// Opt-in extension of --perf-test. Legacy perf/reference formats remain readable.
public partial class SmokeRunner
{
    private async Task LandscapeAsync()
    {
        string scenario = System.Environment.GetEnvironmentVariable("VIVARIUM_LANDSCAPE_SCENARIO") ?? "frozen";
        string[] allowed = { "frozen", "live", "understory", "colony", "traversal", "sculpt", "wet", "density", "stationary", "repeat_traversal" };
        if (scenario == "density") throw new InvalidOperationException("Pinned worst-density fixture not established; density gate is unsupported, never aliased to live.");
        if (!allowed.Contains(scenario)) throw new ArgumentException("Unknown landscape scenario: " + scenario);
        int duration = int.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_SECONDS"), out var seconds) ? seconds : 60;
        if (duration < 60) throw new ArgumentException("Landscape measurements require at least 60 seconds.");
        Session.EnvRig.BenchmarkLightSeconds = 420;
        int benchmarkQuality = int.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_BENCHMARK_QUALITY"), out var requestedQuality) ? requestedQuality : 2;
        if (benchmarkQuality is < 0 or > 2) throw new ArgumentOutOfRangeException("VIVARIUM_BENCHMARK_QUALITY");
        Session.Settings.Quality = benchmarkQuality; Session.ApplySettings();
        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false;
        Session.CameraRig.KeyboardBlocked = true;
        Session.CameraRig.SetProcessUnhandledInput(false);
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
        // Initial mesh publication can take longer than a fixed warmup. Never
        // label a partially drawn population as steady-state render performance.
        ulong populationWait = Time.GetTicksMsec();
        while (!Session.Flora.PopulationReady)
        {
            if (Time.GetTicksMsec() - populationWait > 300_000)
                throw new TimeoutException("Flora population did not finish publishing before landscape measurement.");
            Session.Flora._Process(.5);
            await Frames(1);
        }
        string[] requiredScenes = { "overview", "interaction", "ground_vegetation", "macro_flora", "dense_colony", "water_margin", "sparse_dry", "mixed_depth" };
        var scenes = BuildReferenceScenes().Where(s => requiredScenes.Contains(s.Name)).ToList();
        string selected = scenario switch { "understory" => "ground_vegetation", "colony" => "dense_colony", "wet" => "water_margin", _ => "overview" };
        var pose = scenes.FirstOrDefault(s => s.Name == selected) ?? throw new InvalidOperationException("Required scene absent: " + selected);
        Session.CameraRig.LookAtPoint(pose.Eye, pose.Target);
        // Fixture preparation is an explicit world change. Static dressing otherwise
        // stays cached through ordinary ecological ticks.
        Session.AmbientGround.Invalidate();
        Session.SoilDetail.LoamDetails.Invalidate();
        await Seconds(10); // retained startup/compile cost belongs to launcher logs, never to steady-state frame data
        await SettleSurfaceDetailsAsync();
        if (scenario == "repeat_traversal")
        {
            // Complete each view's first-visit publication before advancing to the
            // next view; jumping every frame cancels work rather than warming it.
            for (int step = 0; step < 32; step++)
            {
                float a = step / 32f * Mathf.Tau;
                Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(a) * 9, 3.5f, Mathf.Cos(a) * 9), Vector3.Zero);
                await Frames(1);
                await SettleSurfaceDetailsAsync();
            }
            await SettleSurfaceDetailsAsync();
            Session.CameraRig.LookAtPoint(new Vector3(0, 3.5f, 9), Vector3.Zero);
            await Frames(2);
        }
        bool floraPopulationReady = Session.Flora.PopulationReady;
        if (!floraPopulationReady) throw new InvalidOperationException("Flora population changed during landscape warmup.");
        int ambientRefreshesBefore = Session.AmbientGround.RefreshCount;
        int loamRebuildsBefore = Session.SoilDetail.LoamDetails.RebuildCount;
        int bankBuildsBefore = Session.AmbientGround.BankPatchBuildCount;
        long depthHitsBefore = W.AquaticSystem.DepthCacheHits, depthMissesBefore = W.AquaticSystem.DepthCacheMisses;
        string before = WorldSerializer.Digest(W);
        long tickBefore = W.Clock.Tick;
        int floraBefore = W.Flora.Count, faunaBefore = W.Fauna.Count;
        bool evolves = scenario is "live" or "density" or "stationary";
        W.Clock.Paused = !evolves;
        // Paused is serialized; the digest contract starts after setting the measurement mode.
        before = WorldSerializer.Digest(W);
        FrameProfiler.TakeStructuredReport();
        FrameProfiler.Reset();
        var measuredViewport = GetViewport().GetViewportRid();
        var measuredSettings = new { width = GetWindow().Size.X, height = GetWindow().Size.Y, renderer = RenderingServer.GetCurrentRenderingMethod(), adapter = RenderingServer.GetVideoAdapterName(), quality = Session.Settings.Quality, vsync = DisplayServer.WindowGetVsyncMode().ToString(), msaa = GetViewport().Msaa3D.ToString(), screen_aa = GetViewport().ScreenSpaceAA.ToString(), scale = GetViewport().Scaling3DScale, presentation_options = new { Session.Settings.ReducedDistantPlants, Session.Settings.DistanceBlur, Session.Settings.SimpleFoliageLighting, Session.Settings.GlobalIllumination, Session.Settings.FineIndirectLighting, Session.Settings.DynamicShadows, Session.Settings.AmbientOcclusion, Session.Settings.VolumetricMist }, indirect_lighting = new { cascades = Session.EnvRig.Env.SdfgiCascades, cell_size = Session.EnvRig.Env.SdfgiMinCellSize, max_distance = Session.EnvRig.Env.SdfgiMaxDistance }, flora_tiers = "projected-size hysteresis, optional 6m/12m distance bands", flags = CaptureRenderFlags(), display = DisplayServer.GetName() };
        RenderingServer.ViewportSetMeasureRenderTime(measuredViewport, true);
        var frames = new List<double>();
        var gpuFrames = new List<double>();
        var renderCpuFrames = new List<double>();
        var windows = new List<object>();
        var memory = new List<object>();
        var cpu = new List<object>();
        var captures = new List<object>();
        var counters = new List<object>();
        var hitchFrames = new List<object>();
        var windowScopes = new Dictionary<string, double>();
        int previousGc0 = GC.CollectionCount(0), previousGc1 = GC.CollectionCount(1), previousGc2 = GC.CollectionCount(2);
        long previousAllocated = GC.GetTotalAllocatedBytes(false);
        double previousPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
        GD.Print("LANDSCAPE_MEASUREMENT_BEGIN " + scenario);
        ulong start = Time.GetTicksUsec(), windowStart = start, previousFrame = start;
        int windowFrames = 0, captureIndex = 0, sculptIndex = 0;
        double cpuPeak = 0;
        while ((Time.GetTicksUsec() - start) / 1_000_000.0 < duration)
        {
            double t = (Time.GetTicksUsec() - start) / 1_000_000.0;
            if (scenario == "wet") Session.EnvRig.BenchmarkLightSeconds = 420 + t * 10;
            // Trajectory is analytic in elapsed presentation seconds; world query poses have stable ID ties.
            if (scenario is "traversal" or "repeat_traversal")
            {
                float angle = (float)t * 1.5f;
                Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(angle) * 9, 3.5f, Mathf.Cos(angle) * 9), Vector3.Zero);
            }
            else if (scenario is "frozen" or "live" or "density" or "sculpt")
            {
                float angle = (float)t * 0.3f;
                Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(angle) * 9, 3.5f, Mathf.Cos(angle) * 9), Vector3.Zero);
            }
            if (scenario == "sculpt" && (int)(t * 10) > sculptIndex)
            {
                sculptIndex = (int)(t * 10);
                Vivarium.Sim.World.TerrainEditing.Sculpt(W, new Vivarium.Sim.Core.Vec2(Math.Sin(t) * 2, Math.Cos(t * 0.7) * 2), 0.6, (sculptIndex & 1) == 0 ? 0.01 : -0.01, Vivarium.Sim.World.SculptMode.Raise);
            }
            await Frames(1);
            // Engine timestamps measure rendering separately from scripts and frame wall time.
            // GPU results may arrive late; retain valid samples without claiming frame alignment.
            double gpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(measuredViewport);
            if (double.IsFinite(gpuMs) && gpuMs > 0) gpuFrames.Add(gpuMs);
            double renderCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(measuredViewport);
            if (double.IsFinite(renderCpuMs) && renderCpuMs > 0) renderCpuFrames.Add(renderCpuMs);
            ulong frameEnd = Time.GetTicksUsec();
            double frameMs = (frameEnd - previousFrame) / 1000.0;
            frames.Add(frameMs); previousFrame = frameEnd; windowFrames++;
            var frameMetrics = FrameProfiler.TakeFrameScopeMetrics();
            var frameScopes = FrameProfiler.TakeStructuredReport();
            foreach (var entry in frameScopes) windowScopes[entry.Key] = Math.Max(windowScopes.GetValueOrDefault(entry.Key), entry.Value);
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(false);
            double pauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
            if (frameMs >= 45 || frameScopes.Values.Any(ms => ms >= 10))
                hitchFrames.Add(new {
                    frame = frames.Count - 1,
                    elapsed_s = (frameEnd - start) / 1_000_000.0,
                    frame_ms = frameMs,
                    gpu_ms = double.IsFinite(gpuMs) && gpuMs > 0 ? (double?)gpuMs : null,
                    render_cpu_ms = double.IsFinite(renderCpuMs) && renderCpuMs > 0 ? (double?)renderCpuMs : null,
                    gc0 = gc0 - previousGc0, gc1 = gc1 - previousGc1, gc2 = gc2 - previousGc2,
                    gc_pause_ms = pauseMs - previousPauseMs,
                    allocated_bytes = allocated - previousAllocated,
                    simulation_last_ms = W.Scheduler.LastAdvanceMs,
                    scheduler_ticks = W.Scheduler.TicksLastAdvance,
                    backlog = W.Scheduler.Backlog,
                    water_tiles_uploaded = Session.WaterFluid.TilesUploaded,
                    water_triangles_built = Session.WaterFluid.TrianglesBuilt,
                    water_allocated_bytes = Session.WaterFluid.AllocatedBytes,
                    water_texture_upload_bytes = Session.WaterFluid.TextureUploadBytes,
                    water_snapshot_version = W.Liquid.Version,
                    scope_metrics = frameMetrics.ToDictionary(k => k.Key, v => new { max_ms = v.Value.MaxMs, total_ms = v.Value.TotalMs, count = v.Value.Count }),
                    scope_max_ms = frameScopes
                });
            previousGc0 = gc0; previousGc1 = gc1; previousGc2 = gc2; previousAllocated = allocated;
            previousPauseMs = pauseMs;
            cpuPeak = Math.Max(cpuPeak, Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            ulong now = Time.GetTicksUsec();
            if (now - windowStart >= 1_000_000)
            {
                double elapsed = (now - windowStart) / 1_000_000.0;
                windows.Add(new { start_s = (windowStart - start) / 1_000_000.0, duration_s = elapsed, frames = windowFrames, fps = windowFrames / elapsed });
                memory.Add(new { elapsed_s = (now - start) / 1_000_000.0, working_set_bytes = System.Environment.WorkingSet, vram_bytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) });
                cpu.Add(new { elapsed_s = (now - start) / 1_000_000.0, process_peak_ms = cpuPeak, simulation_last_ms = W.Scheduler.LastAdvanceMs, scope_max_ms = new Dictionary<string, double>(windowScopes) });
                windowScopes.Clear();
                counters.Add(new { elapsed_s = (now - start) / 1_000_000.0, draw_calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame), flora_visible = Session.Flora.Visible_, flora_triangles = Session.Flora.TrianglesDrawn, fauna_drawn = Session.Fauna.Drawn, coverage_tiles = Session.Coverage.TileCount, coverage_instances = Session.Coverage.InstanceCount, coverage_triangles_built = Session.Coverage.TrianglesBuilt, sculpt_updates = sculptIndex, tick = W.Clock.Tick });
                windowStart = now; windowFrames = 0; cpuPeak = 0;
            }
            // Capture only after measurement: readback stalls must not be hidden from the sample stream.
        }
        double measuredSeconds = (Time.GetTicksUsec() - start) / 1_000_000.0;
        RenderingServer.ViewportSetMeasureRenderTime(measuredViewport, false);
        var scopePerFrame = FrameProfiler.PerFrameDurations(frames.Count);
        var terrainCache = new {
            builds = Session.Island.GravelMaskBuilds, reuses = Session.Island.GravelMaskReuses,
            audited_pixels = Session.Island.GravelMaskAuditedPixels, differences = Session.Island.GravelMaskAuditDifferences
        };
        var surfaceDetail = new {
            ambient_refreshes = Session.AmbientGround.RefreshCount - ambientRefreshesBefore,
            loam_patch_rebuilds = Session.SoilDetail.LoamDetails.RebuildCount - loamRebuildsBefore,
            ambient_instances = Session.AmbientGround.LastGroundcoverInstances,
            bank_patch_builds = Session.AmbientGround.BankPatchBuildCount - bankBuildsBefore,
            aquatic_depth_cache_hits = W.AquaticSystem.DepthCacheHits - depthHitsBefore,
            aquatic_depth_cache_misses = W.AquaticSystem.DepthCacheMisses - depthMissesBefore,
            loam_patches = Session.SoilDetail.LoamDetails.PatchCount
        };
        if (scenario is "understory" or "colony" or "wet" or "stationary")
        {
            Check("idle ecology does not rebuild ambient dressing", surfaceDetail.ambient_refreshes == 0);
            Check("idle ecology does not rebuild loam dressing", surfaceDetail.loam_patch_rebuilds == 0);
        }
        // Retain timings before captures/replay/provenance can fail. Never overwrite an attempt.
        using (var raw = new FileStream(Path.Combine(OutDir, "raw_measurement.json"), FileMode.CreateNew))
            JsonSerializer.Serialize(raw, new { duration_seconds = measuredSeconds, render_settings = measuredSettings, samples_ms = frames, windows, memory, cpu, counters, hitch_frames = hitchFrames, digest_before = before, gpu_samples_ms = gpuFrames, render_cpu_samples_ms = renderCpuFrames, cpu_scope_per_frame_ms = scopePerFrame, terrain_cache = terrainCache, surface_detail = surfaceDetail }, new JsonSerializerOptions { WriteIndented = true });
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_SURFACE_EVENT_AUDIT") == "1")
            await AuditSurfaceCacheAsync();
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_WATER_TILE_AUDIT") == "1")
        {
            var tiles = Session.WaterFluid.AuditPublishedTextures();
            Check("water active tiles, halos and uploaded textures match reference", tiles.Tiles > 0 && tiles.Mismatches == 0);
            File.WriteAllText(Path.Combine(OutDir, "water-tile-audit.json"), JsonSerializer.Serialize(tiles, new JsonSerializerOptions { IncludeFields = true }));
        }
        string after = WorldSerializer.Digest(W);
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_LOAM_BATCH_AUDIT") == "1")
            await LoamBatchAuditAsync();
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_BENCHMARK_EFFECT_SWEEP") == "1")
            await LandscapeEffectSweepAsync();
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_PERFORMANCE_CONTROLS_AUDIT") == "1")
            await AuditPerformanceControlsAsync();
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_HITCH_TRACE_ONLY") == "1")
        {
            var traceGameCode = AssemblyReceipt(typeof(SmokeRunner).Assembly);
            var traceSimCode = AssemblyReceipt(typeof(Vivarium.Sim.World.VivariumWorld).Assembly);
            Check("executing hitch trace assemblies match build modules", traceGameCode.Matches && traceSimCode.Matches);
            await Screenshot("hitch_overview");
            File.WriteAllText(Path.Combine(OutDir, "hitch_receipt.json"), JsonSerializer.Serialize(new {
                schema_version = 1, scope = "Focused frame-time trace and surface-cache audit; full landscape/capture gate not evaluated",
                scenario, preparation, duration_seconds = measuredSeconds,
                game_module_matches = traceGameCode.Matches, sim_module_matches = traceSimCode.Matches,
                game_sha256 = traceGameCode.Sha256, sim_sha256 = traceSimCode.Sha256,
                game_mvid = traceGameCode.Mvid, sim_mvid = traceSimCode.Mvid,
                digest_before = before, surface_detail = surfaceDetail,
                quality = benchmarkQuality, width = GetWindow().Size.X, height = GetWindow().Size.Y
            }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        long tickAfter = W.Clock.Tick;
        int floraAfter = W.Flora.Count, faunaAfter = W.Fauna.Count;
        W.Clock.Paused = true;
        Session.SetProcess(false);
        W.Scheduler.CompletePendingTick(); W.Liquid.CompletePending();
        // Frozen counterpart for live/sculpt: digest remains invariant during camera/light/quality changes.
        string renderOnlyBefore = WorldSerializer.Digest(W);
        foreach (var s in scenes)
        {
            File.WriteAllText(Path.Combine(OutDir, "capture-progress.json"), JsonSerializer.Serialize(new { scene = s.Name, status = "preparing", started_utc = DateTime.UtcNow }));
            Session.CameraRig.LookAtPoint(s.Eye, s.Target);
            var (camValid, camErr) = ValidateCameraRig(Session.CameraRig);
            var (lightValid, lightErr) = ValidateLightRig(Session.EnvRig);
            if (!camValid || !lightValid)
            {
                string err = !camValid ? camErr : lightErr;
                GD.PrintErr($"CAPTURE_VIEW_ERROR {s.Name}: {err}");
                throw new InvalidOperationException($"Capture view validation failed for '{s.Name}': {err}");
            }
            await Frames(2);
            await SettleSurfaceDetailsAsync();
            Session.Flora.Rebuild(resetDetailHistory: true);
            File.WriteAllText(Path.Combine(OutDir, "capture-progress.json"), JsonSerializer.Serialize(new { scene = s.Name, status = "started", started_utc = DateTime.UtcNow }));
            await Screenshot(s.Name);
            File.WriteAllText(Path.Combine(OutDir, "capture-progress.json"), JsonSerializer.Serialize(new { scene = s.Name, status = "completed", completed_utc = DateTime.UtcNow }));
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
            var (camValid, camErr) = ValidateCameraRig(Session.CameraRig);
            if (!camValid)
                throw new InvalidOperationException($"Motion capture camera validation failed at t={t:0.00}: {camErr}");
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
        if (replayExpected != replayActual)
        {
            var actualParts = WorldSerializer.Serialize(W);
            var expectedParts = WorldSerializer.Serialize(replay);
            var differences = actualParts.Keys.Where(k => !actualParts[k].SequenceEqual(expectedParts[k])).ToArray();
            File.WriteAllText(Path.Combine(OutDir, "replay-differences.json"), JsonSerializer.Serialize(new {
                tick = W.Clock.Tick, differences, aquatic_actual_step = W.AquaticSystem.StepIndex,
                aquatic_expected_step = replay.AquaticSystem.StepIndex
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Check("render settings/camera/light preserve same-tick ecology", replayExpected == replayActual);
        Session.Settings.Quality = benchmarkQuality; Session.ApplySettings(); Session.EnvRig.BenchmarkLightSeconds = 420;
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
            scenario = new { id = scenario, library_version = 2, preparation, flora_population_ready = floraPopulationReady, light_seconds = 420, wind_phase = "engine TIME unpinned; no byte-identical wind claim", selected_scene = selected, evolves, sculpts = scenario == "sculpt", warmup_seconds = 10, duration_seconds = measuredSeconds, scenes = captures },
            render_settings = measuredSettings,
            cpu_timing = cpu, cpu_scope_per_frame_ms = scopePerFrame,
            terrain_cache = terrainCache,
            // Preserve the v1 gpu_timing contract (reserved for per-pass timing).
            gpu_timing = (object?)null,
            viewport_gpu_timing = new { available = gpuFrames.Count > 0, samples_ms = gpuFrames, mean_ms = gpuFrames.Count > 0 ? (double?)gpuFrames.Average() : null, scope = "main viewport rendering; asynchronous engine timestamps, not frame-aligned or per-pass" },
            render_cpu_timing = new { samples_ms = renderCpuFrames, mean_ms = renderCpuFrames.Count > 0 ? (double?)renderCpuFrames.Average() : null },
            frame_times = new { samples_ms = frames, windows, p50_ms = P(.5), p95_ms = P(.95), p99_ms = P(.99), p999_ms = P(.999), worst_ms = sorted[^1] },
            counters = new { samples = counters, memory, unsupported = new[] { "instance-upload bytes", "dirty tile count", "geometry queue length", "separate leaf/branch triangle counts", "GPU per-pass draw/lighting/synchronization timing" } },
            digest_before = before, digest_after = after, render_only_digest_before = renderOnlyBefore, render_only_digest_after = renderOnlyAfter,
            invariants = new { reload_digest = reloadDigest, replay_expected_digest = replayExpected, replay_actual_digest = replayActual, ticks = 3 },
            artifacts = captures, warnings = new[] { "CPU scope maxima are overlapping, not additive GPU timings.", "GPU viewport timestamps may be delayed; zero results are unavailable, never zero cost.", "Presentation light fixed to 420 seconds; shader TIME wind is unpinned, so byte-identical image reproduction is not claimed.", "20 minute resource soak and exhaustive growth/death event bursts require follow-up; this run cannot establish those gates." }
        };
        // Fail on collisions; launcher makes run directories unique and never overwrites evidence.
        using var stream = new FileStream(Path.Combine(OutDir, "render_report.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, report, new JsonSerializerOptions { WriteIndented = true });
        Check("frozen render-only digest invariant", renderOnlyBefore == renderOnlyAfter);
        Check("terrain gravel cache audit exact", terrainCache.differences == 0);
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_AUDIT_TERRAIN_CACHE") == "1")
            Check("terrain gravel cache audit executed", terrainCache.audited_pixels > 0 && terrainCache.reuses > 0);
        if (!evolves && scenario != "sculpt") Check("measurement digest invariant", before == after);
        Check("landscape captures available", DisplayServer.GetName() != "headless" && captureIndex > 0);
    }

    private async Task SettleSurfaceDetailsAsync()
    {
        ulong start = Time.GetTicksMsec();
        while (Session.AmbientGround.RefreshPending || Session.SoilDetail.LoamDetails.PendingPatchCount > 0)
        {
            if (Time.GetTicksMsec() - start > 180_000)
                throw new TimeoutException("Static surface detail did not finish publishing before capture.");
            await Frames(1);
        }
    }

    private async Task AuditSurfaceCacheAsync()
    {
        bool paused = W.Clock.Paused;
        bool sessionProcessing = Session.IsProcessing();
        try
        {
            await SettleSurfaceDetailsAsync();
            int ambient = Session.AmbientGround.RefreshCount;
            int loam = Session.SoilDetail.LoamDetails.RebuildCount;
            long tick = W.Clock.Tick;
            // The timed live fixture orbits. Here the camera stays still while ecology
            // continues, so ordinary revisions cannot masquerade as view streaming.
            await Frames(120);
            Check("surface cache stays idle without edits", ambient == Session.AmbientGround.RefreshCount && loam == Session.SoilDetail.LoamDetails.RebuildCount);
            if (!paused) Check("idle surface audit keeps ecology running", W.Clock.Tick > tick);
            W.Clock.Paused = true;
            // A paused scheduler can still drain previously owed ticks. Freeze the
            // host after completing its partial tick; retain the remaining backlog.
            Session.SetProcess(false);
            W.Scheduler.CompletePendingTick();
            W.Liquid.CompletePending();
            string digest = WorldSerializer.Digest(W);
            var eye = Session.CameraRig.Position;
            var target = eye - Session.CameraRig.GlobalBasis.Z * 3;
            Session.CameraRig.LookAtPoint(eye + Vector3.Right * 3, target);
            await Frames(2); await SettleSurfaceDetailsAsync();
            int visitedBuilds = Session.AmbientGround.BankPatchBuildCount;
            Session.CameraRig.LookAtPoint(eye, target);
            await Frames(2); await SettleSurfaceDetailsAsync();
            Check("returning to cached bank patches does not rebuild", visitedBuilds == Session.AmbientGround.BankPatchBuildCount);
            Session.AmbientGround.Invalidate(); Session.SoilDetail.LoamDetails.Invalidate();
            await Frames(20);
            Session.AmbientGround.Invalidate(); Session.SoilDetail.LoamDetails.Invalidate();
            await SettleSurfaceDetailsAsync();
            Check("surface invalidation publishes replacement", Session.AmbientGround.RefreshCount > ambient && Session.SoilDetail.LoamDetails.RebuildCount > loam);
            Check("surface cache work preserves world", digest == WorldSerializer.Digest(W));
            if (W.Flora.Items.FirstOrDefault() is { } plant)
            {
                Session.Tools.ShowPendingPlant(plant.Id);
                Check("pending plant ghost appears immediately", Session.Tools.PendingPlantGhostCount == 1);
                await Frames(2);
                Check("published plant removes pending ghost", Session.Tools.PendingPlantGhostCount == 0);
            }
            File.WriteAllText(Path.Combine(OutDir, "surface-event-audit.json"), JsonSerializer.Serialize(new {
                ambient_before = ambient, ambient_after = Session.AmbientGround.RefreshCount,
                loam_before = loam, loam_after = Session.SoilDetail.LoamDetails.RebuildCount,
                ambient_instances = Session.AmbientGround.LastGroundcoverInstances,
                bank_patch_builds = Session.AmbientGround.BankPatchBuildCount,
                same_world = digest == WorldSerializer.Digest(W)
            }));
        }
        finally { W.Clock.Paused = paused; Session.ResetFrameClock(); Session.SetProcess(sessionProcessing); }
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

    public static (bool Valid, string Error) ValidateCameraRig(CameraRig rig)
    {
        if (rig == null) return (false, "CameraRig is null");
        if (rig.Cam == null) return (false, "Camera3D is null");
        var xf = rig.Cam.GlobalTransform;
        if (!float.IsFinite(xf.Origin.X) || !float.IsFinite(xf.Origin.Y) || !float.IsFinite(xf.Origin.Z))
            return (false, $"Camera position is not finite: {xf.Origin}");
        var det = xf.Basis.Determinant();
        if (!float.IsFinite(det) || Mathf.Abs(det) < 1e-5f)
            return (false, $"Camera basis is singular or not finite: det={det}");
        return (true, string.Empty);
    }

    public static (bool Valid, string Error) ValidateLightRig(EnvironmentRig rig)
    {
        if (rig == null) return (false, "EnvironmentRig is null");
        if (rig.Sun != null)
        {
            var sdet = rig.Sun.GlobalTransform.Basis.Determinant();
            if (!float.IsFinite(sdet) || Mathf.Abs(sdet) < 1e-5f)
                return (false, $"Sun light basis is singular or not finite: det={sdet}");
        }
        if (rig.Fill != null)
        {
            var fdet = rig.Fill.GlobalTransform.Basis.Determinant();
            if (!float.IsFinite(fdet) || Mathf.Abs(fdet) < 1e-5f)
                return (false, $"Fill light basis is singular or not finite: det={fdet}");
        }
        return (true, string.Empty);
    }
}
