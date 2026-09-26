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
        int pad = FlowHalo(physicalSeconds);
        var algaeArea = OccupiedBounds(pad, _w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat);
        if (algaeArea is { } a)
            AlgaeRules.Step(_w.Coverage.AlgaeBed, _w.Coverage.AlgaeFloat, this,
                new AlgaeBedParams(), new AlgaeFloatParams(), a, dtDays, _step, _w.Seed, flowDays);
        var duckweedArea = OccupiedBounds(pad, _w.Coverage.SurfaceFloat);
        if (duckweedArea is { } d)
            SurfaceFloatRules.Step(_w.Coverage.SurfaceFloat, this, new DuckweedParams(), d, dtDays, _step, _w.Seed, flowDays);
        _step++;
        ProjectBiofilm();
    }

    public void ProjectBiofilm() => AquaticBiofilm.Project(_w.Coverage.AlgaeBed, _w.Fields.Biofilm);

    private int FlowHalo(double physicalSeconds)
    {
        double maxSpeed = 0;
        foreach (int cell in _w.Grid.DomainCells)
        {
            if (!_w.Water.IsWet(cell)) continue;
            double speed = Math.Sqrt(_w.Water.FlowX[cell] * _w.Water.FlowX[cell] + _w.Water.FlowZ[cell] * _w.Water.FlowZ[cell]);
            maxSpeed = Math.Max(maxSpeed, speed);
        }
        // An upwind step can advance at most one fine cell per CFL substep. One extra cell keeps occupied
        // biomass away from the artificial rectangular boundary, so no transport is clipped there.
        return Math.Clamp((int)Math.Ceiling(maxSpeed * physicalSeconds /
            (AquaticConst.CflSafety * CoverageSpec.CellSize)) + 1, 1, AquaticConst.MaxSubsteps + 1);
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
        return minX == int.MaxValue ? null : new GridBounds(minX, minZ, maxX, maxZ);
    }

    private int Cell(int gx, int gz)
    {
        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
        int cell = _w.Grid.CellAt(p);
        return _w.Grid.InDomain(cell) ? cell : -1;
    }

    public double DepthAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 && _w.Water.IsWet(c) ? _w.Water.Depth[c] : 0; }
    public Vec2 FlowAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? new Vec2(_w.Water.FlowX[c], _w.Water.FlowZ[c]) : Vec2.Zero; }
    public double LightAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? _w.Fields.Light[c] : 0; }
    public double NutrientsAt(int gx, int gz) { int c = Cell(gx, gz); return c >= 0 ? MathD.Clamp01(_w.Fields.Nutrients[c] / Math.Max(_w.Fields.Nutrients.Max, 1e-9)) : 0; }
    public bool IsObstacle(int gx, int gz) => DepthAt(gx, gz) <= 0;
    public double SurfaceShadeAt(int gx, int gz) => MathD.Clamp01(
        0.65 * _w.Coverage.SurfaceFloat.GetB(gx, gz) + 0.5 * _w.Coverage.AlgaeFloat.GetB(gx, gz));
}
