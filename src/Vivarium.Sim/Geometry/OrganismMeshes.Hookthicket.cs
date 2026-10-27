using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Hookthicket: a bramble thicket (<i>Rubus</i>, blackberry/raspberry) for the 'hookthicket' species.
///
/// The species was a generic arching brake; it is rebuilt here as a real bramble: a knotted perennial
/// crown throwing up arching, angular canes studded with recurved prickles, each cane carrying
/// palmately compound leaves of five serrated leaflets on long petioles with narrow clasping
/// stipules, pale five-petalled flowers and dark aggregate berries.
///
/// The bramble is primal in the way a bramble is primal: it propagates by tip layering. A mature
/// cane reaches the soil, roots where it lands, and throws up fresh shoots of its own, so the thicket
/// spreads by making copies of itself. Here those new shoots come up in a vivid young green against
/// the near-black old wood, and the berries carry a faint light of their own after dark.
///
/// Reference renders: scripts/visuals/hookthicket/bramble_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    // A bramble leaflet is broadest a little below the middle and tapers to a sharp point.
    private static readonly double[] BrambleLeafletT = { 0.00, 0.10, 0.22, 0.36, 0.50, 0.64, 0.76, 0.86, 0.94, 1.00 };
    private static readonly double[] BrambleLeafletW = { 0.18, 0.42, 0.62, 0.78, 0.86, 0.82, 0.68, 0.48, 0.26, 0.05 };


    // Leaflet relief, as fractions of the leaflet's own length.
    private const double BrambleKeel = 0.030;   // raised midrib
    private const double BrambleWrinkle = 0.020; // rugose surface between the lateral veins
    private const double BrambleDroop = 0.055;  // the blade falling towards its tip
    private const double BrambleTooth = 0.042;  // depth of the serrations

    private static double BrambleLeafletBreadth(double t)
    {
        if (t <= 0) return BrambleLeafletW[0];
        if (t >= 1) return BrambleLeafletW[^1];
        for (int i = 0; i < BrambleLeafletT.Length - 1; i++)
        {
            double t0 = BrambleLeafletT[i], t1 = BrambleLeafletT[i + 1];
            if (t < t0 || t > t1) continue;
            double u = (t - t0) / (t1 - t0), u2 = u * u, u3 = u2 * u;
            double v0 = BrambleLeafletW[i], v1 = BrambleLeafletW[i + 1];
            double p0 = BrambleLeafletW[Math.Max(i - 1, 0)];
            double p3 = BrambleLeafletW[Math.Min(i + 2, BrambleLeafletW.Length - 1)];
            return 0.5 * (2 * v0 + (-p0 + v1) * u
                + (2 * p0 - 5 * v0 + 4 * v1 - p3) * u2
                + (-p0 + 3 * v0 - 3 * v1 + p3) * u3);
        }
        return BrambleLeafletW[^1];
    }

    /// <summary>
    /// A tube with an uneven, angular section. A bramble cane is pentagonal in cross-section, and the
    /// irregularity is what makes it read as wood rather than as pipe. The vertex layout matches
    /// <see cref="Primitives.Tube"/>: a ring per path point, sides plus one duplicated seam vertex.
    /// </summary>
    private static void BrambleTube(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        int sides, double[] section, Func<int, double, (double[], double, double, double, double, double)> attr)
    {
        if (path.Count < 2 || radii.Count != path.Count) return;
        int start = m.VertexCount;
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

        // An uneven section would pull each ring's centre off the path, so the section's own
        // centroid is measured once and taken back out: the cane keeps its lumpy section and still
        // runs exactly along its axis.
        double sectionCx = 0, sectionCy = 0;
        for (int s = 0; s < sides; s++)
        {
            sectionCx += section[s] * Math.Cos(2.0 * Math.PI * s / sides);
            sectionCy += section[s] * Math.Sin(2.0 * Math.PI * s / sides);
        }
        sectionCx /= sides;
        sectionCy /= sides;

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
            for (int s = 0; s <= sides; s++)
            {
                double th = 2.0 * Math.PI * s / sides;
                double scale = section[s % sides];
                double r = radii[i];
                // The section scales the ring; the measured centroid is taken back out so the ring
                // stays centred on the path and the cane keeps its axis.
                var p = path[i]
                    + side * ((scale * Math.Cos(th) - sectionCx) * r)
                    + up * ((scale * Math.Sin(th) - sectionCy) * r);
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                var a = attr(i, (double)s / sides);
                m.AddVertex(p, radial, a.Item1, a.Item2, a.Item3, a.Item4, a.Item5, a.Item6);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s;
                int b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        m.Structural.Add((start, m.VertexCount - start, sides, radii.Count > 0 ? radii[0] : 0));
    }

    /// <summary>
    /// One bramble leaflet: ovate, sharply toothed and rugose, the way a blackberry leaf is wrinkled
    /// between its lateral veins.
    /// </summary>
    private static void BrambleLeaflet(MeshData m, Vec3 basePoint, Vec3 direction, Vec3 normal,
        double length, double breadth, int rows, int cols, double toothDepth, ulong seed,
        double[] topColour, double[] backColour)
    {
        if (length <= 1e-5) return;
        var rng = Rng.Keyed(seed, "bramble.leaflet", 0);
        double phase = rng.Range(0.0, 1.0);

        var side = normal.Cross(direction);
        if (side.LengthSq < 1e-10) side = direction.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(direction).Normalized();

        Vec3 Surface(double t, double x, double tooth)
        {
            double w = breadth * length * 0.5 * BrambleLeafletBreadth(t);
            double lateral = x * w * (1.0 + tooth);
            // A bramble leaf is rugose: it bulges between the lateral veins and dips along them.
            double wrinkle = BrambleWrinkle * Math.Cos(t * Math.PI * 6.0 + phase * 6.28) * (1.0 - Math.Abs(x) * 0.6);
            double keel = BrambleKeel * (1.0 - Math.Abs(x)) * (1.0 - Math.Abs(x));
            double droop = -BrambleDroop * t * t;
            return basePoint + direction * (t * length) + side * lateral + up * ((keel + wrinkle + droop) * length);
        }

        const double eps = 0.006;
        int firstVertex = m.VertexCount;
        int firstIndex = m.Indices.Count;
        var grid = new int[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            double t = i / (double)(rows - 1);
            // Teeth alternate from row to row, each pointing towards the tip of the leaflet.
            int teeth = Math.Max(3, (rows - 1) / 2);
            double phaseRow = i % 2 == 0 ? phase : phase + 0.5 / teeth;
            double s = t * teeth + phaseRow;
            double frac = s - Math.Floor(s);
            double tooth = toothDepth * (Math.Pow(1.0 - frac, 1.3) - 0.45);
            for (int j = 0; j < cols; j++)
            {
                double x = (j / (double)(cols - 1)) * 2.0 - 1.0;
                double edge = (j == 0 || j == cols - 1) ? tooth : 0.0;
                var p = Surface(t, x, edge);
                var dT = Surface(Math.Min(1.0, t + eps), x, edge) - Surface(Math.Max(0.0, t - eps), x, edge);
                var dX = Surface(t, Math.Min(1.0, x + eps), edge) - Surface(t, Math.Max(-1.0, x - eps), edge);
                var n = dX.Cross(dT);
                if (n.LengthSq < 1e-14) n = up;
                n = n.Normalized();
                if (n.Dot(up) < 0) n = -n;
                double u = t;
                double v = (x + 1.0) * 0.5;
                grid[i, j] = m.AddVertex(p, n, Primitives.Mix(topColour, backColour, 0.10 + 0.45 * t), 1.0, u, v, 0.0, 1.0);
            }
        }
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols - 1; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                Primitives.TriangleFacing(m, a, c, b, up);
                Primitives.TriangleFacing(m, b, c, d, up);
            }
        m.RecordLeaf(firstVertex, firstIndex, basePoint, length);
    }

    /// <summary>
    /// A stout, recurved, barbed prickle. Real bramble prickles are small; these are the size that
    /// makes a thicket something you do not put a hand into.
    /// </summary>
    private static void BramblePrickle(MeshData m, Vec3 basePoint, Vec3 outward, Vec3 tangent,
        double length, double[] colour)
    {
        var side = outward.Cross(tangent);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        double baseR = length * 0.22;
        var root = basePoint - tangent * (length * 0.16);
        var elbow = basePoint + outward * (length * 0.58);
        var tip = basePoint + outward * length - tangent * (length * 0.62);
        var barb = basePoint + outward * (length * 0.30) + tangent * (length * 0.16) + side * (length * 0.16);
        void Tri(Vec3 a, Vec3 b, Vec3 c)
        {
            var n = (b - a).Cross(c - a);
            if (n.LengthSq < 1e-14) n = outward;
            n = n.Normalized();
            int v0 = m.AddVertex(a, n, colour, 1, 0, 0.5, 2, 0);
            int v1 = m.AddVertex(b, n, colour, 1, 1, 0.5, 2, 0);
            int v2 = m.AddVertex(c, n, colour, 1, 0.5, 1, 2, 0);
            Primitives.TriangleFacing(m, v0, v1, v2, n);
        }
        Tri(root - side * baseR, root + side * baseR, elbow);
        Tri(root + side * baseR, elbow, tip);
        Tri(root - side * baseR, elbow, barb);
        Tri(elbow, barb, tip);
    }

    /// <summary>The narrow stipule that clasps the petiole base, a bramble's signature detail.</summary>
    private static void BrambleStipule(MeshData m, Vec3 basePoint, Vec3 outward, double length,
        double[] colour, ulong seed)
    {
        var rng = Rng.Keyed(seed, "bramble.stipule", 0);
        var side = Vec3.Up.Cross(outward);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var tip = basePoint + outward * length + Vec3.Up * (length * 0.22);
        int start = m.VertexCount;
        m.AddVertex(basePoint - side * (length * 0.12), side, colour, 1, 0, 0.0, 0.0, 1.0);
        m.AddVertex(basePoint + side * (length * 0.12), side, colour, 1, 0, 1.0, 0.0, 1.0);
        m.AddVertex(tip - side * (length * 0.06), side, colour, 1, 1, 0.15, 0.0, 1.0);
        m.AddVertex(tip + side * (length * 0.06), side, colour, 1, 1, 0.85, 0.0, 1.0);
        Primitives.TriangleFacing(m, start, start + 2, start + 1, side);
        Primitives.TriangleFacing(m, start + 1, start + 2, start + 3, side);
    }

    /// <summary>
    /// An aggregate berry: a cluster of drupelets around a hollow core. The core carries the nodule
    /// marker, so the fruit is the only part of the brake that lights up after dark.
    /// </summary>
    private static void BrambleBerry(MeshData m, Vec3 centre, double radius, double[] drupelet,
        double[] heartColour, ulong seed)
    {
        var rng = Rng.Keyed(seed, "bramble.berry", 0);

        // Reflexed calyx sepals clasping the base of the berry cluster where the pedicel joins.
        var calyxColour = new[] { 0.14, 0.22, 0.09 };
        for (int sep = 0; sep < 5; sep++)
        {
            double th = sep * 2.0 * Math.PI / 5.0 + 0.35;
            var sdir = new Vec3(Math.Cos(th), 0.22, Math.Sin(th)).Normalized();
            var sside = Vec3.Up.Cross(sdir).Normalized();
            var b = centre + sdir * (radius * 0.15) - Vec3.Up * (radius * 0.03);
            var tip = centre + sdir * (radius * 0.65) + Vec3.Up * (radius * 0.10);
            int start = m.VertexCount;
            m.AddVertex(b - sside * (radius * 0.16), Vec3.Up, calyxColour, 1, 0, 0.5, 2.0, 0.0);
            m.AddVertex(b + sside * (radius * 0.16), Vec3.Up, calyxColour, 1, 1, 0.5, 2.0, 0.0);
            m.AddVertex(tip, Vec3.Up, calyxColour, 1, 0.5, 1.0, 2.0, 0.0);
            Primitives.TriangleFacing(m, start, start + 1, start + 2, Vec3.Up);
            Primitives.TriangleFacing(m, start, start + 2, start + 1, -Vec3.Up);
        }

        int drupelets = 9;
        for (int d = 0; d < drupelets; d++)
        {
            double y = 1.0 - (d + 0.5) / drupelets;
            double rad = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
            double theta = d * 2.399963229728653;
            var c = centre + new Vec3(Math.Cos(theta) * rad * radius * 0.86, y * radius * 0.92, Math.Sin(theta) * rad * 0.86);
            double r = radius * 0.36;
            const int rings = 2, sides = 6;
            int start = m.VertexCount;
            for (int rr = 0; rr <= rings; rr++)
            {
                double phi = Math.PI * rr / rings;
                for (int s = 0; s <= sides; s++)
                {
                    double th = 2.0 * Math.PI * s / sides;
                    var p = c + new Vec3(Math.Sin(phi) * Math.Cos(th) * r, Math.Cos(phi) * r, Math.Sin(phi) * Math.Sin(th) * r);
                    var n = (p - c);
                    if (n.LengthSq < 1e-12) n = Vec3.Up;
                    m.AddVertex(p, n.Normalized(), drupelet, 1, 0.5, 0.5, 0.0, 0.0);
                }
            }
            for (int rr = 0; rr < rings; rr++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + rr * (sides + 1) + s;
                    Primitives.TriangleFacing(m, a, a + sides + 1, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, a + sides + 1, a + sides + 2, Vec3.Up);
                }
        }
        // The glowing heart of the fruit.
        var heart = centre + Vec3.Up * (radius * 0.18);
        int hs = m.VertexCount;
        for (int s = 0; s <= 6; s++)
        {
            double th = 2.0 * Math.PI * s / 6;
            var p = heart + new Vec3(Math.Cos(th) * radius * 0.30, 0, Math.Sin(th) * radius * 0.30);
            m.AddVertex(p, Vec3.Up, heartColour, 1, 0.5, 0.5, 1.4, 1.0);
        }
        for (int s = 0; s < 6; s++) Primitives.TriangleFacing(m, hs, hs + s, hs + s + 1, Vec3.Up);
    }

    /// <summary>A pale five-petalled bramble flower with a crown of stamens.</summary>
    private static void BrambleFlower(MeshData m, Vec3 centre, double radius, double[] petalColour,
        double[] stamenColour, ulong seed)
    {
        var rng = Rng.Keyed(seed, "bramble.flower", 0);

        // Sepals between petals under the flower receptacle.
        var calyxColour = new[] { 0.14, 0.22, 0.09 };
        for (int p = 0; p < 5; p++)
        {
            double a = p * 2.0 * Math.PI / 5.0 + Math.PI / 5.0;
            var dir = new Vec3(Math.Cos(a), -0.15, Math.Sin(a)).Normalized();
            var side = Vec3.Up.Cross(dir).Normalized();
            var baseP = centre + dir * (radius * 0.12);
            var tip = centre + dir * (radius * 0.55) - Vec3.Up * (radius * 0.08);
            int start = m.VertexCount;
            m.AddVertex(baseP - side * (radius * 0.14), Vec3.Up, calyxColour, 1, 0, 0.5, 2.0, 0.0);
            m.AddVertex(baseP + side * (radius * 0.14), Vec3.Up, calyxColour, 1, 1, 0.5, 2.0, 0.0);
            m.AddVertex(tip, Vec3.Up, calyxColour, 1, 0.5, 1.0, 2.0, 0.0);
            Primitives.TriangleFacing(m, start, start + 1, start + 2, Vec3.Up);
            Primitives.TriangleFacing(m, start, start + 2, start + 1, -Vec3.Up);
        }

        for (int p = 0; p < 5; p++)
        {
            double a = p * 2.0 * Math.PI / 5.0 + rng.Range(-0.12, 0.12);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = Vec3.Up.Cross(dir).Normalized();
            var baseP = centre + dir * (radius * 0.16);
            var tip = centre + dir * radius + Vec3.Up * (radius * 0.12);
            int start = m.VertexCount;
            m.AddVertex(baseP - side * (radius * 0.30), Vec3.Up, petalColour, 1, 0, 0.5, 0.0, 1.0);
            m.AddVertex(baseP + side * (radius * 0.30), Vec3.Up, petalColour, 1, 1, 0.5, 0.0, 1.0);
            m.AddVertex(tip, Vec3.Up, petalColour, 1, 0.5, 1, 0.0, 1.0);
            Primitives.TriangleFacing(m, start, start + 1, start + 2, Vec3.Up);
        }
        int cs = m.VertexCount;
        for (int s = 0; s <= 8; s++)
        {
            double th = 2.0 * Math.PI * s / 8;
            var p = centre + new Vec3(Math.Cos(th) * radius * 0.14, radius * 0.10, Math.Sin(th) * radius * 0.14);
            m.AddVertex(p, Vec3.Up, stamenColour, 1, 0.5, 0.5, 0.0, 1.0);
        }
        for (int s = 0; s < 8; s++) Primitives.TriangleFacing(m, cs, cs + s, cs + s + 1, Vec3.Up);
    }

    /// <summary>
    /// The brake: a knotted crown of old wood, arching canes that root at their tips and throw up
    /// fresh shoots there, and the foliage, flowers and fruit that hang off them.
    /// </summary>
    private static void HookthicketBramble(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        int rows = juvenile ? 6 : 9;
        int cols = 5;
        if (m.Lod is { } lod)
        {
            rows = lod.Segments(rows, 4);
            cols = lod.Segments(cols, 3);
        }

        // Old wood is near black; new growth is a vivid green. That contrast is the brake's read.
        var oldWood = new[] { 0.085, 0.062, 0.048 };
        var youngWood = new[] { 0.16, 0.20, 0.09 };
        var leafTop = Primitives.Mix(c1, new[] { 0.055, 0.125, 0.075 }, 0.55);
        var leafBack = Primitives.Mix(c2, new[] { 0.19, 0.22, 0.20 }, 0.66);
        var prickleColour = new[] { 0.10, 0.08, 0.06 };
        var petalColour = new[] { 0.90, 0.86, 0.83 };
        var stamenColour = new[] { 0.92, 0.78, 0.30 };
        var drupeletColour = new[] { 0.075, 0.028, 0.045 };
        var heartColour = new[] { 0.70, 0.16, 0.26 };
        // An uneven heptagonal section: a bramble cane is angular, and never quite round.
        double[] section = { 1.00, 0.80, 1.14, 0.76, 1.06, 0.84, 1.10 };

        int canes = juvenile ? 7 : 12;
        const int caneSegments = 48;

        for (int k = 0; k < canes; k++)
        {
            double az = 2.0 * Math.PI * k / canes + rng.Range(-0.22, 0.22);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var side = new Vec3(-dir.Z, 0, dir.X);
            var root = dir * rng.Range(0.05, 0.26);
            double arch = height * rng.Range(0.58, 0.86) * (juvenile ? 0.72 : 1.0);
            double reach = radius * rng.Range(0.86, 1.06) * (juvenile ? 0.55 : 1.0);
            // A gentle sideways bow and a low gnarl: the cane is never a clean arc.
            double bow = rng.Range(-0.20, 0.20);
            double gnarlPhase = rng.Range(0.0, 6.283);

            Vec3 Point(double t)
            {
                double sweep = reach * (1.0 - Math.Cos(Math.PI * t)) * 0.5;
                double rise = arch * Math.Sin(t * (juvenile ? 1.75 : Math.PI));
                double lateral = bow * Math.Sin(Math.PI * t) + 0.040 * Math.Sin(2.0 * Math.PI * t + gnarlPhase);
                return root + dir * sweep + Vec3.Up * rise + side * lateral;
            }

            var path = new Vec3[caneSegments + 1];
            var radii = new double[caneSegments + 1];
            for (int j = 0; j <= caneSegments; j++)
            {
                double t = j / (double)caneSegments;
                path[j] = Point(t);
                // A bramble cane is thickest at the base and whips down to a point at the tip.
                radii[j] = 0.0165 * (1.0 - 0.84 * Math.Pow(t, 0.8)) + 0.0014;
            }
            BrambleTube(part, path, radii, 7, section, (i, v) =>
            {
                double t = (double)i / caneSegments;
                var col = Primitives.Mix(oldWood, Primitives.Mix(oldWood, c1, 0.45), 0.35 + 0.3 * Math.Sin(t * 7.0 + k));
                return (col, 1.0, t, v, 2, 0);
            });

            // Prickles in rows, thickest low on the cane.
            int prickleRows = juvenile ? 10 : 22;
            for (int j = 1; j <= prickleRows; j++)
            {
                double t = 0.04 + 0.88 * (j / (double)(prickleRows + 1));
                var p = Point(t);
                var tangent = (Point(Math.Min(1.0, t + 0.01)) - Point(Math.Max(0.0, t - 0.01))).Normalized();
                double r = 0.0165 * (1.0 - 0.84 * Math.Pow(t, 0.8)) + 0.0014;
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var outv = Axis.RotateAround(side, tangent, j * 2.4 + k * 0.7 + (sgn > 0 ? 0.0 : Math.PI)).Normalized();
                    BramblePrickle(part, p + outv * r * 0.9, outv, tangent,
                        0.020 + 0.030 * (1.0 - t) * rng.Range(0.8, 1.25), prickleColour);
                }
            }

            // Foliage: palmately compound leaves on long petioles.
            int leafNodes = juvenile ? 7 : 11;
            for (int j = 2; j <= leafNodes; j++)
            {
                double t = 0.10 + 0.80 * (j / (double)(leafNodes + 1));
                var p = Point(t);
                var tangent = (Point(Math.Min(1.0, t + 0.01)) - Point(Math.Max(0.0, t - 0.01))).Normalized();
                double leafAz = rng.Range(0.0, 2.0 * Math.PI);
                var leafDir = (new Vec3(Math.Cos(leafAz), rng.Range(-0.06, 0.08), Math.Sin(leafAz))).Normalized();
                double petLen = radius * rng.Range(0.11, 0.19);
                var petiole = p + leafDir * petLen + Vec3.Up * (petLen * 0.30);
                Primitives.Tube(part, new[] { p, petiole }, new[] { 0.0042, 0.0022 }, 6,
                    (i, v) => (Primitives.Mix(oldWood, leafTop, 0.35), 1, i, v, 0.0, 0.0));
                BrambleStipule(part, p, leafDir, radius * rng.Range(0.035, 0.055),
                    Primitives.Mix(oldWood, leafTop, 0.5), Rng.Mix(seed, (ulong)(k * 97 + j)));

                // Five leaflets, the terminal one longest, spread like a hand with slight semi-random organic perturbation.
                int leaflets = rng.NextDouble() < 0.42 ? 3 : 5;
                for (int l = 0; l < leaflets; l++)
                {
                    double a = leafAz + (l - (leaflets - 1) * 0.5) * (Math.PI * 2.0 / (leaflets + 1.4)) + rng.Range(-0.08, 0.08);
                    double pitch = rng.Range(-0.11, 0.13);
                    var fwd = (new Vec3(Math.Cos(a), pitch, Math.Sin(a))).Normalized();
                    double len = radius * (l == (leaflets - 1) / 2 ? rng.Range(0.135, 0.175) : rng.Range(0.095, 0.135));
                    double breadth = rng.Range(0.62, 0.80);
                    var baseNormal = (Vec3.Up * rng.Range(0.72, 0.98) + fwd * rng.Range(0.04, 0.22)).Normalized();
                    double rollAngle = rng.Range(-0.18, 0.18);
                    var bladeNormal = Axis.RotateAround(baseNormal, fwd, rollAngle).Normalized();
                    var bladeSide = bladeNormal.Cross(fwd).Normalized();
                    BrambleLeaflet(part, petiole, fwd, bladeNormal, len, breadth, rows, cols,
                        BrambleTooth, Rng.Mix(seed, (ulong)(k * 613 + j * 37 + l * 11)),
                        leafTop, leafBack);
                    // The petiolule that carries the leaflet off the stalk.
                    Primitives.Tube(part, new[] { petiole, petiole + fwd * (len * 0.10) },
                        new[] { 0.0022, 0.0014 }, 4,
                        (i, v) => (Primitives.Mix(oldWood, leafTop, 0.4), 1, i, v, 0.0, 0.0));
                }
            }

            if (!juvenile)
            {
                // Tip layering: the cane reaches the soil, roots where it lands, and throws up fresh
                // shoots of its own. This is how a real bramble spreads, and it is the brake's
                // primal character.
                var tip = path[^1];
                for (int r = 0; r < 3; r++)
                {
                    var end = tip + dir * (0.022 + r * 0.012) + side * ((r - 1) * 0.02) - Vec3.Up * 0.012;
                    Primitives.Tube(part, new[] { tip, Vec3.Lerp(tip, end, 0.5), end },
                        new[] { 0.0032, 0.0018, 0.0008 }, 6,
                        (i, v) => (Primitives.Mix(oldWood, new[] { 0.52, 0.44, 0.28 }, 0.45), 1, i * 0.5, v, 0.0, 0.0));
                }
                int shoots = 2 + rng.NextInt(2);
                for (int s = 0; s < shoots; s++)
                {
                    double a = rng.Range(0.0, 2.0 * Math.PI);
                    var upDir = new Vec3(Math.Cos(a) * 0.42, 1.0, Math.Sin(a) * 0.42).Normalized();
                    double shootLen = radius * rng.Range(0.16, 0.26);
                    var shootTop = tip + upDir * shootLen;
                    Primitives.Tube(part, new[] { tip, shootTop }, new[] { 0.0050, 0.0022 }, 6,
                        (i, v) => (Primitives.Mix(youngWood, leafTop, 0.4), 1, i, v, 0.0, 1.0));
                    // New growth: the vivid green that says the thicket is alive.
                    for (int l = 0; l < 3; l++)
                    {
                        double la = rng.Range(0.0, 2.0 * Math.PI);
                        double pitch = rng.Range(-0.12, 0.16);
                        var lf = (new Vec3(Math.Cos(la), pitch, Math.Sin(la))).Normalized();
                        double rollAngle = rng.Range(-0.20, 0.20);
                        var bladeNorm = Axis.RotateAround(Vec3.Up, lf, rollAngle).Normalized();
                        double len = radius * rng.Range(0.07, 0.11);
                        BrambleLeaflet(part, shootTop, lf, bladeNorm, len, 0.72, Math.Max(5, rows - 2), cols,
                            BrambleTooth, Rng.Mix(seed, (ulong)(k * 191 + s * 13 + l)),
                            Primitives.Mix(leafTop, new[] { 0.30, 0.52, 0.14 }, 0.45), leafBack);
                    }
                }

                // Flowers and berries on the second-year wood.
                int clusters = 2;
                for (int f = 0; f < clusters; f++)
                {
                    double t = rng.Range(0.42, 0.86);
                    var p = Point(t);
                    var tangent = (Point(Math.Min(1.0, t + 0.01)) - Point(Math.Max(0.0, t - 0.01))).Normalized();
                    var caneSide = tangent.Cross(Vec3.Up);
                    if (caneSide.LengthSq < 1e-4) caneSide = side;
                    else caneSide = caneSide.Normalized();

                    double sgn = (f % 2 == 0) ? 1.0 : -1.0;
                    var outVec = (caneSide * sgn * 0.85 + dir * 0.20 - Vec3.Up * 0.35).Normalized();
                    var caneOut = (outVec - tangent * outVec.Dot(tangent)).Normalized();

                    double caneR = 0.0165 * (1.0 - 0.84 * Math.Pow(t, 0.8)) + 0.0014;
                    var attach = p + caneOut * (caneR * 0.88);

                    // A short, firm pedicel holds the cluster right off the cane.
                    double pedicelLen = rng.Range(0.018, 0.026);
                    var centre = attach + caneOut * pedicelLen - Vec3.Up * (pedicelLen * 0.30);
                    var mid = (attach + centre) * 0.5 + caneOut * 0.002;

                    Primitives.Tube(part, new[] { attach, mid, centre },
                        new[] { 0.0036, 0.0028, 0.0020 }, 6,
                        (i, v) => (youngWood, 1.0, i, v, 2.0, 0.0));

                    if (rng.NextDouble() < 0.5)
                        BrambleFlower(part, centre, radius * rng.Range(0.020, 0.030), petalColour, stamenColour,
                            Rng.Mix(seed, (ulong)(k * 71 + f * 7)));
                    else
                        BrambleBerry(part, centre, radius * rng.Range(0.018, 0.026), drupeletColour, heartColour,
                            Rng.Mix(seed, (ulong)(k * 89 + f * 5)));
                }
            }
        }

        // The crown: a knotted burl of old wood that every cane leaves from.
        var crownColour = Primitives.Mix(oldWood, new[] { 0.16, 0.12, 0.08 }, 0.4);
        Primitives.Ellipsoid(part, new Vec3(0, 0.035, 0), new Vec3(0.075, 0.045, 0.075), 4, 12,
            (u, v) => (crownColour, 1, u, v, 2, 0));

        // The cane bases are buried in the litter, so the mesh is allowed to sink a little below the
        // soil. Clamping them to the surface instead would flatten the base rings and pull each
        // cane's axis out of line.
        for (int i = 0; i < part.VertexCount; i++)
            if (part.Positions[i * 3 + 1] < -0.02f) part.Positions[i * 3 + 1] = -0.02f;


        AppendWorld(m, part, radius, height);
    }
}
