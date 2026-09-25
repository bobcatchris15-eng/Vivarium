namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// Tero-style vein-conductance adaptation over the plasmodium's occupied area (docs/overhaul/growth_models.md
/// §6R item 7). Nodes sit on the 4 cm coarse lattice (same coarsening as <see cref="Attractant"/>); an edge
/// exists between two occupied, 8-adjacent coarse nodes. Edge conductance D persists across steps (keyed by the
/// coarse coordinate pair, not by array index, so it survives the graph being rebuilt every step) and adapts by
/// dD/dt = r*<|Q|> - gamma*D, integrated with implicit Euler for unconditional stability at any dt, where <|Q|>
/// is the running average of the *real* shuttle-streaming flow the phase field + <see cref="Foraging.Transport"/>
/// actually pushed through this edge — reported via <see cref="RecordFlow"/>, one contraction cycle at a time.
/// No designated sources or sinks exist anywhere: whatever path actually carries flow thickens, and everything
/// else decays and prunes, which is what produces vein hierarchy (or fails to, if the physiology doesn't support
/// it) instead of it being solved for. Off-network sheet cells (nodes whose best adjacent conductance has decayed
/// below the retraction threshold) are flagged for <see cref="Foraging"/> to vacate.
/// </summary>
public sealed class Network : IPlasmodiumNetwork
{
    /// <summary>Initial conductance for a freshly-appeared edge.</summary>
    public double D0 { get; init; } = 1.0;

    /// <summary>Decay rate gamma, 1/s.</summary>
    public double Gamma { get; init; } = 0.2;

    /// <summary>Growth gain r in dD/dt = r*<|Q|> - gamma*D (linear Tero variant).</summary>
    public double QGain { get; init; } = 3.0;

    /// <summary>Edges below this conductance are treated as pruned (no flux, no splat, retraction-eligible).</summary>
    public double PruneThreshold { get; init; } = 0.05;

    /// <summary>Time constant, seconds, for the exponential running average of |Q| that drives conductance
    /// growth (§6R Cadence: veins adapt on the cycle-averaged flow, not the instantaneous per-step value, so a
    /// vein does not decay every half-cycle just because the shuttle-streaming flow reversed sign).</summary>
    public double FlowAvgTau { get; init; } = 5.0;

    /// <summary>Tube width splat scale: byte width = clamp(W0 * sqrt(D), 0, 255).</summary>
    public double W0 { get; init; } = 40.0;

    private readonly Dictionary<((int, int) a, (int, int) b), double> _edgeD = new();
    private readonly Dictionary<((int, int) a, (int, int) b), double> _edgeAvgQ = new();
    private readonly HashSet<(int, int)> _retractCoarse = new();
    private readonly Dictionary<(int, int), double> _nodeMaxD = new();

    /// <summary>Real flow reported by <see cref="RecordFlow"/> since the last <see cref="Step"/> call, accumulated
    /// per coarse edge as Σ|Q|·dt (weighted sum) and Σdt (weight), so <see cref="Step"/> can recover the
    /// dt-weighted mean flow over whatever fraction of the cycle actually reported anything. Cleared at the end
    /// of every <see cref="Step"/> — each cycle's average is consumed exactly once.</summary>
    private readonly Dictionary<((int, int) a, (int, int) b), double> _pendingFlowWeighted = new();
    private readonly Dictionary<((int, int) a, (int, int) b), double> _pendingFlowDt = new();

    // Reused across steps to avoid per-step allocation churn (this runs every foraging step, not just in tests).
    private readonly HashSet<(int, int)> _coarseOccupied = new();
    private readonly List<(int, int)> _nodesBuf = new();
    private readonly Dictionary<(int, int), int> _indexBuf = new();
    private readonly List<((int, int) a, (int, int) b)> _edgeCoordsBuf = new();
    private double[] _nodeMaxDArr = Array.Empty<double>();
    private bool[] _nodeTouchedArr = Array.Empty<bool>();

