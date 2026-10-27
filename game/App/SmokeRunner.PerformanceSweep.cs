using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    // Diagnostic interventions only. Never applies changes to normal gameplay defaults.
    private async Task LandscapeEffectSweepAsync()
    {
        var rig = Session.EnvRig;
        var viewport = GetViewport();
        var rid = viewport.GetViewportRid();
        bool wasPaused = W.Clock.Paused;
        bool sessionProcessing = Session.IsProcessing();
        W.Clock.Paused = true;
        Session.SetProcess(false);
        W.Scheduler.CompletePendingTick(); W.Liquid.CompletePending();
        string before = WorldSerializer.Digest(W);
        string after = before;
        var rows = new List<object>();
        string? sweepKind = System.Environment.GetEnvironmentVariable("VIVARIUM_BENCHMARK_SWEEP_KIND");
        bool orbitSweep = sweepKind == "orbit";
        bool fixedConfirmation = sweepKind == "fixed60";
        var poses = BuildReferenceScenes().Where(s => s.Name == "overview" || (!orbitSweep && s.Name == "ground_vegetation")).ToArray();
        int quality = Session.Settings.Quality;
        int shadowMaxTier = Session.Flora.ShadowMaxTier;
        var coverageShadows = Session.Coverage.GetChildren().OfType<GeometryInstance3D>().ToDictionary(g => g, g => g.CastShadow);
        var floraShadows = new Dictionary<GeometryInstance3D, GeometryInstance3D.ShadowCastingSetting>();
        void FindFlora(Node node) { foreach (var child in node.GetChildren()) { if (child is GeometryInstance3D g) floraShadows[g] = g.CastShadow; FindFlora(child); } }
        FindFlora(Session.Flora);
        bool fillShadows = rig.Fill.ShadowEnabled;
        string? priorCacheControl = System.Environment.GetEnvironmentVariable("VIVARIUM_TERRAIN_CACHE_BASELINE");
        bool filterSweep = sweepKind == "filter";
        string? confirm = System.Environment.GetEnvironmentVariable("VIVARIUM_BENCHMARK_CONFIRM_FEATURE");
        bool controlsSweep = sweepKind == "controls";
        bool reduced = Session.Settings.ReducedDistantPlants, distanceBlur = Session.Settings.DistanceBlur,
            simpleLighting = Session.Settings.SimpleFoliageLighting, fineIndirectLighting = Session.Settings.FineIndirectLighting;
        double renderScale = Session.Settings.RenderScale;
        float exposureSpeed = rig.CameraAttributes.AutoExposureSpeed;
        int giCascades = rig.Env.SdfgiCascades;
        bool giOcclusion = rig.Env.SdfgiUseOcclusion;
        float giCellSize = rig.Env.SdfgiMinCellSize, giMaxDistance = rig.Env.SdfgiMaxDistance;
        void RestoreGiLayout()
        {
            if (rig.Env.SdfgiCascades != giCascades) rig.Env.SdfgiCascades = giCascades;
            if (!Mathf.IsEqualApprox(rig.Env.SdfgiMinCellSize, giCellSize)) rig.Env.SdfgiMinCellSize = giCellSize;
        }
        double distanceLodScale = Session.Flora.DistanceLodScale;
        using var gillTrial = new Vivarium.Game.Render.FloraRenderer.GillTrial();
        bool gillSweep = confirm is "far_gills_3" or "far_gills_9";
        bool emberSweep = confirm is "ember_micro_3" or "ember_micro_5";
        bool singleCandidateSweep = confirm is "lod_distance_5_10" or "filter_low" or "render_95" or "far_gills_3" or "far_gills_9" or "ember_micro_3" or "ember_micro_5" or "sdfgi_off" or "gi_coarse_cells" or "gi_fine_cells" or "gi_occlusion_off" or "groundcover_off";
        bool contributionAudit = confirm == "geometry_attribution";
        if (singleCandidateSweep && !orbitSweep)
            poses = BuildReferenceScenes().Where(s => s.Name is "overview" or "ground_vegetation" or "water_margin" or "dense_colony").ToArray();
        if (contributionAudit)
            poses = BuildReferenceScenes().Where(s => s.Name is "overview" or "ground_vegetation" or "water_margin" or "dense_colony" or "macro_flora").ToArray();
        if ((gillSweep || emberSweep) && !orbitSweep)
            poses = BuildReferenceScenes().Where(s => s.Name is "overview" or "ground_vegetation" or "water_margin" or "dense_colony" or "macro_flora").ToArray();
        if (fixedConfirmation) poses = BuildReferenceScenes().Where(s => s.Name == "ground_vegetation").ToArray();
        var phases = orbitSweep ? confirm == "lod_options"
            ? new[] { "original_start", "reduced_lod", "lod_dof", "original_end" }
            : new[] { "original_start", confirm ?? "candidate", "original_end" } : singleCandidateSweep
            ? new[] { "reference_start", confirm!, "reference_end" } : controlsSweep
            ? new[] { "reference_start", "reduced_lod", "lod_dof", "simple_lighting", "render_75", "reference_end" } : filterSweep
            ? new[] { "reference_start", "filter_high", "filter_medium", "filter_high_repeat", "reference_end" }
            : new[]
            {
                "reference_start",
                "flora_shadow_off",
                "other_shadows_off",
                "far_flora_shadow_off",
                "small_flora_shadow_off",
                "visible_flora_off",
                "ground_detail_off",
                "water_coverage_off",
                "ssao_off",
                "sdfgi_off",
                "fog_off",
                "sun_shadow_off",
                "reference_end"
            };
        if (contributionAudit) phases = new[] { "reference" };
        bool sunShadows = rig.Sun.ShadowEnabled, ssao = rig.Env.SsaoEnabled, ssil = rig.Env.SsilEnabled;
        bool sdfgi = rig.Env.SdfgiEnabled, volumeFog = rig.Env.VolumetricFogEnabled;
        bool environmentProcessing = rig.IsProcessing();
        var roots = new Node3D[] { Session.Flora, Session.SoilDetail, Session.AmbientGround, Session.WaterFluid, Session.Coverage };
        var visibility = roots.ToDictionary(n => n, n => n.Visible);
        try
        {
            if (gillSweep)
            {
                foreach (bool _ in Session.Flora.PrepareGillTrial(gillTrial, confirm == "far_gills_9" ? 9 : 3)) await Frames(1);
                Check("far gill trial prepares complete replacements", gillTrial.Meshes > 0);
                File.WriteAllText(Path.Combine(OutDir, "far-gill-audit.json"), JsonSerializer.Serialize(gillTrial.Records));
            }
            if (emberSweep)
            {
                foreach (bool _ in Session.Flora.PrepareEmbercupTrial(gillTrial, confirm == "ember_micro_5" ? 5 : 3)) await Frames(1);
                Check("far Embercup trial prepares complete replacements", gillTrial.Meshes > 0);
                File.WriteAllText(Path.Combine(OutDir, "far-embercup-audit.json"), JsonSerializer.Serialize(gillTrial.Records));
            }
            // Day/night processing otherwise overwrites the fog intervention every frame.
            rig.SetProcess(false);
            RenderingServer.ViewportSetMeasureRenderTime(rid, true);
            foreach (var pose in poses)
            foreach (string phase in phases)
            {
                Session.CameraRig.LookAtPoint(pose.Eye, pose.Target);
                if (orbitSweep) Session.CameraRig.LookAtPoint(new Vector3(0, 3.5f, 9), Vector3.Zero);
                Session.Settings.Quality = quality;
                Session.Settings.ReducedDistantPlants = reduced;
                Session.Settings.DistanceBlur = distanceBlur;
                Session.Settings.SimpleFoliageLighting = simpleLighting;
                Session.Settings.FineIndirectLighting = fineIndirectLighting;
                Session.Settings.RenderScale = renderScale;
                Session.Flora.DistanceLodScale = distanceLodScale;
                Session.ApplySettings();
                RestoreGiLayout();
                if (rig.Env.SdfgiUseOcclusion != giOcclusion) rig.Env.SdfgiUseOcclusion = giOcclusion;
                gillTrial.Select(false);
                foreach (var entry in visibility) entry.Key.Visible = entry.Value;
                foreach (var entry in coverageShadows) entry.Key.CastShadow = entry.Value;
                foreach (var entry in floraShadows) entry.Key.CastShadow = entry.Value;
                Session.Flora.ShadowMaxTier = shadowMaxTier;
                rig.Fill.ShadowEnabled = fillShadows;
                rig.Sun.ShadowEnabled = sunShadows;
                rig.Env.SsaoEnabled = ssao;
                rig.Env.SsilEnabled = ssil;
                rig.Env.SdfgiEnabled = sdfgi;
                rig.Env.VolumetricFogEnabled = volumeFog;
                System.Environment.SetEnvironmentVariable("VIVARIUM_TERRAIN_CACHE_BASELINE", phase == "terrain_uncached" ? "1" : null);
                switch (phase)
                {
                    case "far_gills_3":
                    case "far_gills_9":
                    case "ember_micro_3":
                    case "ember_micro_5": gillTrial.Select(true); break;
                    case "candidate": Session.Flora.ShadowMaxTier = 1; break;
                    case "reduced_lod":
                        Session.Settings.ReducedDistantPlants = true; Session.ApplySettings(); break;
                    case "lod_distance_5_10":
                        Session.Flora.DistanceLodScale = 5.0 / 6.0; break;
                    case "lod_dof":
                        Session.Settings.ReducedDistantPlants = true; Session.Settings.DistanceBlur = true; Session.ApplySettings(); break;
                    case "simple_lighting":
                        Session.Settings.SimpleFoliageLighting = true; Session.ApplySettings(); break;
                    case "render_75":
                        Session.Settings.RenderScale = .75; Session.ApplySettings(); break;
                    case "render_95":
                        Session.Settings.RenderScale = .95; Session.ApplySettings(); break;
                    case "filter_high":
                    case "filter_high_repeat":
                        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
                        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftHigh);
                        break;
                    case "filter_medium":
                        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
                        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
                        break;
                    case "filter_low":
                        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
                        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
                        break;
                    case "far_flora_shadow_off":
                    case "far_flora_shadow_off_repeat": Session.Flora.ShadowMaxTier = 1; break;
                    case "small_flora_shadow_off": Session.Flora.ShadowMaxTier = 0; break;
                    case "crust_shadow_off":
                        foreach (var g in coverageShadows.Keys.Where(g => g.Name.ToString().StartsWith("Coverage_Crust_"))) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        break;
                    case "coverage_shadow_off":
                        foreach (var g in coverageShadows.Keys) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        break;
                    case "flora_shadow_off":
                        foreach (var g in floraShadows.Keys) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        Session.Flora.ShadowMaxTier = -1;
                        break;
                    case "other_shadows_off":
                        foreach (var g in coverageShadows.Keys) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                        rig.Fill.ShadowEnabled = false;
                        break;
                    case "visible_flora_off":
                        Session.Flora.Visible = false;
                        break;
                    case "groundcover_off":
                        Session.AmbientGround.Visible = false;
                        break;
                    case "ground_detail_off":
                        Session.SoilDetail.Visible = false;
                        Session.AmbientGround.Visible = false;
                        break;
                    case "water_coverage_off":
                        Session.WaterFluid.Visible = false;
                        Session.Coverage.Visible = false;
                        break;
                    case "ssao_off": rig.Env.SsaoEnabled = false; break;
                    case "ssil_off": rig.Env.SsilEnabled = false; break;
                    case "ssr_off": rig.Env.SsrEnabled = false; break;
                    case "sdfgi_off": rig.Env.SdfgiEnabled = false; break;
                    case "gi_occlusion_off": rig.Env.SdfgiUseOcclusion = false; break;
                    case "gi_coarse_cells":
                    case "gi_fine_cells":
                        bool fine = phase == "gi_fine_cells";
                        rig.ApplyIndirectLightingDetail(fine);
                        Check("GI detail preserves reach: " + pose.Name,
                            rig.Env.SdfgiCascades == (fine ? 4 : 3)
                            && Mathf.IsEqualApprox(rig.Env.SdfgiMinCellSize, fine ? .025f : .05f)
                            && Mathf.IsEqualApprox(rig.Env.SdfgiMaxDistance, giMaxDistance));
                        break;
                    case "fog_off": rig.Env.VolumetricFogEnabled = false; break;
                    case "msaa_off": viewport.Msaa3D = Viewport.Msaa.Disabled; break;
                    case "sun_shadow_off": rig.Sun.ShadowEnabled = false; break;
                    case "fill_shadow_off": rig.Fill.ShadowEnabled = false; break;
                    case "shadow_4096": RenderingServer.DirectionalShadowAtlasSetSize(4096, true); break;
                }
                if (confirm is "gi_coarse_cells" or "gi_fine_cells" or "gi_occlusion_off")
                {
                    // Give every phase the same initial GI history; the initial trace
                    // otherwise leaves only the first reference with warmed cascades.
                    bool enabled = rig.Env.SdfgiEnabled;
                    rig.Env.SdfgiEnabled = false;
                    await Frames(2);
                    rig.Env.SdfgiEnabled = enabled;
                }
                // Publish the chosen tiers at this pose before timing their cost.
                await Frames(2);
                await SettleSurfaceDetailsAsync();
                Session.Flora.Rebuild(resetDetailHistory: true);
                // Match the production exposure state before sampling a new view.
                // The reference route restores the actual adaptation speed in finally.
                await SettleExposureAsync();
                await Seconds(5);
                FrameProfiler.Reset();
                var gpu = new List<double>();
                var wall = new List<double>();
                var elapsedSamples = new List<double>();
                var gpuElapsedSamples = new List<double>();
                ulong start = Time.GetTicksUsec(), previous = start;
                while (Time.GetTicksUsec() - start < (orbitSweep || fixedConfirmation ? 60_000_000UL : 5_000_000UL))
                {
                    if (orbitSweep)
                    {
                        float angle = (float)((Time.GetTicksUsec() - start) / 1_000_000.0) * .3f;
                        Session.CameraRig.LookAtPoint(new Vector3(Mathf.Sin(angle) * 9, 3.5f, Mathf.Cos(angle) * 9), Vector3.Zero);
                    }
                    await Frames(1);
                    ulong now = Time.GetTicksUsec();
                    wall.Add((now - previous) / 1000.0); previous = now;
                    elapsedSamples.Add((now - start) / 1_000_000.0);
                    double ms = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
                    if (double.IsFinite(ms) && ms > 0)
                    {
                        gpu.Add(ms);
                        gpuElapsedSamples.Add((now - start) / 1_000_000.0);
                    }
                }
                rows.Add(new { scene = pose.Name, phase, quality, gi_cascades = rig.Env.SdfgiCascades, gi_cell_size = rig.Env.SdfgiMinCellSize, gi_max_distance = rig.Env.SdfgiMaxDistance, gi_first_cascade_distance = rig.Env.SdfgiCascade0Distance, gi_enabled = rig.Env.SdfgiEnabled, gi_occlusion = rig.Env.SdfgiUseOcclusion, gpu_samples_ms = gpu, gpu_elapsed_samples_s = gpuElapsedSamples, gpu_mean_ms = gpu.Count > 0 ? (double?)gpu.Average() : null, wall_samples_ms = wall, elapsed_samples_s = elapsedSamples, orbit_radians_per_second = orbitSweep ? .3 : 0, wall_mean_ms = wall.Average(), draw_calls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame), flora_triangles = Session.Flora.TrianglesDrawn, render_scale = viewport.Scaling3DScale, reduced_lod = Session.Flora.ReducedDistantPlants, distance_lod_scale = Session.Flora.DistanceLodScale, distance_blur = Session.CameraRig.DistanceBlur, simple_lighting = Session.Settings.SimpleFoliageLighting, cpu_scope_per_frame_ms = FrameProfiler.PerFrameDurations(wall.Count) });
                if (contributionAudit)
                {
                    // Outside the timed sample: reconcile the species/tier breakdown
                    // against native surface metadata, not only the aggregate estimator.
                    var contributions = Session.Flora.AuditTriangleContributions();
                    long total = contributions.Sum(c => c.Triangles), native = Session.Flora.AuditPublishedTriangles();
                    Check("flora species contributions reconcile with native metadata: " + pose.Name,
                        total == native && total == Session.Flora.TrianglesDrawn);
                    File.WriteAllText(Path.Combine(OutDir, "flora-contributions-" + pose.Name + ".json"),
                        JsonSerializer.Serialize(new { total, native, rows = contributions }));
                }
                GD.Print($"PERF_EFFECT {phase} gpu_ms={(gpu.Count > 0 ? gpu.Average() : 0):0.000} wall_ms={wall.Average():0.000}");
                await Screenshot("effect_" + pose.Name + "_" + phase);
                File.WriteAllText(Path.Combine(OutDir, "gpu-effect-progress.json"), JsonSerializer.Serialize(rows));
            }
            after = WorldSerializer.Digest(W);
        }
        finally
        {
            gillTrial.Select(false);
            RestoreGiLayout();
            if (rig.Env.SdfgiUseOcclusion != giOcclusion) rig.Env.SdfgiUseOcclusion = giOcclusion;
            W.Clock.Paused = wasPaused;
            Session.ResetFrameClock();
            Session.SetProcess(sessionProcessing);
            Session.Settings.ReducedDistantPlants = reduced;
            Session.Settings.DistanceBlur = distanceBlur;
            Session.Settings.SimpleFoliageLighting = simpleLighting;
            Session.Settings.FineIndirectLighting = fineIndirectLighting;
            Session.Settings.RenderScale = renderScale;
            Session.Flora.DistanceLodScale = distanceLodScale;
            Session.ApplySettings();
            foreach (var entry in visibility) entry.Key.Visible = entry.Value;
            Session.Flora.ShadowMaxTier = shadowMaxTier;
            foreach (var entry in coverageShadows) if (GodotObject.IsInstanceValid(entry.Key)) entry.Key.CastShadow = entry.Value;
            foreach (var entry in floraShadows) if (GodotObject.IsInstanceValid(entry.Key)) entry.Key.CastShadow = entry.Value;
            rig.Fill.ShadowEnabled = fillShadows;
            rig.Sun.ShadowEnabled = sunShadows;
            rig.Env.SsaoEnabled = ssao;
            rig.Env.SsilEnabled = ssil;
            rig.Env.SdfgiEnabled = sdfgi;
            rig.Env.VolumetricFogEnabled = volumeFog;
            rig.SetProcess(environmentProcessing);
            System.Environment.SetEnvironmentVariable("VIVARIUM_TERRAIN_CACHE_BASELINE", priorCacheControl);
            RenderingServer.ViewportSetMeasureRenderTime(rid, false);
        }
        File.WriteAllText(Path.Combine(OutDir, "gpu-effect-sweep.json"), JsonSerializer.Serialize(new { rows, digest_before = before, digest_after = after, limitation = fixedConfirmation ? "Matched 60-second fixed ground view; explicit exposure and five-second settling; asynchronous GPU measurements; check reference drift." : orbitSweep ? "Matched 60-second orbits in one frozen world; five-second settle per phase; asynchronous GPU samples; check restored reference drift." : "Fixed views; five-second samples after explicit exposure settling and five-second settle; baseline drift must be checked; diagnostic settings only." }, new JsonSerializerOptions { WriteIndented = true }));
        Check("effect sweep preserves simulation", before == after);
        Check("effect sweep restores exposure adaptation speed", rig.CameraAttributes.AutoExposureSpeed == exposureSpeed);
        Check("effect sweep restores indirect lighting", rig.Env.SdfgiEnabled == sdfgi);
        Check("effect sweep restores GI occlusion", rig.Env.SdfgiUseOcclusion == giOcclusion);
        Check("effect sweep restores indirect lighting layout", rig.Env.SdfgiCascades == giCascades
            && Mathf.IsEqualApprox(rig.Env.SdfgiMinCellSize, giCellSize)
            && Mathf.IsEqualApprox(rig.Env.SdfgiMaxDistance, giMaxDistance));
    }
}
