using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Vivarium.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>
/// Growth-lab aquatic scenarios (docs/overhaul/growth_models.md §15.1, §15.2, §15.4 Aq-1): synthetic
/// <see cref="IAquaticEnv"/> ponds drive <see cref="SurfaceFloatRules"/> and <see cref="AlgaeRules"/> directly,
/// no <c>VivariumWorld</c> or hydrology involved.
/// </summary>
[Trait("Suite", "GrowthLab")]
public class AquaticScenarios
{
    private readonly ITestOutputHelper _out;
    public AquaticScenarios(ITestOutputHelper output) => _out = output;

    private const ulong Seed = 8181;

    // Lab-only layer ids: no CoverageLayerId enum member for aquatic layers yet (Aq-2 wires world layers);
    // CoverageLayer only uses the id for identity/hashing, so any distinct int works here.
    private static CoverageLayer NewSurfaceFloat() => new((CoverageLayerId)10, Seed);
    private static CoverageLayer NewAlgaeBed() => new((CoverageLayerId)11, Seed);
    private static CoverageLayer NewAlgaeFloat() => new((CoverageLayerId)12, Seed);

    /// <summary>Small, comfortably-CFL-stable time slice per lab step: real ponds are metres across on a 2 cm
    /// grid, so a "growth day" of advection would need an enormous substep count; many small steps instead.</summary>
    private const double DtDays = 0.0002; // 17.28 s at AquaticConst.SecondsPerDay

    private sealed class FuncEnv : IAquaticEnv
    {
        public Func<int, int, double> Depth = (_, _) => 0.3;
        public Func<int, int, Vec2> Flow = (_, _) => Vec2.Zero;
        public Func<int, int, double> Light = (_, _) => 1.0;
        public Func<int, int, double> Nutrients = (_, _) => 1.0;
        public Func<int, int, bool> Obstacle = (_, _) => false;
        public Func<int, int, double> Shade = (_, _) => 0.0;

        public double DepthAt(int gx, int gz) => Depth(gx, gz);
        public Vec2 FlowAt(int gx, int gz) => Flow(gx, gz);
        public double LightAt(int gx, int gz) => Light(gx, gz);
        public double NutrientsAt(int gx, int gz) => Nutrients(gx, gz);
        public bool IsObstacle(int gx, int gz) => Obstacle(gx, gz);
        public double SurfaceShadeAt(int gx, int gz) => Shade(gx, gz);
    }

    private static void SeedUniform(CoverageLayer layer, GridBounds d, float b)
    {
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
            layer.SetCell(gx, gz, occ: 1, b: b, w: 0, age: 0, dorm: 0, flags: 0, d2e: 0);
    }

    private static double TotalMass(CoverageLayer layer, GridBounds d)
    {
        double sum = 0;
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
            sum += layer.GetB(gx, gz);
        return sum;
    }

    private static double AvgB(CoverageLayer layer, int gx0, int gx1, int gz0, int gz1)
    {
        double sum = 0; int n = 0;
        for (int gz = gz0; gz <= gz1; gz++)
        for (int gx = gx0; gx <= gx1; gx++) { sum += layer.GetB(gx, gz); n++; }
        return n > 0 ? sum / n : 0;
    }

    // ==================================================================== duckweed_still_bay

    [Fact]
    public void DuckweedFillsStillBayAndKeepsChannelClear()
    {
        var d = new GridBounds(0, 0, 49, 29);
        var layer = NewSurfaceFloat();
        SeedUniform(layer, d, 0.3f);

        var env = new FuncEnv
        {
            // channel (gz 0..9): flows east; a turn zone near the far end diverts flow down into the bay;
            // bay (gz 10..29) is still. No explicit bank cells needed — the domain edges are already closed
            // (SurfaceFloatRules.Advect only updates interior faces), and rows only exchange where vz != 0.
            Flow = (gx, gz) =>
            {
                if (gz <= 9) return gx >= 40 ? new Vec2(0.02, 0.08) : new Vec2(0.08, 0.0);
                return Vec2.Zero;
            },
        };

        string scenario = "duckweed_still_bay";
        GrowthLabRunner.Run(scenario, layer, steps: 150, half: 0, writeFrames: false, s =>
            SurfaceFloatRules.Step(layer, env, new DuckweedParams(), d, DtDays, s, Seed));
        WriteFrames(scenario, layer, d);

        double channelAvg = AvgB(layer, 0, 49, 0, 9);
        double bayAvg = AvgB(layer, 0, 49, 10, 29);
        _out.WriteLine($"[duckweed_still_bay] channelAvg={channelAvg:F4} bayAvg={bayAvg:F4}");

        Assert.True(bayAvg > 3 * Math.Max(channelAvg, 1e-9), $"bay/channel ratio {bayAvg / Math.Max(channelAvg, 1e-9):F2} <= 3");
    }

    // ==================================================================== duckweed_obstacle

