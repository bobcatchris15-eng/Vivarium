using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Flora drawn as one MultiMesh per species and detail level, rebuilt from authoritative state on a short
/// cadence. The render instances can be discarded and rebuilt at any time without touching the simulation.
/// </summary>
public partial class FloraRenderer : Node3D
{
    // Legacy diagnostic parameter; Dewbonnet gills now use a normal map at every tier.
    internal const int DistantGillStations = 3;
    internal const int DistantEmberMicroSides = 3;
    // Far caps retain their angular rim; only radial surface tessellation changes.
    internal const int DistantDewbonnetCapRings = 4;
    private VivariumWorld _w = null!;
    private const int DefaultMorphVariants = 5;
    private const int MigratedMorphVariants = 12;
    private static bool IsExpansionClimber(string shape) => shape is "shadewrap" or "bitterbine" or "trumpetbind";
    private static int MorphVariantsFor(string shape) => FloraGeometryPlacement.MorphCount(shape);
    private sealed class VariantLayer { public MultiMeshInstance3D Full = null!; public MultiMeshInstance3D? Fruit, Juvenile, Tendril; public int FullTris, FruitTris, JuvenileTris, TendrilTris; public bool CanCastShadow; public StructuralSurfaceIndex? StructuralSurface; }
    private sealed class Layer
    {
        public VariantLayer[] Variants; public MultiMeshInstance3D? Veins; public int VeinTris;
        public int MorphCount; public FloraVisualProfile? Profile;
        public Layer(int count, FloraVisualProfile? profile = null)
        { MorphCount = count; Profile = profile; Variants = new VariantLayer[count * OrganismMeshes.FloraLodTiers]; }
    }
    private readonly Dictionary<EntityId, int> _visualTiers = new();
    private readonly Dictionary<EntityId, int> _distanceTiers = new();
    private bool _reducedDistantPlants = true;
    private double _distanceLodScale = 1;
    /// <summary>Distance band multiplier; permits matched in-world measurement without rebuilding geometry.</summary>
    public double DistanceLodScale
    {
        get => _distanceLodScale;
        set
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (_distanceLodScale == value) return;
            _distanceLodScale = value;
            _visualTiers.Clear(); _distanceTiers.Clear(); _accum = 999;
        }
    }
    public bool ReducedDistantPlants
    {
        get => _reducedDistantPlants;
        set
        {
            if (_reducedDistantPlants == value) return;
            _reducedDistantPlants = value;
            _visualTiers.Clear(); _distanceTiers.Clear(); _accum = 999;
        }
    }
    private Vector3 _detailCameraPosition, _detailCameraForward;
    private Vector3 _detailGuardianPosition;
    private float _detailPixels, _detailOrthoSize;
    private bool _detailOrthogonal;
    private void CaptureDetailCamera()
    {
        _detailGuardianPosition = _w.Guardian is { } guardian ? Bridge.V(guardian.Position) : Vector3.Zero;
        if (Camera == null) return;
        _detailCameraPosition = Camera.GlobalPosition; _detailCameraForward = -Camera.GlobalBasis.Z;
        float viewport = Camera.GetViewport().GetVisibleRect().Size.Y;
        _detailOrthogonal = Camera.Projection == Camera3D.ProjectionType.Orthogonal;
        _detailOrthoSize = Camera.Size;
        _detailPixels = _detailOrthogonal ? viewport : viewport / (2f*MathF.Tan(Mathf.DegToRad(Camera.Fov)*.5f));
    }
    private static (string Species, int Variant) MorphKey(string species, int variant) => (species, variant);
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, double> _wobbleStart = new();
    public RenderDirtyRegions DirtyRegions { get; } = new(tileSize: 2.0f);
    public RenderSnapshot? CurrentSnapshot { get; private set; }
    private object? _snapshotWorld;
    /// <summary>
    /// Static-ish per-plant placement. A camera-driven refresh reuses these values; the rock/log ground
    /// scan, surface normal, light field and moisture field are resampled only when the world changes or
    /// roughly every 32 simulation ticks.
    /// </summary>
    private struct PlantPlacement
    {
        public Vec2 Position;
        public int TerrainVersion;
        public int PropsVersion;
        public long SampledTick;
        public float GroundY;
        public Vector3 SurfaceNormal;
        public Basis UprightBasis;
        public FloraSpeciesDef Def;
        public float LocalLight;
        public float LocalMoisture;
    }
    private readonly Dictionary<EntityId, PlantPlacement> _placements = new();
    private readonly List<EntityId> _stalePlacements = new();
    private const long PlacementResampleTicks = 32;
    private readonly HashSet<EntityId> _currentPlantIds = new();
    private double _accum = 999;
    private double _lastMissingCheck;
    private double _clock;
    // Keep the currently drawn buffers intact while preparing the next population snapshot. Each unit of
    // work is one individual or one upload, so camera input never waits for an entire flora rebuild.
    private const double RefreshBudgetMs = 4.0;
    private IEnumerator<bool>? _refresh;
    // Every iterator MoveNext() is synchronous: the 2ms/4ms budgets can only
    // check after it returns. Track individual oversized steps separately so
    // a 650ms hitch cannot hide behind the declared per-frame budget.
    private double _peakMissingMs, _peakBuildStepMs, _peakRefreshStepMs;
    private double _peakSpeciesStartMs, _peakSnapshotMs;
    private string _slowestSpecies = "";
    public string TakeRefreshDiagnostics()
    {
        var result = $"missing={_peakMissingMs:0.0}ms buildStep={_peakBuildStepMs:0.0}ms " +
            $"speciesStart={_peakSpeciesStartMs:0.0}ms({ _slowestSpecies }) " +
            $"refreshStep={_peakRefreshStepMs:0.0}ms snapshot={_peakSnapshotMs:0.0}ms";
        _peakMissingMs = _peakBuildStepMs = _peakRefreshStepMs = 0;
        _peakSpeciesStartMs = _peakSnapshotMs = 0;
        _slowestSpecies = "";
        return result;
    }
    private IEnumerator<bool>? _build;
    private ContentLibrary? _builtContent;
    private readonly List<Node> _buildingNodes = new();
    // Stable per-organism ownership while a plant stays inside the active light budget.
    // Lights outside the budget are parked in _luminousPool and reused, never accumulated.
    private readonly Dictionary<EntityId,OmniLight3D> _luminousLights = new();
    private readonly List<OmniLight3D> _luminousPool = new();
    private readonly List<(EntityId Id, FloraSpeciesDef Species, Vector3 Position, float Range, float Energy, float Score)> _lightCandidates = new();
    private readonly List<(OmniLight3D Light, float Energy, Vector3 Position, float Range)> _activeLuminous = new();
    private readonly HashSet<EntityId> _selectedLuminous = new();
    private readonly List<EntityId> _luminousToPool = new();
    /// <summary>At most this many bioluminescent OmniLight3D nodes are energised at once. Every
    /// overlapping light is a full per-pixel shader pass over every lit surface in its range.</summary>
    private const int MaxActiveLuminousLights = 28;
    public int LuminousLightCacheAuditRefreshes { get; private set; }
    public int LuminousLightCacheAuditLights { get; private set; }
    public long LuminousLightCacheAuditPairs { get; private set; }
    public float MaximumLuminousLightCacheAuditError { get; private set; }
    /// <summary>How much of the overlap work the cell-level skip removed.
    /// A measurement only: the overlap value is the same either way.</summary>
    public long LuminousOverlapCellsVisited { get; private set; }
    public long LuminousOverlapCellsSkipped { get; private set; }
    public long LuminousOverlapPairsExamined { get; private set; }
    public long LuminousOverlapRefreshes { get; private set; }
    /// <summary>Total time spent in the overlap loop over the run.</summary>
    public double LuminousOverlapTotalMs { get; private set; }
    public int LuminousLightCount=>_luminousLights.Values.Count(l=>l.Visible);
    /// <summary>Diagnostics for the luminous light update.</summary>
    public int LuminousLightSnapshotCount { get; private set; }
    public int LuminousLightActiveCount { get; private set; }
    public float LuminousNightFactor { get; private set; }
    /// <summary>All currently present species are built and one complete instance refresh has been uploaded.</summary>
    public bool PopulationReady { get; private set; }
    private readonly HashSet<EntityId> _publishedPlants = new();
    public bool IsPlantPublished(EntityId id) => _publishedPlants.Contains(id);
    public Mesh? PlacementGhostMesh(string speciesId) => _layers.TryGetValue(speciesId, out var layer)
        ? (layer.Variants[0].Juvenile ?? layer.Variants[0].Full).Multimesh.Mesh : null;
    public int LoadedSpeciesCount => _layers.Count;
    public bool HasLegacyLayer(string speciesId) => _layers.ContainsKey(speciesId);
    private readonly List<FloraSnapshotItem> _snapshot = new();
    private readonly Dictionary<string, List<FloraSnapshotItem>> _veinPlants = new(StringComparer.Ordinal);
    // Render-only corrected segment centerlines are shared by foliage and vein uploads for this refresh.
    private readonly Dictionary<EntityId, IReadOnlyList<ClimberSegmentSnapshot>> _renderClimberSegments = new();
    private readonly List<(FloraSnapshotItem Plant, FloraSpeciesDef Species)> _woodySupports = new();
    /// <summary>At most this many luminous plant lights cast shadows.</summary>
    private const int MaxShadowCastingLights = 6;
    private const double ShadowReselectSeconds = 0.5;
    private long _shadowSelectedAt;
    private bool _shadowDirty = true;
    private readonly List<DeadFloraSnapshotItem> _deadSnapshot = new();
    private readonly List<EntityId> _expired = new();
    private int _terrainVersion = -1;
    private int _quality = 1;
    private int _shadowMaxTier = 0;
    /// <summary>Largest projected-size tier allowed to cast. Visible geometry is unchanged.</summary>
    public int ShadowMaxTier
    {
        get => _shadowMaxTier;
        set { _shadowMaxTier = Math.Clamp(value, -1, 2); ApplyFloraShadows(); }
    }
    private void ApplyFloraShadows()
    {
        foreach (var layer in _layers.Values)
            for (int index = 0; index < layer.Variants.Length; index++)
            {
                var variant = layer.Variants[index];
                var shadow = _quality >= 1 && variant.CanCastShadow && index / layer.MorphCount <= _shadowMaxTier
                    ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                variant.Full.CastShadow = shadow;
                if (variant.Fruit != null) variant.Fruit.CastShadow = shadow;
                if (variant.Juvenile != null) variant.Juvenile.CastShadow = shadow;
            }
    }
    public Camera3D? Camera { get; set; }
