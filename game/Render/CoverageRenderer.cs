using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Rules;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Continuous terrain-conforming renderer for moss (Mat layer) and lichen (Crust layer).
/// Tracks active tiles in both layers, draping elevated meshes over occupied cells with
/// smoothly feathered edges, vertex colors modulated by physiology, and incremental rebuilding.
/// </summary>
public partial class CoverageRenderer : Node3D
{
    private static readonly bool DebugMode = System.Environment.GetEnvironmentVariable("VIVARIUM_COVERAGE_DEBUG") == "1";

    private VivariumWorld _w = null!;
    private ShaderMaterial _matMaterial = null!;
    private ShaderMaterial _crustMaterial = null!;
    private StandardMaterial3D _debugMatMaterial = null!;
    private StandardMaterial3D _debugCrustMaterial = null!;

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;

    /// <summary>Diagnostics for reference/perf capture (updated every SyncTiles).</summary>
    public int TileCount => _tiles.Count;
    public int TrianglesBuilt { get; private set; } // cumulative triangles built this session (diagnostic only)
    public int InstanceCount => GetChildCount();

    private sealed class TileRecord
    {
        public long Version = -1;
        public MeshInstance3D MeshInstance = null!;
        public ArrayMesh Mesh = null!;
    }

    private sealed class SpeciesRenderInfo
    {
        public Color Color1;
        public Color Color2;
        public MatHeightForm HeightForm;
        public double MaxHeightM;
        public double DomeLength;
    }

    private readonly Dictionary<(CoverageLayerId layer, int ti, int tj), TileRecord> _tiles = new();
    private readonly Dictionary<byte, SpeciesRenderInfo> _matSpecies = new();
    private readonly Dictionary<byte, SpeciesRenderInfo> _crustSpecies = new();

    private static readonly SpeciesRenderInfo _defaultSpecies = new()
    {
        Color1 = new Color(0.25f, 0.55f, 0.20f),
        Color2 = new Color(0.35f, 0.65f, 0.25f),
        HeightForm = MatHeightForm.Flat,
        MaxHeightM = 0.003,
        DomeLength = 5.0,
    };

