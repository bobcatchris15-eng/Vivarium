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
            // Closed 20x12 lamina shells replace single-sheet crowns; preserve every authored leaf.
            Assert.InRange(mesh.TriangleCount, 3000, 2600000);
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
                Assert.True((mesh.Position(b) - mesh.Position(a)).Cross(mesh.Position(c) - mesh.Position(a)).Length > 1e-10, $"{def.Id} tri {i/3} at {mesh.Position(a)} {mesh.Position(b)} {mesh.Position(c)} alpha {mesh.Colors[a*4+3]}");
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

    [Theory]
    [InlineData("gloomspire")]
    [InlineData("needlevault")]
    public void OldGrowthRootAndTrunkSurviveAuthoredCrownCutover(string species)
    {
        var world=World(species);
        var complete=PilotTreeMeshes.Build(world);
        var structural=PilotTreeMeshes.Build(world,includeCrown:false);
        Assert.True(structural.TriangleCount>1000);
        Assert.True(complete.TriangleCount>structural.TriangleCount);
        Assert.All(Enumerable.Range(0,structural.VertexCount),
            i=>Assert.True(structural.Colors[i*4+3]>.5f,
                "Only authoritative wood and attached root gametophores remain; no crown laminae"));
        foreach(var root in world.PilotTree!.Roots.Where(p=>p.AlongSide))
        {
            var sample=root.Samples[root.Samples.Count/2];
            var crest=Vec3.FromXZ(sample.Position,sample.GroundY+sample.Height);
            Assert.Contains(Enumerable.Range(0,structural.VertexCount),
                i=>(structural.Position(i)-crest).Length<1e-5);
        }
        Assert.Equal(structural.DigestHex(),
            PilotTreeMeshes.Build(world,includeCrown:false).DigestHex());
    }

    [Fact]
    public void DynamicRootMeshAndStaticTrunkRecombineToOriginalExactTree()
    {
        foreach (var def in PilotTreeCatalog.All)
        {
            var w=World(def.Id);
            var source=PilotTreeMeshes.Build(w);
            var roots=PilotTreeMeshes.BuildRoots(w);
            var trunkAndCrown=PilotTreeMeshes.BuildTrunkAndCrown(w);
            Assert.True(roots.TriangleCount>0);
            Assert.True(trunkAndCrown.TriangleCount>0);
            Assert.Equal(source.TriangleCount,
                roots.TriangleCount+trunkAndCrown.TriangleCount);
            var reconstructed=new MeshData();
            reconstructed.Append(roots);
            reconstructed.Append(trunkAndCrown);
            Assert.Equal(source.DigestHex(),reconstructed.DigestHex());
            var onlyTrunk=PilotTreeMeshes.BuildTrunkAndCrown(w,includeCrown:false);
            Assert.True(onlyTrunk.TriangleCount<trunkAndCrown.TriangleCount);
        }
    }

    [Theory]
    [InlineData("gloomspire")]
    [InlineData("needlevault")]
    public void RootFootprintSignatureIgnoresRemoteTerrainButDetectsRootErosion(string species)
    {
        var w=World(species);
        var terrain=w.Terrain;
        ulong original=PilotTreeMeshes.RootGroundSignature(w);
        Assert.Equal(original,PilotTreeMeshes.RootGroundSignature(w));
        int version=terrain.Version;
        terrain.BeginEdit();
        // Central field is outside the corner/root perimeter. An unrelated
        // global Terrain.Version bump cannot justify remeshing ancient roots.
        int centerI=(int)Math.Round((0-terrain.OriginX)/terrain.Step);
        int centerJ=(int)Math.Round((0-terrain.OriginZ)/terrain.Step);
        for(int j=centerJ;j<=centerJ+1;j++)
        for(int i=centerI;i<=centerI+1;i++)
            terrain.H[terrain.VertexIndex(i,j)]+=.01;
        terrain.Touch();
        Assert.True(terrain.Version>version);
        Assert.Equal(original,PilotTreeMeshes.RootGroundSignature(w));

        // Edit the four terrain vertices interpolated under a real root crest.
        var sample=w.PilotTree!.Roots.First().Samples[5];
        var point=sample.Position;
        int ix=(int)Math.Floor((point.X-terrain.OriginX)/terrain.Step);
        int iz=(int)Math.Floor((point.Z-terrain.OriginZ)/terrain.Step);
        string before=PilotTreeMeshes.BuildRoots(w).DigestHex();
        for(int j=iz;j<=iz+1;j++)
        for(int i=ix;i<=ix+1;i++)
            terrain.H[terrain.VertexIndex(i,j)]+=.015;
        terrain.Touch();
        Assert.NotEqual(original,PilotTreeMeshes.RootGroundSignature(w));
        Assert.NotEqual(before,PilotTreeMeshes.BuildRoots(w).DigestHex());
    }

    [Fact]
    public void NoPilotTreeBuildsNoGeometry()
    {
        var d = TestUtil.FlatDescriptor(); d.PilotTreeId = "none";
        Assert.Equal(0, PilotTreeMeshes.Build(VivariumWorld.CreateBaseline(TestUtil.Content, d)).TriangleCount);
    }
}

