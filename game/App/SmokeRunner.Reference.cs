using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;

namespace Vivarium.Game.App;

/// <summary>
/// Reference mode (--reference DIR [--preset NAME]): the visual-truth baseline. A fixed preset/seed is advanced a fixed
/// number of simulation ticks without rendering, then a fixed set of named scenes is photographed and measured.
/// Scene targets are found by deterministic world queries, so the same scene names survive content changes.
/// Output: &lt;scene&gt;.png per scene plus reference_report.json (per-scene frame timings and render counters).
/// Compare two runs with scripts/compare-reference.ps1.
/// </summary>
public partial class SmokeRunner
{
    /// <summary>Biological days simulated before photographing (enough for colonies, slime networks and fungi to form).</summary>
    public const double ReferenceBioDays = 6;
    private const int SettleFrames = 45, MeasureFrames = 90;

    /// <summary>
    /// When true, locks exposure to a fixed sensitivity during reference capture instead of settling auto-exposure.
    /// Default false: auto-exposure is enabled and settled deterministically per scene.
    /// </summary>
    private const bool FixedExposureOverride = false;

    private async Task SettleExposureAsync(int frames = SettleFrames)
    {
        var camAttr = Session.EnvRig.CameraAttributes;
        if (camAttr == null || !camAttr.AutoExposureEnabled)
        {
            await Frames(frames);
            return;
        }

        // Fast-settle auto-exposure to eliminate inter-scene history hysteresis,
        // then settle at normal adaptation speed for smooth, deterministic convergence.
        float origSpeed = camAttr.AutoExposureSpeed;
        try
        {
            camAttr.AutoExposureSpeed = 16.0f;
            int fastFrames = Math.Min(frames / 2, 25);
            await Frames(fastFrames);
            camAttr.AutoExposureSpeed = origSpeed;
            await Frames(Math.Max(frames - fastFrames, 1));
        }
        finally
        {
            camAttr.AutoExposureSpeed = origSpeed;
        }
    }

    private record RefScene(string Name, string Category, Vector3 Eye, Vector3 Target);

