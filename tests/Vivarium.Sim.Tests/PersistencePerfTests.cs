using System.IO.Compression;
using System.Text.Json.Nodes;
using Vivarium.Sim.Core;
using Vivarium.Sim.Diagnostics;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Persistence")]
public class PersistenceTests
{
    private static VivariumWorld Running(int ticks = 2000)
    {
        var w = TestUtil.DefaultWorld();
        w.Step(ticks);
        return w;
    }

    [Fact] // t-153
    public void ManifestClassifiesVersionsBeforeTouchingState()
    {
        Assert.Equal(SaveCompatibility.Current, SaveSystem.Classify(AppVersion.SaveSchema));
        Assert.Equal(SaveCompatibility.Migratable, SaveSystem.Classify(AppVersion.SaveSchema - 1));
        Assert.Equal(SaveCompatibility.UnsupportedNewer, SaveSystem.Classify(AppVersion.SaveSchema + 1));
        var w = Running(100);
        var path = Path.Combine(TestUtil.TempDir(), "m.vivsave");
        Assert.True(SaveSystem.Save(w, path).Ok);
        var (m, compat, _) = SaveSystem.Inspect(path);
        Assert.Equal(SaveCompatibility.Current, compat);
        Assert.Equal(AppVersion.Application, m!.AppVersion);
        Assert.Equal(w.Seed, m.Seed);
        Assert.Equal(WorldSerializer.PayloadOrder.OrderBy(x => x, StringComparer.Ordinal), m.Payloads.Select(p => p.Name));
        // a newer save is refused before any world is built
        Rewrite(path, "manifest.json", n => n["FormatVersion"] = AppVersion.SaveSchema + 5);
        var r = SaveSystem.Load(w.Content, path);
        Assert.False(r.Ok);
        Assert.Equal(SaveCompatibility.UnsupportedNewer, r.Compatibility);
        Assert.Null(r.World);
    }

    [Fact] // t-154 … t-158
    public void EverySubsystemRoundTripsExactly()
    {
        var w = Running();
        var path = Path.Combine(TestUtil.TempDir(), "rt.vivsave");
        var before = WorldSerializer.SubsystemDigests(w);
        Assert.True(SaveSystem.Save(w, path).Ok);
        var r = SaveSystem.Load(w.Content, path);
        Assert.True(r.Ok, r.Message);
        var after = WorldSerializer.SubsystemDigests(r.World!);
        foreach (var k in before.Keys) Assert.True(before[k] == after[k], $"payload '{k}' changed across save/load");
        Assert.Equal(w.Terrain.DigestHex(), r.World!.Terrain.DigestHex());         // procedural geometry reconstructs exactly
        Assert.Equal(w.Fields.Nutrients.DigestHex(), r.World.Fields.Nutrients.DigestHex());
        Assert.Equal(w.Fields.Light.DigestHex(), r.World.Fields.Light.DigestHex());
        Assert.Equal(w.Flora.Items.Select(f => f.Id), r.World.Flora.Items.Select(f => f.Id));
        Assert.Equal(w.Fauna.Items.Select(f => (f.Id, f.Energy, f.Age, f.Stage)), r.World.Fauna.Items.Select(f => (f.Id, f.Energy, f.Age, f.Stage)));
        Assert.Equal(w.Lineage.Count, r.World.Lineage.Count);
        Assert.Equal(w.Ids.LastSerial, r.World.Ids.LastSerial);
    }

