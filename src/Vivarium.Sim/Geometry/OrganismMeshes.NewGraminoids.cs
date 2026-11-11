using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New graminoids: quillsedge fountain, cloudtuft, duskbristle, bannerreed, ashwire, sootband.
/// Authored in real metres, converted with <see cref="NewFloraAppendWorld"/>.
/// Colours derive only from c1/c2.
/// </summary>
public static partial class OrganismMeshes
{
    private static void NewGramBract(MeshData part, Vec3 attach, double azimuth, double pitch, double length, double halfWidth, double[] baseCol, double[] tipCol, ulong seed)
    {
        if (length < 1e-6 || halfWidth <= 1e-9) return;
        var axis = new AxisParams(Length: length, BaseAngle: pitch, BaseAzimuth: azimuth, Droop: 0.15, Segments: 4);
        var p = new SheetParams(Axis: axis, HalfWidth: halfWidth, Thickness: 0.0004, Curl: 0.25, DetailLevel: 1);
        var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
        Sheet.Build(tmp, p, seed, baseCol, tipCol);
        int vStart = part.VertexCount;
        int iStart = part.Indices.Count;
        AppendTranslated(part, tmp, attach);
        part.RecordLeaf(vStart, iStart, attach, length);
    }

    private static void Quillsedge(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.68 : 1.0;
        // Wave-9 brightening: shader darkening dulled the fountain; ×1.4 restores it.
        double[] leafBase = Primitives.Scale(c1, 1.4);
        double[] leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.22), 1.4);
        double[] culmBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.35), 1.4);
        double[] culmTip = Primitives.Scale(c1, 1.4);

        // Wave-9 (quillsedge: arching culms following the fountain): 60-75
        // arching narrow blades (0.70-0.95h) whose outer blades pitch to ~1.75 rad and curl
        // hard (0.75-1.05) so their tips fall to the ground; inner blades stay
        // near-upright. Half-width 0.010h keeps each blade narrow/quill-like.
        int nLeaves = NewFloraCount(part, 60 + rng.NextInt(16));
        for (int i = 0; i < nLeaves; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, nLeaves) + rng.Range(-0.22, 0.22);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double age = nLeaves <= 1 ? 0.5 : i / (double)(nLeaves - 1);
            // True fountain: inner blades nearly upright, outer blades kicked
            // well out and weeping back in a long arc to the ground.
            double pitch = rng.Range(0.08, 0.30) + 1.45 * age;
            double len = h * scale * rng.Range(0.70, 0.95);
            var root = radial * rng.Range(0.0, r * 0.08) + new Vec3(0, rng.Range(0.0, 0.006), 0);
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            double halfW = h * 0.010 * rng.Range(0.8, 1.25);
            FoliageBlade.Build(part, root, tip, across, halfW, leafBase, leafTip, camber: 0.22, curl: rng.Range(0.75, 1.05), shoulder: 0.85, segments: 8);
        }

        // Wave-9 (quillsedge: arching culms + dangling Carex clusters): 7-9
        // arching culms above the fountain (culm 1.05-1.25h, lean 0.18-0.28*radius)
        // each bearing a dangling cluster of 3-4 dark-brown cylindrical spikelets
        // (3.5-4.5 cm, 3.5 mm radius, cigar-brown (0.38,0.24,0.12) to golden-tan)
        // on thin nodding pedicels — cylindrical russet bodies, not pale sheet flags.
        int nCulms = NewFloraCount(part, 7 + rng.NextInt(3));
        int nSpkPerCulm = 3 + rng.NextInt(2);
        for (int c = 0; c < nCulms; c++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * rng.Range(0.0, r * 0.12) + Vec3.Up * 0.002;
            double culmH = h * scale * rng.Range(0.74, 0.92);
            var lean = radial * (rng.Range(0.30, 0.46) * r);
            var path = new Vec3[6];
            var radii = new double[6];
            double baseR = h * 0.0045;
            for (int k = 0; k < 6; k++)
            {
                double t = k / 5.0;
                path[k] = foot + Vec3.Up * (culmH * (1.25 * t - 0.25 * t * t)) + lean * (t * t);
                radii[k] = baseR * (1.0 - 0.60 * t);
            }
            Primitives.Tube(part, path, radii, 6, (ii, v) => (Primitives.Mix(culmBase, culmTip, (double)ii / 5.0), 1, (double)ii / 5.0, v, 0, 0));

            Vec3 CulmPoint(double t)
            {
                t = Math.Clamp(t, 0.0, 1.0);
                double f = t * (path.Length - 1);
                int idx = Math.Min(path.Length - 2, (int)f);
                return Vec3.Lerp(path[idx], path[idx + 1], f - idx);
            }

            // Weeping pendulous Carex pendula catkins staggered along the upper arching culm
            double[] spikeBase = new[] { 0.38, 0.24, 0.12 };
            double[] spikeTip = new[] { 0.72, 0.55, 0.28 };
            for (int k = 0; k < nSpkPerCulm; k++)
            {
                double saz = az + rng.Range(-0.85, 0.85);
                var sradial = new Vec3(Math.Cos(saz), 0, Math.Sin(saz));
                double t = 0.78 + 0.22 * k / (double)Math.Max(1, nSpkPerCulm - 1);
                var at = CulmPoint(t);
                double spLen = rng.Range(0.050, 0.068) * scale;
                double pedLen = rng.Range(0.012, 0.020);
                var pedDir = (sradial * 0.70 - Vec3.Up * 0.35).Normalized();
                var pedTip = at + pedDir * pedLen;
                Primitives.Tube(part, new[] { at, pedTip }, new[] { 0.00065, 0.00050 }, 4,
                    (ii, v) => (culmTip, 1, (double)ii, v, 0, 0));

                var s0 = pedTip;
                var s1 = pedTip + sradial * (spLen * 0.22) - Vec3.Up * (spLen * 0.28);
                var s2 = pedTip + sradial * (spLen * 0.32) - Vec3.Up * (spLen * 0.62);
                var s3 = pedTip + sradial * (spLen * 0.35) - Vec3.Up * (spLen * 0.98);
                var sPath = new[] { s0, s1, s2, s3 };
                var sRadii = new[] { 0.0020, 0.0027, 0.0022, 0.0010 };
                int v0Spike = part.VertexCount;
                Primitives.Tube(part, sPath, sRadii, 6,
                    (ii, v) => (Primitives.Mix(spikeBase, spikeTip, (double)ii / 3.0), 1, (double)ii / 3.0, v, 0, 0));
                NewFloraMarkOrgan(part, v0Spike, part.VertexCount - v0Spike);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    private static void Cloudtuft(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.7 : 1.0;
        // Wave-9 brightening: shader darkening dulled the greens; ×1.4 restores them.
        double[] stemCol = Primitives.Scale(c1, 1.4);
        // Dark bog-green tuft so the white cotton clouds pop at both distances.
        double[] leafBase = new[] { 0.10, 0.24, 0.12 };
        double[] leafTip = new[] { 0.18, 0.34, 0.18 };
        // Pure white cotton: the tuft is c2 outright, base to tip, so every
        // hair reads at full brightness from any angle.
        double[] hairCol = new[] { 0.96, 0.96, 0.94 };
        double[] hairBaseCol = new[] { 0.96, 0.96, 0.94 };
        double[] puffCol = new[] { 0.96, 0.96, 0.94 };

        // Basal foliage and culm roots now define the mire cushion without a detached disc.

        // Fuller tussock of 24-30 dark bog-green basal blades so the white
        // cotton clouds pop against a solid green base.
        int nBlades = NewFloraCount(part, 18 + rng.NextInt(7));
        for (int i = 0; i < nBlades; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, nBlades) + rng.Range(-0.3, 0.3);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double pitch = rng.Range(0.10, 0.35);
            double len = h * scale * rng.Range(0.32, 0.50);
            var root = radial * rng.Range(0.0, r * 0.30) + Vec3.Up * 0.002;
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            FoliageBlade.Build(part, root, tip, across, h * 0.008 * rng.Range(0.8, 1.2), leafBase, leafTip, camber: 0.12, curl: 0.10, shoulder: 0.85, segments: 6);
        }

        // A few short basal leaves hug the base so the tuft does not read as bare stems.
        int nBasal = NewFloraCount(part, 6 + rng.NextInt(4));
        for (int i = 0; i < nBasal; i++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double pitch = rng.Range(0.18, 0.45);
            double len = h * scale * rng.Range(0.14, 0.26);
            var root = radial * rng.Range(0.0, r * 0.35) + Vec3.Up * 0.002;
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            FoliageBlade.Build(part, root, tip, across, h * 0.007 * rng.Range(0.8, 1.2), leafBase, leafTip, camber: 0.10, curl: 0.08, shoulder: 0.85, segments: 6);
        }

        // Wave-9: culms raised to 0.55-0.85*height so the cotton clouds sit
        // clearly above the fuller tussock and read at wide distance, but
        // clustered so the stand stays dense.
        int nCulms = NewFloraCount(part, 10 + rng.NextInt(3));
        var tops = new List<(Vec3 top, double culmH)>();
        for (int k = 0; k < nCulms; k++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * (Math.Sqrt(Math.Max(0, Math.Min(1, rng.NextDouble()))) * r * 0.40) + Vec3.Up * 0.002;
            double culmH = h * scale * rng.Range(0.44, 0.66);
            var acrossLean = new Vec3(-radial.Z, 0, radial.X) * rng.Range(-0.010, 0.010);
            var lean = radial * rng.Range(0.025, 0.052) + acrossLean;
            var top = foot + Vec3.Up * culmH + lean;
            var path = new Vec3[4];
            for (int j = 0; j < 4; j++)
            {
                double t = j / 3.0;
                path[j] = foot + Vec3.Up * (culmH * t) + lean * (t * t) + acrossLean * (Math.Sin(Math.PI * t) * 0.35);
            }
            // Wave-4: ~1 mm stems (diameter), not 3 mm poles.
            var radii = new double[] { 0.00165, 0.00125, 0.00082, 0.00042 };
            Primitives.Tube(part, path, radii, 6, (ii, v) => (stemCol, 1, (double)ii / 3.0, v, 0, 0));
            tops.Add((top, culmH));
        }

        // Small connected spikelets carry a dense middle layer and a loose fiber fringe.
        int nTufts = NewFloraCount(part, 6);
        tops.Sort((a, b) => b.culmH.CompareTo(a.culmH));
        int hairs = (m.FloraDetailLevel ?? 0) switch { 0 => 96, 1 => 88, _ => 76 };
        int nTuftUse = Math.Min(nTufts, tops.Count);
        for (int t = 0; t < nTuftUse; t++)
        {
            // Head the tallest culms so bare sterile culms stay below the
            // cloud; adjacent heights keep it coherent without flattening it.
            var centre = tops[t].top;
            // Small green bracts make the cotton head visibly join its culm.
            var junction = centre - Vec3.Up * 0.006;
            var junctionCol = Primitives.Mix(leafBase, leafTip, 0.62);
            int jv = part.VertexCount;
            Primitives.Ellipsoid(part, junction, new Vec3(0.0038, 0.0070, 0.0038), 3, 6,
                (u, v) => (junctionCol, 1.0, u, v, 0.0, 0.0));
            part.RecordLeaf(jv, part.Indices.Count, junction, 0.007, volumetric: true);
            double cream = rng.Range(0.0, 1.0);
            var tuftHairBase = Primitives.Mix(new[] { 0.84, 0.82, 0.74 }, new[] { 0.93, 0.91, 0.84 }, cream);
            var tuftHairTip = Primitives.Mix(new[] { 0.91, 0.89, 0.81 }, new[] { 0.99, 0.98, 0.92 }, cream);
            // Three overlapping spikelets carry bristles at different heights.
            const int spikelets = 3;
            for (int p = 0; p < spikelets; p++)
            {
                double pr = rng.Range(0.0038, 0.0048);
                double az = rng.Range(0, 2 * Math.PI);
                double offset = rng.Range(0.001, 0.0025);
                var pc = centre + new Vec3(Math.Cos(az) * offset, 0.004 * p, Math.Sin(az) * offset);
                var lobeCol = Primitives.Mix(new[] { 0.92, 0.89, 0.79 }, new[] { 0.99, 0.97, 0.90 }, Math.Clamp(cream + rng.Range(-0.18, 0.18), 0.0, 1.0));
                int v0 = part.VertexCount;
                Primitives.Ellipsoid(part, pc, new Vec3(pr, pr * rng.Range(0.84, 1.10), pr * rng.Range(0.82, 1.08)), 3, 6,
                    (u, v) => (lobeCol, 1.0, u, v, 0.0, 0.0));
                part.RecordLeaf(v0, part.Indices.Count, pc, pr, volumetric: true);
            }
            int innerHairs = (hairs * 2) / 3;
            for (int hc = 0; hc < hairs; hc++)
            {
                double az = hc * 2.399963 + rng.Range(-0.25, 0.25);
                var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                bool inner = hc < innerHairs;
                double len = inner ? rng.Range(0.008, 0.015) : rng.Range(0.019, 0.030);
                var dir = inner
                    ? (radial * rng.Range(0.35, 0.80) + Vec3.Up * rng.Range(0.05, 0.90)).Normalized()
                    : (radial * rng.Range(0.42, 0.72) + Vec3.Up * rng.Range(0.72, 1.00)).Normalized();
                double rootHeight = rng.Range(0.001, 0.010);
                var hairRoot = centre + radial * rng.Range(-0.003, 0.003) + Vec3.Up * rootHeight;
                var tip = hairRoot + dir * len;
                double rootRadius = inner ? 0.00058 : 0.00030;
                double tipRadius = inner ? 0.00020 : 0.00007;
                Primitives.Tube(part, new[] { hairRoot, tip }, new[] { rootRadius, tipRadius }, 6, (ii, v) => (Primitives.Mix(tuftHairBase, tuftHairTip, ii == 0 ? 0.0 : 1.0), 1, (double)ii, v, 0, 0));
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    private static void Duskbristle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.7 : 1.0;
        // Desaturated grey-green, pulled well toward c2 so the tuft reads
        // ashy woodland floor, not saturated lawn green. Wave-4 ×1.4 restores
        // brightness lost to shader darkening without adding saturation.
        // Wave-6: hair colour is lifted further toward pale c2 (×1.75) so the
        // marginal fringe reads pale-shaggy against the leaf instead of
        // same-tone litter (review: tuft invisible).
        double[] leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.35), 1.4);
        double[] leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.52), 1.4);
        // Wave-8: hair is near-white c2 (mix 0.90, x1.30) so the marginal
        // fringe reads silvery-white against the grey-green blade.
        double[] hairCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.90), 1.30);
        double[] stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.48), 1.4);
        int detail = m.FloraDetailLevel ?? 0;

        // Wave-9 (duskbristle: lush ground-hugging rosette): 32-40 broader
        // (8-11 mm wide) curving grey-green blades (0.38-0.58h, pitch 0.65-1.05)
        // so the tuft spreads low and wide; silvery-white marginal fringe below.
        int nBlades = NewFloraCount(part, 32 + rng.NextInt(9));
        var bladeGeom = new List<(Vec3 root, Vec3 tip, Vec3 across, double halfW)>();
        for (int i = 0; i < nBlades; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, nBlades) + rng.Range(-0.18, 0.18);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double pitch = rng.Range(0.65, 1.05);
            double len = h * scale * rng.Range(0.38, 0.58);
            var root = radial * rng.Range(0.0, r * 0.45) + Vec3.Up * rng.Range(0.0, 0.004);
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            double halfW = rng.Range(0.004, 0.0055);
            FoliageBlade.Build(part, root, tip, across, halfW, leafBase, leafTip, camber: 0.14, curl: rng.Range(0.55, 0.90), shoulder: 0.85, segments: 8);
            bladeGeom.Add((root, tip, across, halfW));
        }

        // True marginal fringe (Wave-8: ~15 per blade, t 0.08-0.97 on both
        // margins) run along both blade edges, angled a little tipward.
        foreach (var bg in bladeGeom)
        {
            int perBase = 15 + rng.NextInt(3);
            int nHair = detail switch { 0 => perBase, 1 => Math.Max(8, perBase - 2), _ => Math.Max(6, perBase - 5) };
            var along = (bg.tip - bg.root).Normalized();
            for (int hc = 0; hc < nHair; hc++)
            {
                double t = rng.Range(0.12, 0.97);
                double sign = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                var at = Vec3.Lerp(bg.root, bg.tip, t) + bg.across * (bg.halfW * 0.90 * sign) + Vec3.Up * (bg.halfW * 0.15);
                double hLen = rng.Range(0.014, 0.026);
                var hDir = (bg.across * (0.48 * sign) + Vec3.Up * 0.22 + along * 0.30).Normalized();
                var hTip = at + hDir * hLen;
                Primitives.Tube(part, new[] { at, hTip }, new[] { 0.00085, 0.00022 }, 5, (ii, v) => (hairCol, 1, (double)ii, v, 0, 0));
            }
        }

        // Wave-9: 5-6 slender diverging stalks (0.45-0.70h, leaning outward
        // 25-40 deg = 0.44-0.70 rad) each carrying an open airy cyme of 4-5
        // small chestnut-brown starry flower clusters on thin pedicels, so the
        // flower reads as an open branching cyme, not a black lump.
        int nCulms = NewFloraCount(part, 5 + rng.NextInt(2));
        double[] flowerCol = new[] { 0.36, 0.20, 0.10 };
        double[] flowerTip = new[] { 0.52, 0.33, 0.16 };
        for (int c = 0; c < nCulms; c++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * rng.Range(0.0, r * 0.15) + Vec3.Up * 0.002;
            double culmH = h * scale * rng.Range(0.45, 0.70);
            double leanAng = rng.Range(0.44, 0.70);
            var lean = radial * (culmH * Math.Tan(leanAng));
            var top = foot + Vec3.Up * culmH + lean;
            var path = new Vec3[4];
            for (int j = 0; j < 4; j++)
            {
                double t = j / 3.0;
                path[j] = Vec3.Lerp(foot, top, t) + radial * (Math.Sin(Math.PI * t) * 0.004);
            }
            Primitives.Tube(part, path, new[] { 0.0017, 0.0015, 0.0012, 0.0009 }, 6, (ii, v) => (stemCol, 1, (double)ii / 3.0, v, 0, 0));
            int nClusters = 4 + rng.NextInt(2);
            for (int k = 0; k < nClusters; k++)
            {
                double saz = rng.Range(0, 2 * Math.PI);
                var sradial = new Vec3(Math.Cos(saz), 0, Math.Sin(saz));
                double t = k == 0 ? 1.0 : rng.Range(0.88, 1.0);
                var at = Vec3.Lerp(foot, top, t);
                var branchDir = (sradial * 1.0 + Vec3.Up * rng.Range(0.20, 0.60)).Normalized();
                var branchTip = at + branchDir * rng.Range(0.012, 0.020);
                Primitives.Tube(part, new[] { at, branchTip }, new[] { 0.0009, 0.0007 }, 5, (ii, v) => (stemCol, 1, (double)ii, v, 0, 0));
                // Small starry cluster: a tiny chestnut ellipsoid puff plus 3 short rays.
                double fr = rng.Range(0.006, 0.008);
                int v0 = part.VertexCount;
                var clusterCentre = branchTip + branchDir * (fr * 0.55); // SP11: seed head seated on the pedicel tip, not floating
                var clusterRadii = new Vec3(fr * rng.Range(0.82, 1.12), fr * rng.Range(0.62, 0.88), fr * rng.Range(0.72, 1.08));
                Primitives.Ellipsoid(part, clusterCentre, clusterRadii, 3, 7,
                    (u, v) => (Primitives.Mix(flowerCol, flowerTip, u), 1.0, u, v, 0.0, 0.0), pitch: rng.Range(-0.60, 0.60));
                part.RecordLeaf(v0, part.Indices.Count, clusterCentre, fr, volumetric: true);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    private static void Bannerreed(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.7 : 1.0;
        // Wave-4 brightening: ×1.4 greens; straw is lifted ×1.15 so the
        // panicle hangs pale after shader darkening.
        double[] leafBase = Primitives.Scale(c1, 1.4);
        double[] leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.28), 1.4);
        // Straw colour stronger: branchlets and bracts lean hard on c2 so
        // the panicle hangs pale against the green blades.
        double[] branchCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.92), 1.15);
        double[] bractBase = Primitives.Scale(Primitives.Mix(c2, c1, 0.06), 1.15);
        double[] bractTip = Primitives.Scale(c2, 1.15);
        double[] midribCol = Primitives.Mix(c1, c2, 0.80);
        int detail = m.FloraDetailLevel ?? 0;

        void ApplyMidribStripe(int vStart)
        {
            for (int v = vStart; v < part.VertexCount; v++)
            {
                double uvY = part.UV[v * 2 + 1];
                double d = Math.Abs(uvY - 0.5) * 2.0;
                if (d >= 0.36) continue;
                double strength = (1.0 - d / 0.36) * 0.35;
                double cr = part.Colors[v * 4], cg = part.Colors[v * 4 + 1], cb = part.Colors[v * 4 + 2], ca = part.Colors[v * 4 + 3];
                var mixed = Primitives.Mix(new[] { cr, cg, cb }, midribCol, strength);
                part.SetColor(v, mixed[0], mixed[1], mixed[2], ca);
            }
        }

        // Basal reed blades arching naturally around the clump
        int nLeaves = NewFloraCount(part, 12 + rng.NextInt(7));
        for (int i = 0; i < nLeaves; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, nLeaves) + rng.Range(-0.20, 0.20);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double pitch = rng.Range(0.40, 0.70);
            double len = h * scale * rng.Range(0.30, 0.48);
            var root = radial * rng.Range(0.0, r * 0.50) + Vec3.Up * 0.003;
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            double halfW = h * 0.015 * rng.Range(0.85, 1.15);
            int vStart = part.VertexCount;
            FoliageBlade.Build(part, root, tip, across, halfW, leafBase, leafTip, camber: 0.20, curl: rng.Range(0.45, 0.65), shoulder: 0.80, segments: 8);
            ApplyMidribStripe(vStart);
        }

        // Arching culms with nodal swellings, cauline blades, and nodding feathery banner panicles
        double[] branchStations = detail switch
        {
            0 => new[] { 0.0, 0.30, 0.55, 0.80, 1.0 },
            1 => new[] { 0.0, 0.35, 0.70, 1.0 },
            _ => new[] { 0.0, 0.50, 1.0 }
        };
        int glumeCount = detail switch { 0 => 3, 1 => 2, _ => 1 };
        int nCulms = NewFloraCount(part, 5 + rng.NextInt(3));
        for (int c = 0; c < nCulms; c++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * rng.Range(0.0, r * 0.15) + Vec3.Up * 0.003;
            double culmH = h * scale * rng.Range(0.78, 0.96);
            double panLen = h * scale * rng.Range(0.18, 0.26);
            var culmLean = radial * (h * scale * rng.Range(0.08, 0.16));
            var culmTop = foot + Vec3.Up * culmH + culmLean;

            const int culmStations = 13;
            var cpath = new Vec3[culmStations];
            var cradii = new double[culmStations];
            double cBase = Math.Max(0.0022, h * 0.0042);
            double[] nodeTs = { 0.28, 0.48, 0.68, 0.84 };

            for (int j = 0; j < culmStations; j++)
            {
                double t = j / (double)(culmStations - 1);
                cpath[j] = foot + Vec3.Up * (culmH * t) + culmLean * Math.Pow(t, 1.8);
                double rTaper = cBase * (1.0 - 0.45 * t);
                double swell = 0.0;
                for (int ni = 0; ni < nodeTs.Length; ni++)
                {
                    double dt = Math.Abs(t - nodeTs[ni]);
                    if (dt < 0.055)
                        swell = Math.Max(swell, (1.0 - dt / 0.055) * 0.32);
                }
                cradii[j] = rTaper * (1.0 + swell);
            }
            Primitives.Tube(part, cpath, cradii, 6, (ii, v) => (Primitives.Mix(leafBase, branchCol, (double)ii / (culmStations - 1)), 1, (double)ii / (culmStations - 1), v, 0, 0));

            // Cauline reed blades emerging at nodes along the culm
            for (int ni = 0; ni < nodeTs.Length; ni++)
            {
                if (detail >= 2 && ni != 1) continue;
                int nodeIdx = (int)Math.Round(nodeTs[ni] * (culmStations - 1));
                var nodePos = cpath[nodeIdx];
                double leafAz = az + (ni % 2 == 0 ? 0.75 : -0.75) + rng.Range(-0.15, 0.15);
                var cRadial = new Vec3(Math.Cos(leafAz), 0, Math.Sin(leafAz));
                var cAcross = new Vec3(-Math.Sin(leafAz), 0, Math.Cos(leafAz));
                double cLeafLen = h * scale * rng.Range(0.24, 0.38) * (1.0 - 0.12 * ni);
                double cHalfW = h * 0.012 * rng.Range(0.85, 1.15);
                double cPitch = rng.Range(0.50, 0.80);
                var cDir = (cRadial * Math.Sin(cPitch) + Vec3.Up * Math.Cos(cPitch)).Normalized();
                var cTip = nodePos + cDir * cLeafLen;
                int vStartC = part.VertexCount;
                FoliageBlade.Build(part, nodePos, cTip, cAcross, cHalfW, leafBase, leafTip, camber: 0.20, curl: rng.Range(0.40, 0.65), shoulder: 0.80, segments: 6);
                ApplyMidribStripe(vStartC);
            }

            // One-sided silky nodding plume panicle at the top of each culm
            var rachisBase = culmTop;
            double panFace = az + rng.Range(-0.35, 0.35);
            var plumeDir = new Vec3(Math.Cos(panFace), 0, Math.Sin(panFace));
            var rp0 = rachisBase;
            var rp1 = rachisBase + Vec3.Up * (panLen * 0.34) + plumeDir * (panLen * 0.22);
            var rp2 = rachisBase + Vec3.Up * (panLen * 0.50) + plumeDir * (panLen * 0.48);
            var rp3 = rachisBase + Vec3.Up * (panLen * 0.28) + plumeDir * (panLen * 0.82);
            const int rachisStations = 9;
            var rpath = new Vec3[rachisStations];
            var rRadii = new double[rachisStations];
            for (int i = 0; i < rachisStations; i++)
            {
                double t = i / (double)(rachisStations - 1);
                rpath[i] = BezierCubic(rp0, rp1, rp2, rp3, t);
                rRadii[i] = MathD.Lerp(0.0018, 0.0006, t);
            }
            Primitives.Tube(part, rpath, rRadii, 5, (i, v) => (branchCol, 1, i / (double)(rachisStations - 1), v, 0, 0));

            int nSpk = NewFloraCount(part, 21 + rng.NextInt(9));
            for (int k = 0; k < nSpk; k++)
            {
                double t = nSpk <= 1 ? 0.5 : k / (double)(nSpk - 1);
                double station = t * (rachisStations - 1);
                int segment = Math.Min((int)station, rachisStations - 2);
                var at = Vec3.Lerp(rpath[segment], rpath[segment + 1], station - segment);
                double reach = panLen * (0.45 - 0.25 * t) * rng.Range(0.80, 1.15);
                double baz = panFace + rng.Range(-0.45, 0.45);
                var bradial = new Vec3(Math.Cos(baz), 0, Math.Sin(baz));

                var b0 = at;
                var b1 = at + (bradial * 0.38 + Vec3.Up * rng.Range(0.04, 0.10)) * (reach * 0.35);
                var b2 = at + (bradial * 0.75 - Vec3.Up * rng.Range(0.12, 0.22)) * (reach * 0.70);
                var b3 = at + (bradial * 0.95 - Vec3.Up * rng.Range(0.45, 0.75)) * reach;
                // Place the glumes on the sampled branch itself so every spikelet stays attached.
                var bPath = new Vec3[branchStations.Length];
                var bRadii = new double[branchStations.Length];
                for (int i = 0; i < branchStations.Length; i++)
                {
                    bPath[i] = BezierCubic(b0, b1, b2, b3, branchStations[i]);
                    bRadii[i] = MathD.Lerp(0.0009, 0.0003, branchStations[i]);
                }
                Primitives.Tube(part, bPath, bRadii, 4, (i, v) => (branchCol, 1, branchStations[i], v, 0, 0));

                for (int g = 0; g < glumeCount; g++)
                {
                    int i = g + 2;
                    var gAttach = bPath[i];
                    var gTang = bPath[Math.Min(i + 1, bPath.Length - 1)] - bPath[i - 1];
                    if (gTang.LengthSq < 1e-8) gTang = bradial + Vec3.Down * 0.5;

                    double bLen = rng.Range(0.018, 0.032);
                    double bHw = bLen * 0.14;
                    var spread = bradial.Cross(Vec3.Up) * rng.Range(-0.18, 0.18);
                    var gDir = (gTang.Normalized() * 0.60 + Vec3.Down * rng.Range(0.60, 0.80) + spread).Normalized();
                    var gTip = gAttach + gDir * bLen;
                    var gSide = gDir.Cross(Vec3.Up);
                    if (gSide.LengthSq < 1e-8) gSide = bradial.Cross(Vec3.Up);
                    Primitives.CurvedLeaf(part, gAttach, gTip, gSide, bHw, bractBase, bractTip, camber: bLen * 0.10, longitudinal: 3);
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// Ashwire: wiry grey-green turf with NO solid base. Fine bristle blades
    /// 12-20 cm tall and 0.5-1 mm wide, stiffly upright with a slight arch,
    /// over a dense wiry base tuft, plus 2-4 small silvering seed heads.
    /// Wired in the dispatch (<c>"ashwire"</c>) — this is the builder the
    /// species calls.
    /// </summary>
    private static void Ashwire(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.7 : 1.0;
        // SP2: a fountain tussock of fine grey-green blades (arching, tips silver), a straw thatch of old
        // blades at the crown, and nodding silver seed plumes standing clear of the leaf mass.
        double[] leafBase = new[] { 0.20, 0.40, 0.12 };
        double[] leafTip = Primitives.Mix(new[] { 0.62, 0.74, 0.38 }, c2, 0.35);
        double[] thatchBase = new[] { 0.40, 0.34, 0.22 };
        double[] thatchTip = new[] { 0.66, 0.60, 0.42 };
        double[] stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.55), 1.2);
        double[] seedBase = Primitives.Scale(Primitives.Mix(c2, new[] { 0.80, 0.78, 0.66 }, 0.5), 1.05);
        double[] seedTip = Primitives.Scale(c2, 1.15);

        part.FloraDetailLevel = 2; // blade grid: 8 rows x 4 cols per blade
        int nBlades = NewFloraCount(m, 84 + rng.NextInt(7));
        for (int i = 0; i < nBlades; i++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double k = rng.NextDouble();
            double pitch = 0.10 + 0.95 * Math.Pow(k, 0.85);             // inner blades stand, outer ones fountain out
            double len = h * scale * (0.50 + 0.50 * (1.0 - 0.55 * k) * rng.Range(0.85, 1.0));
            var root = radial * (Math.Sqrt(rng.NextDouble()) * r * 0.10) + Vec3.Up * rng.Range(0.0, 0.003);
            var dir = (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            var tip = root + dir * len;
            double halfW = rng.Range(0.0020, 0.0030) * Math.Max(0.7, h / 0.18);
            FoliageBlade.Build(part, root, tip, across, halfW, leafBase, leafTip, camber: 0.10, curl: rng.Range(0.30, 0.60), shoulder: 0.80, segments: 6);
        }

        int nThatch = 0;
        for (int i = 0; i < nThatch; i++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            double pitch = rng.Range(0.9, 1.35);
            double len = h * scale * rng.Range(0.18, 0.32);
            var root = radial * (rng.Range(0.01, 0.07) * r) + Vec3.Up * 0.001;
            var tip = root + (radial * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized() * len;
            FoliageBlade.Build(part, root, tip, across, rng.Range(0.0016, 0.0024), thatchBase, thatchTip, camber: 0.08, curl: 0.2, shoulder: 0.85, segments: 4);
        }

        int nSeeds = NewFloraCount(m, 7 + rng.NextInt(3));
        for (int c = 0; c < nSeeds; c++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * rng.Range(0.0, r * 0.12) + Vec3.Up * 0.002;
            double culmH = h * scale * rng.Range(1.0, 1.30);
            var lean = radial * (culmH * rng.Range(0.14, 0.30));
            var path = new Vec3[4];
            for (int j = 0; j < 4; j++)
            {
                double t = j / 3.0;
                path[j] = foot + Vec3.Up * (culmH * t) + lean * (t * t);
            }
            Primitives.Tube(part, path, new[] { 0.00130, 0.00105, 0.00085, 0.00065 }, 6, (ii, v) => (stemCol, 1, (double)ii / 3.0, v, 0, 0));
            var top = path[3];
            var nodDir = (radial * 0.75 + Vec3.Up * 0.55).Normalized();
            double spLen = rng.Range(0.040, 0.052);
            Primitives.Tube(part, new[] { top, top + nodDir * (spLen * 0.5), top + nodDir * spLen }, new[] { 0.0030, 0.0036, 0.0006 }, 6,
                (ii, v) => (Primitives.Mix(seedBase, seedTip, (double)ii / 2.0), 1, (double)ii / 2.0, v, 0, 0));
            // Two short side spikelets for a feathery plume.
            for (int sb = 0; sb < 2; sb++)
            {
                double sgn = sb == 0 ? 1 : -1;
                var sdir = (nodDir + new Vec3(-radial.Z, 0, radial.X) * (0.7 * sgn)).Normalized();
                var sbase = top + nodDir * (spLen * 0.35);
                Primitives.Tube(part, new[] { sbase, sbase + sdir * 0.018 }, new[] { 0.0020, 0.0005 }, 6,
                    (ii, v) => (seedBase, 1, (double)ii, v, 0, 0));
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    private static void NewGramSootbandShaft(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii, double[] nodeTs,
        double[] shaftBase, double[] shaftTip, double[] bandCol, double[] grooveCol, double[] ridgeCol, int sides, int ribs, double ribDepth, double bandWidth,
        bool capTip)
    {
        if (path.Count < 2) return;
        int effSides = sides;
        if (m.Lod is { } lod) effSides = lod.Sides(sides);
        effSides = Math.Max(6, effSides);
        int start = m.VertexCount;

        var tangents = new Vec3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == path.Count - 1) d = path[^1] - path[^2];
            else d = path[i + 1] - path[i - 1];
            if (d.LengthSq < 1e-14) d = Vec3.Up;
            tangents[i] = d.Normalized();
        }

        var side = tangents[0].Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        double maxR = 0;
        for (int i = 0; i < path.Count; i++)
        {
            double t = i / (double)(path.Count - 1);
            // Black band at every joint: blend toward near-black inside the node band.
            double band = 0;
            foreach (double nt in nodeTs)
            {
                double d = Math.Abs(t - nt);
                if (d < bandWidth)
                {
                    double k = 1.0 - d / bandWidth;
                    band = Math.Max(band, MathD.SmoothStep(0, 1, k));
                }
            }
            var baseCol = Primitives.Mix(shaftBase, shaftTip, t);
            for (int s = 0; s <= effSides; s++)
            {
                double th = 2 * Math.PI * s / effSides;
                double rib = 0.5 + 0.5 * Math.Cos(ribs * th);
                double rr = radii[i] * (1.0 - ribDepth * (1.0 - rib));
                var radial = (side * Math.Cos(th) + side.Cross(tangents[i]).Normalized() * Math.Sin(th)).Normalized();
                var ribCol = Primitives.Mix(grooveCol, baseCol, Math.Min(1.0, rib * 2.0));
                ribCol = Primitives.Mix(ribCol, ridgeCol, Math.Max(0.0, rib * 2.0 - 1.0));
                var col = Primitives.Mix(ribCol, bandCol, band);
                m.AddVertex(path[i] + radial * rr, radial, col, 1, t, (double)s / effSides, 0, 0);
            }
            if (i < path.Count - 1)
            {
                var dir = tangents[i + 1];
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-10)
                {
                    side = dir.Cross(Vec3.Up);
                    if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            maxR = Math.Max(maxR, Math.Abs(radii[i]));
        }
        // Recompute up vectors for the second pass triangles using the stored ring layout:
        // vertices are laid out ring-major, so index math matches Primitives.Tube.
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < effSides; s++)
            {
                int a = start + i * (effSides + 1) + s, b = a + effSides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        m.Structural.Add((start, m.VertexCount - start, effSides, maxR));
        if (capTip)
        {
            int rim = start + (path.Count - 1) * (effSides + 1);
            int tip = m.VertexCount;
            m.AddVertex(path[^1], tangents[^1], shaftTip, 1, 1, 0.5, 0, 0);
            for (int s = 0; s < effSides; s++)
                Primitives.TriangleFacing(m, rim + s, rim + s + 1, tip, tangents[^1]);
        }
    }

    private static void NewGramSootbandCollar(MeshData m, Vec3 centre, double shaftR, double nodeR, double[] bandCol, int detail)
    {
        // Tiny sheath-teeth collar: a short skirt flaring at the joint, its top
        // edge zigzagged into teeth.
        int sides = detail == 2 ? 8 : 10;
        double[] ringY = new[] { -0.008, 0.001, 0.009 };
        double toothBaseR = nodeR * 1.10;
        double[] ringR = new[] { shaftR * 1.02, nodeR * 1.12, toothBaseR };
        int start = m.VertexCount;
        for (int ri = 0; ri < 3; ri++)
        {
            for (int s = 0; s <= sides; s++)
            {
                double th = 2 * Math.PI * s / sides;
                var radial = new Vec3(Math.Cos(th), 0, Math.Sin(th));
                double y = ringY[ri];
                if (ri == 2 && s % 2 == 0) y += 0.0025;
                var p = centre + radial * ringR[ri] + Vec3.Up * y;
                m.AddVertex(p, radial, bandCol, 1, ri * 0.5, (double)s / sides, 0, 0);
            }
        }
        for (int ri = 0; ri < 2; ri++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + ri * (sides + 1) + s, b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    private static void NewGramSootbandCone(MeshData m, Vec3 tip, Vec3 axis, double shaftTipR, double[] coneCol, double[] coneShade, int detail, double sizeScale, double taper)
    {
        // Uneven tapered cones retain the pale seed-head trait without a
        // repeated stack of identical blunt rings.
        int nRings = detail == 0 ? 4 + (sizeScale > 1.0 ? 1 : 0) : 3 + (sizeScale > 1.0 ? 1 : 0);
        int sides = detail == 0 ? 14 : 10;
        double baseR = Math.Clamp(shaftTipR * 2.6 * sizeScale, 0.0055, 0.010);
        double ringH = 0.0065 * sizeScale;
        var path = new Vec3[nRings * 2];
        var radii = new double[path.Length];
        for (int k = 0; k < nRings; k++)
        {
            double rk = baseR * Math.Pow(taper, k);
            path[k * 2] = tip + axis * (k * ringH);
            path[k * 2 + 1] = tip + axis * ((k + 0.78) * ringH);
            radii[k * 2] = rk;
            radii[k * 2 + 1] = rk * (k == nRings - 1 ? 0.42 : taper * 0.88);
        }
        Primitives.Tube(m, path, radii, sides,
            (i, v) => (i % 2 == 0 ? coneCol : coneShade, 1, i / (double)(path.Length - 1), v, 0, 0));
        // Blunt cap fan over the top ring.
        var capBase = path[^1];
        double capR = radii[^1];
        var centre = capBase + axis * 0.0015;
        var side = axis.Cross(new Vec3(0, 0, 1)).Normalized();
        var depth = side.Cross(axis);
        var rim = new List<Vec3>();
        for (int s = 0; s < sides; s++)
        {
            double th = 2 * Math.PI * s / sides;
            rim.Add(capBase + (side * Math.Cos(th) + depth * Math.Sin(th)) * capR);
        }
        Primitives.Fan(m, centre, rim, axis, coneCol, coneCol);
    }

    private static void Sootband(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = Math.Max(0.02, height);
        double r = Math.Max(0.02, radius);
        double scale = juvenile ? 0.7 : 1.0;
        // Wave-8 (sootband): dark soot-green ribbed shafts (c1 dark), stark
        // jet-black nodal bands from c1 at 0.06x, pale tooth-sheath rims
        // from c2 cream, pale-ochre terminal cones from c2.
        double[] shaftBase = c1;
        double[] shaftTip = Primitives.Mix(c1, c2, 0.20);
        double[] grooveCol = Primitives.Scale(c1, 0.35);
        double[] ridgeCol = Primitives.Mix(c1, c2, 0.38);
        double[] bandCol = Primitives.Mix(c1, c2, 0.08);
        double[] collarCol = Primitives.Mix(c1, c2, 0.58);
        double[] coneCol = Primitives.Mix(c2, c1, 0.18);
        double[] coneShade = Primitives.Mix(c2, c1, 0.34);
        int detail = m.FloraDetailLevel ?? 0;

        // Wave-9: variable-height unbranched shafts with gently bowed axes
        // 0.15-0.55*radius (varied heights 0.55-1.0h, radius 4.5-6.0 mm) so it
        // reads as a clustered, uneven colony; a few fertile stalks carry plump
        // pale-ochre strobilus cones.
        int nStems = NewFloraCount(part, 14 + rng.NextInt(3));
        int nFertile = 5 + rng.NextInt(2);
        nFertile = Math.Min(nFertile, nStems);
        int sides = detail switch { 0 => 22, 1 => 18, _ => 14 };
        int ribs = detail switch { 0 => 11, 1 => 10, _ => 8 };

        for (int si = 0; si < nStems; si++)
        {
            bool fertile = si < nFertile;
            double az = rng.Range(0, 2 * Math.PI);
            var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var foot = radial * rng.Range(0.15, 0.55) * r;
            double stemH = h * scale * rng.Range(0.48, 0.96);
            int nodes = 6 + rng.NextInt(3);
            int segs = nodes * 4 + 1;
            var path = new Vec3[segs];
            var radii = new double[segs];
            var nodeTs = new double[Math.Max(0, nodes - 1)];
            for (int k = 1; k < nodes; k++) nodeTs[k - 1] = k / (double)nodes;
            double leanMag = rng.Range(0.024, 0.062);
            var cross = new Vec3(-radial.Z, 0, radial.X);
            double bowMag = rng.Range(-0.035, 0.035);
            double baseR = rng.Range(0.0045, 0.0060);
            for (int idx = 0; idx < segs; idx++)
            {
                double t = idx / (double)(segs - 1);
                // Extend the base beneath uneven soil while keeping the upper silhouette unchanged.
                double embed = 0.022 * Math.Pow(1 - t, 4);
                path[idx] = foot + Vec3.Up * (stemH * t - embed) + radial * (leanMag * t * t) + cross * (bowMag * Math.Sin(Math.PI * t));
                double phase = t * nodes;
                double frac = phase - Math.Floor(phase);
                if (t >= 1.0) frac = 0;
                // Radius modulation both along the stem (node swell) and around it (ribs).
                double nodeFactor = 1.03 + 0.15 * Math.Cos(2 * Math.PI * frac);
                double tipTaper = fertile ? 1.0 : 1.0 - 0.68 * MathD.SmoothStep(0.90, 1.0, t);
                radii[idx] = baseR * (1.0 - 0.30 * t) * nodeFactor * tipTaper;
            }
            double bandWidth = (1.0 / nodes) * rng.Range(0.34, 0.48);
            NewGramSootbandShaft(part, path, radii, nodeTs, shaftBase, shaftTip, bandCol, grooveCol, ridgeCol, sides, ribs, 0.34, bandWidth, capTip: !fertile);
            for (int k = 1; k < nodes; k++)
            {
                if (rng.NextDouble() > 0.28) continue;
                int idx = k * 4;
                if (idx >= segs) continue;
                double nodeR = radii[idx];
                NewGramSootbandCollar(part, path[idx], nodeR * 0.80, nodeR, collarCol, detail);
            }
            if (fertile)
            {
                var tipP = path[^1];
                var tipAxis = (tipP - path[^2]).Normalized();
                NewGramSootbandCone(part, tipP, tipAxis, radii[^1], coneCol, coneShade, detail,
                    rng.Range(0.78, 1.24), rng.Range(0.68, 0.82));
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
