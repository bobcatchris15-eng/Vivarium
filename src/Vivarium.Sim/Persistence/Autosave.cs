using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Persistence;

/// <summary>
/// Periodic autosave. The world snapshot is taken synchronously (milliseconds, main thread); the file write
/// runs on a background task. A new autosave never starts while one is still writing, and the simulation
/// is never blocked waiting for disk.
/// </summary>
public sealed class AutosaveController
{
    private Task<SaveResult>? _pending;
    private double _elapsed;

    public double IntervalSeconds { get; set; } = 300;
    public bool Enabled { get; set; } = true;
    public string Path { get; set; }
    public int Started { get; private set; }
    public int SkippedBecauseBusy { get; private set; }
    public SaveResult? LastResult { get; private set; }
    public bool Busy => _pending != null && !_pending.IsCompleted;
    /// <summary>Raised on the thread that calls Tick when a background write completes.</summary>
    public event Action<SaveResult>? Completed;

    /// <summary>Writer used for the file step (replaceable in tests to simulate slow disks).</summary>
    public Func<SaveManifest, IReadOnlyDictionary<string, byte[]>, string, SaveResult> Writer { get; set; } = SaveSystem.Write;

    public AutosaveController(string path) { Path = path; }

    /// <summary>Call once per frame with real elapsed seconds. Returns true if an autosave was started.</summary>
    public bool Tick(VivariumWorld w, double realSeconds)
    {
        Poll();
        if (!Enabled) return false;
        _elapsed += Math.Max(0, realSeconds);
        if (_elapsed < IntervalSeconds) return false;
        return TryStart(w);
    }

    /// <summary>Starts an autosave now unless one is still in flight.</summary>
    public bool TryStart(VivariumWorld w)
    {
        Poll();
        if (Busy) { SkippedBecauseBusy++; return false; }
        _elapsed = 0;
        var (manifest, payloads) = SaveSystem.Snapshot(w);
        var writer = Writer; var path = Path;
        _pending = Task.Run(() => writer(manifest, payloads, path));
        Started++;
        return true;
    }

    public void Poll()
    {
        if (_pending == null || !_pending.IsCompleted) return;
        var t = _pending; _pending = null;
        LastResult = t.IsFaulted ? new SaveResult { Ok = false, Message = t.Exception?.GetBaseException().Message ?? "autosave failed" } : t.Result;
        if (!LastResult.Ok) Log.Warn(LogCategory.Persistence, "Autosave failed: " + LastResult.Message);
        Completed?.Invoke(LastResult);
    }

    /// <summary>Waits for an in-flight write (used on shutdown).</summary>
    public void Flush(TimeSpan timeout) { try { _pending?.Wait(timeout); } catch { } Poll(); }
}
