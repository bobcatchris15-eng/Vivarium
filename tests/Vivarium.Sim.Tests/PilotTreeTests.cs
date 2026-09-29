using System.Text.Json.Nodes;
using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Time;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "PilotTree")]
public class PilotTreeTests
{
    public static IEnumerable<object[]> FormsAndCorners => PilotTreeCatalog.All.SelectMany(def => Enumerable.Range(0, 6).Select(corner => new object[] { def.Id, corner }));

    private static VivariumWorld World(string id, int corner = 0)
    {
        var d = TestUtil.FlatDescriptor(424242);
        d.PilotTreeId = id; d.PilotTreeCorner = corner;
        return VivariumWorld.Create(TestUtil.Content, d, populate: false);
    }

    [Theory]
    [MemberData(nameof(FormsAndCorners))]
    public void EveryFormHasCornerTrunkAndTwoRootsFollowingNeighboringSides(string id, int corner)
    {
        var w = World(id, corner);
        var tree = Assert.IsType<PilotTreeState>(w.PilotTree);
        Assert.Equal(w.Domain.Vertices[corner], tree.CornerPoint);
        Assert.Equal(tree.CornerPoint, tree.Anchor);
        Assert.True(w.Domain.Contains(tree.Anchor));
        var sideRoots = tree.Roots.Where(r => r.AlongSide).ToArray();
        Assert.Equal(2, sideRoots.Length);
        for (int side = 0; side < 2; side++)
        {
            var root = sideRoots[side];
            var dir = tree.SideDirections[side];
            Assert.InRange(dir.Length, 0.999999, 1.000001);
            Assert.True((root.Samples[^1].Position - tree.CornerPoint).Dot(dir) > w.Domain.Radius * 0.7);
            foreach (var sample in root.Samples.Skip(2).SkipLast(2))
            {
                Assert.True(w.Domain.Contains(sample.Position));
                Assert.True(tree.BlocksDisc(sample.Position));
                Assert.True(w.GroundHeight(sample.Position) > w.Terrain.Height(sample.Position));
                Assert.Equal(Substrate.Wood, w.SubstrateAt(sample.Position));
            }
        }
        Assert.Empty(w.CheckInvariants());
    }

    [Fact]
    public void RandomResolutionAndPersistedTerrainRemainDeterministic()
    {
        var d = TestUtil.FlatDescriptor(721);
        d.PilotTreeId = "random";
        var a = VivariumWorld.Create(TestUtil.Content, d, populate: false);
        var b = VivariumWorld.Create(TestUtil.Content, d, populate: false);
        Assert.NotNull(a.PilotTree);
        Assert.Equal(TestUtil.Digest(a), TestUtil.Digest(b));
        var restored = WorldSerializer.Deserialize(TestUtil.Content, WorldSerializer.Serialize(a));
        Assert.Equal(a.PilotTree!.Seed, restored.PilotTree!.Seed);
        Assert.Equal(a.PilotTree.Corner, restored.PilotTree.Corner);
        Assert.Equal(a.Terrain.DigestHex(), restored.Terrain.DigestHex());
        Assert.Equal(TestUtil.Digest(a), TestUtil.Digest(restored));
        a.Step(180); restored.Step(180);
        Assert.Equal(TestUtil.Digest(a), TestUtil.Digest(restored));
    }

    [Fact]
    public void RootsShapeHydrologyBedAndRejectPropOrWoodyEstablishment()
    {
        var w = World("basinwarden");
        var empty = World("none");
        var p = w.PilotTree!.Roots.First(r => r.AlongSide).Samples[8].Position;
        Assert.True(w.Terrain.Height(p) > empty.Terrain.Height(p) + 0.015);
        Assert.NotNull(w.Placement.ValidateRock(p, 0.10, 0.10));
        Assert.NotNull(w.Placement.ValidateLog(p, 0, 0.25, 0.04));
        var woody = TestUtil.Content.Flora.First(sp => sp.Woody != null);
        Assert.True(w.FloraSystem.Suitability(woody, p).HardRefused);
    }

