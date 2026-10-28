using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using System.Text.Json.Serialization;

namespace Vivarium.Sim.Flora;

public enum FloraStage { Juvenile, Mature, Senescent }
public enum PlantReproductiveStage : byte { Dormant = 0, Developing = 1, Ripe = 2, Spent = 3 }

/// <summary>
/// One plant individual or moss/lichen colony. Colonies and individuals share this API: a colony's
/// area grows with biomass (see <see cref="Radius"/>).
/// </summary>
public sealed class FloraIndividual
{
    public EntityId Id { get; set; }
    public string SpeciesId { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>Age in simulated seconds.</summary>
    public double Age { get; set; }
    public double Biomass { get; set; }
    /// <summary>0..1; reaching 0 kills the individual.</summary>
    public double Health { get; set; } = 1;
    public int SpreadCount { get; set; }
    public double LastSpreadAge { get; set; }
    public PlantReproductiveStage ReproductiveStage { get; set; }
    public double ReproductiveStageAge { get; set; }
    public double ReproductiveReserve { get; set; }
    public double FruitLoad { get; set; }
    public int FruitPulseCount { get; set; }
    /// <summary>Lifespan multiplier drawn at establishment (deterministic).</summary>
    public double LifespanFactor { get; set; } = 1;
    /// <summary>Last evaluated habitat suitability (0..1), for inspection.</summary>
    public double LastSuitability { get; set; }
    /// <summary>Habitat reproductive response recorded by flora update for the next reproduction tick.</summary>
    public double LastReproductionSuitability { get; set; }
    /// <summary>Creeping organisms: biological seconds spent without enough food.</summary>
    public double StarvedFor { get; set; }
    /// <summary>Creeping organisms: stopped to fruit (release spores, then die back).</summary>
    public bool Fruiting { get; set; }
    /// <summary>Creeping organisms: the patch this one budded from (a vein joins them); None for founders.</summary>
    public EntityId ParentId { get; set; }
    /// <summary>Creeping organisms: advance accumulated toward the next bud (m).</summary>
    public double CreepCredit { get; set; }
    /// <summary>Colonial species (moss/lichen): the founding cell's id; None for the founder itself.</summary>
    public EntityId ColonyRoot { get; set; }
    /// <summary>Colonial species: distance from the colony root at birth (m); used for banded lichen tint.</summary>
    public double RingDist { get; set; }
    /// <summary>Per-instance render tint (linear 0..1 RGB), multiplied into the species colour. Default white = no change.</summary>
    public double[] Tint { get; set; } = { 1, 1, 1 };
    /// <summary>Colonial species: 0..1 fill toward the species' colony maxHeight, rises only while interior.</summary>
    public double HeightFactor { get; set; } = 1;
    /// <summary>Colonial species: how many fully-surrounded interior cells have been merged into this one
    /// (1 = unmerged). Lets a mature mat's dense interior collapse into fewer, larger cells while the rim
    /// stays fine-grained; scales <see cref="Radius"/> so the merged cell reads as bigger, not just thicker.</summary>
    public double MergeFactor { get; set; } = 1;
    /// <summary>Slime mold: network depth from the founding plasmodium (0 = founder), set once at bud creation.
    /// Drives the old-dark-ochre to front-bright-yellow tint gradient; not walked from ParentId per frame.</summary>
    public int Generation { get; set; }
    /// <summary>Climbers: this node is an active horizontal growth tip.</summary>
    public bool ClimberTip { get; set; }
    /// <summary>Climbers: this node has physically reached and attached to structural support.</summary>
    public bool ClimberAttached { get; set; }
    /// <summary>Climbers: accumulated runner length from the founder without an attachment (m).</summary>
    public double UnsupportedLength { get; set; }
    /// <summary>Climbers: sequence of stem nodes and segments grown by this plant.</summary>
    public System.Collections.Generic.List<ClimberSegment>? ClimberSegments { get; set; }
    /// <summary>Cached ground height at establishment; avoids repeating expensive terrain + prop queries every frame.</summary>
    [JsonIgnore] // Derived from position and terrain; NaN is the uncached sentinel, never save state.
    public double GroundHeight { get; set; } = double.NaN;

    public Vec2 Position => new(X, Z);

    public FloraStage Stage(FloraSpeciesDef sp) =>
        Age < sp.MaturityAge ? FloraStage.Juvenile : Age > sp.Lifespan * LifespanFactor * 0.85 ? FloraStage.Senescent : FloraStage.Mature;

    public double BiomassFraction(FloraSpeciesDef sp) => MathD.Clamp01(Biomass / sp.MaxBiomass);
    /// <summary>Render/footprint radius. Colonial cells ignore the biomass-scaled formula (that radius describes
    /// a whole plant, not a single mat cell) and instead use the colony's fixed cell size, growing only slightly
    /// as the cell fills toward the colony's max height so rim cells stay small and interior cells overlap enough
    /// for a continuous but visibly lumpy mat.</summary>
    public double Radius(FloraSpeciesDef sp) => sp.Colony != null
        ? sp.Colony.CellRadius * (0.9 + 0.3 * HeightFactor) * Math.Sqrt(MergeFactor)
        : sp.MinRadius + (sp.RadiusAtMax - sp.MinRadius) * Math.Sqrt(BiomassFraction(sp));
}

/// <summary>
/// One node of a climbing or creeping vine. A node is also the state of the shoot that ends there: while it has
/// no children it is that shoot's growing tip, and it carries the shoot's heading, its transported lateral
/// reference and its accumulated growth credit.
/// </summary>
public sealed class ClimberSegment
{
    public Vec3 Position { get; set; }
    public Vec3 Normal { get; set; } = Vec3.Up;
    public Vec3 Forward { get; set; } = new Vec3(0, 0, 1);
    public int ParentIndex { get; set; } = -1;
    public int ShootOrder { get; set; }
    public bool Attached { get; set; }
    public bool Terminal { get; set; }
    public double Age { get; set; }
    public bool Senescent { get; set; }

