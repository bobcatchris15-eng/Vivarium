using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New trees set A: Moonbark, Shiverleaf, Witchberry (high-effort botanical + whimsical overhaul).
///
/// 1. Moonbark:
///    Natural Silver Birch clump (Betula pendula)
///    - Sinuous clump of 2-3 gracefully leaning, bowed trunks with flared buttressed bases
///    - Continuous pale silver-cream bark with horizontal diamond lenticel bands
///    - Exfoliating paper bark ribbons showing warm glowing copper-cinnamon & burnt-sienna inner bark (UV2 = (1.20, 0.80))
///    - Wide-spreading scaffold boughs with flared branch collars branching into weeping delicate terminal withes
///    - Cascading tiers of fluttering serrated birch leaves with guaranteed +Upward normals (sunlit emerald-lime)
///    - Pendulous golden-ochre catkins hanging in pairs/trios from weeping outer twigs (UV2 = (1.30, 0.75))
///    - UV2 = (2.0, 0.0) on all woody tubes for authentic procedural bark shading
///
/// 2. Shiverleaf:
///    Clonal Quaking Aspen (Populus tremuloides)
///    - Clonal trio of sinuous slender trunks from a shared rootstock with smooth creamy grey-green bark (0.72, 0.75, 0.66)
///    - Dark charcoal basal furrowing in the lower 18% and dark horizontal chevron eye scars at branch crotches
///    - Smooth natural branch crotches with flared branch collars (UV2 = (2.0, 0.0))
///    - Deep, billowing crown of broad cordate-orbicular trembling leaves with distinct two-tone contrast:
///      sunlit chartreuse-emerald upper blades and ~28% twisted silvery-celadon/mint undersides
///    - Fluttering in 3D across scaffold boughs and outer twig network
///    - Drooping silky pearl-rose aspen catkins hanging from outer twig nodes (UV2 = (1.30, 0.75))
///
/// 3. Witchberry:
///    Ancient Fairytale Rowan / Mountain-Ash (Sorbus aucuparia)
///    - Sinuous, muscular trunk with natural crook and lean, fluted pewter-plum bole (0.32, 0.27, 0.29) with bronze lenticels
///    - Wide-spreading arching boughs with flared branch collars branching smoothly into secondary and terminal shoots
///    - Continuous bark color throughout and UV2 = (2.0, 0.0) on all woody geometry
///    - Odd-pinnate compound rowan leaves with crimson-tinted rachis and 9-11 lanceolate serrated leaflets (+Up normals)
///      clustered at branch ends in full 3D sprays, warming to coppery-scarlet/gold on outer autumn-kissed fronds
///    - Abundant, vibrant scarlet-vermilion berry corymbs (UV2 = (1.38, 0.90)) nestled directly among terminal foliage sprays
/// </summary>
public static partial class OrganismMeshes
{
    // Fixed bark palettes
    private static readonly double[] TaMoonbarkBarkWhite = { 0.88, 0.86, 0.82 };
    private static readonly double[] TaMoonbarkBarkGrey  = { 0.62, 0.60, 0.56 };
    private static readonly double[] TaMoonbarkBarkDark  = { 0.12, 0.11, 0.10 };
    private static readonly double[] TaMoonbarkTwig      = { 0.72, 0.70, 0.65 };

    private static readonly double[] TaShiverleafBarkPale = { 0.72, 0.75, 0.66 };
    private static readonly double[] TaShiverleafBarkMid  = { 0.48, 0.52, 0.44 };
    private static readonly double[] TaShiverleafBarkDark = { 0.13, 0.15, 0.12 };
    private static readonly double[] TaShiverleafTwig     = { 0.60, 0.64, 0.56 };

    private static readonly double[] TaWitchberryBarkMid  = { 0.32, 0.27, 0.29 };
    private static readonly double[] TaWitchberryBarkPale = { 0.46, 0.42, 0.40 };
    private static readonly double[] TaWitchberryBarkDark = { 0.12, 0.10, 0.11 };
    private static readonly double[] TaWitchberryTwig     = { 0.28, 0.24, 0.22 };
    private static readonly double[] TaScarlet            = { 0.96, 0.08, 0.04 };

