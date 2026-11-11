using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// SP7 rebuilds: prismstar (herb), bonecup, capstar_moss, coinrunner, fencomb.
/// All authored in real metres and normalised through <see cref="NewFloraAppendWorld"/>.
/// Every organ is attached: leaves/coins on petioles on runners, cups on solid stipes sunk into a mycelial cushion.
/// </summary>
public static partial class OrganismMeshes
{
    private static MeshData B7Part(MeshData m) => new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

    /// <summary>Four-triangle keeled lanceolate blade (cheap moss/herb leaf). Single sheet; foliage shader is double sided.</summary>
    private static void B7Blade(MeshData m, Vec3 root, Vec3 tip, Vec3 sideHint, double halfW, double[] cb, double[] ct, double keel = 0.25, bool leaf = true)
    {
        var axis = tip - root;
        double len = axis.Length;
        if (len < 1e-7) return;
        var dir = axis / len;
        var side = sideHint - dir * sideHint.Dot(dir);
        if (side.LengthSq < 1e-10) side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var n = dir.Cross(side).Normalized();
        if (n.Y < 0) { side = -side; n = -n; }
        var cm = Primitives.Mix(cb, ct, 0.4);
        var pm = root + dir * (len * 0.40) + n * (halfW * keel);
        var pl = root + dir * (len * 0.36) + side * halfW - n * (halfW * keel * 0.5);
        var pr = root + dir * (len * 0.36) - side * halfW - n * (halfW * keel * 0.5);
        double lf = leaf ? 1 : 0;
        int a = m.AddVertex(root, n, cb, 1, 0, 0.5, 0, lf);
        int l = m.AddVertex(pl, n, cm, 1, 0.4, 0, 0, lf);
        int r = m.AddVertex(pr, n, cm, 1, 0.4, 1, 0, lf);
        int mm = m.AddVertex(pm, n, cm, 1, 0.4, 0.5, 0, lf);
        int t = m.AddVertex(tip, n, ct, 1, 1, 0.5, 0, lf);
        Primitives.TriangleFacing(m, a, l, mm, n);
        Primitives.TriangleFacing(m, a, mm, r, n);
        Primitives.TriangleFacing(m, l, t, mm, n);
        Primitives.TriangleFacing(m, mm, t, r, n);
    }

    /// <summary>Arching lanceolate leaf: rows+1 stations of left/mid/right, keeled; ~4*rows triangles.</summary>
    private static void B7Leaf(MeshData m, Vec3 root, Vec3 dirH, double len, double hf, double rise, double droop, double hw,
        double[] cb, double[] ct, double keel, int rows = 4)
    {
        var d = new Vec3(dirH.X, 0, dirH.Z).Normalized();
        var side = new Vec3(-d.Z, 0, d.X);
        Vec3 Centre(double t) => root + d * (len * hf * t) + Vec3.Up * (len * (rise * t - droop * t * t));
        var L = new int[rows + 1]; var C = new int[rows + 1]; var R = new int[rows + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var c = Centre(t);
            var tang = (Centre(Math.Min(1, t + 0.03)) - Centre(Math.Max(0, t - 0.03))).Normalized();
            var n = side.Cross(tang).Normalized();
            double w = i == rows ? 0.0 : hw * (0.22 + 0.78 * Math.Pow(Math.Sin(Math.PI * Math.Min(1.0, t * 0.92 + 0.04)), 0.8));
            var col = Primitives.Mix(cb, ct, t);
            L[i] = m.AddVertex(c + side * w - n * (w * keel), n, col, 1, t, 0, 0, 1);
            C[i] = m.AddVertex(c + n * (w * keel * 0.15), n, col, 1, t, 0.5, 0, 1);
            R[i] = m.AddVertex(c - side * w - n * (w * keel), n, col, 1, t, 1, 0, 1);
        }
        for (int i = 0; i < rows; i++)
        {
            var n0 = m.NormalAt(C[i]);
            Primitives.TriangleFacing(m, L[i], L[i + 1], C[i], n0);
            Primitives.TriangleFacing(m, C[i], L[i + 1], C[i + 1], n0);
            Primitives.TriangleFacing(m, C[i], C[i + 1], R[i], n0);
            Primitives.TriangleFacing(m, R[i], C[i + 1], R[i + 1], n0);
        }
    }

