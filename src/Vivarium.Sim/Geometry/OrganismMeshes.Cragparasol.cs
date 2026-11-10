using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Cragparasol (microcanopy_cragparasol) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A rugged, windswept dry-soil/crag microcanopy inspired by the ancient Socotra Dragon's
/// Blood Tree (Dracaena cinnabari), African umbrella acacias, and windswept alpine crag-trees.
///
/// Visual Architecture:
///   1. Muscular Fluted Basal Anchor & Root Buttresses: Prominent flared rocky-grip root
///      buttresses splaying at ground level, rising into a stout, wind-curved fluted trunk with
///      dragon's blood resin seams glowing in the bark crevices (UV2 = (2, 0)).
///   2. Organic Swollen Branch Collar: At the trunk crown, a muscular collar swelling transitions
///      seamlessly into the branching scaffold, eliminating sharp V-notches.
///   3. 3-Order Dichotomous Umbrella Vault: The trunk divides at the collar into 4 spreading scaffold limbs,
///      which fork dichotomously into 8 secondary boughs and 20-24 ascending candelabra tips arranged in a
///      broad, cohesive, continuous convex horizontal umbrella dome canopy (width ~ 1.4x-1.6x height).
///   4. Continuous Umbrella Dome: The overlapping rosettes (radius ~0.26-0.34) blanket the central, mid,
///      and outer zones of the dome, merging into a unified, lush umbrella dome from above and below
///      with blended +Up normals.
///   5. Whimsical Signature: Hanging beneath the umbrella rim, 8-12 branching coral-red panicles
///      bear clusters of glowing vermilion-amber dragon berries (UV2 = (1.35, 0.88)).
/// </summary>
public static partial class OrganismMeshes
{
    // Weathered bark palette with dragon's-blood resin seams
    private static readonly double[] CragBarkFurrow = { 0.16, 0.12, 0.09 };   // Deep shaded crevice
    private static readonly double[] CragBarkResin  = { 0.70, 0.14, 0.08 };   // Dragon's blood resin seam
    private static readonly double[] CragBarkUnder  = { 0.36, 0.24, 0.16 };   // Warm cinnamon living underwood
    private static readonly double[] CragBarkAsh    = { 0.52, 0.49, 0.42 };   // Weathered silver-grey outer bark
    private static readonly double[] CragBarkCrest  = { 0.64, 0.60, 0.52 };   // Sun-bleached wood highlight
    private static readonly double[] CragBarkLichen = { 0.44, 0.48, 0.32 };   // Epilithic sage-lichen dusting

    // Leathery foliage palette (blue-olive glaucous core to sunlit gold-ochre tip & terracotta margin)
    private static readonly double[] CragLeafBlueOlive = { 0.18, 0.32, 0.22 }; // Leathery glaucous blue-olive core
    private static readonly double[] CragLeafGoldOchre = { 0.48, 0.52, 0.24 }; // Sunlit gold-ochre upper blade
    private static readonly double[] CragLeafBlush     = { 0.62, 0.44, 0.22 }; // Terracotta sun-baked margin blush
    private static readonly double[] CragLeafKeel      = { 0.48, 0.56, 0.36 }; // Pale reinforced dorsal midrib

    // Whimsical Dragon-Ruby Berry palette (UV2 = 1.35, 0.88)
    private static readonly double[] CragPanicleStalk  = { 0.60, 0.18, 0.14 }; // Branching coral-red pedicel
    private static readonly double[] CragBerryRuby     = { 0.96, 0.26, 0.12 }; // Glowing vermilion-ruby dragon berry
    private static readonly double[] CragBerryAmber    = { 1.00, 0.68, 0.16 }; // Luminous amber highlights

