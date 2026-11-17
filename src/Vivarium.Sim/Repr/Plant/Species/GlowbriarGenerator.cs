using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Glowbriar: low woody shrub with arching canes from a short stool. Architecture: 6 canes (order 1), 4 side
/// branches per cane (order 2), 2 green twigs per branch (order 3). Phyllotaxy is OPPOSITE DECUSSATE (pairs
/// rotated 90 degrees per node, as in the legacy cane builder) with leaves on petioles at nodes of the twigs.
/// Recurved thorns on canes; one pendant emissive chalice per branch (legacy: 16-22 chalices).
/// Sizes from game/content/flora/glowbriar.json: height 1.12 m, canopy radius 0.95 m.
/// </summary>
public sealed class GlowbriarGenerator : PlanGenerator
{
    public sealed record FormSettings
    {
        public int CaneCount { get; init; } = 6;
        public int BranchCount { get; init; } = 4;
        public int LeafNodes { get; init; } = 5;
        public double CaneDroop { get; init; } = 0.55;
        public double CaneElevationMin { get; init; } = 0.7;
        public double CaneElevationMax { get; init; } = 1.15;
        public double CaneLengthMin { get; init; } = 0.65;
        public double CaneLengthMax { get; init; } = 1.10;
        public double BranchLength { get; init; } = 0.34;
        public double TwigLength { get; init; } = 0.20;
        public double LeafWidthRatio { get; init; } = 0.29;
        public double LeafLengthMin { get; init; } = 0.12;
        public double LeafLengthMax { get; init; } = 0.17;
        public double FlowerLength { get; init; } = 0.035;
        public double FlowerHalfWidth { get; init; } = 0.016;
    }

    private readonly FormSettings _form;
    public GlowbriarGenerator() : this(new FormSettings()) { }
    public GlowbriarGenerator(FormSettings form) => _form = form ?? throw new ArgumentNullException(nameof(form));
    public override string SpeciesId => "glowbriar";

