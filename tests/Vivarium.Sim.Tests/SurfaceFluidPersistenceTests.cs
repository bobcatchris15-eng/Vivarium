using System.IO.Compression;
using System.Text.Json;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
namespace Vivarium.Sim.Tests;
[Trait("Suite", "Persistence")]
public class SurfaceFluidPersistenceTests
{
    private static string LegacyArchive(bool particles, bool corrupt = false)
    {
        var w = TestUtil.FlatWorld();
        var (manifest, payloads) = SaveSystem.Snapshot(w); manifest.FormatVersion = 3;
        var water = JsonSerializer.Deserialize<WaterPayload>(payloads["water"], WorldSerializer.Json)!;
        water.Fluid = null; water.FormatVersion = particles ? 3 : 2;
        if (particles) water.Liquid = new([new(new(0, .8, 0), new(.2, 0, 0), .0003, 0)], new() { { 0, .0000007 } }, 3, .004);
        else { var depths = new double[w.Grid.DomainCells.Length]; depths[Array.IndexOf(w.Grid.DomainCells, w.Grid.CellAt(Vec2.Zero))] = .012; water.Depth = WorldSerializer.Pack(depths); }
        payloads["water"] = JsonSerializer.SerializeToUtf8Bytes(water, WorldSerializer.Json);
        manifest.Payloads = payloads.Select(k => new SavePayloadInfo { Name = k.Key, Bytes = k.Value.Length, Sha256 = Digest.Sha256Hex(k.Value) }).ToList();
        if (corrupt) payloads["water"][0] = 0;
        string path = Path.Combine(TestUtil.TempDir(), "legacy.vivsave"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var entry = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(entry, manifest);
        foreach (var p in payloads) { using var entry = zip.CreateEntry(p.Key + ".json").Open(); entry.Write(p.Value); }
        return path;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldArchivesMigrateWithoutChangingTheOriginal(bool particles)
    {
        string path = LegacyArchive(particles); var bytes = File.ReadAllBytes(path); var result = SaveSystem.Load(TestUtil.Content, path); Assert.True(result.Ok, result.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(SaveCompatibility.Migratable, result.Compatibility);
        Assert.Equal(particles ? .0003007 : .012 * .25 * .25, result.World!.Liquid.Volume, 11);
        Assert.Equal(-.8, result.World.Water.WaterTable, 12);
        var target = Path.Combine(TestUtil.TempDir(), "new.vivsave"); Assert.True(SaveSystem.Save(result.World, target).Ok); Assert.True(SaveSystem.Load(TestUtil.Content, target).Ok);
    }
    [Fact]
    public void FailedMigrationRejectsCorruptInputAndLeavesItUntouched()
    { string path = LegacyArchive(false, true); var bytes = File.ReadAllBytes(path); var result = SaveSystem.Load(TestUtil.Content, path); Assert.False(result.Ok); Assert.Contains("checksum", result.Message); Assert.Equal(bytes, File.ReadAllBytes(path)); }
    [Fact]
    public void BackloggedSourceHistoryAndItsSaveContinueAtTheOriginalRates()
    {
        var w = TestUtil.FlatWorld(); var source = new Spring { Id = w.Ids.Next(EntityKind.Spring), Discharge = .00001 }; w.Water.Springs.Add(source);
        w.Liquid.Advance(1); source.Discharge = .00003; w.Liquid.Advance(.5);
        string path = Path.Combine(TestUtil.TempDir(), "queued.vivsave"); Assert.True(SaveSystem.Save(w, path).Ok); var load = SaveSystem.Load(TestUtil.Content, path); Assert.True(load.Ok, load.Message);
        for (int i = 0; i < 30; i++) { w.Liquid.Advance(0); load.World!.Liquid.Advance(0); }
        Assert.Equal(.00001 + .5 * .00003, w.Water.Budget.SpringInflow, 10); Assert.Equal(w.Water.Budget.SpringInflow, load.World!.Water.Budget.SpringInflow, 12);
        var a = WorldSerializer.Serialize(w); var b = WorldSerializer.Serialize(load.World);
        foreach (var key in a.Keys.Where(k => !a[k].SequenceEqual(b[k])))
        { File.WriteAllBytes(Path.Combine(TestUtil.RepoRoot, "build/surface-water-replacement/continued-original-" + key + ".json"), a[key]); File.WriteAllBytes(Path.Combine(TestUtil.RepoRoot, "build/surface-water-replacement/continued-reload-" + key + ".json"), b[key]); }
        Assert.True(a.All(k => k.Value.SequenceEqual(b[k.Key])), string.Join(",", a.Keys.Where(k => !a[k].SequenceEqual(b[k]))));
    }
    [Fact]
    public void SubstepRateEditsIntegrateBothIntervalsInsteadOfReplacingTheFirst()
    {
        var w = TestUtil.FlatWorld(); var source = new Spring { Id = w.Ids.Next(EntityKind.Spring), Discharge = .00001 }; w.Water.Springs.Add(source);
        w.Liquid.Advance(.007); source.Discharge = .00004; w.Liquid.Advance(.013);
        source.Discharge = 0; w.Liquid.Advance(.014);
        for (int i = 0; i < 5; i++) w.Liquid.Advance(0);
        Assert.Equal(.007 * .00001 + .013 * .00004, w.Water.Budget.SpringInflow, 13);
        Assert.InRange(w.Liquid.BacklogSeconds, 0, .001);
    }

}
