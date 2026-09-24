using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public static class FaunaFixtures
{
    public static FaunaSpeciesDef Sp(string id) => TestUtil.Content.FaunaOrThrow(id);

    /// <summary>Flat world with a 3 m wide, 20 cm deep pond on the west side and moist, food-rich ground elsewhere.</summary>
    public static VivariumWorld PondWorld(ulong seed = 51, double detritus = 2.0, double biofilm = 0.4, double plankton = 0.25)
    {
        var w = TestUtil.FlatWorld(seed, d => d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = -2, Z = 0, Radius = 2.2, Amount = 0.3 }));
        TestUtil.Flood(w, new Vec2(-2, 0), 1.5, 0.2);
        foreach (int c in w.Grid.DomainCells)
        {
            w.Fields.Moisture[c] = w.Water.IsWet(c) ? 1 : 0.7;
            w.Fields.Detritus[c] = detritus;
            if (w.Water.IsWet(c)) { w.Fields.Biofilm[c] = biofilm; w.Fields.Plankton[c] = plankton; }
        }
        return w;
    }

    public static readonly Vec2 Pond = new(-2, 0);
    public static readonly Vec2 Land = new(2, 0.5);

    public static void HoldWater(VivariumWorld w) { foreach (int c in w.Grid.CellsInRadius(Pond, 1.5)) w.Water.Depth[c] = Math.Max(w.Water.Depth[c], 0.2); }
}

[Trait("Suite", "Fauna")]
public class FaunaTests
{
    private static FaunaSpeciesDef Sp(string id) => FaunaFixtures.Sp(id);

    [Fact] // t-087
    public void FaunaSchemaIsDataDriven()
    {
        foreach (var sp in TestUtil.Content.Fauna)
        {
            Assert.NotEmpty(sp.Diet);
            Assert.True(sp.SizeMax > sp.SizeMin && sp.Lifespan > sp.MaturityAge && sp.Speed > 0);
            Assert.Contains("size", sp.Traits);
        }
        Assert.Contains(TestUtil.Content.Fauna, f => f.Medium == Medium.Aquatic);
        Assert.Contains(TestUtil.Content.Fauna, f => f.Medium == Medium.Terrestrial);
    }

    [Fact] // t-088
    public void IndividualStateRoundTripsWithoutRenderObjects()
    {
        var w = FaunaFixtures.PondWorld();
        var f = w.FaunaSystem.CreateFounder(Sp("springtail"), FaunaFixtures.Land);
        var json = System.Text.Json.JsonSerializer.Serialize(f, Persistence.WorldSerializer.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<FaunaIndividual>(json, Persistence.WorldSerializer.Json)!;
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(back, Persistence.WorldSerializer.Json));
        Assert.Equal(f.GenomeId, back.GenomeId);
        Assert.Equal(f.Energy, back.Energy);
        Assert.Equal(f.Stage, back.Stage);
    }

    [Fact] // t-089
    public void SpatialIndexRadiusQueriesAreCorrectAndUpdated()
    {
        var w = FaunaFixtures.PondWorld();
        var ids = new List<EntityId>();
        var rng = Rng.Stream(3, "t");
        for (int i = 0; i < 200; i++) ids.Add(w.FaunaSystem.CreateFounder(Sp("springtail"), new Vec2(rng.Range(0.5, 3.5), rng.Range(-2, 2))).Id);
        w.Fauna.RebuildIndex();
        var q = new Vec2(2, 0); double r = 0.6;
        var got = new List<FaunaIndividual>();
        w.Fauna.Neighbours(q, r, got);
        var brute = w.Fauna.Items.Where(f => Vec2.Distance(f.PositionXZ, q) <= r).Select(f => f.Id).OrderBy(x => x.Value).ToList();
        Assert.Equal(brute, got.Select(f => f.Id).OrderBy(x => x.Value).ToList());
        var moved = got[0]; moved.X = -4; w.Fauna.RebuildIndex();
        w.Fauna.Neighbours(q, r, got);
        Assert.DoesNotContain(got, f => f.Id == moved.Id);
        w.FaunaSystem.Kill(w.Fauna.Items[5], "t");
        Assert.Equal(w.Fauna.Count, w.Fauna.Index.Count);
    }

