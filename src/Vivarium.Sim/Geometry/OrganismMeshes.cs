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
                    double ang = 2 * Math.PI * k / 11 + rng.Range(-0.28, 0.28), len = rng.Range(0.52, 1.0);
                    var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                    var side = new Vec3(-dir.Z, 0, dir.X);
                    double curl = rng.Range(-0.12, 0.16), skew = rng.Range(-0.18, 0.18), shoulder = rng.Range(0.22, 0.38);
                    var rim = new List<Vec3>();
                    for (int q = 0; q <= 10; q++)
                    {
                        double u = (double)q / 10;
                        double th = u * Math.PI;
                        double envelope = Math.Sin(th);
                        double edgeNoise = Noise.Gradient(Rng.Mix(seed, (ulong)(k * 97 + 31)), u * 2.4, 0.37);
                        double reach = envelope * len * (1 + 0.08 * edgeNoise + skew * (u - 0.5));
                        double width = Math.Cos(th) * shoulder * len * (1 + 0.12 * edgeNoise);
                        double lift = 0.34 + 0.55 * envelope + curl * envelope * (u - 0.5) * 2 + edgeNoise * 0.045;
                        rim.Add(dir * (reach + 0.08) + side * width + new Vec3(0, lift, 0));
                    }
                    Primitives.Fan(m, dir * 0.08 + new Vec3(0, 0.18, 0), rim, Vec3.Up,
                        Primitives.Scale(c1, 0.85 + rng.Range(-0.04, 0.04)), Primitives.Mix(c1, c2, rng.Range(0.55, 0.9)));
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
                    double ang = 2 * Math.PI * k / 7 + rng.Range(-0.22, 0.22);
                    var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                    var side = new Vec3(-dir.Z, 0, dir.X);
                    double len = rng.Range(0.78, 0.98), width = rng.Range(0.15, 0.21);
                    var root = dir * rng.Range(0.00, 0.05) + new Vec3(0, 0.045, 0);
                    var tip = dir * len + new Vec3(0, rng.Range(0.08, 0.16), 0);
                    Primitives.CurvedLeaf(m, root, tip, side, width,
                        Primitives.Scale(c1, 0.78), Primitives.Scale(c2, 1.03),
                        camber: rng.Range(0.025, 0.065), longitudinal: 7, asymmetry: rng.Range(-0.18, 0.18));
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
            case "roundleaf":
            {
                // small round/kidney leaflets on short stalks, scattered across the mat
                int n = 40 + rng.NextInt(15);
                for (int k = 0; k < n; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.9;
                    var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    double stalkH = rng.Range(0.08, 0.18);
                    var top = b + new Vec3(0, stalkH, 0);
                    Primitives.Tube(m, new[] { b, top }, new[] { 0.008, 0.006 }, 4, (i, v) => (Primitives.Scale(c1, 0.7), 1, i, v, 0, 0));
                    double leafR = rng.Range(0.07, 0.13), notchAng = rng.Range(0, 2 * Math.PI);
                    var rim = new List<Vec3>();
                    for (int s = 0; s <= 10; s++)
                    {
                        double th = 2 * Math.PI * s / 10;
                        double rr = leafR * (1 - 0.35 * Math.Max(0, Math.Cos(th - notchAng)));
                        rim.Add(top + new Vec3(Math.Cos(th) * rr, 0.005, Math.Sin(th) * rr));
                    }
                    Primitives.Fan(m, top, rim, Vec3.Up, Primitives.Mix(c1, c2, rng.Range(0, 0.5)), c2);
                }
                break;
            }
            case "pairedleaf":
            {
                // opposite pairs of small leaves along thin stems, with occasional tiny white flowers
                int stems = 10 + rng.NextInt(6);
                for (int k = 0; k < stems; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.75;
                    var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    var lean = new Vec3(rng.Range(-0.15, 0.15), 0, rng.Range(-0.15, 0.15));
                    double h = rng.Range(0.25, 0.5);
                    var top = b + lean + new Vec3(0, h, 0);
                    Primitives.Tube(m, new[] { b, b + lean * 0.5 + new Vec3(0, h * 0.5, 0), top }, new[] { 0.01, 0.008, 0.005 }, 4, (i, v) => (Primitives.Scale(c1, 0.75), 1, i, v, 0, 0));
                    int pairs = 3 + rng.NextInt(2);
                    var side = new Vec3(-lean.Z, 0, lean.X);
                    if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
                    side = side.Normalized();
                    for (int p = 1; p <= pairs; p++)
                    {
                        double t = (double)p / (pairs + 1);
                        var c = b + lean * t + new Vec3(0, h * t, 0);
                        double sz = rng.Range(0.05, 0.08);
                        foreach (double sgn in new[] { -1.0, 1.0 })
                        {
                            var tip = c + side * (sgn * sz);
                            var rim = new List<Vec3> { c, c + side * (sgn * sz * 0.4) + new Vec3(0, sz * 0.3, 0), tip, c + side * (sgn * sz * 0.4) + new Vec3(0, -sz * 0.3, 0), c };
                            Primitives.Fan(m, c, rim, Vec3.Up, Primitives.Scale(c1, 0.85), c1);
                        }
                    }
                    if (rng.NextDouble() < 0.3)
                    {
                        var star = new List<Vec3>();
                        for (int s = 0; s <= 8; s++) { double th = 2 * Math.PI * s / 8, rr = s % 2 == 0 ? 0.045 : 0.018; star.Add(top + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr)); }
                        Primitives.Fan(m, top + new Vec3(0, 0.015, 0), star, Vec3.Up, new[] { 1.0, 1.0, 1.0 }, c2);
                    }
                }
                break;
            }
            case "capitula":
            {
                // upright stems ending in small star-shaped heads (peat moss capitula)
                int n = 60 + rng.NextInt(20);
                for (int k = 0; k < n; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.85;
                    var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    double h = rng.Range(0.4, 1.0);
                    var top = b + new Vec3(0, h, 0);
                    var tipCol = Primitives.Mix(c1, c2, rng.Range(0, 1));
                    Primitives.Tube(m, new[] { b, top }, new[] { 0.012, 0.009 }, 5, (i, v) => (Primitives.Mix(Primitives.Scale(c1, 0.8), tipCol, i), 1, i, v, 0, 0));
                    var star = new List<Vec3>();
                    for (int s = 0; s <= 8; s++) { double th = 2 * Math.PI * s / 8 + ang, rr = s % 2 == 0 ? 0.05 : 0.022; star.Add(top + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr)); }
                    Primitives.Fan(m, top + new Vec3(0, 0.015, 0), star, Vec3.Up, tipCol, Primitives.Scale(tipCol, 0.85));
                }
                break;
            }
            case "trifoliate":
            {
                // three-lobed clover leaves on stalks, with an occasional white globe flower head
                int n = 18 + rng.NextInt(8);
                for (int k = 0; k < n; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.8;
                    var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    double h = rng.Range(0.25, 0.5);
                    var top = b + new Vec3(0, h, 0);
                    Primitives.Tube(m, new[] { b, top }, new[] { 0.01, 0.006 }, 4, (i, v) => (Primitives.Scale(c1, 0.75), 1, i, v, 0, 0));
                    double lobeR = rng.Range(0.06, 0.11);
                    for (int l = 0; l < 3; l++)
                    {
                        double la = 2 * Math.PI * l / 3 + rng.Range(-0.1, 0.1);
                        var dir = new Vec3(Math.Cos(la), 0, Math.Sin(la));
                        var side = new Vec3(-dir.Z, 0, dir.X);
                        var tip = top + dir * lobeR;
                        var rim = new List<Vec3> { top, top + side * (lobeR * 0.5) + dir * (lobeR * 0.4), tip, top - side * (lobeR * 0.5) + dir * (lobeR * 0.4), top };
                        Primitives.Fan(m, top, rim, Vec3.Up, Primitives.Mix(c1, c2, 0.3), Primitives.Mix(c1, c2, 0.6));
                    }
                    if (rng.NextDouble() < 0.15)
                    {
                        var fh = top + new Vec3(0, 0.05, 0);
                        var star = new List<Vec3>();
                        for (int s = 0; s <= 10; s++) { double th = 2 * Math.PI * s / 10, rr = 0.035; star.Add(fh + new Vec3(Math.Cos(th) * rr, Math.Sin(th) * rr * 0.4, Math.Sin(th) * rr)); }
                        Primitives.Fan(m, fh, star, Vec3.Up, new[] { 1.0, 1.0, 0.96 }, new[] { 0.96, 0.9, 0.7 });
                    }
                }
                break;
            }
            case "iceplant":
            {
                // thick triangular-section succulent leaves in clusters, reddish tips, occasional magenta flower
                int n = 20 + rng.NextInt(8);
                for (int k = 0; k < n; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.85;
                    var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    int leaves = rng.NextInt(2) + 2;
                    for (int lf = 0; lf < leaves; lf++)
                    {
                        double la = ang + rng.Range(-0.5, 0.5);
                        var ldir = new Vec3(Math.Cos(la), 0, Math.Sin(la));
                        double len = rng.Range(0.35, 0.6), h = rng.Range(0.12, 0.2);
                        var tip = b + ldir * len + new Vec3(0, h * 0.5, 0);
                        var tipCol = Primitives.Mix(c1, new[] { 0.55, 0.12, 0.14 }, rng.Range(0.2, 0.6));
                        Primitives.Ellipsoid(m, (b + tip) * 0.5 + new Vec3(0, h * 0.3, 0), new Vec3(len * 0.5, h * 0.5, 0.05), 4, 6,
                            (u, v) => (Primitives.Mix(c1, tipCol, u), 1, u, v, 0, 0));
                    }
                    if (rng.NextDouble() < 0.12)
                    {
                        var fc = b + new Vec3(0, 0.18, 0);
                        var star = new List<Vec3>();
                        for (int s = 0; s <= 12; s++) { double th = 2 * Math.PI * s / 12, rr = s % 2 == 0 ? 0.09 : 0.03; star.Add(fc + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr)); }
                        Primitives.Fan(m, fc + new Vec3(0, 0.01, 0), star, Vec3.Up, new[] { 0.85, 0.15, 0.55 }, new[] { 0.95, 0.5, 0.75 });
                    }
                }
                break;
            }
            case "fern": Fern(m, rng, c1, c2); break;
            case "vine": Vine(m, rng, c1, c2); break;
            case "mushroom_cluster": Mushrooms(m, rng, c1, c2); break;
            case "bracket": Bracket(m, rng, c1, c2); break;
            case "plasmodium": Plasmodium(m, rng, c1, c2); break;
            case "succulent": Succulent(m, rng, c1, c2); break;
            case "tussock": Tussock(m, rng, c1, c2); break;
            case "fruticose": Fruticose(m, rng, c1, c2); break;
            default:
                Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(1, 1, 1), 6, 8, (a, b) => (c1, 1, a, b, 0, 0));
                break;
        }
        return m;
    }

    /// <summary>Alternate pose for organisms with a visible reproductive stage (slime mold sporangia); null otherwise.</summary>
    public static MeshData? FloraFruiting(FloraSpeciesDef sp, ulong seed = 1)
    {
        if (sp.Shape != "plasmodium") return null;
        var rng = Rng.Keyed(seed, "flora.fruit." + sp.Id, 0);
        var m = new MeshData();
        var stalk = new[] { 0.35, 0.28, 0.16 };
        var head = new[] { 0.18, 0.14, 0.1 };
        // a faded network left behind, crowded with tiny stalked spore cases (height axis is stretched ×8 below)
        Plasmodium(m, rng, Primitives.Scale(sp.Color, 0.55), Primitives.Scale(sp.Color2, 0.5));
        for (int k = 0; k < 40; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.8;
            var b = new Vec3(Math.Cos(ang) * r, 0.2, Math.Sin(ang) * r);
            double h = rng.Range(4, 7);
            Primitives.Tube(m, new[] { b, b + new Vec3(0, h, 0) }, new[] { 0.012, 0.008 }, 3, (i, v) => (stalk, 1, i, v, 0, 0));
            Primitives.Ellipsoid(m, b + new Vec3(0, h + 0.5, 0), new Vec3(0.04, 0.6, 0.04), 4, 6, (u, v) => (head, 1, u, v, 0, 0));
        }
        return m;
    }

    /// <summary>Arching pinnate fronds with fan leaflets, plus one unrolling fiddlehead.</summary>
    private static void Fern(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stalk = new[] { 0.24, 0.2, 0.1 };
        int fronds = 7 + rng.NextInt(3);
        for (int k = 0; k < fronds; k++)
        {
            double ang = 2 * Math.PI * k / fronds + rng.Range(-0.2, 0.2), reach = rng.Range(0.75, 1.0), rise = rng.Range(0.75, 1.0);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)); var side = new Vec3(-dir.Z, 0, dir.X);
            Vec3 At(double t) => dir * (reach * t) + new Vec3(0, rise * Math.Sin(Math.PI * t * 0.85) * (1 - 0.35 * t), 0);
            var path = new List<Vec3>(); var rad = new List<double>();
            for (int i = 0; i <= 8; i++) { path.Add(At(i / 8.0)); rad.Add(0.011 * (1 - i / 10.0)); }
            Primitives.Tube(m, path, rad, 3, (i, v) => (stalk, 1, i, v, 0, 0));
            for (int i = 2; i <= 14; i++)
            {
                double t = i / 15.0;
                var c = At(t);
                double len = 0.26 * Math.Sin(Math.PI * Math.Min(1, t * 1.15)) + 0.03;
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var tip = c + side * (sgn * len) + dir * (len * 0.25) + new Vec3(0, -len * 0.25, 0);
                    var planeNormal = (side * sgn).Cross(dir).Normalized();
                    var leafSide = planeNormal.Cross((tip - c).Normalized()).Normalized();
                    var col = Primitives.Mix(c1, c2, t);
                    Primitives.CurvedLeaf(m, c, tip, leafSide, len * rng.Range(0.15, 0.22),
                        Primitives.Scale(col, 0.82), col, camber: len * rng.Range(0.025, 0.06),
                        longitudinal: 5, asymmetry: rng.Range(-0.12, 0.12));
                }
            }
        }
        // fiddlehead: a coiled young frond
        var coil = new List<Vec3>(); var cr = new List<double>();
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0, th = t * Math.PI * 3.2, rr = 0.12 * (1 - t * 0.8);
            coil.Add(new Vec3(0.1 + Math.Sin(th) * rr, 0.45 + 0.35 * t + Math.Cos(th) * rr * 0.6, 0.05));
            cr.Add(0.03 * (1 - t * 0.6));
        }
        Primitives.Tube(m, coil, cr, 4, (i, v) => (Primitives.Mix(c1, c2, 0.8), 1, i, v, 0, 0));
    }

    /// <summary>Stems that rise from the root (origin) toward +X, arch over and drape down, with small heart leaves.
    /// The renderer turns +X toward the log/rock being climbed and scales the height to its top.</summary>
    private static void Vine(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stem = new[] { 0.3, 0.36, 0.17 };
        for (int k = 0; k < 6; k++)
        {
            double spread = rng.Range(-0.5, 0.5), over = rng.Range(1.0, 1.6), top = rng.Range(0.95, 1.15);
            double bendA = rng.Range(-0.16, 0.16), bendB = rng.Range(-0.11, 0.11), shoulder = rng.Range(0.32, 0.55);
            double drapeStrength = rng.Range(1.25, 1.65);
            Vec3 At(double t)
            {
                double x = t * over;
                double climb = top * Math.Sin(Math.PI * Math.Min(1, t * 1.2) * 0.5);
                double drape = t > 0.8 ? (t - 0.8) * drapeStrength : 0;
                double lateral = spread * (0.3 + t) + bendA * t * (1 - t) * 4
                    + bendB * t * (t - shoulder);
                return new Vec3(x, Math.Max(0, climb - drape), lateral);
            }
            var path = new List<Vec3>(); var rad = new List<double>();
            for (int i = 0; i <= 12; i++) { path.Add(At(i / 12.0)); rad.Add(0.011 * (1 - i / 16.0)); }
            Primitives.Tube(m, path, rad, 3, (i, v) => (stem, 1, i, v, 0, 0));
            for (int i = 1; i <= 22; i++)
            {
                var c = At(i / 22.0 - 0.01);
                double ang = rng.Range(0, 2 * Math.PI), sz = rng.Range(0.1, 0.17);
                var d = new Vec3(Math.Cos(ang), rng.Range(-0.2, 0.4), Math.Sin(ang)).Normalized();
                var side = d.Cross(Vec3.Up).Normalized();
                if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
                var col = Primitives.Mix(c1, c2, rng.Range(0, 1));
                var root = c + d * (sz * 0.08);
                var tip = c + d * (sz * rng.Range(1.35, 1.65));
                Primitives.CurvedLeaf(m, root, tip, side, sz * rng.Range(0.48, 0.66),
                    Primitives.Scale(col, 0.88), col, camber: sz * rng.Range(0.12, 0.22),
                    longitudinal: 6, asymmetry: rng.Range(-0.22, 0.22));
            }
        }
    }

    /// <summary>A troop of bonnet mushrooms: thin pale stems and conical, slightly translucent caps.</summary>
    private static void Mushrooms(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stemCol = new[] { 0.78, 0.74, 0.68 };
        int n = 5 + rng.NextInt(5);
        for (int k = 0; k < n; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.7, h = rng.Range(0.45, 1.0), capR = rng.Range(0.12, 0.2) * (0.6 + 0.4 * h);
            var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            var lean = new Vec3(rng.Range(-0.12, 0.12), 0, rng.Range(-0.12, 0.12));
            var top = b + lean + new Vec3(0, h, 0);
            Primitives.Tube(m, new[] { b, b + lean * 0.5 + new Vec3(0, h * 0.5, 0), top }, new[] { 0.035, 0.03, 0.026 }, 5, (i, v) => (stemCol, 1, i, v, 0, 0));
            // conical bonnet: rings from the tip down to a flared, striated rim (UV.y = 1 on the underside gills)
            int start = m.VertexCount;
            const int around = 12, rings = 5;
            for (int ri = 0; ri <= rings; ri++)
            {
                double t = (double)ri / rings;
                double rr = capR * Math.Pow(t, 0.7), yy = capR * 1.2 * (1 - t);
                var col = Primitives.Mix(c2, c1, Math.Min(1, t * 1.4));
                for (int s = 0; s <= around; s++)
                {
                    double th = 2 * Math.PI * s / around;
                    var dir = new Vec3(Math.Cos(th), 0, Math.Sin(th));
                    var pos = top + dir * rr + new Vec3(0, yy - capR * 0.15, 0);
                    m.AddVertex(pos, (dir * 0.6 + Vec3.Up).Normalized(), col, 1, (double)s / around, 0, t, 0);
                }
            }
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = start + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }
            // underside with gills
            var rim = new List<Vec3>();
            for (int s = 0; s <= around; s++) { double th = 2 * Math.PI * s / around; rim.Add(top + new Vec3(Math.Cos(th) * capR, -capR * 0.15, Math.Sin(th) * capR)); }
            Primitives.Fan(m, top + new Vec3(0, capR * 0.1, 0), rim, -Vec3.Up, Primitives.Scale(c1, 1.15), Primitives.Scale(c1, 0.95), 1, 1);
        }
    }

    /// <summary>Tiers of thin semicircular shelves with concentric colour bands (turkey tail), sticking out along +X.</summary>
    private static void Bracket(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var bands = new[] { Primitives.Scale(c1, 0.5), Primitives.Scale(c1, 1.1), Primitives.Mix(c1, new[] { 0.3, 0.32, 0.4 }, 0.6), Primitives.Scale(c1, 0.75), c2 };
        int tiers = 3 + rng.NextInt(3);
        for (int k = 0; k < tiers; k++)
        {
            double y = 0.15 + k * 0.75 / tiers + rng.Range(-0.04, 0.04), reach = rng.Range(0.6, 1.0), off = rng.Range(-0.35, 0.35);
            const int around = 14, rings = 5;
            int start = m.VertexCount;
            ulong edgeSeed = Rng.Mix((ulong)(k + 1) * 0x9E3779B97F4A7C15UL, (ulong)Math.Round(reach * 10000));
            double tilt = rng.Range(-0.055, 0.055), cup = rng.Range(0.025, 0.075);
            for (int ri = 0; ri <= rings; ri++)
            {
                double f = (double)ri / rings;
                for (int q = 0; q <= around; q++)
                {
                    double th = -Math.PI / 2 + Math.PI * q / around;
                    double n = Noise.Gradient(edgeSeed, Math.Cos(th) * 1.35, Math.Sin(th) * 1.35);
                    double n2 = Noise.Gradient(Rng.Mix(edgeSeed, 19), Math.Cos(th) * 2.7, Math.Sin(th) * 2.7);
                    double wav = 1 + 0.065 * n + 0.025 * n2;
                    double z = off + Math.Sin(th) * reach * f * wav * 0.9;
                    double yy = y + cup * f * f - 0.08 * f * Math.Abs(Math.Sin(th)) + tilt * Math.Sin(th) * f;
                    var pos = new Vec3(Math.Cos(th) * reach * f * wav, yy, z);
                    var col = Primitives.Mix(bands[Math.Min(ri, rings - 1)], bands[Math.Min(rings - 1, Math.Max(0, ri - 1))], MathD.Clamp01(0.25 + n * 0.15));
                    m.AddVertex(pos, Vec3.Up, col, 1, f, (double)q / around, 0, 0);
                }
            }
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = start + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }
            // cream pore surface underneath
            var rim = new List<Vec3>();
            for (int q = 0; q <= around; q++)
            {
                double th = -Math.PI / 2 + Math.PI * q / around;
                double n = Noise.Gradient(edgeSeed, Math.Cos(th) * 1.35, Math.Sin(th) * 1.35);
                double n2 = Noise.Gradient(Rng.Mix(edgeSeed, 19), Math.Cos(th) * 2.7, Math.Sin(th) * 2.7);
                double wav = 1 + 0.065 * n + 0.025 * n2;
                rim.Add(new Vec3(Math.Cos(th) * reach * wav,
                    y + cup - 0.08 * Math.Abs(Math.Sin(th)) + tilt * Math.Sin(th),
                    off + Math.Sin(th) * reach * wav * 0.9));
            }
            Primitives.Fan(m, new Vec3(0, y - 0.005, off), rim, -Vec3.Up,
                new[] { 0.62, 0.56, 0.44 }, new[] { 0.56, 0.5, 0.4 }, 1, 1);
        }
    }

    /// <summary>A mat of fleshy rosettes (stonecrop): plump spiralled leaves, blue-green with red-blushed tips.</summary>
    private static void Succulent(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int rosettes = 5 + rng.NextInt(4);
        for (int r = 0; r < rosettes; r++)
        {
            double ang = rng.Range(0, 2 * Math.PI), rad = r == 0 ? 0 : Math.Sqrt(rng.NextDouble()) * 0.7, size = rng.Range(0.22, 0.34) * (r == 0 ? 1.2 : 1);
            var centre = new Vec3(Math.Cos(ang) * rad, 0, Math.Sin(ang) * rad);
            int leaves = 14;
            for (int k = 0; k < leaves; k++)
            {
                double t = (double)k / leaves;
                double a = k * 2.39996;                                   // golden-angle spiral
                double out1 = size * (0.35 + 0.65 * (1 - t));             // outer leaves are longer
                double lift = 0.2 + 0.8 * t;                              // inner leaves stand up
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var c = centre + dir * (out1 * 0.5) + new Vec3(0, 0.15 + lift * 0.5, 0);
                var tip = Primitives.Mix(c1, c2, 0.25 + 0.5 * (1 - t));
                int start = m.VertexCount;
                Primitives.Ellipsoid(m, c, new Vec3(out1 * 0.55, 0.22 + 0.1 * t, out1 * 0.3), 4, 6,
                    (u, v) => (Primitives.Mix(c1, tip, u < 0.35 ? 1 - u / 0.35 : 0), 1, u, v, 0, 0), pitch: 0);
                // orient every vertex added for this leaf; tessellation is adaptive in Primitives.Ellipsoid.
                double cs = Math.Cos(-a), sn = Math.Sin(-a);
                for (int i = start; i < m.VertexCount; i++)
                {
                    var q = m.Position(i) - c;
                    var rq = new Vec3(q.X * cs - q.Z * sn, q.Y + (q.X * cs - q.Z * sn) * lift * 0.9, q.X * sn + q.Z * cs);
                    var fin = c + rq;
                    m.Positions[i * 3] = (float)fin.X; m.Positions[i * 3 + 1] = (float)fin.Y; m.Positions[i * 3 + 2] = (float)fin.Z;
                }
            }
        }
        m.RecomputeNormals();
    }

    /// <summary>A tussock grass: a dense dome of needle-fine blades fountaining from the crown, straw-tipped.</summary>
    private static void Tussock(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        for (int k = 0; k < 110; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r0 = Math.Sqrt(rng.NextDouble()) * 0.25;
            double spread = rng.Range(0.25, 1.0), h = rng.Range(0.6, 1.0) * (1.1 - spread * 0.3);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)); var side = new Vec3(-dir.Z, 0, dir.X);
            var b = dir * r0;
            var mid = b + dir * (spread * 0.45) + new Vec3(0, h * 0.75, 0);
            var tip = b + dir * (spread * 1.0) + new Vec3(0, h * (0.55 + 0.3 * rng.NextDouble()), 0);
            double w = rng.Range(0.012, 0.02);                      // needle-fine blades (half-width, radius units)
            var tipCol = Primitives.Mix(c1, c2, rng.Range(0.05, 0.45));   // mostly blue-green, a few straw tips
            Blade(m, b, mid, tip, side, w, Primitives.Scale(c1, 0.8), tipCol);
        }
    }

    /// <summary>
    /// A tapered, double-sided ribbon along the quadratic curve base → (control) → tip. A fan from the base would
    /// fill the whole area under an arching blade; a ribbon keeps it needle-thin along its length.
    /// </summary>
    private static void Blade(MeshData m, Vec3 b, Vec3 control, Vec3 tip, Vec3 side, double halfWidth, double[] colBase, double[] colTip)
    {
        const int n = 6;
        var pts = new Vec3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            pts[i] = b * (u * u) + control * (2 * u * t) + tip * (t * t);
        }
        for (int face = 0; face < 2; face++)
        {
            int start = m.VertexCount;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n, wHere = halfWidth * (1 - t * 0.9);
                var along = (i < n ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).Normalized();
                var nrm = along.Cross(side).Normalized() * (face == 0 ? 1 : -1);
                var col = Primitives.Mix(colBase, colTip, t);
                m.AddVertex(pts[i] - side * wHere, nrm, col, 1, t, 0, 0, 0);
                m.AddVertex(pts[i] + side * wHere, nrm, col, 1, t, 1, 0, 0);
            }
            for (int i = 0; i < n; i++)
            {
                int a0 = start + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                var nrm = m.NormalAt(a0);
                Primitives.TriangleFacing(m, a0, b0, a1, nrm);
                Primitives.TriangleFacing(m, a1, b0, b1, nrm);
            }
        }
    }

    /// <summary>Fruticose lichen: a spongy mound of finely forking hollow stalks with pale, nodding tips.</summary>
    private static void Fruticose(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        void Branch(Vec3 from, Vec3 dir, double len, double rad, int depth)
        {
            var to = from + dir * len;
            Primitives.Tube(m, new[] { from, (from + to) * 0.5 + new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02)), to }, new[] { rad, rad * 0.85, rad * 0.7 }, 4,
                (i, v) => (Primitives.Mix(c1, c2, (3 - depth) / 3.0 + i * 0.1), 1, i, v, 0, 0));
            if (depth <= 0) { Primitives.Ellipsoid(m, to, new Vec3(rad, rad, rad), 3, 5, (a, b) => (c2, 1, a, b, 0, 0)); return; }
            for (int k = 0; k < 2; k++)
            {
                var nd = (dir + new Vec3(rng.Range(-0.6, 0.6), rng.Range(-0.1, 0.3), rng.Range(-0.6, 0.6))).Normalized();
                Branch(to, nd, len * 0.72, rad * 0.72, depth - 1);
            }
        }
        for (int k = 0; k < 9; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.65;
            var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            var dir = (new Vec3(Math.Cos(ang) * 0.35, 1, Math.Sin(ang) * 0.35)).Normalized();
            Branch(b, dir, rng.Range(0.3, 0.42), 0.06, 3);
        }
    }

    /// <summary>Slime-mold plasmodium: a branching fan of flattened yellow veins with a thin advancing front.</summary>
    private static void Plasmodium(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        void Branch(Vec3 from, double ang, double len, double rad, int depth)
        {
            var path = new List<Vec3> { from }; var rs = new List<double> { rad };
            var p = from;
            for (int i = 1; i <= 5; i++)
            {
                ang += rng.Range(-0.35, 0.35);
                p += new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * (len / 5);
                path.Add(new Vec3(p.X, 0.3 + 0.2 * rng.NextDouble(), p.Z)); rs.Add(rad * (1 - i / 7.0));
            }
            Primitives.Tube(m, path, rs, 4, (i, v) => (Primitives.Mix(c1, c2, i / 5.0), 1, i, v, 0, 0));
            if (depth <= 0) return;
            for (int b = 0; b < 2; b++) Branch(path[3 + b], ang + (b == 0 ? -0.6 : 0.6) + rng.Range(-0.2, 0.2), len * 0.6, rad * 0.6, depth - 1);
        }
        double baseAng = rng.Range(0, 2 * Math.PI);
        for (int k = 0; k < 5; k++) Branch(new Vec3(0, 0.3, 0), baseAng + k * 0.5 - 1.0, rng.Range(0.6, 0.85), 0.06, 2);
        // advancing front: a thin fan sheet on the leading side
        var rim = new List<Vec3>();
        for (int s = 0; s <= 14; s++)
        {
            double th = baseAng - 1.3 + 2.6 * s / 14;
            double rr = 0.9 + 0.1 * Math.Sin(s * 1.7);
            rim.Add(new Vec3(Math.Cos(th) * rr, 0.2, Math.Sin(th) * rr));
        }
        Primitives.Fan(m, new Vec3(Math.Cos(baseAng) * 0.55, 0.2, Math.Sin(baseAng) * 0.55), rim, Vec3.Up, Primitives.Scale(c2, 0.95), c1, 1, 2);
    }

    // ------------------------------------------------------------------ fauna

    private static readonly double[] White = { 1, 1, 1 };

    public static MeshData Fauna(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var m = new MeshData();
        switch (sp.Model)
        {
            case "springtail": Springtail(m); break;
            case "shrimp": Shrimp(m); break;
            case "triops": Triops(m); break;
            case "minnow": Minnow(m); break;
            case "isopod": Isopod(m); break;
            case "beetle": Beetle(m); break;
            case "silverfish": Silverfish(m); break;
            default: Primitives.Ellipsoid(m, new Vec3(0, 0.2, 0), new Vec3(0.5, 0.2, 0.2), 8, 10, (a, b) => (Body, 0, a, b, 1, 0)); break;
        }
        if (visualSeed != 0) ApplyFaunaMorph(m, sp.Model, visualSeed);
        return m;
    }

    /// <summary>Rolled-up pose for conglobating species (pill bug ball); null for species that never curl.</summary>
    public static MeshData? FaunaCurled(FaunaSpeciesDef sp, ulong visualSeed = 0)
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
        if (visualSeed != 0) ApplyFaunaMorph(m, sp.Model, visualSeed);
        return m;
    }

    private static void ApplyFaunaMorph(MeshData m, string model, ulong seed)
    {
        var rng = Rng.Keyed(seed, "fauna.visual.morph", 0);
        double length = rng.Range(0.94, 1.07);
        double height = rng.Range(0.92, 1.08);
        double width = rng.Range(0.92, 1.09);
        double appendageLong = rng.Range(0.91, 1.10);
        double appendageVert = rng.Range(0.94, 1.07);
        double appendageWide = rng.Range(0.91, 1.11);
        double asym = rng.Range(-0.055, 0.055);
        double arch = rng.Range(-0.018, 0.022);

        // Aquatic bodies benefit from slightly more shape diversity; plated terrestrial animals stay tighter so
        // their joints continue to overlap plausibly.
        if (model is "minnow" or "shrimp" or "triops")
        {
            length *= rng.Range(0.96, 1.05);
            height *= rng.Range(0.96, 1.05);
            width *= rng.Range(0.95, 1.06);
        }
        else if (model is "isopod" or "beetle")
        {
            length = MathD.Lerp(1, length, 0.7);
            height = MathD.Lerp(1, height, 0.7);
            width = MathD.Lerp(1, width, 0.75);
        }

        Vec3 MorphBody(Vec3 p)
        {
            double envelope = Math.Max(0, 1.0 - Math.Abs(p.X) / 0.65);
            return new Vec3(
                p.X * length,
                p.Y * height + arch * envelope,
                p.Z * width + asym * envelope * 0.035);
        }

        for (int i = 0; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            bool appendage = m.Colors[i * 4 + 3] > 0.5f;
            Vec3 outP;
            if (appendage)
            {
                var off = new Vec3(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2]);
                var attach = p - off;
                var a2 = MorphBody(attach);
                double side = off.Z < 0 ? -1 : off.Z > 0 ? 1 : 0;
                var off2 = new Vec3(
                    off.X * appendageLong,
                    off.Y * appendageVert,
                    off.Z * appendageWide * (1.0 + asym * side));
                // Tiny inherited sweep differences keep antennae/legs/fins from sharing one exact outline.
                off2 = new Vec3(off2.X + asym * off2.Z * 0.32, off2.Y, off2.Z - asym * off2.X * 0.22);
                outP = a2 + off2;
                m.Colors[i * 4] = (float)off2.X;
                m.Colors[i * 4 + 1] = (float)off2.Y;
                m.Colors[i * 4 + 2] = (float)off2.Z;
            }
            else outP = MorphBody(p);

            m.Positions[i * 3] = (float)outP.X;
            m.Positions[i * 3 + 1] = (float)outP.Y;
            m.Positions[i * 3 + 2] = (float)outP.Z;
        }
        m.RecomputeNormals();
    }

    private static readonly double[] Body = { 0, 0, 0 };

    private static (double[] Col, double A, double U, double V, double U2, double V2) Region(double u, double v, int region) => (Body, 0, u, v, region, 0);

    /// <summary>Appendage vertex attributes: colour carries the offset from the attachment point.</summary>
    private static void Appendage(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radius, int segs, int region = 4)
    {
        if (path.Count < 2 || radius.Count != path.Count) return;
        var attach = path[0];

        // Build a short flared root into the first segment instead of starting a constant-radius tube abruptly at
        // the body surface. It reads as a joint/socket at macro distance and removes the "tube glued to ellipsoid"
        // construction tell without needing a separate overlapping primitive.
        var p = new List<Vec3>(path.Count + 1) { attach, Vec3.Lerp(path[0], path[1], 0.18) };
        var r = new List<double>(radius.Count + 1) { radius[0] * 1.28, radius[0] * 1.08 };
        for (int k = 1; k < path.Count; k++) { p.Add(path[k]); r.Add(radius[k]); }

        int start = m.VertexCount;
        Primitives.Tube(m, p, r, segs, (i, v) => (Body, 1, 0.5, v, region, 0));
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
        // Entomobryid collembolan, length 1 along +X: one plump, slightly humped capsule (no waist), a large
        // rounded head, long four-segmented antennae, three short leg pairs, the spring (furcula) folded forward
        // under the rear and a small ventral tube. Segment rings come from the body UV in the shader.
        var path = new List<Vec3>(); var radius = new List<double>();
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;                                        // 0 = neck, 1 = tail tip
            double x = 0.2 - t * 0.66;
            double r = 0.125 * Math.Pow(Math.Sin(Math.PI * (0.12 + 0.86 * t)), 0.55) + 0.012;
            path.Add(new Vec3(x, 0.14 + 0.03 * Math.Sin(Math.PI * t), 0)); radius.Add(r);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, 14, (i, v) => (Body, 0, 0.72 - i / 16.0 * 0.7, v, v > 0.62 && v < 0.88 ? 3 : 1, 0), new Vec3(0, 0, 1));
        for (int i = start; i < m.VertexCount; i++)   // slightly flattened belly, rounder back
        {
            var q = m.Position(i);
            if (q.Y < 0.14) m.Positions[i * 3 + 1] = (float)(0.14 + (q.Y - 0.14) * 0.75);
        }
        // head: big and round, tilted a little downward
        Primitives.Ellipsoid(m, new Vec3(0.31, 0.13, 0), new Vec3(0.13, 0.105, 0.115), 8, 12, (a, b) => Region(0.86 + a * 0.1, b, 1), pitch: -0.25);
        foreach (double z in new[] { -1.0, 1.0 })
        {
            // eye patches (clusters of ocelli) on the sides of the head
            Primitives.Ellipsoid(m, new Vec3(0.35, 0.17, 0.085 * z), new Vec3(0.03, 0.022, 0.02), 4, 6, (a, b) => Region(0.97, b, 2));
            // antennae: four segments, gently elbowed, about 70 % of body length
            var ant = new List<Vec3> { new Vec3(0.41, 0.17, 0.05 * z) };
            var dir = new Vec3(0.85, 0.35, 0.4 * z).Normalized();
            double[] seg = { 0.12, 0.14, 0.16, 0.2 };
            for (int k = 0; k < 4; k++)
            {
                dir = (dir + new Vec3(0.05, -0.12 * k, 0.05 * z)).Normalized();
                ant.Add(ant[^1] + dir * seg[k]);
            }
            Appendage(m, ant, new[] { 0.02, 0.017, 0.014, 0.011, 0.007 }, 4);
            // three short leg pairs under the thorax
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.17 - leg * 0.085;
                Appendage(m, new[] { new Vec3(x, 0.07, 0.06 * z), new Vec3(x + 0.015, 0.05, 0.13 * z), new Vec3(x - 0.01, 0.0, 0.15 * z) }, new[] { 0.016, 0.012, 0.008 }, 4);
            }
        }
        // ventral tube (collophore) and the folded spring
        Primitives.Ellipsoid(m, new Vec3(0.04, 0.03, 0), new Vec3(0.03, 0.025, 0.025), 4, 6, (a, b) => Region(0.5, b, 3));
        Appendage(m, new[] { new Vec3(-0.38, 0.06, 0), new Vec3(-0.3, 0.025, 0), new Vec3(-0.12, 0.02, 0.03), new Vec3(-0.02, 0.025, 0.035) }, new[] { 0.022, 0.018, 0.012, 0.008 }, 4);
        Appendage(m, new[] { new Vec3(-0.3, 0.025, 0), new Vec3(-0.12, 0.02, -0.03), new Vec3(-0.02, 0.025, -0.035) }, new[] { 0.016, 0.012, 0.008 }, 4);
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

    private static void Beetle(MeshData m)
    {
        // darkling beetle, length 1 along +X: domed fused wing cases with a centre seam, a wide pronotum, a small
        // head with beaded antennae, and six long legs
        Primitives.Ellipsoid(m, new Vec3(-0.1, 0.17, 0), new Vec3(0.34, 0.17, 0.2), 10, 16, (a, b) => Region(0.55 - a * 0.5, b, b > 0.3 && b < 0.7 ? 3 : 1));  // elytra
        Primitives.Ellipsoid(m, new Vec3(-0.1, 0.335, 0), new Vec3(0.3, 0.008, 0.006), 3, 4, (a, b) => Region(0.5, b, 2));                                      // seam
        Primitives.Ellipsoid(m, new Vec3(0.25, 0.15, 0), new Vec3(0.11, 0.1, 0.17), 8, 12, (a, b) => Region(0.8, b, 1));                                        // pronotum
        Primitives.Ellipsoid(m, new Vec3(0.39, 0.12, 0), new Vec3(0.07, 0.06, 0.09), 6, 10, (a, b) => Region(0.95, b, 1));                                      // head
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.43, 0.14, 0.06 * z), new Vec3(0.018, 0.016, 0.015), 4, 6, (a, b) => Region(0.97, b, 2));
            var ant = new List<Vec3> { new Vec3(0.45, 0.13, 0.05 * z) };
            for (int k = 1; k <= 5; k++) ant.Add(ant[^1] + new Vec3(0.045, 0.012, 0.03 * z));
            Appendage(m, ant, new[] { 0.013, 0.012, 0.013, 0.014, 0.015, 0.017 }, 4);
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.24 - leg * 0.16, sweep = (leg - 1) * 0.12;
                Appendage(m, new[] { new Vec3(x, 0.07, 0.1 * z), new Vec3(x + sweep, 0.12, 0.26 * z), new Vec3(x + sweep * 1.8, 0.0, 0.34 * z) }, new[] { 0.02, 0.016, 0.01 }, 4);
            }
        }
    }

    private static void Silverfish(MeshData m)
    {
        // silverfish, length 1 along +X: a flattened, tapering carrot of a body, long antennae forward and three long
        // tail bristles behind, short legs tucked under the thorax
        var path = new List<Vec3>(); var radius = new List<double>();
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0;
            path.Add(new Vec3(0.36 - t * 0.74, 0.07, 0)); radius.Add(0.09 * Math.Pow(1 - t * 0.85, 0.9) * Math.Min(1, (t + 0.08) * 6) + 0.006);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, 12, (i, v) => (Body, 0, 0.75 - i / 14.0 * 0.7, v, v > 0.62 && v < 0.88 ? 3 : 1, 0), new Vec3(0, 0, 1));
        for (int i = start; i < m.VertexCount; i++)   // flatten top to bottom
        {
            var q = m.Position(i);
            m.Positions[i * 3 + 1] = (float)(0.07 + (q.Y - 0.07) * 0.45);
        }
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.37, 0.085, 0.04 * z), new Vec3(0.015, 0.012, 0.012), 4, 6, (a, b) => Region(0.97, b, 2));
            Appendage(m, new[] { new Vec3(0.4, 0.08, 0.02 * z), new Vec3(0.6, 0.1, 0.12 * z), new Vec3(0.85, 0.08, 0.2 * z) }, new[] { 0.009, 0.006, 0.003 }, 3);
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.26 - leg * 0.08;
                Appendage(m, new[] { new Vec3(x, 0.04, 0.06 * z), new Vec3(x - 0.03, 0.02, 0.12 * z), new Vec3(x - 0.06, 0.0, 0.14 * z) }, new[] { 0.012, 0.009, 0.006 }, 3);
            }
            Appendage(m, new[] { new Vec3(-0.37, 0.07, 0.01 * z), new Vec3(-0.6, 0.075, 0.1 * z), new Vec3(-0.8, 0.07, 0.16 * z) }, new[] { 0.008, 0.005, 0.003 }, 3);
        }
        Appendage(m, new[] { new Vec3(-0.37, 0.07, 0), new Vec3(-0.62, 0.08, 0), new Vec3(-0.85, 0.075, 0) }, new[] { 0.008, 0.005, 0.003 }, 3);
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
