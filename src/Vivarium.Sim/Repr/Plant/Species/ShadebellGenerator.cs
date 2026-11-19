using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Three crooked, staggered trunks carry broad leaf decks and pendant bell racemes.
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
        public double FlowerLength { get; init; } = 0.065;
        public double FlowerHalfWidth { get; init; } = 0.030;
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
            WidthProfile = PlantShapes.Profile(t => 0.5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.68))), 0.62) * (1 - 0.12 * t)),
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

        double rotation = b.Range(0, Math.Tau);
        for (int k = 0; k < 3; k++)
        {
            double az = rotation + k * Math.Tau / 3 + b.Range(-0.22, 0.22);
            var radial = PlantMath.Horizontal(az);
            var side = PlantMath.Horizontal(az + Math.PI / 2);
            double height = (2.18 - k * 0.25) * b.Range(.88,1.10);
            int lower = b.AddAxis(-1, 0, up + radial * 0.25, height * 0.48,
                side * (k % 2 == 0 ? 0.35 : -0.35) + radial * 0.12,
                0.038, 0.020, 0, Tissue.Wood, 0, 0.24);
            int upper = b.AddAxis(lower, 1, up + radial * 0.22, height * 0.52,
                side * (k % 2 == 0 ? -0.35 : 0.35) + radial * 0.15,
                0.020, 0.008, 1, Tissue.Wood, 0.04, 0.22);
            // Continue the trunk through one shared ring, preserving the authored upper tip.
            var lowerStem = b[lower];
            var upperStem = b[upper];
            upperStem.P0 = lowerStem.P1;
            upperStem.T0 = lowerStem.T1.Normalized() * upperStem.T0.Length;
            upperStem.R0 = lowerStem.R1;
            lowerStem.Flags |= OrganFlags.Continuous;
            upperStem.Flags |= OrganFlags.Continuous;
            // Short exposed buttresses flare the foot of each trunk into the soil.
            for (int r = 0; r < 3; r++)
            {
                double ra = az + r * Math.Tau / 3 + b.Range(-0.45, 0.45);
                var ro = PlantMath.Horizontal(ra);
                var rs = PlantMath.Horizontal(ra + Math.PI / 2);
                int root = b.AddAxis(lower, b.Range(0.02, 0.10), ro * 0.60 + Vec3.Down,
                    height * b.Range(0.045, 0.085), rs * b.Range(-0.25, 0.25) + Vec3.Down * 0.2,
                    0.011, 0.004, 1, Tissue.RootBark, 0, 0.18);
                var foot = b[root];
                double reachOut = height * b.Range(0.036, 0.062);
                foot.P1 = ro * (reachOut * b.Range(0.85, 1.05)) + rs * (reachOut * b.Range(-0.22, 0.22))
                    + Vec3.Down * (height * b.Range(0.006, 0.013));
                foot.T0 = PlantMath.SafeNormal(foot.T0, Vec3.Down) * (foot.P1 - foot.P0).Length;
                foot.T1 = PlantMath.SafeNormal(foot.T1, Vec3.Down) * (foot.P1 - foot.P0).Length;
                foot.AxisGnarling = b.Range(0.12, 0.26);
            }
            AddWhorl(b, leaf, upper, 0.98, az, 6, 1.0, form.LeafLengthMin, form.LeafLengthMax);
            for (int tier = 0; tier < 3; tier++)
            {
                int host = tier == 0 ? lower : upper;
                double t = (tier == 0 ? 0.55 : tier == 1 ? 0.28 : 0.72) + b.Range(-0.055, 0.055);
                double branchAz = az + (tier % 2 == 0 ? 0.95 : -0.95) + b.Range(-0.18, 0.18);
                var outward = PlantMath.Horizontal(branchAz);
                var lateral = PlantMath.Horizontal(branchAz + Math.PI / 2);
                int fork = b.AddAxis(host, t, outward + up * 0.3, form.LowerForkLength * b.Range(0.62, 0.76),
                    lateral * b.Range(-0.2, 0.2) + up * -0.12, 0.016, 0.006, 2, Tissue.Wood, 0.03 * b.Draw(), 0.18);
                // Embed the fork into its parent and ease it along the local tangent.
                // Its tip remains in place; the overlapping surfaces are not a mesh union.
                var branch = b[fork];
                var parent = b[host];
                var center = PlantMath.Hermite(parent.P0, parent.T0, parent.P1, parent.T1, t);
                branch.P0 = center + (branch.P0 - center) * .35;
                var axis = b.TangentAt(host, t);
                branch.T0 = PlantMath.SafeNormal(axis * .65 + branch.T0.Normalized() * .75, axis) * branch.T0.Length;
                for (int wh = 0; wh < 3; wh++)
                    AddWhorl(b, leaf, fork, 0.48 + wh * 0.23 + b.Range(-.09,.09), branchAz + wh * 1.3, 5, 0.95,
                        form.LeafLengthMin, form.LeafLengthMax);
                // A hanging raceme with several pendant bells beneath each leaf deck.
                int raceme = b.AddAxis(fork, 0.83, Vec3.Down + outward * 0.15, 0.15,
                    outward * 0.1, 0.002, 0.001, 3, Tissue.Green, 0.03 * b.Draw(), 0.18);
                for (int flower = 0; flower < 4; flower++)
                {
                    double fa = branchAz + flower * 2.399963;
                    var fh = PlantMath.Horizontal(fa);
                    int ped = b.AddAxis(raceme, 0.2 + flower * 0.23, Vec3.Down + fh * 0.4, 0.035,
                        Vec3.Zero, 0.001, 0.0007, 4, Tissue.Green, 0.03 * b.Draw(), 0.12);
                    b.AddFlat(ped, 1, Vec3.Down + fh * 0.15, form.FlowerLength, form.FlowerHalfWidth, 0.009,
                        OrganType.Flower, bell, 5, 0.03 * b.Draw(), 0.2);
                }
            }
        }
    }

    /// <summary>Horizontal whorl of broad blades on petioles at axis parameter t.</summary>
    private static void AddWhorl(PlanBuilder b, ushort leaf, int axis, double t, double az0, int count, double scale, double leafLengthMin, double leafLengthMax)
    {
        for (int i = 0; i < count; i++)
        {
            double az = az0 + i * Math.Tau / count + b.Range(-0.35, 0.35);
            var h = PlantMath.Horizontal(az);
            int pet = b.AddAxis(axis, Math.Clamp(t + b.Range(-0.025, 0.025), 0.05, 0.99), h * 0.82 + Vec3.Up * b.Range(0.28, 0.55), b.Range(0.042, 0.058) * scale,
                Vec3.Down * 0.25, 0.0035, 0.002, 3, Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
            double lLen = b.Range(leafLengthMin, leafLengthMax) * scale * b.Range(0.85, 1.15);
            b.AddFlat(pet, 1.0, h * 0.99 + Vec3.Up * b.Range(-.22,.28), lLen, lLen * 0.52, 0.0006, OrganType.Leaf, leaf, 4,
                0.03 * b.Draw(), 0.14);
        }
    }
}
