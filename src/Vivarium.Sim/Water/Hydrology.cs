using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Water;

public sealed class Spring
{
    public EntityId Id { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>Cubic metres per simulated second.</summary>
    public double Discharge { get; set; }
    public Vec2 Position => new(X, Z);
}

/// <summary>Cumulative water budget (m³). Conservation: volume = inflows − outflows − losses.</summary>
public sealed class WaterBudget
{
    public double SpringInflow { get; set; }
    /// <summary>Legacy accounting field retained for save compatibility; implicit groundwater is not created as surface volume.</summary>
    public double GroundwaterInflow { get; set; }
    /// <summary>Dynamic surface water transferred into the hydrostatic groundwater reservoir.</summary>
    public double GroundwaterRecharge { get; set; }
    public double Evaporation { get; set; }
    public double Infiltration { get; set; }
    public double BoundaryOutflow { get; set; }
    /// <summary>Water poured in / soaked up with the player's water tools.</summary>
    public double ToolInflow { get; set; }
    public double ToolRemoval { get; set; }
}

/// <summary>
/// Dynamic shallow surface water on the environment grid. Groundwater is a separate hydrostatic boundary:
/// cells whose terrain lies below WaterTable expose groundwater without storing it in <see cref="Depth"/>.
/// Depth therefore contains only mobile surface-water volume. The temporary relaxation transport is retained
/// during the state-model migration; a local-inertial shallow-water solver replaces it in the next stage.
/// </summary>
public sealed class Hydrology
{
    public GridSpec Grid { get; }
    public WaterConfig Config { get; }
    /// <summary>Dynamic surface-water depth per cell (m), ≥ 0. Groundwater is never stored here.</summary>
    public double[] Depth { get; }
    /// <summary>Terrain height at each cell centre (derived from the heightfield; not saved).</summary>
    public double[] Bed { get; }
    /// <summary>Net horizontal flow per cell (m³/s along +X/+Z) for visuals and overlays. Derived.</summary>
    public double[] FlowX { get; }
    public double[] FlowZ { get; }
    public List<Spring> Springs { get; set; } = new();
    public WaterBudget Budget { get; set; } = new();

    // Signed internal face discharge (m³/s): +E and +N. West/south faces are the neighbour's east/north face.
    private readonly double[] _faceE, _faceN;
    private readonly double[] _deltaVolume, _outScale;
    private readonly double[] _edgeFlowE, _edgeFlowW, _edgeFlowN, _edgeFlowS;
    private readonly double[] _edgeHeight; // virtual exterior surface for boundary cells
    private readonly int[] _nE, _nW, _nN, _nS; // neighbour cell index or -1 when outside the domain

    public double CellArea => Grid.CellSize * Grid.CellSize;
    /// <summary>Signed discharge through each cell's east face (+X), m³/s. Authoritative surface-water momentum.</summary>
    public double[] FaceFlowEast => _faceE;
    /// <summary>Signed discharge through each cell's north face (+Z), m³/s. Authoritative surface-water momentum.</summary>
    public double[] FaceFlowNorth => _faceN;

    public Hydrology(GridSpec grid, WaterConfig config, Heightfield hf)
    {
        Grid = grid; Config = config;
        int n = grid.Count;
        Depth = new double[n]; Bed = new double[n]; FlowX = new double[n]; FlowZ = new double[n];
        _faceE = new double[n]; _faceN = new double[n];
        _deltaVolume = new double[n]; _outScale = new double[n];
        _edgeFlowE = new double[n]; _edgeFlowW = new double[n]; _edgeFlowN = new double[n]; _edgeFlowS = new double[n];
        _edgeHeight = new double[n];
        _nE = new int[n]; _nW = new int[n]; _nN = new int[n]; _nS = new int[n];
        foreach (int idx in grid.DomainCells)
        {
            int ci = idx % grid.Nx, cj = idx / grid.Nx;
            _nE[idx] = grid.InDomain(ci + 1, cj) ? idx + 1 : -1;
            _nW[idx] = grid.InDomain(ci - 1, cj) ? idx - 1 : -1;
            _nN[idx] = grid.InDomain(ci, cj + 1) ? idx + grid.Nx : -1;
            _nS[idx] = grid.InDomain(ci, cj - 1) ? idx - grid.Nx : -1;
        }
        RefreshBed(hf);
    }

    /// <summary>Elevation that dynamic surface water sits on. Exposed groundwater acts as a fixed hydraulic floor.</summary>
    private double HydraulicBed(int idx) => Math.Max(Bed[idx], WaterTable);

