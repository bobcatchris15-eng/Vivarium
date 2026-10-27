using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Bounded loam dressing covering the full island domain. Organic pieces read existing litter mass; no simulation writes.</summary>
public partial class LoamSurfaceDetailRenderer : Node3D
{
    private const float PatchSize = 2f;
    private VivariumWorld? _world;
    private readonly Dictionary<(int X, int Z), (Node3D Node, long Stamp)> _patches = new();
    private double _editDelay;
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public int PatchCount => _patches.Count;
    public int RebuildCount { get; private set; }
    private readonly Queue<(int X, int Z)> _startupPatches = new();
    private List<(int X, int Z)> _domainPatches = new();
    private readonly Queue<(int X, int Z)> _dirtyPatches = new();
    private IEnumerator<bool>? _pendingBuild;
    private int _pendingTerrain, _pendingProps, _pendingQuality;
    public double MaxBuildStepMs { get; private set; }
    public int PendingPatchCount => _dirtyPatches.Count + (_pendingBuild is null ? 0 : 1);
    public bool InitializationComplete => _startupPatches.Count == 0;

    /// <summary>Called by the loading iterator, so the playable first frame has complete coverage.</summary>
    public void BuildStartupBatch(int budget = 2)
    {
        if (_world is null) return;
        while (budget-- > 0 && _startupPatches.TryDequeue(out var key)) RebuildPatch(key, _world);
    }

    public void Build(VivariumWorld world)
    {
        _pendingBuild?.Dispose(); _pendingBuild = null; _dirtyPatches.Clear();
        foreach (var entry in _patches.Values) entry.Node.QueueFree();
        _patches.Clear();
        _world = world;
        _editDelay = 0;
        RebuildCount = 0;
        _startupPatches.Clear();
        _domainPatches = GetDomainPatches(world);
        if (Quality > 0)
        {
            var all = _domainPatches;
            foreach (var key in all)
            {
                _startupPatches.Enqueue(key);
            }
        }
    }

    public override void _Process(double delta)
    {
        using var timing = FrameProfiler.Measure("LoamDetail");
        if (_world is null) return;
        Visible = Quality > 0;
        if (!Visible) { _pendingBuild?.Dispose(); _pendingBuild = null; _dirtyPatches.Clear(); return; }
        if (!InitializationComplete) { BuildStartupBatch(); return; }
        if (_pendingBuild is not null)
        {
            if (_pendingTerrain != _world.Terrain.Version || _pendingProps != _world.Props.Version || _pendingQuality != Quality)
            {
                Invalidate();
            }
            else { ContinueBuild(); return; }
        }
        _editDelay -= delta > 0 ? delta : .016;
        if (_editDelay > 0) return;
        if (_dirtyPatches.TryDequeue(out var key))
        {
            _pendingTerrain = _world.Terrain.Version; _pendingProps = _world.Props.Version; _pendingQuality = Quality;
            _pendingBuild = RebuildPatchBatched(key, _world).GetEnumerator();
            ContinueBuild();
        }
    }

    public override void _ExitTree() { _pendingBuild?.Dispose(); _pendingBuild = null; }

