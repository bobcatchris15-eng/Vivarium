using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New aquatics set B (2026-11-06 expansion, flora-fiction rename): Bogfringe, Burrflag, Paddlerib, Barbarrow.
/// Each plant is authored in REAL METRES into a scratch part and converted with
/// <see cref="NewFloraAppendWorld"/>; organ counts honour <c>FloraDetailLevel</c>
/// and <c>Lod</c> through <c>NewFloraCount</c> and the shared primitive budgets.
/// Broadleaf / FoliageBlade record foliage spans, Primitives.Tube records structural
/// spans, and flower / burr organs are flagged through <see cref="NewFloraMarkOrgan"/>
/// with essential spans re-based after the append (Hollowcrown pattern).
/// </summary>
public static partial class OrganismMeshes
{
    private static double[] NbCol(double[] c, double[] fallback)
    {
        if (c == null || c.Length < 3) return (double[])fallback.Clone();
        for (int i = 0; i < 3; i++)
            if (!double.IsFinite(c[i])) return (double[])fallback.Clone();
        return c;
    }

    private static Vec3 NbNorm(Vec3 v, Vec3 fallback)
    {
        if (v.LengthSq < 1e-12) return fallback;
        return v.Normalized();
    }

    private static (Vec3 Point, Vec3 Tangent) NbSample(IReadOnlyList<Vec3> path, double f)
    {
        f = MathD.Clamp01(f);
        double x = f * (path.Count - 1);
        int i0 = (int)Math.Floor(x);
        int i1 = Math.Min(path.Count - 1, i0 + 1);
        double u = x - i0;
        var p = Vec3.Lerp(path[i0], path[i1], u);
        var t = path[i1] - path[i0];
        var tan = t.LengthSq > 1e-14 ? t.Normalized() : Vec3.Up;
        return (p, tan);
    }

    private static void NbOffset(MeshData part, int startVertex, Vec3 offset)
    {
        for (int i = startVertex; i < part.VertexCount; i++)
        {
            var p = part.Position(i);
            part.Positions[i * 3] = (float)(p.X + offset.X);
            part.Positions[i * 3 + 1] = (float)(p.Y + offset.Y);
            part.Positions[i * 3 + 2] = (float)(p.Z + offset.Z);
        }
    }

    private static void NbFinish(MeshData m, MeshData part, double radius, double height)
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

    private static void NbStalk(MeshData part, Vec3[] path, double[] radii, int sides, double[] c0, double[] c1)
    {
        Primitives.Tube(part, path, radii, sides,
            (i, v) => (Primitives.Mix(c0, c1, (double)i / Math.Max(1, path.Length - 1)), 1.0, (double)i / Math.Max(1, path.Length - 1), v, 0.0, 0.0));
    }

    // ---------------------------------------------------------------------------------------------
    // Bogfringe - trifoliate rafts with fringed white flowers. Three-lobed leaves lie on still
    // water and the white flowers open feathered like small fringes; the raft rises as it flowers.
    // ---------------------------------------------------------------------------------------------

    private static void NbBogfringeFlower(MeshData tmp, ulong seed, Rng rng, double[] petalBase, double[] petalTip, int frills)
    {
        double azimuthBase = rng.Range(0, Math.Tau);
        for (int k = 0; k < 5; k++)
        {
            double az = azimuthBase + k * (Math.Tau / 5.0) + rng.Range(-0.12, 0.12);
            double len = rng.Range(0.018, 0.026);
            var axis = new AxisParams(Length: len, BaseAngle: rng.Range(1.18, 1.42), BaseAzimuth: az,
                Droop: rng.Range(0.02, 0.10), WobbleAmplitude: len * 0.06, WobbleFrequency: 1.6, Segments: 5);
            ulong pSeed = Rng.Mix(seed, (ulong)(k * 17 + 3));
            double halfW = rng.Range(0.0040, 0.0055);
            Sheet.Build(tmp, new SheetParams(axis, HalfWidth: halfW, Thickness: 0.00028, Curl: 0.22, DetailLevel: 1, FaceAxisUp: true),
                pSeed, petalBase, petalTip);
            if (frills <= 0) continue;
            var frames = Axis.Build(axis, pSeed);
            var tip = frames[^1].Point;
            var tan = NbNorm(frames[^1].Tangent, Vec3.Up);
            var side = NbNorm(frames[^1].Side, new Vec3(1, 0, 0));
            var up = NbNorm(frames[^1].Up, Vec3.Up);
            var fringeCol = Primitives.Mix(petalBase, petalTip, 0.55);
            for (int f = 0; f < frills; f++)
            {
                double off = (f - (frills - 1) * 0.5) * 0.0022;
                var s = tip + side * (off * 0.4);
                var e = tip + tan * rng.Range(0.005, 0.010) + side * (off * 1.8) + up * rng.Range(0.0015, 0.0025);
                Primitives.Tube(tmp, new[] { s, e }, new[] { 0.00030, 0.00004 }, 6,
                    (i, v) => (fringeCol, 1.0, (double)i, v, 0.0, 0.0));
            }
        }
        var throat = Primitives.Mix(petalBase, new[] { 0.96, 0.90, 0.45 }, 0.35);
        Primitives.Ellipsoid(tmp, new Vec3(0, 0.0012, 0), new Vec3(0.0018, 0.0012, 0.0018), 3, 6,
            (u, v) => (throat, 1.0, u, v, 0.0, 0.0));
    }

