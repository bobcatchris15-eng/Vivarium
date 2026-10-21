using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Splitcrown (microcanopy_splitcrown) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A multi-stemmed forked miniature woodland tree inspired by Staghorn Sumac (Rhus typhina),
/// Mountain Ash (Sorbus), and Aralia, carrying separated airy crowns of fine pinnate
/// compound leaves above an open, traversable understory space.
///
/// Visual Architecture:
///   1. Multi-Stemmed Basal Crown: Two or three slender trunks rising from a shared root crown,
///      splaying outward with graceful Bézier curvature to leave the central understory open.
///   2. Smooth Pale Bark: Smooth aspen/birch/poplar grey-green bark with textured lenticels
///      and dark charcoal longitudinal fissures in mature lower wood.
///   3. Separated Asymmetrical Crown Ceilings: Independent scaffold limbs arching into
///      dappled, multi-tiered forest ceilings without closing into a solid wall.
///   4. True Pinnate Compound Leaves: Elegant arching rachises carrying pairs of delicate,
///      lanceolate leaflets and acute terminal blades that flutter in woodland air.
/// </summary>
public static partial class OrganismMeshes
{
    // Smooth grey woodland bark palette
    private static readonly double[] SplitBarkPale    = { 0.58, 0.60, 0.54 };   // Smooth pale grey outer stipe
    private static readonly double[] SplitBarkMid     = { 0.46, 0.48, 0.42 };   // Weathered grey-green body
    private static readonly double[] SplitBarkFissure = { 0.16, 0.13, 0.10 };   // Dark mature basal splits
    private static readonly double[] SplitBarkEdge    = { 0.28, 0.24, 0.19 };   // Fissure margin wood

    // Compound leaf palette
    private static readonly double[] SplitRachisCol   = { 0.38, 0.26, 0.18 };   // Warm reddish-amber petiole/rachis
    private static readonly double[] SplitLeafEmerald = { 0.34, 0.54, 0.26 };   // Vibrant dappled canopy highlight
    private static readonly double[] SplitLeafGlaucous= { 0.48, 0.60, 0.44 };   // Pale chalky/sage underside

