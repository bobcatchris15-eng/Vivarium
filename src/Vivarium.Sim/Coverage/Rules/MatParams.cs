namespace Vivarium.Sim.Coverage.Rules;

/// <summary>Growth-form height rule (docs/overhaul/growth_models.md §4.5).</summary>
public enum MatHeightForm
{
    /// <summary>h = h_max * B (carpet).</summary>
    Flat,
    /// <summary>h = h_max * B * wetness (wetbank).</summary>
    Wet,
    /// <summary>h = h_max * B * (1 - e^(-D2E/l)) (cushion, sphagnum).</summary>
    Dome,
}

/// <summary>
/// Moss ("mat") species parameters (docs/overhaul/growth_models.md §4, §8 "mat" content block). Immutable,
/// keyed into a <see cref="CoverageLayer"/> by <see cref="OccupantId"/> (the layer's Occ byte).
/// </summary>
public sealed record MatParams(
    byte OccupantId,
    string Name,
    MatHeightForm HeightForm,
    double Lateral,             // lambda_s: lateral spread rate
    double MaxHeightM,
    double GrowthRate,          // r: logistic growth-rate constant
    double KWet,
    double KDry,
    double WMin,
    double WOpt,
    double KLight,
    double LightMax = 1.0,      // photoinhibition cap; light above this reduces g_L
    double DormBrownDays = 2,
    double DormDeathDays = 8,
    double SporeRate = 0.001,
    double MoistureFeedback = 0.0,   // phi: sphagnum-style moisture feedback
    double HardMinMoisture = 0.0,    // suitability gate below which the cell is never colonised
    double SeedBiomass = 0.05,
    double DomeLength = 6.0          // l in cells for the dome height profile
);