    private void ContinueBuild()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        do
        {
            long step = System.Diagnostics.Stopwatch.GetTimestamp();
            bool more = _pendingBuild!.MoveNext();
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(step).TotalMilliseconds;
            MaxBuildStepMs = Math.Max(MaxBuildStepMs, ms);
            FrameProfiler.Record("LoamDetail.BuildStep", ms);
            if (!more) { _pendingBuild.Dispose(); _pendingBuild = null; break; }
        } while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2);
    }

    private IEnumerable<bool> RebuildPatchBatched((int X, int Z) key, VivariumWorld world)
    {
        long stamp = PatchStamp(key);
        var center = new Vector2((key.X + .5f) * PatchSize, (key.Z + .5f) * PatchSize);
        int seed = unchecked((int)world.Seed ^ key.X * 73856093 ^ key.Z * 19349663);
        var patch = new Node3D { Name = $"LoamPatch_{key.X}_{key.Z}" };
        bool published = false;
        try
        {
            foreach (bool step in LoamLitterBuilder.BuildBatched(world, seed, node => patch.AddChild(node),
                Quality >= 2 ? 1.45f : .85f, detailStage: 2, patchCenter: center, halfExtent: PatchSize * .5f, respectLitterMass: true))
                yield return step;
            foreach (bool step in LoamAggregateBuilder.BuildBatched(world, seed, node => patch.AddChild(node), center, PatchSize * .5f))
                yield return step;
            SetShadowsRecursive(patch);
            // Keep the old complete patch visible throughout preparation. Terrain/prop/quality
            // changes cancel staging. Dressing is rebuilt after edits, not ecological ticks.
            if (_patches.TryGetValue(key, out var old)) { RemoveChild(old.Node); old.Node.QueueFree(); }
            AddChild(patch); _patches[key] = (patch, stamp); RebuildCount++; published = true;
        }
        finally { if (!published) patch.Free(); }
    }

    private List<(int X, int Z)> GetDomainPatches(VivariumWorld world)
    {
        int minPatch = -Mathf.CeilToInt((float)world.Domain.Radius / PatchSize);
        int maxPatch = Mathf.CeilToInt((float)world.Domain.Radius / PatchSize);
        var patches = new List<(int X, int Z)>();
        float h = PatchSize * 0.5f;
        for (int z = minPatch; z <= maxPatch; z++)
        for (int x = minPatch; x <= maxPatch; x++)
        {
            var center = new Vector2((x + .5f) * PatchSize, (z + .5f) * PatchSize);
            // Check if any corner or center is within Domain
            if (!world.Domain.Contains(new Vivarium.Sim.Core.Vec2(center.X, center.Y)) &&
                !world.Domain.Contains(new Vivarium.Sim.Core.Vec2(center.X - h, center.Y - h)) &&
                !world.Domain.Contains(new Vivarium.Sim.Core.Vec2(center.X + h, center.Y - h)) &&
                !world.Domain.Contains(new Vivarium.Sim.Core.Vec2(center.X - h, center.Y + h)) &&
                !world.Domain.Contains(new Vivarium.Sim.Core.Vec2(center.X + h, center.Y + h)))
                continue;
            patches.Add((x, z));
        }
        return patches;
    }

    private void RebuildPatch((int X, int Z) key, VivariumWorld world)
    {
        long stamp = PatchStamp(key);
        _patches.TryGetValue(key, out var old);
        var center = new Vector2((key.X + .5f) * PatchSize, (key.Z + .5f) * PatchSize);
        int seed = unchecked((int)world.Seed ^ key.X * 73856093 ^ key.Z * 19349663);
        var patch = new Node3D { Name = $"LoamPatch_{key.X}_{key.Z}" };
        var litterNode = LoamLitterBuilder.Build(world, seed, Quality >= 2 ? 1.45f : .85f,
            detailStage: 2, patchCenter: center, halfExtent: PatchSize * .5f, respectLitterMass: true);
        var aggregateNode = LoamAggregateBuilder.Build(world, seed, center, PatchSize * .5f);
        SetShadowsRecursive(litterNode);
        SetShadowsRecursive(aggregateNode);
        patch.AddChild(litterNode);
        patch.AddChild(aggregateNode);
        if (old.Node is not null) { RemoveChild(old.Node); old.Node.QueueFree(); }
        AddChild(patch);
        _patches[key] = (patch, stamp);
        RebuildCount++;
    }

    private static void SetShadowsRecursive(Node node)
    {
        if (node is GeometryInstance3D geom)
            geom.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        foreach (var child in node.GetChildren())
            SetShadowsRecursive(child);
    }

    /// <summary>Only explicit edits queue static dressing. Ordinary litter/cover updates do no render work.</summary>
    public void Invalidate(Vivarium.Sim.Core.Vec2? centre = null, double radius = double.PositiveInfinity)
    {
        if (_world is null) return;
        // An edit can invalidate the patch currently being prepared. Keep it in the queue
        // even when the next edit is elsewhere, so cancellation cannot lose earlier work.
        if (_pendingBuild is not null)
        {
            _pendingBuild.Dispose(); _pendingBuild = null;
            centre = null;
        }
        _editDelay = .15;
        var world = _world!;
        Vector3 eye = Camera?.GlobalPosition ?? Vector3.Zero;
        // Keep the window around the ground being inspected rather than behind a tilted camera.
        Vector3 forward = Camera is null ? Vector3.Down : -Camera.GlobalBasis.Z;
        double ground = world.Terrain.Height(new Vivarium.Sim.Core.Vec2(eye.X, eye.Z));
        float reach = forward.Y < -.1f ? Mathf.Clamp((eye.Y - (float)ground) / -forward.Y, 0f, 5f) : 0f;
        Vector3 focus = eye + forward * reach;
        int cx = Mathf.FloorToInt(focus.X / PatchSize), cz = Mathf.FloorToInt(focus.Z / PatchSize);

        var wanted = _domainPatches;
        var queued = new HashSet<(int X, int Z)>(_dirtyPatches);
        foreach (var key in wanted.OrderBy(k => (k.X - cx) * (k.X - cx) + (k.Z - cz) * (k.Z - cz)))
        {
            if (centre is { } p)
            {
                double x = Math.Clamp(p.X, key.X * PatchSize, (key.X + 1) * PatchSize);
                double z = Math.Clamp(p.Z, key.Z * PatchSize, (key.Z + 1) * PatchSize);
                if (Vivarium.Sim.Core.Vec2.DistanceSq(p, new(x, z)) > radius * radius) continue;
            }
            if (queued.Add(key)) _dirtyPatches.Enqueue(key);
        }
    }

    private long PatchStamp((int X, int Z) key)
    {
        var w = _world!;
        long stamp = unchecked(w.Terrain.Version * 397L ^ w.Props.Version * 31L ^ Quality);
        var g = w.Grid;
        int x0 = Math.Max(0, (int)Math.Floor((key.X * PatchSize - g.OriginX) / g.CellSize));
        int z0 = Math.Max(0, (int)Math.Floor((key.Z * PatchSize - g.OriginZ) / g.CellSize));
        int x1 = Math.Min(g.Nx - 1, (int)Math.Ceiling(((key.X + 1) * PatchSize - g.OriginX) / g.CellSize));
        int z1 = Math.Min(g.Nz - 1, (int)Math.Ceiling(((key.Z + 1) * PatchSize - g.OriginZ) / g.CellSize));
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            if (!g.InDomain(x, z)) continue;
            int cell = g.Index(x, z);
            // Quantization prevents tiny simulation changes from rebuilding visible geometry each tick.
            stamp = unchecked(stamp * 31 + QuantizedCover(w.Litter.FineMass[cell], 55) * 7
                + QuantizedCover(w.Litter.CoarseMass[cell], 65) * 13
                + (long)(w.AmbientGroundCover.Cover[cell] * 8) * 17
                + (w.Water.IsWet(cell) ? 1 : 0));
        }
        return stamp;
    }

    private static long QuantizedCover(double mass, double rate) => mass > 0
        ? Math.Max(1L, (long)Math.Ceiling((1 - Math.Exp(-mass * rate)) * 64)) : 0;
}
