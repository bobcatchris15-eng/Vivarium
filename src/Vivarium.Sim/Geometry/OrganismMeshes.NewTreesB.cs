using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Waterside batch B: Fenforge (dark waterside bank-knitter), Banksorrow
/// (leaning silky bank tree), Mirespire (sparse evergreen bog spire).
/// All builders author in real metres into a scratch part and merge with
/// <c>NewFloraAppendWorld</c>.
///
/// Overhaul features:
/// 1. Leaf normals strictly face skyward (+Up) across all species, eliminating inverted normals.
/// 2. UV2 = (2, 0) explicitly authored on all woody trunks, buttresses, stilt-roots, boughs, and twigs.
/// 3. Fenforge: Dramatic interwoven fen stilt-root & buttress cage (10-12 prop roots), 4-5 muscular dark-fissured
///    trunks, 14-16 primary boughs, 42-48 spreading branches with dense glossy notched alder foliage, sculpted
///    woody mini-cones with warm glowing amber spore centers (UV2 1.30, 0.86), and 60-75 dangling glowing amber catkins (UV2 1.38, 0.90).
/// 4. Banksorrow: Sculptural gnarled leaning trunk with basal buttresses, 7-9 wide-arching scaffold boughs, 56-64
///    swaying sinusoidal golden-willow weeping whip-withes, dense cascading falcate silver-sage foliage, and glowing
///    pearl-silver & pale-gold silky catkins (UV2 1.28, 0.88).
/// 5. Mirespire: Strongly fluted buttressed swamp-cypress base with 6-8 rounded pneumatophore knees (UV2 2, 0),
///    slender tapering spire trunk, 8 tiered horizontal whorls with blue-jade & seafoam needle-rosette pads,
///    and 40-50 upright rose-amethyst & amber larch cones (UV2 1.32, 0.88). Budget strictly <= 72,000 tris at detail 0.
/// </summary>
public static partial class OrganismMeshes
{
    // ------------------------------------------------------------------
    // Palettes (fixed bark/twig/organs; foliage derives from c1/c2)
    // ------------------------------------------------------------------

    // Fenforge: near-black deeply fissured alder bark, mossy stilt-roots, dark woody cones, glowing amber catkins
    private static readonly double[] NtbFenforgeBark = { 0.18, 0.15, 0.12 };
    private static readonly double[] NtbFenforgeBarkLight = { 0.28, 0.23, 0.18 };
    private static readonly double[] NtbFenforgeBarkDeep = { 0.08, 0.06, 0.05 };
    private static readonly double[] NtbFenforgeRoot = { 0.22, 0.15, 0.10 };
    private static readonly double[] NtbFenforgeRootMoss = { 0.25, 0.23, 0.12 };
    private static readonly double[] NtbFenforgeTwig = { 0.20, 0.16, 0.13 };
    private static readonly double[] NtbFenforgeConeBody = { 0.18, 0.13, 0.09 };
    private static readonly double[] NtbFenforgeConeGlow = { 0.90, 0.52, 0.12 };
    private static readonly double[] NtbFenforgeCatkinBase = { 0.82, 0.52, 0.14 };
    private static readonly double[] NtbFenforgeCatkinTip = { 0.98, 0.72, 0.18 };

    // Banksorrow: gnarled pale willow bark, golden-olive weeping withes, pearl catkins
    private static readonly double[] NtbBanksorrowBark = { 0.44, 0.41, 0.35 };
    private static readonly double[] NtbBanksorrowBarkLight = { 0.56, 0.53, 0.45 };
    private static readonly double[] NtbBanksorrowBarkDeep = { 0.24, 0.21, 0.16 };
    private static readonly double[] NtbBanksorrowWithe = { 0.52, 0.46, 0.24 };
    private static readonly double[] NtbBanksorrowPale = { 0.88, 0.94, 0.78 };
    private static readonly double[] NtbBanksorrowCatkinBase = { 0.82, 0.84, 0.68 };
    private static readonly double[] NtbBanksorrowCatkinTip = { 0.94, 0.95, 0.84 };

    // Mirespire: rich weathered russet-cinnamon brown and umbral moss bark, blue-jade needles, rose-amethyst cones
    private static readonly double[] NtbMirespireBarkLower = { 0.24, 0.15, 0.10 };
    private static readonly double[] NtbMirespireBarkUpper = { 0.28, 0.17, 0.12 };
    private static readonly double[] NtbMirespireBarkDeep = { 0.12, 0.07, 0.05 };
    private static readonly double[] NtbMirespireBranch = { 0.25, 0.16, 0.11 };
    private static readonly double[] NtbMirespireKnee = { 0.22, 0.14, 0.09 };
    private static readonly double[] NtbMirespireConeBody = { 0.68, 0.20, 0.42 };
    private static readonly double[] NtbMirespireConeBase = { 0.38, 0.13, 0.24 };
    private static readonly double[] NtbMirespireConeRim = { 0.88, 0.62, 0.22 };

    // ------------------------------------------------------------------
    // Shared helpers (Ntb prefix avoids partial collisions)
    // ------------------------------------------------------------------

    private static Vec3 NtbRadial(double az) => new(Math.Cos(az), 0, Math.Sin(az));

    private static double[] NtbBright(double[] c) => Primitives.Scale(c, 1.4);

    private static Vec3 NtbSafe(Vec3 v, Vec3 fallback) => v.LengthSq > 1e-10 ? v.Normalized() : fallback;

