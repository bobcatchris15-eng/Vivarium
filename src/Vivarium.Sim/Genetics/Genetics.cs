using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Genetics;

/// <summary>
/// Inheritable genome: normalized trait values aligned with the species' enabled trait list
/// (<see cref="FaunaSpeciesDef.Traits"/>). Values are always finite and within each trait's bounds.
/// </summary>
public sealed class Genome
{
    public EntityId Id { get; set; }
    public string SpeciesId { get; set; } = "";
    public double[] Traits { get; set; } = Array.Empty<double>();
    public int Generation { get; set; }
    /// <summary>Number of mutation events in this genome's own creation (0 or 1).</summary>
    public int Mutations { get; set; }
    /// <summary>Index of the mutated trait (-1 when not mutated).</summary>
    public int MutatedTrait { get; set; } = -1;

    public double Get(FaunaSpeciesDef sp, string traitId, double fallback = 0.5)
    {
        int i = sp.TraitIndex(traitId);
        return i >= 0 && i < Traits.Length ? Traits[i] : fallback;
    }
}

/// <summary>Visible and simulation-relevant values derived from a genome. Never stored; always recomputed.</summary>
public readonly record struct Phenotype(
    double BodySize,          // m, true scale
    double SizeFactor,        // body size / mid-range size
    double HueShift,          // -0.12 .. +0.12 (fraction of the colour wheel)
    double OrnamentDensity,   // 0..1, marking density
    double PatternStrength,   // 0..1, marking contrast
    double AppendageScale,    // 0.7 .. 1.3, antennae / tail / fins
    double MetabolicFactor)   // multiplier on basal metabolism (0.85 .. 1.15)
{
    public static Phenotype From(FaunaSpeciesDef sp, Genome g)
    {
        double size = g.Get(sp, "size");
        double body = sp.SizeMin + (sp.SizeMax - sp.SizeMin) * size;
        double mid = (sp.SizeMin + sp.SizeMax) / 2;
        return new Phenotype(
            BodySize: body,
            SizeFactor: body / mid,
            HueShift: (g.Get(sp, "hue_shift") - 0.5) * 0.24,
            OrnamentDensity: g.Get(sp, "ornament_density"),
            PatternStrength: g.Get(sp, "pattern_strength"),
            AppendageScale: 0.7 + 0.6 * g.Get(sp, "appendage_length"),
            MetabolicFactor: 1.15 - 0.3 * g.Get(sp, "metabolic_efficiency"));
    }

    /// <summary>Energy-scaling of metabolism with body size (Kleiber-like exponent 0.75 on mass ∝ size³).</summary>
    public double MetabolicScale => Math.Pow(SizeFactor, 2.25) * MetabolicFactor;
    public double MassScale => SizeFactor * SizeFactor * SizeFactor;
    /// <summary>Larger bodies move somewhat faster.</summary>
    public double SpeedScale => Math.Pow(SizeFactor, 0.5);
}

/// <summary>
/// Inheritance rules.
/// 1. Base traits are the exact arithmetic midpoint of the two parents' values (asexual: copy of the parent).
/// 2. Mutation semantics: each offspring independently undergoes at most ONE mutation event, with probability
///    <see cref="GeneticsConfig.MutationProbability"/> (default 0.10). An event selects one enabled trait uniformly
///    and adds Gaussian noise with σ = species mutationMagnitude.
/// 3. Results are clamped to the trait's [min, max]; non-finite values fall back to the trait default.
/// All randomness is keyed by (world seed, "genetics.offspring", offspring genome id), so results do not depend
/// on unrelated consumption or iteration order.
/// </summary>
public static class Inheritance
{
    public const string Stream = "genetics.offspring";

    public static double[] Midpoint(double[] a, double[]? b)
    {
        var t = new double[a.Length];
        for (int i = 0; i < a.Length; i++) t[i] = b == null ? a[i] : (a[i] + b[i]) / 2;
        return t;
    }

    public static Genome CreateOffspring(FaunaSpeciesDef sp, GeneticsConfig cfg, Genome a, Genome? b, EntityId id, ulong worldSeed)
    {
        if (a.SpeciesId != sp.Id || (b != null && b.SpeciesId != sp.Id)) throw new ArgumentException("Parents must belong to the offspring's species.");
        var traits = Midpoint(a.Traits, b?.Traits);
        var rng = Rng.Keyed(worldSeed, Stream, id.Value);
        int mutated = -1;
        if (rng.NextDouble() < cfg.MutationProbability && traits.Length > 0)
        {
            mutated = rng.NextInt(traits.Length);
            traits[mutated] += rng.NextGaussian() * sp.MutationMagnitude;
        }
        Clamp(sp, cfg, traits);
        return new Genome
        {
            Id = id, SpeciesId = sp.Id, Traits = traits,
            Generation = Math.Max(a.Generation, b?.Generation ?? 0) + 1,
            Mutations = mutated >= 0 ? 1 : 0, MutatedTrait = mutated,
        };
    }

