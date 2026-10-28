using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Five genuinely raised understory architectures. All points are in unit radius/height space.
/// Unlike brakes and curtains these forms retain a mostly open basal volume beneath living foliage.
/// Seeded variation is structural: forks, asymmetrical crowns, missing shelves and leaf orientations.
/// </summary>
public static partial class OrganismMeshes
{
    private static Vec3 MidRadial(double az) => new(Math.Cos(az), 0, Math.Sin(az));
    private static readonly double[] MidBark = { 0.24, 0.21, 0.16 };


    // Ribbed petiole with prominent longitudinal ridges and regular nodules.
    private static void MidRainshieldStem(MeshData m, Vec3[] path, double rBase, double rTip,
        double[] baseCol, double[] tipCol, int segments, int stations, int nodules = 4, int ridges = 5,
        double ridgeAmp = 0.20, double noduleAmp = 0.30)
    {
        int start = m.VertexCount;
        var tangents = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == stations - 1) d = path[^1] - path[^2];
            else d = path[i + 1] - path[i - 1];
            tangents[i] = d.Normalized();
        }

        var hint = Vec3.Up;
        var side = tangents[0].Cross(hint);
        if (side.LengthSq < 1e-8) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i < stations; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-8)
                {
                    side = dir.Cross(hint);
                    if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up2 = side.Cross(dir).Normalized();

            double t = i / (double)Math.Max(1, stations - 1);
            double baseR = MathD.Lerp(rBase, rTip, Math.Pow(t, 0.75));

            // Regular nodules spaced along the petiole
            double noduleMod = 0;
            for (int n = 1; n <= nodules; n++)
            {
                double noduleCenter = 0.15 + n * (0.72 / (nodules + 1));
                double dist = (t - noduleCenter) / 0.038;
                noduleMod += noduleAmp * Math.Exp(-dist * dist * 0.5);
            }
            double stationR = baseR * (1.0 + noduleMod);

            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                // Longitudinal fluted ridges
                double ridgeCos = Math.Cos(ridges * th);
                double ridgeSin = Math.Sin(ridges * th);
                double ridgeMod = 1.0 + ridgeAmp * ridgeCos;
                double r = stationR * ridgeMod;

                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var ringTan = (-side * Math.Sin(th) + up2 * Math.Cos(th)).Normalized();
                var pos = path[i] + radial * r;

                // Surface normal accounting for ridge fluting and path direction
                var dPos_dTh = ringTan * r + radial * (stationR * (-ridges * ridgeAmp * ridgeSin));
                var normal = dPos_dTh.Cross(dir).Normalized();
                if (normal.Dot(radial) < 0) normal = -normal;

                var col = Primitives.Mix(baseCol, tipCol, t);
                if (noduleMod > 0.04)
                    col = Primitives.Mix(col, new[] { 0.24, 0.21, 0.15 }, Math.Min(0.48, noduleMod * 1.2));

                if (ridgeCos < 0)
                    col = Primitives.Mix(col, Primitives.Scale(col, 0.82), -ridgeCos * 0.38);
                else
                    col = Primitives.Mix(col, Primitives.Mix(col, new[] { 0.44, 0.49, 0.30 }, 0.28), ridgeCos * 0.28);

                m.AddVertex(pos, normal, col, 1.0, t, (double)s / segments, 0, 0);
            }
        }

        for (int i = 0; i < stations - 1; i++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * (segments + 1) + s;
                int b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }
        m.Structural.Add((start, m.VertexCount - start, segments, rBase));
    }

    // Rhubarb/Darmera-like umbrella: individual petioles, overlapping nearly horizontal leaves.
    private static void MidRainshield(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int leaves = juvenile
            ? 14 + rng.NextInt(5)
            : detail switch
            {
                2 => 14 + rng.NextInt(5),
                1 => 24 + rng.NextInt(7),
                _ => 36 + rng.NextInt(11)
            };

        var stalk = Primitives.Mix(c1, new[] { .29, .32, .23 }, .60);
        var under = Primitives.Mix(c2, new[] { .48, .63, .47 }, .45);
        var petioleBaseCol = Primitives.Mix(stalk, new[] { .33, .30, .22 }, .30);
        var petioleTipCol = Primitives.Mix(stalk, c1, .35);

        // Basal rootstock / crown collar
        var crownCol = Primitives.Mix(MidBark, stalk, .40);
        var rootstockPath = new[]
        {
            new Vec3(0, 0, 0),
            new Vec3(rng.Range(-.006, .006), juvenile ? .02 : .03, rng.Range(-.006, .006)),
            new Vec3(0, juvenile ? .04 : .055, 0)
        };
        var rootstockRadii = new[]
        {
            juvenile ? .065 : .095,
            juvenile ? .075 : .105,
            juvenile ? .045 : .065
        };
        Primitives.Tube(m, rootstockPath, rootstockRadii, detail == 2 ? 4 : 6,
            (i, v) => (crownCol, 1.0, i / 2.0, v, 0, 0));

        double baseAzimuth = rng.Range(0, Math.PI * 2);
        int pathStations = detail == 2 ? 7 : 11;
        int stemSegments = detail == 2 ? 6 : 10;
        int stemRidges = detail == 2 ? 3 : 5;
        int stemNodules = detail == 2 ? 3 : 4;

        for (int k = 0; k < leaves; k++)
        {
            double u = k / (double)Math.Max(1, leaves - 1);
            double a = baseAzimuth + k * 2.399963 + rng.Range(-.12, .12);
            var outward = MidRadial(a);

            // Stems emerge from a compact central basal rootstock/crown (radius ~0.04 to 0.12)
            double rBase = MathD.Lerp(0.04, 0.11, Math.Pow(u, 0.75)) + rng.Range(-.008, .008);
            rBase = Math.Clamp(rBase, 0.04, 0.12);
            double hBase = MathD.Lerp(juvenile ? 0.03 : 0.045, 0.015, u) + rng.Range(-.004, .004);
            hBase = Math.Max(0.01, hBase);
            var foot = outward * rBase + Vec3.Up * hBase;

            // Tiered reach and summit height
            double reachNorm = Math.Pow(u, 0.75);
            double reach = MathD.Lerp(0.16, 0.52, reachNorm) * rng.Range(0.92, 1.08);
            if (juvenile) reach *= 0.75;

            double archPeak = Math.Sin(Math.PI * Math.Pow(u, 0.80));
            double h = (juvenile
                ? MathD.Lerp(0.42, 0.38, u) + 0.20 * archPeak
                : MathD.Lerp(0.64, 0.56, u) + 0.26 * archPeak) + rng.Range(-.03, .03);

            var crown = foot + outward * reach + Vec3.Up * Math.Max(0.10, h - hBase);
            double hSpan = crown.Y - foot.Y;

            // Cantilever arch control points: emerging steep, arching outward, gently flattening near summit
            double k1r = MathD.Lerp(0.06, 0.15, u);
            double k1y = MathD.Lerp(0.52, 0.48, u);
            var p1 = foot + outward * (reach * k1r) + Vec3.Up * (hSpan * k1y);

            double k2r = MathD.Lerp(0.30, 0.42, u);
            double k2y = MathD.Lerp(0.25, -0.03, Math.Pow(u, 0.70));
            var p2 = crown - outward * (reach * k2r) - Vec3.Up * (hSpan * k2y);

            // Multi-station cantilevered arch path
            var path = new Vec3[pathStations];
            double swayAmp = rng.Range(-.015, .015) * (1.0 - 0.4 * u);
            var ortho = MidRadial(a + Math.PI * 0.5);

            for (int s = 0; s < pathStations; s++)
            {
                double t = s / (double)(pathStations - 1);
                double omt = 1.0 - t;
                var pt = foot * (omt * omt * omt)
                       + p1 * (3.0 * omt * omt * t)
                       + p2 * (3.0 * omt * t * t)
                       + crown * (t * t * t);
                pt += ortho * (swayAmp * Math.Sin(Math.PI * t));
                path[s] = pt;
            }

            // Caliper: ~0.018-0.024 base tapering to ~0.010-0.013 at leaf junction
            double baseCal = juvenile ? rng.Range(0.013, 0.017) : rng.Range(0.018, 0.024);
            double junctionCal = juvenile ? rng.Range(0.007, 0.010) : rng.Range(0.010, 0.013);
            double caliperScale = MathD.Lerp(0.90, 1.08, u);
            double rBaseCal = baseCal * caliperScale;
            double rTipCal = junctionCal * caliperScale;

            // Build ribbed petiole with longitudinal ridges and regular nodules
            // Taper the stem tip slightly below the petiole radius so the ribbed tube
            // narrows into the short blending petiole instead of ending bluntly.
            MidRainshieldStem(m, path, rBaseCal, rTipCal * 0.88, petioleBaseCol, petioleTipCol,
                stemSegments, pathStations, stemNodules, stemRidges);

            // Leaves held mostly horizontal, but slightly tipping downward towards the leaf tip
            var horizOutward = new Vec3(outward.X, 0, outward.Z).Normalized();
            double downTilt = rng.Range(0.08, 0.16); // ~5-9 degrees downward from horizontal
            var leafFwd = (horizOutward - Vec3.Up * downTilt).Normalized();

            // Tiered leaf sizing
            double sizeFactor = juvenile ? 0.72 : 1.0;
            double bladeLen = MathD.Lerp(0.20, 0.38, Math.Sin(Math.PI * Math.Pow(u, 0.65))) * sizeFactor * rng.Range(0.92, 1.08);
            if (u >= 0.25 && u <= 0.75)
                bladeLen = Math.Max(bladeLen, (juvenile ? 0.26 : 0.36) * rng.Range(0.95, 1.10));

            double bladeWid = bladeLen * MathD.Lerp(0.95, 1.22, u) * rng.Range(0.96, 1.05);

            double age = MathD.Lerp(0.25, 0.95, Math.Pow(u, 0.75));
            // Slightly deeper camber and midrib fold so the blade reads as a curved
            // surface rather than a wide flat sheet, and the stem can blend into it.
            double camber = MathD.Lerp(0.09, 0.06, u);
            double midribFold = MathD.Lerp(0.06, 0.04, u);
            double wavyMargin = MathD.Lerp(0.02, 0.035, u);
            double tipCurl = MathD.Lerp(0.03, 0.06, u);

            ulong leafSeed = Rng.Mix(seed, (ulong)(k * 439 + 13));

            double blendPetioleLength = Math.Max(0.010, rTipCal * 1.35);
            Broadleaf.BuildLeaf(m, crown, leafFwd, bladeLen, bladeWid,
                BroadleafOutline.OrbicularCordate, leafSeed, c1, c2,
                age: age, camber: camber, midribFold: midribFold,
                wavyMargin: wavyMargin, wavyFrequency: 3.2, tipCurl: tipCurl,
                petioleLength: blendPetioleLength, petioleRadius: rTipCal, petioleKink: 0.18,
                undersideColor: under, detailOverride: detail);
        }
    }

}