    /// <summary>Re-derives bed heights and the exterior boundary level after the terrain changes.</summary>
    public void RefreshBed(Heightfield hf)
    {
        var grid = Grid; var config = Config;
        foreach (int idx in grid.DomainCells)
        {
            var c = grid.CellCenter(idx);
            Bed[idx] = hf.Height(c);
            if (grid.IsBoundaryCell[idx])
            {
                var edge = grid.Domain.NearestBoundaryPoint(c);
                _edgeHeight[idx] = Math.Max(hf.Height(edge) - config.BoundaryDrop, config.WaterTable);
            }
        }
        InvalidateWaterDistance();
    }

    public double WaterTable => Config.WaterTable;

    /// <summary>Pours <paramref name="volume"/> m³ over a disc (smooth falloff). Returns the volume added.</summary>
    public double AddWater(Vec2 centre, double radius, double volume)
    {
        var cells = WeightsInDisc(centre, radius, out double total);
        if (total <= 0 || volume <= 0) return 0;
        foreach (var (c, wgt) in cells) Depth[c] += volume * wgt / total / CellArea;
        Budget.ToolInflow += volume;
        InvalidateWaterDistance();
        return volume;
    }

    /// <summary>Soaks up to <paramref name="volume"/> m³ of surface water from a disc. Returns the volume removed.</summary>
    public double RemoveWater(Vec2 centre, double radius, double volume)
    {
        var cells = WeightsInDisc(centre, radius, out double total);
        if (total <= 0 || volume <= 0) return 0;
        double removed = 0;
        foreach (var (c, wgt) in cells)
        {
            double take = Math.Min(Depth[c], volume * wgt / total / CellArea);
            Depth[c] -= take;
            removed += take * CellArea;
        }
        Budget.ToolRemoval += removed;
        InvalidateWaterDistance();
        return removed;
    }

    private List<(int Cell, double Weight)> WeightsInDisc(Vec2 centre, double radius, out double total)
    {
        var list = new List<(int, double)>();
        total = 0;
        foreach (int c in Grid.CellsInRadius(centre, radius))
        {
            double d = Vec2.Distance(Grid.CellCenter(c), centre) / radius;
            double wgt = (1 - d * d) + 1e-3;
            list.Add((c, wgt));
            total += wgt;
        }
        return list;
    }

    /// <summary>Dynamic surface-water depth only; groundwater is excluded.</summary>
    public double DepthAt(Vec2 p) { int c = Grid.CellAt(p); return c >= 0 && Grid.InDomain(c) ? Depth[c] : 0; }
    public double SurfaceWaterDepth(int idx) => Grid.InDomain(idx) ? Depth[idx] : 0;
    public double SurfaceWaterDepth(Vec2 p) => DepthAt(p);
    public bool HasSurfaceWater(int idx) => Grid.InDomain(idx) && Depth[idx] >= Config.WetDepth;
    public bool HasSurfaceWater(Vec2 p) => DepthAt(p) >= Config.WetDepth;

    /// <summary>Total visible open-water depth: hydrostatic groundwater exposure plus dynamic water above it.</summary>
    public double OpenWaterDepth(int idx) => Grid.InDomain(idx) ? WaterTableDepth(idx) + Depth[idx] : 0;
    public double OpenWaterDepth(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) ? OpenWaterDepth(c) : 0;
    }

    /// <summary>Visible water-surface elevation, or NaN where neither groundwater nor dynamic surface water is exposed.</summary>
    public double SurfaceAt(Vec2 p)
    {
        int c = Grid.CellAt(p);
        if (c < 0 || !Grid.InDomain(c) || (!IsWaterTable(c) && !HasSurfaceWater(c))) return double.NaN;
        return HydraulicBed(c) + Depth[c];
    }

    /// <summary>Open water from either exposed groundwater or dynamic surface water.</summary>
    public bool IsWet(int idx) => Grid.InDomain(idx) && (IsWaterTable(idx) || HasSurfaceWater(idx));
    public bool IsWet(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && IsWet(c);
    }

