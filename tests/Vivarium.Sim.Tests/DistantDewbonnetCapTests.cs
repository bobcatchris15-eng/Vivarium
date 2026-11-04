using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class DistantDewbonnetCapTests
{
    private static MeshData Build(string id, ulong seed, int tier, int rings) =>
        OrganismMeshes.FloraTier(TestUtil.Content.FloraOrThrow(id), seed, tier,
            farGillStations: 3, farDewbonnetCapRings: rings, thinMinorDetails: false);

    private static (Vec3, Vec3, (float, float, float, float), (float, float), (float, float)) Vertex(MeshData m, int i) =>
        (m.Position(i), m.NormalAt(i),
            (m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]),
            (m.UV[i * 2], m.UV[i * 2 + 1]), (m.UV2[i * 2], m.UV2[i * 2 + 1]));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CloserTiersStayExact(int tier)
    {
        var reference = Build("dewbonnet", 173, tier, 16);
        var candidate = Build("dewbonnet", 173, tier, 8);
        Assert.Equal(reference.DigestHex(), candidate.DigestHex());
        Assert.Equal(Enumerable.Range(0, reference.VertexCount).Select(i => Vertex(reference, i)),
            Enumerable.Range(0, candidate.VertexCount).Select(i => Vertex(candidate, i)));
    }

    [Theory]
    [MemberData(nameof(DistantGillTests.DewbonnetSeeds), MemberType = typeof(DistantGillTests))]
    public void FarCapsRetainAuthoredSamplesAndSolidRims(ulong seed)
    {
        var reference = Build("dewbonnet", seed, 2, 16);
        var candidate = Build("dewbonnet", seed, 2, 8);
        var referenceVertices = Enumerable.Range(0, reference.VertexCount).Select(i => Vertex(reference, i)).ToHashSet();
        var candidateVertices = Enumerable.Range(0, candidate.VertexCount).Select(i => Vertex(candidate, i)).ToHashSet();
        Assert.True(candidateVertices.All(referenceVertices.Contains), "Coarser caps must sample the same authored surface and attributes");
        // Both cap perimeter rings retain every vertex, including UVs and normals.
        var rims = Enumerable.Range(0, reference.VertexCount).Where(i =>
            (reference.UV2[i * 2] == 0 && reference.UV[i * 2 + 1] == 1)
            || (reference.UV2[i * 2] == 1 && reference.NormalAt(i).Y == -1 && reference.UV[i * 2 + 1] == 16));
        Assert.All(rims, i => Assert.Contains(Vertex(reference, i), candidateVertices));
        Assert.True(candidate.TriangleCount < reference.TriangleCount * .8, "Radial coarsening should remove meaningful geometry");
        Assert.All(candidate.Positions, x => Assert.True(float.IsFinite(x)));
        Assert.All(candidate.Indices, i => Assert.InRange(i, 0, candidate.VertexCount - 1));
    }

    [Theory]
    [InlineData("bonecup")]
    [InlineData("embercup")]
    [InlineData("ghostbonnet")]
    public void OtherFungiStayExact(string id) =>
        Assert.Equal(Build(id, 173, 2, 16).DigestHex(), Build(id, 173, 2, 8).DigestHex());
}
