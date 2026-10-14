using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;

public class SpringWaterMeshTests
{
    [Fact]
    public void GroundSnapshotRetainsItsOwnHeightsAfterSourceChanges()
    {
        double[] heights = { 0, 1, 2, 3 };
        var ground = new SpringFluidGroundSnapshot(heights, 2, 0, 1);
        Array.Fill(heights, 100);
        Assert.Equal(1.5, ground.Height(new Vec2(.5, .5)), 12);
        Assert.Equal(0, ground.Height(new Vec2(-10, -10)));
    }

    [Fact]
    public void DistantStreamsDoNotAllocateOrFillTheirDryBoundingRectangle()
    {
        var ground = new SpringFluidGroundSnapshot(new double[4], 2, -6, 12);
        var particles = new[] {
            new FluidParticle(new Vec3(-4, .02, -3), new Vec3(1, 0, 0), .000001, 1),
            new FluidParticle(new Vec3(4, .02, 3), new Vec3(1, 0, 0), .000001, 2)
        };
        var result = SpringWaterMesh.Build(particles, ground, new HexDomain(20));
        Assert.True(result.Mesh.TriangleCount > 0);
        Assert.InRange(result.WetNodes, 2, 200);
        for (int i = 0; i < result.Mesh.Positions.Count; i += 3)
            Assert.True(Math.Abs(result.Mesh.Positions[i]) > 3.9, "Dry space between streams must stay empty.");
    }
}
