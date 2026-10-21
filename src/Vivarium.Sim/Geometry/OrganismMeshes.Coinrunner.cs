using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Coinrunner — a fast, wide-running green groundcover that stitches open ground together with
/// creeping runners and reniform coins.
///
/// Reference. The mat is built from the round-leaved mat formers: moneywort and marsh pennywort
/// (<i>Hydrocotyle vulgaris</i>, <i>H. americana</i>) for the creeping stolon that roots at the
/// nodes, the long petiole, the round leaf held flat at the end of it and the minute umbel thrown
/// above the mat; kidneyweed (<i>Dichondra repens</i>) for the reniform blade itself, with its deep
/// basal sinus and finely crenate margin. White clover (<i>Trifolium repens</i>) informs the mat's
/// construction — the runner that swells at the node, the rooting tuft, the reduced bract and the
/// dense shingled canopy a stolon mat actually carries.
///
/// Fantasy. The blades read like old coins gone green: a mottled verdigris patina with warm bronze
/// showing through where the patina has thinned, pale gold tracery along the radiating veins, a
/// pale mint reverse, and a raised circular bead just inside the crenate margin — the raised
/// border of a struck coin, with the crenations breaking through it at the margin like a milled
/// edge. The leading tip of every runner carries a warm sap-light, because this is the plant that
/// is always stitching new ground into the mat.
///
/// How it differs from Mooncoin, which shares the pennywort reference. Mooncoin is a slow, tight
/// mat of perfectly peltate orbicular discs: thick, waxy, farina-coated, silver-sage, on long
/// slender petioles. Coinrunner is the opposite end of the family: a wide, fast mat of reniform
/// kidney blades with a basal sinus: large, thin, glossy, emerald, on shorter stouter petioles.
///
/// Authoring. The plant is built in world metres and converted to the engine's mesh frame by
/// <see cref="AppendCoinrunnerWorld"/>, so what is built here is what the player sees.
///
/// Junctions. Everything that has to look grown rather than assembled is modelled: the runners
/// swell into nodes, the petioles leave those nodes from inside the swelling, the blade closes
/// onto its petiole through the sinus it is notched for, and every node carries the tuft of
/// adventitious roots and the papery bract a real stolon node carries.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------
    // Anatomy constants, world metres.
    // ---------------------------------------------------------------------

    /// <summary>Radius of a creeping runner where it leaves the crown.</summary>
    private const double CoinrunnerRunnerRadius = 0.00165;
    /// <summary>Radius of a petiole where it leaves the node.</summary>
    private const double CoinrunnerPetioleRadius = 0.00125;
    /// <summary>Radius of an adventitious root at the node.</summary>
    private const double CoinrunnerRootRadius = 0.00046;

    /// <summary>How far the blade's disc centre sits from the petiole attachment, as a fraction of
    /// the blade radius. This is what makes the blade reniform rather than peltate.</summary>
    private const double CoinrunnerSinusOffset = 0.42;
    /// <summary>Angular half width of the basal sinus.</summary>
    private const double CoinrunnerSinusWidth = 0.34;

    /// <summary>
    /// Radial sample positions for the blade, cosine-spaced so the attachment end and the margin
    /// both get resolution while the middle of the dish stays cheap.
    /// </summary>
    private static double CoinrunnerRadial(int ring, int rings)
    {
        double t = ring / (double)rings;
        return 0.5 * (1.0 - Math.Cos(Math.PI * t));
    }

    /// <summary>
    /// Per-blade shape and pigment. Every blade gets its own vein count, vein phase, sinus depth,
    /// margin crenation and patina, so no two coins in the mat are the same object.
    /// </summary>
    private sealed class CoinrunnerBlade
    {
        public double Radius;        // world metres
        public int Veins = 6;        // primary veins fanning out of the sinus
        public double VeinPhase;
        public double VeinAlpha;     // where the fan of veins starts, radians
        public double SinusOffset;   // fraction of the radius
        public double CrenAmp;       // marginal crenation depth, fraction of the radius
        public int Crenations = 13;
        public double CrenPhase;
        public double Dish;          // sag of the blade, fraction of the radius
        public double RimLift;       // lift of the margin, fraction of the radius
        public double Bead;          // the coin's raised border, fraction of the radius
        public double BeadWidth;
        public double Waviness;
        public double Thickness;     // fraction of the radius; Coinrunner's blades are thin
        public double Groove;        // vein groove in the upper face, fraction of the radius
        public double Rib;           // vein rib in the lower face, fraction of the radius
        public double DamageAngle;
        public double DamageDepth;
        public double Bronze;        // how much bare bronze shows through the patina
        public double[] Top = { 0.36, 0.72, 0.26 };
        public double[] Verdigris = { 0.30, 0.55, 0.46 };
        public double[] BronzeCol = { 0.62, 0.40, 0.16 };
        public double[] Vein = { 0.80, 0.78, 0.52 };
        public double[] Under = { 0.70, 0.76, 0.66 };
        public double[] UnderVein = { 0.58, 0.66, 0.40 };
    }

    // ---------------------------------------------------------------------
    // Blade
    // ---------------------------------------------------------------------

    /// <summary>
    /// One reniform coin. The blade is a closed shell: an upper face with the venation cut into
    /// it, a lower face with the same venation standing proud of it, and a thin rolled edge where
    /// the two meet. The notch at the base is a real sinus, not a painted one, and the petiole
    /// plugs into it.
    /// </summary>
    private static void CoinrunnerBladeMesh(MeshData m, Vec3 origin, Vec3 up, Vec3 body,
        CoinrunnerBlade b, int rings, int sectors, ulong seed)
    {
        up = up.Normalized();
        body = body - up * body.Dot(up);
        if (body.LengthSq < 1e-9) body = new Vec3(1, 0, 0);
        body = body.Normalized();
        var side = up.Cross(body).Normalized();
        body = side.Cross(up).Normalized();

        // The disc centre sits away from the attachment, which is what turns the blade into a
        // kidney rather than a coin.
        double offset = b.SinusOffset * b.Radius;
        var centre = origin + body * offset;

        // --- surface fields -------------------------------------------------
        // Basal sinus: the outline dives in to the attachment point and comes back out again.
        double Sinus(double theta)
        {
            double d = MathD.WrapAngle(theta + Math.PI * 0.5);
            double k = Math.Clamp(1.0 - Math.Abs(d) / CoinrunnerSinusWidth, 0.0, 1.0);
            return Math.Sqrt(k);
        }

        double RimScale(double theta)
        {
            double notch = Sinus(theta);
            double depth = 1.0 - b.SinusOffset;
            double scale = (1.0 - depth * notch);
            // Finely crenate margin: the milled edge of the coin.
            double cren = 1.0 + b.CrenAmp * Math.Cos(b.Crenations * theta + b.CrenPhase);
            scale *= cren;
            if (b.DamageDepth > 0.0)
            {
                double d = MathD.WrapAngle(theta - b.DamageAngle);
                scale -= b.DamageDepth * Math.Exp(-(d * d) / (2.0 * 0.26 * 0.26));
            }
            return scale;
        }

        // Vein field: the veins fan out of the sinus, so they are spaced by the angle they make
        // at the attachment rather than around the disc centre.
        double VeinMask(double alpha, double dist)
        {
            double step = Math.PI / b.Veins;
            double a = (alpha - b.VeinAlpha) / step;
            double f = a - Math.Floor(a) - 0.5;
            double d = Math.Abs(f) * 2.0;
            double ridge = MathD.SmoothStep(0.62, 0.30, d);
            return ridge * MathD.SmoothStep(0.05, 0.20, dist);
        }

        // The coin's raised border: a bead just inside the margin, interrupted where the sinus
        // cuts back to the attachment.
        double BeadMask(double t, double theta)
        {
            double ring = MathD.SmoothStep(0.66, 0.78, t) * (1.0 - MathD.SmoothStep(0.87, 0.97, t));
            return ring * (1.0 - Sinus(theta));
        }

        double ZMid(double t, double theta)
        {
            double dish = -b.Dish * MathD.SmoothStep(0.0, 0.62, t);
            double rim = b.RimLift * Math.Pow(MathD.SmoothStep(0.55, 1.0, t), 1.6);
            // Two harmonics of undulation: the blade is never a flat plate, and the fine wrinkle is
            // what breaks a leaf highlight up into something that reads as tissue.
            double wob = b.Waviness * Math.Sin(3.0 * theta + b.CrenPhase * 1.7) * Math.Sin(Math.PI * Math.Min(1.0, t * 1.1));
            double wrinkle = b.Waviness * 0.42 * Math.Sin(7.0 * theta + b.CrenPhase * 2.3) * Math.Sin(Math.PI * Math.Min(1.0, t * 1.2));
            return dish + rim + wob + wrinkle;
        }

        double HalfThickness(double t) => 0.5 * b.Thickness * b.Radius * (1.0 - 0.45 * MathD.SmoothStep(0.86, 1.0, t));

        // A point on the blade, with its angle at the attachment and its distance from it.
        Vec3 Surface(double t, double theta, bool upper, out double alpha, out double dist)
        {
            double r = t * b.Radius * RimScale(theta);
            double z = ZMid(t, theta) * b.Radius;
            double sb = offset + r * Math.Sin(theta);
            double sc = r * Math.Cos(theta);
            alpha = Math.Atan2(sb, sc);
            dist = Math.Sqrt(sb * sb + sc * sc) / b.Radius;
            double vm = VeinMask(alpha, dist);
            double bead = b.Bead * b.Radius * BeadMask(t, theta);
            z += bead;
            if (upper) z += HalfThickness(t) - b.Groove * b.Radius * vm;
            else z -= HalfThickness(t) + b.Rib * b.Radius * vm;
            return origin + body * sb + side * sc + up * z;
        }

        var pos = new Vec3[rings + 1, sectors + 1];
        var alpha = new double[rings + 1, sectors + 1];
        var dist = new double[rings + 1, sectors + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = CoinrunnerRadial(i, rings);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                pos[i, j] = Surface(t, theta, true, out alpha[i, j], out dist[i, j]);
            }
        }
        int underRings = Math.Max(2, rings / 2);
        var under = new Vec3[underRings + 1, sectors + 1];
        var uAlpha = new double[underRings + 1, sectors + 1];
        var uDist = new double[underRings + 1, sectors + 1];
        for (int i = 0; i <= underRings; i++)
        {
            double t = CoinrunnerRadial(i, underRings);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                under[i, j] = Surface(t, theta, false, out uAlpha[i, j], out uDist[i, j]);
            }
        }

        Vec3 FaceNormal(Vec3[,] p, int last, int i, int j, bool upFace)
        {
            int jp = (j + 1) % sectors, jm = (j - 1 + sectors) % sectors;
            Vec3 du, dv;
            if (i == 0) { du = p[1, j] - p[0, j]; dv = p[1, jp] - p[1, jm]; }
            else if (i == last) { du = p[last, j] - p[last - 1, j]; dv = p[last, jp] - p[last, jm]; }
            else { du = p[i + 1, j] - p[i - 1, j]; dv = p[i, jp] - p[i, jm]; }
            Vec3 n = du.Cross(dv);
            if (n.LengthSq < 1e-18) n = upFace ? up : -up;
            n = n.Normalized();
            if (upFace ? n.Dot(up) < 0 : n.Dot(up) > 0) n = -n;
            return n;
        }

        // --- pigment --------------------------------------------------------
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        for (int i = 0; i <= rings; i++)
        {
            double t = CoinrunnerRadial(i, rings);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                double vm = VeinMask(alpha[i, j], dist[i, j]);

                // Patina: a mottled film of verdigris over the living green, with warm bronze
                // showing through only where the film has thinned.
                double patina = Noise.Fbm(seed + 131, Math.Cos(theta) * t * 2.4 + 3.3,
                    Math.Sin(theta) * t * 2.4 - 1.1, 3);
                var col = Primitives.Mix(b.Top, b.Verdigris, MathD.Clamp01(0.16 + patina * 0.42));
                double bare = MathD.Clamp01((patina - 0.30) * 2.4) * b.Bronze;
                col = Primitives.Mix(col, b.BronzeCol, bare * 0.34);

                // The coin's border and its milled edge take the most wear.
                double bead = BeadMask(t, theta);
                col = Primitives.Mix(col, Primitives.Mix(col, b.BronzeCol, 0.42), bead * 0.42);

                // Gold tracery along the veins.
                col = Primitives.Mix(col, b.Vein, Math.Min(1.0, vm * 0.68));

                double drift = Noise.Gradient(seed, Math.Cos(theta) * t * 3.4, Math.Sin(theta) * t * 3.4);
                double shade = 1.0 + 0.09 * drift;
                col = new[] { col[0] * shade, col[1] * shade, col[2] * shade };

                var n = FaceNormal(pos, rings, i, j, true);
                // UV.y carries the distance from the attachment so the shader's pale midrib lands
                // on the sinus rather than across the blade; UV.x stays a whisper of the angular
                // coordinate so the shader's pinnate vein model never fires on this leaf.
                double u = 0.026 * (j / (double)sectors);
                if (j == sectors) u = 0.0;
                m.AddVertex(pos[i, j], n, col, 1.0, u, MathD.Clamp01(0.5 + 0.5 * dist[i, j]), 0.0, 1.0);
            }
        }

        for (int i = 0; i <= underRings; i++)
        {
            double t = CoinrunnerRadial(i, underRings);
            for (int j = 0; j <= sectors; j++)
            {
                double theta = 2.0 * Math.PI * j / sectors;
                double vm = VeinMask(uAlpha[i, j], uDist[i, j]);
                // The reverse of the coin: a pale mint face with the venation showing through.
                var col = Primitives.Mix(b.Under, b.UnderVein, Math.Min(1.0, vm * 0.9));
                double drift = Noise.Gradient(seed + 977, Math.Cos(theta) * t * 3.0, Math.Sin(theta) * t * 3.0);
                double shade = 1.0 + 0.07 * drift;
                col = new[] { col[0] * shade, col[1] * shade, col[2] * shade };

                var n = FaceNormal(under, underRings, i, j, false);
                double u = 0.026 * (j / (double)sectors);
                if (j == sectors) u = 0.0;
                m.AddVertex(under[i, j], n, col, 1.0, u, MathD.Clamp01(0.5 + 0.5 * uDist[i, j]), 1.0, 1.0);
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

        // The edge: a thin rolled rim where the two faces meet.
        var edgeCol = Primitives.Mix(b.Under, b.Top, 0.45);
        for (int j = 0; j < sectors; j++)
        {
            int t0 = topBase + rings * stride + j, t1 = topBase + rings * stride + j + 1;
            int b0 = underBase + underRings * stride + j, b1 = underBase + underRings * stride + j + 1;
            double theta = 2.0 * Math.PI * (j + 0.5) / sectors;
            var outward = (side * Math.Cos(theta) + body * Math.Sin(theta)).Normalized();
            int e0 = m.AddVertex(m.Position(t0), outward, edgeCol, 1.0, 0.026 * (j / (double)sectors), 1.0, 1.0, 1.0);
            int e1 = m.AddVertex(m.Position(t1), outward, edgeCol, 1.0, 0.026 * ((j + 1) / (double)sectors), 1.0, 1.0, 1.0);
            int e2 = m.AddVertex(m.Position(b1), outward, edgeCol, 1.0, 0.026 * ((j + 1) / (double)sectors), 1.0, 1.0, 1.0);
            int e3 = m.AddVertex(m.Position(b0), outward, edgeCol, 1.0, 0.026 * (j / (double)sectors), 1.0, 1.0, 1.0);
            Primitives.TriangleFacing(m, e0, e1, e2, outward);
            Primitives.TriangleFacing(m, e0, e2, e3, outward);
        }

        m.RecordLeaf(leafVertex, leafIndex, origin, b.Radius);
    }

    // ---------------------------------------------------------------------
    // Nodes, runners, roots
    // ---------------------------------------------------------------------

    /// <summary>
    /// A node on a creeping runner: the swelling where the runner roots, the adventitious roots
    /// that peg it to the soil, and the papery bract that sheathes the petiole bases.
    /// </summary>
    private static void CoinrunnerNode(MeshData m, Rng rng, Vec3 node, double[] rootCol, double[] bractCol)
    {
        int roots = 2 + rng.NextInt(3);
        for (int r = 0; r < roots; r++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double len = rng.Range(0.0050, 0.0135);
            var p0 = node + dir * 0.0005 - new Vec3(0, 0.0005, 0);
            var p1 = node + dir * (len * 0.45) - new Vec3(0, 0.0036, 0);
            var p2 = node + dir * len - new Vec3(0, 0.0104, 0);
            Primitives.Tube(m, new[] { p0, p1, p2 },
                new[] { CoinrunnerRootRadius, CoinrunnerRootRadius * 0.56, CoinrunnerRootRadius * 0.18 }, 4,
                (i, v) => (rootCol, 1, i, v, 0, 0));
        }

        if (rng.NextDouble() < 0.55)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var spread = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var across = Vec3.Up.Cross(spread).Normalized();
            double len = rng.Range(0.0034, 0.0060);
            double width = len * rng.Range(0.26, 0.38);
            var root = node + spread * 0.0010 + new Vec3(0, 0.0005, 0);
            var mid = root + spread * (len * 0.42) + new Vec3(0, len * 0.40, 0);
            var tip = root + spread * (len * 0.78) + new Vec3(0, len * 0.70, 0);
            int s0 = m.VertexCount;
            m.AddVertex(root - across * width, across, bractCol, 1.0, 0.0, 0.0, 0.0, 0.0);
            m.AddVertex(root + across * width, across, bractCol, 1.0, 0.0, 1.0, 0.0, 0.0);
            m.AddVertex(mid - across * (width * 0.80), across, bractCol, 1.0, 0.5, 0.0, 0.0, 0.0);
            m.AddVertex(mid + across * (width * 0.80), across, bractCol, 1.0, 0.5, 1.0, 0.0, 0.0);
            m.AddVertex(tip, spread, bractCol, 1.0, 1.0, 0.5, 0.0, 0.0);
            Primitives.TriangleFacing(m, s0, s0 + 2, s0 + 1, spread);
            Primitives.TriangleFacing(m, s0 + 1, s0 + 2, s0 + 3, spread);
            Primitives.TriangleFacing(m, s0 + 2, s0 + 4, s0 + 3, spread);
        }
    }

    /// <summary>
    /// A petiole leaving a node, carrying one coin at its tip. It leaves from inside the node,
    /// arches in three points, and opens into the sinus at the base of the blade.
    /// </summary>
    private static void CoinrunnerPetiole(MeshData m, Rng rng, Vec3 node, Vec3 tangent,
        double petioleHeight, double spread, double[] stemCol, double[] tipCol, CoinrunnerBlade blade,
        int rings, int sectors, out Vec3 bladeOrigin, out Vec3 bladeUp, out Vec3 bladeBody)
    {
        double az = rng.Range(0, 2 * Math.PI);
        var lean = new Vec3(Math.Cos(az), 0, Math.Sin(az));

        // The blade is held nearly flat, tipping away from the vertical in the direction the
        // petiole leans.
        double tilt = rng.Range(0.05, 0.36);
        bladeUp = (Vec3.Up * Math.Cos(tilt) + lean * Math.Sin(tilt)).Normalized();
        bladeBody = (lean - bladeUp * lean.Dot(bladeUp)).Normalized();

        var path = new Vec3[4];
        var radii = new double[4];
        path[0] = node - tangent * 0.0038 - new Vec3(0, 0.0005, 0);
        path[1] = node + tangent * 0.0004 + new Vec3(0, 0.0014, 0);
        path[2] = node + new Vec3(0, petioleHeight * 0.66, 0) + lean * (spread * 0.52);
        path[3] = node + new Vec3(0, petioleHeight, 0) + lean * spread;
        double petioleBase = CoinrunnerPetioleRadius * rng.Range(0.86, 1.14);
        radii[0] = petioleBase * 1.06;
        radii[1] = petioleBase * 0.90;
        radii[2] = petioleBase * 0.80;
        radii[3] = petioleBase * 1.10; // swells where it opens into the sinus

        Primitives.Tube(m, path, radii, 6, (i, v) => (Primitives.Mix(stemCol, tipCol, i / 3.0), 1, i, v, 0, 0));

        // The blade's sinus is cut for the petiole: the attachment sits at the base of the notch.
        bladeOrigin = path[3];
        CoinrunnerBladeMesh(m, bladeOrigin, bladeUp, bladeBody, blade, rings, sectors,
            Rng.Mix((ulong)rng.NextULong(), 0x51ED270B));
    }

    /// <summary>
    /// A creeping runner. It hugs the soil, swells where it roots, and forks into a branch that
    /// leaves through a collar rather than an intersection. The leading tip carries the warm
    /// sap-light of a runner that is still growing.
    /// </summary>
    private static void CoinrunnerStolon(MeshData m, Rng rng, Vec3 origin, double startAz,
        double reach, int nodeCount, double[] stemCol, double[] tipCol, double[] rootCol,
        double[] bractCol, int rings, int sectors, Action<Vec3, Vec3> onNode)
    {
        const int steps = 10;
        double meander = rng.Range(0, 2 * Math.PI);
        var path = new Vec3[steps + 1];
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double ang = startAz + 0.32 * Math.Sin(t * 2.9 + meander) + 0.14 * Math.Sin(t * 7.3 + meander * 1.8);
            double r = t * reach;
            double y = 0.0019 + 0.0012 * Math.Sin(t * 5.3 + meander) + 0.0008 * Math.Sin(t * 10.7 + meander * 2.1);
            path[s] = origin + new Vec3(Math.Cos(ang) * r, y, Math.Sin(ang) * r);
        }

        var nodeSteps = new int[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            nodeSteps[i] = 2 + (int)Math.Round((i + 0.85) * (steps - 3.0) / nodeCount);

        // The runner swells at each node: that swelling is the root crown and the collar the
        // petioles come out of.
        var radii = new double[steps + 1];
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            double r = CoinrunnerRunnerRadius * (1.0 - 0.26 * t);
            foreach (int ns in nodeSteps)
                r *= 1.0 + 0.55 * Math.Exp(-Math.Pow((s - ns) / 1.05, 2.0));
            radii[s] = r;
        }

        // The growing tip is lit: a warm sap-light where the runner is still advancing.
        Primitives.Tube(m, path, radii, 6, (i, v) =>
        {
            double t = i / (double)steps;
            var col = Primitives.Mix(stemCol, tipCol, t);
            double glow = MathD.SmoothStep(0.72, 1.0, t);
            col = Primitives.Mix(col, new[] { 0.78, 0.72, 0.28 }, glow * 0.85);
            return (col, 1, i, v, 0, 0);
        });

        foreach (int ns in nodeSteps)
        {
            var node = path[ns];
            var tangent = (path[Math.Min(steps, ns + 1)] - path[Math.Max(0, ns - 1)]).Normalized();
            CoinrunnerNode(m, rng, node, rootCol, bractCol);
            onNode(node, tangent);
        }

        // One fork per runner. The branch starts slightly behind the fork point so it grows out of
        // the parent instead of being welded onto its side.
        if (rng.NextDouble() < 0.85)
        {
            int forkStep = Math.Clamp(nodeSteps[rng.NextInt(nodeCount)], 1, steps - 2);
            var forkPos = path[forkStep];
            var forkTangent = (path[Math.Min(steps, forkStep + 1)] - path[Math.Max(0, forkStep - 1)]).Normalized();
            double side = rng.NextDouble() < 0.5 ? 1.0 : -1.0;
            double forkAz = startAz + side * rng.Range(0.55, 0.95);
            double forkReach = reach * rng.Range(0.34, 0.56);

            const int bSteps = 4;
            var bPath = new Vec3[bSteps + 1];
            var bRadii = new double[bSteps + 1];
            var start = forkPos - forkTangent * 0.0055; // overlap the parent
            for (int bs = 0; bs <= bSteps; bs++)
            {
                double bt = bs / (double)bSteps;
                double ang = forkAz + 0.24 * Math.Sin(bt * 3.2 + meander);
                double r = bt * forkReach;
                double y = 0.0018 + 0.0010 * Math.Sin(bt * 4.6 + meander);
                bPath[bs] = start + new Vec3(Math.Cos(ang) * r, y, Math.Sin(ang) * r);
                bRadii[bs] = CoinrunnerRunnerRadius * 0.90 * (1.0 - 0.26 * bt);
            }
            Primitives.Tube(m, bPath, bRadii, 6, (i, v) =>
            {
                double t = i / (double)bSteps;
                var col = Primitives.Mix(stemCol, tipCol, 0.45 + 0.55 * t);
                col = Primitives.Mix(col, new[] { 0.78, 0.72, 0.28 }, MathD.SmoothStep(0.70, 1.0, t) * 0.8);
                return (col, 1, i, v, 0, 0);
            });

            // The collar that hides the union of the two runners.
            Primitives.Ellipsoid(m, forkPos, new Vec3(radii[forkStep] * 1.35, radii[forkStep] * 1.25, radii[forkStep] * 1.35), 4, 7,
                (u, v) => (stemCol, 1, u, v, 0, 0));

            int bNodes = 2;
            for (int bn = 1; bn <= bNodes; bn++)
            {
                int idx = Math.Clamp(2 + bn * (bSteps - 2) / (bNodes + 1), 1, bSteps);
                var node = bPath[idx];
                var tangent = (bPath[Math.Min(bSteps, idx + 1)] - bPath[Math.Max(0, idx - 1)]).Normalized();
                CoinrunnerNode(m, rng, node, rootCol, bractCol);
                onNode(node, tangent);
            }
        }
    }

    /// <summary>
    /// The tiny umbel a pennywort throws above the mat: a slender peduncle carrying a tight head
    /// of minute green-white florets, each one a small flat disc.
    /// </summary>
    private static void CoinrunnerUmbel(MeshData m, Rng rng, Vec3 basePoint, double height,
        double[] stemCol, double[] floretCol)
    {
        const int steps = 5;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double lean = 0.0018 * Math.Sin(t * 2.6 + (ulong)rng.NextInt(64));
            path[i] = basePoint + new Vec3(lean, height * Math.Pow(t, 0.92), lean * 0.6);
            radii[i] = 0.00052 * (1.0 - 0.34 * t);
        }
        Primitives.Tube(m, path, radii, 5, (i, v) => (stemCol, 1, i, v, 0, 0));

        var top = path[steps];
        int florets = 6 + rng.NextInt(5);
        for (int f = 0; f < florets; f++)
        {
            double theta = f * 2.399963229728653;
            double ring = 0.35 + 0.65 * (f / (double)Math.Max(1, florets - 1));
            var dir = new Vec3(Math.Cos(theta) * ring, 0.55 - 0.45 * ring, Math.Sin(theta) * ring).Normalized();
            double size = rng.Range(0.00085, 0.00150);
            var centre = top + dir * (0.0010 + rng.Range(0.0, 0.0012));
            var up = dir;
            var sideA = Vec3.Up.Cross(up);
            if (sideA.LengthSq < 1e-9) sideA = new Vec3(1, 0, 0);
            sideA = sideA.Normalized();
            var sideB = up.Cross(sideA).Normalized();

            // A minute floret: a small flat disc with five notches around its rim.
            int start = m.VertexCount;
            m.AddVertex(centre, up, floretCol, 1.0, 0.5, 0.5, 0.0, 0.0);
            for (int k = 0; k <= 5; k++)
            {
                double a = 2 * Math.PI * k / 5.0;
                double notch = 0.86 + 0.14 * Math.Cos(5.0 * a);
                var p = centre + (sideA * Math.Cos(a) + sideB * Math.Sin(a)) * (size * notch) + up * (size * 0.16);
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
    /// Coinrunner. A wide, fast green mat: runners that root at the nodes, a reniform coin at
    /// every node, and a dense, shingled canopy of glossy emerald blades that closes over the open
    /// ground.
    /// </summary>
    private static void Coinrunner(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        // A caller that has no authored size still gets a properly proportioned plant rather than
        // a collapsed one.
        double matSpan = radius > 0.01 ? radius : 0.30;
        double plantHeight = height > 0.01 ? height : 0.05;

        var stemCol = new[] { 0.28, 0.50, 0.20 };   // lush green runner
        var stemTip = new[] { 0.40, 0.66, 0.26 };
        var rootCol = new[] { 0.36, 0.31, 0.22 };
        var bractCol = new[] { 0.62, 0.66, 0.38 };   // pale green papery node bract
        var floretCol = new[] { 0.92, 0.93, 0.84 };

        // Blade tessellation. The reniform notch needs enough angular samples to read as a sinus
        // rather than as a dent.
        int rings = 5, sectors = 24;
        if (m.Lod is { } lod)
        {
            rings = lod.Segments(rings, 4);
            sectors = lod.Segments(sectors, 12);
        }

        // Blade pigments: a living green face under a verdigris patina, gold along the veins.
        var bladeTop = Primitives.Mix(c1, c2, 0.35);
        var bladeVerdigris = Primitives.Mix(c1, new[] { 0.30, 0.56, 0.47 }, 0.55);
        var bladeBronze = new[] { 0.60, 0.39, 0.15 };
        var bladeVein = new[] { 0.88, 0.80, 0.50 };
        var bladeUnder = Primitives.Mix(c2, new[] { 0.74, 0.79, 0.68 }, 0.55);
        var bladeUnderVein = Primitives.Mix(c2, new[] { 0.58, 0.66, 0.40 }, 0.65);

        // A blade factory: every coin is a little different in size, vein count, sinus and patina.
        CoinrunnerBlade NewBlade(Rng br, double sizeScale)
        {
            var b = new CoinrunnerBlade
            {
                Radius = br.Range(0.0200, 0.0460) * sizeScale,
                Veins = 5 + br.NextInt(3),                 // 5..7 veins fanning out of the sinus
                VeinPhase = br.Range(0, 2 * Math.PI),
                VeinAlpha = br.Range(-1.2, 1.2),
                SinusOffset = br.Range(0.26, 0.38),
                CrenAmp = br.Range(0.014, 0.034),
                Crenations = 11 + br.NextInt(5),
                CrenPhase = br.Range(0, 2 * Math.PI),
                Dish = br.Range(0.038, 0.085),
                RimLift = br.Range(0.016, 0.038),
                Bead = br.Range(0.028, 0.048),
                BeadWidth = br.Range(0.06, 0.11),
                Waviness = br.Range(0.008, 0.020),
                // A thin, soft blade: Coinrunner is the opposite of Mooncoin's fleshy coin.
                Thickness = br.Range(0.024, 0.036),
                Groove = br.Range(0.006, 0.011),
                Rib = br.Range(0.010, 0.018),
                Top = bladeTop,
                Verdigris = bladeVerdigris,
                BronzeCol = bladeBronze,
                Vein = bladeVein,
                Under = bladeUnder,
                UnderVein = bladeUnderVein,
                Bronze = br.Range(0.15, 0.75),
            };
            // Older outer coins have been nibbled.
            if (br.NextDouble() < 0.26)
            {
                b.DamageAngle = br.Range(0, 2 * Math.PI);
                b.DamageDepth = br.Range(0.03, 0.10);
            }
            return b;
        }

        // --- crown ---------------------------------------------------------
        // Every runner leaves the same small crown boss, so the mat has a single heart.
        Primitives.Ellipsoid(part, new Vec3(0, 0.0018, 0), new Vec3(0.0046, 0.0026, 0.0046), 4, 8,
            (u, v) => (stemCol, 1, u, v, 0, 0));

        // --- runners and their coins ---------------------------------------
        int runners = 7 + rng.NextInt(3);
        double matRadius = matSpan * 0.96;
        for (int k = 0; k < runners; k++)
        {
            double baseAz = 2 * Math.PI * k / runners + rng.Range(-0.20, 0.20);
            double reach = matRadius * rng.Range(0.82, 1.04);
            double nodeScale = rng.Range(0.85, 1.15);
            CoinrunnerStolon(part, rng, new Vec3(rng.Range(-0.002, 0.002), 0.0014, rng.Range(-0.002, 0.002)),
                baseAz, reach, 3 + rng.NextInt(2), stemCol, stemTip, rootCol, bractCol, rings, sectors,
                (node, tangent) =>
                {
                    int petioles = rng.NextDouble() < 0.28 ? 2 : 1;
                    for (int p = 0; p < petioles; p++)
                    {
                        var blade = NewBlade(rng, nodeScale);
                        double petH = plantHeight * rng.Range(0.12, 0.40) * nodeScale;
                        double spread = blade.Radius * rng.Range(0.18, 0.52);
                        CoinrunnerPetiole(part, rng, node, tangent, petH, spread, stemCol, stemTip,
                            blade, rings, sectors, out _, out _, out _);
                    }
                });
        }

        // --- crown coins ----------------------------------------------------
        int crownLeaves = 7 + rng.NextInt(3);
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2 * Math.PI * c / crownLeaves + rng.Range(-0.28, 0.28);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var blade = NewBlade(rng, 1.05);
            double dist = matRadius * rng.Range(0.02, 0.20);
            double petH = plantHeight * rng.Range(0.13, 0.42);
            double spread = blade.Radius * rng.Range(0.18, 0.48);

            var path = new Vec3[4];
            var radii = new double[4];
            path[0] = new Vec3(0, 0.0012, 0) - dir * 0.0030;
            path[1] = new Vec3(0, 0.0018, 0) + dir * 0.0009;
            path[2] = dir * (dist * 0.55) + new Vec3(0, petH * 0.64, 0);
            path[3] = dir * (dist + spread) + new Vec3(0, petH, 0);
            double petioleBase = CoinrunnerPetioleRadius * rng.Range(0.9, 1.15);
            radii[0] = petioleBase * 1.06; radii[1] = petioleBase * 0.92;
            radii[2] = petioleBase * 0.80; radii[3] = petioleBase * 1.10;
            Primitives.Tube(part, path, radii, 6, (i, v) => (Primitives.Mix(stemCol, stemTip, i / 3.0), 1, i, v, 0, 0));

            double tilt = rng.Range(0.05, 0.38);
            var bladeUp = (Vec3.Up * Math.Cos(tilt) + dir * Math.Sin(tilt)).Normalized();
            var bladeBody = (dir - bladeUp * dir.Dot(bladeUp)).Normalized();
            CoinrunnerBladeMesh(part, path[3], bladeUp, bladeBody, blade, rings, sectors,
                Rng.Mix(seed, (ulong)(c * 877 + 5)));
        }

        // --- a couple of flower heads ---------------------------------------
        for (int f = 0; f < 2; f++)
        {
            double fa = rng.Range(0, 2 * Math.PI);
            double fr = matRadius * rng.Range(0.18, 0.55);
            var baseP = new Vec3(Math.Cos(fa) * fr, 0.0018, Math.Sin(fa) * fr);
            CoinrunnerUmbel(part, rng, baseP, plantHeight * rng.Range(0.46, 0.56), stemCol, floretCol);
        }

        AppendCoinrunnerWorld(m, part, matSpan, plantHeight);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void AppendCoinrunnerWorld(MeshData target, MeshData part, double radius, double height)
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
