using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Rules;
using Vivarium.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>
/// Growth-lab moss scenarios (docs/overhaul/growth_models.md §12): synthetic <see cref="IMicroEnvSource"/>
/// fields drive <see cref="MatRules.Step"/> directly, no <c>VivariumWorld</c> involved.
/// </summary>
[Trait("Suite", "GrowthLab")]
public class MossScenarios
{
    private readonly ITestOutputHelper _out;
    public MossScenarios(ITestOutputHelper output) => _out = output;

    private const long Seed = 4242;

    private sealed class FuncEnv : IMicroEnvSource
    {
        private readonly Func<int, int, MicroEnv> _f;
        public FuncEnv(Func<int, int, MicroEnv> f) => _f = f;
        public MicroEnv Sample(int gx, int gz) => _f(gx, gz);
    }

    private sealed class RecordingSink : IDetritusSink
    {
        public double DetritusTotal;
        public readonly Dictionary<(int, int), double> MoistureBonus = new();
        public void AddDetritus(int gx, int gz, double amount) => DetritusTotal += amount;
        // Spreads to the 3x3 neighbourhood, standing in for the real MoistureBonusField's coarse env-grid cell
        // (§2, §4.5) covering many fine cells around the occupied one — a bonus written at the colony edge is
        // visible to the still-empty margin cell it is trying to wet, not just to the occupied cell itself.
        public void AddMoistureBonus(int gx, int gz, double amount)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var key = (gx + dx, gz + dz);
                MoistureBonus.TryGetValue(key, out var cur);
                MoistureBonus[key] = cur + amount;
            }
        }
    }

    private static MicroEnv Env(double moisture, double light = 0.8) => new()
    {
        Moisture = moisture,
        Humidity = moisture,
        Light = light,
        Nutrients = 0,
        Detritus = 0,
        Substrate = CoverageSubstrate.StableSoil,
        Slope = 0,
        DownslopeDir = Vec2.Zero,
        MoistureGradient = Vec2.Zero,
    };

    private static MatParams Carpet(byte id = 1, double wMin = 0.25, double wOpt = 0.6, double hardMin = 0.3) => new(
        OccupantId: id, Name: "carpet", HeightForm: MatHeightForm.Flat,
        Lateral: 0.8, MaxHeightM: 0.004, GrowthRate: 1.5,
        KWet: 6, KDry: 1.2, WMin: wMin, WOpt: wOpt, KLight: 0.3, LightMax: 1.0,
        DormBrownDays: 1, DormDeathDays: 3, SporeRate: 0.0005, HardMinMoisture: hardMin, SeedBiomass: 0.05);

    // ------------------------------------------------------------------ moss_gradient

    [Fact]
    public void MossGradientFrontIsFasterOnWetSideAndBlockedBelowHardMin()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 1);
        var p = Carpet();
        layer.SetCell(0, 0, p.OccupantId, 0.6f, 200, 0, 0, 0, 0);

        var env = new FuncEnv((gx, gz) => gx >= 0 ? Env(0.9) : gx >= -20 ? Env(0.35) : Env(0.05));

        for (long s = 0; s < 30; s++)
            MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: s, seed: Seed, sink: null);

        int wetExtent = MaxOccupiedGx(layer, dir: 1);
        int dryExtent = -MinOccupiedGx(layer, dir: -1);

        Assert.True(wetExtent > 0, "wet side did not grow");
        Assert.True(wetExtent > 2 * Math.Max(1, dryExtent), $"front speed ratio {wetExtent}/{dryExtent} <= 2");

        for (int gx = -60; gx <= -21; gx++)
            Assert.True(layer.GetOcc(gx, 0) == 0, $"colonised below HardMinMoisture at gx={gx}");
    }

    private static int MaxOccupiedGx(CoverageLayer layer, int dir)
    {
        int best = 0;
        foreach (var t in layer.Tiles)
        for (int lz = 0; lz < CoverageSpec.TileEdge; lz++)
        for (int lx = 0; lx < CoverageSpec.TileEdge; lx++)
        {
            int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
            if (gz != 0 || t.Occ[lz * CoverageSpec.TileEdge + lx] == 0) continue;
            if (dir > 0 && gx > best) best = gx;
        }
        return best;
    }

    private static int MinOccupiedGx(CoverageLayer layer, int dir)
    {
        int best = 0;
        foreach (var t in layer.Tiles)
        for (int lz = 0; lz < CoverageSpec.TileEdge; lz++)
        for (int lx = 0; lx < CoverageSpec.TileEdge; lx++)
        {
            int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
            if (gz != 0 || t.Occ[lz * CoverageSpec.TileEdge + lx] == 0) continue;
            if (dir < 0 && gx < best) best = gx;
        }
        return best;
    }

    // ------------------------------------------------------------------ moss_drought_recovery
    // A disc-shaped colony where a "refuge" strip (gx <= -2) stays just above WMin during the drought (a seep,
    // shade, or simply less exposure) while the rest of the colony dries fully and dies. The refuge survives
    // (well over 30% of the colony), browning and death holes appear in the exposed majority, and once
    // rewetted the refuge's surviving biomass spreads laterally to refill the holes.

    private static int CountOccupied(CoverageLayer layer)
    {
        int n = 0;
        foreach (var t in layer.Tiles)
            for (int i = 0; i < CoverageTile.N; i++)
                if (t.Occ[i] != 0) n++;
        return n;
    }

    private static void SeedDroughtDisc(CoverageLayer layer, MatParams p)
    {
        for (int gx = -4; gx <= 4; gx++)
        for (int gz = -4; gz <= 4; gz++)
            if (gx * gx + gz * gz <= 16) layer.SetCell(gx, gz, p.OccupantId, 0.8f, 220, 20, 0, 0, 0);
    }

    /// <summary>phase: 0 = moist, 1 = drought (refuge at gx&lt;=-2 stays just above WMin), 2 = rewet.</summary>
    private static FuncEnv DroughtEnv(Func<int> phase) => new((gx, _) => phase() switch
    {
        1 => gx <= -2 ? Env(0.35) : Env(0.0),
        _ => Env(0.9),
    });

    [Fact]
    public void MossDroughtRecoveryDipsThenRecoversAboveSeventyPercent()
    {
        var p = Carpet();
        var layer = new CoverageLayer(CoverageLayerId.Mat, 2);
        SeedDroughtDisc(layer, p);

        int phase = 0;
        var env = DroughtEnv(() => phase);

        long step = 0;
        for (int i = 0; i < 6; i++, step++) { phase = 0; MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null); }
        int preDroughtCover = CountOccupied(layer);

        bool sawBrowning = false;
        int minCoverDuringDrought = preDroughtCover;
        for (int i = 0; i < 8; i++, step++)
        {
            phase = 1;
            MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null);
            minCoverDuringDrought = Math.Min(minCoverDuringDrought, CountOccupied(layer));
            foreach (var t in layer.Tiles)
                for (int li = 0; li < CoverageTile.N; li++)
                    if (t.Occ[li] != 0 && MatRules.IsBrowning(t.Dorm[li], p)) sawBrowning = true;
        }
        Assert.True(sawBrowning, "no cell browned during drought");
        Assert.True(minCoverDuringDrought < preDroughtCover, "cover never dipped during drought");
        Assert.True(minCoverDuringDrought > 0.20 * preDroughtCover, $"refuge did not survive: cover fell to {minCoverDuringDrought}/{preDroughtCover}");

        for (int i = 0; i < 25; i++, step++) { phase = 2; MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null); }
        int finalCover = CountOccupied(layer);

        Assert.True(finalCover > 0.70 * preDroughtCover, $"cover only recovered to {finalCover}/{preDroughtCover} (need > 70%)");
    }

    // ------------------------------------------------------------------ moss_competition
    // Two species with disjoint moisture preferences, each seeded on the side of a moisture step that favours
    // it, with an empty gap between the blocks. Each spreads across its own half of the gap (its own
    // HardMinMoisture/WMin excludes it from the other's side) and the two fronts meet and stall exactly at the
    // moisture boundary: a genuine seam, not a takeover, because competition compares each species' suitability
    // AT THE CONTESTED CELL, not at its home cell.

    private static (MatParams favoured, MatParams other) CompetitionSpecies() => (
        new MatParams(
            OccupantId: 1, Name: "favoured", HeightForm: MatHeightForm.Flat,
            Lateral: 0.8, MaxHeightM: 0.004, GrowthRate: 1.5,
            KWet: 6, KDry: 1.2, WMin: 0.4, WOpt: 0.7, KLight: 0.3, LightMax: 1.0,
            DormBrownDays: 3, DormDeathDays: 15, SporeRate: 0.0002, HardMinMoisture: 0.4, SeedBiomass: 0.05),
        new MatParams(
            OccupantId: 2, Name: "other", HeightForm: MatHeightForm.Flat,
            Lateral: 0.8, MaxHeightM: 0.004, GrowthRate: 1.5,
            KWet: 6, KDry: 1.2, WMin: 0.05, WOpt: 0.3, KLight: 0.3, LightMax: 1.0,
            DormBrownDays: 3, DormDeathDays: 15, SporeRate: 0.0002, HardMinMoisture: 0.05, SeedBiomass: 0.05));

    /// <summary>gx &lt; 0 favours "favoured" (wet, 0.7); gx &gt;= 0 favours "other" (dry, 0.2).</summary>
    private static FuncEnv CompetitionEnv() => new((gx, _) => gx < 0 ? Env(0.7) : Env(0.2));

    private static void SeedCompetitionBlocks(CoverageLayer layer, MatParams favoured, MatParams other)
    {
        for (int gx = -14; gx <= -8; gx++)
        for (int gz = -6; gz <= 6; gz++)
            layer.SetCell(gx, gz, favoured.OccupantId, 0.8f, 220, 10, 0, 0, 0);
        for (int gx = 7; gx <= 13; gx++)
        for (int gz = -6; gz <= 6; gz++)
            layer.SetCell(gx, gz, other.OccupantId, 0.8f, 220, 10, 0, 0, 0);
        // gx in [-7, 6] is left empty as the gap the two fronts grow across and meet in.
    }

    [Fact]
    public void MossCompetitionFormsSeamAndBothSpeciesHoldTheirGround()
    {
        var (favoured, other) = CompetitionSpecies();
        var layer = new CoverageLayer(CoverageLayerId.Mat, 4);
        SeedCompetitionBlocks(layer, favoured, other);

        var env = CompetitionEnv();
        var species = new[] { favoured, other };
        for (long s = 0; s < 40; s++)
            MatRules.Step(layer, env, species, dtDays: 1.0, step: s, seed: Seed, sink: null);

        int total = 27 * 13; // gx in [-14,13), gz in [-6,6]
        int finalFavoured = CountOccupant(layer, favoured.OccupantId);
        int finalOther = CountOccupant(layer, other.OccupantId);

        Assert.True(finalFavoured > 0.25 * total, $"favoured only {finalFavoured}/{total} cells");
        Assert.True(finalOther > 0.25 * total, $"other only {finalOther}/{total} cells (species vanished — no seam)");

        bool seamExists = false;
        for (int gx = -14; gx <= 13 && !seamExists; gx++)
        for (int gz = -6; gz <= 6 && !seamExists; gz++)
        {
            byte occ = layer.GetOcc(gx, gz);
            if (occ == 0) continue;
            if (layer.GetOcc(gx + 1, gz) is byte n && n != 0 && n != occ) seamExists = true;
        }
        Assert.True(seamExists, "no seam: one species occupies the whole contested region");
    }

    private static int CountOccupant(CoverageLayer layer, byte occ)
    {
        int n = 0;
        foreach (var t in layer.Tiles)
            for (int i = 0; i < CoverageTile.N; i++)
                if (t.Occ[i] == occ) n++;
        return n;
    }

    // ------------------------------------------------------------------ cushion_dome

    [Fact]
    public void CushionDomeHeightIsMonotoneWithLowRim()
    {
        const int radius = 10;
        var p = new MatParams(
            OccupantId: 1, Name: "cushion", HeightForm: MatHeightForm.Dome,
            Lateral: 0.2, MaxHeightM: 0.025, GrowthRate: 0.5,
            KWet: 6, KDry: 1.2, WMin: 0.2, WOpt: 0.5, KLight: 0.3, LightMax: 1.0,
            DormBrownDays: 4, DormDeathDays: 20, SporeRate: 0, HardMinMoisture: 0.0, SeedBiomass: 0.05, DomeLength: 6.0);

        var layer = new CoverageLayer(CoverageLayerId.Mat, 5);
        for (int gx = -radius; gx <= radius; gx++)
        for (int gz = -radius; gz <= radius; gz++)
            if (gx * gx + gz * gz <= radius * radius)
                layer.SetCell(gx, gz, p.OccupantId, 0.9f, 200, 50, 0, 0, 0);

        var env = new FuncEnv((_, _) => Env(0.5));
        MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: 0, seed: Seed, sink: null);

        double prevHeight = double.MaxValue;
        double centerHeight = HeightAt(layer, p, 0, 0);
        bool monotone = true;
        for (int gx = 0; gx <= radius; gx++)
        {
            if (layer.GetOcc(gx, 0) == 0) break;
            double h = HeightAt(layer, p, gx, 0);
            if (h > prevHeight + 1e-9) monotone = false;
            prevHeight = h;
        }
        Assert.True(monotone, "dome height is not monotone toward the centre");

        double rimHeight = HeightAt(layer, p, radius - 1, 0);
        Assert.True(rimHeight < 0.30 * centerHeight, $"rim height {rimHeight} >= 30% of centre {centerHeight}");
    }

    private static double HeightAt(CoverageLayer layer, MatParams p, int gx, int gz)
    {
        layer.TryGetTile(CoverageSpec.TileOf(gx, gz).Item1, CoverageSpec.TileOf(gx, gz).Item2, out var t);
        if (t == null) return 0;
        int li = CoverageSpec.LocalIndex(gx, gz);
        return MatRules.Height(p, t.B[li], t.D2E[li]);
    }

    // ------------------------------------------------------------------ sphagnum_feedback

    [Fact]
    public void SphagnumFeedbackExpandsIntoMarginalWetnessOnlyWithFeedback()
    {
        int Run(double moistureFeedback)
        {
            var p = new MatParams(
                OccupantId: 1, Name: "sphagnum", HeightForm: MatHeightForm.Dome,
                Lateral: 0.8, MaxHeightM: 0.03, GrowthRate: 1.5,
                KWet: 6, KDry: 1.2, WMin: 0.3, WOpt: 0.6, KLight: 0.3, LightMax: 1.0,
                DormBrownDays: 2, DormDeathDays: 8, SporeRate: 0.0005, MoistureFeedback: moistureFeedback,
                HardMinMoisture: 0.3, SeedBiomass: 0.05);

            var layer = new CoverageLayer(CoverageLayerId.Mat, 6);
            for (int gx = -5; gx <= 0; gx++)
            for (int gz = -3; gz <= 3; gz++)
                layer.SetCell(gx, gz, p.OccupantId, 0.8f, 220, 20, 0, 0, 0);

            var sink = new RecordingSink();
            var env = new FuncEnv((gx, gz) =>
            {
                double baseMoisture = gx <= 0 ? 0.8 : 0.28; // marginal zone just below WMin (0.3)
                sink.MoistureBonus.TryGetValue((gx, gz), out var bonus);
                return Env(Math.Clamp(baseMoisture + bonus, 0, 1));
            });

            for (long s = 0; s < 25; s++)
                MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: s, seed: Seed, sink: sink);

            return MaxOccupiedGx(layer, dir: 1);
        }

        int withFeedback = Run(0.05);
        int withoutFeedback = Run(0.0);

        Assert.True(withFeedback > withoutFeedback, $"feedback ({withFeedback}) did not expand further than control ({withoutFeedback})");
    }

    // ------------------------------------------------------------------ determinism

    [Fact]
    [Trait("Suite", "Coverage")]
    public void MatRulesStepIsDeterministicAcrossTwoRuns()
    {
        var p = Carpet();
        var env = new FuncEnv((gx, gz) => gx >= 0 ? Env(0.9) : Env(0.35));

        CoverageLayer Build()
        {
            var layer = new CoverageLayer(CoverageLayerId.Mat, 7);
            layer.SetCell(0, 0, p.OccupantId, 0.6f, 200, 0, 0, 0, 0);
            for (long s = 0; s < 20; s++)
                MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: s, seed: Seed, sink: null);
            return layer;
        }

        var a = Build();
        var b = Build();
        Assert.Equal(Digest(a), Digest(b));
    }

    private static string Digest(CoverageLayer layer)
    {
        using var d = new Core.DigestBuilder();
        foreach (var t in layer.Tiles)
        {
            d.Add(t.Ti); d.Add(t.Tj);
            d.Add(t.Occ); d.Add(t.W); d.Add(t.Dorm); d.Add(t.Flags); d.Add(t.D2E);
            foreach (var b in t.B) d.Add(b);
            foreach (var a in t.Age) d.Add((int)a);
        }
        return d.Hex();
    }

    // ------------------------------------------------------------------ perf_dense

    [Fact]
    [Trait("Suite", "Coverage")]
    public void PerfDenseStepCost()
    {
        var p = Carpet();
        var layer = new CoverageLayer(CoverageLayerId.Mat, 8);
        const int tilesPerSide = 20; // 400 tiles total
        for (int ti = 0; ti < tilesPerSide; ti++)
        for (int tj = 0; tj < tilesPerSide; tj++)
        {
            var t = layer.GetOrCreateTile(ti, tj);
            for (int i = 0; i < CoverageTile.N; i++)
            {
                // seeded at (near) the species' biomass cap and target water: physiology reaches equilibrium
                // almost immediately, matching a colony that has been stable for a while (§10's steady-state case).
                t.Occ[i] = p.OccupantId; t.B[i] = 0.9998f; t.W[i] = (byte)Math.Round(p.WOpt * 255); t.Age[i] = 400; t.Dorm[i] = 0; t.Flags[i] = 0; t.D2E[i] = 0;
            }
            t.Active = true;
        }
        Assert.Equal(tilesPerSide * tilesPerSide, layer.TileCount);

        var env = new FuncEnv((_, _) => Env(0.6));

        var sw0 = Stopwatch.StartNew();
        var cold = MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: 0, seed: Seed, sink: null);
        sw0.Stop();
        _out.WriteLine($"dense-step ms (cold, D2E+rim uninitialised): {sw0.Elapsed.TotalMilliseconds:F2} total | physiology {cold.PhysiologyMs:F2} | spread {cold.SpreadMs:F2} | d2e {cold.D2EMs:F2}");

        // warm up until interior tiles latch Steady (equilibrium biomass/water/dormancy) — realistic per-flora-step cost.
        for (long s = 1; s < 5; s++) MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: s, seed: Seed, sink: null);

        var sw1 = Stopwatch.StartNew();
        var steady = MatRules.Step(layer, env, new[] { p }, dtDays: 1.0, step: 5, seed: Seed, sink: null);
        sw1.Stop();
        _out.WriteLine($"dense-step ms (steady, §10 budget target): {sw1.Elapsed.TotalMilliseconds:F2} total | physiology {steady.PhysiologyMs:F2} | spread {steady.SpreadMs:F2} | d2e {steady.D2EMs:F2} ({tilesPerSide * tilesPerSide} tiles, {tilesPerSide * tilesPerSide * CoverageTile.N} cells)");
    }

    // ==================================================================== §12 timelapse frames (Speed=Slow)
    // Renders occupancy to PNG: hue by species, brightness by biomass (dome forms use height instead),
    // browning cells tinted toward brown, cells that died this step flagged dark red-brown and faded over a
    // few frames (a render cue only — the simulation itself just clears a dead cell, per §4.1).

    private static readonly (byte r, byte g, byte b)[] Palette =
    {
        (40, 160, 60),   // species 1: carpet green
        (60, 120, 200),  // species 2: blue-teal
        (170, 140, 40),  // species 3: cushion gold
        (50, 130, 95),   // species 4: sphagnum deep green
    };

    private static void RunWithFrames(string scenario, CoverageLayer layer, int steps, int half, IMicroEnvSource env, IReadOnlyList<MatParams> species, double dtDays, ulong seed, IDetritusSink? sink)
    {
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        var byId = new MatParams?[256];
        foreach (var s in species) byId[s.OccupantId] = s;
        var colorOf = new (byte, byte, byte)[256];
        for (int i = 0; i < species.Count; i++) colorOf[species[i].OccupantId] = Palette[i % Palette.Length];

        var prevOcc = new Dictionary<(int, int), byte>();
        var deadFade = new Dictionary<(int, int), int>();

        for (long s = 0; s < steps; s++)
        {
            MatRules.Step(layer, env, species, dtDays, s, seed, sink);
            WriteMossFrame(scenario, (int)s, layer, half, byId, colorOf, prevOcc, deadFade);
        }
    }

    private static void WriteMossFrame(string scenario, int frameIndex, CoverageLayer layer, int half, MatParams?[] byId, (byte r, byte g, byte b)[] colorOf, Dictionary<(int, int), byte> prevOcc, Dictionary<(int, int), int> deadFade)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];

        for (int gz = -half; gz <= half; gz++)
        for (int gx = -half; gx <= half; gx++)
        {
            var key = (gx, gz);
            byte occ = layer.GetOcc(gx, gz);
            prevOcc.TryGetValue(key, out byte wasOcc);
            int px = gx + half, py = gz + half;
            int o = (py * size + px) * 3;
            byte r, g, b;

            if (occ != 0)
            {
                var p = byId[occ]!;
                var (br, bg, bb) = colorOf[occ];
                float bio = layer.GetB(gx, gz);
                byte dorm = 0, d2e = 0;
                layer.TryGetTile(CoverageSpec.TileOf(gx, gz).ti, CoverageSpec.TileOf(gx, gz).tj, out var t);
                if (t != null)
                {
                    int li = CoverageSpec.LocalIndex(gx, gz);
                    dorm = t.Dorm[li];
                    d2e = t.D2E[li];
                }

                double intensity = p.HeightForm == MatHeightForm.Dome
                    ? Math.Clamp(MatRules.Height(p, bio, d2e) / Math.Max(1e-6, p.MaxHeightM), 0, 1)
                    : Math.Clamp(0.25 + 0.75 * bio, 0, 1);

                r = (byte)(br * intensity);
                g = (byte)(bg * intensity);
                b = (byte)(bb * intensity);

                if (MatRules.IsBrowning(dorm, p))
                {
                    // blend toward brown (120, 80, 30) as dormancy deepens
                    double t2 = Math.Clamp(dorm / Math.Max(1.0, p.DormDeathDays), 0, 1);
                    r = (byte)(r * (1 - t2) + 120 * t2);
                    g = (byte)(g * (1 - t2) + 80 * t2);
                    b = (byte)(b * (1 - t2) + 30 * t2);
                }

                deadFade.Remove(key);
            }
            else
            {
                if (wasOcc != 0) deadFade[key] = 5;
                if (deadFade.TryGetValue(key, out int fade) && fade > 0)
                {
                    byte v = (byte)(30 + fade * 15);
                    r = v; g = (byte)(v / 3); b = (byte)(v / 4);
                    deadFade[key] = fade - 1;
                }
                else
                {
                    r = 18; g = 18; b = 18;
                }
            }

            rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            prevOcc[key] = occ;
        }

        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void MossGradientWritesTimelapseFrames()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 101);
        var p = Carpet();
        layer.SetCell(0, 0, p.OccupantId, 0.6f, 200, 0, 0, 0, 0);
        var env = new FuncEnv((gx, gz) => gx >= 0 ? Env(0.9) : gx >= -20 ? Env(0.35) : Env(0.05));

        RunWithFrames("moss_gradient", layer, steps: 30, half: 24, env, new[] { p }, dtDays: 1.0, seed: Seed, sink: null);

        Assert.Equal(30, Directory.GetFiles(GrowthLabRunner.FramesDir("moss_gradient"), "frame_*.png").Length);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void MossDroughtRecoveryWritesTimelapseFrames()
    {
        var p = Carpet();
        var layer = new CoverageLayer(CoverageLayerId.Mat, 102);
        SeedDroughtDisc(layer, p);

        int phase = 0; // 0 = moist, 1 = drought (refuge at gx<=-2 survives), 2 = rewet
        var env = DroughtEnv(() => phase);

        string dir = GrowthLabRunner.FramesDir("moss_drought_recovery");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var byId = new MatParams?[256] ;
        byId[p.OccupantId] = p;
        var colorOf = new (byte, byte, byte)[256];
        colorOf[p.OccupantId] = Palette[0];
        var prevOcc = new Dictionary<(int, int), byte>();
        var deadFade = new Dictionary<(int, int), int>();

        long step = 0;
        for (int i = 0; i < 6; i++, step++) { phase = 0; MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null); WriteMossFrame("moss_drought_recovery", (int)step, layer, 12, byId, colorOf, prevOcc, deadFade); }
        for (int i = 0; i < 8; i++, step++) { phase = 1; MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null); WriteMossFrame("moss_drought_recovery", (int)step, layer, 12, byId, colorOf, prevOcc, deadFade); }
        for (int i = 0; i < 25; i++, step++) { phase = 2; MatRules.Step(layer, env, new[] { p }, 1.0, step, Seed, null); WriteMossFrame("moss_drought_recovery", (int)step, layer, 12, byId, colorOf, prevOcc, deadFade); }

        Assert.Equal((int)step, Directory.GetFiles(dir, "frame_*.png").Length);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void MossCompetitionWritesTimelapseFrames()
    {
        var (favoured, other) = CompetitionSpecies();
        var layer = new CoverageLayer(CoverageLayerId.Mat, 103);
        SeedCompetitionBlocks(layer, favoured, other);

        var env = CompetitionEnv();
        RunWithFrames("moss_competition", layer, steps: 40, half: 16, env, new[] { favoured, other }, dtDays: 1.0, seed: Seed, sink: null);

        Assert.Equal(40, Directory.GetFiles(GrowthLabRunner.FramesDir("moss_competition"), "frame_*.png").Length);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CushionDomeWritesTimelapseFrames()
    {
        const int radius = 10;
        var p = new MatParams(
            OccupantId: 1, Name: "cushion", HeightForm: MatHeightForm.Dome,
            Lateral: 0.2, MaxHeightM: 0.025, GrowthRate: 0.5,
            KWet: 6, KDry: 1.2, WMin: 0.2, WOpt: 0.5, KLight: 0.3, LightMax: 1.0,
            DormBrownDays: 4, DormDeathDays: 20, SporeRate: 0, HardMinMoisture: 0.0, SeedBiomass: 0.05, DomeLength: 6.0);

        var layer = new CoverageLayer(CoverageLayerId.Mat, 104);
        for (int gx = -radius; gx <= radius; gx++)
        for (int gz = -radius; gz <= radius; gz++)
            if (gx * gx + gz * gz <= radius * radius)
                layer.SetCell(gx, gz, p.OccupantId, 0.9f, 200, 50, 0, 0, 0);

        var env = new FuncEnv((_, _) => Env(0.5));
        RunWithFrames("cushion_dome", layer, steps: 10, half: radius + 2, env, new[] { p }, dtDays: 1.0, seed: Seed, sink: null);

        Assert.Equal(10, Directory.GetFiles(GrowthLabRunner.FramesDir("cushion_dome"), "frame_*.png").Length);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void SphagnumFeedbackWritesTimelapseFrames()
    {
        var p = new MatParams(
            OccupantId: 1, Name: "sphagnum", HeightForm: MatHeightForm.Dome,
            Lateral: 0.8, MaxHeightM: 0.03, GrowthRate: 1.5,
            KWet: 6, KDry: 1.2, WMin: 0.3, WOpt: 0.6, KLight: 0.3, LightMax: 1.0,
            DormBrownDays: 2, DormDeathDays: 8, SporeRate: 0.0005, MoistureFeedback: 0.05,
            HardMinMoisture: 0.3, SeedBiomass: 0.05);

        var layer = new CoverageLayer(CoverageLayerId.Mat, 105);
        for (int gx = -5; gx <= 0; gx++)
        for (int gz = -3; gz <= 3; gz++)
            layer.SetCell(gx, gz, p.OccupantId, 0.8f, 220, 20, 0, 0, 0);

        var sink = new RecordingSink();
        var env = new FuncEnv((gx, gz) =>
        {
            double baseMoisture = gx <= 0 ? 0.8 : 0.28;
            sink.MoistureBonus.TryGetValue((gx, gz), out var bonus);
            return Env(Math.Clamp(baseMoisture + bonus, 0, 1));
        });

        RunWithFrames("sphagnum_feedback", layer, steps: 25, half: 10, env, new[] { p }, dtDays: 1.0, seed: Seed, sink: sink);

        Assert.Equal(25, Directory.GetFiles(GrowthLabRunner.FramesDir("sphagnum_feedback"), "frame_*.png").Length);
    }
}
