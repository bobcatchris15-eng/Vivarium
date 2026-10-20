using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class WaterTableShorelineTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void SmallWetDepressionBetweenCoarseProbesRemainsVisible(double diameter)
    {
        var w = TestUtil.FlatWorld(edit: d => { d.Diameter = diameter; d.Water.WaterTable = .2; });
        var hf = w.Terrain;
        hf.BeginEdit();
        Array.Fill(hf.H, .3);
        // An interior terrain vertex away from both coarse corners and cell centers.
        var candidates = from z in Enumerable.Range(2, hf.Nz - 4)
                         from x in Enumerable.Range(2, hf.Nx - 4)
                         let p = hf.VertexPos(x, z)
                         let gx = (p.X - w.Grid.OriginX) / w.Grid.CellSize
                         let gz = (p.Z - w.Grid.OriginZ) / w.Grid.CellSize
                         let fx = gx - Math.Floor(gx)
                         let fz = gz - Math.Floor(gz)
                         where p.Length < diameter * .2 && Math.Abs(fx - fz) > .2
                         select (x, z, p);
        var low = candidates.First();
        hf.H[hf.VertexIndex(low.x, low.z)] = .1;
        hf.Touch(); w.Water.RefreshBed(hf);
        var mesh = WaterMesh.BuildWaterTable(w, hf);
        double area = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Position(mesh.Indices[i]);
            var b = mesh.Position(mesh.Indices[i + 1]);
            var c = mesh.Position(mesh.Indices[i + 2]);
            Assert.InRange(Math.Abs(a.Y - .2), 0, 1e-7);
            area += Math.Abs((b.XZ - a.XZ).Cross(c.XZ - a.XZ)) * .5;
        }
        // Six incident terrain faces, each with one low vertex: the half-height
        // shoreline encloses a quarter of each face's area.
        Assert.InRange(Math.Abs(area - .75 * hf.Step * hf.Step), 0, 1e-8);
    }

    [Fact]
    public void ShoreTrianglesNeverBridgeDryTerrainAcrossAHeightfieldDiagonal()
    {
        var w = TestUtil.FlatWorld(edit: d => { d.Diameter = 5; d.Water.WaterTable = .2; });
        var hf = w.Terrain; hf.BeginEdit();
        for (int z = 0; z < hf.Nz; z++)
            for (int x = 0; x < hf.Nx; x++)
                hf.H[hf.VertexIndex(x, z)] = .2 + .09 * Math.Sin(x * 1.7 + z * .4) * Math.Cos(z * 1.3);
        hf.Touch(); w.Water.RefreshBed(hf);
        var mesh = WaterMesh.BuildWaterTable(w, hf);
        int checkedFaces = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            int ia = mesh.Indices[i], ib = mesh.Indices[i + 1], ic = mesh.Indices[i + 2];
            if (mesh.Normals[ia * 3 + 1] < .9 || mesh.Normals[ib * 3 + 1] < .9 || mesh.Normals[ic * 3 + 1] < .9) continue;
            var a = mesh.Position(ia); var b = mesh.Position(ib); var c = mesh.Position(ic);
            foreach (var weights in new[] { new Vec3(1.0 / 3, 1.0 / 3, 1.0 / 3), new Vec3(.1, .3, .6), new Vec3(.6, .3, .1) })
            {
                var p = a * weights.X + b * weights.Y + c * weights.Z;
                Assert.True(hf.Height(p.XZ) <= .2000002, $"Water bridges dry terrain at {p.XZ}: {hf.Height(p.XZ)}");
                Assert.True(w.Domain.SignedDistance(p.XZ) <= 1e-6);
            }
            checkedFaces++;
        }
        Assert.True(checkedFaces > 20);
    }
}
