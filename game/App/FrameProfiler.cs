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
    private static readonly Dictionary<string, double> _max = new();

    public readonly struct Scope : System.IDisposable
    {
        private readonly string _name; private readonly long _t0;
        public Scope(string name) { _name = name; _t0 = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            double ms = Stopwatch.GetElapsedTime(_t0).TotalMilliseconds;
            if (!_max.TryGetValue(_name, out var m) || ms > m) _max[_name] = ms;
        }
    }

    public static Scope Measure(string name) => new(name);

    /// <summary>The slowest scopes since the last call ("name=ms"), then resets.</summary>
    public static string TakeReport(int top = 5)
    {
        var s = string.Join(" ", _max.OrderByDescending(kv => kv.Value).Take(top).Select(kv => $"{kv.Key}={kv.Value:0.0}"));
        _max.Clear();
        return s;
    }
}
