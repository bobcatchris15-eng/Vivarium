using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// A sparse crown of two or three connected, pierced, lobed frond sheets.
/// </summary>
public sealed class MoonfernGenerator : PlanGenerator
{
    public sealed record FormSettings
    {
        public int MinFronds { get; init; } = 2;
        public double FrondGrowthDuration { get; init; } = 0.50;
        public double PetioleGrowthDuration { get; init; } = 0.12;
        public double FrondLengthMin { get; init; } = 0.45;
        public double FrondLengthMax { get; init; } = 0.55;
        public double FrondHalfWidthRatio { get; init; } = 0.23;
        public double FrondCurl { get; init; } = 1.1;

    }

    private readonly FormSettings _form;
    public MoonfernGenerator() : this(new FormSettings()) { }
    public MoonfernGenerator(FormSettings form) => _form = form ?? throw new ArgumentNullException(nameof(form));
    public override string SpeciesId => "moonfern";

    protected override void Design(PlanBuilder b)
    {
        var form = _form;
        var up = Vec3.Up;
        ushort sheet = b.AddShape(new OrganShape
        {
            Name = "moonfern_connected_frond", Type = OrganType.Leaf,
            WidthProfile = PlantShapes.Profile(t => 0.5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.80))), 0.68) * (1 - 0.24 * t)),
            LobeCount = 14, LobeDepth = 0.60f, PairedLaminaApertures = true, LobePhaseWarp = 0.9f,
            LengthToWidth = 2.6f, MidribCurl = (float)form.FrondCurl, CrossCurl = 0.05f,
            Thickness = 0.0005f, ColorAdaxial = PlantShapes.Srgb(0.19, 0.34, 0.25),
            ColorAbaxial = PlantShapes.Srgb(0.46, 0.67, 0.48), Transmission = 0.4f, Roughness = 0.6f,
        });
        int count = Math.Max(1, form.MinFronds) + (b.Draw() < 0.5 ? 0 : 1);
        int crown = b.AddAxis(-1, 0, up, 0.020, Vec3.Zero, 0.0085, 0.0052, 0, Tissue.Bark, 0, 0.04, OrganFlags.Continuous);
        double rot = b.Range(0, Math.Tau);
        // A cohort of different ages: the first frond is mature and reaches
        // outward, later ones are shorter, younger and lift more steeply, so the
        // pair never reads as one mirrored V.
        double[] ageScale = count == 2 ? new[] { 1.0, 0.72 } : new[] { 1.0, 0.78, 0.62 };
        for (int f = 0; f < count; f++)
        {
            double az = rot + f * Math.Tau / count + b.Range(-0.38, 0.38);
            var outward = PlantMath.Horizontal(az);
            double age = ageScale[Math.Min(f, ageScale.Length - 1)];
            double length = b.Range(form.FrondLengthMin*.86, form.FrondLengthMax*1.10) * age;
            double pitch = f == 0 ? b.Range(0.52, 0.86) : b.Range(0.16, 0.42);
            var direction = PlantMath.SafeNormal(outward * pitch + up, up);
            int rachis = f == 0
                ? b.AddAxis(crown, 1, direction, length * 0.045, outward * b.Range(0.08, 0.24),
                    0.0052, 0.0035, 1, Tissue.Green, 0.03 * f, form.PetioleGrowthDuration, OrganFlags.Continuous)
                : b.AddAxis(crown, Math.Clamp(0.72 + 0.12 * f, 0.55, 0.96), direction, length * 0.045, outward * b.Range(0.08, 0.24),
                    0.0042, 0.0032, 1, Tissue.Green, 0.03 * f, form.PetioleGrowthDuration);
            if (f == 0)
            {
                b[rachis].P0 = b[crown].P1;
                b[rachis].R0 = b[crown].R1;
                b[rachis].P1 = b[rachis].P0 + (b[rachis].T0 + b[rachis].T1) * 0.5;
            }
            b.AddFlat(rachis, 1, direction, length * 0.955, length * form.FrondHalfWidthRatio * b.Range(0.92, 1.08), 0.0005,
                OrganType.Leaf, sheet, 2, 0.03 * f, form.FrondGrowthDuration * age);
        }
    }
}
