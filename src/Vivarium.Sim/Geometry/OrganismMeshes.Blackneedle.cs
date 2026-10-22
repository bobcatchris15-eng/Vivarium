using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Blackneedle - the spiny pioneer of disturbed ground.
/// Analogue: Cirsium, the thistle: a spiny basal rosette of white-backed leaves on a winged stem,
/// ending in a crown of florets inside a cage of spiny bracts.
///
/// A thistle is a plant that has given up on being soft. Its leaves are lobed into spines, its
/// stem carries spiny wings running the whole way up, and its flower head is a cage of bracts
/// built to stop anything from getting at the florets. The one thing it cannot make spiny is the
/// wool: a thistle's leaf back is covered in white hairs, and that white underside is the
/// signature of the whole family.
///
/// The fantasy layer is the material of the spines. A thistle's spines are brown and brittle;
/// these are black GLASS, and they catch the light as hard black points rather than as thorns.
/// The bracts round them keep a dull bronze edge, the colour of a blade that has been tempered
/// and cooled.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The dark green of the upper leaf face.</summary>
    private static readonly double[] NeedleGreen = { 0.18, 0.32, 0.15 };
    /// <summary>The white marbling that runs along the veins of the upper face.</summary>
    private static readonly double[] NeedleMarble = { 0.54, 0.60, 0.48 };
    /// <summary>The woolly white underside. Every thistle has it.</summary>
    private static readonly double[] NeedleWool = { 0.76, 0.74, 0.68 };
    /// <summary>The stem and the midribs.</summary>
    private static readonly double[] NeedleStem = { 0.30, 0.40, 0.22 };
    /// <summary>The tempered bronze edge of the bracts.</summary>
    private static readonly double[] NeedleBronze = { 0.66, 0.42, 0.12 };
    /// <summary>The black glass of the spines.</summary>
    private static readonly double[] NeedleGlass = { 0.07, 0.06, 0.08 };
    /// <summary>The violet of the florets.</summary>
    private static readonly double[] NeedleViolet = { 0.50, 0.28, 0.60 };
    private static readonly double[] NeedleVioletDeep = { 0.32, 0.16, 0.42 };

    /// <summary>A black glass spine: a needle that comes to a hard point and catches the light.</summary>
    private static void BlackneedleSpine(MeshData m, Vec3 from, Vec3 to, double radius)
    {
        var axis = to - from;
        if (axis.LengthSq < 1e-14) return;
        axis = axis.Normalized();
        var mid = Vec3.Lerp(from, to, 0.45) + axis * (radius * 0.5);
        Primitives.Tube(m, new[] { from, mid, to }, new[] { radius, radius * 0.55, radius * 0.05 }, 6,
            (i, v) => (i == 0 ? NeedleGlass : Primitives.Mix(NeedleGlass, NeedleBronze, 0.22), 1, i * 0.5, v, 0, 0));
    }

    /// <summary>
    /// One deeply lobed rosette leaf: a rachis that arches out of the crown and droops at the
    /// tip, carrying paired lobes that angle backwards towards the base, every one of them
    /// finished with a spine.
    ///
    /// The lobes are thin blades rather than solid ones, so the leaf can carry a woolly white
    /// underside and a marbled green upper face at the same time.
    /// </summary>
    private static void BlackneedleLeaf(MeshData m, Vec3 root, Vec3 dir, double length, Rng rng)
    {
        // The rachis arcs out and over: the leaf starts steep and finishes drooping.
        int nodes = 9;
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        var side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var p = root;
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            double theta = 1.05 * (1.0 - t) + (-0.42) * t;
            var d = (dir * Math.Cos(theta) + Vec3.Up * Math.Sin(theta)).Normalized();
            if (i > 0) p = p + d * (length / nodes);
            path[i] = p;
            radii[i] = 0.0045 * (1.0 - 0.70 * t) + 0.0006;
        }
        Primitives.Tube(m, path, radii, 6, (i, v) => (NeedleStem, 1, i / (double)nodes, v, 0, 0));

        // The lobes, in pairs, angled back towards the base of the leaf.
        int pairs = 5 + rng.NextInt(2);
        for (int k = 0; k < pairs; k++)
        {
            double t = 0.12 + 0.80 * (k / (double)Math.Max(1, pairs - 1));
            int seg = Math.Clamp((int)(t * nodes), 0, nodes);
            var at = path[seg];
            var back = (path[Math.Max(0, seg - 1)] - at).Normalized();
            double lobeLen = length * 0.46 * Math.Sin(Math.PI * Math.Min(1.0, 0.20 + 0.80 * t)) * rng.Range(0.85, 1.15);

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var across = side * sign;
                var lobeDir = (back * 0.62 + across * 0.74 + Vec3.Up * 0.10).Normalized();
                int mark = m.VertexCount;
                var actualLobeTip = NewFloraPetal(m, at, lobeDir, Vec3.Up,
                    lobeLen, lobeLen * 0.30,
                    u => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.10 + 0.90 * u)) * (1.0 - 0.18 * u)),
                    (u, x, vein) =>
                    {
                        var col = Primitives.Mix(NeedleGreen, NeedleMarble, Math.Min(1.0, vein * 0.9));
                        col = Primitives.Mix(col, NeedleMarble, 0.40 * Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(x) * 2.6), 4.0));
                        return col;
                    },
                    rows: 6, across: 7,
                    cup: 0.26, camber: 0.14, tipCurl: 0.20,
                    veinCount: 1, veinDepth: 0.12, veinSharp: 9.0,
                    backColour: (u, x, vein) => Primitives.Mix(NeedleWool, NeedleGreen, 0.18 * (1.0 - u)));

                // Spine embedded solidly inside the actual tip of the curved lobe
                var spineDir = (actualLobeTip - at).Normalized();
                BlackneedleSpine(m, actualLobeTip - spineDir * (lobeLen * 0.08), actualLobeTip + spineDir * (lobeLen * 0.18), 0.0013);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }

        // A spine at the very tip of the rachis
        var rachisTip = path[nodes];
        var rachisDir = (path[nodes] - path[nodes - 1]).Normalized();
        BlackneedleSpine(m, rachisTip - rachisDir * (length * 0.03), rachisTip + rachisDir * (length * 0.06), 0.0012);
    }

    /// <summary>
    /// The winged stem: the thistle's stem is flanged with narrow spiny wings that run down from
    /// every leaf that sits on it.
    /// </summary>
    private static void BlackneedleStem(MeshData m, Vec3 root, double height, Rng rng, out Vec3 tip, out Vec3 tipDir, out Vec3[] stemPath)
    {
        int nodes = 12;
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        var dirs = new Vec3[nodes + 1];
        var p = root;
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            var dir = (Vec3.Up + new Vec3(0.06 * Math.Sin(t * 5.0), 0, 0.06 * Math.Cos(t * 4.0))).Normalized();
            dirs[i] = dir;
            if (i > 0)
            {
                p = p + dir * (height / nodes);
                path[i] = p;
            }
            radii[i] = 0.0042 * (1.0 - 0.40 * t) + 0.0008;
        }
        path[0] = root;
        radii[0] = radii[1];
        Primitives.Tube(m, path, radii, 7, (i, v) => (NeedleStem, 1, i / (double)nodes, v, 0, 0));
        tip = path[nodes];
        tipDir = dirs[nodes];
        stemPath = path;

        // The wings: thin flanges running down the stem from each node, each one spined.
        int wings = 7 + rng.NextInt(4);
        for (int k = 0; k < wings; k++)
        {
            double t = 0.10 + 0.80 * (k / (double)Math.Max(1, wings - 1));
            int seg = Math.Clamp((int)(t * nodes), 0, nodes);
            var at = path[seg];
            var down = (path[Math.Max(0, seg - 1)] - at).Normalized();
            double a = rng.Range(0, 2 * Math.PI);
            var across = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var wingDir = (across * 0.86 + down * 0.48).Normalized();
            double len = height * 0.16 * (1.0 - 0.45 * t);
            int mark = m.VertexCount;
            var actualWingTip = NewFloraBlade(m, at, wingDir, Vec3.Up, len, len * 0.16,
                u => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.08 + 0.92 * u)) * (1.0 - 0.4 * u)),
                u => Primitives.Mix(NeedleStem, NeedleGreen, 0.5),
                rows: 4, sides: 5, flatness: 0.28, keel: 0.28, cup: 0.08, camber: 0.12);

            // Spine embedded securely inside the wing tip
            var wingSpineDir = (actualWingTip - at).Normalized();
            BlackneedleSpine(m, actualWingTip - wingSpineDir * (len * 0.06), actualWingTip + wingSpineDir * (len * 0.14), 0.0010);
            NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
        }
    }

    /// <summary>
    /// The flower head: an involucre of overlapping bracts in whorls, every one of them tipped with
    /// a black glass needle, and out of the middle a brush of violet florets.
    /// Includes a solid green calyx receptacle bridging the stem to the bracts and florets.
    /// </summary>
    private static void BlackneedleHead(MeshData m, Vec3 basePoint, double size, Rng rng)
    {
        // Solid green receptacle torus bridging stem to the bract bases and floret floor
        Primitives.Ellipsoid(m, basePoint + Vec3.Up * (size * 0.12),
            new Vec3(size * 0.28, size * 0.16, size * 0.28),
            6, 12, (u, v) => (NeedleGreen, 1, u, v, 0, 0));

        // The involucre. The bracts are in whorls.
        const int whorls = 4;
        for (int w = 0; w < whorls; w++)
        {
            double wt = w / (double)(whorls - 1);
            int count = 8 + w * 2;
            double bractLen = size * (0.52 + 0.78 * wt);
            double rise = 0.18 + 0.88 * wt;
            for (int k = 0; k < count; k++)
            {
                double a = 2 * Math.PI * k / count + w * 0.41 + rng.Range(-0.08, 0.08);
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var dir = (outward * Math.Cos(rise) + Vec3.Up * Math.Sin(rise)).Normalized();
                var bractBase = basePoint + outward * (size * (0.24 - 0.08 * wt)) + Vec3.Up * (size * 0.10 * wt);
                int mark = m.VertexCount;
                double curLen = bractLen * rng.Range(0.95, 1.05);
                var actualBractTip = NewFloraBlade(m, bractBase, dir, Vec3.Up,
                    curLen, curLen * 0.19,
                    t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.06 + 0.94 * t)) * (1.0 - 0.15 * t)),
                    t =>
                    {
                        var col = Primitives.Mix(NeedleGreen, NeedleBronze, MathD.SmoothStep(0.34, 0.95, t));
                        col = Primitives.Mix(col, NeedleBronze, 0.30 * MathD.SmoothStep(0.72, 1.0, t));
                        return col;
                    },
                    rows: 5, sides: 5, flatness: 0.26, keel: 0.22, camber: 0.10, tipCurl: 0.08);

                // The black glass needle embedded solidly in the true tip of the bract
                var spineDir = (actualBractTip - bractBase).Normalized();
                BlackneedleSpine(m, actualBractTip - spineDir * (curLen * 0.08), actualBractTip + spineDir * (curLen * 0.18), 0.0013);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }

        // The florets: a brush of narrow violet tubes standing up out of the receptacle head.
        int florets = NewFloraCount(m, 52);
        for (int i = 0; i < florets; i++)
        {
            double a = 2 * Math.PI * i / florets * 3.7 + rng.Range(-0.1, 0.1);
            double rr = Math.Sqrt(rng.NextDouble()) * size * 0.20;
            var from = basePoint + Vec3.Up * (size * 0.16) + new Vec3(Math.Cos(a) * rr, 0, Math.Sin(a) * rr);
            double lean = rng.Range(0.05, 0.34);
            var dir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) * Math.Sin(lean) + Vec3.Up * Math.Cos(lean)).Normalized();
            double len = size * rng.Range(0.18, 0.32);
            var to = from + dir * len;

            int mark = m.VertexCount;
            Primitives.Tube(m, new[] { from, from + dir * (len * 0.6), to },
                new[] { size * 0.020, size * 0.016, size * 0.012 }, 5,
                (k, v) => (Primitives.Mix(NeedleVioletDeep, NeedleViolet, k * 0.5), 1, k * 0.5, v, 0, 0));

            // Forked style at the tip
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var forkDir = (dir + new Vec3(Math.Cos(a) * sign * 0.6, 0, Math.Sin(a) * sign * 0.6)).Normalized();
                NewFloraBlade(m, to, forkDir, Vec3.Up, size * 0.13, size * 0.016,
                    t => Math.Max(0.05, 1.0 - t),
                    t => Primitives.Mix(NeedleViolet, NeedleVioletDeep, t * 0.5),
                    rows: 3, sides: 4, flatness: 0.4, keel: 0.1);
            }
            NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
        }
    }

    /// <summary>The thistle: a spiny rosette, a winged stem, and glass-spined violet heads.</summary>
    private static void Blackneedle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // Rosette
        int leaves = NewFloraCount(m, 9);
        for (int i = 0; i < leaves; i++)
        {
            double a = 2 * Math.PI * i / leaves + rng.Range(-0.2, 0.2);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = dir * rng.Range(0.002, 0.012) + Vec3.Up * rng.Range(0.002, 0.010);
            BlackneedleLeaf(part, root, dir, height * rng.Range(0.36, 0.54), rng);
        }

        // The stem
        var stemRoot = Vec3.Up * 0.004;
        BlackneedleStem(part, stemRoot, height * rng.Range(0.62, 0.86), rng, out var tip, out var tipDir, out var stemPath);

        // Heads
        int heads = 1 + rng.NextInt(3);
        for (int i = 0; i < heads; i++)
        {
            Vec3 at;
            if (i == 0)
            {
                at = tip;
            }
            else
            {
                // Attach to actual node along the stem path
                int seg = Math.Clamp((int)((0.55 + 0.35 * rng.NextDouble()) * (stemPath.Length - 1)), 2, stemPath.Length - 2);
                var stemPt = stemPath[seg];
                double a = rng.Range(0, 2 * Math.PI);
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var headBase = stemPt + outward * (height * 0.06) + Vec3.Up * (height * 0.04);
                NewFloraStalk(part, stemPt, headBase, outward, height * 0.015,
                    0.0028, 0.0020, NeedleStem, NeedleStem, segments: 6, sides: 6);
                at = headBase;
            }
            BlackneedleHead(part, at, height * rng.Range(0.080, 0.110), rng);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
