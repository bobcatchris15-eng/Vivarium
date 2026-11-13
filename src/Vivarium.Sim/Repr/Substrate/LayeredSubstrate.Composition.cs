using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

public sealed partial class LayeredSubstrate
{
    /// Composition of one horizon at one node, from the node's drivers (drainage, organic input, steepness).
    /// Writes TabStride floats at dst[off..]. Fractions are normalised so CF+Sand+Silt+Clay+Organic == 1.
    /// h: 0 Litter, 1 Humus, 2 MineralSoil, 3 Weathered, 4 Bedrock.
    internal static void Compose(int h, double drain, double org, double steep, Vec3 parentHue, float[] dst, int off)
    {
        double d = MathD.Clamp01(drain), o = MathD.Clamp01(org), st = MathD.Clamp01(steep);
        // Mineral texture: free-draining = sandier; waterlogged = more clay and silt.
        double sandM = 0.15 + 0.55 * d, clayM = 0.30 - 0.22 * d, siltM = 1 - sandM - clayM;
        double cf, og, comp;
        switch (h)
        {
            case 0: cf = 0.02; og = 0.85; comp = 0.05; break;
            case 1: cf = 0.05; og = 0.45 + 0.2 * o; comp = 0.2; break;
            case 2: cf = 0.12 + 0.10 * st; og = 0.03 + 0.05 * o; comp = 0.5; break;
            case 3: cf = 0.35 + 0.2 * st; og = 0.01; comp = 0.65; break;
            default: cf = 1; og = 0; comp = 1; sandM = 0; siltM = 0; clayM = 0; break;
        }
        double fine = 1 - cf - og;
        double sand = fine * sandM, silt = fine * siltM, clay = fine * clayM;
        double total = cf + sand + silt + clay + og;
        cf /= total; sand /= total; silt /= total; clay /= total; og /= total;

        double phi, agg, str, fc, wp, logK, colo;
        if (h == 4)
        {
            phi = 0.02; agg = 0.001; str = 1; fc = 0.005; wp = 0.002; logK = -9; colo = 0.85;
        }
        else
        {
            phi = MathD.Clamp(fine * (0.42 - 0.12 * sandM + 0.04 * clayM) * (1 - 0.45 * comp) + og * 0.6 + cf * 0.1, 0, 0.7);
            agg = MathD.Clamp(0.0005 + 0.03 * og * (1 - sandM), 0.0005, 0.05);
            str = MathD.Clamp01(0.15 + 0.5 * clayM + 0.35 * comp - 0.2 * og);
            fc = MathD.Clamp(fine * (sandM * 0.10 + siltM * 0.27 + clayM * 0.40) + 0.35 * og, 0, phi);
            wp = MathD.Clamp(fine * (sandM * 0.04 + siltM * 0.12 + clayM * 0.25) + 0.15 * og, 0, fc);
            double kr = MathD.Clamp(Math.Pow(phi / 0.4, 3), 0.05, 3);
            logK = -4 - 3 * clayM + Math.Log10(kr);
            colo = h switch
            {
                0 => 0.10 + 0.10 * (1 - sandM),
                1 => 0.55 + 0.20 * o,
                2 => 0.25 + 0.30 * clayM,
                _ => 0.60 + 0.20 * (1 - sandM),
            };
        }
        // Organic matter darkens the parent-rock hue toward humus brown.
        double tOrg = h == 4 ? 0 : MathD.Clamp01(og * 0.9);
        double hr = parentHue.X + (0.10 - parentHue.X) * tOrg;
        double hg = parentHue.Y + (0.07 - parentHue.Y) * tOrg;
        double hb = parentHue.Z + (0.05 - parentHue.Z) * tOrg;

        dst[off + F_CF] = (float)cf;
        dst[off + F_SAND] = (float)sand;
        dst[off + F_SILT] = (float)silt;
        dst[off + F_CLAY] = (float)clay;
        dst[off + F_ORG] = (float)og;
        dst[off + F_PHI] = (float)phi;
        dst[off + F_AGG] = (float)agg;
        dst[off + F_COMP] = (float)comp;
        dst[off + F_STR] = (float)str;
        dst[off + F_FC] = (float)fc;
        dst[off + F_WP] = (float)wp;
        dst[off + F_LOGK] = (float)logK;
        dst[off + F_COL] = (float)MathD.Clamp01(colo);
        dst[off + F_HR] = (float)hr;
        dst[off + F_HG] = (float)hg;
        dst[off + F_HB] = (float)hb;
    }
}
