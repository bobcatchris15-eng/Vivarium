using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class GuardianSchedulerFeedingTests
{
    public static IEnumerable<object[]> GuardiansAndClocks =>
        from def in GuardianCatalog.All
        from bio in new[] { 1.0, WorldDescriptor.DefaultBioAcceleration, WorldDescriptor.MaxBioAcceleration }
        select new object[] { def.Id, bio };

    private static VivariumWorld Fixture(string id, double bio)
    {
        var w = TestUtil.FlatWorld(edit: d => { d.Diameter = 5; d.CellSize = .5; d.BioAcceleration = bio; });
        var g = w.GuardianSystem.InitializeGuardian(id)!;
        var center = w.Grid.CellCenter(w.Grid.NearestDomainCell(Vec2.Zero));
        g.Position = new Vec3(center.X, w.GroundHeight(center), center.Z);
        g.TargetPosition = g.Position;
        g.Activity = GuardianActivity.Feed; g.ActivityDuration = 10;
        g.Energy = w.GuardianSystem.Def!.EnergyMax * .4;
        return w;
    }

    [Theory]
    [MemberData(nameof(GuardiansAndClocks))]
    public void ScheduledFeedingRecoversHungerAndReleasesOtherActivities(string id, double bio)
    {
        var w = Fixture(id, bio); var g = w.Guardian!;
        int cell = w.Grid.NearestDomainCell(g.PositionXZ);
        w.Litter.FruitMass[cell] = 10;
        double initial = g.Energy, maximum = initial;
        bool leftHungerLoop = false;
        for (int tick = 0; tick < 1512; tick++) // 90 visible seconds, actual registered scheduler.
        {
            w.Scheduler.StepTick();
            Assert.InRange(g.Energy, 0, w.GuardianSystem.Def!.EnergyMax);
            maximum = Math.Max(maximum, g.Energy);
            leftHungerLoop |= g.Energy >= w.GuardianSystem.Def.EnergyMax * .65 &&
                g.Activity is not GuardianActivity.Feed and not GuardianActivity.Forage;
        }
        Assert.True(maximum > initial + 5, $"{id} bio={bio}: feeding never restored energy, max={maximum}");
        Assert.True(leftHungerLoop, $"{id} bio={bio}: never left hunger-driven activity");
        Assert.True(w.Scheduler.Systems.Single(s => s.Name == "guardian.metabolism").Runs > 0);
        Assert.True(w.Scheduler.Systems.Single(s => s.Name == "guardian.behaviour").Runs > 0);
    }

    [Theory]
    [MemberData(nameof(GuardiansAndClocks))]
    public void SchedulerCannotRestoreEnergyWithoutFood(string id, double bio)
    {
        var w = Fixture(id, bio); var g = w.Guardian!;
        foreach (int cell in w.Grid.DomainCells)
        {
            w.Litter.FineMass[cell] = w.Litter.FruitMass[cell] = 0;
            w.Fields.Detritus[cell] = 0;
        }
        double previous = g.Energy;
        for (int tick = 0; tick < 168; tick++)
        {
            w.Scheduler.StepTick();
            Assert.True(g.Energy <= previous + 1e-10);
            previous = g.Energy;
        }
        Assert.True(g.Energy < w.GuardianSystem.Def!.EnergyMax * .4);
    }

    [Fact]
    public void MaintenanceKeepsBiologicalAndGameSpeedScaling()
    {
        double Loss(double bio, int speed)
        {
            var w = Fixture(GuardianCatalog.Woodland, bio); var g = w.Guardian!;
            foreach (int cell in w.Grid.DomainCells)
            { w.Litter.FineMass[cell] = w.Litter.FruitMass[cell] = w.Fields.Detritus[cell] = 0; }
            g.Activity = GuardianActivity.Rest; g.ActivityDuration = 100;
            w.Clock.SetSpeedIndex(speed); w.Scheduler.WallBudgetMs = 0;
            double initial = g.Energy;
            for (int step = 0; step < 100; step++) w.Scheduler.Advance(.1);
            return initial - g.Energy;
        }
        double baseline = Loss(1, SimClock.DefaultSpeedIndex);
        Assert.True(baseline > 0);
        Assert.Equal(WorldDescriptor.DefaultBioAcceleration, Loss(WorldDescriptor.DefaultBioAcceleration, SimClock.DefaultSpeedIndex) / baseline, 6);
        Assert.Equal(WorldDescriptor.MaxBioAcceleration, Loss(WorldDescriptor.MaxBioAcceleration, SimClock.DefaultSpeedIndex) / baseline, 6);
        Assert.Equal(2, Loss(1, SimClock.DefaultSpeedIndex + 1) / baseline, 6);
    }

    [Fact]
    public void AStarvedSavedGuardianCanRecoverAndResumeOtherActivity()
    {
        var w = Fixture(GuardianCatalog.Woodland, WorldDescriptor.DefaultBioAcceleration);
        var g = w.Guardian!; g.Energy = 0;
        w.Litter.FruitMass[w.Grid.NearestDomainCell(g.PositionXZ)] = 10;
        bool recovered = false;
        for (int tick = 0; tick < 2016; tick++)
        {
            w.Scheduler.StepTick();
            recovered |= g.Energy >= w.GuardianSystem.Def!.EnergyMax * .65 &&
                g.Activity is not GuardianActivity.Feed and not GuardianActivity.Forage;
        }
        Assert.True(recovered);
    }

    [Fact]
    public void AFullGuardianDoesNotWasteFoodOrCreateUnbackedEnergy()
    {
        var w = Fixture(GuardianCatalog.Woodland, 1); var g = w.Guardian!;
        int cell = w.Grid.NearestDomainCell(g.PositionXZ);
        w.Litter.FineMass[cell] = w.Fields.Detritus[cell] = 0;
        w.Litter.FruitMass[cell] = 1;
        g.Energy = w.GuardianSystem.Def!.EnergyMax;
        w.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * .1);
        Assert.Equal(1, w.Litter.FruitMass[cell]);
        g.Energy -= .01;
        w.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * .1);
        Assert.Equal(w.GuardianSystem.Def.EnergyMax, g.Energy, 8);
        Assert.Equal(.01 / 25, 1 - w.Litter.FruitMass[cell], 8);
    }
}
