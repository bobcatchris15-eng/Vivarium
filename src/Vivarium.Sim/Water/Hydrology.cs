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

/// <summary>Cumulative water budget (mÂ³). Conservation: volume = inflows âˆ’ outflows âˆ’ losses.</summary>
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
/// Standing-water configuration and ecological queries. Mobile liquid belongs exclusively to
/// SurfaceLiquid. Depth/current arrays are derived habitat samples and legacy save import buffers.
/// </summary>
public sealed class Hydrology
{
    /// <summary>When attached by the game, these arrays are ecology samples only. 3D liquid owns volume.</summary>
    public SurfaceLiquid? Liquid {get;internal set;}
    public GridSpec Grid { get; }
    public WaterConfig Config { get; }
    /// <summary>Dynamic surface-water depth per cell (m), â‰¥ 0. Groundwater is never stored here.</summary>
    public double[] Depth { get; }
    /// <summary>Terrain height at each cell centre (derived from the heightfield; not saved).</summary>
    public double[] Bed { get; }
    /// <summary>Net horizontal flow per cell (mÂ³/s along +X/+Z) for visuals and overlays. Derived.</summary>
    public double[] FlowX { get; }
    public double[] FlowZ { get; }
    public List<Spring> Springs { get; set; } = new();
    public WaterBudget Budget { get; set; } = new();

    private Heightfield _terrain;
    // Historical face arrays are import/derived compatibility data, never advancing mobile state.
    private readonly double[] _faceE,_faceN;
    public double CellArea => Grid.CellSize*Grid.CellSize;
    public double[] FaceFlowEast=>_faceE;
    public double[] FaceFlowNorth=>_faceN;
    public Hydrology(GridSpec grid,WaterConfig config,Heightfield hf)
    {
        Grid=grid;Config=config;_terrain=hf;int n=grid.Count;
        Depth=new double[n];Bed=new double[n];FlowX=new double[n];FlowZ=new double[n];
        _faceE=new double[n];_faceN=new double[n];RefreshBed(hf);
    }

    /// <summary>Terrain bed elevation supporting the fluid.</summary>
    private double HydraulicBed(int idx) => Bed[idx];

    /// <summary>Re-derives bed heights and the exterior boundary level after the terrain changes.</summary>
    public void RefreshBed(Heightfield hf)
    {
        _terrain = hf;
        var grid = Grid; var config = Config;
        foreach (int idx in grid.DomainCells)
        {
            var c = grid.CellCenter(idx);
            Bed[idx] = hf.Height(c);
        }
        InvalidateWaterDistance();
    }

    public double WaterTable => Config.WaterTable;

    /// <summary>Pours <paramref name="volume"/> mÂ³ over a disc (smooth falloff). Returns the volume added.</summary>
    public double AddWater(Vec2 centre, double radius, double volume)
    {
        if(Liquid!=null)return Liquid.Add(centre,radius,volume);
        var cells = WeightsInDisc(centre, radius, out double total);
        if (total <= 0 || volume <= 0) return 0;
        foreach (var (c, wgt) in cells) Depth[c] += volume * wgt / total / CellArea;
        Budget.ToolInflow += volume;
        InvalidateWaterDistance();
        return volume;
    }

