using System.Diagnostics;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.World;

// Vivarium developer CLI: headless world runs for tuning, soak and diagnostics.
//   dotnet run --project src/Vivarium.Cli -- soak [--days N] [--preset id] [--seed N] [--report-days N]
//   dotnet run --project src/Vivarium.Cli -- schedule
//   dotnet run --project src/Vivarium.Cli -- validate

var argList = args.ToList();
string cmd = argList.Count > 0 ? argList[0] : "soak";
string Opt(string name, string def) { int i = argList.IndexOf("--" + name); return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : def; }

Log.AddSink(new ConsoleSink());
Log.MinimumLevel = LogLevel.Warning;

string root = FindRepoRoot();
var content = ContentLoader.Load(new DirectoryContentSource(Path.Combine(root, "game", "content")));

switch (cmd)
{
    case "validate":
        Console.WriteLine($"content OK: {content.Flora.Count} flora, {content.Fauna.Count} fauna, {content.Presets.Count} presets, digest {content.ContentDigest[..16]}");
        return 0;
    case "schedule":
    {
        var w = VivariumWorld.Create(content, content.PresetOrThrow("default"));
        Console.WriteLine(w.Scheduler.DescribeSchedule());
        return 0;
    }
    case "soak":
    {
        var d = content.PresetOrThrow(Opt("preset", "default"));
        if (Opt("seed", "") is { Length: > 0 } s) d.Seed = ulong.Parse(s);
        double days = double.Parse(Opt("days", "28"));
        double report = double.Parse(Opt("report-days", "2"));
        var sw = Stopwatch.StartNew();
        var w = VivariumWorld.Create(content, d);
        Console.WriteLine($"created in {sw.ElapsedMilliseconds} ms: props {w.Props.Count}, flora {w.Flora.Count}, fauna {w.Fauna.Count}, wet {EcosystemStatistics.Compute(w).WetFraction:P1}");
        Print(w);
        long ticksPerReport = (long)(report * 86400 / 10);
        double next = report;
        while (w.Clock.SimDays < days - 1e-9)
        {
            var t0 = sw.Elapsed;
            w.Step(ticksPerReport);
            var inv = w.CheckInvariants();
            Console.WriteLine($"--- day {w.Clock.SimDays:0.0}  ({(sw.Elapsed - t0).TotalMilliseconds / ticksPerReport * 1000:0} µs/tick)");
            Print(w);
            if (inv.Count > 0) { Console.WriteLine("INVARIANT FAILURES:\n  " + string.Join("\n  ", inv.Take(10))); return 2; }
        }
        Console.WriteLine($"total {sw.Elapsed.TotalSeconds:0.0} s wall for {days} sim days");
        foreach (var sys in w.Scheduler.Systems) Console.WriteLine($"  {sys.Name,-20} runs {sys.Runs,7}  total {sys.TotalMs,9:0} ms  mean {sys.TotalMs / Math.Max(1, sys.Runs):0.000} ms  max {sys.MaxMs:0.0} ms");
        return 0;
    }
    case "water":
    {
        var w = VivariumWorld.Create(content, content.PresetOrThrow(Opt("preset", "default")), populate: false);
        var g = w.Grid;
        int wet = 0, wetBoundary = 0; double minEdge = double.PositiveInfinity;
        foreach (int c in g.DomainCells)
        {
            if (!w.Water.IsWet(c)) continue;
            wet++;
            if (g.IsBoundaryCell[c]) wetBoundary++;
            minEdge = Math.Min(minEdge, -w.Domain.SignedDistance(g.CellCenter(c)));
        }
        Console.WriteLine($"wet cells {wet}, wet boundary cells {wetBoundary}, closest wet cell centre to edge {minEdge:0.000} m, volume {w.Water.Volume():0.00} m3");
        for (int j = g.Nz - 1; j >= 0; j -= 2)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < g.Nx; i++)
            {
                int c = g.Index(i, j);
                sb.Append(!g.InDomain(c) ? ' ' : w.Water.Depth[c] > 0.1 ? '#' : w.Water.IsWet(c) ? '~' : w.Water.Bed[c] < 0.1 ? '.' : '-');
            }
            Console.WriteLine(sb);
        }
        return 0;
    }
    default:
        Console.Error.WriteLine($"unknown command {cmd}");
        return 1;
}

static void Print(VivariumWorld w)
{
    var s = EcosystemStatistics.Compute(w);
    Console.WriteLine($"  env: moisture {s.MeanMoisture:0.00} nutrients {s.MeanNutrients:0.000} detritus {s.TotalDetritus:0.0} water {s.WaterVolume:0.00} m3 wet {s.WetFraction:P1} light {s.MeanLight:0.00} lineage {s.LineageRecords} genomes {s.Genomes}");
    Console.WriteLine("  flora: " + string.Join("  ", s.Flora.Select(f => $"{f.Id}={f.Count}({f.Biomass:0.0})")));
    Console.WriteLine("  fauna: " + string.Join("  ", s.Fauna.Select(f => $"{f.Id}={f.Count} e{f.MeanEnergy:0.00} g{f.MaxGeneration} b{f.Births}/d{f.Deaths} {f.MeanBodySizeMm:0.0}mm")));
    var causes = w.Tally.Species.Where(kv => kv.Value.DeathsByCause.Count > 0).Select(kv => kv.Key + ":" + string.Join(",", kv.Value.DeathsByCause.Select(c => $"{c.Key}={c.Value}")));
    Console.WriteLine("  deaths: " + string.Join(" | ", causes));
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Vivarium.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

sealed class ConsoleSink : ILogSink
{
    public void Write(in LogEntry e) => Console.Error.WriteLine(e.Format());
}