    [Fact]
    public void DuckweedPilesUpstreamOfObstacle()
    {
        var d = new GridBounds(0, 0, 39, 9);
        var layer = NewSurfaceFloat();
        SeedUniform(layer, d, 0.3f);

        var env = new FuncEnv
        {
            Flow = (_, _) => new Vec2(0.05, 0.0),
            Obstacle = (gx, gz) => gx == 20 && gz is >= 2 and <= 7, // a submerged log/snag spanning rows 2..7
        };

        string scenario = "duckweed_obstacle";
        GrowthLabRunner.Run(scenario, layer, steps: 60, half: 0, writeFrames: false, s =>
            SurfaceFloatRules.Step(layer, env, new DuckweedParams(), d, DtDays, s, Seed));
        WriteFrames(scenario, layer, d);

        double upstream = AvgB(layer, 19, 19, 2, 7);   // immediately upstream face of the obstacle
        double shadow = AvgB(layer, 21, 21, 2, 7);     // immediately downstream (shadow) of the obstacle
        _out.WriteLine($"[duckweed_obstacle] upstream={upstream:F4} shadow={shadow:F4}");

        Assert.True(upstream > shadow, $"no upstream pileup: upstream={upstream:F4} shadow={shadow:F4}");
        Assert.True(upstream > 0.3, "upstream density did not build up above the seed density");
    }

    // ==================================================================== algae_scour

    [Fact]
    public void AlgaeGrowsInStillWaterAndIsScouredByFlow()
    {
        var d = new GridBounds(0, 0, 29, 9);
        var bed = NewAlgaeBed();
        var floatLayer = NewAlgaeFloat();
        SeedUniform(bed, d, 0.05f);

        var env = new FuncEnv
        {
            Flow = (_, gz) => gz <= 4 ? Vec2.Zero : new Vec2(0.2, 0.0), // rows 0..4 still, rows 5..9 flowing
        };
        var bp = new AlgaeBedParams(DetachThickness: 2.0); // never detaches in this scenario
        var fp = new AlgaeFloatParams();

        // Bed growth has no CFL constraint (it doesn't advect); the float layer's flow is zero here too, so a
        // much larger dtDays than the duckweed advection scenarios is fine and lets growth reach a visible level.
        const double growthDt = 0.5;
        string scenario = "algae_scour";
        GrowthLabRunner.Run(scenario, bed, steps: 200, half: 0, writeFrames: false, s =>
            AlgaeRules.Step(bed, floatLayer, env, bp, fp, d, growthDt, s, Seed));
        WriteFrames(scenario, bed, d);

        double stillAvg = AvgB(bed, 0, 29, 0, 4);
        double flowAvg = AvgB(bed, 0, 29, 5, 9);
        _out.WriteLine($"[algae_scour] stillAvg={stillAvg:F4} flowAvg={flowAvg:F4}");

        Assert.True(stillAvg > 0.1, "algae did not grow in still water");
        Assert.True(stillAvg > 5 * Math.Max(flowAvg, 1e-9), $"still/flow ratio {stillAvg / Math.Max(flowAvg, 1e-9):F2} <= 5");
    }

    // ==================================================================== algae_shading

    [Fact]
    public void DenseDuckweedCoverSuppressesBedAlgaeBeneath()
    {
        var d = new GridBounds(0, 0, 19, 9);
        var bed = NewAlgaeBed();
        var floatLayer = NewAlgaeFloat();
        SeedUniform(bed, d, 0.05f);

        var env = new FuncEnv
        {
            Shade = (gx, _) => gx < 10 ? 0.0 : 0.9, // left half open water, right half under a dense duckweed mat
        };
        var bp = new AlgaeBedParams(DetachThickness: 2.0);
        var fp = new AlgaeFloatParams();

        const double growthDt = 0.02; // small enough that growth hasn't saturated both sides to ~1 before comparing
        string scenario = "algae_shading";
        GrowthLabRunner.Run(scenario, bed, steps: 400, half: 0, writeFrames: false, s =>
            AlgaeRules.Step(bed, floatLayer, env, bp, fp, d, growthDt, s, Seed));
        WriteFrames(scenario, bed, d);

        double openAvg = AvgB(bed, 0, 9, 0, 9);
        double shadedAvg = AvgB(bed, 10, 19, 0, 9);
        _out.WriteLine($"[algae_shading] openAvg={openAvg:F4} shadedAvg={shadedAvg:F4}");

        Assert.True(openAvg > 2 * Math.Max(shadedAvg, 1e-9), $"open/shaded ratio {openAvg / Math.Max(shadedAvg, 1e-9):F2} <= 2");
    }

    // ==================================================================== algae_float_bloom

