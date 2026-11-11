using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New aquatics set A: Deepquill, Mirepocket and Pondstars.
/// Deepquill is the deep-water plume, Mirepocket the carnivorous floater,
/// Pondstars the floating star fleet. Each plant is authored in REAL METRES
/// into a scratch part and converted with <see cref="NewFloraAppendWorld"/>;
/// organ counts honour <c>FloraDetailLevel</c> and <c>Lod</c> through
/// <c>NewFloraCount</c> and the shared primitive budgets.
/// </summary>
public static partial class OrganismMeshes
{
    // Shared sampler for stem paths: point + tangent at fractional height.
    private static (Vec3 Point, Vec3 Tangent) NaASample(IReadOnlyList<Vec3> path, double f)
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

    // Bare stem tail hanging below a floating crown. Renderers pin the water film to a fixed mesh height instead of
    // stretching the plant, so this stalk reaches whatever bed lies below; underground it is hidden by the terrain.
    private static void NaAStemTail(MeshData part, Vec3 top, double[] col)
    {
        const double len = 2.5;
        Primitives.Tube(part, new[] { top, top - Vec3.Up * (len * 0.5), top - Vec3.Up * len },
            new[] { 0.0026, 0.0024, 0.0020 }, 5,
            (i, v) => (col, 1, i * 0.5, v, 0, 0));
    }

