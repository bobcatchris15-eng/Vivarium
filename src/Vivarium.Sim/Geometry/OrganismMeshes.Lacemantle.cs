using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Lacemantle (microcanopy_lacemantle) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A cascading, tiered understory microcanopy tree inspired by the picturesque Japanese Laceleaf Maple
/// (Acer palmatum 'Dissectum') and Pagoda Dogwood (Cornus controversa), forming horizontal
/// weeping shelves of deeply dissected lacy foliage that filter woodland light.
///
/// Visual Architecture:
///   1. Picturesque Low-Forking Silver-Plum Multi-Stem: Sinuous, flowing multi-stemmed or low-forking
///      smooth silver-plum trunk with flared root buttresses and organic branch collar swellings at junctions,
///      dividing into dominant and co-dominant S-curved trunks.
///   2. Graceful Asymmetrical Pagoda Tiers of Weeping Boughs: Staggered scaffold boughs emerging at varied,
///      natural heights and asymmetrical azimuths, arching in organic S-curves with cascading weeping branchlets.
///   3. Continuous Layered Lace-Leaf Mantle: Clothed along the weeping boughs with overlapping, deeply
///      palmate-lobed lace-leaf sprays (5-7 lobes per fan, with rich emerald-jade interior leaves warming to
///      sunlit chartreuse and coppery-rose / rose-gold at outer cascade tips, with blended +Up normals).
///   4. Whimsical Signature:
///      - Delicate creamy-jade 4-bract dogwood star-blossoms nestled along upper bough surfaces.
///      - Pendant racemes of glossy amethyst-violet and magenta porcelain berries (UV2 = (1.28, 0.86))
///        dangling beneath each cascading tier on crimson pedicels.
/// </summary>
public static partial class OrganismMeshes
{
    // Smooth silver-plum bark palette (UV2 = (2, 0))
    private static readonly double[] LaceBarkDeep     = { 0.14, 0.10, 0.12 };   // Deep furrow/crevice
    private static readonly double[] LaceBarkPlum     = { 0.28, 0.20, 0.26 };   // Silver-plum wood body
    private static readonly double[] LaceBarkSilver   = { 0.52, 0.48, 0.52 };   // Weathered silver crest highlight
    private static readonly double[] LaceBarkLichen   = { 0.44, 0.50, 0.38 };   // Pale crustose lichen dusting

    // Cascading lace foliage palette (UV2 = (0, 1))
    private static readonly double[] LaceLeafJade     = { 0.14, 0.38, 0.20 };   // Deep emerald-jade interior
    private static readonly double[] LaceLeafChartreuse= { 0.36, 0.58, 0.22 };  // Sunlit chartreuse mid-canopy
    private static readonly double[] LaceLeafCopper   = { 0.68, 0.32, 0.22 };   // Coppery-rose cascade tip
    private static readonly double[] LaceLeafRoseGold = { 0.78, 0.46, 0.26 };   // Sunlit rose-amber leaf margin
    private static readonly double[] LaceLeafUnder    = { 0.46, 0.60, 0.42 };   // Pale silvery-sage underside

    // Whimsical Star-Bracts & Pendant Amethyst Berries (UV2 = 1.28, 0.86)
    private static readonly double[] LaceBractCream   = { 0.84, 0.92, 0.80 };   // Creamy-jade dogwood star-bract
    private static readonly double[] LaceBractEye     = { 0.56, 0.76, 0.28 };   // Translucent chartreuse floral center
    private static readonly double[] LaceBerryStalk   = { 0.66, 0.14, 0.18 };   // Crimson drooping pedicel
    private static readonly double[] LaceBerryAmethyst= { 0.54, 0.18, 0.68 };   // Glossy amethyst-violet porcelain berry
    private static readonly double[] LaceBerryMagenta = { 0.76, 0.22, 0.58 };   // Radiant magenta berry highlight

