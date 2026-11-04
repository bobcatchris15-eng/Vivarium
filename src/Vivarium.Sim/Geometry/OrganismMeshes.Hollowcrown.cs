using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Hollowcrown (hollowcrown_tree) veteran pollard oak procedural geometry.
///
/// Distinct veteran oak pollard anatomy:
///   1. Bole architecture: A muscular, gnarly, deeply-fluted trunk with natural veteran oak/beech
///      silhouette: muscular basal flare, thick columnar mid-trunk, bulbous swollen bolling head
///      with knobby knuckles matching the scaffold limbs, and heavy root buttresses.
///   2. The Authentic Vulva-Shaped Cleft: An authentic vertical cleft spanning from a pinched
///      lower fourchette (yBot = boleHeight * 0.06) to a pinched upper commissure (yTop = boleHeight * 0.68)
///      with plump almond belly, subtle mid-upper waist inflection, and double-lipped callus wound-wood anatomy:
///        - Outer labia majora rolls converging into an overarching upper callus hood and fusing below
///          into a raised basal fourchette threshold lip cradled between front buttress roots.
///        - Distinct inter-labial sulcus crease with deep shadow AO.
///        - Soft, wavy inner labia minora folds tucked inside, shaded with warm living cambium / sapwood.
///        - 100% continuous non-aliased UV seam above and below the opening (no vertical texture streak).
///   3. Dedicated Fibrous Hollow Log Interior: Deep multi-frequency rot flutes, horizontal decay checks,
///      cavernous basal expansion, true finite-difference 3D inward normals, internal decayed heartwood
///      ribs and splintered septa, multi-ring rolled bolling top lip, and mounded humus floor.
///      Shaded with UV2.x = 3.0 (Decayed Wood / Callus / Hollow Log Interior) for rich fibrous wood and rot.
///   4. Organic Bryophyte Mats: Confluent, velvety bioluminescent moss cushions seated directly ON the inner wall
///      and nestled into the threshold fourchette and root crevices (UV2.x = 1.35, UV2.y = 1.0).
///   5. Pollard Bolling Joinery: 5 primary scaffold limbs launching directly from the swollen bolling knuckles
///      with wide fluted basal branch collars (1.58x flare), forking secondary/tertiary boughs with swollen
///      branch union collars, and Quercus lobed oak foliage sprays.
///   6. LOD & Essential Spans: PreserveUpperCanopyOnLod = true, essential cavity moss and scaffold
///      limbs registered in m.EssentialSpans.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Color Palette
    // ---------------------------------------------------------------------------------------------

    // Bark & weathered wood palette
    private static readonly double[] HollowBarkDeep     = { 0.09, 0.07, 0.06 }; // Deep furrow / crevice shadow
    private static readonly double[] HollowBarkDark     = { 0.16, 0.14, 0.12 }; // Weathered dark charcoal bark
    private static readonly double[] HollowBark         = { 0.30, 0.27, 0.23 }; // Grey-brown weathered oak cork
    private static readonly double[] HollowBarkPale     = { 0.50, 0.48, 0.42 }; // Lichen-mottled weather-side bark
    private static readonly double[] HollowBarkCrest    = { 0.60, 0.56, 0.48 }; // Bleached wood ridges & knots

    // Decayed heartwood, callus & living cambium palette
    private static readonly double[] HollowWoodRot      = { 0.19, 0.13, 0.08 }; // Decaying rotten punk heartwood
    private static readonly double[] HollowWoodWarm     = { 0.32, 0.22, 0.13 }; // Warm amber heartwood decay
    private static readonly double[] HollowWood         = { 0.46, 0.34, 0.22 }; // Living sapwood & callus body
    private static readonly double[] HollowWoodFresh    = { 0.58, 0.46, 0.30 }; // Pale callus margin highlight
    private static readonly double[] HollowCambiumRose   = { 0.50, 0.26, 0.16 }; // Living cambium rose-amber fold
    private static readonly double[] HollowCambiumAmber  = { 0.56, 0.38, 0.20 }; // Warm living inner labia cambium
    private static readonly double[] HollowSulcusShadow  = { 0.06, 0.04, 0.03 }; // Inter-labial sulcus deep crease

    // Velvety bioluminescent moss palette
    private static readonly double[] HollowMossBase     = { 0.13, 0.28, 0.15 }; // Deep velvety shaded moss base
    private static readonly double[] HollowMossMid      = { 0.26, 0.60, 0.31 }; // Saturated emerald velvety cushion
    private static readonly double[] HollowMossLit      = { 0.42, 0.96, 0.52 }; // Glowing mint-green cushion crest

    // Oak foliage palette
    private static readonly double[] HollowOakTopBase   = { 0.14, 0.25, 0.12 }; // Deep Quercus forest green
    private static readonly double[] HollowOakTopTip    = { 0.28, 0.46, 0.22 }; // Saturated summer green
    private static readonly double[] HollowOakMargin    = { 0.38, 0.56, 0.26 }; // Translucent lobe margin
    private static readonly double[] HollowOakUnder     = { 0.36, 0.50, 0.28 }; // Pale glaucous sage underside
    private static readonly double[] HollowOakMidrib    = { 0.50, 0.64, 0.36 }; // Pale prominent midrib vein

    // ---------------------------------------------------------------------------------------------
    // Spline & Geometry Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Evaluates a cubic Bézier curve at parameter t in [0, 1].</summary>
    private static Vec3 HollowcrownBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    /// <summary>Evaluates a cubic Bézier tangent vector at parameter t in [0, 1].</summary>
    private static Vec3 HollowcrownBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>Discretizes a cubic Bézier curve into smooth path stations.</summary>
    private static Vec3[] HollowcrownSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts[i] = HollowcrownBezier(p0, c0, c1, p1, t);
        }
        return pts;
    }

    private static Vec3 HollowcrownRadial(double az) => new Vec3(Math.Cos(az), 0, Math.Sin(az));

    // Canonical Hollowcrown orientation & bolling knuckle angles
    private const double HollowcrownOpenDir = Math.PI * 0.28;
    private const double HollowcrownBaseKnuckleAz = HollowcrownOpenDir + Math.PI * 0.20;

    private static double HollowcrownBollingKnuckleWeight(double th)
    {
        double sum = 0.0;
        for (int k = 0; k < 5; k++)
        {
            double kAz = HollowcrownBaseKnuckleAz + 2.0 * Math.PI * k / 5.0;
            double diff = Math.Abs(th - kAz);
            if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
            sum += Math.Exp(-Math.Pow(diff / 0.28, 2.0));
        }
        return sum;
    }

    private static double HollowcrownTopRimY(double th, double boleHeight)
    {
        double kw = HollowcrownBollingKnuckleWeight(th);
        return boleHeight * (0.91 + 0.11 * kw);
    }

    private static Vec3 HollowcrownTrunkCenter(double t, double boleHeight, double baseRadius, Vec3 basePoint)
    {
        double leanX = 0.05 * baseRadius * Math.Sin(Math.PI * t * 0.85);
        double leanZ = -0.04 * baseRadius * Math.Sin(Math.PI * t * 0.70);
        return basePoint + Vec3.Up * (boleHeight * t) + new Vec3(leanX, 0, leanZ);
    }

    private static double HollowcrownMeanOuterRadius(double t, double baseRadius)
    {
        double baseFlare = 1.0 + 0.35 * Math.Pow(Math.Max(0.0, 1.0 - t / 0.28), 2.2);
        double midColumn = 0.88 + 0.08 * MathD.SmoothStep(0.72, 1.0, t);
        return baseRadius * (baseFlare * 0.88 + midColumn * 0.12);
    }

    private static double HollowcrownOuterRadius(double t, double th, double baseRadius, double boleHeight)
    {
        double r = HollowcrownMeanOuterRadius(t, baseRadius);

        r *= 1.0 + 0.06 * Math.Sin(8.0 * th + 0.6 * t) * (0.35 + 0.65 * (1.0 - 0.35 * t));
        r *= 1.0 + 0.02 * Math.Sin(16.0 * th);

        // Muscular bolling head & swollen knuckles matching scaffold limbs
        if (t > 0.65)
        {
            double uBoll = MathD.SmoothStep(0.65, 1.0, t);
            double kw = HollowcrownBollingKnuckleWeight(th);
            r += baseRadius * (0.14 * uBoll + 0.34 * Math.Pow(uBoll, 1.5) * kw);
        }

        // Basal buttress flare flowing smoothly into wide root ridges
        if (t < 0.38)
        {
            double flareDecay = Math.Pow(1.0 - t / 0.38, 2.2);
            double buttressSum = 0.0;
            for (int b = 0; b < 6; b++)
            {
                double bAz = b switch {
                    0 => HollowcrownOpenDir + 0.78,
                    1 => HollowcrownOpenDir + Math.PI * 0.52,
                    2 => HollowcrownOpenDir + Math.PI * 0.94,
                    3 => HollowcrownOpenDir + Math.PI * 1.36,
                    4 => HollowcrownOpenDir + Math.PI * 1.66,
                    _ => HollowcrownOpenDir + 2.0 * Math.PI - 0.78
                };
                double diff = Math.Abs(th - bAz);
                if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
                double weight = (b == 0 || b == 5) ? 0.55 : 0.42;
                double cosLobe = Math.Max(0.0, Math.Cos(diff * 1.85));
                buttressSum += Math.Pow(cosLobe, 3.0) * weight;
            }
            r += baseRadius * buttressSum * flareDecay;
        }

        double y = t * boleHeight;
        double yTop = 0.68 * boleHeight;
        if (y >= yTop && y <= yTop + boleHeight * 0.14)
        {
            double diff = Math.Abs(th - HollowcrownOpenDir);
            if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
            double yDecay = Math.Pow(1.0 - (y - yTop) / (boleHeight * 0.14), 1.6);
            r += Math.Exp(-Math.Pow(diff / 0.25, 2.0)) * (baseRadius * 0.055) * yDecay;
        }

        double yBot = 0.06 * boleHeight;
        if (y <= yBot)
        {
            double diff = Math.Abs(th - HollowcrownOpenDir);
            if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
            double yDecay = Math.Pow(y / yBot, 1.4);
            r += Math.Exp(-Math.Pow(diff / 0.28, 2.0)) * (baseRadius * 0.045) * yDecay;
        }

        return r;
    }

    // ---------------------------------------------------------------------------------------------
    // Woody Stem Builder (Fluted branches with parallel transport frames)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Lofts a fluted woody branch or scaffold limb with parallel transport frames and bark fissures.
    /// </summary>
    private static void HollowcrownWoodStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        int ridges = 5,
        double twistTotal = 0.35)
    {
        int stations = points.Count;
        if (stations < 2 || radii.Count != stations) return;

        int segments = detail == 2 ? 8 : (detail == 1 ? 10 : 12);
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

            double twist = twistTotal * t;

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2.0 * Math.PI * u;

                double ridgeWave = Math.Cos(ridges * th + twist);
                double fluteMod = 1.0 + 0.12 * ridgeWave;
                double r = Math.Max(0.002, baseR * fluteMod);

                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                ringPositions[i, s] = center + radialDir * r;

                var baseCol = Primitives.Mix(HollowBarkDark, HollowBark, 0.40 + 0.60 * t);
                if (ridgeWave < -0.25)
                {
                    double f = (-ridgeWave - 0.25) / 0.75;
                    baseCol = Primitives.Mix(baseCol, HollowBarkDeep, f * 0.85);
                }
                else if (ridgeWave > 0.25)
                {
                    double c = (ridgeWave - 0.25) / 0.75;
                    baseCol = Primitives.Mix(baseCol, HollowBarkCrest, c * 0.65);
                }
                ringColors[i, s] = baseCol;
            }
        }

        for (int i = 0; i < stations; i++)
        {
            for (int s = 0; s <= segments; s++)
            {
                int sPrev = (s == 0) ? segments - 1 : s - 1;
                int sNext = (s == segments) ? 1 : s + 1;
                int iPrev = Math.Max(0, i - 1);
                int iNext = Math.Min(stations - 1, i + 1);

                var dCirc = ringPositions[i, sNext] - ringPositions[i, sPrev];
                var dLong = ringPositions[iNext, s] - ringPositions[iPrev, s];
                var surfNorm = dCirc.Cross(dLong).Normalized();

                var radialHint = ringPositions[i, s] - points[i];
                if (surfNorm.Dot(radialHint) < 0) surfNorm = -surfNorm;

                ringNormals[i, s] = surfNorm.LengthSq > 1e-6 ? surfNorm : radialHint.Normalized();
            }
            ringPositions[i, segments] = ringPositions[i, 0];
            ringNormals[i, segments] = ringNormals[i, 0];
            ringColors[i, segments] = ringColors[i, 0];
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

    // ---------------------------------------------------------------------------------------------
    // Genuine Watertight Hollow Bole with Vulva-Shaped Cleft & Cavity
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the authentic veteran oak bole:
    /// - Vulva-shaped side cleft with double-lipped callus wound roll (outer labia, sulcus, inner labia)
    /// - Continuous non-aliased UV seam above and below the opening
    /// - Deeply fluted, fibrous hollow log interior with finite-difference inward normals
    /// - Internal decayed heartwood ribs and splintered septa
    /// - Multi-ring rolled bolling top lip rolling over knuckles into hollow
    /// - Contoured woodland humus floor with confluent velvety moss mats
    /// - Muscular splayed buttresses framing the cleft threshold
    /// </summary>
    private static void HollowcrownBoleAndCavity(
        MeshData m,
        Vec3 basePoint,
        double boleHeight,
        double baseRadius,
        Rng rng,
        ulong seed,
        int detail)
    {
        const double openDir = HollowcrownOpenDir;
        const double doorMaxHalfAngle = 0.32; // Organic gothic-arch almond cleft (~36 degree max opening)
        double yBot = boleHeight * 0.06;      // Lower fourchette cleft (~0.17m above root collar)
        double yTop = boleHeight * 0.68;      // Upper apex commissure (~1.91m)
        double cleftH = yTop - yBot;

        int sectors = detail switch { 2 => 24, 1 => 36, _ => 48 };
        int botRings = detail switch { 2 => 5, 1 => 7, _ => 9 };
        int cleftRings = detail switch { 2 => 16, 1 => 22, _ => 32 };
        int topRings = detail switch { 2 => 10, 1 => 14, _ => 18 };
        int totalRings = botRings + cleftRings + topRings;
        int cSub = detail switch { 2 => 6, 1 => 8, _ => 12 };

        double furrowPhase = Rng.HashUnit(seed, 101, 0) * Math.PI * 2.0;
        double barkPhase = Rng.HashUnit(seed, 102, 0) * Math.PI * 2.0;

        double MeanOuterRadius(double t) => HollowcrownMeanOuterRadius(t, baseRadius);

        double OuterRadius(double t, double th) => HollowcrownOuterRadius(t, th, baseRadius, boleHeight);

        // Dedicated fibrous hollow log interior radius: multi-frequency fluting sculpturing rot relief
        double InnerRadius(double t, double th)
        {
            double rMean = MeanOuterRadius(t) - baseRadius * 0.25;
            // High-frequency longitudinal ridges and splintered heartwood septa
            double heartwoodRids = 0.09 * Math.Pow(Math.Abs(Math.Sin(14.0 * th + 0.5 * Math.Sin(4.0 * t))), 0.45)
                                 + 0.04 * Math.Cos(22.0 * th + 1.2 * Math.Sin(6.0 * t));
            double rotFlute = 0.10 * Math.Cos(6.0 * th + 0.4)
                            + 0.05 * Math.Sin(11.0 * th + 1.8 * t)
                            + 0.02 * Math.Sin(31.0 * th + 4.0 * t);
            double checks = 0.025 * Math.Sin(t * 26.0 + 2.0 * Math.Cos(4.0 * th));
            double basalRot = 0.10 * Math.Pow(Math.Max(0.0, 1.0 - t / 0.35), 1.8) * (1.0 + 0.4 * Math.Cos(3.0 * th));
            return Math.Max(0.10, rMean * (1.0 + rotFlute + heartwoodRids + checks + basalRot));
        }

        double VulvaHalfAngle(double u)
        {
            if (u <= 0.0 || u >= 1.0) return 0.0;
            // Smooth gothic-arch profile with C1 tangency at the commissures:
            // Eliminates diamond silhouette, gently asymmetrical belly, taper at both commissures
            return doorMaxHalfAngle * Math.Pow(Math.Sin(Math.PI * u), 1.35) * (1.0 + 0.28 * Math.Sin(Math.PI * (1.0 - u)) - 0.12 * Math.Sin(2.0 * Math.PI * u));
        }

        Vec3 TrunkCenter(double t) => HollowcrownTrunkCenter(t, boleHeight, baseRadius, basePoint);
        Vec3 CavernCenter(double t) => TrunkCenter(t);

        double GetYForRing(int i, double th)
        {
            if (i <= botRings)
                return yBot * (i / (double)botRings);
            if (i <= botRings + cleftRings)
                return yBot + cleftH * ((i - botRings) / (double)cleftRings);
            double topFrac = (i - botRings - cleftRings) / (double)topRings;
            return MathD.Lerp(yTop, HollowcrownTopRimY(th, boleHeight), topFrac);
        }

        // -------------------------------------------------------------------------
        // 1. Grid Stations along Trunk Perimeter
        // -------------------------------------------------------------------------
        var outerGrid = new int[totalRings + 1, sectors + 1];
        var innerGrid = new int[totalRings + 1, sectors + 1];
        var outerPos = new Vec3[totalRings + 1, sectors + 1];
        var innerPos = new Vec3[totalRings + 1, sectors + 1];
        var outerNorms = new Vec3[totalRings + 1, sectors + 1];
        var innerNorms = new Vec3[totalRings + 1, sectors + 1];

        // Compute 3D positions
        for (int i = 0; i <= totalRings; i++)
        {
            double uCleft = (i - botRings) / (double)cleftRings;
            double gap = (i >= botRings && i <= botRings + cleftRings) ? VulvaHalfAngle(uCleft) : 0.0;

            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                double uSec = j / (double)sectors;
                double th = startAngle + sweepAngle * uSec;
                double y = GetYForRing(i, th);
                double t = y / boleHeight;

                var radDir = HollowcrownRadial(th);
                double rO = OuterRadius(t, th);
                double rI = InnerRadius(t, th);

                outerPos[i, j] = TrunkCenter(t) + radDir * rO;
                innerPos[i, j] = CavernCenter(t) + radDir * rI;
            }
        }

        // Compute true 3D finite-difference normals
        for (int i = 0; i <= totalRings; i++)
        {
            int iPrev = Math.Max(0, i - 1);
            int iNext = Math.Min(totalRings, i + 1);

            double uCleft = (i - botRings) / (double)cleftRings;
            double gap = (i >= botRings && i <= botRings + cleftRings) ? VulvaHalfAngle(uCleft) : 0.0;
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                int jPrev = Math.Max(0, j - 1);
                int jNext = Math.Min(sectors, j + 1);

                double th = startAngle + sweepAngle * (j / (double)sectors);
                var radDir = HollowcrownRadial(th);

                // Outer normals (point outward away from trunk)
                var dCircO = outerPos[i, jNext] - outerPos[i, jPrev];
                var dLongO = outerPos[iNext, j] - outerPos[iPrev, j];
                var nO = dCircO.Cross(dLongO).Normalized();
                if (nO.Dot(radDir) < 0) nO = -nO;
                outerNorms[i, j] = nO.LengthSq > 1e-6 ? nO : radDir;

                // Inner normals (point inward into cavern center)
                var dCircI = innerPos[i, jNext] - innerPos[i, jPrev];
                var dLongI = innerPos[iNext, j] - innerPos[iPrev, j];
                var nI = dLongI.Cross(dCircI).Normalized();
                if (nI.Dot(radDir) > 0) nI = -nI;
                innerNorms[i, j] = nI.LengthSq > 1e-6 ? nI : -radDir;
            }
        }

        int startOuterVertex = m.VertexCount;

        // Add Outer Vertices (Bark: UV2.x = 2.0, non-aliased continuous UVs!)
        for (int i = 0; i <= totalRings; i++)
        {
            double uCleft = (i - botRings) / (double)cleftRings;
            double gap = (i >= botRings && i <= botRings + cleftRings) ? VulvaHalfAngle(uCleft) : 0.0;
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                double uSec = j / (double)sectors;
                double th = startAngle + sweepAngle * uSec;
                double y = outerPos[i, j].Y;
                double t = y / boleHeight;

                double weather = 0.5 + 0.5 * Math.Cos(th - barkPhase);
                var col = Primitives.Mix(HollowBarkDark, HollowBark, 0.45 + 0.40 * t);
                col = Primitives.Mix(col, HollowBarkPale, 0.35 * weather * (0.3 + 0.7 * t));
                double flute = Math.Sin(8.0 * th + furrowPhase);
                if (flute < -0.2) col = Primitives.Mix(col, HollowBarkDeep, 0.60 * (-flute - 0.2) / 0.8);
                else if (flute > 0.4) col = Primitives.Mix(col, HollowBarkCrest, 0.40 * (flute - 0.4) / 0.6);

                double arcMeters = (startAngle - openDir + sweepAngle * uSec) * baseRadius;
                outerGrid[i, j] = m.AddVertex(outerPos[i, j], outerNorms[i, j], col, 1.0, y, arcMeters, 2.0, 0.0);
            }
        }

        // Add Inner Cavern Vertices (UV2.x = 3.0: Decayed Heartwood / Callus / Hollow Log Interior)
        for (int i = 0; i <= totalRings; i++)
        {
            double uCleft = (i - botRings) / (double)cleftRings;
            double gap = (i >= botRings && i <= botRings + cleftRings) ? VulvaHalfAngle(uCleft) : 0.0;
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                double uSec = j / (double)sectors;
                double th = startAngle + sweepAngle * uSec;
                double y = innerPos[i, j].Y;
                double t = y / boleHeight;

                // High-frequency vertical wood-fiber striation and longitudinal rot cracks
                double grainNoise = Math.Sin(28.0 * th + 2.0 * Math.Sin(5.0 * t)) * 0.6
                                  + Math.Cos(42.0 * th - 3.0 * t) * 0.4;
                double creviceFissure = Math.Pow(Math.Abs(Math.Sin(14.0 * th + 0.5 * Math.Sin(4.0 * t))), 3.0);

                var charredRot = new double[] { 0.05, 0.035, 0.025 };
                var col = Primitives.Mix(HollowWoodRot, HollowWoodWarm, 0.45 + 0.40 * grainNoise);
                col = Primitives.Mix(col, HollowWoodFresh, 0.25 * Math.Max(0.0, grainNoise));
                col = Primitives.Mix(charredRot, col, 1.0 - 0.65 * creviceFissure);

                double cavityAo = (0.28 + 0.72 * t) * (0.65 + 0.35 * Math.Cos(th - openDir));
                col = Primitives.Mix(HollowBarkDeep, col, cavityAo);

                double circMeters = uSec * 2.0 * Math.PI * InnerRadius(t, th);
                innerGrid[i, j] = m.AddVertex(innerPos[i, j], innerNorms[i, j], col, 1.0, y, circMeters, 3.0, 0.0);
            }
        }

        // Register main trunk in Structural for LOD tests
        m.Structural.Add((startOuterVertex, m.VertexCount - startOuterVertex, sectors, baseRadius));

        // Triangulate Outer Shell (Faces OUTWARD away from trunk)
        for (int i = 0; i < totalRings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = outerGrid[i, j];
                int b = outerGrid[i, j + 1];
                int c = outerGrid[i + 1, j];
                int d = outerGrid[i + 1, j + 1];
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }
        }

        // Triangulate Inner Cavern Shell (Faces INWARD towards cavity center)
        for (int i = 0; i < totalRings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = innerGrid[i, j];
                int b = innerGrid[i, j + 1];
                int c = innerGrid[i + 1, j];
                int d = innerGrid[i + 1, j + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(b, c, d);
            }
        }

        // -------------------------------------------------------------------------
        // 2. Double-Lipped Vulva Callus Border (Outer Labia, Sulcus, Inner Labia)
        // -------------------------------------------------------------------------
        var leftCallus = new int[cleftRings + 1, cSub];
        var rightCallus = new int[cleftRings + 1, cSub];
        var leftCallusPos = new Vec3[cleftRings + 1, cSub];
        var rightCallusPos = new Vec3[cleftRings + 1, cSub];

        for (int k = 0; k <= cleftRings; k++)
        {
            int iRing = botRings + k;
            double uCleft = k / (double)cleftRings;
            double gap = VulvaHalfAngle(uCleft);

            // Left side (j = 0)
            var pOutL = outerPos[iRing, 0];
            var pInL = innerPos[iRing, 0];
            double thickL = (pOutL - pInL).Length;
            var inDirL = HollowcrownRadial(openDir + gap - Math.PI * 0.5);
            var outNormL = HollowcrownRadial(openDir + gap);

            // Right side (j = sectors)
            var pOutR = outerPos[iRing, sectors];
            var pInR = innerPos[iRing, sectors];
            double thickR = (pOutR - pInR).Length;
            var inDirR = HollowcrownRadial(openDir - gap + Math.PI * 0.5);
            var outNormR = HollowcrownRadial(openDir - gap);

            // Crucial: scale cleftBulge by gapScale so lips never cross through each other at the tips!
            double gapScale = Math.Min(1.0, gap / 0.16) * Math.Sin(Math.PI * uCleft);

            for (int s = 0; s < cSub; s++)
            {
                double frac = (s + 1) / (double)(cSub + 1);

                // Plump, rounded, non-self-intersecting double-labia cross-section:
                // Outer labium (labia majora): frac in [0.0, 0.45]
                double majoraRad = frac <= 0.45 ? 0.32 * Math.Sin(Math.PI * (frac / 0.45)) : 0.0;
                double majoraIn = frac <= 0.45 ? 0.28 * Math.Sin(Math.PI * (frac / 0.45)) : 0.0;

                // Inner labium (labia minora): frac in [0.45, 0.92]
                double uMinora = (frac - 0.45) / 0.47;
                double innerWave = Math.Pow(Math.Sin(Math.PI * uCleft), 0.75)
                                 * (0.75 + 0.35 * Math.Sin(Math.PI * Math.Pow(uCleft, 0.7)) + 0.08 * Math.Sin(5.0 * uCleft));
                double minoraRad = (frac > 0.45 && frac <= 0.92) ? 0.22 * Math.Sin(Math.PI * uMinora) * innerWave : 0.0;
                double minoraIn = (frac > 0.45 && frac <= 0.92) ? 0.38 * Math.Sin(Math.PI * uMinora) * innerWave : 0.0;

                // Upper clitoral-hood arch and central bud/peak
                double hoodSwell = uCleft > 0.75 ? 0.28 * Math.Pow((uCleft - 0.75) / 0.25, 1.8) * Math.Sin(Math.PI * frac) : 0.0;
                double hoodPeak = (uCleft >= 0.82 && uCleft <= 0.96)
                                ? 0.16 * Math.Sin(Math.PI * (uCleft - 0.82) / 0.14) * Math.Exp(-Math.Pow((frac - 0.50) / 0.18, 2.0)) : 0.0;

                // Left side
                double radSwellL = (majoraRad + minoraRad + hoodSwell) * thickL;
                double cleftBulgeL = ((majoraIn + minoraIn) * gapScale + hoodPeak * gapScale) * thickL;
                leftCallusPos[k, s] = Vec3.Lerp(pOutL, pInL, frac) + outNormL * radSwellL + inDirL * cleftBulgeL;

                // Right side (slight organic asymmetry scaled by gapScale)
                double radSwellR = (majoraRad + minoraRad * 0.94 + hoodSwell) * thickR;
                double cleftBulgeR = ((majoraIn + minoraIn * 0.94) * gapScale + hoodPeak * gapScale) * thickR;
                rightCallusPos[k, s] = Vec3.Lerp(pOutR, pInR, frac) + outNormR * radSwellR + inDirR * cleftBulgeR;
            }
        }

        // Add callus vertices with finite-difference normals and smooth C-infinity Gaussian colors
        for (int k = 0; k <= cleftRings; k++)
        {
            int iRing = botRings + k;
            double uCleft = k / (double)cleftRings;
            double gap = VulvaHalfAngle(uCleft);
            double yL = outerPos[iRing, 0].Y;
            double yR = outerPos[iRing, sectors].Y;

            int kPrev = Math.Max(0, k - 1);
            int kNext = Math.Min(cleftRings, k + 1);

            var inDirL = HollowcrownRadial(openDir + gap - Math.PI * 0.5);
            var outNormL = HollowcrownRadial(openDir + gap);
            var inDirR = HollowcrownRadial(openDir - gap + Math.PI * 0.5);
            var outNormR = HollowcrownRadial(openDir - gap);

            double cambiumMix = Math.Sin(Math.PI * uCleft);
            var livingCambium = Primitives.Mix(HollowCambiumRose, HollowCambiumAmber, cambiumMix);
            livingCambium = Primitives.Mix(livingCambium, HollowWoodFresh, 0.28);

            for (int s = 0; s < cSub; s++)
            {
                double frac = (s + 1) / (double)(cSub + 1);

                // Smooth continuous Gaussian color blending
                double wBark = Math.Exp(-Math.Pow(frac / 0.14, 2.0));
                double wOuterCrest = Math.Exp(-Math.Pow((frac - 0.22) / 0.12, 2.0));
                double wSulcus = Math.Exp(-Math.Pow((frac - 0.41) / 0.07, 2.0));
                double wInnerCrest = Math.Exp(-Math.Pow((frac - 0.64) / 0.13, 2.0));
                double wRot = Math.Pow(Math.Max(0.0, (frac - 0.55) / 0.45), 2.2);

                double sumW = wBark + wOuterCrest + wSulcus + wInnerCrest + wRot + 1e-6;
                var col = new double[3];
                for (int c = 0; c < 3; c++)
                {
                    col[c] = (wBark * HollowBark[c]
                            + wOuterCrest * HollowWoodFresh[c]
                            + wSulcus * HollowSulcusShadow[c]
                            + wInnerCrest * livingCambium[c]
                            + wRot * HollowWoodRot[c]) / sumW;
                }
                double u2x = frac < 0.28 ? 2.0 : 3.0;

                // Left callus normal
                var pPrevSL = (s == 0) ? outerPos[iRing, 0] : leftCallusPos[k, s - 1];
                var pNextSL = (s == cSub - 1) ? innerPos[iRing, 0] : leftCallusPos[k, s + 1];
                var dSL = pNextSL - pPrevSL;
                var dKL = leftCallusPos[kNext, s] - leftCallusPos[kPrev, s];
                var nL = dSL.Cross(dKL).Normalized();
                if (nL.Dot(outNormL + inDirL * 0.5) < 0) nL = -nL;
                if (nL.LengthSq < 1e-6) nL = outNormL;

                leftCallus[k, s] = m.AddVertex(leftCallusPos[k, s], nL, col, 1.0, yL, frac, u2x, 0.0);

                // Right callus normal
                var pPrevSR = (s == 0) ? outerPos[iRing, sectors] : rightCallusPos[k, s - 1];
                var pNextSR = (s == cSub - 1) ? innerPos[iRing, sectors] : rightCallusPos[k, s + 1];
                var dSR = pNextSR - pPrevSR;
                var dKR = rightCallusPos[kNext, s] - rightCallusPos[kPrev, s];
                var nR = dKR.Cross(dSR).Normalized();
                if (nR.Dot(outNormR + inDirR * 0.5) < 0) nR = -nR;
                if (nR.LengthSq < 1e-6) nR = outNormR;

                rightCallus[k, s] = m.AddVertex(rightCallusPos[k, s], nR, col, 1.0, yR, frac, u2x, 0.0);
            }
        }

        // Triangulate left callus roll strip
        for (int k = 0; k < cleftRings; k++)
        {
            int iRing = botRings + k;
            m.AddTriangle(outerGrid[iRing, 0], outerGrid[iRing + 1, 0], leftCallus[k, 0]);
            m.AddTriangle(outerGrid[iRing + 1, 0], leftCallus[k + 1, 0], leftCallus[k, 0]);

            for (int s = 0; s < cSub - 1; s++)
            {
                int a = leftCallus[k, s];
                int b = leftCallus[k, s + 1];
                int c = leftCallus[k + 1, s];
                int d = leftCallus[k + 1, s + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(c, d, b);
            }

            int lastS = cSub - 1;
            m.AddTriangle(leftCallus[k, lastS], leftCallus[k + 1, lastS], innerGrid[iRing, 0]);
            m.AddTriangle(leftCallus[k + 1, lastS], innerGrid[iRing + 1, 0], innerGrid[iRing, 0]);
        }

        // Triangulate right callus roll strip
        for (int k = 0; k < cleftRings; k++)
        {
            int iRing = botRings + k;
            m.AddTriangle(outerGrid[iRing, sectors], rightCallus[k, 0], outerGrid[iRing + 1, sectors]);
            m.AddTriangle(outerGrid[iRing + 1, sectors], rightCallus[k, 0], rightCallus[k + 1, 0]);

            for (int s = 0; s < cSub - 1; s++)
            {
                int a = rightCallus[k, s];
                int b = rightCallus[k, s + 1];
                int c = rightCallus[k + 1, s];
                int d = rightCallus[k + 1, s + 1];
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }

            int lastS = cSub - 1;
            m.AddTriangle(rightCallus[k, lastS], rightCallus[k + 1, lastS], innerGrid[iRing, sectors]);
            m.AddTriangle(rightCallus[k + 1, lastS], innerGrid[iRing + 1, sectors], innerGrid[iRing, sectors]);
        }

        // At k = 0 (fourchette threshold) and k = cleftRings (upper commissure hood apex),
        // gap == 0 and gapScale == 0, so leftCallus and rightCallus are authored at the exact same 3D curve
        // between pOut and pIn. They meet edge-to-edge seamlessly without overlapping double-face fan caps.

        // -------------------------------------------------------------------------
        // 3. Pollard Bolling Rolled Rim at Top of Bole (Multi-ring curved lip)
        // -------------------------------------------------------------------------
        int rSub = detail == 2 ? 2 : (detail == 1 ? 3 : 4);
        var bollingGrid = new int[rSub + 1, sectors + 1];

        for (int j = 0; j <= sectors; j++)
        {
            bollingGrid[0, j] = outerGrid[totalRings, j];
            bollingGrid[rSub, j] = innerGrid[totalRings, j];
        }

        for (int r = 1; r < rSub; r++)
        {
            double frac = r / (double)rSub;
            double domeH = 0.055 * Math.Sin(Math.PI * frac); // Smooth upward roll
            for (int j = 0; j <= sectors; j++)
            {
                var pO = outerPos[totalRings, j];
                var pI = innerPos[totalRings, j];
                var p = Vec3.Lerp(pO, pI, frac) + Vec3.Up * domeH;

                double th = openDir + 2.0 * Math.PI * (j / (double)sectors);
                var radDir = HollowcrownRadial(th);
                var n = (radDir * Math.Cos(Math.PI * frac) + Vec3.Up * Math.Sin(Math.PI * frac)).Normalized();

                var col = frac < 0.4
                    ? Primitives.Mix(HollowBark, HollowWoodFresh, frac * 2.5)
                    : Primitives.Mix(HollowWoodFresh, HollowWoodRot, (frac - 0.4) * 1.66);

                double u2x = frac < 0.35 ? 2.0 : 3.0;
                bollingGrid[r, j] = m.AddVertex(p, n, col, 1.0, boleHeight, th * baseRadius, u2x, 0.0);
            }
        }

        for (int r = 0; r < rSub; r++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = bollingGrid[r, j];
                int b = bollingGrid[r, j + 1];
                int c = bollingGrid[r + 1, j];
                int d = bollingGrid[r + 1, j + 1];
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }
        }

        // -------------------------------------------------------------------------
        // 4. Contoured Cavern Basin Floor (Rises to meet fourchette threshold)
        // -------------------------------------------------------------------------
        int floorRings = detail == 2 ? 3 : (detail == 1 ? 4 : 5);
        int floorSectors = sectors;
        var floorGrid = new int[floorRings + 1, floorSectors + 1];

        var cFloor = CavernCenter(0.0) + Vec3.Up * 0.015;

        for (int fr = 0; fr <= floorRings; fr++)
        {
            double rFrac = fr / (double)floorRings;

            for (int fs = 0; fs <= floorSectors; fs++)
            {
                if (fr == floorRings)
                {
                    floorGrid[fr, fs] = innerGrid[0, fs];
                }
                else
                {
                    double uSec = fs / (double)floorSectors;
                    double th = openDir + 2.0 * Math.PI * uSec;

                    double rCur = InnerRadius(0.0, th) * rFrac;
                    double thresholdLift = yBot * 0.88 * Math.Max(0.0, Math.Cos(th - openDir));
                    double dishY = 0.012 + (thresholdLift + 0.02 * Math.Sin(4.0 * th)) * rFrac;

                    var p = cFloor + Vec3.Up * dishY + HollowcrownRadial(th) * rCur;
                    var n = (Vec3.Up * 0.95 + HollowcrownRadial(th) * (0.20 * rFrac)).Normalized();

                    double humusNoise = Math.Sin(3.0 * p.X * 12.0) * Math.Cos(3.0 * p.Z * 12.0);
                    var col = Primitives.Mix(HollowBarkDeep, HollowWoodRot, 0.40 + 0.30 * humusNoise);

                    double uvX = (p.X - basePoint.X) * 1.2 + 0.5;
                    double uvY = (p.Z - basePoint.Z) * 1.2 + 0.5;

                    floorGrid[fr, fs] = m.AddVertex(p, n, col, 1.0, uvX, uvY, 3.0, 0.0);
                }
            }
        }

        for (int fr = 0; fr < floorRings; fr++)
        {
            for (int fs = 0; fs < floorSectors; fs++)
            {
                int a = floorGrid[fr, fs];
                int b = floorGrid[fr, fs + 1];
                int c = floorGrid[fr + 1, fs];
                int d = floorGrid[fr + 1, fs + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(b, c, d);
            }
        }

        // -------------------------------------------------------------------------
        // 5. Confluent Velvet Bryophyte Cushions (Hugging cavity wall crevices)
        // -------------------------------------------------------------------------
        int mossStartVertex = m.VertexCount;

        // (a) Fourchette threshold cushion: radial disc seated flush on threshold lip
        {
            var pThresh = Vec3.Lerp(outerPos[botRings, 0], innerPos[botRings, 0], 0.55);
            var nThresh = (HollowcrownRadial(openDir) * 0.45 + Vec3.Up * 0.89).Normalized();
            var tThresh = HollowcrownRadial(openDir + Math.PI * 0.5);
            var bThresh = nThresh.Cross(tThresh).Normalized();

            int tRings = detail == 2 ? 3 : 4;
            int tSectors = detail == 2 ? 8 : (detail == 1 ? 10 : 12);

            double radX = baseRadius * 0.12;
            double radY = baseRadius * 0.08;
            double bulge = baseRadius * 0.035;

            // Center vertex
            var pCenter = pThresh + nThresh * bulge;
            var colCenter = Primitives.Mix(HollowMossMid, HollowMossLit, 0.90);
            int threshCenterIdx = m.AddVertex(pCenter, nThresh, colCenter, 1.0, 0.5, 0.5, 1.35, 1.0);

            var tGrid = new int[tRings + 1, tSectors];

            for (int r = 1; r <= tRings; r++)
            {
                double rFrac = r / (double)tRings;
                double dome = Math.Max(0.0, 1.0 - rFrac * rFrac);
                double bAmt = Math.Pow(dome, 0.75) * bulge;

                for (int s = 0; s < tSectors; s++)
                {
                    double ang = s * (2.0 * Math.PI / tSectors);
                    double vx = Math.Cos(ang) * rFrac;
                    double vy = Math.Sin(ang) * rFrac;

                    var p = pThresh + tThresh * (vx * radX) + bThresh * (vy * radY) + nThresh * bAmt;
                    var n = (nThresh * (0.65 + 0.35 * dome) + tThresh * (vx * 0.3) + bThresh * (vy * 0.3)).Normalized();

                    var col = Primitives.Mix(HollowMossBase, HollowMossMid, dome);
                    col = Primitives.Mix(col, HollowMossLit, Math.Pow(dome, 1.8) * 0.90);
                    col = Primitives.Mix(HollowWoodRot, col, MathD.SmoothStep(0.0, 0.20, dome));

                    tGrid[r, s] = m.AddVertex(p, n, col, 1.0, 0.5 + vx * 0.45, 0.5 + vy * 0.45, 1.35, 1.0);
                }
            }

            for (int s = 0; s < tSectors; s++)
            {
                int nextS = (s + 1) % tSectors;
                m.AddTriangle(threshCenterIdx, tGrid[1, nextS], tGrid[1, s]);
            }

            for (int r = 1; r < tRings; r++)
            {
                for (int s = 0; s < tSectors; s++)
                {
                    int nextS = (s + 1) % tSectors;
                    int a = tGrid[r, s];
                    int b = tGrid[r, nextS];
                    int cIdx = tGrid[r + 1, s];
                    int d = tGrid[r + 1, nextS];
                    m.AddTriangle(a, b, cIdx);
                    m.AddTriangle(b, d, cIdx);
                }
            }
        }

        // (b) Velvet moss cushions: lush vertical creeping bryophyte mats climbing damp lower cavity walls
        int numCushions = detail == 2 ? 5 : (detail == 1 ? 7 : 8);
        for (int c = 0; c < numCushions; c++)
        {
            double cFrac = (c + 0.5) / numCushions;
            double cTh = (openDir + Math.PI) + (cFrac - 0.5) * Math.PI * 0.72;
            // Clustered along damp lower cavity walls and floor niches
            double cT = 0.04 + 0.20 * cFrac + 0.04 * Rng.HashUnit(seed, (ulong)(c * 47 + 13), 0);

            // Vertically elongated creeping bryophyte mats
            double cRadX = baseRadius * (0.08 + 0.03 * Rng.HashUnit(seed, (ulong)(c * 67 + 2), 0));
            double cRadY = baseRadius * (0.15 + 0.05 * Rng.HashUnit(seed, (ulong)(c * 67 + 3), 0));
            double cBulge = baseRadius * 0.038;

            int numRings = detail == 2 ? 3 : 4;
            int numSectors = detail == 2 ? 8 : (detail == 1 ? 10 : 12);

            // Center vertex
            double wallRCtr = InnerRadius(cT, cTh);
            var wallCtr = CavernCenter(cT) + HollowcrownRadial(cTh) * wallRCtr;
            var normCtr = -HollowcrownRadial(cTh);
            var pCtr = wallCtr + normCtr * cBulge;
            var colCtr = Primitives.Mix(HollowMossMid, HollowMossLit, 0.90);
            int ctrIdx = m.AddVertex(pCtr, normCtr, colCtr, 1.0, 0.5, 0.5, 1.35, 1.0);

            var padGrid = new int[numRings + 1, numSectors];

            for (int r = 1; r <= numRings; r++)
            {
                double rFrac = r / (double)numRings;
                double dome = Math.Max(0.0, 1.0 - rFrac * rFrac);
                double bulgeAmt = Math.Pow(dome, 0.75) * cBulge;

                for (int s = 0; s < numSectors; s++)
                {
                    double ang = s * (2.0 * Math.PI / numSectors);
                    double rLimit = 1.0 + 0.20 * Math.Sin(3.0 * ang + c) + 0.12 * Math.Cos(5.0 * ang);
                    double vx = Math.Cos(ang) * rLimit * rFrac;
                    double vy = Math.Sin(ang) * rLimit * rFrac;

                    double dTh = (vx * cRadX) / Math.Max(0.1, InnerRadius(cT, cTh));
                    double tV = Math.Clamp(cT + (vy * cRadY) / boleHeight, 0.015, 0.65);
                    double thV = cTh + dTh;

                    var wPos = CavernCenter(tV) + HollowcrownRadial(thV) * InnerRadius(tV, thV);
                    var wNorm = -HollowcrownRadial(thV);

                    // Perimeter vertices (rFrac = 1.0, dome = 0, bulgeAmt = 0) sit exactly ON the wall surface
                    var p = wPos + wNorm * bulgeAmt;
                    var n = (wNorm * (0.6 + 0.4 * dome)
                           - HollowcrownRadial(thV + Math.PI * 0.5) * (vx * 0.3)
                           + Vec3.Up * (vy * 0.3)).Normalized();

                    var col = Primitives.Mix(HollowMossBase, HollowMossMid, dome);
                    col = Primitives.Mix(col, HollowMossLit, Math.Pow(dome, 2.0) * 0.95);
                    col = Primitives.Mix(HollowWoodRot, col, MathD.SmoothStep(0.0, 0.18, dome));

                    padGrid[r, s] = m.AddVertex(p, n, col, 1.0, 0.5 + vx * 0.45, 0.5 + vy * 0.45, 1.35, 1.0);
                }
            }

            // Triangulate center fan
            for (int s = 0; s < numSectors; s++)
            {
                int nextS = (s + 1) % numSectors;
                m.AddTriangle(ctrIdx, padGrid[1, nextS], padGrid[1, s]);
            }

            // Triangulate concentric rings
            for (int r = 1; r < numRings; r++)
            {
                for (int s = 0; s < numSectors; s++)
                {
                    int nextS = (s + 1) % numSectors;
                    int a = padGrid[r, s];
                    int b = padGrid[r, nextS];
                    int cIdx = padGrid[r + 1, s];
                    int d = padGrid[r + 1, nextS];
                    m.AddTriangle(a, b, cIdx);
                    m.AddTriangle(b, d, cIdx);
                }
            }
        }

        int mossCount = m.VertexCount - mossStartVertex;
        if (mossCount > 0)
        {
            m.EssentialSpans.Add((mossStartVertex, mossCount));
        }

        // -------------------------------------------------------------------------
        // 6. Muscular Surface Anchor Roots Flowing from Buttress Flanks
        // -------------------------------------------------------------------------
        int rootRunners = detail == 2 ? 4 : (detail == 1 ? 5 : 6);
        for (int r = 0; r < rootRunners; r++)
        {
            double rAz = (r switch {
                0 => openDir + 0.78,
                1 => openDir + Math.PI * 0.52,
                2 => openDir + Math.PI * 0.94,
                3 => openDir + Math.PI * 1.36,
                4 => openDir + Math.PI * 1.66,
                _ => openDir + 2.0 * Math.PI - 0.78
            }) + rng.Range(-0.06, 0.06);

            var rDir = HollowcrownRadial(rAz);
            var rOrtho = HollowcrownRadial(rAz + Math.PI * 0.5);

            double rootLen = baseRadius * rng.Range(0.95, 1.30);
            double yLaunch = boleHeight * 0.12;
            double rLaunch = OuterRadius(0.12, rAz) * 0.60; // Deep inside trunk buttress core
            var pCenterLaunch = TrunkCenter(0.12);
            var pRootStart = new Vec3(pCenterLaunch.X + rDir.X * rLaunch, yLaunch, pCenterLaunch.Z + rDir.Z * rLaunch);
            double rToe = OuterRadius(0.0, rAz) * 1.30;
            var pRootEnd = basePoint + rDir * rToe + rOrtho * rng.Range(-0.12, 0.12) * baseRadius - Vec3.Up * 0.015;

            // Travel downward inside buttress flute first, then sweep smoothly outward along soil bed
            var cRoot0 = pRootStart - Vec3.Up * (boleHeight * 0.08) + rDir * (baseRadius * 0.12);
            var cRoot1 = pRootEnd - (rDir * 0.92 + Vec3.Up * 0.10).Normalized() * (rootLen * 0.42);

            int rootStations = detail == 2 ? 5 : 6;
            var rootPath = HollowcrownSampleBezier(pRootStart, cRoot0, cRoot1, pRootEnd, rootStations);
            var rootRadii = new double[rootStations];
            double rStart = baseRadius * rng.Range(0.18, 0.22);
            double rEnd = baseRadius * 0.075; // Thick muscular root sinking into soil

            for (int s = 0; s < rootStations; s++)
            {
                double st = s / (double)(rootStations - 1);
                rootRadii[s] = MathD.Lerp(rStart, rEnd, Math.Pow(st, 0.65));
            }

            HollowcrownWoodStem(m, rootPath, rootRadii, detail, ridges: 3, twistTotal: 0.15);
        }
    }

        // Quercus Lobed Oak Leaf Geometry
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a thin, distinctively lobed Quercus oak leaf with authentic 3D camber,
    /// 3 pairs of prominent rounded lobes, deep sinuses, basal auricles, raised midrib,
    /// and double-sided shader lighting.
    /// </summary>
    private static void HollowcrownOakLeaf(
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

        int rows = detail == 2 ? 7 : (detail == 1 ? 9 : 12);
        int cols = detail == 2 ? 3 : 5;

        var side = leafNormal.Cross(leafDir);
        if (side.LengthSq < 1e-6) side = leafDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(leafDir).Normalized();

        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.TriangleCount * 3;

        double OakWidthProfile(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t < 0.10) return 0.05 * (t / 0.10); // Slender petiole stalk

            ReadOnlySpan<double> keyT = [0.10, 0.14, 0.20, 0.35, 0.44, 0.58, 0.68, 0.82, 0.89, 0.95, 1.00];
            ReadOnlySpan<double> keyW = [0.05, 0.36, 0.18, 0.75, 0.26, 1.00, 0.32, 0.80, 0.36, 0.48, 0.00];

            for (int k = 0; k < keyT.Length - 1; k++)
            {
                if (t <= keyT[k + 1])
                {
                    double u = (t - keyT[k]) / (keyT[k + 1] - keyT[k]);
                    double smooth = 0.5 - 0.5 * Math.Cos(Math.PI * u);
                    return keyW[k] + (keyW[k + 1] - keyW[k]) * smooth;
                }
            }
            return 0.0;
        }

        Vec3 SampleSpine(double t)
        {
            double along = length * t;
            double arch = length * (0.045 * Math.Sin(Math.PI * t) - 0.075 * Math.Pow(t, 1.4));
            return basePos + leafDir * along + up * arch;
        }

        var gridVertices = new int[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            var pSpine = SampleSpine(t);

            double dt = 0.02;
            var pNext = SampleSpine(Math.Min(1.0, t + dt));
            var pPrev = SampleSpine(Math.Max(0.0, t - dt));
            var tSpine = (pNext - pPrev).Normalized();
            var sSpine = up.Cross(tSpine).Normalized();
            var nSpine = tSpine.Cross(sSpine).Normalized();

            double wProfile = OakWidthProfile(t);
            double halfW = width * 0.5 * wProfile;

            for (int c = 0; c < cols; c++)
            {
                double x = (c / (double)(cols - 1)) * 2.0 - 1.0;
                double absX = Math.Abs(x);

                double cup = 0.035 * width * (absX * absX);
                double lobeLift = (wProfile - 0.30) * (0.015 * length) * (absX * absX);
                double midribLift = (1.0 - absX) * (0.003 * length);

                var offsetSide = sSpine * (halfW * x);
                var offsetUp = nSpine * (cup + lobeLift + midribLift);
                var p = pSpine + offsetSide + offsetUp;

                var n = (nSpine * (0.90 + 0.10 * (1.0 - absX)) - sSpine * (x * 0.35)).Normalized();

                var bodyCol = Primitives.Mix(colTopBase, colTopTip, t);
                if (absX > 0.65)
                {
                    double marginFrac = (absX - 0.65) / 0.35;
                    bodyCol = Primitives.Mix(bodyCol, HollowOakMargin, marginFrac * 0.50);
                }
                else if (absX < 0.25)
                {
                    double midFrac = (0.25 - absX) / 0.25;
                    bodyCol = Primitives.Mix(bodyCol, HollowOakMidrib, midFrac * 0.40);
                }

                gridVertices[r, c] = m.AddVertex(p, n, bodyCol, 1.0, t, (x + 1.0) * 0.5, 0.0, 1.0);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int v00 = gridVertices[r, c];
                int v10 = gridVertices[r + 1, c];
                int v01 = gridVertices[r, c + 1];
                int v11 = gridVertices[r + 1, c + 1];

                m.AddTriangle(v00, v10, v01);
                m.AddTriangle(v01, v10, v11);
            }
        }

        m.RecordLeaf(leafStartVertex, leafStartIndex, basePos, length, volumetric: false);
    }

    /// <summary>
    /// Builds a natural Quercus oak branchlet spray with radiating rosette and lateral leaves.
    /// </summary>
    private static void HollowcrownOakSpray(
        MeshData m,
        Vec3 tipPos,
        Vec3 tipDir,
        double scale,
        ulong seed,
        double[] c1,
        double[] c2,
        int detail)
    {
        var underColor = Primitives.Mix(c2, HollowOakUnder, 0.50);
        var topBaseColor = Primitives.Mix(c1, HollowOakTopBase, 0.45);
        var topTipColor = Primitives.Mix(c1, HollowOakTopTip, 0.40);

        var fwd = tipDir.Normalized();
        var up = Vec3.Up;
        var side = fwd.Cross(up).Normalized();
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
        var norm = side.Cross(fwd).Normalized();

        int termLeaves = detail == 2 ? 4 : (detail == 1 ? 6 : 8);
        for (int l = 0; l < termLeaves; l++)
        {
            double angle = 2.0 * Math.PI * l / termLeaves + Rng.HashUnit(seed, (ulong)(l * 19 + 5), 0) * 0.25;
            double pitch = 0.52 + 0.35 * Rng.HashUnit(seed, (ulong)(l * 23 + 7), 0);
            var rad = (side * Math.Cos(angle) + norm * Math.Sin(angle)).Normalized();
            var lDir = (rad * Math.Sin(pitch) + fwd * Math.Cos(pitch)).Normalized();
            var lNorm = (Vec3.Up * 0.80 + rad * 0.40).Normalized();

            double lLen = (0.24 + 0.04 * Rng.HashUnit(seed, (ulong)(l * 31 + 1), 0)) * scale;
            double lWid = lLen * 0.52;
            ulong lSeed = Rng.Mix(seed, (ulong)(l * 101 + 3));

            HollowcrownOakLeaf(m, tipPos, lDir, lNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail);
        }

        int lateralLeaves = detail == 2 ? 3 : (detail == 1 ? 5 : 6);
        for (int ll = 0; ll < lateralLeaves; ll++)
        {
            double backFrac = (ll + 1) * (0.075 * scale);
            var atPos = tipPos - fwd * backFrac;

            double sideSign = (ll % 2 == 0) ? 1.0 : -1.0;
            double lAz = sideSign * (0.65 + 0.30 * Rng.HashUnit(seed, (ulong)(ll * 41 + 11), 0));
            var latDir = (side * Math.Sin(lAz) + fwd * (0.35 + 0.25 * Math.Cos(lAz)) - Vec3.Up * 0.12).Normalized();
            var latNorm = (Vec3.Up * 0.85 + side * (sideSign * 0.40)).Normalized();

            double lLen = (0.22 + 0.03 * Rng.HashUnit(seed, (ulong)(ll * 53 + 9), 0)) * scale;
            double lWid = lLen * 0.50;
            ulong lSeed = Rng.Mix(seed, (ulong)(ll * 211 + 17));

            HollowcrownOakLeaf(m, atPos, latDir, latNorm, lLen, lWid, lSeed,
                topBaseColor, topTipColor, underColor, detail);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Crown & Pollard Bolling Scaffold Limbs & Spreading Boughs
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the crown:
    /// - 5 Primary scaffold limbs launching directly from the swollen bolling knuckles with wide fluted branch collars
    /// - Smoothly forking secondary and tertiary boughs with swollen fork collars arching into a spreading canopy
    /// - High-density Quercus oak foliage sprays and epicormic rosettes forming a lush dome
    /// </summary>
    private static void HollowcrownCrown(
        MeshData m,
        Vec3 basePoint,
        double boleHeight,
        double baseRadius,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        int detail,
        bool juvenile)
    {
        int numLimbs = juvenile ? 4 : 5;
        double baseAz = HollowcrownBaseKnuckleAz;

        for (int k = 0; k < numLimbs; k++)
        {
            double lAz = baseAz + (2.0 * Math.PI * k / numLimbs) + rng.Range(-0.03, 0.03);
            var outward = HollowcrownRadial(lAz);
            var ortho = HollowcrownRadial(lAz + Math.PI * 0.5);

            // Scaffold limb launches strictly from INSIDE the swollen bolling knuckle wall
            double yKnuckle = HollowcrownTopRimY(lAz, boleHeight);
            double rKnuckle = (HollowcrownOuterRadius(0.96, lAz, baseRadius, boleHeight)
                             + (HollowcrownMeanOuterRadius(0.96, baseRadius) - baseRadius * 0.25)) * 0.50; // Centered in knuckle wall
            var pCenterKnuckle = HollowcrownTrunkCenter(0.94, boleHeight, baseRadius, basePoint);
            var pRoot = new Vec3(pCenterKnuckle.X + outward.X * rKnuckle, yKnuckle - boleHeight * 0.06, pCenterKnuckle.Z + outward.Z * rKnuckle);

            double limbReach = (juvenile ? 1.05 : 1.50) * rng.Range(0.92, 1.08);
            double limbRise = (juvenile ? 0.70 : 1.05) * rng.Range(0.88, 1.12);

            var pLimbEnd = pRoot + outward * limbReach + ortho * rng.Range(-0.15, 0.15) + Vec3.Up * limbRise;

            // Steep launch upward, graceful spreading arch outward
            var limbStartDir = (outward * 0.30 + Vec3.Up * 0.95).Normalized();
            var cLimb0 = pRoot + limbStartDir * (limbReach * 0.45);
            var limbEndDir = (outward * 0.85 + Vec3.Up * 0.35).Normalized();
            var cLimb1 = pLimbEnd - limbEndDir * (limbReach * 0.38);

            int limbStations = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
            var limbPath = HollowcrownSampleBezier(pRoot, cLimb0, cLimb1, pLimbEnd, limbStations);
            var limbRadii = new double[limbStations];

            // Sized to remain nested strictly within the swollen knuckle wall (zero protruding skirt)
            double limbBaseR = baseRadius * (juvenile ? 0.19 : 0.23);
            double limbTipR = limbBaseR * 0.34;

            for (int s = 0; s < limbStations; s++)
            {
                double st = s / (double)(limbStations - 1);
                // Modest basal collar flare that stays fully inside the knuckle
                double collarFlare = 1.0 + 0.22 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.25), 1.8);
                limbRadii[s] = MathD.Lerp(limbBaseR, limbTipR, Math.Pow(st, 0.80)) * collarFlare;
            }

            int limbStemStart = m.VertexCount;
            HollowcrownWoodStem(m, limbPath, limbRadii, detail, ridges: 4, twistTotal: 0.25);
            m.EssentialSpans.Add((limbStemStart, m.VertexCount - limbStemStart));

            // Terminal foliage spray at the end of the primary scaffold limb
            var limbTipDir = HollowcrownBezierTangent(pRoot, cLimb0, cLimb1, pLimbEnd, 1.0);
            ulong termSeed = Rng.Mix(seed, (ulong)(k * 10000 + 1));
            HollowcrownOakSpray(m, pLimbEnd, limbTipDir, scale: juvenile ? 0.90 : 1.12,
                termSeed, c1, c2, detail);

            // Knuckle rosette right at the bolling shoulder collar
            ulong knuckleSeed = Rng.Mix(seed, (ulong)(k * 60000 + 3));
            var knuckleDir = (outward * 0.75 + Vec3.Up * 0.45).Normalized();
            HollowcrownOakSpray(m, pRoot + knuckleDir * 0.12, knuckleDir, scale: 0.85, knuckleSeed, c1, c2, detail);

            // ---------------------------------------------------------------------
            // Secondary Forking Boughs
            // ---------------------------------------------------------------------
            int numBoughs = juvenile ? 2 : (detail == 2 ? 2 : 3);
            for (int b = 0; b < numBoughs; b++)
            {
                double bFrac = b switch { 0 => 0.40, 1 => 0.65, _ => 0.85 };
                var bRoot = HollowcrownBezier(pRoot, cLimb0, cLimb1, pLimbEnd, bFrac);
                var bMotherTan = HollowcrownBezierTangent(pRoot, cLimb0, cLimb1, pLimbEnd, bFrac);

                double sideSign = (b % 2 == 0) ? 1.0 : -1.0;
                double bAz = lAz + sideSign * rng.Range(0.45, 0.78);
                var bOut = HollowcrownRadial(bAz);

                double bReach = (juvenile ? 0.55 : 0.85) * rng.Range(0.85, 1.15);
                double bRise = (juvenile ? 0.25 : 0.45) * rng.Range(0.80, 1.20);
                var bEnd = bRoot + bOut * bReach + Vec3.Up * bRise;

                var bStartDir = (bMotherTan * 0.45 + bOut * 0.68 + Vec3.Up * 0.30).Normalized();
                var cB0 = bRoot + bStartDir * (bReach * 0.40);
                var bEndDir = (bOut * 0.85 + Vec3.Up * 0.25).Normalized();
                var cB1 = bEnd - bEndDir * (bReach * 0.35);

                int bStations = detail == 2 ? 5 : 7;
                var bPath = HollowcrownSampleBezier(bRoot, cB0, cB1, bEnd, bStations);
                var bRadii = new double[bStations];

                double bBaseR = limbBaseR * 0.50;
                double bTipR = Math.Max(0.005, bBaseR * 0.28);

                for (int s = 0; s < bStations; s++)
                {
                    double st = s / (double)(bStations - 1);
                    // Swollen fork branch collar
                    double bCollar = 1.0 + 0.38 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.20), 2.0);
                    bRadii[s] = MathD.Lerp(bBaseR, bTipR, st) * bCollar;
                }

                int bStemStart = m.VertexCount;
                HollowcrownWoodStem(m, bPath, bRadii, detail, ridges: 3, twistTotal: 0.20);
                m.EssentialSpans.Add((bStemStart, m.VertexCount - bStemStart));

                var bTipDir = HollowcrownBezierTangent(bRoot, cB0, cB1, bEnd, 1.0);
                ulong bSeed = Rng.Mix(seed, (ulong)(k * 20000 + b * 1000 + 7));
                HollowcrownOakSpray(m, bEnd, bTipDir, scale: juvenile ? 0.85 : 1.05,
                    bSeed, c1, c2, detail);

                // Intermediate foliage spray along secondary bough
                double midBFrac = 0.52;
                var bMidPos = HollowcrownBezier(bRoot, cB0, cB1, bEnd, midBFrac);
                var bMidTan = HollowcrownBezierTangent(bRoot, cB0, cB1, bEnd, midBFrac);
                ulong midSeed = Rng.Mix(seed, (ulong)(k * 30000 + b * 2000 + 13));
                HollowcrownOakSpray(m, bMidPos, bMidTan, scale: juvenile ? 0.80 : 0.98,
                    midSeed, c1, c2, detail);

                // Tertiary branchlets in high detail
                if (detail == 0 && !juvenile)
                {
                    double tertAz = bAz - sideSign * rng.Range(0.38, 0.62);
                    var tertOut = HollowcrownRadial(tertAz);
                    double tertReach = 0.40 * rng.Range(0.85, 1.15);
                    var tertEnd = bMidPos + tertOut * tertReach + Vec3.Up * rng.Range(0.08, 0.18);

                    var cTert0 = bMidPos + (bMidTan * 0.40 + tertOut * 0.70).Normalized() * (tertReach * 0.40);
                    var cTert1 = tertEnd - tertOut * (tertReach * 0.35);

                    var tertPath = HollowcrownSampleBezier(bMidPos, cTert0, cTert1, tertEnd, 5);
                    var tertRadii = new[] { bBaseR * 0.45 * 1.30, bBaseR * 0.38, bBaseR * 0.30, bBaseR * 0.22, 0.004 };
                    int tertStemStart = m.VertexCount;
                    HollowcrownWoodStem(m, tertPath, tertRadii, detail, ridges: 3, twistTotal: 0.15);
                    m.EssentialSpans.Add((tertStemStart, m.VertexCount - tertStemStart));

                    var tertDir = HollowcrownBezierTangent(bMidPos, cTert0, cTert1, tertEnd, 1.0);
                    ulong tertSeed = Rng.Mix(seed, (ulong)(k * 40000 + b * 3000 + 29));
                    HollowcrownOakSpray(m, tertEnd, tertDir, scale: 0.92, tertSeed, c1, c2, detail);
                }
            }

            // Epicormic leafy sprouts rising directly from the limb collar
            int epicormics = detail == 2 ? 1 : 2;
            for (int ep = 0; ep < epicormics; ep++)
            {
                double epFrac = 0.20 + ep * 0.18;
                var epRoot = HollowcrownBezier(pRoot, cLimb0, cLimb1, pLimbEnd, epFrac);
                var epDir = (Vec3.Up * 0.85 + outward * 0.30 + ortho * ((ep % 2 == 0 ? 1 : -1) * 0.30)).Normalized();

                ulong epSeed = Rng.Mix(seed, (ulong)(k * 50000 + ep * 500 + 41));
                HollowcrownOakSpray(m, epRoot + epDir * 0.16, epDir, scale: 0.85, epSeed, c1, c2, detail);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Main Hollowcrown Generator Entry Point
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The veteran oak pollard: buttressed, fluted, hollowed with fibrous punk heartwood & bioluminescent moss,
    /// authentic double-lipped vulva cleft, and crowned with spreading scaffold boughs and Quercus foliage.
    /// </summary>
    private static void Hollowcrown(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        double radius,
        double height,
        bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;

        var part = new MeshData
        {
            Lod = m.Lod,
            FloraDetailLevel = m.FloraDetailLevel,
            FloraVisualSeed = m.FloraVisualSeed,
            PreserveUpperCanopyOnLod = true
        };

        int detail = m.FloraDetailLevel ?? 0;

        // Trunk authored in real metres matching content definition (height: 4.4, radiusAtMax: 1.55)
        double boleHeight = height * (juvenile ? 0.58 : 0.64);
        double baseRadius = radius * (juvenile ? 0.28 : 0.38);

        // 1. Bole, fluted trunk, splayed buttresses, authentic vulva cleft, hollow log interior & moss
        HollowcrownBoleAndCavity(part, Vec3.Zero, boleHeight, baseRadius, rng, seed, detail);

        // 2. Pollard bolling head, Bézier scaffold limbs, spreading boughs & lobed Quercus oak foliage
        HollowcrownCrown(part, Vec3.Zero, boleHeight, baseRadius, rng, seed, c1, c2, detail, juvenile);

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;

        // Convert from authored world metres into engine unit frame
        NewFloraAppendWorld(m, part, radius, height);

        // Guarantee essential spans are transferred into target mesh m
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
        {
            foreach (var span in part.EssentialSpans)
            {
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
            }
        }
    }
}
