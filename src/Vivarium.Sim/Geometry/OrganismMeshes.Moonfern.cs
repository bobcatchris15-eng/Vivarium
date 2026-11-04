using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    /// <summary>A sparse crown of connected, pierced leaf sheets with Boston-fern-like lobes.</summary>
    private static void MoonfernSheet(MeshData m, Rng rng, double[] c1, double[] c2, bool juvenile)
    {
        int fronds = 2 + rng.NextInt(2);
        double rotation = rng.Range(0, Math.Tau);
        // LOD changes sampling, never the number/size/orientation of the main fronds.
        int samples = Math.Clamp((int)Math.Round(6 * (m.Lod?.Blade ?? 1)), 2, 6);
        double[] across = { -1, -.76, -.40, -.14, 0, .14, .40, .76, 1 };
        for (int f = 0; f < fronds; f++)
        {
            double angle = rotation + f * Math.Tau / fronds + rng.Range(-.20, .20);
            var forward = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
            var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
            double length = rng.Range(.96, 1.10) * (juvenile ? .48 : 1);
            double width = length * rng.Range(.14, .18);
            double rise = rng.Range(1.75, 1.95);
            double droop = rng.Range(1.17, 1.30);
            double roll = rng.Range(-.13, .13);
            int lobes = 19 + rng.NextInt(4);
            var root = forward * .018;
            Vec3 Axis(double t) => root + forward * (length * (.20 * t + .74 * t * t))
                + Vec3.Up * (length * (rise * t - droop * t * t));
            Vec3 Point(double t, double x)
            {
                double u = .12 + .88 * t;
                double wave = .5 - .5 * Math.Cos(Math.Tau * lobes * t);
                double envelope = Math.Pow(Math.Max(.0001, Math.Sin(Math.PI * t)), .72);
                double w = width * envelope * (.22 + .78 * Math.Pow(wave, .65));
                // Low relief across the leaf, with gently rippling margins rather than tubular pinnae.
                double ruffle = length * .009 * x * x * Math.Sin(Math.Tau * lobes * t + x * .7);
                return Axis(u) + side * (x * w)
                    + Vec3.Up * (x * w * roll + ruffle + length * .006 * (1 - x * x));
            }

            int pathSteps = Math.Max(5, (int)Math.Round(16 * (m.Lod?.Length ?? 1)));
            var path = new Vec3[pathSteps + 1]; var radii = new double[path.Length];
            for (int i = 0; i <= pathSteps; i++)
            {
                double t = (double)i / pathSteps;
                path[i] = Axis(t);
                radii[i] = length * (.0045 * (1 - t) + .0007);
            }
            Primitives.Tube(m, path, radii, 6,
                (i, v) => (Primitives.Mix(c1, new[] { .25, .33, .12 }, .45), 1, (double)i / pathSteps, v, 0, 0));

            int start = m.VertexCount, firstIndex = m.Indices.Count;
            int rows = lobes * samples;
            for (int r = 0; r <= rows; r++)
            for (int col = 0; col < across.Length; col++)
            {
                double t = (double)r / rows, x = across[col];
                var along = Point(Math.Min(1, t + .0001), x) - Point(Math.Max(0, t - .0001), x);
                var lateral = Point(t, x + .001) - Point(t, x - .001);
                var normal = lateral.Cross(along).Normalized();
                if (normal.Y < 0) normal = -normal;
                var color = Primitives.Mix(c1, c2, .22 + .34 * t + .10 * Math.Abs(x));
                m.AddVertex(Point(t, x), normal, color, 0, t, (x + 1) * .5, 0, 1);
            }
            for (int r = 0; r < rows; r++)
            for (int col = 0; col < across.Length - 1; col++)
            {
                // Paired real apertures between lobes. A continuous central web and outer
                // bridges keep each frond one sheet; there is no alpha-discard overdraw.
                bool hole = r >= samples && r < rows - samples
                    && r % samples < Math.Max(1, samples / 3) && col is 1 or 6;
                if (hole) continue;
                int a = start + r * across.Length + col, b = a + across.Length;
                Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, Vec3.Up);
            }
            m.RecordLeaf(start, firstIndex, Axis(.12), length);
            // With only 2–3 defining leaves, whole-frond dropping would destroy the authored form.
            m.EssentialSpans.Add((start, m.VertexCount - start));
        }
    }
}
