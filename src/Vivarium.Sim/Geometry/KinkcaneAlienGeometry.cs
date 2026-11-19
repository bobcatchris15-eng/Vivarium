using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Kinkcane: Architectural Black-Amber Jointed Zigzag Cane-Brake with Contrastingly Erect Palmate Sentinel Canes.
/// - Widely spaced rhizomatous foot pattern linked by creeping surface runners & emerging bamboo shoots.
/// - ~75-80% kink-jointed canes: slender jet-black segmented culms with crisp 28-40 deg alternating elbow joints
///   in world space, double-ridged amber nodal collars, bare lower shafts, and sparse upper leafy fan twigs.
/// - Every 4th cane (~20-25%): arrow-straight taller sentinel culm bare below and crowned by a broad,
///   sculpted radiating alien palmate leaf rosette + amber spore-berry cluster.
/// </summary>
internal static class KinkcaneAlienGeometry
{
    public static void Build(MeshData m, Rng rng, double[] c1, double[] c2, double aspect)
    {
        int detail = m.FloraDetailLevel ?? 0;
        // Work in metric coordinates matching radius=1.0, height=aspect, then scale back to unit space at the end
        // so internode kink angles, circular culm cross-sections, and flat palmate fans never shear vertically.
        double hWorld = Math.Max(0.2, aspect);
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = m.PreserveUpperCanopyOnLod };

        var wood = Primitives.Mix(Primitives.Scale(c1, 0.16), new[] { 0.045, 0.038, 0.036 }, 0.72);
        var tipWood = Primitives.Mix(wood, new[] { 0.14, 0.12, 0.10 }, 0.35);
        var ringCol = new[] { 0.78, 0.50, 0.18 };    // Crisp amber-copper nodal ring
        var sheathCol = new[] { 0.48, 0.35, 0.19 };  // Papery ochre-russet culm sheath
        var leafBase = Primitives.Mix(c1, new[] { 0.05, 0.22, 0.10 }, 0.55); // Saturated jade-emerald
        var leafTip = Primitives.Mix(c2, new[] { 0.22, 0.44, 0.15 }, 0.45);  // Vibrant chartreuse-green
        var strobiliCol = new[] { 0.78, 0.64, 0.20 };

        // 1. Widely spaced surface rhizome runners & emerging bamboo shoots across brake footprint
        int runnerCount = detail == 2 ? 4 : 6;
        double runPhase = rng.Range(0, Math.Tau);
        for (int r = 0; r < runnerCount; r++)
        {
            double a = runPhase + r * Math.Tau / runnerCount + rng.Range(-0.28, 0.28);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var perp = new Vec3(-dir.Z, 0, dir.X);
            var rPath = new[]
            {
                dir * 0.08 + Vec3.Up * 0.015,
                dir * 0.36 + perp * rng.Range(-0.08, 0.08) + Vec3.Up * 0.025,
                dir * rng.Range(0.68, 0.88) + perp * rng.Range(-0.10, 0.10) + Vec3.Up * 0.012
            };
            Primitives.Tube(part, rPath, new[] { 0.022, 0.016, 0.008 }, 5,
                (i, v) => (wood, 1.0, i * 0.2, v, 2, 0));
        }

        int shootCount = detail == 2 ? 4 : 5;
        for (int s = 0; s < shootCount; s++)
        {
            double sAng = runPhase + s * Math.Tau / shootCount + rng.Range(-0.25, 0.25);
            double sDist = rng.Range(0.28, 0.68);
            var sPos = new Vec3(Math.Cos(sAng) * sDist, 0, Math.Sin(sAng) * sDist);
            BuildBambooShoot(part, sPos, rng.Range(0.16, 0.32), rng.Range(0.024, 0.036), wood, sheathCol, ringCol, detail);
        }