    // Pre-allocated scratch buffers to eliminate per-step/per-tile allocations
    private readonly bool[,] _cellOccupied = new bool[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly float[,] _cellThickness = new float[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly Color[,] _cellColor = new Color[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly int[,] _cornerIdx = new int[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly int[,] _hMidIdx = new int[CoverageSpec.TileEdge, CoverageSpec.TileEdge + 1];
    private readonly int[,] _vMidIdx = new int[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge];
    private readonly float[,] _cornerThickness = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly Color[,] _cornerColor = new Color[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly float[,] _cornerAlpha = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly List<(CoverageLayerId layer, int ti, int tj)> _toRemove = new();

    private Vec3 GetGroundNormal(double wx, double wz)
    {
        const double step = CoverageSpec.CellSize * 0.5;
        double hL = _w.GroundHeight(new Vec2(wx - step, wz));
        double hR = _w.GroundHeight(new Vec2(wx + step, wz));
        double hD = _w.GroundHeight(new Vec2(wx, wz - step));
        double hU = _w.GroundHeight(new Vec2(wx, wz + step));
        double dx = (hR - hL) / (2.0 * step);
        double dz = (hU - hD) / (2.0 * step);
        double invLen = 1.0 / Math.Sqrt(dx * dx + 1.0 + dz * dz);
        return new Vec3((float)(-dx * invLen), (float)invLen, (float)(-dz * invLen));
    }

    private static float SlopeFade(Vec3 normal)
    {
        // A height field cannot drape around a vertical log edge. Thin the coverage as its
        // sampled surface approaches that discontinuity instead of leaving triangular curtains.
        float u = Math.Clamp((float)((normal.Y - 0.12) / 0.4), 0f, 1f);
        return u * u * (3f - 2f * u);
    }

    private static float ComputeThickness(SpeciesRenderInfo sp, CoverageLayerId layerId, float b, byte w, byte d2e, byte flags)
    {
        float bNorm = Math.Clamp(b, 0.1f, 1.0f);
        float th;

        if (layerId == CoverageLayerId.Mat)
        {
            switch (sp.HeightForm)
            {
                case MatHeightForm.Wet:
                {
                    // Sphagnum / wetland moss produces distinct rounded dome profiles modulated by moisture
                    double l = sp.DomeLength > 0 ? sp.DomeLength : 6.0;
                    double u = Math.Clamp((d2e + 0.5) / l, 0.0, 1.0);
                    double domeProfile = Math.Sin(Math.PI * 0.5 * u);
                    double wetFactor = 0.4 + 0.6 * (w / 255.0);
                    th = (float)(sp.MaxHeightM * bNorm * wetFactor * domeProfile);
                    break;
                }
                case MatHeightForm.Dome:
                {
                    // Cushion moss produces distinct rounded dome profiles
                    double l = sp.DomeLength > 0 ? sp.DomeLength : 5.0;
                    double u = Math.Clamp((d2e + 0.5) / l, 0.0, 1.0);
                    double domeProfile = Math.Sin(Math.PI * 0.5 * u);
                    th = (float)(sp.MaxHeightM * bNorm * domeProfile);
                    break;
                }
                case MatHeightForm.Flat:
                default:
                {
                    // Carpet moss has 2-4 mm visible thickness with rounded edge falloff
                    double uEdge = Math.Clamp((d2e + 0.5) / 2.0, 0.0, 1.0);
                    double edgeFactor = uEdge * uEdge * (3.0 - 2.0 * uEdge);
                    th = (float)(sp.MaxHeightM * bNorm * edgeFactor);
                    break;
                }
            }
        }
        else
        {
            // Crustose/foliose lichen has ~0.5 mm thickness with smooth edge falloff
            double uEdge = Math.Clamp((d2e + 0.5) / 1.5, 0.0, 1.0);
            double edgeFactor = uEdge * uEdge * (3.0 - 2.0 * uEdge);
            th = (float)(sp.MaxHeightM * bNorm * edgeFactor);
            if ((flags & (byte)CoverageFlags.Boundary) != 0)
            {
                th *= 0.35f;
            }
        }

        return th < 0f ? 0f : th;
    }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _tiles.Clear();

        _matMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _matMaterial.SetShaderParameter("u_pattern", 0.0f); // fibrous moss micro-detail
        Bridge.BindSurface(_matMaterial, "moss", Bridge.Surfaces.Moss);
        _crustMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _crustMaterial.SetShaderParameter("u_pattern", 1.0f); // cracked/areolate lichen micro-detail
        Bridge.BindSurface(_crustMaterial, "moss", Bridge.Surfaces.Moss);
        _crustMaterial.SetShaderParameter("lichen_col", GD.Load<Texture2D>("res://Textures/LichenThallus.png"));
        if (DebugMode)
        {
            _debugMatMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Colors.Magenta,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            _debugCrustMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Colors.Cyan,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }

        InitSpecies();
        SyncTiles();
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Coverage");
        if (_w == null || Quality <= 0)
        {
            if (Visible) Visible = false;
            return;
        }
        if (!Visible) Visible = true;

        SyncTiles();
    }

    private void InitSpecies()
    {
        _matSpecies.Clear();
        _crustSpecies.Clear();
        if (_w == null) return;

        byte matId = 0, lichenId = 0;
        foreach (var sp in _w.Content.Flora)
        {
            if (sp.Mat is { } md)
            {
                matId++;
                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.2f, 0.6f, 0.2f);
                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : c1;
                double maxH;
                if (md.HeightForm == MatHeightForm.Flat)
                {
                    // Carpet moss has 2-4 mm visible thickness
                    maxH = md.MaxHeightM is >= 0.002 and <= 0.004 ? md.MaxHeightM : 0.003;
                }
                else
                {
                    // Cushion / sphagnum moss produces distinct rounded dome profiles
                    maxH = md.MaxHeightM > 0 ? md.MaxHeightM : (sp.Height > 0 ? sp.Height : 0.025);
                }
                _matSpecies[matId] = new SpeciesRenderInfo
                {
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = md.HeightForm,
                    MaxHeightM = maxH,
                    DomeLength = md.DomeLength > 0 ? md.DomeLength : 5.0,
                };
            }
            if (sp.Lichen is { } ld)
            {
                lichenId++;
                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.75f, 0.75f, 0.55f);
                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : c1;
                // The sheet has its own relief below; this is the substrate-to-sheet gap.
                double maxH = ld.Form == LichenForm.Fruticose
                    ? (sp.Height > 0 ? sp.Height : 0.008)
                    : 0.0005;
                _crustSpecies[lichenId] = new SpeciesRenderInfo
                {
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = MatHeightForm.Flat,
                    MaxHeightM = maxH,
                    DomeLength = 3.0,
                };
            }
        }
    }

    private SpeciesRenderInfo GetSpecies(CoverageLayerId layerId, byte occ)
    {
        var dict = layerId == CoverageLayerId.Mat ? _matSpecies : _crustSpecies;
        if (dict.TryGetValue(occ, out var info)) return info;
        return _defaultSpecies;
    }

    private void SyncTiles()
    {
        if (_w == null) return;

        // Discard MeshInstance3D nodes for tiles that become empty or are freed
        _toRemove.Clear();
        foreach (var (key, record) in _tiles)
        {
            var layer = _w.Coverage.ById(key.layer);
            if (!layer.TryGetTile(key.ti, key.tj, out var tile) || tile == null || tile.IsEmpty())
            {
                _toRemove.Add(key);
            }
        }
        for (int i = 0; i < _toRemove.Count; i++)
        {
            var key = _toRemove[i];
            if (_tiles.TryGetValue(key, out var record))
            {
                record.MeshInstance.QueueFree();
                RemoveChild(record.MeshInstance);
                _tiles.Remove(key);
            }
        }

        // Check tile versions and rebuild dirty / new tiles
        CheckLayer(_w.Coverage.Mat);
        CheckLayer(_w.Coverage.Crust);
    }

    private void CheckLayer(CoverageLayer layer)
    {
        foreach (var t in layer.Tiles)
        {
            if (t.IsEmpty()) continue;

            var key = (layer.Id, t.Ti, t.Tj);
            if (_tiles.TryGetValue(key, out var record))
            {
                if (t.Version > record.Version)
                {
                    RebuildTile(layer, t, record);
                }
            }
            else
            {
                CreateTile(layer, t, key);
            }
        }
    }

    private void CreateTile(CoverageLayer layer, CoverageTile t, (CoverageLayerId layer, int ti, int tj) key)
    {
        var md = BuildTileMesh(layer, t);
        if (md.VertexCount == 0 || md.TriangleCount == 0) return;

        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)
            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);
        var mesh = Bridge.ToArrayMesh(md, mat);
        var mi = new MeshInstance3D
        {
            Name = $"Coverage_{layer.Id}_{t.Ti}_{t.Tj}",
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mi);
        _tiles[key] = new TileRecord
        {
            Version = t.Version,
            Mesh = mesh,
            MeshInstance = mi,
        };
        TrianglesBuilt += md.TriangleCount;
    }

