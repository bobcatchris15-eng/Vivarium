using System.Linq;
using Vivarium.Sim.Persistence;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

[Trait("Suite", "GrowthLab")]
public class AquaticBatchingTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.001)]
    public void ResumingAquaticStagesPreservesExactWorldState(double flow)
    {
        var synchronous = TestUtil.DefaultWorld(seed: 8181, populate: false);
        var resumed = TestUtil.DefaultWorld(seed: 8181, populate: false);
        foreach (var world in new[] { synchronous, resumed })
            foreach (int cell in world.Grid.DomainCells)
            {
                world.Water.FlowX[cell] = flow;
                world.Water.FlowZ[cell] = -flow * .25;
            }
        Assert.Contains(synchronous.Coverage.AlgaeBed.Tiles, t => t.B.Any(b => b > 0));
        Assert.Equal(WorldSerializer.Digest(synchronous), WorldSerializer.Digest(resumed));

        // Multiple steps also exercise the persistent recruitment ordinal, not only seeded growth.
        for (int step = 0; step < 3; step++)
        {
            synchronous.AquaticSystem.Step(physicalSeconds: .1, bioSeconds: 600);
            int boundaries = 0;
            using var batches = resumed.AquaticSystem.StepBatches(.1, 600).GetEnumerator();
            string before = WorldSerializer.Digest(resumed);
            Assert.True(batches.MoveNext()); // Collecting wet bounds is read-only.
            Assert.Equal(before, WorldSerializer.Digest(resumed));
            boundaries++;
            while (batches.MoveNext()) boundaries++;
            Assert.True(boundaries >= 6, "Aquatic work must expose stage boundaries to the scheduler.");
            Assert.Equal(WorldSerializer.Digest(synchronous), WorldSerializer.Digest(resumed));
        }
    }
}
