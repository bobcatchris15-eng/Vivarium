using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
namespace Vivarium.Sim.Tests;

public class WaterShorelineTests
{
    [Fact]
    public void IsolatedWetCellHasAnInterpolatedShorelineAndTapersIntoTheBed()
    {
        var w=TestUtil.FlatWorld();
        int c=w.Grid.CellAt(Vec2.Zero);
        w.Water.Depth[c]=0.025;
        var before=w.Water.DigestHex();
        var mesh=WaterMesh.Build(w);
        Assert.True(mesh.TriangleCount>0);
        Assert.Equal(before,w.Water.DigestHex());
        double cs=w.Grid.CellSize;
        var edge=Enumerable.Range(0,mesh.VertexCount).Where(i=>mesh.Colors[i*4+3]<0.002).ToArray();
        Assert.NotEmpty(edge);
        Assert.Contains(edge,i=> {
            var p=mesh.Position(i);
            double x=(p.X-w.Grid.OriginX)/cs,z=(p.Z-w.Grid.OriginZ)/cs;
            return Math.Abs(x-Math.Round(x))>0.02 && Math.Abs(z-Math.Round(z))>0.02;
        });
        Assert.All(edge,i=>Assert.InRange(mesh.Position(i).Y-w.GroundHeight(mesh.Position(i).XZ),0,0.0003));
        Assert.All(Enumerable.Range(0,mesh.VertexCount),i=>Assert.InRange(mesh.Position(i).Y-w.GroundHeight(mesh.Position(i).XZ),-1e-6,0.025001));
    }
}
