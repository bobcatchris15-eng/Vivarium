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

    /// <summary>Pampas-like tuft grass with arching stems and several feathered plume clusters per stem.</summary>
    private static void Pampas(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile,
        FloraLodParams? lod)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int stalks = detail == 2 ? 3 : 4;
        int tuftsPerStalk = 2;
        int plumeRachillae = detail == 2 ? 8 : detail == 1 ? 12 : 16;
        double height = juvenile ? 0.67 : 1.0;
        var bladeBase = Primitives.Mix(c1, new[] { 0.20, 0.30, 0.12 }, 0.62);
        var bladeTip = Primitives.Mix(c2, new[] { 0.61, 0.56, 0.36 }, 0.42);
        int basalLeaves = detail == 2 ? 8 : detail == 1 ? 12 : 16;
        for (int i = 0; i < basalLeaves; i++)
        {
            double a = 2 * Math.PI * i / basalLeaves + rng.Range(-0.18, 0.18);
            double len = rng.Range(0.54, 0.82) * height;
            var root = new Vec3(rng.Range(-0.035, 0.035), 0.035, rng.Range(-0.035, 0.035));
            var tip = root + new Vec3(Math.Cos(a) * len * 0.78, len * rng.Range(0.20, 0.38), Math.Sin(a) * len * 0.78);
            FoliageBlade.Build(m, root, tip, new Vec3(-Math.Sin(a), 0, Math.Cos(a)), rng.Range(0.046, 0.062),
                bladeBase, bladeTip, camber: 0.12, curl: 0.12, asymmetry: rng.Range(-0.08, 0.08), segments: 3);
        }

        for (int s = 0; s < stalks; s++)
        {
            double az = 2 * Math.PI * s / stalks + rng.Range(-0.24, 0.24);
            double stalkHeight = height * rng.Range(0.72, 0.86);
            var axis = new AxisParams(Length: stalkHeight, BaseAngle: rng.Range(0.16, 0.34), BaseAzimuth: az,
                Droop: rng.Range(0.38, 0.58), PhototropicBend: rng.Range(0.025, 0.085),
                WobbleAmplitude: 0.006, Segments: detail == 2 ? 5 : 7);
            ulong stalkSeed = Rng.Mix(seed, (ulong)(s * 701 + 37));
            SoftTube.Build(m, new SoftTubeParams(axis, BaseRadius: 0.010, TipRadius: 0.0035, Segments: detail == 2 ? 4 : 5), stalkSeed,
                (i, v) => (bladeTip, 1, i, v, 0, 0));
            var frames = Axis.Build(axis, stalkSeed);
            for (int k = 0; k < tuftsPerStalk; k++)
            {
                // The terminal plume is largest; smaller satellite tufts sit distinctly down the arching stalk.
                double t = k == 0 ? 0.64 : 0.93;
                var center = Axis.Sample(frames, t).Point;
                double spread = (k == tuftsPerStalk - 1 ? 0.36 : 0.27) * (juvenile ? 0.78 : 1.0);
                double plumeHeight = (k == tuftsPerStalk - 1 ? 0.21 : 0.16) * (juvenile ? 0.78 : 1.0);
                double[] plume = Primitives.Mix(new[] { 0.70, 0.63, 0.47 }, c2, 0.38 + 0.16 * k);
                // A dense globe of broad, tapering rachillae replaces a handful of naked wire hairs. Each
                // cambered blade fans from the stalk into a different azimuth/elevation, giving the panicle a
                // soft, legible volume while its tips retain the fine feathered fringe.
                Primitives.Ellipsoid(m, center, new Vec3(spread * 0.22, plumeHeight * 0.36, spread * 0.22), 4, 6,
                    (u, v) => (Primitives.Mix(plume, bladeTip, v * 0.22), 1, u, v, 0, 0));
                for (int f = 0; f < plumeRachillae; f++)
                {
                    double fa = 2 * Math.PI * f / plumeRachillae + rng.Range(-0.12, 0.12);
                    double reach = spread * rng.Range(0.68, 1.08);
                    double rise = plumeHeight * rng.Range(0.18, 0.94);
                    var root = center + new Vec3(Math.Cos(fa) * spread * 0.055, -plumeHeight * 0.10,
                        Math.Sin(fa) * spread * 0.055);
                    var tip = center + new Vec3(Math.Cos(fa) * reach, rise, Math.Sin(fa) * reach);
                    int plumeStart = m.VertexCount;
                    FoliageBlade.Build(m, root, tip, new Vec3(-Math.Sin(fa), 0.08, Math.Cos(fa)),
                        spread * rng.Range(0.15, 0.22), Primitives.Mix(bladeTip, plume, 0.25), plume,
                        camber: 0.18, curl: 0.06, asymmetry: rng.Range(-0.08, 0.08), segments: 3);
                    // UV2.x=1.25 tags plume rachillae so the leaf palette cannot recolour them green.
                    for (int v = plumeStart; v < m.VertexCount; v++) m.UV2[v * 2] = 1.25f;
                    // Two fine hairs branching from each flat rachilla break up the tips into a feathery edge.
                    var transverse = new Vec3(-Math.Sin(fa), 0, Math.Cos(fa));
                    foreach (double along in new[] { 0.55, 0.78 })
                    {
                        var branchBase = Vec3.Lerp(root, tip, along);
                        for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                        {
                            var branchTip = branchBase + transverse * (spread * 0.055 * sideSign)
                                + Vec3.Up * (plumeHeight * 0.035);
                            Primitives.Tube(m, new[] { branchBase, branchTip }, new[] { 0.0017, 0.00025 }, 3,
                                (i, v) => (Primitives.Mix(plume, new[] { 0.88, 0.82, 0.68 }, i * 0.65), 1, i, v, 0, 0));
                        }
                    }
                }
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