    [Fact] // t-090
    public void AquaticRejectsDryAndTerrestrialExpressesPreferences()
    {
        var w = FaunaFixtures.PondWorld();
        Assert.True(w.FaunaSystem.Suitability(Sp("shrimp"), FaunaFixtures.Land).HardRefused);
        Assert.False(w.FaunaSystem.Suitability(Sp("shrimp"), FaunaFixtures.Pond).HardRefused);
        Assert.True(w.FaunaSystem.Suitability(Sp("springtail"), FaunaFixtures.Pond).HardRefused);
        var moist = w.FaunaSystem.Suitability(Sp("springtail"), FaunaFixtures.Land).Score;
        foreach (int c in w.Grid.CellsInRadius(FaunaFixtures.Land, 0.3)) w.Fields.Moisture[c] = 0.1;
        Assert.True(w.FaunaSystem.Suitability(Sp("springtail"), FaunaFixtures.Land).Score < moist * 0.5);
    }

    [Fact] // t-091, t-092
    public void MetabolismAndFeedingAreBoundedAndTimeDriven()
    {
        var w = FaunaFixtures.PondWorld(detritus: 0);
        var f = w.FaunaSystem.CreateFounder(Sp("springtail"), FaunaFixtures.Land);
        f.Energy = 0.5;
        for (int i = 0; i < 20; i++) w.FaunaSystem.StepMetabolism(30);
        double hungry = f.Energy;
        Assert.True(hungry < 0.5, "energy is consumed over simulated time");
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 2;
        int cell = w.Grid.CellAt(f.PositionXZ);
        double food = w.Fields.Detritus[cell];
        for (int i = 0; i < 40; i++) w.FaunaSystem.StepMetabolism(30);
        Assert.True(f.Energy > hungry, "feeding replenishes energy");
        Assert.True(w.Fields.Detritus[cell] < food, "feeding consumes the resource");
        Assert.InRange(f.Energy, 0, Sp("springtail").MaxEnergy);
        // invalid diet targets are rejected by content validation
        var src = new OverlayContentSource(TestUtil.ContentSource);
        src.Set("fauna/springtail.json", File.ReadAllText(Path.Combine(TestUtil.ContentDir, "fauna", "springtail.json")).Replace("\"resource\": \"detritus\"", "\"resource\": \"cheese\""));
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src));
        Assert.Contains(ex.Errors, e => e.File == "fauna/springtail.json" && e.Path.StartsWith("$.diet[") && e.Path.EndsWith("].resource") && e.Message.Contains("cheese"));
    }

    [Fact] // t-093
    public void AquaticLocomotionStaysInWater()
    {
        var w = FaunaFixtures.PondWorld();
        var shrimp = Enumerable.Range(0, 20).Select(i => w.FaunaSystem.CreateFounder(Sp("shrimp"), FaunaFixtures.Pond + new Vec2(0.05 * i - 0.5, 0.02 * i))).ToList();
        var start = shrimp.Select(s => s.PositionXZ).ToList();
        for (int t = 0; t < 2000; t++)
        {
            w.FaunaSystem.StepBehaviour(20); w.Clock.Tick += 2;
            foreach (var s in shrimp)
            {
                Assert.True(w.Water.DepthAt(s.PositionXZ) >= Sp("shrimp").MinWaterDepth, "shrimp left the water");
                Assert.InRange(s.Y, w.Terrain.Height(s.PositionXZ) - 1e-9, w.Terrain.Height(s.PositionXZ) + w.Water.DepthAt(s.PositionXZ) + 1e-9);
            }
        }
        Assert.True(shrimp.Zip(start).Average(z => Vec2.Distance(z.First.PositionXZ, z.Second)) > 0.1, "shrimp should actually move");
    }

    [Fact] // t-094
    public void TerrestrialLocomotionStaysOnGroundAndFollowsGradients()
    {
        var w = FaunaFixtures.PondWorld();
        // dry the eastern strip: springtails should drift toward the moist middle
        foreach (int c in w.Grid.DomainCells) if (w.Grid.CellCenter(c).X > 3) w.Fields.Moisture[c] = 0.05;
        var st = Enumerable.Range(0, 30).Select(i => w.FaunaSystem.CreateFounder(Sp("springtail"), new Vec2(3.4, -1.5 + 0.1 * i))).ToList();
        for (int t = 0; t < 1500; t++)
        {
            w.FaunaSystem.StepBehaviour(20); w.Clock.Tick += 2;
            foreach (var s in st)
            {
                Assert.True(w.Domain.Contains(s.PositionXZ));
                Assert.Equal(w.GroundHeight(s.PositionXZ), s.Y, 9);
            }
        }
        Assert.True(st.Count(s => s.X < 3) > st.Count / 2, "most springtails should leave the dry strip");
    }

    [Fact] // t-095, t-096
    public void ReproductionEligibilityAndOffspringPipeline()
    {
        var w = FaunaFixtures.PondWorld();
        var sp = Sp("springtail");
        var a = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land, ageFraction: 0.5);
        var juvenile = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land + new Vec2(0.05, 0), ageFraction: 0.01);
        a.Energy = 0.9; juvenile.Energy = 0.9;
        a.ReproCooldownUntil = 0;   // founders get a staggered first cooldown; this fixture tests eligibility itself
        Assert.True(w.FaunaSystem.CanReproduce(a, sp, 0, out var why), why);
        Assert.False(w.FaunaSystem.CanReproduce(juvenile, sp, 0, out why)); Assert.Equal("not mature", why);
        a.Energy = 0.2;
        Assert.False(w.FaunaSystem.CanReproduce(a, sp, 0, out why)); Assert.Equal("not enough energy", why);
        a.Energy = 0.9;
        var b = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land + new Vec2(0.1, 0), ageFraction: 0.5);
        var child = w.FaunaSystem.CreateOffspring(sp, a, b);
        Assert.Equal(EntityKind.Fauna, child.Id.Kind);
        Assert.Equal(a.Id, child.ParentA); Assert.Equal(b.Id, child.ParentB);
        Assert.True(w.Genomes.Contains(child.GenomeId));
        Assert.Equal(0, child.Age); Assert.Equal(sp.OffspringEnergy, child.Energy);
        Assert.Equal(FaunaLifeStage.Juvenile, child.Stage);
        Assert.NotNull(w.Lineage.Get(child.Id));
    }

    [Fact] // t-095
    public void EligibleFixtureReproducesDeterministically()
    {
        string Run()
        {
            var w = FaunaFixtures.PondWorld(77);
            for (int i = 0; i < 6; i++) { var f = w.FaunaSystem.CreateFounder(Sp("springtail"), FaunaFixtures.Land + new Vec2(0.04 * i, 0), 0.5); f.Energy = 0.95; f.ReproCooldownUntil = 0; }
            for (int i = 0; i < 10; i++) { w.FaunaSystem.StepLifecycle(300); w.Clock.Tick += 30; }
            Assert.True(w.Fauna.Count > 6, "eligible fixture must reproduce");
            return Persistence.WorldSerializer.Text(Persistence.WorldSerializer.Serialize(w)["fauna"]);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact] // t-097, t-098
    public void AgingIsTimeDrivenAndDeathReturnsDetritusOnce()
    {
        var w = FaunaFixtures.PondWorld();
        var sp = Sp("triops");
        var f = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Pond, ageFraction: 0.99);
        f.LifespanFactor = 1;
        double det0 = w.Fields.Detritus.Total();
        int steps = 0;
        while (w.Fauna.Get(f.Id) != null && steps++ < 100) { w.FaunaSystem.StepLifecycle(300); w.Clock.Tick += 30; }
        Assert.Null(w.Fauna.Get(f.Id));
        Assert.Equal("old age", w.Lineage.Get(f.Id)!.DeathCause);
        double expected = sp.MassAtMid * w.FaunaSystem.PhenotypeOf(f).MassScale * sp.DetritusOnDeath;
        Assert.Equal(expected, w.Fields.Detritus.Total() - det0, 9);
        Assert.False(w.FaunaSystem.Kill(f, "again"));
        Assert.Equal(expected, w.Fields.Detritus.Total() - det0, 9);
    }

    [Fact] // t-099
    public void SchoolingIncreasesCohesionOnlyWhenEnabled()
    {
        double Spread(bool schooling)
        {
            var content = TestUtil.Content;
            if (!schooling)
            {
                var src = new OverlayContentSource(TestUtil.ContentSource);
                var json = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "fauna", "microminnow.json"));
                src.Set("fauna/microminnow.json", System.Text.RegularExpressions.Regex.Replace(json, "\"behaviors\"\\s*:\\s*\\[\\s*\"schooling\"\\s*\\]", "\"behaviors\": []"));
                content = ContentLoader.Load(src);
                Assert.DoesNotContain("schooling", content.FaunaOrThrow("microminnow").Behaviors);
            }
            var d = TestUtil.FlatDescriptor(9);
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = -2, Z = 0, Radius = 2.2, Amount = 0.3 });
            var w = VivariumWorld.Create(content, d, false);
            TestUtil.Flood(w, FaunaFixtures.Pond, 1.5, 0.2);
            var sp = content.FaunaOrThrow("microminnow");
            var rng = Rng.Stream(1, "school");
            for (int i = 0; i < 16; i++) w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Pond + new Vec2(rng.Range(-0.9, 0.9), rng.Range(-0.9, 0.9)));
            for (int t = 0; t < 600; t++) { w.FaunaSystem.StepBehaviour(20); w.Clock.Tick += 2; }
            var pts = w.Fauna.Items.Select(f => f.PositionXZ).ToList();
            var c = new Vec2(pts.Average(p => p.X), pts.Average(p => p.Z));
            return pts.Average(p => Vec2.Distance(p, c));
        }
        double with = Spread(true), without = Spread(false);
        Assert.True(with < without * 0.75, $"schooling spread {with:0.00} m vs baseline {without:0.00} m");
    }

    public static IEnumerable<object[]> Species() => TestUtil.Content.Fauna.Select(f => new object[] { f.Id });

    [Theory] // t-112 … t-115
    [MemberData(nameof(Species))]
    public void EachSpeciesSurvivesFeedsReproducesInheritsAndRenders(string id)
    {
        var sp = Sp(id);
        var w = FaunaFixtures.PondWorld(61);
        var at = sp.Medium == Medium.Aquatic ? FaunaFixtures.Pond : FaunaFixtures.Land;
        var r = Introduction.IntroduceFauna(w, id, at, 8);
        Assert.True(r.Ok, r.Message);
        double days = Math.Max(8, sp.ReproCooldown / 86400 * 1.5);
        for (long t = 0; t < days * 8640; t++)
        {
            if (t % 8640 == 0)
            {
                FaunaFixtures.HoldWater(w);
                foreach (int c in w.Grid.DomainCells) { if (w.Water.IsWet(c)) { w.Fields.Biofilm[c] = Math.Max(w.Fields.Biofilm[c], 0.3); w.Fields.Plankton[c] = Math.Max(w.Fields.Plankton[c], 0.2); } w.Fields.Detritus[c] = Math.Max(w.Fields.Detritus[c], 1.0); }
            }
            // dry-land species get the parched ground they are built for (the fixture's land is moist by default)
            if (sp.Medium == Medium.Terrestrial && sp.Moisture.Optimum < 0.45 && t % 30 == 0)
                foreach (int c in w.Grid.DomainCells) if (!w.Water.IsWet(c)) w.Fields.Moisture[c] = sp.Moisture.Optimum;
            w.Step();
        }
        var alive = w.Fauna.Items.Where(f => f.SpeciesId == id).ToList();
        Assert.NotEmpty(alive);
        Assert.True(w.Tally.Of(id).Births > 0, $"{id} should reproduce");
        Assert.All(alive, f => Assert.False(w.FaunaSystem.Suitability(sp, f.PositionXZ).HardRefused, $"{id} in invalid habitat"));
        var offspring = alive.Where(f => !f.ParentA.IsNone).ToList();
        Assert.NotEmpty(offspring);
        Assert.All(offspring, o => Assert.True(w.Genomes.Get(o.GenomeId)!.Generation >= 1));
        Assert.True(OrganismMeshes.Fauna(sp).TriangleCount > 50);
        if (sp.Behaviors.Contains("schooling")) Assert.True(w.FaunaSystem.MeanNearestNeighbourDistance(id) < 0.3);
    }

    [Fact]
    public void PillBugsRollUpInPlaceWhenPokedWhileSpringtailsFlee()
    {
        var w = FaunaFixtures.PondWorld();
        var bug = w.FaunaSystem.CreateFounder(Sp("pill_bug"), FaunaFixtures.Land);
        var st = w.FaunaSystem.CreateFounder(Sp("springtail"), FaunaFixtures.Land + new Vec2(0.1, 0));
        var bugAt = bug.PositionXZ; var stAt = st.PositionXZ;
        Assert.False(w.FaunaSystem.IsCurled(bug));
        var res = new ToolActions(w).Poke(new PokeAction(new Vec3(FaunaFixtures.Land.X + 0.05, 0.6, FaunaFixtures.Land.Z), new Vec3(0, -1, 0), 1, WorldHit.None));
        Assert.Contains(bug.Id, res.FaunaDisturbed);
        Assert.True(w.FaunaSystem.IsCurled(bug));
        Assert.False(w.FaunaSystem.IsCurled(st));
        for (int i = 0; i < 20; i++) { w.FaunaSystem.StepBehaviour(20); w.Clock.Tick += 2; }
        Assert.Equal(bugAt.X, bug.X, 12); Assert.Equal(bugAt.Z, bug.Z, 12);
        Assert.True(Vec2.Distance(stAt, st.PositionXZ) > 0.05, "springtail should flee");
        w.Clock.Tick += (long)(w.Content.Tools.PokeDisturbSeconds / 10) + 10;
        Assert.False(w.FaunaSystem.IsCurled(bug), "unrolls once the disturbance passes");
        Assert.NotNull(OrganismMeshes.FaunaCurled(Sp("pill_bug")));
        Assert.Null(OrganismMeshes.FaunaCurled(Sp("springtail")));
    }

    [Fact] // t-114
    public void TriopsIsDistinctFromShrimp()
    {
        var s = Sp("shrimp"); var t = Sp("triops");
        Assert.NotEqual(s.Model, t.Model);
        Assert.True(t.Lifespan < s.Lifespan / 2);
        Assert.True(t.BasalRate > s.BasalRate);
        Assert.NotEqual(s.Sexual, t.Sexual);
        Assert.NotEqual(OrganismMeshes.Fauna(s).DigestHex(), OrganismMeshes.Fauna(t).DigestHex());
    }

    [Fact] // t-116
    public void FaunaLibraryLoadsCleanAndBrokenFixtureIsActionable()
    {
        Assert.Empty(TestUtil.Content.Warnings);
        Assert.Equal(new[] { "darkling_beetle", "microminnow", "pill_bug", "shrimp", "silverfish", "springtail", "triops" }, TestUtil.Content.Fauna.Select(f => f.Id));
        var src = new OverlayContentSource(TestUtil.ContentSource);
        var broken = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "fauna", "microminnow.json"))
            .Replace("\"model\": \"minnow\"", "\"model\": \"whale\"")
            .Replace("\"resource\": \"plankton\"", "\"resource\": \"krill\"")
            .Replace("\"ornament_density\",", "\"glow\",");
        src.Set("fauna/microminnow.json", broken);
        // also a terrestrial species eating an aquatic-only resource, and schooling without parameters
        var st = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "fauna", "springtail.json"))
            .Replace("\"resource\": \"detritus\"", "\"resource\": \"plankton\"").Replace("\"behaviors\": []", "\"behaviors\": [\"schooling\"]");
        src.Set("fauna/springtail.json", st);
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src));
        Assert.Contains(ex.Errors, e => e.File == "fauna/microminnow.json" && e.Path == "$.visual.model");
        Assert.Contains(ex.Errors, e => e.File == "fauna/microminnow.json" && e.Message.Contains("krill"));
        Assert.Contains(ex.Errors, e => e.File == "fauna/microminnow.json" && e.Message.Contains("unknown trait 'glow'"));
        Assert.Contains(ex.Errors, e => e.File == "fauna/springtail.json" && e.Message.Contains("aquatic-only"));
        Assert.Contains(ex.Errors, e => e.File == "fauna/springtail.json" && e.Path == "$.schooling");
    }
}