        // 2. Culms scattered loosely across the brake footprint (~80% strongly zigzag-kinked, every 4th erect sentinel)
        int count = detail == 2 ? 10 : detail == 1 ? 12 : 14;
        for (int k = 0; k < count; k++)
        {
            bool isSentinel = k % 4 == 0; // Tall arrow-straight sentinel culm crowned with broad palmate rosette
            bool isJuvenile = !isSentinel && k < 3;

            double heightMeters = isSentinel
                ? hWorld * rng.Range(0.92, 1.04)
                : isJuvenile
                    ? hWorld * rng.Range(0.46, 0.62)
                    : hWorld * rng.Range(0.66, 0.88);

            double az = runPhase + k * 2.399963 + rng.Range(-0.28, 0.28);
            double spreadDist = isSentinel ? rng.Range(0.14, 0.48) : rng.Range(0.24, 0.80);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var perp = new Vec3(-outward.Z, 0, outward.X);
            var foot = outward * spreadDist;

            int nodeCount = isSentinel ? 8 : (isJuvenile ? 6 : 7);
            var nodes = new Vec3[nodeCount];
            nodes[0] = foot;

            if (isSentinel)
            {
                // Perfectly straight erect sentinel cane with slight dignified basal stance
                var tipTop = foot + outward * rng.Range(0.04, 0.14) + Vec3.Up * heightMeters;
                for (int j = 1; j < nodeCount; j++)
                {
                    double tj = (double)j / (nodeCount - 1);
                    nodes[j] = Vec3.Lerp(foot, tipTop, tj);
                }
            }
            else
            {
                // Dramatic kink-jointed cane: each straight internode elbows sharply 28-40 degrees at amber joint!
                double stepH = heightMeters / (nodeCount - 1);
                var walkDir = (outward * rng.Range(0.18, 0.34) + Vec3.Up * 0.96).Normalized();
                for (int j = 1; j < nodeCount; j++)
                {
                    double tj = (double)j / (nodeCount - 1);
                    double sign = (j % 2 == 0) ? 1.0 : -1.0;
                    // Alternate primary radial kink + perpendicular twist so zigzag silhouettes read from every angle!
                    var kinkVec = (outward * rng.Range(0.46, 0.68) + perp * (rng.Range(0.25, 0.45) * (j % 3 == 0 ? 1.0 : -1.0))).Normalized();
                    double lateralMag = rng.Range(0.46, 0.68) * (tj > 0.72 ? 1.15 : 1.0);
                    walkDir = (Vec3.Up * rng.Range(0.76, 0.90) + kinkVec * (sign * lateralMag) + outward * (0.20 * tj)).Normalized();
                    double segLen = stepH / Math.Max(0.55, walkDir.Y);
                    nodes[j] = nodes[j - 1] + walkDir * segLen;
                }
            }

            // Tube along crisp segmented culm internodes (distinct angular chords rather than soft noodle curves!)
            var radii = new double[nodeCount];
            double baseR = (isSentinel ? 0.024 : 0.017 * (heightMeters / hWorld)) * 1.15;
            for (int j = 0; j < nodeCount; j++)
            {
                double tj = (double)j / (nodeCount - 1);
                radii[j] = MathD.Lerp(baseR, 0.0060, Math.Pow(tj, 0.85));
            }
            Primitives.Tube(part, nodes, radii, detail == 2 ? 4 : 6,
                (i, v) => (Primitives.Mix(wood, tipWood, (double)i / (nodeCount - 1)), 1.0, (double)i / (nodeCount - 1), v, 2, 0));

            // Swollen double-ridged amber nodal collars + lower sheaths
            for (int j = 1; j < nodeCount - 1; j++)
            {
                var segA = (nodes[j] - nodes[j - 1]).Normalized();
                var segB = (nodes[j + 1] - nodes[j]).Normalized();
                var tangent = (segA + segB).Normalized();
                if (tangent.LengthSq < 1e-6) tangent = Vec3.Up;
                var nPos = nodes[j];
                double rNode = radii[j];

                // Lower amber collar + upper ridge accentuating every elbow kink
                var pRing1 = nPos - tangent * 0.010;
                Primitives.Tube(part, new[] { pRing1 - tangent * 0.006, pRing1, pRing1 + tangent * 0.006 },
                    new[] { rNode * 1.02, rNode * 1.32, rNode * 1.02 }, 6,
                    (st, v) => (ringCol, 1.0, st, v, 1.22, 0.80));

                var pRing2 = nPos + tangent * 0.012;
                Primitives.Tube(part, new[] { pRing2 - tangent * 0.006, pRing2, pRing2 + tangent * 0.006 },
                    new[] { rNode * 1.02, rNode * 1.38, rNode * 1.02 }, 6,
                    (st, v) => (ringCol, 1.0, st, v, 1.22, 0.80));

                if (!isSentinel && j <= 2)
                {
                    double sheathLen = 0.14;
                    var peelDir = (segA.Cross(Vec3.Up).LengthSq > 1e-4 ? outward * 0.72 + Vec3.Up * 0.45 : outward).Normalized();
                    var sheathTip = nPos + peelDir * (rNode * 1.6 + sheathLen * 0.45) + tangent * sheathLen;
                    var sheathSide = tangent.Cross(peelDir).Normalized();
                    Primitives.CurvedLeaf(part, nPos, sheathTip, sheathSide, rNode * 0.95,
                        sheathCol, sheathCol, camber: 0.006, longitudinal: 2);
                }

                // Kinked canes keep lower ~70% bare and burst into crisp horizontal twig fans only near upper nodes!
                double heightFraction = (double)j / (nodeCount - 1);
                if (!isSentinel && !isJuvenile && heightFraction >= 0.62)
                {
                    int branchlets = heightFraction > 0.78 ? (detail == 2 ? 1 : 2) : 1;
                    for (int b = 0; b < branchlets; b++)
                    {
                        double bTheta = az + (b == 0 ? 0.55 : -0.65) + rng.Range(-0.25, 0.25);
                        var bHoriz = new Vec3(Math.Cos(bTheta), 0, Math.Sin(bTheta));
                        var bDir = (bHoriz * 0.92 + Vec3.Up * rng.Range(0.08, 0.24)).Normalized();
                        double bLen = rng.Range(0.18, 0.28);

                        var bMid = nPos + bDir * (bLen * 0.55) + Vec3.Up * (bLen * 0.04);
                        var bTip = nPos + bDir * bLen;
                        Primitives.Tube(part, new[] { nPos, bMid, bTip },
                            new[] { rNode * 0.45, 0.0040, 0.0020 }, 4,
                            (st, v) => (tipWood, 1.0, st, v, 2, 0));

                        int leavesInSpray = detail == 2 ? 2 : 3;
                        for (int lf = 0; lf < leavesInSpray; lf++)
                        {
                            double lfSpread = (lf - (leavesInSpray - 1) * 0.5) * 0.48;
                            var lfHoriz = (bHoriz * Math.Cos(lfSpread) + bHoriz.Cross(Vec3.Up) * Math.Sin(lfSpread)).Normalized();
                            var leafDir = (lfHoriz * 0.94 + Vec3.Up * rng.Range(0.08, 0.22)).Normalized();
                            double leafLen = rng.Range(0.24, 0.34);
                            var bladeRoot = bTip + leafDir * 0.010;
                            var leafSide = Vec3.Up.Cross(leafDir).Normalized();
                            FoliageBlade.Build(part, bladeRoot, bladeRoot + leafDir * leafLen, leafSide, leafLen * 0.14,
                                leafBase, leafTip, camber: 0.09, curl: 0.03, shoulder: 0.80, segments: detail == 2 ? 2 : 3);
                        }
                    }

                    if (heightFraction >= 0.72 && (k + j) % 2 == 0)
                    {
                        var beadOrigin = nPos + outward * (rNode * 1.4);
                        int beadCount = detail == 2 ? 2 : 3;
                        for (int bd = 0; bd < beadCount; bd++)
                        {
                            double bAng = bd * Math.Tau / beadCount;
                            var bOffset = new Vec3(Math.Cos(bAng) * 0.014, (bd - 1) * 0.012, Math.Sin(bAng) * 0.014);
                            double beadR = rng.Range(0.012, 0.017);
                            Primitives.Ellipsoid(part, beadOrigin + bOffset, new Vec3(beadR, beadR * 1.15, beadR),
                                detail == 2 ? 3 : 4, detail == 2 ? 4 : 6,
                                (u, v) => (strobiliCol, 1.0, u, v, 1.30, 0.88));
                        }
                    }
                }
            }

            // Summit foliage: Sentinel canes carry unmistakable broad 360-deg alien PALMATE leaf rosettes!
            // Kinked canes end in crisp compact terminal 3-blade fans.
            var apex = nodes[^1];
            if (isSentinel)
            {
                // Swollen copper rosette hub
                Primitives.Ellipsoid(part, apex, new Vec3(0.026, 0.018, 0.026), 3, 6,
                    (u, v) => (ringCol, 1.0, u, v, 1.22, 0.80));

                int palmateRays = detail == 2 ? 6 : 8;
                double roPhase = rng.Range(0, Math.Tau);
                for (int r = 0; r < palmateRays; r++)
                {
                    double rAz = roPhase + r * Math.Tau / palmateRays + rng.Range(-0.08, 0.08);
                    var rHoriz = new Vec3(Math.Cos(rAz), 0, Math.Sin(rAz));
                    double tilt = r % 2 == 0 ? 0.14 : 0.28;
                    var rayDir = (rHoriz + Vec3.Up * tilt).Normalized();
                    double rayLen = rng.Range(0.36, 0.48); // Broad sculptural alien palmate umbrella!
                    var raySide = Vec3.Up.Cross(rayDir).Normalized();
                    var pRoot = apex + rHoriz * 0.018;
                    Primitives.Tube(part, new[] { apex, pRoot + rayDir * 0.06 }, new[] { 0.007, 0.0035 }, 4,
                        (st, v) => (tipWood, 1.0, st, v, 2, 0));
                    FoliageBlade.Build(part, pRoot, pRoot + rayDir * rayLen, raySide, rayLen * 0.19,
                        leafBase, leafTip, camber: 0.10, curl: 0.05, shoulder: 0.76, segments: detail == 2 ? 2 : 4);
                }
                // Central jewel berry bud atop the palmate sentinel crown
                Primitives.Ellipsoid(part, apex + Vec3.Up * 0.026, new Vec3(0.018, 0.020, 0.018), 3, 5,
                    (u, v) => (strobiliCol, 1.0, u, v, 1.30, 0.88));
            }
            else
            {
                var apexDir = (nodes[^1] - nodes[^2]).Normalized();
                var apexSide = Vec3.Up.Cross(apexDir).Normalized();
                if (apexSide.LengthSq < 1e-4) apexSide = perp;
                int apexLeaves = detail == 2 ? 3 : 4;
                for (int af = 0; af < apexLeaves; af++)
                {
                    double afSpread = (af - (apexLeaves - 1) * 0.5) * 0.45;
                    var termHoriz = (outward * Math.Cos(afSpread) + apexSide * Math.Sin(afSpread)).Normalized();
                    var fDir = (termHoriz * 0.92 + Vec3.Up * rng.Range(0.08, 0.22)).Normalized();
                    double fLen = rng.Range(0.22, 0.30);
                    var fSide = Vec3.Up.Cross(fDir).Normalized();
                    FoliageBlade.Build(part, apex, apex + fDir * fLen, fSide, fLen * 0.14,
                        leafBase, leafTip, camber: 0.09, curl: 0.03, shoulder: 0.80, segments: detail == 2 ? 2 : 3);
                }
            }
        }

