using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Fields")]
public class SculptLightTests
{
    [Fact]
    public void SculptedRidgeRecomputesLight()
    {
        var w = TestUtil.FlatWorld();
        w.RefreshDerived();
        var centre = w.Grid.CellCenter(w.Grid.DomainCells[w.Grid.DomainCells.Length / 2]);
        var beside = centre + new Vec2(1.0, 0);
        int cell = w.Grid.NearestDomainCell(beside);
        double before = w.Fields.Light[cell];
        for (int n = 0; n < 12; n++) TerrainEditing.Sculpt(w, centre, 0.6, 0.5, SculptMode.Raise);
        Assert.True(w.Fields.LightStale(w.Props));
        w.RefreshDerived();
        Assert.False(w.Fields.LightStale(w.Props));
        Assert.True(w.Fields.Light[cell] < before, $"before {before} after {w.Fields.Light[cell]}");
    }
}
