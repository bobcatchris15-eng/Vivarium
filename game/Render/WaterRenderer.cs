using System;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Rebuilds the spring-fed fluid surface from solver depth and velocity.
/// </summary>
public partial class WaterRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MeshInstance3D _miStream = null!;
    private ArrayMesh _meshStream = new();
    private ShaderMaterial _matStream = null!;
    private double _accum = 999;
    public double RefreshSeconds { get; set; } = 0.5;
    public int TriangleCount { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _matStream ??= Bridge.Shader("res://Shaders/water_stream.gdshader");
        if (_miStream == null)
        {
            _miStream = new MeshInstance3D { Name = "WaterStream", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_miStream);
        }
        Rebuild();
    }

    public void SetUnderwater(bool under)
    {
        float val = under ? 1.0f : 0.0f;
        _matStream?.SetShaderParameter("underwater", val);
    }

    private double[] _builtDepth = System.Array.Empty<double>();
    private int _builtTerrain = -1;
    private double _sinceBuild;
    private Task<WaterMeshSet>? _pendingSet;
    private WaterMeshSnapshot? _pendingSnapshot;
    private VivariumWorld? _pendingWorld;
    private int _pendingTerrainVersion;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Water");
        if (_w == null) return;
        if (_pendingSet != null)
        {
            if (!_pendingSet.IsCompleted) return;
            var completed = _pendingSet;
            _pendingSet = null;
            if (completed.IsCompletedSuccessfully && ReferenceEquals(_w, _pendingWorld) && _pendingSnapshot != null)
            {
                using (FrameProfiler.Measure("Water.Upload"))
                {
                    Bridge.ToArrayMesh(completed.Result.SurfaceMesh, _matStream, _meshStream);
                }
                _miStream.Mesh = _meshStream;
                TriangleCount = completed.Result.SurfaceMesh.TriangleCount;
                _builtQ = Discharge(_pendingSnapshot.FlowX, _pendingSnapshot.FlowZ);
                _builtDepth = _pendingSnapshot.Depth;
                _builtTerrain = _pendingTerrainVersion;
                _sinceBuild = 0;
            }
            else if (completed.IsFaulted)
                GD.PushError("Water geometry build failed: " + completed.Exception);
            _pendingSnapshot = null;
            _pendingWorld = null;
            return;
        }
        _accum += delta; _sinceBuild += delta;
        if (_accum < RefreshSeconds) return;
        _accum = 0;
        // rebuilding is the expensive part: only do it when the water has visibly changed (ripples and flow
        // animate in the shader regardless), or every few seconds to pick up slow drift
        if (_builtTerrain == _w.Terrain.Version && (_sinceBuild < 3 || (_sinceBuild < 10 && !DepthChanged(0.0002) && !FlowChanged()))) return;
        QueueRebuild();
    }

    private double[] _builtQ = System.Array.Empty<double>();

    private static double[] Discharge(double[] fx, double[] fz)
    {
        var q = new double[fx.Length];
        for (int i = 0; i < q.Length; i++) q[i] = Math.Sqrt(fx[i] * fx[i] + fz[i] * fz[i]);
        return q;
    }

    // Thin fluid changes also alter flow velocity; refresh when either depth or discharge changes.
    private bool FlowChanged()
    {
        var fx = _w.Water.FlowX; var fz = _w.Water.FlowZ;
        if (_builtQ.Length != fx.Length) return true;
        foreach (int c in _w.Grid.DomainCells)
        {
            double q = Math.Sqrt(fx[c] * fx[c] + fz[c] * fz[c]), b = _builtQ[c];
            bool on = q > 1e-7, was = b > 1e-7;
            if (on != was) return true;
            if (on && (q > b * 1.5 || q < b / 1.5)) return true;
        }
        return false;
    }

    private bool DepthChanged(double threshold)
    {
        var d = _w.Water.Depth;
        if (_builtDepth.Length != d.Length) return true;
        foreach (int c in _w.Grid.DomainCells)
            if (Math.Abs(d[c] - _builtDepth[c]) > threshold) return true;   // margins flickering wet/dry by a millimetre don't count
        return false;
    }

    private void QueueRebuild()
    {
        var world = _w;
        var snapshot = WaterMesh.Capture(world);
        _pendingWorld = world;
        _pendingSnapshot = snapshot;
        _pendingTerrainVersion = world.Terrain.Version;
        _sinceBuild = 0;
        // Pure C# geometry work never touches Godot objects. Upload happens in _Process.
        _pendingSet = Task.Run(() => WaterMesh.BuildSet(world, snapshot));
    }

    public void Rebuild()
    {
        _pendingSet = null;
        _pendingSnapshot = null;
        _pendingWorld = null;
        if (_builtDepth.Length != _w.Water.Depth.Length) _builtDepth = new double[_w.Water.Depth.Length];
        Array.Copy(_w.Water.Depth, _builtDepth, _builtDepth.Length);
        _builtTerrain = _w.Terrain.Version;
        _sinceBuild = 0;
        WaterMeshSet set;
        using (FrameProfiler.Measure("Water.Geometry")) set = WaterMesh.BuildSet(_w);
        TriangleCount = set.SurfaceMesh.TriangleCount;
        _builtQ = Discharge(_w.Water.FlowX, _w.Water.FlowZ);
        using (FrameProfiler.Measure("Water.Upload"))
        {
            Bridge.ToArrayMesh(set.SurfaceMesh, _matStream, _meshStream);
        }
        _miStream.Mesh = _meshStream;
    }
}
