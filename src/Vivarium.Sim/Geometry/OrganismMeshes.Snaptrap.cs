using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Latchjaw: a snap-trap carnivore built as a column of jaw-leaves under one large fleshy flower.
///
/// The species used to be a low rosette of traps on radiating petioles. It is rebuilt here as a
/// single upright column with the jaw-leaves set close around it, overlapping the way a Brussels
/// sprout stalk carries its buds, and one big fleshy trumpet flower opening at the top.
///
/// Each jaw-leaf keeps the species' own anatomy: a paired, cupped trap on a continuous hinge, with
/// a fringe of curved marginal cilia and three trigger hairs inside the throat. The flower is the
/// one deliberate departure: it is a large, thick, fleshy trumpet, and its throat carries a faint
/// crimson light after dark.
///
/// Reference renders: scripts/visuals/snaptrap/brussel_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    // Jaw-leaf size up the column: the lowest set are the largest, the top ones still tight.
    private static readonly double[] LatchjawBudT = { 0.00, 0.12, 0.28, 0.45, 0.62, 0.76, 0.88, 1.00 };
    private static readonly double[] LatchjawBudW = { 0.42, 0.78, 1.00, 1.00, 0.90, 0.74, 0.55, 0.34 };

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
    /// One jaw-leaf: a pair of cupped lobes on a continuous hinge, fringed with marginal cilia and
    /// armed with trigger hairs. The lobes are a single sheet each; the underside carries the
    /// species' crimson wash, which is what makes a snap trap read as a trap.
    /// </summary>
    private static void LatchjawTrap(MeshData m, Rng rng, Vec3 attach, Vec3 forward, Vec3 up,
        double length, double half, double open, double[] green, double[] crimson, ulong seed)
    {
        var side = up.Cross(forward);
        if (side.LengthSq < 1e-10) side = forward.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();

        const int rows = 10, cols = 4;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Vec3 At(double t, double x)
            {
                double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), .65);
                return attach + forward * (length * t) + side * (sign * half * envelope * x * Math.Cos(open))
                    + up * (half * envelope * (x * Math.Sin(open) + .12 * x * x));
            }

            int first = m.VertexCount;
            for (int row = 0; row <= rows; row++)
                for (int col = 0; col <= cols; col++)
                {
                    double t = .025 + .95 * row / rows, x = col / (double)cols;
                    var along = At(Math.Min(.99, t + .001), x) - At(Math.Max(.01, t - .001), x);
                    var cross = At(t, Math.Min(1, x + .001)) - At(t, Math.Max(0, x - .001));
                    var normal = cross.Cross(along).Normalized() * sign;
                    // The blade is green on the outside and lets the underside wash carry through.
                    var colour = Primitives.Mix(green, crimson, SmoothStep(0.55, 0.0, x) * 0.35);
                    m.AddVertex(At(t, x), normal, colour, 1, t, x, 0, 1);
                }
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int b = first + row * (cols + 1) + col;
                    Primitives.TriangleFacing(m, b, b + cols + 1, b + 1, m.NormalAt(b));
                    Primitives.TriangleFacing(m, b + 1, b + cols + 1, b + cols + 2, m.NormalAt(b));
                }

            // Marginal cilia curve upward and inward, framing the trap's two lobes.
            var ciliaColour = Primitives.Mix(green, new[] { 0.72, 0.76, 0.42 }, 0.55);
            for (int tooth = 0; tooth < 8; tooth++)
            {
                double t = .10 + tooth * .115;
                var p = At(t, 1);
                var end = p + side * (sign * .004) + up * .008;
                Primitives.Tube(m, new[] { p, p + side * (sign * .004) + up * .004, end },
                    new[] { .0009, .0005, .0002 }, 6, (i, v) => (ciliaColour, 1, i * .5, v, 0, 0));
            }

            // Three trigger hairs stand in the throat.
            for (int hair = 0; hair < 3; hair++)
            {
                var p = At(.30 + hair * .20, .48);
                Primitives.Tube(m, new[] { p, p + up * .008 - side * (sign * .0025) },
                    new[] { .0005, .00015 }, 6, (i, v) => (new[] { .33, .20, .13 }, 1, i, v, 0, 0));
            }
        }

        // The hinge: the closed seam the two lobes meet along.
        Primitives.Tube(m, new[] { attach, attach + forward * length }, new[] { .0016, .0009 }, 6,
            (i, v) => (green, 1, i, v, 0, 0));
    }

    /// <summary>
    /// The stalk. A plant stalk is not a smooth pole: it is a bundle of fibre bundles running up it,
    /// spiralling slightly, with lumps where old growth sat. This builds the column with six flutes
    /// that twist as they rise and an irregular profile along its length.
    /// </summary>
    private static void LatchjawStalk(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        double fluteDepth, double twist, double[] colour)
    {
        const int sides = 12;
        const int flutes = 6;
        var rng = Rng.Keyed((ulong)(uint)m.VertexCount, "latchjaw.flutes", 0);
        // Each fibre bundle is a little different, so the flutes are never a clean machine pattern.
        var bundle = new double[sides];
        for (int s = 0; s < sides; s++) bundle[s] = 0.55 + rng.NextDouble() * 0.9;

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
                // The fibre bundles run up the stalk and drift round it as they rise.
                double flute = 1.0 + fluteDepth * (Math.Cos(flutes * th + twist * t) * bundle[s % sides] - 0.5);
                double r = radii[i] * flute;
                var p = path[i] + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r);
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                m.AddVertex(p, radial, colour, 1, t, s / (double)sides, 0, 0);
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
    /// A strip of dead leaf base hanging from the stalk. It is a narrow sliver that droops and peels
    /// away from the stem, not a plate stuck on it.
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
            // The strip hangs from its attachment and curls off the stem as it falls.
            double peel = t * t * length * 0.38;
            var centre = attach + down * (t * length) + outward * peel;
            // Its width wanders, so the free edge is torn rather than cut.
            double w = width * (1.0 - 0.35 * t) * (0.5 + 0.5 * Math.Sin(t * 9.0 + rng.Range(0, 3)));
            m.AddVertex(centre - side * w, outward, colour, 1, 0, 0.5, 0, 0);
            m.AddVertex(centre + side * w, outward, colour, 1, 0, 1, 0, 0);
        }
        for (int i = 0; i < rows; i++)
        {
            int a = start + i * 2;
            Primitives.TriangleFacing(m, a, a + 2, a + 1, outward);
            Primitives.TriangleFacing(m, a + 1, a + 2, a + 3, outward);
        }
    }

    /// <summary>
    /// A bristle on the stalk. A carnivorous petiole is never smooth to the touch, and a few fine
    /// hairs catching the light are what stop the column reading as a painted pole.
    /// </summary>
    private static void LatchjawBristle(MeshData m, Rng rng, Vec3 basePoint, Vec3 outward,
        double length, double[] colour)
    {
        var bend = new Vec3(rng.Range(-0.4, 0.4), rng.Range(-0.6, 0.2), rng.Range(-0.4, 0.4)).Normalized();
        var mid = basePoint + outward * (length * 0.55) + bend * (length * 0.12);
        var tip = basePoint + outward * length + bend * (length * 0.35);
        Primitives.Tube(m, new[] { basePoint, mid, tip }, new[] { length * 0.055, length * 0.03, length * 0.008 }, 4,
            (i, v) => (colour, 1, i * 0.5, v, 0, 0));
    }

    /// <summary>
    /// The flower: a single large fleshy trumpet. The bell opens from a narrow throat and flares
    /// hard in its upper third, and its rim is ruffled and torn, as a fleshy petal edge is once it
    /// has been open for a while.
    /// </summary>
    private static void LatchjawFlower(MeshData m, Vec3 basePoint, Vec3 axis, double radius,
        double[] outer, double[] inner, double[] throat, ulong seed)
    {
        var rng = Rng.Keyed(seed, "latchjaw.flower", 0);
        axis = axis.Normalized();
        var side = axis.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(axis).Normalized();

        const int rows = 14, cols = 24;
        double length = radius * 1.45;
        double phase = rng.Range(0.0, 6.283);
        // The rim's shape, shared by the bell and the tatters that hang from it.
        double Flare(double t) => radius * (0.13 + 0.87 * Math.Pow(t, 2.3));
        double Droop(double t) => -radius * 0.10 * Math.Pow(t, 2.4);
        double Ruffle(double t, double th) =>
            1.0 + (0.055 * Math.Sin(th * 7.0 + phase) + 0.035 * Math.Sin(th * 13.0 + phase * 2.3)) * Math.Pow(t, 3);
        double Ragged(double t, double th) => radius * 0.055 * Math.Sin(th * 19.0 + phase * 3.1) * Math.Pow(t, 4);
        double RimRadius(double th) => Flare(1.0) * (1.0 + 0.075 * Math.Cos(6.0 * th)) * Ruffle(1.0, th);

        int first = m.VertexCount;
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            double flare = Flare(t);
            double droop = Droop(t);
            for (int j = 0; j <= cols; j++)
            {
                double th = 2.0 * Math.PI * j / cols;
                // Six soft lobes round the rim, and the whole edge wanders and is torn.
                double lobe = 1.0 + 0.075 * Math.Cos(6.0 * th) * Math.Pow(t, 4);
                double r = flare * lobe * Ruffle(t, th);
                var p = basePoint + axis * (length * t + droop + Ragged(t, th))
                    + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r);
                var colour = Primitives.Mix(outer, inner, SmoothStep(0.15, 0.85, t) * 0.55);
                m.AddVertex(p, (p - basePoint).Normalized(), colour, 1, t, j / (double)cols, 0, 1);
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                int a = first + i * (cols + 1) + j;
                Primitives.TriangleFacing(m, a, a + cols + 1, a + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a + 1, a + cols + 1, a + cols + 2, m.NormalAt(a + 1));
            }

        // Tatters: a few flaps of the rim left hanging where the edge has torn.
        int tatters = 5 + rng.NextInt(3);
        for (int k = 0; k < tatters; k++)
        {
            double th = rng.Range(0.0, 2.0 * Math.PI);
            double rimR = RimRadius(th);
            var rim = basePoint + axis * (length + Droop(1.0) + Ragged(1.0, th))
                + side * (Math.Cos(th) * rimR) + up * (Math.Sin(th) * rimR);
            var outward = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
            var tang = (-side * Math.Sin(th) + up * Math.Cos(th)).Normalized();
            double len = radius * rng.Range(0.12, 0.26);
            double wid = radius * rng.Range(0.035, 0.07);
            var tatterColour = Primitives.Mix(outer, inner, 0.35);
            int s0 = m.AddVertex(rim - tang * wid, outward, tatterColour, 1, 1, 0.4, 0, 1);
            int s1 = m.AddVertex(rim + tang * wid, outward, tatterColour, 1, 1, 0.6, 0, 1);
            int s2 = m.AddVertex(rim + outward * len - tang * wid * 0.4, outward, tatterColour, 1, 1, 0.45, 0, 1);
            int s3 = m.AddVertex(rim + outward * len + tang * wid * 0.4, outward, tatterColour, 1, 1, 0.55, 0, 1);
            Primitives.TriangleFacing(m, s0, s2, s1, outward);
            Primitives.TriangleFacing(m, s1, s2, s3, outward);
        }

        // The throat: a short tube of thick, dark, fleshy tissue, and the one part of the plant that
        // carries light after dark.
        const int rings = 4, sides = 16;
        int t0 = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double t = r / (double)rings;
            double rad = radius * (0.17 + 0.05 * t);
            for (int s = 0; s <= sides; s++)
            {
                double th = 2.0 * Math.PI * s / sides;
                var p = basePoint + axis * (length * 0.16 * t)
                    + side * (Math.Cos(th) * rad) + up * (Math.Sin(th) * rad);
                m.AddVertex(p, (p - basePoint).Normalized(), throat, 1, t, s / (double)sides, 1.4, 1.0);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = t0 + r * (sides + 1) + s;
                Primitives.TriangleFacing(m, a, a + sides + 1, a + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a + 1, a + sides + 1, a + sides + 2, m.NormalAt(a + 1));
            }
    }

    /// <summary>
    /// The latchjaw: a stout column carrying jaw-leaves all the way up, and one large fleshy flower
    /// opening at the top of it.
    /// </summary>
    private static void LatchJawColumn(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        var green = Primitives.Mix(c1, new[] { 0.085, 0.215, 0.075 }, 0.55);
        var crimson = Primitives.Mix(c2, new[] { 0.42, 0.075, 0.085 }, 0.55);
        // The stalk is darker and browner than the blades, so the traps read as separate from it.
        // It carries no leaf pigment of its own, so its value is stated directly: the shader reads
        // the vertex colour as sRGB, and a colour chosen to look right in the leaf branch would
        // come out nearly black here.
        var stemColour = Primitives.Mix(new[] { 0.44, 0.385, 0.20 }, green, 0.35);
        var petalOuter = Primitives.Mix(green, new[] { 0.36, 0.52, 0.16 }, 0.55);
        var petalInner = Primitives.Mix(crimson, new[] { 0.62, 0.11, 0.14 }, 0.6);
        var throatColour = new[] { 0.34, 0.06, 0.09 };

        double columnHeight = height * (juvenile ? 0.68 : 0.76);
        double columnRadius = radius * 0.045;

        // 1. The column: a stout stalk that tapers hard as it rises and wanders as it goes. It is
        //    built from fibre bundles rather than as a smooth pole, and it swells at the nodes where
        //    old leaves sat.
        var lean = new Vec3(rng.Range(-0.03, 0.03), 0, rng.Range(-0.03, 0.03));
        var wander = new Vec3(rng.Range(-0.4, 0.4), 0, rng.Range(-0.4, 0.4)).Normalized();
        double wanderPhase = rng.Range(0.0, 6.283);
        const int columnSteps = 16;
        var path = new Vec3[columnSteps + 1];
        var radii = new double[columnSteps + 1];
        for (int i = 0; i <= columnSteps; i++)
        {
            double t = i / (double)columnSteps;
            // The stalk leans, and swings in a slow arc as it climbs.
            path[i] = lean * (t * t * columnHeight) + Vec3.Up * (columnHeight * t)
                + wander * (columnHeight * 0.11 * Math.Sin(t * 2.6 + wanderPhase));
            // Swells at the nodes where old growth sat, and a general taper.
            double node = Math.Pow(Math.Abs(Math.Sin(t * Math.PI * 5.0)), 6.0);
            double lump = 1.0 + 0.28 * Math.Exp(-t * 5.0) + 0.38 * node + 0.07 * Math.Sin(t * 29.0);
            radii[i] = columnRadius * (1.0 - 0.46 * t) * lump;
        }
        LatchjawStalk(part, path, radii, 0.46, 4.4, stemColour);

        // 2. Old leaf bases sheathing the foot of the column, and the fibrous slivers of dead leaf
        //    base still clinging to it: a stripped stalk is never bare.
        int sheaths = juvenile ? 5 : 9;
        for (int k = 0; k < sheaths; k++)
        {
            double a = rng.Range(0.0, 2.0 * Math.PI);
            double h = columnHeight * rng.Range(0.05, 0.22);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var baseP = outward * (columnRadius * 0.9);
            var tip = baseP + outward * (columnRadius * 1.1) + Vec3.Up * (h * 0.8);
            var mid = Vec3.Lerp(baseP, tip, 0.5) + outward * (columnRadius * 0.5);
            int s0 = part.AddVertex(baseP, outward, stemColour, 1, 0, 0.5, 0, 0);
            int s1 = part.AddVertex(mid, outward, stemColour, 1, 0.5, 0.5, 0, 0);
            int s2 = part.AddVertex(tip, outward, stemColour, 1, 1, 0.5, 0, 0);
            Primitives.TriangleFacing(part, s0, s1, s2, outward);
        }

        int shreds = juvenile ? 10 : 26;
        for (int k = 0; k < shreds; k++)
        {
            double a = rng.Range(0.0, 2.0 * Math.PI);
            double t = rng.Range(0.02, 0.74);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var baseP = outward * (columnRadius * rng.Range(0.8, 1.0))
                + Vec3.Up * (columnHeight * t) + lean * (t * t * columnHeight)
                + wander * (columnHeight * 0.11 * Math.Sin(t * 2.6 + wanderPhase));
            LatchjawShred(part, rng, baseP, outward,
                columnHeight * rng.Range(0.14, 0.30), columnRadius * rng.Range(0.18, 0.42),
                Primitives.Mix(stemColour, new[] { 0.44, 0.38, 0.20 }, rng.Range(0.25, 0.7)));
        }

        // Fine bristles over the stalk: the stalk of a snap trap is not smooth to the touch.
        int bristles = juvenile ? 18 : 46;
        for (int k = 0; k < bristles; k++)
        {
            double a = rng.Range(0.0, 2.0 * Math.PI);
            double t = rng.Range(0.0, 0.95);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            int seg = Math.Min(columnSteps, (int)(t * columnSteps));
            var baseP = path[seg] + outward * (radii[seg] * 0.95);
            LatchjawBristle(part, rng, baseP, outward, columnRadius * rng.Range(0.35, 0.8),
                Primitives.Mix(stemColour, new[] { 0.50, 0.52, 0.26 }, 0.5));
        }

        // 3. The jaw-leaves, set close around the column and spiralling up it.
        int leaves = juvenile ? 7 : 12;
        for (int k = 0; k < leaves; k++)
        {
            double t = 0.06 + 0.80 * (k / (double)(leaves - 1));
            double az = k * 2.399963229728653;
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            // The lower leaves clasp the column; the upper ones lean out of the flower's way.
            double leanAngle = rng.Range(0.80, 1.15) - 0.35 * t;
            var forward = (outward * Math.Cos(leanAngle) + Vec3.Up * Math.Sin(leanAngle)).Normalized();
            var up = Vec3.Up.Cross(forward);
            if (up.LengthSq < 1e-10) up = new Vec3(1, 0, 0);
            up = up.Normalized();

            var attach = path[0] + outward * (columnRadius * 0.9)
                + Vec3.Up * (columnHeight * t) + lean * (t * t * columnHeight);
            double size = LatchjawBudWidth(t) * height * 0.185;
            double half = size * rng.Range(0.38, 0.46);
            double open = rng.Range(0.10, 0.34);
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 977 + 13));
            LatchjawTrap(part, rng, attach, forward, up, size, half, open, green, crimson, lSeed);
        }

        // 4. The flower, opening at the top of the column.
        var flowerBase = path[^1] + Vec3.Up * (columnHeight * 0.06);
        var axis = (Vec3.Up + new Vec3(rng.Range(-0.12, 0.12), 0, rng.Range(-0.12, 0.12))).Normalized();
        LatchjawFlower(part, flowerBase, axis, height * 0.30, petalOuter, petalInner, throatColour,
            Rng.Mix(seed, 4211));

        // 5. A crown of small bracts where the flower meets the column.
        int bracts = juvenile ? 4 : 6;
        for (int k = 0; k < bracts; k++)
        {
            double az = k * 2.399963229728653;
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var forward = (outward * 0.55 + Vec3.Up * 0.84).Normalized();
            var up = Vec3.Up.Cross(forward);
            if (up.LengthSq < 1e-10) up = new Vec3(1, 0, 0);
            up = up.Normalized();
            var attach = flowerBase - Vec3.Up * (height * 0.075);
            double size = height * 0.062;
            LatchjawTrap(part, rng, attach, forward, up, size, size * 0.16, 0.7, green, crimson,
                Rng.Mix(seed, (ulong)(k * 613 + 7)));
        }

        AppendWorld(m, part, radius, height);
    }
}
