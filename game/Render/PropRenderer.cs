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
    private ShaderMaterial _pebbleMat = null!;
    private Node3D _propRoot = null!, _gravelRoot = null!;
    private readonly Dictionary<EntityId, MeshInstance3D> _placedRocks = new();
    private readonly Dictionary<EntityId, MeshInstance3D> _placedLogs = new();
    // Pebble MultiMeshes are created once per world and reused; a refresh only rewrites packed buffers.
    private MultiMeshInstance3D[] _gravelMmis = System.Array.Empty<MultiMeshInstance3D>();
    private MultiMesh[] _gravelMultimeshes = System.Array.Empty<MultiMesh>();
    private List<Transform3D>[] _gravelXf = System.Array.Empty<List<Transform3D>>();
    private float[][] _gravelBuffers = System.Array.Empty<float[]>();
    private bool _gravelNodesReady;
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
            Bridge.BindSurface(_logMat, "rotten_wood", Bridge.Surfaces.RottenWood);
        }
        if (_pebbleMat == null || _pebbles.Length == 0)
        {
            _pebbleMat = (ShaderMaterial)_rockMat.Duplicate();
            _pebbleMat.SetShaderParameter("lichen", 0.0f);
            _pebbleMat.SetShaderParameter("tile_metres", 0.35f);
            _pebbleMat.SetShaderParameter("pebble_mode", 1.0f);
            _pebbles = new ArrayMesh[32];
            for (int i = 0; i < _pebbles.Length; i++) _pebbles[i] = Bridge.ToArrayMesh(PropMeshes.Pebble(1000 + (ulong)i * 7), _pebbleMat);
        }
        _rockMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);
        _logMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);
        _pebbleMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);

        IslandRenderer.RegisterCoverMaskConsumer(_rockMat);
        IslandRenderer.RegisterCoverMaskConsumer(_logMat);
        IslandRenderer.RegisterCoverMaskConsumer(_pebbleMat);
        _logMeshes.Clear();
        _placedRocks.Clear();
        _placedLogs.Clear();
        _gravelNodesReady = false;
        foreach (var c in GetChildren()) c.QueueFree();
        _propRoot = new Node3D { Name = "Props" };
        AddChild(_propRoot);
        _gravelRoot = new Node3D { Name = "Gravel" };
        AddChild(_gravelRoot);
        BuildGravelNodes();
        _gravelCount = -1;
        Refresh();
        // Props register after Island.Build during world loading; bind immediately
        // rather than waiting for the island's periodic field refresh.
        if (GetParent() != null)
        {
            foreach (var sibling in GetParent().GetChildren())
                if (sibling is IslandRenderer island) island.RefreshMaterialBindings();
        }
    }


    public override void _ExitTree()
    {
        base._ExitTree();
        IslandRenderer.UnregisterCoverMaskConsumer(_rockMat);
        IslandRenderer.UnregisterCoverMaskConsumer(_logMat);
        IslandRenderer.UnregisterCoverMaskConsumer(_pebbleMat);
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
            var basis = Bridge.Yaw(r.RotationY).ScaledLocal(new Vector3((float)r.SizeX, (float)r.SizeY, (float)r.SizeZ));
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

    /// <summary>
    /// One MultiMesh per pebble variant, created once per world. 4-12 mm pebbles are past sub-pixel
    /// well inside the habitat's view distance, so they take no shadow-map triangles (the terrain
    /// contact mask and basal cavity shading stand in for their contact darkening) and fade out at
    /// 9.5 m instead of being drawn across the whole island.
    /// </summary>
    private void BuildGravelNodes()
    {
        int variants = _pebbles.Length;
        if (_gravelMmis.Length != variants)
        {
            _gravelMmis = new MultiMeshInstance3D[variants];
            _gravelMultimeshes = new MultiMesh[variants];
            _gravelXf = new List<Transform3D>[variants];
            _gravelBuffers = new float[variants][];
        }
        for (int v = 0; v < variants; v++)
        {
            if (_gravelXf[v] is null) _gravelXf[v] = new List<Transform3D>();
            else _gravelXf[v].Clear();
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false,
                Mesh = _pebbles[v],
            };
            var mmi = new MultiMeshInstance3D
            {
                Name = $"Gravel_{v}",
                Multimesh = mm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = 9.5f,
                VisibilityRangeEndMargin = 1.5f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
                Visible = false,
            };
            _gravelMultimeshes[v] = mm;
            _gravelMmis[v] = mmi;
            _gravelRoot.AddChild(mmi);
        }
        _gravelNodesReady = true;
    }

    /// <summary>
    /// Rebuilds pebble placement without touching the scene tree: 32 transform lists are refilled and
    /// each non-empty bucket becomes one 12-float-per-instance buffer upload instead of tens of
    /// thousands of SetInstanceTransform engine calls.
    /// </summary>
    private void RefreshGravel()
    {
        if (!_gravelNodesReady) return;
        for (int v = 0; v < _gravelXf.Length; v++) _gravelXf[v].Clear();
        PebbleCount = 0;
        foreach (var g in _w.Props.Gravel)
            foreach (var p in PropMeshes.GravelScatter(_w, g))
            {
                var basis = Bridge.Yaw(p.RotationY)
                    * new Basis(Vector3.Right, (float)p.TiltX)
                    * new Basis(Vector3.Back, (float)p.TiltZ);
                basis = basis.Scaled(Bridge.V(p.Scale));
                _gravelXf[p.Variant].Add(new Transform3D(basis, Bridge.V(p.Position)));
                PebbleCount++;
            }
        for (int v = 0; v < _gravelXf.Length; v++)
        {
            var list = _gravelXf[v];
            int n = list.Count;
            var mm = _gravelMultimeshes[v];
            var mmi = _gravelMmis[v];
            if (mm.InstanceCount != n) mm.InstanceCount = n;
            mm.VisibleInstanceCount = n;
            mmi.Visible = n > 0;
            if (n == 0) continue;
            var buf = _gravelBuffers[v];
            if (buf is null || buf.Length != n * 12) _gravelBuffers[v] = buf = new float[n * 12];
            for (int i = 0; i < n; i++)
            {
                var t = list[i];
                int o = i * 12;
                buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
                buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
                buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
            }
            mm.Buffer = buf;
        }
    }
}
