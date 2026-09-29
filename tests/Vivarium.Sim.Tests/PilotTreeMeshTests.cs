using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class PilotTreeMeshTests
{
    private static VivariumWorld World(string id, int corner = 0, ulong seed = 76)
    {
        var d = TestUtil.FlatDescriptor(seed);
        d.PilotTreeId = id; d.PilotTreeCorner = corner;
        return VivariumWorld.CreateBaseline(TestUtil.Content, d);
    }

    [Fact]
    public void SixPilotFormsHaveDistinctDeterministicFiniteBoundedAnatomy()
    {
        var digests = new HashSet<string>();
        foreach (var def in PilotTreeCatalog.All)
        {
            var world = World(def.Id);
            var mesh = PilotTreeMeshes.Build(world);
            Assert.Equal(mesh.DigestHex(), PilotTreeMeshes.Build(world).DigestHex());
            Assert.True(digests.Add(mesh.DigestHex()), def.Id);
            Assert.InRange(mesh.TriangleCount, 3000, 180000);
            Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Count);
            Assert.Equal(mesh.VertexCount * 4, mesh.Colors.Count);
            Assert.Equal(mesh.VertexCount * 2, mesh.UV.Count);
            int foliage = 0, wood = 0;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Assert.True(mesh.Position(i).IsFinite && mesh.NormalAt(i).IsFinite, def.Id);
                Assert.InRange(mesh.NormalAt(i).Length, .99, 1.01);
                if (mesh.Colors[i * 4 + 3] < .1) foliage++;
                if (mesh.Colors[i * 4 + 3] > .9) wood++;
            }
            Assert.True(foliage > 100 && wood > 1000);
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                Assert.InRange(a, 0, mesh.VertexCount - 1);
                Assert.InRange(b, 0, mesh.VertexCount - 1);
                Assert.InRange(c, 0, mesh.VertexCount - 1);
                Assert.True((mesh.Position(b) - mesh.Position(a)).Cross(mesh.Position(c) - mesh.Position(a)).Length > 1e-10, def.Id);
            }
        }
        Assert.Equal(6, digests.Count);
    }

    [Fact]
    public void RootCrestsMatchAuthoritativeSamplesAndFollowBothNeighboringSides()
    {
        foreach (var def in PilotTreeCatalog.All)
            for (int corner = 0; corner < 6; corner++)
            {
                var world = World(def.Id, corner);
                var tree = world.PilotTree!;
                var mesh = PilotTreeMeshes.Build(world);
                var points = Enumerable.Range(0, mesh.VertexCount).Select(mesh.Position).ToArray();
                Assert.Equal(tree.CornerPoint, tree.Anchor);
                Assert.True(world.Domain.Contains(tree.Anchor));
                Assert.Equal(world.Domain.Vertices[corner], tree.CornerPoint);
                foreach (var root in tree.Roots.Where(p => p.AlongSide))
                {
                    // Interior root crests are actual vertices, not a separate decorative approximation.
                    var sample = root.Samples[root.Samples.Count / 2];
                    var crest = Vec3.FromXZ(sample.Position, sample.GroundY + sample.Height);
                    Assert.True(points.Any(p => (p - crest).Length < 1e-5), def.Id);
                    Assert.InRange(tree.RootSurfaceHeight(sample.Position), crest.Y - .001, crest.Y + .001);
                    Assert.True((root.Samples[^1].Position - tree.CornerPoint).Length > world.Domain.Radius * .7);
                }
            }
    }

    [Fact]
    public void NoPilotTreeBuildsNoGeometry()
    {
        var d = TestUtil.FlatDescriptor(); d.PilotTreeId = "none";
        Assert.Equal(0, PilotTreeMeshes.Build(VivariumWorld.CreateBaseline(TestUtil.Content, d)).TriangleCount);
    }
}

