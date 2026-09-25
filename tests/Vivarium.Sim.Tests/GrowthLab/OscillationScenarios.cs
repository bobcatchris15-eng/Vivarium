using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Plasmodium;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>Growth-lab proofs for the contraction phase field (docs/overhaul/growth_models.md §6R items 2, 3, 4
/// and the Cadence paragraph): weakly-coupled oscillators drive pressure and shuttle-streaming flow, and net
/// migration only ever comes from rectified asymmetric retention — no steering vector exists anywhere here.</summary>
[Trait("Suite", "GrowthLab")]
public class OscillationScenarios
{
    private sealed class TestEnv : IPlasmodiumEnvironment
    {
        public double MoistureValue = 0.6;
        public readonly Dictionary<(int, int), double> DetritusMap = new();
        public double Moisture(int gx, int gz) => MoistureValue;
        public double LightValue = 0.2;
        public readonly Dictionary<(int, int), double> LightMap = new();
        public double Light(int gx, int gz) => LightMap.TryGetValue((gx, gz), out var v) ? v : LightValue;
        public double Detritus(int gx, int gz) => DetritusMap.TryGetValue((gx, gz), out var v) ? v : 0;
        public double TakeDetritus(int gx, int gz, double amount)
        {
            if (amount <= 0 || !DetritusMap.TryGetValue((gx, gz), out var v) || v <= 0) return 0;
            double taken = Math.Min(v, amount);
            DetritusMap[(gx, gz)] = v - taken;
            return taken;
        }
    }

    // ------------------------------------------------------------------ oscillation_standing_waves

    private const int Half = 8; // 17x17 uniform still body, no frontier growth

    private static (PlasmodiumColony colony, CoverageLayer layer, Attractant attractant, TestEnv env) BuildStillBody()
    {
        // Beta = LambdaF = 0: no front extension, so the body never changes shape; FeedRate = 0 and a uniform
        // environment give every cell the same neutral omega, so any phase gradient that appears comes only from
        // the deterministic per-cell initial-phase disorder and the coupling dynamics themselves.
        var prm = new PlasmodiumParams
        {
            Beta = 0, LambdaF = 0, InitialMass = 2000, FeedRate = 0, MaintenanceRate = 0,
        };
        var env = new TestEnv();
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 44);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        var cells = new List<(int, int)>();
        for (int gz = -Half; gz <= Half; gz++)
            for (int gx = -Half; gx <= Half; gx++)
                cells.Add((gx, gz));
        colony.Seed(cells);
        foreach (var (gx, gz) in cells) layer.SetOcc(gx, gz, 1);

        return (colony, layer, attractant, env);
    }

    private static (double cx, double cz) MassCentroid(PlasmodiumColony colony)
    {
        double sx = 0, sz = 0, w = 0;
        foreach (var ((gx, gz), m) in colony.Mass) { sx += gx * m; sz += gz * m; w += m; }
        return w <= 0 ? (0, 0) : (sx / w, sz / w);
    }

    /// <summary>Kuramoto phase-order parameter r = |mean(e^{iθ})| over every occupied cell: 1.0 means every cell
    /// perfectly in phase (no gradient at all); the closer to 0, the more the phases are spread across the body.</summary>
    private static double PhaseOrderParameter(PlasmodiumColony colony)
    {
        double sx = 0, sy = 0; int n = 0;
        foreach (var (gx, gz) in colony.CellId.Keys)
        {
            double th = colony.ThetaAt(gx, gz);
            sx += Math.Cos(th); sy += Math.Sin(th); n++;
        }
        return n == 0 ? 1.0 : Math.Sqrt(sx * sx + sy * sy) / n;
    }

    /// <summary>Asserts the body stayed one 8-connected component and did not lose more than 20% of its cells
    /// (no fragmentation into a "dust" of isolated cells) at the given step, for the caller's failure message.</summary>
    private static void AssertCoherentSheet(PlasmodiumColony colony, int initialCellCount, long step)
    {
        var components = ComponentLabeler.Label(colony.CellId.Keys);
        Assert.True(components.Count == 1,
            $"body fragmented into {components.Count} components at step {step} (sizes: {string.Join(",", components.Select(c => c.Count))})");

        double fillFraction = initialCellCount > 0 ? (double)colony.CellId.Count / initialCellCount : 1.0;
        Assert.True(fillFraction >= 0.8,
            $"filled area dropped to {fillFraction:P0} of initial at step {step} ({colony.CellId.Count}/{initialCellCount} cells)");
    }

    [Fact]
    public void StandingWavesPersistWithNearZeroDrift()
    {
        var (colony, layer, attractant, env) = BuildStillBody();

        var (cx0, cz0) = MassCentroid(colony);
        int initialCount = colony.CellId.Count;

        // 20 cycles at Ω0's ~100 sim-s period, stepped at the 30 sim-s cadence from the spec (67 steps ≈ 2010 s).
        const double dt = 30.0;
        const int steps = 67;
        int firstCycleSteps = (int)Math.Ceiling(100.0 / dt); // one period's worth of steps
        for (long s = 0; s < steps; s++)
        {
            colony.Step(layer, attractant, env, s, dt);
            // No isolated single cells / fragmentation after the first cycle has had time to settle.
            if (s >= firstCycleSteps) AssertCoherentSheet(colony, initialCount, s);
        }

        var (cx1, cz1) = MassCentroid(colony);
        double drift = Math.Sqrt((cx1 - cx0) * (cx1 - cx0) + (cz1 - cz0) * (cz1 - cz0));
        Assert.True(drift < 0.25, $"centroid drifted in a uniform environment: {drift} cells");

        double r = PhaseOrderParameter(colony);
        Assert.True(r < 0.9, $"phases fully synchronised (order parameter {r}); expected a persisting phase gradient");
    }

    [Fact]
    public void StandingWavesScenarioIsDeterministic()
    {
        (string digest, double r) Run()
        {
            var (colony, layer, attractant, env) = BuildStillBody();
            for (long s = 0; s < 67; s++) colony.Step(layer, attractant, env, s, dt: 30.0);
            using var d = new Core.DigestBuilder();
            foreach (var (gx, gz) in colony.CellId.Keys.OrderBy(c => c)) d.Add(colony.ThetaAt(gx, gz));
            return (d.Hex(), PhaseOrderParameter(colony));
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.digest, b.digest);
        Assert.Equal(a.r, b.r, precision: 12);
    }

    /// <summary>Slow: renders the standing-wave timelapse, mass as brightness and phase as hue.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void StandingWavesScenarioWritesTimelapseFrames()
    {
        var (colony, layer, attractant, env) = BuildStillBody();
        const string scenario = "oscillation_standing_waves";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        for (int s = 0; s < 67; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 30.0);
            WritePhaseFrame(scenario, s, colony, Half + 1);
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ oscillation_rectified_drift

    private const int StripHalf = 12; // strip spans gx in [-StripHalf, StripHalf] at gz = 0
    private const int FoodX = StripHalf;
    private const int MidX = 0;

    private static (PlasmodiumColony colony, CoverageLayer layer, Attractant attractant, TestEnv env) BuildDriftStrip()
    {
        // Beta = LambdaF = 0: a fixed body again, so any centroid movement is transport + rectification alone,
        // never frontier growth toward the food. Food only ever acts through uptake -> ω -> retention (§6R item 4).
        var prm = new PlasmodiumParams
        {
            Beta = 0, LambdaF = 0, InitialMass = 200, FeedRate = 6.0, MaintenanceRate = 0,
            SheetConductance = 1.0,
        };
        var env = new TestEnv();
        for (int fz = -1; fz <= 1; fz++) env.DetritusMap[(FoodX, fz)] = 1e6;

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 45);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        var cells = new List<(int, int)>();
        for (int gx = -StripHalf; gx <= StripHalf; gx++) cells.Add((gx, 0));
        colony.Seed(cells);
        foreach (var (gx, gz) in cells) layer.SetOcc(gx, gz, 1);

        return (colony, layer, attractant, env);
    }

    [Fact]
    public void RectifiedDriftMovesTowardFoodWithAlternatingFlow()
    {
        var (colony, layer, attractant, env) = BuildDriftStrip();
        var (cx0, _) = MassCentroid(colony);
        int initialCount = colony.CellId.Count;

        bool sawPositive = false, sawNegative = false;
        const double dt = 10.0;
        const int steps = 300; // 3000 sim-s ~ 30 cycles
        int firstCycleSteps = (int)Math.Ceiling(100.0 / dt);
        for (long s = 0; s < steps; s++)
        {
            colony.Step(layer, attractant, env, s, dt);
            double q = colony.LastFlowBetween((MidX, 0), (MidX + 1, 0));
            if (q > 1e-9) sawPositive = true;
            if (q < -1e-9) sawNegative = true;
            if (s >= firstCycleSteps) AssertCoherentSheet(colony, initialCount, s);
        }

        Assert.True(sawPositive && sawNegative,
            $"mid-cell flow did not alternate sign over the run (positive seen: {sawPositive}, negative seen: {sawNegative})");

        var (cx1, _) = MassCentroid(colony);
        Assert.True(cx1 > cx0, $"mass centroid did not drift up-gradient: before {cx0}, after {cx1}");
    }

    [Fact]
    public void RectifiedDriftScenarioIsDeterministic()
    {
        (string digest, double centroid) Run()
        {
            var (colony, layer, attractant, env) = BuildDriftStrip();
            for (long s = 0; s < 300; s++) colony.Step(layer, attractant, env, s, dt: 10.0);
            using var d = new Core.DigestBuilder();
            foreach (var (cell, m) in colony.Mass.OrderBy(kv => kv.Key)) { d.Add(cell.gx); d.Add(cell.gz); d.Add(m); }
            var (cx, _) = MassCentroid(colony);
            return (d.Hex(), cx);
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.digest, b.digest);
        Assert.Equal(a.centroid, b.centroid, precision: 12);
    }

    /// <summary>Slow: renders the rectified-drift timelapse, mass as brightness and phase as hue.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void RectifiedDriftScenarioWritesTimelapseFrames()
    {
        var (colony, layer, attractant, env) = BuildDriftStrip();
        const string scenario = "oscillation_rectified_drift";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        for (int s = 0; s < 300; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 10.0);
            if (s % 5 == 0 || s == 299) WriteStripPhaseFrame(scenario, s, colony);
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ perf: 600 nodes, printed ms/step

    /// <summary>Slow/Perf: prints per-step ms at 600 nodes (§6R Cadence budget: ≤1 ms per plasmodium per step,
    /// Debug). Printed, not asserted — Debug JIT and CI hardware vary too much for a hard gate here.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Suite", "Perf")]
    public void PhaseStepPerfAt600Nodes()
    {
        var prm = new PlasmodiumParams { Beta = 0, LambdaF = 0, InitialMass = 5000, FeedRate = 2.0 };
        var env = new TestEnv();
        env.DetritusMap[(0, 0)] = 1e6;
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 46);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        var cells = new List<(int, int)>();
        int side = (int)Math.Ceiling(Math.Sqrt(600));
        for (int gz = 0; gz < side && cells.Count < 600; gz++)
            for (int gx = 0; gx < side && cells.Count < 600; gx++)
                cells.Add((gx, gz));
        colony.Seed(cells);
        foreach (var (gx, gz) in cells) layer.SetOcc(gx, gz, 1);

        // Warm-up (JIT).
        for (int s = 0; s < 5; s++) colony.Step(layer, attractant, env, s, dt: 30.0);

        var sw = Stopwatch.StartNew();
        const int steps = 30;
        for (int s = 5; s < 5 + steps; s++) colony.Step(layer, attractant, env, s, dt: 30.0);
        sw.Stop();

        double msPerStep = sw.Elapsed.TotalMilliseconds / steps;
        Console.WriteLine($"[perf] plasmodium step at 600 nodes: {msPerStep:F3} ms/step (target <= 1 ms Debug)");
        Assert.True(msPerStep >= 0); // always true: this test's job is to print the figure, not gate the build
    }

    // ------------------------------------------------------------------ frame helpers

    private static (byte r, byte g, byte b) HueRgb(double theta, double brightness)
    {
        double hue = (theta + Math.PI) / (2 * Math.PI); // [0,1)
        double h6 = hue * 6.0;
        int i = (int)Math.Floor(h6) % 6;
        double f = h6 - Math.Floor(h6);
        double v = brightness, p = 0, q = v * (1 - f), t = v * f;
        (double r, double g, double b) rgb = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((byte)Math.Clamp(rgb.r * 255, 0, 255), (byte)Math.Clamp(rgb.g * 255, 0, 255), (byte)Math.Clamp(rgb.b * 255, 0, 255));
    }

    private static void WritePhaseFrame(string scenario, int frameIndex, PlasmodiumColony colony, int half)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        double maxMass = 1e-9;
        foreach (var m in colony.Mass.Values) if (m > maxMass) maxMass = m;
        for (int gz = -half; gz <= half; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                int px = gx + half, py = gz + half;
                int o = (py * size + px) * 3;
                double m = colony.MassAt(gx, gz);
                if (m <= 0) { rgb[o] = 12; rgb[o + 1] = 12; rgb[o + 2] = 18; continue; }
                double brightness = Math.Clamp(0.25 + 0.75 * m / maxMass, 0, 1);
                var (r, g, b) = HueRgb(colony.ThetaAt(gx, gz), brightness);
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }
        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    private static void WriteStripPhaseFrame(string scenario, int frameIndex, PlasmodiumColony colony)
    {
        int half = StripHalf + 2;
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        double maxMass = 1e-9;
        foreach (var m in colony.Mass.Values) if (m > maxMass) maxMass = m;
        for (int gz = -2; gz <= 2; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                int px = gx + half, py = gz + 2;
                int o = (py * size + px) * 3;
                double m = colony.MassAt(gx, gz);
                if (m <= 0) { rgb[o] = 12; rgb[o + 1] = 12; rgb[o + 2] = 18; continue; }
                double brightness = Math.Clamp(0.25 + 0.75 * m / maxMass, 0, 1);
                var (r, g, b) = HueRgb(colony.ThetaAt(gx, gz), brightness);
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }
        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }
}
