using System.Diagnostics;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Plasmodium;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>Growth-lab proofs for the Tero flow-adaptation transport network (docs/overhaul/growth_models.md
/// §6.5, §13 row G7): vein hierarchy from flux adaptation, shortest-path convergence in a maze, and plasmodium
/// fusion driven by real foraging growth (not a manually forced bridge).</summary>
[Trait("Suite", "GrowthLab")]
public class NetworkScenarios
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

    private const double CoarseSize = CoverageSpec.CellSize * 2;

    // ------------------------------------------------------------------ shared: coarse-graph Dijkstra

    private static double Dijkstra((int, int) from, (int, int) to,
        IEnumerable<((int, int) a, (int, int) b, double w)> edges)
    {
        var adj = new Dictionary<(int, int), List<((int, int) to, double w)>>();
        void Add((int, int) a, (int, int) b, double w)
        {
            if (!adj.TryGetValue(a, out var list)) adj[a] = list = new List<((int, int), double)>();
            list.Add((b, w));
        }
        foreach (var (a, b, w) in edges) { Add(a, b, w); Add(b, a, w); }

        var dist = new Dictionary<(int, int), double> { [from] = 0 };
        var visited = new HashSet<(int, int)>();
        while (true)
        {
            (int, int)? best = null; double bestD = double.PositiveInfinity;
            foreach (var (node, d) in dist)
                if (!visited.Contains(node) && d < bestD) { best = node; bestD = d; }
            if (best is null) break;
            visited.Add(best.Value);
            if (best.Value.Equals(to)) return bestD;
            if (!adj.TryGetValue(best.Value, out var nbrs)) continue;
            foreach (var (nb, w) in nbrs)
            {
                double nd = bestD + w;
                if (!dist.TryGetValue(nb, out var cur) || nd < cur) dist[nb] = nd;
            }
        }
        return double.PositiveInfinity;
    }

    // ------------------------------------------------------------------ physarum_two_food

    [Fact]
    public void TwoFoodSheetConvergesToOnePathAndRetracts()
    {
        // Real foraging growth (not a hand-seeded symmetric slab: a perfectly symmetric rectangle has no
        // asymmetry for flux adaptation to break, so it never collapses to one path) spans both food sources,
        // then the network prunes the resulting sheet down to one dominant tube.
        var prm = new PlasmodiumParams { InitialMass = 1800, Beta = 0.02, LambdaF = 6.0, Sigma = 2.0 };
        var env = new TestEnv();
        var source = (0, -8);
        var sink = (0, 8);
        for (int fx = -1; fx <= 1; fx++)
        {
            env.DetritusMap[(fx, source.Item2 * 2)] = 1e6;
            env.DetritusMap[(fx, sink.Item2 * 2)] = 1e6;
        }

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 21);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        // §6R item 1: local mass makes the sheet grow larger before the network's own flux adaptation has had
        // time to prune it (mass no longer bottlenecks growth the way a shared, feed-limited pool used to), so
        // a faster decay/growth-gain ratio is needed to reach the same peak->final retraction ratio in budget.
        var network = new Network { Gamma = 0.4, QGain = 6.0 };
        colony.Seed(new[] { (0, 0) });
        layer.SetOcc(0, 0, 1);

        int peakCount = 0;
        // Pl-2 setup-only adaptation: the contraction phase field's mass-proportional pressure term and
        // interleaved transport sub-stepping change the sheet's growth/pruning cadence slightly; a bit more
        // budget still reaches the same peak->final retraction ratio deterministically.
        for (long s = 0; s < 850; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
            peakCount = Math.Max(peakCount, colony.CellId.Count);
        }

        int finalCount = colony.CellId.Count;
        var edges = network.SurvivingEdges.Select(e => (e.a, e.b, EdgeLen(e.a, e.b))).ToList();
        Assert.True(finalCount < peakCount / 2, $"sheet did not retract: peak {peakCount} -> final {finalCount}");

        Assert.True(edges.Count > 0, "no surviving tube edges");

        double pathLen = Dijkstra(source, sink, edges);
        double euclid = Math.Sqrt(Math.Pow((sink.Item1 - source.Item1) * CoarseSize, 2) +
                                   Math.Pow((sink.Item2 - source.Item2) * CoarseSize, 2));
        Assert.True(pathLen <= euclid * 1.15, $"path {pathLen} exceeds 1.15x euclid {euclid}");
    }

    private static double EdgeLen((int, int) a, (int, int) b)
    {
        double dx = a.Item1 - b.Item1, dz = a.Item2 - b.Item2;
        return Math.Sqrt(dx * dx + dz * dz) * CoarseSize;
    }

    [Fact]
    public void ParallelFineTransfersDoubleCoarseEdgeFlux()
    {
        static double MeanFlux(int crossings)
        {
            var layer = new CoverageLayer(CoverageLayerId.Plasmodium, 7);
            var network = new Network { D0 = 0, Gamma = 0, QGain = 1, PruneThreshold = 0, FlowAvgTau = 1 };
            var occupied = new[] { (1, 0), (1, 1), (2, 0), (2, 1) };
            foreach (var (gx, gz) in occupied) layer.SetOcc(gx, gz, 1);
            network.Step(layer, occupied, 1);
            network.RecordFlow(1, 0, 2, 0, 0.2, 1);
            if (crossings == 2) network.RecordFlow(1, 1, 2, 1, 0.2, 1);
            network.EndFlowSample(1);
            network.Step(layer, occupied, 1);
            double d = network.VeinConductance(1, 0, 2, 0);
            // Invert f(Q)=Q^1.5/(1+Q^1.5) to inspect the flux used by the adaptation step.
            return Math.Pow(d / (1 - d), 1.0 / 1.5);
        }

        Assert.Equal(2 * MeanFlux(1), MeanFlux(2), 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrunedNetworkRetractsWithoutTwoFoodBridge(bool oneFoodCell)
    {
        var prm = new PlasmodiumParams { InitialMass = 20, Beta = 0, LambdaF = 0, FeedRate = 0 };
        var env = new TestEnv();
        if (oneFoodCell) env.DetritusMap[(0, 0)] = 100;
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, 8);
        var colony = new PlasmodiumColony(0, prm);
        var network = new Network();
        var occupied = new[] { (0, 0), (2, 0) };
        colony.Seed(occupied);
        foreach (var (gx, gz) in occupied) layer.SetOcc(gx, gz, 1);
        for (int step = 0; step < 25; step++) network.Step(layer, occupied, 1);
        Assert.True(network.ShouldRetract(2, 0));

        colony.Step(layer, new Attractant(prm), env, 25, 1, network);
        Assert.DoesNotContain((2, 0), colony.CellId.Keys);
        Assert.Equal(oneFoodCell, colony.CellId.ContainsKey((0, 0)));
    }

    /// <summary>Slow: renders the two_food convergence as a timelapse (sheet thickness + tube width).</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void TwoFoodScenarioWritesTimelapseFrames()
    {
        var prm = new PlasmodiumParams { InitialMass = 1800, Beta = 0.02, LambdaF = 6.0, Sigma = 2.0, FeedRate = 0 };
        var env = new TestEnv();
        var source = (0, -8);
        var sink = (0, 8);
        for (int fx = -1; fx <= 1; fx++)
        {
            env.DetritusMap[(fx, source.Item2 * 2)] = 1e6;
            env.DetritusMap[(fx, sink.Item2 * 2)] = 1e6;
        }

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 21);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        var network = new Network();
        colony.Seed(new[] { (0, 0) });
        layer.SetOcc(0, 0, 1);

        const string scenario = "physarum_two_food";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        for (int s = 0; s < 400; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
            if (s % 8 == 0 || s == 399) WriteNetworkFrame(scenario, s, layer, colony, new[] { source, sink }, half: 24, originGx: 0, originGz: 0);
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ physarum_maze

    private static (HashSet<(int, int)> open, (int, int) start, (int, int) end) BuildMaze()
    {
        // Coarse-cell maze: a single-width corridor from S to E with one dead-end spur that must prune away.
        // '.' open, '#' wall (absent from the node set entirely — not colonisable).
        string[] rows =
        {
            "S....#....",
            "####.#.##.",
            "....#.#..#",
            ".####.####",
            ".#........",
            ".#.######.",
            ".#.#......",
            ".#.#.####.",
            "...#....#E",
        };
        var open = new HashSet<(int, int)>();
        (int, int) start = default, end = default;
        for (int row = 0; row < rows.Length; row++)
            for (int col = 0; col < rows[row].Length; col++)
            {
                char c = rows[row][col];
                if (c == '#') continue;
                open.Add((col, row));
                if (c == 'S') start = (col, row);
                if (c == 'E') end = (col, row);
            }
        return (open, start, end);
    }

    [Fact]
    public void MazeSurvivingPathMatchesShortestPath()
    {
        var (open, start, end) = BuildMaze();
        var prm = new PlasmodiumParams { InitialMass = 1000, Beta = 0, LambdaF = 0, MaintenanceRate = 0.02 };
        var env = new TestEnv();
        // A maze covered by a uniformly fed, unstressed sheet has no persistent transport demand. Local
        // feeding at both endpoints and maintenance over the occupied maze supply pressure gradients through
        // the actual cytoplasm transport; the network receives no designated source or sink.
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                env.DetritusMap[(start.Item1 * 2 + dx, start.Item2 * 2 + dz)] = 1e6;
                env.DetritusMap[(end.Item1 * 2 + dx, end.Item2 * 2 + dz)] = 1e6;
            }
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 33);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        var network = new Network();

        // Seed the whole open maze as occupied fine cells (2x2 per coarse cell), matching the classic Physarum
        // maze experiment: the plasmodium starts by covering every accessible corridor, then retracts.
        var seedCells = new List<(int, int)>();
        foreach (var (cx, cz) in open)
            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                    seedCells.Add((cx * 2 + dx, cz * 2 + dz));
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        // Ground truth: shortest path over the open-cell graph itself (8-connected, same adjacency the
        // network graph uses), independent of the simulation.
        var groundEdges = new List<((int, int) a, (int, int) b, double w)>();
        (int, int)[] dirs8 = { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) };
        foreach (var a in open)
            foreach (var (dx, dz) in dirs8)
            {
                var b = (a.Item1 + dx, a.Item2 + dz);
                if (a.CompareTo(b) >= 0) continue;
                if (!open.Contains(b)) continue;
                groundEdges.Add((a, b, EdgeLen(a, b)));
            }
        double groundTruth = Dijkstra(start, end, groundEdges);

        for (long s = 0; s < 300; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
        }

        var edges = network.SurvivingEdges.Select(e => (e.a, e.b, EdgeLen(e.a, e.b))).ToList();
        double surviving = Dijkstra(start, end, edges);
        Assert.True(edges.Count > 0, "network fully pruned away, no surviving path");
        Assert.True(edges.Count < groundEdges.Count, "maze sheet did not prune low-flow branches");
        Assert.True(double.IsFinite(surviving), "surviving network no longer connects S to E");
        Assert.True(surviving <= groundTruth * 1.10,
            $"surviving path {surviving} exceeds ground-truth shortest {groundTruth} by more than 10%");
    }

    /// <summary>Slow: renders the maze retraction as a timelapse.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void MazeScenarioWritesTimelapseFrames()
    {
        var (open, start, end) = BuildMaze();
        var prm = new PlasmodiumParams { Beta = 0, LambdaF = 0 };
        var env = new TestEnv();
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 33);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);
        var network = new Network();

        var seedCells = new List<(int, int)>();
        foreach (var (cx, cz) in open)
            for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                    seedCells.Add((cx * 2 + dx, cz * 2 + dz));
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        const string scenario = "physarum_maze";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        for (int s = 0; s < 150; s++)
        {
            network.Step(layer, colony.CellId.Keys.ToList(), dt: 1.0);
            colony.Step(layer, attractant, env, s, dt: 1.0, network);
            colony.Relabel(colony.CellId.Keys.ToList());
            if (s % 5 == 0 || s == 149) WriteNetworkFrame(scenario, s, layer, colony, new[] { start, end }, half: 12, originGx: 10, originGz: 8);
        }

        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ physarum_fusion

    [Fact]
    public void GrowthDrivenFusionMergesComponentIds()
    {
        var prm = new PlasmodiumParams { InitialMass = 900, Beta = 0.08, LambdaF = 6.0, Sigma = 2.0 };
        var env = new TestEnv();
        // Food between the two blobs pulls both fronts toward each other.
        for (int fz = -2; fz <= 2; fz++) env.DetritusMap[(0, fz)] = 1e5;

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 41);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        colony.Seed(new[] { (-8, -1), (-8, 0), (-8, 1), (-7, 0) });
        colony.Seed(new[] { (7, -1), (7, 0), (7, 1), (6, 0) });
        foreach (var (gx, gz) in colony.CellId.Keys) layer.SetOcc(gx, gz, 1);

        bool sawTwo = false, fused = false;
        for (long s = 0; s < 200 && !fused; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 1.0);
            colony.Relabel(colony.CellId.Keys.ToList());
            int count = colony.Organisms.Count;
            if (count >= 2) sawTwo = true;
            if (sawTwo && count == 1) fused = true;
        }

        Assert.True(sawTwo, "never observed two separate components");
        Assert.True(fused, "components never fused into one id");
    }

    /// <summary>Slow: renders the growth-driven fusion as a timelapse, one hue per id.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void FusionScenarioWritesTimelapseFrames()
    {
        var prm = new PlasmodiumParams { InitialMass = 900, Beta = 0.08, LambdaF = 6.0, Sigma = 2.0 };
        var env = new TestEnv();
        for (int fz = -2; fz <= 2; fz++) env.DetritusMap[(0, fz)] = 1e5;

        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 41);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var attractant = new Attractant(prm);

        colony.Seed(new[] { (-8, -1), (-8, 0), (-8, 1), (-7, 0) });
        colony.Seed(new[] { (7, -1), (7, 0), (7, 1), (6, 0) });
        foreach (var (gx, gz) in colony.CellId.Keys) layer.SetOcc(gx, gz, 1);

        const string scenario = "physarum_fusion";
        string dir = GrowthLabRunner.FramesDir(scenario);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);

        bool fused = false;
        for (int s = 0; s < 200 && !fused; s++)
        {
            colony.Step(layer, attractant, env, s, dt: 1.0);
            colony.Relabel(colony.CellId.Keys.ToList());
            WriteNetworkFrame(scenario, s, layer, colony, Array.Empty<(int, int)>(), half: 20, originGx: 0, originGz: 0);
            if (colony.Organisms.Count == 1) fused = true;
        }

        Assert.True(fused, "components never fused into one id");
        Assert.True(Directory.GetFiles(dir, "frame_*.png").Length > 0);
    }

    // ------------------------------------------------------------------ CG solver: residual + determinism

    [Fact]
    public void CgSolverConvergesOnKnownGraphAndIsDeterministic()
    {
        // Path graph of 5 nodes, unit conductance, node 0 grounded, unit supply at node 4: pressures should
        // decrease linearly along the path (each edge carries the full unit flux).
        var edges = new List<CgSolver.Edge>
        {
            new(0, 1, 1), new(1, 2, 1), new(2, 3, 1), new(3, 4, 1),
        };
        var b = new List<double> { 0, 0, 0, 0, 1 };

        double[] Solve() => CgSolver.Solve(5, edges, b, ground: 0);

        var p1 = Solve();
        var p2 = Solve();
        Assert.Equal(p1, p2); // determinism: identical inputs, identical bytes

        for (int i = 1; i < 5; i++)
            Assert.Equal(i, p1[i], precision: 6); // p_i = i for this unit path graph

        // Residual check: L*p - b should be ~0 everywhere except the grounded node.
        for (int i = 1; i < 4; i++)
        {
            double lhs = (p1[i] - p1[i - 1]) + (p1[i] - p1[i + 1]);
            Assert.True(Math.Abs(lhs - b[i]) < 1e-6, $"residual too large at node {i}: {lhs} vs {b[i]}");
        }
    }

    // ------------------------------------------------------------------ perf

    [Fact, Trait("Speed", "Slow"), Trait("Suite", "Perf")]
    public void NetworkStepIsFastOn600Nodes()
    {
        var prm = new PlasmodiumParams { Beta = 0, LambdaF = 0 };
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed: 55);
        var colony = new PlasmodiumColony(speciesId: 0, prm);
        var network = new Network();

        // ~600 coarse nodes -> a 20x30 fine-cell block covers 10x15 = 150 coarse nodes per 2x scale;
        // use a wider slab to reach ~600.
        var seedCells = new List<(int, int)>();
        for (int gz = 0; gz < 40; gz++)
            for (int gx = 0; gx < 60; gx++)
                seedCells.Add((gx, gz));
        colony.Seed(seedCells);
        foreach (var (gx, gz) in seedCells) layer.SetOcc(gx, gz, 1);

        var cells = colony.CellId.Keys.ToList();
        const int warmup = 5, timed = 20;
        for (int i = 0; i < warmup; i++) network.Step(layer, cells, dt: 1.0);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < timed; i++) network.Step(layer, cells, dt: 1.0);
        sw.Stop();

        double msPerStep = sw.Elapsed.TotalMilliseconds / timed;
        Console.WriteLine($"network step (~600 nodes): {msPerStep:F3} ms");
        Assert.True(msPerStep <= 3.0, $"network step too slow: {msPerStep:F3} ms");
    }

    // ------------------------------------------------------------------ frame rendering

    private static void WriteNetworkFrame(string scenario, int frameIndex, CoverageLayer layer, PlasmodiumColony colony,
        (int, int)[] foodCoarseCells, int half, int originGx, int originGz)
    {
        int size = half * 2 + 1;
        var rgb = new byte[size * size * 3];
        for (int gz = -half; gz <= half; gz++)
            for (int gx = -half; gx <= half; gx++)
            {
                int wgx = gx + originGx, wgz = gz + originGz;
                int px = gx + half, py = gz + half;
                int o = (py * size + px) * 3;
                byte occ = layer.GetOcc(wgx, wgz);
                if (occ == 0) { rgb[o] = 12; rgb[o + 1] = 12; rgb[o + 2] = 18; continue; }

                int id = colony.CellId.TryGetValue((wgx, wgz), out var oid) ? oid : 0;
                var (r, g, b) = HsvToRgb((id * 0.6180339887498949) % 1.0, 0.5, 0.55);

                byte w = GetTubeWidth(layer, wgx, wgz);
                double boost = w / 255.0;
                rgb[o] = (byte)Math.Clamp(r + boost * (255 - r), 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g + boost * (255 - g) * 0.9, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b + boost * (255 - b) * 0.6, 0, 255);
            }

        foreach (var (fcx, fcz) in foodCoarseCells)
        {
            int px = fcx * 2 - originGx + half, py = fcz * 2 - originGz + half;
            if (px < 0 || py < 0 || px >= size || py >= size) continue;
            int o = (py * size + px) * 3;
            rgb[o] = 255; rgb[o + 1] = 30; rgb[o + 2] = 30; // food marker: red
        }

        string path = Path.Combine(GrowthLabRunner.FramesDir(scenario), $"frame_{frameIndex:D3}.png");
        PngEncoder.WriteRgb(path, size, size, rgb);
    }

    private static byte GetTubeWidth(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!layer.TryGetTile(ti, tj, out var tile) || tile == null) return 0;
        return tile.W[CoverageSpec.LocalIndex(gx, gz)];
    }

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        double r = 0, g = 0, b = 0;
        int i = (int)(h * 6);
        double f = h * 6 - i;
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            case 5: r = v; g = p; b = q; break;
        }
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
}
