using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

public readonly record struct ColonyOccupantSample(string SpeciesId, double Coverage, double Thickness, double Health);

public sealed record ColonyCellSample(IReadOnlyList<ColonyOccupantSample> Occupants);

/// Attached surface colonies as per-cell state on the ground lattice.
///
/// Conservation: every change is a live <-> litter transfer in the same cell (internal), a growth or deposit input
/// (In), or a dead-pool / decomposition output (Out). Budget "colony_biomass_kg" closes to 1e-9 relative.
///
/// Coverage relaxes toward clamp(1.4 x suitability, 0, 1) (growth rate when rising, 0.05/day when retreating), so
/// marginal habitat stays patchy. Biomass is intensity (kg per covered m2, capped at MaxBiomass) times coverage.
///
/// Moisture (colony-side rule, the substrate is not written):
///  * soil cells: top-layer saturation + 0.7 x clamp(water / 0.2 mm), water = film + ponded;
///  * bare-rock cells: max( clamp(water / 0.1 mm), 0.7 x mean top-layer saturation of soil within +-10 cm,
///    0.35 x (1 - light) ). Seepage from adjacent soil and shade humidity make rock next to wet soil or in shade damp;
///    rock in the open, far from soil, stays dry.
///
/// Litter: deposited into cells as litter occupants (coverage 0.5 x mass, thickness 1 cm per kg/m2); decomposes at
/// (0.005 + 0.05 x moisture)/day; decomposed mass is booked Out and kept per cell for soil organic matter.
///
/// Dirty publishing: a tile publishes a Coverage DirtyRecord when the summed |dCoverage| since its last publish
/// reaches 0.02 (a conservative per-tile approximation of "0.02 in any cell"; thickness is not tracked).
public sealed class ColonySystem
{
    public const double Day = 86400.0;
    private const double DryMoisture = 0.15;
    private const double LitterSuppressThickness = 0.03;
    private const double MinCover = 1e-4;
    private const double MinMass = 1e-7;
    private const double VisibleChange = 0.02;
    private const double Sqrt2 = 1.4142135623730951;
    private const double SpreadDonorCover = 0.05;
    private const double SeedCover = 0.9;
    private const double NewArrivalCover = 0.25;
    private const double RetreatRate = 0.05;          // 1/day coverage retreat when suitability falls
    private const double TargetPerSuit = 1.4;         // equilibrium coverage = clamp(1.4 x suitability)
    private const double UnitSeconds = 600.0;         // ushort age/dryness unit
    private const double SeepHalfWidth = 0.10;        // m, soil seepage search half-width
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
    /// Decomposed litter mass (kg, cumulative).
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
                if (!t.Has(cell, ColonyTile.FUsable)) continue;
                double suit = Suitability(sid, t, cell, Moisture(t, cell));
                if (suit <= 0) continue;
                double f = d / radius;
                double c = Math.Max(0.05, SeedCover * (1 - f * f));
                _pendingIn += Colonize(t, cell, sid, suit, sp.MaxBiomass * 0.3 * suit, c);
            }
        RecheckRims();
    }

    /// Deposits litter (kg dry) uniformly over the usable cells inside the disc. Stored rises by exactly kgDry.
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
                if (t.Has(cell, ColonyTile.FUsable)) cells.Add((t, cell));
            }
        if (cells.Count == 0)
        {
            var t0 = _field.GetOrCreateCell(cx, cz, out int c0);
            EnsureHabitat(t0, c0, cx, cz);
            if (!t0.Has(c0, ColonyTile.FUsable)) return;
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
                if (t.Species[k] == sid) a += t.Cover(k) * _cellArea;
        return a;
    }

    /// Biomass (kg) of one species.
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

    /// Dense bytes per allocated cell (exact sum of the packed arrays). Sparse rim state is extra.
    public const int DenseBytesPerCell = 2 * (1 + 2 + 8 + 2 + 1 + 2 + 2 + 1) + (1 + 1 + 1 + 1 + 1 + 4);

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
                    cov[sid] += w * t.Cover(k);
                    thk[sid] += w * t.Thick(k);
                    hlt[sid] += w * t.Cover(k) * t.Health(k);
                }
            }
        var list = new List<ColonyOccupantSample>();
        for (int sid = 1; sid < n; sid++)
            if (cov[sid] > 1e-9)
                list.Add(new ColonyOccupantSample(_species[sid - 1].Id, cov[sid], thk[sid], hlt[sid] / cov[sid]));
        return new ColonyCellSample(list);
    }

    /// Water the colony could hold (m) at p: sum(thickness x waterHolding x coverage). Reported only, never written.
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

    /// Cumulative decomposed litter in the cell at p (kg).
    public double DecomposedAt(Vec2 p)
    {
        var (ix, iz) = _field.CellOf(p);
        var t = _field.FindCell(ix, iz, out int cell);
        return t == null ? 0 : t.Decomposed[cell];
    }

    /// Colony-side moisture (0..1) of the cell containing p; 0 if unallocated. Diagnostic only.
    public double MoistureAt(Vec2 p)
    {
        var (ix, iz) = _field.CellOf(p);
        var t = _field.FindCell(ix, iz, out int cell);
        return t == null ? 0 : Moisture(t, cell);
    }

    // ------------------------------------------------------------------ habitat and moisture

    private static byte Q8(double v) => (byte)Math.Floor(MathD.Clamp01(v) * 255);
    private static ushort QU(double v) => (ushort)Math.Min(65535, Math.Max(0, Math.Round(v)));

    private void RefreshCell(ColonyTile t, int cell, int ix, int iz)
    {
        var p = _field.CellCentre(ix, iz);
        t.SetFlag(cell, ColonyTile.FValid, true);
        double h = _sub.Height(p);
        int c = double.IsFinite(h) ? _sub.ColumnAt(p) : -1;
        if (c < 0)
        {
            t.SetFlag(cell, ColonyTile.FUsable, false);
            t.SetFlag(cell, ColonyTile.FSandy, false);
            return;
        }
        var m = _sub.SampleSubstrate(new Vec3(p.X, h - 1e-4, p.Z));
        t.SetFlag(cell, ColonyTile.FUsable, true);
        bool soil = m.Horizon == Horizon.MineralSoil || m.Horizon == Horizon.Weathered;
        t.SetFlag(cell, ColonyTile.FSandy, soil && m.Sand > 0.6f);
        t.Horizon[cell] = (byte)m.Horizon;
        t.CapacityQ[cell] = Q8(m.ColonizationCapacity);
        t.SlopeQ[cell] = Q8(_sub.ColumnSteepness(c));
        t.LightQ[cell] = Q8(_light(new Vec3(p.X, h + 0.01, p.Z)));
        t.MinY = Math.Min(t.MinY, h);
        t.MaxY = Math.Max(t.MaxY, h);
    }

    private void EnsureHabitat(ColonyTile t, int cell, int ix, int iz)
    {
        if (!t.Has(cell, ColonyTile.FValid)) RefreshCell(t, cell, ix, iz);
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
                if (!t.Has(cell, ColonyTile.FUsable)) continue;
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                var p = _field.CellCentre(ix, iz);
                double h = _sub.Height(p);
                t.LightQ[cell] = Q8(_light(new Vec3(p.X, h + 0.01, p.Z)));
            }
    }

    /// Moisture 0..1 at a cell. See the class notes for the rock and soil rules.
    private double Moisture(ColonyTile t, int cell)
    {
        ColonyField.CellCoords(t, cell, out int ix, out int iz);
        var p = _field.CellCentre(ix, iz);
        int col = _sub.ColumnAt(p);
        if (col < 0) return 0;
        var w = _sub.Water;
        double water = w.Film[col] + w.Standing[col];
        if (t.Horizon[cell] == (byte)Horizon.Bedrock)
        {
            double film = MathD.Clamp01(water / 0.0001);
            double seep = 0.7 * SoilSaturationNear(p);
            double shade = 0.5 * (1 - t.Light(cell));
            return MathD.Clamp01(film + seep + shade); // orchestrator 2026-11-13: additive (shaded rock beside damp soil is damp), was max()
        }
        double sat = w.TopSaturation(col);
        return MathD.Clamp01(sat + 0.7 * MathD.Clamp01(water / 0.0002));
    }

    /// Mean top-layer saturation of soil (non-outcrop) columns on a 5 x 5 grid spanning +-10 cm around p.
    private double SoilSaturationNear(Vec2 p)
    {
        double sum = 0; int n = 0;
        double step = SeepHalfWidth / 2;
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                int col = _sub.ColumnAt(new Vec2(p.X + i * step, p.Z + j * step));
                if (col < 0 || _sub.IsOutcrop(col)) continue;
                sum += _sub.Water.TopSaturation(col);
                n++;
            }
        return n > 0 ? sum / n : 0;
    }

    private double Suitability(int sid, ColonyTile t, int cell, double moist)
    {
        var sp = _species[sid - 1];
        if (!t.Has(cell, ColonyTile.FUsable)) return 0;
        double moistR = MathD.Preference(moist, sp.MoistureOptimum, sp.MoistureTolerance);
        double shade = MathD.Clamp01(1 - LitterThickness(t, cell) / LitterSuppressThickness);
        double lightR = MathD.Preference(t.Light(cell) * shade, sp.LightOptimum, sp.LightTolerance);
        double sub = sp.SubstrateResponse((Horizon)t.Horizon[cell], t.Has(cell, ColonyTile.FSandy), t.Capacity(cell));
        double slope = 1.0 / (1.0 + Math.Pow(t.Slope(cell) / Math.Max(1e-6, sp.SlopeTolerance), 2));
        return MathD.Clamp01(moistR * lightR * sub * slope);
    }

    // ------------------------------------------------------------------ occupancy helpers

    private static bool HasLive(ColonyTile t, int cell, int sid) =>
        (t.Species[cell * 2] == sid && t.Cover(cell * 2) > 1e-3) ||
        (t.Species[cell * 2 + 1] == sid && t.Cover(cell * 2 + 1) > 1e-3);

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

    private double CoverTotal(ColonyTile t, int cell) => t.Cover(cell * 2) + t.Cover(cell * 2 + 1);

    private double LitterThickness(ColonyTile t, int cell)
    {
        double sum = 0;
        for (int s = 0; s < 2; s++)
        {
            int k = cell * 2 + s, sid = t.Species[k];
            if (sid != 0 && _species[sid - 1].Kind == ColonyKind.Litter) sum += t.Thick(k);
        }
        return sum;
    }

    private double RetainedAt(ColonyTile t, int cell)
    {
        double sum = 0;
        for (int s = 0; s < 2; s++)
        {
            int k = cell * 2 + s, sid = t.Species[k];
            if (sid != 0) sum += t.Thick(k) * _species[sid - 1].WaterHolding * t.Cover(k);
        }
        return sum;
    }

    /// Writes coverage quantised by rounding; never lets the two occupants of a cell exceed 1 together.
    private static void SetCover(ColonyTile t, int k, double c)
    {
        int q = (int)Math.Round(MathD.Clamp01(c) * 65535);
        int oq = t.CoverQ[k ^ 1];
        if (q + oq > 65535) q = 65535 - oq;
        int old = t.CoverQ[k];
        t.CoverQ[k] = (ushort)q;
        t.ChangeSinceCheck += Math.Abs(q - old) / 65535.0;
    }

    private static void SetThick(ColonyTile t, int k, double metres) =>
        t.ThickUm[k] = (ushort)Math.Min(65535, Math.Max(0, Math.Round(metres * 1e6)));

    private void Clear(ColonyTile t, int cell, int slot)
    {
        int k = cell * 2 + slot;
        t.ChangeSinceCheck += t.Cover(k);
        t.Species[k] = 0;
        t.CoverQ[k] = 0; t.Biomass[k] = 0; t.ThickUm[k] = 0; t.HealthQ[k] = 0;
        t.AgeU[k] = 0; t.DryU[k] = 0; t.SuitQ[k] = 0;
        t.SetDormant(k, false);
        t.SetFlag(cell, ColonyTile.FSteady, false);
    }

    private void TouchCell(ColonyTile t, int cell)
    {
        ColonyField.CellCoords(t, cell, out int ix, out int iz);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                _check.Add(ColonyField.CellKey(ix + dx, iz + dz));
        t.SetFlag(cell, ColonyTile.FSteady, false);
    }

    private void AddLitter(ColonyTile t, int cell, int slot, double amount)
    {
        int k = cell * 2 + slot;
        if (t.Species[k] == 0)
        {
            t.Species[k] = (byte)_litterSid;
            t.HealthQ[k] = 255; t.AgeU[k] = 0; t.DryU[k] = 0;
            t.SetDormant(k, false);
        }
        t.Biomass[k] += amount;
        SetCover(t, k, Math.Min(1 - t.Cover(k ^ 1), 0.5 * t.Biomass[k]));
        SetThick(t, k, 0.01 * t.Biomass[k]);
        t.SetFlag(cell, ColonyTile.FSteady, false);
    }

    /// Moves biomass that left a live occupant into the cell's litter (internal), or to the dead pool (Out) if there is no room.
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

    /// Litter slot for a deposit; evicts the weakest live occupant when the cell is full (its mass becomes litter).
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
            double sc = _species[sid - 1].CompetitiveHeight * t.Health(k) * (t.SuitQ[k] / 255.0);
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
                if (t.Has(cell, ColonyTile.FSteady))
                {
                    if (sweep) Physiology(t, cell, dt * 8);   // steady cells: every 8th step with dt x 8
                }
                else Physiology(t, cell, dt);
            }
            int occ = 0, st = 0;
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (t.Species[cell * 2] == 0 && t.Species[cell * 2 + 1] == 0) continue;
                occ++;
                if (t.Has(cell, ColonyTile.FSteady)) st++;
            }
            t.Occupied = occ;
            t.SteadyCount = st;
        }
    }

    private void Physiology(ColonyTile t, int cell, double dt)
    {
        double dayF = dt / Day;
        double stepU = dt / UnitSeconds;
        double moist = Moisture(t, cell);
        bool anyLive = false, allSteady = true;
        for (int slot = 0; slot < 2; slot++)
        {
            int k = cell * 2 + slot, sid = t.Species[k];
            if (sid == 0) continue;
            var sp = _species[sid - 1];
            if (sp.Kind == ColonyKind.Litter) { DecomposeLitter(t, cell, slot, moist, dayF); continue; }

            double other = t.Cover(k ^ 1);
            double suit = Suitability(sid, t, cell, moist);
            double C = t.Cover(k), B = t.Biomass[k];
            double I = C > 1e-9 ? B / C : 0;

            // dryness (600 s units, saturating); dormancy beyond the desiccation tolerance
            double dryU = t.DryU[k];
            double newDry = moist < DryMoisture ? dryU + stepU : Math.Max(0, dryU - 2 * stepU);
            t.DryU[k] = QU(newDry);
            double tolU = sp.DesiccationToleranceDays * 86400.0 / UnitSeconds;
            bool dormant = t.DryU[k] > tolU;
            t.SetDormant(k, dormant);

            double gain = 0, loss = 0, Cn = C;
            if (dormant)
            {
                if (t.DryU[k] > 2 * tolU) loss += B * DieRate(sp) * dayF;
            }
            else
            {
                double target = MathD.Clamp01(TargetPerSuit * suit);
                double rate = (target >= C ? sp.GrowthRate : RetreatRate) * dayF;
                double dC = (target - C) * Math.Min(1, rate);
                Cn = C + dC;
                if (dC > 0) gain += dC * I;                  // newly covered ground takes donor intensity
                else loss += -dC * I;                        // coverage given up takes its mass with it

                double Bm = B + gain - loss;
                double Istar = sp.MaxBiomass * suit;
                double Ip = Cn > 1e-9 ? Bm / Cn : 0;
                if (Istar > 1e-12 && Ip < Istar) gain += sp.GrowthRate * Bm * (1 - Ip / Istar) * dayF;
                else if (Ip > Istar) loss += DecayRate(sp) * Math.Max(0, Bm - Cn * Istar) * dayF;
            }

            loss = Math.Min(loss, B + gain);
            double Bn = B + gain - loss;
            double massChange = Math.Abs(gain - loss);
            _inKg += gain * _cellArea;
            RouteToLitter(t, cell, loss);                    // live -> litter (same cell) or dead pool (Out)
            t.Biomass[k] = Bn;
            SetCover(t, k, Math.Min(1 - other, Cn));

            if (t.Cover(k) < MinCover)
            {
                double rem = t.Biomass[k];
                Clear(t, cell, slot);
                TouchCell(t, cell);
                RouteToLitter(t, cell, rem);
                continue;
            }

            double cov = t.Cover(k);
            double h = t.Health(k);
            double hTarget = dormant ? 0 : suit;
            t.HealthQ[k] = Q8(h + (hTarget - h) * Math.Min(1, 2 * dayF));
            SetThick(t, k, sp.MaxThickness * Math.Min(1, (Bn / cov) / sp.MaxBiomass));
            t.AgeU[k] = QU(t.AgeU[k] + stepU);

            bool steady = Math.Abs(suit - t.SuitQ[k] / 255.0) < 0.005
                       && massChange < 0.005 * Math.Max(Bn, 1e-9)
                       && Math.Abs(Cn - C) < 0.005 * Math.Max(C, 0.01);
            t.SuitQ[k] = Q8(suit);
            anyLive = true;
            allSteady &= steady;
        }
        t.SetFlag(cell, ColonyTile.FSteady, anyLive && allSteady && !t.Rim.ContainsKey(cell));
    }

    private void DecomposeLitter(ColonyTile t, int cell, int slot, double moist, double dayF)
    {
        int k = cell * 2 + slot;
        double B = t.Biomass[k];
        double kd = (0.005 + 0.05 * moist) * dayF;            // rises with moisture
        double loss = Math.Min(B, B * kd);
        double Bn = B - loss;
        double kg = loss * _cellArea;
        _decomposed += kg; t.Decomposed[cell] += (float)kg; _outKg += kg;
        if (Bn < MinMass)
        {
            double rem = Bn * _cellArea;
            _decomposed += rem; t.Decomposed[cell] += (float)rem; _outKg += rem;
            Clear(t, cell, slot);
            return;
        }
        t.Biomass[k] = Bn;
        SetCover(t, k, Math.Min(1 - t.Cover(k ^ 1), 0.5 * Bn));
        SetThick(t, k, 0.01 * Bn);
    }

    // ------------------------------------------------------------------ spread and competition

    /// Newcomer of species sid arrives with coverage dC at donor intensity. Returns mass placed (kg).
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
        if (fs >= 0)
        {
            double add = Math.Min(dC, free);
            return add > 0 ? Place(t, cell, fs, sid, add, intensity, suit) : 0;
        }
        // Full cell: the weakest live occupant loses coverage in proportion to the score difference.
        int weak = WeakestLive(t, cell);
        if (weak < 0) return 0;
        int kw = cell * 2 + weak;
        var wsp = _species[t.Species[kw] - 1];
        double newScore = sp.CompetitiveHeight * suit;
        double weakScore = wsp.CompetitiveHeight * t.Health(kw) * (t.SuitQ[kw] / 255.0);
        if (newScore <= weakScore) return 0;
        double rate = (newScore - weakScore) / newScore;
        double Cw = t.Cover(kw);
        double Iw = Cw > 1e-9 ? t.Biomass[kw] / Cw : 0;
        double take = Math.Min(Cw, dC * rate);
        if (take <= 0) return 0;
        double lost = take * Iw;
        SetCover(t, kw, Cw - take);
        t.Biomass[kw] = t.Cover(kw) * Iw;
        lost = (Cw - t.Cover(kw)) * Iw;                       // quantised removal actually taken
        TouchCell(t, cell);
        if (t.Cover(kw) < MinCover)
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
            t.Biomass[k] = 0; t.CoverQ[k] = 0; t.ThickUm[k] = 0;
            t.HealthQ[k] = Q8(0.5); t.AgeU[k] = 0; t.DryU[k] = 0;
            t.SetDormant(k, false);
            t.SuitQ[k] = Q8(suit);
        }
        double m = add * intensity;
        SetCover(t, k, t.Cover(k) + add);
        t.Biomass[k] += m;
        double cov = t.Cover(k);
        SetThick(t, k, sp.MaxThickness * Math.Min(1, (cov > 1e-9 ? t.Biomass[k] / cov : 0) / sp.MaxBiomass));
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
            var rims = t.Rim.Keys.ToArray();
            Array.Sort(rims);
            foreach (int cell in rims)
            {
                if (!t.Rim.TryGetValue(cell, out var acc)) continue;
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid == 0 || _species[sid - 1].Kind == ColonyKind.Litter) continue;
                    if (t.Cover(k) < SpreadDonorCover) continue;
                    var sp = _species[sid - 1];
                    double intensity = t.Biomass[k] / t.Cover(k);
                    for (int d = 0; d < Dirs.Length; d++)
                    {
                        var (dx, dz) = Dirs[d];
                        var nt = _field.GetOrCreateCell(ix + dx, iz + dz, out int ncell);
                        EnsureHabitat(nt, ncell, ix + dx, iz + dz);
                        if (!nt.Has(ncell, ColonyTile.FUsable)) continue;
                        double suitN = Suitability(sid, nt, ncell, Moisture(nt, ncell));
                        if (suitN <= 0) continue;
                        int idx = slot * 8 + d;
                        double jitter = 0.8 + 0.4 * ReprId.Unit(
                            ReprId.Hash(_seed, (ulong)ColonyField.CellKey(ix, iz), (ulong)idx), 0x7A11);
                        double spacing = _cell * (dx != 0 && dz != 0 ? Sqrt2 : 1.0) * jitter;
                        acc[idx] += sp.SpreadRate * suitN * dayF;
                        if (acc[idx] < spacing) continue;
                        acc[idx] -= spacing;
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
                if (t.Cover(cell * 2 + slot) <= 1e-3) continue;
                foreach (var (dx, dz) in Dirs)
                {
                    var nt = _field.FindCell(ix + dx, iz + dz, out int nc);
                    if (nt == null || !HasLive(nt, nc, sid)) { isRim = true; break; }
                }
            }
            if (isRim) { if (!t.Rim.ContainsKey(cell)) t.Rim[cell] = new double[16]; }
            else t.Rim.Remove(cell);
        }
        _check.Clear();
    }

    // ------------------------------------------------------------------ publishing and patches

    private void Publish()
    {
        foreach (var t in _field.Tiles)
        {
            if (t.ChangeSinceCheck < VisibleChange) continue;
            t.ChangeSinceCheck = 0;
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
            int k = cell * 2 + SlotOf(t, cell, sid);
            ids[i] = unchecked((ulong)key);
            cov[i] = (float)t.Cover(k); thk[i] = (float)t.Thick(k); bio[i] = (float)t.Biomass[k];
            hlt[i] = (float)t.Health(k); age[i] = (float)(t.AgeU[k] * UnitSeconds);
            area += t.Cover(k) * _cellArea;
            mass += t.Biomass[k] * _cellArea;
            version = Math.Max(version, t.Version);
            var p = _field.CellCentre(ix, iz);
            double h = _sub.Height(p);
            x0 = Math.Min(x0, p.X - _cell / 2); x1 = Math.Max(x1, p.X + _cell / 2);
            z0 = Math.Min(z0, p.Z - _cell / 2); z1 = Math.Max(z1, p.Z + _cell / 2);
            y0 = Math.Min(y0, h); y1 = Math.Max(y1, h);
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
