using System.Diagnostics;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.World;

// Vivarium developer CLI: headless world runs for tuning, soak and diagnostics.
//   dotnet run --project src/Vivarium.Cli -- soak [--days N] [--preset id] [--seed N] [--report-days N] [--save-days 7,30,90 --save-dir DIR]
//                    [--scenario default|none|low|medium|high]   (deterministic fauna load; default = preset starters)
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
    case "inspect-save":
    {
        string savePath = Opt("file", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vivarium", "saves", "autosave.vivsave"));
        Console.WriteLine($"Loading {savePath}...");
        var save = Vivarium.Sim.Persistence.SaveSystem.Load(content, savePath);
        if (!save.Ok) { Console.WriteLine("Failed to load: " + save.Message); return 1; }
        var w = save.World!;
        Console.WriteLine($"World loaded: day {w.Clock.BioDays:0.0}, tick {w.Clock.Tick}, flora {w.Flora.Count}, fauna {w.Fauna.Count}");
        Console.WriteLine($"Coverage mat tiles: {w.Coverage.Mat.TileCount}, crust tiles: {w.Coverage.Crust.TileCount}");
        int matCells = 0, crustCells = 0;
        foreach (var t in w.Coverage.Mat.Tiles) matCells += t.Occ.Count(o => o != 0);
        foreach (var t in w.Coverage.Crust.Tiles) crustCells += t.Occ.Count(o => o != 0);
        Console.WriteLine($"Occupied cells: mat {matCells}, crust {crustCells}");
        foreach (var sp in content.Flora)
        {
            var m = Vivarium.Sim.Geometry.OrganismMeshes.Flora(sp, 0);
            int count = w.Flora.Items.Count(f => f.SpeciesId == sp.Id);
            if (count > 0)
                Console.WriteLine($"  Flora '{sp.Id}': {count} instances x {m.TriangleCount} tris = {count * m.TriangleCount:N0} tris");
        }
        for (int s = 1; s <= 3; s++)
        {
            var sw = Stopwatch.StartNew();
            w.CoverageSystem.Step(1800);
            sw.Stop();
            int steadyCount = w.Coverage.Mat.Tiles.Count(t => t.Steady);
            Console.WriteLine($"CoverageSystem.Step #{s} took {sw.ElapsedMilliseconds} ms (Steady tiles: {steadyCount}/{w.Coverage.Mat.TileCount}). Mat stats: {w.CoverageSystem.LastMatStats}");
        }
        return 0;
    }
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
        if (!FaunaScenario.TryParse(Opt("scenario", "default"), out var scenario))
        {
            Console.Error.WriteLine($"unknown --scenario; expected one of {string.Join("|", FaunaScenario.Names)}");
            return 1;
        }
        double days = double.Parse(Opt("days", "28"));
        double report = double.Parse(Opt("report-days", "2"));
        var sw = Stopwatch.StartNew();
        var w = VivariumWorld.Create(content, d);
        // Snapshot the creation report before the scenario touches fauna, so this line keeps describing creation.
        string created = $"created in {sw.ElapsedMilliseconds} ms: props {w.Props.Count}, flora {w.Flora.Count}, fauna {w.Fauna.Count}, wet {EcosystemStatistics.Compute(w).WetFraction:P1}";
        ApplyFaunaScenario(w, scenario);
        Console.WriteLine(created);
        Print(w);
        PrintFaunaScenario(w, scenario);
        long ticksPerReport = (long)(report * 86400 / 10);
        var saveDays = new Queue<double>(Opt("save-days", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).OrderBy(x => x));
        string saveDir = Opt("save-dir", Path.Combine(root, "build", "aged"));
        void SaveDue()
        {
            while (saveDays.Count > 0 && w.Clock.BioDays >= saveDays.Peek() - 1e-9)
            {
                Directory.CreateDirectory(saveDir);
                string path = Path.Combine(saveDir, $"bioday{saveDays.Dequeue():000}.vivsave");
                var r = Vivarium.Sim.Persistence.SaveSystem.Save(w, path);
                Console.WriteLine(r.Ok ? $"saved {path}" : $"SAVE FAILED {path}: {r.Message}");
            }
        }
        SaveDue();
        while (w.Clock.SimDays < days - 1e-9)
        {
            var t0 = sw.Elapsed;
            w.Step(ticksPerReport);
            var inv = w.CheckInvariants();
            Console.WriteLine($"--- day {w.Clock.SimDays:0.0}  ({(sw.Elapsed - t0).TotalMilliseconds / ticksPerReport * 1000:0} µs/tick)");
            Print(w);
            SaveDue();
            if (inv.Count > 0) { Console.WriteLine("INVARIANT FAILURES:\n  " + string.Join("\n  ", inv.Take(10))); return 2; }
        }
        Console.WriteLine($"total {sw.Elapsed.TotalSeconds:0.0} s wall for {days} sim days");
        PrintTiming(w);
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
    var ambientSoil = w.Grid.DomainCells.Where(i => w.SubstrateAtCell(w.Grid.CellCenter(i)) == Vivarium.Sim.Content.Substrate.Soil && w.Water.OpenWaterDepth(w.Grid.CellCenter(i)) <= 0.006).ToArray();
    Console.WriteLine($"  ground: ambient all {w.Grid.DomainCells.Average(i => w.AmbientGroundCover.Cover[i]):P1}  eligible-soil {(ambientSoil.Length > 0 ? ambientSoil.Average(i => w.AmbientGroundCover.Cover[i]) : 0):P1}  litter {(w.Litter.ExportFine().Sum() + w.Litter.ExportCoarse().Sum()):0.00}");
    Console.WriteLine("  flora: " + string.Join("  ", s.Flora.Select(f => $"{f.Id}={f.Count}({f.Biomass:0.0})")));
    Console.WriteLine("  fauna: " + string.Join("  ", s.Fauna.Select(f => $"{f.Id}={f.Count} e{f.MeanEnergy:0.00} g{f.MaxGeneration} b{f.Births}/d{f.Deaths} {f.MeanBodySizeMm:0.0}mm")));
    var causes = w.Tally.Species.Where(kv => kv.Value.DeathsByCause.Count > 0).Select(kv => kv.Key + ":" + string.Join(",", kv.Value.DeathsByCause.Select(c => $"{c.Key}={c.Value}")));
    Console.WriteLine("  deaths: " + string.Join(" | ", causes));
}