    /// <summary>Founder genome for introduced / starter individuals: defaults plus small seeded variation.</summary>
    public static Genome CreateFounder(FaunaSpeciesDef sp, GeneticsConfig cfg, EntityId id, ulong worldSeed)
    {
        var rng = Rng.Keyed(worldSeed, "genetics.founder", id.Value);
        var traits = new double[sp.Traits.Count];
        for (int i = 0; i < traits.Length; i++)
        {
            var def = cfg.Get(sp.Traits[i])!;
            traits[i] = def.Default + rng.NextGaussian() * sp.InitialVariance;
        }
        Clamp(sp, cfg, traits);
        return new Genome { Id = id, SpeciesId = sp.Id, Traits = traits, Generation = 0 };
    }

    public static void Clamp(FaunaSpeciesDef sp, GeneticsConfig cfg, double[] traits)
    {
        for (int i = 0; i < traits.Length; i++)
        {
            var def = cfg.Get(sp.Traits[i]);
            double lo = def?.Min ?? 0, hi = def?.Max ?? 1, d = def?.Default ?? 0.5;
            traits[i] = double.IsFinite(traits[i]) ? MathD.Clamp(traits[i], lo, hi) : d;
        }
    }
}

/// <summary>Genome storage keyed by id. Iteration for persistence is id-ordered.</summary>
public sealed class GenomeBank
{
    private readonly Dictionary<EntityId, Genome> _genomes = new();
    public int Count => _genomes.Count;
    public void Add(Genome g) => _genomes[g.Id] = g;
    public Genome? Get(EntityId id) => _genomes.GetValueOrDefault(id);
    public bool Contains(EntityId id) => _genomes.ContainsKey(id);
    public bool Remove(EntityId id) => _genomes.Remove(id);
    public IEnumerable<Genome> Ordered() => _genomes.Values.OrderBy(g => g.Id.Value);
    public void Clear() => _genomes.Clear();
}

public sealed class LineageRecord
{
    /// <summary>The individual this record describes.</summary>
    public EntityId Id { get; set; }
    public string SpeciesId { get; set; } = "";
    public EntityId GenomeId { get; set; }
    public EntityId ParentA { get; set; }
    public EntityId ParentB { get; set; }
    public long BirthTick { get; set; }
    /// <summary>-1 while alive.</summary>
    public long DeathTick { get; set; } = -1;
    public string DeathCause { get; set; } = "";
    /// <summary>Biological day of death (-1 while alive or for records saved before the biological clock).</summary>
    public double DeathDay { get; set; } = -1;
    public int Generation { get; set; }
    public bool Alive => DeathTick < 0;
}

/// <summary>
/// Parent–child links that outlive the individuals. Records of dead individuals are kept while they are
/// within <see cref="RetainGenerations"/> ancestry steps of a living individual (bounded memory).
/// </summary>
public sealed class LineageBook
{
    private readonly Dictionary<EntityId, LineageRecord> _records = new();
    public int RetainGenerations { get; set; } = 6;
    public int Count => _records.Count;

    public void Add(LineageRecord r) => _records[r.Id] = r;
    public LineageRecord? Get(EntityId id) => _records.GetValueOrDefault(id);
    public IEnumerable<LineageRecord> Ordered() => _records.Values.OrderBy(r => r.Id.Value);
    public void Clear() => _records.Clear();

    public void MarkDead(EntityId id, long tick, string cause, double bioDay = -1)
    {
        if (_records.TryGetValue(id, out var r) && r.Alive) { r.DeathTick = tick; r.DeathCause = cause; r.DeathDay = bioDay; }
    }

    public IEnumerable<LineageRecord> Parents(EntityId id)
    {
        var r = Get(id);
        if (r == null) yield break;
        if (!r.ParentA.IsNone && Get(r.ParentA) is { } a) yield return a;
        if (!r.ParentB.IsNone && r.ParentB != r.ParentA && Get(r.ParentB) is { } b) yield return b;
    }

    public List<(LineageRecord Record, int Depth)> Ancestors(EntityId id, int maxDepth = 4)
    {
        var result = new List<(LineageRecord, int)>();
        var seen = new HashSet<EntityId>();
        var frontier = new List<EntityId> { id };
        for (int depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<EntityId>();
            foreach (var f in frontier)
                foreach (var p in Parents(f))
                    if (seen.Add(p.Id)) { result.Add((p, depth)); next.Add(p.Id); }
            frontier = next;
        }
        return result;
    }

    public List<LineageRecord> Children(EntityId id) =>
        _records.Values.Where(r => r.ParentA == id || r.ParentB == id).OrderBy(r => r.Id.Value).ToList();

    /// <summary>Removes dead records that no living individual descends from within RetainGenerations. Returns removed ids.</summary>
    public List<EntityId> Prune()
    {
        var keep = new HashSet<EntityId>();
        foreach (var r in _records.Values)
        {
            if (!r.Alive) continue;
            keep.Add(r.Id);
            foreach (var (a, _) in Ancestors(r.Id, RetainGenerations)) keep.Add(a.Id);
        }
        var removed = _records.Keys.Where(k => !keep.Contains(k)).OrderBy(k => k.Value).ToList();
        foreach (var k in removed) _records.Remove(k);
        return removed;
    }
}