[Trait("Suite", "Genetics")]
public class GeneticsTests
{
    private static FaunaSpeciesDef Sp(string id) => FaunaFixtures.Sp(id);
    private static GeneticsConfig Cfg => TestUtil.Content.Genetics;

    private static Genome G(FaunaSpeciesDef sp, double v) => new() { Id = EntityId.Make(EntityKind.Genome, 1), SpeciesId = sp.Id, Traits = Enumerable.Repeat(v, sp.Traits.Count).ToArray() };

    [Fact] // t-102
    public void SchemaHasBoundsDefaultsAndSpeciesTraitSets()
    {
        foreach (var t in Cfg.Traits) { Assert.InRange(t.Default, t.Min, t.Max); Assert.True(t.Max > t.Min); }
        Assert.DoesNotContain("ornament_density", Sp("triops").Traits);
        Assert.Contains("ornament_density", Sp("shrimp").Traits);
    }

    [Fact] // t-103
    public void OffspringBaseTraitsAreExactParentalMidpoints()
    {
        var sp = Sp("shrimp");
        var a = new Genome { Id = EntityId.Make(EntityKind.Genome, 1), SpeciesId = sp.Id, Traits = new[] { 0.2, 0.4, 0.6, 0.8, 1.0, 0.0 } };
        var b = new Genome { Id = EntityId.Make(EntityKind.Genome, 2), SpeciesId = sp.Id, Traits = new[] { 0.4, 0.4, 0.2, 0.0, 0.5, 1.0 } };
        Assert.Equal(new[] { 0.30000000000000004, 0.4, 0.4, 0.4, 0.75, 0.5 }, Inheritance.Midpoint(a.Traits, b.Traits));
        // an offspring with no mutation event carries exactly the midpoint
        for (ulong s = 1; s < 200; s++)
        {
            var child = Inheritance.CreateOffspring(sp, Cfg, a, b, EntityId.Make(EntityKind.Genome, 100 + s), 5);
            if (child.Mutations == 0) { Assert.Equal(Inheritance.Midpoint(a.Traits, b.Traits), child.Traits); return; }
        }
        Assert.Fail("no unmutated offspring in 200 draws");
    }

