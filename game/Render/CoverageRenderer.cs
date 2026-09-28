using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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
    private ShaderMaterial _plasmodiumMaterial = null!;
    private StandardMaterial3D _debugMatMaterial = null!;
    private StandardMaterial3D _debugCrustMaterial = null!;

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public float RebuildBudgetMs { get; set; } = 3.0f;

    /// <summary>Diagnostics for reference/perf capture (updated every SyncTiles).</summary>
    public int TileCount => _tiles.Count;
    public int TrianglesBuilt { get; private set; } // cumulative triangles built this session (diagnostic only)
    public int InstanceCount => GetChildCount();
    public int PendingRebuildCount => _pendingRebuilds.Count;

    private sealed class TileRecord
    {
        public long Version = -1;
        public MeshInstance3D MeshInstance = null!;
        public ArrayMesh Mesh = null!;
    }

    private enum CoverageFloraType : byte
    {
        Generic,
        Sphagnum,        // bogglass_moss
        PearlCushion,    // pearl_cushion_moss
        Floodlace,       // floodlace_moss
        Velvetweave,     // velvetweave_moss
        Antlerlace,      // antlerlace_lichen
        RuffleLichen,    // ruffle_lichen
        Embercrust,      // embercrust_lichen
        Plasmodium,      // ambervein
        Sundew,          // blue_sundew
    }

    private sealed class SpeciesRenderInfo
    {
        public string SpeciesId = "";
        public Color Color1;
        public Color Color2;
        public MatHeightForm HeightForm;
        public double MaxHeightM;
        public double DomeLength;
        public CoverageFloraType FloraType;
    }

    private readonly Dictionary<(CoverageLayerId layer, int ti, int tj), TileRecord> _tiles = new();
    private readonly Dictionary<byte, SpeciesRenderInfo> _matSpecies = new();
    private readonly Dictionary<byte, SpeciesRenderInfo> _crustSpecies = new();
    private readonly Dictionary<byte, SpeciesRenderInfo> _plasmodiumSpecies = new();

    private static readonly SpeciesRenderInfo _defaultSpecies = new()
    {
        Color1 = new Color(0.25f, 0.55f, 0.20f),
        Color2 = new Color(0.35f, 0.65f, 0.25f),
        HeightForm = MatHeightForm.Flat,
        MaxHeightM = 0.003,
        DomeLength = 5.0,
        FloraType = CoverageFloraType.Generic,
    };

    // Scratch buffers for BuildTileMesh are task-local: BuildTileMesh runs concurrently
    // (Task.Run, MaxConcurrentBuilds) on a thread pool thread, so per-build state must
    // never be a shared instance field. Buffers are rented from a pool to avoid a fresh
    // allocation per tile build while still giving each concurrent call its own storage.
    private sealed class ScratchBuffers
    {
        public readonly bool[,] CellOccupied = new bool[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
        public readonly float[,] CellThickness = new float[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
        public readonly Color[,] CellColor = new Color[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
        public readonly float[,] CellVeinW = new float[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
        public readonly float[,] CellFront = new float[CoverageSpec.TileEdge, CoverageSpec.TileEdge];
        public readonly int[,] CornerIdx = new int[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerThickness = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly Color[,] CornerColor = new Color[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerAlpha = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerVeinW = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerFront = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    }

    private readonly System.Collections.Concurrent.ConcurrentBag<ScratchBuffers> _scratchPool = new();

    private ScratchBuffers RentScratch() => _scratchPool.TryTake(out var s) ? s : new ScratchBuffers();

    private void ReturnScratch(ScratchBuffers s) => _scratchPool.Add(s);

    private readonly List<(CoverageLayerId layer, int ti, int tj)> _toRemove = new();
    private readonly List<(CoverageLayerId layer, int ti, int tj)> _pendingRebuilds = new();
    private readonly HashSet<(CoverageLayerId layer, int ti, int tj)> _pendingKeys = new();

    // Off-thread mesh build pipeline: tasks run pure geometry generation (no Godot API);
    // results are committed to MeshInstance3D/ArrayMesh on the main thread only.
    private readonly Dictionary<(CoverageLayerId layer, int ti, int tj), Task<MeshData?>> _inFlight = new();
    private const int MaxConcurrentBuilds = 4;


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

        if (layerId == CoverageLayerId.Plasmodium)
        {
            float veinFactor = w / 255.0f;
            float baseSheet = 0.0010f * bNorm;
            float veinRidge = (float)(sp.MaxHeightM * bNorm * Math.Sqrt(veinFactor));
            th = baseSheet + veinRidge;
            if ((flags & (byte)CoverageFlags.Front) != 0) th = Math.Max(0.0006f, th * 0.7f);
            if ((flags & (byte)CoverageFlags.Fruiting) != 0) th = 0.0004f;
        }
        else if (layerId == CoverageLayerId.Mat)
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
        _pendingRebuilds.Clear();
        _pendingKeys.Clear();
        _inFlight.Clear();

        _matMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _matMaterial.SetShaderParameter("u_pattern", 0.0f); // fibrous moss micro-detail
        Bridge.BindSurface(_matMaterial, "moss", Bridge.Surfaces.Moss);
        _crustMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _crustMaterial.SetShaderParameter("u_pattern", 1.0f); // cracked/areolate lichen micro-detail
        Bridge.BindSurface(_crustMaterial, "moss", Bridge.Surfaces.Moss);
        _crustMaterial.SetShaderParameter("lichen_col", GD.Load<Texture2D>("res://Textures/LichenThallus.png"));
        _plasmodiumMaterial = Bridge.Shader("res://Shaders/coverage_plasmodium.gdshader");
        Bridge.BindSurface(_plasmodiumMaterial, "moss", Bridge.Surfaces.Moss);
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
        SyncTiles(immediate: true);
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

        SyncTiles(immediate: false);
    }

    private void InitSpecies()
    {
        _matSpecies.Clear();
        _crustSpecies.Clear();
        _plasmodiumSpecies.Clear();
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
                CoverageFloraType fType = sp.Id switch
                {
                    "bogglass_moss" => CoverageFloraType.Sphagnum,
                    "pearl_cushion_moss" => CoverageFloraType.PearlCushion,
                    "floodlace_moss" => CoverageFloraType.Floodlace,
                    "velvetweave_moss" => CoverageFloraType.Velvetweave,
                    "blue_sundew" => CoverageFloraType.Sundew,
                    _ => CoverageFloraType.Generic,
                };
                _matSpecies[matId] = new SpeciesRenderInfo
                {
                    SpeciesId = sp.Id,
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = md.HeightForm,
                    MaxHeightM = maxH,
                    DomeLength = md.DomeLength > 0 ? md.DomeLength : 5.0,
                    FloraType = fType,
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
                CoverageFloraType fType = sp.Id switch
                {
                    "antlerlace_lichen" => CoverageFloraType.Antlerlace,
                    "ruffle_lichen" => CoverageFloraType.RuffleLichen,
                    "embercrust_lichen" => CoverageFloraType.Embercrust,
                    _ => CoverageFloraType.Generic,
                };
                _crustSpecies[lichenId] = new SpeciesRenderInfo
                {
                    SpeciesId = sp.Id,
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = MatHeightForm.Flat,
                    MaxHeightM = maxH,
                    DomeLength = 3.0,
                    FloraType = fType,
                };
            }
            if (sp.Archetype == "slime_mold")
            {
                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.96f, 0.65f, 0.08f);
                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : new Color(0.98f, 0.86f, 0.18f);
                _plasmodiumSpecies[1] = new SpeciesRenderInfo
                {
                    SpeciesId = sp.Id,
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = MatHeightForm.Flat,
                    MaxHeightM = 0.006,
                    DomeLength = 2.0,
                    FloraType = CoverageFloraType.Plasmodium,
                };
            }
        }
    }

    private SpeciesRenderInfo GetSpecies(CoverageLayerId layerId, byte occ)
    {
        var dict = layerId == CoverageLayerId.Mat ? _matSpecies
            : layerId == CoverageLayerId.Crust ? _crustSpecies
            : _plasmodiumSpecies;
        if (dict.TryGetValue(occ, out var info)) return info;
        return _defaultSpecies;
    }

    private void SyncTiles(bool immediate = false)
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

        // Check tile versions and enqueue dirty / new tiles
        EnqueueTiles(_w.Coverage.Mat);
        EnqueueTiles(_w.Coverage.Crust);
        EnqueueTiles(_w.Coverage.Plasmodium);

        if (immediate)
        {
            // Startup build: no budget slicing, no async offload — process everything now,
            // synchronously, so the world is fully covered before the first frame renders.
            for (int i = 0; i < _pendingRebuilds.Count; i++)
            {
                var key = _pendingRebuilds[i];
                _pendingKeys.Remove(key);
                var layer = _w.Coverage.ById(key.layer);
                if (layer.TryGetTile(key.ti, key.tj, out var tile) && tile != null && !tile.IsEmpty())
                {
                    var md = BuildTileMesh(layer, tile);
                    CommitTile(layer, tile, key, md);
                }
            }
            _pendingRebuilds.Clear();
            return;
        }

        // Nearest-to-camera-first: sort the pending set once per frame so a bounded
        // per-frame budget always spends itself on what's most visible.
        if (_pendingRebuilds.Count > 1 && Camera != null)
        {
            OrderPendingByCameraDistance();
        }

        long started = Stopwatch.GetTimestamp();

        // Kick off async builds (pure geometry, no Godot API) for pending tiles that
        // aren't already in flight, up to a small concurrency cap.
        int slots = MaxConcurrentBuilds - _inFlight.Count;
        for (int i = 0; i < _pendingRebuilds.Count && slots > 0; i++)
        {
            var key = _pendingRebuilds[i];
            if (_inFlight.ContainsKey(key)) continue;

            var layer = _w.Coverage.ById(key.layer);
            if (!layer.TryGetTile(key.ti, key.tj, out var tile) || tile == null || tile.IsEmpty())
            {
                continue;
            }
            _inFlight[key] = Task.Run(() => BuildTileMesh(layer, tile));
            slots--;
        }

        // Commit whatever finished, nearest-first, within the per-frame time budget.
        // Godot object creation/assignment happens here, on the main thread only.
        for (int i = 0; i < _pendingRebuilds.Count; i++)
        {
            var key = _pendingRebuilds[i];
            if (!_inFlight.TryGetValue(key, out var task) || !task.IsCompleted) continue;

            _inFlight.Remove(key);
            _pendingKeys.Remove(key);
            var layer = _w.Coverage.ById(key.layer);
            layer.TryGetTile(key.ti, key.tj, out var tile);
            CommitTile(layer, tile, key, task.Result);

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= RebuildBudgetMs)
                break;
        }

        // Drop committed/obsolete keys from the pending list; anything still in
        // flight or not yet reached stays for a later frame.
        _pendingRebuilds.RemoveAll(k => !_pendingKeys.Contains(k));
    }

    private void OrderPendingByCameraDistance()
    {
        const int edge = CoverageSpec.TileEdge;
        const double cs = CoverageSpec.CellSize;
        var camPos = Camera!.GlobalPosition;
        _pendingRebuilds.Sort((a, b) =>
        {
            double ax = (a.ti + 0.5) * edge * cs - camPos.X;
            double az = (a.tj + 0.5) * edge * cs - camPos.Z;
            double bx = (b.ti + 0.5) * edge * cs - camPos.X;
            double bz = (b.tj + 0.5) * edge * cs - camPos.Z;
            return (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
        });
    }

    private void EnqueueTiles(CoverageLayer layer)
    {
        foreach (var t in layer.Tiles)
        {
            if (t.IsEmpty()) continue;

            var key = (layer.Id, t.Ti, t.Tj);
            if (_tiles.TryGetValue(key, out var record))
            {
                if (t.Version > record.Version && _pendingKeys.Add(key))
                {
                    _pendingRebuilds.Add(key);
                }
            }
            else
            {
                if (_pendingKeys.Add(key))
                {
                    _pendingRebuilds.Add(key);
                }
            }
        }
    }

    /// <summary>Applies an off-thread-built (or synchronously-built) mesh result to the scene tree.</summary>
    private void CommitTile(CoverageLayer layer, CoverageTile? tile, (CoverageLayerId layer, int ti, int tj) key, MeshData? md)
    {
        if (tile == null || tile.IsEmpty() || md == null || md.VertexCount == 0 || md.TriangleCount == 0)
        {
            if (_tiles.TryGetValue(key, out var stale))
            {
                stale.MeshInstance.QueueFree();
                RemoveChild(stale.MeshInstance);
                _tiles.Remove(key);
            }
            return;
        }

        if (_tiles.TryGetValue(key, out var rec))
        {
            RebuildTile(layer, tile, rec, md);
        }
        else
        {
            CreateTile(layer, tile, key, md);
        }
    }

    private void CreateTile(CoverageLayer layer, CoverageTile t, (CoverageLayerId layer, int ti, int tj) key, MeshData md)
    {
        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)
            : layer.Id == CoverageLayerId.Plasmodium ? _plasmodiumMaterial
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

    private void RebuildTile(CoverageLayer layer, CoverageTile t, TileRecord record, MeshData md)
    {
        var mat = DebugMode ? (layer.Id == CoverageLayerId.Crust ? _debugCrustMaterial : _debugMatMaterial)
            : layer.Id == CoverageLayerId.Plasmodium ? _plasmodiumMaterial
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
        if (layer == CoverageLayerId.Plasmodium)
        {
            float wave = ValueNoise(wx + 0.015, wz + 0.015, 0.025);
            return alpha * 0.0025f * wave;
        }
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
        var scratch = RentScratch();
        try
        {
            return BuildTileMeshCore(layer, t, scratch);
        }
        finally
        {
            ReturnScratch(scratch);
        }
    }

    private MeshData BuildTileMeshCore(CoverageLayer layer, CoverageTile t, ScratchBuffers s)
    {
        const int edge = CoverageSpec.TileEdge;
        const double cs = CoverageSpec.CellSize;
        float baseOffset = layer.Id == CoverageLayerId.Crust ? 0.002f : layer.Id == CoverageLayerId.Plasmodium ? 0.0038f : 0.0035f;

        // 1. Compute per-cell thickness and vertex color
        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            int li = lz * edge + lx;
            byte occ = t.Occ[li];
            if (occ == 0)
            {
                s.CellOccupied[lx, lz] = false;
                s.CellThickness[lx, lz] = 0f;
                s.CellColor[lx, lz] = Colors.Black;
                s.CellVeinW[lx, lz] = 0f;
                s.CellFront[lx, lz] = 0f;
                continue;
            }

            s.CellOccupied[lx, lz] = true;
            var sp = GetSpecies(layer.Id, occ);
            float b = t.B[li];
            byte w = t.W[li];
            byte dorm = t.Dorm[li];
            byte flags = t.Flags[li];
            byte d2e = t.D2E[li];

            float th = ComputeThickness(sp, layer.Id, b, w, d2e, flags);
            s.CellThickness[lx, lz] = th;
            s.CellVeinW[lx, lz] = w / 255f;
            s.CellFront[lx, lz] = (flags & (byte)CoverageFlags.Front) != 0 ? 1f : 0f;

            if (layer.Id == CoverageLayerId.Plasmodium)
            {
                float veinFrac = Math.Clamp(w / 180f, 0f, 1f);
                Color pCol = sp.Color2.Lerp(sp.Color1, veinFrac);
                if ((flags & (byte)CoverageFlags.Front) != 0)
                {
                    pCol = sp.Color2 * 1.15f;
                }
                else if ((flags & (byte)CoverageFlags.Fruiting) != 0)
                {
                    pCol = new Color(0.48f, 0.44f, 0.36f);
                }
                else if ((flags & (byte)CoverageFlags.Sclerotium) != 0)
                {
                    pCol = new Color(0.82f, 0.42f, 0.10f);
                }
                pCol.A = 1f;
                s.CellColor[lx, lz] = pCol;
                continue;
            }

            // Vertex colors derive from species Color and Color2 modulated by biomass, dormancy (browning), and health
            float bNorm = Mathf.Clamp(b, 0f, 1f);
            Color col = sp.FloraType == CoverageFloraType.Sphagnum
                ? sp.Color1.Lerp(sp.Color2, (1f - bNorm) * 0.15f)
                : sp.Color2.Lerp(sp.Color1, bNorm);
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
            s.CellColor[lx, lz] = col;
        }

        // 2. Evaluate 33x33 corner grid for continuity and edge feathering
        for (int cz = 0; cz <= edge; cz++)
        for (int cx = 0; cx <= edge; cx++)
        {
            s.CornerIdx[cx, cz] = -1;
            int occCount = 0;
            float sumTh = 0f;
            float sumR = 0f, sumG = 0f, sumB = 0f;
            float sumVeinW = 0f, sumFront = 0f;

            for (int oz = -1; oz <= 0; oz++)
            for (int ox = -1; ox <= 0; ox++)
            {
                int nlx = cx + ox;
                int nlz = cz + oz;
                if (nlx >= 0 && nlx < edge && nlz >= 0 && nlz < edge)
                {
                    if (s.CellOccupied[nlx, nlz])
                    {
                        occCount++;
                        sumTh += s.CellThickness[nlx, nlz];
                        var c = s.CellColor[nlx, nlz];
                        sumR += c.R; sumG += c.G; sumB += c.B;
                        sumVeinW += s.CellVeinW[nlx, nlz];
                        sumFront += s.CellFront[nlx, nlz];
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
                        sumVeinW += nw / 255f;
                        sumFront += (nflags & (byte)CoverageFlags.Front) != 0 ? 1f : 0f;
                    }
                }
            }

            if (occCount == 0)
            {
                s.CornerThickness[cx, cz] = 0f;
                s.CornerColor[cx, cz] = Colors.Black;
                s.CornerAlpha[cx, cz] = 0f;
                s.CornerVeinW[cx, cz] = 0f;
                s.CornerFront[cx, cz] = 0f;
            }
            else
            {
                float inv = 1.0f / occCount;
                Color avgCol = new Color(sumR * inv, sumG * inv, sumB * inv, 1.0f);
                float alpha = occCount / 4.0f;
                if (alpha < 1.0f)
                {
                    double wx = (t.Ti * edge + cx) * cs;
                    double wz = (t.Tj * edge + cz) * cs;
                    float edgeNoise = (float)Vivarium.Sim.Core.Noise.Gradient(0xB10CUL, wx * 10.0, wz * 10.0);
                    alpha = Mathf.Clamp(alpha + 0.16f * edgeNoise, 0.05f, 0.95f);
                }
                s.CornerAlpha[cx, cz] = alpha;
                // Smooth rounded edge falloff for corner thickness
                float falloff = alpha * alpha * (3f - 2f * alpha);
                s.CornerThickness[cx, cz] = (sumTh * inv) * falloff;
                s.CornerColor[cx, cz] = avgCol;
                s.CornerVeinW[cx, cz] = sumVeinW * inv;
                s.CornerFront[cx, cz] = sumFront * inv;
            }
        }

        // 3. Assemble MeshData draped over terrain using the continuous corner grid.
        // Vertices are evaluated and cached once at cell corners, giving smooth normals,
        // watertight coverage boundaries, and eliminating redundant per-subquad relief evaluations.
        var md = new MeshData();

        double tileMinX = t.Ti * edge * cs - 0.1;
        double tileMaxX = (t.Ti + 1) * edge * cs + 0.1;
        double tileMinZ = t.Tj * edge * cs - 0.1;
        double tileMaxZ = (t.Tj + 1) * edge * cs + 0.1;

        List<Rock>? localRocks = null;
        List<LogProp>? localLogs = null;
        foreach (var r in _w.Props.Rocks)
        {
            if (r.X + r.FootprintRadius >= tileMinX && r.X - r.FootprintRadius <= tileMaxX &&
                r.Z + r.FootprintRadius >= tileMinZ && r.Z - r.FootprintRadius <= tileMaxZ)
            {
                localRocks ??= new List<Rock>();
                localRocks.Add(r);
            }
        }
        foreach (var l in _w.Props.Logs)
        {
            if (l.X + l.FootprintRadius >= tileMinX && l.X - l.FootprintRadius <= tileMaxX &&
                l.Z + l.FootprintRadius >= tileMinZ && l.Z - l.FootprintRadius <= tileMaxZ)
            {
                localLogs ??= new List<LogProp>();
                localLogs.Add(l);
            }
        }
        bool hasLocalProps = localRocks != null || localLogs != null;

        double TileGroundHeight(Vec2 wp)
        {
            double h = _w.Terrain.Height(wp);
            if (!hasLocalProps) return h;
            double best = double.NaN;
            if (localRocks != null)
                for (int ri = 0; ri < localRocks.Count; ri++) { double top = localRocks[ri].TopAt(wp); if (!double.IsNaN(top) && !(top <= best)) best = top; }
            if (localLogs != null)
                for (int li = 0; li < localLogs.Count; li++) { double top = localLogs[li].TopAt(wp); if (!double.IsNaN(top) && !(top <= best)) best = top; }
            return double.IsNaN(best) ? h : Math.Max(h, best);
        }

        Vec3 TileGroundNormal(double wx, double wz, double gy)
        {
            var wp = new Vec2(wx, wz);
            if (!hasLocalProps) return _w.Terrain.Normal(wp);
            double th = _w.Terrain.Height(wp);
            if (Math.Abs(gy - th) < 0.001) return _w.Terrain.Normal(wp);
            const double step = CoverageSpec.CellSize * 0.5;
            double hL = TileGroundHeight(new Vec2(wx - step, wz));
            double hR = TileGroundHeight(new Vec2(wx + step, wz));
            double hD = TileGroundHeight(new Vec2(wx, wz - step));
            double hU = TileGroundHeight(new Vec2(wx, wz + step));
            double dx = (hR - hL) / (2.0 * step);
            double dz = (hU - hD) / (2.0 * step);
            double invLen = 1.0 / Math.Sqrt(dx * dx + 1.0 + dz * dz);
            return new Vec3((float)(-dx * invLen), (float)invLen, (float)(-dz * invLen));
        }

        int GetOrAddCorner(int cx, int cz)
        {
            int idx = s.CornerIdx[cx, cz];
            if (idx >= 0) return idx;

            double wx = (t.Ti * edge + cx) * cs;
            double wz = (t.Tj * edge + cz) * cs;
            double gy = TileGroundHeight(new Vec2(wx, wz));
            Vec3 gn = TileGroundNormal(wx, wz, gy);
            float th = s.CornerThickness[cx, cz];
            byte occ = layer.GetOcc(t.Ti * edge + cx, t.Tj * edge + cz);
            var sp = GetSpecies(layer.Id, occ);
            float relief = SurfaceRelief(layer.Id, wx, wz, s.CornerAlpha[cx, cz], sp.MaxHeightM);
            Vec3 p = new Vec3(wx, gy, wz) + gn * (baseOffset + th + relief);
            Color c = s.CornerColor[cx, cz];
            double u2 = layer.Id == CoverageLayerId.Plasmodium ? s.CornerVeinW[cx, cz] : cx / (double)edge;
            double v2 = layer.Id == CoverageLayerId.Plasmodium ? s.CornerFront[cx, cz] : cz / (double)edge;
            idx = md.AddVertex(p, gn, c.R, c.G, c.B, s.CornerAlpha[cx, cz] * SlopeFade(gn), wx, wz, u2, v2);
            s.CornerIdx[cx, cz] = idx;
            return idx;
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

        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            bool anyCoverage = s.CellOccupied[lx, lz]
                || s.CornerAlpha[lx, lz] > 0f || s.CornerAlpha[lx + 1, lz] > 0f
                || s.CornerAlpha[lx, lz + 1] > 0f || s.CornerAlpha[lx + 1, lz + 1] > 0f;
            if (!anyCoverage) continue;

            int c00 = GetOrAddCorner(lx, lz);
            int c10 = GetOrAddCorner(lx + 1, lz);
            int c01 = GetOrAddCorner(lx, lz + 1);
            int c11 = GetOrAddCorner(lx + 1, lz + 1);

            AddSurfaceTriangle(c00, c11, c10);
            AddSurfaceTriangle(c00, c01, c11);
        }

        if (md.TriangleCount > 0)
        {
            md.RecomputeNormals();
        }

        // Append procedural 3D flora structures (setae, capsules, fronds, podetia, ruffles, apothecia)
        for (int lz = 0; lz < edge; lz++)
        for (int lx = 0; lx < edge; lx++)
        {
            if (!s.CellOccupied[lx, lz]) continue;
            if ((t.Flags[lz * edge + lx] & (byte)CoverageFlags.Dead) != 0) continue;
            var sp = GetSpecies(layer.Id, t.Occ[lz * edge + lx]);
            if (sp.FloraType == CoverageFloraType.Generic) continue;

            int gx = t.Ti * edge + lx, gz = t.Tj * edge + lz;
            uint hash = ReliefHash(gx, gz);
            double wx = (gx + 0.5) * cs + ((hash & 255) / 255.0 - 0.5) * 0.017;
            double wz = (gz + 0.5) * cs + (((hash >> 8) & 255) / 255.0 - 0.5) * 0.017;
            double gy = TileGroundHeight(new Vec2(wx, wz));
            Vec3 normal = TileGroundNormal(wx, wz, gy);
            if (normal.Y < 0.45) continue;
            double relief = SurfaceRelief(layer.Id, wx, wz, 1f, sp.MaxHeightM);
            var foot = new Vec3(wx, gy + baseOffset + s.CellThickness[lx, lz] + relief - 0.002, wz);
            float vigour = Math.Clamp(t.B[lz * edge + lx] * 3f, 0.72f, 1f);
            Color shootColor = sp.Color1.Lerp(s.CellColor[lx, lz], 0.35f);

            switch (sp.FloraType)
            {
                case CoverageFloraType.Sphagnum:
                    if ((hash >> 24) < 70)
                        AppendSphagnumShoot(md, foot, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.PearlCushion:
                    if ((hash >> 24) < 110)
                        AppendPearlCushionStructures(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Floodlace:
                    if ((hash >> 24) < 95)
                        AppendFloodlaceFronds(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Velvetweave:
                    if ((hash >> 24) < 100)
                        AppendVelvetweaveTurf(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Antlerlace:
                    if ((hash >> 24) < 85)
                        AppendAntlerlacePodetia(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.RuffleLichen:
                    if ((hash >> 24) < 90)
                        AppendRuffleLobes(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Embercrust:
                    if ((hash >> 24) < 120)
                        AppendEmbercrustApothecia(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Plasmodium:
                    if ((t.Flags[lz * edge + lx] & (byte)CoverageFlags.Fruiting) != 0 || ((hash >> 24) < 75 && t.W[lz * edge + lx] >= 140))
                        AppendPlasmodiumSporangia(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Sundew:
                    if ((hash >> 24) < 125)
                        AppendBlueSundewRosette(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
            }
        }

        return md;
    }

    private static void AppendSphagnumShoot(MeshData mesh, Vec3 foot, Color color, Color color2, uint hash, float vigour)
    {
        double height = (0.034 + ((hash >> 16) & 255) / 255.0 * 0.016) * vigour;
        double leanX = (((hash >> 4) & 15) / 15.0 - 0.5) * 0.005;
        double leanZ = (((hash >> 12) & 15) / 15.0 - 0.5) * 0.005;
        var tip = foot + new Vec3(leanX, height, leanZ);
        var stem = new[] { foot, Vec3.Lerp(foot, tip, 0.36), Vec3.Lerp(foot, tip, 0.72), tip };
        var dark = new[] { color.R * 0.82, color.G * 0.90, color.B * 0.79 };
        Primitives.Tube(mesh, stem, new[] { 0.0018, 0.0015, 0.0012, 0.0009 }, 6,
            (i, v) => (dark, 1, 0, 0, 0, 0));

        var branchColor = new[] { Math.Min(1, color.R * 1.1), Math.Min(1, color.G * 1.13), Math.Min(1, color.B * 1.08) };
        var crownColor = new[] { Math.Min(1, color.R * 1.12 + 0.03), Math.Min(1, color.G * 1.12 + 0.03), Math.Min(1, color.B * 1.10 + 0.02) };
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;
        for (int whorl = 0; whorl < 3; whorl++)
        {
            double t = whorl == 2 ? 0.96 : whorl == 1 ? 0.68 : 0.39;
            var node = Vec3.Lerp(foot, tip, t);
            int branches = whorl == 2 ? 9 : 5;
            double reach = whorl == 2 ? 0.008 : whorl == 1 ? 0.012 : 0.010;
            for (int b = 0; b < branches; b++)
            {
                uint variation = ReliefHash(unchecked((int)hash) + b * 31, whorl * 17 + b);
                double a = phase + b * Math.PI * 2 / branches + whorl * 0.37
                    + ((variation & 255) / 255.0 - 0.5) * 0.16;
                var radial = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var side = new Vec3(-radial.Z, 0, radial.X);
                double droop = whorl == 2 ? 0.003 : whorl == 1 ? -0.001 : -0.004;
                double branchReach = reach * (0.78 + ((variation >> 8) & 255) / 255.0 * 0.4);
                var end = node + radial * branchReach + Vec3.Up * droop;
                var middle = Vec3.Lerp(node, end, 0.56) + Vec3.Up * 0.002;
                var col = whorl == 2 ? crownColor : branchColor;
                int a0 = mesh.AddVertex(node - side * 0.0009, Vec3.Up, col, 1, node.X, node.Z);
                int a1 = mesh.AddVertex(node + side * 0.0009, Vec3.Up, col, 1, node.X, node.Z);
                int mid = mesh.AddVertex(middle + side * 0.0010, Vec3.Up, col, 1, middle.X, middle.Z);
                int midOther = mesh.AddVertex(middle - side * 0.0010, Vec3.Up, col, 1, middle.X, middle.Z);
                int apex = mesh.AddVertex(end, Vec3.Up, col, 1, end.X, end.Z);
                Primitives.TriangleFacing(mesh, a0, a1, mid, Vec3.Up);
                Primitives.TriangleFacing(mesh, a0, mid, midOther, Vec3.Up);
                Primitives.TriangleFacing(mesh, midOther, mid, apex, Vec3.Up);
            }
        }

        // Sphagnum raised spherical spore capsule on mature shoots
        if ((hash & 3) == 0)
        {
            var stalkTip = tip + Vec3.Up * (0.008 + ((hash >> 8) & 15) * 0.0005);
            Primitives.Tube(mesh, new[] { tip, stalkTip }, new[] { 0.0007, 0.0005 }, 4,
                (i, v) => (new[] { 0.50, 0.42, 0.28 }, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, stalkTip + Vec3.Up * 0.0015, new Vec3(0.0014, 0.0018, 0.0014), 4, 6,
                (u, v) => (new[] { 0.16, 0.12, 0.08 }, 1, u, v, 0, 0));
        }
    }

    private static void AppendPearlCushionStructures(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // 1. Wiry setae stalk carrying glossy spore capsule with beaked calyptra
        double height = (0.015 + ((hash >> 16) & 255) / 255.0 * 0.009) * vigour;
        double leanX = (((hash >> 4) & 15) / 15.0 - 0.5) * 0.004;
        double leanZ = (((hash >> 12) & 15) / 15.0 - 0.5) * 0.004;
        var tip = foot + normal * height + new Vec3(leanX, 0, leanZ);
        var setaPath = new[] { foot, Vec3.Lerp(foot, tip, 0.45) + new Vec3(leanX * 0.3, 0, leanZ * 0.3), tip };
        var setaCol = new[] { 0.62, 0.28, 0.12 }; // amber-bronze wire
        Primitives.Tube(mesh, setaPath, new[] { 0.0007, 0.0005, 0.0004 }, 4,
            (i, v) => (setaCol, 1, 0, 0, 0, 0));

        // Spore capsule ellipsoid
        var capCenter = tip + normal * 0.0018 + new Vec3(leanX * 0.4, 0, leanZ * 0.4);
        var capCol = new[] { 0.78, 0.48, 0.16 };
        Primitives.Ellipsoid(mesh, capCenter, new Vec3(0.0012, 0.0022, 0.0012), 4, 6,
            (u, v) => (capCol, 1, u, v, 0, 0));

        // Beaked calyptra cap
        var calyptraCol = new[] { 0.88, 0.80, 0.42 };
        var beakTip = capCenter + normal * 0.0024 + new Vec3(leanX * 0.3, 0.0005, leanZ * 0.3);
        Primitives.Tube(mesh, new[] { capCenter + normal * 0.0012, beakTip }, new[] { 0.0010, 0.0002 }, 4,
            (i, v) => (calyptraCol, 1, i, v, 0, 0));

        // 2. Silvery-pearl hair points (hyaline awns)
        var pearlCol = new[] { 0.94, 0.98, 0.95 };
        for (int p = 0; p < 5; p++)
        {
            double a = (hash & 255) / 255.0 * Math.PI * 2 + p * Math.PI * 2 / 5.0;
            var rad = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var awnEnd = foot + rad * 0.0055 + normal * 0.0035;
            var awnSide = new Vec3(-rad.Z, 0, rad.X) * 0.0004;

            int v0 = mesh.AddVertex(foot - awnSide, normal, pearlCol, 0.95);
            int v1 = mesh.AddVertex(foot + awnSide, normal, pearlCol, 0.95);
            int vTip = mesh.AddVertex(awnEnd, normal, pearlCol, 0.98);
            Primitives.TriangleFacing(mesh, v0, v1, vTip, normal);
        }
    }

    private static void AppendFloodlaceFronds(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Pleurocarpous feathery trailing runner and pinnate branchlets
        double ang = (hash & 1023) / 1023.0 * Math.PI * 2;
        var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
        var side = new Vec3(-dir.Z, 0, dir.X);

        double reach = (0.016 + ((hash >> 16) & 255) / 255.0 * 0.009) * vigour;
        var p0 = foot;
        var p1 = foot + dir * (reach * 0.35) + side * 0.002 + normal * 0.001;
        var p2 = foot + dir * (reach * 0.70) - side * 0.002 + normal * 0.001;
        var p3 = foot + dir * reach;

        var stemCol = new[] { col1.R * 0.65, col1.G * 0.72, col1.B * 0.62 };
        Primitives.Tube(mesh, new[] { p0, p1, p2, p3 }, new[] { 0.0008, 0.0006, 0.0005, 0.0003 }, 4,
            (i, v) => (stemCol, 1, 0, 0, 0, 0));

        // Feather pinnule sprays
        var leafCol = new[] { Math.Min(1, col1.R * 1.15), Math.Min(1, col1.G * 1.25), Math.Min(1, col1.B * 1.08) };
        for (int p = 1; p <= 5; p++)
        {
            double t = p / 6.0;
            var node = Vec3.Lerp(p0, p3, t);
            double pinSpread = 0.0045 * (1.0 - t * 0.3);

            for (int sSign = -1; sSign <= 1; sSign += 2)
            {
                double pinAng = ang + sSign * (1.1 + ((hash >> (p * 2)) & 3) * 0.08);
                var pinDir = new Vec3(Math.Cos(pinAng), 0, Math.Sin(pinAng));
                var pinSide = new Vec3(-pinDir.Z, 0, pinDir.X) * 0.0005;
                var pinTip = node + pinDir * pinSpread + normal * 0.001;

                int v0 = mesh.AddVertex(node - pinSide, normal, leafCol, 1);
                int v1 = mesh.AddVertex(node + pinSide, normal, leafCol, 1);
                int vTip = mesh.AddVertex(pinTip, normal, leafCol, 1);
                Primitives.TriangleFacing(mesh, v0, v1, vTip, normal);
            }
        }

        // Occasional lateral curved sporophyte
        if ((hash & 3) == 0)
        {
            var stalkNode = Vec3.Lerp(p0, p2, 0.5);
            var stalkTip = stalkNode + side * 0.003 + normal * 0.008;
            Primitives.Tube(mesh, new[] { stalkNode, stalkTip }, new[] { 0.0005, 0.00035 }, 4,
                (i, v) => (new[] { 0.55, 0.30, 0.15 }, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, stalkTip + side * 0.0015, new Vec3(0.0009, 0.0016, 0.0009), 4, 5,
                (u, v) => (new[] { 0.68, 0.45, 0.18 }, 1, u, v, 0, 0));
        }
    }

    private static void AppendVelvetweaveTurf(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // 1. Clustered upright pointed micro-turf shoots
        var deepGreen = new[] { col1.R * 0.72, col1.G * 0.85, col1.B * 0.68 };
        var tipGreen = new[] { Math.Min(1, col1.R * 1.15), Math.Min(1, col1.G * 1.25), Math.Min(1, col1.B * 1.05) };

        for (int j = 0; j < 6; j++)
        {
            double a = (hash & 511) / 511.0 * Math.PI * 2 + j * Math.PI * 2 / 6.0;
            var rad = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var shootFoot = foot + rad * 0.003;
            double shootH = 0.005 + ((hash >> (j * 3)) & 7) * 0.0008;
            var shootTip = shootFoot + normal * shootH + rad * 0.0015;
            var side = new Vec3(-rad.Z, 0, rad.X) * 0.0005;

            int v0 = mesh.AddVertex(shootFoot - side, normal, deepGreen, 1);
            int v1 = mesh.AddVertex(shootFoot + side, normal, deepGreen, 1);
            int vTip = mesh.AddVertex(shootTip, normal, tipGreen, 1);
            Primitives.TriangleFacing(mesh, v0, v1, vTip, normal);
        }

        // 2. Tall wire seta and nodding angular urn capsule
        double height = (0.020 + ((hash >> 16) & 255) / 255.0 * 0.010) * vigour;
        double leanX = (((hash >> 4) & 15) / 15.0 - 0.5) * 0.005;
        double leanZ = (((hash >> 12) & 15) / 15.0 - 0.5) * 0.005;
        var tip = foot + normal * height + new Vec3(leanX, 0, leanZ);

        var setaCol = new[] { 0.50, 0.18, 0.12 }; // crimson-bronze wire
        Primitives.Tube(mesh, new[] { foot, Vec3.Lerp(foot, tip, 0.45), tip }, new[] { 0.0006, 0.00045, 0.00035 }, 4,
            (i, v) => (setaCol, 1, 0, 0, 0, 0));

        // Nodding urn capsule
        var urnDir = (new Vec3(leanX, -0.003, leanZ) + normal * 0.001).Normalized();
        var urnCol = new[] { 0.70, 0.52, 0.20 };
        var capCol = new[] { 0.86, 0.78, 0.40 };
        var urnCenter = tip + urnDir * 0.0022;

        Primitives.Tube(mesh, new[] { tip, urnCenter }, new[] { 0.0005, 0.0012 }, 4,
            (i, v) => (urnCol, 1, 0, 0, 0, 0));
        Primitives.Tube(mesh, new[] { urnCenter, urnCenter + urnDir * 0.0016 }, new[] { 0.0012, 0.0002 }, 4,
            (i, v) => (capCol, 1, 0, 0, 0, 0));
    }

    private static void AppendAntlerlacePodetia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Fruticose lichen upright antler-like branching podetia
        double totalH = (0.016 + ((hash >> 16) & 255) / 255.0 * 0.009) * vigour;
        var podetiaCol = new[] { col1.R * 1.08, col1.G * 1.15, col1.B * 1.05 };
        var buttonCol = new[] { 0.45, 0.32, 0.18 };

        double ang = (hash & 1023) / 1023.0 * Math.PI * 2;
        var sideFork = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));

        var trunkTop = foot + normal * (totalH * 0.45);
        Primitives.Tube(mesh, new[] { foot, trunkTop }, new[] { 0.0022, 0.0016 }, 5,
            (i, v) => (podetiaCol, 1, 0, 0, 0, 0));

        for (int b = -1; b <= 1; b += 2)
        {
            var branchDir = (normal * 0.7 + sideFork * (b * 0.45)).Normalized();
            var fork1 = trunkTop + branchDir * (totalH * 0.30);
            Primitives.Tube(mesh, new[] { trunkTop, fork1 }, new[] { 0.0015, 0.0011 }, 4,
                (i, v) => (podetiaCol, 1, 0, 0, 0, 0));

            // Palmate tines
            for (int t = -1; t <= 1; t += 2)
            {
                var tineDir = (branchDir * 0.7 + sideFork * (b * t * 0.35)).Normalized();
                var tineTip = fork1 + tineDir * (totalH * 0.25);
                Primitives.Tube(mesh, new[] { fork1, tineTip }, new[] { 0.0009, 0.0004 }, 4,
                    (i, v) => (podetiaCol, 1, 0, 0, 0, 0));

                Primitives.Ellipsoid(mesh, tineTip + tineDir * 0.0005, new Vec3(0.0007, 0.0007, 0.0007), 3, 4,
                    (u, v) => (buttonCol, 1, u, v, 0, 0));
            }
        }
    }

    private static void AppendRuffleLobes(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Foliose lichen wavy ruffled thallus lobes and apothecia saucers
        var upperCol = new[] { (double)col1.R, (double)col1.G, (double)col1.B };
        var underCol = new[] { 0.82, 0.80, 0.70 };
        var discCol = new[] { 0.80, 0.45, 0.18 };

        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;
        for (int l = 0; l < 3; l++)
        {
            double a = baseAng + l * Math.PI * 2 / 3.0;
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = new Vec3(-dir.Z, 0, dir.X) * 0.004;
            double len = 0.008 + ((hash >> (l * 4)) & 15) * 0.0004;

            var pMid = foot + dir * (len * 0.5) + normal * 0.0025;
            var pTip = foot + dir * len + normal * 0.004; // curled up edge!

            // Curled thallus lobe
            int u0 = mesh.AddVertex(foot - side * 0.5, normal, upperCol);
            int u1 = mesh.AddVertex(foot + side * 0.5, normal, upperCol);
            int uMidL = mesh.AddVertex(pMid - side, normal, upperCol);
            int uMidR = mesh.AddVertex(pMid + side, normal, upperCol);
            int uApex = mesh.AddVertex(pTip, normal, underCol); // edge shows pale underside

            Primitives.TriangleFacing(mesh, u0, u1, uMidR, normal);
            Primitives.TriangleFacing(mesh, u0, uMidR, uMidL, normal);
            Primitives.TriangleFacing(mesh, uMidL, uMidR, uApex, normal);
        }

        // Apothecium saucer cup
        var cupCenter = foot + normal * 0.0018;
        double cupR = 0.0022;
        Primitives.Tube(mesh, new[] { foot, cupCenter }, new[] { cupR * 0.8, cupR }, 5,
            (i, v) => (underCol, 1, 0, 0, 0, 0));
        Primitives.Ellipsoid(mesh, cupCenter + normal * 0.0003, new Vec3(cupR * 0.8, 0.0004, cupR * 0.8), 3, 5,
            (u, v) => (discCol, 1, u, v, 0, 0));
    }

    private static void AppendEmbercrustApothecia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Crustose lichen raised ember-orange apothecial discs
        var rimCol = new[] { 0.88, 0.76, 0.40 };
        var discCol = new[] { 0.92, 0.38, 0.08 };

        int discCount = 2 + (int)((hash & 3));
        for (int d = 0; d < discCount; d++)
        {
            double a = (hash & 255) / 255.0 * Math.PI * 2 + d * Math.PI * 2 / discCount;
            double dist = 0.0035 * ((d + 1) / (double)discCount);
            var center = foot + new Vec3(Math.Cos(a), 0, Math.Sin(a)) * dist + normal * 0.0008;
            double r = 0.0016;

            Primitives.Tube(mesh, new[] { center, center + normal * 0.0008 }, new[] { r * 0.9, r }, 5,
                (i, v) => (rimCol, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, center + normal * 0.0009, new Vec3(r * 0.75, 0.0003, r * 0.75), 3, 5,
                (u, v) => (discCol, 1, u, v, 0, 0));
        }
    }


    private static void AppendBlueSundewRosette(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        int firstPlantVertex = mesh.VertexCount;
        // Raised petioles carry spoon leaves above the substrate. Keep compact colony geometry
        // independent of the large inspection mesh: every occupied cell can contribute a rosette.
        var blue = new[] { (double)col1.R, (double)col1.G, (double)col1.B };
        var pale = new[] { .40, .67, .77 };
        var gland = new[] { .56, .17, .28 };
        int leaves = 6 + (int)(hash & 1);
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;
        double scale = .72 + vigour * .32;
        for (int i = 0; i < leaves; i++)
        {
            double a = phase + i * 2.39996;
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            dir = (dir - normal * dir.Dot(normal)).Normalized();
            var side = dir.Cross(normal).Normalized();
            double reach = (.005 + ((hash >> (i * 3 + 8)) & 7) * .0007) * scale;
            var root = foot + normal * .001;
            var neck = root + dir * reach + normal * ((.006 + .001 * (i % 3)) * scale);
            Primitives.Tube(mesh, new[] { root, Vec3.Lerp(root, neck, .55) + normal * .001, neck },
                new[] { .00055, .0004, .0002 }, 6, (j, v) => (blue, 1, j * .5, v, 0, 0));
            double length = .009 * scale, width = .004 * scale;
            var tip = neck + dir * length + normal * .0015;
            var forward = (tip - neck).Normalized();
            var up = side.Cross(forward).Normalized();
            Vivarium.Sim.Geometry.Form.FoliageBlade.Build(mesh, neck, tip, side, width, blue, pale,
                camber: .22, curl: .08, shoulder: .85, segments: 5);
            Vec3 At(double t, double x)
            {
                double env = Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, .85))), .8);
                double lift = (tip-neck).Length * .08 * Math.Sin(Math.PI*t) * (1-1.5*t)
                    + width * env * (.22*(1-x*x)-.06*Math.Abs(x));
                return neck + (tip-neck)*t + side*(x*width*env) + up*lift;
            }
            for (int g = 0; g < 13; g++)
            {
                bool margin = g < 10;
                double t = margin ? .12 + (g % 5)*.18 : .28 + (g-10)*.20;
                double x = margin ? (g < 5 ? -1 : 1) : .25 * (g-11);
                var gb = At(t, x);
                var gt = gb + side*(x*.00065) + up*(margin ? .0025 : .0015);
                // A submillimetre tentacle needs a solid tapered silhouette, not a six-sided tube.
                var tangent = (gt-gb).Normalized();
                var cross = (side-tangent*side.Dot(tangent)).Normalized();
                var other = cross.Cross(tangent).Normalized();
                int stalk = mesh.VertexCount;
                for(int q=0;q<3;q++)
                {
                    double angle=q*Math.PI*2/3;
                    var radial=cross*Math.Cos(angle)+other*Math.Sin(angle);
                    mesh.AddVertex(gb+radial*.00016,radial,gland,1,0,q/3.0,0,0);
                }
                mesh.AddVertex(gt,tangent,gland,1,1,.5,0,0);
                for(int q=0;q<3;q++)
                    Primitives.TriangleFacing(mesh,stalk+q,stalk+(q+1)%3,stalk+3,
                        (mesh.NormalAt(stalk+q)+mesh.NormalAt(stalk+(q+1)%3)).Normalized());
                // Eight-faced closed droplet, instead of a costly sphere for each submillimetre bead.
                var dew = new[] { .65, .86, .97 };
                double radius = .00035;
                var equator = new[] { gt+side*radius, gt+forward*radius, gt-side*radius, gt-forward*radius };
                int bead=mesh.VertexCount;
                for(int q=0;q<4;q++)
                    mesh.AddVertex(equator[q],(equator[q]-gt).Normalized(),dew,1,q/4.0,.5,1,0);
                mesh.AddVertex(gt-up*radius,-up,dew,1,.5,0,1,0);
                mesh.AddVertex(gt+up*radius,up,dew,1,.5,1,1,0);
                for (int q = 0; q < 4; q++)
                {
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        int pole=bead+(sign<0?4:5);
                        var n=(mesh.NormalAt(bead+q)+mesh.NormalAt(bead+(q+1)%4)+mesh.NormalAt(pole)).Normalized();
                        Primitives.TriangleFacing(mesh,bead+q,bead+(q+1)%4,pole,n);
                    }
                }
            }
        }
        if (((hash >> 18) & 7) == 0)
        {
            double h = (.025 + ((hash >> 10) & 15)*.00055)*scale;
            var top = foot + normal*h;
            Primitives.Tube(mesh, new[] { foot, top }, new[] { .00055, .0002 }, 6,
                (j, v) => (blue, 1, j, v, 0, 0));
            for (int b = 0; b < 3; b++)
            {
                var bp = top + new Vec3((b-1)*.0018, b*.0011, 0);
                Primitives.Ellipsoid(mesh, bp, new Vec3(.0014, .0007, .0014), 3, 5,
                    (u, v) => (pale, 1, u, v, 0, 0));
            }
        }
        // Keep living leaves and dew out of the substrate's moss texture branch.
        for (int v = firstPlantVertex; v < mesh.VertexCount; v++)
            mesh.UV2[v * 2 + 1] = -5f;
    }

    private static void AppendPlasmodiumSporangia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Physarum polycephalum sporangia: clusters of 3-6 erect wire-thin stipes with gleaming spore capsules
        var stipeCol = new[] { 0.12, 0.10, 0.08 }; // chocolate-black gleaming stipe
        var sporeCol = new[] { 0.22, 0.16, 0.09 }; // deep bronze spore mass
        var apexCol = new[] { 0.94, 0.82, 0.32 };  // golden peridium dusting / apex

        int count = 3 + (int)(hash & 3);
        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;

        for (int i = 0; i < count; i++)
        {
            double a = baseAng + i * (Math.PI * 2.0 / count) + (((hash >> (i * 3)) & 7) - 3.5) * 0.15;
            double rOffset = 0.0025 + (((hash >> (i * 4)) & 15) / 15.0) * 0.0035;
            var stalkFoot = foot + new Vec3(Math.Cos(a) * rOffset, 0, Math.Sin(a) * rOffset);

            double height = (0.0045 + (((hash >> (i * 2 + 8)) & 15) / 15.0) * 0.0035) * vigour;
            double leanX = (((hash >> (i * 3)) & 15) / 15.0 - 0.5) * 0.0015;
            double leanZ = (((hash >> (i * 3 + 4)) & 15) / 15.0 - 0.5) * 0.0015;
            var tip = stalkFoot + normal * height + new Vec3(leanX, 0, leanZ);

            // Stipe: hair-thin tapering stalk (0.35mm to 0.2mm)
            Primitives.Tube(mesh, new[] { stalkFoot, Vec3.Lerp(stalkFoot, tip, 0.5), tip }, new[] { 0.00035, 0.00028, 0.00020 }, 4,
                (idx, v) => (stipeCol, 1, 0, 0, 0, 0));

            // Sporangium head: oval/spherical capsule (~0.8mm radius)
            double capR = 0.00075 + (((hash >> (i * 2)) & 7) / 7.0) * 0.0003;
            var capCenter = tip + normal * (capR * 0.9);
            Primitives.Ellipsoid(mesh, capCenter, new Vec3(capR, capR * 1.3, capR), 3, 5,
                (u, v) => (sporeCol, 1, u, v, 0, 0));

            // Apex dusting
            Primitives.Ellipsoid(mesh, capCenter + normal * (capR * 0.7), new Vec3(capR * 0.5, capR * 0.4, capR * 0.5), 3, 4,
                (u, v) => (apexCol, 1, u, v, 0, 0));
        }
    }
}
