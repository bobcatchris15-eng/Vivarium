using System.Diagnostics;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Time;

/// <summary>A registered simulation system. Run(dt) receives the simulated seconds since its last run.</summary>
public sealed class SimSystem
{
    public string Name { get; }
    /// <summary>Runs every Cadence ticks.</summary>
    public int Cadence { get; }
    /// <summary>Tick offset so heavy systems do not all land on the same tick.</summary>
    public int Phase { get; }
    /// <summary>Execution order within a tick (ascending; ties broken by name).</summary>
    public int Order { get; }
    public Action<double> Run { get; }
    internal Func<double, IEnumerable<bool>>? Batches { get; set; }

    // diagnostics (non-authoritative)
    public long Runs { get; internal set; }
    public double TotalMs { get; internal set; }
    public double LastMs { get; internal set; }
    public double MaxMs { get; internal set; }

    public SimSystem(string name, int cadence, int phase, int order, Action<double> run)
    {
        if (cadence < 1) throw new ArgumentOutOfRangeException(nameof(cadence));
        Name = name; Cadence = cadence; Phase = ((phase % cadence) + cadence) % cadence; Order = order; Run = run;
    }

    public bool DueAt(long tick) => (tick - Phase) % Cadence == 0;
    public double Dt => Cadence * SimClock.FixedStepSeconds;
}

/// <summary>
/// Deterministic fixed-step scheduler. Advancing never consults render frame deltas directly: wall time is
/// converted to whole ticks, and leftovers are carried over. Per-frame work is capped; the backlog is kept
/// (never silently dropped) and reported.
/// </summary>
public sealed class Scheduler
{
    private readonly List<SimSystem> _systems = new();
    public SimClock Clock { get; }
    public IReadOnlyList<SimSystem> Systems => _systems;

    /// <summary>Simulated seconds owed but not yet simulated.</summary>
    public double Backlog { get; private set; }
    /// <summary>Maximum ticks executed in one Advance call.</summary>
    public int MaxTicksPerAdvance { get; set; } = 400;
    /// <summary>Optional wall-clock budget per Advance call in milliseconds (0 = unlimited).</summary>
    public double WallBudgetMs { get; set; } = 12;
    /// <summary>Backlog above which diagnostics warn (in simulated seconds).</summary>
    public double BacklogWarnSeconds { get; set; } = 3600;

    /// <summary>Invoked after every tick (for invariants in tests / diagnostics). Must not mutate state.</summary>
    public event Action<long>? AfterTick;
    public event Action<SimSystem, double>? SystemTimed;

    public long TicksLastAdvance { get; private set; }
    public double LastAdvanceMs { get; private set; }

    public Scheduler(SimClock clock) { Clock = clock; }

    public SimSystem Register(string name, int cadence, int order, Action<double> run, int phase = 0)
    {
        if (_systems.Any(s => s.Name == name)) throw new InvalidOperationException($"System '{name}' already registered.");
        var s = new SimSystem(name, cadence, phase, order, run);
        _systems.Add(s);
        _systems.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
        return s;
    }

    /// <summary>Human-readable, deterministic description of tick order and cadence.</summary>
    public string DescribeSchedule() =>
        string.Join("\n", _systems.Select(s => $"{s.Order,4} {s.Name,-22} every {s.Cadence,4} tick(s) (dt {s.Dt,6:0}s) phase {s.Phase}"));

    /// <summary>Names of systems that run on a given tick, in execution order.</summary>
    public IEnumerable<string> SystemsDueAt(long tick) => _systems.Where(s => s.DueAt(tick)).Select(s => s.Name);

    private IEnumerator<bool>? _pendingTick;
    public bool HasPendingTick => _pendingTick != null;
    public bool YieldAtTickBoundary { get; set; }

    /// <summary>Registers ordered resumable work. No other system runs until all its batches finish.</summary>
    public SimSystem RegisterBatched(string name, int cadence, int order, Func<double, IEnumerable<bool>> batches, int phase = 0)
    {
        var system = Register(name, cadence, order, dt => { foreach (var _ in batches(dt)) { } }, phase);
        system.Batches = batches;
        return system;
    }

