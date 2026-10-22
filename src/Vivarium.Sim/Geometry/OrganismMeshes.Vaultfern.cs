using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Cycad groundcover for the 'vaultfern' species.
///
/// The plant is authored in the engine's unit space (radius 1 in XZ, height 1 in Y), like
/// the other mid-story forms.
///
/// Anatomy follows a small cycad rather than a tree fern:
///
///   * a stout, BARREL-shaped caudex, armoured in the spiral rhombic leaf scars a cycad
///     keeps from every frond it has ever shed - each scar a raised plate with a boss,
///     and the plates laid on a golden-angle helix so they march around the trunk
///     instead of stacking in a grid;
///   * a crown of stiff, arching PINNATE fronds springing evenly from the caudex crown;
///   * each frond a rachis with a spiny basal petiole and regular opposite pairs of
///     ENTIRE, leathery pinnae - a cycad leaflet is never divided again the way a fern
///     pinna is - each with a raised midrib, overlapping like shingles;
///   * circinate vernation: the newest fronds still stand as tight coiled FIDDLEHEADS,
///     the way a cycad pushes a frond up and only then lets it unfurl.
///
/// The fantastical element is the vault. The real cycad throws its fronds out in a
/// rosette; here the crown's fronds are laid out as the RIBS of a vault, each springing
/// to meet its neighbour so the canopy closes into an arcade of foliage over the scarred
/// caudex. The plant is armoured: the caudex's plates are cold stone, the pinnae a hard
/// blue-green with a pale mineral midrib, and the pale bud sheaths of the fiddleheads
/// are the one bright thing on it.
///
/// Reference renders: scripts/visuals/vaultfern/cycad_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>A hard, cold blue-green: the pinnae's leathery cuticle.</summary>
    private static readonly double[] VaultPinnaCol = { 0.052, 0.098, 0.078 };
    private static readonly double[] VaultPinnaTipCol = { 0.105, 0.165, 0.115 };
    /// <summary>The pale mineral ridge that runs down the middle of every pinna.</summary>
    private static readonly double[] VaultMidrib = { 0.44, 0.47, 0.38 };
    /// <summary>The bud sheath that wraps a coiled young frond.</summary>
    private static readonly double[] VaultSheath = { 0.78, 0.74, 0.63 };

    /// <summary>Where the plume steps sit up a culm, as a fraction of its length.</summary>
    private static readonly double[] VaultRibCount = { 14, 18 };

    /// <summary>
    /// The caudex: a barrel of armour plate, and the frond crown above it.
    /// </summary>
    private static (Vec3 Crown, double CrownRadius) VaultCaudex(MeshData m, Rng rng, double height,
        double rBase, int detail)
    {
        int stations = detail == 2 ? 16 : 28;
        int segments = detail == 2 ? 16 : 32;
        // A cycad trunk shows only a few big leaf scars around any horizontal circle, and
        // they march a golden angle further round with every frond it has shed. Four plates
        // around, sheared by 137.5 degrees per row, is that spiral.
        const int cells = 4;
        const double numRows = 5.0;
        const double shearPerRow = 1.53;
        int start = m.VertexCount;

        // A cycad leans very little, but it is never perfectly straight.
        double leanAz = rng.Range(0, Math.PI * 2);
        var leanDir = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));
        var path = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double v = i / (double)(stations - 1);
            double sway = Math.Sin(Math.PI * v) * rng.Range(0.004, 0.014);
            path[i] = leanDir * sway + Vec3.Up * (height * v);
        }

        // The barrel: broad and slightly flared at the base, drawn in at the shoulder,
        // then domed over at the crown.
        var radii = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            double v = i / (double)(stations - 1);
            if (v < 0.16) radii[i] = rBase * MathD.Lerp(1.14, 0.94, v / 0.16);
            else if (v < 0.80) radii[i] = rBase * (0.88 + 0.10 * Math.Sin((v - 0.16) / 0.64 * Math.PI));
            else radii[i] = rBase * (0.98 - 0.46 * Math.Pow((v - 0.80) / 0.20, 1.4));
        }

        var dir0 = (path[1] - path[0]).Normalized();
        var side = dir0.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = dir0.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        // Cold stone, darker in the groove between plates.
        double[] groove = { 0.052, 0.058, 0.056 };
        double[] plate = { 0.185, 0.198, 0.190 };
        double[] boss = { 0.245, 0.258, 0.245 };

        for (int i = 0; i < stations; i++)
        {
            var dir = i == 0 ? (path[1] - path[0]).Normalized()
                    : i == stations - 1 ? (path[^1] - path[^2]).Normalized()
                    : (path[i + 1] - path[i - 1]).Normalized();
            side = (side - dir * side.Dot(dir)).Normalized();
            var up2 = side.Cross(dir).Normalized();

            double v = i / (double)(stations - 1);
            double stationR = radii[i];

            for (int s = 0; s <= segments; s++)
            {
                // Sample evenly around the ring; the plates are placed in a sheared lattice
                // below, so the ring's own sampling must not fall on a cell edge.
                double u = s / (double)segments;
                double th = 2 * Math.PI * u;

                // The plates spiral: the lattice is sheared by a golden angle for every row
                // of scars, so the armour marches around the trunk instead of banding it.
                double rowCoord = v * numRows;
                double rowF = rowCoord - Math.Floor(rowCoord);
                double sx = u * cells + rowCoord * shearPerRow;
                double f = sx - Math.Floor(sx);
                // Distance to the nearest cell centre, in cell units: 0 at the centre of a
                // plate, half a cell at the groove between two plates.
                double du = Math.Min(f, 1.0 - f);
                double dv = Math.Abs(rowF - 0.5) * 2.0;
                double d = Math.Sqrt(du * du + dv * dv * 0.9) / 0.62;
                double pillow = Math.Clamp(1.0 - d, 0.0, 1.0);
                double relief = 0.13 * Math.Pow(pillow, 0.7);
                double r = stationR * (1.0 + relief);

                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var pos = path[i] + radial * r;
                var normal = radial;

                var col = Primitives.Mix(groove, plate, Math.Min(1.0, pillow * 1.5));
                if (pillow > 0.55) col = Primitives.Mix(col, boss, (pillow - 0.55) * 1.8);
                // A little moss in the grooves at the foot, where a cycad sits in damp shade.
                if (v < 0.14) col = Primitives.Mix(col, new[] { 0.10, 0.14, 0.09 }, (1.0 - v / 0.14) * 0.45);

                // UV2.x = 2.0 marks woody tissue for the flora shader.
                m.AddVertex(pos, normal, col, 1.0, height * v * 3.0, u, 2.0, 0);
            }
        }

        for (int i = 0; i < stations - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * (segments + 1) + s;
                int b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }

        // The domed crown, closed off.
        double apexR = radii[^1];
        var apexCenter = path[^1] + Vec3.Up * (apexR * 0.70);
        int apexIdx = m.AddVertex(apexCenter, Vec3.Up, plate, 1.0, height * 3.0, 0.5, 2.0, 0);
        int topRow = start + (stations - 1) * (segments + 1);
        for (int s = 0; s < segments; s++) m.AddTriangle(topRow + s, topRow + s + 1, apexIdx);

        m.Structural.Add((start, m.VertexCount - start, segments, rBase));
        return (apexCenter, apexR);
    }

    /// <summary>
    /// One pinna: an entire, leathery cycad leaflet with a raised midrib, tapering to a
    /// fine point and swept slightly forward.
    /// </summary>
    private static void VaultPinnaBlade(MeshData m, Vec3 basePoint, Vec3 along, Vec3 across, Vec3 dorsal,
        double length, double halfWidth, ulong seed, double[] c1, double[] c2, int rows)
    {
        var rng = Rng.Keyed(seed, "vaultfern.pinna", 0);
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        double sweep = rng.Range(0.05, 0.16);
        double curve = rng.Range(-0.06, 0.06);

        // Five points across: margin, flank, ridge, flank, margin. The ridge is the midrib.
        double[] sx = { -1.0, -0.44, 0.0, 0.44, 1.0 };
        double[] sy = { 0.10, 0.30, 0.46, 0.30, 0.10 };

        var ids = new int[rows + 1, 5];
        var normals = new Vec3[rows + 1, 5];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            // widest just above the base, then a long taper to the point
            double w = halfWidth * (0.62 + 0.38 * Math.Sin(Math.PI * Math.Min(1.0, 0.16 + 0.84 * t)))
                * Math.Max(0.05, 1.0 - Math.Pow(t, 2.4));
            // the leaflet sweeps forward as it runs out, and arcs slightly
            var centre = basePoint + along * (length * t) + across * (length * sweep * t * t)
                + dorsal * (length * curve * Math.Sin(Math.PI * t));
            for (int k = 0; k < 5; k++)
            {
                var at = centre + across * (sx[k] * w) + dorsal * (sy[k] * w);
                // the midrib ridge is the pale mineral line
                double ridge = k == 2 ? 1.0 : 0.0;
                var col = Primitives.Mix(c1, c2, t);
                col = Primitives.Mix(col, VaultMidrib, ridge * 0.70);
                ids[i, k] = m.AddVertex(at, dorsal, col, 1, t, 0.5 + sx[k] * 0.5, 0, 1);
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
                if (n.LengthSq < 1e-18) n = dorsal;
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

        m.RecordLeaf(leafVertex, leafIndex, basePoint, length);
    }

    /// <summary>
    /// One frond: the vault's rib. A stiff rachis with a spiny basal petiole and regular
    /// opposite pairs of entire pinnae that overlap like shingles.
    /// </summary>
    private static void VaultFrond(MeshData m, Rng rng, ulong seed, Vec3 crown, double az,
        double reach, double lift, int pairs, int detail, double[] c1, double[] c2)
    {
        var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
        var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
        int stations = detail == 2 ? 6 : 9;
        var path = new Vec3[stations];
        var radii = new double[stations];
        for (int s = 0; s < stations; s++)
        {
            double t = s / (double)(stations - 1);
            // The rib leaves the crown steeply and arches over to the tip.
            path[s] = crown + radial * (reach * Math.Pow(t, 0.86))
                + Vec3.Up * (lift * Math.Sin(t * Math.PI * 0.66) - 0.30 * t * t);
            radii[s] = 0.0062 * (1.0 - 0.66 * t) + 0.0016;
        }
        // The rachis wears the plant's own green, not the stem's grey: it is tagged as leaf
        // tissue (UV2.y = 1) so it takes the species pigment like the pinnae it carries.
        Primitives.Tube(m, path, radii, detail == 2 ? 4 : 6,
            (i, v) => (Primitives.Mix(VaultPinnaCol, VaultPinnaTipCol, 0.42), 1, i / (double)(stations - 1), v, 0, 1));

        // The spiny basal petiole. A cycad arms the lower rachis, and the prickles are
        // the first thing a hand meets.
        int spines = detail == 2 ? 3 : 7;
        for (int k = 0; k < spines; k++)
        {
            double t = 0.04 + 0.15 * (k / (double)Math.Max(1, spines - 1));
            int a = Math.Min(stations - 2, (int)(t * stations));
            var node = Vec3.Lerp(path[a], path[a + 1], t * stations - a);
            double sign = (k % 2 == 0) ? 1.0 : -1.0;
            var tip = node + across * (sign * 0.014) - radial * 0.010 - Vec3.Up * 0.004;
            Primitives.Tube(m, new[] { node, tip }, new[] { 0.0032, 0.0004 }, 4,
                (i, v) => (VaultPinnaTipCol, 1, i, v, 0, 0));
        }

        // Regular opposite pairs of entire pinnae. They run from just above the spiny
        // petiole to the very end of the rachis, and they overlap one another, so the
        // frond reads as a solid blade and not a comb of separate teeth.
        int pinnaRows = detail == 2 ? 4 : 6;
        const double pinnaStart = 0.115;
        const double pinnaEnd = 0.99;
        for (int p = 1; p <= pairs; p++)
        {
            double t = pairs <= 1 ? pinnaEnd
                : pinnaStart + (pinnaEnd - pinnaStart) * (p - 1) / (pairs - 1);
            double a = t * (stations - 1);
            int seg = Math.Min(stations - 2, (int)a);
            var node = Vec3.Lerp(path[seg], path[seg + 1], a - seg);
            // Longest about a third of the way along, tapering to both ends. A cycad
            // pinna is a third of its frond.
            double len = (0.150 + 0.115 * Math.Sin(Math.PI * Math.Min(1.0, 0.22 + 0.78 * t)))
                * Math.Pow(1.0 - t, 0.10) * (detail == 2 ? 1.15 : 1.0);
            for (int sign = -1; sign <= 1; sign += 2)
            {
                // The pinna leaves the rachis nearly square, but sweeps forward so it lies
                // along the frond and shingles over its neighbour.
                var dir = (across * sign + radial * rng.Range(0.30, 0.50) - Vec3.Up * rng.Range(0.02, 0.12))
                    .Normalized();
                var pinnaAcross = dir.Cross(Vec3.Up);
                if (pinnaAcross.LengthSq < 1e-10) pinnaAcross = radial;
                pinnaAcross = pinnaAcross.Normalized();
                var pinnaUp = pinnaAcross.Cross(dir).Normalized();
                if (pinnaUp.Y < 0) { pinnaUp = -pinnaUp; pinnaAcross = -pinnaAcross; }
                // A cycad pinna is a strap, not a grass blade: a fifth to a tenth of its
                // length in width, tapering to a fine point.
                double halfW = len * rng.Range(0.052, 0.068);
                VaultPinnaBlade(m, node, dir, pinnaAcross, pinnaUp, len, halfW,
                    Rng.Mix(seed, (ulong)(p * 733 + sign * 13 + 5)), c1, c2, pinnaRows);
            }
        }

        // A terminal leaflet at the very tip of every frond. A cycad frond does not end in
        // a bare point: the tip carries a reduced leaflet that continues the rachis and
        // angles off it, and it is the first thing that reads at distance.
        var tipDir = (path[stations - 1] - path[stations - 2]).Normalized();
        var tipAcross = tipDir.Cross(Vec3.Up);
        if (tipAcross.LengthSq < 1e-10) tipAcross = across;
        tipAcross = tipAcross.Normalized();
        var tipUp = tipAcross.Cross(tipDir).Normalized();
        if (tipUp.Y < 0) { tipUp = -tipUp; tipAcross = -tipAcross; }
        // The terminal leaflet is set at a slight angle to the rachis, so it reads as a
        // leaflet rather than as a thickening of the stem.
        double tipLen = 0.115 * (detail == 2 ? 1.15 : 1.0);
        var tipLeafDir = (tipDir + tipUp * rng.Range(0.20, 0.42)).Normalized();
        var tipLeafAcross = tipLeafDir.Cross(Vec3.Up);
        if (tipLeafAcross.LengthSq < 1e-10) tipLeafAcross = tipAcross;
        tipLeafAcross = tipLeafAcross.Normalized();
        var tipLeafUp = tipLeafAcross.Cross(tipLeafDir).Normalized();
        if (tipLeafUp.Y < 0) { tipLeafUp = -tipLeafUp; tipLeafAcross = -tipLeafAcross; }
        VaultPinnaBlade(m, path[stations - 1] - tipDir * (tipLen * 0.30), tipLeafDir, tipLeafAcross, tipLeafUp,
            tipLen, tipLen * rng.Range(0.050, 0.064),
            Rng.Mix(seed, (ulong)(pairs * 733 + 997)), c1, c2, pinnaRows);
    }

    /// <summary>
    /// A circinate young frond: a tight logarithmic coil on a short upright stub, wrapped
    /// in the pale bud sheath a cycad pushes up around it.
    /// </summary>
    private static void VaultFiddlehead(MeshData m, Vec3 basePoint, Rng rng, double scale, ulong seed)
    {
        double height = 0.30 * scale * rng.Range(0.80, 1.25);
        double r0 = height * 0.30;
        double turns = rng.Range(2.1, 2.8);
        int steps = 18;
        var points = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double a = t * turns * 2 * Math.PI;
            double r = r0 * Math.Exp(-2.0 * t);
            points[i] = basePoint + new Vec3(Math.Cos(a) * r, height * (0.30 + 0.70 * t), Math.Sin(a) * r);
            radii[i] = r * 0.36 + height * 0.006;
        }
        Primitives.Tube(m, points, radii, 5,
            (i, v) => (Primitives.Mix(VaultPinnaCol, VaultPinnaTipCol, 0.35), 1, i / (double)steps, v, 0, 0));

        // The bud sheath: a pale cone wrapping the foot of the coil. It is the one bright
        // thing on the plant, and it is marked with the nodule organ band so it keeps its
        // own colour instead of taking the species' green.
        int rings = 4, sides = 12;
        int start = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double phi = (Math.PI * 0.62) * r / rings;
            for (int s = 0; s <= sides; s++)
            {
                double th = 2 * Math.PI * s / sides;
                double rr = r0 * 0.95 * Math.Cos(phi);
                var p = basePoint + new Vec3(Math.Cos(th) * rr, Math.Sin(phi) * height * 0.46, Math.Sin(th) * rr);
                m.AddVertex(p, Vec3.Up, VaultSheath, 1, 0, 0.5, 1.35, 1);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + r * (sides + 1) + s;
                int b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    /// <summary>
    /// The vaultfern: a scarred caudex under a crown of arching fronds, with the newest
    /// fronds still coiled at the top.
    /// </summary>
    private static void Vaultfern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var pinnaBase = Primitives.Mix(VaultPinnaCol, c1, 0.22);
        var pinnaTip = Primitives.Mix(VaultPinnaTipCol, c2, 0.26);

        // A short trunk, as a cycad keeps: the armour below, the vault above.
        double height = juvenile ? 0.26 : rng.Range(0.42, 0.52);
        double rBase = juvenile ? 0.082 : rng.Range(0.125, 0.152);
        var (crown, crownR) = VaultCaudex(m, rng, height, rBase, detail);

        // The crown's fronds are the ribs of a vault: they spring to the same ring and meet
        // their neighbours, so the canopy closes into an arcade rather than a rosette.
        int fronds = juvenile ? 6 : (int)rng.Range(VaultRibCount[0], VaultRibCount[1] + 1);
        int pairs = juvenile ? 13 : (detail == 2 ? 20 : 30);
        for (int f = 0; f < fronds; f++)
        {
            double az = 2 * Math.PI * f / fronds + rng.Range(-0.08, 0.08);
            // every rib reaches about the same ring, so their tips close the vault
            double reach = juvenile ? rng.Range(0.44, 0.58) : rng.Range(0.80, 0.94);
            double lift = juvenile ? rng.Range(0.16, 0.26) : rng.Range(0.34, 0.48);
            var root = crown - Vec3.Up * (crownR * 0.30) + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (crownR * 0.80);
            VaultFrond(m, rng, Rng.Mix(seed, (ulong)(f * 911 + 17)), root, az, reach, lift,
                pairs, detail, pinnaBase, pinnaTip);
        }

        // The newest fronds, still coiled, at the crown.
        int heads = juvenile ? 1 : 3;
        for (int i = 0; i < heads; i++)
        {
            double az = 2 * Math.PI * i / heads + rng.Range(-0.3, 0.3);
            var at = crown + new Vec3(Math.Cos(az), -0.01, Math.Sin(az)) * (crownR * 0.35);
            VaultFiddlehead(m, at, rng, juvenile ? 0.7 : 1.0, Rng.Mix(seed, (ulong)(i * 733 + 3)));
        }
    }
}
