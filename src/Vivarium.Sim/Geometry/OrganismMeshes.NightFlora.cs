using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    /// <summary>Submerged, substrate-rooted star rosettes linked by low runners across the silt bed.</summary>
    private static void Siltstars(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int arms = detail == 2 ? 3 : detail == 1 ? 4 : 5;
        double[] fresh = Primitives.Mix(c1, new[] { 0.36, 0.66, 0.53 }, 0.48);
        double[] edge = Primitives.Mix(c2, new[] { 0.70, 0.90, 0.77 }, 0.42);
        var centers = new List<Vec3> { new(0, 0.018, 0) };
        for (int arm = 0; arm < arms; arm++)
        {
            double angle = Math.PI * 2 * arm / arms + rng.Range(-0.09, 0.09);
            var mid = new Vec3(Math.Cos(angle) * 0.35, 0.027, Math.Sin(angle) * 0.35);
            var end = new Vec3(Math.Cos(angle) * 0.72, 0.032, Math.Sin(angle) * 0.72);
            Primitives.Tube(m, new[] { new Vec3(0, 0.015, 0), mid, end },
                new[] { 0.006, 0.0042, 0.0026 }, detail == 2 ? 4 : 5,
                (i, v) => (Primitives.Mix(c1, c2, i * 0.45), 1, i, v, 0, 0));
            if (detail == 0) centers.Add(mid);
            centers.Add(end);
        }

        int leaves = detail == 2 ? 3 : detail == 1 ? 4 : 5;
        foreach (var center in centers)
        {
            double phase = rng.Range(0, Math.PI * 2);
            for (int i = 0; i < leaves; i++)
            {
                double angle = phase + 2 * Math.PI * i / leaves;
                double len = rng.Range(0.18, 0.26);
                var root = center + new Vec3(0, 0.004, 0);
                // Low, swept leaves radiate as a five-point star instead of standing upright like a terrestrial herb.
                var tip = root + new Vec3(Math.Cos(angle) * len, rng.Range(0.025, 0.045), Math.Sin(angle) * len);
                var side = new Vec3(-Math.Sin(angle), 0.10, Math.Cos(angle));
                FoliageBlade.Build(m, root, tip, side, rng.Range(0.026, 0.035), fresh, edge,
                    camber: 0.11, curl: 0.035, asymmetry: rng.Range(-0.06, 0.06), segments: 3);
            }
        }
    }

    /// <summary>Spiral-caned honeysuckle-like woody shrub with three orders of shoots and lanceolate leaves.</summary>
    private static void ShrubGlowbriar(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        GlowbriarGeometry.Build(m, rng, c1, c2, juvenile);
    }
    private static Vec3 SamplePolyline(IReadOnlyList<Vec3> points, double t)
    {
        double f = Math.Clamp(t, 0, 1) * (points.Count - 1);
        int i = Math.Min(points.Count - 2, (int)f);
        return Vec3.Lerp(points[i], points[i + 1], f - i);
    }
}
