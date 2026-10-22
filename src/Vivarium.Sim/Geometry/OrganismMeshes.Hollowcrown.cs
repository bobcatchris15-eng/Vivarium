using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Hollowcrown - the veteran shade tree, hollowed and pollarded.
/// Analogue: an old oak or elm that has been hollowed out and cut back: a furrowed, buttressed
/// bole, a cavity standing open like a door, and a few blunt cut limbs throwing a tight crown of
/// epicormic shoots.
///
/// A veteran tree is a tree that has been through things. Its trunk is not a clean cylinder: it
/// is buttressed where the roots have had to hold it up, fluted down its length where the bark
/// has weathered into deep furrows, and hollow where something has got into it. Its crown is
/// not a canopy either: it was cut back, and it has answered by throwing straight up from the
/// cuts, which is what a tree does when it has lost its top.
///
/// The fantasy layer is the hollow. The cavity is lined with MOSS THAT GLOWS, so the tree keeps
/// its own dim light inside itself, and the doorway is the only place in the wood where anything
/// shines. The moss is flagged through UV2 so the shader's marked-nodule band lights it at night.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The grey-brown bark.</summary>
    private static readonly double[] HollowBark = { 0.32, 0.30, 0.28 };
    /// <summary>The deep shadow in the furrows.</summary>
    private static readonly double[] HollowBarkDark = { 0.15, 0.14, 0.13 };
    /// <summary>The weathered, lichen-mottled bark on the old side of the trunk.</summary>
    private static readonly double[] HollowBarkPale = { 0.54, 0.53, 0.50 };
    /// <summary>The raw wood of a cut face and of the inside of the cavity.</summary>
    private static readonly double[] HollowWood = { 0.52, 0.40, 0.27 };
    /// <summary>The moss that lines the cavity.</summary>
    private static readonly double[] HollowMoss = { 0.32, 0.60, 0.36 };
    /// <summary>The lit moss: the only light inside the tree.</summary>
    private static readonly double[] HollowMossLit = { 0.46, 0.98, 0.56 };
    /// <summary>The small leaves of the epicormic shoots.</summary>
    private static readonly double[] HollowLeaf = { 0.24, 0.40, 0.20 };
    private static readonly double[] HollowLeafPale = { 0.48, 0.62, 0.30 };

    /// <summary>
    /// The buttress: a vertical fin running from the trunk out onto the ground. A veteran tree
    /// does not stand on a cylinder; it stands on a set of flanges that spread its load into the
    /// soil, and the flanges are what make an old bole look like an old bole.
    /// </summary>
    private static void HollowcrownButtress(MeshData m, Vec3 root, Vec3 dir, double length, double height, Rng rng)
    {
        const int stations = 7;
        const int sides = 5;
        var path = new Vec3[stations];
        var heights = new double[stations];
        var widths = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            // The buttress runs out and down, and its crest falls away as it goes.
            path[i] = root + dir * (length * t) + Vec3.Up * (-height * 0.55 * t * t);
            heights[i] = height * (1.0 - 0.72 * t) * (0.6 + 0.4 * Math.Sin(Math.PI * Math.Min(1.0, t * 1.2)));
            widths[i] = length * 0.26 * (1.0 - 0.50 * t);
        }

        int start = m.VertexCount;
        for (int i = 0; i < stations; i++)
        {
            var along = (i == 0) ? path[1] - path[0] : path[i] - path[i - 1];
            if (along.LengthSq < 1e-12) along = dir;
            along = along.Normalized();
            var side = along.Cross(Vec3.Up);
            if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
            side = side.Normalized();
            for (int j = 0; j <= sides; j++)
            {
                // The fin section: a rounded triangle standing on the ground.
                double th = Math.PI * j / sides;
                double sx = Math.Cos(th);
                double sy = Math.Sin(th);
                var p = path[i] + side * (sx * widths[i]) + Vec3.Up * (sy * sy * heights[i]);
                // The bark is weathered pale on the weather side and dark in the crease.
                var col = Primitives.Mix(HollowBark, HollowBarkPale, 0.25 * (0.5 + 0.5 * sy));
                col = Primitives.Mix(col, HollowBarkDark, 0.45 * Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(sx)), 3.0));
                m.AddVertex(p, Vec3.Up, col, 1, i / (double)(stations - 1), j / (double)sides, 0, 0);
            }
        }
        int row = sides + 1;
        for (int i = 0; i < stations - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = start + i * row + j, b = a + row;
                Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, Vec3.Up);
            }
    }

    /// <summary>
    /// The bole: a fluted, buttressed, hollow trunk.
    ///
    /// The trunk is a surface of revolution with three things done to it. Its radius swells at the
    /// foot and again at the shoulder, because a tree that has stood a long time grows girth at
    /// both ends. Its surface is cut by deep longitudinal furrows, because old bark does not
    /// stay smooth. And it is open at the base, because something has hollowed it out, so the
    /// trunk is built over a reduced arc near the ground and the cavity behind the opening is
    /// modelled as a real inside wall.
    /// </summary>
    private static void HollowcrownBole(MeshData m, Vec3 basePoint, double height, double baseRadius, Rng rng)
    {
        const int rings = 18;
        const int sectors = 28;
        // The doorway faces -X.
        const double openDir = Math.PI;
        const double gapMax = 0.46;

        int buttressCount = 5 + rng.NextInt(3);
        var buttressAngles = new double[buttressCount];
        for (int i = 0; i < buttressCount; i++)
            buttressAngles[i] = 2 * Math.PI * i / buttressCount + rng.Range(-0.2, 0.2);
        double furrowPhase = rng.Range(0, 6.28);
        double barkPhase = rng.Range(0, 6.28);

        // The trunk's radius at a given height and bearing.
        double Radius(double t, double th)
        {
            double r = baseRadius * (0.88 + 1.05 * Math.Exp(-5.5 * t) + 0.42 * MathD.SmoothStep(0.60, 1.0, t));
            // The deep fluting: seven flutes running the length of the trunk, fading with height.
            r *= 1.0 + 0.105 * Math.Sin(7.0 * th + furrowPhase) * (0.35 + 0.65 * (1.0 - t));
            // A finer, irregular bark scale on top of the fluting.
            r *= 1.0 + 0.024 * Math.Sin(19.0 * th + barkPhase + 2.0 * Math.Sin(3.0 * th));
            // The buttress flanges, strong at the foot and gone by a third of the way up.
            foreach (double ba in buttressAngles)
            {
                double lobe = Math.Pow(Math.Max(0.0, Math.Cos(th - ba)), 7.0);
                r *= 1.0 + 0.34 * lobe * Math.Exp(-6.5 * t);
            }
            return r;
        }

        // The outer shell. Below the shoulder of the hollow the trunk is open over an arc.
        var outer = new int[rings + 1, sectors + 1];
        var outerNormals = new Vec3[rings + 1, sectors + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            double gap = gapMax * (1.0 - MathD.SmoothStep(0.20, 0.46, t));
            double start = openDir + gap;
            double sweep = 2 * Math.PI - 2 * gap;
            for (int j = 0; j <= sectors; j++)
            {
                double th = start + sweep * (j / (double)sectors);
                double r = Radius(t, th);
                var p = basePoint + Vec3.Up * (height * t) + new Vec3(Math.Cos(th) * r, 0, Math.Sin(th) * r);
                // The bark is pale and lichen-mottled on the weather side, and the deep furrows
                // between the flutes hold their own shadow.
                double weather = 0.5 + 0.5 * Math.Cos(th - barkPhase);
                var col = Primitives.Mix(HollowBark, HollowBarkPale, 0.34 * weather * (0.4 + 0.6 * t));
                col = Primitives.Mix(col, HollowBarkDark, 0.60 * Math.Pow(Math.Max(0.0, -Math.Sin(7.0 * th + furrowPhase)), 1.6));
                // The bark's UV.x runs the normalised height, not the metres: the shader's stem
                // branch reads it as a position along the shaft, and a metre-scale value would
                // put a node scar every half metre instead of a handful over the whole trunk.
                outer[i, j] = m.AddVertex(p, Vec3.Up, col, 1, t, j / (double)sectors, 0, 0);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                var a = m.Position(outer[i, j]);
                var b = m.Position(outer[i, j + 1]);
                var c = m.Position(outer[i + 1, j]);
                var d = m.Position(outer[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                outerNormals[i, j] += n;
                outerNormals[i, j + 1] += n;
                outerNormals[i + 1, j] += n;
                outerNormals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, outer[i, j], outer[i + 1, j], outer[i, j + 1], n);
                Primitives.TriangleFacing(m, outer[i, j + 1], outer[i + 1, j], outer[i + 1, j + 1], n);
            }
        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = outerNormals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = outer[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // The top of the trunk is a broken stump, and it has to be closed: a tube left open at
        // the top shows the inside of the tree from above, which is exactly the kind of gap the
        // eye picks out first.
        {
            var capCentre = basePoint + Vec3.Up * (height * 0.985);
            int cc = m.AddVertex(capCentre, Vec3.Up, Primitives.Mix(HollowWood, HollowBarkDark, 0.45), 1, 1.0, 0.5, 0, 0);
            for (int j = 0; j < sectors; j++)
            {
                var p0 = m.Position(outer[rings, j]);
                var p1 = m.Position(outer[rings, j + 1]);
                // The stump is not a clean circle: it is broken, so the rim dips a little.
                double dip = 0.012 * height * Math.Sin(j * 0.9);
                var cap = m.AddVertex(basePoint + new Vec3(p0.X, height * 0.985 - dip, p0.Z), Vec3.Up,
                    Primitives.Mix(HollowWood, HollowBark, 0.35), 1, 1.0, j / (double)sectors, 0, 0);
                var cap2 = m.AddVertex(basePoint + new Vec3(p1.X, height * 0.985 - dip, p1.Z), Vec3.Up,
                    Primitives.Mix(HollowWood, HollowBark, 0.35), 1, 1.0, (j + 1) / (double)sectors, 0, 0);
                Primitives.TriangleFacing(m, cc, cap, cap2, Vec3.Up);
            }
        }

        // The inside of the cavity: a full inner wall, seen through the doorway, with moss on it.
        const int innerRings = 7;
        int mossMark = m.VertexCount;
        var inner = new int[innerRings + 1, sectors + 1];
        for (int i = 0; i <= innerRings; i++)
        {
            double t = (i / (double)innerRings) * 0.52;
            for (int j = 0; j <= sectors; j++)
            {
                double th = 2 * Math.PI * j / sectors;
                double r = Radius(t, th) * 0.80;
                var p = basePoint + Vec3.Up * (height * t) + new Vec3(Math.Cos(th) * r, 0, Math.Sin(th) * r);
                // Moss grows where the damp is: low down, and in patches.
                double patch = Math.Pow(Math.Max(0.0, Math.Sin(th * 3.0 + 1.1) * Math.Sin(t * 7.0 + 0.6)), 2.0);
                var col = Primitives.Mix(HollowWood, HollowMoss, Math.Min(1.0, patch * 1.5));
                inner[i, j] = m.AddVertex(p, -Vec3.Up, col, 1, t, j / (double)sectors, 0, 0);
            }
        }
        for (int i = 0; i < innerRings; i++)
            for (int j = 0; j < sectors; j++)
            {
                var a = m.Position(inner[i, j]);
                var b = m.Position(inner[i, j + 1]);
                var c = m.Position(inner[i + 1, j]);
                var d = m.Position(inner[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                Primitives.TriangleFacing(m, inner[i, j], inner[i, j + 1], inner[i + 1, j], n);
                Primitives.TriangleFacing(m, inner[i, j + 1], inner[i + 1, j + 1], inner[i + 1, j], n);
            }
        // The moss is the one lit part of the tree.
        NewFloraMarkOrgan(m, mossMark, m.VertexCount - mossMark);

        // The floor of the cavity, and the thickness of the trunk at the two broken edges of the
        // doorway, so the opening has a rim you can see the grain of.
        {
            var floorCentre = m.AddVertex(basePoint + Vec3.Up * (height * 0.02), Vec3.Up,
                Primitives.Mix(HollowWood, HollowBarkDark, 0.6), 1, 0, 0.5, 0, 0);
            for (int j = 0; j < sectors; j++)
                Primitives.TriangleFacing(m, floorCentre, inner[0, j], inner[0, j + 1], Vec3.Up);

            for (int edge = 0; edge < 2; edge++)
            {
                int j = edge == 0 ? 0 : sectors;
                for (int i = 0; i < innerRings; i++)
                {
                    var p0 = m.Position(outer[i, j]);
                    var p1 = m.Position(outer[i + 1, j]);
                    var p2 = m.Position(inner[i, j]);
                    var p3 = m.Position(inner[i + 1, j]);
                    var n = (p1 - p0).Cross(p2 - p0);
                    if (n.LengthSq < 1e-18) continue;
                    n = n.Normalized();
                    Primitives.TriangleFacing(m, outer[i, j], inner[i, j], outer[i + 1, j], n);
                    Primitives.TriangleFacing(m, outer[i + 1, j], inner[i, j], inner[i + 1, j], n);
                }
            }
        }
    }

    /// <summary>
    /// The crown: a few blunt, cut-back limbs, each one throwing a dense burst of epicormic
    /// shoots from its cut face.
    /// </summary>
    private static void HollowcrownCrown(MeshData m, Vec3 basePoint, double height, double baseRadius, Rng rng)
    {
        int limbs = 5 + rng.NextInt(2);
        for (int i = 0; i < limbs; i++)
        {
            double a = 2 * Math.PI * i / limbs + rng.Range(-0.25, 0.25);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            // The limbs come out of the shoulder of the trunk, not out of the top.
            var root = basePoint + Vec3.Up * (height * rng.Range(0.66, 0.84))
                + outward * (baseRadius * rng.Range(0.35, 0.7));
            double len = height * rng.Range(0.11, 0.18);
            var dir = (outward * rng.Range(0.22, 0.42) + Vec3.Up * rng.Range(0.85, 1.05)).Normalized();

            const int nodes = 6;
            var path = new Vec3[nodes + 1];
            var radii = new double[nodes + 1];
            var p = root;
            for (int k = 0; k <= nodes; k++)
            {
                double t = k / (double)nodes;
                path[k] = (k == 0) ? root : p + dir * (len / nodes) + Vec3.Up * (0.02 * len * Math.Sin(t * 2.0));
                p = path[k];
                radii[k] = baseRadius * 0.40 * (1.0 - 0.42 * t);
            }
            Primitives.Tube(m, path, radii, 9, (k, v) => (Primitives.Mix(HollowBark, HollowBarkPale, 0.18), 1, k / (double)nodes, v, 0, 0));

            // The cut face: a flat sawn disc, lighter and rougher than the bark.
            var top = path[nodes];
            var faceNormal = (top - path[nodes - 1]).Normalized();
            var faceSide = faceNormal.Cross(Vec3.Up);
            if (faceSide.LengthSq < 1e-10) faceSide = new Vec3(1, 0, 0);
            faceSide = faceSide.Normalized();
            var faceUp = faceSide.Cross(faceNormal).Normalized();
            int centre = m.AddVertex(top, faceNormal, Primitives.Mix(HollowWood, HollowBarkPale, 0.35), 1, 0.5, 0.5, 0, 0);
            var rim = new int[13];
            for (int k = 0; k <= 12; k++)
            {
                double th = 2 * Math.PI * k / 12;
                var p2 = top + faceSide * (Math.Cos(th) * radii[nodes]) + faceUp * (Math.Sin(th) * radii[nodes]);
                rim[k] = m.AddVertex(p2, faceNormal, Primitives.Mix(HollowWood, HollowBark, 0.45), 1, 0.5 + 0.5 * Math.Cos(th), 0.5 + 0.5 * Math.Sin(th), 0, 0);
            }
            for (int k = 0; k < 12; k++)
                Primitives.TriangleFacing(m, centre, rim[k], rim[k + 1], faceNormal);

            // The epicormic shoots. A cut limb answers a lost crown by throwing straight up from
            // the wound, so these rise rather than arch, and they crowd the cut.
            int shoots = NewFloraCount(m, 10);
            for (int s = 0; s < shoots; s++)
            {
                double sa = rng.Range(0, 2 * Math.PI);
                var sOut = (faceSide * Math.Cos(sa) + faceUp * Math.Sin(sa)).Normalized();
                var from = top + sOut * (radii[nodes] * rng.Range(0.2, 0.8));
                double slen = height * rng.Range(0.10, 0.20);
                var sdir = (sOut * rng.Range(0.2, 0.55) + faceNormal * rng.Range(0.8, 1.0)).Normalized();

                const int sn = 5;
                var spath = new Vec3[sn + 1];
                var sradii = new double[sn + 1];
                var sp = from;
                for (int k = 0; k <= sn; k++)
                {
                    double t = k / (double)sn;
                    spath[k] = (k == 0) ? from : sp + (sdir + sOut * (0.12 * Math.Sin(t * 4.0))).Normalized() * (slen / sn);
                    sp = spath[k];
                    sradii[k] = baseRadius * 0.075 * (1.0 - 0.62 * t) + 0.0015;
                }
                Primitives.Tube(m, spath, sradii, 6, (k, v) => (Primitives.Mix(HollowBarkPale, HollowLeaf, 0.35), 1, k / (double)sn, v, 0, 0));

                // The leaves: small, ovate and finely toothed, crowded along the shoot.
                int leaves = NewFloraCount(m, 14);
                for (int l = 0; l < leaves; l++)
                {
                    double t = 0.14 + 0.84 * (l / (double)Math.Max(1, leaves - 1));
                    int k = Math.Clamp((int)(t * sn), 0, sn);
                    var at = spath[k];
                    double la = rng.Range(0, 2 * Math.PI);
                    var lOut = (faceSide * Math.Cos(la) + faceUp * Math.Sin(la)).Normalized();
                    var lDir = (lOut * 0.75 + faceNormal * rng.Range(0.2, 0.6)).Normalized();
                    NewFloraBlade(m, at, lDir, faceNormal,
                        height * rng.Range(0.019, 0.032), height * rng.Range(0.009, 0.015),
                        // A small ovate leaf with a drawn-out point.
                        u => Math.Max(0.04, Math.Sin(Math.PI * Math.Min(1.0, 0.10 + 0.90 * u)) * (1.0 - 0.12 * u)),
                        u => Primitives.Mix(HollowLeaf, HollowLeafPale, 0.30 * u),
                        rows: 4, sides: 6, flatness: 0.42, keel: 0.16, cup: 0.16, camber: 0.14,
                        serration: 0.16, teeth: 9, midrib: HollowLeafPale, midribWidth: 0.18);
                    // The leaves are ordinary foliage and are left unmarked on purpose: the marked
                    // band is the one the night glow picks up, and only the moss inside the cavity
                    // is meant to shine.
                }
            }
        }
    }

    /// <summary>The veteran: buttressed, furrowed, hollow, and pollarded into epicormic shoots.</summary>
    private static void Hollowcrown(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        double boleHeight = height * (juvenile ? 0.78 : 1.0);
        double baseRadius = radius * (juvenile ? 0.12 : 0.235);

        // The buttresses.
        int buttresses = 6 + rng.NextInt(3);
        for (int i = 0; i < buttresses; i++)
        {
            double a = 2 * Math.PI * i / buttresses + rng.Range(-0.15, 0.15);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            HollowcrownButtress(part, dir * (baseRadius * 0.55), dir,
                baseRadius * rng.Range(2.0, 3.2), baseRadius * rng.Range(1.9, 2.9), rng);
        }

        // The surface roots: sinuous, running out over the ground from the foot of the flanges.
        int roots = 9 + rng.NextInt(5);
        for (int i = 0; i < roots; i++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var from = dir * (baseRadius * rng.Range(0.8, 1.6)) + Vec3.Up * (baseRadius * 0.06);
            var to = from + dir * (baseRadius * rng.Range(1.2, 2.6)) + new Vec3(rng.Range(-0.2, 0.2), 0, rng.Range(-0.2, 0.2)) * baseRadius;
            var mid = Vec3.Lerp(from, to, 0.5) + new Vec3(rng.Range(-0.3, 0.3), 0, rng.Range(-0.3, 0.3)) * baseRadius;
            Primitives.Tube(part, new[] { from, mid, to },
                new[] { baseRadius * 0.16, baseRadius * 0.10, baseRadius * 0.03 }, 6,
                (k, v) => (Primitives.Mix(HollowBark, HollowBarkDark, 0.3), 1, k * 0.5, v, 0, 0));
        }

        // The bole, and then the crown on its shoulder.
        HollowcrownBole(part, Vec3.Up * (baseRadius * 0.08), boleHeight, baseRadius, rng);
        HollowcrownCrown(part, Vec3.Up * (baseRadius * 0.08), boleHeight, baseRadius, rng);

        NewFloraAppendWorld(m, part, radius, height);
    }
}
