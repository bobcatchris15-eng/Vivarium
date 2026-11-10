using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New climber nodes for the 2026-11-06 expansion: Shadewrap (trunk-wrapping
/// shade climber), Bitterbine (vigorous bitter-cone twiner) and Trumpetbind
/// (wood-edge twiner with 2-lipped tubular trumpets). Climber node meshes are
/// authored in real metres in the node frame (Mesh X across the stem, Mesh Y
/// along the stem, Mesh Z the stem's upper side) and placed unscaled by the
/// renderer, so no unit conversion happens here. All colours derive from c1/c2
/// only. Renames per docs/2026-11-06-flora-fiction-rename.md.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>Short stem stub along +Y with a visible S-curve; Tube records the structural span.
    /// Five stations alternate the offset sign and return to the axis at both ends, so the stub reads
    /// as a twining snake that still meets its neighbouring internodes, and the foliage that carries
    /// the twist reads as a twiner rather than a straight rod.</summary>
    private static Vec3[] NewVineStemStub(MeshData m, Rng rng, double halfLen, double rBase, double rTip, double[] stemCol, double zigzag = 0.0)
    {
        double[] ts = { 0.0, 0.25, 0.5, 0.75, 1.0 };
        double[] offs = { 0.0, zigzag, 0.0, -zigzag * 0.8, 0.0 };
        var pts = new Vec3[5];
        var rads = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double t = ts[i];
            pts[i] = new Vec3(
                offs[i] + rng.Range(-0.0015, 0.0015),
                MathD.Lerp(-halfLen, halfLen, t),
                rng.Range(0.0, 0.003) * Math.Sin(Math.PI * t));
            rads[i] = MathD.Lerp(rBase, rTip, t);
        }
        Primitives.Tube(m, pts, rads, 6, (i, v) => (stemCol, 1.0, i / 4.0, v, 0, 0));
        return pts;
    }

    private static Vec3 NewVineStemPoint(Vec3[] path, double y)
    {
        double t = MathD.Clamp01((y - path[0].Y) / (path[^1].Y - path[0].Y)) * (path.Length - 1);
        int i = Math.Min((int)t, path.Length - 2);
        return Vec3.Lerp(path[i], path[i + 1], t - i);
    }

    /// <summary>Thin petiole from the stem surface to the leaf base; the tube records its own structural span.</summary>
    private static void NewVinePetiole(MeshData m, Vec3 from, Vec3 to, double r0, double r1, double[] col)
    {
        var mid = Vec3.Lerp(from, to, 0.5) + new Vec3(0, 0, 0.0025);
        Primitives.Tube(m, new[] { from, mid, to }, new[] { r0, (r0 + r1) * 0.5, r1 }, 5,
            (i, v) => (col, 1.0, i / 2.0, v, 0, 0));
    }

    /// <summary>
    /// A lobed leaf built from cheap lofted sub-blades: a central acute lobe plus a deep
    /// spreading pair (or two pairs for the 5-lobed form), so the saw-toothed silhouette
    /// reads at node scale. The pointed width profile is in the <paramref name="fwd"/> /
    /// <paramref name="norm"/> plane — attached Shadewrap leaves are called with
    /// <paramref name="fwd"/> in the trunk tangent plane and <paramref name="norm"/> =
    /// outward, so the lamina lies flat against the trunk and shows its full lobed
    /// silhouette to a front camera instead of pointing at it.
    /// </summary>
    private static void NewVineLobedLeaf(MeshData m, Rng rng, Vec3 petioleEnd, Vec3 fwd, Vec3 norm,
        double mainLen, double lobes, double[] baseCol, double[] tipCol, double[] underCol)
    {
        var f = fwd.Normalized();
        var n = norm.Normalized();
        if (n.LengthSq < 1e-8) n = new Vec3(0, 0, 1);
        double lobeProfile(double t) => Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.72))), 0.85);
        double[] Colour(double t) => Primitives.Mix(baseCol, tipCol, t);
        double mainHalfW = mainLen * 0.42;

        // Central lobe.
        NewFloraBlade(m, petioleEnd, f, n, mainLen, mainHalfW, lobeProfile, Colour,
            rows: 5, sides: 7, flatness: 0.35, keel: 0.06, cup: 0.10, camber: 0.10, tipCurl: 0.08);

        // Spreading side lobes: a deep pair, and for the 5-lobed form a denser inner pair.
        double[] angles = lobes >= 4.5 ? new[] { -1.05, -0.55, 0.55, 1.05 } : new[] { -0.92, 0.92 };
        foreach (double a in angles)
        {
            bool outer = Math.Abs(a) > 0.9;
            var latDir = Axis.RotateAround(f, n, a + rng.Range(-0.04, 0.04));
            double latLen = mainLen * (outer ? 0.78 : 0.92);
            NewFloraBlade(m, petioleEnd, latDir, n, latLen, mainHalfW * 0.92, lobeProfile, Colour,
                rows: 4, sides: 6, flatness: 0.35, keel: 0.06, cup: 0.10, camber: 0.10, tipCurl: 0.08);
        }
    }

    /// <summary>
    /// One coarse lobed leaf (central plus a deep spreading pair) for Bitterbine: lofted blades with
    /// a serrated margin, so the saw-toothed silhouette reads at node scale.
    /// </summary>
    private static void NewVineSerratedLeaf(MeshData m, Rng rng, Vec3 petioleEnd, Vec3 fwd, Vec3 norm,
        double mainLen, double[] baseCol, double[] tipCol)
    {
        var f = fwd.Normalized();
        var n = norm.Normalized();
        double lobe(double t) => Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.72))), 0.85);
        double[] Colour(double t) => Primitives.Mix(baseCol, tipCol, t);
        double[] midrib = Primitives.Mix(baseCol, tipCol, 0.30);

        // Central lobe.
        NewFloraBlade(m, petioleEnd, f, n, mainLen, mainLen * 0.40, lobe, Colour,
            rows: 6, sides: 7, flatness: 0.28, keel: 0.06, cup: 0.10, camber: 0.12, tipCurl: 0.10,
            serration: 0.80, teeth: 18.0, midrib: midrib, midribWidth: 0.25);

        // Deep spreading pair: narrow, short, and sharply toothed.
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            var latDir = Axis.RotateAround(f, n, sign * 0.92 + rng.Range(-0.04, 0.04));
            double latLen = mainLen * 0.84;
            NewFloraBlade(m, petioleEnd, latDir, n, latLen, latLen * 0.36, lobe, Colour,
                rows: 5, sides: 6, flatness: 0.28, keel: 0.06, cup: 0.12, camber: 0.12, tipCurl: 0.14,
                serration: 0.85, teeth: 16.0, midrib: midrib, midribWidth: 0.25);
        }
    }

    private static void NewVineTrumpet(MeshData mesh, Vec3 root, Vec3 direction, double length, double width,
        double[] throat, double[] rim)
    {
        int first = mesh.VertexCount;
        int sides = mesh.FloraDetailLevel is 2 ? 8 : 12;
        var side = direction.Cross(Vec3.Up).Normalized();
        var crown = side.Cross(direction).Normalized();
        double[] stations = { 0.0, 0.45, 0.78, 1.0 };
        double[] radii = { 0.17, 0.23, 0.43, 1.0 };
        for (int row = 0; row < stations.Length; row++)
        {
            double t = stations[row];
            for (int j = 0; j <= sides; j++)
            {
                double angle = Math.Tau * j / sides;
                var radial = side * Math.Cos(angle) + crown * Math.Sin(angle);
                double rimWave = Math.Sin(angle * 3.0) * 0.14 + Math.Sin(angle) * 0.10;
                double r = width * radii[row] * (1.0 + rimWave * t * t * t);
                var centre = root + direction * (length * t) - Vec3.Up * (length * 0.12 * t * t);
                var position = centre + radial * r + direction * (width * rimWave * t * t * t);
                var normal = (radial - direction * (t * t * 0.47)).Normalized();
                mesh.AddVertex(position, normal, Primitives.Mix(throat, rim, t), 1, t, j / (double)sides, 0, 0);
            }
        }
        int stride = sides + 1;
        for (int row = 0; row < stations.Length - 1; row++)
        for (int j = 0; j < sides; j++)
        {
            int a = first + row * stride + j;
            int b = a + stride;
            var normal = (mesh.NormalAt(a) + mesh.NormalAt(a + 1)).Normalized();
            Primitives.TriangleFacing(mesh, a, b, a + 1, normal);
            Primitives.TriangleFacing(mesh, a + 1, b, b + 1, normal);
        }
        int count = mesh.VertexCount - first;
        NewFloraMarkOrgan(mesh, first, count);
        mesh.EssentialSpans.Add((first, count));
    }

    /// <summary>Overlapping lobed foliage with fine roots along the supporting surface.</summary>
    private static void ShadewrapNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        // Vertex colours brightened ~40% to compensate for shader darkening.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.30), 1.40);
        var petioleCol = Primitives.Scale(Primitives.Mix(stemCol, c1, 0.40), 1.10);
        var rootCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.55), 1.40); // pale misty green-grey, never brown
        var leafBase = Primitives.Scale(c1, 1.40); // glossy dark lamina, brightened ~40%
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.20), 1.40);
        var underCol = Primitives.Scale(Primitives.Mix(Primitives.Scale(c2, 0.85), c1, 0.45), 1.40); // kept for signature

        double halfLen = attached ? 0.058 : 0.062;
        var path = NewVineStemStub(m, rng, halfLen, 0.0028, 0.0020, stemCol, zigzag: attached ? 0.004 : 0.008);

        int leaves = NewFloraCount(m, 5 + rng.NextInt(3));
        for (int i = 0; i < leaves; i++)
        {
            double frac = leaves == 1 ? 0.5 : (double)i / (leaves - 1);
            double y = MathD.Lerp(-halfLen * 0.85, halfLen * 0.85, frac);
            double sign = i % 2 == 0 ? -1.0 : 1.0;
            var stemPt = NewVineStemPoint(path, y);
            // Attached leaves appressed to the trunk: fwd lies in the tangent (X/Y)
            // plane with only a slight outward lift, so the full lobed lamina faces
            // the viewer and overlaps the next leaf up the stem in a shingle.
            // Runners lift clear and face up. Each leaf carries a small alternating
            // yaw twist, so successive leaves yaw left/right as they climb.
            var rawDir = attached
                ? new Vec3(sign * rng.Range(0.42, 0.56), rng.Range(0.62, 0.76), rng.Range(0.18, 0.30)).Normalized()
                : new Vec3(sign * rng.Range(0.40, 0.54), rng.Range(0.34, 0.44), rng.Range(0.62, 0.74)).Normalized();
            double twist = (i % 2 == 0 ? -1.0 : 1.0) * rng.Range(0.08, 0.16);
            var lDir = Axis.RotateAround(rawDir, Vec3.Up, twist);
            double petLen = (attached ? rng.Range(0.008, 0.016) : rng.Range(0.020, 0.032)) * (1.20 - frac * 0.40);
            var petEnd = stemPt + lDir * petLen + new Vec3(0, 0, rng.Range(0.0, 0.002)); // staggered shingle layers
            NewVinePetiole(m, stemPt, petEnd, 0.0018, 0.0012, petioleCol);
            double len = rng.Range(0.055, 0.085) * (1.18 - frac * 0.32);
            double lobes = 5.0; // Wave 9: glossy 5-lobed signature
            NewVineLobedLeaf(m, rng, petEnd, lDir, new Vec3(0, 0, 1), len, lobes, leafBase, leafTip, underCol);
        }

        if (attached)
        {
            int roots = NewFloraCount(m, 16);
            for (int i = 0; i < roots; i++)
            {
                double y = MathD.Lerp(-halfLen, halfLen, (i + rng.Range(0.1, 0.9)) / roots);
                var origin = NewVineStemPoint(path, y);
                double splay = rng.Range(0.004, 0.013) * (i % 2 == 0 ? -1 : 1);
                var mid = origin + new Vec3(splay * 0.42, -0.004, -0.001);
                var tip = origin + new Vec3(splay, -rng.Range(0.006, 0.016), -0.003);
                Primitives.Tube(m, new[] { origin, mid, tip }, new[] { 0.00060, 0.00035, 0.00012 }, 6,
                    (station, v) => (rootCol, 1.0, station * 0.5, v, 0, 0));
            }
        }
    }

    /// <summary>Opposite serrated leaves on a twining shoot with pendent papery cones.</summary>
    private static void BitterbineNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        // Vertex colours brightened ~40% to compensate for shader darkening.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.20), 1.40);
        var petioleCol = Primitives.Scale(Primitives.Mix(stemCol, c1, 0.40), 1.10);
        var leafBase = Primitives.Scale(c1, 1.40); // coarse serrated green, brightened ~40%
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.38), 1.35); // yellow-green tip, lifts the dark mat
        // Papery pale straw cone: c2-weighted straw so it reads pale hop-straw, not
        // brown; brightened ~40% to pop against a dark trunk without clipping.
        var coneCol = Primitives.Scale(Primitives.Mix(c2, c1, 0.72), 1.40);
        var coneTipCol = Primitives.Scale(Primitives.Mix(coneCol, c1, 0.18), 1.05);
        var coneCoreCol = Primitives.Scale(coneCol, 0.82); // shadowed cone core
        var bristleCol = Primitives.Scale(stemCol, 0.80);

        double halfLen = 0.070;
        var path = NewVineStemStub(m, rng, halfLen, 0.0030, 0.0020, stemCol, zigzag: attached ? 0.008 : 0.009);

        // Short stem bristles for a hairy twiner surface.
        int bristles = NewFloraCount(m, 20);
        for (int b = 0; b < bristles; b++)
        {
            var bBase = new Vec3(rng.Range(-0.003, 0.003), rng.Range(-halfLen * 0.9, halfLen * 0.9), 0.003);
            var bDir = new Vec3(rng.Range(-0.7, 0.7), rng.Range(-0.2, 0.2), rng.Range(0.5, 0.9)).Normalized();
            var bTip = bBase + bDir * rng.Range(0.0015, 0.0028);
            Primitives.Tube(m, new[] { bBase, bTip }, new[] { 0.0007, 0.00025 }, 4,
                (i, v) => (bristleCol, 1.0, i, v, 0, 0));
        }

        int stations = NewFloraCount(m, 3 + rng.NextInt(2)); // Wave 9: 3-4 stations = 6-8 leaves
        int coneStations = NewFloraCount(m, attached ? 4 + rng.NextInt(2) : 3 + rng.NextInt(2)); // attached 4-5, runner 3-4
        for (int s = 0; s < stations; s++)
        {
            double y = stations == 1 ? 0.0 : MathD.Lerp(-halfLen * 0.65, halfLen * 0.65, (double)s / (stations - 1));
            // Each succeeding pair is twisted about the stem so the foliage
            // spirals instead of fanning flat into a mat.
            double pairYaw = (s % 2 == 0 ? -1.0 : 1.0) * rng.Range(0.20, 0.35);
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var stemPt = NewVineStemPoint(path, y);
                // Attached pairs spread in the trunk plane with a modest outward
                // lift (full silhouette); runners stand up off the shoot.
                var rawDir = attached
                    ? new Vec3(sign * rng.Range(0.70, 0.86), rng.Range(0.15, 0.35), rng.Range(0.22, 0.38)).Normalized()
                    : new Vec3(sign * rng.Range(0.55, 0.68), rng.Range(0.18, 0.28), rng.Range(0.62, 0.72)).Normalized();
                var lDir = Axis.RotateAround(rawDir, Vec3.Up, pairYaw);
                double petLen = rng.Range(0.020, 0.032);
                var petEnd = stemPt + lDir * petLen;
                NewVinePetiole(m, stemPt, petEnd, 0.0028, 0.0018, petioleCol);
                double len = rng.Range(0.08, 0.11); // Wave 9: 8-11 cm coarse blades
                NewVineSerratedLeaf(m, rng, petEnd, lDir, new Vec3(0, 0, 1), len, leafBase, leafTip);
            }

            // Papery cones in the axils, hanging loose below the foliage on a
            // short peduncle so they clear the leaves. The last station carries
            // a second cone on the far side when the count asks for it.
            int conesHere = (s == stations - 1 || (s == 0 && rng.NextDouble() < 0.45)) ? 1 : 0;
            for (int cIdx = 0; cIdx < conesHere; cIdx++)
            {
                double csign = s % 2 == 0 ? -1.0 : 1.0;
                var coneDir = new Vec3(csign * rng.Range(0.30, 0.52), -0.78, rng.Range(0.25, 0.38)).Normalized();
                var axil = NewVineStemPoint(path, y);
                var coneBase = axil + new Vec3(csign * 0.018, -0.009, 0.020);
                NewVinePetiole(m, axil, coneBase, 0.0020, 0.0014, petioleCol);
                int rings = m.FloraDetailLevel is 2 ? 3 : 4;
                int segs = m.FloraDetailLevel is 2 ? 5 : 6;
                Primitives.Ellipsoid(m, coneBase + coneDir * 0.020, new Vec3(0.012, 0.012, 0.012),
                    Math.Max(3, rings), segs, (u, v) => (coneCoreCol, 1.0, u, v, 0, 0));
                int organStart = m.VertexCount;
                // Plump 4.5-6.0 cm cone, 2.2-2.8 cm wide — shingled papery straw.
                int whorls = m.FloraDetailLevel is 2 ? 3 : 4;
                NewFloraScaleColumn(m, coneBase, coneDir, rng.Range(0.045, 0.060), rng.Range(0.011, 0.014), whorls, 5, rng.Range(0.013, 0.017), rng.Range(0.008, 0.011),
                    (wt, t) => Primitives.Mix(coneCol, coneTipCol, t), rng, 1.0, 0.5, true, false);
                if (m.VertexCount > organStart)
                    m.EssentialSpans.Add((organStart, m.VertexCount - organStart));
            }
        }
    }

    /// <summary>Paired oval leaves and alternating axillary bells with open throats.</summary>
    private static void TrumpetbindNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        // Vertex colours brightened ~40% to compensate for shader darkening
        // (cream c2 parts only 1.05-1.15 — 1.4 would clip the near-white to white).
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.20), 1.40);
        var petioleCol = Primitives.Scale(Primitives.Mix(stemCol, c1, 0.40), 1.10);
        // Deep saturated green, brightened ~40%. Tip stays c1-weighted (only a
        // little c2) so the lamina never washes to pale cabbage-green.
        var leafBase = Primitives.Scale(c1, 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.12), 1.40);
        var underCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.18), 1.30);
        var throatCol = Primitives.Scale(Primitives.Mix(c2, c1, 0.10), 1.15); // cream-pink, c2 strongly

        double halfLen = 0.060;
        var path = NewVineStemStub(m, rng, halfLen, 0.0040, 0.0028, stemCol, zigzag: 0.006);

        int stations = NewFloraCount(m, 2 + rng.NextInt(2));
        for (int s = 0; s < stations; s++)
        {
            double y = stations == 1 ? 0.0 : MathD.Lerp(-halfLen * 0.65, halfLen * 0.65, (double)s / (stations - 1));
            // Each succeeding pair is twisted about the stem so the foliage
            // spirals instead of fanning flat into a lettuce rosette.
            double pairYaw = (s % 2 == 0 ? -1.0 : 1.0) * rng.Range(0.22, 0.32);
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var stemPt = NewVineStemPoint(path, y);
                // Attached pairs spread in the trunk plane with a modest outward
                // lift so the oval blades show head-on; runners stand up.
                var rawDir = attached
                    ? new Vec3(sign * rng.Range(0.62, 0.74), rng.Range(0.30, 0.42), rng.Range(0.38, 0.50)).Normalized()
                    : new Vec3(sign * rng.Range(0.55, 0.68), rng.Range(0.20, 0.30), rng.Range(0.62, 0.72)).Normalized();
                var lDir = Axis.RotateAround(rawDir, Vec3.Up, pairYaw);
                double petLen = rng.Range(0.016, 0.026);
                var petEnd = stemPt + lDir * petLen;
                NewVinePetiole(m, stemPt, petEnd, 0.0025, 0.0016, petioleCol);
                double len = rng.Range(0.050, 0.075);
                double halfW = len * 0.38; // simple oval, not a broad round lettuce leaf
                var side = lDir.Cross(new Vec3(0, 0, 1)).Normalized();
                if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, petEnd, petEnd + lDir * len, side, halfW,
                    leafBase, leafTip, camber: 0.12, curl: 0.12, shoulder: 0.85, segments: 3);
                for (int v = bladeStart; v < m.VertexCount; v++)
                    if (m.UV2[v * 2] > 0.5f) m.SetColor(v, underCol[0], underCol[1], underCol[2], 1.0);
            }

            if (s == 0 && stations > 1 && rng.NextDouble() < 0.6) continue;
            double signOfCluster = s % 2 == 0 ? -1.0 : 1.0;
            var flowerDir = new Vec3(signOfCluster * rng.Range(0.30, 0.58),
                rng.Range(-0.30, 0.17), rng.Range(0.66, 0.90)).Normalized();
            var axil = NewVineStemPoint(path, y);
            var throat = axil + flowerDir * rng.Range(0.009, 0.017);
            NewVinePetiole(m, axil, throat, 0.0014, 0.0010, petioleCol);
            double length = rng.Range(0.034, 0.049);
            double width = rng.Range(0.011, 0.016);
            var rim = Primitives.Mix(c2, new[] { 1.0, 0.83, 0.74 }, rng.Range(0.15, 0.38));
            NewVineTrumpet(m, throat, flowerDir, length, width, throatCol, rim);
        }
    }
}