public int Quality
    {
        get => _quality;
        set
        {
            if (_quality == value) return;
            _quality = value;
            ApplyFloraShadows();
        }
    }
    public int Visible_ { get; private set; }
    public long TrianglesDrawn { get; private set; }

    public void Build(VivariumWorld w)
    {
        _build?.Dispose(); _build = null;
        _refresh?.Dispose(); _refresh = null;
        foreach (var node in _buildingNodes) node.QueueFree();
        _buildingNodes.Clear();
        foreach (var light in _luminousLights.Values) light.QueueFree();
        _luminousLights.Clear();
        foreach (var light in _luminousPool) light.QueueFree();
        _luminousPool.Clear();
        _lightCandidates.Clear(); _activeLuminous.Clear(); _selectedLuminous.Clear(); _luminousToPool.Clear();
        _snapshot.Clear(); _deadSnapshot.Clear(); _wobbleStart.Clear(); _publishedPlants.Clear();
        _renderClimberSegments.Clear();
        _pooledBuffers.Clear(); _placements.Clear(); _stalePlacements.Clear(); DirtyRegions.Clear();
        _full.Clear(); _fruit.Clear(); _juvenile.Clear(); _veins.Clear(); _tendrils.Clear();
        _veinPlants.Clear();
        _visualTiers.Clear();
        _distanceTiers.Clear();
        Visible_ = 0; TrianglesDrawn = 0;
        PopulationReady = false;
        _w = w;
        _terrainVersion = _w.Terrain.Version;
        DirtyRegions.MarkAll(DirtyReason.TerrainVersion, _w.Terrain.Version, _clock);
        if (!ReferenceEquals(_builtContent, w.Content))
        {
            foreach (var c in GetChildren()) c.QueueFree();
            _layers.Clear();
            _builtContent = w.Content;
        }
        // Completed geometry/materials depend on content, not world seed. Keep them through New World/load.
        foreach (var layer in _layers.Values)
        {
            foreach (var variant in layer.Variants)
            {
                variant.Full.Multimesh.VisibleInstanceCount = 0;
                variant.Full.Multimesh.InstanceCount = 0;
                if (variant.Fruit != null) { variant.Fruit.Multimesh.VisibleInstanceCount = 0; variant.Fruit.Multimesh.InstanceCount = 0; }
                if (variant.Juvenile != null) { variant.Juvenile.Multimesh.VisibleInstanceCount = 0; variant.Juvenile.Multimesh.InstanceCount = 0; }
                if (variant.Tendril != null) { variant.Tendril.Multimesh.VisibleInstanceCount = 0; variant.Tendril.Multimesh.InstanceCount = 0; }
            }
            if (layer.Veins != null) { layer.Veins.Multimesh.VisibleInstanceCount = 0; layer.Veins.Multimesh.InstanceCount = 0; }
        }
        RequestMissingLayers();
        _accum = 999;
    }

    private void RequestMissingLayers()
    {
        if (_build != null) return;
        // Build layers for living plants and corpses that lack one.
        var deadSpecies = _w.DeadFlora.Items.Select(p => p.SpeciesId)
            .ToHashSet(StringComparer.Ordinal);
        var needed = _w.Flora.Items.Select(p => p.SpeciesId)
            .Concat(deadSpecies)
            .Where(id => !_layers.ContainsKey(id))
            .ToHashSet(StringComparer.Ordinal);
        if (needed.Count == 0) return;
        PopulationReady = false;
        var cameraPosition = Camera?.GlobalPosition ?? Vector3.Zero;
        var nearest = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var plant in _w.Flora.Items)
        {
            if (!needed.Contains(plant.SpeciesId)) continue;
            float distance = new Vector2((float)plant.X-cameraPosition.X,(float)plant.Z-cameraPosition.Z).LengthSquared();
            nearest[plant.SpeciesId] = Math.Min(nearest.GetValueOrDefault(plant.SpeciesId,float.PositiveInfinity),distance);
        }
        _build = BuildLayers(_w.Content.Flora.Where(sp => needed.Contains(sp.Id))
            .OrderBy(sp => nearest.GetValueOrDefault(sp.Id,float.PositiveInfinity)).ThenBy(sp => sp.Id,StringComparer.Ordinal)).GetEnumerator();
    }

    /// <summary>
    /// Mesh tasks allowed in flight at once. Mesh generation is pure managed work, so it is free to overlap; the
    /// cap keeps a load from stampeding every core on a machine that is already running the game.
    /// </summary>
    private const int MaxPendingVariantBuilds = 24;

    private sealed class InflightBuild
    {
        public Task[] Tasks = Array.Empty<Task>();
        /// <summary>Nodes attached for this species that no layer owns yet, so a rebuild can still free them.</summary>
        public readonly List<Node> Nodes = new();
        /// <summary>Main-thread work that turns the finished meshes into GPU resources and publishes the layer.</summary>
        public Func<IEnumerable<bool>> Publish = null!;
        public int Pending => Tasks.Count(t => !t.IsCompleted);
    }

    /// <summary>
    /// Builds the geometry layer for every species the world needs, overlapping the mesh generation of several
    /// species at a time. The work is already shaped as background tasks, but the old loop awaited one mesh before
    /// starting the next, so a 40-species catalog took the SUM of every build and species surfaced one at a time in
    /// catalog order. Now total time approaches the slowest single build. Material setup and every GPU resource
    /// still happen on this thread, in catalog order, one completed species at a time.
    /// </summary>
    private IEnumerable<bool> BuildLayers(IEnumerable<FloraSpeciesDef> species)
    {
        var queue = new Queue<FloraSpeciesDef>(species);
        var inflight = new List<InflightBuild>();

        while (queue.Count > 0 || inflight.Count > 0)
        {
            // Launch whole species while the in-flight budget allows, so their meshes build concurrently.
            while (queue.Count > 0)
            {
                int pending = 0;
                foreach (var b in inflight) pending += b.Pending;
                if (pending >= MaxPendingVariantBuilds) break;
                inflight.Add(StartSpeciesBuild(queue.Dequeue()));
            }

            // Publish whatever finished. A species becomes drawable the moment its own meshes are done, rather
            // than waiting behind everything ahead of it in the catalog. Throttle to at most one species per frame
            // to eliminate main-thread hitches.
            for (int i = inflight.Count - 1; i >= 0; i--)
            {
                var b = inflight[i];
                if (b.Pending > 0) continue;
                // A species can contain dozens of meshes. Yield after each upload, not just each species.
                using (var publish = b.Publish().GetEnumerator())
                    while (publish.MoveNext()) yield return true;
                // The published layer now owns these nodes, exactly as the serial build transferred them.
                foreach (var n in b.Nodes) _buildingNodes.Remove(n);
                inflight.RemoveAt(i);
                break;
            }

            yield return true;
        }
    }

    /// <summary>
    /// Prepares one species on this thread (material, layer, seeds) and starts every variant mesh as a background
    /// task. Returns immediately; the returned <see cref="InflightBuild.Publish"/> finishes it on this thread later.
    /// </summary>
    private InflightBuild StartSpeciesBuild(FloraSpeciesDef sp)
    {
        long speciesStarted=Stopwatch.GetTimestamp();
        GD.Print("FLORA_LAYER_BUILD_BEGIN " + sp.Id);
        var profile = FloraVisualProfile.Load(sp.Id, sp: sp);
        var mat = Bridge.Shader("res://Shaders/flora.gdshader");
        NightFloraMaterials.Bind(mat, sp);
        if (sp.Shape == "ashcomb") mat.SetShaderParameter("dry_fungal_cuticle", true);
        mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
        mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, _ => 2 });
        mat.SetShaderParameter("deform_leaf_tips", MorphVariantsFor(sp.Shape) == MigratedMorphVariants);
        mat.SetShaderParameter("sway", 0.0f);
        Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
        FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
        var leafMat = profile?.LeafMaterial(animated: false);
        if (leafMat != null) NightFloraMaterials.Bind(leafMat, sp);
        if (profile != null)
        {
            Bridge.BindSurface(mat, "bark", profile.Bark);
            mat.SetShaderParameter("bark_tint", new Vector3(profile.BarkTint[0], profile.BarkTint[1], profile.BarkTint[2]));
            leafMat!.SetShaderParameter("plant_stiffness", sp.Woody != null ? 7f : 3f);
            leafMat.SetShaderParameter("wind_strength", 0.0f);
        }
        var layer = new Layer(MorphVariantsFor(sp.Shape), profile);
        bool buildsJuvenile = sp.Climber != null
            || sp.Shape.StartsWith("microcanopy_", StringComparison.Ordinal)
            || sp.Shape is "fern" or "veilfern" or "hookthicket_brake"
                or "cloudtuft" or "cinderberry" or "brightknot" or "capstar_moss"
                or "deepquill" or "mirepocket" or "pondstars" or "burrflag";
        ulong speciesSeed = Hash.Fnv1a64("flora.visual." + sp.Id);

        var tasks = new List<Task<(MeshData Full, MeshData? Young, MeshData? Fruit, StructuralSurfaceIndex? Structural)>>();
        // Capture review policy on the publishing thread. Review hooks can use
        // Godot resources and must never be invoked by the geometry workers.
        bool prepareProfileUpload = profile != null && FloraVisualProfile.ReviewMeshTransform == null;
        for (int v = 0; v < layer.Variants.Length; v++)
        {
            int morph = v % layer.MorphCount;
            // Every species draws from cached geometric tiers (0 near .. 2 far), chosen per individual by
            // projected screen size. Visual-profile species also switch their leaf tessellation tier.
            int lod = v / layer.MorphCount;
            int? tier = profile == null ? null : lod;
            ulong seed = Rng.Mix(speciesSeed, (ulong)(morph + 1) * 0x9E3779B97F4A7C15UL);
            // Calibration and parametric tessellation are pure managed work. Never block input/rendering on them.
            tasks.Add(Task.Run(() =>
            {
                var fullMesh = sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, seed, attached: true, lod) : OrganismMeshes.FloraTier(sp, seed, lod, tieredSource: profile != null, farGillStations: DistantGillStations, farEmberMicroSides: DistantEmberMicroSides, farDewbonnetCapRings: DistantDewbonnetCapRings);
                MeshData? youngMesh = buildsJuvenile
                    ? sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, seed, attached: false, lod) : OrganismMeshes.FloraTier(sp, seed, lod, juvenile: true, tieredSource: profile != null)
                    : null;
                var fruitMesh = sp.Climber != null ? null : OrganismMeshes.FloraFruitingFromBase(fullMesh, sp, seed);
                var structural = IsContactHostShape(sp) && lod == 0 ? StructuralSurfaceIndex.Build(fullMesh) : null;
                void Prepare(MeshData mesh)
                {
                    if (prepareProfileUpload) FloraVisualProfile.PrepareUpload(mesh);
                    else if (profile == null) Bridge.PrepareMeshUpload(mesh);
                }
                Prepare(fullMesh);
                if (youngMesh != null) Prepare(youngMesh);
                if (fruitMesh != null) Prepare(fruitMesh);
                return (Full: fullMesh, Young: youngMesh, Fruit: fruitMesh, Structural: structural);
            }));
        }

        var build = new InflightBuild { Tasks = tasks.Cast<Task>().ToArray() };
        IEnumerable<bool> PublishLayer()
        {
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                var prepared = tasks[v].GetAwaiter().GetResult();
                var full = prepared.Full;
                bool canCastShadow = sp.Colony == null && sp.Archetype is "plant" or "fungus" && sp.Height >= 0.055;
                bool castShadow = Quality >= 1 && canCastShadow && v / layer.MorphCount <= ShadowMaxTier;
                long uploadStart=Stopwatch.GetTimestamp();
                var vl = new VariantLayer { Full = MakeMmi($"Flora_{sp.Id}_{v}", profile == null ? Bridge.ToArrayMesh(full, mat) : profile.Compile(full, mat, leafMat!), castShadow), FullTris = full.TriangleCount, CanCastShadow = canCastShadow, StructuralSurface = prepared.Structural };
                AddChild(vl.Full); build.Nodes.Add(vl.Full);
                double fullMs=Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
                if(fullMs>25)
                    GD.Print($"FLORA_PUBLISH_SLOW species={sp.Id} variant={v} organ=full " +
                        $"ms={fullMs:0.0} verts={full.VertexCount} tris={full.TriangleCount} profile={profile!=null}");
                yield return true;
                if (buildsJuvenile)
                {
                    var young = prepared.Young!;
                    uploadStart=Stopwatch.GetTimestamp();
                    vl.Juvenile = MakeMmi($"Flora_{sp.Id}_{v}_juvenile", profile == null ? Bridge.ToArrayMesh(young, mat) : profile.Compile(young, mat, leafMat!), castShadow);
                    vl.JuvenileTris = young.TriangleCount;
                    AddChild(vl.Juvenile); build.Nodes.Add(vl.Juvenile);
                    double juvenileMs=Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
                    if(juvenileMs>25)
                        GD.Print($"FLORA_PUBLISH_SLOW species={sp.Id} variant={v} organ=young ms={juvenileMs:0.0} verts={young.VertexCount}");
                    yield return true;
                }
                if (sp.Climber != null)
                {
                    // The growing tip of every shoot: a thin tendril rather than a leaf cluster.
                    uploadStart = Stopwatch.GetTimestamp();
                    ulong tipSeed = Rng.Mix(speciesSeed, (ulong)((v % layer.MorphCount) + 1) * 0x9E3779B97F4A7C15UL);
                    var tip = OrganismMeshes.ClimberNodeTipTier(sp, tipSeed, v / layer.MorphCount);
                    vl.Tendril = MakeMmi($"Flora_{sp.Id}_{v}_tendril", Bridge.ToArrayMesh(tip, mat));
                    vl.TendrilTris = tip.TriangleCount;
                    AddChild(vl.Tendril); build.Nodes.Add(vl.Tendril);
                    yield return true;
                }
                if (prepared.Fruit is { } fruit)
                {
                    uploadStart=Stopwatch.GetTimestamp();
                    var fruitMat = (ShaderMaterial)mat.Duplicate();
                    NightFloraMaterials.Bind(fruitMat, sp);
                    if (sp.Reproduction == null) fruitMat.SetShaderParameter("surface_mode", 3);
                    vl.Fruit = MakeMmi($"Flora_{sp.Id}_{v}_fruit", profile == null ? Bridge.ToArrayMesh(fruit, fruitMat) : profile.Compile(fruit, fruitMat, leafMat!), castShadow);
                    vl.FruitTris = fruit.TriangleCount;
                    AddChild(vl.Fruit); build.Nodes.Add(vl.Fruit);
                    double fruitMs=Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds;
                    if(fruitMs>25)
                        GD.Print($"FLORA_PUBLISH_SLOW species={sp.Id} variant={v} organ=fruit ms={fruitMs:0.0} verts={fruit.VertexCount}");
                    yield return true;
                }
                layer.Variants[v] = vl;
            }
            if (sp.CreepSpeed > 0 || sp.Climber != null)
            {
                // Persistent links joining each network node to the node it grew from (plasmodial veins or climber stems).
                var vein = new MeshData();
                var col = IsExpansionClimber(sp.Shape)
                    ? Primitives.Scale(Primitives.Mix(sp.Color, sp.Color2, 0.10), 0.64)
                    : Primitives.Mix(sp.Color, sp.Color2, 0.30);
                var vpath = new List<Vec3>(); var vrad = new List<double>();
                if (sp.Climber is { } climber)
                {
                    // A climber's stem is a tapering tube whose radius ratio across one internode is the species'
                    // stem taper, so consecutive internodes meet without a step and the stem reads as one stem.
                    // The tube overlaps its neighbours a little so their end caps stay buried inside the stem.
                    // Four rings is all a straight internode needs: the taper is gentle and the stem is long.
                    double ratio = Math.Pow(climber.StemTaper, climber.SegmentLength);
                    const double overlap = 0.10;
                    for (int i = 0; i <= 3; i++)
                    {
                        double x = -overlap + (1.0 + 2 * overlap) * (i / 3.0);
                        vpath.Add(new Vec3(x, 0, 0));
                        vrad.Add(Math.Pow(ratio, x));
                    }
                }
                else
                {
                    // a slightly arched, pinched tube: thinner at mid-length like a real plasmodial vein
                    for (int i = 0; i <= 8; i++)
                    {
                        double t = i / 8.0;
                        vpath.Add(new Vec3(-0.02 + 1.04 * t, 0.1 * Math.Sin(Math.PI * t), 0));
                        vrad.Add(0.75 + 0.25 * Math.Cos(t * 2 * Math.PI));
                    }
                }
                Primitives.Tube(vein, vpath, vrad, 6, (i, v) => (col, 1, i, v, 0, 0));
                layer.Veins = MakeMmi($"Flora_{sp.Id}_veins", Bridge.ToArrayMesh(vein, mat));
                layer.VeinTris = vein.TriangleCount;
                AddChild(layer.Veins); build.Nodes.Add(layer.Veins);
            }
            // Refresh can yield while enumerating the dictionary; retire it before publishing a new layer.
            _refresh?.Dispose(); _refresh = null;
            _layers[sp.Id] = layer;
            ApplyFloraShadows();
            GD.Print("FLORA_LAYER_BUILD_END " + sp.Id + " loaded_species=" + _layers.Count);
            _accum = 999;
        }
        build.Publish = PublishLayer;
        double speciesStartMs=Stopwatch.GetElapsedTime(speciesStarted).TotalMilliseconds;
        if(speciesStartMs>_peakSpeciesStartMs)
        {
            _peakSpeciesStartMs=speciesStartMs;
            _slowestSpecies=sp.Id;
        }
        return build;
    }


    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh, bool castShadow = false) => new()
    {
        Name = name,
        CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        // UseColor carries per-instance tint (colonial moss/lichen; white = no change), multiplied into the
        // mesh's baked vertex colour by the multimesh pipeline before the shader sees COLOR.
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    public void Wobble(IEnumerable<EntityId> ids)
    {
        foreach (var id in ids)
        {
            _wobbleStart[id] = _clock;
            if (CurrentSnapshot != null && CurrentSnapshot.TryGetFlora(id, out var f))
                DirtyRegions.MarkPoint(new Vector3((float)f.X, 0, (float)f.Z), 0.5f, DirtyReason.FloraAttributes, CurrentSnapshot.Version, _clock);
        }
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Flora");
        _clock += delta;
        if (_w == null) return;
        if (_w.Terrain.Version != _terrainVersion)
        {
            _terrainVersion = _w.Terrain.Version;
            DirtyRegions.MarkAll(DirtyReason.TerrainVersion, _w.Terrain.Version, _clock);
        }
        if (_build != null || _clock - _lastMissingCheck >= 1.0)
        {
            _lastMissingCheck = _clock;
            long missingStart = Stopwatch.GetTimestamp();
            RequestMissingLayers();
            _peakMissingMs = Math.Max(_peakMissingMs,
                Stopwatch.GetElapsedTime(missingStart).TotalMilliseconds);
        }
        if (_build != null)
        {
            long buildStarted = Stopwatch.GetTimestamp();
            double buildBudget = PopulationReady ? 2.0 : 50.0;
            for (int step = 0; step < 16 && _build != null; step++)
            {
                long stepStarted = Stopwatch.GetTimestamp();
                bool advanced = _build.MoveNext();
                _peakBuildStepMs = Math.Max(_peakBuildStepMs,
                    Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
                if (!advanced) { _build.Dispose(); _build = null; break; }
                if (Stopwatch.GetElapsedTime(buildStarted).TotalMilliseconds >= buildBudget) break;
            }
        }
        _accum += delta;
        if (_refresh == null)
        {
            bool cameraChanged = Camera != null &&
                (Camera.GlobalPosition.DistanceSquaredTo(_detailCameraPosition) > .25f ||
                 (-Camera.GlobalBasis.Z).Dot(_detailCameraForward) < .995f);
            bool guardianMoved = _w.Guardian is { } guardian
                && Bridge.V(guardian.Position).DistanceSquaredTo(_detailGuardianPosition) > .0225f;
            if (_accum < (cameraChanged || guardianMoved ? .2 : 4.0)) return;
            _accum = 0;
            _refresh = Refresh().GetEnumerator();
        }
        long started = Stopwatch.GetTimestamp();
        do
        {
            long stepStarted = Stopwatch.GetTimestamp();
            bool advanced = _refresh.MoveNext();
            _peakRefreshStepMs = Math.Max(_peakRefreshStepMs,
                Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
            if (advanced) continue;
            _refresh.Dispose(); _refresh = null;
            break;
        } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < RefreshBudgetMs);
    }

    /// <summary>
    /// Creeping, running, or prostrate organisms (runners, moss carpets, cushions, crusts, and mats)
    /// that conform to their substrate and tilt with the local surface normal.
    /// In contrast, erect plants with stems, culms, trunks, or scapes grow world-up (negative gravitropism)
    /// regardless of the local slope.
    /// </summary>
    private static bool IsCreepingOrGroundHugging(FloraSpeciesDef sp)
    {
        if (sp.Woody != null || sp.IsTree || sp.Shape.StartsWith("shrub_") || sp.Shape.StartsWith("microcanopy_"))
            return false;

        if (sp.Shape.EndsWith("_stem") || sp.Shape.EndsWith("_cattail") || sp.Shape.EndsWith("_thistle") || sp.Shape.EndsWith("_umbel")
            || sp.Shape.EndsWith("_brake") || sp.Shape.EndsWith("_curtain") || sp.Shape.EndsWith("_bromeliad")
            || sp.Shape is "pampas" or "reed" or "ribbonweed" or "milfoil" or "submerged_whorl"
            || sp.Shape is "fern" or "veilfern" or "tussock" or "pairedleaf"
            || sp.Shape is "pitcher_rosette" or "snaptrap_rosette"
            || sp.Shape is "carrion_bell" or "herb" or "mushroom_cluster" or "cap")
            return false;

        if (sp.Shape is "creeper" or "roundleaf" or "trifoliate")
            return true;

        if (sp.Shape is "sundew_mat" or "carpet" or "cushion" or "crust" or "foliose" or "fruticose" or "capitula" or "embercup_moss"
            or "succulent" or "iceplant" or "capstar_moss" or "cragset")
            return true;

        if (sp.Shape is "bonecup_fungus" or "rain_jelly" or "bracket" or "glass_antlers")
            return true;

        return sp.Tags.Contains("groundcover") || sp.Tags.Contains("mat");
    }

    private static bool IsUpright(FloraSpeciesDef sp) =>
        !IsCreepingOrGroundHugging(sp)
        && sp.Shape is not ("floatleaf" or "glasslily_pad")
        && sp.Climber == null
        && !sp.Shape.StartsWith("vine");

    public override void _ExitTree()
    {
        _build?.Dispose(); _build = null;
        _refresh?.Dispose(); _refresh = null;
        _pooledBuffers.Clear(); _placements.Clear(); _stalePlacements.Clear(); DirtyRegions.Clear();
        _snapshot.Clear(); _deadSnapshot.Clear(); _wobbleStart.Clear();
    }

    private readonly Dictionary<(string, int), (List<Transform3D> T, List<Color> Tint, List<Color> C)> _full = new(), _fruit = new(), _juvenile = new(), _tendrils = new();
    private readonly Dictionary<string, (List<Transform3D> T, List<Color> Tint, List<Color> C)> _veins = new();

    /// <summary>
    /// Where a climber or bracket attaches: the nearest log or rock (sim angle toward it from p, its top height,
    /// and for logs the point on the trunk surface closest to p). Null when nothing is within reach.
    /// </summary>
    private (double Angle, double Top, double Dist, Vector3 Surface, double Outward)? Anchor(Vector2 p, double reach, HashSet<string>? supports = null)
    {
        (double, double, double, Vector3, double)? best = null;
        double bestD = reach;
        var sp = new Vivarium.Sim.Core.Vec2(p.X, p.Y);
        if (supports == null || supports.Contains("log"))
        foreach (var l in _w.Props.Logs)
        {
            var axis = Vivarium.Sim.Core.Vec2.FromAngle(l.RotationY);
            var rel = sp - l.Position;
            double along = Math.Clamp(rel.Dot(axis), -l.Length / 2, l.Length / 2);
            var onAxis = l.Position + axis * along;
            var outV = sp - onAxis;
            double d = Math.Max(0, outV.Length - l.Radius);
            if (d > bestD) continue;
            bestD = d;
            double outward = outV.LengthSq > 1e-10 ? outV.Angle : l.RotationY + Math.PI / 2;
            var surf = onAxis + Vivarium.Sim.Core.Vec2.FromAngle(outward) * (l.Radius * 0.92);
            best = ((onAxis - sp).LengthSq > 1e-10 ? (onAxis - sp).Angle : outward + Math.PI, l.Y + l.Radius, d,
                    new Vector3((float)surf.X, (float)l.Y, (float)surf.Z), outward);
        }
        if ((supports == null || supports.Contains("log")) && _w.PilotTree is { } pilot &&
            pilot.TryLogSurface(Vivarium.Sim.Core.Vec3.FromXZ(sp, _w.GroundHeight(sp) + .02), reach,
                out var pilotSurface, out var pilotNormal, out double pilotTop))
        {
            double d = Vivarium.Sim.Core.Vec2.Distance(sp, pilotSurface.XZ);
            if (d <= bestD)
            {
                bestD = d;
                double outward = pilotNormal.XZ.Angle;
                best = (outward + Math.PI, pilotTop, d,
                    new Vector3((float)pilotSurface.X, (float)pilotSurface.Y, (float)pilotSurface.Z), outward);
            }
        }
        if (supports == null || supports.Contains("rock"))
        foreach (var r in _w.Props.Rocks)
        {
            double d = Math.Max(0, Vivarium.Sim.Core.Vec2.Distance(sp, r.Position) - r.FootprintRadius * 0.8);
            if (d > bestD) continue;
            bestD = d;
            var to = r.Position - sp;
            double ang = to.LengthSq > 1e-10 ? to.Angle : 0;
            best = (ang, r.Y + r.SizeY, d, new Vector3((float)r.X, (float)(r.Y + r.SizeY * 0.5), (float)r.Z), ang + Math.PI);
        }
        if (supports != null && supports.Contains("woody"))
        {
            foreach (var (w, wsp) in _woodySupports)
            {
                double d = Vivarium.Sim.Core.Vec2.Distance(sp, w.Position);
                if (d > bestD) continue;
                bestD = d;
                var to = w.Position - sp;
                double ang = to.LengthSq > 1e-10 ? to.Angle : 0;
                double height = wsp.Height * (0.45 + 0.55 * Math.Sqrt(w.BiomassFraction(wsp)));
                double ground = _w.GroundHeight(w.Position);
                double boleR = Math.Max(0.08, wsp.RadiusAtMax * 0.12);
                var toNorm = to.LengthSq > 1e-10 ? to.Normalized() : new Vivarium.Sim.Core.Vec2(1, 0);
                var surfacePt = w.Position - toNorm * boleR;
                best = (ang, ground + height, Math.Max(0, d - boleR),
                    new Vector3((float)surfacePt.X, (float)ground, (float)surfacePt.Z), (-toNorm).Angle);
            }
        }
        return best;
    }

    /// <summary>Explicit callers get a complete synchronous refresh; normal frames use the bounded path.</summary>
    public void Rebuild(bool resetDetailHistory = false)
    {
        if (_w == null) return;
        if (resetDetailHistory) { _visualTiers.Clear(); _distanceTiers.Clear(); }
        _refresh?.Dispose(); _refresh = null;
        foreach (bool _ in Refresh()) { }
        _accum = 0;
    }

    private IEnumerable<bool> Refresh()
    {
        // Capture an immutable, versioned presentation snapshot so background build tasks
        // and presentation iterations do not race mutable simulation state.
        CaptureDetailCamera();
        long snapshotStarted=Stopwatch.GetTimestamp();
        bool reuseSnapshot = CurrentSnapshot != null && ReferenceEquals(_snapshotWorld, _w) && CurrentSnapshot.IsCurrentFor(_w);
        var snapshot = reuseSnapshot ? CurrentSnapshot! : RenderSnapshot.Capture(_w);
        _snapshotWorld = _w;
        _peakSnapshotMs=Math.Max(_peakSnapshotMs,
            Stopwatch.GetElapsedTime(snapshotStarted).TotalMilliseconds);
        CurrentSnapshot = snapshot;
        _snapshot.Clear();
        _snapshot.AddRange(snapshot.Flora);
        _renderClimberSegments.Clear();
        _woodySupports.Clear();
        foreach (var w in _snapshot)
        {
            // A species never changes for an individual, so a cached definition stays valid even when
            // the placement itself has expired. This keeps the catalog lookup out of routine refreshes.
            var wsp = _placements.TryGetValue(w.Id, out var cachedPlacement)
                && cachedPlacement.Def.Id == w.SpeciesId
                ? cachedPlacement.Def
                : _w.Content.FloraOrThrow(w.SpeciesId);
            if (wsp.Woody != null) _woodySupports.Add((w, wsp));
        }
        _deadSnapshot.Clear();
        _deadSnapshot.AddRange(snapshot.DeadFlora);
        var planes = Camera?.GetFrustum(); // one native array per refresh, never one per plant
        Plane[]? frustum = planes == null ? null : new Plane[planes.Count];
        if (planes != null) planes.CopyTo(frustum!, 0); // plane tests below stay entirely in managed code
        _expired.Clear();
        foreach (var (id, start) in _wobbleStart)
            if (_clock - start >= 1.2 || _w.Flora.Get(id) == null) _expired.Add(id);
        foreach (var id in _expired) _wobbleStart.Remove(id);

        // The instance set below is fully rebuilt, so the old per-plant dirty tiles had no consumer in
        // this renderer and only paid for dictionary writes. Keep the current id set for cache pruning.
        _currentPlantIds.Clear();
        foreach (var f in _snapshot) _currentPlantIds.Add(f.Id);
        if (_placements.Count > _currentPlantIds.Count + 64)
        {
            _stalePlacements.Clear();
            foreach (var id in _placements.Keys)
                if (!_currentPlantIds.Contains(id)) _stalePlacements.Add(id);
            foreach (var id in _stalePlacements) _placements.Remove(id);
            _stalePlacements.Clear();
        }

        ClearBuckets(_full); ClearBuckets(_fruit); ClearBuckets(_juvenile); ClearBuckets(_tendrils);
        foreach (var veinList in _veinPlants.Values) veinList.Clear();
        int visible = 0;
        long triangles = 0;
        int terrainVersion = _w.Terrain.Version, propsVersion = _w.Props.Version;
        long tick = _w.Clock.Tick;
        foreach (var f in _snapshot)
        {
            yield return false;
            if (_w.Flora.Get(f.Id) == null) continue;
            // A camera move only changes which instances are drawn. Reuse the sampled ground height,
            // surface normal, light/moisture and species definition instead of rescanning every rock
            // and log for every plant on every 0.2 s refresh.
            bool cacheHit = _placements.TryGetValue(f.Id, out var placement)
                && placement.TerrainVersion == terrainVersion
                && placement.PropsVersion == propsVersion
                && placement.Def.Id == f.SpeciesId
                && placement.Position.X == f.X
                && placement.Position.Z == f.Z
                && tick - placement.SampledTick < PlacementResampleTicks;
            if (!cacheHit)
            {
                var species = _w.Content.FloraOrThrow(f.SpeciesId);
                placement = SamplePlantPlacement(f, species, terrainVersion, propsVersion, tick);
                _placements[f.Id] = placement;
            }
            var sp = placement.Def;
            if (!_layers.ContainsKey(sp.Id)) continue;
            var visualLayer = _layers[sp.Id];
            if (visualLayer.Veins != null)
            {
                // Group by species once here so the vein pass never rescans the whole population
                // for each vein layer.
                if (!_veinPlants.TryGetValue(sp.Id, out var veinList))
                    _veinPlants[sp.Id] = veinList = new List<FloraSnapshotItem>();
                veinList.Add(f);
            }
            double r = f.Radius(sp);
            double h = sp.Colony != null
                ? sp.Colony.MaxHeight * (0.15 + 0.85 * f.HeightFactor)
                : sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
            // Preserve Kinkcane's authored internode angles at every growth size. The ordinary
            // radius/height curves stretch young plants vertically and would shrink a 34-degree bend.
            if (sp.Shape == "kinkcane_brake" && sp.Height > 0)
                r = h * sp.RadiusAtMax / sp.Height;
            var pos = new Vector3((float)f.X, placement.GroundY, (float)f.Z);
            bool followsWaterDepth = sp.Shape is "pondstars" or "bogfringe" or "deepquill" or "mirepocket";
            if (sp.Shape is "floatleaf" or "glasslily_pad")
            {
                // Pads are authored directly in metres (not the unit-XZ-radius-1 convention every other flora
                // shape uses), so fixing the horizontal scale to 1 keeps pad size constant regardless of the
                // individual's growth radius or local water depth. Only the vertical scale follows depth: the
                // petiole/flower stalks are built to reach unit Y = 1, so scaling Y by (surface - ground) + a
                // small proud offset lands every pad just above the true water surface instead of at a fixed
                // plant height (or, worse, fractionally submerged and hidden by the water surface shader).
                r = 1.0;
                double surface = _w.Water.SurfaceAt(f.Position);
                double rise = double.IsNaN(surface) ? 0.05 : Math.Max(0.02, surface - pos.Y);
                h = rise + 0.003;
            }
            else if (followsWaterDepth)
            {
                double surface = _w.Water.SurfaceAt(f.Position);
                double rise = double.IsFinite(surface) ? Math.Max(0.005, surface - pos.Y) : sp.Height;
                h = rise + 0.003;
            }
            if (frustum != null && !FloraVisible(frustum, pos, (float)r, (float)h)) continue;
            ulong hash = Rng.Mix(f.Id.Value, 0xF10);
            float yaw = (hash % 6283) / 1000f;
            // Mats conform to their substrate. Upright vascular plants respond to the actual local sky-openness
            // field: they lean slightly toward the more open side, while dry/unhealthy specimens lose some turgor
            // in a stable individual direction. This makes variation read as growth history rather than seed noise.
            var yawBasis = new Basis(Vector3.Up, yaw);
            // Surface-relative shoots share the water's horizontal frame across growth stages.
            bool isUpright = !followsWaterDepth && IsUpright(sp);

            if (isUpright)
            {
                yawBasis = placement.UprightBasis * yawBasis;
                double moisture = placement.LocalMoisture;
                double stress = MathD.Clamp01((1.0 - f.Health) * 0.7 + Math.Max(0, 0.32 - moisture) * 0.55);
                h *= 0.93 + 0.07 * MathD.Clamp01(0.55 * f.Health + 0.45 * Math.Min(1, moisture / 0.45));
                r *= 1.0 + stress * 0.035;
            }
            else if (!followsWaterDepth && sp.Shape is not ("vine" or "floatleaf" or "glasslily_pad"))
            {
                var norm = placement.SurfaceNormal;
                yawBasis = SurfaceFrame.TiltTo(norm) * yawBasis;
                float lift = (float)Math.Min(0.006, 0.002 + h * 0.04);
                pos += norm * lift;
                double moisture = placement.LocalMoisture;
                double stress = MathD.Clamp01((1.0 - f.Health) * 0.7 + Math.Max(0, 0.32 - moisture) * 0.55);
                h *= 0.93 + 0.07 * MathD.Clamp01(0.55 * f.Health + 0.45 * Math.Min(1, moisture / 0.45));
                r *= 1.0 + stress * 0.035;
            }
            if (sp.Climber != null && f.ClimberSegments is { Count: > 1 } segs)
            {
                segs = ProjectWoodyClimberSegments(sp, segs);
                _renderClimberSegments[f.Id] = segs;
                // Multi-segment climber: the stem is drawn as one tapering tube in the vein layer, and the
                // foliage sits on the nodes. Leaves are spaced along the shoot the way they are on a real one,
                // rather than on every internode, and the growing tip of each shoot is a bare tendril.
                var cTint = f.Tint;
                int cTier = VisualTier(f.Id, pos, (float)Math.Max(h, 0.3), (float)Math.Max(r, 0.3));
                int cVariant = (int)((hash >> 8) % (ulong)_layers[sp.Id].MorphCount) + cTier * _layers[sp.Id].MorphCount;
                float leafSpacing = (float)Math.Max(0.11, sp.Climber!.SegmentLength * 3.0);
                var lastLeaf = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

                // A node with no child is the growing tip of its shoot.
                bool[] hasChild = new bool[segs.Count];
                for (int s = 0; s < segs.Count; s++)
                {
                    int p = segs[s].ParentIndex;
                    if (p >= 0 && p < segs.Count) hasChild[p] = true;
                }

                for (int s = 0; s < segs.Count; s++)
                {
                    var seg = segs[s];
                    if (seg.Senescent) continue; // Leaves shed / withered during senescence!

                    var nodePos = new Vector3((float)seg.Position.X, (float)seg.Position.Y, (float)seg.Position.Z);
                    var fwd = new Vector3((float)seg.Forward.X, (float)seg.Forward.Y, (float)seg.Forward.Z);
                    var nrm = new Vector3((float)seg.Normal.X, (float)seg.Normal.Y, (float)seg.Normal.Z);
                    if (fwd.LengthSquared() < 1e-6f) fwd = Vector3.Up; else fwd = fwd.Normalized();
                    if (nrm.LengthSquared() < 1e-6f) nrm = Vector3.Forward; else nrm = nrm.Normalized();
                    // The foliage always sits on the upper side of the stem, whatever the stem is doing: a shoot
                    // spiralling round a trunk still carries its leaves on top of it.
                    ClimberGrowth.StemFrame(new Vec3(fwd.X, fwd.Y, fwd.Z), new Vec3(nrm.X, nrm.Y, nrm.Z), out var sideV, out var upV);
                    var side = new Vector3((float)sideV.X, (float)sideV.Y, (float)sideV.Z);
                    var up = new Vector3((float)upV.X, (float)upV.Y, (float)upV.Z);
                    if (seg.Attached && IsExpansionClimber(sp.Shape))
                    {
                        var outward = nrm - fwd * nrm.Dot(fwd);
                        if (outward.LengthSquared() > 1e-6f)
                        {
                            up = outward.Normalized();
                            side = fwd.Cross(up).Normalized();
                        }
                    }
                    // Mesh X = side (across the stem), Mesh Y = fwd (along the stem), Mesh Z = up (upper side)
                    var nodeBasis = new Basis(side, fwd, up);

                    bool growingTip = !hasChild[s];
                    if (!growingTip && nodePos.DistanceTo(lastLeaf) < leafSpacing) continue;
                    if (!growingTip) lastLeaf = nodePos;

                    var nodeT = new Transform3D(nodeBasis, nodePos);
                    var nodeBd = growingTip
                        ? Get(_tendrils, MorphKey(sp.Id, cVariant))
                        : Get(seg.Attached ? _full : _juvenile, MorphKey(sp.Id, cVariant));
                    nodeBd.T.Add(nodeT);
                    nodeBd.Tint.Add(cTint);
                    nodeBd.C.Add(new Color((hash % 1000) / 1000f, (float)f.Health, 0, ((hash >> 12) % 1000) / 1000f));
                    visible++;
                }
                continue;
            }
            if (sp.Climber != null && !f.ClimberAttached)
            {
                h = Math.Min(h, 0.035);
                r = Math.Max(r, 0.06);
            }
            if (sp.Shape == "rain_jelly")
            {
                // Jelly fungi change silhouette within minutes of wetting; keep that as render-time turgor
                // rather than manufacturing/losing authoritative biomass every weather tick.
                double jellyMoisture = placement.LocalMoisture;
                double swell = MathD.Clamp01((jellyMoisture - 0.24) / 0.62);
                r *= 0.72 + 0.58 * swell;
                h *= 0.52 + 1.05 * swell;
            }
            // Siltstars are authored in an 8 cm low-carpet frame rather than unit height.
            // Their declared 12 mm height must remain 12 mm, not be multiplied down again.
            if(sp.Id=="siltstars")h/=.08;
            var t = new Transform3D(yawBasis.ScaledLocal(new Vector3((float)r, (float)h, (float)r)), pos);
            if (sp.Shape == "bracket" && Anchor(new Vector2(pos.X, pos.Z), 0.35) is { } ba)
            {
                // shelves grow out of the trunk's side
                var at = ba.Surface + new Vector3(0, (float)(((hash >> 20) % 100) / 100.0 - 0.5) * 0.08f, 0);
                t = new Transform3D(Bridge.Yaw(ba.Outward).ScaledLocal(new Vector3((float)r, (float)(h * 2.5), (float)r)), at - new Vector3(0, (float)h, 0));
            }
            else if (sp.Shape == "glass_antlers" && Anchor(new Vector2(pos.X, pos.Z), 0.22, new HashSet<string>(StringComparer.Ordinal) { "log" }) is { } ga)
            {
                // Glass Antlers are authored upright from their attachment foot. Plant them directly on the
                // cylindrical log surface, with a slight outward yaw so branches don't disappear into the wood.
                var at = ga.Surface + new Vector3(0, (float)(((hash >> 20) % 100) / 100.0 - 0.5) * 0.05f, 0);
                t = new Transform3D(Bridge.Yaw(ga.Outward).ScaledLocal(new Vector3((float)r, (float)(h * 1.35), (float)r)), at);
            }
            float wobble = 0;
            if (_wobbleStart.TryGetValue(f.Id, out var ws)) wobble = (float)Math.Max(0, 1 - (_clock - ws) / 1.2);
            var custom = new Color((hash % 1000) / 1000f, (float)f.Health, wobble, ((hash >> 12) % 1000) / 1000f);
            var tint = f.Tint;
            int variant = (int)((hash >> 8) % (ulong)visualLayer.MorphCount);
            int lodTier = VisualTier(f.Id, pos, (float)h, (float)r);
            variant += lodTier * visualLayer.MorphCount;
            var vl = _layers[sp.Id].Variants[variant];
            bool reproductiveFruit = sp.Reproduction != null
                && f.FruitLoad > sp.Reproduction.MaxAttachedMass * 0.03
                && f.ReproductiveStage is PlantReproductiveStage.Developing or PlantReproductiveStage.Ripe;
            var bucket = vl.Juvenile != null && (f.Stage(sp) == FloraStage.Juvenile || (sp.Climber != null && !f.ClimberAttached)) ? _juvenile
                : (f.Fruiting || reproductiveFruit) && vl.Fruit != null ? _fruit : _full;
            var bd = Get(bucket, MorphKey(sp.Id, variant)); bd.T.Add(t); bd.Tint.Add(tint); bd.C.Add(custom);
            visible++;
        }
        // Dead vascular plants remain visible after leaving the living population. Reuse the species mesh and
        // drive pose/tint from authoritative corpse state so no per-corpse mesh or physics body is required.
        foreach (var dead in _deadSnapshot)
        {
            yield return false;
            if (_w.DeadFlora.Get(dead.Id) == null) continue;
            var sp = _w.Content.FloraOrThrow(dead.SpeciesId);
            if (!_layers.ContainsKey(sp.Id)) continue;
            double remain = Math.Sqrt(dead.RemainingFraction);
            double r = dead.OriginalRadius * (0.82 + 0.18 * remain);
            double h = dead.OriginalHeight * (0.78 + 0.22 * remain);
            var pos = new Vector3((float)dead.X, (float)_w.GroundHeight(dead.Position), (float)dead.Z);
            if (frustum != null && !FloraVisible(frustum, pos, (float)r, (float)h)) continue;

            bool isUpright = IsUpright(sp);

            Basis basis;
            if (!isUpright)
            {
                var norm = (sp.Shape is "floatleaf" or "glasslily_pad")
                    ? Vector3.Up
                    : SurfaceFrame.SurfaceNormal(_w, pos.X, pos.Z, Math.Clamp(r * 0.5, 0.04, 0.22));
                basis = SurfaceFrame.TiltTo(norm) * new Basis(Vector3.Up, (float)dead.CollapseHeading);
                pos += norm * 0.003f;
            }
            else
            {
                float lean = dead.Stage switch
                {
                    DeadPlantStage.StandingDead => 0.05f + 0.10f * (float)dead.StageProgress,
                    DeadPlantStage.Collapsing => Mathf.Lerp(0.15f, 1.38f, (float)dead.StageProgress),
                    DeadPlantStage.Fallen => 1.42f,
                    _ => 1.47f,
                };
                basis = new Basis(Vector3.Up, (float)dead.CollapseHeading) * new Basis(Vector3.Forward, lean);
            }
            if (dead.Stage >= DeadPlantStage.Fallen) h *= 0.92;
            var t = new Transform3D(basis.ScaledLocal(new Vector3((float)r, (float)h, (float)r)), pos);

            int cell = _w.Grid.NearestDomainCell(dead.Position);
            double moisture = cell >= 0 ? _w.Fields.Moisture.Values[cell] : 0.5;
            double litterMass = cell >= 0 ? _w.Litter.FineMass[cell] + _w.Litter.CoarseMass[cell] : 0;
            double stageFade = dead.Stage switch
            {
                DeadPlantStage.StandingDead => 0.20 + 0.20 * dead.StageProgress,
                DeadPlantStage.Collapsing => 0.42 + 0.16 * dead.StageProgress,
                DeadPlantStage.Fallen => 0.60 + 0.20 * dead.StageProgress,
                _ => 0.82 + 0.18 * dead.StageProgress,
            };
            double litterBlend = MathD.Clamp01(stageFade * (0.7 + Math.Min(0.3, litterMass * 0.08)));
            var localLitter = moisture > 0.62
                ? new Color(0.47f, 0.42f, 0.31f, 1f)
                : moisture < 0.28 ? new Color(0.72f, 0.64f, 0.46f, 1f)
                : new Color(0.60f, 0.53f, 0.36f, 1f);
            var post = sp.PostLife;
            var deadColor = post != null
                ? new Color((float)post.DeadColor[0], (float)post.DeadColor[1], (float)post.DeadColor[2], 1f)
                : new Color(0.62f, 0.54f, 0.38f, 1f);
            if (post != null)
            {
                float dry = (float)(MathD.Clamp01((0.36 - moisture) / 0.36) * post.DryBleach);
                float wet = (float)(MathD.Clamp01((moisture - 0.58) / 0.42) * post.WetDarken);
                deadColor = deadColor.Lerp(Colors.White, dry).Darkened(wet);
            }
            float litterInfluence = (float)(post?.LitterColorInfluence ?? 0.7);
            var tint = deadColor.Lerp(localLitter, (float)(litterBlend * litterInfluence));
            ulong hash = Rng.Mix(dead.Id.Value, 0xD34DUL);
            // Freshly killed standing plants keep chlorophyll for a while: leaf pigment drains over the
            // standing-dead stage instead of snapping straight to straw (shader fades by inst.y).
            float residualGreen = dead.Stage == DeadPlantStage.StandingDead ? 0.34f * (float)(1.0 - dead.StageProgress) : 0f;
            var custom = new Color((hash % 1000) / 1000f, residualGreen, 0f, ((hash >> 12) % 1000) / 1000f);
            var visualLayer = _layers[sp.Id];
            int variant = (int)((hash >> 8) % (ulong)visualLayer.MorphCount);
            int deadTier = VisualTier(dead.Id, pos, (float)h, (float)r);
            variant += deadTier * visualLayer.MorphCount;
            var bd = Get(_full, MorphKey(sp.Id, variant));
            bd.T.Add(t); bd.Tint.Add(tint); bd.C.Add(custom);
            visible++;
        }

        // slime-mold veins: thickness follows the biomass flowing through each link. The main pass
        // grouped plants by species, so a layer walks only its own individuals instead of rescanning
        // the whole population once per vein layer.
        foreach (var (id, layer) in _layers)
        {
            if (layer.Veins == null) continue;
            var list = Get(_veins, id); list.T.Clear(); list.Tint.Clear(); list.C.Clear();
            var vsp = _w.Content.FloraOrThrow(id);
            var veinPlants = _veinPlants.TryGetValue(id, out var speciesPlants) ? speciesPlants : null;
            if (veinPlants != null)
            foreach (var f in veinPlants)
            {
                yield return false;
                if (_w.Flora.Get(f.Id) == null) continue;

                // Multi-segment climber stems:
                if (vsp.Climber != null && f.ClimberSegments is { Count: > 1 } segs)
                {
                    if (_renderClimberSegments.TryGetValue(f.Id, out var projected)) segs = projected;
                    float shootRadiusScale = IsExpansionClimber(vsp.Shape) ? 0.70f : 1.0f;
                    for (int s = 0; s < segs.Count; s++)
                    {
                        var seg = segs[s];
                        if (seg.ParentIndex < 0 || seg.ParentIndex >= segs.Count) continue;
                        var pSeg = segs[seg.ParentIndex];
                        var pA = new Vector3((float)pSeg.Position.X, (float)pSeg.Position.Y, (float)pSeg.Position.Z);
                        var pB = new Vector3((float)seg.Position.X, (float)seg.Position.Y, (float)seg.Position.Z);
                        if ((pB - pA).Length() < 1e-4f) continue;
                        float cFrac = (float)Math.Sqrt(f.BiomassFraction(vsp));
                        // A climber's stem carries its own radius: it tapers from the base of the plant to the
                        // growing tip and thickens as the plant ages.
                        float cThick = (float)pSeg.Radius * (seg.Senescent ? 0.6f : 1.0f);
                        if (cThick <= 0f) cThick = (0.0028f + 0.0048f * cFrac) * (seg.Senescent ? 0.6f : 1.0f);
                        cThick *= shootRadiusScale;
                        var norm = new Vector3((float)seg.Normal.X, (float)seg.Normal.Y, (float)seg.Normal.Z);
                        if (norm.LengthSquared() < 1e-6f) norm = Vector3.Up; else norm = norm.Normalized();
                        // Attached woody stems are subdivided and each point re-projected onto the host bark so the
                        // tube follows the trunk instead of cutting a straight chord. Runs at mesh rebuild only.
                        int pieces = 1;
                        if (seg.Attached && pSeg.Attached && seg.Support == ClimberSurfaceKind.Woody && !seg.HostId.IsNone)
                            pieces = Math.Clamp((int)Math.Ceiling((pB - pA).Length() / 0.02f), 1, 6);
                        var prev = pA;
                        for (int k = 1; k <= pieces; k++)
                        {
                            Vector3 cur = pB;
                            if (k < pieces) cur = ProjectStemPoint(pSeg, seg, (float)k / pieces, pA.Lerp(pB, (float)k / pieces));
                            var segD = cur - prev;
                            if (segD.Length() < 1e-5f) { prev = cur; continue; }
                            var cXAxis = segD;
                            var cZAxis = cXAxis.Cross(norm);
                            if (cZAxis.LengthSquared() < 1e-6f) cZAxis = cXAxis.Cross(Vector3.Up);
                            cZAxis = cZAxis.Normalized() * cThick;
                            var cYAxis = cZAxis.Cross(cXAxis).Normalized() * cThick;
                            list.T.Add(new Transform3D(new Basis(cXAxis, cYAxis, cZAxis), prev));
                            prev = cur;
                            list.Tint.Add(f.Tint);
                            ulong sHash = Rng.Mix(f.Id.Value, (ulong)s);
                            list.C.Add(new Color((sHash % 1000) / 1000f, (float)(seg.Senescent ? f.Health * 0.4 : f.Health), 0, ((sHash >> 12) % 1000) / 1000f));
                        }
                    }
                    continue;
                }

                if (f.ParentId.IsNone || (CurrentSnapshot?.TryGetFlora(f.ParentId, out var parent) != true && (_w.Flora.Get(f.ParentId) is not { } wParent || (parent = new FloraSnapshotItem(wParent)).Id.IsNone))) continue;
                var a = new Vector3((float)parent.X, CachedGroundY(parent) + 0.004f, (float)parent.Z);
                var b = new Vector3((float)f.X, CachedGroundY(f) + 0.004f, (float)f.Z);
                var d = b - a;
                if (d.Length() < 1e-4f || d.Length() > 0.6f) continue;
                float frac = (float)Math.Sqrt(Math.Min(f.BiomassFraction(vsp), parent.BiomassFraction(vsp)));
                float thick = vsp.Climber != null ? 0.0025f + 0.0045f * frac : 0.004f + 0.009f * frac;
                var xAxis = d; var zAxis = xAxis.Cross(Vector3.Up).Normalized() * thick; var yAxis = zAxis.Cross(xAxis).Normalized() * thick * 0.45f;
                list.T.Add(new Transform3D(new Basis(xAxis, yAxis, zAxis), a));
                list.Tint.Add(f.Tint);
                ulong hash = Rng.Mix(f.Id.Value, 0xF10);
                list.C.Add(new Color((hash % 1000) / 1000f, (float)Math.Min(f.Health, parent.Health), 0, ((hash >> 12) % 1000) / 1000f));
            }
            yield return true;
            Fill(layer.Veins.Multimesh, list);
            triangles += (long)PublishedCount(layer.Veins.Multimesh) * layer.VeinTris;
        }
        foreach (var (id, layer) in _layers)
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                yield return true;
                var vl = layer.Variants[v];
                var mk = MorphKey(id, v);
                // Full and fruit share one upload step: a phase change never briefly draws both forms.
                FillBucket(vl.Full.Multimesh, _full, mk);
                if (vl.Fruit != null) FillBucket(vl.Fruit.Multimesh, _fruit, mk);
                if (vl.Juvenile != null) FillBucket(vl.Juvenile.Multimesh, _juvenile, mk);
                if (vl.Tendril != null) FillBucket(vl.Tendril.Multimesh, _tendrils, mk);
                triangles += (long)PublishedCount(vl.Full.Multimesh) * vl.FullTris
                    + (vl.Fruit != null ? (long)PublishedCount(vl.Fruit.Multimesh) * vl.FruitTris : 0)
                    + (vl.Juvenile != null ? (long)PublishedCount(vl.Juvenile.Multimesh) * vl.JuvenileTris : 0)
                    + (vl.Tendril != null ? (long)PublishedCount(vl.Tendril.Multimesh) * vl.TendrilTris : 0);
            }
        Visible_ = visible; TrianglesDrawn = triangles;
        _publishedPlants.Clear();
        foreach (var plant in _snapshot)
            if (_layers.ContainsKey(plant.SpeciesId)) _publishedPlants.Add(plant.Id);
        PopulationReady = _build == null &&
            _w.Flora.Items.All(f => _layers.ContainsKey(f.SpeciesId)) &&
            _w.DeadFlora.Items.All(f => _layers.ContainsKey(f.SpeciesId));
        foreach (var id in _visualTiers.Keys.Where(id => _w.Flora.Get(id) == null && _w.DeadFlora.Get(id) == null).ToArray()) _visualTiers.Remove(id);
        foreach (var id in _distanceTiers.Keys.Where(id => _w.Flora.Get(id) == null && _w.DeadFlora.Get(id) == null).ToArray()) _distanceTiers.Remove(id);
        UpdateLuminousLights();
    }

    /// <summary>
    /// Samples everything about a plant that only changes with the substrate, light field or moisture
    /// field: the rock/log ground scan, the local surface normal, the upright tilt, and the two field
    /// values. Camera-only refreshes reuse the result.
    /// </summary>
    private PlantPlacement SamplePlantPlacement(FloraSnapshotItem f, FloraSpeciesDef sp,
        int terrainVersion, int propsVersion, long tick)
    {
        var position = f.Position;
        double radius = f.Radius(sp);
        double height = sp.Colony != null
            ? sp.Colony.MaxHeight * (0.15 + 0.85 * f.HeightFactor)
            : sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
        var normal = Vector3.Up;
        var uprightBasis = Basis.Identity;
        if (IsUpright(sp))
        {
            // The shared placement routine keeps rendered plants and animal supports identical.
            var growth = FloraGeometryPlacement.Upright(_w, f.Id.Value, position, f.Health, radius, height);
            uprightBasis = SurfaceFrame.TiltTo(Bridge.V(growth.Up));
        }
        else if (sp.Shape != "vine" && sp.Shape != "floatleaf" && sp.Shape != "glasslily_pad")
        {
            normal = SurfaceFrame.SurfaceNormal(_w, (float)f.X, (float)f.Z,
                Math.Clamp(radius * 0.5, 0.04, 0.22));
        }
        double groundY = _w.GroundHeight(position);
        if (sp.Shape == "cradlecup" && _w.Props.LogAt(position) is { } log
            && Math.Abs(log.TopAt(position) - groundY) < 0.002
            && VisualLogSupport.TryTopAt(log, position, out double barkY))
        {
            groundY = Math.Max(_w.Terrain.Height(position), barkY);
        }
        return new PlantPlacement
        {
            Position = position,
            TerrainVersion = terrainVersion,
            PropsVersion = propsVersion,
            SampledTick = tick,
            GroundY = (float)groundY,
            SurfaceNormal = normal,
            UprightBasis = uprightBasis,
            Def = sp,
            LocalLight = (float)_w.Fields.Light.Sample(position),
            LocalMoisture = (float)_w.Fields.Moisture.Sample(position),
        };
    }

    private float CachedGroundY(FloraSnapshotItem item) =>
        _placements.TryGetValue(item.Id, out var placement)
            ? placement.GroundY
            : (float)_w.GroundHeight(item.Position);

    /// <summary>
    /// The simulation's woody support envelope is intentionally conservative and can sit inside the
    /// rendered bole. For the three natural climbers, move attached woody nodes just proud of that
    /// envelope for presentation only; both foliage and stem links consume this same copied array.
    /// </summary>
    private IReadOnlyList<ClimberSegmentSnapshot> ProjectWoodyClimberSegments(FloraSpeciesDef vineSpecies,
        IReadOnlyList<ClimberSegmentSnapshot> source)
    {
        if (vineSpecies.Climber == null) return source;
        ClimberSegmentSnapshot[]? corrected = null;
        for (int i = 0; i < source.Count; i++)
        {
            var segment = source[i];
            if (!segment.Attached || segment.Support != ClimberSurfaceKind.Woody || segment.HostId.IsNone)
                continue;
            var host = CurrentSnapshot != null && CurrentSnapshot.TryGetFlora(segment.HostId, out var snapshotHost)
                ? snapshotHost : default;
            if (host.Id.IsNone) continue;
            var hostSpecies = _w.Content.FloraOrThrow(host.SpeciesId);
            if (hostSpecies.Woody == null) continue;

            if (!TryProjectClimberNodeToHost(host, hostSpecies, segment, out var projectedPosition, out var projectedNormal)) continue;
            corrected ??= CopySegments(source);
            corrected[i] = new ClimberSegmentSnapshot(
                new Vec3(projectedPosition.X, projectedPosition.Y, projectedPosition.Z),
                new Vec3(projectedNormal.X, projectedNormal.Y, projectedNormal.Z), segment.Forward, segment.ParentIndex,
                segment.Attached, segment.Senescent, segment.Radius, segment.Support, segment.HostId);
        }
        return corrected ?? source;
    }

    private Vector3 ProjectStemPoint(ClimberSegmentSnapshot a, ClimberSegmentSnapshot b, float t, Vector3 fallback)
    {
        var host = CurrentSnapshot != null && CurrentSnapshot.TryGetFlora(b.HostId, out var h) ? h : default;
        if (host.Id.IsNone) return fallback;
        var hostSpecies = _w.Content.FloraOrThrow(host.SpeciesId);
        if (hostSpecies.Woody == null) return fallback;
        var n = new Vec3(a.Normal.X + (b.Normal.X - a.Normal.X) * t, a.Normal.Y + (b.Normal.Y - a.Normal.Y) * t, a.Normal.Z + (b.Normal.Z - a.Normal.Z) * t);
        var synth = new ClimberSegmentSnapshot(new Vec3(fallback.X, fallback.Y, fallback.Z), n, b.Forward, b.ParentIndex,
            true, b.Senescent, b.Radius, b.Support, b.HostId);
        return TryProjectClimberNodeToHost(host, hostSpecies, synth, out var pos, out _) ? pos : fallback;
    }

    private static ClimberSegmentSnapshot[] CopySegments(IReadOnlyList<ClimberSegmentSnapshot> source)
    {
        var copy = new ClimberSegmentSnapshot[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return copy;
    }

    private void UpdateLuminousLights()
    {
        using var lightsProfile=FrameProfiler.Measure("Flora.Lights");
        if(Camera==null)return;
        var lightTimer=System.Diagnostics.Stopwatch.GetTimestamp();
        var planes=Camera.GetFrustum();var frustum=new Plane[planes.Count];
        for(int i=0;i<planes.Count;i++)frustum[i]=planes[i];
        // In daylight every one of these lights is invisible, so moving them and
        // re-energising them is work nobody can see. The state is refreshed when
        // night falls.
        bool night=NightFloraMaterials.CurrentNightFactor>0.00001f;
        var camPos=Camera.GlobalPosition;
        _lightCandidates.Clear();
        if(night)
        {
            foreach(var f in _snapshot)
            {
                var sp=_w.Content.FloraOrThrow(f.SpeciesId);
                if(sp.BioluminescenceStrength<=0||sp.BioluminescenceColor is not {Length:>=3}||_w.Flora.Get(f.Id)==null)continue;
                var pos=new Vector3((float)f.X,(float)_w.GroundHeight(f.Position),(float)f.Z);
                float radius=(float)f.Radius(sp),height=(float)(sp.Height*(.45+.55*Math.Sqrt(f.BiomassFraction(sp))));
                if(!FloraVisible(frustum,pos,radius,height))continue;
                float tissueHeight=sp.Id switch{"ghostbonnet"=>.56f,"lanternsedge"=>.78f,"siltstars"=>.12f,"tidebell"=>.22f,_=>.3f};
                pos.Y+=Math.Max(.018f,height*tissueHeight);
                float size=(float)Math.Clamp(radius/Math.Max(sp.RadiusAtMax,.01),.3,1);
                float range=NightFloraMaterials.LightRange(sp)*MathF.Sqrt(size);
                float energy=NightFloraMaterials.LightEnergy(sp)*size;
                // Rank by emitted power and distance: the nearest, strongest emitters survive the budget.
                _lightCandidates.Add((f.Id,sp,pos,range,energy,energy/(1f+camPos.DistanceSquaredTo(pos))));
            }
        }
        int activeLimit=Math.Min(_lightCandidates.Count,MaxActiveLuminousLights);
        if(_lightCandidates.Count>activeLimit)
            _lightCandidates.Sort(static (a,b)=>b.Score.CompareTo(a.Score));
        _activeLuminous.Clear();
        _selectedLuminous.Clear();
        for(int i=0;i<activeLimit;i++)
        {
            var c=_lightCandidates[i];
            _selectedLuminous.Add(c.Id);
            if(!_luminousLights.TryGetValue(c.Id,out var light))
            {
                if(_luminousPool.Count>0)
                {
                    int last=_luminousPool.Count-1;
                    light=_luminousPool[last];
                    _luminousPool.RemoveAt(last);
                    light.Name="Glow_"+c.Id.Value;
                }
                else
                {
                    light=NightFloraMaterials.CreateLightForSpecies(c.Species,c.Position);
                    light.Name="Glow_"+c.Id.Value;
                    AddChild(light);
                }
                _luminousLights.Add(c.Id,light);
                _shadowDirty=true;
            }
            // A pooled node may have served another species: reassert every species-dependent value.
            if(c.Species.BioluminescenceColor is {Length:>=3} color)
                light.LightColor=new Color((float)color[0],(float)color[1],(float)color[2]);
            light.Position=c.Position;
            light.OmniRange=c.Range;
            light.OmniAttenuation=1.7f;
            // Fade these overlapping per-pixel passes out before they blend into one flat wash.
            light.DistanceFadeEnabled=true;
            light.DistanceFadeBegin=6.5f;
            light.DistanceFadeLength=2.5f;
            // Read back the native values once after setters, preserving any
            // engine clamping rather than assuming the authored values survived.
            _activeLuminous.Add((light,c.Energy,light.Position,light.OmniRange));
        }
        if(night)
        {
            _luminousToPool.Clear();
            foreach(var (id,_) in _luminousLights)
                if(!_selectedLuminous.Contains(id))_luminousToPool.Add(id);
            foreach(var id in _luminousToPool)
            {
                var light=_luminousLights[id];
                _luminousLights.Remove(id);
                NightFloraMaterials.RegisterLight(light,0);
                light.Visible=false;
                light.LightEnergy=0;
                light.ShadowEnabled=false;
                _luminousPool.Add(light);
            }
            _luminousToPool.Clear();
        }
        var active=_activeLuminous;
        // Shadow budget: only the few brightest/nearest eligible lights cast shadows,
        // re-selected at a throttled rate (or immediately when a light was created).
        if(_shadowDirty||System.Diagnostics.Stopwatch.GetElapsedTime(_shadowSelectedAt).TotalSeconds>=ShadowReselectSeconds)
        {
            _shadowSelectedAt=System.Diagnostics.Stopwatch.GetTimestamp();_shadowDirty=false;
            var shadowCamPos=Camera.GlobalPosition;
            var ranked=new List<(float Score,OmniLight3D Light)>();
            foreach(var a in active)
            {
                if(!NightFloraMaterials.CanCastShadow(a.Range,a.Energy)){a.Light.ShadowEnabled=false;continue;}
                ranked.Add((a.Energy/(1f+shadowCamPos.DistanceSquaredTo(a.Position)),a.Light));
            }
            ranked.Sort((x,y)=>y.Score.CompareTo(x.Score));
            for(int i=0;i<ranked.Count;i++)
            {
                bool want=i<MaxShadowCastingLights;
                if(ranked[i].Light.ShadowEnabled!=want)ranked[i].Light.ShadowEnabled=want;
            }
        }
        // Nearby organisms act as a broad, gentle emitter. Every light stays on; coincident colonies
        // cannot multiply a subtle glow into an overexposed fire. Hash only source positions, not light.
        bool audit=active.Count>0 && LuminousLightCacheAuditRefreshes<2 &&
            Array.IndexOf(Main.UserArgs,"--flora-light-cache-audit")>=0;
        var neighbourhood=new Dictionary<(int,int,int),List<int>>();
        // Flat copies of the light state for the pair loop. The overlap runs
        // over millions of pairs in a mature world, and copying the light
        // record for each pair cost more than the arithmetic did.
        var positions=new Vector3[active.Count];var rangeSquares=new float[active.Count];
        // The widest light in each cell. A cell whose nearest point is already
        // further away than that cannot hold a light within reach, so the cell
        // is skipped whole instead of light by light.
        var cellReach=new Dictionary<(int,int,int),float>();
        for(int i=0;i<active.Count;i++)
        {var p=active[i].Position;var key=((int)MathF.Floor(p.X),(int)MathF.Floor(p.Y),(int)MathF.Floor(p.Z));
           positions[i]=p;float r=active[i].Range;rangeSquares[i]=r*r;
           cellReach.TryGetValue(key,out float widest);
           if(r>widest)cellReach[key]=r;
            if(audit)
            {
                var native=active[i].Light.Position;
                var nativeKey=((int)MathF.Floor(native.X),(int)MathF.Floor(native.Y),(int)MathF.Floor(native.Z));
                if(key!=nativeKey || BitConverter.SingleToInt32Bits(p.X)!=BitConverter.SingleToInt32Bits(native.X) ||
                    BitConverter.SingleToInt32Bits(p.Y)!=BitConverter.SingleToInt32Bits(native.Y) ||
                    BitConverter.SingleToInt32Bits(p.Z)!=BitConverter.SingleToInt32Bits(native.Z) ||
                    BitConverter.SingleToInt32Bits(active[i].Range)!=BitConverter.SingleToInt32Bits(active[i].Light.OmniRange))
                    throw new InvalidOperationException("Flora native light cache/bin mismatch");
            }
            if(!neighbourhood.TryGetValue(key,out var bucket))neighbourhood[key]=bucket=new();bucket.Add(i);}
        for(int i=0;i<active.Count;i++)
        {
            var a=active[i];var p=a.Position;float overlap=1;
            int x=(int)MathF.Floor(p.X),y=(int)MathF.Floor(p.Y),z=(int)MathF.Floor(p.Z);
            for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {if(!neighbourhood.TryGetValue((x+dx,y+dy,z+dz),out var bucket))continue;
                if(audit)LuminousOverlapCellsVisited++;
                if(cellReach.TryGetValue((x+dx,y+dy,z+dz),out float widest))
                {
                    // The nearest point of this cell to the light.
                    float mx=MathF.Max(MathF.Max(x+dx-p.X,p.X-(x+dx+1)),0);
                    float my=MathF.Max(MathF.Max(y+dy-p.Y,p.Y-(y+dy+1)),0);
                    float mz=MathF.Max(MathF.Max(z+dz-p.Z,p.Z-(z+dz+1)),0);
                    if(mx*mx+my*my+mz*mz>=widest*widest){if(audit)LuminousOverlapCellsSkipped++;continue;}
                }
                foreach(int j in bucket){if(i==j)continue;
                    if(audit)LuminousOverlapPairsExamined++;
                    float d2=p.DistanceSquaredTo(positions[j]);
                    // A neighbour further away than its own reach contributes
                    // nothing, and the division is the expensive part.
                    if(d2>=rangeSquares[j])continue;
                    float q=1-d2/rangeSquares[j];overlap+=q*q;}}
            if(audit)
            {
                // Original native-read overlap, same bins/traversal/arithmetic.
                // RegisterLight changes energy/visibility, never position/range.
                var nativePosition=a.Light.Position;float reference=1;
                int nx=(int)MathF.Floor(nativePosition.X),ny=(int)MathF.Floor(nativePosition.Y),nz=(int)MathF.Floor(nativePosition.Z);
                for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {if(!neighbourhood.TryGetValue((nx+dx,ny+dy,nz+dz),out var bucket))continue;
                    foreach(int j in bucket){if(i==j)continue;var other=active[j].Light;
                        float q=1-nativePosition.DistanceSquaredTo(other.Position)/(other.OmniRange*other.OmniRange);
                        if(q>0)reference+=q*q;LuminousLightCacheAuditPairs++;}}
                MaximumLuminousLightCacheAuditError=Math.Max(MaximumLuminousLightCacheAuditError,Math.Abs(overlap-reference));
                if(BitConverter.SingleToInt32Bits(overlap)!=BitConverter.SingleToInt32Bits(reference))
                    throw new InvalidOperationException("Flora native light overlap cache mismatch");
                LuminousLightCacheAuditLights++;
            }
            NightFloraMaterials.RegisterLight(a.Light,a.Energy/overlap);
        }
        if(audit)LuminousLightCacheAuditRefreshes++;
        LuminousOverlapRefreshes++;
        LuminousOverlapTotalMs+=System.Diagnostics.Stopwatch.GetElapsedTime(lightTimer).TotalMilliseconds;
        LuminousLightSnapshotCount=_snapshot.Count;
        LuminousLightActiveCount=active.Count;
        LuminousNightFactor=NightFloraMaterials.CurrentNightFactor;
    }

    /// <summary>Geometric tier (0 near .. 2 far) from the projected bounding-sphere diameter, with hysteresis.
    /// Never skips an individual for screen size: the far tier is still the full parametric plant.</summary>
    private int VisualTier(EntityId id, Vector3 position, float height, float radius)
    {
        if (Camera == null) return 0;
        float bound = 2f * MathF.Sqrt(radius * radius + height * height * .25f);
        // Keep the physical interaction neighbourhood on the authored mesh, even in a distant view.
        // The larger exit radius prevents detail flicker as the guardian moves along a plant edge.
        // Only the plants around a guardian that is itself close to the camera must
        // stay on the authored mesh. A guardian across the island no longer locks
        // every plant near it to Tier 0 while the camera is far away.
        if (_w.Guardian is { } guardian
            && _detailCameraPosition.DistanceSquaredTo(_detailGuardianPosition) <= 12.25f)
        {
            float reach = (_visualTiers.GetValueOrDefault(id, -1) == 0 ? 1.3f : 1f) + bound * .5f;
            if ((position + Vector3.Up * height * .5f).DistanceSquaredTo(Bridge.V(guardian.Position)) <= reach * reach)
            {
                _visualTiers[id] = 0;
                return 0;
            }
        }
        Vector3 toCam = position + Vector3.Up * height * .5f - _detailCameraPosition;
        float forwardDepth = toCam.Dot(_detailCameraForward);
        float dist = toCam.Length();
        float depth = forwardDepth > 0.05f ? forwardDepth : Math.Max(dist, 0.05f);
        float pixels = _detailOrthogonal ? bound * _detailPixels / _detailOrthoSize : bound * _detailPixels / depth;
        bool had = _visualTiers.TryGetValue(id, out int old);
        int tier = FloraDetail.Select(pixels, had && old >= 0 ? old : -1);
        if (ReducedDistantPlants && !_detailOrthogonal)
        {
            int previous = _distanceTiers.GetValueOrDefault(id, -1);
            int distanceTier = FloraDetail.SelectDistance(depth, previous, DistanceLodScale);
            _distanceTiers[id] = distanceTier;
            tier = Math.Max(tier, distanceTier);
        }
        _visualTiers[id] = tier;
        return tier;
    }

    // World-space slack added around the bounding sphere before the frustum test. The instance list is only
    // refreshed after camera motion every 0.2s (see _Process), so a plant that is off-frustum now but would rotate into view before
    // the next rebuild must still test as visible; this margin absorbs a normal camera turn across that window.
    private const float FrustumSafetyMargin = 2.0f;
    private const float NearPlaneSlack = 0.15f;

    private bool FloraVisible(Plane[] frustum, Vector3 basePos, float radius, float height)
    {
        // Occlusion is intentionally never tested here: a plant that is only partly behind a log/rock must never
        // be culled, since any ray-based "opaque in front" test flips on/off with sub-pixel camera motion and
        // reads as pop-in/out. Frustum membership is the only rejection criterion, tested against an inflated
        // bounding sphere so genuinely off-screen plants are still dropped.
        float mid = Math.Max(height * 0.5f, 0.01f);
        var center = basePos + Vector3.Up * mid;
        float plantRadius = Math.Max(radius, mid);
        // Godot frustum planes face outward: inside points have negative distance, outside positive.
        foreach (var plane in frustum)
        {
            float margin = plane.Normal.Dot(_detailCameraForward) < -0.7f ? NearPlaneSlack : FrustumSafetyMargin;
            if (plane.DistanceTo(center) > plantRadius + margin) return false;
        }
        return true;
    }

    private static void ClearBuckets<TKey>(Dictionary<TKey, (List<Transform3D> T, List<Color> Tint, List<Color> C)> buckets) where TKey : notnull
    {
        foreach (var b in buckets.Values) { b.T.Clear(); b.Tint.Clear(); b.C.Clear(); }
    }

    private void FillBucket<TKey>(MultiMesh mesh, Dictionary<TKey, (List<Transform3D> T, List<Color> Tint, List<Color> C)> buckets, TKey key) where TKey : notnull
    {
        // Entries remain after clearing, so a previously visible bucket still submits its zero count.
        // A missing entry has never been published and needs no lists or pooled upload buffer.
        if (buckets.TryGetValue(key, out var data)) Fill(mesh, data);
    }

    private static (List<Transform3D> T, List<Color> Tint, List<Color> C) Get<TKey>(Dictionary<TKey, (List<Transform3D>, List<Color>, List<Color>)> d, TKey k) where TKey : notnull
    {
        if (!d.TryGetValue(k, out var v)) d[k] = v = (new List<Transform3D>(), new List<Color>(), new List<Color>());
        return v;
    }

    /// <summary>Uploads all instances in one packed buffer (12 transform + 4 instance-colour tint + 4 custom floats
    /// each) instead of two engine calls per instance, using pooled capacity-cached buffers to avoid reallocating
    /// MultiMesh instance memory on every count change.</summary>
    private sealed class PooledMultiMeshBuffer
    {
        public float[] Buffer = Array.Empty<float>();
        private float[] _uploaded = Array.Empty<float>();
        public int Capacity;
        public int VisibleCount;
        public long UploadRevision;
        public long SkippedUploads;
        public long UploadedBytes;

        public void EnsureCapacity(MultiMesh mm, int needed)
        {
            if (mm.InstanceCount < needed)
            {
                int newCap = Math.Max(needed + needed / 2 + 8, mm.InstanceCount * 2);
                mm.InstanceCount = newCap;
                Capacity = newCap;
                Buffer = new float[newCap * 20];
            }
            else if (Buffer.Length < mm.InstanceCount * 20)
            {
                Buffer = new float[mm.InstanceCount * 20];
                Capacity = mm.InstanceCount;
            }
        }

        public void Upload(MultiMesh mm, int count, bool reuse = true)
        {
            using var uploadProfile=FrameProfiler.Measure("Flora.BufferUpload");
            // Compare the actual packed bytes, including transform, tint and custom
            // data. Simulation revisions alone do not describe all visual inputs.
            // Capacity changes invalidate native storage even if active data matches.
            bool unchanged = count == VisibleCount && _uploaded.Length == Buffer.Length
                && MemoryMarshal.AsBytes(Buffer.AsSpan(0, count * 20)).SequenceEqual(
                    MemoryMarshal.AsBytes(_uploaded.AsSpan(0, count * 20)));
            if (reuse && unchanged)
            {
                SkippedUploads++;
                return;
            }
            UploadRevision++;
            VisibleCount = count;
            mm.VisibleInstanceCount = count;
            if (count > 0)
            {
                mm.Buffer = Buffer;
                UploadedBytes += (long)Buffer.Length * sizeof(float);
                if (_uploaded.Length != Buffer.Length) _uploaded = new float[Buffer.Length];
                Buffer.AsSpan(0, count * 20).CopyTo(_uploaded);
            }
        }
    }

    private readonly Dictionary<MultiMesh, PooledMultiMeshBuffer> _pooledBuffers = new();
    public bool ReuseInstanceBuffers { get; set; } = true;
    public long InstanceBufferUploads => _pooledBuffers.Values.Sum(b => b.UploadRevision);
    public long InstanceBufferUploadSkips => _pooledBuffers.Values.Sum(b => b.SkippedUploads);
    public long InstanceBufferUploadBytes => _pooledBuffers.Values.Sum(b => b.UploadedBytes);

    /// <summary>Native storage regression audit, outside the measured frame window.</summary>
    public void AuditInstanceUploadCache()
    {
        using var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, UseCustomData = true };
        var cache = new PooledMultiMeshBuffer();
        void Publish(int count, bool expectSkip)
        {
            long revisions = cache.UploadRevision, skips = cache.SkippedUploads;
            cache.Upload(mm, count);
            if (cache.UploadRevision != revisions + (expectSkip ? 0 : 1)
                || cache.SkippedUploads != skips + (expectSkip ? 1 : 0)
                || mm.VisibleInstanceCount != count)
                throw new InvalidOperationException("Flora upload cache transition mismatch");
            if (count > 0 && !MemoryMarshal.AsBytes(mm.Buffer.AsSpan(0, count * 20)).SequenceEqual(
                    MemoryMarshal.AsBytes(cache.Buffer.AsSpan(0, count * 20))))
                throw new InvalidOperationException("Flora upload cache native byte mismatch");
        }
        cache.EnsureCapacity(mm, 2);
        for (int i = 0; i < 40; i++) cache.Buffer[i] = i * .03125f;
        Publish(2, false); Publish(2, true);
        foreach (int channel in new[] { 3, 12, 18 }) // position, tint, interaction custom data
        {
            cache.Buffer[channel] += .125f;
            Publish(2, false); Publish(2, true);
        }
        Publish(1, false); Publish(1, true);
        Publish(2, false); Publish(2, true);
        Publish(0, false); Publish(0, true);
        Publish(2, false);
        cache.EnsureCapacity(mm, mm.InstanceCount + 1);
        Publish(2, false); Publish(2, true);
        GD.Print("FLORA_UPLOAD_CACHE_AUDIT_OK transitions=17 native_bytes=exact");
    }
    private int PublishedCount(MultiMesh mm) => _pooledBuffers.TryGetValue(mm, out var buffer) ? buffer.VisibleCount : 0;

    public sealed record TriangleContribution(string Species, int Tier, int Variant, string Organ,
        int Instances, int TrianglesPerInstance, long Triangles, bool Profile);

    /// <summary>Diagnostic snapshot of published base geometry, without native instance-buffer readbacks.</summary>
    public IReadOnlyList<TriangleContribution> AuditTriangleContributions()
    {
        var rows = new List<TriangleContribution>();
        foreach (var (id, layer) in _layers)
        {
            void Add(MultiMeshInstance3D? node, int triangles, int tier, int variant, string organ)
            {
                if (node == null) return;
                int instances = PublishedCount(node.Multimesh);
                if (instances > 0) rows.Add(new(id, tier, variant, organ, instances, triangles,
                    (long)instances * triangles, layer.Profile != null));
            }
            Add(layer.Veins, layer.VeinTris, -1, -1, "veins");
            for (int i = 0; i < layer.Variants.Length; i++)
            {
                var v = layer.Variants[i];
                int tier = i / layer.MorphCount, variant = i % layer.MorphCount;
                Add(v.Full, v.FullTris, tier, variant, "full");
                Add(v.Juvenile, v.JuvenileTris, tier, variant, "juvenile");
                Add(v.Fruit, v.FruitTris, tier, variant, "fruit");
                Add(v.Tendril, v.TendrilTris, tier, variant, "tendril");
            }
        }
        return rows.OrderByDescending(r => r.Triangles).ThenBy(r => r.Species, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Native metadata oracle for a completed refresh; no instance-buffer readback. Not a GPU pass count.</summary>
    public long AuditPublishedTriangles()
    {
        long triangles = 0;
        foreach (var instance in GetChildren().OfType<MultiMeshInstance3D>())
        {
            var mm = instance.Multimesh;
            var mesh = (ArrayMesh)mm.Mesh;
            int count = mm.VisibleInstanceCount < 0 ? mm.InstanceCount : mm.VisibleInstanceCount;
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                int indices = mesh.SurfaceGetArrayIndexLen(surface);
                int vertices = mesh.SurfaceGetArrayLen(surface);
                triangles += (long)count * (indices > 0 ? indices : vertices) / 3;
            }
        }
        return triangles;
    }

    private void Fill(MultiMesh mm, (List<Transform3D> T, List<Color> Tint, List<Color> C) data)
    {
        using var fillProfile=FrameProfiler.Measure("Flora.Fill");
        int n = data.T.Count;
        if (!_pooledBuffers.TryGetValue(mm, out var pb))
            _pooledBuffers[mm] = pb = new PooledMultiMeshBuffer();

        if (n == 0 && pb.VisibleCount == 0) return;
        pb.EnsureCapacity(mm, n);
        if (n > 0)
        {
            var buf = pb.Buffer;
            for (int i = 0; i < n; i++)
            {
                var t = data.T[i]; var tint = data.Tint[i]; var c = data.C[i]; int o = i * 20;
                buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
                buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
                buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
                buf[o + 12] = tint.R; buf[o + 13] = tint.G; buf[o + 14] = tint.B; buf[o + 15] = tint.A;
                buf[o + 16] = c.R; buf[o + 17] = c.G; buf[o + 18] = c.B; buf[o + 19] = c.A;
            }
        }
        pb.Upload(mm, n, ReuseInstanceBuffers);
    }
}

/// <summary>
/// Fauna drawn from authoritative positions with render-only smoothing. Visible animals retain their full model
/// at any distance; performance comes from visibility rejection and batching, not replacement proxies.
/// </summary>
public partial class FaunaRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private const int MorphVariants = 4;
    private sealed class VariantLayer
    {
        public MultiMeshInstance3D High = null!;
        public MultiMeshInstance3D? Curled;
        public int HighTris, CurledTris;
        public float[] HighBuf = System.Array.Empty<float>(), CurledBuf = System.Array.Empty<float>();
        public int HighCount, CurledCount;
    }
    private sealed class Layer
    {
        public VariantLayer[] Variants = new VariantLayer[MorphVariants];
        public FaunaAnimationDef Animation = null!;
    }
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    /// <summary>
    /// Render-side motion track per animal: the two most recent authoritative positions with the simulated time
    /// each was reached. Animals are drawn one behaviour interval in the past, interpolated between them, so
    /// motion is continuous instead of move-stop-move. Also carries the smoothed yaw and walk-cycle phase.
    /// </summary>
    private sealed class Track
    {
        public Vector3 Prev, Cur, Shown, SourceShown;
        public ToadLocomotion? Toad;
        public double PrevT, CurT;
        public float Yaw, Speed, Pitch, Bend, Activity;
        public double Phase;
        public Vector3 Up = Vector3.Up;
        public Vector3 NormalPosition, CachedNormal = Vector3.Up;
        public double NormalTime = double.NegativeInfinity;
        public int NormalTerrainVersion = -1, NormalPropsVersion = -1;
    }

    private readonly Dictionary<EntityId, Track> _tracks = new();
    private double _normalClock;
    private ContentLibrary? _builtFaunaContent;
    // Reused every frame: frustum copies, live ids and the stale-track scratch list would otherwise
    // allocate on every render tick.
    private Plane[] _frustumPlanes = Array.Empty<Plane>();
    private readonly HashSet<EntityId> _seen = new();
    private readonly List<EntityId> _stale = new();

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    /// <summary>Animals drawn this frame (always the full model) and those skipped because they are off-screen.</summary>
    public int Drawn { get; private set; }
    public int OffScreen { get; private set; }
    public long TrianglesDrawn { get; private set; }
    /// <summary>Total reserved GPU instances across actually used morphs.
    /// Previously each of eight variants reserved every animal in its species.</summary>
    public int ReservedGpuInstanceSlots => _layers.Values.Sum(layer =>
        layer.Variants.Sum(v => v.High.Multimesh.InstanceCount +
            (v.Curled?.Multimesh.InstanceCount ?? 0)));

    public void Build(VivariumWorld w)
    {
        _w = w;
        _tracks.Clear();
        _normalClock = 0;
        // Geometry, materials and morph variants are content resources. A new
        // seed only changes instances; retain the expensive meshes and LODs.
        if (ReferenceEquals(_builtFaunaContent, w.Content) && _layers.Count == w.Content.Fauna.Count)
        {
            foreach (var layer in _layers.Values)
                foreach (var variant in layer.Variants)
                {
                    variant.High.Multimesh.VisibleInstanceCount = 0;
                    if (variant.Curled != null)
                    {
                        variant.Curled.Multimesh.VisibleInstanceCount = 0;
                    }
                    variant.HighCount = variant.CurledCount = 0;
                }
            return;
        }
        _builtFaunaContent = null;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear(); _tracks.Clear();
        foreach (var sp in w.Content.Fauna)
        {
            var hiMat = Bridge.Shader("res://Shaders/fauna.gdshader");
            hiMat.SetShaderParameter("base_color", Bridge.C(sp.BaseColor));
            hiMat.SetShaderParameter("ornament_color", Bridge.C(sp.OrnamentColor));
            FaunaBodyProfiles.Bind(hiMat, sp);
            // These profiles require verified geometry/region semantics. Keep
            // every other species on the existing material, including Rainspine.
            hiMat.SetShaderParameter("surface_family", sp.Id switch
            {
                "ghostbristle" or "marbleback" => 1,
                "moonveil" => 2,
                _ => 0,
            });
            var still = (ShaderMaterial)hiMat.Duplicate();
            still.SetShaderParameter("anim_amplitude", 0.0f);
            if (sp.Id == "duskflicker" || sp.Model == "midge")
                NightFloraMaterials.RegisterMaterial(still);
            var layer = new Layer { Animation = sp.Animation };
            ulong speciesSeed = Hash.Fnv1a64("fauna.visual." + sp.Id);
            for (int v = 0; v < MorphVariants; v++)
            {
                ulong seed = Rng.Mix(speciesSeed, (ulong)(v + 1) * 0x9E3779B97F4A7C15UL);
                var hi = OrganismMeshes.Fauna(sp, seed);
                var vl = new VariantLayer
                {
                    High = MakeMmi($"Fauna_{sp.Id}_{v}_high", Bridge.ToArrayMesh(hi, hiMat, signedVertexData: true, generateLods: true)),
                    HighTris = hi.TriangleCount,
                };
                AddChild(vl.High);
                if (OrganismMeshes.FaunaCurled(sp, seed) is { } curled)
                {
                    vl.Curled = MakeMmi($"Fauna_{sp.Id}_{v}_curled", Bridge.ToArrayMesh(curled, still, signedVertexData: true, generateLods: true));
                    vl.CurledTris = curled.TriangleCount;
                    AddChild(vl.Curled);
                }
                layer.Variants[v] = vl;
            }
            _layers[sp.Id] = layer;
        }
        _builtFaunaContent = w.Content;
    }

    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh) => new()
    {
        Name = name,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    /// <summary>Smoothed display position of an animal (falls back to authoritative position).</summary>
    public Vector3 DisplayPosition(EntityId id)
    {
        if (_tracks.TryGetValue(id, out var tr)) return tr.Shown;
        var f = _w?.Fauna.Get(id);
        return f != null ? Bridge.V(f.Position) : Vector3.Zero;
    }

    /// <summary>Render phase for deterministic distance-clock checks.</summary>
    public double DisplayPhase(EntityId id) => _tracks.TryGetValue(id, out var tr) ? tr.Phase : 0;

    public bool DisplayCrawling(EntityId id) => _tracks.TryGetValue(id, out var tr) && tr.Toad?.IsCrawling == true;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Fauna");
        _normalClock += delta;
        if (_w == null) return;
        float k = 1 - Mathf.Exp(-(float)delta * 8f);
        // draw one behaviour interval in the past, at sub-tick precision
        double behaviourInterval = VivariumWorld.Cadence.FaunaBehaviour * Vivarium.Sim.Time.SimClock.FixedStepSeconds;
        double renderT = (_w.Clock.Tick + _w.Scheduler.TickFraction) * Vivarium.Sim.Time.SimClock.FixedStepSeconds - behaviourInterval;

        var cam = Camera;
        var planes = cam?.GetFrustum();
        Plane[]? frustum = null;
        int frustumCount = 0;
        if (planes != null)
        {
            frustumCount = planes.Count;
            if (_frustumPlanes.Length != frustumCount) _frustumPlanes = new Plane[frustumCount];
            planes.CopyTo(_frustumPlanes, 0);
            frustum = _frustumPlanes;
        }
        Drawn = OffScreen = 0; TrianglesDrawn = 0;
        // Reset counts but retain the previous GPU capacities. Only populate
        // an actual morph variant when one visible animal needs it; allocating
        // the whole species population into ALL variants (and curled variants)
        // was an O(variants * species population) GPU bandwidth tax.
        foreach (var layer in _layers.Values)
            foreach (var vl in layer.Variants)
                vl.HighCount = vl.CurledCount = 0;
        _seen.Clear();
        foreach (var f in _w.Fauna.Items)
        {
            _seen.Add(f.Id);
            var sp = _w.Content.FaunaOrThrow(f.SpeciesId);
            var ph = _w.FaunaSystem.PhenotypeOf(f);
            var target = Bridge.V(f.Position);
            double now = _w.Clock.SimSeconds;
            if (!_tracks.TryGetValue(f.Id, out var tr))
                _tracks[f.Id] = tr = new Track { Prev = target, Cur = target, Shown = target, SourceShown = target, PrevT = now, CurT = now, Yaw = (float)-f.Heading };
            else if (f.Grabbed || tr.Cur.DistanceTo(target) > 0.5f)
            {
                // held, released or reintroduced: jump there rather than slide across the island
                tr.Prev = tr.Cur = tr.Shown = tr.SourceShown = target;
                tr.Toad?.Reset(new Vec3(target.X, target.Y, target.Z)); tr.PrevT = tr.CurT = now;
                tr.Bend = tr.Speed = tr.Activity = 0;
            }
            else if (tr.Cur.DistanceSquaredTo(target) > 1e-12)
            {
                tr.Prev = tr.SourceShown; tr.PrevT = Math.Min(renderT, tr.CurT);
                tr.Cur = target; tr.CurT = Math.Max(now, tr.PrevT + 1e-6);
            }
            double span = tr.CurT - tr.PrevT;
            float alpha = span > 1e-9 ? (float)Math.Clamp((renderT - tr.PrevT) / span, 0, 1) : 1f;
            var shown = tr.Prev.Lerp(tr.Cur, alpha);
            // Pausing freezes simulated interpolation time. Let the toad land at its
            // authoritative foothold rather than permanently retain the interpolation delay.
            if (sp.Model == "toad" && _w.Clock.Paused) shown = target;
            var previousShown = tr.Shown;
            float sourceDist = shown.DistanceTo(tr.SourceShown);
            tr.SourceShown = shown;
            float frameDist = sourceDist;
            // face the direction of travel (or the simulated heading when standing), critically damped
            var motion = new Vector2(tr.Cur.X - tr.Prev.X, tr.Cur.Z - tr.Prev.Z);
            float yawTarget = motion.LengthSquared() > 1e-8 ? Mathf.Atan2(-motion.Y, motion.X) : (float)-f.Heading;
            float yawStep = Mathf.Wrap(yawTarget - tr.Yaw, -Mathf.Pi, Mathf.Pi) * k;
            tr.Yaw += yawStep;
            // Animation cadence is species-authored in body-relative units: apparent gait survives genetic size changes
            // and simulation time-scale changes without hard-coding a model name in the renderer.
            float bodyLen = (float)Math.Max(1e-4, ph.BodySize * sp.VisualScale);
            float speed = delta > 1e-6 ? sourceDist / (float)delta / bodyLen : 0;
            tr.Speed += (speed - tr.Speed) * k;
            var layer = _layers[sp.Id];
            var anim = layer.Animation;
            // Curvature is heading change per body length travelled. Keep a short trailing memory
            // so the rear straightens after the head, and suppress bending while held or stationary.
            float bendTarget = frameDist > 1e-7f && !f.Grabbed
                ? Mathf.Clamp(yawStep * bodyLen / Math.Max(frameDist, bodyLen * 0.002f), -1.25f, 1.25f) : 0;
            float bendK = 1 - Mathf.Exp(-(float)delta * 4f);
            tr.Bend += (bendTarget - tr.Bend) * bendK;
            if (sp.Model == "toad")
            {
                if (tr.Toad == null)
                {
                    tr.Toad = new ToadLocomotion();
                    tr.Toad.Reset(new Vec3(previousShown.X, previousShown.Y, previousShown.Z));
                }
                if (!f.Grabbed)
                    tr.Toad.Step(new Vec3(shown.X, shown.Y, shown.Z), bodyLen, delta, tr.Speed, anim);
                var position = tr.Toad.Position;
                shown = new Vector3((float)position.X, (float)position.Y, (float)position.Z);
                frameDist = shown.DistanceTo(previousShown);
                tr.Phase = tr.Toad.Phase;
            }
            else tr.Phase = (tr.Phase + FaunaGait.Advance(anim, frameDist, bodyLen, f.Grabbed ? 0 : delta)) % 1.0;
            tr.Shown = shown;
            // Ground stride stays constant; only lift/body activity fades when travel stops.
            float activityTarget = frameDist > 1e-7f && !f.Grabbed ? 1 : 0;
            tr.Activity += (activityTarget - tr.Activity) * k;
            float speed01 = FaunaGait.GroundSteps(anim) ? tr.Activity : (float)Math.Clamp(tr.Speed / anim.FullSpeed, 0, 1);
            if (tr.Toad != null && !tr.Toad.IsCrawling) speed01 = !f.Grabbed && tr.Toad.Hopping ? 1 : 0;
            var d = (Pos: shown, Yaw: tr.Yaw);
            // full detail at any distance; only animals outside the view are skipped (invisible either way)
            float scale = (float)(ph.BodySize * sp.VisualScale);
            if (frustum != null && !f.Grabbed)
            {
                float radius = Math.Max(scale * 1.5f, 0.05f);
                bool inView = true;
                for (int pIdx = 0; pIdx < frustumCount; pIdx++)
                {
                    if (frustum[pIdx].DistanceTo(d.Pos) > radius) { inView = false; break; }
                }
                if (!inView) { OffScreen++; continue; }
            }
            // Walkers follow the surface; swimmers derive pitch from interpolated 3-D travel.
            // This is render-only and never feeds orientation back into ecology.
            if (sp.Medium != Medium.Aquatic || sp.Model == "snail")
            {
                // The old 2 cm test rescanned the ground for every small camera-frame movement.
                // Resample at the 8 Hz cadence only once the animal has actually moved 2 mm, or
                // immediately when the substrate itself changed.
                float ndx = d.Pos.X - tr.NormalPosition.X;
                float ndz = d.Pos.Z - tr.NormalPosition.Z;
                float normalDistSq = ndx * ndx + ndz * ndz;
                bool versionsChanged = tr.NormalTerrainVersion != _w.Terrain.Version
                    || tr.NormalPropsVersion != _w.Props.Version;
                bool resample = tr.NormalTime == double.NegativeInfinity
                    || versionsChanged
                    || normalDistSq > .0004f
                    || (_normalClock - tr.NormalTime >= .12 && normalDistSq >= 4e-6f);
                if (resample)
                {
                    if (sp.Model == "snail")
                    {
                        // Use the same submerged support as its placement, including a submerged prop.
                        double e = Math.Max(scale * .5, .01);
                        double Height(double x,double z) => _w.FaunaSystem.RestingY(sp,new Vec2(x,z));
                        tr.CachedNormal = new Vector3((float)(Height(d.Pos.X-e,d.Pos.Z)-Height(d.Pos.X+e,d.Pos.Z)),
                            (float)(2*e),(float)(Height(d.Pos.X,d.Pos.Z-e)-Height(d.Pos.X,d.Pos.Z+e))).Normalized();
                    }
                    else tr.CachedNormal = SurfaceFrame.SurfaceNormal(_w, d.Pos.X, d.Pos.Z, Math.Max(scale * .5, .01));
                    tr.NormalPosition = d.Pos; tr.NormalTime = _normalClock;
                    tr.NormalTerrainVersion = _w.Terrain.Version; tr.NormalPropsVersion = _w.Props.Version;
                }
            }
            var upTarget = sp.Medium == Medium.Aquatic && sp.Model != "snail" ? Vector3.Up : (sp.Model == "snail" || frameDist > 1e-4f ? tr.CachedNormal : tr.Up);
            tr.Up = (tr.Up + (upTarget - tr.Up) * k).Normalized();
            var motion3 = tr.Cur - tr.Prev;
            float horizontal = new Vector2(motion3.X, motion3.Z).Length();
            float pitchTarget = sp.Medium == Medium.Aquatic && sp.Model != "snail" && horizontal > 1e-5f ? Mathf.Atan2(motion3.Y, horizontal) : 0.0f;
            tr.Pitch += (pitchTarget - tr.Pitch) * k;
            var orient = SurfaceFrame.TiltTo(tr.Up) * new Basis(Vector3.Up, d.Yaw);
            if (sp.Medium == Medium.Aquatic && sp.Model != "snail") orient *= new Basis(Vector3.Back, tr.Pitch);
            var basis = orient.Scaled(new Vector3(scale, scale, scale));
            int variant = (int)(Rng.Mix(f.Id.Value, 0xFA0AUL) % MorphVariants);
            var vl = layer.Variants[variant];
            bool curled = vl.Curled != null && _w.FaunaSystem.IsCurled(f);
            int i = curled ? vl.CurledCount++ : vl.HighCount++;
            var buf = curled ? vl.CurledBuf : vl.HighBuf;
            if((i+1)*16>buf.Length)
            {
                // Rare geometric growth. Preserve transforms written earlier
                // in this same frame instead of replacing the buffer.
                buf=Ensure(curled ? vl.Curled!.Multimesh : vl.High.Multimesh,
                    buf,i+1);
                if(curled)vl.CurledBuf=buf;
                else vl.HighBuf=buf;
            }
            int o = i * 16;
            buf[o + 0] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = d.Pos.X;
            buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = d.Pos.Y;
            buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = d.Pos.Z;
            // custom.w packs appendage scale, a 4-bit motion-amplitude bucket, and locomotion phase.
            // Keeping this in one channel preserves the three inherited colour/pattern traits already using xyz.
            int speedBucket = (int)Math.Round(speed01 * 15.0f);
            buf[o + 13] = (float)ph.OrnamentDensity; buf[o + 14] = (float)ph.PatternStrength;
            // Flexible bodies reserve the integer part of x for a signed bend bucket;
            // hue remains in the residual. Zero/unpacked x is also a valid straight preview pose.
            buf[o + 12] = FaunaBodyProfiles.PackHue(sp, (float)ph.HueShift, tr.Bend, tr.Toad?.IsCrawling == true);
            buf[o + 15] = (float)(Math.Round(ph.AppendageScale * 1000) * 16 + speedBucket + Math.Min(tr.Phase, 0.999));
            Drawn++;
        }
        foreach (var layer in _layers.Values)
            foreach (var vl in layer.Variants)
            {
                Upload(vl.High.Multimesh, vl.HighBuf, vl.HighCount);
                if (vl.Curled != null) Upload(vl.Curled.Multimesh, vl.CurledBuf, vl.CurledCount);
                TrianglesDrawn += (long)vl.HighCount * vl.HighTris + (long)vl.CurledCount * vl.CurledTris;
            }
        if (_tracks.Count > _seen.Count + 64)
        {
            _stale.Clear();
            foreach (var key in _tracks.Keys) if (!_seen.Contains(key)) _stale.Add(key);
            foreach (var key in _stale) _tracks.Remove(key);
            _stale.Clear();
        }
    }

    private static float[] Ensure(MultiMesh mm, float[] buf, int needed)
    {
        if (mm.InstanceCount >= needed && buf.Length == mm.InstanceCount * 16) return buf;
        int capacity=Math.Max(Math.Max(needed+needed/2+8,16),mm.InstanceCount*2);
        var larger=new float[capacity*16];
        Array.Copy(buf,larger,Math.Min(buf.Length,larger.Length));
        mm.InstanceCount=capacity;
        return larger;
    }

    private sealed class UploadState { public float[] Copy = Array.Empty<float>(); public int Count = -1; public int Instances = -1; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MultiMesh, UploadState> _uploaded = new();

    private static void Upload(MultiMesh mm, float[] buf, int count)
    {
        mm.VisibleInstanceCount = count;
        if (count <= 0) return;
        var st = _uploaded.GetOrCreateValue(mm);
        int n = Math.Min(count * 16, buf.Length);
        if (st.Count == count && st.Instances == mm.InstanceCount && st.Copy.Length == n
            && buf.AsSpan(0, n).SequenceEqual(st.Copy)) return;
        mm.Buffer = buf;
        if (st.Copy.Length != n) st.Copy = new float[n];
        Array.Copy(buf, st.Copy, n);
        st.Count = count; st.Instances = mm.InstanceCount;
    }
}

/// <summary>Orients ground-hugging organisms to the surface under them.</summary>
internal static class SurfaceFrame
{
    /// <summary>Normal of whatever the organism stands on (terrain or a log/rock top), by central differences
    /// over <paramref name="e"/> metres so it follows the local slope rather than single-cell noise.</summary>
    public static Vector3 SurfaceNormal(VivariumWorld w, float x, float z, double e)
    {
        double hx0 = w.GroundHeight(new Vec2(x - e, z)), hx1 = w.GroundHeight(new Vec2(x + e, z));
        double hz0 = w.GroundHeight(new Vec2(x, z - e)), hz1 = w.GroundHeight(new Vec2(x, z + e));
        var n = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
        // a sample that falls off a log or rock edge reads as a cliff: cap the tilt at ~60 degrees
        return n.Y < 0.5f ? new Vector3(n.X, 0, n.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : n;
    }

    /// <summary>Shortest rotation taking +Y onto <paramref name="n"/>.</summary>
    public static Basis TiltTo(Vector3 n)
    {
        var axis = Vector3.Up.Cross(n);
        float s = axis.Length();
        return s < 1e-5f ? Basis.Identity : new Basis(axis / s, Mathf.Atan2(s, Vector3.Up.Dot(n)));
    }
}
