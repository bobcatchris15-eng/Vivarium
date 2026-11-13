using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Glowbriar: low woody shrub with arching canes from a short stool. Architecture: 5 canes (order 1), 3 side
/// branches per cane (order 2), 2 green twigs per branch (order 3). Phyllotaxy is OPPOSITE DECUSSATE (pairs
/// rotated 90 degrees per node, as in the legacy cane builder) with leaves on petioles at nodes of the twigs.
/// Recurved thorns on canes; one pendant emissive chalice per branch (legacy: 16-22 chalices).
/// Sizes from game/content/flora/glowbriar.json: height 1.12 m, canopy radius 0.95 m.
/// </summary>
public sealed class GlowbriarGenerator : PlanGenerator
{
    public override string SpeciesId => "glowbriar";

    protected override void Design(PlanBuilder b)
    {
        var up = Vec3.Up;
        ushort leafShape = b.AddShape(new OrganShape
        {
            Name = "glowbriar_leaf", Type = OrganType.Leaf,
            WidthProfile = PlantShapes.Profile(t => 0.2 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.7)), 0.9)),
            LengthToWidth = 2.5f, MidribCurl = 0.22f, CrossCurl = 0.25f, Twist = 0.12f,
            EdgeWave = 0.012f, EdgeWaveFreq = 5f, Serration = 0.012f, SerrationFreq = 9f,
            Thickness = 0.0004f,
            ColorAdaxial = PlantShapes.Srgb(0.12, 0.28, 0.27), ColorAbaxial = PlantShapes.Srgb(0.32, 0.58, 0.47),
            Transmission = 0.3f, Roughness = 0.5f, Wax = 0.2f,
        });
        ushort chalice = b.AddShape(new OrganShape
        {
            Name = "glowbriar_chalice", Type = OrganType.Flower,
            LengthToWidth = 1.5f, Thickness = 0.0006f,
            ColorAdaxial = PlantShapes.Srgb(0.36, 0.9, 0.66), ColorAbaxial = PlantShapes.Srgb(0.2, 0.6, 0.5),
            Transmission = 0.5f, Roughness = 0.35f, Wax = 0.3f,
        });

        int stool = b.AddAxis(-1, 0, up + new Vec3(0.05, 0, 0.03), 0.12, Vec3.Zero, 0.020, 0.017, 0, Tissue.Wood,
            0, 0.25);

        for (int c = 0; c < 5; c++)
        {
            double az = c * Math.Tau / 5 + b.Range(-0.3, 0.3);
            var caneDir = PlantMath.Horizontal(az) * 0.62 + up * 0.78;
            int cane = b.AddAxis(stool, b.Range(0.35, 0.6), caneDir, b.Range(0.9, 1.05), up * -0.3,
                0.0165, 0.0055, 1, Tissue.Wood, 0.03 * b.Draw(), 0.2);

            // Recurved thorns along the cane internodes.
            for (int th = 0; th < 8; th++)
            {
                double tt = 0.15 + 0.8 * (th + 0.6 * b.Draw()) / 8.0;
                double side = (th & 1) == 0 ? 1 : -1;
                var tdir = (PlantMath.Horizontal(az) * 0.5 + PlantMath.Horizontal(az + Math.PI / 2) * (0.7 * side)
                    + up * 0.3).Normalized();
                b.AddAxis(cane, tt, tdir, 0.016, Vec3.Zero, 0.0035, 0.0003, 2, Tissue.Bark, 0.03 * b.Draw(), 0.12);
            }

            for (int k = 0; k < 3; k++)
            {
                double baz = b.Range(0, Math.Tau);
                var bo = PlantMath.Horizontal(baz);
                var brDir = bo * 0.55 + up * 0.8;
                int br = b.AddAxis(cane, 0.3 + 0.25 * k + b.Range(-0.04, 0.04), brDir,
                    0.24 * (1 - 0.15 * k) * b.Range(0.9, 1.1), up * 0.08, 0.0095, 0.0035, 2, Tissue.Wood,
                    0.03 * b.Draw(), 0.18);

                for (int tw = 0; tw < 2; tw++)
                {
                    double twigT = tw == 0 ? 0.45 : 0.85;
                    var twigDir = PlantMath.Horizontal(baz + (tw == 0 ? 0.9 : -0.9)) * 0.7 + up * 0.7;
                    int twig = b.AddAxis(br, twigT, twigDir, 0.13, up * 0.05, 0.0045, 0.0016, 3, Tissue.Green,
                        0.03 * b.Draw(), 0.14);

                    // Internodes shorten toward the tip: nodes sit closer together near the end of the twig.
                    for (int nd = 0; nd < 4; nd++)
                    {
                        double nt = 0.25 + 0.2 * nd;
                        for (int sd = 0; sd < 2; sd++)
                        {
                            double la = nd * (Math.PI / 2) + sd * Math.PI + baz;
                            var h = PlantMath.Horizontal(la);
                            int pet = b.AddAxis(twig, nt, h * 0.6 + up * 0.8, 0.02, Vec3.Zero, 0.0016, 0.0010, 4,
                                Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
                            double ll = b.Range(0.09, 0.12);
                            b.AddFlat(pet, 1.0, h * 0.9 + up * 0.4, ll, ll * 0.2, 0.0004, OrganType.Leaf, leafShape,
                                5, 0.03 * b.Draw(), 0.14);
                        }
                    }
                }

                // One pendant luminous chalice per branch, hanging from a pedicel on the branch.
                int ped = b.AddAxis(br, 0.7, Vec3.Down * 0.95 + bo * 0.3, 0.035, Vec3.Zero, 0.0011, 0.0008, 5,
                    Tissue.Green, 0.03 * b.Draw(), 0.12);
                b.AddFlat(ped, 1.0, Vec3.Down * 0.9 + bo * 0.3, 0.02, 0.009, 0.008, OrganType.Flower, chalice, 6,
                    0.03 * b.Draw(), 0.15, OrganFlags.Emissive);
            }
        }
    }
}
