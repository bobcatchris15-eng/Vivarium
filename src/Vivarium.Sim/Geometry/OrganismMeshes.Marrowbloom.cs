using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Marrowbloom - the rootless woodland flower of deep shade.
/// Analogue: Monotropa uniflora, the ghost pipe: a waxy, scaly, nodding white stem carrying a
/// single drooping bell, with no green anywhere in the plant.
///
/// A ghost pipe is a plant that gave up on photosynthesis. It has no leaves, only scales; it has
/// no chlorophyll, only wax; and it lives on fungus, not on soil. Every part of that story has to
/// be visible: the scales stand where leaves would, the stem is fleshy and translucent rather than
/// woody, and the flower hangs because the ovary sits above the bell and the bell is a weight.
///
/// The fantasy layer is the translucency. A ghost pipe is already waxy and pale enough that the
/// light comes through it; here the bell is thin enough to see the shadow of its own stamens
/// through the petal, which is the plant's whole idea made literal. There is no green in the
/// plant at all - it is bleached to bone, with a faint warm flush that comes and goes with the
/// light.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The bleached, waxy stem.</summary>
    private static readonly double[] MarrowBone = { 0.90, 0.87, 0.82 };
    /// <summary>The older, duller tissue low down where the scales are crowded.</summary>
    private static readonly double[] MarrowOld = { 0.78, 0.76, 0.72 };
    /// <summary>The scales: a shade warmer and drier than the stem.</summary>
    private static readonly double[] MarrowScale = { 0.88, 0.86, 0.81 };
    /// <summary>The rose flush the bell carries when it is new.</summary>
    private static readonly double[] MarrowRose = { 0.92, 0.66, 0.66 };
    /// <summary>The dry anthers at the heart of the bell.</summary>
    private static readonly double[] MarrowAnther = { 0.86, 0.72, 0.36 };
    /// <summary>The dark throat inside the bell.</summary>
    private static readonly double[] MarrowThroat = { 0.46, 0.30, 0.30 };

    /// <summary>
    /// A scale: one of the reduced leaves that clothes the stem. A ghost pipe wears its leaves
    /// flat against the stem, overlapping like roof tiles, and they get smaller and sparser as
    /// they go up towards the flower.
    /// </summary>
    private static void MarrowbloomScale(MeshData m, Vec3 at, Vec3 stemDir, double size, Rng rng)
    {
        // The scale lies against the stem and points up along it, flaring out a little at the tip.
        var side = new Vec3(rng.Range(-1, 1), 0, rng.Range(-1, 1));
        if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
        side = side - stemDir * side.Dot(stemDir);
        if (side.LengthSq < 1e-8) side = stemDir.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var outward = stemDir.Cross(side).Normalized();

        int mark = m.VertexCount;
        NewFloraBlade(m, at,
            (stemDir * 0.88 + outward * 0.48).Normalized(),
            outward,
            size * rng.Range(0.85, 1.15),
            size * 0.34,
            // A scale is broad at its base, narrows quickly, and comes to a soft point.
            t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.05 + 0.95 * t)) * (1.0 - 0.35 * t)),
            t => Primitives.Mix(MarrowScale, MarrowOld, 0.35 * (1.0 - t) + 0.2),
            rows: 4, sides: 5, flatness: 0.30, keel: 0.30, cup: 0.10, camber: 0.10);
        // A scale is not a green leaf: it keeps the bone colour it was authored in.
        NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
    }

    /// <summary>
    /// The bell: a waxy, nodding corolla of four to five fused lobes, with a swollen ovary held
    /// above it and the stamens hanging inside.
    ///
    /// The bell is a surface of revolution with a lobed rim, and the lobes are separated by raised
    /// seams that run the whole length of the corolla, the way the real flower's petals are fused
    /// into one tube. It hangs downwards from the attachment, and its mouth turns back on itself.
    /// </summary>
    private static void MarrowbloomBell(MeshData m, Vec3 attachment, Vec3 axis, double size, Rng rng)
    {
        const int rings = 9;
        const int sectors = 24;
        int lobes = 4 + rng.NextInt(2);

        // `down` is the direction the bell hangs in.
        var down = axis.Normalized();
        var side = down.Cross(new Vec3(1, 0, 0));
        if (side.LengthSq < 1e-10) side = down.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var forward = side.Cross(down).Normalized();

        // The profile: a narrow neck under the ovary, a swelling shoulder, and a mouth that flares
        // only a little before its lobes turn back.
        double BellRadius(double t) =>
            size * (0.17 + 0.70 * Math.Pow(Math.Min(1.0, t), 0.55) + 0.14 * MathD.SmoothStep(0.84, 1.0, t));
        double BellY(double t) => size * 1.22 * t;

        int mark = m.VertexCount;
        var grid = new int[rings + 1, sectors + 1];
        var normals = new Vec3[rings + 1, sectors + 1];

        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            double r = BellRadius(t);
            for (int j = 0; j <= sectors; j++)
            {
                double th = 2 * Math.PI * j / sectors;
                // A seam runs down each junction between two lobes: a slight groove in the tube.
                double seam = Math.Pow(Math.Abs(Math.Cos(lobes * th)), 12.0);
                double rr = r * (1.0 - 0.07 * seam * MathD.SmoothStep(0.15, 0.9, t));
                double lobe = 1.0 + 0.055 * Math.Cos(lobes * th) * MathD.SmoothStep(0.55, 1.0, t);
                rr *= lobe;
                var p = attachment + down * BellY(t) + (side * Math.Cos(th) + forward * Math.Sin(th)) * rr;

                // Waxy cream, warming to a rose flush towards the mouth, and the deep throat
                // right down inside the tube.
                var col = Primitives.Mix(MarrowBone, MarrowRose, MathD.SmoothStep(0.45, 1.0, t) * 0.72);
                col = Primitives.Mix(col, MarrowThroat, MathD.SmoothStep(0.55, 0.0, t) * 0.55);
                grid[i, j] = m.AddVertex(p, -down, col, 1, t, j / (double)sectors, 0, 1);
            }
        }

        for (int i = 0; i < rings; i++)
            for (int j = 0; j < sectors; j++)
            {
                var a = m.Position(grid[i, j]);
                var b = m.Position(grid[i, j + 1]);
                var c = m.Position(grid[i + 1, j]);
                var d = m.Position(grid[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                normals[i, j] += n;
                normals[i, j + 1] += n;
                normals[i + 1, j] += n;
                normals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, grid[i, j], grid[i + 1, j], grid[i, j + 1], n);
                Primitives.TriangleFacing(m, grid[i, j + 1], grid[i + 1, j], grid[i + 1, j + 1], n);
            }

        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = -down;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // The mouth is open, so the tube needs a rim: the lobes turn back on themselves, which is
        // what makes the flower read as a bell rather than as a funnel.
        int rimStart = m.VertexCount;
        for (int j = 0; j <= sectors; j++)
        {
            double th = 2 * Math.PI * j / sectors;
            double lobe = 1.0 + 0.055 * Math.Cos(lobes * th);
            var dir = side * Math.Cos(th) + forward * Math.Sin(th);
            // The recurved lip sits a little back up the bell and a little further out.
            var p = attachment + down * (BellY(0.97)) + dir * (BellRadius(1.0) * 1.16 * lobe);
            var col = Primitives.Mix(MarrowRose, MarrowBone, 0.4);
            m.AddVertex(p, -down, col, 1, 1.0, j / (double)sectors, 0, 1);
        }
        for (int j = 0; j < sectors; j++)
        {
            var a = m.Position(grid[rings, j]);
            var b = m.Position(grid[rings, j + 1]);
            var c = m.Position(rimStart + j);
            var d = m.Position(rimStart + j + 1);
            var n = (b - a).Cross(c - a);
            if (n.LengthSq < 1e-18) continue;
            n = n.Normalized();
            Primitives.TriangleFacing(m, grid[rings, j], rimStart + j, grid[rings, j + 1], n);
            Primitives.TriangleFacing(m, grid[rings, j + 1], rimStart + j, rimStart + j + 1, n);
        }

        // The bell keeps its own waxy pigment and its own light through it.
        NewFloraMarkOrgan(m, mark, m.VertexCount - mark);

        // The ovary: the swollen green-white mass that sits above the bell and holds it up.
        Primitives.Ellipsoid(m, attachment + down * (size * 0.06), new Vec3(size * 0.30, size * 0.34, size * 0.30),
            5, 9, (u, v) => (Primitives.Mix(MarrowBone, MarrowOld, 0.3), 1, u, v, 0, 0));

        // The stamens: five to eight filaments hanging inside the bell, each tipped with a dry
        // anther. They are what you can see through the petal.
        int stamens = 5 + rng.NextInt(4);
        for (int i = 0; i < stamens; i++)
        {
            double a = 2 * Math.PI * i / stamens + rng.Range(-0.2, 0.2);
            var outward = (side * Math.Cos(a) + forward * Math.Sin(a)).Normalized();
            var from = attachment + down * (size * 0.04) + outward * (size * 0.09);
            var to = attachment + down * (size * rng.Range(0.62, 0.92)) + outward * (size * rng.Range(0.04, 0.16));
            NewFloraFilament(m, from, to, size * 0.030, MarrowBone, 4);
            // The anther is a small dry club.
            Primitives.Ellipsoid(m, to, new Vec3(size * 0.08, size * 0.14, size * 0.08), 3, 6,
                (u, v) => (MarrowAnther, 1, u, v, 0, 0));
        }
    }

    /// <summary>
    /// The scaly base: a low, knuckled, cream-coloured rhizome clothed in overlapping scales that
    /// look like little crab shells. A ghost pipe's underground stem is the only part of it that is
    /// really in the soil, and it is covered in the same scales as the stem above.
    /// </summary>
    private static void MarrowbloomBase(MeshData m, Vec3 origin, Rng rng)
    {
        var path = new Vec3[5];
        var radii = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double t = i / 4.0;
            path[i] = origin + new Vec3(t * 0.030, 0.002 * Math.Sin(t * 5.0), t * 0.012);
            radii[i] = 0.0055 * (0.8 + 0.5 * Math.Abs(Math.Cos(t * Math.PI * 3)));
        }
        Primitives.Tube(m, path, radii, 8, (i, v) => (MarrowOld, 1, i / 4.0, v, 0, 0));

        // The scales that clothe it, overlapping like tiles.
        int scales = 16;
        for (int i = 0; i < scales; i++)
        {
            double t = i / (double)scales;
            int seg = Math.Min(4, (int)(t * 4));
            var at = path[seg] + new Vec3(rng.Range(-0.004, 0.004), rng.Range(-0.002, 0.004), rng.Range(-0.004, 0.004));
            MarrowbloomScale(m, at, new Vec3(0.6, 0.7, 0.3).Normalized(), 0.011, rng);
        }
    }

    /// <summary>The ghost pipe: a scaly base, bone-white nodding stems, and one bell per stem.</summary>
    private static void Marrowbloom(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        MarrowbloomBase(part, Vec3.Zero, rng);

        int stems = NewFloraCount(m, 7);
        for (int s = 0; s < stems; s++)
        {
            double a = 2 * Math.PI * s / stems + rng.Range(-0.3, 0.3);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = outward * rng.Range(0.004, 0.016);

            // The stem rises, then nods over, and the flower hangs from the nod. The bend starts
            // about two thirds of the way up, which is what makes a ghost pipe look like it is
            // bowing to look at something.
            const int nodes = 18;
            var path = new Vec3[nodes + 1];
            var radii = new double[nodes + 1];
            var dirs = new Vec3[nodes + 1];
            double stemH = height * rng.Range(0.62, 0.86);
            var p = root;
            path[0] = p;
            for (int i = 0; i <= nodes; i++)
            {
                double t = i / (double)nodes;
                double bend = MathD.SmoothStep(0.52, 1.0, t);
                // From straight up to well past horizontal, so the flower ends up hanging.
                double angle = (Math.PI * 0.5) * (1.0 - bend) + (-1.15) * bend;
                var dir = (Vec3.Up * Math.Sin(angle) + outward * Math.Cos(angle)).Normalized();
                dirs[i] = dir;
                if (i > 0)
                {
                    double seg = stemH / nodes;
                    p = p + dir * seg;
                    path[i] = p;
                    // The stem is thickest just above the ground and tapers to the flower.
                    radii[i] = 0.0026 * (1.0 - 0.42 * t) + 0.0007;
                }
            }
            radii[0] = radii[1];
            Primitives.Tube(part, path, radii, 7, (i, v) =>
                (Primitives.Mix(MarrowBone, MarrowOld, 0.45 * (1.0 - i / (double)nodes)), 1, i / (double)nodes, v, 0, 0));

            // The scales. They crowd the foot of the stem and thin out towards the flower, the
            // way the reduced leaves of a non-photosynthetic plant do.
            int scales = 13 + rng.NextInt(6);
            for (int i = 0; i < scales; i++)
            {
                double t = 0.04 + 0.74 * (i / (double)Math.Max(1, scales - 1));
                int seg = Math.Clamp((int)(t * nodes), 0, nodes);
                double size = 0.010 * (1.0 - 0.45 * t) * rng.Range(0.8, 1.2);
                MarrowbloomScale(part, path[seg], dirs[seg], size, rng);
            }

            // The flower hangs from the nod: the bell's axis points along the last stem direction.
            var tip = path[nodes];
            var flowerAxis = dirs[nodes];
            double bellSize = rng.Range(0.012, 0.016);
            MarrowbloomBell(part, tip, flowerAxis, bellSize, rng);

            // A spent flower: the bell has gone over and hangs as a withered rag.
            if (rng.Chance(0.28))
            {
                var withered = Primitives.Mix(MarrowOld, new[] { 0.40, 0.32, 0.24 }, 0.6);
                int mark = part.VertexCount;
                NewFloraBlade(part, tip, (flowerAxis + Vec3.Down * 0.6).Normalized(), Vec3.Up,
                    bellSize * 2.2, bellSize * 0.8,
                    t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.1 + 0.9 * t)) * (1.0 - 0.5 * t)),
                    t => Primitives.Mix(withered, MarrowOld, 0.3),
                    rows: 4, sides: 5, flatness: 0.4, keel: 0.2, cup: 0.35, camber: 0.2, tipCurl: 0.3);
                NewFloraMarkOrgan(part, mark, part.VertexCount - mark);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
