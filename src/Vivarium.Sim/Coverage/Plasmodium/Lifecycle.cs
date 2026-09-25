namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// One fruiting body produced by a Fruiting plasmodium (§6.6): sits at a former transport-network hub node
/// (coarse lattice coordinate), ripens over <see cref="PlasmodiumParams.FruitingRipenSeconds"/>, then releases
/// its spore exactly once via the configured <see cref="ISporeSink"/>. Deliberately a plain mutable record, not
/// a coverage-layer cell — spec calls these "small sim entities" distinct from the sheet.
/// </summary>
public sealed class FruitingBody
{
    /// <summary>Coarse (4 cm) lattice node this body sits at — the former hub, in the same coordinate space as
    /// <see cref="Network.NodeMaxD"/>.</summary>
    public (int cx, int cz) Pos { get; }

    /// <summary>Ripeness in [0,1]; 1 means ready to release (and, once <see cref="SporeReleased"/>, already has).</summary>
    public double Maturity { get; internal set; }

    /// <summary>Deterministic seed for whatever the spore-bank mechanism does with it (derived from owner id + index).</summary>
    public int Seed { get; }

    public bool SporeReleased { get; internal set; }

    public FruitingBody((int cx, int cz) pos, int seed)
    {
        Pos = pos;
        Seed = seed;
    }
}

/// <summary>Receives a spore released by a mature <see cref="FruitingBody"/> (§6.6). Hook for the (future)
/// spore-bank mechanism; the growth lab supplies a recording stub.</summary>
public interface ISporeSink
{
    void ReleaseSpore(FruitingBody body);
}

/// <summary>
/// Drives the Physarum life cycle (docs/overhaul/growth_models.md §6.1, §6.6) on top of an already-stepped
/// <see cref="PlasmodiumColony"/>/<see cref="Network"/> pair. Owns no day-to-day growth of its own: it watches
/// each organism's moisture/food timers, flips <see cref="Plasmodium.State"/>, and handles the mechanics that
/// only a transition needs — freezing/thawing (Sclerotium), placing fruiting bodies at former hub nodes and
/// collapsing the sheet to residue (Fruiting). The caller is responsible for not running normal
/// foraging/network steps on a Sclerotium organism (a real frozen state, not just a cosmetic flag) — this
/// controller only flags the state; a per-organism gate on the growth steps is the caller's job, exactly like
/// the growth-lab scenarios do.
/// </summary>
public sealed class LifecycleController
{
    private readonly PlasmodiumParams _prm;
    private readonly List<FruitingBody> _fruitingBodies = new();
    private readonly HashSet<int> _bodiesPlacedForOrg = new();
    private readonly HashSet<int> _decayedForOrg = new();

    /// <summary>Read-only view of every fruiting body ever placed (across all organisms), for rendering.</summary>
    public IReadOnlyList<FruitingBody> FruitingBodies => _fruitingBodies;

    /// <summary>Optional sink notified exactly once per body when it first reaches full maturity.</summary>
    public ISporeSink? SporeSink { get; set; }

    public LifecycleController(PlasmodiumParams prm) => _prm = prm;

