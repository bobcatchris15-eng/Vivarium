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
    private double _sinceMeshBuild;
    private byte[] _cachedSubstrate = Array.Empty<byte>();
    private int _materialTerrainVersion = -1, _materialPropsVersion = -1;
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
        _strataMat.SetShaderParameter("layer_palette", ImageTexture.CreateFromImage(palette));
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
        // sculpting: follow the heightfield at up to ~12 rebuilds per second
        if (_w.Terrain.Version != _terrainVersion && _sinceMeshBuild > 0.08)
        {
            using (FrameProfiler.Measure("Island.MeshBuild"))
            {
                BuildMeshes();
                UpdateFieldTexture(true);
            }
        }
        using (FrameProfiler.Measure("Island.ShaderParams"))
        {
            if (OverlayMode != _lastOverlayMode)
            {
                _lastOverlayMode = OverlayMode;
                _terrainMat.SetShaderParameter("overlay_mode", OverlayMode);
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
        if (!propsDirty && _accum < 15.0) return;
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
            _bytes[o + 3] = (byte)(Mathf.Clamp((float)_w.FloraSystem.EffectiveLightCell(d), 0, 1) * 255);
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

            for (int sj = 0; sj < subNz; sj++)
            {
                for (int si = 0; si < subNx; si++)
                {
                    int so = (sj * subNx + si) * 4;
                    var p = new Vivarium.Sim.Core.Vec2(g.OriginX + (si + 0.5) * subStep, g.OriginZ + (sj + 0.5) * subStep);

                    // 1. _subBytes[so + 0] (Gravel/Rock mask): exact signed distance perturbed by 2-octave FBM noise
                    double minDist = double.MaxValue;
                    for (int gi = 0; gi < _w.Props.Gravel.Count; gi++)
                    {
                        var gr = _w.Props.Gravel[gi];
                        double d = Vivarium.Sim.Core.Vec2.Distance(p, gr.Position) - gr.Radius;
                        if (d < minDist) minDist = d;
                    }
                    for (int ri = 0; ri < _w.Props.Rocks.Count; ri++)
                    {
                        var rk = _w.Props.Rocks[ri];
                        double d = Vivarium.Sim.Core.Vec2.Distance(p, rk.Position) - rk.FootprintRadius;
                        if (d < minDist) minDist = d;
                    }
                    int cellD = g.CellAt(p);
                    if (cellD >= 0 && cellD < g.Count)
                    {
                        byte cCode = _cachedSubstrate[cellD];
                        if (cCode == (byte)Substrate.Gravel || cCode == (byte)Substrate.Rock)
                            minDist = Math.Min(minDist, -0.15);
                    }
                    double fbmNoise = Vivarium.Sim.Core.Noise.Fbm(0x501UL, p.X * 5.0, p.Z * 5.0, 2) * 0.08;
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

                    // 4. _subBytes[so + 3] (High-res contact AO mask): rock (6 cm), log (8 cm), root collar (12 cm)
                    double contact = 0;
                    for (int ti = 0; ti < trees.Count; ti++)
                    {
                        double dTr = Vivarium.Sim.Core.Vec2.Distance(p, trees[ti].Pos);
                        double rTr = trees[ti].Radius;
                        if (dTr < rTr + 0.12) contact = Math.Max(contact, 1.0 - Math.Max(0.0, dTr - rTr) / 0.12);
                    }
                    for (int li = 0; li < _w.Props.Logs.Count; li++)
                    {
                        var log = _w.Props.Logs[li];
                        double dLog = log.AxisDistance(p);
                        double rLog = log.Radius;
                        if (dLog < rLog + 0.08) contact = Math.Max(contact, 1.0 - Math.Max(0.0, dLog - rLog) / 0.08);
                    }
                    for (int ri = 0; ri < _w.Props.Rocks.Count; ri++)
                    {
                        var rk = _w.Props.Rocks[ri];
                        double dRk = Vivarium.Sim.Core.Vec2.Distance(p, rk.Position);
                        double rRk = rk.FootprintRadius;
                        if (dRk < rRk + 0.06) contact = Math.Max(contact, 1.0 - Math.Max(0.0, dRk - rRk) / 0.06);
                    }
                    _subBytes[so + 3] = (byte)Math.Clamp((int)Math.Round(contact * 255.0), 0, 255);
                }
            }
            _subImage.SetData(subNx, subNz, false, Image.Format.Rgba8, _subBytes);
            _subTex.Update(_subImage);
        }
    }
}

file static class HydrologyExtensions
{
    public static double HydraulicBed(this Vivarium.Sim.Water.Hydrology water, int idx) => water.Bed[idx];
}
