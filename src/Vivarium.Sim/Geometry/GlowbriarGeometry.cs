using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Glowbriar: Sculpted sinuous spiral-cane thorny briar shrub.
/// - Gnarled mossy root-crown stool at center with recurved jade-amber thorns along cane internodes.
/// - Sinuous spiral-arching briar canes with organic secondary lateral shoots (no abrupt angular forks).
/// - Opposite decussate blue-emerald cambered leaves spreading naturally (+Up normals).
/// - 16-22 pendant bioluminescent chalices (UV2 = 1.45, 0.92) with glowing cyan nectar pearls (UV2 = 1.75, 0.98)
///   dangling on slender curved pedicels beneath the cane arches.
/// </summary>
internal static class GlowbriarGeometry
{
    public static void Build(MeshData mesh, Rng rng, double[] c1, double[] c2, bool juvenile)
    {
        int detail = mesh.FloraDetailLevel ?? 0;
        int canes = juvenile ? 4 : detail == 2 ? 4 : detail == 1 ? 5 : 6;
        int sideShoots = juvenile ? 1 : (detail == 2 ? 1 : 2); // gracefully arching secondary shoots per cane
        double scale = juvenile ? 0.72 : 1.0;

        var oldWood = Primitives.Mix(c1, new[] { 0.28, 0.26, 0.22 }, 0.53);
        var newWood = Primitives.Mix(c1, new[] { 0.34, 0.30, 0.24 }, 0.31);
        var darkLeaf = Primitives.Mix(c1, new[] { 0.08, 0.26, 0.20 }, 0.45);
        var litLeaf = Primitives.Mix(c2, new[] { 0.28, 0.56, 0.42 }, 0.45);

        // 1. Root Stool & Buttress Flares (UV2 = 2, 0)
        var stoolCol = Primitives.Mix(oldWood, new[] { 0.14, 0.22, 0.16 }, 0.45);
        Primitives.Ellipsoid(mesh, new Vec3(0, 0.020 * scale, 0),
            new Vec3(0.068 * scale, 0.034 * scale, 0.068 * scale),
            detail == 2 ? 4 : 5, detail == 2 ? 6 : 8, (u, v) => (stoolCol, 1.0, u, v, 2, 0));

        int buttressCount = detail == 2 ? 3 : 5;
        for (int b = 0; b < buttressCount; b++)
        {
            double bAng = b * Math.Tau / buttressCount + rng.Range(-0.15, 0.15);
            var bDir = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));
            var bPath = new[]
            {
                new Vec3(0, 0.022 * scale, 0) + bDir * (0.030 * scale),
                bDir * (0.080 * scale) + new Vec3(0, 0.010 * scale, 0),
                bDir * (0.145 * scale) - new Vec3(0, 0.008 * scale, 0)
            };
            Primitives.Tube(mesh, bPath, new[] { 0.018 * scale, 0.011 * scale, 0.004 * scale }, 5,
                (i, v) => (stoolCol, 1.0, i * 0.1, v, 2, 0));
        }

        // Emerging juvenile basal briar shoots
        int basalShoots = juvenile ? 2 : (detail == 2 ? 2 : 3);
        for (int bs = 0; bs < basalShoots; bs++)
        {
            double bsAng = rng.Range(0, Math.Tau);
            var bsDir = new Vec3(Math.Cos(bsAng), 0, Math.Sin(bsAng));
            double bsLen = scale * rng.Range(0.12, 0.20);
            var bsPath = new[]
            {
                bsDir * 0.035,
                bsDir * 0.05 + Vec3.Up * (bsLen * 0.5) + new Vec3(rng.Range(-0.01, 0.01), 0, rng.Range(-0.01, 0.01)),
                bsDir * 0.07 + Vec3.Up * bsLen
            };
            Primitives.Tube(mesh, bsPath, new[] { 0.008 * scale, 0.005 * scale, 0.0025 * scale }, 5,
                (i, v) => (newWood, 1.0, i * 0.2, v, 2, 0));

            // Juvenile leaf pair at shoot tip
            Pair(mesh, rng, bsPath[^1], (bsPath[^1] - bsPath[^2]).Normalized(), bsDir,
                new Vec3(-bsDir.Z, 0, bsDir.X), scale * 0.70, darkLeaf, litLeaf, detail, bs * 1.3, nodeIndex: 0);
        }

        // 2. Primary Sinuous Spiral Arching Canes
        for (int cane = 0; cane < canes; cane++)
        {
            double az = Math.PI * 2 * (cane + 0.31 * rng.Range(-0.5, 0.5)) / canes;
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var sideways = new Vec3(-radial.Z, 0, radial.X);
            double phase = rng.Range(-0.45, 0.45);
            double reach = scale * rng.Range(0.70, 0.88);
            double height = scale * rng.Range(0.85, 0.98);

            const int stations = 14;
            var primary = new Vec3[stations];
            var thickness = new double[stations];

            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                // Smooth sinuous briar arch: rising steeply, cresting at t~0.65, drooping at distal tip
                double rArch = reach * (0.58 * t + 0.42 * t * t);
                double yArch = height * Math.Sin(t * Math.PI * 0.68) * (1.0 - 0.16 * t);
                double spiral = scale * (0.015 + 0.055 * t) * Math.Sin(Math.PI * 1.6 * t + phase);
                double lift = scale * 0.025 * t * Math.Cos(Math.PI * 1.6 * t + phase);

                primary[i] = radial * (0.030 * scale + rArch)
                    + sideways * spiral
                    + Vec3.Up * Math.Max(0.018 * scale, yArch + lift);

                double collars = 0.08 * (Math.Exp(-Math.Pow((t - 0.32) / 0.08, 2)) +
                    Math.Exp(-Math.Pow((t - 0.56) / 0.08, 2)) +
                    Math.Exp(-Math.Pow((t - 0.78) / 0.08, 2)));
                thickness[i] = scale * (0.017 - 0.0125 * Math.Pow(t, 0.85)) * (1 + collars);
            }
            Wood(mesh, primary, thickness, oldWood, newWood, detail == 2 ? 5 : 6);

            // Sculpted recurved briar prickles/thorns along cane internodes
            int thornCount = detail == 2 ? 6 : 9;
            for (int th = 0; th < thornCount; th++)
            {
                double tt = 0.12 + 0.76 * th / thornCount + rng.Range(-0.02, 0.02);
                var tp = Sample(primary, tt);
                var tDir = Direction(primary, tt);
                var tSide = (sideways * (th % 2 == 0 ? 1 : -1) + radial * 0.35).Normalized();
                AddBriarThorn(mesh, tp, tDir, tSide, scale * rng.Range(0.014, 0.019), scale * 0.0038);
            }

            // Opposite decussate leaf pairs along primary cane (lush foliage coverage)
            int leafNodes = juvenile ? 4 : (detail == 2 ? 4 : detail == 1 ? 5 : 6);
            for (int node = 0; node < leafNodes; node++)
            {
                double t = 0.22 + (0.70 / leafNodes) * (node + 0.25);
                var p = Sample(primary, t);
                var ahead = Direction(primary, t);
                Pair(mesh, rng, p, ahead, radial, sideways, scale * (0.95 + 0.08 * rng.Range(-1, 1)),
                    darkLeaf, litLeaf, detail, cane * 0.43 + node * 0.69, nodeIndex: node);
            }

            // Terminal leaves capping the arching cane tip
            var tipPoint = primary[^1];
            var tipDir = Direction(primary, 0.95);
            var tipSide = tipDir.Cross(Vec3.Up).Normalized();
            if (tipSide.LengthSq < 1e-4) tipSide = sideways;
            for (int ti = -1; ti <= 1; ti++)
            {
                var tHoriz = (tipSide * (ti * 0.65) + tipDir.Cross(tipSide).Normalized() * 0.60).Normalized();
                double tElev = rng.Range(45.0, 60.0) * Math.PI / 180.0;
                var tLeafDir = (tHoriz * Math.Sin(tElev) + Vec3.Up * Math.Cos(tElev)).Normalized();
                var tLeafSide = Vec3.Up.Cross(tLeafDir).Normalized();
                double tLen = scale * rng.Range(0.12, 0.16);
                FoliageBlade.Build(mesh, tipPoint, tipPoint + tLeafDir * tLen, tLeafSide, tLen * 0.22,
                    darkLeaf, litLeaf, camber: 0.08, curl: 0.02, shoulder: 0.76, segments: detail == 2 ? 2 : 3);
            }

            // Secondary side shoots: branching at an organic acute angle with flared branch collar
            for (int b = 0; b < sideShoots; b++)
            {
                double t = 0.36 + b * 0.24 + (0.015 * rng.Range(-1, 1));
                var basePoint = Sample(primary, t);
                var parentDir = Direction(primary, t);
                double handed = (b % 2 == 0 ? 1 : -1);
                double span = scale * rng.Range(0.24, 0.34) * (1.0 - b * 0.12);

                // Flared nodal branch collar at junction
                Primitives.Ellipsoid(mesh, basePoint,
                    new Vec3(scale * 0.013, scale * 0.010, scale * 0.013),
                    4, 6, (u, v) => (oldWood, 1.0, u, v, 2, 0));

                // Organic acute branching angle (35°-45° from cane tangent)
                var bDir = (parentDir * 0.60 + sideways * (handed * 0.52) + radial * 0.36 + Vec3.Up * 0.18).Normalized();
                const int bStations = 7;
                var branch = new Vec3[bStations];
                var branchRadii = new double[bStations];

                for (int k = 0; k < bStations; k++)
                {
                    double u = k / (double)(bStations - 1);
                    branch[k] = basePoint + bDir * (span * u)
                        + sideways * (handed * span * 0.08 * Math.Sin(Math.PI * u))
                        + Vec3.Up * (span * 0.08 * Math.Sin(Math.PI * u))
                        - Vec3.Up * (span * 0.10 * u * u);
                    branchRadii[k] = scale * MathD.Lerp(0.0075, 0.0020, u);
                }
                Wood(mesh, branch, branchRadii, newWood, oldWood, detail == 2 ? 4 : 5);

                // Leaf pairs on secondary shoots
                int bNodes = detail == 2 ? 1 : 2;
                for (int n = 0; n < bNodes; n++)
                {
                    double u = 0.35 + (0.50 * n / Math.Max(1, bNodes - 1));
                    var p = Sample(branch, u);
                    Pair(mesh, rng, p, Direction(branch, u), radial, sideways,
                        scale * (0.86 + 0.10 * rng.Range(-1, 1)), darkLeaf, litLeaf,
                        detail, b * 0.91 + n * 0.48 + cane * 0.17, nodeIndex: n + 1);
                }

                // Pendant chalice beneath secondary branch arch (1 per cane on primary lateral shoot)
                if (!juvenile && b == 0)
                {
                    var bMid = Sample(branch, 0.70);
                    AddChalicedDewdrop(mesh, rng, bMid - Vec3.Up * 0.008, bDir, scale * rng.Range(0.88, 0.98), detail);
                }
            }

            // 3. Pendant Bioluminescent Briar-Lantern Chalices
            // Chalice 1: dangling beneath the primary cane arch
            var archPoint = Sample(primary, rng.Range(0.58, 0.68));
            AddChalicedDewdrop(mesh, rng, archPoint - Vec3.Up * 0.012, radial, scale * rng.Range(0.94, 1.06), detail);

            // Chalice 2: nodding terminal chalice at cane tip
            AddChalicedDewdrop(mesh, rng, tipPoint, tipDir, scale * rng.Range(0.90, 1.02), detail);
        }
    }

    /// <summary>
    /// Sculpted recurved briar prickle (rose thorn):
    /// Flattened oval base curving backwards along -tangent to sharp amber-translucent tip.
    /// </summary>
    private static void AddBriarThorn(MeshData mesh, Vec3 canePos, Vec3 tangent, Vec3 outward, double length, double width)
    {
        var thornCurve = (-tangent * 0.60 + outward * 0.80).Normalized();
        var tip = canePos + thornCurve * length;
        var side = tangent.Cross(outward).Normalized();

        var amberBase = new[] { 0.28, 0.36, 0.22 };
        var amberTip = new[] { 0.74, 0.58, 0.22 };

        int start = mesh.VertexCount;
        mesh.AddVertex(tip, thornCurve, amberTip, 1.0, 1.0, 0.5, 1.35, 0.90);

        const int sides = 4;
        for (int i = 0; i < sides; i++)
        {
            double ang = i * Math.Tau / sides;
            var offset = (tangent * Math.Cos(ang) * (width * 1.5) + side * Math.Sin(ang) * width);
            var p = canePos + offset;
            var n = (offset + outward * width).Normalized();
            mesh.AddVertex(p, n, amberBase, 1.0, 0.0, (double)i / sides, 1.35, 0.90);
        }

        // Triangles forming 4-sided recurved cone
        for (int i = 0; i < sides; i++)
        {
            int a = start + 1 + i;
            int b = start + 1 + (i + 1) % sides;
            mesh.AddTriangle(start, a, b);
        }
    }

    /// <summary>
    /// Pendant Bioluminescent Briar-Lantern Chalice:
    /// - Slender curved gooseneck pedicel dangling beneath cane arch (UV2 = 2, 0).
    /// - 4-pointed starry jade leafy calyx cap (UV2 = 0.6, 0.5).
    /// - Fluted translucent seafoam-aquamarine bell chalice body (UV2 = 1.45, 0.92).
    /// - Glowing cyan-mint nectar pearl (UV2 = 1.75, 0.98) suspended inside/at opening.
    /// </summary>
    private static void AddChalicedDewdrop(MeshData mesh, Rng rng, Vec3 origin, Vec3 hangDir, double scale, int detail)
    {
        double chaliceLen = scale * rng.Range(0.072, 0.098);
        double chaliceRadius = scale * rng.Range(0.028, 0.040);
        var pedicelWood = new[] { 0.32, 0.28, 0.22 };

        // 1. Gracefully curved gooseneck pedicel curving outward and hanging down
        var hangOut = (hangDir * 0.70 + Vec3.Up * 0.12).Normalized();
        var p0 = origin;
        var p1 = origin + hangOut * (chaliceLen * 0.30) - Vec3.Up * (chaliceLen * 0.14);
        var p2 = origin + hangOut * (chaliceLen * 0.38) - Vec3.Up * (chaliceLen * 0.42);
        var p3 = origin + hangOut * (chaliceLen * 0.20) - Vec3.Up * (chaliceLen * 0.75);
        var apex = origin + hangOut * (chaliceLen * 0.06) - Vec3.Up * (chaliceLen * 0.92);

        Primitives.Tube(mesh, new[] { p0, p1, p2, p3, apex },
            new[] { scale * 0.0022, scale * 0.0018, scale * 0.0015, scale * 0.0013, scale * 0.0011 }, 4,
            (i, v) => (pedicelWood, 1.0, i / 4.0, v, 2, 0));

        // 2. 4-pointed starry jade leafy calyx cap (UV2 = 0.6, 0.5)
        var calyxJade = new[] { 0.14, 0.44, 0.36 };
        for (int s = 0; s < 4; s++)
        {
            double sAng = s * Math.PI * 0.5 + 0.35;
            var sDir = new Vec3(Math.Cos(sAng), -0.28, Math.Sin(sAng)).Normalized();
            var sepalTip = apex + sDir * (scale * 0.016);
            var sepalSide = Vec3.Up.Cross(sDir);
            Primitives.CurvedLeaf(mesh, apex, sepalTip, sepalSide, scale * 0.0045,
                calyxJade, calyxJade, camber: 0.003, longitudinal: 2);
        }

        // 3. Fluted translucent seafoam-aquamarine bell chalice body (UV2 = 1.45, 0.92)
        int rings = detail == 2 ? 4 : 6;
        const int cols = 12; // 4 lobes * 3 vertices
        int startVertex = mesh.VertexCount;

        var seafoam = new[] { 0.32, 0.88, 0.76 };
        var mintTeal = new[] { 0.18, 0.70, 0.58 };

        for (int r = 0; r <= rings; r++)
        {
            double t = (double)r / rings;
            double yOffset = -chaliceLen * t;

            // Bell chalice profile: narrow apex -> swelling body -> flared scalloped rim
            double rEnv;
            if (t < 0.40)
                rEnv = 0.22 + 0.78 * Math.Sin(Math.PI * t / 0.80);
            else if (t < 0.85)
                rEnv = 0.90 + 0.10 * Math.Cos(Math.PI * (t - 0.40) / 0.45);
            else
                rEnv = 0.94 + 0.22 * Math.Sin(Math.PI * (t - 0.85) / 0.15); // flared scalloped rim

            double ringR = chaliceRadius * rEnv;

            for (int c = 0; c <= cols; c++)
            {
                double theta = (double)c / cols * Math.Tau;
                double lobe = Math.Cos(4 * theta); // 4-lobed fluted bell
                double effR = ringR * (1.0 + 0.22 * lobe);

                var outward = new Vec3(Math.Cos(theta), 0, Math.Sin(theta));
                var pos = apex + outward * effR + new Vec3(0, yOffset, 0);
                var norm = (outward * 0.80 - Vec3.Up * (t - 0.40)).Normalized();

                double lobeFactor = (lobe + 1.0) * 0.5;
                var col = Primitives.Mix(mintTeal, seafoam, lobeFactor);

                mesh.AddVertex(pos, norm, col, 1.0, t, (double)c / cols, 1.45, 0.92);
            }
        }

        // Triangulate chalice body
        int stride = cols + 1;
        for (int r = 0; r < rings; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a = startVertex + r * stride + c;
                int b = a + stride;
                mesh.AddTriangle(a, a + 1, b);
                mesh.AddTriangle(a + 1, b + 1, b);
            }
        }

        // 4. Glowing cyan-mint nectar droplet / pearl (UV2 = 1.75, 0.98)
        var pearlPos = apex - Vec3.Up * (chaliceLen * 0.70);
        double pearlR = chaliceRadius * 0.40;
        var pearlCol = new[] { 0.45, 1.0, 0.88 };
        Primitives.Ellipsoid(mesh, pearlPos, new Vec3(pearlR, pearlR * 1.15, pearlR),
            detail == 2 ? 4 : 5, detail == 2 ? 6 : 8,
            (u, v) => (pearlCol, 1.0, u, v, 1.75, 0.98));
    }

    private static void Pair(MeshData mesh, Rng rng, Vec3 node, Vec3 stem,
        Vec3 radial, Vec3 sideways, double scale, double[] baseColor,
        double[] endColor, int detail, double phase, int nodeIndex)
    {
        // Natural decussation: alternate nodes rotate ~90° around stem
        double decussAngle = nodeIndex * (Math.PI * 0.5) + 0.20 * Math.Sin(phase * 2.5);
        var stemNorm = stem.Normalized();
        var refSide = sideways - stemNorm * sideways.Dot(stemNorm);
        if (refSide.LengthSq < 1e-6) refSide = stemNorm.Cross(Vec3.Up);
        refSide = refSide.Normalized();
        var refUp = stemNorm.Cross(refSide).Normalized();

        var nodeSide = (refSide * Math.Cos(decussAngle) + refUp * Math.Sin(decussAngle)).Normalized();

        for (int sign = -1; sign <= 1; sign += 2)
        {
            // Outward horizontal spread away from cane
            var horizDir = (nodeSide * sign * 0.82 + radial * 0.42).Normalized();

            // Elevation angle: 45° to 62° from vertical (28° to 45° above horizontal, lush layered sun-catching sprays)
            double elevAngle = rng.Range(46.0, 60.0) * Math.PI / 180.0;
            var dir = (horizDir * Math.Sin(elevAngle) + Vec3.Up * Math.Cos(elevAngle) + stemNorm * 0.08).Normalized();

            double size = scale * rng.Range(0.88, 1.15);
            double petioleLength = 0.012 * size;
            var join = node + (horizDir * 0.85 + Vec3.Up * 0.30).Normalized() * petioleLength;

            Primitives.Tube(mesh, new[] { node, join },
                new[] { 0.0020 * scale, 0.0013 * scale }, 4,
                (i, v) => (baseColor, 1.0, i * 0.012, v, 2, 0));

            // Stipule leaflets flaring at petiole base
            var stipuleTip = node + (nodeSide * sign * 0.65 + Vec3.Up * 0.35).Normalized() * (0.018 * scale);
            var stipuleSide = Vec3.Up.Cross(stipuleTip - node).Normalized();
            Primitives.CurvedLeaf(mesh, node, stipuleTip, stipuleSide, 0.0040 * scale,
                baseColor, endColor, camber: 0.002, longitudinal: 2);

            // Decussate cambered blue-emerald blade with guaranteed sky-facing normals
            double length = rng.Range(0.12, 0.17) * size;
            var tip = join + dir * length;

            var leafSide = Vec3.Up.Cross(dir).Normalized();
            if (leafSide.Dot(nodeSide * sign) < 0) leafSide = -leafSide;

            double width = length * rng.Range(0.20, 0.24);
            var baseTint = Primitives.Mix(baseColor, endColor, rng.Range(0.02, 0.18));
            var tipTint = Primitives.Mix(baseColor, endColor, rng.Range(0.40, 0.68));

            FoliageBlade.Build(mesh, join, tip, leafSide, width,
                baseTint, tipTint, camber: 0.08, curl: rng.Range(0.02, 0.04),
                roll: 0, asymmetry: 0,
                shoulder: rng.Range(0.72, 0.80), segments: detail == 2 ? 2 : 3);
        }
    }

    private static Vec3 Sample(IReadOnlyList<Vec3> p, double t)
    {
        double q = Math.Clamp(t, 0, 1) * (p.Count - 1);
        int index = Math.Min(p.Count - 2, (int)q);
        return Vec3.Lerp(p[index], p[index + 1], q - index);
    }

    private static Vec3 Direction(IReadOnlyList<Vec3> p, double t)
    {
        return (Sample(p, Math.Min(1, t + 0.035)) - Sample(p, Math.Max(0, t - 0.035))).Normalized();
    }

    private static void Wood(MeshData mesh, IReadOnlyList<Vec3> path,
        IReadOnlyList<double> radii, double[] aged, double[] young, int sides)
    {
        sides = Math.Max(4, sides);
        var tangent = new Vec3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            tangent[i] = (path[Math.Min(i + 1, path.Count - 1)] -
                path[Math.Max(0, i - 1)]).Normalized();
        }
        var side = tangent[0].Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = tangent[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        int start = mesh.VertexCount;
        double distance = 0;

        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) distance += (path[i] - path[i - 1]).Length;
            if (i > 0)
            {
                side = (side - tangent[i] * side.Dot(tangent[i])).Normalized();
                if (side.LengthSq < 1e-8) side = tangent[i].Cross(Vec3.Up).Normalized();
            }
            var dorsal = side.Cross(tangent[i]).Normalized();
            double progress = i / (double)(path.Count - 1);
            for (int j = 0; j <= sides; j++)
            {
                double theta = Math.PI * 2 * j / sides;
                double grain = 1 + 0.04 * Math.Cos(5 * theta + distance * 5.0);
                var outward = (side * Math.Cos(theta) + dorsal * Math.Sin(theta)).Normalized();
                var color = Primitives.Mix(aged, young, progress * 0.72);
                var position = path[i] + outward * radii[i] * grain;
                mesh.AddVertex(position, outward, color, 1.0, distance, j / (double)sides, 2, 0);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = start + i * (sides + 1) + j;
                int b = a + sides + 1;
                mesh.AddTriangle(a, a + 1, b);
                mesh.AddTriangle(a + 1, b + 1, b);
            }
        }
    }
}
