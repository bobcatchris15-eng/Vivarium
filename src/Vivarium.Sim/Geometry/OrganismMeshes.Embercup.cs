using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Embercup - the cushion moss that ripens lit spore cups.
///
/// A cushion moss is a dense low mound of upright shoots: Grimmia pulvinata or Polytrichum
/// commune, the grey-green hummocks that sit on boulders and on the shoulders of old walls.
/// You never see the whole plant in a cushion moss, only its edge, where the packed shoots
/// stand up like the pile of a hundred needles and the cushion breaks into a lumpy, uneven
/// skyline. The plant is modelled that way: a lobed mat of packed stems, with a scatter of
/// shoots rising out of it, each shoot a hair-thin stem clothed in ranks of tiny overlapping
/// microphylls that are a millimetre or two long.
///
/// The sporophytes are the second half of the plant and the half the eye goes to. A moss seta
/// is a brown hair that lifts the capsule clear of the cushion so the spores can be shed into
/// the wind; in Splachnum the seta and the swollen foot below the capsule are themselves
/// brilliantly coloured, which is why that genus is in every old flora book. Here that colour
/// is the fantasy layer: the capsules are ember orange, and they are the one lit organ of the
/// plant, so a bed of embercups reads as a scatter of live coals on wet stone after dark.
///
/// Everything else is honest botany. The olive cushion with its bronze flecked leaf tips. The
/// russet setae leaning at their own angles. The capsule with its swollen foot, its
/// cylindrical case, its narrow neck and its beaked lid. One capsule has shed its lid and
/// shows the dark mouth and the pale ring of spores around it.
///
/// Authored in real metres and converted into the engine frame by NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Palette. The content file supplies two colours (c1 the olive cushion, c2 the ember);
    // these are the species' own working tints, which the content colours are mixed into.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The mat inside the cushion: the olive of old stems, in the shade of the cushion.</summary>
    private static readonly double[] EmbercupMat = { 0.052, 0.082, 0.036 };
    /// <summary>A living microphyll: dull olive green, the colour of Polytrichum in shade.</summary>
    private static readonly double[] EmbercupOlive = { 0.120, 0.166, 0.072 };
    /// <summary>The pale tip of a microphyll, where the leaf thins and the light comes through.</summary>
    private static readonly double[] EmbercupOlivePale = { 0.226, 0.268, 0.104 };
    /// <summary>The bronze fleck a dried moss leaf tip takes in low sun.</summary>
    private static readonly double[] EmbercupGold = { 0.545, 0.408, 0.098 };
    /// <summary>The seta: the dull russet of a real moss seta, dark against the olive cushion.</summary>
    private static readonly double[] EmbercupSeta = { 0.232, 0.096, 0.038 };
    /// <summary>The seta at the neck, where it warms towards the capsule.</summary>
    private static readonly double[] EmbercupSetaTip = { 0.402, 0.182, 0.072 };
    /// <summary>The capsule: ember orange.</summary>
    private static readonly double[] EmbercupEmber = { 0.930, 0.300, 0.068 };
    /// <summary>The swollen foot of the capsule, where the colour is deepest.</summary>
    private static readonly double[] EmbercupEmberDeep = { 0.420, 0.096, 0.024 };
    /// <summary>The beak of the operculum, hot at the point.</summary>
    private static readonly double[] EmbercupEmberHot = { 1.000, 0.545, 0.160 };
    /// <summary>The throat of an open capsule: the dark mouth and the spore mass below it.</summary>
    private static readonly double[] EmbercupMouth = { 0.038, 0.019, 0.014 };
    /// <summary>The spilled spores: a pale, dry ash, the one colour in the plant that is not alive.</summary>
    private static readonly double[] EmbercupSpore = { 0.852, 0.798, 0.658 };

    // ---------------------------------------------------------------------------------------------
    // The plan form of the cushion
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The plan form of the cushion. A Grimmia cushion is not a disc; it is a hummock with
    /// three or four soft lobes and a scalloped edge, so the plan radius is modulated by a
    /// few low harmonics instead of being a circle. The same factors drive the mat and the
    /// shoots, so the two can never disagree about where the moss is.
    /// </summary>
    private static readonly double[] EmbercupLobeOrder = { 3.0, 5.0, 7.0 };
    private static readonly double[] EmbercupLobeDepth = { 0.088, 0.052, 0.028 };
    private static readonly double[] EmbercupLobePhase = { 0.72, 2.31, 4.06 };

    /// <summary>The cushion's plan radius at an azimuth, as a multiple of its mean radius.</summary>
    private static double EmbercupPlanFactor(double theta)
    {
        double f = 1.0;
        for (int i = 0; i < EmbercupLobeOrder.Length; i++)
            f += EmbercupLobeDepth[i] * Math.Cos(EmbercupLobeOrder[i] * theta + EmbercupLobePhase[i]);
        return f;
    }

    /// <summary>
    /// The knuckling of the cushion's mat: the hummocks and hollows between them, as a
    /// fraction of the mat's height. The colour of the mat is painted from the same field,
    /// so the pale new growth sits on the high knuckles and the old dark stems lie in the
    /// hollows, which is how a real cushion moss is coloured.
    /// </summary>
    private static double EmbercupKnuckle(double theta, double u)
    {
        double knob = 0.30 * Math.Sin(3.0 * theta + 2.1 * u + 1.7)
            + 0.20 * Math.Sin(5.0 * theta - 3.3 * u + 4.2)
            + 0.13 * Math.Sin(7.0 * theta + 4.7 * u + 2.6);
        // The transverse ridges: the scars of old dead stems lying across the mat.
        return knob + 0.11 * Math.Sin(u * 12.0 + 1.4);
    }

    /// <summary>
    /// The height of the cushion's mat at a point, in metres above the ground. The shoots are
    /// planted on this surface, so the mat and the shoots always agree about the shape of
    /// the mound.
    ///
    /// A cushion moss is not a smooth dome. The surface is knuckled: the big lobes of the
    /// mat, the smaller hummocks between them, and the transverse ridges the dead stems
    /// leave lying across it. All of them are here, because a smooth mound reads as a
    /// pastry and a knuckled one reads as moss. Every term is given a radial phase as well
    /// as an angular one, so the knuckles run across the cushion instead of fanning out of
    /// its centre like the segments of a pumpkin.
    /// </summary>
    private static double EmbercupDomeHeight(double r, double theta, double baseRadius, double height)
    {
        double u = Math.Clamp(r / Math.Max(1e-6, baseRadius * EmbercupPlanFactor(theta)), 0.0, 1.0);
        double dome = Math.Pow(Math.Max(0.0, 1.0 - u * u), 0.55);
        // Every knuckle of the cushion lives in the outer two thirds of it. The crown of a
        // moss cushion is a smooth dome; the hummocks are out towards the rim, where the
        // shoots have crowded and died back.
        double shape = Math.Max(0.0, u * u * (1.0 - u) * 6.0);
        return height * Math.Max(0.0, dome + EmbercupKnuckle(theta, u) * shape);
    }

    // ---------------------------------------------------------------------------------------------
    // The mat
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The mat of the cushion: a lobed dome that closes the underside of the plant, so the
    /// shoots are not growing out of nothing and the cushion reads as a solid body of moss
    /// rather than as a handful of hairs over bare ground. It is deliberately darker than the
    /// shoots, because the inside of a cushion moss sits in its own shadow.
    ///
    /// The shading of the mat is analytic, taken from the height field itself rather than
    /// averaged from the triangles. A cushion moss is a soft body, and the long thin wedges
    /// of a polar mesh band it into a fan of pale stripes if their normals are left to the
    /// faces.
    /// </summary>
    private static void EmbercupCushionBase(MeshData m, double baseRadius, double height,
        double[] deep, double[] crown)
    {
        // The mat is the body of the plant, so it keeps its resolution longer than a leaf
        // does: a coarse mat still has to read as a mossy lump.
        int rings = 14, sides = 28;
        NewFloraBudget(m, ref rings, ref sides, minRows: 8, minSides: 14);
        // The underside sits a hair below the ground line so the flat base can never z-fight
        // with the soil it is standing on.
        const double floorY = -0.0006;

        var grid = new int[rings + 2, sides + 1];

        grid[0, 0] = m.AddVertex(new Vec3(0, height, 0), Vec3.Up, crown, 1, 0.0, 0.5, 0, 0);
        for (int i = 1; i <= rings; i++)
        {
            double v = i / (double)rings;
            for (int j = 0; j <= sides; j++)
            {
                double th = 2 * Math.PI * j / sides;
                double r = baseRadius * EmbercupPlanFactor(th) * v;
                double y = EmbercupDomeHeight(r, th, baseRadius, height);
                var p = new Vec3(Math.Cos(th) * r, y, Math.Sin(th) * r);
                var n = i == rings ? EmbercupMatNormal(r, th, baseRadius, height) : Vec3.Up;
                // The mottling: the colour of the mat is painted from the same knuckle field
                // that shapes it, so the pale new growth sits on the high knuckles and the
                // old dark stems lie in the hollows between them.
                double u = i / (double)rings;
                double up = Math.Clamp(y / Math.Max(1e-6, height), 0.0, 1.0);
                double knuckle = Math.Clamp(0.5 + 0.62 * EmbercupKnuckle(th, u), 0.0, 1.0);
                double tone = Math.Clamp(up * 0.58 + knuckle * 0.48, 0.0, 1.0);
                grid[i, j] = m.AddVertex(p, n, Primitives.Mix(deep, crown, tone), 1,
                    v, (double)j / sides, 0, 0);
            }
        }

        // The apex fan.
        for (int j = 0; j < sides; j++)
        {
            var a = m.Position(grid[0, 0]);
            var b = m.Position(grid[1, j]);
            var c = m.Position(grid[1, j + 1]);
            var n = EmbercupOutwardNormal(a, b, c, true);
            if (n == Vec3.Zero) continue;
            Primitives.TriangleFacing(m, grid[0, 0], grid[1, j], grid[1, j + 1], n);
        }
        // The rings.
        for (int i = 1; i < rings; i++)
            for (int j = 0; j < sides; j++)
            {
                var a = m.Position(grid[i, j]);
                var b = m.Position(grid[i, j + 1]);
                var c = m.Position(grid[i + 1, j]);
                var d = m.Position(grid[i + 1, j + 1]);
                var n = EmbercupOutwardNormal(a, b, c, true);
                if (n == Vec3.Zero) continue;
                Primitives.TriangleFacing(m, grid[i, j], grid[i, j + 1], grid[i + 1, j], n);
                Primitives.TriangleFacing(m, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], n);
            }

        // The flat underside, a hair below the ground so it never fights the soil.
        int under = m.AddVertex(new Vec3(0, floorY, 0), Vec3.Down, deep, 1, 0.0, 0.5, 0, 0);
        for (int j = 0; j < sides; j++)
        {
            var a = m.Position(under);
            var b = m.Position(grid[rings, j]);
            var c = m.Position(grid[rings, j + 1]);
            var n = EmbercupOutwardNormal(a, b, c, false);
            if (n == Vec3.Zero) continue;
            Primitives.TriangleFacing(m, under, grid[rings, j], grid[rings, j + 1], n);
        }

        // The shading of the mat comes from the height field itself, so the cushion is a soft
        // body and not a fan of pale wedges.
        for (int i = 1; i <= rings; i++)
            for (int j = 0; j <= sides; j++)
            {
                double th = 2 * Math.PI * j / sides;
                double r = baseRadius * EmbercupPlanFactor(th) * (i / (double)rings);
                var n = i == rings ? EmbercupMatNormal(r, th, baseRadius, height) : Vec3.Up;
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }
    }

    /// <summary>
    /// The normal of the mat at a point, taken from the gradient of its own height field.
    /// Differentiating the surface rather than averaging the triangles is what keeps a
    /// mossy cushion looking soft.
    /// </summary>
    private static Vec3 EmbercupMatNormal(double r, double theta, double baseRadius, double height)
    {
        const double dr = 0.0018;
        const double dth = 0.040;
        double hr = (EmbercupDomeHeight(r + dr, theta, baseRadius, height)
            - EmbercupDomeHeight(Math.Max(0.0, r - dr), theta, baseRadius, height)) / (2 * dr);
        double ht = (EmbercupDomeHeight(r, theta + dth, baseRadius, height)
            - EmbercupDomeHeight(r, theta - dth, baseRadius, height)) / (2 * dth);
        double cs = Math.Cos(theta), sn = Math.Sin(theta);
        double inv = r > 1e-6 ? 1.0 / r : 0.0;
        double gx = hr * cs - ht * inv * sn;
        double gz = hr * sn + ht * inv * cs;
        var n = new Vec3(-gx, 1.0, -gz);
        return n.LengthSq > 1e-12 ? n.Normalized() : Vec3.Up;
    }

    /// <summary>
    /// The outward normal of a cushion face. The cushion is a closed mound, so the sign of the
    /// geometric normal is decided by the direction the face must be seen from: up and away
    /// from the axis for the outside of the mound, straight down for the underside.
    /// </summary>
    private static Vec3 EmbercupOutwardNormal(Vec3 a, Vec3 b, Vec3 c, bool up)
    {
        var g = (b - a).Cross(c - a);
        if (g.LengthSq < 1e-20) return Vec3.Zero;
        g = g.Normalized();
        if (!up) return g.Y > 0 ? -g : g;
        // The reference direction at the face: out along the cushion's own radius, lifted.
        var radial = new Vec3(a.X + b.X + c.X, 0, a.Z + b.Z + c.Z);
        if (radial.LengthSq < 1e-20) return g.Y > 0 ? g : -g;
        var ref0 = (radial.Normalized() * 0.78 + Vec3.Up * 0.78).Normalized();
        return g.Dot(ref0) < 0 ? -g : g;
    }

    // ---------------------------------------------------------------------------------------------
    // The shoots and their microphylls
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The width profile of a microphyll, as a fraction of its half width. A moss leaf is a
    /// single cell thick, lanceolate, widest just above the sheath and drawn out to an acute
    /// point, so the profile opens fast at the base and then tapers for the rest of its life.
    /// </summary>
    private static double EmbercupMicroProfile(double t)
    {
        if (t < 0.14) return 0.46 + 3.86 * (t / 0.14);            // out of the sheath
        return Math.Max(0.04, 1.0 - 0.97 * Math.Pow((t - 0.14) / 0.86, 1.45));
    }

    /// <summary>
    /// One shoot of the cushion: a hair-thin tapering stem carrying three or four ranks of
    /// microphylls. The ranks spiral, as the leaves of a moss shoot do, and each leaf is
    /// appressed back towards the stem: a moss leaf curls inward when it dries, and that
    /// curl is what gives a cushion its bristled edge.
    /// </summary>
    private static void EmbercupMicroStem(MeshData m, Vec3 root, Vec3 axis, double height, double azimuth,
        Rng rng, double[] olive, double[] pale, double[] gold)
    {
        // Three points is all a one-centimetre shoot needs: a moss shoot is not a curve, it is
        // a slightly kinked hair that leans off the cushion.
        var side = axis.Cross(new Vec3(1, 0, 0));
        if (side.LengthSq < 1e-10) side = axis.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var p0 = root;
        var p1 = root + axis * (height * 0.5) + side * (height * rng.Range(-0.05, 0.05));
        var p2 = root + axis * height + side * (height * rng.Range(-0.13, 0.13));

        var stemCol = Primitives.Scale(olive, 0.78);
        Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.00022, 0.00015, 0.000055 }, 5,
            (i, v) => (Primitives.Mix(stemCol, olive, (double)i / 2.0), 1, i * 0.5, v, 0, 0));

        // The microphylls. Their size is fixed by the plant - a moss leaf is a millimetre or
        // two - so what carries the cushion is the number of ranks and the way they spiral.
        int ranks = rng.Chance(0.38) ? 4 : 3;
        double rank0 = azimuth + rng.Range(0.0, Math.PI * 2.0);
        for (int r = 0; r < ranks; r++)
        {
            double t = ranks == 1 ? 0.5 : 0.28 + 0.66 * (r / (double)(ranks - 1));
            var at = t <= 0.5 ? Vec3.Lerp(p0, p1, t * 2.0) : Vec3.Lerp(p1, p2, (t - 0.5) * 2.0);
            double rankAz = rank0 + r * 2.399963229728653;   // the golden angle: the ranks spiral
            for (int k = 0; k < 2; k++)
            {
                double la = rankAz + k * Math.PI + rng.Range(-0.20, 0.20);
                var radial = new Vec3(Math.Cos(la), 0, Math.Sin(la));
                // The leaf leaves the shoot at about thirty degrees and then curves back up
                // along it, the way a moss leaf lies against its stem: the shoot reads as a
                // shingled column, not as a wire with blades on it.
                double elev = rng.Range(0.42, 0.80);
                var leafAxis = (axis * Math.Cos(elev) + radial * Math.Sin(elev)).Normalized();
                double len = rng.Range(0.0014, 0.0028);
                double wid = len * rng.Range(0.155, 0.205);
                // The gold flecks: the light catches the tips of some leaves and not others,
                // which is why a cushion moss sparkles in raking sun and not in flat light.
                bool fleck = rng.Chance(0.26);
                NewFloraBlade(m, at, leafAxis, axis, len, wid,
                    EmbercupMicroProfile,
                    u =>
                    {
                        var c = Primitives.Mix(olive, pale, MathD.SmoothStep(0.20, 1.0, u));
                        double k = fleck ? MathD.SmoothStep(0.42, 1.0, u) : MathD.SmoothStep(0.78, 1.0, u) * 0.28;
                        return Primitives.Mix(c, gold, k);
                    },
                    rows: 2, sides: 5, flatness: 0.32, keel: 0.40, camber: 0.30);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The capsule
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One capsule: the spore case of the sporophyte. A moss capsule is a swelling foot, a
    /// cylindrical case, a narrow neck, and a pointed lid - the operculum - that is thrown
    /// off when the spores are ripe. The organ is a surface of revolution, so it is lofted
    /// with the shared blade builder: a round section and a width profile that walks up the
    /// case and tapers to the beak.
    ///
    /// The ember colour is the fantasy layer, and the whole organ is flagged with the
    /// shader's marked-nodule band, so that it is the one part of the plant that glows after
    /// dark.
    /// </summary>
    private static void EmbercupCapsule(MeshData m, Vec3 basePoint, Vec3 axis, double length, double radius,
        bool shed, Rng rng, double[] ember, double[] deep, double[] hot, double[] mouth, double[] spore)
    {
        int rows = 8, sides = 10;

        // The profile of the case, as a fraction of the capsule's radius.
        Func<double, double> profile = t =>
        {
            if (t < 0.06) return 0.60 + 0.40 * (t / 0.06);                        // the foot swells out of the seta
            if (t < 0.20) return 1.00 - 0.20 * ((t - 0.06) / 0.14);               // and draws back in
            if (t < 0.60) return 0.80 + 0.06 * Math.Sin(Math.PI * (t - 0.20) / 0.40); // the case, faintly barrelled
            if (t < 0.76) return 0.80 - 0.36 * ((t - 0.60) / 0.16);               // the neck draws in
            if (shed) return 0.44;                                                // an open mouth, no lid
            return 0.44 * Math.Max(0.0, 1.0 - Math.Pow((t - 0.76) / 0.24, 1.6));  // the beak of the operculum
        };

        Func<double, double[]> pigment = t =>
        {
            if (t < 0.12) return Primitives.Mix(deep, ember, MathD.SmoothStep(0.0, 0.12, t));
            if (t < 0.72) return Primitives.Mix(ember, Primitives.Scale(ember, 0.86), (t - 0.12) / 0.60);
            return Primitives.Mix(ember, hot, MathD.SmoothStep(0.72, 1.0, t));
        };

        // The frame the case is turned in. The dorsal vector is horizontal and square to the
        // axis, so the section never degenerates however the sporophyte leans.
        var a0 = axis.Normalized();
        var dorsal = new Vec3(-a0.Z, 0, a0.X);
        if (dorsal.LengthSq < 1e-10) dorsal = new Vec3(1, 0, 0);
        dorsal = dorsal.Normalized();

        int first = m.VertexCount;
        NewFloraBlade(m, basePoint, a0, dorsal, length, radius, profile, pigment,
            rows: rows, sides: sides, flatness: 1.0, volumetric: true);

        // The lit band. UV2.x = 1.35 is the shader's marked-nodule range and UV2.y = 1 marks
        // living surface, so the capsules are the part of the plant that carries the night
        // glow, and the shader keeps their own vertex pigment instead of the leaf palette.
        for (int v = first; v < m.VertexCount; v++)
        {
            m.UV2[v * 2] = 1.35f;
            m.UV2[v * 2 + 1] = 1f;
        }

        if (!shed) return;

        // The shed capsule: the dark throat inside the case, and the ring of spilled spores
        // sitting on the rim.
        var tip = basePoint + a0 * length;
        EmbercupCapsuleMouth(m, tip, a0, dorsal, radius, sides, profile(1.0), length, mouth);
        EmbercupSporeRing(m, tip, a0, radius * profile(1.0) * 1.06, radius * 0.085, spore, rng);
    }

    /// <summary>
    /// The throat of an open capsule: a dark funnel dropping from the rim to a recessed floor.
    /// The rim is built on exactly the ring the capsule loft ended on, so the ember case and
    /// the dark inside meet without a seam.
    /// </summary>
    private static void EmbercupCapsuleMouth(MeshData m, Vec3 tip, Vec3 axis, Vec3 dorsal, double radius,
        int sides, double tipWidth, double length, double[] mouth)
    {
        var side0 = axis.Cross(dorsal).Normalized();
        double w = radius * tipWidth;
        double drop = length * 0.42;

        var rings = new Vec3[3][];
        for (int r = 0; r < 3; r++)
        {
            double shrink = r == 0 ? 1.0 : (r == 1 ? 0.78 : 0.42);
            double down = r == 0 ? 0.0 : (r == 1 ? 0.44 : 0.82);
            var ring = new Vec3[sides];
            for (int j = 0; j < sides; j++)
            {
                double th = 2 * Math.PI * j / sides;
                double cs = Math.Cos(th), sn = Math.Sin(th);
                double sx = Math.Sign(cs) * Math.Pow(Math.Abs(cs), 0.70);
                double sy = Math.Sign(sn) * Math.Pow(Math.Abs(sn), 1.55);
                ring[j] = tip + axis * (-drop * down) + side0 * (sx * w * shrink) + dorsal * (sy * w * shrink);
            }
            rings[r] = ring;
        }

        var ids = new int[3, sides];
        for (int r = 0; r < 3; r++)
            for (int j = 0; j < sides; j++)
                ids[r, j] = m.AddVertex(rings[r][j], axis, mouth, 1, r / 3.0, (double)j / sides, 0, 1);

        // The funnel walls face inward and up: the inside of the capsule is what the eye sees
        // when it looks into an open case.
        for (int r = 0; r < 2; r++)
            for (int j = 0; j < sides; j++)
            {
                var a = rings[r][j];
                var b = rings[r][(j + 1) % sides];
                var c = rings[r + 1][j];
                var d = rings[r + 1][(j + 1) % sides];
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-20) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-20) continue;
                n = n.Normalized();
                var inward = new Vec3(a.X + c.X, 0, a.Z + c.Z);
                var want = (inward.LengthSq > 1e-20 ? -inward.Normalized() : Vec3.Zero) + axis * 0.55;
                if (want.LengthSq > 1e-20 && n.Dot(want.Normalized()) < 0) n = -n;
                Primitives.TriangleFacing(m, ids[r, j], ids[r, (j + 1) % sides], ids[r + 1, j], n);
                Primitives.TriangleFacing(m, ids[r, (j + 1) % sides], ids[r + 1, (j + 1) % sides], ids[r + 1, j], n);
            }

        // The floor of the mouth, a little below the rim so the cavity reads as a hole.
        var floor = tip + axis * (-drop);
        int fc = m.AddVertex(floor, axis, mouth, 1, 1.0, 0.5, 0, 1);
        for (int j = 0; j < sides; j++)
        {
            var a = m.Position(fc);
            var b = rings[2][j];
            var c = rings[2][(j + 1) % sides];
            var n = (b - a).Cross(c - a);
            if (n.LengthSq < 1e-20) continue;
            n = n.Normalized();
            if (n.Dot(axis) < 0) n = -n;
            Primitives.TriangleFacing(m, fc, ids[2, j], ids[2, (j + 1) % sides], n);
        }
    }

    /// <summary>
    /// The ring of spilled spores on the rim of an open capsule: a bead of pale, dry
    /// granules. It is the one colour in the plant that is not alive, and it is what tells
    /// the eye that this capsule has already emptied.
    /// </summary>
    private static void EmbercupSporeRing(MeshData m, Vec3 centre, Vec3 axis, double ringRadius,
        double beadRadius, double[] spore, Rng rng)
    {
        const int beads = 18;
        var path = new Vec3[beads];
        var radii = new double[beads];
        var a0 = axis.Normalized();
        var u = new Vec3(-a0.Z, 0, a0.X);
        if (u.LengthSq < 1e-10) u = new Vec3(1, 0, 0);
        u = u.Normalized();
        var v = a0.Cross(u).Normalized();
        for (int i = 0; i < beads; i++)
        {
            double th = 2 * Math.PI * i / beads;
            double rr = ringRadius * (1.0 + 0.07 * Math.Sin(th * 5.0 + 0.7));
            path[i] = centre + u * (Math.Cos(th) * rr) + v * (Math.Sin(th) * rr);
            radii[i] = beadRadius * rng.Range(0.72, 1.35);
        }
        Primitives.Tube(m, path, radii, 5, (i, vv) => (spore, 1, (double)i / beads, vv, 0, 0));
    }

    // ---------------------------------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The embercup: a low, lumpy cushion of olive shoots with a scatter of long russet setae
    /// standing clear of it, each carrying an ember-coloured capsule.
    /// </summary>
    private static void Embercup(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The content file gives the species its two colours: c1 is the olive of the cushion,
        // c2 the ember of the capsules. Everything below is the species' own working palette
        // with those two mixed in.
        var olive = Primitives.Mix(EmbercupOlive, c1, 0.28);
        var olivePale = Primitives.Mix(EmbercupOlivePale, c1, 0.24);
        var gold = Primitives.Mix(EmbercupGold, c2, 0.16);
        var ember = Primitives.Mix(EmbercupEmber, c2, 0.55);
        var emberDeep = Primitives.Mix(EmbercupEmberDeep, c2, 0.18);
        var emberHot = Primitives.Mix(EmbercupEmberHot, c2, 0.22);

        // The proportions: a cushion that is low and wide, and setae that stand well clear of
        // it. The plant is 0.19 m across and 0.16 m tall.
        double cushionRadius = radius * 0.455;   // 0.086 m radius: the cushion is 0.17 m across
        double cushionHeight = height * 0.162;   // 0.026 m tall: a cushion, not a mound

        // 1. The mat. It carries the mass of the plant and closes the silhouette, so the
        // shoots read as growing out of moss rather than out of a bare patch of ground. It is
        // much darker than the shoots, because the inside of a cushion moss sits in its own
        // shadow and the colour of the plant is really the colour of its nap.
        EmbercupCushionBase(part, cushionRadius, cushionHeight, EmbercupMat, Primitives.Scale(olive, 0.72));

        // 2. The shoots. They are packed over the whole cushion on a Vogel spiral, so the
        // cushion has no rings and no rows - a moss cushion never has either.
        int stemCount = NewFloraCount(part, 205);
        const double golden = 2.399963229728653;
        for (int i = 0; i < stemCount; i++)
        {
            double f = (i + 0.5) / stemCount;
            double th = i * golden + rng.Range(-0.14, 0.14);
            double rim = cushionRadius * EmbercupPlanFactor(th);
            double rr = Math.Min(cushionRadius * Math.Sqrt(f) * rng.Range(0.90, 1.08), rim * 0.985);
            double ground = EmbercupDomeHeight(rr, th, cushionRadius, cushionHeight);

            // The shoot leans more at the rim of the cushion: the edge of a moss cushion
            // splays outwards and the outer shoots lie over the edge, the way a cushion that
            // has been sat on does.
            double lean = rng.Range(0.04, 0.15) + 0.78 * Math.Pow(f, 2.1);
            double leanAz = th + rng.Range(-0.75, 0.75);
            var outward = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));
            var axis = (Vec3.Up * Math.Cos(lean) + outward * Math.Sin(lean)).Normalized();

            // The shoots stand to different heights, the way the shoots of a real cushion do:
            // not taller in the middle, but taller where the mat has knuckled up. A shoot is
            // only a centimetre or so long, which is what keeps its millimetre leaves in
            // proportion - a long bare stem with two blades on it is not a moss.
            double knuckle = Math.Clamp(0.5 + 0.62 * EmbercupKnuckle(th, Math.Sqrt(f)), 0.0, 1.0);
            double shoot = rng.Range(0.0050, 0.0105) * (0.84 + 0.32 * knuckle);

            var root = new Vec3(Math.Cos(th) * rr, ground - 0.0013, Math.Sin(th) * rr);
            EmbercupMicroStem(part, root, axis, shoot, leanAz, rng, olive, olivePale, gold);
        }

        // 3. The sporophytes. A moss seta is a bare hair that lifts the capsule clear of the
        // cushion so the spores can be shed into the wind; each one leans at its own angle.
        int setaCount = NewFloraCount(part, 9);
        int shedIndex = setaCount / 2;
        for (int i = 0; i < setaCount; i++)
        {
            // One capsule has shed its lid. It is given the tallest, straightest seta of the
            // lot, because the open mouth is the detail that has to be legible.
            bool shed = i == shedIndex;
            double az = rng.Range(0, 2 * Math.PI);
            // The sporophytes come out of the crown of the cushion, not out of its skirt: the
            // growing point of a moss sits at the top of the mound, and a seta that rose from
            // the edge would drag its capsule through the shoots.
            double rr = rng.Range(0.003, 0.034);
            double ground = EmbercupDomeHeight(rr, az, cushionRadius, cushionHeight);
            // The foot is buried in the shoots that crown the cushion, so the seta appears to
            // come out of the moss rather than out of the soil.
            double footY = ground + 0.0095 - 0.0035;
            var foot = new Vec3(Math.Cos(az) * rr, footY, Math.Sin(az) * rr);

            double setaLen = shed ? rng.Range(0.100, 0.110) : rng.Range(0.064, 0.108);
            double lean = shed ? rng.Range(0.03, 0.14) : rng.Range(0.05, 0.34);
            double leanAz = az + rng.Range(-1.2, 1.2);
            var outward = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));
            var tip = foot + (Vec3.Up * Math.Cos(lean) + outward * Math.Sin(lean)) * setaLen;

            // The seta bows very slightly back as it rises, the way a seta that has stood up
            // in rain and dried again keeps a curve it never quite loses.
            var bow = -outward * (setaLen * 0.05);
            NewFloraStalk(part, foot, tip, bow, 1.0, 0.00030, 0.00017,
                Primitives.Scale(EmbercupSeta, 0.86), EmbercupSetaTip, segments: 6, sides: 6, taper: 0.9);

            // The capsule sits on the end of the seta, pointing along the seta's last tangent.
            var axis = (tip - foot - bow * Math.PI).Normalized();
            double capLen = rng.Range(0.0058, 0.0086);
            double capRad = capLen * rng.Range(0.155, 0.195);
            EmbercupCapsule(part, tip - axis * 0.0004, axis, capLen, capRad, shed, rng,
                ember, emberDeep, emberHot, EmbercupMouth, EmbercupSpore);
        }

        // The capsules are the plant's identity, and they are the one organ that has to
        // survive the mid distance tier. Ask the LOD tierer for the crown-first rule, the
        // same one the splitcrown and the lacemantle use, so the sporophytes are kept whole
        // while the interior of the cushion thins first.
        m.PreserveUpperCanopyOnLod = true;

        NewFloraAppendWorld(m, part, radius, height);
    }
}
