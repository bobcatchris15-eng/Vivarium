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
    public void DewbonnetHasSolidUndersidesAndNoGillStripsAtEveryTier(ulong seed)
    {
        var species = TestUtil.Content.FloraOrThrow("dewbonnet");
        for (int tier = 0; tier < 3; tier++)
        {
            var mesh = OrganismMeshes.FloraTier(species, seed, tier);
            var underside = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2] == 1).ToArray();
            Assert.NotEmpty(underside);
            Assert.All(underside, i => {
                Assert.Equal(-1, mesh.NormalAt(i).Y);
                Assert.InRange(mesh.UV[i * 2], 0, 1);
                Assert.InRange(mesh.UV[i * 2 + 1], 0, 16);
            });
            var centres = underside.Where(i => mesh.UV[i * 2 + 1] == 0).Select(mesh.Position).ToHashSet();
            Assert.NotEmpty(centres);
            var usedCentres = new HashSet<Vec3>();
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                var indices = mesh.Indices.Skip(t).Take(3).ToArray();
                if (!indices.All(i => mesh.UV2[i * 2] == 1)) continue;
                var a = mesh.Position(indices[0]); var b = mesh.Position(indices[1]); var c = mesh.Position(indices[2]);
                Assert.True((b - a).Cross(c - a).LengthSq > 1e-20, "Solid underside must have no degenerate triangles");
                foreach (var i in indices) if (centres.Contains(mesh.Position(i))) usedCentres.Add(mesh.Position(i));
            }
            Assert.True(centres.SetEquals(usedCentres), "Every cap underside must close at its axis");
            Assert.Equal(mesh.DigestHex(), OrganismMeshes.FloraTier(species, seed, tier, farGillStations: 3).DigestHex());
            Assert.All(mesh.Positions, x => Assert.True(float.IsFinite(x)));
            Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
        }
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
