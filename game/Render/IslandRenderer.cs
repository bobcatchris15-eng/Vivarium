using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Terrain top, strata cut faces and underside. Geometry is built once per world from authoritative terrain;
/// live habitat (moisture, substrate, nutrients, light) is streamed into a small texture the terrain shader
/// samples, so the ground visibly responds to the simulation without rebuilding meshes.
/// </summary>
public partial class IslandRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private ShaderMaterial _terrainMat = null!;
    private Image _fieldImage = null!;
    private ImageTexture _fieldTex = null!;
    private int[] _nearestDomain = System.Array.Empty<int>();
    private double _accum = 999;
    private byte[] _bytes = System.Array.Empty<byte>();
    // material weights for blending (R = gravel, G = exposed rock), filtered smoothly; water is ignored so
    // shorelines don't interpolate through fake gravel/rock bands
    private Image _subImage = null!;
    private ImageTexture _subTex = null!;
    private byte[] _subBytes = System.Array.Empty<byte>();
    public int OverlayMode { get; set; }
    private ShaderMaterial _strataMat = null!;
    private MeshInstance3D _top = null!, _walls = null!;
    private int _terrainVersion;
    private double _sinceMeshBuild;
    public int TriangleCount { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();

        _terrainMat = Bridge.Shader("res://Shaders/terrain.gdshader");
        Bridge.BindSurface(_terrainMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_terrainMat, "damp", Bridge.Surfaces.Damp);
        Bridge.BindSurface(_terrainMat, "moss", Bridge.Surfaces.Moss);
        Bridge.BindSurface(_terrainMat, "leaf", Bridge.Surfaces.Leaves);
        Bridge.BindSurface(_terrainMat, "rock", Bridge.Surfaces.Rock);
        Bridge.BindSurface(_terrainMat, "gravel", Bridge.Surfaces.Gravel);
        var g = w.Grid;
        _terrainMat.SetShaderParameter("field_rect", new Vector4((float)g.OriginX, (float)g.OriginZ, (float)(g.Nx * g.CellSize), (float)(g.Nz * g.CellSize)));
        _fieldImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.Rgba8);
        _fieldTex = ImageTexture.CreateFromImage(_fieldImage);
        _terrainMat.SetShaderParameter("field_tex", _fieldTex);
        _bytes = new byte[g.Nx * g.Nz * 4];
        _subImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.Rg8);
        _subTex = ImageTexture.CreateFromImage(_subImage);
        _terrainMat.SetShaderParameter("sub_tex", _subTex);
        _subBytes = new byte[g.Nx * g.Nz * 2];
        _nearestDomain = new int[g.Count];
        for (int c = 0; c < g.Count; c++) _nearestDomain[c] = g.InDomain(c) ? c : g.NearestDomainCell(g.CellCenter(c));

        _strataMat = Bridge.Shader("res://Shaders/strata.gdshader");
        Bridge.BindSurface(_strataMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_strataMat, "gravel", Bridge.Surfaces.Gravel);
        Bridge.BindSurface(_strataMat, "rock", Bridge.Surfaces.Rock);
        _top = new MeshInstance3D { Name = "TerrainTop" };
        _walls = new MeshInstance3D { Name = "StrataWalls" };
        AddChild(_top); AddChild(_walls);
        BuildMeshes();
        UpdateFieldTexture();
    }

    /// <summary>(Re)builds the top surface and cut faces from the current authoritative heightfield.</summary>
    private void BuildMeshes()
    {
        _terrainVersion = _w.Terrain.Version;
        _sinceMeshBuild = 0;
        var top = TerrainMesh.BuildTop(_w);
        var walls = TerrainMesh.BuildWalls(_w);
        TriangleCount = top.Mesh.TriangleCount + walls.TriangleCount;
        // reuse the same meshes: a fresh ArrayMesh per sculpt step (up to 12/s) left the old GPU buffers to the
        // GC finalizer, and over a long session the churn exhausted memory and crashed inside AddSurfaceFromArrays
        _top.Mesh = Bridge.ToArrayMesh(top.Mesh, _terrainMat, _top.Mesh as ArrayMesh);
        _walls.Mesh = Bridge.ToArrayMesh(walls, _strataMat, _walls.Mesh as ArrayMesh);
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Island");
        if (_w == null) return;
        _accum += delta;
        _sinceMeshBuild += delta;
        // sculpting: follow the heightfield at up to ~12 rebuilds per second
        if (_w.Terrain.Version != _terrainVersion && _sinceMeshBuild > 0.08) BuildMeshes();
        _terrainMat.SetShaderParameter("overlay_mode", OverlayMode);
        if (_accum < 0.5) return;
        _accum = 0;
        UpdateFieldTexture();
    }

    private void UpdateFieldTexture()
    {
        var g = _w.Grid; var f = _w.Fields;
        double nmax = _w.Content.Ecology.NutrientMax;
        for (int c = 0; c < g.Count; c++)
        {
            int d = _nearestDomain[c];
            if (d < 0) continue;
            var p = g.CellCenter(d);
            byte code = (byte)_w.SubstrateAtCell(p);
            if (code == (byte)Substrate.Wood) code = (byte)Substrate.Soil; // logs are drawn as meshes
            int o = c * 4;
            _bytes[o] = (byte)(Mathf.Clamp((float)f.Moisture.Values[d], 0, 1) * 255);
            _bytes[o + 1] = (byte)(code * 255 / 4);
            _bytes[o + 2] = (byte)(Mathf.Clamp((float)(f.Nutrients.Values[d] / nmax), 0, 1) * 255);
            _bytes[o + 3] = (byte)(Mathf.Clamp((float)f.Light.Values[d], 0, 1) * 255);
            bool gravel = _w.Props.GravelAt(p) != null;
            bool rock = !gravel && (Substrate)f.BaseSubstrate[d] == Substrate.Rock;
            _subBytes[c * 2] = gravel ? (byte)255 : (byte)0;
            _subBytes[c * 2 + 1] = rock ? (byte)255 : (byte)0;
        }
        _fieldImage.SetData(g.Nx, g.Nz, false, Image.Format.Rgba8, _bytes);
        _fieldTex.Update(_fieldImage);
        _subImage.SetData(g.Nx, g.Nz, false, Image.Format.Rg8, _subBytes);
        _subTex.Update(_subImage);
    }
}
