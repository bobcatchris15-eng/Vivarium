using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Shadebell: broad-leaved understory shrub. One trunk (order 0), three tiers of two forks (order 1), two side
/// canes per fork (order 2). Leaves are broad near-orbicular blades on petioles in horizontal whorls (3 per node,
/// 137.5 degree divergence between nodes, as in the legacy builder). Pale pendant bells hang from pedicels
/// attached to the order-2 canes.
/// Sizes from game/content/flora/shadebell.json: height 2.35 m, canopy radius 1.0 m.
/// </summary>
public sealed class ShadebellGenerator : PlanGenerator
{
    public override string SpeciesId => "shadebell";

    protected override void Design(PlanBuilder b)
    {
        var up = Vec3.Up;
        ushort leaf = b.AddShape(new OrganShape
        {
            Name = "shadebell_leaf", Type = OrganType.Leaf,
            WidthProfile = PlantShapes.Profile(t => 0.5 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.55)), 0.6)),
            LengthToWidth = 1.15f, MidribCurl = 0.10f, CrossCurl = 0.12f, Twist = 0.05f,
            EdgeWave = 0.03f, EdgeWaveFreq = 2.5f, Serration = 0f,
            Thickness = 0.0006f,
            ColorAdaxial = PlantShapes.Srgb(0.14, 0.32, 0.23), ColorAbaxial = PlantShapes.Srgb(0.31, 0.51, 0.34),
            Transmission = 0.25f, Roughness = 0.35f, Wax = 0.6f,
        });
        ushort bell = b.AddShape(new OrganShape
        {
            Name = "shadebell_bell", Type = OrganType.Flower,
            LengthToWidth = 1.2f, Thickness = 0.0005f,
            ColorAdaxial = PlantShapes.Srgb(0.92, 0.89, 0.80), ColorAbaxial = PlantShapes.Srgb(0.85, 0.48, 0.44),
            Transmission = 0.45f, Roughness = 0.5f,
        });

        int trunk = b.AddAxis(-1, 0, up + new Vec3(0.08, 0, 0.04), 1.5, new Vec3(0.12, 0, 0.06), 0.024, 0.014, 0,
            Tissue.Wood, 0, 0.5);

        for (int tier = 0; tier < 3; tier++)
        {
            double tierT = 0.45 + 0.2 * tier;
            for (int k = 0; k < 2; k++)
            {
                double az = tier * 2.399963 + k * Math.PI + b.Range(-0.2, 0.2);
                var forkDir = PlantMath.Horizontal(az) * 0.7 + up * 0.75;
                double forkLen = (0.7 - 0.08 * tier) * b.Range(0.92, 1.08);
                int fork = b.AddAxis(trunk, tierT, forkDir, forkLen, up * -0.2, 0.017, 0.009, 1, Tissue.Wood,
                    0.03 * b.Draw(), 0.3);

                // Whorl on the fork itself.
                AddWhorl(b, leaf, fork, 0.5, az, 3, 0.8);

                for (int s2 = 0; s2 < 2; s2++)
                {
                    double t2 = 0.5 + 0.4 * s2;
                    double baz = az + (s2 == 0 ? 1.2 : -1.2);
                    var brDir = PlantMath.Horizontal(baz) * 0.5 + up * 0.85;
                    int br = b.AddAxis(fork, t2, brDir, 0.38 * b.Range(0.9, 1.1), up * 0.1, 0.008, 0.004, 2,
                        Tissue.Wood, 0.03 * b.Draw(), 0.2);

                    for (int wh = 0; wh < 3; wh++)
                    {
                        AddWhorl(b, leaf, br, 0.3 + 0.25 * wh, baz + wh * 2.399963, 3, 1.0);
                    }

                    // Two pendant bells per side cane.
                    for (int pd = 0; pd < 2; pd++)
                    {
                        double pt = 0.45 + 0.4 * pd;
                        double pa = baz + pd * 2.1;
                        var pedDir = Vec3.Down * 0.9 + PlantMath.Horizontal(pa) * 0.35;
                        int ped = b.AddAxis(br, pt, pedDir, 0.045, Vec3.Zero, 0.0014, 0.0010, 3, Tissue.Green,
                            0.03 * b.Draw(), 0.15);
                        b.AddFlat(ped, 1.0, Vec3.Down * 0.95 + PlantMath.Horizontal(pa) * 0.2, 0.024, 0.011, 0.009,
                            OrganType.Flower, bell, 4, 0.03 * b.Draw(), 0.2);
                    }
                }
            }
        }
    }

    /// <summary>Horizontal whorl of broad blades on petioles at axis parameter t.</summary>
    private static void AddWhorl(PlanBuilder b, ushort leaf, int axis, double t, double az0, int count, double scale)
    {
        for (int i = 0; i < count; i++)
        {
            double az = az0 + i * Math.Tau / count + b.Range(-0.1, 0.1);
            var h = PlantMath.Horizontal(az);
            int pet = b.AddAxis(axis, t, h * 0.8 + Vec3.Up * 0.45, 0.05 * scale, Vec3.Zero, 0.0035, 0.002, 3,
                Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
            double lLen = b.Range(0.17, 0.21) * scale;
            b.AddFlat(pet, 1.0, h * 0.95 + Vec3.Up * 0.25, lLen, lLen * 0.5, 0.0006, OrganType.Leaf, leaf, 4,
                0.03 * b.Draw(), 0.14);
        }
    }
}
