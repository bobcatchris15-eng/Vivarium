using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Ice-plant (Aizoaceae) groundcover for the 'glassfinger' species.
///
/// The plant is authored in real-world metres and converted into the engine's mesh frame by
/// <see cref="AppendGlassWorld"/>, so what is built here is what the player sees.
///
/// Anatomy follows the family rather than any single species:
///
///   * prostrate, sprawling, repeatedly-branching shoots that hug the gravel and only
///     rise a few millimetres off it;
///   * opposite and decussate leaves - every node rotates the pair 90 degrees from the
///     node below, which is how a real ice plant avoids stacking its blades in one plane;
///   * succulent leaves that are triangular in cross-section with a keel underneath, sheathed
///     at the base where they clasp the stem, waisted above the sheath and widest about a
///     quarter of the way along before tapering to an acute point;
///   * nodal swellings on the shoots, so a stem reads as a beaded succulent axis rather
///     than an extruded wire;
///   * the family's epidermal bladder cells, the vesicles that give the ice plants their
///     name - here they are grown as small faceted crystals over the dorsal blade.
///
/// The fantastical element is the colour of those cells: the family's clear vesicles are
/// rendered as a saturated azure frost that spreads over the distal half of every leaf and
/// runs the full length of the keel, while the anthocyanin flush the real plant shows at
/// its tips is kept, and now sits over the blue as a violet-red. The plant still reads as
/// a drought-hardened ice plant; it just also looks frozen from the middle out.
///
/// Reference renders: scripts/visuals/glassfinger/iceplant_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    // The azure the bladder crystals carry. Saturated, cool, and much stronger than the
    // foliage around it, so the blue reads as a deliberate detail rather than a tint.
    private static readonly double[] GlassCrystal = { 0.10, 0.48, 1.00 };
    private static readonly double[] GlassCrystalDeep = { 0.03, 0.16, 0.70 };
    // The glaucous green of the leaf body: a real ice plant is far from a saturated green.
    private static readonly double[] GlassGlaucous = { 0.30, 0.47, 0.35 };
    private static readonly double[] GlassGlaucousPale = { 0.46, 0.60, 0.46 };

    /// <summary>Cross-section phase that puts one lobe up (the dorsal ridge) and two below (the flanks).</summary>
    private const double GlassKeelPhase = Math.PI / 2.0;

    /// <summary>Radius profile of a leaf blade, as a fraction of its half width.</summary>
    private static double GlassLeafProfile(double t)
    {
        if (t < 0.06) return 0.52 + 0.66 * (t / 0.06);            // sheath swelling over the stem
        if (t < 0.15) return 1.18 - 0.40 * ((t - 0.06) / 0.09);    // the waist above the sheath
        if (t < 0.27) return 0.78 + 0.22 * ((t - 0.15) / 0.12);    // the blade opens out
        double u = (t - 0.27) / 0.73;
        return Math.Max(0.035, 1.0 - Math.Pow(u, 0.88));           // long taper to the point
    }

    /// <summary>Along-blade pigment: green body, azure crystal band, anthocyanin tip.</summary>
    private static double[] GlassLeafColour(double t, double[] green, double[] red, double variance)
    {
        // Base and waist stay green; the crystal frost creeps in over the distal half.
        double crystal = MathD.SmoothStep(0.32, 0.68, t);
        // The anthocyanin flush of the real plant is confined to the last stretch of blade.
        double flush = MathD.SmoothStep(0.86, 1.0, t);
        var col = Primitives.Mix(green, GlassCrystal, crystal * 0.96);
        // Violet-red over azure, so the tip keeps the species' red identity.
        col = Primitives.Mix(col, red, flush * 0.9);
        // A little per-leaf value drift so no two blades are identical.
        return Primitives.Scale(col, 1.0 + variance);
    }

    /// <summary>
    /// A tube whose cross-section is a rounded triangle rather than a circle. Real ice-plant
    /// stems and leaves are distinctly three-angled, and that faceting is most of what makes
    /// the plant read as succulent rather than as a bundle of pipes. Frames are parallel
    /// transported so the facets do not corkscrew through a bend.
    /// </summary>
    private static void GlassLobedTube(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radius,
        int sides, double lobe, double lobePhase,
        Func<int, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr)
    {
        if (path.Count < 2 || radius.Count != path.Count) return;
        sides = Math.Max(6, sides);
        var src = path; var srcR = radius;
        int[]? rows = null;
        if (m.Lod is { } lod)
        {
            sides = lod.Sides(sides);
            int keep = Math.Max(2, (int)Math.Round((path.Count - 1) * lod.Length) + 1);
            if (keep < path.Count)
            {
                rows = new int[keep];
                for (int k = 0; k < keep; k++) rows[k] = (int)Math.Round(k * (path.Count - 1) / (double)(keep - 1));
                var p2 = new Vec3[keep]; var r2 = new double[keep];
                for (int k = 0; k < keep; k++) { p2[k] = path[rows[k]]; r2[k] = radius[rows[k]]; }
                path = p2; radius = r2;
            }
        }
        var authored = attr;
        if (rows != null) { var map = rows; attr = (i, v) => authored(map[i], v); }

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
        if (side.LengthSq < 1e-8) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i < path.Count; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-8)
                {
                    side = dir.Cross(hint);
                    if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up2 = side.Cross(dir).Normalized();

            double drds = 0;
            if (i == 0)
            {
                double ds = (path[1] - path[0]).Length;
                if (ds > 1e-9) drds = (radius[1] - radius[0]) / ds;
            }
            else if (i == path.Count - 1)
            {
                double ds = (path[^1] - path[^2]).Length;
                if (ds > 1e-9) drds = (radius[^1] - radius[^2]) / ds;
            }
            else
            {
                double ds = (path[i + 1] - path[i - 1]).Length;
                if (ds > 1e-9) drds = (radius[i + 1] - radius[i - 1]) / ds;
            }

            for (int s = 0; s <= sides; s++)
            {
                double th = 2 * Math.PI * s / sides;
                // The three-fold harmonic is what turns the circle into a rounded wedge.
                double r = radius[i] * (1.0 + lobe * Math.Cos(3.0 * (th - lobePhase)));
                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var normal = (radial - dir * drds).Normalized();
                var a = attr(i, (double)s / sides);
                m.AddVertex(path[i] + radial * r, normal, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s, b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        double maxR = 0;
        for (int i = 0; i < radius.Count; i++) maxR = Math.Max(maxR, Math.Abs(radius[i]));
        m.Structural.Add((start, m.VertexCount - start, sides, maxR));
    }

    /// <summary>
    /// One ice-plant leaf: a keeled, succulent blade sheathed onto the stem, with a scatter of
    /// faceted crystal cells over its dorsal face.
    /// </summary>
    private static void GlassLeaf(MeshData m, Vec3 basePoint, Vec3 along, Vec3 across, Vec3 dorsal,
        double length, double halfWidth, double lobe, int rows, int sides, ulong seed,
        double[] green, double[] red, double variance, int cells)
    {
        var rng = Rng.Keyed(seed, "glassfinger.leaf", 0);
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;

        double phase = GlassKeelPhase + rng.Range(-0.13, 0.13);
        double twist = rng.Range(-0.10, 0.10);
        double arcUp = rng.Range(0.05, 0.11);
        double tipDrop = rng.Range(0.03, 0.08);
        // A gentle sideways bow, so no two blades leave the node on a dead straight line.
        double bow = rng.Range(-0.16, 0.16);

        Vec3 Centre(double t)
        {
            double lift = Math.Sin(t * Math.PI * 0.88) * arcUp - Math.Pow(Math.Max(0.0, t - 0.70) / 0.30, 2.0) * tipDrop;
            double side = Math.Sin(t * Math.PI) * bow * length;
            return basePoint + along * (t * length) + dorsal * (lift * length) + across * side;
        }

        // The section is a rounded triangle with a raised dorsal ridge and two lower corners,
        // so the blade is a tent: a median keel above, a keel below, and two flanks that
        // catch the light differently. The ring starts at the ventral keel and runs round,
        // which puts the ridge at UV.y = 0.5 and the keel at the seam.
        //
        // The blade is one shared vertex grid, not a stack of separate quads: a fleshy leaf
        // shades as a continuous surface, and per-quad normals turn it into folded paper.
        var rings = new Vec3[rows + 1][];
        var grid = new int[rows + 1, sides + 1];
        var normals = new Vec3[rows + 1, sides + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var centre = Centre(t);
            // The widest lateral reach of the section is 1.16 of its base radius, so the base
            // radius is scaled down to keep halfWidth honest.
            double r0 = halfWidth * GlassLeafProfile(t) / 1.16;
            var ring = new Vec3[sides + 1];
            for (int j = 0; j <= sides; j++)
            {
                double th = -Math.PI * 0.5 + 2 * Math.PI * j / sides;
                // A little irregularity in the section keeps the blade from looking machined.
                double wobble = 1.0 + 0.045 * Math.Sin(t * 15.0 + j * 2.3 + phase * 3.0)
                    * Math.Sin(Math.PI * Math.Min(1.0, t));
                double r = r0 * wobble * (1.0 + lobe * Math.Cos(3.0 * (th - phase)));
                // The blade is deeper than it is wide, the way a fleshy ice-plant leaf is.
                double vs = Math.Sin(th) >= 0.0 ? 1.24 : 0.86;
                // A slow twist down the blade stops the facets from running dead straight.
                double tw = twist * t;
                var a2 = across * Math.Cos(tw) + dorsal * Math.Sin(tw);
                var d2 = dorsal * Math.Cos(tw) - across * Math.Sin(tw);
                ring[j] = centre + a2 * (r * Math.Cos(th)) + d2 * (r * Math.Sin(th) * vs);
            }
            rings[i] = ring;
            for (int j = 0; j <= sides; j++)
            {
                grid[i, j] = m.AddVertex(ring[j], Vec3.Up, GlassLeafColour(t, green, red, variance), 1,
                    t, j / (double)sides, 0, 1);
            }
        }

        // Faces, and the smooth normals that go with them.
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                var a = rings[i][j];
                var b = rings[i][j + 1];
                var c = rings[i + 1][j];
                var d = rings[i + 1][j + 1];
                var normal = (b - a).Cross(c - a);
                if (normal.LengthSq < 1e-18) normal = (d - a).Cross(c - a);
                if (normal.LengthSq < 1e-18) continue;
                normal = normal.Normalized();
                normals[i, j] += normal;
                normals[i, j + 1] += normal;
                normals[i + 1, j] += normal;
                normals[i + 1, j + 1] += normal;
                Primitives.TriangleFacing(m, grid[i, j], grid[i, j + 1], grid[i + 1, j], normal);
                Primitives.TriangleFacing(m, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], normal);
            }
        }
        for (int i = 0; i <= rows; i++)
        {
            for (int j = 0; j <= sides; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }
        }

        // Cap the base with a small fan, set back inside the stem so the join never shows.
        var capCentre = Centre(0.0);
        int capIdx = m.AddVertex(capCentre, -along, GlassLeafColour(0, green, red, variance), 1, 0.0, 0.5, 0, 1);
        for (int j = 0; j < sides; j++)
        {
            var n0 = (rings[0][j + 1] - capCentre).Cross(rings[0][j] - capCentre);
            if (n0.LengthSq < 1e-16) continue;
            n0 = n0.Normalized();
            int a = m.AddVertex(rings[0][j], n0, GlassLeafColour(0, green, red, variance), 1, 0.0, j / (double)sides, 0, 1);
            int b = m.AddVertex(rings[0][j + 1], n0, GlassLeafColour(0, green, red, variance), 1, 0.0, (j + 1) / (double)sides, 0, 1);
            Primitives.TriangleFacing(m, capIdx, a, b, n0);
        }

        // Crystal cells. These are the family's bladder cells, grown as little cut gems. They
        // run as a spine down the dorsal ridge and scatter over the two flanks above it,
        // crowding towards the tip where the real plant's vesicles are densest and most
        // glittery. They are placed on the actual ring surface so they grow out of the
        // epidermis instead of hovering over it.
        int spine = Math.Max(2, cells / 3);
        for (int i = 0; i < spine; i++)
        {
            double t = 0.40 + 0.60 * (i / (double)Math.Max(1, spine - 1)) + rng.Range(-0.03, 0.03);
            double j = 0.5 + rng.Range(-0.030, 0.030);
            double size = 0.00024 * rng.Range(0.75, 1.3) * (0.5 + 0.9 * t);
            PlaceCrystal(m, rings, rows, sides, t, j, size, rng, phase, lobe, halfWidth);
        }
        for (int i = 0; i < cells; i++)
        {
            double t = 0.28 + 0.72 * Math.Pow(rng.NextDouble(), 0.6);
            double j = 0.5 + rng.Range(-0.22, 0.22);
            double size = 0.00015 * rng.Range(0.6, 1.4) * (0.5 + 0.9 * t);
            PlaceCrystal(m, rings, rows, sides, t, j, size, rng, phase, lobe, halfWidth);
        }

        m.RecordLeaf(leafVertex, leafIndex, basePoint, length, volumetric: true);
    }

    /// <summary>
    /// Places one crystal on the leaf surface at the given along-position and ring fraction.
    /// </summary>
    private static void PlaceCrystal(MeshData m, Vec3[][] rings, int rows, int sides, double t, double jf,
        double size, Rng rng, double phase, double lobe, double halfWidth)
    {
        double fi = Math.Clamp(t, 0.0, 1.0) * rows;
        int i0 = Math.Min(rows - 1, (int)Math.Floor(fi));
        double bi = fi - i0;
        double fj = Math.Clamp(jf, 0.0, 1.0) * sides;
        int j0 = Math.Min(sides - 1, (int)Math.Floor(fj));
        double bj = fj - j0;
        var p = Vec3.Lerp(Vec3.Lerp(rings[i0][j0], rings[i0][j0 + 1], bj),
            Vec3.Lerp(rings[i0 + 1][j0], rings[i0 + 1][j0 + 1], bj), bi);
        // Outward normal from the local surface, taken across and along the blade.
        var alongN = rings[Math.Min(rows, i0 + 1)][j0] - rings[i0][j0];
        var acrossN = rings[i0][j0 + 1] - rings[i0][j0];
        var n = acrossN.Cross(alongN);
        if (n.LengthSq < 1e-18)
        {
            n = (p - rings[i0][j0]).Cross(alongN);
            if (n.LengthSq < 1e-18) return;
        }
        n = n.Normalized();
        GlassCrystalCell(m, p + n * (size * 0.25), n, size, rng, t);
    }

    /// <summary>
    /// One bladder crystal: a low, faceted bipyramid half sunk into the blade so it reads as
    /// growing out of the epidermis rather than glued onto it. Its own pigment is written to
    /// the vertex colour, and it is flagged with the berry organ marker so the renderer
    /// gives it a glossy cut instead of the waxy leaf finish.
    /// </summary>
    private static void GlassCrystalCell(MeshData m, Vec3 centre, Vec3 normal, double size, Rng rng, double t)
    {
        // Build a local frame around the surface normal.
        var n = normal.LengthSq > 1e-12 ? normal.Normalized() : Vec3.Up;
        var t1 = n.Cross(new Vec3(1, 0, 0));
        if (t1.LengthSq < 1e-8) t1 = n.Cross(new Vec3(0, 0, 1));
        t1 = t1.Normalized();
        var t2 = n.Cross(t1).Normalized();

        // Azure, running a touch violet towards the leaf tip where the red flush overlaps.
        var col = Primitives.Mix(GlassCrystal, GlassCrystalDeep, rng.Range(0.0, 0.45));
        col = Primitives.Mix(col, new[] { 0.42, 0.16, 0.46 }, MathD.SmoothStep(0.72, 1.0, t) * 0.5);
        col = Primitives.Scale(col, rng.Range(0.85, 1.2));

        double h = size * rng.Range(0.85, 1.5);
        double w = size * rng.Range(0.8, 1.25);
        // Apex, a four-point girdle at the surface, and a sunk base.
        Vec3 Apex = centre + n * h;
        Vec3 Base = centre - n * (h * 0.7);
        var girdle = new Vec3[4];
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI * 0.5 + rng.Range(-0.22, 0.22);
            girdle[i] = centre + t1 * (Math.Cos(a) * w) + t2 * (Math.Sin(a) * w) - n * (h * 0.16);
        }
        int apex = m.AddVertex(Apex, n, col, 1, 0, 0.5, 0.8, 0);
        int sunk = m.AddVertex(Base, -n, col, 1, 0, 0.5, 0.8, 0);
        var ids = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var outward = (girdle[i] - centre).Normalized();
            ids[i] = m.AddVertex(girdle[i], outward, col, 1, 0, 0.5 + 0.1 * i, 0.8, 0);
        }
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Primitives.TriangleFacing(m, apex, ids[i], ids[j], (girdle[i] - centre).Cross(girdle[j] - centre).Normalized());
            Primitives.TriangleFacing(m, sunk, ids[j], ids[i], (girdle[i] - centre).Cross(girdle[j] - centre).Normalized());
        }
    }

    /// <summary>
    /// A daisy-like ice-plant flower: many narrow petals in two whorls over a ring of stamens.
    /// </summary>
    private static void GlassFlower(MeshData m, Vec3 centre, double radius, ulong seed, double[] green)
    {
        var rng = Rng.Keyed(seed, "glassfinger.flower", 0);
        int petals = 30 + rng.NextInt(10);
        var pink = new[] { 0.90, 0.46, 0.58 };
        var pale = new[] { 0.97, 0.80, 0.84 };

        // Receptacle.
        Primitives.Ellipsoid(m, centre - Vec3.Up * (radius * 0.10),
            new Vec3(radius * 0.30, radius * 0.16, radius * 0.30), 4, 10,
            (u, v) => (Primitives.Mix(green, new[] { 0.30, 0.42, 0.24 }, 0.5), 1, u, v, 0, 0));

        for (int p = 0; p < petals; p++)
        {
            double a = 2 * Math.PI * p / petals + rng.Range(-0.03, 0.03);
            double tier = p % 2 == 0 ? 0.0 : 0.35;
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = new Vec3(-dir.Z, 0, dir.X);
            double len = radius * rng.Range(0.82, 1.0) * (1.0 - tier * 0.22);
            double wid = len * rng.Range(0.10, 0.15);
            var root = centre + dir * (radius * (0.22 + tier * 0.10));
            // The petal lifts, then curls back over.
            int start = m.VertexCount;
            int steps = 3;
            for (int i = 0; i <= steps; i++)
            {
                double tt = i / (double)steps;
                var mid = root + dir * (len * tt);
                double lift = Math.Sin(tt * Math.PI * 0.85) * len * 0.13 - Math.Pow(tt, 3.0) * len * 0.26;
                double w = wid * (0.30 + 0.70 * Math.Sin(Math.PI * (0.16 + 0.84 * tt)));
                var c = mid + Vec3.Up * lift;
                var nrm = Vec3.Up;
                m.AddVertex(c - side * w, nrm, Primitives.Mix(pink, pale, tt * 0.5), 1, tt, 0.0, 0, 1);
                m.AddVertex(c + side * w, nrm, Primitives.Mix(pink, pale, tt * 0.5), 1, tt, 1.0, 0, 1);
            }
            for (int i = 0; i < steps; i++)
            {
                int a0 = start + i * 2;
                Primitives.TriangleFacing(m, a0, a0 + 1, a0 + 2, Vec3.Up);
                Primitives.TriangleFacing(m, a0 + 1, a0 + 3, a0 + 2, Vec3.Up);
            }
        }

        // Stamen ring.
        int stamens = 26;
        for (int s = 0; s < stamens; s++)
        {
            double a = 2 * Math.PI * s / stamens + rng.Range(-0.05, 0.05);
            double rr = radius * rng.Range(0.10, 0.20);
            var foot = centre + new Vec3(Math.Cos(a) * rr, radius * 0.02, Math.Sin(a) * rr);
            var tip = foot + new Vec3(Math.Cos(a) * radius * 0.05, radius * 0.13, Math.Sin(a) * radius * 0.05);
            var yellow = new[] { 0.95, 0.76, 0.22 };
            int i0 = m.AddVertex(foot, Vec3.Up, yellow, 1, 0, 0.5, 0, 0);
            int i1 = m.AddVertex(tip, Vec3.Up, yellow, 1, 1, 0.5, 0, 0);
            int i2 = m.AddVertex(tip + new Vec3(0, 0.0001, 0), Vec3.Up, new[] { 0.98, 0.88, 0.40 }, 1, 1, 0.5, 0, 0);
            Primitives.TriangleFacing(m, i0, i1, i2, Vec3.Up);
        }
    }

    /// <summary>
    /// The glassfinger plant. Shoots creep across the gravel; every node carries a decussate
    /// pair of keeled leaves; a few branches and flowers break the symmetry.
    /// </summary>
    private static void Glassfinger(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };
        var green = Primitives.Mix(GlassGlaucous, c1, 0.35);
        var red = Primitives.Mix(new[] { 0.58, 0.12, 0.20 }, c2, 0.4);

        int rows = 11, sides = 10;
        if (m.Lod is { } lod)
        {
            rows = lod.Segments(rows, 4);
            sides = lod.Segments(sides, 5);
        }
        if (m.FloraDetailLevel is 1) { rows = Math.Max(4, rows - 3); sides = Math.Max(5, sides - 2); }
        else if (m.FloraDetailLevel is 2) { rows = Math.Max(3, rows - 5); sides = Math.Max(4, sides - 4); }

        const int shoots = 10;
        const int segsPerShoot = 12;
        const double stemBase = 0.0017;
        var flowers = new List<Vec3>();

        for (int k = 0; k < shoots; k++)
        {
            double az = 2 * Math.PI * k / shoots + rng.Range(-0.26, 0.26);
            double reach = radius * rng.Range(0.74, 0.98);
            var path = new Vec3[segsPerShoot + 1];
            var radii = new double[segsPerShoot + 1];
            for (int s = 0; s <= segsPerShoot; s++)
            {
                double t = s / (double)segsPerShoot;
                double wander = 0.15 * Math.Sin(t * Math.PI * 2.1 + k * 1.7);
                double a = az + wander;
                double r = t * reach;
                // The shoot lifts clear of the gravel at the crown and settles onto it.
                double y = 0.0060 * Math.Pow(1.0 - t, 2.2) + 0.0016 * Math.Sin(t * 4.1 + k);
                path[s] = new Vec3(Math.Cos(a) * r, y, Math.Sin(a) * r);
                // Nodes are visibly thicker than the internode beside them.
                bool node = s >= 3 && s <= segsPerShoot - 1 && s % 2 == 1;
                radii[s] = stemBase * (1.0 - 0.38 * t) * (node ? 1.46 : 1.0)
                    * (1.0 + 0.06 * Math.Sin(s * 2.1 + k));
            }
            GlassLobedTube(part, path, radii, 9, 0.20, az + rng.Range(-0.4, 0.4),
                (i, v) => (Primitives.Mix(Primitives.Scale(green, 0.60), Primitives.Scale(green, 0.80), (double)i / segsPerShoot), 1,
                    (double)i / segsPerShoot, v, 0, 0));

            // Leaves: opposite pairs, decussate, one pair per node.
            double nodeAz = az + rng.Range(0.0, 2 * Math.PI);
            for (int s = 1; s <= segsPerShoot - 1; s += 2)
            {
                var node = path[s];
                var shootDir = path[s + 1] - path[s - 1];
                shootDir = new Vec3(shootDir.X, 0, shootDir.Z);
                if (shootDir.LengthSq < 1e-10) shootDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                shootDir = shootDir.Normalized();

                double lastAz = nodeAz;
                for (int pair = 0; pair < 2; pair++)
                {
                    double la = nodeAz + pair * Math.PI;
                    nodeAz += Math.PI * 0.5; // decussate: the next pair turns a quarter turn
                    lastAz = la;
                    var spread = new Vec3(Math.Cos(la), 0, Math.Sin(la));
                    // The blade leaves the node across the shoot and forward, lifted a little.
                    double tilt = rng.Range(0.10, 0.50);
                    var along = (spread * Math.Cos(tilt) + Vec3.Up * Math.Sin(tilt)
                        + shootDir * rng.Range(0.18, 0.46)).Normalized();
                    var across = Vec3.Up.Cross(along).Normalized();
                    var dorsal = along.Cross(across).Normalized();
                    // The shoot tip is the newest growth, so its blades are the smallest; the
                    // mass of the plant sits in the older wood near the crown.
                    double len = rng.Range(0.024, 0.037) * (1.0 - 0.30 * (s / (double)segsPerShoot));
                    double halfW = len * rng.Range(0.115, 0.155);
                    ulong leafSeed = Rng.Mix(seed, (ulong)(k * 733 + s * 61 + pair * 17 + 5));
                    int cells = 6 + (int)(rng.NextDouble() * 7);
                    // The blade starts inside the stem, so the sheath appears to clasp it.
                    var root = node - along * (stemBase * 0.55);
                    GlassLeaf(part, root, along, across, dorsal, len, halfW, 0.32, rows, sides,
                        leafSeed, green, red, rng.Range(-0.05, 0.05), cells);
                }

                // A short side shoot now and then, so the mat branches instead of radiating.
                if (s >= 5 && rng.Chance(0.30))
                {
                    double sa = lastAz + rng.Range(-0.5, 0.5);
                    double slen = rng.Range(0.016, 0.030);
                    var sdir = new Vec3(Math.Cos(sa), 0, Math.Sin(sa));
                    var spath = new Vec3[4];
                    var sradii = new double[4];
                    for (int q = 0; q <= 3; q++)
                    {
                        double qt = q / 3.0;
                        spath[q] = node + sdir * (slen * qt) + Vec3.Up * (0.0022 * Math.Sin(qt * Math.PI));
                        sradii[q] = stemBase * 0.72 * (1.0 - 0.3 * qt) * (q == 0 ? 1.2 : 1.0);
                    }
                    GlassLobedTube(part, spath, sradii, 7, 0.18, sa,
                        (i, v) => (Primitives.Scale(green, 0.80), 1, i / 3.0, v, 0, 0));
                    for (int q = 1; q <= 3; q++)
                    {
                        double la2 = sa + q * Math.PI * 0.5;
                        var spread2 = new Vec3(Math.Cos(la2), 0, Math.Sin(la2));
                        double tilt2 = rng.Range(0.20, 0.55);
                        var along2 = (spread2 * Math.Cos(tilt2) + Vec3.Up * Math.Sin(tilt2)
                            + sdir * rng.Range(0.1, 0.3)).Normalized();
                        var across2 = Vec3.Up.Cross(along2).Normalized();
                        var dorsal2 = along2.Cross(across2).Normalized();
                        double len2 = rng.Range(0.016, 0.026);
                        GlassLeaf(part, spath[q] - along2 * (stemBase * 0.4), along2, across2, dorsal2,
                            len2, len2 * rng.Range(0.10, 0.14), 0.30, Math.Max(3, rows - 2), Math.Max(5, sides - 1),
                            Rng.Mix(seed, (ulong)(k * 191 + s * 29 + q * 7 + 3)), green, red, 0.0, 4);
                    }
                }
            }
            flowers.Add(path[segsPerShoot]);
        }

        // Crown: the tight cluster of young leaves at the growing point, paler and more upright
        // than the rest of the plant. This is where the mass of an ice plant actually sits.
        for (int c = 0; c < 18; c++)
        {
            double az = 2 * Math.PI * c / 18 + rng.Range(-0.26, 0.26);
            var spread = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double tilt = rng.Range(0.42, 0.95);
            var along = (spread * Math.Cos(tilt) + Vec3.Up * Math.Sin(tilt)).Normalized();
            var across = Vec3.Up.Cross(along).Normalized();
            var dorsal = along.Cross(across).Normalized();
            double len = rng.Range(0.017, 0.031);
            var root = spread * (radius * rng.Range(0.01, 0.16)) + Vec3.Up * 0.002;
            GlassLeaf(part, root, along, across, dorsal, len, len * rng.Range(0.125, 0.170), 0.32,
                Math.Max(4, rows - 2), Math.Max(5, sides - 1),
                Rng.Mix(seed, (ulong)(c * 397 + 11)), Primitives.Mix(green, GlassGlaucousPale, 0.45),
                red, rng.Range(-0.04, 0.04), 4);
        }

        // Outer skirt: a ring of older blades lying almost flat on the gravel, filling the
        // ground between the crown and the shoots so the plant sits on the soil instead of
        // hovering over it.
        for (int c = 0; c < 16; c++)
        {
            double az = 2 * Math.PI * c / 16 + rng.Range(-0.2, 0.2) + 0.19;
            var spread = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double tilt = rng.Range(0.06, 0.26);
            var along = (spread * Math.Cos(tilt) + Vec3.Up * Math.Sin(tilt)).Normalized();
            var across = Vec3.Up.Cross(along).Normalized();
            var dorsal = along.Cross(across).Normalized();
            double len = rng.Range(0.022, 0.034);
            var root = spread * (radius * rng.Range(0.10, 0.34)) + Vec3.Up * 0.0016;
            GlassLeaf(part, root, along, across, dorsal, len, len * rng.Range(0.130, 0.175), 0.32,
                Math.Max(4, rows - 2), Math.Max(5, sides - 1),
                Rng.Mix(seed, (ulong)(c * 523 + 29)), green, red, rng.Range(-0.05, 0.05), 5);
        }

        // Flowers on short peduncles, a few of them open and the rest still in bud.
        int flowerCount = 3 + rng.NextInt(2);
        for (int f = 0; f < flowerCount; f++)
        {
            var basePoint = flowers[rng.NextInt(flowers.Count)];
            double a = rng.Range(0, 2 * Math.PI);
            var lean = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double pedLen = rng.Range(0.010, 0.024);
            var top = basePoint + lean * (pedLen * 0.55) + Vec3.Up * pedLen;
            var ppath = new Vec3[4];
            var pradii = new double[4];
            for (int i = 0; i <= 3; i++)
            {
                double pt = i / 3.0;
                ppath[i] = basePoint + (top - basePoint) * pt + Vec3.Up * (0.0009 * Math.Sin(pt * Math.PI));
                pradii[i] = 0.0013 * (1.0 - 0.18 * pt);
            }
            GlassLobedTube(part, ppath, pradii, 7, 0.16, a,
                (i, v) => (Primitives.Scale(green, 0.86), 1, i / 3.0, v, 0, 0));
            GlassFlower(part, top, 0.0088, Rng.Mix(seed, (ulong)(f * 811 + 17)), green);
        }

        AppendGlassWorld(m, part, radius, height);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer
    /// scales instances by (radius, height, radius), so this inverse scale makes the plant
    /// appear at the size and proportion it was authored at. Normals need the inverse
    /// transpose of that scale.
    /// </summary>
    private static void AppendGlassWorld(MeshData target, MeshData part, double radius, double height)
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

        // Tube radii are recorded in world metres; restate them in mesh units so the LOD tiers
        // keep classifying petioles as petioles rather than as structural wood.
        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }
}
