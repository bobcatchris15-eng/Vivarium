using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Tankbloom - the tank-forming rosette of humid, bright ground.
/// Analogue: a tank bromeliad: a rosette of strap leaves whose inner bases are swollen into a
/// cupped tank that holds water, with a tall spike of scarlet bracts rising out of the middle.
///
/// A bromeliad is a plant that has turned its own leaves into a cistern. The inner leaves stand
/// upright and their bases are swollen, so the water the plant collects in them is held in a
/// cup; the outer leaves sweep down and out, and they are the ones that shade the tank and keep
/// the water from evaporating. The whole animal depends on that one piece of engineering.
///
/// The fantasy layer is the pool. A real tank holds a scummy film of debris and mosquitoes; this
/// one holds a mirror. The water is perfectly still and darker than the sky, and the leaves that
/// hem it in are waxy and pale on the inside, so the cup reads as cut glass.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The green of the outer leaves.</summary>
    private static readonly double[] TankGreen = { 0.22, 0.40, 0.22 };
    /// <summary>The paler, waxier inner leaves.</summary>
    private static readonly double[] TankPale = { 0.46, 0.60, 0.38 };
    /// <summary>The pale midrib that runs the length of every leaf.</summary>
    private static readonly double[] TankMidrib = { 0.66, 0.74, 0.52 };
    /// <summary>The scarlet bracts of the flower spike.</summary>
    private static readonly double[] TankScarlet = { 0.72, 0.11, 0.07 };
    private static readonly double[] TankScarletDeep = { 0.44, 0.06, 0.05 };
    /// <summary>The still water in the tank.</summary>
    private static readonly double[] TankWater = { 0.04, 0.08, 0.10 };
    /// <summary>The old leaf bases at the foot of the rosette.</summary>
    private static readonly double[] TankBrown = { 0.42, 0.30, 0.18 };
    /// <summary>The small flowers that open between the bracts.</summary>
    private static readonly double[] TankWhite = { 0.90, 0.88, 0.82 };

    /// <summary>
    /// The tank: a shallow cup whose walls are the swollen bases of the inner leaves, holding a
    /// mirror of still water.
    ///
    /// The cup is a surface of revolution with a thick lip, and the water is a shallow lens inside
    /// it whose rim is tucked under that lip, so the pool never floats free of the plant.
    /// </summary>
    private static void TankbloomCup(MeshData m, Vec3 centre, double rimRadius, double depth, Rng rng)
    {
        const int rings = 7;
        const int sectors = 28;
        int mark = m.VertexCount;
        var grid = new int[rings + 1, sectors + 1];
        var normals = new Vec3[rings + 1, sectors + 1];

        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            // The wall rises quickly out of the floor and then turns out to the lip.
            double r = rimRadius * Math.Pow(t, 0.62);
            double y = -depth * (1.0 - Math.Pow(t, 0.78));
            for (int j = 0; j <= sectors; j++)
            {
                double th = 2 * Math.PI * j / sectors;
                // The lip is scalloped, because the leaves that hem the tank in are not equal.
                double scallop = 1.0 + 0.03 * Math.Sin(th * 7.0);
                var p = centre + new Vec3(Math.Cos(th) * r * scallop, y, Math.Sin(th) * r * scallop);
                // The inside of the tank is in its own shadow, and it is waxy rather than green.
                var col = Primitives.Mix(TankBrown, TankGreen, 0.25 + 0.35 * t);
                col = Primitives.Mix(col, TankMidrib, 0.14 * Math.Pow(Math.Max(0.0, Math.Cos(th * 7.0)), 8.0));
                grid[i, j] = m.AddVertex(p, Vec3.Up, col, 1, t, j / (double)sectors, 0, 1);
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
                if (n.Y > 0) n = -n;
                normals[i, j] += n;
                normals[i, j + 1] += n;
                normals[i + 1, j] += n;
                normals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, grid[i, j], grid[i, j + 1], grid[i + 1, j], n);
                Primitives.TriangleFacing(m, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], n);
            }

        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = -Vec3.Up;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // The floor of the tank, so the water has something to sit on.
        var floor = m.AddVertex(centre - Vec3.Up * depth, Vec3.Up, Primitives.Mix(TankBrown, TankPale, 0.4), 1, 0.0, 0.5, 0, 1);
        for (int j = 0; j < sectors; j++)
            Primitives.TriangleFacing(m, floor, grid[0, j + 1], grid[0, j], Vec3.Up);

        // The still water: a shallow lens at the lip, darker than the sky, with a crescent of
        // reflected light along its far edge.
        double waterR = rimRadius * 0.96;
        int wc = m.AddVertex(centre - Vec3.Up * (depth * 0.30), Vec3.Up, TankWater, 1, 0.5, 0.5, 0, 1);
        var rim = new int[26];
        for (int i = 0; i < 26; i++)
        {
            double a = 2 * Math.PI * i / 26;
            double lift = 0.0012 * Math.Max(0.0, Math.Cos(a - 2.1));
            var col = Primitives.Mix(TankWater, TankPale, 0.30 * Math.Max(0.0, Math.Cos(a - 2.1)));
            rim[i] = m.AddVertex(centre - Vec3.Up * (depth * 0.30) + new Vec3(Math.Cos(a) * waterR, lift, Math.Sin(a) * waterR),
                Vec3.Up, col, 1, 0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a), 0, 0);
        }
        for (int i = 0; i < 26; i++)
            Primitives.TriangleFacing(m, wc, rim[i], rim[(i + 1) % 26], Vec3.Up);

        // The inside of the tank is its own material, not a green leaf.
        NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
    }

    /// <summary>
    /// One strap leaf. A bromeliad leaf is a channel: it is folded along its midrib so that it
    /// can hold water, and the fold points DOWN, which makes the upper face a trough.
    /// </summary>
    private static void TankbloomLeaf(MeshData m, Vec3 root, Vec3 dir, double length, double halfWidth,
        double reach, double serration, Rng rng, bool inner)
    {
        int nodes = 8;
        var path = new Vec3[nodes + 1];
        var centre = new Vec3[nodes + 1];
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            // The leaf springs up and then lies over; the inner leaves stay much more upright.
            double theta = inner ? (1.30 * (1.0 - t) + 0.30 * t) : (1.15 * (1.0 - t) - 0.55 * t);
            var d = (dir * Math.Cos(theta) + Vec3.Up * Math.Sin(theta)).Normalized();
            centre[i] = (i == 0) ? root : centre[i - 1] + d * (length / nodes);
            path[i] = centre[i];
        }

        int mark = m.VertexCount;
        NewFloraBlade(m, root,
            (centre[nodes] - root).Normalized(),
            Vec3.Up,
            length,
            halfWidth,
            // A strap leaf is widest a third of the way out and narrows to a point.
            t => Math.Max(0.04, Math.Sin(Math.PI * Math.Min(1.0, 0.08 + 0.92 * t)) * (1.0 - 0.10 * t)),
            t =>
            {
                var col = Primitives.Mix(inner ? TankPale : TankGreen, inner ? TankGreen : TankPale, 0.22 * t);
                // The midrib pales towards the tip.
                col = Primitives.Mix(col, TankMidrib, 0.30 * (1.0 - t) + 0.18);
                return col;
            },
            rows: 8, sides: 7,
            flatness: 0.44,
            // The fold points down: the upper face of the leaf is a trough, and a deep one,
            // because this leaf's whole job is to hold water.
            keel: -0.78,
            cup: 0.10,
            camber: inner ? 0.06 : 0.16,
            tipCurl: 0.18,
            serration: serration,
            teeth: 26,
            midrib: TankMidrib,
            midribWidth: 0.16,
            volumetric: true);
        NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
    }

    /// <summary>The flower spike: overlapping scarlet bracts with small flowers between them.</summary>
    private static void TankbloomSpike(MeshData m, Vec3 basePoint, double height, Rng rng)
    {
        const int whorls = 7;
        for (int w = 0; w < whorls; w++)
        {
            double wt = w / (double)(whorls - 1);
            // The lowest bract sits on the scape's tip, so the spike grows out of the stalk
            // instead of floating above it.
            var at = basePoint + Vec3.Up * (height * (0.04 + 0.96 * wt));
            int count = 3 + (w % 2);
            double bractLen = height * (0.30 - 0.16 * wt);
            for (int k = 0; k < count; k++)
            {
                double a = 2 * Math.PI * k / count + w * 1.1 + rng.Range(-0.15, 0.15);
                var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                // The bracts stand up and curve in over the top of the spike.
                var dir = (outward * Math.Cos(0.55 + 0.5 * wt) + Vec3.Up * Math.Sin(0.55 + 0.5 * wt)).Normalized();
                int mark = m.VertexCount;
                NewFloraBlade(m, at, dir, Vec3.Up, bractLen * rng.Range(0.9, 1.1), bractLen * 0.34,
                    t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.10 + 0.90 * t)) * (1.0 - 0.18 * t)),
                    t => Primitives.Mix(TankScarlet, TankScarletDeep, MathD.SmoothStep(0.35, 1.0, t) * 0.8),
                    rows: 5, sides: 6, flatness: 0.34, keel: 0.18, camber: 0.24, tipCurl: 0.16);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }

        // The small flowers, opening between the bracts.
        int flowers = NewFloraCount(m, 9);
        for (int i = 0; i < flowers; i++)
        {
            double wt = rng.Range(0.08, 0.85);
            double a = rng.Range(0, 2 * Math.PI);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var at = basePoint + Vec3.Up * (height * (0.04 + 0.96 * wt)) + outward * (height * 0.045);
            for (int p = 0; p < 3; p++)
            {
                double pa = a + p * 2.09;
                var pdir = (new Vec3(Math.Cos(pa), 0, Math.Sin(pa)) * 0.7 + Vec3.Up * 0.7).Normalized();
                int mark = m.VertexCount;
                NewFloraPetal(m, at, pdir, Vec3.Up, height * 0.055, height * 0.014,
                    t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.1 + 0.9 * t)) * (1.0 - 0.3 * t)),
                    (t, x, vein) => Primitives.Mix(TankWhite, TankScarlet, vein * 0.35),
                    rows: 4, across: 5, cup: 0.3, camber: 0.1);
                NewFloraMarkOrgan(m, mark, m.VertexCount - mark);
            }
        }
    }

    /// <summary>The bromeliad: a rosette of channels, a pool of still water, a scarlet spike.</summary>
    private static void Tankbloom(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The old leaf bases: the brown skirt that every bromeliad wears round its foot.
        int old = 6 + rng.NextInt(4);
        for (int i = 0; i < old; i++)
        {
            double a = 2 * Math.PI * i / old + rng.Range(-0.2, 0.2);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = dir * rng.Range(0.030, 0.075) + Vec3.Up * 0.002;
            int mark = part.VertexCount;
            NewFloraBlade(part, root, (dir * 0.75 + Vec3.Up * 0.66).Normalized(), Vec3.Up,
                height * rng.Range(0.12, 0.22), height * 0.055,
                t => Math.Max(0.05, Math.Sin(Math.PI * Math.Min(1.0, 0.08 + 0.92 * t)) * (1.0 - 0.55 * t)),
                t => Primitives.Mix(TankBrown, TankGreen, 0.25 * (1.0 - t)),
                rows: 4, sides: 5, flatness: 0.5, keel: -0.2, camber: 0.3, tipCurl: 0.2);
            NewFloraMarkOrgan(part, mark, part.VertexCount - mark);
        }

        // The tank itself, at the heart of the rosette.
        double rimR = height * 0.085;
        TankbloomCup(part, Vec3.Up * (height * 0.075), rimR, height * 0.075, rng);

        // The rosette, in three ranks. The outer rank sweeps down to the ground, the middle rank
        // arches, and the inner rank stands up and closes the cup.
        var ranks = new (int count, double spread, double length, double rise, double width, bool inner)[]
        {
            (11, 0.95, 0.62, 0.60, 0.040, false),
            (9, 0.62, 0.58, 0.90, 0.034, false),
            (7, 0.30, 0.48, 1.18, 0.028, true),
        };
        foreach (var r in ranks)
        {
            int count = NewFloraCount(m, r.count);
            for (int i = 0; i < count; i++)
            {
                double a = 2 * Math.PI * i / count + r.spread * 3.1 + rng.Range(-0.16, 0.16);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                // The inner leaves spring from the lip of the tank; the outer ones from the foot.
                var root = r.inner
                    ? dir * (rimR * 0.92) + Vec3.Up * (height * 0.075)
                    : dir * (rimR * 0.6) + Vec3.Up * (height * 0.020);
                TankbloomLeaf(part, root, dir, height * r.length * rng.Range(0.88, 1.12),
                    height * r.width, r.spread, r.inner ? 0.0 : 0.10, rng, r.inner);
            }
        }

        // The scape: a smooth stalk rising out of the middle of the tank.
        var scapeBase = Vec3.Up * (height * 0.09);
        var scapeTop = scapeBase + Vec3.Up * (height * 0.62);
        NewFloraStalk(part, scapeBase, scapeTop, new Vec3(0.02, 0, 0.01), height * 0.02,
            height * 0.017, height * 0.008, TankPale, TankScarletDeep, segments: 8, sides: 7);
        TankbloomSpike(part, scapeTop, height * 0.30, rng);

        // A couple of aerial roots creeping over the ground.
        for (int i = 0; i < 3; i++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var from = dir * (rimR * 0.8) + Vec3.Up * (height * 0.02);
            var to = from + dir * (height * rng.Range(0.10, 0.20)) + Vec3.Up * (height * 0.01);
            var mid = Vec3.Lerp(from, to, 0.5) + Vec3.Up * (height * 0.02);
            Primitives.Tube(part, new[] { from, mid, to }, new[] { 0.0016, 0.0012, 0.0004 }, 5,
                (k, v) => (TankBrown, 1, k * 0.5, v, 0, 0));
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
