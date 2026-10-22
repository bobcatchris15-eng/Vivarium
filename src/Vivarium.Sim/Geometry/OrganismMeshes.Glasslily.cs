using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Glasslily - the rooted floating-leaf lily of still pond water.
/// Analogue: Nymphaea, the water lily: a knuckled rhizome on the pond bed, round floating pads with
/// a deep basal cleft held on long flexible underwater petioles, and a tall flower holding a cup of
/// cut glass proud of the water surface.
///
/// Coordinate contract for floating species:
///   - Pond bed / rhizome sits at Y ~ 0.008m.
///   - Water surface is normalized to unit height Y = 1.0.
///   - Floating pads lie flat at Y = 1.0.
///   - Underwater petioles connect the bed rhizome up through the water column to Y ~ 0.995 (pad underside).
///   - The emergent flower scape rises from the bed through the water surface to Y ~ 1.20m.
///   - Horizontal spread (X, Z) is authored directly in real metres (r = 1.0 in renderer).
///   - The engine's renderer scales Y by local water depth (rise + 0.003m proud of water).
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The waxy upper face of a pad: deep lustrous aquatic jade green.</summary>
    private static readonly double[] LilyPadTop = { 0.14, 0.34, 0.18 };
    /// <summary>The raised radial rib veins on the upper face.</summary>
    private static readonly double[] LilyPadVein = { 0.30, 0.50, 0.24 };
    /// <summary>The underside of a real lily pad: rich wine-red/maroon.</summary>
    private static readonly double[] LilyPadUnder = { 0.36, 0.12, 0.18 };
    /// <summary>Raised structural aerenchyma ribs on the underside.</summary>
    private static readonly double[] LilyPadUnderVein = { 0.56, 0.22, 0.24 };
    /// <summary>The petioles: aquatic green underwater, darkening to olive-brown near rhizome.</summary>
    private static readonly double[] LilyPetiole = { 0.22, 0.36, 0.18 };
    /// <summary>The cold crystalline glass of the petals.</summary>
    private static readonly double[] LilyGlass = { 0.88, 0.94, 0.93 };
    /// <summary>The warm rose blush at the petal bases.</summary>
    private static readonly double[] LilyRose = { 0.94, 0.64, 0.70 };
    /// <summary>The lead wire dividing stained-glass facets along the petal veins.</summary>
    private static readonly double[] LilyLead = { 0.38, 0.48, 0.46 };
    /// <summary>The boss of golden stamens at the heart of the flower.</summary>
    private static readonly double[] LilyGold = { 0.98, 0.82, 0.28 };
    /// <summary>The still water meniscus lens inside the flower cup.</summary>
    private static readonly double[] LilyWater = { 0.05, 0.10, 0.13 };
    /// <summary>The rhizome's ancient, mud-stained scarred tissue.</summary>
    private static readonly double[] LilyRhizome = { 0.28, 0.22, 0.16 };
    /// <summary>Fine adventitious roots anchoring into the silt.</summary>
    private static readonly double[] LilyRoot = { 0.38, 0.32, 0.24 };

    /// <summary>
    /// A floating pad: a dished disc with a deep radial cleft oriented toward the rhizome,
    /// a rolled rim, a veined waxy upper face and a veined, wine-coloured underside.
    ///
    /// The pad is a shell, not a sheet: it has a real thickness, so the rim reads as a rolled edge
    /// from any angle and the underside can be a different material from the top. The cleft is a
    /// real cut - the two edges of the wedge are capped with the cut face, so you can see into the
    /// gap the way you can on a real pad.
    /// </summary>
    private static void GlasslilyPad(MeshData m, Vec3 centre, double padRadius, double cleftAngle, double dish, bool isOld, Rng rng)
    {
        int firstVertex = m.VertexCount;
        int firstIndex = m.Indices.Count;
        const int rings = 6;
        const int sectors = 42;
        // Half-angle of the cleft notch (approx 34-40 degrees total notch).
        double cleft = rng.Range(0.28, 0.35);
        double a0 = cleft, a1 = Math.PI * 2.0 - cleft;
        const double rimLift = 0.0045;
        const double thickness = 0.0024;

        var top = new int[rings, sectors + 1];
        var bottom = new int[rings, sectors + 1];
        var topNormals = new Vec3[rings, sectors + 1];
        var bottomNormals = new Vec3[rings, sectors + 1];

        // 9 primary radiating veins on the upper face. Underneath, 7 heavier ones with a finer secondary net.
        static double VeinTop(double theta) => Math.Pow(Math.Max(0.0, Math.Cos(9.0 * theta)), 16.0);
        static double VeinUnder(double theta) =>
            Math.Pow(Math.Max(0.0, Math.Cos(7.0 * theta)), 10.0)
            + 0.45 * Math.Pow(Math.Max(0.0, Math.Cos(23.0 * theta)), 22.0);

        Vec3 Point(double u, double thetaLocal, bool upper, out double vein)
        {
            vein = upper ? VeinTop(thetaLocal) : VeinUnder(thetaLocal);
            // cleftAngle rotates local +X to point toward the rhizome (-outward).
            double worldTheta = cleftAngle + thetaLocal;

            // Subtle asymmetry / lobed contouring of a real water lily pad
            double lobePerturb = 1.0 + 0.035 * Math.Cos(2.0 * thetaLocal) - 0.02 * Math.Cos(thetaLocal);
            double r = padRadius * lobePerturb * u;

            // Fade veins out smoothly toward the center to prevent any star pinch
            double veinFade = MathD.SmoothStep(0.04, 0.26, u);

            double h = -dish * (1.0 - Math.Pow(Math.Min(1.0, u), 1.8)) + rimLift * MathD.SmoothStep(0.78, 1.0, u);
            if (upper)
            {
                h += 0.0020 * vein * veinFade * (1.0 - 0.45 * u);
            }
            else
            {
                double th = thickness * (0.40 + 0.60 * (1.0 - MathD.SmoothStep(0.50, 1.0, u)));
                h -= th;
                h -= 0.0020 * vein * veinFade * (1.0 - 0.40 * u);
            }
            return centre + new Vec3(Math.Cos(worldTheta) * r, h, Math.Sin(worldTheta) * r);
        }

        var yellowEdge = new[] { 0.58, 0.52, 0.20 };

        for (int i = 0; i < rings; i++)
        {
            // u spans from 1/rings to 1.0 (ring 0 is at radius > 0, connected to tc/bc center)
            double u = (i + 1.0) / rings;
            for (int j = 0; j <= sectors; j++)
            {
                double thetaLocal = a0 + (a1 - a0) * (j / (double)sectors);
                var p = Point(u, thetaLocal, true, out double vein);
                var col = Primitives.Mix(LilyPadTop, LilyPadVein, Math.Min(1.0, vein * 0.8));
                if (isOld && u > 0.65)
                {
                    double edgeBlend = MathD.SmoothStep(0.65, 1.0, u) * rng.Range(0.35, 0.65);
                    col = Primitives.Mix(col, yellowEdge, edgeBlend);
                }
                top[i, j] = m.AddVertex(p, Vec3.Up, col, 1, j / (double)sectors, u, 1.35, 1.0);

                var pb = Point(u, thetaLocal, false, out double veinB);
                var colB = Primitives.Mix(LilyPadUnder, LilyPadUnderVein, Math.Min(1.0, veinB * 0.85));
                if (isOld && u > 0.70)
                {
                    colB = Primitives.Mix(colB, yellowEdge, 0.3 * MathD.SmoothStep(0.70, 1.0, u));
                }
                bottom[i, j] = m.AddVertex(pb, -Vec3.Up, colB, 1, j / (double)sectors, u, 1.35, 1.0);
            }
        }

        // Center vertices for top and bottom faces
        int tc = m.AddVertex(centre - Vec3.Up * dish, Vec3.Up, Primitives.Mix(LilyPadTop, LilyPadVein, 0.35), 1, 0.0, 0.5, 1.35, 1.0);
        int bc = m.AddVertex(centre - Vec3.Up * (dish + thickness), -Vec3.Up, Primitives.Mix(LilyPadUnder, LilyPadUnderVein, 0.35), 1, 0.0, 0.5, 1.35, 1.0);

        // Center fans (connecting center vertex to ring 0)
        for (int j = 0; j < sectors; j++)
        {
            Primitives.TriangleFacing(m, tc, top[0, j], top[0, j + 1], Vec3.Up);
            Primitives.TriangleFacing(m, bc, bottom[0, j + 1], bottom[0, j], -Vec3.Up);
        }

        // Upper face intermediate ring quads
        for (int i = 0; i < rings - 1; i++)
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

        // Underside intermediate ring quads
        for (int i = 0; i < rings - 1; i++)
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

        // Rolled rim strip joining top and bottom along the outer ring (i = rings - 1)
        for (int j = 0; j < sectors; j++)
        {
            int rTop = rings - 1;
            var a = m.Position(top[rTop, j]);
            var b = m.Position(top[rTop, j + 1]);
            var c = m.Position(bottom[rTop, j]);
            var n = (b - a).Cross(c - a);
            if (n.LengthSq < 1e-18) continue;
            n = n.Normalized();
            Primitives.TriangleFacing(m, top[rTop, j], bottom[rTop, j], top[rTop, j + 1], n);
            Primitives.TriangleFacing(m, top[rTop, j + 1], bottom[rTop, j], bottom[rTop, j + 1], n);
        }

        // Cleft cut faces along j = 0 and j = sectors
        for (int i = 0; i < rings - 1; i++)
        {
            foreach (int j in new[] { 0, sectors })
            {
                var p0 = m.Position(top[i, j]);
                var p1 = m.Position(top[i + 1, j]);
                var p2 = m.Position(bottom[i, j]);
                var n = (p1 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                Primitives.TriangleFacing(m, top[i, j], bottom[i, j], top[i + 1, j], n);
                Primitives.TriangleFacing(m, top[i + 1, j], bottom[i, j], bottom[i + 1, j], n);
            }
        }
        // Inner cleft cap connecting center vertices tc/bc to ring 0 at j = 0 and j = sectors
        {
            var p0 = m.Position(tc);
            var p1 = m.Position(top[0, 0]);
            var p2 = m.Position(bc);
            var n = (p1 - p0).Cross(p2 - p0);
            if (n.LengthSq > 1e-18)
            {
                n = n.Normalized();
                Primitives.TriangleFacing(m, tc, bc, top[0, 0], n);
                Primitives.TriangleFacing(m, top[0, 0], bc, bottom[0, 0], n);
            }

            var q0 = m.Position(tc);
            var q1 = m.Position(top[0, sectors]);
            var q2 = m.Position(bc);
            var nq = (q1 - q0).Cross(q2 - q0);
            if (nq.LengthSq > 1e-18)
            {
                nq = nq.Normalized();
                Primitives.TriangleFacing(m, tc, top[0, sectors], bc, nq);
                Primitives.TriangleFacing(m, top[0, sectors], bottom[0, sectors], bc, nq);
            }
        }

        // Apply smoothed normals to ring vertices
        for (int i = 0; i < rings; i++)
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

        // Record leaf and essential span
        m.RecordLeaf(firstVertex, firstIndex, centre, padRadius * 2.0, volumetric: true);
        m.EssentialSpans.Add((firstIndex, m.Indices.Count - firstIndex));
    }

    /// <summary>
    /// A long, buoyant underwater petiole connecting the pond-floor rhizome to the floating pad.
    /// It sweeps outward through the water column and rises smoothly into the peltate underside of the pad.
    /// </summary>
    private static void GlasslilyPetiole(MeshData m, Vec3 root, Vec3 padCentre, Vec3 outward, double reach, Rng rng)
    {
        var p0 = root;
        // Lateral lazy sway from gentle water currents
        var drift = new Vec3(-outward.Z, 0, outward.X) * rng.Range(-0.035, 0.035);
        // Sweeps outward along lower depth, then curves gracefully upwards into pad
        var p1 = p0 + outward * (reach * 0.42) + Vec3.Up * 0.18 + drift * 1.4;
        var p2 = padCentre - outward * (reach * 0.08) - Vec3.Up * 0.42 + drift * 0.7;
        var p3 = padCentre - Vec3.Up * 0.006;

        const int segs = 12;
        var path = new Vec3[segs + 1];
        var radii = new double[segs + 1];

        for (int i = 0; i <= segs; i++)
        {
            double t = i / (double)segs;
            double u = 1.0 - t;
            path[i] = p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
            radii[i] = 0.0038 * (1.0 - 0.38 * Math.Pow(t, 0.85));
        }

        var colBase = Primitives.Mix(LilyRhizome, LilyPetiole, 0.55);
        var colTop = Primitives.Mix(LilyPetiole, LilyPadVein, 0.35);

        Primitives.Tube(m, path, radii, 6,
            (i, v) => (Primitives.Mix(colBase, colTop, i / (double)segs), 1, i / (double)segs, v, 0, 0));
    }

    /// <summary>
    /// The rhizome: a short, knuckled, horizontal rootstock lying on the pond bed, wearing the
    /// scarred stubs of shed leaves, with fine adventitious roots hanging into the silt below.
    /// </summary>
    private static void GlasslilyRhizome(MeshData m, Vec3 origin, double length, Rng rng)
    {
        const int knuckles = 6;
        var path = new Vec3[knuckles + 1];
        var radii = new double[knuckles + 1];
        var lateral = new Vec3(Math.Cos(0.4), 0, Math.Sin(0.4));
        for (int i = 0; i <= knuckles; i++)
        {
            double t = i / (double)knuckles;
            path[i] = origin + lateral * (length * t) + Vec3.Up * (0.002 * Math.Sin(t * 7.0));
            double swell = 0.85 + 0.5 * Math.Abs(Math.Cos(t * Math.PI * knuckles));
            radii[i] = 0.0070 * swell;
        }
        Primitives.Tube(m, path, radii, 8,
            (i, v) => (LilyRhizome, 1, i / (double)knuckles, v, 0, 0));

        // Old leaf bases: dark stubs at each node
        for (int i = 0; i <= knuckles; i++)
        {
            var at = path[i];
            double a = rng.Range(0, 2.0 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var tip = at + outward * rng.Range(0.008, 0.016) + Vec3.Up * rng.Range(0.004, 0.012);
            Primitives.Tube(m, new[] { at - outward * 0.002, at + outward * 0.004, tip },
                new[] { 0.0035, 0.0024, 0.0007 }, 5,
                (k, v) => (Primitives.Scale(LilyRhizome, 0.82), 1, k * 0.5, v, 0, 0));
        }

        // Roots: fine unbranched anchoring filaments
        int roots = NewFloraCount(m, 28);
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
    /// A young rolled scroll leaf rising proudly above the water line (unit Y = 1).
    /// Water lilies unfurl in the air: the petiole carries a tight cylindrical coil of involute vernation
    /// with delicate bronze-rose coloration before opening out flat onto the water.
    /// </summary>
    private static void GlasslilyYoungScroll(MeshData m, Vec3 root, Vec3 outward, double reach, Rng rng)
    {
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        double riseY = 1.0 + rng.Range(0.06, 0.14);
        var tipP = root + outward * reach + new Vec3(0, riseY, 0);

        var p0 = root;
        var p1 = p0 + outward * (reach * 0.35) + Vec3.Up * 0.35;
        var p2 = tipP - outward * (reach * 0.12) - Vec3.Up * 0.22;
        var p3 = tipP;

        const int segs = 10;
        var path = new Vec3[segs + 1];
        var radii = new double[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            double t = i / (double)segs;
            double u = 1.0 - t;
            path[i] = p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
            radii[i] = 0.0032 * (1.0 - 0.45 * t);
        }

        Primitives.Tube(m, path, radii, 6,
            (i, v) => (Primitives.Mix(LilyRhizome, LilyPetiole, 0.5), 1, i / (double)segs, v, 0, 0));

        // Scroll coil: spiral tube along the lateral axis
        var side = new Vec3(-outward.Z, 0, outward.X).Normalized();
        const int coilSegs = 14;
        var coil = new Vec3[coilSegs + 1];
        var crad = new double[coilSegs + 1];
        double scrollLen = rng.Range(0.045, 0.065);

        for (int i = 0; i <= coilSegs; i++)
        {
            double t = i / (double)coilSegs;
            double th = t * Math.PI * 3.2;
            double r = 0.012 * (1.0 - t * 0.55);
            coil[i] = tipP + Vec3.Up * (scrollLen * t)
                     + side * (Math.Cos(th) * r)
                     + outward * (Math.Sin(th) * r * 0.45);
            crad[i] = 0.0028 * (1.0 - t * 0.5);
        }

        var youngColor = Primitives.Mix(LilyPadTop, LilyRose, 0.45);
        Primitives.Tube(m, coil, crad, 5,
            (i, v) => (Primitives.Mix(LilyPetiole, youngColor, i / (double)coilSegs), 1, i / (double)coilSegs, v, 1.35, 1.0));

        m.RecordLeaf(leafVertex, leafIndex, tipP, scrollLen, volumetric: true);
    }

    /// <summary>
    /// The glasslily flower: held erect on a strong emergent scape well above the water surface
    /// (Y ~ 1.20). Three imbricated whorls of cut glass petals with delicate rose blush at the base
    /// and dark stained-glass lead lines cradle a glowing golden stamen boss and a still water lens.
    /// Protective sepals cradle the calyx from beneath without occluding the cup.
    /// </summary>
    private static void GlasslilyFlower(MeshData m, Vec3 root, Vec3 flowerBase, double scale, Rng rng)
    {
        int flowerFirstIndex = m.Indices.Count;

        // Scape (stalk) rising from the bed through the water column to flowerBase (Y ~ 1.20)
        {
            const int segs = 12;
            var path = new Vec3[segs + 1];
            var radii = new double[segs + 1];
            var p0 = root;
            var lean = new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02));
            var p1 = p0 + lean * 0.5 + Vec3.Up * 0.40;
            var p2 = flowerBase - lean * 0.3 - Vec3.Up * 0.25;
            var p3 = flowerBase;

            for (int i = 0; i <= segs; i++)
            {
                double t = i / (double)segs;
                double u = 1.0 - t;
                path[i] = p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
                radii[i] = 0.0050 * (1.0 - 0.32 * t);
            }

            Primitives.Tube(m, path, radii, 8,
                (i, v) => (Primitives.Mix(LilyPetiole, LilyPadVein, 0.35 + 0.25 * (i / (double)segs)), 1, i / (double)segs, v, 0, 0));
        }

        // Swollen green/bronze receptacle (calyx torus)
        Primitives.Ellipsoid(m, flowerBase + Vec3.Up * (0.005 * scale),
            new Vec3(0.013 * scale, 0.008 * scale, 0.013 * scale),
            6, 10, (u, v) => (Primitives.Mix(LilyPadTop, LilyPadVein, 0.4), 1, u, v, 0, 0));

        // 4 sturdy protective outer sepals cradling the calyx STRICTLY FROM BENEATH
        for (int k = 0; k < 4; k++)
        {
            double a = 2.0 * Math.PI * k / 4.0 + 0.35;
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            // Cupping outwards and slightly down, supporting the chalice from below
            var dir = (outward * 0.96 - Vec3.Up * 0.10).Normalized();
            NewFloraBlade(m,
                flowerBase + outward * (0.006 * scale) - Vec3.Up * (0.004 * scale),
                dir, Vec3.Up,
                0.055 * scale, 0.020 * scale,
                t => Math.Sin(Math.PI * Math.Min(1.0, 0.08 + 0.92 * t)) * (0.4 + 0.6 * (1.0 - 0.4 * t)),
                t => Primitives.Mix(LilyPadTop, LilyPadUnder, 0.30 + 0.35 * t),
                rows: 5, sides: 5, flatness: 0.50, keel: 0.25, cup: 0.40, camber: 0.15);
        }

        // Three tiered whorls of cut-glass petals inside the calyx:
        var whorls = new (int count, double length, double width, double rise, double drop, double pointiness)[]
        {
            (10, 0.076, 0.030, 0.16, 0.18, 1.00), // outer: large spreading chalice cup
            (9,  0.060, 0.026, 0.38, 0.10, 0.85), // middle: upright chalice walls
            (7,  0.044, 0.020, 0.65, 0.02, 0.55), // inner: compact protective crown
        };

        foreach (var w in whorls)
        {
            int count = NewFloraCount(m, w.count);
            for (int k = 0; k < count; k++)
            {
                double a = 2.0 * Math.PI * k / count + rng.Range(-0.06, 0.06) + w.count * 0.15;
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                double rise = w.rise + rng.Range(-0.04, 0.04);
                var dir = (outward * Math.Cos(rise) + Vec3.Up * Math.Sin(rise)).Normalized();

                int mark = m.VertexCount;
                NewFloraPetal(m,
                    flowerBase + outward * (0.0035 * scale) + Vec3.Up * (0.004 * scale),
                    dir, Vec3.Up,
                    w.length * scale * rng.Range(0.95, 1.05),
                    w.width * scale * rng.Range(0.92, 1.08),
                    t => {
                        double swell = Math.Sin(Math.PI * Math.Min(1.0, 0.12 + 0.88 * t));
                        double point = 1.0 - w.pointiness * 0.70 * Math.Pow(Math.Max(0.0, t - 0.45) / 0.55, 1.6);
                        return Math.Max(0.04, swell * point + (1.0 - w.pointiness) * 0.20 * (1.0 - t));
                    },
                    (t, x, vein) =>
                    {
                        var col = Primitives.Mix(LilyRose, LilyGlass, MathD.SmoothStep(0.04, 0.55, t));
                        col = Primitives.Mix(col, LilyLead, Math.Min(1.0, vein * 0.92));
                        col = Primitives.Mix(col, LilyLead, Math.Min(1.0, Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(x) * 6.0), 3.0) * 0.35));
                        return col;
                    },
                    rows: 7, across: 9,
                    cup: 0.30, camber: -0.04, tipCurl: w.drop,
                    veinCount: 2, veinDepth: 0.08);

                // Glass organ marking: UV2.x = 1.28f, UV2.y = 1.0f
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark, glass: true);
            }
        }

        // Golden stamen brush: 30 radiating filaments with curved anthers
        int stamens = NewFloraCount(m, 30);
        for (int i = 0; i < stamens; i++)
        {
            double a = 2.0 * Math.PI * i / stamens + rng.Range(-0.15, 0.15);
            double lean = rng.Range(0.38, 0.80);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var dir = (outward * Math.Sin(lean) + Vec3.Up * Math.Cos(lean)).Normalized();
            double len = rng.Range(0.012, 0.022) * scale;
            var at = flowerBase + Vec3.Up * (0.0055 * scale);
            var tip = at + dir * len;
            NewFloraFilament(m, at, tip, 0.00060 * scale, LilyGold, 4);
            Primitives.Ellipsoid(m, tip, new Vec3(0.0018 * scale, 0.0030 * scale, 0.0018 * scale), 2, 5,
                (u, v) => (Primitives.Scale(LilyGold, 0.92), 1, u, v, 0, 0));
        }

        // Still water meniscus lens nestled inside the flower cup
        double waterR = 0.012 * scale;
        var waterCentre = flowerBase + Vec3.Up * (0.0050 * scale);
        int wc = m.AddVertex(waterCentre + Vec3.Up * (0.0010 * scale), Vec3.Up, LilyWater, 1, 0.5, 0.5, 0, 0);
        var rim = new int[20];
        for (int i = 0; i < 20; i++)
        {
            double a = 2.0 * Math.PI * i / 20.0;
            double lift = 0.0014 * scale * Math.Max(0.0, Math.Cos(a - 2.2));
            var col = Primitives.Mix(LilyWater, LilyGlass, 0.35 * Math.Max(0.0, Math.Cos(a - 2.2)));
            rim[i] = m.AddVertex(waterCentre + new Vec3(Math.Cos(a) * waterR, lift, Math.Sin(a) * waterR),
                Vec3.Up, col, 1, 0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a), 0, 0);
        }
        for (int i = 0; i < 20; i++)
            Primitives.TriangleFacing(m, wc, rim[i], rim[(i + 1) % 20], Vec3.Up);

        // Keep flower head in essential spans so it never disappears on LOD
        m.EssentialSpans.Add((flowerFirstIndex, m.Indices.Count - flowerFirstIndex));
    }

    /// <summary>
    /// A flower bud on its own emergent scape, just rising above the water surface (Y ~ 1.04).
    /// </summary>
    private static void GlasslilyBud(MeshData m, Vec3 root, Vec3 budBase, double height, Rng rng)
    {
        const int segs = 8;
        var path = new Vec3[segs + 1];
        var radii = new double[segs + 1];
        for (int i = 0; i <= segs; i++)
        {
            double t = i / (double)segs;
            path[i] = Vec3.Lerp(root, budBase, t) + new Vec3(rng.Range(-0.01, 0.01), 0, rng.Range(-0.01, 0.01)) * Math.Sin(Math.PI * t);
            radii[i] = 0.0032 * (1.0 - 0.35 * t);
        }
        Primitives.Tube(m, path, radii, 6,
            (i, v) => (Primitives.Mix(LilyRhizome, LilyPetiole, 0.5), 1, i / (double)segs, v, 0, 0));

        double r = height * 0.24;
        const int wraps = 6;
        for (int k = 0; k < wraps; k++)
        {
            double a = 2.0 * Math.PI * k / wraps;
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var dir = (outward * 0.34 + Vec3.Up).Normalized();
            int mark = m.VertexCount;
            NewFloraBlade(m,
                budBase + outward * (r * 0.30),
                dir, Vec3.Up,
                height * rng.Range(0.85, 1.0),
                r * 0.42,
                t => Math.Sin(Math.PI * Math.Min(1.0, 0.06 + 0.94 * t)) * (0.5 + 0.5 * Math.Sin(Math.PI * t)),
                t => Primitives.Mix(LilyRose, LilyGlass, MathD.SmoothStep(0.1, 0.8, t)),
                rows: 6, sides: 6, flatness: 0.55, keel: 0.10, cup: 0.30, camber: 0.22, tipCurl: 0.12);
            NewFloraMarkOrgan(m, mark, m.VertexCount - mark, glass: true);
        }

        // Green/bronze sepals at the foot of the bud
        for (int k = 0; k < 4; k++)
        {
            double a = 2.0 * Math.PI * k / 4.0 + 0.3;
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            NewFloraBlade(m, budBase - Vec3.Up * (height * 0.06), (outward * 0.75 - Vec3.Up * 0.25).Normalized(),
                Vec3.Up, height * 0.34, height * 0.10,
                t => Math.Sin(Math.PI * Math.Min(1.0, 0.1 + 0.9 * t)),
                t => Primitives.Mix(LilyPadTop, LilyPadVein, 0.3),
                rows: 4, sides: 5, flatness: 0.4, keel: 0.22, camber: 0.3);
        }
    }

    /// <summary>
    /// The glasslily: a rooted floating-leaf lily of still pond water.
    /// Pond bed is at Y ~ 0.008; water surface is normalized to unit Y = 1.0.
    /// Long flexible petioles connect the bed rhizome to the floating pads resting on the surface.
    /// A single proud flower scape holds the cut-glass goblet above the water line (Y ~ 1.20).
    /// </summary>
    private static void Glasslily(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        // Pond floor at Y ~ 0.008
        var bed = new Vec3(0, 0.008, 0);
        double rhizomeLen = rng.Range(0.08, 0.12);
        GlasslilyRhizome(m, bed, rhizomeLen, rng);

        // Colony spreading reach (metres): pads reach 0.20 to 0.35m from the center
        int padCount = NewFloraCount(m, 7);
        int youngScrollCount = 1 + rng.NextInt(2); // 1-2 young emergent scroll leaves
        int oldPadCount = 1 + rng.NextInt(2);      // 1-2 older pads with weathered margins

        for (int i = 0; i < padCount; i++)
        {
            double a = 2.0 * Math.PI * i / padCount + rng.Range(-0.25, 0.25);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = rng.Range(0.20, 0.35);

            var root = bed + outward * rng.Range(0.01, 0.04) + new Vec3(rng.Range(-0.008, 0.008), 0, rng.Range(-0.008, 0.008));
            var padCentre = new Vec3(root.X + outward.X * reach, 1.0, root.Z + outward.Z * reach);

            // Connect rhizome on pond bed to pad on water surface
            GlasslilyPetiole(m, root, padCentre, outward, reach, rng);

            // Pad cleft points inward toward rhizome (-outward)
            double cleftAngle = Math.Atan2(-outward.Z, -outward.X);
            double padR = rng.Range(0.095, 0.135);
            bool isOld = i >= padCount - oldPadCount;

            GlasslilyPad(m, padCentre, padR, cleftAngle, 0.004, isOld, rng);
        }

        // Young rolled scroll leaves (emergent involute vernation)
        for (int i = 0; i < youngScrollCount; i++)
        {
            double a = rng.Range(0, 2.0 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = rng.Range(0.12, 0.22);
            var root = bed + outward * rng.Range(0.01, 0.03);
            GlasslilyYoungScroll(m, root, outward, reach, rng);
        }

        // The tall flower on its own scape standing proudly above the water line (Y ~ 1.20)
        var flowerRoot = bed + new Vec3(rng.Range(-0.015, 0.015), 0, rng.Range(-0.015, 0.015));
        var flowerBase = flowerRoot + new Vec3(rng.Range(-0.03, 0.03), 1.20, rng.Range(-0.03, 0.03));
        GlasslilyFlower(m, flowerRoot, flowerBase, 1.0, rng);

        // 1-2 closed flower buds
        int budCount = NewFloraCount(m, 2);
        for (int i = 0; i < budCount; i++)
        {
            double a = rng.Range(0, 2.0 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = bed + outward * rng.Range(0.01, 0.035);
            var budBase = root + outward * rng.Range(0.06, 0.12) + Vec3.Up * rng.Range(1.03, 1.08);
            GlasslilyBud(m, root, budBase, 0.048, rng);
        }
    }
}
