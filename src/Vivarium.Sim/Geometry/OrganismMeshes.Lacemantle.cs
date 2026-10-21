using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Lacemantle (microcanopy_lacemantle) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A tiered understory tree inspired by Pagoda Dogwood (Cornus alternifolia / controversa),
/// creating horizontal 'shelves' or 'mantles' of delicate, lacy foliage that filter woodland light.
///
/// Visual Architecture:
///   1. Upright Sinuous Dark Trunk: Slender, dark charcoal/bistre trunk with fine longitudinal bark fissures
///      and swollen nodal branch collars at each tier level.
///   2. Smoothly Curved Horizontal Branch Shelves: Branches emerge from nodal collars with a gentle upward sweep,
///      then curve gracefully into strictly horizontal tabular planes (continuous cubic Bézier arches with NO sharp angles).
///   3. Bilateral Planar Branchlets: Branches divide into flat, horizontal fan twigs spreading like floating cloud shelves.
///   4. Lacy Foliage Sprays: Dense, delicate, finely divided, multi-lobed lacy leaves with 3D camber,
///      prominent midribs, pale sage undersides, and translucent emerald margins.
///   5. Upright Apex Crown: Slender vertical terminal spray crowning the central leader shoot.
/// </summary>
public static partial class OrganismMeshes
{
    // Dark rough woody bark palette
    private static readonly double[] LaceBarkDeep     = { 0.12, 0.10, 0.09 };   // Deep furrow/crevice
    private static readonly double[] LaceBarkDark     = { 0.20, 0.18, 0.16 };   // Charcoal-brown bark body
    private static readonly double[] LaceBarkCrest    = { 0.30, 0.27, 0.24 };   // Rough crest highlight
    private static readonly double[] LaceBarkNodular  = { 0.25, 0.22, 0.18 };   // Swollen branch collar

