using System;
using System.Collections.Generic;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>Local contact with the instances actually drawn by FloraRenderer.
/// Plants bend about their roots and recover; no simulation population is moved.</summary>
public partial class GuardianVegetationContact : Node
{
    private sealed class Plant
    {
        public MultiMeshInstance3D Draw = null!;
        public int Index;
        public Transform3D Base, Applied;
        public Vector3 Bend, Target;
        public Aabb Bounds;
    }
    private readonly Dictionary<(ulong, int), Plant> _plants = new();
    private IEnumerator<(MultiMeshInstance3D, int)>? _scan;
    public FloraRenderer? Flora { get; set; }
    public Vector3 Focus { get; set; }
    public int ContactCount { get; private set; }
    public float MaximumBend { get; private set; }
    public int CachedPlantCount => _plants.Count;
    public override void _ExitTree()
    {
        _scan?.Dispose();
        foreach (var plant in _plants.Values)
            if (GodotObject.IsInstanceValid(plant.Draw) && plant.Draw.Multimesh != null && plant.Index < plant.Draw.Multimesh.InstanceCount
                && plant.Draw.Multimesh.GetInstanceTransform(plant.Index).IsEqualApprox(plant.Applied))
                plant.Draw.Multimesh.SetInstanceTransform(plant.Index, plant.Base);
    }
    public void Register(MultiMeshInstance3D draw, int index)
    {
        if (draw.Multimesh?.Mesh == null || index >= draw.Multimesh.InstanceCount) return;
        var key = (draw.GetInstanceId(), index);
        var transform = draw.Multimesh.GetInstanceTransform(index);
        if (!_plants.TryGetValue(key, out var plant))
        {
            if (_plants.Count >= 128)
            {
                var incoming = (draw.GlobalTransform * transform).Origin.DistanceSquaredTo(Focus);
                (ulong, int) farthest = default; float distance = incoming;
                foreach (var (candidate, existing) in _plants)
                {
                    if (!GodotObject.IsInstanceValid(existing.Draw)) { farthest = candidate; distance = float.PositiveInfinity; break; }
                    float d = (existing.Draw.GlobalTransform * existing.Base).Origin.DistanceSquaredTo(Focus);
                    if (d > distance) { distance = d; farthest = candidate; }
                }
                if (distance <= incoming) return;
                var removed = _plants[farthest];
                if (GodotObject.IsInstanceValid(removed.Draw) && removed.Draw.Multimesh != null && removed.Index < removed.Draw.Multimesh.InstanceCount
                    && removed.Draw.Multimesh.GetInstanceTransform(removed.Index).IsEqualApprox(removed.Applied))
                    removed.Draw.Multimesh.SetInstanceTransform(removed.Index, removed.Base);
                _plants.Remove(farthest);
            }
            _plants[key] = plant = new Plant { Draw = draw, Index = index, Base = transform, Applied = transform };
        }
        else if (!transform.IsEqualApprox(plant.Applied)) plant.Base = transform;
        plant.Bounds = (draw.GlobalTransform * plant.Base) * draw.Multimesh.Mesh.GetAabb();
    }
    public override void _Process(double delta)
    {
        ContactCount = 0; MaximumBend = 0;
        if (Flora != null)
        {
            _scan ??= Flora.GuardianContactInstances().GetEnumerator();
            for (int i = 0; i < 256; i++)
            {
                if (!_scan.MoveNext()) { _scan.Dispose(); _scan = null; break; }
                var (draw, index) = _scan.Current;
                if (!GodotObject.IsInstanceValid(draw) || draw.Multimesh?.Mesh == null || index >= draw.Multimesh.InstanceCount) continue;
                var t = draw.GlobalTransform * draw.Multimesh.GetInstanceTransform(index);
                if (t.Origin.DistanceSquaredTo(Focus) > 16) continue;
                var bounds = t * draw.Multimesh.Mesh.GetAabb();
                // Ground mats and large tree trunks are not flexible shrubs.
                if (bounds.Size.Y < .16f || bounds.Size.Y > 2.5f) continue;
                Register(draw, index);
            }
        }
        var expired = new List<(ulong, int)>();
        foreach (var (key, plant) in _plants)
        {
            if (!GodotObject.IsInstanceValid(plant.Draw) || plant.Draw.Multimesh?.Mesh == null || plant.Index >= plant.Draw.Multimesh.InstanceCount)
            { expired.Add(key); continue; }
            var current = plant.Draw.Multimesh.GetInstanceTransform(plant.Index);
            if (!current.IsEqualApprox(plant.Applied)) plant.Base = current; // Population/LOD refresh owns the base.
            float blend = 1 - MathF.Exp(-(float)Math.Clamp(delta, 0, .1) * (plant.Target.LengthSquared() > 0 ? 14 : 4));
            plant.Bend = plant.Bend.Lerp(plant.Target, blend);
            if (plant.Target.LengthSquared() > 0) ContactCount++;
            MaximumBend = Math.Max(MaximumBend, plant.Bend.Length());
            var root = plant.Draw.GlobalTransform * plant.Base;
            var axis = Vector3.Up.Cross(plant.Bend);
            var rotation = axis.LengthSquared() < 1e-8f ? Basis.Identity : new Basis(axis.Normalized(), plant.Bend.Length());
            root.Basis = rotation * root.Basis;
            plant.Applied = plant.Draw.GlobalTransform.AffineInverse() * root;
            plant.Draw.Multimesh.SetInstanceTransform(plant.Index, plant.Applied);
            plant.Bounds = root * plant.Draw.Multimesh.Mesh.GetAabb();
            plant.Target = Vector3.Zero;
            if (root.Origin.DistanceSquaredTo(Focus) > 25 && plant.Bend.LengthSquared() < 1e-6f)
            { plant.Draw.Multimesh.SetInstanceTransform(plant.Index, plant.Base); expired.Add(key); }
        }
        foreach (var key in expired) _plants.Remove(key);
    }
    public Vector3 Resolve(Vector3 point, float radius, bool flexible)
    {
        foreach (var plant in _plants.Values)
        {
            var box = plant.Bounds;
            var closest = new Vector3(Math.Clamp(point.X, box.Position.X, box.End.X),
                Math.Clamp(point.Y, box.Position.Y, box.End.Y), Math.Clamp(point.Z, box.Position.Z, box.End.Z));
            var separation = point - closest;
            if (separation.LengthSquared() >= radius * radius) continue;
            var away = box.GetCenter() - point; away.Y = 0;
            if (away.LengthSquared() < 1e-6f) away = Vector3.Right;
            float overlap = radius - separation.Length();
            var requested = away.Normalized() * Math.Min(.38f, overlap * 2.5f);
            if (requested.LengthSquared() > plant.Target.LengthSquared()) plant.Target = requested;
            if (!flexible) continue;
            // A narrow woody core resists appendages; foliage yields visibly.
            var root = (plant.Draw.GlobalTransform * plant.Base).Origin;
            var lateral = point - root; lateral.Y = 0;
            float stemRadius = Math.Min(.045f, box.Size.X * .10f) + radius;
            if (point.Y < root.Y + box.Size.Y * .65f && lateral.Length() < stemRadius)
            {
                if (lateral.LengthSquared() < 1e-8f) lateral = -away.Normalized();
                point += lateral.Normalized() * (stemRadius - lateral.Length());
            }
        }
        return point;
    }
}

public partial class FloraRenderer
{
    internal IEnumerable<(MultiMeshInstance3D, int)> GuardianContactInstances()
    {
        // Snapshot draw nodes so an incremental scan survives population rebuilds.
        var draws = new List<MultiMeshInstance3D>();
        foreach (var layer in _layers.Values)
            foreach (var variant in layer.Variants)
            {
                if (variant?.Full != null) draws.Add(variant.Full);
                if (variant?.Juvenile != null) draws.Add(variant.Juvenile);
                if (variant?.Fruit != null) draws.Add(variant.Fruit);
            }
        foreach (var draw in draws)
        {
            if (!GodotObject.IsInstanceValid(draw) || draw.Multimesh == null) continue;
            int count = draw.Multimesh.VisibleInstanceCount < 0 ? draw.Multimesh.InstanceCount : draw.Multimesh.VisibleInstanceCount;
            for (int index = 0; index < count; index++) yield return (draw, index);
        }
    }
}