    // Moves a just-built organ (vertices startVertex..end) to a new anchor point.
    private static void NaAOffset(MeshData part, int startVertex, Vec3 offset)
    {
        for (int i = startVertex; i < part.VertexCount; i++)
        {
            var p = part.Position(i);
            part.Positions[i * 3] = (float)(p.X + offset.X);
            part.Positions[i * 3 + 1] = (float)(p.Y + offset.Y);
            part.Positions[i * 3 + 2] = (float)(p.Z + offset.Z);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Deepquill - deep-water plume with forked whorls
    // ---------------------------------------------------------------------------------------------

    /// <summary>One stiff forked quill leaf: a short stub splitting into two tapered tips.</summary>
    private static void NaADeepquillLeaf(MeshData part, Rng rng, Vec3 node, Vec3 dir, double len,
        double[] baseCol, double[] tipCol)
    {
        int lv = part.VertexCount;
        int li = part.Indices.Count;
        var bend = Vec3.Up - dir * Vec3.Up.Dot(dir);
        if (bend.LengthSq < 1e-10) bend = dir.Cross(new Vec3(1, 0, 0));
        if (bend.LengthSq < 1e-10) bend = dir.Cross(new Vec3(0, 0, 1));
        bend = bend.Normalized();
        var lat = dir.Cross(bend).Normalized();
        // Stiff coontail fork (wave 6): narrow V and thick rami, not a feathery hydrilla spray.
        double reach = len * rng.Range(0.78, 0.88);
        double spread = len * rng.Range(0.30, 0.42);
        var mid = node + dir * (len * 0.45);
        Primitives.Tube(part, new[] { node, mid }, new[] { 0.0040, 0.0026 }, 6,
            (i, v) => (i == 0 ? baseCol : Primitives.Mix(baseCol, tipCol, 0.30), 1, (double)i, v, 0, 0));
        var tip1 = mid + dir * reach + bend * spread + lat * (len * 0.08);
        var tip2 = mid + dir * reach - bend * spread - lat * (len * 0.08);
        Primitives.Tube(part, new[] { mid, tip1 }, new[] { 0.0026, 0.0011 }, 6,
            (i, v) => (i == 0 ? Primitives.Mix(baseCol, tipCol, 0.30) : tipCol, 1, (double)i, v, 0, 0));
        Primitives.Tube(part, new[] { mid, tip2 }, new[] { 0.0026, 0.0011 }, 6,
            (i, v) => (i == 0 ? Primitives.Mix(baseCol, tipCol, 0.30) : tipCol, 1, (double)i, v, 0, 0));
        part.RecordLeaf(lv, li, node, len);
    }

    /// <summary>Deepquill: arching, slightly helical deep-water plume carrying whorls of forked quills.</summary>
    private static void Deepquill(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double foot = radius > 0.01 ? radius : 0.22;
        double tall = height > 0.01 ? height : 0.50;
        // Vertex colours are authored brighter than the visual target because the leaf shader
        // mixes 62% dark species pigment back in; wave 6 darkens the plume (paler tips made it
        // read as pondweed) and the stem is thickened below so it reads as a clear dark axis.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.02), 1.20); // dark deep-water stem
        var stemTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.12), 1.20);
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.03), 1.20); // saturated plume green
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.30), 1.20); // only a mild lighten at the tips

        NaAStemTail(part, new Vec3(0, 0.002, 0), stemCol);
        int stems = NewFloraCount(part, (juvenile ? 3 : 7) + rng.NextInt(juvenile ? 2 : 3)); // Wave 9: 7-9 dense adult plumes
        for (int s = 0; s < stems; s++)
        {
            double baseAng = rng.Range(0, Math.Tau);
            double r0 = foot * rng.Range(0.15, 0.75);
            var basePt = new Vec3(Math.Cos(baseAng) * r0, 0.002, Math.Sin(baseAng) * r0);
            double stemH = tall * (juvenile ? 0.55 : rng.Range(0.78, 0.95));
            double outAng = rng.Range(0, Math.Tau);
            var outward = new Vec3(Math.Cos(outAng), 0, Math.Sin(outAng));
            var perp = new Vec3(-outward.Z, 0, outward.X);
            double lean = rng.Range(0.05, 0.10);
            double helixTurns = rng.Range(0.10, 0.30);
            double helixAmp = rng.Range(0.004, 0.012);
            double helixPhase = rng.Range(0, Math.Tau);
            double arch = rng.Range(0.03, 0.07);
            const int P = 9;
            var path = new List<Vec3>();
            var radii = new List<double>();
            for (int i = 0; i < P; i++)
            {
                double t = (double)i / (P - 1);
                double y = stemH * t;
                var horiz = outward * (stemH * lean * t * t)
                    + perp * (Math.Sin(helixPhase + t * Math.Tau * helixTurns) * helixAmp * t);
                // Tip arcs over: droops below the asymptote as it reaches out.
                horiz += outward * (stemH * arch * 0.55 * t * t * t);
                y -= stemH * arch * t * t * t;
                path.Add(basePt + horiz + new Vec3(0, y, 0));
                radii.Add(0.0075 * (1.0 - 0.35 * t));
            }
            Primitives.Tube(part, path, radii, 6,
                (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / (path.Count - 1)), 1, (double)i / (path.Count - 1), v, 0, 0));

            int whorls = NewFloraCount(part, 7 + rng.NextInt(3));
            int leaves = NewFloraCount(part, 7 + rng.NextInt(3));
            for (int k = 0; k < whorls; k++)
            {
                double f = 0.08 + (k + rng.Range(0.28, 0.72)) / whorls * 0.86;
                var (nodePt, tangent) = NaASample(path, f);
                var side = tangent.Cross(perp).Normalized();
                var across = side.Cross(tangent).Normalized();
                double whorlPhase = helixPhase + f * Math.Tau * helixTurns + k * 0.70 + rng.Range(-0.20, 0.20);
                double tilt = MathD.Lerp(0.15, 0.62, f);
                for (int j = 0; j < leaves; j++)
                {
                    double a = whorlPhase + Math.Tau * j / leaves + rng.Range(-0.12, 0.12);
                    var radial = side * Math.Cos(a) + across * Math.Sin(a);
                    var dir = (radial * Math.Cos(tilt) + tangent * Math.Sin(tilt) + outward * (f * 0.12)).Normalized();
                    double len = tall * 0.105 * MathD.Lerp(1.18, 0.60, f) * rng.Range(0.84, 1.16) * (juvenile ? 0.8 : 1.0);
                    NaADeepquillLeaf(part, rng, nodePt, dir, len, leafBase, leafTip);
                }
            }
        }
        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Mirepocket - carnivorous floater with pocketed stems and emergent flowers
    // ---------------------------------------------------------------------------------------------

    /// <summary>One trap: a flattened pale bladder with a hinged funnel mouth and lid dome facing open water.
    /// Sized up for wave 6 (3.2 cm body) so the trap reads at specimen distance instead of vanishing.</summary>
    private static void NaAMirepocketBladder(MeshData part, Vec3 at, Vec3 outDir, double[] bodyCol, double[] mouthCol)
    {
        int organStart = part.VertexCount;
        // Wave 8: true utricularia size — 8-12 mm translucent bladder (was 3.2 cm balloon).
        Primitives.Ellipsoid(part, at, new Vec3(0.0050, 0.0035, 0.0050), 3, 6,
            (u, v) => (bodyCol, 1, u, v, 0, 0));
        Primitives.Tube(part, new[] { at + outDir * 0.0030, at + outDir * 0.0075 }, new[] { 0.0009, 0.0018 }, 6,
            (i, v) => (mouthCol, 1, (double)i, v, 0, 0));
        // Hinged lid: a flattened dome capping the funnel mouth.
        Primitives.Ellipsoid(part, at + outDir * 0.0080 + Vec3.Up * 0.0008, new Vec3(0.0028, 0.0010, 0.0028), 2, 6,
            (u, v) => (mouthCol, 1, u, v, 0, 0));
        NewFloraMarkOrgan(part, organStart, part.VertexCount - organStart);
    }

    /// <summary>Mirepocket: a loose tangle of dissected stems, bladders and yellow flowers.</summary>
    private static void Mirepocket(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double foot = radius > 0.01 ? radius : 0.20;
        double tall = height > 0.01 ? height : 0.30;
        bool coarse = part.Lod?.CullMinorDetails == true;
        // Vertex colours authored ~40% brighter than target (leaf shader mixes 62% dark pigment).
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.08), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.35), 1.40);
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.22), 1.40);
        var petalBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.70), 1.40); // amber throat, brightened for the shader mix
        var petalTip = Primitives.Scale(Primitives.Mix(c2, new[] { 1.0, 0.96, 0.40 }, 0.25), 1.40); // bright c2 yellow
        // Pale trap bodies read against green water from a top-down camera; warm funnel mouths add contrast.
        // Wave 6: paler, more saturated traps (organ-marked, so kept as authored) instead of washed-out sacs.
        var bladderBody = Primitives.Scale(Primitives.Mix(Primitives.Mix(c1, c2, 0.68), new[] { 0.95, 0.93, 0.66 }, 0.75), 1.40);
        var bladderMouth = Primitives.Scale(Primitives.Mix(Primitives.Mix(c1, c2, 0.30), new[] { 0.98, 0.88, 0.42 }, 0.65), 1.40);

        NaAStemTail(part, new Vec3(0, 0.002, 0), stemCol);
        int stemCount = NewFloraCount(part, 3 + rng.NextInt(3)); // 3-5 tangling stems
        int totalBladders = juvenile ? 0 : 28 + rng.NextInt(9); // Wave 9: 28-36 traps total across the whole net
        int bladdersPerStem = stemCount > 0 ? totalBladders / stemCount : 0;
        var scapeRoots = new List<Vec3>();
        for (int s = 0; s < stemCount; s++)
        {
            double baseAng = rng.Range(0, Math.Tau);
            double r0 = foot * rng.Range(0.15, 0.80);
            var basePt = new Vec3(Math.Cos(baseAng) * r0, 0.002, Math.Sin(baseAng) * r0);
            double stemH = tall * (juvenile ? 0.55 : rng.Range(0.65, 0.96));
            var swayDir = new Vec3(Math.Cos(baseAng + 0.7), 0, Math.Sin(baseAng + 0.7));
            var perpDir = new Vec3(-swayDir.Z, 0, swayDir.X);
            double phase = rng.Range(0, Math.Tau);
            const int P = 9;
            var path = new List<Vec3>();
            var radii = new List<double>();
            for (int i = 0; i < P; i++)
            {
                double t = (double)i / (P - 1);
                double sway = Math.Sin(phase + t * 3.4) * 0.030 * t * t;
                double cross = Math.Cos(phase * 1.3 + t * 2.2) * 0.018 * t;
                path.Add(basePt + swayDir * sway + perpDir * cross + new Vec3(0, stemH * t, 0));
                radii.Add(0.0030 * (1.0 - 0.52 * t));
            }
            Primitives.Tube(part, path, radii, 6,
                (i, v) => (Primitives.Mix(stemCol, leafTip, (double)i / (path.Count - 1)), 1, (double)i / (path.Count - 1), v, 0, 0));

            scapeRoots.Add(NaASample(path, 0.67).Point);
            int nodes = NewFloraCount(part, 8 + rng.NextInt(3));
            int fils = NewFloraCount(part, 4 + rng.NextInt(2));
            var filTips = new List<Vec3>();
            for (int k = 0; k < nodes; k++)
            {
                double f = 0.10 + (k + 0.5) / nodes * 0.82;
                var (nodePt, _) = NaASample(path, f);
                int lv = part.VertexCount;
                int li = part.Indices.Count;
                double nodePhase = rng.Range(0, Math.Tau);
                for (int j = 0; j < fils; j++)
                {
                    double a = nodePhase + Math.Tau * j / fils + rng.Range(-0.18, 0.18);
                    double pitch = rng.Range(-0.65, 0.40);
                    var dir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) * Math.Cos(pitch) + Vec3.Up * Math.Sin(pitch)).Normalized();
                    double filLen = rng.Range(0.042, 0.098) * (1.0 - 0.22 * f);
                    var mid = nodePt + dir * (filLen * 0.55) + Vec3.Up * (filLen * 0.05);
                    var tip = nodePt + dir * filLen + Vec3.Up * (filLen * rng.Range(-0.14, 0.06));
                    Primitives.Tube(part, new[] { nodePt, mid, tip }, new[] { 0.0018, 0.0011, 0.00040 }, 6,
                        (i, v) => (Primitives.Mix(leafBase, leafTip, f), 1, (double)i / 2, v, 0, 0));
                    filTips.Add(tip);
                }
                part.RecordLeaf(lv, li, nodePt, 0.028);
            }

            int bladders = juvenile ? 0 : bladdersPerStem + (s == 0 ? totalBladders - bladdersPerStem * stemCount : 0); // share of the 25-35 total
            if (coarse) bladders /= 2;
            bladders = Math.Min(bladders, filTips.Count);
            for (int b = 0; b < bladders; b++)
            {
                // Traps concentrate in the upper, near-film filaments so they read from above:
                // pick the highest of several candidates (upper-half nodes only when available),
                // and point the mouth up/out toward the film rather than drooping into the murk.
                int pick = rng.NextInt(filTips.Count);
                for (int q = 0; q < 4; q++)
                {
                    int candidate = rng.NextInt(filTips.Count);
                    if (filTips[candidate].Y > filTips[pick].Y) pick = candidate;
                }
                var tip = filTips[pick];
                filTips[pick] = filTips[^1];
                filTips.RemoveAt(filTips.Count - 1);
                var away = tip - path[0];
                var dir = away.LengthSq > 1e-12 ? away.Normalized() : Vec3.Up;
                dir = (dir + Vec3.Up * 0.60).Normalized();
                NaAMirepocketBladder(part, tip + dir * 0.0025, dir, bladderBody, bladderMouth);
            }
        }

        // Emergent snapdragon flower stalks: lifted well clear of the film (wave 6) so the
        // lipped blooms read from above instead of sitting in the surface glare.
        if (!juvenile)
        {
            int stalks = Math.Min(scapeRoots.Count, 2 + rng.NextInt(2));
            for (int s = 0; s < stalks; s++)
            {
                double azimuth = rng.Range(0, Math.Tau);
                var bowDir = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
                var p0 = scapeRoots[s];
                var p1 = new Vec3(p0.X, height * rng.Range(1.25, 1.52), p0.Z)
                    + bowDir * rng.Range(0.014, 0.035);
                var mid = (p0 + p1) * 0.5 - bowDir * 0.008;
                var scape = new[] { p0, mid, p1 };
                Primitives.Tube(part, scape, new[] { 0.0015, 0.0012, 0.00070 }, 6,
                    (i, v) => (Primitives.Mix(stemCol, petalBase, (double)i / 2), 1, (double)i / 2, v, 0, 0));

                int flowers = 2 + rng.NextInt(2);
                for (int fi = 0; fi < flowers; fi++)
                {
                    double tf = 0.55 + 0.40 * (fi + 0.5) / flowers;
                    var (flowerNode, _) = NaASample(scape, tf);
                    double az = rng.Range(0, Math.Tau);
                    var fwd = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                    var throatTip = flowerNode + fwd * 0.013 + Vec3.Up * 0.007;
                    var throatMid = flowerNode + fwd * 0.006 + Vec3.Up * 0.003;
                    int organStart = part.VertexCount;
                    Primitives.Tube(part, new[] { flowerNode, throatMid, throatTip }, new[] { 0.00130, 0.00100, 0.00070 }, 6,
                        (i, v) => (Primitives.Mix(petalBase, petalTip, (double)i / 2), 1, (double)i / 2, v, 0, 0));
                    // Lips are Sheet organs anchored at the origin; plant them at the throat opening.
                    int s0 = part.VertexCount;
                    Sheet.Build(part, new SheetParams(
                        new AxisParams(Length: rng.Range(0.019, 0.025), BaseAngle: 0.48, BaseAzimuth: az + Math.PI,
                            Droop: -0.10, WobbleAmplitude: 0.0006, WobbleFrequency: 1.0, Segments: 4),
                        HalfWidth: 0.0080, Thickness: 0.00030, Curl: 0.30, DetailLevel: 1, FaceAxisUp: true),
                        Rng.Mix(seed, (ulong)(s * 733 + fi * 97 + 11)), petalBase, petalTip);
                    NaAOffset(part, s0, throatTip);
                    int s1 = part.VertexCount;
                    Sheet.Build(part, new SheetParams(
                        new AxisParams(Length: rng.Range(0.023, 0.030), BaseAngle: 1.02, BaseAzimuth: az,
                            Droop: 0.20, WobbleAmplitude: 0.0006, WobbleFrequency: 1.0, Segments: 4),
                        HalfWidth: 0.0100, Thickness: 0.00030, Curl: 0.25, DetailLevel: 1, FaceAxisUp: true),
                        Rng.Mix(seed, (ulong)(s * 733 + fi * 97 + 23)), petalBase, petalTip);
                    NaAOffset(part, s1, throatTip);
                    // Petals must keep their authored yellow instead of being re-greened as foliage.
                    NewFloraMarkOrgan(part, organStart, part.VertexCount - organStart);
                }
            }
        }
        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Pondstars - floating star fleet of tiny star rosettes
    // ---------------------------------------------------------------------------------------------

    /// <summary>Pondstars: a fleet of six-pointed pale stars on shallow runners, lying in the surface film.</summary>
    private static void Pondstars(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double foot = radius > 0.01 ? radius : 0.10;
        var stemCol = Primitives.Mix(c1, c2, 0.18);
        var leafBase = Primitives.Mix(c1, c2, 0.22);
        var leafTip = Primitives.Mix(c1, c2, 0.57);
        int rosettes = NewFloraCount(part, juvenile ? 6 + rng.NextInt(3) : 11 + rng.NextInt(4));
        var placed = new List<Vec3>(rosettes);
        for (int r = 0; r < rosettes; r++)
        {
            double filmY = height * rng.Range(1.005, 1.018);
            double leafLen = rng.Range(0.012, 0.017) * (juvenile ? 0.72 : 1.0);
            double clearance = leafLen * 2.25;
            Vec3 centre = default;
            double bestClearance = -1.0;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double azimuth = rng.Range(0, Math.Tau);
                double spread = foot * rng.Range(0.14, 0.84);
                var candidate = new Vec3(Math.Cos(azimuth) * spread, filmY, Math.Sin(azimuth) * spread);
                double nearest = double.PositiveInfinity;
                foreach (var other in placed)
                    nearest = Math.Min(nearest, (candidate.XZ - other.XZ).LengthSq);
                if (nearest > bestClearance)
                {
                    centre = candidate;
                    bestClearance = nearest;
                }
                if (nearest >= clearance * clearance) break;
            }
            if (bestClearance < leafLen * leafLen) continue;
            Vec3 neighbor = default;
            double neighborDistance = double.PositiveInfinity;
            foreach (var other in placed)
            {
                double distance = (centre.XZ - other.XZ).LengthSq;
                if (distance >= neighborDistance) continue;
                neighbor = other;
                neighborDistance = distance;
            }
            if (neighborDistance < Math.Pow(foot * 0.49, 2))
            {
                var bridge = Vec3.Lerp(neighbor, centre, 0.5) - Vec3.Up * (height * 0.055);
                Primitives.Tube(part, new[] { neighbor, bridge, centre }, new[] { 0.00045, 0.00065, 0.00045 }, 6,
                    (i, v) => (stemCol, 1, (double)i / 2, v, 0, 0));
            }
            placed.Add(centre);

            // Floating rosettes trail short roots below the film.
            double rootAzimuth = rng.Range(0, Math.Tau);
            int roots = part.FloraDetailLevel is 2 ? 1 : 3;
            for (int i = 0; i < roots; i++)
            {
                double angle = rootAzimuth + Math.Tau * i / roots;
                var spread = new Vec3(Math.Cos(angle), 0, Math.Sin(angle)) * rng.Range(0.002, 0.006);
                var mid = centre + spread * 0.35 - Vec3.Up * (height * 0.075);
                var tip = centre + spread - Vec3.Up * (height * rng.Range(0.12, 0.25));
                Primitives.Tube(part, new[] { centre, mid, tip }, new[] { 0.00034, 0.00023, 0.00010 }, 6,
                    (row, v) => (stemCol, 1, row * 0.5, v, 0, 0));
            }

            int leaves = NewFloraCount(part, 6 + rng.NextInt(2));
            double starAngle = rng.Range(0, Math.Tau);
            for (int j = 0; j < leaves; j++)
            {
                double a = starAngle + Math.Tau * j / leaves + rng.Range(-0.08, 0.08);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var tip = centre + dir * (leafLen * rng.Range(0.87, 1.12)) + Vec3.Up * (height * 0.022);
                var side = new Vec3(Math.Sin(a), 0, -Math.Cos(a));
                Primitives.CurvedLeaf(part, centre, tip, side, leafLen * rng.Range(0.19, 0.26),
                    leafBase, leafTip, camber: height * 0.018, longitudinal: 4, asymmetry: rng.Range(-0.10, 0.10));
            }
        }
        NewFloraAppendWorld(m, part, radius, height);
    }
}
