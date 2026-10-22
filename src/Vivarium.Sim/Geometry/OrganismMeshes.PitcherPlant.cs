using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Raincup Pitcher (<c>pitcher_plant</c>) — a rosette of Sarracenia pitchers that gathers rain.
///
/// Reference. The plant is built against the purple pitcher plant, <i>Sarracenia purpurea</i>, and
/// the other North American Sarracenia: a short rhizome crown throwing a rosette of upright traps
/// that are narrow at the base and flare into a wide trumpet only in their top third; a thin, smooth,
/// slightly flared mouth rim; and above that mouth the broad, erect, kidney-shaped hood, veined
/// with a red areolate net that encloses pale windows, waved along its free margin and fringed
/// with long fine hairs. Inside the throat, the descending wall is darker and carries the stiff
/// nectar-guide hairs that stop prey escaping. A Sarracenia clump also keeps the collapsed, torn,
/// brown remains of last year's pitchers around its crown, and throws a single tall, leafless
/// scape carrying one nodding flower.
///
/// Fantasy. This is the Raincup, and it is not after prey: the cup keeps a skin of cold rain that
/// never quite runs off, held mirror-still in the throat, and the plant has grown its nectar-guide
/// hairs and its hood fringe as fine crystalline dew needles instead of plain bristles, so the
/// whole trap is rimed with a cold light that gathers on the wet interior and the pale areolate
/// windows.
///
/// Authoring. The plant is built in world metres and converted to the engine's mesh frame by
/// <see cref="AppendPitcherWorld"/>, so what is built here is what the player sees.
///
/// Organs. The trap walls and the hood carry their pigment in vertex colour rather than in the
/// shared procedural tissue, because the shared ochre mottle fights the Sarracenia areolate net
/// that is the whole character of the species. The wet interior is flagged through <c>UV2</c> so
/// the shader keeps it glossy, and the dry outer skin matte.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------
    // Anatomy constants, world metres.
    // ---------------------------------------------------------------------

    /// <summary>Wall thickness of a trap: a Sarracenia pitcher is a thin, tough tube.</summary>
    private const double PitcherWall = 0.0011;
    /// <summary>Radius of the rolled lip at the mouth.</summary>
    private const double PitcherLip = 0.0016;
    /// <summary>Depth of the throat at which the rain surface sits.</summary>
    private const double PitcherLiquidLevel = 0.30;

    /// <summary>Smallest signed angular distance between two directions.</summary>
    private static double PitcherAngleDelta(double a, double b)
    {
        double d = a - b;
        while (d > Math.PI) d -= Math.Tau;
        while (d < -Math.PI) d += Math.Tau;
        return d;
    }

    /// <summary>
    /// Per-trap shape and pigment. Every trap in the rosette gets its own height, mouth width,
    /// hood, lean, vein count and colouring, so no two cups in the clump are the same object.
    /// </summary>
    private sealed class PitcherShape
    {
        public double Height;        // metres, base to mouth
        public double MouthRadius;   // metres
        public double Lean;          // radians away from vertical
        public double HoodHeight;    // metres the hood stands above the rim at its back
        public int Veins = 9;        // longitudinal veins up the tube
        public double VeinPhase;
        public double Redness;       // 0 green, 1 wine red
        public double[] Tissue = { 0.30, 0.52, 0.22 };
        public double[] Vein = { 0.68, 0.18, 0.20 };
        public double[] Window = { 0.72, 0.74, 0.44 };
        public double[] NetVein = { 0.68, 0.18, 0.20 };
        public double[] NetGround = { 0.72, 0.74, 0.44 };
        public double[] Under = { 0.26, 0.09, 0.13 };
        public double[] Old = { 0.44, 0.30, 0.16 };
        public double[] Dew = { 0.80, 0.88, 0.94 };
    }

    // ---------------------------------------------------------------------
    // One trap
    // ---------------------------------------------------------------------

    /// <summary>
    /// The trap's axis, radius and surface. The tube is narrow at the foot and opens into a
    /// trumpet; the back carries a thin vertical wing; the wall is ribbed by the longitudinal
    /// veins that run up it.
    /// </summary>
    private static void PitcherTrap(MeshData m, Rng rng, ulong seed, Vec3 basePoint, Vec3 axis,
        Vec3 fwd, Vec3 side, PitcherShape s, int rings, int around, int lipQ)
    {
        Vec3 Centre(double t) => basePoint + axis * (s.Height * t) + fwd * (s.Height * 0.09 * t * t);

        double Radius(double t) =>
            s.MouthRadius * (0.19 + 0.32 * Math.Sin(Math.PI * t * 0.72) + 0.56 * Math.Pow(t, 5));

        /// <summary>Longitudinal vein relief: the ribs that run the length of the tube.</summary>
        double VeinRelief(double phi, double t)
        {
            double v = 0.5 + 0.5 * Math.Cos(s.Veins * phi + s.VeinPhase);
            return Math.Pow(v, 3.0) * t;
        }

        Vec3 Surface(double t, double phi, double offset)
        {
            double r = Radius(t) + offset + s.MouthRadius * 0.016 * VeinRelief(phi, t);
            var dir = fwd * Math.Cos(phi) + side * Math.Sin(phi);
            return Centre(t) + dir * r;
        }

        /// <summary>Vein colour on the outer skin: red where the ribs run.</summary>
        double[] OuterColour(double t, double phi)
        {
            double relief = VeinRelief(phi, t);
            double patch = 0.5 + 0.5 * Math.Sin(t * 11.0 + Math.Sin(phi * 13.0 + s.VeinPhase) * 1.6);
            var tissue = Primitives.Mix(s.Tissue, s.Vein, MathD.Clamp01(s.Redness * 0.75 + 0.25 * patch));
            tissue = Primitives.Mix(tissue, s.Vein, Math.Min(1.0, relief * 0.85));
            return tissue;
        }

        // --- outer wall -----------------------------------------------------
        int outerStart = m.VertexCount;
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            for (int j = 0; j <= around; j++)
            {
                double phi = j / (double)around * Math.Tau;
                var p = Surface(t, phi, 0);
                var outward = (fwd * Math.Cos(phi) + side * Math.Sin(phi));
                var n = (outward - axis * (0.10 * Math.Cos(t * Math.PI))).Normalized();
                m.AddVertex(p, n, OuterColour(t, phi), 1, t, j / (double)around, 0, 0);
            }
        }
        int stride = around + 1;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = outerStart + i * stride + j, b = a + stride;
                Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a));
            }

        // --- interior wall --------------------------------------------------
        // Descends from the lip into the cup, narrowing with the tube and closing on a rounded
        // bottom that sits under the rain.
        int innerRings = Math.Max(4, rings - 2);
        int innerStart = m.VertexCount;
        for (int i = 0; i <= innerRings; i++)
        {
            double t = 1.0 - (i / (double)innerRings) * (1.0 - 0.13);
            for (int j = 0; j <= around; j++)
            {
                double phi = j / (double)around * Math.Tau;
                var p = Surface(t, phi, -PitcherWall);
                var outward = (fwd * Math.Cos(phi) + side * Math.Sin(phi));
                var n = (-outward - axis * (0.08 * Math.Cos(t * Math.PI))).Normalized();
                // The inside is darker, and darker still towards the throat.
                var col = Primitives.Mix(s.Under, s.Vein, MathD.Clamp01(0.30 * VeinRelief(phi, t)));
                col = Primitives.Mix(col, s.Under, 0.45 * MathD.SmoothStep(0.35, 0.95, t));
                m.AddVertex(p, n, col, 1, t, j / (double)around, 0, -6);
            }
        }
        for (int i = 0; i < innerRings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = innerStart + i * stride + j, b = a + stride;
                Primitives.TriangleFacing(m, a, b + 1, b, m.NormalAt(a));
                Primitives.TriangleFacing(m, a, a + 1, b + 1, m.NormalAt(a));
            }
        // The floor of the cup.
        {
            var floorCentre = Surface(0.13, 0, -PitcherWall);
            int c = m.AddVertex(floorCentre, -axis, s.Under, 1, 0.13, 0.5, 0, -6);
            int last = innerStart + innerRings * stride;
            for (int j = 0; j < around; j++)
                Primitives.TriangleFacing(m, c, last + j + 1, last + j, -axis);
        }

        // --- the rolled lip -------------------------------------------------
        // A smooth half-round rim that rolls from the outer skin over the top and down into the
        // throat. It is never a filled disc: the cup is open.
        int lipStart = m.VertexCount;
        for (int j = 0; j <= around; j++)
        {
            double phi = j / (double)around * Math.Tau;
            var dir = fwd * Math.Cos(phi) + side * Math.Sin(phi);
            var rim = Surface(1.0, phi, -PitcherWall * 0.5);
            for (int q = 0; q <= lipQ; q++)
            {
                double theta = q / (double)lipQ * Math.PI;
                var p = rim + dir * (PitcherLip * Math.Cos(theta)) + axis * (PitcherLip * Math.Sin(theta));
                var n = (dir * Math.Cos(theta) + axis * Math.Sin(theta)).Normalized();
                var col = Primitives.Mix(s.Vein, s.Window, 0.28);
                m.AddVertex(p, n, col, 1, 1.0, j / (double)around, 0, 0);
            }
        }
        int lipStride = lipQ + 1;
        for (int j = 0; j < around; j++)
            for (int q = 0; q < lipQ; q++)
            {
                int a = lipStart + j * lipStride + q, b = a + lipStride;
                Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a));
            }

        // --- the keel --------------------------------------------------------
        // A thin vertical wing down the back of the tube, the way a Sarracenia trap is keeled.
        {
            int keelStart = m.VertexCount;
            const int kRows = 7, kCols = 3;
            for (int face = 0; face < 2; face++)
            {
                int start = m.VertexCount;
                double sign = face == 0 ? 1 : -1;
                for (int i = 0; i <= kRows; i++)
                {
                    double t = 0.05 + (i / (double)kRows) * 0.90;
                    double width = s.MouthRadius * 0.16 * Math.Sin(Math.PI * Math.Min(1.0, t * 1.02));
                    for (int j = 0; j <= kCols; j++)
                    {
                        double x = j / (double)kCols;
                        var p = Surface(t, 0, 0) + fwd * (width * x) + axis * (width * 0.22 * x * x);
                        var n = side * sign;
                        var col = Primitives.Mix(s.Tissue, s.Vein, 0.45 * MathD.SmoothStep(0.2, 0.9, t));
                        m.AddVertex(p, n, col, 1, t, x, 0, 0);
                    }
                }
                int kStride = kCols + 1;
                for (int i = 0; i < kRows; i++)
                    for (int j = 0; j < kCols; j++)
                    {
                        int a = start + i * kStride + j, b = a + kStride;
                        if (face == 0) { Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a)); Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a)); }
                        else { Primitives.TriangleFacing(m, a, b + 1, b, m.NormalAt(a)); Primitives.TriangleFacing(m, a, a + 1, b + 1, m.NormalAt(a)); }
                    }
            }
            m.EssentialSpans.Add((keelStart, m.VertexCount - keelStart));
        }

        // --- the hood --------------------------------------------------------
        PitcherHood(m, rng, seed, s, Centre(1.0), Radius(1.0), fwd, side, axis, around);

        // --- the rain --------------------------------------------------------
        // A still, mirror-bright skin of cold water lying deep in the cup. The trap gathers rain,
        // not prey, and the surface holds the sky.
        {
            double t = PitcherLiquidLevel;
            double r = Radius(t) * 0.90;
            var c = Centre(t);
            Primitives.Ellipsoid(m, c, new Vec3(r, r * 0.16, r * 0.90), 3, 12,
                (u, v) => (new[] { 0.055, 0.085, 0.075 }, 1, u, v, 0, -4));
        }
    }

    /// <summary>
    /// The hood: a broad, erect, kidney-shaped lid standing over the mouth and leaning forward
    /// over it, waved along its free margin, notched at the front, and veined with a red
    /// areolate net that encloses pale windows.
    /// </summary>
    private static void PitcherHood(MeshData m, Rng rng, ulong seed, PitcherShape s, Vec3 mouthCentre,
        double mouthRadius, Vec3 fwd, Vec3 side, Vec3 axis, int around)
    {
        const int cols = 5;
        int phiCount = Math.Max(16, around);
        int start = m.VertexCount;

        // How far the hood stands above the rim at a given bearing. It stands tallest at the back
        // of the trap and comes down at the front, where it hangs over the throat.
        double Height(double phi) => s.HoodHeight * (0.70 + 0.30 * Math.Cos(phi));

        // The plan of the free margin: the hood narrows as it rises, and is pulled in further at
        // the front into the notch, and gently waved.
        double MarginScale(double phi)
        {
            double notch = 1.0 - 0.34 * Math.Exp(-Math.Pow(PitcherAngleDelta(phi, Math.PI) / 0.55, 2));
            double wave = 1.0 + 0.030 * Math.Sin(4.0 * phi + s.VeinPhase * 1.7);
            return (0.62 - 0.10 * Math.Cos(phi)) * notch * wave;
        }

        Vec3 RimPoint(double phi)
        {
            var dir = fwd * Math.Cos(phi) + side * Math.Sin(phi);
            return mouthCentre + dir * mouthRadius;
        }

        Vec3 MarginPoint(double phi)
        {
            var dir = fwd * Math.Cos(phi) + side * Math.Sin(phi);
            double h = Height(phi);
            // The hood leans right forward over the mouth, so its free edge hangs out past the
            // throat and the throat stays open underneath it.
            return mouthCentre + dir * (mouthRadius * MarginScale(phi))
                 + axis * h - fwd * (h * 0.48);
        }

        Vec3 HoodPoint(double phi, double w)
        {
            var rim = RimPoint(phi);
            var margin = MarginPoint(phi);
            double arch = Math.Sin(Math.PI * w);
            // The dome bulges up and forward, the way a hood catches the light.
            var archDir = (axis * 0.88 - fwd * 0.48).Normalized();
            return Vec3.Lerp(rim, margin, w) + archDir * (s.HoodHeight * 0.24 * arch);
        }

        // The areolate net: two crossed gratings give the lattice of veins that encloses the pale
        // windows of a Sarracenia hood. The cells are kept large enough to read at the distance the
        // plant is actually seen from.
        double Net(double phi, double w)
        {
            double u = phi / Math.Tau, v = w;
            double g1 = Math.Abs(Math.Sin(Math.PI * (u * 3.5 + v * 1.1 + s.VeinPhase)));
            double g2 = Math.Abs(Math.Sin(Math.PI * (u * 1.3 - v * 2.4 + s.VeinPhase * 1.7)));
            double g3 = Math.Abs(Math.Sin(Math.PI * (u * 7.0 + v * 4.1)));
            double major = 1.0 - MathD.SmoothStep(0.0, 0.48, Math.Min(g1, g2));
            double minor = (1.0 - MathD.SmoothStep(0.0, 0.30, g3)) * 0.35;
            return Math.Max(major, minor);
        }

        for (int face = 0; face < 2; face++)
        {
            int base0 = m.VertexCount;
            double sign = face == 0 ? 1 : -1;
            for (int i = 0; i <= phiCount; i++)
            {
                double phi = i / (double)phiCount * Math.Tau;
                for (int j = 0; j <= cols; j++)
                {
                    double w = j / (double)cols;
                    var p = HoodPoint(phi, w);
                    // The hood is a thin sheet; the inner face sits just under the outer one.
                    if (face == 1)
                    {
                        var outward = (p - mouthCentre);
                        outward = (outward - axis * outward.Dot(axis)).Normalized();
                        p -= outward * (s.MouthRadius * 0.030) - axis * (s.MouthRadius * 0.012);
                    }
                    var n = (p - mouthCentre).Normalized();
                    if (face == 1) n = -n;

                    double net = Net(phi, w);
                    // The veins of the net carry one pigment and the windows they enclose carry
                    // the other; which is which flips with the trap's own colour.
                    var col = Primitives.Mix(s.NetGround, s.NetVein, Math.Min(1.0, net * 0.52));
                    if (face == 1) col = Primitives.Mix(col, s.Under, 0.15);

                    m.AddVertex(p, n, col, 1, phi / Math.Tau, w, 0, face == 0 ? 0 : -6);
                }
            }
            int hStride = cols + 1;
            for (int i = 0; i < phiCount; i++)
                for (int j = 0; j < cols; j++)
                {
                    int a = base0 + i * hStride + j, b = a + hStride;
                    if (face == 0) { Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a)); Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a)); }
                    else { Primitives.TriangleFacing(m, a, b + 1, b, m.NormalAt(a)); Primitives.TriangleFacing(m, a, a + 1, b + 1, m.NormalAt(a)); }
                }
        }
        m.EssentialSpans.Add((start, m.VertexCount - start));

            // The crystalline fringe: long fine hairs on the inner face of the hood and along its
            // free margin, the plant has grown them as dew needles rather than as plain bristles.
            int hairs = m.Lod is { } lod ? Math.Max(12, (int)Math.Round(96 * lod.Blade)) : 96;
            for (int h = 0; h < hairs; h++)
            {
                double phi = rng.Range(0, Math.Tau);
                double w = rng.NextDouble() < 0.45 ? rng.Range(0.86, 1.0) : rng.Range(0.10, 0.88);
                var root = HoodPoint(phi, w);
                // The needles lie along the inner face, radiating away from the hood's centre.
                var radial = (root - mouthCentre);
                radial = (radial - axis * radial.Dot(axis)).Normalized();
                if (radial.LengthSq < 1e-9) radial = axis;
                double len = s.HoodHeight * rng.Range(0.12, 0.30);
                var mid = root + radial * (len * 0.55) + axis * (len * 0.12);
                var tip = root + radial * len + axis * (len * 0.04);
                double r0 = s.MouthRadius * 0.006;
                Primitives.Tube(m, new[] { root, mid, tip }, new[] { r0, r0 * 0.5, r0 * 0.10 }, 4,
                    (i, v) => (s.Dew, 1, i, v, 0, 0));
            }
    }

    /// <summary>
    /// The brown, collapsed, torn remains of last year's pitchers that ring the crown of a
    /// Sarracenia clump. They are what makes the rosette read as a clump rather than a bouquet.
    /// </summary>
    private static void PitcherOldTrap(MeshData m, Rng rng, PitcherShape s, Vec3 basePoint, Vec3 axis,
        Vec3 fwd, Vec3 side, int rings, int around)
    {
        double height = s.Height * rng.Range(0.55, 0.85);
        double radius = s.MouthRadius * rng.Range(0.55, 0.85);
        int start = m.VertexCount;
        var ragged = new double[around + 1];
        for (int j = 0; j <= around; j++) ragged[j] = rng.Range(0.55, 1.0);

        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            for (int j = 0; j <= around; j++)
            {
                double phi = j / (double)around * Math.Tau;
                double lean = s.Height * 0.12 * t * t;
                // The old trap has collapsed: it is flattened and folded over.
                double squash = 0.28 + 0.26 * Math.Sin(Math.PI * t);
                double r = radius * (0.22 + 0.78 * Math.Sin(Math.PI * 0.55 * t + 0.35)) * ragged[j];
                var dir = fwd * Math.Cos(phi) + side * Math.Sin(phi);
                var p = basePoint + axis * (height * t) + fwd * lean
                       + dir * (r * squash) + axis * (r * (1.0 - squash) * 0.7);
                var n = (dir * 0.4 + axis * 0.9).Normalized();
                var col = Primitives.Mix(s.Old, s.Tissue, 0.22 * rng.NextDouble());
                m.AddVertex(p, n, col, 1, t, j / (double)around, 0, 0);
            }
        }
        int stride = around + 1;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = start + i * stride + j, b = a + stride;
                Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a));
            }
    }

    /// <summary>
    /// The scape and the flower: a single tall, leafless stalk carrying one nodding flower with
    /// five spreading sepals over five hanging petals.
    /// </summary>
    private static void PitcherFlower(MeshData m, Rng rng, ulong seed, Vec3 basePoint, double height,
        double[] sepalCol, double[] petalCol)
    {
        const int steps = 9;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            // The scape leans and nods over at the top, as a Sarracenia flower does.
            path[i] = basePoint + new Vec3(0.030 * Math.Sin(t * 1.5), height * t, 0.018 * t * t);
            radii[i] = 0.0034 * (1.0 - 0.42 * t);
        }
        Primitives.Tube(m, path, radii, 6, (i, v) => (new[] { 0.36, 0.44, 0.18 }, 1, i / (double)steps, v, 0, 0));

        var flower = path[steps];
        var nod = new Vec3(0, -0.010, 0);
        // The ovary and the domed style.
        Primitives.Ellipsoid(m, flower + nod, new Vec3(0.010, 0.009, 0.010), 4, 9,
            (u, v) => (new[] { 0.42, 0.38, 0.20 }, 1, u, v, 0, 0));
        for (int k = 0; k < 5; k++)
        {
            double a = k * Math.Tau / 5 + 0.3;
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = new Vec3(-dir.Z, 0, dir.X);
            // Five broad sepals spreading into a shallow umbrella over the flower.
            CurvedBlade(m, flower + nod, flower + nod + dir * 0.030 + new Vec3(0, 0.006, 0),
                flower + nod + dir * 0.048 - new Vec3(0, 0.004, 0), side, 0.020,
                sepalCol, Primitives.Mix(sepalCol, petalCol, 0.35), 8, 0, 1, 0, 0);
            // Five narrower petals hanging below it.
            CurvedBlade(m, flower + nod + dir * 0.004, flower + nod + dir * 0.026 - new Vec3(0, 0.020, 0),
                flower + nod + dir * 0.016 - new Vec3(0, 0.046, 0), side, 0.017,
                Primitives.Mix(petalCol, sepalCol, 0.20), petalCol, 8, 0.1, 0.7, 0, 0);
        }
    }

    // ---------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------

    /// <summary>
    /// Raincup Pitcher. A rosette of Sarracenia traps on a short crown, ringed by the collapsed
    /// remains of last year's pitchers, with a single flowering scape standing clear of the clump.
    /// </summary>
    private static void PitcherPlant(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        double matSpan = radius > 0.01 ? radius : 0.34;
        double plantHeight = height > 0.01 ? height : 0.58;

        int rings = m.Lod is { } lod ? Math.Max(6, (int)Math.Round(11 * lod.Length)) : 11;
        int around = m.Lod is { } lod2 ? Math.Max(10, (int)Math.Round(26 * lod2.Radial)) : 26;
        int lipQ = m.Lod is { } lod3 ? Math.Max(3, (int)Math.Round(6 * lod3.Radial)) : 6;

        // --- the crown -------------------------------------------------------
        // A short rhizome mound that the traps and the old remains both spring from.
        Primitives.Ellipsoid(part, new Vec3(0, 0.012, 0), new Vec3(0.040, 0.016, 0.040), 4, 9,
            (u, v) => (Primitives.Mix(c1, new[] { 0.34, 0.26, 0.14 }, 0.55), 1, u, v, 0, 0));

        // --- the rosette -----------------------------------------------------
        int traps = 7 + rng.NextInt(3);
        var shape = new PitcherShape();
        for (int k = 0; k < traps; k++)
        {
            double az = Math.Tau * k / traps + rng.Range(-0.20, 0.20);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));

            shape.Height = rng.Range(0.155, 0.265);
            shape.MouthRadius = rng.Range(0.021, 0.037);
            shape.Lean = rng.Range(0.10, 0.30);
            shape.HoodHeight = shape.MouthRadius * rng.Range(2.0, 3.0);
            shape.Veins = 7 + rng.NextInt(4);
            shape.VeinPhase = rng.Range(0, Math.Tau);
            // A Sarracenia clump runs to two forms: a green one with red veins, and a wine-red one
            // with pale windows. Each trap in this clump is one or the other.
            shape.Redness = rng.NextDouble() < 0.45 ? rng.Range(0.02, 0.26) : rng.Range(0.62, 0.98);
            shape.Tissue = Primitives.Scale(Primitives.Mix(c1, c2, shape.Redness), 0.65);
            shape.Vein = Primitives.Scale(Primitives.Mix(c2, new[] { 0.32, 0.05, 0.09 }, 0.32), 0.65);
            shape.Window = Primitives.Scale(Primitives.Mix(c1, new[] { 0.70, 0.68, 0.36 }, 0.45), 0.80);
            // In a green trap the net is red on a pale ground; in a red trap it is the other way.
            shape.NetVein = Primitives.Mix(shape.Vein, shape.Window, shape.Redness);
            shape.NetGround = Primitives.Mix(shape.Window, shape.Vein, shape.Redness);
            shape.Under = Primitives.Mix(c2, new[] { 0.14, 0.04, 0.08 }, 0.55);
            shape.Old = Primitives.Scale(Primitives.Mix(new[] { 0.30, 0.20, 0.11 }, c2, 0.14), 0.80);
            shape.Dew = new[] { 0.70, 0.79, 0.86 };

            var axis = (Vec3.Up * Math.Cos(shape.Lean) + outward * Math.Sin(shape.Lean)).Normalized();
            var fwd = (outward - axis * outward.Dot(axis)).Normalized();
            var side = axis.Cross(fwd).Normalized();
            var basePoint = outward * rng.Range(0.012, 0.055) + Vec3.Up * 0.014;

            PitcherTrap(part, rng, seed, basePoint, axis, fwd, side, shape, rings, around, lipQ);
        }

        // --- last year's remains --------------------------------------------
        int old = 2 + rng.NextInt(3);
        for (int k = 0; k < old; k++)
        {
            double az = rng.Range(0, Math.Tau);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double lean = rng.Range(0.55, 1.05);
            var axis = (Vec3.Up * Math.Cos(lean) + outward * Math.Sin(lean)).Normalized();
            var fwd = (outward - axis * outward.Dot(axis)).Normalized();
            var side = axis.Cross(fwd).Normalized();
            var oldShape = new PitcherShape
            {
                Height = rng.Range(0.10, 0.19),
                MouthRadius = rng.Range(0.014, 0.024),
                Old = shape.Old,
                Tissue = shape.Tissue,
                Vein = shape.Vein,
            };
            PitcherOldTrap(part, rng, oldShape, outward * rng.Range(0.02, 0.07) + Vec3.Up * 0.010,
                axis, fwd, side, Math.Max(4, rings / 2), Math.Max(8, around / 2));
        }

        // --- the flowering scape --------------------------------------------
        double scapeHeight = plantHeight * rng.Range(0.72, 0.94);
        var scapeBase = new Vec3(rng.Range(-0.03, 0.03), 0.010, rng.Range(-0.03, 0.03));
        PitcherFlower(part, rng, seed, scapeBase, scapeHeight,
            Primitives.Mix(c2, new[] { 0.40, 0.12, 0.10 }, 0.45),
            Primitives.Mix(c2, new[] { 0.26, 0.05, 0.08 }, 0.60));

        AppendPitcherWorld(m, part, matSpan, plantHeight);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void AppendPitcherWorld(MeshData target, MeshData part, double radius, double height)
    {
        double sx = 1.0 / Math.Max(0.01, radius);
        double sy = 1.0 / Math.Max(0.01, height);
        int baseVertex = target.VertexCount;
        int structuralBefore = target.Structural.Count;
        target.Append(part, p => new Vec3(p.X * sx, p.Y * sy, p.Z * sx));

        for (int v = baseVertex; v < target.VertexCount; v++)
        {
            var n = target.NormalAt(v);
            var scaled = new Vec3(n.X * radius, n.Y * height, n.Z * radius);
            double len = scaled.Length;
            if (len > 1e-9)
            {
                scaled = scaled / len;
                target.Normals[v * 3] = (float)scaled.X;
                target.Normals[v * 3 + 1] = (float)scaled.Y;
                target.Normals[v * 3 + 2] = (float)scaled.Z;
            }
        }

        // Tube radii are recorded in world metres; restate them in mesh units so the LOD tiers keep
        // classifying the scape as a scape rather than as structural wood.
        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }
}
