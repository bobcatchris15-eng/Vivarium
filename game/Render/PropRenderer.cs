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
    private int _version = int.MinValue, _terrainVersion;
    private double _sinceRefresh;
    private readonly Dictionary<ulong, ArrayMesh> _rockMeshes = new();
    private readonly Dictionary<EntityId, ArrayMesh> _logMeshes = new();
    private ArrayMesh[] _pebbles = System.Array.Empty<ArrayMesh>();
    private ShaderMaterial _rockMat = null!, _logMat = null!;
    private Node3D _propRoot = null!, _gravelRoot = null!;
    private readonly Dictionary<EntityId, MeshInstance3D> _placedRocks = new();
    private readonly Dictionary<EntityId, MeshInstance3D> _placedLogs = new();
    private int _gravelCount = -1;
    public int PebbleCount { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _version = int.MinValue;
        _terrainVersion = int.MinValue;
        if (_rockMat == null)
        {
            _rockMat = Bridge.Shader("res://Shaders/rock.gdshader");
            Bridge.BindSurface(_rockMat, "rock", Bridge.Surfaces.Rock);
            _logMat = Bridge.Shader("res://Shaders/log.gdshader");
            Bridge.BindSurface(_logMat, "bark", Bridge.Surfaces.Bark);
            Bridge.BindSurface(_logMat, "moss", Bridge.Surfaces.Moss);
            if (!IslandRenderer.CoverMaskConsumers.Contains(_logMat)) IslandRenderer.CoverMaskConsumers.Add(_logMat);
        }
        if (_pebbles.Length == 0)
        {
            var pebbleMat = (ShaderMaterial)_rockMat.Duplicate();
            pebbleMat.SetShaderParameter("lichen", 0.0f);
            pebbleMat.SetShaderParameter("tile_metres", 0.35f);
            _pebbles = new ArrayMesh[16];
            for (int i = 0; i < _pebbles.Length; i++) _pebbles[i] = Bridge.ToArrayMesh(PropMeshes.Pebble(1000 + (ulong)i * 7), pebbleMat);
        }
        _logMeshes.Clear();
        _placedRocks.Clear();
        _placedLogs.Clear();
        foreach (var c in GetChildren()) c.QueueFree();
        _propRoot = new Node3D { Name = "Props" };
        AddChild(_propRoot);
        _gravelRoot = new Node3D { Name = "Gravel" };
        AddChild(_gravelRoot);
        _gravelCount = -1;
        Refresh();
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Props");
        if (_w == null) return;
        _sinceRefresh += delta;
        // gravel pebbles sit on the terrain, so follow sculpting too (throttled)
        if (_w.Props.Version != _version || (_w.Terrain.Version != _terrainVersion && _sinceRefresh > 0.25)) Refresh();
    }

    private void Refresh()
    {
        _version = _w.Props.Version;
        _sinceRefresh = 0;
        RefreshProps();
        if (_w.Props.Gravel.Count != _gravelCount || _w.Terrain.Version != _terrainVersion)
        {
            _terrainVersion = _w.Terrain.Version;
            _gravelCount = _w.Props.Gravel.Count;
            RefreshGravel();
        }
    }

    private void RefreshProps()
    {
        var activeRockIds = new HashSet<EntityId>();
        foreach (var r in _w.Props.Rocks)
        {
            activeRockIds.Add(r.Id);
            var basis = Bridge.Yaw(r.RotationY).Scaled(new Vector3((float)r.SizeX, (float)r.SizeY, (float)r.SizeZ));
            var xform = new Transform3D(basis, new Vector3((float)r.X, (float)r.Y, (float)r.Z));
            if (_placedRocks.TryGetValue(r.Id, out var mi))
            {
                mi.Transform = xform;
            }
            else
            {
                if (!_rockMeshes.TryGetValue(r.VariantSeed, out var mesh))
                    _rockMeshes[r.VariantSeed] = mesh = Bridge.ToArrayMesh(PropMeshes.Rock(r.VariantSeed), _rockMat);
                var newMi = new MeshInstance3D { Name = $"Rock_{r.Id.Serial}", Mesh = mesh, Transform = xform };
                _placedRocks[r.Id] = newMi;
                _propRoot.AddChild(newMi);
            }
        }
        var removedRocks = new List<EntityId>();
        foreach (var (id, mi) in _placedRocks)
        {
            if (!activeRockIds.Contains(id))
            {
                mi.QueueFree();
                removedRocks.Add(id);
            }
        }
        foreach (var id in removedRocks) _placedRocks.Remove(id);

        var activeLogIds = new HashSet<EntityId>();
        foreach (var l in _w.Props.Logs)
        {
            activeLogIds.Add(l.Id);
            var xform = new Transform3D(Bridge.Yaw(l.RotationY), new Vector3((float)l.X, (float)l.Y, (float)l.Z));
            if (_placedLogs.TryGetValue(l.Id, out var mi))
            {
                mi.Transform = xform;
            }
            else
            {
                if (!_logMeshes.TryGetValue(l.Id, out var mesh)) _logMeshes[l.Id] = mesh = Bridge.ToArrayMesh(PropMeshes.Log(l), _logMat);
                var newMi = new MeshInstance3D { Name = $"Log_{l.Id.Serial}", Mesh = mesh, Transform = xform };
                _placedLogs[l.Id] = newMi;
                _propRoot.AddChild(newMi);
            }
        }
        var removedLogs = new List<EntityId>();
        foreach (var (id, mi) in _placedLogs)
        {
            if (!activeLogIds.Contains(id))
            {
                mi.QueueFree();
                removedLogs.Add(id);
            }
        }
        foreach (var id in removedLogs) _placedLogs.Remove(id);
    }

    private void RefreshGravel()
    {
        foreach (var c in _gravelRoot.GetChildren()) c.QueueFree();
        var byVariant = new List<Transform3D>[_pebbles.Length];
        for (int i = 0; i < byVariant.Length; i++) byVariant[i] = new List<Transform3D>();
        PebbleCount = 0;
        foreach (var g in _w.Props.Gravel)
            foreach (var p in PropMeshes.GravelScatter(_w, g))
            {
                var basis = Bridge.Yaw(p.RotationY)
                    * new Basis(Vector3.Right, (float)p.TiltX)
                    * new Basis(Vector3.Back, (float)p.TiltZ);
                basis = basis.Scaled(Bridge.V(p.Scale));
                byVariant[p.Variant].Add(new Transform3D(basis, Bridge.V(p.Position)));
                PebbleCount++;
            }
        for (int v = 0; v < _pebbles.Length; v++)
        {
            if (byVariant[v].Count == 0) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _pebbles[v], InstanceCount = byVariant[v].Count };
            for (int i = 0; i < byVariant[v].Count; i++) mm.SetInstanceTransform(i, byVariant[v][i]);
            _gravelRoot.AddChild(new MultiMeshInstance3D { Name = $"Gravel_{v}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }
}
