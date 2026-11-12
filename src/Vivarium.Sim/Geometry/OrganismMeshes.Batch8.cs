using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// SP8 rebuilds: silverweed, mooncoin, brooklace, paddlerib, streamribbon. Authored in real metres and
/// normalised through <see cref="NewFloraAppendWorld"/>. Every leaf hangs from a petiole or a stem node.
/// </summary>
public static partial class OrganismMeshes
{
    private static Vec3 B8Dir(double a) => new Vec3(Math.Cos(a), 0, Math.Sin(a));

    private static void B8Stem(MeshData part, Vec3[] pts, double r0, double r1, double[] c0, double[] c1, int sides = 5)
    {
        var rad = new double[pts.Length];
        for (int i = 0; i < pts.Length; i++) rad[i] = r0 + (r1 - r0) * i / (double)(pts.Length - 1);
        int n = pts.Length - 1;
        Primitives.Tube(part, pts, rad, sides, (st, u) => (Primitives.Mix(c0, c1, st / (double)n), 1.0, st / (double)n, u, 0, 0));
    }

    /// <summary>Flat ribbon blade: three vertices per station (edge, keel, edge), twisting about its tangent.</summary>
    private static void B8Ribbon(MeshData m, Vec3 root, Vec3 outDir, Vec3 flow, double len, double lean, double w0,
        double twist, double sway, double swayPhase, double[] cb, double[] ct, int rows)
    {
        int ribV0 = m.VertexCount, ribI0 = m.Indices.Count;
        var o = new Vec3(outDir.X, 0, outDir.Z).Normalized();
        var lat = new Vec3(-o.Z, 0, o.X);
        Vec3 P(double t) => root + Vec3.Up * (len * t * (1.0 - 0.10 * t))
            + o * (len * lean * t * t) + flow * (len * 0.30 * t * t * t)
            + lat * (len * sway * Math.Sin(swayPhase + t * 4.2) * t);
        var L = new int[rows + 1]; var C = new int[rows + 1]; var R = new int[rows + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var c = P(t);
            var tg = (P(Math.Min(1, t + 0.02)) - P(Math.Max(0, t - 0.02))).Normalized();
            double ang = twist * t;
            var s0 = (o * Math.Cos(ang) + lat * Math.Sin(ang));
            s0 = (s0 - tg * s0.Dot(tg)).Normalized();
            var n = tg.Cross(s0).Normalized();
            double w = i == rows ? 0.0 : w0 * (0.62 + 0.38 * (1 - t)) * (t < 0.1 ? 0.5 + 5 * t : 1.0) * (t > 0.88 ? (1 - t) / 0.12 * 0.85 + 0.15 : 1.0);
            var col = Primitives.Mix(cb, ct, t);
            L[i] = m.AddVertex(c + s0 * w - n * (w * 0.18), n, col, 1, t, 0, 0, 1);
            C[i] = m.AddVertex(c + n * (w * 0.10), n, col, 1, t, 0.5, 0, 1);
            R[i] = m.AddVertex(c - s0 * w - n * (w * 0.18), n, col, 1, t, 1, 0, 1);
        }
        for (int i = 0; i < rows; i++)
        {
            var n0 = m.NormalAt(C[i]);
            Primitives.TriangleFacing(m, L[i], L[i + 1], C[i], n0);
            Primitives.TriangleFacing(m, C[i], L[i + 1], C[i + 1], n0);
            Primitives.TriangleFacing(m, C[i], C[i + 1], R[i], n0);
            Primitives.TriangleFacing(m, R[i], C[i + 1], R[i + 1], n0);
        }
        m.RecordLeaf(ribV0, ribI0, root, len);
    }

    private static void B8Flower(MeshData part, Vec3 c, Vec3 axis, int petals, double len, double rot, double[] pCol, double[] tCol, double[] eye, double eyeR)
    {
        var d = axis.Normalized();
        var (t1, t2) = WsFrame(d);
        for (int k = 0; k < petals; k++)
        {
            double pa = rot + Math.Tau * k / petals;
            var pd = ((t1 * Math.Cos(pa) + t2 * Math.Sin(pa)) * 0.90 + d * 0.38).Normalized();
            B7Blade(part, c, c + pd * len, d, len * 0.46, pCol, tCol, 0.12, false);
        }
        Primitives.Ellipsoid(part, c + d * (eyeR * 0.3), new Vec3(eyeR, eyeR * 0.8, eyeR), 3, 6, (u, v) => (eye, 1.0, u, v, 0.3, 1.0));
    }

