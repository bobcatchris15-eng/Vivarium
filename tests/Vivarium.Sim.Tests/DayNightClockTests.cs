using System.Reflection;
using Vivarium.Sim.Time;

namespace Vivarium.Sim.Tests;

/// <summary>
/// The day/night clock is PRESENTATION ONLY: a fourth clock beside SimClock's physical, biological and
/// frame-time clocks, driven by an injected monotonic source, immune to SimClock's pause and speed settings.
/// </summary>
[Trait("Suite", "Time")]
public class DayNightClockTests
{
    private static readonly double[] Boundaries =
    {
        DayNightClock.MorningStart, DayNightClock.AfternoonStart, DayNightClock.DuskStart,
        DayNightClock.NightStart, DayNightClock.DawnStart,
    };

    private static readonly DayNightPhase[] DayPhases =
        { DayNightPhase.Morning, DayNightPhase.Afternoon, DayNightPhase.Dusk };
    private static readonly DayNightPhase[] NightPhases = { DayNightPhase.Night, DayNightPhase.Dawn };

    /// <summary>A clock whose injected source is frozen at <paramref name="seconds"/>.</summary>
    private static DayNightClock At(double seconds)
    {
        double t = seconds;
        return new DayNightClock(() => t);
    }

    private static double MidpointOf(DayNightPhase phase) => phase switch
    {
        DayNightPhase.Morning => (DayNightClock.MorningStart + DayNightClock.AfternoonStart) / 2,
        DayNightPhase.Afternoon => (DayNightClock.AfternoonStart + DayNightClock.DuskStart) / 2,
        DayNightPhase.Dusk => (DayNightClock.DuskStart + DayNightClock.NightStart) / 2,
        DayNightPhase.Night => (DayNightClock.NightStart + DayNightClock.DawnStart) / 2,
        _ => (DayNightClock.DawnStart + DayNightClock.CycleSeconds) / 2,
    };

    [Fact]
    public void CycleIsFifteenRealMinutesOfDayAndFiveOfNight()
    {
        Assert.Equal(1200.0, DayNightClock.CycleSeconds);
        Assert.Equal(900.0, DayNightClock.DaySeconds);
        Assert.Equal(300.0, DayNightClock.NightSeconds);
        Assert.Equal(DayNightClock.DaySeconds + DayNightClock.NightSeconds, DayNightClock.CycleSeconds);
        Assert.Equal(DayNightClock.DaySeconds, DayNightClock.NightStart);                                // dusk ends the day segment
        Assert.Equal(DayNightClock.CycleSeconds, DayNightClock.DawnStart + DayNightClock.DawnSeconds);  // dawn ends the night segment
    }

    [Fact]
    public void PhaseDurationsTileTheCycleWithoutGapsOrOverlap()
    {
        Assert.Equal(0.0, Boundaries[0]);
        Assert.Equal(DayNightClock.CycleSeconds, Boundaries[^1] + DayNightClock.DawnSeconds);
        for (int i = 1; i < Boundaries.Length; i++)
            Assert.True(Boundaries[i] > Boundaries[i - 1], $"boundary {i} ({Boundaries[i]}) must follow {Boundaries[i - 1]}");
        // Day segment is morning+afternoon+dusk; night segment is night+dawn. Note DawnStart is an absolute
        // offset into the cycle, not a duration, so the night segment is sized by its two phase lengths and
        // is shown to run from NightStart to the end of the cycle.
        Assert.Equal(DayNightClock.DaySeconds, DayNightClock.DuskStart + DayNightClock.DuskSeconds);
        Assert.Equal(DayNightClock.NightSeconds, DayNightClock.DeepNightSeconds + DayNightClock.DawnSeconds);
        Assert.Equal(DayNightClock.CycleSeconds, DayNightClock.NightStart + DayNightClock.NightSeconds);
        Assert.Equal(5, DayNightClock.PhaseOrder.Length);
    }

    [Fact]
    public void PhaseSwitchesExactlyAtEveryBoundary()
    {
        for (int i = 0; i < Boundaries.Length; i++)
        {
            double b = Boundaries[i];
            Assert.Equal(DayNightClock.PhaseOrder[i], At(b).Phase);

            // The instant before the boundary belongs to the previous phase. Cycle second 0 has no positive
            // predecessor, so probe the source just before zero and let the wrap land in the last phase.
            double before = b == 0 ? -1e-6 : b - 1e-6;
            var previous = DayNightClock.PhaseOrder[(i + Boundaries.Length - 1) % Boundaries.Length];
            Assert.Equal(previous, At(before).Phase);
        }
    }

    [Fact]
    public void CycleRunsDawnThenMorningThenAfternoonThenDuskThenNightAndLoopsIntoMorning()
    {
        var seen = new List<DayNightPhase>();
        DayNightPhase? last = null;
        for (double t = 0; t < DayNightClock.CycleSeconds; t += 0.25)
        {
            var p = At(t).Phase;
            if (last != p) { seen.Add(p); last = p; }
        }

        // Sampling from t=0 starts mid-order; rotating to dawn gives the whole cycle with dawn first.
        int i = seen.IndexOf(DayNightPhase.Dawn);
        Assert.True(i >= 0, "one full cycle must include dawn");
        var fromDawn = seen.Skip(i).Concat(seen.Take(i)).ToList();
        Assert.Equal(new[]
        {
            DayNightPhase.Dawn, DayNightPhase.Morning, DayNightPhase.Afternoon,
            DayNightPhase.Dusk, DayNightPhase.Night,
        }, fromDawn);

        // Dawn is last, so the cycle rolls dawn straight back into morning.
        Assert.Equal(DayNightPhase.Morning, At(DayNightClock.CycleSeconds).Phase);
    }

