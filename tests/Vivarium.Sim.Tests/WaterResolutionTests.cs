using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;
public class WaterResolutionTests
{
    [Fact]
    public void RefinementConservesVolumeAndRoundTrips()
    {
        var f = new SurfaceFluidDynamics(new HexDomain(3), p => .1*p.X, .025);
        f.AddWater(new Vec2(0,0), .08, .0004);
        var before=f.Export(); double volume=f.Volume;
        f.RefineResolution();
        Assert.Equal(.0125,f.Spacing);
        Assert.InRange(Math.Abs(f.Volume-volume),0,1e-12);
        Assert.Equal(before.Time,f.Time);
        Assert.Equal(before.Remainder,f.Remainder);
        var saved=f.Export();
        var restored=new SurfaceFluidDynamics(new HexDomain(3),p=>.1*p.X);
        restored.Restore(saved);
        Assert.Equal(saved.Cells,restored.Export().Cells);
        restored.RefineResolution();
        Assert.Equal(saved.Cells,restored.Export().Cells);
    }
}
