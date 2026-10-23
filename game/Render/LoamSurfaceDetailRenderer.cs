using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Bounded, camera-local loam dressing. Organic pieces read existing litter mass; no simulation writes.</summary>
public partial class LoamSurfaceDetailRenderer : Node3D
{
    private const float PatchSize = 2f;
    private VivariumWorld? _world;
    private readonly Dictionary<(int X, int Z), (Node3D Node, long Stamp)> _patches = new();
    private double _elapsed;
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public int PatchCount => _patches.Count;
    public int RebuildCount { get; private set; }

    public void Build(VivariumWorld world)
    {
        foreach (var entry in _patches.Values) entry.Node.QueueFree();
        _patches.Clear();
        _world = world;
        _elapsed = 999;
        RebuildCount = 0;
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        Visible = Quality > 0;
        if (!Visible) return;
        // Native still-frame reviews freeze engine time; presentation streaming must still settle.
        _elapsed += delta > 0 ? delta : .10;
        if (_elapsed < .10) return;
        _elapsed = 0;
        RefreshOnePatch();
    }

    private void RefreshOnePatch()
    {
        var world = _world!;
        Vector3 eye = Camera?.GlobalPosition ?? Vector3.Zero;
        // Keep the window around the ground being inspected rather than behind a tilted camera.
        Vector3 forward = Camera is null ? Vector3.Down : -Camera.GlobalBasis.Z;
        double ground = world.Terrain.Height(new Vivarium.Sim.Core.Vec2(eye.X, eye.Z));
        float reach = forward.Y < -.1f ? Mathf.Clamp((eye.Y - (float)ground) / -forward.Y, 0f, 5f) : 0f;
        Vector3 focus = eye + forward * reach;
        int cx = Mathf.FloorToInt(focus.X / PatchSize), cz = Mathf.FloorToInt(focus.Z / PatchSize);
        var wanted = new List<(int X, int Z)>();
        for (int z = cz - 1; z <= cz + 1; z++)
        for (int x = cx - 1; x <= cx + 1; x++) wanted.Add((x, z));
        foreach (var key in _patches.Keys.Where(k => !wanted.Contains(k)).ToArray())
        { _patches[key].Node.QueueFree(); _patches.Remove(key); }
        // Nearest patch first; at most one mesh upload per update regardless of island size.
        foreach (var key in wanted.OrderBy(k => (k.X - cx) * (k.X - cx) + (k.Z - cz) * (k.Z - cz)))
        {
            long stamp = PatchStamp(key);
            if (_patches.TryGetValue(key, out var old) && old.Stamp == stamp) continue;
            var center = new Vector2((key.X + .5f) * PatchSize, (key.Z + .5f) * PatchSize);
            int seed = unchecked((int)world.Seed ^ key.X * 73856093 ^ key.Z * 19349663);
            var patch = new Node3D { Name = $"LoamPatch_{key.X}_{key.Z}" };
            patch.AddChild(LoamLitterBuilder.Build(world, seed, Quality >= 2 ? 1.45f : .85f,
                detailStage: 2, patchCenter: center, halfExtent: PatchSize * .5f, respectLitterMass: true));
            patch.AddChild(LoamAggregateBuilder.Build(world, seed, center, PatchSize * .5f));
            if (old.Node is not null) { RemoveChild(old.Node); old.Node.QueueFree(); }
            AddChild(patch);
            _patches[key] = (patch, stamp);
            RebuildCount++;
            return;
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
