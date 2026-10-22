using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Glasslily - the rooted floating-leaf lily of still pond water.
/// Analogue: Nymphaea, the water lily: a knuckled rhizome on the bed, round floating pads with a
/// deep radial cleft on long petioles, and a large flower standing clear of the surface.
///
/// The plant is a piece of engineering, not a decoration. A lily pad is a floating raft: it is
/// dished so the water it carries runs to the centre, it is veined like a leaf so it can hold
/// itself out of the water, and it is split by a cleft so it can unfurl like a hand without
/// tearing its own veins. Everything here follows from those three facts.
///
/// The fantasy layer is the flower. A water lily's petals are waxy and pale; here they are cut
/// from glass, and the veins that carry them are the LEAD of a stained-glass panel: a dark wire
/// laid along each vein, with the glass cold and bright between the wires. The pad's underside
/// keeps the real plant's purple-red, but its vein network has spread into the spread of a
/// human palm, because a hand is the thing that holds water.
///
/// Reference renders: none; this species is authored from the botany of the family.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The waxy upper face of a pad: a deep, almost black green.</summary>
    private static readonly double[] LilyPadTop = { 0.17, 0.36, 0.18 };
    /// <summary>The raised radial veins on the upper face.</summary>
    private static readonly double[] LilyPadVein = { 0.34, 0.52, 0.24 };
    /// <summary>The underside of a real lily pad is a deep purple-red; the veins stand proud of it.</summary>
    private static readonly double[] LilyPadUnder = { 0.38, 0.14, 0.20 };
    private static readonly double[] LilyPadUnderVein = { 0.58, 0.24, 0.26 };
    /// <summary>The petioles: green where they run under the water, brown where they leave the rhizome.</summary>
    private static readonly double[] LilyPetiole = { 0.26, 0.38, 0.20 };
    /// <summary>The cold glass of the petals.</summary>
    private static readonly double[] LilyGlass = { 0.86, 0.93, 0.92 };
    /// <summary>The warm rose the petals keep at their base, where the flower is still warm.</summary>
    private static readonly double[] LilyRose = { 0.94, 0.66, 0.70 };
    /// <summary>The lead: the dark wire that divides a stained-glass panel.</summary>
    private static readonly double[] LilyLead = { 0.42, 0.52, 0.50 };
    /// <summary>The boss of stamens at the heart of the flower.</summary>
    private static readonly double[] LilyGold = { 0.97, 0.80, 0.30 };
    /// <summary>The still water the flower holds.</summary>
    private static readonly double[] LilyWater = { 0.05, 0.10, 0.13 };
    /// <summary>The rhizome's old, scarred tissue.</summary>
    private static readonly double[] LilyRhizome = { 0.30, 0.24, 0.18 };
    /// <summary>The fine roots hanging into the water.</summary>
    private static readonly double[] LilyRoot = { 0.42, 0.36, 0.28 };

    /// <summary>
    /// A floating pad: a dished disc with a deep radial cleft, a rolled rim, a veined upper face
    /// and a veined, wine-coloured underside.
    ///
    /// The pad is a shell, not a sheet: it has a real thickness, so the rim reads as a rolled edge
    /// from any angle and the underside can be a different material from the top. The cleft is a
    /// real cut - the two edges of the wedge are capped with the cut face, so you can see into the
    /// gap the way you can on a real pad.
    /// </summary>
    private static void GlasslilyPad(MeshData m, Vec3 centre, double padRadius, double dish, Rng rng)
    {
        int firstVertex = m.VertexCount;
        const int rings = 6;
        const int sectors = 40;
        // The cleft of a water lily runs almost to the middle of the pad. Half of 0.34 radians is
        // a 39-degree gap, which is about right for the notch a Nymphaea leaf opens from.
        const double cleft = 0.34;
        double a0 = cleft, a1 = Math.PI * 2 - cleft;
        const double rimLift = 0.005;
        const double thickness = 0.0022;

        var top = new int[rings + 1, sectors + 1];
        var bottom = new int[rings + 1, sectors + 1];
        var topNormals = new Vec3[rings + 1, sectors + 1];
        var bottomNormals = new Vec3[rings + 1, sectors + 1];

        // The vein networks. Nine primary veins radiate on the upper face, the way a lily pad
        // carries its load to the petiole. Underneath there are seven heavier ones with a finer
        // secondary net between them: the spread of a palm.
        static double VeinTop(double theta) => Math.Pow(Math.Max(0.0, Math.Cos(9.0 * theta)), 16.0);
        static double VeinUnder(double theta) =>
            Math.Pow(Math.Max(0.0, Math.Cos(7.0 * theta)), 10.0)
            + 0.45 * Math.Pow(Math.Max(0.0, Math.Cos(23.0 * theta)), 22.0);

        Vec3 Point(double u, double theta, bool upper, out double vein, out double veinU)
        {
            vein = upper ? VeinTop(theta) : VeinUnder(theta);
            veinU = vein;
            // The rim rolls: the last ring is pulled in and lifted, so the margin turns over.
            // The first ring keeps a small but real radius, because a ring of coincident points
            // would make a degenerate fan of zero-area faces in the middle of the pad.
            double radius = padRadius * (u >= 0.999 ? 0.955 : u <= 0.0001 ? 0.045 : u);
            double h = -dish * (1.0 - Math.Pow(Math.Min(1.0, u), 1.8)) + rimLift * MathD.SmoothStep(0.80, 1.0, u);
            if (upper)
            {
                h += 0.0022 * vein * (1.0 - 0.45 * u);
            }
            else
            {
                double th = thickness * (0.38 + 0.62 * (1.0 - MathD.SmoothStep(0.45, 1.0, u)));
                h -= th;
                // On the underside the veins stand out the other way, away from the leaf.
                h -= 0.0020 * vein * (1.0 - 0.40 * u);
            }
            return centre + new Vec3(Math.Cos(theta) * radius, h, Math.Sin(theta) * radius);
        }

        for (int i = 0; i <= rings; i++)
        {
            double u = i / (double)rings;
            for (int j = 0; j <= sectors; j++)
            {
                double theta = a0 + (a1 - a0) * (j / (double)sectors);
                var p = Point(u, theta, true, out double vein, out _);
                var col = Primitives.Mix(LilyPadTop, LilyPadVein, Math.Min(1.0, vein * 0.8));
                // The pad's UVs run angle-first, radius-second. The shader's leaf venation draws
                // its branch veins at a constant position along the leaf, so an angle-first pad
                // gets RADIAL veins - which is what a lily pad actually is - instead of the
                // concentric ripples an angle-last pad would produce.
                top[i, j] = m.AddVertex(p, Vec3.Up, col, 1, j / (double)sectors, u, 0, 1);

                var pb = Point(u, theta, false, out double veinB, out _);
                var colB = Primitives.Mix(LilyPadUnder, LilyPadUnderVein, Math.Min(1.0, veinB * 0.85));
                bottom[i, j] = m.AddVertex(pb, -Vec3.Up, colB, 1, j / (double)sectors, u, 0, 1);
            }
        }

        // The upper face.
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                var a = m.Position(top[i, j]);
                var b = m.Position(top[i, j + 1]);
                var c = m.Position(top[i + 1, j]);
                var d = m.Position(top[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                if (n.Y < 0) n = -n;
                topNormals[i, j] += n;
                topNormals[i, j + 1] += n;
                topNormals[i + 1, j] += n;
                topNormals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, top[i, j], top[i + 1, j], top[i, j + 1], n);
                Primitives.TriangleFacing(m, top[i, j + 1], top[i + 1, j], top[i + 1, j + 1], n);
            }

        // The underside.
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                var a = m.Position(bottom[i, j]);
                var b = m.Position(bottom[i, j + 1]);
                var c = m.Position(bottom[i + 1, j]);
                var d = m.Position(bottom[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                if (n.Y > 0) n = -n;
                bottomNormals[i, j] += n;
                bottomNormals[i, j + 1] += n;
                bottomNormals[i + 1, j] += n;
                bottomNormals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, bottom[i, j], bottom[i, j + 1], bottom[i + 1, j], n);
                Primitives.TriangleFacing(m, bottom[i, j + 1], bottom[i + 1, j + 1], bottom[i + 1, j], n);
            }

        // The centre of the pad is capped with a small fan on both faces, so the middle of the
        // leaf is a surface and not a ring of coincident points.
        {
            int tc = m.AddVertex(centre - Vec3.Up * dish, Vec3.Up, Primitives.Mix(LilyPadTop, LilyPadVein, 0.35), 1, 0.0, 0.5, 0, 1);
            int bc = m.AddVertex(centre - Vec3.Up * (dish + thickness), -Vec3.Up, Primitives.Mix(LilyPadUnder, LilyPadUnderVein, 0.35), 1, 0.0, 0.5, 0, 1);
            for (int j = 0; j < sectors; j++)
            {
                Primitives.TriangleFacing(m, tc, top[0, j], top[0, j + 1], Vec3.Up);
                Primitives.TriangleFacing(m, bc, bottom[0, j + 1], bottom[0, j], -Vec3.Up);
            }
        }

        // The rolled rim: a strip of faces joining the two shells round the outside.
        for (int j = 0; j < sectors; j++)
        {
            var a = m.Position(top[rings, j]);
            var b = m.Position(top[rings, j + 1]);
            var c = m.Position(bottom[rings, j]);
            var d = m.Position(bottom[rings, j + 1]);
            var n = (b - a).Cross(c - a);
            if (n.LengthSq < 1e-18) continue;
            n = n.Normalized();
            Primitives.TriangleFacing(m, top[rings, j], bottom[rings, j], top[rings, j + 1], n);
            Primitives.TriangleFacing(m, top[rings, j + 1], bottom[rings, j], bottom[rings, j + 1], n);
        }

        // The two cut faces of the cleft, so the notch has a visible thickness.
        for (int i = 0; i < rings; i++)
        {
            foreach (int j in new[] { 0, sectors })
            {
                var p0 = m.Position(top[i, j]);
                var p1 = m.Position(top[i + 1, j]);
                var p2 = m.Position(bottom[i, j]);
                var p3 = m.Position(bottom[i + 1, j]);
                var n = (p1 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                Primitives.TriangleFacing(m, top[i, j], bottom[i, j], top[i + 1, j], n);
                Primitives.TriangleFacing(m, top[i + 1, j], bottom[i, j], bottom[i + 1, j], n);
            }
        }

        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var nt = topNormals[i, j];
                if (nt.LengthSq < 1e-18) nt = Vec3.Up;
                nt = nt.Normalized();
                int v = top[i, j];
                m.Normals[v * 3] = (float)nt.X;
                m.Normals[v * 3 + 1] = (float)nt.Y;
                m.Normals[v * 3 + 2] = (float)nt.Z;

                var nb = bottomNormals[i, j];
                if (nb.LengthSq < 1e-18) nb = -Vec3.Up;
                nb = nb.Normalized();
                int vb = bottom[i, j];
                m.Normals[vb * 3] = (float)nb.X;
                m.Normals[vb * 3 + 1] = (float)nb.Y;
                m.Normals[vb * 3 + 2] = (float)nb.Z;
            }

        // The pad keeps its own pigment. The shader's procedural leaf venation assumes a leaf that
        // runs along its own length, and on a disc that reads as concentric ripples, so the pad is
        // flagged as an organ and carries the vein network it was modelled with.
        NewFloraMarkOrgan(m, firstVertex, m.VertexCount - firstVertex);
    }
    /// <summary>
    /// The rhizome: a short, knuckled, horizontal axis lying on the bed, wearing the stubs of the
    /// leaves it shed last year, with a curtain of fine roots hanging into the water below it.
    /// </summary>
    private static void GlasslilyRhizome(MeshData m, Vec3 origin, double length, Rng rng)
    {
        int knuckles = 5;
        var path = new Vec3[knuckles + 1];
        var radii = new double[knuckles + 1];
        var lateral = new Vec3(Math.Cos(0.4), 0, Math.Sin(0.4));
        for (int i = 0; i <= knuckles; i++)
        {
            double t = i / (double)knuckles;
            path[i] = origin + lateral * (length * t) + Vec3.Up * (0.002 * Math.Sin(t * 7.0));
            // The rhizome is a string of swollen knuckles with a narrow waist between them.
            double swell = 0.85 + 0.5 * Math.Abs(Math.Cos(t * Math.PI * knuckles));
            radii[i] = 0.0055 * swell;
        }
        Primitives.Tube(m, path, radii, 8,
            (i, v) => (LilyRhizome, 1, i / (double)knuckles, v, 0, 0));

        // The old leaf bases: a cone of shrivelled, dark stubs at each node.
        for (int i = 0; i <= knuckles; i++)
        {
            var at = path[i];
            double a = rng.Range(0, 2 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var tip = at + outward * rng.Range(0.008, 0.016) + Vec3.Up * rng.Range(0.004, 0.012);
            Primitives.Tube(m, new[] { at - outward * 0.002, at + outward * 0.004, tip },
                new[] { 0.0032, 0.0022, 0.0006 }, 5,
                (k, v) => (Primitives.Scale(LilyRhizome, 0.82), 1, k * 0.5, v, 0, 0));
        }

        // The roots: fine, unbranched, hanging straight down into the water and swaying a little.
        int roots = NewFloraCount(m, 26);
        for (int i = 0; i < roots; i++)
        {
            var at = origin + lateral * (length * rng.NextDouble()) + new Vec3(rng.Range(-0.004, 0.004), 0, rng.Range(-0.004, 0.004));
            double len = rng.Range(0.020, 0.048);
            var sway = new Vec3(rng.Range(-0.004, 0.004), 0, rng.Range(-0.004, 0.004));
            Primitives.Tube(m, new[] { at, at + sway * 0.4 - Vec3.Up * (len * 0.5), at + sway - Vec3.Up * len },
                new[] { 0.0009, 0.0005, 0.00012 }, 4,
                (k, v) => (LilyRoot, 1, k * 0.5, v, 0, 0));
        }
    }

    /// <summary>
    /// One flower: three whorls of glass petals round a golden boss, with the water the flower
    /// holds still as a dark lens in the cup.
    /// </summary>
    private static void GlasslilyFlower(MeshData m, Vec3 basePoint, double scale, Rng rng)
    {
        // The receptacle: the swollen green mass the petals are set into.
        Primitives.Ellipsoid(m, basePoint + Vec3.Up * (0.004 * scale), new Vec3(0.010 * scale, 0.006 * scale, 0.010 * scale),
            5, 9, (u, v) => (Primitives.Mix(LilyPadTop, LilyPadVein, 0.4), 1, u, v, 0, 0));

        // The three whorls. The outer whorl is the largest and lies almost flat; the inner whorl is
        // short and stands up and closes the cup. This is how a real water lily is built, and it
        // is what gives the flower its depth instead of a flat rosette.
        var whorls = new (int count, double length, double width, double rise, double drop, double pointiness)[]
        {
            (9, 0.072, 0.030, 0.10, 0.24, 1.0),
            (8, 0.056, 0.024, 0.32, 0.14, 0.85),
            (6, 0.038, 0.019, 0.56, 0.04, 0.55),
        };

        foreach (var w in whorls)
        {
            int count = NewFloraCount(m, w.count);
            for (int k = 0; k < count; k++)
            {
                double a = 2 * Math.PI * k / count + rng.Range(-0.09, 0.09) + w.count * 0.11;
                // The petals of a water lily overlap by a good half of their width, and the whorl
                // is offset from the one below so the seams do not line up.
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                double rise = w.rise + rng.Range(-0.05, 0.05);
                var dir = (outward * Math.Cos(rise) + Vec3.Up * Math.Sin(rise)).Normalized();

                int mark = m.VertexCount;
                NewFloraPetal(m,
                    basePoint + outward * (0.002 * scale) + Vec3.Up * (0.003 * scale),
                    dir, Vec3.Up,
                    w.length * scale * rng.Range(0.92, 1.08),
                    w.width * scale * rng.Range(0.9, 1.1),
                    // A lily petal is broadest a little past halfway and narrows to a point, and
                    // the outer whorl is the pointiest of the three.
                    t => {
                        double swell = Math.Sin(Math.PI * Math.Min(1.0, 0.12 + 0.88 * t));
                        double point = 1.0 - w.pointiness * 0.72 * Math.Pow(Math.Max(0.0, t - 0.45) / 0.55, 1.6);
                        return Math.Max(0.04, swell * point + (1.0 - w.pointiness) * 0.22 * (1.0 - t));
                    },
                    (t, x, vein) =>
                    {
                        // Cold glass at the tip, the warm rose of the base, and the lead of a
                        // stained-glass panel laid along every vein.
                        var col = Primitives.Mix(LilyRose, LilyGlass, MathD.SmoothStep(0.02, 0.62, t));
                        col = Primitives.Mix(col, LilyLead, Math.Min(1.0, vein * 0.9));
                        // A darker line down the exact centre of the petal.
                        col = Primitives.Mix(col, LilyLead, Math.Min(1.0, Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(x) * 6.0), 3.0) * 0.35));
                        return col;
                    },
                    rows: 7, across: 9,
                    cup: 0.26, camber: -0.04, tipCurl: w.drop,
                    veinCount: 2, veinDepth: 0.08);
                // Cut glass: the petals keep their authored pigment and pick up the view-angle
                // prismatic shift that makes glass read as glass.
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark, glass: true);
            }
        }

        // The boss of stamens: a brush of golden filaments, each tipped with an anther.
        int stamens = NewFloraCount(m, 22);
        for (int i = 0; i < stamens; i++)
        {
            double a = 2 * Math.PI * i / stamens + rng.Range(-0.2, 0.2);
            double lean = rng.Range(0.35, 0.95);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var dir = (outward * Math.Sin(lean) + Vec3.Up * Math.Cos(lean)).Normalized();
            double len = rng.Range(0.010, 0.020) * scale;
            var at = basePoint + Vec3.Up * (0.004 * scale);
            var tip = at + dir * len;
            NewFloraFilament(m, at, tip, 0.00055 * scale, LilyGold, 4);
            Primitives.Ellipsoid(m, tip, new Vec3(0.0016 * scale, 0.0028 * scale, 0.0016 * scale), 2, 5,
                (u, v) => (Primitives.Scale(LilyGold, 0.92), 1, u, v, 0, 0));
        }

        // The still water the flower holds. It is a shallow lens, not a flat disc, so it catches
        // the light along one edge the way a meniscus does, and its rim is tucked under the bases
        // of the petals so it never floats free of the flower.
        double waterR = 0.011 * scale;
        var waterCentre = basePoint + Vec3.Up * (0.0035 * scale);
        int wc = m.AddVertex(waterCentre + Vec3.Up * (0.0012 * scale), Vec3.Up, LilyWater, 1, 0.5, 0.5, 0, 0);
        var rim = new int[20];
        for (int i = 0; i < 20; i++)
        {
            double a = 2 * Math.PI * i / 20;
            // A crescent of reflected sky along the far edge.
            double lift = 0.0016 * scale * Math.Max(0.0, Math.Cos(a - 2.2));
            var col = Primitives.Mix(LilyWater, LilyGlass, 0.35 * Math.Max(0.0, Math.Cos(a - 2.2)));
            rim[i] = m.AddVertex(waterCentre + new Vec3(Math.Cos(a) * waterR, lift, Math.Sin(a) * waterR),
                Vec3.Up, col, 1, 0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a), 0, 0);
        }
        for (int i = 0; i < 20; i++)
            Primitives.TriangleFacing(m, wc, rim[i], rim[(i + 1) % 20], Vec3.Up);
    }

    /// <summary>
    /// A closed bud: a spindle of tightly wrapped petals on its own stalk, with a few green
    /// sepals clasping its base.
    /// </summary>
    private static void GlasslilyBud(MeshData m, Vec3 basePoint, double height, Rng rng)
    {
        double r = height * 0.24;
        int wraps = 6;
        for (int k = 0; k < wraps; k++)
        {
            double a = 2 * Math.PI * k / wraps;
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var dir = (outward * 0.34 + Vec3.Up).Normalized();
            int mark = m.VertexCount;
            NewFloraBlade(m,
                basePoint + outward * (r * 0.30),
                dir, Vec3.Up,
                height * rng.Range(0.80, 1.0),
                r * 0.42,
                t => Math.Sin(Math.PI * Math.Min(1.0, 0.06 + 0.94 * t)) * (0.5 + 0.5 * Math.Sin(Math.PI * t)),
                t => Primitives.Mix(LilyRose, LilyGlass, MathD.SmoothStep(0.1, 0.8, t)),
                rows: 6, sides: 6, flatness: 0.55, keel: 0.10, cup: 0.30, camber: 0.22, tipCurl: 0.12);
            NewFloraMarkOrgan(m, mark, m.VertexCount - mark, glass: true);
        }
        // The green sepals at the foot of the bud.
        for (int k = 0; k < 4; k++)
        {
            double a = 2 * Math.PI * k / 4 + 0.3;
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            NewFloraBlade(m, basePoint - Vec3.Up * (height * 0.06), (outward * 0.75 - Vec3.Up * 0.25).Normalized(),
                Vec3.Up, height * 0.34, height * 0.10,
                t => Math.Sin(Math.PI * Math.Min(1.0, 0.1 + 0.9 * t)),
                t => Primitives.Mix(LilyPadTop, LilyPadVein, 0.3),
                rows: 4, sides: 5, flatness: 0.4, keel: 0.22, camber: 0.3);
        }
    }

    /// <summary>The lily: a rhizome on the bed, floating pads on long petioles, one tall flower.</summary>
    private static void Glasslily(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The bed sits just under the waterline; the surface is y = 0 and the flower stands above it.
        var bed = new Vec3(0, -0.055, 0);
        GlasslilyRhizome(part, bed, 0.055, rng);

        // The pads. A lily spreads by putting a pad on the end of a long petiole, so the pads are
        // scattered around the rhizome on stalks that lean outwards.
        int pads = NewFloraCount(m, 5);
        for (int i = 0; i < pads; i++)
        {
            double a = 2 * Math.PI * i / pads + rng.Range(-0.35, 0.35);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = rng.Range(0.16, 0.24);
            var root = bed + outward * rng.Range(0.005, 0.030) + new Vec3(rng.Range(-0.006, 0.006), 0, rng.Range(-0.006, 0.006));
            var padCentre = root + outward * reach + Vec3.Up * 0.055;

            // The petiole: a water lily's stalk is long and slack, and it bends over at the top so
            // the pad lies flat on the surface with its own weight pulling it down.
            NewFloraStalk(part, root, padCentre, outward, reach * 0.16,
                0.0032, 0.0020,
                Primitives.Mix(LilyRhizome, LilyPetiole, 0.5), Primitives.Mix(LilyPetiole, LilyPadVein, 0.3),
                segments: 8, sides: 6);

            double padR = rng.Range(0.105, 0.135);
            // The pad lies almost flat, with a slight tilt, the way a floating pad does.
            var tilt = new Vec3(rng.Range(-0.05, 0.05), 1, rng.Range(-0.05, 0.05)).Normalized();
            var padPos = new Vec3(padCentre.X, 0, padCentre.Z);
            GlasslilyPad(part, padPos, padR, 0.004, rng);
        }

        // The flower, on its own stalk well clear of the pads.
        var flowerRoot = bed + new Vec3(rng.Range(-0.01, 0.01), 0, rng.Range(-0.01, 0.01));
        var flowerBase = flowerRoot + Vec3.Up * rng.Range(0.20, 0.25);
        NewFloraStalk(part, flowerRoot, flowerBase, new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02)), 0.012,
            0.0042, 0.0028,
            Primitives.Mix(LilyRhizome, LilyPetiole, 0.55), Primitives.Mix(LilyPetiole, LilyPadVein, 0.4),
            segments: 9, sides: 7);
        GlasslilyFlower(part, flowerBase, 1.0, rng);

        // One or two buds, still closed.
        int buds = NewFloraCount(m, 2);
        for (int i = 0; i < buds; i++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = bed + outward * rng.Range(0.01, 0.04);
            var budBase = root + Vec3.Up * rng.Range(0.13, 0.19);
            NewFloraStalk(part, root, budBase, outward, 0.010,
                0.0028, 0.0020, Primitives.Mix(LilyRhizome, LilyPetiole, 0.5), LilyPetiole, segments: 7, sides: 6);
            GlasslilyBud(part, budBase, 0.045, rng);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