    [Fact] // t-159
    public void FailedWriteNeverDestroysThePreviousSave()
    {
        var w = Running(200);
        var path = Path.Combine(TestUtil.TempDir(), "atomic.vivsave");
        Assert.True(SaveSystem.Save(w, path).Ok);
        var good = File.ReadAllBytes(path);
        w.Step(100);
        SaveSystem.FaultInjectionBeforeCommit = _ => throw new IOException("simulated disk failure");
        try
        {
            var r = SaveSystem.Save(w, path);
            Assert.False(r.Ok);
            Assert.Contains("simulated disk failure", r.Message);
        }
        finally { SaveSystem.FaultInjectionBeforeCommit = null; }
        Assert.Equal(good, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(SaveSystem.Load(w.Content, path).Ok);
    }

    [Fact] // t-160
    public void CorruptSavesFailCleanlyWithoutTouchingTheRunningWorld()
    {
        var live = Running(300);
        string liveDigest = TestUtil.Digest(live);
        var dir = TestUtil.TempDir();
        var path = Path.Combine(dir, "c.vivsave");
        Assert.True(SaveSystem.Save(live, path).Ok);

        var truncated = Path.Combine(dir, "truncated.vivsave");
        File.WriteAllBytes(truncated, File.ReadAllBytes(path).Take(500).ToArray());
        Assert.False(SaveSystem.Load(live.Content, truncated).Ok);

        var badRef = Path.Combine(dir, "badref.vivsave"); File.Copy(path, badRef);
        Rewrite(badRef, "fauna.json", n => n["Items"]![0]!["GenomeId"] = 123456789);
        var faunaSha = Sha(badRef, "fauna.json");
        Rewrite(badRef, "manifest.json", n => { n["StateDigest"] = ""; foreach (var p in n["Payloads"]!.AsArray()) if ((string)p!["Name"]! == "fauna") p["Sha256"] = faunaSha; });
        var r = SaveSystem.Load(live.Content, badRef);
        Assert.False(r.Ok);
        Assert.Contains("missing genome", r.Message);

        var tampered = Path.Combine(dir, "tampered.vivsave"); File.Copy(path, tampered);
        Rewrite(tampered, "flora.json", n => n["Items"]![0]!["Biomass"] = 99.0);
        Assert.Contains("checksum", SaveSystem.Load(live.Content, tampered).Message);

        var unknownSpecies = Path.Combine(dir, "species.vivsave"); File.Copy(path, unknownSpecies);
        Rewrite(unknownSpecies, "flora.json", n => n["Items"]![0]!["SpeciesId"] = "dragon_moss");
        var floraSha = Sha(unknownSpecies, "flora.json");
        Rewrite(unknownSpecies, "manifest.json", n => { n["StateDigest"] = ""; foreach (var p in n["Payloads"]!.AsArray()) if ((string)p!["Name"]! == "flora") p["Sha256"] = floraSha; });
        Assert.Contains("dragon_moss", SaveSystem.Load(live.Content, unknownSpecies).Message);

        Assert.Equal(liveDigest, TestUtil.Digest(live));
    }

    [Fact] // t-161
    public void PreviousVersionFixtureMigrates()
    {
        var w = Running(400);
        var path = Path.Combine(TestUtil.TempDir(), "v0.vivsave");
        Assert.True(SaveSystem.Save(w, path).Ok);
        // synthesise a v0 file: clock as seconds, fauna energy as percent, format version 0
        long tick = w.Clock.Tick;
        Rewrite(path, "world.json", n => { n.AsObject().Remove("Tick"); n["SimSeconds"] = tick * 10.0; });
        Rewrite(path, "fauna.json", n => { foreach (var f in n["Items"]!.AsArray()) { var e = (double)f!["Energy"]!; f.AsObject().Remove("Energy"); f["EnergyPercent"] = e * 100; } });
        Rewrite(path, "manifest.json", n => n["FormatVersion"] = 0);
        Assert.Equal(SaveCompatibility.Migratable, SaveSystem.Inspect(path).Compat);
        var r = SaveSystem.Load(w.Content, path);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(AppVersion.SaveSchema, r.Manifest!.FormatVersion);
        Assert.Single(r.Manifest.AppliedMigrations);
        Assert.Equal(tick, r.World!.Clock.Tick);
        Assert.Equal(w.Fauna.Items.Select(f => Math.Round(f.Energy, 9)), r.World.Fauna.Items.Select(f => Math.Round(f.Energy, 9)));
    }

    [Fact] // t-162
    public void AutosaveNeverOverlapsAndDoesNotBlockSimulation()
    {
        var w = Running(50);
        var path = Path.Combine(TestUtil.TempDir(), "auto.vivsave");
        var gate = new ManualResetEventSlim(false);
        var auto = new AutosaveController(path) { IntervalSeconds = 1 };
        auto.Writer = (m, p, file) => { gate.Wait(TimeSpan.FromSeconds(10)); return SaveSystem.Write(m, p, file); };
        Assert.True(auto.Tick(w, 1.5));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) { Assert.False(auto.Tick(w, 1.5)); w.Step(5); }   // writer still blocked: no overlap
        Assert.True(sw.ElapsedMilliseconds < 5000, "simulation kept running while the write was pending");
        Assert.Equal(1, auto.Started);
        Assert.True(auto.SkippedBecauseBusy >= 1);
        gate.Set();
        auto.Flush(TimeSpan.FromSeconds(10));
        Assert.True(auto.LastResult!.Ok);
        Assert.True(SaveSystem.Load(w.Content, path).Ok);
        Assert.True(auto.Tick(w, 1.5));
        auto.Flush(TimeSpan.FromSeconds(10));
        Assert.Equal(2, auto.Started);
    }