    private static Vec3 TaBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        return p0 * (u * u * u) + c0 * (3.0 * u * u * t) + c1 * (3.0 * u * t * t) + p1 * (t * t * t);
    }

    private static Vec3 TaBezierTangent(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double u = 1.0 - t;
        var d = (c0 - p0) * (3.0 * u * u) + (c1 - c0) * (6.0 * u * t) + (p1 - c1) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    private static Vec3[] TaSampleBezier(Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, int stations)
    {
        var pts = new Vec3[stations];
        for (int i = 0; i < stations; i++)
            pts[i] = TaBezier(p0, c0, c1, p1, i / (double)(stations - 1));
        return pts;
    }

    private static Vec3 TaRadial(double az) => new Vec3(Math.Cos(az), 0, Math.Sin(az));

    private static Vec3 TaNorm(Vec3 v, Vec3 fallback) =>
        v.LengthSq > 1e-10 ? v.Normalized() : fallback;

    private static double[] TaBright(double[] c) => Primitives.Scale(c, 1.4);

    private static double[] TaMoonbarkTrunkCol(double t, double v)
    {
        var baseCol = Primitives.Mix(TaMoonbarkBarkGrey, TaMoonbarkBarkWhite, 0.70 + 0.30 * t);
        // Horizontal diamond lenticel bands near base and at branch nodes
        double wave = Math.Sin(t * 32.0 + Math.Sin(v * 12.566) * 0.4);
        double diamond = Math.Pow(Math.Max(0.0, wave), 8.0) * (0.4 + 0.6 * Math.Pow(Math.Max(0.0, Math.Cos(v * 18.849)), 2.0));
        var col = Primitives.Mix(baseCol, TaMoonbarkBarkDark, diamond * 0.90);
        double patch = Math.Sin(t * 9.0 - v * 12.566 + 1.7) * 0.04;
        return Primitives.Scale(col, 1.0 + patch);
    }

    private static double[] TaShiverleafTrunkCol(double t, double v)
    {
        var baseCol = Primitives.Mix(TaShiverleafBarkMid, TaShiverleafBarkPale, 0.60 + 0.40 * t);
        // Dark charcoal basal furrowing at the bottom 18%
        if (t < 0.18)
        {
            double f = Math.Pow(1.0 - t / 0.18, 1.8);
            double furrow = Math.Pow(0.5 + 0.5 * Math.Sin(v * 25.132), 3.0);
            baseCol = Primitives.Mix(baseCol, TaShiverleafBarkDark, f * (0.65 + 0.35 * furrow));
        }
        // Dark horizontal chevron eye-shaped branch scars
        double scarWave = Math.Sin(t * 22.0 + 1.2);
        double eye = Math.Pow(Math.Max(0.0, scarWave), 10.0) * Math.Pow(Math.Max(0.0, Math.Cos(v * 6.283)), 3.0);
        var col = Primitives.Mix(baseCol, TaShiverleafBarkDark, eye * 0.88);
        return col;
    }

    private static double[] TaWitchberryTrunkCol(double t, double v)
    {
        var baseCol = Primitives.Mix(TaWitchberryBarkDark, TaWitchberryBarkMid, 0.50 + 0.50 * t);
        // Fluted vertical ribs
        double fiss = Math.Pow(0.5 + 0.5 * Math.Sin(v * 31.416 + Math.Sin(t * 5.0) * 1.5), 2.2);
        var col = Primitives.Mix(baseCol, TaWitchberryBarkDark, fiss * 0.70);
        // Bronze-grey lenticels
        double lenticel = Math.Pow(Math.Max(0.0, Math.Sin(t * 36.0 + v * 3.1415)), 10.0);
        col = Primitives.Mix(col, TaWitchberryBarkPale, lenticel * 0.40);
        return col;
    }

    private static void TaPinShell(MeshData part, int firstVertex) =>
        part.EssentialSpans.Add((firstVertex, part.VertexCount - firstVertex));

    // -------------------------------------------------------------------------
    // Lightweight leaf with strictly upward-pointing normals (+Up).
    // Eliminates inverted normals and ensures lush lighting from above.
    // -------------------------------------------------------------------------
    private static void TaLightLeaf(MeshData part, Vec3 root, Vec3 tip, Vec3 sideHint,
        double halfWidth, double[] baseColIn, double[] tipColIn, int detail, double shoulder, double serrate,
        bool flipUnderside = false, double[]? undersideCol = null, int stationOverride = 0, int colOverride = 0)
    {
        var baseCol = TaBright(baseColIn);
        var tipCol = TaBright(tipColIn);
        if (flipUnderside && undersideCol != null)
        {
            baseCol = TaBright(undersideCol);
            tipCol = TaBright(Primitives.Mix(undersideCol, new[] { 0.88, 0.95, 0.88 }, 0.25));
        }

        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-8 || halfWidth <= 1e-6) return;
        var forward = axis / length;

        // Side vector orthogonal to forward and Vec3.Up
        var baseSide = Vec3.Up.Cross(forward);
        if (baseSide.LengthSq < 1e-8) baseSide = forward.Cross(new Vec3(1, 0, 0));
        baseSide = baseSide.Normalized();

        if (sideHint.LengthSq > 1e-6)
        {
            var projHint = (sideHint - forward * sideHint.Dot(forward)).Normalized();
            if (projHint.LengthSq > 1e-6 && projHint.Dot(baseSide) < 0.0)
                baseSide = -baseSide;
        }

        // Up vector orthogonal to forward and baseSide, guaranteed Y > 0
        var baseUp = forward.Cross(baseSide).Normalized();
        if (baseUp.Y < 0.0)
        {
            baseUp = -baseUp;
            baseSide = -baseSide;
        }

        int stations = stationOverride > 0 ? stationOverride : (detail switch { 2 => 4, 1 => 5, _ => 6 });
        int cols = colOverride > 0 ? colOverride : (detail switch { 2 => 3, 1 => 3, _ => 4 });
        if (part.Lod is { } lod)
        {
            stations = lod.Segments(stations, 3);
            cols = Math.Max(2, (int)Math.Round(cols * lod.Blade));
        }

        Vec3 At(double t, double x)
        {
            double env = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, shoulder))), 0.8);
            double tooth = serrate > 0.0 ? 1.0 + serrate * 0.18 * Math.Sin(t * 38.0) * env : 1.0;
            double w = halfWidth * env * tooth;
            double lift = length * 0.10 * Math.Sin(Math.PI * t) * (1.0 - t * 0.5)
                          + halfWidth * env * 0.14 * (1.0 - x * x);
            double droop = -length * 0.05 * t * t;
            return root + axis * t + baseSide * (x * w) + baseUp * (lift + droop);
        }

        Vec3 NormalAt(double t, double x)
        {
            const double d = 0.01;
            var a = At(Math.Min(1.0, t + d), x) - At(Math.Max(0.0, t - d), x);
            var b = At(t, Math.Min(1.0, x + d)) - At(t, Math.Max(-1.0, x - d));
            var n = a.Cross(b).Normalized();
            if (n.Dot(baseUp) < 0.0) n = -n;
            if (n.Y < 0.20) n = (n + Vec3.Up * (0.35 + Math.Max(0.0, -n.Y))).Normalized();
            if (n.Y <= 0.01) n = (n + Vec3.Up * (0.25 - n.Y)).Normalized();
            return n;
        }

        int leafVertex = part.VertexCount;
        int leafIndex = part.TriangleCount * 3;
        int midRows = Math.Max(1, stations - 2);
        var grid = new int[midRows, cols + 1];

        int rootIdx = part.AddVertex(At(0.0, 0.0), NormalAt(0.0, 0.0), baseCol, 1.0, 0.0, 0.5, 0.0, 1.0);
        for (int r = 0; r < midRows; r++)
        {
            double t = (r + 1) / (double)(stations - 1);
            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var col = Primitives.Mix(baseCol, tipCol, t);
                grid[r, c] = part.AddVertex(At(t, x), NormalAt(t, x), col, 1.0, t, (x + 1.0) * 0.5, 0.0, 1.0);
            }
        }
        int tipIdx = part.AddVertex(At(1.0, 0.0), NormalAt(1.0, 0.0), tipCol, 1.0, 1.0, 0.5, 0.0, 1.0);

        for (int c = 0; c < cols; c++)
            Primitives.TriangleFacing(part, rootIdx, grid[0, c], grid[0, c + 1], baseUp);
        for (int r = 0; r < midRows - 1; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a0 = grid[r, c], b0 = grid[r, c + 1], d0 = grid[r + 1, c], e0 = grid[r + 1, c + 1];
                Primitives.TriangleFacing(part, a0, d0, b0, baseUp);
                Primitives.TriangleFacing(part, b0, d0, e0, baseUp);
            }
        }
        for (int c = 0; c < cols; c++)
            Primitives.TriangleFacing(part, grid[midRows - 1, c], tipIdx, grid[midRows - 1, c + 1], baseUp);

        part.RecordLeaf(leafVertex, leafIndex, root, length, volumetric: false);
        TaPinShell(part, leafVertex);
    }

    // -------------------------------------------------------------------------
    // Basal root buttress flares: muscular root ribs running down into soil.
    // Shaded with UV2 = (2.0, 0.0) for authentic procedural bark shading.
    // -------------------------------------------------------------------------
    private static void TaRootButtresses(MeshData part, Rng rng, Vec3 center, double rBase, Func<double, double, double[]> colFunc, int detail)
    {
        int roots = detail == 2 ? 3 : 5;
        for (int r = 0; r < roots; r++)
        {
            double az = r * (Math.Tau / roots) + rng.Range(-0.15, 0.15);
            var rad = TaRadial(az);
            var p0 = center + rad * (rBase * 0.70) + Vec3.Up * (rBase * 0.90);
            var p1 = center + rad * (rBase * 1.30) + Vec3.Up * (rBase * 0.28);
            var p2 = center + rad * (rBase * 1.90) - Vec3.Up * 0.02;
            int sides = detail == 2 ? 4 : 5;
            Primitives.Tube(part, new[] { p0, p1, p2 }, new[] { rBase * 0.32, rBase * 0.20, rBase * 0.06 }, sides,
                (i, v) => (colFunc(0.04 * i, v), 1.0, (double)i / 2.0, v, 2.0, 0.0));
        }
    }

    // -------------------------------------------------------------------------
    // Moonbark exfoliating copper-amber bark ribbons (TaMoonbarkPeelStrip).
    // Displays warm glowing copper-cinnamon & burnt-sienna inner bark (UV2 = (1.20, 0.80)).
    // -------------------------------------------------------------------------
    private static void TaMoonbarkPeelStrip(MeshData part, Rng rng,
        Vec3 p0, Vec3 c0, Vec3 c1, Vec3 p1, double rBase, double rTop, int detail)
    {
        int stripStart = part.VertexCount;
        double az = rng.Range(0.0, Math.Tau);
        double twist = rng.Range(-0.35, 0.35);
        double tStart = rng.Range(0.05, 0.50);
        double lenFrac = rng.Range(0.10, 0.20);
        double halfW = rng.Range(0.012, 0.022);
        double curl = rng.Range(0.008, 0.020);
        int rows = detail switch { 2 => 4, 1 => 5, _ => 6 };
        const int cols = 3;
        if (part.Lod is { } lod) rows = lod.Segments(rows, 3);

        var grid = new int[rows, cols + 1];
        var copper = new[] { 0.78, 0.44, 0.22 }; // glowing copper-cinnamon
        var sienna = new[] { 0.58, 0.26, 0.14 }; // burnt-sienna

        for (int r = 0; r < rows; r++)
        {
            double s = r / (double)(rows - 1); // 0 = hinged, 1 = free scrolled tip
            double t = Math.Min(0.96, tStart + s * lenFrac);
            var centre = TaBezier(p0, c0, c1, p1, t);
            double trunkR = MathD.Lerp(rBase, rTop, Math.Pow(t, 0.85)) * (1.0 + 0.45 * Math.Exp(-t * 4.5));
            double a = az + s * twist;
            var radial = TaRadial(a);
            var side = TaRadial(a + Math.PI * 0.5);
            double lift = 0.0012 + curl * s * s;
            double edgeCurl = curl * 0.25 * s;
            var n = TaNorm(radial * 0.85 + Vec3.Up * 0.35, radial);

            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var pos = centre + radial * (trunkR + lift + edgeCurl * x * x) + side * (x * halfW * (1.0 - 0.20 * s));
                var innerTone = Primitives.Mix(copper, sienna, s);
                var col = Primitives.Mix(TaMoonbarkBarkWhite, innerTone, 0.15 + 0.80 * s + 0.15 * x * x);
                if (s < 0.22) col = Primitives.Mix(TaMoonbarkBarkDark, col, s / 0.22); // shadow at hinge
                grid[r, c] = part.AddVertex(pos, n, col, 1.0, t, (x + 1.0) * 0.5, 1.20, 0.80);
            }
        }
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a0 = grid[r, c], b0 = grid[r, c + 1], d0 = grid[r + 1, c], e0 = grid[r + 1, c + 1];
                part.AddTriangle(a0, d0, b0);
                part.AddTriangle(b0, d0, e0);
            }
        }
        part.EssentialSpans.Add((stripStart, part.VertexCount - stripStart));
    }

    // -------------------------------------------------------------------------
    // Pendulous golden-ochre catkin for Moonbark (UV2 = (1.30, 0.75)).
    // -------------------------------------------------------------------------
    private static void TaMoonbarkCatkin(MeshData part, Rng rng, Vec3 attach, Vec3 twigDir, double[] c2)
    {
        int catV0 = part.VertexCount;
        double hang = rng.Range(0.07, 0.11);
        var catCol = Primitives.Mix(c2, new[] { 0.82, 0.65, 0.24 }, 0.68);
        var drift = TaRadial(rng.Range(0.0, Math.Tau)) * rng.Range(0.004, 0.010);
        var p0 = attach;
        var p1 = attach + drift * 0.45 - Vec3.Up * (hang * 0.45);
        var p2 = attach + drift - Vec3.Up * hang;
        int catSides = part.Lod is { } lodCat ? lodCat.Sides(5) : 5;
        double catR = rng.Range(0.0035, 0.0048);
        Primitives.Tube(part, new[] { p0, p1, p2 }, new[] { catR * 0.75, catR, catR * 0.60 },
            catSides, (i, v) => (catCol, 1.0, (double)i / 2.0, v, 1.30, 0.75));
        for (int cs = 0; cs < 4; cs++)
        {
            double ct = (cs + 0.5) / 4.0;
            var cc = Vec3.Lerp(p1, p2, ct);
            double cr = catR * (1.15 - 0.25 * ct);
            Primitives.Ellipsoid(part, cc, new Vec3(cr, 0.006, cr), 2, 5,
                (u, v) => (catCol, 1.0, u, v, 1.30, 0.75));
        }
        NewFloraMarkOrgan(part, catV0, part.VertexCount - catV0);
        part.EssentialSpans.Add((catV0, part.VertexCount - catV0));
    }

    // -------------------------------------------------------------------------
    // Drooping silky pearl-rose catkin for Shiverleaf (UV2 = (1.30, 0.75)).
    // -------------------------------------------------------------------------
    private static void TaShiverleafCatkin(MeshData part, Rng rng, Vec3 attach, Vec3 twigDir, double[] c2)
    {
        int catV0 = part.VertexCount;
        double hang = rng.Range(0.05, 0.08);
        var catCol = Primitives.Mix(c2, new[] { 0.78, 0.72, 0.75 }, 0.62); // silky pearl-rose
        var drift = TaRadial(rng.Range(0.0, Math.Tau)) * rng.Range(0.003, 0.008);
        var p0 = attach;
        var p1 = attach + drift * 0.45 - Vec3.Up * (hang * 0.45);
        var p2 = attach + drift - Vec3.Up * hang;
        int catSides = part.Lod is { } lodCat ? lodCat.Sides(5) : 5;
        double catR = rng.Range(0.0030, 0.0042);
        Primitives.Tube(part, new[] { p0, p1, p2 }, new[] { catR * 0.75, catR, catR * 0.55 },
            catSides, (i, v) => (catCol, 1.0, (double)i / 2.0, v, 1.30, 0.75));
        for (int cs = 0; cs < 3; cs++)
        {
            double ct = (cs + 0.5) / 3.0;
            var cc = Vec3.Lerp(p1, p2, ct);
            double cr = catR * (1.10 - 0.20 * ct);
            Primitives.Ellipsoid(part, cc, new Vec3(cr, 0.005, cr), 2, 5,
                (u, v) => (catCol, 1.0, u, v, 1.30, 0.75));
        }
        NewFloraMarkOrgan(part, catV0, part.VertexCount - catV0);
        part.EssentialSpans.Add((catV0, part.VertexCount - catV0));
    }

    // -------------------------------------------------------------------------
    // Moonbark weeping spray: delicate birch twig spray with airy deltoid leaves
    // distributed in cascading tiers (+Up normals, sunlit golden-emerald).
    // -------------------------------------------------------------------------
    private static void TaMoonbarkSpray(MeshData part, Rng rng, Vec3 node, Vec3 twigDir, double leafScale, double[] c1, double[] c2, int detail)
    {
        int spurStart = part.VertexCount;
        var dir = TaNorm(twigDir, Vec3.Up);
        var side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(dir).Normalized();
        if (up.Y < 0.0) { up = -up; side = -side; }

        double sprayLen = rng.Range(0.045, 0.075) * leafScale;
        var sprayEnd = node + dir * sprayLen - Vec3.Up * (sprayLen * 0.15);
        int spurSides = part.Lod is { } lodSp ? lodSp.Sides(5) : 5;
        Primitives.Tube(part, new[] { node, sprayEnd }, new[] { 0.0020, 0.0009 }, spurSides,
            (i, v) => (TaMoonbarkTwig, 1.0, (double)i, v, 2.0, 0.0));

        int leaves = detail switch { 2 => 3, 1 => 4, _ => 5 };
        for (int j = 0; j < leaves; j++)
        {
            double tAlong = (j + 0.5) / leaves;
            var atPos = Vec3.Lerp(node, sprayEnd, tAlong);
            double sideSign = (j % 2 == 0) ? 1.0 : -1.0;
            double len = rng.Range(0.09, 0.13) * leafScale;
            double halfW = rng.Range(0.034, 0.048);

            var lDir = TaNorm(side * (sideSign * 0.78) + dir * 0.45 + Vec3.Up * rng.Range(0.15, 0.42), Vec3.Up);
            var root = atPos + side * (sideSign * 0.003);
            var tip = root + lDir * len;

            var leafBase = Primitives.Scale(Primitives.Mix(c1, new[] { 0.32, 0.64, 0.22 }, 0.60), rng.Range(1.04, 1.18));
            var leafTip  = Primitives.Scale(Primitives.Mix(c1, new[] { 0.48, 0.76, 0.25 }, 0.65), rng.Range(1.08, 1.22));

            TaLightLeaf(part, root, tip, Vec3.Up, halfW, leafBase, leafTip, detail,
                shoulder: 0.42, serrate: 0.20, stationOverride: 4, colOverride: 3);
        }
        TaPinShell(part, spurStart);
    }

    // -------------------------------------------------------------------------
    // Witchberry odd-pinnate leaf: crimson rachis with 9-11 lanceolate serrated leaflets.
    // -------------------------------------------------------------------------
    private static void TaWitchberryLeaf(MeshData part, Rng rng, Vec3 node, Vec3 leafDirIn, double leafLen, double leafScale, double[] c1, int detail)
    {
        int leafStart = part.VertexCount;
        var dir = TaNorm(leafDirIn, Vec3.Up);
        var end = node + dir * leafLen - Vec3.Up * (leafLen * 0.14);
        var c0 = node + TaNorm(dir * 0.75 + Vec3.Up * 0.35, Vec3.Up) * (leafLen * 0.35);
        var c1p = end - TaNorm(dir * 0.92 - Vec3.Up * 0.15, Vec3.Up) * (leafLen * 0.34);
        int stations = detail == 2 ? 3 : 4;
        var path = TaSampleBezier(node, c0, c1p, end, stations);
        var radii = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            double t = i / (double)(stations - 1);
            radii[i] = MathD.Lerp(0.0022, 0.0009, t);
        }
        var rachisCol = new[] { 0.42, 0.18, 0.16 }; // crimson-tinted rachis
        int rachisSides = part.Lod is { } lodR ? lodR.Sides(5) : 5;
        int stCount = stations;
        Primitives.Tube(part, path, radii, rachisSides,
            (i, v) => { double t = stCount <= 1 ? 0 : i / (double)(stCount - 1); return (rachisCol, 1.0, t, v, 2.0, 0.0); });

        var side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(dir).Normalized();
        if (up.Y < 0.0) { up = -up; side = -side; }

        int pairs = detail == 0 ? 4 : 3; // 4 pairs (8 leaflets) + 1 terminal = 9 leaflets
        bool autumnTouch = rng.NextDouble() < 0.14; // ~14% autumn golden-scarlet fronds
        var leafBase = autumnTouch
            ? new[] { 0.68, 0.30, 0.14 }
            : Primitives.Scale(Primitives.Mix(c1, new[] { 0.16, 0.36, 0.14 }, 0.40), 0.88);
        var leafTip = autumnTouch
            ? new[] { 0.85, 0.42, 0.16 }
            : Primitives.Scale(Primitives.Mix(c1, new[] { 0.24, 0.48, 0.20 }, 0.45), 0.94);

        for (int p = 0; p < pairs; p++)
        {
            double tPair = 0.22 + 0.70 * (p + 0.5) / pairs;
            var pNode = TaBezier(node, c0, c1p, end, tPair);
            var tNode = TaBezierTangent(node, c0, c1p, end, tPair);
            double midFactor = Math.Sin(Math.PI * (0.20 + 0.80 * (p + 0.5) / pairs));
            double leafletLen = Math.Clamp((0.062 + 0.024 * midFactor) * leafScale * rng.Range(0.92, 1.08), 0.050, 0.088);
            double halfW = leafletLen * 0.38;
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var lDir = TaNorm(side * (sg * 0.85) + tNode * 0.45 + up * 0.20, Vec3.Up);
                var root = pNode + side * (sg * 0.0022);
                var tip = root + lDir * leafletLen;
                var bCol = Primitives.Scale(leafBase, rng.Range(0.94, 1.06));
                var tCol = Primitives.Scale(leafTip, rng.Range(0.95, 1.05));
                TaLightLeaf(part, root, tip, up, halfW, bCol, tCol, detail, shoulder: 0.50, serrate: 0.35,
                    stationOverride: 4, colOverride: 3);
            }
        }
        // Terminal leaflet
        var termDir = TaBezierTangent(node, c0, c1p, end, 1.0);
        double termLen = Math.Clamp(0.072 * leafScale * rng.Range(0.92, 1.08), 0.055, 0.088);
        TaLightLeaf(part, end, end + termDir * termLen, up, termLen * 0.38,
            leafBase, leafTip, detail, shoulder: 0.50, serrate: 0.35,
            stationOverride: 4, colOverride: 3);
        TaPinShell(part, leafStart);
    }

    // -------------------------------------------------------------------------
    // Witchberry 3D terminal spray: a full, lush rosette of 3-4 odd-pinnate fronds
    // clustered at the shoot tip with a heavy pendant scarlet corymb nestled within.
    // -------------------------------------------------------------------------
    private static void TaWitchberryTerminalSpray(MeshData part, Rng rng, Vec3 node, Vec3 shootDir,
        double leafScale, double[] c1, double[] scarlet, int detail, bool coarse)
    {
        var fwd = TaNorm(shootDir, Vec3.Up);
        var side = fwd.Cross(Vec3.Up);
        if (side.LengthSq < 1e-6) side = fwd.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(fwd).Normalized();
        if (up.Y < 0.0) { up = -up; side = -side; }

        int frondsInSpray = detail switch { 2 => 2, 1 => 3, _ => 4 };
        for (int fi = 0; fi < frondsInSpray; fi++)
        {
            double ang = (fi - (frondsInSpray - 1) * 0.5) * 1.02 + rng.Range(-0.24, 0.24);
            double pitch = rng.Range(0.18, 0.56);
            var frondDir = TaNorm(fwd * Math.Cos(pitch) + (side * Math.Sin(ang) + up * Math.Cos(ang)) * Math.Sin(pitch), Vec3.Up);
            double frondLen = rng.Range(0.24, 0.32) * leafScale;
            TaWitchberryLeaf(part, rng, node, frondDir, frondLen, leafScale, c1, detail);
        }

        // Nestled vibrant scarlet berry corymb
        var corymbDir = TaNorm(fwd * 0.48 - Vec3.Up * 0.86, Vec3.Up);
        TaWitchberryCorymb(part, rng, node, corymbDir, scarlet, coarse);
    }

    // -------------------------------------------------------------------------
    // Witchberry pendant corymb: branching crimson pedicels with 10-14 plump scarlet berries
    // (r ~ 0.018-0.024m, UV2 = (1.38, 0.90)) with tiny dark 5-point star calyces.
    // -------------------------------------------------------------------------
    private static void TaWitchberryCorymb(MeshData part, Rng rng, Vec3 attach, Vec3 outDir, double[] scarlet, bool coarse)
    {
        _ = coarse;
        int corymbStart = part.VertexCount;
        var outN = TaNorm(outDir, TaRadial(rng.Range(0.0, Math.Tau)));
        var hub = attach + outN * 0.060 - Vec3.Up * 0.010;
        var stalkMid = attach + outN * 0.030 - Vec3.Up * 0.004;
        int stalkSides = part.Lod is { } lodS ? lodS.Sides(4) : 4;
        var pedicelCol = new[] { 0.45, 0.16, 0.14 }; // crimson pedicel
        Primitives.Tube(part, new[] { attach, stalkMid, hub }, new[] { 0.0020, 0.0014, 0.0010 }, stalkSides,
            (i, v) => (pedicelCol, 1.0, i / 2.0, v, 2.0, 0.0));

        int berries = 10 + rng.NextInt(4); // 10-13 plump berries
        var rubyOrange = new[] { 0.92, 0.26, 0.06 };
        int brSides = part.Lod is { } lodB ? lodB.Sides(4) : 4;

        for (int b = 0; b < berries; b++)
        {
            double az = rng.Range(0.0, Math.Tau);
            var flat = TaRadial(az);
            var dir = TaNorm(outN * 0.25 + flat * 0.50 - Vec3.Up * rng.Range(0.65, 0.95), Vec3.Up);
            double reach = rng.Range(0.015, 0.028);
            var berryBase = hub + dir * reach;
            Primitives.Tube(part, new[] { hub, berryBase },
                new[] { 0.0008, 0.0004 }, brSides,
                (i, v) => (pedicelCol, 1.0, (double)i, v, 2.0, 0.0));

            double berryR = rng.Range(0.011, 0.016); // compact berries integrated with leafy terminal sprays
            var centre = berryBase + dir * (berryR * 0.60);
            int berryStart = part.VertexCount;
            var berryTone = Primitives.Mix(scarlet, rubyOrange, rng.Range(0.0, 0.35));
            var bDark = Primitives.Scale(berryTone, 0.62);
            var bLight = Primitives.Mix(berryTone, new[] { 1.0, 0.82, 0.72 }, 0.40);

            Primitives.Ellipsoid(part, centre, new Vec3(berryR, berryR * 0.92, berryR), 2, 5,
                (u, v) =>
                {
                    double hi = Math.Pow(Math.Max(0.0, 1.0 - u), 1.5) * 0.45 + v * 0.10;
                    return (Primitives.Mix(bDark, bLight, hi), 1.0, u, v, 1.38, 0.90);
                }, pitch: rng.Range(-0.2, 0.2));

            // Tiny 5-point star calyx at berry apex
            var calyxCenter = centre + dir * (berryR * 0.92);
            var calyxSide = dir.Cross(Vec3.Up);
            if (calyxSide.LengthSq < 1e-6) calyxSide = new Vec3(1, 0, 0);
            calyxSide = calyxSide.Normalized();
            var calyxUp = calyxSide.Cross(dir).Normalized();
            var calyxCol = new[] { 0.10, 0.06, 0.05 };
            int calyxC = part.AddVertex(calyxCenter, dir, calyxCol, 1.0, 0.5, 0.5, 1.38, 0.90);
            int prevStar = -1, firstStar = -1;
            for (int cp = 0; cp < 5; cp++)
            {
                double ca = cp * (Math.Tau / 5.0);
                var starPt = calyxCenter + (calyxSide * Math.Cos(ca) + calyxUp * Math.Sin(ca)) * (berryR * 0.28);
                int sIdx = part.AddVertex(starPt, dir, calyxCol, 1.0, 0.5, 0.5, 1.38, 0.90);
                if (cp == 0) firstStar = sIdx;
                else part.AddTriangle(calyxC, prevStar, sIdx);
                prevStar = sIdx;
            }
            if (firstStar != -1 && prevStar != -1)
                part.AddTriangle(calyxC, prevStar, firstStar);

            NewFloraMarkOrgan(part, berryStart, part.VertexCount - berryStart);
        }
        part.EssentialSpans.Add((corymbStart, part.VertexCount - corymbStart));
    }

    // -------------------------------------------------------------------------
    // Shiverleaf trembling leaf: ribbon petiole + broad cordate-orbicular blade.
    // ~28% of leaves twisted to show pale silvery-celadon underside.
    // -------------------------------------------------------------------------
    private static void TaShiverleafLeaf(MeshData part, Rng rng, Vec3 node, Vec3 leafDirIn, double leafScale, double[] c1, double[] c2, int detail)
    {
        int petioleStart = part.VertexCount;
        double petioleLen = rng.Range(0.065, 0.095) * leafScale;
        var dir = TaNorm(leafDirIn, Vec3.Up);
        var leafRoot = node + dir * petioleLen - Vec3.Up * (petioleLen * 0.20);
        var petioleMid = node + dir * (petioleLen * 0.5) - Vec3.Up * (petioleLen * 0.04);
        var petioleCol = Primitives.Scale(Primitives.Mix(c1, new[] { 0.86, 0.90, 0.80 }, 0.55), rng.Range(0.98, 1.08));

        var petSide = dir.Cross(Vec3.Up);
        if (petSide.LengthSq < 1e-8) petSide = dir.Cross(new Vec3(1, 0, 0));
        petSide = petSide.Normalized();

        bool flipUnderside = rng.NextDouble() < 0.28; // ~28% twisted leaves for two-tone shimmer
        if (flipUnderside)
        {
            // Rotate petiole cross-section to catch glancing light
            petSide = (petSide * 0.35 + Vec3.Up * 0.93).Normalized();
        }

        var petStations = new[] { node, petioleMid, leafRoot };
        var petWidths = new[] { 0.0018, 0.0012, 0.0008 };
        var petGrid = new int[petStations.Length, 2];
        for (int pi = 0; pi < petStations.Length; pi++)
        {
            double pw = petWidths[pi];
            var pn = TaNorm(petSide.Cross(dir), Vec3.Up);
            petGrid[pi, 0] = part.AddVertex(petStations[pi] - petSide * pw, pn, petioleCol, 1.0, pi / 2.0, 0.0, 2.0, 0.0);
            petGrid[pi, 1] = part.AddVertex(petStations[pi] + petSide * pw, pn, petioleCol, 1.0, pi / 2.0, 1.0, 2.0, 0.0);
        }
        for (int pi = 0; pi < petStations.Length - 1; pi++)
        {
            int a = petGrid[pi, 0], b = petGrid[pi, 1], d = petGrid[pi + 1, 0], e = petGrid[pi + 1, 1];
            var fn = TaNorm(petSide.Cross(dir), Vec3.Up);
            Primitives.TriangleFacing(part, a, d, b, fn);
            Primitives.TriangleFacing(part, b, d, e, fn);
        }

        double len = rng.Range(0.085, 0.125) * leafScale;
        double halfW = len * rng.Range(0.38, 0.46);
        var flat = TaRadial(rng.Range(0.0, Math.Tau));
        var bladeDir = TaNorm(dir * 0.32 + flat * 0.82 + Vec3.Up * rng.Range(-0.12, 0.24), Vec3.Up);
        var tip = leafRoot + bladeDir * len;

        var leafBase = Primitives.Scale(Primitives.Mix(c1, new[] { 0.40, 0.70, 0.25 }, 0.55), rng.Range(1.02, 1.16));
        var leafTip  = Primitives.Scale(Primitives.Mix(c1, new[] { 0.50, 0.78, 0.28 }, 0.60), rng.Range(1.06, 1.20));
        var underCol = new[] { 0.68, 0.82, 0.72 }; // pale silvery-celadon underside

        TaLightLeaf(part, leafRoot, tip, petSide, halfW, leafBase, leafTip, detail,
            shoulder: 0.85, serrate: 0.12, flipUnderside: flipUnderside, undersideCol: underCol);
        TaPinShell(part, petioleStart);
    }

    // =========================================================================
    // 1. MOONBARK (Natural Silver Birch Clump - Betula pendula)
    // Sinuous leaning clump, copper peeling scrolls, pendulous weeping withes.
    // =========================================================================
    private static double TaLocalR(double[] r, double t)
    {
        double f = MathD.Clamp01(t) * (r.Length - 1);
        int i = Math.Min((int)Math.Floor(f), r.Length - 2);
        return MathD.Lerp(r[i], r[i + 1], f - i);
    }

    private static void Moonbark(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        int detail = part.FloraDetailLevel ?? 0;
        double H = height > 0.05 ? height : 3.40;
        double R = radius > 0.01 ? radius : 1.10;
        double hScale = juvenile ? 0.62 : 1.0;
        double leafScale = juvenile ? 0.78 : 1.0;

        // Basal root buttress flares
        TaRootButtresses(part, rng, Vec3.Zero, 0.20 * hScale, TaMoonbarkTrunkCol, detail);

        // Clump of 2-3 gracefully leaning, bowed silver-white birch trunks
        int numTrunks = juvenile ? 2 : 3;
        double baseAz = rng.Range(0.0, Math.Tau);

        var scaffoldBoughs = new List<(Vec3 Root, Vec3 MotherTan, double Az, double BaseR)>();
        var weepingTwigs = new List<(Vec3 Root, Vec3 C0, Vec3 C1, Vec3 End, double Az, double BaseR)>();

        for (int tIdx = 0; tIdx < numTrunks; tIdx++)
        {
            bool isDominant = (tIdx == 0);
            double trunkAz = baseAz + tIdx * (Math.Tau / numTrunks) + rng.Range(-0.18, 0.18);
            var trunkDir = TaRadial(trunkAz);

            // Natural bowing lean for each trunk (graceful open habit, not a tight broom!)
            double leanDist = isDominant ? rng.Range(0.32, 0.45) : rng.Range(0.48, 0.65);
            double trunkH = H * (isDominant ? 0.52 : rng.Range(0.38, 0.45)) * hScale;

            var tP0 = isDominant ? Vec3.Zero : trunkDir * rng.Range(0.08, 0.16);
            var tP1 = tP0 + trunkDir * (leanDist * 0.70 * hScale) + Vec3.Up * trunkH;
            // S-curve: bows outward gracefully, then sweeps upper section back towards vertical
            var tC0 = tP0 + trunkDir * (leanDist * 0.92 * hScale) + Vec3.Up * (trunkH * 0.32);
            var tC1 = tP1 - trunkDir * (leanDist * 0.12 * hScale) - Vec3.Up * (trunkH * 0.22);

            int stations = detail == 2 ? 8 : (detail == 1 ? 14 : 22);
            var path = TaSampleBezier(tP0, tC0, tC1, tP1, stations);
            double rBase = (isDominant ? 0.13 : 0.095) * (juvenile ? 0.65 : 1.0);
            double rTop = rBase * 0.38;
            var radii = new double[stations];
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                double flare = 1.0 + 0.45 * Math.Exp(-t * 4.5);
                radii[i] = MathD.Lerp(rBase, rTop, Math.Pow(t, 0.85)) * flare;
            }

            // Fork radii (F1): r^2.5 conservation caps child radii; parent swells toward each fork
            int topBoughs = isDominant ? 2 : 1;
            double mbR1 = Math.Min(rBase * 0.48, Form.ForkJunction.ChildRadiusShare(TaLocalR(radii, 0.62), 0.4));
            double mbR2 = Math.Min(rBase * 0.44, Form.ForkJunction.ChildRadiusShare(TaLocalR(radii, 0.82), 0.4));
            double mbTopR = Math.Min(rTop, Form.ForkJunction.ChildRadius(radii[stations - 1], topBoughs));
            Form.ForkJunction.WidenParent(path, radii,
                (TaBezier(tP0, tC0, tC1, tP1, 0.62), mbR1), (TaBezier(tP0, tC0, tC1, tP1, 0.82), mbR2));

            int sides = part.Lod is { } lodT ? lodT.Sides(16) : 16;
            int trunkStart = part.VertexCount;
            int stCount = stations;
            Primitives.Tube(part, path, radii, sides,
                (i, v) => { double t = stCount <= 1 ? 0 : i / (double)(stCount - 1); return (TaMoonbarkTrunkCol(t, v), 1.0, t, v, 2.0, 0.0); });
            part.EssentialSpans.Add((trunkStart, part.VertexCount - trunkStart));

            // Exfoliating copper-amber curls on each trunk (UV2 = (1.20, 0.80))
            int peels = isDominant ? (juvenile ? 12 : 28) : (juvenile ? 8 : 16);
            for (int pI = 0; pI < peels; pI++)
                TaMoonbarkPeelStrip(part, rng, tP0, tC0, tC1, tP1, rBase, rTop, detail);

            // Staggered scaffold forks (staggered branch collars, natural branching)
            var tanTop = TaBezierTangent(tP0, tC0, tC1, tP1, 1.0);

            // Mid scaffold limb peeling off at 62% trunk height
            double forkFrac1 = 0.62;
            var forkPos1 = TaBezier(tP0, tC0, tC1, tP1, forkFrac1);
            var forkTan1 = TaBezierTangent(tP0, tC0, tC1, tP1, forkFrac1);
            double sideAz1 = trunkAz + rng.Range(0.50, 0.90) * (tIdx % 2 == 0 ? 1.0 : -1.0);
            scaffoldBoughs.Add((forkPos1, forkTan1, sideAz1, mbR1));

            // Upper scaffold limb peeling off at 82% trunk height
            double forkFrac2 = 0.82;
            var forkPos2 = TaBezier(tP0, tC0, tC1, tP1, forkFrac2);
            var forkTan2 = TaBezierTangent(tP0, tC0, tC1, tP1, forkFrac2);
            double sideAz2 = trunkAz - rng.Range(0.40, 0.80) * (tIdx % 2 == 0 ? 1.0 : -1.0);
            scaffoldBoughs.Add((forkPos2, forkTan2, sideAz2, mbR2));

            // Codominant upper scaffold boughs continuing the trunk top
            for (int tb = 0; tb < topBoughs; tb++)
            {
                double tbAz = trunkAz + (tb - (topBoughs - 1) * 0.5) * 0.80 + rng.Range(-0.15, 0.15);
                scaffoldBoughs.Add((tP1, tanTop, tbAz, mbTopR));
            }
        }

        // Ascending silver-white primary scaffold boughs with organic curved sways
        int boughCount = scaffoldBoughs.Count;
        for (int k = 0; k < boughCount; k++)
        {
            var (root, motherTan, az, limbBaseR) = scaffoldBoughs[k];
            var outward = TaRadial(az);
            var side = TaRadial(az + Math.PI * 0.5);

            double reach = R * rng.Range(0.68, 0.95) * (juvenile ? 0.75 : 1.0);
            double rise = H * rng.Range(0.25, 0.42) * (juvenile ? 0.80 : 1.0);
            var end = root + outward * reach + Vec3.Up * rise + side * rng.Range(-0.08, 0.08);
            double len = (end - root).Length;

            // Organic curved sway: sweeps outward, rises gracefully, then arches at extremity
            var limbC0 = root + TaNorm(motherTan * 0.40 + outward * 0.65 + Vec3.Up * 0.35, Vec3.Up) * (len * 0.38);
            var limbC1 = end - TaNorm(outward * 0.85 + Vec3.Up * 0.20, Vec3.Up) * (len * 0.32);

            int limbStations = detail == 2 ? 4 : (detail == 1 ? 6 : 7);
            var limbPath = TaSampleBezier(root, limbC0, limbC1, end, limbStations);
            double limbTipR = Math.Max(0.004, limbBaseR * 0.30);
            var limbRadii = new double[limbStations];
            for (int i = 0; i < limbStations; i++)
            {
                double st = i / (double)(limbStations - 1);
                double collar = 1.0 + 0.38 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.22), 2.0);
                limbRadii[i] = MathD.Lerp(limbBaseR, limbTipR, st) * collar;
            }

            int limbSides = part.Lod is { } lodL ? lodL.Sides(8) : 8;
            int limbStart = part.VertexCount;
            int lsCount = limbStations;
            Primitives.Tube(part, Form.ForkJunction.SinkStart(limbPath, limbBaseR), limbRadii, limbSides,
                (i, v) => { double t = lsCount <= 1 ? 0 : i / (double)(lsCount - 1); return (TaMoonbarkTrunkCol(0.55 + 0.45 * t, v), 1.0, t, v, 2.0, 0.0); });
            part.EssentialSpans.Add((limbStart, part.VertexCount - limbStart));

            // Sprays along scaffold limbs
            int limbSprays = juvenile ? 3 : 5;
            for (int s = 0; s < limbSprays; s++)
            {
                double f = 0.22 + 0.70 * (s + 0.5) / limbSprays;
                var node = TaBezier(root, limbC0, limbC1, end, f);
                var tan = TaBezierTangent(root, limbC0, limbC1, end, f);
                TaMoonbarkSpray(part, rng, node, tan, leafScale, c1, c2, detail);
            }
            TaMoonbarkSpray(part, rng, end, TaNorm(outward * 0.85 + Vec3.Up * 0.20, Vec3.Up), leafScale, c1, c2, detail);

            // Weeping secondary twigs hanging from scaffold boughs (pendulous withes)
            int twigsOnLimb = juvenile ? 3 : 6;
            for (int w = 0; w < twigsOnLimb; w++)
            {
                double twigF = 0.20 + 0.75 * (w + 0.5) / twigsOnLimb;
                var twRoot = TaBezier(root, limbC0, limbC1, end, twigF);
                var twTan = TaBezierTangent(root, limbC0, limbC1, end, twigF);
                double sideSign = (w % 2 == 0) ? 1.0 : -1.0;
                double twAz = az + sideSign * rng.Range(0.40, 0.80);

                var twOut = TaRadial(twAz);
                double twReach = rng.Range(0.42, 0.68) * (juvenile ? 0.75 : 1.0);
                var twEnd = twRoot + twOut * (twReach * 0.60) - Vec3.Up * rng.Range(0.18, 0.38); // graceful pendulous droop
                var twC0 = twRoot + TaNorm(twTan * 0.30 + twOut * 0.70, Vec3.Up) * (twReach * 0.38);
                var twC1 = twEnd - TaNorm(twOut * 0.55 - Vec3.Up * 0.75, Vec3.Up) * (twReach * 0.35);

                weepingTwigs.Add((twRoot, twC0, twC1, twEnd, twAz, limbBaseR * 0.44));
            }
        }

        // Loft weeping outer twigs with pendulous deltoid sprays and golden catkins
        int catkinBudget = juvenile ? 40 : 85;
        int catkinsBuilt = 0;

        foreach (var (twRoot, twC0, twC1, twEnd, twAz, twBaseR) in weepingTwigs)
        {
            var twOut = TaRadial(twAz);
            int twStations = detail == 2 ? 3 : 5;
            var twPath = TaSampleBezier(twRoot, twC0, twC1, twEnd, twStations);
            double twTipR = Math.Max(0.0022, twBaseR * 0.26);
            var twRadii = new double[twStations];
            for (int i = 0; i < twStations; i++)
            {
                double st = i / (double)(twStations - 1);
                double collar = 1.0 + 0.25 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.22), 2.0);
                twRadii[i] = MathD.Lerp(twBaseR, twTipR, st) * collar;
            }

            int twSides = part.Lod is { } lodW ? lodW.Sides(6) : 6;
            int twsCount = twStations;
            Primitives.Tube(part, Form.ForkJunction.SinkStart(twPath, twBaseR), twRadii, twSides,
                (i, v) => { double t = twsCount <= 1 ? 0 : i / (double)(twsCount - 1); return (Primitives.Mix(TaMoonbarkTwig, TaMoonbarkBarkDark, t * 0.25), 1.0, t, v, 2.0, 0.0); });

            // Cascading airy deltoid sprays along weeping twig
            int sprays = juvenile ? 4 : 6;
            for (int s = 0; s < sprays; s++)
            {
                double f = 0.12 + 0.80 * (s + 0.5) / sprays;
                var node = TaBezier(twRoot, twC0, twC1, twEnd, f);
                var tan = TaBezierTangent(twRoot, twC0, twC1, twEnd, f);
                TaMoonbarkSpray(part, rng, node, tan, leafScale, c1, c2, detail);
            }
            TaMoonbarkSpray(part, rng, twEnd, TaNorm(twEnd - twC1, Vec3.Up), leafScale, c1, c2, detail);

            // Pendulous golden-ochre catkins in pairs/trios from weeping twig tips and nodes
            if (!juvenile && catkinsBuilt < catkinBudget && rng.NextDouble() < 0.85)
            {
                int catkinCluster = 2 + rng.NextInt(2); // pairs or trios
                for (int cc = 0; cc < catkinCluster && catkinsBuilt < catkinBudget; cc++)
                {
                    double catF = (cc == 0) ? 1.0 : 0.72;
                    var catAttach = TaBezier(twRoot, twC0, twC1, twEnd, catF) + TaRadial(rng.Range(0, Math.Tau)) * 0.005;
                    TaMoonbarkCatkin(part, rng, catAttach, twOut, c2);
                    catkinsBuilt++;
                }
            }
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
    }

    // =========================================================================
    // 2. SHIVERLEAF (Clonal Quaking Aspen - Populus tremuloides)
    // Sinuous clonal trio, dark chevron eye scars, fluttering two-tone canopy.
    // =========================================================================
    private static void Shiverleaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        int detail = part.FloraDetailLevel ?? 0;
        double H = height > 0.05 ? height : 3.00;
        double R = radius > 0.01 ? radius : 1.00;
        double hScale = juvenile ? 0.62 : 1.0;
        double leafScale = juvenile ? 0.78 : 1.0;

        // Shared basal root buttresses
        TaRootButtresses(part, rng, Vec3.Zero, 0.20 * hScale, TaShiverleafTrunkCol, detail);

        // Clonal trio: 1 tall dominant trunk + 2 companion trunks from shared rootstock
        int numTrunks = juvenile ? 2 : 3;
        double baseAz = rng.Range(0.0, Math.Tau);

        var scaffoldBoughs = new List<(Vec3 Root, Vec3 C0, Vec3 C1, Vec3 End, double Az, double BaseR)>();
        var twigList = new List<(Vec3 Root, Vec3 C0, Vec3 C1, Vec3 End, double Az, double BaseR)>();

        for (int tIdx = 0; tIdx < numTrunks; tIdx++)
        {
            bool isDominant = (tIdx == 0);
            double trunkAz = baseAz + tIdx * (Math.Tau / numTrunks) + rng.Range(-0.15, 0.15);
            var trunkDir = TaRadial(trunkAz);

            // Sinuous trunks that rise gracefully to full tree stature
            double offsetDist = isDominant ? 0.0 : rng.Range(0.12, 0.22) * hScale;
            double leanDist = isDominant ? rng.Range(0.12, 0.22) : rng.Range(0.24, 0.38);
            double trunkH = H * (isDominant ? 0.58 : rng.Range(0.48, 0.54)) * hScale;

            var tP0 = trunkDir * offsetDist;
            // Sinuous sways: flare outward slightly at base, then curve gracefully upward
            var tP1 = tP0 + trunkDir * (leanDist * 0.70 * hScale) + Vec3.Up * trunkH;
            var tC0 = tP0 + trunkDir * (leanDist * 0.95 * hScale) + Vec3.Up * (trunkH * 0.30);
            var tC1 = tP1 - trunkDir * (leanDist * 0.15 * hScale) - Vec3.Up * (trunkH * 0.24);

            int stations = detail == 2 ? 8 : (detail == 1 ? 14 : 22);
            var path = TaSampleBezier(tP0, tC0, tC1, tP1, stations);
            double rBase = (isDominant ? 0.13 : 0.095) * (juvenile ? 0.65 : 1.0);
            double rTop = rBase * 0.38;
            var radii = new double[stations];
            for (int i = 0; i < stations; i++)
            {
                double t = i / (double)(stations - 1);
                double flare = 1.0 + 0.42 * Math.Exp(-t * 4.5);
                radii[i] = MathD.Lerp(rBase, rTop, Math.Pow(t, 0.85)) * flare;
            }

            // Fork radii (F1): r^2.5 conservation caps child radii; parent swells toward each fork
            int forksFromTrunk = isDominant ? (juvenile ? 3 : 5) : (juvenile ? 3 : 4);
            var slForks = new (Vec3, double)[forksFromTrunk];
            var slForkR = new double[forksFromTrunk];
            for (int f = 0; f < forksFromTrunk; f++)
            {
                double fFr = 0.42 + 0.54 * (f + 0.5) / forksFromTrunk;
                slForkR[f] = Math.Min(rBase * 0.44, Form.ForkJunction.ChildRadiusShare(TaLocalR(radii, fFr), 0.35));
                slForks[f] = (TaBezier(tP0, tC0, tC1, tP1, fFr), fFr < 0.90 ? slForkR[f] : 0.0);
            }
            Form.ForkJunction.WidenParent(path, radii, slForks);

            int sides = part.Lod is { } lodT ? lodT.Sides(16) : 16;
            int trunkStart = part.VertexCount;
            int stCount = stations;
            Primitives.Tube(part, path, radii, sides,
                (i, v) => { double t = stCount <= 1 ? 0 : i / (double)(stCount - 1); return (TaShiverleafTrunkCol(t, v), 1.0, t, v, 2.0, 0.0); });
            part.EssentialSpans.Add((trunkStart, part.VertexCount - trunkStart));

            // Staggered scaffold forks along the trunk with flared branch crotches
            for (int f = 0; f < forksFromTrunk; f++)
            {
                double fFrac = 0.42 + 0.54 * (f + 0.5) / forksFromTrunk;
                var fRoot = TaBezier(tP0, tC0, tC1, tP1, fFrac);
                var fTan = TaBezierTangent(tP0, tC0, tC1, tP1, fFrac);
                double fAz = trunkAz + ((f % 2 == 0 ? 1.0 : -1.0) * (0.50 + 0.35 * (f / (double)forksFromTrunk)));
                var fOut = TaRadial(fAz);

                double bReach = R * rng.Range(0.55, 0.80) * (juvenile ? 0.72 : 1.0);
                double bRise = H * rng.Range(0.25, 0.42) * (juvenile ? 0.78 : 1.0);
                var bEnd = fRoot + fOut * bReach + Vec3.Up * bRise;
                double bLen = (bEnd - fRoot).Length;

                // Smooth curving sweep (no rigid sticks!)
                var cB0 = fRoot + TaNorm(fTan * 0.35 + fOut * 0.65 + Vec3.Up * 0.40, Vec3.Up) * (bLen * 0.40);
                var cB1 = bEnd - TaNorm(fOut * 0.75 + Vec3.Up * 0.35, Vec3.Up) * (bLen * 0.34);

                scaffoldBoughs.Add((fRoot, cB0, cB1, bEnd, fAz, slForkR[f]));
            }
        }

        // Ascending creamy grey-green scaffold boughs
        int catkinBudget = juvenile ? 25 : 70;
        int catkinsBuilt = 0;

        foreach (var (root, cB0, cB1, end, az, limbBaseR) in scaffoldBoughs)
        {
            var outward = TaRadial(az);
            var side = TaRadial(az + Math.PI * 0.5);

            int leadStations = detail == 2 ? 4 : (detail == 1 ? 6 : 7);
            var leadPath = TaSampleBezier(root, cB0, cB1, end, leadStations);
            double leadTipR = Math.Max(0.0035, limbBaseR * 0.28);
            var leadRadii = new double[leadStations];
            for (int i = 0; i < leadStations; i++)
            {
                double st = i / (double)(leadStations - 1);
                double collar = 1.0 + 0.36 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.22), 2.0);
                leadRadii[i] = MathD.Lerp(limbBaseR, leadTipR, st) * collar;
            }

            int leadSides = part.Lod is { } lodL ? lodL.Sides(8) : 8;
            int leadStart = part.VertexCount;
            int lsCount = leadStations;
            Primitives.Tube(part, Form.ForkJunction.SinkStart(leadPath, limbBaseR), leadRadii, leadSides,
                (i, v) => { double t = lsCount <= 1 ? 0 : i / (double)(lsCount - 1); return (TaShiverleafTrunkCol(0.50 + 0.50 * t, v), 1.0, t, v, 2.0, 0.0); });
            part.EssentialSpans.Add((leadStart, part.VertexCount - leadStart));

            // Secondary twigs along scaffold bough forming a deep, rounded billowing crown
            int twigsFull = juvenile ? 3 : 5;
            for (int w = 0; w < twigsFull; w++)
            {
                double twigF = 0.22 + 0.70 * (w + 0.5) / twigsFull;
                var twRoot = TaBezier(root, cB0, cB1, end, twigF);
                var twTan = TaBezierTangent(root, cB0, cB1, end, twigF);
                double sideSign = (w % 2 == 0) ? 1.0 : -1.0;
                var twOut = TaNorm(outward * 0.70 + side * (sideSign * 0.60) + Vec3.Up * 0.25, Vec3.Up);
                double twReach = rng.Range(0.24, 0.42) * (juvenile ? 0.75 : 1.0);
                var twEnd = twRoot + twOut * twReach + Vec3.Up * rng.Range(0.06, 0.16);
                var twC0 = twRoot + TaNorm(twTan * 0.35 + twOut * 0.70, Vec3.Up) * (twReach * 0.40);
                var twC1 = twEnd - TaNorm(twOut * 0.80 + Vec3.Up * 0.20, Vec3.Up) * (twReach * 0.34);

                twigList.Add((twRoot, twC0, twC1, twEnd, az + sideSign * 0.50, limbBaseR * 0.42));
            }
        }

        // Loft secondary twigs with tubes
        foreach (var (twRoot, twC0, twC1, twEnd, twAz, twBaseR) in twigList)
        {
            var twOut = TaRadial(twAz);
            int twStations = detail == 2 ? 3 : 4;
            var twPath = TaSampleBezier(twRoot, twC0, twC1, twEnd, twStations);
            double twTipR = Math.Max(0.0022, twBaseR * 0.28);
            var twRadii = new double[twStations];
            for (int i = 0; i < twStations; i++)
            {
                double st = i / (double)(twStations - 1);
                double collar = 1.0 + 0.28 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.22), 2.0);
                twRadii[i] = MathD.Lerp(twBaseR, twTipR, st) * collar;
            }

            int twSides = part.Lod is { } lodW ? lodW.Sides(6) : 6;
            int twsCount = twStations;
            Primitives.Tube(part, Form.ForkJunction.SinkStart(twPath, twBaseR), twRadii, twSides,
                (i, v) => { double t = twsCount <= 1 ? 0 : i / (double)(twsCount - 1); return (Primitives.Mix(TaShiverleafTwig, TaShiverleafBarkDark, t * 0.20), 1.0, t, v, 2.0, 0.0); });

            // Silky pearl-rose catkins hanging from outer twigs
            if (!juvenile && catkinsBuilt < catkinBudget && rng.NextDouble() < 0.75)
            {
                TaShiverleafCatkin(part, rng, twEnd, twOut, c2);
                catkinsBuilt++;
            }
        }

        // Broad, billowing crown: distribute leaves along scaffold boughs and twigs
        // on trembling petioles (~28% twisted for shimmering two-tone canopy)
        int leavesBuilt = 0;
        int leafCap = detail switch { 2 => 650, 1 => 1050, _ => (juvenile ? 850 : 1550) };

        // 1. Foliage along scaffold boughs
        foreach (var (root, cB0, cB1, end, az, _) in scaffoldBoughs)
        {
            if (leavesBuilt >= leafCap) break;
            int boughLeafCount = detail switch { 2 => 6, 1 => 10, _ => 15 };
            for (int li = 0; li < boughLeafCount && leavesBuilt < leafCap; li++)
            {
                double f = 0.18 + 0.78 * (li + 0.5) / boughLeafCount;
                var node = TaBezier(root, cB0, cB1, end, f);
                double sideSign = (li % 2 == 0) ? 1.0 : -1.0;
                double leafAz = az + sideSign * rng.Range(0.40, 0.85);
                var horiz = TaRadial(leafAz);
                double pitch = rng.Range(0.72, 1.28);
                var petioleDir = TaNorm(horiz * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch), Vec3.Up);
                TaShiverleafLeaf(part, rng, node, petioleDir, leafScale, c1, c2, detail);
                leavesBuilt++;
            }
        }

        // 2. Foliage along secondary twigs
        foreach (var (twRoot, twC0, twC1, twEnd, twAz, _) in twigList)
        {
            if (leavesBuilt >= leafCap) break;
            int twigLeafCount = detail switch { 2 => 4, 1 => 8, _ => 12 };
            for (int li = 0; li < twigLeafCount && leavesBuilt < leafCap; li++)
            {
                double f = 0.12 + 0.82 * (li + 0.5) / twigLeafCount;
                var node = TaBezier(twRoot, twC0, twC1, twEnd, f);
                double sideSign = (li % 2 == 0) ? 1.0 : -1.0;
                double leafAz = twAz + sideSign * rng.Range(0.35, 0.80);
                var horiz = TaRadial(leafAz);
                double pitch = rng.Range(0.68, 1.30);
                var petioleDir = TaNorm(horiz * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch), Vec3.Up);
                TaShiverleafLeaf(part, rng, node, petioleDir, leafScale, c1, c2, detail);
                leavesBuilt++;
            }
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
    }

    // =========================================================================
    // 3. WITCHBERRY (Fairytale Rowan / Mountain-Ash - Sorbus aucuparia)
    // Sinuous muscular trunk with natural crook/lean, flared branch collars,
    // wide-spreading arching boughs, feathered odd-pinnate leaves clustered at branch
    // ends in full 3D sprays, and abundant vibrant scarlet berry corymbs.
    // =========================================================================
    private static void Witchberry(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed, PreserveUpperCanopyOnLod = true };
        int detail = part.FloraDetailLevel ?? 0;
        bool coarse = part.Lod?.CullMinorDetails == true;
        double H = height > 0.05 ? height : 2.90;
        double R = radius > 0.01 ? radius : 1.05;
        double hScale = juvenile ? 0.62 : 1.0;
        double leafScale = juvenile ? 0.78 : 1.0;
        var scarlet = Primitives.Mix(c2, TaScarlet, 0.92);

        // Flared root buttresses radiating from trunk base
        TaRootButtresses(part, rng, Vec3.Zero, 0.22 * hScale, TaWitchberryTrunkCol, detail);

        // Sinuous muscular trunk with natural crook and lean:
        // Rises to ~38% height with an organic S-curve crook
        double boleH = H * (juvenile ? 0.32 : 0.38) * hScale;
        double leanAz = rng.Range(0.0, Math.Tau);
        var leanDir = TaRadial(leanAz);
        double leanMag = rng.Range(0.18, 0.28) * hScale;

        var boleP0 = Vec3.Zero;
        var boleP1 = leanDir * leanMag + Vec3.Up * boleH;
        var boleC0 = Vec3.Up * (boleH * 0.38) + leanDir * (leanMag * 0.70);
        var boleC1 = boleP1 - Vec3.Up * (boleH * 0.26) - leanDir * (leanMag * 0.20);

        int trunkStations = detail == 2 ? 8 : (detail == 1 ? 14 : 22);
        var trunkPath = TaSampleBezier(boleP0, boleC0, boleC1, boleP1, trunkStations);
        double rBase = rng.Range(0.19, 0.24) * (juvenile ? 0.62 : 1.0);
        double rBoleTop = rBase * 0.44;
        var trunkRadii = new double[trunkStations];
        for (int i = 0; i < trunkStations; i++)
        {
            double t = i / (double)(trunkStations - 1);
            // Muscular fluting flare at base decaying smoothly into upper trunk
            double flare = 1.0 + 0.48 * Math.Exp(-t * 4.2) + 0.10 * Math.Pow(Math.Max(0.0, t - 0.70) / 0.30, 2.0);
            trunkRadii[i] = MathD.Lerp(rBase, rBoleTop, Math.Pow(t, 0.85)) * flare;
        }

        // Fork radii (F1): children capped by r^2.5 conservation; trunk swells toward each limb fork
        int limbCount = juvenile ? 3 : 4;
        var wbForks = new (Vec3, double)[limbCount];
        var wbForkR = new double[limbCount];
        for (int k = 0; k < limbCount; k++)
        {
            double fT = Math.Clamp(0.42 + 0.48 * (k / (double)limbCount) + 0.04 * Math.Sin(leanAz + k * 1.7), 0.36, 0.94);
            wbForkR[k] = Math.Min(rBoleTop * 0.72, Form.ForkJunction.ChildRadiusShare(TaLocalR(trunkRadii, fT), 0.25));
            wbForks[k] = (TaBezier(boleP0, boleC0, boleC1, boleP1, fT), wbForkR[k]);
        }
        Form.ForkJunction.WidenParent(trunkPath, trunkRadii, wbForks);

        int trunkSides = part.Lod is { } lodT ? lodT.Sides(18) : 18;
        int trunkStart = part.VertexCount;
        int tsCount = trunkStations;
        Primitives.Tube(part, trunkPath, trunkRadii, trunkSides,
            (i, v) => { double t = tsCount <= 1 ? 0 : i / (double)(tsCount - 1); return (TaWitchberryTrunkCol(t * 0.35, v), 1.0, t, v, 2.0, 0.0); });
        part.EssentialSpans.Add((trunkStart, part.VertexCount - trunkStart));

        // Primary boughs: Leader bough + 4 spreading boughs branching at staggered heights
        // Wide-spreading arching boughs with flared branch collars (no stiff single V-fork!)
        var primaryLimbs = new List<(Vec3 Root, Vec3 C0, Vec3 C1, Vec3 End, double Az, double BaseR)>();
        var secondaryBoughs = new List<(Vec3 Root, Vec3 C0, Vec3 C1, Vec3 End, double Az, double BaseR)>();
        var terminalShoots = new List<(Vec3 Root, Vec3 Dir, double Az)>();

        // 1. Central ascending leader continuing the trunk top
        {
            var leadTan = TaBezierTangent(boleP0, boleC0, boleC1, boleP1, 1.0);
            double leadAz = leanAz + rng.Range(-0.25, 0.25);
            var leadOut = TaRadial(leadAz);
            double leadReach = R * rng.Range(0.30, 0.46) * (juvenile ? 0.72 : 1.0);
            double leadRise = H * rng.Range(0.28, 0.40) * (juvenile ? 0.78 : 1.0);
            var leadEnd = boleP1 + leadOut * leadReach + Vec3.Up * leadRise;
            double len = (leadEnd - boleP1).Length;

            var cL0 = boleP1 + TaNorm(leadTan * 0.60 + leadOut * 0.40 + Vec3.Up * 0.30, Vec3.Up) * (len * 0.38);
            var cL1 = leadEnd - TaNorm(leadOut * 0.60 + Vec3.Up * 0.50, Vec3.Up) * (len * 0.34);

            int st = detail == 2 ? 5 : (detail == 1 ? 7 : 9);
            var path = TaSampleBezier(boleP1, cL0, cL1, leadEnd, st);
            double bR = rBoleTop * 0.72;
            double tR = Math.Max(0.008, bR * 0.32);
            var rads = new double[st];
            for (int i = 0; i < st; i++)
            {
                double s = i / (double)(st - 1);
                double collar = 1.0 + 0.40 * Math.Pow(Math.Max(0.0, 1.0 - s / 0.24), 2.0);
                rads[i] = MathD.Lerp(bR, tR, Math.Pow(s, 0.90)) * collar;
            }
            int sides = part.Lod is { } lodL ? lodL.Sides(10) : 10;
            int lStart = part.VertexCount;
            Primitives.Tube(part, path, rads, sides,
                (i, v) => (TaWitchberryTrunkCol(0.35 + 0.35 * (i / (double)(st - 1)), v), 1.0, (double)i / (st - 1), v, 2.0, 0.0));
            part.EssentialSpans.Add((lStart, part.VertexCount - lStart));
            primaryLimbs.Add((boleP1, cL0, cL1, leadEnd, leadAz, bR));
            terminalShoots.Add((leadEnd, TaBezierTangent(boleP1, cL0, cL1, leadEnd, 1.0), leadAz));
        }

        // 2. 4 spreading primary boughs branching at staggered heights from the trunk
        double baseAz = leanAz + Math.PI * 0.65;

        for (int k = 0; k < limbCount; k++)
        {
            double az = baseAz + k * (Math.Tau / limbCount) + rng.Range(-0.18, 0.18);
            var outward = TaRadial(az);
            var side = TaRadial(az + Math.PI * 0.5);

            // Staggered attachment heights along the trunk (0.45, 0.62, 0.80, 0.95)
            double forkT = Math.Clamp(0.42 + 0.48 * (k / (double)limbCount) + 0.04 * Math.Sin(leanAz + k * 1.7), 0.36, 0.94);
            var limbRoot = TaBezier(boleP0, boleC0, boleC1, boleP1, forkT) + outward * (rBoleTop * 0.45);
            var trunkTan = TaBezierTangent(boleP0, boleC0, boleC1, boleP1, forkT);

            double reach = R * rng.Range(0.68, 0.92) * (juvenile ? 0.72 : 1.0);
            double rise = H * rng.Range(0.24, 0.38) * (juvenile ? 0.78 : 1.0);
            var limbEnd = limbRoot + outward * reach + Vec3.Up * rise + side * rng.Range(-0.08, 0.08);
            double len = (limbEnd - limbRoot).Length;

            // Sinuous arching curve with flared branch collar (natural curve, no sharp elbow kink!)
            var limbC0 = limbRoot + TaNorm(trunkTan * 0.30 + outward * 0.70 + Vec3.Up * 0.35, Vec3.Up) * (len * 0.40);
            var limbC1 = limbEnd - TaNorm(outward * 0.85 + Vec3.Up * 0.15, Vec3.Up) * (len * 0.34);

            int limbStations = detail == 2 ? 5 : (detail == 1 ? 7 : 9);
            var limbPath = TaSampleBezier(limbRoot, limbC0, limbC1, limbEnd, limbStations);
            double limbBaseR = Math.Min(rBoleTop * rng.Range(0.65, 0.78), wbForkR[k]);
            double limbTipR = Math.Max(0.007, limbBaseR * 0.32);
            var limbRadii = new double[limbStations];
            for (int i = 0; i < limbStations; i++)
            {
                double st = i / (double)(limbStations - 1);
                // Smooth flared organic branch collar at fork (1.42x)
                double collar = 1.0 + 0.42 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.24), 2.0);
                limbRadii[i] = MathD.Lerp(limbBaseR, limbTipR, Math.Pow(st, 0.90)) * collar;
            }

            int limbSides = part.Lod is { } lodL ? lodL.Sides(10) : 10;
            int limbStart = part.VertexCount;
            int lsCount = limbStations;
            Primitives.Tube(part, Form.ForkJunction.SinkStart(limbPath, limbBaseR), limbRadii, limbSides,
                (i, v) => { double t = lsCount <= 1 ? 0 : i / (double)(lsCount - 1); return (TaWitchberryTrunkCol(0.35 + 0.45 * t, v), 1.0, t, v, 2.0, 0.0); });
            part.EssentialSpans.Add((limbStart, part.VertexCount - limbStart));
            primaryLimbs.Add((limbRoot, limbC0, limbC1, limbEnd, az, limbBaseR));
            terminalShoots.Add((limbEnd, TaBezierTangent(limbRoot, limbC0, limbC1, limbEnd, 1.0), az));

            // Secondary spreading boughs branching off primary limb
            int secCount = juvenile ? 2 : 3;
            for (int s = 0; s < secCount; s++)
            {
                double sF = 0.35 + 0.52 * (s + 0.5) / secCount;
                var secRoot = TaBezier(limbRoot, limbC0, limbC1, limbEnd, sF);
                var secTan = TaBezierTangent(limbRoot, limbC0, limbC1, limbEnd, sF);
                double sideSign = (s % 2 == 0) ? 1.0 : -1.0;
                double secAz = az + sideSign * rng.Range(0.38, 0.70);
                var secOut = TaRadial(secAz);
                double secReach = reach * rng.Range(0.42, 0.62);
                double secRise = rise * rng.Range(0.18, 0.38);

                var secEnd = secRoot + secOut * secReach + Vec3.Up * secRise + side * (sideSign * 0.05);
                double secLen = (secEnd - secRoot).Length;
                var secC0 = secRoot + TaNorm(secTan * 0.35 + secOut * 0.65 + Vec3.Up * 0.28, Vec3.Up) * (secLen * 0.38);
                var secC1 = secEnd - TaNorm(secOut * 0.85 + Vec3.Up * 0.15, Vec3.Up) * (secLen * 0.32);

                int secStations = detail == 2 ? 4 : (detail == 1 ? 5 : 6);
                var secPath = TaSampleBezier(secRoot, secC0, secC1, secEnd, secStations);
                double secBaseR = MathD.Lerp(limbBaseR, limbTipR, sF) * 0.58;
                double secTipR = Math.Max(0.0035, secBaseR * 0.35);
                var secRadii = new double[secStations];
                for (int i = 0; i < secStations; i++)
                {
                    double st = i / (double)(secStations - 1);
                    double collar = 1.0 + 0.30 * Math.Pow(Math.Max(0.0, 1.0 - st / 0.25), 2.0);
                    secRadii[i] = MathD.Lerp(secBaseR, secTipR, st) * collar;
                }

                int secSides = part.Lod is { } lodSec ? lodSec.Sides(7) : 7;
                int secStart = part.VertexCount;
                int scCount = secStations;
                Primitives.Tube(part, Form.ForkJunction.SinkStart(secPath, secBaseR), secRadii, secSides,
                    (i, v) => { double t = scCount <= 1 ? 0 : i / (double)(scCount - 1); return (TaWitchberryTrunkCol(0.65 + 0.35 * t, v), 1.0, t, v, 2.0, 0.0); });
                part.EssentialSpans.Add((secStart, part.VertexCount - secStart));
                secondaryBoughs.Add((secRoot, secC0, secC1, secEnd, secAz, secBaseR));
                terminalShoots.Add((secEnd, TaBezierTangent(secRoot, secC0, secC1, secEnd, 1.0), secAz));

                // Outer terminal shoots branching off secondary bough
                int shootCount = juvenile ? 1 : 2;
                for (int sh = 0; sh < shootCount; sh++)
                {
                    double shF = 0.50 + 0.42 * (sh + 0.5) / shootCount;
                    var shRoot = TaBezier(secRoot, secC0, secC1, secEnd, shF);
                    var shTan = TaBezierTangent(secRoot, secC0, secC1, secEnd, shF);
                    double shSide = (sh % 2 == 0) ? 1.0 : -1.0;
                    var shOut = TaNorm(secOut * 0.70 + side * (shSide * 0.50) + Vec3.Up * 0.15, Vec3.Up);
                    double shReach = rng.Range(0.20, 0.32) * (juvenile ? 0.75 : 1.0);
                    var shEnd = shRoot + shOut * shReach + Vec3.Up * rng.Range(0.02, 0.10);
                    var shC0 = shRoot + TaNorm(shTan * 0.35 + shOut * 0.65, Vec3.Up) * (shReach * 0.36);
                    var shC1 = shEnd - TaNorm(shOut * 0.85 + Vec3.Up * 0.15, Vec3.Up) * (shReach * 0.32);

                    int shStations = detail == 2 ? 3 : 4;
                    var shPath = TaSampleBezier(shRoot, shC0, shC1, shEnd, shStations);
                    double shBaseR = secTipR * 0.80;
                    double shTipR = Math.Max(0.0018, shBaseR * 0.32);
                    var shRadii = new double[shStations];
                    for (int i = 0; i < shStations; i++)
                        shRadii[i] = MathD.Lerp(shBaseR, shTipR, i / (double)(shStations - 1));

                    int shSides = part.Lod is { } lodW ? lodW.Sides(5) : 5;
                    int shsCount = shStations;
                    Primitives.Tube(part, Form.ForkJunction.SinkStart(shPath, shBaseR), shRadii, shSides,
                        (i, v) => { double t = shsCount <= 1 ? 0 : i / (double)(shsCount - 1); return (Primitives.Mix(TaWitchberryTwig, TaWitchberryBarkDark, t * 0.25), 1.0, t, v, 2.0, 0.0); });
                    terminalShoots.Add((shEnd, TaBezierTangent(shRoot, shC0, shC1, shEnd, 1.0), secAz));
                }
            }
        }

        // Lush feathered odd-pinnate leaves clustered at branch ends in full 3D sprays,
        // with abundant vibrant scarlet berry corymbs nestled within
        foreach (var (shootNode, shootDir, shootAz) in terminalShoots)
        {
            TaWitchberryTerminalSpray(part, rng, shootNode, shootDir, leafScale, c1, scarlet, detail, coarse);
        }

        // Additional filler fronds along secondary boughs to give deep, lush foliage volume
        foreach (var sec in secondaryBoughs)
        {
            int fillerCount = detail switch { 2 => 1, 1 => 2, _ => (juvenile ? 2 : 3) };
            for (int sf = 0; sf < fillerCount; sf++)
            {
                double f = 0.25 + 0.60 * (sf + 0.5) / fillerCount;
                var node = TaBezier(sec.Root, sec.C0, sec.C1, sec.End, f);
                var tan = TaBezierTangent(sec.Root, sec.C0, sec.C1, sec.End, f);
                double sideSign = (sf % 2 == 0) ? 1.0 : -1.0;
                var side = TaRadial(sec.Az + Math.PI * 0.5);
                var leafDir = TaNorm(tan * rng.Range(0.38, 0.60) + TaRadial(sec.Az) * 0.34 + side * (sideSign * 0.58) + Vec3.Up * rng.Range(-0.10, 0.42), Vec3.Up);
                double leafLen = rng.Range(0.24, 0.30) * leafScale;
                TaWitchberryLeaf(part, rng, node, leafDir, leafLen, leafScale, c1, detail);
            }
        }

        // Additional filler fronds along primary limbs
        int limbFillerCount = detail switch { 2 => 4, 1 => 8, _ => (juvenile ? 8 : 14) };
        for (int li = 0; li < limbFillerCount; li++)
        {
            int k = li % primaryLimbs.Count;
            var (root, c0, c1p, end, az, _) = primaryLimbs[k];
            double f = 0.35 + 0.55 * (li / (double)limbFillerCount);
            var node = TaBezier(root, c0, c1p, end, f);
            var tan = TaBezierTangent(root, c0, c1p, end, f);
            var outward = TaRadial(az);
            double sideSign = (li % 2 == 0) ? 1.0 : -1.0;
            var side = TaRadial(az + Math.PI * 0.5);
            var leafDir = TaNorm(tan * rng.Range(0.36, 0.60) + outward * 0.34 + side * (sideSign * 0.58) + Vec3.Up * rng.Range(-0.10, 0.42), Vec3.Up);
            double leafLen = rng.Range(0.25, 0.32) * leafScale;
            TaWitchberryLeaf(part, rng, node, leafDir, leafLen, leafScale, c1, detail);
        }

        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
    }
}
