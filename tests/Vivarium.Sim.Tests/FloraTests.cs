using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Flora")]
public class FloraTests
{
    private static FloraSpeciesDef Sp(string id) => TestUtil.Content.FloraOrThrow(id);

    private static void RunFlora(VivariumWorld w, double days, Action? everyDay = null)
    {
        int steps = (int)(days * 86400 / 600);
        for (int i = 0; i < steps; i++)
        {
            if (i % 144 == 0) everyDay?.Invoke();
            w.FloraSystem.Step(600);
        }
    }

    [Fact] // t-065
    public void NewSpeciesNeedsOnlyData()
    {
        var src = new OverlayContentSource(TestUtil.ContentSource);
        var index = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "index.json")).Replace("\"flora/carpet_moss.json\",", "\"flora/carpet_moss.json\", \"flora/test_fern.json\",");
        var fern = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "flora", "creeping_groundcover.json"))
            .Replace("creeping_groundcover", "test_fern").Replace("Creeping Pennywort", "Test Fern").Replace("\"shape\": \"creeper\"", "\"shape\": \"herb\"");
        src.Set("index.json", index).Set("flora/test_fern.json", fern);
        var content = ContentLoader.Load(src);
        Assert.NotNull(content.FloraById("test_fern"));
        var w = VivariumWorld.Create(content, TestUtil.FlatDescriptor(), false);
        TestUtil.Condition(w, 0.6, 1.0);
        var f = w.FloraSystem.Establish(content.FloraOrThrow("test_fern"), new Vec2(0, 0), "test");
        double b0 = f.Biomass;
        RunFlora(w, 5, () => TestUtil.Condition(w, 0.6, 1.0));
        Assert.True(f.Biomass > b0);
    }

    [Fact] // t-066
    public void IndividualStateHasCommonFields()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.7, 0.5);
        var f = w.FloraSystem.Establish(Sp("carpet_moss"), new Vec2(0.5, 0.5), "test");
        Assert.Equal(EntityKind.Flora, f.Id.Kind);
        Assert.Equal("carpet_moss", f.SpeciesId);
        Assert.Equal(FloraStage.Juvenile, f.Stage(Sp("carpet_moss")));
        RunFlora(w, 8);
        Assert.Equal(FloraStage.Mature, f.Stage(Sp("carpet_moss")));
        Assert.True(f.Age > 0 && f.Biomass > 0 && f.Health > 0);
        Assert.True(f.Radius(Sp("carpet_moss")) > Sp("carpet_moss").MinRadius);
    }

    [Fact] // t-067
    public void SuitabilityIsDeterministicAndSeparatesRefusalFromLowScore()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.7, 0.5);
        var p = new Vec2(1, 1);
        var a = w.FloraSystem.Suitability(Sp("carpet_moss"), p);
        var b = w.FloraSystem.Suitability(Sp("carpet_moss"), p);
        Assert.Equal(a.Score, b.Score);
        Assert.False(a.HardRefused);
        var refused = w.FloraSystem.Suitability(Sp("crust_lichen"), p);   // crust lichen refuses soil
        Assert.True(refused.HardRefused);
        Assert.Contains("refuses soil", refused.RefusalReason);
        TestUtil.Condition(w, 0.05, 0.5);
        var dry = w.FloraSystem.Suitability(Sp("carpet_moss"), p);
        Assert.False(dry.HardRefused);
        Assert.True(dry.Score < a.Score * 0.3, "dry ground is low suitability, not refusal");
    }

    [Fact] // t-068
    public void GrowthIsTimestepStableAndBounded()
    {
        double Grow(double dt)
        {
            var w = TestUtil.FlatWorld();
            TestUtil.Condition(w, 0.7, 1.5);
            var f = w.FloraSystem.Establish(Sp("carpet_moss"), Vec2.Zero, "t");
            f.LastSpreadAge = double.MaxValue / 4; // no propagation, isolate growth
            int steps = (int)(4 * 86400 / dt);
            for (int i = 0; i < steps; i++) { w.FloraSystem.Step(dt); f.LastSpreadAge = f.Age; }
            return f.Biomass;
        }
        double fine = Grow(600), coarse = Grow(3600), veryCoarse = Grow(4 * 3600);
        Assert.InRange(coarse / fine, 0.97, 1.03);
        Assert.InRange(veryCoarse / fine, 0.95, 1.05);
        Assert.True(fine <= Sp("carpet_moss").MaxBiomass);
    }

    [Fact] // t-069
    public void GrowthConsumesLocalNutrientsDeterministically()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.7, 0.8);
        var p = new Vec2(-1, 1);
        int cell = w.Grid.CellAt(p);
        w.FloraSystem.Establish(Sp("creeping_groundcover"), p, "t");
        double before = w.Fields.Nutrients[cell];
        RunFlora(w, 2);
        Assert.True(w.Fields.Nutrients[cell] < before);
        Assert.True(w.Fields.Nutrients[cell] >= 0);
        var w2 = TestUtil.FlatWorld();
        TestUtil.Condition(w2, 0.7, 0.8);
        w2.FloraSystem.Establish(Sp("creeping_groundcover"), p, "t");
        RunFlora(w2, 2);
        Assert.Equal(w.Fields.Nutrients.DigestHex(), w2.Fields.Nutrients.DigestHex());
    }

    [Fact] // t-070, t-080
    public void PropagationNeverEntersRefusedHabitatAndIsReproducible()
    {
        string Run(out VivariumWorld w)
        {
            w = TestUtil.FlatWorld(21);
            TestUtil.Condition(w, 0.35, 0.2, 0.95);
            Assert.True(w.Placement.PlaceRock(new Vec2(0, 0), 0.7, 0, 5).Ok);
            var lichen = Sp("crust_lichen");
            Assert.True(w.FloraSystem.CanEstablish(lichen, new Vec2(0, 0), out var why), why);
            Assert.False(w.FloraSystem.CanEstablish(lichen, new Vec2(2, 2), out _));
            w.FloraSystem.Establish(lichen, new Vec2(0, 0), "t", lichen.MaxBiomass * 0.8).Age = lichen.MaturityAge;
            var ww = w;
            RunFlora(w, 120, () => TestUtil.Condition(ww, 0.35, 0.2, 0.95));
            return Persistence.WorldSerializer.Text(Persistence.WorldSerializer.Serialize(w)["flora"]);
        }
        var s1 = Run(out var w1);
        var s2 = Run(out _);
        Assert.Equal(s1, s2);
        var lichens = w1.Flora.Items.Where(f => f.SpeciesId == "crust_lichen").ToList();   // spore rain may add decomposers on the soil
        Assert.True(lichens.Count > 1, "lichen should have spread across the rock");
        foreach (var f in lichens) Assert.NotEqual(Substrate.Soil, w1.SubstrateAt(f.Position));
    }

    [Fact] // t-071
    public void MossRefusesFreshBarkEvenWhenConditionsAreIdeal()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.68, 0.5, 0.45);
        var fresh = w.Placement.PlaceLog(new Vec2(-1.5, 0), 0, 2.0, 0.25, 0, 1);
        var rotten = w.Placement.PlaceLog(new Vec2(1.5, 1.5), 0, 2.0, 0.25, 3, 2);
        Assert.True(fresh.Ok && rotten.Ok);
        TestUtil.Condition(w, 0.68, 0.5, 0.45);
        var onFresh = w.FloraSystem.Suitability(Sp("carpet_moss"), new Vec2(-1.5, 0));
        Assert.True(onFresh.HardRefused);
        Assert.Contains("bark_fresh", onFresh.RefusalReason);
        Assert.False(w.FloraSystem.CanEstablish(Sp("carpet_moss"), new Vec2(-1.5, 0), out _));
        Assert.False(w.FloraSystem.Suitability(Sp("carpet_moss"), new Vec2(1.5, 1.5)).HardRefused);
        Assert.True(w.FloraSystem.Suitability(Sp("foliose_lichen"), new Vec2(1.5, 1.5)).HardRefused, "leafy lichen refuses rotting wood");
    }

    [Fact] // t-072
    public void BeneficialProximityOnlyWithinConfiguredDistance()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.68, 0.5, 0.45);
        Assert.True(w.Placement.PlaceLog(new Vec2(0, 0), 0, 2.0, 0.15, 2, 1).Ok);
        TestUtil.Condition(w, 0.68, 0.5, 0.45);
        var moss = Sp("carpet_moss");
        double near = w.FloraSystem.ProximityBonus(moss, new Vec2(0, 0.45));
        double far = w.FloraSystem.ProximityBonus(moss, new Vec2(0, 2.0));
        Assert.Equal(0.15, near, 6);
        Assert.Equal(0, far);
        // species-to-species benefit from the interaction matrix
        var rush = w.FloraSystem.Establish(Sp("marginal_waterside"), new Vec2(3, 0), "t");
        Assert.True(w.FloraSystem.ProximityBonus(Sp("wetbank_moss"), new Vec2(3.3, 0)) >= 0.15 - 1e-9);
        Assert.Equal(0, w.FloraSystem.ProximityBonus(Sp("wetbank_moss"), new Vec2(3.9, 0)));
    }

    [Fact] // t-073
    public void OvercrowdingReducesGrowthAndRecruitment()
    {
        double Growth(bool crowded)
        {
            var w = TestUtil.FlatWorld();
            TestUtil.Condition(w, 0.68, 1.0);
            var sp = Sp("carpet_moss");
            var f = w.FloraSystem.Establish(sp, Vec2.Zero, "t");
            if (crowded)
                for (int k = 0; k < 6; k++) w.FloraSystem.Establish(sp, Vec2.FromAngle(k * Math.PI / 3) * 0.2, "t", sp.MaxBiomass);
            double b0 = f.Biomass;
            for (int i = 0; i < 144; i++) w.FloraSystem.Step(600);
            if (crowded) Assert.False(w.FloraSystem.CanEstablish(sp, new Vec2(0.05, 0.05), out var why) && why == "ok");
            return f.Biomass - b0;
        }
        double alone = Growth(false), crowdedGrowth = Growth(true);
        Assert.True(crowdedGrowth < alone * 0.6, $"crowded {crowdedGrowth:0.000} vs alone {alone:0.000}");
    }

    [Fact] // t-074
    public void DeathReturnsLitterExactlyOnce()
    {
        var w = TestUtil.FlatWorld();
        var sp = Sp("creeping_groundcover");
        var f = w.FloraSystem.Establish(sp, new Vec2(1, -1), "t", 1.0);
        double det0 = w.Fields.Detritus.Total(), nut0 = w.Fields.Nutrients.Total();
        Assert.True(w.FloraSystem.Kill(f, "test"));
        double det1 = w.Fields.Detritus.Total();
        Assert.Equal(1.0 * sp.LitterFraction, det1 - det0, 9);
        Assert.Equal(1.0 * (1 - sp.LitterFraction) * sp.NutrientPerBiomass, w.Fields.Nutrients.Total() - nut0, 9);
        Assert.Null(w.Flora.Get(f.Id));
        Assert.False(w.FloraSystem.Kill(f, "again"));
        Assert.Equal(det1, w.Fields.Detritus.Total());
        Assert.Equal(1, w.Tally.Of(sp.Id).Deaths);
    }

    public static IEnumerable<object[]> SpeciesFixtures() => new[]
    {
        new object[] { "carpet_moss" }, new object[] { "cushion_moss" }, new object[] { "wetbank_moss" }, new object[] { "crust_lichen" },
        new object[] { "foliose_lichen" }, new object[] { "creeping_groundcover" }, new object[] { "marginal_waterside" }, new object[] { "ornamental_herb" },
        new object[] { "fern" }, new object[] { "climbing_vine" }, new object[] { "bonnet_mushroom" }, new object[] { "turkey_tail" },
        new object[] { "stonecrop" }, new object[] { "blue_fescue" }, new object[] { "reindeer_lichen" },
    };

    /// <summary>Builds the habitat each archetype is designed for.</summary>
    public static (VivariumWorld W, Vec2 P, Action Hold) Habitat(string id)
    {
        var w = TestUtil.FlatWorld(31);
        Vec2 p = new(0.5, 0.5);
        (double m, double n, double? l) cond = (0.6, 0.8, null);
        double? detritus = null;   // decomposers eat dead matter, held at this level
        switch (id)
        {
            case "carpet_moss": cond = (0.68, 0.5, 0.5); break;
            case "cushion_moss": w.Placement.PlaceRock(p, 0.7, 0, 3); cond = (0.55, 0.3, 0.6); break;
            case "wetbank_moss": TestUtil.Flood(w, new Vec2(-1, 0.5), 1.0, 0.05); p = new Vec2(0.25, 0.5); cond = (0.95, 0.7, 0.5); break;
            case "crust_lichen": w.Placement.PlaceRock(p, 0.8, 0, 4); cond = (0.35, 0.2, 0.9); break;
            case "foliose_lichen": w.Placement.PlaceLog(p, 0.3, 2.4, 0.3, 1, 5); cond = (0.5, 0.3, 0.65); break;
            case "creeping_groundcover": cond = (0.6, 1.0, 0.6); break;
            case "marginal_waterside": TestUtil.Flood(w, new Vec2(-1, 0.5), 1.0, 0.04); p = new Vec2(0.15, 0.5); cond = (1.0, 0.9, 0.7); break;
            case "ornamental_herb": cond = (0.5, 1.1, 0.8); break;
            case "fern": cond = (0.72, 0.8, 0.35); break;
            case "stonecrop": cond = (0.22, 0.3, 0.85); break;
            case "blue_fescue": cond = (0.28, 0.6, 0.8); break;
            case "reindeer_lichen": cond = (0.25, 0.1, 0.8); break;
            case "climbing_vine": w.Placement.PlaceLog(p + new Vec2(0.4, 0.35), 0, 2.4, 0.12, 1, 6); cond = (0.6, 0.9, 0.6); break;
            case "bonnet_mushroom": cond = (0.78, 0.3, 0.3); detritus = 1.2; break;
            case "turkey_tail": w.Placement.PlaceLog(p + new Vec2(0.6, 0), 0, 2.6, 0.25, 2, 7); cond = (0.6, 0.3, 0.3); detritus = 1.0; break;
        }
        void Hold()
        {
            foreach (int c in w.Grid.DomainCells)
            {
                if (!w.Water.IsWet(c)) w.Fields.Moisture[c] = cond.m;
                else w.Fields.Moisture[c] = 1;
                w.Fields.Nutrients[c] = cond.n;
                if (cond.l.HasValue) w.Fields.Light[c] = cond.l.Value;
                if (detritus.HasValue) w.Fields.Detritus[c] = detritus.Value;
            }
        }
        Hold();
        return (w, p, Hold);
    }

    [Theory] // t-077 … t-084
    [MemberData(nameof(SpeciesFixtures))]
    public void EachArchetypeEstablishesGrowsSpreadsAndRenders(string id)
    {
        var sp = Sp(id);
        var (w, p, hold) = Habitat(id);
        Assert.True(w.FloraSystem.CanEstablish(sp, p, out var why), $"{id}: {why}");
        var f = w.FloraSystem.Establish(sp, p, "fixture");
        double b0 = f.Biomass;
        double days = Math.Max(45, (sp.MaturityAge + sp.SpreadInterval * 3) / 86400);
        RunFlora(w, days, hold);
        Assert.True(w.Flora.Count > 1, $"{id} should propagate (count {w.Flora.Count})");
        Assert.True(w.Flora.Items.Max(x => x.Biomass) > b0 * 2, $"{id} should grow");
        Assert.All(w.Flora.Items, x => Assert.False(w.FloraSystem.Suitability(sp, x.Position, x.Id).HardRefused));
        var mesh = OrganismMeshes.Flora(sp);
        Assert.True(mesh.TriangleCount > 20);
    }

    [Fact] // t-078, t-081
    public void ArchetypesAreGenuinelyDistinct()
    {
        var carpet = Sp("carpet_moss"); var cushion = Sp("cushion_moss");
        Assert.NotEqual(carpet.Shape, cushion.Shape);
        Assert.NotEqual(carpet.GrowthRate, cushion.GrowthRate);
        Assert.NotEqual(carpet.RadiusAtMax, cushion.RadiusAtMax);
        Assert.True(cushion.SubstrateAffinity[Substrate.Rock] > carpet.SubstrateAffinity[Substrate.Rock]);
        var crust = Sp("crust_lichen"); var foliose = Sp("foliose_lichen");
        Assert.NotEqual(crust.Shape, foliose.Shape);
        Assert.NotEqual(crust.GrowthRate, foliose.GrowthRate);
        Assert.True(foliose.SubstrateAffinity[Substrate.Wood] > crust.SubstrateAffinity.GetValueOrDefault(Substrate.Wood));
        Assert.NotEqual(OrganismMeshes.Flora(crust).DigestHex(), OrganismMeshes.Flora(foliose).DigestHex());
    }

    [Fact] // t-079, t-083
    public void WaterMarginSpecialistsPreferBanks()
    {
        var (w, bank, _) = Habitat("marginal_waterside");
        var dry = new Vec2(3.5, -2);
        foreach (int c in w.Grid.CellsInRadius(dry, 1)) w.Fields.Moisture[c] = 0.3;
        foreach (var id in new[] { "wetbank_moss", "marginal_waterside" })
        {
            var sBank = w.FloraSystem.Suitability(Sp(id), bank);
            var sDry = w.FloraSystem.Suitability(Sp(id), dry);
            Assert.False(sBank.HardRefused, sBank.ToString());
            Assert.True(sDry.HardRefused || sDry.Score < sBank.Score * 0.3, $"{id}: bank {sBank} vs dry {sDry}");
        }
        Assert.True(w.FloraSystem.Suitability(Sp("marginal_waterside"), dry).HardRefused, "persistently dry interior is rejected");
    }

    [Fact] // t-085
    public void InteractionMatrixIsCompleteAndExplained()
    {
        var m = TestUtil.Content.FloraInteractions;
        var ids = TestUtil.Content.Flora.Select(f => f.Id).ToHashSet();
        Assert.NotEmpty(m.Relations);
        foreach (var r in m.Relations)
        {
            Assert.Contains(r.A, ids); Assert.Contains(r.B, ids);
            if (r.Type != FloraRelationType.Neutral) Assert.False(string.IsNullOrWhiteSpace(r.Reason));
        }
        Assert.Contains(m.Relations, r => r.Type == FloraRelationType.Refuse);
        Assert.Contains(m.Relations, r => r.Type == FloraRelationType.Benefit);
        Assert.Contains(m.Relations, r => r.Type == FloraRelationType.Compete);
        Assert.Contains(m.Relations, r => r.Type == FloraRelationType.Neutral);
        var broken = new OverlayContentSource(TestUtil.ContentSource);
        broken.Set("flora_interactions.json", "{ \"relations\": [ { \"a\": \"carpet_moss\", \"b\": \"ghost_moss\", \"type\": \"compete\", \"strength\": 1, \"reason\": \"x\" } ] }");
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(broken));
        Assert.Contains(ex.Errors, e => e.Message.Contains("ghost_moss"));
    }

    [Fact] // t-086
    public void FloraSuiteIsDeterministic()
    {
        string Run()
        {
            var w = TestUtil.DefaultWorld();
            for (int i = 0; i < 20 * 144; i++) w.FloraSystem.Step(600);
            return Persistence.WorldSerializer.Text(Persistence.WorldSerializer.Serialize(w)["flora"]);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void DecomposersTurnDeadMatterIntoSoilNutrients()
    {
        var w = TestUtil.FlatWorld(33);
        TestUtil.Condition(w, 0.78, 0.0, 0.3);
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 1.5;
        var sp = Sp("bonnet_mushroom");
        var f = w.FloraSystem.Establish(sp, new Vec2(0.5, 0.5), "t");
        double det0 = w.Fields.Detritus.Total(), nut0 = w.Fields.Nutrients.Total();
        for (int i = 0; i < 48; i++) w.FloraSystem.Step(3600);
        Assert.True(f.Biomass > sp.InitialBiomass * 2, $"the troop should grow on litter alone ({f.Biomass:0.000})");
        Assert.True(w.Tally.DetritusDecomposed > 0 && w.Tally.NutrientsFromDecomposers > 0);
        Assert.True(w.Fields.Detritus.Total() < det0);
        Assert.True(w.Fields.Nutrients.Total() > nut0, "decomposition releases nutrients");
        Assert.Equal(0, w.Tally.NutrientsUptake, 12);   // fungi never draw on soil nutrients
    }

    [Fact]
    public void ClimbersNeedSomethingToClimb()
    {
        var w = TestUtil.FlatWorld(34);
        TestUtil.Condition(w, 0.6, 0.9, 0.6);
        var vine = Sp("climbing_vine");
        var open = new Vec2(-2, -2);
        var s = w.FloraSystem.Suitability(vine, open);
        Assert.True(s.HardRefused && s.RefusalReason.Contains("log"), s.ToString());
        w.Placement.PlaceLog(open + new Vec2(0.4, 0.25), 0, 2.0, 0.12, 1, 8);
        Assert.False(w.FloraSystem.Suitability(vine, open).HardRefused);
        Assert.True(w.FloraSystem.Suitability(Sp("turkey_tail"), new Vec2(2.5, 2.5)).HardRefused, "bracket fungi only grow on logs");
    }

    [Fact]
    public void SlimeMoldCreepsTowardFoodThenFruitsWhenItRunsOut()
    {
        var w = TestUtil.FlatWorld(35);
        TestUtil.Condition(w, 0.8, 0.2, 0.2);
        var sp = Sp("slime_mold");
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0.5;
        var food = new Vec2(1.5, 0.5);
        foreach (int c in w.Grid.CellsInRadius(food, 0.6)) w.Fields.Detritus[c] = 3.0;
        var f = w.FloraSystem.Establish(sp, new Vec2(0, 0.5), "t", 0.5);
        var start = f.Position;
        double Nearest() => w.Flora.Items.Where(x => x.SpeciesId == sp.Id).Min(x => Vec2.Distance(x.Position, food));
        double d0 = Nearest();
        for (int i = 0; i < 48; i++) w.FloraSystem.Step(3600);
        var patches = w.Flora.Items.Where(x => x.SpeciesId == sp.Id).ToList();
        Assert.True(Nearest() < d0 - 0.4, $"the network should grow toward the food: {d0:0.00} → {Nearest():0.00}");
        Assert.True(patches.Count >= 3, $"it grows as a network of patches, not one body ({patches.Count})");
        Assert.Equal(start.X, f.Position.X, 12);   // no patch slides: fronts bud, the body stays put
        Assert.All(patches.Where(x => x.Id != f.Id), x => Assert.False(x.ParentId.IsNone));
        // starve it: the network fruits, releases spores and dies back
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0.05;
        foreach (int c in w.Grid.CellsInRadius(new Vec2(-1.5, -1.0), 0.8)) w.Fields.Detritus[c] = 2.0;
        bool fruited = false;
        for (int i = 0; i < 24 * 8 && patches.Any(x => w.Flora.Get(x.Id) != null); i++)
        {
            w.FloraSystem.Step(3600);
            fruited |= w.Flora.Items.Any(x => x.SpeciesId == sp.Id && x.Fruiting);
        }
        Assert.True(fruited, "a starving plasmodium fruits");
        Assert.All(patches, x => Assert.Null(w.Flora.Get(x.Id)));
        Assert.Empty(w.CheckInvariants());
        Assert.NotNull(OrganismMeshes.FloraFruiting(sp));
        Assert.Null(OrganismMeshes.FloraFruiting(Sp("carpet_moss")));
    }

    [Fact]
    public void SlimeMoldNetworkTintsDarkAtRootBrightAtFront()
    {
        var w = TestUtil.FlatWorld(37);
        TestUtil.Condition(w, 0.8, 0.2, 0.2);
        var sp = Sp("slime_mold");
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0.5;
        var food = new Vec2(1.5, 0.5);
        foreach (int c in w.Grid.CellsInRadius(food, 0.6)) w.Fields.Detritus[c] = 3.0;
        var root = w.FloraSystem.Establish(sp, new Vec2(0, 0.5), "t", 0.5);
        Assert.Equal(0, root.Generation);
        for (int i = 0; i < 48; i++) w.FloraSystem.Step(3600);
        var patches = w.Flora.Items.Where(x => x.SpeciesId == sp.Id).ToList();
        Assert.True(patches.Count >= 3);
        foreach (var child in patches.Where(x => !x.ParentId.IsNone))
        {
            var parent = w.Flora.Get(child.ParentId);
            Assert.NotNull(parent);
            Assert.Equal(parent!.Generation + 1, child.Generation);
        }
        var front = patches.Where(x => x.Id != root.Id).OrderByDescending(x => x.Generation).First();
        Assert.True(front.Generation > root.Generation, "growth front should be deeper in the network than the root");
        Assert.NotEqual(root.Tint[2], front.Tint[2], 2);   // ochre root vs. yellow front differ in blue channel
    }

    [Fact]
    public void DecomposersReturnFromTheSporeBankWhenLitterIsRich()
    {
        var w = TestUtil.FlatWorld(36);
        TestUtil.Condition(w, 0.8, 0.2, 0.25);
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 1.2;
        Assert.DoesNotContain(w.Flora.Items, f => f.SpeciesId == "bonnet_mushroom");
        for (int day = 0; day < 6; day++) for (int h = 0; h < 24; h++) { w.FloraSystem.Step(3600); w.Clock.Tick += 360; }
        var fungi = w.Flora.Items.Where(f => f.SpeciesId == "bonnet_mushroom").ToList();
        Assert.NotEmpty(fungi);
        Assert.True(w.Flora.Items.Count(f => f.SpeciesId == "slime_mold") > 0, "slime mold spores sprout too");
        Assert.Empty(w.CheckInvariants());
    }

    [Fact] // colony-edge growth: rim buds, interior thickens
    public void ColonyGrowsAtRimOnlyAndInteriorHeightRises()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.7, 0.5);
        var sp = Sp("carpet_moss");
        Assert.NotNull(sp.Colony);
        var founder = w.FloraSystem.Establish(sp, Vec2.Zero, "test");
        founder.Biomass = sp.MaxBiomass;
        RunFlora(w, 25);
        var cells = w.Flora.Items.Where(f => f.SpeciesId == "carpet_moss").ToList();
        Assert.True(cells.Count > 5, "a mature colony should have budded several rim cells");
        // rim cells (few same-species neighbours within 2*cellRadius) should have advanced least in height;
        // the interior founder, long surrounded, should have thickened noticeably.
        double cellR = sp.Colony!.CellRadius;
        bool IsEdge(FloraIndividual f)
        {
            int n = 0;
            foreach (var o in cells) if (o.Id != f.Id && Vec2.Distance(o.Position, f.Position) <= cellR * 2) n++;
            return n < 3;
        }
        var interior = cells.Where(f => !IsEdge(f)).ToList();
        var rim = cells.Where(IsEdge).ToList();
        Assert.NotEmpty(interior);
        Assert.NotEmpty(rim);
        Assert.True(interior.Average(f => f.HeightFactor) > rim.Average(f => f.HeightFactor),
            "interior cells should have thickened more than the actively-budding rim");
        Assert.Empty(w.CheckInvariants());
    }

    [Fact] // colony-edge growth: lichen tint bands with distance from the colony root
    public void LichenTintCorrelatesWithRingDist()
    {
        var w = TestUtil.FlatWorld();
        w.Placement.PlaceLog(new Vec2(0, 0), 0, 3.0, 0.35, 1, 5);   // foliose_lichen refuses soil; needs wood/rock
        TestUtil.Condition(w, 0.55, 0.65);
        var sp = Sp("foliose_lichen");
        Assert.NotNull(sp.Colony);
        Assert.Equal(ColonyPattern.Banded, sp.Colony!.PatternMode);
        var founder = w.FloraSystem.Establish(sp, Vec2.Zero, "test");
        founder.Biomass = sp.MaxBiomass;
        RunFlora(w, 60);
        var cells = w.Flora.Items.Where(f => f.SpeciesId == "foliose_lichen" && f.RingDist > 0).ToList();
        Assert.True(cells.Count > 4, "the lichen colony should have budded outward");
        // banded tint is a function of RingDist / bandWidth: cells at similar ring distance should land in the
        // same or an adjacent palette band far more often than by chance across the whole palette.
        int SameOrAdjacentBand(FloraIndividual a, FloraIndividual b)
        {
            double bw = sp.Colony!.BandWidth, n = sp.Colony!.Palette.Length;
            int ia = (int)Math.Floor(a.RingDist / bw) % (int)n, ib = (int)Math.Floor(b.RingDist / bw) % (int)n;
            return Math.Abs(ia - ib) <= 1 ? 1 : 0;
        }
        var ordered = cells.OrderBy(f => f.RingDist).ToList();
        int hits = 0;
        for (int i = 1; i < ordered.Count; i++) hits += SameOrAdjacentBand(ordered[i - 1], ordered[i]);
        Assert.True(hits >= (ordered.Count - 1) / 2, "neighbouring ring distances should usually land in nearby tint bands");
    }
}
