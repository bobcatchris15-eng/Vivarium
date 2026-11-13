using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

public readonly record struct ColonyOccupantSample(string SpeciesId, double Coverage, double Thickness, double Health);

public sealed record ColonyCellSample(IReadOnlyList<ColonyOccupantSample> Occupants);

/// Attached surface colonies as per-cell state on the ground lattice. Mass is conserved: every change is
/// either a live <-> litter transfer in the same cell, a growth/deposit input, or an output (dead pool,
/// decomposition). Budget "colony_biomass_kg" reports the closed balance.
public sealed class ColonySystem
{
    public const double Day = 86400.0;
    private const double DryMoisture = 0.15;        // top-layer moisture below this counts as dry
    private const double LitterSuppressThickness = 0.03;
    private const double MinCover = 1e-4;
    private const double MinMass = 1e-7;
    private const double VisibleChange = 0.02;
    private const double Sqrt2 = 1.4142135623730951;
    private const double SpreadDonorCover = 0.05;
    private const double SeedCover = 0.9;
    private const double NewArrivalCover = 0.25;
    private static readonly (int Dx, int Dz)[] Dirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private readonly LayeredSubstrate _sub;
    private readonly ulong _seed;
    private readonly Func<Vec3, double> _light;
    private readonly ColonyField _field;
    private readonly List<ColonySpecies> _species;
    private readonly double _cell, _cellArea;
    private readonly int _litterSid;
    private long _subCursor;
    private readonly List<DirtyRecord> _subDirty = new();
    private readonly HashSet<long> _check = new();
    private long _step;
    private double _simTime, _storedLast, _pendingIn, _inKg, _outKg, _deadPool, _decomposed;

    public DirtyLog DirtyLog { get; } = new();
    public ColonyField Field => _field;
    public IReadOnlyList<ColonySpecies> Species => _species;
    public double SimSeconds => _simTime;
    /// Biomass that left the colony entirely (kg, cumulative).
    public double DeadPool => _deadPool;
    /// Decomposed litter mass (kg, cumulative). Soil organic matter can receive this later.
    public double DecomposedTotal => _decomposed;
    /// Cells whose habitat cache was refreshed by the most recent Step (substrate dirty records).
    public int LastRefreshCount { get; private set; }

    public ColonySystem(LayeredSubstrate substrate, ulong worldSeed, Func<Vec3, double>? light = null,
        IEnumerable<ColonySpecies>? species = null, double cellSize = 0.02)
    {
        _sub = substrate ?? throw new ArgumentNullException(nameof(substrate));
        _seed = worldSeed;
        _light = light ?? (_ => 0.6);
        _species = new List<ColonySpecies>(species ?? ColonySpecies.Defaults);
        if (_species.Count > 120) throw new ArgumentException("too many colony species");
        _cell = cellSize;
        _cellArea = cellSize * cellSize;
        _field = new ColonyField(cellSize);
        _subCursor = substrate.DirtyLog.Head;
        for (int i = 0; i < _species.Count; i++)
            if (_species[i].Kind == ColonyKind.Litter) { _litterSid = i + 1; break; }
    }

    // ------------------------------------------------------------------ public API

    public int SpeciesIndex(string id)
    {
        for (int i = 0; i < _species.Count; i++) if (_species[i].Id == id) return i + 1;
        throw new ArgumentException($"unknown colony species '{id}'");
    }

