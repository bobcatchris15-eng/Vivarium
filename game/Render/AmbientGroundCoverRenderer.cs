using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Cheap anonymous low vegetation generated from the authoritative ambient-cover field. Instances are disposable
/// render detail, not organisms. Habitat and neighboring cells choose colour and grass-vs-broadleaf silhouette.
/// <para>
/// Turf lives in 2 m world chunks, each with its own blade and leaf MultiMesh. A chunk is generated at full density
/// the first time the camera comes near it and is then kept: camera motion never regenerates anything. Distance
/// thinning is done per instance in ground_cover.gdshader. A chunk is regenerated (old geometry stays visible until
/// the replacement is ready) only when a tracked plant establishes or is removed nearby, a terrain edit touches it,
/// a rock/log/gravel patch overlapping it changes, or quality changes. Moisture, light and maturity drift are not triggers.
/// </para>
/// </summary>
public partial class AmbientGroundCoverRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private ArrayMesh _bladeMesh = null!, _leafMesh = null!;
    private Node3D _chunkRoot = null!;

    private sealed class Chunk
    {
        public MultiMeshInstance3D BladeMmi = null!, LeafMmi = null!;
        public MultiMesh Blade = null!, Leaf = null!;
        public int Instances, BladeN, LeafN;
        public float LastFrac = -1f;
        public bool Dirty;
        public int Reason;
        public ulong PropSig;
    }
    private const int ReasonFlora = 0, ReasonTerrain = 1, ReasonProps = 2, ReasonQuality = 3;
    private const float ChunkSize = 2f;
    private readonly Dictionary<(int X, int Z), Chunk> _chunks = new();
    private IEnumerator<bool>? _gen;
    private (int X, int Z) _genKey;
    private IEnumerator<bool>? _pendingBank;
    private bool _bankDirty = true;
    private Vector3 _lastBankCam = new(float.MaxValue, 0, 0);
    private double _editDelay;
    private double _floraTimer;
    private ulong _floraBuiltMs;
    private int _pendingChunks = int.MaxValue;
    private int _totalInstances;
    private readonly int[] _regenByReason = new int[4];

    /// <summary>Incremented once per completed chunk generation (first build or regeneration). Bank patches are not counted.</summary>
    public int RefreshCount { get; private set; }
    /// <summary>True while any in-range chunk is missing or dirty (or an edit is coalescing), or bank patches are pending.</summary>
    public bool RefreshPending => _pendingChunks > 0 || _gen is not null || _bankDirty || _pendingBank is not null;
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    private const float CullRadius = 8.0f;
    private const float FadeStart = 5.5f;
    // Chunk centre distance at which a chunk is first generated: cull radius plus one chunk diagonal (a one-chunk lead).
    private const float GenerateRadius = CullRadius + 2.83f;
    /// <summary>Sum of live instances over all generated chunks.</summary>
    public int LastGroundcoverInstances => _totalInstances;
    public int LastInfluencedGridCells { get; private set; }
    public int LastFloraExclusionCandidateChecks { get; private set; }
    public int ChunkCount => _chunks.Count;
    public int ChunksGenerated { get; private set; }
    public int ChunkRegenFlora => _regenByReason[ReasonFlora];
    public int ChunkRegenTerrain => _regenByReason[ReasonTerrain];
    public int ChunkRegenProps => _regenByReason[ReasonProps];
    public int ChunkRegenQuality => _regenByReason[ReasonQuality];

    public void Build(VivariumWorld w, IslandRenderer? habitat = null)
    {
        _gen?.Dispose(); _gen = null; _pendingBank?.Dispose(); _pendingBank = null;
        _w = w;
        _chunks.Clear();
        foreach (var c in GetChildren()) c.QueueFree();

        // Same optical rules for ephemeral ground vegetation and persistent organisms.
        var mat = Bridge.Shader("res://Shaders/ground_cover.gdshader");
        mat.SetShaderParameter("sway_strength", 0f);
        mat.SetShaderParameter("instance_shape_variation", true);
        // The custom direct-light path has its own cuticle lobes; SPECULAR alone
        // only controls the engine's environment response.
        mat.SetShaderParameter("tissue_specular_stress", .12f);
        mat.SetShaderParameter("tissue_material_f0", .02f);
        mat.SetShaderParameter("tissue_direct_transmission", .20f);
        habitat?.BindHabitatMaterial(mat);
        _bladeMesh = BuildBlade(mat, true);
        _leafMesh = BuildBroadleaf(mat, true);
        // These 1.5-3 cm turf fillers ground themselves with basal contact AO in
        // ground_cover.gdshader. Casting them into the 8192 directional shadow map added
        // millions of tiny triangles for no readable shadow, so chunks are shadow-free.
        _chunkRoot = new Node3D { Name = "AmbientChunks" };
        AddChild(_chunkRoot);
        BuildBankFringe(mat);
        habitat?.BindHabitatMaterial(_bankMaterial);
        // Turf chunks thin by camera distance in the shader; the bank fringe (a duplicate made above) does not.
        mat.SetShaderParameter("ambient_distance_fade", true);
        mat.SetShaderParameter("ambient_fade_start", FadeStart);
        mat.SetShaderParameter("ambient_fade_end", CullRadius);
        _bankMaterial.SetShaderParameter("ambient_distance_fade", false);

        _bankDirty = true; _editDelay = 0; RefreshCount = 0;
        _lastBankCam = new(float.MaxValue, 0, 0);
        _terrainVersion = int.MinValue; _propsVersion = int.MinValue; _lastQuality = -1;
        _pendingChunks = int.MaxValue; _totalInstances = 0; ChunksGenerated = 0;
        Array.Clear(_regenByReason);
        _tracked.Clear(); _floraIndex = null; _carpetIndex = null; _floraDiscs.Clear();
        _rockBounds = null; _logBounds = null; _gravelList = null;
        _floraTimer = 0;
    }

    private int _terrainVersion = int.MinValue;
    private int _propsVersion = int.MinValue;
    private int _lastQuality = -1;
    private readonly List<Transform3D> _bladeT = new();
    private readonly List<Color> _bladeC = new();
    private readonly List<Transform3D> _leafT = new();
    private readonly List<Color> _leafC = new();
    private readonly List<float> _bladeR = new();
    private readonly List<float> _leafR = new();

    // ---- tracked flora (id -> position and the widest disc its presence can change, from the last index build)
    private readonly Dictionary<EntityId, (Vec2 P, double Reach)> _tracked = new();
    private AmbientCoverSpatialIndex? _floraIndex, _carpetIndex;
    private readonly List<(Vec2 P, double Exclusion)> _floraDiscs = new();
    private readonly List<(EntityId Id, Vec2 P, double Reach)> _scanAdded = new();
    private readonly List<(Vec2 P, double Reach)> _scanRemoved = new();
    private readonly HashSet<EntityId> _scanSeen = new();

    // ---- props bounds (rebuilt when Props.Version changes)
    private List<(Vivarium.Sim.World.Rock Prop, double MinX, double MaxX, double MinZ, double MaxZ)>? _rockBounds;
    private List<(Vivarium.Sim.World.LogProp Prop, double MinX, double MaxX, double MinZ, double MaxZ)>? _logBounds;
    private List<Vivarium.Sim.World.GravelPatch>? _gravelList;

    public override void _Process(double delta)
    {
        using var timing = Vivarium.Game.App.FrameProfiler.Measure("AmbientGround");
        if (_w == null) return;
        bool enabled = Quality > 0;
        _chunkRoot.Visible = enabled;
        if (_bankNode != null) _bankNode.Visible = enabled;
        if (!enabled)
        {
            _gen?.Dispose(); _gen = null; _pendingBank?.Dispose(); _pendingBank = null;
            _bankDirty = true; _lastQuality = Quality; _pendingChunks = int.MaxValue;
            return;
        }
        double dt = delta > 0 ? delta : .016;
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool haveCam = Camera != null;

        if (_lastQuality != Quality)
        {
            _lastQuality = Quality;
            MarkAll(ReasonQuality); _bankEpoch++; _bankDirty = true;
        }
        if (_terrainVersion != _w.Terrain.Version) Invalidate();
        _editDelay -= dt;
        _floraTimer -= dt;
        if (_floraIndex == null || _floraTimer <= 0) { _floraTimer = .5; ScanFlora(); }
        if (_rockBounds == null || _propsVersion != _w.Props.Version) PropsChanged();

        bool bankMoved = haveCam && cam.DistanceSquaredTo(_lastBankCam) > 2.0f * 2.0f;
        if (_pendingBank is null && (_bankDirty || bankMoved) && _editDelay <= 0)
        {
            _bankDirty = false; _lastBankCam = cam;
            _pendingBank = RefreshBankFringeBatched(cam, haveCam, new List<Vivarium.Sim.World.Rock>(),
                new List<Vivarium.Sim.World.LogProp>(), new List<Vivarium.Sim.World.GravelPatch>(), _floraDiscs).GetEnumerator();
        }

        // One shared 2 ms slice: turf chunks (nearest first) and bank patches, 1 ms each when both have work.
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double chunkBudget = _pendingBank is not null ? 1.0 : 2.0;
        do
        {
            if (_gen == null && !StartNextChunk(cam, haveCam)) break;
            if (!_gen!.MoveNext()) { _gen.Dispose(); _gen = null; }
        } while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < chunkBudget);
        if (_gen == null && _pendingChunks == int.MaxValue) _pendingChunks = 0;
        ApplyDistanceLod(cam, haveCam);
        if (_pendingBank is not null)
        {
            do
            {
                if (!_pendingBank.MoveNext()) { _pendingBank.Dispose(); _pendingBank = null; break; }
            } while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2);
        }
    }

    /// <summary>Terrain edit. With a centre and finite radius only overlapping chunks go dirty, otherwise all of them.
    /// Coalesces edits and keeps visible geometry until a replacement is ready.</summary>
    public void Invalidate(Vec2? centre = null, double radius = double.PositiveInfinity)
    {
        _pendingBank?.Dispose(); _pendingBank = null;
        if (centre is { } p)
        {
            foreach (var (key, patch) in _bankPatches)
            {
                double x = Math.Clamp(p.X, key.X * 2, (key.X + 1) * 2);
                double z = Math.Clamp(p.Z, key.Z * 2, (key.Z + 1) * 2);
                if (Vec2.DistanceSq(p, new(x, z)) <= radius * radius) patch.Epoch = -1;
            }
            // 0.3 m: slope normals sample +-1 cell and instances jitter about a cell around the chunk edge.
            if (double.IsFinite(radius)) MarkDisc(p, radius + .3, ReasonTerrain); else MarkAll(ReasonTerrain);
        }
        else { _bankEpoch++; MarkAll(ReasonTerrain); }
        if (_w is not null) _terrainVersion = _w.Terrain.Version;
        _bankDirty = true; _editDelay = .15;
    }

    public override void _ExitTree()
    {
        _gen?.Dispose(); _gen = null; _pendingBank?.Dispose(); _pendingBank = null;
    }

    // ------------------------------------------------------------------ invalidation

    private void AbortGen() { _gen?.Dispose(); _gen = null; }

    private void MarkChunk((int X, int Z) key, int reason)
    {
        if (_chunks.TryGetValue(key, out var ch) && !ch.Dirty) { ch.Dirty = true; ch.Reason = reason; }
        if (_gen != null && _genKey == key) AbortGen();
    }

    private void MarkAll(int reason)
    {
        foreach (var ch in _chunks.Values) if (!ch.Dirty) { ch.Dirty = true; ch.Reason = reason; }
        AbortGen();
    }

    private void MarkDisc(Vec2 c, double r, int reason)
    {
        int x0 = (int)Math.Floor((c.X - r) / ChunkSize), x1 = (int)Math.Floor((c.X + r) / ChunkSize);
        int z0 = (int)Math.Floor((c.Z - r) / ChunkSize), z1 = (int)Math.Floor((c.Z + r) / ChunkSize);
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
        {
            double nx = Math.Clamp(c.X, x * ChunkSize, (x + 1) * ChunkSize);
            double nz = Math.Clamp(c.Z, z * ChunkSize, (z + 1) * ChunkSize);
            if (Vec2.DistanceSq(c, new Vec2(nx, nz)) <= r * r) MarkChunk((x, z), reason);
        }
    }

    /// <summary>Cheap id-set compare every 0.5 s. A new or removed plant dirties the chunks its exclusion disc
    /// (plus living-carpet reach for groundcover) overlaps. Growth of an existing plant never does.</summary>
    private void ScanFlora()
    {
        var items = _w.Flora.Items;
        if (_floraIndex == null) { RebuildFloraIndex(); return; }
        bool same = items.Count == _tracked.Count;
        if (same)
            foreach (var plant in items)
                if (!_tracked.ContainsKey(plant.Id)) { same = false; break; }
        if (same) return;
        _scanSeen.Clear(); _scanAdded.Clear(); _scanRemoved.Clear();
        var addedIds = new List<EntityId>();
        foreach (var plant in items)
        {
            _scanSeen.Add(plant.Id);
            if (!_tracked.ContainsKey(plant.Id)) addedIds.Add(plant.Id);
        }
        foreach (var (id, t) in _tracked)
            if (!_scanSeen.Contains(id)) _scanRemoved.Add(t);
        RebuildFloraIndex();
        foreach (var id in addedIds)
            if (_tracked.TryGetValue(id, out var t)) MarkDisc(t.P, t.Reach + .3, ReasonFlora);
        foreach (var t in _scanRemoved) MarkDisc(t.P, t.Reach + .3, ReasonFlora);
    }

    private void RebuildFloraIndex()
    {
        var excl = new AmbientCoverSpatialIndex();
        var carpet = new AmbientCoverSpatialIndex();
        _floraDiscs.Clear(); _tracked.Clear();
        foreach (var plant in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(plant.SpeciesId);
            bool isWoody = sp.Shape == "tree" || sp.Tags.Contains("tree") || sp.Tags.Contains("woody");
            // Keep a readable opening at each simulated plant, while allowing low
            // anonymous cover beneath its canopy. Include the turf's leaf reach.
            double collarR = (isWoody ?
                Math.Clamp(plant.Radius(sp) * 0.32, 0.12, 0.28) :
                Math.Clamp(plant.Radius(sp) * 0.65, 0.045, 0.24)) + 0.055;
            double reach = collarR;
            if (sp.Archetype == "plant" && sp.Tags.Contains("groundcover") && !isWoody)
            {
                // Living clonal carpets fill in between leaves as the
                // actual tracked plant gains biomass. These are inexpensive
                // anonymous turf instances, not simulated daughter plants.
                double maturity = plant.BiomassFraction(sp);
                double carpetR = Math.Max(0.16, plant.Radius(sp) * 1.5);
                carpet.Add(plant.Position, carpetR, maturity * Math.Clamp(plant.Health, 0, 1));
                reach = Math.Max(reach, carpetR);
            }
            excl.Add(plant.Position, collarR);
            _floraDiscs.Add((plant.Position, collarR));
            _tracked[plant.Id] = (plant.Position, reach);
        }
        _floraIndex = excl; _carpetIndex = carpet;
        _floraBuiltMs = Time.GetTicksMsec();
    }

    private void PropsChanged()
    {
        bool first = _rockBounds == null;
        var rocks = new List<(Vivarium.Sim.World.Rock, double, double, double, double)>();
        foreach (var r in _w.Props.Rocks)
            rocks.Add((r, r.X - r.FootprintRadius, r.X + r.FootprintRadius, r.Z - r.FootprintRadius, r.Z + r.FootprintRadius));
        var logs = new List<(Vivarium.Sim.World.LogProp, double, double, double, double)>();
        foreach (var l in _w.Props.Logs)
        {
            double extent = l.Length * 0.5 + l.Radius;
            logs.Add((l, l.X - extent, l.X + extent, l.Z - extent, l.Z + extent));
        }
        _rockBounds = rocks; _logBounds = logs; _gravelList = new(_w.Props.Gravel);
        _propsVersion = _w.Props.Version;
        if (first) return;
        AbortGen();
        foreach (var (key, ch) in _chunks)
        {
            if (ch.Dirty) continue;
            if (ChunkPropSig(key) != ch.PropSig) { ch.Dirty = true; ch.Reason = ReasonProps; }
        }
    }

    private static ulong Fnv(ulong h, double v) => (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL;

    /// <summary>Hash of every rock/log/gravel that can touch the chunk, including its pose and size.</summary>
    private ulong ChunkPropSig((int X, int Z) key)
    {
        double minX = key.X * ChunkSize - .35, maxX = (key.X + 1) * ChunkSize + .35;
        double minZ = key.Z * ChunkSize - .35, maxZ = (key.Z + 1) * ChunkSize + .35;
        ulong h = 14695981039346656037UL;
        foreach (var rb in _rockBounds!)
        {
            if (rb.MaxX < minX || rb.MinX > maxX || rb.MaxZ < minZ || rb.MinZ > maxZ) continue;
            var r = rb.Prop;
            h = Fnv(h, r.Id.Value); h = Fnv(h, r.X); h = Fnv(h, r.Z); h = Fnv(h, r.Y); h = Fnv(h, r.RotationY);
            h = Fnv(h, r.SizeX); h = Fnv(h, r.SizeY); h = Fnv(h, r.SizeZ);
        }
        foreach (var lb in _logBounds!)
        {
            if (lb.MaxX < minX || lb.MinX > maxX || lb.MaxZ < minZ || lb.MinZ > maxZ) continue;
            var l = lb.Prop;
            h = Fnv(h, l.Id.Value); h = Fnv(h, l.X); h = Fnv(h, l.Z); h = Fnv(h, l.Y); h = Fnv(h, l.RotationY);
            h = Fnv(h, l.Length); h = Fnv(h, l.Radius);
        }
        foreach (var gr in _gravelList!)
        {
            double reach = gr.Radius + .2;
            if (gr.X + reach < minX || gr.X - reach > maxX || gr.Z + reach < minZ || gr.Z - reach > maxZ) continue;
            h = Fnv(h, gr.Id.Value); h = Fnv(h, gr.X); h = Fnv(h, gr.Z); h = Fnv(h, gr.Radius);
        }
        return h;
    }

    // ------------------------------------------------------------------ scheduling

    /// <summary>Pick the nearest in-range chunk that is missing or dirty and start generating it.</summary>
    private bool StartNextChunk(Vector3 cam, bool haveCam)
    {
        (int X, int Z) bestKey = default; double best = double.MaxValue; int pending = 0;
        void Consider((int X, int Z) key, double d2)
        {
            bool have = _chunks.TryGetValue(key, out var ch);
            if (have && !ch!.Dirty) return;
            pending++;
            if (have && _editDelay > 0) return; // coalesce terrain-edit bursts before regenerating
            if (d2 < best) { best = d2; bestKey = key; }
        }
        if (haveCam)
        {
            int x0 = (int)Math.Floor((cam.X - GenerateRadius) / ChunkSize), x1 = (int)Math.Floor((cam.X + GenerateRadius) / ChunkSize);
            int z0 = (int)Math.Floor((cam.Z - GenerateRadius) / ChunkSize), z1 = (int)Math.Floor((cam.Z + GenerateRadius) / ChunkSize);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                if (!_bankCells.ContainsKey((x, z))) continue;
                double dx = (x + .5) * ChunkSize - cam.X, dz = (z + .5) * ChunkSize - cam.Z;
                double d2 = dx * dx + dz * dz;
                if (d2 > (double)GenerateRadius * GenerateRadius) continue;
                Consider((x, z), d2);
            }
        }
        else
        {
            foreach (var key in _bankCells.Keys) Consider(key, 0);
        }
        _pendingChunks = pending;
        if (best == double.MaxValue) return false;
        // A stale index would leave turf inside collars of plants that have grown since the last build.
        if (_floraIndex == null || Time.GetTicksMsec() - _floraBuiltMs > 2000) RebuildFloraIndex();
        _genKey = bestKey;
        _gen = GenerateChunk(bestKey, _bankCells[bestKey], ChunkPropSig(bestKey), _floraIndex!, _carpetIndex!).GetEnumerator();
        return true;
    }

    // ------------------------------------------------------------------ generation

    private IEnumerable<bool> GenerateChunk((int X, int Z) key, int[] cells, ulong sig,
        AmbientCoverSpatialIndex floraExclusions, AmbientCoverSpatialIndex livingCarpet)
    {
        var g = _w.Grid;
        _bladeT.Clear(); _bladeC.Clear();
        _leafT.Clear(); _leafC.Clear(); _bladeR.Clear(); _leafR.Clear();
        var rng = new CellRng();
        int exclusionCandidates = 0, influencedCells = 0;

        // Props that can touch any instance of this chunk (cells jitter about a cell around their centres).
        double minX = key.X * ChunkSize - .35, maxX = (key.X + 1) * ChunkSize + .35;
        double minZ = key.Z * ChunkSize - .35, maxZ = (key.Z + 1) * ChunkSize + .35;
        var nearRockBounds = new List<(Vivarium.Sim.World.Rock Prop, double MinX, double MaxX, double MinZ, double MaxZ)>();
        foreach (var rb in _rockBounds!)
            if (!(rb.MaxX < minX || rb.MinX > maxX || rb.MaxZ < minZ || rb.MinZ > maxZ)) nearRockBounds.Add(rb);
        var nearLogBounds = new List<(Vivarium.Sim.World.LogProp Prop, double MinX, double MaxX, double MinZ, double MaxZ)>();
        foreach (var lb in _logBounds!)
            if (!(lb.MaxX < minX || lb.MinX > maxX || lb.MaxZ < minZ || lb.MinZ > maxZ)) nearLogBounds.Add(lb);
        var nearGravel = new List<Vivarium.Sim.World.GravelPatch>();
        foreach (var gr in _gravelList!)
        {
            double reach = gr.Radius + .2;
            if (!(gr.X + reach < minX || gr.X - reach > maxX || gr.Z + reach < minZ || gr.Z - reach > maxZ)) nearGravel.Add(gr);
        }

        foreach (int idx in cells)
        {
            yield return true;
            int i = idx % g.Nx, j = idx / g.Nx;
            if (!g.InDomain(i, j)) continue;
            double cover = _w.AmbientGroundCover.Cover[idx];
            var center = g.CellCenter(idx);

            var (moisture, light) = NeighbourCharacter(i, j);
            double broadleaf = MathD.Clamp(0.22 + (1 - light) * 0.34 + moisture * 0.18
                + .12 * Vivarium.Sim.Core.Noise.Gradient(_w.Seed, center.X * 2.7, center.Z * 2.7), .18, .72);

            rng.Seed = Rng.Mix(_w.Seed, (ulong)idx + 0xA6B13UL);
            // This present-state influence rises naturally as established
            // rooted carpets accumulate biomass. Constant authored blades
            // become denser without creating extra tracked organisms.
            double establishedCarpet = livingCarpet.Influence(center);
            if (establishedCarpet > 0.02) influencedCells++;
            // Ground fabric is continuous even where the coarse historical cover field
            // was cleared by a plant. Point exclusions below supply the actual berth.
            // Full density: the shader thins instances by camera distance.
            int count = Math.Max(1, (int)Math.Round((Quality >= 2 ? 42 : 36)
                * (g.CellSize * g.CellSize / .0625) * (1 + establishedCarpet * .12)));
            int columns = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)columns);
            double phaseX = rng.Next01(), phaseZ = rng.Next01();

            // Terrain slope normal and tilt
            double e = g.CellSize;
            double hx0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(-e, 0));
            double hx1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(e, 0));
            double hz0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, -e));
            double hz1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, e));
            var norm = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
            if (norm.Y < 0.70f) continue;
            var cellTilt = SurfaceFrame.TiltTo(norm.Y < 0.5f ? new Vector3(norm.X, 0, norm.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : norm);

            for (int k = 0; k < count; k++)
            {
                // Independently rotated strata plus a continuous world-space warp retain
                // coverage without lining up the same little rows in neighbouring cells.
                double u = (k % columns + rng.Next01()) / columns + phaseX;
                double v = (k / columns + rng.Next01()) / rows + phaseZ;
                double jx = (u - Math.Floor(u) - .5) * g.CellSize;
                double jz = (v - Math.Floor(v) - .5) * g.CellSize;
                double wx = center.X + jx, wz = center.Z + jz;
                jx += .025 * Vivarium.Sim.Core.Noise.Gradient(Rng.Mix(_w.Seed, 71), wx * 4, wz * 4);
                jz += .025 * Vivarium.Sim.Core.Noise.Gradient(Rng.Mix(_w.Seed, 73), wx * 4, wz * 4);
                var p = center + new Vivarium.Sim.Core.Vec2(jx, jz);
                if (!_w.Domain.Contains(p) || _w.SubstrateAt(p) != Substrate.Soil || _w.Water.OpenWaterDepth(p) > 0.0003) continue;

                bool excluded = false;
                for (int ri = 0; ri < nearRockBounds.Count; ri++)
                {
                    var rb = nearRockBounds[ri];
                    if (p.X < rb.MinX || p.X > rb.MaxX || p.Z < rb.MinZ || p.Z > rb.MaxZ) continue;
                    if (rb.Prop.Covers(p)) { excluded = true; break; }
                }
                if (excluded) continue;
                for (int li = 0; li < nearLogBounds.Count; li++)
                {
                    var lb = nearLogBounds[li];
                    if (p.X < lb.MinX || p.X > lb.MaxX || p.Z < lb.MinZ || p.Z > lb.MaxZ) continue;
                    if (lb.Prop.Covers(p)) { excluded = true; break; }
                }
                if (excluded) continue;
                double gravelWeight = GravelCoverWeight(p, nearGravel);
                if (rng.Next01() > gravelWeight) continue;
                excluded = floraExclusions.Contains(p);
                exclusionCandidates += floraExclusions.LastCandidates;
                if (excluded) continue;

                bool isLeaf = rng.Next01() < broadleaf;
                float yaw = (float)(rng.Next01() * Math.PI * 2);
                float scale = (float)((0.78 + rng.Next01() * 0.38) * (0.94 + cover * 0.08))
                    * (float)(.65 + .35 * gravelWeight);
                float heightScale = isLeaf
                    ? scale * (float)(0.70 + 0.16 * moisture)
                    : scale * (float)(0.72 + 0.22 * light);
                float aspect = (float)(.75 + rng.Next01() * .58);
                heightScale *= (float)(.62 + rng.Next01() * .83);
                var basis = cellTilt * new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale * aspect, heightScale, scale / aspect));
                // Embed the root collar by one millimetre; leave the crown visible.
                float y = (float)_w.Terrain.Height(p) - 0.001f;
                var t = new Transform3D(basis, new Vector3((float)p.X, y, (float)p.Z));
                // The live field supplies habitat colour in the shader; vertex colour
                // supplies only a small stable tissue variation, never a stale field sample.
                float vary = (float)(0.88 + rng.Next01() * 0.16);
                var col = new Color(vary, vary, vary, 1);
                float rank = Math.Min(0.99999f, (uint)(Rng.Mix(_w.Seed ^ 0x51EDUL, ((ulong)BitConverter.SingleToUInt32Bits(t.Origin.X) << 32) | BitConverter.SingleToUInt32Bits(t.Origin.Z)) >> 32) / 4294967296f);
                if (isLeaf) { _leafT.Add(t); _leafC.Add(col); _leafR.Add(rank); }
                else { _bladeT.Add(t); _bladeC.Add(col); _bladeR.Add(rank); }
            }
        }

        // Replacement is complete: swap it in within one frame so old geometry stays visible until now.
        if (!_chunks.TryGetValue(key, out var chunk))
        {
            chunk = NewChunk(key);
            _chunks[key] = chunk;
            ChunksGenerated++;
        }
        else _regenByReason[chunk.Reason]++;
        using (FrameProfiler.Measure("AmbientGround.BladeUpload")) FillChunk(chunk.Blade, _bladeT, _bladeC, _bladeR);
        using (FrameProfiler.Measure("AmbientGround.LeafUpload")) FillChunk(chunk.Leaf, _leafT, _leafC, _leafR);
        int n = _bladeT.Count + _leafT.Count;
        _totalInstances += n - chunk.Instances;
        chunk.Instances = n; chunk.BladeN = _bladeT.Count; chunk.LeafN = _leafT.Count; chunk.LastFrac = -1f;
        chunk.Dirty = false; chunk.PropSig = sig;
        LastInfluencedGridCells = influencedCells;
        LastFloraExclusionCandidateChecks = exclusionCandidates;
        RefreshCount++;
    }

    /// <summary>Per-frame draw-count LOD. Instances are stored sorted by thinning rank, so the part the shader
    /// can still show at a chunk's nearest point is a prefix; the thinned tail is not submitted at all.</summary>
    private void ApplyDistanceLod(Vector3 cam, bool haveCam)
    {
        foreach (var (key, ch) in _chunks)
        {
            float frac = 1f;
            if (haveCam)
            {
                float nx = Math.Clamp(cam.X, key.X * ChunkSize, (key.X + 1) * ChunkSize);
                float nz = Math.Clamp(cam.Z, key.Z * ChunkSize, (key.Z + 1) * ChunkSize);
                float dn = Mathf.Max(0f, Mathf.Sqrt((nx - cam.X) * (nx - cam.X) + (nz - cam.Z) * (nz - cam.Z)) - .15f);
                float keep = 1f - (dn - FadeStart) / (CullRadius - FadeStart);
                frac = Mathf.Clamp(keep * 1.125f, 0f, 1f);
            }
            if (Mathf.Abs(frac - ch.LastFrac) < .004f && (frac > 0f) == (ch.LastFrac > 0f)) continue;
            ch.LastFrac = frac;
            int b = frac >= 1f ? ch.BladeN : (int)Math.Ceiling(ch.BladeN * frac);
            int l = frac >= 1f ? ch.LeafN : (int)Math.Ceiling(ch.LeafN * frac);
            ch.Blade.VisibleInstanceCount = b; ch.Leaf.VisibleInstanceCount = l;
            ch.BladeMmi.Visible = b > 0; ch.LeafMmi.Visible = l > 0;
        }
    }

    private Chunk NewChunk((int X, int Z) key)
    {
        var ch = new Chunk
        {
            Blade = NewMultiMesh(_bladeMesh),
            Leaf = NewMultiMesh(_leafMesh),
        };
        // No visibility range: ApplyDistanceLod trims the drawn prefix and hides chunks past the cull radius;
        // the shader does the exact per-instance thinning inside that prefix.
        ch.BladeMmi = NewChunkMmi($"Blades_{key.X}_{key.Z}", ch.Blade);
        ch.LeafMmi = NewChunkMmi($"Leaves_{key.X}_{key.Z}", ch.Leaf);
        _chunkRoot.AddChild(ch.BladeMmi); _chunkRoot.AddChild(ch.LeafMmi);
        return ch;
    }

    private static MultiMeshInstance3D NewChunkMmi(string name, MultiMesh mm) => new()
    {
        Name = name,
        Multimesh = mm,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private static MultiMesh NewMultiMesh(ArrayMesh mesh) => new()
    {
        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
        UseColors = true,
        Mesh = mesh,
        InstanceCount = 0,
    };

    private static double GravelCoverWeight(Vec2 p, List<GravelPatch> gravel)
    {
        double weight = 1;
        foreach (var patch in gravel)
        {
            var d = p - patch.Position;
            if (d.LengthSq > Math.Pow(patch.Radius + .20, 2)) continue;
            // Match GravelPatch.Covers' authored irregular outline, then taper on
            // the soil side. Gravel interiors stay clear of vegetation.
            double a = d.Angle;
            ulong seed = Rng.Mix(patch.VariantSeed, 0x47524156454CUL);
            double n = Vivarium.Sim.Core.Noise.Value3(seed, Math.Cos(a) * 1.7, Math.Sin(a) * 1.7, .37);
            double n2 = Vivarium.Sim.Core.Noise.Value3(Rng.Mix(seed, 17), Math.Cos(a) * 3.1, Math.Sin(a) * 3.1, .11);
            double edge = Math.Min(patch.Radius, patch.Radius * (.86 + .11 * (n * .5 + .5) + .04 * n2));
            double t = Math.Clamp((Math.Sqrt(d.LengthSq) - edge - .025) / .18, 0, 1);
            weight = Math.Min(weight, t * t * (3 - 2 * t));
        }
        return weight;
    }

    private (double Moisture, double Light) NeighbourCharacter(int i, int j)
    {
        double m = 0, l = 0, w = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!_w.Grid.InDomain(i + dx, j + dz)) continue;
            int idx = _w.Grid.Index(i + dx, j + dz);
            double weight = dx == 0 && dz == 0 ? 2 : 1;
            m += _w.Fields.Moisture.Values[idx] * weight;
            l += _w.FloraSystem.EffectiveLightCell(idx) * weight;
            w += weight;
        }
        return w > 0 ? (m / w, l / w) : (0.5, 0.7);
    }

    private static ArrayMesh BuildBlade(Material mat, bool simplified = false)
    {
        // A render instance is a 5-blade turf cluster: high instance count plus deterministic cell
        // jitter makes this read as continuous anonymous turf instead of repeated circular clumps.
        // Every blade is a cambered V-sheet carrying real u (across the width) and v (base to
        // tip), so ground_cover.gdshader's midrib and vein terms are live instead of dead code.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        for (int k = 0; k < 5; k++)
        {
            double a = k * 2.399963229728653 + 0.31 + Math.Sin(k * 3.7) * .27;
            AddBladeSheet(m, a, .078 + .022 * Math.Sin(k * 2.8 + .4),
                .0034 + .0012 * Math.Cos(k * 3.1), white, simplified);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddBladeSheet(MeshData m, double angle, double length, double halfWidth, double[] rgb, bool simplified)
    {
        int rows = simplified ? 3 : 4; const int lanes = 2;
        var pos = new Vec3[rows + 1, lanes + 1];
        // Buried root collar, then a steeper rise and an outward-drooping tip.
        var root = new Vec3(Math.Cos(angle) * 0.0035, -0.0015, Math.Sin(angle) * 0.0035);
        var lean = new Vec3(Math.Cos(angle + 0.55), 0, Math.Sin(angle + 0.55));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double height = length * (t - (.28 + .18 * Math.Sin(angle * 1.7)) * t * t);
            double reach = length * (.32 + .20 * Math.Cos(angle * 2.3)) * t * t;
            double width = halfWidth * Math.Max(0.08, 1.0 - 0.94 * t * t);
            for (int lane = 0; lane <= lanes; lane++)
            {
                // Lanes run +side to -side so the sheet normal (and therefore the front face) points
                // away from the clump axis, i.e. the visible outside of the blade.
                double s = 1.0 - 2.0 * lane / lanes;                         // +1, 0, -1
                // A turf shoot is round, not a folded fin: a shallow dome down the
                // midrib and a soft margin droop, matched by the shader's normal roll.
                double ridge = halfWidth * 0.62 * (1.0 - Math.Abs(s));       // raised midrib
                double fold = -halfWidth * 0.20 * Math.Abs(s) * Math.Abs(s); // rounded margins
                pos[r, lane] = root + Vec3.Up * (height + ridge + fold) + lean * reach + side * (width * s);
            }
        }
        EmitSheet(m, pos, rows, lanes, rgb, 0.58, 1.0);
    }

    private static ArrayMesh BuildBroadleaf(Material mat, bool simplified = false)
    {
        // Low rosette of five curled cotyledons of varied size. Each leaf has an
        // arched petiole, a crenate margin and cupped edges, carrying real u (across) and v (petiole
        // to tip) so the venation terms resolve on every blade.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        double[] sizes = { 1.0, 0.72, 1.25, 0.85, 0.60 };
        for (int k = 0; k < 5; k++)
            AddLeafSheet(m, 0.4 + k * 2.399963229728653 + Math.Sin(k * 2.1) * .31, sizes[k], white, simplified);
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddLeafSheet(MeshData m, double angle, double size, double[] rgb, bool simplified)
    {
        int rows = simplified ? 4 : 6, lanes = simplified ? 2 : 4;
        var pos = new Vec3[rows + 1, lanes + 1];
        var dir = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        var crown = new Vec3(0, 0.005, 0);
        double length = 0.043 * size, halfWidth = 0.016 * size * (.78 + .28 * Math.Cos(angle * 2.3));
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double bladeT = Math.Clamp((t - 0.22) / 0.78, 0, 1);
            double profile = Math.Sin(Math.PI * bladeT);
            // Arched petiole lifts the blade off the soil, then the tip droops back toward it.
            double arch = 0.30 * length * Math.Sin(Math.PI * Math.Clamp(t, 0, 1))
                + 0.010 * length * Math.Min(1.0, t / 0.22) - 0.02 * length * t * t * t;
            // Crenate margin: shallow scallops rather than a smooth lens.
            double width = halfWidth * Math.Pow(Math.Max(0, profile), .52 + .36 * (.5 + .5 * Math.Sin(angle * 1.9)))
                * (1.0 + 0.07 * Math.Sin(bladeT * Math.Tau * 4.5));
            width = Math.Max(width, halfWidth * 0.075);
            for (int lane = 0; lane <= lanes; lane++)
            {
                double s = 2.0 * lane / lanes - 1.0;
                double cup = halfWidth * 0.55 * Math.Abs(s) * profile;          // margin curls up
                double rib = halfWidth * 0.30 * (1.0 - Math.Abs(s)) * profile;  // raised midrib
                double twist = halfWidth * .30 * Math.Sin(angle * 2.7) * s * bladeT * bladeT;
                pos[r, lane] = crown + dir * (length * t) + side * (width * s
                    + length * .08 * Math.Sin(angle) * t * t) + Vec3.Up * (arch + cup + rib + twist);
            }
        }
        EmitSheet(m, pos, rows, lanes, rgb, 0.55, 1.0);
    }

    /// <summary>
    /// Emits one single-sided sheet with smooth normals and real (u, v) coordinates.
    /// Godot front faces are clockwise, so every triangle is reversed against its vertex normal.
    /// </summary>
    private static void EmitSheet(MeshData m, Vec3[,] grid, int rows, int lanes, double[] rgb,
        double aoBase, double aoTip)
    {
        int stride = lanes + 1;
        int count = (rows + 1) * stride;
        var normals = new Vec3[count];
        for (int r = 0; r < rows; r++)
        for (int lane = 0; lane < lanes; lane++)
        {
            Vec3 p00 = grid[r, lane], p10 = grid[r, lane + 1];
            Vec3 p01 = grid[r + 1, lane], p11 = grid[r + 1, lane + 1];
            Vec3 n0 = (p10 - p00).Cross(p11 - p00);
            Vec3 n1 = (p11 - p00).Cross(p01 - p00);
            int v00 = r * stride + lane, v10 = v00 + 1;
            int v01 = (r + 1) * stride + lane, v11 = v01 + 1;
            normals[v00] += n0; normals[v10] += n0; normals[v11] += n0;
            normals[v00] += n1; normals[v11] += n1; normals[v01] += n1;
        }
        for (int i = 0; i < count; i++)
            normals[i] = normals[i].LengthSq > 1e-20 ? normals[i].Normalized() : Vec3.Up;

        var index = new int[count];
        for (int r = 0; r <= rows; r++)
        for (int lane = 0; lane <= lanes; lane++)
        {
            int v = r * stride + lane;
            double u = lane / (double)lanes, w = r / (double)rows;
            double ao = aoBase + (aoTip - aoBase) * w;
            // Baked basal warm tint: the root collar picks up olive humus so each
            // instance blends into the litter before the shader's own habitat tint.
            double root = 1.0 - Math.Clamp(w / 0.30, 0.0, 1.0);
            root *= root;
            double[] vc =
            {
                rgb[0] * (1.0 - 0.16 * root),
                rgb[1] * (1.0 - 0.22 * root),
                rgb[2] * (1.0 - 0.34 * root),
            };
            index[v] = m.AddVertex(grid[r, lane], normals[v], vc, 1, u, w, ao, 0);
        }
        for (int r = 0; r < rows; r++)
        for (int lane = 0; lane < lanes; lane++)
        {
            int a = index[r * stride + lane], b = index[r * stride + lane + 1];
            int c = index[(r + 1) * stride + lane + 1], d = index[(r + 1) * stride + lane];
            AddSheetTriangle(m, a, b, c);
            AddSheetTriangle(m, a, c, d);
        }
    }

    private static void AddSheetTriangle(MeshData m, int a, int b, int c)
    {
        var face = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
        if (face.Dot(m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c)) > 0) (b, c) = (c, b);
        m.AddTriangle(a, b, c);
    }

    private static void FillChunk(MultiMesh mm, List<Transform3D> xf, List<Color> col, List<float> rank)
    {
        int n = xf.Count;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        mm.VisibleInstanceCount = n;
        if (n == 0) return;
        var buf = new float[n * 16];
        var order = new int[n]; var keys = rank.ToArray();
        for (int q = 0; q < n; q++) order[q] = q;
        Array.Sort(keys, order); // ascending rank: the shader keeps low ranks longest, so a prefix is the visible set
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            int src = order[i]; var t = xf[src]; var c = col[src]; int o = i * 16;
            buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
            buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
            buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
            buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = keys[i];
            minX = Math.Min(minX, t.Origin.X); maxX = Math.Max(maxX, t.Origin.X);
            minY = Math.Min(minY, t.Origin.Y); maxY = Math.Max(maxY, t.Origin.Y);
            minZ = Math.Min(minZ, t.Origin.Z); maxZ = Math.Max(maxZ, t.Origin.Z);
        }
        mm.Buffer = buf;
        // Instance reach: blade/leaf meshes are ~0.15 m tall at full scale plus lean and curl; pad generously.
        mm.CustomAabb = new Aabb(new Vector3(minX - .3f, minY - .1f, minZ - .3f),
            new Vector3(maxX - minX + .6f, maxY - minY + .45f, maxZ - minZ + .6f));
    }

    private struct CellRng
    {
        private ulong _s;
        public ulong Seed { set => _s = value == 0 ? 0x9E3779B97F4A7C15UL : value; }
        private uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 32); }
        public double Next01() => Next() * (1.0 / 4294967296.0);
    }
}
