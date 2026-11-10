using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Kinkcane: Lush, architectural Black-Amber Jointed Bamboo / Horsetail Cane-Brake
/// (Phyllostachys nigra meets whimsical alien joint-cane).
/// - Basal rhizome stool with 4-6 pointed bamboo shoots with overlapping sheaths.
/// - 16-22 culms of varied heights: juvenile, medium erect, and tall outer canes that arch naturally outward.
/// - Prominent double-ridged amber nodal rings (UV2 = 1.22, 0.80) and papery sheaths at lower nodes.
/// - Sub-branches spreading horizontally at nodes in the upper 65%, bearing feathery palmate-lanceolate leaf sprays
///   spreading in layered horizontal tiers (+Up normals).
/// - Clusters of amber-jade beaded spore-berries (UV2 = 1.30, 0.88).
/// </summary>
internal static class KinkcaneAlienGeometry
{
    public static void Build(MeshData m, Rng rng, double[] c1, double[] c2, double aspect)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var wood = Primitives.Mix(Primitives.Scale(c1, 0.22), new[] { 0.065, 0.055, 0.050 }, 0.65);
        var tipWood = Primitives.Mix(wood, new[] { 0.16, 0.14, 0.12 }, 0.35);
        var ringCol = new[] { 0.72, 0.46, 0.18 }; // Amber-copper nodal ring
        var sheathCol = new[] { 0.54, 0.40, 0.22 }; // Papery ochre-russet culm sheath
        var leafBase = Primitives.Mix(c1, new[] { 0.08, 0.24, 0.14 }, 0.40); // Jade-emerald
        var leafTip = Primitives.Mix(c2, new[] { 0.26, 0.48, 0.16 }, 0.42); // Chartreuse-olive
        var strobiliCol = new[] { 0.75, 0.62, 0.22 }; // Amber-jade strobili beads

        // 1. Basal Rhizome Stool & Emerging Shoots
        int runnerCount = detail == 2 ? 4 : 6;
        for (int r = 0; r < runnerCount; r++)
        {
            double a = rng.Range(0, Math.Tau);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var rPath = new[]
            {
                dir * 0.06 + Vec3.Up * 0.012,
                dir * 0.28 + Vec3.Up * 0.018,
                dir * 0.52 + Vec3.Up * 0.008
            };
            Primitives.Tube(m, rPath, new[] { 0.014, 0.010, 0.005 }, 5,
                (i, v) => (wood, 1.0, i * 0.2, v, 2, 0));
        }

        // 4-6 Pointed emerging bamboo shoots around clump perimeter
        int shootCount = detail == 2 ? 4 : 5;
        for (int s = 0; s < shootCount; s++)
        {
            double sAng = s * Math.Tau / shootCount + rng.Range(-0.25, 0.25);
            double sDist = rng.Range(0.20, 0.42);
            var sPos = new Vec3(Math.Cos(sAng) * sDist, 0, Math.Sin(sAng) * sDist);
            double sH = rng.Range(0.12, 0.22);
            double sR = rng.Range(0.022, 0.032);
            BuildBambooShoot(m, sPos, sH, sR, wood, sheathCol, ringCol, detail);
        }

