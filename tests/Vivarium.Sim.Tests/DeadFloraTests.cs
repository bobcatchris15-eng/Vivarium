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
        control.Fields.Detritus[control.Grid.NearestDomainCell(p)] = 0;
        fungus.Fields.Detritus[fungus.Grid.NearestDomainCell(p)] = 0;
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
}
