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
    private static void NewShrubsFinish(MeshData m, MeshData part, double radius, double height)
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

    // =================================================================================================
    // 1. ShrubMiremyrtle: Bog Myrtle / Sweet Gale (Myrica gale) with Luminous Amber Candle-Spikes
    // =================================================================================================
    private static void ShrubMiremyrtle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        bool cull = part.Lod?.CullMinorDetails == true;
        double heightScale = juvenile ? 0.58 : 1.0;

        // Botanical palette: warm coppery-plum / red-bronze bark, sunlit olive-emerald foliage with pale resin midrib
        var barkCol = Primitives.Mix(new[] { 0.36, 0.22, 0.18 }, c1, 0.18);
        var leafBase = Primitives.Mix(c1, new[] { 0.44, 0.60, 0.38 }, 0.75);
        var leafTip = Primitives.Mix(c2, new[] { 0.58, 0.76, 0.48 }, 0.75);
        var resinCol = new[] { 0.98, 0.78, 0.24 };

        // Whimsical signature: Luminous Golden-Amber Candle Catkins on curved axillary pedicels
        void MiremyrtleCandleCatkin(Vec3 at, Vec3 outDir)
        {
            int v0 = part.VertexCount;
            double catLen = (juvenile ? 0.038 : 0.054) * rng.Range(0.92, 1.10) * heightScale;

            // Slender curved pedicel emerging naturally from leaf axil / terminal shoot
            var ped0 = at;
            var ped1 = at + outDir * 0.008 + Vec3.Up * (catLen * 0.15);
            var ped2 = at + outDir * 0.012 + Vec3.Up * (catLen * 0.45);
            var ped3 = at + outDir * 0.008 + Vec3.Up * (catLen * 0.75);
            var pedPath = SampleBezier(ped0, ped1, ped2, ped3, 4);
            WoodTube(part, pedPath, new[] { 0.0034, 0.0026, 0.0020, 0.0014 }, NewShrubsSides(part, 5), barkCol);

            // Sculpted overlapping catkin scales with warm bronze-to-saffron gradient
            int tiers = cull ? 4 : 7;
            for (int t = 0; t < tiers; t++)
            {
                double frac = (double)t / Math.Max(1, tiers - 1);
                var cPos = BezierCubic(ped1, ped2, ped3, ped3 + Vec3.Up * (catLen * 0.35), frac);
                double r = 0.0125 * (1.0 - 0.42 * frac);
                if (r < 0.0032) r = 0.0032;

                double[] scaleCol = frac < 0.35
                    ? Primitives.Mix(new[] { 0.65, 0.40, 0.16 }, new[] { 0.90, 0.64, 0.18 }, frac / 0.35)
                    : Primitives.Mix(new[] { 0.90, 0.64, 0.18 }, new[] { 0.98, 0.88, 0.32 }, (frac - 0.35) / 0.65);

                Primitives.Ellipsoid(part, cPos, new Vec3(r, 0.0052, r), 2, 5, (u, v) => (scaleCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.28f, 0.85f);
        }

        // Glistening amber resin droplets in leaf axils
        void MiremyrtleResinDroplets(Vec3 at)
        {
            if (cull) return;
            int v0 = part.VertexCount;
            for (int d = 0; d < 2; d++)
            {
                var off = new Vec3(rng.Range(-0.004, 0.004), rng.Range(0.001, 0.005), rng.Range(-0.004, 0.004));
                Primitives.Ellipsoid(part, at + off, new Vec3(0.0032, 0.0038, 0.0032), 2, 4,
                    (u, v) => (resinCol, 1.0, u, v, 0.0, 0.0));
            }
            MarkOrgan(part, v0, part.VertexCount - v0, 1.28f, 0.85f);
        }

        // Central mossy peat hummock
        WoodEllipsoid(part, new Vec3(0, height * 0.022, 0),
            new Vec3(radius * 0.22, height * 0.036, radius * 0.22), 3, 8, barkCol);

        // Multi-stemmed tangled thicket: 3-4 distinct rhizomatous clump centers across the peat
        int clumpCount = juvenile ? 2 : 4;
        var clumpCenters = new Vec3[clumpCount];
        for (int c = 0; c < clumpCount; c++)
        {
            double cAng = c * (Math.PI * 2.0 / clumpCount) + rng.Range(-0.25, 0.25);
            double cDist = radius * rng.Range(0.05, 0.14) * heightScale;
            clumpCenters[c] = new Vec3(Math.Cos(cAng) * cDist, height * 0.020, Math.Sin(cAng) * cDist);
            WoodEllipsoid(part, clumpCenters[c], new Vec3(radius * 0.08, height * 0.024, radius * 0.08), 2, 6, barkCol);
        }

        var catkinAxils = new List<(Vec3 Pos, Vec3 Dir)>();

        // Total stems: adult 18-21, juvenile 8
        int totalStems = juvenile ? 8 : 19 + rng.NextInt(3);
        for (int s = 0; s < totalStems; s++)
        {
            var clump = clumpCenters[s % clumpCount];
            // Varied heights and roles:
            // 0: tall emergent cane (full height 0.95h - 1.15h)
            // 1: mid-tier arching wand (~0.72h - 0.88h)
            // 2: low tangled skirt/sucker (~0.50h - 0.65h)
            int role = s % 3;
            double hMult = role switch
            {
                0 => rng.Range(0.95, 1.15),
                1 => rng.Range(0.72, 0.88),
                _ => rng.Range(0.50, 0.65)
            };
            double rMult = role switch
            {
                0 => rng.Range(0.68, 0.85),
                1 => rng.Range(0.78, 0.95),
                _ => rng.Range(0.85, 1.10)
            };

            double h = height * hMult * heightScale;
            double reach = radius * rMult * heightScale;

            // Clustered irregular azimuth with natural crossing
            double baseAz = s * (Math.PI * 2.0 / totalStems) + rng.Range(-0.35, 0.35);
            var radial = new Vec3(Math.Cos(baseAz), 0, Math.Sin(baseAz));
            var tangent = new Vec3(-Math.Sin(baseAz), 0, Math.Cos(baseAz));

            // Stems emerge from clump with slight jitter
            var rootSpot = clump + new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02)) * radius;
            BranchCollar(part, rootSpot, 0.024 * (hMult > 0.8 ? 1.1 : 0.85), barkCol);

            // Sinuous tangled path crossing neighboring canes
            double bow1 = rng.Range(-0.16, 0.16) * radius;
            double bow2 = rng.Range(-0.14, 0.14) * radius;

            var p0 = rootSpot;
            var p1 = rootSpot + radial * (reach * 0.22) + tangent * bow1 + Vec3.Up * (h * 0.32);
            var p2 = radial * (reach * 0.74) + tangent * bow2 + Vec3.Up * (h * 0.76);
            var p3 = radial * reach + tangent * (bow1 * 0.4) + Vec3.Up * h;

            int stations = NewShrubsStations(part, 8);
            var path = SampleBezier(p0, p1, p2, p3, stations);
            var radii = new double[stations];
            double baseStemR = MathD.Lerp(0.018, 0.028, (hMult - 0.5) / 0.65);
            double tipStemR = MathD.Lerp(0.0045, 0.0065, (hMult - 0.5) / 0.65);
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                radii[i] = MathD.Lerp(baseStemR, tipStemR, t);
            }
            WoodTube(part, path, radii, NewShrubsSides(part, 6), barkCol);

            // Occasional swollen dormant node on the lower/mid wood
            if (!juvenile && s % 2 == 0)
            {
                WoodEllipsoid(part, NewShrubsAlong(path, 0.40), new Vec3(0.011, 0.008, 0.011), 2, 5, barkCol);
            }

            var stemTip = path[^1];
            catkinAxils.Add((stemTip, radial * 0.5 + Vec3.Up * 0.5));

            // Branchlets begin in the lower third and continue into the upper cane; keep the root collar itself open.
            int twigs = (role == 2) ? (juvenile ? 1 : 2) : (juvenile ? 2 : 3 + rng.NextInt(2));
            for (int tw = 0; tw < twigs; tw++)
            {
                double tTw = 0.27 + 0.63 * ((tw + 0.5) / twigs) + rng.Range(-0.04, 0.04);
                var twigBase = NewShrubsAlong(path, Math.Clamp(tTw, 0.0, 1.0));
                var caneTangent = PathTangent(path, tTw);

                double twAz = baseAz + (tw % 2 == 0 ? 0.65 : -0.65) + rng.Range(-0.20, 0.20);
                var twDir = new Vec3(Math.Cos(twAz), 0.36, Math.Sin(twAz)).Normalized();
                double twLen = radius * rng.Range(0.20, 0.32) * heightScale * (hMult > 0.8 ? 1.0 : 0.8);

                // Flared branch collar at junction
                BranchCollar(part, twigBase, 0.009, barkCol);

                // Smooth cubic Bezier path flowing naturally from cane tangent
                var tp0 = twigBase;
                var tp1 = twigBase + (caneTangent * 0.50 + twDir * 0.50).Normalized() * (twLen * 0.32);
                var tp2 = twigBase + twDir * (twLen * 0.72) + Vec3.Up * (twLen * 0.12);
                var tp3 = twigBase + twDir * twLen + Vec3.Up * (twLen * 0.18);
                var twigPath = SampleBezier(tp0, tp1, tp2, tp3, 6);
                var twigRadii = new[] { 0.0080, 0.0062, 0.0046, 0.0034, 0.0024, 0.0016 };

                WoodTube(part, twigPath, twigRadii, NewShrubsSides(part, 5), barkCol);
                var twigTip = twigPath[^1];
                catkinAxils.Add((twigTip, twDir * 0.5 + Vec3.Up * 0.5));

                MiremyrtleResinDroplets(twigBase + twDir * (twLen * 0.20));

                // Dense 3D spiral leaf clusters along the distal branchlet
                int twigLeaves = juvenile ? 6 : 11;
                for (int j = 0; j < twigLeaves; j++)
                {
                    double t = 0.22 + 0.75 * ((j + 0.5) / twigLeaves) + rng.Range(-0.02, 0.02);
                    var at = NewShrubsAlong(twigPath, Math.Clamp(t, 0.0, 1.0));
                    double leafAz = twAz + j * 2.399963 + rng.Range(-0.16, 0.16);
                    double pitch = rng.Range(0.28, 0.46);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.82, pitch, Math.Sin(leafAz) * 0.82).Normalized();
                    double len = height * rng.Range(0.052, 0.074) * heightScale;
                    var tip = at + dir * len + Vec3.Up * (len * 0.12);
                    var sideHint = Vec3.Up.Cross(dir);
                    LeafUpward(part, at, tip, sideHint, len * 0.20, leafBase, leafTip,
                        camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
                }

                // Terminal leaf tuft on twig tip
                for (int k = 0; k < (juvenile ? 2 : 4); k++)
                {
                    double leafAz = twAz + k * 2.399963 + 0.8 + rng.Range(-0.12, 0.12);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.60, 0.62, Math.Sin(leafAz) * 0.60).Normalized();
                    double len = height * rng.Range(0.046, 0.068) * heightScale;
                    var tip = twigTip + dir * len + Vec3.Up * (len * 0.10);
                    var sideHint = Vec3.Up.Cross(dir);
                    LeafUpward(part, twigTip, tip, sideHint, len * 0.20, leafBase, leafTip,
                        camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
                }

                // Minor tertiary branchlet
                if (!juvenile && !cull && tw == 0)
                {
                    double bt = 0.45;
                    var brBase = NewShrubsAlong(twigPath, bt);
                    var twTangent = PathTangent(twigPath, bt);
                    double brAz = twAz + 0.85 + rng.Range(-0.14, 0.14);
                    var brDir = new Vec3(Math.Cos(brAz), 0.42, Math.Sin(brAz)).Normalized();
                    double brLen = 0.034 * heightScale;
                    double brLeafLen = height * 0.056 * heightScale;
                    NewShrubsBranchlet(part, brBase, twTangent, brDir, brLen, brLeafLen, brLeafLen * 0.20, 3, 3,
                        leafBase, leafTip, barkCol, rng, brAz);
                }
            }

            // Distal spiral leaves on upper 35% of cane
            int caneLeaves = juvenile ? 6 : 13;
            for (int j = 0; j < caneLeaves; j++)
            {
                double t = 0.60 + 0.38 * ((j + 0.5) / caneLeaves) + rng.Range(-0.02, 0.02);
                var at = NewShrubsAlong(path, Math.Clamp(t, 0.0, 1.0));
                double leafAz = baseAz + j * 2.399963 + 0.4 + rng.Range(-0.16, 0.16);
                double pitch = rng.Range(0.30, 0.50);
                var dir = new Vec3(Math.Cos(leafAz) * 0.80, pitch, Math.Sin(leafAz) * 0.80).Normalized();
                double len = height * rng.Range(0.054, 0.076) * heightScale;
                var tip = at + dir * len + Vec3.Up * (len * 0.12);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, at, tip, sideHint, len * 0.20, leafBase, leafTip,
                    camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));

                if (j % 4 == 1)
                    catkinAxils.Add((at, dir * 0.6 + Vec3.Up * 0.4));
            }

            // Cane terminal leaf cap
            for (int k = 0; k < (juvenile ? 2 : 4); k++)
            {
                double leafAz = baseAz + k * 2.399963 + 0.5 + rng.Range(-0.12, 0.12);
                var dir = new Vec3(Math.Cos(leafAz) * 0.52, 0.70, Math.Sin(leafAz) * 0.52).Normalized();
                double len = height * rng.Range(0.048, 0.070) * heightScale;
                var tip = stemTip + dir * len + Vec3.Up * (len * 0.10);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, stemTip, tip, sideHint, len * 0.20, leafBase, leafTip,
                    camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
            }
        }

        // Luminous candle catkins emerging naturally from axils and terminal buds
        int catkinCount = juvenile ? 4 : 22 + rng.NextInt(5);
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

        // Mossy burred coppice stool with root buttresses
        WoodEllipsoid(part, new Vec3(0, height * 0.032, 0),
            new Vec3(radius * 0.24, height * 0.046, radius * 0.24), 3, 9, barkCol);
        for (int fb = 0; fb < 4; fb++)
        {
            double faz = fb * (Math.PI * 0.5) + 0.3;
            var fPos = new Vec3(Math.Cos(faz), 0, Math.Sin(faz)) * (radius * 0.16) + Vec3.Up * (height * 0.015);
            WoodEllipsoid(part, fPos, new Vec3(radius * 0.09, height * 0.022, radius * 0.09), 2, 6, barkCol);
        }

        // 12-15 gracefully arching, bowed hazel wand poles
        int wands = juvenile ? 6 : 13 + rng.NextInt(3);
        var boughTips = new List<Vec3>();
        var nutForks = new List<(Vec3 Pos, Vec3 Dir)>();

        for (int p = 0; p < wands; p++)
        {
            // Multi-tiered wand roles:
            // 0: Tall dominant arching canopy wand (0.95h - 1.15h)
            // 1: Mid-height bowed wand (0.75h - 0.90h)
            // 2: Supple outer bowed wand (0.55h - 0.70h)
            int role = p % 3;
            double hMult = role switch
            {
                0 => rng.Range(0.95, 1.15),
                1 => rng.Range(0.75, 0.90),
                _ => rng.Range(0.55, 0.70)
            };
            double rMult = role switch
            {
                0 => rng.Range(0.70, 0.88),
                1 => rng.Range(0.82, 1.00),
                _ => rng.Range(0.90, 1.15)
            };

            double az = p * (Math.PI * 2.0 / wands) + rng.Range(-0.25, 0.25);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double reach = radius * rMult * heightScale;
            double h = height * hMult * heightScale;
            double bow = rng.Range(-0.12, 0.12) * radius;

            // Flared basal collar at coppice stool
            var stoolSpot = radial * (radius * 0.10);
            BranchCollar(part, stoolSpot, 0.032 * (hMult > 0.8 ? 1.1 : 0.85), barkCol);

            // True bowed wand curve: shoots steeply from stool, arches out, tip nods slightly under gravity
            var p0 = stoolSpot;
            var p1 = stoolSpot + radial * (reach * 0.16) + Vec3.Up * (h * 0.46);
            var p2 = radial * (reach * 0.74) + tangent * bow + Vec3.Up * (h * 0.92);
            var p3 = radial * reach + tangent * (bow * 1.4) + Vec3.Up * (h * 0.80);

            int stations = NewShrubsStations(part, 8);
            var wandPath = SampleBezier(p0, p1, p2, p3, stations);
            var radii = new double[stations];
            double baseWandR = MathD.Lerp(0.022, 0.034, (hMult - 0.55) / 0.60);
            double tipWandR = MathD.Lerp(0.005, 0.0075, (hMult - 0.55) / 0.60);
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                radii[i] = MathD.Lerp(baseWandR, tipWandR, t);
            }
            WoodTube(part, wandPath, radii, NewShrubsSides(part, 6), barkCol);
            var wandTip = wandPath[^1];
            boughTips.Add(wandTip);

            // Spreading lateral boughs forming layered canopy tiers on upper 60% of wands
            int boughs = (role == 2) ? (juvenile ? 2 : 3) : (juvenile ? 2 : 4 + rng.NextInt(2));
            for (int b = 0; b < boughs; b++)
            {
                double tBough = 0.34 + 0.58 * ((b + 0.5) / boughs) + rng.Range(-0.065, 0.065) + (role - 1) * 0.025;
                var boughBase = NewShrubsAlong(wandPath, Math.Clamp(tBough, 0.0, 1.0));
                var wandTangent = PathTangent(wandPath, tBough);

                double bAz = az + (b % 2 == 0 ? 0.72 : -0.72) + rng.Range(-0.18, 0.18);
                var bDir = new Vec3(Math.Cos(bAz), rng.Range(-0.10, 0.40), Math.Sin(bAz)).Normalized();
                double bLen = radius * rng.Range(0.24, 0.46) * heightScale;

                // Organic flared branch collar at junction
                BranchCollar(part, boughBase, 0.013, twigCol);
                nutForks.Add((boughBase, bDir));

                // Smooth cubic Bezier path for lateral bough
                var bp0 = boughBase;
                var bp1 = boughBase + (wandTangent * 0.45 + bDir * 0.55).Normalized() * (bLen * 0.32);
                var bp2 = boughBase + bDir * (bLen * 0.72) + Vec3.Up * (bLen * 0.06);
                var bp3 = boughBase + bDir * bLen + Vec3.Up * (bLen * 0.10);
                var bPath = SampleBezier(bp0, bp1, bp2, bp3, 6);
                var bRadii = new[] { 0.012, 0.0088, 0.0064, 0.0046, 0.0032, 0.0022 };

                WoodTube(part, bPath, bRadii, NewShrubsSides(part, 5), twigCol);
                var bTip = bPath[^1];
                boughTips.Add(bTip);

                // Short horizontal twiglets off boughs creating multi-leaf canopy fans
                int twiglets = cull ? 1 : 2;
                for (int tw = 0; tw < twiglets; tw++)
                {
                    double twT = 0.45 + 0.38 * tw;
                    var twBase = NewShrubsAlong(bPath, twT);
                    var bTangent = PathTangent(bPath, twT);
                    double twSign = (tw % 2 == 0) ? 1.0 : -1.0;
                    double twAz = bAz + twSign * 0.85 + rng.Range(-0.15, 0.15);
                    var twDir = new Vec3(Math.Cos(twAz), 0.12, Math.Sin(twAz)).Normalized();
                    double twLen = bLen * 0.45;

                    BranchCollar(part, twBase, 0.006, twigCol);
                    var twEnd = twBase + twDir * twLen;
                    WoodTube(part, new[] { twBase, twEnd }, new[] { 0.0042, 0.0018 }, NewShrubsSides(part, 4), twigCol);
                    boughTips.Add(twEnd);

                    // Leaves on twiglet forming layered horizontal fan
                    int twLeaves = juvenile ? 3 : 5;
                    for (int lj = 0; lj < twLeaves; lj++)
                    {
                        double lt = (lj + 0.5) / twLeaves;
                        var lat = Vec3.Lerp(twBase, twEnd, lt);
                        double lSign = (lj % 2 == 0) ? 1.0 : -1.0;
                        double lAz = twAz + lSign * 0.68 + rng.Range(-0.12, 0.12);
                        var lDir = new Vec3(Math.Cos(lAz) * 0.88, rng.Range(-0.30, 0.52), Math.Sin(lAz) * 0.88).Normalized();
                        double len = rng.Range(0.095, 0.14) * heightScale;
                        var tip = lat + lDir * len + Vec3.Up * (len * rng.Range(-0.10, 0.12));
                        var sideHint = Vec3.Up.Cross(lDir);
                        LeafUpward(part, lat, tip, sideHint, len * 0.45, leafC1, leafC2,
                            camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
                    }
                }

                // Broad cordate/ovate leaves along the lateral bough forming soft layered canopies
                int leavesOnBough = juvenile ? 5 : 9;
                for (int j = 0; j < leavesOnBough; j++)
                {
                    double t = 0.22 + 0.74 * ((j + 0.5) / leavesOnBough) + rng.Range(-0.02, 0.02);
                    var at = NewShrubsAlong(bPath, Math.Clamp(t, 0.0, 1.0));
                    double sideSign = (j % 2 == 0) ? 1.0 : -1.0;
                    double leafAz = bAz + sideSign * 0.70 + rng.Range(-0.16, 0.16);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.86, rng.Range(-0.32, 0.52), Math.Sin(leafAz) * 0.86).Normalized();

                    double len = rng.Range(0.10, 0.145) * heightScale;
                    var tip = at + dir * len + Vec3.Up * rng.Range(-len * 0.08, len * 0.14);
                    var sideHint = Vec3.Up.Cross(dir);

                    LeafUpward(part, at, tip, sideHint, len * 0.46, leafC1, leafC2,
                        camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.11, 0.11));
                }
            }

            // Broad leaves along distal section of wands
            int wandLeaves = juvenile ? 4 : 8;
            for (int j = 0; j < wandLeaves; j++)
            {
                double t = 0.65 + 0.32 * ((j + 0.5) / wandLeaves) + rng.Range(-0.02, 0.02);
                var at = NewShrubsAlong(wandPath, Math.Clamp(t, 0.0, 1.0));
                double leafAz = az + ((j % 2 == 0) ? 0.68 : -0.68) + rng.Range(-0.15, 0.15);
                var dir = new Vec3(Math.Cos(leafAz) * 0.85, rng.Range(-0.32, 0.52), Math.Sin(leafAz) * 0.85).Normalized();
                double len = rng.Range(0.105, 0.15) * heightScale;
                var tip = at + dir * len + Vec3.Up * rng.Range(-len * 0.08, len * 0.14);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, at, tip, sideHint, len * 0.46, leafC1, leafC2,
                    camber: len * 0.15, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
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

            // Rosette spray of 4-5 lobed hawthorn leaves with guaranteed upward normals
            int leafCount = juvenile ? 4 : 5;
            for (int j = 0; j < leafCount; j++)
            {
                double leafAz = clusterIdx * 2.399963 + j * (Math.PI * 2.0 / leafCount) + rng.Range(-0.20, 0.20);
                var dir = new Vec3(Math.Cos(leafAz), rng.Range(0.25, 0.48), Math.Sin(leafAz)).Normalized();
                double len = rng.Range(0.068, 0.102) * heightScale;
                var tip = spurEnd + dir * len + Vec3.Up * (len * 0.10);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, spurEnd, tip, sideHint, len * 0.34, leafBase, leafTip,
                    camber: len * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
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

        // Gnarled multi-lobed root boss with mossy crevices
        WoodEllipsoid(part, new Vec3(0, height * 0.028, 0),
            new Vec3(radius * 0.22, height * 0.038, radius * 0.22), 3, 8, barkCol);
        for (int rb = 0; rb < 4; rb++)
        {
            double rbAz = rb * (Math.PI * 0.5) + 0.4;
            var rbPos = new Vec3(Math.Cos(rbAz), 0, Math.Sin(rbAz)) * (radius * 0.14) + Vec3.Up * (height * 0.014);
            WoodEllipsoid(part, rbPos, new Vec3(radius * 0.08, height * 0.022, radius * 0.08), 2, 6, barkCol);
        }

        // 7-8 gnarled, twisting, interwoven trunks (fairytale thicket)
        int trunks = juvenile ? 4 : 7 + rng.NextInt(2);
        for (int s = 0; s < trunks; s++)
        {
            double az = s * (Math.PI * 2.0 / trunks) + rng.Range(-0.30, 0.30);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double reach = radius * rng.Range(0.62, 0.85) * heightScale;
            double h = height * rng.Range(0.65, 0.85) * heightScale;

            // Flanged root base
            var rootPos = radial * (radius * 0.08);
            BranchCollar(part, rootPos, 0.036, barkCol);

            // Gnarled twisting cubic Bezier curve for main trunk with serpentine excursions
            var p0 = rootPos;
            var p1 = rootPos + radial * (reach * 0.24) + tangent * (0.12 * Math.Sin(s * 1.7) * radius) + Vec3.Up * (h * 0.32);
            var p2 = radial * (reach * 0.70) - tangent * (0.10 * Math.Cos(s * 1.5) * radius) + Vec3.Up * (h * 0.74);
            var p3 = radial * reach + tangent * (0.06 * Math.Sin(s * 2.3) * radius) + Vec3.Up * h;

            int stations = NewShrubsStations(part, 8);
            var trunkPath = SampleBezier(p0, p1, p2, p3, stations);
            var radii = new double[stations];
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                radii[i] = MathD.Lerp(0.034, 0.009, t);
                if (!juvenile)
                    radii[i] *= 1 - 0.52 * MathD.SmoothStep(0.72, 1.0, t);
            }
            WoodTube(part, trunkPath, radii, NewShrubsSides(part, 6), barkCol);

            // Arm just a few upper leader axils; these hash-derived placements do
            // not shift the existing RNG-driven twig, blossom, or haw arrangement.
            if (!juvenile && !cull && (s % 3 == 1 || Rng.HashUnit(seed, 0x54484158, (ulong)s) < 0.12))
            {
                ulong leaderIndex = (ulong)s;
                double axilT = 0.76 + 0.08 * Rng.HashUnit(seed, 0x54484159, leaderIndex);
                var axil = NewShrubsAlong(trunkPath, axilT);
                double hand = Rng.HashUnit(seed, 0x5448415A, leaderIndex) < 0.5 ? -1.0 : 1.0;
                var thornDir = (radial * 0.46 + tangent * hand * 0.20 + Vec3.Up * 0.66).Normalized();
                BranchCollar(part, axil, 0.0085, barkCol);
                ThornmantleBranchingThorn(axil, thornDir, radius * 0.105, hand);
            }

            // Short lower cross-shoots weave between leaders without thickening the upper crown.
            int basalCrossShoots = juvenile ? 1 : 2;
            for (int bs = 0; bs < basalCrossShoots; bs++)
            {
                double attachT = 0.14 + bs * 0.12 + rng.Range(-0.025, 0.025);
                var attach = NewShrubsAlong(trunkPath, attachT);
                var parentTan = PathTangent(trunkPath, attachT);
                double crossSign = bs % 2 == 0 ? 1.0 : -1.0;
                var crossDir = (tangent * crossSign * 0.70 + radial * 0.35 + Vec3.Up * 0.06).Normalized();
                double crossLen = radius * rng.Range(0.16, 0.24) * heightScale;
                var q0 = attach;
                var q1 = attach + (parentTan * 0.40 + crossDir * 0.60).Normalized() * (crossLen * 0.38);
                var q2 = attach + crossDir * (crossLen * 0.72) - Vec3.Up * (crossLen * 0.06);
                var q3 = attach + crossDir * crossLen + Vec3.Up * (crossLen * 0.04);
                var crossPath = SampleBezier(q0, q1, q2, q3, 4);
                WoodTube(part, crossPath, new[] { 0.0080, 0.0058, 0.0038, 0.0018 }, NewShrubsSides(part, 5), barkCol);
                ThornmantleThorn(NewShrubsAlong(crossPath, 0.42), crossDir + Vec3.Up * 0.45, 0.026 * heightScale);
                ThornmantleThorn(NewShrubsAlong(crossPath, 0.82), -crossDir * 0.25 + Vec3.Up * 0.75, 0.022 * heightScale);
            }

            // Gnarled branching boughs
            int twigs = juvenile ? 3 : 4;
            for (int tw = 0; tw < twigs; tw++)
            {
                double tTw = 0.22 + 0.60 * ((tw + 0.5) / twigs) + rng.Range(-0.05, 0.05);
                var twigBase = NewShrubsAlong(trunkPath, Math.Clamp(tTw, 0.0, 1.0));
                var trunkTangent = PathTangent(trunkPath, tTw);

                double twAz = az + (tw % 2 == 0 ? 0.75 : -0.75) + rng.Range(-0.18, 0.18);
                var twDir = new Vec3(Math.Cos(twAz), 0.28, Math.Sin(twAz)).Normalized();
                double twLen = radius * rng.Range(0.28, 0.42) * heightScale;

                // Branch collar at fork
                BranchCollar(part, twigBase, 0.014, barkCol);

                // Smooth cubic Bezier path for tortuous gnarled bough
                var tp0 = twigBase;
                var tp1 = twigBase + (trunkTangent * 0.45 + twDir * 0.55).Normalized() * (twLen * 0.32);
                var tp2 = twigBase + twDir * (twLen * 0.72) + Vec3.Up * (twLen * 0.10);
                var tp3 = twigBase + twDir * twLen + Vec3.Up * (twLen * 0.14);
                var twigPath = SampleBezier(tp0, tp1, tp2, tp3, 6);
                var twigRadii = new[] { 0.012, 0.0085, 0.0060, 0.0042, 0.0030, 0.0022 };

                WoodTube(part, twigPath, twigRadii, NewShrubsSides(part, 5), barkCol);

                // Thorns at knot stations
                ThornmantleThorn(NewShrubsAlong(twigPath, 0.35), twDir + Vec3.Up * 0.5, 0.030 * heightScale);
                ThornmantleThorn(NewShrubsAlong(twigPath, 0.70), -twDir * 0.3 + Vec3.Up * 0.7, 0.028 * heightScale);

                // Spur rosettes distributed naturally along the bough
                int spurs = juvenile ? 3 : 6;
                for (int qi = 0; qi < spurs; qi++)
                {
                    double qt = 0.20 + 0.75 * qi / Math.Max(1, spurs - 1);
                    var qb = NewShrubsAlong(twigPath, qt);
                    double spPitch = (qi % 2 == 0) ? 0.45 : 0.20;
                    double spAz = twAz + ((qi % 2 == 0) ? 0.60 : -0.60);
                    var sDir = new Vec3(Math.Cos(spAz), spPitch, Math.Sin(spAz)).Normalized();
                    ThornmantleSpur(qb, sDir, s * 100 + tw * 10 + qi);
                }
            }

            // Spurs directly along main trunk
            for (int sq = 0; sq < (juvenile ? 2 : 3); sq++)
            {
                bool terminal = !juvenile && sq == 2;
                double st = terminal ? 1.0 : 0.42 + 0.48 * sq / 2.0;
                var sb = NewShrubsAlong(trunkPath, st);
                var sDir = (radial + Vec3.Up * (terminal ? 0.55 : 0.30)).Normalized();
                if (terminal) terminalSpurs.Add(spurNodes.Count);
                ThornmantleSpur(sb, sDir, s * 50 + sq);
            }
        }

        // Each adult leader finishes in a flowering spur. Spread the remaining blossoms across the thicket.
        int blossomCount = juvenile ? 6 : 32 + rng.NextInt(8);
        var lateralSpurs = new List<Vec3>();
        int blossomsPlaced = 0;
        for (int i = 0; i < spurNodes.Count; i++)
        {
            var (at, _) = spurNodes[i];
            if (terminalSpurs.Contains(i)) AddSpurBlossom(at);
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

        // Hanging ruby-garnet haws interspersed across canopy spurs
        int hawClusters = juvenile ? 4 : 14 + rng.NextInt(4);
        for (int hi = 0; hi < hawClusters && spurNodes.Count > 0; hi++)
        {
            var (anchor, _) = spurNodes[(hi * 4 + 2) % spurNodes.Count];
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

        // Fluted basal trunk with weathered shari ridges
        double trunkH = height * rng.Range(0.12, 0.16) * heightScale;
        double baseR = radius * rng.Range(0.14, 0.18) * heightScale;
        double topR = baseR * 0.65;
        int trunkStations = NewShrubsStations(part, 5);
        int trunkSides = NewShrubsSides(part, 10);
        {
            int start = part.VertexCount;
            for (int i = 0; i < trunkStations; i++)
            {
                double t = i / (double)(trunkStations - 1);
                double flare = 1.0 + 0.55 * Math.Exp(-t * 8.0);
                double rr = MathD.Lerp(baseR, topR, t) * flare;
                var centre = new Vec3(0, trunkH * t, 0);
                for (int sgi = 0; sgi <= trunkSides; sgi++)
                {
                    double th = 2.0 * Math.PI * sgi / trunkSides;
                    double flute = 1.0 + 0.25 * Math.Cos(th * 6.0) * (1.0 - 0.40 * t);
                    var radial = new Vec3(Math.Cos(th), 0, Math.Sin(th));
                    part.AddVertex(centre + radial * (rr * flute), radial.Normalized(), barkCol, 1.0,
                        t * trunkH, (double)sgi / trunkSides, 2.0, 0.0);
                }
            }
            for (int i = 0; i < trunkStations - 1; i++)
                for (int sgi = 0; sgi < trunkSides; sgi++)
                {
                    int a = start + i * (trunkSides + 1) + sgi;
                    int b = a + trunkSides + 1;
                    part.AddTriangle(a, a + 1, b);
                    part.AddTriangle(a + 1, b + 1, b);
                }
            part.Structural.Add((start, part.VertexCount - start, trunkSides, baseR));
        }
        var crownBase = new Vec3(0, trunkH * 0.92, 0);

        // Volumetric tiered cloud-pad builder (3D branched armature + multi-layered overlapping scale sprays)
        var cloudBerrySpots = new List<Vec3>();
        void VolumetricCloudPad(Vec3 start, Vec3 end, Vec3 boughTangent, bool terminal = false)
        {
            var axis = end - start;
            double len = axis.Length;
            if (len < 1e-6) return;
            var fwd = axis / len;
            var side = Vec3.Up.Cross(fwd);
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0).Cross(fwd);
            side = side.Normalized();

            // Main support twig of cloud pad
            BranchCollar(part, start, 0.0075, barkCol);
            var cp0 = start;
            var cp1 = start + (boughTangent * 0.40 + fwd * 0.60).Normalized() * (len * 0.35);
            var cp2 = start + fwd * (len * 0.72) + Vec3.Up * (len * 0.04);
            var cp3 = end;
            var mainPath = SampleBezier(cp0, cp1, cp2, cp3, 5);
            WoodTube(part, mainPath, new[] { 0.0065, 0.0048, 0.0035, 0.0024, 0.0016 },
                NewShrubsSides(part, 5), barkCol);

            // Spreading lateral sub-twigs creating 3D cloud cushion
            int subTwigs = cull ? 3 : 5;
            for (int st = 0; st < subTwigs; st++)
            {
                double q = 0.28 + 0.58 * ((st + 0.5) / subTwigs);
                var subBase = NewShrubsAlong(mainPath, q);
                double sign = (st % 2 == 0) ? 1.0 : -1.0;
                double fanOut = 0.35 + 0.15 * (st / 2);
                double upTilt = (st % 2 == 0) ? 0.18 : 0.06;
                var subDir = (fwd * 0.70 + side * (sign * fanOut) + Vec3.Up * upTilt).Normalized();
                double subLen = len * 0.42;
                var subEnd = subBase + subDir * subLen;

                WoodTube(part, new[] { subBase, subEnd }, new[] { 0.0030, 0.0014 }, NewShrubsSides(part, 4), barkCol);
                cloudBerrySpots.Add(subEnd);

                // Overlapping scale sprays on sub-twig at multiple 3D angles
                int subScales = cull ? 3 : 5;
                for (int sc = 0; sc < subScales; sc++)
                {
                    double sq = (sc + 0.5) / subScales;
                    var at = Vec3.Lerp(subBase, subEnd, sq);
                    double size = rng.Range(0.065, 0.095) * heightScale;

                    // Lateral fan scale
                    double along = terminal ? 0.34 : 0.12;
                    var lTip = at + side * (sign * size * 0.42) + fwd * (size * along) + Vec3.Up * (size * (terminal ? 0.30 : 0.38));
                    LeafUpward(part, at, lTip, (side * sign + Vec3.Up * 0.45).Normalized(), size * 0.075, needleBase, needleTip,
                        camber: 0.002, longitudinal: 3);

                    // Dorsal arched scale
                    var dTip = at + fwd * (size * (terminal ? 0.44 : 0.24)) - side * (sign * size * 0.28) + Vec3.Up * (size * 0.38);
                    LeafUpward(part, at, dTip, (side * 0.15 + Vec3.Up).Normalized(), size * 0.072, needleBase, needleTip,
                        camber: 0.002, longitudinal: 3);
                }
            }

            // Dense layered scales along main pad axis forming convex cushion
            int stations = cull ? 7 : 11;
            for (int i = 0; i < stations; i++)
            {
                double q = (i + 0.5) / stations;
                var at = NewShrubsAlong(mainPath, q);
                double size = rng.Range(0.075, 0.110) * heightScale;

                // Bilateral flank scales with upward normals
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var tip = at + side * (sign * size * 0.42) + fwd * (size * 0.10) + Vec3.Up * (size * 0.38);
                    LeafUpward(part, at, tip, (side * sign + Vec3.Up * 0.50).Normalized(), size * 0.072, needleBase, needleTip,
                        camber: 0.002, longitudinal: 3);
                }

                // Dorsal scale
                var dTip = at + fwd * (size * 0.22) + side * (rng.Range(-0.10, 0.10) * size) + Vec3.Up * (size * 0.42);
                LeafUpward(part, at, dTip, (side * 0.15 + Vec3.Up).Normalized(), size * 0.072, needleBase, needleTip,
                    camber: 0.002, longitudinal: 3);
            }
        }

        // Frosted Sapphire-Cobalt Galbulus Berry-Cone
        void GreywatchBerry(Vec3 at)
        {
            int v0 = part.VertexCount;
            double r = rng.Range(0.0085, 0.0125) * heightScale;
            // Sapphire berry body
            Primitives.Ellipsoid(part, at, new Vec3(r, r, r), 3, 6,
                (u, v) => (berrySapphire, 1.0, u, v, 0.0, 0.0));
            // Epicuticular frost bloom cap
            Primitives.Ellipsoid(part, at + Vec3.Up * (r * 0.85), new Vec3(r * 0.48, r * 0.25, r * 0.48), 2, 5,
                (u, v) => (bloomIce, 1.0, u, v, 0.0, 0.0));
            MarkOrgan(part, v0, part.VertexCount - v0, 1.22f, 0.82f);
        }

        // Golden-ochre pollen cones at tips
        void GreywatchPollenCone(Vec3 at, Vec3 fwd)
        {
            if (cull) return;
            int v0 = part.VertexCount;
            var tip = at + fwd * 0.008;
            WoodTube(part, new[] { at, tip }, new[] { 0.0022, 0.0010 }, NewShrubsSides(part, 4), pollenGold);
            MarkOrgan(part, v0, part.VertexCount - v0, 1.25f, 0.85f);
        }

        // 6-7 sinewy krummholz limbs sweeping in wind-sculpted asymmetric S-curves
        int limbs = juvenile ? 3 : 6 + rng.NextInt(2);
        for (int l = 0; l < limbs; l++)
        {
            // Windward vs Leeward asymmetry:
            // Limbs on windward side stay low & hugging the terrain; limbs on leeward side sweep out & up
            bool isWindward = l < 2;
            double az = isWindward
                ? windAz + Math.PI + rng.Range(-0.55, 0.55)
                : windAz + ((l - 2) * (Math.PI * 1.5 / Math.Max(1, limbs - 2))) + rng.Range(-0.25, 0.25);

            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));

            double outReach = isWindward
                ? (radius * 0.75) * rng.Range(0.85, 1.10)
                : (radius * 0.85) * rng.Range(0.95, 1.22);

            double riseTop = isWindward
                ? height * rng.Range(0.28, 0.42) * heightScale
                : height * rng.Range(0.55, 0.78) * heightScale;

            double sCurvature = isWindward ? rng.Range(-0.15, 0.15) : rng.Range(-0.25, 0.25);

            // Flared branch crotch at trunk attachment
            BranchCollar(part, crownBase, 0.032, barkCol);

            // Smooth cubic Bezier path for windswept krummholz limb
            var p0 = crownBase;
            var p1 = crownBase + radial * (outReach * 0.28) + tangent * (sCurvature * radius) + Vec3.Up * (riseTop * 0.25);
            var p2 = crownBase + radial * (outReach * 0.72) - tangent * (sCurvature * 0.6 * radius) + Vec3.Up * (riseTop * 0.72);
            var p3 = crownBase + radial * outReach + tangent * (sCurvature * 0.4 * radius) + Vec3.Up * riseTop;

            int stations = NewShrubsStations(part, 7);
            var path = SampleBezier(p0, p1, p2, p3, stations);
            var radii = new double[stations];
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                radii[i] = MathD.Lerp(0.032, 0.0075, t) * heightScale;
            }
            WoodTube(part, path, radii, NewShrubsSides(part, 6), barkCol);
            var limbTip = path[^1];

            // Small sprays along exposed upper limbs soften long bare spans before the terminal pad.
            int smallSprays = cull ? 1 : 3;
            for (int si = 0; si < smallSprays; si++)
            {
                double q = 0.34 + 0.17 * si + rng.Range(-0.035, 0.035);
                var sprayAt = NewShrubsAlong(path, q);
                var tangentDir = PathTangent(path, q);
                for (int ni = 0; ni < 2; ni++)
                {
                    double sideSign = ni == 0 ? -1.0 : 1.0;
                    var needleDir = (tangentDir * 0.58 + tangent * (sideSign * 0.48) + Vec3.Up * 0.18).Normalized();
                    double needleLen = rng.Range(0.075, 0.105) * heightScale;
                    var needlePos = sprayAt + needleDir * needleLen;
                    LeafUpward(part, sprayAt, needlePos, tangent * sideSign + Vec3.Up * 0.20, needleLen * 0.055, needleBase, needleTip,
                        camber: needleLen * 0.05, longitudinal: 3);
                }
            }

            // Tiered horizontal cloud-pads per bough
            int pads = juvenile ? 2 : 4 + rng.NextInt(2);
            for (int sp = 0; sp < pads; sp++)
            {
                double q = 0.28 + 0.66 * ((sp + 0.5) / pads) + rng.Range(-0.03, 0.03);
                var attach = NewShrubsAlong(path, Math.Clamp(q, 0.0, 1.0));
                var boughTangent = PathTangent(path, q);

                double padAz = az + (sp % 2 == 0 ? 0.48 : -0.48) + rng.Range(-0.12, 0.12);
                var padDir = (new Vec3(Math.Cos(padAz), 0.18, Math.Sin(padAz)) + tangent * (sp % 2 == 0 ? 0.15 : -0.15)).Normalized();
                double padLen = rng.Range(0.16, 0.24) * heightScale;
                var padEnd = attach + padDir * padLen;

                VolumetricCloudPad(attach, padEnd, boughTangent);
                cloudBerrySpots.Add(padEnd);
                GreywatchPollenCone(padEnd, padDir);
            }

            var termEnd = limbTip + radial * 0.05 + Vec3.Up * 0.02;
            VolumetricCloudPad(limbTip, termEnd, PathTangent(path, 1.0), terminal: true);
            cloudBerrySpots.Add(termEnd);
        }

        // Frosted sapphire berry-cones nestled in and around the volumetric cloud pads
        int berryCount = juvenile ? 8 : 34 + rng.NextInt(8);
        for (int b = 0; b < berryCount && cloudBerrySpots.Count > 0; b++)
        {
            var spot = cloudBerrySpots[b % cloudBerrySpots.Count] +
                new Vec3(rng.Range(-0.015, 0.015), rng.Range(-0.01, 0.015), rng.Range(-0.015, 0.015));
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

        // Central knobby rootstock
        WoodEllipsoid(part, new Vec3(0, height * 0.024, 0),
            new Vec3(radius * 0.20, height * 0.036, radius * 0.20), 3, 8, barkCol);

        // Low spreading undulating cushion: 16-19 decumbent crooked stems crawling over ground
        int stems = juvenile ? 7 : 16 + rng.NextInt(3);
        var catkinNodes = new List<(Vec3 Pos, Vec3 Dir)>();

        for (int s = 0; s < stems; s++)
        {
            // Varied reach and height for natural undulating cushion:
            // Interior stems fill center, outer stems sprawl across ground
            int role = s % 3;
            double rMult = role switch
            {
                0 => rng.Range(0.50, 0.70),
                1 => rng.Range(0.75, 0.95),
                _ => rng.Range(0.95, 1.18)
            };
            double hMult = role switch
            {
                0 => rng.Range(0.48, 0.62),
                1 => rng.Range(0.38, 0.52),
                _ => rng.Range(0.28, 0.40)
            };

            double az = s * (Math.PI * 2.0 / stems) + rng.Range(-0.25, 0.25);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var tangent = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double reach = radius * rMult * heightScale;
            double h = height * hMult * heightScale;
            double undulate = rng.Range(-0.06, 0.06);

            // Flared root collar at rootstock
            var rootSpot = radial * (radius * 0.08);
            BranchCollar(part, rootSpot, 0.022, barkCol);

            // Decumbent crooked path: stays low to ground then curls gently up at tip
            var p0 = rootSpot;
            var p1 = rootSpot + radial * (reach * 0.32) + tangent * undulate + Vec3.Up * (h * 0.15);
            var p2 = radial * (reach * 0.78) - tangent * (undulate * 0.6) + Vec3.Up * (h * 0.42);
            var p3 = radial * reach + Vec3.Up * h;

            int stations = NewShrubsStations(part, 7);
            var path = SampleBezier(p0, p1, p2, p3, stations);
            var radii = new double[stations];
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                radii[i] = MathD.Lerp(0.022, 0.0058, t);
            }
            WoodTube(part, path, radii, NewShrubsSides(part, 6), barkCol);
            var stemTip = path[^1];
            catkinNodes.Add((stemTip, radial * 0.4 + Vec3.Up * 0.6));

            // Spreading lateral twigs branching with swollen collars
            int twigs = juvenile ? 2 : 4;
            for (int tw = 0; tw < twigs; tw++)
            {
                double tTw = 0.35 + 0.16 * tw + rng.Range(-0.03, 0.03);
                var twigBase = NewShrubsAlong(path, Math.Clamp(tTw, 0.0, 1.0));
                var stemTangent = PathTangent(path, tTw);

                double twAz = az + (tw % 2 == 0 ? 0.74 : -0.74) + rng.Range(-0.15, 0.15);
                var twDir = new Vec3(Math.Cos(twAz), 0.22, Math.Sin(twAz)).Normalized();
                double twLen = radius * rng.Range(0.20, 0.30) * heightScale;

                // Branch collar at fork
                BranchCollar(part, twigBase, 0.009, barkCol);

                // Smooth cubic Bezier path for decumbent twig
                var tp0 = twigBase;
                var tp1 = twigBase + (stemTangent * 0.48 + twDir * 0.52).Normalized() * (twLen * 0.32);
                var tp2 = twigBase + twDir * (twLen * 0.70) + Vec3.Up * (twLen * 0.06);
                var tp3 = twigBase + twDir * twLen + Vec3.Up * (twLen * 0.10);
                var twigPath = SampleBezier(tp0, tp1, tp2, tp3, 5);
                var twigRadii = new[] { 0.0085, 0.0060, 0.0042, 0.0028, 0.0018 };

                WoodTube(part, twigPath, twigRadii, NewShrubsSides(part, 5), barkCol);
                var twigTip = twigPath[^1];
                catkinNodes.Add((twigTip, twDir * 0.4 + Vec3.Up * 0.6));

                // Felted silvery obovate leaves along twig with upward normals
                int twigLeaves = juvenile ? 6 : 12;
                for (int j = 0; j < twigLeaves; j++)
                {
                    double t = 0.14 + 0.78 * ((j + 0.5) / twigLeaves) + rng.Range(-0.02, 0.02);
                    var at = NewShrubsAlong(twigPath, Math.Clamp(t, 0.0, 1.0));
                    double leafAz = twAz + j * 2.399963 + rng.Range(-0.32, 0.32);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.78, rng.Range(0.28, 0.62), Math.Sin(leafAz) * 0.78).Normalized();
                    double len = rng.Range(0.050, 0.072) * heightScale;
                    var tip = at + dir * len + Vec3.Up * rng.Range(-len * 0.08, len * 0.14);
                    var sideHint = Vec3.Up.Cross(dir);
                    LeafUpward(part, at, tip, sideHint, len * 0.34, leafBase, leafTip, camber: len * 0.16, longitudinal: 5, asymmetry: rng.Range(-0.13, 0.13));
                }

                // Terminal whorl on twig
                for (int k = 0; k < (juvenile ? 2 : 3); k++)
                {
                    double leafAz = twAz + k * 2.399963 + 0.9 + rng.Range(-0.12, 0.12);
                    var dir = new Vec3(Math.Cos(leafAz) * 0.50, 0.62, Math.Sin(leafAz) * 0.50).Normalized();
                    double len = rng.Range(0.046, 0.068) * heightScale;
                    var tip = twigTip + dir * len + Vec3.Up * (len * 0.06);
                    var sideHint = Vec3.Up.Cross(dir);
                    LeafUpward(part, twigTip, tip, sideHint, len * 0.34, leafBase, leafTip, camber: len * 0.16, longitudinal: 5, asymmetry: rng.Range(-0.13, 0.13));
                }
            }

            // Distribute felted leaves along decumbent stem
            int stemLeaves = juvenile ? 7 : 15;
            for (int j = 0; j < stemLeaves; j++)
            {
                double t = 0.24 + 0.68 * ((j + 0.5) / stemLeaves);
                var at = NewShrubsAlong(path, Math.Clamp(t, 0.0, 1.0));
                double leafAz = az + j * 2.399963 + rng.Range(-0.15, 0.15);
                var dir = new Vec3(Math.Cos(leafAz) * 0.78, rng.Range(0.28, 0.62), Math.Sin(leafAz) * 0.78).Normalized();
                double len = rng.Range(0.050, 0.072) * heightScale;
                var tip = at + dir * len + Vec3.Up * rng.Range(-len * 0.08, len * 0.14);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, at, tip, sideHint, len * 0.34, leafBase, leafTip, camber: len * 0.16, longitudinal: 5, asymmetry: rng.Range(-0.13, 0.13));

                if (j % 4 == 1)
                    catkinNodes.Add((at, dir * 0.5 + Vec3.Up * 0.5));
            }

            // Stem terminal cap
            for (int k = 0; k < (juvenile ? 2 : 3); k++)
            {
                double leafAz = az + k * 2.399963 + 0.5 + rng.Range(-0.12, 0.12);
                var dir = new Vec3(Math.Cos(leafAz) * 0.50, 0.62, Math.Sin(leafAz) * 0.50).Normalized();
                double len = rng.Range(0.046, 0.068) * heightScale;
                var tip = stemTip + dir * len + Vec3.Up * (len * 0.06);
                var sideHint = Vec3.Up.Cross(dir);
                LeafUpward(part, stemTip, tip, sideHint, len * 0.34, leafBase, leafTip, camber: len * 0.16, longitudinal: 5, asymmetry: rng.Range(-0.13, 0.13));
            }
        }

        // Delicate pearl-silk catkins standing proudly above undulating cushion
        int catkinCount = juvenile ? 3 : 16 + rng.NextInt(5);
        for (int k = 0; k < catkinCount && catkinNodes.Count > 0; k++)
        {
            var (spot, outDir) = catkinNodes[(k * 2) % catkinNodes.Count];
            PussyWillowCatkin(spot, outDir);
        }

        NewShrubsFinish(m, part, radius, height);
    }
}
