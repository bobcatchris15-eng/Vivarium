using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private const int SettleFrames = 30, MeasureFrames = 90;

    private record RefScene(string Name, string Category, Vector3 Eye, Vector3 Target);

    private async Task ReferenceAsync()
    {
        var cam = Session.CameraRig;
        W.Clock.Paused = true;
        // deterministic warm-up: step whole ticks, yielding so the window stays responsive
        long startTick = W.Clock.Tick;
        while (W.Clock.BioDays < ReferenceBioDays && W.Clock.Tick - startTick < 2_000_000)
        {
            W.Step(200);
            await Frames(1);
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
        foreach (var s in scenes)
        {
            cam.LookAtPoint(s.Eye, s.Target);
            await Frames(SettleFrames);
            FrameProfiler.TakeReport(); // reset worst-scope window
            var times = new List<double>(MeasureFrames);
            long draws = 0, prims = 0, objs = 0;
            for (int i = 0; i < MeasureFrames; i++)
            {
                cam.LookAtPoint(s.Eye, s.Target);
                ulong f0 = Time.GetTicksUsec();
                await Frames(1);
                times.Add((Time.GetTicksUsec() - f0) / 1000.0);
                draws = Math.Max(draws, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
                prims = Math.Max(prims, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame));
                objs = Math.Max(objs, (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
            }
            string scopes = FrameProfiler.TakeReport();
            await Screenshot(s.Name);
            times.Sort();
            double mean = times.Average();
            rows.Add(new Dictionary<string, object>
            {
                ["name"] = s.Name, ["category"] = s.Category,
                ["eye"] = new[] { s.Eye.X, s.Eye.Y, s.Eye.Z }, ["target"] = new[] { s.Target.X, s.Target.Y, s.Target.Z },
                ["fps_mean"] = Math.Round(1000.0 / mean, 1),
                ["frame_ms_p50"] = Math.Round(times[times.Count / 2], 2),
                ["frame_ms_p95"] = Math.Round(times[(int)(times.Count * 0.95)], 2),
                ["frame_ms_worst"] = Math.Round(times[^1], 2),
                ["draw_calls"] = draws, ["primitives"] = prims, ["objects"] = objs,
                ["flora_visible"] = Session.Flora.Visible_, ["flora_triangles"] = Session.Flora.TrianglesDrawn,
                ["fauna_drawn"] = Session.Fauna.Drawn, ["fauna_triangles"] = Session.Fauna.TrianglesDrawn,
                ["slowest_scopes"] = scopes,
            });
            Log.Info(LogCategory.Perf, $"ref {s.Name}: fps {1000.0 / mean:0.0} p95 {times[(int)(times.Count * 0.95)]:0.0}ms draws {draws} prims {prims}");
        }
        _facts["scenes"] = rows;
        _facts["scene_count"] = rows.Count;
        _facts["renderer"] = RenderingServer.GetVideoAdapterName();
        Check("reference scenes captured", rows.Count >= 14, $"{rows.Count} scenes");
        Check("rendering leaves the simulation digest unchanged", WorldSerializer.Digest(W) == digest);
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

    private List<RefScene> BuildReferenceScenes()
    {
        var list = new List<RefScene>();
        var flora = W.Flora.Items.OrderBy(f => f.Id.Value).ToList();
        string Arch(string id) => W.Content.FloraOrThrow(id).Archetype;
        double H(Vivarium.Sim.Flora.FloraIndividual f) => W.Content.FloraOrThrow(f.SpeciesId).Height;
        bool Colonial(string id) => W.Content.FloraOrThrow(id).Colony != null;

        // densest point of a set of individuals (deterministic: ties broken by id)
        Vec2? Hub(IEnumerable<Vivarium.Sim.Flora.FloraIndividual> src, double r)
        {
            var s = src.ToList();
            if (s.Count == 0) return null;
            return s.OrderByDescending(x => s.Count(o => Vec2.Distance(o.Position, x.Position) < r)).ThenBy(x => x.Id.Value).First().Position;
        }
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
            var layerId = sp.Mat != null ? CoverageLayerId.Mat : CoverageLayerId.Crust;
            var layer = W.Coverage.ById(layerId);
            byte? occ = FindCoverageOccupant(layerId, sp.Id);
            if (occ == null) continue;
            var hit = DensestCoverageTile(layer, occ.Value);
            if (hit == null) continue;
            var (center, count) = hit.Value;
            double area = count * CoverageSpec.CellSize * CoverageSpec.CellSize;
            double dist = Math.Max(0.1, CoverageSpec.TileWorldSize * 0.9);
            list.Add(Orbit("species_" + sp.Id, "species", Ground(center), (float)dist, 35));
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
}
