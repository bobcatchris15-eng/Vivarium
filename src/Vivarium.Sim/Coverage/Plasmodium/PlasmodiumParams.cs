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

    /// <summary>Mass transferred from a parent cell to a newly colonised cell (§6R item 1).</summary>
    public double MCell { get; init; } = 1.0;

    /// <summary>Minimum own mass a boundary cell needs before it may spend <see cref="MCell"/> on a new
    /// neighbour (§6R item 5: "boundary cells with m &gt; m_occ"). Must exceed <see cref="MCell"/> or every
    /// colonisation would drain its parent to exactly zero.</summary>
    public double MOcc { get; init; } = 1.1;

    /// <summary>Mass below which an occupied cell vacates outright (§6R item 6, "m_min").</summary>
    public double MMin { get; init; } = 0.005;

    /// <summary>Reference mass m_ref for the local pressure term P_i = P0 * m_i / MRef (§6R item 1; the phase
    /// term A*sin(theta) is added in Pl-2).</summary>
    public double MRef { get; init; } = 3.0;

    /// <summary>Pressure gain P0 in P_i = P0 * m_i / MRef.</summary>
    public double P0 { get; init; } = 1.0;

    /// <summary>Base conductance between two 8-adjacent occupied sheet cells, used for mass transport
    /// (§6R item 1) even where no vein edge exists. Vein conductance (from <see cref="Network"/>, when supplied)
    /// adds to this in parallel rather than replacing it. High enough that mass reaches a growing front within a
    /// handful of steps — with a fixed initial mass and no shared pool, transport is now the only way mass gets
    /// from a fed interior cell out to the boundary that spends it — but not so high that it equalises an entire
    /// large sheet in one step and starves the thin bridging path a vein hierarchy needs to stay connected.</summary>
    public double SheetConductance { get; init; } = 1.5;

    /// <summary>Maintenance cost, mass/second, removed from every occupied cell every step (§6R item 1;
    /// tallied into <see cref="PlasmodiumColony.RemovedMass"/>). Zero by default until §6R item 8 (stress) wires
    /// a nonzero rate; existing lab scenarios that never configured a global mass pool upkeep stay unaffected.</summary>
    public double MaintenanceRate { get; init; } = 0.0;

    /// <summary>Extra maintenance-rate multiplier per unit of local stress (§6R item 8: light + dryness), on top
    /// of <see cref="MaintenanceRate"/>: effective rate = MaintenanceRate * (1 + StressMaintenanceGain * stress).
    /// No effect while MaintenanceRate is 0, which is why every scenario that never configured upkeep stays
    /// unaffected by this addition.</summary>
    public double StressMaintenanceGain { get; init; } = 1.0;

    /// <summary>Feeding rate, mass/second, taken from detritus under an occupied cell.</summary>
    public double FeedRate { get; init; } = 4.0;

    /// <summary>Initial total mass for a newly seeded plasmodium, split evenly across the seeded cells.</summary>
    public double InitialMass { get; init; } = 20.0;

    // ------------------------------------------------------------------ contraction phase field (§6R item 2-4)

    /// <summary>Natural angular frequency ω0 at neutral uptake/stress, rad/s. Period ≈ 100 sim-s at ω0.</summary>
    public double PhaseOmega0 { get; init; } = 2 * Math.PI / 100.0;

    /// <summary>Coupling gain K in dθ/dt = ω_i + K·Σ sin(θ_j − θ_i). Kept weak relative to Ω0 so travelling
    /// phase waves over a body of a few hundred cells do not fully synchronise within tens of cycles.</summary>
    public double PhaseCouplingK { get; init; } = 0.001;

    /// <summary>ω rise per unit of normalised local uptake (0..1, recent feeding rate over want).</summary>
    public double PhaseOmegaUptakeGain { get; init; } = 0.02;

    /// <summary>ω fall per unit of normalised local stress (0..1, dryness = 1 − moisture).</summary>
    public double PhaseOmegaStressGain { get; init; } = 0.02;

    /// <summary>Phase pressure amplitude, as a fraction of P0: P_i = (P0 + PhaseAmplitude·sin θ_i)·m_i/MRef. Kept
    /// mass-proportional (multiplying the same m_i/MRef term rather than adding a flat, mass-independent swing)
    /// so a low-mass cell's pressure swing shrinks with it and the oscillation can never demand more outflow than
    /// a bounded fraction of the cell's own mass — a bare cell cannot be pumped empty by the cycle alone.</summary>
    public double PhaseAmplitude { get; init; } = 0.5;

    /// <summary>Rectification ε (§6R item 4): a cell retains (releases less by) this fraction times its own
    /// retention score when it is the source of an outflow. Retention score is derived from how far the cell's
    /// ω currently sits above Ω0 (better local conditions raise ω the same way they raise retention), so no
    /// separate steering signal exists — food only ever acts through uptake -> ω -> retention.</summary>
    public double RetentionEpsilon { get; init; } = 0.15;

    /// <summary>Cap on the internal phase-integration sub-step, sim-seconds. The public Step API is designed to
    /// be called every 30 sim-s (§6R Cadence); this subdivides that call for a stable, deterministic Euler
    /// integration regardless of the caller's dt.</summary>
    public double PhaseMaxSubDt { get; init; } = 0.5;

    /// <summary>Mass transport is interleaved with the phase update in sub-steps of this fraction of one
    /// oscillation period (2π/Ω0), rather than applied once over the whole outer dt. A big single-shot transport
    /// call driven by an oscillating pressure term can overshoot within a step (the per-cell outflow limiter
    /// only prevents going negative *that call*, not a same-step round trip that empties then can't refill), which
    /// is what let a still, uniform sheet disintegrate into isolated dust under the phase field. Sub-stepping at a
    /// fraction of the period keeps each transport call's implied pressure change small.</summary>
    public double TransportSubStepFraction { get; init; } = 1.0 / 8.0;

    /// <summary>Time constant, seconds, for the exponential running average of a cell's mass that the m_min
    /// withdrawal check (§6R item 6) reads instead of the instantaneous value — a mid-cycle dip from shuttle
    /// streaming must not vacate a cell that recovers next half-cycle. About one oscillation period by default.</summary>
    public double MassEmaTau { get; init; } = 8.0;

    // ------------------------------------------------------------------ life cycle (§6.1, §6.6)

    /// <summary>Wetness threshold w_s: mean moisture below this for T_s drives Foraging -> Sclerotium.</summary>
    public double WS { get; init; } = 0.15;

    /// <summary>T_s, seconds of sustained dryness (mean moisture below <see cref="WS"/>) before Sclerotium.</summary>
    public double TS { get; init; } = 20.0;

    /// <summary>Mean per-cell detritus below this counts as "no food inflow" for the starvation timer.</summary>
    public double StarveDetritusThreshold { get; init; } = 0.05;

    /// <summary>T_starve, seconds of sustained food shortage before Foraging -> Migrating.</summary>
    public double TStarve { get; init; } = 15.0;

    /// <summary>T_mig, maximum seconds spent Migrating before committing to Fruiting.</summary>
    public double TMig { get; init; } = 10.0;

    /// <summary>Mass converted per fruiting body; K = round(mass / MassPerFruitingBody), clamped to available hubs.</summary>
    public double MassPerFruitingBody { get; init; } = 20.0;

    /// <summary>Minimum spacing between fruiting-body hub sites, metres (§6.6: >= 5 cm).</summary>
    public double FruitingMinSpacingMeters { get; init; } = 0.05;

    /// <summary>Seconds for a fruiting body to ripen from placement to spore release.</summary>
    public double FruitingRipenSeconds { get; init; } = 8.0;

    /// <summary>Seconds after fruiting-body placement before the remaining sheet/network collapses to residue.</summary>
    public double FruitingDecaySeconds { get; init; } = 12.0;

    /// <summary>Seconds for a residue sheet's tube width to fully fade to zero once decay starts.</summary>
    public double ResidueFadeSeconds { get; init; } = 15.0;

    public static double Smoothstep(double lo, double hi, double x)
    {
        if (hi <= lo) return x >= hi ? 1 : 0;
        double t = MathD.Clamp01((x - lo) / (hi - lo));
        return t * t * (3 - 2 * t);
    }

    /// <summary>g_W: wetness growth gate used by the front-extension probability.</summary>
    public double WetnessGate(double moisture) => Smoothstep(WMin, WOpt, moisture);
}
