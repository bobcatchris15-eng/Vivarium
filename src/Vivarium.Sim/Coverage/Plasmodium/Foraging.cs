using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>The only view the foraging front needs of the world around a fine (2 cm) Plasmodium cell
/// (docs/overhaul/growth_models.md §6.4). A real adapter over <c>Fields.Detritus</c>/moisture is G9's job;
/// the growth lab supplies a synthetic implementation.</summary>
public interface IPlasmodiumEnvironment
{
    /// <summary>Wetness W in [0,1], feeds the g_W gate on front extension.</summary>
    double Moisture(int gx, int gz);

    /// <summary>Detritus amount at this cell; the attractant field's source term F.</summary>
    double Detritus(int gx, int gz);

    /// <summary>Removes up to <paramref name="amount"/> of detritus at this cell and returns what was actually taken.</summary>
    double TakeDetritus(int gx, int gz, double amount);
}

/// <summary>
/// Hook for the G7 transport network: per-cell tube width (splatted into <see cref="CoverageTile.W"/> by the
/// network step, not by Foraging) and which sheet cells it wants retracted. Null until G7 lands; Foraging
/// runs unmodified without it (no retraction, front extension unthrottled by tube state).
/// </summary>
public interface IPlasmodiumNetwork
{
    /// <summary>True if the network considers this cell thinned out enough to vacate (§6.5 retraction).</summary>
    bool ShouldRetract(int gx, int gz);

    /// <summary>Conductance to use for mass transport (§6R item 1) between two fine cells, when a surviving vein
    /// edge connects the coarse nodes they fall in. Zero when the two cells share a coarse node (sheet-only, no
    /// separate vein contribution) or no surviving edge exists between their coarse nodes.</summary>
    double VeinConductance(int gx, int gz, int nx, int nz);
}

/// <summary>Deterministic 8-connected component labelling over a set of occupied cells (union-find).
/// Used to recompute plasmodium identity every step (§6.1): touching components fuse, a severed piece splits.</summary>
public static class ComponentLabeler
{
    private static readonly (int dx, int dz)[] Neighbours8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>Returns components as cell lists, each sorted, components ordered by their smallest cell — a
    /// stable, allocation-order-independent result for the same input set.</summary>
    public static List<List<(int gx, int gz)>> Label(IEnumerable<(int gx, int gz)> cells)
    {
        var set = new HashSet<(int, int)>(cells);
        var parent = new Dictionary<(int, int), (int, int)>();
        foreach (var c in set) parent[c] = c;

        (int, int) Find((int, int) x)
        {
            while (!parent[x].Equals(x))
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union((int, int) a, (int, int) b)
        {
            var ra = Find(a); var rb = Find(b);
            if (!ra.Equals(rb)) parent[ra] = rb;
        }

        foreach (var (gx, gz) in set)
            foreach (var (dx, dz) in Neighbours8)
            {
                var n = (gx + dx, gz + dz);
                if (set.Contains(n)) Union((gx, gz), n);
            }

        var groups = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var c in set)
        {
            var r = Find(c);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<(int, int)>();
            list.Add(c);
        }

        var result = groups.Values.ToList();
        foreach (var g in result) g.Sort();
        result.Sort((a, b) => a[0].CompareTo(b[0]));
        return result;
    }
}

/// <summary>
/// Owns every plasmodium of one species: the cell -> id map, the id -> <see cref="Plasmodium"/> table, and the
/// front-extension/feeding step. Recomputes connected components every step from scratch (§6.1) and reconciles
/// them against the previous assignment: a component touching two old ids fuses (keeps the smaller id, merges
/// mass); an old id whose cells now form more than one component splits (the largest keeps the id, the rest get
/// fresh ids with mass split proportionally to cell count, so total mass is exact either way).
/// </summary>
public sealed class PlasmodiumColony
{
    public int SpeciesId { get; }
    public PlasmodiumParams Params { get; }

