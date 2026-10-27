using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public sealed class CoveragePlantGeometryTests
{
    public static IEnumerable<object[]> Forms => Enum.GetValues<CoveragePlantForm>().Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(Forms))]
    public void ClumpIsFiniteDeterministicClosedScaleGeometryWithUniformMarkers(CoveragePlantForm form)
    {
        var first = new MeshData();
        var second = new MeshData();
        CoveragePlantGeometry.AppendClump(first, new Vec3(.2, .3, -.1), new Vec3(.12, .97, -.18), form, 817u);
        CoveragePlantGeometry.AppendClump(second, new Vec3(.2, .3, -.1), new Vec3(.12, .97, -.18), form, 817u);

        Assert.Equal(first.DigestHex(), second.DigestHex());
        Assert.InRange(first.TriangleCount, 50, 1500);
        Assert.All(first.Positions, x => Assert.True(float.IsFinite(x)));
        Assert.All(first.Normals, x => Assert.True(float.IsFinite(x)));
        var marker = -20f - (int)form;
        for (int i = 0; i < first.VertexCount; i++)
        {
            Assert.Equal(marker, first.UV2[i * 2 + 1]);
            Assert.Contains(first.UV2[i * 2], new[] { 0f, 1f });
            var n = first.NormalAt(i);
            Assert.InRange(n.Length, .98, 1.02);
        }

        var bounds = first.Bounds();
        Assert.InRange(bounds.Max.Y - bounds.Min.Y, .005, .08);
        Assert.InRange(bounds.Max.X - bounds.Min.X, .01, .07);
        Assert.InRange(bounds.Max.Z - bounds.Min.Z, .01, .07);
        var up = new Vec3(.12, .97, -.18).Normalized();
        for (int i = 0; i < first.VertexCount; i++)
        {
            var delta = first.Position(i) - new Vec3(.2, .3, -.1);
            Assert.InRange(delta.Length, 0, .075);
            Assert.InRange(delta.Dot(up), -.006, .08);
        }
        for (int t = 0; t < first.Indices.Count; t += 3)
        {
            int a = first.Indices[t], b = first.Indices[t + 1], c = first.Indices[t + 2];
            Assert.Equal(first.UV2[a * 2], first.UV2[b * 2]);
            Assert.Equal(first.UV2[a * 2], first.UV2[c * 2]);
            var cross = (first.Position(b) - first.Position(a)).Cross(first.Position(c) - first.Position(a));
            Assert.True(cross.Length > 1e-10, $"Degenerate triangle at index {t / 3} for {form}.");
            var averageNormal = (first.NormalAt(a) + first.NormalAt(b) + first.NormalAt(c)).Normalized();
            Assert.True(cross.Normalized().Dot(averageNormal) < -.05, $"Inconsistent Godot winding at index {t / 3} for {form}.");
        }
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void ClumpOrientationAndSeedRemainFinite(CoveragePlantForm form)
    {
        var mesh = new MeshData();
        CoveragePlantGeometry.AppendClump(mesh, Vec3.Zero, new Vec3(1, .01, .02), form, 91u, .8, 1.2);
        Assert.All(mesh.Positions, x => Assert.True(float.IsFinite(x)));
        Assert.InRange(mesh.TriangleCount, 50, 1500);
    }

    [Fact]
    public void SurfaceSamplerInterpolatesSlopesChoosesHighestAndSkipsVerticalTriangles()
    {
        var slope = new MeshData();
        AddTriangle(slope, new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 0, 1));
        Assert.Equal(.2, new TriangleSurfaceSampler(slope).HeightAt(.2, .2), 5);

        var mesh = new MeshData();
        AddTriangle(mesh, new Vec3(0, 0, 0), new Vec3(1, 1, 0), new Vec3(0, 0, 1));
        AddTriangle(mesh, new Vec3(0, 2, 0), new Vec3(1, 2, 0), new Vec3(0, 2, 1));
        AddTriangle(mesh, new Vec3(2, 0, 0), new Vec3(2, 1, 0), new Vec3(2, 0, 1)); // vertical in XZ projection

        var sampler = new TriangleSurfaceSampler(mesh);
        Assert.Equal(2, sampler.HeightAt(.2, .2), 5);
        Assert.Equal(2, sampler.HeightAt(.4, .1), 5);
        Assert.True(double.IsNaN(sampler.HeightAt(3, 3)));
        Assert.True(double.IsNaN(sampler.HeightAt(2, .2)));
    }

    [Fact]
    public void SurfaceSamplerReadsTopOfProductionLogMesh()
    {
        var log = PropMeshes.Log(0xC0FFEEUL, 1.1, .095, 1);
        var sampler = new TriangleSurfaceSampler(log);
        Assert.True(double.IsFinite(sampler.HeightAt(0, 0)));
        Assert.True(sampler.HeightAt(0, 0) > 0.07);
        Assert.True(double.IsNaN(sampler.HeightAt(1, 0)));
    }

    private static void AddTriangle(MeshData mesh, Vec3 a, Vec3 b, Vec3 c)
    {
        int first = mesh.VertexCount;
        var n = (b - a).Cross(c - a).Normalized();
        mesh.AddVertex(a, n, 1, 1, 1, 1);
        mesh.AddVertex(b, n, 1, 1, 1, 1);
        mesh.AddVertex(c, n, 1, 1, 1, 1);
        mesh.AddTriangle(first, first + 1, first + 2);
    }
}
