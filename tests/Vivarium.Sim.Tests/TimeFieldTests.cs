using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.Observation;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Time")]
public class TimeTests
{
    [Fact] // t-039
    public void DefaultRateIsOneWeekPerRealHour()
    {
        Assert.Equal(168.0, SimClock.DefaultSimSecondsPerRealSecond);
        var c = new SimClock();
        Assert.Equal(168.0, c.EffectiveRate);
        var s = new Scheduler(c) { MaxTicksPerAdvance = int.MaxValue, WallBudgetMs = 0 };
        for (int i = 0; i < 3600; i++) s.Advance(1.0);            // one real hour in 1 s frames
        Assert.Equal(7.0, c.SimDays, 3);
        Assert.Equal((1, 1, 0, 0), new SimClock().Calendar());
    }

    [Fact] // t-040
    public void PauseAdvancesZeroSimTime()
    {
        var w = TestUtil.FlatWorld();
        var host = new SimHost(w);
        host.Advance(0.5);
        long t0 = w.Clock.Tick;
        w.Clock.Paused = true;
        for (int i = 0; i < 100; i++) Assert.Equal(0, host.Advance(1 / 60.0));
        Assert.Equal(t0, w.Clock.Tick);
        w.Clock.Paused = false;
        host.Advance(0.5);
        Assert.True(w.Clock.Tick > t0);
    }

    [Fact] // t-041
    public void SpeedChangesAreBoundedAndMonotonic()
    {
        var w = TestUtil.FlatWorld();
        var host = new SimHost(w);
        long last = 0;
        for (int i = 0; i < 40; i++)
        {
            if (i % 5 == 0) w.Clock.Faster(); else if (i % 7 == 0) w.Clock.Slower();
            host.Advance(0.25);
            Assert.True(w.Clock.Tick >= last);
            last = w.Clock.Tick;
        }
        for (int i = 0; i < 20; i++) w.Clock.Faster();
        Assert.Equal(SimClock.SpeedSteps[^1], w.Clock.SpeedMultiplier);
        for (int i = 0; i < 20; i++) w.Clock.Slower();
        Assert.Equal(SimClock.SpeedSteps[0], w.Clock.SpeedMultiplier);
        Assert.True(w.Clock.SpeedMultiplier > 0);
    }

    [Fact] // t-042
    public void DifferentFrameSequencesProduceIdenticalState()
    {
        var a = TestUtil.DefaultWorld(); var b = TestUtil.DefaultWorld();
        a.Scheduler.MaxTicksPerAdvance = b.Scheduler.MaxTicksPerAdvance = int.MaxValue;
        a.Scheduler.WallBudgetMs = b.Scheduler.WallBudgetMs = 0;
        const long target = 600;
        var rngFrames = Rng.Stream(5, "frames");
        while (a.Clock.Tick < target) a.Scheduler.Advance(1 / 64.0);                                      // steady 64 fps
        while (b.Clock.Tick < target) b.Scheduler.Advance(rngFrames.NextInt(9) / 64.0);                  // jittery frames
        a.Scheduler.RunUntilTick(Math.Max(a.Clock.Tick, b.Clock.Tick));
        b.Scheduler.RunUntilTick(Math.Max(a.Clock.Tick, b.Clock.Tick));
        Assert.Equal(TestUtil.Digest(a), TestUtil.Digest(b));
    }

    [Fact] // t-043
    public void CadencesAndOrderAreDeterministicAndInspectable()
    {
        var w = TestUtil.FlatWorld();
        var names = w.Scheduler.Systems.Select(s => s.Name).ToList();
        Assert.Equal(names, TestUtil.FlatWorld().Scheduler.Systems.Select(s => s.Name).ToList());
        Assert.Contains("hydrology", w.Scheduler.DescribeSchedule());
        var flora = w.Scheduler.Systems.Single(s => s.Name == "flora");
        Assert.Equal(VivariumWorld.Cadence.Flora, flora.Cadence);
        var counts = w.Scheduler.Systems.ToDictionary(s => s.Name, _ => 0);
        for (long t = 1; t <= 600; t++) foreach (var n in w.Scheduler.SystemsDueAt(t)) counts[n]++;
        Assert.Equal(600 / VivariumWorld.Cadence.Hydrology, counts["hydrology"]);
        Assert.Equal(600 / VivariumWorld.Cadence.Flora, counts["flora"]);
        var order = w.Scheduler.Systems.Select(s => s.Order).ToList();
        Assert.Equal(order.OrderBy(x => x).ToList(), order);
    }

