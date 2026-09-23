using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;

namespace Vivarium.Sim.Fauna;

public enum FaunaLifeStage { Juvenile, Adult }

/// <summary>Common persistent state for every animal. Contains no render references.</summary>
public sealed class FaunaIndividual
{
    public EntityId Id { get; set; }
    public string SpeciesId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>Heading in the XZ plane (radians).</summary>
    public double Heading { get; set; }
    /// <summary>Vertical swim direction for aquatic animals (-1..1).</summary>
    public double Pitch { get; set; }
    public double Age { get; set; }
    public double Energy { get; set; }
    public FaunaLifeStage Stage { get; set; }
    public double LifespanFactor { get; set; } = 1;
    public double ReproCooldownUntil { get; set; }
    public EntityId GenomeId { get; set; }
    public EntityId ParentA { get; set; }
    public EntityId ParentB { get; set; }
    /// <summary>Counter for keyed randomness (increments on each stochastic decision batch).</summary>
    public ulong RngCounter { get; set; }
    /// <summary>Sim time until which the animal flees from DisturbFrom.</summary>
    public double DisturbedUntil { get; set; }
    public double DisturbX { get; set; }
    public double DisturbZ { get; set; }
    /// <summary>True while held by the grab tool: exempt from locomotion, still ages and metabolises.</summary>
    public bool Grabbed { get; set; }
    public int Offspring { get; set; }
    public double LastSuitability { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Vec3 Position { get => new(X, Y, Z); set { X = value.X; Y = value.Y; Z = value.Z; } }
    public Vec2 PositionXZ => new(X, Z);
}

public sealed class FaunaPopulation
{
    public List<FaunaIndividual> Items { get; } = new();
    private readonly Dictionary<EntityId, FaunaIndividual> _byId = new();
    public SpatialBuckets<FaunaIndividual> Index { get; }
    public int Version { get; private set; }
    private readonly Dictionary<string, int> _countBySpecies = new(StringComparer.Ordinal);

    public FaunaPopulation(double halfExtent) { Index = new SpatialBuckets<FaunaIndividual>(halfExtent, 0.5, f => f.PositionXZ); }

    public int Count => Items.Count;
    public FaunaIndividual? Get(EntityId id) => _byId.GetValueOrDefault(id);
    public int CountOf(string species) => _countBySpecies.GetValueOrDefault(species);

    public void Add(FaunaIndividual f)
    {
        if (Items.Count > 0 && Items[^1].Id.Value >= f.Id.Value) throw new InvalidOperationException("Fauna must be added in ascending id order.");
        Items.Add(f); _byId[f.Id] = f; Index.Add(f); Version++;
        _countBySpecies[f.SpeciesId] = CountOf(f.SpeciesId) + 1;
    }

    public bool Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var f)) return false;
        Items.Remove(f); Version++;
        _countBySpecies[f.SpeciesId] = CountOf(f.SpeciesId) - 1;
        RebuildIndex();
        return true;
    }

    public void RemoveMany(HashSet<EntityId> ids)
    {
        if (ids.Count == 0) return;
        Items.RemoveAll(f => ids.Contains(f.Id));
        foreach (var id in ids) _byId.Remove(id);
        _countBySpecies.Clear();
        foreach (var f in Items) _countBySpecies[f.SpeciesId] = CountOf(f.SpeciesId) + 1;
        Version++;
        RebuildIndex();
    }

    public void Clear() { Items.Clear(); _byId.Clear(); Index.Clear(); _countBySpecies.Clear(); Version++; }

    /// <summary>Rebuild in id order so neighbour query order is deterministic and position-current.</summary>
    public void RebuildIndex() { Index.Clear(); foreach (var f in Items) Index.Add(f); }

    public void Neighbours(Vec2 p, double radius, List<FaunaIndividual> into) { into.Clear(); Index.Query(p, radius, into); }
}