    [Fact] // t-104, t-105
    public void MutationProbabilityIsTenPercentPerOffspring()
    {
        Assert.Equal(0.10, Cfg.MutationProbability);
        var sp = Sp("springtail");
        var a = G(sp, 0.5);
        int n = 20000, mutated = 0;
        var perTrait = new int[sp.Traits.Count];
        for (int i = 0; i < n; i++)
        {
            var c = Inheritance.CreateOffspring(sp, Cfg, a, a, EntityId.Make(EntityKind.Genome, (ulong)i + 10), 2026);
            if (c.Mutations > 0) { mutated++; perTrait[c.MutatedTrait]++; }
            Assert.True(c.Mutations <= 1, "at most one mutation event per offspring");
        }
        double p = (double)mutated / n, se = Math.Sqrt(0.1 * 0.9 / n);
        Assert.InRange(p, 0.10 - 4 * se, 0.10 + 4 * se);
        // mutated trait is uniform across enabled traits
        foreach (var k in perTrait) Assert.InRange(k / (double)mutated, 1.0 / perTrait.Length - 0.03, 1.0 / perTrait.Length + 0.03);
    }

    [Fact] // t-106
    public void ExtremeMutationStaysFiniteAndInBounds()
    {
        var sp = Sp("shrimp");
        var extreme = new FaunaSpeciesDef { Id = sp.Id, Traits = sp.Traits, MutationMagnitude = 50, SizeMin = sp.SizeMin, SizeMax = sp.SizeMax };
        var cfg = new GeneticsConfig { MutationProbability = 1.0, Traits = Cfg.Traits };
        foreach (var start in new[] { 0.0, 1.0 })
            for (int i = 0; i < 2000; i++)
            {
                var c = Inheritance.CreateOffspring(extreme, cfg, G(sp, start), null, EntityId.Make(EntityKind.Genome, (ulong)i + 1), 1);
                Assert.All(c.Traits, t => Assert.InRange(t, 0, 1));
                var ph = Phenotype.From(extreme, c);
                Assert.InRange(ph.BodySize, sp.SizeMin, sp.SizeMax);
                Assert.True(double.IsFinite(ph.MetabolicScale));
            }
        var nan = new[] { double.NaN, double.PositiveInfinity, -3, 7, 0.5, 0.5 };
        Inheritance.Clamp(sp, Cfg, nan);
        Assert.All(nan, t => Assert.InRange(t, 0, 1));
    }

