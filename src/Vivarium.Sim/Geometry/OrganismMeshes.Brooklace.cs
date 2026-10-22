using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Brooklace (<c>brooklace</c>) — a trailing water-starwort that knits shallow water together.
///
/// Reference. The plant is built against water starwort, <i>Callitriche stagnalis</i>, and the
/// brooklimes (<i>Veronica beccabunga</i>): thin, pale, thread-like runners that creep over wet mud
/// and rise towards the surface; a pair of opposite, glossy, fleshy leaves at every node, narrow
/// and spatulate on the submerged stem and broader in the floating rosettes at its tip; white
/// adventitious rootlets at the nodes, diving into the mud; tiny four-petalled flowers in the leaf
/// axils; and a star-rosette of leaves lying at the water surface on the runner's end.
///
/// Fantasy. The plant's name is its trick: the rootlets are not roots but lace, branching again and
/// again into fine white threads that knit the mud together, and the leaves are glassy and
/// translucent at their margins the way wet starwort leaves are.
///
/// Authoring. The plant is built in world metres and converted to the engine's mesh frame by
/// <see cref="AppendBrooklaceWorld"/>, so what is built here is what the player sees.
///
/// Junctions. The brief asked for organic junctions, so: the leaf pair at a node rises from a
/// swollen node ring on the runner, the rootlets leave the same ring, and the runner thickens
/// where it leaves the crown.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------
    // Anatomy constants, world metres.
    // ---------------------------------------------------------------------

    /// <summary>Radius of a runner where it leaves the crown.</summary>
    private const double BrooklaceRunnerRadius = 0.0013;
    /// <summary>Length of a stem leaf.</summary>
    private const double BrooklaceLeafLength = 0.0105;

    /// <summary>
    /// The leaf's girth along its length: a narrow stalk at the base, the widest about half way,
    /// and a rounded tip. Width factor, then thickness factor.
    /// </summary>
    private static readonly double[] BrooklaceLeafProfile =
    {
        0.00, 0.10, 0.14,
        0.10, 0.40, 0.54,
        0.26, 0.72, 0.88,
        0.46, 0.92, 1.00,
        0.66, 1.00, 0.95,
        0.82, 0.93, 0.74,
        0.93, 0.72, 0.50,
        1.00, 0.28, 0.18,
    };

    private static void BrooklaceProfile(double t, out double width, out double thickness)
    {
        double u = Math.Clamp(t, 0, 1);
        for (int i = 0; i < BrooklaceLeafProfile.Length / 3 - 1; i++)
        {
            int k = i * 3;
            double t0 = BrooklaceLeafProfile[k], t1 = BrooklaceLeafProfile[k + 3];
            if (u < t0 || u > t1) continue;
            double f = (u - t0) / Math.Max(1e-9, t1 - t0);
            f = f * f * (3 - 2 * f);
            width = BrooklaceLeafProfile[k + 1] + (BrooklaceLeafProfile[k + 4] - BrooklaceLeafProfile[k + 1]) * f;
            thickness = BrooklaceLeafProfile[k + 2] + (BrooklaceLeafProfile[k + 5] - BrooklaceLeafProfile[k + 2]) * f;
            return;
        }
        width = BrooklaceLeafProfile[1];
        thickness = BrooklaceLeafProfile[2];
    }

    // ---------------------------------------------------------------------
    // The leaf
    // ---------------------------------------------------------------------

    /// <summary>
    /// One glossy starwort leaf: a fleshy, keeled spatulate blade with a translucent margin.
    /// </summary>
    private static void BrooklaceLeaf(MeshData m, Vec3 attach, Vec3 dir, Vec3 across, Vec3 up,
        double length, double width, double girth, double[] baseCol, double[] tipCol,
        double[] marginCol, int rings, int around)
    {
        var fwd = dir.Normalized();
        var side = across - fwd * across.Dot(fwd);
        if (side.LengthSq < 1e-9) side = fwd.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var top = side.Cross(fwd).Normalized();

        int first = m.VertexCount;
        var pos = new Vec3[rings + 1, around + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            BrooklaceProfile(t, out double wf, out double tf);
            double lift = Math.Sin(Math.PI * t) * length * 0.07;
            for (int j = 0; j <= around; j++)
            {
                double th = Math.Tau * j / around;
                double rx = width * wf;
                double ry = girth * tf;
                // The midrib: the underside is keeled, the upper face flat.
                double k = 1.0 - 0.22 * Math.Max(0.0, -Math.Sin(th)) * wf;
                pos[i, j] = attach + fwd * (length * t) + top * lift
                       + side * (Math.Cos(th) * rx) + top * (Math.Sin(th) * ry * k);
            }
        }

        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            for (int j = 0; j <= around; j++)
            {
                double th = Math.Tau * j / around;
                int jp = (j + 1) % around, jm = (j - 1 + around) % around;
                Vec3 du, dv;
                if (i == 0) { du = pos[1, j] - pos[0, j]; dv = pos[1, jp] - pos[1, jm]; }
                else if (i == rings) { du = pos[rings, j] - pos[rings - 1, j]; dv = pos[rings, jp] - pos[rings, jm]; }
                else { du = pos[i + 1, j] - pos[i - 1, j]; dv = pos[i, jp] - pos[i, jm]; }
                Vec3 n = du.Cross(dv);
                if (n.LengthSq < 1e-18) n = top;
                n = n.Normalized();

                var col = Primitives.Mix(baseCol, tipCol, Math.Pow(t, 1.15));
                // The margin of a wet starwort leaf is thin and translucent.
                double margin = Math.Pow(Math.Abs(Math.Cos(th)), 6.0);
                col = Primitives.Mix(col, marginCol, margin * 0.45);

                m.AddVertex(pos[i, j], n, col, 1.0, t, j / (double)around, 0.0, 1.0);
            }
        }

        int stride = around + 1;
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < around; j++)
            {
                int a = first + i * stride + j, b = a + stride;
                Primitives.TriangleFacing(m, a, b, b + 1, m.NormalAt(a));
                Primitives.TriangleFacing(m, a, b + 1, a + 1, m.NormalAt(a));
            }
        m.RecordLeaf(first, m.Indices.Count, attach, length);
    }

    // ---------------------------------------------------------------------
    // The lace
    // ---------------------------------------------------------------------

    /// <summary>
    /// A rootlet, branching as it goes. A water starwort's roots are white and thread-fine; here
    /// they branch again and again, which is where the plant's name comes from.
    /// </summary>
    private static void BrooklaceRootlet(MeshData m, Rng rng, Vec3 from, Vec3 dir, double length,
        double radius, int depth, double[] colour)
    {
        var tip = from + dir * length;
        var mid = from + dir * (length * 0.5) + new Vec3(rng.Range(-0.001, 0.001), 0, rng.Range(-0.001, 0.001));
        Primitives.Tube(m, new[] { from, mid, tip },
            new[] { radius, radius * 0.62, radius * 0.22 }, 4, (i, v) => (colour, 1, i * 0.5, v, 0, 0));
        if (depth <= 0) return;
        int branches = 2;
        for (int b = 0; b < branches; b++)
        {
            double a = rng.Range(0, Math.Tau);
            var side = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            // The thread keeps going down and only drifts sideways.
            var nd = (dir * 0.72 + side * 0.55).Normalized();
            BrooklaceRootlet(m, rng, tip, nd, length * rng.Range(0.45, 0.72), radius * 0.62, depth - 1, colour);
        }
    }

    // ---------------------------------------------------------------------
    // The flower
    // ---------------------------------------------------------------------

    /// <summary>
    /// An axillary brooklace blossom: four pale lilac petals with a white throat and a golden eye.
    /// </summary>
    private static void BrooklaceFlower(MeshData m, Vec3 centre, Vec3 normal, double radius,
        double[] petalCol, double[] eyeCol)
    {
        normal = normal.Normalized();
        var side = normal.Cross(Math.Abs(normal.Y) > 0.9 ? new Vec3(1, 0, 0) : Vec3.Up).Normalized();
        var up = side.Cross(normal).Normalized();

        const int petals = 4;
        const int steps = 24;
        var rim = new List<Vec3>();
        for (int i = 0; i <= steps; i++)
        {
            double th = Math.Tau * i / steps;
            double petalWave = 0.56 + 0.44 * Math.Cos(th * petals);
            double r = radius * petalWave;
            rim.Add(centre + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r));
        }
        Primitives.Fan(m, centre, rim, normal, eyeCol, petalCol);

        // Four stamens with pale anthers, the way a starwort carries them.
        for (int a = 0; a < 4; a++)
        {
            double th = a * Math.Tau / 4 + 0.6;
            var d = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
            var tip = centre + d * (radius * 0.75) + normal * (radius * 0.18);
            Primitives.Tube(m, new[] { centre + normal * (radius * 0.06), tip },
                new[] { radius * 0.030, radius * 0.045 }, 4, (i, v) => (eyeCol, 1, i * 0.5, v, 0, 0));
        }
    }

    // ---------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------

    /// <summary>
    /// Brooklace. A skein of runners creeping over wet mud, each node throwing a pair of glossy
    /// leaves, a spray of white lace-rootlets and, now and then, an axillary blossom; the runner's
    /// tip opens into a floating rosette at the surface.
    /// </summary>
    private static void Brooklace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        double matSpan = radius > 0.01 ? radius : 0.11;
        double plantHeight = height > 0.01 ? height : 0.10;

        // Pigments: a cool wet green, a paler growing tip, and the translucent margin.
        var stemCol = Primitives.Mix(c1, c2, 0.34);
        var stemTip = Primitives.Mix(c1, c2, 0.58);
        var leafBase = Primitives.Mix(c1, c2, 0.22);
        var leafTip = Primitives.Mix(c1, c2, 0.62);
        var margin = Primitives.Mix(c2, new[] { 0.86, 0.92, 0.80 }, 0.55);
        var lace = new[] { 0.90, 0.92, 0.86 };
        var petal = Primitives.Mix(c2, new[] { 0.78, 0.70, 0.92 }, 0.60);
        var eye = new[] { 0.96, 0.90, 0.38 };

        int rings = m.Lod is { } lod ? Math.Max(4, (int)Math.Round(5 * lod.Length)) : 5;
        int around = m.Lod is { } lod2 ? Math.Max(6, (int)Math.Round(8 * lod2.Radial)) : 8;

        // --- the crown -------------------------------------------------------
        Primitives.Ellipsoid(part, new Vec3(0, 0.004, 0), new Vec3(0.009, 0.004, 0.009), 4, 8,
            (u, v) => (Primitives.Scale(stemCol, 0.85), 1, u, v, 0, 0));

        // --- the runners -----------------------------------------------------
        int runnerCount = 6 + rng.NextInt(4);
        for (int k = 0; k < runnerCount; k++)
        {
            double baseAng = Math.Tau * k / runnerCount + rng.Range(-0.26, 0.26);
            double totalReach = matSpan * rng.Range(0.72, 1.0);
            const int segs = 7;

            var path = new Vec3[segs + 1];
            var radii = new double[segs + 1];
            double curAng = baseAng;
            var curPos = new Vec3(0, 0.005, 0);
            path[0] = curPos;
            radii[0] = BrooklaceRunnerRadius * 1.3;
            for (int s = 1; s <= segs; s++)
            {
                double st = s / (double)segs;
                curAng += rng.Range(-0.26, 0.26);
                double stepLen = (totalReach / segs) * rng.Range(0.88, 1.12);
                // The runner creeps along the mud and lifts a little as it goes, so that its tip
                // reaches the surface.
                double y = 0.004 + (plantHeight * 0.16) * (st * st);
                curPos = new Vec3(curPos.X + Math.Cos(curAng) * stepLen, y, curPos.Z + Math.Sin(curAng) * stepLen);
                path[s] = curPos;
                // The runner swells at every node.
                radii[s] = BrooklaceRunnerRadius * (1.0 - 0.42 * st) * (1.0 + 0.30 * (s % 2 == 0 ? 1 : 0));
            }
            Primitives.Tube(part, path, radii, 6, (i, v) =>
            {
                double t = i / (double)segs;
                return (Primitives.Mix(stemCol, stemTip, t), 1, t, v, 0, 0);
            });

            for (int j = 1; j < segs; j++)
            {
                var nodePos = path[j];
                var fwd = (path[j + 1] - path[j - 1]).Normalized();
                var side = fwd.Cross(Vec3.Up);
                if (side.LengthSq < 1e-9) side = fwd.Cross(new Vec3(1, 0, 0));
                side = side.Normalized();

                // --- the lace -----------------------------------------------------
                int rootlets = rng.NextDouble() < 0.30 ? 2 : 1;
                for (int r = 0; r < rootlets; r++)
                {
                    double a = rng.Range(0, Math.Tau);
                    var dir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) * 0.8 - Vec3.Up * 0.9).Normalized();
                    BrooklaceRootlet(part, rng, nodePos - Vec3.Up * 0.0008, dir,
                        rng.Range(0.006, 0.013), 0.00030, 2, lace);
                }

                // --- the leaf pair ------------------------------------------------
                for (int s = -1; s <= 1; s += 2)
                {
                    var lDir = (side * s * 0.86 + fwd * rng.Range(0.10, 0.30) + Vec3.Up * rng.Range(0.08, 0.26)).Normalized();
                    double len = BrooklaceLeafLength * rng.Range(0.72, 1.20) * (1.0 - 0.18 * j / segs);
                    var lSide = lDir.Cross(Vec3.Up);
                    if (lSide.LengthSq < 1e-9) lSide = lDir.Cross(new Vec3(1, 0, 0));
                    BrooklaceLeaf(part, nodePos, lDir, lSide, Vec3.Up, len, len * 0.30, len * 0.19,
                        leafBase, leafTip, margin, rings, around);
                }

                // --- the axillary blossom -----------------------------------------
                if (j >= 2 && rng.NextDouble() < 0.38)
                {
                    var flDir = (side * rng.Range(-0.7, 0.7) + Vec3.Up * 0.9 + fwd * 0.2).Normalized();
                    var stalk = nodePos + flDir * 0.006;
                    Primitives.Tube(part, new[] { nodePos, stalk }, new[] { 0.00042, 0.00030 }, 4,
                        (i, v) => (stemTip, 1, i * 0.5, v, 0, 0));
                    BrooklaceFlower(part, stalk + flDir * 0.0012, flDir, 0.0030, petal, eye);
                }
            }

            // --- the floating rosette ------------------------------------------
            var tipPos = path[^1];
            var tipFwd = (path[^1] - path[^2]).Normalized();
            int rosetteCount = 8 + rng.NextInt(4);
            for (int r = 0; r < rosetteCount; r++)
            {
                double rAng = Math.Tau * r / rosetteCount + rng.Range(-0.14, 0.14);
                // The rosette lies flat at the surface, its leaves broader than the stem leaves.
                var rDir = (new Vec3(Math.Cos(rAng), 0, Math.Sin(rAng)) * 0.86 + tipFwd * 0.42 + Vec3.Up * 0.06).Normalized();
                double rLen = BrooklaceLeafLength * rng.Range(1.15, 1.65) * (0.88 + 0.20 * Math.Cos(rAng));
                var rSide = rDir.Cross(Vec3.Up);
                if (rSide.LengthSq < 1e-9) rSide = rDir.Cross(new Vec3(1, 0, 0));
                BrooklaceLeaf(part, tipPos, rDir, rSide, Vec3.Up, rLen, rLen * 0.38, rLen * 0.17,
                    leafBase, leafTip, margin, rings, around);
            }
        }

        AppendBrooklaceWorld(m, part, matSpan, plantHeight);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void AppendBrooklaceWorld(MeshData target, MeshData part, double radius, double height)
    {
        double sx = 1.0 / Math.Max(0.01, radius);
        double sy = 1.0 / Math.Max(0.01, height);
        int baseVertex = target.VertexCount;
        int structuralBefore = target.Structural.Count;
        target.Append(part, p => new Vec3(p.X * sx, p.Y * sy, p.Z * sx));

        for (int v = baseVertex; v < target.VertexCount; v++)
        {
            var n = target.NormalAt(v);
            var scaled = new Vec3(n.X * radius, n.Y * height, n.Z * radius);
            double len = scaled.Length;
            if (len > 1e-9)
            {
                scaled = scaled / len;
                target.Normals[v * 3] = (float)scaled.X;
                target.Normals[v * 3 + 1] = (float)scaled.Y;
                target.Normals[v * 3 + 2] = (float)scaled.Z;
            }
        }

        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }
}
