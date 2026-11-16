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
        public double FrondHalfWidthRatio { get; init; } = 0.19;
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
            WidthProfile = PlantShapes.Profile(t => 0.5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 0.72)),
            LobeCount = 20, LobeDepth = 0.78f, PairedLaminaApertures = true,
            LengthToWidth = 3.0f, MidribCurl = (float)form.FrondCurl, CrossCurl = 0.05f,
            Thickness = 0.0005f, ColorAdaxial = PlantShapes.Srgb(0.19, 0.34, 0.25),
            ColorAbaxial = PlantShapes.Srgb(0.46, 0.67, 0.48), Transmission = 0.4f, Roughness = 0.6f,
        });
        int count = Math.Max(1, form.MinFronds) + (b.Draw() < 0.5 ? 0 : 1);
        int crown = b.AddAxis(-1, 0, up, 0.035, Vec3.Zero, 0.010, 0.008, 0, Tissue.Bark, 0, 0.04);
        double rot = b.Range(0, Math.Tau);
        for (int f = 0; f < count; f++)
        {
            double az = rot + f * Math.Tau / count + b.Range(-0.2, 0.2);
            var outward = PlantMath.Horizontal(az);
            double length = b.Range(form.FrondLengthMin, form.FrondLengthMax);
            var direction = PlantMath.SafeNormal(outward * 0.48 + up, up);
            int rachis = b.AddAxis(crown, 1, direction, length * 0.045, outward * 0.15,
                0.0045, 0.0035, 1, Tissue.Green, 0.02 * b.Draw(), form.PetioleGrowthDuration);
            b.AddFlat(rachis, 1, direction, length * 0.955, length * form.FrondHalfWidthRatio * b.Range(0.9, 1.1), 0.0005,
                OrganType.Leaf, sheet, 2, 0.02 * b.Draw(), form.FrondGrowthDuration);
        }
    }
}
