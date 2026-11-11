using System;
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
    private ImageTexture? _paletteTex;
    // Prop-only sub-texel caches (keyed by Props.Version + sub-grid size).
    // _propGravelSub: signed distance (m) from the sub-texel centre to the nearest gravel/rock edge.
    // _propContactSub: basal contact occlusion (0..1) from rocks and logs.
    private double[] _propGravelSub = System.Array.Empty<double>(), _propFbm = System.Array.Empty<double>(), _propContactSub = System.Array.Empty<double>();
    private int _propCacheVersion = int.MinValue, _propCacheLen = -1;
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
    /// <summary>Share the live habitat image; updates change colour without rebuilding consumers' geometry.</summary>
    public void BindHabitatMaterial(ShaderMaterial material)
    {
        material.SetShaderParameter("field_tex", _fieldTex);
        material.SetShaderParameter("field_rect", _fieldRect);
    }
    /// <summary>Other materials (log bark) that read cover_tex/field_rect; refreshed with the field textures.</summary>
    public static readonly System.Collections.Generic.List<ShaderMaterial> CoverMaskConsumers = new();
    public int OverlayMode { get; set; }
    private int _lastOverlayMode = -1;
    private float _lastWaterTable = float.NaN;
    public static void RegisterCoverMaskConsumer(ShaderMaterial mat)
    {
        if (mat != null && !CoverMaskConsumers.Contains(mat))
            CoverMaskConsumers.Add(mat);
    }
    public static void UnregisterCoverMaskConsumer(ShaderMaterial mat)
    {
        if (mat != null)
            CoverMaskConsumers.Remove(mat);
    }
    public void RefreshMaterialBindings()
    {
        foreach (var m in CoverMaskConsumers)
        {
            if (m == null) continue;
            m.SetShaderParameter("cover_tex", _coverTex);
            m.SetShaderParameter("field_rect", _fieldRect);
            m.SetShaderParameter("water_table", (float)_w.Water.WaterTable);
        }
    }
    private ShaderMaterial _strataMat = null!;
    private MeshInstance3D _top = null!, _walls = null!;
    private int _terrainVersion;
    private int _propsVersion = int.MinValue;
    // Water-driven sub-texel channels (G silt, B stream wetness) refresh on liquid/water-table change, <= 2 Hz.
    private long _subWaterLiquidVersion = long.MinValue;
    private double _subWaterTable = double.NaN;
    private double _sinceSubWater = 999;
    private const double SubWaterInterval = 0.5;
    private double _sinceMeshBuild;
    private byte[] _cachedSubstrate = Array.Empty<byte>();
    private int _materialTerrainVersion = -1, _materialPropsVersion = -1;
    /// <summary>Habitat field refresh period (s). Moisture, litter and moss/silt read as live state rather than
    /// stepping in 40 s jumps; the cost is one 16k-cell sweep plus three texture uploads every 2 s.</summary>
    private const double FieldRefreshInterval = 2.0;
    /// <summary>Set when a sculpt already spent the frame on a terrain mesh rebuild, so the habitat image
    /// rebuild is deferred a frame instead of landing on the same one.</summary>
    private bool _pendingFieldRefresh;
    public long GravelMaskBuilds { get; private set; }
    public long GravelMaskReuses { get; private set; }
    public long GravelMaskAuditedPixels { get; private set; }
    public long GravelMaskAuditDifferences { get; private set; }
    private void CacheSubstrate()
    {
        if (_cachedSubstrate.Length == _w.Grid.Count && _materialTerrainVersion == _w.Terrain.Version && _materialPropsVersion == _w.Props.Version) return;
        _cachedSubstrate = new byte[_w.Grid.Count];
        foreach (int c in _w.Grid.DomainCells)
        {
            var p = _w.Grid.CellCenter(c);
            _cachedSubstrate[c] = (byte)(_w.PilotTree?.BlocksDisc(p) == true ? Substrate.Wood
                : _w.Props.RockAt(p) != null ? Substrate.Rock : _w.Props.LogAt(p) != null ? Substrate.Wood
                : _w.Props.GravelAt(p) != null ? Substrate.Gravel : (Substrate)_w.Fields.BaseSubstrate[c]);
        }
        _materialTerrainVersion = _w.Terrain.Version; _materialPropsVersion = _w.Props.Version;
    }
    public int TriangleCount { get; private set; }

    /// <summary>Depth (m) of standing water above which a cell is a waterway bed rather than merely damp soil.
    /// The same 4 mm cut AmbientGroundCoverRenderer already uses to keep plants out of a channel: below it the
    /// water is a film that only wets the surface, and the bed stays green.</summary>
    private const double SiltMinDepthM = 0.004;

    public void Build(VivariumWorld w)
    {
        _w = w;
        _materialTerrainVersion = _materialPropsVersion = -1;
        _propCacheVersion = int.MinValue; _propCacheLen = -1;
        _pendingFieldRefresh = false;
        foreach (var c in GetChildren()) c.QueueFree();

        _terrainMat = Bridge.Shader("res://Shaders/terrain.gdshader");
        Bridge.BindSurface(_terrainMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_terrainMat, "damp", "Ground037");
        Bridge.BindSurface(_terrainMat, "moss", Bridge.Surfaces.Moss);
        Bridge.BindSurface(_terrainMat, "leaf", "ScatteredLeaves007");
        Bridge.BindSurface(_terrainMat, "gravel", Bridge.Surfaces.Gravel);
        var g = w.Grid;
        int subNx = g.Nx * 4, subNz = g.Nz * 4;
        _terrainMat.SetShaderParameter("field_rect", new Vector4((float)g.OriginX, (float)g.OriginZ, (float)(g.Nx * g.CellSize), (float)(g.Nz * g.CellSize)));
        _terrainMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);
        _fieldImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.Rgba8);
        _fieldTex = ImageTexture.CreateFromImage(_fieldImage);
        _terrainMat.SetShaderParameter("field_tex", _fieldTex);
        _bytes = new byte[g.Nx * g.Nz * 4];
        _subImage = Image.CreateEmpty(subNx, subNz, false, Image.Format.Rgba8);
        _subTex = ImageTexture.CreateFromImage(_subImage);
        _terrainMat.SetShaderParameter("sub_tex", _subTex);
        _subBytes = new byte[subNx * subNz * 4];
        _litterImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.R8);
        _litterTex = ImageTexture.CreateFromImage(_litterImage);
        _terrainMat.SetShaderParameter("litter_tex", _litterTex);
        _litterBytes = new byte[g.Count];
        _ambientImage = Image.CreateEmpty(g.Nx, g.Nz, false, Image.Format.R8);
        _ambientTex = ImageTexture.CreateFromImage(_ambientImage);
        _terrainMat.SetShaderParameter("ambient_tex", _ambientTex);
        _ambientBytes = new byte[g.Count];
        _coverImage = Image.CreateEmpty(subNx, subNz, false, Image.Format.R8);
        _coverTex = ImageTexture.CreateFromImage(_coverImage);
        _terrainMat.SetShaderParameter("cover_tex", _coverTex);
        _coverBytes = new byte[subNx * subNz];
        _coverCount = new int[subNx * subNz];
        _fieldRect = new Vector4((float)g.OriginX, (float)g.OriginZ, (float)(g.Nx * g.CellSize), (float)(g.Nz * g.CellSize));
        _nearestDomain = new int[g.Count];
        for (int c = 0; c < g.Count; c++) _nearestDomain[c] = g.InDomain(c) ? c : g.NearestDomainCell(g.CellCenter(c));

        _strataMat = Bridge.Shader("res://Shaders/strata.gdshader");
        Bridge.BindSurface(_strataMat, "soil", Bridge.Surfaces.Soil);
        Bridge.BindSurface(_strataMat, "gravel", Bridge.Surfaces.Gravel);
        Bridge.BindSurface(_strataMat, "rock", Bridge.Surfaces.Rock);
        // The cut faces map their U coordinate around the whole hex perimeter; an integer repeat count
        // keeps the loop seam texel-continuous at the first corner.
        _strataMat.SetShaderParameter("perimeter_metres", (float)(6.0 * w.Domain.Radius));
        _strataMat.SetShaderParameter("water_table", (float)w.Water.WaterTable);
        BindStrataPalette(w);
        _top = new MeshInstance3D { Name = "TerrainTop" };
        _walls = new MeshInstance3D { Name = "StrataWalls" };
        AddChild(_top); AddChild(_walls);
        BuildMeshes();
        UpdateFieldTexture();
    }

    private void BindStrataPalette(VivariumWorld world)
    {
        var layers = world.Strata.Layers;
        // Art-direct the shipped illustrative palette only. Custom palettes and
        // arbitrary layer counts retain their authored colours, including saves.
        string[] ids = { "topsoil", "subsoil", "gravel_clay", "bedrock" };
        double[][] original = { new[] { .24, .15, .09 }, new[] { .63, .43, .22 },
            new[] { .60, .38, .30 }, new[] { .46, .47, .50 } };
        Color[] natural = { new(.34f, .26f, .19f), new(.48f, .36f, .24f),
            new(.43f, .39f, .32f), new(.37f, .38f, .36f) };
        bool canonical = layers.Count == ids.Length;
        for (int i = 0; canonical && i < layers.Count; i++)
        {
            canonical &= layers[i].Id == ids[i];
            for (int c = 0; c < 3; c++) canonical &= Math.Abs(layers[i].Color[c] - original[i][c]) < 1e-6;
        }
        using var palette = Image.CreateEmpty(layers.Count, 1, false, Image.Format.Rgba8);
        for (int i = 0; i < layers.Count; i++)
        {
            var col = layers[i].Color;
            palette.SetPixel(i, 0, canonical ? natural[i] : new Color((float)col[0], (float)col[1], (float)col[2]));
        }
        if (_paletteTex != null && _paletteTex.GetWidth() == layers.Count) _paletteTex.Update(palette);
        else _paletteTex = ImageTexture.CreateFromImage(palette);
        _strataMat.SetShaderParameter("layer_palette", _paletteTex);
        _strataMat.SetShaderParameter("layer_count", layers.Count);
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
        _sinceSubWater += delta;
        // A sculpt currently pays for a 157k-vertex mesh rebuild in this frame; the 16k-cell habitat
        // image rebuild waits one frame so the two never stall the same frame together.
        if (_pendingFieldRefresh)
        {
            _pendingFieldRefresh = false;
            using (FrameProfiler.Measure("Island.FieldTexture"))
                UpdateFieldTexture(true);
            _accum = 0;
        }
        // sculpting: follow the heightfield at up to ~12 rebuilds per second
        if (_w.Terrain.Version != _terrainVersion && _sinceMeshBuild > 0.08)
        {
            using (FrameProfiler.Measure("Island.MeshBuild"))
                BuildMeshes();
            _pendingFieldRefresh = true;
        }
        using (FrameProfiler.Measure("Island.ShaderParams"))
        {
            if (OverlayMode != _lastOverlayMode)
            {
                _lastOverlayMode = OverlayMode;
                _terrainMat.SetShaderParameter("overlay_mode", OverlayMode);
                _accum = 999; // light channel switches between ecology and appearance values
            }
            float wt = (float)_w.Water.WaterTable;
            if (wt != _lastWaterTable)
            {
                _lastWaterTable = wt;
                _terrainMat.SetShaderParameter("water_table", wt);
                _strataMat.SetShaderParameter("water_table", wt);
            }
        }
        bool propsDirty = _propsVersion != _w.Props.Version;
        if (!propsDirty && _sinceSubWater >= SubWaterInterval
            && (_w.Liquid.Version != _subWaterLiquidVersion || _w.Water.WaterTable != _subWaterTable))
        {
            using (FrameProfiler.Measure("Island.SubWater"))
                UpdateSubWaterChannels();
        }
        if (!propsDirty && _accum < FieldRefreshInterval) return;
        _accum = 0;
        using (FrameProfiler.Measure("Island.FieldTexture"))
            UpdateFieldTexture(updateSubstrate: propsDirty);
    }

    private void UpdateCoverMask()
    {
        using var pMask = FrameProfiler.Measure("Island.CoverMask");
        var g = _w.Grid;
        int subNx = g.Nx * 4, subNz = g.Nz * 4;
        if (_coverCount.Length != subNx * subNz)
        {
            _coverCount = new int[subNx * subNz];
            _coverBytes = new byte[subNx * subNz];
        }
        System.Array.Clear(_coverCount);
        const double cs = Vivarium.Sim.Coverage.CoverageSpec.CellSize;
        double subCs = g.CellSize / 4.0;
        foreach (var layer in new[] { _w.Coverage.Mat, _w.Coverage.Crust })
            foreach (var t in layer.Tiles)
            {
                if (t.IsEmpty()) continue;
                int gx0 = t.Ti * Vivarium.Sim.Coverage.CoverageSpec.TileEdge, gz0 = t.Tj * Vivarium.Sim.Coverage.CoverageSpec.TileEdge;
                for (int k = 0; k < t.Occ.Length; k++)
                {
                    if (t.Occ[k] == 0) continue;
                    int lx = k % Vivarium.Sim.Coverage.CoverageSpec.TileEdge, lz = k / Vivarium.Sim.Coverage.CoverageSpec.TileEdge;
                    double px = (gx0 + lx + 0.5) * cs;
                    double pz = (gz0 + lz + 0.5) * cs;
                    int si = (int)Math.Floor((px - g.OriginX) / subCs);
                    int sj = (int)Math.Floor((pz - g.OriginZ) / subCs);
                    if (si >= 0 && si < subNx && sj >= 0 && sj < subNz)
                        _coverCount[sj * subNx + si]++;
                }
            }
        // ~40 occupied 2 cm cells in 25 cm cell -> scaled to 4x sub-cell (~6.25 cm texel)
        for (int k = 0; k < _coverBytes.Length; k++)
            _coverBytes[k] = (byte)System.Math.Min(255, _coverCount[k] * 255 / 3);
        _coverImage.SetData(subNx, subNz, false, Image.Format.R8, _coverBytes);
        _coverTex.Update(_coverImage);
        foreach (var m in CoverMaskConsumers)
        {
            m.SetShaderParameter("cover_tex", _coverTex);
            m.SetShaderParameter("field_rect", _fieldRect);
        }
    }

    /// <summary>Ramp width (m) of the gravel/rock edge mask; includes the ±FBM dither so the bbox
    /// stamping below can never clip a texel that would have shown any cover.</summary>
    private const double PropMaskMargin = 0.16;
    private const double PropLogContactMargin = 0.08;
    private const double PropRockContactMargin = 0.06;

    /// <summary>Clamped sub-cell index box covering a world-space disc.</summary>
    private static void SubCellRange(double cx, double cz, double radius, double originX, double originZ,
        double step, int subNx, int subNz, out int si0, out int si1, out int sj0, out int sj1)
    {
        si0 = Math.Max(0, (int)Math.Floor((cx - radius - originX) / step));
        si1 = Math.Min(subNx - 1, (int)Math.Floor((cx + radius - originX) / step));
        sj0 = Math.Max(0, (int)Math.Floor((cz - radius - originZ) / step));
        sj1 = Math.Min(subNz - 1, (int)Math.Floor((cz + radius - originZ) / step));
    }

    /// <summary>
    /// Rebuilds the sub-texel prop masks. Each gravel patch, rock and log stamps only the clamped
    /// (si, sj) bounding box its influence radius reaches, so the cost is proportional to the area
    /// props actually occupy rather than 16,384 x (gravel + rocks + logs) distance tests.
    /// </summary>
    private void RebuildPropCaches(int subNx, int subNz, double subStep)
    {
        var g = _w.Grid;
        int n = subNx * subNz;
        if (_propGravelSub.Length != n)
        {
            _propGravelSub = new double[n];
            _propFbm = new double[n];
            _propContactSub = new double[n];
        }
        // The distance mask needs a large positive "no prop nearby" sentinel; Array.Clear's zero would
        // read as a surface running through every texel once the FBM dither is added.
        System.Array.Fill(_propGravelSub, double.MaxValue);
        System.Array.Clear(_propContactSub);

        // FBM dither is prop-independent: one full sweep, then all prop work is bbox-stamped.
        for (int sj = 0; sj < subNz; sj++)
        {
            double z = g.OriginZ + (sj + 0.5) * subStep;
            int row = sj * subNx;
            for (int si = 0; si < subNx; si++)
                _propFbm[row + si] = Vivarium.Sim.Core.Noise.Fbm(0x501UL, (g.OriginX + (si + 0.5) * subStep) * 5.0, z * 5.0, 2) * 0.08;
        }

        foreach (var gr in _w.Props.Gravel)
        {
            double r = gr.Radius;
            SubCellRange(gr.X, gr.Z, r + PropMaskMargin, g.OriginX, g.OriginZ, subStep, subNx, subNz,
                out int si0, out int si1, out int sj0, out int sj1);
            for (int sj = sj0; sj <= sj1; sj++)
            {
                double dz = g.OriginZ + (sj + 0.5) * subStep - gr.Z;
                int row = sj * subNx;
                for (int si = si0; si <= si1; si++)
                {
                    double dx = g.OriginX + (si + 0.5) * subStep - gr.X;
                    double d = Math.Sqrt(dx * dx + dz * dz) - r;
                    int k = row + si;
                    if (d < _propGravelSub[k]) _propGravelSub[k] = d;
                }
            }
        }

        foreach (var rk in _w.Props.Rocks)
        {
            double r = rk.FootprintRadius;
            SubCellRange(rk.X, rk.Z, r + PropMaskMargin, g.OriginX, g.OriginZ, subStep, subNx, subNz,
                out int si0, out int si1, out int sj0, out int sj1);
            for (int sj = sj0; sj <= sj1; sj++)
            {
                double dz = g.OriginZ + (sj + 0.5) * subStep - rk.Z;
                int row = sj * subNx;
                for (int si = si0; si <= si1; si++)
                {
                    double dx = g.OriginX + (si + 0.5) * subStep - rk.X;
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    int k = row + si;
                    double edge = d - r;
                    if (edge < _propGravelSub[k]) _propGravelSub[k] = edge;
                    if (d < r + PropRockContactMargin)
                    {
                        double contact = 1.0 - Math.Max(0.0, d - r) / PropRockContactMargin;
                        if (contact > _propContactSub[k]) _propContactSub[k] = contact;
                    }
                }
            }
        }

        foreach (var log in _w.Props.Logs)
        {
            double r = log.Radius;
            SubCellRange(log.X, log.Z, log.FootprintRadius + PropLogContactMargin, g.OriginX, g.OriginZ, subStep,
                subNx, subNz, out int si0, out int si1, out int sj0, out int sj1);
            for (int sj = sj0; sj <= sj1; sj++)
            {
                double z = g.OriginZ + (sj + 0.5) * subStep;
                int row = sj * subNx;
                for (int si = si0; si <= si1; si++)
                {
                    var p = new Vivarium.Sim.Core.Vec2(g.OriginX + (si + 0.5) * subStep, z);
                    double dLog = log.AxisDistance(p);
                    if (dLog < r + PropLogContactMargin)
                    {
                        double contact = 1.0 - Math.Max(0.0, dLog - r) / PropLogContactMargin;
                        int k = row + si;
                        if (contact > _propContactSub[k]) _propContactSub[k] = contact;
                    }
                }
            }
        }
    }

    /// <summary>Habitat light for surface appearance: the ecology value without the pilot-tree canopy term,
    /// whose shade on the ground already comes from the real-time shadow map. Woody shrub shade is kept.</summary>
    private double AppearanceLight(int cell, Vivarium.Sim.Core.Vec2 p)
    {
        double light = _w.Fields.Light.Values[cell];
        var tree = _w.PilotTree;
        if (tree != null)
        {
            double canopy = 1 - tree.Def.ShadeOpacity * tree.CanopyInfluence(p);
            if (canopy > 0.01) light = Math.Min(1.0, light / canopy);
        }
        double shade = _w.FloraSystem.CanopyShadeCell(cell);
        return Math.Max(0, Math.Min(1, light - Math.Min(shade, light)));
    }

    private void UpdateFieldTexture(bool updateSubstrate = true)
    {
        CacheSubstrate();
        var g = _w.Grid; var f = _w.Fields;
        double nmax = _w.Content.Ecology.NutrientMax;
        var trees = new System.Collections.Generic.List<(Vivarium.Sim.Core.Vec2 Pos, double Radius)>();
        if (updateSubstrate)
        {
            foreach (var plant in _w.Flora.Items)
            {
                var sp = _w.Content.FloraOrThrow(plant.SpeciesId);
                if (sp.Shape == "tree" || sp.Tags.Contains("tree") || sp.Tags.Contains("woody"))
                    trees.Add((plant.Position, Math.Max(0.20, plant.Radius(sp) * 0.40)));
            }
        }
        _w.FloraSystem.RefreshCanopyShade();
        for (int c = 0; c < g.Count; c++)
        {
            int d = _nearestDomain[c];
            if (d < 0) continue;
            var p = g.CellCenter(d);
            byte code = _cachedSubstrate[d];
            if (code != (byte)Substrate.Rock && code != (byte)Substrate.Wood && _w.Water.IsWet(d)) code = (byte)Substrate.Water;
            if (code == (byte)Substrate.Wood) code = (byte)Substrate.Soil; // logs are drawn as meshes
            int o = c * 4;
            _bytes[o] = (byte)(Mathf.Clamp((float)f.Moisture.Values[d], 0, 1) * 255);
            _bytes[o + 1] = (byte)(code * 255 / 4);
            _bytes[o + 2] = (byte)(Mathf.Clamp((float)(f.Nutrients.Values[d] / nmax), 0, 1) * 255);
            _bytes[o + 3] = (byte)(Mathf.Clamp((float)(OverlayMode == 3 ? _w.FloraSystem.EffectiveLightCell(d) : AppearanceLight(d, p)), 0, 1) * 255);
            _litterBytes[c] = (byte)(Mathf.Clamp((float)(1.0 - System.Math.Exp(-_w.Litter.FineMass[d] * 55.0)), 0, 1) * 255);
            _ambientBytes[c] = (byte)(Mathf.Clamp((float)_w.AmbientGroundCover.Cover[d], 0, 1) * 255);
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
            int subNx = g.Nx * 4, subNz = g.Nz * 4;
            double subStep = g.CellSize / 4.0;
            if (_subBytes.Length != subNx * subNz * 4) _subBytes = new byte[subNx * subNz * 4];
            if (_propCacheVersion != _w.Props.Version || _propCacheLen != subNx * subNz)
            {
                RebuildPropCaches(subNx, subNz, subStep);
                _propCacheVersion = _w.Props.Version; _propCacheLen = subNx * subNz;
            }

            for (int sj = 0; sj < subNz; sj++)
            {
                for (int si = 0; si < subNx; si++)
                {
                    int so = (sj * subNx + si) * 4;
                    var p = new Vivarium.Sim.Core.Vec2(g.OriginX + (si + 0.5) * subStep, g.OriginZ + (sj + 0.5) * subStep);

                    // 1. _subBytes[so + 0] (Gravel/Rock mask): cached prop distance + FBM
                    double minDist = _propGravelSub[sj * subNx + si];
                    int cellD = g.CellAt(p);
                    {
                        // Bilinear (cell-centre) blend of the binary gravel/rock cell mask: replaces the
                        // hard -0.15 step at cell borders with a ramp so outlines round off instead of stair-stepping.
                        double fx = (p.X - g.OriginX) / g.CellSize - 0.5, fz = (p.Z - g.OriginZ) / g.CellSize - 0.5;
                        int ci = (int)Math.Floor(fx), cj = (int)Math.Floor(fz);
                        double tx = fx - ci, tz = fz - cj;
                        double m00 = GravelCell(g, ci, cj), m10 = GravelCell(g, ci + 1, cj);
                        double m01 = GravelCell(g, ci, cj + 1), m11 = GravelCell(g, ci + 1, cj + 1);
                        double m = (m00 * (1 - tx) + m10 * tx) * (1 - tz) + (m01 * (1 - tx) + m11 * tx) * tz;
                        double s = 0.15 * Math.Clamp(2.0 * (m - 0.42), -1.0, 1.0);
                        minDist = Math.Min(minDist, -s);
                    }
                    double fbmNoise = _propFbm[sj * subNx + si];
                    double gravSigned = -(minDist + fbmNoise);
                    _subBytes[so + 0] = (byte)Math.Clamp((int)Math.Round(Math.Clamp(gravSigned / 0.08 + 0.5, 0.0, 1.0) * 255.0), 0, 255);

                    // 2. _subBytes[so + 1] (Silt bed mask): smooth interpolation of WaterTableDepth(p) > SiltMinDepthM
                    double wtDepth = _w.Water.WaterTableDepth(p);
                    double siltFrac = Math.Clamp((wtDepth - SiltMinDepthM) / 0.015, 0.0, 1.0);
                    _subBytes[so + 1] = (byte)Math.Clamp((int)Math.Round(siltFrac * 255.0), 0, 255);

                    // 3. _subBytes[so + 2] (Dynamic stream wetness mask): active liquid depth at p
                    double streamDepth = _w.Water.SurfaceWaterDepth(p);
                    if (streamDepth > 0.0005)
                    {
                        double surfY = _w.Terrain.Height(p) + streamDepth;
                        double surfAboveTable = Math.Max(0.0, surfY - _w.Water.WaterTable);
                        _subBytes[so + 2] = (byte)Math.Clamp((int)Math.Round(surfAboveTable * 100.0 + 1.0), 1, 255);
                    }
                    else
                    {
                        _subBytes[so + 2] = 0;
                    }

                    // 4. _subBytes[so + 3] (High-res contact AO mask): rock (6 cm), log (8 cm), root collar (12 cm),
                    // plus a subtle basal term where a dense litter/loam bed rests on the soil.
                    double contact = _propContactSub[sj * subNx + si];
                    if (cellD >= 0 && cellD < g.Count)
                        contact = Math.Max(contact,
                            0.40 * (1.0 - System.Math.Exp(-Math.Max(0.0, _w.Litter.FineMass[cellD]) * 55.0)));
                    for (int ti = 0; ti < trees.Count; ti++)
                    {
                        double dTr = Vivarium.Sim.Core.Vec2.Distance(p, trees[ti].Pos);
                        double rTr = trees[ti].Radius;
                        if (dTr < rTr + 0.12) contact = Math.Max(contact, 1.0 - Math.Max(0.0, dTr - rTr) / 0.12);
                    }
                    _subBytes[so + 3] = (byte)Math.Clamp((int)Math.Round(contact * 255.0), 0, 255);
                }
            }
            _subImage.SetData(subNx, subNz, false, Image.Format.Rgba8, _subBytes);
            _subTex.Update(_subImage);
            MarkSubWaterFresh();
        }
    }

    private double GravelCell(Vivarium.Sim.Fields.GridSpec g, int i, int j)
    {
        if (i < 0 || j < 0 || i >= g.Nx || j >= g.Nz) return 0.0;
        byte c = _cachedSubstrate[j * g.Nx + i];
        return (c == (byte)Substrate.Gravel || c == (byte)Substrate.Rock) ? 1.0 : 0.0;
    }

    private void MarkSubWaterFresh()
    {
        _subWaterLiquidVersion = _w.Liquid.Version;
        _subWaterTable = _w.Water.WaterTable;
        _sinceSubWater = 0;
    }

    /// <summary>Rewrites only sub-texel G (silt, from water-table depth) and B (stream wetness); R/A stay prop-cached.</summary>
    private void UpdateSubWaterChannels()
    {
        var g = _w.Grid;
        int subNx = g.Nx * 4, subNz = g.Nz * 4;
        if (_subBytes.Length != subNx * subNz * 4 || _propCacheLen != subNx * subNz) { MarkSubWaterFresh(); return; }
        double subStep = g.CellSize / 4.0;
        double table = _w.Water.WaterTable;
        for (int sj = 0; sj < subNz; sj++)
            for (int si = 0; si < subNx; si++)
            {
                int so = (sj * subNx + si) * 4;
                var p = new Vivarium.Sim.Core.Vec2(g.OriginX + (si + 0.5) * subStep, g.OriginZ + (sj + 0.5) * subStep);
                double wtDepth = _w.Water.WaterTableDepth(p);
                double siltFrac = Math.Clamp((wtDepth - SiltMinDepthM) / 0.015, 0.0, 1.0);
                _subBytes[so + 1] = (byte)Math.Clamp((int)Math.Round(siltFrac * 255.0), 0, 255);
                double streamDepth = _w.Water.SurfaceWaterDepth(p);
                if (streamDepth > 0.0005)
                {
                    double surfY = _w.Terrain.Height(p) + streamDepth;
                    double surfAboveTable = Math.Max(0.0, surfY - table);
                    _subBytes[so + 2] = (byte)Math.Clamp((int)Math.Round(surfAboveTable * 100.0 + 1.0), 1, 255);
                }
                else _subBytes[so + 2] = 0;
            }
        _subImage.SetData(subNx, subNz, false, Image.Format.Rgba8, _subBytes);
        _subTex.Update(_subImage);
        MarkSubWaterFresh();
    }
}
