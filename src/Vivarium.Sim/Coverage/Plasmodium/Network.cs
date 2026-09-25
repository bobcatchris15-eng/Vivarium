namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// Tero flow-adaptation transport network over the plasmodium's occupied area (docs/overhaul/growth_models.md
/// §6.5). Nodes sit on the 4 cm coarse lattice (same coarsening as <see cref="Attractant"/>); an edge exists
/// between two occupied, 8-adjacent coarse nodes. Edge conductance D persists across steps (keyed by the
/// coarse coordinate pair, not by array index, so it survives the graph being rebuilt every step) and adapts
/// by dD/dt = q*f(|Q|) - gamma*D, integrated with implicit Euler for unconditional stability at any dt. The
/// two richest active food sources act as a point-to-point source/sink pair; flux converges onto the shortest
/// low-resistance path and everything else decays and prunes, which is what produces vein hierarchy instead of
/// a uniform-width web. Off-network sheet cells (nodes whose best adjacent conductance has decayed below the
/// retraction threshold, once the network is actually carrying flow) are flagged for <see cref="Foraging"/> to
/// vacate.
/// </summary>
public sealed class Network : IPlasmodiumNetwork
{
    /// <summary>Initial conductance for a freshly-appeared edge.</summary>
    public double D0 { get; init; } = 1.0;

    /// <summary>Decay rate gamma, 1/s.</summary>
    public double Gamma { get; init; } = 0.2;

    /// <summary>Growth gain q in dD/dt = q*f(|Q|) - gamma*D; f(x) = x (linear Tero variant).</summary>
    public double QGain { get; init; } = 3.0;

    /// <summary>Total supply/demand magnitude injected at the source / withdrawn at the sink.</summary>
    public double Q0 { get; init; } = 1.0;

    /// <summary>Edges below this conductance are treated as pruned (no flux, no splat, retraction-eligible).</summary>
    public double PruneThreshold { get; init; } = 0.05;

    /// <summary>Tube width splat scale: byte width = clamp(W0 * sqrt(D), 0, 255).</summary>
    public double W0 { get; init; } = 40.0;

    private readonly Dictionary<((int, int) a, (int, int) b), double> _edgeD = new();
    private readonly HashSet<(int, int)> _retractCoarse = new();
    private readonly Dictionary<(int, int), double> _nodeMaxD = new();
    private readonly HashSet<(int, int)> _sources = new();

    // Reused across steps to avoid per-step allocation churn (this runs every foraging step, not just in tests).
    private readonly HashSet<(int, int)> _coarseOccupied = new();
    private readonly List<(int, int)> _nodesBuf = new();
    private readonly Dictionary<(int, int), int> _indexBuf = new();
    private readonly List<((int, int) a, (int, int) b)> _edgeCoordsBuf = new();
    private readonly List<CgSolver.Edge> _edgesBuf = new();
    private readonly List<double> _dOldBuf = new();
    private readonly List<(int, int)> _activeSourcesBuf = new();
    private double[] _nodeMaxDArr = Array.Empty<double>();
    private readonly List<double> _supplyBuf = new();

