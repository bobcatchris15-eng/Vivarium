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
        MaxHeightM = 0.015,
        DomeLength = 6.0,
    };

    // Pre-allocated scratch buffers to eliminate per-step/per-tile allocations
    private readonly bool[,] _cellOccupied = new bool[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly float[,] _cellThickness = new float[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly Color[,] _cellColor = new Color[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
    private readonly int[,] _cornerIdx = new int[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly float[,] _cornerThickness = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly Color[,] _cornerColor = new Color[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly float[,] _cornerAlpha = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    private readonly List<(CoverageLayerId layer, int ti, int tj)> _toRemove = new();

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _tiles.Clear();

        _matMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _matMaterial.SetShaderParameter("u_pattern", 0.0f); // fibrous moss micro-detail
        _crustMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _crustMaterial.SetShaderParameter("u_pattern", 1.0f); // cracked/areolate lichen micro-detail
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
                _matSpecies[matId] = new SpeciesRenderInfo
                {
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = md.HeightForm,
                    MaxHeightM = md.MaxHeightM > 0 ? md.MaxHeightM : (sp.Height > 0 ? sp.Height : 0.02),
                    DomeLength = md.DomeLength > 0 ? md.DomeLength : 6.0,
                };
            }
            if (sp.Lichen is { } ld)
            {
                lichenId++;
                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.75f, 0.75f, 0.55f);
                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : c1;
                _crustSpecies[lichenId] = new SpeciesRenderInfo
                {
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = MatHeightForm.Flat,
                    MaxHeightM = sp.Height > 0 ? sp.Height : 0.003,
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

            float th;
            if (layer.Id == CoverageLayerId.Mat)
            {
                switch (sp.HeightForm)
                {
                    case MatHeightForm.Wet:
                        th = (float)(sp.MaxHeightM * b * (w / 255.0));
                        break;
                    case MatHeightForm.Dome:
                        double l = sp.DomeLength > 0 ? sp.DomeLength : 6.0;
                        th = (float)(sp.MaxHeightM * b * (1.0 - Math.Exp(-(d2e + 0.5) / l)));
                        break;
                    case MatHeightForm.Flat:
                    default:
                        th = (float)(sp.MaxHeightM * b);
                        break;
                }
            }
            else
            {
                th = (float)(sp.MaxHeightM * Math.Clamp(b, 0.05f, 1.0f));
            }

            if (th < 0f) th = 0f;
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
                        float nth = (float)(nsp.MaxHeightM * Math.Clamp(nb, 0.05f, 1.0f));
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
                // Continuous occupancy fraction (0.25/0.5/0.75/1.0) drives shader edge feathering
                // instead of hard-zeroing thickness, which produced a staircase skirt at the rim.
                float alpha = occCount / 4.0f;
                _cornerAlpha[cx, cz] = alpha;
                _cornerThickness[cx, cz] = (sumTh * inv) * alpha;
                _cornerColor[cx, cz] = avgCol;
            }
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
            float y = (float)gy + baseOffset + _cornerThickness[cx, cz];
            var p = new Vec3(wx, y, wz);
            var norm = new Vec3(0, 1, 0);
            Color c = _cornerColor[cx, cz];
            idx = md.AddVertex(p, norm, c.R, c.G, c.B, _cornerAlpha[cx, cz], wx, wz, cx / (double)edge, cz / (double)edge);
            _cornerIdx[cx, cz] = idx;
            return idx;
        }

        static float Bilerp(float v00, float v10, float v01, float v11, double s, double t)
            => (float)((v00 * (1 - s) + v10 * s) * (1 - t) + (v01 * (1 - s) + v11 * s) * t);

        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            // Build geometry whenever this cell or any of its 4 corners carries coverage, not just
            // when the cell itself is occupied: an empty cell fully surrounded by occupied neighbours
            // still has partial corner alpha and must be covered so the shader (not the mesh topology)
            // decides the hole's ragged edge, instead of leaving a hard cell-shaped gap.
            bool anyCoverage = _cellOccupied[lx, lz]
                || _cornerAlpha[lx, lz] > 0f || _cornerAlpha[lx + 1, lz] > 0f
                || _cornerAlpha[lx, lz + 1] > 0f || _cornerAlpha[lx + 1, lz + 1] > 0f;
            if (!anyCoverage) continue;

            float th00 = _cornerThickness[lx, lz], th10 = _cornerThickness[lx + 1, lz];
            float th01 = _cornerThickness[lx, lz + 1], th11 = _cornerThickness[lx + 1, lz + 1];
            Color c00 = _cornerColor[lx, lz], c10 = _cornerColor[lx + 1, lz];
            Color c01 = _cornerColor[lx, lz + 1], c11 = _cornerColor[lx + 1, lz + 1];
            float a00 = _cornerAlpha[lx, lz], a10 = _cornerAlpha[lx + 1, lz];
            float a01 = _cornerAlpha[lx, lz + 1], a11 = _cornerAlpha[lx + 1, lz + 1];

            int AddSub(double s, double t2)
            {
                double wx = (t.Ti * edge + lx + s) * cs;
                double wz = (t.Tj * edge + lz + t2) * cs;
                float th = Bilerp(th00, th10, th01, th11, s, t2);
                float r = Bilerp(c00.R, c10.R, c01.R, c11.R, s, t2);
                float g = Bilerp(c00.G, c10.G, c01.G, c11.G, s, t2);
                float b = Bilerp(c00.B, c10.B, c01.B, c11.B, s, t2);
                float a = Bilerp(a00, a10, a01, a11, s, t2);
                double gy = _w.GroundHeight(new Vec2(wx, wz));
                float y = (float)gy + baseOffset + th;
                var p = new Vec3(wx, y, wz);
                return md.AddVertex(p, new Vec3(0, 1, 0), r, g, b, a, wx, wz, (lx + s) / edge, (lz + t2) / edge);
            }

            // 3x3 local vertex grid: true corners cached tile-wide, edge midpoints and center local.
            int v00 = GetOrAddCorner(lx, lz);
            int v20 = GetOrAddCorner(lx + 1, lz);
            int v02 = GetOrAddCorner(lx, lz + 1);
            int v22 = GetOrAddCorner(lx + 1, lz + 1);
            int v10 = AddSub(0.5, 0.0);
            int v01 = AddSub(0.0, 0.5);
            int v21 = AddSub(1.0, 0.5);
            int v12 = AddSub(0.5, 1.0);
            int v11 = AddSub(0.5, 0.5);

            // Same fan pattern (next-corner, center, current-corner) as the original single-quad
            // code, applied to each of the 4 sub-quads: cycle C=[q00,q10,q11,q01].
            void SubQuad(int q00, int q10, int q11, int q01, double cs2, double ct2)
            {
                int qc = AddSub(cs2, ct2);
                md.AddTriangle(q10, qc, q00);
                md.AddTriangle(q11, qc, q10);
                md.AddTriangle(q01, qc, q11);
                md.AddTriangle(q00, qc, q01);
            }

            SubQuad(v00, v10, v11, v01, 0.25, 0.25);
            SubQuad(v10, v20, v21, v11, 0.75, 0.25);
            SubQuad(v01, v11, v12, v02, 0.25, 0.75);
            SubQuad(v11, v21, v22, v12, 0.75, 0.75);
        }

        if (md.TriangleCount > 0)
        {
            md.RecomputeNormals();
        }

        return md;
    }
}