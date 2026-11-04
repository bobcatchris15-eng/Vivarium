using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>Moonfern's sparse pierced-sheet anatomy; other fern kernels remain independent.</summary>
public sealed class MoonfernFountainTests
{
    [Theory]
    [InlineData(177UL)]
    [InlineData(8993UL)]
    [InlineData(15511UL)]
    public void MatureMoonfernHasTwoOrThreeConnectedPiercedFronds(ulong seed)
    {
        var sp = TestUtil.Content.FloraOrThrow("moonfern");
        var mesh = OrganismMeshes.Flora(sp, seed);
        Assert.InRange(mesh.LeafSpans.Count, 2, 3);
        Assert.Equal(mesh.LeafSpans.Count, mesh.Structural.Count);
        Assert.InRange(mesh.TriangleCount, 500, 10000);
        foreach (var leaf in mesh.LeafSpans)
        {
            int end = leaf.FirstVertex + leaf.VertexCount;
            var edges = new HashSet<(int,int)>(); var vertices = new HashSet<int>();
            var parent = new Dictionary<int,int>();
            int Find(int v) { if (!parent.ContainsKey(v)) parent[v] = v; return parent[v] == v ? v : parent[v] = Find(parent[v]); }
            int faces = 0;
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (a < leaf.FirstVertex || a >= end) continue;
                Assert.InRange(b, leaf.FirstVertex, end - 1); Assert.InRange(c, leaf.FirstVertex, end - 1);
                Assert.True((mesh.Position(b) - mesh.Position(a)).Cross(mesh.Position(c) - mesh.Position(a)).LengthSq > 1e-22);
                foreach (var (x,y) in new[] { (a,b), (b,c), (c,a) })
                {
                    edges.Add((Math.Min(x,y),Math.Max(x,y))); vertices.Add(x); vertices.Add(y);
                    parent[Find(x)] = Find(y);
                }
                faces++;
            }
            Assert.Single(vertices.Select(Find).Distinct());
            // A connected disk has V-E+F=1. Many negative units prove actual cut-through apertures.
            Assert.True(vertices.Count - edges.Count + faces < -10);
            Assert.All(Enumerable.Range(leaf.FirstVertex, leaf.VertexCount), i => {
                Assert.Equal(0, mesh.Colors[i * 4 + 3]); Assert.Equal(1, mesh.UV2[i * 2 + 1]);
            });
        }
        Assert.All(mesh.Positions, v => Assert.True(float.IsFinite(v)));
        Assert.All(mesh.Normals, v => Assert.True(float.IsFinite(v)));
        Assert.Equal(mesh.DigestHex(), OrganismMeshes.Flora(sp, seed).DigestHex());
    }

    [Theory]
    [InlineData(177UL)]
    [InlineData(8993UL)]
    public void EveryLodRetainsTheWholeSparseCrown(ulong seed)
    {
        var sp = TestUtil.Content.FloraOrThrow("moonfern");
        var near = OrganismMeshes.FloraTier(sp, seed, 0, tieredSource:true);
        foreach (int tier in new[] { 1, 2 })
        {
            var mesh = OrganismMeshes.FloraTier(sp, seed, tier, tieredSource:true);
            Assert.Equal(near.Leaves.Count, mesh.Leaves.Count);
            Assert.InRange(mesh.Leaves.Count, 2, 3);
            Assert.True(mesh.TriangleCount < near.TriangleCount);
            Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.VertexCount - 1));
            for (int i = 0; i < mesh.Leaves.Count; i++) {
                Assert.Equal(near.Leaves[i].Attachment, mesh.Leaves[i].Attachment);
                Assert.Equal(near.Leaves[i].Length, mesh.Leaves[i].Length);
                Assert.Equal(near.Leaves[i].Identity, mesh.Leaves[i].Identity);
            }
        }
    }

    [Fact]
    public void YoungMoonfernKeepsSparseCrownAtSmallerSize()
    {
        var sp = TestUtil.Content.FloraOrThrow("moonfern");
        var young = OrganismMeshes.Flora(sp, 991UL, juvenile:true);
        var adult = OrganismMeshes.Flora(sp, 991UL);
        Assert.Equal(adult.LeafSpans.Count, young.LeafSpans.Count);
        Assert.True(young.Bounds().Max.Y < adult.Bounds().Max.Y * .6);
        Assert.True(young.Bounds().Max.X - young.Bounds().Min.X < adult.Bounds().Max.X - adult.Bounds().Min.X);
    }
}
