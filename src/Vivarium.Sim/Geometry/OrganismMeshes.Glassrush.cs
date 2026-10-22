using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Horsetail groundcover for the 'glassrush' species.
///
/// Anatomy follows a real horsetail (Equisetum), and the great horsetail in particular:
///
///   * unbranched, jointed, hollow culms rising in a compact fan from a basal clump, pale
///     and glaucous rather than grassy green;
///   * at every node a SHEATH - a short collar that clasps the culm - whose upper rim is cut
///     into a ring of black, upward-pointing TEETH. The teeth are the great horsetail's
///     signature and they are what makes a joint read as a joint at a glance;
///   * whorls of fine, simple branchlets springing from the nodes of the fertile culms;
///   * a basal clump of old, brown, papery sheaths where the culms leave the mud;
///   * on the mature shoots, the species' faintly outlandish cattail-like seed spike, which
///     is kept but given a smoky-glass finish to match the plant it belongs to.
///
/// The fantastical element is that the horsetail is literally glass. The culms are hollow and
/// faintly translucent, the black teeth are cut from black crystal, and the seed spike is a
/// smokey glass cylinder with a pale crown. The real plant's silhouette and every one of its
/// diagnostic features are kept; only the material of the thing is changed.
///
/// Reference renders: scripts/visuals/glassrush/horsetail_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The glaucous culm green: pale, cold and slightly blue.</summary>
    private static readonly double[] RushCulm = { 0.150, 0.255, 0.205 };
    private static readonly double[] RushCulmTip = { 0.275, 0.385, 0.320 };
    /// <summary>The black, faintly crystalline teeth at every node.</summary>
    private static readonly double[] RushTooth = { 0.030, 0.032, 0.038 };
    /// <summary>The old, papery sheaths at the foot of the clump.</summary>
    private static readonly double[] RushOld = { 0.240, 0.185, 0.120 };
    /// <summary>The smoky glass of the seed spike.</summary>
    private static readonly double[] RushGlass = { 0.185, 0.215, 0.210 };

    /// <summary>
    /// A culm: a jointed, hollow-looking stem with a sheathing collar and a ring of black
    /// teeth at every node.
    /// </summary>
    private static (Vec3 Tip, Vec3 Axis, double TipRadius) RushCulmStem(MeshData m, Rng rng, Vec3 foot, Vec3 lean, double height,
        double baseRadius, int nodes, bool fertile, double[] c1, double[] c2, ulong seed)
    {
        // The culm arcs: it rises almost straight and leans over towards the tip.
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        for (int j = 0; j <= nodes; j++)
        {
            double t = j / (double)nodes;
            path[j] = foot + Vec3.Up * (height * t) + lean * (t * t * 0.85);
            // A horsetail culm swells very slightly at each node and tapers between them.
            radii[j] = baseRadius * (1.0 - 0.30 * t);
        }
        var culm = Primitives.Mix(c1, c2, 0.35);
        Primitives.Tube(m, path, radii, 9,
            (i, v) => (Primitives.Mix(Primitives.Scale(culm, 0.86), culm, (double)i / nodes), 1, (double)i / nodes, v, 0, 0));

        for (int j = 1; j < nodes; j++)
        {
            var node = path[j];
            double r = radii[j];
            // The sheath: a collar that sits ON the culm and flares into a cup. Its base is
            // exactly the culm's radius, so there is no step where the two meet.
            double collarH = r * 1.15;
            var collar = new Vec3[4];
            var collarR = new double[4];
            for (int k = 0; k <= 3; k++)
            {
                double t = k / 3.0;
                collar[k] = node + Vec3.Up * ((t - 0.30) * collarH);
                collarR[k] = r * (1.0 + 0.30 * Math.Pow(t, 0.75));
            }
            Primitives.Tube(m, collar, collarR, 9,
                (i, v) => (Primitives.Mix(Primitives.Scale(culm, 0.80), culm, 0.55), 1, i / 3.0, v, 0, 0));

            // The teeth: an even crown of black, upward-pointing triangles standing out of
            // the collar's rim. These are the great horsetail's clearest diagnostic and they
            // are what the eye reads as "jointed" from any distance, so they are regular:
            // the same length, evenly spaced, rooted exactly on the rim.
            int teeth = 9;
            var up = (path[Math.Min(nodes, j + 1)] - path[Math.Max(0, j - 1)]).Normalized();
            var side = up.Cross(Vec3.Up);
            if (side.LengthSq < 1e-10) side = new Vec3(1, 0, 0);
            side = side.Normalized();
            var other = up.Cross(side).Normalized();
            double rimR = collarR[3];
            double rimY = collar[3].Y;
            for (int t = 0; t < teeth; t++)
            {
                double a = 2 * Math.PI * t / teeth;
                var dir = (side * Math.Cos(a) + other * Math.Sin(a)).Normalized();
                // The tooth's base straddles the rim, so it is rooted in the collar and not
                // balanced on it.
                var at = new Vec3(node.X, node.Y, node.Z) + Vec3.Up * (rimY - node.Y) + dir * rimR;
                double len = r * 0.85;
                var tang = up;
                var bit = dir.Cross(tang).Normalized();
                double w = r * 0.52;
                // The teeth splay outwards as they rise, so the crown reads as separate teeth
                // rather than as one flat zigzag band.
                Vec3 tip = at + tang * len + dir * (len * 0.30);
                int a0 = m.AddVertex(at - bit * w, -bit, RushTooth, 1, 0, 0, 0, 0);
                int a1 = m.AddVertex(at + bit * w, bit, RushTooth, 1, 0, 1, 0, 0);
                int a2 = m.AddVertex(tip, dir, RushTooth, 1, 1, 0.5, 0, 0);
                Primitives.TriangleFacing(m, a0, a1, a2, dir);
                Primitives.TriangleFacing(m, a1, a0, a2, -dir);
            }

            // Fertile culms carry a whorl of fine, simple branchlets at each node. They root
            // just BELOW the sheath, out of the culm itself, the way a horsetail's branchlets
            // emerge from under the node.
            if (!fertile) continue;
            int branchlets = 6 + rng.NextInt(4);
            for (int b = 0; b < branchlets; b++)
            {
                double a = 2 * Math.PI * b / branchlets + rng.Range(-0.22, 0.22);
                var dir = (side * Math.Cos(a) + other * Math.Sin(a)).Normalized();
                double blen = rng.Range(0.075, 0.150) * (1.0 - 0.42 * (j / (double)nodes));
                var bRoot = node - Vec3.Up * (collarH * 0.25) + dir * (r * 0.92);
                var bTip = bRoot + dir * (blen * 0.72) + up * (blen * rng.Range(0.08, 0.26))
                    - up * (blen * 0.30);
                var bMid = bRoot + dir * (blen * 0.45) + up * (blen * 0.12);
                // Two segments with a joint at the middle, the way a horsetail branchlet is.
                Primitives.Tube(m, new[] { bRoot, bMid, bTip }, new[] { r * 0.22, r * 0.14, r * 0.05 }, 4,
                    (i, v) => (Primitives.Scale(culm, 0.92), 1, i, v, 0, 0));
            }
        }

        // The cut top of a culm shows the hollow bore, which is what makes a glassy stem
        // read as a tube rather than a dowel. The bore is a dark disc set just inside the
        // rim, so it sits in the mouth of the tube instead of on top of it.
        var topR = radii[nodes];
        var rim = path[nodes];
        var axis = (path[nodes] - path[nodes - 1]);
        if (axis.LengthSq < 1e-14) axis = Vec3.Up;
        axis = axis.Normalized();
        Primitives.Ellipsoid(m, rim - axis * (topR * 0.10),
            new Vec3(topR * 0.70, topR * 0.22, topR * 0.70), 3, 9,
            (u, v) => (Primitives.Scale(Primitives.Mix(culm, RushTooth, 0.55), 1.0), 1, u, v, 0, 0));
        return (rim, axis, topR);
    }

    /// <summary>
    /// The seed spike: the species' faintly outlandish cattail-like pod, given a smoky glass
    /// body with a pale crown and a haze of fine fibres.
    ///
    /// The spike is aligned to the culm it sits on: it starts exactly on the culm's tip ring
    /// and runs along the culm's own axis, so there is no kink and no gap where the two meet.
    /// </summary>
    private static void RushSeedSpike(MeshData m, Rng rng, Vec3 culmTip, Vec3 culmAxis,
        double culmRadius, double length, double width, double[] c1, double[] c2)
    {
        var axis = culmAxis.LengthSq > 1e-12 ? culmAxis.Normalized() : Vec3.Up;
        // The spike's axis continues the culm's, and its foot overlaps the culm's last ring so
        // the two are one continuous form.
        var basePoint = culmTip - axis * (length * 0.06);

        var path = new Vec3[9];
        var radii = new double[9];
        double[] profile = { 0.30, 0.72, 0.93, 1.00, 0.99, 0.90, 0.70, 0.42, 0.10 };
        for (int j = 0; j < 9; j++)
        {
            double t = j / 8.0;
            path[j] = basePoint + axis * (length * t);
            radii[j] = width * profile[j];
        }
        Primitives.Tube(m, path, radii, 11,
            (i, v) => (Primitives.Mix(RushGlass, Primitives.Mix(c1, c2, 0.4), v * 0.45), 1, i / 8.0, v, 0, 0));

        // A pale, tapered crown where the spike closes off, sitting on the spike's own tip.
        var crown = path[8];
        Primitives.Ellipsoid(m, crown + axis * (length * 0.03), new Vec3(width * 0.13, width * 0.46, width * 0.13), 4, 8,
            (u, v) => (Primitives.Mix(RushGlass, new[] { 0.70, 0.69, 0.62 }, 0.55), 1, u, v, 0, 0));

        // Fine fibres standing off the spike, the fuzz of a real cattail. They are short and
        // lie close to the surface, so they read as fuzz rather than as bristles.
        var ringA = axis.Cross(Vec3.Up);
        if (ringA.LengthSq < 1e-10) ringA = axis.Cross(new Vec3(1, 0, 0));
        ringA = ringA.Normalized();
        var ringB = axis.Cross(ringA).Normalized();
        int hairs = 150;
        for (int h = 0; h < hairs; h++)
        {
            double t = 0.06 + 0.88 * (h / (double)hairs);
            double a = h * 2.399963;
            var ring = (ringA * Math.Cos(a) + ringB * Math.Sin(a)).Normalized();
            var at = basePoint + axis * (length * t);
            double r = width * (0.30 + 0.70 * Math.Sin(Math.PI * t));
            var root = at + ring * r;
            // The fibres lie back along the spike rather than standing straight out, so they
            // read as a fuzz and not as a rash of bristles.
            var tip = root + axis * rng.Range(0.002, 0.007) + ring * rng.Range(0.000, 0.002);
            Primitives.Tube(m, new[] { root, tip }, new[] { 0.0006, 0.00012 }, 3,
                (i, v) => (Primitives.Mix(RushGlass, new[] { 0.70, 0.70, 0.66 }, 0.45), 1, i, v, 0, 0));
        }
    }

    private static Vec3 side(Vec3 up)
    {
        var s = up.Cross(Vec3.Up);
        if (s.LengthSq < 1e-10) s = up.Cross(new Vec3(1, 0, 0));
        return s.Normalized();
    }

    private static Vec3 up(Vec3 v)
    {
        var s = v.Cross(new Vec3(1, 0, 0));
        if (s.LengthSq < 1e-10) s = v.Cross(new Vec3(0, 0, 1));
        return s.Normalized();
    }

    /// <summary>
    /// The glassrush: a compact fan of glassy, jointed horsetail culms out of wet mud.
    /// </summary>
    private static void Glassrush(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var culmBase = Primitives.Mix(RushCulm, c1, 0.30);
        var culmTip = Primitives.Mix(RushCulmTip, c2, 0.16);

        int count = juvenile ? 12 : 26 + rng.NextInt(8);
        for (int k = 0; k < count; k++)
        {
            double angle = rng.Range(0, 2 * Math.PI);
            double radius = Math.Sqrt(rng.NextDouble()) * 0.26;
            var foot = new Vec3(Math.Cos(angle) * radius, -0.02, Math.Sin(angle) * radius);
            var lean = new Vec3(Math.Cos(angle), 0, Math.Sin(angle)) * rng.Range(0.10, 0.40);
            // The oldest culms in the middle of the clump are the tallest and the palest.
            double central = 1.0 - radius / 0.26;
            bool podded = !juvenile && k % 6 == 0;
            double height = rng.Range(podded ? 0.78 : 0.52, podded ? 0.98 : 1.0) * (0.80 + 0.24 * central);
            int nodes = juvenile ? 5 : 7 + rng.NextInt(3);
            double baseRadius = rng.Range(0.0105, 0.0165) * (0.80 + 0.28 * central);
            bool fertile = podded || (!juvenile && k % 3 == 0);

            var culm = RushCulmStem(m, rng, foot, lean, height, baseRadius, nodes, fertile, culmBase, culmTip,
                Rng.Mix(seed, (ulong)(k * 733 + 11)));

            if (!podded) continue;
            // The seed spike continues the culm's own axis, and each one is a different size.
            RushSeedSpike(m, rng, culm.Tip, culm.Axis, culm.TipRadius, rng.Range(0.17, 0.26),
                rng.Range(0.021, 0.030), culmBase, culmTip);
        }

        // The foot of the clump: old, brown, papery sheaths where the culms leave the mud.
        int old = juvenile ? 5 : 11;
        for (int k = 0; k < old; k++)
        {
            double angle = rng.Range(0, 2 * Math.PI);
            double radius = rng.Range(0.02, 0.13);
            var foot = new Vec3(Math.Cos(angle) * radius, -0.018, Math.Sin(angle) * radius);
            double h = rng.Range(0.05, 0.13);
            var lean = new Vec3(Math.Cos(angle), 0, Math.Sin(angle)) * rng.Range(0.01, 0.05);
            Primitives.Tube(m, new[] { foot, foot + Vec3.Up * h + lean }, new[] { 0.012, 0.006 }, 5,
                (i, v) => (Primitives.Mix(RushOld, Primitives.Scale(culmBase, 0.9), v * 0.55), 1, i, v, 0, 0));
        }
    }
}