    public IReadOnlyDictionary<(int gx, int gz), int> CellId => _cellId;
    public IReadOnlyDictionary<int, Plasmodium> Organisms => _organisms;

    /// <summary>Per-cell cytoplasm mass (§6R item 1). No global pool: every mass change is either a local
    /// feeding gain, a local maintenance/withdrawal loss (both tallied into <see cref="RemovedMass"/>/
    /// <see cref="FedMass"/>), or a conservative transfer between two cells that leaves the sum unchanged.</summary>
    public IReadOnlyDictionary<(int gx, int gz), double> Mass => _mass;

    /// <summary>Running tally of mass permanently removed (maintenance cost, network retraction, m_min
    /// withdrawal) since this colony was created. Kept so callers can assert exact conservation:
    /// <c>TotalMass() == initialTotal + FedMass - RemovedMass</c>.</summary>
    public double RemovedMass { get; private set; }

    /// <summary>Running tally of mass added by feeding since this colony was created.</summary>
    public double FedMass { get; private set; }

    private readonly Dictionary<(int, int), int> _cellId = new();
    private readonly Dictionary<int, Plasmodium> _organisms = new();
    private readonly Dictionary<(int, int), double> _mass = new();
    private int _nextId = 1;

    public PlasmodiumColony(int speciesId, PlasmodiumParams prm)
    {
        SpeciesId = speciesId;
        Params = prm;
    }

    /// <summary>Seeds a brand-new plasmodium at the given cells with a fresh id, splitting the configured
    /// initial mass evenly across the seeded cells (§6R item 1: mass lives on cells, not a per-organism pool).</summary>
    public int Seed(IEnumerable<(int gx, int gz)> cells)
    {
        int id = _nextId++;
        _organisms[id] = new Plasmodium(id, SpeciesId);
        var list = cells.ToList();
        double perCell = list.Count > 0 ? Params.InitialMass / list.Count : 0;
        foreach (var c in list)
        {
            _cellId[c] = id;
            _mass[c] = (_mass.TryGetValue(c, out var m) ? m : 0) + perCell;
        }
        return id;
    }

    /// <summary>Sum of every occupied cell's mass — a diagnostic total, not an authoritative pool (§6R item 1).</summary>
    public double TotalMass()
    {
        double m = 0;
        foreach (var v in _mass.Values) m += v;
        return m;
    }

    /// <summary>Mass at a cell, or 0 if unoccupied/untracked.</summary>
    public double MassAt(int gx, int gz) => _mass.TryGetValue((gx, gz), out var m) ? m : 0;

    /// <summary>Zeroes the mass of every given cell and returns the total taken (tallied into
    /// <see cref="RemovedMass"/>): used by <see cref="LifecycleController"/> to convert a Fruiting organism's
    /// remaining sheet mass into fruiting bodies (§6.6) without a per-organism pool to draw from directly.</summary>
    public double ConsumeMassForFruiting(IEnumerable<(int gx, int gz)> cells)
    {
        double total = 0;
        foreach (var c in cells)
        {
            double m = MassAt(c.gx, c.gz);
            if (m <= 0) continue;
            total += m;
            _mass[c] = 0;
        }
        RemovedMass += total;
        return total;
    }

