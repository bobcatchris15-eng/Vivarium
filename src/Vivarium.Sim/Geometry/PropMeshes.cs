using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Seed-driven procedural prop meshes. Any seed yields a distinct, deterministic variant.</summary>
public static class PropMeshes
{
    private static readonly double[][] RockPalette =
    {
        new[] { 0.58, 0.56, 0.54 }, // warm grey granite
        new[] { 0.46, 0.50, 0.56 }, // blue slate
        new[] { 0.66, 0.50, 0.38 }, // sandstone
        new[] { 0.52, 0.52, 0.47 }, // lichen-grey basalt
        new[] { 0.70, 0.66, 0.60 }, // pale limestone
        new[] { 0.55, 0.42, 0.40 }, // red jasper-ish
    };

    /// <summary>Unit rock (bounding radius ≈ 1, flattened base). Scale by the rock's half-extents when drawing.</summary>
    public static MeshData Rock(ulong variantSeed, int detail = 3)
    {
        var (verts, tris) = Primitives.Icosphere(detail);
        ulong ns = Rng.Mix(variantSeed, 0x70C4);
        var rng = Rng.Keyed(variantSeed, "rock.mesh", 0);
        var basePal = RockPalette[rng.NextInt(RockPalette.Length)];
        double roughness = rng.Range(0.18, 0.34);
        double faceting = rng.Range(0.0, 0.6);   // blend toward sharper planar facets
        double squash = rng.Range(-0.1, 0.25);
        var facetNormals = Enumerable.Range(0, 7).Select(_ => new Vec3(rng.Range(-1, 1), rng.Range(-0.4, 1), rng.Range(-1, 1)).Normalized()).ToArray();
        var m = new MeshData();
        var disp = new double[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            double n = Noise.Fbm3(ns, v.X * 1.6, v.Y * 1.6, v.Z * 1.6, 4);
            double r = 1 + roughness * n;
            // facets: pull toward the nearest cutting plane
            double facet = 1;
            foreach (var fn in facetNormals) facet = Math.Min(facet, 0.82 / Math.Max(0.3, v.Dot(fn)));
            r = MathD.Lerp(r, Math.Min(r, facet), faceting);
            var p = v * r;
            p = new Vec3(p.X, p.Y * (1 - squash), p.Z);
            if (p.Y < -0.35) p = new Vec3(p.X * 0.97, -0.35 - (p.Y + 0.35) * 0.25, p.Z * 0.97); // flattened base
            disp[i] = n;
            double tint = 0.85 + 0.25 * n + 0.08 * Noise.Value3(Rng.Mix(ns, 9), v.X * 6, v.Y * 6, v.Z * 6);
            var col = Primitives.Scale(basePal, tint);
            // cleaner, brighter tops (sanitized realism)
            if (v.Y > 0.3) col = Primitives.Mix(col, Primitives.Scale(basePal, 1.12), 0.35);
            m.AddVertex(p, v, col, 1, v.X, v.Z, disp[i], 0);
        }
        foreach (var t in tris) m.Indices.Add(t);
        m.RecomputeNormals();
        return m;
    }

    /// <summary>Small pebble for gravel scatter (render only).</summary>
    public static MeshData Pebble(ulong seed) => Rock(seed, detail: 1);

    /// <summary>
    /// Fallen log along local +X, centred at the origin (its axis at y = 0), built at real size.
    /// Bark ridges, knots/branch stubs, broken ends, decay sag and colour all derive from the seed + decay class.
    /// </summary>
    public static MeshData Log(LogProp log) => Log(log.VariantSeed, log.Length, log.Radius, log.DecayClass);

