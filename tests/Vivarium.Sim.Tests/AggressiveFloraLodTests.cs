using System.Reflection;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class AggressiveFloraLodTests
{
    private static MeshData Finish(MeshData mesh) => (MeshData)typeof(OrganismMeshes)
        .GetMethod("FinishTier", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { mesh })!;

    private static MeshData Fixture(bool cull)
    {
        var m = new MeshData { Lod = new FloraLodParams(LeafFraction: .25, CullMinorDetails: cull), FloraDetailLevel = 2 };
        Primitives.Tube(m, new[] { Vec3.Zero, Vec3.Up }, new[] { .1, .08 }, 6,
            (i, v) => (new[] { .3, .2, .1 }, 1, (double)i, v, 0, 0));
        void Quad(double x, bool essential)
        {
            int first = m.VertexCount;
            m.AddVertex(new(x, .5, 0), Vec3.Up, new[] { .2, .5, .1 });
            m.AddVertex(new(x + .1, .5, 0), Vec3.Up, new[] { .2, .5, .1 });
            m.AddVertex(new(x, .5, .1), Vec3.Up, new[] { .2, .5, .1 });
            m.AddVertex(new(x + .1, .5, .1), Vec3.Up, new[] { .2, .5, .1 });
            Primitives.TriangleFacing(m, first, first + 2, first + 1, Vec3.Up);
            Primitives.TriangleFacing(m, first + 1, first + 2, first + 3, Vec3.Up);
            if (essential) m.EssentialSpans.Add((first, 4));
        }
        int compound = m.VertexCount, compoundIndices = m.Indices.Count;
        Quad(4, false); Quad(4.2, true);
        m.RecordLeaf(compound, compoundIndices, new(4.2, .5, 0), .1);
        for (int i = 1; i <= 8; i++)
        {
            int first = m.VertexCount, index = m.Indices.Count;
            Quad(i, false); m.RecordLeaf(first, index, new(i, .5, 0), .1);
        }
        return m;
    }

    [Fact]
    public void ThinningPreservesMainStemEssentialFeaturesAndValidMetadata()
    {
        var raw = Fixture(true);
        var m = Finish(raw);
        Assert.True(m.TriangleCount < raw.TriangleCount);
        Assert.Single(m.Structural);
        Assert.Equal(raw.Structural[0].VertexCount, m.Structural[0].VertexCount);
        Assert.Single(m.EssentialSpans);
        Assert.Equal(4, m.EssentialSpans[0].VertexCount);
        Assert.Contains(m.Leaves, l => l.Identity == raw.Leaves[0].Identity);
        Assert.Equal(4, m.Leaves.Single(l => l.Identity == raw.Leaves[0].Identity).VertexCount);
        foreach (var s in m.Structural) Assert.InRange(s.FirstVertex + s.VertexCount, 1, m.VertexCount);
        foreach (var s in m.LeafSpans) Assert.InRange(s.FirstVertex + s.VertexCount, 1, m.VertexCount);
        foreach (var l in m.Leaves)
        {
            Assert.InRange(l.FirstVertex + l.VertexCount, 1, m.VertexCount);
            Assert.InRange(l.FirstIndex + l.IndexCount, 1, m.Indices.Count);
        }
        Assert.Equal(m.DigestHex(), Finish(raw).DigestHex());
    }

    [Fact]
    public void NonCullingPathPreservesAllGeometry()
    {
        var raw = Fixture(false);
        var m = Finish(raw);
        Assert.Equal(raw.DigestHex(), m.DigestHex());
        Assert.Equal(raw.Leaves, m.Leaves);
        Assert.Equal(raw.LeafSpans, m.LeafSpans);
    }

    [Theory]
    [InlineData("plumegrass")]
    [InlineData("fencomb")]
    public void DenseFoliageHasMeaningfullyCheaperFarGeometry(string id)
    {
        var sp = TestUtil.Content.FloraOrThrow(id);
        var near = OrganismMeshes.FloraTier(sp, 173, 0, tieredSource: true);
        var far = OrganismMeshes.FloraTier(sp, 173, 2, tieredSource: true);
        Assert.True(far.TriangleCount < near.TriangleCount * .35, $"{id}: {near.TriangleCount} -> {far.TriangleCount}");
        Assert.All(far.Positions, x => Assert.True(float.IsFinite(x)));
        Assert.All(far.Normals, x => Assert.True(float.IsFinite(x)));
        Assert.All(far.Indices, i => Assert.InRange(i, 0, far.VertexCount - 1));
        Assert.All(far.Structural, s => Assert.InRange(s.FirstVertex + s.VertexCount, 1, far.VertexCount));
    }

    [Fact]
    public void FineTubeLeavesAreSelectedAsWholeOrgans()
    {
        var m = new MeshData { Lod = new FloraLodParams(LeafFraction: .25, CullMinorDetails: true), FloraDetailLevel = 2 };
        void Tube(Vec3 a, Vec3 b, double radius) => Primitives.Tube(m, new[] { a, b }, new[] { radius, radius * .6 }, 6,
            (i, v) => (new[] { .2, .5, .1 }, 1, (double)i, v, 0, 1));
        Tube(Vec3.Zero, Vec3.Up, .1);
        for (int i = 0; i < 8; i++)
        {
            int first = m.VertexCount, index = m.Indices.Count;
            var a = new Vec3(i, .5, 0);
            Tube(a, a + new Vec3(.3, .2, 0), .005);
            Tube(a + new Vec3(.1, .1, 0), a + new Vec3(.2, .1, .2), .002);
            m.RecordLeaf(first, index, a, .4);
        }
        int leafVertices = m.Leaves[0].VertexCount;
        var result = Finish(m);
        Assert.InRange(result.Leaves.Count, 1, 7);
        Assert.All(result.Leaves, leaf => Assert.Equal(leafVertices, leaf.VertexCount));
    }

    [Fact]
    public void AuditModeCannotPolluteGameplayCalibration()
    {
        var sp = TestUtil.Content.FloraOrThrow("fencomb");
        var first = OrganismMeshes.FloraTier(sp, 109, 2);
        var unthinned = OrganismMeshes.FloraTier(sp, 109, 2, thinMinorDetails: false);
        var repeated = OrganismMeshes.FloraTier(sp, 109, 2);
        Assert.Equal(first.DigestHex(), repeated.DigestHex());
        Assert.True(unthinned.TriangleCount > first.TriangleCount);
    }

    [Fact]
    public void BarkBranchesInsideCompoundLeafSpansRemainStructural()
    {
        var raw = Fixture(true);
        int first = raw.VertexCount, index = raw.Indices.Count;
        Primitives.Tube(raw, new[] { new Vec3(4, .4, 0), new Vec3(4, .6, 0) }, new[] { .06, .05 }, 6,
            (i, v) => (new[] { .3, .2, .1 }, 1, (double)i, v, 2, 0));
        int count = raw.VertexCount - first;
        raw.RecordLeaf(first, index, new(4, .4, 0), .2);
        var result = Finish(raw);
        Assert.Contains(result.Structural, s => s.Radius == .06 && s.VertexCount == count);
    }
}
