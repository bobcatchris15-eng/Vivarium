using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Mooncoin — a peltate, silver-sage groundcover that spreads as a creeping mat.
///
/// Reference. The blade follows the round-leaved mat formers: common pennywort
/// (<i>Hydrocotyle vulgaris</i>) and American pennywort for the peltate disc with its shallowly
/// crenate margin and radiating palmate venation, Chinese money plant (<i>Pilea peperomioides</i>)
/// for the perfectly orbicular blade and the pale waxy boss where the petiole meets the leaf, and
/// white clover (<i>Trifolium repens</i>) for the mat's own construction — creeping runners that root
/// at the nodes, the node spacing, the way a petiole leaves a runner, and the dense shingled canopy
/// a stolon mat actually carries.
///
/// Fantasy. The mat is a xerophytic silver-sage: the blades are glaucous and farina-coated, the
/// grooves between the radiating veins carry a cool moonlace flush, the pip is a pale waxy boss,
/// and a minority of blades carry a bleached silver sector as if moonlight had caught one side of
/// the mat.
///
/// Authoring. The plant is built in world metres and converted to the engine's mesh frame by
/// <see cref="MooncoinToMeshFrame"/>, so what is built here is what the player sees.
///
/// Junctions. Everything that has to look grown rather than assembled is modelled: the runners
/// swell into nodes, the petioles leave those nodes from inside the swelling, the branch forks are
/// collars rather than intersections, the blade closes onto its petiole through a boss, and every
/// node carries the tuft of adventitious roots and the papery bract a real stolon node carries.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------
    // Anatomy constants, world metres.
    // ---------------------------------------------------------------------

    /// <summary>Radius of a creeping runner where it leaves the crown.</summary>
    private const double MooncoinRunnerRadius = 0.00150;
    /// <summary>Radius of a petiole where it leaves the node.</summary>
    private const double MooncoinPetioleRadius = 0.00105;
    /// <summary>Blade thickness of a mature coin.</summary>
    private const double MooncoinBladeThickness = 0.00090;
    /// <summary>Depth of the waxy boss under the blade centre that the petiole plugs into.</summary>
    private const double MooncoinBossDepth = 0.00120;
    /// <summary>Radius of an adventitious root at the node.</summary>
    private const double MooncoinRootRadius = 0.00042;

    /// <summary>Half width of a primary vein ridge, measured across the gap between veins.</summary>
    private const double MooncoinVeinInner = 0.34;
    /// <summary>Where a primary vein ridge has faded out entirely.</summary>
    private const double MooncoinVeinOuter = 0.58;

    /// <summary>
    /// Radial sample positions for the blade, cosine-spaced so the pip and the margin both get
    /// resolution while the middle of the dish stays cheap.
    /// </summary>
    private static double MooncoinRadial(int ring, int rings)
    {
        double t = ring / (double)rings;
        return 0.5 * (1.0 - Math.Cos(Math.PI * t));
    }

    /// <summary>
    /// Per-blade shape and pigment. Every blade gets its own vein count, vein phase, lobe
    /// amplitude, dish depth and tint, so no two coins in the mat are the same object.
    /// </summary>
    private sealed class MooncoinBlade
    {
        public double Radius;        // world metres
        public int Veins = 6;        // primary radiating veins
        public double VeinPhase;
        public double LobeAmp;       // marginal crenation depth, fraction of the radius
        public double FineAmp;       // secondary crenation depth
        public double Dish;          // sag of the blade away from the pip, fraction of radius
        public double PipRise;       // raised collar around the pip, fraction of radius
        public double RimLift;       // lift of the margin, fraction of radius
        public double Waviness;      // low frequency undulation, fraction of radius
        public double Curl;          // overall twist of the disc, fraction of radius
        public double Thickness = MooncoinBladeThickness;
        public double Groove;        // vein groove in the upper face, fraction of the radius
        public double Rib;           // vein rib in the lower face, fraction of the radius
        public double Boss = MooncoinBossDepth;
        public double DamageAngle;   // where the margin has been nibbled
        public double DamageDepth;   // depth of the nibble, fraction of the radius
        public double SilverAngle = -1.0; // centre of a bleached sector, radians
        public double SilverWidth;   // half width of that sector, radians
        public double[] Top = { 0.35, 0.39, 0.29 };
        public double[] Vein = { 0.72, 0.78, 0.66 };
        public double[] Pip = { 0.86, 0.88, 0.80 };
        public double[] Under = { 0.62, 0.64, 0.58 };
        public double[] UnderVein = { 0.50, 0.55, 0.44 };
        public double[] Silver = { 0.82, 0.85, 0.82 };
        public double[] Moonlace = { 0.46, 0.44, 0.58 };
    }

    // ---------------------------------------------------------------------
    // Blade
    // ---------------------------------------------------------------------

    /// <summary>
    /// One peltate coin. The blade is a closed shell: an upper face with the venation cut into it,
    /// a lower face with the same venation standing proud of it, and a thin rolled edge where the
    /// two meet. The venation, the dish, the crenate margin and the pip are all real geometry, so
    /// they hold up under a close camera instead of dissolving into a painted texture.
    /// </summary>
    private static void MooncoinBladeMesh(MeshData m, Vec3 centre, Vec3 up, double spin,
        MooncoinBlade b, int rings, int sectors, ulong seed)
    {
        up = up.Normalized();
        var refDir = new Vec3(Math.Cos(spin), 0, Math.Sin(spin));
        var side = up.Cross(refDir);
        if (side.LengthSq < 1e-9) side = up.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        refDir = side.Cross(up).Normalized();

        // --- surface fields -------------------------------------------------
        // Margin radius: shallow rounded crenations whose tips line up with the primary veins,
        // plus a finer secondary scallop, plus an occasional bite taken out of the rim.
        double RimScale(double theta)
        {
            double lobe = 1.0 + b.LobeAmp * Math.Cos(b.Veins * theta + b.VeinPhase);
            double fine = 1.0 + b.FineAmp * Math.Cos(2.0 * b.Veins * theta + b.VeinPhase * 1.37 + 0.7);
            double scale = lobe * fine;
            if (b.DamageDepth > 0.0)
            {
                double d = MathD.WrapAngle(theta - b.DamageAngle);
                scale -= b.DamageDepth * Math.Exp(-(d * d) / (2.0 * 0.24 * 0.24));
            }
            return scale;
        }

        // Vein field. The primary ribs are explicit ridges measured from the nearest vein rather
        // than a narrow cosine spike, so a coarse tessellation still resolves them; finer
        // secondaries run between them. The mask fades out at the pip, where the veins have not
        // separated yet.
        double Ridge(double theta, int count, double phase, double inner, double outer)
        {
            double a = count * theta + phase;
            double f = a - Math.Floor(a) - 0.5;   // -0.5 .. 0.5, distance in periods
            double d = Math.Abs(f) * 2.0;          // 0 on a vein, 1 midway between veins
            return MathD.SmoothStep(outer, inner, d);
        }

        double VeinMask(double theta, double t)
        {
            double primary = Ridge(theta, b.Veins, b.VeinPhase, MooncoinVeinInner, MooncoinVeinOuter);
            // The primaries fade out towards the margin: in a real peltate blade the ribs are
            // strongest where they leave the pip and soften before they reach the edge.
            double fade = 1.0 - 0.35 * MathD.SmoothStep(0.55, 1.0, t);
            return primary * fade * MathD.SmoothStep(0.0, 0.13, t);
        }

        // Mid surface: the blade sags away from the pip, carries a low collar around the pip,
        // lifts again at the margin and is never perfectly flat.
        double ZMid(double t, double theta)
        {
            double dish = -b.Dish * MathD.SmoothStep(0.0, 0.58, t);
            double pip = b.PipRise * Math.Exp(-Math.Pow((t - 0.15) / 0.23, 2.0));
            double rim = b.RimLift * Math.Pow(MathD.SmoothStep(0.46, 1.0, t), 1.6);
            double wob = b.Waviness * Math.Sin(3.0 * theta + b.VeinPhase * 1.7) * Math.Sin(Math.PI * Math.Min(1.0, t * 1.1));
            double twist = b.Curl * t * t * Math.Sin(theta + b.VeinPhase * 0.6);
            return dish + pip + rim + wob + twist;
        }

        double HalfThickness(double t) => 0.5 * b.Thickness * (1.0 - 0.55 * MathD.SmoothStep(0.84, 1.0, t));
        double Boss(double t) => b.Boss * (1.0 - MathD.SmoothStep(0.0, 0.20, t));

        Vec3 Point(double t, double theta, bool upper)
        {
            double r = t * b.Radius * RimScale(theta);
            double z = ZMid(t, theta) * b.Radius;
            double vm = VeinMask(theta, t);
            // A vein makes the blade locally thicker, so it stands proud of both faces: a shallow
            // crown on top, a deeper rib underneath.
            if (upper) z += HalfThickness(t) + b.Groove * b.Radius * vm;
            else z -= HalfThickness(t) + Boss(t) + b.Rib * b.Radius * vm;
            return centre + refDir * (r * Math.Cos(theta)) + side * (r * Math.Sin(theta)) + up * z;
        }

        // --- tessellate -----------------------------------------------------
        // The upper face carries the full radial resolution; the underside is rarely seen, so it
        // is sampled coarsely and still closes on the same rim.
        int underRings = Math.Max(2, rings / 2);
        var pos = new Vec3[rings + 1, sectors + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = MooncoinRadial(i, rings);
            for (int j = 0; j <= sectors; j++)
                pos[i, j] = Point(t, 2.0 * Math.PI * j / sectors, true);
        }
        var under = new Vec3[underRings + 1, sectors + 1];
        for (int i = 0; i <= underRings; i++)
        {
            double t = MooncoinRadial(i, underRings);
            for (int j = 0; j <= sectors; j++)
                under[i, j] = Point(t, 2.0 * Math.PI * j / sectors, false);
        }

        Vec3 FaceNormal(Vec3[,] p, int last, int i, int j, bool upper)
        {
            int jp = (j + 1) % sectors, jm = (j - 1 + sectors) % sectors;
            Vec3 du, dv;
            if (i == 0) { du = p[1, j] - p[0, j]; dv = p[1, jp] - p[1, jm]; }
            else if (i == last) { du = p[last, j] - p[last - 1, j]; dv = p[last, jp] - p[last, jm]; }
            else { du = p[i + 1, j] - p[i - 1, j]; dv = p[i, jp] - p[i, jm]; }
            Vec3 n = du.Cross(dv);
            if (n.LengthSq < 1e-18) n = upper ? up : -up;
            n = n.Normalized();
            if (upper ? n.Dot(up) < 0 : n.Dot(up) > 0) n = -n;
            return n;
        }

        // --- pigment --------------------------------------------------------
        // Vertex pigment carries the silvering, the pip, the vein paleness and the moonlace flush
        // in the grooves. The shader's own leaf model then tints and lights it.
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        for (int i = 0; i <= rings; i++)
        {
            double t = MooncoinRadial(i, rings);
            double pipMask = 1.0 - MathD.SmoothStep(0.02, 0.07, t);
            double marginMask = MathD.SmoothStep(0.88, 1.0, t);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                double vm = VeinMask(theta, t);
                var col = Primitives.Mix(b.Top, b.Vein, Math.Min(1.0, vm * 0.95));
                // The hollows between the veins keep a cool moonlace flush.
                col = Primitives.Mix(col, b.Moonlace, (1.0 - vm) * 0.10 * (1.0 - pipMask));
                col = Primitives.Mix(col, b.Pip, pipMask * 0.70);

                // Farina: the dusty silver film a glaucous leaf wears, thickest away from the
                // veins and strongest toward the dry margin.
                double bloom = 0.5 + 0.5 * Noise.Fbm(seed + 71, Math.Cos(theta) * t * 1.1 + 5.1,
                    Math.Sin(theta) * t * 1.1 - 2.7, 2);
                col = Primitives.Mix(col, b.Silver, bloom * 0.09 * (1.0 - vm * 0.6));
                // The fine reticulation between the primaries.
                double cell = Noise.Fbm(seed + 313, Math.Cos(theta) * t * 6.5, Math.Sin(theta) * t * 6.5, 3);
                if (cell > 0.0) col = Primitives.Mix(col, b.Vein, cell * 0.20 * (1.0 - vm));
                col = Primitives.Mix(col, Primitives.Mix(b.Top, b.Under, 0.45), marginMask * 0.40);

                // Sun-bleached sector: one side of the blade has gone silver.
                if (b.SilverAngle >= 0.0)
                {
                    double d = Math.Abs(MathD.WrapAngle(theta - b.SilverAngle));
                    float silver = (float)(1.0 - MathD.SmoothStep(b.SilverWidth * 0.55, b.SilverWidth, d));
                    silver *= (float)MathD.SmoothStep(0.02, 0.22, t);
                    if (silver > 0.001f) col = Primitives.Mix(col, b.Silver, silver * 0.45);
                }

                // Fine tonal drift so neighbouring blades never read as copies.
                double drift = Noise.Gradient(seed, Math.Cos(theta) * t * 3.1, Math.Sin(theta) * t * 3.1);
                double shade = 1.0 + 0.10 * drift - 0.06 * MathD.SmoothStep(0.55, 1.0, t);
                col = new[] { col[0] * shade, col[1] * shade, col[2] * shade };

                var n = FaceNormal(pos, rings, i, j, true);
                // UV.x carries only a whisper of the angular coordinate: enough to keep the
                // shader's procedural detail two dimensional, far too little to trigger its
                // pinnate vein model, which does not apply to a peltate blade.
                double u = 0.026 * (j / (double)sectors);
                if (j == sectors) u = 0.0;
                m.AddVertex(pos[i, j], n, col, 1.0, u, 0.5 + 0.5 * t, 0.0, 1.0);
            }
        }

        for (int i = 0; i <= underRings; i++)
        {
            double t = MooncoinRadial(i, underRings);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                double vm = VeinMask(theta, t);
                // The underside of a pennywort is much paler than its face, with the veins standing
                // proud of it and a faint blush around the attachment.
                var col = Primitives.Mix(b.Under, b.UnderVein, Math.Min(1.0, vm * 0.95));
                col = Primitives.Mix(col, Primitives.Mix(b.Under, b.Pip, 0.30), 0.30 * (1.0 - MathD.SmoothStep(0.0, 0.26, t)));
                double drift = Noise.Gradient(seed + 977, Math.Cos(theta) * t * 2.7, Math.Sin(theta) * t * 2.7);
                double shade = 1.0 + 0.07 * drift;
                col = new[] { col[0] * shade, col[1] * shade, col[2] * shade };

                var n = FaceNormal(under, underRings, i, j, false);
                double u = 0.026 * (j / (double)sectors);
                if (j == sectors) u = 0.0;
                m.AddVertex(under[i, j], n, col, 1.0, u, 0.5 + 0.5 * t, 1.0, 1.0);
            }
        }

        int stride = sectors + 1;
        int topBase = leafVertex;
        int underBase = leafVertex + (rings + 1) * stride;

        // Upper face. The innermost band closes onto the single centre point, so it is a fan of
        // triangles rather than a quad strip: the duplicated centre ring would otherwise emit a
        // zero-area triangle at every sector.
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                int a = topBase + i * stride + j, b1 = topBase + (i + 1) * stride + j;
                int c = topBase + (i + 1) * stride + j + 1, d = topBase + i * stride + j + 1;
                Primitives.TriangleFacing(m, a, b1, c, up);
                if (i > 0) Primitives.TriangleFacing(m, a, c, d, up);
            }

        // Lower face, wound the other way.
        for (int i = 0; i < underRings; i++)
            for (int j = 0; j < sectors; j++)
            {
                int a = underBase + i * stride + j, b1 = underBase + (i + 1) * stride + j;
                int c = underBase + (i + 1) * stride + j + 1, d = underBase + i * stride + j + 1;
                Primitives.TriangleFacing(m, a, c, b1, -up);
                if (i > 0) Primitives.TriangleFacing(m, a, d, c, -up);
            }

        // The edge: a thin, slightly rolled rim joining the two faces. Real blades have a visible
        // thickness at the margin, and it is the pale underside colour that shows there.
        var edgeCol = Primitives.Mix(b.Under, b.Pip, 0.22);
        for (int j = 0; j < sectors; j++)
        {
            int t0 = topBase + rings * stride + j, t1 = topBase + rings * stride + j + 1;
            int b0 = underBase + underRings * stride + j, b1 = underBase + underRings * stride + j + 1;
            double theta = 2.0 * Math.PI * (j + 0.5) / sectors;
            var outward = (refDir * Math.Cos(theta) + side * Math.Sin(theta)).Normalized();
            int e0 = m.AddVertex(m.Position(t0), outward, edgeCol, 1.0, 0.02 * (j / (double)sectors), 1.0, 1.0, 1.0);
            int e1 = m.AddVertex(m.Position(t1), outward, edgeCol, 1.0, 0.02 * ((j + 1) / (double)sectors), 1.0, 1.0, 1.0);
            int e2 = m.AddVertex(m.Position(b1), outward, edgeCol, 1.0, 0.02 * ((j + 1) / (double)sectors), 1.0, 1.0, 1.0);
            int e3 = m.AddVertex(m.Position(b0), outward, edgeCol, 1.0, 0.02 * (j / (double)sectors), 1.0, 1.0, 1.0);
            Primitives.TriangleFacing(m, e0, e1, e2, outward);
            Primitives.TriangleFacing(m, e0, e2, e3, outward);
        }

        m.RecordLeaf(leafVertex, leafIndex, centre, b.Radius);
    }

    // ---------------------------------------------------------------------
    // Nodes, runners, roots
    // ---------------------------------------------------------------------

    /// <summary>
    /// A node on a creeping runner: the swelling where the runner roots, the adventitious roots
    /// that peg it to the soil, and the papery bract that sheathes the petiole bases.
    /// </summary>
    private static void MooncoinNode(MeshData m, Rng rng, ulong seed, Vec3 node, Vec3 tangent,
        double[] rootCol, double[] bractCol)
    {
        // Root tuft: fine roots leaving the node in every direction and diving into the soil.
        int roots = rng.NextDouble() < 0.45 ? 3 : 2;
        for (int r = 0; r < roots; r++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double len = rng.Range(0.0045, 0.0125);
            var p0 = node + dir * 0.0004 - new Vec3(0, 0.0004, 0);
            var p1 = node + dir * (len * 0.45) - new Vec3(0, 0.0034, 0);
            var p2 = node + dir * len - new Vec3(0, 0.0098, 0);
            Primitives.Tube(m, new[] { p0, p1, p2 },
                new[] { MooncoinRootRadius, MooncoinRootRadius * 0.56, MooncoinRootRadius * 0.18 }, 4,
                (i, v) => (rootCol, 1, i, v, 0, 0));
        }

        // Node bract: a small reduced leaf folded against the runner.
        if (rng.NextDouble() < 0.55)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var spread = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var across = Vec3.Up.Cross(spread).Normalized();
            double len = rng.Range(0.0032, 0.0056);
            double width = len * rng.Range(0.28, 0.40);
            var root = node + spread * 0.0009 + new Vec3(0, 0.0004, 0);
            var mid = root + spread * (len * 0.42) + new Vec3(0, len * 0.42, 0);
            var tip = root + spread * (len * 0.80) + new Vec3(0, len * 0.74, 0);
            int s0 = m.VertexCount;
            m.AddVertex(root - across * width, across, bractCol, 1.0, 0.0, 0.0, 0.0, 0.0);
            m.AddVertex(root + across * width, across, bractCol, 1.0, 0.0, 1.0, 0.0, 0.0);
            m.AddVertex(mid - across * (width * 0.82), across, bractCol, 1.0, 0.5, 0.0, 0.0, 0.0);
            m.AddVertex(mid + across * (width * 0.82), across, bractCol, 1.0, 0.5, 1.0, 0.0, 0.0);
            m.AddVertex(tip, spread, bractCol, 1.0, 1.0, 0.5, 0.0, 0.0);
            Primitives.TriangleFacing(m, s0, s0 + 2, s0 + 1, spread);
            Primitives.TriangleFacing(m, s0 + 1, s0 + 2, s0 + 3, spread);
            Primitives.TriangleFacing(m, s0 + 2, s0 + 4, s0 + 3, spread);
        }
    }

    /// <summary>
    /// A petiole leaving a node, carrying one coin at its tip. The petiole starts inside the
    /// runner so the two read as one growth, thins as it climbs and flares where it opens into the
    /// blade's boss.
    /// </summary>
    private static void MooncoinPetiole(MeshData m, Rng rng, ulong seed, Vec3 node, Vec3 tangent,
        double petioleHeight, double spread, double[] runnerCol, double[] tipCol, MooncoinBlade blade,
        int rings, int sectors, out Vec3 leafCentre, out Vec3 leafUp)
    {
        double az = rng.Range(0, 2 * Math.PI);
        var lean = new Vec3(Math.Cos(az), 0, Math.Sin(az));

        // The blade tips away from the vertical, in the direction the petiole leans.
        double tilt = rng.Range(0.04, 0.32);
        leafUp = (Vec3.Up * Math.Cos(tilt) + lean * Math.Sin(tilt)).Normalized();

        // Petiole path: it leaves the node from inside the runner, stands up, then arches out
        // under the blade the way a pennywort's long stalk does.
        var path = new Vec3[5];
        var radii = new double[5];
        path[0] = node - tangent * 0.0034 - new Vec3(0, 0.0004, 0);
        path[1] = node + tangent * 0.0004 + new Vec3(0, 0.0012, 0);
        path[2] = node + new Vec3(0, petioleHeight * 0.34, 0) + lean * (spread * 0.10);
        path[3] = node + new Vec3(0, petioleHeight * 0.72, 0) + lean * (spread * 0.46);
        path[4] = node + new Vec3(0, petioleHeight, 0) + lean * spread;
        double petioleBase = MooncoinPetioleRadius * rng.Range(0.80, 1.05);
        radii[0] = petioleBase * 1.06;
        radii[1] = petioleBase * 0.92;
        radii[2] = petioleBase * 0.78;
        radii[3] = petioleBase * 0.70;
        radii[4] = petioleBase * 1.16; // flares open where it meets the blade

        Primitives.Tube(m, path, radii, 6, (i, v) => (Primitives.Mix(runnerCol, tipCol, i / 4.0), 1, i, v, 0, 0));

        // The blade sits so that the petiole's flared tip is buried in the boss under its centre.
        double halfT = 0.5 * blade.Thickness;
        double bossTip = -halfT - blade.Boss; // boss tip height relative to the blade centre
        leafCentre = path[4] - leafUp * (bossTip + blade.Boss * 0.42);

        MooncoinBladeMesh(m, leafCentre, leafUp, rng.Range(0, 2 * Math.PI), blade, rings, sectors,
            Rng.Mix(seed, (ulong)(rng.NextULong() & 0xFFFF)));
    }

    /// <summary>
    /// A creeping runner. It hugs the soil, swells where it roots, and forks into a branch that
    /// leaves through a collar rather than an intersection.
    /// </summary>
    private static void MooncoinRunner(MeshData m, Rng rng, ulong seed, Vec3 origin, double startAz,
        double reach, int nodeCount, double[] runnerCol, double[] tipCol, double[] rootCol,
        double[] bractCol, int rings, int sectors, Action<Vec3, Vec3> onNode)
    {
        const int steps = 11;
        double meander = rng.Range(0, 2 * Math.PI);
        var path = new Vec3[steps + 1];
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double ang = startAz + 0.30 * Math.Sin(t * 3.1 + meander) + 0.13 * Math.Sin(t * 7.7 + meander * 1.9);
            double r = t * reach;
            // The runner undulates over the soil instead of lying on a perfect plane.
            double y = 0.0017 + 0.0011 * Math.Sin(t * 5.7 + meander) + 0.0007 * Math.Sin(t * 11.3 + meander * 2.3);
            path[s] = origin + new Vec3(Math.Cos(ang) * r, y, Math.Sin(ang) * r);
        }

        // Node steps: spread along the runner, never at either end.
        var nodeSteps = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            nodeSteps[i] = 2 + (int)Math.Round((i + 0.85) * (steps - 3.0) / nodeCount);

        // The runner swells at each node: that swelling is the root crown and the collar the
        // petioles come out of.
        var radii = new double[steps + 1];
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double r = MooncoinRunnerRadius * (1.0 - 0.28 * t);
            foreach (int ns in nodeSteps)
                r *= 1.0 + 0.58 * Math.Exp(-Math.Pow((s - ns) / 1.05, 2.0));
            radii[s] = r;
        }

        Primitives.Tube(m, path, radii, 6, (i, v) => (Primitives.Mix(runnerCol, tipCol, i / (double)steps), 1, i, v, 0, 0));

        // Fine bristles along the runner. A pennywort stolon is clothed in root hairs, and the
        // little bottle-brush fringe is most of what tells you at close range that this is a
        // creeping stolon and not a wire.
        for (int s = 1; s < steps; s++)
        {
            if (rng.NextDouble() > 0.55) continue;
            var p = path[s];
            var dir = (path[s + 1] - path[s - 1]).Normalized();
            int hairs = 2 + rng.NextInt(3);
            for (int h = 0; h < hairs; h++)
            {
                double a = rng.Range(0, 2 * Math.PI);
                var outDir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) + dir * 0.55).Normalized();
                double len = rng.Range(0.0011, 0.0026);
                var h0 = p + outDir * (radii[s] * 0.55);
                var h1 = h0 + outDir * len + new Vec3(0, len * 0.30, 0);
                Primitives.Tube(m, new[] { h0, h1 }, new[] { 0.00019, 0.00007 }, 4,
                    (i, v) => (rootCol, 1, i, v, 0, 0));
            }
        }

        foreach (int ns in nodeSteps)
        {
            var node = path[ns];
            var tangent = (path[Math.Min(steps, ns + 1)] - path[Math.Max(0, ns - 1)]).Normalized();
            MooncoinNode(m, rng, seed, node, tangent, rootCol, bractCol);
            onNode(node, tangent);
        }

        // One fork per runner. The branch starts slightly behind the fork point so it grows out of
        // the parent instead of being welded onto its side.
        if (rng.NextDouble() < 0.60)
        {
            int forkStep = nodeSteps[rng.NextInt(nodeCount)];
            forkStep = Math.Clamp(forkStep, 1, steps - 2);
            var forkPos = path[forkStep];
            var forkTangent = (path[Math.Min(steps, forkStep + 1)] - path[Math.Max(0, forkStep - 1)]).Normalized();
            double side = rng.NextDouble() < 0.5 ? 1.0 : -1.0;
            double forkAz = startAz + side * rng.Range(0.55, 0.95);
            double forkReach = reach * rng.Range(0.30, 0.48);

            const int bSteps = 5;
            var bPath = new Vec3[bSteps + 1];
            var bRadii = new double[bSteps + 1];
            var start = forkPos - forkTangent * 0.0050; // overlap the parent
            for (int bs = 0; bs <= bSteps; bs++)
            {
                double bt = bs / (double)bSteps;
                double ang = forkAz + 0.22 * Math.Sin(bt * 3.4 + meander);
                double r = bt * forkReach;
                double y = 0.0016 + 0.0009 * Math.Sin(bt * 5.0 + meander);
                bPath[bs] = start + new Vec3(Math.Cos(ang) * r, y, Math.Sin(ang) * r);
                bRadii[bs] = MooncoinRunnerRadius * 0.92 * (1.0 - 0.28 * bt);
            }
            Primitives.Tube(m, bPath, bRadii, 6, (i, v) => (Primitives.Mix(runnerCol, tipCol, 0.5 + 0.5 * i / bSteps), 1, i, v, 0, 0));

            // The collar that hides the union of the two runners.
            Primitives.Ellipsoid(m, forkPos, new Vec3(radii[forkStep] * 1.35, radii[forkStep] * 1.25, radii[forkStep] * 1.35), 4, 7,
                (u, v) => (runnerCol, 1, u, v, 0, 0));

            int bNodes = 2;
            for (int bn = 1; bn <= bNodes; bn++)
            {
                int idx = Math.Clamp(2 + bn * (bSteps - 2) / (bNodes + 1), 1, bSteps);
                var node = bPath[idx];
                var tangent = (bPath[Math.Min(bSteps, idx + 1)] - bPath[Math.Max(0, idx - 1)]).Normalized();
                MooncoinNode(m, rng, seed, node, tangent, rootCol, bractCol);
                onNode(node, tangent);
            }
        }
    }

    /// <summary>
    /// The tiny umbel a pennywort throws above the mat: a slender peduncle carrying a tight head
    /// of minute green-white florets. Rare, and small enough to read as a detail rather than a
    /// feature.
    /// </summary>
    private static void MooncoinUmbel(MeshData m, Rng rng, ulong seed, Vec3 basePoint, double height,
        double[] stemCol, double[] floretCol)
    {
        const int steps = 5;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double lean = 0.0016 * Math.Sin(t * 2.6 + seed % 7);
            path[i] = basePoint + new Vec3(lean, height * Math.Pow(t, 0.92), lean * 0.6);
            radii[i] = 0.00048 * (1.0 - 0.34 * t);
        }
        Primitives.Tube(m, path, radii, 5, (i, v) => (stemCol, 1, i, v, 0, 0));

        var top = path[steps];
        int florets = 6 + rng.NextInt(4);
        for (int f = 0; f < florets; f++)
        {
            double theta = f * 2.399963229728653;
            double ring = 0.35 + 0.65 * (f / (double)Math.Max(1, florets - 1));
            var dir = new Vec3(Math.Cos(theta) * ring, 0.55 - 0.45 * ring, Math.Sin(theta) * ring).Normalized();
            double size = rng.Range(0.00055, 0.00095);
            var centre = top + dir * (0.0009 + rng.Range(0.0, 0.0011));
            var up = dir;
            var sideA = Vec3.Up.Cross(up);
            if (sideA.LengthSq < 1e-9) sideA = new Vec3(1, 0, 0);
            sideA = sideA.Normalized();
            var sideB = up.Cross(sideA).Normalized();

            // A five-pointed floret: five petals around a tiny centre.
            int start = m.VertexCount;
            m.AddVertex(centre, up, floretCol, 1.0, 0.5, 0.5, 0.0, 0.0);
            for (int k = 0; k <= 5; k++)
            {
                double a = 2 * Math.PI * k / 5.0;
                double petal = 1.0 + 0.35 * Math.Cos(5.0 * a);
                var p = centre + (sideA * Math.Cos(a) + sideB * Math.Sin(a)) * (size * petal) + up * (size * 0.18);
                m.AddVertex(p, up, floretCol, 1.0, 0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a), 0.0, 0.0);
            }
            for (int k = 0; k < 5; k++)
                Primitives.TriangleFacing(m, start, start + 1 + k, start + 2 + k, up);
        }
    }

    // ---------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------

    /// <summary>
    /// Mooncoin. A creeping silver-sage mat: runners that root at the nodes, a coin at every
    /// node, and a dense shingled canopy of peltate blades that closes over the warm open soil.
    /// </summary>
    private static void Mooncoin(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        // A caller that has no authored size still gets a properly proportioned plant rather than
        // a collapsed one.
        double matSpan = radius > 0.01 ? radius : 0.20;
        double plantHeight = height > 0.01 ? height : 0.06;

        var runnerCol = new[] { 0.40, 0.34, 0.26 };   // warm bronze-olive wiry runner
        var runnerTip = new[] { 0.52, 0.55, 0.38 };
        var petioleCol = new[] { 0.46, 0.50, 0.30 };  // the petiole is paler and greener than the runner
        var rootCol = new[] { 0.34, 0.29, 0.22 };
        var bractCol = new[] { 0.58, 0.55, 0.42 };    // papery, bleached node bract
        var floretCol = new[] { 0.90, 0.91, 0.82 };

        // Blade tessellation. The peltate venation needs enough angular samples for the ribs to
        // read and enough radial rings for the dish, the pip collar and the lifted margin to be
        // smooth rather than faceted.
        int rings = 5, sectors = 30;
        if (m.Lod is { } lod)
        {
            rings = lod.Segments(rings, 4);
            sectors = lod.Segments(sectors, 12);
        }

        // Blade pigments: a cool sage face, a paler silver underside, and a waxy pip.
        var bladeTop = Primitives.Mix(Primitives.Scale(Primitives.Mix(c1, c2, 0.32), 0.70),
            new[] { 0.62, 0.66, 0.60 }, 0.55);
        var bladeVein = Primitives.Mix(bladeTop, new[] { 0.70, 0.73, 0.66 }, 0.50);
        var bladePip = new[] { 0.82, 0.84, 0.76 };
        var bladeUnder = Primitives.Mix(c2, new[] { 0.78, 0.79, 0.73 }, 0.52);
        var bladeUnderVein = Primitives.Mix(c2, new[] { 0.44, 0.49, 0.37 }, 0.70);
        var bladeSilver = new[] { 0.82, 0.85, 0.82 };
        var bladeMoonlace = Primitives.Mix(bladeTop, new[] { 0.50, 0.47, 0.62 }, 0.55);

        // A blade factory: every coin is a little different in size, vein count, dish and tint.
        MooncoinBlade NewBlade(Rng br, double sizeScale)
        {
            var b = new MooncoinBlade
            {
                Radius = br.Range(0.0150, 0.0340) * sizeScale,
                Veins = 7 + br.NextInt(2),                 // 7..8 primary radiating veins
                VeinPhase = br.Range(0, 2 * Math.PI),
                // Broad, shallow, rounded lobes: a pennywort margin is scalloped, not toothed.
                LobeAmp = br.Range(0.035, 0.068),
                FineAmp = br.Range(0.003, 0.011),
                Dish = br.Range(0.035, 0.070),
                PipRise = br.Range(0.010, 0.022),
                RimLift = br.Range(0.018, 0.038),
                Waviness = br.Range(0.005, 0.014),
                Curl = br.Range(-0.022, 0.022),
                Groove = br.Range(0.012, 0.020),
                Rib = br.Range(0.020, 0.034),
                Top = bladeTop,
                Vein = bladeVein,
                Pip = bladePip,
                Under = bladeUnder,
                UnderVein = bladeUnderVein,
                Silver = bladeSilver,
                Moonlace = bladeMoonlace,
            };
            // Older outer coins have been nibbled and have gone a little dull at the rim.
            if (br.NextDouble() < 0.30)
            {
                b.DamageAngle = br.Range(0, 2 * Math.PI);
                b.DamageDepth = br.Range(0.03, 0.09);
            }
            // A minority of blades carry a bleached silver sector.
            if (br.NextDouble() < 0.20)
            {
                b.SilverAngle = br.Range(0, 2 * Math.PI);
                b.SilverWidth = br.Range(0.5, 1.35);
            }
            return b;
        }

        // --- crown ---------------------------------------------------------
        // Every runner leaves the same small crown boss, so the mat has a single heart.
        Primitives.Ellipsoid(part, new Vec3(0, 0.0016, 0), new Vec3(0.0040, 0.0024, 0.0040), 4, 8,
            (u, v) => (runnerCol, 1, u, v, 0, 0));

        // --- runners and their coins ---------------------------------------
        int runners = 6 + rng.NextInt(3);
        double matRadius = matSpan * 0.96;
        for (int k = 0; k < runners; k++)
        {
            double baseAz = 2 * Math.PI * k / runners + rng.Range(-0.22, 0.22);
            double reach = matRadius * rng.Range(0.80, 1.04);
            double nodeScale = rng.Range(0.85, 1.15);
            MooncoinRunner(part, rng, seed, new Vec3(rng.Range(-0.002, 0.002), 0.0012, rng.Range(-0.002, 0.002)),
                baseAz, reach, 3 + rng.NextInt(2), runnerCol, runnerTip, rootCol, bractCol,
                rings, sectors,
                (node, tangent) =>
                {
                    int petioles = rng.NextDouble() < 0.18 ? 2 : 1;
                    for (int p = 0; p < petioles; p++)
                    {
                        var blade = NewBlade(rng, nodeScale);
                        double petH = plantHeight * rng.Range(0.38, 0.88) * nodeScale;
                        double spread = blade.Radius * rng.Range(0.12, 0.45);
                        MooncoinPetiole(part, rng, seed, node, tangent, petH, spread,
                            petioleCol, runnerTip, blade, rings, sectors, out _, out _);
                    }
                });
        }

        // --- crown coins ----------------------------------------------------
        int crownLeaves = 7 + rng.NextInt(3);
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2 * Math.PI * c / crownLeaves + rng.Range(-0.30, 0.30);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var blade = NewBlade(rng, 1.08);
            double dist = matRadius * rng.Range(0.02, 0.26);
            double petH = plantHeight * rng.Range(0.40, 0.88);
            double spread = blade.Radius * rng.Range(0.12, 0.42);

            var path = new Vec3[5];
            var radii = new double[5];
            path[0] = new Vec3(0, 0.0010, 0) - dir * 0.0026;
            path[1] = new Vec3(0, 0.0016, 0) + dir * 0.0008;
            path[2] = dir * (dist * 0.30) + new Vec3(0, petH * 0.42, 0);
            path[3] = dir * (dist * 0.70) + new Vec3(0, petH * 0.78, 0);
            path[4] = dir * dist + new Vec3(0, petH, 0) + dir * spread;
            double petioleBase = MooncoinPetioleRadius * rng.Range(0.9, 1.15);
            radii[0] = petioleBase * 1.04; radii[1] = petioleBase * 0.94; radii[2] = petioleBase * 0.82;
            radii[3] = petioleBase * 0.78; radii[4] = petioleBase * 1.18;
            Primitives.Tube(part, path, radii, 6, (i, v) => (Primitives.Mix(petioleCol, runnerTip, i / 4.0), 1, i, v, 0, 0));

            double tilt = rng.Range(0.04, 0.34);
            var leafUp = (Vec3.Up * Math.Cos(tilt) + dir * Math.Sin(tilt)).Normalized();
            double halfT = 0.5 * blade.Thickness;
            double bossTip = -halfT - blade.Boss;
            var leafCentre = path[4] - leafUp * (bossTip + blade.Boss * 0.42);
            MooncoinBladeMesh(part, leafCentre, leafUp, rng.Range(0, 2 * Math.PI), blade, rings, sectors,
                Rng.Mix(seed, (ulong)(c * 911 + 7)));
        }

        // --- a couple of flower heads ---------------------------------------
        for (int f = 0; f < 2; f++)
        {
            double fa = rng.Range(0, 2 * Math.PI);
            double fr = matRadius * rng.Range(0.18, 0.55);
            var baseP = new Vec3(Math.Cos(fa) * fr, 0.0016, Math.Sin(fa) * fr);
            MooncoinUmbel(part, rng, seed, baseP, plantHeight * rng.Range(0.72, 0.95), runnerCol, floretCol);
        }

        MooncoinToMeshFrame(m, part, matSpan, plantHeight);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void MooncoinToMeshFrame(MeshData target, MeshData part, double radius, double height)
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
        // classifying petioles as petioles rather than as structural wood.
        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }
}