        // 2. 16-22 Culms of Varied Heights (juvenile, medium erect, and tall outer canes arching naturally outward)
        int count = detail == 2 ? 10 : detail == 1 ? 12 : 13;
        for (int k = 0; k < count; k++)
        {
            // Varied cane heights & roles:
            // k < 3: juvenile canes (0.48x-0.62x)
            // 3 <= k < 10: medium central canes (0.72x-0.86x, mostly erect with joint kinks)
            // 10 <= k: tall outer canes (0.92x-1.06x, arching gracefully outward)
            bool isJuvenile = k < 3;
            bool isOuter = k >= 10;

            double heightTier;
            if (isJuvenile) heightTier = rng.Range(0.48, 0.62);
            else if (!isOuter) heightTier = rng.Range(0.72, 0.86);
            else heightTier = rng.Range(0.92, 1.06);

            double az = rng.Range(0, Math.Tau);
            // Outer canes spring from farther out on the rhizome base
            double spreadDist = isOuter ? rng.Range(0.26, 0.44) : rng.Range(0.04, 0.24);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = outward * spreadDist;

            int nodeCount = heightTier < 0.65 ? 6 : (heightTier < 0.88 ? 7 : 8);
            var nodes = new Vec3[nodeCount];
            nodes[0] = foot;

            // Base direction: outer canes start with graceful outward splay
            double baseTilt = isOuter ? rng.Range(0.32, 0.46) : rng.Range(0.06, 0.16);
            var currentDir = (Vec3.Up + outward * baseTilt).Normalized();
            double nodeDist = (heightTier * 0.95) / (nodeCount - 1);

            // Jointed bamboo kinks along culm internodes
            for (int j = 1; j < nodeCount; j++)
            {
                double tj = (double)j / (nodeCount - 1);
                if (j > 1)
                {
                    // Alternate kink angle (12-20 degrees)
                    double turn = rng.Range(0.18, 0.28) * (j % 2 == 0 ? 1.0 : -1.0);
                    var kinkSide = currentDir.Cross(Vec3.Up).Normalized();
                    if (kinkSide.LengthSq < 1e-6) kinkSide = new Vec3(-currentDir.Z, 0, currentDir.X).Normalized();

                    // Outer canes: progressive outward bow (natural weeping fountain arch under canopy weight)
                    double archBias = isOuter ? 0.20 * (tj * tj) : (tj > 0.6 ? 0.05 * tj : 0.0);
                    double droopBias = isOuter && tj > 0.7 ? 0.08 * (tj - 0.7) : 0.0;
                    currentDir = (currentDir * Math.Cos(turn) + kinkSide * Math.Sin(turn) * (1.0 - 0.3 * tj)
                        + outward * archBias + Vec3.Up * (0.05 - droopBias)).Normalized();
                }
                nodes[j] = nodes[j - 1] + currentDir * (nodeDist * rng.Range(0.95, 1.05));
            }

            // Tube along culm internodes
            var radii = new double[nodeCount];
            for (int j = 0; j < nodeCount; j++)
            {
                double tj = (double)j / (nodeCount - 1);
                radii[j] = MathD.Lerp(0.020 * heightTier, 0.0055, tj);
            }
            Primitives.Tube(m, nodes, radii, detail == 2 ? 4 : 5,
                (i, v) => (Primitives.Mix(wood, tipWood, (double)i / (nodeCount - 1)), 1.0, (double)i / (nodeCount - 1), v, 2, 0));

            // Double-ridged amber-copper nodal rings & peeling sheaths
            for (int j = 1; j < nodeCount - 1; j++)
            {
                var tangent = (nodes[j + 1] - nodes[j - 1]).Normalized();
                var nPos = nodes[j];
                double rNode = radii[j];

                // Lower ring (sheath scar ring)
                var pRing1 = nPos - tangent * 0.005;
                Primitives.Tube(m, new[] { pRing1 - tangent * 0.003, pRing1, pRing1 + tangent * 0.003 },
                    new[] { rNode * 1.02, rNode * 1.16, rNode * 1.02 }, 5,
                    (st, v) => (ringCol, 1.0, st, v, 1.22, 0.80));

                // Upper ring (supranodal ridge)
                var pRing2 = nPos + tangent * 0.006;
                Primitives.Tube(m, new[] { pRing2 - tangent * 0.003, pRing2, pRing2 + tangent * 0.003 },
                    new[] { rNode * 1.02, rNode * 1.22, rNode * 1.02 }, 5,
                    (st, v) => (ringCol, 1.0, st, v, 1.22, 0.80));

                // Papery culm-sheaths peeling from lower nodes (nodes 1-3)
                if (j <= 3)
                {
                    double sheathLen = nodeDist * 0.45;
                    var peelDir = (outward * 0.70 + Vec3.Up * 0.30).Normalized();
                    var sheathTip = nPos + peelDir * (rNode * 1.45 + sheathLen * 0.5) + tangent * sheathLen;
                    var sheathSide = tangent.Cross(peelDir).Normalized();
                    Primitives.CurvedLeaf(m, nPos, sheathTip, sheathSide, rNode * 0.85,
                        sheathCol, sheathCol, camber: 0.004, longitudinal: 2);
                }

                // 3. Multi-Tiered Feathery Palmate Leaf Sprays in Upper 60% of Culm
                double heightFraction = (double)j / (nodeCount - 1);
                if (!isJuvenile && heightFraction >= 0.48)
                {
                    int branchlets = heightFraction > 0.72 ? (detail == 2 ? 1 : 2) : 1;
                    for (int b = 0; b < branchlets; b++)
                    {
                        // Sub-branches spread HORIZONTALLY outward like airy bamboo shelves
                        double bTheta = az + (b - (branchlets - 1) * 0.5) * 0.85 + rng.Range(-0.12, 0.12);
                        var bHoriz = new Vec3(Math.Cos(bTheta), 0, Math.Sin(bTheta));
                        var bDir = (bHoriz * 0.94 + Vec3.Up * rng.Range(0.04, 0.16)).Normalized();
                        double bLen = rng.Range(0.12, 0.19) * heightTier;

                        // Flared nodal branch collar pad at junction
                        Primitives.Ellipsoid(m, nPos + bDir * (rNode * 0.75),
                            new Vec3(rNode * 0.40, rNode * 0.45, rNode * 0.40), 4, 5,
                            (u, v) => (ringCol, 1.0, u, v, 1.22, 0.80));

                        // Slender curved branchlet tube with flared collar base
                        var bMid = nPos + bDir * (bLen * 0.5) + Vec3.Up * (bLen * 0.06);
                        var bTip = nPos + bDir * bLen;
                        Primitives.Tube(m, new[] { nPos, bMid, bTip },
                            new[] { rNode * 0.30, 0.0024, 0.0014 }, 4,
                            (st, v) => (tipWood, 1.0, st, v, 2, 0));

                        // Lush feathery lanceolate leaf spray: 3 leaves (central leader + flanking pair)
                        // forming a layered horizontal bamboo shelf
                        int leavesInSpray = detail == 2 ? 2 : 3;
                        for (int lf = 0; lf < leavesInSpray; lf++)
                        {
                            double lfSpread = (lf - (leavesInSpray - 1) * 0.5) * 0.42;
                            var lfHoriz = (bHoriz * Math.Cos(lfSpread) + bHoriz.Cross(Vec3.Up) * Math.Sin(lfSpread)).Normalized();
                            // Flat / gentle droop: elevation ~78°-88° from vertical
                            var leafDir = (lfHoriz * 0.96 + Vec3.Up * rng.Range(0.04, 0.14) - Vec3.Up * (0.04 * Math.Abs(lfSpread))).Normalized();
                            double leafLen = (lf == 1 && leavesInSpray == 3 ? rng.Range(0.22, 0.27) : rng.Range(0.18, 0.23)) * heightTier;
                            var bladeRoot = bTip + leafDir * 0.008;
                            var bladeTip = bladeRoot + leafDir * leafLen;

                            // Explicit sky-facing normals for FoliageBlade
                            var leafSide = Vec3.Up.Cross(leafDir).Normalized();
                            double halfWidth = leafLen * rng.Range(0.11, 0.14); // Feathery bamboo profile

                            FoliageBlade.Build(m, bladeRoot, bladeTip, leafSide, halfWidth,
                                leafBase, leafTip, camber: 0.10, curl: 0.04, shoulder: 0.82,
                                segments: detail == 2 ? 2 : 3);
                        }
                    }

                    // 4. Glossy amber-jade beaded spore-berries (UV2 = 1.30, 0.88)
                    if (heightFraction >= 0.65 && (k + j) % 2 == 0)
                    {
                        var beadOrigin = nPos + (tangent * 0.4 + outward * 0.6).Normalized() * (rNode * 1.15);
                        int beadCount = detail == 2 ? 2 : 3;
                        for (int bd = 0; bd < beadCount; bd++)
                        {
                            double bAng = bd * Math.Tau / beadCount;
                            var bOffset = new Vec3(Math.Cos(bAng) * 0.007, (bd - 1) * 0.006, Math.Sin(bAng) * 0.007);
                            double beadR = rng.Range(0.007, 0.010);
                            Primitives.Ellipsoid(m, beadOrigin + bOffset, new Vec3(beadR, beadR * 1.1, beadR),
                                detail == 2 ? 3 : 4, detail == 2 ? 4 : 6,
                                (u, v) => (strobiliCol, 1.0, u, v, 1.30, 0.88));
                        }
                    }
                }
            }

            // Cane apex crown fan: lush feathery leaves cascading outward horizontally
            var apexDir = (nodes[^1] - nodes[^2]).Normalized();
            var apexSide = Vec3.Up.Cross(apexDir).Normalized();
            if (apexSide.LengthSq < 1e-4) apexSide = new Vec3(-apexDir.Z, 0, apexDir.X).Normalized();

            int apexLeaves = detail == 2 ? 3 : 4;
            for (int af = 0; af < apexLeaves; af++)
            {
                double afSpread = (af - (apexLeaves - 1) * 0.5) * 0.38;
                var termHoriz = (outward * Math.Cos(afSpread) + apexSide * Math.Sin(afSpread)).Normalized();
                var fDir = (termHoriz * 0.90 + Vec3.Up * rng.Range(0.06, 0.16) - Vec3.Up * (0.05 * Math.Abs(afSpread))).Normalized();
                double fLen = rng.Range(0.16, 0.22) * heightTier;
                var fSide = Vec3.Up.Cross(fDir).Normalized();
                FoliageBlade.Build(m, nodes[^1], nodes[^1] + fDir * fLen, fSide, fLen * 0.12,
                    leafBase, leafTip, camber: 0.10, curl: 0.04, shoulder: 0.82, segments: detail == 2 ? 2 : 3);
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