    [Fact] // t-164
    public void RunningEcologyRoundTripsToIdenticalDigest()
    {
        var w = Running(8640 * 2);
        var path = Path.Combine(TestUtil.TempDir(), "eco.vivsave");
        string pre = TestUtil.Digest(w);
        Assert.True(SaveSystem.Save(w, path).Ok);
        var r = SaveSystem.Load(w.Content, path);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(pre, TestUtil.Digest(r.World!));
    }

    // ------------------------------------------------------------------ helpers
    internal static void Rewrite(string zipPath, string entry, Action<JsonNode> edit)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var e = zip.GetEntry(entry)!;
        JsonNode node;
        using (var s = e.Open()) node = JsonNode.Parse(s)!;
        edit(node);
        e.Delete();
        var ne = zip.CreateEntry(entry);
        using var w = ne.Open();
        var bytes = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
        w.Write(bytes);
    }

    private static string Sha(string zipPath, string entry)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var s = zip.GetEntry(entry)!.Open();
        using var ms = new MemoryStream(); s.CopyTo(ms);
        return Digest.Sha256Hex(ms.ToArray());
    }
}

[Trait("Suite", "Perf")]
public class PerfTests
{
    [Fact] // t-165
    public void TimingIsReportedPerSubsystemWithoutChangingBehaviour()
    {
        var a = TestUtil.DefaultWorld(); var b = TestUtil.DefaultWorld();
        var timer = new SubsystemTimer(); timer.Attach(a.Scheduler);
        a.Step(600); b.Step(600);
        timer.Measure("persistence", () => SaveSystem.Snapshot(a));
        timer.Measure("rendering-support", () => Geometry.WaterMesh.Build(a));
        var names = timer.Entries.Select(e => e.Name).ToHashSet();
        foreach (var n in new[] { "hydrology", "flora", "fauna", "ecology", "persistence", "rendering-support" }) Assert.Contains(n, names);
        Assert.Contains("hydrology", timer.Report());
        Assert.Equal(TestUtil.Digest(a), TestUtil.Digest(b));
    }

    [Fact] // t-166
    public void CountersDetectMismatches()
    {
        var w = TestUtil.DefaultWorld();
        w.Fauna.RebuildIndex();
        var ok = Counters.Collect(w, visibleFlora: w.Flora.Count, visibleFauna: w.Fauna.Count, lodHigh: w.Fauna.Count);
        Assert.Empty(ok.Mismatches());
        var bad = Counters.Collect(w, visibleFauna: w.Fauna.Count + 3, lodHigh: 1, lodLow: 1);
        Assert.NotEmpty(bad.Mismatches());
        w.Fauna.Items.RemoveAt(0); // simulate a desynchronised collection
        Assert.Contains(Counters.Collect(w).Mismatches(), m => m.Contains("spatial index"));
    }

