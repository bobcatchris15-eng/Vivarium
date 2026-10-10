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

    private readonly List<WetCoarseCell> _wetCells = new(256);

    public void Step(double physicalSeconds, double bioSeconds)
    {
        double dtDays = bioSeconds / AquaticConst.SecondsPerDay;
        double flowDays = physicalSeconds / AquaticConst.SecondsPerDay;
        var wet = CollectWetCoarseCells();

        var occBed = OccupiedBounds(_w.Coverage.AlgaeBed);
        var occFloat = OccupiedBounds(_w.Coverage.AlgaeFloat);
        var occAlgae = Union(occBed, occFloat);
        var algaeArea = ComputeReachableBounds(occAlgae, physicalSeconds, wet);
        if (algaeArea is { } a)
            AlgaeRules.Step(_w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat, this,
                new AlgaeBedParams(), new AlgaeFloatParams(), a, dtDays, _step, _w.Seed, flowDays);

        var occDuck = OccupiedBounds(_w.Coverage.SurfaceFloat);
        var duckweedArea = ComputeReachableBounds(occDuck, physicalSeconds, wet);
        if (duckweedArea is { } d)
            SurfaceFloatRules.Step(_w.Coverage.SurfaceFloat, this, new DuckweedParams(), d, dtDays, _step, _w.Seed, flowDays);

        _step++;
        ProjectBiofilm();
    }

    public void ProjectBiofilm() => AquaticBiofilm.Project(_w.Coverage.AlgaeBed, _w.Fields.Biofilm, _w.Water);

    private readonly struct WetCoarseCell
    {
        public readonly int MinGx, MinGz, MaxGx, MaxGz;
        public readonly double Speed;
        public WetCoarseCell(int minGx, int minGz, int maxGx, int maxGz, double speed)
        {
            MinGx = minGx; MinGz = minGz; MaxGx = maxGx; MaxGz = maxGz; Speed = speed;
        }
    }

    private GridBounds? CollectWetCoarseCells()
    {
        _wetCells.Clear();
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        double half = _w.Grid.CellSize * 0.5;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            double speed = Math.Sqrt(_w.Water.FlowX[cell] * _w.Water.FlowX[cell] + _w.Water.FlowZ[cell] * _w.Water.FlowZ[cell]);
            var c = _w.Grid.CellCenter(cell);
            var (x0, z0) = CoverageSpec.CellOf(new Vec2(c.X - half, c.Z - half));
            var (x1, z1) = CoverageSpec.CellOf(new Vec2(c.X + half, c.Z + half));
            int xMin = x0 - 1, zMin = z0 - 1, xMax = x1 + 1, zMax = z1 + 1;
            minX = Math.Min(minX, xMin); minZ = Math.Min(minZ, zMin);
            maxX = Math.Max(maxX, xMax); maxZ = Math.Max(maxZ, zMax);
            _wetCells.Add(new WetCoarseCell(xMin, zMin, xMax, zMax, speed));
        }
        return minX == int.MaxValue ? null : new GridBounds(minX, minZ, maxX, maxZ);
    }

    /// <summary>
    /// Computes the exact bounding box reachable by advection from occupied cells within physicalSeconds.
    /// Eliminates quadratic/unbounded scaling and dry-cell over-padding: instead of padding every layer by
    /// the maximum flow speed across the entire world, the halo is bounded by the maximum velocity in the
    /// locally reachable wet coarse cells.
    /// </summary>
    private GridBounds? ComputeReachableBounds(GridBounds? occ, double physicalSeconds, GridBounds? wet)
    {
        if (occ is not { } o) return null;
        if (wet is not { } w) return occ;

        int curMinX = o.MinGx, curMinZ = o.MinGz, curMaxX = o.MaxGx, curMaxZ = o.MaxGz;
        double maxSpeed = 0;

        while (true)
        {
            double speedInBounds = 0;
            for (int i = 0; i < _wetCells.Count; i++)
            {
                var wc = _wetCells[i];
                if (wc.MaxGx >= curMinX && wc.MinGx <= curMaxX &&
                    wc.MaxGz >= curMinZ && wc.MinGz <= curMaxZ)
                {
                    if (wc.Speed > speedInBounds) speedInBounds = wc.Speed;
                }
            }

            if (speedInBounds <= maxSpeed) break;
            maxSpeed = speedInBounds;

            int pad = Math.Clamp((int)Math.Ceiling(maxSpeed * physicalSeconds /
                (AquaticConst.CflSafety * CoverageSpec.CellSize)) + 1, 1, AquaticConst.MaxSubsteps + 1);

            int newMinX = Math.Max(o.MinGx - pad, w.MinGx);
            int newMinZ = Math.Max(o.MinGz - pad, w.MinGz);
            int newMaxX = Math.Min(o.MaxGx + pad, w.MaxGx);
            int newMaxZ = Math.Min(o.MaxGz + pad, w.MaxGz);

            if (newMinX == curMinX && newMinZ == curMinZ && newMaxX == curMaxX && newMaxZ == curMaxZ)
                break;

            curMinX = newMinX; curMinZ = newMinZ; curMaxX = newMaxX; curMaxZ = newMaxZ;
        }

        int hx0 = Math.Min(o.MinGx, curMinX);
        int hz0 = Math.Min(o.MinGz, curMinZ);
        int hx1 = Math.Max(o.MaxGx, curMaxX);
        int hz1 = Math.Max(o.MaxGz, curMaxZ);
        return new GridBounds(hx0, hz0, hx1, hz1);
    }

    private static GridBounds? OccupiedBounds(CoverageLayer layer)
    {
        int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
        foreach (var tile in layer.Tiles)
        for (int li = 0; li < CoverageTile.N; li++)
        {
            if (tile.B[li] <= 0 && tile.Occ[li] == 0) continue;
            int x = tile.Ti * CoverageSpec.TileEdge + li % CoverageSpec.TileEdge;
            int z = tile.Tj * CoverageSpec.TileEdge + li / CoverageSpec.TileEdge;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }
        return minX == int.MaxValue ? null : new GridBounds(minX, minZ, maxX, maxZ);
    }

    private static GridBounds? Union(GridBounds? a, GridBounds? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return new GridBounds(
            Math.Min(a.Value.MinGx, b.Value.MinGx),
            Math.Min(a.Value.MinGz, b.Value.MinGz),
            Math.Max(a.Value.MaxGx, b.Value.MaxGx),
            Math.Max(a.Value.MaxGz, b.Value.MaxGz));
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
