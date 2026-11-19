using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Glowbriar: Low-mounding multi-order forked thorny wetland briar shrub (~1.5 m broad × ~1.1 m tall).
/// Authored in world metres and converted back into unit space via OrganismMeshes.NewShrubsFinish so branching
/// arches, thorn hooks, leaves, and hanging chalices stay un-squashed and true to proportion.
/// - Gnarled mossy root crown with sprawling stolon runners and curved basal briar canes.
/// - Primary wide-spreading arches forking cleanly into curved Order 2 & Order 3 weeping sub-twigs.
/// - Opposite decussate blue-emerald cambered ovate-lanceolate leaves forming a rich mounding dome.
/// - Dainty clustered pendant bioluminescent chalices (UV2 = 1.45, 0.92 / 1.75, 0.98) suspended below arches.
/// </summary>
internal static class GlowbriarGeometry
{
    public static void Build(MeshData mesh, Rng rng, double[] c1, double[] c2, bool juvenile,
        double radius = 0.72, double height = 1.12)
    {
        int detail = mesh.FloraDetailLevel ?? 0;
        var part = new MeshData
        {
            Lod = mesh.Lod,
            FloraDetailLevel = mesh.FloraDetailLevel,
            FloraVisualSeed = mesh.FloraVisualSeed,
            PreserveUpperCanopyOnLod = mesh.PreserveUpperCanopyOnLod
        };

        int canes = juvenile ? 4 : detail == 2 ? 4 : detail == 1 ? 5 : 6;
        int sideShoots = juvenile ? 2 : detail == 2 ? 2 : 3;
        int subTwigs = juvenile ? 1 : detail == 2 ? 1 : 2;
        double scale = juvenile ? 0.68 : 1.0;

        var oldWood = Primitives.Mix(c1, new[] { 0.28, 0.26, 0.22 }, 0.53);
        var newWood = Primitives.Mix(c1, new[] { 0.34, 0.30, 0.24 }, 0.31);
        var darkLeaf = Primitives.Mix(c1, new[] { 0.08, 0.26, 0.20 }, 0.45);
        var litLeaf = Primitives.Mix(c2, new[] { 0.28, 0.56, 0.42 }, 0.45);

        // 1. Gnarled Root Stool, Creeping Runners & Low Spreading Basal Briar Arches (UV2 = 2, 0)
        var stoolCol = Primitives.Mix(oldWood, new[] { 0.14, 0.22, 0.16 }, 0.45);
        Primitives.Ellipsoid(part, new Vec3(0, 0.025 * scale, 0),
            new Vec3(0.082 * scale, 0.036 * scale, 0.082 * scale),
            detail == 2 ? 4 : 5, detail == 2 ? 6 : 8, (u, v) => (stoolCol, 1.0, u, v, 2, 0));

        int buttressCount = detail == 2 ? 3 : 5;
        for (int b = 0; b < buttressCount; b++)
        {
            double bAng = b * Math.Tau / buttressCount + rng.Range(-0.22, 0.22);
            var bDir = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));
            var bSide = new Vec3(-bDir.Z, 0, bDir.X);
            var bPath = new[]
            {
                new Vec3(0, 0.026 * scale, 0) + bDir * (0.042 * scale),
                bDir * (0.125 * scale) + bSide * rng.Range(-0.025, 0.025) + new Vec3(0, 0.015 * scale, 0),
                bDir * (0.240 * scale) + bSide * rng.Range(-0.040, 0.040) - new Vec3(0, 0.008 * scale, 0)
            };
            Primitives.Tube(part, bPath, new[] { 0.020 * scale, 0.012 * scale, 0.0045 * scale }, 5,
                (i, v) => (stoolCol, 1.0, i * 0.1, v, 2, 0));
        }

        // Low-arching juvenile basal canes fanning sideways out of offset stolon hubs (not upright center pins)
        int basalShoots = juvenile ? 2 : (detail == 2 ? 2 : 3);
        double baseAzOffset = rng.Range(0, Math.Tau);
        for (int bs = 0; bs < basalShoots; bs++)
        {
            double bsAng = baseAzOffset + bs * Math.Tau / basalShoots + rng.Range(-0.30, 0.30);
            var bsDir = new Vec3(Math.Cos(bsAng), 0, Math.Sin(bsAng));
            var bsSide = new Vec3(-bsDir.Z, 0, bsDir.X);
            double bsReach = scale * rng.Range(0.24, 0.36);
            double bsHeight = scale * rng.Range(0.18, 0.29);
            var bsOrigin = bsDir * (0.065 * scale);
            var bsPath = new[]
            {
                bsOrigin + Vec3.Up * (0.015 * scale),
                bsOrigin + bsDir * (bsReach * 0.35) + bsSide * rng.Range(-0.03, 0.03) + Vec3.Up * (bsHeight * 0.72),
                bsOrigin + bsDir * (bsReach * 0.74) + bsSide * rng.Range(-0.04, 0.04) + Vec3.Up * bsHeight,
                bsOrigin + bsDir * bsReach + Vec3.Up * (bsHeight * 0.76)
            };
            Wood(part, bsPath, new[] { 0.0085 * scale, 0.0062 * scale, 0.0040 * scale, 0.0022 * scale },
                oldWood, newWood, detail == 2 ? 4 : 5);

            for (int bn = 0; bn < 2; bn++)
            {
                double bt = 0.45 + bn * 0.38;
                Pair(part, rng, Sample(bsPath, bt), Direction(bsPath, bt), bsDir, bsSide,
                    scale * 0.72, darkLeaf, litLeaf, detail, bs * 1.3 + bn * 0.7, nodeIndex: bn);
            }
        }

        // 2. Primary Offset Wide-Arching Briar Mound Canes (Order 1 -> Order 2 -> Order 3)
        double canePhase = rng.Range(0, Math.Tau);
        for (int cane = 0; cane < canes; cane++)
        {
            bool crownCane = cane % 3 == 0;
            double az = canePhase + Math.Tau * cane / canes + rng.Range(-0.22, 0.22);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var sideways = new Vec3(-radial.Z, 0, radial.X);
            double phase = rng.Range(-0.55, 0.55);

            // Outer briar canes arch strongly sideways from near ground (~0.56..0.82 m high, 0.65..0.90 m wide);
            // Crown canes billow higher (~0.88..1.08 m high) to bridge the dome cleanly.
            double offsetDist = scale * rng.Range(0.045, 0.110);
            double reach = scale * (crownCane ? rng.Range(0.48, 0.68) : rng.Range(0.64, 0.88));
            double archHeight = scale * (crownCane ? rng.Range(0.88, 1.08) : rng.Range(0.56, 0.82));

            const int stations = 13;
            var primary = new Vec3[stations];
            var thickness = new double[stations];
            var origin = radial * offsetDist + Vec3.Up * (0.018 * scale);

            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                // Smooth crooked bramble arch: broad early flare (0.72*t + 0.28*t^2), cresting around t~0.58 and weeping toward tip
                double rArch = reach * (0.72 * t + 0.28 * t * t);
                double crestProfile = crownCane
                    ? Math.Sin(t * Math.PI * 0.64) * (1.0 - 0.14 * t)
                    : Math.Sin(t * Math.PI * 0.68) * (1.0 - 0.14 * t);
                double yArch = archHeight * crestProfile;
                double wander = scale * (0.025 + 0.085 * t) * Math.Sin(Math.PI * 1.85 * t + phase);
                double verticalWave = scale * 0.042 * t * Math.Cos(Math.PI * 2.1 * t + phase);

                primary[i] = origin + radial * rArch + sideways * wander
                    + Vec3.Up * Math.Max((0.018 + 0.122 * t * t) * scale, yArch + verticalWave);

                double collars = 0.09 * (Math.Exp(-Math.Pow((t - 0.28) / 0.07, 2)) +
                    Math.Exp(-Math.Pow((t - 0.52) / 0.07, 2)) +
                    Math.Exp(-Math.Pow((t - 0.76) / 0.07, 2)));
                thickness[i] = scale * (0.016 - 0.0125 * Math.Pow(t, 0.82)) * (1 + collars);
            }
            Wood(part, primary, thickness, oldWood, newWood, detail == 2 ? 5 : 6);

            // Sculpted recurved briar prickles/thorns along cane internodes
            int thornCount = detail == 2 ? 5 : 8;
            for (int th = 0; th < thornCount; th++)
            {
                double tt = 0.14 + 0.74 * th / thornCount + rng.Range(-0.02, 0.02);
                var tp = Sample(primary, tt);
                var tDir = Direction(primary, tt);
                var tSide = (sideways * (th % 2 == 0 ? 1 : -1) + radial * 0.35).Normalized();
                AddBriarThorn(part, tp, tDir, tSide, scale * rng.Range(0.011, 0.016), scale * 0.0032);
            }

            // Opposite decussate leaf pairs along mid-to-distal primary cane
            int leafNodes = juvenile ? 4 : (detail == 2 ? 4 : detail == 1 ? 5 : 6);
            for (int node = 0; node < leafNodes; node++)
            {
                double t = 0.24 + (0.68 / leafNodes) * (node + 0.25);
                var p = Sample(primary, t);
                var ahead = Direction(primary, t);
                Pair(part, rng, p, ahead, radial, sideways, scale * (0.95 + 0.10 * rng.Range(-1, 1)),
                    darkLeaf, litLeaf, detail, cane * 0.43 + node * 0.69, nodeIndex: node);
            }

            // Terminal leaf cluster capping the weeping cane tip
            var tipPoint = primary[^1];
            var tipDir = Direction(primary, 0.94);
            var tipSide = tipDir.Cross(Vec3.Up).Normalized();
            if (tipSide.LengthSq < 1e-4) tipSide = sideways;
            int termSegs = detail == 2 ? 2 : 4;
            for (int ti = -1; ti <= 1; ti++)
            {
                var tHoriz = (tipSide * (ti * 0.72) + tipDir.Cross(tipSide).Normalized() * 0.50).Normalized();
                double tElev = rng.Range(56.0, 74.0) * Math.PI / 180.0;
                var tLeafDir = (tHoriz * Math.Sin(tElev) + Vec3.Up * Math.Cos(tElev)).Normalized();
                var tLeafSide = Vec3.Up.Cross(tLeafDir).Normalized();
                double tLen = scale * rng.Range(0.072, 0.102);
                Primitives.CurvedLeaf(part, tipPoint, tipPoint + tLeafDir * tLen, tLeafSide, tLen * 0.17,
                    darkLeaf, litLeaf, longitudinal: termSegs, camber: tLen * 0.08);
            }

            // Order 2 & Order 3 branching shoots: sweeping laterally out of primary arch with flared collars
            for (int b = 0; b < sideShoots; b++)
            {
                double t = 0.22 + b * (0.50 / Math.Max(1, sideShoots - 1)) + rng.Range(-0.025, 0.025);
                var basePoint = Sample(primary, t);
                var parentDir = Direction(primary, t);
                double handed = ((b + cane) % 2 == 0 ? 1 : -1);
                double span = scale * rng.Range(0.25, 0.42) * (1.0 - b * 0.10);

                Primitives.Ellipsoid(part, basePoint,
                    new Vec3(scale * 0.012, scale * 0.010, scale * 0.012),
                    4, 6, (u, v) => (oldWood, 1.0, u, v, 2, 0));

                // Wide organic branching angle (38°-62° from parent stem, curving horizontally out & drooping gently)
                var bDir = (parentDir * 0.48 + sideways * (handed * 0.70) + radial * 0.42 + Vec3.Up * 0.16).Normalized();
                const int bStations = 8;
                var branch = new Vec3[bStations];
                var branchRadii = new double[bStations];
                var bOrigin = basePoint - bDir * (0.008 * scale);

                for (int k = 0; k < bStations; k++)
                {
                    double u = k / (double)(bStations - 1);
                    branch[k] = bOrigin + bDir * (span * u)
                        + sideways * (handed * span * 0.12 * Math.Sin(Math.PI * 1.15 * u))
                        + Vec3.Up * (span * (0.14 * Math.Sin(Math.PI * u) - 0.18 * u * u));
                    branchRadii[k] = scale * MathD.Lerp(0.0078, 0.0022, Math.Pow(u, 0.85));
                }
                Wood(part, branch, branchRadii, oldWood, newWood, detail == 2 ? 4 : 5);

                // Opposite leaves along Order 2 shoot
                int bNodes = detail == 2 ? 2 : 3;
                for (int n = 0; n < bNodes; n++)
                {
                    double u = 0.26 + 0.60 * n / Math.Max(1, bNodes - 1);
                    var p = Sample(branch, u);
                    Pair(part, rng, p, Direction(branch, u), radial, sideways,
                        scale * (0.85 + 0.10 * rng.Range(-1, 1)), darkLeaf, litLeaf,
                        detail, b * 0.91 + n * 0.48 + cane * 0.17, nodeIndex: n + 1);
                }

                // Order 3 fine weeping flowering/leafy sub-twigs branching sideways off Order 2 arch
                for (int st = 0; st < subTwigs; st++)
                {
                    double u3 = 0.38 + st * 0.36;
                    var p3 = Sample(branch, u3);
                    var d3 = Direction(branch, u3);
                    double sign3 = ((st + b) % 2 == 0) ? 1.0 : -1.0;
                    var subSide = d3.Cross(Vec3.Up);
                    if (subSide.LengthSq < 1e-6) subSide = sideways;
                    subSide = subSide.Normalized();

                    var subDir = (d3 * 0.50 + subSide * (sign3 * 0.72) + Vec3.Up * 0.08).Normalized();
                    double subSpan = scale * rng.Range(0.12, 0.20);
                    var subPath = new Vec3[5];
                    var subRadii = new double[5];
                    for (int si = 0; si < 5; si++)
                    {
                        double su = si / 4.0;
                        subPath[si] = p3 + subDir * (subSpan * su) + Vec3.Up * (subSpan * (0.10 * Math.Sin(Math.PI * su) - 0.16 * su * su));
                        subRadii[si] = scale * MathD.Lerp(0.0036, 0.0012, su);
                    }
                    Wood(part, subPath, subRadii, newWood, oldWood, detail == 2 ? 4 : 4);

                    // Terminal leaf pair + dangling dainty chalice cluster under Order 3 twig
                    var subTip = subPath[^1];
                    var subTipDir = Direction(subPath, 0.9);
                    Pair(part, rng, Sample(subPath, 0.62), subTipDir, subDir, subSide,
                        scale * 0.74, darkLeaf, litLeaf, detail, st * 0.8 + b * 0.4, nodeIndex: st);

                    if (!juvenile && st == subTwigs - 1)
                    {
                        AddChalicedDewdrop(part, rng, subTip - Vec3.Up * (0.004 * scale), subDir,
                            scale * rng.Range(0.84, 1.02), detail);
                    }
                }

                // Nodding chalice beneath Order 2 midpoint arch
                if (!juvenile && b < 2)
                {
                    var bMid = Sample(branch, rng.Range(0.58, 0.74));
                    AddChalicedDewdrop(part, rng, bMid - Vec3.Up * (0.006 * scale), bDir,
                        scale * rng.Range(0.88, 1.04), detail);
                }
            }

            // 3. Pendant Bioluminescent Briar-Lantern Chalices nestled beneath outer primary arches
            var archPoint = Sample(primary, rng.Range(0.56, 0.70));
            AddChalicedDewdrop(part, rng, archPoint - Vec3.Up * (0.010 * scale), radial,
                scale * rng.Range(0.92, 1.08), detail);

            if (!juvenile || cane % 2 == 0)
            {
                AddChalicedDewdrop(part, rng, tipPoint, tipDir, scale * rng.Range(0.82, 0.98), detail);
            }
        }

        OrganismMeshes.NewShrubsFinish(mesh, part, radius, height);
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
        int chaliceStart = mesh.VertexCount;
        double chaliceLen = scale * rng.Range(0.044, 0.062);
        double chaliceRadius = scale * rng.Range(0.016, 0.024);
        var pedicelWood = new[] { 0.32, 0.28, 0.22 };

        // 1. Gracefully curved gooseneck pedicel curving outward and hanging down
        var hangOut = (hangDir * 0.72 + Vec3.Up * 0.14).Normalized();
        var p0 = origin;
        var p1 = origin + hangOut * (chaliceLen * 0.34) - Vec3.Up * (chaliceLen * 0.14);
        var p2 = origin + hangOut * (chaliceLen * 0.42) - Vec3.Up * (chaliceLen * 0.44);
        var p3 = origin + hangOut * (chaliceLen * 0.22) - Vec3.Up * (chaliceLen * 0.78);
        var apex = origin + hangOut * (chaliceLen * 0.08) - Vec3.Up * (chaliceLen * 0.96);

        Primitives.Tube(mesh, new[] { p0, p1, p2, p3, apex },
            new[] { scale * 0.0020, scale * 0.0016, scale * 0.0014, scale * 0.0012, scale * 0.0010 }, 4,
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

        mesh.EssentialSpans.Add((chaliceStart, mesh.VertexCount - chaliceStart));
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
            var horizDir = (nodeSide * sign * 0.84 + radial * 0.42).Normalized();

            // Elevation angle: 56° to 74° from vertical (16° to 34° above horizontal for wide lush mound dome)
            double elevAngle = rng.Range(56.0, 74.0) * Math.PI / 180.0;
            var dir = (horizDir * Math.Sin(elevAngle) + Vec3.Up * Math.Cos(elevAngle) + stemNorm * 0.06).Normalized();

            double size = scale * rng.Range(0.88, 1.15);
            double petioleLength = 0.012 * size;
            var join = node + (horizDir * 0.85 + Vec3.Up * 0.24).Normalized() * petioleLength;

            Primitives.Tube(mesh, new[] { node, join },
                new[] { 0.0020 * scale, 0.0012 * scale }, 4,
                (i, v) => (baseColor, 1.0, i * 0.012, v, 2, 0));

            // Stipule leaflets flaring at petiole base
            var stipuleTip = node + (nodeSide * sign * 0.65 + Vec3.Up * 0.30).Normalized() * (0.016 * scale);
            var stipuleSide = Vec3.Up.Cross(stipuleTip - node).Normalized();
            Primitives.CurvedLeaf(mesh, node, stipuleTip, stipuleSide, 0.0038 * scale,
                baseColor, endColor, camber: 0.002, longitudinal: 2);

            // Decussate cambered blue-emerald ovate-lanceolate blade with sky-facing normals
            double length = rng.Range(0.072, 0.108) * size;
            var tip = join + dir * length;

            var leafSide = Vec3.Up.Cross(dir).Normalized();

            double halfWidth = length * rng.Range(0.16, 0.20);
            var baseTint = Primitives.Mix(baseColor, endColor, rng.Range(0.02, 0.18));
            var tipTint = Primitives.Mix(baseColor, endColor, rng.Range(0.40, 0.68));

            Primitives.CurvedLeaf(mesh, join, tip, leafSide, halfWidth,
                baseTint, tipTint, longitudinal: detail == 2 ? 2 : 4, camber: length * 0.08);
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