    /// <summary>Soaks up to <paramref name="volume"/> mÂ³ of surface water from a disc. Returns the volume removed.</summary>
    public double RemoveWater(Vec2 centre, double radius, double volume)
    {
        if(Liquid!=null)return Liquid.Remove(centre,radius,volume);
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
    public double DepthAt(Vec2 p) { if(Liquid!=null)return Liquid.DepthAt(p);int c = Grid.CellAt(p); return c >= 0 && Grid.InDomain(c) ? Depth[c] : 0; }
    public double SurfaceWaterDepth(int idx) => Grid.InDomain(idx) ? Depth[idx] : 0;
    public double SurfaceWaterDepth(Vec2 p)
    {
        if(Liquid!=null)return Liquid.DepthAt(p);
        int c = Grid.CellAt(p);
        if (c < 0 || !Grid.InDomain(c) || Depth[c] <= 0) return 0;
        return Math.Max(0.0, (HydraulicBed(c) + Depth[c]) - _terrain.Height(p));
    }
    public bool HasSurfaceWater(int idx) => Grid.InDomain(idx) && Depth[idx] >= 0.00005;
    public bool HasSurfaceWater(Vec2 p) => SurfaceWaterDepth(p) >= 0.00005;

    /// <summary>Open-water depth from mobile liquid or the preserved standing table.</summary>
    public double OpenWaterDepth(int idx) => Grid.InDomain(idx) ? Math.Max(Depth[idx], WaterTableDepth(idx)) : 0;
    public double OpenWaterDepth(Vec2 p) => Math.Max(SurfaceWaterDepth(p), WaterTableDepth(p));

    /// <summary>Fluid surface elevation, or NaN in dry cells.</summary>
    public double SurfaceAt(Vec2 p)
    {
        if(Liquid!=null){double surface=Liquid.SurfaceAt(p);return IsWaterTable(p)?Math.Max(WaterTable,double.IsNaN(surface)?WaterTable:surface):surface;}
        int c = Grid.CellAt(p);
        if (c < 0 || !Grid.InDomain(c) || !IsWet(p)) return double.NaN;
        bool isTable = IsWaterTable(p);
        bool hasDepth = Depth[c] > 0;
        if (isTable && hasDepth)
            return Math.Max(WaterTable, HydraulicBed(c) + Depth[c]);
        if (isTable)
            return WaterTable;
        if (hasDepth)
            return HydraulicBed(c) + Depth[c];
        return double.NaN;
    }

    /// <summary>Open water present in stored fluid depth.</summary>
    public bool IsWet(int idx) => Grid.InDomain(idx) && (HasSurfaceWater(idx) || IsWaterTable(idx));
    public bool IsWet(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) && (SurfaceWaterDepth(p) > 0.0005 || IsWaterTable(p));
    }