    private static readonly (int dx, int dz, double dist)[] Dirs8 =
    {
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.4142135623730951), (1, -1, 1.4142135623730951), (-1, 1, 1.4142135623730951), (-1, -1, 1.4142135623730951),
    };

    public bool ShouldRetract(int gx, int gz) => _retractCoarse.Contains(Attractant.CoarseOf(gx, gz));

    /// <summary>§6R item 7: records the real flow <see cref="Foraging.Transport"/> just pushed along one fine-cell
    /// edge, attributed to whichever coarse-node edge it crosses (a no-op if the pair shares a coarse node — there
    /// is nothing for the vein network to attribute a purely-intra-node flow to). Accumulated as a dt-weighted sum
    /// so several small sub-steps within one cycle average correctly; consumed and cleared by the next
    /// <see cref="Step"/> call.</summary>
    public void RecordFlow(int gx, int gz, int nx, int nz, double flow, double dt)
    {
        if (dt <= 0) return;
        var a = Attractant.CoarseOf(gx, gz);
        var b = Attractant.CoarseOf(nx, nz);
        if (a == b) return;
        var key = Key(a, b);
        _pendingFlowWeighted[key] = _pendingFlowWeighted.GetValueOrDefault(key) + Math.Abs(flow) * dt;
        _pendingFlowDt[key] = _pendingFlowDt.GetValueOrDefault(key) + dt;
    }

    /// <summary>Surviving edge conductance between the coarse nodes containing two fine cells (§6R item 1's
    /// "existing network conductance D for vein edges"); 0 if they share a coarse node or no surviving edge
    /// connects their (necessarily 8-adjacent) coarse nodes.</summary>
    public double VeinConductance(int gx, int gz, int nx, int nz)
    {
        var a = Attractant.CoarseOf(gx, gz);
        var b = Attractant.CoarseOf(nx, nz);
        if (a == b) return 0;
        var key = Key(a, b);
        return _edgeD.TryGetValue(key, out var d) && d >= PruneThreshold ? d : 0;
    }

    /// <summary>Read-only view of the last solve's per-node max adjacent conductance, for frame rendering.</summary>
    public IReadOnlyDictionary<(int, int), double> NodeMaxD => _nodeMaxD;

    /// <summary>Surviving (non-pruned) edges as (coarse a, coarse b, conductance), for shortest-path checks.</summary>
    public IEnumerable<((int, int) a, (int, int) b, double d)> SurvivingEdges =>
        _edgeD.Where(kv => kv.Value >= PruneThreshold).Select(kv => (kv.Key.a, kv.Key.b, kv.Value));

    /// <summary>Returns true if a connected path of surviving vein edges connects coarse node a to b.</summary>
    public bool AreNodesConnected((int cx, int cz) a, (int cx, int cz) b)
    {
        if (a == b) return true;
        var visited = new HashSet<(int, int)> { a };
        var queue = new Queue<(int, int)>();
        queue.Enqueue(a);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (dx, dz, _) in Dirs8)
            {
                var n = (cur.Item1 + dx, cur.Item2 + dz);
                if (visited.Contains(n)) continue;
                var key = Key(cur, n);
                if (_edgeD.TryGetValue(key, out var d) && d >= PruneThreshold)
                {
                    if (n == b) return true;
                    visited.Add(n);
                    queue.Enqueue(n);
                }
            }
        }
        return false;
    }

    private static ((int, int) a, (int, int) b) Key((int, int) x, (int, int) y) =>
        x.CompareTo(y) <= 0 ? (x, y) : (y, x);

    /// <summary>
    /// Advances the network by one step: rebuilds the graph from the colony's current occupancy, grows/decays
    /// each edge's conductance from the real flow <see cref="RecordFlow"/> accumulated since the previous call
    /// (§6R item 7 — no solve, no designated sources/sinks), splats tube width into <paramref name="layer"/>'s
    /// <c>W</c> field, and recomputes the retraction set.
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

        // Build edges (fixed order: node index ascending, then direction index) with persisted D. Edges are
        // only ever added with a < b (canonical), so (a, b) itself is already the dictionary key.
        _edgeCoordsBuf.Clear();
        var edgeCoords = _edgeCoordsBuf;
        foreach (var a in nodes)
        {
            foreach (var (dx, dz, _) in Dirs8)
            {
                var b = (a.Item1 + dx, a.Item2 + dz);
                if (a.CompareTo(b) >= 0) continue; // each undirected edge once, canonical a < b
                if (!index.ContainsKey(b)) continue; // also confirms b is occupied
                edgeCoords.Add((a, b));
            }
        }

        if (_nodeMaxDArr.Length < nodes.Count) _nodeMaxDArr = new double[nodes.Count];
        Array.Clear(_nodeMaxDArr, 0, nodes.Count);
        if (_nodeTouchedArr.Length < nodes.Count) _nodeTouchedArr = new bool[nodes.Count];
        Array.Clear(_nodeTouchedArr, 0, nodes.Count);
        _retractCoarse.Clear();

        for (int i = 0; i < edgeCoords.Count; i++)
        {
            var edgeKey = edgeCoords[i];
            double dOld = _edgeD.TryGetValue(edgeKey, out var existing) ? existing : D0;

            // dt-weighted mean of whatever real flow RecordFlow reported for this edge this cycle (0 if none
            // reported — e.g. a brand-new coarse-adjacency with no transport across it yet).
            double meanFlow = _pendingFlowDt.TryGetValue(edgeKey, out var wDt) && wDt > 0
                ? _pendingFlowWeighted[edgeKey] / wDt
                : 0;

            // Cycle-averaged |Q| (exponential running average, time constant FlowAvgTau) drives growth, not the
            // instantaneous flow, so a vein does not thin every time shuttle streaming reverses sign.
            double avgOld = _edgeAvgQ.TryGetValue(edgeKey, out var av) ? av : meanFlow;
            double avgNew = avgOld + (dt / FlowAvgTau) * (meanFlow - avgOld);
            _edgeAvgQ[edgeKey] = avgNew;

            // Superlinear reinforcement separates sustained trunk flow from low-flow sheet edges. The
            // prior 1.8 response removed even the direct occupied route between the food patches; a
            // gentler response keeps that route while low-flow branches still decay below D_min.
            double qPow = Math.Pow(avgNew, 1.5);
            double fQ = qPow / (1.0 + qPow);
            double dNew = (dOld + dt * QGain * fQ) / (1 + dt * Gamma);
            if (dNew < PruneThreshold) dNew = 0;
            _edgeD[edgeKey] = dNew;


            int ia = index[edgeKey.a], ib = index[edgeKey.b];
            if (dNew > _nodeMaxDArr[ia]) _nodeMaxDArr[ia] = dNew;
            if (dNew > _nodeMaxDArr[ib]) _nodeMaxDArr[ib] = dNew;
            _nodeTouchedArr[ia] = true;
            _nodeTouchedArr[ib] = true;
        }


        // Any previously-tracked edge that fell out of the current occupied graph (a cell retracted/vacated)
        // still needs to relax toward zero rather than being frozen at its last value forever.
        foreach (var key in _edgeD.Keys.ToList())
        {
            if (index.ContainsKey(key.a) && index.ContainsKey(key.b)) continue; // handled above
            double dNew = _edgeD[key] / (1 + dt * Gamma);
            _edgeD[key] = dNew < PruneThreshold ? 0 : dNew;
        }

        _pendingFlowWeighted.Clear();
        _pendingFlowDt.Clear();

        // Tube width splat + retraction: every occupied coarse node with no edge above the prune threshold is
        // sheet, not vein — flagged for Foraging to vacate once veins have formed.
        _nodeMaxD.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            double maxD = _nodeMaxDArr[i];
            _nodeMaxD[node] = maxD;
            byte width = (byte)Math.Clamp(W0 * Math.Sqrt(Math.Max(0, maxD)), 0, 255);
            SplatWidth(layer, node, width);

            if (_nodeTouchedArr[i] && maxD < PruneThreshold) _retractCoarse.Add(node);
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
