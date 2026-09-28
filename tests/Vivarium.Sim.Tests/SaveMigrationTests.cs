using System.Text;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Persistence")]
[Collection("GlobalLog")]
public class SaveMigrationTests
{
    private static void Rename(SortedDictionary<string, byte[]> p, string payload, string from, string to) =>
        p[payload] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(p[payload]).Replace($"\"{from}\"", $"\"{to}\""));

    [Fact]
    public void SaveWithRemovedFloraSpeciesDropsThemWithOneWarning()
    {
        var w = TestUtil.FlatWorld(77);
        TestUtil.Condition(w, 0.6, 0.7, 1.0);
        var sp = w.Content.FloraOrThrow("embercrown");
        var keep = w.Content.FloraOrThrow("shadebell");
        w.FloraSystem.Establish(sp, new Vec2(1.5, 0), "fixture", sp.MaxBiomass);
        var dead = w.FloraSystem.Establish(sp, new Vec2(-1.5, 0), "fixture", sp.MaxBiomass);
        w.FloraSystem.Establish(keep, new Vec2(0, 2.0), "fixture", keep.MaxBiomass);
        w.FloraSystem.Kill(dead, "test");
        w.SeedBank.Deposit("embercrown", new Vec2(0.5, 0), 0.08, 7 * SimUnits.Day);
        int keptBefore = w.Flora.Items.Count(f => f.SpeciesId == "shadebell");
        Assert.True(w.DeadFlora.Items.Count(d => d.SpeciesId == "embercrown") > 0);

        var payloads = WorldSerializer.Serialize(w);
        string removed = WorldSerializer.RemovedFloraSpecies.First();
        foreach (var name in new[] { "flora", "dead_flora", "seed_bank", "genetics" }) Rename(payloads, name, "embercrown", removed);

        var sink = new MemoryLogSink();
        Log.AddSink(sink);
        try
        {
            var restored = WorldSerializer.Deserialize(w.Content, payloads);
            Assert.DoesNotContain(restored.Flora.Items, f => f.SpeciesId == removed);
            Assert.DoesNotContain(restored.DeadFlora.Items, d => d.SpeciesId == removed);
            Assert.DoesNotContain(restored.SeedBank.Lots, l => l.SpeciesId == removed);
            Assert.DoesNotContain(restored.Lineage.Ordered(), r => r.SpeciesId == removed);
            Assert.Equal(keptBefore, restored.Flora.Items.Count(f => f.SpeciesId == "shadebell"));
        }
        finally { Log.RemoveSink(sink); }
        Assert.Single(sink.Snapshot(), e => e.Level == LogLevel.Warning && e.Category == LogCategory.Persistence && e.Message.Contains("removed flora"));
    }

    [Fact]
    public void LegacyLargeDiameterSaveStillLoadsButNewWorldsAreCapped()
    {
        Assert.Equal(10, WorldDescriptor.MaxDiameter);
        var d = TestUtil.FlatDescriptor(78);
        d.Diameter = 16;
        Assert.NotEmpty(d.Validate());

        WorldDescriptor.AllowLegacyDiameter = true;
        VivariumWorld big;
        try { big = VivariumWorld.CreateBaseline(TestUtil.Content, d); }
        finally { WorldDescriptor.AllowLegacyDiameter = false; }

        var restored = WorldSerializer.Deserialize(big.Content, WorldSerializer.Serialize(big));
        Assert.Equal(16, restored.Descriptor.Diameter);
        Assert.False(WorldDescriptor.AllowLegacyDiameter);
    }
}
