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
    public double GroundwaterInflow { get; set; }
    public double Evaporation { get; set; }
    public double Infiltration { get; set; }
    public double BoundaryOutflow { get; set; }
    /// <summary>Water poured in / soaked up with the player's water tools.</summary>
    public double ToolInflow { get; set; }
    public double ToolRemoval { get; set; }
}

/// <summary>
/// Shallow surface water on the environment grid, without CFD. Each cell holds a depth; water moves
/// toward lower neighbouring water surfaces (terrain + depth) by a relaxation flux, which is stable for
/// FlowRate &lt;= 0.24 and never drives depth negative. Cells below the fixed water table are kept topped up
/// from groundwater. Boundary cells exchange with a virtual exterior BoundaryDrop below the terrain edge,
/// so water leaves at the cut plane and is tallied, never reflected.
/// </summary>
public sealed class Hydrology
{
    public GridSpec Grid { get; }
    public WaterConfig Config { get; }
    /// <summary>Surface water depth per cell (m), ≥ 0. Out-of-domain cells are always 0.</summary>
    public double[] Depth { get; }
    /// <summary>Terrain height at each cell centre (derived from the heightfield; not saved).</summary>
    public double[] Bed { get; }
    /// <summary>Net horizontal flow per cell (m³/s along +X/+Z) for visuals and overlays. Derived.</summary>
    public double[] FlowX { get; }
    public double[] FlowZ { get; }
    public List<Spring> Springs { get; set; } = new();
    public WaterBudget Budget { get; set; } = new();

    private readonly double[] _out;      // outgoing volume per cell per substep
    private readonly double[] _fluxE, _fluxW, _fluxN, _fluxS; // per-direction outgoing (m depth-equivalent)
    private readonly double[] _edgeHeight; // virtual exterior surface for boundary cells
    private readonly int[] _nE, _nW, _nN, _nS; // neighbour cell index or -1 when outside the domain

    public double CellArea => Grid.CellSize * Grid.CellSize;