    [Fact] // t-107
    public void SizeGeneDrivesVisibleAndSimulatedSize()
    {
        var sp = Sp("shrimp");
        var small = G(sp, 0.5); small.Traits[sp.TraitIndex("size")] = 0;
        var large = G(sp, 0.5); large.Traits[sp.TraitIndex("size")] = 1;
        var ps = Phenotype.From(sp, small); var pl = Phenotype.From(sp, large);
        Assert.Equal(sp.SizeMin, ps.BodySize, 9);
        Assert.Equal(sp.SizeMax, pl.BodySize, 9);
        Assert.True(pl.BodySize / ps.BodySize > 1.8, "extremes must be reliably different at a glance");
        Assert.True(pl.MetabolicScale > ps.MetabolicScale, "size also changes simulation-relevant metabolism");
    }

    [Fact] // t-108
    public void OrnamentationGenesProduceDistinguishableVariants()
    {
        var sp = Sp("microminnow");
        var plain = G(sp, 0.5); var fancy = G(sp, 0.5);
        foreach (var t in new[] { "ornament_density", "pattern_strength", "hue_shift", "appendage_length" }) { plain.Traits[sp.TraitIndex(t)] = 0.05; fancy.Traits[sp.TraitIndex(t)] = 0.95; }
        var a = Phenotype.From(sp, plain); var b = Phenotype.From(sp, fancy);
        Assert.True(Math.Abs(a.HueShift - b.HueShift) > 0.15);
        Assert.True(b.OrnamentDensity - a.OrnamentDensity > 0.8);
        Assert.True(b.PatternStrength - a.PatternStrength > 0.8);
        Assert.True(b.AppendageScale / a.AppendageScale > 1.5);
    }

