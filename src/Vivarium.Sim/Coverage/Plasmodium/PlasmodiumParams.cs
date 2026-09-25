using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// Tunables for the Physarum sheet/attractant/foraging model (docs/overhaul/growth_models.md §6.2-§6.4).
/// One instance is shared by every plasmodium of a species; species differences are just different values here.
/// </summary>
public sealed class PlasmodiumParams
{
    // ------------------------------------------------------------------ attractant field (§6.3)

    /// <summary>Coarse attractant lattice cell size, metres (2x2 fine cells).</summary>
    public double CoarseCellSize { get; init; } = CoverageSpec.CellSize * 2;

    /// <summary>Fixed Jacobi iteration count per step, warm-started from the previous step.</summary>
    public const int JacobiIterations = 8;

    /// <summary>Margin, in metres, added around the active tiles when building the attractant domain.</summary>
    public double MarginMeters { get; init; } = 1.0;

    /// <summary>Diffusion coefficient D_c, m^2/s.</summary>
    public double Dc { get; init; } = 0.02;

    /// <summary>Decay rate δ, 1/s.</summary>
    public double Delta { get; init; } = 0.15;

    /// <summary>Source coupling σ: attractant produced per unit of detritus F.</summary>
    public double Sigma { get; init; } = 1.0;

    // ------------------------------------------------------------------ foraging front (§6.4)

    /// <summary>Extension rate λ_f in the front-colonisation probability.</summary>
    public double LambdaF { get; init; } = 4.0;

    /// <summary>Exploratory term β; gives the fan shape when the attractant gradient is weak/absent.</summary>
    public double Beta { get; init; } = 0.05;

    /// <summary>
    /// Hard ceiling on the per-direction extension probability. A strong, broad gradient (many front cells all
    /// facing straight at a nearby food patch) would otherwise push p toward 1 for a whole contiguous stretch
    /// of the front at once, and a probability that close to 1 makes every independent per-direction dice roll
    /// succeed regardless of the roll — a deterministic, dead-straight wavefront rather than a foraging front.
    /// Capping p keeps the direction bias (uphill cells are still far more likely to colonise than flat ones)
    /// while guaranteeing a real per-attempt failure rate, which is what actually keeps an edge ragged.
    /// </summary>
    public double MaxExtensionProbability { get; init; } = 0.7;

    /// <summary>Wetness gate g_W = smoothstep(WMin, WOpt, moisture) (mirrors §5's g_W).</summary>
    public double WMin { get; init; } = 0.1;
    public double WOpt { get; init; } = 0.5;

    /// <summary>Mass cost of colonising one new cell.</summary>
    public double MCell { get; init; } = 1.0;

    /// <summary>Feeding rate, mass/second, taken from detritus under an occupied cell.</summary>
    public double FeedRate { get; init; } = 4.0;

    /// <summary>Initial mass pool for a newly seeded plasmodium.</summary>
    public double InitialMass { get; init; } = 20.0;

    public static double Smoothstep(double lo, double hi, double x)
    {
        if (hi <= lo) return x >= hi ? 1 : 0;
        double t = MathD.Clamp01((x - lo) / (hi - lo));
        return t * t * (3 - 2 * t);
    }

    /// <summary>g_W: wetness growth gate used by the front-extension probability.</summary>
    public double WetnessGate(double moisture) => Smoothstep(WMin, WOpt, moisture);
}
