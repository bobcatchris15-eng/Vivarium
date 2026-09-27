using System.Text.RegularExpressions;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[CollectionDefinition("GlobalLog", DisableParallelization = true)]
public class GlobalLogCollection { }

[Trait("Suite", "Bootstrap")]
[Collection("GlobalLog")]
public class BootstrapTests
{
    [Fact] // t-006
    public void VersionServiceReportsAppAndSaveSchemaFromOneSource()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Application);
        Assert.True(AppVersion.SaveSchema >= 1);
        Assert.Contains(AppVersion.Application, AppVersion.Describe());
        // no other file may hard-code the application version (the Godot project is stamped by export.ps1 / checked here)
        var project = File.ReadAllText(Path.Combine(TestUtil.RepoRoot, "game", "project.godot"));
        var m = Regex.Match(project, "config/version=\"([^\"]+)\"");
        Assert.True(m.Success, "project.godot must declare config/version");
        Assert.Equal(AppVersion.Application, m.Groups[1].Value);
    }

    [Fact] // t-007
    public void LoggerRecordsCategoriesAndSurvivesFailingSinks()
    {
        var mem = new MemoryLogSink();
        var bad = new ThrowingSink();
        Log.AddSink(mem); Log.AddSink(bad);
        try
        {
            Log.Info(LogCategory.App, "startup");
            Log.Error(LogCategory.Persistence, "save failed", new IOException("disk full"));
            Log.Error(LogCategory.Content, "content validation failed");
            Log.Fatal(LogCategory.World, "simulation fatal error");
            Log.Info(LogCategory.App, "shutdown");
            var entries = mem.Snapshot();
            Assert.Contains(entries, e => e.Category == LogCategory.Persistence && e.Level == LogLevel.Error && e.Message.Contains("disk full"));
            Assert.Contains(entries, e => e.Level == LogLevel.Fatal && e.Category == LogCategory.World);
            Assert.All(entries, e => Assert.True(e.TimestampUtc.Kind == DateTimeKind.Utc));
            Assert.Contains("[ERROR  ] [Persistence]", entries.First(e => e.Category == LogCategory.Persistence).Format());
        }
        finally { Log.RemoveSink(mem); Log.RemoveSink(bad); }
    }

    [Fact] // t-007
    public void FileSinkWritesToWritableLocation()
    {
        var dir = TestUtil.TempDir();
        var path = Path.Combine(dir, "logs", "vivarium.log");
        using (var sink = new FileLogSink(path))
        {
            Log.AddSink(sink);
            Log.Warn(LogCategory.App, "hello file");
            Log.RemoveSink(sink);
        }
        Assert.Contains("hello file", File.ReadAllText(path));
    }

    [Fact] // t-008
    public void RngStreamsAreReproducibleAndIndependent()
    {
        var a = Rng.Stream(123, "flora"); var b = Rng.Stream(123, "flora");
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());

        var fauna1 = Rng.Stream(123, "fauna");
        var expected = Enumerable.Range(0, 100).Select(_ => fauna1.NextULong()).ToArray();
        var flora = Rng.Stream(123, "flora");
        var fauna2 = Rng.Stream(123, "fauna");
        for (int i = 0; i < 5000; i++) flora.NextULong(); // heavy consumption of an unrelated stream
        Assert.Equal(expected, Enumerable.Range(0, 100).Select(_ => fauna2.NextULong()).ToArray());
        Assert.NotEqual(Rng.Stream(123, "flora").NextULong(), Rng.Stream(124, "flora").NextULong());
        Assert.NotEqual(Rng.Stream(123, "flora").NextULong(), Rng.Stream(123, "fauna").NextULong());
    }

    [Fact] // t-008
    public void RngDistributionsStayInRange()
    {
        var r = Rng.Stream(9, "dist");
        double sum = 0;
        for (int i = 0; i < 20000; i++) { double d = r.NextDouble(); Assert.InRange(d, 0, 1); sum += d; Assert.InRange(r.NextInt(7), 0, 6); }
        Assert.InRange(sum / 20000, 0.48, 0.52);
    }

    [Fact] // t-009
    public void IdsAreUniqueAcross10000EntitiesAndSurviveRoundTrip()
    {
        var alloc = new IdAllocator();
        var kinds = new[] { EntityKind.Rock, EntityKind.Log, EntityKind.Flora, EntityKind.Fauna, EntityKind.Genome };
        var set = new HashSet<ulong>();
        for (int i = 0; i < 12000; i++) Assert.True(set.Add(alloc.Next(kinds[i % kinds.Length]).Value));
        var restored = new IdAllocator { LastSerial = alloc.LastSerial };
        Assert.DoesNotContain(restored.Next(EntityKind.Fauna).Value, set);
        var id = EntityId.Make(EntityKind.Flora, 77);
        Assert.Equal(EntityKind.Flora, id.Kind); Assert.Equal(77UL, id.Serial);
        Assert.Equal("flora-77", id.ToString());
    }

    [Fact] // t-010
    public void ValidContentLoadsDeterministically()
    {
        var a = ContentLoader.Load(TestUtil.ContentSource);
        var b = ContentLoader.Load(TestUtil.ContentSource);
        Assert.Equal(a.ContentDigest, b.ContentDigest);
        Assert.Equal(a.Flora.Select(f => f.Id), b.Flora.Select(f => f.Id));
        Assert.Equal(35, a.Flora.Count);
        Assert.Equal(17, a.Fauna.Count);
        Assert.All(a.Fauna, f =>
        {
            Assert.NotEqual(FaunaAnimationFamily.Still, f.Animation.Family);
            Assert.True(f.Animation.MaxHz >= f.Animation.IdleHz);
            Assert.InRange(f.Animation.DutyFactor, 0.05, 0.95);
        });
        Assert.Equal(7, a.Flora.Count(f => f.PlacementGroup == FloraPlacementGroup.MossLichen));
        Assert.Equal(11, a.Flora.Count(f => f.PlacementGroup == FloraPlacementGroup.Terrestrial));
        Assert.Equal(7, a.Flora.Count(f => f.PlacementGroup == FloraPlacementGroup.WatersideAquatic));
        Assert.Equal(3, a.Flora.Count(f => f.PlacementGroup == FloraPlacementGroup.Decomposer));
        Assert.Equal(7, a.Flora.Count(f => f.PlacementGroup == FloraPlacementGroup.Woody));
        Assert.Empty(a.Warnings);
    }

    [Fact] // t-010
    public void InvalidDefinitionsNameFileAndField()
    {
        var src = new OverlayContentSource(TestUtil.ContentSource);
        var carpet = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "flora", "velvetweave_moss.json"))
            .Replace("\"ratePerDay\": 0.35", "\"ratePerDay\": -3").Replace("\"soil\": 1", "\"loam\": 1")
            .Replace("\"placementGroup\": \"moss_lichen\"", "\"placementGroup\": \"bog_goblin\"");
        src.Set("flora/velvetweave_moss.json", carpet);
        src.Set("tools.json", "{ \"nutrients\": { \"amount\": 0.6 } ");
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src));
        Assert.Contains(ex.Errors, e => e.File == "flora/velvetweave_moss.json" && e.Path == "$.growth.ratePerDay" && e.Message.Contains("out of range"));
        Assert.Contains(ex.Errors, e => e.File == "flora/velvetweave_moss.json" && e.Path == "$.habitat.substrates.loam" && e.Message.Contains("unknown substrate"));
        Assert.Contains(ex.Errors, e => e.File == "flora/velvetweave_moss.json" && e.Path == "$.placementGroup" && e.Message.Contains("expected moss_lichen"));
        Assert.Contains(ex.Errors, e => e.File == "tools.json" && e.Message.Contains("invalid JSON"));
    }

    [Fact] // t-010
    public void MissingIndexedFileAndUnknownFieldsAreReported()
    {
        var src = new OverlayContentSource(TestUtil.ContentSource).Set("fauna/emberglass_swimmer.json", null);
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src));
        Assert.Contains(ex.Errors, e => e.File == "fauna/emberglass_swimmer.json" && e.Message.Contains("does not exist"));

        var src2 = new OverlayContentSource(TestUtil.ContentSource);
        src2.Set("genetics.json", File.ReadAllText(Path.Combine(TestUtil.ContentDir, "genetics.json")).Replace("\"mutationProbability\"", "\"mutationChance\""));
        var ex2 = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src2));
        Assert.Contains(ex2.Errors, e => e.Path == "$.mutationChance" && e.Message.Contains("unknown field"));
    }

    [Fact] // t-011
    public void HeadlessBootAndShutdown()
    {
        var mem = new MemoryLogSink();
        Log.AddSink(mem);
        try
        {
            var content = ContentLoader.Load(TestUtil.ContentSource);
            var world = VivariumWorld.Create(content, content.PresetOrThrow("default"));
            var host = new Time.SimHost(world);
            for (int i = 0; i < 30; i++) host.Advance(1 / 30.0);
            Assert.True(world.Clock.Tick > 0);
            Assert.Empty(world.CheckInvariants());
            Assert.Equal(0, mem.Count(LogLevel.Error));
        }
        finally { Log.RemoveSink(mem); }
    }

    [Fact]
    public void AllPresetsLoadAndBootSuccessfully()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var expectedPresets = new[] { "default", "rocky_rise", "shoreline", "creek", "bayou_marsh", "steep_rocky_slope", "isolated_pond", "oxbow_creek", "deep_inlet" };
        foreach (var id in expectedPresets)
        {
            Assert.True(content.Presets.ContainsKey(id), $"Preset '{id}' should be loaded");
            var preset = content.Presets[id];
            Assert.NotEmpty(preset.Name);
            var world = VivariumWorld.Create(content, preset);
            Assert.Empty(world.CheckInvariants());
        }
    }

    [Fact]
    public void WaterwayPresetsHaveDeepChannelBedsAtBoundaryEdges()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);

        // 1. Deep inlet: bed at the northern edge (Z ~ 6.928) must reach deep into negative elevation (below waterTable 0.0)
        {
            var preset = content.PresetOrThrow("deep_inlet");
            var world = VivariumWorld.Create(content, preset, populate: false);
            var dom = world.Domain;
            // Sample along edge 1 (Z = Apothem = 6.9282)
            double edgeZ = dom.Apothem;
            double minBed = double.PositiveInfinity;
            for (double x = -3.0; x <= 3.0; x += 0.1)
            {
                var p = new Vec2(x, edgeZ - 0.01);
                minBed = Math.Min(minBed, world.Terrain.Height(p));
            }
            Assert.True(minBed <= -0.5, $"Deep inlet bed at boundary edge should be <= -0.5m, got {minBed:F3}m");
        }

        // 2. Oxbow creek: bed at both entry (edge 3) and exit (edge 0) must be carved well below baseHeight
        {
            var preset = content.PresetOrThrow("oxbow_creek");
            var world = VivariumWorld.Create(content, preset, populate: false);
            var dom = world.Domain;
            // Exit along edge 0: normal (0.866, 0.5), crosses near (5.7, 3.1)
            double minExit = double.PositiveInfinity;
            for (double t = 0.3; t <= 0.8; t += 0.05)
            {
                var p = Vec2.Lerp(dom.Vertices[0], dom.Vertices[1], t);
                minExit = Math.Min(minExit, world.Terrain.Height(p - dom.EdgeNormals[0] * 0.02));
            }
            Assert.True(minExit <= 0.05, $"Oxbow creek exit bed at boundary edge should be <= 0.05m, got {minExit:F3}m");

            // Entry along edge 3: normal (-0.866, -0.5), crosses near (-5.9, -3.1)
            double minEntry = double.PositiveInfinity;
            for (double t = 0.3; t <= 0.8; t += 0.05)
            {
                var p = Vec2.Lerp(dom.Vertices[3], dom.Vertices[4], t);
                minEntry = Math.Min(minEntry, world.Terrain.Height(p - dom.EdgeNormals[3] * 0.02));
            }
            Assert.True(minEntry <= 0.05, $"Oxbow creek entry bed at boundary edge should be <= 0.05m, got {minEntry:F3}m");
        }

        // 3. Creek: bed at exit (edge 0) must be carved well below baseHeight
        {
            var preset = content.PresetOrThrow("creek");
            var world = VivariumWorld.Create(content, preset, populate: false);
            var dom = world.Domain;
            double minExit = double.PositiveInfinity;
            for (double t = 0.5; t <= 0.95; t += 0.05)
            {
                var p = Vec2.Lerp(dom.Vertices[0], dom.Vertices[1], t);
                minExit = Math.Min(minExit, world.Terrain.Height(p - dom.EdgeNormals[0] * 0.02));
            }
            Assert.True(minExit <= 0.35, $"Creek exit bed at boundary edge should be <= 0.35m, got {minExit:F3}m");
        }
    }

    private sealed class ThrowingSink : ILogSink { public void Write(in LogEntry e) => throw new IOException("sink broken"); }
}
