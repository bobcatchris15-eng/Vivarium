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
    private StandardMaterial3D _debugMatMaterial = null!;
    private StandardMaterial3D _debugCrustMaterial = null!;

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public float RebuildBudgetMs { get; set; } = 0.5f;

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
        Sundew,          // blue_sundew
        Feathermoss,     // quillfeather_moss
        Tufted,          // pinequilt_moss
        ThreadWisp,      // wispthread_moss
        Thallus,         // slickmantle
        BeardTufts,      // hagshair_lichen
        SunburstRosette, // sunscald_lichen
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
        public readonly int[,] CornerIdx = new int[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerThickness = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerMaxHeight = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly Color[,] CornerColor = new Color[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
        public readonly float[,] CornerAlpha = new float[CoverageSpec.TileEdge + 1, CoverageSpec.TileEdge + 1];
    }

    private readonly System.Collections.Concurrent.ConcurrentBag<ScratchBuffers> _scratchPool = new();

    private ScratchBuffers RentScratch() => _scratchPool.TryTake(out var s) ? s : new ScratchBuffers();

    private void ReturnScratch(ScratchBuffers s) => _scratchPool.Add(s);

    private readonly List<(CoverageLayerId layer, int ti, int tj)> _toRemove = new();
    private readonly List<(CoverageLayerId layer, int ti, int tj)> _pendingRebuilds = new();
    private readonly HashSet<(CoverageLayerId layer, int ti, int tj)> _pendingKeys = new();

    // In-view tiles used to be frozen until the camera looked away, which left moss growth
    // and sculpt edits stale in front of the player. Track how long each visible tile has
    // been dirty and refresh it after a short cooldown (or one per sync when nothing else
    // is pending). World/sculpt version changes bypass the cooldown entirely.
    private readonly Dictionary<(CoverageLayerId layer, int ti, int tj), ulong> _inViewDirtySince = new();
    private const ulong InViewRebuildCooldownMs = 2500;
    private int _lastTerrainVersion = int.MinValue;

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
        _pendingRebuilds.Clear();
        _pendingKeys.Clear();
        _inFlight.Clear();

        _matMaterial = Bridge.Shader("res://Shaders/coverage_mat.gdshader");
        _matMaterial.SetShaderParameter("u_pattern", 0.0f); // fibrous moss micro-detail
        if (float.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_MOSS_AGGREGATE"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var mossGain))
            _matMaterial.SetShaderParameter("u_moss_aggregate", Math.Clamp(mossGain, 0f, 4f));
        _matMaterial.SetShaderParameter("u_moss_colony_nuclei", (float)MossMicroGrowthStrength);
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
        SyncTiles(immediate: true);
    }

    private double _syncTimer;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Coverage");
        if (_w == null || Quality <= 0)
        {
            if (Visible) Visible = false;
            return;
        }
        if (!Visible) Visible = true;

        _syncTimer += delta;
        if (_syncTimer >= 0.25 || _inFlight.Count > 0 || _pendingRebuilds.Count > 0)
        {
            _syncTimer = 0;
            SyncTiles(immediate: false);
        }
    }

    // Wave-6 visibility: sheets pushed hard (blend ~0.6) toward per-species
    // island hues so the patch reads as a coloured island in green turf at
    // wide distance. Each target is the maximum-contrast pole against turf
    // green: rust-red, mustard, bone-white, white-blue, charcoal, gold,
    // black-green, khaki, olive gloss, pale sage, blue-grey. Legacy species
    // keep their original palette; only the 11 detailed taxa move.
    private static Color SaturateClamp(Color c, float satMult, float valueMult)
    {
        float r = c.R, g = c.G, b = c.B, a = c.A;
        if (!float.IsFinite(r)) r = 0.2f;
        if (!float.IsFinite(g)) g = 0.6f;
        if (!float.IsFinite(b)) b = 0.2f;
        float lum = r * 0.299f + g * 0.587f + b * 0.114f;
        r = lum + (r - lum) * satMult;
        g = lum + (g - lum) * satMult;
        b = lum + (b - lum) * satMult;
        r *= valueMult; g *= valueMult; b *= valueMult;
        return new Color(Mathf.Clamp(r, 0f, 1f), Mathf.Clamp(g, 0f, 1f), Mathf.Clamp(b, 0f, 1f), a);
    }

    private static void PushCoverageColors(string speciesId, ref Color c1, ref Color c2)
    {
        Color t1, t2;
        float blend;
        switch (speciesId)
        {
            case "quillfeather_moss": t1 = new Color(0.38f, 0.56f, 0.34f); t2 = new Color(0.70f, 0.88f, 0.62f); blend = 0.60f; break; // pale sage
            case "rustbellow_moss": t1 = new Color(0.50f, 0.18f, 0.08f); t2 = new Color(0.62f, 0.26f, 0.11f); blend = 0.60f; break; // rust-brown
            case "wispthread_moss": t1 = new Color(0.02f, 0.07f, 0.03f); t2 = new Color(0.06f, 0.15f, 0.07f); blend = 0.65f; break; // black-green
            case "silkgleam_moss": t1 = new Color(0.55f, 0.38f, 0.08f); t2 = new Color(0.98f, 0.78f, 0.18f); blend = 0.60f; break; // gold
            case "stoneknot_moss": t1 = new Color(0.10f, 0.11f, 0.10f); t2 = new Color(0.28f, 0.30f, 0.26f); blend = 0.60f; break; // charcoal
            case "pinequilt_moss": t1 = new Color(0.38f, 0.36f, 0.16f); t2 = new Color(0.72f, 0.66f, 0.32f); blend = 0.60f; break; // khaki
            case "slickmantle": t1 = new Color(0.42f, 0.62f, 0.24f); t2 = new Color(0.55f, 0.72f, 0.32f); blend = 0.85f; break; // bright olive sheet (wave-9: was crushing to dark at high biomass)
            case "hagshair_lichen": t1 = new Color(0.48f, 0.52f, 0.55f); t2 = new Color(0.82f, 0.86f, 0.90f); blend = 0.60f; break; // blue-grey
            case "sunscald_lichen": t1 = new Color(0.88f, 0.56f, 0.10f); t2 = new Color(0.95f, 0.66f, 0.16f); blend = 0.85f; break; // warm orange-gold sheet (wave-9)
            case "moonshell_lichen": t1 = new Color(0.78f, 0.88f, 0.96f); t2 = new Color(0.85f, 0.93f, 0.99f); blend = 0.85f; break; // pearly ice-blue sheet (wave-9)
            case "ashfingers_lichen": t1 = new Color(0.62f, 0.62f, 0.54f); t2 = new Color(0.96f, 0.96f, 0.88f); blend = 0.60f; break; // bone-white
            default: return;
        }
        c1 = SaturateClamp(c1.Lerp(t1, blend), 1.35f, 1f);
        c2 = SaturateClamp(c2.Lerp(t2, blend), 1.35f, 1f);
        c1 = SaturateClamp(c1, 1.20f, 0.90f);
        c2 = SaturateClamp(c2, 1.20f, 1.12f);
        // Wave-9 black-triangle fix: the base sheet must stay bright at high biomass
        // (b = 0.90). The double SaturateClamp above crushes these three species'
        // palettes back to dark, so pin their sheet colours to the bright targets.
        // BuildTileMesh lerps toward Color1 at high biomass, so Color1 is the sheet.
        if (speciesId == "slickmantle") { c1 = new Color(0.42f, 0.62f, 0.24f); c2 = new Color(0.55f, 0.72f, 0.32f); }
        else if (speciesId == "sunscald_lichen") { c1 = new Color(0.88f, 0.56f, 0.10f); c2 = new Color(0.95f, 0.66f, 0.16f); }
        else if (speciesId == "moonshell_lichen") { c1 = new Color(0.78f, 0.88f, 0.96f); c2 = new Color(0.85f, 0.93f, 0.99f); }
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
                PushCoverageColors(sp.Id, ref c1, ref c2);
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
                    "quillfeather_moss" => CoverageFloraType.Feathermoss,
                    "pinequilt_moss" => CoverageFloraType.Tufted,
                    "wispthread_moss" => CoverageFloraType.ThreadWisp,
                    "slickmantle" => CoverageFloraType.Thallus,
                    "silkgleam_moss" => CoverageFloraType.Velvetweave,
                    "stoneknot_moss" => CoverageFloraType.PearlCushion,
                    "rustbellow_moss" => CoverageFloraType.Sphagnum,
                    _ => CoverageFloraType.Generic,
                };
                // New coverage types need visible sheet relief, not the generic 3 mm carpet
                // cap. Wave-6: every one of the 11 taxa's sheet bulges 9-11 mm above
                // the ground so the patch itself rises above the turf plane; the
                // 3D structures below then carry well past the 3 cm turf.
                MatHeightForm effectiveForm = md.HeightForm;
                double effectiveMaxH = maxH;
                double effectiveDome = md.DomeLength > 0 ? md.DomeLength : 5.0;
                if (fType == CoverageFloraType.Feathermoss)
                {
                    effectiveMaxH = 0.010;
                    effectiveForm = MatHeightForm.Flat;
                }
                else if (fType == CoverageFloraType.Tufted)
                {
                    effectiveMaxH = 0.010;
                    effectiveForm = MatHeightForm.Dome;
                    effectiveDome = 4.0;
                }
                else if (fType == CoverageFloraType.ThreadWisp)
                {
                    effectiveMaxH = 0.010;
                    effectiveForm = MatHeightForm.Flat;
                }
                else if (fType == CoverageFloraType.Thallus)
                {
                    // Slickmantle lobes carry their own relief; keep the substrate sheet seated.
                    effectiveMaxH = 0.002;
                    effectiveForm = MatHeightForm.Flat;
                }
                // SpeciesId-gated overrides for new species sharing legacy FloraTypes, so
                // legacy species (bogglass, velvetweave, pearl_cushion) keep their tuned heights.
                else if (sp.Id == "rustbellow_moss")
                {
                    // Sphagnum hummock sheet is 11 mm; the 15 mm dome + 3 cm capitula
                    // structures sit on top of it. (Was a 75 mm sheet; the dome itself
                    // now carries the relief, as it should.)
                    effectiveMaxH = 0.011;
                    effectiveForm = MatHeightForm.Wet;
                    effectiveDome = 5.0;
                }
                else if (sp.Id == "silkgleam_moss")
                {
                    // Without this branch silkgleam stayed on the generic 3 mm carpet cap.
                    effectiveMaxH = 0.010;
                    effectiveForm = MatHeightForm.Flat;
                }
                else if (sp.Id == "stoneknot_moss")
                {
                    effectiveMaxH = 0.012;
                    effectiveForm = MatHeightForm.Dome;
                    effectiveDome = 6.0;
                }
                _matSpecies[matId] = new SpeciesRenderInfo
                {
                    SpeciesId = sp.Id,
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = effectiveForm,
                    MaxHeightM = effectiveMaxH,
                    DomeLength = effectiveDome,
                    FloraType = fType,
                };
            }
            if (sp.Lichen is { } ld)
            {
                lichenId++;
                Color c1 = sp.Color != null && sp.Color.Length >= 3 ? Bridge.C(sp.Color) : new Color(0.75f, 0.75f, 0.55f);
                Color c2 = sp.Color2 != null && sp.Color2.Length >= 3 ? Bridge.C(sp.Color2) : c1;
                PushCoverageColors(sp.Id, ref c1, ref c2);
                // The sheet has its own relief below; this is the substrate-to-sheet gap.
                double maxH = ld.Form == LichenForm.Fruticose
                    ? (sp.Height > 0 ? sp.Height : 0.008)
                    : 0.0005;
                CoverageFloraType fType = sp.Id switch
                {
                    "antlerlace_lichen" => CoverageFloraType.Antlerlace,
                    "ruffle_lichen" => CoverageFloraType.RuffleLichen,
                    "embercrust_lichen" => CoverageFloraType.Embercrust,
                    "hagshair_lichen" => CoverageFloraType.BeardTufts,
                    "sunscald_lichen" => CoverageFloraType.SunburstRosette,
                    "moonshell_lichen" => CoverageFloraType.RuffleLichen,
                    "ashfingers_lichen" => CoverageFloraType.Antlerlace,
                    _ => CoverageFloraType.Generic,
                };
                double effectiveMaxH = maxH;
                // Wave-6: every lichen sheet is 8-12 mm (these taxa had 0.5-6 mm
                // sheets the turf grass grew straight through).
                if (fType == CoverageFloraType.BeardTufts)
                    effectiveMaxH = 0.010;
                else if (fType == CoverageFloraType.SunburstRosette)
                    effectiveMaxH = 0.011;
                else if (sp.Id == "moonshell_lichen")
                    effectiveMaxH = 0.010;
                else if (sp.Id == "ashfingers_lichen")
                    effectiveMaxH = 0.010;
                _crustSpecies[lichenId] = new SpeciesRenderInfo
                {
                    SpeciesId = sp.Id,
                    Color1 = c1,
                    Color2 = c2,
                    HeightForm = MatHeightForm.Flat,
                    MaxHeightM = effectiveMaxH,
                    DomeLength = 3.0,
                    FloraType = fType,
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

    private void SyncTiles(bool immediate = false)
    {
        if (_w == null) return;

        int terrainVersion = _w.Terrain.Version;
        bool worldChanged = terrainVersion != _lastTerrainVersion;
        _lastTerrainVersion = terrainVersion;

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
            _inViewDirtySince.Remove(key);
        }

        Plane[]? frustum = null;
        if (!immediate && Camera != null)
        {
            try
            {
                var planes = Camera.GetFrustum();
                if (planes != null && planes.Count > 0)
                {
                    frustum = new Plane[planes.Count];
                    planes.CopyTo(frustum, 0);
                }
            }
            catch { }
        }

        // Allow one visible tile through per sync when the out-of-view work queue is
        // empty, so foreground growth cannot be starved by a permanently visible tile.
        int inViewAllowance = 0;
        if (!immediate && frustum != null && _pendingRebuilds.Count == 0
            && !HasDirtyOutOfViewTiles(_w.Coverage.Mat, frustum)
            && !HasDirtyOutOfViewTiles(_w.Coverage.Crust, frustum))
        {
            inViewAllowance = 1;
        }

        // Check tile versions and enqueue dirty / new tiles
        EnqueueTiles(_w.Coverage.Mat, frustum, immediate, worldChanged, ref inViewAllowance);
        EnqueueTiles(_w.Coverage.Crust, frustum, immediate, worldChanged, ref inViewAllowance);

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
            _inFlight[key] = Task.Run<MeshData?>(() => {
                var mesh = BuildTileMesh(layer,tile);
                if (mesh != null) Bridge.PrepareMeshUpload(mesh);
                return mesh;
            });
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

    private void EnqueueTiles(CoverageLayer layer, Plane[]? frustum, bool immediate,
        bool worldChanged, ref int inViewAllowance)
    {
        ulong now = Time.GetTicksMsec();
        foreach (var t in layer.Tiles)
        {
            if (t.IsEmpty()) continue;

            var key = (layer.Id, t.Ti, t.Tj);
            if (_tiles.TryGetValue(key, out var record))
            {
                if (t.Version <= record.Version)
                {
                    _inViewDirtySince.Remove(key);
                    continue;
                }

                // No frustum means we cannot prove the tile is off-screen (startup or a
                // missing camera), so rebuild rather than freezing it forever.
                bool inView = frustum != null && IsTileInFrustum(frustum, t.Ti, t.Tj);
                bool rebuild = immediate || worldChanged || !inView;
                bool usedAllowance = false;

                if (!rebuild)
                {
                    if (!_inViewDirtySince.TryGetValue(key, out ulong since))
                        _inViewDirtySince[key] = since = now;

                    bool cooledDown = now - since >= InViewRebuildCooldownMs;
                    if (cooledDown || inViewAllowance > 0)
                    {
                        rebuild = true;
                        usedAllowance = !cooledDown;
                    }
                }

                if (rebuild && _pendingKeys.Add(key))
                {
                    _inViewDirtySince.Remove(key);
                    _pendingRebuilds.Add(key);
                    if (usedAllowance) inViewAllowance--;
                }
            }
            else
            {
                // Brand new tile: enqueue so colony appears.
                _inViewDirtySince.Remove(key);
                if (_pendingKeys.Add(key))
                {
                    _pendingRebuilds.Add(key);
                }
            }
        }
    }

    /// <summary>True when a culled tile is waiting on a rebuild, so visible tiles yield to it.</summary>
    private bool HasDirtyOutOfViewTiles(CoverageLayer layer, Plane[] frustum)
    {
        foreach (var t in layer.Tiles)
        {
            if (t.IsEmpty()) continue;
            var key = (layer.Id, t.Ti, t.Tj);
            if (!_tiles.TryGetValue(key, out var record) || t.Version <= record.Version) continue;
            if (!IsTileInFrustum(frustum, t.Ti, t.Tj)) return true;
        }
        return false;
    }

    private bool IsTileInFrustum(Plane[] frustum, int ti, int tj)
    {
        const int edge = CoverageSpec.TileEdge;
        const double cs = CoverageSpec.CellSize;
        double wx = (ti + 0.5) * edge * cs;
        double wz = (tj + 0.5) * edge * cs;
        double wy = _w.Terrain.Height(new Vec2(wx, wz));
        var center = new Vector3((float)wx, (float)wy, (float)wz);
        const float radius = 2.0f;

        for (int i = 0; i < frustum.Length; i++)
        {
            if (frustum[i].DistanceTo(center) > radius)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Applies an off-thread-built (or synchronously-built) mesh result to the scene tree.</summary>
    private void CommitTile(CoverageLayer layer, CoverageTile? tile, (CoverageLayerId layer, int ti, int tj) key, MeshData? md)
    {
        _inViewDirtySince.Remove(key);
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
            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);
        var mesh = Bridge.ToArrayMesh(md, mat);
        var mi = new MeshInstance3D
        {
            Name = $"Coverage_{layer.Id}_{t.Ti}_{t.Tj}",
            Mesh = mesh,
            MaterialOverride = mat,
        };
        ConfigureCoverageInstance(mi);
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
            : (Material)(layer.Id == CoverageLayerId.Crust ? _crustMaterial : _matMaterial);
        record.Mesh = Bridge.ToArrayMesh(md, mat, record.Mesh);
        record.MeshInstance.Mesh = record.Mesh;
        record.MeshInstance.MaterialOverride = mat;
        ConfigureCoverageInstance(record.MeshInstance);
        record.Version = t.Version;
        TrianglesBuilt += md.TriangleCount;
    }

    /// <summary>Shadow-free, close-range moss/lichen detail with a hardware distance fade.</summary>
    private static void ConfigureCoverageInstance(MeshInstance3D mi)
    {
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        mi.VisibilityRangeEnd = 11.0f;
        mi.VisibilityRangeEndMargin = 1.8f;
        mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
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
        float height = Math.Max(0f, (float)maxHeight);
        float mergedPillows = 1f - MathF.Exp(-pillow * 0.9f);
        return alpha * height * (0.3f + mergedPillows * 0.7f);
    }

    private static float MatSurfaceLift(double wx, double wz, float alpha, float thickness, double maxHeight)
    {
        // D2E already supplies the colony-scale dome. Modulate that volume with pillows, rather
        // than adding a second layer whose old 14 mm minimum inflated even 3 mm carpet species.
        if (alpha <= 0 || maxHeight <= 0) return 0;
        float a = Math.Clamp(alpha, 0f, 1f);
        float edge = a * a * (3f - 2f * a);
        float pillow = SurfaceRelief(CoverageLayerId.Mat, wx, wz, 1f, maxHeight) / (float)maxHeight;
        float boundedThickness = Math.Clamp(thickness, 0f, (float)maxHeight * edge);
        return boundedThickness * (0.70f + 0.30f * pillow);
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
        float baseOffset = layer.Id == CoverageLayerId.Crust ? 0.002f : 0.0035f;
        int mossGrowthStrength = layer.Id == CoverageLayerId.Mat ? MossMicroGrowthStrength : 0;

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
            // Styled SP3 mats are not crusts: no dark prothallus line, which read as black polygons.
            if (isBoundary && !(layer.Id == CoverageLayerId.Mat && MossStyle(sp) > 0))
            {
                Color lineCol = new Color(0.08f, 0.08f, 0.07f);
                col = col.Lerp(lineCol, 0.75f);
            }

            col.A = 1f;
            s.CellColor[lx, lz] = col;
        }

        // 2. Evaluate 33x33 corner grid for continuity and edge feathering
        var cornerStyle = new int[edge + 1, edge + 1];
        for (int cz = 0; cz <= edge; cz++)
        for (int cx = 0; cx <= edge; cx++)
        {
            s.CornerIdx[cx, cz] = -1;
            int occCount = 0;
            float sumTh = 0f;
            float sumMaxHeight = 0f;
            float sumR = 0f, sumG = 0f, sumB = 0f;

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
                        if (layer.Id == CoverageLayerId.Mat)
                        {
                            var csp = GetSpecies(layer.Id, t.Occ[nlz * edge + nlx]);
                            sumMaxHeight += (float)csp.MaxHeightM;
                            if (cornerStyle[cx, cz] == 0) cornerStyle[cx, cz] = MossStyle(csp);
                        }
                        var c = s.CellColor[nlx, nlz];
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
                        if (layer.Id == CoverageLayerId.Mat)
                        {
                            sumMaxHeight += (float)nsp.MaxHeightM;
                            if (cornerStyle[cx, cz] == 0) cornerStyle[cx, cz] = MossStyle(nsp);
                        }
                        sumR += nsp.Color1.R; sumG += nsp.Color1.G; sumB += nsp.Color1.B;
                    }
                }
            }

            if (occCount == 0)
            {
                s.CornerThickness[cx, cz] = 0f;
                s.CornerMaxHeight[cx, cz] = 0f;
                s.CornerColor[cx, cz] = Colors.Black;
                s.CornerAlpha[cx, cz] = 0f;
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
                s.CornerMaxHeight[cx, cz] = sumMaxHeight * inv;
                s.CornerColor[cx, cz] = avgCol;
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
                for (int li = 0; li < localLogs.Count; li++)
                {
                    // Use the exact generated log triangles, not the cylindrical ecology proxy,
                    // so living coverage contacts the rendered sag/taper/flatten profile.
                    if (VisualLogSupport.TryTopAt(localLogs[li], wp, out double top) && !(top <= best)) best = top;
                }
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
            float alpha = s.CornerAlpha[cx, cz];
            float relief = layer.Id == CoverageLayerId.Mat ? 0f : SurfaceRelief(layer.Id, wx, wz, alpha, sp.MaxHeightM);
            float lift = baseOffset + th + relief;
            float colonization = 0f;
            if (layer.Id == CoverageLayerId.Mat)
            {
                float edgeWeight = alpha * alpha * (3f - 2f * alpha);
                float matLift = MatSurfaceLift(wx, wz, alpha, th, s.CornerMaxHeight[cx, cz]);
                lift = baseOffset * edgeWeight + matLift;
                if (mossGrowthStrength > 0)
                {
                    colonization = MossColonizationField(layer, wx, wz);
                    float strength = mossGrowthStrength >= 4 ? 1f : .48f;
                    lift = baseOffset * edgeWeight +
                        matLift * Mathf.Lerp(1f, .18f + .82f * colonization, strength);
                }
            }
            Vec3 p = new Vec3(wx, gy, wz) + gn * lift;
            Color c = s.CornerColor[cx, cz];
            double u2 = layer.Id == CoverageLayerId.Mat && mossGrowthStrength > 0
                    ? colonization : cx / (double)edge;
            double v2 = cz / (double)edge;
            // Styled SP3 mats tag their sheet with a negative UV2.y (-0.5 * style) so the shader can pick a
            // per-species surface; it also keeps the sheet from being treated as an elevated shoot.
            if (layer.Id == CoverageLayerId.Mat && cornerStyle[cx, cz] > 0) v2 = -0.5 * cornerStyle[cx, cz];
            idx =md.AddVertex(p, gn, c.R, c.G, c.B, s.CornerAlpha[cx, cz] * SlopeFade(gn), wx, wz, u2, v2);
            s.CornerIdx[cx, cz] = idx;
            return idx;
        }

        // Rock facets can be steep yet perfectly supported. Refine only moss near rocks
        // instead of dropping whole 3 cm spans and leaving coarse triangular teeth.
        var edgeMidpoints = new Dictionary<(int, int), int>();
        Vec2 SurfacePoint(int v) => new(md.UV[v * 2], md.UV[v * 2 + 1]);
        double SurfaceLift(int v)
        {
            var uv = SurfacePoint(v);
            return (md.Position(v) - Vec3.FromXZ(uv, TileGroundHeight(uv))).Length;
        }
        int SurfaceMidpoint(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (edgeMidpoints.TryGetValue(key, out int old)) return old;
            var uv = (SurfacePoint(a) + SurfacePoint(b)) * .5;
            double ground = TileGroundHeight(uv);
            var normal = TileGroundNormal(uv.X, uv.Z, ground);
            var p = Vec3.FromXZ(uv, ground) + normal * ((SurfaceLift(a) + SurfaceLift(b)) * .5);
            int vertex = md.AddVertex(p, normal,
                (md.Colors[a*4]+md.Colors[b*4])*.5, (md.Colors[a*4+1]+md.Colors[b*4+1])*.5,
                (md.Colors[a*4+2]+md.Colors[b*4+2])*.5, (md.Colors[a*4+3]+md.Colors[b*4+3])*.5,
                uv.X, uv.Z, (md.UV2[a*2]+md.UV2[b*2])*.5, (md.UV2[a*2+1]+md.UV2[b*2+1])*.5);
            edgeMidpoints[key] = vertex;
            return vertex;
        }
        double SupportPlaneError(int a, int b, int c)
        {
            var pa = SurfacePoint(a); var pb = SurfacePoint(b); var pc = SurfacePoint(c);
            double ha = TileGroundHeight(pa), hb = TileGroundHeight(pb), hc = TileGroundHeight(pc);
            double error = Math.Abs((ha+hb+hc)/3 - TileGroundHeight((pa+pb+pc)/3));
            error = Math.Max(error, Math.Abs((ha+hb)*.5 - TileGroundHeight((pa+pb)*.5)));
            error = Math.Max(error, Math.Abs((hb+hc)*.5 - TileGroundHeight((pb+pc)*.5)));
            return Math.Max(error, Math.Abs((ha+hc)*.5 - TileGroundHeight((pa+pc)*.5)));
        }
        void AddSurfaceTriangle(int a, int b, int c, int depth = 0, bool refineHagshairLog = false)
        {
            if ((layer.Id == CoverageLayerId.Mat || layer.Id == CoverageLayerId.Crust) &&
                (localRocks != null || (refineHagshairLog && localLogs != null)))
            {
                double error = SupportPlaneError(a, b, c);
                if (error > .0015 && depth < 4)
                {
                    int ab = SurfaceMidpoint(a,b), bc = SurfaceMidpoint(b,c), ca = SurfaceMidpoint(c,a);
                    AddSurfaceTriangle(a,ab,ca,depth+1,refineHagshairLog); AddSurfaceTriangle(ab,b,bc,depth+1,refineHagshairLog);
                    AddSurfaceTriangle(ca,bc,c,depth+1,refineHagshairLog); AddSurfaceTriangle(ab,bc,ca,depth+1,refineHagshairLog);
                    return;
                }
                if (error > .002) return;
            }
            else
            {
                // Preserve the older policy away from refined rock or Hagshair-log cells.
                double ya = md.Position(a).Y, yb = md.Position(b).Y, yc = md.Position(c).Y;
                if (Math.Max(ya, Math.Max(yb, yc)) - Math.Min(ya, Math.Min(yb, yc)) > .03) return;
            }
            md.AddTriangle(a,b,c);
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

            bool hagshairNearCell = false;
            if (layer.Id == CoverageLayerId.Crust && localLogs != null)
            {
                if (s.CellOccupied[lx, lz])
                {
                    hagshairNearCell = GetSpecies(layer.Id, t.Occ[lz * edge + lx]).SpeciesId == "hagshair_lichen";
                }
                else
                {
                    for (int nz = Math.Max(0, lz - 1); nz <= Math.Min(edge - 1, lz + 1) && !hagshairNearCell; nz++)
                    for (int nx = Math.Max(0, lx - 1); nx <= Math.Min(edge - 1, lx + 1) && !hagshairNearCell; nx++)
                        hagshairNearCell = s.CellOccupied[nx, nz] && GetSpecies(layer.Id, t.Occ[nz * edge + nx]).SpeciesId == "hagshair_lichen";
                }
            }
            bool refineHagshairLog = false;
            if (hagshairNearCell && localLogs != null)
            {
                var centre = new Vec2((t.Ti * edge + lx + 0.5) * cs, (t.Tj * edge + lz + 0.5) * cs);
                double terrain = _w.Terrain.Height(centre);
                for (int li = 0; li < localLogs.Count; li++)
                    if (VisualLogSupport.TryTopAt(localLogs[li], centre, out double top) && top > terrain + 0.003)
                    { refineHagshairLog = true; break; }
            }
            AddSurfaceTriangle(c00, c11, c10, refineHagshairLog: refineHagshairLog);
            AddSurfaceTriangle(c00, c01, c11, refineHagshairLog: refineHagshairLog);
        }

        if (md.TriangleCount > 0)
        {
            md.RecomputeNormals();
            // RecomputeNormals uses RH cross products; Godot expects clockwise front faces.
            // Retain the outward draped normals and flip index winding for every coverage sheet
            // (moss mat and lichen crust share the same (c00,c11,c10)/(c00,c01,c11) order).
            // Runs before the 3D structures below are appended; those emit Godot-correct winding
            // (Primitives.* / TriangleFacing, and AddStraightTube is clockwise seen from outside).
            for (int i = 0; i < md.Indices.Count; i += 3)
                    (md.Indices[i + 1], md.Indices[i + 2]) = (md.Indices[i + 2], md.Indices[i + 1]);
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
            double relief = layer.Id == CoverageLayerId.Mat ? 0 : SurfaceRelief(layer.Id, wx, wz, 1f, sp.MaxHeightM);
            var foot = layer.Id == CoverageLayerId.Mat
                ? new Vec3(wx, gy, wz) + normal * (baseOffset + MatSurfaceLift(wx, wz, 1f, s.CellThickness[lx, lz], sp.MaxHeightM) - 0.002)
                : new Vec3(wx, gy + baseOffset + s.CellThickness[lx, lz] + relief - 0.002, wz);
            float vigour = Math.Clamp(t.B[lz * edge + lx] * 3f, 0.72f, 1f);
            Color shootColor = sp.Color1.Lerp(s.CellColor[lx, lz], 0.35f);

            // An occupied coverage cell is a potential moss establishment point.
            // As living biomass fills in, its seed-stable leaflets reach neighboring
            // nuclei and merge into a canopy; dead cells leave visible openings.
            if (mossGrowthStrength > 0)
            {
                float biomass = t.B[lz * edge + lx];
                int strength = mossGrowthStrength;
                bool rim = (t.Flags[lz * edge + lx] & (byte)CoverageFlags.Rim) != 0;
                // Sparse established sites create nonuniform patches, not green noise
                // sprayed on every cell. The default tier (1) is the production budget:
                // only dense cushions are eligible, and a stable 2-bit hash stride grows
                // roughly one cluster in four. Tiers 2-4 are progressively denser
                // comparison fixtures and intentionally relax the establishment floor.
                float biomassFloor = strength >= 2 ? 0.03f : 0.55f;
                int threshold = strength switch
                {
                    >= 4 => 224,
                    3 => 176,
                    2 => 120,
                    _ => 64,
                };
                if (biomass >= biomassFloor && (hash >> 24) < threshold)
                    AppendMossGrowthNucleus(md, foot, normal, s.CellColor[lx, lz],
                        sp.Color2, hash, biomass, sp.MaxHeightM, rim, strength);
            }

            switch (sp.FloraType)
            {
                case CoverageFloraType.Sphagnum:
                    if (sp.SpeciesId == "rustbellow_moss")
                        AppendRustbellowDome(md, foot, normal, sp.Color1, sp.Color2, hash, vigour);
                    else if ((hash >> 24) < 140)
                        AppendSphagnumShoot(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.PearlCushion:
                    if (sp.SpeciesId == "stoneknot_moss")
                        AppendStoneknotCushion(md, foot, normal, sp.Color1, sp.Color2, hash, vigour);
                    else if ((hash >> 24) < 150)
                        AppendPearlCushionStructures(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Floodlace:
                    if ((hash >> 24) < 190)
                        AppendFloodlaceFronds(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Velvetweave:
                    if (sp.SpeciesId == "silkgleam_moss")
                        AppendSilkgleamTurf(md, foot, normal, sp.Color1, sp.Color2, hash, vigour);
                    else if ((hash >> 24) < 175)
                        AppendVelvetweaveTurf(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Antlerlace:
                    if (sp.SpeciesId == "ashfingers_lichen")
                        AppendAshfingersPodetia(md, foot, normal, sp.Color1, sp.Color2, hash, vigour);
                    else if ((hash >> 24) < 85)
                        AppendAntlerlacePodetia(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.RuffleLichen:
                    if (sp.SpeciesId == "moonshell_lichen")
                        AppendMoonshellLobes(md, foot, normal, sp.Color1, sp.Color2, hash, vigour);
                    else if ((hash >> 24) < 90)
                        AppendRuffleLobes(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Embercrust:
                    if ((hash >> 24) < 120)
                        AppendEmbercrustApothecia(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Sundew:
                    if ((hash >> 24) < 125)
                        AppendBlueSundewRosette(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Feathermoss:
                    // Every occupied cell gets fronds: 2-3 per call is already the spec floor.
                    {
                        AppendFeatherFronds(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                        if (((hash >> 16) & 255) < 24)
                            AppendFeatherFronds(md, foot + new Vec3((((hash >> 4) & 15) / 15.0 - 0.5) * 0.008, 0, (((hash >> 12) & 15) / 15.0 - 0.5) * 0.008), normal, shootColor, sp.Color2, hash ^ 0xB5297A4D, vigour);
                        if (((hash >> 8) & 255) < 16)
                            AppendFeatherFronds(md, foot + new Vec3((((hash >> 20) & 15) / 15.0 - 0.5) * 0.008, 0, (((hash >> 6) & 15) / 15.0 - 0.5) * 0.008), normal, shootColor, sp.Color2, hash ^ 0x5A3E1C90, vigour);
                    }
                    break;
                case CoverageFloraType.Tufted:
                    AppendTuftedShoots(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.ThreadWisp:
                    AppendThreadWisps(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.Thallus:
                    // Every cell gets lobes (3-4 per call); the offset calls add density.
                    {
                        AppendThallusLobes(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                        if (((hash >> 16) & 255) < 28)
                            AppendThallusLobes(md, foot + new Vec3((((hash >> 4) & 15) / 15.0 - 0.5) * 0.008, 0, (((hash >> 12) & 15) / 15.0 - 0.5) * 0.008), normal, shootColor, sp.Color2, hash ^ 0x1B873593, vigour);
                        if (((hash >> 8) & 255) < 18)
                            AppendThallusLobes(md, foot + new Vec3((((hash >> 20) & 15) / 15.0 - 0.5) * 0.008, 0, (((hash >> 6) & 15) / 15.0 - 0.5) * 0.008), normal, shootColor, sp.Color2, hash ^ 0x8EB2F149, vigour);
                    }
                    break;
                case CoverageFloraType.BeardTufts:
                    // On ground/rock the compact colony stays unchanged. For a log-supported
                    // Hagshair cell, use only bark-rooted strands; generic tufts on the steep
                    // shoulder were visibly detached from the host.
                    if (sp.SpeciesId == "hagshair_lichen" && localLogs != null)
                    {
                        var wp = new Vec2(wx, wz);
                        double bestLogTop = gy;
                        double terrainY = _w.Terrain.Height(wp);
                        LogProp? supportedLog = null;
                        foreach (var log in localLogs)
                        {
                            if (VisualLogSupport.TryTopAt(log, wp, out double top) &&
                                top > terrainY + 0.003 && top >= bestLogTop - 0.002)
                            { bestLogTop = top; supportedLog = log; }
                        }
                        if (supportedLog is { } host)
                        {
                            if ((hash & 3) == 0)
                            {
                                var axis2 = host.Axis.Normalized();
                                var side2 = new Vec2(-axis2.Z, axis2.X);
                                var rel2 = wp - host.Position;
                                double along = Math.Clamp(rel2.Dot(axis2), -host.Length * 0.49, host.Length * 0.49);
                                double sideSign = rel2.Dot(side2) < 0 ? -1.0 : 1.0;
                                foreach (double shoulderSin in new[] { 0.76, 0.64, 0.52, 0.40 })
                                {
                                    var shoulder2 = host.Position + axis2 * along + side2 * (sideSign * host.Radius * shoulderSin);
                                    if (!VisualLogSupport.TryTopAt(host, shoulder2, out double shoulderY) ||
                                        shoulderY <= _w.Terrain.Height(shoulder2) + 0.004) continue;
                                    double shoulderCos = Math.Sqrt(1.0 - shoulderSin * shoulderSin);
                                    var barkNormal = new Vec3(side2.X * sideSign * shoulderSin, shoulderCos, side2.Z * sideSign * shoulderSin).Normalized();
                                    var barkRoot = new Vec3(shoulder2.X, shoulderY, shoulder2.Z) + barkNormal * 0.001;
                                    AppendHagshairLogCurls(md, barkRoot, new Vec3(axis2.X, 0, axis2.Z), barkNormal,
                                        shootColor, hash ^ 0x9E3779B9, vigour, host.Radius);
                                    break;
                                }
                            }
                        }
                        else AppendBeardCurls(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    }
                    else AppendBeardCurls(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                    break;
                case CoverageFloraType.SunburstRosette:
                    // Every cell gets a rosette (8-12 lobes); the offset calls add density.
                    {
                        AppendSunburstLobes(md, foot, normal, shootColor, sp.Color2, hash, vigour);
                        if (((hash >> 16) & 255) < 65)
                            AppendSunburstLobes(md, foot + new Vec3((((hash >> 4) & 15) / 15.0 - 0.5) * 0.006, 0, (((hash >> 12) & 15) / 15.0 - 0.5) * 0.006), normal, shootColor, sp.Color2, hash ^ 0x3C6EF372, vigour);
                        if (((hash >> 8) & 255) < 45)
                            AppendSunburstLobes(md, foot + new Vec3((((hash >> 20) & 15) / 15.0 - 0.5) * 0.006, 0, (((hash >> 6) & 15) / 15.0 - 0.5) * 0.006), normal, shootColor, sp.Color2, hash ^ 0xA5C9E643, vigour);
                    }
                    break;
            }
        }

        return md;
    }

    // Structure vertices (blades, domes, setae) carry UV2.y = -2.5 so the mat shader shades them as plain tissue.
    private static int V(MeshData m, Vec3 p, Vec3 n, double[] c, double a = 1) => m.AddVertex(p, n, c, a, 0, 0, 0, -2.5);
    // ---- SP3: four wetland/cushion moss mats share these low-poly builders ----
    private static int MossStyle(SpeciesRenderInfo sp) => sp.SpeciesId switch
    {
        "floodlace_moss" => 1,
        "pearl_cushion_moss" => 2,
        "velvetweave_moss" => 3,
        "bogglass_moss" => 4,
        _ => 0,
    };

    private static void MossBasis(Vec3 normal, out Vec3 up, out Vec3 tu, out Vec3 tv)
    {
        up = normal.Normalized();
        tu = Math.Abs(up.Y) < 0.99 ? Vec3.Up.Cross(up).Normalized() : Vec3.Right;
        tv = up.Cross(tu).Normalized();
    }

    private static double[] Shade(Color c, double r, double g, double b) =>
        new[] { Math.Clamp(c.R * r, 0, 1), Math.Clamp(c.G * g, 0, 1), Math.Clamp(c.B * b, 0, 1) };

    /// <summary>Tapered, arched leaflet (3 triangles) lying on the substrate tangent plane.</summary>
    private static void MossBlade(MeshData mesh, Vec3 root, Vec3 up, Vec3 dir, double length, double width,
        double rise, double[] cBase, double[] cTip)
    {
        var wd = up.Cross(dir).Normalized();
        var m = root + dir * (length * 0.5) + up * (rise * 0.85);
        var tip = root + dir * length + up * rise;
        var n = (up * 0.8 + dir * 0.25).Normalized();
        var cMid = new[] { (cBase[0] + cTip[0]) * 0.5, (cBase[1] + cTip[1]) * 0.5, (cBase[2] + cTip[2]) * 0.5 };
        int a = V(mesh, root - wd * (width * 0.35), n, cBase, 1);
        int b = V(mesh, root + wd * (width * 0.35), n, cBase, 1);
        int c = V(mesh, m - wd * (width * 0.5), n, cMid, 1);
        int d = V(mesh, m + wd * (width * 0.5), n, cMid, 1);
        int e = V(mesh, tip, n, cTip, 1);
        Primitives.TriangleFacing(mesh, a, b, c, up);
        Primitives.TriangleFacing(mesh, b, d, c, up);
        Primitives.TriangleFacing(mesh, c, d, e, up);
    }

    /// <summary>Squat 7-sided cap (21 tris): dark rim, pale crown.</summary>
    private static void MossDome(MeshData mesh, Vec3 centre, Vec3 up, Vec3 tu, Vec3 tv, double radius, double height,
        double[] cRim, double[] cMid, double[] cTop, double phase)
    {
        const int N = 7;
        int tip = V(mesh, centre + up * height, up, cTop, 1);
        var mids = new int[N];
        var rims = new int[N];
        for (int i = 0; i < N; i++)
        {
            double a = phase + i * Math.PI * 2 / N;
            var radial = tu * Math.Cos(a) + tv * Math.Sin(a);
            mids[i] = V(mesh, centre + radial * (radius * 0.72) + up * (height * 0.74),
                (radial * 0.62 + up * 0.78).Normalized(), cMid, 1);
            rims[i] = V(mesh, centre + radial * radius + up * (height * 0.04),
                (radial * 0.92 + up * 0.38).Normalized(), cRim, 1);
        }
        for (int i = 0; i < N; i++)
        {
            int j = (i + 1) % N;
            Primitives.TriangleFacing(mesh, tip, mids[i], mids[j], up);
            Primitives.TriangleFacing(mesh, mids[i], rims[i], rims[j], up);
            Primitives.TriangleFacing(mesh, mids[i], rims[j], mids[j], up);
        }
    }

    private static void AppendSphagnumShoot(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        // Glassy sphagnum: short pale stem carrying a star-shaped capitulum (dense crown, drooping branches).
        MossBasis(normal, out var up, out var tu, out var tv);
        double height = (0.008 + ((hash >> 16) & 255) / 255.0 * 0.007) * vigour;
        double leanX = (((hash >> 4) & 15) / 15.0 - 0.5) * 0.004;
        double leanZ = (((hash >> 12) & 15) / 15.0 - 0.5) * 0.004;
        var tip = foot + up * height + tu * leanX + tv * leanZ;
        var stemCol = Shade(color, 1.05, 1.02, 0.85);
        Primitives.Tube(mesh, new[] { foot, Vec3.Lerp(foot, tip, 0.55), tip }, new[] { 0.0016, 0.0013, 0.0010 }, 4,
            (i, v) => (stemCol, 1, 0, 0, 0, -2.5));

        var branchBase = Shade(color, 1.0, 1.0, 0.85);
        float rust = ((hash >> 20) & 15) / 15f;
        var crown = Shade(color.Lerp(color2, 0.12f + 0.22f * rust), 1.22, 1.18, 1.05);
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;
        for (int whorl = 0; whorl < 2; whorl++)
        {
            double t = whorl == 1 ? 1.0 : 0.62;
            var node = Vec3.Lerp(foot, tip, t);
            int branches = whorl == 1 ? 8 : 5;
            double reach = whorl == 1 ? 0.0085 : 0.0105;
            for (int b = 0; b < branches; b++)
            {
                uint vr = ReliefHash(unchecked((int)hash) + b * 31, whorl * 17 + b);
                double a = phase + b * Math.PI * 2 / branches + whorl * 0.4 + ((vr & 255) / 255.0 - 0.5) * 0.25;
                var dir = (tu * Math.Cos(a) + tv * Math.Sin(a));
                double len = reach * (0.8 + ((vr >> 8) & 255) / 255.0 * 0.4);
                double droop = whorl == 1 ? 0.0012 : -0.0030;
                MossBlade(mesh, node, up, dir, len, 0.0028, droop, branchBase, crown);
            }
        }
        if ((hash & 15) == 0)
        {
            var stalkTip = tip + up * (0.007 + ((hash >> 8) & 15) * 0.0003);
            Primitives.Tube(mesh, new[] { tip, stalkTip }, new[] { 0.0006, 0.0004 }, 4,
                (i, v) => (new[] { 0.58, 0.48, 0.30 }, 1, 0, 0, 0, -2.5));
            Primitives.Tube(mesh, new[] { stalkTip, stalkTip + up * 0.0028 }, new[] { 0.0014, 0.0006 }, 5,
                (i, v) => (new[] { 0.46, 0.30, 0.16 }, 1, 0, 0, 0, -2.5));
        }
    }

    private static void AppendPearlCushionStructures(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Domed silver-tipped cushion heads packed shoulder to shoulder; rare short seta + capsule.
        MossBasis(normal, out var up, out var tu, out var tv);
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;
        double radius = 0.0058 + ((hash >> 16) & 255) / 255.0 * 0.0030;
        double height = (0.0034 + ((hash >> 8) & 255) / 255.0 * 0.0030) * vigour;
        var centre = foot + up * 0.0003 + tu * ((((hash >> 4) & 15) / 15.0 - 0.5) * 0.005) + tv * ((((hash >> 24) & 15) / 15.0 - 0.5) * 0.005);
        var rim = Shade(col1, 0.95, 0.98, 0.90);
        var mid = Shade(col1, 1.25, 1.25, 1.15);
        var pearl = new[]
        {
            Math.Min(1, col2.R * 0.55 + 0.46), Math.Min(1, col2.G * 0.45 + 0.52), Math.Min(1, col2.B * 0.55 + 0.42),
        };
        MossDome(mesh, centre, up, tu, tv, radius, height, rim, mid, pearl, phase);

        // Hyaline awns: three short pearl-white hair points off the crown.
        var crownPt = centre + up * height;
        var awn = new[] { 0.95, 0.97, 0.90 };
        for (int p = 0; p < 3; p++)
        {
            double a = phase + p * Math.PI * 2 / 3.0 + 0.5;
            var rad = tu * Math.Cos(a) + tv * Math.Sin(a);
            var side = up.Cross(rad).Normalized() * 0.00035;
            var end = crownPt + rad * 0.0030 + up * 0.0042;
            int v0 = V(mesh, crownPt - side, up, awn, 0.95);
            int v1 = V(mesh, crownPt + side, up, awn, 0.95);
            int vt = V(mesh, end, up, awn, 0.98);
            Primitives.TriangleFacing(mesh, v0, v1, vt, up);
        }

        if (((hash >> 12) & 15) == 0)
        {
            var top = crownPt + up * (0.010 + ((hash >> 20) & 7) * 0.0008) + tu * 0.0015;
            Primitives.Tube(mesh, new[] { crownPt, top }, new[] { 0.0004, 0.0003 }, 4,
                (i, v) => (new[] { 0.66, 0.55, 0.30 }, 1, 0, 0, 0, -2.5));
            Primitives.Tube(mesh, new[] { top, top + up * 0.0032 }, new[] { 0.0011, 0.0004 }, 5,
                (i, v) => (new[] { 0.80, 0.68, 0.36 }, 1, 0, 0, 0, -2.5));
        }
    }

    private static void AppendFloodlaceFronds(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Lacy wet fronds: a flat arching runner with pinnate leaflets streaming along it.
        MossBasis(normal, out var up, out var tu, out var tv);
        double ang = (hash & 1023) / 1023.0 * Math.PI * 2;
        var dir = tu * Math.Cos(ang) + tv * Math.Sin(ang);
        var side = up.Cross(dir).Normalized();
        double reach = (0.014 + ((hash >> 16) & 255) / 255.0 * 0.010) * vigour;
        var pts = new[]
        {
            foot + up * 0.0008,
            foot + dir * (reach * 0.38) + side * 0.0022 + up * 0.0022,
            foot + dir * (reach * 0.72) - side * 0.0018 + up * 0.0024,
            foot + dir * reach + up * 0.0012,
        };
        var widths = new[] { 0.0016, 0.0013, 0.0009 };
        var stemCol = Shade(col1, 0.75, 0.85, 0.80);
        int prevL = -1, prevR = -1;
        for (int i = 0; i < 3; i++)
        {
            int l = V(mesh, pts[i] - side * widths[i], up, stemCol, 1);
            int r = V(mesh, pts[i] + side * widths[i], up, stemCol, 1);
            if (prevL >= 0)
            {
                Primitives.TriangleFacing(mesh, prevL, prevR, l, up);
                Primitives.TriangleFacing(mesh, prevR, r, l, up);
            }
            prevL = l; prevR = r;
        }
        int endV = V(mesh, pts[3], up, stemCol, 1);
        Primitives.TriangleFacing(mesh, prevL, prevR, endV, up);

        var leafBase = Shade(col1, 0.95, 1.05, 1.0);
        var leafTip = Shade(col2.Lerp(col1, 0.3f), 1.15, 1.2, 1.15);
        for (int k = 0; k < 4; k++)
        {
            double t = 0.18 + k * 0.22;
            var node = Vec3.Lerp(Vec3.Lerp(pts[0], pts[1], Math.Min(1, t * 2.6)), pts[3], t * 0.9);
            double len = 0.0052 * (1.1 - t * 0.5) * (0.85 + ((hash >> (k * 3)) & 7) / 7.0 * 0.3);
            for (int sg = -1; sg <= 1; sg += 2)
            {
                double pa = 0.95 + ((hash >> (k * 2 + 5)) & 3) * 0.08;
                var pdir = (dir * Math.Cos(pa) + side * (sg * Math.Sin(pa))).Normalized();
                MossBlade(mesh, node, up, pdir, len, 0.0022, 0.0030, leafBase, leafTip);
            }
        }

        if ((hash & 15) == 0)
        {
            var stalkNode = Vec3.Lerp(pts[0], pts[2], 0.5);
            var stalkTip = stalkNode + side * 0.003 + up * 0.0085;
            Primitives.Tube(mesh, new[] { stalkNode, stalkTip }, new[] { 0.0004, 0.0003 }, 4,
                (i, v) => (new[] { 0.62, 0.52, 0.30 }, 1, 0, 0, 0, -2.5));
            Primitives.Tube(mesh, new[] { stalkTip, stalkTip + side * 0.0016 + up * 0.0018 }, new[] { 0.0011, 0.0004 }, 4,
                (i, v) => (new[] { 0.72, 0.58, 0.30 }, 1, 0, 0, 0, -2.5));
        }
    }

    private static void AppendVelvetweaveTurf(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Velvet nap: short pointed shoots all combed the same way across a patch, tips gilded.
        MossBasis(normal, out var up, out var tu, out var tv);
        var deep = Shade(col1, 0.62, 0.70, 0.60);
        var tipC = Shade(col2.Lerp(col1, 0.25f), 1.12, 1.10, 1.0);
        double grain = ValueNoise(foot.X, foot.Z, 0.28) * Math.PI * 1.6;
        for (int j = 0; j < 7; j++)
        {
            uint jh = ReliefHash(unchecked((int)hash) + j * 53, j * 11 + 3);
            double pa = (jh & 255) / 255.0 * Math.PI * 2;
            var off = (tu * Math.Cos(pa) + tv * Math.Sin(pa)) * (0.002 + ((jh >> 8) & 15) * 0.0003);
            double da = grain + (((jh >> 12) & 255) / 255.0 - 0.5) * 0.9;
            var dir = tu * Math.Cos(da) + tv * Math.Sin(da);
            double len = (0.0075 + ((jh >> 20) & 7) * 0.0008) * vigour;
            MossBlade(mesh, foot + off, up, dir, len, 0.0020, 0.0042 + ((jh >> 24) & 7) * 0.0004, deep, tipC);
        }

        if ((hash & 31) == 0)
        {
            double height = (0.016 + ((hash >> 16) & 255) / 255.0 * 0.008) * vigour;
            var tip = foot + up * height;
            Primitives.Tube(mesh, new[] { foot, tip }, new[] { 0.0004, 0.0003 }, 4,
                (i, v) => (new[] { 0.62, 0.42, 0.20 }, 1, 0, 0, 0, -2.5));
            Primitives.Tube(mesh, new[] { tip, tip + tu * 0.0018 - up * 0.0010 }, new[] { 0.0007, 0.0011 }, 5,
                (i, v) => (new[] { 0.80, 0.68, 0.34 }, 1, 0, 0, 0, -2.5));
        }
    }

    private static void AddStraightTube(MeshData mesh, Vec3 a, Vec3 b, double r0, double r1, int sides, double[] col)
    {
        var axis = b - a;
        double len = axis.Length;
        if (len < 1e-9) return;
        axis = axis / len;
        Vec3 hint = Math.Abs(axis.Y) < 0.9 ? Vec3.Up : new Vec3(1, 0, 0);
        var side = axis.Cross(hint);
        if (side.LengthSq < 1e-8) side = axis.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var up = side.Cross(axis).Normalized();

        int start = mesh.VertexCount;
        for (int ring = 0; ring < 2; ring++)
        {
            var center = ring == 0 ? a : b;
            double r = ring == 0 ? r0 : r1;
            for (int i = 0; i < sides; i++)
            {
                double th = 2 * Math.PI * i / sides;
                var radial = side * Math.Cos(th) + up * Math.Sin(th);
                var n = radial;
                mesh.AddVertex(center + radial * r, n, col, 1, (double)i / sides, ring, 0, 0);
            }
        }
        for (int i = 0; i < sides; i++)
        {
            int i0 = i, i1 = (i + 1) % sides;
            int a0 = start + i0, a1 = start + i1;
            int b0 = start + sides + i0, b1 = start + sides + i1;
            mesh.AddTriangle(a0, a1, b0);
            mesh.AddTriangle(a1, b1, b0);
        }
    }

    private static void AppendAntlerlacePodetia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Fruticose lichen: dense dichotomous 3D branching podetia bushes.
        var podetiaCol = new[] { col1.R * 1.05, col1.G * 1.12, col1.B * 1.02 };
        var tipCol = new[] { Math.Min(1.0, col1.R * 1.35 + 0.15), Math.Min(1.0, col1.G * 1.25 + 0.10), Math.Min(1.0, col1.B * 1.15 + 0.05) };

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        int stemCount = 4 + (int)((hash >> 2) & 3); // 4..7
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;

        for (int s = 0; s < stemCount; s++)
        {
            uint h = ReliefHash(unchecked((int)hash) + s * 97, s * 13 + 7);
            double ang = phase + s * Math.PI * 2.0 / stemCount + ((h & 255) / 255.0 - 0.5) * 0.35;
            var radial = (right * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            double clusterR = 0.0015 + ((h >> 8) & 255) / 255.0 * 0.0035;
            var basePos = foot + radial * clusterR;
            double height = (0.006 + ((h >> 16) & 255) / 255.0 * 0.008) * vigour;

            var trunkTop = basePos + normal * (height * 0.38);
            AddStraightTube(mesh, basePos, trunkTop, 0.0016, 0.0012, 5, podetiaCol);

            var work = new List<(Vec3 start, Vec3 dir, double len, double rad, int depth)>();
            var firstDir = (normal * 0.55 + radial * 0.45).Normalized();
            work.Add((trunkTop, firstDir, height * 0.30, 0.0010, 0));
            while (work.Count > 0)
            {
                var (start, dir, len, rad, depth) = work[work.Count - 1];
                work.RemoveAt(work.Count - 1);
                var end = start + dir * len;
                double r1 = depth >= 2 ? rad * 0.55 : rad * 0.80;
                AddStraightTube(mesh, start, end, rad, r1, 5, podetiaCol);

                if (depth < 3)
                {
                    var perp = dir.Cross(normal);
                    if (perp.LengthSq < 1e-8) perp = dir.Cross(right);
                    perp = perp.Normalized();
                    for (int f = -1; f <= 1; f += 2)
                    {
                        var forkDir = (dir * 0.72 + perp * (f * 0.32) + normal * 0.18).Normalized();
                        work.Add((end, forkDir, len * 0.72, rad * 0.72, depth + 1));
                    }
                }
                else
                {
                    Primitives.Ellipsoid(mesh, end + dir * 0.0004, new Vec3(0.0006, 0.0006, 0.0006), 3, 4,
                        (u, v) => (tipCol, 1, u, v, 0, 0));
                }
            }
        }
    }

    private static void AppendRuffleLobes(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Foliose lichen: 6-10 imbricate arched shells with concave apothecia cups.
        var upperCol = new[] { (double)col1.R * 1.05, (double)col1.G * 1.05, (double)col1.B * 1.02 };
        var underCol = new[] { 0.28, 0.25, 0.22 };
        var pruinoseCol = new[] { Math.Min(1.0, col1.R * 1.4 + 0.15), Math.Min(1.0, col1.G * 1.35 + 0.12), Math.Min(1.0, col1.B * 1.3 + 0.10) };
        var discCol = new[] { 0.85, 0.50, 0.20 };
        var rimCol = new[] { 0.92, 0.85, 0.60 };

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        int lobeCount = 6 + (int)((hash >> 4) & 7);
        if (lobeCount > 10) lobeCount = 10;
        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;

        for (int l = 0; l < lobeCount; l++)
        {
            uint h = ReliefHash(unchecked((int)hash) + l * 53, l * 29 + 11);
            double a = baseAng + l * Math.PI * 2.0 / lobeCount + ((h & 255) / 255.0 - 0.5) * 0.25;
            var radial = (right * Math.Cos(a) + fwd * Math.Sin(a)).Normalized();
            var lateral = (right * (-Math.Sin(a)) + fwd * Math.Cos(a)).Normalized();

            double len = (0.008 + ((h >> 8) & 255) / 255.0 * 0.006) * vigour;
            double lift = (0.0015 + ((h >> 16) & 255) / 255.0 * 0.0030) * vigour;
            double halfW = 0.0025 + ((h >> 4) & 15) * 0.0003;

            int segs = 4;
            int across = 3;
            int[,] U = new int[segs + 1, across + 1];
            // Wave-9 black-triangle fix: upper surface only. The lower surface L
            // was emitted at the same position with -nrm and dark underCol, and
            // with cull_disabled it overwrote U and rendered pitch black.
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                for (int j = 0; j <= across; j++)
                {
                    double x = (double)j / across * 2.0 - 1.0;
                    double widthScale = 1.0 - t * 0.25;
                    double lateralOff = x * halfW * widthScale;
                    double ruffle = 0.0008 * Math.Sin(3.0 * x + t * 4.0 + a * 2.0);
                    double height = lift * t * t + ruffle * (0.4 + 0.6 * t);
                    if (t > 0.7) height += (t - 0.7) * 0.002;
                    var pos = foot + radial * (len * t) + lateral * lateralOff + normal * height;
                    var nrm = (radial * 0.3 + normal * (1.0 - t * 0.3)).Normalized();
                    double[] col = t > 0.8 ? pruinoseCol : upperCol;
                    U[i, j] = mesh.AddVertex(pos, nrm, col, 1, t, (x + 1) * 0.5);
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < across; j++)
                {
                    Primitives.TriangleFacing(mesh, U[i, j], U[i + 1, j], U[i, j + 1], normal);
                    Primitives.TriangleFacing(mesh, U[i, j + 1], U[i + 1, j], U[i + 1, j + 1], normal);
                }
        }

        int cupCount = 1 + (int)((hash >> 20) & 1);
        for (int c = 0; c < cupCount; c++)
        {
            uint h = ReliefHash(unchecked((int)hash) + c * 71, c * 19 + 3);
            double ca = (h & 1023) / 1023.0 * Math.PI * 2;
            double cr = 0.002 + ((h >> 10) & 255) / 255.0 * 0.004;
            var cupCenter = foot + (right * Math.Cos(ca) + fwd * Math.Sin(ca)) * cr + normal * 0.0015;
            double cupD = 0.0012 + ((h >> 18) & 255) / 255.0 * 0.0016;
            double cupR = cupD * 0.5;
            int rimSegs = 8;
            var rimPath = new List<Vec3>();
            for (int r = 0; r < rimSegs; r++)
            {
                double th = 2 * Math.PI * r / rimSegs;
                var radial = right * Math.Cos(th) + fwd * Math.Sin(th);
                rimPath.Add(cupCenter + radial * cupR + normal * 0.0006);
            }
            rimPath.Add(rimPath[0]);
            var rimRadii = new double[rimPath.Count];
            for (int r = 0; r < rimRadii.Length; r++) rimRadii[r] = cupR * 0.18;
            Primitives.Tube(mesh, rimPath, rimRadii, rimSegs,
                (i, v) => (rimCol, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, cupCenter + normal * 0.0002, new Vec3(cupR * 0.85, 0.00035, cupR * 0.85), 3, 6,
                (u, v) => (discCol, 1, u, v, 0, 0));
        }
    }

    private static void AppendEmbercrustApothecia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        // Crustose lichen: clustered orange/amber apothecia discs near colony center.
        var rimCol = new[] { 0.90, 0.78, 0.42 };
        var discCol = new[] { 0.95, 0.42, 0.10 };
        var centerCol = new[] { 0.75, 0.30, 0.06 };

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        int discCount = 3 + (int)((hash & 3));
        for (int d = 0; d < discCount; d++)
        {
            uint h = ReliefHash(unchecked((int)hash) + d * 41, d * 17 + 5);
            double ca = (h & 1023) / 1023.0 * Math.PI * 2 + d * 0.7;
            double cr = 0.0012 * (d + 1) * 0.6;
            if (cr > 0.0045) cr = 0.0045;
            var center = foot + (right * Math.Cos(ca) + fwd * Math.Sin(ca)) * cr + normal * 0.0007;
            double r = 0.0013 + ((h >> 8) & 255) / 255.0 * 0.0012;

            Primitives.Tube(mesh, new[] { center, center + normal * 0.0007 }, new[] { r * 0.92, r }, 6,
                (i, v) => (rimCol, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, center + normal * 0.00075, new Vec3(r * 0.8, 0.00025, r * 0.8), 3, 6,
                (u, v) => (discCol, 1, u, v, 0, 0));
            if ((h & 1) == 0)
                Primitives.Ellipsoid(mesh, center + normal * 0.00085, new Vec3(r * 0.35, 0.0002, r * 0.35), 3, 4,
                    (u, v) => (centerCol, 1, u, v, 0, 0));
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

    private static void AppendFeatherFronds(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        // Floor at 1.0 so spec lengths hold at seed biomass (0.08), where vigour is 0.72.
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Wave-8: olive-sage prostrate feather mat, base (0.42,0.62,0.32) to
        // tips (0.68,0.82,0.48). Harness no longer grows grass over bare
        // terrain, so fronds lie flat on the sheet instead of clearing turf.
        var baseCol = new[] { 0.42, 0.62, 0.32 };
        var axisCol = new[] { 0.38, 0.56, 0.29 };
        var leafCol = new[] { 0.68, 0.82, 0.48 };

        int frondCount = 2; // sparse rooted fans; neighboring nuclei overlap the patch
        for (int f = 0; f < frondCount; f++)
        {
            uint h = ReliefHash(unchecked((int)hash) + f * 61, f * 23 + 5);
            double ang = (h & 1023) / 1023.0 * Math.PI * 2 + f * 2.2;
            var radial = (right * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            if (radial.LengthSq < 1e-8) radial = right;
            var lateral = (right * (-Math.Sin(ang)) + fwd * Math.Cos(ang)).Normalized();
            if (lateral.LengthSq < 1e-8) lateral = fwd;

            // Wave-8: 3.5-5 cm fronds lying PROSTRATE on the mat: low arch
            // 4-7 mm off the sheet (not 4 cm upright spears).
            double length = (0.035 + ((h >> 8) & 255) / 255.0 * 0.015) * v; // 3.5-5 cm
            var basePos = foot + radial * (f * 0.003);
            var mid = basePos + radial * (length * 0.5) + normal * (0.004 + ((h >> 12) & 7) * 0.00040); // 4-7 mm arch
            var tipPos = basePos + radial * length + normal * 0.003;
            Primitives.Tube(mesh, new[] { basePos, mid, tipPos }, new[] { 0.00070, 0.00052, 0.00030 }, 6,
                (i, vv) => (axisCol, 1, 0, 0, 0, 0));

            int pairs = 6 + (int)(((h >> 16) & 255) % 3); // 6..8 paired pinnae
            for (int p = 1; p <= pairs; p++)
            {
                double t = (double)p / (pairs + 1);
                uint hb = ReliefHash(unchecked((int)hash) + f * 131 + p * 37, p * 19 + 7);
                var node = basePos + radial * (length * t) + normal * (0.002 + 0.004 * Math.Sin(Math.PI * t));
                double pinLen = (0.0080 * (1.0 - t * 0.35) + ((hb & 15) * 0.00025)) * v;
                for (int sSign = -1; sSign <= 1; sSign += 2)
                {
                    // Pinnae fan flat horizontally along the mat.
                    var pinDir = (radial * 0.30 + lateral * (sSign * 0.95)).Normalized();
                    if (pinDir.LengthSq < 1e-8) pinDir = lateral * sSign;
                    var leafSide = new Vec3(-pinDir.Z, 0, pinDir.X) * 0.0010;
                    var pinTip = node + pinDir * pinLen + normal * (0.0008 + ((hb >> 6) & 3) * 0.00020);
                    int v0 = mesh.AddVertex(node - leafSide, normal, baseCol, 1);
                    int v1 = mesh.AddVertex(node + leafSide, normal, baseCol, 1);
                    int vTip = mesh.AddVertex(pinTip, normal, leafCol, 1);
                    Primitives.TriangleFacing(mesh, v0, v1, vTip, normal);
                }
            }
        }
    }

    private static void AppendTuftedShoots(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Wave-8: warm khaki-olive (0.45,0.50,0.24) base to (0.68,0.66,0.34) tips.
        var deepCol = new[] { 0.45, 0.50, 0.24 };
        var tipCol = new[] { 0.68, 0.66, 0.34 };

        int tuftCount = 2 + (int)(((hash >> 2) & 255) % 2); // 2-3 separated rooted tufts per cell
        for (int tIdx = 0; tIdx < tuftCount; tIdx++)
        {
            uint h = ReliefHash(unchecked((int)hash) + tIdx * 47, tIdx * 31 + 9);
            double a0 = (h & 1023) / 1023.0 * Math.PI * 2;
            double offR = ((h >> 8) & 255) / 255.0 * 0.006;
            var basePos = foot + (right * Math.Cos(a0) + fwd * Math.Sin(a0)) * offR;
            double height = (0.008 + ((h >> 10) & 255) / 255.0 * 0.004) * v; // 8-12 mm tufts; litter remains visible

            Primitives.Tube(mesh, new[] { basePos, basePos + normal * (height * 0.4) },
                new[] { 0.00065, 0.00048 }, 4, (i, vv) => (deepCol, 1, 0, 0, 0, 0));

            int leafCount = 6 + (int)(((h >> 18) & 255) % 3); // 6..8 pointed leaves
            double phase = a0 + ((h >> 6) & 15) * 0.1;
            for (int l = 0; l < leafCount; l++)
            {
                uint hb = ReliefHash(unchecked((int)hash) + tIdx * 89 + l * 41, l * 13 + 17);
                double a = phase + l * Math.PI * 2.0 / leafCount + ((hb & 255) / 255.0 - 0.5) * 0.22;
                var leanDir = (right * Math.Cos(a) + fwd * Math.Sin(a)).Normalized();
                if (leanDir.LengthSq < 1e-8) leanDir = right;
                var sideVec = (right * (-Math.Sin(a)) + fwd * Math.Cos(a)) * 0.0010;
                double leanAmt = 0.70 + ((hb >> 8) & 15) / 15.0 * 0.73; // 35-55 deg splay: star-tuft cushion
                var leafTip = basePos + normal * height + leanDir * (height * leanAmt);
                int v0 = mesh.AddVertex(basePos - sideVec, normal, deepCol, 1);
                int v1 = mesh.AddVertex(basePos + sideVec, normal, deepCol, 1);
                int vTip = mesh.AddVertex(leafTip, normal, tipCol, 1);
                Primitives.TriangleFacing(mesh, v0, v1, vTip, normal);
            }
        }
    }

    private static void AppendThreadWisps(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Near-black-green base with a pale dew-sheen tip (wave-7): dark
        // strands contrast against turf, but the light tip catches light
        // instead of vanishing into the black ground. Base stays at
        // 0.12-0.16; tip is mixed ~0.85 toward pale dew (0.70,0.78,0.62)
        // so each ~4 cm filament reads individually.
        // Strands ~1 mm diameter (radius 0.50 mm) for silhouette thickness.
        var threadBase = new[] { color.R * 0.12, color.G * 0.16, color.B * 0.14 };
        var dewPale = new[] { 0.70, 0.78, 0.62 };
        var threadTip = new[] { threadBase[0] + (dewPale[0] - threadBase[0]) * 0.85, threadBase[1] + (dewPale[1] - threadBase[1]) * 0.85, threadBase[2] + (dewPale[2] - threadBase[2]) * 0.85 };

        int wispCount = 2 + (int)(((hash >> 3) & 255) % 2); // 2-3 fine anchored filaments per cell
        for (int w = 0; w < wispCount; w++)
        {
            uint h = ReliefHash(unchecked((int)hash) + w * 59, w * 27 + 3);
            double ang = (h & 1023) / 1023.0 * Math.PI * 2 + w * 1.7;
            var radial = (right * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            if (radial.LengthSq < 1e-8) radial = right;
            var lateral = (right * (-Math.Sin(ang)) + fwd * Math.Cos(ang)).Normalized();
            if (lateral.LengthSq < 1e-8) lateral = fwd;

            // Wave-6: 3.6-4.4 cm strands arched 30 mm so they clear the turf.
            double length = (0.024 + ((h >> 8) & 255) / 255.0 * 0.012) * v; // short draping strands
            double bendAmp = 0.0040 + ((h >> 12) & 15) * 0.00045;
            double sAmp = 0.0016 + ((h >> 16) & 7) * 0.00030;
            double phase = ((h >> 20) & 15) * 0.3;

            var path = new Vec3[6];
            var radii = new double[6];
            for (int k = 0; k < path.Length; k++)
            {
                double t = (double)k / (path.Length - 1);
                double latOff = bendAmp * Math.Sin(Math.PI * t) + sAmp * Math.Sin(2 * Math.PI * t + phase);
                double height = 0.0010 + (0.004 + ((h >> 12) & 15) * 0.00030) * Math.Sin(Math.PI * t); // low drape, remains anchored
                path[k] = foot + radial * (length * t) + lateral * latOff + normal * height;
                radii[k] = 0.00062 * (1.0 - t) + 0.00016 * t; // fine root tapers to a hairline tip
            }
            Primitives.Tube(mesh, path, radii, 4, (i, vv) => {
                double tt = i / 5.0;
                var cc = new[] { threadBase[0] + (threadTip[0] - threadBase[0]) * tt, threadBase[1] + (threadTip[1] - threadBase[1]) * tt, threadBase[2] + (threadTip[2] - threadBase[2]) * tt };
                return (cc, 1, 0, 0, 0, 0);
            });
        }
    }

    private static void AppendThallusLobes(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Wave-8: flat glossy olive tongue-lobes hugging the stone, bright
        // pale midrib stripe (0.75,0.85,0.55). Stalk lowered to 2-4 mm so
        // the tongue lies on the wet stone (harness no longer buries it).
        double centreR = Math.Min(1.0, (color.R + (color2.R - color.R) * 0.70) * 1.80);
        double centreG = Math.Min(1.0, (color.G + (color2.G - color.G) * 0.70) * 1.82);
        double centreB = Math.Min(1.0, (color.B + (color2.B - color.B) * 0.70) * 1.60);
        var edgeCol = new[] { color.R * 0.38, color.G * 0.42, color.B * 0.34 };
        var midribCol = new[] { 0.75, 0.85, 0.55 };
        var underCol = new[] { 0.25, 0.22, 0.18 };

        int lobeCount = 3 + (int)(((hash >> 4) & 255) % 2); // 3..4 lobes
        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;
        for (int l = 0; l < lobeCount; l++)
        {
            uint h = ReliefHash(unchecked((int)hash) + l * 53, l * 29 + 11);
            double a = baseAng + l * Math.PI * 2.0 / lobeCount + ((h & 255) / 255.0 - 0.5) * 0.3;
            var radial = (right * Math.Cos(a) + fwd * Math.Sin(a)).Normalized();
            if (radial.LengthSq < 1e-8) radial = right;
            var lateral = (right * (-Math.Sin(a)) + fwd * Math.Cos(a)).Normalized();
            if (lateral.LengthSq < 1e-8) lateral = fwd;

            // Wave-8: 2.5-3.5 cm tongues, 8-10 mm wide, forked/overlapping,
            // stalk only 2-4 mm so the flat lobes hug the stone.
            double len = (0.025 + ((h >> 8) & 255) / 255.0 * 0.010) * v; // 2.5-3.5 cm tongues
            double halfW = 0.0040 + ((h >> 12) & 7) * 0.00018; // 8-10 mm wide -> halfW 4-5 mm
            var stalkTop = foot + radial * 0.0015 + normal * (0.002 + ((h >> 6) & 7) * 0.00028); // 2-4 mm stalk
            Primitives.Tube(mesh, new[] { foot + radial * 0.0005, stalkTop },
                new[] { 0.0009, 0.0006 }, 4, (i, vv) => (edgeCol, 1, 0, 0, 0, 0));

            int segs = 5;
            int across = 4;
            int[,] U = new int[segs + 1, across + 1];
            // Wave-9 black-triangle fix: upper surface only (see AppendRuffleLobes).
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                for (int j = 0; j <= across; j++)
                {
                    double x = (double)j / across * 2.0 - 1.0;
                    double env = Math.Sqrt(Math.Max(0, Math.Sin(Math.PI * Math.Min(1, t * 1.05))));
                    double lateralOff = x * halfW * env;
                    double height = 0.0008 + 0.0020 * t + 0.0012 * x * x * Math.Min(1, env * 2);
                    var pos = stalkTop + radial * (len * t) + lateral * lateralOff + normal * height;
                    var nrm = (radial * 0.2 + normal * (1.0 - t * 0.2)).Normalized();
                    double ef = Math.Abs(x);
                    double cr = edgeCol[0] + (centreR - edgeCol[0]) * (1.0 - ef * ef);
                    double cg = edgeCol[1] + (centreG - edgeCol[1]) * (1.0 - ef * ef);
                    double cb = edgeCol[2] + (centreB - edgeCol[2]) * (1.0 - ef * ef);
                    // Bright pale midrib stripe (0.75,0.85,0.55).
                    double stripe = Math.Max(0, 1.0 - Math.Abs(x) / 0.45);
                    if (stripe > 0)
                    {
                        double sc = stripe * stripe;
                        cr += (0.75 - cr) * sc * 0.95;
                        cg += (0.85 - cg) * sc * 0.95;
                        cb += (0.55 - cb) * sc * 0.95;
                    }
                    var col = new[] { cr, cg, cb };
                    U[i, j] = mesh.AddVertex(pos, nrm, col, 1, t, (x + 1) * 0.5);
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < across; j++)
                {
                    Primitives.TriangleFacing(mesh, U[i, j], U[i + 1, j], U[i, j + 1], normal);
                    Primitives.TriangleFacing(mesh, U[i, j + 1], U[i + 1, j], U[i + 1, j + 1], normal);
                }
        }
    }

    private static void AppendBeardCurls(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Wave-8: pale silver-sage (0.76,0.82,0.76) arched/draping tuft.
        var paleCol = new[] { 0.76, 0.82, 0.76 };
        var baseCol = new[] { color.R * 0.55, color.G * 0.55, color.B * 0.58 };

        // Wave-8: 4-5 curled branching strands, 3-4.5 cm long, arching up
        // 18-28 mm and drooping back down (fruticose tuft, not a tall hang).
        int strandCount = 3 + (int)((hash >> 5) & 1); // 3-4 rooted curls per cell
        for (int s = 0; s < strandCount; s++)
        {
            uint h = ReliefHash(unchecked((int)hash) + s * 43, s * 37 + 13);
            double ang = (h & 1023) / 1023.0 * Math.PI * 2 + s * 1.5;
            var radial = (right * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            if (radial.LengthSq < 1e-8) radial = right;
            var lateral = (right * (-Math.Sin(ang)) + fwd * Math.Cos(ang)).Normalized();
            if (lateral.LengthSq < 1e-8) lateral = fwd;

            double length = (0.030 + ((h >> 8) & 255) / 255.0 * 0.015) * v; // 3-4.5 cm
            double peakH = (0.008 + ((h >> 12) & 15) / 15.0 * 0.006) * v; // compact curls rooted at the mat
            double curlAmp = 0.0025 + ((h >> 6) & 15) * 0.00030;
            double phase = ((h >> 20) & 15) * 0.3;

            var path = new Vec3[6];
            var radii = new double[6];
            for (int k = 0; k < path.Length; k++)
            {
                double t = (double)k / (path.Length - 1);
                // Arch up then droop back down: height peaks mid-span.
                double radialOff = length * t;
                double lateralOff = curlAmp * Math.Sin(Math.PI * t) + 0.0010 * Math.Sin(2 * Math.PI * t + phase);
                // Smooth arch: rise to 18-28 mm peak, droop to ~3 mm at tip.
                double height = peakH * Math.Sin(Math.PI * Math.Min(1.0, t * 1.08)) * (1.0 - t * 0.72) + 0.0015;
                path[k] = foot + radial * radialOff + lateral * lateralOff + normal * height;
                radii[k] = 0.00100 * (1.0 - t) + 0.00035 * t;
            }
            Primitives.Tube(mesh, path, radii, 5, (i, vv) => {
                double tt = i / 5.0;
                var cc = new[] { baseCol[0] + (paleCol[0] - baseCol[0]) * tt, baseCol[1] + (paleCol[1] - baseCol[1]) * tt, baseCol[2] + (paleCol[2] - baseCol[2]) * tt };
                return (cc, 1, 0, 0, 0, 0);
            });
            // Small side branchlet per strand for the branching read.
            var branchBase = path[2] + lateral * 0.0006;
            var branchTip = path[4] + radial * 0.003 + lateral * 0.002 + normal * 0.002;
            Primitives.Tube(mesh, new[] { branchBase, branchTip }, new[] { 0.00045, 0.00025 }, 4,
                (i, vv) => (paleCol, 1, 0, 0, 0, 0));
        }
    }

    private static void AppendHagshairLogCurls(MeshData mesh, Vec3 anchor, Vec3 logAxis, Vec3 barkNormal,
        Color color, uint hash, float vigour, double logRadius)
    {
        int strands = 2 + (int)((hash >> 6) & 1);
        var pale = new[] { 0.76, 0.82, 0.76 };
        var baseCol = new[] { color.R * 0.55, color.G * 0.55, color.B * 0.58 };
        var sideDir = new Vec2(barkNormal.X, barkNormal.Z).Normalized();
        if (sideDir.LengthSq < 1e-8) sideDir = new Vec2(1, 0);
        var axis2 = new Vec2(logAxis.X, logAxis.Z).Normalized();
        // Recover the cylinder centreline from the shoulder anchor. As each strand
        // drops, follow the widening upper-half cross section so it never enters bark.
        double surfaceSide = logRadius * Math.Sqrt(Math.Max(0.0, 1.0 - barkNormal.Y * barkNormal.Y));
        var centreLine = anchor.XZ - sideDir * (surfaceSide + 0.001 * Math.Sqrt(Math.Max(0.0, 1.0 - barkNormal.Y * barkNormal.Y)));
        double centreY = anchor.Y - logRadius * barkNormal.Y - 0.001 * barkNormal.Y;
        double maxDrop = Math.Min(logRadius * 1.15, 0.085) * Math.Clamp(vigour, 1.0f, 1.25f);
        for (int s = 0; s < strands; s++)
        {
            uint h = ReliefHash(unchecked((int)hash) + s * 53, s * 29 + 17);
            double phase = ((h >> 8) & 255) / 255.0 * Math.PI * 2.0;
            double drop = maxDrop * (0.58 + ((h >> 16) & 255) / 255.0 * 0.42);
            // Keep the first vertex on the sampled bark contact; along-axis variation
            // grows only after the strand leaves the root.
            var root = anchor;
            var path = new Vec3[5];
            var radii = new double[5];
            for (int i = 0; i < path.Length; i++)
            {
                double t = (double)i / (path.Length - 1);
                // Keep the bark-clear radial profile, but let the strand wander
                // gently along the log axis instead of reading as a ruler-straight drop.
                double curl = Math.Sin(Math.PI * t + phase) * 0.0040 * t;
                double y = root.Y - drop * t;
                double radial;
                if (y > centreY)
                {
                    double dy = Math.Min(logRadius, y - centreY);
                    radial = Math.Sqrt(Math.Max(0.0, logRadius * logRadius - dy * dy)) + 0.0012;
                }
                else
                {
                    // Below the log axis the strand leaves the cylinder and hangs free,
                    // keeping the last bark-clear radius instead of wrapping underground.
                    radial = logRadius + 0.0012;
                }
                double along = (((h >> 23) & 255) / 255.0 - 0.5) * 0.012 * t + curl;
                var xz = centreLine + sideDir * radial + axis2 * along;
                path[i] = i == 0 ? anchor : new Vec3(xz.X, y, xz.Z);
                radii[i] = 0.00062 * (1.0 - t) + 0.00012 * t;
            }
            Primitives.Tube(mesh, path, radii, 4, (i, vv) =>
            {
                double t = i / 4.0;
                var c = new[] { baseCol[0] + (pale[0] - baseCol[0]) * t,
                    baseCol[1] + (pale[1] - baseCol[1]) * t,
                    baseCol[2] + (pale[2] - baseCol[2]) * t };
                return (c, 1, 0, 0, 0, 0);
            });

            // A few hanging branches fork once, matching the sparse dichotomous
            // branching of beard lichens without adding more main strands.
            if ((h & 3) == 0)
            {
                double branchSign = ((h >> 4) & 1) == 0 ? -1.0 : 1.0;
                var branchBase = path[2];
                var branchOffset = new Vec3(axis2.X, 0, axis2.Z) * (branchSign * 0.0050);
                var branchMid = Vec3.Lerp(path[2], path[3], 0.55) + branchOffset * 0.5;
                var branchTip = path[3] + branchOffset;
                Primitives.Tube(mesh, new[] { branchBase, branchMid, branchTip },
                    new[] { 0.00038, 0.00025, 0.00012 }, 4,
                    (i, vv) => (pale, 1, 0, 0, 0, 0));
            }
        }
    }

    private static void AppendSunburstLobes(MeshData mesh, Vec3 foot, Vec3 normal, Color color, Color color2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Wave-8: deep orange-amber centre (0.82,0.42,0.08) to bright
        // mustard-gold tips (0.98,0.76,0.14), flat radiating rosette.
        double centreR = 0.82, centreG = 0.42, centreB = 0.08;
        double tipR = 0.98, tipG = 0.76, tipB = 0.14;
        var underCol = new[] { 0.25, 0.22, 0.18 };

        int lobeCount = 7 + (int)(((hash >> 4) & 255) % 4); // 7-10 bonded wedges per crust cell
        double radiusLen = (0.020 + ((hash >> 8) & 255) / 255.0 * 0.008) * v; // 2-2.8 cm radius
        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;
        for (int l = 0; l < lobeCount; l++)
        {
            uint h = ReliefHash(unchecked((int)hash) + l * 53, l * 29 + 11);
            double a = baseAng + l * Math.PI * 2.0 / lobeCount + ((h & 255) / 255.0 - 0.5) * 0.38;
            var radial = (right * Math.Cos(a) + fwd * Math.Sin(a)).Normalized();
            if (radial.LengthSq < 1e-8) radial = right;
            var lateral = (right * (-Math.Sin(a)) + fwd * Math.Cos(a)).Normalized();
            if (lateral.LengthSq < 1e-8) lateral = fwd;

            double len = radiusLen * (0.82 + ((h & 15) / 15.0) * 0.30);
            double halfW = len * (0.22 + ((h >> 6) & 7) * 0.012);

            int segs = 4;
            int across = 3;
            int[,] U = new int[segs + 1, across + 1];
            // Wave-9 black-triangle fix: upper surface only (see AppendRuffleLobes).
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                for (int j = 0; j <= across; j++)
                {
                    double x = (double)j / across * 2.0 - 1.0;
                    double widthScale = 1.0 - t * 0.65;
                    double lateralOff = x * halfW * widthScale;
                    double height = 0.001 + 0.002 * (1.0 - t) + 0.0005 * t * t; // flat crustose appression
                    var pos = foot + radial * (len * t) + lateral * lateralOff + normal * height;
                    var nrm = (radial * 0.25 + normal * (1.0 - t * 0.25)).Normalized();
                    double cr = centreR + (tipR - centreR) * t;
                    double cg = centreG + (tipG - centreG) * t;
                    double cb = centreB + (tipB - centreB) * t;
                    var col = new[] { cr, cg, cb };
                    U[i, j] = mesh.AddVertex(pos, nrm, col, 1, t, (x + 1) * 0.5);
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < across; j++)
                {
                    Primitives.TriangleFacing(mesh, U[i, j], U[i + 1, j], U[i, j + 1], normal);
                    Primitives.TriangleFacing(mesh, U[i, j + 1], U[i + 1, j], U[i + 1, j + 1], normal);
                }
        }

        // Flat amber centre disc: the hub the lobes radiate from.
        var centreCol = new[] { centreR, centreG, centreB };
        Primitives.Ellipsoid(mesh, foot + normal * 0.0012, new Vec3(0.007, 0.0012, 0.007), 4, 8,
            (u, vv) => (centreCol, 1, u, vv, 0, 0));
    }

    // Rustbellow (rustbellow_moss): rust-brown hummock dome with green base.
    // The shared Sphagnum builder only reads Color1 and has no dome, so the
    // rust-vs-green split needs its own builder. Color1 = rust, Color2 = green.
    // Wave-7: rust toned from pure magenta-red toward rust-brown
    // (0.55,0.22,0.10) by mixing 0.55 toward brown, reducing saturation
    // while keeping the rust read against green turf.
    private static void AppendRustbellowDome(MeshData mesh, Vec3 foot, Vec3 normal, Color rust, Color green, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        uint shape = ReliefHash(unchecked((int)hash), 0x52B311);
        var baseGreen = new[] { green.R * 0.62, green.G * 0.72, green.B * 0.58 };
        var domeRust = new[] { 0.48, 0.20, 0.09 };
        var crownRust = new[] { 0.69, 0.31, 0.13 };
        double rx = (0.021 + ((shape >> 4) & 255) / 255.0 * 0.004) * v;
        // Low overlapping wet hummocks: the broad footprint closes the pinched gaps
        // between occupied cells; the flattened profile avoids repeated hard domes.
        Primitives.Ellipsoid(mesh, foot + normal * 0.001, new Vec3(rx * 1.34, 0.0020, rx * 1.34), 4, 9,
            (u, vv) => (baseGreen, 1, u, vv, 0, 0));
        Primitives.Ellipsoid(mesh, foot + normal * 0.004, new Vec3(rx * 0.86, 0.0068 * v, rx * (0.78 + ((shape >> 12) & 15) * 0.012)), 5, 10,
            (u, vv) => {
                double top = Math.Clamp((Math.Sin(Math.PI * u) * Math.Cos(2 * Math.PI * vv) * 0.5 + 0.5), 0.0, 1.0);
                double k = Math.Pow(top, 1.35);
                return (new[] { domeRust[0] + (crownRust[0] - domeRust[0]) * k,
                                domeRust[1] + (crownRust[1] - domeRust[1]) * k,
                                domeRust[2] + (crownRust[2] - domeRust[2]) * k }, 1, u, vv, 0, 0);
            });
        // A few short living crests emerge from the hummock instead of a crown of beads.
        int crestCount = 2 + (int)((shape >> 20) & 1);
        for (int k = 0; k < crestCount; k++)
        {
            uint hb = ReliefHash(unchecked((int)hash) + k * 31, k * 17 + 5);
            double a = (hb & 1023) / 1023.0 * Math.PI * 2;
            double r = rx * (0.18 + ((hb >> 8) & 255) / 255.0 * 0.42);
            var delta = new Vec3(Math.Cos(a) * r, 0, Math.Sin(a) * r);
            var root = foot + delta + normal * 0.006;
            var tip = root + normal * ((0.0025 + ((hb >> 12) & 7) / 7.0 * 0.0025) * v);
            Primitives.Tube(mesh, new[] { root, tip }, new[] { 0.00045, 0.00028 }, 4, (i, vv) => (domeRust, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, tip, new Vec3(0.0014, 0.0007, 0.0012), 3, 5,
                (u, vv) => (crownRust, 1, u, vv, 0, 0));
        }
    }

    // Stoneknot (stoneknot_moss): charcoal-green cushion dome (12 mm) with beaded
    // surface. The shared PearlCushion builder draws a seta + capsule with no
    // cushion, so the dome needs its own builder. Wave-7: lightened to mid
    // charcoal with a pale top so the dome reads in relief (was crushing
    // near-black); sheet is 12 mm (see InitSpecies).
    private static void AppendStoneknotCushion(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        // Wave-9: richer mossy olive-charcoal (0.24,0.38,0.22) base to
        // (0.48,0.64,0.38) crown. Fixed palette so the cushion reads as living
        // moss, not grey gravel. Silvery-white hyaline hair-points on top.
        var baseCol = new[] { 0.24, 0.38, 0.22 };
        var topCol = new[] { 0.48, 0.64, 0.38 };
        var hairCol = new[] { 0.85, 0.90, 0.85 };

        // Keep each crown as a distinct low cushion. Larger crowns stacked into
        // shingle bands on slopes, so use a tighter footprint and align each dome
        // to the local terrain tangent before applying its soft irregular outline.
        double rx = 0.0155 * v, ry = 0.0055 * v;
        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();
        int moundFirst = mesh.VertexCount;
        var moundCentre = foot + normal * (ry * 0.92);
        Primitives.Ellipsoid(mesh, moundCentre, new Vec3(rx, ry, rx * 0.94), 6, 10,
            (u, vv) => {
                double top = 1.0 - Math.Abs(u - 0.5) * 2.0;
                var c = new[] { baseCol[0] + (topCol[0] - baseCol[0]) * top,
                                baseCol[1] + (topCol[1] - baseCol[1]) * top,
                                baseCol[2] + (topCol[2] - baseCol[2]) * top };
                return (c, 1, u, vv, 0, 0);
            });
        // Uneven three-lobed footprint breaks the isolated coin silhouette without
        // adding vertices; the high center stays a soft cushion, with low connected shoulders.
        for (int vi = moundFirst; vi < mesh.VertexCount; vi++)
        {
            var p = mesh.Position(vi);
            var rel = p - moundCentre;
            // Ellipsoid coordinates are mapped onto the ground tangent so a
            // sloping substrate cannot turn neighboring crowns into layered plates.
            double x = rel.X;
            double z = rel.Z;
            double az = Math.Atan2(z, x);
            double radialScale = 1.0 + 0.075 * Math.Cos(3.0 * az + (hash & 255) / 255.0 * 0.7)
                + 0.025 * Math.Sin(5.0 * az + ((hash >> 8) & 255) / 255.0 * 2.0);
            var tangent = right * rel.X + fwd * rel.Z;
            var warped = moundCentre + tangent * radialScale + normal * (rel.Y * (0.91 + 0.05 * Math.Sin(3.0 * az + 0.4)));
            mesh.Positions[vi * 3] = (float)warped.X;
            mesh.Positions[vi * 3 + 1] = (float)warped.Y;
            mesh.Positions[vi * 3 + 2] = (float)warped.Z;
        }

        // Sparse small beads remain subordinate and sit on the crown shoulders.
        var beadCol = new[] { Math.Min(1.0, topCol[0] + 0.05), Math.Min(1.0, topCol[1] + 0.05), Math.Min(1.0, topCol[2] + 0.04) };
        for (int k = 0; k < 3; k++)
        {
            uint hb = ReliefHash(unchecked((int)hash) + k * 41, k * 19 + 7);
            double a = (hb & 1023) / 1023.0 * Math.PI * 2;
            double r = 0.0015 + ((hb >> 8) & 255) / 255.0 * 0.0045;
            double q = Math.Min(1.0, r / rx);
            double h = ry * 0.92 + ry * Math.Sqrt(Math.Max(0.0, 1.0 - q * q));
            var p = foot + (right * Math.Cos(a) + fwd * Math.Sin(a)) * r + normal * h;
            var s = 0.0006 + ((hb >> 12) & 3) * 0.00012;
            Primitives.Ellipsoid(mesh, p, new Vec3(s, s * 0.8, s), 3, 5,
                (u, vv) => (beadCol, 1, u, vv, 0, 0));
        }

        // Wave-9: 6-8 short silvery-white hyaline hair-point tips (3-5 mm)
        // rooted on top of the cushion dome.
        var pearlCol = hairCol;
        int hairCount = 3 + (int)((hash >> 7) & 1); // sparse hyaline tips among the beads
        for (int p = 0; p < hairCount; p++)
        {
            uint hh = ReliefHash(unchecked((int)hash) + p * 37, p * 17 + 5);
            double ha = (hh & 1023) / 1023.0 * Math.PI * 2 + p * Math.PI * 2 / hairCount;
            double hr = ((hh >> 8) & 255) / 255.0 * rx * 0.7;
            double hq = Math.Min(1.0, hr / rx);
            double hDome = ry * 0.92 + ry * Math.Sqrt(Math.Max(0.0, 1.0 - hq * hq));
            var hairBase = foot + (right * Math.Cos(ha) + fwd * Math.Sin(ha)) * hr + normal * hDome;
            double hairLen = (0.003 + ((hh >> 12) & 7) / 7.0 * 0.002) * v; // 3-5 mm
            var hairTip = hairBase + normal * hairLen
                + (right * Math.Cos(ha) + fwd * Math.Sin(ha)) * (hairLen * 0.25);
            Primitives.Tube(mesh, new[] { hairBase, hairTip }, new[] { 0.00025, 0.00012 }, 4,
                (i, vv) => (pearlCol, 1, 0, 0, 0, 0));
        }
    }

    // Silkgleam (silkgleam_moss): golden combed turf with pale tip sheen.
    // The shared Velvetweave builder reads only Color1 (olive) for its turf,
    // so the golden Color2 never rendered; this builder carries the gold.
    private static void AppendSilkgleamTurf(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        // Wave-9: warm burnished gold-green (0.78,0.72,0.22) base to pale
        // silk-gold (0.95,0.90,0.48) tips. Combed in one consistent direction:
        // every shoot fans within +/-0.1 rad of a shared heading, arching low.
        var goldBase = new[] { 0.78, 0.72, 0.22 };
        var goldTip = new[] { 0.95, 0.90, 0.48 };
        var setaCol = new[] { 0.50, 0.18, 0.12 };

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(0, 0, 1);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        // Shared comb direction from the cell hash.
        double combAng = (hash & 1023) / 1023.0 * Math.PI * 2;
        var combDir = (right * Math.Cos(combAng) + fwd * Math.Sin(combAng)).Normalized();
        if (combDir.LengthSq < 1e-8) combDir = right;
        var combLat = (right * (-Math.Sin(combAng)) + fwd * Math.Cos(combAng)).Normalized();
        if (combLat.LengthSq < 1e-8) combLat = fwd;

        int shootCount = 3 + (int)((hash >> 5) & 1); // 3-4 shoots; keep open gaps between directional fans
        for (int j = 0; j < shootCount; j++)
        {
            uint hb = ReliefHash(unchecked((int)hash) + j * 37, j * 23 + 3);
            double jitter = ((hb & 255) / 255.0 - 0.5) * 0.76; // mixed-angle comb, not a row of parallel sticks
            var dir = (combDir * Math.Cos(jitter) + combLat * Math.Sin(jitter) + normal * (0.18 + ((hb >> 20) & 15) / 15.0 * 0.22)).Normalized();
            var basePos = foot + combLat * (((hb >> 8) & 255) / 255.0 - 0.5) * 0.006;
            double len = (0.018 + ((hb >> 10) & 255) / 255.0 * 0.018) * v; // variable 1.8-3.6 cm shoots
            double archH = (0.010 + ((hb >> 12) & 7) / 7.0 * 0.007) * v; // taller swept arches, still low turf
            var mid = basePos + dir * (len * 0.5) + normal * archH + combLat * ((((hb >> 18) & 15) / 15.0 - 0.5) * 0.008);
            var tip = basePos + dir * len + normal * (0.006 + ((hb >> 15) & 7) * 0.00055) + combLat * ((((hb >> 23) & 7) / 7.0 - 0.5) * 0.010);
            // 1.2 mm wide ribbon/tube (radius 0.6 mm), tapering to the tip.
            Primitives.Tube(mesh, new[] { basePos, mid, tip }, new[] { 0.00060, 0.00045, 0.00028 }, 5, (i, vv) => {
                double tt = i / 2.0;
                var cc = new[] { goldBase[0] + (goldTip[0] - goldBase[0]) * tt, goldBase[1] + (goldTip[1] - goldBase[1]) * tt, goldBase[2] + (goldTip[2] - goldBase[2]) * tt };
                return (cc, 1, 0, 0, 0, 0);
            });
        }

        // Wave-9: only 1-2 short seta wires, 10-15 mm (was 3.5-4.7 cm vertical sticks).
        var urnCol = new[] { 0.70, 0.52, 0.20 };
        int setaCount = 1; // one or occasional capsule per nucleus
        for (int s = 0; s < setaCount; s++)
        {
            uint hs = ReliefHash(unchecked((int)hash) + 500 + s * 29, s * 11 + 7);
            var setaBase = foot + combLat * (((hs & 255) / 255.0 - 0.5) * 0.004);
            double height = (0.010 + ((hs >> 8) & 7) / 7.0 * 0.005) * v; // 10-15 mm
            var wireTip = setaBase + normal * height;
            Primitives.Tube(mesh, new[] { setaBase, wireTip }, new[] { 0.00045, 0.00030 }, 4,
                (i, vv) => (setaCol, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, wireTip + normal * 0.0012, new Vec3(0.0010, 0.0016, 0.0010), 3, 5,
                (u, vv) => (urnCol, 1, u, vv, 0, 0));
        }
    }

    // Moonshell (moonshell_lichen): white-blue ruffle lobes, larger than the
    // shared RuffleLichen builder draws, mixed 0.5 toward near-white.
    private static void AppendMoonshellLobes(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        // Wave-8: pearly white-blue (0.82,0.90,0.96) broad shell lobes,
        // darkslate undersides, amber-brown apothecia.
        var upperCol = new[] { 0.58, 0.68, 0.74 };
        var underCol = new[] { 0.24, 0.28, 0.33 }; // darkslate
        var pruinoseCol = new[] { 0.70, 0.79, 0.83 };
        var discCol = new[] { 0.55, 0.33, 0.15 }; // amber-brown
        var rimCol = new[] { 0.92, 0.85, 0.60 };

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        int lobeCount = 3 + (int)((hash >> 4) & 1); // a few broad attached cups, with room for substrate to show
        double baseAng = (hash & 1023) / 1023.0 * Math.PI * 2;

        for (int l = 0; l < lobeCount; l++)
        {
            uint h = ReliefHash(unchecked((int)hash) + l * 53, l * 29 + 11);
            double a = baseAng + l * Math.PI * 2.0 / lobeCount + ((h & 255) / 255.0 - 0.5) * 0.25;
            var radial = (right * Math.Cos(a) + fwd * Math.Sin(a)).Normalized();
            var lateral = (right * (-Math.Sin(a)) + fwd * Math.Cos(a)).Normalized();

            double len = (0.022 + ((h >> 8) & 255) / 255.0 * 0.009) * v; // broad attached cups
            double lift = (0.006 + ((h >> 16) & 255) / 255.0 * 0.005) * v;
            double halfW = 0.0100 + ((h >> 4) & 15) * 0.00045;

            int segs = 4;
            int across = 3;
            int[,] U = new int[segs + 1, across + 1];
            // Wave-9 black-triangle fix: upper surface only (see AppendRuffleLobes).
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                for (int j = 0; j <= across; j++)
                {
                    double x = (double)j / across * 2.0 - 1.0;
                    // Broad, rounded distal lobe instead of a pointed shard tip.
                    double widthScale = t < 0.72 ? 0.76 + 0.24 * (t / 0.72) : 0.78 + 0.12 * Math.Cos((t - 0.72) / 0.28 * Math.PI * 0.5);
                    double lateralOff = x * halfW * widthScale;
                    double ruffle = 0.0012 * Math.Sin(3.0 * x + t * 4.0 + a * 2.0); // scalloped/ruffled margin
                    double height = lift * t * t + ruffle * (0.4 + 0.6 * t);
                    if (t > 0.7) height += (t - 0.7) * 0.003;
                    var pos = foot + radial * (len * t) + lateral * lateralOff + normal * height;
                    var nrm = (radial * 0.3 + normal * (1.0 - t * 0.3)).Normalized();
                    double[] col = t > 0.8 ? pruinoseCol : upperCol;
                    U[i, j] = mesh.AddVertex(pos, nrm, col, 1, t, (x + 1) * 0.5);
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < across; j++)
                {
                    Primitives.TriangleFacing(mesh, U[i, j], U[i + 1, j], U[i, j + 1], normal);
                    Primitives.TriangleFacing(mesh, U[i, j + 1], U[i + 1, j], U[i + 1, j + 1], normal);
                }
        }

        int cupCount = 1 + (int)((hash >> 20) & 1);
        for (int c = 0; c < cupCount; c++)
        {
            uint h = ReliefHash(unchecked((int)hash) + c * 71, c * 19 + 3);
            double ca = (h & 1023) / 1023.0 * Math.PI * 2;
            double cr = 0.002 + ((h >> 10) & 255) / 255.0 * 0.004;
            var cupCenter = foot + (right * Math.Cos(ca) + fwd * Math.Sin(ca)) * cr + normal * 0.0015;
            double cupD = 0.0016 + ((h >> 18) & 255) / 255.0 * 0.0016;
            double cupR = cupD * 0.5;
            int rimSegs = 8;
            var rimPath = new List<Vec3>();
            for (int r = 0; r < rimSegs; r++)
            {
                double th = 2 * Math.PI * r / rimSegs;
                var radial = right * Math.Cos(th) + fwd * Math.Sin(th);
                rimPath.Add(cupCenter + radial * cupR + normal * 0.0006);
            }
            rimPath.Add(rimPath[0]);
            var rimRadii = new double[rimPath.Count];
            for (int r = 0; r < rimRadii.Length; r++) rimRadii[r] = cupR * 0.18;
            Primitives.Tube(mesh, rimPath, rimRadii, rimSegs,
                (i, vv) => (rimCol, 1, 0, 0, 0, 0));
            Primitives.Ellipsoid(mesh, cupCenter + normal * 0.0002, new Vec3(cupR * 0.85, 0.00035, cupR * 0.85), 3, 6,
                (u, vv) => (discCol, 1, u, vv, 0, 0));
        }
    }

    // Ashfingers (ashfingers_lichen): pale bone-white upright fingers, 4 cm,
    // more deeply branched than the shared Antlerlace builder draws.
    private static void AppendAshfingersPodetia(MeshData mesh, Vec3 foot, Vec3 normal, Color col1, Color col2, uint hash, float vigour)
    {
        float v = Math.Clamp(vigour, 1.0f, 1.25f);
        // Wave-9: chalky bone-white (0.92,0.94,0.88) over 90% of height;
        // only the very tips (last 10%) warm brown (0.48,0.42,0.35), so the
        // colony reads as pale bone-white branching reindeer-lichen fingers.
        var bone = new[] { 0.92, 0.94, 0.88 };
        var podetiaCol = new[] { bone[0], bone[1], bone[2] };
        var tipCol = new[] { 0.48, 0.42, 0.35 }; // warm brown tips

        Vec3 right = Vec3.Up.Cross(normal);
        if (right.LengthSq < 1e-8) right = new Vec3(1, 0, 0).Cross(normal);
        right = right.Normalized();
        Vec3 fwd = normal.Cross(right).Normalized();

        int stemCount = 4 + (int)((hash >> 2) & 2); // 4-6 brittle fingers, with exposed crust between
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;

        for (int s = 0; s < stemCount; s++)
        {
            uint h = ReliefHash(unchecked((int)hash) + s * 97, s * 13 + 7);
            double ang = phase + s * Math.PI * 2.0 / stemCount + ((h & 255) / 255.0 - 0.5) * 0.35;
            var radial = (right * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            double clusterR = 0.0025 + ((h >> 8) & 255) / 255.0 * 0.0045;
            var basePos = foot + radial * clusterR;
            double height = (0.018 + ((h >> 16) & 255) / 255.0 * 0.010) * v; // short irregular chalky podetia

            var trunkTop = basePos + normal * (height * 0.38);
            AddStraightTube(mesh, basePos, trunkTop, 0.0020, 0.0018, 5, podetiaCol);

            var work = new List<(Vec3 start, Vec3 dir, double len, double rad, int depth)>();
            var firstDir = (normal * 0.78 + radial * 0.28).Normalized();
            work.Add((trunkTop, firstDir, height * 0.30, 0.0016, 0));
            while (work.Count > 0)
            {
                var (start, dir, len, rad, depth) = work[work.Count - 1];
                work.RemoveAt(work.Count - 1);
                var end = start + dir * len;
                double r1 = depth >= 3 ? rad * 0.55 : rad * 0.80;
                AddStraightTube(mesh, start, end, rad, r1, 5, podetiaCol);

                if (depth < 4) // deeper forks: pale tips at depth 4
                {
                    var perp = dir.Cross(normal);
                    if (perp.LengthSq < 1e-8) perp = dir.Cross(right);
                    perp = perp.Normalized();
                    for (int f = -1; f <= 1; f += 2)
                    {
                        var forkDir = (dir * 0.80 + perp * (f * 0.24) + normal * 0.20).Normalized();
                        work.Add((end, forkDir, len * 0.72, rad * 0.72, depth + 1));
                    }
                }
                else
                {
                    Primitives.Ellipsoid(mesh, end + dir * 0.0004, new Vec3(0.0006, 0.0006, 0.0006), 3, 4,
                        (u, vv) => (tipCol, 1, u, vv, 0, 0));
                }
            }
        }
    }

}
