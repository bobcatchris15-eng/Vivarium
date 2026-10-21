using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Veilfern: an ostrich fern (<i>Matteuccia struthiopteris</i>) for the 'veilfern' species.
///
/// The species was a generic arching fern; it is rebuilt here as a real ostrich fern: a scaly
/// crown holding a vase of arching sterile fronds, each carrying many pairs of sub-opposite,
/// forward-swept, lobed pinnae, with golden-brown chaffy croziers emerging from the middle.
///
/// The one deliberate departure is the fertile fronds. An ostrich fern overwinters as a shuttlecock
/// of bronze fertile fronds whose pinna margins roll in to protect the sori; here that shuttlecock
/// stands in the crown of the plant and its beads of sori carry a faint mint light after dark.
///
/// Reference renders: scripts/visuals/veilfern/ostrich_fern_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    // Pinna length along the frond as a fraction of the maximum. An ostrich fern's pinnae are
    // longest a little above the base and shrink steadily towards the frond tip.
    private static readonly double[] VeilfernPinnaT = { 0.00, 0.08, 0.18, 0.30, 0.45, 0.60, 0.72, 0.84, 0.93, 1.00 };
    private static readonly double[] VeilfernPinnaW = { 0.42, 0.72, 0.93, 1.00, 0.97, 0.88, 0.76, 0.60, 0.42, 0.24 };

    /// <summary>Pinna breadth as a fraction of its own length.</summary>
    private const double VeilfernPinnaBreadth = 0.36;

    // Pinna relief, as fractions of the pinna's half width.
    private const double VeilfernKeel = 0.085;   // raised midrib
    private const double VeilfernFall = 0.050;   // blade falling from the midrib to the margin
    private const double VeilfernCurl = 0.070;   // the pinna tip curling under
    private const double VeilfernLobe = 0.038;   // depth of the lobed margin

    private static double VeilfernPinnaFraction(double t)
    {
        if (t <= 0) return VeilfernPinnaW[0];
        if (t >= 1) return VeilfernPinnaW[^1];
        for (int i = 0; i < VeilfernPinnaT.Length - 1; i++)
        {
            double t0 = VeilfernPinnaT[i], t1 = VeilfernPinnaT[i + 1];
            if (t < t0 || t > t1) continue;
            double u = (t - t0) / (t1 - t0), u2 = u * u, u3 = u2 * u;
            double v0 = VeilfernPinnaW[i], v1 = VeilfernPinnaW[i + 1];
            double p0 = VeilfernPinnaW[Math.Max(i - 1, 0)];
            double p3 = VeilfernPinnaW[Math.Min(i + 2, VeilfernPinnaW.Length - 1)];
            return 0.5 * (2 * v0 + (-p0 + v1) * u
                + (2 * p0 - 5 * v0 + 4 * v1 - p3) * u2
                + (-p0 + 3 * v0 - 3 * v1 + p3) * u3);
        }
        return VeilfernPinnaW[^1];
    }

    private static Vec3 VeilfernBez(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        t = MathD.Clamp01(t);
        double u = 1.0 - t;
        return p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
    }

    private static Vec3 VeilfernTan(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        t = MathD.Clamp01(t);
        double u = 1.0 - t;
        var d = (p1 - p0) * (3.0 * u * u) + (p2 - p1) * (6.0 * u * t) + (p3 - p2) * (3.0 * t * t);
        return d.LengthSq > 1e-10 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>
    /// One lobed pinna. The margin alternates in and out from row to row, which gives the fern's
    /// toothed lobes crisply at the row spacing the blade is built with.
    /// </summary>
    private static void VeilfernPinna(MeshData m, Vec3 basePoint, Vec3 direction, Vec3 normal,
        double length, double halfWidth, int rows, int cols, double lobeDepth, double roll,
        ulong seed, double[] topColour, double[] underColour, bool senescent = false)
    {
        if (length <= 1e-5 || halfWidth <= 1e-6) return;
        var rng = Rng.Keyed(seed, "veilfern.pinna", 0);
        double phase = rng.Range(0.0, 1.0);
        double asymmetry = rng.Range(-0.10, 0.10);

        var side = normal.Cross(direction);
        if (side.LengthSq < 1e-10) side = direction.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var up = side.Cross(direction).Normalized();

        // A short petiolule carries the blade clear of the rachis, as a fern's pinnae do.
        var attach = basePoint + direction * (length * 0.06);
        var stalkCol = Primitives.Mix(new[] { 0.16, 0.13, 0.07 }, topColour, 0.35);
        Primitives.Tube(m, new[] { basePoint, attach }, new[] { length * 0.012, length * 0.008 }, 4,
            (i, v) => (stalkCol, 1, i, v, 0, 0));

        Vec3 Surface(double t, double x, double lobe)
        {
            double w = halfWidth * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.62))), 0.72);
            double lateral = x * w * (1.0 + lobe);
            double keel = VeilfernKeel * (1.0 - Math.Abs(x)) * (1.0 - Math.Abs(x));
            double fall = -VeilfernFall * Math.Pow(Math.Abs(x), 1.7);
            double curl = -VeilfernCurl * SmoothStep(0.68, 1.0, t);
            // A fertile pinna rolls its margin inwards over the sori.
            double rolled = roll * Math.Pow(Math.Abs(x), 3.0) * Math.Sin(Math.PI * Math.Min(1.0, t * 1.1));
            double skew = asymmetry * halfWidth * Math.Sin(t * 2.6 + phase * 6.28);
            return attach + direction * (t * length) + side * (lateral + skew) + up * ((keel + fall + curl + rolled) * halfWidth);
        }

        const double eps = 0.005;
        int firstVertex = m.VertexCount;
        int firstIndex = m.Indices.Count;
        var grid = new int[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            double t = i / (double)(rows - 1);
            // Alternating margin gives one lobe per two rows.
            double lobe = (i % 2 == 0 ? 1.0 : -1.0) * lobeDepth * (0.6 + 0.8 * rng.Range(0.2, 1.0));
            double lobeScale = lobeDepth > 0 ? lobe : 0.0;
            for (int j = 0; j < cols; j++)
            {
                double x = (j / (double)(cols - 1)) * 2.0 - 1.0;
                double edge = j == 0 || j == cols - 1 ? lobeScale * Math.Abs(x) : 0.0;
                var p = Surface(t, x, edge);
                var dT = Surface(Math.Min(1.0, t + eps), x, edge) - Surface(Math.Max(0.0, t - eps), x, edge);
                var dX = Surface(t, Math.Min(1.0, x + eps), edge) - Surface(t, Math.Max(-1.0, x - eps), edge);
                var n = dX.Cross(dT);
                if (n.LengthSq < 1e-14) n = up;
                n = n.Normalized();
                if (n.Dot(up) < 0) n = -n;
                double u = t;
                double v = (x + 1.0) * 0.5;
                // A senescent fertile pinna is dead tissue: it is not a living leaf, so it is marked
                // outside the leaf shading path and keeps its own dry bronze pigment.
                double u2 = senescent ? 0.0 : 0.0;
                double v2 = senescent ? 0.0 : 1.0;
                grid[i, j] = m.AddVertex(p, n, Primitives.Mix(topColour, underColour, 0.15 + 0.5 * t), 1.0, u, v, u2, v2);
            }
        }
        for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < cols - 1; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                Primitives.TriangleFacing(m, a, c, b, up);
                Primitives.TriangleFacing(m, b, c, d, up);
            }
        m.RecordLeaf(firstVertex, firstIndex, attach, length);
    }

    /// <summary>
    /// A bead of sori. It carries the nodule marker, so the night shader can light it without
    /// lighting the whole frond.
    /// </summary>
    private static void VeilfernSori(MeshData m, Vec3 centre, double radius, double[] colour)
    {
        const int rings = 1, sides = 5;
        int start = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;
            for (int s = 0; s <= sides; s++)
            {
                double th = 2.0 * Math.PI * s / sides;
                var p = centre + new Vec3(Math.Sin(phi) * Math.Cos(th) * radius,
                    Math.Sin(phi) * Math.Sin(th) * radius * 0.7,
                    Math.Cos(phi) * radius * 0.8);
                var n = (p - centre);
                if (n.LengthSq < 1e-12) n = Vec3.Up;
                // The nodule marker is what the night shader lights up: UV2.x in the marked-nodule
                // band, with the leaf-side flag set so the glow mask catches it.
                m.AddVertex(p, n.Normalized(), colour, 1.0, 0.5, 0.5, 1.4, 1.0);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + r * (sides + 1) + s;
                Primitives.TriangleFacing(m, a, a + sides + 1, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, a + sides + 1, a + sides + 2, Vec3.Up);
            }
    }

    /// <summary>
    /// The ostrich fern's shuttlecock: short bronze fertile fronds whose pinna margins roll in
    /// over rows of glowing sori. This is how the species overwinters, and here it stands in the
    /// middle of the crown.
    /// </summary>
    private static void VeilfernFertileFrond(MeshData m, Rng rng, Vec3 origin, double azimuth,
        double length, int pairs, int rows, int cols, double[] rachisColour, double[] bladeColour,
        double[] soriColour, ulong seed)
    {
        var H = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var P0 = origin;
        var P1 = origin + Vec3.Up * (length * 0.72) + H * (length * 0.05);
        var P2 = origin + Vec3.Up * (length * 0.98) + H * (length * 0.16);
        var P3 = origin + Vec3.Up * (length * 0.94) + H * (length * 0.26);

        const int steps = 8;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            path[i] = VeilfernBez(P0, P1, P2, P3, t);
            radii[i] = 0.0032 * (1.0 - 0.62 * t);
        }
        Primitives.Tube(m, path, radii, 5, (i, v) => (rachisColour, 1, (double)i / steps, v, 0, 0));

        for (int i = 0; i < pairs; i++)
        {
            double t = 0.16 + 0.76 * (i / (double)Math.Max(1, pairs - 1));
            double frac = VeilfernPinnaFraction(t);
            double pinnaLen = length * 0.135 * frac;
            if (pinnaLen < 0.006) continue;
            foreach (double sgn in new[] { -1.0, 1.0 })
            {
                var rp = VeilfernBez(P0, P1, P2, P3, t);
                var rt = VeilfernTan(P0, P1, P2, P3, t);
                var rn = (Vec3.Up - rt * Vec3.Up.Dot(rt)).Normalized();
                if (rn.LengthSq < 1e-10) rn = (H - rt * H.Dot(rt)).Normalized();
                var rs = rt.Cross(rn).Normalized();
                // Fertile pinnae are narrower and roll their margins over the spore beds.
                var dir = (rs * (sgn * 0.64) + rt * 0.70 + rn * (0.13 * sgn)).Normalized();
                var norm = dir.Cross(rt).Normalized();
                if (norm.Dot(rn) < 0) norm = -norm;
                var attach = rp + rs * (sgn * 0.004);
                ulong pSeed = Rng.Mix(seed, (ulong)(i * 97 + (sgn > 0 ? 1 : 2)));
                VeilfernPinna(m, attach, dir, norm, pinnaLen, pinnaLen * VeilfernPinnaBreadth * 0.34,
                    rows, cols, VeilfernLobe * 0.7, 0.16, pSeed, bladeColour, bladeColour, senescent: true);

                if (i < pairs * 0.72)
                {
                    // Beads of sori along the rolled margin.
                    for (int b = 0; b < 3; b++)
                    {
                        double f = 0.30 + 0.32 * b;
                        var p = attach + dir * (pinnaLen * f) + rs * (sgn * pinnaLen * VeilfernPinnaBreadth * 0.16);
                        VeilfernSori(m, p, pinnaLen * 0.045, soriColour);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A crozier: the coiled tip of an emerging frond, sheathed in golden-brown chaff. The coil
    /// carries the nodule marker so its inner light can be woken at night.
    /// </summary>
    private static void VeilfernCrozier(MeshData m, Rng rng, Vec3 origin, double azimuth,
        double height, double[] stalkColour, double[] chaffColour, double[] glowColour, ulong seed)
    {
        var H = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        const int steps = 7;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            path[i] = origin + H * (height * 0.16 * t * t) + Vec3.Up * (height * 0.62 * t);
            radii[i] = 0.0030 * (1.0 - 0.30 * t);
        }
        // The coil.
        double coilR = height * 0.16;
        var centre = path[^1] + H * coilR;
        const int coilSteps = 10;
        int coilStart = path.Length;
        var coilPath = new Vec3[coilSteps];
        var coilRadii = new double[coilSteps];
        for (int j = 0; j < coilSteps; j++)
        {
            double theta = Math.PI * 1.85 * (j + 1) / coilSteps;
            double r = coilR * (1.0 - 0.42 * j / coilSteps);
            coilPath[j] = centre + H * (r * Math.Sin(theta)) + Vec3.Up * (r * (1.0 - Math.Cos(theta)));
            coilRadii[j] = 0.0026 * (1.0 - 0.55 * j / coilSteps);
        }
        var full = new Vec3[steps + 1 + coilSteps];
        var fullRadii = new double[steps + 1 + coilSteps];
        Array.Copy(path, full, path.Length);
        Array.Copy(radii, fullRadii, radii.Length);
        Array.Copy(coilPath, 0, full, coilStart, coilSteps);
        Array.Copy(coilRadii, 0, fullRadii, coilStart, coilSteps);
        int tubeStart = m.VertexCount;
        Primitives.Tube(m, full, fullRadii, 6, (i, v) =>
            (i >= coilStart + coilSteps - 3 ? glowColour : stalkColour, 1, (double)i / (full.Length - 1), v, 0, 0));
        // The heart of the coil carries the faint night light of the spore beds.
        int glowRings = 2 * 7; // two rings of a six-sided tube
        for (int i = 0; i < glowRings; i++)
        {
            int v = tubeStart + (coilStart + coilSteps - 2) * 7 + i;
            if (v < m.VertexCount) { m.UV2[v * 2] = 1.45f; m.UV2[v * 2 + 1] = 1.0f; }
        }

        // Chaffy scales: the golden fleece an ostrich fern's crozier wears as it pushes up.
        int scales = 10 + rng.NextInt(5);
        for (int k = 0; k < scales; k++)
        {
            double t = rng.Range(0.12, 0.92);
            int idx = Math.Min((int)(t * steps), steps);
            var p = path[idx];
            double a = rng.Range(0.0, 2.0 * Math.PI);
            var outDir = new Vec3(Math.Cos(a), 0.30, Math.Sin(a)).Normalized();
            var tip = p + outDir * rng.Range(0.006, 0.013) + H * rng.Range(-0.004, 0.005);
            var mid = Vec3.Lerp(p, tip, 0.55) + outDir * 0.0012;
            int s0 = m.AddVertex(p, outDir, chaffColour, 1, 0, 0.5, 0, 0);
            int s1 = m.AddVertex(mid, outDir, chaffColour, 1, 0.5, 0.5, 0, 0);
            int s2 = m.AddVertex(tip, outDir, chaffColour, 1, 1, 0.5, 0, 0);
            Primitives.TriangleFacing(m, s0, s1, s2, outDir);
        }
    }

    /// <summary>
    /// The veilfern plant: a scaly crown holding a vase of arching sterile fronds, a bronze
    /// shuttlecock of fertile fronds and a few emerging croziers.
    /// </summary>
    private static void Veilfern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        bool juvenile, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        int rows = juvenile ? 7 : 10;
        int cols = 5;
        if (m.Lod is { } lod)
        {
            rows = lod.Segments(rows, 4);
            cols = lod.Segments(cols, 3);
        }

        var topGreen = Primitives.Mix(c1, new[] { 0.16, 0.38, 0.17 }, 0.55);
        var underJade = Primitives.Mix(c2, new[] { 0.28, 0.48, 0.30 }, 0.55);
        var rachisColour = Primitives.Mix(new[] { 0.46, 0.39, 0.17 }, topGreen, 0.42);
        var bronze = new[] { 0.26, 0.15, 0.06 };
        var bronzeRachis = new[] { 0.20, 0.11, 0.05 };
        var chaff = new[] { 0.55, 0.38, 0.16 };
        var sori = new[] { 0.52, 0.92, 0.70 };

        // 1. Sterile fronds: a vase of arching plumes. Each frond leaves the crown nearly vertical,
        //    then arches over and droops, the way an ostrich fern's do.
        int fronds = juvenile ? 4 : 9;
        int pairs = juvenile ? 12 : 27;
        for (int k = 0; k < fronds; k++)
        {
            double az = 2.0 * Math.PI * k / fronds + rng.Range(-0.16, 0.16);
            double len = height * (juvenile ? rng.Range(0.85, 1.05) : rng.Range(1.12, 1.42));
            double reach = radius * (juvenile ? rng.Range(0.55, 0.78) : rng.Range(0.86, 1.05));
            var H = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var side = new Vec3(-H.Z, 0, H.X);
            var baseP = H * rng.Range(0.012, 0.030);

            var P0 = baseP;
            var P1 = baseP + Vec3.Up * (len * 0.66) + H * (len * 0.06);
            var P2 = baseP + Vec3.Up * (len * 0.88) + H * (reach * 0.60) + side * rng.Range(-0.05, 0.05) * len;
            var P3 = baseP + Vec3.Up * (len * 0.10) + H * (reach * 1.08) + side * rng.Range(-0.12, 0.12) * len;

            const int steps = 12;
            var path = new Vec3[steps + 1];
            var radii = new double[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;
                var p = VeilfernBez(P0, P1, P2, P3, t);
                if (i == 0) p = new Vec3(p.X, Math.Max(0.0, p.Y), p.Z);
                path[i] = p;
                radii[i] = 0.0052 * (1.0 - 0.82 * t);
            }
            Primitives.Tube(part, path, radii, 6, (i, v) => (rachisColour, 1, (double)i / steps, v, 0, 0));

            for (int i = 0; i < pairs; i++)
            {
                double t = 0.12 + 0.86 * (i / (double)(pairs - 1));
                double frac = VeilfernPinnaFraction(t);
                double pinnaLen = len * 0.135 * frac;
                if (pinnaLen < 0.006) continue;
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    // Sub-opposite: the upper pinna of each pair sits slightly further along.
                    double tt = Math.Min(0.995, t + (sgn > 0 ? 0.012 : 0.0));
                    var rp = VeilfernBez(P0, P1, P2, P3, tt);
                    var rt = VeilfernTan(P0, P1, P2, P3, tt);
                    var rn = (Vec3.Up - rt * Vec3.Up.Dot(rt)).Normalized();
                    if (rn.LengthSq < 1e-10) rn = (H - rt * H.Dot(rt)).Normalized();
                    var rs = rt.Cross(rn).Normalized();
                    // Pinnae sweep forwards towards the frond tip and lift a little out of plane.
                    var dir = (rs * (sgn * 0.74) + rt * 0.62 + rn * (0.20 * sgn)).Normalized();
                    var norm = dir.Cross(rt).Normalized();
                    if (norm.Dot(rn) < 0) norm = -norm;
                    var attach = rp + rs * (sgn * 0.005);
                    ulong pSeed = Rng.Mix(seed, (ulong)(k * 613 + i * 97 + (sgn > 0 ? 1 : 2)));
                    VeilfernPinna(part, attach, dir, norm, pinnaLen,
                        pinnaLen * VeilfernPinnaBreadth * 0.5, rows, cols, VeilfernLobe, 0.0,
                        pSeed, topGreen, underJade);
                }
            }
        }

        // 2. The bronze shuttlecock of fertile fronds standing in the crown.
        int fertile = juvenile ? 2 : 6;
        for (int k = 0; k < fertile; k++)
        {
            double az = 2.0 * Math.PI * k / fertile + rng.Range(-0.22, 0.22);
            double len = height * rng.Range(0.78, 0.96);
            VeilfernFertileFrond(part, rng, new Vec3(Math.Cos(az) * 0.010, 0.006, Math.Sin(az) * 0.010),
                az, len, juvenile ? 4 : 9, Math.Max(5, rows - 2), cols,
                bronzeRachis, bronze, sori, Rng.Mix(seed, (ulong)(k * 431 + 17)));
        }

        // 3. Emerging croziers.
        int croziers = juvenile ? 3 : 2;
        for (int k = 0; k < croziers; k++)
        {
            double az = rng.Range(0.0, 2.0 * Math.PI);
            VeilfernCrozier(part, rng, new Vec3(Math.Cos(az) * 0.008, 0.004, Math.Sin(az) * 0.008),
                az, height * rng.Range(0.45, 0.62), rachisColour, chaff, sori,
                Rng.Mix(seed, (ulong)(k * 733 + 5)));
        }

        // 4. The crown: a stubby, scaly caudex the fronds rise from.
        var crownColour = Primitives.Mix(new[] { 0.24, 0.17, 0.08 }, topGreen, 0.18);
        Primitives.Ellipsoid(part, new Vec3(0, 0.014, 0), new Vec3(0.030, 0.016, 0.030), 4, 8,
            (u, v) => (crownColour, 1, u, v, 0, 0));
        int chaffScales = juvenile ? 12 : 18;
        for (int k = 0; k < chaffScales; k++)
        {
            double a = rng.Range(0.0, 2.0 * Math.PI);
            var outDir = new Vec3(Math.Cos(a), rng.Range(0.35, 0.9), Math.Sin(a)).Normalized();
            var p = new Vec3(Math.Cos(a) * 0.018, rng.Range(0.004, 0.020), Math.Sin(a) * 0.018);
            var tip = p + outDir * rng.Range(0.008, 0.016);
            var mid = Vec3.Lerp(p, tip, 0.5) + outDir * 0.0015;
            int s0 = part.AddVertex(p, outDir, chaff, 1, 0, 0.5, 0, 0);
            int s1 = part.AddVertex(mid, outDir, chaff, 1, 0.5, 0.5, 0, 0);
            int s2 = part.AddVertex(tip, outDir, chaff, 1, 1, 0.5, 0, 0);
            Primitives.TriangleFacing(part, s0, s1, s2, outDir);
        }

        // Nothing may sink through the soil. A fern's frond bases sit in the litter, so the few
        // millimetres of a stalk that would dip below ground are simply held at the surface.
        for (int i = 0; i < part.VertexCount; i++)
            if (part.Positions[i * 3 + 1] < 0.0f) part.Positions[i * 3 + 1] = 0.0f;

        AppendWorld(m, part, radius, height);
    }
}
