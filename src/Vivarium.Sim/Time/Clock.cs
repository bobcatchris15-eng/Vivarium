using Vivarium.Sim.Content;

namespace Vivarium.Sim.Time;

/// <summary>
/// Authoritative simulation clock. Simulated time is an integer tick count times a fixed step, so it
/// cannot drift. The wall-to-sim conversion is explicit and lives here, not in scattered constants.
/// </summary>
public sealed class SimClock
{
    /// <summary>Fixed authoritative step in simulated seconds.</summary>
    public const double FixedStepSeconds = 10.0;

    /// <summary>Default rate: one real hour equals one simulated week = 168 simulated hours per real hour.</summary>
    public const double DefaultSimSecondsPerRealSecond = SimUnits.Week / SimUnits.Hour; // 168

    /// <summary>User-selectable speed multipliers around the default rate (index 3 = 1×).</summary>
    public static readonly double[] SpeedSteps = { 0.125, 0.25, 0.5, 1, 2, 4, 8, 16 };
    public const int DefaultSpeedIndex = 3;

    public long Tick { get; set; }
    /// <summary>Physical simulated time: locomotion, water flow and disturbances run on this.</summary>
    public double SimSeconds => Tick * FixedStepSeconds;
    public double SimDays => SimSeconds / SimUnits.Day;

    /// <summary>
    /// Biological time: growth, feeding, breeding, ageing and decay run on this faster clock so the terrarium
    /// visibly lives within minutes while animals still walk at a natural pace. Accumulated tick by tick
    /// (BioAcceleration may change during a run) and saved with the world.
    /// </summary>
    public double BioSeconds { get; set; }
    public double BioDays => BioSeconds / SimUnits.Day;
    /// <summary>Biological seconds per physical simulated second (world setting, see WorldDescriptor).</summary>
    public double BioAcceleration { get; set; } = 1;

    public bool Paused { get; set; }
    public int SpeedIndex { get; private set; } = DefaultSpeedIndex;
    public double SpeedMultiplier => SpeedSteps[SpeedIndex];
    public double BaseRate { get; set; } = DefaultSimSecondsPerRealSecond;
    /// <summary>Simulated seconds per real second at the current setting (0 while paused).</summary>
    public double EffectiveRate => Paused ? 0 : BaseRate * SpeedMultiplier;

    public void SetSpeedIndex(int i) => SpeedIndex = Math.Clamp(i, 0, SpeedSteps.Length - 1);
    public void Faster() => SetSpeedIndex(SpeedIndex + 1);
    public void Slower() => SetSpeedIndex(SpeedIndex - 1);

    /// <summary>Calendar view: week number (1-based), day of week (1..7), hour and minute.</summary>
    public (int Week, int Day, int Hour, int Minute) Calendar()
    {
        long totalMinutes = (long)Math.Floor(BioSeconds / 60);
        int minute = (int)(totalMinutes % 60);
        long totalHours = totalMinutes / 60;
        int hour = (int)(totalHours % 24);
        long totalDays = totalHours / 24;
        return ((int)(totalDays / 7) + 1, (int)(totalDays % 7) + 1, hour, minute);
    }

    public string CalendarText()
    {
        var (w, d, h, m) = Calendar();
        return $"Week {w}, Day {d}  {h:00}:{m:00}";
    }
}
