using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// SP9 rebuilds: cinderberry, cloudtuft, lanternsedge, fenbead, rain_jelly. Authored in real metres and
/// normalised through <see cref="NewFloraAppendWorld"/>. Every organ hangs from a stem, petiole or pedicel.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>Pointed spindle / bead along an arbitrary axis (leaf-bead, seed head, lantern).</summary>
    private static void B9Spindle(MeshData m, Vec3 b, Vec3 dir, double len, double rad, double[] cb, double[] ct,
        int rings, int segs, double glow = 0, double blunt = 0.7)
    {
        dir = dir.Normalized();
        var (t1, t2) = WsFrame(dir);
        int start = m.VertexCount;
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            double w = rad * Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), blunt);
            double dw = rad * blunt * Math.Pow(Math.Max(1e-4, Math.Sin(Math.PI * t)), blunt - 1) * Math.Cos(Math.PI * t) * Math.PI;
            var col = Primitives.Mix(cb, ct, t);
            for (int s = 0; s <= segs; s++)
            {
                double th = (s % segs) * Math.Tau / segs;
                var radial = t1 * Math.Cos(th) + t2 * Math.Sin(th);
                var n = (radial * len - dir * dw).Normalized();
                m.AddVertex(b + dir * (len * t) + radial * w, n, col, 1, t, s / (double)segs, glow, glow > 0 ? 1.0 : 0.0);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int s = 0; s < segs; s++)
            {
                int a = start + i * (segs + 1) + s, c = a + segs + 1;
                var vn = m.NormalAt(a) + m.NormalAt(c);
                Primitives.TriangleFacing(m, a, c, a + 1, vn);
                Primitives.TriangleFacing(m, a + 1, c, c + 1, vn);
            }
    }

    // ---------------------------------------------------------------------------------------------
    // Cinderberry: low strawberry-like patch. Trifoliate leaves on petioles, white flowers on stalks, runners
    // ---------------------------------------------------------------------------------------------
    private static void Cinderberry9(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.30;
        double h = height > 0.01 ? height : 0.10;
        double sz = juvenile ? 0.65 : 1.0;
        var leafBase = Primitives.Scale(c1, 1.05);
        var leafTip = Primitives.Mix(c1, new[] { 0.60, 0.80, 0.30 }, 0.45);
        var stemCol = Primitives.Mix(c1, new[] { 0.55, 0.38, 0.28 }, 0.40);
        var stemTip = Primitives.Mix(c1, new[] { 0.50, 0.46, 0.30 }, 0.30);
        var white = Primitives.Scale(c2, 1.05);
        var gold = new[] { 0.98, 0.80, 0.22 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.006, 0), new Vec3(0.022, 0.009, 0.022), 3, 7, (u, v) => (stemCol, 1.0, u, v, 0, 0));

        int n = NewFloraCount(part, juvenile ? 8 : 13);
        for (int i = 0; i < n; i++)
        {
            double az = i * Math.Tau / n + rng.Range(-0.2, 0.2);
            var d = B8Dir(az);
            var start = d * 0.012 + new Vec3(0, 0.008, 0);
            var apex = d * (r * rng.Range(0.20, 0.58) * sz) + new Vec3(0, h * rng.Range(0.55, 0.92) * sz, 0);
            var mid = Vec3.Lerp(start, apex, 0.5) + new Vec3(0, h * 0.18, 0);
            B8Stem(part, new[] { start, mid, apex }, 0.0022, 0.0012, stemCol, stemTip, 6);
            double L = h * rng.Range(0.26, 0.34) * sz;
            for (int lf = 0; lf < 3; lf++)
            {
                double la = az + (lf - 1) * 0.85 + rng.Range(-0.08, 0.08);
                var ld = (B8Dir(la) * 0.95 + new Vec3(0, 0.30, 0)).Normalized();
                double len = lf == 1 ? L * 1.05 : L;
                Primitives.CurvedLeaf(part, apex, apex + ld * len, Vec3.Up.Cross(ld), len * 0.45, leafBase, leafTip, 0.003, 4);
            }
        }

        int flowers = NewFloraCount(part, juvenile ? 2 : 4);
        for (int f = 0; f < flowers; f++)
        {
            double az = f * Math.Tau / Math.Max(1, flowers) + rng.Range(-0.4, 0.4) + 0.6;
            var d = B8Dir(az);
            var start = d * 0.012 + new Vec3(0, 0.008, 0);
            var top = d * (r * rng.Range(0.22, 0.42) * sz) + new Vec3(0, h * rng.Range(0.95, 1.18) * sz, 0);
            var mid = Vec3.Lerp(start, top, 0.5) + d * 0.004;
            B8Stem(part, new[] { start, mid, top }, 0.0013, 0.0008, stemCol, stemTip, 6);
            B8Flower(part, top, (Vec3.Up * 0.9 + d * 0.25).Normalized(), 5, 0.012 * sz, rng.Range(0, Math.Tau), white, Primitives.Scale(c2, 0.92), gold, 0.0028);
        }

        // Scarlet berries hang from arching pedicels rising out of the crown; calyx star on each.
        var berryB = new[] { 0.95, 0.20, 0.10 };
        var berryT = new[] { 0.72, 0.06, 0.05 };
        var calyx = Primitives.Mix(leafBase, leafTip, 0.5);
        int berries = juvenile ? 2 : 5;
        for (int b = 0; b < berries; b++)
        {
            double az = b * Math.Tau / berries + rng.Range(-0.3, 0.3) + 0.2;
            var d = B8Dir(az);
            var start = d * 0.014 + new Vec3(0, 0.008, 0);
            double reach = r * rng.Range(0.14, 0.30) * sz;
            var p1 = start + d * (reach * 0.35) + new Vec3(0, h * 0.55 * sz, 0);
            var p2 = start + d * (reach * 0.80) + new Vec3(0, h * 0.62 * sz, 0);
            var p3 = start + d * reach + new Vec3(0, h * 0.40 * sz, 0);
            B8Stem(part, new[] { start, p1, p2, p3 }, 0.0013, 0.0009, stemCol, stemTip, 6);
            double bl = 0.021 * sz;
            B9Spindle(part, p3 - new Vec3(0, bl * 0.95, 0), Vec3.Up, bl, 0.0092 * sz, berryT, berryB, 4, 8, 0, 0.55);
            for (int c = 0; c < 5; c++)
            {
                double ca = c * Math.Tau / 5 + az;
                var cd = (B8Dir(ca) * 0.9 + new Vec3(0, 0.15, 0)).Normalized();
                B7Blade(part, p3 - new Vec3(0, 0.002, 0), p3 - new Vec3(0, 0.002, 0) + cd * 0.007 * sz, Vec3.Up, 0.0017, calyx, calyx, 0.1);
            }
        }

        int runners = juvenile ? 1 : 2;
        double ra = rng.Range(0, Math.Tau);
        for (int s = 0; s < runners; s++)
        {
            double az = ra + s * 2.4;
            var d = B8Dir(az); var side = new Vec3(-d.Z, 0, d.X);
            double L = r * rng.Range(0.65, 0.85) * sz;
            var pts = new[]
            {
                new Vec3(0, 0.006, 0), d * (L * 0.3) + side * (L * 0.06) + new Vec3(0, 0.010, 0),
                d * (L * 0.65) - side * (L * 0.05) + new Vec3(0, 0.007, 0), d * L + new Vec3(0, 0.004, 0),
            };
            B8Stem(part, pts, 0.0011, 0.0007, stemCol, stemTip, 6);
            var pl = pts[3];
            double pLen = h * 0.30;
            var pet = pl + new Vec3(0, h * 0.32, 0);
            B8Stem(part, new[] { pl, pet }, 0.0012, 0.0009, stemCol, stemTip, 6);
            for (int lf = 0; lf < 3; lf++)
            {
                double la = az + (lf - 1) * 0.9;
                var ld = (B8Dir(la) * 0.95 + new Vec3(0, 0.30, 0)).Normalized();
                Primitives.CurvedLeaf(part, pet, pet + ld * pLen, Vec3.Up.Cross(ld), pLen * 0.42, leafBase, leafTip, 0.005, 4);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Cloudtuft: fine arching blades, tall culms each topped by a fluffy cream seed head with fine awns
    // ---------------------------------------------------------------------------------------------
    private static void Cloudtuft9(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.18;
        double h = height > 0.01 ? height : 0.55;
        double sz = juvenile ? 0.6 : 1.0;
        var bladeB = Primitives.Scale(c1, 0.78);
        var bladeT = Primitives.Mix(c1, new[] { 0.72, 0.74, 0.42 }, 0.35);
        var culmB = Primitives.Mix(c1, new[] { 0.55, 0.60, 0.38 }, 0.4);
        var culmT = Primitives.Mix(c1, c2, 0.4);
        var fluffB = Primitives.Mix(c1, c2, 0.55);
        var fluff = Primitives.Scale(c2, 1.04);

        int blades = NewFloraCount(part, juvenile ? 14 : 26);
        double ph = rng.Range(0, Math.Tau);
        for (int i = 0; i < blades; i++)
        {
            double az = ph + i * 2.39996 + rng.Range(-0.1, 0.1);
            var o = B8Dir(az);
            var root = o * rng.Range(0.002, 0.014);
            double len = h * rng.Range(0.34, 0.74) * sz;
            B8Ribbon(part, root, o, o * 0.75, len, 0.20, 0.0030, rng.Range(-0.5, 0.5), 0.04, rng.Range(0, Math.Tau), bladeB, bladeT, 5);
        }

        int culms = NewFloraCount(part, juvenile ? 3 : 7);
        for (int c = 0; c < culms; c++)
        {
            double az = c * 2.39996 + ph;
            var o = B8Dir(az);
            var root = o * rng.Range(0.002, 0.014);
            double ch = h * rng.Range(0.80, 0.98) * sz;
            double lean = rng.Range(0.05, 0.12) * ch;
            var pts = new Vec3[5];
            for (int k = 0; k < 5; k++)
            {
                double t = k / 4.0;
                pts[k] = root + o * (lean * t * t) + new Vec3(0, ch * t, 0);
            }
            B8Stem(part, pts, 0.0017, 0.0009, culmB, culmT, 6);
            var dir = (pts[4] - pts[3]).Normalized();
            double hl = 0.044 * sz;
            B9Spindle(part, pts[4] - dir * 0.006, dir, hl, 0.0075 * sz, culmT, fluffB, 3, 6, 0, 0.8);
            B9Spindle(part, pts[4] + dir * 0.004, dir, hl * 0.9, 0.0125 * sz, fluffB, fluff, 4, 7, 0, 0.75);
            var (t1, t2) = WsFrame(dir);
            for (int a = 0; a < 18; a++)
            {
                double ang = a * 2.39996;
                double f = 0.15 + 0.80 * ((a * 0.618) % 1.0);
                var radial = t1 * Math.Cos(ang) + t2 * Math.Sin(ang);
                var bp = pts[4] + dir * (0.004 + hl * 0.9 * f) + radial * (0.0095 * sz * Math.Sin(Math.PI * f));
                var ad = (radial * 0.75 + dir * 0.80).Normalized();
                B7Blade(part, bp, bp + ad * (0.020 * sz), t1, 0.0011, fluffB, fluff, 0.1, false);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Lanternsedge: leafy clump of broad blades, sturdy stalks carrying umbels of glowing lantern buds
    // ---------------------------------------------------------------------------------------------
    private static void Lanternsedge9(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.22;
        double h = height > 0.01 ? height : 0.55;
        double sz = juvenile ? 0.6 : 1.0;
        var bladeB = new[] { 0.06, 0.12, 0.08 };
        var bladeT = Primitives.Mix(c1, new[] { 0.10, 0.20, 0.12 }, 0.5);
        var stalkB = new[] { 0.36, 0.09, 0.20 };
        var stalkT = new[] { 0.52, 0.18, 0.30 };
        var lanternB = Primitives.Mix(c2, new[] { 0.55, 0.95, 0.65 }, 0.45);
        var lanternT = new[] { 0.78, 1.0, 0.82 };

        int blades = NewFloraCount(part, juvenile ? 9 : 17);
        double ph = rng.Range(0, Math.Tau);
        for (int i = 0; i < blades; i++)
        {
            double az = ph + i * 2.39996 + rng.Range(-0.1, 0.1);
            var o = B8Dir(az);
            B8Ribbon(part, o * rng.Range(0.002, 0.016), o, o * 0.7, h * rng.Range(0.36, 0.72) * sz, 0.24, 0.0070, rng.Range(-0.4, 0.4), 0.03, rng.Range(0, Math.Tau), bladeB, bladeT, 5);
        }

        int stalks = NewFloraCount(part, juvenile ? 2 : 5);
        for (int s = 0; s < stalks; s++)
        {
            double az = ph + s * 2.39996 + 0.5;
            var o = B8Dir(az);
            var root = o * rng.Range(0.002, 0.012);
            double sh = h * rng.Range(0.80, 0.98) * sz;
            double lean = rng.Range(0.03, 0.09) * sh;
            var pts = new Vec3[5];
            for (int k = 0; k < 5; k++)
            {
                double t = k / 4.0;
                pts[k] = root + o * (lean * t * t) + new Vec3(0, sh * t, 0);
            }
            B8Stem(part, pts, 0.0048, 0.0030, stalkB, stalkT, 6);
            var top = pts[4];
            for (int b = 0; b < 3; b++)
            {
                double ba = az + b * Math.Tau / 3 + 0.4;
                var bd = (B8Dir(ba) * 0.8 + new Vec3(0, 0.55, 0)).Normalized();
                var b0 = top - new Vec3(0, 0.004, 0);
                B7Blade(part, b0, b0 + bd * 0.07 * sz, Vec3.Up.Cross(bd), 0.0045, bladeB, bladeT, 0.2);
            }
            int rays = juvenile ? 5 : 8;
            for (int k = 0; k < rays; k++)
            {
                double ra = k * Math.Tau / rays + rng.Range(-0.15, 0.15) + s;
                double len = rng.Range(0.055, 0.080) * sz;
                var rd = (B8Dir(ra) * 0.85 + new Vec3(0, 0.55, 0)).Normalized();
                var p0 = top; var p2 = top + rd * len;
                var p1 = Vec3.Lerp(p0, p2, 0.5) + new Vec3(0, 0.004, 0);
                B8Stem(part, new[] { p0, p1, p2 }, 0.0012, 0.0007, stalkT, stalkT, 6);
                B9Spindle(part, p2 - new Vec3(0, 0.002, 0), (rd * 0.5 + new Vec3(0, 1, 0)).Normalized(), 0.016 * sz, 0.0058 * sz, lanternB, lanternT, 3, 6, 0.9, 0.7);
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Fenbead: arching, tapering closed stems; opposite plump bead-leaves at every node; small white flowers
    // ---------------------------------------------------------------------------------------------
    private static void Fenbead9(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.18;
        double h = height > 0.01 ? height : 0.10;
        double sz = juvenile ? 0.65 : 1.0;
        var stemB = Primitives.Mix(c1, new[] { 0.55, 0.48, 0.30 }, 0.35);
        var stemT = Primitives.Mix(c1, new[] { 0.60, 0.80, 0.40 }, 0.30);
        var beadB = Primitives.Scale(c1, 0.85);
        var beadT = Primitives.Mix(c1, new[] { 0.62, 0.84, 0.46 }, 0.40);
        var flowerC = Primitives.Scale(c2, 1.04);
        var gold = new[] { 0.95, 0.78, 0.25 };

        Primitives.Ellipsoid(part, new Vec3(0, 0.004, 0), new Vec3(0.012, 0.006, 0.012), 3, 7, (u, v) => (stemB, 1.0, u, v, 0, 0));
        int stems = NewFloraCount(part, juvenile ? 4 : 7);
        double ph = rng.Range(0, Math.Tau);
        for (int s = 0; s <= stems; s++)
        {
            bool centre = s == stems;
            double az = ph + s * Math.Tau / stems + rng.Range(-0.2, 0.2);
            var o = B8Dir(az);
            double R = centre ? 0.006 : r * rng.Range(0.55, 0.88) * sz;
            double H = centre ? h * 0.95 * sz : h * rng.Range(0.45, 0.85) * sz;
            const int nn = 7;
            var pts = new Vec3[nn];
            for (int k = 0; k < nn; k++)
            {
                double t = k / (double)(nn - 1);
                pts[k] = o * (R * t) + new Vec3(0, 0.005 + H * Math.Sin(t * Math.PI * 0.55), 0);
            }
            var rad = new double[nn];
            for (int k = 0; k < nn; k++) rad[k] = 0.0030 * (1.0 - 0.80 * k / (double)(nn - 1)) * (centre ? 1.2 : 1.0);
            Primitives.Tube(part, pts, rad, 6, (st, u) => (Primitives.Mix(stemB, stemT, st / (double)(nn - 1)), 1.0, st / (double)(nn - 1), u, 0, 0));
            for (int k = 1; k < nn; k++)
            {
                double t = k / (double)(nn - 1);
                var tg = (pts[Math.Min(nn - 1, k + 1)] - pts[k - 1]).Normalized();
                var side = tg.Cross(Vec3.Up);
                if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
                side = side.Normalized();
                double bl = (centre ? 0.024 : 0.019) * (1.0 - 0.35 * t) * sz;
                double br = bl * 0.24;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var ld = (side * sgn * 0.85 + tg * 0.45 + new Vec3(0, 0.30, 0)).Normalized();
                    int beadV0 = part.VertexCount, beadI0 = part.Indices.Count;
                    B9Spindle(part, pts[k] + ld * 0.001, ld, bl, br, beadB, beadT, 4, 7, 0, 1.1);
                    part.RecordLeaf(beadV0, beadI0, pts[k], bl);
                }
            }
            if (!centre && s % 2 == 0)
                B8Flower(part, pts[nn - 1] + new Vec3(0, 0.006, 0), Vec3.Up, 4, 0.008 * sz, rng.Range(0, Math.Tau), flowerC, Primitives.Scale(c2, 0.94), gold, 0.0022);
        }
        NewFloraAppendWorld(m, part, r, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Rain jelly: ruffled gelatinous lobes seated directly in litter, no plate beneath
    // ---------------------------------------------------------------------------------------------
    private static void RainJelly9(MeshData m, Rng rng, double[] c1, double[] c2, double radius, double height)
    {
        var part = B7Part(m);
        double r = radius > 0.01 ? radius : 0.24;
        double h = height > 0.01 ? height : 0.095;
        var deep = Primitives.Scale(c1, 0.40);
        var body = Primitives.Scale(c1, 0.58);
        var crest = new[] { 0.98, 0.86, 1.0 };
        var drop = Primitives.Mix(c2, new[] { 0.95, 0.95, 1.0 }, 0.6);

        int lobes = 6 + rng.NextInt(3);
        for (int k = 0; k < lobes; k++)
        {
            double a = k * (Math.Tau / lobes) + rng.Range(-0.25, 0.25);
            double dist = k == 0 ? 0.0 : (0.55 + 0.45 * rng.NextDouble()) * r * 0.5;
            var centre = new Vec3(Math.Cos(a) * dist, 0, Math.Sin(a) * dist);
            double R = rng.Range(0.050, 0.085) * (k == 0 ? 1.15 : 1.0);
            double H = rng.Range(0.034, 0.060) * (k == 0 ? 1.15 : 1.0);
            double ph = rng.Range(0, Math.Tau), ph2 = rng.Range(0, Math.Tau);

            Vec3 P(double rho, double th)
            {
                double fold = 1.0 + 0.12 * Math.Cos(3.0 * th + ph) * rho;
                double ruffle = 1.0 + 0.07 * Math.Sin(7.0 * th + ph2) * rho;
                double foot = 1.0 + 0.10 * MathD.SmoothStep(0.80, 1.0, rho);
                double rad = R * rho * fold * ruffle * foot;
                double dome = Math.Pow(Math.Max(0.0, 1.0 - rho * rho * rho), 0.60);
                double y = H * dome * (1.0 + 0.08 * Math.Sin(3.0 * th + ph)) + 0.0045 * Math.Sin(9.0 * th + ph2) * MathD.SmoothStep(0.55, 0.95, rho) * dome
                    - 0.004 * MathD.SmoothStep(0.85, 1.0, rho);
                return centre + new Vec3(Math.Cos(th) * rad, y, Math.Sin(th) * rad);
            }

            const int rings = 8, sides = 18;
            int st = part.VertexCount;
            for (int i = 0; i <= rings; i++)
            {
                double rho = i / (double)rings;
                for (int s = 0; s <= sides; s++)
                {
                    double th = (s % sides) * Math.Tau / sides;
                    var p = P(rho, th);
                    double rn = Math.Clamp(rho, 0.10, 0.97);
                    var d1 = P(rn + 0.03, th) - P(rn - 0.03, th);
                    var d2 = P(rn, th + 0.04) - P(rn, th - 0.04);
                    var nrm = d2.Cross(d1);
                    if (nrm.LengthSq < 1e-18) nrm = Vec3.Up;
                    nrm = nrm.Normalized();
                    if (nrm.Y < 0) nrm = -nrm;
                    double foldF = Math.Max(0.0, Math.Cos(3.0 * th + ph));
                    double star = Math.Pow(0.5 + 0.5 * Math.Cos(5.0 * th + ph2), 0.8) * (1.0 - MathD.SmoothStep(0.12, 0.62, rho));
                    var col = Primitives.Mix(body, deep, MathD.SmoothStep(0.3, 1.0, rho) * 0.6 + (1.0 - foldF) * 0.15);
                    col = Primitives.Mix(col, crest, Math.Clamp(star * 1.6, 0, 1));
                    part.AddVertex(p, nrm, col, 0.88, rho, s / (double)sides, 0, 0);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int s = 0; s < sides; s++)
                {
                    int ia = st + i * (sides + 1) + s, ib = ia + sides + 1;
                    Primitives.TriangleFacing(part, ia, ib, ia + 1, Vec3.Up + part.NormalAt(ia));
                    Primitives.TriangleFacing(part, ia + 1, ib, ib + 1, Vec3.Up + part.NormalAt(ia));
                }
            int drops = 1 + rng.NextInt(2);
            for (int d = 0; d < drops; d++)
            {
                var dp = P(rng.Range(0.15, 0.55), rng.Range(0, Math.Tau));
                double dr = rng.Range(0.0035, 0.0055);
                Primitives.Ellipsoid(part, dp + new Vec3(0, dr * 0.5, 0), new Vec3(dr, dr * 0.8, dr), 3, 6, (u, v) => (drop, 0.92, u, v, 0, 0));
            }
        }
        NewFloraAppendWorld(m, part, r, h);
    }
}
