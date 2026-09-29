namespace Vivarium.Sim.Time;

/// <summary>
/// Phase of the presentation day/night cycle. Declared in cycle order, so <c>(Phase + 1) % 5</c> is the next
/// phase and <c>Night + 1</c> wraps to <c>Dawn</c>, which in turn rolls into <c>Morning</c>.
/// </summary>
public enum DayNightPhase
{
    /// <summary>First 300 s of the cycle. Full daylight, sun still climbing.</summary>
    Morning = 0,
    /// <summary>300–660 s. Sun at its highest; the calibrated daytime look lives here.</summary>
    Afternoon = 1,
    /// <summary>660–900 s. Last 240 s of the day segment: the sun drops to the horizon and light falls off.</summary>
    Dusk = 2,
    /// <summary>900–1140 s. First 240 s of the night segment: dark, moonlit.</summary>
    Night = 3,
    /// <summary>1140–1200 s. Last 60 s of the night segment: the sun climbs back to the horizon.</summary>
    Dawn = 4,
}

/// <summary>
/// PRESENTATION-ONLY day/night clock. This is a fourth clock, independent of <see cref="SimClock"/>: it reads a
/// monotonic real-seconds source supplied by the caller and deliberately ignores <c>SimClock.Paused</c> and
/// <c>SimClock.SpeedMultiplier</c>, so the scene keeps its light even while the simulation is frozen.
///
/// Because it is wall-clock driven it is not reproducible from a save, so it is never serialised, never lives on
/// <c>VivariumWorld</c>, and never registers a <see cref="Scheduler"/> cadence. The simulation must not read it.
/// </summary>
public sealed class DayNightClock
{
    /// <summary>Phase durations in seconds. They tile the cycle exactly; see <see cref="CycleSeconds"/>.</summary>
    public const double MorningSeconds = 300.0;
    public const double AfternoonSeconds = 360.0;
    public const double DuskSeconds = 240.0;
    public const double DeepNightSeconds = 240.0;
    public const double DawnSeconds = 60.0;

    /// <summary>Daylight budget: the first 900 s of the cycle (15 real minutes).</summary>
    public const double DaySeconds = MorningSeconds + AfternoonSeconds + DuskSeconds;   // 900
    /// <summary>Night budget: the final 300 s of the cycle (5 real minutes).</summary>
    public const double NightSeconds = DeepNightSeconds + DawnSeconds;                 // 300
    /// <summary>Total cycle length: 15 real minutes of day plus 5 of night.</summary>
    public const double CycleSeconds = DaySeconds + NightSeconds;                      // 1200

    /// <summary>Second at which each phase begins, within one cycle.</summary>
    public const double MorningStart = 0.0;
    public const double AfternoonStart = MorningStart + MorningSeconds;                // 300
    public const double DuskStart = AfternoonStart + AfternoonSeconds;                  // 660
    /// <summary>End of the day segment and start of the night segment.</summary>
    public const double NightStart = DuskStart + DuskSeconds;                           // 900
    /// <summary>End of the deep-night stretch; sunrise begins here, at the end of the night segment.</summary>
    public const double DawnStart = NightStart + DeepNightSeconds;                      // 1140

    private readonly Func<double> _monotonicSeconds;

    /// <summary>Phases in cycle order, starting at <see cref="DayNightPhase.Morning"/>.</summary>
    public static readonly DayNightPhase[] PhaseOrder =
        { DayNightPhase.Morning, DayNightPhase.Afternoon, DayNightPhase.Dusk, DayNightPhase.Night, DayNightPhase.Dawn };

    /// <param name="monotonicSeconds">
    /// Monotonically non-decreasing real seconds. Injected rather than read from the environment so tests can
    /// drive the clock deterministically. The clock never calls any other time source.
    /// </param>
    public DayNightClock(Func<double> monotonicSeconds)
    {
        _monotonicSeconds = monotonicSeconds ?? throw new ArgumentNullException(nameof(monotonicSeconds));
    }

    /// <summary>Seconds into the current cycle, always in [0, <see cref="CycleSeconds"/>).</summary>
    public double Seconds
    {
        get
        {
            double t = _monotonicSeconds();
            if (!double.IsFinite(t)) t = 0;
            double w = t % CycleSeconds;
            if (w < 0) w += CycleSeconds;      // a source that starts at or before zero still yields a valid phase
            if (w == 0) w = 0;                 // normalize the -0.0 a negative multiple can produce
            return w;
        }
    }

    /// <summary>Normalized position through the cycle, in [0, 1). 0 is the start of the day segment.</summary>
    public double DayFraction => Seconds / CycleSeconds;

    /// <summary>Current phase. A pure function of the injected source; nothing else can move it.</summary>
    public DayNightPhase Phase => PhaseAt(Seconds);

    /// <summary>True for the daylight phases, false across the night segment.</summary>
    public bool IsDay => Phase is DayNightPhase.Morning or DayNightPhase.Afternoon or DayNightPhase.Dusk;

    /// <summary>Phase containing a cycle-relative second, in [0, <see cref="CycleSeconds"/>).</summary>
    public static DayNightPhase PhaseAt(double cycleSeconds)
    {
        if (cycleSeconds < AfternoonStart) return DayNightPhase.Morning;
        if (cycleSeconds < DuskStart) return DayNightPhase.Afternoon;
        if (cycleSeconds < NightStart) return DayNightPhase.Dusk;
        if (cycleSeconds < DawnStart) return DayNightPhase.Night;
        return DayNightPhase.Dawn;
    }
}