    /// <summary>True if the cell or position is part of the hydrostatic water table (ground below water table).</summary>
    public bool IsWaterTable(int idx) => Grid.InDomain(idx) && Bed[idx] < WaterTable;
    public bool IsWaterTable(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) && Bed[c] < WaterTable;
    }

    /// <summary>Depth of the hydrostatic water table above the terrain bed (0 if ground is above the water table).</summary>
    public double WaterTableDepth(int idx) => Grid.InDomain(idx) ? Math.Max(0.0, WaterTable - Bed[idx]) : 0;
    public double WaterTableDepth(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) ? Math.Max(0.0, WaterTable - Bed[c]) : 0;
    }

    /// <summary>True if the cell or position has active surface stream / spring runoff above the water table.</summary>
    public bool IsStream(int idx) => Grid.InDomain(idx) && Depth[idx] >= Config.WetDepth && Bed[idx] >= WaterTable - 0.02;
    public bool IsStream(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) && Depth[c] >= Config.WetDepth && Bed[c] >= WaterTable - 0.02;
    }

    /// <summary>Dynamic surface-water depth. Stream classification remains a convenience query.</summary>
    public double StreamDepth(int idx) => SurfaceWaterDepth(idx);
    public double StreamDepth(Vec2 p) => SurfaceWaterDepth(p);

    /// <summary>Flow velocity vector (m/s) in world XZ for a cell.</summary>
    public Vec2 StreamVelocity(int idx)
    {
        if (!Grid.InDomain(idx) || Depth[idx] < Config.WetDepth) return Vec2.Zero;
        double area = Math.Max(Depth[idx] * Grid.CellSize, 1e-6);
        return new Vec2(FlowX[idx] / area, FlowZ[idx] / area);
    }
    public Vec2 StreamVelocity(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 ? StreamVelocity(c) : Vec2.Zero;
    }

    /// <summary>Dynamic surface-water volume only. The implicit groundwater reservoir is intentionally not counted.</summary>
    public double Volume() { double v = 0; foreach (int idx in Grid.DomainCells) v += Depth[idx]; return v * CellArea; }
    public double SurfaceVolume() => Volume();

    /// <summary>Groundwater is implicit, so initialization no longer materializes it into dynamic surface storage.</summary>
    public void InitializeFromWaterTable() => InvalidateWaterDistance();

    public void Step(double dt)
    {
        int sub = Math.Max(1, Config.SubSteps);
        double h = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(h);
        InvalidateWaterDistance();
    }

    private void SubStep(double dt)
    {
        double area = CellArea;
        // 1. sources: springs inject dynamic surface water.
        foreach (var sp in Springs)
        {
            int c = Grid.NearestDomainCell(sp.Position);
            if (c < 0) continue;
            double vol = sp.Discharge * dt;
            Depth[c] += vol / area;
            Budget.SpringInflow += vol;
        }

        // 2. losses from dynamic surface water only. Groundwater is a separate implicit reservoir.
        double evap = Config.Evaporation / SimUnits.Day * dt, infil = Config.Infiltration / SimUnits.Day * dt;
        foreach (int idx in Grid.DomainCells)
        {
            if (Depth[idx] <= 0) continue;
            double loss = Math.Min(Depth[idx], evap + infil);
            double le = loss * (evap / Math.Max(evap + infil, 1e-18));
            Budget.Evaporation += le * area;
            Budget.Infiltration += (loss - le) * area;
            Depth[idx] -= loss;
        }

        // 3. Update signed face discharge from free-surface head. The previous discharge is retained
        // with exponential decay, providing local inertia; the hydraulic target is deliberately bounded
        // so one coarse hydrology tick cannot create an unstable Courant jump.
        double memory = Math.Exp(-dt / 45.0);
        foreach (int idx in Grid.DomainCells)
        {
            int e = _nE[idx], n = _nN[idx];
            _faceE[idx] = e >= 0 ? UpdateFaceDischarge(idx, e, _faceE[idx], memory) : 0;
            _faceN[idx] = n >= 0 ? UpdateFaceDischarge(idx, n, _faceN[idx], memory) : 0;
            _deltaVolume[idx] = 0;
            _outScale[idx] = 1;
            _edgeFlowE[idx] = _edgeFlowW[idx] = _edgeFlowN[idx] = _edgeFlowS[idx] = 0;
        }

        // Boundary discharge is stateless: the specimen can drain out, but the virtual exterior never injects water.
        foreach (int idx in Grid.DomainCells)
        {
            if (!Grid.IsBoundaryCell[idx] || Depth[idx] <= 0) continue;
            double surface = HydraulicBed(idx) + Depth[idx];
            double target = BoundaryDischarge(idx, surface);
            int i = idx % Grid.Nx, j = idx / Grid.Nx;
            if (_nE[idx] < 0) _edgeFlowE[idx] = target;
            if (_nW[idx] < 0) _edgeFlowW[idx] = target;
            if (_nN[idx] < 0) _edgeFlowN[idx] = target;
            if (_nS[idx] < 0) _edgeFlowS[idx] = target;
        }

        // 4. Per-cell outflow limiter. Every internal face has exactly one upstream cell according to its sign.
        foreach (int idx in Grid.DomainCells)
        {
            double outgoing = _edgeFlowE[idx] + _edgeFlowW[idx] + _edgeFlowN[idx] + _edgeFlowS[idx];
            int e = _nE[idx], w = _nW[idx], n = _nN[idx], so = _nS[idx];
            if (e >= 0 && _faceE[idx] > 0) outgoing += _faceE[idx];
            if (w >= 0 && _faceE[w] < 0) outgoing += -_faceE[w];
            if (n >= 0 && _faceN[idx] > 0) outgoing += _faceN[idx];
            if (so >= 0 && _faceN[so] < 0) outgoing += -_faceN[so];
            double requested = outgoing * dt;
            double available = Depth[idx] * area;
            if (requested > available && requested > 0) _outScale[idx] = available / requested;
        }

        // Scale each shared face by its upstream cell, then transfer the exact same volume out/in.
        foreach (int idx in Grid.DomainCells)
        {
            int e = _nE[idx], n = _nN[idx];
            if (e >= 0)
            {
                double q = _faceE[idx];
                q *= q >= 0 ? _outScale[idx] : _outScale[e];
                _faceE[idx] = q;
                double vol = q * dt;
                _deltaVolume[idx] -= vol;
                _deltaVolume[e] += vol;
            }
            if (n >= 0)
            {
                double q = _faceN[idx];
                q *= q >= 0 ? _outScale[idx] : _outScale[n];
                _faceN[idx] = q;
                double vol = q * dt;
                _deltaVolume[idx] -= vol;
                _deltaVolume[n] += vol;
            }
        }

        foreach (int idx in Grid.DomainCells)
        {
            double exteriorRate = (_edgeFlowE[idx] + _edgeFlowW[idx] + _edgeFlowN[idx] + _edgeFlowS[idx]) * _outScale[idx];
            if (exteriorRate > 0)
            {
                double vol = exteriorRate * dt;
                _deltaVolume[idx] -= vol;
                Budget.BoundaryOutflow += vol;
            }
        }

        // 5. Commit depths and expose cell-centred flow for rendering/ecology.
        foreach (int idx in Grid.DomainCells)
        {
            Depth[idx] = Math.Max(0, Depth[idx] + _deltaVolume[idx] / area);
            int w = _nW[idx], so = _nS[idx];
            double east = _nE[idx] >= 0 ? _faceE[idx] : _edgeFlowE[idx] * _outScale[idx];
            double west = w >= 0 ? _faceE[w] : -_edgeFlowW[idx] * _outScale[idx];
            double north = _nN[idx] >= 0 ? _faceN[idx] : _edgeFlowN[idx] * _outScale[idx];
            double south = so >= 0 ? _faceN[so] : -_edgeFlowS[idx] * _outScale[idx];
            FlowX[idx] = 0.5 * (east + west);
            FlowZ[idx] = 0.5 * (north + south);
        }
    }

    private double UpdateFaceDischarge(int a, int b, double previous, double memory)
    {
        double sa = HydraulicBed(a) + Depth[a], sb = HydraulicBed(b) + Depth[b];
        double head = sa - sb;
        if (Math.Abs(head) < 1e-12 && Math.Abs(previous) < 1e-15) return 0;

        int upstream = head >= 0 ? a : b;
        double mobileDepth = Depth[upstream];
        double target = 0;
        if (mobileDepth > 1e-12 && Math.Abs(head) > 1e-12)
        {
            // FlowRate remains the bounded transport fraction for compatibility. The 30 s response scale
            // converts the old per-step fraction into a discharge rate while making the stored momentum
            // independent of the caller's current dt.
            double transferableDepth = Math.Min(mobileDepth, Math.Abs(head));
            target = Math.Sign(head) * Config.FlowRate * transferableDepth * CellArea / 30.0;
        }

        double q = previous * memory + target * (1 - memory);
        // Never allow retained momentum to pull water out of a completely dry upstream cell.
        int qUpstream = q >= 0 ? a : b;
        if (Depth[qUpstream] <= 1e-12) return 0;
        return q;
    }

    private double BoundaryDischarge(int idx, double surface)
    {
        double head = surface - _edgeHeight[idx];
        if (head <= 0 || Depth[idx] <= 0) return 0;
        double transferableDepth = Math.Min(Depth[idx], head);
        return Config.FlowRate * transferableDepth * CellArea / 30.0;
    }

    private double[]? _waterDistance;
    private long _lastDepthHash = -1;

    public void InvalidateWaterDistance() => _lastDepthHash = -1;

    private long ComputeDepthHash()
    {
        long hash = 17;
        foreach (int c in Grid.DomainCells)
        {
            hash = unchecked(hash * 31 + BitConverter.DoubleToInt64Bits(Depth[c]));
        }
        return hash;
    }

    /// <summary>
    /// Distance (m) from each domain cell to the nearest wet cell: a two-pass chamfer transform (3-4 weights),
    /// deterministic and O(cells). Cells with no water anywhere get +∞.
    /// </summary>
    public double[] DistanceToWater()
    {
        long hash = ComputeDepthHash();
        if (_waterDistance != null && hash == _lastDepthHash) return _waterDistance;
        int nx = Grid.Nx, nz = Grid.Nz, n = Grid.Count;
        var d = _waterDistance ??= new double[n];
        const double Inf = double.PositiveInfinity;
        for (int k = 0; k < n; k++) d[k] = Grid.InDomain(k) && IsWet(k) ? 0 : Inf;
        double a = Grid.CellSize, b = Grid.CellSize * Math.Sqrt(2);
        void Relax(int k, int i, int j, int di, int dj, double w)
        {
            int ii = i + di, jj = j + dj;
            if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) return;
            double c = d[jj * nx + ii] + w;
            if (c < d[k]) d[k] = c;
        }
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int k = j * nx + i;
                Relax(k, i, j, -1, 0, a); Relax(k, i, j, 0, -1, a); Relax(k, i, j, -1, -1, b); Relax(k, i, j, 1, -1, b);
            }
        for (int j = nz - 1; j >= 0; j--)
            for (int i = nx - 1; i >= 0; i--)
            {
                int k = j * nx + i;
                Relax(k, i, j, 1, 0, a); Relax(k, i, j, 0, 1, a); Relax(k, i, j, 1, 1, b); Relax(k, i, j, -1, 1, b);
            }
        _lastDepthHash = hash;
        return d;
    }

    /// <summary>
    /// Soil moisture coupling: wet cells saturate; nearby soil wicks water sideways with a smooth fall-off by
    /// distance to open water (capillary fringe); elsewhere ground dries toward the level implied by its
    /// height above the water table.
    /// </summary>
    public void CoupleMoisture(ScalarField moisture, EcologyConfig eco, double dt, double[] scratch)
    {
        double wet = 1 - Math.Exp(-eco.MoistureWetting * dt);
        double dry = 1 - Math.Exp(-eco.MoistureDrying * dt);
        var dist = DistanceToWater();
        foreach (int idx in Grid.DomainCells)
        {
            double m = moisture.Values[idx];
            double target;
            if (IsWet(idx)) target = 1;
            else
            {
                double capillary = MathD.Clamp01(1 - (Bed[idx] - WaterTable) / eco.MoistureWaterTableRange);
                double baseline = Math.Max(eco.MoistureDryBaseline, 0.85 * capillary * capillary);
                double wick = double.IsInfinity(dist[idx]) ? 0 : 0.95 * Math.Exp(-(dist[idx] - Grid.CellSize) / eco.MoistureCapillaryRange);
                target = Math.Max(baseline, Math.Min(0.95, wick));
            }
            double rate = target > m ? wet : dry;
            moisture[idx] = m + (target - m) * rate;
        }
        moisture.Diffuse(eco.MoistureDiffusion * dt, scratch);
    }

    public bool AllFinite()
    {
        foreach (int idx in Grid.DomainCells)
            if (!double.IsFinite(Depth[idx]) || Depth[idx] < 0 || !double.IsFinite(_faceE[idx]) || !double.IsFinite(_faceN[idx])) return false;
        return true;
    }

    public string DigestHex()
    {
        using var d = new DigestBuilder();
        foreach (int idx in Grid.DomainCells) d.Add(Depth[idx]).Add(_faceE[idx]).Add(_faceN[idx]);
        foreach (var s in Springs) d.Add(s.Id.Value).Add(s.X).Add(s.Z).Add(s.Discharge);
        d.Add(Budget.SpringInflow).Add(Budget.GroundwaterInflow).Add(Budget.GroundwaterRecharge).Add(Budget.Evaporation).Add(Budget.Infiltration).Add(Budget.BoundaryOutflow).Add(Budget.ToolInflow).Add(Budget.ToolRemoval);
        return d.Hex();
    }
}