    private static void Bogfringe(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        // Wave-5 daylight pass: leaf shader mixes vertex 38/62 toward dark species pigment; author ~40% brighter.
        double[] green = Primitives.Scale(NbCol(c1, new[] { 0.22, 0.38, 0.16 }), 1.40);
        double[] white = Primitives.Scale(NbCol(c2, new[] { 0.92, 0.92, 0.88 }), 1.10);
        double foot = radius > 0.01 ? radius : 0.30;
        double tall = height > 0.01 ? height : 0.14;
        double size = juvenile ? 0.65 : 1.0;

        double[] petalBase = new[] { 0.96, 0.97, 0.98 }; // Wave 9: snow-white
        double[] petalTip = new[] { 0.96, 0.97, 0.98 };
        double[] stalkCol = Primitives.Scale(Primitives.Mix(green, white, 0.12), 1.15);
        var sheenCol = Primitives.Mix(green, new[] { 1.0, 1.0, 0.94 }, 0.60); // pale gloss proxy at leaflet centre

        // Rhizome crown: a small bed node the raft petioles rise from.
        {
            int v0 = part.VertexCount;
            Primitives.Ellipsoid(part, new Vec3(0, 0.006, 0), new Vec3(0.016, 0.006, 0.016), 3, 6,
                (u, v) => (Primitives.Scale(Primitives.Mix(green, white, 0.10), 0.82), 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
        }

        int petioles = NewFloraCount(part, 8 + rng.NextInt(3)); // Wave 9: 8-10 unmistakable trifoliate rafts
        if (juvenile) petioles = Math.Max(5, Math.Min(petioles, 6)); // 5+ rafts read even when young
        double spin = rng.Range(0, Math.Tau);
        for (int k = 0; k < petioles; k++)
        {
            double az = spin + k * Math.Tau / Math.Max(1, petioles) + rng.Range(-0.22, 0.22);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            double raftR = foot * rng.Range(0.25, 0.85);
            var footPt = outward * (raftR * 0.30) + Vec3.Up * 0.004;
            double floatY = height * rng.Range(1.013, 1.035);
            var junction = outward * raftR + Vec3.Up * floatY;
            var mid = (footPt + junction) * 0.5 + Vec3.Up * 0.002;
            NbStalk(part, new[] { footPt, mid, junction }, new[] { 0.0015, 0.0011, 0.0007 }, 6,
                Primitives.Scale(green, 0.85), green);

            double bladeLen = rng.Range(0.045, 0.065) * size; // Wave 9: 4.5-6.5 cm obovate leaflets per raft
            (double AzOff, double LenMul)[] slots = new[] { (0.0, 1.0), (-0.91, 0.78), (0.91, 0.85) }; // wide trifoliate fan: 3 leaflets share one junction
            for (int q = 0; q < 3; q++)
            {
                double a2 = az + slots[q].AzOff;
                var o2 = new Vec3(Math.Cos(a2), 0, Math.Sin(a2));
                var dir = NbNorm(o2 + Vec3.Up * 0.035, Vec3.Up); // near-horizontal: flat on water
                double len = bladeLen * slots[q].LenMul * rng.Range(0.92, 1.08);
                double wid = len * rng.Range(0.61, 0.79);
                var outline = rng.NextDouble() < 0.70 ? BroadleafOutline.OrbicularCordate : BroadleafOutline.Ovate;
                int? det = (part.FloraDetailLevel ?? 0) == 0 ? 1 : null;
                int leafV0 = part.VertexCount;
                Broadleaf.BuildLeaf(part, junction, dir, len, wid, outline,
                    Rng.Mix(seed, (ulong)(k * 733 + q * 97 + 5)), green, white,
                    age: q == 0 ? 0.90 : rng.Range(0.72, 0.90), // younger paler, brighter
                    camber: 0.05, midribFold: 0.06, wavyMargin: 0.01, wavyFrequency: 5.0, // flat glossy sheet
                    tipCurl: 0.02, petioleLength: rng.Range(0.012, 0.018) * size, petioleRadius: 0.0007, petioleKink: 0.10, detailOverride: det); // short trifoliate petioles keep the 3 leaflets grouped
                for (int v = leafV0; v < part.VertexCount; v++)
                {
                    double face = part.UV2[v * 2];
                    if (face > 0.5) continue;
                    double t = part.UV[v * 2];
                    double across = part.UV[v * 2 + 1];
                    double wx = 1.0 - Math.Abs(across - 0.5) / 0.30;
                    double wt = 1.0 - Math.Abs(t - 0.40) / 0.45;
                    double w = Math.Max(0.0, Math.Min(wx, wt)) * 0.52; // glossy centre sheen reads as still-water rafts
                    if (w <= 0.0) continue;
                    var cur = new[] { (double)part.Colors[v * 4], (double)part.Colors[v * 4 + 1], (double)part.Colors[v * 4 + 2] };
                    var mixed = Primitives.Mix(cur, sheenCol, w);
                    part.SetColor(v, mixed[0], mixed[1], mixed[2], 1.0);
                }
            }
        }

        int spikesFull = juvenile ? 2 : 3; // Wave 9: 3 stout racemes
        int spikes = spikesFull;
        if (spikesFull > 0 && (part.FloraDetailLevel ?? 0) > 0)
            spikes = Math.Min(spikesFull, NewFloraCount(part, spikesFull));
        if (part.Lod is { } lodSp && spikes > 1)
            spikes = Math.Min(spikes, Math.Max(1, (int)Math.Round(spikes * lodSp.LeafFraction)));

        for (int s = 0; s < spikes; s++)
        {
            double az0 = rng.Range(0, Math.Tau);
            var out0 = new Vec3(Math.Cos(az0), 0, Math.Sin(az0));
            var p0 = new Vec3(0, 0.006, 0) + out0 * rng.Range(0.002, 0.009);
            double hH = height * rng.Range(1.35, 1.66);
            var top = p0 + out0 * rng.Range(0.010, 0.026) + Vec3.Up * hH;
            var path = new[]
            {
                p0,
                p0 + Vec3.Up * (hH*0.33) + out0 * 0.005,
                p0 + Vec3.Up * (hH*0.66) + out0 * 0.012,
                top,
            };
            NbStalk(part, path, new[] { 0.0022, 0.0018, 0.0013, 0.0009 }, 6, stalkCol, Primitives.Scale(stalkCol, 1.05));

            int flowers = NewFloraCount(part, 5 + rng.NextInt(3));
            int frills = (part.FloraDetailLevel ?? 0) switch
            {
                0 => 4 + rng.NextInt(2), // 4-5 fringe tubes per petal at full detail
                1 => 3,
                _ => 2,
            };
            if (part.Lod is { } lodF) frills = Math.Max(1, (int)Math.Round(frills * lodF.LeafFraction));
            for (int k = 0; k < flowers; k++)
            {
                double f = 0.78 + 0.20 * (k + 0.5) / flowers;
                var (nodePt, _) = NbSample(path, f);
                double azimuth = az0 + k * 2.399963229728653 + rng.Range(-0.22, 0.22);
                var pedicel = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
                var elbow = nodePt + pedicel * 0.011 + Vec3.Up * 0.007;
                var centre = elbow + pedicel * rng.Range(0.009, 0.022) - Vec3.Up * rng.Range(0.002, 0.005);
                Primitives.Tube(part, new[] { nodePt, elbow, centre }, new[] { 0.00065, 0.00046, 0.00028 }, 6,
                    (i, v) => (stalkCol, 1.0, (double)i, v, 0.0, 0.0));
                var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
                NbBogfringeFlower(tmp, Rng.Mix(seed, (ulong)(s * 613 + k * 97 + 11)), rng, petalBase, petalTip, frills);
                NewFloraMarkOrgan(tmp, 0, tmp.VertexCount);
                int baseV = part.VertexCount;
                part.Append(tmp, p => p + centre);
                if (part.VertexCount > baseV) part.EssentialSpans.Add((baseV, part.VertexCount - baseV));
            }
            int b0 = part.VertexCount;
            Primitives.Ellipsoid(part, top + Vec3.Up * 0.005, new Vec3(0.0036, 0.0045, 0.0036), 3, 6,
                (u, v) => (petalTip, 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, b0, part.VertexCount - b0);
            part.EssentialSpans.Add((b0, part.VertexCount - b0));
        }
        NbFinish(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Burrflag - floating ribbon and burr of shallow margins. Long ribbons trail from the bank
    // and end in spiked burr heads; grazing animals carry them to the next pool in their coats.
    // ---------------------------------------------------------------------------------------------

    private static void NbBurrflagHead(MeshData part, Vec3 centre, Vec3 outDir, Rng rng, int bracts,
        double[] strawBase, double[] strawTip)
    {
        int v0 = part.VertexCount;
        double burrR = rng.Range(0.014, 0.018); // Wave 9: 2.8-3.6 cm diameter core; spines extend beyond
        Primitives.Ellipsoid(part, centre, new Vec3(burrR, burrR, burrR), 4, 8,
            (u, v) => (Primitives.Mix(strawBase, strawTip, u * 0.50), 1.0, u, v, 0.0, 0.0));
        var axis = NbNorm(outDir, Vec3.Up);
        var side = axis.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = axis.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(axis).Normalized();
        for (int b = 0; b < bracts; b++)
        {
            double phi = rng.Range(0, Math.Tau);
            double lift = rng.Range(0.15, 0.55);
            var dir = (side * Math.Cos(phi) + up * Math.Sin(phi) + axis * lift).Normalized();
            var b0 = centre + dir * (burrR * 0.65);
            var b1 = centre + dir * (burrR * 1.75) + axis * 0.0040;
            var b2 = centre + dir * rng.Range(0.040, 0.055) + axis * 0.0060; // forward-leaning hooked green spikes: burr, not marble
            var col = Primitives.Mix(strawBase, strawTip, rng.Range(0.15, 0.85));
            Primitives.Tube(part, new[] { b0, b1, b2 }, new[] { 0.0011, 0.00050, 0.00004 }, 6,
                (i, v) => (col, 1.0, (double)i / 2.0, v, 0.0, 0.0));
        }
        NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
        part.EssentialSpans.Add((v0, part.VertexCount - v0));
    }

    private static void Burrflag(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        // Wave-5 daylight pass: leaf shader mixes vertex 38/62 toward dark species pigment; author ~40% brighter.
        double[] green = Primitives.Scale(NbCol(c1, new[] { 0.24, 0.42, 0.18 }), 1.40);
        double[] straw = Primitives.Scale(NbCol(c2, new[] { 0.58, 0.45, 0.24 }), 1.40);
        double tall = height > 0.01 ? height : 0.55;
        double size = juvenile ? 0.65 : 1.0;

        double[] leafBase = Primitives.Scale(Primitives.Mix(Primitives.Mix(green, new[] { 0.45, 0.62, 0.32 }, 0.30), new[] { 0.10, 0.18, 0.06 }, 0.45), 1.15); // dark trailing ribbons, not washed-out pond blur
        double[] leafTip = Primitives.Scale(Primitives.Mix(Primitives.Mix(green, new[] { 0.65, 0.78, 0.45 }, 0.35), new[] { 0.10, 0.18, 0.06 }, 0.45), 1.15);
        double[] strawBase = Primitives.Scale(Primitives.Mix(straw, new[] { 0.30, 0.44, 0.14 }, 0.65), 1.05); // deep green-brown burr core
        double[] strawTip = Primitives.Scale(Primitives.Mix(straw, new[] { 0.62, 0.74, 0.36 }, 0.55), 1.05); // green spike tips, not yellow

        {
            int v0 = part.VertexCount;
            Primitives.Ellipsoid(part, new Vec3(0, 0.005, 0), new Vec3(0.014, 0.006, 0.014), 3, 6,
                (u, v) => (Primitives.Scale(green, 0.80), 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
        }

        int tussockBlades = NewFloraCount(part, 16 + rng.NextInt(7)); // Wave 9: 16-22 upright-arching keeled blades
        for (int tb = 0; tb < tussockBlades; tb++)
        {
            double tAz = rng.Range(0, Math.Tau);
            var tOut = new Vec3(Math.Cos(tAz), 0, Math.Sin(tAz));
            var tRoot = tOut * rng.Range(0.004, 0.020) + Vec3.Up * 0.004;
            double tTipY = rng.Range(0.18, 0.45) * size;
            var tTip = tRoot + tOut * rng.Range(0.06, 0.14) + Vec3.Up * tTipY;
            var tSide = new Vec3(-Math.Sin(tAz), 0, Math.Cos(tAz));
            FoliageBlade.Build(part, tRoot, tTip, tSide, rng.Range(0.006, 0.010),
                leafBase, leafTip, camber: 0.10, curl: 0.08, roll: rng.Range(-0.25, 0.25),
                asymmetry: 0.0, shoulder: 0.85);
        }

        int ribbons = NewFloraCount(part, 6 + rng.NextInt(3)); // 6-8 trailing water ribbons
        if (juvenile) ribbons = Math.Min(ribbons, 6);
        double spin = rng.Range(0, Math.Tau);
        for (int k = 0; k < ribbons; k++)
        {
            double az = spin + (k + rng.Range(-0.35, 0.35)) * Math.Tau / ribbons;
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var root = outward * rng.Range(0.006, 0.018) + Vec3.Up * 0.004;
            double trail = rng.Range(0.24, 0.43) * size;
            var axis = new AxisParams(Length: trail, BaseAngle: rng.Range(0.60, 0.93), BaseAzimuth: az,
                Droop: rng.Range(-1.14, -0.77), TwistPerLength: rng.Range(-3.2, 3.2),
                WobbleAmplitude: trail * 0.075, WobbleFrequency: 0.85, Segments: 7);
            ulong ribbonSeed = Rng.Mix(seed, (ulong)(k * 377 + 11));
            int first = part.VertexCount;
            int firstIndex = part.Indices.Count;
            Sheet.Build(part, new SheetParams(axis, HalfWidth: rng.Range(0.009, 0.017),
                Curl: 0.12, DetailLevel: 1, FaceAxisUp: true), ribbonSeed, leafBase, leafTip);
            NbOffset(part, first, root);
            part.RecordLeaf(first, firstIndex, root, trail);
            if (juvenile || k % 3 == 0) continue;
            var end = Axis.Build(axis, ribbonSeed)[^1];
            NbBurrflagHead(part, root + end.Point, end.Tangent, rng, NewFloraCount(part, 22), strawBase, strawTip);
        }

        int stems = NewFloraCount(part, 4 + rng.NextInt(2)); // Wave 9: 4-5 stems
        if (juvenile) stems = Math.Min(stems, 3);
        double cspin = rng.Range(0, Math.Tau);
        for (int s = 0; s < stems; s++)
        {
            double az = cspin + s * Math.Tau / Math.Max(1, stems) + rng.Range(-0.40, 0.40);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var footPt = outward * rng.Range(0.004, 0.016) + Vec3.Up * 0.004;
            double hH = rng.Range(0.25, 0.50) * size; // Wave 8: zigzag emergent stems Y 0.25-0.50 m
            var top = footPt + outward * rng.Range(0.02, 0.05) + Vec3.Up * hH;
            var sideZ = new Vec3(-outward.Z, 0, outward.X);
            double zig = rng.Range(0.012, 0.022);
            var path = new[]
            {
                footPt,
                footPt + outward * 0.010 + sideZ * zig + Vec3.Up * (hH*0.25),
                footPt + outward * 0.018 - sideZ * zig + Vec3.Up * (hH*0.50),
                footPt + outward * 0.026 + sideZ * zig + Vec3.Up * (hH*0.75),
                top,
            };
            NbStalk(part, path, new[] { 0.0065, 0.0054, 0.0043, 0.0033, 0.0022 }, 6, leafBase, leafTip);

            int burrs = juvenile ? 2 : 3 + rng.NextInt(2); // Wave 9: 3-4 burrs per risen stem
            burrs = Math.Min(burrs, NewFloraCount(part, burrs));
            Vec3 topBurr = top;
            for (int b = 0; b < burrs; b++)
            {
                double f = 0.42 + 0.50 * (b + 0.5) / Math.Max(1, burrs);
                var (at, tan) = NbSample(path, f);
                var joint = at + outward * 0.009 + tan * 0.006;
                var centre = joint + outward * rng.Range(0.004, 0.010) - Vec3.Up * 0.005;
                NbStalk(part, new[] { at, joint, centre }, new[] { 0.0014, 0.0010, 0.0008 }, 6, leafBase, leafTip);
                int bracts = NewFloraCount(part, 38); // ~38 pointed spikes (spiky burr, not marble)
                NbBurrflagHead(part, centre, (outward + tan + Vec3.Up * 0.15).Normalized(), rng, bracts, strawBase, strawTip);
                if (b == burrs - 1) topBurr = centre;
            }
            var contDir = (top - footPt).Normalized();
            if (contDir.LengthSq < 1e-12) contDir = Vec3.Up;
            var cRoot = topBurr + contDir * 0.006;
            var cTip = cRoot + contDir * rng.Range(0.070, 0.110) * size + outward * 0.008;
            var cSide = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
            FoliageBlade.Build(part, cRoot, cTip, cSide, rng.Range(0.0048, 0.0068),
                leafBase, leafTip, camber: 0.10, curl: 0.10, roll: rng.Range(-0.3, 0.3));
        }
        NbFinish(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Paddlerib - broad ribbed leaves and whorled airy stems of mud and shallows. Deep-ribbed
    // leaves stand out of the mud on long stalks, and the branching stems open whorl upon whorl
    // of small pale flowers.
    // ---------------------------------------------------------------------------------------------

    private static void NbStarFlower(MeshData tmp, ulong seed, Rng rng,
        double[] petalBase, double[] petalTip, double[] centreCol,
        double petalLen, double petalHalfW, double centreR, double baseAngle)
    {
        double azimuthBase = rng.Range(0, Math.Tau);
        for (int k = 0; k < 3; k++)
        {
            double az = azimuthBase + k * (Math.Tau / 3.0) + rng.Range(-0.14, 0.14);
            double len = petalLen * rng.Range(0.92, 1.08);
            var axis = new AxisParams(Length: len, BaseAngle: baseAngle + rng.Range(-0.08, 0.08), BaseAzimuth: az,
                Droop: rng.Range(0.02, 0.07), WobbleAmplitude: len * 0.05, WobbleFrequency: 1.5, Segments: 5);
            Sheet.Build(tmp, new SheetParams(axis, HalfWidth: petalHalfW, Thickness: 0.00028, Curl: 0.07, DetailLevel: 1, FaceAxisUp: true),
                Rng.Mix(seed, (ulong)(k * 17 + 5)), petalBase, petalTip);
        }
        Primitives.Ellipsoid(tmp, new Vec3(0, petalLen * 0.04, 0),
            new Vec3(centreR, Math.Max(1e-4, centreR * 0.6), centreR), 3, 6,
            (u, v) => (centreCol, 1.0, u, v, 0.0, 0.0));
    }

    private static void Paddlerib(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        // Wave-5 daylight pass: leaf shader mixes vertex 38/62 toward dark species pigment; author ~40% brighter.
        double[] green = Primitives.Scale(NbCol(c1, new[] { 0.26, 0.46, 0.20 }), 1.40);
        double[] pale = Primitives.Scale(NbCol(c2, new[] { 0.96, 0.90, 0.90 }), 1.10);
        double tall = height > 0.01 ? height : 0.45;
        double size = juvenile ? 0.65 : 1.0;

        double[] petalBase = Primitives.Mix(pale, new[] { 0.99, 0.99, 0.97 }, 0.60);
        double[] petalTip = Primitives.Mix(pale, new[] { 1.0, 1.0, 1.0 }, 0.75);
        double[] centreCol = Primitives.Mix(pale, new[] { 0.92, 0.98, 0.84 }, 0.35); // pale green-cream centre, no yellow cluster
        double[] stalkCol = Primitives.Scale(Primitives.Mix(green, pale, 0.12), 1.15);

        {
            int v0 = part.VertexCount;
            Primitives.Ellipsoid(part, new Vec3(0, 0.006, 0), new Vec3(0.016, 0.007, 0.016), 3, 6,
                (u, v) => (Primitives.Scale(green, 0.82), 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
        }

        int leaves = NewFloraCount(part, 8 + rng.NextInt(3)); // Wave 8: 8-10 broad cordate paddles
        if (juvenile) leaves = Math.Min(leaves, 5);
        double spin = rng.Range(0, Math.Tau);
        for (int i = 0; i < leaves; i++)
        {
            double az = spin + i * 2.399963229728653 + rng.Range(-0.20, 0.20);
            double pitch = rng.Range(0.18, 0.32); // upright: blades stand clear above water, tilted out so ribs read from above
            var dir = new Vec3(Math.Cos(az) * Math.Sin(pitch), Math.Cos(pitch), Math.Sin(az) * Math.Sin(pitch));
            var attach = new Vec3(Math.Cos(az) * 0.012, 0.020, Math.Sin(az) * 0.012);
            double len = rng.Range(0.12, 0.18) * size; // Wave 8: blades land at Y ~0.15-0.32 m (shorter petiole below)
            double wid = len * rng.Range(1.0, 1.15); // broad ribbed paddles, not narrow blades
            var outline = rng.NextDouble() < 0.75 ? BroadleafOutline.Ovate : BroadleafOutline.OrbicularCordate;
            Broadleaf.BuildLeaf(part, attach, NbNorm(dir, Vec3.Up), len, wid, outline,
                Rng.Mix(seed, (ulong)(i * 733 + 23)), green, pale,
                age: rng.Range(0.60, 0.95), camber: 0.14, midribFold: rng.Range(0.20, 0.38),
                wavyMargin: 0.025, wavyFrequency: 4.0, tipCurl: 0.06,
                petioleLength: rng.Range(0.10, 0.18) * size, petioleRadius: 0.0055,
                petioleKink: 0.20, roll: rng.Range(-0.25, 0.25));
        }

        int stemsFull = juvenile ? 1 : 2 + rng.NextInt(2); // 2-3 taller stems (budget: whorls per stem up)
        int stems = stemsFull;
        if (stemsFull > 0 && (part.FloraDetailLevel ?? 0) > 0)
            stems = Math.Min(stemsFull, NewFloraCount(part, stemsFull));
        if (part.Lod is { } lodSt && stems > 1)
            stems = Math.Min(stems, Math.Max(1, (int)Math.Round(stems * lodSt.LeafFraction)));

        for (int s = 0; s < stems; s++)
        {
            double az0 = rng.Range(0, Math.Tau);
            var out0 = new Vec3(Math.Cos(az0), 0, Math.Sin(az0));
            var p0 = out0 * rng.Range(0.002, 0.012) + Vec3.Up * 0.008;
            double hH = rng.Range(0.30, 0.48) * size; // Wave 8: tall airy panicles Y 0.30-0.48 m
            var top = p0 + out0 * rng.Range(0.008, 0.022) + Vec3.Up * hH;
            var path = new[]
            {
                p0,
                p0 + Vec3.Up * (hH*0.33) + out0 * 0.008,
                p0 + Vec3.Up * (hH*0.66) + out0 * 0.015,
                top,
            };
            NbStalk(part, path, new[] { 0.0019, 0.0016, 0.0012, 0.0007 }, 6, stalkCol, Primitives.Scale(stalkCol, 1.05));

            int whorls = juvenile ? 2 : 4 + rng.NextInt(2); // 4-5 small pale whorls: understated against the paddles
            whorls = Math.Min(whorls, NewFloraCount(part, whorls));
            for (int w = 0; w < whorls; w++)
            {
                double f = 0.45 + 0.52 * (w + rng.Range(0.28, 0.72)) / whorls;
                if (w == whorls - 1) f = 1.0; // The last flower whorl closes the stalk.
                var (nodePt, _) = NbSample(path, f);
                int branches = NewFloraCount(part, 4); // 4 small pale flowers per whorl
                double bBase = rng.Range(0, Math.Tau);
                for (int b = 0; b < branches; b++)
                {
                    double baz = bBase + b * Math.Tau / Math.Max(1, branches) + rng.Range(-0.18, 0.18);
                    double tilt = rng.Range(0.15, 0.35);
                    var bDir = new Vec3(Math.Cos(baz) * Math.Cos(tilt), Math.Sin(tilt), Math.Sin(baz) * Math.Cos(tilt));
                    double blen = rng.Range(0.040, 0.070) * MathD.Lerp(1.35, 0.75, f) * size;
                    var bMid = nodePt + bDir * (blen * 0.6) + Vec3.Up * 0.003;
                    var bEnd = nodePt + bDir * blen + Vec3.Up * 0.005;
                    Primitives.Tube(part, new[] { nodePt, bMid, bEnd }, new[] { 0.00038, 0.00026, 0.00014 }, 6,
                        (ii, v) => (stalkCol, 1.0, (double)ii / 2.0, v, 0.0, 0.0));
                    var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
                    NbStarFlower(tmp, Rng.Mix(seed, (ulong)(s * 733 + w * 97 + b * 17 + 7)), rng,
                        petalBase, petalTip, centreCol, 0.017, 0.0050, 0.0024, 1.30);
                    NewFloraMarkOrgan(tmp, 0, tmp.VertexCount);
                    int baseV = part.VertexCount;
                    part.Append(tmp, p => p + bEnd);
                    if (part.VertexCount > baseV) part.EssentialSpans.Add((baseV, part.VertexCount - baseV));
                }
            }
        }
        NbFinish(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Barbarrow - arrow-leaved emergent of shallow margins. Leaves end in two long barbed lobes
    // and the stems carry whorls of white triple flowers; the barbs hook drifting debris into shelter.
    // ---------------------------------------------------------------------------------------------

    private static void Barbarrow(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        // Wave-4 daylight pass: leaf shader mixes vertex 38/62 toward dark species pigment; author ~40% brighter.
        double[] green = Primitives.Scale(NbCol(c1, new[] { 0.24, 0.44, 0.19 }), 1.40);
        double[] white = Primitives.Scale(NbCol(c2, new[] { 0.94, 0.94, 0.90 }), 1.05);
        double tall = height > 0.01 ? height : 0.50;
        double size = juvenile ? 0.65 : 1.0;

        double[] petalBase = Primitives.Mix(white, new[] { 0.97, 0.97, 0.94 }, 0.45);
        double[] petalTip = Primitives.Mix(white, new[] { 1.0, 1.0, 0.99 }, 0.60);
        double[] centreCol = new[] { 1.0, 0.88, 0.30 };
        double[] stalkCol = Primitives.Scale(Primitives.Mix(green, white, 0.10), 1.10);
        double[] barbBase = Primitives.Scale(green, 0.95);
        double[] barbTip = Primitives.Mix(green, white, 0.25);

        {
            int v0 = part.VertexCount;
            Primitives.Ellipsoid(part, new Vec3(0, 0.006, 0), new Vec3(0.015, 0.007, 0.015), 3, 6,
                (u, v) => (Primitives.Scale(green, 0.82), 1.0, u, v, 0.0, 0.0));
            NewFloraMarkOrgan(part, v0, part.VertexCount - v0);
        }

        int leaves = NewFloraCount(part, 4 + rng.NextInt(4)); // 4-7 arrow leaves
        if (juvenile) leaves = Math.Min(leaves, 5);
        double spin = rng.Range(0, Math.Tau);
        for (int i = 0; i < leaves; i++)
        {
            double az = spin + i * 2.399963229728653 + rng.Range(-0.20, 0.20);
            double pitch = rng.Range(0.25, 0.55); // tall erect petioles
            var dir = NbNorm(new Vec3(Math.Cos(az) * Math.Sin(pitch), Math.Cos(pitch), Math.Sin(az) * Math.Sin(pitch)), Vec3.Up);
            var attach = new Vec3(Math.Cos(az) * 0.012, 0.005, Math.Sin(az) * 0.012);
            double pLen = rng.Range(0.23, 0.37) * size;
            double pKink = rng.Range(0.25, 0.35);
            double bladeLen = rng.Range(0.12, 0.20) * size;
            double bladeWid = bladeLen * rng.Range(0.60, 0.75);
            var pDir = dir;
            double age = rng.Range(0.50, 0.95);
            Broadleaf.BuildLeaf(part, attach, pDir, bladeLen, bladeWid, BroadleafOutline.Sagittate,
                Rng.Mix(seed, (ulong)(i * 733 + 41)), green, white,
                age: age, camber: 0.12, midribFold: 0.14, wavyMargin: 0.04, wavyFrequency: 5.0,
                tipCurl: 0.08, petioleLength: pLen, petioleRadius: 0.0030, petioleKink: pKink,
                roll: rng.Range(-0.25, 0.25));

            var junction = attach + pDir * pLen + Vec3.Up * (pLen * 0.18);
            var fwd = NbNorm(pDir * Math.Cos(pKink) - Vec3.Up * Math.Sin(pKink), pDir);
            var side = fwd.Cross(Vec3.Up);
            if (side.LengthSq < 1e-8) side = fwd.Cross(new Vec3(1, 0, 0));
            side = side.Normalized();
            var normal0 = NbNorm(side.Cross(fwd), Vec3.Up);
            double barbLen = bladeLen * rng.Range(0.58, 0.73);
            var barbL = NbNorm(-fwd * 0.85 - side * 0.55, normal0);
            var barbR = NbNorm(-fwd * 0.85 + side * 0.55, normal0);
            NewFloraBlade(part, junction, barbL, normal0, barbLen, 0.0046,
                t => 1.0 - 0.88 * Math.Pow(t, 1.8), // sharp point, not blunt tip
                t => Primitives.Mix(barbBase, barbTip, t),
                rows: 6, sides: 7, flatness: 0.30, keel: 0.10, cup: 0.10, camber: -0.03,
                tipCurl: 0.10, twist: rng.Range(-0.12, 0.12), volumetric: true);
            NewFloraBlade(part, junction, barbR, normal0, barbLen * rng.Range(0.92, 1.08), 0.0046,
                t => 1.0 - 0.88 * Math.Pow(t, 1.8),
                t => Primitives.Mix(barbBase, barbTip, t),
                rows: 6, sides: 7, flatness: 0.30, keel: 0.10, cup: 0.10, camber: -0.03,
                tipCurl: 0.10, twist: rng.Range(-0.12, 0.12), volumetric: true);
        }

        int stemsFull = juvenile ? 1 + rng.NextInt(2) : 2 + rng.NextInt(3); // 2-4 flower stems
        int stems = stemsFull;
        if (stemsFull > 0 && (part.FloraDetailLevel ?? 0) > 0)
            stems = Math.Min(stemsFull, NewFloraCount(part, stemsFull));
        if (part.Lod is { } lodSt && stems > 1)
            stems = Math.Min(stems, Math.Max(1, (int)Math.Round(stems * lodSt.LeafFraction)));

        for (int s = 0; s < stems; s++)
        {
            double az0 = rng.Range(0, Math.Tau);
            var out0 = new Vec3(Math.Cos(az0), 0, Math.Sin(az0));
            var p0 = out0 * rng.Range(0.002, 0.012) + Vec3.Up * 0.008;
            double hH = tall * rng.Range(0.95, 1.15) * size;
            var top = p0 + out0 * rng.Range(0.008, 0.024) + Vec3.Up * hH;
            var path = new[]
            {
                p0,
                p0 + Vec3.Up * (hH*0.33) + out0 * 0.008,
                p0 + Vec3.Up * (hH*0.66) + out0 * 0.016,
                top,
            };
            NbStalk(part, path, new[] { 0.0027, 0.0021, 0.0017, 0.0011 }, 6, stalkCol, Primitives.Scale(stalkCol, 1.05));

            int whorls = juvenile ? 2 : 3 + rng.NextInt(3); // 3-5 whorls
            whorls = Math.Min(whorls, NewFloraCount(part, whorls));
            for (int w = 0; w < whorls; w++)
            {
                double f = 0.48 + 0.48 * (w + rng.Range(0.27, 0.73)) / whorls;
                if (w == whorls - 1) f = 1.0; // The last flower whorl closes the stalk.
                var (nodePt, _) = NbSample(path, f);
                int per = NewFloraCount(part, 2 + rng.NextInt(2)); // 2-3 flowers per whorl
                double bBase = rng.Range(0, Math.Tau);
                for (int b = 0; b < per; b++)
                {
                    double baz = bBase + b * Math.Tau / Math.Max(1, per) + rng.Range(-0.18, 0.18);
                    double tilt = rng.Range(0.18, 0.38);
                    var bDir = new Vec3(Math.Cos(baz) * Math.Cos(tilt), Math.Sin(tilt), Math.Sin(baz) * Math.Cos(tilt));
                    double blen = rng.Range(0.028, 0.045) * size;
                    var bMid = nodePt + bDir * (blen * 0.6) + Vec3.Up * 0.004;
                    var bEnd = nodePt + bDir * blen + Vec3.Up * 0.006;
                    Primitives.Tube(part, new[] { nodePt, bMid, bEnd }, new[] { 0.00045, 0.00030, 0.00016 }, 6,
                        (ii, v) => (stalkCol, 1.0, (double)ii / 2.0, v, 0.0, 0.0));
                    var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
                    NbStarFlower(tmp, Rng.Mix(seed, (ulong)(s * 733 + w * 97 + b * 17 + 13)), rng,
                        petalBase, petalTip, centreCol, 0.018, 0.0070, 0.0028, 1.30);
                    NewFloraMarkOrgan(tmp, 0, tmp.VertexCount);
                    int baseV = part.VertexCount;
                    part.Append(tmp, p => p + bEnd);
                    if (part.VertexCount > baseV) part.EssentialSpans.Add((baseV, part.VertexCount - baseV));
                }
            }
        }
        NbFinish(m, part, radius, height);
    }
}