    public static MeshData Log(ulong seed, double length, double radius, int decay)
    {
        var rng = Rng.Keyed(seed, "log.mesh", 0);
        ulong ns = Rng.Mix(seed, 0x106);
        var m = new MeshData();
        int around = 16;
        int along = Math.Max(10, (int)(length / 0.06));
        double ridgeFreq = rng.Range(7, 13), ridgeDepth = rng.Range(0.04, 0.09) * (decay >= 3 ? 0.5 : 1);
        double taper = rng.Range(0.05, 0.18), sag = decay * 0.012 * length, flatten = decay >= 2 ? 0.88 : 1;
        double[] bark = decay switch
        {
            0 => new[] { 0.40, 0.29, 0.20 },
            1 => new[] { 0.46, 0.40, 0.34 },
            2 => new[] { 0.50, 0.30, 0.17 },
            _ => new[] { 0.34, 0.22, 0.13 },
        };
        bark = Primitives.Scale(bark, rng.Range(0.9, 1.12));
        var heart = new[] { 0.78, 0.62, 0.42 };
        var moss = new[] { 0.32, 0.62, 0.20 };
        double brokenA = rng.Range(0, 1), brokenB = rng.Range(0, 1);

        Vec3 AxisPoint(double t) => new((t - 0.5) * length, -sag * Math.Sin(Math.PI * t), 0);
        double RadiusAt(double t) => radius * (1 - taper * t) * (1 + 0.05 * Math.Sin(t * 9 + seed % 7));

        int ringStart = m.VertexCount;
        for (int i = 0; i <= along; i++)
        {
            double t = (double)i / along;
            var c = AxisPoint(t);
            for (int s = 0; s <= around; s++)
            {
                double th = 2 * Math.PI * s / around;
                double ridge = Math.Sin(th * ridgeFreq + Noise.Gradient(ns, t * length * 3, th) * 2.2);
                double rr = RadiusAt(t) * (1 + ridgeDepth * ridge + 0.03 * Noise.Gradient(ns, t * 20, th * 3));
                // broken, jagged end rims
                double endNoise = 0;
                if (i == 0) endNoise = -0.03 * length * (1 + Noise.Gradient(ns, th * 3, 7)) * brokenA;
                if (i == along) endNoise = 0.03 * length * (1 + Noise.Gradient(ns, th * 3, 11)) * brokenB;
                var dir = new Vec3(0, Math.Cos(th) * flatten, Math.Sin(th));
                var p = c + dir * rr + new Vec3(endNoise, 0, 0);
                double shade = 0.85 + 0.25 * ridge;
                var col = Primitives.Scale(bark, shade);
                if (decay >= 2 && Math.Cos(th) > 0.35)
                {
                    double patch = MathD.SmoothStep(0.1, 0.5, Noise.Gradient(Rng.Mix(ns, 3), t * length * 2.5, th * 2));
                    col = Primitives.Mix(col, moss, patch * (decay >= 3 ? 0.8 : 0.5));
                }
                m.AddVertex(p, dir, col, 1, t * length, th * RadiusAt(t), decay, 0);   // uv in metres (along, around)
            }
        }
        for (int i = 0; i < along; i++)
            for (int s = 0; s < around; s++)
            {
                int a = ringStart + i * (around + 1) + s, b = a + around + 1;
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        // end caps with growth rings (heartwood colours)
        for (int end = 0; end < 2; end++)
        {
            double t = end;
            var c = AxisPoint(t);
            var nrm = new Vec3(end == 0 ? -1 : 1, 0, 0);
            int rings = 4;
            int centre = m.AddVertex(c + nrm * (0.01 * length * (end == 0 ? brokenA : brokenB)), nrm, Primitives.Scale(heart, 0.8), 1, 0, 0, decay, 1);
            int prevRing = -1;
            for (int r = 1; r <= rings; r++)
            {
                double f = (double)r / rings;
                int ringBase = m.VertexCount;
                var col = r % 2 == 0 ? Primitives.Scale(heart, 0.78) : heart;
                if (r == rings) col = Primitives.Scale(bark, 0.9);
                if (decay >= 3) col = Primitives.Scale(col, 0.75);
                for (int s = 0; s <= around; s++)
                {
                    double th = 2 * Math.PI * s / around;
                    var dir = new Vec3(0, Math.Cos(th) * flatten, Math.Sin(th));
                    m.AddVertex(c + dir * (RadiusAt(t) * f * 0.97), nrm, col, 1, f, th, decay, 1);
                }
                for (int s = 0; s < around; s++)
                {
                    if (prevRing < 0) Primitives.TriangleFacing(m, centre, ringBase + s, ringBase + s + 1, nrm);
                    else
                    {
                        Primitives.TriangleFacing(m, prevRing + s, ringBase + s, ringBase + s + 1, nrm);
                        Primitives.TriangleFacing(m, prevRing + s, ringBase + s + 1, prevRing + s + 1, nrm);
                    }
                }
                prevRing = ringBase;
            }
        }
        // branch stubs on younger logs
        int stubs = decay <= 1 ? 1 + rng.NextInt(3) : rng.NextInt(2);
        for (int k = 0; k < stubs; k++)
        {
            double t = rng.Range(0.2, 0.8), th = rng.Range(0.2, Math.PI - 0.2);
            var baseP = AxisPoint(t);
            var dir = new Vec3(rng.Range(-0.3, 0.3), Math.Cos(th) * flatten, Math.Sin(th)).Normalized();
            double sl = radius * rng.Range(0.6, 1.3), sr = radius * rng.Range(0.15, 0.25);
            var path = new List<Vec3> { baseP + dir * (RadiusAt(t) * 0.6), baseP + dir * (RadiusAt(t) + sl * 0.6), baseP + dir * (RadiusAt(t) + sl) };
            Primitives.Tube(m, path, new[] { sr, sr * 0.8, sr * 0.4 }, 8, (i, v) => (Primitives.Scale(bark, 0.95), 1, i, v, decay, 0));
        }
        return m;
    }

    public readonly record struct PebbleInstance(Vec3 Position, double Scale, double RotationY, int Variant);

    /// <summary>
    /// Render-only pebble scatter for a gravel patch (no simulation entities). Deterministic from the patch seed.
    /// </summary>
    public static List<PebbleInstance> GravelScatter(VivariumWorld w, GravelPatch g, double density = 140, int variants = 6)
    {
        var rng = Rng.Keyed(g.VariantSeed, "gravel.scatter", 0);
        int count = (int)(Math.PI * g.Radius * g.Radius * density);
        var list = new List<PebbleInstance>(count);
        for (int i = 0; i < count * 2 && list.Count < count; i++)
        {
            var p = g.Position + new Vec2(rng.Range(-g.Radius, g.Radius), rng.Range(-g.Radius, g.Radius));
            if (!g.Covers(p) || !w.Domain.ContainsDisc(p, 0.02)) continue;
            double s = rng.Range(0.012, 0.035) * (rng.Chance(0.1) ? 1.8 : 1);
            list.Add(new PebbleInstance(new Vec3(p.X, w.Terrain.Height(p) + s * 0.25, p.Z), s, rng.Range(0, 2 * Math.PI), rng.NextInt(variants)));
        }
        return list;
    }
}
