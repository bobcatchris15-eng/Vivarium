using Vivarium.Sim.Geometry;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Tests;

public class LogOverhaulTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void DecayedLogsHaveContinuousInwardFacingTunnelAndDarkInterior(int decay)
    {
        var mesh = PropMeshes.Log(42, 1.8, 0.2, decay);
        var inner = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2 + 1] == 2).ToArray();
        Assert.NotEmpty(inner);
        Assert.True(inner.Select(i => mesh.Position(i).X).Max() - inner.Select(i => mesh.Position(i).X).Min() >= 1.8);
        Assert.True(inner.Select(i => mesh.Colors[i * 4]).Min() < 0.09);
        Assert.True(inner.Select(i => mesh.Colors[i * 4]).Max() > 0.99);
        // Every 49-vertex ring has a real open bore; normals point toward its centre.
        for (int row = 0; row < inner.Length / 49; row++)
        {
            var center = Vec3.Zero;
            for (int j = 0; j < 48; j++) center += mesh.Position(inner[row * 49 + j]) / 48;
            for (int j = 0; j < 48; j++)
            {
                int i = inner[row * 49 + j];
                var radial = mesh.Position(i) - center;
                Assert.True(radial.Length > 0.04);
                Assert.True(mesh.NormalAt(i).Dot(radial) < 0);
            }
        }
        // No endgrain triangles block the tunnel centre.
        foreach (int i in Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2 + 1] == 1))
            Assert.True(mesh.UV[i * 2] >= 0.35);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DisplacedLogNormalsHaveCorrectGodotWindingAndBarkSeam(int decay)
    {
        const int along = 120, stride = 73;
        var mesh = PropMeshes.Log(91, 1.8, 0.2, decay);
        for (int row = 0; row <= along; row++)
        {
            int a = row * stride, b = a + 72;
            Assert.True((mesh.Position(a) - mesh.Position(b)).Length < 1e-6);
            Assert.True((mesh.NormalAt(a) - mesh.NormalAt(b)).Length < 1e-6);
        }
        foreach (int i in mesh.Indices.Distinct())
        {
            var normal = mesh.NormalAt(i);
            Assert.True(double.IsFinite(normal.X + normal.Y + normal.Z));
            Assert.InRange(normal.Length, 0.999, 1.001);
        }
        int agree = 0, count = 0;
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var face = (mesh.Position(c) - mesh.Position(a)).Cross(mesh.Position(b) - mesh.Position(a));
            Assert.True(face.LengthSq > 1e-26);
            if (face.Dot(mesh.NormalAt(a) + mesh.NormalAt(b) + mesh.NormalAt(c)) > 0) agree++;
            count++;
        }
        Assert.True(agree > count * 0.99, $"Only {agree}/{count} face normals agree with clockwise winding");
    }

    [Fact]
    public void SplintersCarryFiniteNonconstantUvAndMeshesAreDeterministic()
    {
        var a = PropMeshes.Log(19, 1.8, 0.2, 3);
        var b = PropMeshes.Log(19, 1.8, 0.2, 3);
        Assert.Equal(a.DigestHex(), b.DigestHex());
        Assert.Equal(a.Normals, b.Normals);
        var shards = Enumerable.Range(0, a.VertexCount).Where(i => a.UV2[i * 2 + 1] == 3).ToArray();
        Assert.True(shards.Length >= 24 * 22);
        Assert.True(shards.Select(i => a.UV[i * 2]).Max() > 0.02);
        Assert.True(shards.Select(i => a.UV[i * 2 + 1]).Max() > 0.005);
    }
}