    [Fact]
    public void StillNutrientRichWaterGrowsFloatingMatsThatDriftToMargin()
    {
        var d = new GridBounds(0, 0, 29, 0 + 0); // 1-row strip keeps the drift comparison unambiguous
        d = new GridBounds(0, 0, 29, 9);
        var bed = NewAlgaeBed();
        var floatLayer = NewAlgaeFloat();
        SeedUniform(bed, d, 0.8f); // already-thick bed film, ready to detach

        var env = new FuncEnv
        {
            // still everywhere so mats persist and grow, but a gentle drift toward the right-hand margin
            Flow = (gx, _) => new Vec2(0.01, 0.0),
        };
        var bp = new AlgaeBedParams(DetachThickness: 0.5, DetachRate: 0.3);
        var fp = new AlgaeFloatParams(StillFlowThreshold: 0.05);

        string scenario = "algae_float_bloom";
        GrowthLabRunner.Run(scenario, floatLayer, steps: 200, half: 0, writeFrames: false, s =>
            AlgaeRules.Step(bed, floatLayer, env, bp, fp, d, DtDays, s, Seed));
        WriteFrames(scenario, floatLayer, d);

        double totalFloat = TotalMass(floatLayer, d);
        double centreAvg = AvgB(floatLayer, 10, 19, 0, 9);
        double marginAvg = AvgB(floatLayer, 25, 29, 0, 9);
        _out.WriteLine($"[algae_float_bloom] total={totalFloat:F2} centreAvg={centreAvg:F4} marginAvg={marginAvg:F4}");

        Assert.True(totalFloat > 0, "no floating mat formed (bed never detached)");
        Assert.True(marginAvg > centreAvg, $"mats did not drift to the margin: margin={marginAvg:F4} centre={centreAvg:F4}");
    }

    // ==================================================================== determinism

    [Fact]
    public void SurfaceFloatStepIsDeterministic()
    {
        var d = new GridBounds(0, 0, 19, 19);

        (double total, string digest) Run()
        {
            var layer = NewSurfaceFloat();
            SeedUniform(layer, d, 0.2f);
            var env = new FuncEnv { Flow = (gx, gz) => new Vec2(0.03 * Math.Sin(gx * 0.3), 0.03 * Math.Cos(gz * 0.3)) };
            for (long s = 0; s < 40; s++) SurfaceFloatRules.Step(layer, env, new DuckweedParams(), d, DtDays, s, Seed);

            var db = new DigestBuilder();
            for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
            for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
                db.Add(BitConverter.GetBytes(layer.GetB(gx, gz)));
            return (TotalMass(layer, d), db.Hex());
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.digest, b.digest);
        Assert.Equal(a.total, b.total, precision: 12);
    }

    // ==================================================================== advection mass conservation

    [Fact]
    public void AdvectionConservesMassWithNoGrowthOrSources()
    {
        var d = new GridBounds(0, 0, 24, 24);
        var layer = NewSurfaceFloat();
        SeedUniform(layer, d, 0.4f);
        var env = new FuncEnv { Flow = (gx, gz) => new Vec2(0.06 * Math.Sin(gx * 0.4 + gz), 0.06 * Math.Cos(gz * 0.4)) };

        double before = TotalMass(layer, d);
        for (int s = 0; s < 30; s++) SurfaceFloatRules.Advect(layer, env, d, DtDays, windX: 0, windZ: 0);
        double after = TotalMass(layer, d);

        _out.WriteLine($"[advection_conservation] before={before:F6} after={after:F6}");
        // float32-per-cell storage accumulates rounding over many substeps; conservation is checked in relative
        // terms rather than to double precision.
        Assert.True(Math.Abs(after - before) < 1e-3 * before, $"mass not conserved: before={before:F6} after={after:F6}");
    }

    // ==================================================================== perf: dense pond, ms/step printed

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Suite", "Perf")]
    public void DenseStepPerfIsPrinted()
    {
        var d = new GridBounds(0, 0, 99, 99); // 10000 cells, fully occupied
        var layer = NewSurfaceFloat();
        SeedUniform(layer, d, 0.5f);
        var env = new FuncEnv { Flow = (gx, gz) => new Vec2(0.05 * Math.Sin(gx * 0.1), 0.05 * Math.Cos(gz * 0.1)) };
        var p = new DuckweedParams();

        for (long s = 0; s < 3; s++) SurfaceFloatRules.Step(layer, env, p, d, DtDays, s, Seed); // JIT warm-up

        const int steps = 10;
        var sw = Stopwatch.StartNew();
        for (long s = 3; s < 3 + steps; s++) SurfaceFloatRules.Step(layer, env, p, d, DtDays, s, Seed);
        sw.Stop();

        double msPerStep = sw.Elapsed.TotalMilliseconds / steps;
        _out.WriteLine($"[perf] dense (100x100) SurfaceFloat step: {msPerStep:F3} ms/step");
        Assert.True(msPerStep >= 0);
    }

    // ==================================================================== frame helper

    private static void WriteFrames(string scenario, CoverageLayer layer, GridBounds d)
    {
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        int w = d.Width, h = d.Height;
        var rgb = new byte[w * h * 3];
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            double b = layer.GetB(gx, gz);
            int o = ((gz - d.MinGz) * w + (gx - d.MinGx)) * 3;
            byte g = (byte)Math.Clamp(40 + b * 200, 0, 255);
            rgb[o] = 10; rgb[o + 1] = g; rgb[o + 2] = 40;
        }
        PngEncoder.WriteRgb(Path.Combine(dir, "frame_final.png"), w, h, rgb);
    }
}