    private static readonly (int dx, int dz, double dist)[] Dirs8 =
    {
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.4142135623730951), (1, -1, 1.4142135623730951), (-1, 1, 1.4142135623730951), (-1, -1, 1.4142135623730951),
    };

    /// <summary>Registers which coarse cells currently count as food sources. Deterministic order: the two
    /// smallest coordinates (by tuple comparison) become the point-to-point source/sink pair.</summary>
    public void SetSources(IEnumerable<(int cx, int cz)> sources)
    {
        _sources.Clear();
        foreach (var s in sources) _sources.Add(s);
    }

    public bool ShouldRetract(int gx, int gz) => _retractCoarse.Contains(Attractant.CoarseOf(gx, gz));

    /// <summary>Read-only view of the last solve's per-node max adjacent conductance, for frame rendering.</summary>
    public IReadOnlyDictionary<(int, int), double> NodeMaxD => _nodeMaxD;

    /// <summary>Surviving (non-pruned) edges as (coarse a, coarse b, conductance), for shortest-path checks.</summary>
    public IEnumerable<((int, int) a, (int, int) b, double d)> SurvivingEdges =>
        _edgeD.Where(kv => kv.Value >= PruneThreshold).Select(kv => (kv.Key.a, kv.Key.b, kv.Value));

    private static ((int, int) a, (int, int) b) Key((int, int) x, (int, int) y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);

    /// <summary>
    /// Advances the network by one step: rebuilds the graph from the colony's current occupancy, solves for
    /// pressures, updates conductance, splats tube width into <paramref name="layer"/>'s <c>W</c> field, and
    /// recomputes the retraction set. Cheap no-op (decay only, no retraction) when fewer than two active
    /// sources are occupied — a lone or foodless sheet has no flow context to judge vein vs. sheet by.
    /// </summary>
    public void Step(CoverageLayer layer, IEnumerable<(int gx, int gz)> occupiedFineCells, double dt)
    {
        _coarseOccupied.Clear();
        foreach (var (gx, gz) in occupiedFineCells) _coarseOccupied.Add(Attractant.CoarseOf(gx, gz));

        _nodesBuf.Clear();
        _nodesBuf.AddRange(_coarseOccupied);
        _nodesBuf.Sort();
        var nodes = _nodesBuf;

        _indexBuf.Clear();
        for (int i = 0; i < nodes.Count; i++) _indexBuf[nodes[i]] = i;
        var index = _indexBuf;

        _activeSourcesBuf.Clear();
        foreach (var n in nodes) if (_sources.Contains(n)) _activeSourcesBuf.Add(n);
        var activeSources = _activeSourcesBuf; // already ascending: nodes is sorted

        // Build edges (fixed order: node index ascending, then direction index) with persisted D. Edges are
        // only ever added with a < b (canonical), so (a, b) itself is already the dictionary key — no need to
        // re-derive it, here or in the update loop below.
        _edgeCoordsBuf.Clear();
        _edgesBuf.Clear();
        _dOldBuf.Clear();
        var edgeCoords = _edgeCoordsBuf;
        var edges = _edgesBuf;
        var dOldBuf = _dOldBuf;
        const double coarseCellSize = CoverageSpec.CellSize * 2;
        foreach (var a in nodes)
        {
            foreach (var (dx, dz, dist) in Dirs8)
            {
                var b = (a.Item1 + dx, a.Item2 + dz);
                if (a.CompareTo(b) >= 0) continue; // each undirected edge once, canonical a < b
                if (!index.TryGetValue(b, out int idxB)) continue; // also confirms b is occupied
                double d = _edgeD.TryGetValue((a, b), out var existing) ? existing : D0;
                edgeCoords.Add((a, b));
                dOldBuf.Add(d);
                double length = coarseCellSize * dist;
                edges.Add(new CgSolver.Edge(index[a], idxB, d / length));
            }
        }

        if (_nodeMaxDArr.Length < nodes.Count) _nodeMaxDArr = new double[nodes.Count];
        Array.Clear(_nodeMaxDArr, 0, nodes.Count);
        _retractCoarse.Clear();

        if (activeSources.Count < 2 || nodes.Count == 0)
        {
            // No flow context: let conductance relax toward zero (pure decay), but do not judge retraction.
            foreach (var key in _edgeD.Keys.ToList())
            {
                double dNew = _edgeD[key] / (1 + dt * Gamma);
                _edgeD[key] = dNew < PruneThreshold ? 0 : dNew;
            }
            return;
        }

        int ground = index[activeSources[0]];
        int sink = index[activeSources[1]];
        _supplyBuf.Clear();
        for (int i = 0; i < nodes.Count; i++) _supplyBuf.Add(0);
        _supplyBuf[ground] = Q0;
        _supplyBuf[sink] = -Q0;

        var p = CgSolver.Solve(nodes.Count, edges, _supplyBuf, ground);

        for (int i = 0; i < edges.Count; i++)
        {
            var e = edges[i];
            double flux = e.W * (p[e.A] - p[e.B]);
            double dOld = dOldBuf[i];
            double dNew = (dOld + dt * QGain * Math.Abs(flux)) / (1 + dt * Gamma);
            if (dNew < PruneThreshold) dNew = 0;
            _edgeD[edgeCoords[i]] = dNew;

            if (dNew > _nodeMaxDArr[e.A]) _nodeMaxDArr[e.A] = dNew;
            if (dNew > _nodeMaxDArr[e.B]) _nodeMaxDArr[e.B] = dNew;
        }

        // Tube width splat + retraction: every occupied coarse node with no edge above the prune threshold is
        // sheet, not vein — flagged for Foraging to vacate unless it is itself an active source.
        _nodeMaxD.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            double maxD = _nodeMaxDArr[i];
            _nodeMaxD[node] = maxD;
            byte width = (byte)Math.Clamp(W0 * Math.Sqrt(Math.Max(0, maxD)), 0, 255);
            SplatWidth(layer, node, width);

            if (maxD < PruneThreshold && !_sources.Contains(node)) _retractCoarse.Add(node);
        }
    }

    private static void SplatWidth(CoverageLayer layer, (int cx, int cz) node, byte width)
    {
        int gx0 = node.cx * 2, gz0 = node.cz * 2;
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
            {
                int gx = gx0 + dx, gz = gz0 + dz;
                var (ti, tj) = CoverageSpec.TileOf(gx, gz);
                var tile = layer.GetOrCreateTile(ti, tj);
                int li = CoverageSpec.LocalIndex(gx, gz);
                if (tile.Occ[li] == 0) continue; // don't splat width onto ground that isn't sheet
                if (tile.W[li] == width) continue;
                tile.W[li] = width;
                tile.Touch();
            }
    }
}
