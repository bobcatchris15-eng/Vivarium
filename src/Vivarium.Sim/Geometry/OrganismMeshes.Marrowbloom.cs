using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Marrowbloom - rootless woodland understory flower of deep shade (Monotropa uniflora / Ghost Pipe).
///
/// Distinctive botanical architecture:
///   1. Fleshy, achlorophyllous mycoheterotroph: completely lacking chlorophyll. Fleshy succulent
///      ivory-white stipes with subtle longitudinal fluting.
///   2. Subterranean root crown: tangled mycorrhizal root nubs and mycelial threads in dark duff.
///   3. Imbricated translucent bracts: genuine appressed, clasping papery scales that wrap around the
///      stem like shingles, crowded at the base and gathering into calyx-like sepals beneath each bloom.
///   4. Multi-petaled campanulate goblet flower: 5 distinct overlapping waxy petals forming a true
///      cylindrical-campanulate corolla with waxy thickness and gently recurved, wavy tips.
///   5. Visible floral interior: prominent columnar style ending in a broad peltate/funnel stigma,
///      encircled by 10 honey-amber anthers that cast warm internal silhouettes through the
///      translucent waxy petals.
///   6. Diverse colony stages: gracefully nodding mature shepherd's crooks, emergent young hooks,
///      and upright post-pollination seed capsules.
///
/// Authored in real metres and transformed into the engine unit frame via NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // Ethereal translucent bone / ivory palette
    private static readonly double[] MarrowBone        = { 0.94, 0.92, 0.88 }; // Fresh succulent ivory
    private static readonly double[] MarrowTranslucent = { 0.98, 0.96, 0.92 }; // Ultra-thin waxy edges
    private static readonly double[] MarrowOld         = { 0.74, 0.71, 0.65 }; // Weathered duff-stained base
    private static readonly double[] MarrowScale       = { 0.90, 0.87, 0.82 }; // Papery translucent bract
    private static readonly double[] MarrowRose        = { 0.94, 0.70, 0.70 }; // Subtle amethyst/rose apical blush
    private static readonly double[] MarrowAnther      = { 0.88, 0.68, 0.24 }; // Rich warm honey-amber pollen sacs
    private static readonly double[] MarrowStigma      = { 0.91, 0.89, 0.84 }; // Fleshy peltate stigma disc
    private static readonly double[] MarrowRoot        = { 0.38, 0.32, 0.26 }; // Mycorrhizal root humus
    private static readonly double[] MarrowHyphae      = { 0.78, 0.75, 0.70 }; // Pale fungal mycelium

    /// <summary>
    /// Evaluates a cubic Bézier curve at parameter t.
    /// </summary>
    private static Vec3 MarrowBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        return p0 * (uu * u) + c0 * (3.0 * uu * t) + c1 * (3.0 * u * tt) + p1 * (tt * t);
    }

    /// <summary>
    /// Discretizes a cubic Bézier curve into equidistant sample points.
    /// </summary>
    private static List<Vec3> MarrowSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new List<Vec3>(stations);
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts.Add(MarrowBezier(p0, c0, c1, p1, t));
        }
        return pts;
    }

    /// <summary>
    /// Builds a succulent, fluted ivory-white stipe with 8 rounded longitudinal ridges,
    /// smooth normal vectors, and rotation-minimizing transport frame.
    /// </summary>
    private static void MarrowbloomFlutedStem(
        MeshData m,
        IReadOnlyList<Vec3> points,
        IReadOnlyList<double> radii,
        int detail,
        int ridges = 8)
    {
        int stations = points.Count;
        if (stations < 2 || radii.Count != stations) return;

        int segments = detail == 2 ? 6 : (detail == 1 ? 8 : 12);
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

                // Fleshy succulent fluting along the stem
                double ridgeWave = Math.Cos(ridges * th);
                double rMod = 1.0 + 0.085 * ridgeWave + 0.025 * Math.Cos(4.0 * th);
                double r = Math.Max(0.0008, baseR * rMod);

                var radialDir = (norm * Math.Cos(th) + binorm * Math.Sin(th)).Normalized();
                var pos = center + radialDir * r;
                ringPositions[i, s] = pos;

                // Translucent ivory, warming slightly toward the duff base
                var baseCol = Primitives.Mix(MarrowOld, MarrowBone, Math.Pow(t, 0.65));
                if (ridgeWave > 0.3)
                    baseCol = Primitives.Mix(baseCol, MarrowTranslucent, 0.45 * (ridgeWave - 0.3) / 0.7);

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
            double t = i / (double)(stations - 1);
            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                m.AddVertex(ringPositions[i, s], ringNormals[i, s], ringColors[i, s], 1.0, t, u, 0.0, 1.0);
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
        NewFloraMarkOrgan(m, startVertex, m.VertexCount - startVertex);
    }

    /// <summary>
    /// Builds an authentic translucent clasping scale bract sheathing the stem.
    /// The bract lies appressed to the stem surface at its base and curves gently outward at the tip.
    /// </summary>
    private static void MarrowbloomClaspingBract(
        MeshData m,
        Vec3 stemPos,
        Vec3 stemDir,
        Vec3 stemRadial,
        double stemRadius,
        double length,
        double arcSpread,
        ulong seed,
        int detail)
    {
        if (length <= 1e-4) return;

        int rows = detail == 2 ? 3 : 4;
        int cols = detail == 2 ? 2 : 3;

        var radialNorm = stemRadial.Normalized();
        var side = stemDir.Cross(radialNorm).Normalized();

        int bractStart = m.VertexCount;
        int totalCols = 2 * cols + 1;

        var topPos = new Vec3[rows, totalCols];
        var topNorm = new Vec3[rows, totalCols];
        var topCol = new double[rows, totalCols][];

        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            double along = length * t;

            // Appressed to the stem: stays within 0.5mm - 1.5mm of stem surface,
            // flaring gently outward only at the very distal tip (t > 0.70)
            double outwardLift = (t > 0.65) ? Math.Pow((t - 0.65) / 0.35, 1.8) * (length * 0.16) : 0.0004;
            double rRing = stemRadius + 0.0005 + outwardLift;

            // Circumferential angle spread: tapers from broad sheath at base to acute tip
            double currentArc = arcSpread * (1.0 - 0.75 * Math.Pow(t, 0.70));

            for (int c = 0; c < totalCols; c++)
            {
                double u = (c / (double)(totalCols - 1)) * 2.0 - 1.0; // [-1, 1]
                double angle = u * currentArc;

                // Position wrapped around the stem axis
                var radDir = radialNorm * Math.Cos(angle) + side * Math.Sin(angle);
                var p = stemPos + stemDir * along + radDir * rRing;
                topPos[r, c] = p;

                var norm = (radDir * 0.85 + stemDir * (0.35 * (1.0 - t))).Normalized();
                topNorm[r, c] = norm;

                var col = Primitives.Mix(MarrowScale, MarrowTranslucent, 0.45 * t);
                if (t > 0.70)
                    col = Primitives.Mix(col, MarrowRose, 0.35 * (t - 0.70) / 0.30);
                topCol[r, c] = col;
            }
        }

        // Add single-sheet double-sided shaded vertices
        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            for (int c = 0; c < totalCols; c++)
            {
                double u = c / (double)(totalCols - 1);
                m.AddVertex(topPos[r, c], topNorm[r, c], topCol[r, c], 1.0, t, u, 0.0, 1.0);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < totalCols - 1; c++)
            {
                int a = bractStart + r * totalCols + c;
                int b = a + totalCols;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
                // Backface for translucent scale
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        NewFloraMarkOrgan(m, bractStart, m.VertexCount - bractStart);
    }

    /// <summary>
    /// Builds one individual waxy petal for the nodding campanulate goblet flower.
    /// 5 overlapping petals form the authentic 3D cylindrical-campanulate corolla.
    /// </summary>
    private static void MarrowbloomPetal(
        MeshData m,
        Vec3 receptaclePos,
        Vec3 flowerAxis,
        Vec3 petalOutward,
        double length,
        double width,
        ulong seed,
        int detail)
    {
        int rows = detail == 2 ? 4 : 6;
        int cols = detail == 2 ? 1 : 2;
        int totalCols = 2 * cols + 1;

        var side = flowerAxis.Cross(petalOutward).Normalized();
        var outDir = petalOutward.Normalized();

        int petalStart = m.VertexCount;
        var pos = new Vec3[rows, totalCols];
        var norm = new Vec3[rows, totalCols];
        var col = new double[rows, totalCols][];

        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            double along = length * t;

            // Campanulate urn curve: cylindrical body with gentle mid-swell and delicate lip recurve
            double bulb = Math.Sin(Math.PI * Math.Pow(t, 0.75)) * (length * 0.11);
            double lipRecurve = Math.Pow(Math.Max(0.0, t - 0.70) / 0.30, 2.0) * (length * 0.13);
            double radialOffset = bulb + lipRecurve;

            var spine = receptaclePos + flowerAxis * along + outDir * radialOffset;

            // Spatulate petal width profile
            double widthProfile;
            if (t < 0.20) widthProfile = 0.40 + 0.60 * (t / 0.20);
            else if (t < 0.82) widthProfile = 1.0 + 0.15 * Math.Sin(Math.PI * (t - 0.20) / 0.62);
            else widthProfile = Math.Max(0.70, 1.15 - 0.45 * ((t - 0.82) / 0.18));

            double w = width * widthProfile;

            for (int c = 0; c < totalCols; c++)
            {
                double u = (c / (double)(totalCols - 1)) * 2.0 - 1.0; // [-1, 1]
                // Transverse concave cup forming cylindrical goblet wall
                double cup = (1.0 - u * u) * (w * 0.22);
                // Delicate wave at petal rim
                double wave = (t > 0.70) ? 0.05 * w * Math.Sin(u * Math.PI * 3.0) : 0.0;

                var p = spine + side * (u * w) - outDir * cup + flowerAxis * wave;
                pos[r, c] = p;

                var n = (outDir * 0.85 + side * (u * 0.40) - flowerAxis * (0.20 * (1.0 - t))).Normalized();
                norm[r, c] = n;

                // Ivory waxy base warming to delicate rose-blush rim
                var cBase = Primitives.Mix(MarrowBone, MarrowTranslucent, 0.40 * t);
                if (t > 0.60)
                {
                    double rf = (t - 0.60) / 0.40;
                    cBase = Primitives.Mix(cBase, MarrowRose, 0.45 * rf);
                }
                col[r, c] = cBase;
            }
        }

        // Add double-sided vertices for rich waxy translucency
        for (int r = 0; r < rows; r++)
        {
            double t = r / (double)(rows - 1);
            for (int c = 0; c < totalCols; c++)
            {
                double u = c / (double)(totalCols - 1);
                m.AddVertex(pos[r, c], norm[r, c], col[r, c], 1.0, t, u, 0.0, 1.0);
            }
        }

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < totalCols - 1; c++)
            {
                int a = petalStart + r * totalCols + c;
                int b = a + totalCols;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
                // Backface
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        NewFloraMarkOrgan(m, petalStart, m.VertexCount - petalStart);
    }

    /// <summary>
    /// Builds the internal floral organs of Monotropa: central columnar style with broad peltate
    /// stigma funnel, encircled by 10 honey-amber anthers that cast glowing silhouettes through
    /// the translucent waxy petals.
    /// </summary>
    private static void MarrowbloomFloralInterior(
        MeshData m,
        Vec3 receptaclePos,
        Vec3 flowerAxis,
        double flowerSize,
        Rng rng,
        int detail)
    {
        int organStart = m.VertexCount;
        var down = flowerAxis.Normalized();
        var side = down.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = down.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var forward = side.Cross(down).Normalized();

        // 1. Swollen 5-lobed Ovary at the receptacle
        var ovaryCenter = receptaclePos + down * (flowerSize * 0.16);
        Primitives.Ellipsoid(m, ovaryCenter,
            new Vec3(flowerSize * 0.22, flowerSize * 0.18, flowerSize * 0.22),
            5, 8, (u, v) => (Primitives.Mix(MarrowBone, MarrowOld, 0.25), 1, u, v, 0, 1));

        // 2. Fleshy Columnar Style extending down the center of the goblet
        int styleStations = 6;
        var stylePath = new Vec3[styleStations];
        var styleRadii = new double[styleStations];
        double styleLen = flowerSize * 0.58;
        for (int i = 0; i < styleStations; i++)
        {
            double t = i / (double)(styleStations - 1);
            stylePath[i] = ovaryCenter + down * (styleLen * t);
            styleRadii[i] = flowerSize * 0.055 * (1.0 + 0.20 * Math.Pow(t, 2.0));
        }
        Primitives.Tube(m, stylePath, styleRadii, 6, (k, v) => (MarrowBone, 1, k / 5.0, v, 0, 1));

        // 3. Broad Peltate Funnel Stigma Disc at the style tip
        var stigmaPos = stylePath[^1];
        int stigmaSectors = detail == 2 ? 8 : 12;
        int centerV = m.AddVertex(stigmaPos + down * (flowerSize * 0.02), down, MarrowStigma, 1, 0.5, 0.5, 0, 1);
        int rimStart = m.VertexCount;
        double stigmaRadius = flowerSize * 0.16;

        for (int s = 0; s <= stigmaSectors; s++)
        {
            double th = 2.0 * Math.PI * s / stigmaSectors;
            var rad = side * Math.Cos(th) + forward * Math.Sin(th);
            var pRim = stigmaPos + rad * stigmaRadius - down * (flowerSize * 0.015);
            m.AddVertex(pRim, (down * 0.8 + rad * 0.6).Normalized(), MarrowStigma, 1, 0.5 + 0.5 * Math.Cos(th), 0.5 + 0.5 * Math.Sin(th), 0, 1);
        }

        for (int s = 0; s < stigmaSectors; s++)
        {
            m.AddTriangle(centerV, rimStart + s, rimStart + s + 1);
            m.AddTriangle(centerV, rimStart + s + 1, rimStart + s); // double sided
        }

        // 4. Ring of 10 Honey-Amber Stamens nestled around the style
        int numStamens = detail == 2 ? 6 : 10;
        for (int i = 0; i < numStamens; i++)
        {
            double a = 2.0 * Math.PI * i / numStamens + rng.Range(-0.08, 0.08);
            var stamenOut = (side * Math.Cos(a) + forward * Math.Sin(a)).Normalized();
            var filFrom = ovaryCenter + stamenOut * (flowerSize * 0.16);
            double filReach = flowerSize * rng.Range(0.48, 0.56);
            var filTo = receptaclePos + down * filReach + stamenOut * (flowerSize * 0.15);

            NewFloraFilament(m, filFrom, filTo, flowerSize * 0.016, MarrowBone, sides: 4);

            // Two-celled Honey-Amber Anther
            var antherPos = filTo + down * (flowerSize * 0.025);
            Primitives.Ellipsoid(m, antherPos,
                new Vec3(flowerSize * 0.045, flowerSize * 0.055, flowerSize * 0.035),
                4, 5, (u, v) => (MarrowAnther, 1, u, v, 0, 1));
        }

        NewFloraMarkOrgan(m, organStart, m.VertexCount - organStart);
    }

    /// <summary>
    /// Builds the subterranean mycorrhizal root crown with organic humus nubs and fungal hyphae.
    /// </summary>
    private static void MarrowbloomRhizome(MeshData m, Vec3 origin, Rng rng, double radius, int detail)
    {
        int rootStart = m.VertexCount;

        // Fleshy root clump
        int clumps = detail == 2 ? 4 : 7;
        for (int c = 0; c < clumps; c++)
        {
            double a = 2.0 * Math.PI * c / clumps + rng.Range(-0.25, 0.25);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double rReach = radius * rng.Range(0.20, 0.40);
            var from = origin + dir * (rReach * 0.25) - Vec3.Up * (radius * 0.03);
            var to = origin + dir * rReach - Vec3.Up * (radius * 0.05);
            var mid = Vec3.Lerp(from, to, 0.5) - Vec3.Up * (radius * 0.02);

            Primitives.Tube(m, new[] { from, mid, to },
                new[] { radius * 0.060, radius * 0.040, radius * 0.018 }, 6,
                (k, v) => (Primitives.Mix(MarrowRoot, MarrowOld, 0.4), 1, k * 0.5, v, 0, 0));
        }

        // Delicate pale mycelial hyphae threads weaving through duff
        int hyphaeCount = detail == 2 ? 6 : 14;
        for (int h = 0; h < hyphaeCount; h++)
        {
            double a = rng.Range(0, 2.0 * Math.PI);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var hFrom = origin + dir * (radius * rng.Range(0.05, 0.20)) - Vec3.Up * (radius * 0.01);
            var hTo = hFrom + dir * (radius * rng.Range(0.20, 0.45)) + new Vec3(rng.Range(-0.02, 0.02), rng.Range(-0.015, 0.005), rng.Range(-0.02, 0.02));
            NewFloraFilament(m, hFrom, hTo, radius * 0.004, MarrowHyphae, sides: 3);
        }

        NewFloraMarkOrgan(m, rootStart, m.VertexCount - rootStart);
    }

    /// <summary>
    /// Marrowbloom procedural generator (Monotropa uniflora / Ghost Pipe).
    /// Builds a multi-stemmed colony of succulent, translucent ivory nodding pipes,
    /// sheathed in clasping bracts, and crowned with 5-petaled waxy goblet flowers
    /// with glowing honey-amber internal stamens.
    /// </summary>
    private static void Marrowbloom(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        double radius,
        double height)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;

        // 1. Subterranean Mycorrhizal Rhizome
        MarrowbloomRhizome(part, Vec3.Zero, rng, radius, detail);

        // 2. Colony of 6 to 9 Succulent Stipes with Natural Clump Spread
        int numStems = detail == 2 ? 4 : (detail == 1 ? 6 : 8 + rng.NextInt(2));
        double baseAz = rng.Range(0, Math.PI * 2);

        for (int s = 0; s < numStems; s++)
        {
            double a = baseAz + (2.0 * Math.PI * s / numStems) + rng.Range(-0.28, 0.28);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double rOffset = radius * rng.Range(0.12, 0.36);
            var rootPos = outward * rOffset;

            // Determine stem growth stage:
            // stage 0 = prime nodding mature flower (70%)
            // stage 1 = young emerging hooked bud (18%)
            // stage 2 = upright post-pollination seed capsule (12%)
            double stageRoll = (s == 0) ? 0.10 : rng.Range(0.0, 1.0);
            int stage = stageRoll < 0.70 ? 0 : (stageRoll < 0.88 ? 1 : 2);

            double stemHeight = height * (stage == 1 ? rng.Range(0.48, 0.65) : rng.Range(0.80, 1.02));
            double stemBaseRadius = radius * (stage == 1 ? 0.038 : 0.046);
            double stemTipRadius = stemBaseRadius * 0.68;

            // Outward splay at root collar (5° to 20°)
            double splayAngle = rng.Range(0.10, 0.32);
            var splayDir = (Vec3.Up * Math.Cos(splayAngle) + outward * Math.Sin(splayAngle)).Normalized();

            // Compute graceful cubic Bézier control points for the stipe
            Vec3 p0 = rootPos;
            Vec3 ctrl0 = rootPos + splayDir * (stemHeight * 0.42);
            Vec3 ctrl1;
            Vec3 p1;
            Vec3 flowerDir;

            if (stage == 0)
            {
                // Mature nodding flower: arching over gracefully into a shepherd's crook
                double bowReach = radius * rng.Range(0.24, 0.38);
                ctrl1 = rootPos + Vec3.Up * (stemHeight * 0.98) + outward * (bowReach * 0.50);
                p1 = rootPos + Vec3.Up * (stemHeight * 0.82) + outward * bowReach;
                // Last tangent points downward and slightly outward
                flowerDir = (Vec3.Down * 0.88 + outward * 0.45).Normalized();
            }
            else if (stage == 1)
            {
                // Young emerging hook: tightly curled downward
                double hookReach = radius * rng.Range(0.14, 0.20);
                ctrl1 = rootPos + Vec3.Up * (stemHeight * 0.92) + outward * (hookReach * 0.35);
                p1 = rootPos + Vec3.Up * (stemHeight * 0.70) + outward * hookReach;
                flowerDir = (Vec3.Down * 0.95 + outward * 0.25).Normalized();
            }
            else
            {
                // Upright post-pollination capsule: stem straightens vertically
                ctrl1 = rootPos + Vec3.Up * (stemHeight * 0.78) + outward * (radius * 0.06);
                p1 = rootPos + Vec3.Up * stemHeight + outward * (radius * 0.08);
                flowerDir = (Vec3.Up * 0.96 + outward * 0.20).Normalized();
            }

            int stemStations = detail == 2 ? 8 : (detail == 1 ? 12 : 16);
            var stemPoints = MarrowSampleBezier(p0, ctrl0, ctrl1, p1, stemStations);
            var stemRadii = new double[stemStations];
            for (int i = 0; i < stemStations; i++)
            {
                double t = i / (double)(stemStations - 1);
                stemRadii[i] = MathD.Lerp(stemBaseRadius, stemTipRadius, Math.Pow(t, 0.80));
            }

            // Loft the succulent ribbed stipe
            MarrowbloomFlutedStem(part, stemPoints, stemRadii, detail, ridges: 8);

            // 3. Imbricated Translucent Scales Clasping Along the Stipe
            int numBracts = detail == 2 ? 7 : (detail == 1 ? 11 : 16);
            double bractSpiralBase = rng.Range(0, Math.PI * 2);
            for (int b = 0; b < numBracts; b++)
            {
                double t = 0.04 + 0.88 * Math.Pow(b / (double)Math.Max(1, numBracts - 1), 0.80);
                int ptIdx = Math.Min(stemStations - 2, (int)(t * (stemStations - 1)));
                double frac = (t * (stemStations - 1)) - ptIdx;
                var bPos = Vec3.Lerp(stemPoints[ptIdx], stemPoints[ptIdx + 1], frac);
                var bTan = (stemPoints[ptIdx + 1] - stemPoints[ptIdx]).Normalized();
                double curRadius = MathD.Lerp(stemRadii[ptIdx], stemRadii[ptIdx + 1], frac);

                double bAz = bractSpiralBase + b * 2.399963; // Golden angle spiral
                var bOut = (outward * Math.Cos(bAz) + bTan.Cross(outward).Normalized() * Math.Sin(bAz)).Normalized();

                double bLen = stemHeight * (stage == 1 ? 0.075 : 0.095) * MathD.Lerp(1.15, 0.75, t);
                double bArc = MathD.Lerp(0.95, 0.55, t); // Covers ~110° at base, tapering to 65° near flower
                ulong bSeed = Rng.Mix(seed, (ulong)(s * 1000 + b * 31 + 7));

                MarrowbloomClaspingBract(part, bPos, bTan, bOut, curRadius, bLen, bArc, bSeed, detail);
            }

            // Calyx whorl of 4 protective bracts cupping the base of the flower
            int calyxBracts = 4;
            for (int cb = 0; cb < calyxBracts; cb++)
            {
                double cbAz = cb * (2.0 * Math.PI / calyxBracts) + rng.Range(-0.15, 0.15);
                var cbSide = flowerDir.Cross(Vec3.Up);
                if (cbSide.LengthSq < 1e-6) cbSide = flowerDir.Cross(new Vec3(1, 0, 0));
                cbSide = cbSide.Normalized();
                var cbFwd = cbSide.Cross(flowerDir).Normalized();
                var cbOut = (cbSide * Math.Cos(cbAz) + cbFwd * Math.Sin(cbAz)).Normalized();

                double cbLen = stemHeight * 0.085;
                ulong cbSeed = Rng.Mix(seed, (ulong)(s * 5000 + cb * 19 + 3));
                MarrowbloomClaspingBract(part, p1, flowerDir, cbOut, stemTipRadius * 1.1, cbLen, 0.70, cbSeed, detail);
            }

            // 4. Multi-Petaled Waxy Goblet Flower
            double flowerLength = stemHeight * (stage == 1 ? 0.13 : 0.17);
            double flowerRadius = flowerLength * 0.38;

            int flowerStart = part.VertexCount;

            // 5 distinct overlapping waxy petals forming the campanulate corolla
            int numPetals = 5;
            double petalWidth = (2.0 * Math.PI * flowerRadius / numPetals) * 1.35; // 35% lateral overlap
            double petalAzBase = rng.Range(0, Math.PI * 2);

            for (int p = 0; p < numPetals; p++)
            {
                double pAz = petalAzBase + p * (2.0 * Math.PI / numPetals);
                var pSide = flowerDir.Cross(Vec3.Up);
                if (pSide.LengthSq < 1e-6) pSide = flowerDir.Cross(new Vec3(1, 0, 0));
                pSide = pSide.Normalized();
                var pFwd = pSide.Cross(flowerDir).Normalized();
                var pOut = (pSide * Math.Cos(pAz) + pFwd * Math.Sin(pAz)).Normalized();

                ulong pSeed = Rng.Mix(seed, (ulong)(s * 8000 + p * 43 + 11));
                MarrowbloomPetal(part, p1, flowerDir, pOut, flowerLength, petalWidth, pSeed, detail);
            }

            // 5. Interior Floral Organs: Central Column, Funnel Stigma & Honey-Amber Anthers
            MarrowbloomFloralInterior(part, p1, flowerDir, flowerLength, rng, detail);

            // Register key floral anatomy in EssentialSpans so LOD never strips the flowers
            if (part.VertexCount > flowerStart)
            {
                part.EssentialSpans.Add((flowerStart, part.VertexCount - flowerStart));
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
