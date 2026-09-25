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
    private StandardMaterial3D _material = null!;
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
    private readonly List<(CoverageLayerId layer, int ti, int tj)> _toRemove = new();

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _tiles.Clear();

        _material = Bridge.VertexColorMaterial(roughness: 0.85f, doubleSided: false);
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

        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial) : _material;
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

        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial) : _material;
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
            }
            else
            {
                float inv = 1.0f / occCount;
                Color avgCol = new Color(sumR * inv, sumG * inv, sumB * inv, 1.0f);
                // Outer/rim cells taper thickness smoothly to 0 so edges are feathered
                if (occCount < 4)
                {
                    _cornerThickness[cx, cz] = 0f;
                    _cornerColor[cx, cz] = avgCol * 0.85f;
                }
                else
                {
                    _cornerThickness[cx, cz] = sumTh * 0.25f;
                    _cornerColor[cx, cz] = avgCol;
                }
            }
        }

        // 3. Assemble MeshData draped over terrain
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
            idx = md.AddVertex(p, norm, c.R, c.G, c.B, c.A, wx, wz, cx / (double)edge, cz / (double)edge);
            _cornerIdx[cx, cz] = idx;
            return idx;
        }

        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            if (!_cellOccupied[lx, lz]) continue;

            double cwx = (t.Ti * edge + lx + 0.5) * cs;
            double cwz = (t.Tj * edge + lz + 0.5) * cs;
            double cgy = _w.GroundHeight(new Vec2(cwx, cwz));
            float cy = (float)cgy + baseOffset + _cellThickness[lx, lz];
            var cp = new Vec3(cwx, cy, cwz);
            Color ccol = _cellColor[lx, lz];
            int centerIdx = md.AddVertex(cp, new Vec3(0, 1, 0), ccol.R, ccol.G, ccol.B, ccol.A, cwx, cwz, (lx + 0.5) / edge, (lz + 0.5) / edge);

            int v00 = GetOrAddCorner(lx, lz);
            int v10 = GetOrAddCorner(lx + 1, lz);
            int v11 = GetOrAddCorner(lx + 1, lz + 1);
            int v01 = GetOrAddCorner(lx, lz + 1);

            // Winding reversed relative to the original fan order so triangles face up under
            // Godot's front-face convention (was relying on doubleSided to be seen at all).
            md.AddTriangle(v10, centerIdx, v00);
            md.AddTriangle(v11, centerIdx, v10);
            md.AddTriangle(v01, centerIdx, v11);
            md.AddTriangle(v00, centerIdx, v01);
        }

        if (md.TriangleCount > 0)
        {
            md.RecomputeNormals();
        }

        return md;
    }
}