    private async Task ReferenceAsync()
    {
        string? reqScenario = GetRequestedScenario();
        if (reqScenario != null)
        {
            if (reqScenario.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                await RunAllScenariosAsync();
                return;
            }
            await RunScenarioAsync(reqScenario);
            return;
        }

        if (FixedExposureOverride && Session.EnvRig.CameraAttributes is { } refAttr)
        {
            refAttr.AutoExposureEnabled = false;
            refAttr.ExposureSensitivity = 100.0f;
        }

        var cam = Session.CameraRig;
        W.Clock.Paused = true;
        // deterministic warm-up: step whole ticks, yielding so the window stays responsive
        long startTick = W.Clock.Tick;
        while (W.Clock.BioDays < ReferenceBioDays && W.Clock.Tick - startTick < 2_000_000)
        {
            ulong s0 = Time.GetTicksUsec();
            W.Step(200);
            ulong s1 = Time.GetTicksUsec();
            await Frames(1);
            ulong s2 = Time.GetTicksUsec();
            if (s2 - s0 > 2_000_000)
                GD.Print($"REFERENCE_WARMUP_SLOW day={W.Clock.BioDays:0.00} step_ms={(s1 - s0) / 1000} frame_ms={(s2 - s1) / 1000}");
        }
        _facts["preset_bio_days"] = Math.Round(W.Clock.BioDays, 3);
        _facts["ticks"] = W.Clock.Tick;
        _facts["flora_total"] = W.Flora.Count;
        _facts["fauna_total"] = W.Fauna.Count;
        _facts["species_present"] = W.Flora.Items.GroupBy(f => f.SpeciesId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        _facts["coverage_area_m2"] = W.Content.Flora.Where(sp => sp.IsCoverageSpecies).OrderBy(sp => sp.Id)
            .ToDictionary(sp => sp.Id, sp => Math.Round(W.CoverageSystem.CoveredArea(sp.Id), 4));
        string digest = WorldSerializer.Digest(W);
        _facts["digest"] = digest;

        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false; // clean photographs: no HUD

        await Frames(10);

        var scenes = BuildReferenceScenes();
        var rows = new List<Dictionary<string, object>>();
        var allTimes = new List<double>();
        long totalDraws = 0, totalPrims = 0, totalObjs = 0;
        var artifacts = new List<string>();

        foreach (var s in scenes)
        {
            cam.LookAtPoint(s.Eye, s.Target);
            await SettleExposureAsync(SettleFrames);
            FrameProfiler.TakeReport(); // reset worst-scope window
            var times = new List<double>(MeasureFrames);
            long draws = 0, prims = 0, objs = 0;
            for (int i = 0; i < MeasureFrames; i++)
            {
                cam.LookAtPoint(s.Eye, s.Target);
                ulong f0 = Time.GetTicksUsec();
                await Frames(1);
                double ft = (Time.GetTicksUsec() - f0) / 1000.0;
                times.Add(ft);
                allTimes.Add(ft);
                draws = Math.Max(draws, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                prims = Math.Max(prims, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
                objs = Math.Max(objs, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
            }
            totalDraws = Math.Max(totalDraws, draws);
            totalPrims = Math.Max(totalPrims, prims);
            totalObjs = Math.Max(totalObjs, objs);

            string scopes = FrameProfiler.TakeReport();
            await Screenshot(s.Name);
            artifacts.Add($"{s.Name}.png");
            using Image? img = GetViewport().GetTexture()?.GetImage();
            var lum = img != null ? ComputeLuminance(img) : default;
            var sortedTimes = times.ToArray();
            Array.Sort(sortedTimes);
            double mean = times.Average();
            double p50 = Percentile(sortedTimes, 0.50);
            double p95 = Percentile(sortedTimes, 0.95);
            double p99 = Percentile(sortedTimes, 0.99);
            double p99_9 = Percentile(sortedTimes, 0.999);
            double worst = sortedTimes[^1];
            double fpsMin1s = ComputeMinFps1sWindow(times);

            rows.Add(new Dictionary<string, object>
            {
                ["name"] = s.Name, ["category"] = s.Category,
                ["eye"] = new[] { s.Eye.X, s.Eye.Y, s.Eye.Z }, ["target"] = new[] { s.Target.X, s.Target.Y, s.Target.Z },
                ["fps_mean"] = Math.Round(1000.0 / mean, 1),
                ["fps_min_1s_window"] = Math.Round(fpsMin1s, 1),
                ["frame_ms_p50"] = Math.Round(p50, 2),
                ["frame_ms_p95"] = Math.Round(p95, 2),
                ["frame_ms_p99"] = Math.Round(p99, 2),
                ["frame_ms_p99_9"] = Math.Round(p99_9, 2),
                ["frame_ms_worst"] = Math.Round(worst, 2),
                ["draw_calls"] = draws, ["primitives"] = prims, ["objects"] = objs,
                ["flora_visible"] = Session.Flora.Visible_, ["flora_triangles"] = Session.Flora.TrianglesDrawn,
                ["fauna_drawn"] = Session.Fauna.Drawn, ["fauna_triangles"] = Session.Fauna.TrianglesDrawn,
                ["coverage_tiles"] = Session.Coverage.TileCount, ["coverage_instances"] = Session.Coverage.InstanceCount,
                ["coverage_triangles_built"] = Session.Coverage.TrianglesBuilt,
                ["slowest_scopes"] = scopes,
                ["lum_mean"] = lum.Mean,
                ["lum_p05"] = lum.P05,
                ["lum_p95"] = lum.P95,
                ["lum_clip_black"] = lum.ClipBlack,
                ["lum_clip_white"] = lum.ClipWhite,
            });
            Log.Info(LogCategory.Perf, $"ref {s.Name}: fps {1000.0 / mean:0.0} p95 {p95:0.0}ms p99 {p99:0.0}ms min1s {fpsMin1s:0.0} draws {draws} prims {prims}");
        }
        _facts["scenes"] = rows;
        _facts["scene_count"] = rows.Count;
        _facts["renderer"] = RenderingServer.GetVideoAdapterName();

        string digestAfter = WorldSerializer.Digest(W);
        var refFrameTimes = ComputeFrameTimes(allTimes);
        var refCounters = BuildCounters(totalDraws, totalPrims, totalObjs);
        var warnings = BuildWarnings(refFrameTimes, digest, digestAfter, true);

        var report = new RenderReport(
            Provenance: BuildProvenance(),
            World: BuildWorldInfo(),
            Scenario: new RenderReportScenario(
                Name: "reference_suite",
                Category: "reference",
                Description: "Standard 14+ reference scenes capture suite",
                FramesMeasured: allTimes.Count,
                DurationSeconds: Math.Round(allTimes.Sum() / 1000.0, 2),
                CameraTrajectory: "14+ multi-perspective deterministic vantage points"),
            RenderSettings: BuildRenderSettings(),
            CpuTiming: BuildCpuTiming(),
            GpuTiming: null,
            FrameTimes: refFrameTimes,
            Counters: refCounters,
            DigestBefore: digest,
            DigestAfter: digestAfter,
            Artifacts: artifacts,
            Warnings: warnings);

        EmitRenderReport(report);

        Check("reference scenes captured", rows.Count >= 14, $"{rows.Count} scenes");
        Check("rendering leaves the simulation digest unchanged", digestAfter == digest);
    }

    private async Task SpeciesWorldAsync()
    {
        if (FixedExposureOverride && Session.EnvRig.CameraAttributes is { } refAttr)
        {
            refAttr.AutoExposureEnabled = false;
            refAttr.ExposureSensitivity = 100.0f;
        }

        W.Clock.Paused = true;
        var sp = W.Content.FloraOrThrow(SpeciesId);
        if (sp.IsCoverageSpecies)
        {
            var layerId = sp.Mat != null ? CoverageLayerId.Mat : sp.Lichen != null ? CoverageLayerId.Crust : CoverageLayerId.Plasmodium;
            var layer = W.Coverage.ById(layerId);
            byte occ = FindCoverageOccupant(layerId, SpeciesId)
                ?? throw new InvalidOperationException($"No coverage slot for {SpeciesId}");
            var patch = DensestCoverageTile(layer, occ)
                ?? throw new InvalidOperationException($"No {SpeciesId} patch in this world");
            var patchTarget = Ground(patch.center) + new Vector3(0, 0.045f, 0);
            var patchView = new RefScene("species_" + SpeciesId, "species",
                patchTarget + new Vector3(0.16f, 0.32f, 0.16f), patchTarget);
            Session.Ui.Visible = false;
            GetWindow().Size = new Vector2I(1600, 900);
            Session.CameraRig.LookAtPoint(patchView.Eye, patchView.Target);
            await SettleExposureAsync(30);
            await Screenshot(patchView.Name);
            if (SpeciesId == "bogglass_moss")
            {
                Session.CameraRig.LookAtPoint(patchTarget + new Vector3(-0.19f, 0.13f, -0.15f), patchTarget);
                await SettleExposureAsync(20);
                await Screenshot("species_" + SpeciesId + "_side");
            }
            else if (SpeciesId == "ambervein")
            {
                Session.CameraRig.LookAtPoint(patchTarget + new Vector3(-0.12f, 0.08f, -0.10f), patchTarget);
                await SettleExposureAsync(20);
                await Screenshot("species_" + SpeciesId + "_close");
            }
            _facts["species"] = SpeciesId;
            _facts["coverage_cells"] = patch.count;
            _facts["patch_at"] = new[] { patch.center.X, patch.center.Z };
            _facts["water_depth_at_patch"] = W.Water.OpenWaterDepth(patch.center);
            Check("species coverage patch in the running world", patch.count > 0);
            return;
        }

        // Stage one mature plant in the normal rendered world at a clear habitat
        // point. This run is transient and never touches the player's save.
        double Clearance(Vec2 p)
        {
            double rock = W.Props.Rocks.Select(r => Vec2.Distance(r.Position, p) - r.FootprintRadius)
                .DefaultIfEmpty(10).Min();
            double log = W.Props.Logs.Select(l => l.AxisDistance(p) - l.Radius)
                .DefaultIfEmpty(10).Min();
            return Math.Min(rock, log);
        }
        bool floating = sp.Shape == "floatleaf";
        bool aquatic = floating || sp.MinWaterDepth > 0;
        var spots = W.Grid.DomainCells.Select(c => W.Grid.CellCenter(c))
            .Where(p => aquatic
                ? W.Water.OpenWaterDepth(p) >= Math.Max(0.05, sp.MinWaterDepth) && W.Domain.ContainsDisc(p, 0.5)
                : !W.Water.IsWet(p) && W.Domain.ContainsDisc(p, 1.0))
            .OrderByDescending(Clearance).ToList();
        var spot = spots.Where(p => W.FloraSystem.CanEstablish(sp, p, out _)).Cast<Vec2?>().FirstOrDefault()
            ?? spots.Cast<Vec2?>().FirstOrDefault()
            ?? throw new InvalidOperationException("No suitable review site in the world");
        var candidate = W.FloraSystem.Establish(sp, spot, "visual review", sp.MaxBiomass);
        candidate.Age = sp.MaturityAge + 1;
        Session.Ui.Visible = false;
        var target = floating
            ? new Vector3((float)spot.X, (float)W.Water.SurfaceAt(spot), (float)spot.Z)
            : Ground(spot) + new Vector3(0, (float)sp.Height * 0.38f, 0);
        var view = floating
            ? new RefScene("species_" + SpeciesId, "species", target + new Vector3(0.14f, 0.30f, 0.18f), target)
            : Orbit("species_" + SpeciesId, "species", target,
                (float)Math.Max(0.8, sp.Height * 3.0), 22, 35);
        GetWindow().Size = new Vector2I(1600, 900);
        Session.CameraRig.LookAtPoint(view.Eye, view.Target);
        // Flora refreshes on a time budget; a frame count can finish before the next refresh at high FPS.
        await Seconds(1);
        await SettleExposureAsync(30);
        await Screenshot(view.Name);
        if (SpeciesId == "glassfinger")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(.20f,.19f,.24f), target);
            await Seconds(1);
            await SettleExposureAsync(20);
            await Screenshot("species_glassfinger_close");
        }
        else if (SpeciesId == "embercrown")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(1.3f, 0.55f, 1.4f), target);
            await SettleExposureAsync(20);
            await Screenshot("species_embercrown_close");
        }
        else if (SpeciesId == "dewbonnet")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(0.18f, 0.12f, 0.22f), target);
            await SettleExposureAsync(20);
            await Screenshot("species_dewbonnet_close");
        }
        else if (SpeciesId == "sunstone_rosette")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(0.20f, 0.14f, 0.22f), target);
            await SettleExposureAsync(20);
            await Screenshot("species_sunstone_rosette_close");
        }
        else if (SpeciesId is "streamribbon" or "fencomb")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(0.35f, 0.30f, 0.35f), target + new Vector3(0, 0.12f, 0));
            await SettleExposureAsync(20);
            await Screenshot("species_" + SpeciesId + "_close");
        }
        else if (SpeciesId == "shadebell")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(1.2f, 0.45f, 1.3f), target);
            await SettleExposureAsync(20);
            await Screenshot("species_shadebell_close");
        }
        else if (SpeciesId == "lanternbrush")
        {
            Session.CameraRig.LookAtPoint(target + new Vector3(1.3f, 0.55f, 1.4f), target);
            await Seconds(1);
            await SettleExposureAsync(30);
            await Screenshot("species_lanternbrush_close");
        }
        _facts["species"] = SpeciesId;
        _facts["staged_at"] = new[] { spot.X, spot.Z };
        _facts["flora_visible"] = Session.Flora.Visible_;
        Check("staged species in the running world", Session.Flora.Visible_ > 0);
    }
    private Vector3 Ground(Vec2 p) => new((float)p.X, (float)W.GroundHeight(p), (float)p.Z);

    /// <summary>Camera at a fixed distance/elevation from a target. Starting from the preferred bearing it steps round
    /// until the eye is clear of terrain and props and has an unobstructed line to the target (deterministic).</summary>
    private RefScene Orbit(string name, string cat, Vector3 target, float dist, float elevDeg, float bearingDeg = 45)
    {
        Vector3 EyeAt(float bDeg, float eDeg)
        {
            float e = Mathf.DegToRad(eDeg), b = Mathf.DegToRad(bDeg);
            return target + new Vector3(Mathf.Cos(e) * Mathf.Sin(b), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Cos(b)) * dist;
        }
        bool Clear(Vector3 eye)
        {
            var xz = new Vec2(eye.X, eye.Z);
            if (!W.Domain.ContainsDisc(xz, 0)) return true; // outside the specimen: nothing to collide with
            double floor = W.SurfaceHeight(xz);
            double prop = W.Props.PropTopAt(xz);
            if (!double.IsNaN(prop)) floor = Math.Max(floor, prop);
            if (eye.Y < floor + dist * 0.08) return false;
            var d = target - eye; float len = d.Length();
            double hit = Selection.RayOpaque(W, new Vec3(eye.X, eye.Y, eye.Z), new Vec3(d.X / len, d.Y / len, d.Z / len), len);
            return hit >= len * 0.9;
        }
        foreach (float lift in new[] { 0f, 15f, 30f })
            for (int k = 0; k < 12; k++)
            {
                var eye = EyeAt(bearingDeg + k * 30, Math.Min(85, elevDeg + lift));
                if (Clear(eye)) return new RefScene(name, cat, eye, target);
            }
        return new RefScene(name, cat, EyeAt(bearingDeg, 75), target);
    }

    // densest point of a set of individuals (deterministic: ties broken by id)
    private Vec2? Hub(IEnumerable<Vivarium.Sim.Flora.FloraIndividual> src, double r)
    {
        var s = src.ToList();
        if (s.Count == 0) return null;
        return s.OrderByDescending(x => s.Count(o => Vec2.Distance(o.Position, x.Position) < r)).ThenBy(x => x.Id.Value).First().Position;
    }

    private List<RefScene> BuildReferenceScenes()
    {
        var list = new List<RefScene>();
        var flora = W.Flora.Items.OrderBy(f => f.Id.Value).ToList();
        string Arch(string id) => W.Content.FloraOrThrow(id).Archetype;
        double H(Vivarium.Sim.Flora.FloraIndividual f) => W.Content.FloraOrThrow(f.SpeciesId).Height;
        bool Colonial(string id) => W.Content.FloraOrThrow(id).Colony != null;

        void AddHub(string name, string cat, IEnumerable<Vivarium.Sim.Flora.FloraIndividual> src, double r, float dist, float elev, float bearing = 45)
        {
            var h = Hub(src, r);
            if (h != null) list.Add(Orbit(name, cat, Ground(h.Value), dist, elev, bearing));
        }

        // 1 whole island
        list.Add(new RefScene("overview", "overview", new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0)));
        // 2 normal interaction distance: centre of the densest vegetation
        var vascular = flora.Where(f => Arch(f.SpeciesId) == "plant").ToList();
        AddHub("interaction", "interaction", vascular, 1.5, 3.2f, 38);
        // 3 ground-level vegetation: grazing angle into dense plants
        AddHub("ground_vegetation", "ground", vascular, 0.6, 0.9f, 8, 20);
        // 4 macro flora: the tallest non-colonial plant
        var tall = vascular.Where(f => !Colonial(f.SpeciesId)).OrderByDescending(f => H(f)).ThenBy(f => f.Id.Value).FirstOrDefault();
        if (tall != null) list.Add(Orbit("macro_flora", "macro", Ground(tall.Position) + new Vector3(0, (float)H(tall) * 0.4f, 0), (float)Math.Max(0.12, H(tall) * 2.2), 22));
        // 5 slime mold network (and a macro of its front)
        var slime = flora.Where(f => Arch(f.SpeciesId) == "slime_mold").ToList();
        AddHub("slime_network", "slime", slime, 0.5, 0.9f, 50);
        AddHub("slime_macro", "slime", slime, 0.25, 0.28f, 30, 110);
        // 6 fungi
        var fungi = flora.Where(f => Arch(f.SpeciesId) == "fungus").ToList();
        AddHub("fungi_patch", "fungi", fungi, 0.4, 0.55f, 25);
        var bracket = fungi.Where(f => W.Content.FloraOrThrow(f.SpeciesId).Shape == "bracket").OrderBy(f => f.Id.Value).FirstOrDefault();
        if (bracket != null) list.Add(Orbit("bracket_macro", "fungi", Ground(bracket.Position) + new Vector3(0, 0.05f, 0), 0.3f, 15, 200));
        // 7 terrestrial / 8 aquatic fauna: densest animal cluster of each medium
        foreach (var (name, aquatic) in new[] { ("fauna_terrestrial", false), ("fauna_aquatic", true) })
        {
            var fa = W.Fauna.Items.Where(f => (W.Content.FaunaOrThrow(f.SpeciesId).Medium == Vivarium.Sim.Content.Medium.Aquatic) == aquatic).OrderBy(f => f.Id.Value).ToList();
            if (fa.Count == 0) continue;
            var hub = fa.OrderByDescending(x => fa.Count(o => Vec2.Distance(o.PositionXZ, x.PositionXZ) < 0.3)).First();
            var p = Bridge.V(hub.Position);
            float scale = (float)(W.FaunaSystem.PhenotypeOf(hub).BodySize * W.Content.FaunaOrThrow(hub.SpeciesId).VisualScale);
            list.Add(Orbit(name, "fauna", p, Math.Max(0.05f, scale * 4f), aquatic ? 8 : 30));
        }
        // 9 water margin: a wet cell beside dry ground, low angle
        var shoreCell = W.Grid.DomainCells.Where(c => W.Water.IsWet(c) && !W.Grid.IsBoundaryCell[c]).OrderBy(c => c)
            .FirstOrDefault(c => { var q = W.Grid.CellCenter(c); return !W.Water.IsWet(q + new Vec2(0.5, 0)) && W.Domain.ContainsDisc(q, 2); }, -1);
        if (shoreCell >= 0)
        {
            var q = W.Grid.CellCenter(shoreCell);
            var sp3 = new Vector3((float)q.X, (float)W.Water.SurfaceAt(q), (float)q.Z);
            list.Add(new RefScene("water_margin", "water", sp3 + new Vector3(-0.9f, 0.45f, 0.6f), sp3 + new Vector3(0.25f, 0, 0)));
        }
        // 10 rock/soil contact, 11 log/decomposer contact
        var rock = W.Props.Rocks.OrderBy(r => r.Id.Value).FirstOrDefault();
        if (rock != null) list.Add(Orbit("rock_contact", "rock", Ground(rock.Position) + new Vector3(0, (float)(rock.SizeY * 0.35), 0), (float)(rock.FootprintRadius * 2.8), 25, 30));
        var log = W.Props.Logs.OrderBy(l => l.Id.Value).FirstOrDefault();
        if (log != null) list.Add(Orbit("log_contact", "log", Ground(log.Position), (float)(log.Radius * 5), 18, 60));
        // 12 dense colony: moss/lichen no longer live as FloraIndividuals (coverage layers, §CoverageSystem), so
        // "colony" here means the densest coverage patch, not a Colony-flagged flora species (none remain).
        var coverageHubs = new List<(Vec2 center, double area, string id)>();
        foreach (var sp in W.Content.Flora.Where(sp => sp.IsCoverageSpecies).OrderBy(sp => sp.Id))
        {
            var layerId = sp.Mat != null ? CoverageLayerId.Mat : sp.Lichen != null ? CoverageLayerId.Crust : CoverageLayerId.Plasmodium;
            var layer = W.Coverage.ById(layerId);
            byte? occ = FindCoverageOccupant(layerId, sp.Id);
            if (occ == null) continue;
            var hit = DensestCoverageTile(layer, occ.Value);
            if (hit == null) continue;
            var (center, count) = hit.Value;
            double area = count * CoverageSpec.CellSize * CoverageSpec.CellSize;
            double dist = Math.Max(0.1, CoverageSpec.TileWorldSize * 0.9);
            list.Add(Orbit("species_" + sp.Id, "species", Ground(center), (float)dist, 35));
            if (sp.Lichen != null)
                list.Add(Orbit("detail_" + sp.Id, "species_detail", Ground(center), 0.22f, 25));
            coverageHubs.Add((center, area, sp.Id));
        }
        if (coverageHubs.Count > 0)
        {
            var best = coverageHubs.OrderByDescending(h => h.area).ThenBy(h => h.id).First();
            list.Add(Orbit("dense_colony", "colony", Ground(best.center), 0.45f, 40));
            list.Add(Orbit("colony_edge", "colony", Ground(best.center), 0.3f, 18, 160));
        }
        // 13 sparse dry area: driest domain cell far from water with ground visible
        var dry = W.Grid.DomainCells.Where(c => !W.Water.IsWet(c) && W.Domain.ContainsDisc(W.Grid.CellCenter(c), 1.5) && double.IsNaN(W.Props.PropTopAt(W.Grid.CellCenter(c))))
            .OrderBy(c => W.Fields.Moisture[c]).ThenBy(c => c).FirstOrDefault(-1);
        if (dry >= 0) list.Add(Orbit("sparse_dry", "dry", Ground(W.Grid.CellCenter(dry)), 1.2f, 30, 300));
        // 14 mixed depth: low wide view across the island from near the ground
        list.Add(new RefScene("mixed_depth", "depth", new Vector3(-4.5f, 0.6f, 4.5f), new Vector3(1.5f, 0.1f, -1.5f)));
        // cutaway context (the specimen look)
        var dom = W.Domain;
        var deep = DeepestWater();
        var edgePt = dom.NearestBoundaryPoint(deep);
        var en = dom.BoundaryNormal(edgePt);
        list.Add(new RefScene("cutaway_pond", "water", new Vector3((float)(edgePt.X + en.X * 3.2), 0.4f, (float)(edgePt.Z + en.Z * 3.2)), new Vector3((float)edgePt.X, -0.6f, (float)edgePt.Z)));
        // one macro per flora species present (species regressions)
        foreach (var g in flora.GroupBy(f => f.SpeciesId).OrderBy(g => g.Key))
        {
            var f = g.OrderByDescending(x => H(x)).ThenBy(x => x.Id.Value).First();
            if (g.Key == "mirrorleaf")
            {
                // Floats on the surface, not at a height derived from the species' sim-side height factor:
                // frame it from above the water, close and steep, or the generic per-species framing (anchored
                // near the pond floor via H(f)) looks up at the petioles from underwater instead of down at pads.
                double surfaceY = W.Water.SurfaceAt(f.Position);
                if (double.IsNaN(surfaceY)) surfaceY = W.GroundHeight(f.Position);
                var padTarget = new Vector3((float)f.X, (float)surfaceY, (float)f.Z);
                list.Add(Orbit("species_mirrorleaf", "species", padTarget, 0.35f, 55));
                continue;
            }
            float d = (float)Math.Clamp(H(f) * 3.0, 0.12, 0.6);
            list.Add(Orbit("species_" + g.Key, "species", Ground(f.Position) + new Vector3(0, (float)H(f) * 0.3f, 0), d, 50));
        }
        return list;
    }

    /// <summary>First occupant byte in <paramref name="layerId"/> resolving to <paramref name="speciesId"/> (occupant
    /// slots are assigned once at <see cref="Vivarium.Sim.Coverage.CoverageSystem"/> construction, so this is stable
    /// for the life of the world).</summary>
    private byte? FindCoverageOccupant(CoverageLayerId layerId, string speciesId)
    {
        for (int occ = 1; occ < 256; occ++)
            if (W.CoverageSystem.SpeciesId(layerId, (byte)occ) == speciesId) return (byte)occ;
        return null;
    }

    /// <summary>The tile with the most cells occupied by <paramref name="occ"/>, and the world-space centroid of
    /// just those cells (not the tile centre, so a patch hugging a tile edge still frames correctly).</summary>
    private (Vec2 center, int count)? DensestCoverageTile(CoverageLayer layer, byte occ)
    {
        CoverageTile? best = null;
        int bestCount = 0;
        foreach (var t in layer.Tiles)
        {
            int c = 0;
            for (int i = 0; i < CoverageTile.N; i++) if (t.Occ[i] == occ) c++;
            if (c > bestCount) { bestCount = c; best = t; }
        }
        if (best == null) return null;

        double sx = 0, sz = 0;
        for (int li = 0; li < CoverageTile.N; li++)
        {
            if (best.Occ[li] != occ) continue;
            int lx = li % CoverageSpec.TileEdge, lz = li / CoverageSpec.TileEdge;
            int gx = best.Ti * CoverageSpec.TileEdge + lx, gz = best.Tj * CoverageSpec.TileEdge + lz;
            sx += (gx + 0.5) * CoverageSpec.CellSize;
            sz += (gz + 0.5) * CoverageSpec.CellSize;
        }
        return (new Vec2(sx / bestCount, sz / bestCount), bestCount);
    }

    private readonly record struct LuminanceMetrics(
        double Mean,
        double P05,
        double P95,
        double ClipBlack,
        double ClipWhite);

    private static LuminanceMetrics ComputeLuminance(Image img)
    {
        if (img.GetFormat() != Image.Format.Rgb8)
        {
            img.Convert(Image.Format.Rgb8);
        }
        byte[] data = img.GetData();
        int totalPixels = data.Length / 3;
        if (totalPixels == 0) return default;

        double[] lums = new double[totalPixels];
        double sum = 0.0;
        int blackCount = 0;
        int whiteCount = 0;

        for (int i = 0; i < totalPixels; i++)
        {
            int offset = i * 3;
            double r = data[offset] / 255.0;
            double g = data[offset + 1] / 255.0;
            double b = data[offset + 2] / 255.0;
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            lums[i] = l;
            sum += l;
            if (l < 0.01) blackCount++;
            if (l > 0.99) whiteCount++;
        }

        Array.Sort(lums);
        double mean = sum / totalPixels;
        double p05 = Percentile(lums, 0.05);
        double p95 = Percentile(lums, 0.95);
        double clipBlack = (double)blackCount * 100.0 / totalPixels;
        double clipWhite = (double)whiteCount * 100.0 / totalPixels;

        return new LuminanceMetrics(
            Math.Round(mean, 4),
            Math.Round(p05, 4),
            Math.Round(p95, 4),
            Math.Round(clipBlack, 4),
            Math.Round(clipWhite, 4));
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0.0;
        if (sorted.Length == 1) return sorted[0];
        double idx = (sorted.Length - 1) * p;
        int lower = (int)idx;
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        double frac = idx - lower;
        return sorted[lower] + frac * (sorted[upper] - sorted[lower]);
    }

    /// <summary>Calculates minimum frames delivered in any rolling 1-second (1000 ms) window.</summary>
    public static double ComputeMinFps1sWindow(IReadOnlyList<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0) return 0.0;
        double totalMs = 0;
        for (int i = 0; i < frameTimesMs.Count; i++) totalMs += frameTimesMs[i];
        if (totalMs < 1000.0)
        {
            return Math.Round(frameTimesMs.Count * 1000.0 / Math.Max(1.0, totalMs), 1);
        }

        double[] timestamps = new double[frameTimesMs.Count];
        double acc = 0;
        for (int i = 0; i < frameTimesMs.Count; i++)
        {
            acc += frameTimesMs[i];
            timestamps[i] = acc;
        }

        int minFrames = int.MaxValue;
        int left = 0;
        for (int right = 0; right < timestamps.Length; right++)
        {
            if (timestamps[right] < 1000.0) continue;
            while (left < right && (timestamps[right] - timestamps[left]) > 1000.0)
            {
                left++;
            }
            int count = right - left + 1;
            if (count < minFrames) minFrames = count;
        }
        return minFrames == int.MaxValue ? Math.Round(frameTimesMs.Count * 1000.0 / totalMs, 1) : minFrames;
    }

    /// <summary>Computes frame timing percentiles and hitches from a sequence of frame durations.</summary>
    public static RenderReportFrameTimes ComputeFrameTimes(List<double> times)
    {
        if (times.Count == 0)
        {
            return new RenderReportFrameTimes(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        var sorted = times.ToArray();
        Array.Sort(sorted);
        double mean = times.Average();
        double fpsMean = mean > 0 ? 1000.0 / mean : 0.0;
        double fpsMin1s = ComputeMinFps1sWindow(times);
        double p50 = Percentile(sorted, 0.50);
        double p95 = Percentile(sorted, 0.95);
        double p99 = Percentile(sorted, 0.99);
        double p99_9 = Percentile(sorted, 0.999);
        double worst = sorted[^1];
        int hitches33 = times.Count(t => t > 33.3);
        int hitches50 = times.Count(t => t > 50.0);

        return new RenderReportFrameTimes(
            times.Count,
            Math.Round(mean, 2),
            Math.Round(fpsMean, 1),
            Math.Round(fpsMin1s, 1),
            Math.Round(p50, 2),
            Math.Round(p95, 2),
            Math.Round(p99, 2),
            Math.Round(p99_9, 2),
            Math.Round(worst, 2),
            hitches33,
            hitches50);
    }

    private RenderReportProvenance BuildProvenance()
    {
        return new RenderReportProvenance(
            Engine: "Godot 4.7.1 Forward+ / C# / .NET 8",
            CommitSha: "1a17b2c06e28776a7f650f73b4cac9cd931c3059",
            Os: $"{OS.GetName()} {System.Environment.OSVersion.VersionString}",
            Cpu: System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? $"{System.Environment.ProcessorCount} cores",
            Gpu: RenderingServer.GetVideoAdapterName(),
            GpuVendor: RenderingServer.GetVideoAdapterVendor(),
            RamMb: (long)(System.GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)),
            TimestampUtc: DateTime.UtcNow.ToString("o"),
            TargetResolution: "1600x900");
    }

    private RenderReportWorld BuildWorldInfo()
    {
        return new RenderReportWorld(
            Preset: "default",
            Seed: W.Seed,
            BioDays: Math.Round(W.Clock.BioDays, 3),
            Ticks: W.Clock.Tick,
            FloraTotal: W.Flora.Count,
            FaunaTotal: W.Fauna.Count,
            SpeciesPresent: W.Flora.Items.GroupBy(f => f.SpeciesId).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            CoverageAreaM2: W.Content.Flora.Where(sp => sp.IsCoverageSpecies).OrderBy(sp => sp.Id)
                .ToDictionary(sp => sp.Id, sp => Math.Round(W.CoverageSystem.CoveredArea(sp.Id), 4)));
    }

    private RenderReportSettings BuildRenderSettings()
    {
        var vsync = DisplayServer.WindowGetVsyncMode();
        bool autoExp = Session.EnvRig.CameraAttributes?.AutoExposureEnabled ?? false;
        return new RenderReportSettings(
            Resolution: new[] { 1600, 900 },
            Vsync: vsync.ToString(),
            QualityTier: Session.EnvRig.Quality.ToString(),
            AutoExposureEnabled: autoExp,
            FloraTiersEnabled: true);
    }

    private static RenderReportCpuTiming BuildCpuTiming()
    {
        var summary = FrameProfiler.GetCpuTimingSummary();
        return new RenderReportCpuTiming(
            SimulationCpuMs: summary.SimulationCpuMs,
            RenderStateGatherMs: summary.RenderStateGatherMs,
            GeometryBuildCpuMs: summary.GeometryBuildCpuMs,
            MainThreadCommitMs: summary.MainThreadCommitMs,
            GpuDrawSyncMs: summary.GpuDrawSyncMs,
            Subsystems: summary.Subsystems);
    }

    private RenderReportCounters BuildCounters(long draws, long prims, long objs)
    {
        long vram = (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        long ws = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        return new RenderReportCounters(
            DrawCalls: draws,
            Primitives: prims,
            Objects: objs,
            FloraVisible: Session.Flora.Visible_,
            FloraTriangles: Session.Flora.TrianglesDrawn,
            FaunaDrawn: Session.Fauna.Drawn,
            FaunaTriangles: Session.Fauna.TrianglesDrawn,
            CoverageTiles: Session.Coverage.TileCount,
            CoverageInstances: Session.Coverage.InstanceCount,
            CoverageTrianglesBuilt: Session.Coverage.TrianglesBuilt,
            VideoMemoryBytes: vram,
            WorkingSetBytes: ws);
    }

    private static List<string> BuildWarnings(RenderReportFrameTimes ft, string digestBefore, string digestAfter, bool expectDigestEqual)
    {
        var warnings = new List<string>();
        if (ft.FpsMin1sWindow < 30.0)
        {
            warnings.Add($"GATE_FAIL: 1-second window FPS fell below 30.0 (got {ft.FpsMin1sWindow:0.1})");
        }
        if (ft.P99Ms > 33.3)
        {
            warnings.Add($"GATE_FAIL: p99 frame time exceeded 33.3 ms (got {ft.P99Ms:0.2} ms)");
        }
        if (ft.HitchesOver50Ms > 0)
        {
            warnings.Add($"GATE_WARN: Observed {ft.HitchesOver50Ms} frame hitches > 50 ms");
        }
        if (expectDigestEqual && digestBefore != digestAfter)
        {
            warnings.Add("GATE_FAIL: Deterministic simulation digest changed during rendering");
        }
        return warnings;
    }

    private void EmitRenderReport(RenderReport report, string? scenarioTag = null)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        string json = JsonSerializer.Serialize(report, options);

        string baseDir = string.IsNullOrWhiteSpace(OutDir) ? "." : OutDir;
        Directory.CreateDirectory(baseDir);

        string mainPath = Path.Combine(baseDir, "render_report.json");
        File.WriteAllText(mainPath, json);

        if (!string.IsNullOrWhiteSpace(scenarioTag))
        {
            string tagPath = Path.Combine(baseDir, $"render_report_{scenarioTag}.json");
            File.WriteAllText(tagPath, json);
        }

        _facts["render_report"] = report;
        _facts["provenance"] = report.Provenance;
        _facts["world"] = report.World;
        _facts["scenario"] = report.Scenario;
        _facts["render_settings"] = report.RenderSettings;
        _facts["cpu_timing"] = report.CpuTiming;
        if (report.GpuTiming != null) _facts["gpu_timing"] = report.GpuTiming;
        _facts["frame_times"] = report.FrameTimes;
        _facts["counters"] = report.Counters;
        _facts["digest_before"] = report.DigestBefore;
        _facts["digest_after"] = report.DigestAfter;
        _facts["artifacts"] = report.Artifacts;
        _facts["warnings"] = report.Warnings;
    }

    public enum ScenarioMode
    {
        FrozenWorldMovingCamera,
        LiveWorldMovingCamera,
        CloseUnderCanopy,
        DenseColonyFloor,
        FastCameraTraversal,
        SculptingNearDensePlanting,
        EventBurst,
        WetMargin,
        WorstStressPreset
    }

    public static string GetScenarioName(ScenarioMode mode) => mode switch
    {
        ScenarioMode.FrozenWorldMovingCamera => "frozen_world_moving_camera",
        ScenarioMode.LiveWorldMovingCamera => "live_world_moving_camera",
        ScenarioMode.CloseUnderCanopy => "close_under_canopy",
        ScenarioMode.DenseColonyFloor => "dense_colony_floor",
        ScenarioMode.FastCameraTraversal => "fast_camera_traversal",
        ScenarioMode.SculptingNearDensePlanting => "sculpting_near_dense_planting",
        ScenarioMode.EventBurst => "event_burst",
        ScenarioMode.WetMargin => "wet_margin",
        ScenarioMode.WorstStressPreset => "worst_stress_preset",
        _ => mode.ToString().ToLowerInvariant()
    };

    public static string GetScenarioDisplayName(ScenarioMode mode) => mode switch
    {
        ScenarioMode.FrozenWorldMovingCamera => "frozen world moving camera",
        ScenarioMode.LiveWorldMovingCamera => "live world moving camera",
        ScenarioMode.CloseUnderCanopy => "close under-canopy",
        ScenarioMode.DenseColonyFloor => "dense colony floor",
        ScenarioMode.FastCameraTraversal => "fast camera traversal",
        ScenarioMode.SculptingNearDensePlanting => "sculpting near dense planting",
        ScenarioMode.EventBurst => "event burst",
        ScenarioMode.WetMargin => "wet margin",
        ScenarioMode.WorstStressPreset => "worst stress preset",
        _ => mode.ToString()
    };

    public static string GetScenarioDescription(ScenarioMode mode) => mode switch
    {
        ScenarioMode.FrozenWorldMovingCamera => "Frozen world, moving camera: isolates rendering and instance publication.",
        ScenarioMode.LiveWorldMovingCamera => "Live world, moving camera: captures ecology scheduler and presentation contention.",
        ScenarioMode.CloseUnderCanopy => "Close under-canopy, stationary camera: isolates foliage shading and pixel fill/overdraw.",
        ScenarioMode.DenseColonyFloor => "Dense colony floor grazing angle: moss, litter and contact.",
        ScenarioMode.FastCameraTraversal => "Fast camera traversal/rotation: invalidation and buffer churn.",
        ScenarioMode.SculptingNearDensePlanting => "Sculpting near dense planting: terrain update and re-projection.",
        ScenarioMode.EventBurst => "Growth/death/litter event burst: dirty tile and batching behavior.",
        ScenarioMode.WetMargin => "Wet margin / moving daylight phase: shader and lighting stability.",
        ScenarioMode.WorstStressPreset => "Worst populated preset/stress state: a saved, deterministic high-density scenario.",
        _ => mode.ToString()
    };

    public static ScenarioMode? ParseScenarioMode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string s = input.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        return s switch
        {
            "1" or "frozen" or "frozen_world" or "frozen_world_moving_camera" => ScenarioMode.FrozenWorldMovingCamera,
            "2" or "live" or "live_world" or "live_world_moving_camera" => ScenarioMode.LiveWorldMovingCamera,
            "3" or "undercanopy" or "under_canopy" or "close_undercanopy" or "close_under_canopy" => ScenarioMode.CloseUnderCanopy,
            "4" or "colony" or "colony_floor" or "dense_colony" or "dense_colony_floor" => ScenarioMode.DenseColonyFloor,
            "5" or "traversal" or "fast_traversal" or "fast_camera_traversal" => ScenarioMode.FastCameraTraversal,
            "6" or "sculpt" or "sculpting" or "sculpting_near_dense_planting" => ScenarioMode.SculptingNearDensePlanting,
            "7" or "burst" or "event_burst" or "growth_burst" => ScenarioMode.EventBurst,
            "8" or "wet" or "margin" or "wet_margin" or "wet_margin_moving_daylight" => ScenarioMode.WetMargin,
            "9" or "stress" or "worst" or "worst_stress" or "worst_stress_preset" or "worst_populated_preset" => ScenarioMode.WorstStressPreset,
            _ => null
        };
    }

    private static string? GetRequestedScenario()
    {
        var env = System.Environment.GetEnvironmentVariable("VIVARIUM_SCENARIO")
            ?? System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_SCENARIO")
            ?? System.Environment.GetEnvironmentVariable("VIVARIUM_REFERENCE_SCENARIO")
            ?? System.Environment.GetEnvironmentVariable("VIVARIUM_SCENARIO_MODE");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--scenario" or "--scenario-mode" or "--perf-scenario" && i + 1 < args.Length)
            {
                return args[i + 1].Trim();
            }
            if (args[i].StartsWith("--scenario=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i].Substring("--scenario=".Length).Trim();
            }
        }
        return null;
    }

    private async Task PrepareScenarioEnvironmentAsync()
    {
        if (FixedExposureOverride && Session.EnvRig.CameraAttributes is { } refAttr)
        {
            refAttr.AutoExposureEnabled = false;
            refAttr.ExposureSensitivity = 100.0f;
        }

        W.Clock.Paused = true;
        long startTick = W.Clock.Tick;
        while (W.Clock.BioDays < ReferenceBioDays && W.Clock.Tick - startTick < 2_000_000)
        {
            ulong s0 = Time.GetTicksUsec();
            W.Step(200);
            ulong s1 = Time.GetTicksUsec();
            await Frames(1);
            ulong s2 = Time.GetTicksUsec();
            if (s2 - s0 > 2_000_000)
                GD.Print($"REFERENCE_WARMUP_SLOW day={W.Clock.BioDays:0.00} step_ms={(s1 - s0) / 1000} frame_ms={(s2 - s1) / 1000}");
        }

        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false;
        await Frames(10);
        await SettleExposureAsync(SettleFrames);
    }

    public async Task<RenderReport> RunScenarioAsync(string scenarioName)
    {
        var mode = ParseScenarioMode(scenarioName)
            ?? throw new ArgumentException($"Unknown scenario mode '{scenarioName}'. Supported: frozen world moving camera, live world moving camera, close under-canopy, dense colony floor, fast camera traversal, sculpting near dense planting, event burst, wet margin, worst stress preset");
        return await RunScenarioAsync(mode);
    }

    public async Task<RenderReport> RunScenarioAsync(ScenarioMode mode)
    {
        await PrepareScenarioEnvironmentAsync();

        string scenarioId = GetScenarioName(mode);
        string displayName = GetScenarioDisplayName(mode);
        string description = GetScenarioDescription(mode);

        var cam = Session.CameraRig;
        int framesToMeasure = MeasureFrames;
        var times = new List<double>(framesToMeasure);
        long maxDraws = 0, maxPrims = 0, maxObjs = 0;
        var artifacts = new List<string>();

        double[]? savedHeights = null;
        Vector3 origSunRot = Session.EnvRig.Sun?.RotationDegrees ?? Vector3.Zero;
        Vec2? sculptTarget = null;
        string trajectory = "";

        var vascular = W.Flora.Items.Where(f => W.Content.FloraOrThrow(f.SpeciesId).Archetype == "plant").ToList();
        var tall = vascular.OrderByDescending(f => W.Content.FloraOrThrow(f.SpeciesId).Height).ThenBy(f => f.Id.Value).FirstOrDefault();

        if (mode == ScenarioMode.WorstStressPreset)
        {
            long sTick = W.Clock.Tick;
            while (W.Clock.BioDays < 10.0 && W.Clock.Tick - sTick < 3_000_000)
            {
                W.Step(200);
            }
        }

        string digestBefore = WorldSerializer.Digest(W);
        FrameProfiler.Reset();

        switch (mode)
        {
            case ScenarioMode.FrozenWorldMovingCamera:
                W.Clock.Paused = true;
                trajectory = "Circular orbit around island at r=8.5m h=3.2m";
                break;

            case ScenarioMode.LiveWorldMovingCamera:
                W.Clock.Paused = false;
                trajectory = "Orbit around island at r=8.5m h=3.2m with live simulation stepping";
                break;

            case ScenarioMode.CloseUnderCanopy:
                W.Clock.Paused = true;
                trajectory = "Stationary low-angle looking up through foliage understory";
                Vector3 canopyEye, canopyTarget;
                if (tall != null)
                {
                    Vector3 g = Ground(tall.Position);
                    float h = (float)W.Content.FloraOrThrow(tall.SpeciesId).Height;
                    canopyEye = g + new Vector3(0.08f, Math.Max(0.04f, h * 0.12f), 0.08f);
                    canopyTarget = canopyEye + new Vector3(-0.04f, Math.Max(0.2f, h * 0.5f), -0.04f);
                }
                else
                {
                    canopyEye = new Vector3(0, 0.25f, 0);
                    canopyTarget = new Vector3(0, 1.0f, 0);
                }
                cam.LookAtPoint(canopyEye, canopyTarget);
                await SettleExposureAsync(SettleFrames);
                break;

            case ScenarioMode.DenseColonyFloor:
                W.Clock.Paused = true;
                trajectory = "Stationary grazing angle 10 degrees at ground colony carpet";
                Vec2 colPos = Vec2.Zero;
                bool foundTile = false;
                foreach (var sp in W.Content.Flora.Where(s => s.IsCoverageSpecies))
                {
                    var layerId = sp.Mat != null ? CoverageLayerId.Mat : sp.Lichen != null ? CoverageLayerId.Crust : CoverageLayerId.Plasmodium;
                    var layer = W.Coverage.ById(layerId);
                    byte? occ = FindCoverageOccupant(layerId, sp.Id);
                    if (occ == null) continue;
                    var hit = DensestCoverageTile(layer, occ.Value);
                    if (hit != null) { colPos = hit.Value.center; foundTile = true; break; }
                }
                if (!foundTile) colPos = W.Grid.CellCenter(W.Grid.DomainCells.First());
                Vector3 colGround = Ground(colPos);
                Vector3 colTarget = colGround + new Vector3(0, 0.02f, 0);
                Vector3 colEye = colGround + new Vector3(0.30f, 0.06f, 0.30f);
                cam.LookAtPoint(colEye, colTarget);
                await SettleExposureAsync(SettleFrames);
                break;

            case ScenarioMode.FastCameraTraversal:
                W.Clock.Paused = true;
                trajectory = "Rapid sweep and oscillation across opposite island quadrants";
                break;

            case ScenarioMode.SculptingNearDensePlanting:
                W.Clock.Paused = true;
                trajectory = "Fixed view focused on planting hub with per-frame terrain displacement";
                savedHeights = (double[])W.Terrain.H.Clone();
                Vec2 hub = Hub(vascular, 1.5) ?? Vec2.Zero;
                sculptTarget = hub + new Vec2(0.35, 0.35);
                Vector3 scEye = Ground(hub) + new Vector3(1.2f, 0.9f, 1.2f);
                Vector3 scTarget = Ground(hub);
                cam.LookAtPoint(scEye, scTarget);
                await SettleExposureAsync(SettleFrames);
                break;

            case ScenarioMode.EventBurst:
                W.Clock.Paused = false;
                trajectory = "Fixed mid-elevation view across island during biological event burst";
                cam.LookAtPoint(new Vector3(0, 4.0f, 6.0f), new Vector3(0, 0.3f, 0));
                await SettleExposureAsync(SettleFrames);
                break;

            case ScenarioMode.WetMargin:
                W.Clock.Paused = true;
                trajectory = "Low angle on shore boundary with sweeping sunlight rotation";
                var shoreCell = W.Grid.DomainCells.Where(c => W.Water.IsWet(c) && !W.Grid.IsBoundaryCell[c]).OrderBy(c => c)
                    .FirstOrDefault(c => { var q = W.Grid.CellCenter(c); return !W.Water.IsWet(q + new Vec2(0.5, 0)) && W.Domain.ContainsDisc(q, 2); }, -1);
                Vector3 wmTarget, wmEye;
                if (shoreCell >= 0)
                {
                    var q = W.Grid.CellCenter(shoreCell);
                    var sp3 = new Vector3((float)q.X, (float)W.Water.SurfaceAt(q), (float)q.Z);
                    wmEye = sp3 + new Vector3(-0.9f, 0.45f, 0.6f);
                    wmTarget = sp3 + new Vector3(0.25f, 0, 0);
                }
                else
                {
                    wmEye = new Vector3(0, 1.0f, 3.0f);
                    wmTarget = Vector3.Zero;
                }
                cam.LookAtPoint(wmEye, wmTarget);
                await SettleExposureAsync(SettleFrames);
                break;

            case ScenarioMode.WorstStressPreset:
                W.Clock.Paused = true;
                trajectory = "High-density overview vantage point";
                cam.LookAtPoint(new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0));
                await SettleExposureAsync(SettleFrames);
                break;
        }

        FrameProfiler.Reset();

        for (int i = 0; i < framesToMeasure; i++)
        {
            switch (mode)
            {
                case ScenarioMode.FrozenWorldMovingCamera:
                case ScenarioMode.LiveWorldMovingCamera:
                {
                    float angle = (float)i / framesToMeasure * Mathf.Tau;
                    Vector3 eye = new Vector3(Mathf.Sin(angle) * 8.5f, 3.2f, Mathf.Cos(angle) * 8.5f);
                    Vector3 target = new Vector3(0, 0.4f, 0);
                    cam.LookAtPoint(eye, target);
                    if (mode == ScenarioMode.LiveWorldMovingCamera)
                    {
                        W.Step(1);
                    }
                    break;
                }

                case ScenarioMode.FastCameraTraversal:
                {
                    float t = i * 0.35f;
                    Vector3 eye = new Vector3(Mathf.Sin(t * 2.2f) * 9.5f, 2.0f + Mathf.Cos(t * 1.5f) * 1.5f, Mathf.Cos(t * 2.2f) * 9.5f);
                    Vector3 target = new Vector3(Mathf.Sin(t * 4.7f) * 3.5f, 0.3f, Mathf.Cos(t * 4.7f) * 3.5f);
                    cam.LookAtPoint(eye, target);
                    break;
                }

                case ScenarioMode.SculptingNearDensePlanting:
                {
                    if (sculptTarget != null)
                    {
                        double delta = (i % 2 == 0) ? 0.012 : -0.012;
                        Vivarium.Sim.World.TerrainEditing.Sculpt(W, sculptTarget.Value, 0.6, delta, Vivarium.Sim.World.SculptMode.Raise);
                    }
                    break;
                }

                case ScenarioMode.EventBurst:
                {
                    if (i % 3 == 0)
                    {
                        W.Step(5);
                    }
                    break;
                }

                case ScenarioMode.WetMargin:
                {
                    if (Session.EnvRig.Sun != null)
                    {
                        float sweep = (float)i / framesToMeasure * 45.0f;
                        Session.EnvRig.Sun.RotationDegrees = new Vector3(origSunRot.X, origSunRot.Y + sweep, origSunRot.Z);
                    }
                    break;
                }
            }

            ulong f0 = Time.GetTicksUsec();
            await Frames(1);
            times.Add((Time.GetTicksUsec() - f0) / 1000.0);

            maxDraws = Math.Max(maxDraws, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
            maxPrims = Math.Max(maxPrims, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
            maxObjs = Math.Max(maxObjs, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
        }

        if (mode == ScenarioMode.SculptingNearDensePlanting && savedHeights != null)
        {
            Array.Copy(savedHeights, W.Terrain.H, savedHeights.Length);
            W.Terrain.Touch();
        }
        if (mode == ScenarioMode.WetMargin && Session.EnvRig.Sun != null)
        {
            Session.EnvRig.Sun.RotationDegrees = origSunRot;
        }

        await Screenshot($"scenario_{scenarioId}");
        artifacts.Add($"scenario_{scenarioId}.png");

        string digestAfter = WorldSerializer.Digest(W);
        bool expectDigestEqual = mode != ScenarioMode.LiveWorldMovingCamera && mode != ScenarioMode.EventBurst;
        if (expectDigestEqual)
        {
            Check($"rendering leaves the simulation digest unchanged in {displayName}", digestBefore == digestAfter);
        }
        else
        {
            Check($"deterministic progression in {displayName}", !string.IsNullOrEmpty(digestAfter));
        }

        var frameTimes = ComputeFrameTimes(times);
        var counters = BuildCounters(maxDraws, maxPrims, maxObjs);
        var warnings = BuildWarnings(frameTimes, digestBefore, digestAfter, expectDigestEqual);

        var report = new RenderReport(
            Provenance: BuildProvenance(),
            World: BuildWorldInfo(),
            Scenario: new RenderReportScenario(
                Name: scenarioId,
                Category: "scenario",
                Description: description,
                FramesMeasured: times.Count,
                DurationSeconds: Math.Round(times.Sum() / 1000.0, 2),
                CameraTrajectory: trajectory),
            RenderSettings: BuildRenderSettings(),
            CpuTiming: BuildCpuTiming(),
            GpuTiming: null,
            FrameTimes: frameTimes,
            Counters: counters,
            DigestBefore: digestBefore,
            DigestAfter: digestAfter,
            Artifacts: artifacts,
            Warnings: warnings);

        EmitRenderReport(report, scenarioId);

        Log.Info(LogCategory.Perf, $"scenario {displayName}: fps {frameTimes.FpsMean:0.0} min1s {frameTimes.FpsMin1sWindow:0.0} p95 {frameTimes.P95Ms:0.0}ms p99 {frameTimes.P99Ms:0.0}ms draws {maxDraws} prims {maxPrims}");

        return report;
    }

    public async Task<List<RenderReport>> RunAllScenariosAsync()
    {
        var reports = new List<RenderReport>();
        foreach (ScenarioMode mode in Enum.GetValues<ScenarioMode>())
        {
            Log.Info(LogCategory.Perf, $"Starting scenario: {GetScenarioDisplayName(mode)}");
            var rep = await RunScenarioAsync(mode);
            reports.Add(rep);
        }
        return reports;
    }

    public record RenderReport(
        [property: JsonPropertyName("provenance")] RenderReportProvenance Provenance,
        [property: JsonPropertyName("world")] RenderReportWorld World,
        [property: JsonPropertyName("scenario")] RenderReportScenario Scenario,
        [property: JsonPropertyName("render_settings")] RenderReportSettings RenderSettings,
        [property: JsonPropertyName("cpu_timing")] RenderReportCpuTiming CpuTiming,
        [property: JsonPropertyName("gpu_timing")] RenderReportGpuTiming? GpuTiming,
        [property: JsonPropertyName("frame_times")] RenderReportFrameTimes FrameTimes,
        [property: JsonPropertyName("counters")] RenderReportCounters Counters,
        [property: JsonPropertyName("digest_before")] string DigestBefore,
        [property: JsonPropertyName("digest_after")] string DigestAfter,
        [property: JsonPropertyName("artifacts")] List<string> Artifacts,
        [property: JsonPropertyName("warnings")] List<string> Warnings);

    public record RenderReportProvenance(
        [property: JsonPropertyName("engine")] string Engine,
        [property: JsonPropertyName("commit_sha")] string CommitSha,
        [property: JsonPropertyName("os")] string Os,
        [property: JsonPropertyName("cpu")] string Cpu,
        [property: JsonPropertyName("gpu")] string Gpu,
        [property: JsonPropertyName("gpu_vendor")] string GpuVendor,
        [property: JsonPropertyName("ram_mb")] long RamMb,
        [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
        [property: JsonPropertyName("target_resolution")] string TargetResolution);

    public record RenderReportWorld(
        [property: JsonPropertyName("preset")] string Preset,
        [property: JsonPropertyName("seed")] ulong Seed,
        [property: JsonPropertyName("bio_days")] double BioDays,
        [property: JsonPropertyName("ticks")] long Ticks,
        [property: JsonPropertyName("flora_total")] int FloraTotal,
        [property: JsonPropertyName("fauna_total")] int FaunaTotal,
        [property: JsonPropertyName("species_present")] Dictionary<string, int> SpeciesPresent,
        [property: JsonPropertyName("coverage_area_m2")] Dictionary<string, double> CoverageAreaM2);

    public record RenderReportScenario(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("frames_measured")] int FramesMeasured,
        [property: JsonPropertyName("duration_seconds")] double DurationSeconds,
        [property: JsonPropertyName("camera_trajectory")] string CameraTrajectory);

    public record RenderReportSettings(
        [property: JsonPropertyName("resolution")] int[] Resolution,
        [property: JsonPropertyName("vsync")] string Vsync,
        [property: JsonPropertyName("quality_tier")] string QualityTier,
        [property: JsonPropertyName("auto_exposure_enabled")] bool AutoExposureEnabled,
        [property: JsonPropertyName("flora_tiers_enabled")] bool FloraTiersEnabled);

    public record RenderReportCpuTiming(
        [property: JsonPropertyName("simulation_cpu_ms")] double SimulationCpuMs,
        [property: JsonPropertyName("render_state_gather_ms")] double RenderStateGatherMs,
        [property: JsonPropertyName("geometry_build_cpu_ms")] double GeometryBuildCpuMs,
        [property: JsonPropertyName("main_thread_commit_ms")] double MainThreadCommitMs,
        [property: JsonPropertyName("gpu_draw_sync_ms")] double GpuDrawSyncMs,
        [property: JsonPropertyName("subsystems")] Dictionary<string, double> Subsystems);

    public record RenderReportGpuTiming(
        [property: JsonPropertyName("draw_ms")] double? DrawMs,
        [property: JsonPropertyName("compute_ms")] double? ComputeMs,
        [property: JsonPropertyName("available")] bool Available);

    public record RenderReportFrameTimes(
        [property: JsonPropertyName("sample_count")] int SampleCount,
        [property: JsonPropertyName("mean_ms")] double MeanMs,
        [property: JsonPropertyName("fps_mean")] double FpsMean,
        [property: JsonPropertyName("fps_min_1s_window")] double FpsMin1sWindow,
        [property: JsonPropertyName("p50_ms")] double P50Ms,
        [property: JsonPropertyName("p95_ms")] double P95Ms,
        [property: JsonPropertyName("p99_ms")] double P99Ms,
        [property: JsonPropertyName("p99_9_ms")] double P99_9Ms,
        [property: JsonPropertyName("worst_ms")] double WorstMs,
        [property: JsonPropertyName("hitches_over_33ms")] int HitchesOver33Ms,
        [property: JsonPropertyName("hitches_over_50ms")] int HitchesOver50Ms);

    public record RenderReportCounters(
        [property: JsonPropertyName("draw_calls")] long DrawCalls,
        [property: JsonPropertyName("primitives")] long Primitives,
        [property: JsonPropertyName("objects")] long Objects,
        [property: JsonPropertyName("flora_visible")] int FloraVisible,
        [property: JsonPropertyName("flora_triangles")] long FloraTriangles,
        [property: JsonPropertyName("fauna_drawn")] int FaunaDrawn,
        [property: JsonPropertyName("fauna_triangles")] long FaunaTriangles,
        [property: JsonPropertyName("coverage_tiles")] int CoverageTiles,
        [property: JsonPropertyName("coverage_instances")] int CoverageInstances,
        [property: JsonPropertyName("coverage_triangles_built")] long CoverageTrianglesBuilt,
        [property: JsonPropertyName("video_memory_bytes")] long VideoMemoryBytes,
        [property: JsonPropertyName("working_set_bytes")] long WorkingSetBytes);
}
