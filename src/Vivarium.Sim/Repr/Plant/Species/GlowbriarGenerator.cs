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

        for (int c = 0; c < form.CaneCount; c++)
        {
            double az = c * Math.Tau / form.CaneCount + b.Range(-0.3, 0.3);
            var caneDir = PlantMath.Horizontal(az) * 0.62 + up * b.Range(form.CaneElevationMin, form.CaneElevationMax);
            int cane = b.AddAxis(stool, b.Range(0.35, 0.6), caneDir, b.Range(form.CaneLengthMin, form.CaneLengthMax), up * -form.CaneDroop * b.Range(0.75, 1.25) + PlantMath.Horizontal(az) * 0.15,
                0.013, 0.0025, 1, Tissue.Wood, 0.03 * b.Draw(), 0.2);

            // Recurved thorns along the cane internodes.
            for (int th = 0; th < 8; th++)
            {
                double tt = 0.15 + 0.8 * (th + 0.6 * b.Draw()) / 8.0;
                double side = (th & 1) == 0 ? 1 : -1;
                var tdir = (PlantMath.Horizontal(az) * 0.5 + PlantMath.Horizontal(az + Math.PI / 2) * (0.7 * side)
                    + up * 0.3).Normalized();
                b.AddAxis(cane, tt, tdir, 0.016, Vec3.Zero, 0.0035, 0.0003, 2, Tissue.Bark, 0.03 * b.Draw(), 0.12);
            }

            for (int k = 0; k < form.BranchCount; k++)
            {
                double baz = az + ((k & 1) == 0 ? 0.8 : -0.8) + b.Range(-0.25, 0.25);
                var bo = PlantMath.Horizontal(baz);
                var brDir = bo * b.Range(0.55, 0.9) + up * b.Range(0.35, 0.9);
                double branchPos = 0.13 + 0.78 * (k / (double)Math.Max(1, form.BranchCount - 1));
                int br = b.AddAxis(cane, Math.Clamp(branchPos + b.Range(-0.05, 0.05), 0.08, 0.98), brDir,
                    form.BranchLength * (1 - 0.25 * k / Math.Max(1, form.BranchCount - 1.0)) * b.Range(0.72, 1.25), up * b.Range(-0.3, 0.15), 0.0065, 0.0018, 2, Tissue.Wood,
                    0.03 * b.Draw(), 0.18);

                for (int tw = 0; tw < 2; tw++)
                {
                    double twigT = tw == 0 ? b.Range(0.38, 0.58) : 0.97;
                    var twigDir = PlantMath.Horizontal(baz + (tw == 0 ? 0.9 : -0.9)) * 0.8 + up * b.Range(0.25, 0.9);
                    int twig = b.AddAxis(br, twigT, twigDir, form.TwigLength, up * b.Range(-0.25, 0.1), 0.0045, 0.0016, 3, Tissue.Green,
                        0.03 * b.Draw(), 0.14);

                    // Internodes shorten toward the tip: nodes sit closer together near the end of the twig.
                    for (int nd = 0; nd < form.LeafNodes; nd++)
                    {
                        double nodeProgress = nd / (double)Math.Max(1, form.LeafNodes - 1);
                        double nt = Math.Clamp(0.16 + 0.79 * nodeProgress + b.Range(-0.025, 0.025), 0.1, 0.98);
                        for (int sd = 0; sd < 2; sd++)
                        {
                            double la = nd * (Math.PI / 2) + sd * Math.PI + baz;
                            var h = PlantMath.Horizontal(la);
                            int pet = b.AddAxis(twig, nt, h * 0.6 + up * 0.8, 0.02, Vec3.Zero, 0.0016, 0.0010, 4,
                                Tissue.Green, 0.03 * b.Draw(), 0.1, OrganFlags.None, OrganType.Petiole);
                            double ll = b.Range(form.LeafLengthMin, form.LeafLengthMax) * (1.0 - 0.20 * nodeProgress);
                            b.AddFlat(pet, 1.0, h * 0.9 + up * 0.4, ll, ll * form.LeafWidthRatio, 0.0004, OrganType.Leaf, leafShape,
                                5, 0.03 * b.Draw(), 0.14);
                        }
                    }
                }

                // One pendant luminous chalice per branch, hanging from a pedicel on the branch.
                int ped = b.AddAxis(br, 0.7, Vec3.Down * 0.95 + bo * 0.3, 0.035, Vec3.Zero, 0.0011, 0.0008, 5,
                    Tissue.Green, 0.03 * b.Draw(), 0.12);
                b.AddFlat(ped, 1.0, Vec3.Down * 0.9 + bo * 0.3, form.FlowerLength, form.FlowerHalfWidth, 0.008, OrganType.Flower, chalice, 6,
                    0.03 * b.Draw(), 0.15, OrganFlags.Emissive);
            }
        }
    }
}