    [Fact] // t-044
    public void CatchUpIsBudgetedAndConverges()
    {
        var w = TestUtil.FlatWorld();
        w.Scheduler.MaxTicksPerAdvance = 50; w.Scheduler.WallBudgetMs = 0;
        w.Scheduler.Advance(60);   // a 60 s stall: 10080 sim-s = 1008 ticks owed
        Assert.Equal(50, w.Clock.Tick);
        Assert.True(w.Scheduler.Backlog > 0);
        int frames = 0;
        while (w.Scheduler.Backlog >= SimClock.FixedStepSeconds && frames++ < 1000) w.Scheduler.Advance(0);
        Assert.Equal(1008, w.Clock.Tick);        // no authoritative time silently skipped
        Assert.True(frames < 30);
    }
}

[Trait("Suite", "Fields")]
public class FieldTests
{
    [Fact] // t-045
    public void ScalarFieldReadWriteInterpolateSerialize()
    {
        var dom = new HexDomain(10);
        var g = new GridSpec(dom, 0.25);
        var f = new ScalarField("test", g, 0.5, 0, 1);
        int c = g.CellAt(new Vec2(0.1, 0.1));
        f[c] = 0.9;
        Assert.Equal(0.9, f[c]);
        f[c] = 7; Assert.Equal(1, f[c]);                  // bounded
        double s = f.Sample(g.CellCenter(c) + new Vec2(0.125, 0));
        Assert.InRange(s, 0.5, 1);
        f[c] = double.NaN; Assert.True(double.IsFinite(f[c]));
        int n = 0; foreach (var _ in g.DomainCells) n++;
        Assert.Equal(g.DomainCells.Length, n);
        var copy = new ScalarField("test", g, 0, 0, 1);
        copy.ImportDomainValues(f.ExportDomainValues());
        Assert.Equal(f.DigestHex(), copy.DigestHex());
        Assert.Throws<InvalidDataException>(() => copy.ImportDomainValues(new double[3]));
    }

    [Fact] // t-046
    public void CategoricalFieldReturnsExactCategories()
    {
        var dom = new HexDomain(10);
        var g = new GridSpec(dom, 0.25);
        var f = new CategoricalField("cat", g, 1, 9);
        var p = new Vec2(1, 1);
        f[g.CellAt(p)] = 3;
        Assert.Equal(3, f.Get(p));
        Assert.Equal(1, f.Get(new Vec2(-1, -1)));
        Assert.Equal(9, f.Get(new Vec2(20, 0)));       // outside the island → fallback
    }

    [Fact] // t-047
    public void LightFieldReflectsObstruction()
    {
        var w = TestUtil.FlatWorld();
        var open = new Vec2(2, 2);
        double before = w.Fields.Light.Sample(open);
        Assert.InRange(before, 0.9, 1);
        Assert.True(w.Placement.PlaceLog(open + new Vec2(0, 0.35), 0, 2.0, 0.3, 1, 3).Ok);
        w.RefreshDerived();
        double after = w.Fields.Light.Sample(open);
        Assert.True(after < before - 0.02, $"log shade should reduce exposure ({before:0.000} → {after:0.000})");
        foreach (int c in w.Grid.DomainCells) Assert.InRange(w.Fields.Light[c], 0, 1);
    }