    /// <summary>
    /// The shoot's lateral reference, parallel-transported along the surface. It does not rotate across a flat
    /// plane and does rotate around a round host; that difference is what makes one growth law wind a vine
    /// around a trunk without winding it around a wall.
    /// </summary>
    public Vec3 Lateral { get; set; } = new Vec3(1, 0, 0);
    /// <summary>The world direction this shoot wants to travel: the host's axis, or its course over open ground.</summary>
    public Vec3 ClimbDir { get; set; } = Vec3.Up;
    /// <summary>The surface this node lies on.</summary>
    public ClimberSurfaceKind Support { get; set; } = ClimberSurfaceKind.Ground;
    /// <summary>Host entity for woody supports.</summary>
    public EntityId HostId { get; set; }
    /// <summary>Index into the prop log or rock list; -1 otherwise.</summary>
    public int HostIndex { get; set; } = -1;
    /// <summary>Arc length from the founder to this node, in metres.</summary>
    public double Arc { get; set; }
    /// <summary>Arc length from the start of this node's own shoot, in metres. Lateral shoots are short.</summary>
    public double LocalArc { get; set; }
    /// <summary>Node depth from the founder. Drives the stem's taper.</summary>
    public int Depth { get; set; }
    /// <summary>Growth credit accumulated by the shoot whose tip this node is.</summary>
    public double Credit { get; set; }
    /// <summary>Stem radius at this node, in metres.</summary>
    public double Radius { get; set; }
    /// <summary>Length of stem this shoot has grown without a host to hold it.</summary>
    public double UnsupportedLength { get; set; }
}

/// <summary>Uniform-grid (spatial hash) bucket index for local queries (avoids whole-population scans).</summary>
public class SpatialGrid<T> where T : class
{
    private readonly double _cell;
    private readonly Func<T, Vec2> _pos;
    private readonly Dictionary<(int,int), List<T>> _buckets;
    public int Count { get; private set; }

    public SpatialGrid(double cell, Func<T, Vec2> pos)
    {
        _cell = cell <= 0 ? 1 : cell; _pos = pos;
        _buckets = new Dictionary<(int,int), List<T>>();
    }

    public SpatialGrid(double halfExtent, double cell, Func<T, Vec2> pos) : this(cell, pos) { }

    public SpatialGrid(Func<T, Vec2> pos) : this(1.0, pos) { }

    private (int,int) CellOf(Vec2 p) => ((int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Z / _cell));

    public void Clear() { _buckets.Clear(); Count = 0; }

    public void Add(T item)
    {
        var c = CellOf(_pos(item));
        if (!_buckets.TryGetValue(c, out var list)) { list = new List<T>(4); _buckets[c] = list; }
        list.Add(item); Count++;
    }

    public bool Remove(T item) { return Remove(item, _pos(item)); }

    public bool Remove(T item, Vec2 at)
    {
        var c = CellOf(at);
        if (!_buckets.TryGetValue(c, out var list)) return false;
        bool r = list.Remove(item);
        if (r) Count--;
        if (list.Count == 0) _buckets.Remove(c);
        return r;
    }

