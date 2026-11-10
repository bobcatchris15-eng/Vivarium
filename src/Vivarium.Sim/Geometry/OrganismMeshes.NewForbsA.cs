using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// NewFlora forb set A (2026-11-06 expansion), fictional identities: Starveil (starveil),
/// Butterglint (butterglint), Mistviolet (mistviolet), Hushclover (hushclover),
/// Cinderberry (cinderberry), Marshkettle (marshkettle).
/// Each builder authors in real metres into a scratch part, then converts with
/// <see cref="NewFloraAppendWorld"/>. Petal heads (Sheet ribbons are origin-born) are built in a
/// throwaway sub-mesh and appended at the stalk apex. All colours derive from c1/c2 by
/// <see cref="Primitives.Mix"/>/<see cref="Primitives.Scale"/>; flower organs are flagged through
/// <see cref="NewFloraMarkOrgan"/> so the shader keeps their author pigment.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>Leaf/flower budget for the current detail tier and LOD: far tiers drop counts, silhouette kept.</summary>
    private static int NewForbsACount(MeshData part, int full)
    {
        int detail = part.FloraDetailLevel ?? 0;
        int n = detail switch
        {
            0 => full,
            1 => Math.Max(1, (int)Math.Round(full * 0.7)),
            _ => Math.Max(1, (int)Math.Round(full * 0.5)),
        };
        if (part.Lod is { } lod) n = Math.Max(1, (int)Math.Round(n * lod.LeafFraction));
        return n;
    }

    /// <summary>Appends <paramref name="part"/> to <paramref name="m"/> and re-bases essential spans (Append drops them).</summary>
    private static void NewForbsAFinish(MeshData m, MeshData part, double radius, double height)
    {
        int baseVertex = m.VertexCount;
        int spansBefore = m.EssentialSpans.Count;
        NewFloraAppendWorld(m, part, radius, height);
        if (m.EssentialSpans.Count == spansBefore && part.EssentialSpans.Count > 0)
            foreach (var span in part.EssentialSpans)
                m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));
    }

    /// <summary>Petiole direction from azimuth and pitch measured from vertical (1.0+ = sprawling).</summary>
    private static Vec3 NewForbsAPetioleDir(double azimuth, double pitch)
        => new Vec3(Math.Cos(azimuth) * Math.Sin(pitch), Math.Cos(pitch), Math.Sin(azimuth) * Math.Sin(pitch)).Normalized();

    /// <summary>Low basal crown anchoring the rosette (rhizome cap).</summary>
    private static void NewForbsACrown(MeshData part, double r, double h, double[] c1, double[] c2)
    {
        var col = Primitives.Mix(c1, c2, 0.15);
        int v0 = part.VertexCount;
        Primitives.Ellipsoid(part, new Vec3(0, h * 0.018, 0), new Vec3(r * 0.10, h * 0.045, r * 0.10),
            3, 6, (u, v) => (col, 1.0, u, v, 0.0, 0.0));
        NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
    }

    /// <summary>
    /// A radial flower head built at the origin and appended at <paramref name="apex"/>:
    /// pointed Sheet petals around a flattened centre disc, optionally with a dark mid-vein on the
    /// petals and/or a ring of stamen nubs. Flowers are essential spans so LOD keeps them.
    /// </summary>
    private static void NewForbsAFlower(MeshData part, Vec3 apex, ulong seed,
        int petalCount, double petalLength, double petalHalfWidth, double curl, double droop,
        double[] petalBase, double[] petalTip, double[] centreCol, double centreRadius, double centreFlatten,
        bool vein, double[] veinCol, double veinStrength, int stamenRing, double[] stamenCol,
        double[]? lenScale = null, double[]? widScale = null)
    {
        var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
        double azimuthBase = Rng.HashUnit(seed, Hash.Fnv1a64("newforbsA.flower"), 0) * Math.PI * 2.0;
        for (int k = 0; k < petalCount; k++)
        {
            double az = azimuthBase + k * (Math.PI * 2.0 / petalCount)
                + (Rng.HashUnit(seed, Hash.Fnv1a64("newforbsA.petal"), (ulong)k) - 0.5) * 0.16;
            double len = petalLength * (lenScale is { } ls ? ls[k % ls.Length] : 1.0);
            double wid = petalHalfWidth * (widScale is { } ws ? ws[k % ws.Length] : 1.0);
            var axis = new AxisParams(Length: len, BaseAngle: 1.22, BaseAzimuth: az, Droop: droop,
                WobbleAmplitude: petalLength * 0.05, WobbleFrequency: 1.7, Segments: 5);
            ulong pSeed = Rng.Mix(seed, (ulong)(k * 17 + 3));
            int v0 = tmp.VertexCount;
            Sheet.Build(tmp, new SheetParams(axis, HalfWidth: wid, Thickness: 0.0004, Curl: curl, DetailLevel: 1, FaceAxisUp: true),
                pSeed, petalBase, petalTip);
            if (vein) // darken the petal mid-vein: Sheet UV.y is the across-blade coordinate
            {
                for (int v = v0; v < tmp.VertexCount; v++)
                {
                    double x = Math.Abs(tmp.UV[v * 2 + 1] * 2.0 - 1.0);
                    double w = Math.Max(0.0, 1.0 - x / 0.22);
                    if (w <= 0.0) continue;
                    var cur = new[] { (double)tmp.Colors[v * 4], (double)tmp.Colors[v * 4 + 1], (double)tmp.Colors[v * 4 + 2] };
                    var mixed = Primitives.Mix(cur, veinCol, w * veinStrength);
                    tmp.SetColor(v, mixed[0], mixed[1], mixed[2], 1.0);
                }
            }
        }
        // Centre disc / receptacle.
        Primitives.Ellipsoid(tmp, new Vec3(0, petalLength * 0.03, 0),
            new Vec3(centreRadius, Math.Max(1e-4, centreRadius * centreFlatten), centreRadius),
            3, 6, (u, v) => (centreCol, 1.0, u, v, 0.0, 0.0));
        // Optional dense stamen ring (marigold pompom).
        for (int s = 0; s < stamenRing; s++)
        {
            double a = s * (Math.PI * 2.0 / stamenRing) + azimuthBase;
            var pos = new Vec3(Math.Cos(a) * centreRadius * 1.35, centreRadius * centreFlatten * 0.55, Math.Sin(a) * centreRadius * 1.35);
            Primitives.Ellipsoid(tmp, pos, new Vec3(centreRadius * 0.32, centreRadius * 0.55, centreRadius * 0.32),
                2, 5, (u, v) => (stamenCol, 1.0, u, v, 0.0, 0.0));
        }
        NewFloraMarkOrgan(tmp, 0, tmp.VertexCount);
        int baseV = part.VertexCount;
        part.Append(tmp, p => p + apex);
        if (part.VertexCount > baseV) part.EssentialSpans.Add((baseV, part.VertexCount - baseV));
    }

    /// <summary>Slender flower stalk (SoftTube from the crown) plus a flower head at its apex. Returns the apex.</summary>
    private static Vec3 NewForbsAStalk(MeshData part, ulong seed, double azimuth, double length,
        double tilt, double droop, double baseRadius, double[] c1, double[] c2)
    {
        var axis = new AxisParams(Length: Math.Max(0.005, length), BaseAngle: tilt, BaseAzimuth: azimuth,
            Droop: droop, WobbleAmplitude: 0.0015, WobbleFrequency: 2.0, Segments: 8);
        var col = Primitives.Mix(c1, c2, 0.20);
        int v0 = part.VertexCount;
        var frames = Axis.Build(axis, seed);
        SoftTube.Build(part, new SoftTubeParams(axis, BaseRadius: Math.Max(1e-4, baseRadius), TipRadius: baseRadius * 0.55, Segments: 6),
            seed, (i, v) => (col, 1.0, i / (double)Math.Max(1, frames.Count - 1), v, 0.0, 0.0));
        int sides = Math.Max(12, 6);
        if (part.Lod is { } lod) sides = Math.Max(8, lod.Sides(sides));
        if (part.VertexCount > v0) part.Structural.Add((v0, part.VertexCount - v0, sides, Math.Max(1e-4, baseRadius)));
        return frames[^1].Point;
    }

    /// <summary>Slender stalk rooted at <paramref name="basePos"/> whose apex targets <paramref name="targetY"/> (world height). Returns apex.</summary>
    private static Vec3 NewForbsAStalkFrom(MeshData part, ulong seed, Vec3 basePos, double targetY, double azimuth,
        double tilt, double droop, double baseRadius, double[] c1, double[] c2)
    {
        double dy = Math.Max(0.01, targetY - basePos.Y);
        double cosTilt = Math.Max(0.80, Math.Cos(tilt));
        double length = dy / cosTilt;
        int v0 = part.VertexCount;
        RefreshStalk(part, seed, azimuth, length, tilt, droop, baseRadius, c1, c2);
        for (int i = v0; i < part.VertexCount; i++)
        {
            var pos = part.Position(i) + basePos;
            part.Positions[i * 3] = (float)pos.X;
            part.Positions[i * 3 + 1] = (float)pos.Y;
            part.Positions[i * 3 + 2] = (float)pos.Z;
        }
        // Return apex = original tip + basePos; recapture by rebuilding the frame tip
        var axis = new AxisParams(Length: Math.Max(0.005, length), BaseAngle: tilt, BaseAzimuth: azimuth,
            Droop: droop, WobbleAmplitude: 0.0015, WobbleFrequency: 2.0, Segments: 8);
        var frames = Axis.Build(axis, seed);
        return frames[^1].Point + basePos;
    }

    private static void RefreshStalk(MeshData part, ulong seed, double azimuth, double length,
        double tilt, double droop, double baseRadius, double[] c1, double[] c2)
    {
        var axis = new AxisParams(Length: Math.Max(0.005, length), BaseAngle: tilt, BaseAzimuth: azimuth,
            Droop: droop, WobbleAmplitude: 0.0015, WobbleFrequency: 2.0, Segments: 8);
        var col = Primitives.Mix(c1, c2, 0.20);
        int v0 = part.VertexCount;
        var frames = Axis.Build(axis, seed);
        SoftTube.Build(part, new SoftTubeParams(axis, BaseRadius: Math.Max(1e-4, baseRadius), TipRadius: baseRadius * 0.55, Segments: 6),
            seed, (i, v) => (col, 1.0, i / (double)Math.Max(1, frames.Count - 1), v, 0.0, 0.0));
        int sides = Math.Max(12, 6);
        if (part.Lod is { } lod) sides = Math.Max(8, lod.Sides(sides));
        if (part.VertexCount > v0) part.Structural.Add((v0, part.VertexCount - v0, sides, Math.Max(1e-4, baseRadius)));
    }

    /// <summary>One Broadleaf leaf on an already-placed petiole apex (used for trifoliate fans).</summary>
    private static void NewForbsALeaflet(MeshData part, Vec3 apex, Vec3 dir, double bladeLen, double widthRatio,
        BroadleafOutline outline, ulong seed, double[] c1, double[] c2, double age,
        double camber, double wavyMargin, double petioleLength, double petioleRadius, int? detailOverride = null,
        double midribFold = 0.10, double[]? undersideColor = null)
    {
        double jitter = Rng.HashUnit(seed, Hash.Fnv1a64("newforbsA.leaflet"), 0);
        Broadleaf.BuildLeaf(part, apex, dir, bladeLen, bladeLen * widthRatio, outline, seed, c1, c2,
            age: age, camber: camber, midribFold: midribFold, wavyMargin: wavyMargin, wavyFrequency: 5.0,
            tipCurl: 0.08, petioleLength: petioleLength, petioleRadius: petioleRadius, petioleKink: 0.30,
            asymmetry: (jitter - 0.5) * 0.10, roll: (jitter - 0.5) * 0.5, undersideColor: undersideColor,
            detailOverride: detailOverride);
    }

    /// <summary>
    /// Wave-4 daylight pass: Broadleaf dims <c>c1</c> internally (mature base *0.72), so foliage is
    /// pre-brightened ~40% and pinned to a bright mid-green with a light underside. Leaves never
    /// reach a near-black mature base in shade, while keeping the species' own green hue.
    /// </summary>
    private static void NewForbsAFoliage(double[] c1, out double[] leafC1, out double[] leafC2, out double[] underCol)
    {
        leafC1 = Primitives.Scale(c1, 1.94);
        leafC2 = Primitives.Mix(leafC1, new[] { 0.85, 0.92, 0.55 }, 0.35);
        underCol = Primitives.Mix(leafC1, new[] { 0.85, 0.90, 0.60 }, 0.40);
    }

    /// <summary>Starveil: White-starred spring ephemeral of deep shade. Palmate leaves unfold under the first warm days and hold up a veil of white stars; the whole colony retreats to root before the canopy closes.</summary>
    private static void Starveil(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = part.FloraDetailLevel ?? 0;
        double size = juvenile ? 0.6 : 1.0;
        // Wave-4 daylight pass: bright mid-green foliage (never near-black), light underside.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);

        // Wave-9: 5-6 sub-crowns across 0.15-0.55*radius; each bears 3 deeply divided
        // 3-lobed toothed leaves at Y=0.20-0.42*height forming a leafy canopy intermixed
        // with 6-8 flowers at Y=0.48-0.70*height.
        int subCrowns = NewForbsACount(part, juvenile ? 3 : rng.NextInt(2) + 5); // 5-6
        var crownBases = new List<Vec3>();
        for (int c = 0; c < subCrowns; c++)
        {
            double caz = c * (Math.PI * 2.0 / Math.Max(1, subCrowns)) + rng.Range(-0.25, 0.25);
            double cr = radius * rng.Range(0.15, 0.55) * size;
            var cb = new Vec3(Math.Cos(caz) * cr, height * rng.Range(0.02, 0.06) * size, Math.Sin(caz) * cr);
            crownBases.Add(cb);
            // Small crown cap at each sub-crown.
            int cv0 = part.VertexCount;
            Primitives.Ellipsoid(part, cb + Vec3.Up * (height * 0.015 * size), new Vec3(radius * 0.05, height * 0.025, radius * 0.05),
                3, 6, (u, v) => (leafC1, 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, cv0, part.VertexCount - cv0);
        }

        int? leafletDetail = detail == 0 ? 1 : null;
        for (int i = 0; i < subCrowns; i++)
        {
            var cBase = crownBases[i];
            for (int lf = 0; lf < 3; lf++)
            {
                double az = i * (Math.PI * 2.0 / Math.Max(1, subCrowns)) + lf * (Math.PI * 2.0 / 3.0) + rng.Range(-0.2, 0.2);
                var outDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                // Petiole from the sub-crown (NOT (0,0,0)); blade centre in 0.20-0.42*height,
                // spread across the sub-crown footprint.
                var apex = cBase + outDir * (radius * rng.Range(0.10, 0.30) * size) + Vec3.Up * (height * rng.Range(0.16, 0.36) * size);
                var petioleCol = Primitives.Mix(leafC1, leafC2, 0.30);
                Primitives.Tube(part, new[] { cBase, apex }, new[] { 0.0010, 0.0007 }, 5,
                    (k, v) => (petioleCol, 1.0, k * 0.5, v, 0.0, 0.0));
                var fanBase = (apex - cBase).Normalized();
                var dir = Axis.RotateAround(fanBase, Vec3.Up, rng.Range(-0.3, 0.3)).Normalized();
                if (dir.LengthSq < 1e-6) dir = fanBase;
                NewForbsALeaflet(part, apex, dir, height * rng.Range(0.16, 0.24) * size, 0.80,
                    BroadleafOutline.Hastate, Rng.Mix(seed, (ulong)(i * 300 + lf * 17 + 5)), leafC1, leafC2,
                    rng.Range(0.45, 0.95), 0.14, 0.18, 0.004, 0.0007, leafletDetail,
                    midribFold: 0.12, undersideColor: underCol); // toothed 3-lobed wood-anemone leaf
            }
        }

        // Wave-9: 6-8 flat-open (curl=0.08) 6-7-petalled pure-white stars Y=0.48-0.70,
        // spread 0.15-0.60*radius, tilted outward 15-30 deg, golden stamen boss.
        int stalks = NewForbsACount(part, juvenile ? 3 : rng.NextInt(3) + 6); // flowers 6-8
        var petalBase = Primitives.Scale(c2, 1.50);
        var petalTip = Primitives.Scale(c2, 1.90);
        var golden = new double[] { c2[0] * 1.02, c2[1] * 0.78, c2[2] * 0.38 };
        var centreCol = Primitives.Scale(Primitives.Mix(golden, c1, 0.10), 1.60);
        for (int s = 0; s < stalks; s++)
        {
            int ci = s % Math.Max(1, crownBases.Count);
            var cBase = crownBases.Count > 0 ? crownBases[ci] : Vec3.Zero;
            double targetRR = radius * rng.Range(0.15, 0.60) * size;
            double saz = s * (Math.PI * 2.0 / Math.Max(1, stalks)) + rng.Range(-0.3, 0.3);
            var leafDir = new Vec3(Math.Cos(saz), 0, Math.Sin(saz));
            double tilt = rng.Range(0.26, 0.52); // 15-30 deg outward
            double targetY = height * rng.Range(0.42, 0.62) * size;
            double proj = Math.Max(0.01, (targetY - cBase.Y) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.02 * radius, 0.50 * radius);
            // Sub-crown offset: shift base toward the flower azimuth so final radius ~ targetRR.
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 11));
            var stalkBase = new Vec3(Math.Cos(saz) * baseR, cBase.Y, Math.Sin(saz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, saz, tilt, 0.18, 0.0014, leafC1, leafC2);
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 7)), 6 + (s % 2), // 6-7 plain tepals
                height * 0.145 * size, height * 0.042 * size, 0.08, 0.08, // compact open white star, slight nod
                petalBase, petalTip, centreCol, height * 0.050 * size, 0.45,
                vein: false, veinCol: c2, veinStrength: 0, stamenRing: 6, stamenCol: centreCol);
        }
        NewForbsAFinish(m, part, radius, height);
    }

    /// <summary>Butterglint: Glossy gold-flowered meadow herb. The cupped petals carry a varnish so deep that each flower keeps a small bright copy of the sky.</summary>
    private static void Butterglint(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double size = juvenile ? 0.65 : 1.0;
        // Wave-4 daylight pass: bright mid-green basal leaves, light underside.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);

        // Wave-9: 5-6 sub-crowns across 0.15-0.55*radius, 12-16 deeply 3-parted leaves
        // intermixed Y=0.15-0.45, and 8-10 gold cups Y=0.42-0.70 spread 0.15-0.65 radius.
        int subCrowns = NewForbsACount(part, juvenile ? 3 : rng.NextInt(2) + 5); // 5-6
        var crownBases = new List<Vec3>();
        for (int c = 0; c < subCrowns; c++)
        {
            double caz = c * (Math.PI * 2.0 / Math.Max(1, subCrowns)) + rng.Range(-0.25, 0.25);
            double cr = radius * rng.Range(0.15, 0.55) * size;
            var cb = new Vec3(Math.Cos(caz) * cr, height * rng.Range(0.02, 0.06) * size, Math.Sin(caz) * cr);
            crownBases.Add(cb);
            int cv0 = part.VertexCount;
            Primitives.Ellipsoid(part, cb + Vec3.Up * (height * 0.015 * size), new Vec3(radius * 0.05, height * 0.025, radius * 0.05),
                3, 6, (u, v) => (leafC1, 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, cv0, part.VertexCount - cv0);
        }
        int leafCount = NewForbsACount(part, juvenile ? 6 : rng.NextInt(5) + 12); // 12-16 leaves
        for (int i = 0; i < leafCount; i++)
        {
            var cBase = crownBases.Count > 0 ? crownBases[i % crownBases.Count] : Vec3.Zero;
            double az = rng.Range(0, Math.PI * 2);
            var dir = NewForbsAPetioleDir(az, rng.Range(0.55, 1.05)); // pitched up so blades sit Y=0.15-0.45 intermixed
            // Distribute attach across the sub-crown neighbourhood, never central.
            var attach = cBase + new Vec3(Math.Cos(az) * radius * rng.Range(0.02, 0.12), height * rng.Range(0.02, 0.10) * size, Math.Sin(az) * radius * rng.Range(0.02, 0.12));
            double len = height * rng.Range(0.15, 0.22) * size;
            Broadleaf.BuildLeaf(part, attach, dir, len, len * rng.Range(0.85, 0.95), BroadleafOutline.OrbicularCordate,
                Rng.Mix(seed, (ulong)(i * 733 + 13)), leafC1, leafC2, age: rng.Range(0.40, 0.95),
                camber: 0.10, midribFold: 0.08, wavyMargin: 0.10, wavyFrequency: 8.0, tipCurl: 0.06,
                petioleLength: radius * rng.Range(0.15, 0.35) * size, petioleRadius: 0.0013,
                petioleKink: 0.28, roll: rng.Range(-0.2, 0.2), undersideColor: underCol); // deeply 3-parted lobed
        }

        // Wave-9: 8-10 glossy cup golden-yellow 5-petalled cups Y=0.42-0.70, spread
        // 0.15-0.65*radius, tilted 15-30 deg toward viewer (face-on open cups, never clipped).
        int stalks = NewForbsACount(part, juvenile ? 4 : rng.NextInt(3) + 8); // 8-10 blooms
        var receptacle = Primitives.Scale(c1, 1.60);
        var petalDeep = Primitives.Mix(Primitives.Scale(c2, 0.85), new double[] { 0.95, 0.72, 0.08 }, 0.80);
        var petalTip = Primitives.Mix(Primitives.Scale(c2, 1.00), new double[] { 1.00, 0.88, 0.18 }, 0.80);
        for (int s = 0; s < stalks; s++)
        {
            double targetRR = radius * rng.Range(0.15, 0.65) * size;
            double saz = s * (Math.PI * 2.0 / Math.Max(1, stalks)) + rng.Range(-0.3, 0.3);
            double tilt = rng.Range(0.26, 0.52); // 15-30 deg
            double targetY = height * rng.Range(0.42, 0.70) * size;
            var cBase = crownBases.Count > 0 ? crownBases[s % crownBases.Count] : Vec3.Zero;
            double proj = Math.Max(0.01, (targetY - cBase.Y) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.02 * radius, 0.60 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 21));
            var stalkBase = new Vec3(Math.Cos(saz) * baseR, cBase.Y, Math.Sin(saz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, saz, tilt, 0.02, 0.0022, leafC1, leafC2);
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 17)), 5,
                height * 0.102 * size, height * 0.048 * size, 0.18, 0.02, // shallow gold cup, petals face upward
                petalDeep, petalTip, receptacle, height * 0.030 * size, 0.55,
                vein: false, veinCol: c2, veinStrength: 0, stamenRing: 0, stamenCol: receptacle);
        }
        NewForbsAFinish(m, part, radius, height);
    }

    /// <summary>Mistviolet: Violet star of spring flushes and wet meadows. Round leaves lie flat under pale violet flowers that nod; whole patches surface for a few spring weeks and vanish beneath the summer growth.</summary>
    private static void Mistviolet(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double size = juvenile ? 0.65 : 1.0;
        // Wave-4 daylight pass: bright mid-green ground leaves, light underside.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);

        // Wave-9: 14-18 heart-to-kidney scalloped leaves across 0.15-0.65*radius Y=0.18-0.42,
        // 8-10 nodding violet blooms Y=0.42-0.68 spread 0.18-0.60 radius (never central column).
        int leafCount = NewForbsACount(part, juvenile ? 6 : rng.NextInt(5) + 14); // 14-18 leaves
        for (int i = 0; i < leafCount; i++)
        {
            double az = i * (Math.PI * 2.0 / Math.Max(1, leafCount)) + rng.Range(-0.15, 0.15);
            var dir = NewForbsAPetioleDir(az, rng.Range(0.60, 1.05)); // pitched up so blades sit Y=0.18-0.42
            double rr = radius * rng.Range(0.15, 0.65) * size;
            var attach = new Vec3(Math.Cos(az) * rr * rng.Range(0.15, 0.45), height * rng.Range(0.03, 0.12) * size, Math.Sin(az) * rr * rng.Range(0.15, 0.45));
            double len = height * rng.Range(0.18, 0.26) * size;
            Broadleaf.BuildLeaf(part, attach, dir, len, len * rng.Range(0.95, 1.05), BroadleafOutline.Reniform,
                Rng.Mix(seed, (ulong)(i * 733 + 23)), leafC1, leafC2, age: rng.Range(0.45, 0.95),
                camber: 0.03, midribFold: 0.05, wavyMargin: 0.08, wavyFrequency: 8.0, tipCurl: 0.03, // scalloped kidney
                petioleLength: radius * rng.Range(0.15, 0.35) * size, petioleRadius: 0.0011,
                petioleKink: 0.25, roll: rng.Range(-0.15, 0.15), undersideColor: underCol);
        }

        // Wave-9: 8-10 nodding zygomorphic violet blooms Y=0.42-0.68, spread 0.18-0.60 radius,
        // rich periwinkle-violet (0.52,0.36,0.92) with white-and-purple veined lower spur petal.
        int stalks = NewForbsACount(part, juvenile ? 3 : rng.NextInt(3) + 8); // 8-10 flowers
        var veinCol = Primitives.Scale(c2, 0.65);
        var satTip = Primitives.Mix(Primitives.Scale(c2, 0.90), new double[] { 0.52, 0.36, 0.92 }, 0.85);
        var satBase = Primitives.Scale(satTip, 0.85);
        var lenScale = new[] { 0.65, 0.65, 0.82, 0.85, 1.30 }; // two small uppers, two sides, one large lower
        var widScale = new[] { 0.75, 0.75, 0.85, 0.90, 1.18 };
        var throatCol = Primitives.Scale(Primitives.Mix(satTip, new double[] { 1.0, 1.0, 1.0 }, 0.25), 1.05);
        for (int s = 0; s < stalks; s++)
        {
            double targetRR = radius * rng.Range(0.18, 0.60) * size;
            double saz = s * (Math.PI * 2.0 / Math.Max(1, stalks)) + rng.Range(-0.3, 0.3);
            double tilt = rng.Range(0.35, 0.60); // nodding arc
            double targetY = height * rng.Range(0.40, 0.56) * size;
            double proj = Math.Max(0.01, targetY / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.02 * radius, 0.55 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 31));
            var stalkBase = new Vec3(Math.Cos(saz) * baseR, height * rng.Range(0.02, 0.06) * size, Math.Sin(saz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, saz, tilt, 0.55, 0.0012, leafC1, leafC2);
            int vBefore = part.VertexCount;
            double petalLen = height * 0.145 * size;
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 27)), 5,
                petalLen, height * 0.055 * size, 0.18, 0.22, // ~5 cm open nodding star, flat not tulip cup
                satBase, satTip, throatCol, height * 0.035 * size, 0.60,
                vein: true, veinCol: veinCol, veinStrength: 0.38, stamenRing: 0, stamenCol: c2,
                lenScale: lenScale, widScale: widScale);
            // Pale throat marking: lighten vertices near the flower centre (vertex colour).
            double throatR = Math.Max(1e-4, petalLen * 0.45);
            for (int v = vBefore; v < part.VertexCount; v++)
            {
                double d = (part.Position(v) - apex).Length;
                if (d >= throatR) continue;
                double w = 0.55 * (1.0 - d / throatR);
                var cur = new[] { (double)part.Colors[v * 4], (double)part.Colors[v * 4 + 1], (double)part.Colors[v * 4 + 2] };
                var mixed = Primitives.Mix(cur, throatCol, w);
                part.SetColor(v, mixed[0], mixed[1], mixed[2], 1.0);
            }
        }
        NewForbsAFinish(m, part, radius, height);
    }

    /// <summary>Hushclover: Shamrock-leaved clumper of acid shade. Three heart-leaves fold at dusk and open again at first light, and the small veined white flowers appear to hover well above the leaves.</summary>
    private static void Hushclover(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = part.FloraDetailLevel ?? 0;
        double size = juvenile ? 0.65 : 1.0;
        // Wave-4 daylight pass: bright mid-green mat (never near-black), light underside, matte finish.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);
        int? leafletDetail = detail == 0 ? 1 : null; // 3-cm leaflets need no full-res rugosity

        // Wave-9: fix umbrella spokes — 18-24 SHORT upright petioles (0.025-0.045 m ~ 0.12-0.22*height)
        // distributed evenly across 0.10-0.65*radius, each from a distributed soil base (NOT (0,0,0)),
        // topped with 3 wide notched obcordate leaflets in a continuous cushion.
        int petioles = NewForbsACount(part, juvenile ? 10 : rng.NextInt(7) + 18); // 18-24
        for (int i = 0; i < petioles; i++)
        {
            double az = i * (Math.PI * 2.0 / Math.Max(1, petioles)) + rng.Range(-0.12, 0.12);
            var outDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double rr = radius * rng.Range(0.10, 0.65) * size;
            var soilBase = outDir * rr + Vec3.Up * (height * 0.02 * size);
            var apex = soilBase + Vec3.Up * (height * rng.Range(0.10, 0.20) * size);
            var petioleCol = Primitives.Mix(leafC1, leafC2, 0.30);
            Primitives.Tube(part, new[] { soilBase, apex }, new[] { 0.0010, 0.0007 }, 5,
                (k, v) => (petioleCol, 1.0, k * 0.5, v, 0.0, 0.0));
            for (int lf = 0; lf < 3; lf++)
            {
                // Rotating Up about Up leaves all three directions coincident. Incline
                // each leaflet away from the shared apex to form a trifoliate fan.
                var dir = NewForbsAPetioleDir(lf * (Math.PI * 2.0 / 3.0) + az + rng.Range(-0.06, 0.06), 0.85);
                NewForbsALeaflet(part, apex, dir, height * rng.Range(0.18, 0.24) * size, 0.95, // heart-leaves, matte, crowns obvious
                    BroadleafOutline.Cordate, Rng.Mix(seed, (ulong)(i * 300 + lf * 17 + 41)), leafC1, leafC2,
                    rng.Range(0.45, 0.95), 0.10, 0.01, 0.004, 0.0007, leafletDetail,
                    midribFold: 0.36, undersideColor: underCol); // sharp fold crease, hearts folded
            }
        }

        // Wave-9: 7-9 delicate 5-petalled white lilac-veined flowers hovering just above
        // the shamrocks at Y=0.48-0.68, spread across the cushion.
        int stalks = NewForbsACount(part, juvenile ? 3 : rng.NextInt(3) + 7); // 7-9 flowers
        var stripeCol = Primitives.Mix(c2, leafC1, 0.30);
        var flowerDeep = Primitives.Scale(c2, 1.30);
        var flowerTip = Primitives.Scale(c2, 1.90);
        for (int s = 0; s < stalks; s++)
        {
            double targetRR = radius * rng.Range(0.12, 0.55) * size;
            double flowerAz = s * (Math.PI * 2.0 / Math.Max(1, stalks)) + rng.Range(-0.3, 0.3);
            double targetY = height * rng.Range(0.40, 0.56) * size;
            double tilt = rng.Range(0.10, 0.26);
            double proj = Math.Max(0.01, (targetY - height * 0.05 * size) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.05 * radius, 0.50 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 43));
            var stalkBase = new Vec3(Math.Cos(flowerAz) * baseR, height * 0.05 * size, Math.Sin(flowerAz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, flowerAz, tilt, 0.05, 0.0014, leafC1, leafC2);
            int vBefore = part.VertexCount;
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 41)), 5,
                height * 0.112 * size, height * 0.044 * size, 0.10, 0.03, // compact open flower, not an oversized bowl
                flowerDeep, flowerTip, Primitives.Mix(Primitives.Scale(c2, 1.60), leafC1, 0.20), height * 0.025 * size, 0.60,
                vein: true, veinCol: stripeCol, veinStrength: 0.40,
                stamenRing: 0, stamenCol: c2);
            // Faint vein striping via vertex colour: faint outer stripes flanking the mid-vein.
            for (int v = vBefore; v < part.VertexCount; v++)
            {
                double across = part.UV[v * 2 + 1]; // Sheet across-blade 0..1
                double d = Math.Min(Math.Abs(across - 0.25), Math.Abs(across - 0.75));
                double w = Math.Max(0.0, 1.0 - d / 0.06) * 0.18; // faint
                if (w <= 0.0) continue;
                var cur = new[] { (double)part.Colors[v * 4], (double)part.Colors[v * 4 + 1], (double)part.Colors[v * 4 + 2] };
                var mixed = Primitives.Mix(cur, stripeCol, w);
                part.SetColor(v, mixed[0], mixed[1], mixed[2], 1.0);
            }
        }
        NewForbsAFinish(m, part, radius, height);
    }

    /// <summary>Cinderberry: Low runner with scarlet fruit and trifoliate leaves. Red fruit is held above the leaves on short stalks; the runners root as they run, and the seeds travel in fur and feathers.</summary>
    private static void Cinderberry(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = part.FloraDetailLevel ?? 0;
        double size = juvenile ? 0.65 : 1.0;
        // Wave-4 daylight pass: bright mid-green trifoliate foliage, light underside.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);
        int? leafletDetail = detail == 0 ? 1 : null;

        // Wave-9: lush strawberry patch 0.15-0.75*radius: 16-20 coarsely serrated trifoliate
        // leaves Y=0.25-0.55, from distributed soil bases (not (0,0,0)).
        int petioles = NewForbsACount(part, juvenile ? 8 : rng.NextInt(5) + 16); // 16-20 leaves
        for (int i = 0; i < petioles; i++)
        {
            double az = i * (Math.PI * 2.0 / Math.Max(1, petioles)) + rng.Range(-0.15, 0.15);
            var outDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double rr = radius * rng.Range(0.15, 0.75) * size;
            var soilBase = outDir * rr + Vec3.Up * (height * 0.02 * size);
            var apex = soilBase + Vec3.Up * (height * rng.Range(0.23, 0.53) * size);
            var petioleCol = Primitives.Mix(leafC1, leafC2, 0.28);
            Primitives.Tube(part, new[] { soilBase, apex }, new[] { 0.0013, 0.0009 }, 5,
                (k, v) => (petioleCol, 1.0, k * 0.5, v, 0.0, 0.0));
            for (int lf = 0; lf < 3; lf++)
            {
                var dir = NewForbsAPetioleDir(az + lf * (Math.PI * 2.0 / 3.0) + rng.Range(-0.06, 0.06), 0.85);
                NewForbsALeaflet(part, apex, dir, height * rng.Range(0.20, 0.28) * size, 0.85,
                    BroadleafOutline.Ovate,
                    Rng.Mix(seed, (ulong)(i * 300 + lf * 17 + 53)), leafC1, leafC2,
                    rng.Range(0.45, 0.95), 0.12, 0.14, 0.004, 0.0007, leafletDetail, // coarsely serrated trifoliate leaflets
                    midribFold: 0.08, undersideColor: underCol);
            }
        }

        // Wave-9: 5-6 open white 5-petalled flowers (2.2-2.8 cm, golden centre) Y=0.45-0.68.
        int flowers = NewForbsACount(part, juvenile ? 2 : rng.NextInt(2) + 5); // 5-6 flowers
        for (int s = 0; s < flowers; s++)
        {
            double targetRR = radius * rng.Range(0.15, 0.60) * size;
            double saz = s * (Math.PI * 2.0 / Math.Max(1, flowers)) + rng.Range(-0.3, 0.3);
            double targetY = height * rng.Range(0.45, 0.68) * size;
            double tilt = rng.Range(0.18, 0.40);
            double proj = Math.Max(0.01, (targetY - height * 0.05 * size) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.05 * radius, 0.55 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 59));
            var stalkBase = new Vec3(Math.Cos(saz) * baseR, height * 0.05 * size, Math.Sin(saz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, saz, tilt, 0.06, 0.0015, leafC1, leafC2);
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 59)), 5,
                height * 0.155 * size, height * 0.070 * size, 0.08, 0.05, // small white flower, fruit dominates
                Primitives.Scale(c2, 1.50), Primitives.Scale(c2, 1.85),
                Primitives.Mix(Primitives.Scale(c2, 1.60), leafC1, 0.30), height * 0.050 * size, 0.60,
                vein: false, veinCol: c2, veinStrength: 0, stamenRing: 6, stamenCol: Primitives.Mix(Primitives.Scale(c2, 1.60), leafC1, 0.30));
        }

        // Wave-9: 7-9 plump conical bright-scarlet strawberries (1.6-2.2 cm) Y=0.35-0.60,
        // dotted yellow achenes, green 5-sepal calyx star, hanging prominently distributed.
        int berries = juvenile ? 3 : rng.NextInt(3) + 6; // 6-8 berries
        int stolons = NewForbsACount(part, juvenile ? 2 : rng.NextInt(2) + 2); // 2-3 anchored runners
        var runnerAzimuths = new double[stolons];
        for (int i = 0; i < stolons; i++) runnerAzimuths[i] = rng.Range(0, Math.PI * 2);
        var berryCol = Primitives.Mix(Primitives.Scale(c2, 1.0), new double[] { 1.0, 0.05, 0.02 }, 0.85); // pure scarlet, c2-derived
        var acheneCol = new double[] { 1.00, 0.85, 0.20 }; // yellow achenes
        var calyxCol = Primitives.Mix(leafC1, leafC2, 0.20);
        for (int b = 0; b < berries; b++)
        {
            int runner = b % stolons;
            double berryAz = runnerAzimuths[runner] + rng.Range(-0.30, 0.30);
            double targetRR = radius * rng.Range(0.18, 0.42) * size;
            double targetY = height * rng.Range(0.27, 0.43) * size;
            double tilt = rng.Range(0.32, 0.58);
            double proj = Math.Max(0.01, (targetY - height * 0.04 * size) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.05 * radius, 0.60 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(b * 613 + 67));
            var stalkBase = new Vec3(Math.Cos(berryAz) * baseR, height * 0.04 * size, Math.Sin(berryAz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, berryAz, tilt, 0.62, 0.0022, leafC1, leafC2);
            double bs = rng.Range(0.008, 0.011) * size; // 1.6-2.2 cm long conical fruit (radius ~0.8-1.1cm)
            int v0 = part.VertexCount;
            var fruitCentre = apex - Vec3.Up * (bs * 0.88);
            Primitives.Ellipsoid(part, fruitCentre, new Vec3(bs, bs * 1.25, bs), 4, 7,
                (u, v) =>
                {
                    // Deterministic achene speckle: dark pits on a coarse dot lattice.
                    double dot = Math.Sin(u * 38.0 + v * 13.0) + Math.Sin(v * 31.0 - u * 7.0);
                    var col = dot > 1.45 ? acheneCol : berryCol;
                    return (col, 1.0, u, v, 0.0, 0.0);
                }, pitch: 0.35);
            // Green 5-sepal calyx star: 5 small green discs around the crown.
            for (int cp = 0; cp < 5; cp++)
            {
                double caz = berryAz + cp * (Math.PI * 2.0 / 5.0);
                var cPos = apex + new Vec3(Math.Cos(caz), 0, Math.Sin(caz)) * bs * 0.55 + Vec3.Up * (bs * 0.08);
                Primitives.Ellipsoid(part, cPos, new Vec3(bs * 0.35, bs * 0.14, bs * 0.22), 2, 4,
                    (u, v) => (calyxCol, 1.0, u, v, 0.0, 0.0));
            }
            Primitives.Ellipsoid(part, apex + new Vec3(0, bs * 0.08, 0), new Vec3(bs * 0.4, bs * 0.18, bs * 0.4),
                3, 5, (u, v) => (calyxCol, 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
            part.EssentialSpans.Add((v0, part.VertexCount - v0));
        }

        // Wave-9: 4 creeping red stolon runners with daughter rosettes.
        var stolonCol = Primitives.Mix(leafC1, leafC2, 0.30);
        for (int s = 0; s < stolons; s++)
        {
            double az = runnerAzimuths[s];
            var outDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double L = radius * rng.Range(0.42, 0.68) * size;
            var sideDir = new Vec3(-outDir.Z, 0, outDir.X) * rng.Range(-0.18, 0.18);
            var p1 = outDir * (L * 0.28) + sideDir * L + Vec3.Up * 0.010;
            var p2 = outDir * (L * 0.64) - sideDir * (L * 0.45) + Vec3.Up * 0.006;
            var p3 = outDir * L + sideDir * (L * 0.18) + Vec3.Up * 0.003;
            Primitives.Tube(part, new[] { Vec3.Zero, p1, p2, p3 }, new[] { 0.0011, 0.0010, 0.00085, 0.00065 }, 5,
                (k, v) => (stolonCol, 1.0, k / 3.0, v, 0.0, 0.0));
            foreach (var node in new[] { p1, p2 })
                Primitives.Tube(part, new[] { node, node - Vec3.Up * 0.018 }, new[] { 0.00065, 0.00025 }, 4,
                    (k, v) => (Primitives.Mix(stolonCol, leafC1, 0.25), 1.0, k * 0.5, v, 0.0, 0.0));
            // Anchoring rootlet at the plantlet.
            var rootCol = Primitives.Mix(leafC1, leafC2, 0.15);
            Primitives.Tube(part, new[] { p3, p3 - Vec3.Up * 0.012 }, new[] { 0.0006, 0.0003 }, 4,
                (k, v) => (rootCol, 1.0, k * 0.5, v, 0.0, 0.0));
            int leaflets = 3; // 3 leaflets each, larger
            for (int lf = 0; lf < leaflets; lf++)
            {
                double faz = az + (lf - 1) * 0.7;
                var dir = NewForbsAPetioleDir(faz, rng.Range(0.55, 0.85));
                NewForbsALeaflet(part, p3, dir, height * 0.24 * size, 0.85, // plantlets match main-leaf scale
                    BroadleafOutline.Ovate, Rng.Mix(seed, (ulong)(s * 300 + lf * 17 + 71)), leafC1, leafC2,
                    rng.Range(0.5, 0.9), 0.18, 0.03, 0.005, 0.0007, detailOverride: 2,
                    midribFold: 0.08, undersideColor: underCol);
            }
        }
        NewForbsAFinish(m, part, radius, height);
    }

    /// <summary>Marshkettle: Glossy gold cups at the edge of spring pools. Round glossy leaves hold dewdrops like coins and the first gold cups open while there is still ice in the shadows.</summary>
    private static void Marshkettle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double size = juvenile ? 0.6 : 1.0;
        // Wave-4 daylight pass: bright mid-green glossy leaves, light underside.
        NewForbsAFoliage(c1, out var leafC1, out var leafC2, out var underCol);
        NewForbsACrown(part, radius, height, leafC1, leafC2);

        // Wave-9: fix long bare spoke-petioles — 10-14 large glossy kidney-cordate leaves
        // on SHORTER stout petioles across 0.15-0.60*radius Y=0.18-0.42, glistening dew.
        int leafCount = NewForbsACount(part, juvenile ? 5 : rng.NextInt(5) + 10); // 10-14 leaves
        var sheenCol = Primitives.Mix(leafC1, new double[] { 1.0, 1.0, 0.92 }, 0.22);
        var dewCol = Primitives.Mix(new[] { 0.88, 0.93, 0.96 }, leafC1, 0.15);
        for (int i = 0; i < leafCount; i++)
        {
            double az = i * (Math.PI * 2.0 / Math.Max(1, leafCount)) + rng.Range(-0.15, 0.15);
            var dir = NewForbsAPetioleDir(az, rng.Range(0.55, 1.00)); // pitched up, blades Y=0.18-0.42
            double rr = radius * rng.Range(0.15, 0.60) * size;
            var attach = new Vec3(Math.Cos(az) * rr * rng.Range(0.2, 0.5), height * rng.Range(0.03, 0.10) * size, Math.Sin(az) * rr * rng.Range(0.2, 0.5));
            double len = height * rng.Range(0.22, 0.32) * size;
            double petioleLen = radius * rng.Range(0.15, 0.30) * size; // shorter stout petioles, never 15cm bare spokes
            int leafV0 = part.VertexCount;
            Broadleaf.BuildLeaf(part, attach, dir, len, len * rng.Range(0.92, 1.00), BroadleafOutline.OrbicularCordate,
                Rng.Mix(seed, (ulong)(i * 733 + 83)), leafC1, leafC2, age: rng.Range(0.55, 0.95),
                camber: 0.30, midribFold: 0.10, wavyMargin: 0.08, wavyFrequency: 6.0, tipCurl: 0.07,
                petioleLength: petioleLen, petioleRadius: 0.0028, // stout
                petioleKink: 0.30, roll: rng.Range(-0.25, 0.25), undersideColor: underCol);
            // Dewdrop sheen: lighter vertex tint at leaf centre on the upper face.
            for (int v = leafV0; v < part.VertexCount; v++)
            {
                double face = part.UV2[v * 2]; // 0 = upper, 1 = lower
                if (face > 0.5) continue;
                double t = part.UV[v * 2]; // along blade 0..1 (Broadleaf u)
                double across = part.UV[v * 2 + 1]; // across blade 0..1 (0.5 = midrib)
                double wx = 1.0 - Math.Abs(across - 0.5) / 0.30;
                double wt = 1.0 - Math.Abs(t - 0.40) / 0.45;
                double w = Math.Max(0.0, Math.Min(wx, wt)) * 0.35;
                if (w <= 0.0) continue;
                var cur = new[] { (double)part.Colors[v * 4], (double)part.Colors[v * 4 + 1], (double)part.Colors[v * 4 + 2] };
                var mixed = Primitives.Mix(cur, sheenCol, w);
                part.SetColor(v, mixed[0], mixed[1], mixed[2], 1.0);
            }
            // 2-3 dew-drop spheres resting on the blade: junction/fwd mirror Broadleaf so the
            // beads sit on the lamina, not in the air. Small, pale, organ-flagged.
            var pDir = dir.Normalized();
            var up = Vec3.Up;
            var junction = attach + pDir * petioleLen + up * (petioleLen * 0.18);
            var bladeFwd = (pDir * Math.Cos(0.30) - up * Math.Sin(0.30)).Normalized();
            if (bladeFwd.LengthSq < 1e-6) bladeFwd = pDir;
            int dewDrops = 2 + rng.NextInt(2); // 2-3 per blade
            for (int dd = 0; dd < dewDrops; dd++)
            {
                var dewPos = junction + bladeFwd * (len * rng.Range(0.30, 0.50)) + up * 0.007
                    + new Vec3(rng.Range(-len * 0.15, len * 0.15), 0, rng.Range(-len * 0.15, len * 0.15));
                int dewV0 = part.VertexCount;
                Primitives.Ellipsoid(part, dewPos, new Vec3(0.0040, 0.0032, 0.0040), 3, 6,
                    (u, v) => (dewCol, 1.0, u, v, 0.0, 0.0));
                NewFloraMarkOrgan(part, dewV0, part.VertexCount - dewV0);
            }
        }

        // Wave-9: fix clipped central yellow blob — 7-9 wide-open (curl=0.15, not closed cones)
        // rich golden-yellow 5-sepalled cups (3.8-4.8 cm) across 0.15-0.58*radius Y=0.42-0.68.
        int flowers = NewForbsACount(part, juvenile ? 3 : rng.NextInt(3) + 7); // 7-9 blooms
        // Marigold gold pinned toward (1.00,0.80,0.06); never white.
        var centreCol = Primitives.Mix(Primitives.Scale(c2, 1.00), new double[] { 1.00, 0.80, 0.06 }, 0.80);
        var stamenCol = Primitives.Mix(Primitives.Scale(c2, 1.05), new double[] { 1.00, 0.80, 0.06 }, 0.80);
        var cupDeep = Primitives.Mix(Primitives.Scale(c2, 0.82), new double[] { 1.00, 0.80, 0.06 }, 0.80); // deep gold, saturated
        var cupTip = Primitives.Mix(Primitives.Scale(c2, 1.02), new double[] { 1.00, 0.80, 0.06 }, 0.80); // bright gold, still saturated
        for (int s = 0; s < flowers; s++)
        {
            double targetRR = radius * rng.Range(0.15, 0.58) * size;
            double saz = s * (Math.PI * 2.0 / Math.Max(1, flowers)) + rng.Range(-0.3, 0.3);
            double targetY = height * rng.Range(0.40, 0.56) * size;
            double tilt = rng.Range(0.14, 0.30);
            double proj = Math.Max(0.01, (targetY - height * 0.05 * size) / Math.Max(0.80, Math.Cos(tilt))) * Math.Sin(tilt);
            double baseR = Math.Clamp(targetRR - proj, 0.05 * radius, 0.52 * radius);
            ulong sSeed = Rng.Mix(seed, (ulong)(s * 613 + 89));
            var stalkBase = new Vec3(Math.Cos(saz) * baseR, height * 0.05 * size, Math.Sin(saz) * baseR);
            var apex = NewForbsAStalkFrom(part, sSeed, stalkBase, targetY, saz, tilt, 0.05, 0.0026, leafC1, leafC2);
            NewForbsAFlower(part, apex, Rng.Mix(seed, (ulong)(s * 97 + 83)), 5,
                height * 0.188 * size, height * 0.113 * size, 0.15, 0.05, // wide-open cup, never a closed cone
                cupDeep, cupTip, centreCol, height * 0.055 * size, 0.55,
                vein: false, veinCol: c2, veinStrength: 0, stamenRing: 6, stamenCol: stamenCol);
        }
        NewForbsAFinish(m, part, radius, height);
    }
}