    // ---------------------------------------------------------------------------------------------
    // Mooncoin: silver-green coin leaves, each on its own wiry petiole from a rooting node on a ground runner
    // ---------------------------------------------------------------------------------------------
    private static void Mooncoin8(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.20;
        double h = height > 0.01 ? height : 0.06;
        var runnerCol = new[] { 0.36, 0.40, 0.26 };
        var runnerTip = new[] { 0.50, 0.55, 0.34 };
        var petCol = Primitives.Mix(c1, new[] { 0.42, 0.50, 0.32 }, 0.5);
        var coinCentre = Primitives.Scale(c1, 0.80);
        var coinRim = Primitives.Mix(c2, new[] { 0.80, 0.86, 0.72 }, 0.35);
        var rootCol = new[] { 0.38, 0.32, 0.24 };
        var white = new[] { 0.95, 0.96, 0.90 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.003, 0), new Vec3(0.007, 0.004, 0.007), 3, 7, (u, v) => (runnerCol, 1.0, u, v, 0, 0));
        int runners = NewFloraCount(part, 6);
        for (int k = 0; k < runners; k++)
        {
            double a = Math.Tau * k / runners + rng.Range(-0.3, 0.3);
            double reach = r * rng.Range(0.80, 0.97);
            double drift = rng.Range(-0.45, 0.45);
            int nodes = 4;
            var path = new Vec3[nodes + 1];
            var tang = new Vec3[nodes + 1];
            var pos = new Vec3(0, 0.0028, 0);
            for (int i = 0; i <= nodes; i++)
            {
                path[i] = pos;
                a += drift * 0.30 + rng.Range(-0.15, 0.15);
                tang[i] = B8Dir(a);
                pos = pos + tang[i] * (reach / nodes);
                pos = new Vec3(pos.X, 0.0028, pos.Z);
            }
            B8Stem(part, path, 0.0016, 0.0010, runnerCol, runnerTip, 5);
            for (int i = 1; i <= nodes; i++)
            {
                var node = path[i];
                double sc = rng.Range(0.85, 1.15) * (1.0 - 0.15 * i / nodes);
                Primitives.Tube(part, new[] { node, node + new Vec3(0.002, -0.007, 0.001) }, new[] { 0.0009, 0.0004 }, 5, (st, u) => (rootCol, 1.0, st, u, 0, 0));
                int cnt = rng.NextDouble() < 0.35 ? 2 : 1;
                for (int c = 0; c < cnt; c++)
                {
                    double coinR = rng.Range(0.022, 0.034) * sc;
                    double sa = Math.Atan2(tang[i].Z, tang[i].X) + (rng.NextDouble() < 0.5 ? 1 : -1) * rng.Range(0.9, 1.9);
                    var o = B8Dir(sa);
                    double petH = Math.Max(0.010, h * rng.Range(0.30, 0.75));
                    var centre = node + o * (coinR * 0.30) + Vec3.Up * petH;
                    var mid = node + o * (coinR * 0.10) + Vec3.Up * (petH * 0.6);
                    Primitives.Tube(part, new[] { node, mid, centre - Vec3.Up * 0.0012 }, new[] { 0.0010, 0.0008, 0.0007 }, 5,
                        (st, u) => (Primitives.Mix(runnerCol, petCol, st / 2.0), 1.0, st / 2.0, u, 0, 0));
                    var nrm = (Vec3.Up + o * rng.Range(-0.12, 0.20)).Normalized();
                    B7Coin(part, centre, nrm, o, coinR, rng.Range(0.10, 0.22), 14, coinCentre, coinRim, rng.Range(0, Math.Tau));
                }
            }
        }
        for (int c = 0; c < 4; c++)
        {
            double a = Math.Tau * c / 4 + rng.Range(-0.4, 0.4);
            var o = B8Dir(a);
            double coinR = rng.Range(0.024, 0.032);
            double petH = h * rng.Range(0.45, 0.70);
            var centre = o * (coinR * 0.4) + Vec3.Up * petH;
            Primitives.Tube(part, new[] { new Vec3(0, 0.003, 0), o * (coinR * 0.12) + Vec3.Up * (petH * 0.6), centre - Vec3.Up * 0.0012 }, new[] { 0.0013, 0.0010, 0.0008 }, 5,
                (st, u) => (Primitives.Mix(runnerCol, petCol, st / 2.0), 1.0, st / 2.0, u, 0, 0));
            B7Coin(part, centre, (Vec3.Up + o * 0.12).Normalized(), o, coinR, 0.16, 14, coinCentre, coinRim, rng.Range(0, Math.Tau));
        }
        // tiny umbels of pale flowers on thread stalks
        for (int f = 0; f < 3; f++)
        {
            double fa = rng.Range(0, Math.Tau);
            double fr = r * rng.Range(0.2, 0.5);
            var b = new Vec3(Math.Cos(fa) * fr, 0.003, Math.Sin(fa) * fr);
            var top = b + Vec3.Up * (h * rng.Range(0.8, 0.98));
            Primitives.Tube(part, new[] { b, Vec3.Lerp(b, top, 0.5) + B8Dir(fa) * 0.003, top }, new[] { 0.0007, 0.0006, 0.0005 }, 5, (st, u) => (runnerTip, 1.0, st / 2.0, u, 0, 0));
            for (int k = 0; k < 4; k++)
            {
                double ka = Math.Tau * k / 4 + fa;
                Primitives.Ellipsoid(part, top + B8Dir(ka) * 0.003 + Vec3.Up * 0.001, new Vec3(0.0022, 0.0018, 0.0022), 3, 5, (u, v) => (white, 1.0, u, v, 0, 0));
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Brooklace: slender water-starwort, jointed trailing stolons with paired spoon leaves, rosette tips, tiny flowers
    // ---------------------------------------------------------------------------------------------
    private static void Brooklace8(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.11;
        double h = height > 0.01 ? height : 0.10;
        var stemBase = Primitives.Mix(c1, new[] { 0.40, 0.46, 0.26 }, 0.45);
        var stemTip = Primitives.Mix(c2, new[] { 0.50, 0.66, 0.34 }, 0.4);
        var leafBase = Primitives.Scale(c1, 0.85);
        var leafTip = Primitives.Mix(c2, new[] { 0.55, 0.85, 0.35 }, 0.35);
        var rootCol = new[] { 0.80, 0.80, 0.72 };
        var lilac = new[] { 0.80, 0.72, 0.95 };
        var gold = new[] { 1.0, 0.84, 0.30 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.004, 0), new Vec3(0.007, 0.005, 0.007), 3, 7, (u, v) => (stemBase, 1.0, u, v, 0, 0));
        int strands = NewFloraCount(part, 6);
        for (int s = 0; s < strands; s++)
        {
            bool rise = s % 3 == 0;
            double a = Math.Tau * s / strands + rng.Range(-0.3, 0.3);
            double reach = r * rng.Range(0.82, 0.98);
            double topY = rise ? h * rng.Range(0.65, 0.90) : rng.Range(0.012, 0.03);
            int nodes = 5;
            var pts = new Vec3[nodes + 1];
            double drift = rng.Range(-0.5, 0.5);
            var pos = new Vec3(0, 0.004, 0);
            double aa = a;
            for (int i = 0; i <= nodes; i++)
            {
                double t = i / (double)nodes;
                pts[i] = new Vec3(pos.X, 0.004 + (topY - 0.004) * Math.Pow(t, 1.2), pos.Z);
                aa += drift * 0.22 + rng.Range(-0.10, 0.10);
                pos = pos + B8Dir(aa) * (reach / nodes);
            }
            B8Stem(part, pts, 0.0017, 0.0010, stemBase, stemTip, 5);
            for (int i = 1; i <= nodes; i++)
            {
                var node = pts[i];
                var tg = (pts[i] - pts[i - 1]);
                var fwd = new Vec3(tg.X, 0, tg.Z).Normalized();
                var side = new Vec3(-fwd.Z, 0, fwd.X);
                double sz = rng.Range(0.019, 0.028) * (1.0 - 0.18 * i / nodes);
                if (i % 2 == 0) Primitives.Tube(part, new[] { node, node + new Vec3(0, -0.006, 0) + side * 0.001 }, new[] { 0.0007, 0.0003 }, 5, (st, u) => (rootCol, 1.0, st, u, 0, 0));
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var od = (side * sd + fwd * 0.45).Normalized();
                    B7Leaf(part, node, od, sz, 0.95, 0.30, 0.20, sz * 0.40, leafBase, leafTip, 0.20, 3);
                }
                if (i == nodes)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        double ra = Math.Atan2(fwd.Z, fwd.X) + Math.Tau * k / 3 + 0.4;
                        B7Leaf(part, node, B8Dir(ra), sz * 1.1, 0.9, 0.40, 0.15, sz * 0.42, leafBase, leafTip, 0.20, 3);
                    }
                    if (rise && rng.NextDouble() < 0.8)
                        B8Flower(part, node + Vec3.Up * 0.003, Vec3.Up, 4, 0.0065, rng.Range(0, Math.Tau), lilac, new[] { 0.95, 0.9, 1.0 }, gold, 0.0018);
                }
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Silverweed: submerged leafy stems, whorls of fine silvery leaves at every node, tips arching with the current
    // ---------------------------------------------------------------------------------------------
    private static void Silverweed8(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.34;
        double h = height > 0.01 ? height : 0.25;
        var stemBase = Primitives.Mix(c1, new[] { 0.35, 0.30, 0.22 }, 0.5);
        var stemTip = Primitives.Mix(c1, c2, 0.5);
        var leafBase = Primitives.Mix(c1, new[] { 0.14, 0.40, 0.34 }, 0.5);
        var leafTip = Primitives.Mix(c2, new[] { 0.82, 0.92, 0.88 }, 0.55);
        var flow = B8Dir(rng.Range(0, Math.Tau));

        Primitives.Ellipsoid(part, new Vec3(0, 0.003, 0), new Vec3(0.012, 0.006, 0.012), 3, 7, (u, v) => (stemBase, 1.0, u, v, 0, 0));
        int stems = NewFloraCount(part, 8);
        int nodes = 9;
        for (int s = 0; s < stems; s++)
        {
            double a = Math.Tau * s / stems + rng.Range(-0.3, 0.3);
            var o = B8Dir(a);
            double top = h * rng.Range(0.62, 0.98);
            double spread = r * rng.Range(0.25, 0.80);
            var b = o * rng.Range(0.004, 0.018) + new Vec3(0, 0.002, 0);
            var pts = new Vec3[nodes + 1];
            for (int i = 0; i <= nodes; i++)
            {
                double t = i / (double)nodes;
                pts[i] = b + Vec3.Up * (top * t) + o * (spread * t * t) + flow * (0.2 * h * t * t * t);
            }
            B8Stem(part, pts, 0.0021, 0.0009, stemBase, stemTip, 5);
            for (int i = 1; i <= nodes; i++)
            {
                double t = i / (double)nodes;
                var node = pts[i];
                double ll = (0.052 - 0.028 * t) * rng.Range(0.8, 1.15) * (h / 0.25);
                int whorl = i < 3 ? 5 : 6;
                double ph = rng.Range(0, Math.Tau);
                for (int k = 0; k < whorl; k++)
                {
                    double la = ph + Math.Tau * k / whorl;
                    var od = B8Dir(la);
                    var tip = node + od * (ll * 0.80) + Vec3.Up * (ll * (0.10 + 0.5 * t));
                    B7Blade(part, node, tip, new Vec3(-od.Z, 0.1, od.X), 0.0034, leafBase, leafTip, 0.1);
                }
            }
            var tp = pts[nodes];
            for (int k = 0; k < 4; k++)
            {
                double la = Math.Tau * k / 4 + a;
                var od = B8Dir(la);
                B7Blade(part, tp, tp + od * 0.012 + Vec3.Up * 0.028, new Vec3(-od.Z, 0, od.X), 0.003, leafBase, leafTip, 0.1);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Paddlerib: water-plantain rosette of ribbed paddle leaves on tall petioles plus branched white-flower scapes
    // ---------------------------------------------------------------------------------------------
    private static void B8Paddle(MeshData m, Vec3 root, Vec3 dirH, double len, double rise, double droop, double hw, double[] cb, double[] ct, int rows)
    {
        int recV0 = m.VertexCount, recI0 = m.Indices.Count;
        var d = new Vec3(dirH.X, 0, dirH.Z).Normalized();
        var side = new Vec3(-d.Z, 0, d.X);
        Vec3 Centre(double t) => root + d * (len * 0.55 * t) + Vec3.Up * (len * (rise * t - droop * t * t));
        var L = new int[rows + 1]; var C = new int[rows + 1]; var R = new int[rows + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var c = Centre(t);
            var tang = (Centre(Math.Min(1, t + 0.03)) - Centre(Math.Max(0, t - 0.03))).Normalized();
            var n = side.Cross(tang).Normalized();
            if (n.Y < 0) n = -n;
            double w = i == rows ? 0.0 : hw * Math.Pow(Math.Sin(Math.PI * Math.Min(1.0, 0.12 + t * 0.88)), 0.7);
            var col = Primitives.Mix(cb, ct, t);
            var rib = Primitives.Scale(col, 0.82);
            L[i] = m.AddVertex(c + side * w - n * (w * 0.28), n, col, 1, t, 0, 0, 1);
            C[i] = m.AddVertex(c + n * (w * 0.06), n, rib, 1, t, 0.5, 0, 1);
            R[i] = m.AddVertex(c - side * w - n * (w * 0.28), n, col, 1, t, 1, 0, 1);
        }
        for (int i = 0; i < rows; i++)
        {
            var n0 = m.NormalAt(C[i]);
            Primitives.TriangleFacing(m, L[i], L[i + 1], C[i], n0);
            Primitives.TriangleFacing(m, C[i], L[i + 1], C[i + 1], n0);
            Primitives.TriangleFacing(m, C[i], C[i + 1], R[i], n0);
            Primitives.TriangleFacing(m, R[i], C[i + 1], R[i + 1], n0);
        }
        m.RecordLeaf(recV0, recI0, root, len);
    }

    private static void Paddlerib8(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.28;
        double h = height > 0.01 ? height : 0.45;
        var petBase = Primitives.Mix(c1, new[] { 0.30, 0.40, 0.22 }, 0.5);
        var petTip = Primitives.Mix(c1, new[] { 0.40, 0.56, 0.30 }, 0.4);
        var leafBase = Primitives.Mix(c1, new[] { 0.22, 0.46, 0.24 }, 0.5);
        var leafTip = Primitives.Mix(c1, new[] { 0.46, 0.68, 0.36 }, 0.5);
        var white = Primitives.Mix(c2, new[] { 0.98, 0.97, 0.95 }, 0.5);
        var pink = new[] { 0.95, 0.80, 0.82 };
        var gold = new[] { 0.95, 0.80, 0.30 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.006, 0), new Vec3(0.022, 0.012, 0.022), 3, 7, (u, v) => (petBase, 1.0, u, v, 0, 0));
        int leaves = juvenile ? 5 : 9;
        double ph = rng.Range(0, Math.Tau);
        for (int i = 0; i < leaves; i++)
        {
            double a = ph + Math.Tau * i / leaves + rng.Range(-0.2, 0.2);
            var o = B8Dir(a);
            double petL = h * rng.Range(0.28, 0.52);
            double out0 = r * rng.Range(0.18, 0.40);
            double bl = r * rng.Range(0.50, 0.68);
            var p0 = new Vec3(o.X * 0.006, 0.005, o.Z * 0.006);
            var p3 = o * out0 + Vec3.Up * petL;
            var p1 = Vec3.Lerp(p0, p3, 0.33) - o * (out0 * 0.1);
            var p2 = Vec3.Lerp(p0, p3, 0.66) + o * (out0 * 0.05);
            B8Stem(part, new[] { p0, p1, p2, p3 }, 0.0042, 0.0028, petBase, petTip, 5);
            B8Paddle(part, p3, o, bl, 0.55, 0.45, bl * 0.30, leafBase, leafTip, 5);
        }
        int scapes = juvenile ? 0 : 2;
        for (int s = 0; s < scapes; s++)
        {
            double a = ph + 0.6 + Math.PI * s + rng.Range(-0.3, 0.3);
            var o = B8Dir(a);
            double top = h * rng.Range(0.88, 0.98);
            var b = o * 0.006 + new Vec3(0, 0.005, 0);
            var pts = new Vec3[5];
            for (int i = 0; i < 5; i++) { double t = i / 4.0; pts[i] = b + Vec3.Up * (top * t) + o * (0.03 * t * t); }
            B8Stem(part, pts, 0.0030, 0.0014, petBase, petTip, 5);
            for (int tier = 0; tier < 3; tier++)
            {
                double tt = 0.52 + tier * 0.16;
                var node = Vec3.Lerp(pts[2], pts[4], (tt - 0.5) / 0.5);
                int br = 5 - tier;
                for (int k = 0; k < br; k++)
                {
                    double ba = Math.Tau * k / br + tier * 0.7;
                    var bo = B8Dir(ba);
                    double bl = (0.075 - tier * 0.018);
                    var tip = node + bo * bl + Vec3.Up * (bl * 0.7);
                    Primitives.Tube(part, new[] { node, Vec3.Lerp(node, tip, 0.5) + Vec3.Up * 0.004, tip }, new[] { 0.0010, 0.0008, 0.0006 }, 4,
                        (st, u) => (petTip, 1.0, st / 2.0, u, 0, 0));
                    B8Flower(part, tip, Vec3.Up + bo * 0.3, 3, 0.013, rng.Range(0, Math.Tau), white, pink, gold, 0.0022);
                }
            }
            B8Flower(part, pts[4], Vec3.Up, 3, 0.012, rng.Range(0, Math.Tau), white, pink, gold, 0.002);
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Streamribbon: tape-grass; clumps of long twisting ribbon blades streaming with the current, joined by stolons
    // ---------------------------------------------------------------------------------------------
    private static void Streamribbon8(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.18;
        double h = height > 0.01 ? height : 0.48;
        var bladeBase = Primitives.Mix(c1, new[] { 0.30, 0.45, 0.22 }, 0.55);
        var bladeTip = Primitives.Mix(c1, c2, 0.75);
        var stolonCol = new[] { 0.40, 0.46, 0.28 };
        var rootCol = new[] { 0.62, 0.58, 0.46 };
        var flow = B8Dir(rng.Range(0, Math.Tau));
        int clumps = juvenile ? 1 : 3;
        double basePh = rng.Range(0, Math.Tau);
        for (int c = 0; c < clumps; c++)
        {
            Vec3 org;
            if (c == 0) org = new Vec3(0, 0.003, 0);
            else
            {
                double a = basePh + Math.Tau * c / clumps + rng.Range(-0.3, 0.3);
                var o = B8Dir(a);
                double d = r * rng.Range(0.62, 0.85);
                org = o * d + new Vec3(0, 0.003, 0);
                var mid = o * (d * 0.5) + new Vec3(0, 0.005, 0);
                Primitives.Tube(part, new[] { new Vec3(0, 0.003, 0), mid, org }, new[] { 0.0020, 0.0016, 0.0016 }, 5,
                    (st, u) => (stolonCol, 1.0, st / 2.0, u, 0, 0));
            }
            Primitives.Ellipsoid(part, org + new Vec3(0, 0.003, 0), new Vec3(0.010, 0.006, 0.010), 3, 7, (u, v) => (rootCol, 1.0, u, v, 0, 0));
            Primitives.Tube(part, new[] { org, org + new Vec3(0.002, -0.010, 0.001) }, new[] { 0.0014, 0.0004 }, 5, (st, u) => (rootCol, 1.0, st, u, 0, 0));
            int blades = NewFloraCount(part, c == 0 ? (juvenile ? 9 : 14) : 10);
            double hs = c == 0 ? 1.0 : rng.Range(0.62, 0.80);
            double ph = rng.Range(0, Math.Tau);
            for (int i = 0; i < blades; i++)
            {
                double a = ph + Math.Tau * i / blades + rng.Range(-0.25, 0.25);
                double len = h * hs * rng.Range(0.60, 1.0);
                double w = rng.Range(0.0085, 0.0125);
                B8Ribbon(part, org + B8Dir(a) * rng.Range(0.002, 0.008), B8Dir(a), flow, len, rng.Range(0.20, 0.42), w,
                    rng.Range(-2.6, 2.6), rng.Range(0.03, 0.09), rng.Range(0, Math.Tau), bladeBase, bladeTip, 7);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }
}
