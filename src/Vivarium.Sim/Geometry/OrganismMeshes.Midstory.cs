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
    private static void MidStem(MeshData m, double[] bark, Vec3[] points, double baseRadius)
    {
        double[] r = new double[points.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = Math.Max(.0015, baseRadius * Math.Pow(1 - i / (double)points.Length, .95));
        Primitives.Tube(m, points, r, m.FloraDetailLevel == 2 ? 4 : 6,
            (i, v) => (bark, 1, i / (double)Math.Max(1, points.Length - 1), v, 0, 0));
    }

    private static void MidBlade(MeshData m, Vec3 attach, Vec3 direction, double length, double width,
        double[] c1, double[] c2, ulong leafSeed, bool round = false, double drop = .035)
    {
        var dir = direction.Normalized();
        if (dir.LengthSq < 1e-7) dir = new Vec3(1, 0, 0);
        var side = dir.Cross(Vec3.Up).Normalized();
        if (side.LengthSq < 1e-7) side = new Vec3(0, 0, 1);
        var end = attach + dir * length - Vec3.Up * drop;
        var ctrl = attach + dir * (length * .56) + Vec3.Up * (length * (round ? .06 : .09));
        CurvedBlade(m, attach, ctrl, end, side, width, c1, c2,
            segments: m.FloraDetailLevel == 2 ? 4 : 7,
            twist: (Rng.HashUnit(leafSeed, 0x901UL, 0) - .5) * .24,
            shoulder: round ? .72 : 1.10, serration: round ? 0 : .035);
    }

    // Woody stem with smooth pale greenish-grey bark and dark furrowed splits on older lower trunk.
    private static void MidSplitcrownWoodyStem(MeshData m, Vec3[] points, double baseRadius, bool isLowerTrunk, int detail)
    {
        int stations = detail == 2 ? 6 : (isLowerTrunk ? 12 : 8);
        int segments = detail == 2 ? 6 : (isLowerTrunk ? 10 : 7);
        int start = m.VertexCount;

        var path = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            if (points.Length == 3)
            {
                double u = 1.0 - t;
                path[i] = points[0] * (u * u) + points[1] * (2.0 * u * t) + points[2] * (t * t);
            }
            else
            {
                double st = t * (points.Length - 1);
                int idx = Math.Min(points.Length - 2, (int)st);
                double frac = st - idx;
                path[i] = points[idx] * (1.0 - frac) + points[idx + 1] * frac;
            }
        }

        var dir0 = (path[1] - path[0]).Normalized();
        var hint = Vec3.Up;
        var side = dir0.Cross(hint);
        if (side.LengthSq < 1e-8) side = dir0.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        // Smooth pale grey/greenish bark (poplar/aspen stipe) with dark split wood in mature fissures
        double[] smoothGrey = { 0.55, 0.57, 0.52 };
        double[] smoothGreyLight = { 0.62, 0.64, 0.58 };
        double[] darkSplit = { 0.17, 0.14, 0.11 };
        double[] darkWoodEdge = { 0.28, 0.24, 0.19 };

        int splits = isLowerTrunk ? 4 : 0;

        for (int i = 0; i < stations; i++)
        {
            var dir = i == 0 ? (path[1] - path[0]).Normalized()
                    : i == stations - 1 ? (path[^1] - path[^2]).Normalized()
                    : (path[i + 1] - path[i - 1]).Normalized();

            side = (side - dir * side.Dot(dir)).Normalized();
            var up2 = side.Cross(dir).Normalized();

            double t = i / (double)(stations - 1);
            double taperR = Math.Max(0.002, baseRadius * (1.0 - 0.52 * Math.Pow(t, 0.90)));

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2 * Math.PI * u;

                // Dark splits in older mature bark (lower trunk)
                double splitFactor = 0.0;
                double splitIndent = 0.0;
                if (isLowerTrunk && t < 0.65)
                {
                    double splitWave = Math.Cos(th * splits);
                    double heightMaturity = 1.0 - t / 0.65;
                    if (splitWave > 0.38)
                    {
                        double fissureT = (splitWave - 0.38) / 0.62;
                        splitFactor = fissureT * heightMaturity;
                        splitIndent = 0.14 * splitFactor;
                    }
                }

                double r = taperR * (1.0 - splitIndent);
                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var pos = path[i] + radial * r;
                var normal = radial;

                // Smooth grey bark with dark furrowed splits
                var barkBase = Primitives.Mix(smoothGrey, smoothGreyLight, t);
                var col = barkBase;
                if (splitFactor > 0.04)
                {
                    var splitCol = Primitives.Mix(darkWoodEdge, darkSplit, splitFactor);
                    col = Primitives.Mix(barkBase, splitCol, Math.Min(1.0, splitFactor * 1.4));
                }

                // UV2.x = 2.0 marks woody tissue for the flora shader
                m.AddVertex(pos, normal, col, 1.0, path[i].Y * 3.0, u, 2.0, 0);
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
        m.Structural.Add((start, m.VertexCount - start, segments, baseRadius));
    }

    // Small forked tree with smooth grey bark, dark mature splits, and broad poplar foliage.
    private static void MidSplitcrown(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int trunks = juvenile ? 1 : 2 + rng.NextInt(2);
        var under = Primitives.Mix(c2, new[] { 0.55, 0.65, 0.48 }, 0.45);
        int detail = m.FloraDetailLevel ?? 0;

        for (int tr = 0; tr < trunks; tr++)
        {
            double az = tr * 2.399963 + rng.Range(-.20, .20);
            var radial = MidRadial(az);
            var basePos = radial * (tr == 0 ? 0 : rng.Range(.04, .14));

            // Shorter habit: trunk forks much lower (about half original height)
            double trunkTop = juvenile ? rng.Range(.26, .34) : rng.Range(.34, .44);
            var p1 = basePos * .50 + Vec3.Up * (trunkTop * .42);
            var fork = radial * rng.Range(.05, .13) + Vec3.Up * trunkTop;

            // Lower trunk with smooth grey bark and dark mature splits
            MidSplitcrownWoodyStem(m, new[] { basePos, p1, fork }, juvenile ? .036 : .050, isLowerTrunk: true, detail);

            int branches = juvenile ? 3 : 4 + rng.NextInt(2);
            for (int b = 0; b < branches; b++)
            {
                double ba = az + 2 * Math.PI * b / branches + rng.Range(-.25, .25);
                var outward = MidRadial(ba);
                double reach = rng.Range(.48, .76);
                var join = fork + outward * (reach * .42) + Vec3.Up * rng.Range(.10, .18);
                double tipLift = b == 0 ? rng.Range(.28, .36) : rng.Range(.20, .30);
                var tip = fork + outward * reach + Vec3.Up * tipLift;
                tip = new Vec3(tip.X, Math.Clamp(tip.Y, .58, .74), tip.Z);

                // Scaffold branch
                MidSplitcrownWoodyStem(m, new[] { fork, join, tip }, .018, isLowerTrunk: false, detail);

                // Dedicated terminal spray of 2 or 3 poplar leaves at the very end of each scaffold branch
                int branchEndLeaves = 2 + rng.NextInt(2); // 2 or 3 leaves
                for (int bl = 0; bl < branchEndLeaves; bl++)
                {
                    double spread = (bl - (branchEndLeaves - 1) * 0.5) * 0.55;
                    var endDir = (outward * 0.85 + MidRadial(ba + Math.PI * 0.5) * spread + Vec3.Up * rng.Range(0.04, 0.18)).Normalized();
                    double endBladeLen = rng.Range(0.12, 0.18);
                    double endBladeWid = endBladeLen * rng.Range(0.88, 0.98);
                    ulong endSeed = Rng.Mix(seed, (ulong)(tr * 200000 + b * 20000 + bl * 100 + 43));
                    Broadleaf.BuildLeaf(m, tip, endDir, endBladeLen, endBladeWid,
                        BroadleafOutline.Ovate, endSeed, c1, c2,
                        age: rng.Range(0.65, 0.90), camber: 0.055, midribFold: 0.038,
                        wavyMargin: 0.030, wavyFrequency: 4.2, tipCurl: 0.035,
                        petioleLength: endBladeLen * 0.42, petioleRadius: 0.0035, petioleKink: 0.16,
                        undersideColor: under, detailOverride: detail);
                }

                int branchlets = detail == 2 ? 3 : (juvenile ? 3 : 5 + rng.NextInt(2));
                for (int n = 0; n < branchlets; n++)
                {
                    double frac = 0.20 + 0.70 * (n / (double)Math.Max(1, branchlets - 1));
                    var origin = fork * (1 - frac) + tip * frac;
                    double branchletAngle = ba + ((n % 2 == 0) ? 0.60 : -0.60) * rng.Range(0.75, 1.25);
                    var spray = (MidRadial(branchletAngle) * rng.Range(.75, 1.0) + outward * 0.35
                        + Vec3.Up * rng.Range(-.02, .18)).Normalized();
                    var twigEnd = origin + spray * rng.Range(.20, .32) + Vec3.Up * rng.Range(.02, .08);

                    // Twig
                    MidSplitcrownWoodyStem(m, new[] { origin, twigEnd }, .007, isLowerTrunk: false, detail);

                    // Broad, deltoid/ovate poplar leaves on slender petioles along the twig
                    int leafCount = detail == 2 ? 4 : (juvenile ? 5 : 7 + rng.NextInt(3));
                    for (int l = 0; l < leafCount; l++)
                    {
                        double lt = (l + 0.5) / leafCount;
                        var attach = origin * (1 - lt) + twigEnd * lt;
                        double lAngle = branchletAngle + (l % 2 == 0 ? 0.85 : -0.85) + rng.Range(-0.15, 0.15);
                        var lOut = MidRadial(lAngle);
                        var lDir = (lOut * 0.85 + Vec3.Up * rng.Range(-0.06, 0.14)).Normalized();

                        double bladeLen = rng.Range(0.10, 0.16);
                        double bladeWid = bladeLen * rng.Range(0.88, 0.98); // Broad poplar deltoid proportions

                        ulong leafSeed = Rng.Mix(seed, (ulong)(tr * 100000 + b * 10000 + n * 1000 + l * 10 + 7));

                        Broadleaf.BuildLeaf(m, attach, lDir, bladeLen, bladeWid,
                            BroadleafOutline.Ovate, leafSeed, c1, c2,
                            age: rng.Range(0.70, 0.96), camber: 0.05, midribFold: 0.038,
                            wavyMargin: 0.030, wavyFrequency: 4.2, tipCurl: 0.035,
                            petioleLength: bladeLen * 0.40, petioleRadius: 0.0035, petioleKink: 0.16,
                            undersideColor: under, detailOverride: detail);
                    }

                    // Terminal pair at twig end
                    for (int tl = -1; tl <= 1; tl += 2)
                    {
                        var tDir = (spray * 0.82 + MidRadial(branchletAngle + tl * 0.45) * 0.40 + Vec3.Up * rng.Range(0.02, 0.12)).Normalized();
                        double tLen = rng.Range(0.10, 0.15);
                        ulong tSeed = Rng.Mix(seed, (ulong)(tr * 150000 + b * 15000 + n * 1500 + (tl + 1) * 33 + 1));
                        Broadleaf.BuildLeaf(m, twigEnd, tDir, tLen, tLen * 0.92,
                            BroadleafOutline.Ovate, tSeed, c1, c2,
                            age: rng.Range(0.68, 0.92), camber: 0.05, midribFold: 0.038,
                            wavyMargin: 0.030, wavyFrequency: 4.2, tipCurl: 0.035,
                            petioleLength: tLen * 0.38, petioleRadius: 0.0035, petioleKink: 0.16,
                            undersideColor: under, detailOverride: detail);
                    }
                }
            }
        }
    }

    // Stout tree-fern caudex with light woody regular macro-tessellated leaf scars, tapering rapidly to a rounded dome crown.
    private static (Vec3 Apex, double ApexRadius) MidVaultfernCaudex(MeshData m, Rng rng, double height, double rBase, int detail)
    {
        int stations = detail == 2 ? 10 : 16;
        int segments = detail == 2 ? 8 : 12;
        int start = m.VertexCount;

        // Path curves subtly like a real fibrous tree-fern trunk
        var path = new Vec3[stations];
        double leanAz = rng.Range(0, Math.PI * 2);
        var leanDir = MidRadial(leanAz);
        for (int i = 0; i < stations; i++)
        {
            double v = i / (double)(stations - 1);
            double y = height * v;
            double sway = Math.Sin(Math.PI * v) * rng.Range(0.012, 0.026);
            path[i] = leanDir * sway + Vec3.Up * y;
        }

        // Taper profile: thick base, stout lower/mid stipe, rapid upper taper to rounded top
        var radii = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            double v = i / (double)(stations - 1);
            if (v < 0.68)
            {
                // Stout body with slight basal flare
                radii[i] = rBase * MathD.Lerp(1.18, 0.82, Math.Pow(v / 0.68, 0.80));
            }
            else
            {
                // Taper pretty quickly above 68% height to a rounded dome top
                double topFrac = (v - 0.68) / 0.32;
                radii[i] = rBase * MathD.Lerp(0.82, 0.34, Math.Pow(topFrac, 1.35));
            }
        }

        var hint = Vec3.Up;
        var dir0 = (path[1] - path[0]).Normalized();
        var side = dir0.Cross(hint);
        if (side.LengthSq < 1e-8) side = dir0.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        // Lighter weathered wood palette (inspired by WoodyBarkLight in Embercrown and Shadebell)
        double[] creviceWood = { 0.34, 0.30, 0.23 };
        double[] bodyWood = { 0.48, 0.42, 0.33 };
        double[] crestWood = { 0.58, 0.52, 0.40 };

        const double numRows = 7.0;
        const double numCols = 6.0;

        for (int i = 0; i < stations; i++)
        {
            var dir = i == 0 ? (path[1] - path[0]).Normalized()
                    : i == stations - 1 ? (path[^1] - path[^2]).Normalized()
                    : (path[i + 1] - path[i - 1]).Normalized();

            side = (side - dir * side.Dot(dir)).Normalized();
            var up2 = side.Cross(dir).Normalized();

            double v = i / (double)(stations - 1);
            double stationR = radii[i];
            double y = height * v;

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2 * Math.PI * u;

                // Regular macro pattern: staggered rhombic/hexagonal leaf scar cushions
                double rowCoord = v * numRows;
                double stagger = ((int)Math.Floor(rowCoord) % 2) * 0.5;
                double colCoord = u * numCols + stagger;
                double cellU = (colCoord - Math.Floor(colCoord)) - 0.5;
                double cellV = (rowCoord - Math.Floor(rowCoord)) - 0.5;
                double diamondDist = (Math.Abs(cellU) + Math.Abs(cellV)) * 2.0;
                double cushionPillow = Math.Clamp(1.0 - diamondDist, 0.0, 1.0);
                double pillowRelief = Math.Sin(cushionPillow * Math.PI * 0.5);

                double tessRelief = v < 0.86 ? 0.09 * pillowRelief : 0.0;
                double r = stationR * (1.0 + tessRelief);

                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var pos = path[i] + radial * r;
                var normal = radial;

                // Lighter woody coloration with distinct macro scar definition
                var col = Primitives.Mix(creviceWood, bodyWood, Math.Min(1.0, pillowRelief * 1.5));
                if (pillowRelief > 0.42)
                    col = Primitives.Mix(col, crestWood, (pillowRelief - 0.42) * 1.7);
                if (v < 0.12)
                    col = Primitives.Mix(col, new[] { 0.32, 0.36, 0.24 }, (1.0 - v / 0.12) * 0.35);

                // UV2.x = 2.0 marks woody tissue for the flora shader
                m.AddVertex(pos, normal, col, 1.0, y * 2.5, u, 2.0, 0);
            }
        }

        // Triangles along trunk
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

        // Rounded top dome cap
        double apexR = radii[^1];
        var apexCenter = path[^1] + Vec3.Up * (apexR * 0.75);
        int apexIdx = m.AddVertex(apexCenter, Vec3.Up, crestWood, 1.0, (height + apexR * 0.75) * 2.5, 0.5, 2.0, 0);
        int topRowStart = start + (stations - 1) * (segments + 1);
        for (int s = 0; s < segments; s++)
        {
            m.AddTriangle(topRowStart + s, topRowStart + s + 1, apexIdx);
        }

        m.Structural.Add((start, m.VertexCount - start, segments, rBase));
        return (apexCenter, apexR);
    }

    // Tree-fern analogue: stout fibrous stipe under umbrella of compound drooping fronds.
    private static void MidVaultfern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bark = new[] { .25, .28, .19 };

        // Caudex is about a third shorter, thicker, and tapers rapidly to a rounded top
        double height = juvenile ? 0.36 : rng.Range(0.50, 0.56);
        double rBase = juvenile ? 0.075 : rng.Range(0.115, 0.14);
        var (apex, apexR) = MidVaultfernCaudex(m, rng, height, rBase, detail);

        int fronds = juvenile ? 6 : (detail == 2 ? 8 : 12 + rng.NextInt(3));
        var low = Primitives.Scale(c1, .76);

        for (int f = 0; f < fronds; f++)
        {
            double angle = 2 * Math.PI * f / fronds + rng.Range(-.12, .12);
            var radial = MidRadial(angle);
            var ortho = MidRadial(angle + Math.PI * .5);
            double reach = (juvenile ? .46 : rng.Range(.68, .96));

            // Fronds project out directly from the rounded dome top
            var root = apex - Vec3.Up * (apexR * 0.25) + radial * (apexR * 0.85);
            int stations = 9;
            var path = new Vec3[stations];
            var radii = new double[stations];
            for (int s = 0; s < stations; s++)
            {
                double t = s / (double)(stations - 1);
                path[s] = root + radial * (reach * t) + ortho * (.045 * Math.Sin(t * Math.PI * 1.3))
                    + Vec3.Up * (.22 * Math.Sin(Math.PI * t) - .20 * t * t);
                radii[s] = .014 * (1 - .85 * t);
            }
            Primitives.Tube(m, path, radii, detail == 2 ? 3 : 5,
                (i, v) => (bark, 1, i, v, 0, 0));
            int pairs = detail == 2 ? 7 : 13;
            for (int p = 1; p <= pairs; p++)
            {
                double t = p / (double)(pairs + 1);
                double a = t * (stations - 1);
                int segment = Math.Min(stations - 2, (int)a);
                var node = path[segment] * (1 - (a - segment)) + path[segment + 1] * (a - segment);
                double extent = (.17 + .20 * Math.Sin(Math.PI * t)) * Math.Pow(1 - t, .25);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var end = node + ortho * (sign * extent) + radial * extent * .11
                        - Vec3.Up * (extent * rng.Range(.12, .31));
                    var control = (node + end) * .5 + Vec3.Up * .025;
                    MidBlade(m, node, end - node, (end - node).Length, extent * .21,
                        c1, low, Rng.Mix(seed, (ulong)(f * 1000 + p * 10 + sign + 3)), drop: .018);
                    if (detail == 0 && p % 2 == 0)
                    {
                        // Sparse secondary pinnae create the characteristic fern fractal edge.
                        var subRoot = node + (end - node) * .62;
                        var subDir = ((end - node).Normalized() * .35 + radial * (sign * .45)
                            + Vec3.Up * .1).Normalized();
                        MidBlade(m, subRoot, subDir, extent * .31, extent * .035,
                            c1, low, Rng.Mix(seed, (ulong)(f * 600 + p * 9 + sign + 50)), drop: .004);
                    }
                }
            }
        }
    }

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
            MidRainshieldStem(m, path, rBaseCal, rTipCal, petioleBaseCol, petioleTipCol,
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
            double camber = MathD.Lerp(0.05, 0.035, u);
            double midribFold = MathD.Lerp(0.04, 0.025, u);
            double wavyMargin = MathD.Lerp(0.02, 0.035, u);
            double tipCurl = MathD.Lerp(0.03, 0.06, u);

            ulong leafSeed = Rng.Mix(seed, (ulong)(k * 439 + 13));

            Broadleaf.BuildLeaf(m, crown, leafFwd, bladeLen, bladeWid,
                BroadleafOutline.OrbicularCordate, leafSeed, c1, c2,
                age: age, camber: camber, midribFold: midribFold,
                wavyMargin: wavyMargin, wavyFrequency: 3.2, tipCurl: tipCurl,
                petioleLength: 0, petioleRadius: rTipCal, petioleKink: 0,
                undersideColor: under, detailOverride: detail);
        }
    }

    // Fine-grained horizontal foliage shelves; dark spaces remain visible between tiers.
    private static void MidLacemantle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bark = Primitives.Mix(MidBark, new[] { .27, .22, .24 }, .28);
        int stems = juvenile ? 2 : 3 + rng.NextInt(2);
        for (int s = 0; s < stems; s++)
        {
            double phase = rng.Range(0, Math.PI * 2);
            var rootDir = MidRadial(phase);
            var root = rootDir * rng.Range(0, .10);
            double height = rng.Range(.83, .99);
            var mid = root + rootDir * .10 + Vec3.Up * (height * .51);
            var top = root + rootDir * .15 + Vec3.Up * height;
            MidStem(m, bark, new[] { root, mid, top }, juvenile ? .025 : .040);
            int tiers = juvenile ? 2 : (detail == 2 ? 3 : 4);
            for (int t = 0; t < tiers; t++)
            {
                double ht = .38 + t * (.55 / (tiers - 1));
                var junction = root * (1 - ht) + top * ht;
                int arms = detail == 2 ? 2 : 3;
                for (int a = 0; a < arms; a++)
                {
                    double az = phase + (a * 2 * Math.PI / arms) + (t % 2) * .7 + rng.Range(-.24, .24);
                    var outward = MidRadial(az);
                    double reach = rng.Range(.43, .79) * (1 - .22 * t / (double)tiers);
                    var bend = junction + outward * reach * .54 + Vec3.Up * .025;
                    var end = junction + outward * reach + Vec3.Up * rng.Range(-.07, .05);
                    MidStem(m, bark, new[] { junction, bend, end }, .009);
                    int twigs = detail == 2 ? 3 : 5;
                    for (int q = 0; q < twigs; q++)
                    {
                        var anchor = bend * (1 - q / (double)(twigs + 1)) + end * (q / (double)(twigs + 1));
                        double twigAz = az + rng.Range(-.95, .95);
                        var dir = MidRadial(twigAz);
                        var twigTip = anchor + dir * rng.Range(.12, .23) + Vec3.Up * rng.Range(-.06, .07);
                        MidStem(m, bark, new[] { anchor, twigTip }, .0036);
                        int leafCount = detail == 2 ? 3 : 5;
                        for (int l = 0; l < leafCount; l++)
                        {
                            var attach = anchor * (1 - l / (double)leafCount) + twigTip * (l / (double)leafCount);
                            var leafDir = (dir * .35 + MidRadial(twigAz + (l % 2 == 0 ? 1.10 : -1.10))
                                + Vec3.Up * rng.Range(-.08, .10)).Normalized();
                            double len = rng.Range(.085, .15);
                            MidBlade(m, attach, leafDir, len, len * .27, c1, c2,
                                Rng.Mix(seed, (ulong)(s * 100000 + t * 10000 + a * 1000 + q * 100 + l)));
                        }
                    }
                }
            }
        }
    }

    // Gnarled woody stem with weathered stringy juniper bark (cinnamon underwood with weathered ash ridges)
    private static void MidCragparasolGnarledStem(MeshData m, Vec3[] points, double rBase, double rTip, int detail, int ridges = 6)
    {
        int stations = points.Length;
        int segments = detail == 2 ? 4 : (detail == 1 ? 6 : 8);
        int start = m.VertexCount;

        var tangents = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            Vec3 d;
            if (i == 0) d = points[1] - points[0];
            else if (i == stations - 1) d = points[^1] - points[^2];
            else d = points[i + 1] - points[i - 1];
            tangents[i] = d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
        }

        var hint = Vec3.Up;
        var side = tangents[0].Cross(hint);
        if (side.LengthSq < 1e-8) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        double[] underWood = { 0.34, 0.22, 0.16 }; // Warm cinnamon/red-brown
        double[] ashBark = { 0.50, 0.48, 0.42 };   // Weathered silver-ash
        double[] crestWood = { 0.58, 0.56, 0.50 }; // Bleached grey highlights
        double[] darkCrevice = { 0.22, 0.16, 0.12 }; // Deep stringy furrow

        for (int i = 0; i < stations; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = (side - dir * side.Dot(dir)).Normalized();
                if (side.LengthSq < 1e-8)
                {
                    side = dir.Cross(hint);
                    if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
                    side = side.Normalized();
                }
            }
            var up2 = side.Cross(dir).Normalized();

            double t = i / (double)Math.Max(1, stations - 1);
            double baseR = MathD.Lerp(rBase, rTip, Math.Pow(t, 0.85));

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2 * Math.PI * u;

                // Shredded stringy fibrous bark strips
                double stripWave = Math.Cos(ridges * th);
                double stripMod = 1.0 + 0.14 * stripWave;
                double r = Math.Max(0.0015, baseR * stripMod);

                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var pos = points[i] + radial * r;
                var normal = radial;

                // Weathered bark palette
                var baseCol = Primitives.Mix(underWood, ashBark, Math.Min(1.0, 0.35 + 0.65 * t));
                if (stripWave < -0.30)
                {
                    double furr = (-stripWave - 0.30) / 0.70;
                    baseCol = Primitives.Mix(baseCol, darkCrevice, furr * 0.75);
                }
                else if (stripWave > 0.30)
                {
                    double crest = (stripWave - 0.30) / 0.70;
                    baseCol = Primitives.Mix(baseCol, crestWood, crest * 0.60);
                }

                // UV2.x = 2.0 marks woody tissue for the flora shader
                m.AddVertex(pos, normal, baseCol, 1.0, points[i].Y * 3.5, u, 2.0, 0);
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

    // Windswept rocky-hillside juniper habit: shorter, thinner gnarled zig-zag stems
    // and dense untidy outsprays of small, narrow lanceolate leaves along each branch.
    private static void MidCragparasol(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        double az = rng.Range(0, Math.PI * 2);
        var lean = MidRadial(az);
        var orthoTrunk = MidRadial(az + Math.PI * 0.5);

        // 1. Gnarled, crooked lower trunk (leaning, twisted with an angular knee/elbow)
        var foot = Vec3.Zero;
        double kneeHeight = juvenile ? rng.Range(0.10, 0.14) : rng.Range(0.13, 0.18);
        var knee = lean * rng.Range(0.06, 0.11) + orthoTrunk * rng.Range(-0.03, 0.03) + Vec3.Up * kneeHeight;

        double forkHeight = juvenile ? rng.Range(0.19, 0.25) : rng.Range(0.25, 0.33);
        double forkLean = rng.Range(0.10, 0.18);
        var fork = lean * forkLean + orthoTrunk * rng.Range(-0.05, 0.05) + Vec3.Up * forkHeight;

        int trunkStations = detail == 2 ? 4 : 5;
        var trunkPoints = new Vec3[trunkStations];
        for (int i = 0; i < trunkStations; i++)
        {
            double t = i / (double)(trunkStations - 1);
            if (t <= 0.5)
            {
                double u = t * 2.0;
                trunkPoints[i] = foot * (1.0 - u) + knee * u;
            }
            else
            {
                double u = (t - 0.5) * 2.0;
                trunkPoints[i] = knee * (1.0 - u) + fork * u;
            }
        }

        double trunkBaseR = juvenile ? rng.Range(0.019, 0.024) : rng.Range(0.028, 0.036);
        double trunkForkR = trunkBaseR * 0.72;
        MidCragparasolGnarledStem(m, trunkPoints, trunkBaseR, trunkForkR, detail);

        // 2. Gnarled scaffold limbs radiating outward with tortuous angular bends
        int limbs = juvenile ? 3 : (detail == 2 ? 3 : 3 + rng.NextInt(2));
        var limbDirections = new double[limbs];
        double baseLimbAz = az + rng.Range(0.3, 0.7);
        for (int k = 0; k < limbs; k++)
        {
            limbDirections[k] = baseLimbAz + 2 * Math.PI * k / limbs + rng.Range(-0.25, 0.25);
        }

        var underColor = Primitives.Mix(c2, new[] { 0.44, 0.50, 0.34 }, 0.45);

        for (int k = 0; k < limbs; k++)
        {
            double a = limbDirections[k];
            var outward = MidRadial(a);
            var ortho = MidRadial(a + Math.PI * 0.5);

            // Limb reaches outward and curves up to crown summit
            double reach = juvenile ? rng.Range(0.50, 0.65) : rng.Range(0.65, 0.82);
            double summitY = (juvenile ? rng.Range(0.68, 0.72) : rng.Range(0.71, 0.75));

            int limbStations = detail == 2 ? 4 : 5;
            var limbPath = new Vec3[limbStations];

            // Elbow 1: pushes out and tilts
            double j1Reach = reach * rng.Range(0.24, 0.32);
            double j1Y = MathD.Lerp(forkHeight, summitY, 0.28) + rng.Range(-0.02, 0.03);
            var p1 = fork + outward * j1Reach + ortho * rng.Range(-0.06, 0.06) + Vec3.Up * (j1Y - forkHeight);

            // Knee 2: sharp zigzag crook, dipping slightly or jutting sideways
            double j2Reach = reach * rng.Range(0.52, 0.62);
            double j2Y = MathD.Lerp(forkHeight, summitY, 0.55) + rng.Range(-0.04, 0.02);
            var p2 = fork + outward * j2Reach + ortho * rng.Range(-0.09, 0.09) + Vec3.Up * (j2Y - forkHeight);

            // Elbow 3: rising toward crown
            double j3Reach = reach * rng.Range(0.78, 0.88);
            double j3Y = MathD.Lerp(forkHeight, summitY, 0.85) + rng.Range(-0.02, 0.03);
            var p3 = fork + outward * j3Reach + ortho * rng.Range(-0.05, 0.05) + Vec3.Up * (j3Y - forkHeight);

            // Tip
            var p4 = fork + outward * reach + ortho * rng.Range(-0.04, 0.04) + Vec3.Up * (summitY - forkHeight);

            limbPath[0] = fork;
            if (limbStations == 4)
            {
                limbPath[1] = p1;
                limbPath[2] = p2;
                limbPath[3] = p4;
            }
            else
            {
                limbPath[1] = p1;
                limbPath[2] = p2;
                limbPath[3] = p3;
                limbPath[4] = p4;
            }

            double limbBaseR = trunkForkR * rng.Range(0.62, 0.75);
            double limbTipR = Math.Max(0.0035, limbBaseR * 0.40);
            MidCragparasolGnarledStem(m, limbPath, limbBaseR, limbTipR, detail);

            // 3. Secondary gnarled lateral branches emerging from the main limb
            int secBranches = juvenile ? 1 : (detail == 2 ? 1 : 2 + rng.NextInt(2));
            var secEndpoints = new List<(Vec3 Start, Vec3 End, Vec3 Dir)>();

            for (int b = 0; b < secBranches; b++)
            {
                double branchFrac = b == 0 ? rng.Range(0.38, 0.52) : rng.Range(0.65, 0.80);
                int segIdx = Math.Min(limbStations - 2, (int)(branchFrac * (limbStations - 1)));
                double segT = (branchFrac * (limbStations - 1)) - segIdx;
                var branchRoot = limbPath[segIdx] * (1.0 - segT) + limbPath[segIdx + 1] * segT;

                double sideSign = (b % 2 == 0) ? 1.0 : -1.0;
                double secAz = a + sideSign * rng.Range(0.65, 1.25);
                var secOut = MidRadial(secAz);
                double secReach = rng.Range(0.18, 0.32) * (juvenile ? 0.75 : 1.0);
                double secRise = rng.Range(0.04, 0.12);

                var secKnee = branchRoot + secOut * (secReach * 0.52) + Vec3.Up * (secRise * 0.4) + ortho * (sideSign * 0.03);
                var secTip = branchRoot + secOut * secReach + Vec3.Up * secRise;

                var secPath = new[] { branchRoot, secKnee, secTip };
                double secBaseR = limbBaseR * 0.55;
                double secTipR = Math.Max(0.0025, secBaseR * 0.45);
                MidCragparasolGnarledStem(m, secPath, secBaseR, secTipR, detail);

                secEndpoints.Add((branchRoot, secTip, (secTip - branchRoot).Normalized()));
            }

            // 4. Dense untidy outsprays of small, narrow leaves along the limb and its secondary branches
            int sprayStations = juvenile ? 2 : (detail == 2 ? 2 : (detail == 1 ? 3 : 5));
            for (int s = 0; s < sprayStations; s++)
            {
                double stT = MathD.Lerp(0.30, 0.88, (s + 0.5) / sprayStations);
                int segIdx = Math.Min(limbStations - 2, (int)(stT * (limbStations - 1)));
                double segT = (stT * (limbStations - 1)) - segIdx;
                var attach = limbPath[segIdx] * (1.0 - segT) + limbPath[segIdx + 1] * segT;
                var limbDir = (limbPath[segIdx + 1] - limbPath[segIdx]).Normalized();

                int clusterLeaves = detail == 2 ? 2 : (juvenile ? 2 : 3 + (s % 2));
                for (int l = 0; l < clusterLeaves; l++)
                {
                    double lAz = a + rng.Range(-1.15, 1.15);
                    double lPitch = rng.Range(-0.18, 0.38);
                    var lDir = (MidRadial(lAz) * 0.65 + limbDir * 0.35 + Vec3.Up * lPitch).Normalized();

                    double bladeLen = (juvenile ? rng.Range(0.060, 0.082) : rng.Range(0.075, 0.115));
                    double bladeWid = bladeLen * rng.Range(0.20, 0.24); // Small narrow leaf
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 10000 + s * 100 + l * 7 + 1));

                    Broadleaf.BuildLeaf(m, attach, lDir, bladeLen, bladeWid,
                        BroadleafOutline.Lanceolate, lSeed, c1, c2,
                        age: rng.Range(0.65, 0.95), camber: 0.07, midribFold: 0.09,
                        wavyMargin: 0.015, wavyFrequency: 3.2, tipCurl: 0.05,
                        petioleLength: 0.012, petioleRadius: 0.0018, petioleKink: 0.12,
                        undersideColor: underColor, detailOverride: Math.Max(1, detail));
                }
            }

            // Terminal outspray of leaves at the end of the main limb
            int terminalLeaves = detail == 2 ? 3 : (juvenile ? 4 : (detail == 1 ? 5 : 8 + rng.NextInt(3)));
            var tipAttach = limbPath[^1];
            var tipDir = (limbPath[^1] - limbPath[^2]).Normalized();
            for (int l = 0; l < terminalLeaves; l++)
            {
                double lAz = a + 2 * Math.PI * l / terminalLeaves + rng.Range(-0.28, 0.28);
                double lPitch = rng.Range(-0.06, 0.32);
                var lDir = (MidRadial(lAz) * 0.55 + tipDir * 0.45 + Vec3.Up * lPitch).Normalized();

                double bladeLen = (juvenile ? rng.Range(0.065, 0.088) : rng.Range(0.085, 0.125));
                double bladeWid = bladeLen * rng.Range(0.20, 0.24);
                ulong lSeed = Rng.Mix(seed, (ulong)(k * 20000 + l * 19 + 55));

                Broadleaf.BuildLeaf(m, tipAttach, lDir, bladeLen, bladeWid,
                    BroadleafOutline.Lanceolate, lSeed, c1, c2,
                    age: rng.Range(0.70, 0.98), camber: 0.07, midribFold: 0.09,
                    wavyMargin: 0.015, wavyFrequency: 3.2, tipCurl: 0.05,
                    petioleLength: 0.014, petioleRadius: 0.0018, petioleKink: 0.12,
                    undersideColor: underColor, detailOverride: Math.Max(1, detail));
            }

            // Outsprays on secondary branches
            for (int b = 0; b < secEndpoints.Count; b++)
            {
                var (secStart, secEnd, sDir) = secEndpoints[b];

                if (detail < 2)
                {
                    var midAttach = (secStart + secEnd) * 0.5;
                    int midCount = juvenile ? 1 : 2;
                    for (int l = 0; l < midCount; l++)
                    {
                        double lAz = a + (l % 2 == 0 ? 0.7 : -0.7) + rng.Range(-0.35, 0.35);
                        var lDir = (MidRadial(lAz) * 0.60 + sDir * 0.40 + Vec3.Up * rng.Range(-0.15, 0.25)).Normalized();
                        double bladeLen = (juvenile ? rng.Range(0.055, 0.075) : rng.Range(0.070, 0.100));
                        double bladeWid = bladeLen * rng.Range(0.20, 0.24);
                        ulong lSeed = Rng.Mix(seed, (ulong)(k * 30000 + b * 200 + l * 13 + 3));

                        Broadleaf.BuildLeaf(m, midAttach, lDir, bladeLen, bladeWid,
                            BroadleafOutline.Lanceolate, lSeed, c1, c2,
                            age: rng.Range(0.65, 0.95), camber: 0.07, midribFold: 0.09,
                            wavyMargin: 0.015, wavyFrequency: 3.2, tipCurl: 0.05,
                            petioleLength: 0.012, petioleRadius: 0.0018, petioleKink: 0.12,
                            undersideColor: underColor, detailOverride: Math.Max(1, detail));
                    }
                }

                int secTipLeaves = detail == 2 ? 2 : (juvenile ? 3 : (detail == 1 ? 3 : 5 + rng.NextInt(2)));
                for (int l = 0; l < secTipLeaves; l++)
                {
                    double lAz = a + 2 * Math.PI * l / secTipLeaves + rng.Range(-0.3, 0.3);
                    var lDir = (MidRadial(lAz) * 0.55 + sDir * 0.45 + Vec3.Up * rng.Range(-0.08, 0.28)).Normalized();
                    double bladeLen = (juvenile ? rng.Range(0.060, 0.080) : rng.Range(0.075, 0.110));
                    double bladeWid = bladeLen * rng.Range(0.20, 0.24);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 40000 + b * 500 + l * 23 + 9));

                    Broadleaf.BuildLeaf(m, secEnd, lDir, bladeLen, bladeWid,
                        BroadleafOutline.Lanceolate, lSeed, c1, c2,
                        age: rng.Range(0.70, 0.98), camber: 0.07, midribFold: 0.09,
                        wavyMargin: 0.015, wavyFrequency: 3.2, tipCurl: 0.05,
                        petioleLength: 0.012, petioleRadius: 0.0018, petioleKink: 0.12,
                        undersideColor: underColor, detailOverride: Math.Max(1, detail));
                }
            }
        }
    }
}