    // Foliage palette
    private static readonly double[] LaceLeafEmerald  = { 0.32, 0.52, 0.26 };   // Lighter emerald margin/tip
    private static readonly double[] LaceLeafVein     = { 0.42, 0.58, 0.32 };   // Pale midrib vein

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
    /// Builds a lofted dark woody stem with subtle longitudinal fissures, swollen nodal collars, and smooth normals.
    /// </summary>
    private static void LacemantleStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        int ridges = 6)
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

                double ridgeWave = Math.Cos(ridges * th);
                double roughMod = 1.0 + 0.12 * ridgeWave;
                double r = Math.Max(0.0016, baseR * roughMod);

                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                var pos = center + radialDir * r;
                ringPositions[i, s] = pos;

                var baseCol = Primitives.Mix(LaceBarkDark, LaceBarkCrest, 0.40 * t);
                if (ridgeWave < -0.25)
                {
                    double f = (-ridgeWave - 0.25) / 0.75;
                    baseCol = Primitives.Mix(baseCol, LaceBarkDeep, f * 0.85);
                }
                else if (ridgeWave > 0.25)
                {
                    double c = (ridgeWave - 0.25) / 0.75;
                    baseCol = Primitives.Mix(baseCol, LaceBarkCrest, c * 0.65);
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
    /// Builds a single delicate, finely-tapered lacy broadleaf with 3D camber,
    /// central midrib fold, and double-sided solid shading.
    /// </summary>
    private static void LacemantleLacyLeaf(
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

        // Elegant tapered rhombic/ovate width profile
        double WidthProfile(double t)
        {
            if (t < 0.15) return 0.20 + 0.80 * (t / 0.15);
            if (t <= 0.55) return 1.00 + 0.32 * Math.Sin(Math.PI * (t - 0.15) / 0.40);
            double u = (t - 0.55) / 0.45;
            return Math.Max(0.04, 1.32 * Math.Pow(Math.Max(0.0, 1.0 - u), 0.85));
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            // Gentle arch and slight horizontal droop at the tip
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

                double gutter = 0.04 * width * (x * x - 1.0);
                double keel = -0.07 * width * Math.Pow(Math.Max(0.0, 1.0 - absX), 1.3) - 0.012 * width;

                var offsetSide = sSpine * (w * x);
                topPositions[r, c] = pSpine + offsetSide + nSpine * gutter;
                botPositions[r, c] = pSpine + offsetSide + nSpine * keel;

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.55 || t > 0.70)
                {
                    double marginFrac = Math.Max((absX - 0.55) / 0.45, (t - 0.70) / 0.30);
                    bodyCol = Primitives.Mix(bodyCol, LaceLeafEmerald, marginFrac * 0.50);
                }
                topColors[r, c] = bodyCol;

                var underCol = colUnder;
                if (absX < 0.25)
                {
                    double kFrac = (0.25 - absX) / 0.25;
                    underCol = Primitives.Mix(underCol, LaceLeafVein, kFrac * 0.55);
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
                m.AddTriangle(a + 1, b + 1, b);
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
    /// Builds a flat, horizontal fan spray of fine lacy leaves along a branchlet shelf.
    /// </summary>
    private static void LacemantleLacyFan(
        MeshData m,
        Vec3 tipPos,
        Vec3 tipDir,
        double scale,
        ulong seed,
        double[] col1,
        double[] col2,
        int detail)
    {
        var underColor = Primitives.Mix(col2, new[] { 0.44, 0.58, 0.38 }, 0.45);
        var topBaseColor = Primitives.Mix(col1, new[] { 0.16, 0.35, 0.20 }, 0.40);
        var topTipColor = Primitives.Mix(col1, new[] { 0.30, 0.48, 0.25 }, 0.35);

        var side = tipDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = tipDir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(tipDir).Normalized();

        int leaves = detail == 2 ? 6 : (detail == 1 ? 11 : 16);
        for (int l = 0; l < leaves; l++)
        {
            double frac = (l - (leaves - 1) * 0.5) / (leaves * 0.5);
            double fanAngle = frac * 1.15 + 0.10 * Math.Sin(l * 2.1);
            double lateral = Math.Sin(fanAngle);
            double forward = Math.Cos(fanAngle);

            var lDir = (side * lateral + tipDir * (forward * 0.92) + Vec3.Up * (0.03 * Math.Sin(l * 1.8) - 0.04)).Normalized();
            var lNorm = (Vec3.Up * 0.94 + side * (lateral * 0.30)).Normalized();

            double lLen = (0.090 + 0.018 * Rng.HashUnit(seed, (ulong)(l * 19 + 7), 0)) * scale;
            double lWid = lLen * 0.42;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 47 + 13));

            LacemantleLacyLeaf(m, tipPos, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail);
        }
    }

    /// <summary>
    /// Lacemantle procedural generator.
    /// Builds an elegant, tiered understory tree with dark sinuous trunk and distinct
    /// horizontal branch shelves carrying dense, delicate mantles of lacy foliage.
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
        // 1. Slender, Sinuous Dark Trunk
        // -------------------------------------------------------------------------
        double apexY = juvenile ? rng.Range(0.68, 0.72) : rng.Range(0.74, 0.77);
        var pFoot = Vec3.Zero;
        var pApex = Vec3.Up * apexY;

        double swayAz = rng.Range(0, Math.PI * 2);
        var swayDir = MidRadial(swayAz);
        var cTrunk0 = pFoot + Vec3.Up * (apexY * 0.35) + swayDir * rng.Range(0.015, 0.025);
        var cTrunk1 = pFoot + Vec3.Up * (apexY * 0.70) - swayDir * rng.Range(0.010, 0.020);

        int trunkStations = detail == 2 ? 8 : (detail == 1 ? 11 : 15);
        var trunkPoints = LacemantleSampleBezier(pFoot, cTrunk0, cTrunk1, pApex, trunkStations);
        var trunkRadii = new double[trunkStations];

        double rBase = juvenile ? rng.Range(0.022, 0.026) : rng.Range(0.028, 0.036);
        double rTip = Math.Max(0.005, rBase * 0.28);

        for (int i = 0; i < trunkStations; i++)
        {
            double t = i / (double)(trunkStations - 1);
            trunkRadii[i] = MathD.Lerp(rBase, rTip, Math.Pow(t, 0.85));
        }

        LacemantleStem(m, trunkPoints, trunkRadii, detail, ridges: 6);

        // -------------------------------------------------------------------------
        // 2. Terminal Apex Crown Canopy & Radiating Foliage
        // -------------------------------------------------------------------------
        int crownStartVertex = m.VertexCount;
        // (a) Radiating horizontal twigs crowning the leader apex
        int crownTwigs = detail == 2 ? 3 : (juvenile ? 3 : 5);
        double crownAzBase = rng.Range(0, Math.PI * 2);
        for (int ct = 0; ct < crownTwigs; ct++)
        {
            double twigAz = crownAzBase + ct * (2.0 * Math.PI / crownTwigs) + rng.Range(-0.15, 0.15);
            var twigOut = MidRadial(twigAz);
            double twigReach = (juvenile ? 0.16 : 0.24) * rng.Range(0.90, 1.10);
            var twigEnd = pApex + twigOut * twigReach + Vec3.Up * rng.Range(0.010, 0.025);

            var cCrown0 = pApex + (twigOut * 0.60 + Vec3.Up * 0.25).Normalized() * (twigReach * 0.40);
            var cCrown1 = twigEnd - (twigOut * 0.90 + Vec3.Up * 0.08).Normalized() * (twigReach * 0.35);

            int crownStations = detail == 2 ? 4 : 5;
            var crownPath = LacemantleSampleBezier(pApex, cCrown0, cCrown1, twigEnd, crownStations);
            var crownRadii = new[] { rTip * 0.95, rTip * 0.75, rTip * 0.55, rTip * 0.40, 0.002 };
            LacemantleStem(m, crownPath, crownRadii, detail, ridges: 3);

            var crownTipDir = LacemantleBezierTangent(pApex, cCrown0, cCrown1, twigEnd, 1.0);
            ulong crownFanSeed = Rng.Mix(seed, (ulong)(ct * 8191 + 555));
            LacemantleLacyFan(m, twigEnd, crownTipDir, scale: juvenile ? 0.85 : 1.05,
                crownFanSeed, c1, c2, detail);

            // Intermediate fan halfway along the crown twig for rich canopy density
            var sMid = LacemantleBezier(pApex, cCrown0, cCrown1, twigEnd, 0.50);
            var sMidTan = LacemantleBezierTangent(pApex, cCrown0, cCrown1, twigEnd, 0.50);
            ulong crownMidSeed = Rng.Mix(seed, (ulong)(ct * 12345 + 777));
            LacemantleLacyFan(m, sMid, sMidTan, scale: juvenile ? 0.75 : 0.90,
                crownMidSeed, c1, c2, detail);
        }

        // (b) Central horizontal lacy fans right across the leader apex
        for (int cf = 0; cf < (detail == 2 ? 1 : 2); cf++)
        {
            double fanAngle = crownAzBase + cf * (Math.PI * 0.55);
            var fanDir = MidRadial(fanAngle);
            ulong centerFanSeed = Rng.Mix(seed, (ulong)(99991 + cf * 313));
            LacemantleLacyFan(m, pApex, fanDir, scale: juvenile ? 0.85 : 1.05,
                centerFanSeed, c1, c2, detail);
        }

        // (c) Two-tier spreading umbrella rosette of broadleaf blades around the apex collar
        int apexLeaves = detail == 2 ? 8 : (juvenile ? 10 : (detail == 1 ? 14 : 20));
        var underColor = Primitives.Mix(c2, new[] { 0.44, 0.58, 0.38 }, 0.45);
        var topBaseColor = Primitives.Mix(c1, new[] { 0.16, 0.35, 0.20 }, 0.40);
        var topTipColor = Primitives.Mix(c1, new[] { 0.30, 0.48, 0.25 }, 0.35);

        for (int l = 0; l < apexLeaves; l++)
        {
            double leafAngle = 2.0 * Math.PI * l / apexLeaves + rng.Range(-0.10, 0.10);
            // Splay outward gracefully (pitch 55° to 78° from vertical) so it creates a lush, layered horizontal canopy
            double pitch = (l % 2 == 0) ? rng.Range(0.95, 1.15) : rng.Range(1.20, 1.38);
            var rad = MidRadial(leafAngle);
            var lDir = (rad * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var lNorm = (Vec3.Up * 0.85 - rad * 0.45).Normalized();

            double bladeLen = (juvenile ? rng.Range(0.075, 0.095) : rng.Range(0.095, 0.125));
            double bladeWid = bladeLen * 0.42;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 37 + 101));

            LacemantleLacyLeaf(m, pApex, lDir, lNorm, bladeLen, bladeWid, lSeed,
                topBaseColor, topTipColor, underColor, detail);
        }

        // Protect apex crown foliage from ever being pruned away at distance/LOD tiers
        if (m.VertexCount > crownStartVertex)
        {
            m.EssentialSpans.Add((crownStartVertex, m.VertexCount - crownStartVertex));
        }

        // -------------------------------------------------------------------------
        // 3. Regular Horizontal Tier Shelves (120° Clockwise Rotation)
        // -------------------------------------------------------------------------
        int numTiers = juvenile ? 4 : (detail == 2 ? 4 : 5 + rng.NextInt(2));
        double yStart = juvenile ? 0.20 : 0.18;
        double yEnd = juvenile ? 0.58 : 0.65;
        double baseAz = rng.Range(0, Math.PI * 2);

        for (int tier = 0; tier < numTiers; tier++)
        {
            double tTier = tier / (double)(numTiers - 1);
            double yTier = MathD.Lerp(yStart, yEnd, tTier);

            double sT = Math.Clamp(yTier / apexY, 0.0, 1.0);
            var tierTrunkPos = LacemantleBezier(pFoot, cTrunk0, cTrunk1, pApex, sT);

            double tierAz = baseAz - tier * (2.0 * Math.PI / 3.0);
            double tierReach = (juvenile ? 0.60 : 0.84) * MathD.Lerp(1.05, 0.65, Math.Pow(tTier, 0.80));

            int branchesInTier = 2;
            for (int b = 0; b < branchesInTier; b++)
            {
                double bAz = tierAz + (b == 0 ? 0 : Math.PI * 0.85) + rng.Range(-0.15, 0.15);
                var outward = MidRadial(bAz);
                var ortho = MidRadial(bAz + Math.PI * 0.5);

                double branchReach = tierReach * rng.Range(0.92, 1.08);

                // Scaffold branch: emerges upward, flattens horizontally into shelf
                var pBranchEnd = tierTrunkPos + outward * branchReach + ortho * rng.Range(-0.04, 0.04) + Vec3.Up * rng.Range(0.02, 0.05);
                var cBranch0 = tierTrunkPos + (outward * 0.50 + Vec3.Up * 0.40).Normalized() * (branchReach * 0.45);
                var cBranch1 = pBranchEnd - (outward * 0.95 + Vec3.Up * 0.05).Normalized() * (branchReach * 0.40);

                int branchStations = detail == 2 ? 5 : (detail == 1 ? 7 : 9);
                var branchPath = LacemantleSampleBezier(tierTrunkPos, cBranch0, cBranch1, pBranchEnd, branchStations);
                var branchRadii = new double[branchStations];

                double branchBaseR = Math.Max(0.005, rBase * 0.42 * (1.0 - 0.30 * tTier));
                double branchTipR = Math.Max(0.0025, branchBaseR * 0.35);

                for (int i = 0; i < branchStations; i++)
                {
                    double st = i / (double)(branchStations - 1);
                    branchRadii[i] = MathD.Lerp(branchBaseR, branchTipR, st);
                }

                LacemantleStem(m, branchPath, branchRadii, detail, ridges: 4);

                // Terminal lacy fan spray at branch tip
                var tipDir = LacemantleBezierTangent(tierTrunkPos, cBranch0, cBranch1, pBranchEnd, 1.0);
                ulong termFanSeed = Rng.Mix(seed, (ulong)(tier * 10000 + b * 1000 + 1));
                LacemantleLacyFan(m, pBranchEnd, tipDir, scale: juvenile ? 0.85 : 1.05,
                    termFanSeed, c1, c2, detail);

                // Intermediate bilateral fan sprays along branch shelf
                int branchSprays = detail == 2 ? 1 : 2;
                for (int s = 0; s < branchSprays; s++)
                {
                    double sFrac = s == 0 ? 0.45 : 0.72;
                    var sPos = LacemantleBezier(tierTrunkPos, cBranch0, cBranch1, pBranchEnd, sFrac);
                    var sTan = LacemantleBezierTangent(tierTrunkPos, cBranch0, cBranch1, pBranchEnd, sFrac);

                    // Central spray right on the branch shelf
                    ulong midFanSeed = Rng.Mix(seed, (ulong)(tier * 30000 + b * 3000 + s * 300 + 5));
                    LacemantleLacyFan(m, sPos, sTan, scale: juvenile ? 0.75 : 0.92,
                        midFanSeed, c1, c2, detail);

                    // Lateral twigs spreading in the flat plane
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    {
                        double twigAz = bAz + sideSign * rng.Range(0.55, 0.80);
                        var twigOut = MidRadial(twigAz);
                        double twigReach = branchReach * rng.Range(0.24, 0.36);
                        var twigEnd = sPos + twigOut * twigReach + Vec3.Up * rng.Range(-0.01, 0.02);

                        var cTwig0 = sPos + (sTan * 0.45 + twigOut * 0.65).Normalized() * (twigReach * 0.40);
                        var cTwig1 = twigEnd - twigOut * (twigReach * 0.35);

                        int twigStations = detail == 2 ? 4 : 5;
                        var twigPath = LacemantleSampleBezier(sPos, cTwig0, cTwig1, twigEnd, twigStations);
                        var twigRadii = new[] { branchTipR * 1.1, branchTipR * 0.85, branchTipR * 0.65, branchTipR * 0.50, 0.002 };
                        LacemantleStem(m, twigPath, twigRadii, detail, ridges: 3);

                        var twigDir = LacemantleBezierTangent(sPos, cTwig0, cTwig1, twigEnd, 1.0);
                        ulong twigFanSeed = Rng.Mix(seed, (ulong)(tier * 20000 + b * 2000 + s * 200 + (sideSign > 0 ? 1 : 2)));
                        LacemantleLacyFan(m, twigEnd, twigDir, scale: juvenile ? 0.78 : 0.95,
                            twigFanSeed, c1, c2, detail);
                    }
                }
            }
        }
    }
}
