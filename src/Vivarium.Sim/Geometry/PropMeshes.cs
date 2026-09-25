using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Seed-driven procedural prop meshes. Any seed yields a distinct, deterministic variant.</summary>
public static class PropMeshes
{
    // Toned toward natural, weathered stone: low saturation greys, tans and slate, no saturated hues.
    private static readonly double[][] RockPalette =
    {
        new[] { 0.56, 0.54, 0.52 }, // warm grey granite
        new[] { 0.49, 0.51, 0.53 }, // slate grey
        new[] { 0.60, 0.53, 0.44 }, // tan sandstone
        new[] { 0.51, 0.50, 0.47 }, // umber basalt
        new[] { 0.66, 0.63, 0.58 }, // pale limestone
        new[] { 0.53, 0.46, 0.42 }, // muted umber-red
    };

    /// <summary>Unit rock (bounding radius ≈ 1, flattened base). Scale by the rock's half-extents when drawing.</summary>
    public static MeshData Rock(ulong variantSeed, int detail = 3)
    {
        var rng = Rng.Keyed(variantSeed, "rock.mesh", 0);
        var basePal = RockPalette[rng.NextInt(RockPalette.Length)];
        int family = rng.NextInt(3); // fractured block, layered slab, upright shard
        const int sides = 10;
        double phase = rng.Range(0, Math.PI * 2);
        double shearX = rng.Range(-0.12, 0.12), shearZ = rng.Range(-0.12, 0.12);
        double[] heights = family switch
        {
            0 => new[] { -0.38, -0.16, 0.22, 0.56 },
            1 => new[] { -0.38, -0.12, 0.29, 0.60 },
            _ => new[] { -0.38, -0.20, 0.34, 0.65 },
        };
        double[] radii = family switch
        {
            0 => new[] { 0.78, 1.0, 0.76, 0.38 },
            1 => new[] { 0.84, 1.0, 0.79, 0.44 },
            _ => new[] { 0.78, 0.97, 0.66, 0.23 },
        };
        var sector = new double[sides];
        var angular = new double[sides];
        for (int s = 0; s < sides; s++)
        {
            sector[s] = rng.Range(0.85, 1.07);
            angular[s] = phase + 2 * Math.PI * (s + rng.Range(-0.10, 0.10)) / sides;
        }
        var rings = new Vec3[heights.Length, sides];
        for (int r = 0; r < heights.Length; r++)
            for (int s = 0; s < sides; s++)
            {
                double y = heights[r] + rng.Range(-0.06, 0.06);
                // Whole-sector offsets make long fracture edges; slab bands step outward at each layer.
                double radius = radii[r] * sector[s] * rng.Range(0.96, 1.04);
                double a = angular[s];
                rings[r, s] = new Vec3(Math.Cos(a) * radius + y * shearX, y,
                    Math.Sin(a) * radius + y * shearZ);
            }
        var m = new MeshData();
        void Face(Vec3 a, Vec3 b, Vec3 c, Vec3 outward, double shade)
        {
            var n = (b - a).Cross(c - a).Normalized();
            if (n.Dot(outward) < 0) { (b, c) = (c, b); n = n * -1; }
            var col = Primitives.Scale(basePal, shade);
            int start = m.AddVertex(a, n, col, 1, a.X, a.Z, family, 0);
            m.AddVertex(b, n, col, 1, b.X, b.Z, family, 0);
            m.AddVertex(c, n, col, 1, c.X, c.Z, family, 0);
            m.AddTriangle(start, start + 1, start + 2);
        }
        for (int r = 0; r < heights.Length - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                var a = rings[r, s]; var b = rings[r, next];
                var c = rings[r + 1, next]; var d = rings[r + 1, s];
                var outwards = new Vec3(a.X + b.X + c.X + d.X, 0, a.Z + b.Z + c.Z);
                double shade = rng.Range(0.86, 1.10);
                Face(a, b, c, outwards, shade);
                Face(a, c, d, outwards, shade * rng.Range(0.96, 1.04));
            }
        var bottom = new Vec3(0, heights[0], 0);
        // Shared crest height is the placement and surface-query contract for every family.
        var top = new Vec3(0.75 * shearX, 0.75, 0.75 * shearZ);
        for (int s = 0; s < sides; s++)
        {
            int next = (s + 1) % sides;
            Face(bottom, rings[0, next], rings[0, s], new Vec3(0, -1, 0), 0.76);
            Face(top, rings[heights.Length - 1, s], rings[heights.Length - 1, next], new Vec3(0, 1, 0), rng.Range(0.98, 1.12));
        }
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
        int around = 20;
        int along = Math.Max(10, (int)(length / 0.06));
        double barkDepth = rng.Range(0.035, 0.075) * (decay >= 3 ? 0.55 : 1);
        double taper = rng.Range(0.05, 0.18), sag = decay * 0.012 * length, flatten = decay >= 2 ? 0.88 : 1;
        double bendY = rng.Range(-0.035, 0.035) * length, bendZ = rng.Range(-0.05, 0.05) * length;
        double phaseY = rng.Range(0, Math.PI * 2), phaseZ = rng.Range(0, Math.PI * 2);
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

        Vec3 AxisPoint(double t) => new((t - 0.5) * length,
            -sag * Math.Sin(Math.PI * t) + bendY * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.7 + phaseY),
            bendZ * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.35 + phaseZ));
        double RadiusAt(double t) => radius * (1 - taper * t) * (0.97 + 0.06 * Noise.Value3(Rng.Mix(ns, 91), t * 3.2, 0.4, 0.2));

        int ringStart = m.VertexCount;
        for (int i = 0; i <= along; i++)
        {
            double t = (double)i / along;
            var c = AxisPoint(t);
            for (int s = 0; s <= around; s++)
            {
                double th = 2 * Math.PI * s / around;
                double barkN = Noise.Gradient(ns, t * length * 4.2, th * 1.7);
                double bark2 = Noise.Gradient(Rng.Mix(ns, 29), t * length * 10.0, th * 3.1);
                double oval = 1.0 + 0.055 * Math.Cos(2 * th + phaseZ + t * 1.4);
                double ridge = barkN * 0.72 + bark2 * 0.28;
                double rr = RadiusAt(t) * oval * (1 + barkDepth * ridge);
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
            var path = new List<Vec3> { baseP + dir * (RadiusAt(t) * 0.35), baseP + dir * (RadiusAt(t) * 0.72), baseP + dir * (RadiusAt(t) + sl * 0.6), baseP + dir * (RadiusAt(t) + sl) };
            Primitives.Tube(m, path, new[] { sr * 1.65, sr * 1.25, sr * 0.78, sr * 0.35 }, 10, (i, v) => (Primitives.Scale(bark, 0.95), 1, i, v, decay, 0));
        }
        return m;
    }

    public readonly record struct PebbleInstance(Vec3 Position, Vec3 Scale, double RotationY, double TiltX, double TiltZ, int Variant);

    /// <summary>Render-only clustered gravel with voids, burial and full 3-D orientation.</summary>
    public static List<PebbleInstance> GravelScatter(VivariumWorld w, GravelPatch g, double density = 140, int variants = 16)
    {
        var rng = Rng.Keyed(g.VariantSeed, "gravel.scatter", 0);
        int count = (int)(Math.PI * g.Radius * g.Radius * density);
        var list = new List<PebbleInstance>(count);
        var clusters = new List<Vec2>();
        for (int c = 0; c < 5; c++)
            for (int tries = 0; tries < 12; tries++)
            {
                double a = rng.Range(0, Math.PI * 2), r = Math.Sqrt(rng.NextDouble()) * g.Radius * 0.82;
                var p = g.Position + Vec2.FromAngle(a) * r;
                if (g.Covers(p)) { clusters.Add(p); break; }
            }
        if (clusters.Count == 0) clusters.Add(g.Position);

        for (int i = 0; i < count * 5 && list.Count < count; i++)
        {
            Vec2 p;
            if (rng.Chance(0.76))
            {
                var c = clusters[rng.NextInt(clusters.Count)];
                double a = rng.Range(0, Math.PI * 2);
                double spread = g.Radius * (0.08 + 0.28 * Math.Pow(rng.NextDouble(), 1.8));
                p = c + Vec2.FromAngle(a) * spread;
            }
            else
            {
                double a = rng.Range(0, Math.PI * 2), r = Math.Sqrt(rng.NextDouble()) * g.Radius;
                p = g.Position + Vec2.FromAngle(a) * r;
            }
            if (!g.Covers(p) || !w.Domain.ContainsDisc(p, 0.02)) continue;
            double s = rng.Range(0.011, 0.033) * (rng.Chance(0.08) ? rng.Range(1.5, 2.2) : 1);
            double sx = s * rng.Range(0.78, 1.25), sy = s * rng.Range(0.55, 0.92), sz = s * rng.Range(0.8, 1.3);
            double burial = rng.Range(0.12, 0.42);
            var pos = new Vec3(p.X, w.Terrain.Height(p) + sy * (0.38 - burial), p.Z);
            list.Add(new PebbleInstance(pos, new Vec3(sx, sy, sz), rng.Range(0, 2 * Math.PI),
                rng.Range(-0.45, 0.45), rng.Range(-0.45, 0.45), rng.NextInt(variants)));
        }
        return list;
    }

}
