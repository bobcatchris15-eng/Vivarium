using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    // All subsidiary geometry carries the same root as its parent appendage. This also
    // keeps the attachment intact under inherited morphs, hinge motion and body bending.
    private static void AttachSpan(MeshData m, int first, Vec3 root, double role, bool distal = false)
    {
        for (int i = first; i < m.VertexCount; i++)
        {
            var off = m.Position(i) - root;
            m.SetColor(i, off.X, off.Y, off.Z, 1);
            if (distal || role != 1) m.UV2[i * 2 + 1] = (float)(role == 1 ? 1.25 : role);
        }
    }

    private static void DetailedTube(MeshData m, Vec3 root, Vec3[] path, double[] radii,
        int region = 4, double role = 1, bool distal = false, int sides = 8)
    {
        var points = new List<Vec3>(); var widths = new List<double>();
        for (int k = 0; k < path.Length - 1; k++)
        {
            var a = path[Math.Max(0, k - 1)]; var b = path[k];
            var c = path[k + 1]; var d = path[Math.Min(path.Length - 1, k + 2)];
            for (int j = 0; j < 3; j++)
            {
                double t = j / 3.0, tt = t * t, ttt = tt * t;
                points.Add((b * 2 + (c - a) * t + (a * 2 - b * 5 + c * 4 - d) * tt
                    + (-a + b * 3 - c * 3 + d) * ttt) * 0.5);
                widths.Add(MathD.Lerp(radii[k], radii[k + 1], t));
            }
        }
        points.Add(path[^1]); widths.Add(radii[^1]);
        int first = m.VertexCount;
        Appendage(m, points, widths, sides, region, role);
        AttachSpan(m, first, root, role, distal);
    }

    private static void SensoryEye(MeshData m, Vec3 centre, Vec3 radii, Vec3 root)
    {
        int first = m.VertexCount;
        BodySegment(m, centre, radii, 8, 12, (a, b) => Region(a, b, 2));
        AttachSpan(m, first, root, 3);
    }

    private static void Triops(MeshData m)
    {
        // Thin horseshoe shield with a recessed underside and a continuous rounded rim.
        BodySegment(m, new Vec3(0.135, 0.105, 0), new Vec3(0.34, 0.095, 0.28), 24, 36,
            (a, b) => (Body, 0, a, b, 5, 4));
        BodySegment(m, new Vec3(0.11, 0.035, 0), new Vec3(0.27, 0.026, 0.225), 14, 24,
            (a, b) => Region(a, b, 3));
        BodySegment(m, new Vec3(0.310, 0.188, 0), new Vec3(0.006, 0.004, 0.006), 6, 10,
            (a, b) => (Body, 0, a, b, 2, 4)); // median ocellus between the paired compound eyes
        var rim = new List<Vec3>();
        for (int k = 0; k <= 72; k++)
        {
            double a = Math.Tau * k / 72;
            rim.Add(new Vec3(0.135 + 0.335 * Math.Cos(a), 0.105, 0.278 * Math.Sin(a)));
        }
        Primitives.Tube(m, rim, rim.Select(_ => 0.005).ToArray(), 6,
            (i, v) => (Body, 0, i / 72.0, v, 5, 4));
        for (int k = 0; k < 15; k++)
        {
            double t = k / 14.0, x = -0.13 - t * 0.40, r = 0.082 - t * 0.036;
            BodySegment(m, new Vec3(x, 0.070, 0), new Vec3(0.024, r * 0.55, r), 8, 16,
                (a, b) => Region(0.4 - t * 0.4, b, 1));
        }
        foreach (double side in new[] { -1.0, 1.0 })
        {
            // Paired eyes sit near the midline rather than on stalks.
            BodySegment(m, new Vec3(0.275, 0.190, 0.038 * side), new Vec3(0.022, 0.009, 0.016), 10, 16,
                (a, b) => (Body, 0, a, b, 2, 4));
            var tail = new Vec3(-0.53, 0.065, 0.021 * side);
            DetailedTube(m, tail, new[] { tail, tail + new Vec3(-0.14, 0.005, 0.020 * side),
                tail + new Vec3(-0.37, -0.015, 0.055 * side) }, new[] { 0.013, 0.008, 0.002 }, role: 3);
            for (int k = 0; k < 16; k++)
            {
                double x = 0.29 - k * 0.047, reach = 0.20 - k * 0.006;
                var root = new Vec3(x, 0.042, 0.070 * side);
                var end = new Vec3(x - 0.035, 0.005, reach * side);
                DetailedTube(m, root, new[] { root, new Vec3(x - 0.015, 0.012, reach * 0.7 * side), end },
                    new[] { 0.009, 0.012, 0.004 }, sides: 6);
                // Leaf-like swimming lobes and fine marginal filaments share that leg's root.
                int first = m.VertexCount;
                BodySegment(m, end + new Vec3(-0.012, 0.003, 0), new Vec3(0.023, 0.006, 0.020), 5, 8,
                    (a, b) => Region(a, b, 4));
                AttachSpan(m, first, root, 1, distal: true);
                for (int j = 0; j < 3; j++)
                {
                    var p = end + new Vec3(-0.022 + j * 0.014, 0, 0.013 * side);
                    DetailedTube(m, root, new[] { p, p + new Vec3(-0.012, 0, 0.025 * side) },
                        new[] { 0.002, 0.0007 }, distal: true, sides: 4);
                }
            }
            var antenna = new Vec3(0.30, 0.04, 0.10 * side);
            DetailedTube(m, antenna, new[] { antenna, antenna + new Vec3(0.14, 0, 0.20 * side),
                antenna + new Vec3(0.19, 0.025, 0.33 * side) }, new[] { 0.006, 0.004, 0.001 }, role: 3);
        }
    }

    private static void Harvestman(MeshData m)
    {
        // One fused oval body, dorsal eye turret, short pedipalps and eight multi-jointed legs.
        BodySegment(m, new Vec3(-0.015, 0.19, 0), new Vec3(0.175, 0.098, 0.133), 20, 28,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(0.115, 0.185, 0), new Vec3(0.058, 0.053, 0.082), 12, 20,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(0.069, 0.282, 0), new Vec3(0.035, 0.023, 0.040), 10, 16,
            (a, b) => Region(a, b, 1));
        foreach (double side in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.071, 0.289, 0.036 * side), new Vec3(0.015, 0.013, 0.008), 8, 12,
                (a, b) => Region(a, b, 2));
            var palp = new Vec3(0.14, 0.155, 0.05 * side);
            DetailedTube(m, palp, new[] { palp, new Vec3(0.22, 0.10, 0.10 * side),
                new Vec3(0.26, 0.070, 0.12 * side), new Vec3(0.285, 0.045, 0.09 * side) },
                new[] { 0.014, 0.012, 0.008, 0.003 }, role: 3);
            for (int k = 0; k < 4; k++)
            {
                double x = 0.13 - k * 0.085, sweep = (1.5 - k) * 0.18;
                double span = k == 1 ? 0.74 : k == 2 ? 0.61 : 0.55;
                var root = new Vec3(x, 0.18, 0.096 * side);
                var knee = new Vec3(x + sweep * 0.55, 0.29, span * 0.48 * side);
                var ankle = new Vec3(x + sweep, 0.038, span * 0.86 * side);
                var toe = new Vec3(x + sweep + 0.045, 0.005, span * side);
                DetailedTube(m, root, new[] { root, root + new Vec3(sweep * 0.12, 0.025, 0.038 * side),
                    knee, ankle, toe }, new[] { 0.016, 0.016, 0.011, 0.006, 0.0028 });
                // Terminal tarsus bends down into a small claw, at the full planted stroke.
                DetailedTube(m, root, new[] { toe, toe + new Vec3(0.017, 0.008, 0.006 * side),
                    toe + new Vec3(0.022, 0, 0.003 * side) }, new[] { 0.003, 0.002, 0.0008 }, distal: true, sides: 5);
            }
            var jaw = new Vec3(0.16, 0.16, 0.026 * side);
            DetailedTube(m, jaw, new[] { jaw, jaw + new Vec3(0.035, -0.032, 0), jaw + new Vec3(0.02, -0.045, 0) },
                new[] { 0.014, 0.010, 0.002 }, region: 7, role: 3);
        }
    }

    private static void Slug(MeshData m)
    {
        // A flattened muscular sole beneath a tapered visceral mass and oval mantle.
        BodySegment(m, new Vec3(-0.025, 0.028, 0), new Vec3(0.46, 0.027, 0.14), 24, 28,
            (a, b) => Region(a, b, 3));
        var path = new List<Vec3>(); var radii = new List<double>();
        for (int k = 0; k <= 44; k++)
        {
            double t = k / 44.0;
            double r = 0.004 + 0.091 * Math.Pow(Math.Sin(Math.PI * t), 0.55);
            path.Add(new Vec3(0.42 - t * 0.91, 0.040 + r * 0.68, 0));
            radii.Add(r);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radii, 24, (i, v) => Region(1 - i / 44.0, v, 1), Vec3.Up);
        // Elliptical sections keep the flank smooth while resting on the muscular sole.
        for (int i = start; i < m.VertexCount; i++)
        {
            int sample = Math.Min(44, (i - start) / 25);
            m.Positions[i * 3 + 1] = (float)(path[sample].Y + (m.Position(i).Y - path[sample].Y) * 0.68);
        }
        BodySegment(m, new Vec3(0.075, 0.155, 0), new Vec3(0.195, 0.052, 0.098), 20, 28,
            (a, b) => Region(a, b, 5));
        BodySegment(m, new Vec3(0.32, 0.090, 0), new Vec3(0.12, 0.060, 0.10), 16, 24,
            (a, b) => Region(a, b, 1));
        // Pneumostome: small breathing opening on the mantle's right side.
        BodySegment(m, new Vec3(0.16, 0.155, 0.087), new Vec3(0.015, 0.009, 0.004), 8, 12,
            (a, b) => Region(a, b, 7));
        foreach (double side in new[] { -1.0, 1.0 })
        {
            var root = new Vec3(0.34, 0.13, 0.055 * side);
            var tip = new Vec3(0.515, 0.268, 0.145 * side);
            DetailedTube(m, root, new[] { root, new Vec3(0.43, 0.22, 0.115 * side), tip },
                new[] { 0.017, 0.011, 0.007 }, region: 1, role: 3);
            SensoryEye(m, tip, new Vec3(0.011, 0.010, 0.010), root);
            var lower = new Vec3(0.375, 0.077, 0.060 * side);
            DetailedTube(m, lower, new[] { lower, lower + new Vec3(0.083, 0.025, 0.060 * side),
                lower + new Vec3(0.125, 0.010, 0.080 * side) }, new[] { 0.012, 0.006, 0.0025 }, region: 1, role: 3);
        }
    }

    private static void Millipede(MeshData m)
    {
        const int rings = 20;
        for (int k = 0; k < rings; k++)
        {
            double t = k / (double)(rings - 1), x = 0.405 - t * 0.86;
            double r = 0.058 * (0.72 + 0.28 * Math.Sin(Math.PI * t));
            Primitives.Tube(m, new[] { new Vec3(x + 0.024, 0.084, 0), new Vec3(x, 0.084, 0),
                new Vec3(x - 0.024, 0.084, 0) }, new[] { r * 0.95, r, r * 0.95 }, 20,
                (i, v) => Region(1 - i / 2.0, v, 1));
            if (k == 0) continue; // Collum behind the head has no legs.
            int pairs = k < 4 ? 1 : 2;
            for (int pair = 0; pair < pairs; pair++) foreach (double side in new[] { -1.0, 1.0 })
            {
                var root = new Vec3(x + (pair - (pairs - 1) * 0.5) * 0.019, 0.053, r * 0.72 * side);
                DetailedTube(m, root, new[] { root, root + new Vec3(-0.012, -0.012, 0.045 * side),
                    root + new Vec3(0.010, -0.047, 0.068 * side), root + new Vec3(0.025, -0.050, 0.074 * side) },
                    new[] { 0.007, 0.005, 0.003, 0.0015 }, sides: 6);
            }
        }
        BodySegment(m, new Vec3(0.475, 0.084, 0), new Vec3(0.071, 0.053, 0.064), 16, 24,
            (a, b) => Region(a, b, 1));
        foreach (double side in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.497, 0.101, 0.058 * side), new Vec3(0.015, 0.012, 0.007), 8, 12,
                (a, b) => Region(a, b, 2));
            var root = new Vec3(0.52, 0.079, 0.034 * side);
            DetailedTube(m, root, new[] { root, root + new Vec3(0.04, 0.018, 0.044 * side),
                root + new Vec3(0.086, 0.025, 0.057 * side), root + new Vec3(0.12, 0.010, 0.05 * side) },
                new[] { 0.009, 0.007, 0.006, 0.003 }, role: 3);
        }
    }

    private static void Worm(MeshData m)
    {
        // Actual annuli and a slightly swollen clitellum, rather than one smooth hose.
        var path = new List<Vec3>(); var radii = new List<double>();
        const int samples = 240;
        for (int k = 0; k <= samples; k++)
        {
            double t = k / (double)samples, taper = Math.Pow(Math.Sin(Math.PI * t), 0.28);
            double annulus = 1 - 0.055 * Math.Pow(0.5 + 0.5 * Math.Cos(t * Math.Tau * 60), 6);
            double collar = t > 0.21 && t < 0.34 ? 1.18 : 1;
            path.Add(new Vec3(0.51 - t * 1.02, 0.044 + 0.004 * Math.Sin(Math.PI * t), 0));
            radii.Add((0.006 + 0.038 * taper) * annulus * collar);
        }
        Primitives.Tube(m, path, radii, 18, (i, v) => Region(1 - i / (double)samples, v,
            i > samples * 0.21 && i < samples * 0.34 ? 5 : 1), Vec3.Up);
        BodySegment(m, new Vec3(0.512, 0.044, 0), new Vec3(0.005, 0.006, 0.006), 6, 10,
            (a, b) => Region(a, b, 7));
    }

    private static void AquaticLarva(MeshData m)
    {
        // Predatory aquatic nymph: broad thorax, tapered abdomen, six legs, jaw mask and three gills.
        for (int k = 0; k < 10; k++)
        {
            double t = k / 9.0, r = 0.067 * (1 - 0.63 * t);
            BodySegment(m, new Vec3(0.04 - t * 0.49, 0.088, 0), new Vec3(0.044, r * 0.8, r), 10, 18,
                (a, b) => Region(0.65 - t * 0.55, b, 1));
        }
        BodySegment(m, new Vec3(0.19, 0.105, 0), new Vec3(0.14, 0.072, 0.087), 16, 24,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(0.365, 0.115, 0), new Vec3(0.105, 0.053, 0.106), 18, 28,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(0.390, 0.067, 0), new Vec3(0.070, 0.020, 0.065), 12, 18,
            (a, b) => Region(a, b, 5));
        foreach (double side in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.398, 0.135, 0.094 * side), new Vec3(0.021, 0.028, 0.018), 12, 18,
                (a, b) => Region(a, b, 2));
            BodySegment(m, new Vec3(0.11, 0.154, 0.055 * side), new Vec3(0.097, 0.017, 0.034), 12, 18,
                (a, b) => Region(a, b, 5)); // folded developing wing pads
            var antenna = new Vec3(0.43, 0.145, 0.033 * side);
            DetailedTube(m, antenna, new[] { antenna, antenna + new Vec3(0.075, 0.015, 0.035 * side),
                antenna + new Vec3(0.12, 0.035, 0.045 * side) }, new[] { 0.005, 0.003, 0.001 }, role: 3);
            for (int k = 0; k < 3; k++)
            {
                var root = new Vec3(0.265 - k * 0.090, 0.092, 0.069 * side);
                DetailedTube(m, root, new[] { root, root + new Vec3(0.025, 0.027, 0.063 * side),
                    root + new Vec3(0.075 - k * 0.045, -0.070, 0.14 * side),
                    root + new Vec3(0.11 - k * 0.055, -0.085, 0.18 * side) },
                    new[] { 0.014, 0.012, 0.006, 0.0025 });
            }
        }
        foreach (double side in new[] { -1.0, 0.0, 1.0 })
        {
            var root = new Vec3(-0.46, 0.090, 0.014 * side);
            var rim = new List<Vec3>();
            for (int k = 0; k < 32; k++)
            {
                double a = k / 32.0 * Math.Tau;
                rim.Add(root + new Vec3(-0.115 + 0.115 * Math.Cos(a),
                    (side == 0 ? 0.053 : 0.014) * Math.Sin(a), side * 0.09 * (1 - Math.Cos(a)) + 0.040 * Math.Sin(a)));
            }
            AppendageFan(m, root, root + new Vec3(-0.10, 0, side * 0.08), rim, side == 0 ? new Vec3(0, 0, 1) : Vec3.Up);
        }
    }

    private static void Snail(MeshData m)
    {
        BodySegment(m, new Vec3(0.04, 0.033, 0), new Vec3(0.45, 0.032, 0.13), 24, 28,
            (a, b) => Region(a, b, 3));
        BodySegment(m, new Vec3(0.29, 0.084, 0), new Vec3(0.18, 0.066, 0.094), 18, 24,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(-0.10, 0.070, 0), new Vec3(0.23, 0.065, 0.091), 18, 24,
            (a, b) => Region(a, b, 1));
        // Planispiral freshwater shell: growing tube in the XY plane, visible from both flanks.
        var path = new List<Vec3>(); var radii = new List<double>();
        const int samples = 176;
        for (int k = 0; k <= samples; k++)
        {
            double t = k / (double)samples, a = -0.65 + t * Math.Tau * 2.65;
            double r = 0.020 * Math.Exp(t * 2.22);
            path.Add(new Vec3(-0.09 + r * Math.Cos(a), 0.200 + r * Math.Sin(a), 0));
            radii.Add(0.010 + 0.060 * t * t);
        }
        int first = m.VertexCount;
        Primitives.Tube(m, path, radii, 24, (i, v) => (Body, 0, i / (double)samples, v, 5, 4), new Vec3(0, 0, 1));
        // Tube cross-section is slightly compressed into a ramshorn-like disc.
        for (int i = first; i < m.VertexCount; i++) m.Positions[i * 3 + 2] *= 0.85f;
        var aperture = path[^1];
        BodySegment(m, aperture, new Vec3(0.040, 0.040, 0.044), 12, 18,
            (a, b) => (Body, 0, a, b, 7, 4));
        foreach (double side in new[] { -1.0, 1.0 })
        {
            var root = new Vec3(0.39, 0.11, 0.047 * side);
            var tip = new Vec3(0.565, 0.222, 0.133 * side);
            DetailedTube(m, root, new[] { root, root + new Vec3(0.093, 0.10, 0.068 * side), tip },
                new[] { 0.011, 0.006, 0.0018 }, region: 1, role: 3);
            // Aquatic snail eyes sit at the bases of the tentacles, not on their tips.
            SensoryEye(m, root + new Vec3(0.020, 0.005, 0.012 * side), new Vec3(0.008, 0.007, 0.006), root);
            var lip = new Vec3(0.435, 0.043, 0.044 * side);
            DetailedTube(m, lip, new[] { lip, lip + new Vec3(0.06, 0.005, 0.019 * side) },
                new[] { 0.011, 0.004 }, region: 1, role: 3);
        }
    }

    private static void Moth(MeshData m)
    {
        BodySegment(m, new Vec3(0.09, 0.13, 0), new Vec3(0.15, 0.074, 0.070), 18, 24,
            (a, b) => Region(a, b, 1));
        BodySegment(m, new Vec3(0.285, 0.135, 0), new Vec3(0.068, 0.057, 0.065), 14, 24,
            (a, b) => Region(a, b, 1));
        for (int k = 0; k < 8; k++)
        {
            double t = k / 7.0, r = 0.056 * (1 - 0.5 * t);
            BodySegment(m, new Vec3(-0.055 - t * 0.30, 0.115, 0), new Vec3(0.032, r, r), 10, 16,
                (a, b) => Region(0.40 - t * 0.30, b, 1));
        }
        foreach (double side in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.304, 0.145, 0.058 * side), new Vec3(0.027, 0.029, 0.016), 12, 20,
                (a, b) => Region(a, b, 2));
            Wing(new Vec3(0.105, 0.163, 0.054 * side), side, true);
            Wing(new Vec3(-0.005, 0.147, 0.048 * side), side, false);
            var root = new Vec3(0.32, 0.173, 0.030 * side);
            var tip = root + new Vec3(0.23, 0.082, 0.13 * side);
            DetailedTube(m, root, new[] { root, root + new Vec3(0.10, 0.055, 0.06 * side), tip },
                new[] { 0.006, 0.004, 0.001 }, role: 3, sides: 6);
            for (int k = 1; k < 13; k++)
            {
                double t = k / 13.0;
                var p = Vec3.Lerp(root, tip, t);
                foreach (double direction in new[] { -1.0, 1.0 })
                    DetailedTube(m, root, new[] { p, p + new Vec3(-0.015 * direction, 0.004, 0.030 * direction * side * Math.Sin(Math.PI * t)) },
                        new[] { 0.002, 0.0006 }, role: 3, sides: 4);
            }
            for (int k = 0; k < 3; k++)
            {
                var leg = new Vec3(0.18 - k * 0.08, 0.088, 0.046 * side);
                DetailedTube(m, leg, new[] { leg, leg + new Vec3(0.045 - k * 0.045, -0.014, 0.05 * side),
                    leg + new Vec3(0.09 - k * 0.060, -0.065, 0.09 * side),
                    leg + new Vec3(0.12 - k * 0.065, -0.08, 0.13 * side) }, new[] { 0.009, 0.007, 0.004, 0.0015 }, sides: 6);
            }
        }
        // Fine thoracic pile changes the silhouette without an expensive transparent fur material.
        var rng = Rng.Keyed(713, "fauna.moth.pile", 0);
        for (int k = 0; k < 150; k++)
        {
            double x = rng.Range(-0.04, 0.22), a = rng.Range(-1.6, 1.6);
            double r = Math.Sqrt(Math.Max(0, 1 - Math.Pow((x - 0.09) / 0.15, 2)));
            var p = new Vec3(x, 0.13 + 0.073 * r * Math.Cos(a), 0.070 * r * Math.Sin(a));
            var d = new Vec3(-0.008, 0.013 * Math.Cos(a), 0.013 * Math.Sin(a));
            Primitives.Tube(m, new[] { p, p + d }, new[] { 0.0017, 0.00025 }, 4, (i, v) => Region(i, v, 6));
        }

        void Wing(Vec3 root, double side, bool fore)
        {
            // Subdivided, curved membranes with scalloped margins; each pair hinges independently.
            const int around = 64, radial = 10;
            int first = m.VertexCount;
            for (int ring = 0; ring <= radial; ring++) for (int k = 0; k <= around; k++)
            {
                double a = k / (double)around * Math.Tau, f = ring / (double)radial;
                double z = (1 - Math.Cos(a)) * (fore ? 0.31 : 0.225);
                double x = (fore ? 0.03 : -0.16) * (1 - Math.Cos(a)) + Math.Sin(a) * (fore ? 0.23 : 0.20);
                double scallop = 1 - 0.035 * Math.Pow(Math.Sin(a * 6), 2);
                var off = new Vec3(x * f * scallop, 0.012 * Math.Sin(Math.PI * f) - (fore ? 0 : 0.007 * f), z * f * side * scallop);
                m.AddVertex(root + off, Vec3.Up, off.X, off.Y, off.Z, 1, f, k / (double)around, fore ? 5 : 8, 2);
            }
            for (int ring = 0; ring < radial; ring++) for (int k = 0; k < around; k++)
            {
                int a = first + ring * (around + 1) + k, b = a + around + 1;
                m.AddTriangle(a, b, a + 1); m.AddTriangle(a + 1, b, b + 1);
            }
        }
    }
}