    /// <summary>Evaluates a cubic Bézier curve at parameter t in [0, 1].</summary>
    private static Vec3 CragparasolBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    /// <summary>Evaluates a cubic Bézier tangent vector at parameter t in [0, 1].</summary>
    private static Vec3 CragparasolBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>Discretizes a cubic Bézier curve into smooth path stations.</summary>
    private static Vec3[] CragparasolSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts[i] = CragparasolBezier(p0, c0, c1, p1, t);
        }
        return pts;
    }

    /// <summary>
    /// Builds a muscular, swollen branch collar node at the trunk summit where limbs diverge,
    /// eliminating sharp V-notches with an organic, flared fluted junction.
    /// </summary>
    private static void CragparasolBranchCollar(
        MeshData m,
        Vec3 center,
        double radius,
        double height,
        int detail)
    {
        int collarStart = m.VertexCount;
        int rings = detail == 2 ? 4 : (detail == 1 ? 5 : 7);
        int sectors = detail == 2 ? 8 : (detail == 1 ? 10 : 12);

        for (int r = 0; r <= rings; r++)
        {
            double tr = r / (double)rings;
            double y = (tr - 0.5) * height;
            double shape = Math.Sin(Math.PI * tr);
            double rad = radius * (0.88 + 0.42 * shape);

            for (int s = 0; s <= sectors; s++)
            {
                double u = s / (double)sectors;
                double th = 2.0 * Math.PI * u;
                double flute = 1.0 + 0.12 * Math.Cos(5.0 * th);
                var pos = center + new Vec3(Math.Cos(th) * rad * flute, y, Math.Sin(th) * rad * flute);
                var norm = new Vec3(Math.Cos(th), (tr - 0.5) * 0.7, Math.Sin(th)).Normalized();
                var col = Primitives.Mix(CragBarkUnder, CragBarkAsh, tr);
                m.AddVertex(pos, norm, col, 1.0, y * 3.0, u, 2.0, 0.0);
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
    /// Builds a lofted fluted stem with organic buttressing, resin seams, and smooth normals.
    /// </summary>
    private static void CragparasolFlutedStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        double twistTotal = 0.85,
        int ridges = 5,
        bool rootButtress = false,
        double[]? buttressAngles = null)
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

            var transportedN = (prevN - curT * curT.Dot(prevN));
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

            double twist = twistTotal * t;

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2.0 * Math.PI * u;

                double harmonic1 = Math.Cos(ridges * th + twist);
                double harmonic2 = Math.Cos(2 * ridges * th + 2 * twist);
                double fluteMod = 1.0 + 0.14 * harmonic1 + 0.06 * harmonic2;

                if (rootButtress && t < 0.35)
                {
                    double flareDecay = Math.Pow(1.0 - t / 0.35, 2.0);
                    double buttressSum = 0.0;
                    var angles = buttressAngles ?? new[] { 0.2, 1.7, 3.4, 4.9 };
                    for (int k = 0; k < angles.Length; k++)
                    {
                        double diff = Math.Abs(th - angles[k]);
                        if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
                        double cosLobe = Math.Max(0.0, Math.Cos(diff * 1.5));
                        buttressSum += Math.Pow(cosLobe, 3.0) * 0.75;
                    }
                    fluteMod += buttressSum * flareDecay;
                }

                double r = Math.Max(0.002, baseR * fluteMod);
                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                var pos = center + radialDir * r;
                ringPositions[i, s] = pos;

                var baseCol = Primitives.Mix(CragBarkUnder, CragBarkAsh, Math.Clamp(0.30 + 0.70 * t, 0.0, 1.0));
                if (harmonic1 < -0.25)
                {
                    double f = (-harmonic1 - 0.25) / 0.75;
                    var creviceCol = (t > 0.15 && t < 0.75 && harmonic2 > 0.2)
                        ? Primitives.Mix(CragBarkFurrow, CragBarkResin, 0.45)
                        : CragBarkFurrow;
                    baseCol = Primitives.Mix(baseCol, creviceCol, f * 0.85);
                }
                else if (harmonic1 > 0.25)
                {
                    double c = (harmonic1 - 0.25) / 0.75;
                    baseCol = Primitives.Mix(baseCol, CragBarkCrest, c * 0.70);
                }

                if (radialDir.Y > 0.35)
                {
                    double lichenAmt = Math.Min(0.40, (radialDir.Y - 0.35) * 0.75);
                    baseCol = Primitives.Mix(baseCol, CragBarkLichen, lichenAmt);
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
    /// Builds a single broad, sculpted 3D sword-like leathery leaf with dorsal midrib keel,
    /// adaxial moisture gutter, acute recurved tip, and blended +Up normal shading.
    /// </summary>
    private static void CragparasolLeaf(
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
        int detail,
        double droopFactor = 1.0)
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

        double phase = (seed % 991) / 991.0 * Math.PI * 2.0;

        // Broad oblanceolate / ensiform sword profile
        double WidthProfile(double t)
        {
            if (t < 0.12) return 0.28 + 0.72 * (t / 0.12);
            if (t <= 0.65) return 1.00 + 0.32 * Math.Sin(Math.PI * (t - 0.12) / 0.53);
            double u = (t - 0.65) / 0.35;
            return Math.Max(0.035, 1.32 * Math.Pow(Math.Max(0.0, 1.0 - u), 1.15));
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.08 * Math.Sin(Math.PI * Math.Pow(t, 0.70)) - 0.18 * Math.Pow(t, 1.7) * droopFactor);
            double drip = -length * 0.055 * droopFactor * Math.Pow(Math.Max(0.0, (t - 0.80) / 0.20), 2.0);
            double sway = length * 0.014 * Math.Sin(Math.PI * 1.30 * t + phase);
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

                double gutter = 0.045 * width * (x * x - 1.0);
                double midrib = 0.018 * width * Math.Exp(-absX * absX * 24.0);
                double ripple = 0.020 * width * Math.Pow(absX, 1.8) * Math.Sin(t * Math.PI * 2.8 + phase);
                double tipDroop = -0.025 * width * Math.Pow(absX, 1.5) * Math.Pow(t, 3.0);

                topPositions[r, c] = pSpine + sSpine * (w * x) + nSpine * (gutter + midrib + ripple + tipDroop);

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.55 || t > 0.65)
                {
                    double marginFrac = Math.Max((absX - 0.55) / 0.45, (t - 0.65) / 0.35);
                    bodyCol = Primitives.Mix(bodyCol, CragLeafBlush, marginFrac * 0.55);
                }
                double veinFrac = Math.Max(0.0, 1.0 - absX / 0.24);
                if (veinFrac > 0)
                    bodyCol = Primitives.Mix(bodyCol, Primitives.Mix(colUnder, CragLeafKeel, 0.65), veinFrac * 0.32);
                topColors[r, c] = bodyCol;
            }
        }

        // Smooth normals blended heavily with +Up (65%) for luminous, continuous umbrella dome lighting
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
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        m.RecordLeaf(leafStartVertex, leafStartIndex, basePos, length, volumetric: false);
    }

    /// <summary>
    /// Builds a multi-tiered rosette radiating horizontally and arching with natural droop (radius ~0.24-0.34)
    /// to form the authentic Dracaena cinnabari continuous umbrella canopy dome.
    /// </summary>
    private static void CragparasolRosette(
        MeshData m,
        Vec3 tipPos,
        Vec3 tipDir,
        double scale,
        ulong seed,
        double[] col1,
        double[] col2,
        int detail)
    {
        var underColor = Primitives.Mix(col2, new[] { 0.42, 0.50, 0.32 }, 0.45);
        var topBaseColor = Primitives.Mix(col1, CragLeafBlueOlive, 0.50);
        var topTipColor = Primitives.Mix(col1, CragLeafGoldOchre, 0.45);

        // Orthonormal frame for horizontal umbrella rosette:
        // Leaves radiate horizontally in a full 360° circle conforming to the dome surface
        var outDir = new Vec3(tipPos.X, 0, tipPos.Z);
        if (outDir.LengthSq < 1e-4) outDir = new Vec3(0, 0, 1);
        outDir = outDir.Normalized();
        var refSide = outDir.Cross(Vec3.Up).Normalized();

        int nTier1 = detail == 2 ? 4 : (detail == 1 ? 8 : 15);  // Outer cascading rim
        int nTier2 = detail == 2 ? 3 : (detail == 1 ? 7 : 13);  // Broad middle mantle
        int nTier3 = detail == 2 ? 2 : (detail == 1 ? 5 : 9);   // Elevated inner dome
        int nTier4 = detail == 2 ? 2 : (detail == 1 ? 3 : 5);   // Central apical bud

        // --- Tier 1: Outer Cascading Rim (radiating horizontally with gentle outward droop) ---
        double az1 = Rng.HashUnit(seed, 2, 0) * Math.PI * 2.0;
        for (int l = 0; l < nTier1; l++)
        {
            double angle = az1 + 2.0 * Math.PI * l / nTier1 + 0.12 * Math.Sin(l * 2.3);
            var leafRadial = (outDir * Math.Cos(angle) + refSide * Math.Sin(angle)).Normalized();

            // Spreading horizontally with gentle downward arch to form the outer convex umbrella rim
            var lDir = (leafRadial * 0.95 - Vec3.Up * 0.18).Normalized();
            var lNorm = (Vec3.Up * 0.88 + leafRadial * 0.32).Normalized();

            double lLen = (0.24 + 0.04 * Rng.HashUnit(seed, (ulong)(l * 17 + 101), 0)) * scale;
            double lWid = lLen * 0.30;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 43 + 7));

            CragparasolLeaf(m, tipPos + leafRadial * 0.015, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 1.25);
        }

        // --- Tier 2: Middle Spreading Mantle (horizontal leathery umbrella blanket) ---
        double az2 = az1 + Math.PI / nTier1;
        for (int l = 0; l < nTier2; l++)
        {
            double angle = az2 + 2.0 * Math.PI * l / nTier2 + 0.10 * Math.Cos(l * 1.8);
            var leafRadial = (outDir * Math.Cos(angle) + refSide * Math.Sin(angle)).Normalized();

            // Broad horizontal blade catching sunlight across the dome plane
            var lDir = (leafRadial * 0.98 + Vec3.Up * 0.06).Normalized();
            var lNorm = (Vec3.Up * 0.92 - leafRadial * 0.20).Normalized();

            double lLen = (0.20 + 0.03 * Rng.HashUnit(seed, (ulong)(l * 23 + 201), 0)) * scale;
            double lWid = lLen * 0.31;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 59 + 19));

            CragparasolLeaf(m, tipPos + leafRadial * 0.010 + Vec3.Up * 0.008, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 1.00);
        }

        // --- Tier 3: Inner Elevated Dome Layer (gently raised horizontal dome contour) ---
        double az3 = az2 + Math.PI / nTier2;
        for (int l = 0; l < nTier3; l++)
        {
            double angle = az3 + 2.0 * Math.PI * l / nTier3;
            var leafRadial = (outDir * Math.Cos(angle) + refSide * Math.Sin(angle)).Normalized();

            // Gentle elevation (~16° above horizontal, 74° from vertical - NOT a vertical spike!)
            var lDir = (leafRadial * 0.92 + Vec3.Up * 0.28).Normalized();
            var lNorm = (Vec3.Up * 0.90 - leafRadial * 0.30).Normalized();

            double lLen = (0.16 + 0.025 * Rng.HashUnit(seed, (ulong)(l * 31 + 301), 0)) * scale;
            double lWid = lLen * 0.32;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 71 + 37));

            CragparasolLeaf(m, tipPos + leafRadial * 0.007 + Vec3.Up * 0.015, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 0.70);
        }

        // --- Tier 4: Central Apical Bud (compact sword points at rosette heart) ---
        double az4 = az3 + Math.PI / nTier3;
        for (int l = 0; l < nTier4; l++)
        {
            double angle = az4 + 2.0 * Math.PI * l / nTier4;
            var leafRadial = (outDir * Math.Cos(angle) + refSide * Math.Sin(angle)).Normalized();

            // Compact 35°-40° rise (50°-55° from vertical) finishing the rosette crown
            var lDir = (leafRadial * 0.78 + Vec3.Up * 0.58).Normalized();
            var lNorm = (Vec3.Up * 0.85 - leafRadial * 0.40).Normalized();

            double lLen = (0.11 + 0.018 * Rng.HashUnit(seed, (ulong)(l * 41 + 401), 0)) * scale;
            double lWid = lLen * 0.32;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 83 + 53));

            CragparasolLeaf(m, tipPos + leafRadial * 0.004 + Vec3.Up * 0.022, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 0.45);
        }
    }

    /// <summary>
    /// Adds lateral foliage fan sprays along secondary branch arms to connect the umbrella plane.
    /// </summary>
    private static void CragparasolBranchInfillSprays(
        MeshData m,
        IReadOnlyList<Vec3> branchPath,
        double startFrac,
        double endFrac,
        int count,
        double scale,
        ulong seed,
        double[] col1,
        double[] col2,
        int detail)
    {
        var underColor = Primitives.Mix(col2, new[] { 0.42, 0.50, 0.32 }, 0.45);
        var topBaseColor = Primitives.Mix(col1, CragLeafBlueOlive, 0.50);
        var topTipColor = Primitives.Mix(col1, CragLeafGoldOchre, 0.45);

        for (int i = 0; i < count; i++)
        {
            double t = MathD.Lerp(startFrac, endFrac, (i + 0.5) / count);
            int idx = Math.Min(branchPath.Count - 2, (int)(t * (branchPath.Count - 1)));
            double frac = (t * (branchPath.Count - 1)) - idx;
            var pos = branchPath[idx] * (1.0 - frac) + branchPath[idx + 1] * frac;
            var fwd = (branchPath[idx + 1] - branchPath[idx]).Normalized();

            var side = fwd.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            int leavesPerSide = detail == 2 ? 1 : (detail == 1 ? 2 : 3);
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                for (int l = 0; l < leavesPerSide; l++)
                {
                    double spread = (l - (leavesPerSide - 1) * 0.5) * 0.28;
                    var lDir = (side * sideSign * 0.88 + fwd * (0.35 + spread) - Vec3.Up * 0.18).Normalized();
                    var lNorm = (Vec3.Up * 0.85 + side * (sideSign * 0.50)).Normalized();

                    double lLen = (0.16 + 0.025 * Rng.HashUnit(seed, (ulong)(i * 50 + sideSign * 10 + l), 0)) * scale;
                    double lWid = lLen * 0.30;
                    ulong lSeed = Rng.Mix(seed, (ulong)(i * 1000 + sideSign * 100 + l * 7));

                    CragparasolLeaf(m, pos, lDir, lNorm, lLen, lWid, lSeed,
                        topBaseColor, topTipColor, underColor, detail, droopFactor: 1.10);
                }
            }
        }
    }

    /// <summary>
    /// Whimsical Signature: Hanging coral-red panicle bearing glowing vermilion-amber dragon berries (UV2 = (1.35, 0.88)).
    /// </summary>
    private static void CragparasolBerryPanicle(
        MeshData m,
        Vec3 attachPos,
        Vec3 hangDir,
        double length,
        ulong seed,
        int detail)
    {
        int panicleStart = m.VertexCount;
        var rng = Rng.Keyed(seed, "cragparasol.berry", 0);

        // Central hanging stalk
        int stalkStations = detail == 2 ? 3 : 5;
        var p0 = attachPos;
        var pEnd = attachPos + (hangDir * 0.65 - Vec3.Up * 0.76).Normalized() * length;
        var c0 = attachPos - Vec3.Up * (length * 0.35);
        var c1 = pEnd - (hangDir * 0.30 - Vec3.Up * 0.70).Normalized() * (length * 0.35);

        var stalkPath = CragparasolSampleBezier(p0, c0, c1, pEnd, stalkStations);
        var stalkRadii = new double[stalkStations];
        for (int i = 0; i < stalkStations; i++)
        {
            double t = i / (double)(stalkStations - 1);
            stalkRadii[i] = MathD.Lerp(0.0035, 0.0012, t);
        }

        // Build pedicel tube
        Primitives.Tube(m, stalkPath, stalkRadii, detail == 2 ? 4 : 5,
            (i, v) => (CragPanicleStalk, 1.0, i / (double)(stalkStations - 1), v, 2.0, 0.0));

        // Lateral berry clusters along stalk
        int clusters = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
        for (int c = 0; c < clusters; c++)
        {
            double tCluster = 0.22 + 0.74 * (c / (double)Math.Max(1, clusters - 1));
            var nodePos = CragparasolBezier(p0, c0, c1, pEnd, tCluster);

            int berriesInCluster = detail == 2 ? 2 : (detail == 1 ? 3 : 3);
            for (int b = 0; b < berriesInCluster; b++)
            {
                double bAz = b * (2.0 * Math.PI / berriesInCluster) + rng.Range(-0.2, 0.2);
                var bOut = MidRadial(bAz);
                double bReach = rng.Range(0.015, 0.028);
                var berryCenter = nodePos + bOut * bReach - Vec3.Up * rng.Range(0.005, 0.015);

                // Short connecting pedicel
                Primitives.Tube(m, new[] { nodePos, berryCenter }, new[] { 0.0012, 0.0008 }, detail == 2 ? 3 : 4,
                    (i, v) => (CragPanicleStalk, 1.0, i, v, 2.0, 0.0));

                // Glowing dragon-ruby berry
                double berryR = rng.Range(0.008, 0.012);
                var berryCol = Primitives.Mix(CragBerryRuby, CragBerryAmber, rng.Range(0.1, 0.5));

                int bRings = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
                int bSides = detail == 2 ? 4 : (detail == 1 ? 5 : 6);
                Primitives.Ellipsoid(m, berryCenter, new Vec3(berryR, berryR * 1.15, berryR), bRings, bSides,
                    (r, s) => (berryCol, 1.0, r, s, 1.35, 0.88));
            }
        }

        if (m.VertexCount > panicleStart)
        {
            m.EssentialSpans.Add((panicleStart, m.VertexCount - panicleStart));
        }
    }

    /// <summary>
    /// Cragparasol procedural generator.
    /// Builds a rugged, windswept dry-soil microcanopy inspired by the Socotra Dragon's Blood Tree,
    /// with muscular fluted buttressed trunk, swollen branch collar, 3-order dichotomous umbrella vault,
    /// cohesive continuous convex parasol canopy dome (width ~1.4x-1.6x height), and hanging dragon-ruby berry panicles.
    /// </summary>
    private static void Cragparasol(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        int detail = m.FloraDetailLevel ?? 0;

        // General wind-lean orientation
        double az = rng.Range(0, Math.PI * 2);
        var leanDir = MidRadial(az);
        var orthoTrunk = MidRadial(az + Math.PI * 0.5);

        // -------------------------------------------------------------------------
        // 1. Muscular Fluted Sinuous Trunk & Buttressed Anchor
        // -------------------------------------------------------------------------
        double footY = 0.00;
        var pFoot = Vec3.Up * footY;

        double forkH = juvenile ? rng.Range(0.32, 0.38) : rng.Range(0.42, 0.48);
        double forkLean = rng.Range(0.05, 0.10);
        var pFork = pFoot + leanDir * forkLean + orthoTrunk * rng.Range(-0.02, 0.02) + Vec3.Up * forkH;

        var cTrunk0 = pFoot + Vec3.Up * (forkH * 0.45);
        var cTrunk1 = pFork - (Vec3.Up * 0.88 + leanDir * 0.30).Normalized() * (forkH * 0.40);

        int trunkStations = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
        var trunkPoints = CragparasolSampleBezier(pFoot, cTrunk0, cTrunk1, pFork, trunkStations);
        var trunkRadii = new double[trunkStations];

        double rBase = juvenile ? rng.Range(0.034, 0.040) : rng.Range(0.048, 0.058);
        double rFork = rBase * 0.68;

        for (int i = 0; i < trunkStations; i++)
        {
            double t = i / (double)(trunkStations - 1);
            trunkRadii[i] = MathD.Lerp(rBase, rFork, Math.Pow(t, 0.80));
        }

        var buttressAngles = new[] { az - 0.40, az + 1.25, az + 2.85, az + 4.60 };
        CragparasolFlutedStem(m, trunkPoints, trunkRadii, detail,
            twistTotal: 1.15, ridges: 5, rootButtress: true, buttressAngles: buttressAngles);

        // -------------------------------------------------------------------------
        // 2. Muscular Swollen Branch Collar at Trunk Summit
        // -------------------------------------------------------------------------
        double collarH = rFork * 1.50;
        CragparasolBranchCollar(m, pFork, rFork * 1.30, collarH, detail);

        // -------------------------------------------------------------------------
        // 3. 3-Order Dichotomous Umbrella Vault (Convex Mushroom Dome Canopy)
        // -------------------------------------------------------------------------
        // Total tree height ~ 0.95 - 1.05 m; dome rise above fork ~ 0.50 - 0.55 m.
        // Canopy radius ~ 0.72 - 0.82 m (width ~ 1.45 - 1.65 m -> ratio ~ 1.45x - 1.60x).
        double domeRise = juvenile ? 0.36 : rng.Range(0.48, 0.54);
        double maxCanopyR = juvenile ? 0.56 : rng.Range(0.72, 0.82);
        var domeCenter = pFork + Vec3.Up * domeRise + leanDir * (domeRise * 0.12);

        // Convex umbrella dome height formula: highest in center, arching smoothly down to outer rim
        double DomeSurfaceY(double distR)
        {
            double normR = Math.Clamp(distR / maxCanopyR, 0.0, 1.0);
            return domeCenter.Y - (domeRise * 0.36) * Math.Pow(normR, 1.70);
        }

        int numPrimaryLimbs = juvenile ? 3 : 4;
        double baseLimbAz = az + rng.Range(0.10, 0.35);

        var berryAttachPoints = new List<(Vec3 Pos, Vec3 Dir)>();

        for (int k = 0; k < numPrimaryLimbs; k++)
        {
            double limbAz = baseLimbAz + 2.0 * Math.PI * k / numPrimaryLimbs + rng.Range(-0.08, 0.08);
            var limbOutward = MidRadial(limbAz);
            var limbOrtho = MidRadial(limbAz + Math.PI * 0.5);

            // Primary limb extends to ~45-52% of canopy radius in an expansive candelabra curve
            double primReach = maxCanopyR * rng.Range(0.44, 0.52);
            var primEndPos = pFork + limbOutward * primReach + Vec3.Up * (domeRise * 0.36);

            // Candelabra bough flare: leaves trunk crown spreading broadly outward at ~55° from vertical,
            // sweeping outward and turning upward toward the dome
            var primStartDir = (limbOutward * 0.82 + Vec3.Up * 0.55).Normalized();
            var cPrim0 = pFork + primStartDir * (primReach * 0.45);
            var primEndDir = (limbOutward * 0.65 + Vec3.Up * 0.75).Normalized();
            var cPrim1 = primEndPos - primEndDir * (primReach * 0.40);

            int primStations = detail == 2 ? 4 : (detail == 1 ? 6 : 8);
            var primPath = CragparasolSampleBezier(pFork, cPrim0, cPrim1, primEndPos, primStations);
            var primRadii = new double[primStations];

            double primBaseR = rFork * 0.64;
            double primTipR = primBaseR * 0.68;
            for (int i = 0; i < primStations; i++)
            {
                double t = i / (double)(primStations - 1);
                primRadii[i] = MathD.Lerp(primBaseR, primTipR, t);
            }
            CragparasolFlutedStem(m, primPath, primRadii, detail, twistTotal: 0.35, ridges: 4);

            // Flared branch collar at primary fork junction
            CragparasolBranchCollar(m, primEndPos, primTipR * 1.35, primTipR * 1.5, detail);

            // Infill foliage along primary limb
            if (detail < 2)
            {
                ulong primSpraySeed = Rng.Mix(seed, (ulong)(k * 15000 + 11));
                CragparasolBranchInfillSprays(m, primPath, 0.50, 0.85, 1,
                    scale: juvenile ? 0.75 : 0.95, primSpraySeed, c1, c2, detail);
            }

            // -----------------------------------------------------------------
            // Secondary Dichotomous Boughs: Inner Bough & Outer Bough
            // -----------------------------------------------------------------
            // Inner Bough: arches inward/upward to populate the central & mid dome
            // Outer Bough: arches outward to populate the outer rim
            for (int b = 0; b < 2; b++)
            {
                bool isInner = (b == 0);
                double boughAngleOffset = isInner ? -0.32 : 0.38;
                double boughAz = limbAz + boughAngleOffset + rng.Range(-0.06, 0.06);
                var boughOutward = MidRadial(boughAz);
                var boughOrtho = MidRadial(boughAz + Math.PI * 0.5);

                double boughReach = isInner ? maxCanopyR * rng.Range(0.35, 0.52) : maxCanopyR * rng.Range(0.68, 0.82);
                var boughForkPos = primEndPos + (boughOutward * 0.75 + Vec3.Up * 0.55).Normalized() * (maxCanopyR * 0.24);

                var cBough0 = primEndPos + (boughOutward * 0.70 + Vec3.Up * 0.65).Normalized() * 0.12;
                var cBough1 = boughForkPos - (boughOutward * 0.55 + Vec3.Up * 0.75).Normalized() * 0.10;

                int boughStations = detail == 2 ? 4 : 5;
                var boughPath = CragparasolSampleBezier(primEndPos, cBough0, cBough1, boughForkPos, boughStations);
                var boughRadii = new double[boughStations];
                double boughBaseR = primTipR * 0.78;
                double boughTipR = boughBaseR * 0.68;
                for (int i = 0; i < boughStations; i++)
                {
                    double t = i / (double)(boughStations - 1);
                    boughRadii[i] = MathD.Lerp(boughBaseR, boughTipR, t);
                }
                CragparasolFlutedStem(m, boughPath, boughRadii, detail, twistTotal: 0.25, ridges: 3);

                // Flared branch collar at secondary fork junction
                CragparasolBranchCollar(m, boughForkPos, boughTipR * 1.30, boughTipR * 1.4, detail);

                // Infill foliage along secondary bough
                if (detail < 2)
                {
                    ulong boughSpraySeed = Rng.Mix(seed, (ulong)(k * 25000 + b * 2500 + 43));
                    CragparasolBranchInfillSprays(m, boughPath, 0.45, 0.85, 1,
                        scale: juvenile ? 0.75 : 0.95, boughSpraySeed, c1, c2, detail);
                }

                // -------------------------------------------------------------
                // Tertiary Candelabra Tips (Sitting directly on the umbrella dome)
                // -------------------------------------------------------------
                // Inner bough produces 2 tips: 1 central dome tip + 1 mid-canopy tip
                // Outer bough produces 2-3 tips: 2 outer rim tips (+ 1 perimeter tip)
                int numTips = isInner ? 2 : (detail == 2 ? 2 : 3);
                for (int tipIdx = 0; tipIdx < numTips; tipIdx++)
                {
                    double tipR;
                    double tipAz;
                    if (isInner)
                    {
                        if (tipIdx == 0)
                        {
                            // Central apex tip (radius ~0.15 - 0.30 from center, high in dome)
                            tipR = rng.Range(0.16, 0.28) * maxCanopyR;
                            tipAz = limbAz + rng.Range(-0.45, 0.45);
                        }
                        else
                        {
                            // Mid-canopy tip (radius ~0.42 - 0.58)
                            tipR = rng.Range(0.42, 0.58) * maxCanopyR;
                            tipAz = boughAz - 0.28 + rng.Range(-0.08, 0.08);
                        }
                    }
                    else
                    {
                        // Outer rim tips (radius ~0.72 - 0.96)
                        double tipFrac = (tipIdx - (numTips - 1) * 0.5) * 0.35;
                        tipR = rng.Range(0.78, 0.96) * maxCanopyR;
                        tipAz = boughAz + tipFrac + rng.Range(-0.06, 0.06);
                    }

                    var tipOut = MidRadial(tipAz);
                    double targetY = DomeSurfaceY(tipR);
                    var pTipTarget = domeCenter + tipOut * tipR;
                    pTipTarget = new Vec3(pTipTarget.X, targetY, pTipTarget.Z);

                    // Upward-ascending candelabrum spur
                    var cSpur0 = boughForkPos + (boughOutward * 0.45 + Vec3.Up * 0.70).Normalized() * 0.10;
                    var candelabrumUp = (Vec3.Up * 0.75 + tipOut * 0.45).Normalized();
                    var cSpur1 = pTipTarget - candelabrumUp * 0.08;

                    int spurStations = detail == 2 ? 4 : 5;
                    var spurPath = CragparasolSampleBezier(boughForkPos, cSpur0, cSpur1, pTipTarget, spurStations);
                    var spurRadii = new double[spurStations];
                    double spurBaseR = boughTipR * 0.70;
                    double spurTipR = Math.Max(0.005, spurBaseR * 0.55);
                    for (int si = 0; si < spurStations; si++)
                    {
                        double st = si / (double)(spurStations - 1);
                        spurRadii[si] = MathD.Lerp(spurBaseR, spurTipR, st);
                    }
                    CragparasolFlutedStem(m, spurPath, spurRadii, detail, twistTotal: 0.15, ridges: 3);

                    // Terminal Rosette on the dome
                    var tipDir = CragparasolBezierTangent(boughForkPos, cSpur0, cSpur1, pTipTarget, 1.0);
                    ulong rosetteSeed = Rng.Mix(seed, (ulong)(k * 50000 + b * 5000 + tipIdx * 500 + 77));
                    double rosetteScale = isInner
                        ? (juvenile ? 0.85 : 1.05)
                        : (juvenile ? 0.92 : 1.15);

                    CragparasolRosette(m, pTipTarget, tipDir, rosetteScale, rosetteSeed, c1, c2, detail);

                    if (!isInner)
                    {
                        berryAttachPoints.Add((pTipTarget - Vec3.Up * 0.02, tipOut));
                    }
                }
            }
        }

        // -------------------------------------------------------------------------
        // 4. Whimsical Signature: Hanging Dragon-Ruby Berry Panicles (UV2 = (1.35, 0.88))
        // -------------------------------------------------------------------------
        int panicleCount = juvenile ? 4 : (detail == 2 ? 4 : Math.Min(berryAttachPoints.Count, 10));
        for (int p = 0; p < panicleCount; p++)
        {
            var (attach, outDir) = berryAttachPoints[p % berryAttachPoints.Count];
            double panicleLen = rng.Range(0.08, 0.13) * (juvenile ? 0.75 : 1.0);
            ulong panicleSeed = Rng.Mix(seed, (ulong)(p * 8888 + 107));
            CragparasolBerryPanicle(m, attach, outDir, panicleLen, panicleSeed, detail);
        }
    }
}
