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

    private readonly record struct LogKnot(double T, double Th, double Radius, double Height, Vec3 Dir, bool HasStub);

    private static double WrapAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static double FracturedEndSplinter(ulong seed, int end, double rFrac, double th, double length, double radius, int decay)
    {
        ulong endSeed = Rng.Mix(seed, (ulong)end * 0x9E3779B97F4A7C15UL + 0x454E44UL);
        if (decay >= 3)
        {
            const double hollowRadius = 0.62;
            if (rFrac < hollowRadius)
            {
                double cavityNorm = 1.0 - (rFrac / hollowRadius);
                double rotDepth = 0.065 * length + 0.020 * length * Noise.Gradient(endSeed, Math.Cos(th) * 2.5, Math.Sin(th) * 2.5);
                return -rotDepth * Math.Sqrt(cavityNorm);
            }
            double shellFrac = (rFrac - hollowRadius) / (1.0 - hollowRadius);
            double shellSplinter = 0.018 * length * (1.0 + Noise.Gradient(Rng.Mix(endSeed, 29), Math.Cos(th) * 4.0, Math.Sin(th) * 4.0));
            return Math.Min(shellSplinter * shellFrac, 0.055 * length);
        }

        double shearPhase = th + (end == 0 ? 0.8 : 2.4);
        double stepShear = Math.Sin(shearPhase) > 0.0 ? 0.020 * length : -0.005 * length;
        double grainA = Noise.Gradient(Rng.Mix(endSeed, 11), Math.Cos(th) * 5.0, Math.Sin(th) * 5.0);
        double grainB = Noise.Gradient(Rng.Mix(endSeed, 37), rFrac * 8.0, th * 4.0);
        double fiberNoise = (0.015 * length) * (grainA * 0.65 + grainB * 0.35);

        double spurProtrusion = 0;
        for (int sp = 0; sp < 3; sp++)
        {
            ulong spKey = Rng.Mix(endSeed, (ulong)(sp * 101 + 7));
            double spTh = ((spKey % 1000) / 1000.0) * 2 * Math.PI;
            double spR = 0.25 + (((spKey / 1000) % 1000) / 1000.0) * 0.55;
            double spLen = radius * (0.28 + (((spKey / 1000000) % 1000) / 1000.0) * 0.35);
            double dTh = WrapAngle(th - spTh);
            double dR = rFrac - spR;
            double d = Math.Sqrt(dTh * dTh * spR * spR + dR * dR);
            if (d < 0.25)
            {
                double h = spLen * (1.0 - d / 0.25);
                if (h > spurProtrusion) spurProtrusion = h;
            }
        }

        double total = 0.012 * length + stepShear + fiberNoise + spurProtrusion;
        return Math.Clamp(total, 0.002 * length, 0.055 * length);
    }

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
        int around = 28;
        int along = Math.Max(12, (int)(length / 0.05));
        double barkDepth = rng.Range(0.035, 0.065) * (decay >= 3 ? 0.55 : 1);
        double taper = rng.Range(0.04, 0.14), sag = decay * 0.010 * length;
        double flatten = decay >= 3 ? 0.80 : (decay == 2 ? 0.86 : 1.0);
        double bendY = rng.Range(-0.025, 0.025) * length, bendZ = rng.Range(-0.035, 0.035) * length;
        double phaseY = rng.Range(0, Math.PI * 2), phaseZ = rng.Range(0, Math.PI * 2);

        double[] bark = decay switch
        {
            0 => new[] { 0.40, 0.29, 0.19 },
            1 => new[] { 0.46, 0.42, 0.36 },
            2 => new[] { 0.44, 0.30, 0.20 },
            _ => new[] { 0.28, 0.18, 0.11 },
        };
        bark = Primitives.Scale(bark, rng.Range(0.92, 1.08));
        var heart = decay switch
        {
            0 => new[] { 0.82, 0.70, 0.48 },
            1 => new[] { 0.70, 0.63, 0.48 },
            2 => new[] { 0.58, 0.48, 0.32 },
            _ => new[] { 0.32, 0.22, 0.14 },
        };
        var sapwood = new[] { 0.72, 0.60, 0.44 };
        var weatheredWood = new[] { 0.62, 0.54, 0.42 };
        var punkWood = new[] { 0.22, 0.14, 0.08 };
        var cavityWood = new[] { 0.10, 0.07, 0.04 };
        var moss = new[] { 0.26, 0.58, 0.18 };

        // Knots: branch junctions with swelling collars and optional broken stubs
        var knotRng = Rng.Keyed(seed, "log.knots", 0);
        int knotCount = 2 + knotRng.NextInt(3);
        var knots = new List<LogKnot>(knotCount);
        for (int k = 0; k < knotCount; k++)
        {
            double kt = knotRng.Range(0.20, 0.80);
            double kth = knotRng.Range(0, 2 * Math.PI);
            double kr = radius * knotRng.Range(0.25, 0.40);
            double kh = radius * knotRng.Range(0.18, 0.32);
            var kdir = new Vec3(knotRng.Range(-0.2, 0.2), Math.Cos(kth) * flatten, Math.Sin(kth)).Normalized();
            bool hasStub = (decay <= 1 && knotRng.Chance(0.70)) || (decay == 2 && knotRng.Chance(0.35));
            knots.Add(new LogKnot(kt, kth, kr, kh, kdir, hasStub));
        }

        Vec3 AxisPoint(double t) => new(
            (t - 0.5) * length,
            -sag * Math.Sin(Math.PI * t) + bendY * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.7 + phaseY),
            bendZ * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.35 + phaseZ));

        double RadiusAt(double t) => radius * (1.0 - taper * t) * (0.97 + 0.05 * Noise.Value3(Rng.Mix(ns, 91), t * 3.2, 0.4, 0.2));

        int ringStart = m.VertexCount;
        for (int i = 0; i <= along; i++)
        {
            double t = (double)i / along;
            var c = AxisPoint(t);
            double rBase = RadiusAt(t);

            for (int s = 0; s <= around; s++)
            {
                double th = 2 * Math.PI * s / around;

                // Knot swelling and grain deflection around knots
                double knotSwell = 0;
                double thDeflect = 0;
                for (int k = 0; k < knots.Count; k++)
                {
                    var knot = knots[k];
                    double dt = (t - knot.T) * length;
                    double dTh = WrapAngle(th - knot.Th);
                    double dyz = dTh * rBase;
                    double dist = Math.Sqrt(dt * dt + dyz * dyz);
                    double collarRadius = knot.Radius * 2.5;
                    if (dist < collarRadius)
                    {
                        double u = dist / collarRadius;
                        double w = (1.0 - u * u) * (1.0 - u);
                        knotSwell += knot.Height * w;

                        if (dist > 1e-4)
                        {
                            double angleToKnot = Math.Atan2(dyz, dt);
                            thDeflect += 0.30 * (1.0 - u) * Math.Sin(angleToKnot);
                        }
                    }
                }

                // Longitudinal bark furrowing (ridges and grooves running along length)
                double thGrain = th + thDeflect;
                const int furrowCount = 14;
                double furrowPhase = thGrain * (furrowCount / (2 * Math.PI)) + Noise.Gradient(ns, t * length * 1.2, thGrain * 0.8) * 0.40;
                double furrowDist = Math.Abs(Math.Sin(furrowPhase * Math.PI));
                double furrowProfile = Math.Pow(furrowDist, 0.55) * 1.8 - 0.9;
                double fineGrain = Noise.Gradient(Rng.Mix(ns, 47), t * length * 6.0, thGrain * 3.0) * 0.20;
                double barkRidge = (furrowProfile + fineGrain) * barkDepth;

                // Progressive decay class features
                bool isPeelingFissure = false;
                if (decay == 2)
                {
                    double fissureNoise = Noise.Gradient(Rng.Mix(ns, 113), t * length * 1.8, thGrain * 1.5);
                    if (fissureNoise > 0.12 && furrowDist < 0.42)
                    {
                        isPeelingFissure = true;
                        barkRidge -= barkDepth * 1.35 * MathD.SmoothStep(0.12, 0.40, fissureNoise);
                    }
                    else if (fissureNoise > 0.04 && furrowDist >= 0.42 && furrowDist < 0.62)
                    {
                        barkRidge += barkDepth * 0.45 * MathD.SmoothStep(0.04, 0.22, fissureNoise);
                    }
                }

                double rotTrough = 0;
                bool isPunkWood = false;
                if (decay >= 3)
                {
                    double slough = Noise.Gradient(Rng.Mix(ns, 151), t * length * 1.5, thGrain * 1.2);
                    if (slough > -0.22)
                    {
                        isPunkWood = true;
                        barkRidge = -barkDepth * 0.65;
                    }
                    if (Math.Cos(th) > 0.30)
                    {
                        double rotNoise = Noise.Gradient(Rng.Mix(ns, 199), t * length * 1.3, 0.5);
                        rotTrough = MathD.SmoothStep(0.30, 0.85, Math.Cos(th)) * (radius * 0.32) * MathD.SmoothStep(-0.2, 0.5, rotNoise);
                    }
                }

                double rr = Math.Max(radius * 0.15, rBase + knotSwell + barkRidge - rotTrough);
                var dir = new Vec3(0, Math.Cos(th) * flatten, Math.Sin(th));

                double xPos = c.X;
                if (i == 0)
                {
                    double rimSplinter = FracturedEndSplinter(seed, 0, 1.0, th, length, radius, decay);
                    xPos = -0.5 * length - rimSplinter;
                }
                else if (i == along)
                {
                    double rimSplinter = FracturedEndSplinter(seed, 1, 1.0, th, length, radius, decay);
                    xPos = 0.5 * length + rimSplinter;
                }
                var p = new Vec3(xPos, c.Y + dir.Y * rr, c.Z + dir.Z * rr);

                // Vertex shading and coloring
                double shade = 0.82 + 0.28 * furrowDist;
                var col = Primitives.Scale(bark, shade);
                if (isPeelingFissure)
                {
                    col = Primitives.Mix(sapwood, weatheredWood, 0.35);
                }
                else if (isPunkWood || rotTrough > 0)
                {
                    col = Primitives.Scale(punkWood, 0.85 + 0.25 * Noise.Gradient(ns, t * 5.0, th * 3.5));
                }

                if (decay >= 2 && Math.Cos(th) > 0.30)
                {
                    double patch = MathD.SmoothStep(0.08, 0.48, Noise.Gradient(Rng.Mix(ns, 3), t * length * 2.5, th * 2.0));
                    col = Primitives.Mix(col, moss, patch * (decay >= 3 ? 0.75 : 0.50));
                }

                m.AddVertex(p, dir, col, 1, t * length, th * rBase, decay, isPeelingFissure ? 1 : 0);
            }
        }

        // Trunk cylinder quads
        for (int i = 0; i < along; i++)
        {
            for (int s = 0; s < around; s++)
            {
                int a = ringStart + i * (around + 1) + s, b = a + around + 1;
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        // Fibrous fractured ends (jagged 3D fracture planes, growth rings, splinters, or decay hollows)
        for (int end = 0; end < 2; end++)
        {
            double endSign = end == 0 ? -1.0 : 1.0;
            var endNorm = new Vec3(endSign, 0, 0);
            var cEnd = AxisPoint(end);
            double rEnd = RadiusAt(end);
            const int rings = 3;

            int rimVertexIndex(int s) => ringStart + (end == 0 ? 0 : along) * (around + 1) + s;

            int prevRing = -1;
            for (int r = rings - 1; r >= 1; r--)
            {
                int ringBase = m.VertexCount;
                double rFrac = (double)r / rings;

                for (int s = 0; s <= around; s++)
                {
                    double th = 2 * Math.PI * s / around;
                    double splinterX = FracturedEndSplinter(seed, end, rFrac, th, length, radius, decay);
                    double rInner = rEnd * rFrac;
                    var p = cEnd + new Vec3(endSign * splinterX, Math.Cos(th) * rInner * flatten, Math.Sin(th) * rInner);

                    if (decay >= 3 && rFrac < 0.62)
                    {
                        var col = Primitives.Scale(cavityWood, 0.90);
                        var nrm = new Vec3(-endSign * 0.7, -Math.Cos(th) * 0.5, -Math.Sin(th) * 0.5).Normalized();
                        m.AddVertex(p, nrm, col, 1, rFrac, th, decay, 2);
                    }
                    else
                    {
                        var col = (r % 2 == 0) ? heart : Primitives.Scale(heart, 0.84);
                        if (decay >= 3) col = Primitives.Scale(col, 0.75);
                        m.AddVertex(p, endNorm, col, 1, rFrac, th, decay, 1);
                    }
                }

                var faceNorm = (decay >= 3 && rFrac < 0.62) ? -endNorm : endNorm;
                for (int s = 0; s < around; s++)
                {
                    int p0 = (r == rings - 1) ? rimVertexIndex(s) : (prevRing + s);
                    int p1 = (r == rings - 1) ? rimVertexIndex(s + 1) : (prevRing + s + 1);
                    int c0 = ringBase + s;
                    int c1 = ringBase + s + 1;
                    Primitives.TriangleFacing(m, p0, c0, c1, faceNorm);
                    Primitives.TriangleFacing(m, p0, c1, p1, faceNorm);
                }
                prevRing = ringBase;
            }

            // Central core vertex
            double centerSplinter = FracturedEndSplinter(seed, end, 0.0, 0.0, length, radius, decay);
            var centerPos = cEnd + new Vec3(endSign * centerSplinter, 0, 0);
            if (decay >= 3)
            {
                int centerIdx = m.AddVertex(centerPos, -endNorm, cavityWood, 1, 0, 0, decay, 2);
                for (int s = 0; s < around; s++)
                {
                    Primitives.TriangleFacing(m, centerIdx, prevRing + s, prevRing + s + 1, -endNorm);
                }
            }
            else
            {
                int centerIdx = m.AddVertex(centerPos, endNorm, Primitives.Scale(heart, 0.80), 1, 0, 0, decay, 1);
                for (int s = 0; s < around; s++)
                {
                    Primitives.TriangleFacing(m, centerIdx, prevRing + s, prevRing + s + 1, endNorm);
                }
            }

            // Protruding 3D splinter tines (fibrous wood shards extending past the fracture plane)
            int tineCount = decay >= 3 ? 2 : 4;
            for (int tIdx = 0; tIdx < tineCount; tIdx++)
            {
                ulong tSeed = Rng.Mix(seed, (ulong)end * 0x12345UL + (ulong)tIdx * 997UL);
                double tAngle = ((tSeed % 1000) / 1000.0) * 2 * Math.PI;
                double tRFrac = decay >= 3 ? (0.68 + ((tSeed / 1000 % 1000) / 1000.0) * 0.24) : (0.25 + ((tSeed / 1000 % 1000) / 1000.0) * 0.60);
                double tLen = radius * (0.16 + ((tSeed / 1000000 % 1000) / 1000.0) * 0.20);
                double tWidth = radius * 0.05;

                double spX = FracturedEndSplinter(seed, end, tRFrac, tAngle, length, radius, decay);
                var baseCenter = cEnd + new Vec3(endSign * spX, Math.Cos(tAngle) * rEnd * tRFrac * flatten, Math.Sin(tAngle) * rEnd * tRFrac);
                var tip = baseCenter + endNorm * tLen;

                var sideA = new Vec3(0, -Math.Sin(tAngle), Math.Cos(tAngle)) * tWidth;
                var sideB = new Vec3(0, Math.Cos(tAngle) * flatten, Math.Sin(tAngle)) * tWidth;
                var b0 = baseCenter + sideA;
                var b1 = baseCenter - sideA * 0.5 + sideB * 0.866;
                var b2 = baseCenter - sideA * 0.5 - sideB * 0.866;

                var tineCol = Primitives.Scale(heart, 1.05);
                int vb0 = m.AddVertex(b0, endNorm, tineCol, 1, 0, 0, decay, 1);
                int vb1 = m.AddVertex(b1, endNorm, tineCol, 1, 0, 0, decay, 1);
                int vb2 = m.AddVertex(b2, endNorm, tineCol, 1, 0, 0, decay, 1);
                int vTip = m.AddVertex(tip, endNorm, Primitives.Scale(tineCol, 1.15), 1, 0, 0, decay, 1);

                Primitives.TriangleFacing(m, vb0, vb1, vTip, (b0 + b1 + tip) / 3 - baseCenter);
                Primitives.TriangleFacing(m, vb1, vb2, vTip, (b1 + b2 + tip) / 3 - baseCenter);
                Primitives.TriangleFacing(m, vb2, vb0, vTip, (b2 + b0 + tip) / 3 - baseCenter);
            }
        }

        // Branch stubs and knot collars
        for (int k = 0; k < knotCount; k++)
        {
            var knot = knots[k];
            if (knot.HasStub)
            {
                double t = knot.T;
                var baseP = AxisPoint(t);
                double rAtT = RadiusAt(t);
                double collarR = rAtT + knot.Height * 0.85;
                double stubLen = radius * rng.Range(0.35, 0.65);
                double stubRad = knot.Radius * rng.Range(0.35, 0.55);

                var dir = knot.Dir;
                var p0 = baseP + dir * (collarR * 0.65);
                var p1 = baseP + dir * (collarR * 0.95);
                var p2 = baseP + dir * (collarR + stubLen * 0.50);
                var p3 = baseP + dir * (collarR + stubLen);
                var path = new List<Vec3> { p0, p1, p2, p3 };
                var radii = new[] { stubRad * 1.4, stubRad * 1.15, stubRad * 0.8, stubRad * 0.4 };
                Primitives.Tube(m, path, radii, 8, (idx, v) => (Primitives.Scale(bark, 0.94), 1, idx, v, decay, 0));

                // Broken splintered branch tip
                var tipCenter = p3;
                var tipNorm = dir;
                int tipStart = m.VertexCount;
                m.AddVertex(tipCenter + tipNorm * (stubRad * 0.35), tipNorm, Primitives.Scale(heart, 0.85), 1, 0, 0, decay, 1);
                for (int s = 0; s <= 8; s++)
                {
                    double a = 2 * Math.PI * s / 8;
                    var sideDir = (new Vec3(-dir.Z, 0, dir.X).Normalized() * Math.Cos(a) + new Vec3(0, 1, 0) * Math.Sin(a)).Normalized();
                    double splinter = stubRad * 0.30 * Math.Abs(Math.Sin(a * 2.5 + k));
                    var pt = tipCenter + sideDir * (stubRad * 0.40) + tipNorm * splinter;
                    m.AddVertex(pt, tipNorm, Primitives.Scale(heart, 0.80), 1, Math.Cos(a), Math.Sin(a), decay, 1);
                }
                for (int s = 0; s < 8; s++)
                {
                    Primitives.TriangleFacing(m, tipStart, tipStart + 1 + s, tipStart + 1 + s + 1, tipNorm);
                }
            }
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