// Scenario fauna is applied AFTER world creation. Terrain, water, props and flora are therefore identical to a
// same-seed `--scenario default` run and only the animal count differs -- the measured delta is fauna load, not
// world layout. Nothing here touches a Cadence, a simulation parameter or a scheduler registration.
static void ApplyFaunaScenario(VivariumWorld w, FaunaScenario scenario)
{
    if (!scenario.Apply) return;                 // default: the preset's own starter fauna, untouched
    w.Fauna.Clear();
    if (scenario.Density <= 0) return;          // none: zero fauna, flora and water as created

    // Seeded, independent, CLI-owned stream. It consumes nothing from the simulation's own streams
    // (world.starters, fauna.founder, ...), so applying a scenario cannot perturb the world it measures.
    var rng = Rng.Stream(w.Seed, "cli.soak.scenario");
    const string densityStream = "cli.soak.scenario.density";
    int speciesIndex = 0;
    foreach (var entry in w.Descriptor.StarterFauna)
    {
        var sp = w.Content.FaunaById(entry.Species);
        if (sp == null) continue;
        // Per-species size is a function of (preset starter count, world seed, density) -- never a literal count.
        int index = speciesIndex++;
        double jitter = 0.8 + 0.4 * Rng.HashUnit(w.Seed, Hash.Fnv1a64(densityStream), (ulong)index);
        int target = Math.Clamp((int)Math.Round(entry.Count * scenario.Density * jitter), 0, sp.PopulationCap);
        if (target == 0) continue;

        var sites = RankFaunaSites(w, sp, rng);
        int placed = 0, group = 0;
        while (placed < target && sites.Count > 0 && group <= target * 4)
        {
            var centre = sites[rng.NextInt(sites.Count)];
            int n = Math.Min(sp.Behaviors.Contains("schooling") ? 8 : 4, target - placed);
            for (int i = 0; i < n; i++)
            {
                var q = centre + new Vec2(rng.Range(-0.15, 0.15), rng.Range(-0.15, 0.15));
                if (Introduction.FaunaPlacementProblem(w, sp, q) != null) q = centre;
                w.FaunaSystem.CreateFounder(sp, q);
                placed++;
            }
            group++;
        }
        if (placed < target)
            Console.WriteLine($"  scenario: {sp.Id} placed {placed}/{target} (limited suitable habitat)");
    }
}