    [Fact]
    public void SpatialLightMoistureAndLitterAreBoundedAndDoNotCompoundOnRefresh()
    {
        var w = World("gloomspire");
        var tree = w.PilotTree!;
        var near = w.Grid.DomainCells.OrderBy(c => Vec2.Distance(w.Grid.CellCenter(c), tree.CornerPoint)).Skip(5).First();
        var far = w.Grid.DomainCells.OrderByDescending(c => Vec2.Distance(w.Grid.CellCenter(c), tree.CornerPoint)).First();
        Assert.True(w.Fields.Light[near] < w.Fields.Light[far]);
        double[] before = w.Fields.Light.Values.ToArray();
        w.Fields.MarkLightStale(); w.RefreshDerived();
        Assert.Equal(before, w.Fields.Light.Values);
        Assert.True(w.Litter.DetritusAt(near) > w.Litter.DetritusAt(far));
        tree.CoupleHabitat(w, SimUnits.Day * 1000);
        Assert.InRange(w.Fields.Moisture[near], 0, 1);
        Assert.Contains(w.Grid.DomainCells, c => w.Fields.Moisture[c] > w.Content.Ecology.MoistureDryBaseline + 0.03);
        for (int i = 0; i < 50; i++) tree.DepositLitter(w, SimUnits.Day * 100);
        Assert.True(w.Litter.AllFinite());
        Assert.All(w.Grid.DomainCells, c => Assert.InRange(w.Litter.DetritusAt(c), 0, w.Content.Ecology.DetritusMax * 1.5 + 1e-9));
    }

    [Fact]
    public void TreeIsAnOpaqueCameraObstacleAndRootsFollowSculptedGround()
    {
        var w = World("emberpillar");
        var tree = w.PilotTree!;
        var radial = tree.CornerPoint.Normalized();
        var outside = Vec3.FromXZ(tree.Anchor + radial * (tree.Def.TrunkRadius + 0.5), tree.BaseHeight + 2);
        var desired = Vec3.FromXZ(tree.Anchor, tree.BaseHeight + 2);
        var resolved = CameraCollision.Resolve(w, outside, desired);
        Assert.False(tree.ContainsVolume(resolved));
        Assert.True(Vec2.Distance(resolved.XZ, tree.Anchor) >= tree.Def.TrunkRadius * 0.90);
        Assert.True(double.IsFinite(Selection.RayOpaque(w, outside, (desired - outside).Normalized())));
        var sample = tree.Roots.First(r => r.AlongSide).Samples[9];
        double old = w.GroundHeight(sample.Position);
        var delta = Enumerable.Repeat(0.1, w.Terrain.H.Length).ToArray();
        w.Terrain.ApplyDelta(delta);
        Assert.InRange(w.GroundHeight(sample.Position) - old, 0.099999, 0.100001);
        var restored = WorldSerializer.Deserialize(TestUtil.Content, WorldSerializer.Serialize(w));
        Assert.Equal(w.GroundHeight(sample.Position), restored.GroundHeight(sample.Position), 8);
    }

    [Fact]
    public void MissingDescriptorPropertyDoesNotInventTreeInLegacyWorldPayload()
    {
        var w = World("none");
        var payloads = WorldSerializer.Serialize(w);
        var node = JsonNode.Parse(payloads["world"])!;
        node["Descriptor"]!.AsObject().Remove("PilotTreeId");
        node["Descriptor"]!.AsObject().Remove("PilotTreeCorner");
        payloads["world"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node);
        Assert.Null(WorldSerializer.Deserialize(TestUtil.Content, payloads).PilotTree);
    }

    [Fact]
    public void GenuineSchemaTwoSaveMigratesWithoutAddingTree()
    {
        string path = Path.Combine(TestUtil.RepoRoot, "tests", "Vivarium.Sim.Tests", "Fixtures", "pilot-legacy-schema2.vivsave");
        var result = SaveSystem.Load(TestUtil.Content, path);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(SaveCompatibility.Migratable, result.Compatibility);
        Assert.Null(result.World!.PilotTree);
        Assert.Equal("none", result.World.Descriptor.PilotTreeId);
        Assert.Equal(424242UL, result.World.Seed);
        Assert.NotEmpty(result.Manifest!.AppliedMigrations);
        Assert.Empty(result.World.CheckInvariants());
    }
    [Fact]
    public void PilotWoodSupportsLogHostsAndShrubsHaveNoCarryingCap()
    {
        var w=World("gloomspire");var tree=w.PilotTree!;
        var sample=tree.Roots.First(p=>p.AlongSide).Samples[9];
        var inward=(-sample.Position).Normalized();
        var p=sample.Position+inward*.05;
        Assert.Equal(Substrate.Wood,w.SubstrateAt(p));
        var hit=w.FloraSystem.CheckHostMaterial(Vec3.FromXZ(p,w.GroundHeight(p)+.01),.3,new HashSet<string>{"log"});
        Assert.True(hit.HasMaterial);
        Assert.Equal("log",hit.HostType);
        Assert.Equal(int.MaxValue,w.FloraSystem.WoodyPopulationCap(Vivarium.Sim.Content.WoodyLayer.Shrub));
        foreach(var id in new[]{"kinkcane","hookthicket"})
            Assert.Equal(Vivarium.Sim.Content.WoodyLayer.Shrub,w.Content.FloraOrThrow(id).Woody!.Layer);
        Assert.DoesNotContain(w.Content.Flora,sp=>sp.Woody?.Layer==Vivarium.Sim.Content.WoodyLayer.Tree);
    }
}
