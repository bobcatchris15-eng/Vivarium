using System.Diagnostics;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>Per-step cost breakdown for <see cref="AlgaeRules.Step"/> (docs/overhaul/growth_models.md §15.1).</summary>
public readonly record struct AlgaeStepStats(double BedMs, double FloatGrowMs, double FloatAdvectMs, int FloatSubsteps);

/// <summary>
/// Benthic algae film (<c>AlgaeBed</c>) and floating filamentous mats (<c>AlgaeFloat</c>) over the aquatic
/// coverage layers (docs/overhaul/growth_models.md §15.1). Pure step function driven by <see cref="IAquaticEnv"/>;
/// no world wiring (Aq-2 derives the <c>Biofilm</c> field and wires grazing).
/// </summary>
public static class AlgaeRules
{
    /// <summary>Sole occupant id for both algae layers — no species variation yet (Aq-2).</summary>
    public const byte OccupantId = 1;

    public static AlgaeStepStats Step(CoverageLayer bed, CoverageLayer floatLayer, IAquaticEnv env, AlgaeBedParams bp, AlgaeFloatParams fp, GridBounds domain, double dtDays, long step, ulong seed)
    {
        var sw = Stopwatch.StartNew();
        StepBed(bed, floatLayer, env, bp, domain, dtDays);
        double bedMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

        GrowFloat(floatLayer, env, fp, domain, dtDays);
        double floatGrowMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

        int substeps = SurfaceFloatRules.Advect(floatLayer, env, domain, dtDays, windX: 0, windZ: 0);
        double advectMs = sw.Elapsed.TotalMilliseconds;

        return new AlgaeStepStats(bedMs, floatGrowMs, advectMs, substeps);
    }

    // ------------------------------------------------------------------ §15.1 bed film: light-at-depth, nutrients, scour, detachment

    private static void StepBed(CoverageLayer bed, CoverageLayer floatLayer, IAquaticEnv env, AlgaeBedParams p, GridBounds d, double dtDays)
    {
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            double depth = env.DepthAt(gx, gz);
            if (depth <= 0)
            {
                if (bed.GetOcc(gx, gz) != 0) { bed.SetOcc(gx, gz, 0); bed.SetB(gx, gz, 0f); }
                continue;
            }

            double b = bed.GetB(gx, gz);
            if (b <= 0) continue; // no spontaneous bloom; keep unseeded water out of the sparse tile map
            double shade = env.SurfaceShadeAt(gx, gz);
            double lightAtDepth = env.LightAt(gx, gz) * (1 - shade) * Math.Exp(-p.LightAtten * depth); // Beer-Lambert
            double gL = lightAtDepth / (lightAtDepth + p.LightHalfSat);
            double n = env.NutrientsAt(gx, gz);
            double gN = n / (n + p.NutrientHalfSat);

            double flowSpeed = env.FlowAt(gx, gz).Length;
            double scour = flowSpeed > p.ScourFlowThreshold ? p.ScourRate * (flowSpeed - p.ScourFlowThreshold) : 0;
            double grazePerDay = p.GrazeRate; // hook: Aq-2 wires fauna consumption here

            double db = (p.GrowthRate * gL * gN * b * (1 - b) - (scour + grazePerDay) * b) * dtDays;
            double nb = Math.Clamp(b + db, 0, 1);

            double detach = 0;
            if (nb > p.DetachThickness && flowSpeed <= p.ScourFlowThreshold)
            {
                detach = Math.Min(p.DetachRate * (nb - p.DetachThickness) * dtDays, nb);
                nb -= detach;
            }

            bed.SetB(gx, gz, (float)nb);
            bed.SetOcc(gx, gz, nb > 1e-6 ? OccupantId : (byte)0);

            if (detach > 0)
            {
                double curFloat = floatLayer.GetB(gx, gz);
                double newFloat = Math.Clamp(curFloat + detach, 0, 1);
                floatLayer.SetB(gx, gz, (float)newFloat);
                floatLayer.SetOcc(gx, gz, OccupantId);
            }
        }
    }

    // ------------------------------------------------------------------ §15.1 floating mats: slow autonomous growth in still water
    // (advection, shared with duckweed, handles the drift-to-margin part; see SurfaceFloatRules.Advect)

    private static void GrowFloat(CoverageLayer floatLayer, IAquaticEnv env, AlgaeFloatParams p, GridBounds d, double dtDays)
    {
        for (int gz = d.MinGz; gz <= d.MaxGz; gz++)
        for (int gx = d.MinGx; gx <= d.MaxGx; gx++)
        {
            if (env.DepthAt(gx, gz) <= 0 || env.IsObstacle(gx, gz))
            {
                if (floatLayer.GetOcc(gx, gz) != 0) { floatLayer.SetOcc(gx, gz, 0); floatLayer.SetB(gx, gz, 0f); }
                continue;
            }

            double b = floatLayer.GetB(gx, gz);
            if (b <= 0) continue;

            double flowSpeed = env.FlowAt(gx, gz).Length;
            if (flowSpeed > p.StillFlowThreshold) continue; // established mats only grow (not disperse) in still water

            double n = env.NutrientsAt(gx, gz);
            double gN = n / (n + p.NutrientHalfSat);
            double db = p.GrowthRate * gN * b * (1 - b) * dtDays;
            double nb = Math.Clamp(b + db, 0, 1);
            if (nb == b) continue;

            floatLayer.SetB(gx, gz, (float)nb);
            floatLayer.SetOcc(gx, gz, OccupantId);
        }
    }
}