    /// <summary>Recomputes components over the current occupied set and reconciles ids (fusion/split).
    /// Call once per step, after occupancy has changed for the step, before <see cref="Step"/>.</summary>
    public void Relabel(IEnumerable<(int gx, int gz)> occupiedCells)
    {
        var components = ComponentLabeler.Label(occupiedCells);

        // Pass 1: per component, which old ids does it overlap and how many cells of each (by cell count)?
        var overlaps = new List<Dictionary<int, int>>(components.Count);
        foreach (var comp in components)
        {
            var overlap = new Dictionary<int, int>();
            foreach (var cell in comp)
                if (_cellId.TryGetValue(cell, out int oldId))
                    overlap[oldId] = overlap.GetValueOrDefault(oldId) + 1;
            overlaps.Add(overlap);
        }

        var assignedId = new int?[components.Count]; // resolved id per component index, filled below

        // Pass 2: fusion — any component whose own cells carry more than one distinct old id merges those
        // organisms into the smallest id right away; this consumes those old ids entirely (§6.1). Mass itself
        // needs no bookkeeping here (§6R item 1): it is keyed by cell coordinate, not by id, so it rides along
        // with each cell across the relabel automatically.
        var consumedOldIds = new HashSet<int>();
        for (int i = 0; i < components.Count; i++)
        {
            if (overlaps[i].Count <= 1) continue;
            int keepId = overlaps[i].Keys.Min();
            foreach (var oldId in overlaps[i].Keys) consumedOldIds.Add(oldId);
            assignedId[i] = keepId;
        }

        // Pass 3: split — group the remaining (single-old-id, unconsumed) components by that old id. One old
        // id mapping to several components is a split: the largest component keeps the id, the mass is shared
        // proportionally by cell count (so the total stays exact), and the rest get fresh ids.
        var bySingleOldId = new Dictionary<int, List<int>>();
        for (int i = 0; i < components.Count; i++)
        {
            if (assignedId[i].HasValue) continue;
            if (overlaps[i].Count != 1) continue; // no old id at all: handled in pass 4
            int oldId = overlaps[i].Keys.First();
            if (consumedOldIds.Contains(oldId)) continue; // already folded into a fusion elsewhere
            if (!bySingleOldId.TryGetValue(oldId, out var list)) bySingleOldId[oldId] = list = new List<int>();
            list.Add(i);
        }

        foreach (var (oldId, compIndices) in bySingleOldId)
        {
            // Deterministic winner: most cells, ties broken by the smallest first cell.
            int winner = compIndices
                .OrderByDescending(i => components[i].Count)
                .ThenBy(i => components[i][0])
                .First();

            foreach (int i in compIndices)
                assignedId[i] = i == winner ? oldId : _nextId++;
        }

        // Pass 4: anything left is a brand-new component with no old-id overlap at all.
        for (int i = 0; i < components.Count; i++)
        {
            if (assignedId[i].HasValue) continue;
            assignedId[i] = _nextId++;
        }

        var newCellId = new Dictionary<(int, int), int>();
        var newOrganisms = new Dictionary<int, Plasmodium>();
        for (int i = 0; i < components.Count; i++)
        {
            int id = assignedId[i]!.Value;
            var existing = _organisms.TryGetValue(id, out var e) ? e : null;
            var plasmodium = new Plasmodium(id, SpeciesId, existing?.State ?? PlasmodiumState.Foraging)
            {
                TimeInState = existing?.TimeInState ?? 0,
                TimeDry = existing?.TimeDry ?? 0,
                TimeStarving = existing?.TimeStarving ?? 0,
            };
            newOrganisms[id] = plasmodium;
            foreach (var cell in components[i]) newCellId[cell] = id;
        }

        _cellId.Clear();
        foreach (var (k, v) in newCellId) _cellId[k] = v;
        _organisms.Clear();
        foreach (var (k, v) in newOrganisms) _organisms[k] = v;
    }

