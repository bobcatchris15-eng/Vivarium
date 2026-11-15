using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Shadebell: broad-leaved understory shrub. One trunk (order 0), six staggered forks (order 1), two side
/// canes per fork (order 2). Leaves are broad near-orbicular blades on petioles in horizontal whorls (3 per node,
/// 137.5 degree divergence between nodes, as in the legacy builder). Pale pendant bells hang from pedicels
/// attached to the order-2 canes.
/// Sizes from game/content/flora/shadebell.json: height 2.35 m, canopy radius 1.0 m.
/// </summary>
public sealed class ShadebellGenerator : PlanGenerator
{
    public sealed record FormSettings
    {
        public double ForkDroop { get; init; } = 0.45;
        public double FirstTier { get; init; } = 0.16;
        public double TierSpacing { get; init; } = 0.12;
        public double TrunkLength { get; init; } = 1.35;
        public double LowerForkLength { get; init; } = 0.92;
        public double BranchLength { get; init; } = 0.48;
        public double LeafLengthMin { get; init; } = 0.22;
        public double LeafLengthMax { get; init; } = 0.27;
        public double FlowerLength { get; init; } = 0.038;
        public double FlowerHalfWidth { get; init; } = 0.018;
    }

    private readonly FormSettings _form;
    public ShadebellGenerator() : this(new FormSettings()) { }
    public ShadebellGenerator(FormSettings form) => _form = form ?? throw new ArgumentNullException(nameof(form));
    public override string SpeciesId => "shadebell";

    protected override void Design(PlanBuilder b)
    {
        // Mature form controls: drooping forks, lateral reach and broad leaf size.
        var form = _form;
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

        int trunk = b.AddAxis(-1, 0, up + new Vec3(0.08, 0, 0.04), form.TrunkLength, new Vec3(0.12, 0, 0.06), 0.024, 0.014, 0,
            Tissue.Wood, 0, 0.5);

        for (int tier = 0; tier < 6; tier++)
        {
            {
                double tierT = Math.Clamp(form.FirstTier + form.TierSpacing * tier + b.Range(-0.035, 0.035), 0.05, 0.9);
                double az = tier * 2.399963 + b.Range(-0.35, 0.35);
                var forkDir = PlantMath.Horizontal(az) * b.Range(0.65, 0.85) + up * b.Range(0.65, 1.0);
                double forkLen = (form.LowerForkLength - 0.05 * tier) * b.Range(0.78, 1.2);
                int fork = b.AddAxis(trunk, tierT, forkDir, forkLen, up * -form.ForkDroop * b.Range(0.65, 1.3), 0.013, 0.004, 1, Tissue.Wood,
                    0.03 * b.Draw(), 0.3);

                // Whorl on the fork itself.
                AddWhorl(b, leaf, fork, b.Range(0.35, 0.62), az, 3, 0.8, form.LeafLengthMin, form.LeafLengthMax);

                for (int s2 = 0; s2 < 2; s2++)
                {
                    double t2 = 0.38 + 0.43 * s2 + b.Range(-0.04, 0.04);
                    double baz = az + (s2 == 0 ? 1.0 : -1.0) + b.Range(-0.35, 0.35);
                    var brDir = PlantMath.Horizontal(baz) * b.Range(0.5, 0.85) + up * b.Range(0.55, 0.95);
                    int br = b.AddAxis(fork, t2, brDir, form.BranchLength * b.Range(0.75, 1.25), up * b.Range(-0.25, 0.15), 0.008, 0.004, 2,
                        Tissue.Wood, 0.03 * b.Draw(), 0.2);

                    for (int wh = 0; wh < 3; wh++)
                    {
                        AddWhorl(b, leaf, br, 0.25 + 0.3 * wh + b.Range(-0.04, 0.04), baz + wh * 2.399963, 3, 1.0, form.LeafLengthMin, form.LeafLengthMax);
                    }

                    // Two pendant bells per side cane.
                    for (int pd = 0; pd < 2; pd++)
                    {
                        double pt = 0.45 + 0.4 * pd;
                        double pa = baz + pd * 2.1;
                        var pedDir = Vec3.Down * 0.9 + PlantMath.Horizontal(pa) * 0.35;
                        int ped = b.AddAxis(br, pt, pedDir, 0.045, Vec3.Zero, 0.0014, 0.0010, 3, Tissue.Green,
                            0.03 * b.Draw(), 0.15);
                        b.AddFlat(ped, 1.0, Vec3.Down * 0.95 + PlantMath.Horizontal(pa) * 0.2, form.FlowerLength, form.FlowerHalfWidth, 0.009,
                            OrganType.Flower, bell, 4, 0.03 * b.Draw(), 0.2);
                    }
                }
            }
        }
    }

    /// <summary>Horizontal whorl of broad blades on petioles at axis parameter t.</summary>
    private static void AddWhorl(PlanBuilder b, ushort leaf, int axis, double t, double az0, int count, double scale, double leafLengthMin, double leafLengthMax)
    {
        for (int i = 0; i < count; i++)
        {
            double az = az0 + i * Math.Tau / count + b.Range(-0.3, 0.3);
            var h = PlantMath.Horizontal(az);
            int pet = b.AddAxis(axis, t, h * 0.8 + Vec3.Up * 0.45, 0.05 * scale, Vec3.Zero, 0.0035, 0.002, 3,
                Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
            double lLen = b.Range(leafLengthMin, leafLengthMax) * scale * b.Range(0.85, 1.1);
            b.AddFlat(pet, 1.0, h * 0.95 + Vec3.Up * b.Range(-0.22, 0.55), lLen, lLen * 0.5, 0.0006, OrganType.Leaf, leaf, 4,
                0.03 * b.Draw(), 0.14);
        }
    }
}
