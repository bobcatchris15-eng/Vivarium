using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Rules;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>Constant, hospitable micro-environment: every cell is Rock, well-lit and wet (docs/overhaul/growth_models.md §5, §12).</summary>
internal sealed class UniformRockEnv : ILichenEnvSource
{
    public MicroEnv Sample(int gx, int gz) => new()
    {
        Moisture = 0.8,
        Humidity = 0.7,
        Light = 0.8,
        Nutrients = 0.5,
        Detritus = 0,
        Substrate = CoverageSubstrate.Rock,
    };
}

[Trait("Suite", "GrowthLab")]
public class LichenScenarios
{
    private const int Half = 30;

    private static LichenParams CrustSpecies(byte slot = 1) => new()
    {
        OccSlot = slot,
        Form = LichenForm.Crustose,
        Lateral = 0.35,
        SeedBiomass = 0.2,
        MaxBiomass = 1.0,
    };

    private static LichenParams FolioseSpecies(byte slot = 1) => new()
    {
        OccSlot = slot,
        Form = LichenForm.Foliose,
        Lateral = 0.9,
        TipBiasGamma = 6.0,
        OpennessRadius = 5,
        MinNeighboursToColonise = 2,
        SeedBiomass = 0.2,
        MaxBiomass = 1.0,
    };

    private static CoverageLayer Seeded(byte occSlot, float b = 0.5f, ulong seed = 7)
    {
        var layer = new CoverageLayer(CoverageLayerId.Crust, seed);
        layer.SetCell(0, 0, occSlot, b, 0, 0, 0, 0, 0);
        return layer;
    }

    /// <summary>Seeds a small filled disc rather than a single cell: foliose's contiguity gate (MinNeighboursToColonise)
    /// needs a founding cluster with interior support before its own front can bootstrap outward.</summary>
    private static CoverageLayer SeededDisc(byte occSlot, int radius, float b = 0.5f, ulong seed = 7, int cx = 0, int cz = 0)
    {
        var layer = new CoverageLayer(CoverageLayerId.Crust, seed);
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
            if (dx * dx + dz * dz <= radius * radius) layer.SetCell(cx + dx, cz + dz, occSlot, b, 0, 0, 0, 0, 0);
        return layer;
    }

    // ------------------------------------------------------------------ crust_eden

    [Fact]
    public void CrustEdenGrowsRoundWithRoughEdge()
    {
        var layer = Seeded(1);
        var env = new UniformRockEnv();
        var species = new[] { CrustSpecies() };

        for (long s = 0; s < 60; s++) LichenRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: 7);

        double roundness = GrowthLabRunner.Roundness(layer, Half);
        Assert.True(roundness > 0.75, $"crust roundness {roundness:F3} <= 0.75");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CrustEdenWritesTimelapseFrames()
    {
        var layer = Seeded(1);
        var env = new UniformRockEnv();
        var species = new[] { CrustSpecies() };

        RunAndWriteColoredFrames("crust_eden", layer, 40, Half,
            s => LichenRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: 7));

