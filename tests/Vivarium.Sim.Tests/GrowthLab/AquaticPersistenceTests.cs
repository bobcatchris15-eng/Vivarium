using System.Text;
using System.Text.Json.Nodes;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests.GrowthLab;

public class AquaticPersistenceTests
{
    [Fact]
    public void ReloadRetainsRecruitmentOrdinalAndExactContinuation()
    {
        var world = TestUtil.DefaultWorld(seed: 8181, populate: false);
        world.AquaticSystem.Step(.1, 600);
        world.AquaticSystem.Step(.1, 600);
        var restored = WorldSerializer.Deserialize(world.Content, WorldSerializer.Serialize(world));
        Assert.Equal(2, world.AquaticSystem.StepIndex);
        Assert.Equal(world.AquaticSystem.StepIndex, restored.AquaticSystem.StepIndex);
        Assert.Equal(WorldSerializer.Digest(world), WorldSerializer.Digest(restored));
        world.AquaticSystem.Step(.1, 600);
        restored.AquaticSystem.Step(.1, 600);
        Assert.Equal(WorldSerializer.Digest(world), WorldSerializer.Digest(restored));
    }

    [Theory]
    [InlineData(20, 0)]
    [InlineData(141, 3)]
    public void OlderSavesInferOrdinalFromScheduledTicks(long tick, long expected)
    {
        var world = TestUtil.DefaultWorld(seed: 8181, populate: false);
        world.Clock.Tick = tick;
        var payloads = WorldSerializer.Serialize(world);
        var coverage = JsonNode.Parse(payloads["coverage"])!;
        coverage.AsObject().Remove("AquaticStep");
        payloads["coverage"] = Encoding.UTF8.GetBytes(coverage.ToJsonString());
        var restored = WorldSerializer.Deserialize(world.Content, payloads);
        Assert.Equal(expected, restored.AquaticSystem.StepIndex);
    }

    [Fact]
    public void NegativeOrdinalIsRejected()
    {
        var world = TestUtil.DefaultWorld(seed: 8181, populate: false);
        var payloads = WorldSerializer.Serialize(world);
        var coverage = JsonNode.Parse(payloads["coverage"])!;
        coverage["AquaticStep"] = -1;
        payloads["coverage"] = Encoding.UTF8.GetBytes(coverage.ToJsonString());
        Assert.Throws<InvalidDataException>(() => WorldSerializer.Deserialize(world.Content, payloads));
    }

    [Fact]
    public void ArchiveRoundTripRetainsAquaticOrdinal()
    {
        var world = TestUtil.DefaultWorld(seed: 8181, populate: false);
        world.AquaticSystem.Step(.1, 600);
        string path = Path.Combine(TestUtil.TempDir(), "aquatic.vivsave");
        Assert.True(SaveSystem.Save(world, path).Ok);
        var loaded = SaveSystem.Load(world.Content, path);
        Assert.True(loaded.Ok, loaded.Message);
        Assert.Equal(world.AquaticSystem.StepIndex, loaded.World!.AquaticSystem.StepIndex);
        Assert.Equal(WorldSerializer.Digest(world), WorldSerializer.Digest(loaded.World));
    }

    [Fact]
    public void OlderArchiveRetainsItsCanonicalDigestAndInfersOrdinal()
    {
        var world = TestUtil.DefaultWorld(seed: 8181, populate: false);
        world.Clock.Tick = 141;
        var payloads = WorldSerializer.Serialize(world, includeAquaticStep: false);
        var (manifest, _) = SaveSystem.Snapshot(world);
        manifest.StateDigest = WorldSerializer.DigestOf(payloads);
        manifest.Payloads = payloads.Select(kv => new SavePayloadInfo {
            Name = kv.Key, Bytes = kv.Value.Length, Sha256 = Digest.Sha256Hex(kv.Value)
        }).ToList();
        string path = Path.Combine(TestUtil.TempDir(), "older-aquatic.vivsave");
        Assert.True(SaveSystem.Write(manifest, payloads, path).Ok);
        byte[] before = File.ReadAllBytes(path);
        var loaded = SaveSystem.Load(world.Content, path);
        Assert.True(loaded.Ok, loaded.Message);
        Assert.Equal(3, loaded.World!.AquaticSystem.StepIndex);
        Assert.Equal(manifest.StateDigest, WorldSerializer.DigestOf(WorldSerializer.Serialize(loaded.World, includeAquaticStep: false)));
        Assert.Equal(before, File.ReadAllBytes(path));
        manifest.StateDigest = new string('0', 64);
        string invalidPath = Path.Combine(TestUtil.TempDir(), "invalid-older-aquatic.vivsave");
        Assert.True(SaveSystem.Write(manifest, payloads, invalidPath).Ok);
        var rejected = SaveSystem.Load(world.Content, invalidPath);
        Assert.False(rejected.Ok);
        Assert.Contains("state digest mismatch", rejected.Message);
    }
}
