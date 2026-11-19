using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Expansion shrub suite (2026-11-07 botanical overhaul - WP-Shrubs-Rev2):
/// - Miremyrtle (bog myrtle / sweet gale with luminous amber candle-spikes & aromatic spiral leaves)
/// - Nutglade (coppice hazel with frilled lantern-husks & graceful pendulous catkins)
/// - Thornmantle (fairytale hawthorn thicket with gnarled wood, sharp ivory-tipped thorns, may-blossoms & ruby haws)
/// - Greywatch (sculptural alpine krummholz juniper with tiered volumetric cloud-pads & sapphire berry-cones)
/// - Silverfelt (sub-alpine cushion willow with undulating decumbent stems, felted silver-celadon leaves & pearl-silk catkins)
///
/// Realism & Optical Rules:
/// 1. Upward Lamina Normals: Every leaf builder ensures normal · Vec3.Up >= 0 (adaxial face points to sky).
/// 2. Woody Tagging: All trunks, boughs, twigs, stools, and nodules set UV2 = (2.0, 0.0) for procedural bark PBR.
/// 3. Whimsical Organs: Blossoms, catkins, nuts, and berries are marked with UV2.x in [1.18..1.35] and UV2.y = 0.85
///    to retain authored pigments and produce gentle dusk/night bioluminescence.
/// 4. Triangle Budget: Strictly between 35,000 and 65,000 triangles at Detail 0, scaling down cleanly for LOD.
/// 5. Natural Junctions: Smooth cubic Bezier curves with organic taper and flared branch-collar swellings at forks;
///    never sharp geometric creases or mitered elbows!
/// </summary>
public static partial class OrganismMeshes
{
    internal static void NewShrubsFinish(MeshData m, MeshData part, double radius, double height)
    {
        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
        {
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
        }
    }

    private static int NewShrubsSides(MeshData part, int full)
    {
        if (part.Lod is { } lod) return lod.Sides(Math.Max(4, full));
        return Math.Max(4, full);
    }

    private static int NewShrubsStations(MeshData part, int full)
    {
        if (part.Lod is { } lod) return lod.Segments(full, 3);
        return full;
    }

    private static void NewShrubsPinLeaf(MeshData part, int firstVertex) =>
        part.EssentialSpans.Add((firstVertex, part.VertexCount - firstVertex));

    /// <summary>
    /// Writes a continuous woody tube with physical longitudinal UV mapping and UV2 = (2.0, 0.0).
    /// </summary>
    private static void WoodTube(MeshData part, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii, int sides, double[] barkCol)
    {
        double length = 0;
        var distance = new double[path.Count];
        for (int i = 1; i < path.Count; i++)
            distance[i] = length += (path[i] - path[i - 1]).Length;
        Primitives.Tube(part, path, radii, sides, (i, v) => (barkCol, 1.0, distance[i], v, 2.0, 0.0));
    }

    /// <summary>
    /// Writes a woody burl, root collar, or swollen nodal boss with UV2 = (2.0, 0.0).
    /// </summary>
    private static void WoodEllipsoid(MeshData part, Vec3 centre, Vec3 radii, int rings, int sides, double[] barkCol)
    {
        Primitives.Ellipsoid(part, centre, radii, rings, sides, (u, v) => (barkCol, 1.0, u, v, 2.0, 0.0));
    }

    /// <summary>
    /// Tags a freshly authored reproductive organ (catkin, blossom, nut, berry) with custom UV2
    /// and registers it as an essential span so it survives LOD culling.
    /// </summary>
    private static void MarkOrgan(MeshData part, int firstVertex, int count, float uv2x = 1.28f, float uv2y = 0.85f)
    {
        int last = Math.Min(part.VertexCount, firstVertex + count);
        for (int v = Math.Max(0, firstVertex); v < last; v++)
        {
            part.UV2[v * 2] = uv2x;
            part.UV2[v * 2 + 1] = uv2y;
        }
        part.EssentialSpans.Add((firstVertex, count));
    }

    /// <summary>
    /// Evaluates a 3D cubic Bezier spline at parameter t in [0, 1].
    /// </summary>
    private static Vec3 BezierCubic(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        return p0 * (uu * u) + p1 * (3.0 * uu * t) + p2 * (3.0 * u * tt) + p3 * (tt * t);
    }