    private void RebuildTile(CoverageLayer layer, CoverageTile t, TileRecord record)
    {
        var md = BuildTileMesh(layer, t);
        if (md.VertexCount == 0 || md.TriangleCount == 0)
        {
            record.MeshInstance.QueueFree();
            RemoveChild(record.MeshInstance);
            _tiles.Remove((layer.Id, t.Ti, t.Tj));
            return;
        }

        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)
            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);
        record.Mesh = Bridge.ToArrayMesh(md, mat, record.Mesh);
        record.MeshInstance.Mesh = record.Mesh;
        record.MeshInstance.MaterialOverride = mat;
        record.Version = t.Version;
        TrianglesBuilt += md.TriangleCount;
    }

    private static uint ReliefHash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093 ^ z * 19349663);
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return h;
        }
    }

    private static float ValueNoise(double x, double z, double spacing)
    {
        double fx = x / spacing, fz = z / spacing;
        int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
        float u = (float)(fx - ix), v = (float)(fz - iz);
        u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
        static float Unit(uint h) => (h & 65535) / 65535f;
        float a = Mathf.Lerp(Unit(ReliefHash(ix, iz)), Unit(ReliefHash(ix + 1, iz)), u);
        float b = Mathf.Lerp(Unit(ReliefHash(ix, iz + 1)), Unit(ReliefHash(ix + 1, iz + 1)), u);
        return Mathf.Lerp(a, b, v);
    }

    private static float SurfaceRelief(CoverageLayerId layer, double wx, double wz, float alpha, double maxHeight)
    {
        if (alpha <= 0) return 0;
        if (layer == CoverageLayerId.Crust)
        {
            // Two nonaligned wavelengths create continuous, crinkled thallus folds. The rim curls up.
            float broad = Math.Abs(ValueNoise(wx + 0.013, wz, 0.052) - 0.5f) * 2f;
            float fine = Math.Abs(ValueNoise(wx, wz + 0.021, 0.019) - 0.5f) * 2f;
            float fold = broad * 0.7f + fine * 0.3f;
            float edge = MathF.Sqrt(alpha);
            return edge * (0.002f + fold * 0.018f + (1f - alpha) * 0.012f);
        }

        // Overlapping rounded pillows are sampled in world coordinates, so the shape merges across
        // 32-cell tile seams. The low continuous base fills the valleys between adjacent mounds.
        const double spacing = 0.09;
        int ix = (int)Math.Floor(wx / spacing), iz = (int)Math.Floor(wz / spacing);
        float pillow = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = ix + dx, cz = iz + dz;
            uint h = ReliefHash(cx, cz);
            double mx = (cx + 0.5 + ((h & 255) / 255.0 - 0.5) * 0.36) * spacing;
            double mz = (cz + 0.5 + (((h >> 8) & 255) / 255.0 - 0.5) * 0.36) * spacing;
            double radius = spacing * (1.08 + (((h >> 16) & 255) / 255.0) * 0.30);
            double d2 = ((wx - mx) * (wx - mx) + (wz - mz) * (wz - mz)) / (radius * radius);
            if (d2 >= 1) continue;
            float cap = (float)((1 - d2) * (1 - d2));
            pillow += cap;
        }
        float height = Math.Clamp((float)maxHeight, 0.014f, 0.028f);
        float mergedPillows = 1f - MathF.Exp(-pillow * 0.9f);
        return alpha * height * (0.3f + mergedPillows * 0.7f);
    }

    private MeshData BuildTileMesh(CoverageLayer layer, CoverageTile t)
    {
        const int edge = CoverageSpec.TileEdge;
        const double cs = CoverageSpec.CellSize;
        float baseOffset = layer.Id == CoverageLayerId.Crust ? 0.002f : 0.0035f;

        // 1. Compute per-cell thickness and vertex color
        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            int li = lz * edge + lx;
            byte occ = t.Occ[li];
            if (occ == 0)
            {
                _cellOccupied[lx, lz] = false;
                _cellThickness[lx, lz] = 0f;
                _cellColor[lx, lz] = Colors.Black;
                continue;
            }

            _cellOccupied[lx, lz] = true;
            var sp = GetSpecies(layer.Id, occ);
            float b = t.B[li];
            byte w = t.W[li];
            byte dorm = t.Dorm[li];
            byte flags = t.Flags[li];
            byte d2e = t.D2E[li];

            float th = ComputeThickness(sp, layer.Id, b, w, d2e, flags);
            _cellThickness[lx, lz] = th;

            // Vertex colors derive from species Color and Color2 modulated by biomass, dormancy (browning), and health
            float bNorm = Mathf.Clamp(b, 0f, 1f);
            Color col = sp.Color2.Lerp(sp.Color1, bNorm);
            col = col * (0.8f + 0.2f * bNorm);

            float dormRatio = dorm / 255f;
            if (dormRatio > 0.005f)
            {
                Color brown = new Color(0.48f, 0.36f, 0.18f);
                col = col.Lerp(brown, dormRatio * 0.85f);
            }

            bool isDead = (flags & (byte)CoverageFlags.Dead) != 0;
            if (isDead)
            {
                Color deadCol = new Color(0.26f, 0.22f, 0.17f);
                col = col.Lerp(deadCol, 0.9f);
            }
            else if (w < 40 && dorm == 0)
            {
                float stress = (40f - w) / 40f;
                col = col.Lerp(new Color(0.50f, 0.45f, 0.25f), stress * 0.35f);
            }

            // Rim cells are younger growth: brighter and slightly less saturated toward Color2.
            bool isRim = (flags & (byte)CoverageFlags.Rim) != 0;
            if (isRim && !isDead)
            {
                col = col.Lerp(sp.Color2, 0.4f);
                col *= 1.12f;
            }

            // Boundary (prothallus) cells render as a thin dark line.
            bool isBoundary = (flags & (byte)CoverageFlags.Boundary) != 0;
            if (isBoundary)
            {
                Color lineCol = new Color(0.08f, 0.08f, 0.07f);
                col = col.Lerp(lineCol, 0.75f);
            }

            col.A = 1f;
            _cellColor[lx, lz] = col;
        }

        // 2. Evaluate 33x33 corner grid for continuity and edge feathering
        for (int cz = 0; cz <= edge; cz++)
        for (int cx = 0; cx <= edge; cx++)
        {
            _cornerIdx[cx, cz] = -1;
            int occCount = 0;
            float sumTh = 0f;
            float sumR = 0f, sumG = 0f, sumB = 0f;

            for (int oz = -1; oz <= 0; oz++)
            for (int ox = -1; ox <= 0; ox++)
            {
                int nlx = cx + ox;
                int nlz = cz + oz;
                if (nlx >= 0 && nlx < edge && nlz >= 0 && nlz < edge)
                {
                    if (_cellOccupied[nlx, nlz])
                    {
                        occCount++;
                        sumTh += _cellThickness[nlx, nlz];
                        var c = _cellColor[nlx, nlz];
                        sumR += c.R; sumG += c.G; sumB += c.B;
                    }
                }
                else
                {
                    int gx = t.Ti * edge + nlx;
                    int gz = t.Tj * edge + nlz;
                    byte nOcc = layer.GetOcc(gx, gz);
                    if (nOcc != 0)
                    {
                        occCount++;
                        var nsp = GetSpecies(layer.Id, nOcc);
                        float nb = layer.GetB(gx, gz);
                        int nTi = t.Ti + (nlx < 0 ? -1 : (nlx >= edge ? 1 : 0));
                        int nTj = t.Tj + (nlz < 0 ? -1 : (nlz >= edge ? 1 : 0));
                        int clx = (nlx % edge + edge) % edge;
                        int clz = (nlz % edge + edge) % edge;
                        int cli = clz * edge + clx;
                        byte nw = 128, nd2e = 1, nflags = 0;
                        if (layer.TryGetTile(nTi, nTj, out var nTile) && nTile != null)
                        {
                            nw = nTile.W[cli];
                            nd2e = nTile.D2E[cli];
                            nflags = nTile.Flags[cli];
                        }
                        float nth = ComputeThickness(nsp, layer.Id, nb, nw, nd2e, nflags);
                        sumTh += nth;
                        sumR += nsp.Color1.R; sumG += nsp.Color1.G; sumB += nsp.Color1.B;
                    }
                }
            }

            if (occCount == 0)
            {
                _cornerThickness[cx, cz] = 0f;
                _cornerColor[cx, cz] = Colors.Black;
                _cornerAlpha[cx, cz] = 0f;
            }
            else
            {
                float inv = 1.0f / occCount;
                Color avgCol = new Color(sumR * inv, sumG * inv, sumB * inv, 1.0f);
                float alpha = occCount / 4.0f;
                _cornerAlpha[cx, cz] = alpha;
                // Smooth rounded edge falloff for corner thickness
                float falloff = alpha * alpha * (3f - 2f * alpha);
                _cornerThickness[cx, cz] = (sumTh * inv) * falloff;
                _cornerColor[cx, cz] = avgCol;
            }
        }

        for (int cz = 0; cz <= edge; cz++)
        for (int cx = 0; cx < edge; cx++)
        {
            _hMidIdx[cx, cz] = -1;
        }
        for (int cz = 0; cz < edge; cz++)
        for (int cx = 0; cx <= edge; cx++)
        {
            _vMidIdx[cx, cz] = -1;
        }

        // 3. Assemble MeshData draped over terrain. Each cell is subdivided into a 2x2 grid of
        // sub-quads whose corners are bilinearly interpolated from the cell's 4 true corners, so
        // color/height/alpha vary smoothly across the cell instead of faceting at cell boundaries.
        var md = new MeshData();

        int GetOrAddCorner(int cx, int cz)
        {
            int idx = _cornerIdx[cx, cz];
            if (idx >= 0) return idx;

            double wx = (t.Ti * edge + cx) * cs;
            double wz = (t.Tj * edge + cz) * cs;
            double gy = _w.GroundHeight(new Vec2(wx, wz));
            Vec3 gn = GetGroundNormal(wx, wz);
            float th = _cornerThickness[cx, cz];
            byte occ = layer.GetOcc(t.Ti * edge + cx, t.Tj * edge + cz);
            var sp = GetSpecies(layer.Id, occ);
            float relief = SurfaceRelief(layer.Id, wx, wz, _cornerAlpha[cx, cz], sp.MaxHeightM);
            Vec3 p = new Vec3(wx, gy, wz) + gn * (baseOffset + th + relief);
            Color c = _cornerColor[cx, cz];
            idx = md.AddVertex(p, gn, c.R, c.G, c.B, _cornerAlpha[cx, cz] * SlopeFade(gn), wx, wz, cx / (double)edge, cz / (double)edge);
            _cornerIdx[cx, cz] = idx;
            return idx;
        }

        static float Bilerp(float v00, float v10, float v01, float v11, double s, double t)
            => (float)((v00 * (1 - s) + v10 * s) * (1 - t) + (v01 * (1 - s) + v11 * s) * t);

        int AddSubDirect(double fx, double fz)
        {
            double wx = (t.Ti * edge + fx) * cs;
            double wz = (t.Tj * edge + fz) * cs;
            int c0x = Math.Clamp((int)Math.Floor(fx), 0, edge - 1);
            int c0z = Math.Clamp((int)Math.Floor(fz), 0, edge - 1);
            double s = fx - c0x;
            double t2 = fz - c0z;
            float th = Bilerp(_cornerThickness[c0x, c0z], _cornerThickness[c0x + 1, c0z], _cornerThickness[c0x, c0z + 1], _cornerThickness[c0x + 1, c0z + 1], s, t2);
            float r = Bilerp(_cornerColor[c0x, c0z].R, _cornerColor[c0x + 1, c0z].R, _cornerColor[c0x, c0z + 1].R, _cornerColor[c0x + 1, c0z + 1].R, s, t2);
            float g = Bilerp(_cornerColor[c0x, c0z].G, _cornerColor[c0x + 1, c0z].G, _cornerColor[c0x, c0z + 1].G, _cornerColor[c0x + 1, c0z + 1].G, s, t2);
            float b = Bilerp(_cornerColor[c0x, c0z].B, _cornerColor[c0x + 1, c0z].B, _cornerColor[c0x, c0z + 1].B, _cornerColor[c0x + 1, c0z + 1].B, s, t2);
            float a = Bilerp(_cornerAlpha[c0x, c0z], _cornerAlpha[c0x + 1, c0z], _cornerAlpha[c0x, c0z + 1], _cornerAlpha[c0x + 1, c0z + 1], s, t2);
            double gy = _w.GroundHeight(new Vec2(wx, wz));
            Vec3 gn = GetGroundNormal(wx, wz);
            byte occ = layer.GetOcc((int)Math.Floor(wx / cs), (int)Math.Floor(wz / cs));
            var sp = GetSpecies(layer.Id, occ);
            float relief = SurfaceRelief(layer.Id, wx, wz, a, sp.MaxHeightM);
            Vec3 p = new Vec3(wx, gy, wz) + gn * (baseOffset + th + relief);
            return md.AddVertex(p, gn, r, g, b, a * SlopeFade(gn), wx, wz, fx / edge, fz / edge);
        }

        int GetOrAddHMid(int ex, int ez)
        {
            int idx = _hMidIdx[ex, ez];
            if (idx >= 0) return idx;
            idx = AddSubDirect(ex + 0.5, ez);
            _hMidIdx[ex, ez] = idx;
            return idx;
        }

        int GetOrAddVMid(int ex, int ez)
        {
            int idx = _vMidIdx[ex, ez];
            if (idx >= 0) return idx;
            idx = AddSubDirect(ex, ez + 0.5);
            _vMidIdx[ex, ez] = idx;
            return idx;
        }

        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            bool anyCoverage = _cellOccupied[lx, lz]
                || _cornerAlpha[lx, lz] > 0f || _cornerAlpha[lx + 1, lz] > 0f
                || _cornerAlpha[lx, lz + 1] > 0f || _cornerAlpha[lx + 1, lz + 1] > 0f;
            if (!anyCoverage) continue;

            // 3x3 local vertex grid: true corners and edge midpoints cached across cells.
            int v00 = GetOrAddCorner(lx, lz);
            int v20 = GetOrAddCorner(lx + 1, lz);
            int v02 = GetOrAddCorner(lx, lz + 1);
            int v22 = GetOrAddCorner(lx + 1, lz + 1);
            int v10 = GetOrAddHMid(lx, lz);
            int v12 = GetOrAddHMid(lx, lz + 1);
            int v01 = GetOrAddVMid(lx, lz);
            int v21 = GetOrAddVMid(lx + 1, lz);
            int v11 = AddSubDirect(lx + 0.5, lz + 0.5);

            void SubQuad(int q00, int q10, int q11, int q01, double cs2, double ct2)
            {
                int qc = AddSubDirect(cs2, ct2);
                AddSurfaceTriangle(q00, qc, q10);
                AddSurfaceTriangle(q10, qc, q11);
                AddSurfaceTriangle(q11, qc, q01);
                AddSurfaceTriangle(q01, qc, q00);
            }

            void AddSurfaceTriangle(int a, int b, int c)
            {
                // GroundHeight can jump from soil to the top of a log or rock within one fine
                // cell. Joining those samples creates a tall, stretched curtain of moss.
                double ya = md.Position(a).Y, yb = md.Position(b).Y, yc = md.Position(c).Y;
                if (Math.Max(ya, Math.Max(yb, yc)) - Math.Min(ya, Math.Min(yb, yc)) > 0.03)
                    return;
                md.AddTriangle(a, b, c);
            }

            SubQuad(v00, v10, v11, v01, lx + 0.25, lz + 0.25);
            SubQuad(v10, v20, v21, v11, lx + 0.75, lz + 0.25);
            SubQuad(v01, v11, v12, v02, lx + 0.25, lz + 0.75);
            SubQuad(v11, v21, v22, v12, lx + 0.75, lz + 0.75);
        }

        if (md.TriangleCount > 0)
        {
            md.RecomputeNormals();
        }

        return md;
    }
}