    private IEnumerable<bool> TickBatches()
    {
        long next = Clock.Tick + 1;
        foreach (var s in _systems)
        {
            if (!s.DueAt(next)) continue;
            double total = 0;
            if (s.Batches is { } batches)
            {
                using var work = batches(s.Dt).GetEnumerator();
                while (true)
                {
                    long begin = Stopwatch.GetTimestamp();
                    bool more = work.MoveNext();
                    double ms = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                    total += ms;
                    SystemTimed?.Invoke(s, ms);
                    if (!more) break;
                    yield return true;
                }
            }
            else
            {
                long begin = Stopwatch.GetTimestamp();
                s.Run(s.Dt);
                total = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                SystemTimed?.Invoke(s, total);
            }
            s.Runs++; s.TotalMs += total; s.LastMs = total; s.MaxMs = Math.Max(s.MaxMs, total);
            yield return true;
        }
        Clock.Tick = next;
        Clock.BioSeconds += SimClock.FixedStepSeconds * Clock.BioAcceleration;
        AfterTick?.Invoke(next);
    }

    /// <summary>Finish an already-started tick before saving or an external world edit.</summary>
    public void CompletePendingTick()
    {
        if (_pendingTick == null) return;
        while (_pendingTick.MoveNext()) { }
        _pendingTick.Dispose(); _pendingTick = null;
        YieldAtTickBoundary = false;
        Backlog = Math.Max(0, Backlog - SimClock.FixedStepSeconds);
    }

    /// <summary>Executes exactly one fixed tick, or finishes the current partially processed tick.</summary>
    public void StepTick()
    {
        if (HasPendingTick) { CompletePendingTick(); return; }
        foreach (var _ in TickBatches()) { }
    }

    public void StepTicks(long n) { for (long i = 0; i < n; i++) StepTick(); }

    /// <summary>Advances until the clock reaches the given tick (ignores budgets; for tests and loading).</summary>
    public void RunUntilTick(long tick) { while (Clock.Tick < tick) StepTick(); }

    /// <summary>
    /// Converts real elapsed seconds into simulated work. Returns ticks executed. Pausing contributes zero
    /// simulated time. Work beyond the tick/wall budget stays in Backlog and is executed on later calls.
    /// </summary>
    public int Advance(double realSeconds)
    {
        if (double.IsFinite(realSeconds) && realSeconds > 0)
        {
            if (realSeconds > 5 && !Clock.Paused)
                Log.Info(LogCategory.Time, $"Long frame of {realSeconds:0.0} s queued {realSeconds * Clock.EffectiveRate / 60:0} sim-min of catch-up work.");
            Backlog += realSeconds * Clock.EffectiveRate;
        }
        long t0 = Stopwatch.GetTimestamp();
        int ticks = 0;
        while (Backlog >= SimClock.FixedStepSeconds && ticks < MaxTicksPerAdvance)
        {
            _pendingTick ??= TickBatches().GetEnumerator();
            if (!_pendingTick.MoveNext())
            {
                _pendingTick.Dispose(); _pendingTick = null;
                Backlog -= SimClock.FixedStepSeconds;
                ticks++;
                if (YieldAtTickBoundary) { YieldAtTickBoundary = false; break; }
            }
            if (WallBudgetMs > 0 && Stopwatch.GetElapsedTime(t0).TotalMilliseconds > WallBudgetMs) break;
        }
        TicksLastAdvance = ticks;
        LastAdvanceMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        // warn at most every 30 s of wall time: warning every frame wrote tens of thousands of identical lines
        if (Backlog > BacklogWarnSeconds && Stopwatch.GetElapsedTime(_lastBacklogWarn).TotalSeconds >= 30)
        {
            _lastBacklogWarn = Stopwatch.GetTimestamp();
            Log.Warn(LogCategory.Perf, $"Simulation backlog {Backlog / 60:0} sim-min exceeds budget; speed {Clock.SpeedMultiplier}x is more than this machine sustains.");
        }
        return ticks;
    }

    private long _lastBacklogWarn;   // 0 = process start, so the first warning is not suppressed

    /// <summary>Explicitly discard backlog (e.g. after pausing). Never called implicitly.</summary>
    public void ClearBacklog() { CompletePendingTick(); Backlog = 0; }

    /// <summary>Fraction of the next tick accumulated (render interpolation hint).</summary>
    public double TickFraction => MathD.Clamp01(Backlog / SimClock.FixedStepSeconds);
}