    /// <summary>True if the cell or position is part of the hydrostatic water table (ground below water table).</summary>
    public bool IsWaterTable(int idx) => Grid.InDomain(idx) && Bed[idx] < WaterTable;
    public bool IsWaterTable(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) && _terrain.Height(p) < WaterTable;
    }

    /// <summary>Depth of the hydrostatic water table above the terrain bed (0 if ground is above the water table).</summary>
    public double WaterTableDepth(int idx) => Grid.InDomain(idx) ? Math.Max(0.0, WaterTable - Bed[idx]) : 0;
    public double WaterTableDepth(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && Grid.InDomain(c) ? Math.Max(0.0, WaterTable - _terrain.Height(p)) : 0;
    }

    /// <summary>Derived convenience classification: dynamic surface water with a measurable current.</summary>
    public bool IsStream(int idx) =>
        Grid.InDomain(idx) && Depth[idx] >= 0.00005 && StreamVelocity(idx).Length > Config.StreamVelocityThreshold;
    public bool IsStream(Vec2 p)
    {
        int c = Grid.CellAt(p);
        return c >= 0 && IsStream(c);
    }

    /// <summary>Dynamic surface-water depth. Stream classification remains a convenience query.</summary>
    public double StreamDepth(int idx) => SurfaceWaterDepth(idx);
    public double StreamDepth(Vec2 p) => SurfaceWaterDepth(p);

    /// <summary>Flow velocity vector (m/s) in world XZ for a cell.</summary>
    public Vec2 StreamVelocity(int idx)
    {
        if (!Grid.InDomain(idx) || Depth[idx] < 0.00005) return Vec2.Zero;
        double area = Math.Max(Depth[idx] * Grid.CellSize, 1e-6);
        return new Vec2(FlowX[idx] / area, FlowZ[idx] / area);
    }
    public Vec2 StreamVelocity(Vec2 p)
    {
        if(Liquid!=null)return Liquid.VelocityAt(p);
        int c = Grid.CellAt(p);
        return c >= 0 ? StreamVelocity(c) : Vec2.Zero;
    }

    /// <summary>Dynamic surface-water volume only. The implicit groundwater reservoir is intentionally not counted.</summary>
    public double Volume() { if(Liquid!=null)return Liquid.Volume;double v = 0; foreach (int idx in Grid.DomainCells) v += Depth[idx]; return v * CellArea; }
    public double SurfaceVolume() => Volume();

    /// <summary>Groundwater is implicit, so initialization no longer materializes it into dynamic surface storage.</summary>
    public void InitializeFromWaterTable() => InvalidateWaterDistance();

    public Substrate[]? Substrates { get; set; }
    public Func<int, Substrate>? SubstrateProvider { get; set; }

    public void Advance(double dt,Substrate[]? substrates=null)=>Liquid?.AdvancePhysical(dt);
    public void Advance(double dt,Func<int,Substrate> substrateProvider)=>Liquid?.AdvancePhysical(dt);
    public void Step(double dt)=>Liquid?.AdvancePhysical(dt);
    public void Step(double dt,Substrate[]? substrates)=>Liquid?.AdvancePhysical(dt);
    public void Step(double dt,Func<int,Substrate> substrateProvider)=>Liquid?.AdvancePhysical(dt);

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
    /// deterministic and O(cells). Cells with no water anywhere get +âˆž.
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
    /// Soil hydration is an ecological field, not stored water. It is driven independently by:
    /// hydrostatic capillary rise from groundwater, saturating local surface-water depth, neighbouring
    /// surface-water depth, then the ordinary soil diffusion/drying response.
    /// </summary>
    public void CoupleMoisture(ScalarField moisture, EcologyConfig eco, double dt, double[] scratch)
    {
        double wet = 1 - Math.Exp(-eco.MoistureWetting * dt);
        double dry = 1 - Math.Exp(-eco.MoistureDrying * dt);
        double depthScale = Math.Max(Config.WetDepth * 2.5, 0.02);
        double capRange = Math.Max(eco.MoistureCapillaryRange, Grid.CellSize);

        double SurfaceHydration(double depth) =>
            depth <= 0 ? 0 : MathD.Clamp01(1 - Math.Exp(-depth / depthScale));

        // Soil only couples on the environment cadence. Evaluate water hydration once per cell,
        // and the fixed neighbour kernel once per pass, instead of repeating exp/sqrt 24 times per cell.
        foreach (int idx in Grid.DomainCells) scratch[idx] = SurfaceHydration(Depth[idx]);
        var neighbourWeight = new double[25];
        for (int dj = -2; dj <= 2; dj++)
            for (int di = -2; di <= 2; di++)
            {
                double distance = Grid.CellSize * Math.Sqrt(di * di + dj * dj);
                neighbourWeight[(dj + 2) * 5 + di + 2] = Math.Exp(-distance / (capRange * 2.5));
            }

        foreach (int idx in Grid.DomainCells)
        {
            double m = moisture.Values[idx];

            // Groundwater influence depends on vertical distance to the hydrostatic table.
            double aboveTable = Math.Max(0, Bed[idx] - WaterTable);
            double capillary = MathD.Clamp01(1 - aboveTable / Math.Max(eco.MoistureWaterTableRange, 1e-9));
            double groundwaterTarget = IsWaterTable(idx)
                ? 1.0
                : Math.Max(eco.MoistureDryBaseline, 0.85 * capillary * capillary);

            // Surface water is deliberately saturating: a deeper pool matters, but cannot make soil > fully wet.
            double localTarget = 0.98 * scratch[idx];

            // Immediate neighbours contribute according to their actual dynamic water amount. Longer-range
            // smoothing remains the job of the soil-moisture diffusion field rather than a water-distance binary.
            int i = idx % Grid.Nx, j = idx / Grid.Nx;
            double neighbourTarget = 0;
            for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ii = i + di, jj = j + dj;
                    if (!Grid.InDomain(ii, jj)) continue;
                    int n = Grid.Index(ii, jj);
                    double h = scratch[n];
                    if (h <= 0) continue;
                    neighbourTarget = Math.Max(neighbourTarget, 0.98 * h * neighbourWeight[(dj + 2) * 5 + di + 2]);
                }

            double target = MathD.Clamp01(Math.Max(groundwaterTarget, Math.Max(localTarget, neighbourTarget)));
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
        if(Liquid!=null)
        {
            // Include slopes, airborne shape, retained fractions and queued source history.
            d.Add(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Liquid.CaptureState(),Vivarium.Sim.Persistence.WorldSerializer.Json));
        }
        d.Add(Budget.SpringInflow).Add(Budget.GroundwaterInflow).Add(Budget.GroundwaterRecharge).Add(Budget.Evaporation).Add(Budget.Infiltration).Add(Budget.BoundaryOutflow).Add(Budget.ToolInflow).Add(Budget.ToolRemoval);
        return d.Hex();
    }
}
