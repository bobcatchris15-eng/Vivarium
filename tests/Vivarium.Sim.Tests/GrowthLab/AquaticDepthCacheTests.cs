using Vivarium.Sim.Core;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

public class AquaticDepthCacheTests
{
    [Fact]
    public void RevisitingDifferentCoarseCellsReusesExactSamplesAndInvalidatesEachEdit()
    {
        var world = TestUtil.FlatWorld();
        int a = world.Grid.CellAt(new Vec2(.19, .19));
        int b = world.Grid.CellAt(new Vec2(.59, .19));
        Assert.NotEqual(a, b);
        world.Water.Depth[a] = .1; world.Water.Depth[b] = .2;
        var env = world.AquaticSystem;
        long misses = env.DepthCacheMisses;
        Assert.Equal(.1, env.DepthAt(9, 9));
        Assert.Equal(.2, env.DepthAt(29, 9));
        Assert.Equal(.1, env.DepthAt(9, 9));
        Assert.Equal(misses + 2, env.DepthCacheMisses);
        world.Water.Depth[b] = .3;
        Assert.Equal(.3, env.DepthAt(29, 9));
        Assert.Equal(.1, env.DepthAt(9, 9));
        Assert.Equal(misses + 3, env.DepthCacheMisses);
    }

    [Fact]
    public void FineQueriesMatchAuthoritativeDepthAcrossWaterAndBedEdits()
    {
        var world = TestUtil.FlatWorld();
        int cell = world.Grid.CellAt(new Vec2(.19, .19));
        void CheckRegion()
        {
            for (int z = 0; z < 10; z++)
            for (int x = 0; x < 10; x++)
            {
                var p = new Vec2((x + .5) * .02, (z + .5) * .02);
                int c = world.Grid.CellAt(p);
                double expected = world.Water.IsWet(c) ? world.Water.OpenWaterDepth(c) : 0;
                Assert.Equal(expected, world.AquaticSystem.DepthAt(x, z));
                Assert.Equal(expected <= 0, world.AquaticSystem.IsObstacle(x, z));
            }
        }
        CheckRegion();
        world.Water.Depth[cell] = .1;
        CheckRegion();
        Assert.Equal(.1, world.AquaticSystem.DepthAt(9, 9));
        world.Water.Depth[cell] = .2;
        Assert.Equal(.2, world.AquaticSystem.DepthAt(9, 9));
        world.Water.Depth[cell] = 0;
        world.Water.Config.WaterTable = world.Water.Bed[cell] + .3;
        CheckRegion();
        world.Water.Bed[cell] += .1;
        CheckRegion();
        Assert.True(world.AquaticSystem.DepthCacheHits > 100);
    }
}