    /// <summary>One life-cycle step over every organism currently known to <paramref name="colony"/>.</summary>
    public void Step(CoverageLayer layer, PlasmodiumColony colony, Network? network, IPlasmodiumEnvironment env,
        long step, double dt)
    {
        foreach (var org in colony.Organisms.Values.ToList())
        {
            var cells = CellsOf(colony, org.Id);
            if (cells.Count == 0) continue;

            org.TimeInState += dt;

            switch (org.State)
            {
                case PlasmodiumState.Foraging:
                    UpdateDryTimer(org, env, cells, dt);
                    UpdateStarveTimer(org, env, cells, dt);
                    if (org.TimeDry >= _prm.TS)
                    {
                        org.EnterState(PlasmodiumState.Sclerotium);
                        SetFlag(layer, cells, CoverageFlags.Sclerotium, true);
                    }
                    else if (org.TimeStarving >= _prm.TStarve)
                    {
                        org.EnterState(PlasmodiumState.Migrating);
                    }
                    break;

                case PlasmodiumState.Sclerotium:
                    UpdateDryTimer(org, env, cells, dt);
                    if (org.TimeDry <= 0)
                    {
                        org.EnterState(PlasmodiumState.Foraging);
                        SetFlag(layer, cells, CoverageFlags.Sclerotium, false);
                    }
                    // else: frozen. D/occupancy untouched here; caller does not step growth for this organism.
                    break;

                case PlasmodiumState.Migrating:
                    if (org.TimeInState >= _prm.TMig)
                    {
                        org.EnterState(PlasmodiumState.Fruiting);
                        PlaceFruitingBodies(org, colony, network, cells);
                        SetFlag(layer, cells, CoverageFlags.Fruiting, true);
                    }
                    break;

                case PlasmodiumState.Fruiting:
                    AdvanceFruitingBodies(org, dt);
                    if (!_decayedForOrg.Contains(org.Id) && org.TimeInState >= _prm.FruitingDecaySeconds)
                    {
                        _decayedForOrg.Add(org.Id);
                    }
                    if (_decayedForOrg.Contains(org.Id))
                    {
                        double decayElapsed = org.TimeInState - _prm.FruitingDecaySeconds;
                        double fade = _prm.ResidueFadeSeconds <= 0 ? 1.0
                            : Math.Clamp(decayElapsed / _prm.ResidueFadeSeconds, 0.0, 1.0);
                        SetResidue(layer, cells, fade);
                    }
                    break;

                case PlasmodiumState.Dormant:
                default:
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ timers

    private void UpdateDryTimer(Plasmodium org, IPlasmodiumEnvironment env, List<(int gx, int gz)> cells, double dt)
    {
        double mean = MeanMoisture(env, cells);
        if (mean < _prm.WS) org.TimeDry += dt;
        else org.TimeDry = 0;
    }

    private void UpdateStarveTimer(Plasmodium org, IPlasmodiumEnvironment env, List<(int gx, int gz)> cells, double dt)
    {
        double meanDetritus = MeanDetritus(env, cells);
        if (meanDetritus < _prm.StarveDetritusThreshold) org.TimeStarving += dt;
        else org.TimeStarving = 0;
    }

    private static double MeanMoisture(IPlasmodiumEnvironment env, List<(int gx, int gz)> cells)
    {
        double sum = 0;
        foreach (var (gx, gz) in cells) sum += env.Moisture(gx, gz);
        return cells.Count == 0 ? 0 : sum / cells.Count;
    }

    private static double MeanDetritus(IPlasmodiumEnvironment env, List<(int gx, int gz)> cells)
    {
        double sum = 0;
        foreach (var (gx, gz) in cells) sum += env.Detritus(gx, gz);
        return cells.Count == 0 ? 0 : sum / cells.Count;
    }

    private static List<(int gx, int gz)> CellsOf(PlasmodiumColony colony, int id)
    {
        var result = new List<(int, int)>();
        foreach (var (cell, ownerId) in colony.CellId)
            if (ownerId == id) result.Add(cell);
        return result;
    }

    // ------------------------------------------------------------------ fruiting placement (§6.6)

    /// <summary>
    /// Chooses K hub sites (local maxima of the network's summed adjacent conductance, §6.6), >= 5 cm apart,
    /// K proportional to the organism's remaining mass, and converts that mass into <see cref="FruitingBody"/>
    /// records. Falls back to the organism's own occupied cells (coarsened to the same lattice, evenly spaced)
    /// when no network was supplied, so fruiting still produces something without G7 wired in.
    /// </summary>
    private void PlaceFruitingBodies(Plasmodium org, PlasmodiumColony colony, Network? network, List<(int gx, int gz)> cells)
    {
        if (_bodiesPlacedForOrg.Contains(org.Id)) return;
        _bodiesPlacedForOrg.Add(org.Id);

        double orgMass = cells.Sum(c => colony.MassAt(c.gx, c.gz));

        var candidates = network != null
            ? LocalMaximaOf(network)
            : cells.Select(c => Attractant.CoarseOf(c.gx, c.gz)).Distinct().Select(n => (n, 0.0)).ToList();

        if (candidates.Count == 0) return;

        candidates.Sort((a, b) => b.Item2.CompareTo(a.Item2)); // descending by strength

        double coarseCellSize = CoverageSpec.CellSize * 2;
        double minSpacingCells = _prm.FruitingMinSpacingMeters / coarseCellSize;

        int k = Math.Max(1, (int)Math.Round(orgMass / Math.Max(1e-6, _prm.MassPerFruitingBody)));

        var chosen = new List<(int cx, int cz)>();
        foreach (var (node, _) in candidates)
        {
            if (chosen.Count >= k) break;
            bool farEnough = true;
            foreach (var c in chosen)
            {
                double dx = node.Item1 - c.cx, dz = node.Item2 - c.cz;
                if (Math.Sqrt(dx * dx + dz * dz) < minSpacingCells) { farEnough = false; break; }
            }
            if (farEnough) chosen.Add(node);
        }

        double consumed = colony.ConsumeMassForFruiting(cells); // whole sheet's remaining mass converts (§6.6)
        double massPerBody = chosen.Count > 0 ? consumed / chosen.Count : 0;

        for (int i = 0; i < chosen.Count; i++)
        {
            var body = new FruitingBody(chosen[i], seed: HashSeed(org.Id, i));
            _ = massPerBody; // mass is not currently modelled on the body itself; consumed above (§6.6: "mass converts")
            _fruitingBodies.Add(body);
        }
    }

    private static int HashSeed(int orgId, int index) => unchecked(orgId * 1000003 + index);

    /// <summary>Nodes whose summed/adjacent conductance (<see cref="Network.NodeMaxD"/>) is >= every present
    /// 8-neighbour's — the former hubs — paired with that value.</summary>
    private static List<((int cx, int cz) node, double strength)> LocalMaximaOf(Network network)
    {
        var d = network.NodeMaxD;
        var result = new List<((int, int), double)>();
        (int dx, int dz)[] dirs =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
        };
        foreach (var (node, strength) in d)
        {
            if (strength <= 0) continue;
            bool isMax = true;
            foreach (var (dx, dz) in dirs)
            {
                var n = (node.Item1 + dx, node.Item2 + dz);
                if (d.TryGetValue(n, out var nv) && nv > strength) { isMax = false; break; }
            }
            if (isMax) result.Add((node, strength));
        }
        return result;
    }

    private void AdvanceFruitingBodies(Plasmodium org, double dt)
    {
        if (_prm.FruitingRipenSeconds <= 0) return;
        foreach (var body in _fruitingBodies)
        {
            if (body.SporeReleased) continue;
            body.Maturity = Math.Min(1.0, body.Maturity + dt / _prm.FruitingRipenSeconds);
            if (body.Maturity >= 1.0)
            {
                body.SporeReleased = true;
                SporeSink?.ReleaseSpore(body);
            }
        }
    }

    // ------------------------------------------------------------------ rendering hooks

    private static void SetFlag(CoverageLayer layer, List<(int gx, int gz)> cells, CoverageFlags flag, bool on)
    {
        byte bit = (byte)flag;
        foreach (var (gx, gz) in cells)
        {
            var (ti, tj) = CoverageSpec.TileOf(gx, gz);
            var tile = layer.GetOrCreateTile(ti, tj);
            int li = CoverageSpec.LocalIndex(gx, gz);
            byte before = tile.Flags[li];
            byte after = on ? (byte)(before | bit) : (byte)(before & ~bit);
            if (after == before) continue;
            tile.Flags[li] = after;
            tile.Touch();
        }
    }

    /// <summary>Marks cells as decaying residue (Dead flag — no dedicated Residue bit in <see cref="CoverageFlags"/>)
    /// and fades their tube width toward zero as <paramref name="fade"/> goes 0 -> 1.</summary>
    private static void SetResidue(CoverageLayer layer, List<(int gx, int gz)> cells, double fade)
    {
        foreach (var (gx, gz) in cells)
        {
            var (ti, tj) = CoverageSpec.TileOf(gx, gz);
            var tile = layer.GetOrCreateTile(ti, tj);
            int li = CoverageSpec.LocalIndex(gx, gz);

            byte before = tile.Flags[li];
            byte after = (byte)(before | (byte)CoverageFlags.Dead);
            byte newW = (byte)Math.Clamp(tile.W[li] * (1.0 - fade), 0, 255);
            if (after == before && newW == tile.W[li]) continue;
            tile.Flags[li] = after;
            tile.W[li] = newW;
            tile.Touch();
        }
    }
}