    /// <summary>Evaluates a cubic Bézier curve at parameter t in [0, 1].</summary>
    private static Vec3 SplitcrownBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    /// <summary>Evaluates a cubic Bézier tangent vector at parameter t in [0, 1].</summary>
    private static Vec3 SplitcrownBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>Discretizes a cubic Bézier curve into smooth path stations.</summary>
    private static Vec3[] SplitcrownSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts[i] = SplitcrownBezier(p0, c0, c1, p1, t);
        }
        return pts;
    }

    /// <summary>
    /// Builds a smooth woodland stem with pale grey/green bark, mature basal fissures, and smooth normals.
    /// </summary>
    private static void SplitcrownStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        bool isLowerTrunk = false)
    {
        int stations = points.Count;
        if (stations < 2 || radii.Count != stations) return;

        int segments = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
        int startVertex = m.VertexCount;

        var tangents = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            Vec3 d;
            if (i == 0) d = points[1] - points[0];
            else if (i == stations - 1) d = points[^1] - points[^2];
            else d = points[i + 1] - points[i - 1];
            tangents[i] = d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
        }

        var normalFrames = new Vec3[stations];
        var binormalFrames = new Vec3[stations];

        var hint = Vec3.Up;
        var side0 = tangents[0].Cross(hint);
        if (side0.LengthSq < 1e-6) side0 = tangents[0].Cross(new Vec3(1, 0, 0));
        side0 = side0.Normalized();
        normalFrames[0] = side0;
        binormalFrames[0] = side0.Cross(tangents[0]).Normalized();

        for (int i = 1; i < stations; i++)
        {
            var curT = tangents[i];
            var prevN = normalFrames[i - 1];

            var transportedN = prevN - curT * curT.Dot(prevN);
            if (transportedN.LengthSq < 1e-6)
            {
                transportedN = curT.Cross(hint);
                if (transportedN.LengthSq < 1e-6) transportedN = curT.Cross(new Vec3(1, 0, 0));
            }
            normalFrames[i] = transportedN.Normalized();
            binormalFrames[i] = normalFrames[i].Cross(curT).Normalized();
        }

        var ringPositions = new Vec3[stations, segments + 1];
        var ringNormals = new Vec3[stations, segments + 1];
        var ringColors = new double[stations, segments + 1][];

        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            double baseR = radii[i];
            var center = points[i];
            var norm = normalFrames[i];
            var binorm = binormalFrames[i];

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2.0 * Math.PI * u;

                double splitIndent = 0.0;
                double splitFactor = 0.0;
                if (isLowerTrunk && t < 0.60)
                {
                    double splitWave = Math.Cos(4.0 * th);
                    double heightDecay = 1.0 - t / 0.60;
                    if (splitWave > 0.40)
                    {
                        double fissT = (splitWave - 0.40) / 0.60;
                        splitFactor = fissT * heightDecay;
                        splitIndent = 0.15 * splitFactor;
                    }
                }

                double r = Math.Max(0.0018, baseR * (1.0 - splitIndent));
                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                var pos = center + radialDir * r;
                ringPositions[i, s] = pos;

                var barkBase = Primitives.Mix(SplitBarkMid, SplitBarkPale, t);
                var col = barkBase;
                if (splitFactor > 0.04)
                {
                    var splitCol = Primitives.Mix(SplitBarkEdge, SplitBarkFissure, splitFactor);
                    col = Primitives.Mix(barkBase, splitCol, Math.Min(1.0, splitFactor * 1.35));
                }

                ringColors[i, s] = col;
            }
        }

        for (int i = 0; i < stations; i++)
        {
            int iPrev = Math.Max(0, i - 1);
            int iNext = Math.Min(stations - 1, i + 1);

            for (int s = 0; s <= segments; s++)
            {
                int sPrev = (s == 0) ? segments - 1 : s - 1;
                int sNext = (s == segments) ? 1 : s + 1;

                var dCirc = ringPositions[i, sNext] - ringPositions[i, sPrev];
                var dLong = ringPositions[iNext, s] - ringPositions[iPrev, s];
                var surfNorm = dCirc.Cross(dLong).Normalized();

                var radialHint = ringPositions[i, s] - points[i];
                if (surfNorm.Dot(radialHint) < 0) surfNorm = -surfNorm;

                ringNormals[i, s] = surfNorm.LengthSq > 1e-6 ? surfNorm : radialHint.Normalized();
            }
        }

        for (int i = 0; i < stations; i++)
        {
            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                m.AddVertex(ringPositions[i, s], ringNormals[i, s], ringColors[i, s], 1.0, points[i].Y * 3.0, u, 2.0, 0.0);
            }
        }

        for (int i = 0; i < stations - 1; i++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = startVertex + i * (segments + 1) + s;
                int b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        m.Structural.Add((startVertex, m.VertexCount - startVertex, segments, radii[0]));
    }

    /// <summary>
    /// Builds a single pinnate leaflet attached to the central compound rachis.
    /// </summary>
    private static void SplitcrownLeaflet(
        MeshData m,
        Vec3 basePos,
        Vec3 leafDir,
        Vec3 leafNormal,
        double length,
        double width,
        ulong seed,
        double[] colTopBase,
        double[] colTopTip,
        double[] colUnder,
        int detail)
    {
        if (length <= 1e-4 || width <= 1e-5) return;

        int rows = detail == 2 ? 3 : 4;
        int cols = detail == 2 ? 1 : 2;

        var side = leafNormal.Cross(leafDir);
        if (side.LengthSq < 1e-6) side = leafDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(leafDir).Normalized();

        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.TriangleCount * 3;

        double WidthProfile(double t)
        {
            if (t < 0.15) return 0.22 + 0.78 * (t / 0.15);
            if (t <= 0.60) return 1.00 + 0.25 * Math.Sin(Math.PI * (t - 0.15) / 0.45);
            double u = (t - 0.60) / 0.40;
            return Math.Max(0.04, 1.25 * Math.Pow(Math.Max(0.0, 1.0 - u), 0.85));
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.05 * Math.Sin(Math.PI * t) - 0.08 * Math.Pow(t, 1.5));
            return basePos + leafDir * along + up * arch;
        }

        int totalCols = 2 * cols + 1;
        var topPositions = new Vec3[rows, totalCols];
        var botPositions = new Vec3[rows, totalCols];
        var topNormals = new Vec3[rows, totalCols];
        var botNormals = new Vec3[rows, totalCols];
        var topColors = new double[rows, totalCols][];
        var botColors = new double[rows, totalCols][];

        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            var pSpine = SampleSpine(t);

            double dt = 0.01;
            var pNext = SampleSpine(Math.Min(1.0, t + dt));
            var pPrev = SampleSpine(Math.Max(0.0, t - dt));
            var tSpine = (pNext - pPrev).Normalized();
            var sSpine = up.Cross(tSpine).Normalized();
            var nSpine = tSpine.Cross(sSpine).Normalized();

            double w = width * 0.5 * WidthProfile(t);

            for (int c = 0; c < totalCols; c++)
            {
                double x = (c - cols) / (double)cols;
                double absX = Math.Abs(x);

                double gutter = 0.035 * width * (x * x - 1.0);
                double keel = -0.065 * width * Math.Pow(Math.Max(0.0, 1.0 - absX), 1.3) - 0.010 * width;

                var offsetSide = sSpine * (w * x);
                topPositions[r, c] = pSpine + offsetSide + nSpine * gutter;
                botPositions[r, c] = pSpine + offsetSide + nSpine * keel;

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.55 || t > 0.70)
                {
                    double marginFrac = Math.Max((absX - 0.55) / 0.45, (t - 0.70) / 0.30);
                    bodyCol = Primitives.Mix(bodyCol, SplitLeafEmerald, marginFrac * 0.45);
                }
                topColors[r, c] = bodyCol;

                var underCol = colUnder;
                if (absX < 0.25)
                {
                    double kFrac = (0.25 - absX) / 0.25;
                    underCol = Primitives.Mix(underCol, SplitLeafGlaucous, kFrac * 0.60);
                }
                botColors[r, c] = underCol;
            }
        }

        for (int r = 0; r < rows; r++)
        {
            int rP = Math.Max(0, r - 1);
            int rN = Math.Min(rows - 1, r + 1);

            for (int c = 0; c < totalCols; c++)
            {
                int cP = Math.Max(0, c - 1);
                int cN = Math.Min(totalCols - 1, c + 1);

                var dCircTop = topPositions[r, cN] - topPositions[r, cP];
                var dLongTop = topPositions[rN, c] - topPositions[rP, c];
                var nTop = dLongTop.Cross(dCircTop).Normalized();
                topNormals[r, c] = nTop.LengthSq > 1e-6 ? nTop : up;

                var dCircBot = botPositions[r, cN] - botPositions[r, cP];
                var dLongBot = botPositions[rN, c] - botPositions[rP, c];
                var nBot = dCircBot.Cross(dLongBot).Normalized();
                botNormals[r, c] = nBot.LengthSq > 1e-6 ? nBot : -up;
            }
        }

        int topStart = m.VertexCount;
        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            for (int c = 0; c < totalCols; c++)
            {
                double u = c / (double)(totalCols - 1);
                m.AddVertex(topPositions[r, c], topNormals[r, c], topColors[r, c], 1.0, t, u, 0.0, 1.0);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < totalCols - 1; c++)
            {
                int a = topStart + r * totalCols + c;
                int b = a + totalCols;
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        int botStart = m.VertexCount;
        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            for (int c = 0; c < totalCols; c++)
            {
                double u = c / (double)(totalCols - 1);
                m.AddVertex(botPositions[r, c], botNormals[r, c], botColors[r, c], 1.0, t, u, 0.0, 1.0);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < totalCols - 1; c++)
            {
                int a = botStart + r * totalCols + c;
                int b = a + totalCols;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            int t0 = topStart + r * totalCols;
            int t1 = topStart + (r + 1) * totalCols;
            int b0 = botStart + r * totalCols;
            int b1 = botStart + (r + 1) * totalCols;
            m.AddTriangle(t0, b0, t1);
            m.AddTriangle(t1, b0, b1);

            int rt0 = topStart + r * totalCols + (totalCols - 1);
            int rt1 = topStart + (r + 1) * totalCols + (totalCols - 1);
            int rb0 = botStart + r * totalCols + (totalCols - 1);
            int rb1 = botStart + (r + 1) * totalCols + (totalCols - 1);
            m.AddTriangle(rt0, rt1, rb0);
            m.AddTriangle(rt1, rb1, rb0);
        }

        m.RecordLeaf(leafStartVertex, leafStartIndex, basePos, length, volumetric: false);
    }

    /// <summary>
    /// Builds a full pinnate compound leaf (arching central rachis with 5-7 pairs of leaflets and terminal blade).
    /// </summary>
    private static void SplitcrownCompoundFrond(
        MeshData m,
        Vec3 attachPos,
        Vec3 frondDir,
        Vec3 frondNormal,
        double frondLength,
        double scale,
        ulong seed,
        double[] col1,
        double[] col2,
        int detail)
    {
        var underColor = Primitives.Mix(col2, SplitLeafGlaucous, 0.40);
        var topBaseColor = Primitives.Mix(col1, new[] { 0.14, 0.32, 0.18 }, 0.40);
        var topTipColor = Primitives.Mix(col1, new[] { 0.28, 0.48, 0.24 }, 0.35);

        var side = frondNormal.Cross(frondDir);
        if (side.LengthSq < 1e-6) side = frondDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(frondDir).Normalized();

        // 1. Arching central rachis
        int rachisStations = detail == 2 ? 4 : 6;
        var pRachisEnd = attachPos + frondDir * frondLength - Vec3.Up * (frondLength * 0.16);
        var cRachis0 = attachPos + (frondDir * 0.70 + Vec3.Up * 0.30).Normalized() * (frondLength * 0.40);
        var cRachis1 = pRachisEnd - (frondDir * 0.90 - Vec3.Up * 0.15).Normalized() * (frondLength * 0.35);

        var rachisPath = SplitcrownSampleBezier(attachPos, cRachis0, cRachis1, pRachisEnd, rachisStations);
        var rachisRadii = new double[rachisStations];
        for (int i = 0; i < rachisStations; i++)
        {
            double t = i / (double)(rachisStations - 1);
            rachisRadii[i] = MathD.Lerp(0.0035 * scale, 0.0012 * scale, t);
        }

        // Build rachis stem
        SplitcrownStem(m, rachisPath, rachisRadii, detail, isLowerTrunk: false);

        // 2. Pairs of pinnate leaflets along rachis
        int numPairs = detail == 2 ? 4 : (detail == 1 ? 5 : 6);
        double pairStart = 0.20;
        double pairEnd = 0.90;

        for (int p = 0; p < numPairs; p++)
        {
            double tPair = MathD.Lerp(pairStart, pairEnd, p / (double)(numPairs - 1));
            var pNode = SplitcrownBezier(attachPos, cRachis0, cRachis1, pRachisEnd, tPair);
            var tNode = SplitcrownBezierTangent(attachPos, cRachis0, cRachis1, pRachisEnd, tPair);
            var sNode = up.Cross(tNode).Normalized();
            var nNode = tNode.Cross(sNode).Normalized();

            // Leaflet length peaks in middle of frond, tapers near base and tip
            double pairFactor = Math.Sin(Math.PI * (0.15 + 0.85 * (p / (double)(numPairs - 1))));
            double leafletLen = (0.052 + 0.015 * pairFactor) * scale;
            double leafletWid = leafletLen * 0.35;

            // Left and right leaflets
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                var lDir = (sNode * sideSign * 0.88 + tNode * 0.42 - nNode * 0.12).Normalized();
                var lNorm = (nNode * 0.88 + sNode * (sideSign * 0.40)).Normalized();
                ulong lSeed = Rng.Mix(seed, (ulong)(p * 100 + (sideSign > 0 ? 1 : 2)));

                SplitcrownLeaflet(m, pNode + sNode * (sideSign * 0.003 * scale), lDir, lNorm,
                    leafletLen, leafletWid, lSeed, topBaseColor, topTipColor, underColor, detail);
            }
        }

        // 3. Terminal leaflet at the tip of the rachis
        var termDir = SplitcrownBezierTangent(attachPos, cRachis0, cRachis1, pRachisEnd, 1.0);
        double termLen = 0.056 * scale;
        double termWid = termLen * 0.34;
        ulong termSeed = Rng.Mix(seed, 9999);
        SplitcrownLeaflet(m, pRachisEnd, termDir, up, termLen, termWid, termSeed,
            topBaseColor, topTipColor, underColor, detail);
    }

    /// <summary>
    /// Splitcrown procedural generator.
    /// Builds a multi-stemmed forked miniature woodland tree carrying separated
    /// compound-leaf crowns above an open, traversable understory space.
    /// </summary>
    private static void Splitcrown(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        int detail = m.FloraDetailLevel ?? 0;

        // 2 to 3 forked trunks
        int numTrunks = juvenile ? 1 : 2 + (int)rng.NextInt(2);
        double baseTrunkAz = rng.Range(0, Math.PI * 2);

        for (int tr = 0; tr < numTrunks; tr++)
        {
            double trAz = baseTrunkAz + 2.0 * Math.PI * tr / numTrunks + rng.Range(-0.20, 0.20);
            var trOutward = MidRadial(trAz);
            var trOrtho = MidRadial(trAz + Math.PI * 0.5);

            // ---------------------------------------------------------------------
            // 1. Sinuous Forked Trunk (Curving Outward from Shared Base)
            // ---------------------------------------------------------------------
            var pTrunkBase = trOutward * (tr == 0 ? 0.01 : rng.Range(0.04, 0.10));
            double forkH = juvenile ? rng.Range(0.30, 0.36) : rng.Range(0.38, 0.46);
            double forkReach = rng.Range(0.08, 0.16);
            var pFork = pTrunkBase + trOutward * forkReach + trOrtho * rng.Range(-0.03, 0.03) + Vec3.Up * forkH;

            // Smooth Bézier curve for trunk splay
            var cTrunk0 = pTrunkBase + (Vec3.Up * 0.75 + trOutward * 0.35).Normalized() * (forkH * 0.42);
            var cTrunk1 = pFork - (Vec3.Up * 0.85 + trOutward * 0.30).Normalized() * (forkH * 0.38);

            int trunkStations = detail == 2 ? 6 : (detail == 1 ? 9 : 12);
            var trunkPoints = SplitcrownSampleBezier(pTrunkBase, cTrunk0, cTrunk1, pFork, trunkStations);
            var trunkRadii = new double[trunkStations];

            double rBase = juvenile ? rng.Range(0.024, 0.030) : rng.Range(0.034, 0.042);
            double rFork = rBase * 0.65;

            for (int i = 0; i < trunkStations; i++)
            {
                double t = i / (double)(trunkStations - 1);
                trunkRadii[i] = MathD.Lerp(rBase, rFork, Math.Pow(t, 0.85));
            }

            SplitcrownStem(m, trunkPoints, trunkRadii, detail, isLowerTrunk: true);

            // ---------------------------------------------------------------------
            // 2. Scaffold Branches Forming Independent Crown Ceiling
            // ---------------------------------------------------------------------
            int numBranches = juvenile ? 3 : (detail == 2 ? 3 : 4 + (int)rng.NextInt(2));
            for (int b = 0; b < numBranches; b++)
            {
                double bAz = trAz + 2.0 * Math.PI * b / numBranches + rng.Range(-0.22, 0.22);
                var bOutward = MidRadial(bAz);
                var bOrtho = MidRadial(bAz + Math.PI * 0.5);

                double reach = rng.Range(0.55, 0.78);
                double summitY = juvenile ? rng.Range(0.68, 0.71) : rng.Range(0.72, 0.76);

                var pBranchEnd = pFork + bOutward * reach + bOrtho * rng.Range(-0.05, 0.05) + Vec3.Up * (summitY - forkH);
                var cBranch0 = pFork + (bOutward * 0.45 + Vec3.Up * 0.75).Normalized() * (reach * 0.42);
                var cBranch1 = pBranchEnd - (bOutward * 0.90 + Vec3.Up * 0.20).Normalized() * (reach * 0.38);

                int branchStations = detail == 2 ? 5 : (detail == 1 ? 7 : 9);
                var branchPath = SplitcrownSampleBezier(pFork, cBranch0, cBranch1, pBranchEnd, branchStations);
                var branchRadii = new double[branchStations];

                double branchBaseR = rFork * rng.Range(0.58, 0.70);
                double branchTipR = Math.Max(0.004, branchBaseR * 0.35);

                for (int i = 0; i < branchStations; i++)
                {
                    double st = i / (double)(branchStations - 1);
                    branchRadii[i] = MathD.Lerp(branchBaseR, branchTipR, st);
                }

                SplitcrownStem(m, branchPath, branchRadii, detail, isLowerTrunk: false);

                // Terminal compound frond cluster at branch tip
                var tipDir = SplitcrownBezierTangent(pFork, cBranch0, cBranch1, pBranchEnd, 1.0);
                int termFronds = detail == 2 ? 3 : (juvenile ? 3 : 4 + (int)rng.NextInt(2));

                for (int tf = 0; tf < termFronds; tf++)
                {
                    double spreadAngle = (tf - (termFronds - 1) * 0.5) * 0.45 + rng.Range(-0.08, 0.08);
                    var frondDir = (tipDir * 0.85 + bOrtho * Math.Sin(spreadAngle) + Vec3.Up * (0.12 - 0.15 * Math.Abs(spreadAngle))).Normalized();
                    var frondNorm = (Vec3.Up * 0.88 + bOutward * 0.40).Normalized();

                    double frondLen = rng.Range(0.16, 0.22);
                    ulong frondSeed = Rng.Mix(seed, (ulong)(tr * 100000 + b * 10000 + tf * 100 + 43));

                    SplitcrownCompoundFrond(m, pBranchEnd, frondDir, frondNorm, frondLen,
                        scale: juvenile ? 0.80 : 1.00, frondSeed, c1, c2, detail);
                }

                // Intermediate compound fronds along the branch
                int midFrondNodes = detail == 2 ? 1 : 2;
                for (int mf = 0; mf < midFrondNodes; mf++)
                {
                    double mFrac = mf == 0 ? 0.50 : 0.75;
                    var mPos = SplitcrownBezier(pFork, cBranch0, cBranch1, pBranchEnd, mFrac);
                    var mTan = SplitcrownBezierTangent(pFork, cBranch0, cBranch1, pBranchEnd, mFrac);

                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    {
                        var mSide = bOrtho * sideSign;
                        var mFrondDir = (mSide * 0.78 + mTan * 0.45 + Vec3.Up * 0.06).Normalized();
                        var mFrondNorm = (Vec3.Up * 0.85 + mSide * 0.45).Normalized();

                        double mLen = rng.Range(0.14, 0.18);
                        ulong mSeed = Rng.Mix(seed, (ulong)(tr * 200000 + b * 20000 + mf * 2000 + (sideSign > 0 ? 1 : 2)));

                        SplitcrownCompoundFrond(m, mPos, mFrondDir, mFrondNorm, mLen,
                            scale: juvenile ? 0.75 : 0.90, mSeed, c1, c2, detail);
                    }
                }
            }
        }
    }
}