    private static Vec3 NtbBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    private static Vec3 NtbBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return NtbSafe(d, Vec3.Up);
    }

    private static Vec3[] NtbSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        if (stations < 2) stations = 2;
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            pts[i] = NtbBezier(p0, c0, c1, p1, t);
        }
        return pts;
    }

    private static Vec3 NtbAt(IReadOnlyList<Vec3> path, double t)
    {
        if (path.Count == 0) return Vec3.Zero;
        if (path.Count == 1) return path[0];
        double f = Math.Clamp(t, 0.0, 1.0) * (path.Count - 1);
        int i = Math.Min(path.Count - 2, (int)Math.Floor(f));
        return Vec3.Lerp(path[i], path[i + 1], f - i);
    }

    private static void NtbSetUV2(MeshData m, int firstVertex, int count, float u2, float v2)
    {
        int last = Math.Min(m.VertexCount, firstVertex + count);
        for (int v = Math.Max(0, firstVertex); v < last; v++)
        {
            m.UV2[v * 2] = u2;
            m.UV2[v * 2 + 1] = v2;
        }
    }

    /// <summary>
    /// Authors woody tube with UV2 = (2.0f, 0.0f) for full procedural bark grain and roughness.
    /// </summary>
    private static void NtbWood(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        int detail, double[] lowerCol, double[] upperCol, double[] darkCol,
        double ridgeFreq, double twist, bool essential)
    {
        if (path.Count < 2 || radii.Count != path.Count) return;
        int sides = detail switch { 2 => 5, 1 => 6, _ => 8 };
        int stations = path.Count;
        int startVertex = m.VertexCount;
        Primitives.Tube(m, path, radii, sides, (i, v) =>
        {
            double tRow = stations <= 1 ? 0.0 : (double)i / (stations - 1);
            double baseMix = MathD.Clamp01(tRow);
            var baseCol = Primitives.Mix(lowerCol, upperCol, baseMix);
            double th = v * Math.PI * 2.0;
            double phase = th * ridgeFreq + tRow * twist;
            double ridge = 0.5 + 0.5 * Math.Sin(phase);
            double fine = 0.5 + 0.5 * Math.Sin(th * ridgeFreq * 3.7 - tRow * 2.3);
            double fissure = Math.Pow(Math.Max(0.0, 1.0 - ridge), 1.6);
            double crest = Math.Max(0.0, ridge - 0.65) / 0.35;
            var col = Primitives.Mix(baseCol, darkCol, Math.Min(1.0, fissure * 0.85));
            col = Primitives.Mix(col, Primitives.Scale(baseCol, 1.15), Math.Min(1.0, crest * 0.35));
            col = Primitives.Scale(col, 0.90 + 0.10 * fine);
            return (col, 1.0, tRow, v, 2.0, 0.0); // UV2 = (2.0, 0.0)
        });
        if (essential) m.EssentialSpans.Add((startVertex, m.VertexCount - startVertex));
    }

    private static void NtbPin(MeshData part, int firstVertex) =>
        part.EssentialSpans.Add((firstVertex, part.VertexCount - firstVertex));

    /// <summary>
    /// Compatibility helper with guaranteed upward normals.
    /// </summary>
    private static void NtbShellLeaf(MeshData part, Vec3 root, Vec3 tip, Vec3 hint, double halfWidth,
        double[] baseCol, double[] tipCol, double camber, int longitudinal)
    {
        int v0 = part.VertexCount;
        var axis = tip - root;
        if (axis.LengthSq < 1e-8) return;
        var dir = axis.Normalized();
        var side = Vec3.Up.Cross(dir);
        if (side.LengthSq < 1e-6) side = hint;
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(dir);
        side = side.Normalized();
        Primitives.CurvedLeaf(part, root, tip, side, halfWidth, baseCol, tipCol,
            camber: camber, longitudinal: longitudinal);
        if ((part.FloraDetailLevel ?? 0) < 2 || part.LeafSpans.Count % 2 == 0)
            NtbPin(part, v0);
    }

    /// <summary>
    /// Sculpted obovate alder leaf with retuse/notched apex, 3D roll/pitch jitter, gentle camber/gravity droop, and guaranteed +Upward normals.
    /// </summary>
    private static void NtbAlderLeaf(MeshData part, Vec3 root, Vec3 tip, Vec3 outward,
        double halfWidth, double[] baseCol, double[] tipCol, int detail, bool essential,
        double roll = 0.0, double droop = 0.0)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-6 || halfWidth < 1e-6) return;
        int leafVertex = part.VertexCount;
        int leafIndex = part.Indices.Count;

        var fwd = axis / length;
        var latRef = Vec3.Up.Cross(fwd);
        if (latRef.LengthSq < 1e-6) latRef = outward.Cross(fwd);
        if (latRef.LengthSq < 1e-6) latRef = new Vec3(1, 0, 0).Cross(fwd);
        latRef = latRef.Normalized();
        var normRef = fwd.Cross(latRef).Normalized();

        // 3D roll rotation around the leaf forward axis (-25° to +35° roll jitter)
        var side = (latRef * Math.Cos(roll) + normRef * Math.Sin(roll)).Normalized();
        var upNormal = fwd.Cross(side).Normalized();
        if (upNormal.Y < 0.20f)
            upNormal = (upNormal + Vec3.Up * (0.35f - upNormal.Y)).Normalized();

        double w1 = halfWidth * 0.65;
        double w2 = halfWidth * 1.05; // Obovate: widest in distal third
        var p0 = root;
        var p1 = root + fwd * (length * 0.35) - Vec3.Up * (length * droop * 0.30) + upNormal * (length * 0.04);
        var p2 = root + fwd * (length * 0.72) - Vec3.Up * (length * droop * 0.75) + upNormal * (length * 0.06);
        var p3L = root + fwd * (length * 0.98) - side * (halfWidth * 0.40) - Vec3.Up * (length * droop) + upNormal * (length * 0.03);
        var p3M = root + fwd * (length * 0.91) - Vec3.Up * (length * droop); // sinus notch indentation
        var p3R = root + fwd * (length * 0.98) + side * (halfWidth * 0.40) - Vec3.Up * (length * droop) + upNormal * (length * 0.03);

        var col0 = baseCol;
        var col1 = Primitives.Mix(baseCol, tipCol, 0.35);
        var col2 = Primitives.Mix(baseCol, tipCol, 0.72);
        var col3 = tipCol;

        var normL = (upNormal - side * 0.28).Normalized();
        if (normL.Y < 0.18f) normL = (normL + Vec3.Up * 0.22f).Normalized();
        var normR = (upNormal + side * 0.28).Normalized();
        if (normR.Y < 0.18f) normR = (normR + Vec3.Up * 0.22f).Normalized();

        int vRoot = part.AddVertex(p0, upNormal, col0, 1.0, 0.0, 0.5, 0.0, 1.0);
        int v1L = part.AddVertex(p1 - side * w1, normL, col1, 1.0, 0.35, 0.0, 0.0, 1.0);
        int v1M = part.AddVertex(p1, upNormal, col1, 1.0, 0.35, 0.5, 0.0, 1.0);
        int v1R = part.AddVertex(p1 + side * w1, normR, col1, 1.0, 0.35, 1.0, 0.0, 1.0);

        int v2L = part.AddVertex(p2 - side * w2, normL, col2, 1.0, 0.72, 0.0, 0.0, 1.0);
        int v2M = part.AddVertex(p2, upNormal, col2, 1.0, 0.72, 0.5, 0.0, 1.0);
        int v2R = part.AddVertex(p2 + side * w2, normR, col2, 1.0, 0.72, 1.0, 0.0, 1.0);

        int v3L = part.AddVertex(p3L, normL, col3, 1.0, 1.0, 0.15, 0.0, 1.0);
        int v3M = part.AddVertex(p3M, upNormal, col3, 1.0, 0.91, 0.5, 0.0, 1.0);
        int v3R = part.AddVertex(p3R, normR, col3, 1.0, 1.0, 0.85, 0.0, 1.0);

        // Triangles facing skyward
        Primitives.TriangleFacing(part, vRoot, v1L, v1M, upNormal);
        Primitives.TriangleFacing(part, vRoot, v1M, v1R, upNormal);
        Primitives.TriangleFacing(part, v1L, v2L, v1M, upNormal);
        Primitives.TriangleFacing(part, v1M, v2L, v2M, upNormal);
        Primitives.TriangleFacing(part, v1M, v2M, v1R, upNormal);
        Primitives.TriangleFacing(part, v1R, v2M, v2R, upNormal);
        Primitives.TriangleFacing(part, v2L, v3L, v2M, upNormal);
        Primitives.TriangleFacing(part, v2M, v3L, v3M, upNormal);
        Primitives.TriangleFacing(part, v2M, v3M, v3R, upNormal);
        Primitives.TriangleFacing(part, v2M, v3R, v2R, upNormal);

        part.RecordLeaf(leafVertex, leafIndex, root, length, volumetric: false);
        if (essential) part.EssentialSpans.Add((leafVertex, part.VertexCount - leafVertex));
    }

    /// <summary>
    /// Volumetric 3D alder foliage cloud cluster at a spur node with petiole stalks, spiral phyllotaxis, and nestled organ sites.
    /// </summary>
    private static void NtbAlderFoliageCluster(MeshData part, Rng rng, Vec3 clusterNode, Vec3 twigDir,
        double clusterScale, double[] fol1, double[] fol2, int detail, bool juvenile,
        List<Vec3> coneSites, List<Vec3> coneDirs, List<Vec3> catkinSites, List<Vec3> catkinDirs)
    {
        // Woody spur shoot stub with UV2 (2.0, 0.0)
        double spurLen = rng.Range(0.045, 0.075) * clusterScale;
        var sDir = (twigDir * 0.65 + NtbRadial(rng.Range(0, Math.PI * 2)) * 0.35).Normalized();
        var spurEnd = clusterNode + sDir * spurLen;
        var spurPath = new[] { clusterNode, spurEnd };
        var spurRadii = new[] { 0.0040 * clusterScale, 0.0022 * clusterScale };
        NtbWood(part, spurPath, spurRadii, detail, NtbFenforgeTwig, NtbFenforgeTwig, NtbFenforgeBarkDeep, 2.0, 0.2, true);

        // 3D spiral phyllotaxis around the spur shoot
        int leafCount = detail switch { 2 => 3, 1 => 4, _ => 5 };
        double baseAz = rng.Range(0, Math.PI * 2);
        var lateralRef = Vec3.Up.Cross(sDir);
        if (lateralRef.LengthSq < 1e-6) lateralRef = new Vec3(1, 0, 0).Cross(sDir);
        lateralRef = lateralRef.Normalized();
        var upRef = sDir.Cross(lateralRef).Normalized();

        for (int li = 0; li < leafCount; li++)
        {
            double lt = (li + 0.5) / leafCount;
            var attach = Vec3.Lerp(clusterNode, spurEnd, lt);
            double leafAngle = baseAz + li * 2.39996 + rng.Range(-0.15, 0.15); // Golden angle spiral
            var radDir = (lateralRef * Math.Cos(leafAngle) + upRef * Math.Sin(leafAngle)).Normalized();

            // Radiate in 3D: elevation pitch (-15° to +40° from spur plane)
            double pitch = rng.Range(0.20, 0.65);
            var petioleDir = (radDir * Math.Cos(pitch) + sDir * 0.30 + Vec3.Up * Math.Sin(pitch)).Normalized();
            double petioleLen = rng.Range(0.035, 0.060) * clusterScale;
            var leafRoot = attach + petioleDir * petioleLen - Vec3.Up * (petioleLen * 0.12);

            // Petiole stalk with UV2 = (2.0, 0.0)
            int pV0 = part.VertexCount;
            var petSide = petioleDir.Cross(Vec3.Up);
            if (petSide.LengthSq < 1e-6) petSide = lateralRef;
            petSide = petSide.Normalized();
            double pw = 0.0016 * clusterScale;
            var petNorm = petSide.Cross(petioleDir).Normalized();
            if (petNorm.Y < 0.20f) petNorm = Vec3.Up;
            int p0 = part.AddVertex(attach - petSide * pw, petNorm, NtbFenforgeTwig, 1.0, 0.0, 0.0, 2.0, 0.0);
            int p1 = part.AddVertex(attach + petSide * pw, petNorm, NtbFenforgeTwig, 1.0, 0.0, 1.0, 2.0, 0.0);
            int p2 = part.AddVertex(leafRoot - petSide * (pw * 0.7), petNorm, NtbFenforgeTwig, 1.0, 1.0, 0.0, 2.0, 0.0);
            int p3 = part.AddVertex(leafRoot + petSide * (pw * 0.7), petNorm, NtbFenforgeTwig, 1.0, 1.0, 1.0, 2.0, 0.0);
            Primitives.TriangleFacing(part, p0, p2, p1, petNorm);
            Primitives.TriangleFacing(part, p1, p2, p3, petNorm);
            NtbPin(part, pV0);

            // Leaf blade: varied size for layered depth
            double bladeLen = rng.Range(0.14, 0.22) * clusterScale * (juvenile ? 0.80 : 1.0);
            double halfW = bladeLen * rng.Range(0.38, 0.44); // Broad obovate
            double roll = rng.Range(-0.44, 0.61); // -25° to +35° roll jitter!
            double droop = rng.Range(0.08, 0.20); // Gentle gravity droop
            var leafTip = leafRoot + (petioleDir * 0.75 + radDir * 0.25 - Vec3.Up * (droop * 0.5)).Normalized() * bladeLen;

            NtbAlderLeaf(part, leafRoot, leafTip, radDir, halfW, fol1, fol2, detail, li % 2 == 0, roll, droop);
        }

        // Signature organ sites clustered naturally at spur nodes nestled among the 3D foliage
        if (!juvenile)
        {
            if (rng.NextDouble() < 0.45)
            {
                coneSites.Add(clusterNode);
                coneDirs.Add(sDir);
            }
            if (rng.NextDouble() < 0.60)
            {
                catkinSites.Add(spurEnd);
                catkinDirs.Add(sDir);
            }
        }
    }

    /// <summary>
    /// Slender falcate-lanceolate willow leaf with skyward/outward normal and silver-sage underside.
    /// </summary>
    private static void NtbWillowLeaf(MeshData part, Vec3 root, Vec3 tip, Vec3 outward,
        double halfWidth, double[] baseCol, double[] tipCol, int detail, bool essential)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-6 || halfWidth < 1e-6) return;
        int leafVertex = part.VertexCount;
        int leafIndex = part.Indices.Count;

        var fwd = axis / length;
        var side = Vec3.Up.Cross(fwd);
        if (side.LengthSq < 1e-6) side = outward.Cross(fwd);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(fwd);
        side = side.Normalized();
        var upNormal = fwd.Cross(side).Normalized();
        if (upNormal.Y < 0.20f) upNormal = Vec3.Up;
        upNormal = upNormal.Normalized();

        var mid1 = root + axis * 0.35 + upNormal * (halfWidth * 0.20);
        var mid2 = root + axis * 0.70 + upNormal * (halfWidth * 0.22);
        var normL = (upNormal - side * 0.28).Normalized();
        var normR = (upNormal + side * 0.28).Normalized();
        var col1 = Primitives.Mix(baseCol, tipCol, 0.35);
        var col2 = Primitives.Mix(baseCol, tipCol, 0.70);

        int iRoot = part.AddVertex(root, upNormal, baseCol, 1.0, 0.0, 0.5, 0.0, 1.0);
        int iM1L = part.AddVertex(mid1 - side * (halfWidth * 0.85), normL, col1, 1.0, 0.35, 0.0, 0.0, 1.0);
        int iM1M = part.AddVertex(mid1, upNormal, col1, 1.0, 0.35, 0.5, 0.0, 1.0);
        int iM1R = part.AddVertex(mid1 + side * (halfWidth * 0.85), normR, col1, 1.0, 0.35, 1.0, 0.0, 1.0);

        int iM2L = part.AddVertex(mid2 - side * halfWidth, normL, col2, 1.0, 0.70, 0.0, 0.0, 1.0);
        int iM2M = part.AddVertex(mid2, upNormal, col2, 1.0, 0.70, 0.5, 0.0, 1.0);
        int iM2R = part.AddVertex(mid2 + side * halfWidth, normR, col2, 1.0, 0.70, 1.0, 0.0, 1.0);

        int iTip = part.AddVertex(tip, upNormal, tipCol, 1.0, 1.0, 0.5, 0.0, 1.0);

        Primitives.TriangleFacing(part, iRoot, iM1L, iM1M, upNormal);
        Primitives.TriangleFacing(part, iRoot, iM1M, iM1R, upNormal);
        Primitives.TriangleFacing(part, iM1L, iM2L, iM1M, upNormal);
        Primitives.TriangleFacing(part, iM1M, iM2L, iM2M, upNormal);
        Primitives.TriangleFacing(part, iM1M, iM2M, iM1R, upNormal);
        Primitives.TriangleFacing(part, iM1R, iM2M, iM2R, upNormal);
        Primitives.TriangleFacing(part, iM2L, iTip, iM2M, upNormal);
        Primitives.TriangleFacing(part, iM2M, iTip, iM2R, upNormal);

        part.RecordLeaf(leafVertex, leafIndex, root, length, volumetric: false);
        if (essential) part.EssentialSpans.Add((leafVertex, part.VertexCount - leafVertex));
    }

    /// <summary>
    /// Feathery 3D spherical / tufted tamarack needle rosette (Larix laricina) with sky-facing normals.
    /// Replaces flat 2D horizontal pancakes with voluminous, feathery pincushion needle clusters.
    /// </summary>
    private static void NtbTamarackRosette(MeshData part, Vec3 root, Vec3 spurDir, double rosetteSize,
        double[] col1, double[] col2, double[] col3, int detail, bool essential)
    {
        int leafVertex = part.VertexCount;
        int leafIndex = part.Indices.Count;

        var fwd = spurDir.LengthSq > 1e-6 ? spurDir.Normalized() : Vec3.Up;
        var lat = Vec3.Up.Cross(fwd);
        if (lat.LengthSq < 1e-6) lat = new Vec3(1, 0, 0).Cross(fwd);
        lat = lat.Normalized();
        var up = lat.Cross(fwd).Normalized();
        if (up.Y < 0.25f) up = Vec3.Up;
        up = up.Normalized();

        // 3D tufted needle rosette: radiating in multiple angular tiers around the spur
        int bladeCount = detail switch { 2 => 4, 1 => 7, _ => 9 };
        for (int b = 0; b < bladeCount; b++)
        {
            // Golden angle spiral distribution in 3D
            double bAz = b * 2.39996 + 0.35;
            double elev = (b % 2 == 0) ? 0.35 : 0.78; // Alternating high / low elevation rings
            var radLat = (lat * Math.Cos(bAz) + up * Math.Sin(bAz)).Normalized();
            var bDir = (fwd * (0.40 + 0.35 * Math.Cos(elev)) + radLat * Math.Sin(elev) + Vec3.Up * 0.15).Normalized();

            double bladeLen = rosetteSize * (0.85 + 0.30 * Math.Abs(Math.Sin(b * 1.7)));
            double bladeWid = bladeLen * 0.14; // Slender feathery needle

            var bSide = up.Cross(bDir);
            if (bSide.LengthSq < 1e-6) bSide = lat;
            bSide = bSide.Normalized();

            var bNorm = bDir.Cross(bSide).Normalized();
            if (bNorm.Y < 0.25f) bNorm = (bNorm + Vec3.Up * (0.45f - bNorm.Y)).Normalized();

            var tip = root + bDir * bladeLen;
            var mid = root + bDir * (bladeLen * 0.50) + bNorm * (bladeLen * 0.08);

            var colBase = col1;
            var colMid = col2;
            var colTip = (b % 2 == 0) ? col3 : col2;

            int iRoot = part.AddVertex(root, bNorm, colBase, 1.0, 0.0, 0.5, 0.0, 1.0);
            int iMidL = part.AddVertex(mid - bSide * bladeWid, bNorm, colMid, 1.0, 0.5, 0.0, 0.0, 1.0);
            int iMidR = part.AddVertex(mid + bSide * bladeWid, bNorm, colMid, 1.0, 0.5, 1.0, 0.0, 1.0);
            int iTip = part.AddVertex(tip, bNorm, colTip, 1.0, 1.0, 0.5, 0.0, 1.0);

            Primitives.TriangleFacing(part, iRoot, iMidL, iMidR, bNorm);
            Primitives.TriangleFacing(part, iMidL, iTip, iMidR, bNorm);
        }

        part.RecordLeaf(leafVertex, leafIndex, root, rosetteSize, volumetric: true);
        if (essential) part.EssentialSpans.Add((leafVertex, part.VertexCount - leafVertex));
    }

    /// <summary>
    /// Compatibility redirect for tamarack foliage spray -> 3D needle rosette.
    /// </summary>
    private static void NtbTamarackSpray(MeshData part, Vec3 root, Vec3 forward, Vec3 lateral,
        double sprayLen, double sprayWid, double[] col1, double[] col2, double[] col3, int detail, bool essential)
    {
        NtbTamarackRosette(part, root, forward, sprayLen, col1, col2, col3, detail, essential);
    }

    /// <summary>
    /// Sculpted woody alder cone with parted scales and warm amber-orange spore glow inside (UV2 = 1.30, 0.86).
    /// </summary>
    private static void NtbFenCone(MeshData m, Vec3 attach, Vec3 dir, double length, double radius)
    {
        if (length < 1e-4 || radius < 1e-4) return;
        int v0 = m.VertexCount;
        var axis = dir.LengthSq > 1e-6 ? dir.Normalized() : -Vec3.Up;
        var side = Vec3.Up.Cross(axis);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(axis);
        side = side.Normalized();
        var up = axis.Cross(side).Normalized();

        int whorls = 4;
        int perWhorl = 5;
        var pBase = attach;
        var pTip = attach + axis * length;

        int baseIdx = m.AddVertex(pBase, -axis, NtbFenforgeConeBody, 1.0, 0.0, 0.0, 1.30, 0.86);
        var prevRing = new int[perWhorl];
        for (int k = 0; k < perWhorl; k++)
        {
            double ang = k * 2.0 * Math.PI / perWhorl;
            var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
            var pos = pBase + axis * (length * 0.20) + radial * (radius * 0.70);
            var norm = (radial * 0.8 - axis * 0.3).Normalized();
            var col = (k % 2 == 0) ? NtbFenforgeConeGlow : NtbFenforgeConeBody;
            prevRing[k] = m.AddVertex(pos, norm, col, 1.0, 0.2, (double)k / perWhorl, 1.30, 0.86);
        }
        for (int k = 0; k < perWhorl; k++)
        {
            Primitives.TriangleFacing(m, baseIdx, prevRing[k], prevRing[(k + 1) % perWhorl], -axis);
        }

        for (int w = 1; w < whorls; w++)
        {
            double wt = (w + 1) / (double)(whorls + 1);
            double r = radius * Math.Sin(Math.PI * wt) * 1.1;
            var nextRing = new int[perWhorl];
            double offset = (w % 2 == 1) ? Math.PI / perWhorl : 0.0;
            for (int k = 0; k < perWhorl; k++)
            {
                double ang = k * 2.0 * Math.PI / perWhorl + offset;
                var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
                var pos = pBase + axis * (length * wt) + radial * r;
                var norm = (radial * 0.85 + axis * (wt - 0.5) * 0.5).Normalized();
                var col = ((w + k) % 2 == 0) ? NtbFenforgeConeGlow : NtbFenforgeConeBody;
                nextRing[k] = m.AddVertex(pos, norm, col, 1.0, wt, (double)k / perWhorl, 1.30, 0.86);
            }

            for (int k = 0; k < perWhorl; k++)
            {
                int a = prevRing[k];
                int b = prevRing[(k + 1) % perWhorl];
                int c = nextRing[k];
                int d = nextRing[(k + 1) % perWhorl];
                var faceNorm = (m.NormalAt(c) + m.NormalAt(d)).Normalized();
                Primitives.TriangleFacing(m, a, c, b, faceNorm);
                Primitives.TriangleFacing(m, b, c, d, faceNorm);
            }
            prevRing = nextRing;
        }

        int tipIdx = m.AddVertex(pTip, axis, NtbFenforgeConeBody, 1.0, 1.0, 0.0, 1.30, 0.86);
        for (int k = 0; k < perWhorl; k++)
        {
            Primitives.TriangleFacing(m, prevRing[k], tipIdx, prevRing[(k + 1) % perWhorl], axis);
        }

        int count = m.VertexCount - v0;
        NewFloraMarkOrgan(m, v0, count);
        NtbSetUV2(m, v0, count, 1.30f, 0.86f);
        m.EssentialSpans.Add((v0, count));
    }

    /// <summary>
    /// Pendulous articulated copper-gold & glowing amber catkin dangling from branch tips (UV2 = 1.38, 0.90).
    /// </summary>
    private static void NtbFenCatkin(MeshData m, Vec3 attach, Vec3 dir, double length, double radius)
    {
        if (length < 1e-4 || radius < 1e-4) return;
        int v0 = m.VertexCount;
        var axis = dir.LengthSq > 1e-6 ? dir.Normalized() : -Vec3.Up;
        var side = Vec3.Up.Cross(axis);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(axis);
        side = side.Normalized();
        var up = axis.Cross(side).Normalized();

        int segments = 5;
        int sides = 5;
        var p0 = attach;
        var prevRing = new int[sides];

        int baseIdx = m.AddVertex(p0, -axis, NtbFenforgeCatkinBase, 1.0, 0.0, 0.0, 1.38, 0.90);
        for (int s = 0; s < sides; s++)
        {
            double ang = s * 2.0 * Math.PI / sides;
            var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
            prevRing[s] = m.AddVertex(p0 + axis * (length * 0.1) + radial * (radius * 0.5), radial, NtbFenforgeCatkinBase, 1.0, 0.1, (double)s / sides, 1.38, 0.90);
        }
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, baseIdx, prevRing[s], prevRing[(s + 1) % sides], -axis);
        }

        for (int seg = 1; seg <= segments; seg++)
        {
            double t = seg / (double)segments;
            double sway = 0.015 * Math.Sin(t * Math.PI);
            var center = p0 + axis * (length * t) + side * sway;
            double r = radius * (1.0 + 0.35 * Math.Sin(Math.PI * t));
            if (seg == segments) r *= 0.2;
            var nextRing = new int[sides];
            var col = Primitives.Mix(NtbFenforgeCatkinBase, NtbFenforgeCatkinTip, Math.Pow(t, 0.7));

            for (int s = 0; s < sides; s++)
            {
                double ang = s * 2.0 * Math.PI / sides + t * 0.5;
                var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
                var pos = center + radial * r;
                var norm = (radial * 0.85 + axis * 0.3).Normalized();
                nextRing[s] = m.AddVertex(pos, norm, col, 1.0, t, (double)s / sides, 1.38, 0.90);
            }

            for (int s = 0; s < sides; s++)
            {
                int a = prevRing[s];
                int b = prevRing[(s + 1) % sides];
                int c = nextRing[s];
                int d = nextRing[(s + 1) % sides];
                var faceNorm = (m.NormalAt(c) + m.NormalAt(d)).Normalized();
                Primitives.TriangleFacing(m, a, c, b, faceNorm);
                Primitives.TriangleFacing(m, b, c, d, faceNorm);
            }
            prevRing = nextRing;
        }

        var tipPos = p0 + axis * (length * 1.04);
        int tipIdx = m.AddVertex(tipPos, axis, NtbFenforgeCatkinTip, 1.0, 1.0, 0.0, 1.38, 0.90);
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, prevRing[s], tipIdx, prevRing[(s + 1) % sides], axis);
        }

        int count = m.VertexCount - v0;
        NewFloraMarkOrgan(m, v0, count);
        NtbSetUV2(m, v0, count, 1.38f, 0.90f);
        m.EssentialSpans.Add((v0, count));
    }

    /// <summary>
    /// Soft, glowing pearl-silver & pale-gold silky willow catkin (UV2 = 1.28, 0.88).
    /// </summary>
    private static void NtbWillowCatkin(MeshData m, Vec3 attach, Vec3 dir, double length, double radius)
    {
        if (length < 1e-4 || radius < 1e-4) return;
        int v0 = m.VertexCount;
        var axis = dir.LengthSq > 1e-6 ? dir.Normalized() : Vec3.Up;
        var side = Vec3.Up.Cross(axis);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(axis);
        side = side.Normalized();
        var up = axis.Cross(side).Normalized();

        int segments = 4;
        int sides = 5;
        var p0 = attach;
        var prevRing = new int[sides];

        int baseIdx = m.AddVertex(p0, -axis, NtbBanksorrowCatkinBase, 1.0, 0.0, 0.0, 1.28, 0.88);
        for (int s = 0; s < sides; s++)
        {
            double ang = s * 2.0 * Math.PI / sides;
            var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
            prevRing[s] = m.AddVertex(p0 + axis * (length * 0.15) + radial * (radius * 0.6), radial, NtbBanksorrowCatkinBase, 1.0, 0.15, (double)s / sides, 1.28, 0.88);
        }
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, baseIdx, prevRing[s], prevRing[(s + 1) % sides], -axis);
        }

        for (int seg = 1; seg <= segments; seg++)
        {
            double t = seg / (double)segments;
            var center = p0 + axis * (length * t);
            double r = radius * Math.Sin(Math.PI * Math.Clamp(t, 0.1, 0.95)) * 1.2;
            var nextRing = new int[sides];
            var col = Primitives.Mix(NtbBanksorrowCatkinBase, NtbBanksorrowCatkinTip, t);

            for (int s = 0; s < sides; s++)
            {
                double ang = s * 2.0 * Math.PI / sides;
                var radial = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();
                var pos = center + radial * r;
                var norm = (radial * 0.85 + axis * (t - 0.5) * 0.5).Normalized();
                nextRing[s] = m.AddVertex(pos, norm, col, 1.0, t, (double)s / sides, 1.28, 0.88);
            }

            for (int s = 0; s < sides; s++)
            {
                int a = prevRing[s];
                int b = prevRing[(s + 1) % sides];
                int c = nextRing[s];
                int d = nextRing[(s + 1) % sides];
                var faceNorm = (m.NormalAt(c) + m.NormalAt(d)).Normalized();
                Primitives.TriangleFacing(m, a, c, b, faceNorm);
                Primitives.TriangleFacing(m, b, c, d, faceNorm);
            }
            prevRing = nextRing;
        }

        var tipPos = p0 + axis * (length * 1.02);
        int tipIdx = m.AddVertex(tipPos, axis, NtbBanksorrowCatkinTip, 1.0, 1.0, 0.0, 1.28, 0.88);
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, prevRing[s], tipIdx, prevRing[(s + 1) % sides], axis);
        }

        int count = m.VertexCount - v0;
        NewFloraMarkOrgan(m, v0, count);
        NtbSetUV2(m, v0, count, 1.28f, 0.88f);
        m.EssentialSpans.Add((v0, count));
    }

    /// <summary>
    /// Upright rose-amethyst & amber larch cone (UV2 = 1.32, 0.88).
    /// </summary>
    private static void NtbLarchCone(MeshData m, Vec3 attach, Vec3 upDir, double height, double radius)
    {
        if (height < 1e-4 || radius < 1e-4) return;
        int v0 = m.VertexCount;
        var axis = upDir.LengthSq > 1e-6 ? upDir.Normalized() : Vec3.Up;
        var side = Vec3.Up.Cross(axis);
        if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(axis);
        side = side.Normalized();
        var fwd = axis.Cross(side).Normalized();

        int whorls = 4;
        int perWhorl = 5;
        var p0 = attach;
        int baseIdx = m.AddVertex(p0, -axis, NtbMirespireConeBase, 1.0, 0.0, 0.0, 1.32, 0.88);
        var prevRing = new int[perWhorl];

        for (int k = 0; k < perWhorl; k++)
        {
            double ang = k * 2.0 * Math.PI / perWhorl;
            var radial = (side * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
            prevRing[k] = m.AddVertex(p0 + axis * (height * 0.15) + radial * (radius * 0.65), radial, NtbMirespireConeBase, 1.0, 0.15, (double)k / perWhorl, 1.32, 0.88);
        }
        for (int k = 0; k < perWhorl; k++)
        {
            Primitives.TriangleFacing(m, baseIdx, prevRing[k], prevRing[(k + 1) % perWhorl], -axis);
        }

        for (int w = 1; w <= whorls; w++)
        {
            double wt = w / (double)whorls;
            double r = radius * (1.0 - 0.70 * wt * wt) * (0.85 + 0.25 * Math.Sin(wt * Math.PI * 3.0));
            if (w == whorls) r = radius * 0.15;
            double offset = w * 0.65;
            var nextRing = new int[perWhorl];
            var col = (w % 2 == 0) ? NtbMirespireConeRim : NtbMirespireConeBody;

            for (int k = 0; k < perWhorl; k++)
            {
                double ang = k * 2.0 * Math.PI / perWhorl + offset;
                var radial = (side * Math.Cos(ang) + fwd * Math.Sin(ang)).Normalized();
                var pos = p0 + axis * (height * wt) + radial * r;
                var norm = (radial * 0.80 + axis * 0.45).Normalized();
                nextRing[k] = m.AddVertex(pos, norm, col, 1.0, wt, (double)k / perWhorl, 1.32, 0.88);
            }

            for (int k = 0; k < perWhorl; k++)
            {
                int a = prevRing[k];
                int b = prevRing[(k + 1) % perWhorl];
                int c = nextRing[k];
                int d = nextRing[(k + 1) % perWhorl];
                var faceNorm = (m.NormalAt(c) + m.NormalAt(d)).Normalized();
                Primitives.TriangleFacing(m, a, c, b, faceNorm);
                Primitives.TriangleFacing(m, b, c, d, faceNorm);
            }
            prevRing = nextRing;
        }

        var tipPos = p0 + axis * (height * 1.03);
        int tipIdx = m.AddVertex(tipPos, axis, NtbMirespireConeRim, 1.0, 1.0, 0.0, 1.32, 0.88);
        for (int k = 0; k < perWhorl; k++)
        {
            Primitives.TriangleFacing(m, prevRing[k], tipIdx, prevRing[(k + 1) % perWhorl], axis);
        }

        int count = m.VertexCount - v0;
        NewFloraMarkOrgan(m, v0, count);
        NtbSetUV2(m, v0, count, 1.32f, 0.88f);
        m.EssentialSpans.Add((v0, count));
    }

    /// <summary>
    /// Conical, rounded-top swamp-cypress pneumatophore knee (UV2 = 2, 0).
    /// </summary>
    private static void NtbSwampKnee(MeshData m, Vec3 basePos, double height, double radius, double[] col)
    {
        if (height < 1e-4 || radius < 1e-4) return;
        int v0 = m.VertexCount;
        int sides = 6;
        int rings = 3;
        var prevRing = new int[sides];

        int groundIdx = m.AddVertex(basePos, -Vec3.Up, col, 1.0, 0.0, 0.0, 2.0, 0.0);
        for (int s = 0; s < sides; s++)
        {
            double ang = s * 2.0 * Math.PI / sides;
            var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            prevRing[s] = m.AddVertex(basePos + radial * radius, radial, col, 1.0, 0.0, (double)s / sides, 2.0, 0.0);
        }
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, groundIdx, prevRing[s], prevRing[(s + 1) % sides], -Vec3.Up);
        }

        for (int r = 1; r <= rings; r++)
        {
            double t = r / (double)rings;
            double currentR = radius * (1.0 - 0.75 * Math.Pow(t, 1.4));
            if (r == rings) currentR = radius * 0.25;
            var nextRing = new int[sides];
            var ringCol = Primitives.Scale(col, 0.95 + 0.15 * t);

            for (int s = 0; s < sides; s++)
            {
                double ang = s * 2.0 * Math.PI / sides;
                var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                var pos = basePos + Vec3.Up * (height * t) + radial * currentR;
                var norm = (radial * 0.85 + Vec3.Up * 0.35).Normalized();
                nextRing[s] = m.AddVertex(pos, norm, ringCol, 1.0, t, (double)s / sides, 2.0, 0.0);
            }

            for (int s = 0; s < sides; s++)
            {
                int a = prevRing[s];
                int b = prevRing[(s + 1) % sides];
                int c = nextRing[s];
                int d = nextRing[(s + 1) % sides];
                var faceNorm = (m.NormalAt(c) + m.NormalAt(d)).Normalized();
                Primitives.TriangleFacing(m, a, c, b, faceNorm);
                Primitives.TriangleFacing(m, b, c, d, faceNorm);
            }
            prevRing = nextRing;
        }

        int domeIdx = m.AddVertex(basePos + Vec3.Up * (height * 1.02), Vec3.Up, Primitives.Scale(col, 1.1), 1.0, 1.0, 0.0, 2.0, 0.0);
        for (int s = 0; s < sides; s++)
        {
            Primitives.TriangleFacing(m, prevRing[s], domeIdx, prevRing[(s + 1) % sides], Vec3.Up);
        }

        int count = m.VertexCount - v0;
        m.EssentialSpans.Add((v0, count));
    }

    // ------------------------------------------------------------------
    // 1. Fenforge — Buttressed Fen Alder (Alnus glutinosa)
    // ------------------------------------------------------------------

    private static void Fenforge(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData
        {
            Lod = m.Lod,
            FloraDetailLevel = m.FloraDetailLevel,
            FloraVisualSeed = m.FloraVisualSeed,
            PreserveUpperCanopyOnLod = true
        };
        int detail = part.FloraDetailLevel ?? 0;

        double totalH = Math.Max(0.5, height);
        double crownR = Math.Max(0.20, radius);
        double baseR = (juvenile ? 0.12 : 0.19) * (crownR / 1.2);

        // Pre-brightened rich emerald foliage with sunlit golden-lime margins
        var fenRaw1 = Primitives.Mix(c1, new[] { 0.16, 0.44, 0.16 }, 0.45);
        var fenRaw2 = Primitives.Mix(c2, new[] { 0.30, 0.60, 0.22 }, 0.45);
        var fol1 = NtbBright(fenRaw1);
        var fol2 = NtbBright(fenRaw2);

        // Direction of natural picturesque lean
        double leanAz = rng.Range(0, Math.PI * 2);
        var leanDir = NtbRadial(leanAz);
        var ortho = NtbRadial(leanAz + Math.PI * 0.5);

        // 1. Basal Stilt-Roots & Sweeping Flanged Prop-Roots (10-13 prop roots clutching and fusing into substrate)
        int rootCount = juvenile ? 8 : (11 + rng.NextInt(3));
        for (int ri = 0; ri < rootCount; ri++)
        {
            double rAz = leanAz + Math.PI * 2 * ri / rootCount + rng.Range(-0.18, 0.18);
            var rOut = NtbRadial(rAz);
            double attachH = totalH * rng.Range(0.10, 0.28);
            double groundDist = baseR * rng.Range(2.18, 3.20);
            var pAttach = Vec3.Up * attachH + rOut * (baseR * rng.Range(0.45, 0.80));
            double embed = Math.Clamp(groundDist * 0.48, 0.12, 0.23);
            var pGround = rOut * groundDist - Vec3.Up * embed;

            // Sweeping outward arch: emerges curving downward and outward from trunk, plunging into mud with flared claw
            var span = pGround - pAttach;
            double spanDist = span.Length;
            var tanAttach = (rOut * 0.82 - Vec3.Up * 0.55).Normalized();
            var tanGround = (rOut * 0.38 - Vec3.Up * 0.90).Normalized();

            var cR0 = pAttach + tanAttach * (spanDist * 0.42) + rOut * (baseR * 0.45);
            var cR1 = pGround - tanGround * (spanDist * 0.38) + rOut * (baseR * 0.20);

            int rStations = detail switch { 2 => 5, 1 => 6, _ => 7 };
            var rPath = NtbSampleBezier(pAttach, cR0, cR1, pGround, rStations);
            var rRadii = new double[rStations];
            for (int s = 0; s < rStations; s++)
            {
                double st = s / (double)(rStations - 1);
                // Flared collar at trunk attachment (st=0) and broad gripping foot at ground (st=1)
                double collarTrunk = 1.0 + 0.45 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.30), 2.0);
                double flareGround = 1.0 + 0.55 * Math.Pow(Math.Max(0.0, (st - 0.70) / 0.30), 2.0);
                rRadii[s] = Math.Max(0.008, baseR * MathD.Lerp(0.36, 0.18, st) * collarTrunk * flareGround);
            }
            NtbWood(part, rPath, rRadii, detail, NtbFenforgeRoot, NtbFenforgeRootMoss, NtbFenforgeBarkDeep,
                ridgeFreq: 3.5, twist: 0.6, essential: true);

            // Intertwining secondary arching root braces (knitting the prop cage together)
            if (ri % 2 == 0)
            {
                int braceStation = rStations / 2;
                var bAttach = rPath[braceStation];
                double bAz = rAz + rng.Range(0.40, 0.65) * (ri % 4 == 0 ? 1.0 : -1.0);
                var bOut = NtbRadial(bAz);
                var bGround = bOut * (groundDist * 1.10) - Vec3.Up * (embed * 1.10);
                var bSpan = bGround - bAttach;
                double bSpanDist = bSpan.Length;
                var bC0 = bAttach + (bOut * 0.70 - Vec3.Up * 0.60).Normalized() * (bSpanDist * 0.40);
                var bC1 = bGround - (bOut * 0.35 - Vec3.Up * 0.90).Normalized() * (bSpanDist * 0.35);

                var bPath = NtbSampleBezier(bAttach, bC0, bC1, bGround, 5);
                var bRadii = new double[5];
                for (int s = 0; s < 5; s++)
                {
                    double st = s / 4.0;
                    bRadii[s] = Math.Max(0.006, baseR * MathD.Lerp(0.20, 0.10, st) * (1.0 + 0.35 * Math.Pow(st, 2.0)));
                }
                NtbWood(part, bPath, bRadii, detail, NtbFenforgeRoot, NtbFenforgeRootMoss, NtbFenforgeBarkDeep,
                    ridgeFreq: 3.0, twist: 0.5, essential: true);
            }
        }

        // 2. Muscular, Leaning Main Bole + Co-dominant Twin Bole with smooth continuous curvature
        double forkH = totalH * rng.Range(0.38, 0.48);
        var pFork = leanDir * (crownR * rng.Range(0.22, 0.36)) + Vec3.Up * forkH;
        var pBoleTop = leanDir * (crownR * rng.Range(0.36, 0.56)) + Vec3.Up * (totalH * rng.Range(0.85, 0.95));

        int tStations = detail switch { 2 => 7, 1 => 9, _ => 11 };

        // Continuous smooth Bezier for main trunk from ground to fork
        var cM0 = Vec3.Up * (forkH * 0.35) + leanDir * (crownR * 0.08);
        var cM1 = Vec3.Up * (forkH * 0.75) + leanDir * (crownR * 0.20) + ortho * (crownR * 0.04);
        var mainTrunkPath = NtbSampleBezier(Vec3.Zero, cM0, cM1, pFork, tStations);
        var mainTrunkRadii = new double[tStations];
        for (int i = 0; i < tStations; i++)
        {
            double st = i / (double)(tStations - 1);
            // Muscular taper with crotch flare approaching the fork
            double crotchFlare = 1.0 + 0.35 * Math.Pow(Math.Max(0.0, (st - 0.65) / 0.35), 2.0);
            mainTrunkRadii[i] = Math.Max(0.018, baseR * MathD.Lerp(1.18, 0.55, Math.Pow(st, 0.85)) * crotchFlare);
        }
        // Fork radii (F1): parent swells smoothly toward the twin bole and scaffold boughs
        {
            double twinNominalR = Form.ForkJunction.ChildRadiusShare(mainTrunkRadii[(int)(0.28 * (tStations - 1))], 0.4);
            var mainForks = new List<(Vec3, double)> { (NtbAt(mainTrunkPath, 0.28), twinNominalR) };
            foreach (double bf in new[] { 0.38, 0.58, 0.74, 0.88 })
                mainForks.Add((NtbAt(mainTrunkPath, bf), baseR * 0.24 * 1.5));
            Form.ForkJunction.WidenParent(mainTrunkPath, mainTrunkRadii, mainForks.ToArray());
        }
        NtbWood(part, mainTrunkPath, mainTrunkRadii, detail, NtbFenforgeBark, NtbFenforgeBarkLight, NtbFenforgeBarkDeep,
            ridgeFreq: 4.0, twist: 0.7, essential: true);

        // Main trunk continuation past fork: C1-continuous tangent at pFork!
        var trunkTanAtFork = NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, 1.0);
        var cM2 = pFork + trunkTanAtFork * (totalH * 0.20);
        var cM3 = pBoleTop - (leanDir * 0.45 + Vec3.Up * 0.85).Normalized() * (totalH * 0.18);
        var upperBolePath = NtbSampleBezier(pFork, cM2, cM3, pBoleTop, tStations);
        var upperBoleRadii = new double[tStations];
        for (int i = 0; i < tStations; i++)
        {
            double st = i / (double)(tStations - 1);
            double collar = 1.0 + 0.30 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.30), 2.0);
            upperBoleRadii[i] = Math.Max(0.010, baseR * MathD.Lerp(0.55, 0.14, st) * collar);
        }
        NtbWood(part, Form.ForkJunction.SinkStart(upperBolePath, upperBoleRadii[0]), upperBoleRadii, detail, NtbFenforgeBark, NtbFenforgeBarkLight, NtbFenforgeBarkDeep,
            ridgeFreq: 3.5, twist: 0.6, essential: true);

        // Co-dominant twin bole (branches at ~28% height, leaning outward with smooth collar)
        double twinSplitT = 0.28;
        var pTwinStartCenter = NtbAt(mainTrunkPath, twinSplitT);
        double twinAz = leanAz + rng.Range(1.9, 2.6) * (rng.NextDouble() > 0.5 ? 1 : -1);
        var twinDir = NtbRadial(twinAz);
        var mainTanAtTwin = NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, twinSplitT);
        double trunkRadiusAtTwin = mainTrunkRadii[(int)(twinSplitT * (tStations - 1))];
        var pTwinStart = pTwinStartCenter + twinDir * (trunkRadiusAtTwin * 0.45);

        double twinH = totalH * rng.Range(0.65, 0.78);
        var pTwinTop = pTwinStart + twinDir * (crownR * rng.Range(0.48, 0.68)) + Vec3.Up * (twinH - pTwinStart.Y);
        double twinDist = (pTwinTop - pTwinStart).Length;
        var cTw0 = pTwinStart + (mainTanAtTwin * 0.45 + twinDir * 0.55 + Vec3.Up * 0.35).Normalized() * (twinDist * 0.38);
        var cTw1 = pTwinTop - (twinDir * 0.65 + Vec3.Up * 0.65).Normalized() * (twinDist * 0.35);

        var twinPath = NtbSampleBezier(pTwinStart, cTw0, cTw1, pTwinTop, tStations);
        var twinRadii = new double[tStations];
        for (int i = 0; i < tStations; i++)
        {
            double st = i / (double)(tStations - 1);
            double collar = 1.0 + 0.45 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.25), 2.0);
            twinRadii[i] = Math.Max(0.010, baseR * MathD.Lerp(0.50, 0.12, st) * collar);
        }
        NtbWood(part, Form.ForkJunction.SinkStart(twinPath, twinRadii[0]), twinRadii, detail, NtbFenforgeBark, NtbFenforgeBarkLight, NtbFenforgeBarkDeep,
            ridgeFreq: 3.5, twist: 0.6, essential: true);

        // 3. Spreading Scaffold Boughs with flared branch collars
        var coneSites = new List<Vec3>();
        var coneDirs = new List<Vec3>();
        var catkinSites = new List<Vec3>();
        var catkinDirs = new List<Vec3>();

        var boughOrigins = new List<(Vec3 Root, Vec3 Tan, double Azimuth, double HeightFrac, double ParentRadius)>
        {
            // Lower scaffold boughs
            (NtbAt(mainTrunkPath, 0.38), NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, 0.38), leanAz - 2.8, 0.22, mainTrunkRadii[(int)(0.38*(tStations-1))]),
            (NtbAt(mainTrunkPath, 0.58), NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, 0.58), leanAz - 1.2, 0.32, mainTrunkRadii[(int)(0.58*(tStations-1))]),
            (NtbAt(mainTrunkPath, 0.74), NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, 0.74), leanAz + 0.1, 0.38, mainTrunkRadii[(int)(0.74*(tStations-1))]),
            (NtbAt(mainTrunkPath, 0.88), NtbBezierTangent(Vec3.Zero, cM0, cM1, pFork, 0.88), leanAz + 1.4, 0.46, mainTrunkRadii[(int)(0.88*(tStations-1))]),
            // Twin bole boughs
            (NtbAt(twinPath, 0.25), NtbBezierTangent(pTwinStart, cTw0, cTw1, pTwinTop, 0.25), twinAz + 2.1, 0.30, twinRadii[(int)(0.25*(tStations-1))]),
            (NtbAt(twinPath, 0.48), NtbBezierTangent(pTwinStart, cTw0, cTw1, pTwinTop, 0.48), twinAz - 0.9, 0.44, twinRadii[(int)(0.48*(tStations-1))]),
            (NtbAt(twinPath, 0.72), NtbBezierTangent(pTwinStart, cTw0, cTw1, pTwinTop, 0.72), twinAz + 0.9, 0.58, twinRadii[(int)(0.72*(tStations-1))]),
            (pTwinTop, NtbBezierTangent(pTwinStart, cTw0, cTw1, pTwinTop, 0.99), twinAz, 0.72, twinRadii[tStations-1]),
            // Upper bole boughs
            (NtbAt(upperBolePath, 0.18), NtbBezierTangent(pFork, cM2, cM3, pBoleTop, 0.18), leanAz + 2.4, 0.52, upperBoleRadii[(int)(0.18*(tStations-1))]),
            (NtbAt(upperBolePath, 0.40), NtbBezierTangent(pFork, cM2, cM3, pBoleTop, 0.40), leanAz - 0.7, 0.62, upperBoleRadii[(int)(0.40*(tStations-1))]),
            (NtbAt(upperBolePath, 0.62), NtbBezierTangent(pFork, cM2, cM3, pBoleTop, 0.62), leanAz - 2.1, 0.72, upperBoleRadii[(int)(0.62*(tStations-1))]),
            (NtbAt(upperBolePath, 0.82), NtbBezierTangent(pFork, cM2, cM3, pBoleTop, 0.82), leanAz + 0.6, 0.84, upperBoleRadii[(int)(0.82*(tStations-1))]),
            (pBoleTop, Vec3.Up, leanAz - 0.3, 0.92, upperBoleRadii[tStations-1])
        };

        for (int bi = 0; bi < boughOrigins.Count; bi++)
        {
            var (bCenter, bTrunkTan, bAzBase, hFrac, pRad) = boughOrigins[bi];
            double bAz = bAzBase + rng.Range(-0.14, 0.14);
            var bOut = NtbRadial(bAz);

            // Bough starts offset on outer surface of trunk
            var bRoot = bCenter + bOut * (pRad * 0.40);
            double bReach = crownR * rng.Range(0.68, 1.02) * (1.0 - hFrac * 0.28);
            double bElevation = bReach * rng.Range(0.28, 0.50);
            var bEnd = bRoot + bOut * bReach + Vec3.Up * bElevation;
            double bDist = (bEnd - bRoot).Length;

            // Smooth curved Bezier with natural branch-collar transition
            var bTan0 = (bTrunkTan * 0.35 + bOut * 0.70 + Vec3.Up * 0.25).Normalized();
            var bC0 = bRoot + bTan0 * (bDist * 0.40);
            var bC1 = bEnd - (bOut * 0.85 + Vec3.Up * 0.25).Normalized() * (bDist * 0.35);

            int bStations = detail switch { 2 => 4, 1 => 5, _ => 6 };
            var bPath = NtbSampleBezier(bRoot, bC0, bC1, bEnd, bStations);
            var bRadii = new double[bStations];
            for (int s = 0; s < bStations; s++)
            {
                double st = s / (double)(bStations - 1);
                double collar = 1.0 + 0.55 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.24), 2.0);
                bRadii[s] = Math.Max(0.005, baseR * MathD.Lerp(0.24, 0.045, Math.Pow(st, 0.85)) * collar);
            }
            NtbWood(part, Form.ForkJunction.SinkStart(bPath, bRadii[0]), bRadii, detail, NtbFenforgeBark, NtbFenforgeBarkLight, NtbFenforgeBarkDeep,
                ridgeFreq: 3.5, twist: 0.5, essential: true);

            // Volumetric 3D foliage clusters along primary bough mid-to-distal section
            int boughClusters = detail switch { 2 => 2, 1 => 4, _ => 5 };
            for (int bc = 0; bc < boughClusters; bc++)
            {
                double bct = Math.Clamp(0.32 + 0.62 * (bc + 0.5) / boughClusters + rng.Range(-0.045, 0.045), 0.20, 0.98);
                var cNode = NtbBezier(bRoot, bC0, bC1, bEnd, bct);
                var cTan = NtbBezierTangent(bRoot, bC0, bC1, bEnd, bct);
                double clusterScale = rng.Range(0.90, 1.15) * (juvenile ? 0.82 : 1.0);
                NtbAlderFoliageCluster(part, rng, cNode, cTan, clusterScale, fol1, fol2, detail, juvenile,
                    coneSites, coneDirs, catkinSites, catkinDirs);
            }

            // Secondary branches (2-3 per primary bough)
            int secCount = juvenile ? 2 : (bi % 2 == 0 ? 3 : 2);
            for (int si = 0; si < secCount; si++)
            {
                double sFrac = 0.36 + si * 0.28;
                var sRootCenter = NtbBezier(bRoot, bC0, bC1, bEnd, sFrac);
                var sTanParent = NtbBezierTangent(bRoot, bC0, bC1, bEnd, sFrac);
                double sSideSign = (si % 2 == 0) ? 1.0 : -1.0;
                double sAz = bAz + sSideSign * rng.Range(0.48, 0.80);
                var sOut = NtbRadial(sAz);
                var sRoot = sRootCenter + sOut * (bRadii[(int)(sFrac * (bStations - 1))] * 0.35);

                double sReach = bReach * rng.Range(0.42, 0.62);
                var sEnd = sRoot + sOut * sReach + Vec3.Up * (sReach * rng.Range(0.20, 0.40));
                double sDist = (sEnd - sRoot).Length;

                var sTan0 = (sTanParent * 0.40 + sOut * 0.70 + Vec3.Up * 0.25).Normalized();
                var sC0 = sRoot + sTan0 * (sDist * 0.40);
                var sC1 = sEnd - (sOut * 0.80 + Vec3.Up * 0.25).Normalized() * (sDist * 0.35);

                int sStations = detail switch { 2 => 3, 1 => 4, _ => 5 };
                var sPath = NtbSampleBezier(sRoot, sC0, sC1, sEnd, sStations);
                var sRadii = new double[sStations];
                for (int s = 0; s < sStations; s++)
                {
                    double st = s / (double)(sStations - 1);
                    double collar = 1.0 + 0.45 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.24), 2.0);
                    sRadii[s] = Math.Max(0.0035, baseR * MathD.Lerp(0.08, 0.022, st) * collar);
                }
                NtbWood(part, sPath, sRadii, detail, NtbFenforgeTwig, NtbFenforgeTwig, NtbFenforgeBarkDeep,
                    ridgeFreq: 3.0, twist: 0.4, essential: true);

                // Volumetric foliage clusters along secondary branches
                int secClusters = detail switch { 2 => 2, 1 => 4, _ => 6 };
                for (int sc = 0; sc < secClusters; sc++)
                {
                    double sct = 0.15 + 0.80 * (sc + 0.5) / secClusters;
                    var cNode = NtbBezier(sRoot, sC0, sC1, sEnd, sct);
                    var cTan = NtbBezierTangent(sRoot, sC0, sC1, sEnd, sct);
                    double clusterScale = rng.Range(0.85, 1.08) * (juvenile ? 0.80 : 1.0);
                    NtbAlderFoliageCluster(part, rng, cNode, cTan, clusterScale, fol1, fol2, detail, juvenile,
                        coneSites, coneDirs, catkinSites, catkinDirs);
                }

                // Tertiary twiglets (2 per secondary branch)
                for (int ti = 0; ti < 2; ti++)
                {
                    double tFrac = 0.50 + ti * 0.36;
                    var tRootCenter = NtbBezier(sRoot, sC0, sC1, sEnd, tFrac);
                    var tTanParent = NtbBezierTangent(sRoot, sC0, sC1, sEnd, tFrac);
                    double tAz = sAz + (ti == 0 ? -0.60 : 0.60);
                    var tOut = NtbRadial(tAz);
                    var tRoot = tRootCenter + tOut * (sRadii[(int)(tFrac * (sStations - 1))] * 0.35);
                    double tLen = sReach * rng.Range(0.32, 0.48);
                    var tEnd = tRoot + tOut * tLen + Vec3.Up * rng.Range(0.04, 0.12);

                    var tTan0 = (tTanParent * 0.40 + tOut * 0.70 + Vec3.Up * 0.25).Normalized();
                    var tPath = new[] { tRoot, tRoot + tTan0 * (tLen * 0.45), tEnd };
                    var tR = new[] { 0.0050, 0.0035, 0.0020 };
                    NtbWood(part, tPath, tR, detail, NtbFenforgeTwig, NtbFenforgeTwig, NtbFenforgeBarkDeep, 2.0, 0.3, true);

                    // Foliage clusters along twiglet
                    int twigClusters = detail switch { 2 => 1, 1 => 2, _ => 3 };
                    for (int tc = 0; tc < twigClusters; tc++)
                    {
                        double tct = 0.20 + 0.75 * (tc + 0.5) / twigClusters;
                        var cNode = Vec3.Lerp(tRoot, tEnd, tct);
                        double clusterScale = rng.Range(0.80, 1.00) * (juvenile ? 0.80 : 1.0);
                        NtbAlderFoliageCluster(part, rng, cNode, tOut, clusterScale, fol1, fol2, detail, juvenile,
                            coneSites, coneDirs, catkinSites, catkinDirs);
                    }
                }
            }
        }

        // 4. Signature Organs: Woody Alder Cones & Dangling Amber Catkins clustered at spur nodes
        if (!juvenile)
        {
            // Mini woody alder cones nestled among 3D foliage (35-42 cones)
            int coneCount = Math.Min(coneSites.Count, 40);
            for (int ci = 0; ci < coneCount; ci++)
            {
                var attach = coneSites[ci] - Vec3.Up * 0.01;
                var cDir = (coneDirs[ci] * 0.35 - Vec3.Up * 0.92).Normalized();
                double cLen = rng.Range(0.038, 0.052);
                double cRad = rng.Range(0.016, 0.022);
                NtbFenCone(part, attach, cDir, cLen, cRad);
            }

            // Glowing amber catkins dangling from spur tips (55-68 catkins)
            int catkinCount = Math.Min(catkinSites.Count, 65);
            for (int ki = 0; ki < catkinCount; ki++)
            {
                var attach = catkinSites[ki] - Vec3.Up * 0.006;
                var kDir = (catkinDirs[ki] * 0.06 - Vec3.Up * 0.995).Normalized();
                double kLen = rng.Range(0.085, 0.125);
                double kRad = rng.Range(0.0055, 0.0075);
                NtbFenCatkin(part, attach, kDir, kLen, kRad);
            }
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
        {
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
        }
    }

    // ------------------------------------------------------------------
    // 2. Banksorrow — Weeping Streamside Willow (Salix babylonica)
    // ------------------------------------------------------------------

    private static void Banksorrow(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData
        {
            Lod = m.Lod,
            FloraDetailLevel = m.FloraDetailLevel,
            FloraVisualSeed = m.FloraVisualSeed,
            PreserveUpperCanopyOnLod = true
        };
        int detail = part.FloraDetailLevel ?? 0;

        double totalH = Math.Max(0.5, height);
        double crownR = Math.Max(0.20, radius);
        double trunkBaseR = rng.Range(0.09, 0.12) * (juvenile ? 0.70 : 1.0);

        // Pale shimmering silver-sage & celadon palette
        var willowRaw = Primitives.Mix(c1, NtbBanksorrowPale, 0.65);
        var fol1 = NtbBright(willowRaw);
        var fol2 = NtbBright(Primitives.Mix(c2, NtbBanksorrowPale, 0.85));

        // 1. Gnarled Leaning Willow Trunk & Basal Buttresses
        double leanAz = rng.Range(0, Math.PI * 2);
        var leanDir = NtbRadial(leanAz);
        var ortho = NtbRadial(leanAz + Math.PI * 0.5);
        double forkH = totalH * rng.Range(0.36, 0.46);
        var pFork = leanDir * (crownR * rng.Range(0.25, 0.38)) + Vec3.Up * forkH;

        // Basal buttress roots flaring into the bank (5-7 buttresses)
        int buttressCount = juvenile ? 4 : 6;
        for (int br = 0; br < buttressCount; br++)
        {
            double bAz = leanAz + (br - (buttressCount - 1) * 0.5) * 0.85 + rng.Range(-0.12, 0.12);
            var bOut = NtbRadial(bAz);
            var bStart = bOut * (trunkBaseR * rng.Range(1.6, 2.4)) - Vec3.Up * 0.03;
            var bTop = Vec3.Up * (forkH * 0.35) + bOut * (trunkBaseR * 0.4);
            var bMid = bOut * (trunkBaseR * 1.0) + Vec3.Up * (forkH * 0.10);
            var bPath = NtbSampleBezier(bStart, bMid, bMid, bTop, 4);
            var bRadii = new[] { trunkBaseR * 0.45, trunkBaseR * 0.35, trunkBaseR * 0.25, trunkBaseR * 0.15 };
            NtbWood(part, bPath, bRadii, detail, NtbBanksorrowBark, NtbBanksorrowBark, NtbBanksorrowBarkDeep,
                ridgeFreq: 2.5, twist: 0.5, essential: true);
        }

        // Leaning sculptural trunk with muscular flared crotch
        int tStations = detail switch { 2 => 6, 1 => 8, _ => 10 };
        var cT0 = Vec3.Up * (forkH * 0.30) + leanDir * (crownR * 0.06);
        var cT1 = Vec3.Up * (forkH * 0.70) + leanDir * (crownR * 0.20) + ortho * (crownR * 0.04);
        var trunkPath = NtbSampleBezier(Vec3.Zero, cT0, cT1, pFork, tStations);
        var trunkRadii = new double[tStations];
        for (int i = 0; i < tStations; i++)
        {
            double st = i / (double)(tStations - 1);
            // Muscular trunk with basal flare, slender waist, and swollen pollard crotch at pFork
            double baseFlare = 1.0 + 0.45 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.35), 2.0);
            double crotchFlare = 1.0 + 0.55 * Math.Pow(Math.Max(0.0, (st - 0.58) / 0.42), 2.0);
            trunkRadii[i] = Math.Max(0.016, trunkBaseR * 0.70 * baseFlare * crotchFlare);
        }
        NtbWood(part, trunkPath, trunkRadii, detail, NtbBanksorrowBark, NtbBanksorrowBarkLight, NtbBanksorrowBarkDeep,
            ridgeFreq: 3.0, twist: 0.7, essential: true);

        // Swollen crotch junction knob to close the fork seam organically (NO sharp miter elbow!)
        double forkR = trunkRadii[tStations - 1];
        var crotchPath = new[] { pFork - Vec3.Up * (forkR * 0.12), pFork + Vec3.Up * (forkR * 0.10) };
        var crotchRadii = new[] { forkR * 1.02, forkR * 0.70 };
        NtbWood(part, crotchPath, crotchRadii, detail, NtbBanksorrowBark, NtbBanksorrowBarkLight, NtbBanksorrowBarkDeep,
            ridgeFreq: 2.5, twist: 0.4, essential: true);

        // 2. Wide Fountain-Arching Scaffold Boughs & Secondary Limbs with smooth crotch collars
        int boughCount = juvenile ? 5 : 7;
        var witheSites = new List<(Vec3 Attach, Vec3 Tan, double DrapeLen, double Phase)>();
        var catkinSites = new List<Vec3>();
        var trunkTanAtFork = NtbBezierTangent(Vec3.Zero, cT0, cT1, pFork, 1.0);

        for (int bi = 0; bi < boughCount; bi++)
        {
            double bAz = leanAz + Math.PI * 2 * bi / boughCount + rng.Range(-0.18, 0.18);
            var bOut = NtbRadial(bAz);
            double bReach = crownR * rng.Range(0.72, 0.98);
            double archH = totalH * rng.Range(0.78, 0.96);
            // Tip arches outward and slightly descends
            var bTip = pFork + bOut * bReach + Vec3.Up * (archH - forkH - totalH * 0.15);

            // Bough root emerges from the rim of the flared crotch
            var bRoot = pFork + bOut * (forkR * 0.42) - Vec3.Up * (forkR * 0.12);

            // Fountain curve: blends smoothly with trunk tangent at fork (NO sharp miter elbow!)
            var bTan0 = (trunkTanAtFork * 0.45 + bOut * 0.55 + Vec3.Up * 0.40).Normalized();
            var bC0 = bRoot + bTan0 * (bReach * 0.52);
            var bC1 = bTip + (-bOut * 0.60 + Vec3.Up * 0.25).Normalized() * (bReach * 0.45);

            int bStations = detail switch { 2 => 5, 1 => 6, _ => 7 };
            var bPath = NtbSampleBezier(bRoot, bC0, bC1, bTip, bStations);
            var bRadii = new double[bStations];
            for (int s = 0; s < bStations; s++)
            {
                double st = s / (double)(bStations - 1);
                double collar = 1.0 + 0.48 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.25), 2.0);
                bRadii[s] = Math.Max(0.005, trunkBaseR * MathD.Lerp(0.48, 0.10, Math.Pow(st, 0.85)) * collar);
            }
            NtbWood(part, bPath, bRadii, detail, NtbBanksorrowBark, NtbBanksorrowBarkLight, NtbBanksorrowBarkDeep,
                ridgeFreq: 2.5, twist: 0.6, essential: true);

            // Foliage along arching scaffold bough
            int scaffoldLeaves = detail switch { 2 => 8, 1 => 16, _ => 26 };
            for (int sl = 0; sl < scaffoldLeaves; sl++)
            {
                double st = 0.20 + 0.75 * sl / (double)(scaffoldLeaves - 1);
                var at = NtbBezier(pFork, bC0, bC1, bTip, st);
                double lAng = bAz + (sl % 2 == 0 ? 0.75 : -0.75);
                var lOut = NtbRadial(lAng);
                var lDir = (lOut * 0.70 + Vec3.Up * 0.50).Normalized();
                double bladeLen = rng.Range(0.11, 0.16);
                var tip = at + lDir * bladeLen;
                NtbWillowLeaf(part, at, tip, lOut, 0.013, fol1, fol2, detail, essential: sl % 2 == 0);
            }

            // Withes originating from the primary bough (4 per bough, with natural curve!)
            for (int cp = 0; cp < 4; cp++)
            {
                double ct = 0.35 + cp * 0.16;
                var pAttach = NtbBezier(pFork, bC0, bC1, bTip, ct);
                var pTan = NtbBezierTangent(pFork, bC0, bC1, bTip, ct);
                double drape = totalH * rng.Range(0.45, 0.78) * (1.0 - ct * 0.25);
                witheSites.Add((pAttach, pTan, drape, rng.Range(0, Math.PI * 2)));
            }

            // Secondary arching limbs (2 per primary bough)
            for (int si = 0; si < 2; si++)
            {
                double sFrac = 0.45 + si * 0.30;
                var sRoot = NtbBezier(pFork, bC0, bC1, bTip, sFrac);
                var sTanParent = NtbBezierTangent(pFork, bC0, bC1, bTip, sFrac);
                double sSideSign = (si == 0) ? 1.0 : -1.0;
                double sAz = bAz + sSideSign * rng.Range(0.45, 0.75);
                var sOut = NtbRadial(sAz);
                double sReach = bReach * rng.Range(0.40, 0.58);
                var sTip = sRoot + sOut * sReach + Vec3.Up * rng.Range(-0.05, 0.08);

                var sTan0 = (sTanParent * 0.40 + sOut * 0.70 + Vec3.Up * 0.30).Normalized();
                var sC0 = sRoot + sTan0 * (sReach * 0.40);
                var sC1 = sTip - (sOut * 0.80 - Vec3.Up * 0.10).Normalized() * (sReach * 0.35);

                var sPath = NtbSampleBezier(sRoot, sC0, sC1, sTip, bStations);
                var sRadii = new double[bStations];
                for (int s = 0; s < bStations; s++)
                {
                    double st = s / (double)(bStations - 1);
                    sRadii[s] = Math.Max(0.003, trunkBaseR * MathD.Lerp(0.12, 0.035, st));
                }
                NtbWood(part, sPath, sRadii, detail, NtbBanksorrowBark, NtbBanksorrowBarkLight, NtbBanksorrowBarkDeep,
                    ridgeFreq: 2.0, twist: 0.4, essential: true);

                // Withes originating from secondary limbs (3 per limb)
                for (int wp = 0; wp < 3; wp++)
                {
                    double wt = 0.30 + wp * 0.30;
                    var pAttach = NtbBezier(sRoot, sC0, sC1, sTip, wt);
                    var pTan = NtbBezierTangent(sRoot, sC0, sC1, sTip, wt);
                    double drape = totalH * rng.Range(0.40, 0.72);
                    witheSites.Add((pAttach, pTan, drape, rng.Range(0, Math.PI * 2)));
                }

                catkinSites.Add(sRoot);
            }
        }

        // 3. Graceful Weeping Curtains with Smooth Curved Transitions (NO 90° T-Junctions!)
        int curtainCount = Math.Min(witheSites.Count, juvenile ? 36 : 56);
        for (int ci = 0; ci < curtainCount; ci++)
        {
            var (cStart, bTan, drapeLen, sPhase) = witheSites[ci];

            // Smooth drooping transition: withe emerges along branch direction and sweeps downward
            var wDir0 = (bTan * 0.65 - Vec3.Up * 0.45).Normalized();
            var p0 = cStart;
            var wC0 = p0 + wDir0 * (drapeLen * 0.28);

            double sAmp = rng.Range(0.018, 0.038);
            double sFreq = rng.Range(1.8, 3.2);
            var pEnd = p0 - Vec3.Up * drapeLen + ortho * (Math.Sin(sPhase) * sAmp);
            if (pEnd.Y < 0.03) pEnd = new Vec3(pEnd.X, 0.03, pEnd.Z);

            var wC1 = pEnd + Vec3.Up * (drapeLen * 0.14) + ortho * (Math.Sin(sPhase + 1.5) * sAmp * 0.65);

            int wStations = detail switch { 2 => 4, 1 => 5, _ => 6 };
            var wPath = new Vec3[wStations];
            var wRadii = new double[wStations];
            for (int wi = 0; wi < wStations; wi++)
            {
                double wt = wi / (double)(wStations - 1);
                var curvePt = NtbBezier(p0, wC0, wC1, pEnd, wt);
                // Additional subtle sinusoidal sway along the drape
                double lateral = Math.Sin(wt * sFreq + sPhase) * sAmp * Math.Sin(Math.PI * Math.Clamp(wt, 0.05, 0.95));
                wPath[wi] = curvePt + ortho * lateral;
                wRadii[wi] = Math.Max(0.0018, MathD.Lerp(0.0065, 0.0018, wt));
            }
            NtbWood(part, wPath, wRadii, detail, NtbBanksorrowWithe, NtbBanksorrowWithe, NtbBanksorrowBarkDeep,
                ridgeFreq: 2.0, twist: 0.3, essential: true);

            // Cascading falcate willow leaves along the weeping withe
            int leavesPerCurtain = detail switch { 2 => 18, 1 => 38, _ => 66 };
            for (int lf = 0; lf < leavesPerCurtain; lf++)
            {
                double lt = 0.06 + 0.90 * lf / (double)(leavesPerCurtain - 1);
                var at = NtbAt(wPath, lt);
                double lAng = sPhase + lf * 2.39996;
                var lOut = NtbRadial(lAng);
                var lDir = (lOut * rng.Range(0.40, 0.65) - Vec3.Up * rng.Range(0.50, 0.85)).Normalized();
                double bladeLen = rng.Range(0.10, 0.15) * (juvenile ? 0.85 : 1.0);
                double halfW = rng.Range(0.009, 0.013);
                var tip = at + lDir * bladeLen;
                if (tip.Y < 0.02) tip = new Vec3(tip.X, 0.02, tip.Z);

                NtbWillowLeaf(part, at, tip, lOut, halfW, fol1, fol2, detail, essential: lf % 3 == 0);
            }

            if (ci % 2 == 0)
            {
                catkinSites.Add(NtbAt(wPath, rng.Range(0.20, 0.50)));
            }
        }

        // 4. Whimsical Signature: Glowing Dewdrop-Pearl Willow Catkins (30-40 catkins)
        if (!juvenile && catkinSites.Count > 0)
        {
            int catkinCount = Math.Min(catkinSites.Count, 38);
            for (int ki = 0; ki < catkinCount; ki++)
            {
                var attach = catkinSites[ki] + new Vec3(rng.Range(-0.02, 0.02), rng.Range(-0.01, 0.01), rng.Range(-0.02, 0.02));
                var kDir = (NtbRadial(rng.Range(0, Math.PI * 2)) * 0.25 + Vec3.Up * 0.75).Normalized();
                double kLen = rng.Range(0.045, 0.065);
                double kRad = rng.Range(0.0045, 0.0065);
                NtbWillowCatkin(part, attach, kDir, kLen, kRad);
            }
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
        {
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
        }
    }

    // ------------------------------------------------------------------
    // 3. Mirespire — Buttressed Bog Tamarack / Swamp Cypress Spire
    // ------------------------------------------------------------------

    private static void Mirespire(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData
        {
            Lod = m.Lod,
            FloraDetailLevel = m.FloraDetailLevel,
            FloraVisualSeed = m.FloraVisualSeed,
            PreserveUpperCanopyOnLod = true
        };
        int detail = part.FloraDetailLevel ?? 0;

        double totalH = Math.Max(0.5, height);
        double crownR = Math.Max(0.20, radius);
        double spireTopH = totalH * (juvenile ? 0.94 : 0.98);

        // Fluted, concave-flared cypress base (rBase ~ 0.18m, tapering smoothly up the spire)
        double rBase = crownR * 0.18 * (juvenile ? 0.75 : 1.0);
        double rMid = crownR * 0.065 * (juvenile ? 0.80 : 1.0);
        double rTop = crownR * 0.010;

        // Cooler glaucous blue-jade, soft seafoam, golden-larch tips
        var mirRaw1 = Primitives.Mix(c1, new[] { 0.12, 0.34, 0.30 }, 0.50);
        var mirRaw2 = Primitives.Mix(c2, new[] { 0.24, 0.48, 0.38 }, 0.50);
        var fol1 = NtbBright(mirRaw1);
        var fol2 = NtbBright(mirRaw2);
        var folAutumn = NtbBright(new[] { 0.60, 0.66, 0.22 });

        // 1. Swamp-Cypress Buttressed Base & Connected Pneumatophore Knees (7-9 knees)
        int kneeCount = juvenile ? 5 : (7 + rng.NextInt(3));
        double kneeBaseAz = rng.Range(0, Math.PI * 2);
        for (int ki = 0; ki < kneeCount; ki++)
        {
            double kAz = kneeBaseAz + Math.PI * 2 * ki / kneeCount + rng.Range(-0.20, 0.20);
            var kOut = NtbRadial(kAz);
            double dist = crownR * rng.Range(0.28, 0.52);
            var kPos = kOut * dist;
            double kHeight = totalH * rng.Range(0.055, 0.095);
            double kRadius = rBase * rng.Range(0.28, 0.40);

            // Fluted root rib flowing continuously from the trunk base to and beyond the knee
            var pRootStart = kOut * (rBase * 0.85);
            var pRootMid = Vec3.Lerp(pRootStart, kPos, 0.5) - Vec3.Up * 0.005;
            var pRootEnd = kPos;
            var pRootTip = kOut * (dist + kRadius * 1.6) - Vec3.Up * 0.03;

            var ribPath = new[] { pRootStart, pRootMid, pRootEnd, pRootTip };
            var ribRadii = new[] { rBase * 0.35, rBase * 0.24, kRadius * 0.85, 0.012 };
            NtbWood(part, ribPath, ribRadii, detail, NtbMirespireBarkLower, NtbMirespireKnee, NtbMirespireBarkDeep,
                ridgeFreq: 3.5, twist: 0.2, essential: true);

            // Rounded pneumatophore knee dome rising directly from the root rib
            NtbSwampKnee(part, kPos, kHeight, kRadius, NtbMirespireKnee);
        }

        // 2. Organic Spire Trunk with subtle wind-lean sway & fluted buttress
        int tStations = detail switch { 2 => 7, 1 => 9, _ => 12 };
        var trunkPath = new Vec3[tStations];
        var trunkRadii = new double[tStations];
        double swayAz = rng.Range(0, Math.PI * 2);
        var swayDir = NtbRadial(swayAz);
        for (int i = 0; i < tStations; i++)
        {
            double st = i / (double)(tStations - 1);
            // Subtle organic sway & wind lean (0.05m sway + 0.04m progressive wind lean)
            double sway = Math.Sin(st * Math.PI) * 0.055 + Math.Sin(st * Math.PI * 2.0) * 0.020;
            trunkPath[i] = Vec3.Up * (spireTopH * st) + swayDir * (sway + st * st * 0.04);
            double flare = Math.Exp(-st * 8.0);
            trunkRadii[i] = Math.Max(rTop, MathD.Lerp(rMid, rTop, Math.Pow(st, 0.85)) + (rBase - rMid) * flare);
        }
        NtbWood(part, trunkPath, trunkRadii, detail, NtbMirespireBarkLower, NtbMirespireBarkUpper, NtbMirespireBarkDeep,
            ridgeFreq: 5.0, twist: 0.5, essential: true);

        // 3. Dense Tiered Conifer Spire Architecture (18% to 94% height)
        int whorlCount = juvenile ? 11 : 16;
        var coneSites = new List<(Vec3 Pos, Vec3 Out)>();

        for (int w = 0; w < whorlCount; w++)
        {
            double baseWt = w / (double)(whorlCount - 1);
            double whorlJitter = (w == 0 || w == whorlCount - 1) ? 0.0 : (Rng.HashUnit(seed, 0x4D49524553504952UL, (ulong)w) * 2.0 - 1.0) * 0.008;
            double wt = Math.Clamp(baseWt + whorlJitter, 0.0, 1.0);
            var wRootCenter = NtbAt(trunkPath, (0.16 + 0.78 * wt) * (totalH / spireTopH));
            double trunkRadiusAtWhorl = trunkRadii[Math.Min(trunkRadii.Length - 1, (int)((0.16 + 0.78 * wt) * (tStations - 1)))];
            var trunkTan = NtbAt(trunkPath, Math.Min(0.99, (0.16 + 0.78 * wt) * (totalH / spireTopH) + 0.05))
                - NtbAt(trunkPath, Math.Max(0.0, (0.16 + 0.78 * wt) * (totalH / spireTopH) - 0.05));
            trunkTan = NtbSafe(trunkTan, Vec3.Up);

            // Interleaved spiral tiers via golden angle
            double whorlAz = rng.Range(0, Math.PI * 2) + w * 2.39996;

            // Pyramidal conifer taper
            double profileReach = Math.Pow(1.0 - wt, 0.85) * (juvenile ? 0.72 : 1.0);
            double whorlReach = crownR * MathD.Lerp(0.85, 0.16, wt) * profileReach / (1.0 - wt + 0.02);
            whorlReach = Math.Clamp(whorlReach, crownR * 0.12, crownR * 0.88);

            int branchesPerWhorl = (wt > 0.75) ? (juvenile ? 3 : 4) : (juvenile ? 4 : 5);
            for (int bi = 0; bi < branchesPerWhorl; bi++)
            {
                double bAz = whorlAz + Math.PI * 2 * bi / branchesPerWhorl + rng.Range(-0.24, 0.24);
                var bOut = NtbRadial(bAz);
                var bLat = NtbRadial(bAz + Math.PI * 0.5);
                double bLen = whorlReach * rng.Range(0.78, 1.14);

                // Branch root starts on outer surface of trunk for seamless junction
                var wRoot = wRootCenter + bOut * (trunkRadiusAtWhorl * 0.35);

                // Conifer branching architecture:
                // - Top spire (wt > 0.75): branches ascend sharply at 35° to 45°
                // - Middle tiers (wt 0.35 to 0.75): branches arch outward at ~62°-75° with gentle droop and upturned tips
                // - Lower tiers (wt < 0.35): longer drooping branches under gravity
                Vec3 bTip, bC0, bC1, bTan0;
                if (wt > 0.75)
                {
                    double pitch = MathD.Lerp(55.0 * Math.PI / 180.0, 38.0 * Math.PI / 180.0, (wt - 0.75) / 0.25);
                    var bDir = (bOut * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
                    bTip = wRoot + bDir * bLen;
                    bTan0 = (trunkTan * 0.35 + bDir * 0.65).Normalized();
                    bC0 = wRoot + bTan0 * (bLen * 0.40);
                    bC1 = bTip - bDir * (bLen * 0.35);
                }
                else if (wt >= 0.35)
                {
                    double pitch = MathD.Lerp(75.0 * Math.PI / 180.0, 62.0 * Math.PI / 180.0, (wt - 0.35) / 0.40);
                    double droop = -bLen * 0.06;
                    double tipLift = bLen * 0.08;
                    bTip = wRoot + bOut * bLen + Vec3.Up * (bLen * Math.Cos(pitch) + tipLift);
                    bTan0 = (trunkTan * 0.25 + bOut * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
                    bC0 = wRoot + bTan0 * (bLen * 0.40) + Vec3.Up * droop;
                    bC1 = bTip - (bOut * 0.88 + Vec3.Up * 0.28).Normalized() * (bLen * 0.35);
                }
                else
                {
                    double droop = -bLen * 0.12;
                    double tipLift = bLen * 0.04;
                    bTip = wRoot + bOut * bLen + Vec3.Up * tipLift;
                    bTan0 = (trunkTan * 0.25 + bOut * 0.88 + Vec3.Up * 0.15).Normalized();
                    bC0 = wRoot + bTan0 * (bLen * 0.40) + Vec3.Up * droop;
                    bC1 = bTip - (bOut * 0.88 + Vec3.Up * 0.18).Normalized() * (bLen * 0.35);
                }

                // Smooth flared branch collar at junction with trunk (NO perpendicular T-joint sticks!)
                int bStations = detail switch { 2 => 4, 1 => 5, _ => 6 };
                var bPath = NtbSampleBezier(wRoot, bC0, bC1, bTip, bStations);
                var bRadii = new double[bStations];
                double bBaseR = Math.Max(0.005, trunkRadiusAtWhorl * (1.0 - wt * 0.45) * 0.42);
                for (int s = 0; s < bStations; s++)
                {
                    double st = s / (double)(bStations - 1);
                    double collar = 1.0 + 0.60 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.24), 2.0);
                    bRadii[s] = Math.Max(0.0022, MathD.Lerp(bBaseR, 0.0020, Math.Pow(st, 0.85)) * collar);
                }
                NtbWood(part, bPath, bRadii, detail, NtbMirespireBranch, NtbMirespireBranch, NtbMirespireBarkDeep,
                    ridgeFreq: 2.5, twist: 0.3, essential: true);

                // Record cone attachment site on upper surface of horizontal/mid branches
                if (wt >= 0.35 && wt <= 0.80 && rng.NextDouble() < 0.65)
                {
                    double ct = rng.Range(0.28, 0.58);
                    var cPt = NtbBezier(wRoot, bC0, bC1, bTip, ct);
                    coneSites.Add((cPt, bOut));
                }

                // Secondary branchlets with flared collars
                int secCount = (wt > 0.70) ? 1 : (wt > 0.38 ? 2 : 3);
                for (int si = 0; si < secCount; si++)
                {
                    double sFrac = 0.32 + si * 0.26;
                    var sRootCenter = NtbBezier(wRoot, bC0, bC1, bTip, sFrac);
                    var sTanParent = NtbBezierTangent(wRoot, bC0, bC1, bTip, sFrac);
                    double sSideSign = (si % 2 == 0) ? 1.0 : -1.0;
                    double sAz = bAz + sSideSign * rng.Range(0.45, 0.70);
                    var sOut = NtbRadial(sAz);
                    var sRoot = sRootCenter + sOut * (bRadii[(int)(sFrac * (bStations - 1))] * 0.35);
                    double sLen = bLen * rng.Range(0.32, 0.46);
                    var sTip = sRoot + sOut * sLen + Vec3.Up * rng.Range(0.01, 0.05);

                    var sTan0 = (sTanParent * 0.40 + sOut * 0.75 + Vec3.Up * 0.20).Normalized();
                    var sPath = new[] { sRoot, sRoot + sTan0 * (sLen * 0.45), sTip };
                    var sRadii = new[] { 0.0042, 0.0028, 0.0018 };
                    NtbWood(part, sPath, sRadii, detail, NtbMirespireBranch, NtbMirespireBranch, NtbMirespireBarkDeep,
                        ridgeFreq: 2.0, twist: 0.3, essential: true);

                    // 3D needle rosettes along secondary branchlets
                    int rosettesOnBranchlet = detail switch { 2 => 3, 1 => 5, _ => 7 };
                    for (int rp = 0; rp < rosettesOnBranchlet; rp++)
                    {
                        double rt = 0.18 + 0.80 * rp / (double)(rosettesOnBranchlet - 1);
                        var rPos = Vec3.Lerp(sRoot, sTip, rt);
                        double rAng = sAz + (rp % 2 == 0 ? 0.6 : -0.6);
                        var rOut = NtbRadial(rAng);
                        double rSize = rng.Range(0.080, 0.125);
                        NtbTamarackRosette(part, rPos, rOut, rSize, fol1, fol2, folAutumn, detail, essential: rp % 2 == 0);
                    }
                }

                // 3D needle rosettes along primary branch tip & mid
                int mainRosettes = detail switch { 2 => 3, 1 => 5, _ => 6 };
                for (int mr = 0; mr < mainRosettes; mr++)
                {
                    double mt = 0.45 + 0.52 * mr / (double)(mainRosettes - 1);
                    var at = NtbBezier(wRoot, bC0, bC1, bTip, mt);
                    double sideSign = (mr % 2 == 0) ? 1.0 : -1.0;
                    var rOut = (bOut * 0.70 + bLat * (sideSign * 0.70)).Normalized();
                    double rSize = rng.Range(0.085, 0.130);
                    NtbTamarackRosette(part, at, rOut, rSize, fol1, fol2, folAutumn, detail, essential: mr % 2 == 0);
                }
            }
        }

        // Leader needle tufts at spire top
        {
            int leaderSprays = detail switch { 2 => 8, 1 => 14, _ => 20 };
            for (int ls = 0; ls < leaderSprays; ls++)
            {
                double lt = ls / (double)(leaderSprays - 1);
                var at = Vec3.Up * (spireTopH * (0.88 + 0.11 * lt));
                double lAz = rng.Range(0, Math.PI * 2);
                var lOut = NtbRadial(lAz);
                double sprayLen = rng.Range(0.075, 0.110);
                NtbTamarackRosette(part, at, lOut + Vec3.Up * 0.5, sprayLen, fol1, fol2, folAutumn, detail, essential: true);
            }
        }

        // 4. Upright Rose-Amethyst & Amber Larch Cones perched naturally on upper surfaces of branchlets
        if (!juvenile && coneSites.Count > 0)
        {
            int coneCount = Math.Min(coneSites.Count, 40);
            for (int ci = 0; ci < coneCount; ci++)
            {
                var (cPt, bOut) = coneSites[ci];
                // Perched upright on upper surface of branch
                var attach = cPt + Vec3.Up * 0.008;
                var coneUp = (Vec3.Up * 0.92 + bOut * 0.15).Normalized();
                double cHeight = rng.Range(0.038, 0.052);
                double cRadius = rng.Range(0.022, 0.030);
                NtbLarchCone(part, attach, coneUp, cHeight, cRadius);
            }
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
        {
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
        }
    }
}
