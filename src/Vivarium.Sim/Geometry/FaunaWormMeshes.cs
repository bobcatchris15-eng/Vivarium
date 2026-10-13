using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    private static void CinematicWorm(MeshData m)
    {
        // One continuous skin with a rounded anterior, gradual posterior taper
        // and a soft clitellum. Its underside follows a common support plane;
        // swelling the collar no longer pushes its belly through the ground.
        const int samples = 288, sides = 32;
        int first = m.VertexCount;
        for (int i = 0; i <= samples; i++)
        {
            double t = i / (double)samples;
            double taper = Math.Pow(Math.Sin(Math.PI * t), .30);
            double collar = Smooth(.18, .22, t) * (1 - Smooth(.32, .36, t));
            double annulus = Math.Pow(.5 + .5 * Math.Cos(t * Math.Tau * 60), 6);
            double r = (.003 + .039 * taper) * (1 + .16 * collar) * (1 - .026 * annulus * (1 - collar));
            for (int j = 0; j <= sides; j++)
            {
                double angle = Math.Tau * j / sides;
                double vertical = Math.Pow((1 + Math.Sin(angle)) * .5, .90);
                var p = new Vec3(.51 - t * 1.02, .002 + 2 * r * vertical, r * Math.Cos(angle));
                // Collar shading uses axial UVs, so region interpolation cannot
                // cross an eye label. Annelids have no eye geometry here.
                m.AddVertex(p, Vec3.Up, Body, 0, 1 - t, j / (double)sides, 1, 0);
            }
        }
        for (int i = 0; i < samples; i++) for (int j = 0; j < sides; j++)
        {
            int a = first + i * (sides + 1) + j, b = a + sides + 1;
            m.Indices.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
        }
        foreach (int i in new[] { 0, samples })
        {
            int centre = m.VertexCount;
            m.AddVertex(new Vec3(.51 - i / (double)samples * 1.02, .005, 0), Vec3.Up, Body, 0, i == 0 ? 1 : 0, .5, 1, 0);
            for (int j = 0; j < sides; j++)
            {
                int a = first + i * (sides + 1) + j;
                m.Indices.AddRange(i == 0 ? new[] { centre, a + 1, a } : new[] { centre, a, a + 1 });
            }
        }
        m.RecomputeNormals();

        static double Smooth(double a, double b, double t)
        {
            double u = Math.Clamp((t - a) / (b - a), 0, 1);
            return u * u * (3 - 2 * u);
        }
    }
}
