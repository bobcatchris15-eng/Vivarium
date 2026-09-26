using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Observation;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Time;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Integration")]
public class IntegrationTests
{
    private static int Wet(VivariumWorld w) => w.Grid.DomainCells.Count(c => w.Water.IsWet(c));

    [Fact] // t-179
    public void DefaultPresetExposesEveryHabitat()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var cells = w.Grid.DomainCells.Select(c => (P: w.Grid.CellCenter(c), C: c)).ToList();
        Assert.Contains(cells, x => !w.Water.IsWet(x.C) && w.Fields.Moisture[x.C] < 0.35);            // dry
        Assert.Contains(cells, x => !w.Water.IsWet(x.C) && w.Fields.Moisture[x.C] > 0.75);            // moist bank
        Assert.True(cells.Count(x => w.Water.Depth[x.C] > 0.1) > 50, "aquatic habitat");              // pond
        Assert.NotEmpty(w.Props.Rocks); Assert.NotEmpty(w.Props.Logs); Assert.NotEmpty(w.Props.Gravel);
        Assert.NotEmpty(w.Water.Springs);
        Assert.True(w.Water.FlowX.Any(v => Math.Abs(v) > 1e-7), "a stream flows from the spring");
    }

    [Fact] // t-180, t-181
    public void StarterPopulationsAreCompleteAndValid()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var sp in w.Content.Flora.Where(s => !s.IsCoverageSpecies))
        {
            var members = w.Flora.Items.Where(f => f.SpeciesId == sp.Id).ToList();
            Assert.True(members.Count > 0, $"{sp.Id} missing from starters");
            Assert.All(members, f => Assert.False(w.FloraSystem.Suitability(sp, f.Position, f.Id).HardRefused, $"{sp.Id} placed in refused habitat"));
        }
        foreach (var sp in w.Content.Flora.Where(s => s.IsCoverageSpecies))
        {
            Assert.True(w.CoverageSystem.CoveredArea(sp.Id) > 0, $"{sp.Id} missing from coverage starters");
        }
        foreach (var sp in w.Content.Fauna)
        {
            var members = w.Fauna.Items.Where(f => f.SpeciesId == sp.Id).ToList();
            Assert.True(members.Count > 0, $"{sp.Id} missing from starters");
            foreach (var f in members)
            {
                Assert.Null(Introduction.FaunaPlacementProblem(w, sp, f.PositionXZ));
                Assert.NotNull(w.Genomes.Get(f.GenomeId));
                Assert.Equal(EntityKind.Fauna, f.Id.Kind);
            }
            // food opportunity: some starter can find its diet within its sense range
            Assert.Contains(members, f => w.FaunaSystem.FoodAt(sp, f.PositionXZ) > 0 || sp.Diet.Any(d => d.Resource is "biofilm" or "plankton"));
        }
    }

    [Fact] // t-182
    [Trait("Speed", "Slow")]
    public void DefaultEcologyRunsThreeWeeksCleanly()
    {
        var w = TestUtil.DefaultWorld(bio: TestUtil.ShippedBio);   // as shipped: 21 biological days
        var budget = new Diagnostics.WorkloadBudget(w) { SystemBudgetMs = 500 };
        for (int day = 0; day < 21; day++)
        {
            w.Step(TestUtil.TicksPerBioDay());
            var inv = w.CheckInvariants();
            Assert.True(inv.Count == 0, $"day {day}: {string.Join("; ", inv.Take(5))}");
            WorldSerializer.Validate(w);   // content references and habitat integrity
        }
        var stats = EcosystemStatistics.Compute(w);
        Assert.True(stats.FloraTotal > 50 && stats.FaunaTotal > 20, $"flora {stats.FloraTotal}, fauna {stats.FaunaTotal}");
        Assert.True(w.Tally.Species.Values.Sum(t => t.Births) > 100);
        Assert.True(w.Tally.NutrientsFromDecay > 0 && w.Tally.DetritusFromFlora > 0 && w.Tally.NutrientsFromWaste > 0, "nutrient cycle active");
        // no animal is left in impossible habitat (brief transients while escaping are allowed for ≤5 %)
        int invalid = w.Fauna.Items.Count(f => Introduction.FaunaPlacementProblem(w, w.Content.FaunaOrThrow(f.SpeciesId), f.PositionXZ) != null);
        Assert.True(invalid <= Math.Max(2, w.Fauna.Count / 20), $"{invalid} of {w.Fauna.Count} animals in invalid habitat");
    }

    [Fact] // t-183
    [Trait("Speed", "Slow")]
    public void GeneticDriftIsVisibleAndExplainedByLineage()
    {
        var w = FaunaFixtures.PondWorld(2027);
        var sp = w.Content.FaunaOrThrow("siltshield");
        Assert.True(Introduction.IntroduceFauna(w, "siltshield", FaunaFixtures.Pond, 10).Ok);
        for (int day = 0; day < 60; day++)
        {
            FaunaFixtures.HoldWater(w);
            foreach (int c in w.Grid.DomainCells) { w.Fields.Detritus[c] = Math.Max(w.Fields.Detritus[c], 1.5); if (w.Water.IsWet(c)) w.Fields.Biofilm[c] = Math.Max(w.Fields.Biofilm[c], 0.3); }
            w.Step(8640);
        }
        var alive = w.Fauna.Items.Where(f => f.SpeciesId == "siltshield").ToList();
        Assert.NotEmpty(alive);
        int maxGen = alive.Max(f => w.Genomes.Get(f.GenomeId)!.Generation);
        Assert.True(maxGen >= 4, $"only {maxGen} generations");
        var sizes = alive.Select(f => w.FaunaSystem.PhenotypeOf(f).BodySize).ToList();
        Assert.True(sizes.Max() / sizes.Min() > 1.08, "descendants should visibly vary in size");
        foreach (var f in alive)
        {
            var g = w.Genomes.Get(f.GenomeId)!;
            Assert.All(g.Traits, t => Assert.InRange(t, 0, 1));
            var rec = w.Lineage.Get(f.Id)!;
            if (rec.ParentA.IsNone) continue;
            var pa = w.Lineage.Get(rec.ParentA);
            if (pa == null) continue;                       // pruned beyond retained depth
            var mid = Inheritance.Midpoint(w.Genomes.Get(pa.GenomeId)!.Traits, rec.ParentB.IsNone ? null : w.Genomes.Get(w.Lineage.Get(rec.ParentB)!.GenomeId)!.Traits);
            int differing = mid.Zip(g.Traits).Count(z => Math.Abs(z.First - z.Second) > 1e-12);
            Assert.True(differing <= 1 && differing == g.Mutations, "phenotype = parental midpoint + at most the recorded mutation");
        }
    }

    [Fact] // t-184
    public void UnderwaterObservationPathWorks()
    {
        var w = TestUtil.DefaultWorld();
        int deepCell = w.Grid.DomainCells.OrderByDescending(c => w.Water.Depth[c]).First();
        var deep = w.Grid.CellCenter(deepCell);
        var edge = w.Domain.NearestBoundaryPoint(deep);
        var n = w.Domain.BoundaryNormal(edge);
        // exterior cutaway: water geometry ends on the cut plane
        var water = WaterMesh.Build(w);
        Assert.True(Enumerable.Range(0, water.VertexCount).All(i => w.Domain.SignedDistance(water.Position(i).XZ) <= 2e-6));
        var tracker = new WaterMediumTracker();
        double surf = w.Water.SurfaceAt(deep);
        var path = new[] { new Vec3(edge.X + n.X * 3, 0.5, edge.Z + n.Z * 3), new Vec3(deep.X, surf + 0.3, deep.Z), new Vec3(deep.X, surf - 0.1, deep.Z) };
        foreach (var eye in path) tracker.Update(w, eye);
        Assert.True(tracker.Underwater);
        Assert.Equal(1, tracker.Transitions);
        // an aquatic animal remains selectable from the underwater eye
        var fish = w.Fauna.Items.Where(f => w.Content.FaunaOrThrow(f.SpeciesId).Medium == Medium.Aquatic).OrderBy(f => Vec2.Distance(f.PositionXZ, deep)).First();
        var eyeUnder = new Vec3(fish.X + 0.15, fish.Y + 0.01, fish.Z);
        var hit = Selection.Raycast(w, eyeUnder, fish.Position - eyeUnder);
        Assert.Equal(HitKind.Fauna, hit.Kind);
        Assert.Equal(fish.Id, hit.Id);
    }

    [Fact] // t-185
    public void EveryToolChangesOnlyItsIntendedState()
    {
        var w = TestUtil.DefaultWorld();
        var t = new ToolActions(w);
        Dictionary<string, string> D() => WorldSerializer.SubsystemDigests(w);
        void Expect(Action act, params string[] changed)
        {
            var before = D(); act(); var after = D();
            foreach (var k in before.Keys)
                if (changed.Contains(k)) Assert.NotEqual(before[k], after[k]);
                else Assert.True(before[k] == after[k], $"'{k}' changed unexpectedly");
        }
        var land = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => !w.Water.IsWet(p) && w.Domain.ContainsDisc(p, 1.5) && w.SubstrateAt(p) == Substrate.Soil && double.IsNaN(w.Props.PropTopAt(p)));
        Expect(() => Assert.True(t.ApplyNutrients(land, 0.4).Ok), "fields", "world");     // world: tally of applied nutrients
        var plant = w.Flora.Items[0];
        Expect(() => Assert.True(t.RemovePlant(plant.Id).Ok), "flora", "fields", "world");
        var critter = w.Fauna.Items.First(f => f.SpeciesId == "prismhopper");
        Expect(() => t.Poke(new PokeAction(critter.Position, new Vec3(0, -1, 0), 1, new WorldHit(HitKind.Fauna, critter.Id, critter.Position, 1))), "fauna");
        var shrimp = w.Fauna.Items.First(f => f.SpeciesId == "emberglass_swimmer");
        var origin = shrimp.Position;
        Expect(() => { t.Grab(shrimp.Id); t.ReturnHeld(shrimp.Id, origin); });              // round trip is a no-op
        var spot = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => t.PreviewRock(p, 0.2) == null && !w.Water.IsWet(p) && Vec2.Distance(p, land) > 1);
        Expect(() => Assert.True(t.PlaceRock(spot, 0.2, 0, 9).Ok), "world");
        var mossSpot = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => t.PreviewFlora("coinrunner", p) == null);
        Expect(() => Assert.True(t.IntroduceFlora("coinrunner", mossSpot).Ok), "flora", "world");
        int pond = w.Grid.DomainCells.OrderByDescending(c => w.Water.Depth[c]).First();
        Expect(() => Assert.True(t.IntroduceFauna("glintfin", w.Grid.CellCenter(pond), 3).Ok), "fauna", "genetics", "world");
        w.Step(8640);                                                                        // ecosystem continues
        Assert.Empty(w.CheckInvariants());
    }

    [Fact] // t-186
    public void ExtinctionIsRecoverableThroughOrdinaryTools()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var f in w.Fauna.Items.Where(f => f.SpeciesId == "prismhopper").ToList()) w.FaunaSystem.Kill(f, "extinction test");
        foreach (var f in w.Flora.Items.Where(f => f.SpeciesId == "coinrunner").ToList()) w.FloraSystem.Kill(f, "extinction test");
        w.Step(600);
        Assert.Equal(0, w.Fauna.CountOf("prismhopper"));
        var t = new ToolActions(w);
        var land = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => t.PreviewFauna("prismhopper", p) == null);
        Assert.True(t.IntroduceFauna("prismhopper", land, 6).Ok);
        var plant = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => t.PreviewFlora("coinrunner", p) == null);
        Assert.True(t.IntroduceFlora("coinrunner", plant).Ok);
        w.Step(8640 * 3);
        Assert.True(w.Fauna.CountOf("prismhopper") > 0, "reintroduced species persists");
        Assert.Contains(w.Flora.Items, f => f.SpeciesId == "coinrunner");
        Assert.Empty(w.CheckInvariants());
    }

    [Fact] // t-187
    [Trait("Speed", "Slow")]
    public void SaveDuringActiveEcologyThenContinueDeterministically()
    {
        var w = TestUtil.DefaultWorld();
        w.Step(8640 * 7);
        var path = Path.Combine(TestUtil.TempDir(), "active.vivsave");
        Assert.True(SaveSystem.Save(w, path).Ok);
        var loaded = SaveSystem.Load(w.Content, path);
        Assert.True(loaded.Ok, loaded.Message);
        Assert.Equal(TestUtil.Digest(w), TestUtil.Digest(loaded.World!));
        w.Step(8640 * 3); loaded.World!.Step(8640 * 3);
        Assert.Equal(TestUtil.Digest(w), TestUtil.Digest(loaded.World));
    }

    [Fact] // t-188
    public void RenderCadenceCameraAndQualityCannotAffectSimulation()
    {
        string Run(double[] frames, bool heavyObservation)
        {
            var w = TestUtil.DefaultWorld();
            w.Scheduler.MaxTicksPerAdvance = int.MaxValue; w.Scheduler.WallBudgetMs = 0;
            var host = new SimHost(w);
            var tracker = new WaterMediumTracker();
            int i = 0;
            while (w.Clock.Tick < 3000)
            {
                host.Advance(frames[i++ % frames.Length]);
                if (heavyObservation)
                {
                    // everything a client does between frames: camera, picking, meshes, stats, counters
                    var eye = new Vec3(Math.Sin(i) * 6, 2 + Math.Cos(i * 0.3), Math.Cos(i) * 6);
                    tracker.Update(w, eye);
                    Selection.Raycast(w, eye, -eye, includeWater: true);
                    if (i % 7 == 0) { WaterMesh.Build(w); EcosystemStatistics.Compute(w); Diagnostics.Counters.Collect(w); }
                    if (i % 50 == 0) { TerrainMesh.BuildTop(w); foreach (var f in w.Fauna.Items) w.FaunaSystem.PhenotypeOf(f); }
                }
            }
            w.Scheduler.RunUntilTick(3100);
            return TestUtil.Digest(w);
        }
        var plain = Run(new[] { 1 / 60.0 }, false);
        Assert.Equal(plain, Run(new[] { 1 / 144.0, 1 / 20.0, 0.25 }, true));
        Assert.Equal(plain, Run(new[] { 0.0, 1 / 30.0 }, true));
    }

    [Fact] // t-189
    public void FreshWorldReplayWithScriptedInterventionsIsExact()
    {
        string Run()
        {
            var w = TestUtil.DefaultWorld();
            var t = new ToolActions(w);
            w.Step(2000);
            t.ApplyNutrients(new Vec2(-1, 1), 0.6);
            var spot = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => t.PreviewRock(p, 0.25) == null && !w.Water.IsWet(p));
            t.PlaceRock(spot, 0.25, 0.4, 12345);
            w.Step(1500);
            var st = w.Fauna.Items.First(f => f.SpeciesId == "prismhopper");
            t.Poke(new PokeAction(st.Position, new Vec3(0, -1, 0), 1, WorldHit.None));
            t.RemovePlant(w.Flora.Items[3].Id);
            int pond = w.Grid.DomainCells.OrderByDescending(c => w.Water.Depth[c]).First();
            t.IntroduceFauna("emberglass_swimmer", w.Grid.CellCenter(pond), 3);
            w.Step(8640);
            return TestUtil.Digest(w);
        }
        Assert.Equal(Run(), Run());
    }
}