    public Hydrology(GridSpec grid, WaterConfig config, Heightfield hf)
    {
        Grid = grid; Config = config;
        int n = grid.Count;
        Depth = new double[n]; Bed = new double[n]; FlowX = new double[n]; FlowZ = new double[n];
        _out = new double[n]; _fluxE = new double[n]; _fluxW = new double[n]; _fluxN = new double[n]; _fluxS = new double[n];
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
                // Groundwater at or below the fixed table is held by the saturated ground beyond the cut;
                // only surface water above it leaves the specimen.
                _edgeHeight[idx] = Math.Max(hf.Height(edge) - config.BoundaryDrop, config.WaterTable);
            }
        }
    }

    public double WaterTable => Config.WaterTable;

    /// <summary>Pours <paramref name="volume"/> m³ over a disc (smooth falloff). Returns the volume added.</summary>
    public double AddWater(Vec2 centre, double radius, double volume)
    {
        var cells = WeightsInDisc(centre, radius, out double total);
        if (total <= 0 || volume <= 0) return 0;
        foreach (var (c, wgt) in cells) Depth[c] += volume * wgt / total / CellArea;
        Budget.ToolInflow += volume;
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

    public double DepthAt(Vec2 p) { int c = Grid.CellAt(p); return c >= 0 && Grid.InDomain(c) ? Depth[c] : 0; }
    /// <summary>Water surface elevation at p, or NaN where the ground is dry.</summary>
    public double SurfaceAt(Vec2 p)
    {
        int c = Grid.CellAt(p);
        if (c < 0 || !Grid.InDomain(c) || Depth[c] < Config.WetDepth) return double.NaN;
        return Bed[c] + Depth[c];
    }
    public bool IsWet(int idx) => Grid.InDomain(idx) && Depth[idx] >= Config.WetDepth;
    public bool IsWet(Vec2 p) => DepthAt(p) >= Config.WetDepth;

    public double Volume() { double v = 0; foreach (int idx in Grid.DomainCells) v += Depth[idx]; return v * CellArea; }

    /// <summary>Initial state: fill cells below the water table (the pond baseline).</summary>
    public void InitializeFromWaterTable()
    {
        foreach (int idx in Grid.DomainCells)
        {
            double need = WaterTable - Bed[idx];
            if (need > Depth[idx]) { Budget.GroundwaterInflow += (need - Depth[idx]) * CellArea; Depth[idx] = need; }
        }
    }

    public void Step(double dt)
    {
        int sub = Math.Max(1, Config.SubSteps);
        double h = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(h);
    }

    private void SubStep(double dt)
    {
        double area = CellArea;
        // 1. sources: springs
        foreach (var sp in Springs)
        {
            int c = Grid.NearestDomainCell(sp.Position);
            if (c < 0) continue;
            double vol = sp.Discharge * dt;
            Depth[c] += vol / area;
            Budget.SpringInflow += vol;
        }
        // 2. groundwater top-up below the table; evaporation/infiltration losses elsewhere
        double evap = Config.Evaporation / SimUnits.Day * dt, infil = Config.Infiltration / SimUnits.Day * dt;
        foreach (int idx in Grid.DomainCells)
        {
            double need = WaterTable - Bed[idx];
            if (need > 0)
            {
                if (Depth[idx] < need) { Budget.GroundwaterInflow += (need - Depth[idx]) * area; Depth[idx] = need; }
                continue; // standing groundwater: table maintains level; surplus above table evaporates below
            }
            if (Depth[idx] <= 0) continue;
            double loss = Math.Min(Depth[idx], evap + infil);
            double le = loss * (evap / Math.Max(evap + infil, 1e-18));
            Budget.Evaporation += le * area;
            Budget.Infiltration += (loss - le) * area;
            Depth[idx] -= loss;
        }
        // surplus above the table on groundwater cells evaporates at the same rate
        foreach (int idx in Grid.DomainCells)
        {
            double need = WaterTable - Bed[idx];
            if (need <= 0) continue;
            double surplus = Depth[idx] - need;
            if (surplus <= 0) continue;
            double loss = Math.Min(surplus, evap);
            Budget.Evaporation += loss * area;
            Depth[idx] -= loss;
        }

        // 3. relaxation flow (Jacobi: all fluxes from the pre-step state)
        double k = MathD.Clamp(Config.FlowRate, 0, 0.24);
        foreach (int idx in Grid.DomainCells)
        {
            _fluxE[idx] = _fluxW[idx] = _fluxN[idx] = _fluxS[idx] = 0; _out[idx] = 0;
            double d = Depth[idx];
            if (d <= 1e-12) continue;
            double surf = Bed[idx] + d;
            _fluxE[idx] = Flux(surf, d, _nE[idx], idx, k);
            _fluxW[idx] = Flux(surf, d, _nW[idx], idx, k);
            _fluxN[idx] = Flux(surf, d, _nN[idx], idx, k);
            _fluxS[idx] = Flux(surf, d, _nS[idx], idx, k);
            double total = _fluxE[idx] + _fluxW[idx] + _fluxN[idx] + _fluxS[idx];
            if (total > d) { double sc = d / total; _fluxE[idx] *= sc; _fluxW[idx] *= sc; _fluxN[idx] *= sc; _fluxS[idx] *= sc; total = d; }
            _out[idx] = total;
        }
        double toFlow = CellArea / dt;
        foreach (int idx in Grid.DomainCells)
        {
            int e = _nE[idx], w = _nW[idx], n = _nN[idx], so = _nS[idx];
            double inW = w >= 0 ? _fluxE[w] : 0, inE = e >= 0 ? _fluxW[e] : 0, inS = so >= 0 ? _fluxN[so] : 0, inN = n >= 0 ? _fluxS[n] : 0;
            double exterior = (e < 0 ? _fluxE[idx] : 0) + (w < 0 ? _fluxW[idx] : 0) + (n < 0 ? _fluxN[idx] : 0) + (so < 0 ? _fluxS[idx] : 0);
            if (exterior > 0) Budget.BoundaryOutflow += exterior * area;
            // net flow through the cell (average of in- and outgoing faces), m³/s
            FlowX[idx] = ((_fluxE[idx] - _fluxW[idx]) + (inW - inE)) * 0.5 * toFlow;
            FlowZ[idx] = ((_fluxN[idx] - _fluxS[idx]) + (inS - inN)) * 0.5 * toFlow;
            Depth[idx] = Math.Max(0, Depth[idx] - _out[idx] + inW + inE + inS + inN);
        }
    }

    private double Flux(double surf, double depth, int n, int idx, double k)
    {
        double nsurf;
        if (n >= 0) nsurf = Bed[n] + Depth[n];
        else if (Grid.IsBoundaryCell[idx]) nsurf = _edgeHeight[idx];
        else return 0;
        double diff = surf - nsurf;
        if (diff <= 0) return 0;
        return k * Math.Min(depth, diff);
    }

    private double[]? _waterDistance;

    /// <summary>
    /// Distance (m) from each domain cell to the nearest wet cell: a two-pass chamfer transform (3-4 weights),
    /// deterministic and O(cells). Cells with no water anywhere get +∞.
    /// </summary>
    public double[] DistanceToWater()
    {
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

    public bool AllFinite() { foreach (int idx in Grid.DomainCells) if (!double.IsFinite(Depth[idx]) || Depth[idx] < 0) return false; return true; }

    public string DigestHex()
    {
        using var d = new DigestBuilder();
        foreach (int idx in Grid.DomainCells) d.Add(Depth[idx]);
        foreach (var s in Springs) d.Add(s.Id.Value).Add(s.X).Add(s.Z).Add(s.Discharge);
        d.Add(Budget.SpringInflow).Add(Budget.GroundwaterInflow).Add(Budget.Evaporation).Add(Budget.Infiltration).Add(Budget.BoundaryOutflow).Add(Budget.ToolInflow).Add(Budget.ToolRemoval);
        return d.Hex();
    }
}
