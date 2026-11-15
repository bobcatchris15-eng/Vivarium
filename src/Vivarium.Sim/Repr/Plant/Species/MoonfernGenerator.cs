using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Moonfern: rosette of 6-7 arching fronds from a short crown. Each frond is a curved rachis (order 1) carrying
/// 14 pairs of pinnae (Leaflet organs attached directly to the rachis). Pinna length follows a frond outline:
/// small at the base, widest near one third of the frond, tapering to the tip. Some pinnae are emissive.
/// Crozier coiling of young fronds is NOT implemented (see report): young fronds are short and have small pinnae.
/// Sizes from game/content/flora/moonfern.json: height 0.46 m, radius at max 0.28 m.
/// </summary>
public sealed class MoonfernGenerator : PlanGenerator
{
    public sealed record FormSettings
    {
        public int MinFronds { get; init; } = 6;
        public double FrondDroop { get; init; } = 0.7;
        public double FrondGrowthDuration { get; init; } = 0.65;
        public double PinnaGrowthDuration { get; init; } = 0.10;
        public double ElevationMin { get; init; } = 0.82;
        public double ElevationMax { get; init; } = 1.35;
        public double FrondLengthMin { get; init; } = 0.30;
        public double FrondLengthMax { get; init; } = 0.51;
        public double PinnaLengthRatio { get; init; } = 0.24;
        public double PinnaWidthRatio { get; init; } = 0.18;
        public int PinnaPairs { get; init; } = 14;
    }

    private readonly FormSettings _form;
    public MoonfernGenerator() : this(new FormSettings()) { }
    public MoonfernGenerator(FormSettings form) => _form = form ?? throw new ArgumentNullException(nameof(form));
    public override string SpeciesId => "moonfern";

    protected override void Design(PlanBuilder b)
    {
        // Mature form controls: rosette density, arch and the pinna envelope.
        var form = _form;
        var up = Vec3.Up;
        ushort pinna = b.AddShape(new OrganShape
        {
            Name = "moonfern_pinna", Type = OrganType.Leaflet,
            WidthProfile = PlantShapes.Profile(t => 0.32 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.6)), 0.7)),
            LengthToWidth = 1.6f, MidribCurl = 0.25f, CrossCurl = 0.35f, Twist = 0.1f,
            EdgeWave = 0.02f, EdgeWaveFreq = 7f, Serration = 0.05f, SerrationFreq = 11f,
            Thickness = 0.0005f,
            ColorAdaxial = PlantShapes.Srgb(0.19, 0.34, 0.39), ColorAbaxial = PlantShapes.Srgb(0.46, 0.67, 0.72),
            Transmission = 0.4f, Roughness = 0.6f,
        });

        int fronds = form.MinFronds + (b.Draw() < 0.5 ? 0 : 1);
        int crown = b.AddAxis(-1, 0, up, 0.035, Vec3.Zero, 0.010, 0.008, 0, Tissue.Bark, 0, 0.15);
        double rot = b.Range(0, Math.Tau);

        for (int f = 0; f < fronds; f++)
        {
            double az = rot + f * Math.Tau / fronds + b.Range(-0.2, 0.2);
            var outward = PlantMath.Horizontal(az);
            double elev = b.Range(form.ElevationMin, form.ElevationMax);
            var dir = outward * Math.Cos(elev) + up * Math.Sin(elev);
            double flen = b.Range(form.FrondLengthMin, form.FrondLengthMax);
            int rachis = b.AddAxis(crown, 1.0, dir, flen, up * -form.FrondDroop * b.Range(0.8, 1.2) + outward * 0.15, 0.0045, 0.0009, 1, Tissue.Green,
                0.02 * b.Draw(), form.FrondGrowthDuration);

            // Frond-plane sideways direction (pinnae spread across it).
            var frondSide = PlantMath.Horizontal(az + Math.PI / 2);
            double frondTilt = b.Range(-0.3, 0.3);
            int pairs = form.PinnaPairs;
            for (int p = 0; p < pairs; p++)
            {
                double s = 0.12 + 0.83 * p / (pairs - 1);
                double sp = (s - 0.12) / 0.83;
                // Outline: small at base, widest near 1/3 of the frond, tapering to the tip.
                double prof = Math.Max(0.22, Math.Sqrt(sp) * Math.Pow(1 - sp, 1.1) / 0.3714);
                double plen = flen * form.PinnaLengthRatio * prof * b.Range(0.9, 1.1);
                double halfW = plen * form.PinnaWidthRatio;
                var tan = b.TangentAt(rachis, s);
                var side = PlantMath.SafeNormal(frondSide + up * frondTilt, frondSide);
                var flags = b.Draw() < 0.3 ? OrganFlags.Emissive : OrganFlags.None;
                double baseDelay = 0.1 * b.Draw();
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var pdir = tan * b.Range(0.3, 0.48) + side * (0.95 * sign) + up * b.Range(-0.06, 0.16);
                    b.AddFlat(rachis, s, pdir, plen, halfW, 0.0005, OrganType.Leaflet, pinna, 2, baseDelay, form.PinnaGrowthDuration, flags);
                }
            }
        }
    }
}
