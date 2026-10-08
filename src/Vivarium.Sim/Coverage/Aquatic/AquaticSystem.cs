using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>World adapter and scheduler entry point for aquatic coverage over real hydrology.</summary>
public sealed class AquaticSystem : IAquaticEnv
{
    private readonly VivariumWorld _w;
    private long _step;

    public AquaticSystem(VivariumWorld world) => _w = world;

    public void SeedInitial()
    {
        int ordinal = 0;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            var (gx, gz) = CoverageSpec.CellOf(_w.Grid.CellCenter(cell));
            // Sparse, repeatable founders. Subsequent expansion follows the water and its flow.
            if (ordinal % 9 == 0) _w.Coverage.AlgaeBed.SetCell(gx, gz, AlgaeRules.OccupantId, 0.2f, 0, 0, 0, 0, 0);
            if (ordinal % 47 == 0) _w.Coverage.AlgaeFloat.SetCell(gx, gz, AlgaeRules.OccupantId, 0.1f, 0, 0, 0, 0, 0);
            if (ordinal % 31 == 0) _w.Coverage.SurfaceFloat.SetCell(gx, gz, SurfaceFloatRules.OccupantId, 0.1f, 0, 0, 0, 0, 0);
            ordinal++;
        }
        ProjectBiofilm();
    }

    public void Step(double physicalSeconds, double bioSeconds)
    {
        double dtDays = bioSeconds / AquaticConst.SecondsPerDay;
        double flowDays = physicalSeconds / AquaticConst.SecondsPerDay;
        var (pad, wet) = FlowHaloAndWetBounds(physicalSeconds);
        var algaeArea = Clip(OccupiedBounds(pad, _w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat), OccupiedBounds(0, _w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat), wet);
        if (algaeArea is { } a)
            AlgaeRules.Step(_w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat, this,
                new AlgaeBedParams(), new AlgaeFloatParams(), a, dtDays, _step, _w.Seed, flowDays);
        var duckweedArea = Clip(OccupiedBounds(pad, _w.Coverage.SurfaceFloat), OccupiedBounds(0, _w.Coverage.SurfaceFloat), wet);
        if (duckweedArea is { } d)
            SurfaceFloatRules.Step(_w.Coverage.SurfaceFloat, this, new DuckweedParams(), d, dtDays, _step, _w.Seed, flowDays);
        _step++;
        ProjectBiofilm();
    }

    public void ProjectBiofilm() => AquaticBiofilm.Project(_w.Coverage.AlgaeBed, _w.Fields.Biofilm, _w.Water);

    private (int Pad, GridBounds? Wet) FlowHaloAndWetBounds(double physicalSeconds)
    {
        double maxSpeed = 0;
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        double half = _w.Grid.CellSize * 0.5;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            double speed = Math.Sqrt(_w.Water.FlowX[cell] * _w.Water.FlowX[cell] + _w.Water.FlowZ[cell] * _w.Water.FlowZ[cell]);
            maxSpeed = Math.Max(maxSpeed, speed);
            // Fine cells whose centre falls in this coarse cell's square; one cell of slack for edge rounding.
            var c = _w.Grid.CellCenter(cell);
            var (x0, z0) = CoverageSpec.CellOf(new Vec2(c.X - half, c.Z - half));
            var (x1, z1) = CoverageSpec.CellOf(new Vec2(c.X + half, c.Z + half));
            minX = Math.Min(minX, x0 - 1); minZ = Math.Min(minZ, z0 - 1);
            maxX = Math.Max(maxX, x1 + 1); maxZ = Math.Max(maxZ, z1 + 1);
        }
        // An upwind step can advance at most one fine cell per CFL substep. One extra cell keeps occupied
        // biomass away from the artificial rectangular boundary, so no transport is clipped there.
        int pad = Math.Clamp((int)Math.Ceiling(maxSpeed * physicalSeconds /
            (AquaticConst.CflSafety * CoverageSpec.CellSize)) + 1, 1, AquaticConst.MaxSubsteps + 1);
        return (pad, minX == int.MaxValue ? null : new GridBounds(minX, minZ, maxX, maxZ));
    }

    /// <summary>
    /// Clips the padded step rectangle to the hull of the unpadded occupied cells and every wet fine cell.
    /// Exact, not an approximation: a cell outside that hull holds no biomass (growth skips it) and is dry, so
    /// advection treats it as blocked: it exchanges no flux and is never written back. Without this, a fast
    /// flow pads the rectangle by up to MaxSubsteps cells per side, and advection then sweeps tens of millions
    /// of dry cells for thousands of substeps (minutes per call).
    /// </summary>
    private static GridBounds? Clip(GridBounds? padded, GridBounds? occupied, GridBounds? wet)
    {
        if (padded is not { } p || occupied is not { } o) return padded;
        int hx0 = o.MinGx, hz0 = o.MinGz, hx1 = o.MaxGx, hz1 = o.MaxGz;
        if (wet is { } w) { hx0 = Math.Min(hx0, w.MinGx); hz0 = Math.Min(hz0, w.MinGz); hx1 = Math.Max(hx1, w.MaxGx); hz1 = Math.Max(hz1, w.MaxGz); }
        return new GridBounds(Math.Max(p.MinGx, hx0), Math.Max(p.MinGz, hz0), Math.Min(p.MaxGx, hx1), Math.Min(p.MaxGz, hz1));
    }

    private static GridBounds? OccupiedBounds(int pad, params CoverageLayer[] layers)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var layer in layers)
        foreach (var tile in layer.Tiles)
        for (int li = 0; li < CoverageTile.N; li++)
        {
            if (tile.B[li] <= 0 && tile.Occ[li] == 0) continue;
            int x = tile.Ti * CoverageSpec.TileEdge + li % CoverageSpec.TileEdge;
            int z = tile.Tj * CoverageSpec.TileEdge + li / CoverageSpec.TileEdge;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return minX == int.MaxValue ? null : new GridBounds(minX - pad, minZ - pad, maxX + pad, maxZ + pad);
    }

    // The (gx,gz) -> coarse cell mapping is pure grid geometry, invariant for the object's lifetime, so a
    // 1-slot cache is always valid to reuse (no staleness risk) and turns the common DepthAt-then-IsObstacle-
    // then-FlowAt call sequence for the same fine cell (the aquatic rules' per-cell pattern) into a single
    // lookup instead of up to three.
    private int _cacheGx = int.MinValue, _cacheGz = int.MinValue, _cacheCell = -1;

    private int Cell(int gx, int gz)
    {
        if (gx == _cacheGx && gz == _cacheGz) return _cacheCell;
        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
        int cell = _w.Grid.CellAt(p);
        cell = _w.Grid.InDomain(cell) ? cell : -1;
        _cacheGx = gx; _cacheGz = gz; _cacheCell = cell;
        return cell;
    }

    private int _depthGx = int.MinValue, _depthGz = int.MinValue; private double _depthVal;

    public double DepthAt(int gx, int gz)
    {
        if (gx == _depthGx && gz == _depthGz) return _depthVal;
        int c = Cell(gx, gz);
        double d = c >= 0 && _w.Water.IsWet(c) ? _w.Water.OpenWaterDepth(c) : 0;
        _depthGx = gx; _depthGz = gz; _depthVal = d;
        return d;
    }
    public Vec2 FlowAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? new Vec2(_w.Water.FlowX[c], _w.Water.FlowZ[c]) : Vec2.Zero; }
    public double LightAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? _w.Fields.Light[c] : 0; }
    public double NutrientsAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? MathD.Clamp01(_w.Fields.Nutrients[c] / Math.Max(_w.Fields.Nutrients.Max, 1e-9)) : 0; }
    public bool IsObstacle(int gx, int gz) => DepthAt(gx, gz) <= 0;
    public double SurfaceShadeAt(int gx, int gz) => MathD.Clamp01(
        0.65 * _w.Coverage.SurfaceFloat.GetB(gx, gz) + 0.5 * _w.Coverage.AlgaeFloat.GetB(gx, gz));
}
