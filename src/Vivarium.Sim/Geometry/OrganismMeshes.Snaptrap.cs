using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Latchjaw (snaptrap): a predatory snap-trap plant built as a stout upright column of climbing
/// jaw-leaves topped with a large, fleshy, bioluminescent trumpet flower.
///
/// Distinctive botanical architecture:
///   1. Brussels-sprout column habit: A stout fibrous column with spiralling leaf nodes, clasping
///      leaf cushions, and old basal sheaths/fibrous shreds adhering to lower nodes.
///   2. Volumetric snap-traps: Each jaw-leaf connects via a succulent winged petiole and carries
///      two cupped, dual-walled lobes on a continuous central hinge. Outer abaxial shells are convex
///      and olive-green with longitudinal vein ridges; inner adaxial basins are concave with a rich
///      velvety crimson/magenta glandular nectar bed and translucent chartreuse margins.
///   3. Interlocking marginal cilia: Gracefully tapered, inward-curving marginal teeth that form an
///      interdigitating cage when approaching closure.
///   4. Sensory trigger hairs: Distinct triangular triad of mechanosensory trigger needles on each lobe.
///   5. Terminal fleshy trumpet flower: A campanulate 5-lobed bloom cradled by 5 recurved calyx sepals,
///      with undulating recurved petal lobes, a glowing crimson nectar throat, a central columnar
///      style with a 5-lobed star stigma, and 5 pollen-tipped stamens.
/// </summary>
public static partial class OrganismMeshes
{
    // Jaw-leaf size up the column: lowest are substantial, mid-column peak in size, top ones tighter buds.
    private static readonly double[] LatchjawBudT = { 0.00, 0.12, 0.28, 0.45, 0.62, 0.76, 0.88, 1.00 };
    private static readonly double[] LatchjawBudW = { 0.50, 0.82, 1.00, 1.00, 0.92, 0.76, 0.58, 0.38 };

    private static double LatchjawBudWidth(double t)
    {
        if (t <= 0) return LatchjawBudW[0];
        if (t >= 1) return LatchjawBudW[^1];
        for (int i = 0; i < LatchjawBudT.Length - 1; i++)
        {
            double t0 = LatchjawBudT[i], t1 = LatchjawBudT[i + 1];
            if (t < t0 || t > t1) continue;
            double u = (t - t0) / (t1 - t0), u2 = u * u, u3 = u2 * u;
            double v0 = LatchjawBudW[i], v1 = LatchjawBudW[i + 1];
            double p0 = LatchjawBudW[Math.Max(i - 1, 0)];
            double p3 = LatchjawBudW[Math.Min(i + 2, LatchjawBudW.Length - 1)];
            return 0.5 * (2 * v0 + (-p0 + v1) * u
                + (2 * p0 - 5 * v0 + 4 * v1 - p3) * u2
                + (-p0 + 3 * v0 - 3 * v1 + p3) * u3);
        }
        return LatchjawBudW[^1];
    }