    /// Establishes a nucleus with coverage ramping from the centre. Only cells with suitability > 0 are colonised.
    public void Seed(string speciesId, Vec2 at, double radius)
    {
        int sid = SpeciesIndex(speciesId);
        var sp = _species[sid - 1];
        if (sp.Kind == ColonyKind.Litter) throw new ArgumentException("litter is deposited, not seeded");
        if (!(radius > 0)) radius = _cell;
        var (cx, cz) = _field.CellOf(at);
        int r = (int)Math.Ceiling(radius / _cell) + 1;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                int ix = cx + dx, iz = cz + dz;
                double d = Vec2.Distance(_field.CellCentre(ix, iz), at);
                if (d > radius) continue;
                var t = _field.GetOrCreateCell(ix, iz, out int cell);
                EnsureHabitat(t, cell, ix, iz);
                if (!t.Usable[cell]) continue;
                double suit = Suitability(sid, t, cell, Moisture(t, cell));
                if (suit <= 0) continue;
                double f = d / radius;
                double c = Math.Max(0.05, SeedCover * (1 - f * f));
                double intensity = sp.MaxBiomass * 0.3 * suit;
                _pendingIn += Colonize(t, cell, sid, suit, intensity, c);
            }
        RecheckRims();
    }

    /// Deposits litter (kg dry) uniformly over the cells inside the disc. Conserved: stored rises by exactly kgDry.
    public void DepositLitter(Vec2 at, double radius, double kgDry)
    {
        if (!(kgDry > 0)) return;
        if (_litterSid == 0) throw new InvalidOperationException("no litter species configured");
        if (!(radius > 0)) radius = _cell;
        var (cx, cz) = _field.CellOf(at);
        int r = (int)Math.Ceiling(radius / _cell) + 1;
        var cells = new List<(ColonyTile T, int Cell)>();
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                int ix = cx + dx, iz = cz + dz;
                if (Vec2.Distance(_field.CellCentre(ix, iz), at) > radius) continue;
                var t = _field.GetOrCreateCell(ix, iz, out int cell);
                EnsureHabitat(t, cell, ix, iz);
                if (t.Usable[cell]) cells.Add((t, cell));
            }
        if (cells.Count == 0)
        {
            var t0 = _field.GetOrCreateCell(cx, cz, out int c0);
            EnsureHabitat(t0, c0, cx, cz);
            if (!t0.Usable[c0]) return;
            cells.Add((t0, c0));
        }
        double perCell = kgDry / (cells.Count * _cellArea);
        foreach (var (t, cell) in cells)
        {
            int slot = EnsureLitterSlot(t, cell);
            AddLitter(t, cell, slot, perCell);
            TouchCell(t, cell);
        }
        _pendingIn += perCell * _cellArea * cells.Count;
        RecheckRims();
    }

    /// Advances the colonies by dtSimSeconds. dt = 0 only processes substrate changes.
    public Budget Step(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        double before = _storedLast;
        _inKg = _pendingIn; _pendingIn = 0; _outKg = 0;
        ProcessSubstrateDirty();
        if (dt > 0)
        {
            bool sweep = _step % 8 == 0;
            if (sweep) RefreshLight();
            PhysiologyPass(dt, sweep);
            SpreadPass(dt);
            RecheckRims();
            Publish();
            _simTime += dt;
            _step++;
        }
        double after = Stored();
        _storedLast = after;
        return new Budget("colony_biomass_kg", before, after, _inKg, _outKg);
    }

    /// Total live plus litter biomass (kg).
    public double Stored()
    {
        double s = 0;
        foreach (var t in _field.Tiles)
            for (int k = 0; k < 2 * ColonyTile.N; k++)
                if (t.Species[k] != 0) s += t.Biomass[k];
        return s * _cellArea;
    }

    /// Covered area (m2) for one species.
    public double CoveredArea(string speciesId)
    {
        int sid = SpeciesIndex(speciesId);
        double a = 0;
        foreach (var t in _field.Tiles)
            for (int k = 0; k < 2 * ColonyTile.N; k++)
                if (t.Species[k] == sid) a += t.Coverage[k] * _cellArea;
        return a;
    }

    /// Biomass (kg) of one species, live only.
    public double Biomass(string speciesId)
    {
        int sid = SpeciesIndex(speciesId);
        double m = 0;
        foreach (var t in _field.Tiles)
            for (int k = 0; k < 2 * ColonyTile.N; k++)
                if (t.Species[k] == sid) m += t.Biomass[k];
        return m * _cellArea;
    }

    public int AllocatedCells => _field.Tiles.Count * ColonyTile.N;

    public int RimCellCount
    {
        get { int n = 0; foreach (var t in _field.Tiles) n += t.Rim.Count; return n; }
    }

    /// Rough per-cell memory of the arrays in one tile (bytes), for the cost report.
    public static double BytesPerAllocatedCell =>
        2 * (1 + 10 * 8 + 1 + 8)  // occupants: species, 10 doubles, dormant, +8 for LastBiomass rounding
        + (1 + 1 + 1 + 4 + 96 + 4 * 8 + 1)  // steady, usable, habitat valid, column, habitat sample, height/steep/light/decomposed
        + 2 * 16 * 8;                        // spread accumulators and jitter

    /// Connected components (8-neighbour) of one species as contract patches. Cell keys go into SurfaceIds.
    public IReadOnlyList<SurfaceColonyPatch> ExtractPatches(string speciesId)
    {
        int sid = SpeciesIndex(speciesId);
        var sp = _species[sid - 1];
        var keys = new List<long>();
        foreach (var t in _field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
                if (HasLive(t, cell, sid))
                {
                    ColonyField.CellCoords(t, cell, out int ix, out int iz);
                    keys.Add(ColonyField.CellKey(ix, iz));
                }
        keys.Sort();
        var visited = new HashSet<long>();
        var result = new List<SurfaceColonyPatch>();
        foreach (long start in keys)
        {
            if (!visited.Add(start)) continue;
            var comp = new List<long> { start };
            var queue = new Queue<long>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                long k = queue.Dequeue();
                int ix = ColonyField.CellX(k), iz = ColonyField.CellZ(k);
                foreach (var (dx, dz) in Dirs)
                {
                    long nk = ColonyField.CellKey(ix + dx, iz + dz);
                    if (visited.Contains(nk)) continue;
                    var nt = _field.FindCell(ix + dx, iz + dz, out int nc);
                    if (nt == null || !HasLive(nt, nc, sid)) continue;
                    visited.Add(nk);
                    comp.Add(nk);
                    queue.Enqueue(nk);
                }
            }
            comp.Sort();
            result.Add(BuildPatch(sid, sp, comp));
        }
        return result;
    }

    public bool TryGetTile(int tx, int tz, out ColonyTileView? view)
    {
        var t = _field.FindTile(tx, tz);
        view = t == null ? null : new ColonyTileView(t);
        return t != null;
    }

    /// Bilinear sample over the four cell centres around p, per species.
    public ColonyCellSample Sample(Vec2 p)
    {
        double fx = p.X / _cell - 0.5, fz = p.Z / _cell - 0.5;
        int ix0 = (int)Math.Floor(fx), iz0 = (int)Math.Floor(fz);
        double tx = fx - ix0, tz = fz - iz0;
        int n = _species.Count + 1;
        var cov = new double[n]; var thk = new double[n]; var hlt = new double[n];
        for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++)
            {
                double w = (dx == 1 ? tx : 1 - tx) * (dz == 1 ? tz : 1 - tz);
                var t = _field.FindCell(ix0 + dx, iz0 + dz, out int cell);
                if (t == null || w <= 0) continue;
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid == 0) continue;
                    cov[sid] += w * t.Coverage[k];
                    thk[sid] += w * t.Thickness[k];
                    hlt[sid] += w * t.Coverage[k] * t.Health[k];
                }
            }
        var list = new List<ColonyOccupantSample>();
        for (int sid = 1; sid < n; sid++)
            if (cov[sid] > 1e-9)
                list.Add(new ColonyOccupantSample(_species[sid - 1].Id, cov[sid], thk[sid], hlt[sid] / cov[sid]));
        return new ColonyCellSample(list);
    }

    /// Water the colony could hold (m), per cell: sum(thickness * waterHolding * coverage). Reported only.
    public double RetainedWater(Vec2 p)
    {
        var (ix, iz) = _field.CellOf(p);
        var t = _field.FindCell(ix, iz, out int cell);
        return t == null ? 0 : RetainedAt(t, cell);
    }

    public double TotalRetainedWater()
    {
        double s = 0;
        foreach (var t in _field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++) s += RetainedAt(t, cell);
        return s;
    }

    /// Cumulative decomposed litter in a cell (kg).
    public double DecomposedAt(Vec2 p)
    {
        var (ix, iz) = _field.CellOf(p);
        var t = _field.FindCell(ix, iz, out int cell);
        return t == null ? 0 : t.Decomposed[cell];
    }

    // ------------------------------------------------------------------ habitat

    private void RefreshCell(ColonyTile t, int cell, int ix, int iz)
    {
        var p = _field.CellCentre(ix, iz);
        t.HabitatValid[cell] = true;
        double h = _sub.Height(p);
        int c = double.IsFinite(h) ? _sub.ColumnAt(p) : -1;
        if (c < 0)
        {
            t.Usable[cell] = false; t.Column[cell] = -1; t.Height[cell] = 0; t.Light[cell] = 0;
            return;
        }
        t.Usable[cell] = true;
        t.Column[cell] = c;
        t.Habitat[cell] = _sub.SampleSubstrate(new Vec3(p.X, h - 1e-4, p.Z));
        t.Height[cell] = h;
        t.Steepness[cell] = _sub.ColumnSteepness(c);
        t.Light[cell] = _light(new Vec3(p.X, h + 0.01, p.Z));
        t.MinY = Math.Min(t.MinY, h);
        t.MaxY = Math.Max(t.MaxY, h);
    }

    private void EnsureHabitat(ColonyTile t, int cell, int ix, int iz)
    {
        if (!t.HabitatValid[cell]) RefreshCell(t, cell, ix, iz);
    }

    /// Only the cells inside a substrate change's bounds are refreshed; untouched regions are not revisited.
    private void ProcessSubstrateDirty()
    {
        LastRefreshCount = 0;
        _subDirty.Clear();
        _subCursor = _sub.DirtyLog.Read(_subCursor, DirtyDomain.Geometry | DirtyDomain.SoilMaterial, _subDirty);
        foreach (var r in _subDirty)
        {
            var b = r.Bounds;
            int ix0 = (int)Math.Floor(b.Min.X / _cell), ix1 = (int)Math.Floor(b.Max.X / _cell);
            int iz0 = (int)Math.Floor(b.Min.Z / _cell), iz1 = (int)Math.Floor(b.Max.Z / _cell);
            for (int ix = ix0; ix <= ix1; ix++)
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    var t = _field.FindCell(ix, iz, out int cell);
                    if (t == null) continue;
                    var p = _field.CellCentre(ix, iz);
                    if (p.X < b.Min.X || p.X > b.Max.X || p.Z < b.Min.Z || p.Z > b.Max.Z) continue;
                    RefreshCell(t, cell, ix, iz);
                    LastRefreshCount++;
                    TouchCell(t, cell);
                }
        }
    }

    private void RefreshLight()
    {
        foreach (var t in _field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (!t.Usable[cell]) continue;
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                var p = _field.CellCentre(ix, iz);
                t.Light[cell] = _light(new Vec3(p.X, t.Height[cell] + 0.01, p.Z));
            }
    }

    /// Moisture 0..1 from the column's top-layer saturation plus free film and standing water (live from the substrate).
    private double Moisture(ColonyTile t, int cell)
    {
        int c = t.Column[cell];
        if (c < 0) return 0;
        var w = _sub.Water;
        double sat = w.TopSaturation(c);
        double wet = (w.Film[c] + w.Standing[c]) / 0.0002;
        return MathD.Clamp01(sat + 0.7 * MathD.Clamp01(wet));
    }

    private double Suitability(int sid, ColonyTile t, int cell, double moist)
    {
        var sp = _species[sid - 1];
        if (!t.Usable[cell]) return 0;
        double moistR = MathD.Preference(moist, sp.MoistureOptimum, sp.MoistureTolerance);
        double shade = MathD.Clamp01(1 - LitterThickness(t, cell) / LitterSuppressThickness);
        double lightR = MathD.Preference(t.Light[cell] * shade, sp.LightOptimum, sp.LightTolerance);
        double sub = sp.SubstrateResponse(t.Habitat[cell]);
        double slope = 1.0 / (1.0 + Math.Pow(t.Steepness[cell] / Math.Max(1e-6, sp.SlopeTolerance), 2));
        return MathD.Clamp01(moistR * lightR * sub * slope);
    }

    // ------------------------------------------------------------------ occupancy helpers

    private static bool HasLive(ColonyTile t, int cell, int sid) =>
        (t.Species[cell * 2] == sid && t.Coverage[cell * 2] > 1e-3) ||
        (t.Species[cell * 2 + 1] == sid && t.Coverage[cell * 2 + 1] > 1e-3);

    private static int FreeSlot(ColonyTile t, int cell)
    {
        for (int s = 0; s < 2; s++) if (t.Species[cell * 2 + s] == 0) return s;
        return -1;
    }

    private static int SlotOf(ColonyTile t, int cell, int sid)
    {
        for (int s = 0; s < 2; s++) if (t.Species[cell * 2 + s] == sid) return s;
        return -1;
    }

    private int SlotOfKind(ColonyTile t, int cell, ColonyKind kind)
    {
        for (int s = 0; s < 2; s++)
        {
            int sid = t.Species[cell * 2 + s];
            if (sid != 0 && _species[sid - 1].Kind == kind) return s;
        }
        return -1;
    }

    private double CoverTotal(ColonyTile t, int cell) => t.Coverage[cell * 2] + t.Coverage[cell * 2 + 1];

    private double LitterThickness(ColonyTile t, int cell)
    {
        double sum = 0;
        for (int s = 0; s < 2; s++)
        {
            int k = cell * 2 + s, sid = t.Species[k];
            if (sid != 0 && _species[sid - 1].Kind == ColonyKind.Litter) sum += t.Thickness[k];
        }
        return sum;
    }

    private double RetainedAt(ColonyTile t, int cell)
    {
        double sum = 0;
        for (int s = 0; s < 2; s++)
        {
            int k = cell * 2 + s, sid = t.Species[k];
            if (sid != 0) sum += t.Thickness[k] * _species[sid - 1].WaterHolding * t.Coverage[k];
        }
        return sum;
    }

    private void Clear(ColonyTile t, int cell, int slot)
    {
        int k = cell * 2 + slot;
        t.Species[k] = 0;
        t.Coverage[k] = 0; t.Biomass[k] = 0; t.Thickness[k] = 0; t.Health[k] = 0; t.FrontAge[k] = 0;
        t.Dryness[k] = 0; t.Dormant[k] = false; t.LastSuit[k] = 0; t.LastBiomass[k] = 0;
        t.Steady[cell] = false;
    }

    private void TouchCell(ColonyTile t, int cell)
    {
        ColonyField.CellCoords(t, cell, out int ix, out int iz);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                _check.Add(ColonyField.CellKey(ix + dx, iz + dz));
        t.Steady[cell] = false;
    }

    private void AddLitter(ColonyTile t, int cell, int slot, double amount)
    {
        int k = cell * 2 + slot;
        if (t.Species[k] == 0)
        {
            t.Species[k] = (byte)_litterSid;
            t.Health[k] = 1; t.FrontAge[k] = 0; t.Dryness[k] = 0; t.Dormant[k] = false;
        }
        t.Biomass[k] += amount;
        t.Coverage[k] = Math.Max(0, Math.Min(1 - t.Coverage[cell * 2 + (1 - slot)], 0.5 * t.Biomass[k]));
        t.Thickness[k] = 0.01 * t.Biomass[k];
        t.Steady[cell] = false;
    }

    /// Moves biomass that left a live occupant into the cell's litter (internal) or, with no room, to the dead pool (Out).
    private void RouteToLitter(ColonyTile t, int cell, double amount)
    {
        if (!(amount > 0)) return;
        int s = _litterSid == 0 ? -1 : SlotOfKind(t, cell, ColonyKind.Litter);
        if (s < 0 && _litterSid != 0) s = FreeSlot(t, cell);
        if (s < 0)
        {
            double kg = amount * _cellArea;
            _deadPool += kg; _outKg += kg;
            return;
        }
        AddLitter(t, cell, s, amount);
    }

    /// Litter slot for a deposit; evicts the weakest live occupant if the cell is full (its mass becomes litter).
    private int EnsureLitterSlot(ColonyTile t, int cell)
    {
        int s = SlotOfKind(t, cell, ColonyKind.Litter);
        if (s >= 0) return s;
        s = FreeSlot(t, cell);
        if (s >= 0) return s;
        int weak = WeakestLive(t, cell);
        double m = t.Biomass[cell * 2 + weak];
        Clear(t, cell, weak);
        AddLitter(t, cell, weak, m);
        return weak;
    }

    private int WeakestLive(ColonyTile t, int cell)
    {
        int weak = -1; double ws = double.MaxValue;
        for (int s = 0; s < 2; s++)
        {
            int k = cell * 2 + s, sid = t.Species[k];
            if (sid == 0 || _species[sid - 1].Kind == ColonyKind.Litter) continue;
            double sc = _species[sid - 1].CompetitiveHeight * t.Health[k] * t.LastSuit[k];
            if (sc < ws) { ws = sc; weak = s; }
        }
        return weak;
    }

    // ------------------------------------------------------------------ physiology

    private static double DieRate(ColonySpecies sp) => sp.Kind == ColonyKind.Moss ? 0.05 : 0.002;   // 1/day, dormant dieback
    private static double DecayRate(ColonySpecies sp) => sp.Kind == ColonyKind.Moss ? 0.1 : 0.02;   // 1/day, excess biomass

    private void PhysiologyPass(double dt, bool sweep)
    {
        foreach (var t in _field.Tiles)
        {
            if (t.Rim.Count == 0 && !sweep && t.SteadyCount == t.Occupied) continue;   // fully steady, no front: skip
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (t.Species[cell * 2] == 0 && t.Species[cell * 2 + 1] == 0) continue;
                if (t.Steady[cell])
                {
                    if (sweep) Physiology(t, cell, dt * 8);  // steady cells: every 8th step with dt x 8
                }
                else Physiology(t, cell, dt);
            }
            int occ = 0, st = 0;
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (t.Species[cell * 2] == 0 && t.Species[cell * 2 + 1] == 0) continue;
                occ++;
                if (t.Steady[cell]) st++;
            }
            t.Occupied = occ;
            t.SteadyCount = st;
        }
    }

    private void Physiology(ColonyTile t, int cell, double dt)
    {
        double dayF = dt / Day;
        double moist = Moisture(t, cell);
        bool anyLive = false, allSteady = true;
        for (int slot = 0; slot < 2; slot++)
        {
            int k = cell * 2 + slot, sid = t.Species[k];
            if (sid == 0) continue;
            var sp = _species[sid - 1];
            if (sp.Kind == ColonyKind.Litter) { DecomposeLitter(t, cell, slot, moist, dayF); continue; }

            double other = t.Coverage[cell * 2 + (1 - slot)];
            double suit = Suitability(sid, t, cell, moist);
            double C = t.Coverage[k], B = t.Biomass[k];
            double I = C > 1e-12 ? B / C : 0;
            double tolS = sp.DesiccationToleranceDays * Day;
            t.Dryness[k] = moist < DryMoisture ? t.Dryness[k] + dt : Math.Max(0, t.Dryness[k] - 2 * dt);
            t.Dormant[k] = t.Dryness[k] > tolS;

            double gain = 0, loss = 0, Cn = C;
            if (t.Dormant[k])
            {
                if (t.Dryness[k] > 2 * tolS) loss += B * DieRate(sp) * dayF;
            }
            else
            {
                double dC;
                if (suit >= 0.05)
                    dC = Math.Min(sp.GrowthRate * suit * C * (1 - C) * dayF, Math.Max(0, 1 - C - other));
                else
                    dC = -0.05 * C * dayF;                    // unhospitable: coverage retreats
                Cn = C + dC;
                if (dC > 0) gain += dC * I;                   // newly covered ground takes the donor intensity
                else loss += -dC * I;

                double Bm = B + gain - loss;
                double Istar = sp.MaxBiomass * suit;
                double Ip = Cn > 1e-12 ? Bm / Cn : 0;
                if (Istar > 1e-12 && Ip < Istar) gain += sp.GrowthRate * Bm * (1 - Ip / Istar) * dayF;
                else if (Ip > Istar) loss += DecayRate(sp) * Math.Max(0, Bm - Cn * Istar) * dayF;
            }

            loss = Math.Min(loss, B + gain);
            double Bn = B + gain - loss;
            _inKg += gain * _cellArea;
            RouteToLitter(t, cell, loss);                     // live -> litter (same cell) or dead pool
            t.Biomass[k] = Bn;
            Cn = Math.Max(0, Math.Min(1 - other, Cn));

            if (Cn < MinCover)
            {
                double rem = t.Biomass[k];
                Clear(t, cell, slot);
                TouchCell(t, cell);
                RouteToLitter(t, cell, rem);
                continue;
            }
            t.Coverage[k] = Cn;
            double target = t.Dormant[k] ? 0 : suit;
            t.Health[k] += (target - t.Health[k]) * Math.Min(1, 2 * dayF);
            t.Thickness[k] = sp.MaxThickness * Math.Min(1, (Bn / Cn) / sp.MaxBiomass);
            t.FrontAge[k] += dt;

            double dS = Math.Abs(suit - t.LastSuit[k]), dB = Math.Abs(Bn - t.LastBiomass[k]);
            bool steady = dS < 0.005 && dB < 0.005 * Math.Max(Bn, 1e-12);
            t.LastSuit[k] = suit;
            t.LastBiomass[k] = Bn;
            anyLive = true;
            allSteady &= steady;
        }
        t.Steady[cell] = anyLive && allSteady && !t.Rim.Contains(cell);
    }

    private void DecomposeLitter(ColonyTile t, int cell, int slot, double moist, double dayF)
    {
        int k = cell * 2 + slot;
        double B = t.Biomass[k];
        double kd = (0.005 + 0.05 * moist) * dayF;            // rises with moisture
        double loss = Math.Min(B, B * kd);
        double Bn = B - loss;
        double kg = loss * _cellArea;
        _decomposed += kg; t.Decomposed[cell] += kg; _outKg += kg;
        if (Bn < MinMass)
        {
            double rem = Bn * _cellArea;
            _decomposed += rem; t.Decomposed[cell] += rem; _outKg += rem;
            Clear(t, cell, slot);
            return;
        }
        t.Biomass[k] = Bn;
        t.Coverage[k] = Math.Max(0, Math.Min(1 - t.Coverage[cell * 2 + (1 - slot)], 0.5 * Bn));
        t.Thickness[k] = 0.01 * Bn;
    }

    // ------------------------------------------------------------------ spread and competition

    /// Newcomer of species sid arrives in a cell with coverage dC at donor intensity. Returns mass placed (kg).
    private double Colonize(ColonyTile t, int cell, int sid, double suit, double intensity, double dC)
    {
        var sp = _species[sid - 1];
        int same = SlotOf(t, cell, sid);
        double free = Math.Max(0, 1 - CoverTotal(t, cell));
        if (same >= 0)
        {
            double add = Math.Min(dC, free);
            return add > 0 ? Place(t, cell, same, sid, add, intensity, suit) : 0;
        }
        int fs = FreeSlot(t, cell);
        if (fs >= 0 && free > 0)
        {
            double add = Math.Min(dC, free);
            return Place(t, cell, fs, sid, add, intensity, suit);
        }
        if (fs >= 0) return 0;
        // Full cell: the weakest live occupant loses coverage in proportion to the score difference.
        int weak = WeakestLive(t, cell);
        if (weak < 0) return 0;
        int kw = cell * 2 + weak;
        var wsp = _species[t.Species[kw] - 1];
        double newScore = sp.CompetitiveHeight * suit;
        double weakScore = wsp.CompetitiveHeight * t.Health[kw] * t.LastSuit[kw];
        if (newScore <= weakScore) return 0;
        double rate = (newScore - weakScore) / newScore;
        double Cw = t.Coverage[kw];
        double Iw = Cw > 1e-12 ? t.Biomass[kw] / Cw : 0;
        double take = Math.Min(Cw, dC * rate);
        if (take <= 0) return 0;
        double lost = take * Iw;
        t.Coverage[kw] = Cw - take;
        t.Biomass[kw] = (Cw - take) * Iw;
        TouchCell(t, cell);
        if (t.Coverage[kw] < MinCover)
        {
            double rem = t.Biomass[kw];
            Clear(t, cell, weak);
            RouteToLitter(t, cell, rem);
        }
        RouteToLitter(t, cell, lost);
        int slot = FreeSlot(t, cell);
        if (slot < 0) return 0;
        double add2 = Math.Min(dC, Math.Max(0, 1 - CoverTotal(t, cell)));
        return add2 > 0 ? Place(t, cell, slot, sid, add2, intensity, suit) : 0;
    }

    private double Place(ColonyTile t, int cell, int slot, int sid, double add, double intensity, double suit)
    {
        var sp = _species[sid - 1];
        int k = cell * 2 + slot;
        if (t.Species[k] == 0)
        {
            t.Species[k] = (byte)sid;
            t.Coverage[k] = 0; t.Biomass[k] = 0; t.Thickness[k] = 0;
            t.Health[k] = 0.5; t.FrontAge[k] = 0; t.Dryness[k] = 0; t.Dormant[k] = false;
            t.LastSuit[k] = suit; t.LastBiomass[k] = 0;
        }
        double m = add * intensity;
        t.Coverage[k] += add;
        t.Biomass[k] += m;
        t.Thickness[k] = sp.MaxThickness * Math.Min(1, (t.Biomass[k] / t.Coverage[k]) / sp.MaxBiomass);
        TouchCell(t, cell);
        return m * _cellArea;
    }

    private void SpreadPass(double dt)
    {
        double dayF = dt / Day;
        var tiles = _field.Tiles.ToArray();   // snapshot: neighbour tiles may be allocated during the pass
        foreach (var t in tiles)
        {
            if (t.Rim.Count == 0) continue;
            var rims = new int[t.Rim.Count];
            t.Rim.CopyTo(rims);
            Array.Sort(rims);
            foreach (int cell in rims)
            {
                if (!t.Rim.Contains(cell)) continue;
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid == 0 || _species[sid - 1].Kind == ColonyKind.Litter) continue;
                    if (t.Coverage[k] < SpreadDonorCover) continue;
                    var sp = _species[sid - 1];
                    double intensity = t.Biomass[k] / t.Coverage[k];
                    for (int d = 0; d < Dirs.Length; d++)
                    {
                        var (dx, dz) = Dirs[d];
                        var nt = _field.GetOrCreateCell(ix + dx, iz + dz, out int ncell);
                        EnsureHabitat(nt, ncell, ix + dx, iz + dz);
                        if (!nt.Usable[ncell]) continue;
                        double suitN = Suitability(sid, nt, ncell, Moisture(nt, ncell));
                        if (suitN <= 0) continue;
                        int idx = (cell * 2 + slot) * 8 + d;
                        if (t.Jitter[idx] == 0)
                            t.Jitter[idx] = 0.8 + 0.4 * ReprId.Unit(ReprId.Hash(_seed, (ulong)ColonyField.CellKey(ix, iz), (ulong)(slot * 8 + d)), 0x7A11);
                        double spacing = _cell * (dx != 0 && dz != 0 ? Sqrt2 : 1.0) * t.Jitter[idx];
                        t.Spread[idx] += sp.SpreadRate * suitN * dayF;
                        if (t.Spread[idx] < spacing) continue;
                        t.Spread[idx] -= spacing;
                        _inKg += Colonize(nt, ncell, sid, suitN, intensity, NewArrivalCover);
                    }
                }
            }
        }
    }

    /// Rim = a live occupied cell with at least one 8-neighbour lacking the same species. Only changed cells are rechecked.
    private void RecheckRims()
    {
        foreach (long key in _check)
        {
            int ix = ColonyField.CellX(key), iz = ColonyField.CellZ(key);
            var t = _field.FindCell(ix, iz, out int cell);
            if (t == null) continue;
            bool isRim = false;
            for (int slot = 0; slot < 2 && !isRim; slot++)
            {
                int sid = t.Species[cell * 2 + slot];
                if (sid == 0 || _species[sid - 1].Kind == ColonyKind.Litter) continue;
                if (t.Coverage[cell * 2 + slot] <= 1e-3) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    var nt = _field.FindCell(ix + dx, iz + dz, out int nc);
                    if (nt == null || !HasLive(nt, nc, sid)) { isRim = true; break; }
                }
            }
            if (isRim) t.Rim.Add(cell);
            else t.Rim.Remove(cell);
        }
        _check.Clear();
    }

    // ------------------------------------------------------------------ publishing and patches

    private void Publish()
    {
        foreach (var t in _field.Tiles)
        {
            bool changed = false;
            for (int k = 0; k < 2 * ColonyTile.N && !changed; k++)
                changed = Math.Abs(t.Coverage[k] - t.PubCoverage[k]) > VisibleChange
                       || Math.Abs(t.Thickness[k] - t.PubThickness[k]) > VisibleChange;
            if (!changed) continue;
            Array.Copy(t.Coverage, t.PubCoverage, t.Coverage.Length);
            Array.Copy(t.Thickness, t.PubThickness, t.Thickness.Length);
            t.Version++;
            DirtyLog.Publish(new DirtyRecord(_field.TileBounds(t), DirtyDomain.Coverage, t.Version, "grow", _simTime));
        }
    }

    private SurfaceColonyPatch BuildPatch(int sid, ColonySpecies sp, List<long> comp)
    {
        int n = comp.Count;
        var ids = new ulong[n];
        var cov = new float[n]; var thk = new float[n]; var bio = new float[n];
        var hlt = new float[n]; var age = new float[n];
        double area = 0, mass = 0;
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        ulong version = 0;
        for (int i = 0; i < n; i++)
        {
            long key = comp[i];
            int ix = ColonyField.CellX(key), iz = ColonyField.CellZ(key);
            var t = _field.FindCell(ix, iz, out int cell)!;
            int slot = SlotOf(t, cell, sid);
            int k = cell * 2 + slot;
            ids[i] = unchecked((ulong)key);
            cov[i] = (float)t.Coverage[k]; thk[i] = (float)t.Thickness[k]; bio[i] = (float)t.Biomass[k];
            hlt[i] = (float)t.Health[k]; age[i] = (float)t.FrontAge[k];
            area += t.Coverage[k] * _cellArea;
            mass += t.Biomass[k] * _cellArea;
            version = Math.Max(version, t.Version);
            var p = _field.CellCentre(ix, iz);
            x0 = Math.Min(x0, p.X - _cell / 2); x1 = Math.Max(x1, p.X + _cell / 2);
            z0 = Math.Min(z0, p.Z - _cell / 2); z1 = Math.Max(z1, p.Z + _cell / 2);
            y0 = Math.Min(y0, t.Height[cell]); y1 = Math.Max(y1, t.Height[cell]);
        }
        return new SurfaceColonyPatch
        {
            PatchId = ReprId.Hash(_seed, ids[0], (ulong)sid),
            SpeciesId = sp.Id,
            Kind = sp.Kind,
            Version = version,
            Bounds = new Aabb(new Vec3(x0, y0, z0), new Vec3(x1, y1 + 0.01, z1)),
            SurfaceIds = ids,
            Coverage = cov,
            Thickness = thk,
            Biomass = bio,
            Health = hlt,
            FrontAge = age,
            Area = area,
            TotalBiomass = mass,
        };
    }
}
