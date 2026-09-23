using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class EcologyTests
{
    [Fact] // t-117
    public void FloraAndFaunaDeathShareOneDetritusPathwayWithoutDoubleCounting()
    {
        var w = TestUtil.FlatWorld(17);
        var fl = w.FloraSystem.Establish(TestUtil.Content.FloraOrThrow("carpet_moss"), new Vec2(2, 1), "t", 0.8);
        var fa = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), new Vec2(2, -1));
        double d0 = w.Fields.Detritus.Total();
        w.FloraSystem.Kill(fl, "t"); w.FaunaSystem.Kill(fa, "t");
        double d1 = w.Fields.Detritus.Total();
        Assert.Equal(w.Tally.DetritusFromFlora + w.Tally.DetritusFromFauna, d1 - d0, 12);
        Assert.True(w.Tally.DetritusFromFlora > 0 && w.Tally.DetritusFromFauna > 0);
        // detritus decays into nutrients through the same pathway
        double n0 = w.Fields.Nutrients.Total();
        w.Ecology.StepResources(86400);
        Assert.True(w.Fields.Detritus.Total() < d1);
        Assert.True(w.Fields.Nutrients.Total() > n0 - 1e-9);
        Assert.True(w.Tally.NutrientsFromDecay > 0);
    }

    [Fact] // t-118
    public void SpringtailsGainEnergyByProcessingDetritus()
    {
        var w = FaunaFixtures.PondWorld(detritus: 1.0);
        var st = Enumerable.Range(0, 10).Select(i => w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), FaunaFixtures.Land + new Vec2(0.03 * i, 0))).ToList();
        foreach (var s in st) s.Energy = 0.3;
        double det = w.Fields.Detritus.Total();
        for (int i = 0; i < 200; i++) w.FaunaSystem.StepMetabolism(30);
        Assert.All(st, s => Assert.True(s.Energy > 0.3));
        Assert.True(w.Fields.Detritus.Total() < det);
    }

    [Fact] // t-119
    public void EveryAquaticSpeciesHasAValidFoodPathway()
    {
        foreach (var sp in TestUtil.Content.Fauna.Where(f => f.Medium == Medium.Aquatic))
        {
            Assert.NotEmpty(sp.Diet);
            foreach (var d in sp.Diet)
            {
                var res = TestUtil.Content.Ecology.Resources.SingleOrDefault(r => r.Id == d.Resource);
                Assert.NotNull(res);
                Assert.NotEqual("terrestrial", res!.Medium);
            }
        }
        var w = TestUtil.DefaultWorld(populate: false);
        for (int i = 0; i < 20; i++) w.Ecology.StepResources(3600);
        Assert.True(w.Fields.Biofilm.Total() > 0 && w.Fields.Plankton.Total() > 0, "aquatic primary food grows in wet cells");
    }

    [Fact] // t-120
    public void FedPopulationReturnsBoundedWasteNutrients()
    {
        var w = FaunaFixtures.PondWorld(detritus: 2.0);
        for (int i = 0; i < 20; i++) w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), FaunaFixtures.Land + new Vec2(0.04 * i, 0)).Energy = 0.3;
        for (int i = 0; i < 400; i++) w.FaunaSystem.StepMetabolism(30);
        Assert.True(w.Tally.NutrientsFromWaste > 0);
        Assert.True(w.Fields.Nutrients.AllFinite());
        foreach (int c in w.Grid.DomainCells) Assert.InRange(w.Fields.Nutrients[c], 0, w.Content.Ecology.NutrientMax);
    }

    [Fact] // t-121
    public void ScarceResourcesSustainLowerGrowthThanAbundant()
    {
        int Run(double food)
        {
            var w = FaunaFixtures.PondWorld(71, detritus: food);
            foreach (int c in w.Grid.DomainCells) w.Fields.Nutrients[c] = 0;
            var sp = FaunaFixtures.Sp("springtail");
            for (int i = 0; i < 20; i++) { var f = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land + new Vec2(0.04 * (i % 5), 0.04 * (i / 5)), 0.4); f.Energy = 0.7; }
            for (long t = 0; t < 12 * 8640; t++)
            {
                w.FaunaSystem.StepBehaviour(10); if (t % 3 == 0) w.FaunaSystem.StepMetabolism(30); if (t % 30 == 0) w.FaunaSystem.StepLifecycle(300);
                w.Clock.Tick++;
            }
            return w.Fauna.CountOf("springtail");
        }
        int scarce = Run(0.002), abundant = Run(3.0);
        Assert.True(abundant > scarce * 1.5, $"abundant {abundant} vs scarce {scarce}");
    }

    [Fact] // t-122
    public void AnyCatalogSpeciesCanBeReintroducedWithoutReset()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var f in w.Fauna.Items.Where(x => x.SpeciesId == "triops").ToList()) w.FaunaSystem.Kill(f, "extinction test");
        Assert.Equal(0, w.Fauna.CountOf("triops"));
        int pond = w.Grid.DomainCells.OrderByDescending(c => w.Water.Depth[c]).First();
        var r = Introduction.IntroduceFauna(w, "triops", w.Grid.CellCenter(pond), 5);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(5, w.Fauna.CountOf("triops"));
        foreach (var fl in w.Flora.Items.Where(x => x.SpeciesId == "ornamental_herb").ToList()) w.FloraSystem.Kill(fl, "t");
        var spot = w.Grid.DomainCells.Select(c => w.Grid.CellCenter(c)).First(p => w.FloraSystem.CanEstablish(w.Content.FloraOrThrow("ornamental_herb"), p, out _));
        Assert.True(Introduction.IntroduceFlora(w, "ornamental_herb", spot).Ok);
        Assert.False(Introduction.IntroduceFauna(w, "triops", new Vec2(-6, -3), 1).Ok);  // dry ground refused
        Assert.Empty(w.CheckInvariants());
    }

    [Fact] // t-123
    public void StatisticsDeriveFromStateWithoutSideEffects()
    {
        var w = TestUtil.DefaultWorld();
        w.Step(500);
        string before = TestUtil.Digest(w);
        var s1 = EcosystemStatistics.Compute(w);
        var s2 = EcosystemStatistics.Compute(w);
        Assert.Equal(before, TestUtil.Digest(w));
        Assert.Equal(s1.FaunaTotal, w.Fauna.Count);
        Assert.Equal(s1.FloraTotal, w.Flora.Count);
        Assert.Equal(s1.Fauna.Select(f => f.Count), s2.Fauna.Select(f => f.Count));
        Assert.All(s1.Fauna.Where(f => f.Count > 0), f => Assert.Contains("size", f.MeanTraits.Keys));
    }

    [Fact] // t-124
    [Trait("Speed", "Slow")]
    public void FourWeekSoakStaysValidAndDeterministic()
    {
        string Run(out VivariumWorld w)
        {
            w = TestUtil.DefaultWorld();
            for (int day = 0; day < 28; day++)
            {
                w.Step(8640);
                var inv = w.CheckInvariants();
                Assert.True(inv.Count == 0, $"day {day}: {string.Join("; ", inv.Take(5))}");
            }
            return TestUtil.Digest(w);
        }
        var d1 = Run(out var world);
        Assert.Equal(28.0, world.Clock.SimDays, 6);
        Assert.True(world.Fauna.Count > 0 && world.Flora.Count > 0);
        Assert.Equal(d1, Run(out _));
    }
}

