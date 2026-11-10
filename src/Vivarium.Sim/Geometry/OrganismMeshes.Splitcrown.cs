using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Splitcrown (microcanopy_splitcrown) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A multi-stemmed forked miniature woodland tree inspired by Staghorn Sumac (Rhus typhina),
/// Silk Tree / Mimosa (Albizia julibrissin), and Mountain Ash (Sorbus), carrying separated airy crowns
/// of fine pinnate compound leaves above an open, traversable understory space.
///
/// Visual Architecture:
///   1. Mossy Buttressed Stool & Diverging Trunks with Swollen Collars: Gnarled root-collar stool at ground
///      level with mossy buttresses, giving rise to 2-3 sinuous diverging trunks joined with smooth collar swellings
///      that eliminate sharp V-notches, clad in pale lichen-dappled grey-ochre bark with mature fissures (UV2 = (2, 0)).
///   2. Multi-Tiered Staggered Acacia/Albizia Scaffold Boughs: Varied scaffold bough heights (lower sweeping boughs,
///      mid-canopy shelves, and upper crown tiers) eliminating flat table-tops.
///   3. Feathery Pinnate Compound Leaves: Arching rachises carrying pairs of cambered acuminate leaflets
///      distributed along the boughs and at the tips with blended +Up normals.
///   4. Whimsical Signature:
///      - Glowing rose-gold & apricot silk-puff mimosa pom-poms perched among the foliage tiers (UV2 = (1.32, 0.88)).
///      - Large, curved, scalloped velvet-indigo & plum seed-pods with golden sutures dangling in the open space between trunks.
/// </summary>
public static partial class OrganismMeshes
{
    // Smooth grey woodland bark palette (UV2 = (2, 0))
    private static readonly double[] SplitBarkPale    = { 0.58, 0.60, 0.54 };   // Smooth pale grey outer stipe
    private static readonly double[] SplitBarkMid     = { 0.46, 0.48, 0.42 };   // Weathered grey-green body
    private static readonly double[] SplitBarkFissure = { 0.16, 0.13, 0.10 };   // Dark mature basal splits
    private static readonly double[] SplitBarkEdge    = { 0.28, 0.24, 0.19 };   // Fissure margin wood
    private static readonly double[] SplitMossStool   = { 0.20, 0.36, 0.14 };   // Mossy basal stool

    // Compound leaf palette (UV2 = (0, 1))
    private static readonly double[] SplitRachisCol   = { 0.38, 0.26, 0.18 };   // Warm reddish-amber petiole/rachis
    private static readonly double[] SplitLeafEmerald = { 0.32, 0.54, 0.24 };   // Vibrant dappled canopy highlight
    private static readonly double[] SplitLeafChartreuse = { 0.48, 0.62, 0.26 };// Sunlit leaflet edge
    private static readonly double[] SplitLeafGlaucous= { 0.48, 0.60, 0.44 };   // Pale chalky/sage underside

    // Whimsical Silk-Puff Pom-Poms (UV2 = 1.32, 0.88) & Velvet Indigo Seed Pods
    private static readonly double[] SplitSilkRose    = { 0.96, 0.46, 0.54 };   // Glowing rose-gold silk puff
    private static readonly double[] SplitSilkApricot = { 1.00, 0.74, 0.38 };   // Radiant apricot stamen tips
    private static readonly double[] SplitPodIndigo   = { 0.16, 0.12, 0.34 };   // Deep velvet indigo pod body
    private static readonly double[] SplitPodPlum     = { 0.28, 0.10, 0.26 };   // Rich plum pod shading
    private static readonly double[] SplitPodSuture   = { 0.84, 0.68, 0.24 };   // Warm golden suture margin

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

    private static void SplitcrownBranchCollar(MeshData m, Vec3 center, Vec3 tangent, double radius, double height, int detail) =>
        Form.ForkJunction.Collar(m, center, tangent, radius, height, detail, SplitBarkMid, SplitBarkPale, 0.10, 4);

