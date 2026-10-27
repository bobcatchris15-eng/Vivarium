using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>World adapter and scheduler entry point for aquatic coverage over real hydrology.</summary>
public sealed class AquaticSystem : IAquaticEnv
{
    private readonly VivariumWorld _w;
    private long _step;
    /// <summary>Completed aquatic steps; recruitment uses this ordinal.</summary>
    public long StepIndex => _step;
    internal void RestoreStep(long step)
    {
        if (step < 0) throw new InvalidDataException($"aquatic step is invalid ({step})");
        _step = step;
    }

    public AquaticSystem(VivariumWorld world) => _w = world;

    public void SeedInitial()
    {
        int ordinal = 0;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            var c = _w.Grid.CellCenter(cell);
            var (centerGx, centerGz) = CoverageSpec.CellOf(c);
            double depth = _w.Water.OpenWaterDepth(cell);
            double speed = Math.Sqrt(_w.Water.FlowX[cell] * _w.Water.FlowX[cell] + _w.Water.FlowZ[cell] * _w.Water.FlowZ[cell]);
            double light = _w.Fields.Light[cell];
            double nut = _w.Fields.Nutrients[cell];

            // 1. Benthic periphyton (AlgaeBed) founder colonies on submerged bed with light
            if (depth >= 0.003 && light >= 0.08 && speed <= 0.28)
            {
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx * dx + dz * dz > 5) continue;
                    int gx = centerGx + dx, gz = centerGz + dz;
                    if (DepthAt(gx, gz) > 0)
                        _w.Coverage.AlgaeBed.SetCell(gx, gz, AlgaeRules.OccupantId, 0.3f, 0, 0, 0, 0, 0);
                }
            }

            // 2. Duckweed (SurfaceFloat) founder colonies in calm water
            if (speed <= 0.06 && depth >= 0.008 && light >= 0.15 && ordinal % 3 == 0)
            {
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx * dx + dz * dz > 6) continue;
                    int gx = centerGx + dx, gz = centerGz + dz;
                    if (DepthAt(gx, gz) > 0 && !IsObstacle(gx, gz))
                        _w.Coverage.SurfaceFloat.SetCell(gx, gz, SurfaceFloatRules.OccupantId, 0.35f, 0, 0, 0, 0, 0);
                }
            }

            // 3. Filamentous algae (AlgaeFloat) founder clusters in calm nutrient-rich pools
            if (speed <= 0.04 && depth >= 0.008 && nut >= 0.08 && light >= 0.15 && ordinal % 5 == 0)
            {
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx * dx + dz * dz > 5) continue;
                    int gx = centerGx + dx, gz = centerGz + dz;
                    if (DepthAt(gx, gz) > 0 && !IsObstacle(gx, gz))
                        _w.Coverage.AlgaeFloat.SetCell(gx, gz, AlgaeRules.OccupantId, 0.25f, 0, 0, 0, 0, 0);
                }
            }

            ordinal++;
        }
        ProjectBiofilm();
    }

    private readonly List<WetCoarseCell> _wetCells = new(256);
    private readonly List<(int Gx, int Gz, float B)> _spreadDuck = new(128);
    private readonly List<(int Gx, int Gz, float B)> _spreadBed = new(128);
    private readonly List<(int Gx, int Gz, float B)> _spreadFloat = new(128);
    private readonly HashSet<long> _seenCoords = new(256);

    public void Step(double physicalSeconds, double bioSeconds)
    {
        foreach (bool _ in StepBatches(physicalSeconds, bioSeconds)) { }
    }

    public static System.Action<string, double>? StageTimed { get; set; }
    public AlgaeStepStats LastAlgaeStep { get; private set; }
    public SurfaceFloatStats LastSurfaceFloatStep { get; private set; }

    /// <summary>Preserves step order while allowing the scheduler to spread atomic stages over frames.</summary>
    public IEnumerable<bool> StepBatches(double physicalSeconds, double bioSeconds)
    {
        double dtDays = bioSeconds / AquaticConst.SecondsPerDay;
        double flowDays = physicalSeconds / AquaticConst.SecondsPerDay;
        var wet = CollectWetCoarseCells();
        yield return true;

        if (wet != null && dtDays > 0)
        {
            var swRecruit = System.Diagnostics.Stopwatch.StartNew();
            RecruitAndSpread(dtDays, _step);
            StageTimed?.Invoke("Aquatic.Step.Recruit", swRecruit.Elapsed.TotalMilliseconds);
        }
        yield return true;

        var occBed = OccupiedBounds(_w.Coverage.AlgaeBed);
        var occFloat = OccupiedBounds(_w.Coverage.AlgaeFloat);
        var occAlgae = Union(occBed, occFloat);
        var algaeArea = ComputeReachableBounds(occAlgae, physicalSeconds, wet);
        yield return true;
        if (algaeArea is { } a)
            foreach (bool batch in AlgaeRules.StepBatches(_w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat, this,
                new AlgaeBedParams(), new AlgaeFloatParams(), a, dtDays, _step, _w.Seed, flowDays,
                complete: stats =>
                {
                    LastAlgaeStep = stats;
                    StageTimed?.Invoke("Aquatic.Step.AlgaeBed", stats.BedMs);
                    StageTimed?.Invoke("Aquatic.Step.AlgaeFloatGrow", stats.FloatGrowMs);
                }))
                yield return batch;
        yield return true;

        var occDuck = OccupiedBounds(_w.Coverage.SurfaceFloat);
        var duckweedArea = ComputeReachableBounds(occDuck, physicalSeconds, wet);
        yield return true;
        if (duckweedArea is { } d)
            foreach (bool batch in SurfaceFloatRules.StepBatches(_w.Coverage.SurfaceFloat, this, new DuckweedParams(), d, dtDays, _step, _w.Seed, flowDays,
                complete: stats =>
                {
                    LastSurfaceFloatStep = stats;
                    StageTimed?.Invoke("Aquatic.Step.DuckweedGrow", stats.GrowthMs);
                }))
                yield return batch;
        yield return true;

        _step++;
        var swBiofilm = System.Diagnostics.Stopwatch.StartNew();
        ProjectBiofilm();
        StageTimed?.Invoke("Aquatic.Step.Biofilm", swBiofilm.Elapsed.TotalMilliseconds);
    }

    private void RecruitAndSpread(double dtDays, long step)
    {
        // 1. Natural recruitment into suitable wet coarse cells (e.g. spring runoff that filled pools after world creation)
        int ordinal = 0;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            var c = _w.Grid.CellCenter(cell);
            var (cgx, cgz) = CoverageSpec.CellOf(c);
            double depth = _w.Water.OpenWaterDepth(cell);
            double speed = Math.Sqrt(_w.Water.FlowX[cell] * _w.Water.FlowX[cell] + _w.Water.FlowZ[cell] * _w.Water.FlowZ[cell]);
            double light = _w.Fields.Light[cell];
            double nut = _w.Fields.Nutrients[cell];

            // Benthic periphyton recruitment: uncolonized submerged bed with light
            if (depth >= 0.003 && light >= 0.12 && speed <= 0.20 && _w.Coverage.AlgaeBed.GetB(cgx, cgz) < 0.05f)
            {
                if ((step + ordinal) % 3 == 0)
                {
                    _w.Coverage.AlgaeBed.SetCell(cgx, cgz, AlgaeRules.OccupantId, 0.2f, 0, 0, 0, 0, 0);
                }
            }

            // Duckweed pioneer recruitment in calm, sunny water
            if (speed <= 0.04 && depth >= 0.01 && light >= 0.25 && _w.Coverage.SurfaceFloat.GetB(cgx, cgz) < 0.05f)
            {
                if ((step + ordinal) % 5 == 0)
                {
                    _w.Coverage.SurfaceFloat.SetCell(cgx, cgz, SurfaceFloatRules.OccupantId, 0.2f, 0, 0, 0, 0, 0);
                }
            }

            // Filamentous algae pioneer recruitment in calm nutrient pools
            if (speed <= 0.03 && depth >= 0.01 && nut >= 0.15 && light >= 0.25 && _w.Coverage.AlgaeFloat.GetB(cgx, cgz) < 0.05f)
            {
                if ((step + ordinal) % 7 == 0)
                {
                    _w.Coverage.AlgaeFloat.SetCell(cgx, cgz, AlgaeRules.OccupantId, 0.15f, 0, 0, 0, 0, 0);
                }
            }
            ordinal++;
        }

        // 2. Vegetative lateral expansion from thriving colonies into adjacent suitable wet cells
        _spreadDuck.Clear();
        _spreadBed.Clear();
        _spreadFloat.Clear();

        // Duckweed clonal budding expansion
        _seenCoords.Clear();
        foreach (var tile in _w.Coverage.SurfaceFloat.Tiles)
        {
            int baseGx = tile.Ti * CoverageSpec.TileEdge, baseGz = tile.Tj * CoverageSpec.TileEdge;
            for (int li = 0; li < CoverageTile.N; li++)
            {
                float b = tile.B[li];
                if (b < 0.15f) continue;
                int gx = baseGx + li % CoverageSpec.TileEdge, gz = baseGz + li / CoverageSpec.TileEdge;
                if (DepthAt(gx, gz) <= 0) continue;
                if (LightAt(gx, gz) < 0.1 || FlowAt(gx, gz).Length > 0.06) continue;

                TryQueueSpread(_w.Coverage.SurfaceFloat, gx + 1, gz, _spreadDuck);
                TryQueueSpread(_w.Coverage.SurfaceFloat, gx - 1, gz, _spreadDuck);
                TryQueueSpread(_w.Coverage.SurfaceFloat, gx, gz + 1, _spreadDuck);
                TryQueueSpread(_w.Coverage.SurfaceFloat, gx, gz - 1, _spreadDuck);
            }
        }

        // AlgaeBed benthic periphyton expansion
        _seenCoords.Clear();
        foreach (var tile in _w.Coverage.AlgaeBed.Tiles)
        {
            int baseGx = tile.Ti * CoverageSpec.TileEdge, baseGz = tile.Tj * CoverageSpec.TileEdge;
            for (int li = 0; li < CoverageTile.N; li++)
            {
                float b = tile.B[li];
                if (b < 0.15f) continue;
                int gx = baseGx + li % CoverageSpec.TileEdge, gz = baseGz + li / CoverageSpec.TileEdge;
                if (DepthAt(gx, gz) <= 0 || FlowAt(gx, gz).Length > 0.15) continue;
                if (LightAt(gx, gz) < 0.08) continue;

                TryQueueSpread(_w.Coverage.AlgaeBed, gx + 1, gz, _spreadBed);
                TryQueueSpread(_w.Coverage.AlgaeBed, gx - 1, gz, _spreadBed);
                TryQueueSpread(_w.Coverage.AlgaeBed, gx, gz + 1, _spreadBed);
                TryQueueSpread(_w.Coverage.AlgaeBed, gx, gz - 1, _spreadBed);
            }
        }

        // AlgaeFloat mat expansion in calm water
        _seenCoords.Clear();
        foreach (var tile in _w.Coverage.AlgaeFloat.Tiles)
        {
            int baseGx = tile.Ti * CoverageSpec.TileEdge, baseGz = tile.Tj * CoverageSpec.TileEdge;
            for (int li = 0; li < CoverageTile.N; li++)
            {
                float b = tile.B[li];
                if (b < 0.15f) continue;
                int gx = baseGx + li % CoverageSpec.TileEdge, gz = baseGz + li / CoverageSpec.TileEdge;
                if (DepthAt(gx, gz) <= 0) continue;
                if (FlowAt(gx, gz).Length > 0.05) continue;

                TryQueueSpread(_w.Coverage.AlgaeFloat, gx + 1, gz, _spreadFloat);
                TryQueueSpread(_w.Coverage.AlgaeFloat, gx - 1, gz, _spreadFloat);
                TryQueueSpread(_w.Coverage.AlgaeFloat, gx, gz + 1, _spreadFloat);
                TryQueueSpread(_w.Coverage.AlgaeFloat, gx, gz - 1, _spreadFloat);
            }
        }

        // Apply queued spreads
        foreach (var (gx, gz, b) in _spreadDuck)
            _w.Coverage.SurfaceFloat.SetCell(gx, gz, SurfaceFloatRules.OccupantId, b, 0, 0, 0, 0, 0);
        foreach (var (gx, gz, b) in _spreadBed)
            _w.Coverage.AlgaeBed.SetCell(gx, gz, AlgaeRules.OccupantId, b, 0, 0, 0, 0, 0);
        foreach (var (gx, gz, b) in _spreadFloat)
            _w.Coverage.AlgaeFloat.SetCell(gx, gz, AlgaeRules.OccupantId, b, 0, 0, 0, 0, 0);
    }

    private void TryQueueSpread(CoverageLayer layer, int gx, int gz, List<(int, int, float)> queue)
    {
        // Dense colonies reject most neighbours here. Avoid hydrology queries for
        // an occupied cell, or one that is already queued, without changing order.
        if (layer.GetB(gx, gz) >= 0.05f) return;
        long key = ((long)gx << 32) | (uint)gz;
        if (_seenCoords.Contains(key)) return;
        double depth = DepthAt(gx, gz);
        if (depth <= 0) return;
        // This sealed adapter defines IsObstacle as DepthAt <= 0, already tested.
        if (_seenCoords.Add(key))
            queue.Add((gx, gz, 0.10f));
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

    private struct DepthSample
    {
        public bool Valid;
        public long LiquidVersion;
        public int TerrainVersion, PropsVersion;
        public double Table, Stored, Bed, Value;
    }
    private DepthSample[]? _depthSamples;
    public long DepthCacheHits { get; private set; }
    public long DepthCacheMisses { get; private set; }

    public double DepthAt(int gx, int gz)
    {
        int c = Cell(gx, gz);
        if (c < 0) return 0;
        var water = _w.Water;
        long version = water.Liquid?.Version ?? -1;
        double table = water.WaterTable, stored = water.Depth[c], bed = water.Bed[c];
        // Tile scans repeatedly revisit the same coarse cell in different rows and
        // layers. Retain each exact coarse sample, validating all query dependencies.
        _depthSamples ??= new DepthSample[water.Depth.Length];
        ref var sample = ref _depthSamples[c];
        if (sample.Valid && sample.LiquidVersion == version && sample.Table == table &&
            sample.Stored == stored && sample.Bed == bed && sample.TerrainVersion == _w.Terrain.Version &&
            sample.PropsVersion == _w.Props.Version)
        {
            DepthCacheHits++; return sample.Value;
        }
        DepthCacheMisses++;
        sample = new DepthSample { Valid = true, LiquidVersion = version, Table = table, Stored = stored, Bed = bed,
            TerrainVersion = _w.Terrain.Version, PropsVersion = _w.Props.Version,
            Value = water.IsWet(c) ? water.OpenWaterDepth(c) : 0 };
        return sample.Value;
    }
    public Vec2 FlowAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? new Vec2(_w.Water.FlowX[c], _w.Water.FlowZ[c]) : Vec2.Zero; }
    public double LightAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? _w.Fields.Light[c] : 0; }
    public double NutrientsAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? MathD.Clamp01(_w.Fields.Nutrients[c] / Math.Max(_w.Fields.Nutrients.Max, 1e-9)) : 0; }
    public bool IsObstacle(int gx, int gz) => DepthAt(gx, gz) <= 0;
    public double SurfaceShadeAt(int gx, int gz) => MathD.Clamp01(
        0.65 * _w.Coverage.SurfaceFloat.GetB(gx, gz) + 0.5 * _w.Coverage.AlgaeFloat.GetB(gx, gz));
}