    /// <summary>Surface of revolution about <paramref name="axis"/>. Profile is (radius, height along axis, colour, glow).</summary>
    private static void B7Lathe(MeshData m, Vec3 origin, Vec3 axis, (double R, double Y, double[] Col, double Glow)[] prof, int sides,
        double wobble = 0, int lobes = 3, double phase = 0)
    {
        var ax = axis.Normalized();
        var (t1, t2) = WsFrame(ax);
        int n = prof.Length;
        double maxY = 1e-9;
        foreach (var p in prof) maxY = Math.Max(maxY, p.Y);
        int start = m.VertexCount;
        for (int i = 0; i < n; i++)
        {
            var prev = prof[Math.Max(0, i - 1)];
            var next = prof[Math.Min(n - 1, i + 1)];
            double dr = next.R - prev.R, dy = next.Y - prev.Y;
            double nr = dy, ny = -dr;
            double nl = Math.Sqrt(nr * nr + ny * ny);
            if (nl < 1e-12) { nr = 0; ny = 1; } else { nr /= nl; ny /= nl; }
            double w = prof[i].Y / maxY; w *= w;
            for (int s = 0; s <= sides; s++)
            {
                double th = (s % sides) * Math.Tau / sides;
                var rad = t1 * Math.Cos(th) + t2 * Math.Sin(th);
                double wob = 1 + wobble * w * Math.Cos(lobes * th + phase);
                var pos = origin + ax * prof[i].Y + rad * (prof[i].R * wob);
                var nn = (rad * nr + ax * ny).Normalized();
                m.AddVertex(pos, nn, prof[i].Col, 1, i / (double)Math.Max(1, n - 1), s / (double)sides, prof[i].Glow, prof[i].Glow > 0 ? 1.0 : 0.0);
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s, b = a + sides + 1;
                var vn = m.NormalAt(a) + m.NormalAt(b);
                if (prof[i].R < 1e-7) { Primitives.TriangleFacing(m, a, b, b + 1, vn); continue; }
                Primitives.TriangleFacing(m, a, b, a + 1, vn);
                if (prof[i + 1].R >= 1e-7) Primitives.TriangleFacing(m, a + 1, b, b + 1, vn);
            }
    }

    // ---------------------------------------------------------------------------------------------
    // Prismstar: a green star-rosette of two leaf tiers around a crown, with iridescent star blooms on lean stalks
    // ---------------------------------------------------------------------------------------------
    private static void Prismstar7(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.12;
        double h = height > 0.01 ? height : 0.12;
        var leafBase = Primitives.Scale(c1, 0.55);
        var leafTip = Primitives.Mix(c1, new[] { 0.62, 0.84, 0.36 }, 0.45);
        var stalkCol = Primitives.Mix(c1, new[] { 0.55, 0.45, 0.65 }, 0.35);
        var gold = new[] { 1.0, 0.84, 0.30 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.004, 0), new Vec3(0.012, 0.007, 0.012), 3, 7, (u, v) => (leafBase, 1.0, u, v, 0, 0));

        int outer = juvenile ? 7 : 11;
        double ph = rng.Range(0, Math.Tau);
        for (int i = 0; i < outer; i++)
        {
            double a = ph + Math.Tau * i / outer + rng.Range(-0.12, 0.12);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double len = r * rng.Range(0.80, 0.98);
            B7Leaf(part, new Vec3(0, 0.005, 0) + dir * 0.005, dir, len, 0.95, 0.55, 0.42, len * 0.13, leafBase, leafTip, 0.35);
        }
        int inner = juvenile ? 4 : 7;
        double ph2 = ph + Math.PI / outer;
        for (int i = 0; i < inner; i++)
        {
            double a = ph2 + Math.Tau * i / inner + rng.Range(-0.2, 0.2);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double len = r * rng.Range(0.55, 0.72);
            B7Leaf(part, new Vec3(0, 0.006, 0) + dir * 0.003, dir, len, 0.40, 1.05, 0.30, len * 0.15, leafBase, leafTip, 0.30);
        }

        // Blooms: slender lean stalks from the crown, each carrying a flat five-six petal star with a gold eye.
        int stalks = juvenile ? 1 : 4 + rng.NextInt(2);
        var tints = new[] { new[] { 0.62, 0.36, 0.95 }, new[] { 0.95, 0.45, 0.85 }, new[] { 0.45, 0.72, 1.0 }, new[] { 0.78, 0.55, 1.0 } };
        for (int s = 0; s < stalks; s++)
        {
            double a = Math.Tau * s / stalks + rng.Range(-0.4, 0.4);
            var lean = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double top = h * rng.Range(0.70, 0.88);
            double off = rng.Range(0.012, 0.045);
            var p0 = new Vec3(0, 0.003, 0);
            var p1 = p0 + Vec3.Up * (top * 0.45) + lean * (off * 0.2);
            var p2 = p0 + Vec3.Up * (top * 0.80) + lean * (off * 0.65);
            var p3 = p0 + Vec3.Up * top + lean * off;
            Primitives.Tube(part, new[] { p0, p1, p2, p3 }, new[] { 0.0020, 0.0015, 0.0012, 0.0011 }, 6,
                (st, u) => (Primitives.Mix(leafBase, stalkCol, st / 3.0), 1.0, st / 3.0, u, 0, 0));
            var d = (p3 - p2).Normalized();
            var (t1, t2) = WsFrame(d);
            int petals = 5 + (s % 2);
            double petLen = rng.Range(0.014, 0.019);
            double rot = rng.Range(0, Math.Tau);
            var tint = tints[s % tints.Length];
            for (int k = 0; k < petals; k++)
            {
                double pa = rot + Math.Tau * k / petals;
                var pd = ((t1 * Math.Cos(pa) + t2 * Math.Sin(pa)) * 0.88 + d * 0.42).Normalized();
                var hue = Primitives.Mix(tint, tints[(s + k) % tints.Length], 0.55);
                var tipCol = Primitives.Mix(hue, new[] { 1.0, 1.0, 1.0 }, 0.18);
                B7Blade(part, p3 + d * 0.001, p3 + d * 0.001 + pd * petLen, d, petLen * 0.40,
                    Primitives.Mix(new[] { 1.0, 0.95, 1.0 }, hue, 0.45), tipCol, 0.12, false);
            }
            Primitives.Ellipsoid(part, p3 + d * 0.002, new Vec3(0.0034, 0.0030, 0.0034), 3, 6, (u, v) => (gold, 1.0, u, v, 0.4, 1.0));
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Bonecup: alabaster goblets with dark fluid on solid stipes, rising from pale mycelial cushions on a rotted twig
    // ---------------------------------------------------------------------------------------------
    private static void B7Goblet(MeshData m, Vec3 basePt, Vec3 axis, double rimR, double depth, double stipeL, double fluid, int sides,
        double[] outer, double[] inner, double[] fluidCol, double[] stipeCol, double wobble, double phase)
    {
        double sr = rimR * 0.30;
        double L = stipeL, d = depth;
        double yF = L + d * fluid;
        var prof = new (double R, double Y, double[] Col, double Glow)[]
        {
            (sr * 1.6, -0.006, stipeCol, 0),
            (sr * 1.15, L * 0.15, stipeCol, 0),
            (sr * 0.90, L * 0.60, outer, 0),
            (sr * 1.05, L, outer, 0),
            (rimR * 0.46, L + 0.34 * d, outer, 0),
            (rimR * 0.86, L + 0.76 * d, outer, 0),
            (rimR, L + d, outer, 0),
            (rimR * 0.93, L + d, inner, 0),
            (rimR * 0.76, L + (fluid + 0.28 * (1 - fluid)) * d, inner, 0),
            (rimR * 0.52, yF + 0.002, fluidCol, 0),
            (0.0, yF, fluidCol, 0),
        };
        B7Lathe(m, basePt, axis, prof, sides, wobble, 5, phase);
    }

    private static void Bonecup7(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.16;
        double h = height > 0.01 ? height : 0.075;
        var bark = new[] { 0.40, 0.31, 0.23 };
        var barkDark = new[] { 0.26, 0.19, 0.14 };
        var cushion = Primitives.Mix(c1, new[] { 0.62, 0.52, 0.40 }, 0.45);
        var cushionPale = Primitives.Mix(c1, new[] { 0.95, 0.92, 0.85 }, 0.3);
        var outer = Primitives.Mix(c1, new[] { 0.98, 0.96, 0.90 }, 0.6);
        var inner = Primitives.Mix(outer, c2, 0.14);
        var fluid = Primitives.Scale(c2, 0.22);
        var stipe = Primitives.Scale(c1, 0.96);

        // Rotted twig lying along the ground, half sunk, with a broken stub end.
        double az = rng.Range(0, Math.Tau);
        var ax = new Vec3(Math.Cos(az), 0, Math.Sin(az));
        var sd = new Vec3(-ax.Z, 0, ax.X);
        double half = r * 0.88;
        var tw = new Vec3[5];
        var twR = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double t = i / 4.0 * 2 - 1;
            tw[i] = ax * (half * t) + sd * (0.012 * Math.Sin(t * 2.3)) + new Vec3(0, 0.0065 + 0.002 * (1 - Math.Abs(t)), 0);
            twR[i] = 0.0135 * (1 - 0.25 * Math.Abs(t)) * (i == 4 ? 0.8 : 1.0);
        }
        Primitives.Tube(part, tw, twR, 7, (st, u) => (Primitives.Mix(bark, barkDark, (st % 2) * 0.5 + 0.1), 1.0, st / 4.0, u, 0, 0));
        Primitives.Ellipsoid(part, tw[4] + ax * 0.002, new Vec3(0.004, 0.0085, 0.0085), 3, 7, (u, v) => (barkDark, 1.0, u, v, 0, 0));

        // Mycelial cushions + cups, one cluster per cushion.
        int clusters = 3;
        for (int c = 0; c < clusters; c++)
        {
            double t = (c - 1) * 0.62 + rng.Range(-0.08, 0.08);
            var centre = ax * (half * t) + sd * rng.Range(-0.012, 0.012);
            double cr = rng.Range(0.034, 0.044);
            var cc = new Vec3(centre.X, 0.004, centre.Z);
            Primitives.Ellipsoid(part, cc, new Vec3(cr * 1.15, 0.011, cr * 0.95), 5, 14, (u, v) => (Primitives.Mix(cushion, cushionPale, v), 1.0, u, v, 0, 0));
            for (int l = 0; l < 4; l++)
            {
                double la = rng.Range(0, Math.Tau);
                Primitives.Ellipsoid(part, cc + new Vec3(Math.Cos(la), 0, Math.Sin(la)) * (cr * 0.8) - new Vec3(0, 0.003, 0),
                    new Vec3(cr * 0.42, 0.008, cr * 0.38), 3, 7, (u, v) => (cushion, 1.0, u, v, 0, 0));
            }
            double topY = cc.Y + 0.010;
            // main cup(s)
            int big = c == 1 ? 1 : 1;
            double cupR = c == 1 ? rng.Range(0.021, 0.025) : rng.Range(0.015, 0.019);
            double depth = cupR * rng.Range(1.2, 1.4);
            double sl = c == 1 ? 0.022 : rng.Range(0.012, 0.017);
            var lean = new Vec3(rng.Range(-0.10, 0.10), 1, rng.Range(-0.10, 0.10)).Normalized();
            B7Goblet(part, new Vec3(cc.X + rng.Range(-0.004, 0.004), topY - 0.004, cc.Z + rng.Range(-0.004, 0.004)), lean, cupR, depth, sl, 0.45, 12,
                outer, inner, fluid, stipe, 0.07, rng.Range(0, Math.Tau));
            // companions
            int comp = 3 + rng.NextInt(2);
            for (int k = 0; k < comp; k++)
            {
                double a = Math.Tau * k / comp + rng.Range(-0.5, 0.5) + c;
                double dist = cr * rng.Range(0.45, 0.80);
                var bp = new Vec3(cc.X + Math.Cos(a) * dist, cc.Y + 0.010 * (1 - (dist / cr) * (dist / cr)) - 0.003, cc.Z + Math.Sin(a) * dist * 0.82);
                double rr = rng.Range(0.008, 0.013);
                var ln = new Vec3(Math.Cos(a) * 0.30, 1, Math.Sin(a) * 0.30).Normalized();
                B7Goblet(part, bp, ln, rr, rr * rng.Range(1.0, 1.3), rng.Range(0.008, 0.015), 0.35, k == 0 ? 9 : 7,
                    outer, inner, fluid, stipe, 0.05, rng.Range(0, Math.Tau));
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Capstar moss: dense lumpy turf; each shoot a stem topped by a star of spear leaves, fertile ones with seta + gold cap
    // ---------------------------------------------------------------------------------------------
    private static void CapstarMoss7(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.19;
        double h = height > 0.01 ? height : 0.12;
        double hs = juvenile ? 0.6 : 1.0;
        // moss pigment is darkened twice in the shader; lift as the original builder did
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.08), 1.26);
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.05), 1.75);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.28), 1.80);
        var matCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.04), 1.2);
        var rimCol = Primitives.Scale(c1, 0.9);
        var setaCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.45, 0.30, 0.10 }, 0.30), 1.05);
        var capCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.88, 0.62, 0.15 }, 0.42), 1.05);
        var capDeep = Primitives.Scale(capCol, 0.7);
        var hoodCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.92, 0.86, 0.68 }, 0.65), 1.05);

        var olive = new[] { 0.30, 0.34, 0.14 };
        int turf = NewFloraCount(part, juvenile ? 80 : 260);
        for (int t = 0; t < turf; t++)
        {
            double th = rng.Range(0, Math.Tau);
            double f = Math.Sqrt(rng.NextDouble()) * 0.95;
            double ph = f * r * 0.95;
            var tp = new Vec3(Math.Cos(th) * ph, -0.002, Math.Sin(th) * ph);
            double ba = rng.Range(0, Math.Tau);
            var bo = new Vec3(Math.Cos(ba), 0, Math.Sin(ba));
            double bl = rng.Range(0.014, 0.024) * (1.0 - 0.25 * f);
            double tone = rng.Range(0.55, 1.05);
            var bc = Primitives.Scale(Primitives.Mix(leafBase, olive, rng.Range(0.1, 0.7)), tone * 1.25);
            var bt = Primitives.Scale(Primitives.Mix(leafTip, olive, rng.Range(0.2, 0.8)), tone * 1.45);
            B7Blade(part, tp, tp + bo * (bl * 0.55) + Vec3.Up * (bl * 0.8), new Vec3(-bo.Z, 0, bo.X), 0.0032, bc, bt);
        }

        int shoots = NewFloraCount(part, juvenile ? 14 : 28);
        int fertile = juvenile ? 0 : Math.Min(shoots, 5);
        for (int s = 0; s < shoots; s++)
        {
            double th = rng.Range(0, Math.Tau);
            double f = Math.Sqrt(rng.NextDouble()) * 0.93;
            var p = new Vec3(Math.Cos(th) * f * r * 0.93, -0.003, Math.Sin(th) * f * r * 0.93);
            double ht = rng.Range(0.022, 0.042) * (1.0 - 0.30 * f) * hs;
            double la = rng.Range(0, Math.Tau);
            var lean = new Vec3(Math.Cos(la), 0, Math.Sin(la)) * (ht * rng.Range(0.05, 0.25));
            var p1 = p + Vec3.Up * (ht * 0.5) + lean * 0.3;
            var p2 = p + Vec3.Up * ht + lean;
            Primitives.Tube(part, new[] { p, p2 }, new[] { 0.0012, 0.0008 }, 6,
                (st, u) => (stemCol, 1.0, st, u, 0, 0));
            var d = (p2 - p1).Normalized();
            // sparse stem leaves
            double sa = rng.Range(0, Math.Tau);
            for (int k = 0; k < 2; k++)
            {
                double a = sa + Math.PI * k;
                var o = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var rt = Vec3.Lerp(p, p2, 0.45);
                B7Blade(part, rt, rt + o * 0.007 + Vec3.Up * 0.005, new Vec3(-o.Z, 0, o.X), 0.0014, leafBase, leafTip);
            }
            // star head
            int rays = 6;
            double ph = rng.Range(0, Math.Tau);
            double ll = rng.Range(0.0125, 0.0175);
            for (int k = 0; k < rays; k++)
            {
                double a = ph + Math.Tau * k / rays;
                var o = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var tip = p2 + o * (ll * 0.86) + Vec3.Up * (ll * 0.35);
                B7Blade(part, p2 - Vec3.Up * 0.001, tip, new Vec3(-o.Z, 0.1, o.X), 0.0031, leafBase, leafTip);
            }
            for (int k = 0; k < 3; k++)
            {
                double a = ph + 0.4 + Math.Tau * k / 3;
                var o = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                B7Blade(part, p2, p2 + o * (ll * 0.38) + Vec3.Up * (ll * 0.8), new Vec3(-o.Z, 0.1, o.X), 0.0024, leafBase, leafTip);
            }

            if (s < fertile)
            {
                double up = Math.Min(rng.Range(0.040, 0.060), h * 0.90 - 0.012 - p2.Y);
                if (up < 0.02) continue;
                double sa2 = rng.Range(0, Math.Tau);
                var oo = new Vec3(Math.Cos(sa2), 0, Math.Sin(sa2));
                var q0 = p2;
                var q1 = p2 + Vec3.Up * (up * 0.6) + oo * 0.002;
                var q2 = p2 + Vec3.Up * (up * 0.95) + oo * 0.010;
                var q3 = p2 + Vec3.Up * (up * 1.0) + oo * 0.018 - Vec3.Up * 0.002;
                Primitives.Tube(part, new[] { q0, q1, q2, q3 }, new[] { 0.0007, 0.0006, 0.0006, 0.0006 }, 6,
                    (st, u) => (Primitives.Mix(stemCol, setaCol, st / 3.0), 1.0, st / 3.0, u, 0, 0));
                var nod = (oo * 0.85 - Vec3.Up * 0.5).Normalized();
                double cl = 0.0125, cr2 = 0.0042;
                B7Lathe(part, q3, nod, new (double R, double Y, double[] Col, double Glow)[]
                {
                    (cr2 * 0.35, 0, capDeep, 0), (cr2 * 0.95, cl * 0.22, capDeep, 0.5), (cr2, cl * 0.55, capCol, 0.8),
                    (cr2 * 0.75, cl * 0.88, capCol, 0.6), (0.0, cl, capCol, 0)
                }, 6);
                B7Lathe(part, q3 + nod * (cl * 0.86), nod, new (double R, double Y, double[] Col, double Glow)[]
                {
                    (cr2 * 0.8, 0, hoodCol, 0), (cr2 * 0.45, 0.004, hoodCol, 0), (0.0, 0.0085, hoodCol, 0)
                }, 6);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Coinrunner: ground-hugging runners rooting at nodes; every coin leaf stands on its own petiole from a node
    // ---------------------------------------------------------------------------------------------
    private static void B7Coin(MeshData m, Vec3 centre, Vec3 normal, Vec3 fwd, double rad, double dish, int sides,
        double[] cCentre, double[] cRim, double wobPhase)
    {
        var n0 = normal.Normalized();
        var (t1, t2) = WsFrame(n0);
        int start = m.VertexCount;
        // centre + two rings
        m.AddVertex(centre + n0 * (rad * dish * 0.0), n0, cCentre, 1, 0, 0.5, 0, 1);
        int[] ringStart = new int[2];
        for (int ring = 0; ring < 2; ring++)
        {
            double f = (ring + 1) / 2.0;
            ringStart[ring] = m.VertexCount;
            for (int s = 0; s < sides; s++)
            {
                double th = s * Math.Tau / sides;
                double wob = 1 + 0.05 * Math.Sin(th * 7 + wobPhase) + 0.03 * Math.Sin(th * 3 + wobPhase * 2);
                // slight notch where the petiole meets (angle ~ 0 along fwd)
                var radial = t1 * Math.Cos(th) + t2 * Math.Sin(th);
                double rr = rad * f * wob;
                double lift = rad * dish * f * f;
                var p = centre + radial * rr + n0 * lift;
                var nn = (n0 - radial * (2 * dish * f)).Normalized();
                m.AddVertex(p, nn, Primitives.Mix(cCentre, cRim, f), 1, f, s / (double)sides, 0, 1);
            }
        }
        for (int s = 0; s < sides; s++)
        {
            int s2 = (s + 1) % sides;
            Primitives.TriangleFacing(m, start, ringStart[0] + s, ringStart[0] + s2, n0);
            Primitives.TriangleFacing(m, ringStart[0] + s, ringStart[1] + s, ringStart[0] + s2, n0);
            Primitives.TriangleFacing(m, ringStart[0] + s2, ringStart[1] + s, ringStart[1] + s2, n0);
        }
    }

    private static void Coinrunner7(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.30;
        double h = height > 0.01 ? height : 0.05;
        var stem = new[] { 0.30, 0.52, 0.22 };
        var stemTip = new[] { 0.42, 0.66, 0.28 };
        var rootCol = new[] { 0.40, 0.33, 0.24 };
        var coinCentre = Primitives.Mix(c2, new[] { 0.80, 0.92, 0.50 }, 0.35);
        var coinRim = Primitives.Scale(c1, 0.82);
        var petCol = Primitives.Mix(stem, new[] { 0.60, 0.70, 0.35 }, 0.3);
        var white = new[] { 0.95, 0.95, 0.88 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.0025, 0), new Vec3(0.006, 0.004, 0.006), 3, 7, (u, v) => (stem, 1.0, u, v, 0, 0));
        int runners = NewFloraCount(part, 6);
        for (int k = 0; k < runners; k++)
        {
            double az = Math.Tau * k / runners + rng.Range(-0.25, 0.25);
            double reach = r * rng.Range(0.80, 0.98);
            int nodes = 4;
            double drift = rng.Range(-0.5, 0.5);
            int pts = nodes + 1;
            var path = new Vec3[pts];
            var rad = new double[pts];
            var tang = new Vec3[pts];
            double a = az;
            var pos = new Vec3(0, 0.0028, 0);
            for (int i = 0; i < pts; i++)
            {
                path[i] = pos;
                rad[i] = 0.0016 * (1 - 0.4 * i / (double)pts);
                a += drift * 0.35 + rng.Range(-0.18, 0.18);
                tang[i] = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                pos = pos + tang[i] * (reach / nodes);
                pos = new Vec3(pos.X, 0.0028 + 0.0006 * Math.Sin(i * 1.7 + k), pos.Z);
            }
            Primitives.Tube(part, path, rad, 6, (st, u) => (Primitives.Mix(stem, stemTip, st / (double)(pts - 1)), 1.0, st / (double)(pts - 1), u, 0, 0));
            for (int i = 1; i < pts; i++)
            {
                var node = path[i];
                double along = i / (double)nodes;
                double scale = rng.Range(0.85, 1.15) * (1.0 - 0.18 * along);
                // anchoring rootlet
                if (i % 2 == 0) Primitives.Tube(part, new[] { node, node + new Vec3(0.003, -0.008, 0.001) }, new[] { 0.0010, 0.0005 }, 6,
                    (st, u) => (rootCol, 1.0, st, u, 0, 0));
                int petioles = rng.NextDouble() < 0.3 ? 2 : 1;
                for (int pI = 0; pI < petioles; pI++)
                {
                    double coinR = rng.Range(0.026, 0.040) * scale;
                    double petH = h * rng.Range(0.20, 0.50);
                    double sa = Math.Atan2(tang[i].Z, tang[i].X) + (rng.NextDouble() < 0.5 ? 1 : -1) * rng.Range(0.9, 1.9);
                    var out1 = new Vec3(Math.Cos(sa), 0, Math.Sin(sa));
                    var centre = node + out1 * (coinR * rng.Range(0.35, 0.8)) + Vec3.Up * petH;
                    var mid = node + out1 * (coinR * 0.12) + Vec3.Up * (petH * 0.6);
                    Primitives.Tube(part, new[] { node, centre - Vec3.Up * 0.001 }, new[] { 0.0010, 0.0008 }, 6,
                        (st, u) => (Primitives.Mix(stem, petCol, st), 1.0, st, u, 0, 0));
                    var nrm = (Vec3.Up + out1 * rng.Range(0.05, 0.28)).Normalized();
                    B7Coin(part, centre, nrm, out1, coinR, rng.Range(0.06, 0.14), 12, coinCentre, coinRim, rng.Range(0, Math.Tau));
                }
            }
        }
        // crown coins on short petioles
        int crown = NewFloraCount(part, 4);
        for (int c = 0; c < crown; c++)
        {
            double a = Math.Tau * c / crown + rng.Range(-0.3, 0.3);
            var o = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double coinR = rng.Range(0.026, 0.036);
            double petH = h * rng.Range(0.35, 0.65);
            var centre = o * (coinR * 0.9) + Vec3.Up * petH;
            Primitives.Tube(part, new[] { new Vec3(0, 0.003, 0), o * (coinR * 0.2) + Vec3.Up * (petH * 0.6), centre - Vec3.Up * 0.001 }, new[] { 0.0014, 0.0011, 0.0010 }, 6,
                (st, u) => (Primitives.Mix(stem, petCol, st / 2.0), 1.0, st / 2.0, u, 0, 0));
            B7Coin(part, centre, (Vec3.Up + o * 0.15).Normalized(), o, coinR, rng.Range(0.06, 0.14), 12, coinCentre, coinRim, rng.Range(0, Math.Tau));
        }
        // a few small white flower heads on stalks
        for (int f = 0; f < 3; f++)
        {
            double fa = rng.Range(0, Math.Tau);
            double fr = r * rng.Range(0.25, 0.6);
            var b = new Vec3(Math.Cos(fa) * fr, 0.003, Math.Sin(fa) * fr);
            var top = b + Vec3.Up * (h * rng.Range(0.7, 0.95)) + new Vec3(Math.Cos(fa), 0, Math.Sin(fa)) * 0.006;
            Primitives.Tube(part, new[] { b, Vec3.Lerp(b, top, 0.5), top }, new[] { 0.0008, 0.0007, 0.0006 }, 6, (st, u) => (stemTip, 1.0, st / 2.0, u, 0, 0));
            Primitives.Ellipsoid(part, top + Vec3.Up * 0.002, new Vec3(0.0055, 0.0038, 0.0055), 3, 6, (u, v) => (white, 1.0, u, v, 0, 0));
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Fencomb: a fan of flexuous red-tinted stems with whorls of flat comb leaves and a red flower spike each
    // ---------------------------------------------------------------------------------------------
    private static void B7Comb(MeshData m, Vec3 root, Vec3 dir, Vec3 side, Vec3 lift, double len, int pairs, double toothLen, double[] cb, double[] ct)
    {
        var n = dir.Cross(side).Normalized();
        if (n.Dot(lift) < 0) n = -n;
        for (int i = 0; i < pairs; i++)
        {
            double t0 = i / (double)pairs, t1 = (i + 1) / (double)pairs, tm = (t0 + t1) * 0.5;
            Vec3 P(double t) => root + dir * (len * t) + lift * (len * 0.22 * t * t);
            var p0 = P(t0); var p1 = P(t1);
            double tl = toothLen * Math.Sin(Math.PI * Math.Min(1, tm * 0.92 + 0.06)) * (1.0 - 0.3 * tm);
            var mid = P(tm);
            var tl3 = mid - side * tl + dir * (tl * 0.55) + lift * (tl * 0.35);
            var tr3 = mid + side * tl + dir * (tl * 0.55) + lift * (tl * 0.35);
            var colA = Primitives.Mix(cb, ct, t0);
            var colB = Primitives.Mix(cb, ct, t1);
            var colT = Primitives.Mix(cb, ct, Math.Min(1, tm + 0.35));
            int a = m.AddVertex(p0, n, colA, 1, t0, 0.5, 0, 1);
            int b = m.AddVertex(p1, n, colB, 1, t1, 0.5, 0, 1);
            int l = m.AddVertex(tl3, n, colT, 1, tm, 0, 0, 1);
            int r = m.AddVertex(tr3, n, colT, 1, tm, 1, 0, 1);
            Primitives.TriangleFacing(m, a, b, l, n);
            Primitives.TriangleFacing(m, a, b, r, n);
        }
    }

    private static void Fencomb7(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.16;
        double h = height > 0.01 ? height : 0.42;
        var stemBase = Primitives.Mix(c2, new[] { 0.45, 0.20, 0.14 }, 0.5);
        var stemTip = Primitives.Mix(c1, c2, 0.45);
        var leafBase = Primitives.Mix(c1, new[] { 0.14, 0.44, 0.24 }, 0.5);
        var leafTip = Primitives.Mix(c1, new[] { 0.48, 0.80, 0.42 }, 0.5);
        var spikeCol = Primitives.Mix(c2, new[] { 0.80, 0.30, 0.24 }, 0.6);
        var spikeTip = new[] { 0.92, 0.62, 0.50 };
        var rootCol = new[] { 0.28, 0.22, 0.15 };

        int stems = juvenile ? 3 : 5;
        double ph0 = rng.Range(0, Math.Tau);
        for (int s = 0; s < stems; s++)
        {
            double a = ph0 + Math.Tau * s / stems + rng.Range(-0.3, 0.3);
            var outd = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var perp = new Vec3(-outd.Z, 0, outd.X);
            double rb = rng.Range(0.006, 0.020);
            var b0 = outd * rb + new Vec3(0, 0.0, 0);
            double top = h * rng.Range(0.72, 0.95) * (juvenile ? 0.65 : 1.0) * (s == 0 ? 1.0 : 0.9);
            double lean = r * rng.Range(0.25, 0.75);
            double wp = rng.Range(0, Math.Tau);
            const int segs = 7;
            var path = new Vec3[segs + 1];
            var rads = new double[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                double t = i / (double)segs;
                path[i] = b0 + outd * (lean * t * t + 0.012 * Math.Sin(t * 5 + wp) * t) + perp * (0.010 * Math.Sin(t * 4 + wp) * t) + Vec3.Up * (top * t - 0.004 * (1 - t));
                rads[i] = 0.0040 * (1 - 0.55 * t);
            }
            Primitives.Tube(part, path, rads, 6, (st, u) => (Primitives.Mix(stemBase, stemTip, st / (double)segs), 1.0, st / (double)segs, u, 0, 0));
            // rootlets
            for (int k = 0; k < 2; k++)
            {
                double ra = a + k * Math.PI + rng.Range(-0.5, 0.5);
                var rd = new Vec3(Math.Cos(ra), 0, Math.Sin(ra));
                Primitives.Tube(part, new[] { path[0], path[0] + rd * 0.02 - Vec3.Up * 0.006, path[0] + rd * 0.04 - Vec3.Up * 0.012 }, new[] { 0.0020, 0.0013, 0.0007 }, 6,
                    (st, u) => (rootCol, 1.0, st / 2.0, u, 0, 0));
            }
            // whorls of comb leaves
            int whorls = juvenile ? 4 : 7;
            for (int w = 0; w < whorls; w++)
            {
                double t = 0.12 + 0.80 * w / (double)(whorls - 1 + 0.001);
                double ft = t * segs;
                int i0 = Math.Min(segs - 1, (int)ft);
                var node = Vec3.Lerp(path[i0], path[i0 + 1], ft - i0);
                var fwd = (path[i0 + 1] - path[i0]).Normalized();
                var sA = fwd.Cross(Vec3.Up); if (sA.LengthSq < 1e-4) sA = fwd.Cross(Vec3.Right);
                sA = sA.Normalized();
                var sB = sA.Cross(fwd).Normalized();
                double leafLen = (0.070 + 0.055 * Math.Sin(Math.Min(1, t * 1.1) * Math.PI)) * (1.05 - 0.35 * t);
                int nl = 4;
                double wph = w * 0.7 + s;
                for (int l = 0; l < nl; l++)
                {
                    double la = wph + Math.Tau * l / nl;
                    var radial = sA * Math.Cos(la) + sB * Math.Sin(la);
                    var ldir = (radial * 0.90 + fwd * 0.30).Normalized();
                    var lside = ldir.Cross(fwd).Normalized();
                    B7Comb(part, node, ldir, lside, fwd, leafLen, 6, leafLen * 0.34, leafBase, leafTip);
                }
                            }
            // flower spike with beaded florets
            var tip = path[segs];
            var sd = (path[segs] - path[segs - 1]).Normalized();
            double sl = rng.Range(0.045, 0.065) * (juvenile ? 0.6 : 1.0);
            var sp = new Vec3[4];
            var sr = new double[] { 0.0030, 0.0040, 0.0038, 0.0012 };
            for (int i = 0; i < 4; i++) sp[i] = tip + sd * (sl * i / 3.0);
            Primitives.Tube(part, sp, sr, 6, (st, u) => (Primitives.Mix(spikeCol, spikeTip, st / 3.0), 1.0, st / 3.0, u, 0, 0));
        }
        NewFloraAppendWorld(m, part, r, h);
    }
}
