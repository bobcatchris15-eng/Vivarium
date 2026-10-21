using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Cragparasol (microcanopy_cragparasol) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A rugged, windswept dry-soil/crag microcanopy inspired by ancient alpine junipers,
/// dragon-blood trees, and African umbrella acacias.
///
/// Visual Architecture:
///   1. Muscular Fluted Basal Anchor: Prominent root buttresses splaying at ground level
///      into rock crevices, rising into a smooth, sinuous, spiral-fluted trunk.
///   2. Gracefully Curved Scaffold Limbs: 100% continuous cubic Bézier splines with ZERO sharp
///      angles. Smooth, sweeping upward-and-outward arches that flatten naturally into the parasol plane.
///   3. Lush, High-Density Canopy: Dense multi-tiered terminal rosettes and lateral branch sprays
///      forming a continuous, luxuriant umbrella mantle of hundreds of tough leathery leaves.
///   4. 3D Sculpted Leaf Geometry: Broad oblanceolate blades with prominent dorsal keels,
///      adaxial moisture gutters, elegant recurved tips, and sun-baked terracotta margin blush.
/// </summary>
public static partial class OrganismMeshes
{
    // Weathered bark palette
    private static readonly double[] CragBarkFurrow = { 0.18, 0.14, 0.10 };   // Deep shaded crevice
    private static readonly double[] CragBarkUnder  = { 0.35, 0.23, 0.15 };   // Warm cinnamon living underwood
    private static readonly double[] CragBarkAsh    = { 0.52, 0.49, 0.42 };   // Weathered silver-grey outer bark
    private static readonly double[] CragBarkCrest  = { 0.62, 0.59, 0.52 };   // Sun-bleached wood highlight
    private static readonly double[] CragBarkLichen = { 0.42, 0.46, 0.30 };   // Subtle epilithic lichen dusting

    // Leathery foliage palette adjustments
    private static readonly double[] CragLeafBlush  = { 0.58, 0.44, 0.22 };   // Sun-baked terracotta margin blush
    private static readonly double[] CragLeafKeel   = { 0.46, 0.54, 0.34 };   // Pale reinforced dorsal midrib

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
    /// Builds a lofted fluted stem with organic buttressing, spiral twist, and smooth normal vectors.
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

        int segments = detail == 2 ? 6 : (detail == 1 ? 8 : 12);
        int startVertex = m.VertexCount;

