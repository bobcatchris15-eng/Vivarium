using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class WaterMeshRivuletTests
{
    // Tilted plane (falls toward +x) with a V-channel along z = 0; a spring at the upstream end
    // feeds a thin film down the channel.
    private static (VivariumWorld w, WaterMeshSnapshot snap, int[] path) ChannelWorld(double q)
    {
        var w = TestUtil.FlatWorld();
        var snap = WaterMesh.Capture(w);
        var hf = snap.Terrain;
        double baseH = hf.Height(new Vec2(0, 0));
        for (int j = 0; j < hf.Nz; j++)
            for (int i = 0; i < hf.Nx; i++)
            {
                var p = hf.VertexPos(i, j);
                hf.H[hf.VertexIndex(i, j)] = baseH + 0.2 - 0.05 * p.X + 0.08 * Math.Abs(p.Z);
            }
        var g = w.Grid;
        int jc = (int)Math.Floor((0 - g.OriginZ) / g.CellSize);
        int ic = (int)Math.Floor((0 - g.OriginX) / g.CellSize);
        var path = new List<int>();
        for (int i = ic - 3; i <= ic + 3; i++)
        {
            Assert.True(g.InDomain(i, jc));
            path.Add(g.Index(i, jc));
        }
        Array.Clear(snap.Depth);
        Array.Clear(snap.FlowX);
        Array.Clear(snap.FlowZ);
        foreach (int c in path)
        {
            snap.Depth[c] = q > 0 ? 0.0004 : 0.0;
            snap.FlowX[c] = q;
        }
        return (w, snap, path.ToArray());
    }

    [Fact]
    public void ThinFluidUsesTheSameSurfaceAsPools()
    {
        var (w,snap,path)=ChannelWorld(3e-6);
        foreach(int c in w.Grid.DomainCells) snap.Bed[c]=snap.Terrain.Height(w.Grid.CellCenter(c));
        var set=WaterMesh.BuildSet(w,snap);
        Assert.True(set.SurfaceMesh.TriangleCount>0);
        Assert.Equal(set.SurfaceMesh.DigestHex(),WaterMesh.Build(w,snap).DigestHex());
    }
    [Fact]
    public void DischargeDoesNotInventExtraWidthOrVolume()
    {
        var (w,snap,_)=ChannelWorld(3e-6);
        foreach(int c in w.Grid.DomainCells) snap.Bed[c]=snap.Terrain.Height(w.Grid.CellCenter(c));
        var small=WaterMesh.Build(w,snap);
        for(int i=0;i<snap.FlowX.Length;i++) snap.FlowX[i]*=100;
        var fast=WaterMesh.Build(w,snap);
        Assert.Equal(small.Positions,fast.Positions);
        Assert.Equal(small.Indices,fast.Indices);
        Assert.NotEqual(small.UV2,fast.UV2);
    }
    [Fact]
    public void DischargeWithoutDepthNeverDrawsWater()
    {
        var(w,snap,_)=ChannelWorld(0);
        Array.Fill(snap.FlowX,.1);
        Assert.Equal(0,WaterMesh.Build(w,snap).TriangleCount);
    }
}
