using System.Reflection;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class DistantGillTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NearAndMiddleMeshesStayExact(int tier)
    {
        var species = TestUtil.Content.FloraOrThrow("dewbonnet");
        var expected = OrganismMeshes.FloraTier(species, 173, tier);
        var candidate = OrganismMeshes.FloraTier(species, 173, tier, farGillStations: 3);
        Assert.Equal(expected.DigestHex(), candidate.DigestHex());
        Assert.Equal(expected.Normals, candidate.Normals); Assert.Equal(expected.Colors, candidate.Colors);
        Assert.Equal(expected.UV, candidate.UV); Assert.Equal(expected.UV2, candidate.UV2);
    }

    public static IEnumerable<object[]> DewbonnetSeeds()
    {
        yield return new object[] { 173UL };
        ulong species = Hash.Fnv1a64("flora.visual.dewbonnet");
        for (ulong morph = 1; morph <= 5; morph++)
            yield return new object[] { Rng.Mix(species, morph * 0x9E3779B97F4A7C15UL) };
    }

    [Theory]
    [MemberData(nameof(DewbonnetSeeds))]
    public void FarReductionPreservesEveryNonGillVertexAndTriangle(ulong seed)
    {
        var species = TestUtil.Content.FloraOrThrow("dewbonnet");
        var expected = OrganismMeshes.FloraTier(species, seed, 2);
        var candidate = OrganismMeshes.FloraTier(species, seed, 2, farGillStations: 3);
        bool Gill(MeshData m, int i) => m.UV2[i * 2] == 1 && m.NormalAt(i).Y == 0;
        int[] NonGill(MeshData m) => Enumerable.Range(0, m.VertexCount).Where(i => !Gill(m, i)).ToArray();
        var a = NonGill(expected); var b = NonGill(candidate);
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(expected.Position(a[i]), candidate.Position(b[i]));
            Assert.Equal(expected.NormalAt(a[i]), candidate.NormalAt(b[i]));
            for (int k = 0; k < 4; k++) Assert.Equal(expected.Colors[a[i] * 4 + k], candidate.Colors[b[i] * 4 + k]);
            for (int k = 0; k < 2; k++)
            {
                Assert.Equal(expected.UV[a[i] * 2 + k], candidate.UV[b[i] * 2 + k]);
                Assert.Equal(expected.UV2[a[i] * 2 + k], candidate.UV2[b[i] * 2 + k]);
            }
        }
        int[] NonGillTriangles(MeshData m, int[] vertices)
        {
            var map = Enumerable.Repeat(-1, m.VertexCount).ToArray();
            for (int i = 0; i < vertices.Length; i++) map[vertices[i]] = i;
            var result = new List<int>();
            for (int i = 0; i < m.Indices.Count; i += 3)
            {
                int count = Enumerable.Range(0, 3).Count(j => map[m.Indices[i + j]] >= 0);
                Assert.True(count is 0 or 3, "Gill triangles must not borrow cap/stipe vertices");
                if (count == 3) for (int j = 0; j < 3; j++) result.Add(map[m.Indices[i + j]]);
            }
            return result.ToArray();
        }
        Assert.Equal(NonGillTriangles(expected, a), NonGillTriangles(candidate, b));
        Assert.True(candidate.TriangleCount < expected.TriangleCount);
        int oldGills = expected.VertexCount - a.Length, newGills = candidate.VertexCount - b.Length;
        Assert.True(oldGills > 0);
        Assert.Equal(0, oldGills % (36 * 10 * 4));
        Assert.Equal(oldGills / 10 * 4, newGills);
    }

    [Fact]
    public void EveryGillKeepsBothEndpointsAndNormals()
    {
        var method = typeof(OrganismMeshes).GetMethod("AddLamellarGills", BindingFlags.NonPublic | BindingFlags.Static)!;
        MeshData Build(int stations)
        {
            var mesh = new MeshData { Lod = new FloraLodParams(GillStations: stations) };
            Func<double, double, Vec3> underside = (f, angle) => new(Math.Cos(angle) * f, 1 - f * f * .1, Math.Sin(angle) * f);
            method.Invoke(null, new object[] { mesh, underside, 1.0, new[] { .2, .3, .4 }, 36 });
            return mesh;
        }
        var a = Build(9); var b = Build(3);
        Assert.Equal(36 * 10 * 4, a.VertexCount); Assert.Equal(36 * 4 * 4, b.VertexCount);
        for (int g = 0; g < 36; g++)
        foreach (bool outer in new[] { false, true })
        for (int k = 0; k < 4; k++)
        {
            int ai = g * 40 + (outer ? 36 : 0) + k, bi = g * 16 + (outer ? 12 : 0) + k;
            Assert.Equal(a.Position(ai), b.Position(bi)); Assert.Equal(a.NormalAt(ai), b.NormalAt(bi));
        }
        Assert.All(b.Positions, x => Assert.True(float.IsFinite(x)));
        Assert.All(b.Indices, i => Assert.InRange(i, 0, b.VertexCount - 1));
    }

    [Theory]
    [InlineData("embercup")]
    [InlineData("bonecup")]
    public void OtherFungiAreUnchanged(string id)
    {
        var species = TestUtil.Content.FloraOrThrow(id);
        Assert.Equal(OrganismMeshes.FloraTier(species, 173, 2).DigestHex(),
            OrganismMeshes.FloraTier(species, 173, 2, farGillStations: 3).DigestHex());
    }
}