    /// <summary>
    /// Builds a gnarled, mossy buttressed root stool at the ground where trunks divide.
    /// </summary>
    private static void SplitcrownBasalStool(
        MeshData m,
        Vec3 center,
        double radius,
        double height,
        int detail)
    {
        int stoolStart = m.VertexCount;
        int rings = detail == 2 ? 3 : 5;
        int sectors = detail == 2 ? 8 : 12;

        for (int r = 0; r <= rings; r++)
        {
            double tr = r / (double)rings;
            double y = height * (1.0 - tr * tr);
            double rad = radius * MathD.Lerp(0.60, 1.45, tr);

            for (int s = 0; s <= sectors; s++)
            {
                double u = s / (double)sectors;
                double th = 2.0 * Math.PI * u;

                double buttressLobe = Math.Max(0.0, Math.Cos(4.0 * th));
                double rMod = rad * (1.0 + 0.35 * Math.Pow(buttressLobe, 2.0) * tr);

                var pos = center + new Vec3(Math.Cos(th) * rMod, y, Math.Sin(th) * rMod);
                var norm = new Vec3(Math.Cos(th), 0.40, Math.Sin(th)).Normalized();

                var col = Primitives.Mix(SplitBarkFissure, SplitMossStool, Math.Sin(th * 2.0 + y * 10.0) * 0.5 + 0.5);
                m.AddVertex(pos, norm, col, 1.0, y * 3.0, u, 2.0, 0.0);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < sectors; s++)
            {
                int a = stoolStart + r * (sectors + 1) + s;
                int b = a + sectors + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        m.Structural.Add((stoolStart, m.VertexCount - stoolStart, sectors, radius));
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
    /// Builds a single pinnate leaflet attached to the compound rachis with camber,
    /// central fold, and blended +Up normals.
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

        // Controlled tessellation: detail 0 is 7 rows x 2 cols (4 across = 24 quads = 48 tris).
        // detail 1 is 4 rows x 1 col (2 across = 6 quads = 12 tris).
        // detail 2 is 3 rows x 1 col (2 across = 4 quads = 8 tris).
        int rows = detail == 0 ? 7 : (detail == 1 ? 4 : 3);
        int cols = detail == 0 ? 2 : 1;
        if (m.Lod is { } lod) rows = Math.Max(3, lod.Segments(rows, detail == 0 ? 5 : 3));

        var side = leafNormal.Cross(leafDir);
        if (side.LengthSq < 1e-6) side = leafDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(leafDir).Normalized();

        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.TriangleCount * 3;

        double phase = (seed % 983) / 983.0 * Math.PI * 2.0;

        // Oblong-lanceolate leaflet profile
        double WidthProfile(double t)
        {
            if (t < 0.15) return 0.25 + 0.75 * (t / 0.15);
            if (t <= 0.65) return 1.00 + 0.22 * Math.Sin(Math.PI * (t - 0.15) / 0.50);
            double u = (t - 0.65) / 0.35;
            return Math.Max(0.035, 1.22 * Math.Pow(Math.Max(0.0, 1.0 - u), 1.15));
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.05 * Math.Sin(Math.PI * t) - 0.08 * Math.Pow(t, 1.5));
            double drip = -length * 0.050 * Math.Pow(Math.Max(0.0, (t - 0.80) / 0.20), 2.0);
            double sway = length * 0.012 * Math.Sin(Math.PI * 1.25 * t + phase);
            return basePos + leafDir * along + up * (arch + drip) + side * sway;
        }

        int totalCols = 2 * cols + 1;
        var topPositions = new Vec3[rows, totalCols];
        var topNormals = new Vec3[rows, totalCols];
        var topColors = new double[rows, totalCols][];

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

                double gutter = 0.040 * width * (x * x - 1.0);
                double ripple = 0.016 * width * Math.Pow(absX, 1.6) * Math.Sin(t * Math.PI * 3.0 + phase);
                double tipDroop = -0.022 * width * Math.Pow(absX, 1.4) * Math.Pow(t, 2.8);

                topPositions[r, c] = pSpine + sSpine * (w * x) + nSpine * (gutter + ripple + tipDroop);

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.55 || t > 0.65)
                {
                    double marginFrac = Math.Max((absX - 0.55) / 0.45, (t - 0.65) / 0.35);
                    bodyCol = Primitives.Mix(bodyCol, SplitLeafChartreuse, marginFrac * 0.45);
                }
                topColors[r, c] = bodyCol;
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

                var dCirc = topPositions[r, cN] - topPositions[r, cP];
                var dLong = topPositions[rN, c] - topPositions[rP, c];
                var n = dLong.Cross(dCirc).Normalized();
                if (n.LengthSq < 1e-6) n = up;

                var blendedN = (n * 0.35 + Vec3.Up * 0.65).Normalized();
                topNormals[r, c] = blendedN;
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
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        m.RecordLeaf(leafStartVertex, leafStartIndex, basePos, length, volumetric: false);
    }

