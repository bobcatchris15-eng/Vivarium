using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Moonfern: rosette of 2-3 arching fronds from a short crown. Each frond is a curved rachis (order 1) carrying
/// 14 pairs of pinnae (Leaflet organs attached directly to the rachis). Pinna length follows a frond outline:
/// small at the base, widest near one third of the frond, tapering to the tip. Some pinnae are emissive.
/// Crozier coiling of young fronds is NOT implemented (see report): young fronds are short and have small pinnae.
/// Sizes from game/content/flora/moonfern.json: height 0.46 m, radius at max 0.28 m.
/// </summary>
public sealed class MoonfernGenerator : PlanGenerator
{
    public override string SpeciesId => "moonfern";

    protected override void Design(PlanBuilder b)
    {
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

        int fronds = b.Draw() < 0.5 ? 2 : 3;
        int crown = b.AddAxis(-1, 0, up, 0.035, Vec3.Zero, 0.010, 0.008, 0, Tissue.Bark, 0, 0.15);
        double rot = b.Range(0, Math.Tau);

        for (int f = 0; f < fronds; f++)
        {
            double az = rot + f * Math.Tau / fronds + b.Range(-0.2, 0.2);
            var outward = PlantMath.Horizontal(az);
            double elev = b.Range(0.9, 1.1);
            var dir = outward * Math.Cos(elev) + up * Math.Sin(elev);
            double flen = b.Range(0.44, 0.5);
            int rachis = b.AddAxis(crown, 1.0, dir, flen, up * -0.3, 0.0045, 0.0009, 1, Tissue.Green,
                0.02 * b.Draw(), 0.45);

            // Frond-plane sideways direction (pinnae spread across it).
            var frondSide = PlantMath.Horizontal(az + Math.PI / 2);
            const int pairs = 14;
            for (int p = 0; p < pairs; p++)
            {
                double s = 0.12 + 0.83 * p / (pairs - 1);
                double sp = (s - 0.12) / 0.83;
                // Outline: small at base, widest near 1/3 of the frond, tapering to the tip.
                double prof = Math.Max(0.22, Math.Sqrt(sp) * Math.Pow(1 - sp, 1.1) / 0.3714);
                double plen = flen * 0.16 * prof * b.Range(0.9, 1.1);
                double halfW = plen * 0.32;
                var tan = b.TangentAt(rachis, s);
                var side = PlantMath.SafeNormal(tan.Cross(frondSide), frondSide);
                var flags = b.Draw() < 0.3 ? OrganFlags.Emissive : OrganFlags.None;
                double baseDelay = 0.1 * b.Draw();
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var pdir = tan * 0.55 + side * (0.8 * sign) + up * 0.1;
                    b.AddFlat(rachis, s, pdir, plen, halfW, 0.0005, OrganType.Leaflet, pinna, 2, baseDelay, 0.3, flags);
                }
            }
        }
    }
}
