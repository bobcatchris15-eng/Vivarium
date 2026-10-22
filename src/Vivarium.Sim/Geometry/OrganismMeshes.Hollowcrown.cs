using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Hollowcrown (hollowcrown_tree) high-fidelity procedural geometry.
///
/// Distinct veteran oak pollard anatomy:
///   1. Bole architecture: A muscular, gnarly, deeply-fluted trunk with organic splayed root
///      buttresses that flare smoothly and powerfully into the ground, plus sinuous surface root runners.
///   2. The Great Hollow Portal: A wide, majestic Gothic arched doorway facing the front 3/4 perspective,
///      framed by a continuous, multi-segmented rounded callus wound roll of healing cambium.
///   3. Dedicated Interior Cavern: A spacious, smoothly concave heartwood chamber carved out of decayed
///      punk wood, shaded with dark cavern depth and open upward to the crown chimney.
///   4. Contoured Woodland Floor: A natural bed of dark forest humus and decaying punk wood with
///      organic, velvety bioluminescent moss cushions (UV2.x = 1.35f, UV2.y = 1f) clustered at the threshold
///      and wall crevices (no concentric puddles or radial stretching).
///   5. Pollard Bolling Head: A swollen, gnarled crown collar with muscular branch knuckles where
///      the scaffold limbs emerge, opening into a hollow chimney.
///   6. Quercus Oak Foliage: True lobed oak blades with 3D camber, rounded lobes, deep sinuses,
///      and pale midribs arranged in dense epicormic sprays forming a lush, majestic canopy dome.
///   7. LOD & Essential Spans: PreserveUpperCanopyOnLod = true, essential cavity moss and scaffold
///      limbs registered in m.EssentialSpans.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Color Palette
    // ---------------------------------------------------------------------------------------------

    // Bark & wood palette
    private static readonly double[] HollowBarkDeep     = { 0.09, 0.07, 0.06 }; // Deep furrow/crevice shadow
    private static readonly double[] HollowBarkDark     = { 0.16, 0.14, 0.12 }; // Weathered dark charcoal bark
    private static readonly double[] HollowBark         = { 0.30, 0.27, 0.23 }; // Grey-brown weathered oak cork
    private static readonly double[] HollowBarkPale     = { 0.50, 0.48, 0.42 }; // Lichen-mottled weather-side bark
    private static readonly double[] HollowBarkCrest    = { 0.60, 0.56, 0.48 }; // Bleached wood ridges & knots
    private static readonly double[] HollowWoodRot      = { 0.20, 0.14, 0.09 }; // Decaying rotten punk heartwood
    private static readonly double[] HollowWoodWarm     = { 0.32, 0.23, 0.14 }; // Warm amber heartwood decay
    private static readonly double[] HollowWood         = { 0.46, 0.34, 0.22 }; // Living sapwood & callus body
    private static readonly double[] HollowWoodFresh    = { 0.58, 0.46, 0.30 }; // Pale callus margin highlight

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

    // ---------------------------------------------------------------------------------------------
    // Woody Stem Builder (Fluted, gnarly branches)
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
    // Genuine Watertight Hollow Bole with Open Arched Portal & Inner Cavern
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the genuine hollow veteran oak bole:
    /// - Wide, majestic Gothic arched doorway facing the front 3/4 camera view
    /// - Continuous rounded callus roll (4 segments per slice) with rich cambium shading
    /// - Dedicated, smoothly concave interior chamber with authentic punk wood textures
    /// - Contoured, dark humus woodland floor with organic velvety moss cushions
    /// - Muscular splayed buttresses that stay safely within the turf ground boundary
    /// - Gnarled pollard bolling collar with hollow chimney and branch knuckles
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
        // Gothic arched portal facing the front 3/4 camera (+X, +Z)
        const double openDir = Math.PI * 0.28;
        const double doorMaxHalfAngle = 0.58; // ~66 degree arch opening (inviting cathedral portal)
        double doorHeight = boleHeight * 0.62; // Arch reaches 62% of bole height (~1.75m)

        int sectors = detail == 2 ? 24 : (detail == 1 ? 30 : 36);
        int lowRings = detail == 2 ? 16 : (detail == 1 ? 20 : 24);
        int highRings = detail == 2 ? 10 : (detail == 1 ? 14 : 18);
        int totalRings = lowRings + highRings;

        // Buttresses placed on flanks and rear (never blocking or crowding the portal threshold)
        var buttressAngles = new[] {
            openDir + 0.85,
            openDir + Math.PI * 0.55,
            openDir + Math.PI * 0.95,
            openDir + Math.PI * 1.35,
            openDir + Math.PI * 1.65,
            openDir + 2.0 * Math.PI - 0.85
        };
        int buttressCount = buttressAngles.Length;
        double furrowPhase = Rng.HashUnit(seed, 101, 0) * Math.PI * 2.0;
        double barkPhase = Rng.HashUnit(seed, 102, 0) * Math.PI * 2.0;

        // Mean trunk radius profile without buttress flare
        double MeanOuterRadius(double t)
        {
            return baseRadius * (1.05 + 0.32 * MathD.SmoothStep(0.65, 0.98, t));
        }

        // Exterior trunk radius with bark furrows and splayed root buttresses
        double OuterRadius(double t, double th)
        {
            double r = MeanOuterRadius(t);
            r *= 1.0 + 0.08 * Math.Sin(8.0 * th + furrowPhase + 0.5 * t) * (0.35 + 0.65 * (1.0 - 0.4 * t));
            r *= 1.0 + 0.02 * Math.Sin(16.0 * th + barkPhase);

            if (t < 0.30)
            {
                double flareDecay = Math.Pow(1.0 - t / 0.30, 2.5);
                double buttressSum = 0.0;
                for (int b = 0; b < buttressCount; b++)
                {
                    double diff = Math.Abs(th - buttressAngles[b]);
                    if (diff > Math.PI) diff = 2.0 * Math.PI - diff;
                    double cosLobe = Math.Max(0.0, Math.Cos(diff * 1.8));
                    buttressSum += Math.Pow(cosLobe, 3.2) * 0.45;
                }
                r += baseRadius * buttressSum * flareDecay;
            }

            return r;
        }

        // Uniform wall thickness: ~0.22 * baseRadius (~23cm thick shell, authentic veteran hollow tree)
        double wallThickness = baseRadius * 0.22;

        // Dedicated spacious interior cavern radius (smooth concave heartwood chamber)
        double InnerRadius(double t, double th)
        {
            double rMean = MeanOuterRadius(t) - wallThickness;
            return rMean * (1.0 + 0.025 * Math.Sin(3.0 * th + 0.5));
        }

        // Doorway half angle at vertical height y (pointed Gothic arch with smooth base flare)
        double DoorHalfAngle(double y)
        {
            if (y >= doorHeight) return 0.0;
            double u = y / doorHeight;
            double gothicArch = Math.Sqrt(Math.Max(0.0, 1.0 - u * u));
            double baseFlare = 1.0 + 0.12 * Math.Pow(1.0 - u, 2.0);
            return doorMaxHalfAngle * gothicArch * baseFlare;
        }

        Vec3 TrunkCenter(double t)
        {
            double leanX = 0.04 * baseRadius * Math.Sin(Math.PI * t * 0.85);
            double leanZ = -0.03 * baseRadius * Math.Sin(Math.PI * t * 0.70);
            return basePoint + Vec3.Up * (boleHeight * t) + new Vec3(leanX, 0, leanZ);
        }

        Vec3 CavernCenter(double t) => TrunkCenter(t);

        // -------------------------------------------------------------------------
        // 1. Grid Stations along Trunk Perimeter
        // -------------------------------------------------------------------------
        var outerGrid = new int[totalRings + 1, sectors + 1];
        var innerGrid = new int[totalRings + 1, sectors + 1];
        var outerPos = new Vec3[totalRings + 1, sectors + 1];
        var innerPos = new Vec3[totalRings + 1, sectors + 1];
        var outerNorms = new Vec3[totalRings + 1, sectors + 1];
        var innerNorms = new Vec3[totalRings + 1, sectors + 1];

        for (int i = 0; i <= totalRings; i++)
        {
            double y = i <= lowRings
                ? doorHeight * (i / (double)lowRings)
                : doorHeight + (boleHeight - doorHeight) * ((i - lowRings) / (double)highRings);
            double t = y / boleHeight;

            double gap = DoorHalfAngle(y);
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            var cTrunk = TrunkCenter(t);
            var cCav = CavernCenter(t);

            for (int j = 0; j <= sectors; j++)
            {
                double uSec = j / (double)sectors;
                double th = startAngle + sweepAngle * uSec;
                var radDir = HollowcrownRadial(th);

                double rO = OuterRadius(t, th);
                double rI = InnerRadius(t, th);

                outerPos[i, j] = cTrunk + radDir * rO;
                innerPos[i, j] = cCav + radDir * rI;

                outerNorms[i, j] = (radDir + Vec3.Up * (0.12 - 0.24 * t)).Normalized();
                innerNorms[i, j] = (-radDir + Vec3.Up * 0.05).Normalized();
            }
        }

        // Add Outer Vertices (Bark: UV2.x = 2.0)
        for (int i = 0; i <= totalRings; i++)
        {
            double y = i <= lowRings
                ? doorHeight * (i / (double)lowRings)
                : doorHeight + (boleHeight - doorHeight) * ((i - lowRings) / (double)highRings);
            double t = y / boleHeight;

            double gap = DoorHalfAngle(y);
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                if (i >= lowRings && j == sectors)
                {
                    // Closed loop above arch: last vertex shares index with first vertex
                    outerGrid[i, j] = outerGrid[i, 0];
                    continue;
                }

                double uSec = j / (double)sectors;
                double th = startAngle + sweepAngle * uSec;

                double weather = 0.5 + 0.5 * Math.Cos(th - barkPhase);
                var col = Primitives.Mix(HollowBarkDark, HollowBark, 0.45 + 0.40 * t);
                col = Primitives.Mix(col, HollowBarkPale, 0.35 * weather * (0.3 + 0.7 * t));
                double flute = Math.Sin(8.0 * th + furrowPhase);
                if (flute < -0.2) col = Primitives.Mix(col, HollowBarkDeep, 0.60 * (-flute - 0.2) / 0.8);
                else if (flute > 0.4) col = Primitives.Mix(col, HollowBarkCrest, 0.40 * (flute - 0.4) / 0.6);

                double arcMeters = th * baseRadius;
                outerGrid[i, j] = m.AddVertex(outerPos[i, j], outerNorms[i, j], col, 1.0, y, arcMeters, 2.0, 0.0);
            }
        }

        // Inner Cavern Vertices: UV2.x = 0.0, UV2.y = 0.0 (Matte rotting heartwood, roughness 0.72, NO SPECULAR GLARE!)
        for (int i = 0; i <= totalRings; i++)
        {
            double y = i <= lowRings
                ? doorHeight * (i / (double)lowRings)
                : doorHeight + (boleHeight - doorHeight) * ((i - lowRings) / (double)highRings);
            double t = y / boleHeight;

            double gap = DoorHalfAngle(y);
            double startAngle = openDir + gap;
            double sweepAngle = 2.0 * Math.PI - 2.0 * gap;

            for (int j = 0; j <= sectors; j++)
            {
                if (i >= lowRings && j == sectors)
                {
                    innerGrid[i, j] = innerGrid[i, 0];
                    continue;
                }

                double uSec = j / (double)sectors;
                double rotGrain = Math.Sin(3.0 * uSec * Math.PI * 2.0 + 1.0) * Math.Sin(5.0 * t + 0.5);
                var col = Primitives.Mix(HollowWoodRot, HollowWoodWarm, 0.35 + 0.30 * rotGrain);
                col = Primitives.Mix(col, HollowBarkDeep, 0.50 * (1.0 - t));

                double circMeters = uSec * 2.0 * Math.PI * InnerRadius(t, 0.0);
                innerGrid[i, j] = m.AddVertex(innerPos[i, j], innerNorms[i, j], col, 1.0, y, circMeters, 0.0, 0.0);
            }
        }

        // Triangulate Outer Shell
        for (int i = 0; i < totalRings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = outerGrid[i, j];
                int b = outerGrid[i, j + 1];
                int c = outerGrid[i + 1, j];
                int d = outerGrid[i + 1, j + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(b, c, d);
            }
        }

        // Triangulate Inner Cavern Shell (Faces INWARD towards cavern center)
        for (int i = 0; i < totalRings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = innerGrid[i, j];
                int b = innerGrid[i, j + 1];
                int c = innerGrid[i + 1, j];
                int d = innerGrid[i + 1, j + 1];
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }
        }

        // -------------------------------------------------------------------------
        // 2. Thickened, Rounded Callus Wood Roll Framing the Gothic Arch
        // -------------------------------------------------------------------------
        const int cSub = 3;
        var leftCallus = new int[lowRings + 1, cSub];
        var rightCallus = new int[lowRings + 1, cSub];

        for (int i = 0; i <= lowRings; i++)
        {
            double y = doorHeight * (i / (double)lowRings);
            double gap = DoorHalfAngle(y);
            var pOut = outerPos[i, 0];
            var pIn = innerPos[i, 0];
            double thick = (pOut - pIn).Length;

            // Direction tangent to the portal opening, pointing into the doorway
            var doorInL = HollowcrownRadial(openDir + gap - Math.PI * 0.5);

            for (int s = 0; s < cSub; s++)
            {
                double frac = (s + 1) / (double)(cSub + 1);
                var pBase = Vec3.Lerp(pOut, pIn, frac);
                double bulgeAmt = Math.Sin(Math.PI * frac) * (thick * 0.35);
                var p = pBase + doorInL * bulgeAmt;
                var n = (doorInL * 0.70 + (p - (pOut + pIn) * 0.5).Normalized() * 0.30).Normalized();

                // Smooth gradient: mature bark -> pale fresh living callus -> dark punk wood
                var col = frac < 0.5
                    ? Primitives.Mix(HollowBark, HollowWoodFresh, frac * 2.0)
                    : Primitives.Mix(HollowWoodFresh, HollowWoodRot, (frac - 0.5) * 2.0);

                double u2x = (s == 0) ? 2.0 : 0.0; // outer edge bark, crest & inside matte wood
                leftCallus[i, s] = m.AddVertex(p, n, col, 1.0, y, frac, u2x, 0.0);
            }

            var pOutR = outerPos[i, sectors];
            var pInR = innerPos[i, sectors];
            double thickR = (pOutR - pInR).Length;
            var doorInR = HollowcrownRadial(openDir - gap + Math.PI * 0.5);

            for (int s = 0; s < cSub; s++)
            {
                double frac = (s + 1) / (double)(cSub + 1);
                var pBase = Vec3.Lerp(pOutR, pInR, frac);
                double bulgeAmt = Math.Sin(Math.PI * frac) * (thickR * 0.35);
                var p = pBase + doorInR * bulgeAmt;
                var n = (doorInR * 0.70 + (p - (pOutR + pInR) * 0.5).Normalized() * 0.30).Normalized();

                var col = frac < 0.5
                    ? Primitives.Mix(HollowBark, HollowWoodFresh, frac * 2.0)
                    : Primitives.Mix(HollowWoodFresh, HollowWoodRot, (frac - 0.5) * 2.0);

                double u2x = (s == 0) ? 2.0 : 0.0;
                rightCallus[i, s] = m.AddVertex(p, n, col, 1.0, y, frac, u2x, 0.0);
            }
        }

        // Triangulate left callus roll strip
        for (int i = 0; i < lowRings; i++)
        {
            m.AddTriangle(outerGrid[i, 0], outerGrid[i + 1, 0], leftCallus[i, 0]);
            m.AddTriangle(outerGrid[i + 1, 0], leftCallus[i + 1, 0], leftCallus[i, 0]);

            for (int s = 0; s < cSub - 1; s++)
            {
                int a = leftCallus[i, s];
                int b = leftCallus[i, s + 1];
                int c = leftCallus[i + 1, s];
                int d = leftCallus[i + 1, s + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(c, d, b);
            }

            int lastS = cSub - 1;
            m.AddTriangle(leftCallus[i, lastS], leftCallus[i + 1, lastS], innerGrid[i, 0]);
            m.AddTriangle(leftCallus[i + 1, lastS], innerGrid[i + 1, 0], innerGrid[i, 0]);
        }

        // Triangulate right callus roll strip
        for (int i = 0; i < lowRings; i++)
        {
            m.AddTriangle(outerGrid[i, sectors], rightCallus[i, 0], outerGrid[i + 1, sectors]);
            m.AddTriangle(outerGrid[i + 1, sectors], rightCallus[i, 0], rightCallus[i + 1, 0]);

            for (int s = 0; s < cSub - 1; s++)
            {
                int a = rightCallus[i, s];
                int b = rightCallus[i, s + 1];
                int c = rightCallus[i + 1, s];
                int d = rightCallus[i + 1, s + 1];
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }

            int lastS = cSub - 1;
            m.AddTriangle(rightCallus[i, lastS], innerGrid[i, sectors], rightCallus[i + 1, lastS]);
            m.AddTriangle(rightCallus[i + 1, lastS], innerGrid[i, sectors], innerGrid[i + 1, sectors]);
        }

        // Seal arch apex crest where left and right jambs join
        for (int s = 0; s < cSub; s++)
        {
            m.AddTriangle(leftCallus[lowRings, s], outerGrid[lowRings, 0], rightCallus[lowRings, s]);
            m.AddTriangle(leftCallus[lowRings, s], rightCallus[lowRings, s], innerGrid[lowRings, 0]);
        }

        // -------------------------------------------------------------------------
        // 3. Pollard Bolling Rim at Top of Bole (UV2.x = 0.0, UV2.y = 0.0 - NO VINYL GROOVES!)
        // -------------------------------------------------------------------------
        for (int j = 0; j < sectors; j++)
        {
            int o0 = outerGrid[totalRings, j];
            int o1 = outerGrid[totalRings, j + 1];
            int i0 = innerGrid[totalRings, j];
            int i1 = innerGrid[totalRings, j + 1];
            m.AddTriangle(o0, o1, i0);
            m.AddTriangle(o1, i1, i0);
        }

        // -------------------------------------------------------------------------
        // 4. Contoured Cavern Basin Floor (Planar UVs, dark forest humus & punk mold)
        // -------------------------------------------------------------------------
        int floorRings = detail == 2 ? 3 : (detail == 1 ? 4 : 5);
        int floorSectors = sectors;
        var floorGrid = new int[floorRings + 1, floorSectors + 1];

        var cFloor = CavernCenter(0.0) + Vec3.Up * 0.005;

        for (int fr = 0; fr <= floorRings; fr++)
        {
            double rFrac = fr / (double)floorRings;
            double dishY = 0.005 + 0.015 * (rFrac * rFrac);

            for (int fs = 0; fs <= floorSectors; fs++)
            {
                if (fr == floorRings)
                {
                    floorGrid[fr, fs] = innerGrid[0, fs];
                }
                else
                {
                    double uSec = fs / (double)floorSectors;
                    double gap = DoorHalfAngle(0.0);
                    double th = (openDir + gap) + (2.0 * Math.PI - 2.0 * gap) * uSec;

                    double rCur = InnerRadius(0.0, th) * rFrac;
                    var p = cFloor + Vec3.Up * dishY + HollowcrownRadial(th) * rCur;
                    var n = (Vec3.Up * 0.95 + HollowcrownRadial(th) * (0.20 * rFrac)).Normalized();

                    double humusNoise = Math.Sin(3.0 * p.X * 12.0) * Math.Cos(3.0 * p.Z * 12.0);
                    var col = Primitives.Mix(HollowBarkDeep, HollowWoodRot, 0.40 + 0.30 * humusNoise);

                    double uvX = (p.X - basePoint.X) * 1.2 + 0.5;
                    double uvY = (p.Z - basePoint.Z) * 1.2 + 0.5;

                    floorGrid[fr, fs] = m.AddVertex(p, n, col, 1.0, uvX, uvY, 0.0, 0.0);
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
                m.AddTriangle(a, b, c);
                m.AddTriangle(b, d, c);
            }
        }

        int fcVertex = m.AddVertex(cFloor, Vec3.Up, HollowBarkDeep, 1.0, 0.5, 0.5, 0.0, 0.0);
        for (int fs = 0; fs < floorSectors; fs++)
        {
            m.AddTriangle(fcVertex, floorGrid[0, fs], floorGrid[0, fs + 1]);
        }

        // -------------------------------------------------------------------------
        // 5. Multi-layered Velvety Bioluminescent Moss Cushions (UV2.x = 1.35f, UV2.y = 1f)
        // -------------------------------------------------------------------------
        int mossStartVertex = m.VertexCount;

        // (a) Organic moss cushions clustering across the doorway threshold and callus roots
        var doorFwd = HollowcrownRadial(openDir);
        var doorSide = HollowcrownRadial(openDir + Math.PI * 0.5);

        var threshPatches = new[] {
            (center: cFloor + doorFwd * (baseRadius * 0.38) - doorSide * (baseRadius * 0.15) + Vec3.Up * 0.012, radS: baseRadius * 0.16, radF: baseRadius * 0.14, h: 0.018),
            (center: cFloor + doorFwd * (baseRadius * 0.38) + doorSide * (baseRadius * 0.15) + Vec3.Up * 0.012, radS: baseRadius * 0.16, radF: baseRadius * 0.14, h: 0.018),
            (center: cFloor + doorFwd * (baseRadius * 0.46) + doorSide * (baseRadius * 0.02) + Vec3.Up * 0.008, radS: baseRadius * 0.18, radF: baseRadius * 0.11, h: 0.012)
        };

        for (int pIdx = 0; pIdx < threshPatches.Length; pIdx++)
        {
            var patch = threshPatches[pIdx];
            int tRings = 3;
            int tSectors = 6;
            var tGrid = new int[tRings + 1, tSectors + 1];

            for (int tr = 0; tr <= tRings; tr++)
            {
                double vFrac = (tr / (double)tRings) * 2.0 - 1.0;
                for (int ts = 0; ts <= tSectors; ts++)
                {
                    double hFrac = (ts / (double)tSectors) * 2.0 - 1.0;
                    double distSq = hFrac * hFrac + vFrac * vFrac;
                    double dome = Math.Max(0.0, 1.0 - distSq);
                    double moundY = patch.h * Math.Pow(dome, 0.85);

                    var p = patch.center + doorSide * (hFrac * patch.radS) + doorFwd * (vFrac * patch.radF) + Vec3.Up * moundY;
                    var n = (Vec3.Up * (0.80 + 0.20 * dome) + doorSide * (hFrac * 0.30) + doorFwd * (vFrac * 0.30)).Normalized();

                    var col = Primitives.Mix(HollowMossBase, HollowMossMid, dome);
                    col = Primitives.Mix(col, HollowMossLit, Math.Pow(dome, 2.0) * 0.90);

                    tGrid[tr, ts] = m.AddVertex(p, n, col, 1.0, ts / (double)tSectors, tr / (double)tRings, 1.35, 1.0);
                }
            }

            for (int tr = 0; tr < tRings; tr++)
            {
                for (int ts = 0; ts < tSectors; ts++)
                {
                    int a = tGrid[tr, ts];
                    int b = tGrid[tr, ts + 1];
                    int c = tGrid[tr + 1, ts];
                    int d = tGrid[tr + 1, ts + 1];
                    m.AddTriangle(a, b, c);
                    m.AddTriangle(b, d, c);
                }
            }
        }

        // (b) Smooth rounded velvety moss cushions nestled into interior heartwood niches
        int numCushions = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
        for (int c = 0; c < numCushions; c++)
        {
            double cFrac = (c + 0.5) / numCushions;
            double cTh = (openDir + Math.PI) + (cFrac - 0.5) * Math.PI * 0.90;
            double cT = 0.04 + 0.38 * Rng.HashUnit(seed, (ulong)(c * 47 + 13), 0);

            double rWall = InnerRadius(cT, cTh) * 0.96;
            var cCenter = CavernCenter(cT) + HollowcrownRadial(cTh) * rWall;

            var cInNorm = (-new Vec3(Math.Cos(cTh), -0.15, Math.Sin(cTh))).Normalized();
            var cTangent = cInNorm.Cross(Vec3.Up).Normalized();
            var cUp = cTangent.Cross(cInNorm).Normalized();

            double cRadX = baseRadius * (0.09 + 0.04 * Rng.HashUnit(seed, (ulong)(c * 67 + 2), 0));
            double cRadY = baseRadius * (0.07 + 0.04 * Rng.HashUnit(seed, (ulong)(c * 67 + 3), 0));
            double cBulge = baseRadius * 0.038;

            int cRings = detail == 2 ? 3 : 4;
            int cCols = detail == 2 ? 4 : 5;
            var padGrid = new int[cRings + 1, cCols + 1];

            for (int cr = 0; cr <= cRings; cr++)
            {
                double vFrac = cr / (double)cRings;
                double vy = (vFrac - 0.5) * 2.0;

                for (int cc = 0; cc <= cCols; cc++)
                {
                    double hFrac = cc / (double)cCols;
                    double vx = (hFrac - 0.5) * 2.0;

                    double distSq = vx * vx + vy * vy;
                    double dome = Math.Max(0.0, 1.0 - distSq);
                    double bulgeAmt = Math.Pow(dome, 0.75) * cBulge;

                    var p = cCenter + cTangent * (vx * cRadX) + cUp * (vy * cRadY) + cInNorm * bulgeAmt;
                    var n = (cInNorm * (0.6 + 0.4 * dome) + cTangent * vx + cUp * vy).Normalized();

                    var col = Primitives.Mix(HollowMossBase, HollowMossMid, dome);
                    col = Primitives.Mix(col, HollowMossLit, Math.Pow(dome, 2.0) * 0.95);

                    padGrid[cr, cc] = m.AddVertex(p, n, col, 1.0, hFrac, vFrac, 1.35, 1.0);
                }
            }

            for (int cr = 0; cr < cRings; cr++)
            {
                for (int cc = 0; cc < cCols; cc++)
                {
                    int a = padGrid[cr, cc];
                    int b = padGrid[cr, cc + 1];
                    int cIdx = padGrid[cr + 1, cc];
                    int d = padGrid[cr + 1, cc + 1];
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
        // 6. Splayed Surface Root Runners Snaking Across Turf
        // -------------------------------------------------------------------------
        int rootRunners = detail == 2 ? 4 : (detail == 1 ? 5 : 7);
        for (int r = 0; r < rootRunners; r++)
        {
            double rAz = buttressAngles[r % buttressAngles.Length] + rng.Range(-0.15, 0.15);
            var rDir = HollowcrownRadial(rAz);
            var rOrtho = HollowcrownRadial(rAz + Math.PI * 0.5);

            double rootLen = baseRadius * rng.Range(0.80, 1.25);
            var pRootStart = basePoint + rDir * (baseRadius * 0.88) + Vec3.Up * (baseRadius * 0.035);
            var pRootEnd = pRootStart + rDir * rootLen + rOrtho * rng.Range(-0.15, 0.15) * baseRadius + Vec3.Up * 0.005;

            var cRoot0 = pRootStart + (rDir * 0.85 - Vec3.Up * 0.10).Normalized() * (rootLen * 0.40);
            var cRoot1 = pRootEnd - rDir * (rootLen * 0.35);

            int rootStations = detail == 2 ? 4 : 5;
            var rootPath = HollowcrownSampleBezier(pRootStart, cRoot0, cRoot1, pRootEnd, rootStations);
            var rootRadii = new double[rootStations];
            double rStart = baseRadius * rng.Range(0.11, 0.15);
            double rEnd = baseRadius * 0.020;

            for (int s = 0; s < rootStations; s++)
            {
                double st = s / (double)(rootStations - 1);
                rootRadii[s] = MathD.Lerp(rStart, rEnd, Math.Pow(st, 0.70));
            }

            HollowcrownWoodStem(m, rootPath, rootRadii, detail, ridges: 3, twistTotal: 0.15);
        }
    }

    // ---------------------------------------------------------------------------------------------
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

        // Piecewise smooth lobed width curve with 3 distinct pairs of deep rounded lobes,
        // deep sinuses cutting 65-70% towards midrib, basal auricles, and rounded terminal lobe.
        double OakWidthProfile(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t < 0.10) return 0.05 * (t / 0.10); // Slender petiole stalk

            // Key landmark stations along the blade (t in 0.10 .. 1.00)
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
                double x = (c / (double)(cols - 1)) * 2.0 - 1.0; // -1 to +1
                double absX = Math.Abs(x);

                // 3D Camber:
                // - V-cup across lamina
                // - Raised midrib spine
                // - Undulating lobe margins curling upward at lobe summits
                double cup = 0.035 * width * (absX * absX);
                double lobeLift = (wProfile - 0.30) * (0.015 * length) * (absX * absX);
                double midribLift = (1.0 - absX) * (0.003 * length);

                var offsetSide = sSpine * (halfW * x);
                var offsetUp = nSpine * (cup + lobeLift + midribLift);
                var p = pSpine + offsetSide + offsetUp;

                // Normal with camber slope
                var n = (nSpine * (0.90 + 0.10 * (1.0 - absX)) - sSpine * (x * 0.35)).Normalized();

                // Vertex color gradient
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

                // UV: t along spine (0..1), (x + 1)/2 across blade (0..1)
                // UV2.x = 0.0, UV2.y = 1.0 (Full leaf shader processing with double-sided normal inversion)
                gridVertices[r, c] = m.AddVertex(p, n, bodyCol, 1.0, t, (x + 1.0) * 0.5, 0.0, 1.0);
            }
        }

        // Triangulate thin single-surface lamina
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

        // 1. Terminal radiating rosette of lobed oak leaves
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

        // 2. Lateral leaves staggered in spiral phyllotaxis along the sub-terminal twig
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
    /// - 5 Muscular curved cubic Bézier scaffold limbs launching from the swollen bolling head
    /// - Smoothly forking secondary and tertiary boughs arching into a spreading canopy
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
        double bollingY = boleHeight * 0.96;
        var pBollingCenter = basePoint + Vec3.Up * bollingY;

        int numLimbs = juvenile ? 4 : 5;
        double baseAz = Rng.HashUnit(seed, 201, 0) * Math.PI * 2.0;

        for (int k = 0; k < numLimbs; k++)
        {
            double lAz = baseAz + (2.0 * Math.PI * k / numLimbs) + rng.Range(-0.12, 0.12);
            var outward = HollowcrownRadial(lAz);
            var ortho = HollowcrownRadial(lAz + Math.PI * 0.5);

            double bollingRad = baseRadius * rng.Range(0.65, 0.76);
            var pRoot = pBollingCenter + outward * bollingRad + Vec3.Up * rng.Range(-0.02, 0.04);

            double limbReach = (juvenile ? 1.05 : 1.50) * rng.Range(0.92, 1.08);
            double limbRise = (juvenile ? 0.70 : 1.05) * rng.Range(0.88, 1.12);

            var pLimbEnd = pRoot + outward * limbReach + ortho * rng.Range(-0.15, 0.15) + Vec3.Up * limbRise;

            // Continuous cubic Bézier curve: steep launch upward, graceful spreading arch outward
            var limbStartDir = (outward * 0.35 + Vec3.Up * 0.92).Normalized();
            var cLimb0 = pRoot + limbStartDir * (limbReach * 0.45);
            var limbEndDir = (outward * 0.85 + Vec3.Up * 0.35).Normalized();
            var cLimb1 = pLimbEnd - limbEndDir * (limbReach * 0.38);

            int limbStations = detail == 2 ? 6 : (detail == 1 ? 8 : 10);
            var limbPath = HollowcrownSampleBezier(pRoot, cLimb0, cLimb1, pLimbEnd, limbStations);
            var limbRadii = new double[limbStations];

            double limbBaseR = baseRadius * (juvenile ? 0.20 : 0.26);
            double limbTipR = limbBaseR * 0.34;

            for (int s = 0; s < limbStations; s++)
            {
                double st = s / (double)(limbStations - 1);
                limbRadii[s] = MathD.Lerp(limbBaseR, limbTipR, Math.Pow(st, 0.80));
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
                    bRadii[s] = MathD.Lerp(bBaseR, bTipR, st);
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
                    var tertRadii = new[] { bBaseR * 0.45, bBaseR * 0.38, bBaseR * 0.30, bBaseR * 0.22, 0.004 };
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
    /// The veteran oak pollard: buttressed, fluted, hollowed with bioluminescent moss,
    /// and crowned with spreading cubic Bézier scaffold boughs and Quercus foliage.
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

        // 1. Bole, fluted trunk, splayed buttresses, genuine hollow cavity & bioluminescent moss
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
