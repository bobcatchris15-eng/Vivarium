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
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            if (env.DepthAt(gx, gz) <= 0 || env.IsObstacle(gx, gz))
            {
                if (layer.GetOcc(gx, gz) != 0) { layer.SetOcc(gx, gz, 0); layer.SetB(gx, gz, 0f); }
                continue;
            }

            double b = layer.GetB(gx, gz);
            double gL = LightResponse(env.LightAt(gx, gz), p);
            double gN = NutrientResponse(env.NutrientsAt(gx, gz), p);
            double db = p.GrowthRate * gL * gN * b * (1 - b / p.MaxDensity) * dtDays;
            double nb = Math.Clamp(b + db, 0, p.MaxDensity);
            if (nb == b) continue;

            layer.SetB(gx, gz, (float)nb);
            layer.SetOcc(gx, gz, nb > 1e-6 ? OccupantId : (byte)0);
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
    public static int Advect(CoverageLayer layer, IAquaticEnv env, GridBounds d, double dtDays, double windX, double windZ)
    {
        double dtSeconds = dtDays * AquaticConst.SecondsPerDay;
        double cell = CoverageSpec.CellSize;
        int w = d.Width, h = d.Height;
        int n = w * h;

        var rho = new double[n];
        var blocked = new bool[n];
        var vx = new double[n];
        var vz = new double[n];
        double maxSpeed = 0;

        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            int idx = d.Index(gx, gz);
            rho[idx] = layer.GetB(gx, gz);
            bool block = env.DepthAt(gx, gz) <= 0 || env.IsObstacle(gx, gz);
            blocked[idx] = block;
            if (block) continue;

            var v = env.FlowAt(gx, gz) + new Vec2(windX, windZ);
            vx[idx] = v.X; vz[idx] = v.Z;
            double s = v.Length;
            if (s > maxSpeed) maxSpeed = s;
        }

        int substeps = maxSpeed > 1e-9
            ? Math.Clamp((int)Math.Ceiling(maxSpeed * dtSeconds / (AquaticConst.CflSafety * cell)), 1, AquaticConst.MaxSubsteps)
            : 1;
        double subDt = dtSeconds / substeps;

        var next = new double[n];
        for (int s = 0; s < substeps; s++)
        {
            Array.Copy(rho, next, n);

            // faces along x
            for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
            for (int gx = d.MinGx; gx < d.MaxGx; gx++)
            {
                int a = d.Index(gx, gz), b = d.Index(gx + 1, gz);
                if (blocked[a] || blocked[b]) continue;
                double u = 0.5 * (vx[a] + vx[b]);
                double flux = u >= 0 ? rho[a] * u : rho[b] * u;
                double amt = flux * subDt / cell;
                next[a] -= amt; next[b] += amt;
            }

            // faces along z
            for (int gz = d.MinGz; gz < d.MaxGz; gz++)
            for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
            {
                int a = d.Index(gx, gz), b = d.Index(gx, gz + 1);
                if (blocked[a] || blocked[b]) continue;
                double u = 0.5 * (vz[a] + vz[b]);
                double flux = u >= 0 ? rho[a] * u : rho[b] * u;
                double amt = flux * subDt / cell;
                next[a] -= amt; next[b] += amt;
            }

            for (int i = 0; i < n; i++) if (next[i] < 0) next[i] = 0; // numerical guard only; CFL keeps this a no-op in practice
            Array.Copy(next, rho, n);
        }

        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            int idx = d.Index(gx, gz);
            if (blocked[idx]) continue;
            double nb = rho[idx];
            if (nb <= 0 && layer.GetOcc(gx, gz) == 0) continue; // keep empty water out of the sparse tile map
            layer.SetB(gx, gz, (float)nb);
            layer.SetOcc(gx, gz, nb > 1e-6 ? OccupantId : (byte)0);
        }

        return substeps;
    }
}
