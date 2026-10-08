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
    // material weights for blending (R = gravel, G = groundwater bed / silt, B = dynamic surface water), filtered smoothly;
    // water itself is ignored so shorelines don't interpolate through fake gravel/rock bands
    private Image _subImage = null!;
    private ImageTexture _subTex = null!;
    private byte[] _subBytes = System.Array.Empty<byte>();
    // Broad leaf-litter coverage is sourced from the simulation's fine surface-mass reservoir.
    private Image _litterImage = null!;
    private ImageTexture _litterTex = null!;
    private byte[] _litterBytes = System.Array.Empty<byte>();
    private Image _ambientImage = null!;
    private ImageTexture _ambientTex = null!;
    private byte[] _ambientBytes = System.Array.Empty<byte>();
    // Coverage-colony mask (Mat + Crust occupancy per field cell). terrain.gdshader and log.gdshader suppress their
    // procedural moss under/around real colonies so the species patches keep their own identity.
    private Image _coverImage = null!;
    private ImageTexture _coverTex = null!;
    private byte[] _coverBytes = System.Array.Empty<byte>();
    private int[] _coverCount = System.Array.Empty<int>();
    private Vector4 _fieldRect;
    /// <summary>Other materials (log bark) that read cover_tex/field_rect; refreshed with the field textures.</summary>
    public static readonly System.Collections.Generic.List<ShaderMaterial> CoverMaskConsumers = new();
    public int OverlayMode { get; set; }
    private ShaderMaterial _strataMat = null!;
    private MeshInstance3D _top = null!, _walls = null!;
    private int _terrainVersion;
    private int _propsVersion = int.MinValue;
    private double _sinceMeshBuild;
    public int TriangleCount { get; private set; }

    /// <summary>Depth (m) of standing water above which a cell is a waterway bed rather than merely damp soil.
    /// The same 4 mm cut AmbientGroundCoverRenderer already uses to keep plants out of a channel: below it the
    /// water is a film that only wets the surface, and the bed stays green.</summary>
    private const double SiltMinDepthM = 0.004;

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();

        _terrainMat = Bridge.Shader("res://Shaders/terrain.gdshader");
        Bridge.BindSurface(_terrainMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_terrainMat, "damp", Bridge.Surfaces.Damp);
        Bridge.BindSurface(_terrainMat, "moss", Bridge.Surfaces.Moss);
        Bridge.BindSurface(_terrainMat, "leaf", Bridge.Surfaces.Leaves);
        Bridge.BindSurface(_terrainMat, "gravel", Bridge.Surfaces.Gravel);
        var g = w.Grid;
        _terrainMat.SetShaderParameter("field_rect", new Vector4((float)g.OriginX, (float)g.OriginZ, (float)(g.Nx * g.CellSize), (float)(g.Nz * g.CellSize)));
        _terrainMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);
        _fieldImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.Rgba8);
        _fieldTex = ImageTexture.CreateFromImage(_fieldImage);
        _terrainMat.SetShaderParameter("field_tex", _fieldTex);
        _bytes = new byte[g.Nx * g.Nz * 4];
        _subImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.Rgba8);
        _subTex = ImageTexture.CreateFromImage(_subImage);
        _terrainMat.SetShaderParameter("sub_tex", _subTex);
        _subBytes = new byte[g.Nx * g.Nz * 4];
        _litterImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.R8);
        _litterTex = ImageTexture.CreateFromImage(_litterImage);
        _terrainMat.SetShaderParameter("litter_tex", _litterTex);
        _litterBytes = new byte[g.Count];
        _ambientImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.R8);
        _ambientTex = ImageTexture.CreateFromImage(_ambientImage);
        _terrainMat.SetShaderParameter("ambient_tex", _ambientTex);
        _ambientBytes = new byte[g.Count];
        _coverImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.R8);
        _coverTex = ImageTexture.CreateFromImage(_coverImage);
        _terrainMat.SetShaderParameter("cover_tex", _coverTex);
        _coverBytes = new byte[g.Count];
        _coverCount = new int[g.Count];
        _fieldRect = new Vector4((float)g.OriginX, (float)g.OriginZ, (float)(g.Nx * g.CellSize), (float)(g.Nz * g.CellSize));
        _nearestDomain = new int[g.Count];
        for (int c = 0; c < g.Count; c++) _nearestDomain[c] = g.InDomain(c) ? c : g.NearestDomainCell(g.CellCenter(c));

        _strataMat = Bridge.Shader("res://Shaders/strata.gdshader");
        Bridge.BindSurface(_strataMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_strataMat, "gravel", Bridge.Surfaces.Gravel);
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
        _terrainMat.SetShaderParameter("water_table", (float)_w.Water.WaterTable);
        bool propsDirty = _propsVersion != _w.Props.Version;
        if (!propsDirty && _accum < 1.5) return;
        _accum = 0;
        UpdateFieldTexture(true);
    }

    private void UpdateCoverMask()
    {
        var g = _w.Grid;
        System.Array.Clear(_coverCount);
        const double cs = Vivarium.Sim.Coverage.CoverageSpec.CellSize;
        foreach (var layer in new[] { _w.Coverage.Mat, _w.Coverage.Crust })
            foreach (var t in layer.Tiles)
            {
                int gx0 = t.Ti * Vivarium.Sim.Coverage.CoverageSpec.TileEdge, gz0 = t.Tj * Vivarium.Sim.Coverage.CoverageSpec.TileEdge;
                for (int k = 0; k < t.Occ.Length; k++)
                {
                    if (t.Occ[k] == 0) continue;
                    int lx = k % Vivarium.Sim.Coverage.CoverageSpec.TileEdge, lz = k / Vivarium.Sim.Coverage.CoverageSpec.TileEdge;
                    int c = g.CellAt(new Vivarium.Sim.Core.Vec2((gx0 + lx + 0.5) * cs, (gz0 + lz + 0.5) * cs));
                    if (c >= 0) _coverCount[c]++;
                }
            }
        // ~40 occupied 2 cm cells (a quarter of a 25 cm field cell) saturates the mask
        for (int c = 0; c < g.Count; c++) _coverBytes[c] = (byte)System.Math.Min(255, _coverCount[c] * 255 / 40);
        _coverImage.SetData(g.Nx, g.Nz, false, Image.Format.R8, _coverBytes);
        _coverTex.Update(_coverImage);
        foreach (var m in CoverMaskConsumers)
        {
            m.SetShaderParameter("cover_tex", _coverTex);
            m.SetShaderParameter("field_rect", _fieldRect);
        }
    }

    private void UpdateFieldTexture(bool updateSubstrate = true)
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
            _bytes[o + 3] = (byte)(Mathf.Clamp((float)_w.FloraSystem.EffectiveLightCell(d), 0, 1) * 255);
            _litterBytes[c] = (byte)(Mathf.Clamp((float)(1.0 - System.Math.Exp(-_w.Litter.FineMass[d] * 55.0)), 0, 1) * 255);
            _ambientBytes[c] = (byte)(Mathf.Clamp((float)_w.AmbientGroundCover.Cover[d], 0, 1) * 255);
            if (updateSubstrate)
            {
                bool gravel = _w.Props.GravelAt(p) != null;
                int so = c * 4;
                _subBytes[so] = gravel ? (byte)255 : (byte)0;
                // Green channel = groundwater bed mask (WaterTableDepth > SiltMinDepthM)
                // Blue channel = dynamic surface water (SurfaceWaterDepth > 0.0005)
                _subBytes[so + 1] = _w.Water.WaterTableDepth(d) > SiltMinDepthM ? (byte)255 : (byte)0;
                _subBytes[so + 2] = _w.Water.SurfaceWaterDepth(d) > 0.0005 ? (byte)255 : (byte)0;
                _subBytes[so + 3] = 0;
            }
        }
        _fieldImage.SetData(g.Nx, g.Nz, false, Image.Format.Rgba8, _bytes);
        _fieldTex.Update(_fieldImage);
        _litterImage.SetData(g.Nx, g.Nz, false, Image.Format.R8, _litterBytes);
        _litterTex.Update(_litterImage);
        _ambientImage.SetData(g.Nx, g.Nz, false, Image.Format.R8, _ambientBytes);
        _ambientTex.Update(_ambientImage);
        UpdateCoverMask();
        if (updateSubstrate)
        {
            _propsVersion = _w.Props.Version;
            _subImage.SetData(g.Nx, g.Nz, false, Image.Format.Rgba8, _subBytes);
            _subTex.Update(_subImage);
        }
    }
}
