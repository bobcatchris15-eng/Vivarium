using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class DeadFloraTests
{
    [Fact]
    public void VascularPlantDeathPersistsBeforeAssimilation()
    {
        var w = TestUtil.DefaultWorld();
        var f = w.Flora.Items.First(x => x.SpeciesId == "prismstar");
        var id = f.Id;
        double biomass = f.Biomass;

        Assert.True(w.FloraSystem.Kill(f, "test"));
        Assert.Null(w.Flora.Get(id));
        var dead = Assert.IsType<DeadPlant>(w.DeadFlora.Get(id));
        Assert.Equal(biomass, dead.RemainingBiomass, 10);
        Assert.Equal(DeadPlantStage.StandingDead, dead.Stage);
        Assert.Equal(1, w.Tally.DeadFloraCreated);

        double beforeLitter = w.Litter.ExportFine().Sum() + w.Litter.ExportCoarse().Sum();
        w.DeadFloraSystem.Step(15 * SimUnits.Day);
        dead = Assert.IsType<DeadPlant>(w.DeadFlora.Get(id));
        Assert.True(dead.Stage >= DeadPlantStage.Collapsing);
        Assert.True(dead.RemainingBiomass < biomass);
        Assert.True(w.Litter.ExportFine().Sum() + w.Litter.ExportCoarse().Sum() > beforeLitter);
    }

    [Fact]
    public void DeadFloraRoundTripsWithEntityIdentity()
    {
        var w = TestUtil.DefaultWorld();
        var f = w.Flora.Items.First(x => x.SpeciesId == "frosttussock");
        var id = f.Id;
        w.FloraSystem.Kill(f, "test");
        w.DeadFloraSystem.Step(12 * SimUnits.Day);

        var restored = WorldSerializer.Deserialize(w.Content, WorldSerializer.Serialize(w));
        var a = Assert.IsType<DeadPlant>(w.DeadFlora.Get(id));
        var b = Assert.IsType<DeadPlant>(restored.DeadFlora.Get(id));
        Assert.Equal(a.SpeciesId, b.SpeciesId);
        Assert.Equal(a.Stage, b.Stage);
        Assert.Equal(a.StageProgress, b.StageProgress);
        Assert.Equal(a.RemainingBiomass, b.RemainingBiomass);
        Assert.Equal(a.CollapseHeading, b.CollapseHeading);
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
    }

    [Fact]
    public void CorpseEventuallyAssimilatesIntoSurfaceCycle()
    {
        var w = TestUtil.DefaultWorld();
        var f = w.Flora.Items.First(x => x.SpeciesId == "prismstar");
        var id = f.Id;
        w.FloraSystem.Kill(f, "test");

        w.DeadFloraSystem.Step(80 * SimUnits.Day);

        Assert.Null(w.DeadFlora.Get(id));
        Assert.Equal(1, w.Tally.DeadFloraAssimilated);
        Assert.True(w.Tally.CorpseToLitter > 0 || w.Tally.CorpseToNutrients > 0);
    }

    [Fact]
    public void NearbyFungusAcceleratesFineLitterBreakdown()
    {
        var control = TestUtil.FlatWorld();
        var fungus = TestUtil.FlatWorld();
        var p = new Vec2(0, 0);
        foreach (int c in control.Grid.DomainCells) control.Fields.Detritus[c] = 0;
        control.Litter.Restore(new double[control.Grid.DomainCells.Length], new double[control.Grid.DomainCells.Length], new double[control.Grid.DomainCells.Length]);
        foreach (int c in fungus.Grid.DomainCells) fungus.Fields.Detritus[c] = 0;
        fungus.Litter.Restore(new double[fungus.Grid.DomainCells.Length], new double[fungus.Grid.DomainCells.Length], new double[fungus.Grid.DomainCells.Length]);
        control.Litter.Deposit(p, 1);
        fungus.Litter.Deposit(p, 1);

        var sp = fungus.Content.FloraOrThrow("dewbonnet");
        var f = fungus.FloraSystem.Establish(sp, p, "test", sp.MaxBiomass);
        f.Health = 1;

        control.Litter.Step(SimUnits.Day);
        fungus.Litter.Step(SimUnits.Day);

        int cc = control.Grid.NearestDomainCell(p);
        int fc = fungus.Grid.NearestDomainCell(p);
        Assert.True(fungus.Litter.FineMass[fc] < control.Litter.FineMass[cc]);
    }

    [Fact]
    public void PostLifeProfilesDriveSpeciesSpecificPersistence()
    {
        var frost = TestUtil.FlatWorld();
        var fern = TestUtil.FlatWorld();
        var frostSp = frost.Content.FloraOrThrow("frosttussock");
        var fernSp = fern.Content.FloraOrThrow("veilfern");
        Assert.NotNull(frostSp.PostLife);
        Assert.NotNull(fernSp.PostLife);
        Assert.True(frostSp.PostLife!.StandingTime > fernSp.PostLife!.StandingTime);

        var a = frost.FloraSystem.Establish(frostSp, new Vec2(0, 0), "test", frostSp.MaxBiomass);
        var b = fern.FloraSystem.Establish(fernSp, new Vec2(0, 0), "test", fernSp.MaxBiomass);
        frost.FloraSystem.Kill(a, "test");
        fern.FloraSystem.Kill(b, "test");

        frost.DeadFloraSystem.Step(10 * SimUnits.Day);
        fern.DeadFloraSystem.Step(10 * SimUnits.Day);

        Assert.Equal(DeadPlantStage.StandingDead, Assert.Single(frost.DeadFlora.Items).Stage);
        Assert.True(Assert.Single(fern.DeadFlora.Items).Stage >= DeadPlantStage.Fallen);
    }

    [Fact]
    public void WoodyPostLifeProfileRoutesMostCorpseMassToCoarseLitter()
    {
        var w = TestUtil.FlatWorld();
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0;
        w.Litter.Restore(new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length]);
        var sp = w.Content.FloraOrThrow("ironlace");
        Assert.NotNull(sp.PostLife);
        Assert.True(sp.PostLife!.CoarseFraction > 0.75);
        var f = w.FloraSystem.Establish(sp, new Vec2(0, 0), "test", sp.MaxBiomass);
        w.FloraSystem.Kill(f, "test");

        w.DeadFloraSystem.Step(30 * SimUnits.Day);

        Assert.True(w.Litter.ExportCoarse().Sum() > w.Litter.ExportFine().Sum());
    }

    [Fact]
    public void DecomposerProfilesExpressDifferentMaterialSpecialties()
    {
        var w = TestUtil.FlatWorld();
        var dew = w.Content.FloraOrThrow("dewbonnet").Decomposition;
        var ember = w.Content.FloraOrThrow("emberfan_fungus").Decomposition;
        var amber = w.Content.FloraOrThrow("ambervein").Decomposition;

        Assert.NotNull(dew);
        Assert.NotNull(ember);
        Assert.NotNull(amber);
        Assert.True(dew!.FineMultiplier > dew.CoarseMultiplier);
        Assert.True(ember!.CoarseMultiplier > ember.FineMultiplier);
        Assert.True(dew.FruitMultiplier > 1);
        Assert.True(amber!.FineMultiplier > 1);
    }

    [Fact]
    public void EmberfanAcceleratesCoarseLitterMoreThanDewbonnet()
    {
        var dewWorld = TestUtil.FlatWorld();
        var emberWorld = TestUtil.FlatWorld();
        var p = new Vec2(0, 0);
        dewWorld.Litter.Deposit(p, 0, 1);
        emberWorld.Litter.Deposit(p, 0, 1);

        var dewSp = dewWorld.Content.FloraOrThrow("dewbonnet");
        var emberSp = emberWorld.Content.FloraOrThrow("emberfan_fungus");
        var dew = dewWorld.FloraSystem.Establish(dewSp, p, "test", dewSp.MaxBiomass);
        var ember = emberWorld.FloraSystem.Establish(emberSp, p, "test", emberSp.MaxBiomass);
        dew.Health = 1;
        ember.Health = 1;

        dewWorld.Litter.Step(5 * SimUnits.Day);
        emberWorld.Litter.Step(5 * SimUnits.Day);

        int dc = dewWorld.Grid.NearestDomainCell(p);
        int ec = emberWorld.Grid.NearestDomainCell(p);
        Assert.True(emberWorld.Litter.CoarseMass[ec] < dewWorld.Litter.CoarseMass[dc]);
    }
}
