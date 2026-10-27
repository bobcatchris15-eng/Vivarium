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
    private static readonly Dictionary<string, double> _intervalAccum = new();
    private static readonly Dictionary<string, int> _intervalCount = new();
    private static readonly Dictionary<string, double> _frameMax = new();
    private static readonly Dictionary<string, double> _frameAccum = new();
    private static readonly Dictionary<string, int> _frameCount = new();

    public readonly record struct ScopeMetric(double MaxMs, double TotalMs, int Count);

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

    /// <summary>CPU wall-time maxima, overlapping scopes; never GPU time.</summary>
    public static Dictionary<string, double> TakeStructuredReport()
    {
        lock (_lock) { var result = new Dictionary<string, double>(_max); _max.Clear(); return result; }
    }

    public static Dictionary<string,double> MeanDurations()
    {
        lock(_lock)return _accum.ToDictionary(p=>p.Key,p=>p.Value/System.Math.Max(1,_count[p.Key]));
    }

    public static Dictionary<string,double> PerFrameDurations(int frames)
    {
        lock(_lock)return _accum.ToDictionary(p=>p.Key,p=>p.Value/System.Math.Max(1,frames));
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
            _intervalAccum[name] = _intervalAccum.GetValueOrDefault(name) + ms;
            _intervalCount[name] = _intervalCount.GetValueOrDefault(name) + 1;
            if (!_frameMax.TryGetValue(name, out var fm) || ms > fm) _frameMax[name] = ms;
            _frameAccum[name] = _frameAccum.GetValueOrDefault(name) + ms;
            _frameCount[name] = _frameCount.GetValueOrDefault(name) + 1;
        }
    }

    /// <summary>
    /// Total CPU milliseconds and call counts recorded per scope since the previous call.
    /// Unlike the maximum report this shows how much wall time a scope actually consumed
    /// in the interval, which is what grows when a world matures.
    /// </summary>
    public static (Dictionary<string,double> TotalMs, Dictionary<string,int> Calls) TakeIntervalTotals()
    {
        lock (_lock)
        {
            var total = new Dictionary<string,double>(_intervalAccum);
            var calls = new Dictionary<string,int>(_intervalCount);
            _intervalAccum.Clear();
            _intervalCount.Clear();
            return (total, calls);
        }
    }

    /// <summary>The busiest scopes of the last interval by total milliseconds ("name=ms", "name=ms/calls").</summary>
    public static string TakeIntervalReport(int top = 12)
    {
        lock (_lock)
        {
            var s = string.Join(" ", _intervalAccum
                .OrderByDescending(kv => kv.Value)
                .Take(top)
                .Select(kv => $"{kv.Key}={kv.Value:0.0}/{_intervalCount[kv.Key]}"));
            _intervalAccum.Clear();
            _intervalCount.Clear();
            return s;
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
    public static Dictionary<string, ScopeMetric> TakeFrameScopeMetrics()
    {
        lock (_lock)
        {
            var res = new Dictionary<string, ScopeMetric>(_frameAccum.Count);
            foreach (var (k, total) in _frameAccum)
            {
                res[k] = new ScopeMetric(_frameMax.GetValueOrDefault(k), total, _frameCount.GetValueOrDefault(k));
            }
            _frameMax.Clear();
            _frameAccum.Clear();
            _frameCount.Clear();
            return res;
        }
    }

    public static void Reset()
    {
        lock (_lock)
        {
            _max.Clear();
            _accum.Clear();
            _count.Clear();
            _last.Clear();
            _intervalAccum.Clear();
            _intervalCount.Clear();
            _frameMax.Clear();
            _frameAccum.Clear();
            _frameCount.Clear();
        }
    }
}
