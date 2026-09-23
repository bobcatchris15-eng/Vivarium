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

    public override void _Process(double delta)
    {
        if (_w == null) return;
        _accum += delta;
        if (_accum < RefreshSeconds) return;
        _accum = 0;
        Rebuild();
    }

    public void Rebuild()
    {
        var md = WaterMesh.Build(_w);
        TriangleCount = md.TriangleCount;
        Bridge.ToArrayMesh(md, _mat, _mesh);
        _mi.Mesh = _mesh;
    }
}