    [Fact] // t-167
    public void FloraNeighbourQueriesStayLocalAtScale()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, 0.7, 1.0);
        var sp = w.Content.FloraOrThrow("carpet_moss");
        var rng = Rng.Stream(4, "scale");
        for (int i = 0; i < 4000; i++) w.FloraSystem.Establish(sp, w.Domain.ClampInside(new Vec2(rng.Range(-5, 5), rng.Range(-4.3, 4.3)), 0.1), "t");
        int examined = w.Flora.Index.CandidatesExamined(Vec2.Zero, sp.CompetitionRadius);
        Assert.True(examined < 400, $"a local query examined {examined} of {w.Flora.Count} individuals");
        // identical ecological result with a brute-force scan
        var fast = w.FloraSystem.Crowding(sp, Vec2.Zero, EntityId.None);
        double brute = w.Flora.Items.Where(f => Vec2.Distance(f.Position, Vec2.Zero) <= sp.CompetitionRadius).Sum(f => w.Content.FloraInteractions.CompetitionWeight(sp.Id, f.SpeciesId) * f.BiomassFraction(sp));
        Assert.Equal(brute, fast, 9);
    }

    [Fact] // t-168
    public void FaunaNeighbourQueriesAvoidAllToAll()
    {
        var w = FaunaFixtures.PondWorld();
        var rng = Rng.Stream(8, "scale");
        for (int i = 0; i < 2000; i++) w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), new Vec2(rng.Range(0.5, 4), rng.Range(-3, 3)));
        w.Fauna.RebuildIndex();
        Assert.True(w.Fauna.Index.CandidatesExamined(new Vec2(2, 0), 0.4) < 300);
        string a = Run(), b = Run();
        Assert.Equal(a, b);
        string Run()
        {
            var x = FaunaFixtures.PondWorld();
            var r = Rng.Stream(8, "scale");
            for (int i = 0; i < 600; i++) x.FaunaSystem.CreateFounder(FaunaFixtures.Sp("microminnow"), FaunaFixtures.Pond + new Vec2(r.Range(-1, 1), r.Range(-1, 1)));
            for (int i = 0; i < 50; i++) { x.FaunaSystem.StepBehaviour(20); x.Clock.Tick += 2; }
            return Persistence.WorldSerializer.Text(WorldSerializer.Serialize(x)["fauna"]);
        }
    }

    [Fact] // t-170
    public void OverBudgetWorkProducesActionableWarning()
    {
        var w = TestUtil.FlatWorld();
        var budget = new WorkloadBudget(w) { SystemBudgetMs = 1 };
        w.Scheduler.Register("test.expensive", 1, 99, _ => Thread.Sleep(5));
        w.Step(3);
        Assert.NotEmpty(budget.Warnings);
        var warn = budget.Warnings[0];
        Assert.Equal("test.expensive", warn.System);
        Assert.Contains("test.expensive", warn.ToString());
        Assert.Contains("fauna", warn.ToString());
    }

    [Fact] // t-171
    [Trait("Speed", "Slow")]
    public void SustainedAcceleratedRunStaysBounded()
    {
        var w = TestUtil.DefaultWorld(bio: TestUtil.ShippedBio);   // as shipped: 8 biological weeks
        long mem0 = GC.GetTotalMemory(true);
        var counts = new List<int>();
        var budget = new WorkloadBudget(w) { SystemBudgetMs = 250 };
        for (int week = 0; week < 8; week++)
        {
            w.Step(TestUtil.TicksPerBioDay() * 7);
            Assert.Empty(w.CheckInvariants());
            counts.Add(w.Fauna.Count + w.Flora.Count);
        }
        long mem1 = GC.GetTotalMemory(true);
        Assert.True(counts[^1] < 6000, $"entity count {counts[^1]}");
        foreach (var sp in w.Content.Fauna) Assert.True(w.Fauna.CountOf(sp.Id) <= sp.PopulationCap);
        Assert.True(w.Lineage.Count < 20000 && w.Genomes.Count < 20000, $"lineage {w.Lineage.Count}, genomes {w.Genomes.Count}");
        Assert.True(mem1 - mem0 < 300_000_000, $"memory grew by {(mem1 - mem0) / 1e6:0} MB");
        Assert.Empty(budget.Warnings);
    }
}
