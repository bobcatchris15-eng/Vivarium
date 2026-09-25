using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>Inclusive range a per-individual parameter is sampled from.</summary>
public readonly record struct FloatRange(double Min, double Max)
{
    public static FloatRange Fixed(double v) => new(v, v);
    public double Sample(Rng rng) => Min + (Max - Min) * rng.NextDouble();

    public static implicit operator FloatRange(double v) => Fixed(v);
}

/// <summary>
/// Seeded per-individual jitter: deterministic sampling of species-defined parameter ranges so clones vary
/// without touching the sim's own RNG streams.
/// </summary>
public static class Variation
{
    /// <summary>Deterministic RNG for one individual's form variation, independent of any other stream.</summary>
    public static Rng SeedFor(ulong worldSeed, string speciesId, ulong individualId) =>
        Rng.Keyed(worldSeed, "form.variation." + speciesId, individualId);

    /// <summary>Samples a fully-formed parameter record from a range-builder, given a stable per-individual seed.</summary>
    public static T Sample<T>(ulong worldSeed, string speciesId, ulong individualId, Func<Rng, T> build) =>
        build(SeedFor(worldSeed, speciesId, individualId));
}
