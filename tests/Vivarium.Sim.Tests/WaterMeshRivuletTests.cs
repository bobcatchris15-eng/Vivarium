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

    private static List<Vec3> SheetVertices(MeshData m)
    {
        var list = new List<Vec3>();
        for (int v = 0; v < m.VertexCount; v++)
            if (m.Colors[v * 4] < 0.95f) list.Add(m.Position(v));
        return list;
    }

    [Fact]
    public void SpringFilmBelowWetDepthYieldsConnectedRivulet()
    {
        var (w, snap, path) = ChannelWorld(3e-6);
        var m = WaterMesh.BuildRivulets(w, snap);
        Assert.True(m.TriangleCount > 0);
        var sheet = SheetVertices(m);
        var g = w.Grid;
        // continuous along the channel: every cell's x-span contains ribbon vertices
        foreach (int c in path)
        {
            double x0 = g.OriginX + (c % g.Nx) * g.CellSize;
            Assert.Contains(sheet, p => p.X >= x0 && p.X <= x0 + g.CellSize);
        }
        // follows the thalweg (z ~ 0) and sits above the terrain
        Assert.All(sheet, p => Assert.True(Math.Abs(p.Z) < 0.1, $"z {p.Z}"));
        Assert.All(sheet, p => Assert.True(p.Y > snap.Terrain.Height(new Vec2(p.X, p.Z)) + 0.0009));
        // UV2 carries downstream velocity
        Assert.True(m.UV2[0] > 0);
        // deterministic
        var m2 = WaterMesh.BuildRivulets(w, snap);
        Assert.Equal(m.Positions, m2.Positions);
    }

    [Fact]
    public void RivuletWidthGrowsWithDischarge()
    {
        double Width(double q)
        {
            var (w, snap, _) = ChannelWorld(q);
            var sheet = SheetVertices(WaterMesh.BuildRivulets(w, snap));
            return sheet.Max(p => p.Z) - sheet.Min(p => p.Z);
        }
        double small = Width(1e-6), large = Width(1e-4);
        Assert.True(large > small * 2, $"{small} vs {large}");
        Assert.True(WaterMesh.RivuletWidth(1e-6, 0.25) < WaterMesh.RivuletWidth(1e-5, 0.25));
        Assert.Equal(0.25, WaterMesh.RivuletWidth(1.0, 0.25));
    }

    [Fact]
    public void NoFlowNoRivulet()
    {
        var (w, snap, _) = ChannelWorld(0);
        Assert.Equal(0, WaterMesh.BuildRivulets(w, snap).TriangleCount);
        Assert.Equal(0, WaterMesh.BuildSet(w, snap).RivuletMesh!.TriangleCount);
    }
}
