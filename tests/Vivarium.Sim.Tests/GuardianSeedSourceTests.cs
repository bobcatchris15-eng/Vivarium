using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class GuardianSeedSourceTests
{
    private static (VivariumWorld w, int cell) Fixture()
    {
        var w = TestUtil.FlatWorld(edit: d => { d.Diameter = 5; d.CellSize = .5; });
        var g = w.GuardianSystem.InitializeGuardian(GuardianCatalog.Woodland)!;
        int cell = w.Grid.NearestDomainCell(Vec2.Zero);
        var c = w.Grid.CellCenter(cell);
        g.Position = new Vec3(c.X, w.GroundHeight(c), c.Z);
        g.TargetPosition = g.Position;
        g.Activity = GuardianActivity.Feed; g.ActivityDuration = 10;
        g.Energy = w.GuardianSystem.Def!.EnergyMax * .4;
        foreach (int i in w.Grid.DomainCells) { w.Litter.FineMass[i] = w.Litter.FruitMass[i] = w.Fields.Detritus[i] = 0; }
        return (w, cell);
    }

    private static void Feed(VivariumWorld w) =>
        w.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * .1);

    [Fact]
    public void EatingNonFruitDetritusQueuesNoSeeds()
    {
        var (w, cell) = Fixture();
        w.Litter.FineMass[cell] = 5;
        double e = w.Guardian!.Energy;
        Feed(w);
        Assert.True(w.Guardian.Energy > e);
        Assert.Empty(w.Guardian.PendingSeeds);
    }

    [Fact]
    public void EatingFruitQueuesSeedOfNearestFruitingSpecies()
    {
        var (w, cell) = Fixture();
        var c = w.Grid.CellCenter(cell);
        var sp = w.Content.Flora.Take(2).ToArray();
        string far = sp[0].Id, near = sp[^1].Id;
        w.Flora.Add(new FloraIndividual { Id = w.Ids.Next(EntityKind.Flora), SpeciesId = far, X = c.X + 3, Z = c.Z, Biomass = 1, Health = 1, Fruiting = true });
        w.Flora.Add(new FloraIndividual { Id = w.Ids.Next(EntityKind.Flora), SpeciesId = near, X = c.X + 1, Z = c.Z, Biomass = 1, Health = 1, Fruiting = true });
        w.Litter.FruitMass[cell] = 5;
        Feed(w);
        var seed = Assert.Single(w.Guardian!.PendingSeeds);
        Assert.Equal(near, seed.SpeciesId);
    }

    [Fact]
    public void FruitWithoutFruitingSourceQueuesNoSeeds()
    {
        var (w, cell) = Fixture();
        w.Litter.FruitMass[cell] = 5;
        Feed(w);
        Assert.Empty(w.Guardian!.PendingSeeds);
    }
}