    [Fact]
    public void DaySegmentCarriesDuskAndNightSegmentCarriesDawn()
    {
        for (double t = 0; t < DayNightClock.NightStart; t += 0.25)
            Assert.Contains(At(t).Phase, DayPhases);
        for (double t = DayNightClock.NightStart; t < DayNightClock.CycleSeconds; t += 0.25)
            Assert.Contains(At(t).Phase, NightPhases);

        Assert.True(At(0).IsDay);
        Assert.True(At(DayNightClock.DuskStart).IsDay);
        Assert.False(At(DayNightClock.NightStart).IsDay);
        Assert.False(At(DayNightClock.DawnStart).IsDay);
    }

    [Fact]
    public void DayFractionStaysNormalizedAndWrapsPastTheCycleLength()
    {
        Assert.Equal(0.0, At(0).DayFraction);
        for (double t = -3 * DayNightClock.CycleSeconds; t <= 5 * DayNightClock.CycleSeconds; t += 7.3)
        {
            var c = At(t);
            Assert.InRange(c.DayFraction, 0.0, 1.0);
            Assert.True(c.DayFraction < 1.0, $"day fraction must be a half-open interval (at {t})");
            Assert.InRange(c.Seconds, 0.0, DayNightClock.CycleSeconds);

            // A whole number of cycles either way lands on the same phase.
            Assert.Equal(c.Phase, At(t + DayNightClock.CycleSeconds).Phase);
            Assert.Equal(c.Phase, At(t - DayNightClock.CycleSeconds).Phase);
        }
    }

    [Fact]
    public void NonFiniteAndNegativeSourcesStillProduceAValidPhase()
    {
        Assert.Equal(DayNightPhase.Morning, new DayNightClock(() => double.NaN).Phase);
        Assert.Equal(DayNightPhase.Morning, new DayNightClock(() => double.PositiveInfinity).Phase);
        Assert.Equal(DayNightPhase.Morning, new DayNightClock(() => double.NegativeInfinity).Phase);
        // A source that starts before zero is a legal case, not a crash.
        Assert.Equal(DayNightPhase.Dawn, At(-1e-6).Phase);
        Assert.InRange(At(-5000).Seconds, 0.0, DayNightClock.CycleSeconds);
        Assert.Throws<ArgumentNullException>(() => new DayNightClock(null!));
    }

    [Fact]
    public void PhaseIsAPureFunctionOfTheInjectedSource()
    {
        foreach (var phase in DayNightClock.PhaseOrder)
        {
            double mid = MidpointOf(phase);
            var a = At(mid);
            var b = At(mid);
            Assert.Equal(phase, a.Phase);
            Assert.Equal(a.Phase, b.Phase);
            Assert.Equal(a.Seconds, b.Seconds);
            Assert.Equal(mid / DayNightClock.CycleSeconds, a.DayFraction, 12);
        }
    }

    [Fact]
    public void NothingButTheSourceMovesTheClock()
    {
        double t = 0;
        var c = new DayNightClock(() => t);
        Assert.Equal(DayNightPhase.Morning, c.Phase);

        var seen = new List<DayNightPhase> { c.Phase };
        for (t = 0; t <= DayNightClock.CycleSeconds; t += 0.5)
        {
            var p = c.Phase;
            if (seen[^1] != p) seen.Add(p);
        }
        // Stepping the source walks every phase once and returns to the start.
        Assert.Equal(DayNightPhase.Morning, c.Phase);
        Assert.Equal(new[]
        {
            DayNightPhase.Morning, DayNightPhase.Afternoon, DayNightPhase.Dusk,
            DayNightPhase.Night, DayNightPhase.Dawn, DayNightPhase.Morning,
        }, seen);
    }

    [Fact]
    public void ClockExposesNoSimClockDependency()
    {
        var types = typeof(DayNightClock)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m switch
            {
                FieldInfo f => f.FieldType,
                PropertyInfo p => p.PropertyType,
                _ => null,
            })
            .OfType<Type>()
            .ToList();
        Assert.DoesNotContain(typeof(SimClock), types);
    }

    [Fact]
    public void PhaseIsUnaffectedBySimulationPauseAndSpeed()
    {
        var w = TestUtil.FlatWorld();
        var c = At(DayNightClock.DuskStart);
        var expected = c.Phase;
        Assert.Equal(DayNightPhase.Dusk, expected);
        Assert.Equal(0.55, c.DayFraction, 9);

        // Paused: the simulation stops dead, the presentation clock does not notice.
        w.Clock.Paused = true;
        long tickAtPause = w.Clock.Tick;
        for (int i = 0; i < 120; i++) w.Scheduler.Advance(1 / 60.0);
        Assert.True(w.Clock.Paused);
        Assert.Equal(0.0, w.Clock.EffectiveRate);
        Assert.Equal(tickAtPause, w.Clock.Tick);
        Assert.Equal(expected, c.Phase);
        Assert.Equal(0.55, c.DayFraction, 9);

        // Full speed: simulated time barrels past, the presentation clock does not move either.
        w.Clock.Paused = false;
        for (int i = 0; i < 4; i++) w.Clock.Faster();
        Assert.Equal(SimClock.SpeedSteps[^1], w.Clock.SpeedMultiplier);
        for (int i = 0; i < 120; i++) w.Scheduler.Advance(1 / 60.0);
        Assert.True(w.Clock.Tick > tickAtPause, "the simulation must actually have advanced");
        Assert.True(w.Clock.SimSeconds > 0);
        Assert.Equal(expected, c.Phase);
        Assert.Equal(0.55, c.DayFraction, 9);

        // The two clocks are decoupled: simulated seconds and presentation seconds are not the same quantity.
        Assert.NotEqual(w.Clock.SimSeconds, c.Seconds);
    }
}
