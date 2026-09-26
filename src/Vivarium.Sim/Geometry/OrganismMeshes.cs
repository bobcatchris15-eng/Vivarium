using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

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
            case "creeper": Creeper(m, rng, seed, c1, c2); break;
            case "reed":
                for (int k = 0; k < 11; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), r = rng.Range(0, 0.45);
                    var baseP = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
                    var lean = new Vec3(rng.Range(-0.35, 0.35), 0, rng.Range(-0.35, 0.35));
                    double h = rng.Range(0.6, 1.0), w = rng.Range(0.03, 0.06);
                    var side = new Vec3(Math.Cos(ang + Math.PI / 2), 0, Math.Sin(ang + Math.PI / 2));
                    var tip = baseP + lean * h + new Vec3(0, h, 0);
                    Primitives.CurvedLeaf(m, baseP + new Vec3(0, 0.015, 0), tip, side, w,
                        Primitives.Scale(c1, 0.78), c2, camber: rng.Range(0.01, 0.035),
                        longitudinal: 7, asymmetry: rng.Range(-0.10, 0.10));
                }
                break;
            case "herb":
            {
                // irregular basal crown of kernel-built lanceolate leaves (cambered, midrib-folded, drooping tips,
                // random overlapping angles/lengths, always GREEN regardless of species tint) around several
                // leaning flower stalks, each carrying a kernel-staged cambered cup coloured with the species
                // tint (lilac/violet) so the plant no longer reads as a flat lilac star.
                var leafBase = new[] { 0.14, 0.42, 0.10 };
                var leafTip = new[] { 0.32, 0.62, 0.22 };
                int nLeaves = 5 + rng.NextInt(3); // 5..7 irregular leaves; low detail retains 72 triangles per blade
                for (int k = 0; k < nLeaves; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI); // fully irregular, not evenly spaced -> leaves overlap
                    double len = rng.Range(0.55, 1.0);       // unequal lengths
                    var leafAxis = new AxisParams(Length: len, BaseAngle: rng.Range(0.5, 0.95), BaseAzimuth: ang,
                        Droop: rng.Range(0.7, 1.4), WobbleAmplitude: 0.015, WobbleFrequency: 1.1, Segments: 5);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 3));
                    var bp = new LeafBladeParams(
                        Midrib: leafAxis, Profile: BladeProfile.Lanceolate, HalfWidth: rng.Range(0.08, 0.13),
                        Camber: rng.Range(0.05, 0.1), MidribFold: rng.Range(0.04, 0.09),
                        Asymmetry: rng.Range(-0.12, 0.12), MidribThickness: 0.003, DetailLevel: 0);
                    LeafBlade.Build(m, bp, lSeed, leafBase, leafTip);
                }
                int nFlowers = 2 + rng.NextInt(2); // 2..3 (kept off the requested 2..5 ceiling for the same reason)
                for (int f = 0; f < nFlowers; f++)
                {
                    double fang = rng.Range(0, 2 * Math.PI), fh = rng.Range(0.55, 1.05); // different heights
                    var stalkAxis = new AxisParams(Length: fh, BaseAngle: rng.Range(0.2, 0.55), BaseAzimuth: fang,
                        Droop: rng.Range(-0.05, 0.25), WobbleAmplitude: 0.012, Segments: 4);
                    ulong sSeed = Rng.Mix(seed, (ulong)(f * 613 + 71));
                    SoftTube.Build(m, new SoftTubeParams(stalkAxis, BaseRadius: 0.016, TipRadius: 0.01, Segments: 4), sSeed,
                        (i, v) => (leafBase, 1, i, v, 0, 0));
                    var top = Axis.Build(stalkAxis, sSeed)[^1].Point;
                    double stageT = (double)((seed + (ulong)f * 2) % 5) / 4.0; // guarantees a bud and a spent variant
                    FlowerHead(m, rng, Rng.Mix(seed, (ulong)(f * 97 + 5)), top, 0.12, stageT,
                        c1, c2, new[] { 0.95, 0.85, 0.35 });
                }
                break;
            }
            case "roundleaf": RoundLeaf(m, rng, seed, c1, c2); break;
            case "pairedleaf": PairedLeaf(m, rng, seed, c1, c2); break;
            case "floatleaf": FloatLeaf(m, rng, seed, c1, c2); break;
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
                // three-lobed clover leaves on stalks (cheap curved blades keep the plant in its tri budget),
                // with an occasional pompom flower head: a small cluster of individually cambered, thick
                // LeafBlade petals (kernel-built) rather than a flat star fan
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
                        double la = 2 * Math.PI * l / 3 + rng.Range(-0.14, 0.14);
                        var dir = new Vec3(Math.Cos(la), rng.Range(-0.04, 0.10), Math.Sin(la)).Normalized();
                        var side = new Vec3(-dir.Z, 0, dir.X).Normalized();
                        var tip = top + dir * lobeR;
                        int bladeStart = m.VertexCount;
                        Primitives.CurvedLeaf(m, top, tip, side, lobeR * rng.Range(0.42, 0.56),
                            Primitives.Mix(c1, c2, 0.24), Primitives.Mix(c1, c2, 0.62),
                            camber: lobeR * rng.Range(0.08, 0.16), longitudinal: 4, asymmetry: rng.Range(-0.14, 0.14));
                        MarkBladeVertices(m, bladeStart);
                    }
                    if (rng.NextDouble() < 0.15)
                    {
                        // clover pompom: a handful of tiny cambered obcordate petals, splayed into a globe
                        var fh = top + new Vec3(0, 0.05, 0);
                        int florets = 5 + rng.NextInt(3);
                        for (int fl = 0; fl < florets; fl++)
                        {
                            double fa = 2 * Math.PI * fl / florets + rng.Range(-0.2, 0.2);
                            double fPitch = rng.Range(0.3, 1.4);
                            double fSize = 0.03 * rng.Range(0.8, 1.2);
                            var petalAxis = new AxisParams(Length: fSize, BaseAngle: fPitch, BaseAzimuth: fa,
                                Droop: rng.Range(0.0, 0.3), Segments: 3);
                            var bp = new LeafBladeParams(
                                Midrib: petalAxis, Profile: BladeProfile.Obcordate, HalfWidth: fSize * 0.4,
                                Camber: rng.Range(0.16, 0.26), Cup: rng.Range(0.08, 0.16),
                                MidribThickness: fSize * 0.08, DetailLevel: 1);
                            ulong flSeed = Rng.Mix(seed, (ulong)(k * 331 + fl * 11 + 5));
                            var tmp = new MeshData();
                            LeafBlade.Build(tmp, bp, flSeed, new[] { 1.0, 1.0, 0.96 }, new[] { 0.96, 0.9, 0.7 });
                            AppendTranslated(m, tmp, fh);
                        }
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
            case "fern": Fern(m, rng, seed, c1, c2); break;
            case "vine": Vine(m, rng, c1, c2); break;
            case "mushroom_cluster": Mushrooms(m, rng, c1, c2); break;
            case "bracket": Bracket(m, rng, c1, c2); break;
            case "plasmodium": Plasmodium(m, rng, c1, c2); break;
            case "succulent": Succulent(m, rng, c1, c2); break;
            case "tussock": Tussock(m, rng, c1, c2); break;
            case "fruticose": Fruticose(m, rng, c1, c2); break;
            case "tree_locust": TreeLocust(m, rng, c1, c2); break;
            case "tree_catalpa": TreeCatalpa(m, rng, c1, c2); break;
            case "tree_tamarack": TreeTamarack(m, rng, c1, c2); break;
            case "tree_cottonwood": TreeCottonwood(m, rng, c1, c2); break;
            case "shrub_sumac": ShrubSumac(m, rng, c1, c2); break;
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

    /// <summary>Appends <paramref name="src"/> into <paramref name="dst"/>, rotating positions and normals about
    /// world Y by <paramref name="yawRad"/>, then translating the result by <paramref name="translate"/>.</summary>
    private static void AppendRotatedY(MeshData dst, MeshData src, Vec3 translate, double yawRad)
    {
        double cs = Math.Cos(yawRad), sn = Math.Sin(yawRad);
        Vec3 Rot(Vec3 v) => new Vec3(v.X * cs - v.Z * sn, v.Y, v.X * sn + v.Z * cs);
        int baseIndex = dst.VertexCount;
        for (int i = 0; i < src.VertexCount; i++)
        {
            var p = Rot(src.Position(i)) + translate;
            dst.Positions.Add((float)p.X); dst.Positions.Add((float)p.Y); dst.Positions.Add((float)p.Z);
            var n = Rot(src.NormalAt(i));
            dst.Normals.Add((float)n.X); dst.Normals.Add((float)n.Y); dst.Normals.Add((float)n.Z);
        }
        dst.Colors.AddRange(src.Colors); dst.UV.AddRange(src.UV); dst.UV2.AddRange(src.UV2);
        foreach (var i in src.Indices) dst.Indices.Add(baseIndex + i);
    }

    /// <summary>Appends <paramref name="src"/> into <paramref name="dst"/> translated by <paramref name="translate"/>
    /// with no rotation (positions and normals both left in the source's orientation).</summary>
    private static void AppendTranslated(MeshData dst, MeshData src, Vec3 translate)
    {
        int baseIndex = dst.VertexCount;
        for (int i = 0; i < src.VertexCount; i++)
        {
            var p = src.Position(i) + translate;
            dst.Positions.Add((float)p.X); dst.Positions.Add((float)p.Y); dst.Positions.Add((float)p.Z);
        }
        dst.Normals.AddRange(src.Normals); dst.Colors.AddRange(src.Colors); dst.UV.AddRange(src.UV); dst.UV2.AddRange(src.UV2);
        foreach (var i in src.Indices) dst.Indices.Add(baseIndex + i);
    }

    private static void MarkBladeVertices(MeshData mesh, int start)
    {
        for (int i = start; i < mesh.VertexCount; i++) mesh.UV2[i * 2 + 1] = 1;
    }

    private static void MarkOvateBladeVertices(MeshData mesh, int start, Vec3 centre, Vec3 axis, double length)
    {
        for (int i = start; i < mesh.VertexCount; i++)
        {
            mesh.UV[i * 2] = (float)MathD.Clamp01(0.5 + (mesh.Position(i) - centre).Dot(axis) / length);
            mesh.UV2[i * 2 + 1] = 1;
        }
    }

    /// <summary>Shallow cup of cambered, thick petals (cheap ThickOvateLeaflet blades) around a small centre disc, staged by
    /// <paramref name="stageT"/> in [0,1]: 0 is a near-closed upright bud, ~0.5 is fully open and splayed, and 1 is
    /// spent (petals drooping past horizontal, discoloured, some dropped). Built at the origin and translated to
    /// <paramref name="topPos"/> so callers can place it atop any stalk/pedicel.</summary>
    private static void FlowerHead(MeshData m, Rng rng, ulong seed, Vec3 topPos, double size, double stageT,
        double[] petalCol1, double[] petalCol2, double[] centerCol, int petalCountMax = 7)
    {
        // petalCountMax caps the upper end of the 4..7 range: each petal is a full LeafBlade (a tri-cost floor
        // around 128 tris), so callers with a tight budget (e.g. one flower head per plant) can cap the count.
        int petalCount = 4 + rng.NextInt(Math.Max(1, petalCountMax - 3)); // 4..petalCountMax, unequal size/angle below
        double t = MathD.Clamp01(stageT);
        // pitch from vertical: bud stays near-upright/closed, opens outward, then droops past horizontal when spent
        double basePitch = t <= 0.5 ? MathD.Lerp(0.22, 1.2, t / 0.5) : MathD.Lerp(1.2, 2.5, (t - 0.5) / 0.5);
        double openness = t <= 0.5 ? MathD.Lerp(0.4, 1.0, t / 0.5) : 1.0;
        int visiblePetals = t > 0.7 ? Math.Max(2, petalCount - 1 - rng.NextInt(2)) : petalCount;
        double wiltAmt = t > 0.55 ? MathD.Clamp01((t - 0.55) / 0.45) : 0.0;
        var wilt = new[] { 0.5, 0.4, 0.2 };
        var col1 = wiltAmt > 0 ? Primitives.Mix(petalCol1, wilt, wiltAmt * 0.75) : petalCol1;
        var col2 = wiltAmt > 0 ? Primitives.Mix(petalCol2, wilt, wiltAmt * 0.75) : petalCol2;

        // Petals are cheap cambered ThickOvateLeaflet blades (same double-sided crown/camber technique the
        // pairedleaf leaves use) spread around a spherical (pitch, azimuth) direction, not full LeafBlade calls
        // per petal: a full LeafBlade has a ~128-tri floor in this kernel, which would blow the plant's tri
        // budget once multiplied by several flower heads.
        var tmp = new MeshData();
        for (int i = 0; i < visiblePetals; i++)
        {
            double ang = 2 * Math.PI * i / petalCount + rng.Range(-0.18, 0.18);
            double pitch = MathD.Clamp(basePitch + rng.Range(-0.12, 0.12), 0, Math.PI);
            double petalLen = size * openness * rng.Range(0.85, 1.3); // unequal petal size
            var azimuthDir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var fwd = (Vec3.Up * Math.Cos(pitch) + azimuthDir * Math.Sin(pitch)).Normalized();
            var side = fwd.Cross(Vec3.Up);
            if (side.LengthSq < 1e-8) side = fwd.Cross(new Vec3(1, 0, 0));
            side = side.Normalized();
            var up = side.Cross(fwd).Normalized();
            var center = fwd * (petalLen * 0.55);
            ulong pSeed = Rng.Mix(seed, (ulong)(i * 331 + 13));
            ThickOvateLeaflet(tmp, center, fwd, side, up, petalLen * 1.3, petalLen * 0.55,
                petalLen * rng.Range(0.18, 0.32), pSeed, Primitives.Scale(col1, 0.9), col2);
        }
        double centerR = size * 0.22;
        var centerRim = new List<Vec3>();
        for (int s = 0; s <= 8; s++)
        {
            double th = 2 * Math.PI * s / 8;
            centerRim.Add(new Vec3(Math.Cos(th) * centerR, 0, Math.Sin(th) * centerR));
        }
        Primitives.Fan(tmp, new Vec3(0, centerR * 0.6, 0), centerRim, Vec3.Up, centerCol, Primitives.Scale(centerCol, 0.85));
        AppendTranslated(m, tmp, topPos);
    }

    /// <summary>Dense mat of cupped reniform/cordate leaflets on short arching petioles (dichondra, watercress),
    /// scattered across the full mat footprint like the old scattered-leaflet coverage. A handful of larger
    /// "hero" leaves (via the LeafBlade kernel) sit among many cheaper cupped-fan leaflets so the mat reads as
    /// dense and covers the whole footprint within the triangle budget. Youngest leaves are smaller and folded.</summary>
    private static void RoundLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int nHero = 3 + rng.NextInt(4);   // 3..6 larger kernel-built leaves for close-up detail
        int nMat = 16 + rng.NextInt(16);  // 16..31 cheap cupped leaflets filling out the mat, like the old coverage

        for (int k = 0; k < nHero; k++)
        {
            double ageT = nHero <= 1 ? 1.0 : (double)k / (nHero - 1);
            double ang = rng.Range(0, 2 * Math.PI);
            double clumpR = rng.Range(0, 0.5) * 0.9;
            var basePos = new Vec3(Math.Cos(ang) * clumpR, MathD.Lerp(-0.01, 0.03, 1 - ageT), Math.Sin(ang) * clumpR);

            double sizeScale = MathD.Lerp(0.2, 0.12, ageT) * rng.Range(0.85, 1.15);
            double petioleLen = MathD.Lerp(0.5, 0.22, ageT) * rng.Range(0.85, 1.15);
            bool folded = ageT > 0.72;

            var petioleAxis = new AxisParams(Length: petioleLen, BaseAngle: rng.Range(0.35, 0.75), BaseAzimuth: 0,
                Droop: rng.Range(0.9, 1.5), WobbleAmplitude: 0.01, WobbleFrequency: 1.3, Segments: 5);
            ulong pSeed = Rng.Mix(seed, (ulong)(k * 131 + 7));
            var petioleFrames = Axis.Build(petioleAxis, pSeed);
            var tipFrame = petioleFrames[^1];

            var tmp = new MeshData();
            SoftTube.Build(tmp, new SoftTubeParams(petioleAxis, BaseRadius: 0.007 * (sizeScale / 0.13), TipRadius: 0.004 * (sizeScale / 0.13), Segments: 4),
                pSeed, (i, v) => (Primitives.Scale(c1, 0.72), 1, i, v, 0, 0));

            double tipPitch = Math.Acos(MathD.Clamp(tipFrame.Tangent.Y, -1.0, 1.0));
            double tipAz = Math.Atan2(tipFrame.Tangent.Z, tipFrame.Tangent.X);
            var profile = rng.NextDouble() < 0.5 ? BladeProfile.Reniform : BladeProfile.Cordate;
            var bladeAxis = new AxisParams(Length: sizeScale * 0.5, BaseAngle: tipPitch, BaseAzimuth: tipAz,
                Droop: rng.Range(-0.2, 0.2), Segments: 5);
            var bladeParams = new LeafBladeParams(
                Midrib: bladeAxis, Profile: profile, HalfWidth: sizeScale,
                Camber: rng.Range(0.14, 0.22) + (folded ? 0.16 : 0),
                Cup: rng.Range(0.06, 0.14), MidribFold: folded ? 0.10 : 0.02,
                Asymmetry: rng.Range(-0.08, 0.08), MidribThickness: 0.002, DetailLevel: 1);
            var bladeTmp = new MeshData();
            LeafBlade.Build(bladeTmp, bladeParams, Rng.Mix(seed, (ulong)(k * 977 + 3)),
                Primitives.Scale(c1, 0.85), Primitives.Mix(c1, c2, rng.Range(0.1, 0.4)));
            AppendTranslated(tmp, bladeTmp, tipFrame.Point);

            double leafYaw = ang + rng.Range(-0.35, 0.35); // no two leaves share orientation
            AppendRotatedY(m, tmp, basePos, leafYaw);
        }

        for (int k = 0; k < nMat; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.88;
            var basePos = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            double stalkH = rng.Range(0.05, 0.14);
            double leafR = rng.Range(0.05, 0.11);
            bool folded = rng.NextDouble() < 0.18;
            var profile = rng.NextDouble() < 0.5 ? BladeProfile.Reniform : BladeProfile.Cordate;
            double leafYaw = ang + rng.Range(-0.5, 0.5);
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 733 + 91));

            var tmp = new MeshData();
            var top = new Vec3(0, stalkH, 0);
            Primitives.Tube(tmp, new[] { Vec3.Zero, top }, new[] { 0.008 * (leafR / 0.08), 0.005 * (leafR / 0.08) }, 4,
                (i, v) => (Primitives.Scale(c1, 0.72), 1, i, v, 0, 0));
            CuppedLeaflet(tmp, top, leafR, profile, folded, lSeed, Primitives.Mix(c1, c2, rng.Range(0.1, 0.45)), c2);
            AppendRotatedY(m, tmp, basePos, leafYaw);
        }
    }

    /// <summary>Cheap double-sided cupped/notched leaflet (a raised-centre fan): the mat-filler counterpart to the
    /// full LeafBlade kernel, used where many instances are needed within the triangle budget.</summary>
    private static void CuppedLeaflet(MeshData m, Vec3 top, double leafR, BladeProfile profile, bool folded, ulong seed,
        double[] rimCol, double[] tipCol)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.leaflet", 0);
        double notchAng = rng.Range(0, 2 * Math.PI);
        double notchDepth = profile == BladeProfile.Reniform ? 0.4 : 0.3;
        double cup = leafR * rng.Range(0.22, 0.4);
        double foldAng = rng.Range(0, 2 * Math.PI);

        var rim = new List<Vec3>();
        for (int s = 0; s <= 10; s++)
        {
            double th = 2 * Math.PI * s / 10;
            double rr = leafR * (1 - notchDepth * Math.Max(0, Math.Cos(th - notchAng)));
            double lift = -0.08 * leafR; // rim curls down slightly relative to the cupped centre
            if (folded)
            {
                double fold = Math.Max(0, Math.Cos(th - foldAng));
                lift += fold * fold * leafR * 0.9; // one half folded up against the other
                rr *= 1 - 0.25 * fold;
            }
            rim.Add(top + new Vec3(Math.Cos(th) * rr, lift, Math.Sin(th) * rr));
        }
        var apex = top + new Vec3(0, cup, 0);
        Primitives.Fan(m, apex, rim, Vec3.Up, tipCol, rimCol);
    }

    /// <summary>Cheap double-sided cupped ovate leaflet oriented by an explicit (fwd, side, up) basis — the
    /// pairedleaf counterpart to <see cref="CuppedLeaflet"/>, elongated along <paramref name="fwd"/> and cambered
    /// toward <paramref name="up"/> to read as a thick fleshy leaf rather than a flat card.</summary>
    private static void ThickOvateLeaflet(MeshData m, Vec3 center, Vec3 fwd, Vec3 side, Vec3 up,
        double length, double halfWidth, double cupAmt, ulong seed, double[] baseCol, double[] tipCol, int segs = 8)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.ovateleaflet", 0);
        double asym = rng.Range(-0.15, 0.15);
        var rim = new List<Vec3>();
        for (int s = 0; s <= segs; s++)
        {
            double th = 2 * Math.PI * s / segs;
            double envelope = Math.Pow(Math.Abs(Math.Sin(th)), 0.7);
            double along = Math.Cos(th) * length * 0.5;
            double across = Math.Sin(th) * halfWidth * envelope * (1 + asym * Math.Sign(Math.Sin(th)));
            double lift = -0.06 * halfWidth;
            rim.Add(center + fwd * along + side * across + up * lift);
        }
        var apex = center + up * cupAmt;
        Primitives.Fan(m, apex, rim, up, tipCol, baseCol);
    }

    /// <summary>Decumbent curved stems with opposite pairs of thick ovate leaves, decussate and tapering to the
    /// tip (bacopa). Occasional tiny white flower near the growing tip.</summary>
    private static void PairedLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int stems = 6 + rng.NextInt(7); // 6..12 stems, a visible sprawl rather than a few sprigs
        for (int k = 0; k < stems; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.75;
            var basePos = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            double stemLen = rng.Range(0.4, 0.75);
            int pairs = 4 + rng.NextInt(4); // 4..7 pairs, tapering toward tip

            var stemAxis = new AxisParams(Length: stemLen, BaseAngle: rng.Range(1.15, 1.45), BaseAzimuth: 0,
                Droop: rng.Range(-0.15, 0.35), PhototropicBend: rng.Range(0.1, 0.3),
                WobbleAmplitude: 0.015, WobbleFrequency: 1.6, Segments: 8);
            ulong sSeed = Rng.Mix(seed, (ulong)(k * 211 + 11));
            var stemFrames = Axis.Build(stemAxis, sSeed);

            var tmp = new MeshData();
            SoftTube.Build(tmp, new SoftTubeParams(stemAxis, BaseRadius: 0.012, TipRadius: 0.006, Segments: 6), sSeed,
                (i, v) => (Primitives.Scale(c1, 0.78), 1, i, v, 0, 0));

            for (int p = 1; p <= pairs; p++)
            {
                double t = (double)p / (pairs + 1);
                var f = Axis.Sample(stemFrames, t);
                double taper = 1.0 - 0.5 * (double)p / pairs;
                double leafSize = rng.Range(0.12, 0.18) * taper;
                double pairYaw = (p % 2 == 0) ? Math.PI / 2 : 0.0; // decussate: successive pairs rotated 90 degrees

                var rotSide = Axis.RotateAround(f.Side, f.Tangent, pairYaw);
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var fwd = rotSide * sgn;
                    var up = fwd.Cross(f.Tangent).Normalized();
                    if (up.LengthSq < 1e-8) up = Vec3.Up;
                    var center = f.Point + fwd * (leafSize * 0.65);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 4111 + p * 17 + (sgn > 0 ? 1u : 2u)));
                    int bladeStart = tmp.VertexCount;
                    double bladeLength = leafSize * 1.3;
                    ThickOvateLeaflet(tmp, center, fwd, f.Tangent, up, bladeLength, leafSize * 0.55,
                        leafSize * rng.Range(0.12, 0.22), lSeed,
                        Primitives.Scale(c1, 0.82), Primitives.Mix(c1, c2, rng.Range(0.15, 0.4)));
                    MarkOvateBladeVertices(tmp, bladeStart, center, fwd, bladeLength);
                }
            }
            if (rng.NextDouble() < 0.3)
            {
                var tipF = stemFrames[^1];
                var star = new List<Vec3>();
                for (int s = 0; s <= 8; s++) { double th = 2 * Math.PI * s / 8, rr = s % 2 == 0 ? 0.045 : 0.018; star.Add(tipF.Point + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr)); }
                Primitives.Fan(tmp, tipF.Point + new Vec3(0, 0.015, 0), star, Vec3.Up, new[] { 1.0, 1.0, 1.0 }, c2);
            }
            double leafYawWhole = rng.Range(0, 2 * Math.PI);
            AppendRotatedY(m, tmp, basePos, leafYawWhole);
        }
    }

    /// <summary>Rooted lily-pad plant: flat, notched, orbicular leaf pads lying at the water surface, each fed by
    /// a petiole that bends up from a small central rhizome and stays UNDER the pad (submerged, never visible
    /// above water). Unlike every other flora shape, this one is NOT built at unit XZ radius 1: the renderer
    /// fixes this species' horizontal scale to 1 (see FloraRenderer.Rebuild) so pad size is authored here
    /// directly in metres and stays constant regardless of plant growth radius or local water depth. Only
    /// vertical position is unit-normalised (surface at Y = 1) so the renderer's depth-driven Y scale lands
    /// everything at the true water height. Includes 1-2 young rolled scroll leaves that DO stand a little
    /// above the water, older pads with torn/yellowed margins, and 0-2 staged cup flowers held above the surface.</summary>
    private static void FloatLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var yellow = new[] { 0.62, 0.56, 0.22 };
        var youngCol = new[] { 0.62, 0.58, 0.2 };
        var underCol = Primitives.Scale(c1, 0.55); // paler, matte underside

        // Small rhizome crown the petioles emerge from (metres, on the pond floor).
        {
            var rim = new List<Vec3>();
            for (int s = 0; s <= 6; s++) { double th = 2 * Math.PI * s / 6; rim.Add(new Vec3(Math.Cos(th) * 0.02, 0, Math.Sin(th) * 0.02)); }
            Primitives.Fan(m, new Vec3(0, 0.006, 0), rim, Vec3.Up, Primitives.Scale(c1, 0.55), Primitives.Scale(c1, 0.4));
        }

        int nLeaves = 4 + rng.NextInt(9); // 4..12
        int nYoung = 1 + rng.NextInt(2);  // 1..2 rolled scroll leaves
        int nOld = rng.NextInt(3);        // 0..2 older torn/yellowed pads

        for (int k = 0; k < nLeaves; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double reach = rng.Range(0.03, 0.11); // pad/scroll centre offset from the rhizome (metres)
            bool isYoung = k < nYoung;
            bool isOld = !isYoung && k >= nLeaves - nOld;
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 7));

            if (isYoung)
            {
                // Young rolled scroll: the one leaf stage allowed above water. Its petiole rises past unit
                // Y = 1 (the surface) carrying a tight, not-yet-unfurled coil.
                double riseY = 1.0 + rng.Range(0.05, 0.14);
                var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                var path = new List<Vec3> { Vec3.Zero, dir * (reach * 0.4) + new Vec3(0, 0.55, 0),
                    dir * (reach * 0.85) + new Vec3(0, 0.92, 0), dir * reach + new Vec3(0, riseY, 0) };
                var rad = new List<double> { 0.003, 0.0026, 0.002, 0.0016 };
                Primitives.Tube(m, path, rad, 5, (i, v) => (Primitives.Scale(c1, 0.65), 1, i, v, 0, 0));
                var tipP = path[^1];
                var side = new Vec3(-Math.Sin(ang), 0, Math.Cos(ang));
                var coil = new List<Vec3>(); var crad = new List<double>();
                for (int i = 0; i <= 10; i++)
                {
                    double t = i / 10.0, th = t * Math.PI * 2.6, rr = 0.018 * (1 - t * 0.6);
                    var p = tipP + new Vec3(0, 0.008 + 0.05 * t, 0) + side * (Math.Cos(th) * rr) + new Vec3(0, Math.Sin(th) * rr * 0.4, 0);
                    coil.Add(p); crad.Add(0.0022 * (1 - t * 0.5));
                }
                int coilCount = coil.Count;
                Primitives.Tube(m, coil, crad, 4, (i, v) => (Primitives.Mix(c1, youngCol, i / (double)(coilCount - 1)), 1, i, v, 0, 0));
                continue;
            }

            // Mature/old pads lie flat at the surface (unit Y = 1); the petiole bends up from the rhizome to
            // meet the underside of the pad from below and stops a hair short of it, so no stalk shows above
            // the water for these leaves — only the pad itself does.
            double padY = 1.0;
            var padCentre = new Vec3(Math.Cos(ang) * reach, padY, Math.Sin(ang) * reach);
            const double gap = 0.012;
            var petPath = new List<Vec3> { Vec3.Zero,
                (padCentre * 0.35) with { Y = padY * 0.45 },
                (padCentre * 0.75) with { Y = padY - gap * 3 },
                padCentre with { Y = padY - gap } };
            var petRad = new List<double> { 0.0035, 0.003, 0.0024, 0.0016 };
            Primitives.Tube(m, petPath, petRad, 5, (i, v) => (Primitives.Scale(c1, 0.6), 1, i, v, 0, 0));

            double radius = rng.Range(0.015, 0.04) * (isOld ? rng.Range(0.85, 1.05) : 1.0); // 3-8 cm diameter
            var topCentreCol = isOld ? Primitives.Mix(c1, yellow, rng.Range(0.4, 0.75)) : Primitives.Scale(c1, 0.95);
            var topRimCol = isOld ? Primitives.Mix(c2, yellow, rng.Range(0.5, 0.85)) : Primitives.Mix(c1, c2, rng.Range(0.1, 0.3));
            var bottomCol = isOld ? Primitives.Mix(underCol, yellow, 0.3) : underCol;
            var tmp = new MeshData();
            OrbicularPad(tmp, lSeed, radius, thickness: 0.02, topCentreCol, topRimCol, bottomCol,
                torn: isOld, tearAmp: isOld ? rng.Range(0.06, 0.12) : 0);
            AppendRotatedY(m, tmp, padCentre, ang);
        }

        int nFlowers = rng.NextInt(3); // 0..2
        for (int f = 0; f < nFlowers; f++)
        {
            double ang = rng.Range(0, 2 * Math.PI), petAngle = rng.Range(0.04, 0.14);
            double petLen = 1.02 / Math.Cos(petAngle);
            var stalkAxis = new AxisParams(Length: petLen, BaseAngle: petAngle, BaseAzimuth: ang,
                Droop: rng.Range(-0.02, 0.03), WobbleAmplitude: 0.006, Segments: 5);
            ulong sSeed = Rng.Mix(seed, (ulong)(f * 613 + 71));
            SoftTube.Build(m, new SoftTubeParams(stalkAxis, BaseRadius: 0.004, TipRadius: 0.002, Segments: 5), sSeed,
                (i, v) => (Primitives.Scale(c1, 0.6), 1, i, v, 0, 0));
            var top = Axis.Build(stalkAxis, sSeed)[^1].Point;
            double stageT = (double)((seed + (ulong)f * 3) % 5) / 4.0; // guarantees bud/open/spent variety
            FlowerHead(m, rng, Rng.Mix(seed, (ulong)(f * 97 + 5)), top, 0.05, stageT,
                c1, c2, new[] { 0.95, 0.9, 0.55 }, petalCountMax: 6);
        }
    }

    /// <summary>Flat, roughly circular double-sided pad in the local XZ plane (Y ~ 0, width ~= length) with a
    /// V-shaped basal sinus notch cut from the rim all the way to the centre — the peltate attachment point
    /// where a petiole meets it from below — and a gently upturned rim. Top and bottom faces carry distinct
    /// colour and UV2.x (0 top / 1 bottom, matching the LeafBlade convention) so a shader can tell the leaf's
    /// topside from its pale underside; both faces' normals are exactly (0, ±1, 0). The notch/attachment point
    /// faces local -X; callers rotate the whole pad about Y to aim the notch back toward the plant's rhizome and
    /// translate it into place.</summary>
    private static void OrbicularPad(MeshData m, ulong seed, double radius, double thickness,
        double[] topCentreCol, double[] topRimCol, double[] bottomCol, bool torn, double tearAmp)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.orbicularpad", 0);
        const int seg = 20;
        const double notchHalf = 0.24; // ~14 deg either side of the -X notch centre
        double phase = rng.Range(0, 2 * Math.PI);
        double upturn = radius * 0.22;
        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1.0 : -1.0;
            var n = new Vec3(0, sign, 0);
            double faceY = sign * thickness * 0.5;
            var centreCol = face == 0 ? topCentreCol : bottomCol;
            var rimCol = face == 0 ? topRimCol : bottomCol;
            int c = m.AddVertex(new Vec3(0, faceY, 0), n, centreCol, 1, 0.5, 0.5, face, 1);
            var ids = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                double th = Math.PI + notchHalf + (2 * Math.PI - 2 * notchHalf) * s / seg;
                double tear = torn ? tearAmp * Math.Sin(th * 5 + phase) : 0;
                double rr = radius * (1 + tear);
                var p = new Vec3(Math.Cos(th) * rr, faceY + upturn, Math.Sin(th) * rr);
                ids.Add(m.AddVertex(p, n, rimCol, 1, (double)s / seg, 1, face, 1));
            }
            for (int i = 0; i < ids.Count - 1; i++) Primitives.TriangleFacing(m, c, ids[i], ids[i + 1], n);
        }
    }

    /// <summary>Prostrate branching stolons radiating irregularly from a low central crown, hugging the ground,
    /// with small rooting-node thickenings and many small cambered, thick, varied-tilt leaves at nodes along
    /// them (alternate/opposite, tapering toward the runner tips, youngest folded). Replaces the old flat,
    /// pale octagonal-disc silhouette with a believable low creeping groundcover.</summary>
    private static void Creeper(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int stolons = 3 + rng.NextInt(4); // 3..6 runners radiating from the crown
        int totalLeaves = 12 + rng.NextInt(29); // 12..40 leaves across the whole plant

        // Crown: a small low rosette bump the runners root from.
        {
            var crownRim = new List<Vec3>();
            double crownR = 0.05;
            for (int s = 0; s <= 6; s++)
            {
                double th = 2 * Math.PI * s / 6;
                crownRim.Add(new Vec3(Math.Cos(th) * crownR, 0, Math.Sin(th) * crownR));
            }
            Primitives.Fan(m, new Vec3(0, 0.02, 0), crownRim, Vec3.Up, Primitives.Scale(c1, 0.6), Primitives.Scale(c1, 0.75));
        }

        int leavesLeft = totalLeaves;
        for (int k = 0; k < stolons; k++)
        {
            int stolonsRemaining = stolons - k;
            int leavesHere = Math.Max(2, leavesLeft / stolonsRemaining);
            leavesLeft -= leavesHere;
            int pairs = Math.Max(1, leavesHere / 2);

            // Irregular radiating azimuth (not an evenly spaced star) and a long, mostly-horizontal curved path
            // spanning the same footprint radius as the old disc stems, so the plant doesn't shrink on screen.
            double baseAz = 2 * Math.PI * k / stolons + rng.Range(-0.4, 0.4);
            double runnerLen = rng.Range(0.55, 0.95);
            var stolonAxis = new AxisParams(Length: runnerLen, BaseAngle: rng.Range(1.25, 1.48), BaseAzimuth: baseAz,
                Droop: rng.Range(0.05, 0.35), WobbleAmplitude: rng.Range(0.01, 0.03), WobbleFrequency: rng.Range(0.8, 1.6),
                Segments: 4);
            ulong stSeed = Rng.Mix(seed, (ulong)(k * 251 + 17));
            var frames = Axis.Build(stolonAxis, stSeed);

            var tmp = new MeshData();
            SoftTube.Build(tmp, new SoftTubeParams(stolonAxis, BaseRadius: 0.014, TipRadius: 0.006, Segments: 6), stSeed,
                (i, v) => (Primitives.Scale(c1, 0.68), 1, i, v, 0, 0));

            for (int p = 1; p <= pairs; p++)
            {
                double t = (double)p / (pairs + 1);
                var f = Axis.Sample(frames, t);
                bool folded = t > 0.8 || rng.NextDouble() < 0.1; // youngest (tip-most) leaves fold
                double taper = MathD.Lerp(1.0, 0.4, t); // leaf size tapers toward the runner tip
                double leafSize = rng.Range(0.05, 0.09) * taper;
                double pairYaw = (p % 2 == 0) ? Math.PI / 2 : 0.0; // decussate: alternating/opposite pair spacing
                var rotSide = Axis.RotateAround(f.Side, f.Tangent, pairYaw);

                // Rooting node: a small thickening every other pair.
                if (p % 2 == 0)
                {
                    var nodeRim = new List<Vec3>();
                    double nodeR = 0.022 * taper;
                    for (int s = 0; s <= 4; s++)
                    {
                        double th = 2 * Math.PI * s / 4;
                        nodeRim.Add(f.Point + f.Side * Math.Cos(th) * nodeR + f.Tangent * Math.Sin(th) * nodeR - f.Up * nodeR * 0.2);
                    }
                    Primitives.Fan(tmp, f.Point + f.Up * (nodeR * 0.65), nodeRim, f.Up, Primitives.Scale(c1, 0.5), Primitives.Scale(c1, 0.38));
                }

                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    double lift = rng.Range(0.35, 0.85); // varied tilt off the runner so it reads as textured, not flat
                    var fwd = (rotSide * sgn + Vec3.Up * lift).Normalized();
                    var up = fwd.Cross(f.Tangent).Normalized();
                    if (up.LengthSq < 1e-8) up = Vec3.Up;
                    var center = f.Point + fwd * (leafSize * 0.6);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 4703 + p * 19 + (sgn > 0 ? 1u : 2u)));
                    int bladeStart = tmp.VertexCount;
                    double bladeLength = leafSize * (folded ? 0.8 : 1.3);
                    ThickOvateLeaflet(tmp, center, fwd, f.Tangent, up, bladeLength, leafSize * 0.55,
                        leafSize * rng.Range(0.12, 0.24), lSeed,
                        Primitives.Scale(c1, 0.8), Primitives.Mix(c1, c2, rng.Range(0.15, 0.5)), segs: 6);
                    MarkOvateBladeVertices(tmp, bladeStart, center, fwd, bladeLength);
                }
            }
            AppendTranslated(m, tmp, Vec3.Zero);
        }
    }

    /// <summary>Arching pinnate fronds with fan leaflets, plus one unrolling fiddlehead.</summary>
    private static void Fern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var stalk = new[] { 0.24, 0.2, 0.1 };
        int fronds = 7 + rng.NextInt(3);
        for (int k = 0; k < fronds; k++)
        {
            double ang = 2 * Math.PI * k / fronds + rng.Range(-0.17, 0.17);
            var rachis = new AxisParams(Length: rng.Range(1.24, 1.52), BaseAngle: rng.Range(0.27, 0.45),
                BaseAzimuth: ang, Droop: rng.Range(-1.2, -0.9), WobbleAmplitude: 0.012,
                WobbleFrequency: 1.2, Segments: 12);
            ulong rachisSeed = Rng.Mix(seed, (ulong)(k * 301 + 13));
            var frames = Axis.Build(rachis, rachisSeed);
            var frond = new MeshData();
            SoftTube.Build(frond, new SoftTubeParams(rachis, BaseRadius: 0.012, TipRadius: 0.003, Segments: 3),
                rachisSeed, (i, v) => (stalk, 1, i, v, 0, 0));
            int pairs = 6 + rng.NextInt(2);
            for (int i = 0; i < pairs; i++)
            {
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    double t = 0.19 + (i + (sgn > 0 ? 0.13 : 0.0)) * 0.105 + rng.Range(-0.01, 0.01);
                    var frame = Axis.Sample(frames, t);
                    double envelope = Math.Sin(Math.PI * (0.13 + 0.8 * t));
                    double length = rng.Range(0.24, 0.31) * envelope;
                    var pinna = new AxisParams(Length: length, BaseAngle: rng.Range(1.14, 1.34),
                        BaseAzimuth: ang + sgn * rng.Range(1.03, 1.28), Droop: rng.Range(-0.18, 0.1),
                        Segments: 6);
                    var col = Primitives.Mix(c1, c2, 0.2 + 0.6 * t);
                    var blade = new MeshData();
                    LeafBlade.Build(blade, new LeafBladeParams(Midrib: pinna, Profile: BladeProfile.Obcordate,
                        HalfWidth: length * rng.Range(0.27, 0.34), Camber: 0.18,
                        Cup: 0.09, TipCurl: 0.06, Asymmetry: rng.Range(-0.2, 0.2),
                        Margin: LeafMargin.Crenate, MarginAmplitude: 0.07, MarginFrequency: 3,
                        DetailLevel: 0), Rng.Mix(rachisSeed, (ulong)(i * 17 + (sgn > 0 ? 1u : 2u))),
                        Primitives.Scale(col, 0.84), col);
                    AppendTranslated(frond, blade, frame.Point);
                }
            }
            AppendTranslated(m, frond, new Vec3(0, 0.0045, 0));
        }
        // fiddlehead: a coiled young frond
        var coil = new List<Vec3> { new(0.1, 0.024, 0.05), new(0.1, 0.45, 0.05) };
        var cr = new List<double> { 0.015, 0.019 };
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0, th = t * Math.PI * 3.2, rr = 0.12 * (1 - t * 0.8);
            coil.Add(new Vec3(0.1 + Math.Sin(th) * rr, 0.45 + 0.35 * t + Math.Cos(th) * rr * 0.6, 0.05));
            cr.Add(0.019 * (1 - t * 0.6));
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

    /// <summary>A ground-rooted troop with buds, open bonnets and weathered, drooping caps.</summary>
    private static void Mushrooms(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stemCol = new[] { 0.78, 0.74, 0.68 };
        int n = 5 + rng.NextInt(5);
        for (int k = 0; k < n; k++)
        {
            int stage = k % 3 + 1; // every troop contains a bud, open cap and old cap
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.7;
            double h = rng.Range(0.42, 0.92) * (stage == 1 ? 0.72 : 1);
            double capR = rng.Range(0.13, 0.21) * (stage == 1 ? 0.65 : stage == 3 ? 1.18 : 1);
            var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            var lean = new Vec3(rng.Range(-0.15, 0.15), 0, rng.Range(-0.15, 0.15));
            var top = b + lean + new Vec3(0, h, 0);
            var bend = new Vec3(rng.Range(-0.035, 0.035), 0, rng.Range(-0.035, 0.035));
            Primitives.Tube(m, new[] { b + new Vec3(0, 0.035, 0), b + lean * 0.18 + new Vec3(0, h * 0.12, 0), b + lean * 0.48 + bend + new Vec3(0, h * 0.54, 0), top },
                new[] { 0.065, 0.037, 0.029, 0.025 }, 7, (i, v) => (stemCol, 1, i, v, 0, 0));
            // Each cap uses one shared radial profile, so the scalloped brim meets the underside exactly.
            int start = m.VertexCount;
            const int around = 24, rings = 5;
            double phase = rng.Range(0, 2 * Math.PI), asym = rng.Range(0.07, 0.14);
            double rise = capR * (stage == 1 ? 1.18 : stage == 2 ? 0.62 : 0.31);
            double droop = stage == 3 ? capR * 0.28 : stage == 2 ? capR * 0.08 : 0;
            Vec3 CapPoint(double f, int s)
            {
                double th = 2 * Math.PI * s / around;
                double wave = Math.Sin(th * 5 + phase) * 0.055 + Math.Sin(th * 9 - phase) * 0.024;
                double width = capR * f * (1 + asym * Math.Cos(th - phase) + wave * f);
                double y = rise * (1 - f * f) - droop * Math.Pow(f, 5) + capR * 0.055 * Math.Sin(th * 5 + phase) * Math.Pow(f, 4);
                return top + new Vec3(Math.Cos(th) * width, y, Math.Sin(th) * width);
            }
            for (int ri = 0; ri <= rings; ri++)
            {
                double t = (double)ri / rings;
                double f = 0.07 + t * 0.93;
                var col = Primitives.Mix(c2, c1, Math.Min(1, t * 1.3));
                for (int s = 0; s <= around; s++)
                {
                    var pos = CapPoint(f, s);
                    m.AddVertex(pos, Vec3.Up, col, 1, (double)s / around, t, stage, ri == rings ? 1 : 0);
                }
            }
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = start + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }
            // Underside gills: alternating radial wedges and a darker inner ring.
            int under = m.VertexCount;
            for (int row = 0; row < 2; row++)
                for (int s = 0; s <= around; s++)
                {
                    var p = CapPoint(row == 0 ? 0.16 : 1, s) + new Vec3(0, -capR * 0.035, 0);
                    var col = Primitives.Scale(c1, row == 0 ? 0.55 : (s % 2 == 0 ? 0.85 : 1.08));
                    m.AddVertex(p, -Vec3.Up, col, 1, (double)s / around, row, 0, 0);
                }
            for (int s = 0; s < around; s++)
            {
                int a = under + s, c = a + around + 1;
                Primitives.TriangleFacing(m, a, c, a + 1, -Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, c, c + 1, -Vec3.Up);
            }
        }
    }

    /// <summary>Tiers of leathery bracket shelves with concentric colour bands (turkey tail), sticking out along +X.</summary>
    private static void Bracket(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var darkBrown = Primitives.Scale(c1, 0.55);
        var warmBrown = c1;
        var greyBrown = Primitives.Mix(c1, new[] { 0.32, 0.35, 0.40 }, 0.65);
        var tanBuff = Primitives.Mix(c1, c2, 0.55);
        var lightCream = c2;
        var paleWhite = Primitives.Scale(c2, 1.12);
        double[][] bands = [darkBrown, tanBuff, greyBrown, warmBrown, tanBuff, darkBrown, lightCream, paleWhite];

        var poreBase = Primitives.Mix(c2, new[] { 0.92, 0.88, 0.80 }, 0.25);
        var poreCenter = Primitives.Scale(poreBase, 0.85);

        int tiers = 4 + rng.NextInt(3);
        const int rings = 7, around = 20;

        for (int k = 0; k < tiers; k++)
        {
            double tTier = tiers > 1 ? (double)k / (tiers - 1) : 0.5;
            double y = 0.14 + tTier * 0.66 + rng.Range(-0.02, 0.02);
            double reach = rng.Range(0.65, 0.92) * (0.85 + 0.15 * Math.Sin(Math.PI * (k + 0.5) / tiers));
            double spread = reach * rng.Range(0.9, 1.2);
            double off = (k % 2 == 0 ? -1.0 : 1.0) * rng.Range(0.04, 0.20) + rng.Range(-0.04, 0.04);
            double tilt = rng.Range(-0.035, 0.035);
            double cup = rng.Range(0.02, 0.05);
            double thickness = rng.Range(0.013, 0.018);

            double phaseRuffle = rng.Range(0, 2 * Math.PI);
            double phaseCren = rng.Range(0, 2 * Math.PI);
            ulong edgeSeed = Rng.Mix((ulong)(k + 1) * 0x9E3779B97F4A7C15UL, (ulong)Math.Round(reach * 10000));

            int topStart = m.VertexCount;

            // 1. Top shelf surface with ruffled crenulated margins and concentric color bands
            for (int ri = 0; ri <= rings; ri++)
            {
                double u = (double)ri / rings;
                var baseBand = bands[ri % bands.Length];
                for (int s = 0; s <= around; s++)
                {
                    double angle = (double)s / around * Math.PI;
                    double sinA = Math.Sin(angle);
                    double cosA = -Math.Cos(angle);

                    double edgeFactor = Math.Pow(u, 1.8) * sinA;
                    double cren = (0.065 * Math.Sin(8.0 * angle + phaseCren) + 0.035 * Math.Cos(14.0 * angle - phaseCren)) * edgeFactor;
                    double wav = 1.0 + cren;
                    double wavZ = 1.0 + cren * 0.7;

                    double x = reach * u * sinA * wav;
                    if (s == 0 || s == around || ri == 0) x = 0.0;

                    double currentSpread = spread * (0.65 + 0.35 * u);
                    double z = off + currentSpread * cosA * wavZ;

                    double cupDroop = cup * u * u - 0.04 * u * Math.Pow(sinA, 1.5) + tilt * cosA * u;
                    double ruffleWave = 0.040 * Math.Sin(9.0 * angle + phaseRuffle) + 0.022 * Math.Sin(17.0 * angle - phaseRuffle);
                    double ruffleNoise = 0.018 * Noise.Gradient(Rng.Mix(edgeSeed, 101), Math.Cos(angle) * 3.0, Math.Sin(angle) * 3.0);
                    double ruffle = (ruffleWave + ruffleNoise) * edgeFactor;

                    double yTop = y + cupDroop + ruffle;
                    var posTop = new Vec3(x, yTop, z);

                    double bandNoise = Noise.Gradient(edgeSeed, Math.Cos(angle) * 2.2, Math.Sin(angle) * 2.2);
                    var col = Primitives.Mix(baseBand, bands[(ri + 1) % bands.Length], MathD.Clamp01(0.15 + 0.15 * bandNoise));

                    m.AddVertex(posTop, Vec3.Up, col, 1, u, (double)s / around, 0, ri == rings ? 1 : 0);
                }
            }

            int underStart = m.VertexCount;

            // 2. Shelf underside (pore surface) following top shelf contour with thickness (no single-vertex central Fan)
            for (int ri = 0; ri <= rings; ri++)
            {
                double u = (double)ri / rings;
                var underCol = Primitives.Mix(poreCenter, poreBase, u);
                double thk = thickness * (1.0 - 0.35 * u);

                for (int s = 0; s <= around; s++)
                {
                    double angle = (double)s / around * Math.PI;
                    double sinA = Math.Sin(angle);
                    double cosA = -Math.Cos(angle);

                    double edgeFactor = Math.Pow(u, 1.8) * sinA;
                    double cren = (0.065 * Math.Sin(8.0 * angle + phaseCren) + 0.035 * Math.Cos(14.0 * angle - phaseCren)) * edgeFactor;
                    double wav = 1.0 + cren;
                    double wavZ = 1.0 + cren * 0.7;

                    double x = reach * u * sinA * wav;
                    if (s == 0 || s == around || ri == 0) x = 0.0;

                    double currentSpread = spread * (0.65 + 0.35 * u);
                    double z = off + currentSpread * cosA * wavZ;

                    double cupDroop = cup * u * u - 0.04 * u * Math.Pow(sinA, 1.5) + tilt * cosA * u;
                    double ruffleWave = 0.040 * Math.Sin(9.0 * angle + phaseRuffle) + 0.022 * Math.Sin(17.0 * angle - phaseRuffle);
                    double ruffleNoise = 0.018 * Noise.Gradient(Rng.Mix(edgeSeed, 101), Math.Cos(angle) * 3.0, Math.Sin(angle) * 3.0);
                    double ruffle = (ruffleWave + ruffleNoise) * edgeFactor;

                    double yTop = y + cupDroop + ruffle;
                    var posBottom = new Vec3(x, yTop - thk, z);

                    m.AddVertex(posBottom, -Vec3.Up, underCol, 1, u, (double)s / around, 1, 0);
                }
            }

            // Top surface triangulation
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = topStart + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }

            // Underside triangulation
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = underStart + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, -Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, -Vec3.Up);
                }
        }
        m.RecomputeNormals();
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
            double ang = rng.Range(0, 2 * Math.PI), r0 = Math.Sqrt(rng.NextDouble()) * 0.22;
            double spread = rng.Range(0.2, 0.94), h = rng.Range(0.46, 1.0) * (1.12 - spread * 0.24);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)); var side = new Vec3(-dir.Z, 0, dir.X);
            var b = dir * r0;
            var mid = b + dir * (spread * rng.Range(0.18, 0.42)) + side * rng.Range(-0.08, 0.08) + new Vec3(0, h * rng.Range(0.8, 1.05), 0);
            var tip = b + dir * spread + side * rng.Range(-0.16, 0.16) + new Vec3(0, h * rng.Range(0.48, 0.82), 0);
            double w = rng.Range(0.009, 0.017);
            var blue = Primitives.Mix(c1, new[] { 0.58, 0.7, 0.72 }, rng.Range(0.08, 0.38));
            var tipCol = rng.NextDouble() < 0.3 ? Primitives.Mix(c1, c2, rng.Range(0.72, 1.0)) : blue;
            Blade(m, b, mid, tip, side, w, Primitives.Scale(c1, rng.Range(0.72, 0.92)), tipCol);
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
                double t = (double)i / n;
                double wHere = halfWidth * (0.58 + 0.42 * Math.Sin(Math.PI * t)) * (1 - 0.94 * t);
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

    private static readonly double[] WoodyBark = { 0.30, 0.24, 0.18 };
    private static readonly double[] WoodyBarkLight = { 0.42, 0.34, 0.24 };

    private static void FoliageClump(MeshData m, Vec3 centre, Vec3 radii, Rng rng, double[] c1, double[] c2)
    {
        // Crown volume is made from overlapping real blade geometry rather than an opaque green ellipsoid.
        // At Vivarium scale this keeps nearby trees leafy while the aggregate still reads as a coherent crown.
        double horizontal = Math.Max(radii.X, radii.Z);
        int leaves = 10 + (int)Math.Round(horizontal * 18);
        for (int k = 0; k < leaves; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double rr = Math.Sqrt(rng.NextDouble()) * 0.92;
            var root = centre + new Vec3(
                Math.Cos(ang) * radii.X * rr,
                rng.Range(-0.78, 0.78) * radii.Y,
                Math.Sin(ang) * radii.Z * rr);
            var dir = new Vec3(
                Math.Cos(ang) * rng.Range(0.65, 1.0),
                rng.Range(-0.2, 0.35),
                Math.Sin(ang) * rng.Range(0.65, 1.0)).Normalized();
            var side = dir.Cross(Vec3.Up);
            if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
            double len = horizontal * rng.Range(0.42, 0.7);
            var col = Primitives.Mix(c1, c2, rng.Range(0.08, 0.92));
            Primitives.CurvedLeaf(m, root, root + dir * len, side, len * rng.Range(0.20, 0.31),
                Primitives.Scale(col, 0.82), col, camber: len * rng.Range(0.06, 0.12),
                longitudinal: 4, asymmetry: rng.Range(-0.16, 0.16));
        }
    }

    private static void NeedleTuft(MeshData m, Vec3 centre, Vec3 radii, Rng rng, double[] c1, double[] c2)
    {
        int needles = 11;
        for (int k = 0; k < needles; k++)
        {
            double ang = 2 * Math.PI * k / needles + rng.Range(-0.16, 0.16);
            var root = centre + new Vec3(
                Math.Cos(ang) * radii.X * rng.Range(0.1, 0.65),
                rng.Range(-0.5, 0.5) * radii.Y,
                Math.Sin(ang) * radii.Z * rng.Range(0.1, 0.65));
            var dir = new Vec3(Math.Cos(ang), rng.Range(-0.08, 0.16), Math.Sin(ang)).Normalized();
            double len = rng.Range(0.055, 0.09);
            var side = dir.Cross(Vec3.Up);
            if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
            var col = Primitives.Mix(c1, c2, rng.Range(0.15, 0.85));
            Primitives.CurvedLeaf(m, root, root + dir * len, side, rng.Range(0.004, 0.008),
                Primitives.Scale(col, 0.82), col, camber: 0.0025, longitudinal: 3,
                asymmetry: rng.Range(-0.05, 0.05));
        }
    }

    /// <summary>Open, irregular crown with many small compound-leaf masses and visible branch structure.</summary>
    private static void TreeLocust(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var trunk = new[] { new Vec3(0, 0, 0), new Vec3(0.02, 0.34, -0.01), new Vec3(-0.015, 0.68, 0.02), new Vec3(0, 0.9, 0) };
        Primitives.Tube(m, trunk, new[] { 0.11, 0.09, 0.055, 0.018 }, 8,
            (i, v) => (Primitives.Mix(WoodyBark, WoodyBarkLight, i / 3.0), 1, i, v, 2, 0));
        for (int k = 0; k < 8; k++)
        {
            double y = 0.35 + 0.055 * k + rng.Range(-0.025, 0.025);
            double ang = k * 2.399 + rng.Range(-0.25, 0.25);
            double len = rng.Range(0.42, 0.67) * (1.05 - 0.035 * k);
            var root = new Vec3(0, y, 0);
            var mid = root + new Vec3(Math.Cos(ang) * len * 0.45, len * 0.22, Math.Sin(ang) * len * 0.45);
            var tip = root + new Vec3(Math.Cos(ang) * len, len * 0.34, Math.Sin(ang) * len);
            Primitives.Tube(m, new[] { root, mid, tip }, new[] { 0.032, 0.022, 0.008 }, 6,
                (i, v) => (WoodyBarkLight, 1, i, v, 2, 0));
            FoliageClump(m, tip, new Vec3(rng.Range(0.18, 0.27), rng.Range(0.11, 0.18), rng.Range(0.16, 0.24)), rng, c1, c2);
            if (k % 2 == 0)
                FoliageClump(m, (mid + tip) * 0.5 + new Vec3(0, 0.05, 0),
                    new Vec3(0.18, 0.11, 0.16), rng, c1, c2);
        }
    }

    /// <summary>Low-forking broad crown; oversized leaves make the miniature tree read as catalpa at a glance.</summary>
    private static void TreeCatalpa(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var trunk = new[] { new Vec3(0, 0, 0), new Vec3(0.01, 0.30, 0), new Vec3(-0.02, 0.54, 0.01), new Vec3(0, 0.68, 0) };
        Primitives.Tube(m, trunk, new[] { 0.14, 0.12, 0.085, 0.035 }, 9,
            (i, v) => (Primitives.Mix(WoodyBark, WoodyBarkLight, i / 3.0), 1, i, v, 2, 0));
        for (int k = 0; k < 6; k++)
        {
            double ang = 2 * Math.PI * k / 6 + rng.Range(-0.22, 0.22);
            double len = rng.Range(0.5, 0.78);
            var root = new Vec3(0, rng.Range(0.43, 0.62), 0);
            var tip = root + new Vec3(Math.Cos(ang) * len, rng.Range(0.18, 0.34), Math.Sin(ang) * len);
            Primitives.Tube(m, new[] { root, (root + tip) * 0.5 + new Vec3(0, 0.07, 0), tip },
                new[] { 0.045, 0.027, 0.009 }, 6, (i, v) => (WoodyBarkLight, 1, i, v, 2, 0));
            FoliageClump(m, tip, new Vec3(0.34, 0.19, 0.31), rng, c1, c2);
            for (int j = 0; j < 3; j++)
            {
                double la = ang + rng.Range(-0.65, 0.65);
                var dir = new Vec3(Math.Cos(la), rng.Range(-0.05, 0.18), Math.Sin(la)).Normalized();
                var side = dir.Cross(Vec3.Up);
                if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
                Primitives.CurvedLeaf(m, tip, tip + dir * rng.Range(0.22, 0.32), side, rng.Range(0.12, 0.18),
                    Primitives.Scale(c1, 0.82), c2, camber: 0.035, longitudinal: 5, asymmetry: rng.Range(-0.12, 0.12));
            }
        }
        FoliageClump(m, new Vec3(0, 0.88, 0), new Vec3(0.42, 0.2, 0.38), rng, c1, c2);
    }

    /// <summary>Feathery deciduous conifer: straight bole with layered whorls and soft needle masses.</summary>
    private static void TreeTamarack(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        Primitives.Tube(m, new[] { new Vec3(0, 0, 0), new Vec3(0.01, 0.52, 0), new Vec3(-0.01, 1.0, 0) },
            new[] { 0.09, 0.055, 0.008 }, 8, (i, v) => (Primitives.Mix(WoodyBark, WoodyBarkLight, i / 2.0), 1, i, v, 2, 0));
        for (int ring = 0; ring < 6; ring++)
        {
            double y = 0.24 + ring * 0.115;
            double len = 0.72 - ring * 0.085;
            int branches = ring < 3 ? 7 : 6;
            for (int k = 0; k < branches; k++)
            {
                double ang = 2 * Math.PI * k / branches + ring * 0.43 + rng.Range(-0.1, 0.1);
                var root = new Vec3(0, y, 0);
                var tip = root + new Vec3(Math.Cos(ang) * len, rng.Range(-0.015, 0.055), Math.Sin(ang) * len);
                Primitives.Tube(m, new[] { root, tip }, new[] { 0.018, 0.004 }, 6,
                    (i, v) => (WoodyBarkLight, 1, i, v, 2, 0));
                for (int j = 1; j <= 3; j++)
                {
                    double t = j / 3.0;
                    var p = root + (tip - root) * t + new Vec3(0, rng.Range(0.0, 0.035), 0);
                    NeedleTuft(m, p, new Vec3(0.15 * (1 - 0.12 * j), 0.055, 0.095), rng, c1, c2);
                }
            }
        }
        NeedleTuft(m, new Vec3(0, 0.94, 0), new Vec3(0.17, 0.10, 0.17), rng, c1, c2);
    }

    /// <summary>Tall fast pioneer with an ascending scaffold and an irregular, airy oval crown.</summary>
    private static void TreeCottonwood(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        Primitives.Tube(m, new[] { new Vec3(0, 0, 0), new Vec3(-0.015, 0.38, 0.01), new Vec3(0.018, 0.72, -0.01), new Vec3(0, 0.98, 0) },
            new[] { 0.12, 0.095, 0.052, 0.012 }, 9,
            (i, v) => (Primitives.Mix(WoodyBark, WoodyBarkLight, i / 3.0), 1, i, v, 2, 0));
        for (int k = 0; k < 9; k++)
        {
            double ang = k * 2.18 + rng.Range(-0.22, 0.22);
            double y = rng.Range(0.36, 0.72);
            double len = rng.Range(0.42, 0.72);
            var root = new Vec3(0, y, 0);
            var tip = root + new Vec3(Math.Cos(ang) * len, rng.Range(0.2, 0.42), Math.Sin(ang) * len);
            Primitives.Tube(m, new[] { root, (root + tip) * 0.52 + new Vec3(0, 0.08, 0), tip },
                new[] { 0.038, 0.021, 0.006 }, 6, (i, v) => (WoodyBarkLight, 1, i, v, 2, 0));
            FoliageClump(m, tip, new Vec3(rng.Range(0.24, 0.34), rng.Range(0.16, 0.24), rng.Range(0.22, 0.31)), rng, c1, c2);
        }
        FoliageClump(m, new Vec3(0.02, 0.91, 0), new Vec3(0.34, 0.22, 0.31), rng, c1, c2);
    }

    /// <summary>Staghorn sumac clump: several crooked stems, pinnate sprays and a few upright crimson fruit cones.</summary>
    private static void ShrubSumac(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var fruit = new[] { 0.52, 0.08, 0.07 };
        int stems = 5 + rng.NextInt(3);
        for (int k = 0; k < stems; k++)
        {
            double ang = 2 * Math.PI * k / stems + rng.Range(-0.28, 0.28);
            double spread = rng.Range(0.12, 0.34);
            var root = new Vec3(Math.Cos(ang) * spread * 0.25, 0, Math.Sin(ang) * spread * 0.25);
            var top = new Vec3(Math.Cos(ang) * spread, rng.Range(0.68, 0.96), Math.Sin(ang) * spread);
            Primitives.Tube(m, new[] { root, (root + top) * 0.52 + new Vec3(rng.Range(-0.04, 0.04), 0.04, rng.Range(-0.04, 0.04)), top },
                new[] { 0.035, 0.025, 0.012 }, 6, (i, v) => (Primitives.Mix(WoodyBark, WoodyBarkLight, 0.45), 1, i, v, 2, 0));
            for (int pair = 0; pair < 5; pair++)
            {
                double t = 0.38 + pair * 0.105;
                var p = root + (top - root) * t;
                double pa = ang + Math.PI / 2 + (pair % 2 == 0 ? 0.35 : -0.35);
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var dir = new Vec3(Math.Cos(pa) * sgn, rng.Range(0.04, 0.2), Math.Sin(pa) * sgn).Normalized();
                    Primitives.CurvedLeaf(m, p, p + dir * rng.Range(0.18, 0.27), dir.Cross(Vec3.Up), rng.Range(0.045, 0.07),
                        Primitives.Scale(c1, 0.82), c2, camber: 0.018, longitudinal: 4, asymmetry: rng.Range(-0.08, 0.08));
                }
            }
            if (k % 2 == 0)
            {
                var fc = top + new Vec3(0, 0.055, 0);
                Primitives.Ellipsoid(m, fc, new Vec3(0.055, 0.11, 0.055), 5, 8,
                    (u, v) => (Primitives.Mix(fruit, new[] { 0.70, 0.13, 0.08 }, u), 1, u, v, 0, 0));
            }
        }
    }

    // ------------------------------------------------------------------ fauna

    private static readonly double[] White = { 1, 1, 1 };

    private static void BodySegment(MeshData m, Vec3 centre, Vec3 radii, int rings, int segments,
        Func<double, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr, double pitch = 0)
    {
        int start = m.VertexCount;
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;
            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                var local = new Vec3(Math.Cos(phi) * radii.X, Math.Sin(phi) * Math.Cos(th) * radii.Y, Math.Sin(phi) * Math.Sin(th) * radii.Z);
                var n = new Vec3(Math.Cos(phi) / radii.X, Math.Sin(phi) * Math.Cos(th) / radii.Y, Math.Sin(phi) * Math.Sin(th) / radii.Z).Normalized();
                var rp = new Vec3(local.X * cp - local.Y * sp, local.X * sp + local.Y * cp, local.Z);
                var rn = new Vec3(n.X * cp - n.Y * sp, n.X * sp + n.Y * cp, n.Z);
                var a = attr((double)r / rings, (double)s / segments);
                m.AddVertex(centre + rp, rn, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * (segments + 1) + s, b = a + segments + 1;
                if (r > 0) m.AddTriangle(a, a + 1, b);
                if (r < rings - 1) m.AddTriangle(a + 1, b + 1, b);
            }
    }

    public static MeshData Fauna(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var m = new MeshData();
        var model = !string.IsNullOrEmpty(sp.Model) ? sp.Model : sp.Id;
        switch (model)
        {
            case "springtail": Springtail(m); break;
            case "shrimp": Shrimp(m); break;
            case "triops": Triops(m); break;
            case "minnow": Minnow(m); break;
            case "isopod":
            case "pill_bug":
                Isopod(m);
                break;
            case "beetle": Beetle(m); break;
            case "silverfish": Silverfish(m); break;
            default: Primitives.Ellipsoid(m, new Vec3(0, 0.2, 0), new Vec3(0.5, 0.2, 0.2), 8, 10, (a, b) => (Body, 0, a, b, 1, 0)); break;
        }
        if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
        return m;
    }

    /// <summary>Rolled-up pose for conglobating species (pill bug ball); null for species that never curl.</summary>
    public static MeshData? FaunaCurled(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var model = !string.IsNullOrEmpty(sp.Model) ? sp.Model : sp.Id;
        if (model is not ("isopod" or "pill_bug")) return null;
        var m = new MeshData();
        const double cy = 0.22, ringR = 0.175;

        // Ventral sphere: inner sphere representing the tightly curled belly
        BodySegment(m, new Vec3(0, cy, 0), new Vec3(0.185, 0.185, 0.195), 4, 7, (a, b) => Region(0.5, b, 3));

        // Exactly 7 arched overlapping armor plates (Pereonites 1 to 7) wrapping tightly over the sphere
        const int plates = 7;
        for (int k = 0; k < plates; k++)
        {
            double t = (double)k / (plates - 1);
            // Plates run across the dorsal circumference from anterior to posterior
            double ang = Math.PI * 0.72 - k * (Math.PI * 1.44 / (plates - 1));
            var c = new Vec3(Math.Cos(ang) * ringR, cy + Math.Sin(ang) * ringR, 0);
            double w = 0.22 + 0.02 * Math.Sin(Math.PI * t);

            // Arched pereonite armor plate tightly fitted over sphere
            BodySegment(m, c, new Vec3(0.075, 0.040, w), 2, 6, (a, b) => Region(0.85 - t * 0.6, b, 1), pitch: ang + Math.PI / 2);

            // Lateral epimera flanges on each plate
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var epiC = c + new Vec3(0, 0, (w - 0.01) * z);
                BodySegment(m, epiC, new Vec3(0.055, 0.018, 0.028), 2, 3, (a, b) => Region(0.85 - t * 0.6, b, 1), pitch: ang + Math.PI / 2);
            }
        }

        // Tucked cephalon at anterior end
        double headAng = Math.PI * 0.72 + 0.32;
        var headC = new Vec3(Math.Cos(headAng) * ringR, cy + Math.Sin(headAng) * ringR, 0);
        BodySegment(m, headC, new Vec3(0.055, 0.038, 0.15), 2, 5, (a, b) => Region(0.95, b, 1), pitch: headAng + Math.PI / 2);

        // Lateral compound eyes
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var eyeC = headC + new Vec3(0, 0.008, 0.12 * z);
            BodySegment(m, eyeC, new Vec3(0.016, 0.014, 0.012), 2, 4, (a, b) => Region(0.97, b, 2));
        }

        // Tucked pleotelson at posterior end meeting cephalon
        double tailAng = Math.PI * 0.72 - 1.44 - 0.28;
        var tailC = new Vec3(Math.Cos(tailAng) * ringR, cy + Math.Sin(tailAng) * ringR, 0);
        BodySegment(m, tailC, new Vec3(0.050, 0.035, 0.11), 2, 5, (a, b) => Region(0.05, b, 1), pitch: tailAng + Math.PI / 2);

        // Tucked appendages (antennae / legs sealed in ventral seam)
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var attach = headC + new Vec3(0, -0.018, 0.04 * z);
            Appendage(m, new[] { attach, attach + new Vec3(0.015, -0.025, 0.025 * z), attach + new Vec3(0.01, -0.04, 0.04 * z) },
                new[] { 0.012, 0.008, 0.004 }, 6, 4);
        }

        if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
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
        else if (model is "isopod" or "beetle" or "pill_bug")
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
        // Entomobryid collembolan: length 1 along +X (head forward), origin at underside center.
        // Articulated arthropod anatomy:
        // 1. Globular head with clypeus/mouthparts and lateral ocellar compound eye patches
        // 2. 3 thoracic segments (pro-, meso-, metathorax)
        // 3. 6 abdominal segments (A1..A6) with smooth tapering posterior
        // 4. Ventral sternum (belly) and ventral collophore tube
        // 5. 4-segmented elbowed antennae
        // 6. 3 pairs of jointed legs (coxa, femur, tibia, tarsus)
        // 7. Folded jumping fork (furcula: manubrium, paired dentes, mucro tips) tucked underneath

        // --- 1. Globular Head & Clypeus / Mouthparts ---
        // Globular head capsule
        BodySegment(m, new Vec3(0.36, 0.165, 0), new Vec3(0.095, 0.085, 0.080), 4, 7, (a, b) => Region(0.90, b, 1), pitch: -0.22);
        // Clypeus / mouthparts (anterior-ventral projection)
        BodySegment(m, new Vec3(0.43, 0.105, 0), new Vec3(0.040, 0.032, 0.032), 3, 5, (a, b) => Region(0.96, b, 1), pitch: -0.45);

        // Lateral eye patches (clusters of ocelli)
        foreach (double z in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.38, 0.195, 0.062 * z), new Vec3(0.020, 0.016, 0.014), 2, 4, (a, b) => Region(0.98, b, 2));
        }

        // --- 2. Three Thoracic Segments ---
        // Prothorax (T1)
        BodySegment(m, new Vec3(0.24, 0.165, 0), new Vec3(0.042, 0.082, 0.085), 3, 6, (a, b) => Region(0.78, b, 1));
        // Mesothorax (T2)
        BodySegment(m, new Vec3(0.15, 0.172, 0), new Vec3(0.048, 0.090, 0.092), 3, 6, (a, b) => Region(0.70, b, 1));
        // Metathorax (T3)
        BodySegment(m, new Vec3(0.05, 0.170, 0), new Vec3(0.050, 0.090, 0.092), 3, 6, (a, b) => Region(0.62, b, 1));

        // --- 3. Six Abdominal Segments (A1..A6) ---
        // A1
        BodySegment(m, new Vec3(-0.05, 0.165, 0), new Vec3(0.048, 0.086, 0.088), 3, 6, (a, b) => Region(0.54, b, 1));
        // A2
        BodySegment(m, new Vec3(-0.14, 0.160, 0), new Vec3(0.046, 0.082, 0.082), 3, 6, (a, b) => Region(0.46, b, 1));
        // A3
        BodySegment(m, new Vec3(-0.22, 0.152, 0), new Vec3(0.044, 0.076, 0.075), 3, 6, (a, b) => Region(0.38, b, 1));
        // A4
        BodySegment(m, new Vec3(-0.29, 0.142, 0), new Vec3(0.042, 0.068, 0.065), 3, 6, (a, b) => Region(0.30, b, 1));
        // A5
        BodySegment(m, new Vec3(-0.36, 0.130, 0), new Vec3(0.038, 0.058, 0.052), 3, 6, (a, b) => Region(0.22, b, 1));
        // A6 (tapering terminal segment)
        BodySegment(m, new Vec3(-0.42, 0.115, 0), new Vec3(0.032, 0.042, 0.038), 3, 6, (a, b) => Region(0.14, b, 1));

        // --- 4. Ventral Sternum (Belly) and Collophore Tube ---
        // Ventral belly floor connecting sternites (Region 3)
        BodySegment(m, new Vec3(-0.06, 0.082, 0), new Vec3(0.36, 0.030, 0.062), 3, 6, (a, b) => Region(0.50, b, 3));
        // Ventral tube (collophore) projecting downward from A1 sternite
        BodySegment(m, new Vec3(-0.03, 0.045, 0), new Vec3(0.022, 0.028, 0.018), 3, 5, (a, b) => Region(0.50, b, 3));

        // --- 5. Appendages: Antennae and Legs ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            // 4-segmented elbowed antennae:
            // Ant I (scape) -> Ant II (pedicel) -> elbow bend -> Ant III -> Ant IV (flagellum tip)
            var antSocket = new Vec3(0.42, 0.18, 0.045 * z);
            var ant1 = antSocket + new Vec3(0.07, 0.025, 0.040 * z);
            var ant2 = ant1 + new Vec3(0.08, 0.010, 0.060 * z); // elbow
            var ant3 = ant2 + new Vec3(0.09, -0.018, 0.035 * z);
            var ant4 = ant3 + new Vec3(0.09, -0.035, 0.015 * z); // tip
            Appendage(m, new[] { antSocket, ant1, ant2, ant3, ant4 }, new[] { 0.016, 0.013, 0.010, 0.007, 0.004 }, 6, 4);

            // 3 pairs of jointed legs (coxa / femur / tibia / tarsus) under T1, T2, T3
            for (int leg = 0; leg < 3; leg++)
            {
                double legX = 0.24 - leg * 0.095;
                double sweep = (leg - 1) * -0.018;
                var pCoxa = new Vec3(legX, 0.088, 0.052 * z);
                var pFemur = new Vec3(legX + sweep * 0.3, 0.078, 0.082 * z);
                var pTibia = new Vec3(legX + sweep * 0.7, 0.055, 0.120 * z);
                var pTarsus = new Vec3(legX + sweep * 1.1, 0.025, 0.145 * z);
                var pClaw = new Vec3(legX + sweep * 1.5, 0.000, 0.160 * z);
                Appendage(m, new[] { pCoxa, pFemur, pTibia, pTarsus, pClaw }, new[] { 0.015, 0.013, 0.010, 0.007, 0.004 }, 6, 4);
            }
        }

        // --- 6. Folded Jumping Fork (Furcula: Manubrium, Paired Dentes, Mucro Tips) ---
        // Basal piece (manubrium) attached at ventral A4/A5 and extending forward along midline
        var manuAttach = new Vec3(-0.31, 0.068, 0);
        var manuMid = new Vec3(-0.24, 0.050, 0);
        var manuFork = new Vec3(-0.17, 0.038, 0);
        Appendage(m, new[] { manuAttach, manuMid, manuFork }, new[] { 0.016, 0.013, 0.010 }, 6, 4);

        // Paired dentes with hooked mucro tips extending forward under belly
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var densAttach = new Vec3(-0.17, 0.038, 0.008 * z);
            var densMid = new Vec3(-0.10, 0.035, 0.016 * z);
            var densApex = new Vec3(-0.04, 0.032, 0.016 * z);
            var mucroTip = new Vec3(-0.015, 0.038, 0.010 * z); // hooked tip
            Appendage(m, new[] { densAttach, densMid, densApex, mucroTip }, new[] { 0.009, 0.007, 0.005, 0.003 }, 6, 4);
        }
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
        // Oniscidean isopod (woodlouse / pill bug): length 1 along +X, origin at underside center.
        // Articulated arthropod anatomy:
        // 1. Distinct cephalon with lateral compound eyes
        // 2. Elbowed antennae (antenna 2)
        // 3. 7 overlapping arched pereonite plates with articulated lateral epimera flanges
        // 4. 5 pleonites + shield-like pleotelson
        // 5. Ventral belly (sternites)
        // 6. 7 pairs of jointed walking pereopods
        // 7. Sensory uropods at rear

        // --- 1. Distinct Cephalon & Lateral Compound Eyes ---
        // Cephalon: broad, rounded anterior shield
        BodySegment(m, new Vec3(0.36, 0.075, 0), new Vec3(0.065, 0.055, 0.150), 3, 6, (a, b) => Region(0.92, b, 1));

        // Lateral compound eyes
        foreach (double z in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.37, 0.095, 0.125 * z), new Vec3(0.018, 0.016, 0.014), 2, 4, (a, b) => Region(0.97, b, 2));
        }

        // --- 2. Elbowed Antennae ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var ant0 = new Vec3(0.40, 0.065, 0.055 * z); // socket
            var ant1 = new Vec3(0.47, 0.082, 0.110 * z); // pedicel
            var ant2 = new Vec3(0.55, 0.060, 0.170 * z); // elbow
            var ant3 = new Vec3(0.62, 0.020, 0.200 * z); // flagellum tip
            Appendage(m, new[] { ant0, ant1, ant2, ant3 }, new[] { 0.015, 0.012, 0.009, 0.005 }, 6, 4);
        }

        // --- 3. Seven Overlapping Arched Pereonite Plates with Lateral Epimera Flanges ---
        for (int s = 0; s < 7; s++)
        {
            double t = s / 6.0;
            double x = 0.26 - s * 0.078;
            double dome = Math.Sin(Math.PI * (0.15 + t * 0.70));
            double w = 0.22 + 0.04 * dome;
            double h = 0.085 + 0.065 * dome;

            // Arched central tergite plate (tilted with pitch so trailing edge overlaps next plate)
            BodySegment(m, new Vec3(x, 0.065, 0), new Vec3(0.048, h, w), 2, 6, (a, b) => Region(0.85 - t * 0.55, b, 1), pitch: -0.16);

            // Articulated lateral epimera flanges on left and right
            double sweep = (s >= 3 ? (s - 2) * 0.012 : 0);
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var epiC = new Vec3(x - sweep * 0.5, 0.038 + 0.015 * dome, (w + 0.015) * z);
                var epiR = new Vec3(0.040 + sweep * 0.5, 0.016, 0.030);
                BodySegment(m, epiC, epiR, 2, 3, (a, b) => Region(0.85 - t * 0.55, b, 1), pitch: -0.14);
            }
        }

        // --- 4. Five Pleonites and Shield-like Pleotelson ---
        for (int s = 0; s < 5; s++)
        {
            double t = s / 4.0;
            double px = -0.26 - s * 0.028;
            double pw = 0.17 - s * 0.018;
            double ph = 0.070 - s * 0.010;
            BodySegment(m, new Vec3(px, 0.062, 0), new Vec3(0.020, ph, pw), 2, 4, (a, b) => Region(0.26 - t * 0.15, b, 1), pitch: -0.20);
        }

        // Shield-like pleotelson
        BodySegment(m, new Vec3(-0.42, 0.050, 0), new Vec3(0.042, 0.028, 0.065), 2, 5, (a, b) => Region(0.05, b, 1), pitch: -0.22);

        // --- 5. Ventral Belly (Sternites) ---
        BodySegment(m, new Vec3(0.0, 0.042, 0), new Vec3(0.42, 0.030, 0.18), 2, 6, (a, b) => Region(0.50, b, 3));

        // --- 6. Seven Pairs of Jointed Walking Pereopods ---
        for (int s = 0; s < 7; s++)
        {
            double legX = 0.26 - s * 0.078;
            double sweep = (s - 3) * -0.012;
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var p0 = new Vec3(legX, 0.042, 0.13 * z);
                var p1 = new Vec3(legX + sweep * 0.5, 0.048, 0.19 * z);
                var p2 = new Vec3(legX + sweep, 0.024, 0.25 * z);
                var p3 = new Vec3(legX + sweep * 1.4, 0.000, 0.28 * z);
                Appendage(m, new[] { p0, p1, p2, p3 }, new[] { 0.014, 0.011, 0.008, 0.005 }, 6, 4);
            }
        }

        // --- 7. Sensory Uropods at Rear ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var uro0 = new Vec3(-0.41, 0.042, 0.045 * z);
            var uro1 = new Vec3(-0.46, 0.034, 0.075 * z);
            var uro2 = new Vec3(-0.51, 0.024, 0.095 * z);
            Appendage(m, new[] { uro0, uro1, uro2 }, new[] { 0.012, 0.008, 0.004 }, 6, 4);
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