    /// <summary>Items within radius of p. Order: cell scan order, then insertion order - deterministic for a
    /// deterministic insertion sequence. Callers that need id order must sort.</summary>
    public void Query(Vec2 p, double radius, List<T> into)
    {
        int i0 = (int)Math.Floor((p.X - radius) / _cell);
        int i1 = (int)Math.Floor((p.X + radius) / _cell);
        int j0 = (int)Math.Floor((p.Z - radius) / _cell);
        int j1 = (int)Math.Floor((p.Z + radius) / _cell);
        double r2 = radius * radius;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                if (_buckets.TryGetValue((i,j), out var list))
                    foreach (var it in list)
                        if (Vec2.DistanceSq(_pos(it), p) <= r2) into.Add(it);
    }

    public List<T> Query(Vec2 p, double radius)
    {
        var into = new List<T>();
        Query(p, radius, into);
        return into;
    }

    public IEnumerable<T> ItemsNear(Vec2 p, double radius)
    {
        var into = new List<T>();
        Query(p, radius, into);
        return into;
    }

    /// <summary>Number of stored items examined by a query (for bounded-cost tests).</summary>
    public int CandidatesExamined(Vec2 p, double radius)
    {
        int n = 0;
        int i0 = (int)Math.Floor((p.X - radius) / _cell);
        int i1 = (int)Math.Floor((p.X + radius) / _cell);
        int j0 = (int)Math.Floor((p.Z - radius) / _cell);
        int j1 = (int)Math.Floor((p.Z + radius) / _cell);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                if (_buckets.TryGetValue((i,j), out var list)) n += list.Count;
        return n;
    }
}

/// <summary>Backwards-compatible alias for the earlier bounded-bucket index.</summary>
public sealed class SpatialBuckets<T> : SpatialGrid<T> where T : class
{
    public SpatialBuckets(double halfExtent, double cell, Func<T, Vec2> pos) : base(halfExtent, cell, pos) { }
    public SpatialBuckets(double cell, Func<T, Vec2> pos) : base(cell, pos) { }
    public SpatialBuckets(Func<T, Vec2> pos) : base(pos) { }
}

/// <summary>Id-ordered flora population with a spatial index.</summary>
public sealed class FloraPopulation
{
    public List<FloraIndividual> Items { get; } = new();
    private readonly Dictionary<EntityId, FloraIndividual> _byId = new();
    private readonly Dictionary<string, int> _countBySpecies = new(StringComparer.Ordinal);
    public SpatialGrid<FloraIndividual> Index { get; }
    /// <summary>Changes whenever membership changes (renderer hint; not authoritative).</summary>
    public int Version { get; private set; }
    private int _indexedVersion;

    public FloraPopulation(double halfExtent) { Index = new SpatialBuckets<FloraIndividual>(halfExtent, 0.5, f => f.Position); _indexedVersion = Version; }

    public int Count => Items.Count;
    public int CountOf(string species) => _countBySpecies.GetValueOrDefault(species);
    public FloraIndividual? Get(EntityId id) => _byId.GetValueOrDefault(id);

    public void Add(FloraIndividual f)
    {
        if (Items.Count > 0 && Items[^1].Id.Value >= f.Id.Value) throw new InvalidOperationException("Flora must be added in ascending id order.");
        Items.Add(f); _byId[f.Id] = f; _countBySpecies[f.SpeciesId] = CountOf(f.SpeciesId) + 1; Index.Add(f); Version++; _indexedVersion = Version;
    }

    public bool Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var f)) return false;
        Items.Remove(f); _countBySpecies[f.SpeciesId] = Math.Max(0, CountOf(f.SpeciesId) - 1); Index.Remove(f, f.Position); Version++; _indexedVersion = Version;
        return true;
    }

    public void Clear() { Items.Clear(); _byId.Clear(); _countBySpecies.Clear(); Index.Clear(); Version++; _indexedVersion = Version; }

    /// <summary>Relocates an individual (creeping organisms) keeping the spatial index consistent.</summary>
    public void Move(FloraIndividual f, Vec2 to)
    {
        Index.Remove(f, f.Position);
        f.X = to.X; f.Z = to.Z;
        Index.Add(f);
        _indexedVersion = Version;
    }

    public void RebuildIndex() { Index.Clear(); foreach (var f in Items) Index.Add(f); _indexedVersion = Version; }

    public void RebuildIfNeeded() { if (_indexedVersion != Version) RebuildIndex(); }

    public void Neighbours(Vec2 p, double radius, List<FloraIndividual> into) { RebuildIfNeeded(); into.Clear(); Index.Query(p, radius, into); }
    public void Query(Vec2 p, double radius, List<FloraIndividual> into) { Neighbours(p, radius, into); }

    public List<FloraIndividual> ItemsNear(Vec2 p, double radius) { var list = new List<FloraIndividual>(); Neighbours(p, radius, list); return list; }

    // Aliases for discoverability.
    public SpatialGrid<FloraIndividual> SpatialGrid => Index;
    public SpatialGrid<FloraIndividual> SpatialHash => Index;
}