    [Fact] // t-109
    public void LineageSurvivesParentDeath()
    {
        var w = FaunaFixtures.PondWorld();
        var sp = Sp("springtail");
        var a = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land, 0.5);
        var b = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land + new Vec2(0.05, 0), 0.5);
        var c = w.FaunaSystem.CreateOffspring(sp, a, b);
        w.FaunaSystem.Kill(a, "test"); w.FaunaSystem.Kill(b, "test");
        var parents = w.Lineage.Parents(c.Id).Select(p => p.Id).ToList();
        Assert.Equal(new[] { a.Id, b.Id }, parents);
        Assert.False(w.Lineage.Get(a.Id)!.Alive);
        w.FaunaSystem.PruneGenetics();
        Assert.NotNull(w.Lineage.Get(a.Id));                 // still needed: ancestor of a living individual
        Assert.True(w.Genomes.Contains(w.Lineage.Get(a.Id)!.GenomeId));
        w.FaunaSystem.Kill(c, "test");
        w.FaunaSystem.PruneGenetics();
        Assert.Equal(0, w.Lineage.Count);
    }

    [Fact] // t-110
    public void GenomeRoundTripsExactly()
    {
        var sp = Sp("shrimp");
        var g = Inheritance.CreateFounder(sp, Cfg, EntityId.Make(EntityKind.Genome, 42), 99);
        g.Traits[0] = 0.1 + 0.2; // non-terminating binary fraction
        var json = System.Text.Json.JsonSerializer.Serialize(g, Persistence.WorldSerializer.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<Genome>(json, Persistence.WorldSerializer.Json)!;
        Assert.Equal(g.Traits, back.Traits);
        Assert.Equal(BitConverter.DoubleToInt64Bits(g.Traits[0]), BitConverter.DoubleToInt64Bits(back.Traits[0]));
        Assert.Equal(g.Id, back.Id); Assert.Equal(g.Generation, back.Generation);
    }

    [Fact] // t-111
    public void InheritanceIsDeterministic()
    {
        string Run()
        {
            var w = FaunaFixtures.PondWorld(88);
            for (int i = 0; i < 8; i++) { var f = w.FaunaSystem.CreateFounder(Sp("triops"), FaunaFixtures.Pond + new Vec2(0.05 * i, 0), 0.4); f.Energy = 0.95; f.ReproCooldownUntil = 0; }
            for (int i = 0; i < 60; i++) { w.FaunaSystem.StepLifecycle(300); w.Clock.Tick += 30; foreach (var f in w.Fauna.Items) f.Energy = 0.95; }
            return Persistence.WorldSerializer.Text(Persistence.WorldSerializer.Serialize(w)["genetics"]);
        }
        var a = Run();
        Assert.Equal(a, Run());
        Assert.Contains("\"Generation\":1", a);
    }
}
