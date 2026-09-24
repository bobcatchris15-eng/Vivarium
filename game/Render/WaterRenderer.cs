using System;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Rebuilds the water surface + cut face from the authoritative depth field a few times per second.</summary>
public partial class WaterRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MeshInstance3D _mi = null!;
    private ArrayMesh _mesh = new();
    private ShaderMaterial _mat = null!;
    private double _accum = 999;
    public double RefreshSeconds { get; set; } = 0.5;
    public int TriangleCount { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _mat ??= Bridge.Shader("res://Shaders/water.gdshader");
        if (_mi == null) { _mi = new MeshInstance3D { Name = "Water", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(_mi); }
        Rebuild();
    }

    public void SetUnderwater(bool under) => _mat?.SetShaderParameter("underwater", under ? 1.0f : 0.0f);

    private double[] _builtDepth = System.Array.Empty<double>();
    private int _builtTerrain = -1;
    private double _sinceBuild;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Water");
        if (_w == null) return;
        _accum += delta; _sinceBuild += delta;
        if (_accum < RefreshSeconds) return;
        _accum = 0;
        // rebuilding is the expensive part: only do it when the water has visibly changed (ripples and flow
        // animate in the shader regardless), or every few seconds to pick up slow drift
        if (_builtTerrain == _w.Terrain.Version && (_sinceBuild < 3 || (_sinceBuild < 10 && !DepthChanged(0.005)))) return;
        Rebuild();
    }

    private bool DepthChanged(double threshold)
    {
        var d = _w.Water.Depth;
        if (_builtDepth.Length != d.Length) return true;
        foreach (int c in _w.Grid.DomainCells)
            if (Math.Abs(d[c] - _builtDepth[c]) > threshold) return true;   // margins flickering wet/dry by a millimetre don't count
        return false;
    }

    public void Rebuild()
    {
        if (_builtDepth.Length != _w.Water.Depth.Length) _builtDepth = new double[_w.Water.Depth.Length];
        Array.Copy(_w.Water.Depth, _builtDepth, _builtDepth.Length);
        _builtTerrain = _w.Terrain.Version;
        _sinceBuild = 0;
        var md = WaterMesh.Build(_w);
        TriangleCount = md.TriangleCount;
        Bridge.ToArrayMesh(md, _mat, _mesh);
        _mi.Mesh = _mesh;
    }
}
