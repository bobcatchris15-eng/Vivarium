using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Fenbead (<c>fenbead</c>) — a clump of bead-leaved water crassula.
///
/// Reference. The plant is built against Helms' crassula, <i>Crassula helmsii</i>, the invasive
/// aquatic-margin succulent of the southern hemisphere: a dense tangle of fleshy stems that run
/// prostrate over wet mud and then arch up, pinkish where they lie against the ground and green
/// above it; swollen collars where each node has fused the bases of its leaf pair; narrow, plump,
/// glossy club-shaped leaves in strictly opposite decussate pairs, the tip of each one flushed
/// bronze-red; and small four-petalled white flowers with a cluster of brown anthers, which
/// <i>Crassula</i> carries on short terminal stalks.
///
/// Fantasy. The beads are pearls. Every leaf carries a soft nacreous lustre band along its upper
/// face and a blush of colour at the tip, and the blossoms are pearl-white with a golden eye, so
/// the whole clump reads as a strand of wet pearls rather than as a weed.
///
/// Authoring. The plant is built in world metres and converted to the engine's mesh frame by
/// <see cref="AppendFenbeadWorld"/>, so what is built here is what the player sees.
///
/// Junctions. The brief asked for organic junctions, so: the opposite pair of leaves is joined at
/// the node by a connate collar that clasps the stem, the stem swells into a ring at every node,
/// and the stem itself thickens where it leaves the crown.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------
    // Anatomy constants, world metres.
    // ---------------------------------------------------------------------

    /// <summary>Radius of a fleshy stem at its base.</summary>
    private const double FenbeadStemRadius = 0.0026;
    /// <summary>Length of a mature leaf.</summary>
    private const double FenbeadLeafLength = 0.0165;

    /// <summary>
    /// The leaf's girth along its length: a narrow stalk at the base, the widest about a third of
    /// the way up, and a blunt tip. Width factor, then thickness factor.
    /// </summary>
    private static readonly double[] FenbeadLeafProfile =
    {
        0.00, 0.10, 0.16,
        0.08, 0.34, 0.46,
        0.20, 0.72, 0.82,
        0.34, 0.94, 1.00,
        0.50, 1.00, 0.94,
        0.68, 0.93, 0.78,
        0.84, 0.74, 0.56,
        0.94, 0.46, 0.34,
        1.00, 0.13, 0.11,
    };

    /// <summary>Samples the leaf profile at a normalised position along the leaf.</summary>
    private static void FenbeadProfile(double t, out double width, out double thickness)
    {
        double u = Math.Clamp(t, 0, 1);
        for (int i = 0; i < FenbeadLeafProfile.Length / 3 - 1; i++)
        {
            int k = i * 3;
            double t0 = FenbeadLeafProfile[k], t1 = FenbeadLeafProfile[k + 3];
            if (u < t0 || u > t1) continue;
            double f = (u - t0) / Math.Max(1e-9, t1 - t0);
            f = f * f * (3 - 2 * f);
            width = FenbeadLeafProfile[k + 1] + (FenbeadLeafProfile[k + 4] - FenbeadLeafProfile[k + 1]) * f;
            thickness = FenbeadLeafProfile[k + 2] + (FenbeadLeafProfile[k + 5] - FenbeadLeafProfile[k + 2]) * f;
            return;
        }
        width = FenbeadLeafProfile[1];
        thickness = FenbeadLeafProfile[2];
    }

    // ---------------------------------------------------------------------
    // The leaf
    // ---------------------------------------------------------------------

    /// <summary>
    /// One bead: a plump, three-dimensional club, not a card. The blade is lofted around its own
    /// axis with a keeled cross-section, so it catches the light the way a fleshy leaf does.
    /// </summary>
    private static void FenbeadBead(MeshData m, Vec3 attach, Vec3 dir, Vec3 across, Vec3 up,
        double length, double width, double girth, double[] baseCol, double[] tipCol,
        double[] blushCol, double[] nacreCol, int rings, int around)
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
            FenbeadProfile(t, out double wf, out double tf);
            // The leaf curves gently upward, the way a crassula leaf lifts off its collar.
            double lift = Math.Sin(Math.PI * t) * length * 0.10;
            for (int j = 0; j <= around; j++)
            {
                double th = Math.Tau * j / around;
                double rx = width * wf;
                double ry = girth * tf;
                // The keel: the upper face is slightly flattened where the leaf is thickest.
                double k = 1.0 - 0.18 * Math.Max(0.0, Math.Sin(th)) * wf;
                var p = attach + fwd * (length * t) + top * lift
                       + side * (Math.Cos(th) * rx) + top * (Math.Sin(th) * ry * k);
                pos[i, j] = p;
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

                // Pigment: the leaf flushes with colour towards its tip, and a soft nacre band runs
                // along the upper face.
                var col = Primitives.Mix(baseCol, tipCol, Math.Pow(t, 1.35));
                double blush = MathD.SmoothStep(0.62, 1.0, t);
                col = Primitives.Mix(col, blushCol, blush * 0.55);
                double nacre = Math.Max(0.0, Math.Sin(th)) * (1.0 - 0.45 * MathD.SmoothStep(0.2, 0.9, t));
                col = Primitives.Mix(col, nacreCol, nacre * 0.30);

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
    // The flower
    // ---------------------------------------------------------------------

    /// <summary>
    /// A crassula flower: four white petals over a cluster of brown anthers on pale filaments.
    /// <i>Crassula</i> flowers are four-merous, not five.
    /// </summary>
    private static void FenbeadFlower(MeshData m, Vec3 centre, Vec3 normal, double radius,
        double[] petalCol, double[] antherCol)
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
            double petalWave = 0.52 + 0.48 * Math.Cos(th * petals);
            double r = radius * petalWave;
            rim.Add(centre + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r));
        }
        Primitives.Fan(m, centre, rim, normal, petalCol, Primitives.Mix(petalCol, antherCol, 0.10));

        // The anthers: a little brown cluster over the ovary.
        for (int a = 0; a < 5; a++)
        {
            double th = a * Math.Tau / 5 + 0.4;
            var d = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
            var stalk = centre + d * (radius * 0.18) + normal * (radius * 0.10);
            var tip = stalk + d * (radius * 0.16) + normal * (radius * 0.14);
            Primitives.Tube(m, new[] { centre + normal * (radius * 0.04), stalk, tip },
                new[] { radius * 0.022, radius * 0.018, radius * 0.030 }, 4,
                (i, v) => (antherCol, 1, i * 0.5, v, 0, 0));
        }
    }

    // ---------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------

    /// <summary>
    /// Fenbead. A clump of beaded succulent stems: prostrate runners along the mud arching up into
    /// a dense thicket of decussate bead leaves, tipped with small pearl blossoms.
    /// </summary>
    private static void Fenbead(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };

        double matSpan = radius > 0.01 ? radius : 0.18;
        double plantHeight = height > 0.01 ? height : 0.10;

        // Pigments: a waxy green body, a bronze-red flush, and the pearl.
        var stemBase = Primitives.Mix(new[] { 0.54, 0.27, 0.21 }, c1, 0.22);
        var stemTip = Primitives.Mix(c1, c2, 0.18);
        var leafBase = Primitives.Mix(c1, new[] { 0.34, 0.52, 0.28 }, 0.30);
        var leafTip = Primitives.Mix(c1, c2, 0.30);
        var blush = Primitives.Mix(c2, new[] { 0.66, 0.26, 0.22 }, 0.55);
        var nacre = new[] { 0.86, 0.90, 0.84 };
        var petal = Primitives.Mix(c2, new[] { 0.98, 0.97, 0.94 }, 0.86);
        var anther = new[] { 0.44, 0.32, 0.16 };

        int rings = m.Lod is { } lod ? Math.Max(4, (int)Math.Round(5 * lod.Length)) : 5;
        int around = m.Lod is { } lod2 ? Math.Max(6, (int)Math.Round(8 * lod2.Radial)) : 8;

        // --- the crown -------------------------------------------------------
        // Old, prostrate, reddened stems massed at the base of the clump.
        Primitives.Ellipsoid(part, new Vec3(0, 0.005, 0), new Vec3(0.014, 0.006, 0.014), 4, 8,
            (u, v) => (Primitives.Scale(stemBase, 0.8), 1, u, v, 0, 0));

        // --- the stems --------------------------------------------------------
        int stemCount = 14 + rng.NextInt(5);
        for (int k = 0; k < stemCount; k++)
        {
            double az = Math.Tau * k / stemCount + rng.Range(-0.24, 0.24);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double reach = matSpan * rng.Range(0.45, 0.92);
            double rise = plantHeight * rng.Range(0.55, 0.98);

            // The stem runs out along the mud first, then lifts.
            var p0 = radial * rng.Range(0.004, 0.020) + Vec3.Up * 0.004;
            var p1 = radial * (reach * 0.55) + Vec3.Up * (rise * 0.22);
            var p2 = radial * reach + Vec3.Up * rise;

            const int segs = 7;
            var sPts = new Vec3[segs + 1];
            var sRads = new double[segs + 1];
            for (int si = 0; si <= segs; si++)
            {
                double t = si / (double)segs;
                // A quadratic bezier gives the characteristic run-then-lift.
                double u = 1 - t;
                sPts[si] = p0 * (u * u) + p1 * (2 * u * t) + p2 * (t * t);
                // The stem swells at every node into a raised ring.
                double ring = 1.0 + 0.34 * Math.Pow(Math.Abs(Math.Sin(t * Math.PI * segs)), 3.0);
                sRads[si] = FenbeadStemRadius * (1.0 - 0.42 * t) * ring;
            }
            Primitives.Tube(part, sPts, sRads, 6, (i, v) =>
            {
                double t = i / (double)segs;
                var col = Primitives.Mix(stemBase, stemTip, MathD.SmoothStep(0.0, 0.42, t));
                return (col, 1, t, v, 0, 0);
            });

            for (int si = 1; si <= segs; si++)
            {
                var attach = sPts[si];
                var fwd = (si == segs ? sPts[si] - sPts[si - 1] : sPts[si + 1] - sPts[si - 1]).Normalized();
                var side0 = fwd.Cross(Vec3.Up);
                if (side0.LengthSq < 1e-9) side0 = fwd.Cross(new Vec3(1, 0, 0));
                side0 = side0.Normalized();
                var up0 = side0.Cross(fwd).Normalized();

                // Decussate: every pair is turned a quarter turn from the one below it.
                double rot = (si % 2 == 0) ? 0.0 : Math.PI * 0.5;
                var pairAxis = side0 * Math.Cos(rot) + up0 * Math.Sin(rot);

                // The connate collar: the bases of the two leaves are fused into a ring that clasps
                // the stem, the way a crassula's leaf pair is.
                var collarCentre = attach - fwd * (FenbeadStemRadius * 0.6);
                Primitives.Tube(part,
                    new[] { collarCentre - fwd * (FenbeadStemRadius * 0.8), collarCentre + fwd * (FenbeadStemRadius * 0.8) },
                    new[] { FenbeadStemRadius * 1.12, FenbeadStemRadius * 1.12 }, 6,
                    (i, v) => (Primitives.Mix(stemBase, leafBase, 0.55), 1, i, v, 0, 0));

                double len = FenbeadLeafLength * rng.Range(0.68, 1.15) * (1.0 - 0.22 * si / segs);
                double wid = len * rng.Range(0.24, 0.32);
                double girth = len * rng.Range(0.20, 0.27);

                for (int s = -1; s <= 1; s += 2)
                {
                    var dir = (pairAxis * s + fwd * rng.Range(0.10, 0.34) + Vec3.Up * rng.Range(0.02, 0.16)).Normalized();
                    var across = dir.Cross(Vec3.Up);
                    if (across.LengthSq < 1e-9) across = dir.Cross(new Vec3(1, 0, 0));
                    FenbeadBead(part, attach, dir, across, Vec3.Up, len, wid, girth,
                        leafBase, leafTip, blush, nacre, rings, around);
                }

                // A pearl bud at a few of the upper nodes.
                if (si >= 3 && rng.NextDouble() < 0.28)
                {
                    var budDir = (fwd * 0.5 + Vec3.Up * 0.8).Normalized();
                    var budBase = attach + budDir * (len * 0.2);
                    FenbeadBead(part, budBase, budDir, budDir.Cross(Vec3.Up), Vec3.Up,
                        len * 0.34, len * 0.13, len * 0.12, blush, blush, blush, nacre,
                        Math.Max(3, rings / 2), Math.Max(5, around / 2));
                }
            }

            // --- the blossom --------------------------------------------------
            if (rise > plantHeight * 0.62 && rng.NextDouble() < 0.55)
            {
                var tipPos = sPts[^1];
                var tipDir = (sPts[^1] - sPts[^2]).Normalized();
                double fr = 0.0056;
                // A short stalk lifts the flower clear of the leaves.
                var stalkTop = tipPos + tipDir * (fr * 2.4);
                Primitives.Tube(part, new[] { tipPos, stalkTop }, new[] { 0.0009, 0.0006 }, 5,
                    (i, v) => (stemTip, 1, i * 0.5, v, 0, 0));
                FenbeadFlower(part, stalkTop + tipDir * (fr * 0.4), tipDir, fr, petal, anther);
            }
        }

        AppendFenbeadWorld(m, part, matSpan, plantHeight);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the
    /// size and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void AppendFenbeadWorld(MeshData target, MeshData part, double radius, double height)
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
