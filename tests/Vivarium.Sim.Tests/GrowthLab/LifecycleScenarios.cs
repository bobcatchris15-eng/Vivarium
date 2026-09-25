using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Plasmodium;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>Growth-lab proofs for the Physarum life cycle (docs/overhaul/growth_models.md §6.1, §6.6, §13 row
/// G8a): starvation drives Migrating then Fruiting at former transport-network hubs, and drying drives
/// Sclerotium with a clean resume on rewetting.</summary>
[Trait("Suite", "GrowthLab")]
public class LifecycleScenarios
{
    private sealed class TestEnv : IPlasmodiumEnvironment
    {
        public double MoistureValue = 0.6;
        public readonly Dictionary<(int, int), double> DetritusMap = new();
        public double Moisture(int gx, int gz) => MoistureValue;
        public double Detritus(int gx, int gz) => DetritusMap.TryGetValue((gx, gz), out var v) ? v : 0;
        public double TakeDetritus(int gx, int gz, double amount)
        {
            if (amount <= 0 || !DetritusMap.TryGetValue((gx, gz), out var v) || v <= 0) return 0;
            double taken = Math.Min(v, amount);
            DetritusMap[(gx, gz)] = v - taken;
            return taken;
        }
    }

    private static Plasmodium? SoleOrg(PlasmodiumColony colony) =>
        colony.Organisms.Values.Any() ? colony.Organisms.Values.First() : null;

    private static bool IsLocalMax((int cx, int cz) node, double strength, IReadOnlyDictionary<(int, int), double> field)
    {
        (int dx, int dz)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        foreach (var (dx, dz) in dirs)
        {
            var n = (node.cx + dx, node.cz + dz);
            if (field.TryGetValue(n, out var nv) && nv > strength) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ physarum_starve

    private static (bool sawMigrating, bool sawFruiting, List<FruitingBody> bodies,
        Dictionary<(int, int), double> hubSnapshot, PlasmodiumColony colony, CoverageLayer layer,
        Network network, LifecycleController lifecycle)
        RunStarveScenario(int seed)
    {
        var prm = new PlasmodiumParams
        {
            InitialMass = 200, Beta = 0.05, LambdaF = 6.0, Sigma = 2.0, FeedRate = 4.0,
            // §6R item 1: seeded far from the sparse food rows, mean-detritus-over-all-cells starves from step
            // 0 regardless of mass model, exactly as before — but local mass now colonises this aggressively
            // (no shared pool throttling it), so TStarve/TMig are raised to give the transport network enough
            // steps to build real hub structure before Migrating -> Fruiting fires (needed for the hub-placement
            // assertions below to have anything to place bodies at).
            TStarve = 20.0, TMig = 20.0, StarveDetritusThreshold = 0.05,
            MassPerFruitingBody = 30.0, FruitingRipenSeconds = 3.0, FruitingDecaySeconds = 4.0, ResidueFadeSeconds = 4.0,
        };
        var env = new TestEnv();
        var source = (0, -4);
        var sink = (0, 4);
        // §6R item 1: local mass grows the sheet at a different pace than the retired shared pool did, so a
        // food patch just wide enough for the old model's growth rate let the mean-detritus-over-all-cells
        // starvation gate fire before phase 1 even finished growing the hub structure. Widened so mean detritus
        // stays above StarveDetritusThreshold for the whole of phase 1, exactly as the scenario intends.
        for (int fx = -3; fx <= 3; fx++)
        {
            env.DetritusMap[(fx, source.Item2 * 2)] = 1e6;
            env.DetritusMap[(fx, sink.Item2 * 2)] = 1e6;
        }

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: (ulong)seed);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        var network = new Network();
        var lifecycle = new LifecycleController(prm);
        colony.Seed(new[] { (0, 0) });
        layer.SetOcc(0, 0, 1);
        network.SetSources(new[] { source, sink });

        long s = 0;
        bool sawMigrating = false, sawFruiting = false;
        Dictionary<(int, int), double> hubSnapshot = new();
        List<FruitingBody> bodies = new();

        // Phase 1: grow with food present, building transport-network hub structure. §6R item 1's local mass
        // model grows/starves at a different pace than the retired shared pool did — a sparse, distant food
        // patch means the mean-detritus-over-all-cells starvation gate can fire well before phase 2's explicit
        // food removal, so the Migrating/Fruiting watch below spans both phases now, not just phase 2.
        for (; s < 150 && !sawFruiting; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
            lifecycle.Step(layer, colony, network, env, s, dt: 1.0);

            var p1Org = SoleOrg(colony);
            if (p1Org == null) break;
            if (p1Org.State == PlasmodiumState.Migrating) sawMigrating = true;
            if (p1Org.State == PlasmodiumState.Fruiting && !sawFruiting)
            {
                sawFruiting = true;
                foreach (var kv in network.NodeMaxD) hubSnapshot[kv.Key] = kv.Value;
                bodies = lifecycle.FruitingBodies.ToList();
            }
        }

        // Phase 2: food gone. Keep stepping growth/network (Migrating is not frozen — only Sclerotium is) so
        // starvation and the eventual Fruiting transition can be observed if phase 1 hasn't already reached it.
        env.DetritusMap.Clear();

        for (; s < 300 && !sawFruiting; s++)
        {
            var org = SoleOrg(colony);
            if (org == null) break;
            if (org.State != PlasmodiumState.Sclerotium)
            {
                network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
                colony.Step(layer, attractant, env, s, dt: 1.0, network);
                colony.Relabel(colony.CellId.Keys.ToList());
            }

            lifecycle.Step(layer, colony, network, env, s, dt: 1.0);

            org = SoleOrg(colony);
            if (org == null) break;
            if (org.State == PlasmodiumState.Migrating) sawMigrating = true;
            if (org.State == PlasmodiumState.Fruiting && !sawFruiting)
            {
                sawFruiting = true;
                foreach (var kv in network.NodeMaxD) hubSnapshot[kv.Key] = kv.Value;
                bodies = lifecycle.FruitingBodies.ToList();
                break;
            }
        }

        return (sawMigrating, sawFruiting, bodies, hubSnapshot, colony, layer, network, lifecycle);
    }

    [Fact]
    public void StarvationMigratesThenFruitsAtFormerHubs()
    {
        var (sawMigrating, sawFruiting, bodies, hubSnapshot, _, _, _, _) = RunStarveScenario(seed: 61);

        Assert.True(sawMigrating, "never observed Migrating state");
        Assert.True(sawFruiting, "never reached Fruiting state within budget");
        Assert.True(bodies.Count > 0, "no fruiting bodies placed");

        foreach (var body in bodies)
        {
            Assert.True(hubSnapshot.TryGetValue(body.Pos, out var strength), $"fruiting body at {body.Pos} is not a network node");
            Assert.True(IsLocalMax(body.Pos, strength, hubSnapshot), $"fruiting body at {body.Pos} is not a hub (local max of D)");
        }
    }

    [Fact]
    public void StarvationScenarioIsDeterministic()
    {
        var a = RunStarveScenario(seed: 61);
        var b = RunStarveScenario(seed: 61);
        Assert.Equal(a.sawMigrating, b.sawMigrating);
        Assert.Equal(a.sawFruiting, b.sawFruiting);
        Assert.Equal(a.bodies.Select(x => x.Pos).OrderBy(x => x).ToList(), b.bodies.Select(x => x.Pos).OrderBy(x => x).ToList());
    }

    /// <summary>Slow: renders the starve -> migrate -> fruit -> residue timelapse.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void StarveScenarioWritesTimelapseFrames()
    {
        var prm = new PlasmodiumParams
        {
            InitialMass = 200, Beta = 0.05, LambdaF = 6.0, Sigma = 2.0, FeedRate = 4.0,
            // §6R item 1: seeded far from the sparse food rows, mean-detritus-over-all-cells starves from step
            // 0 regardless of mass model, exactly as before — but local mass now colonises this aggressively
            // (no shared pool throttling it), so TStarve/TMig are raised to give the transport network enough
            // steps to build real hub structure before Migrating -> Fruiting fires (needed for the hub-placement
            // assertions below to have anything to place bodies at).
            TStarve = 20.0, TMig = 20.0, StarveDetritusThreshold = 0.05,
            MassPerFruitingBody = 30.0, FruitingRipenSeconds = 3.0, FruitingDecaySeconds = 4.0, ResidueFadeSeconds = 4.0,
        };
        var env = new TestEnv();
        var source = (0, -4);
        var sink = (0, 4);
        // §6R item 1: local mass grows the sheet at a different pace than the retired shared pool did, so a
        // food patch just wide enough for the old model's growth rate let the mean-detritus-over-all-cells
        // starvation gate fire before phase 1 even finished growing the hub structure. Widened so mean detritus
        // stays above StarveDetritusThreshold for the whole of phase 1, exactly as the scenario intends.
        for (int fx = -3; fx <= 3; fx++)
        {
            env.DetritusMap[(fx, source.Item2 * 2)] = 1e6;
            env.DetritusMap[(fx, sink.Item2 * 2)] = 1e6;
        }

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 61);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        var network = new Network();
        var lifecycle = new LifecycleController(prm);
        colony.Seed(new[] { (0, 0) });
        layer.SetOcc(0, 0, 1);
        network.SetSources(new[] { source, sink });

        const string scenario = "physarum_starve";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        long s = 0;
        for (; s < 150; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
            lifecycle.Step(layer, colony, network, env, s, dt: 1.0);
            if (s % 8 == 0) WriteLifecycleFrame(scenario, (int)s, layer, colony, lifecycle, half: 16);
        }

        env.DetritusMap.Clear();
        for (; s < 300; s++)
        {
            var org = SoleOrg(colony);
            if (org == null) break;
            if (org.State != PlasmodiumState.Sclerotium)
            {
                network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
                colony.Step(layer, attractant, env, s, dt: 1.0, network);
                colony.Relabel(colony.CellId.Keys.ToList());
            }
            lifecycle.Step(layer, colony, network, env, s, dt: 1.0);
            if (s % 4 == 0) WriteLifecycleFrame(scenario, (int)s, layer, colony, lifecycle, half: 16);

            org = SoleOrg(colony);
            if (org != null && org.State == PlasmodiumState.Fruiting && org.TimeInState >= prm.FruitingDecaySeconds + prm.ResidueFadeSeconds)
                break;
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ physarum_dry_sclerotium

    [Fact]
    public void DryingTriggersSclerotiumThenResumesOnRewet()
    {
        var prm = new PlasmodiumParams { TS = 5.0, WS = 0.15 };
        var env = new TestEnv { MoistureValue = 0.6 };
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 71);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var lifecycle = new LifecycleController(prm);
        var seedCells = new[] { (0, 0), (1, 0), (0, 1) };
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        long s = 0;
        for (; s < 3; s++) lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
        Assert.Equal(PlasmodiumState.Foraging, SoleOrg(colony)!.State);

        var beforeCells = colony.CellId.Keys.OrderBy(c => c).ToList();

        env.MoistureValue = 0.05;
        for (; s < 100 && SoleOrg(colony)!.State != PlasmodiumState.Sclerotium; s++)
            lifecycle.Step(layer, colony, null, env, s, dt: 1.0);

        Assert.Equal(PlasmodiumState.Sclerotium, SoleOrg(colony)!.State);
        foreach (var (gx, gz) in seedCells) Assert.True(HasFlag(layer, gx, gz, CoverageFlags.Sclerotium), $"cell {(gx, gz)} not flagged Sclerotium");

        var duringCells = colony.CellId.Keys.OrderBy(c => c).ToList();
        Assert.Equal(beforeCells, duringCells); // frozen: no growth happened while dry

        env.MoistureValue = 0.6;
        for (; s < 200 && SoleOrg(colony)!.State != PlasmodiumState.Foraging; s++)
            lifecycle.Step(layer, colony, null, env, s, dt: 1.0);

        Assert.Equal(PlasmodiumState.Foraging, SoleOrg(colony)!.State);
        foreach (var (gx, gz) in seedCells) Assert.False(HasFlag(layer, gx, gz, CoverageFlags.Sclerotium), $"cell {(gx, gz)} still flagged Sclerotium");

        var afterCells = colony.CellId.Keys.OrderBy(c => c).ToList();
        Assert.Equal(beforeCells, afterCells); // same network resumed, nothing lost
    }

    [Fact]
    public void DrySclerotiumScenarioIsDeterministic()
    {
        (PlasmodiumState final, List<(int, int)> cells) Run()
        {
            var prm = new PlasmodiumParams { TS = 5.0, WS = 0.15 };
            var env = new TestEnv { MoistureValue = 0.6 };
            var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 71);
            var colony = new PlasmodiumColony(speciesId: 0, prm);
            var lifecycle = new LifecycleController(prm);
            var seedCells = new[] { (0, 0), (1, 0), (0, 1) };
            colony.Seed(seedCells);
            foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

            long s = 0;
            for (; s < 3; s++) lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
            env.MoistureValue = 0.05;
            for (; s < 100 && SoleOrg(colony)!.State != PlasmodiumState.Sclerotium; s++)
                lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
            env.MoistureValue = 0.6;
            for (; s < 200 && SoleOrg(colony)!.State != PlasmodiumState.Foraging; s++)
                lifecycle.Step(layer, colony, null, env, s, dt: 1.0);

            return (SoleOrg(colony)!.State, colony.CellId.Keys.OrderBy(c => c).ToList());
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.final, b.final);
        Assert.Equal(a.cells, b.cells);
    }

    /// <summary>Slow: renders the dry -> sclerotium -> rewet -> resume timelapse.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void DrySclerotiumScenarioWritesTimelapseFrames()
    {
        var prm = new PlasmodiumParams { TS = 5.0, WS = 0.15 };
        var env = new TestEnv { MoistureValue = 0.6 };
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 71);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var lifecycle = new LifecycleController(prm);
        var seedCells = new[] { (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1) };
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        const string scenario = "physarum_dry_sclerotium";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        long s = 0;
        for (; s < 3; s++)
        {
            lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
            WriteLifecycleFrame(scenario, (int)s, layer, colony, lifecycle, half: 6);
        }

        env.MoistureValue = 0.05;
        for (; s < 100; s++)
        {
            lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
            WriteLifecycleFrame(scenario, (int)s, layer, colony, lifecycle, half: 6);
            if (SoleOrg(colony)!.State == PlasmodiumState.Sclerotium && s > 10) break;
        }

        env.MoistureValue = 0.6;
        for (; s < 200; s++)
        {
            lifecycle.Step(layer, colony, null, env, s, dt: 1.0);
            WriteLifecycleFrame(scenario, (int)s, layer, colony, lifecycle, half: 6);
            if (SoleOrg(colony)!.State == PlasmodiumState.Foraging) break;
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ helpers

    private static bool HasFlag(CoverageLayer layer, int gx, int gz, CoverageFlags flag)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!layer.TryGetTile(ti, tj, out var tile) || tile == null) return false;
        int li = CoverageSpec.LocalIndex(gx, gz);
        return (tile.Flags[li] & (byte)flag) != 0;
    }

    private static void WriteLifecycleFrame(string scenario, int frameIndex, CoverageLayer layer, PlasmodiumColony colony,
        LifecycleController lifecycle, int half)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        for (int gz = -half; gz <= half; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                int px = gx + half, py = gz + half;
                int o = (py * size + px) * 3;
                byte occ = layer.GetOcc(gx, gz);
                if (occ == 0) { rgb[o] = 12; rgb[o + 1] = 12; rgb[o + 2] = 18; continue; }

                var (r, g, b) = StateTint(colony.CellId.TryGetValue((gx, gz), out var id) ? colony.Organisms.TryGetValue(id, out var org) ? org.State : PlasmodiumState.Foraging : PlasmodiumState.Foraging);
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }

        foreach (var body in lifecycle.FruitingBodies)
        {
            int gx = body.Pos.cx * 2, gz = body.Pos.cz * 2;
            int px = gx + half, py = gz + half;
            if (px < 0 || py < 0 || px >= size || py >= size) continue;
            int o = (py * size + px) * 3;
            byte v = body.SporeReleased ? (byte)255 : (byte)(120 + body.Maturity * 135);
            rgb[o] = v; rgb[o + 1] = v; rgb[o + 2] = 40; // bright yellow-white dot
        }

        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    private static (byte r, byte g, byte b) StateTint(PlasmodiumState state) => state switch
    {
        PlasmodiumState.Foraging => ((byte)60, (byte)180, (byte)80),
        PlasmodiumState.Sclerotium => ((byte)110, (byte)90, (byte)50),
        PlasmodiumState.Migrating => ((byte)140, (byte)80, (byte)200),
        PlasmodiumState.Fruiting => ((byte)220, (byte)140, (byte)30),
        PlasmodiumState.Dormant => ((byte)80, (byte)80, (byte)80),
        _ => ((byte)60, (byte)180, (byte)80),
    };
}
