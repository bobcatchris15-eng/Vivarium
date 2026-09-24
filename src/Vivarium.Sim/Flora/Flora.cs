using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Flora;

public enum FloraStage { Juvenile, Mature, Senescent }

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
    /// <summary>Lifespan multiplier drawn at establishment (deterministic).</summary>
    public double LifespanFactor { get; set; } = 1;
    /// <summary>Last evaluated habitat suitability (0..1), for inspection.</summary>
    public double LastSuitability { get; set; }
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

    public Vec2 Position => new(X, Z);

    public FloraStage Stage(FloraSpeciesDef sp) =>
        Age < sp.MaturityAge ? FloraStage.Juvenile : Age > sp.Lifespan * LifespanFactor * 0.85 ? FloraStage.Senescent : FloraStage.Mature;

    public double BiomassFraction(FloraSpeciesDef sp) => MathD.Clamp01(Biomass / sp.MaxBiomass);
    public double Radius(FloraSpeciesDef sp) => sp.MinRadius + (sp.RadiusAtMax - sp.MinRadius) * Math.Sqrt(BiomassFraction(sp));
}

/// <summary>Uniform-grid bucket index for local flora queries (avoids whole-population scans).</summary>
public sealed class SpatialBuckets<T> where T : class
{
    private readonly double _cell;
    private readonly double _ox, _oz;
    private readonly int _nx, _nz;
    private readonly List<T>[] _buckets;
    private readonly Func<T, Vec2> _pos;
    public int Count { get; private set; }

    public SpatialBuckets(double halfExtent, double cell, Func<T, Vec2> pos)
    {
        _cell = cell; _pos = pos;
        _nx = _nz = Math.Max(1, (int)Math.Ceiling(2 * halfExtent / cell));
        _ox = _oz = -_nx * cell / 2;
        _buckets = new List<T>[_nx * _nz];
        for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new List<T>(4);
    }

    private int Bucket(Vec2 p)
    {
        int i = Math.Clamp((int)Math.Floor((p.X - _ox) / _cell), 0, _nx - 1);
        int j = Math.Clamp((int)Math.Floor((p.Z - _oz) / _cell), 0, _nz - 1);
        return j * _nx + i;
    }

    public void Clear() { foreach (var b in _buckets) b.Clear(); Count = 0; }
    public void Add(T item) { _buckets[Bucket(_pos(item))].Add(item); Count++; }
    public bool Remove(T item, Vec2 at) { bool r = _buckets[Bucket(at)].Remove(item); if (r) Count--; return r; }

    /// <summary>Items within radius of p. Order: bucket scan order, then insertion order — deterministic for a
    /// deterministic insertion sequence. Callers that need id order must sort.</summary>
    public void Query(Vec2 p, double radius, List<T> into)
    {
        int i0 = Math.Clamp((int)Math.Floor((p.X - radius - _ox) / _cell), 0, _nx - 1);
        int i1 = Math.Clamp((int)Math.Floor((p.X + radius - _ox) / _cell), 0, _nx - 1);
        int j0 = Math.Clamp((int)Math.Floor((p.Z - radius - _oz) / _cell), 0, _nz - 1);
        int j1 = Math.Clamp((int)Math.Floor((p.Z + radius - _oz) / _cell), 0, _nz - 1);
        double r2 = radius * radius;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                foreach (var it in _buckets[j * _nx + i])
                    if (Vec2.DistanceSq(_pos(it), p) <= r2) into.Add(it);
    }

    /// <summary>Number of stored items examined by a query (for bounded-cost tests).</summary>
    public int CandidatesExamined(Vec2 p, double radius)
    {
        int n = 0;
        int i0 = Math.Clamp((int)Math.Floor((p.X - radius - _ox) / _cell), 0, _nx - 1);
        int i1 = Math.Clamp((int)Math.Floor((p.X + radius - _ox) / _cell), 0, _nx - 1);
        int j0 = Math.Clamp((int)Math.Floor((p.Z - radius - _oz) / _cell), 0, _nz - 1);
        int j1 = Math.Clamp((int)Math.Floor((p.Z + radius - _oz) / _cell), 0, _nz - 1);
        for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) n += _buckets[j * _nx + i].Count;
        return n;
    }
}

/// <summary>Id-ordered flora population with a spatial index.</summary>
public sealed class FloraPopulation
{
    public List<FloraIndividual> Items { get; } = new();
    private readonly Dictionary<EntityId, FloraIndividual> _byId = new();
    public SpatialBuckets<FloraIndividual> Index { get; }
    /// <summary>Changes whenever membership changes (renderer hint; not authoritative).</summary>
    public int Version { get; private set; }

    public FloraPopulation(double halfExtent) { Index = new SpatialBuckets<FloraIndividual>(halfExtent, 0.5, f => f.Position); }

    public int Count => Items.Count;
    public FloraIndividual? Get(EntityId id) => _byId.GetValueOrDefault(id);

    public void Add(FloraIndividual f)
    {
        if (Items.Count > 0 && Items[^1].Id.Value >= f.Id.Value) throw new InvalidOperationException("Flora must be added in ascending id order.");
        Items.Add(f); _byId[f.Id] = f; Index.Add(f); Version++;
    }

    public bool Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var f)) return false;
        Items.Remove(f); Index.Remove(f, f.Position); Version++;
        return true;
    }

    public void Clear() { Items.Clear(); _byId.Clear(); Index.Clear(); Version++; }

    /// <summary>Relocates an individual (creeping organisms) keeping the spatial index consistent.</summary>
    public void Move(FloraIndividual f, Vec2 to)
    {
        Index.Remove(f, f.Position);
        f.X = to.X; f.Z = to.Z;
        Index.Add(f);
    }

    public void Neighbours(Vec2 p, double radius, List<FloraIndividual> into) { into.Clear(); Index.Query(p, radius, into); }
}
