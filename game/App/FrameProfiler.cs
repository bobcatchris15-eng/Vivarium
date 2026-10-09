using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Vivarium.Game.App;

/// <summary>
/// Cheap per-subsystem frame timing for the perf run: each scope records its slowest duration since the last
/// report, so a periodic hitch can be traced to the node that caused it. Negligible cost when nobody reads it.
/// </summary>
public static class FrameProfiler
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, double> _max = new();
    private static readonly Dictionary<string, double> _accum = new();
    private static readonly Dictionary<string, int> _count = new();
    private static readonly Dictionary<string, double> _last = new();

    public readonly struct Scope : System.IDisposable
    {
        private readonly string _name; private readonly long _t0;
        public Scope(string name) { _name = name; _t0 = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            double ms = Stopwatch.GetElapsedTime(_t0).TotalMilliseconds;
            RecordDuration(_name, ms);
        }
    }

    public static Scope Measure(string name) => new(name);

    public static void Record(string name, double ms) => RecordDuration(name, ms);

    private static void RecordDuration(string name, double ms)
    {
        lock (_lock)
        {
            if (!_max.TryGetValue(name, out var m) || ms > m) _max[name] = ms;
            _accum[name] = _accum.GetValueOrDefault(name) + ms;
            _count[name] = _count.GetValueOrDefault(name) + 1;
            _last[name] = ms;
        }
    }

    /// <summary>The slowest scopes since the last call ("name=ms"), then resets.</summary>
    public static string TakeReport(int top = 5)
    {
        lock (_lock)
        {
            var s = string.Join(" ", _max.OrderByDescending(kv => kv.Value).Take(top).Select(kv => $"{kv.Key}={kv.Value:0.0}"));
            _max.Clear();
            return s;
        }
    }

    /// <summary>Summary of CPU timings matching Section 1.2 breakdown.</summary>
    public readonly record struct CpuTimingSummary(
        double SimulationCpuMs,
        double RenderStateGatherMs,
        double GeometryBuildCpuMs,
        double MainThreadCommitMs,
        double GpuDrawSyncMs,
        Dictionary<string, double> Subsystems);

    /// <summary>Categorizes tracked subsystem scopes into Section 1.2 CPU timing breakdown.</summary>
    public static CpuTimingSummary GetCpuTimingSummary()
    {
        lock (_lock)
        {
            var subs = new Dictionary<string, double>(_max);
            double sim = 0.0, renderGather = 0.0, geoBuild = 0.0, commit = 0.0, sync = 0.0;

            foreach (var (k, v) in subs)
            {
                if (k.StartsWith("Sys.", System.StringComparison.Ordinal) ||
                    k.StartsWith("Session", System.StringComparison.Ordinal))
                {
                    sim = System.Math.Max(sim, v);
                }
                else if (k.Contains("Geometry", System.StringComparison.OrdinalIgnoreCase) ||
                         k.Contains("Build", System.StringComparison.OrdinalIgnoreCase))
                {
                    geoBuild = System.Math.Max(geoBuild, v);
                }
                else if (k.Contains("Upload", System.StringComparison.OrdinalIgnoreCase) ||
                         k.Contains("Commit", System.StringComparison.OrdinalIgnoreCase))
                {
                    commit = System.Math.Max(commit, v);
                }
                else if (k.Contains("Sync", System.StringComparison.OrdinalIgnoreCase) ||
                         k.Contains("Draw", System.StringComparison.OrdinalIgnoreCase))
                {
                    sync = System.Math.Max(sync, v);
                }
                else
                {
                    renderGather = System.Math.Max(renderGather, v);
                }
            }

            return new CpuTimingSummary(
                System.Math.Round(sim, 2),
                System.Math.Round(renderGather, 2),
                System.Math.Round(geoBuild, 2),
                System.Math.Round(commit, 2),
                System.Math.Round(sync, 2),
                subs);
        }
    }

    /// <summary>Resets all recorded profiler timings.</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _max.Clear();
            _accum.Clear();
            _count.Clear();
            _last.Clear();
        }
    }
}
