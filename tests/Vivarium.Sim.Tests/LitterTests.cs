using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class LitterTests
{
    [Fact]
    public void DepositSpreadsWithoutCreatingOrLosingMass()
    {
        var w = TestUtil.FlatWorld();
        Assert.All(w.Litter.ExportFine(), mass => Assert.Equal(0, mass));
        var p = new Vec2(0, 0);
        Assert.Equal(0.7, w.Litter.Deposit(p, 0.5, 0.2, 0.8), 10);
        Assert.Equal(0.5, w.Litter.ExportFine().Sum(), 10);
        Assert.Equal(0.2, w.Litter.ExportCoarse().Sum(), 10);
        Assert.True(w.Litter.FineMass.Count(mass => mass > 0) > 1);
        Assert.Equal(0.7, w.Tally.LitterDeposited, 10);
    }

    [Fact]
    public void BreakdownTransfersOnlyWhatDetritusCanAccept()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0, 0);
        int cell = w.Grid.NearestDomainCell(p);
        w.Fields.Detritus[cell] = 0;
        w.Litter.Deposit(p, 1, 0.5);
        w.Litter.Step(SimUnits.Day);
        double remaining = w.Litter.FineMass[cell] + w.Litter.CoarseMass[cell];
        Assert.True(remaining < 1.5);
        Assert.Equal(1.5, remaining + w.Fields.Detritus[cell], 10);
        Assert.Equal(w.Fields.Detritus[cell], w.Tally.LitterToDetritus, 10);

        w.Fields.Detritus[cell] = w.Fields.Detritus.Max;
        double before = w.Litter.FineMass[cell] + w.Litter.CoarseMass[cell];
        w.Litter.Step(SimUnits.Day);
        Assert.Equal(before, w.Litter.FineMass[cell] + w.Litter.CoarseMass[cell], 10);
    }

    [Fact]
    public void FineAndCoarseLitterRoundTripExactly()
    {
        var w = TestUtil.FlatWorld();
        w.Litter.Deposit(new Vec2(0, 0), 0.34, 0.12, 0.7);
        var restored = WorldSerializer.Deserialize(w.Content, WorldSerializer.Serialize(w));
        Assert.Equal(w.Litter.ExportFine(), restored.Litter.ExportFine());
        Assert.Equal(w.Litter.ExportCoarse(), restored.Litter.ExportCoarse());
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
    }

    [Fact]
    public void LivingFloraSheddingFeedsVisibleReservoir()
    {
        var w = TestUtil.DefaultWorld();
        double initial = w.Litter.ExportFine().Sum();
        Assert.True(initial > 0); // established woody starters carry a short litter history
        double depositedAtStart = w.Tally.LitterDeposited;
        w.Step(100);
        Assert.True(w.Tally.LitterDeposited > depositedAtStart);
    }
}
