using System;
using System.Collections.Generic;
using System.Threading;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Light;

public sealed class LightCache
{
    private sealed record Change(float[]? Positions, int[]? Indices, double[]? Transform, double Opacity);
    private readonly object _gate = new();
    private readonly Dictionary<ulong, Change> _pending = new();
    private readonly Queue<ulong> _order = new();
    private readonly int _capacity;
    private readonly OcclusionVolume _volume;
    public Aabb Bounds => _volume.Bounds;
    public double VoxelSize => _volume.VoxelSize;
    public LightCache(Aabb bounds, double voxel, int capacity, CancellationToken cancellation = default)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _volume = new OcclusionVolume(bounds, voxel, cancellation: cancellation);
        _volume.BakeAll(cancellation);
    }
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    // Arrays are copied because producers may recycle their buffers after submission.
    public bool TrySet(ulong id, float[] positions, int[] indices, double[]? transform, double opacity)
    {
        ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(indices);
        if (positions.Length % 3 != 0 || indices.Length % 3 != 0 || (transform != null && transform.Length != 12) || !double.IsFinite(opacity) || opacity < 0)
            throw new ArgumentException("Invalid triangle source shape or opacity");
        foreach (float x in positions) if (!float.IsFinite(x)) throw new ArgumentException("Non-finite position");
        foreach (int index in indices) if (index < 0 || index >= positions.Length / 3) throw new ArgumentException("Invalid triangle index");
        if (transform != null) foreach (double x in transform) if (!double.IsFinite(x)) throw new ArgumentException("Non-finite transform");
        lock (_gate)
        {
            if (!_pending.ContainsKey(id) && _pending.Count >= _capacity) return false;
            if (!_pending.ContainsKey(id)) _order.Enqueue(id);
            _pending[id] = new Change((float[])positions.Clone(), (int[])indices.Clone(), transform == null ? null : (double[])transform.Clone(), opacity);
            return true;
        }
    }
    public bool TryRemove(ulong id) => Enqueue(id, new Change(null, null, null, 0));
    private bool Enqueue(ulong id, Change change)
    {
        lock (_gate)
        {
            if (!_pending.ContainsKey(id) && _pending.Count >= _capacity) return false;
            if (!_pending.ContainsKey(id)) _order.Enqueue(id);
            _pending[id] = change;
            return true;
        }
    }
    // Single consumer only. Pending changes remain enqueueable while it bakes.
    public void ProcessBatch(int maxSources, CancellationToken cancellation = default)
    {
        if (maxSources < 1) throw new ArgumentOutOfRangeException(nameof(maxSources));
        var work = new List<KeyValuePair<ulong, Change>>();
        lock (_gate)
        {
            while (work.Count < maxSources && _order.Count > 0)
            {
                ulong id = _order.Dequeue();
                work.Add(new(id, _pending[id])); _pending.Remove(id);
            }
        }
        var dirty = new List<DirtyRecord>();
        foreach (var (id, change) in work)
        {
            cancellation.ThrowIfCancellationRequested();
            var old = _volume.Remove(id, cancellation);
            if (old is { } b) dirty.Add(new DirtyRecord(b, DirtyDomain.Geometry, 0, "remove", 0));
            if (change.Positions != null)
            {
                var added = _volume.AddTriangles(id, change.Positions, change.Indices!, change.Transform, change.Opacity, cancellation);
                dirty.Add(new DirtyRecord(added, DirtyDomain.Geometry, 0, "set", 0));
            }
        }
        _volume.Invalidate(dirty, cancellation);
    }
    public LightTransportSample Sample(Vec3 p) => _volume.Sample(p);
    public byte[] Pack(out int nx, out int ny, out int nz, CancellationToken cancellation = default) => _volume.PackRgba8(out nx, out ny, out nz, cancellation);
}