    [Fact] // t-048
    public void NutrientFieldIsBoundedAndDeterministic()
    {
        var a = TestUtil.FlatWorld(); var b = TestUtil.FlatWorld();
        foreach (var w in new[] { a, b })
        {
            int c = w.Grid.CellAt(new Vec2(0, 0));
            w.Fields.Nutrients.Add(c, 1.2);
            Assert.Equal(0, w.Fields.Nutrients.Take(c, -5));
            double taken = w.Fields.Nutrients.Take(c, 100);
            Assert.True(taken <= 2.0 && w.Fields.Nutrients[c] == 0);
            w.Fields.Nutrients.Add(c, 1.5);
            for (int i = 0; i < 50; i++) w.Fields.StepNutrients(w.Content.Ecology, 3600);
        }
        Assert.Equal(a.Fields.Nutrients.DigestHex(), b.Fields.Nutrients.DigestHex());
        Assert.True(a.Fields.Nutrients.AllFinite());
        foreach (int c in a.Grid.DomainCells) Assert.InRange(a.Fields.Nutrients[c], 0, a.Content.Ecology.NutrientMax);
    }

    [Fact] // t-049
    public void MoistureFieldIsBoundedAndNormalized()
    {
        var w = TestUtil.FlatWorld();
        for (int i = 0; i < 20; i++) w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 3600, w.Fields.Scratch);
        foreach (int c in w.Grid.DomainCells) Assert.InRange(w.Fields.Moisture[c], 0, 1);
        Assert.InRange(w.Fields.Moisture.Sample(new Vec2(0.3, 0.3)), 0, 1);
    }

    [Fact] // t-050 diagnosis support: scheduler + fields under different frame patterns
    public void SchedulerAndFieldDigestsAgreeAcrossFramePatterns()
    {
        string Run(double[] frames)
        {
            var w = TestUtil.DefaultWorld();
            w.Scheduler.MaxTicksPerAdvance = int.MaxValue; w.Scheduler.WallBudgetMs = 0;
            int i = 0;
            while (w.Clock.Tick < 720) w.Scheduler.Advance(frames[i++ % frames.Length]);
            w.Scheduler.RunUntilTick(760);
            return w.Fields.Nutrients.DigestHex() + w.Fields.Moisture.DigestHex() + TestUtil.Digest(w);
        }
        var d1 = Run(new[] { 1 / 60.0 });
        var d2 = Run(new[] { 1 / 30.0, 1 / 120.0, 0.0, 1 / 15.0 });
        var d3 = Run(new[] { 0.5 });
        Assert.Equal(d1, d2);
        Assert.Equal(d1, d3);
    }
}

[Trait("Suite", "Camera")]
public class CameraMediumTests
{
    [Fact] // t-033
    public void WaterMediumTransitionsOncePerCrossingWithHysteresis()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        int wet = w.Grid.DomainCells.OrderByDescending(c => w.Water.Depth[c]).First();
        var p = w.Grid.CellCenter(wet);
        double surface = w.Water.SurfaceAt(p);
        var tracker = new WaterMediumTracker();
        tracker.Update(w, new Vec3(p.X, surface + 0.5, p.Z));
        Assert.False(tracker.Underwater);
        // jitter around the surface does not flip the state
        for (int i = 0; i < 50; i++) tracker.Update(w, new Vec3(p.X, surface + (i % 2 == 0 ? 0.005 : -0.005), p.Z));
        Assert.Equal(0, tracker.Transitions);
        tracker.Update(w, new Vec3(p.X, surface - 0.1, p.Z));
        Assert.True(tracker.Underwater);
        for (int i = 0; i < 50; i++) tracker.Update(w, new Vec3(p.X, surface + (i % 2 == 0 ? 0.005 : -0.005), p.Z));
        Assert.Equal(1, tracker.Transitions);
        tracker.Update(w, new Vec3(p.X, surface + 0.3, p.Z));
        Assert.False(tracker.Underwater);
        Assert.Equal(2, tracker.Transitions);
        // outside the island is always air
        tracker.Update(w, new Vec3(50, -5, 0));
        Assert.False(tracker.Underwater);
    }
}