        var frames = Directory.GetFiles(GrowthLabRunner.FramesDir("crust_eden"), "frame_*.png");
        Assert.Equal(40, frames.Length);
    }

    // ------------------------------------------------------------------ foliose_lobes

    [Fact]
    public void FolioseLobesHaveMorePerimeterThanCrustAtEqualAreaAndPlausibleLobeCount()
    {
        var env = new UniformRockEnv();

        var crustLayer = Seeded(1);
        var crustSpecies = new[] { CrustSpecies() };
        double crustArea = RunToArea(crustLayer, env, crustSpecies, targetArea: 200, maxSteps: 200);
        double crustPerim = OuterPerimeter(crustLayer, Half);
        double crustRatio = crustPerim * crustPerim / crustArea;

        var folLayer = SeededDisc(1, radius: 2);
        var folSpecies = new[] { FolioseSpecies() };
        double folArea = RunToArea(folLayer, env, folSpecies, targetArea: 200, maxSteps: 200);
        double folPerim = OuterPerimeter(folLayer, Half);
        double folRatio = folPerim * folPerim / folArea;

        Assert.True(folRatio >= 2.0 * crustRatio,
            $"foliose perimeter^2/area {folRatio:F1} should be >= 2x crust's {crustRatio:F1} (areas {folArea:F0}/{crustArea:F0})");

        var (occupied, holes) = OccupiedAndHoles(folLayer, Half);
        double holeFraction = occupied == 0 ? 0 : (double)holes / occupied;
        Assert.True(holeFraction < 0.05, $"foliose hole fraction {holeFraction:P1} should be < 5% (a solid thallus, not speckle)");

        Assert.True(IsSingleConnectedComponent(folLayer, Half, occSlot: 1), "foliose thallus should be one connected component");

        int lobes = CountLobes(folLayer, Half);
        Assert.InRange(lobes, 5, 15);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void FolioseLobesWritesTimelapseFrames()
    {
        var layer = SeededDisc(1, radius: 2);
        var env = new UniformRockEnv();
        var species = new[] { FolioseSpecies() };

        RunAndWriteColoredFrames("foliose_lobes", layer, 60, Half,
            s => LichenRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: 11));

        var frames = Directory.GetFiles(GrowthLabRunner.FramesDir("foliose_lobes"), "frame_*.png");
        Assert.Equal(60, frames.Length);
    }

    // ------------------------------------------------------------------ lichen_prothallus

    [Fact]
    public void ProthallusStopsAtBoundaryWithNoOverlap()
    {
        var env = new UniformRockEnv();
        var layer = new CoverageLayer(CoverageLayerId.Crust, 3);
        layer.SetCell(-8, 0, 1, 0.3f, 0, 0, 0, 0, 0);
        layer.SetCell(8, 0, 2, 0.3f, 0, 0, 0, 0, 0);
        var species = new[] { CrustSpecies(1), CrustSpecies(2) };

        for (long s = 0; s < 40; s++) LichenRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: 3);

        // No cell ever holds two occupants (structural: one Occ slot per cell) - verify no stray double-write
        // corrupted state, and that a meeting frontier produced Boundary flags rather than one thallus overrunning
        // the other's territory.
        bool sawBoundary = false;
        int sp1MaxX = int.MinValue, sp2MinX = int.MaxValue;
        for (int gz = -Half; gz <= Half; gz++)
        for (int gx = -Half; gx <= Half; gx++)
        {
            byte occ = layer.GetOcc(gx, gz);
            if (occ == 1) sp1MaxX = Math.Max(sp1MaxX, gx);
            if (occ == 2) sp2MinX = Math.Min(sp2MinX, gx);
            if (occ != 0 && HasBoundaryFlag(layer, gx, gz)) sawBoundary = true;
        }
        Assert.True(sawBoundary, "expected at least one Boundary-flagged cell where the two thalli meet");
        Assert.True(sp1MaxX < sp2MinX + 7, $"species territories interpenetrated too far: sp1 max x {sp1MaxX}, sp2 min x {sp2MinX}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void ProthallusWritesTimelapseFrames()
    {
        var env = new UniformRockEnv();
        var layer = new CoverageLayer(CoverageLayerId.Crust, 3);
        layer.SetCell(-8, 0, 1, 0.3f, 0, 0, 0, 0, 0);
        layer.SetCell(8, 0, 2, 0.3f, 0, 0, 0, 0, 0);
        var species = new[] { CrustSpecies(1), CrustSpecies(2) };

        RunAndWriteColoredFrames("lichen_prothallus", layer, 40, Half,
            s => LichenRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: 3));

        var frames = Directory.GetFiles(GrowthLabRunner.FramesDir("lichen_prothallus"), "frame_*.png");
        Assert.Equal(40, frames.Length);
    }

    // ------------------------------------------------------------------ determinism (§1.4-1.5)

    [Fact]
    [Trait("Suite", "Coverage")]
    public void DeterministicAcrossRunsAndShuffledTileAllocationOrder()
    {
        var env = new UniformRockEnv();
        var species = new[] { FolioseSpecies() };

        var a = SeededDisc(1, radius: 2, seed: 99);
        for (long s = 0; s < 30; s++) LichenRules.Step(a, env, species, 1.0, s, 99);

        var b = SeededDisc(1, radius: 2, seed: 99);
        for (long s = 0; s < 30; s++) LichenRules.Step(b, env, species, 1.0, s, 99);

        Assert.Equal(Digest(a), Digest(b));

        var c = new CoverageLayer(CoverageLayerId.Crust, 99);
        foreach (var (ti, tj) in new (int, int)[] { (3, 3), (-3, -3), (0, 0), (1, -1) }) c.GetOrCreateTile(ti, tj);
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            if (dx * dx + dz * dz <= 4) c.SetCell(dx, dz, 1, 0.5f, 0, 0, 0, 0, 0);
        for (long s = 0; s < 30; s++) LichenRules.Step(c, env, species, 1.0, s, 99);

        Assert.Equal(Digest(a), Digest(c));
    }

    // ------------------------------------------------------------------ perf (dense-step timing, printed per packet)

    [Fact]
    public void DenseStepTimingIsPrinted()
    {
        var env = new UniformRockEnv();
        var species = new[] { CrustSpecies() };
        var layer = Seeded(1);
        for (long s = 0; s < 30; s++) LichenRules.Step(layer, env, species, 1.0, s, 7); // fill a dense patch first

        var sw = Stopwatch.StartNew();
        const int trials = 10;
        for (long s = 30; s < 30 + trials; s++) LichenRules.Step(layer, env, species, 1.0, s, 7);
        sw.Stop();
        double msPerStep = sw.Elapsed.TotalMilliseconds / trials;
        Console.WriteLine($"lichen dense-step: {msPerStep:F3} ms/step over {trials} steps");
        Assert.True(msPerStep < 50, $"dense step took {msPerStep:F3} ms, expected well under budget");
    }

    // ------------------------------------------------------------------ helpers

    private static double RunToArea(CoverageLayer layer, ILichenEnvSource env, IReadOnlyList<LichenParams> species, int targetArea, int maxSteps)
    {
        int area = 0;
        long s = 0;
        for (; s < maxSteps; s++)
        {
            LichenRules.Step(layer, env, species, 1.0, s, 5);
            area = CountOccupied(layer, Half);
            if (area >= targetArea) break;
        }
        return area;
    }

    private static int CountOccupied(CoverageLayer layer, int half)
    {
        int n = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
            if (layer.GetOcc(gx, gz) != 0) n++;
        return n;
    }

    private static double Perimeter(CoverageLayer layer, int half)
    {
        int perim = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            if (layer.GetOcc(gx, gz) == 0) continue;
            foreach (var (dx, dz) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (layer.GetOcc(gx + dx, gz + dz) == 0) perim++;
        }
        return perim;
    }

    /// <summary>Counts lobes as the number of separate runs of occupied cells crossing a ring at ~80% of max radius.</summary>
    private static int CountLobes(CoverageLayer layer, int half)
    {
        double rMax = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
            if (layer.GetOcc(gx, gz) != 0) rMax = Math.Max(rMax, Math.Sqrt(gx * gx + gz * gz));

        double ringR = rMax * 0.8;
        const int samples = 720;
        bool[] hit = new bool[samples];
        for (int i = 0; i < samples; i++)
        {
            double a = 2 * Math.PI * i / samples;
            int gx = (int)Math.Round(ringR * Math.Cos(a));
            int gz = (int)Math.Round(ringR * Math.Sin(a));
            hit[i] = layer.GetOcc(gx, gz) != 0;
        }

        int lobes = 0;
        for (int i = 0; i < samples; i++)
        {
            int prev = (i - 1 + samples) % samples;
            if (hit[i] && !hit[prev]) lobes++;
        }
        return lobes;
    }

    private static bool HasBoundaryFlag(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!layer.TryGetTile(ti, tj, out var t) || t == null) return false;
        return (t.Flags[CoverageSpec.LocalIndex(gx, gz)] & (byte)CoverageFlags.Boundary) != 0;
    }

    /// <summary>Perimeter counted only against exterior-reachable empty cells (flood-filled from the domain
    /// border), so interior holes do not inflate it. Domain edges themselves count as exterior.</summary>
    private static int OuterPerimeter(CoverageLayer layer, int half)
    {
        var exterior = FloodExterior(layer, half);
        int w = half * 2 + 1;
        int perim = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            if (layer.GetOcc(gx, gz) == 0) continue;
            foreach (var (dx, dz) in new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = gx + dx, nz = gz + dz;
                if (nx < -half || nx > half || nz < -half || nz > half) { perim++; continue; }
                if (layer.GetOcc(nx, nz) != 0) continue;
                if (exterior[(nz + half) * w + (nx + half)]) perim++;
            }
        }
        return perim;
    }

    /// <summary>Occupied-cell count and count of empty cells NOT reachable from the domain border (interior holes).</summary>
    private static (int occupied, int holes) OccupiedAndHoles(CoverageLayer layer, int half)
    {
        var exterior = FloodExterior(layer, half);
        int w = half * 2 + 1;
        int occupied = 0, holes = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            if (layer.GetOcc(gx, gz) != 0) { occupied++; continue; }
            if (!exterior[(gz + half) * w + (gx + half)]) holes++;
        }
        return (occupied, holes);
    }

    /// <summary>Flood fill of empty cells reachable from the [-half, half] domain border.</summary>
    private static bool[] FloodExterior(CoverageLayer layer, int half)
    {
        int w = half * 2 + 1;
        var visited = new bool[w * w];
        var stack = new Stack<(int x, int z)>();
        for (int gx = -half; gx <= half; gx++) { stack.Push((gx, -half)); stack.Push((gx, half)); }
        for (int gz = -half; gz <= half; gz++) { stack.Push((-half, gz)); stack.Push((half, gz)); }
        while (stack.Count > 0)
        {
            var (x, z) = stack.Pop();
            if (x < -half || x > half || z < -half || z > half) continue;
            int idx = (z + half) * w + (x + half);
            if (visited[idx]) continue;
            if (layer.GetOcc(x, z) != 0) continue;
            visited[idx] = true;
            stack.Push((x + 1, z)); stack.Push((x - 1, z)); stack.Push((x, z + 1)); stack.Push((x, z - 1));
        }
        return visited;
    }

    private static bool IsSingleConnectedComponent(CoverageLayer layer, int half, byte occSlot)
    {
        int w = half * 2 + 1;
        int total = 0, startX = int.MinValue, startZ = 0;
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
            if (layer.GetOcc(gx, gz) == occSlot)
            {
                total++;
                if (startX == int.MinValue) { startX = gx; startZ = gz; }
            }
        if (total == 0) return true;

        var visited = new bool[w * w];
        var stack = new Stack<(int x, int z)>();
        stack.Push((startX, startZ));
        int reached = 0;
        while (stack.Count > 0)
        {
            var (x, z) = stack.Pop();
            if (x < -half || x > half || z < -half || z > half) continue;
            int idx = (z + half) * w + (x + half);
            if (visited[idx]) continue;
            if (layer.GetOcc(x, z) != occSlot) continue;
            visited[idx] = true;
            reached++;
            stack.Push((x + 1, z)); stack.Push((x - 1, z)); stack.Push((x, z + 1)); stack.Push((x, z - 1));
        }
        return reached == total;
    }

    /// <summary>
    /// Growth-lab timelapse writer, coloured for legibility (orchestrator review): hue by species (Occ), brightness
    /// by biomass B, Boundary-flagged cells drawn dark (prothallus line), empty cells dark grey.
    /// </summary>
    private static void RunAndWriteColoredFrames(string scenario, CoverageLayer layer, int steps, int half, Action<long> stepFn)
    {
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        for (long s = 0; s < steps; s++)
        {
            stepFn(s);
            WriteColoredFrame(scenario, (int)s, layer, half);
        }
    }

    private static void WriteColoredFrame(string scenario, int frameIndex, CoverageLayer layer, int half)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            byte occ = layer.GetOcc(gx, gz);
            int px = gx + half, py = gz + half;
            int o = (py * size + px) * 3;
            if (occ == 0) { rgb[o] = 18; rgb[o + 1] = 18; rgb[o + 2] = 18; continue; }
            if (HasBoundaryFlag(layer, gx, gz)) { rgb[o] = 8; rgb[o + 1] = 8; rgb[o + 2] = 8; continue; }

            var (r, g, b) = SpeciesHue(occ);
            double k = 0.35 + 0.65 * Math.Clamp(layer.GetB(gx, gz), 0f, 1f);
            rgb[o] = (byte)(r * k); rgb[o + 1] = (byte)(g * k); rgb[o + 2] = (byte)(b * k);
        }
        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    private static (int r, int g, int b) SpeciesHue(byte occSlot) => occSlot switch
    {
        1 => (215, 140, 40),   // orange/rust
        2 => (90, 120, 220),   // blue-violet
        3 => (110, 200, 110),  // green
        _ => (170, 170, 170),
    };

    private static string Digest(CoverageLayer layer)
    {
        using var d = new Core.DigestBuilder();
        foreach (var t in layer.Tiles) { d.Add(t.Ti); d.Add(t.Tj); d.Add(t.Occ); d.Add(t.W); d.Add(t.Flags); d.Add(t.D2E); }
        return d.Hex();
    }
}
