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

        // The lobes, in pairs, angled back towards the base of the leaf. The lower pairs are the
        // longest, which is what gives a thistle leaf its spear shape. The lobes are broad: a
        // thistle leaf is a broad blade cut into spiny divisions, not a bare stem with a few
        // whiskers on it.
        int pairs = 5 + rng.NextInt(2);
        for (int k = 0; k < pairs; k++)
        {
            double t = 0.12 + 0.80 * (k / (double)Math.Max(1, pairs - 1));
            int seg = Math.Clamp((int)(t * nodes), 0, nodes);
            var at = path[seg];
            // The lobe points backwards and outwards, and lies in the plane of the leaf.
            var back = (path[Math.Max(0, seg - 1)] - at).Normalized();
            double lobeLen = length * 0.46 * Math.Sin(Math.PI * Math.Min(1.0, 0.20 + 0.80 * t)) * rng.Range(0.85, 1.15);

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var across = side * sign;
                var lobeDir = (back * 0.62 + across * 0.74 + Vec3.Up * 0.10).Normalized();
                int mark = m.VertexCount;
                NewFloraPetal(m, at, lobeDir, Vec3.Up,
                    lobeLen, lobeLen * 0.30,
                    // A thistle lobe is a broad blade that comes to a point and is wavy at the edge.
                    u => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.10 + 0.90 * u)) * (1.0 - 0.18 * u)),
                    (u, x, vein) =>
                    {
                        var col = Primitives.Mix(NeedleGreen, NeedleMarble, Math.Min(1.0, vein * 0.9));
                        // The marbling runs the length of the veins, not as a wash.
                        col = Primitives.Mix(col, NeedleMarble, 0.40 * Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(x) * 2.6), 4.0));
                        return col;
                    },
                    rows: 6, across: 7,
                    cup: 0.26, camber: 0.14, tipCurl: 0.20,
                    veinCount: 1, veinDepth: 0.12, veinSharp: 9.0,
                    // The underside of a thistle leaf is white wool, and that is the whole family's
                    // signature, so the back of every lobe carries it.
                    backColour: (u, x, vein) => Primitives.Mix(NeedleWool, NeedleGreen, 0.18 * (1.0 - u)));

                // The spine at the end of the lobe.
                var tip = at + lobeDir * lobeLen;
                BlackneedleSpine(m, tip - lobeDir * (lobeLen * 0.06), tip + lobeDir * (lobeLen * 0.18), 0.0013);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }

        // A spine at the very tip of the rachis as well.
        var rachisTip = path[nodes];
        var rachisDir = (path[nodes] - path[nodes - 1]).Normalized();
        BlackneedleSpine(m, rachisTip, rachisTip + rachisDir * (length * 0.05), 0.0012);
    }

    /// <summary>
    /// The winged stem: the thistle's stem is flanged with narrow spiny wings that run down from
    /// every leaf that sits on it.
    /// </summary>
    private static void BlackneedleStem(MeshData m, Vec3 root, double height, Rng rng, out Vec3 tip, out Vec3 tipDir)
    {
        int nodes = 12;
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        var dirs = new Vec3[nodes + 1];
        var p = root;
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            // The stem leans slightly and wanders, the way a thistle's does when it is in fruit.
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
            NewFloraBlade(m, at, wingDir, Vec3.Up, len, len * 0.16,
                u => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.08 + 0.92 * u)) * (1.0 - 0.4 * u)),
                u => Primitives.Mix(NeedleStem, NeedleGreen, 0.5),
                rows: 4, sides: 5, flatness: 0.28, keel: 0.28, cup: 0.08, camber: 0.12);
            var wingTip = at + wingDir * len;
            BlackneedleSpine(m, wingTip - wingDir * (len * 0.05), wingTip + wingDir * (len * 0.12), 0.0010);
            NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
        }
    }

    /// <summary>
    /// The flower head: an involucre of overlapping bracts in whorls, every one of them tipped with
    /// a black glass needle, and out of the middle a brush of violet florets.
    /// </summary>
    private static void BlackneedleHead(MeshData m, Vec3 basePoint, double size, Rng rng)
    {
        // The involucre. The bracts are in whorls: the outer whorl is short and spreads out, and
        // the inner whorls are long and stand up, closing into a cage over the florets. That is
        // the cage, and it is why a thistle head is a ball and not a tuft.
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
                // The inner whorls start higher up the head, so the cage closes from the bottom.
                var bractBase = basePoint + outward * (size * (0.30 - 0.10 * wt)) + Vec3.Up * (size * 0.12 * wt);
                int mark = m.VertexCount;
                NewFloraBlade(m, bractBase, dir, Vec3.Up,
                    bractLen * rng.Range(0.9, 1.1), bractLen * 0.19,
                    // A bract is a narrow lanceolate plate that comes to a point.
                    t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.06 + 0.94 * t)) * (1.0 - 0.15 * t)),
                    t =>
                    {
                        // Green at the foot, tempered to bronze at the tip.
                        var col = Primitives.Mix(NeedleGreen, NeedleBronze, MathD.SmoothStep(0.34, 0.95, t));
                        col = Primitives.Mix(col, NeedleBronze, 0.30 * MathD.SmoothStep(0.72, 1.0, t));
                        return col;
                    },
                    rows: 5, sides: 5, flatness: 0.26, keel: 0.22, camber: 0.10, tipCurl: 0.08);
                // The black glass needle at the point of every bract.
                var bractTip = bractBase + dir * (bractLen * 1.0);
                BlackneedleSpine(m, bractTip - dir * (bractLen * 0.04), bractTip + dir * (bractLen * 0.16), 0.0013);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }

        // The florets: a brush of narrow violet tubes standing up out of the head, each one
        // finished with a forked style. A composite head like this is made of a hundred small
        // florets, and it is the individual florets that give a thistle its brush of colour.
        // They sit INSIDE the involucre: the cage is there to keep them in.
        int florets = NewFloraCount(m, 52);
        for (int i = 0; i < florets; i++)
        {
            double a = 2 * Math.PI * i / florets * 3.7 + rng.Range(-0.1, 0.1);
            double rr = Math.Sqrt(rng.NextDouble()) * size * 0.22;
            var from = basePoint + Vec3.Up * (size * 0.22) + new Vec3(Math.Cos(a) * rr, 0, Math.Sin(a) * rr);
            double lean = rng.Range(0.05, 0.34);
            var dir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) * Math.Sin(lean) + Vec3.Up * Math.Cos(lean)).Normalized();
            double len = size * rng.Range(0.16, 0.30);
            var to = from + dir * len;

            int mark = m.VertexCount;
            Primitives.Tube(m, new[] { from, from + dir * (len * 0.6), to },
                new[] { size * 0.020, size * 0.016, size * 0.012 }, 5,
                (k, v) => (Primitives.Mix(NeedleVioletDeep, NeedleViolet, k * 0.5), 1, k * 0.5, v, 0, 0));
            // The forked style: two tiny blades at the tip of the floret.
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

        // The rosette. The leaves leave the crown in every direction and arch down, so the plant
        // sits on the ground as a ring of spears.
        int leaves = NewFloraCount(m, 9);
        for (int i = 0; i < leaves; i++)
        {
            double a = 2 * Math.PI * i / leaves + rng.Range(-0.2, 0.2);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = dir * rng.Range(0.002, 0.012) + Vec3.Up * rng.Range(0.002, 0.010);
            BlackneedleLeaf(part, root, dir, height * rng.Range(0.36, 0.54), rng);
        }

        // The stem, and the heads on it.
        var stemRoot = Vec3.Up * 0.004;
        BlackneedleStem(part, stemRoot, height * rng.Range(0.62, 0.86), rng, out var tip, out var tipDir);

        int heads = 1 + rng.NextInt(3);
        for (int i = 0; i < heads; i++)
        {
            double t = heads == 1 ? 1.0 : (i == 0 ? 1.0 : 0.55 + 0.35 * rng.NextDouble());
            int seg = Math.Clamp((int)(t * 12), 0, 12);
            var at = seg >= 12 ? tip : Vec3.Lerp(stemRoot, tip, t);
            // A side head grows on its own short stalk out of the axil of a bract.
            if (i > 0)
            {
                double a = rng.Range(0, 2 * Math.PI);
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var headBase = at + outward * (height * 0.05) + Vec3.Up * (height * 0.03);
                NewFloraStalk(part, at, headBase, outward, height * 0.02,
                    0.0022, 0.0016, NeedleStem, NeedleStem, segments: 4, sides: 6);
                at = headBase;
            }
            BlackneedleHead(part, at, height * rng.Range(0.075, 0.105), rng);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