    /// <summary>
    /// Builds a full pinnate compound leaf (arching central rachis with pairs of leaflets and terminal blade).
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
        var topBaseColor = Primitives.Mix(col1, new[] { 0.16, 0.35, 0.20 }, 0.40);
        var topTipColor = Primitives.Mix(col1, SplitLeafEmerald, 0.35);

        var side = frondNormal.Cross(frondDir);
        if (side.LengthSq < 1e-6) side = frondDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(frondDir).Normalized();

        // 1. Arching central rachis
        int rachisStations = detail == 2 ? 4 : 6;
        var pRachisEnd = attachPos + frondDir * frondLength - Vec3.Up * (frondLength * 0.14);
        var cRachis0 = attachPos + (frondDir * 0.70 + Vec3.Up * 0.30).Normalized() * (frondLength * 0.40);
        var cRachis1 = pRachisEnd - (frondDir * 0.90 - Vec3.Up * 0.15).Normalized() * (frondLength * 0.35);

        var rachisPath = SplitcrownSampleBezier(attachPos, cRachis0, cRachis1, pRachisEnd, rachisStations);
        var rachisRadii = new double[rachisStations];
        for (int i = 0; i < rachisStations; i++)
        {
            double t = i / (double)(rachisStations - 1);
            rachisRadii[i] = MathD.Lerp(0.0032 * scale, 0.0011 * scale, t);
        }

        SplitcrownStem(m, rachisPath, rachisRadii, detail, isLowerTrunk: false);

        // 2. Pairs of pinnate leaflets along rachis (5-6 pairs)
        int numPairs = detail == 2 ? 3 : (detail == 1 ? 5 : 6);
        double pairStart = 0.20;
        double pairEnd = 0.90;

