using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// White clover (Trifolium repens) groundcover for the 'trifold' species.
///
/// The plant is authored in real-world metres and converted to the engine's mesh frame by
/// <see cref="AppendWorld"/>, so what is built here is what the player sees: a creeping mat of
/// runners rooting at the nodes, long petioles carrying palmately trifoliate leaves, and globose
/// flower heads held clear of the foliage.
///
/// Leaflet anatomy follows the species: obcordate blades (widest above the middle, narrowing to a
/// cuneate base) with a real apical cleft between two rounded lobes, a finely serrate margin, a
/// raised midrib fold and the pale chevron watermark the species is known for. White clover also
/// throws the occasional four-leaflet sprig, and those are kept here.
///
/// Reference renders: scripts/visuals/trifold/clover_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    // Obcordate half-width profile of a white clover leaflet, widest above the middle, narrowing to
    // a cuneate base and to a broad apex that carries the apical cleft.
    private static readonly double[] CloverProfileT = { 0.00, 0.06, 0.14, 0.24, 0.36, 0.50, 0.62, 0.72, 0.80, 0.88, 0.94, 1.00 };
    private static readonly double[] CloverProfileW = { 0.05, 0.17, 0.33, 0.53, 0.72, 0.88, 0.97, 1.00, 0.97, 0.86, 0.71, 0.44 };

    /// <summary>Where the midrib stops and the apical cleft begins.</summary>
    private const double CloverCleftT = 0.845;
    /// <summary>Lateral position of a lobe tip, as a fraction of the leaflet's maximum half width.</summary>
    private const double CloverLobeX = 0.40;
    /// <summary>Cleft edge curvature: above one keeps the notch narrow low down and open at the tip.</summary>
    private const double CloverCleftPower = 1.30;

    // Leaflet relief, expressed as fractions of the leaflet's half width so that the fold, the
    // droop and the margin flick keep their real proportions at any leaf size.
    private const double CloverRidge = 0.105;   // raised midrib keel
    private const double CloverDrop = 0.058;    // blade falling away from the keel to the margin
    private const double CloverFlick = 0.030;   // the very margin turning back up
    private const double CloverCurl = 0.050;    // the apex drooping
    private const double CloverWrinkle = 0.009; // transverse creasing across the blade

    private const int CloverLeafletRows = 9;
    private const int CloverLeafletCols = 3;
    private const double CloverToothDepth = 0.055; // serration depth, fraction of the half width

    /// <summary>Half width of a clover leaflet at normalised along-position t.</summary>
    private static double CloverHalfWidth(double t)
    {
        if (t <= 0) return CloverProfileW[0];
        if (t >= 1) return CloverProfileW[^1];
        for (int i = 0; i < CloverProfileT.Length - 1; i++)
        {
            double t0 = CloverProfileT[i], t1 = CloverProfileT[i + 1];
            if (t < t0 || t > t1) continue;
            double u = (t - t0) / (t1 - t0), u2 = u * u, u3 = u2 * u;
            double v0 = CloverProfileW[i], v1 = CloverProfileW[i + 1];
            double p0 = CloverProfileW[Math.Max(i - 1, 0)];
            double p3 = CloverProfileW[Math.Min(i + 2, CloverProfileW.Length - 1)];
            return 0.5 * (2 * v0 + (-p0 + v1) * u
                + (2 * p0 - 5 * v0 + 4 * v1 - p3) * u2
                + (-p0 + 3 * v0 - 3 * v1 + p3) * u3);
        }
        return CloverProfileW[^1];
    }

    /// <summary>
    /// Lateral position of the leaflet's inner boundary: the midrib below the cleft, then the cleft
    /// edge that separates the two apical lobes. Returning the cleft edge keeps the blade a single
    /// connected sheet instead of two lobes floating apart.
    /// </summary>
    private static double CloverInnerEdge(double t)
    {
        if (t <= CloverCleftT) return 0.0;
        double s = (t - CloverCleftT) / (1.0 - CloverCleftT);
        return CloverLobeX * Math.Pow(s, CloverCleftPower);
    }

    private static double SmoothStep(double a, double b, double x)
    {
        if (b <= a) return x >= b ? 1.0 : 0.0;
        double t = Math.Clamp((x - a) / (b - a), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// One white clover leaflet: an obcordate blade with a real apical cleft, a raised midrib fold,
    /// a finely serrate margin and a tapering apex droop. Single sided; the flora shader paints the
    /// backface with the species' underside pigment.
    /// </summary>
    private static void CloverLeaflet(MeshData m, Vec3 basePoint, Vec3 along, Vec3 across, Vec3 up,
        double length, double halfWidth, int rows, int cols, ulong seed, double[] c1, double[] c2)
    {
        var rng = Rng.Keyed(seed, "clover.leaflet", 0);
        double phase = rng.Range(0.0, 6.283);
        double asymmetry = rng.Range(-0.055, 0.055);
        double toothPhase = rng.Range(0.0, 1.0);

        // Surface of one half of the blade. x runs from the inner boundary (midrib, then cleft edge)
        // out to the margin; t runs from the petiolule to the apex.
        Vec3 Surface(double t, double x, int side)
        {
            double inner = CloverInnerEdge(t);
            double outer = CloverHalfWidth(t);
            double px = inner + (outer - inner) * x;
            double z = CloverRidge * (1.0 - SmoothStep(0.0, 0.30, x))
                     - CloverDrop * Math.Pow(x, 1.8)
                     + CloverFlick * SmoothStep(0.78, 1.0, x)
                     - CloverCurl * SmoothStep(0.68, 1.0, t)
                     + CloverWrinkle * Math.Sin(t * 21.0 + phase * 6.0) * x
                     + asymmetry * Math.Sin(t * 3.1 + phase) * x;
            return basePoint + along * (t * length) + across * (side * px * halfWidth) + up * (z * halfWidth);
        }

        const double eps = 0.004;
        var colourBase = Primitives.Mix(c1, c2, rng.Range(0.10, 0.40));
        var colourTip = Primitives.Mix(c1, c2, rng.Range(0.45, 0.85));

        int firstVertex = m.VertexCount;
        int firstIndex = m.Indices.Count;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int side = sideIndex == 0 ? 1 : -1;
            var grid = new int[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                double t = i / (double)(rows - 1);
                for (int j = 0; j < cols; j++)
                {
                    double x = j / (double)(cols - 1);
                    var p = Surface(t, x, side);
                    var dT = Surface(Math.Min(1.0, t + eps), x, side) - Surface(Math.Max(0.0, t - eps), x, side);
                    var dX = Surface(t, Math.Min(1.0, x + eps), side) - Surface(t, Math.Max(0.0, x - eps), side);
                    var n = dX.Cross(dT);
                    if (n.LengthSq < 1e-12) n = up;
                    n = n.Normalized();
                    if (n.Dot(up) < 0) n = -n;
                    double px = CloverInnerEdge(t) + (CloverHalfWidth(t) - CloverInnerEdge(t)) * x;
                    double u = t;
                    double v = 0.5 + 0.5 * side * px;
                    grid[i, j] = m.AddVertex(p, n, Primitives.Mix(colourBase, colourTip, t * 0.85), 1.0, u, v, 0.0, 1.0);
                }
            }
            for (int i = 0; i < rows - 1; i++)
            {
                for (int j = 0; j < cols - 1; j++)
                {
                    int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                    Primitives.TriangleFacing(m, a, c, b, up);
                    Primitives.TriangleFacing(m, b, c, d, up);
                }
            }

            // Serrate margin: one tooth per row interval, standing out from the smooth outline.
            double tooth = CloverToothDepth * (0.75 + 0.5 * Math.Sin(toothPhase * 6.283));
            for (int i = 0; i < rows - 1; i++)
            {
                int v0 = grid[i, cols - 1], v1 = grid[i + 1, cols - 1];
                var p0 = m.Position(v0);
                var p1 = m.Position(v1);
                var mid = (p0 + p1) * 0.5;
                var marginOut = (across * side).Normalized();
                var tip = mid + marginOut * (tooth * halfWidth) + along * (halfWidth * tooth * 0.10);
                var n = (m.NormalAt(v0) + m.NormalAt(v1)).Normalized();
                int tipId = m.AddVertex(tip, n, m.Colors[v0 * 4], m.Colors[v0 * 4 + 1], m.Colors[v0 * 4 + 2], 1.0,
                    m.UV[v0 * 2], m.UV[v0 * 2 + 1], 0.0, 1.0);
                Primitives.TriangleFacing(m, v0, tipId, v1, up);
            }
        }

        // One leaf span for the whole leaflet, so the LOD tiers shed a blade and its two halves
        // together instead of leaving half a clover leaf behind.
        m.RecordLeaf(firstVertex, firstIndex, basePoint, length);
    }

    /// <summary>
    /// A palmately arranged leaf group. White clover is usually trifoliate but genuinely does throw
    /// four-leaflet (and rarer five-leaflet) sprigs, and those are kept here.
    ///
    /// The leaflets do not converge on a single point: each starts on its own short petiolule
    /// around the stalk tip, so the junction reads as three blades springing from a swollen stalk
    /// instead of three flat plates meeting at a point.
    /// </summary>
    private static void CloverLeafGroup(MeshData m, Vec3 apex, Vec3 normal, Vec3 fwd, double span,
        double[] c1, double[] c2, ulong seed, int rows, int cols)
    {
        normal = normal.Normalized();
        var side = normal.Cross(fwd);
        if (side.LengthSq < 1e-8) side = normal.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        fwd = side.Cross(normal).Normalized();

        var rng = Rng.Keyed(seed, "clover.group", 0);
        int count = rng.NextDouble() < 0.17 ? 4 : 3;
        double reach = span / (2.0 * Math.Sin(Math.PI / count)) * (count == 4 ? 0.88 : 1.0);
        for (int k = 0; k < count; k++)
        {
            double a = 2.0 * Math.PI * k / count + rng.Range(-0.16, 0.16);
            var dir = (fwd * Math.Cos(a) + side * Math.Sin(a)).Normalized();
            // Each leaflet of the palm sits at its own angle. A real clover leaf is never a flat
            // propeller, and the small differences in pitch are what catch the light separately.
            var bladeNormal = (normal + side * rng.Range(-0.26, 0.26) + dir * rng.Range(-0.12, 0.20)).Normalized();
            var across = bladeNormal.Cross(dir).Normalized();
            double length = reach * rng.Range(0.90, 1.08);
            double halfWidth = length * rng.Range(0.40, 0.48);
            // The stalk tip swells where the leaflets spring from it, so the joint between stalk and
            // blade is a living shoulder rather than a hard corner.
            double shoulder = halfWidth * rng.Range(0.26, 0.38);
            var basePoint = apex + dir * shoulder + bladeNormal * (shoulder * 0.35);
            ulong leafletSeed = Rng.Mix(seed, (ulong)(k * 977 + 13));
            CloverLeaflet(m, basePoint, dir, across, bladeNormal, length, halfWidth, rows, cols, leafletSeed, c1, c2);
            // A short petiolule carries each leaflet away from the shoulder, which is what makes the
            // junction read as three separate blades rather than one split fan.
            var stalkTip = basePoint;
            var stalkBase = apex + dir * (shoulder * 0.35);
            Primitives.Tube(m, new[] { stalkBase, stalkTip }, new[] { halfWidth * 0.16, halfWidth * 0.10 }, 5,
                (i, v) => (Primitives.Mix(c1, c2, 0.25), 1, i, v, 0.0, 1.0));
        }
    }

    /// <summary>
    /// Globose white clover head: a green calyx carrying a dense cluster of small papilionaceous
    /// florets, cream at first and blushing pink as the head ages.
    /// </summary>
    private static void CloverHead(MeshData m, Vec3 top, double headRadius, int florets, double blush,
        double fantasy, ulong seed)
    {
        var rng = Rng.Keyed(seed, "clover.head", 0);
        var calyx = new[] { 0.22, 0.36, 0.15 };
        var floretBase = new[] { 0.93, 0.92, 0.86 };
        var floretBlush = new[] { 0.90, 0.72, 0.76 };
        var moonlace = new[] { 0.80, 0.84, 1.00 };
        if (fantasy > 0) floretBase = Primitives.Mix(floretBase, moonlace, fantasy);

        // Involucre: the green cup of bracts the head sits in.
        const int calyxRings = 3, calyxSides = 7;
        int calyxStart = m.VertexCount;
        for (int r = 0; r <= calyxRings; r++)
        {
            double phi = (Math.PI * 0.5) * r / calyxRings;
            for (int s = 0; s <= calyxSides; s++)
            {
                double th = 2.0 * Math.PI * s / calyxSides;
                var p = top + new Vec3(Math.Cos(phi) * Math.Cos(th) * headRadius * 0.52,
                    -Math.Sin(phi) * headRadius * 0.42,
                    Math.Cos(phi) * Math.Sin(th) * headRadius * 0.52);
                var n = (new Vec3(Math.Cos(phi) * Math.Cos(th), -0.5, Math.Cos(phi) * Math.Sin(th))).Normalized();
                m.AddVertex(p, n, calyx, 1.0, 0.5, (double)s / calyxSides, 0.0, 0.0);
            }
        }
        for (int r = 0; r < calyxRings; r++)
            for (int s = 0; s < calyxSides; s++)
            {
                int a = calyxStart + r * (calyxSides + 1) + s;
                Primitives.TriangleFacing(m, a, a + calyxSides + 1, a + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a + 1, a + calyxSides + 1, a + calyxSides + 2, m.NormalAt(a + 1));
            }

        // Florets: tiny tubes with a flared lip, packed over the upper hemisphere.
        for (int f = 0; f < florets; f++)
        {
            double y = 1.0 - (f + 0.5) / florets;
            double rad = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
            double theta = f * 2.399963229728653;
            var dir = new Vec3(Math.Cos(theta) * rad, y, Math.Sin(theta) * rad).Normalized();
            var side = Vec3.Up.Cross(dir);
            if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
            side = side.Normalized();
            var up = side.Cross(dir).Normalized();

            double length = headRadius * rng.Range(0.52, 0.80);
            double width = length * rng.Range(0.26, 0.38);
            var baseP = top + dir * (headRadius * 0.30);
            var midP = baseP + dir * (length * 0.55);
            var tipP = baseP + dir * length;
            var colour = blush > 0.5 && rng.NextDouble() < 0.6 ? floretBlush : floretBase;
            int start = m.VertexCount;
            // UV2.x = 1 marks a glossy petal rather than a leaf blade, so the head does not pick up
            // leaf venation or leaf transmission.
            m.AddVertex(baseP, dir, colour, 1.0, 0.0, 0.5, 1.0, 0.0);
            m.AddVertex(midP - side * width, up, colour, 1.0, 0.5, 0.0, 1.0, 0.0);
            m.AddVertex(tipP, dir, Primitives.Mix(colour, new[] { 1.0, 0.98, 0.92 }, 0.35), 1.0, 1.0, 0.5, 1.0, 0.0);
            m.AddVertex(midP + side * width, up, colour, 1.0, 0.5, 1.0, 1.0, 0.0);
            Primitives.TriangleFacing(m, start, start + 1, start + 3, dir);
            Primitives.TriangleFacing(m, start + 3, start + 1, start + 2, dir);
        }
    }

    /// <summary>
    /// The clover plant. Runners creep across the soil and root at the nodes; every node carries
    /// petioles that lift palmate leaves clear of the mat; a few peduncles rise above the foliage
    /// carrying the flower heads.
    /// </summary>
    private static void TrifoldClover(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };
        var stemCol = new[] { 0.26, 0.44, 0.19 };
        var stemTip = new[] { 0.38, 0.60, 0.26 };
        var rootCol = new[] { 0.34, 0.28, 0.20 };

        int rows = CloverLeafletRows;
        int cols = CloverLeafletCols;
        if (m.Lod is { } lod)
        {
            rows = lod.Segments(rows, 5);
            cols = lod.Segments(cols, 3);
        }

        const int stolons = 8;
        const int nodesPerStolon = 8;
        const int crownLeaves = 12;

        // 1. Creeping runners. A white clover mat is a network of stolons rooting at the nodes, not
        //    a rosette, so the runners reach well past the foliage and stay visible at the mat edge.
        for (int k = 0; k < stolons; k++)
        {
            double baseAz = 2.0 * Math.PI * k / stolons + rng.Range(-0.16, 0.16);
            double reach = radius * rng.Range(0.84, 1.0);
            const int segs = 18;
            var path = new Vec3[segs + 1];
            var radii = new double[segs + 1];
            for (int s = 0; s <= segs; s++)
            {
                double t = s / (double)segs;
                double wander = 0.12 * Math.Sin(t * Math.PI * 2.0 + k * 1.7);
                double az = baseAz + wander;
                double r = t * reach;
                double y = 0.004 + 0.003 * Math.Sin(t * 3.4 + k);
                path[s] = new Vec3(Math.Cos(az) * r, y, Math.Sin(az) * r);
                // A rooting node of white clover is visibly thicker than the internode beside it, so
                // the runner reads as a beaded stem rather than an extruded wire.
                bool node = s >= 2 && s <= 16 && s % 2 == 0;
                radii[s] = 0.0016 * (1.0 - 0.20 * t) * (node ? 1.6 : 1.0)
                    * (1.0 + 0.07 * Math.Sin(s * 2.3 + k));
            }
            Primitives.Tube(part, path, radii, 6,
                (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / segs), 1, (double)i / segs, v, 0, 0));

            for (int s = 2; s <= 16 && s <= nodesPerStolon * 2; s += 2)
            {
                double t = s / (double)segs;
                var node = path[s];

                // Adventitious roots at the node, pegging the runner to the soil. They start inside
                // the node swelling so the root and the runner share one organic junction.
                if (s % 4 == 0)
                {
                    var rootTip = node + new Vec3(rng.Range(-0.005, 0.005), -0.016, rng.Range(-0.005, 0.005));
                    Primitives.Tube(part, new[] { node + Vec3.Up * 0.0006, rootTip }, new[] { 0.0013, 0.0006 }, 4,
                        (i, v) => (rootCol, 1, i, v, 0, 0));
                }

                int petioles = rng.NextDouble() < 0.55 ? 2 : 1;
                for (int p = 0; p < petioles; p++)
                {
                    double petAz = rng.Range(0.0, 2.0 * Math.PI);
                    var lean = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = height * rng.Range(0.42, 0.78) * (1.0 - 0.18 * t);
                    double span = 0.050 * rng.Range(0.85, 1.15) * (1.0 + 0.10 * t);
                    ulong pSeed = Rng.Mix(seed, (ulong)(k * 547 + s * 43 + p * 13 + 3));
                    // Leaves do not all lie flat: a mat is layered, with blades tilted to catch the
                    // light at different angles.
                    var leafNormal = (Vec3.Up * rng.Range(0.58, 0.95) + lean * rng.Range(0.22, 0.62)).Normalized();

                    // Petiole: leaves the runner low, arcs outward and lifts the blade clear of the
                    // mat. The stalk swells where it leaves the node and flares again where the
                    // leaflets spring from it, so neither end reads as a tube glued to another part.
                    const int steps = 3;
                    var pPath = new Vec3[steps + 1];
                    var pRadii = new double[steps + 1];
                    double wobble = rng.Range(0.0, 6.283);
                    for (int i = 0; i <= steps; i++)
                    {
                        double pt = i / (double)steps;
                        double rise = petH * Math.Pow(pt, 0.66);
                        double reachOut = petH * 0.40 * Math.Pow(pt, 1.7);
                        pPath[i] = node + lean * reachOut + Vec3.Up * rise;
                        double flare = 1.0 + 0.26 * Math.Pow(1.0 - pt, 3.0) + 0.34 * Math.Pow(pt, 4.0);
                        pRadii[i] = 0.0012 * flare * (1.0 + 0.07 * Math.Sin(pt * 8.0 + wobble));
                    }
                    Primitives.Tube(part, pPath, pRadii, 6,
                        (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / steps), 1, (double)i / steps, v, 0, 0));

                    var apex = pPath[steps];
                    CloverLeafGroup(part, apex, leafNormal, lean, span, c1, c2, pSeed, rows, cols);
                    CloverStipules(part, node, petAz, pSeed);
                }
            }
        }

        // 2. Central crown: the leaves that hold the middle of the mat together.
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2.0 * Math.PI * c / crownLeaves + rng.Range(-0.22, 0.22);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double dist = radius * rng.Range(0.02, 0.38);
            double petH = height * rng.Range(0.40, 0.72);
            double span = 0.050 * rng.Range(0.88, 1.12);
            ulong lSeed = Rng.Mix(seed, (ulong)(c * 179 + 7));
            var leafNormal = (Vec3.Up * rng.Range(0.58, 0.95) + dir * rng.Range(0.22, 0.62)).Normalized();

            const int steps = 3;
            var pPath = new Vec3[steps + 1];
            var pRadii = new double[steps + 1];
            double wobble = rng.Range(0.0, 6.283);
            for (int i = 0; i <= steps; i++)
            {
                double pt = i / (double)steps;
                double rise = petH * Math.Pow(pt, 0.66);
                double reachOut = petH * 0.38 * Math.Pow(pt, 1.7);
                pPath[i] = dir * (dist + reachOut) + Vec3.Up * rise;
                double flare = 1.0 + 0.26 * Math.Pow(1.0 - pt, 3.0) + 0.34 * Math.Pow(pt, 4.0);
                pRadii[i] = 0.0012 * flare * (1.0 + 0.07 * Math.Sin(pt * 8.0 + wobble));
            }
            Primitives.Tube(part, pPath, pRadii, 6,
                (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / steps), 1, (double)i / steps, v, 0, 0));

            CloverLeafGroup(part, pPath[steps], leafNormal, dir, span, c1, c2, lSeed, rows, cols);
            CloverStipules(part, pPath[0], az, lSeed);
        }

        // 3. Flower heads on long peduncles, held above the foliage the way a real white clover
        //    carries them, so they read against the sky rather than dissolving into the mat.
        int heads = 6;
        for (int fl = 0; fl < heads; fl++)
        {
            double fa = 2.0 * Math.PI * fl / heads + rng.Range(-0.28, 0.28);
            double fr = radius * rng.Range(0.24, 0.66);
            var dir = new Vec3(Math.Cos(fa), 0, Math.Sin(fa));
            var baseP = dir * (fr * 0.55) + Vec3.Up * 0.004;
            var top = dir * fr + Vec3.Up * (height * rng.Range(0.80, 1.0));

            const int steps = 5;
            var pPath = new Vec3[steps + 1];
            var pRadii = new double[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double pt = i / (double)steps;
                double rise = (top.Y - baseP.Y) * Math.Pow(pt, 0.88);
                double reachOut = fr * 0.45 * Math.Pow(pt, 1.4);
                pPath[i] = baseP + dir * reachOut + Vec3.Up * rise;
                pRadii[i] = 0.0011 * (1.0 - 0.18 * pt);
            }
            Primitives.Tube(part, pPath, pRadii, 6,
                (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / steps), 1, (double)i / steps, v, 0, 0));

            // Older heads blush; one head in the clump runs the cool moonlace colour.
            double fantasy = fl == heads - 1 ? 1.0 : 0.0;
            double blush = fl >= heads - 2 ? 0.7 : 0.0;
            // The peduncle carries on a little way into the head so the calyx swallows it rather
            // than butting against it.
            CloverHead(part, pPath[steps] - Vec3.Up * 0.0016, 0.0092, 26, blush, fantasy,
                Rng.Mix(seed, (ulong)(fl * 433 + 71)));
        }

        AppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// The paired membranous stipules that sheath the base of a clover petiole. Small, but they are
    /// the species' clearest diagnostic feature at close range.
    /// </summary>
    private static void CloverStipules(MeshData m, Vec3 basePoint, double azimuth, ulong seed)
    {
        var rng = Rng.Keyed(seed, "clover.stipules", 0);
        var colour = new[] { 0.44, 0.56, 0.28 };
        for (int k = 0; k < 2; k++)
        {
            double a = azimuth + k * Math.PI;
            var spread = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var across = Vec3.Up.Cross(spread).Normalized();
            double length = 0.0052 * rng.Range(0.85, 1.15);
            double width = length * rng.Range(0.30, 0.40);
            var root = basePoint + spread * 0.0012;
            var mid = root + spread * (length * 0.45) + Vec3.Up * (length * 0.55);
            var tip = root + spread * (length * 0.85) + Vec3.Up * (length * 1.0);
            int start = m.VertexCount;
            m.AddVertex(root - across * width, across, colour, 1.0, 0.0, 0.0, 0.0, 1.0);
            m.AddVertex(root + across * width, across, colour, 1.0, 0.0, 1.0, 0.0, 1.0);
            m.AddVertex(mid - across * (width * 0.8), across, colour, 1.0, 0.5, 0.0, 0.0, 1.0);
            m.AddVertex(mid + across * (width * 0.8), across, colour, 1.0, 0.5, 1.0, 0.0, 1.0);
            m.AddVertex(tip, spread, colour, 1.0, 1.0, 0.5, 0.0, 1.0);
            Primitives.TriangleFacing(m, start, start + 2, start + 1, spread);
            Primitives.TriangleFacing(m, start + 1, start + 2, start + 3, spread);
            Primitives.TriangleFacing(m, start + 2, start + 4, start + 3, spread);
        }
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void AppendWorld(MeshData target, MeshData part, double radius, double height)
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