        // Convert from (radius=1.0, height=hWorld) back into engine unit frame [XZ/1, Y/hWorld, Z/1]
        double invH = 1.0 / hWorld;
        m.Append(part, p => new Vec3(p.X, p.Y * invH, p.Z));
        for (int v = 0; v < m.VertexCount; v++)
        {
            var n = m.NormalAt(v);
            var scaled = new Vec3(n.X, n.Y * hWorld, n.Z);
            double len = scaled.Length;
            if (len > 1e-9)
            {
                scaled = scaled / len;
                m.Normals[v * 3] = (float)scaled.X;
                m.Normals[v * 3 + 1] = (float)scaled.Y;
                m.Normals[v * 3 + 2] = (float)scaled.Z;
            }
        }
    }

    /// <summary>
    /// Sculpted pointed bamboo shoot with overlapping triangular sheaths:
    /// Banded in obsidian-plum base, warm amber-ochre margins, sharp spear tip.
    /// </summary>
    private static void BuildBambooShoot(MeshData m, Vec3 root, double height, double baseRadius,
        double[] woodCol, double[] sheathCol, double[] ringCol, int detail)
    {
        var plumBase = new[] { 0.12, 0.06, 0.09 };
        var amberOchre = new[] { 0.58, 0.44, 0.22 };

        // Central core shoot cone
        int rings = detail == 2 ? 4 : 5;
        const int cols = 8;
        int start = m.VertexCount;

        for (int r = 0; r <= rings; r++)
        {
            double t = (double)r / rings;
            double ringR = baseRadius * (1.0 - Math.Pow(t, 0.75));
            double y = height * t;

            for (int c = 0; c <= cols; c++)
            {
                double theta = (double)c / cols * Math.Tau;
                var dir = new Vec3(Math.Cos(theta), 0, Math.Sin(theta));
                var pos = root + dir * ringR + Vec3.Up * y;
                var norm = (dir * (1.0 - t) + Vec3.Up * 0.4).Normalized();
                var col = Primitives.Mix(plumBase, amberOchre, t * 0.85);

                m.AddVertex(pos, norm, col, 1.0, t, (double)c / cols, 2, 0);
            }
        }

        int stride = cols + 1;
        for (int r = 0; r < rings; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a = start + r * stride + c;
                int b = a + stride;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        // 3 Overlapping triangular sheaths wrapping tightly around shoot
        for (int sh = 0; sh < 3; sh++)
        {
            double shAng = sh * Math.Tau / 3.0;
            var shDir = new Vec3(Math.Cos(shAng), 0, Math.Sin(shAng));
            var shSide = Vec3.Up.Cross(shDir).Normalized();
            double shH = height * (0.45 + sh * 0.22);
            var shTip = root + shDir * (baseRadius * 0.4) + Vec3.Up * shH;
            Primitives.CurvedLeaf(m, root + shDir * (baseRadius * 0.95), shTip, shSide,
                baseRadius * 0.65, plumBase, amberOchre, camber: 0.005, longitudinal: 2);
        }
    }
}
