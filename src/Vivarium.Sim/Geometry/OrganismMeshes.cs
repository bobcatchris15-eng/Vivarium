using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Species meshes. Flora meshes are built at unit scale (radius 1 in XZ, height 1 in Y) and scaled by the
/// individual's radius/height; fauna meshes are unit body length along +X (head forward), origin at the
/// underside centre.
/// Fauna vertex encoding (read by the fauna shader):
///   COLOR.rgb = offset from the appendage's attachment point (0 for body), COLOR.a = 1 on appendages;
///   UV = (position along body 0..1, around 0..1) for markings; UV2.x = region
///   (0 body, 1 marking-eligible body, 2 eye, 3 belly/light, 4 fin/limb).
/// </summary>
public static class OrganismMeshes
{
    public static MeshData Flora(FloraSpeciesDef sp, ulong seed = 1)
    {
        var rng = Rng.Keyed(seed, "flora.mesh." + sp.Id, 0);
        var m = new MeshData();
        var c1 = sp.Color; var c2 = sp.Color2;
        switch (sp.Shape)
        {
            case "carpet":
            {
                // low mat plus many small tapered fronds: reads as moss up close, as a soft patch from afar
                var matCol = Primitives.Scale(c1, 0.7);
                int matStart = m.VertexCount;
                Primitives.Ellipsoid(m, new Vec3(0, 0.05, 0), new Vec3(1.0, 0.25, 1.0), 6, 28, (a, b) => (matCol, 1, a, b, 0, 0));
                // ragged outline: moss mats creep unevenly, so wobble the rim and tuck its edge into the ground
                ulong rim = Rng.Mix(seed, 0xC4A9);
                for (int i = matStart; i < m.VertexCount; i++)
                {
                    var p = m.Position(i);
                    double ang = Math.Atan2(p.Z, p.X);
                    double wob = 1 + 0.22 * (Noise.Value3(rim, Math.Cos(ang) * 2.2, Math.Sin(ang) * 2.2, 0.5) - 0.3);
                    double edge = Math.Sqrt(p.X * p.X + p.Z * p.Z);
                    m.Positions[i * 3] = (float)(p.X * wob);
                    m.Positions[i * 3 + 2] = (float)(p.Z * wob);
                    m.Positions[i * 3 + 1] = (float)(p.Y - 0.08 * edge * edge);
                }
                m.RecomputeNormals();
                for (int k = 0; k < 70; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.92;
                    var baseP = new Vec3(Math.Cos(ang) * r, 0.1, Math.Sin(ang) * r);
                    double h = rng.Range(0.5, 1.0) * (1.05 - r * 0.35);
                    var lean = new Vec3(rng.Range(-0.12, 0.12), 0, rng.Range(-0.12, 0.12));
                    var tipCol = Primitives.Mix(c1, c2, rng.Range(0.4, 1.0));
                    Primitives.Tube(m, new[] { baseP, baseP + lean * 0.5 + new Vec3(0, h * 0.55, 0), baseP + lean + new Vec3(0, h, 0) },
                        new[] { 0.05, 0.035, 0.008 }, 4, (i, v) => (Primitives.Mix(Primitives.Scale(c1, 0.75), tipCol, i / 2.0), 1, i, v, 0, 0));
                }
                break;
            }
            case "cushion":
            {
                var (v, t) = Primitives.Icosphere(2);
                ulong ns = Rng.Mix(seed, 55);
                for (int i = 0; i < v.Count; i++)
                {
                    var p = v[i];
                    double bump = 1 + 0.12 * Noise.Value3(ns, p.X * 8, p.Y * 8, p.Z * 8);
                    var q = new Vec3(p.X * bump, Math.Max(p.Y, -0.05) * bump, p.Z * bump);
                    var col = Primitives.Mix(c1, c2, MathD.Clamp01(0.5 + 0.8 * p.Y * (bump - 0.95) * 4));
                    m.AddVertex(q, p, col, 1, p.X, p.Z, 0, 0);
                }
                foreach (var i in t) m.Indices.Add(i);
                m.RecomputeNormals();
                break;
            }
            case "crust":
            {
                // flat rosette with lighter concentric margins and darker fruiting dots
                int rings = 5, seg = 28;
                ulong ns = Rng.Mix(seed, 71);
                int centre = m.AddVertex(new Vec3(0, 1, 0), Vec3.Up, Primitives.Scale(c1, 0.8), 1, 0.5, 0.5);
                int prev = -1;
                for (int r = 1; r <= rings; r++)
                {
                    double f = (double)r / rings;
                    int start = m.VertexCount;
                    for (int s = 0; s <= seg; s++)
                    {
                        double th = 2 * Math.PI * s / seg;
                        double rr = f * (0.85 + 0.15 * Noise.Gradient(ns, Math.Cos(th) * 2, Math.Sin(th) * 2));
                        var col = r % 2 == 0 ? c2 : c1;
                        if (r == rings) col = Primitives.Scale(c2, 1.05);
                        m.AddVertex(new Vec3(Math.Cos(th) * rr, 1 - f * 0.6, Math.Sin(th) * rr), Vec3.Up, col, 1, f, th);
                    }
                    for (int s = 0; s < seg; s++)
                    {
                        if (prev < 0) Primitives.TriangleFacing(m, centre, start + s, start + s + 1, Vec3.Up);
                        else { Primitives.TriangleFacing(m, prev + s, start + s, start + s + 1, Vec3.Up); Primitives.TriangleFacing(m, prev + s, start + s + 1, prev + s + 1, Vec3.Up); }
                    }
                    prev = start;
                }
                break;
            }
            case "foliose":
                for (int k = 0; k < 11; k++)
                {
                    double ang = 2 * Math.PI * k / 11 + rng.Range(-0.2, 0.2), len = rng.Range(0.55, 1.0);
                    var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                    var side = new Vec3(-dir.Z, 0, dir.X);
                    var rim = new List<Vec3>();
                    for (int s = 0; s <= 8; s++)
                    {
                        double u = (double)s / 8 * Math.PI;
                        double wave = 0.12 * Math.Sin(u * 5);
                        var p = dir * (Math.Sin(u) * len + 0.1) + side * (Math.Cos(u) * 0.28 * len) + new Vec3(0, 0.4 + 0.6 * Math.Sin(u) + wave, 0);
                        rim.Add(p);
                    }
                    Primitives.Fan(m, dir * 0.08 + new Vec3(0, 0.2, 0), rim, new Vec3(0, 1, 0), Primitives.Scale(c1, 0.85), c2);
                }
                break;
            case "creeper":
                for (int k = 0; k < 14; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.85;
                    var baseP = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    double h = rng.Range(0.4, 1.0), lr = rng.Range(0.14, 0.22);
                    var top = baseP + new Vec3(0, h, 0);
                    Primitives.Tube(m, new[] { baseP, top }, new[] { 0.015, 0.012 }, 4, (i, v) => (Primitives.Scale(c1, 0.7), 1, i, v, 0, 0));
                    var rim = new List<Vec3>();
                    double tilt = rng.Range(0.1, 0.4);
                    for (int s = 0; s <= 10; s++)
                    {
                        double th = 2 * Math.PI * s / 10;
                        rim.Add(top + new Vec3(Math.Cos(th) * lr, Math.Cos(th) * lr * tilt, Math.Sin(th) * lr));
                    }
                    Primitives.Fan(m, top + new Vec3(0, 0.02, 0), rim, Vec3.Up, c2, Primitives.Mix(c1, c2, 0.4));
                }
                break;
            case "reed":
                for (int k = 0; k < 11; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = rng.Range(0, 0.45);
                    var baseP = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    var lean = new Vec3(rng.Range(-0.35, 0.35), 0, rng.Range(-0.35, 0.35));
                    double h = rng.Range(0.6, 1.0), w = rng.Range(0.03, 0.06);
                    var side = new Vec3(Math.Cos(ang + Math.PI / 2), 0, Math.Sin(ang + Math.PI / 2)) * w;
                    var tip = baseP + lean * h + new Vec3(0, h, 0);
                    var mid = baseP + lean * (h * 0.4) + new Vec3(0, h * 0.55, 0);
                    Primitives.Fan(m, baseP, new List<Vec3> { baseP - side, mid - side * 0.8, tip, mid + side * 0.8, baseP + side }, new Vec3(side.Z, 0, -side.X).Normalized(),
                        Primitives.Scale(c1, 0.8), c2);
                }
                break;
            case "herb":
            {
                for (int k = 0; k < 7; k++)
                {
                    double ang = 2 * Math.PI * k / 7 + rng.Range(-0.15, 0.15);
                    var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)); var side = new Vec3(-dir.Z, 0, dir.X);
                    var rim = new List<Vec3> { side * 0.08, dir * 0.45 + side * 0.18 + new Vec3(0, 0.2, 0), dir * 0.9 + new Vec3(0, 0.12, 0), dir * 0.45 - side * 0.18 + new Vec3(0, 0.2, 0), -side * 0.08 };
                    Primitives.Fan(m, new Vec3(0, 0.05, 0), rim, Vec3.Up, Primitives.Scale(c1, 0.8), Primitives.Scale(c1, 1.1));
                }
                for (int f = 0; f < 3; f++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = rng.Range(0.05, 0.3), h = rng.Range(0.7, 1.0);
                    var top = new Vec3(Math.Cos(ang) * r, h, Math.Sin(ang) * r);
                    Primitives.Tube(m, new[] { new Vec3(0, 0, 0), top }, new[] { 0.02, 0.015 }, 4, (i, v) => (c1, 1, i, v, 0, 0));
                    var star = new List<Vec3>();
                    for (int s = 0; s <= 10; s++)
                    {
                        double th = 2 * Math.PI * s / 10, rr = s % 2 == 0 ? 0.22 : 0.09;
                        star.Add(top + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr));
                    }
                    Primitives.Fan(m, top + new Vec3(0, 0.03, 0), star, Vec3.Up, new[] { 1.0, 0.92, 0.45 }, c2);
                }
                break;
            }
            default:
                Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(1, 1, 1), 6, 8, (a, b) => (c1, 1, a, b, 0, 0));
                break;
        }
        return m;
    }

    // ------------------------------------------------------------------ fauna

    private static readonly double[] White = { 1, 1, 1 };

    public static MeshData Fauna(FaunaSpeciesDef sp)
    {
        var m = new MeshData();
        switch (sp.Model)
        {
            case "springtail": Springtail(m); break;
            case "shrimp": Shrimp(m); break;
            case "triops": Triops(m); break;
            case "minnow": Minnow(m); break;
            case "isopod": Isopod(m); break;
            default: Primitives.Ellipsoid(m, new Vec3(0, 0.2, 0), new Vec3(0.5, 0.2, 0.2), 8, 10, (a, b) => (Body, 0, a, b, 1, 0)); break;
        }
        return m;
    }

    /// <summary>Rolled-up pose for conglobating species (pill bug ball); null for species that never curl.</summary>
    public static MeshData? FaunaCurled(FaunaSpeciesDef sp)
    {
        if (sp.Model != "isopod") return null;
        var m = new MeshData();
        const int plates = 11;
        const double cy = 0.22, ring = 0.17;
        Primitives.Ellipsoid(m, new Vec3(0, cy, 0), new Vec3(0.19, 0.19, 0.2), 8, 12, (a, b) => Region(0.5, b, 3));
        for (int k = 0; k < plates; k++)
        {
            double ang = Math.PI / 2 - k * 2 * Math.PI / plates;   // plates run over the top and around the ball
            var c = new Vec3(Math.Cos(ang) * ring, cy + Math.Sin(ang) * ring, 0);
            double t = (double)k / plates;
            Primitives.Ellipsoid(m, c, new Vec3(0.075, 0.05, 0.25 - 0.02 * Math.Abs(Math.Sin(ang))), 5, 12, (a, b) => Region(0.95 - t * 0.9, b, 1), pitch: ang + Math.PI / 2);
        }
        return m;
    }

    private static readonly double[] Body = { 0, 0, 0 };

    private static (double[] Col, double A, double U, double V, double U2, double V2) Region(double u, double v, int region) => (Body, 0, u, v, region, 0);

    /// <summary>Appendage vertex attributes: colour carries the offset from the attachment point.</summary>
    private static void Appendage(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radius, int segs, int region = 4)
    {
        var attach = path[0];
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, segs, (i, v) => (Body, 1, 0.5, v, region, 0));
        for (int i = start; i < m.VertexCount; i++)
        {
            var off = m.Position(i) - attach;
            m.SetColor(i, off.X, off.Y, off.Z, 1);
        }
    }

    private static void AppendageFan(MeshData m, Vec3 attach, Vec3 centre, IReadOnlyList<Vec3> rim, Vec3 normal)
    {
        int start = m.VertexCount;
        Primitives.Fan(m, centre, rim, normal, White, White, 1, 4);
        for (int i = start; i < m.VertexCount; i++)
        {
            var off = m.Position(i) - attach;
            m.SetColor(i, off.X, off.Y, off.Z, 1);
        }
    }

    private static void Springtail(MeshData m)
    {
        // head, thorax, abdomen (elongated, slightly humped); length 1 along +X
        Primitives.Ellipsoid(m, new Vec3(0.36, 0.13, 0), new Vec3(0.13, 0.09, 0.1), 8, 10, (a, b) => Region(0.9 - a * 0.1, b, 1));
        Primitives.Ellipsoid(m, new Vec3(0.12, 0.14, 0), new Vec3(0.17, 0.11, 0.12), 8, 10, (a, b) => Region(0.65 - a * 0.2, b, 1));
        Primitives.Ellipsoid(m, new Vec3(-0.2, 0.15, 0), new Vec3(0.26, 0.13, 0.14), 8, 10, (a, b) => Region(0.45 - a * 0.4, b, 1));
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.44, 0.17, 0.05 * z), new Vec3(0.025, 0.025, 0.025), 4, 6, (a, b) => Region(0.95, b, 2)); // eyes
            Appendage(m, new[] { new Vec3(0.45, 0.17, 0.04 * z), new Vec3(0.58, 0.26, 0.12 * z), new Vec3(0.72, 0.3, 0.2 * z) }, new[] { 0.018, 0.013, 0.008 }, 4); // antennae
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.22 - leg * 0.1;
                Appendage(m, new[] { new Vec3(x, 0.08, 0.08 * z), new Vec3(x + 0.02, 0.07, 0.18 * z), new Vec3(x - 0.01, 0.0, 0.22 * z) }, new[] { 0.014, 0.011, 0.008 }, 4);
            }
        }
        // furcula (spring tail) folded under the abdomen
        Appendage(m, new[] { new Vec3(-0.4, 0.07, 0), new Vec3(-0.3, 0.03, 0), new Vec3(-0.05, 0.02, 0) }, new[] { 0.02, 0.015, 0.01 }, 4);
    }

    private static void Shrimp(MeshData m)
    {
        // curved, tapering segmented body: carapace + 6 abdominal segments
        Primitives.Ellipsoid(m, new Vec3(0.18, 0.2, 0), new Vec3(0.24, 0.12, 0.1), 10, 12, (a, b) => Region(0.8 - a * 0.25, b, 1));
        for (int s = 0; s < 6; s++)
        {
            double t = s / 5.0;
            double x = -0.05 - t * 0.38, y = 0.18 - t * t * 0.08;
            double r = 0.1 * (1 - t * 0.55);
            Primitives.Ellipsoid(m, new Vec3(x, y, 0), new Vec3(0.075, r, r * 0.85), 6, 10, (a, b) => Region(0.5 - t * 0.45, b, b > 0.6 ? 3 : 1), pitch: -t * 0.5);
        }
        // tail fan
        var tailAttach = new Vec3(-0.46, 0.1, 0);
        AppendageFan(m, tailAttach, tailAttach, new List<Vec3> { tailAttach + new Vec3(-0.02, 0.0, -0.1), tailAttach + new Vec3(-0.14, -0.02, -0.08), tailAttach + new Vec3(-0.16, -0.02, 0), tailAttach + new Vec3(-0.14, -0.02, 0.08), tailAttach + new Vec3(-0.02, 0.0, 0.1) }, Vec3.Up);
        // rostrum, eyes, antennae, legs
        Appendage(m, new[] { new Vec3(0.4, 0.24, 0), new Vec3(0.5, 0.26, 0) }, new[] { 0.015, 0.004 }, 4, 1);
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.38, 0.25, 0.06 * z), new Vec3(0.03, 0.03, 0.03), 4, 6, (a, b) => Region(0.95, b, 2));
            Appendage(m, new[] { new Vec3(0.4, 0.22, 0.04 * z), new Vec3(0.7, 0.3, 0.18 * z), new Vec3(1.0, 0.25, 0.32 * z), new Vec3(1.25, 0.15, 0.4 * z) }, new[] { 0.01, 0.007, 0.005, 0.003 }, 4);
            for (int leg = 0; leg < 5; leg++)
            {
                double x = 0.28 - leg * 0.08;
                Appendage(m, new[] { new Vec3(x, 0.1, 0.05 * z), new Vec3(x + 0.03, 0.04, 0.12 * z), new Vec3(x + 0.02, 0.0, 0.16 * z) }, new[] { 0.01, 0.008, 0.005 }, 3);
            }
        }
    }

    private static void Triops(MeshData m)
    {
        // broad shield carapace (flattened dome) over the front, segmented abdomen behind
        Primitives.Ellipsoid(m, new Vec3(0.15, 0.08, 0), new Vec3(0.34, 0.08, 0.3), 12, 16, (a, b) => Region(0.9 - a * 0.4, b, b > 0.55 ? 3 : 1));
        for (int s = 0; s < 7; s++)
        {
            double t = s / 6.0;
            double r = 0.07 * (1 - t * 0.5);
            Primitives.Ellipsoid(m, new Vec3(-0.12 - t * 0.28, 0.06, 0), new Vec3(0.05, r * 0.6, r), 5, 8, (a, b) => Region(0.4 - t * 0.35, b, 1));
        }
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.34, 0.15, 0.04 * z), new Vec3(0.022, 0.015, 0.022), 4, 6, (a, b) => Region(0.95, b, 2));
            Appendage(m, new[] { new Vec3(-0.42, 0.06, 0.02 * z), new Vec3(-0.62, 0.07, 0.07 * z), new Vec3(-0.85, 0.06, 0.12 * z) }, new[] { 0.012, 0.008, 0.004 }, 4);
            for (int leg = 0; leg < 6; leg++)
            {
                double x = 0.2 - leg * 0.06;
                Appendage(m, new[] { new Vec3(x, 0.03, 0.12 * z), new Vec3(x - 0.02, 0.0, 0.24 * z) }, new[] { 0.01, 0.005 }, 3);
            }
        }
    }

    private static void Isopod(MeshData m)
    {
        // domed oval of seven overlapping armour plates (pereon), small head, tapering tail plates; length 1 along +X
        Primitives.Ellipsoid(m, new Vec3(0.02, 0.05, 0), new Vec3(0.44, 0.045, 0.24), 6, 12, (a, b) => Region(0.5, b, 3)); // pale underside
        for (int s = 0; s < 7; s++)
        {
            double t = s / 6.0;
            double x = 0.27 - t * 0.5;
            double dome = Math.Sin(Math.PI * (0.18 + t * 0.64));
            double w = 0.25 + 0.03 * dome, h = 0.1 + 0.08 * dome;
            Primitives.Ellipsoid(m, new Vec3(x, 0.07, 0), new Vec3(0.062, h, w), 6, 14, (a, b) => Region(0.85 - t * 0.6, b, b > 0.3 && b < 0.7 ? 1 : 1), pitch: -0.18);
        }
        for (int s = 0; s < 4; s++)
        {
            double t = s / 3.0;
            double w = 0.19 - t * 0.09;
            Primitives.Ellipsoid(m, new Vec3(-0.29 - t * 0.07, 0.07, 0), new Vec3(0.045, 0.085 - t * 0.03, w), 5, 12, (a, b) => Region(0.22 - t * 0.15, b, 1), pitch: -0.25);
        }
        Primitives.Ellipsoid(m, new Vec3(-0.43, 0.05, 0), new Vec3(0.05, 0.035, 0.07), 4, 8, (a, b) => Region(0.03, b, 1)); // telson
        Primitives.Ellipsoid(m, new Vec3(0.37, 0.08, 0), new Vec3(0.07, 0.07, 0.15), 6, 10, (a, b) => Region(0.95, b, 1));  // head
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.4, 0.1, 0.11 * z), new Vec3(0.022, 0.022, 0.018), 4, 6, (a, b) => Region(0.97, b, 2)); // eyes
            // elbowed antennae
            Appendage(m, new[] { new Vec3(0.43, 0.08, 0.06 * z), new Vec3(0.52, 0.12, 0.13 * z), new Vec3(0.6, 0.08, 0.2 * z), new Vec3(0.68, 0.03, 0.22 * z) }, new[] { 0.016, 0.013, 0.01, 0.006 }, 4);
            for (int leg = 0; leg < 7; leg++)
            {
                double x = 0.26 - leg * 0.075;
                Appendage(m, new[] { new Vec3(x, 0.04, 0.16 * z), new Vec3(x + 0.01, 0.03, 0.25 * z), new Vec3(x - 0.01, 0.0, 0.28 * z) }, new[] { 0.013, 0.01, 0.006 }, 3);
            }
            // uropods (little tail prongs)
            Appendage(m, new[] { new Vec3(-0.42, 0.04, 0.05 * z), new Vec3(-0.49, 0.03, 0.09 * z) }, new[] { 0.014, 0.008 }, 3);
        }
    }

    private static void Minnow(MeshData m)
    {
        // spindle body via lathe: profile radius along length
        var path = new List<Vec3>(); var radius = new List<double>();
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0;
            double x = 0.42 - t * 0.78;
            double r = 0.105 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.8)), 0.75) + 0.004;
            path.Add(new Vec3(x, 0.14, 0)); radius.Add(r);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, 14, (i, v) => (Body, 0, 1 - i / 14.0, v, v > 0.35 && v < 0.65 ? 3 : 1, 0), new Vec3(0, 0, 1));
        // flatten laterally a little (fish are compressed side to side)
        for (int i = start; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            m.Positions[i * 3 + 2] = (float)(p.Z * 0.72);
        }
        foreach (double z in new[] { -1.0, 1.0 })
            Primitives.Ellipsoid(m, new Vec3(0.33, 0.16, 0.055 * z), new Vec3(0.022, 0.022, 0.012), 4, 6, (a, b) => Region(0.95, b, 2));
        // forked caudal fin
        var ta = new Vec3(-0.36, 0.14, 0);
        AppendageFan(m, ta, ta, new List<Vec3> { ta + new Vec3(0, 0.02, 0), ta + new Vec3(-0.2, 0.13, 0), ta + new Vec3(-0.12, 0.0, 0), ta + new Vec3(-0.2, -0.13, 0), ta + new Vec3(0, -0.02, 0) }, new Vec3(0, 0, 1));
        // dorsal + anal fins
        var da = new Vec3(-0.05, 0.24, 0);
        AppendageFan(m, da, da, new List<Vec3> { da + new Vec3(0.06, 0, 0), da + new Vec3(-0.02, 0.08, 0), da + new Vec3(-0.1, 0.0, 0) }, new Vec3(0, 0, 1));
        var aa = new Vec3(-0.12, 0.06, 0);
        AppendageFan(m, aa, aa, new List<Vec3> { aa + new Vec3(0.05, 0, 0), aa + new Vec3(-0.03, -0.06, 0), aa + new Vec3(-0.09, 0, 0) }, new Vec3(0, 0, 1));
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var pa = new Vec3(0.2, 0.1, 0.06 * z);
            AppendageFan(m, pa, pa, new List<Vec3> { pa, pa + new Vec3(-0.08, -0.03, 0.06 * z), pa + new Vec3(-0.03, -0.05, 0.02 * z) }, new Vec3(0, 1, 0));
        }
    }
}
