namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>Shared numeric constants for the aquatic coverage rules (docs/overhaul/growth_models.md §15.1, §15.2).
/// Lab-only for Aq-1: no world wiring.</summary>
public static class AquaticConst
{
    /// <summary>Real-time seconds a growth "day" (dtDays = 1) represents, used only to convert flow speeds
    /// (m/s) into a physically meaningful CFL substep count for advection.</summary>
    public const double SecondsPerDay = 86_400.0;

    /// <summary>Fraction of the CFL limit (dx / |v|) actually used per substep; keeps upwind advection stable
    /// with margin against the coarsest cell velocity in the domain.</summary>
    public const double CflSafety = 0.5;

    /// <summary>Hard cap on substeps per call, so a runaway/garbage velocity cannot make a step unbounded.</summary>
    public const int MaxSubsteps = 4096;

    /// <summary>
    /// Per-slice CFL substep budget. One upwind substep moves a cell at most <see cref="CflSafety"/> cells, so a
    /// slice whose advection is capped here displaces biomass by at most <c>SliceSubsteps*CflSafety</c> cells --
    /// that is the only reach the dense advection buffer has to cover.
    /// </summary>
    /// <remarks>
    /// The caller runs a step as many slices as the true per-tick displacement requires, each with a fresh
    /// occupied-bounds halo of <see cref="SliceHalo"/>. Drift rate is therefore unchanged, but the dense buffer is
    /// bounded by the OCCUPIED area rather than by how far the fastest water anywhere in the domain can carry mass
    /// in one tick. Sizing the halo from the whole tick's displacement (the previous behaviour) made the buffer
    /// area quadratic in flow speed and the total cost cubic: on the 8 m default preset a 9.6 mm/s current asked
    /// for 580 cells of padding, an 11.6 m halo on an 8 m island, and 5.5 billion element-touches per step.
    /// </remarks>
    public const int SliceSubsteps = 32;

    /// <summary>Cells of padding the dense advection buffer needs for one <see cref="SliceSubsteps"/> slice.</summary>
    public static int SliceHalo => (int)Math.Ceiling(SliceSubsteps * CflSafety) + 1;

    /// <summary>Absolute cap on slices per step, so a garbage velocity cannot make one step unbounded.</summary>
    public const int MaxSlices = 4096;
}

/// <summary>Inclusive rectangular domain of global coverage cells (gx, gz), the coordinate space the lab
/// scenarios drive the aquatic rules over (docs/overhaul/growth_models.md §15.4, Aq-1: no world wiring yet).</summary>
public readonly record struct GridBounds(int MinGx, int MinGz, int MaxGx, int MaxGz)
{
    public int Width => MaxGx - MinGx + 1;
    public int Height => MaxGz - MinGz + 1;
    public bool Contains(int gx, int gz) => gx >= MinGx && gx <= MaxGx && gz >= MinGz && gz <= MaxGz;

    /// <summary>Row-major index into a dense Width*Height buffer covering this domain.</summary>
    public int Index(int gx, int gz) => (gz - MinGz) * Width + (gx - MinGx);
}

/// <summary>Duckweed (<c>SurfaceFloat</c> layer) species parameters (§15.2).</summary>
public sealed record DuckweedParams(
    double GrowthRate = 0.8,   // r: logistic budding rate, per day
    double KLight = 0.20,      // half-saturation constant for light response g(L) = L / (L + KLight)
    double KNutrient = 0.20,   // half-saturation constant for nutrient response g(N) = N / (N + KNutrient)
    double MaxDensity = 1.0,   // rho_max, normalised to 1 (fronds/cm^2 collapsed to a coverage fraction)
    double WindX = 0.0,        // gentle deterministic wind added to surface flow for advection, m/s
    double WindZ = 0.0
);

/// <summary>Benthic/epilithic algae film (<c>AlgaeBed</c> layer) parameters (§15.1).</summary>
public sealed record AlgaeBedParams(
    double GrowthRate = 0.5,          // logistic growth-rate constant, per day
    double LightAtten = 1.8,          // Beer-Lambert absorption coefficient, per metre of depth
    double LightHalfSat = 0.15,       // half-saturation constant for light response
    double NutrientHalfSat = 0.2,     // half-saturation constant for nutrient response
    double ScourFlowThreshold = 0.05, // flow speed (m/s) above which scour removes film
    double ScourRate = 2.0,           // per day, per unit speed above threshold
    double GrazeRate = 0.0,           // grazing hook (Aq-2 wires fauna consumption); unused here
    double DetachThickness = 0.45,    // film density above which bubbles start lifting it off the bed
    double DetachRate = 0.08          // per day, fraction of the excess above DetachThickness that detaches
);

/// <summary>Floating filamentous algae mat (<c>AlgaeFloat</c> layer) parameters (§15.1).</summary>
public sealed record AlgaeFloatParams(
    double StillFlowThreshold = 0.06, // mats only persist/grow where flow is below this speed (m/s)
    double GrowthRate = 0.3,          // slow autonomous growth of an established mat, per day
    double NutrientHalfSat = 0.3
);
