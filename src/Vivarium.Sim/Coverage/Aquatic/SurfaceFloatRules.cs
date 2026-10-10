using System.Diagnostics;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>Per-step cost breakdown for <see cref="SurfaceFloatRules.Step"/> (docs/overhaul/growth_models.md §15.2).</summary>
public readonly record struct SurfaceFloatStats(double GrowthMs, double AdvectMs, int Substeps);

/// <summary>
/// Duckweed growth (logistic budding) and flow/wind advection over the <c>SurfaceFloat</c> coverage layer
/// (docs/overhaul/growth_models.md §15.2). Pure step function: no world wiring, driven entirely by
/// <see cref="IAquaticEnv"/>, so the growth lab can prove it against synthetic ponds.
/// </summary>
public static class SurfaceFloatRules
{
    /// <summary>The layer's sole occupant id — duckweed cover has no species variation yet (Aq-2).</summary>
    public const byte OccupantId = 1;

    public static SurfaceFloatStats Step(CoverageLayer layer, IAquaticEnv env, DuckweedParams p, GridBounds domain, double dtDays, long step, ulong seed, double? advectionDays = null)
    {
        var sw = Stopwatch.StartNew();
        Grow(layer, env, p, domain, dtDays);
        double growthMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

        int substeps = Advect(layer, env, domain, advectionDays ?? dtDays, p.WindX, p.WindZ);
        double advectMs = sw.Elapsed.TotalMilliseconds;

        return new SurfaceFloatStats(growthMs, advectMs, substeps);
    }

    // ------------------------------------------------------------------ §15.2 logistic budding

    private static void Grow(CoverageLayer layer, IAquaticEnv env, DuckweedParams p, GridBounds d, double dtDays)
    {
        // Walk allocated tiles directly instead of the full bounding rectangle (see AlgaeRules.StepBed):
        // unallocated cells are implicitly empty and Grow is a no-op on them either way.
        foreach (var tile in layer.Tiles)
        {
            int baseGx = tile.Ti * CoverageSpec.TileEdge, baseGz = tile.Tj * CoverageSpec.TileEdge;
            for (int li = 0; li < CoverageTile.N; li++)
            {
                int gx = baseGx + li % CoverageSpec.TileEdge, gz = baseGz + li / CoverageSpec.TileEdge;
                if (!d.Contains(gx, gz)) continue;

                if (env.DepthAt(gx, gz) <= 0 || env.IsObstacle(gx, gz))
                {
                    if (tile.Occ[li] != 0) { tile.Occ[li] = 0; tile.B[li] = 0f; tile.Active = true; tile.Touch(); }
                    continue;
                }

                double b = tile.B[li];
                double gL = LightResponse(env.LightAt(gx, gz), p);
                double gN = NutrientResponse(env.NutrientsAt(gx, gz), p);
                double db = p.GrowthRate * gL * gN * b * (1 - b / p.MaxDensity) * dtDays;
                double nb = Math.Clamp(b + db, 0, p.MaxDensity);
                if (nb == b) continue;

                tile.B[li] = (float)nb; tile.Active = true; tile.Touch();
                byte occ = nb > 1e-6 ? OccupantId : (byte)0;
                if (tile.Occ[li] != occ) tile.Occ[li] = occ;
            }
        }
    }

    private static double LightResponse(double light, DuckweedParams p) => light / (light + p.KLight);
    private static double NutrientResponse(double n, DuckweedParams p) => n / (n + p.KNutrient);

    // ------------------------------------------------------------------ §15.2 upwind donor-cell advection
    // Flux-form update on a dense per-domain buffer: every internal face contributes +flux to one cell and
    // -flux to its neighbour (or is skipped entirely when either side is land/obstacle), so total mass over
    // the domain is exactly conserved regardless of grid content. CFL-limited sub-stepping keeps it stable at
    // the lab's dt even for fast flows on the fine (2 cm) coverage grid. Shared by AlgaeRules for AlgaeFloat.

    /// <summary>Advects <paramref name="layer"/>'s biomass by flow + a constant wind term, upwind donor-cell,
    /// mass-conserving, sub-stepped to satisfy CFL. Returns the substep count used.</summary>
    // Reused across calls (single-threaded, one call completes before the next starts) so a dense advection
    // buffer isn't freshly allocated and GC'd on every tick — these dominate large-pond frame time otherwise.
    [ThreadStatic] private static double[]? _rho, _vx, _vz, _next, _fx, _fz, _amtX, _amtZ;
    [ThreadStatic] private static bool[]? _blocked;

    private static T[] Rent<T>(ref T[]? buf, int n)
    {
        if (buf == null || buf.Length < n) buf = new T[n];
        return buf;
    }

