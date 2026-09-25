namespace Vivarium.Sim.Coverage.Rules;

/// <summary>Growth form for a lichen species (docs/overhaul/growth_models.md §5).</summary>
public enum LichenForm : byte
{
    /// <summary>Plain Eden growth: round, rough-edged, banded by age (§5.2).</summary>
    Crustose,
    /// <summary>Tip-biased growth toward exposed cells: lobed rosettes (§5.3).</summary>
    Foliose,
    /// <summary>Cushion-style dome footprint; upright branching is render-derived (§5.4).</summary>
    Fruticose,
}

/// <summary>
/// Per-species parameters for <see cref="LichenRules"/> (docs/overhaul/growth_models.md §5, §8 "lichen" block).
/// A plain data holder — no behaviour — so growth-lab scenarios can build synthetic species sets without content.
/// </summary>
public sealed class LichenParams
{
    /// <summary>Occupant slot (<see cref="CoverageTile.Occ"/> value, 1-based; 0 is reserved for "empty").</summary>
    public byte OccSlot { get; init; }

    public LichenForm Form { get; init; } = LichenForm.Crustose;

    /// <summary>Substrates this species can colonise (§5.1). Never includes LooseSoil or Water.</summary>
    public CoverageSubstrate[] Substrates { get; init; } =
    {
        CoverageSubstrate.Rock, CoverageSubstrate.Log, CoverageSubstrate.Bark, CoverageSubstrate.StableSoil,
    };

    /// <summary>Rate multiplier applied on Gravel, on top of the base rate (§5.1). 0 disallows Gravel entirely.</summary>
    public double GravelRateMultiplier { get; init; } = 0.3;

    /// <summary>Lateral/front rate lambda (world units per bio-day, §8); very low for crustose/fruticose Eden growth,
    /// and the tip-bias coefficient for foliose (§5.3).</summary>
    public double Lateral { get; init; } = 0.02;

    /// <summary>Foliose tip-bias exponent gamma applied to local openness (§5.3). Unused by other forms.</summary>
    public double TipBiasGamma { get; init; } = 4.0;

    /// <summary>Radius (cells) of the openness disc sampled for foliose tip bias (§5.3).</summary>
    public int OpennessRadius { get; init; } = 3;

    /// <summary>Minimum occupied same-species 8-neighbours (on the snapshot) an empty cell needs before it is even
    /// considered for foliose colonisation (§5.3). Keeps lobes solid, contiguous fingers instead of porous speckle;
    /// unused by crustose/fruticose, whose Eden front is already contiguous by construction.</summary>
    public int MinNeighboursToColonise { get; init; } = 3;

    /// <summary>Foliose centre senescence age threshold, in bio-days (§5.3). Ignored by crustose/fruticose.</summary>
    public double CentreDeathDays { get; init; } = 30;

    /// <summary>D2E (cells) beyond which an old cell is eligible for centre death (§5.3).</summary>
    public int CentreDeathMinD2E { get; init; } = 3;

    /// <summary>Biomass decay per bio-day once Dead (§4.1-style decay, applied to lichen too).</summary>
    public double DeadDecayPerDay { get; init; } = 0.5;

    // -------- water balance (§3), private per-species copy --------
    public double KWet { get; init; } = 3.0;
    public double KDry { get; init; } = 0.4;
    public double WMin { get; init; } = 0.15;
    public double WOpt { get; init; } = 0.55;

    // -------- light (§3) --------
    public double KLight { get; init; } = 0.25;

    /// <summary>Initial biomass fraction on colonisation.</summary>
    public double SeedBiomass { get; init; } = 0.12;

    /// <summary>Species growth-form maximum biomass (0..1 fraction of species max, per §1.2).</summary>
    public double MaxBiomass { get; init; } = 1.0;

    /// <summary>Small noise exponent softening the Eden front for crustose/fruticose (§5.2); 1 = no softening.</summary>
    public double EdenNoiseExponent { get; init; } = 1.0;

    public bool AllowsSubstrate(CoverageSubstrate s)
    {
        foreach (var allowed in Substrates) if (allowed == s) return true;
        return false;
    }
}
