using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    /// <summary>
    /// Siltstars: Submerged, substrate-rooted bioluminescent star rosettes linked by arching stolon runners across the silt bed.
    /// - Arching, succulent stolon runners connect mother and daughter rosettes.
    /// - Each rosette node anchors with adventitious rhizoid rootlets into the silt bed.
    /// - Rosettes feature radiating succulent lanceolate blades with translucent margins.
    /// - Central jewel-like bioluminescent lens beads (UV2.x = 1.65, UV2.y = 1.0) emit bright teal-cyan glow.
    /// </summary>
    private static void Siltstars(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int arms = detail == 2 ? 3 : detail == 1 ? 4 : 5;
        double[] fresh = Primitives.Mix(c1, new[] { 0.22, 0.58, 0.52 }, 0.45);
        double[] edge = Primitives.Mix(c2, new[] { 0.55, 0.92, 0.88 }, 0.42);
        double[] runnerCol = Primitives.Mix(c1, c2, 0.35);
        double[] rootCol = new[] { 0.18, 0.22, 0.20 };
        double[] hubCol = Primitives.Mix(c1, new[] { 0.16, 0.38, 0.35 }, 0.50);
        double[] coreLumenCol = Primitives.Mix(c2, new[] { 0.35, 0.90, 1.00 }, 0.70);

        var centers = new List<Vec3> { new(0, 0.016, 0) };

        for (int arm = 0; arm < arms; arm++)
        {
            double angle = Math.PI * 2 * arm / arms + rng.Range(-0.08, 0.08);
            double sinA = Math.Sin(angle), cosA = Math.Cos(angle);
            var armDir = new Vec3(cosA, 0, sinA);
            var perpDir = new Vec3(-sinA, 0, cosA);

            // Sinuous, gently arching stolon runner
            double distMid = 0.35 + rng.Range(-0.03, 0.03);
            double distEnd = 0.72 + rng.Range(-0.04, 0.04);

            var p0 = new Vec3(0, 0.015, 0);
            var pArch1 = armDir * (distMid * 0.45) + perpDir * rng.Range(-0.025, 0.025) + new Vec3(0, 0.026, 0);
            var mid = armDir * distMid + perpDir * rng.Range(-0.02, 0.02) + new Vec3(0, 0.018, 0);
            var pArch2 = armDir * ((distMid + distEnd) * 0.5) + perpDir * rng.Range(-0.03, 0.03) + new Vec3(0, 0.028, 0);
            var end = armDir * distEnd + perpDir * rng.Range(-0.02, 0.02) + new Vec3(0, 0.020, 0);

            var runnerPath = new[] { p0, pArch1, mid, pArch2, end };
            var runnerRads = new[] { 0.0055, 0.0045, 0.0040, 0.0035, 0.0028 };
            Primitives.Tube(m, runnerPath, runnerRads, detail == 2 ? 4 : 5,
                (i, v) => (Primitives.Mix(runnerCol, edge, (double)i / 4 * 0.4), 1.0, (double)i / 4, v, 0, 0));

            if (detail == 0) centers.Add(mid);
            centers.Add(end);
        }

        // Rosettes at each center node
        int leaves = detail == 2 ? 4 : detail == 1 ? 5 : 6;
        for (int cIdx = 0; cIdx < centers.Count; cIdx++)
        {
            var center = centers[cIdx];
            bool isMother = cIdx == 0;
            double scale = isMother ? 1.0 : 0.78;

            // 1. Anchoring rhizoid rootlets dipping down into sediment
            int roots = 3 + (isMother ? 2 : 0);
            for (int r = 0; r < roots; r++)
            {
                double rAng = (double)r / roots * Math.PI * 2 + (double)(seed % 11);
                var rEnd = new Vec3(center.X + Math.Cos(rAng) * 0.04 * scale, -0.008, center.Z + Math.Sin(rAng) * 0.04 * scale);
                Primitives.Tube(m, new[] { center + new Vec3(0, 0.002, 0), rEnd }, new[] { 0.0025 * scale, 0.0010 }, 3,
                    (st, u) => (rootCol, 1.0, st, u, 0, 0));
            }

            // 2. Fleshy basal hub cushion
            double hubR = 0.016 * scale;
            Primitives.Ellipsoid(m, center + new Vec3(0, 0.004, 0), new Vec3(hubR, 0.006 * scale, hubR), 3, 6,
                (u, v) => (hubCol, 1.0, u, v, 0, 0));

            // 3. Central bioluminescent jewel/lens bead
            double jewelR = 0.0075 * scale;
            Primitives.Ellipsoid(m, center + new Vec3(0, 0.009 * scale, 0), new Vec3(jewelR, 0.005 * scale, jewelR), 3, 6,
                (u, v) => (coreLumenCol, 1.0, u, v, 1.65, 1.0));

            // 4. Radiating succulent star blades
            double phase = rng.Range(0, Math.PI * 2);
            for (int i = 0; i < leaves; i++)
            {
                double angle = phase + 2 * Math.PI * i / leaves + rng.Range(-0.06, 0.06);
                double len = rng.Range(0.16, 0.24) * scale;
                var root = center + new Vec3(0, 0.004, 0);

                // Low, arched succulent blades radiating outward like a starfish
                double lift = rng.Range(0.018, 0.035);
                var tip = root + new Vec3(Math.Cos(angle) * len, lift, Math.Sin(angle) * len);
                var side = new Vec3(-Math.Sin(angle), 0.08, Math.Cos(angle));

                FoliageBlade.Build(m, root, tip, side, rng.Range(0.024, 0.034) * scale, fresh, edge,
                    camber: 0.12, curl: 0.045, asymmetry: rng.Range(-0.05, 0.05), shoulder: 0.82, segments: 4);
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