    public static int Advect(CoverageLayer layer, IAquaticEnv env, GridBounds d, double dtDays, double windX, double windZ)
    {
        double dtSeconds = dtDays * AquaticConst.SecondsPerDay;
        double cell = CoverageSpec.CellSize;
        int w = d.Width, h = d.Height;
        int n = w * h;

        var rho = Rent(ref _rho, n);
        var blocked = Rent(ref _blocked, n);
        var vx = Rent(ref _vx, n);
        var vz = Rent(ref _vz, n);
        double maxSpeed = 0;

        // Tile pointer cached across the row: consecutive gx share a tile most of the time (32 cells per
        // tile), so this turns ~n dictionary lookups into ~n/32 of them. gx/gz -> (ti,li) tracked by
        // increment instead of CoverageSpec.TileOf/LocalIndex's div/mod per cell — those add up at this
        // cell count (a fine 2 cm coverage grid over a whole pond can be hundreds of thousands of cells).
        int edge = CoverageSpec.TileEdge;
        CoverageTile? readTile = null; int rTi = int.MinValue, rTj = int.MinValue;

        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        {
            int tj = CoverageSpec.FloorDiv(gz, edge), lz = CoverageSpec.FloorMod(gz, edge);
            int ti = CoverageSpec.FloorDiv(d.MinGx, edge), lx = CoverageSpec.FloorMod(d.MinGx, edge);
            int idx = d.Index(d.MinGx, gz);
            for (int gx = d.MinGx; gx <= d.MaxGx; gx++, idx++)
            {
                if (ti != rTi || tj != rTj) { layer.TryGetTile(ti, tj, out readTile); rTi = ti; rTj = tj; }
                rho[idx] = readTile != null ? readTile.B[lz * edge + lx] : 0f;
                bool block = env.DepthAt(gx, gz) <= 0 || env.IsObstacle(gx, gz);
                blocked[idx] = block;
                if (!block)
                {
                    var v = env.FlowAt(gx, gz) + new Vec2(windX, windZ);
                    vx[idx] = v.X; vz[idx] = v.Z;
                    double s = v.Length;
                    if (s > maxSpeed) maxSpeed = s;
                }

                lx++; if (lx == edge) { lx = 0; ti++; }
            }
        }

        int substeps = maxSpeed > 1e-9
            ? Math.Clamp((int)Math.Ceiling(maxSpeed * dtSeconds / (AquaticConst.CflSafety * cell)), 1, AquaticConst.MaxSubsteps)
            : 1;
        double subDt = dtSeconds / substeps;

        // Face velocities are constant across substeps: precompute them once, with NaN marking a closed face
        // (either side blocked, or on the domain edge). fx[a] is the face between cell a and its +x neighbour,
        // fz[a] the face between cell a and its +z neighbour.
        var fx = Rent(ref _fx, n);
        var fz = Rent(ref _fz, n);
        for (int j = 0; j < h; j++)
        {
            int row = j * w;
            for (int i = 0; i < w; i++)
            {
                int a = row + i;
                fx[a] = i + 1 < w && !blocked[a] && !blocked[a + 1] ? 0.5 * (vx[a] + vx[a + 1]) : double.NaN;
                fz[a] = j + 1 < h && !blocked[a] && !blocked[a + w] ? 0.5 * (vz[a] + vz[a + w]) : double.NaN;
            }
        }

        // Gather form of the former scatter loops (all x faces row-major, then all z faces, then the clamp).
        // Per cell it applies the same operations in the same order: += the -x face, -= the +x face, += the -z
        // face, -= the +z face, clamp. Results are bit-identical, without a full copy-in/copy-out per substep.
        var amtX = Rent(ref _amtX, n);
        var amtZ = Rent(ref _amtZ, n);
        var next = Rent(ref _next, n);
        for (int s = 0; s < substeps; s++)
        {
            for (int a = 0; a < n; a++)
            {
                double u = fx[a];
                if (!double.IsNaN(u)) amtX[a] = (u >= 0 ? rho[a] * u : rho[a + 1] * u) * subDt / cell;
                u = fz[a];
                if (!double.IsNaN(u)) amtZ[a] = (u >= 0 ? rho[a] * u : rho[a + w] * u) * subDt / cell;
            }

            for (int j = 0; j < h; j++)
            {
                int row = j * w;
                for (int i = 0; i < w; i++)
                {
                    int a = row + i;
                    double v = rho[a];
                    if (i > 0 && !double.IsNaN(fx[a - 1])) v += amtX[a - 1];
                    if (!double.IsNaN(fx[a])) v -= amtX[a];
                    if (j > 0 && !double.IsNaN(fz[a - w])) v += amtZ[a - w];
                    if (!double.IsNaN(fz[a])) v -= amtZ[a];
                    next[a] = v < 0 ? 0 : v; // numerical guard only; CFL keeps this a no-op in practice
                }
            }
            (rho, next) = (next, rho);
        }

        CoverageTile? writeTile = null; int wTi = int.MinValue, wTj = int.MinValue;
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        {
            int tj = CoverageSpec.FloorDiv(gz, edge), lz = CoverageSpec.FloorMod(gz, edge);
            int ti = CoverageSpec.FloorDiv(d.MinGx, edge), lx = CoverageSpec.FloorMod(d.MinGx, edge);
            int idx = d.Index(d.MinGx, gz);
            for (int gx = d.MinGx; gx <= d.MaxGx; gx++, idx++)
            {
                if (!blocked[idx])
                {
                    if (ti != wTi || tj != wTj) { layer.TryGetTile(ti, tj, out writeTile); wTi = ti; wTj = tj; }
                    double nb = rho[idx];
                    int li = lz * edge + lx;
                    byte curOcc = writeTile != null ? writeTile.Occ[li] : (byte)0;
                    if (!(nb <= 0 && curOcc == 0))
                    {
                        if (writeTile == null) { writeTile = layer.GetOrCreateTile(ti, tj); wTi = ti; wTj = tj; }
                        var t = writeTile;
                        if (t.B[li] != (float)nb) { t.B[li] = (float)nb; t.Active = true; t.Touch(); }
                        byte occ = nb > 1e-6 ? OccupantId : (byte)0;
                        if (t.Occ[li] != occ) { t.Occ[li] = occ; t.Active = true; t.Touch(); }
                    }
                }

                lx++; if (lx == edge) { lx = 0; ti++; }
            }
        }

        return substeps;
    }
}