    protected override void Design(PlanBuilder b)
    {
        // Mature form controls: cane count, arch, branch reach and blade size.
        var form = _form;
        var up = Vec3.Up;
        ushort leafShape = b.AddShape(new OrganShape
        {
            Name = "glowbriar_leaf", Type = OrganType.Leaf,
            WidthProfile = PlantShapes.Profile(t => 0.2 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.82))), 0.72) * (1 - 0.20 * t)),
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

        int stool = b.AddAxis(-1, 0, up + new Vec3(b.Range(-0.08, 0.08), 0, b.Range(-0.08, 0.08)), 0.12,
            new Vec3(b.Range(-0.12, 0.12), 0, b.Range(-0.12, 0.12)), 0.050, 0.030, 0, Tissue.Wood, 0, 0.25);

        double baseRot = b.Range(0, Math.Tau);
        // Each low runner turns upward into a cane. Their inner ends begin inside
        // the stool; the runner/cane transition itself is a welded continuous skin.
        for (int c = 0; c < form.CaneCount; c++)
        {
            double az = baseRot + c * Math.Tau / form.CaneCount + b.Range(-0.42, 0.42);
            var hCane = PlantMath.Horizontal(az);
            var sCane = PlantMath.Horizontal(az + Math.PI / 2);
            double cSpread = b.Range(0.45, 0.85);
            var caneDir = hCane * cSpread + up * b.Range(form.CaneElevationMin * 0.85, form.CaneElevationMax * 1.15);
            var caneBend = up * (-form.CaneDroop * b.Range(0.85, 1.45)) + hCane * b.Range(0.10, 0.35) + sCane * b.Range(-0.32, 0.32);
            double rootLength = b.Range(0.24, 0.34);
            int root = b.AddAxis(stool, 0.08, hCane, rootLength, Vec3.Zero,
                0.016, 0.013, 1, Tissue.Wood, 0.02 * b.Draw(), 0.2, OrganFlags.Continuous);
            var runner = b[root];
            runner.P0 = PlantMath.Hermite(b[stool].P0, b[stool].T0, b[stool].P1, b[stool].T1, 0.08);
            runner.T0 = hCane * rootLength;
            runner.T1 = caneDir.Normalized() * rootLength;
            runner.P1 = runner.P0 + (runner.T0 + runner.T1) * 0.5;
            int cane = b.AddAxis(root, 1, caneDir, b.Range(form.CaneLengthMin * 0.9, form.CaneLengthMax * 1.08),
                caneBend, 0.013, 0.0025, 1, Tissue.Wood, 0.03 * b.Draw(), 0.2, OrganFlags.Continuous);
            var shoot = b[cane];
            // AddAxis normally attaches at the parent's side; this is a continuation.
            var displacement = runner.P1 - shoot.P0;
            shoot.P0 += displacement;
            shoot.P1 += displacement;
            shoot.R0 = runner.R1;

            // Recurved thorns along the cane internodes.
            for (int th = 0; th < 8; th++)
            {
                double tt = 0.15 + 0.8 * (th + 0.6 * b.Draw()) / 8.0;
                double side = (th & 1) == 0 ? 1 : -1;
                var tdir = (hCane * 0.5 + sCane * (0.7 * side) + up * 0.3).Normalized();
                b.AddAxis(cane, tt, tdir, 0.016, Vec3.Down * 0.35, 0.0035, 0.0003, 2, Tissue.Bark, 0.03 * b.Draw(), 0.12);
            }

            for (int k = 0; k < form.BranchCount; k++)
            {
                double baz = az + ((k & 1) == 0 ? 0.75 : -0.75) + b.Range(-0.40, 0.40);
                var bo = PlantMath.Horizontal(baz);
                var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                var brDir = bo * b.Range(0.55, 0.95) + up * b.Range(0.30, 0.95) + bs * b.Range(-0.2, 0.2);
                double branchPos = 0.13 + 0.78 * (k / (double)Math.Max(1, form.BranchCount - 1));
                var brBend = up * b.Range(-0.48, -0.05) + bs * b.Range(-0.35, 0.35) + bo * b.Range(0.05, 0.25);
                int br = b.AddAxis(cane, Math.Clamp(branchPos + b.Range(-0.06, 0.06), 0.08, 0.98), brDir,
                    form.BranchLength * (1 - 0.25 * k / Math.Max(1, form.BranchCount - 1.0)) * b.Range(0.70, 1.30), brBend, 0.0065, 0.0018, 2, Tissue.Wood,
                    0.03 * b.Draw(), 0.18);

                for (int tw = 0; tw < 2; tw++)
                {
                    double twigT = tw == 0 ? b.Range(0.35, 0.62) : b.Range(0.88, 0.98);
                    double twAz = baz + (tw == 0 ? 0.85 : -0.85) + b.Range(-0.35, 0.35);
                    var twOut = PlantMath.Horizontal(twAz);
                    var twSide = PlantMath.Horizontal(twAz + Math.PI / 2);
                    var twigDir = twOut * b.Range(0.7, 0.95) + up * b.Range(0.20, 0.85);
                    var twigBend = up * b.Range(-0.45, -0.05) + twSide * b.Range(-0.35, 0.35);
                    int twig = b.AddAxis(br, twigT, twigDir, form.TwigLength * b.Range(0.82, 1.22), twigBend, 0.0045, 0.0016, 3, Tissue.Green,
                        0.03 * b.Draw(), 0.14);

                    // Internodes shorten toward the tip: nodes sit closer together near the end of the twig.
                    for (int nd = 0; nd < form.LeafNodes; nd++)
                    {
                        double nodeProgress = nd / (double)Math.Max(1, form.LeafNodes - 1);
                        double nt = Math.Clamp(0.14 + 0.81 * (1 - Math.Pow(1 - nodeProgress, 1.22)) + b.Range(-0.025, 0.025), 0.1, 0.98);
                        for (int sd = 0; sd < 2; sd++)
                        {
                            double la = nd * (Math.PI / 2) + sd * Math.PI + twAz + b.Range(-0.22, 0.22);
                            var h = PlantMath.Horizontal(la);
                            int pet = b.AddAxis(twig, nt, h * 0.65 + up * b.Range(0.55, 0.90), b.Range(0.016, 0.026),
                                Vec3.Down * 0.25, 0.0016, 0.0010, 4, Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
                            double ll = b.Range(form.LeafLengthMin, form.LeafLengthMax) * (1.05 - 0.22 * nodeProgress);
                            b.AddFlat(pet, 1.0, h * 0.92 + up * b.Range(0.15, 0.45), ll, ll * form.LeafWidthRatio, 0.0004, OrganType.Leaf, leafShape,
                                5, 0.03 * b.Draw(), 0.14);
                        }
                    }
                }

                // One pendant luminous chalice per branch, hanging from a pedicel on the branch.
                int ped = b.AddAxis(br, 0.7, Vec3.Down * 0.95 + bo * 0.3, 0.035, bs * b.Range(-0.2, 0.2), 0.0011, 0.0008, 5,
                    Tissue.Green, 0.03 * b.Draw(), 0.12);
                b.AddFlat(ped, 1.0, Vec3.Down * 0.9 + bo * 0.3, form.FlowerLength, form.FlowerHalfWidth, 0.008, OrganType.Flower, chalice, 6,
                    0.03 * b.Draw(), 0.15, OrganFlags.Emissive);
            }
        }
    }
}
