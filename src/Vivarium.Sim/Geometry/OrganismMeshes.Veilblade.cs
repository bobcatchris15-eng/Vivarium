using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Tall wetland grass for the 'veilblade' species.
///
/// The plant is authored in the engine's unit space (radius 1 in XZ, height 1 in Y), like the
/// other mid-story forms.
///
/// Anatomy follows a tall reed - giant reed (Arundo donax) and common reed (Phragmites
/// australis) - rather than a generic grass:
///
///   * a dense clump of long, broad, arching blades, each one rising from a SHEATHING base
///     that wraps the culm, keeled along a midrib, and TWISTED as it runs out;
///   * a blade tip that is still ROLLED, the way a monocot leaf unrolls from its sheath;
///   * wiry, jointed culms that rise clear above the foliage and end in a feathery panicle
///     of small spikelets.
///
/// The fantastical element is the veil itself. A real reed blade has a smooth margin; here the
/// margin grows a fine comb of marginal filaments, the kind cattail and iris carry and that
/// people once wove, and the filaments of neighbouring blades interlace. The curtain of blades
/// is therefore literally woven, and the panicles above it trail a long silk of seed filaments.
///
/// Reference renders: scripts/visuals/veilblade/reed_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The blade green, and the paler green of the young inner growth.</summary>
    private static readonly double[] VeilBladeCol = { 0.100, 0.215, 0.108 };
    private static readonly double[] VeilBladeTip = { 0.250, 0.410, 0.200 };
    private static readonly double[] VeilSheath = { 0.255, 0.320, 0.170 };
    /// <summary>The marginal filaments: a bleached, faintly golden fibre.</summary>
    private static readonly double[] VeilFilamentCol = { 0.520, 0.520, 0.360 };
    /// <summary>The spikelets of the panicle.</summary>
    private static readonly double[] VeilSpikelet = { 0.330, 0.250, 0.190 };

    /// <summary>Across the blade: margin, mid, keel, mid, margin.</summary>
    private static readonly double[] VeilSectionX = { -1.0, -0.44, 0.0, 0.44, 1.0 };
    private static readonly double[] VeilSectionY = { 0.30, 0.04, -1.0, 0.04, 0.30 };
    /// <summary>
    /// How deep the blade folds, as a fraction of its half width. Low, because this is a
    /// wide thin strap of grass, not a keel.
    /// </summary>
    private const double VeilFlatness = 0.38;

    /// <summary>
    /// One reed blade: sheathed at the base, keeled along the midrib, twisted as it arches
    /// over, and rolled tight at the tip.
    /// </summary>
    private static void VeilBlade(MeshData m, Vec3 root, Vec3 dir, double length, double halfWidth,
        int rows, double[] c1, double[] c2, Rng rng, bool filaments)
    {
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;

        // The blade leaves the sheath at a set angle, arcs gently over, and finishes drooping.
        // The old curve curled the tip back inward, which made the clump read as a vase; a
        // grass blade's arc is the other way round, and it is a long, lazy sweep.
        double theta0 = rng.Range(0.86, 1.20);   // 49-69 degrees above horizontal
        double theta1 = rng.Range(-0.50, 0.08);   // drooping at the tip
        double twist = rng.Range(-1.4, 1.4);
        double rollFrom = rng.Range(0.86, 0.93);

        // The centreline is integrated from that turning angle, so the arc is gentle and
        // even instead of a pair of stacked curves.
        var centres = new Vec3[rows + 1];
        centres[0] = root;
        {
            double dTheta = (theta1 - theta0) / rows;
            double dl = length / rows;
            var p = root;
            for (int i = 0; i < rows; i++)
            {
                double a = theta0 + dTheta * (i + 0.5);
                var d = dir * Math.Cos(a) + Vec3.Up * Math.Sin(a);
                p = p + d * dl;
                centres[i + 1] = p;
            }
        }

        Vec3 Centre(double t)
        {
            double f = Math.Clamp(t, 0.0, 1.0) * rows;
            int i = Math.Min(rows - 1, (int)f);
            return Vec3.Lerp(centres[i], centres[i + 1], f - i);
        }

        // The frame the section is laid out in: along the blade, and the two normals.
        void Frame(double t, out Vec3 along, out Vec3 across, out Vec3 up)
        {
            double e = 0.004;
            var c0 = Centre(Math.Max(0.0, t - e));
            var c1 = Centre(Math.Min(1.0, t + e));
            along = (c1 - c0);
            if (along.LengthSq < 1e-14) along = dir;
            along = along.Normalized();
            across = along.Cross(Vec3.Up);
            if (across.LengthSq < 1e-10) across = new Vec3(1, 0, 0);
            across = across.Normalized();
            up = across.Cross(along).Normalized();
        }

        var ids = new int[rows + 1, 5];
        var normals = new Vec3[rows + 1, 5];
        var edgeA = new Vec3[rows + 1];
        var edgeB = new Vec3[rows + 1];
        var edgeUp = new Vec3[rows + 1];

        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var centre = Centre(t);
            Vec3 along, across, up;
            Frame(t, out along, out across, out up);

            // The width swells out of the sheath and tapers away to the tip.
            double w = halfWidth * (0.42 + 0.58 * Math.Sin(Math.PI * Math.Min(1.0, 0.10 + 0.72 * t)))
                * Math.Max(0.05, 1.0 - Math.Pow(Math.Max(0.0, t - 0.25) / 0.75, 1.9));
            w = Math.Max(w, halfWidth * 0.05);

            // The tip is still rolled: the section curls into a tight scroll.
            double rollT = Math.Clamp((t - rollFrom) / (1.0 - rollFrom), 0.0, 1.0);
            double roll = rollT * rollT * Math.PI * 2.4;
            double shrink = 1.0 - 0.78 * rollT;
            double twistA = twist * t;

            for (int k = 0; k < 5; k++)
            {
                double sx = VeilSectionX[k], sy = VeilSectionY[k];
                // roll the section about the blade's own axis
                double rx = sx * Math.Cos(roll) - sy * Math.Sin(roll);
                double ry = sx * Math.Sin(roll) + sy * Math.Cos(roll);
                double lx = rx * w * shrink;
                // The section is a flat strap: its depth is a fraction of its width, not a
                // share of it, so a wide blade stays a wide thin blade and not a gutter.
                double ly = ry * w * shrink * VeilFlatness;
                // twist the whole section along the blade
                double tx = lx * Math.Cos(twistA) - ly * Math.Sin(twistA);
                double ty = lx * Math.Sin(twistA) + ly * Math.Cos(twistA);
                var p = centre + across * tx + up * ty;
                var col = Primitives.Mix(c1, c2, t);
                // the midrib is paler than the blade
                if (k == 2) col = Primitives.Mix(col, VeilSheath, 0.35);
                ids[i, k] = m.AddVertex(p, up, col, 1, t, 0.5 + sx * 0.5, 0, 1);
                if (k == 0) edgeA[i] = p;
                if (k == 4) edgeB[i] = p;
                if (k == 2) edgeUp[i] = p;
            }
        }

        for (int i = 0; i < rows; i++)
            for (int k = 0; k < 4; k++)
            {
                var p0 = m.Position(ids[i, k]);
                var p1 = m.Position(ids[i, k + 1]);
                var p2 = m.Position(ids[i + 1, k]);
                var p3 = m.Position(ids[i + 1, k + 1]);
                var n = (p1 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) n = (p3 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                normals[i, k] += n;
                normals[i, k + 1] += n;
                normals[i + 1, k] += n;
                normals[i + 1, k + 1] += n;
            }
        for (int i = 0; i <= rows; i++)
            for (int k = 0; k < 5; k++)
            {
                var n = normals[i, k];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = ids[i, k];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < 4; k++)
            {
                var p0 = m.Position(ids[i, k]);
                var p1 = m.Position(ids[i, k + 1]);
                var p2 = m.Position(ids[i + 1, k]);
                var normal = (p1 - p0).Cross(p2 - p0);
                if (normal.LengthSq < 1e-18) continue;
                normal = normal.Normalized();
                Primitives.TriangleFacing(m, ids[i, k], ids[i + 1, k], ids[i, k + 1], normal);
                Primitives.TriangleFacing(m, ids[i, k + 1], ids[i + 1, k], ids[i + 1, k + 1], normal);
            }

        m.RecordLeaf(leafVertex, leafIndex, root, length);

        if (!filaments) return;

        // The marginal filaments. They stand off both edges of the blade, leaning forward
        // and lying along it, and they are what weaves the curtain together.
        int perEdge = 18;
        for (int e = 0; e < 2; e++)
        {
            for (int f = 0; f < perEdge; f++)
            {
                double t = 0.08 + 0.84 * (f / (double)(perEdge - 1)) + rng.Range(-0.015, 0.015);
                int i = Math.Clamp((int)(t * rows), 0, rows - 1);
                double lt = i / (double)rows;
                var at = e == 0 ? edgeA[i] : edgeB[i];
                Vec3 along, across, up;
                Frame(lt, out along, out across, out up);
                var outward = e == 0 ? -across : across;
                double len = length * rng.Range(0.014, 0.032);
                var tip = at + outward * (len * 0.75) + along * (len * rng.Range(1.4, 2.6))
                    + up * (len * rng.Range(-0.25, 0.35));
                VeilFilament(m, at, tip, length * 0.0013);
            }
        }
    }

    /// <summary>
    /// A marginal filament: a flat tapered ribbon. A hair is a hair at any distance, and a
    /// ribbon costs a fraction of what a solid tube would.
    /// </summary>
    private static void VeilFilament(MeshData m, Vec3 from, Vec3 to, double halfWidth)
    {
        var axis = to - from;
        if (axis.LengthSq < 1e-14) return;
        axis = axis.Normalized();
        var side = axis.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = axis.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var mid = Vec3.Lerp(from, to, 0.5);
        int b = m.AddVertex(from - side * halfWidth, side, VeilFilamentCol, 1, 0, 0, 0, 0);
        int c = m.AddVertex(from + side * halfWidth, side, VeilFilamentCol, 1, 0, 1, 0, 0);
        int d = m.AddVertex(mid + side * (halfWidth * 0.6), side, VeilFilamentCol, 1, 0.5, 1, 0, 0);
        int e = m.AddVertex(mid - side * (halfWidth * 0.6), side, VeilFilamentCol, 1, 0.5, 0, 0, 0);
        int f = m.AddVertex(to, side, VeilFilamentCol, 1, 1, 0.5, 0, 0);
        Primitives.TriangleFacing(m, b, c, d, side);
        Primitives.TriangleFacing(m, b, d, e, side);
        Primitives.TriangleFacing(m, e, d, f, side);
        Primitives.TriangleFacing(m, d, c, f, side);
    }

    /// <summary>
    /// A fertile culm with its feathery panicle: a rachis carrying whorls of branchlets, each
    /// bearing small spikelets and a long trailing silk.
    /// </summary>
    private static void VeilPanicle(MeshData m, Vec3 basePoint, Vec3 up, double length,
        double spread, Rng rng)
    {
        var side = up.Cross(new Vec3(1, 0, 0));
        if (side.LengthSq < 1e-10) side = up.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var forward = side.Cross(up).Normalized();

        // The rachis.
        var path = new Vec3[7];
        var radii = new double[7];
        for (int i = 0; i <= 6; i++)
        {
            double t = i / 6.0;
            path[i] = basePoint + up * (length * t);
            radii[i] = 0.0045 * (1.0 - 0.55 * t);
        }
        Primitives.Tube(m, path, radii, 5,
            (i, v) => (VeilSheath, 1, i / 6.0, v, 0, 0));

        int whorls = 9;
        for (int w = 0; w < whorls; w++)
        {
            double wt = (w + 0.4) / whorls;
            var wBase = basePoint + up * (length * wt);
            // The branchlets are longest low down and short at the tip, so the panicle feathers
            // out and then tapers.
            double reach = spread * (1.0 - 0.62 * wt) * rng.Range(0.85, 1.15);
            int branches = 5 + rng.NextInt(3);
            for (int b = 0; b < branches; b++)
            {
                double a = 2 * Math.PI * b / branches + w * 0.51 + rng.Range(-0.2, 0.2);
                var outward = (side * Math.Cos(a) + forward * Math.Sin(a)).Normalized();
                var bTip = wBase + outward * reach + up * (length * 0.10 * rng.Range(0.3, 1.2));
                var bMid = wBase + outward * (reach * 0.5) + up * (length * 0.05);
                Primitives.Tube(m, new[] { wBase, bMid, bTip }, new[] { 0.0016, 0.0011, 0.0004 }, 3,
                    (i, v) => (VeilSheath, 1, i * 0.5, v, 0, 0));

                // Spikelets: small dark clusters along the branchlet.
                int spikes = 3 + rng.NextInt(2);
                for (int s = 0; s < spikes; s++)
                {
                    double st = 0.25 + 0.62 * (s / (double)Math.Max(1, spikes - 1));
                    var at = Vec3.Lerp(wBase, bTip, st);
                    Primitives.Ellipsoid(m, at, new Vec3(0.0030, 0.0052, 0.0030), 2, 5,
                        (u, v) => (VeilSpikelet, 1, u, v, 0, 0));

                    // The silk: a long fine filament trailing from each spikelet. This is the
                    // part that hangs and drifts.
                    double silkLen = length * rng.Range(0.10, 0.22);
                    var silkTip = at + up * (-silkLen) + outward * (silkLen * rng.Range(-0.35, 0.35))
                        + forward * (silkLen * rng.Range(-0.3, 0.3));
                    Primitives.Tube(m, new[] { at, Vec3.Lerp(at, silkTip, 0.5), silkTip },
                        new[] { 0.0007, 0.0004, 0.00008 }, 3,
                        (i, v) => (Primitives.Mix(VeilFilamentCol, VeilSheath, 0.35), 1, i * 0.5, v, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// The veilblade: several basal fans of long arching blades that bow together into a
    /// curtain, with a few wiry fertile culms above them.
    /// </summary>
    private static void Veilblade(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bladeCol = Primitives.Mix(VeilBladeCol, c1, 0.28);
        var bladeTip = Primitives.Mix(VeilBladeTip, c2, 0.24);

        // Several fans, each a clump of its own, so the curtain has depth rather than being
        // one even ring of blades.
        int fans = 5;
        for (int fan = 0; fan < fans; fan++)
        {
            double a = fan * 2 * Math.PI / fans + rng.Range(-0.32, 0.32);
            var fanDir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var crown = fanDir * rng.Range(0.03, 0.22);
            int blades = detail == 2 ? 9 : detail == 1 ? 13 : 17;
            for (int k = 0; k < blades; k++)
            {
                // The inner blades of a fan stand up; the outer ones lie further over.
                double age = Math.Abs(k - (blades - 1) * 0.5) / ((blades - 1) * 0.5);
                double az = a + (k - (blades - 1) * 0.5) * rng.Range(0.17, 0.27) + rng.Range(-0.12, 0.12);
                var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                var side = new Vec3(-dir.Z, 0, dir.X);
                double length = rng.Range(1.05, 1.45) * (1.0 - 0.26 * age);
                double halfW = rng.Range(0.026, 0.042) * (1.0 - 0.22 * age);
                var root = crown + side * rng.Range(-0.055, 0.055);

                // The sheathing base: a short collar wrapping the stem the blade springs from.
                Primitives.Tube(m, new[] { root - Vec3.Up * 0.012, root + Vec3.Up * 0.030 },
                    new[] { halfW * 0.52, halfW * 0.44 }, 5,
                    (i, v) => (VeilSheath, 1, i, v, 0, 0));

                VeilBlade(m, root, dir, length, halfW, detail == 2 ? 9 : 15, bladeCol, bladeTip,
                    rng, filaments: detail != 2);
            }
        }

        // The fertile culms: wiry, jointed, and standing clear above the curtain.
        int culms = detail == 2 ? 3 : 5;
        for (int k = 0; k < culms; k++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            var foot = new Vec3(Math.Cos(a) * rng.Range(0.10, 0.45), 0.0, Math.Sin(a) * rng.Range(0.10, 0.45));
            var top = foot + new Vec3(rng.Range(-0.14, 0.14), rng.Range(0.80, 1.26), rng.Range(-0.14, 0.14));
            int rings = 9;
            var path = new Vec3[rings + 1];
            var radii = new double[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                double t = i / (double)rings;
                path[i] = Vec3.Lerp(foot, top, t) + Vec3.Up * (0.022 * Math.Sin(t * Math.PI));
                radii[i] = 0.0085 * (1.0 - 0.60 * t);
            }
            Primitives.Tube(m, path, radii, 6,
                (i, v) => (VeilSheath, 1, i / (double)rings, v, 0, 0));
            // The joints on the culm.
            for (int i = 2; i < rings - 1; i += 3)
            {
                Primitives.Tube(m, new[] { path[i] - Vec3.Up * 0.006, path[i] + Vec3.Up * 0.008 },
                    new[] { radii[i] * 1.18, radii[i] * 1.12 }, 6,
                    (j, v) => (Primitives.Scale(VeilSheath, 0.86), 1, j, v, 0, 0));
            }
            var up = (top - foot).Normalized();
            VeilPanicle(m, top, up, rng.Range(0.16, 0.26), rng.Range(0.075, 0.115), rng);
        }
    }
}
