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

    private readonly Dictionary<(int, int), int> _cellId = new();
    private readonly Dictionary<int, Plasmodium> _organisms = new();
    private int _nextId = 1;

    public PlasmodiumColony(int speciesId, PlasmodiumParams prm)
    {
        SpeciesId = speciesId;
        Params = prm;
    }

    /// <summary>Seeds a brand-new plasmodium at the given cells with a fresh id and the configured initial mass.</summary>
    public int Seed(IEnumerable<(int gx, int gz)> cells)
    {
        int id = _nextId++;
        _organisms[id] = new Plasmodium(id, SpeciesId, Params.InitialMass);
        foreach (var c in cells) _cellId[c] = id;
        return id;
    }

    public double TotalMass()
    {
        double m = 0;
        foreach (var o in _organisms.Values) m += o.MassPool;
        return m;
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
        // organisms into the smallest id right away; this consumes those old ids entirely (§6.1).
        var consumedOldIds = new HashSet<int>();
        var fusedMass = new Dictionary<int, double>();
        for (int i = 0; i < components.Count; i++)
        {
            if (overlaps[i].Count <= 1) continue;
            int keepId = overlaps[i].Keys.Min();
            double mass = 0;
            foreach (var oldId in overlaps[i].Keys)
            {
                if (_organisms.TryGetValue(oldId, out var org)) mass += org.MassPool;
                consumedOldIds.Add(oldId);
            }
            assignedId[i] = keepId;
            fusedMass[i] = mass;
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
            double oldMass = _organisms.TryGetValue(oldId, out var org) ? org.MassPool : 0;
            int totalCells = compIndices.Sum(i => components[i].Count);

            // Deterministic winner: most cells, ties broken by the smallest first cell.
            int winner = compIndices
                .OrderByDescending(i => components[i].Count)
                .ThenBy(i => components[i][0])
                .First();

            foreach (int i in compIndices)
            {
                int id = i == winner ? oldId : _nextId++;
                assignedId[i] = id;
                fusedMass[i] = totalCells > 0 ? oldMass * components[i].Count / totalCells : 0;
            }
        }

        // Pass 4: anything left is a brand-new component with no old-id overlap at all.
        for (int i = 0; i < components.Count; i++)
        {
            if (assignedId[i].HasValue) continue;
            assignedId[i] = _nextId++;
            fusedMass[i] = 0;
        }

        var newCellId = new Dictionary<(int, int), int>();
        var newOrganisms = new Dictionary<int, Plasmodium>();
        for (int i = 0; i < components.Count; i++)
        {
            int id = assignedId[i]!.Value;
            var existing = _organisms.TryGetValue(id, out var e) ? e : null;
            var plasmodium = new Plasmodium(id, SpeciesId, fusedMass[i], existing?.State ?? PlasmodiumState.Foraging)
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
    private readonly List<((int gx, int gz) cell, int ownerId)> _toColoniseBuf = new();

    /// <summary>
    /// One foraging step (§6.4): flags fronts, tries extension into empty 8-neighbours with
    /// p = 1 - exp(-(lambda_f/dist) * (beta + max(0, gradC . dir)) * g_W * dt) — the 1/dist term makes a
    /// diagonal colonisation attempt (dist = sqrt2) exactly as likely per unit distance covered as an axial one
    /// (dist = 1), which is what keeps the front an irregular blob instead of the Moore-neighbourhood square you
    /// get from an undamped per-neighbour probability — paid for out of the owning plasmodium's mass pool, then
    /// feeds every occupied cell over detritus back into that pool. Colonised cells are folded into whichever id
    /// currently owns their component's neighbours; call <see cref="Relabel"/> afterwards for up-to-date identity.
    /// </summary>
    public void Step(CoverageLayer layer, Attractant attractant, IPlasmodiumEnvironment env, long step, double dt,
        IPlasmodiumNetwork? network = null)
    {
        layer.BeginStep();

        attractant.Step(_cellId.Keys, (cx, cz) => SampleDetritusCoarse(env, cx, cz));

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
            var organism = _organisms[ownerId];
            if (organism.MassPool < Params.MCell) continue;

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
                if (u < p) _toColoniseBuf.Add((n, ownerId));
            }
        }

        foreach (var ((gx, gz), ownerId) in _toColoniseBuf)
        {
            if (_cellId.ContainsKey((gx, gz))) continue; // already claimed by an earlier front cell this step
            var organism = _organisms[ownerId];
            if (organism.MassPool < Params.MCell) continue;
            organism.MassPool -= Params.MCell;
            _cellId[(gx, gz)] = ownerId;
            layer.SetOcc(gx, gz, 1);
        }

        // Feeding: every occupied cell over detritus takes into its owner's mass pool (§6.4). Order doesn't
        // matter here (each cell's take is independent and mass addition is commutative), so no sort needed.
        double want = Params.FeedRate * dt;
        if (want > 0)
        {
            foreach (var ((gx, gz), ownerId) in _cellId)
            {
                double taken = env.TakeDetritus(gx, gz, want);
                if (taken > 0) _organisms[ownerId].MassPool += taken;
            }
        }

        layer.Advance();
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
