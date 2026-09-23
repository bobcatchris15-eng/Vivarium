using Vivarium.Sim.Core;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Diagnostics;

/// <summary>Per-subsystem timing aggregated from the scheduler (read-only observation; never changes behaviour).</summary>
public sealed class SubsystemTimer
{
    public sealed class Entry
    {
        public string Name { get; init; } = "";
        public long Runs { get; set; }
        public double TotalMs { get; set; }
        public double MaxMs { get; set; }
        public double MeanMs => Runs == 0 ? 0 : TotalMs / Runs;
    }

    private readonly SortedDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public IReadOnlyCollection<Entry> Entries => _entries.Values;

    /// <summary>Maps scheduler system names onto the reporting categories requested by diagnostics.</summary>
    public static string Category(string system) => system switch
    {
        "hydrology" => "hydrology",
        "flora" => "flora",
        var s when s.StartsWith("fauna.", StringComparison.Ordinal) => "fauna",
        "environment" or "ecology.resources" or "genetics.prune" => "ecology",
        _ => system,
    };

    public void Attach(Scheduler scheduler) => scheduler.SystemTimed += (s, ms) => Record(Category(s.Name), ms);

    public void Record(string category, double ms)
    {
        if (!_entries.TryGetValue(category, out var e)) _entries[category] = e = new Entry { Name = category };
        e.Runs++; e.TotalMs += ms; if (ms > e.MaxMs) e.MaxMs = ms;
    }

    /// <summary>Times an arbitrary block (rendering-support, persistence) under a category.</summary>
    public T Measure<T>(string category, Func<T> f)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return f(); }
        finally { Record(category, System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds); }
    }

    public void Measure(string category, Action f) => Measure(category, () => { f(); return 0; });

    public string Report() => string.Join("\n", _entries.Values.Select(e => $"{e.Name,-18} runs {e.Runs,8}  mean {e.MeanMs,8:0.000} ms  max {e.MaxMs,7:0.00} ms  total {e.TotalMs,9:0} ms"));
}

public sealed record PopulationCounters(int Flora, int Fauna, int FloraIndexed, int FaunaIndexed, int Genomes, int Lineage,
    int VisibleFlora, int VisibleFauna, int FaunaLodHigh, int FaunaLodLow, int FaunaCulled)
{
    /// <summary>Discrepancies between authoritative collections and their indexes/render counts.</summary>
    public List<string> Mismatches()
    {
        var e = new List<string>();
        if (FloraIndexed != Flora) e.Add($"flora spatial index holds {FloraIndexed}, population is {Flora}");
        if (FaunaIndexed != Fauna) e.Add($"fauna spatial index holds {FaunaIndexed}, population is {Fauna}");
        if (VisibleFlora > Flora) e.Add($"{VisibleFlora} flora rendered but only {Flora} exist");
        if (VisibleFauna > Fauna) e.Add($"{VisibleFauna} fauna rendered but only {Fauna} exist");
        if (FaunaLodHigh + FaunaLodLow + FaunaCulled != Fauna && (FaunaLodHigh + FaunaLodLow + FaunaCulled) > 0)
            e.Add($"fauna LOD buckets sum to {FaunaLodHigh + FaunaLodLow + FaunaCulled}, population is {Fauna}");
        return e;
    }
}

public static class Counters
{
    /// <summary>Authoritative counts plus render-side counts supplied by the client (zero when headless).</summary>
    public static PopulationCounters Collect(VivariumWorld w, int visibleFlora = 0, int visibleFauna = 0, int lodHigh = 0, int lodLow = 0, int culled = 0) =>
        new(w.Flora.Count, w.Fauna.Count, w.Flora.Index.Count, w.Fauna.Index.Count, w.Genomes.Count, w.Lineage.Count,
            visibleFlora, visibleFauna, lodHigh, lodLow, culled);
}

public sealed record BudgetWarning(long Tick, string System, double Milliseconds, double BudgetMs, int Flora, int Fauna)
{
    public override string ToString() => $"tick {Tick}: '{System}' took {Milliseconds:0.0} ms (budget {BudgetMs:0.0} ms) with {Flora} flora / {Fauna} fauna";
}

/// <summary>
/// Detects simulation work exceeding a wall-time budget and names the responsible system and population,
/// turning silent slowdown into an actionable warning.
/// </summary>
public sealed class WorkloadBudget
{
    private readonly VivariumWorld _w;
    public double SystemBudgetMs { get; set; } = 8;
    public int MaxWarningsKept { get; set; } = 50;
    public List<BudgetWarning> Warnings { get; } = new();
    private double _lastLogged = double.NegativeInfinity;

    public WorkloadBudget(VivariumWorld w)
    {
        _w = w;
        w.Scheduler.SystemTimed += OnTimed;
    }

    private void OnTimed(SimSystem s, double ms)
    {
        if (ms <= SystemBudgetMs) return;
        var warn = new BudgetWarning(_w.Clock.Tick, s.Name, ms, SystemBudgetMs, _w.Flora.Count, _w.Fauna.Count);
        Warnings.Add(warn);
        if (Warnings.Count > MaxWarningsKept) Warnings.RemoveAt(0);
        // rate-limit log output to one line per simulated hour
        if (_w.Clock.SimSeconds - _lastLogged > 3600)
        {
            _lastLogged = _w.Clock.SimSeconds;
            Log.Warn(LogCategory.Perf, "Simulation budget exceeded: " + warn);
        }
    }

    public void Detach() => _w.Scheduler.SystemTimed -= OnTimed;
}