        // 1. Calculate smooth tangents along path
        var tangents = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            Vec3 d;
            if (i == 0) d = points[1] - points[0];
            else if (i == stations - 1) d = points[^1] - points[^2];
            else d = points[i + 1] - points[i - 1];
            tangents[i] = d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
        }

        // 2. Parallel transport reference frame
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

        // 3. Station ring positions and colors
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

                if (rootButtress && t < 0.28)
                {
                    double flareDecay = Math.Pow(1.0 - t / 0.28, 2.0);
                    double buttressSum = 0.0;
                    var angles = buttressAngles ?? new[] { 0.2, 1.7, 3.4, 4.9 };
                    for (int k = 0; k < angles.Length; k++)
                    {
                        double diff = Math.Abs(th - angles[k]);
                        if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
                        double cosLobe = Math.Max(0.0, Math.Cos(diff * 1.5));
                        buttressSum += Math.Pow(cosLobe, 3.0) * 0.65;
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
                    baseCol = Primitives.Mix(baseCol, CragBarkFurrow, f * 0.85);
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

        // 4. Smooth normal calculation
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

        // 5. Add vertices (UV2.x = 2.0 marks woody tissue)
        for (int i = 0; i < stations; i++)
        {
            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                m.AddVertex(ringPositions[i, s], ringNormals[i, s], ringColors[i, s], 1.0, points[i].Y * 3.5, u, 2.0, 0.0);
            }
        }

        // 6. Connect triangles
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
    /// Builds a single broad, sculpted 3D oblanceolate leathery leaf with dorsal midrib keel,
    /// adaxial moisture gutter, acute recurved tip, and double-sided solid shading.
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

        int rows = detail == 2 ? 3 : 4;
        int cols = detail == 2 ? 1 : 2;

        var side = leafNormal.Cross(leafDir);
        if (side.LengthSq < 1e-6) side = leafDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(leafDir).Normalized();

        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.TriangleCount * 3;

        // Broad oblanceolate width profile
        double WidthProfile(double t)
        {
            if (t < 0.12) return 0.25 + 0.75 * (t / 0.12);
            if (t <= 0.65) return 1.00 + 0.35 * Math.Sin(Math.PI * (t - 0.12) / 0.53);
            double u = (t - 0.65) / 0.35;
            return Math.Max(0.04, 1.35 * Math.Pow(Math.Max(0.0, 1.0 - u), 0.85));
        }

        // Spine curve: upward arching emergence, then elegant downward recurve
        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.08 * Math.Sin(Math.PI * Math.Pow(t, 0.70)) - 0.20 * Math.Pow(t, 1.7) * droopFactor);
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

                double gutter = 0.05 * width * (x * x - 1.0);
                double keel = -0.10 * width * Math.Pow(Math.Max(0.0, 1.0 - absX), 1.4) - 0.015 * width;

                var offsetSide = sSpine * (w * x);
                topPositions[r, c] = pSpine + offsetSide + nSpine * gutter;
                botPositions[r, c] = pSpine + offsetSide + nSpine * keel;

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.60 || t > 0.70)
                {
                    double marginFrac = Math.Max((absX - 0.60) / 0.40, (t - 0.70) / 0.30);
                    bodyCol = Primitives.Mix(bodyCol, CragLeafBlush, marginFrac * 0.55);
                }
                topColors[r, c] = bodyCol;

                var underCol = colUnder;
                if (absX < 0.25)
                {
                    double kFrac = (0.25 - absX) / 0.25;
                    underCol = Primitives.Mix(underCol, CragLeafKeel, kFrac * 0.65);
                }
                botColors[r, c] = underCol;
            }
        }

        // Normals
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

        // Top surface
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

        // Bottom surface
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

        // Seal margins
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
    /// Builds a rich, dense multi-tiered rosette ("parasol dome") with heavy horizontal spread
    /// and cascading outer leaves that form the iconic umbrella canopy.
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
        var underColor = Primitives.Mix(col2, new[] { 0.40, 0.48, 0.32 }, 0.45);
        var topBaseColor = Primitives.Mix(col1, new[] { 0.22, 0.33, 0.15 }, 0.40);
        var topTipColor = Primitives.Mix(col1, new[] { 0.36, 0.45, 0.19 }, 0.35);

        var side = tipDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = tipDir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(tipDir).Normalized();

        // Tier counts: 4 overlapping whorls for rich density
        int nTier1 = detail == 2 ? 6 : (detail == 1 ? 10 : 14); // Outer cascading rim
        int nTier2 = detail == 2 ? 5 : (detail == 1 ? 8  : 12); // Broad middle mantle
        int nTier3 = detail == 2 ? 4 : (detail == 1 ? 6  : 10); // Elevated inner dome
        int nTier4 = detail == 2 ? 2 : (detail == 1 ? 4  : 6);  // Central apical cluster

        // --- Tier 1: Outer Cascading Rim ---
        // Radiates outward and downward, forming the wide umbrella rim
        double az1 = Rng.HashUnit(seed, 2, 0) * Math.PI * 2.0;
        for (int l = 0; l < nTier1; l++)
        {
            double angle = az1 + 2.0 * Math.PI * l / nTier1 + 0.15 * Math.Sin(l * 2.3);
            double lateral = Math.Cos(angle);
            double forward = Math.Sin(angle);

            var rad = (side * lateral + tipDir * (forward * 0.85)).Normalized();
            // Downward pitch (-20 to -35 degrees)
            var lDir = (rad * 0.90 - Vec3.Up * 0.43).Normalized();
            var lNorm = (Vec3.Up * 0.85 + rad * 0.52).Normalized();

            double lLen = (0.115 + 0.022 * Rng.HashUnit(seed, (ulong)(l * 17 + 101), 0)) * scale;
            double lWid = lLen * 0.36;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 43 + 7));

            CragparasolLeaf(m, tipPos + rad * 0.012, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 1.25);
        }

        // --- Tier 2: Middle Spreading Mantle ---
        // Spreads horizontally to fill the umbrella plane
        double az2 = az1 + Math.PI / nTier1;
        for (int l = 0; l < nTier2; l++)
        {
            double angle = az2 + 2.0 * Math.PI * l / nTier2 + 0.12 * Math.Cos(l * 1.8);
            double lateral = Math.Cos(angle);
            double forward = Math.Sin(angle);

            var rad = (side * lateral + tipDir * (forward * 0.85)).Normalized();
            // Shallow pitch (-5 to +10 degrees)
            var lDir = (rad * 0.96 + Vec3.Up * 0.12).Normalized();
            var lNorm = (Vec3.Up * 0.90 - rad * 0.40).Normalized();

            double lLen = (0.095 + 0.018 * Rng.HashUnit(seed, (ulong)(l * 23 + 201), 0)) * scale;
            double lWid = lLen * 0.35;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 59 + 19));

            CragparasolLeaf(m, tipPos + rad * 0.009 + Vec3.Up * 0.006, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 1.05);
        }

        // --- Tier 3: Inner Elevated Dome ---
        // Angled upward at ~35 degrees to build volume
        double az3 = az2 + Math.PI / nTier2;
        for (int l = 0; l < nTier3; l++)
        {
            double angle = az3 + 2.0 * Math.PI * l / nTier3;
            double lateral = Math.Cos(angle);
            double forward = Math.Sin(angle);

            var rad = (side * lateral + tipDir * (forward * 0.85)).Normalized();
            var lDir = (rad * 0.75 + Vec3.Up * 0.65).Normalized();
            var lNorm = (Vec3.Up * 0.70 - rad * 0.70).Normalized();

            double lLen = (0.075 + 0.014 * Rng.HashUnit(seed, (ulong)(l * 31 + 301), 0)) * scale;
            double lWid = lLen * 0.34;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 71 + 37));

            CragparasolLeaf(m, tipPos + rad * 0.006 + Vec3.Up * 0.012, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 0.75);
        }

        // --- Tier 4: Central Apical Bud ---
        // Upright young leaves in the center
        double az4 = az3 + Math.PI / nTier3;
        for (int l = 0; l < nTier4; l++)
        {
            double angle = az4 + 2.0 * Math.PI * l / nTier4;
            double lateral = Math.Cos(angle);
            double forward = Math.Sin(angle);

            var rad = (side * lateral + tipDir * (forward * 0.85)).Normalized();
            var lDir = (rad * 0.35 + Vec3.Up * 0.93).Normalized();
            var lNorm = (Vec3.Up * 0.40 - rad * 0.90).Normalized();

            double lLen = (0.052 + 0.010 * Rng.HashUnit(seed, (ulong)(l * 41 + 401), 0)) * scale;
            double lWid = lLen * 0.32;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 83 + 53));

            CragparasolLeaf(m, tipPos + rad * 0.003 + Vec3.Up * 0.018, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail, droopFactor: 0.45);
        }
    }

    /// <summary>
    /// Adds lush lateral foliage fan sprays along the upper arms of branches,
    /// creating a continuous, connected umbrella canopy without bare voids.
    /// </summary>
    private static void CragparasolBranchLateralSprays(
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
        var underColor = Primitives.Mix(col2, new[] { 0.40, 0.48, 0.32 }, 0.45);
        var topBaseColor = Primitives.Mix(col1, new[] { 0.22, 0.33, 0.15 }, 0.40);
        var topTipColor = Primitives.Mix(col1, new[] { 0.36, 0.45, 0.19 }, 0.35);

        for (int i = 0; i < count; i++)
        {
            double t = MathD.Lerp(startFrac, endFrac, (i + 0.5) / count);
            int idx = Math.Min(branchPath.Count - 2, (int)(t * (branchPath.Count - 1)));
            double frac = (t * (branchPath.Count - 1)) - idx;
            var pos = branchPath[idx] * (1.0 - frac) + branchPath[idx + 1] * frac;
            var fwd = (branchPath[idx + 1] - branchPath[idx]).Normalized();

            var side = fwd.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            // Bilateral fan sprays on both sides of the branch
            int leavesPerSide = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                for (int l = 0; l < leavesPerSide; l++)
                {
                    double spread = (l - (leavesPerSide - 1) * 0.5) * 0.28;
                    var lDir = (side * sideSign * 0.85 + fwd * (0.35 + spread) - Vec3.Up * 0.22).Normalized();
                    var lNorm = (Vec3.Up * 0.85 + side * (sideSign * 0.50)).Normalized();

                    double lLen = (0.088 + 0.018 * Rng.HashUnit(seed, (ulong)(i * 50 + sideSign * 10 + l), 0)) * scale;
                    double lWid = lLen * 0.34;
                    ulong lSeed = Rng.Mix(seed, (ulong)(i * 1000 + sideSign * 100 + l * 7));

                    CragparasolLeaf(m, pos, lDir, lNorm, lLen, lWid, lSeed,
                        topBaseColor, topTipColor, underColor, detail, droopFactor: 1.10);
                }
            }
        }
    }

    /// <summary>
    /// Cragparasol procedural generator.
    /// Builds a rugged, windswept dry-soil microcanopy with fluted buttressed trunk,
    /// beautifully curved Bézier scaffold limbs, and dense, lush, cascading parasol canopies.
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
        // 1. Smooth Fluted Sinuous Trunk (Continuous Bézier Spline)
        // -------------------------------------------------------------------------
        double footY = 0.00;
        var pFoot = Vec3.Up * footY;

        double forkH = juvenile ? rng.Range(0.32, 0.38) : rng.Range(0.38, 0.45);
        double forkLean = rng.Range(0.06, 0.12);
        var pFork = pFoot + leanDir * forkLean + orthoTrunk * rng.Range(-0.03, 0.03) + Vec3.Up * forkH;

        // Smooth cubic Bézier control points for trunk
        var cTrunk0 = pFoot + Vec3.Up * (forkH * 0.40);
        var cTrunk1 = pFork - (Vec3.Up * 0.85 + leanDir * 0.35).Normalized() * (forkH * 0.40);

        int trunkStations = detail == 2 ? 6 : (detail == 1 ? 9 : 12);
        var trunkPoints = CragparasolSampleBezier(pFoot, cTrunk0, cTrunk1, pFork, trunkStations);
        var trunkRadii = new double[trunkStations];

        double rBase = juvenile ? rng.Range(0.026, 0.032) : rng.Range(0.036, 0.045);
        double rFork = rBase * 0.62;

        for (int i = 0; i < trunkStations; i++)
        {
            double t = i / (double)(trunkStations - 1);
            trunkRadii[i] = MathD.Lerp(rBase, rFork, Math.Pow(t, 0.80));
        }

        var buttressAngles = new[] { az - 0.40, az + 1.25, az + 2.85, az + 4.60 };
        CragparasolFlutedStem(m, trunkPoints, trunkRadii, detail,
            twistTotal: 1.15, ridges: 5, rootButtress: true, buttressAngles: buttressAngles);

        // -------------------------------------------------------------------------
        // 2. Smoothly Curved Scaffold Limbs (Bézier Arches)
        // -------------------------------------------------------------------------
        int numLimbs = juvenile ? 4 : (detail == 2 ? 4 : 5 + (int)(rng.NextInt(2)));
        var limbAzimuths = new double[numLimbs];
        double baseLimbAz = az + rng.Range(0.15, 0.45);
        for (int k = 0; k < numLimbs; k++)
        {
            limbAzimuths[k] = baseLimbAz + 2.0 * Math.PI * k / numLimbs + rng.Range(-0.16, 0.16);
        }

        for (int k = 0; k < numLimbs; k++)
        {
            double lAz = limbAzimuths[k];
            var lOutward = MidRadial(lAz);
            var lOrtho = MidRadial(lAz + Math.PI * 0.5);

            // Wide lateral canopy spread
            double reach = juvenile ? rng.Range(0.56, 0.68) : rng.Range(0.72, 0.88);
            double summitY = juvenile ? rng.Range(0.68, 0.71) : rng.Range(0.72, 0.76);

            // Scaffold limb start: fork
            // End point: summit position
            var pLimbEnd = pFork + lOutward * reach + lOrtho * rng.Range(-0.05, 0.05) + Vec3.Up * (summitY - forkH);

            // Smooth Bézier control points:
            // c0 leaves fork curving outward and upward
            var startDir = (lOutward * 0.45 + Vec3.Up * 0.85).Normalized();
            var cLimb0 = pFork + startDir * (reach * 0.42);

            // c1 approaches end point horizontally to form the flat parasol canopy
            var endDir = (lOutward * 0.90 + Vec3.Up * 0.25).Normalized();
            var cLimb1 = pLimbEnd - endDir * (reach * 0.38);

            int limbStations = detail == 2 ? 6 : (detail == 1 ? 8 : 11);
            var limbPath = CragparasolSampleBezier(pFork, cLimb0, cLimb1, pLimbEnd, limbStations);
            var limbRadii = new double[limbStations];

            double limbBaseR = rFork * rng.Range(0.58, 0.70);
            double limbTipR = Math.Max(0.005, limbBaseR * 0.34);

            for (int i = 0; i < limbStations; i++)
            {
                double t = i / (double)(limbStations - 1);
                limbRadii[i] = MathD.Lerp(limbBaseR, limbTipR, Math.Pow(t, 0.75));
            }

            // Build smooth fluted limb
            CragparasolFlutedStem(m, limbPath, limbRadii, detail, twistTotal: 0.45, ridges: 4);

            // Primary terminal rosette
            var mainTipDir = CragparasolBezierTangent(pFork, cLimb0, cLimb1, pLimbEnd, 1.0);
            ulong mainRosetteSeed = Rng.Mix(seed, (ulong)(k * 10000 + 1));
            CragparasolRosette(m, pLimbEnd, mainTipDir, scale: juvenile ? 0.88 : 1.10,
                mainRosetteSeed, c1, c2, detail);

            // Lateral foliage fan sprays along distal primary limb
            int latSprays = detail == 2 ? 1 : 2;
            ulong spraySeed = Rng.Mix(seed, (ulong)(k * 20000 + 77));
            CragparasolBranchLateralSprays(m, limbPath, 0.52, 0.86, latSprays,
                scale: juvenile ? 0.80 : 1.00, spraySeed, c1, c2, detail);

            // ---------------------------------------------------------------------
            // 3. Smoothly Curved Secondary Branches
            // ---------------------------------------------------------------------
            int numSec = juvenile ? 1 : (detail == 2 ? 1 : 2);
            for (int b = 0; b < numSec; b++)
            {
                double branchFrac = b == 0 ? rng.Range(0.40, 0.52) : rng.Range(0.65, 0.78);
                var secRoot = CragparasolBezier(pFork, cLimb0, cLimb1, pLimbEnd, branchFrac);
                var secMotherTan = CragparasolBezierTangent(pFork, cLimb0, cLimb1, pLimbEnd, branchFrac);

                double sideSign = (b % 2 == 0) ? 1.0 : -1.0;
                double secAz = lAz + sideSign * rng.Range(0.55, 0.95);
                var secOut = MidRadial(secAz);
                double secReach = rng.Range(0.24, 0.38) * (juvenile ? 0.75 : 1.0);
                double secRise = rng.Range(0.03, 0.08);

                var secEnd = secRoot + secOut * secReach + Vec3.Up * secRise;

                // Smooth Bézier control points for secondary branch
                var secStartDir = (secMotherTan * 0.55 + secOut * 0.60 + Vec3.Up * 0.25).Normalized();
                var cSec0 = secRoot + secStartDir * (secReach * 0.40);
                var secEndDir = (secOut * 0.90 + Vec3.Up * 0.20).Normalized();
                var cSec1 = secEnd - secEndDir * (secReach * 0.35);

                int secStations = detail == 2 ? 5 : 7;
                var secPath = CragparasolSampleBezier(secRoot, cSec0, cSec1, secEnd, secStations);
                var secRadii = new double[secStations];

                double secBaseR = limbBaseR * 0.55;
                double secTipR = Math.Max(0.004, secBaseR * 0.35);

                for (int si = 0; si < secStations; si++)
                {
                    double st = si / (double)(secStations - 1);
                    secRadii[si] = MathD.Lerp(secBaseR, secTipR, st);
                }

                CragparasolFlutedStem(m, secPath, secRadii, detail, twistTotal: 0.30, ridges: 3);

                // Secondary terminal rosette
                var secTipDir = CragparasolBezierTangent(secRoot, cSec0, cSec1, secEnd, 1.0);
                ulong secRosetteSeed = Rng.Mix(seed, (ulong)(k * 30000 + b * 1000 + 19));
                CragparasolRosette(m, secEnd, secTipDir, scale: juvenile ? 0.80 : 0.98,
                    secRosetteSeed, c1, c2, detail);

                // Lateral spray along secondary branch
                if (detail < 2)
                {
                    ulong secSpraySeed = Rng.Mix(seed, (ulong)(k * 40000 + b * 200 + 13));
                    CragparasolBranchLateralSprays(m, secPath, 0.55, 0.85, 1,
                        scale: juvenile ? 0.75 : 0.90, secSpraySeed, c1, c2, detail);
                }

                // Tertiary spur branch in detail 0
                if (detail == 0 && !juvenile && b == 0)
                {
                    double tertFrac = 0.55;
                    var tertRoot = CragparasolBezier(secRoot, cSec0, cSec1, secEnd, tertFrac);
                    var tertMotherTan = CragparasolBezierTangent(secRoot, cSec0, cSec1, secEnd, tertFrac);

                    double tertAz = secAz - sideSign * rng.Range(0.50, 0.80);
                    var tertOut = MidRadial(tertAz);
                    double tertReach = rng.Range(0.14, 0.20);
                    var tertEnd = tertRoot + tertOut * tertReach + Vec3.Up * rng.Range(0.02, 0.05);

                    var tertStartDir = (tertMotherTan * 0.50 + tertOut * 0.65 + Vec3.Up * 0.20).Normalized();
                    var cTert0 = tertRoot + tertStartDir * (tertReach * 0.40);
                    var cTert1 = tertEnd - tertOut * (tertReach * 0.35);

                    var tertPath = CragparasolSampleBezier(tertRoot, cTert0, cTert1, tertEnd, 5);
                    var tertRadii = new[] { secBaseR * 0.50, secBaseR * 0.45, secBaseR * 0.40, secBaseR * 0.35, 0.003 };
                    CragparasolFlutedStem(m, tertPath, tertRadii, detail, twistTotal: 0.15, ridges: 3);

                    var tertDir = CragparasolBezierTangent(tertRoot, cTert0, cTert1, tertEnd, 1.0);
                    ulong tertRosetteSeed = Rng.Mix(seed, (ulong)(k * 50000 + b * 500 + 41));
                    CragparasolRosette(m, tertEnd, tertDir, scale: 0.85,
                        tertRosetteSeed, c1, c2, detail);
                }
            }
        }
    }
}