    /// <summary>Evaluates a cubic Bézier curve at parameter t in [0, 1].</summary>
    private static Vec3 LacemantleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    /// <summary>Evaluates a cubic Bézier tangent vector at parameter t in [0, 1].</summary>
    private static Vec3 LacemantleBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>Discretizes a cubic Bézier curve into smooth path stations.</summary>
    private static Vec3[] LacemantleSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts[i] = LacemantleBezier(p0, c0, c1, p1, t);
        }
        return pts;
    }

    /// <summary>
    /// Builds an organic branch collar swelling at a branch junction,
    /// creating a natural swollen wood collar blending boughs into trunks.
    /// </summary>
    private static void LacemantleBranchCollar(
        MeshData m,
        Vec3 center,
        double radius,
        double height,
        int detail)
    {
        int collarStart = m.VertexCount;
        int rings = detail == 2 ? 3 : 5;
        int sectors = detail == 2 ? 8 : 10;

        for (int r = 0; r <= rings; r++)
        {
            double tr = r / (double)rings;
            double y = (tr - 0.5) * height;
            double shape = Math.Sin(Math.PI * tr);
            double rad = radius * (0.85 + 0.40 * shape);

            for (int s = 0; s <= sectors; s++)
            {
                double u = s / (double)sectors;
                double th = 2.0 * Math.PI * u;
                double flute = 1.0 + 0.08 * Math.Cos(5.0 * th);
                var pos = center + new Vec3(Math.Cos(th) * rad * flute, y, Math.Sin(th) * rad * flute);
                var norm = new Vec3(Math.Cos(th), (tr - 0.5) * 0.6, Math.Sin(th)).Normalized();
                var col = Primitives.Mix(LaceBarkPlum, LaceBarkSilver, 0.35 + 0.30 * tr);
                m.AddVertex(pos, norm, col, 1.0, y * 3.5, u, 2.0, 0.0);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < sectors; s++)
            {
                int a = collarStart + r * (sectors + 1) + s;
                int b = a + sectors + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }
        m.Structural.Add((collarStart, m.VertexCount - collarStart, sectors, radius));
    }

    /// <summary>
    /// Builds a lofted smooth silver-plum woody stem with subtle fluting and smooth normals.
    /// </summary>
    private static void LacemantleStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        bool rootFlare = false,
        double flareAzimuth = 0.0)
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

                double flute = Math.Cos(5.0 * th);
                double rMod = 1.0 + 0.08 * flute;

                if (rootFlare && t < 0.35)
                {
                    double flareT = Math.Pow(1.0 - t / 0.35, 2.0);
                    double flareLobe = Math.Max(0.0, Math.Cos(th - flareAzimuth)) * 0.65;
                    rMod += (0.40 + flareLobe) * flareT;
                }

                double r = Math.Max(0.0016, baseR * rMod);
                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                var pos = center + radialDir * r;
                ringPositions[i, s] = pos;

                var baseCol = Primitives.Mix(LaceBarkPlum, LaceBarkSilver, 0.40 * t);
                if (flute < -0.3)
                {
                    baseCol = Primitives.Mix(baseCol, LaceBarkDeep, 0.50);
                }
                else if (flute > 0.3)
                {
                    baseCol = Primitives.Mix(baseCol, LaceBarkSilver, 0.45);
                }

                if (radialDir.Y > 0.40)
                {
                    baseCol = Primitives.Mix(baseCol, LaceBarkLichen, 0.30);
                }

                ringColors[i, s] = baseCol;
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
                m.AddVertex(ringPositions[i, s], ringNormals[i, s], ringColors[i, s], 1.0, points[i].Y * 3.5, u, 2.0, 0.0);
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
    /// Builds a single deeply serrated, cambered lace lobe (1/5th to 1/7th of a palmate leaf fan)
    /// with blended +Up normal shading.
    /// </summary>
    private static void LacemantleLaceLobe(
        MeshData m,
        Vec3 basePos,
        Vec3 leafDir,
        Vec3 leafNormal,
        double length,
        double width,
        ulong seed,
        double[] colBase,
        double[] colTip,
        double[] colMargin,
        int detail)
    {
        if (length <= 1e-4 || width <= 1e-5) return;

        // Controlled tessellation: detail 0 is 5 rows x 2 cols (4 across = 16 quads = 32 tris).
        // detail 1 is 3 rows x 1 col (2 across = 4 quads = 8 tris).
        // detail 2 is 2 rows x 1 col (2 across = 2 quads = 4 tris).
        int rows = detail == 0 ? 5 : (detail == 1 ? 3 : 2);
        int cols = detail == 0 ? 2 : 1;
        if (m.Lod is { } lod) rows = Math.Max(2, lod.Segments(rows, detail == 0 ? 4 : 2));

        var side = leafNormal.Cross(leafDir);
        if (side.LengthSq < 1e-6) side = leafDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(leafDir).Normalized();

        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.TriangleCount * 3;

        double phase = (seed % 997) / 997.0 * Math.PI * 2.0;

        // Dissected palmate lobe width profile: narrow base, wide middle with serrated shoulders, tapered tip
        double WidthProfile(double t)
        {
            if (t < 0.15) return 0.20 + 0.80 * (t / 0.15);
            if (t <= 0.60) return 1.00 + 0.28 * Math.Sin(Math.PI * (t - 0.15) / 0.45);
            double u = (t - 0.60) / 0.40;
            return Math.Max(0.04, 1.28 * Math.Pow(Math.Max(0.0, 1.0 - u), 1.15));
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.05 * Math.Sin(Math.PI * t) - 0.08 * Math.Pow(t, 1.5));
            double drip = -length * 0.065 * Math.Pow(Math.Max(0.0, (t - 0.75) / 0.25), 2.0);
            double sway = length * 0.012 * Math.Sin(Math.PI * 1.35 * t + phase);
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
                double ripple = 0.018 * width * Math.Pow(absX, 1.6) * Math.Sin(t * Math.PI * 3.2 + phase);
                double tipDroop = -0.025 * width * Math.Pow(absX, 1.4) * Math.Pow(t, 2.8);

                topPositions[r, c] = pSpine + sSpine * (w * x) + nSpine * (gutter + ripple + tipDroop);

                var bodyCol = Primitives.Mix(colBase, colTip, t);
                if (absX > 0.50 || t > 0.65)
                {
                    double marginFrac = Math.Max((absX - 0.50) / 0.50, (t - 0.65) / 0.35);
                    bodyCol = Primitives.Mix(bodyCol, colMargin, marginFrac * 0.60);
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

                // Blend heavily with +Up (65%) for luminous layered pagoda light
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
    /// Builds a full 5-7 lobed palmate lace-leaf fan spray (Japanese Maple style) at a branch node.
    /// </summary>
    private static void LacemantlePalmateFan(
        MeshData m,
        Vec3 nodePos,
        Vec3 fanDir,
        Vec3 fanNorm,
        double scale,
        ulong seed,
        double cascadeFrac, // 0 = interior, 1 = tip cascade
        int detail)
    {
        var side = fanNorm.Cross(fanDir);
        if (side.LengthSq < 1e-6) side = fanDir.Cross(Vec3.Up);
        side = side.Normalized();

        // Color gradation along cascade: emerald inside to coppery-rose & rose-gold at tips
        var colBase = Primitives.Mix(LaceLeafJade, LaceLeafChartreuse, cascadeFrac * 0.60);
        var colTip = Primitives.Mix(LaceLeafChartreuse, LaceLeafCopper, Math.Pow(cascadeFrac, 0.70));
        var colMargin = Primitives.Mix(LaceLeafRoseGold, LaceLeafCopper, cascadeFrac);

        int numLobes = detail == 2 ? 3 : (detail == 1 ? 5 : 7);
        double lobeSpread = 1.45; // radians total spread

        for (int l = 0; l < numLobes; l++)
        {
            double lobeT = (l - (numLobes - 1) * 0.5) / (double)Math.Max(1, numLobes - 1); // -0.5 .. +0.5
            double angle = lobeT * lobeSpread;

            var lDir = (fanDir * Math.Cos(angle) + side * Math.Sin(angle) - Vec3.Up * (0.04 + 0.08 * Math.Abs(lobeT))).Normalized();
            var lNorm = (Vec3.Up * 0.85 + fanDir * 0.25 + side * (lobeT * 0.35)).Normalized();

            // Center lobe is longest, lateral lobes taper down
            double lenFactor = 1.0 - 0.28 * Math.Abs(lobeT);
            double lLen = (0.095 + 0.020 * Rng.HashUnit(seed, (ulong)(l * 13 + 5), 0)) * lenFactor * scale;
            double lWid = lLen * 0.34;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 47 + 19));

            LacemantleLaceLobe(m, nodePos, lDir, lNorm, lLen, lWid, lSeed,
                colBase, colTip, colMargin, detail);
        }
    }

    /// <summary>
    /// Whimsical Feature 1: Creamy-jade 4-bract dogwood star-blossoms on upper bough surface.
    /// </summary>
    private static void LacemantleDogwoodBlossom(
        MeshData m,
        Vec3 centerPos,
        Vec3 boughUp,
        double size,
        ulong seed,
        int detail)
    {
        int blossomStart = m.VertexCount;
        var side1 = boughUp.Cross(Vec3.Up);
        if (side1.LengthSq < 1e-6) side1 = boughUp.Cross(new Vec3(1, 0, 0));
        side1 = side1.Normalized();
        var side2 = side1.Cross(boughUp).Normalized();

        // 4 obovate bracts radiating at 90 degrees with notched tips
        for (int b = 0; b < 4; b++)
        {
            double th = b * (Math.PI * 0.5) + 0.20;
            var bDir = (side1 * Math.Cos(th) + side2 * Math.Sin(th) + boughUp * 0.15).Normalized();
            var bSide = bDir.Cross(boughUp).Normalized();

            double bLen = size * 0.55;
            double bWid = bLen * 0.58;

            int bStart = m.VertexCount;
            for (int r = 0; r < 3; r++)
            {
                double t = r / 2.0;
                double w = bWid * (t < 0.6 ? t / 0.6 : 1.0 - 0.3 * (t - 0.6) / 0.4);
                var pMid = centerPos + bDir * (bLen * t) + boughUp * (0.004 * Math.Sin(Math.PI * t));

                m.AddVertex(pMid - bSide * (w * 0.5), boughUp, LaceBractCream, 1.0, t, 0.0, 0.0, 1.0);
                m.AddVertex(pMid, boughUp, LaceBractCream, 1.0, t, 0.5, 0.0, 1.0);
                m.AddVertex(pMid + bSide * (w * 0.5), boughUp, LaceBractCream, 1.0, t, 1.0, 0.0, 1.0);
            }

            for (int r = 0; r < 2; r++)
            {
                int a0 = bStart + r * 3;
                int a1 = a0 + 1;
                int a2 = a0 + 2;
                int b0 = a0 + 3;
                int b1 = b0 + 1;
                int b2 = b0 + 2;

                m.AddTriangle(a0, b0, a1);
                m.AddTriangle(a1, b0, b1);
                m.AddTriangle(a1, b1, a2);
                m.AddTriangle(a2, b1, b2);
            }
        }

        // Central chartreuse button
        Primitives.Ellipsoid(m, centerPos + boughUp * 0.003, new Vec3(size * 0.12, size * 0.10, size * 0.12),
            2, 4, (r, s) => (LaceBractEye, 1.0, r, s, 0.0, 1.0));

        if (m.VertexCount > blossomStart)
        {
            m.EssentialSpans.Add((blossomStart, m.VertexCount - blossomStart));
        }
    }

    /// <summary>
    /// Whimsical Feature 2: Hanging raceme of glossy amethyst-violet & magenta porcelain berries (UV2 = (1.28, 0.86)).
    /// </summary>
    private static void LacemantleAmethystBerryRaceme(
        MeshData m,
        Vec3 attachPos,
        Vec3 hangDir,
        double length,
        ulong seed,
        int detail)
    {
        int racemeStart = m.VertexCount;
        var rng = Rng.Keyed(seed, "lacemantle.amethyst", 0);

        // Drooping crimson pedicel
        int stations = detail == 2 ? 3 : 5;
        var pEnd = attachPos + (hangDir * 0.50 - Vec3.Up * 0.85).Normalized() * length;
        var c0 = attachPos - Vec3.Up * (length * 0.35);
        var c1 = pEnd - (hangDir * 0.25 - Vec3.Up * 0.75).Normalized() * (length * 0.30);

        var path = LacemantleSampleBezier(attachPos, c0, c1, pEnd, stations);
        var radii = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            radii[i] = MathD.Lerp(0.0028, 0.0010, i / (double)(stations - 1));
        }

        Primitives.Tube(m, path, radii, detail == 2 ? 3 : 4,
            (i, v) => (LaceBerryStalk, 1.0, i / (double)(stations - 1), v, 2.0, 0.0));

        // Clustered amethyst porcelain berries
        int numBerries = detail == 2 ? 3 : (detail == 1 ? 5 : 6);
        for (int b = 0; b < numBerries; b++)
        {
            double t = 0.25 + 0.72 * (b / (double)Math.Max(1, numBerries - 1));
            var nodePos = LacemantleBezier(attachPos, c0, c1, pEnd, t);

            double az = b * 2.4 + rng.Range(-0.2, 0.2);
            var sideDir = MidRadial(az);
            double reach = rng.Range(0.012, 0.022);
            var berryPos = nodePos + sideDir * reach - Vec3.Up * rng.Range(0.004, 0.012);

            // Tiny pedicel twig
            Primitives.Tube(m, new[] { nodePos, berryPos }, new[] { 0.0010, 0.0006 }, detail == 2 ? 3 : 4,
                (i, v) => (LaceBerryStalk, 1.0, i, v, 2.0, 0.0));

            // Porcelain berry bead
            double bR = rng.Range(0.008, 0.013);
            var bCol = Primitives.Mix(LaceBerryAmethyst, LaceBerryMagenta, rng.Range(0.15, 0.65));

            int bRings = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
            int bSides = detail == 2 ? 4 : (detail == 1 ? 5 : 6);
            Primitives.Ellipsoid(m, berryPos, new Vec3(bR, bR * 1.1, bR), bRings, bSides,
                (r, s) => (bCol, 1.0, r, s, 1.28, 0.86));
        }

        if (m.VertexCount > racemeStart)
        {
            m.EssentialSpans.Add((racemeStart, m.VertexCount - racemeStart));
        }
    }

    /// <summary>
    /// Builds a single weeping pagoda bough with organic S-curve and cascading branchlets.
    /// </summary>
    private static void LacemantleWeepingBough(
        MeshData m,
        Vec3 boughOrigin,
        Vec3 boughOutward,
        double boughReach,
        double boughDroop,
        double boughBaseR,
        ulong boughSeed,
        Rng rng,
        bool juvenile,
        int detail)
    {
        var boughOrtho = MidRadial(Math.Atan2(boughOutward.Z, boughOutward.X) + Math.PI * 0.5);

        // Organic S-curve: rises out of stem, sweeps horizontally, dips into weeping cascade at rim
        var pEnd = boughOrigin + boughOutward * boughReach + boughOrtho * rng.Range(-0.04, 0.04) - Vec3.Up * boughDroop;
        var c0 = boughOrigin + (boughOutward * 0.45 + Vec3.Up * 0.60).Normalized() * (boughReach * 0.40);
        var c1 = pEnd - (boughOutward * 0.85 - Vec3.Up * 0.35).Normalized() * (boughReach * 0.38);

        int branchStations = detail == 2 ? 5 : (detail == 1 ? 7 : 9);
        var branchPath = LacemantleSampleBezier(boughOrigin, c0, c1, pEnd, branchStations);
        var branchRadii = new double[branchStations];
        double branchTipR = Math.Max(0.003, boughBaseR * 0.35);

        for (int i = 0; i < branchStations; i++)
        {
            double st = i / (double)(branchStations - 1);
            branchRadii[i] = MathD.Lerp(boughBaseR, branchTipR, st);
        }
        LacemantleStem(m, branchPath, branchRadii, detail);

        // Dogwood star blossom on upper bough surface
        if (detail < 2)
        {
            var blossomPos = LacemantleBezier(boughOrigin, c0, c1, pEnd, 0.38);
            ulong bSeed = Rng.Mix(boughSeed, 777);
            LacemantleDogwoodBlossom(m, blossomPos + Vec3.Up * (boughBaseR * 0.8), Vec3.Up,
                size: 0.035, bSeed, detail);
        }

        // Amethyst porcelain berry raceme dangling beneath bough
        var racemePos = LacemantleBezier(boughOrigin, c0, c1, pEnd, 0.52);
        ulong rSeed = Rng.Mix(boughSeed, 999);
        LacemantleAmethystBerryRaceme(m, racemePos - Vec3.Up * (boughBaseR * 0.6), boughOutward,
            length: rng.Range(0.07, 0.11), rSeed, detail);

        // Terminal weeping palmate fan at branch tip
        var tipDir = LacemantleBezierTangent(boughOrigin, c0, c1, pEnd, 1.0);
        ulong termFanSeed = Rng.Mix(boughSeed, 1);
        LacemantlePalmateFan(m, pEnd, tipDir, Vec3.Up, scale: juvenile ? 0.90 : 1.15,
            termFanSeed, cascadeFrac: 1.0, detail);

        // 3-4 lateral weeping branchlets along outer 70% of the bough
        int lateralNodes = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
        for (int ln = 0; ln < lateralNodes; ln++)
        {
            double sFrac = MathD.Lerp(0.38, 0.88, ln / (double)Math.Max(1, lateralNodes - 1));
            var sPos = LacemantleBezier(boughOrigin, c0, c1, pEnd, sFrac);
            var sTan = LacemantleBezierTangent(boughOrigin, c0, c1, pEnd, sFrac);

            // Infill fan right on the bough
            ulong midFanSeed = Rng.Mix(boughSeed, (ulong)(ln * 200 + 5));
            LacemantlePalmateFan(m, sPos, sTan, Vec3.Up, scale: juvenile ? 0.80 : 1.00,
                midFanSeed, cascadeFrac: sFrac, detail);

            // Bilateral weeping branchlets spreading in the pagoda tier
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                double bAz = Math.Atan2(boughOutward.Z, boughOutward.X);
                double twigAz = bAz + sideSign * rng.Range(0.55, 0.90);
                var twigOut = MidRadial(twigAz);
                double twigReach = boughReach * rng.Range(0.24, 0.38);
                var twigEnd = sPos + twigOut * twigReach - Vec3.Up * rng.Range(0.020, 0.050);

                var cTwig0 = sPos + (sTan * 0.40 + twigOut * 0.70).Normalized() * (twigReach * 0.40);
                var cTwig1 = twigEnd - (twigOut * 0.90 - Vec3.Up * 0.25).Normalized() * (twigReach * 0.35);

                int twigStations = detail == 2 ? 4 : 5;
                var twigPath = LacemantleSampleBezier(sPos, cTwig0, cTwig1, twigEnd, twigStations);
                var twigRadii = new[] { branchTipR * 1.05, branchTipR * 0.80, branchTipR * 0.60, branchTipR * 0.45, 0.002 };
                LacemantleStem(m, twigPath, twigRadii, detail);

                var twigDir = LacemantleBezierTangent(sPos, cTwig0, cTwig1, twigEnd, 1.0);
                ulong twigFanSeed = Rng.Mix(boughSeed, (ulong)(ln * 300 + (sideSign > 0 ? 1 : 2)));
                LacemantlePalmateFan(m, twigEnd, twigDir, Vec3.Up, scale: juvenile ? 0.85 : 1.05,
                    twigFanSeed, cascadeFrac: Math.Min(1.0, sFrac + 0.20), detail);

                // Midpoint sub-fan on lateral branchlet
                if (detail < 2)
                {
                    var twigMid = LacemantleBezier(sPos, cTwig0, cTwig1, twigEnd, 0.50);
                    var twigMidTan = LacemantleBezierTangent(sPos, cTwig0, cTwig1, twigEnd, 0.50);
                    ulong midTwigSeed = Rng.Mix(boughSeed, (ulong)(ln * 350 + (sideSign > 0 ? 3 : 4)));
                    LacemantlePalmateFan(m, twigMid, twigMidTan, Vec3.Up, scale: juvenile ? 0.75 : 0.90,
                        midTwigSeed, cascadeFrac: sFrac, detail);
                }
            }
        }
    }

    /// <summary>
    /// Lacemantle procedural generator.
    /// Builds a picturesque Japanese Laceleaf Maple (Acer palmatum 'Dissectum') with sinuous,
    /// low-forking silver-plum multi-stem, organic branch collars, asymmetrical pagoda tiers
    /// with cascading S-curves, continuous weeping lace-leaf mantle, and pendant amethyst berries.
    /// </summary>
    private static void Lacemantle(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        int detail = m.FloraDetailLevel ?? 0;

        // -------------------------------------------------------------------------
        // 1. Picturesque Low-Forking Silver-Plum Multi-Stem & Flared Buttress Base
        // -------------------------------------------------------------------------
        double apexY = juvenile ? rng.Range(0.70, 0.74) : rng.Range(0.76, 0.84);
        var pFoot = Vec3.Zero;

        double swayAz = rng.Range(0, Math.PI * 2);
        var swayDir = MidRadial(swayAz);

        double rBase = juvenile ? rng.Range(0.034, 0.040) : rng.Range(0.044, 0.054);

        // Low fork occurs at y ~ 0.12 - 0.16
        double lowForkH = juvenile ? 0.12 : rng.Range(0.14, 0.18);
        var pLowFork = pFoot + swayDir * rng.Range(0.02, 0.04) + Vec3.Up * lowForkH;

        // Short stout basal trunk
        int baseStations = detail == 2 ? 4 : 5;
        var basePoints = new Vec3[baseStations];
        var baseRadii = new double[baseStations];
        for (int i = 0; i < baseStations; i++)
        {
            double t = i / (double)(baseStations - 1);
            basePoints[i] = Vec3.Lerp(pFoot, pLowFork, t);
            baseRadii[i] = MathD.Lerp(rBase, rBase * 0.88, t);
        }
        LacemantleStem(m, basePoints, baseRadii, detail, rootFlare: true, flareAzimuth: swayAz);

        // Organic branch collar swelling uniting the low fork
        LacemantleBranchCollar(m, pLowFork, rBase * 1.25, lowForkH * 0.90, detail);

        // -------------------------------------------------------------------------
        // 2. Co-Dominant Sinuous Trunks (Stem 1: Dominant; Stem 2: Co-Dominant)
        // -------------------------------------------------------------------------
        // Stem 1: Sweeps gracefully to the main summit (apexY)
        double stem1Az = swayAz + rng.Range(0.20, 0.45);
        var stem1Dir = MidRadial(stem1Az);
        var pApex1 = pLowFork + stem1Dir * rng.Range(0.08, 0.14) + Vec3.Up * (apexY - lowForkH);

        var cStem1_0 = pLowFork + (stem1Dir * 0.40 + Vec3.Up * 0.75).Normalized() * ((apexY - lowForkH) * 0.45);
        var cStem1_1 = pApex1 - (stem1Dir * 0.50 + Vec3.Up * 0.65).Normalized() * ((apexY - lowForkH) * 0.40);

        int stem1Stations = detail == 2 ? 7 : (detail == 1 ? 9 : 12);
        var stem1Path = LacemantleSampleBezier(pLowFork, cStem1_0, cStem1_1, pApex1, stem1Stations);
        var stem1Radii = new double[stem1Stations];
        double stem1BaseR = rBase * 0.68;
        double stem1TipR = Math.Max(0.005, stem1BaseR * 0.32);
        for (int i = 0; i < stem1Stations; i++)
        {
            double t = i / (double)(stem1Stations - 1);
            stem1Radii[i] = MathD.Lerp(stem1BaseR, stem1TipR, Math.Pow(t, 0.85));
        }
        LacemantleStem(m, stem1Path, stem1Radii, detail);

        // Stem 2: Spreads lower and wider to create the picturesque asymmetrical Japanese maple form
        double stem2Az = swayAz + Math.PI + rng.Range(-0.35, 0.35);
        var stem2Dir = MidRadial(stem2Az);
        double apex2Y = apexY * rng.Range(0.68, 0.76);
        var pApex2 = pLowFork + stem2Dir * rng.Range(0.22, 0.32) + Vec3.Up * (apex2Y - lowForkH);

        var cStem2_0 = pLowFork + (stem2Dir * 0.65 + Vec3.Up * 0.60).Normalized() * ((apex2Y - lowForkH) * 0.45);
        var cStem2_1 = pApex2 - (stem2Dir * 0.85 + Vec3.Up * 0.25).Normalized() * ((apex2Y - lowForkH) * 0.40);

        int stem2Stations = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
        var stem2Path = LacemantleSampleBezier(pLowFork, cStem2_0, cStem2_1, pApex2, stem2Stations);
        var stem2Radii = new double[stem2Stations];
        double stem2BaseR = rBase * 0.56;
        double stem2TipR = Math.Max(0.004, stem2BaseR * 0.32);
        for (int i = 0; i < stem2Stations; i++)
        {
            double t = i / (double)(stem2Stations - 1);
            stem2Radii[i] = MathD.Lerp(stem2BaseR, stem2TipR, Math.Pow(t, 0.85));
        }
        LacemantleStem(m, stem2Path, stem2Radii, detail);

        // -------------------------------------------------------------------------
        // 3. Asymmetrical Pagoda Tiers of Sinuous Weeping Boughs
        // -------------------------------------------------------------------------
        // Boughs on Dominant Stem 1 (4-5 staggered boughs)
        int boughsStem1 = juvenile ? 3 : (detail == 2 ? 3 : 5);
        for (int bi = 0; bi < boughsStem1; bi++)
        {
            double tStem = MathD.Lerp(0.22, 0.88, bi / (double)Math.Max(1, boughsStem1 - 1));
            var bOrigin = LacemantleBezier(pLowFork, cStem1_0, cStem1_1, pApex1, tStem);

            // Asymmetrical spiral azimuth
            double bAz = stem1Az + (bi * 1.85) + rng.Range(-0.15, 0.15);
            var bOutward = MidRadial(bAz);

            double bReach = (juvenile ? 0.50 : 0.72) * MathD.Lerp(1.05, 0.65, Math.Pow(tStem, 0.75)) * rng.Range(0.92, 1.08);
            double bDroop = rng.Range(0.03, 0.08);
            double bBaseR = Math.Max(0.006, stem1BaseR * 0.45 * (1.0 - 0.25 * tStem));

            // Branch collar at connection
            LacemantleBranchCollar(m, bOrigin, bBaseR * 1.30, bBaseR * 1.20, detail);

            ulong bSeed = Rng.Mix(seed, (ulong)(bi * 10000 + 17));
            LacemantleWeepingBough(m, bOrigin, bOutward, bReach, bDroop, bBaseR, bSeed, rng, juvenile, detail);
        }

        // Boughs on Co-Dominant Stem 2 (3-4 staggered boughs)
        int boughsStem2 = juvenile ? 2 : (detail == 2 ? 2 : 4);
        for (int bi = 0; bi < boughsStem2; bi++)
        {
            double tStem = MathD.Lerp(0.28, 0.88, bi / (double)Math.Max(1, boughsStem2 - 1));
            var bOrigin = LacemantleBezier(pLowFork, cStem2_0, cStem2_1, pApex2, tStem);

            double bAz = stem2Az + (bi * 1.70) + rng.Range(-0.15, 0.15);
            var bOutward = MidRadial(bAz);

            double bReach = (juvenile ? 0.44 : 0.64) * MathD.Lerp(1.00, 0.62, Math.Pow(tStem, 0.75)) * rng.Range(0.90, 1.10);
            double bDroop = rng.Range(0.025, 0.07);
            double bBaseR = Math.Max(0.005, stem2BaseR * 0.45 * (1.0 - 0.25 * tStem));

            LacemantleBranchCollar(m, bOrigin, bBaseR * 1.30, bBaseR * 1.20, detail);

            ulong bSeed = Rng.Mix(seed, (ulong)(bi * 20000 + 43));
            LacemantleWeepingBough(m, bOrigin, bOutward, bReach, bDroop, bBaseR, bSeed, rng, juvenile, detail);
        }

        // -------------------------------------------------------------------------
        // 4. Terminal Apex Crowns (Finishing Both Trunks)
        // -------------------------------------------------------------------------
        int crownStart = m.VertexCount;
        int crownTwigs = detail == 2 ? 3 : 4;

        // Apex 1 crown fountain
        for (int ct = 0; ct < crownTwigs; ct++)
        {
            double twigAz = stem1Az + ct * (2.0 * Math.PI / crownTwigs) + rng.Range(-0.15, 0.15);
            var twigOut = MidRadial(twigAz);
            double twigReach = (juvenile ? 0.16 : 0.24) * rng.Range(0.90, 1.10);
            var twigEnd = pApex1 + twigOut * twigReach - Vec3.Up * rng.Range(0.010, 0.025);

            var cCrown0 = pApex1 + (twigOut * 0.55 + Vec3.Up * 0.35).Normalized() * (twigReach * 0.40);
            var cCrown1 = twigEnd - (twigOut * 0.90 - Vec3.Up * 0.15).Normalized() * (twigReach * 0.35);

            var crownPath = LacemantleSampleBezier(pApex1, cCrown0, cCrown1, twigEnd, 5);
            var crownRadii = new[] { stem1TipR * 0.90, stem1TipR * 0.70, stem1TipR * 0.50, stem1TipR * 0.35, 0.002 };
            LacemantleStem(m, crownPath, crownRadii, detail);

            var crownTipDir = LacemantleBezierTangent(pApex1, cCrown0, cCrown1, twigEnd, 1.0);
            ulong crownFanSeed = Rng.Mix(seed, (ulong)(ct * 8191 + 555));
            LacemantlePalmateFan(m, twigEnd, crownTipDir, Vec3.Up, scale: juvenile ? 0.85 : 1.05,
                crownFanSeed, cascadeFrac: 0.85, detail);
        }

        // Apex 2 crown fountain
        for (int ct = 0; ct < (detail == 2 ? 2 : 3); ct++)
        {
            double twigAz = stem2Az + ct * (2.0 * Math.PI / 3) + rng.Range(-0.15, 0.15);
            var twigOut = MidRadial(twigAz);
            double twigReach = (juvenile ? 0.14 : 0.20) * rng.Range(0.90, 1.10);
            var twigEnd = pApex2 + twigOut * twigReach - Vec3.Up * rng.Range(0.010, 0.025);

            var cCrown0 = pApex2 + (twigOut * 0.55 + Vec3.Up * 0.35).Normalized() * (twigReach * 0.40);
            var cCrown1 = twigEnd - (twigOut * 0.90 - Vec3.Up * 0.15).Normalized() * (twigReach * 0.35);

            var crownPath = LacemantleSampleBezier(pApex2, cCrown0, cCrown1, twigEnd, 5);
            var crownRadii = new[] { stem2TipR * 0.90, stem2TipR * 0.70, stem2TipR * 0.50, stem2TipR * 0.35, 0.002 };
            LacemantleStem(m, crownPath, crownRadii, detail);

            var crownTipDir = LacemantleBezierTangent(pApex2, cCrown0, cCrown1, twigEnd, 1.0);
            ulong crownFanSeed = Rng.Mix(seed, (ulong)(ct * 9999 + 888));
            LacemantlePalmateFan(m, twigEnd, crownTipDir, Vec3.Up, scale: juvenile ? 0.80 : 0.95,
                crownFanSeed, cascadeFrac: 0.85, detail);
        }

        if (m.VertexCount > crownStart)
        {
            m.EssentialSpans.Add((crownStart, m.VertexCount - crownStart));
        }
    }
}