    /// <summary>
    /// Builds one volumetric jaw-leaf: winged clasping petiole, dual-walled cupped lobes on a central
    /// hinge, curved interdigitating marginal teeth, and sensory trigger hairs.
    /// </summary>
    private static void LatchjawTrap(MeshData m, Rng rng, Vec3 attach, Vec3 forward, Vec3 leafUp,
        double length, double half, double open, double[] green, double[] crimson, ulong seed)
    {
        int leafStartVertex = m.VertexCount;
        int leafStartIndex = m.Indices.Count;

        var leafSide = leafUp.Cross(forward);
        if (leafSide.LengthSq < 1e-10) leafSide = forward.Cross(Vec3.Up);
        if (leafSide.LengthSq < 1e-10) leafSide = new Vec3(1, 0, 0);
        leafSide = leafSide.Normalized();
        leafUp = leafSide.Cross(forward).Normalized();

        // 1. Winged clasping petiole: connects stem cushion to the trap hinge.
        double petLength = length * 0.28;
        var trapBase = attach + forward * petLength;
        const int petSteps = 4;
        int petBaseV = m.VertexCount;

        for (int i = 0; i <= petSteps; i++)
        {
            double pt = i / (double)petSteps;
            var pos = Vec3.Lerp(attach, trapBase, pt) - leafUp * (length * 0.02 * Math.Sin(pt * Math.PI));
            // Petiole is broad at the stem cushion, slightly narrower at mid-length, and widens at hinge.
            double w = length * (0.16 + 0.12 * Math.Pow(1.0 - pt, 1.5) + 0.08 * pt * pt);
            double thk = length * (0.045 - 0.015 * pt);
            var col = Primitives.Mix(green, new[] { 0.28, 0.36, 0.18 }, (1.0 - pt) * 0.45);

            m.AddVertex(pos - leafSide * w, -leafSide, col, 1, pt, 0, 0, 1.0);
            m.AddVertex(pos + leafSide * w, leafSide, col, 1, pt, 1, 0, 1.0);
            m.AddVertex(pos + leafUp * thk, leafUp, col, 1, pt, 0.5, 0, 1.0);
            m.AddVertex(pos - leafUp * thk, -leafUp, col, 1, pt, 0.5, 0, 1.0);
        }

        for (int i = 0; i < petSteps; i++)
        {
            int r0 = petBaseV + i * 4;
            int r1 = r0 + 4;
            // Top face
            Primitives.TriangleFacing(m, r0 + 0, r1 + 0, r0 + 2, leafUp);
            Primitives.TriangleFacing(m, r1 + 0, r1 + 2, r0 + 2, leafUp);
            Primitives.TriangleFacing(m, r0 + 2, r1 + 2, r0 + 1, leafUp);
            Primitives.TriangleFacing(m, r1 + 2, r1 + 1, r0 + 1, leafUp);
            // Bottom face
            Primitives.TriangleFacing(m, r0 + 0, r0 + 3, r1 + 0, -leafUp);
            Primitives.TriangleFacing(m, r1 + 0, r0 + 3, r1 + 3, -leafUp);
            Primitives.TriangleFacing(m, r0 + 3, r0 + 1, r1 + 3, -leafUp);
            Primitives.TriangleFacing(m, r1 + 3, r0 + 1, r1 + 1, -leafUp);
        }

        // 2. Continuous Central Hinge (Midrib)
        Vec3 HingePoint(double t) =>
            trapBase + forward * (length * t) + leafUp * (length * 0.035 * Math.Sin(Math.PI * t));

        const int rows = 12;
        const int cols = 5;

        // 3. Bilateral Volumetric Cupped Lobes
        for (int sign = -1; sign <= 1; sign += 2)
        {
            var latDir = (leafSide * (sign * Math.Cos(open)) + leafUp * Math.Sin(open)).Normalized();
            var cupDepthDir = (-leafSide * (sign * Math.Sin(open)) + leafUp * Math.Cos(open)).Normalized();

            Vec3 MedianPoint(double u, double v)
            {
                double envelope = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * u)), 0.65);
                double w = half * envelope * v;
                // Hollow bowl curve in the center, delicate recurve flare toward the margin rim.
                double hollow = half * envelope * 0.28 * Math.Sin(Math.PI * v);
                double edgeRecurve = half * envelope * 0.06 * Math.Pow(v, 2.5);
                return HingePoint(u) + latDir * w - cupDepthDir * hollow + leafUp * edgeRecurve;
            }

            int outerStart = m.VertexCount;
            int innerStart = outerStart + (rows + 1) * (cols + 1);

            for (int r = 0; r <= rows; r++)
            {
                double u = 0.02 + 0.96 * (r / (double)rows);
                for (int c = 0; c <= cols; c++)
                {
                    double v = c / (double)cols;
                    var midP = MedianPoint(u, v);

                    // Compute smooth surface normal on median surface
                    double du = 0.015, dv = 0.02;
                    var along = MedianPoint(Math.Min(0.99, u + du), v) - MedianPoint(Math.Max(0.01, u - du), v);
                    var cross = MedianPoint(u, Math.Min(1.0, v + dv)) - MedianPoint(u, Math.Max(0.0, v - dv));
                    var outNorm = cross.Cross(along).Normalized() * sign;
                    if (outNorm.LengthSq < 1e-10) outNorm = leafUp;

                    // Fleshy lobe thickness: thicker near midrib, delicately tapering toward the margin
                    double thick = length * (0.018 - 0.010 * v);
                    var pOuter = midP + outNorm * (thick * 0.5);
                    var pInner = midP - outNorm * (thick * 0.5);

                    // Outer color: rich succulent green with subtle vein tone
                    var colOuter = Primitives.Mix(green, new[] { 0.20, 0.44, 0.15 }, v * 0.35);
                    colOuter = Primitives.Mix(colOuter, crimson, (1.0 - v) * 0.18);

                    // Inner color: velvety crimson nectar bed transitioning to pale translucent chartreuse rim
                    var deepCrimson = Primitives.Mix(crimson, new[] { 0.60, 0.08, 0.12 }, 0.65);
                    var rimChartreuse = new[] { 0.62, 0.72, 0.26 };
                    double rimFactor = MathD.SmoothStep(0.70, 0.98, v);
                    var colInner = Primitives.Mix(deepCrimson, rimChartreuse, rimFactor);

                    // UV2.y = 1.0 marks blade vertices
                    m.AddVertex(pOuter, outNorm, colOuter, 1, u, v, 0, 1.0);
                }
            }

            // Inner vertices
            for (int r = 0; r <= rows; r++)
            {
                double u = 0.02 + 0.96 * (r / (double)rows);
                for (int c = 0; c <= cols; c++)
                {
                    double v = c / (double)cols;
                    var midP = MedianPoint(u, v);

                    double du = 0.015, dv = 0.02;
                    var along = MedianPoint(Math.Min(0.99, u + du), v) - MedianPoint(Math.Max(0.01, u - du), v);
                    var cross = MedianPoint(u, Math.Min(1.0, v + dv)) - MedianPoint(u, Math.Max(0.0, v - dv));
                    var outNorm = cross.Cross(along).Normalized() * sign;
                    if (outNorm.LengthSq < 1e-10) outNorm = leafUp;

                    double thick = length * (0.018 - 0.010 * v);
                    var pInner = midP - outNorm * (thick * 0.5);

                    var deepCrimson = Primitives.Mix(crimson, new[] { 0.60, 0.08, 0.12 }, 0.65);
                    var rimChartreuse = new[] { 0.62, 0.72, 0.26 };
                    double rimFactor = MathD.SmoothStep(0.70, 0.98, v);
                    var colInner = Primitives.Mix(deepCrimson, rimChartreuse, rimFactor);

                    m.AddVertex(pInner, -outNorm, colInner, 1, u, v, 0, 1.0);
                }
            }

            // Triangulate outer and inner surfaces
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    // Outer face
                    int o0 = outerStart + r * (cols + 1) + c;
                    int o1 = o0 + 1;
                    int o2 = o0 + cols + 1;
                    int o3 = o2 + 1;
                    Primitives.TriangleFacing(m, o0, o2, o1, m.NormalAt(o0));
                    Primitives.TriangleFacing(m, o1, o2, o3, m.NormalAt(o1));

                    // Inner face
                    int i0 = innerStart + r * (cols + 1) + c;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols + 1;
                    int i3 = i2 + 1;
                    Primitives.TriangleFacing(m, i0, i1, i2, m.NormalAt(i0));
                    Primitives.TriangleFacing(m, i1, i3, i2, m.NormalAt(i1));
                }
            }

            // Connect outer and inner sheets at the margin rim (watertight volumetric boundary)
            for (int r = 0; r < rows; r++)
            {
                int oA = outerStart + r * (cols + 1) + cols;
                int oB = outerStart + (r + 1) * (cols + 1) + cols;
                int iA = innerStart + r * (cols + 1) + cols;
                int iB = innerStart + (r + 1) * (cols + 1) + cols;
                var rimNorm = latDir;
                Primitives.TriangleFacing(m, oA, oB, iA, rimNorm);
                Primitives.TriangleFacing(m, oB, iB, iA, rimNorm);
            }

            // 4. Interlocking Marginal Cilia (Curved Teeth)
            const int toothCount = 11;
            var toothBaseCol = new[] { 0.68, 0.76, 0.38 };
            var toothTipCol = new[] { 0.65, 0.18, 0.15 };

            for (int tooth = 0; tooth < toothCount; tooth++)
            {
                double tu = 0.10 + 0.80 * (tooth / (double)(toothCount - 1));
                var rimP = MedianPoint(tu, 1.0);
                double toothLen = half * 0.42 * Math.Sin(Math.PI * tu);
                if (toothLen < 1e-4) continue;

                // Spines curve upward and hook inward toward the opposite lobe
                var baseDir = (latDir * 0.4 + leafUp * 0.9).Normalized();
                var tipDir = (-latDir * 0.65 + leafUp * 0.75).Normalized();

                var p0 = rimP;
                var p1 = rimP + baseDir * (toothLen * 0.52);
                var p2 = p1 + tipDir * (toothLen * 0.58);

                Primitives.Tube(m, new[] { p0, p1, p2 },
                    new[] { length * 0.013, length * 0.007, length * 0.0018 }, 4,
                    (st, rad) =>
                    {
                        var tCol = Primitives.Mix(toothBaseCol, toothTipCol, st * 0.85);
                        return (tCol, 1.0, st, rad, 0, 1.0);
                    });
            }

            // 5. Sensory Trigger Hairs on inner crimson bed
            double[,] triggerCoords = { { 0.36, 0.45 }, { 0.50, 0.58 }, { 0.64, 0.45 } };
            for (int tr = 0; tr < 3; tr++)
            {
                double tu = triggerCoords[tr, 0];
                double tv = triggerCoords[tr, 1];
                var baseP = MedianPoint(tu, tv);
                var hairDir = (leafUp * 0.85 - latDir * 0.3).Normalized();
                double hLen = length * 0.14;

                var h0 = baseP;
                var h1 = baseP + hairDir * (hLen * 0.55);
                var h2 = baseP + hairDir * hLen + forward * (hLen * 0.15);

                Primitives.Tube(m, new[] { h0, h1, h2 },
                    new[] { length * 0.005, length * 0.0028, length * 0.0009 }, 4,
                    (st, rad) => (new[] { 0.45, 0.16, 0.14 }, 1.0, st, rad, 0, 1.0));
            }
        }

        // 6. Succulent Hinge Midrib Tube
        var hBase = HingePoint(0.0);
        var hMid = HingePoint(0.5);
        var hTip = HingePoint(1.0);
        Primitives.Tube(m, new[] { hBase, hMid, hTip },
            new[] { length * 0.026, length * 0.022, length * 0.010 }, 6,
            (st, rad) => (Primitives.Mix(green, crimson, 0.20), 1.0, st, rad, 0, 1.0));

        m.RecordLeaf(leafStartVertex, leafStartIndex, attach, length + petLength, volumetric: true);
    }

    /// <summary>
    /// Stalk: stout column built from twisting fluted fibre bundles and irregular nodal swellings.
    /// </summary>
    private static void LatchjawStalk(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        double fluteDepth, double twist, double[] colour)
    {
        const int sides = 14;
        const int flutes = 7;
        var rng = Rng.Keyed((ulong)(uint)m.VertexCount, "latchjaw.flutes", 0);
        var bundle = new double[sides];
        for (int s = 0; s < sides; s++) bundle[s] = 0.60 + rng.NextDouble() * 0.8;

        var tangents = new Vec3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == path.Count - 1) d = path[^1] - path[^2];
            else d = path[i + 1] - path[i - 1];
            tangents[i] = d.LengthSq > 1e-12 ? d.Normalized() : Vec3.Up;
        }

        var hint = Vec3.Up;
        var side = tangents[0].Cross(hint);
        if (side.LengthSq < 1e-10) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        int start = m.VertexCount;
        for (int i = 0; i < path.Count; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-10)
                {
                    side = dir.Cross(hint);
                    if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up = side.Cross(dir).Normalized();
            double t = (double)i / (path.Count - 1);

            for (int s = 0; s <= sides; s++)
            {
                double th = 2.0 * Math.PI * s / sides;
                double flute = 1.0 + fluteDepth * (Math.Cos(flutes * th + twist * t) * bundle[s % sides] - 0.5);
                double r = radii[i] * flute;
                var p = path[i] + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r);
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                var col = Primitives.Mix(colour, new[] { 0.28, 0.24, 0.14 }, MathD.SmoothStep(0.0, 0.8, 1.0 - t) * 0.35);
                m.AddVertex(p, radial, col, 1, t, s / (double)sides, 0, 0);
            }
        }

        for (int i = 0; i < path.Count - 1; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s;
                int b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }
        m.Structural.Add((start, m.VertexCount - start, sides, radii.Count > 0 ? radii[0] : 0));
    }

    /// <summary>
    /// A strip of dried fibrous leaf base adhering to and drooping from lower stem nodes.
    /// </summary>
    private static void LatchjawShred(MeshData m, Rng rng, Vec3 attach, Vec3 outward,
        double length, double width, double[] colour)
    {
        const int rows = 5;
        var down = new Vec3(0, -1, 0);
        var side = down.Cross(outward);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        int start = m.VertexCount;

        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            double peel = t * t * length * 0.38;
            var centre = attach + down * (t * length) + outward * peel;
            double w = width * (1.0 - 0.35 * t) * (0.55 + 0.45 * Math.Sin(t * 9.0 + rng.Range(0, 3)));
            m.AddVertex(centre - side * w, outward, colour, 1, 0, 0.5, 0, 0);
            m.AddVertex(centre + side * w, outward, colour, 1, 0, 1.0, 0, 0);
        }

        for (int i = 0; i < rows; i++)
        {
            int a = start + i * 2;
            Primitives.TriangleFacing(m, a, a + 2, a + 1, outward);
            Primitives.TriangleFacing(m, a + 1, a + 2, a + 3, outward);
        }
    }

    /// <summary>
    /// Micro-bristle / glandular trichome on the stalk.
    /// </summary>
    private static void LatchjawBristle(MeshData m, Rng rng, Vec3 basePoint, Vec3 outward,
        double length, double[] colour)
    {
        var bend = new Vec3(rng.Range(-0.4, 0.4), rng.Range(-0.6, 0.2), rng.Range(-0.4, 0.4)).Normalized();
        var mid = basePoint + outward * (length * 0.55) + bend * (length * 0.12);
        var tip = basePoint + outward * length + bend * (length * 0.35);
        Primitives.Tube(m, new[] { basePoint, mid, tip },
            new[] { length * 0.055, length * 0.03, length * 0.008 }, 4,
            (i, v) => (colour, 1, i * 0.5, v, 0, 0));
    }

    /// <summary>
    /// The magnificent terminal bloom: a sculptural 5-lobed campanulate trumpet flower cradled by
    /// 5 recurved calyx sepals, featuring dual-walled undulating recurved petal lobes, a deep
    /// bioluminescent velvety crimson nectar throat, a central style with 5-lobed star stigma,
    /// and 5 golden-anthered stamens.
    /// </summary>
    private static void LatchjawFlower(MeshData m, Vec3 basePoint, Vec3 axis, double radius,
        double[] outerCol, double[] innerCol, double[] throatCol, ulong seed)
    {
        var rng = Rng.Keyed(seed, "latchjaw.flower", 0);
        axis = axis.Normalized();
        var side = axis.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = axis.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(axis).Normalized();

        double flowerLen = radius * 1.50;
        double phase = rng.Range(0.0, Math.Tau);

        // 1. Swollen Floral Receptacle & 5 Calyx Sepals
        double recipRadius = radius * 0.22;
        double recipHeight = flowerLen * 0.14;
        Primitives.Ellipsoid(m, basePoint + axis * (recipHeight * 0.4), new Vec3(recipRadius, recipRadius, recipRadius), 8, 8,
            (u, v) => (Primitives.Mix(outerCol, new[] { 0.22, 0.34, 0.14 }, 0.55), 1.0, u, v, 0, 0));

        // 5 Calyx Sepals clasping the corolla base
        const int calyxCount = 5;
        for (int k = 0; k < calyxCount; k++)
        {
            double sepalAz = k * (Math.Tau / calyxCount) + rng.Range(-0.05, 0.05);
            var sepalRad = (side * Math.Cos(sepalAz) + up * Math.Sin(sepalAz)).Normalized();
            var sepalTan = (-side * Math.Sin(sepalAz) + up * Math.Cos(sepalAz)).Normalized();

            int sepalBaseV = m.VertexCount;
            const int sepalSteps = 6;
            for (int s = 0; s <= sepalSteps; s++)
            {
                double st = s / (double)sepalSteps;
                // Sepal curves up and tips recurve outward
                double z = flowerLen * 0.36 * st;
                double rOut = recipRadius * 0.95 + radius * 0.12 * st * st + (st > 0.7 ? (st - 0.7) * radius * 0.15 : 0);
                var cP = basePoint + axis * z + sepalRad * rOut;
                double sepalW = radius * 0.075 * Math.Sin(Math.PI * st * 0.95);
                var sepalCol = Primitives.Mix(new[] { 0.24, 0.38, 0.15 }, new[] { 0.52, 0.14, 0.18 }, st * 0.45);

                m.AddVertex(cP - sepalTan * sepalW, sepalRad, sepalCol, 1, st, 0, 0, 0.8);
                m.AddVertex(cP + sepalTan * sepalW, sepalRad, sepalCol, 1, st, 1, 0, 0.8);
            }

            for (int s = 0; s < sepalSteps; s++)
            {
                int a = sepalBaseV + s * 2;
                Primitives.TriangleFacing(m, a, a + 2, a + 1, sepalRad);
                Primitives.TriangleFacing(m, a + 1, a + 2, a + 3, sepalRad);
            }
        }

        // 2. Sculptural 5-Lobed Campanulate Corolla (Outer & Inner Dual Wall)
        const int rings = 18;
        const int ringSides = 30; // Divisible by 5 for perfect 5-lobed petal symmetry

        // Longitudinal corolla profile functions
        double CorollaRadius(double t)
        {
            // Narrow basal tube flaring into a broad trumpet
            return radius * (0.16 + 0.84 * Math.Pow(t, 2.3));
        }

        double CorollaCurl(double t)
        {
            // Elegant recurve at the open petal mouth
            if (t < 0.78) return 0.0;
            double u = (t - 0.78) / 0.22;
            return -flowerLen * 0.15 * Math.Pow(u, 1.8);
        }

        int outerCorollaStart = m.VertexCount;
        int innerCorollaStart = outerCorollaStart + (rings + 1) * (ringSides + 1);

        for (int r = 0; r <= rings; r++)
        {
            double t = r / (double)rings;
            double baseR = CorollaRadius(t);
            double z = flowerLen * t + CorollaCurl(t);
            double wallThick = radius * (0.024 - 0.012 * t);

            for (int s = 0; s <= ringSides; s++)
            {
                double th = 2.0 * Math.PI * s / ringSides;
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();

                // 5 petal lobes + fine organic ruffling
                double lobeCos = Math.Cos(5.0 * th);
                double lobeMod = 1.0 + 0.19 * lobeCos * Math.Pow(t, 2.2);
                double rippleMod = 1.0 + 0.040 * Math.Sin(15.0 * th + phase) * Math.Pow(t, 3.2);

                double rOuter = baseR * lobeMod * rippleMod;
                double rInner = Math.Max(radius * 0.02, rOuter - wallThick);

                var pOuter = basePoint + axis * z + radial * rOuter;
                var pInner = basePoint + axis * z + radial * rInner;

                // Outer petal color: waxy ivory-chartreuse with crimson petal-rib stripes and blush
                var colOuter = Primitives.Mix(outerCol, new[] { 0.86, 0.88, 0.72 }, MathD.SmoothStep(0.2, 0.8, t) * 0.65);
                double ribFactor = Math.Max(0.0, lobeCos);
                colOuter = Primitives.Mix(colOuter, new[] { 0.58, 0.12, 0.16 }, ribFactor * 0.35 * Math.Pow(t, 1.5));
                if (t > 0.85) colOuter = Primitives.Mix(colOuter, innerCol, (t - 0.85) / 0.15 * 0.4);

                // Outward normal
                var outNorm = (radial * (1.0 + 0.3 * t) + axis * 0.2).Normalized();
                m.AddVertex(pOuter, outNorm, colOuter, 1, t, s / (double)ringSides, 0, 1.0);
            }
        }

        // Inner throat vertices
        for (int r = 0; r <= rings; r++)
        {
            double t = r / (double)rings;
            double baseR = CorollaRadius(t);
            double z = flowerLen * t + CorollaCurl(t);
            double wallThick = radius * (0.024 - 0.012 * t);

            for (int s = 0; s <= ringSides; s++)
            {
                double th = 2.0 * Math.PI * s / ringSides;
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();

                double lobeCos = Math.Cos(5.0 * th);
                double lobeMod = 1.0 + 0.19 * lobeCos * Math.Pow(t, 2.2);
                double rippleMod = 1.0 + 0.040 * Math.Sin(15.0 * th + phase) * Math.Pow(t, 3.2);

                double rOuter = baseR * lobeMod * rippleMod;
                double rInner = Math.Max(radius * 0.02, rOuter - wallThick);
                var pInner = basePoint + axis * z + radial * rInner;

                // Inner throat color: rich velvety wine-crimson with pale nectar-guide valleys
                var deepCrimson = Primitives.Mix(innerCol, new[] { 0.52, 0.07, 0.11 }, 0.65);
                double nectarGuide = Math.Max(0.0, -lobeCos);
                var colInner = Primitives.Mix(deepCrimson, new[] { 0.78, 0.32, 0.36 }, nectarGuide * 0.30);

                // Bioluminescent emission in deep throat chamber
                double emission = 0;
                if (t <= 0.22)
                {
                    double glowT = MathD.SmoothStep(0.22, 0.02, t);
                    emission = 1.4 * glowT;
                    colInner = Primitives.Mix(colInner, throatCol, glowT * 0.85);
                }

                var inNorm = (-radial * (1.0 + 0.3 * t) - axis * 0.2).Normalized();
                m.AddVertex(pInner, inNorm, colInner, 1, t, s / (double)ringSides, emission, 1.0);
            }
        }

        // Triangulate outer corolla wall
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < ringSides; s++)
            {
                int a = outerCorollaStart + r * (ringSides + 1) + s;
                int b = a + ringSides + 1;
                Primitives.TriangleFacing(m, a, b, a + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a + 1, b, b + 1, m.NormalAt(a + 1));
            }
        }

        // Triangulate inner throat wall
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < ringSides; s++)
            {
                int a = innerCorollaStart + r * (ringSides + 1) + s;
                int b = a + ringSides + 1;
                Primitives.TriangleFacing(m, a, a + 1, b, m.NormalAt(a));
                Primitives.TriangleFacing(m, a + 1, b + 1, b, m.NormalAt(a + 1));
            }
        }

        // Connect outer and inner walls at the corolla mouth rim (succulent rolled lip)
        int lastRingO = outerCorollaStart + rings * (ringSides + 1);
        int lastRingI = innerCorollaStart + rings * (ringSides + 1);
        for (int s = 0; s < ringSides; s++)
        {
            int o0 = lastRingO + s;
            int o1 = o0 + 1;
            int i0 = lastRingI + s;
            int i1 = i0 + 1;
            var rimNorm = axis;
            Primitives.TriangleFacing(m, o0, o1, i0, rimNorm);
            Primitives.TriangleFacing(m, o1, i1, i0, rimNorm);
        }

        // Bottom throat seal
        int bOuter = outerCorollaStart;
        int bInner = innerCorollaStart;
        for (int s = 0; s < ringSides; s++)
        {
            Primitives.TriangleFacing(m, bOuter + s, bInner + s, bOuter + s + 1, -axis);
            Primitives.TriangleFacing(m, bOuter + s + 1, bInner + s, bInner + s + 1, -axis);
        }

        // 3. Central Style & 5-Lobed Stigma
        var styleBase = basePoint + axis * (flowerLen * 0.08);
        var styleTip = basePoint + axis * (flowerLen * 0.46);
        Primitives.Tube(m, new[] { styleBase, styleTip },
            new[] { radius * 0.038, radius * 0.024 }, 6,
            (st, rad) => (new[] { 0.88, 0.86, 0.78 }, 1.0, st, rad, 0, 1.0));

        // 5-Lobed Stigma Disc
        const int stigmaSides = 10;
        int stigmaBaseV = m.VertexCount;
        m.AddVertex(styleTip + axis * (radius * 0.015), axis, new[] { 0.85, 0.72, 0.32 }, 1, 0, 0, 0.3, 1.0);
        for (int s = 0; s <= stigmaSides; s++)
        {
            double th = 2.0 * Math.PI * s / stigmaSides;
            var sRad = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
            double sMod = 1.0 + 0.35 * Math.Cos(5.0 * th);
            var p = styleTip + sRad * (radius * 0.065 * sMod);
            m.AddVertex(p, axis, new[] { 0.90, 0.78, 0.35 }, 1, 0.5, s / (double)stigmaSides, 0.3, 1.0);
        }
        for (int s = 0; s < stigmaSides; s++)
        {
            Primitives.TriangleFacing(m, stigmaBaseV, stigmaBaseV + 1 + s, stigmaBaseV + 2 + s, axis);
        }

        // 4. Five Pollen Stamens
        for (int st = 0; st < 5; st++)
        {
            double stAz = st * (Math.Tau / 5) + 0.314;
            var stRad = (side * Math.Cos(stAz) + up * Math.Sin(stAz)).Normalized();
            var stOrigin = basePoint + axis * (flowerLen * 0.22) + stRad * (radius * 0.16);
            var stArch = basePoint + axis * (flowerLen * 0.42) + stRad * (radius * 0.10);
            var stTip = basePoint + axis * (flowerLen * 0.44) + stRad * (radius * 0.055);

            Primitives.Tube(m, new[] { stOrigin, stArch, stTip },
                new[] { radius * 0.014, radius * 0.010, radius * 0.006 }, 4,
                (i, v) => (new[] { 0.84, 0.80, 0.68 }, 1.0, i, v, 0, 1.0));

            // Golden Pollen Anther
            double antherR = radius * 0.022;
            Primitives.Ellipsoid(m, stTip, new Vec3(antherR, antherR * 1.5, antherR), 5, 5,
                (u, v) => (new[] { 0.94, 0.76, 0.20 }, 1.0, u, v, 0.2, 1.0));
        }
    }

    /// <summary>
    /// The latchjaw: an upright column carrying climbing jaw-leaves all the way up, crowned with
    /// one large, fleshy, bioluminescent trumpet flower.
    /// </summary>
    private static void LatchJawColumn(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = seed };

        var green = Primitives.Mix(c1, new[] { 0.085, 0.215, 0.075 }, 0.55);
        var crimson = Primitives.Mix(c2, new[] { 0.42, 0.075, 0.085 }, 0.55);
        var stemColour = Primitives.Mix(new[] { 0.42, 0.36, 0.18 }, green, 0.35);
        var petalOuter = Primitives.Mix(green, new[] { 0.42, 0.58, 0.20 }, 0.55);
        var petalInner = Primitives.Mix(crimson, new[] { 0.65, 0.10, 0.14 }, 0.60);
        var throatColour = new[] { 0.88, 0.12, 0.16 };

        double columnHeight = height * (juvenile ? 0.68 : 0.76);
        double columnRadius = radius * 0.048;

        // 1. Column spine and radius functions
        var lean = new Vec3(rng.Range(-0.03, 0.03), 0, rng.Range(-0.03, 0.03));
        var wander = new Vec3(rng.Range(-0.4, 0.4), 0, rng.Range(-0.4, 0.4)).Normalized();
        double wanderPhase = rng.Range(0.0, Math.Tau);

        Vec3 StalkPos(double t) =>
            lean * (t * t * columnHeight) + Vec3.Up * (columnHeight * t)
            + wander * (columnHeight * 0.11 * Math.Sin(t * 2.6 + wanderPhase));

        Vec3 StalkTangent(double t)
        {
            double dt = 0.005;
            var p0 = StalkPos(Math.Max(0.0, t - dt));
            var p1 = StalkPos(Math.Min(1.0, t + dt));
            var delta = p1 - p0;
            return delta.LengthSq > 1e-12 ? delta.Normalized() : Vec3.Up;
        }

        double StalkRadius(double t)
        {
            double node = Math.Pow(Math.Abs(Math.Sin(t * Math.PI * 5.0)), 6.0);
            double lump = 1.0 + 0.28 * Math.Exp(-t * 5.0) + 0.38 * node + 0.07 * Math.Sin(t * 29.0);
            return columnRadius * (1.0 - 0.46 * t) * lump;
        }

        const int columnSteps = 20;
        var path = new Vec3[columnSteps + 1];
        var radii = new double[columnSteps + 1];
        for (int i = 0; i <= columnSteps; i++)
        {
            double t = i / (double)columnSteps;
            path[i] = StalkPos(t);
            radii[i] = StalkRadius(t);
        }
        LatchjawStalk(part, path, radii, 0.46, 4.4, stemColour);

        // 2. Basal sheaths: papery bracts clasping the column foot
        int sheaths = juvenile ? 5 : 9;
        for (int k = 0; k < sheaths; k++)
        {
            double a = rng.Range(0.0, Math.Tau);
            double h = columnHeight * rng.Range(0.05, 0.22);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var baseP = outward * (columnRadius * 0.92);
            var tip = baseP + outward * (columnRadius * 1.15) + Vec3.Up * (h * 0.82);
            var mid = Vec3.Lerp(baseP, tip, 0.5) + outward * (columnRadius * 0.5);

            int s0 = part.AddVertex(baseP, outward, stemColour, 1, 0, 0.5, 0, 0);
            int s1 = part.AddVertex(mid, outward, stemColour, 1, 0.5, 0.5, 0, 0);
            int s2 = part.AddVertex(tip, outward, stemColour, 1, 1, 0.5, 0, 0);
            Primitives.TriangleFacing(part, s0, s1, s2, outward);
        }

        // Fibrous dead shreds along lower nodes
        int shreds = juvenile ? 10 : 24;
        for (int k = 0; k < shreds; k++)
        {
            double a = rng.Range(0.0, Math.Tau);
            double t = rng.Range(0.02, 0.74);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var center = StalkPos(t);
            var tangent = StalkTangent(t);
            var outwardNorm = (outward - tangent * outward.Dot(tangent)).Normalized();
            var baseP = center + outwardNorm * (StalkRadius(t) * rng.Range(0.85, 1.0));

            LatchjawShred(part, rng, baseP, outwardNorm,
                columnHeight * rng.Range(0.14, 0.30), columnRadius * rng.Range(0.18, 0.42),
                Primitives.Mix(stemColour, new[] { 0.44, 0.38, 0.20 }, rng.Range(0.25, 0.7)));
        }

        // Micro-bristles catching the light
        int bristles = juvenile ? 18 : 42;
        for (int k = 0; k < bristles; k++)
        {
            double a = rng.Range(0.0, Math.Tau);
            double t = rng.Range(0.0, 0.95);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var center = StalkPos(t);
            var tangent = StalkTangent(t);
            var outwardNorm = (outward - tangent * outward.Dot(tangent)).Normalized();
            var baseP = center + outwardNorm * (StalkRadius(t) * 0.95);

            LatchjawBristle(part, rng, baseP, outwardNorm, columnRadius * rng.Range(0.35, 0.8),
                Primitives.Mix(stemColour, new[] { 0.50, 0.52, 0.26 }, 0.5));
        }

        // 3. The Climbing Jaw-Leaves (Snap-Traps)
        int leaves = juvenile ? 7 : 12;
        for (int k = 0; k < leaves; k++)
        {
            double t = 0.06 + 0.80 * (k / (double)(leaves - 1));
            double az = k * 2.399963229728653; // Golden angle phyllotaxis
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));

            var center = StalkPos(t);
            var tangent = StalkTangent(t);
            var stalkR = StalkRadius(t);

            var outwardNorm = outward - tangent * outward.Dot(tangent);
            if (outwardNorm.LengthSq < 1e-10) outwardNorm = new Vec3(1, 0, 0);
            outwardNorm = outwardNorm.Normalized();

            // Lower traps clasp the column; upper ones lean out for the terminal flower
            double leanAngle = rng.Range(0.82, 1.15) - 0.35 * t;
            var forward = (outwardNorm * Math.Cos(leanAngle) + Vec3.Up * Math.Sin(leanAngle)).Normalized();
            var leafUp = tangent.Cross(forward);
            if (leafUp.LengthSq < 1e-10) leafUp = Vec3.Up.Cross(forward);
            leafUp = leafUp.Normalized();

            // Firm attachment precisely anchored to the stalk surface
            var attach = center + outwardNorm * (stalkR * 0.92);

            double size = LatchjawBudWidth(t) * height * 0.185;
            double half = size * rng.Range(0.38, 0.46);
            double open = rng.Range(0.18, 0.36);
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 977 + 13));

            LatchjawTrap(part, rng, attach, forward, leafUp, size, half, open, green, crimson, lSeed);
        }

        // 4. The Terminal Fleshy Trumpet Flower
        var flowerBase = path[^1] + Vec3.Up * (columnHeight * 0.05);
        var axis = (Vec3.Up + lean * 0.2 + new Vec3(rng.Range(-0.08, 0.08), 0, rng.Range(-0.08, 0.08))).Normalized();
        double flowerRadius = height * 0.30;
        LatchjawFlower(part, flowerBase, axis, flowerRadius, petalOuter, petalInner, throatColour,
            Rng.Mix(seed, 4211));

        // 5. Crown of Involucral Bracts under the flower
        int bracts = juvenile ? 4 : 6;
        for (int k = 0; k < bracts; k++)
        {
            double az = k * 2.399963229728653;
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var forward = (outward * 0.55 + Vec3.Up * 0.84).Normalized();
            var leafUp = Vec3.Up.Cross(forward);
            if (leafUp.LengthSq < 1e-10) leafUp = new Vec3(1, 0, 0);
            leafUp = leafUp.Normalized();

            var attach = flowerBase - Vec3.Up * (height * 0.065);
            double size = height * 0.065;
            LatchjawTrap(part, rng, attach, forward, leafUp, size, size * 0.22, 0.65, green, crimson,
                Rng.Mix(seed, (ulong)(k * 613 + 7)));
        }

        AppendWorld(m, part, radius, height);
    }
}