    /// <summary>
    /// Samples a smooth multi-station polyline along a cubic Bezier curve.
    /// </summary>
    private static Vec3[] SampleBezier(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = (double)i / Math.Max(1, stations - 1);
            pts[i] = BezierCubic(p0, p1, p2, p3, t);
        }
        return pts;
    }

    /// <summary>
    /// Computes the forward tangent vector along a polyline at parameter t in [0, 1].
    /// </summary>
    private static Vec3 PathTangent(IReadOnlyList<Vec3> path, double t)
    {
        if (path.Count < 2) return Vec3.Up;
        double f = Math.Clamp(t, 0.0, 1.0) * (path.Count - 1);
        int i = Math.Min(path.Count - 2, (int)f);
        var dir = path[i + 1] - path[i];
        return dir.LengthSq > 1e-8 ? dir.Normalized() : Vec3.Up;
    }

    /// <summary>
    /// Writes a swollen branch collar at a fork junction with UV2 = (2.0, 0.0).
    /// </summary>
    private static void BranchCollar(MeshData part, Vec3 attachPos, double radius, double[] barkCol)
    {
        WoodEllipsoid(part, attachPos, new Vec3(radius * 1.25, radius * 0.95, radius * 1.25), 2, 6, barkCol);
    }

    /// <summary>
    /// Builds a single-sheet curved leaf with guaranteed sky-facing upper lamina normals (dir x side · Up >= 0.08).
    /// </summary>
    private static void LeafUpward(MeshData part, Vec3 root, Vec3 tip, Vec3 sideHint, double halfWidth,
        double[] rootCol, double[] tipCol, double camber = 0.04, int longitudinal = 4, double asymmetry = 0.0)
    {
        var axis = tip - root;
        double len = axis.Length;
        if (len < 1e-6 || halfWidth <= 1e-6) return;
        var dir = axis / len;
        var side = sideHint - dir * sideHint.Dot(dir);
        if (side.LengthSq < 1e-8)
        {
            side = Vec3.Up.Cross(dir);
            if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0).Cross(dir);
        }
        side = side.Normalized();
        // Invert side if normal dir x side points downward, guaranteeing upward adaxial surface
        if (dir.Cross(side).Dot(Vec3.Up) < -1e-4)
            side = -side;

        // Bias side towards Vec3.Up x dir if normal is nearly horizontal, ensuring positive upward tilt
        var n = dir.Cross(side);
        if (n.Dot(Vec3.Up) < 0.08)
        {
            var skySide = Vec3.Up.Cross(dir);
            if (skySide.LengthSq > 1e-4)
            {
                skySide = skySide.Normalized();
                var altSide = dir.Cross(skySide).Dot(Vec3.Up) > 0 ? skySide : -skySide;
                side = (side * 0.4 + altSide * 0.6).Normalized();
                if (dir.Cross(side).Dot(Vec3.Up) < -1e-4)
                    side = -side;
            }
        }

        int v0 = part.VertexCount;
        Primitives.CurvedLeaf(part, root, tip, side, halfWidth, rootCol, tipCol,
            camber: camber, longitudinal: longitudinal, asymmetry: asymmetry);
        NewShrubsPinLeaf(part, v0);
    }

    /// <summary>
    /// Short branchlet off a woody twig with smooth cubic curve, flared branch collar, and upward-normal leaves.
    /// </summary>
    private static void NewShrubsBranchlet(MeshData part, Vec3 basePos, Vec3 parentTangent, Vec3 outDir, double len,
        double leafLen, double leafHalfWidth, int leafCount, int longitudinal,
        double[] leafBase, double[] leafTip, double[] barkCol, Rng rng, double baseAz)
    {
        BranchCollar(part, basePos, 0.0055, barkCol);
        var p0 = basePos;
        var p1 = basePos + (parentTangent * 0.45 + outDir * 0.55).Normalized() * (len * 0.32);
        var p2 = basePos + outDir * (len * 0.70) + Vec3.Up * (len * 0.08);
        var p3 = basePos + outDir * len + Vec3.Up * (len * 0.12);
        var bPath = SampleBezier(p0, p1, p2, p3, 5);
        var bRadii = new[] { 0.0050, 0.0036, 0.0026, 0.0018, 0.0012 };
        WoodTube(part, bPath, bRadii, NewShrubsSides(part, 5), barkCol);

        for (int j = 0; j < leafCount; j++)
        {
            double t = 0.18 + 0.74 * ((j + 0.5) / leafCount) + rng.Range(-0.02, 0.02);
            var at = NewShrubsAlong(bPath, Math.Clamp(t, 0.0, 1.0));
            double leafAz = baseAz + j * 2.399963 + rng.Range(-0.14, 0.14);
            var leafDir = new Vec3(Math.Cos(leafAz) * 0.82, 0.38, Math.Sin(leafAz) * 0.82).Normalized();
            double lenJ = leafLen * rng.Range(0.88, 1.08);
            var lTip = at + leafDir * lenJ + Vec3.Up * (lenJ * 0.10);
            var sideHint = Vec3.Up.Cross(leafDir);
            LeafUpward(part, at, lTip, sideHint, leafHalfWidth, leafBase, leafTip,
                camber: lenJ * 0.08, longitudinal: longitudinal, asymmetry: rng.Range(-0.05, 0.05));
        }
    }

    private static Vec3 NewShrubsAlong(IReadOnlyList<Vec3> path, double t)
    {
        if (path.Count < 1) return Vec3.Zero;
        if (path.Count < 2) return path[0];
        double f = Math.Clamp(t, 0.0, 1.0) * (path.Count - 1);
        int i = Math.Min(path.Count - 2, (int)f);
        return Vec3.Lerp(path[i], path[i + 1], f - i);
    }

    private static void BuildTaperedWood(MeshData part, IReadOnlyList<Vec3> path, double rStart, double rEnd,
        int sides, double[] barkCol, bool collarAtStart = true, double taperPow = 1.0)
    {
        if (path.Count < 2) return;
        if (collarAtStart)
            BranchCollar(part, path[0], rStart * 1.12, barkCol);
        var radii = new double[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            double t = i / (double)(path.Count - 1);
            radii[i] = MathD.Lerp(rStart, rEnd, Math.Pow(t, taperPow));
        }
        WoodTube(part, path, radii, sides, barkCol);
    }

    private static (Vec3[] Path, Vec3[] NodePos, Vec3[] NodeOutDir) BuildZigzagWood(
        MeshData part, Vec3 start, Vec3 initialDir, double totalLen, int segments,
        double rStart, double rEnd, double yawKinkRad, double pitchSpread, double archBow,
        int sides, double[] barkCol, Rng rng, double handSign = 1.0)
    {
        var dir = initialDir.Normalized();
        if (dir.LengthSq < 1e-6) dir = Vec3.Up;
        var pts = new List<Vec3>(segments * 2 + 1) { start };
        var nodePos = new Vec3[segments];
        var nodeOut = new Vec3[segments];
        double segLen = totalLen / Math.Max(1, segments);
        var cur = start;

        for (int s = 0; s < segments; s++)
        {
            double frac = (s + 1) / (double)segments;
            double sign = ((s % 2 == 0) ? 1.0 : -1.0) * handSign;
            double az = Math.Atan2(dir.Z, dir.X) + sign * yawKinkRad * rng.Range(0.75, 1.25);
            double dy = dir.Y + rng.Range(-pitchSpread, pitchSpread) - archBow * frac;
            var horiz = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var horizLen = Math.Sqrt(Math.Max(0.08, 1.0 - Math.Clamp(dy, -0.75, 0.88) * Math.Clamp(dy, -0.75, 0.88)));
            dir = (horiz * horizLen + Vec3.Up * Math.Clamp(dy, -0.75, 0.88)).Normalized();

            double thisLen = segLen * rng.Range(0.84, 1.18);
            var mid = cur + dir * (thisLen * 0.5) + new Vec3(-dir.Z, 0, dir.X) * (sign * thisLen * 0.08);
            var next = cur + dir * thisLen;
            pts.Add(mid);
            pts.Add(next);
            nodePos[s] = next;
            nodeOut[s] = (dir + horiz * (sign * 0.45)).Normalized();
            cur = next;
        }

        var path = pts.ToArray();
        BuildTaperedWood(part, path, rStart, rEnd, sides, barkCol, collarAtStart: true, taperPow: 0.92);
        return (path, nodePos, nodeOut);
    }

    private static void EmitLeafRosette(MeshData part, Vec3 origin, int count, double leafLen, double halfWidth,
        double radialFrac, double pitchMin, double pitchMax, double[] leafBase, double[] leafTip,
        Rng rng, double phase, int longitudinal = 4, double camberFrac = 0.09)
    {
        for (int i = 0; i < count; i++)
        {
            double az = phase + i * 2.399963 + rng.Range(-0.18, 0.18);
            double pitch = rng.Range(pitchMin, pitchMax);
            var dir = new Vec3(Math.Cos(az) * radialFrac, pitch, Math.Sin(az) * radialFrac).Normalized();
            double len = leafLen * rng.Range(0.84, 1.14);
            var tip = origin + dir * len + Vec3.Up * (len * 0.08);
            var sideHint = Vec3.Up.Cross(dir);
            LeafUpward(part, origin, tip, sideHint, halfWidth * (len / Math.Max(1e-5, leafLen)),
                leafBase, leafTip, camber: len * camberFrac, longitudinal: longitudinal, asymmetry: rng.Range(-0.08, 0.08));
        }
    }

    // =================================================================================================
    // 1. ShrubMiremyrtle: Intricately Forked Copper-Twig Bog Myrtle Cloud with Amber Candle Catkins
    // =================================================================================================
    private static void ShrubMiremyrtle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.58 : 1.0;

        // Botanical palette: warm coppery-plum / red-bronze bark, sunlit olive-emerald foliage with pale resin midrib
        var barkCol = Primitives.Mix(new[] { 0.36, 0.22, 0.18 }, c1, 0.18);
        var twigCol = Primitives.Mix(new[] { 0.44, 0.25, 0.19 }, c1, 0.16);
        var leafBase = Primitives.Mix(c1, new[] { 0.42, 0.58, 0.36 }, 0.72);
        var leafTip = Primitives.Mix(c2, new[] { 0.56, 0.74, 0.46 }, 0.72);
        var resinCol = new[] { 0.98, 0.78, 0.24 };

        void MiremyrtleCandleCatkin(Vec3 at, Vec3 outDir)
        {
            int v0 = part.VertexCount;
            double catLen = (juvenile ? 0.036 : 0.050) * rng.Range(0.92, 1.10) * heightScale;
            var ped0 = at;
            var ped1 = at + outDir * 0.008 + Vec3.Up * (catLen * 0.16);
            var ped2 = at + outDir * 0.011 + Vec3.Up * (catLen * 0.48);
            var ped3 = at + outDir * 0.006 + Vec3.Up * (catLen * 0.78);
            var pedPath = SampleBezier(ped0, ped1, ped2, ped3, 4);
            WoodTube(part, pedPath, new[] { 0.0030, 0.0022, 0.0017, 0.0012 }, NewShrubsSides(part, 5), twigCol);

            int tiers = cull ? 4 : 6;
            for (int t = 0; t < tiers; t++)
            {
                double frac = (double)t / Math.Max(1, tiers - 1);
                var cPos = BezierCubic(ped1, ped2, ped3, ped3 + Vec3.Up * (catLen * 0.32), frac);
                double r = Math.Max(0.0032, 0.0115 * (1.0 - 0.42 * frac));
                double[] scaleCol = frac < 0.35
                    ? Primitives.Mix(new[] { 0.65, 0.40, 0.16 }, new[] { 0.90, 0.64, 0.18 }, frac / 0.35)
                    : Primitives.Mix(new[] { 0.90, 0.64, 0.18 }, new[] { 0.98, 0.88, 0.32 }, (frac - 0.35) / 0.65);
                Primitives.Ellipsoid(part, cPos, new Vec3(r, 0.0048, r), 2, 5, (u, v) => (scaleCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.28f, 0.85f);
        }

        void MiremyrtleResinDroplets(Vec3 at)
        {
            if (cull) return;
            int v0 = part.VertexCount;
            for (int d = 0; d < 2; d++)
            {
                var off = new Vec3(rng.Range(-0.004, 0.004), rng.Range(0.001, 0.005), rng.Range(-0.004, 0.004));
                Primitives.Ellipsoid(part, at + off, new Vec3(0.0030, 0.0035, 0.0030), 2, 4,
                    (u, v) => (resinCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.28f, 0.85f);
        }

        // Subtle low peat collar anchored cleanly into ground (no oversized knobby lump)
        WoodEllipsoid(part, new Vec3(0, height * 0.012, 0),
            new Vec3(radius * 0.14, height * 0.022, radius * 0.14), 2, 7, barkCol);

        var catkinAxils = new List<(Vec3 Pos, Vec3 Dir)>();

        // 4-5 asymmetrical crooked basal trunks dissolving repeatedly through 3 orders of fine Y-forking
        int trunkCount = juvenile ? 3 : 4 + rng.NextInt(2);
        double azOffset = rng.Range(0, Math.PI * 2.0);
        for (int t = 0; t < trunkCount; t++)
        {
            double baseAz = azOffset + t * (Math.PI * 2.0 / trunkCount) + rng.Range(-0.34, 0.34);
            var radial = new Vec3(Math.Cos(baseAz), 0, Math.Sin(baseAz));
            var tangent = new Vec3(-Math.Sin(baseAz), 0, Math.Cos(baseAz));
            var root = radial * (radius * rng.Range(0.04, 0.12) * heightScale);

            double leanOut = radius * rng.Range(0.32, 0.56) * heightScale;
            double trunkTopH = height * rng.Range(0.30, 0.46) * heightScale;
            double swerve = rng.Range(-0.14, 0.14) * radius * heightScale;

            var p0 = root;
            var p1 = root + radial * (leanOut * 0.30) + tangent * swerve + Vec3.Up * (trunkTopH * 0.42);
            var p2 = radial * (leanOut * 0.80) - tangent * (swerve * 0.6) + Vec3.Up * (trunkTopH * 0.85);
            var p3 = radial * leanOut + tangent * (swerve * 0.35) + Vec3.Up * trunkTopH;
            var trunkPath = SampleBezier(p0, p1, p2, p3, NewShrubsStations(part, 6));
            double trunkR0 = (juvenile ? 0.012 : 0.017) * rng.Range(0.88, 1.14);
            double trunkR1 = trunkR0 * 0.56;
            BuildTaperedWood(part, trunkPath, trunkR0, trunkR1, NewShrubsSides(part, 6), barkCol);

            // Order 2: 2-3 forked secondary crooked limbs per trunk flaring out to build a broad irregular cloud
            int secCount = juvenile ? 2 : (t % 2 == 0 ? 3 : 2);
            for (int b = 0; b < secCount; b++)
            {
                double attachT = 0.52 + 0.46 * (b / (double)Math.Max(1, secCount - 1));
                var bBase = NewShrubsAlong(trunkPath, attachT);
                var bParentTan = PathTangent(trunkPath, attachT);
                double fanSpread = (b - (secCount - 1) * 0.5) * rng.Range(0.75, 1.05) + rng.Range(-0.22, 0.22);
                double secAz = baseAz + fanSpread;
                var secRad = new Vec3(Math.Cos(secAz), 0, Math.Sin(secAz));
                var secTan = new Vec3(-Math.Sin(secAz), 0, Math.Cos(secAz));

                bool outerArch = b == 0;
                double secReach = radius * (outerArch ? rng.Range(0.55, 0.78) : rng.Range(0.34, 0.52)) * heightScale;
                double secRise = height * (outerArch ? rng.Range(0.22, 0.38) : rng.Range(0.34, 0.48)) * heightScale;
                double secSide = rng.Range(-0.14, 0.14) * radius * heightScale;

                var bDir = (secRad * secReach + secTan * secSide + Vec3.Up * secRise).Normalized();
                double bLen = Math.Sqrt(secReach * secReach + secRise * secRise);
                var bp0 = bBase - bParentTan * (trunkR1 * 0.35);
                var bp1 = bBase + (bParentTan * 0.48 + bDir * 0.52).Normalized() * (bLen * 0.34);
                var bp2 = bBase + secRad * (secReach * 0.74) - secTan * (secSide * 0.7) + Vec3.Up * (secRise * 0.78);
                var bp3 = bBase + secRad * secReach + secTan * (secSide * 0.4) + Vec3.Up * secRise;
                var secPath = SampleBezier(bp0, bp1, bp2, bp3, NewShrubsStations(part, 6));
                double secR0 = trunkR1 * (secCount == 3 ? 0.64 : 0.74);
                double secR1 = secR0 * 0.44;
                BuildTaperedWood(part, secPath, secR0, secR1, NewShrubsSides(part, 5), barkCol);

                // Order 3: dichotomous forked fine copper twigs forming the fine rounded shrub silhouette
                int twigForks = cull ? 2 : (juvenile ? 2 : 3);
                for (int tw = 0; tw < twigForks; tw++)
                {
                    double twT = 0.42 + 0.56 * (tw / (double)Math.Max(1, twigForks - 1));
                    var twBase = NewShrubsAlong(secPath, twT);
                    var twParentTan = PathTangent(secPath, twT);
                    double twAz = secAz + (tw % 2 == 0 ? 0.58 : -0.62) + rng.Range(-0.22, 0.22);
                    var twH = new Vec3(Math.Cos(twAz), 0, Math.Sin(twAz));
                    double twLen = radius * rng.Range(0.36, 0.56) * heightScale;
                    double twLift = twLen * rng.Range(0.38, 0.78);
                    var twDir = (twH * twLen + Vec3.Up * twLift).Normalized();

                    var tp0 = twBase;
                    var tp1 = twBase + (twParentTan * 0.45 + twDir * 0.55).Normalized() * (twLen * 0.36);
                    var tp2 = twBase + twH * (twLen * 0.76) + Vec3.Up * (twLift * 0.82);
                    var tp3 = twBase + twH * twLen + Vec3.Up * twLift;
                    var twPath = SampleBezier(tp0, tp1, tp2, tp3, NewShrubsStations(part, 5));
                    double twR0 = secR1 * 0.85;
                    double twR1 = 0.0014;
                    BuildTaperedWood(part, twPath, twR0, twR1, NewShrubsSides(part, 4), twigCol);
                    var twTip = twPath[^1];

                    if (tw % 2 == 1)
                        MiremyrtleResinDroplets(NewShrubsAlong(twPath, 0.25));
                    catkinAxils.Add((twTip, (twDir * 0.5 + Vec3.Up * 0.5).Normalized()));

                    // Spiralled aromatic elliptic leaves clustered on fine distal twig + terminal tip
                    int twLeaves = juvenile ? 6 : (cull ? 6 : 12);
                    for (int j = 0; j < twLeaves; j++)
                    {
                        double lt = 0.22 + 0.74 * ((j + 0.5) / twLeaves) + rng.Range(-0.02, 0.02);
                        var at = NewShrubsAlong(twPath, Math.Clamp(lt, 0.0, 1.0));
                        double lAz = twAz + j * 2.399963 + rng.Range(-0.18, 0.18);
                        var lDir = new Vec3(Math.Cos(lAz) * 0.84, rng.Range(0.24, 0.52), Math.Sin(lAz) * 0.84).Normalized();
                        double len = height * rng.Range(0.064, 0.088) * heightScale;
                        var lTip = at + lDir * len + Vec3.Up * (len * 0.10);
                        LeafUpward(part, at, lTip, Vec3.Up.Cross(lDir), len * 0.22, leafBase, leafTip,
                            camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
                    }
                    EmitLeafRosette(part, twTip, juvenile ? 3 : 4, height * 0.058 * heightScale,
                        height * 0.0125 * heightScale, 0.68, 0.42, 0.74, leafBase, leafTip, rng, twAz + 0.6);

                    // Order 4 terminal fork shoot on mature outer twigs
                    if (!juvenile && !cull && tw != 1)
                    {
                        double f4T = 0.62;
                        var f4Base = NewShrubsAlong(twPath, f4T);
                        var f4Tan = PathTangent(twPath, f4T);
                        double f4Az = twAz + (tw == 0 ? -0.78 : 0.74) + rng.Range(-0.15, 0.15);
                        var f4Dir = new Vec3(Math.Cos(f4Az), rng.Range(0.32, 0.58), Math.Sin(f4Az)).Normalized();
                        double f4Len = radius * rng.Range(0.22, 0.34) * heightScale;
                        double f4Leaf = height * 0.055 * heightScale;
                        NewShrubsBranchlet(part, f4Base, f4Tan, f4Dir, f4Len, f4Leaf, f4Leaf * 0.21, 5, 3,
                            leafBase, leafTip, twigCol, rng, f4Az);
                    }
                }

                // Mid-shoot foliage cluster on upper secondary branch
                int secLeaves = juvenile ? 5 : 8;
                for (int j = 0; j < secLeaves; j++)
                {
                    double lt = 0.36 + 0.58 * ((j + 0.5) / secLeaves);
                    var at = NewShrubsAlong(secPath, lt);
                    double lAz = secAz + j * 2.399963 + rng.Range(-0.2, 0.2);
                    var lDir = new Vec3(Math.Cos(lAz) * 0.85, rng.Range(0.26, 0.50), Math.Sin(lAz) * 0.85).Normalized();
                    double len = height * rng.Range(0.064, 0.088) * heightScale;
                    LeafUpward(part, at, at + lDir * len + Vec3.Up * (len * 0.09), Vec3.Up.Cross(lDir),
                        len * 0.22, leafBase, leafTip, camber: len * 0.08, longitudinal: 4);
                }
            }
        }

        // Luminous candle catkins emerging cleanly from upper forked twig tips
        int catkinCount = juvenile ? 4 : 18 + rng.NextInt(5);
        for (int k = 0; k < catkinCount && catkinAxils.Count > 0; k++)
        {
            var (spot, outDir) = catkinAxils[(k * 3 + 1) % catkinAxils.Count];
            MiremyrtleCandleCatkin(spot, outDir);
        }

        NewShrubsFinish(m, part, radius, height);
    }

    // =================================================================================================
    // 2. ShrubNutglade: Coppice Hazel (Corylus avellana) with Frilled Husks & Golden Lamb's-Tail Catkins
    // =================================================================================================
    private static void ShrubNutglade(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.60 : 1.0;

        // Rich botanical palette: sturdy grey-brown hazel bark, sunlit chartreuse-emerald foliage
        var barkCol = Primitives.Mix(new[] { 0.38, 0.32, 0.24 }, c1, 0.20);
        var twigCol = Primitives.Mix(new[] { 0.46, 0.38, 0.28 }, c1, 0.22);
        var catCol = new[] { 0.94, 0.82, 0.26 };
        var leafC1 = Primitives.Mix(c1, new[] { 0.35, 0.72, 0.22 }, 0.70);
        var leafC2 = Primitives.Mix(c2, new[] { 0.52, 0.88, 0.30 }, 0.68);

        // Pendulous Golden Lamb's-Tail Catkins curving gracefully in pairs/trios under gravity
        void NutgladeTasselCatkin(Vec3 at, Vec3 sway)
        {
            int v0 = part.VertexCount;
            double hangLen = rng.Range(0.08, 0.12) * heightScale;
            var p0 = at;
            var p1 = at + sway * 0.25 - Vec3.Up * (hangLen * 0.25);
            var p2 = at + sway * 0.65 - Vec3.Up * (hangLen * 0.60);
            var p3 = at + sway * 1.00 - Vec3.Up * hangLen;

            var catPath = SampleBezier(p0, p1, p2, p3, 5);
            WoodTube(part, catPath, new[] { 0.0028, 0.0022, 0.0018, 0.0014, 0.0010 },
                NewShrubsSides(part, 5), twigCol);

            int segments = cull ? 4 : 7;
            for (int s = 0; s < segments; s++)
            {
                double t = (double)s / Math.Max(1, segments - 1);
                var cPos = NewShrubsAlong(catPath, 0.20 + 0.78 * t);
                double r = 0.0092 * (1.0 - 0.26 * t);
                Primitives.Ellipsoid(part, cPos, new Vec3(r, 0.0075, r), 2, 5,
                    (u, v) => (catCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.26f, 0.85f);
        }

        // Whimsical Leafy Frilled Involucre Nut Clusters nestled in branch forks
        void NutgladeCobnutCluster(Vec3 at, Vec3 outDir)
        {
            int v0 = part.VertexCount;
            var stalkEnd = at + outDir * 0.020 - Vec3.Up * 0.005;
            WoodTube(part, new[] { at, stalkEnd }, new[] { 0.0034, 0.0022 }, NewShrubsSides(part, 5), twigCol);

            var nutCol = Primitives.Mix(new[] { 0.38, 0.22, 0.12 }, c1, 0.15);
            var huskCol = Primitives.Mix(new[] { 0.38, 0.65, 0.25 }, c2, 0.25);
            var rosyTipCol = new[] { 0.82, 0.58, 0.32 };

            int nutCount = cull ? 2 : 3;
            for (int ni = 0; ni < nutCount; ni++)
            {
                double ang = ni * (Math.PI * 2.0 / nutCount) + rng.Range(-0.25, 0.25);
                var off = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * 0.013 + Vec3.Up * rng.Range(-0.004, 0.004);
                var cNut = stalkEnd + off;
                double r = rng.Range(0.0125, 0.0160) * heightScale;

                // Glossy mahogany cobnut
                Primitives.Ellipsoid(part, cNut, new Vec3(r, r * 1.12, r), 3, 6,
                    (u, v) => (nutCol, 1.0, u, v, 0.0, 0.0));

                // Scalloped leafy lantern husk with rosy-gold tips
                var cHusk = cNut + Vec3.Up * (r * 0.60);
                Primitives.Ellipsoid(part, cHusk, new Vec3(r * 1.05, r * 0.55, r * 1.05), 2, 6,
                    (u, v) => (huskCol, 1.0, u, v, 0.0, 0.0));

                if (!cull)
                {
                    for (int f = 0; f < 4; f++)
                    {
                        double faz = ang + f * (Math.PI * 2.0 / 4.0) + rng.Range(-0.15, 0.15);
                        var fDir = new Vec3(Math.Cos(faz), rng.Range(0.20, 0.55), Math.Sin(faz)).Normalized();
                        var f0 = cNut + fDir * (r * 0.75) + Vec3.Up * (r * 0.35);
                        var f1 = f0 + fDir * (r * 1.65);
                        WoodTube(part, new[] { f0, f1 }, new[] { 0.0016, 0.0005 }, NewShrubsSides(part, 4), rosyTipCol);
                    }
                }
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.25f, 0.85f);
        }

        // Low asymmetric rootstock with exposed creeping coppice stolons feeding distant sucker poles
        WoodEllipsoid(part, new Vec3(0, height * 0.016, 0),
            new Vec3(radius * 0.16, height * 0.028, radius * 0.16), 2, 8, barkCol);

        int poleCount = juvenile ? 4 : 6 + rng.NextInt(2);
        var boughTips = new List<Vec3>();
        var nutForks = new List<(Vec3 Pos, Vec3 Dir)>();
        double phaseAz = rng.Range(0, Math.PI * 2.0);

        for (int p = 0; p < poleCount; p++)
        {
            // Role 0: sinuous primary scaffold (0.76-0.94h), Role 1: mid crooked arch (0.54-0.74h), Role 2: wide low skirt sucker (0.38-0.54h)
            int role = p % 3;
            double hMult = role switch
            {
                0 => rng.Range(0.76, 0.94),
                1 => rng.Range(0.54, 0.74),
                _ => rng.Range(0.38, 0.54)
            };
            double rMult = role switch
            {
                0 => rng.Range(0.68, 0.96),
                1 => rng.Range(0.78, 1.06),
                _ => rng.Range(0.86, 1.16)
            };

            double az = phaseAz + p * (Math.PI * 2.0 / poleCount) + rng.Range(-0.34, 0.34);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double reach = radius * rMult * heightScale;
            double h = height * hMult * heightScale;

            // Sucker offsets: spread poles loosely across 0.10-0.40r, linked visibly by ground stolons
            double suckerDist = radius * (role == 2 ? rng.Range(0.24, 0.40) : rng.Range(0.10, 0.30)) * heightScale;
            var stoolSpot = radial * suckerDist + tangent * rng.Range(-0.08, 0.08) * radius;
            if (suckerDist > radius * 0.14)
            {
                var stMid = stoolSpot * 0.5 + Vec3.Up * (height * 0.014) + tangent * rng.Range(-0.04, 0.04) * radius;
                BuildTaperedWood(part, new[] { Vec3.Zero, stMid, stoolSpot + Vec3.Up * 0.008 },
                    0.022, 0.014, NewShrubsSides(part, 5), barkCol, collarAtStart: false);
            }

            double bowSign = (p % 2 == 0) ? 1.0 : -1.0;
            double bow1 = bowSign * rng.Range(0.20, 0.32) * radius;
            double bow2 = -bow1 * 0.92 + rng.Range(-0.10, 0.10) * radius;
            var p0 = stoolSpot;
            var p1 = stoolSpot + radial * (reach * (role == 2 ? 0.36 : 0.20)) + tangent * bow1 + Vec3.Up * (h * (role == 2 ? 0.42 : 0.36));
            var p2 = stoolSpot + radial * (reach * 0.72) + tangent * bow2 + Vec3.Up * (h * 0.88);
            var p3 = radial * reach + tangent * (bow1 * 0.5) + Vec3.Up * h;

            int stations = NewShrubsStations(part, 8);
            var wandPath = SampleBezier(p0, p1, p2, p3, stations);
            double baseWandR = (role == 0 ? 0.026 : role == 1 ? 0.020 : 0.015) * heightScale;
            double tipWandR = baseWandR * 0.24;
            BuildTaperedWood(part, wandPath, baseWandR, tipWandR, NewShrubsSides(part, 6), barkCol);
            var wandTip = wandPath[^1];
            boughTips.Add(wandTip);

            // True Y-forking & wide horizontal canopy shelves on upper three-quarters of each pole
            int boughs = (role == 2) ? (juvenile ? 2 : 3) : (juvenile ? 2 : 4 + rng.NextInt(2));
            for (int b = 0; b < boughs; b++)
            {
                double tBough = 0.24 + 0.68 * ((b + 0.5) / boughs) + rng.Range(-0.04, 0.04);
                var boughBase = NewShrubsAlong(wandPath, Math.Clamp(tBough, 0.0, 1.0));
                var wandTangent = PathTangent(wandPath, tBough);

                double sideSign = (b % 2 == 0) ? 1.0 : -1.0;
                double bAz = az + sideSign * rng.Range(0.65, 1.05) + rng.Range(-0.18, 0.18);
                var bDir = new Vec3(Math.Cos(bAz), rng.Range(-0.06, 0.24), Math.Sin(bAz)).Normalized();
                double bLen = radius * rng.Range(0.38, 0.62) * heightScale * (role == 0 ? 1.05 : 0.85);

                nutForks.Add((boughBase, bDir));
                var bp0 = boughBase - wandTangent * 0.006;
                var bp1 = boughBase + (wandTangent * 0.38 + bDir * 0.62).Normalized() * (bLen * 0.34);
                var bp2 = boughBase + bDir * (bLen * 0.74) + Vec3.Up * (bLen * 0.04);
                var bp3 = boughBase + bDir * bLen - Vec3.Up * (bLen * 0.04);
                var bPath = SampleBezier(bp0, bp1, bp2, bp3, NewShrubsStations(part, 6));
                double bR0 = MathD.Lerp(baseWandR, tipWandR, tBough) * 0.68;
                BuildTaperedWood(part, bPath, bR0, 0.0020, NewShrubsSides(part, 5), twigCol);
                boughTips.Add(bPath[^1]);

                // Forked horizontal sub-twigs fanning out on either side of the shelf bough
                int twiglets = cull ? 2 : 3;
                for (int tw = 0; tw < twiglets; tw++)
                {
                    double twT = 0.34 + 0.54 * (tw / (double)Math.Max(1, twiglets - 1));
                    var twBase = NewShrubsAlong(bPath, twT);
                    double twSign = (tw % 2 == 0) ? 1.0 : -1.0;
                    double twAz = bAz + twSign * rng.Range(0.68, 0.95) + rng.Range(-0.14, 0.14);
                    var twDir = new Vec3(Math.Cos(twAz), rng.Range(-0.04, 0.16), Math.Sin(twAz)).Normalized();
                    double twLen = bLen * rng.Range(0.38, 0.52);

                    var twPath = SampleBezier(twBase, twBase + twDir * (twLen * 0.45),
                        twBase + twDir * (twLen * 0.82) + Vec3.Up * (twLen * 0.04), twBase + twDir * twLen, 4);
                    BuildTaperedWood(part, twPath, 0.0046, 0.0016, NewShrubsSides(part, 4), twigCol);
                    boughTips.Add(twPath[^1]);

                    int twLeaves = juvenile ? 3 : (cull ? 3 : 5);
                    for (int lj = 0; lj < twLeaves; lj++)
                    {
                        double lt = (lj + 0.4) / twLeaves;
                        var lat = NewShrubsAlong(twPath, Math.Clamp(lt, 0.0, 1.0));
                        double lSign = (lj % 2 == 0) ? 1.0 : -1.0;
                        double lAz = twAz + lSign * 0.72 + rng.Range(-0.14, 0.14);
                        var lDir = new Vec3(Math.Cos(lAz) * 0.92, rng.Range(-0.18, 0.36), Math.Sin(lAz) * 0.92).Normalized();
                        double len = rng.Range(0.095, 0.140) * heightScale;
                        var tip = lat + lDir * len + Vec3.Up * (len * rng.Range(-0.06, 0.10));
                        LeafUpward(part, lat, tip, Vec3.Up.Cross(lDir), len * 0.46, leafC1, leafC2,
                            camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
                    }
                }

                // Broad distichous leaves along the arched bough
                int leavesOnBough = juvenile ? 5 : (cull ? 5 : 8);
                for (int j = 0; j < leavesOnBough; j++)
                {
                    double t = 0.20 + 0.78 * ((j + 0.5) / leavesOnBough);
                    var at = NewShrubsAlong(bPath, Math.Clamp(t, 0.0, 1.0));
                    double side = (j % 2 == 0) ? 1.0 : -1.0;
                    double leafAz = bAz + side * 0.76 + rng.Range(-0.16, 0.16);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.90, rng.Range(-0.20, 0.42), Math.Sin(leafAz) * 0.90).Normalized();
                    double len = rng.Range(0.105, 0.148) * heightScale;
                    LeafUpward(part, at, at + dir * len + Vec3.Up * (len * 0.08), Vec3.Up.Cross(dir),
                        len * 0.46, leafC1, leafC2, camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.11, 0.11));
                }
            }

            // Crown terminal forked sprays at tip of main pole
            int wandLeaves = juvenile ? 4 : 6;
            for (int j = 0; j < wandLeaves; j++)
            {
                double t = 0.72 + 0.27 * ((j + 0.5) / wandLeaves);
                var at = NewShrubsAlong(wandPath, Math.Clamp(t, 0.0, 1.0));
                double leafAz = az + ((j % 2 == 0) ? 0.70 : -0.70) + rng.Range(-0.15, 0.15);
                var dir = new Vec3(Math.Cos(leafAz) * 0.88, rng.Range(-0.20, 0.42), Math.Sin(leafAz) * 0.88).Normalized();
                double len = rng.Range(0.105, 0.15) * heightScale;
                LeafUpward(part, at, at + dir * len + Vec3.Up * (len * 0.08), Vec3.Up.Cross(dir),
                    len * 0.46, leafC1, leafC2, camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
            }
        }

        // Pendulous Lamb's-Tail catkins swaying in graceful pairs & trios
        int catkinClusters = juvenile ? 3 : 14 + rng.NextInt(4);
        for (int k = 0; k < catkinClusters && boughTips.Count > 0; k++)
        {
            var tip = boughTips[(k * 2 + 1) % boughTips.Count];
            var sway1 = new Vec3(rng.Range(-0.012, 0.012), 0, rng.Range(-0.012, 0.012));
            NutgladeTasselCatkin(tip + new Vec3(0.010, 0, 0), sway1);

            if (!juvenile)
            {
                var sway2 = new Vec3(rng.Range(-0.012, 0.012), 0, rng.Range(-0.012, 0.012));
                NutgladeTasselCatkin(tip - new Vec3(0.010, 0, 0), sway2);
                if (k % 3 == 0)
                {
                    var sway3 = new Vec3(rng.Range(-0.012, 0.012), 0, rng.Range(-0.012, 0.012));
                    NutgladeTasselCatkin(tip + new Vec3(0, 0, 0.012), sway3);
                }
            }
        }

        // Frilled Involucre Nut Clusters nestling organically in branch forks
        int nutClusters = juvenile ? 2 : 13 + rng.NextInt(4);
        for (int n = 0; n < nutClusters && nutForks.Count > 0; n++)
        {
            var (spot, outDir) = nutForks[n % nutForks.Count];
            NutgladeCobnutCluster(spot, outDir);
        }

        NewShrubsFinish(m, part, radius, height);
    }

    // =================================================================================================
    // 3. ShrubThornmantle: Fairytale Hawthorn (Crataegus) Bramble-Treelet
    // =================================================================================================
    private static void ShrubThornmantle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.58 : 1.0;

        // Dark pewter / umbral bark, deep glossy emerald foliage with bronze young flush
        var barkCol = Primitives.Mix(new[] { 0.26, 0.22, 0.20 }, c1, 0.18);
        var thornMahogany = new[] { 0.32, 0.18, 0.12 };
        var thornIvory = new[] { 0.88, 0.82, 0.70 };
        var leafBase = Primitives.Mix(c1, new[] { 0.26, 0.56, 0.22 }, 0.72);
        var leafTip = Primitives.Mix(leafBase, new[] { 0.45, 0.68, 0.28 }, 0.50);
        var hawCol = new[] { 0.86, 0.08, 0.12 };
        var blossomPetal = new[] { 0.98, 0.96, 0.94 };
        var blossomRose = new[] { 0.94, 0.76, 0.82 };
        var antherCol = new[] { 0.95, 0.80, 0.35 };

        var spurNodes = new List<(Vec3 Pos, Vec3 Dir)>();
        var terminalSpurs = new HashSet<int>();

        // Sharp, slightly curved ivory-tipped mahogany thorn
        void ThornmantleThorn(Vec3 at, Vec3 outDir, double len)
        {
            var mid = at + outDir * (len * 0.55) + Vec3.Up * (len * 0.08);
            var tip = at + outDir * len - Vec3.Up * (len * 0.06);
            WoodTube(part, new[] { at, mid, tip }, new[] { 0.0034, 0.0018, 0.0006 },
                NewShrubsSides(part, 5), thornMahogany);
            if (!cull)
            {
                var ivoryBase = mid + (tip - mid) * 0.5;
                WoodTube(part, new[] { ivoryBase, tip }, new[] { 0.0012, 0.0004 },
                    NewShrubsSides(part, 4), thornIvory);
            }
        }

        // Short branching shoot-thorns sit in a few upper leader axils. Dark bases
        // connect to the stem; small ivory terminal portions keep the signature
        // legible without adding a new foliage layer.
        void ThornmantleBranchingThorn(Vec3 at, Vec3 outDir, double len, double hand)
        {
            var axis = outDir.Normalized();
            var side = axis.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            side *= hand;
            var fork = at + axis * (len * 0.62);
            var stemMid = at + axis * (len * 0.34);
            var tipA = fork + axis * (len * 0.37) + side * (len * 0.12);
            var tipB = fork + axis * (len * 0.24) - side * (len * 0.25);
            var branchMidA = fork + axis * (len * 0.20) + side * (len * 0.06);
            var branchMidB = fork + axis * (len * 0.13) - side * (len * 0.12);
            WoodTube(part, new[] { at, stemMid, fork }, new[] { 0.0052, 0.0036, 0.0024 },
                NewShrubsSides(part, 5), thornMahogany);
            WoodTube(part, new[] { fork, branchMidA, tipA }, new[] { 0.0024, 0.0015, 0.00055 },
                NewShrubsSides(part, 4), thornMahogany);
            WoodTube(part, new[] { fork, branchMidB, tipB }, new[] { 0.0020, 0.0012, 0.0005 },
                NewShrubsSides(part, 4), thornMahogany);
            if (!cull)
            {
                var paleA = fork + (tipA - fork) * 0.72;
                var paleB = fork + (tipB - fork) * 0.70;
                WoodTube(part, new[] { paleA, tipA }, new[] { 0.0010, 0.00025 },
                    NewShrubsSides(part, 4), thornIvory);
                WoodTube(part, new[] { paleB, tipB }, new[] { 0.0009, 0.00022 },
                    NewShrubsSides(part, 4), thornIvory);
            }
        }

        // Dense spur rosette of glossy lobed leaves + thorns distributed along gnarled boughs
        void ThornmantleSpur(Vec3 spurBase, Vec3 spurDir, int clusterIdx)
        {
            BranchCollar(part, spurBase, 0.008, barkCol);
            double spurLen = rng.Range(0.018, 0.030) * heightScale;
            var spurEnd = spurBase + spurDir * spurLen;
            WoodTube(part, new[] { spurBase, spurEnd }, new[] { 0.0046, 0.0024 },
                NewShrubsSides(part, 5), barkCol);
            spurNodes.Add((spurEnd, spurDir));

            // Sharp thorn arming the spur node
            var tDir = (spurDir * 0.6 + Vec3.Up * 0.4 + new Vec3(rng.Range(-0.2, 0.2), 0, rng.Range(-0.2, 0.2))).Normalized();
            ThornmantleThorn(spurEnd, tDir, rng.Range(0.026, 0.036) * heightScale);

            // Rosette spray of 5-6 lobed hawthorn leaves with guaranteed upward normals
            int leafCount = juvenile ? 4 : 6;
            for (int j = 0; j < leafCount; j++)
            {
                double leafAz = clusterIdx * 2.399963 + j * (Math.PI * 2.0 / leafCount) + rng.Range(-0.20, 0.20);
                var dir = new Vec3(Math.Cos(leafAz), rng.Range(0.25, 0.48), Math.Sin(leafAz)).Normalized();
                double len = rng.Range(0.082, 0.122) * heightScale;
                var tip = spurEnd + dir * len + Vec3.Up * (len * 0.10);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, spurEnd, tip, sideHint, len * 0.34, leafBase, leafTip,
                    camber: len * 0.08, longitudinal: 3, asymmetry: rng.Range(-0.05, 0.05));
            }
        }

        // Cupped 5-petaled ivory & blush May-blossoms
        void ThornmantleBlossom(Vec3 centre, ulong fseed)
        {
            int v0 = part.VertexCount;
            double azBase = Rng.HashUnit(fseed, 0x48, 0) * Math.PI * 2.0;
            double bLen = height * 0.024;

            for (int p = 0; p < 5; p++)
            {
                double az = azBase + p * (Math.PI * 2.0 / 5.0);
                var pDir = new Vec3(Math.Cos(az), 0.35, Math.Sin(az)).Normalized();
                var pTip = centre + pDir * bLen + Vec3.Up * (bLen * 0.22);
                var sideHint = Vec3.Up.Cross(pDir);
                LeafUpward(part, centre, pTip, sideHint, bLen * 0.45, blossomPetal, blossomRose,
                    camber: bLen * 0.15, longitudinal: 3);
            }
            // Pink-gold anther crown
            Primitives.Ellipsoid(part, centre + Vec3.Up * 0.003, new Vec3(bLen * 0.32, bLen * 0.22, bLen * 0.32),
                2, 5, (u, v) => (antherCol, 1.0, u, v, 0.0, 0.0));
            MarkOrgan(part, v0, part.VertexCount - v0, 1.32f, 0.85f);
        }

        // Glossy ruby-garnet haw corymb
        void ThornmantleHawCluster(Vec3 anchor)
        {
            int v0 = part.VertexCount;
            int count = cull ? 3 : 5 + rng.NextInt(3);
            for (int i = 0; i < count; i++)
            {
                var off = new Vec3(rng.Range(-0.016, 0.016), -rng.Range(0.008, 0.020), rng.Range(-0.016, 0.016));
                var pos = anchor + off;
                double r = rng.Range(0.011, 0.015);
                Primitives.Ellipsoid(part, pos, new Vec3(r, r * 1.15, r), 3, 6,
                    (u, v) => (hawCol, 1.0, u, v, 0.0, 0.0));
                // Persistent dark calyx crown
                var calyxCol = new[] { 0.22, 0.14, 0.10 };
                Primitives.Ellipsoid(part, pos + Vec3.Up * (r * 0.90), new Vec3(r * 0.30, r * 0.15, r * 0.30), 2, 4,
                    (u, v) => (calyxCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.22f, 0.85f);
        }

        // Gnarled asymmetric root crown
        WoodEllipsoid(part, new Vec3(0, height * 0.020, 0),
            new Vec3(radius * 0.16, height * 0.028, radius * 0.16), 2, 7, barkCol);

        // 3 (adult) or 2 (juvenile) chunky crooked primary trunks leaning asymmetrically before dividing wide
        int trunks = juvenile ? 2 : 3;
        double startAz = rng.Range(0, Math.PI * 2.0);
        var branchTipHazards = new List<(Vec3 Pos, Vec3 Dir)>();

        for (int s = 0; s < trunks; s++)
        {
            double az = startAz + s * (Math.PI * 2.0 / trunks) + rng.Range(-0.42, 0.42);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            var rootPos = radial * (radius * rng.Range(0.06, 0.14) * heightScale);
            double hand = (s % 2 == 0) ? 1.0 : -1.0;

            double trunkLen = height * rng.Range(0.50, 0.66) * heightScale;
            var initDir = (radial * rng.Range(0.45, 0.72) + tangent * (hand * 0.22) + Vec3.Up * rng.Range(0.75, 0.95)).Normalized();
            var (_, trunkNodes, trunkOuts) = BuildZigzagWood(
                part, rootPos, initDir, trunkLen, juvenile ? 3 : 4,
                0.036 * heightScale, 0.015 * heightScale,
                yawKinkRad: 0.56, pitchSpread: 0.14, archBow: 0.10,
                sides: NewShrubsSides(part, 6), barkCol: barkCol, rng: rng, handSign: hand);

            // Divaricate, crisscrossing secondary zigzag boughs branching at wide angles from each trunk bend
            for (int n = 0; n < trunkNodes.Length; n++)
            {
                var nodePos = trunkNodes[n];
                double nodeFrac = (n + 1.0) / trunkNodes.Length;
                int boughCount = (!juvenile && n >= 1) ? 2 : 1;

                // Crisp mahogany-ivory spine arming outer knee bend of trunk
                ThornmantleThorn(nodePos, (trunkOuts[n] + Vec3.Up * 0.28).Normalized(), rng.Range(0.036, 0.050) * heightScale);

                if (!juvenile && !cull && n == trunkNodes.Length - 2)
                {
                    ThornmantleBranchingThorn(nodePos, (trunkOuts[n] * 0.7 + Vec3.Up * 0.6).Normalized(),
                        radius * 0.125, hand);
                }

                for (int b = 0; b < boughCount; b++)
                {
                    double sideSign = (b == 0 ? 1.0 : -1.0) * hand;
                    // Divaricate horizontal/interlocking azimuth: swings tangentially across adjacent trunk zones!
                    double bAz = az + sideSign * rng.Range(0.82, 1.52) + (n == trunkNodes.Length - 1 ? 0.35 * -hand : 0.0);
                    var bHoriz = new Vec3(Math.Cos(bAz), 0, Math.Sin(bAz));
                    double pitch = rng.Range(0.02, 0.38) + (n == trunkNodes.Length - 1 ? 0.22 : -0.06);
                    var bDir = (bHoriz + Vec3.Up * pitch).Normalized();

                    double bLen = radius * rng.Range(0.46, 0.76) * heightScale * (1.0 - 0.18 * nodeFrac);
                    double bR0 = MathD.Lerp(0.022, 0.011, nodeFrac) * heightScale;
                    int bSegs = cull ? 2 : (juvenile ? 2 : 3);

                    var (_, bNodes, bOuts) = BuildZigzagWood(
                        part, nodePos, bDir, bLen, bSegs,
                        bR0, 0.0042 * heightScale,
                        yawKinkRad: 0.72, pitchSpread: 0.18, archBow: 0.12,
                        sides: NewShrubsSides(part, 5), barkCol: barkCol, rng: rng, handSign: sideSign);

                    // Tertiary cross-twigs & jagged thorny spur shoots at each bough bend
                    for (int bn = 0; bn < bNodes.Length; bn++)
                    {
                        var bJoint = bNodes[bn];
                        bool isEnd = bn == bNodes.Length - 1;
                        ThornmantleThorn(bJoint, (bOuts[bn] + Vec3.Up * 0.32).Normalized(), rng.Range(0.030, 0.044) * heightScale);

                        // Short jagged cross-twig shooting sideways from bend
                        if (!cull && bn < bNodes.Length - 1)
                        {
                            double tAz = bAz + (bn % 2 == 0 ? -1.05 : 1.08) + rng.Range(-0.22, 0.22);
                            var tDir = new Vec3(Math.Cos(tAz), rng.Range(-0.08, 0.36), Math.Sin(tAz)).Normalized();
                            double tLen = radius * rng.Range(0.24, 0.40) * heightScale;
                            var (_, tNodes, tOuts) = BuildZigzagWood(
                                part, bJoint, tDir, tLen, 2,
                                0.0068 * heightScale, 0.0022 * heightScale,
                                yawKinkRad: 0.65, pitchSpread: 0.15, archBow: 0.08,
                                sides: NewShrubsSides(part, 4), barkCol: barkCol, rng: rng, handSign: -sideSign);

                            for (int tn = 0; tn < tNodes.Length; tn++)
                            {
                                var spDir = (tOuts[tn] * 0.6 + Vec3.Up * 0.5).Normalized();
                                if (tn == tNodes.Length - 1) terminalSpurs.Add(spurNodes.Count);
                                ThornmantleSpur(tNodes[tn], spDir, s * 100 + n * 20 + bn * 7 + tn);
                                branchTipHazards.Add((tNodes[tn], spDir));
                            }
                        }

                        var spurDir = (bOuts[bn] * 0.55 + Vec3.Up * (isEnd ? 0.55 : 0.35)).Normalized();
                        if (isEnd) terminalSpurs.Add(spurNodes.Count);
                        ThornmantleSpur(bJoint, spurDir, s * 200 + n * 31 + b * 11 + bn);
                        branchTipHazards.Add((bJoint, spurDir));
                    }
                }
            }
        }

        // Each upper/terminal spur finishes in a blossom tuft; spread remaining May-blossoms across thorny boughs
        int blossomCount = juvenile ? 6 : 28 + rng.NextInt(8);
        var lateralSpurs = new List<Vec3>();
        int blossomsPlaced = 0;
        for (int i = 0; i < spurNodes.Count; i++)
        {
            var (at, _) = spurNodes[i];
            if (terminalSpurs.Contains(i) && blossomsPlaced < blossomCount) AddSpurBlossom(at);
            else lateralSpurs.Add(at);
        }
        int lateralBlooms = Math.Min(blossomCount - blossomsPlaced, lateralSpurs.Count);
        for (int b = 0; b < lateralBlooms; b++)
            AddSpurBlossom(lateralSpurs[(int)((b + 0.5) * lateralSpurs.Count / lateralBlooms)]);

        void AddSpurBlossom(Vec3 at)
        {
            var bloom = at + Vec3.Up * (height * 0.012);
            Primitives.Tube(part, new[] { at, bloom }, new[] { 0.0016, 0.0009 }, 4,
                (i, v) => (leafBase, 1.0, i, v, 0, 0));
            ThornmantleBlossom(bloom, Rng.Mix(seed, (ulong)(blossomsPlaced * 71 + 13)));
            blossomsPlaced++;
        }

        // Ruby-garnet haws hanging below thorny bough knots
        int hawClusters = juvenile ? 4 : 15 + rng.NextInt(4);
        for (int hi = 0; hi < hawClusters && spurNodes.Count > 0; hi++)
        {
            var (anchor, _) = spurNodes[(hi * 3 + 1) % spurNodes.Count];
            ThornmantleHawCluster(anchor);
        }

        NewShrubsFinish(m, part, radius, height);
    }

    // =================================================================================================
    // 4. ShrubGreywatch: Windswept Krummholz Juniper with Frosted Sapphire Berry-Cones
    // =================================================================================================
    private static void ShrubGreywatch(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.60 : 1.0;

        // Glaucous blue-spruce / seafoam scale-foliage, cinnamon-red and silver-grey krummholz bark
        var barkCol = Primitives.Mix(new[] { 0.38, 0.28, 0.22 }, c1, 0.16);
        var needleBase = Primitives.Mix(c1, new[] { 0.32, 0.65, 0.72 }, 0.75);
        var needleTip = Primitives.Mix(c2, new[] { 0.48, 0.78, 0.82 }, 0.72);
        var berrySapphire = new[] { 0.24, 0.42, 0.76 };
        var bloomIce = new[] { 0.80, 0.88, 0.98 };
        var pollenGold = new[] { 0.90, 0.78, 0.30 };

        // Directional wind sculpting (alpine krummholz habit)
        double windAz = Rng.HashUnit(seed, 0x57, 1) * Math.PI * 2.0;
        var windHoriz = new Vec3(Math.Cos(windAz), 0, Math.Sin(windAz));
        var windCross = new Vec3(-windHoriz.Z, 0, windHoriz.X);

        // Exposed gnarled procumbent krummholz trunk rising crookedly leeward along wind vector
        var rootAnchor = -windHoriz * (radius * 0.16 * heightScale);
        var trunkMid = rootAnchor * 0.25 + windHoriz * (radius * 0.08 * heightScale) + Vec3.Up * (height * 0.16 * heightScale);
        var crownBase = windHoriz * (radius * 0.20 * heightScale) + windCross * rng.Range(-0.06, 0.06) * radius + Vec3.Up * (height * 0.30 * heightScale);
        var trunkPath = SampleBezier(rootAnchor, rootAnchor + Vec3.Up * (height * 0.10), trunkMid, crownBase, NewShrubsStations(part, 7));
        BuildTaperedWood(part, trunkPath, radius * 0.12 * heightScale, radius * 0.056 * heightScale,
            NewShrubsSides(part, 7), barkCol, collarAtStart: false);

        // Volumetric tiered cloud-pad builder (dense wind-planed bonsai dome cushions on crooked twigs)
        var cloudBerrySpots = new List<Vec3>();
        void VolumetricCloudPad(Vec3 start, Vec3 end, Vec3 boughTangent, double padScale = 1.0, bool terminal = false)
        {
            var axis = end - start;
            double len = axis.Length;
            if (len < 1e-6) return;
            var fwd = axis / len;
            var side = Vec3.Up.Cross(fwd);
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(fwd);
            side = side.Normalized();

            var cp0 = start;
            var cp1 = start + (boughTangent * 0.42 + fwd * 0.58).Normalized() * (len * 0.35);
            var cp2 = start + fwd * (len * 0.72) + Vec3.Up * (len * 0.10);
            var cp3 = end + windHoriz * (len * 0.06);
            var mainPath = SampleBezier(cp0, cp1, cp2, cp3, 5);
            BuildTaperedWood(part, mainPath, 0.0072 * padScale, 0.0018, NewShrubsSides(part, 5), barkCol);

            int subTwigs = cull ? 3 : 5;
            for (int st = 0; st < subTwigs; st++)
            {
                double q = 0.20 + 0.72 * ((st + 0.5) / subTwigs);
                var subBase = NewShrubsAlong(mainPath, q);
                double sign = (st % 2 == 0) ? 1.0 : -1.0;
                var subDir = (fwd * 0.42 + side * (sign * 0.74) + windHoriz * 0.22 + Vec3.Up * 0.14).Normalized();
                double subLen = len * rng.Range(0.46, 0.64);
                var subEnd = subBase + subDir * subLen;

                WoodTube(part, new[] { subBase, subEnd }, new[] { 0.0034, 0.0014 }, NewShrubsSides(part, 4), barkCol);
                cloudBerrySpots.Add(subEnd);

                int subScales = cull ? 3 : 5;
                for (int sc = 0; sc < subScales; sc++)
                {
                    double sq = (sc + 0.35) / subScales;
                    var at = Vec3.Lerp(subBase, subEnd, sq);
                    double size = rng.Range(0.078, 0.115) * heightScale * padScale;
                    var lTip = at + side * (sign * size * 0.58) + fwd * (size * 0.24) + Vec3.Up * (size * 0.28);
                    LeafUpward(part, at, lTip, Vec3.Up.Cross(lTip - at), size * 0.14, needleBase, needleTip,
                        camber: 0.004, longitudinal: 3);
                    var dTip = at + fwd * (size * (terminal ? 0.38 : 0.28)) + windHoriz * (size * 0.14) + Vec3.Up * (size * 0.38);
                    LeafUpward(part, at, dTip, Vec3.Up.Cross(dTip - at), size * 0.13, needleBase, needleTip,
                        camber: 0.004, longitudinal: 3);
                }
            }

            int stations = cull ? 5 : 8;
            for (int i = 0; i < stations; i++)
            {
                double q = (i + 0.5) / stations;
                var at = NewShrubsAlong(mainPath, q);
                double size = rng.Range(0.088, 0.125) * heightScale * padScale;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var tip = at + side * (sign * size * 0.62) + fwd * (size * 0.20) + Vec3.Up * (size * 0.28);
                    LeafUpward(part, at, tip, Vec3.Up.Cross(tip - at), size * 0.14, needleBase, needleTip,
                        camber: 0.004, longitudinal: 3);
                }
                var dTip = at + fwd * (size * 0.28) + windHoriz * (size * 0.14) + Vec3.Up * (size * 0.40);
                LeafUpward(part, at, dTip, Vec3.Up.Cross(dTip - at), size * 0.13, needleBase, needleTip,
                    camber: 0.004, longitudinal: 3);
            }
        }

        // Frosted Sapphire-Cobalt Galbulus Berry-Cone
        void GreywatchBerry(Vec3 at)
        {
            int v0 = part.VertexCount;
            double r = rng.Range(0.0085, 0.0125) * heightScale;
            Primitives.Ellipsoid(part, at, new Vec3(r, r, r), 3, 6,
                (u, v) => (berrySapphire, 1.0, u, v, 0.0, 0.0));
            Primitives.Ellipsoid(part, at + Vec3.Up * (r * 0.85), new Vec3(r * 0.48, r * 0.25, r * 0.48), 2, 5,
                (u, v) => (bloomIce, 1.0, u, v, 0.0, 0.0));
            MarkOrgan(part, v0, part.VertexCount - v0, 1.22f, 0.82f);
        }

        void GreywatchPollenCone(Vec3 at, Vec3 fwd)
        {
            if (cull) return;
            int v0 = part.VertexCount;
            var tip = at + fwd * 0.008;
            WoodTube(part, new[] { at, tip }, new[] { 0.0022, 0.0010 }, NewShrubsSides(part, 4), pollenGold);
            MarkOrgan(part, v0, part.VertexCount - v0, 1.25f, 0.85f);
        }

        // Multi-tiered wind-sculpted krummholz boughs climbing from low windward skirt up to ~1.1 m crest pads
        int limbs = juvenile ? 3 : 5;
        for (int l = 0; l < limbs; l++)
        {
            bool isWindward = l == 0;
            bool isMid = l == 1 || l == 2;
            bool isCrest = !isWindward && !isMid;
            double relAz = isWindward
                ? Math.PI + rng.Range(-0.45, 0.45)
                : isMid
                    ? (l == 1 ? -1.05 : 1.05) + rng.Range(-0.28, 0.28)
                    : (l == 3 ? -0.32 : 0.42) + rng.Range(-0.20, 0.20);
            double az = windAz + relAz;
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));

            double outReach = isWindward
                ? radius * rng.Range(0.45, 0.62) * heightScale
                : isMid
                    ? radius * rng.Range(0.68, 0.94) * heightScale
                    : radius * rng.Range(0.55, 0.82) * heightScale;
            double riseTop = isWindward
                ? height * rng.Range(0.08, 0.16) * heightScale
                : isMid
                    ? height * rng.Range(0.30, 0.46) * heightScale
                    : height * rng.Range(0.56, 0.78) * heightScale;

            var bStart = isWindward ? trunkMid : crownBase;
            var initDir = (radial * (outReach * 0.65) + windHoriz * (isWindward ? 0.02 : radius * 0.34) + Vec3.Up * (riseTop * 0.85)).Normalized();
            double bLen = Math.Sqrt(outReach * outReach + riseTop * riseTop);
            var (limbPath, limbNodes, limbOuts) = BuildZigzagWood(
                part, bStart, initDir, bLen, juvenile ? 2 : 3,
                radius * (isCrest ? 0.050 : 0.040) * heightScale, 0.0085 * heightScale,
                yawKinkRad: 0.46, pitchSpread: 0.14, archBow: isCrest ? 0.16 : 0.10,
                sides: NewShrubsSides(part, 6), barkCol: barkCol, rng: rng, handSign: l % 2 == 0 ? 1.0 : -1.0);

            // Stacked horizontal cloud-pads fanning cleanly out along crooked bonsai tiers
            for (int n = 0; n < limbNodes.Length; n++)
            {
                var attach = limbNodes[n];
                var boughTan = limbOuts[n];
                int forkPads = (n == limbNodes.Length - 1) ? 2 : (isWindward ? 0 : 1);
                for (int fp = 0; fp < forkPads; fp++)
                {
                    double padAz = az + (fp == 0 ? 0.48 : -0.52) * (n % 2 == 0 ? 1.0 : -1.0) + rng.Range(-0.18, 0.18);
                    var rawDir = (new Vec3(Math.Cos(padAz), rng.Range(0.06, 0.22), Math.Sin(padAz)) * 0.72 + windHoriz * 0.28).Normalized();
                    double padLen = radius * rng.Range(0.28, 0.44) * heightScale;
                    var padEnd = attach + rawDir * padLen;

                    VolumetricCloudPad(attach, padEnd, boughTan, padScale: isWindward ? 0.90 : 1.15,
                        terminal: n == limbNodes.Length - 1);
                    cloudBerrySpots.Add(padEnd);
                    GreywatchPollenCone(padEnd, rawDir);

                    if (!cull && n == limbNodes.Length - 1)
                    {
                        var crestDir = (windHoriz * 0.64 + rawDir * 0.34 + Vec3.Up * 0.18).Normalized();
                        var crestEnd = padEnd + crestDir * (radius * 0.22 * heightScale);
                        VolumetricCloudPad(padEnd, crestEnd, rawDir, padScale: 0.96, terminal: true);
                        cloudBerrySpots.Add(crestEnd);
                    }
                }
            }
        }

        // Frosted sapphire berry-cones nestled inside sculpted bonsai cloud pads
        int berryCount = juvenile ? 8 : 30 + rng.NextInt(8);
        for (int b = 0; b < berryCount && cloudBerrySpots.Count > 0; b++)
        {
            var spot = cloudBerrySpots[b % cloudBerrySpots.Count] +
                new Vec3(rng.Range(-0.015, 0.015), rng.Range(-0.008, 0.014), rng.Range(-0.015, 0.015));
            GreywatchBerry(spot);
        }

        NewShrubsFinish(m, part, radius, height);
    }

    // =================================================================================================
    // 5. ShrubSilverfelt: Sub-Alpine Cushion Willow (Salix lanata) with Pearl-Silk Catkins
    // =================================================================================================
    private static void ShrubSilverfelt(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.60 : 1.0;

        // Dark chocolate & olive woody stems, felted silvery-celadon leaves with silky white margins
        var barkCol = Primitives.Mix(new[] { 0.30, 0.25, 0.18 }, c1, 0.18);
        var leafBase = new[] { 0.86, 0.93, 0.88 }; // Luminous pearl-celadon
        var leafTip = new[] { 0.94, 0.98, 0.96 };  // Soft ivory-felted margin
        var catSilk = new[] { 0.95, 0.96, 0.96 };
        var antherGold = new[] { 0.98, 0.85, 0.28 };

        // Delicate Upright Pearl-Silk Pussy-Willow Catkin on slender curved pedicel with Golden Stamen Halo
        void PussyWillowCatkin(Vec3 at, Vec3 outDir)
        {
            int v0 = part.VertexCount;
            double catH = (juvenile ? 0.034 : 0.046) * rng.Range(0.92, 1.10) * heightScale;
            double catR = catH * 0.40;

            // Slender curved woody pedicel emerging from node
            var ped0 = at;
            var ped1 = at + outDir * 0.008 + Vec3.Up * (catH * 0.25);
            var ped2 = at + outDir * 0.006 + Vec3.Up * (catH * 0.65);
            WoodTube(part, new[] { ped0, ped1, ped2 }, new[] { 0.0030, 0.0022, 0.0016 },
                NewShrubsSides(part, 5), barkCol);

            var centre = ped2 + Vec3.Up * (catH * 0.40);

            // Plump pearl-silk ovoid body
            Primitives.Ellipsoid(part, centre, new Vec3(catR, catH * 0.70, catR), 3, 6,
                (u, v) => (catSilk, 1.0, u, v, 0.0, 0.0));

            // Fine halo of warm golden-amber stamen filaments & anthers
            int stamens = cull ? 6 : 14;
            for (int s = 0; s < stamens; s++)
            {
                double az = s * (Math.PI * 2.0 / stamens) + rng.Range(-0.15, 0.15);
                var sDir = new Vec3(Math.Cos(az), rng.Range(0.20, 0.65), Math.Sin(az)).Normalized();
                var s0 = centre + sDir * (catR * 0.75) + Vec3.Up * rng.Range(-catH * 0.25, catH * 0.25);
                var s1 = s0 + sDir * (catR * 0.60);
                WoodTube(part, new[] { s0, s1 }, new[] { 0.0009, 0.0004 }, NewShrubsSides(part, 4), antherGold);
                // Tiny anther head
                Primitives.Ellipsoid(part, s1, new Vec3(0.0016, 0.0016, 0.0016), 2, 4,
                    (u, v) => (antherGold, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.25f, 0.88f);
        }

        // Low central gnarled woody root-plate flush with ground
        WoodEllipsoid(part, new Vec3(0, height * 0.014, 0),
            new Vec3(radius * 0.15, height * 0.022, radius * 0.15), 2, 7, barkCol);

        // Asymmetric multi-hummock prostrate cushion mat:
        // 5 gnarled ground-hugging creeping runners crawl unevenly across terrain, forking into overlapping silver pillow lobes
        int runnerCount = juvenile ? 3 : 5;
        double startAz = rng.Range(0, Math.PI * 2.0);
        var catkinNodes = new List<(Vec3 Pos, Vec3 Dir)>();

        void AddSilverPillowCluster(Vec3 basePt, Vec3 dirOut, double hFrac, int twigCount, double padScale)
        {
            double domeH = height * hFrac * heightScale;
            for (int tw = 0; tw < twigCount; tw++)
            {
                double tAz = Math.Atan2(dirOut.Z, dirOut.X) + (tw - (twigCount - 1) * 0.5) * rng.Range(0.75, 1.05) + rng.Range(-0.22, 0.22);
                var twH = new Vec3(Math.Cos(tAz), 0, Math.Sin(tAz));
                double twOut = radius * rng.Range(0.16, 0.30) * padScale * heightScale;
                double twRise = domeH * rng.Range(0.70, 1.00);
                var p0 = basePt;
                var p1 = basePt + twH * (twOut * 0.45) + Vec3.Up * (twRise * 0.32);
                var p2 = basePt + twH * (twOut * 0.85) + Vec3.Up * (twRise * 0.86);
                var p3 = basePt + twH * twOut + Vec3.Up * twRise;
                var twPath = SampleBezier(p0, p1, p2, p3, 5);
                BuildTaperedWood(part, twPath, 0.0082 * padScale, 0.0020, NewShrubsSides(part, 5), barkCol);

                // Dense tufted cushions of cupped pearl-silver felted leaves wrapping each short crooked shoot into a low pillow mound
                int ringsOnTwig = juvenile ? 3 : (cull ? 3 : 5);
                for (int r = 0; r < ringsOnTwig; r++)
                {
                    double rt = 0.28 + 0.72 * (r / (double)Math.Max(1, ringsOnTwig - 1));
                    var pt = NewShrubsAlong(twPath, rt);
                    int perRing = (r == ringsOnTwig - 1) ? (juvenile ? 4 : 6) : (juvenile ? 3 : 4);
                    for (int k = 0; k < perRing; k++)
                    {
                        double lAz = tAz + r * 1.1 + k * (Math.PI * 2.0 / perRing) + rng.Range(-0.20, 0.20);
                        double pitch = r == ringsOnTwig - 1 ? rng.Range(0.28, 0.62) : rng.Range(0.08, 0.44);
                        var lDir = new Vec3(Math.Cos(lAz) * 0.88, pitch, Math.Sin(lAz) * 0.88).Normalized();
                        double len = rng.Range(0.052, 0.074) * padScale * heightScale;
                        var lTip = pt + lDir * len + Vec3.Up * (len * 0.05);
                        LeafUpward(part, pt, lTip, Vec3.Up.Cross(lDir), len * 0.38, leafBase, leafTip,
                            camber: len * 0.16, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
                    }
                }
                if (tw % 2 == 0)
                    catkinNodes.Add((twPath[^1], (twH * 0.35 + Vec3.Up * 0.65).Normalized()));

                // Low short prostrate skirt fork around cushion edge
                if (!cull && tw == 0)
                {
                    double skAz = tAz + (dirOut.X > 0 ? 0.85 : -0.85);
                    var skDir = new Vec3(Math.Cos(skAz), 0.16, Math.Sin(skAz)).Normalized();
                    double skLen = radius * rng.Range(0.18, 0.26) * padScale * heightScale;
                    var skMid = basePt + skDir * (skLen * 0.55) + Vec3.Up * 0.015;
                    var skEnd = basePt + skDir * skLen + Vec3.Up * 0.025;
                    BuildTaperedWood(part, new[] { basePt, skMid, skEnd }, 0.0052, 0.0018, NewShrubsSides(part, 4), barkCol);
                    EmitLeafRosette(part, skEnd, juvenile ? 3 : 5, 0.056 * padScale * heightScale, 0.021 * padScale * heightScale,
                        0.92, 0.12, 0.42, leafBase, leafTip, rng, skAz, longitudinal: 4, camberFrac: 0.15);
                }
            }
        }

        // Core central cushion dome built from gnarled short central forks
        AddSilverPillowCluster(new Vec3(0, height * 0.02, 0), new Vec3(Math.Cos(startAz), 0, Math.Sin(startAz)),
            hFrac: 0.44, twigCount: juvenile ? 3 : 5, padScale: 1.08);

        // Creeping surface runners crawling out irregularly along terrain into uneven perimeter cushion pillows
        for (int s = 0; s < runnerCount; s++)
        {
            double az = startAz + s * (Math.PI * 2.0 / runnerCount) + rng.Range(-0.42, 0.42);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double runnerLen = radius * rng.Range(0.68, 1.18) * heightScale;
            var initDir = (radial + new Vec3(-radial.Z, 0, radial.X) * rng.Range(-0.28, 0.28) + Vec3.Up * 0.06).Normalized();

            var (_, rNodes, rOuts) = BuildZigzagWood(
                part, radial * (radius * 0.06), initDir, runnerLen, juvenile ? 2 : 3,
                0.016 * heightScale, 0.0045 * heightScale,
                yawKinkRad: 0.48, pitchSpread: 0.04, archBow: 0.03,
                sides: NewShrubsSides(part, 5), barkCol: barkCol, rng: rng, handSign: s % 2 == 0 ? 1.0 : -1.0);

            for (int n = 0; n < rNodes.Length; n++)
            {
                // Clamp creeping woody stolon nodes snugly near the soil surface
                var groundAnchor = new Vec3(rNodes[n].X, Math.Min(rNodes[n].Y, height * (0.04 + 0.03 * n)), rNodes[n].Z);
                bool outer = n == rNodes.Length - 1;
                double lobeH = outer ? rng.Range(0.18, 0.28) : rng.Range(0.28, 0.40);
                double lobeScale = outer ? rng.Range(0.74, 0.90) : rng.Range(0.88, 1.04);
                int twCount = juvenile ? 2 : (outer ? 3 : 2);
                AddSilverPillowCluster(groundAnchor, rOuts[n], hFrac: lobeH, twigCount: twCount, padScale: lobeScale);
            }
        }

        // Delicate pearl-silk pussy-willow catkins perched across top of undulating cushion pillows
        int catkinCount = juvenile ? 3 : 15 + rng.NextInt(5);
        for (int k = 0; k < catkinCount && catkinNodes.Count > 0; k++)
        {
            var (spot, outDir) = catkinNodes[(k * 2) % catkinNodes.Count];
            PussyWillowCatkin(spot, outDir);
        }

        NewShrubsFinish(m, part, radius, height);
    }
}
