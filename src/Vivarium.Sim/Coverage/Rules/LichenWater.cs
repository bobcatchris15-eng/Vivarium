namespace Vivarium.Sim.Coverage.Rules;

/// <summary>
/// Private copy of the shared water-balance model (docs/overhaul/growth_models.md §3) for lichen. Kept separate
/// from any moss implementation deliberately: a later refactor may merge them once both exist, but G5a must not
/// assume or depend on a shared file another worker owns.
/// </summary>
public static class LichenWater
{
    /// <summary>
    /// Advances water content one step toward its moisture target (§3):
    /// wets fast (k_wet), dries slower in humid air (k_dry scaled by 1-humidity).
    /// </summary>
    public static byte StepWater(byte w0, double moistureTarget, double humidity, double dtDays, double kWet, double kDry)
    {
        double w = w0 / 255.0;
        double target = Math.Clamp(moistureTarget, 0, 1);
        if (target > w) w += (target - w) * (1 - Math.Exp(-kWet * dtDays));
        else w -= (w - target) * (1 - Math.Exp(-kDry * (1 - humidity) * dtDays));
        w = Math.Clamp(w, 0, 1);
        return (byte)Math.Round(w * 255.0);
    }

    /// <summary>Water growth multiplier g_W = smoothstep(wMin, wOpt, W) (§3).</summary>
    public static double GrowthWaterFactor(double w01, double wMin, double wOpt)
    {
        if (wOpt <= wMin) return w01 >= wOpt ? 1.0 : 0.0;
        double t = Math.Clamp((w01 - wMin) / (wOpt - wMin), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Light growth multiplier g_L = L / (L + K_L) (§3).</summary>
    public static double GrowthLightFactor(double light, double kLight) => light / (light + Math.Max(kLight, 1e-6));

    public static double W01(byte w) => w / 255.0;
}
