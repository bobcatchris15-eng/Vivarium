using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Rocks, logs and gravel pebbles, rebuilt whenever the prop set's version changes.</summary>
public partial class PropRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private int _version = int.MinValue;
    private readonly Dictionary<ulong, ArrayMesh> _rockMeshes = new();
    private readonly Dictionary<EntityId, ArrayMesh> _logMeshes = new();
    private ArrayMesh[] _pebbles = System.Array.Empty<ArrayMesh>();
    private StandardMaterial3D _mat = null!;
    private Node3D _root = null!;
    public int PebbleCount { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _version = int.MinValue;
        _mat ??= Bridge.VertexColorMaterial(0.88f);
        if (_pebbles.Length == 0)
        {
            _pebbles = new ArrayMesh[6];
            for (int i = 0; i < _pebbles.Length; i++) _pebbles[i] = Bridge.ToArrayMesh(PropMeshes.Pebble(1000 + (ulong)i * 7), _mat);
        }
        _logMeshes.Clear();
        Refresh();
    }

    public override void _Process(double delta) { if (_w != null && _w.Props.Version != _version) Refresh(); }

    private void Refresh()
    {
        _version = _w.Props.Version;
        _root?.QueueFree();
        _root = new Node3D { Name = "Props" };
        AddChild(_root);
        foreach (var r in _w.Props.Rocks)
        {
            if (!_rockMeshes.TryGetValue(r.VariantSeed, out var mesh))
                _rockMeshes[r.VariantSeed] = mesh = Bridge.ToArrayMesh(PropMeshes.Rock(r.VariantSeed), _mat);
            var basis = Bridge.Yaw(r.RotationY).Scaled(new Vector3((float)r.SizeX, (float)r.SizeY, (float)r.SizeZ));
            _root.AddChild(new MeshInstance3D { Name = $"Rock_{r.Id.Serial}", Mesh = mesh, Transform = new Transform3D(basis, new Vector3((float)r.X, (float)r.Y, (float)r.Z)) });
        }
        foreach (var l in _w.Props.Logs)
        {
            if (!_logMeshes.TryGetValue(l.Id, out var mesh)) _logMeshes[l.Id] = mesh = Bridge.ToArrayMesh(PropMeshes.Log(l), _mat);
            _root.AddChild(new MeshInstance3D { Name = $"Log_{l.Id.Serial}", Mesh = mesh, Transform = new Transform3D(Bridge.Yaw(l.RotationY), new Vector3((float)l.X, (float)l.Y, (float)l.Z)) });
        }
        // gravel: render-only pebble instances (no simulation entities)
        var byVariant = new List<Transform3D>[_pebbles.Length];
        for (int i = 0; i < byVariant.Length; i++) byVariant[i] = new List<Transform3D>();
        PebbleCount = 0;
        foreach (var g in _w.Props.Gravel)
            foreach (var p in PropMeshes.GravelScatter(_w, g))
            {
                var s = (float)p.Scale;
                byVariant[p.Variant].Add(new Transform3D(Bridge.Yaw(p.RotationY).Scaled(new Vector3(s, s * 0.7f, s)), Bridge.V(p.Position)));
                PebbleCount++;
            }
        for (int v = 0; v < _pebbles.Length; v++)
        {
            if (byVariant[v].Count == 0) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _pebbles[v], InstanceCount = byVariant[v].Count };
            for (int i = 0; i < byVariant[v].Count; i++) mm.SetInstanceTransform(i, byVariant[v][i]);
            _root.AddChild(new MultiMeshInstance3D { Name = $"Gravel_{v}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }
}