        for (int p = 0; p < numPairs; p++)
        {
            double tPair = MathD.Lerp(pairStart, pairEnd, p / (double)Math.Max(1, numPairs - 1));
            var pNode = SplitcrownBezier(attachPos, cRachis0, cRachis1, pRachisEnd, tPair);
            var tNode = SplitcrownBezierTangent(attachPos, cRachis0, cRachis1, pRachisEnd, tPair);
            var sNode = up.Cross(tNode).Normalized();
            var nNode = tNode.Cross(sNode).Normalized();

            double pairFactor = Math.Sin(Math.PI * (0.15 + 0.85 * (p / (double)Math.Max(1, numPairs - 1))));
            double leafletLen = (0.055 + 0.016 * pairFactor) * scale;
            double leafletWid = leafletLen * 0.36;

            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                var lDir = (sNode * sideSign * 0.88 + tNode * 0.42 - nNode * 0.10).Normalized();
                var lNorm = (nNode * 0.88 + sNode * (sideSign * 0.40)).Normalized();
                ulong lSeed = Rng.Mix(seed, (ulong)(p * 100 + (sideSign > 0 ? 1 : 2)));

                SplitcrownLeaflet(m, pNode + sNode * (sideSign * 0.002 * scale), lDir, lNorm,
                    leafletLen, leafletWid, lSeed, topBaseColor, topTipColor, underColor, detail);
            }
        }

        // 3. Terminal leaflet at the tip of the rachis
        var termDir = SplitcrownBezierTangent(attachPos, cRachis0, cRachis1, pRachisEnd, 1.0);
        double termLen = 0.058 * scale;
        double termWid = termLen * 0.35;
        ulong termSeed = Rng.Mix(seed, 9999);
        SplitcrownLeaflet(m, pRachisEnd, termDir, up, termLen, termWid, termSeed,
            topBaseColor, topTipColor, underColor, detail);
    }

    /// <summary>
    /// Whimsical Feature 1: Glowing rose-gold & apricot silk-puff mimosa pom-pom blossom (UV2 = (1.32, 0.88)).
    /// </summary>
    private static void SplitcrownSilkPomPom(
        MeshData m,
        Vec3 centerPos,
        double radius,
        ulong seed,
        int detail)
    {
        int pomStart = m.VertexCount;
        var rng = Rng.Keyed(seed, "splitcrown.pompom", 0);

        // Central nectar bead
        var coreCol = Primitives.Mix(SplitSilkRose, SplitSilkApricot, 0.30);
        Primitives.Ellipsoid(m, centerPos, new Vec3(radius * 0.35, radius * 0.35, radius * 0.35),
            detail == 2 ? 2 : 3, detail == 2 ? 4 : 5, (r, s) => (coreCol, 1.0, r, s, 1.32, 0.88));

        // Radiating silk tufts (mimosa stamen cloud)
        int tufts = detail == 2 ? 4 : (detail == 1 ? 7 : 12);
        for (int i = 0; i < tufts; i++)
        {
            double phi = rng.Range(0.2, Math.PI * 0.65);
            double theta = rng.Range(0, Math.PI * 2.0);
            var dir = new Vec3(Math.Sin(phi) * Math.Cos(theta), Math.Cos(phi), Math.Sin(phi) * Math.Sin(theta));

            var pTip = centerPos + dir * (radius * rng.Range(0.85, 1.25));
            var col = Primitives.Mix(SplitSilkRose, SplitSilkApricot, (double)i / tufts);

            int vStart = m.VertexCount;
            var side = dir.Cross(Vec3.Up);
            if (side.LengthSq < 1e-6) side = dir.Cross(new Vec3(1, 0, 0));
            side = side.Normalized() * (radius * 0.12);

            m.AddVertex(centerPos, dir, SplitSilkRose, 1.0, 0.0, 0.5, 1.32, 0.88);
            m.AddVertex(pTip - side, dir, col, 1.0, 1.0, 0.0, 1.32, 0.88);
            m.AddVertex(pTip + side, dir, col, 1.0, 1.0, 1.0, 1.32, 0.88);

            m.AddTriangle(vStart, vStart + 1, vStart + 2);
        }

        if (m.VertexCount > pomStart)
        {
            m.EssentialSpans.Add((pomStart, m.VertexCount - pomStart));
        }
    }

    /// <summary>
    /// Whimsical Feature 2: Large curved scalloped velvet-indigo & plum seed pods dangling between twin trunks.
    /// </summary>
    private static void SplitcrownSeedPod(
        MeshData m,
        Vec3 attachPos,
        Vec3 hangDir,
        double length,
        double width,
        ulong seed,
        int detail)
    {
        int podStart = m.VertexCount;
        var rng = Rng.Keyed(seed, "splitcrown.seedpod", 0);

        // Hanging curved spline
        var pEnd = attachPos + (hangDir * 0.35 - Vec3.Up * 0.90).Normalized() * length;
        var c0 = attachPos - Vec3.Up * (length * 0.35);
        var c1 = pEnd - (hangDir * 0.20 - Vec3.Up * 0.80).Normalized() * (length * 0.30);

        int stations = detail == 2 ? 4 : 6;
        var pts = SplitcrownSampleBezier(attachPos, c0, c1, pEnd, stations);

        var across = hangDir.Cross(Vec3.Up).Normalized();
        if (across.LengthSq < 1e-6) across = new Vec3(1, 0, 0);

        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            double bulge = 1.0 + 0.32 * Math.Sin(t * Math.PI * 4.0);
            double w = width * 0.5 * Math.Sin(Math.PI * t) * bulge;

            var center = pts[i];
            var col = Primitives.Mix(SplitPodIndigo, SplitPodPlum, t);

            m.AddVertex(center - across * w, Vec3.Up, SplitPodSuture, 1.0, t, 0.0, 0.0, 1.0);
            m.AddVertex(center - across * (w * 0.5) + Vec3.Up * 0.003, Vec3.Up, col, 1.0, t, 0.25, 0.0, 1.0);
            m.AddVertex(center + Vec3.Up * 0.005, Vec3.Up, col, 1.0, t, 0.50, 0.0, 1.0);
            m.AddVertex(center + across * (w * 0.5) + Vec3.Up * 0.003, Vec3.Up, col, 1.0, t, 0.75, 0.0, 1.0);
            m.AddVertex(center + across * w, Vec3.Up, SplitPodSuture, 1.0, t, 1.0, 0.0, 1.0);
        }

        for (int i = 0; i < stations - 1; i++)
        {
            int rowA = podStart + i * 5;
            int rowB = rowA + 5;
            for (int k = 0; k < 4; k++)
            {
                m.AddTriangle(rowA + k, rowB + k, rowA + k + 1);
                m.AddTriangle(rowA + k + 1, rowB + k, rowB + k + 1);
            }
        }

        if (m.VertexCount > podStart)
        {
            m.EssentialSpans.Add((podStart, m.VertexCount - podStart));
        }
    }

    /// <summary>
    /// Splitcrown procedural generator.
    /// Builds a multi-stemmed forked woodland tree with gnarled mossy buttressed root stool,
    /// organic branch collar swellings joining the diverging trunks, multi-tiered staggered acacia/albizia
    /// boughs with varied heights (no flat table-top), feathery pinnate fronds, glowing silk pom-poms,
    /// and hanging velvet indigo seed-pods.
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

        int numTrunks = juvenile ? 1 : 2 + (int)rng.NextInt(2);
        double baseTrunkAz = rng.Range(0, Math.PI * 2);

        // -------------------------------------------------------------------------
        // 1. Gnarled Mossy Buttressed Root-Collar Stool & Shared Collar Junction
        // -------------------------------------------------------------------------
        double stoolR = juvenile ? 0.040 : 0.060;
        double stoolH = juvenile ? 0.045 : 0.070;
        SplitcrownBasalStool(m, Vec3.Zero, stoolR, stoolH, detail);

        // Organic shared collar transition at top of stool
        SplitcrownBranchCollar(m, Vec3.Up * (stoolH * 0.85), Vec3.Up, stoolR * 1.15, stoolH * 0.45, detail);

        var podAttachPoints = new List<(Vec3 Pos, Vec3 Dir)>();

        // Staggered scaffold bough height configurations per trunk:
        // Lower sweeping shelf (0.54-0.62), Mid canopy shelf (0.68-0.75), Upper crown tier (0.80-0.90)
        int branchesPerTrunk = juvenile ? 3 : (numTrunks == 2 ? 5 : 4);
        int termFrondsPerBranch = juvenile ? 3 : (detail == 2 ? 2 : (numTrunks == 2 ? 4 : 3));

        for (int tr = 0; tr < numTrunks; tr++)
        {
            double trAz = baseTrunkAz + 2.0 * Math.PI * tr / numTrunks + rng.Range(-0.12, 0.12);
            var trOutward = MidRadial(trAz);
            var trOrtho = MidRadial(trAz + Math.PI * 0.5);

            // Sinuous Forked Trunk (Curving Outward with Muscular Basal Swelling)
            var pTrunkBase = trOutward * (stoolR * 0.40) + Vec3.Up * (stoolH * 0.80);
            double forkH = juvenile ? rng.Range(0.34, 0.40) : rng.Range(0.40, 0.48);
            double forkReach = rng.Range(0.12, 0.20);
            var pFork = pTrunkBase + trOutward * forkReach + trOrtho * rng.Range(-0.03, 0.03) + Vec3.Up * (forkH - stoolH);

            var cTrunk0 = pTrunkBase + (Vec3.Up * 0.70 + trOutward * 0.40).Normalized() * (forkH * 0.42);
            var cTrunk1 = pFork - (Vec3.Up * 0.85 + trOutward * 0.30).Normalized() * (forkH * 0.38);

            int trunkStations = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
            var trunkPoints = SplitcrownSampleBezier(pTrunkBase, cTrunk0, cTrunk1, pFork, trunkStations);
            var trunkRadii = new double[trunkStations];

            double rBase = juvenile ? rng.Range(0.026, 0.032) : rng.Range(0.036, 0.044);
            double rFork = rBase * 0.65;

            for (int i = 0; i < trunkStations; i++)
            {
                double t = i / (double)(trunkStations - 1);
                trunkRadii[i] = MathD.Lerp(rBase, rFork, Math.Pow(t, 0.85));
            }

            // Trunk collar at base connection to stool
            SplitcrownBranchCollar(m, pTrunkBase, (trunkPoints[1] - trunkPoints[0]).Normalized(), rBase * 1.25, rBase * 1.10, detail);

            // Fork: parent swells smoothly toward the crown (F1); child radii follow r^2.5 conservation
            double nominalChildR = Form.ForkJunction.ChildRadius(rFork, branchesPerTrunk);
            Form.ForkJunction.WidenParent(trunkPoints, trunkRadii, (pFork, nominalChildR));
            SplitcrownStem(m, trunkPoints, trunkRadii, detail, isLowerTrunk: true);

            // Crown collar at trunk fork summit (matched to widened parent end)
            var trunkEndTan = (trunkPoints[trunkStations - 1] - trunkPoints[trunkStations - 2]).Normalized();
            SplitcrownBranchCollar(m, pFork - trunkEndTan * (rFork * 0.30), trunkEndTan, rFork * 1.12, rFork * 1.00, detail);

            // Seed pod attach point beneath the fork
            podAttachPoints.Add((pFork - Vec3.Up * 0.02, -trOutward));

            // ---------------------------------------------------------------------
            // 2. Multi-Tiered Staggered Scaffold Boughs (Varied Heights, NO Flat Table-Top!)
            // ---------------------------------------------------------------------
            for (int b = 0; b < branchesPerTrunk; b++)
            {
                double bAz = trAz + 2.0 * Math.PI * b / branchesPerTrunk + rng.Range(-0.16, 0.16);
                var bOutward = MidRadial(bAz);
                var bOrtho = MidRadial(bAz + Math.PI * 0.5);

                // Varied bough height and reach tiers:
                // b == 0: Low sweeping bough (height ~ 0.54-0.62, reach wide ~ 0.68-0.78)
                // b == 1: Mid-tier bough (height ~ 0.68-0.75, reach ~ 0.58-0.70)
                // b == 2: Upper crown bough (height ~ 0.82-0.92, reach ~ 0.45-0.56)
                // b >= 3: Infill / interior bough (height ~ 0.62-0.72, reach ~ 0.40-0.54)
                double summitY;
                double reach;
                if (b == 0)
                {
                    summitY = juvenile ? rng.Range(0.52, 0.58) : rng.Range(0.55, 0.64);
                    reach = rng.Range(0.66, 0.78);
                }
                else if (b == 1)
                {
                    summitY = juvenile ? rng.Range(0.62, 0.68) : rng.Range(0.68, 0.76);
                    reach = rng.Range(0.58, 0.70);
                }
                else if (b == 2)
                {
                    summitY = juvenile ? rng.Range(0.72, 0.78) : rng.Range(0.82, 0.90);
                    reach = rng.Range(0.48, 0.60);
                }
                else
                {
                    summitY = juvenile ? rng.Range(0.58, 0.65) : rng.Range(0.64, 0.74);
                    reach = rng.Range(0.42, 0.56);
                }

                var pBranchEnd = pFork + bOutward * reach + bOrtho * rng.Range(-0.04, 0.04) + Vec3.Up * (summitY - forkH);
                var cBranch0 = pFork + (bOutward * 0.45 + Vec3.Up * 0.75).Normalized() * (reach * 0.42);
                var cBranch1 = pBranchEnd - (bOutward * 0.85 + Vec3.Up * 0.25).Normalized() * (reach * 0.38);

                int branchStations = detail == 2 ? 5 : (detail == 1 ? 7 : 8);
                var branchPath = SplitcrownSampleBezier(pFork, cBranch0, cBranch1, pBranchEnd, branchStations);
                var branchRadii = new double[branchStations];

                double branchBaseR = nominalChildR * (rng.Range(0.55, 0.68) / 0.615);
                double branchTipR = Math.Max(0.004, branchBaseR * 0.35);

                for (int i = 0; i < branchStations; i++)
                {
                    double st = i / (double)(branchStations - 1);
                    branchRadii[i] = MathD.Lerp(branchBaseR, branchTipR, st);
                }

                var branchTan0 = (cBranch0 - pFork).Normalized();
                SplitcrownStem(m, Form.ForkJunction.SinkStart(branchPath, branchBaseR), branchRadii, detail, isLowerTrunk: false);

                // Branch collar at attachment to fork, ring plane normal to the child's first tangent
                SplitcrownBranchCollar(m, pFork + branchTan0 * (branchBaseR * 0.5), branchTan0,
                    branchBaseR * 1.15, branchBaseR * 1.10, detail);

                // --- Whimsical Silk-Puff Pom-Pom nestled among the tiers ---
                if (detail < 2 && (b % 2 == 1 || b == 2))
                {
                    ulong pomSeed = Rng.Mix(seed, (ulong)(tr * 80000 + b * 800 + 77));
                    SplitcrownSilkPomPom(m, pBranchEnd + Vec3.Up * 0.035, radius: 0.025, pomSeed, detail);
                }

                // Terminal compound fronds at branch tip
                var tipDir = SplitcrownBezierTangent(pFork, cBranch0, cBranch1, pBranchEnd, 1.0);

                for (int tf = 0; tf < termFrondsPerBranch; tf++)
                {
                    double spreadAngle = (tf - (termFrondsPerBranch - 1) * 0.5) * 0.45 + rng.Range(-0.06, 0.06);
                    var frondDir = (tipDir * 0.85 + bOrtho * Math.Sin(spreadAngle) + Vec3.Up * (0.10 - 0.12 * Math.Abs(spreadAngle))).Normalized();
                    var frondNorm = (Vec3.Up * 0.88 + bOutward * 0.40).Normalized();

                    double frondLen = rng.Range(0.18, 0.24);
                    ulong frondSeed = Rng.Mix(seed, (ulong)(tr * 100000 + b * 10000 + tf * 100 + 43));

                    SplitcrownCompoundFrond(m, pBranchEnd, frondDir, frondNorm, frondLen,
                        scale: juvenile ? 0.82 : 1.05, frondSeed, c1, c2, detail);
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

                        double mLen = rng.Range(0.15, 0.20);
                        ulong mSeed = Rng.Mix(seed, (ulong)(tr * 200000 + b * 20000 + mf * 2000 + (sideSign > 0 ? 1 : 2)));

                        SplitcrownCompoundFrond(m, mPos, mFrondDir, mFrondNorm, mLen,
                            scale: juvenile ? 0.78 : 0.95, mSeed, c1, c2, detail);
                    }
                }
            }
        }

        // -------------------------------------------------------------------------
        // 3. Whimsical Signature: Hanging Velvet Indigo Seed Pods
        // -------------------------------------------------------------------------
        int podCount = juvenile ? 4 : (detail == 2 ? 4 : 12);
        for (int p = 0; p < podCount; p++)
        {
            var (attach, dir) = podAttachPoints[p % podAttachPoints.Count];
            double pLen = rng.Range(0.12, 0.17) * (juvenile ? 0.75 : 1.0);
            double pWid = rng.Range(0.026, 0.034);
            ulong pSeed = Rng.Mix(seed, (ulong)(p * 7777 + 91));

            var offset = MidRadial(p * 1.5) * rng.Range(0.01, 0.03);
            SplitcrownSeedPod(m, attach + offset, dir, pLen, pWid, pSeed, detail);
        }
    }
}
