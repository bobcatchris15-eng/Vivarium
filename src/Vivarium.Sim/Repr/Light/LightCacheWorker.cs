using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vivarium.Sim.Repr.Light;
public sealed class LightCacheWorker : IDisposable
{
    public sealed record Snapshot(byte[] Bytes, int Nx, int Ny, int Nz, Aabb Bounds, double Voxel);
    private readonly Task<LightCache> _initialization;
    private static readonly SemaphoreSlim WorkGate = new(1, 1);
    private readonly CancellationTokenSource _cancel = new();
    private Task<Snapshot>? _work;
    private bool _disposed;
    public LightCacheWorker(Aabb bounds, double voxel, int capacity)
    {
        if (capacity < 1 || !double.IsFinite(voxel) || voxel <= 0) throw new ArgumentOutOfRangeException();
        var token = _cancel.Token;
        _initialization = Task.Run(async () =>
        {
            await WorkGate.WaitAsync(token).ConfigureAwait(false);
            try { return new LightCache(bounds, voxel, capacity, token); }
            finally { WorkGate.Release(); }
        });
    }
    public bool Ready => !_disposed && _initialization.IsCompletedSuccessfully && !(_work?.IsFaulted ?? false) && !(_work?.IsCanceled ?? false);
    public Exception? Error => _initialization.Exception ?? _work?.Exception;
    public int PendingCount => Ready ? _initialization.Result.PendingCount : 0;
    public bool TrySet(ulong id, float[] p, int[] i, double[]? t, double opacity)
        => Ready && _initialization.Result.TrySet(id, p, i, t, opacity);
    public bool TryRemove(ulong id) => Ready && _initialization.Result.TryRemove(id);
    public bool Start(int maxSources)
    {
        if (maxSources < 1) throw new ArgumentOutOfRangeException(nameof(maxSources));
        if (!Ready || _work != null || PendingCount == 0) return false;
        var cache = _initialization.Result;
        var token = _cancel.Token;
        _work = Task.Run(async () =>
        {
            await WorkGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                cache.ProcessBatch(maxSources, token);
                var bytes = cache.Pack(out int nx, out int ny, out int nz, token);
                return new Snapshot(bytes, nx, ny, nz, cache.Bounds, cache.VoxelSize);
            }
            finally { WorkGate.Release(); }
        });
        return true;
    }
    public bool TryTake(out Snapshot? snapshot)
    {
        snapshot = null;
        if (_disposed || _work == null || !_work.IsCompletedSuccessfully) return false;
        snapshot = _work.Result; _work = null; return true;
    }
    /// <summary>After disposal, await this off-thread before reclaiming world resources.</summary>
    public Task Quiescence => _work ?? (Task)_initialization;
    // A fault invalidates the partially mutated cache. Discard this worker and replay authoritative sources into a new one.
    public void Dispose() { if (_disposed) return; _disposed = true; _cancel.Cancel(); }
}
