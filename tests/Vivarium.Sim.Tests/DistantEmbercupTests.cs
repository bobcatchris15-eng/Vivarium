using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class DistantEmbercupTests
{
    public static IEnumerable<object[]> Seeds()
    {
        ulong species = Hash.Fnv1a64("flora.visual.embercup");
        for (ulong morph = 1; morph <= 5; morph++)
            yield return new object[] { Rng.Mix(species, morph * 0x9E3779B97F4A7C15UL) };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CloserTiersStayExact(int tier)
    {
        var species = TestUtil.Content.FloraOrThrow("embercup");
        var a = OrganismMeshes.FloraTier(species, 173, tier);
        var b = OrganismMeshes.FloraTier(species, 173, tier, farEmberMicroSides: 3);
        Assert.Equal(a.DigestHex(), b.DigestHex());
        Assert.Equal(a.Normals, b.Normals); Assert.Equal(a.Colors, b.Colors);
        Assert.Equal(a.UV, b.UV); Assert.Equal(a.UV2, b.UV2);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void AllMicrophyllsStayInPlaceAndEveryOtherOrganStaysExact(ulong seed)
    {
        var species = TestUtil.Content.FloraOrThrow("embercup");
        var a = OrganismMeshes.FloraTier(species, seed, 2);
        var b = OrganismMeshes.FloraTier(species, seed, 2, farEmberMicroSides: 3);
        var la = a.LeafSpans.Where(s => a.UV2[s.FirstVertex * 2] == 0).ToArray();
        var lb = b.LeafSpans.Where(s => b.UV2[s.FirstVertex * 2] == 0).ToArray();
        Assert.NotEmpty(la); Assert.Equal(la.Length, lb.Length);
        int Centre(MeshData m, (int FirstVertex, int VertexCount) span, float t) =>
            Assert.Single(Enumerable.Range(span.FirstVertex, span.VertexCount)
                .Where(v => m.UV[v * 2] == t && m.UV[v * 2 + 1] == .5f));
        for (int i = 0; i < la.Length; i++)
        {
            Assert.True(lb[i].VertexCount < la[i].VertexCount);
            foreach (float t in new[] { 0f, 1f })
            {
                int va = Centre(a, la[i], t), vb = Centre(b, lb[i], t);
                Assert.Equal(a.Position(va), b.Position(vb));
                Assert.Equal(a.NormalAt(va), b.NormalAt(vb));
                for (int k = 0; k < 4; k++) Assert.Equal(a.Colors[va * 4 + k], b.Colors[vb * 4 + k]);
            }
        }
        int[] Retained(MeshData m, (int FirstVertex, int VertexCount)[] spans)
        {
            var micro = new bool[m.VertexCount];
            foreach (var s in spans) Array.Fill(micro, true, s.FirstVertex, s.VertexCount);
            return Enumerable.Range(0, m.VertexCount).Where(v => !micro[v]).ToArray();
        }
        var ra = Retained(a, la); var rb = Retained(b, lb);
        Assert.Equal(ra.Length, rb.Length);
        for (int i = 0; i < ra.Length; i++)
        {
            Assert.Equal(a.Position(ra[i]), b.Position(rb[i]));
            Assert.Equal(a.NormalAt(ra[i]), b.NormalAt(rb[i]));
            for (int k = 0; k < 4; k++) Assert.Equal(a.Colors[ra[i] * 4 + k], b.Colors[rb[i] * 4 + k]);
            for (int k = 0; k < 2; k++)
            {
                Assert.Equal(a.UV[ra[i] * 2 + k], b.UV[rb[i] * 2 + k]);
                Assert.Equal(a.UV2[ra[i] * 2 + k], b.UV2[rb[i] * 2 + k]);
            }
        }
        int[] Triangles(MeshData m, int[] retained)
        {
            var map = Enumerable.Repeat(-1, m.VertexCount).ToArray();
            for (int i = 0; i < retained.Length; i++) map[retained[i]] = i;
            var result = new List<int>();
            for (int i = 0; i < m.Indices.Count; i += 3)
            {
                int count = Enumerable.Range(0, 3).Count(k => map[m.Indices[i + k]] >= 0);
                Assert.True(count is 0 or 3);
                if (count == 3) for (int k = 0; k < 3; k++) result.Add(map[m.Indices[i + k]]);
            }
            return result.ToArray();
        }
        Assert.Equal(Triangles(a, ra), Triangles(b, rb));
        Assert.True(b.TriangleCount < a.TriangleCount);
        Assert.All(b.Positions, p => Assert.True(float.IsFinite(p)));
        Assert.All(b.Indices, i => Assert.InRange(i, 0, b.VertexCount - 1));
    }

    [Theory]
    [InlineData("bonecup")]
    [InlineData("dewbonnet")]
    public void OtherFungiStayExact(string id)
    {
        var species = TestUtil.Content.FloraOrThrow(id);
        Assert.Equal(OrganismMeshes.FloraTier(species, 173, 2, farGillStations: 3).DigestHex(),
            OrganismMeshes.FloraTier(species, 173, 2, farGillStations: 3, farEmberMicroSides: 3).DigestHex());
    }
}