/// <summary>Suitability-ranked placement sites for a species: best habitat first, seeded jitter on ties.</summary>
static List<Vec2> RankFaunaSites(VivariumWorld w, FaunaSpeciesDef sp, Rng rng)
{
    var scored = new List<(Vec2 Pos, double Score, int Cell)>();
    var cells = w.Grid.DomainCells;
    for (int k = 0; k < cells.Length; k += 2)
    {
        int cell = cells[k];
        var c = w.Grid.CellCenter(cell);
        if (Introduction.FaunaPlacementProblem(w, sp, c) != null) continue;
        var s = w.FaunaSystem.Suitability(sp, c);
        if (s.HardRefused) continue;
        scored.Add((c, s.Score + 0.5 * w.FaunaSystem.FoodAt(sp, c) + rng.NextDouble() * 0.15, cell));
    }
    scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Cell.CompareTo(b.Cell));
    return scored.Take(Math.Max(8, scored.Count / 3)).Select(x => x.Pos).ToList();
}

/// <summary>Scenario identity plus the total and per-species fauna counts every scenario must report.</summary>
static void PrintFaunaScenario(VivariumWorld w, FaunaScenario scenario)
{
    var s = EcosystemStatistics.Compute(w);
    Console.WriteLine($"  scenario: {scenario.Name} (density {scenario.Density:0.00}, seed {w.Seed}, applied {(scenario.Apply ? "yes" : "no")})");
    Console.WriteLine($"  fauna total: {s.FaunaTotal}");
    Console.WriteLine("  fauna species: " + string.Join("  ", s.Fauna.Select(f => $"{f.Id}={f.Count}")));
}

/// <summary>Runs / total / mean / max ms for every registered scheduler system, plus a guard on the measured three.</summary>
static void PrintTiming(VivariumWorld w)
{
    Console.WriteLine("--- scheduler timing: every registered system ---");
    foreach (var sys in w.Scheduler.Systems) Console.WriteLine($"  {sys.Name,-20} runs {sys.Runs,7}  total {sys.TotalMs,9:0} ms  mean {sys.TotalMs / Math.Max(1, sys.Runs):0.000} ms  max {sys.MaxMs:0.0} ms");
    foreach (var name in FaunaScenario.TimedSystems)
        if (!w.Scheduler.Systems.Any(s => s.Name == name)) Console.WriteLine($"  WARNING: measured system '{name}' is not registered");
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

/// <summary>
/// Deterministic fauna load for the soak baseline harness. Densities are multipliers on the world preset's own
/// starter counts, so the harness stays honest as content data changes: no animal count is hardcoded, and
/// <c>default</c> leaves the preset's starters exactly as they are.
/// </summary>
sealed record FaunaScenario(string Name, double Density, bool Apply)
{
    public static readonly string[] Names = { "default", "none", "low", "medium", "high" };

    /// <summary>Systems this effort measures; asserted present in every scenario timing report.</summary>
    public static readonly string[] TimedSystems = { "fauna.behaviour", "fauna.metabolism", "fauna.lifecycle" };

    public static bool TryParse(string? raw, out FaunaScenario scenario)
    {
        switch ((raw ?? "default").Trim().ToLowerInvariant())
        {
            case "default": scenario = new("default", 1.0, false); return true;   // preset starters, untouched
            case "none": scenario = new("none", 0.0, true); return true;
            case "low": scenario = new("low", 0.2, true); return true;
            case "medium": scenario = new("medium", 1.0, true); return true;
            case "high": scenario = new("high", 3.0, true); return true;
            default: scenario = new("default", 1.0, false); return false;
        }
    }
}
