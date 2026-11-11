using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// SP2 worst-singles rewrites: Starmoss, Siltstars, Embercup and the Emberfan bracket fungus.
/// Starmoss, Siltstars and Embercup are authored in metres at full size and normalised through
/// <see cref="NewFloraAppendWorld"/>; Emberfan keeps the unit frame (X outward from the wood, Y up, unit 1 ~ r/h).
/// Everything is seated: skirts sink below Y=0 so slopes never expose a floating rim.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Shared low cushion surface (starmoss, embercup)
    // ---------------------------------------------------------------------------------------------
    private sealed class WsCushion
    {
        public double R, H, Floor = -0.004, P1, P2, Lump = 0.3, Power = 0.6;
        public ulong Seed;
        public double Edge(double th) => R * (1 + 0.10 * Math.Sin(3 * th + P1) + 0.06 * Math.Cos(5 * th + P2) + 0.03 * Math.Sin(8 * th + P1 * 2));
        public double Y(double x, double z)
        {
            double th = Math.Atan2(z, x);
            double f = Math.Min(1.0, Math.Sqrt(x * x + z * z) / Edge(th));
            double dome = Math.Pow(Math.Max(0.0, 1 - f * f), Power);
            double lump = 1 + Lump * (Noise.Value3(Seed, x * 38, 0.5, z * 38) - 0.5);
            return Floor + (H - Floor) * dome * lump;
        }
        public Vec3 N(double x, double z)
        {
            const double e = 0.004;
            double dx = (Y(x + e, z) - Y(x - e, z)) / (2 * e), dz = (Y(x, z + e) - Y(x, z - e)) / (2 * e);
            return new Vec3(-dx, 1, -dz).Normalized();
        }
        public Vec3 Point(double th, double f)
        {
            double rad = Edge(th) * f;
            double x = Math.Cos(th) * rad, z = Math.Sin(th) * rad;
            return new Vec3(x, Y(x, z), z);
        }
    }

    private static void WsDomeMesh(MeshData m, WsCushion c, int rings, int sides, double[] centreCol, double[] rimCol, double skirtDepth)
    {
        int start = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double f = r / (double)rings;
            for (int s = 0; s <= sides; s++)
            {
                double th = (s % sides) * Math.Tau / sides;
                var p = c.Point(th, f);
                m.AddVertex(p, c.N(p.X, p.Z), Primitives.Mix(centreCol, rimCol, f * f), 1.0, f, s / (double)sides, 0, 0);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + r * (sides + 1) + s, b = a + sides + 1;
                Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, Vec3.Up);
            }
        // Skirt: a vertical collar below the rim so the cushion is seated on any slope.
        int skirt = m.VertexCount;
        var dark = Primitives.Scale(rimCol, 0.6);
        for (int s = 0; s <= sides; s++)
        {
            double th = (s % sides) * Math.Tau / sides;
            var rim = c.Point(th, 1.0);
            var outN = new Vec3(Math.Cos(th), 0.1, Math.Sin(th)).Normalized();
            m.AddVertex(rim, outN, rimCol, 1.0, 1, s / (double)sides, 0, 0);
            m.AddVertex(new Vec3(rim.X * 0.97, c.Floor - skirtDepth, rim.Z * 0.97), outN, dark, 1.0, 1, s / (double)sides, 0, 0);
        }
        for (int s = 0; s < sides; s++)
        {
            int a = skirt + s * 2, b = a + 1, a2 = a + 2, b2 = a + 3;
            var o = new Vec3(Math.Cos((s + 0.5) * Math.Tau / sides), 0, Math.Sin((s + 0.5) * Math.Tau / sides));
            Primitives.TriangleFacing(m, a, b, a2, o);
            Primitives.TriangleFacing(m, a2, b, b2, o);
        }
    }

    /// <summary>Tapered urn/capsule of any orientation built as a profiled tube. glow>0 marks lit tissue (UV2.x).</summary>
    private static void WsUrn(MeshData m, Vec3 basePoint, Vec3 axis, double length, double radius, double[] baseCol, double[] tipCol, double glow)
    {
        var a = axis.Normalized();
        double[] prof = { 0.40, 0.85, 1.0, 0.85, 0.30 };
        var path = new Vec3[5];
        var radii = new double[5];
        for (int i = 0; i < 5; i++) { path[i] = basePoint + a * (length * i / 4.0); radii[i] = radius * prof[i]; }
        Primitives.Tube(m, path, radii, 6, (i, v) => (Primitives.Mix(baseCol, tipCol, i / 4.0), 1.0, i / 4.0, v, glow, glow > 0 ? 1.0 : 0.0));
    }

    private static void WsHood(MeshData m, Vec3 basePoint, Vec3 axis, double length, double radius, double[] col, double[] tipCol)
    {
        var a = axis.Normalized();
        Primitives.Tube(m, new[] { basePoint, basePoint + a * length }, new[] { radius, radius * 0.06 }, 6,
            (i, v) => (Primitives.Mix(col, tipCol, i), 1.0, i, v, 0, 0));
    }

    private static (Vec3 T1, Vec3 T2) WsFrame(Vec3 n)
    {
        var refv = Math.Abs(n.Y) > 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var t1 = n.Cross(refv).Normalized();
        return (t1, n.Cross(t1).Normalized());
    }

    // ---------------------------------------------------------------------------------------------
    // Starmoss: emerald cushion crowded with star-shaped shoot rosettes, lit hearts, nodding capsules
    // ---------------------------------------------------------------------------------------------
    private static void StarmossCushion(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        var cush = new WsCushion { R = radius * 0.96, H = height * 0.62, Floor = -0.004, Lump = 0.5, Power = 0.5, Seed = Rng.Mix(seed, 0x57A9), P1 = rng.Range(0, 6.28), P2 = rng.Range(0, 6.28) };
        var deep = Primitives.Scale(c1, 0.95);
        var rimCol = Primitives.Scale(c1, 0.60);
        WsDomeMesh(part, cush, 6, 26, deep, rimCol, 0.016);

        var lens = new[] { 0.72, 1.0, 0.45 };
        int stars = NewFloraCount(part, 58);
        for (int i = 0; i < stars; i++)
        {
            double th = rng.Range(0, Math.Tau);
            double f = Math.Sqrt(rng.NextDouble()) * 0.93;
            var p = cush.Point(th, f);
            var n = cush.N(p.X, p.Z);
            p += n * 0.0015;
            var (t1, t2) = WsFrame(n);
            double sr = rng.Range(0.0115, 0.0185) * (1.0 - 0.35 * f);
            int rays = 6 + rng.NextInt(2);
            double ph = rng.Range(0, Math.Tau);
            double mixT = rng.Range(0.20, 0.50);
            var baseCol = Primitives.Scale(Primitives.Mix(c1, c2, mixT), 1.25);
            var tipCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.90), 1.15);
            int cV = part.AddVertex(p + n * 0.003, n, baseCol, 1.0, 0, 0, 0.5, 1.0);
            for (int r = 0; r < rays; r++)
            {
                double ang = ph + r * Math.Tau / rays;
                var d = t1 * Math.Cos(ang) + t2 * Math.Sin(ang);
                var side = n.Cross(d).Normalized();
                double len = sr * rng.Range(0.9, 1.1), w = sr * 0.24;
                var mid = p + d * (len * 0.42) + n * (len * 0.06);
                int iT = part.AddVertex(p + d * len + n * (len * 0.30), (d * 0.4 + n).Normalized(), tipCol, 1.0, 1, 0.5, 0.3, 1.0);
                int iL = part.AddVertex(mid + side * w + n * (len * 0.10), n, baseCol, 1.0, 0.5, 0, 0.2, 1.0);
                int iR = part.AddVertex(mid - side * w + n * (len * 0.10), n, baseCol, 1.0, 0.5, 1, 0.2, 1.0);
                Primitives.TriangleFacing(part, cV, iL, iT, n);
                Primitives.TriangleFacing(part, cV, iT, iR, n);
            }
            Primitives.Ellipsoid(part, p + n * 0.004, new Vec3(sr * 0.2, sr * 0.14, sr * 0.2), 3, 5, (u, v) => (lens, 1.0, u, v, 1.1, 1.0));
        }

        var seta = new[] { 0.52, 0.30, 0.14 };
        var capBase = new[] { 0.78, 0.46, 0.14 };
        var capTip = new[] { 0.92, 0.66, 0.22 };
        int setae = NewFloraCount(part, 9);
        for (int i = 0; i < setae; i++)
        {
            double th = rng.Range(0, Math.Tau);
            var p = cush.Point(th, Math.Sqrt(rng.NextDouble()) * 0.80);
            double up = rng.Range(0.030, Math.Max(0.034, height * 0.95 - p.Y));
            var lean = new Vec3(Math.Cos(th), 0, Math.Sin(th)) * rng.Range(0.004, 0.014);
            var path = new[] { p, p + Vec3.Up * (up * 0.5) + lean * 0.4, p + Vec3.Up * up + lean };
            Primitives.Tube(part, path, new[] { 0.0013, 0.0010, 0.0007 }, 6, (s, v) => (seta, 1.0, s / 2.0, v, 0, 0));
            var axis = (new Vec3(Math.Cos(th), 0, Math.Sin(th)) * 0.7 + Vec3.Up * 0.25).Normalized();
            WsUrn(part, path[2], axis, 0.011, 0.0036, capBase, capTip, 0.5);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Siltstars: creeping stolons joining luminous star rosettes on the pond floor
    // ---------------------------------------------------------------------------------------------
    private static void SiltstarsRunners(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        int arms = detail == 2 ? 4 : 5;
        var fresh = Primitives.Scale(Primitives.Mix(c1, c2, 0.40), 1.35);
        var edge = Primitives.Scale(Primitives.Mix(c2, new[] { 0.60, 0.95, 0.90 }, 0.45), 1.05);
        var runnerCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.30), 1.5);
        var rootCol = new[] { 0.16, 0.30, 0.26 };
        var hubCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.25), 1.2);
        var lumen = Primitives.Mix(c2, new[] { 0.40, 0.95, 1.0 }, 0.65);

        part.FloraDetailLevel = 2; // blade grid 8x4 per blade
        var nodes = new List<(Vec3 P, double S)> { (new Vec3(0, 0.006, 0), 1.0) };
        double reach = radius * 0.72;
        for (int a = 0; a < arms; a++)
        {
            double ang = Math.Tau * a / arms + rng.Range(-0.18, 0.18);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var perp = new Vec3(-dir.Z, 0, dir.X);
            double dEnd = reach * rng.Range(0.85, 1.1);
            double bend = rng.Range(-0.025, 0.025);
            var path = new Vec3[5];
            for (int i = 0; i < 5; i++)
            {
                double t = i / 4.0;
                path[i] = dir * (dEnd * t) + perp * (bend * Math.Sin(Math.PI * t)) + Vec3.Up * (0.006 + 0.007 * Math.Sin(Math.PI * t));
            }
            Primitives.Tube(part, path, new[] { 0.0028, 0.0024, 0.0020, 0.0018, 0.0016 }, 6,
                (i, v) => (runnerCol, 1.0, i / 4.0, v, 0, 0));
            nodes.Add((path[4], 0.72));
            
            // Lit bead on each stolon.
            Primitives.Ellipsoid(part, path[1] + Vec3.Up * 0.004, new Vec3(0.0042, 0.0034, 0.0042), 3, 5, (u, v) => (lumen, 1.0, u, v, 1.65, 1.0));
        }

        for (int n = 0; n < nodes.Count; n++)
        {
            var (c, s) = nodes[n];
            bool mother = n == 0;
            for (int r = 0; r < 0; r++)
            {
                double ra = r * Math.Tau / 4 + rng.Range(0, 1);
                Primitives.Tube(part, new[] { c, new Vec3(c.X + Math.Cos(ra) * 0.004 * s, -0.004, c.Z + Math.Sin(ra) * 0.004 * s) },
                    new[] { 0.0016 * s, 0.0007 }, 6, (st, u) => (rootCol, 1.0, st, u, 0, 0));
            }
            Primitives.Ellipsoid(part, c + Vec3.Up * 0.003, new Vec3(0.011 * s, 0.006 * s, 0.011 * s), 3, 6, (u, v) => (hubCol, 1.0, u, v, 0, 0));

            int outer = detail == 2 ? 4 : 5;
            double ph = rng.Range(0, Math.Tau);
            for (int i = 0; i < outer; i++)
            {
                double ang = ph + Math.Tau * i / outer + rng.Range(-0.08, 0.08);
                double len = rng.Range(0.040, 0.052) * s;
                var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                var root = c + Vec3.Up * 0.005;
                var tip = root + dir * (len * 0.92) + Vec3.Up * (len * rng.Range(0.30, 0.42));
                FoliageBlade.Build(part, root, tip, new Vec3(-dir.Z, 0.1, dir.X), rng.Range(0.0075, 0.0095) * s, fresh, edge,
                    camber: 0.18, curl: 0.10, asymmetry: rng.Range(-0.05, 0.05), shoulder: 0.78, segments: 4);
            }
            if (mother)
            {
                int inner = mother ? 4 : 3;
                double ph2 = ph + Math.PI / outer;
                for (int i = 0; i < inner; i++)
                {
                    double ang = ph2 + Math.Tau * i / inner;
                    double len = 0.030 * s;
                    var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                    var root = c + Vec3.Up * 0.007;
                    var tip = root + dir * (len * 0.55) + Vec3.Up * (len * 0.95);
                    FoliageBlade.Build(part, root, tip, new Vec3(-dir.Z, 0.1, dir.X), 0.0065 * s, fresh, edge,
                        camber: 0.15, curl: 0.12, shoulder: 0.78, segments: 4);
                }
            }
            Primitives.Ellipsoid(part, c + Vec3.Up * 0.012 * s, new Vec3(0.0075 * s, 0.0055 * s, 0.0075 * s), 3, 6, (u, v) => (lumen, 1.0, u, v, 1.65, 1.0));
        }

        // The renderer divides the plant height by 0.08 for siltstars (low-carpet frame).
        NewFloraAppendWorld(m, part, radius, height / 0.08);
    }

    // ---------------------------------------------------------------------------------------------
    // Embercup: olive tufted cushion with long russet setae carrying lit ember capsules and open cups
    // ---------------------------------------------------------------------------------------------
    private static void EmbercupGlowCups(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        var cush = new WsCushion { R = radius * 0.88, H = height * 0.36, Floor = -0.006, Lump = 0.35, Power = 0.55, Seed = Rng.Mix(seed, 0xE3B1), P1 = rng.Range(0, 6.28), P2 = rng.Range(0, 6.28) };
        var olive = Primitives.Scale(Primitives.Mix(c1, new[] { 0.30, 0.42, 0.18 }, 0.5), 1.15);
        var olivePale = Primitives.Scale(Primitives.Mix(c1, new[] { 0.62, 0.70, 0.30 }, 0.6), 1.1);
        var gold = new[] { 0.92, 0.70, 0.20 };
        var ember = new[] { 1.0, 0.46, 0.05 };
        var emberDeep = new[] { 0.85, 0.24, 0.03 };
        var emberHot = new[] { 1.0, 0.70, 0.18 };
        var seta = new[] { 0.40, 0.17, 0.07 };
        var setaTip = new[] { 0.58, 0.28, 0.10 };
        var hood = new[] { 0.80, 0.56, 0.16 };

        WsDomeMesh(part, cush, 6, 24, Primitives.Scale(olive, 0.55), Primitives.Scale(olive, 0.45), 0.02);

        // Tufted shoots: three-sided spikes standing off the dome, a few gold-tipped.
        int spikes = NewFloraCount(part, 260);
        for (int i = 0; i < spikes; i++)
        {
            double th = rng.Range(0, Math.Tau);
            double f = Math.Sqrt(rng.NextDouble()) * 0.97;
            var p = cush.Point(th, f) - new Vec3(0, 0.002, 0);
            var n = cush.N(p.X, p.Z);
            var (t1, t2) = WsFrame(n);
            double la = rng.Range(0, Math.Tau);
            var outward = new Vec3(Math.Cos(th), 0, Math.Sin(th));
            var axis = (n + (t1 * Math.Cos(la) + t2 * Math.Sin(la)) * 0.35 + outward * (0.25 * f)).Normalized();
            double len = rng.Range(0.013, 0.022) * (1.0 - 0.3 * f);
            double w = rng.Range(0.0022, 0.0034);
            bool flecked = rng.NextDouble() < 0.14;
            var cb = Primitives.Mix(olive, olivePale, rng.Range(0.0, 0.5));
            var ct = flecked ? gold : Primitives.Mix(olivePale, new[] { 0.80, 0.84, 0.50 }, rng.Range(0.1, 0.5));
            var a0 = part.AddVertex(p + t1 * w, n, cb, 1.0, 0, 0, 0, 0);
            var a1 = part.AddVertex(p - t1 * (w * 0.5) + t2 * w, n, cb, 1.0, 0, 0, 0, 0);
            var a2 = part.AddVertex(p - t1 * (w * 0.5) - t2 * w, n, cb, 1.0, 0, 0, 0, 0);
            var tp = part.AddVertex(p + axis * len, axis, ct, 1.0, 1, 0, 0, 0);
            Primitives.TriangleFacing(part, a0, a1, tp, t1 * 0.25 + t2 * 0.5);
            Primitives.TriangleFacing(part, a1, a2, tp, -t1);
            Primitives.TriangleFacing(part, a2, a0, tp, t1 * 0.25 - t2 * 0.5);
        }

        // Setae with ember capsules; a few have shed their hood and show a glowing cup.
        int setae = NewFloraCount(part, 11);
        for (int i = 0; i < setae; i++)
        {
            double th = rng.Range(0, Math.Tau);
            var p = cush.Point(th, Math.Sqrt(rng.NextDouble()) * 0.78) - new Vec3(0, 0.004, 0);
            double capLen = 0.030, hoodLen = 0.013;
            double up = rng.Range(0.075, 0.100);
            up = Math.Min(up, height * 0.93 - capLen * 0.5 - p.Y);
            double la = rng.Range(0, Math.Tau);
            var lean = new Vec3(Math.Cos(la), 0, Math.Sin(la)) * rng.Range(0.012, 0.034);
            var path = new Vec3[4];
            for (int j = 0; j < 4; j++)
            {
                double t = j / 3.0;
                path[j] = p + Vec3.Up * (up * t) + lean * (t * t);
            }
            Primitives.Tube(part, path, new[] { 0.0016, 0.0013, 0.0010, 0.0008 }, 6, (s, v) => (Primitives.Mix(seta, setaTip, s / 3.0), 1.0, s / 3.0, v, 0, 0));
            var nod = (new Vec3(Math.Cos(la), 0, Math.Sin(la)) * 0.85 + Vec3.Up * 0.45).Normalized();
            bool open = i % 4 == 3;
            if (!open)
            {
                WsUrn(part, path[3], nod, capLen, 0.0072, emberDeep, ember, 1.35);
                WsHood(part, path[3] + nod * capLen * 0.92, nod, hoodLen, 0.0058, hood, gold);
            }
            else
            {
                double cl = 0.022, cr = 0.0095;
                WsUrn(part, path[3], nod, cl, cr, emberDeep, emberHot, 1.35);
                // Glowing mouth disc just inside the rim.
                var rimC = path[3] + nod * (cl * 0.97);
                var (u1, u2) = WsFrame(nod);
                int c0 = part.AddVertex(rimC - nod * 0.002, nod, emberHot, 1.0, 0, 0, 1.35, 1.0);
                const int mouthN = 8;
                int first = part.VertexCount;
                for (int j = 0; j < mouthN; j++)
                {
                    double a = j * Math.Tau / mouthN;
                    part.AddVertex(rimC + (u1 * Math.Cos(a) + u2 * Math.Sin(a)) * (cr * 0.50), nod, emberDeep, 1.0, 1, j / (double)mouthN, 1.35, 1.0);
                }
                for (int j = 0; j < mouthN; j++)
                    Primitives.TriangleFacing(part, c0, first + j, first + (j + 1) % mouthN, nod);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Emberfan: rust-and-cream bracket shelves growing from a rotten wood stub, gills underneath
    // ---------------------------------------------------------------------------------------------
    private static void EmberfanShelves(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var umber = new[] { 0.30, 0.15, 0.07 };
        var terra = Primitives.Mix(c1, new[] { 0.88, 0.28, 0.08 }, 0.5);
        var deepRust = Primitives.Scale(c1, 0.72);
        double[][] zones = { umber, deepRust, Primitives.Scale(terra, 0.85), Primitives.Scale(c1, 0.9), Primitives.Scale(terra, 0.8), Primitives.Mix(c1, c2, 0.45), c2 };
        var gillA = Primitives.Mix(c2, new[] { 0.94, 0.88, 0.76 }, 0.5);
        var gillB = Primitives.Scale(gillA, 0.72);
        var barkDark = new[] { 0.19, 0.12, 0.075 };
        var barkLight = new[] { 0.36, 0.25, 0.15 };
        var heart = new[] { 0.62, 0.50, 0.34 };

        const double cx = -0.30;
        double ph1 = rng.Range(0, 6.28), ph2 = rng.Range(0, 6.28), ph3 = rng.Range(0, 6.28);
        Func<double, double, double> stumpR = (y, th) =>
            (0.36 + 0.12 * Math.Max(0.0, (0.15 - y) / 0.3)) * (1 - 0.10 * Math.Clamp(y, 0, 1))
            * (1 + 0.07 * Math.Sin(5 * th + ph1) + 0.04 * Math.Sin(9 * th + ph2));
        Func<double, double> topY = th => 0.98 + 0.10 * Math.Sin(3 * th + ph3) + 0.05 * Math.Sin(7 * th + ph1);

        // 1. Wood stub, sunk below the ground line.
        {
            int sides = detail == 2 ? 10 : 14, rows = 7;
            int start = m.VertexCount;
            for (int k = 0; k <= rows; k++)
            {
                double t = k / (double)rows;
                for (int s = 0; s <= sides; s++)
                {
                    double th = (s % sides) * Math.Tau / sides;
                    double y = -0.15 + t * (topY(th) + 0.15);
                    double rr = stumpR(y, th);
                    var p = new Vec3(cx + Math.Cos(th) * rr, y, rr * Math.Sin(th));
                    double ridge = 0.5 + 0.5 * Math.Sin(7 * th + ph2 + 2 * t);
                    var col = Primitives.Mix(barkDark, barkLight, ridge * 0.7);
                    if (k >= rows - 1) col = Primitives.Mix(col, heart, k == rows ? 0.85 : 0.4);
                    var n = new Vec3(Math.Cos(th), 0.12, Math.Sin(th)).Normalized();
                    m.AddVertex(p, n, col, 1.0, t, s / (double)sides, 0, 0);
                }
            }
            for (int k = 0; k < rows; k++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + k * (sides + 1) + s, b = a + sides + 1;
                    var o = new Vec3(Math.Cos((s + 0.5) * Math.Tau / sides), 0, Math.Sin((s + 0.5) * Math.Tau / sides));
                    Primitives.TriangleFacing(m, a, b, a + 1, o);
                    Primitives.TriangleFacing(m, a + 1, b, b + 1, o);
                }
            // Concave rotten top.
            int topRing = start + rows * (sides + 1);
            int cTop = m.AddVertex(new Vec3(cx, 0.86, 0), Vec3.Up, Primitives.Scale(heart, 0.8), 1.0, 0, 0, 0, 0);
            for (int s = 0; s < sides; s++)
                Primitives.TriangleFacing(m, cTop, topRing + s, topRing + s + 1, Vec3.Up);
        }

        // 2. Shelves.
        int tiers = detail == 2 ? 4 : 5;
        int rings = detail == 2 ? 6 : 9, around = detail == 2 ? 16 : 22;
        const double beta = 0.78;
        for (int k = 0; k < tiers; k++)
        {
            double y0 = 0.16 + k * (0.66 / Math.Max(1, tiers - 1)) + rng.Range(-0.025, 0.025);
            double frac = (k * 0.618 + 0.12 + rng.Range(0, 0.08)) % 1.0;
            double phi = -1.30 + 2.60 * frac;
            double reach = (0.95 - 0.10 * k) * rng.Range(0.88, 1.10);
            double phaseC = rng.Range(0, Math.Tau);
            double tone = rng.Range(0.92, 1.08);
            double baseThick = rng.Range(0.050, 0.065);

            int topStart = m.VertexCount;
            for (int i = 0; i <= rings; i++)
            {
                double u = i / (double)rings;
                for (int j = 0; j <= around; j++)
                {
                    double s = -Math.Cos(Math.PI * j / around);
                    double q = Math.Sqrt(Math.Max(0.0, 1 - s * s));
                    double cren = 1.0 + 0.09 * Math.Sin(9 * s + phaseC) * u + 0.05 * Math.Sin(15 * s - phaseC) * u;
                    double D = reach * u * Math.Pow(q, 0.85) * cren;
                    double az = phi + beta * s * (1 + 0.35 * u);
                    double rho = stumpR(y0, az) - 0.05 + D;
                    double yy = y0 + 0.035 * u * (1 - u) - 0.045 * u * u + 0.012 * Math.Sin(11 * s + phaseC) * u * u;
                    var p = new Vec3(cx + rho * Math.Cos(az), yy, rho * Math.Sin(az));
                    double zt = Math.Pow(u, 1.6) * (zones.Length - 1);
                    int z0 = Math.Min(zones.Length - 2, (int)zt);
                    var col = Primitives.Scale(Primitives.Mix(zones[z0], zones[z0 + 1], zt - z0), tone);
                    if (u > 0.95) col = c2;
                    var n = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (0.30 * u) + Vec3.Up).Normalized();
                    m.AddVertex(p, n, col, 1.0, u, (j / (double)around), 0, 0);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = topStart + i * (around + 1) + j, b = a + around + 1;
                    Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, b, b + 1, Vec3.Up);
                }

            int ur = detail == 2 ? 3 : 4;
            int underStart = m.VertexCount;
            for (int i = 0; i <= ur; i++)
            {
                double u = i / (double)ur;
                double thk = MathD.Lerp(baseThick, 0.014, Math.Pow(u, 0.7));
                for (int j = 0; j <= around; j++)
                {
                    double s = -Math.Cos(Math.PI * j / around);
                    double q = Math.Sqrt(Math.Max(0.0, 1 - s * s));
                    double cren = 1.0 + 0.09 * Math.Sin(9 * s + phaseC) * u + 0.05 * Math.Sin(15 * s - phaseC) * u;
                    double D = reach * u * Math.Pow(q, 0.85) * cren;
                    double az = phi + beta * s * (1 + 0.35 * u);
                    double rho = stumpR(y0, az) - 0.05 + D;
                    double yy = y0 + 0.035 * u * (1 - u) - 0.045 * u * u + 0.012 * Math.Sin(11 * s + phaseC) * u * u;
                    bool ridge = j % 2 == 0;
                    var p = new Vec3(cx + rho * Math.Cos(az), yy - thk - (ridge ? 0.012 * u : 0), rho * Math.Sin(az));
                    var col = u > 0.93 ? c2 : (ridge ? gillA : gillB);
                    m.AddVertex(p, -Vec3.Up, col, 1.0, u, j / (double)around, 1, 0);
                }
            }
            // Under surface is built from the same angular grid so the shelf is a closed shell.
            // The top ring uses `rings` rows; match the outer top ring to the outer under ring for the rim.
            for (int i = 0; i < ur; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = underStart + i * (around + 1) + j, b = a + around + 1;
                    Primitives.TriangleFacing(m, a, b, a + 1, -Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, b, b + 1, -Vec3.Up);
                }
            int topOuter = topStart + rings * (around + 1);
            int underOuter = underStart + ur * (around + 1);
            for (int j = 0; j < around; j++)
            {
                double s = -Math.Cos(Math.PI * (j + 0.5) / around);
                double az = phi + beta * s * 1.35;
                var o = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                Primitives.TriangleFacing(m, topOuter + j, underOuter + j, topOuter + j + 1, o);
                Primitives.TriangleFacing(m, topOuter + j + 1, underOuter + j, underOuter + j + 1, o);
            }
        }

        // 3. Young button shelves on the stub (including the back, so it reads from every side).
        int buttons = detail == 2 ? 2 : 4;
        for (int b = 0; b < buttons; b++)
        {
            double az = 1.9 + b * (4.3 / buttons) + rng.Range(-0.2, 0.2);
            double y = rng.Range(0.15, 0.80);
            double rr = stumpR(y, az);
            var centre = new Vec3(cx + (rr + 0.01) * Math.Cos(az), y, (rr + 0.01) * Math.Sin(az));
            var col = Primitives.Mix(c1, c2, rng.Range(0.1, 0.5));
            Primitives.Ellipsoid(m, centre, new Vec3(0.11, 0.045, 0.11), 3, 6, (u, v) => (col, 1.0, u, v, 0, 0));
        }
    }
}
