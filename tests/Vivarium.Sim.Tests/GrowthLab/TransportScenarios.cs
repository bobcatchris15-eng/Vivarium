using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Plasmodium;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>Growth-lab proofs for local per-cell mass and conservative transport (docs/overhaul/growth_models.md
/// §6R items 1, 5, 6): mass moves only by conservative flow along sheet adjacencies, colonisation/withdrawal are
/// exact transfers, and a long strip with food at only one end thins and withdraws at the far end purely from
/// the pressure gradient — no steering code involved.</summary>
[Trait("Suite", "GrowthLab")]
public class TransportScenarios
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

    // ------------------------------------------------------------------ transport_far_end_drains

    private const int StripHalf = 10; // strip spans gx in [-StripHalf, StripHalf] at gz = 0
    private const int FoodX = StripHalf; // food only at the +x end

    private static (PlasmodiumColony colony, CoverageLayer layer, Attractant attractant, TestEnv env) BuildStrip()
    {
        // Beta = LambdaF = 0: no foraging front extension at all, so any shape change is transport (+ m_min
        // withdrawal) alone — exactly what this scenario needs to isolate.
        // Maintenance (§6R item 1) is what actually makes a foodless region wither here: pure conservative
        // diffusion alone only equalises concentration (the far end, being the low end of the gradient, would be
        // a net *importer*, never draining) — it takes a constant per-cell upkeep cost the food end can outrun
        // via feeding and the far end cannot outrun via the trickle of diffusive resupply over 40 cells for §6R
        // item 6's withdrawal to have anything to act on. This is local maintenance + local feeding +
        // conservative transport exactly as specced, not steering: nothing here ever looks at food position.
        var prm = new PlasmodiumParams
        {
            Beta = 0, LambdaF = 0, InitialMass = 100, FeedRate = 6.0,
            SheetConductance = 0.3, MMin = 0.03, MaintenanceRate = 0.15,
        };
        var env = new TestEnv();
        for (int fz = -1; fz <= 1; fz++) env.DetritusMap[(FoodX, fz)] = 1e6;

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 91);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        var seedCells = new List<(int, int)>();
        for (int gx = -StripHalf; gx <= StripHalf; gx++) seedCells.Add((gx, 0));
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        return (colony, layer, attractant, env);
    }

    private static double MassCentroidX(PlasmodiumColony colony)
    {
        double sum = 0, wsum = 0;
        foreach (var ((gx, _), m) in colony.Mass)
        {
            sum += gx * m;
            wsum += m;
        }
        return wsum <= 0 ? 0 : sum / wsum;
    }

    private static int FarEndCellCount(PlasmodiumColony colony)
    {
        // "far end" = the quarter of the strip furthest from food, i.e. gx <= -StripHalf/2.
        return colony.CellId.Keys.Count(c => c.gx <= -StripHalf / 2);
    }

    [Fact]
    public void FarEndDrainsTowardFoodWithNoSteering()
    {
        var (colony, layer, attractant, env) = BuildStrip();

        double centroidBefore = MassCentroidX(colony);
        int farEndBefore = FarEndCellCount(colony);
        Assert.True(farEndBefore > 0, "far end starts with no cells to drain");

        double initialTotal = colony.TotalMass();

        for (long s = 0; s < 900; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 1.0);
            colony.Relabel(colony.CellId.Keys.ToList());

            // Mass conservation assert every step (§6R item 1): total mass always equals the initial total plus
            // everything fed in, minus everything tallied removed (maintenance + m_min withdrawal) — no other
            // path exists for mass to appear or vanish.
            double expected = initialTotal + colony.FedMass - colony.RemovedMass;
            double actual = colony.TotalMass();
            double tol = Math.Max(1e-9, Math.Abs(expected) * 1e-9);
            Assert.True(Math.Abs(actual - expected) <= tol,
                $"mass not conserved at step {s}: expected {expected}, actual {actual} (diff {actual - expected})");
        }

        double centroidAfter = MassCentroidX(colony);
        int farEndAfter = FarEndCellCount(colony);

        Assert.True(centroidAfter > centroidBefore,
            $"mass centroid did not move toward food: before {centroidBefore}, after {centroidAfter}");
        Assert.True(farEndAfter < farEndBefore,
            $"far-end cell count did not decrease: before {farEndBefore}, after {farEndAfter}");
    }

    [Fact]
    public void FarEndDrainsScenarioIsDeterministic()
    {
        (string digest, double centroid) Run()
        {
            var (colony, layer, attractant, env) = BuildStrip();
            for (long s = 0; s < 900; s++)
            {
                colony.Step(layer, attractant, env, s, dt: 1.0);
                colony.Relabel(colony.CellId.Keys.ToList());
            }
            using var d = new Core.DigestBuilder();
            foreach (var (cell, m) in colony.Mass.OrderBy(kv => kv.Key)) { d.Add(cell.gx); d.Add(cell.gz); d.Add(m); }
            return (d.Hex(), MassCentroidX(colony));
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.digest, b.digest);
        Assert.Equal(a.centroid, b.centroid, precision: 12);
    }

    /// <summary>Slow: renders the far-end-drains timelapse, mass as brightness.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void FarEndDrainsScenarioWritesTimelapseFrames()
    {
        var (colony, layer, attractant, env) = BuildStrip();

        const string scenario = "transport_far_end_drains";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        double maxMassSeen = 1e-9;
        for (int s = 0; s < 900; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 1.0);
            colony.Relabel(colony.CellId.Keys.ToList());
            foreach (var m in colony.Mass.Values) if (m > maxMassSeen) maxMassSeen = m;
            if (s % 5 == 0 || s == 899) WriteMassFrame(scenario, s, colony, maxMassSeen);
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    private static void WriteMassFrame(string scenario, int frameIndex, PlasmodiumColony colony, double maxMass)
    {
        int half = StripHalf + 2;
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        for (int gz = -2; gz <= 2; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                int px = gx + half, py = gz + 2;
                int o = (py * size + px) * 3;
                double m = colony.MassAt(gx, gz);
                if (m <= 0) { rgb[o] = 12; rgb[o + 1] = 12; rgb[o + 2] = 18; continue; }
                byte v = (byte)Math.Clamp(40 + 215 * m / maxMass, 0, 255);
                rgb[o] = v; rgb[o + 1] = (byte)(v * 0.9); rgb[o + 2] = (byte)(v * 0.4); // warm = massier
            }
        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }
}