    // 8-neighbours with their Euclidean distance and a stable index (used as part of the Hash01 purpose so
    // each (source cell, direction) pair draws its own random number — §6.4 asked for exactly this, and it
    // also breaks the "all fronts pick the same corner" correlation that a target-keyed draw produced).
    private static readonly (int dx, int dz, double dist)[] Dirs8 =
    {
        (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
        (1, 1, 1.4142135623730951), (1, -1, 1.4142135623730951), (-1, 1, 1.4142135623730951), (-1, -1, 1.4142135623730951),
    };

    // Reused across steps to avoid per-step list allocations; cleared, not reallocated.
    private readonly List<(int gx, int gz)> _frontBuf = new();
    private readonly List<((int gx, int gz) cell, int ownerId, (int gx, int gz) parent)> _toColoniseBuf = new();
    private readonly List<(int gx, int gz)> _retractBuf = new();
    private readonly List<(int gx, int gz)> _cellsSortedBuf = new();
    private readonly Dictionary<(int, int), double> _outFlowBuf = new();
    private readonly List<((int gx, int gz) from, (int gx, int gz) to, double amount)> _edgeFlowBuf = new();

    /// <summary>
    /// One foraging step (§6.4, revised by §6R item 1): flags fronts, tries extension into empty 8-neighbours
    /// with p = 1 - exp(-(lambda_f/dist) * (beta + max(0, gradC . dir)) * g_W * dt) — the 1/dist term makes a
    /// diagonal colonisation attempt (dist = sqrt2) exactly as likely per unit distance covered as an axial one
    /// (dist = 1), which is what keeps the front an irregular blob instead of the Moore-neighbourhood square you
    /// get from an undamped per-neighbour probability. A boundary cell may only spend mass this way once its own
    /// mass exceeds <see cref="PlasmodiumParams.MOcc"/>, and the new cell's <see cref="PlasmodiumParams.MCell"/>
    /// starting mass is a direct transfer from its parent (no pool, no creation). Feeding then adds mass locally
    /// at cells over detritus, and a conservative transport pass (§6R items 1, 5, 6) redistributes mass along
    /// sheet/vein conductance before maintenance and m_min withdrawal remove whatever they remove — tallied, not
    /// discarded. Colonised cells are folded into whichever id currently owns their component's neighbours; call
    /// <see cref="Relabel"/> afterwards for up-to-date identity.
    /// </summary>
    public void Step(CoverageLayer layer, Attractant attractant, IPlasmodiumEnvironment env, long step, double dt,
        IPlasmodiumNetwork? network = null)
    {
        layer.BeginStep();

        attractant.Step(_cellId.Keys, (cx, cz) => SampleDetritusCoarse(env, cx, cz));

        // Retraction (§6.5): cells the transport network has flagged as off-network sheet vacate outright,
        // before front detection runs, so a retracting cell never gets re-flagged as a front the same step. Any
        // mass still sitting on the cell at that instant is tallied removed (§6R item 1), not discarded silently.
        if (network != null)
        {
            _retractBuf.Clear();
            foreach (var (gx, gz) in _cellId.Keys)
                if (network.ShouldRetract(gx, gz)) _retractBuf.Add((gx, gz));
            foreach (var cell in _retractBuf) VacateCell(layer, cell);
        }

        // Front detection first, over the unsorted dictionary (cheap membership tests only); the sort below is
        // over just the front cells, not the whole colony, which is the expensive part on a large sheet.
        _frontBuf.Clear();
        foreach (var (gx, gz) in _cellId.Keys)
        {
            ClearFrontFlag(layer, gx, gz); // recomputed fresh every step, so a cell that's no longer a front stops glowing
            if (network != null && network.ShouldRetract(gx, gz)) continue;
            bool isFront = false;
            foreach (var (dx, dz, _) in Dirs8)
                if (!_cellId.ContainsKey((gx + dx, gz + dz))) { isFront = true; break; }
            if (isFront) _frontBuf.Add((gx, gz));
        }
        _frontBuf.Sort();

        _toColoniseBuf.Clear();
        foreach (var (gx, gz) in _frontBuf)
        {
            SetFrontFlag(layer, gx, gz);

            int ownerId = _cellId[(gx, gz)];
            double ownMass = MassAt(gx, gz);
            if (ownMass < Params.MOcc) continue; // §6R item 5: only a boundary cell with m > m_occ may extend

            var (cx, cz) = Attractant.CoarseOf(gx, gz);
            var grad = attractant.GradientAt(cx, cz);
            double gW = Params.WetnessGate(env.Moisture(gx, gz));

            // Small per-cell biological-variability jitter (deterministic hash, not per-direction): without it,
            // every front cell along a wide, evenly-lit stretch sees the same gradient and gate and advances in
            // lockstep, freezing into a long straight wavefront the instant mass runs low. Real tissue doesn't
            // act that uniformly cell-to-cell; this desynchronises neighbours just enough to keep the edge ragged.
            double jitter = 0.6 + 0.8 * layer.Hash01(gx, gz, step, purpose: 1);

            for (int dirIdx = 0; dirIdx < Dirs8.Length; dirIdx++)
            {
                var (dx, dz, dist) = Dirs8[dirIdx];
                var n = (gx + dx, gz + dz);
                if (_cellId.ContainsKey(n) || layer.SnapshotOcc(n.Item1, n.Item2) != 0) continue;

                double uphill = Math.Max(0, (grad.X * dx + grad.Z * dz) / dist);
                double p = 1 - Math.Exp(-(Params.LambdaF / dist) * (Params.Beta + uphill) * gW * jitter * dt);
                p = Math.Min(p, Params.MaxExtensionProbability);
                if (p <= 0) continue;

                double u = layer.Hash01(gx, gz, step, purpose: 100 + dirIdx);
                if (u < p) _toColoniseBuf.Add((n, ownerId, (gx, gz)));
            }
        }

        foreach (var ((gx, gz), ownerId, parent) in _toColoniseBuf)
        {
            if (_cellId.ContainsKey((gx, gz))) continue; // already claimed by an earlier front cell this step
            double parentMass = MassAt(parent.gx, parent.gz);
            if (parentMass < Params.MCell) continue; // parent already spent below this on an earlier direction
            _mass[parent] = parentMass - Params.MCell;
            _mass[(gx, gz)] = Params.MCell;
            _cellId[(gx, gz)] = ownerId;
            layer.SetOcc(gx, gz, 1);
        }

        // Feeding: every occupied cell over detritus takes mass directly, locally (§6R item 1). Order doesn't
        // matter here (each cell's take is independent and mass addition is commutative), so no sort needed.
        double want = Params.FeedRate * dt;
        if (want > 0)
        {
            foreach (var (gx, gz) in _cellId.Keys)
            {
                double taken = env.TakeDetritus(gx, gz, want);
                if (taken > 0)
                {
                    _mass[(gx, gz)] = MassAt(gx, gz) + taken;
                    FedMass += taken;
                }
            }
        }

        // Conservative transport (§6R items 1, 3): moves mass between occupied cells only, sum unchanged.
        Transport(network, dt);

        // Maintenance cost (local, tallied removed) and m_min withdrawal (§6R item 6).
        ApplyMaintenanceAndWithdrawal(layer, network, dt);

        layer.Advance();
    }

    /// <summary>Vacates one cell outright: whatever mass remains on it is tallied into <see cref="RemovedMass"/>
    /// (not discarded silently), a residue flag (the Dead bit — no dedicated Residue bit exists) is left on the
    /// now-unoccupied ground for rendering, and it is dropped from both the mass and cell-id maps.</summary>
    private void VacateCell(CoverageLayer layer, (int gx, int gz) cell)
    {
        double leftover = MassAt(cell.gx, cell.gz);
        if (leftover > 0) RemovedMass += leftover;
        _mass.Remove(cell);
        _cellId.Remove(cell);
        SetResidueFlag(layer, cell.gx, cell.gz);
        layer.SetOcc(cell.gx, cell.gz, 0);
        ClearFrontFlag(layer, cell.gx, cell.gz);
    }

    /// <summary>
    /// Conservative mass flow along sheet adjacencies and vein edges (§6R items 1, 3): Q_ij = D_ij * (P_i - P_j)
    /// / dist, with P_i = P0 * m_i / MRef (the phase term A*sin(theta) arrives in Pl-2), D_ij = base sheet
    /// conductance plus whatever vein conductance the network reports for that pair (parallel paths, so they
    /// add). Two passes over a fixed cell order (ascending tuple) keep this deterministic: pass one computes
    /// every cell's total desired outflow this step; pass two derives a per-cell limiter (1 if outflow &lt;=
    /// current mass, otherwise mass/outflow) so no cell can be driven negative, then applies every edge's actual
    /// transfer scaled by its source cell's limiter. Every transfer subtracts from one cell and adds the same
    /// amount to another, so the sum over the colony is exactly unchanged by this method.
    /// </summary>
    private void Transport(IPlasmodiumNetwork? network, double dt)
    {
        if (dt <= 0 || _cellId.Count == 0) return;

        _cellsSortedBuf.Clear();
        _cellsSortedBuf.AddRange(_cellId.Keys);
        _cellsSortedBuf.Sort();

        _outFlowBuf.Clear();
        _edgeFlowBuf.Clear();

        foreach (var i in _cellsSortedBuf)
        {
            double mi = MassAt(i.gx, i.gz);
            double pi = Params.P0 * mi / Params.MRef;
            foreach (var (dx, dz, dist) in Dirs8)
            {
                var j = (i.gx + dx, i.gz + dz);
                if (i.CompareTo(j) >= 0) continue; // each unordered pair once
                if (!_cellId.ContainsKey(j)) continue;

                double mj = MassAt(j.Item1, j.Item2);
                double pj = Params.P0 * mj / Params.MRef;
                double dVein = network?.VeinConductance(i.gx, i.gz, j.Item1, j.Item2) ?? 0;
                double dTotal = Params.SheetConductance + dVein;
                if (dTotal <= 0) continue;

                double q = dTotal * (pi - pj) / dist;
                double amt = q * dt;
                if (amt > 0)
                {
                    _outFlowBuf[i] = _outFlowBuf.GetValueOrDefault(i) + amt;
                    _edgeFlowBuf.Add((i, j, amt));
                }
                else if (amt < 0)
                {
                    var from = j;
                    _outFlowBuf[from] = _outFlowBuf.GetValueOrDefault(from) + (-amt);
                    _edgeFlowBuf.Add((from, i, -amt));
                }
            }
        }

        // Convert accumulated outflow totals into per-cell limiters in place (safe: fully populated above,
        // consumed only in the loop below).
        foreach (var cell in _cellsSortedBuf)
        {
            if (!_outFlowBuf.TryGetValue(cell, out var totalOut) || totalOut <= 0) continue;
            double mass = MassAt(cell.gx, cell.gz);
            _outFlowBuf[cell] = totalOut > mass ? mass / totalOut : 1.0;
        }

        foreach (var (from, to, amount) in _edgeFlowBuf)
        {
            double limiter = _outFlowBuf.TryGetValue(from, out var lim) ? lim : 1.0;
            double actual = amount * limiter;
            if (actual <= 0) continue;
            _mass[from] = MassAt(from.gx, from.gz) - actual;
            _mass[to] = MassAt(to.Item1, to.Item2) + actual;
        }
    }

    /// <summary>Maintenance cost (local, tallied into <see cref="RemovedMass"/>) followed by m_min withdrawal
    /// (§6R item 6): any cell whose mass has fallen below <see cref="PlasmodiumParams.MMin"/> vacates outright.
    /// A cell that still sits on a surviving vein edge is exempt from mass-driven withdrawal even if its own
    /// local mass has dipped low: it legitimately runs thin under conservative diffusion toward a richer
    /// neighbour (exactly the mechanism §6R item 4's rectified transport will exploit) without being a candidate
    /// for removal — the network's own <see cref="IPlasmodiumNetwork.ShouldRetract"/> (conductance decay,
    /// already applied earlier this step) is the retraction authority for anything still carrying flow. Plain
    /// sheet cells off the vein network drain and vacate through this path exactly as spec'd.</summary>
    private void ApplyMaintenanceAndWithdrawal(CoverageLayer layer, IPlasmodiumNetwork? network, double dt)
    {
        if (Params.MaintenanceRate > 0)
        {
            foreach (var cell in _cellId.Keys.ToList())
            {
                double m = MassAt(cell.Item1, cell.Item2);
                double cost = Math.Min(m, Params.MaintenanceRate * dt);
                if (cost <= 0) continue;
                _mass[cell] = m - cost;
                RemovedMass += cost;
            }
        }

        _retractBuf.Clear();
        foreach (var cell in _cellId.Keys)
        {
            if (MassAt(cell.Item1, cell.Item2) >= Params.MMin) continue;
            if (network != null && IsOnLiveVein(network, cell)) continue;
            _retractBuf.Add(cell);
        }
        foreach (var cell in _retractBuf) VacateCell(layer, cell);
    }

    private bool IsOnLiveVein(IPlasmodiumNetwork network, (int gx, int gz) cell)
    {
        foreach (var (dx, dz, _) in Dirs8)
        {
            var n = (cell.Item1 + dx, cell.Item2 + dz);
            if (_cellId.ContainsKey(n) && network.VeinConductance(cell.Item1, cell.Item2, n.Item1, n.Item2) > 0)
                return true;
        }
        return false;
    }

    /// <summary>Marks a cell as m_min residue (§6R item 6): the Dead bit, left set on the now-unoccupied ground
    /// so the renderer can still show it fading, matching the convention <see cref="LifecycleController"/>
    /// already uses for Fruiting decay (no dedicated Residue bit exists in <see cref="CoverageFlags"/>).</summary>
    private static void SetResidueFlag(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        var tile = layer.GetOrCreateTile(ti, tj);
        int li = CoverageSpec.LocalIndex(gx, gz);
        byte before = tile.Flags[li];
        byte after = (byte)(before | (byte)CoverageFlags.Dead);
        if (after == before) return;
        tile.Flags[li] = after;
        tile.Touch();
    }

    /// <summary>Sets the Front flag bit directly on the tile's array, without touching Occ/B/W/Age/Dorm/D2E
    /// (unlike <see cref="CoverageLayer.SetCell"/>, which overwrites every field) and without the per-call
    /// allocation of going through the struct-returning getters first.</summary>
    private static void SetFrontFlag(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        var tile = layer.GetOrCreateTile(ti, tj);
        int li = CoverageSpec.LocalIndex(gx, gz);
        byte before = tile.Flags[li];
        byte after = (byte)(before | (byte)CoverageFlags.Front);
        bool occWasZero = tile.Occ[li] == 0;
        if (occWasZero) tile.Occ[li] = 1;
        if (after == before && !occWasZero) return; // nothing changed: skip the version bump
        tile.Flags[li] = after;
        tile.Active = true;
        tile.Touch();
    }

    private static void ClearFrontFlag(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!layer.TryGetTile(ti, tj, out var tile) || tile == null) return;
        int li = CoverageSpec.LocalIndex(gx, gz);
        byte before = tile.Flags[li];
        byte after = (byte)(before & ~(byte)CoverageFlags.Front);
        if (after == before) return;
        tile.Flags[li] = after;
        tile.Touch();
    }

    private static double SampleDetritusCoarse(IPlasmodiumEnvironment env, int cx, int cz)
    {
        // Coarse cell (cx, cz) covers fine cells (2cx..2cx+1, 2cz..2cz+1); average their detritus as F.
        double sum = 0;
        for (int dz = 0; dz < 2; dz++)
            for (int dx = 0; dx < 2; dx++)
                sum += env.Detritus(cx * 2 + dx, cz * 2 + dz);
        return sum / 4.0;
    }
}
