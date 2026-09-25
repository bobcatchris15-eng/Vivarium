namespace Vivarium.Sim.Coverage.Rules;

/// <summary>
/// Shared poikilohydric water-balance physiology (docs/overhaul/growth_models.md §3): wets fast, dries slower
/// in humid air, and gates growth via smoothstep(W_min, W_opt, W) x light response. Pure functions over
/// species parameters so both moss and (later) lichen rules can reuse it.
/// </summary>
public static class WaterBalance
{
    /// <summary>Advances water content 0..1 one step toward <paramref name="moistureTarget"/> (§3).</summary>
    public static double StepWater(double w01, double moistureTarget, double humidity01, MatParams p, double dtDays)
    {
        double target = Math.Clamp(moistureTarget, 0, 1);
        double w = w01;
        if (target > w)
            w += (target - w) * (1 - Math.Exp(-p.KWet * dtDays));
        else
            w -= (w - target) * (1 - Math.Exp(-p.KDry * (1 - Math.Clamp(humidity01, 0, 1)) * dtDays));
        return Math.Clamp(w, 0, 1);
    }

    /// <summary>g_W = smoothstep(W_min, W_opt, W).</summary>
    public static double GrowthMultiplierWater(double w01, MatParams p) => Smoothstep(p.WMin, p.WOpt, w01);

    /// <summary>g_L = L / (L + K_L), reduced above the species' photoinhibition cap.</summary>
    public static double GrowthMultiplierLight(double light01, MatParams p)
    {
        double l = Math.Clamp(light01, 0, 1);
        double g = l / (l + Math.Max(1e-9, p.KLight));
        if (p.LightMax > 0 && p.LightMax < 1 && l > p.LightMax)
            g *= Math.Max(0, 1 - (l - p.LightMax) / Math.Max(1e-9, 1 - p.LightMax));
        return g;
    }

    private static double Smoothstep(double lo, double hi, double x)
    {
        if (hi <= lo) return x >= hi ? 1 : 0;
        double t = Math.Clamp((x - lo) / (hi - lo), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