[Trait("Suite", "Tools")]
public class ToolTests
{
    private static (VivariumWorld W, ToolActions T) Setup()
    {
        var w = FaunaFixtures.PondWorld(91);
        return (w, new ToolActions(w));
    }

    [Fact] // t-125
    public void SelectionResolvesByDocumentedPriority()
    {
        var (w, _) = Setup();
        var p = new Vec2(2, 1);
        var fl = w.FloraSystem.Establish(w.Content.FloraOrThrow("creeping_groundcover"), p, "t", 1.0);
        var eye = new Vec3(p.X, 3, p.Z);
        var hit = Selection.Raycast(w, eye, new Vec3(0, -1, 0));
        Assert.Equal(HitKind.Flora, hit.Kind); Assert.Equal(fl.Id, hit.Id);
        var fa = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), p);
        hit = Selection.Raycast(w, eye, new Vec3(0, -1, 0));
        Assert.Equal(HitKind.Fauna, hit.Kind); Assert.Equal(fa.Id, hit.Id);
        var rock = w.Placement.PlaceRock(new Vec2(-0.5, 2.5), 0.4, 0, 1);
        hit = Selection.Raycast(w, new Vec3(-0.5, 3, 2.5), new Vec3(0, -1, 0));
        Assert.Equal(HitKind.Rock, hit.Kind); Assert.Equal(rock.Id, hit.Id);
        hit = Selection.Raycast(w, new Vec3(3.5, 3, -2), new Vec3(0, -1, 0));
        Assert.Equal(HitKind.Terrain, hit.Kind);
        Assert.Equal(w.SurfaceHeight(new Vec2(3.5, -2)), hit.Point.Y, 3);
        Assert.False(Selection.Raycast(w, new Vec3(0, 3, 0), new Vec3(0, 1, 0)).IsHit);
        // an organism hidden behind a rock is not selected through it
        var hidden = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), new Vec2(-0.5, 2.5));
        hit = Selection.Raycast(w, new Vec3(-2, 0.8, 2.5), new Vec3(1, -0.2, 0));
        Assert.NotEqual(hidden.Id, hit.Id);
    }

    [Fact] // t-126, t-127
    public void GrabAndReleaseKeepIdentityAndValidateHabitat()
    {
        var (w, t) = Setup();
        var shrimp = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("shrimp"), FaunaFixtures.Pond);
        var id = shrimp.Id; var genome = shrimp.GenomeId; var origin = shrimp.Position;
        Assert.True(t.Grab(id).Ok);
        Assert.True(t.MoveHeld(id, new Vec3(1, 1.5, 1)).Ok);
        w.Step(60);
        Assert.Equal(1, shrimp.X, 9);                       // held animals are not moved by behaviour
        var bad = t.Release(id, FaunaFixtures.Land);
        Assert.False(bad.Ok);
        Assert.StartsWith("Can't release here", bad.Message);
        Assert.NotNull(t.ReleaseProblem(id, FaunaFixtures.Land));
        Assert.True(shrimp.Grabbed, "invalid release keeps the critter held");
        var good = t.Release(id, FaunaFixtures.Pond + new Vec2(0.3, 0));
        Assert.True(good.Ok, good.Message);
        Assert.Equal(id, shrimp.Id); Assert.Equal(genome, shrimp.GenomeId);
        Assert.False(shrimp.Grabbed);
        Assert.True(w.Water.DepthAt(shrimp.PositionXZ) >= FaunaFixtures.Sp("shrimp").MinWaterDepth);
        Assert.True(t.Grab(id).Ok);
        Assert.True(t.ReturnHeld(id, origin).Ok);
    }

    [Fact] // t-128
    public void RemovePlantUsesEcologicalPathwayOnce()
    {
        var (w, t) = Setup();
        var f = w.FloraSystem.Establish(w.Content.FloraOrThrow("ornamental_herb"), new Vec2(2, 2), "t", 0.5);
        double det = w.Fields.Detritus.Total();
        Assert.True(t.RemovePlant(f.Id).Ok);
        Assert.Null(w.Flora.Get(f.Id));
        double after = w.Fields.Detritus.Total();
        Assert.True(after > det);
        Assert.False(t.RemovePlant(f.Id).Ok);
        Assert.Equal(after, w.Fields.Detritus.Total());
        w.FloraSystem.Step(600);   // no longer participates
        Assert.DoesNotContain(w.Flora.Items, x => x.Id == f.Id);
    }

    [Fact] // t-129
    public void NutrientToolOnlyChangesItsFootprint()
    {
        var (w, t) = Setup();
        var before = w.Fields.Nutrients.ExportDomainValues();
        var centre = new Vec2(2, 0);
        var r = t.ApplyNutrients(centre, 0.5);
        Assert.True(r.Ok);
        Assert.Contains("added", r.Message);
        var after = w.Fields.Nutrients.ExportDomainValues();
        for (int k = 0; k < after.Length; k++)
        {
            var p = w.Grid.CellCenter(w.Grid.DomainCells[k]);
            if (Vec2.Distance(p, centre) > 0.5) Assert.Equal(before[k], after[k]);
        }
        Assert.True(after.Sum() > before.Sum());
        Assert.False(t.ApplyNutrients(new Vec2(40, 0), 0.5).Ok);
    }

    [Fact] // t-130, t-131, t-132
    public void PokeDisturbsFaunaAndOnlyWobblesFlora()
    {
        var (w, t) = Setup();
        var st = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), new Vec2(2, 0));
        var fl = w.FloraSystem.Establish(w.Content.FloraOrThrow("carpet_moss"), new Vec2(2.1, 0.1), "t", 0.5);
        double biomass = fl.Biomass;
        var hit = Selection.Raycast(w, new Vec3(2, 2, 0), new Vec3(0, -1, 0));
        var res = t.Poke(new PokeAction(hit.Point, new Vec3(0, -1, 0), 1.0, hit));
        Assert.Contains(st.Id, res.FaunaDisturbed);
        Assert.Contains(fl.Id, res.FloraWobble);
        Assert.True(st.DisturbedUntil > w.Clock.SimSeconds);
        Assert.Equal(biomass, fl.Biomass);
        Assert.NotNull(w.Flora.Get(fl.Id));
        Assert.Empty(w.CheckInvariants());
        var terrain = t.Poke(new PokeAction(new Vec3(-3.5, 0.5, 2.5), new Vec3(0, -1, 0), 1, WorldHit.None)); // generic: works on bare terrain
        Assert.Empty(terrain.FaunaDisturbed);
    }

    [Fact] // t-133, t-134, t-135
    public void PlacedPropsImmediatelyAffectQueriesSelectionAndSaves()
    {
        var (w, t) = Setup();
        var rock = t.PlaceRock(new Vec2(2.5, -1.5), 0.35, 0.2, 77);
        Assert.True(rock.Ok, rock.Message);
        Assert.Equal(Substrate.Rock, w.SubstrateAt(new Vec2(2.5, -1.5)));
        Assert.Equal(HitKind.Rock, Selection.Raycast(w, new Vec3(2.5, 3, -1.5), new Vec3(0, -1, 0)).Kind);
        var log = t.PlaceLog(new Vec2(1.0, 2.5), 0.4, 1.4, 0.15, 2, 78);
        Assert.True(log.Ok, log.Message);
        Assert.Contains("wood_soft", w.Props.HabitatTagsAt(new Vec2(1.0, 2.5)));
        var gravel = t.PlaceGravel(new Vec2(3.2, 1.8), 0.5, 79);
        Assert.True(gravel.Ok, gravel.Message);
        Assert.Equal(Substrate.Gravel, w.SubstrateAt(new Vec2(3.2, 1.8)));
        Assert.NotNull(t.PreviewRock(new Vec2(w.Domain.Radius, 0), 0.5));
        Assert.Null(t.PreviewRock(new Vec2(3.5, -0.5), 0.2));
        var dir = TestUtil.TempDir();
        var path = Path.Combine(dir, "props.vivsave");
        Assert.True(Persistence.SaveSystem.Save(w, path).Ok);
        var loaded = Persistence.SaveSystem.Load(w.Content, path);
        Assert.True(loaded.Ok, loaded.Message);
        Assert.Equal(Substrate.Gravel, loaded.World!.SubstrateAt(new Vec2(3.2, 1.8)));
        Assert.NotNull(loaded.World.Props.Find(log.Affected[0]));
    }

    [Fact] // t-136, t-137
    public void IntroductionToolsValidateHabitat()
    {
        var (w, t) = Setup();
        Assert.False(t.IntroduceFlora("crust_lichen", new Vec2(2, 1)).Ok);        // soil: hard refusal
        Assert.NotNull(t.PreviewFlora("crust_lichen", new Vec2(2, 1)));
        var ok = t.IntroduceFlora("carpet_moss", new Vec2(2, 1));
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal("carpet_moss", w.Flora.Get(ok.Affected[0])!.SpeciesId);
        Assert.False(t.IntroduceFauna("microminnow", FaunaFixtures.Land).Ok);
        var fish = t.IntroduceFauna("microminnow", FaunaFixtures.Pond);
        Assert.True(fish.Ok, fish.Message);
        Assert.All(fish.Affected, id => Assert.True(w.Genomes.Contains(w.Fauna.Get(id)!.GenomeId)));
        Assert.False(t.IntroduceFauna("springtail", FaunaFixtures.Pond).Ok);
    }
}
