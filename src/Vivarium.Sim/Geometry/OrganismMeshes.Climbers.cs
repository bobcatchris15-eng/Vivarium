using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// SP1 climber node meshes: Clinglace, Shadewrap, Bitterbine, Spiralvine, Fenhook.
/// Node frame (metres, placed unscaled): X across the stem, Y along the stem (growth direction),
/// +Z outward from the host surface. The renderer puts the node origin ~4 mm proud of the host, so
/// the host surface is at about Z = -0.004. Nothing here goes below Z = +0.001 and wide leaves
/// roll down toward the surface at their edges (see <c>wrap</c> in <see cref="ClimbLeaf"/>) so they
/// neither float off a trunk nor cut into it. Runner nodes (attached = false) use +Z as world up.
/// </summary>
public static partial class OrganismMeshes
{
    private delegate void ClimbLeafFn(Vec3 root, Vec3 fwd, Vec3 up, double scale);

    private static Vec3 ClimbUp(Vec3 f, Vec3 hint)
    {
        var u = hint - f * hint.Dot(f);
        if (u.LengthSq < 0.09) u = new Vec3(0, 1, 0) - f * f.Y;
        if (u.LengthSq < 1e-8) u = new Vec3(1, 0, 0);
        return u.Normalized();
    }

    /// <summary>One leaf lamina: lofted grid, outline from <paramref name="prof"/>, tip lift away from the host,
    /// cupped edges, optional trunk-hugging roll-down, optional pale margin colour.</summary>
    private static void ClimbLeaf(MeshData m, Vec3 root, Vec3 fwd, Vec3 up, double len, double halfW,
        Func<double, double> prof, double[] baseCol, double[] tipCol, double tipLift, double cup, bool wrap,
        double[]? edgeCol = null)
    {
        var f = fwd.Normalized();
        var u = ClimbUp(f, up);
        var s = f.Cross(u).Normalized();
        int detail = m.FloraDetailLevel ?? 0;
        int rows = detail == 0 ? 8 : detail == 1 ? 6 : 5;
        int cols = detail == 0 ? 6 : 4;
        if (m.Lod is { } lod) { rows = lod.Segments(rows, 3); cols = Math.Max(2, (int)Math.Round(cols * lod.Blade)); }
        cols = Math.Max(2, cols / 2 * 2);

        Vec3 P(double t, double x)
        {
            double w = halfW * prof(t);
            var p = root + f * (len * t) + s * (x * w) + u * (tipLift * len * t * t + cup * w * x * x);
            if (wrap) p = new Vec3(p.X, p.Y, Math.Max(0.0015, p.Z - 2.2 * p.X * p.X));
            return p;
        }

        int firstV = m.VertexCount, firstI = m.Indices.Count;
        for (int r = 0; r <= rows; r++)
        {
            double t = (double)r / rows;
            var baseToTip = Primitives.Mix(baseCol, tipCol, Math.Pow(t, 0.8));
            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var pos = P(t, x);
                double d = 0.01;
                var a = P(Math.Min(1, t + d), x) - P(Math.Max(0, t - d), x);
                var b = P(t, Math.Min(1, x + d)) - P(t, Math.Max(-1, x - d));
                var n = a.Cross(b).Normalized();
                if (n.LengthSq < 1e-8 || n.Dot(u) < 0) n = u;
                var col = edgeCol == null ? baseToTip : Primitives.Mix(baseToTip, edgeCol, x * x * x * x * 0.7);
                if (_climbBright > 0 && _climbBright != 1.0) col = Primitives.Scale(col, _climbBright);
                m.AddVertex(pos, n, col, 1.0, t, (x + 1.0) * 0.5, 0.5, 1.0);
            }
        }
        int stride = cols + 1;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int a = firstV + r * stride + c, b = a + stride;
            var n0 = m.NormalAt(a);
            Primitives.TriangleFacing(m, a, b, a + 1, n0);
            Primitives.TriangleFacing(m, a + 1, b, b + 1, n0);
        }
        m.RecordLeaf(firstV, firstI, root, len);
    }

    private static double CPLance(double t) => Math.Max(0.03, Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.8))), 0.9));
    private static double CPOvate(double t) => Math.Max(0.03, Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.62))), 0.85));

    private static void ClimbTube(MeshData m, Vec3[] pts, double[] rads, double[] col, int sides = 6) =>
        Primitives.Tube(m, pts, rads, sides, (i, v) => (col, 1.0, i / (double)Math.Max(1, pts.Length - 1), v, 0, 0));

    private static void ClimbPetiole(MeshData m, Vec3 from, Vec3 to, double r0, double r1, double[] col, double bow = 0.0035)
    {
        var mid = Vec3.Lerp(from, to, 0.5) + new Vec3(0, 0, bow);
        ClimbTube(m, new[] { from, mid, to }, new[] { r0, (r0 + r1) * 0.5, r1 }, col, 5);
    }

    private static ClimbLeafFn ClimbLeafForRaw(string id, MeshData m, Rng rng, double[] c1, double[] c2, bool attached)
    {
        bool wrap = attached;
        switch (id)
        {
            case "clinglace":
            {
                // Glossy dark ivy, cream-lime margins, 3 lobes (acute centre, two spreading lateral lobes).
                var baseC = Primitives.Scale(c1, 0.62);
                var tipC = Primitives.Mix(c1, new[] { 0.70, 0.84, 0.42 }, 0.40);
                var edge = new[] { 0.72, 0.82, 0.46 };
                return (root, fwd, up, sc) =>
                {
                    double L = sc * rng.Range(0.062, 0.080);
                    ClimbLeaf(m, root, fwd, up, L, L * 0.30, CPOvate, baseC, tipC, 0.16, 0.12, wrap, edge);
                    foreach (double sg in new[] { -1.0, 1.0 })
                    {
                        var d = Axis.RotateAround(fwd.Normalized(), up, sg * (0.95 + rng.Range(-0.06, 0.06)));
                        ClimbLeaf(m, root, d, up, L * 0.72, L * 0.22, CPOvate, baseC, tipC, 0.16, 0.12, wrap, edge);
                    }
                };
            }
            case "shadewrap":
            {
                // Big 5-lobed leaves, near-black slate green fading to silver-blue tips; a ghostly cloak.
                var baseC = Primitives.Scale(c1, 0.85);
                var tipC = Primitives.Mix(c2, new[] { 0.66, 0.80, 0.78 }, 0.60);
                var edge = new[] { 0.72, 0.84, 0.82 };
                return (root, fwd, up, sc) =>
                {
                    double L = sc * rng.Range(0.080, 0.100);
                    ClimbLeaf(m, root, fwd, up, L, L * 0.26, CPLance, baseC, tipC, 0.10, 0.10, wrap, edge);
                    foreach (double sg in new[] { -1.0, 1.0 })
                    {
                        ClimbLeaf(m, root, Axis.RotateAround(fwd.Normalized(), up, sg * 0.58), up, L * 0.84, L * 0.22, CPLance, baseC, tipC, 0.10, 0.10, wrap, edge);
                        ClimbLeaf(m, root, Axis.RotateAround(fwd.Normalized(), up, sg * 1.18), up, L * 0.60, L * 0.19, CPLance, baseC, tipC, 0.10, 0.10, wrap, edge);
                    }
                };
            }
            case "bitterbine":
            {
                // Coarse hop-like lobed leaves, olive green to hop-yellow tips.
                var baseC = Primitives.Scale(Primitives.Mix(c1, new[] { 0.30, 0.46, 0.14 }, 0.4), 1.05);
                var tipC = Primitives.Mix(c1, new[] { 0.80, 0.80, 0.30 }, 0.50);
                var edge = new[] { 0.62, 0.62, 0.20 };
                return (root, fwd, up, sc) =>
                {
                    double L = sc * rng.Range(0.085, 0.110);
                    ClimbLeaf(m, root, fwd, up, L, L * 0.30, CPOvate, baseC, tipC, 0.34, 0.16, wrap, edge);
                    foreach (double sg in new[] { -1.0, 1.0 })
                        ClimbLeaf(m, root, Axis.RotateAround(fwd.Normalized(), up, sg * (0.88 + rng.Range(-0.05, 0.05))), up,
                            L * 0.70, L * 0.20, CPLance, baseC, tipC, 0.34, 0.16, wrap, edge);
                };
            }
            case "spiralvine":
            {
                // Slender bright-lime heart-lanceolate leaves that flick up at the tip.
                var baseC = Primitives.Mix(c1, new[] { 0.28, 0.62, 0.18 }, 0.5);
                var tipC = Primitives.Mix(c2, new[] { 0.78, 0.92, 0.36 }, 0.5);
                return (root, fwd, up, sc) =>
                {
                    double L = sc * rng.Range(0.090, 0.115);
                    ClimbLeaf(m, root, fwd, up, L, L * 0.22, CPOvate, baseC, tipC, 0.45, 0.18, wrap);
                };
            }
            default: // fenhook
            {
                // Narrow strap leaves in a starry whorl; cool teal with mint tips.
                var baseC = Primitives.Scale(c1, 1.05);
                var tipC = Primitives.Mix(c2, new[] { 0.62, 0.92, 0.78 }, 0.5);
                return (root, fwd, up, sc) =>
                {
                    double L = sc * rng.Range(0.062, 0.078);
                    ClimbLeaf(m, root, fwd, up, L, L * 0.15, CPLance, baseC, tipC, 0.20, 0.08, wrap);
                };
            }
        }
    }

    [ThreadStatic] private static double _climbBright;

    private static ClimbLeafFn ClimbLeafFor(string id, MeshData m, Rng rng, double[] c1, double[] c2, bool attached)
    {
        var raw = ClimbLeafForRaw(id, m, rng, c1, c2, attached);
        double bright = id switch { "shadewrap" => 1.55, "bitterbine" => 1.35, "clinglace" => 1.35, "spiralvine" => 1.25, _ => 1.3 };
        double size = id switch { "shadewrap" => 1.15, "fenhook" => 1.25, _ => 1.3 };
        return (root, fwd, up, sc) => { _climbBright = bright; raw(root, fwd, up, sc * size); _climbBright = 1.0; };
    }

    /// <summary>Climber node for the five SP1 species; null id falls through to false (caller keeps legacy).</summary>
    private static bool TryClimberNodeSp1(MeshData m, Rng rng, string id, double[] c1, double[] c2, bool attached)
    {
        if (id is not ("clinglace" or "shadewrap" or "bitterbine" or "spiralvine" or "fenhook")) return false;
        var leaf = ClimbLeafFor(id, m, rng, c1, c2, attached);
        var stemCol = id switch
        {
            "clinglace" => Primitives.Mix(WoodyBark, c1, 0.35),
            "shadewrap" => Primitives.Mix(c2, new[] { 0.70, 0.78, 0.72 }, 0.5),
            "bitterbine" => Primitives.Mix(c1, new[] { 0.55, 0.50, 0.24 }, 0.5),
            "spiralvine" => Primitives.Mix(c1, new[] { 0.40, 0.70, 0.22 }, 0.45),
            _ => Primitives.Mix(c1, c2, 0.4),
        };
        var petCol = Primitives.Mix(stemCol, c1, 0.4);

        if (!attached)
        {
            // Ground runner: two leaves held up and out from the runner, plus an anchoring rootlet down.
            foreach (double sg in new[] { -1.0, 1.0 })
            {
                var dir = new Vec3(sg * rng.Range(0.45, 0.6), rng.Range(0.1, 0.4), 0.62).Normalized();
                var pe = dir * 0.02;
                ClimbPetiole(m, new Vec3(0, 0, 0), pe, 0.0024, 0.0016, petCol, 0.002);
                leaf(pe, dir, Vec3.Up, 0.7);
            }
            ClimbTube(m, new[] { Vec3.Zero, -Vec3.Up * 0.013 }, new[] { 0.0016, 0.0007 }, Primitives.Scale(stemCol, 0.6));
            return true;
        }

        switch (id)
        {
            case "clinglace":
            {
                var padCol = new[] { 0.46, 0.30, 0.20 };
                // Rootlet pads: short fibres creeping sideways along the bark, each ending in a flat pad.
                int pads = NewFloraCount(m, 6);
                for (int i = 0; i < pads; i++)
                {
                    double y = MathD.Lerp(-0.027, 0.027, (i + rng.Range(0.2, 0.8)) / pads);
                    double sg = i % 2 == 0 ? -1 : 1;
                    var a = new Vec3(0, y, 0.0015);
                    var b = new Vec3(sg * 0.007, y - 0.002, 0.0012);
                    var c = new Vec3(sg * rng.Range(0.012, 0.017), y - rng.Range(0.002, 0.007), 0.0010);
                    ClimbTube(m, new[] { a, b, c }, new[] { 0.0014, 0.0010, 0.0007 }, padCol, 5);
                    Primitives.Ellipsoid(m, c, new Vec3(0.0034, 0.0034, 0.0009), 3, 6, (u, v) => (padCol, 1.0, u, v, 0, 0));
                }
                // Alternate leaves up the stem, shingled over each other, held flat against the host.
                for (int k = 0; k < 3; k++)
                {
                    double sg = k % 2 == 0 ? -1 : 1;
                    double y = -0.022 + k * 0.022;
                    double sc = k == 2 ? 0.55 : 1.0;
                    var from = new Vec3(0, y, 0.002);
                    var dir = new Vec3(sg * rng.Range(0.42, 0.55), rng.Range(0.78, 0.9), 0.20).Normalized();
                    var pe = new Vec3(sg * 0.011, y + 0.012, 0.005 + 0.0015 * k);
                    ClimbPetiole(m, from, pe, 0.0022, 0.0015, petCol, 0.0015);
                    leaf(pe, dir, new Vec3(0, 0, 1), sc);
                }
                break;
            }
            case "shadewrap":
            {
                var threadCol = Primitives.Mix(c2, new[] { 0.78, 0.86, 0.80 }, 0.7);
                // Misty root veil: fine pale threads laid down the bark.
                int th = NewFloraCount(m, 10);
                for (int i = 0; i < th; i++)
                {
                    double y = MathD.Lerp(-0.03, 0.03, (i + rng.Range(0.1, 0.9)) / th);
                    double x0 = rng.Range(-0.004, 0.004), sg = i % 2 == 0 ? -1 : 1;
                    double drop = rng.Range(0.030, 0.055), sway = sg * rng.Range(0.004, 0.012);
                    var a = new Vec3(x0, y, 0.002);
                    var b = new Vec3(x0 + sway * 0.5, y - drop * 0.5, 0.0022);
                    var c = new Vec3(x0 + sway, y - drop, 0.0020);
                    ClimbTube(m, new[] { a, b, c }, new[] { 0.00075, 0.00050, 0.00018 }, threadCol, 4);
                }
                for (int k = 0; k < 2; k++)
                {
                    double sg = k == 0 ? -1 : 1;
                    double y = k == 0 ? -0.018 : 0.020;
                    var from = new Vec3(0, y, 0.002);
                    var dir = new Vec3(sg * rng.Range(0.55, 0.7), rng.Range(0.62, 0.78), 0.12).Normalized();
                    var pe = new Vec3(sg * 0.012, y + 0.008, 0.005 + 0.002 * k);
                    ClimbPetiole(m, from, pe, 0.0020, 0.0014, petCol, 0.001);
                    leaf(pe, dir, new Vec3(0, 0, 1), 1.0);
                }
                break;
            }
            case "bitterbine":
            {
                var bristle = Primitives.Scale(stemCol, 0.75);
                int bs = NewFloraCount(m, 12);
                for (int i = 0; i < bs; i++)
                {
                    var bb = new Vec3(rng.Range(-0.003, 0.003), rng.Range(-0.03, 0.03), 0.003);
                    var bd = new Vec3(rng.Range(-0.7, 0.7), rng.Range(-0.5, 0.2), rng.Range(0.5, 0.9)).Normalized();
                    ClimbTube(m, new[] { bb, bb + bd * rng.Range(0.002, 0.0035) }, new[] { 0.0006, 0.0002 }, bristle, 4);
                }
                // Opposite lobed leaves, held a little off the host on stout petioles.
                double pairY = rng.Range(-0.020, -0.012);
                for (int pr = 0; pr < 2; pr++)
                foreach (double sg in new[] { -1.0, 1.0 })
                {
                    double py = pairY + pr * 0.030;
                    double sgn = pr == 0 ? sg : -sg * 0.8;
                    var from = new Vec3(0, py, 0.003);
                    var dir = new Vec3(sgn * rng.Range(0.75, 0.88), rng.Range(0.30, 0.45), 0.35).Normalized();
                    var pe = new Vec3(sgn * 0.020, py + 0.010, 0.012);
                    ClimbPetiole(m, from, pe, 0.0026, 0.0018, petCol, 0.004);
                    leaf(pe, dir, new Vec3(0, 0, 1), pr == 0 ? 1.0 : 0.8);
                }
                // Papery hop cone hanging from a short peduncle: overlapping straw bracts.
                if (rng.NextDouble() < 0.8)
                {
                    double sg = rng.NextDouble() < 0.5 ? -1 : 1;
                    var axil = new Vec3(0, pairY + 0.006, 0.003);
                    var top = new Vec3(sg * 0.016, pairY + 0.002, 0.024);
                    ClimbPetiole(m, axil, top, 0.0018, 0.0014, petCol, 0.006);
                    var straw = new[] { 0.80, 0.78, 0.40 }; var green = new[] { 0.52, 0.62, 0.22 };
                    int bracts = m.FloraDetailLevel is 2 ? 4 : 6;
                    for (int b = 0; b < bracts; b++)
                    {
                        double t = b / (double)(bracts - 1);
                        double r = 0.0125 * Math.Sin(Math.PI * (0.25 + 0.65 * t)) + 0.002;
                        var cc = top + new Vec3(0, -0.0085 * b - 0.004, -0.002 * b * 0.3);
                        var col = Primitives.Mix(green, straw, 0.35 + 0.55 * t * (b % 2 == 0 ? 1.0 : 0.8));
                        Primitives.Ellipsoid(m, cc, new Vec3(r, 0.0065, r), 3, 6, (u, v) => (col, 1.0, u, v, 0, 0));
                    }
                }
                break;
            }
            case "spiralvine":
            {
                var tendrilCol = new[] { 0.82, 0.54, 0.40 };
                double pairY = rng.Range(-0.012, 0.0);
                foreach (double sg in new[] { -1.0, 1.0 })
                {
                    var from = new Vec3(0, pairY, 0.003);
                    var dir = new Vec3(sg * rng.Range(0.70, 0.82), rng.Range(0.45, 0.58), 0.30).Normalized();
                    var pe = new Vec3(sg * 0.010, pairY + 0.009, 0.007);
                    ClimbPetiole(m, from, pe, 0.0022, 0.0015, petCol, 0.003);
                    leaf(pe, dir, new Vec3(0, 0, 1), 1.0);
                }
                // Corkscrew tendril: a straight lead reaching sideways, then 2.5 coils tapering to a curl.
                {
                    double sg = rng.NextDouble() < 0.5 ? -1 : 1;
                    double y0 = pairY + 0.022;
                    int n = m.FloraDetailLevel is 2 ? 12 : 18;
                    var pts = new Vec3[n]; var rr = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double u = i / (double)(n - 1);
                        double lead = Math.Min(1.0, u / 0.22);
                        double coil = Math.Max(0.0, (u - 0.18) / 0.82);
                        double ph = Math.PI * 2 * 2.6 * coil;
                        double rad = 0.0072 * (1 - 0.45 * coil) * Math.Min(1.0, coil * 6);
                        pts[i] = new Vec3(sg * (0.003 + 0.046 * u), y0 + rad * Math.Sin(ph),
                            0.004 + 0.007 * lead + rad * (1 - Math.Cos(ph)));
                        rr[i] = 0.0017 * (1 - 0.8 * u) + 0.0003;
                    }
                    ClimbTube(m, pts, rr, tendrilCol, 5);
                }
                break;
            }
            default: // fenhook
            {
                var hookCol = new[] { 0.86, 0.88, 0.70 };
                // Half whorls of strap leaves fanning out from the stem, two stations per node.
                for (int w = 0; w < 3; w++)
                {
                    double y = -0.026 + w * 0.026;
                    int n = NewFloraCount(m, 5);
                    for (int i = 0; i < n; i++)
                    {
                        double al = MathD.Lerp(-1.40, 1.40, (i + 0.5 * (w % 2)) / (double)Math.Max(1, n - (w == 0 ? 0 : 0.5)));
                        var dir = new Vec3(Math.Sin(al) * 0.9, 0.35, 0.10 + 0.42 * Math.Cos(al)).Normalized();
                        var root = new Vec3(0, y, 0.003) + dir * 0.004;
                        leaf(root, dir, new Vec3(0, 0, 1), 0.95 + 0.1 * Math.Cos(al));
                    }
                }
                // Reflexed hooked prickles on the stem.
                int hooks = NewFloraCount(m, 6);
                for (int i = 0; i < hooks; i++)
                {
                    double sg = i % 2 == 0 ? -1 : 1;
                    double y = MathD.Lerp(-0.026, 0.026, (i + 0.5) / hooks);
                    var a = new Vec3(sg * 0.002, y, 0.003);
                    var b = new Vec3(sg * 0.009, y + 0.003, 0.0055);
                    var c = new Vec3(sg * 0.014, y + 0.001, 0.0075);
                    var d = new Vec3(sg * 0.016, y - 0.006, 0.0070);
                    ClimbTube(m, new[] { a, b, c, d }, new[] { 0.0013, 0.0010, 0.0006, 0.0002 }, hookCol, 4);
                }
                break;
            }
        }
        return true;
    }

    /// <summary>Growing tip: tapering tendril following the stem direction with unfurling baby leaves close to it.</summary>
    private static bool TryClimberTipSp1(MeshData m, Rng rng, string id, double[] c1, double[] c2)
    {
        if (id is not ("clinglace" or "shadewrap" or "bitterbine" or "spiralvine" or "fenhook")) return false;
        var leaf = ClimbLeafFor(id, m, rng, c1, c2, true);
        var stemCol = id == "shadewrap" ? Primitives.Mix(c2, new[] { 0.70, 0.78, 0.72 }, 0.5)
            : id == "spiralvine" ? new[] { 0.82, 0.54, 0.40 }
            : Primitives.Mix(c1, c2, 0.45);
        int n = 8;
        var pts = new Vec3[n]; var rr = new double[n];
        for (int i = 0; i < n; i++)
        {
            double u = i / (double)(n - 1);
            double curl = id == "spiralvine" ? Math.Pow(u, 2.0) : id == "fenhook" ? Math.Pow(u, 3.0) : u * u * 0.5;
            double ang = curl * (id == "spiralvine" ? 5.5 : id == "fenhook" ? 3.6 : 1.0);
            double rad = id is "spiralvine" or "fenhook" ? 0.008 * (1 - 0.5 * u) : 0.0;
            pts[i] = new Vec3(0, 0.014 + 0.032 * u * (1 - 0.3 * u) + rad * Math.Sin(ang) * u,
                0.004 + 0.004 * u + rad * (1 - Math.Cos(ang)) * u);
            rr[i] = 0.0022 * (1 - 0.88 * u) + 0.0003;
        }
        ClimbTube(m, pts, rr, stemCol, 5);
        foreach (double sg in new[] { -1.0, 1.0 })
        {
            var dir = new Vec3(sg * 0.50, 0.80, 0.22).Normalized();
            var root = new Vec3(sg * 0.005, 0.008, 0.004);
            leaf(root, dir, new Vec3(0, 0, 1), 0.34);
        }
        return true;
    }
}
