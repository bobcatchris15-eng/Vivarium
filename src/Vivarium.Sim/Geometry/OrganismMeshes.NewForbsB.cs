using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Tall forb batch B: Hallowsting, Burlmantle, Heddlelace, Fenfoam, Widowspire.
/// All builders author in real metres into a scratch part and merge with
/// <c>NewFloraAppendWorld</c>. Counts are scaled with <c>NewFloraCount</c> and
/// tessellation follows <c>FloraDetailLevel</c>/<c>Lod</c> through the shared
/// primitives. All pigment is derived from <c>c1</c>/<c>c2</c> via Mix/Scale.
/// </summary>
public static partial class OrganismMeshes
{
    // ------------------------------------------------------------------
    // Shared helpers (distinctive ForbB prefix avoids partial collisions)
    // ------------------------------------------------------------------

    private static Vec3 ForbBAt(Vec3[] path, double t)
    {
        double f = Math.Clamp(t, 0.0, 1.0) * (path.Length - 1);
        int i = Math.Min(path.Length - 2, (int)Math.Floor(f));
        return Vec3.Lerp(path[i], path[i + 1], f - i);
    }

    /// <summary>
    /// A slightly leaning, tapered stem with node-wise radius ripples.
    /// The ripple is read as ridging / squareness; Primitives.Tube promotes
    /// any sides &lt; 6 to 6, so a square read comes from the modulation.
    /// </summary>
    private static Vec3[] ForbBStem(MeshData m, Vec3 root, double length, double azimuth, double lean,
        double baseRadius, int sides, double[] col, out Vec3 tip)
    {
        int nodes = 6;
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        var horiz = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var dir = (Vec3.Up + horiz * lean).Normalized();
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            path[i] = root + dir * (length * t);
            double taper = 1.0 - 0.38 * t;
            double ripple = 1.0 + 0.08 * Math.Sin(t * Math.PI * 5.0);
            radii[i] = Math.Max(0.0004, baseRadius * taper * ripple);
        }
        Primitives.Tube(m, path, radii, sides,
            (i, v) => (col, 1.0, i / (double)nodes, v, 0, 0));
        tip = path[nodes];
        return path;
    }

    /// <summary>
    /// Gracefully curving stem: starts near-vertical, bends progressively outward so the
    /// top splays to 0.25-0.48*radius while the base stays clustered at 0.08-0.22*radius.
    /// Same ripple taper as <see cref="ForbBStem"/> so ribs still align.
    /// </summary>
    private static Vec3[] ForbBCurvedStem(MeshData m, Vec3 root, double length, double azimuth, double lean,
        double bend, double baseRadius, int sides, double[] col, out Vec3 tip)
    {
        int nodes = 8;
        var path = new Vec3[nodes + 1];
        var radii = new double[nodes + 1];
        var horiz = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var dir = (Vec3.Up + horiz * lean).Normalized();
        for (int i = 0; i <= nodes; i++)
        {
            double t = i / (double)nodes;
            // Graceful outward bow: horizontal offset grows with t^2 (curve, never a straight pole).
            path[i] = root + dir * (length * t) + horiz * (bend * t * t);
            double taper = 1.0 - 0.38 * t;
            double ripple = 1.0 + 0.08 * Math.Sin(t * Math.PI * 5.0);
            radii[i] = Math.Max(0.0004, baseRadius * taper * ripple);
        }
        Primitives.Tube(m, path, radii, sides,
            (i, v) => (col, 1.0, i / (double)nodes, v, 0, 0));
        tip = path[nodes];
        return path;
    }

    /// <summary>
    /// Longitudinal ribs/grooves following a <see cref="ForbBStem"/> path so a promoted
    /// 6-sided tube still reads as square (4 ribs) or hollow-fluted (5 grooves).
    /// Ribs are thin darker tubes inset on the stem surface; they catch light as
    /// corner ridges (square) or shadow as fluted grooves (hollow).
    /// </summary>
    private static void ForbBLongitudinalRibs(MeshData m, Vec3[] path, double baseRadius, int ribCount, double ribRadius, double[] ribCol)
    {
        if (path.Length < 2 || ribCount < 3) return;
        for (int k = 0; k < ribCount; k++)
        {
            double ang = 2.0 * Math.PI * k / ribCount + 0.4;
            var ribPath = new Vec3[path.Length];
            for (int i = 0; i < path.Length; i++)
            {
                double t = i / (double)(path.Length - 1);
                Vec3 tangent = i == 0
                    ? (path[1] - path[0]).Normalized()
                    : i == path.Length - 1
                        ? (path[^1] - path[^2]).Normalized()
                        : (path[i + 1] - path[i - 1]).Normalized();
                var side = tangent.Cross(Vec3.Up);
                if (side.LengthSq < 1e-8) side = tangent.Cross(new Vec3(1, 0, 0));
                side = side.Normalized();
                var other = tangent.Cross(side).Normalized();
                var off = (side * Math.Cos(ang) + other * Math.Sin(ang)).Normalized();
                double taper = 1.0 - 0.38 * t;
                double ripple = 1.0 + 0.08 * Math.Sin(t * Math.PI * 5.0);
                double r = Math.Max(0.0004, baseRadius * taper * ripple) * 0.82;
                ribPath[i] = path[i] + off * r;
            }
            var ribRadii = new double[path.Length];
            for (int i = 0; i < path.Length; i++)
                ribRadii[i] = Math.Max(0.0002, ribRadius * (1.0 - 0.35 * i / (double)(path.Length - 1)));
            Primitives.Tube(m, ribPath, ribRadii, 4,
                (j, v) => (ribCol, 1.0, j / (double)(path.Length - 1), v, 0, 0));
        }
    }

    /// <summary>
    /// One Sheet petal placed at <paramref name="attach"/>. Sheet builds from the
    /// origin along an Axis, so vertices are translated afterwards; the span is
    /// recorded and marked as an organ so pigment is kept.
    /// </summary>
    private static void ForbBPetal(MeshData m, Vec3 attach, double azimuth, double baseAngle, double droop,
        double length, double halfWidth, double curl, double[] baseCol, double[] tipCol, ulong seed)
    {
        if (length < 1e-5 || halfWidth <= 1e-6) return;
        int v0 = m.VertexCount, i0 = m.Indices.Count;
        var axis = new AxisParams(Length: length, BaseAngle: baseAngle, BaseAzimuth: azimuth, Droop: droop, Segments: 5);
        var p = new SheetParams(Axis: axis, HalfWidth: halfWidth, LobeCount: 0, LobeDepth: 0, FaceAxisUp: true,
            Thickness: Math.Min(0.0005, halfWidth * 0.25), Curl: curl, DetailLevel: 1);
        Sheet.Build(m, p, seed, baseCol, tipCol);
        for (int i = v0; i < m.VertexCount; i++)
        {
            var pos = m.Position(i) + attach;
            m.Positions[i * 3] = (float)pos.X;
            m.Positions[i * 3 + 1] = (float)pos.Y;
            m.Positions[i * 3 + 2] = (float)pos.Z;
        }
        m.RecordLeaf(v0, i0, attach, length);
        NewFloraMarkOrgan(m, v0, m.VertexCount - v0);
    }

    /// <summary>Small solid disc/floret (ellipsoid) with organ pigment kept.</summary>
    private static void ForbBDisc(MeshData m, Vec3 centre, double r, int rings, int segments, double[] col)
    {
        if (r < 1e-5) return;
        int v0 = m.VertexCount;
        double rr = Math.Max(0.0005, r);
        Primitives.Ellipsoid(m, centre, new Vec3(rr, rr, rr), rings, segments,
            (u, v) => (col, 1.0, u, v, 0, 0));
        NewFloraMarkOrgan(m, v0, m.VertexCount - v0);
    }

    /// <summary>
    /// A pinnate leaf: a small rachis tube with opposite FoliageBlade leaflets
    /// and a terminal leaflet. When <paramref name="interrupted"/> is set, a
    /// smaller interlobe pair sits between each main pair (Fenfoam).
    /// </summary>
    private static void ForbBPinnate(MeshData m, Rng rng, ulong seed, Vec3 root, double azimuth, double pitch,
        double length, int pairs, double leafletLen, double leafletHalfW,
        double[] rachisCol, double[] leafBase, double[] leafTip, bool interrupted)
    {
        if (length < 1e-4 || pairs < 1) return;
        var horiz = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var dir = (horiz * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
        var rachis = new[]
        {
            root,
            root + dir * (length * 0.35) + Vec3.Up * (length * 0.02),
            root + dir * (length * 0.70) - Vec3.Up * (length * 0.03),
            root + dir * length - Vec3.Up * (length * 0.08),
        };
        Primitives.Tube(m, rachis,
            new[] { 0.0025, 0.0018, 0.0012, 0.0007 }, 5,
            (i, v) => (rachisCol, 1.0, i / 3.0, v, 0, 0));
        var side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        pairs = Math.Max(1, Math.Min(6, pairs));
        for (int k = 0; k < pairs; k++)
        {
            double t = 0.20 + 0.62 * (pairs == 1 ? 0.5 : k / (double)(pairs - 1));
            var at = ForbBAt(rachis, t);
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var ldir = (side * sign * 0.85 + dir * 0.45 + Vec3.Up * 0.25).Normalized();
                double ll = leafletLen * rng.Range(0.85, 1.15) * (k == pairs - 1 ? 1.15 : 1.0);
                var tip = at + ldir * ll;
                var hint = ldir.Cross(Vec3.Up);
                if (hint.LengthSq < 1e-10) hint = new Vec3(0, 0, 1);
                FoliageBlade.Build(m, at, tip, hint, leafletHalfW, leafBase, leafTip,
                    camber: 0.14, curl: 0.10);
            }
            if (interrupted && k < pairs - 1)
            {
                double tm = 0.20 + 0.62 * (k + 0.5) / Math.Max(1, pairs - 1);
                var atM = ForbBAt(rachis, tm);
                foreach (double sign in new[] { -1.0, 1.0 })
                {
                    var ldir = (side * sign * 0.80 + dir * 0.40 + Vec3.Up * 0.22).Normalized();
                    var tip = atM + ldir * (leafletLen * 0.38 * rng.Range(0.85, 1.15));
                    var hint = ldir.Cross(Vec3.Up);
                    if (hint.LengthSq < 1e-10) hint = new Vec3(0, 0, 1);
                    FoliageBlade.Build(m, atM, tip, hint, leafletHalfW * 0.48, leafBase, leafTip,
                        camber: 0.12, curl: 0.08);
                }
            }
        }
        // Terminal leaflet continues the rachis.
        var termDir = (dir * 0.55 + Vec3.Up * 0.45).Normalized();
        var hintT = termDir.Cross(Vec3.Up);
        if (hintT.LengthSq < 1e-10) hintT = new Vec3(0, 0, 1);
        FoliageBlade.Build(m, rachis[3], rachis[3] + termDir * (leafletLen * 1.25), hintT,
            leafletHalfW, leafBase, leafTip, camber: 0.14, curl: 0.10);
    }

    private static void ForbBTerminalBud(MeshData part, Vec3 tip, double size, double[] color)
    {
        int first = part.VertexCount;
        Primitives.Ellipsoid(part, tip - Vec3.Up * (size * 0.45),
            new Vec3(size * 0.62, size, size * 0.62), 3, 6,
            (u, v) => (color, 1.0, u, v, 0.0, 0.0));
        NewFloraMarkOrgan(part, first, part.VertexCount - first);
    }
    // ------------------------------------------------------------------
    // Hallowsting (nettle analogue) â€” bushy colony, square-ish stems, serrated opposite leaves, drooping green tassels
    // ------------------------------------------------------------------
    private static void Hallowsting(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = height * (juvenile ? 0.55 : 1.0);
        // Palette from c1 alone: no purple (or any second hue) anywhere on this species.
        // Brightened ~40% for shader compensation so stems/leaves survive wash-out.
        var stemCol = Primitives.Scale(c1, 1.40);
        var leafBase = Primitives.Scale(c1, 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.55, 0.75, 0.45 }, 0.30), 1.40);
        // Tassels are pale-green drooping green catkins (c1 only, no second hue).
        // Pale-green per wave 6 but kept clearly green: lightened toward yellow-green, never white/purple.
        var catkinCol = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.60, 0.76, 0.44 }, 0.42), 1.40);
        var catkinBase = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.55, 0.71, 0.40 }, 0.48), 1.40);
        var catkinTip = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.72, 0.86, 0.55 }, 0.52), 1.40);
        var ribCol = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.12, 0.22, 0.12 }, 0.35), 1.40); // square corner shade

        // Wave-9: 5-7 stems clustered at base 0.08-0.22*radius, splaying gracefully outward
        // (tops 0.25-0.48*radius, varied heights 0.60-0.95*h) — never a vertical-pole ring.
        int stemCount = NewFloraCount(part, juvenile ? 3 : (5 + rng.NextInt(3))); // 5-7 bushy branching stems
        var stemPaths = new List<Vec3[]>();
        var stemAz = new List<double>();

        for (int s = 0; s < stemCount; s++)
        {
            double az = 2 * Math.PI * s / Math.Max(1, stemCount) + rng.Range(-0.25, 0.25);
            // Clustered base: 0.08-0.22*radius.
            var root = new Vec3(Math.Cos(az) * radius * rng.Range(0.08, 0.22), 0, Math.Sin(az) * radius * rng.Range(0.08, 0.22));
            double len = h * rng.Range(0.60, 0.95);
            // Gracefully curving splay: initial lean + progressive bow (bend) so the top
            // lands 0.25-0.48*radius. The bow amplitude is a fraction of the crown radius.
            double bend = radius * rng.Range(0.12, 0.28) * (juvenile ? 0.7 : 1.0);
            var path = ForbBCurvedStem(part, root, len, az, rng.Range(0.05, 0.14), bend, height * 0.015, 4, stemCol, out _);
            ForbBTerminalBud(part, path[^1], h * 0.016, catkinCol);
            ForbBLongitudinalRibs(part, path, height * 0.015, ribCount: 4, ribRadius: 0.0011, ribCol);
            stemPaths.Add(path);
            stemAz.Add(az);
        }

        // Short flowering laterals rise from upper axils and bend into hanging tassels.
        const int floweringLateralsPerStem = 2;
        var branchTips = new List<Vec3>();
        for (int s = 0; !juvenile && s < stemCount; s++)
        {
            for (int b = 0; b < floweringLateralsPerStem; b++)
            {
                var baseP = ForbBAt(stemPaths[s], 0.61 + b * 0.20);
                double az = stemAz[s] + rng.Range(-1.1, 1.1) + b * 1.7;
                var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                double length = h * rng.Range(0.07, 0.11);
                var mid = baseP + outward * (length * 0.55) + Vec3.Up * (length * 0.25);
                var end = baseP + outward * length + Vec3.Up * (length * 0.12);
                Primitives.Tube(part, new[] { baseP, mid, end }, new[] { 0.0022, 0.0016, 0.0009 }, 5,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
                branchTips.Add(end);
            }
        }

        // Dense opposite tooth-serrated cordate-ovate leaves: 5 pairs per stem (every ~9-10 cm), decussate.
        // Wave-9: larger blades (14-20 cm mid-stem, tapering to ~7 cm at apex) that DROOP
        // downward 30-45 deg so they overlap like a real stinging-nettle patch — never
        // sticking straight out horizontally like a trellis.
        for (int s = 0; s < stemCount; s++)
        {
            int pairsHere = juvenile ? 3 : rng.NextInt(3) + 4;
            for (int k = 0; k < pairsHere; k++)
            {
                bool terminalPair = !juvenile && k == pairsHere - 1;
                double t = terminalPair ? 0.97 : 0.16 + 0.62 * k / (pairsHere - 1);
                var attach = ForbBAt(stemPaths[s], t);
                double pairAz = stemAz[s] + k * (Math.PI / 2) + rng.Range(-0.15, 0.15);
                // Strong taper: mid-stem hero leaves 14-20 cm, apex ~7 cm (h-scaled).
                double sizeScale = (1.0 - 0.55 * t) * (juvenile ? 0.70 : 1.0);
                double bladeLen = h * rng.Range(0.28, 0.33) * sizeScale;
                bladeLen = Math.Max(h * 0.11, bladeLen); // apex floor ~7 cm at typical h
                if (terminalPair) bladeLen *= 0.64;
                double bladeWid = bladeLen * 0.66;
                var outline = BroadleafOutline.Cordate;
                int detailOverride = k < 2 ? 1 : 2;
                foreach (double sign in new[] { -1.0, 1.0 })
                {
                    double az = pairAz + (sign < 0 ? 0 : Math.PI);
                    // Drooping downward 30-45 deg below horizontal.
                    double droopBelow = terminalPair ? rng.Range(-0.30, -0.08) : rng.Range(0.52, 0.79);
                    var ldir = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * Math.Cos(droopBelow) - Vec3.Up * Math.Sin(droopBelow)).Normalized();
                    ulong ls = Rng.Mix(seed, (ulong)(s * 401 + k * 37 + (sign < 0 ? 1 : 2)));
                    Broadleaf.BuildLeaf(part, attach, ldir, bladeLen, bladeWid, outline, ls,
                        leafBase, leafTip, age: 0.80 - t * 0.25, camber: 0.15, midribFold: 0.20,
                        wavyMargin: 0.45, wavyFrequency: 20.0, tipCurl: 0.10,
                        petioleLength: bladeLen * 0.30, petioleRadius: 0.0025, petioleKink: 0.30,
                        detailOverride: detailOverride);
                }
            }
        }

        for (int i = 0; i < branchTips.Count; i++)
        {
            int s = i / floweringLateralsPerStem;
            var axil = branchTips[i];
            double az = stemAz[s] + rng.Range(-0.6, 0.6);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            // Long, near-vertical hang so the spike reads as a drooping tassel, not a twig or head.
            // Lengthened + densified + widened so pale-green catkins are findable from the wide
            // view (wave 6: previously too thin/sparse and read as a support trellis);
            // pedicel kept short and tight so the tassel hangs close to the stem (heads up, scapes short).
            double len = h * 0.46;
            var mid = axil + outward * (len * 0.10) - Vec3.Up * (len * 0.32);
            var end = axil + outward * (len * 0.12) - Vec3.Up * (len * 0.92);
            Primitives.Tube(part, new[] { axil, mid, end }, new[] { 0.0042, 0.0029, 0.0016 }, 5,
                (j, v) => (catkinCol, 1.0, j * 0.5, v, 0, 0));
            // Tiny bead bractlets (never large flat discs): small bead discs along the spike.
            for (int cd = 0; cd < 7; cd++)
            {
                double ct = 0.12 + 0.76 * cd / 6.0;
                ForbBDisc(part, Vec3.Lerp(axil, end, ct), 0.006, rings: 2, segments: 5, col: catkinCol);
            }
            int bracts = NewFloraCount(part, 30 + rng.NextInt(7)); // 22â€“26 densely packed bracts on the hanging spike
            for (int b = 0; b < bracts; b++)
            {
                double bt = 0.06 + 0.92 * (bracts <= 1 ? 0.5 : b / (double)(bracts - 1));
                var root = Vec3.Lerp(axil, end, bt);
                var bdir = (outward * 0.30 - Vec3.Up * 0.95).Normalized();
                var tip = root + bdir * h * rng.Range(0.075, 0.100);
                var hint = bdir.Cross(outward);
                if (hint.LengthSq < 1e-10) hint = new Vec3(0, 0, 1);
                FoliageBlade.Build(part, root, tip, hint, 0.016, catkinBase, catkinTip, camber: 0.08, curl: 0.45);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ------------------------------------------------------------------
    // Burlmantle (burdock analogue) â€” giant woolly rosette + tall stems + hooked burr heads with purple tufts
    // ------------------------------------------------------------------
    private static void Burlmantle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = height * (juvenile ? 0.55 : 1.0);
        // Brightened ~40% for shader compensation (wave 6: felt/burs were unreadable, so felt is mattened).
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.30), 1.40);
        // Matte grey-green felt on the top face too (not just the underside, which a top-down
        // wide shot never sees): both tones are pulled toward matte grey so the rosette
        // reads as woolly felt from above. c2 stays reserved for the purple tufts on the burs.
        var leafBase = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.82, 0.84, 0.74 }, 0.52), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.85, 0.87, 0.77 }, 0.58), 1.40);
        var wool = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.92, 0.93, 0.84 }, 0.85), 1.40);

        // Wave-9: basal rosette SHORTENED to 0.30-0.42*radius (18-26 cm), pitched UP 25-45 deg
        // in a multi-layered rosette — never a giant flat tarp on the ground.
        int leaves = NewFloraCount(part, juvenile ? 4 : (8 + rng.NextInt(3))); // 8-10 broad woolly leaves
        for (int i = 0; i < leaves; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, leaves) + rng.Range(-0.20, 0.20);
            double u = leaves <= 1 ? 0 : i / (double)(leaves - 1); // oldest outer, largest
            double pitch = MathD.Lerp(1.15, 0.78, u);               // 25-45 deg above horizontal, outer flattest
            var dir = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            double bladeLen = radius * rng.Range(0.30, 0.42) * (1.05 - 0.25 * u);
            double bladeWid = bladeLen * 0.62;
            ulong ls = Rng.Mix(seed, (ulong)(i * 613 + 7));
            Broadleaf.BuildLeaf(part, new Vec3(0, 0.008, 0), dir, bladeLen, bladeWid, BroadleafOutline.Cordate, ls,
                leafBase, leafTip, age: 1.0 - u * 0.45, camber: 0.18, midribFold: 0.15,
                wavyMargin: 0.16, wavyFrequency: 6.0, tipCurl: 0.32,
                petioleLength: bladeLen * 0.30, petioleRadius: 0.006, petioleKink: 0.65,
                undersideColor: wool);
        }

        // Second, smaller inner rosette ring for fullness (offset azimuths).
        int ring2 = NewFloraCount(part, 3 + rng.NextInt(2)); // 3-4
        for (int i = 0; i < ring2; i++)
        {
            double az = 2 * Math.PI * (i + 0.5) / Math.Max(1, ring2) + rng.Range(-0.18, 0.18);
            double pitch = rng.Range(0.60, 0.95); // steeper, more upright inner layer
            var dir = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * Math.Sin(pitch) + Vec3.Up * Math.Cos(pitch)).Normalized();
            double bladeLen = radius * rng.Range(0.22, 0.32);
            double bladeWid = bladeLen * 0.62;
            ulong ls = Rng.Mix(seed, (ulong)(7919 + i * 617));
            Broadleaf.BuildLeaf(part, new Vec3(0, 0.010, 0), dir, bladeLen, bladeWid, BroadleafOutline.Cordate, ls,
                leafBase, leafTip, age: 0.65, camber: 0.18, midribFold: 0.15,
                wavyMargin: 0.16, wavyFrequency: 6.0, tipCurl: 0.32,
                petioleLength: bladeLen * 0.30, petioleRadius: 0.005, petioleKink: 0.65,
                undersideColor: wool);
        }

        // Wave-9: 3-4 branching central stems, THICK base (10-14 mm), rising to 0.65-0.85*h,
        // with smaller cauline leaves continuing up the stems to 0.55*h.
        int stems = juvenile ? 1 : (3 + rng.NextInt(2)); // 3-4
        var stemTips = new List<Vec3>();
        var stemPaths = new List<Vec3[]>();
        for (int s = 0; s < stems; s++)
        {
            double az = rng.Range(0, 2 * Math.PI);
            double baseR = rng.Range(0.010, 0.014);
            var path = ForbBStem(part, new Vec3(0, 0.01, 0), h * rng.Range(0.62, 0.82), az, rng.Range(0.18, 0.34),
                baseR, 6, stemCol, out var tip);
            stemTips.Add(tip);
            stemPaths.Add(path);
            ForbBTerminalBud(part, tip, h * 0.018, leafTip);
            // Sturdy branched stalk: two lateral branches so burrs hang from a branched frame.
            for (int b = 0; b < 2; b++)
            {
                var bBase = ForbBAt(path, 0.55 + b * 0.20);
                double bAz = az + rng.Range(-1.0, 1.0) + b * 1.8;
                var bOut = new Vec3(Math.Cos(bAz), 0, Math.Sin(bAz));
                var bMid = bBase + bOut * (h * 0.05) + Vec3.Up * (h * 0.04);
                var bEnd = bBase + bOut * (h * 0.09) + Vec3.Up * (h * 0.08);
                Primitives.Tube(part, new[] { bBase, bMid, bEnd }, new[] { 0.0028, 0.0018, 0.0010 }, 5,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
            }
            int upper = NewFloraCount(part, 4); // cauline leaves per stem
            for (int k = 0; k < upper; k++)
            {
                double t = 0.15 + 0.40 * (upper <= 1 ? 0.5 : k / (double)(upper - 1)); // spread 0.15-0.55*h
                var attach = ForbBAt(path, t);
                double lz = az + rng.Range(0, 2 * Math.PI);
                var ldir = (new Vec3(Math.Cos(lz), 0, Math.Sin(lz)) * 0.85 + Vec3.Up * 0.45).Normalized();
                var tipP = attach + ldir * (h * 0.11);
                var hint = tipP.Cross(Vec3.Up);
                if (hint.LengthSq < 1e-10) hint = new Vec3(0, 0, 1);
                FoliageBlade.Build(part, attach, tipP, hint, h * 0.028, leafBase, leafTip, camber: 0.15, curl: 0.12);
            }
        }

        // Wave-9: 12-16 large (3.5-4.5 cm diameter) spherical chestnut-brown spiky hooked burrs
        // crowned with a vivid magenta-purple floret brush across 0.45-0.80*h.
        int heads = juvenile ? 0 : NewFloraCount(part, 8 + rng.NextInt(3)); // 12-16
        for (int hd = 0; hd < heads; hd++)
        {
            int s = hd % Math.Max(1, stems);
            Vec3 hangBase;
            bool terminal = hd < stems; // one terminal burr per main axis, remaining heads stay in lateral axils
            if (terminal)
            {
                hangBase = stemTips[s];
            }
            else
            {
                double t = rng.Range(0.45, 0.80);
                var baseP = ForbBAt(stemPaths[s], t);
                double az = rng.Range(0, 2 * Math.PI);
                var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                hangBase = baseP + outward * (h * 0.045) + Vec3.Up * (h * 0.015);
                Primitives.Tube(part, new[] { baseP, hangBase }, new[] { 0.0020, 0.0014 }, 5,
                    (j, v) => (stemCol, 1.0, j, v, 0, 0));
            }
            // Large hooked husk so the burr reads at wide distance, never a berry globe:
            // the hooked bracts dominate the body so the head reads as a burdock burr.
            // Wave 6: previous ~5 cm husk was invisible behind the giant leaves, so the
            // core and hooks are ~50% larger and the bracts carry the purple (c2) hue.
            // Wave-8: spherical hooked brown-green burrs 2.5-3.2 cm diameter, crowned with a
            // brush of magenta-purple tubular florets (never an upright thistle head).
            double coreR = rng.Range(0.0175, 0.0225); // 3.5-4.5 cm diameter
            // Hanging globe: the burr dangles below its pedicel end on a short drooping stalk,
            // so it reads as a hooked burdock burr, never as an upright thistle head.
            double hAz = rng.Range(0, 2 * Math.PI);
            var hOut = new Vec3(Math.Cos(hAz), 0, Math.Sin(hAz));
            var hMid = hangBase + hOut * (coreR * 0.4) - Vec3.Up * (coreR * 0.4);
            var centre = hangBase + hOut * (coreR * 0.5) - Vec3.Up * (coreR * 1.0);
            Primitives.Tube(part, new[] { hangBase, hMid, centre }, new[] { 0.0016, 0.0010, 0.0006 }, 5,
                (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
            var burrCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.52), 1.40); // chestnut-brown leaning
            var burrBrown = new double[] { 0.42, 0.20, 0.10 };
            burrCol = Primitives.Scale(Primitives.Mix(burrCol, burrBrown, 0.55), 1.40);
            ForbBDisc(part, centre, coreR, rings: 4, segments: 8, col: burrCol);
            int bracts = NewFloraCount(part, 28 + rng.NextInt(7)); // 28â€“34 hooked bracts
            var bractCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.55), 1.40);
            for (int b = 0; b < bracts; b++)
            {
                double theta = b * 2.399963229728653 + rng.Range(-0.05, 0.05);
                double y = 1.0 - (b + 0.5) / Math.Max(1, bracts);
                double rr = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                var dir = new Vec3(Math.Cos(theta) * rr, y, Math.Sin(theta) * rr);
                var baseP = centre + dir * (coreR * 0.80);
                var midP = centre + dir * (coreR * 3.20);
                // Hook: the long tip curls back toward the head (inward + sideways) â€” the burdock hook.
                var tang = new Vec3(-dir.Z, 0, dir.X);
                if (tang.LengthSq < 1e-6) tang = new Vec3(1, 0, 0);
                tang = tang.Normalized();
                var tipP = midP - dir * (coreR * 1.40) + tang * (coreR * rng.Range(0.35, 0.60)) - Vec3.Up * (coreR * 0.12);
                Primitives.Tube(part, new[] { baseP, midP, tipP },
                    new[] { 0.0024, 0.0014, 0.00045 }, 5,
                    (j, v) => (bractCol, 1.0, j * 0.5, v, 0, 0));
            }
            // Wave-9: vivid magenta-purple floret brush crown (never a modest tuft).
            var magenta = new double[] { 0.85, 0.15, 0.75 };
            var tuftCol = Primitives.Scale(Primitives.Mix(c2, magenta, 0.70), 1.40);
            var tuftC = centre + Vec3.Up * (coreR * 0.85);
            ForbBDisc(part, tuftC, coreR * 0.55, rings: 3, segments: 6, col: tuftCol);
            int spikes = NewFloraCount(part, 12); // brush of tubular florets, not a tuft
            for (int i = 0; i < spikes; i++)
            {
                double az = 2 * Math.PI * i / Math.Max(1, spikes) + rng.Range(-0.1, 0.1);
                var from = tuftC + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (coreR * 0.30);
                var to = from + Vec3.Up * (coreR * 1.40) + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (coreR * 0.15);
                Primitives.Tube(part, new[] { from, to }, new[] { 0.0006, 0.00025 }, 4,
                    (j, v) => (tuftCol, 1.0, j, v, 0, 0));
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ------------------------------------------------------------------
    // Heddlelace (cowparsley analogue) â€” hollow ridged stems, pinnate rosette, white lace umbels
    // ------------------------------------------------------------------
    private static void Heddlelace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = height * (juvenile ? 0.55 : 1.0);
        // Brightened ~40% for shader compensation.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.30), 1.40);
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.15), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.42), 1.40);
        var paleCol = Primitives.Scale(Primitives.Mix(c2, new double[] { 1.0, 1.0, 1.0 }, 0.50), 1.40); // brighter, whiter lace
        var grooveCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.12), 1.40); // hollow flute shadow

        // Rosette of 3-4 large lacy bipinnate leaves, kept low so umbels ride high.
        // Feathery (finely divided) leaflets so the basal leaves read as cowparsley
        // division, not as broad whole leaves.
        int rosLeaves = NewFloraCount(part, 3 + rng.NextInt(2));
        for (int i = 0; i < rosLeaves; i++)
        {
            double az = 2 * Math.PI * i / Math.Max(1, rosLeaves) + rng.Range(-0.2, 0.2);
            int pairs = 6 + rng.NextInt(2); // 5â€“6 pairs
            ForbBPinnate(part, rng, Rng.Mix(seed, (ulong)(i * 211 + 3)), new Vec3(0, 0.006, 0),
                az, rng.Range(0.55, 0.95), radius * rng.Range(0.65, 0.80) * (juvenile ? 0.7 : 1.0),
                pairs, radius * 0.080, radius * 0.018, stemCol, leafBase, leafTip, interrupted: false);
        }

        // Wave-9: stems branched at 0.35-0.65*h; umbels at staggered 0.48-0.82*h (never 0.90-1.0 clipped).
        // Ferny bipinnate leaves clothing lower/mid stems 0.12-0.55*h.
        int stems = NewFloraCount(part, 3 + rng.NextInt(2)); // 3-4
        var stemPaths = new List<Vec3[]>();
        var stemTips = new List<Vec3>();
        for (int s = 0; s < stems; s++)
        {
            double az = 2 * Math.PI * s / Math.Max(1, stems) + rng.Range(-0.2, 0.2);
            var path = ForbBStem(part, new Vec3(0, 0.008, 0), h * 1.0, az, rng.Range(0.05, 0.12),
                height * 0.014, 6, stemCol, out var tip);
            ForbBLongitudinalRibs(part, path, height * 0.014, ribCount: 5, ribRadius: 0.0016, grooveCol);
            stemPaths.Add(path);
            stemTips.Add(tip);
            ForbBTerminalBud(part, tip, h * 0.014, paleCol);
            // Branching at 0.35-0.65*h so umbels sit staggered in the macro frame.
            for (int b = 0; b < 2; b++)
            {
                var bBase = ForbBAt(path, 0.35 + b * 0.30);
                double bAz = az + rng.Range(-1.0, 1.0) + b * 1.8;
                var bOut = new Vec3(Math.Cos(bAz), 0, Math.Sin(bAz));
                var bMid = bBase + bOut * (h * 0.04) + Vec3.Up * (h * 0.03);
                var bEnd = bBase + bOut * (h * 0.08) + Vec3.Up * (h * 0.07);
                Primitives.Tube(part, new[] { bBase, bMid, bEnd }, new[] { 0.0018, 0.0012, 0.0007 }, 5,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
            }
            // Three ferny bipinnate stem leaves clothing the lower/mid stem 0.12-0.55*h.
            for (int sl = 0; sl < 3; sl++)
            {
                var mid = ForbBAt(path, 0.12 + sl * 0.215); // 0.12, 0.335, 0.55
                ForbBPinnate(part, rng, Rng.Mix(seed, (ulong)(97 + s * 13 + sl * 7)), mid,
                    az + 1.5 + sl * 1.2 + rng.Range(-0.4, 0.4), 0.85, radius * 0.50, 3, radius * 0.075, radius * 0.018,
                    stemCol, leafBase, leafTip, interrupted: false);
            }
        }

        // Wave-9: 7-9 white lace umbels at staggered 0.48-0.82*h, half in the macro-frame centre.
        int umbels = juvenile ? 0 : NewFloraCount(part, 7 + rng.NextInt(3)); // 7-9
        for (int u = 0; u < umbels; u++)
        {
            int s = u % Math.Max(1, stems);
            Vec3 baseP = ForbBAt(stemPaths[s], u < stems ? 1.0 : rng.Range(0.48, 0.82));
            if (u % 2 == 0 && u >= stems)
            {
                // Staggered lateral umbels off the stem branches for spread.
                var from = ForbBAt(stemPaths[s], rng.Range(0.48, 0.82));
                var outward = new Vec3(Math.Cos(rng.Range(0, 2 * Math.PI)), 0, Math.Sin(rng.Range(0, 2 * Math.PI)));
                baseP = from + outward * (h * 0.04) + Vec3.Up * (h * 0.02);
                Primitives.Tube(part, new[] { from, baseP }, new[] { 0.0012, 0.0008 }, 5,
                    (j, v) => (stemCol, 1.0, j, v, 0, 0));
            }
            int rays = NewFloraCount(part, 8 + rng.NextInt(5)); // 12â€“17 rays
            // Flat lace head: long rays near-horizontal so ray tips sit coplanar â€” a compound
            // umbel, never the upright foam cone of Fenfoam. Enlarged +10% so the lace reads
            // slightly larger than wave 5 (polish only; the flat umbel already read at 5).
            double rayLen = Math.Max(radius * 0.24, h * 0.043) * (juvenile ? 0.7 : 1.0);
            double tilt = rng.Range(1.15, 1.35); // umbrella spoke spread from vertical: flat-topped
            // Central umbellet at the hub so the head reads as one compound umbel.
            var hub = baseP + Vec3.Up * 0.012;
            ForbBDisc(part, hub, 0.013, rings: 2, segments: 5, col: paleCol);
            for (int cp = 0; cp < 3; cp++)
            {
                double paz = rng.Range(0, 2 * Math.PI);
                ForbBPetal(part, hub, paz, baseAngle: 0.85, droop: 0.12,
                    length: 0.040, halfWidth: 0.0132, curl: 0.25,
                    baseCol: paleCol, tipCol: paleCol,
                    seed: Rng.Mix(seed, (ulong)(u * 777 + cp * 13 + 59)));
            }
            for (int i = 0; i < rays; i++)
            {
                double az = 2 * Math.PI * i / Math.Max(1, rays) + rng.Range(-0.06, 0.06);
                var horiz = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                var rdir = (horiz * Math.Sin(tilt) + Vec3.Up * Math.Cos(tilt)).Normalized();
                var mid = baseP + rdir * (rayLen * 0.55) + Vec3.Up * (rayLen * 0.08);
                var tip = baseP + rdir * rayLen + Vec3.Up * (rayLen * 0.12);
                Primitives.Tube(part, new[] { baseP, mid, tip }, new[] { 0.0008, 0.0006, 0.00035 }, 4,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
                // Umbellet cluster at the ray tip: dense white petalled florets so the head reads as lace, not dots.
                int petals = 5;
                ForbBDisc(part, tip, 0.0165, rings: 2, segments: 5, col: paleCol);
                // Filler florets around the tip.
                for (int fl = 0; fl < 2; fl++)
                {
                    var off = new Vec3(rng.Range(-0.018, 0.018), rng.Range(-0.002, 0.005), rng.Range(-0.018, 0.018));
                    ForbBDisc(part, tip + off, rng.Range(0.0075, 0.0102), rings: 2, segments: 5, col: paleCol);
                }
                for (int pIdx = 0; pIdx < petals; pIdx++)
                {
                    double paz = az + 2 * Math.PI * pIdx / Math.Max(1, petals) + rng.Range(-0.25, 0.25);
                    ForbBPetal(part, tip, paz, baseAngle: 0.85, droop: 0.12,
                        length: 0.036, halfWidth: 0.0115, curl: 0.25,
                        baseCol: paleCol, tipCol: paleCol,
                        seed: Rng.Mix(seed, (ulong)(u * 1009 + i * 101 + pIdx * 7 + 5)));
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ------------------------------------------------------------------
    // Fenfoam (meadowsweet analogue) â€” reddish stems, interruptedly pinnate leaves, frothy cream plumes
    // ------------------------------------------------------------------
    private static void Fenfoam(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = height * (juvenile ? 0.55 : 1.0);
        // Wine-reddish stems: darkened palette tipped toward brown-red â€” the wine-red read
        // must survive in the render, so the brown-red pull is strong.
        // Brightened ~40% for shader compensation (wine-red still dominant).
        var brownRed = new double[] { 0.36, 0.14, 0.14 };
        var stemCol = Primitives.Mix(Primitives.Scale(c1, 1.15), brownRed, 0.55);
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.14), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.40), 1.40);
        var creamCol = Primitives.Scale(Primitives.Mix(c2, new double[] { 1.0, 0.88, 0.62 }, 0.50), 1.40); // warmer cream

        // Wave-9: 4-5 dark crimson-wine stems; panicles at staggered 0.48-0.82*h, never 0.90-1.0 clipped.
        int stems = NewFloraCount(part, 4 + rng.NextInt(2)); // 4-5
        var stemPaths = new List<Vec3[]>();
        var stemTips = new List<Vec3>();
        for (int s = 0; s < stems; s++)
        {
            double az = 2 * Math.PI * s / Math.Max(1, stems) + rng.Range(-0.2, 0.2);
            double bend = radius * rng.Range(0.08, 0.18);
            var path = ForbBCurvedStem(part, new Vec3(Math.Cos(az) * radius * 0.15, 0, Math.Sin(az) * radius * 0.15),
                h * 0.82, az, rng.Range(0.04, 0.09), bend, height * 0.009, 6, stemCol, out var tip);
            stemPaths.Add(path);
            stemTips.Add(tip);
            ForbBTerminalBud(part, tip, h * 0.014, creamCol);
            // Interruptedly pinnate leaves across 0.14-0.68*h: lush tiers framing the upper stem under the foam.
            int leavesHere = NewFloraCount(part, 5);
            for (int k = 0; k < leavesHere; k++)
            {
                double t = 0.14 + 0.54 * (leavesHere <= 1 ? 0.5 : k / (double)(leavesHere - 1)); // 0.14-0.68
                var at = ForbBAt(path, t);
                ForbBPinnate(part, rng, Rng.Mix(seed, (ulong)(s * 373 + k * 41 + 11)), at,
                    az + rng.Range(0, 2 * Math.PI), rng.Range(0.70, 0.95), radius * 0.36,
                    3, radius * 0.085, radius * 0.027, stemCol, leafBase, leafTip, interrupted: true);
            }
        }
        // Basal leaf for the silhouette at the crown.
        if (stems > 0)
            ForbBPinnate(part, rng, Rng.Mix(seed, 19), new Vec3(0, 0.006, 0),
                rng.Range(0, 2 * Math.PI), 0.80, radius * 0.42, 3, radius * 0.095, radius * 0.030,
                stemCol, leafBase, leafTip, interrupted: true);

        // Wave-9: 6-8 fluffy pyramidal creamy-white foam panicles at staggered 0.48-0.82*h,
        // right in the macro frame (none on juveniles).
        int plumes = juvenile ? 0 : NewFloraCount(part, 6 + rng.NextInt(3)); // 6-8
        for (int pl = 0; pl < plumes; pl++)
        {
            int s = pl % Math.Max(1, stems);
            // Peduncle from upper stem; branches radiate from its top. Base at 0.48-0.82*h.
            var pedBase = ForbBAt(stemPaths[s], pl < stems ? 1.0 : rng.Range(0.42, 0.66));
            if (pl % 2 == 1 && pl >= stems)
            {
                var from = ForbBAt(stemPaths[s], rng.Range(0.42, 0.66));
                var outv = new Vec3(Math.Cos(rng.Range(0, 2 * Math.PI)), 0, Math.Sin(rng.Range(0, 2 * Math.PI)));
                pedBase = from + outv * (h * 0.035) + Vec3.Up * (h * 0.015);
                Primitives.Tube(part, new[] { from, pedBase }, new[] { 0.0010, 0.0006 }, 4,
                    (j, v) => (stemCol, 1.0, j, v, 0, 0));
            }
            // Short peduncle so foam tops stay inside 0.48-0.82*h (never clipped at 0.90-1.0).
            double pedLen = h * rng.Range(0.025, 0.045);
            var pedTop = pedBase + Vec3.Up * pedLen + new Vec3(rng.Range(-0.01, 0.01), 0, rng.Range(-0.01, 0.01));
            Primitives.Tube(part, new[] { pedBase, pedTop }, new[] { 0.0011, 0.0006 }, 4,
                (j, v) => (stemCol, 1.0, j, v, 0, 0));
            int branches = 6 + rng.NextInt(3); // 6-8 branches keep the cream plume conical and open
            for (int br = 0; br < branches; br++)
            {
                double az = 2 * Math.PI * br / Math.Max(1, branches) + rng.Range(-0.2, 0.2);
                var tilt = rng.Range(0.25, 0.55);
                var bdir = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * Math.Sin(tilt) + Vec3.Up * Math.Cos(tilt)).Normalized();
                double bLen = pedLen * rng.Range(1.35, 1.55);
                var bMid = pedTop + bdir * (bLen * 0.55);
                var bTip = pedTop + bdir * bLen;
                Primitives.Tube(part, new[] { pedTop, bMid, bTip }, new[] { 0.0006, 0.0004, 0.00025 }, 4,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));

                int florets = NewFloraCount(part, 4);
                for (int f = 0; f < florets; f++)
                {
                    double bt = 0.20 + 0.80 * (florets <= 1 ? 0.5 : (f + 0.5) / (double)florets);
                    var c = Vec3.Lerp(pedTop, bTip, bt) + new Vec3(rng.Range(-0.009, 0.009), rng.Range(-0.002, 0.006), rng.Range(-0.009, 0.009));
                    double prog = Math.Clamp((bt - 0.20) / 0.80, 0.0, 1.0);
                    var budCol = new double[] { 0.68, 0.74, 0.52 };
                    var blossomCol = new double[] { 0.90, 0.86, 0.72 };
                    var floretCol = Primitives.Mix(budCol, blossomCol, prog);
                    double rFloret = rng.Range(0.0045, 0.0070);
                    int floretV0 = part.VertexCount;
                    Primitives.Ellipsoid(part, c, new Vec3(rFloret * 1.15, rFloret * 0.78, rFloret * 0.72), 3, 6,
                        (u, v) => (floretCol, 1.0, u, v, 0.0, 0.0));
                    NewFloraMarkOrgan(part, floretV0, part.VertexCount - floretV0);
                }
                if (br == 0 || br == branches / 2)
                {
                    // A few fine open florets punctuate the outer edge of the foam.
                    for (int petal = 0; petal < 3; petal++)
                        ForbBPetal(part, bTip, az + petal * (2.0 * Math.PI / 3.0), baseAngle: 0.92, droop: 0.08,
                            length: 0.020, halfWidth: 0.0045, curl: 0.28,
                            baseCol: new double[] { 0.90, 0.86, 0.72 }, tipCol: creamCol,
                            seed: Rng.Mix(seed, (ulong)(pl * 701 + br * 31 + petal * 5 + 17)));
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ------------------------------------------------------------------
    // Widowspire (loosestrife analogue) â€” square stems, narrow opposite leaves, magenta flower spikes
    // ------------------------------------------------------------------
    private static void Widowspire(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double h = height * (juvenile ? 0.55 : 1.0);
        int floraDetail = m.FloraDetailLevel ?? 0;
        // Brightened ~40% for shader compensation.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.25), 1.40);
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.14), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.40), 1.40);
        var magentaCol = Primitives.Scale(c2, 1.40); // strongly saturated magenta from c2 (never diluted)
        var darkCol = Primitives.Scale(c2, 0.85); // darker centre, darkened base then brightened ~40%
        var ribCol = Primitives.Scale(Primitives.Mix(c1, new double[] { 0.08, 0.15, 0.10 }, 0.45), 1.40); // square corner shade

        int stems = NewFloraCount(part, 3 + rng.NextInt(3)); // 3â€“5
        var stemPaths = new List<Vec3[]>();
        var stemAz = new List<double>();
        for (int s = 0; s < stems; s++)
        {
            double az = 2 * Math.PI * s / Math.Max(1, stems) + rng.Range(-0.2, 0.2);
            // Square stems: sides 4 requested (promoted to 6) with radius ripples.
            // Four longitudinal corner ribs carry the square read that promotion erases.
            // Wave 6: stems/ribs thickened + corner shadow darkened so squareness reads
            // below the magenta spires instead of as a plain round pole.
            var path = ForbBStem(part,
                new Vec3(Math.Cos(az) * radius * 0.18, 0, Math.Sin(az) * radius * 0.18),
                h * rng.Range(0.82, 0.90), az, rng.Range(0.04, 0.09), height * 0.011, 4, stemCol, out _);
            ForbBLongitudinalRibs(part, path, height * 0.011, ribCount: 4, ribRadius: 0.0025, ribCol);
            stemPaths.Add(path);
            stemAz.Add(az);
            ForbBTerminalBud(part, path[^1], h * 0.014, magentaCol);
        }

        // Narrow willow leaves in close-set opposite pairs, decussate (LeafBlade, lanceolate).
        for (int s = 0; s < stems; s++)
        {
            int whorls = NewFloraCount(part, 7);
            for (int k = 0; k < whorls; k++)
            {
                double t = 0.15 + 0.34 * (whorls <= 1 ? 0.5 : k / (double)(whorls - 1));
                var attach = ForbBAt(stemPaths[s], t);
                double baseAz = stemAz[s] + k * 1.05;
                for (int leaf = 0; leaf < 2; leaf++)
                {
                    double az = baseAz + leaf * Math.PI + rng.Range(-0.08, 0.08);
                    int v0 = part.VertexCount, i0 = part.Indices.Count;
                    int leafDetail = 2 - Math.Clamp(floraDetail, 0, 2); // invert: full FloraDetail -> high LeafDetail
                    // Narrow lanceolate (not broad) so willow opposites read instead of rhubarb.
                    // Iter 2: slightly longer + narrower so opposite pairs read below the spikes.
                    double len = h * 0.170 * (1.0 - 0.25 * t) * rng.Range(0.90, 1.10);
                    var axis = new AxisParams(Length: len, BaseAngle: rng.Range(1.05, 1.30), BaseAzimuth: az, Droop: 0.35, Segments: 6);
                    var p = new LeafBladeParams(Midrib: axis, Profile: BladeProfile.Lanceolate,
                        HalfWidth: len * 0.092, Camber: 0.12, MidribFold: 0.05, Cup: 0.0, TipCurl: 0.25,
                        EdgeRuffle: 0.0, Asymmetry: 0.0, Margin: LeafMargin.Entire, MarginAmplitude: 0.0,
                        MarginFrequency: 0.0, MidribThickness: 0.0008, PetioleLength: 0.0, PetioleRadius: 0.0005,
                        Attach: Vec3.Zero, DetailLevel: leafDetail);
                    LeafBlade.Build(part, p, Rng.Mix(seed, (ulong)(s * 503 + k * 59 + leaf * 7 + 31)), leafBase, leafTip);
                    for (int i = v0; i < part.VertexCount; i++)
                    {
                        var pos = part.Position(i) + attach;
                        part.Positions[i * 3] = (float)pos.X;
                        part.Positions[i * 3 + 1] = (float)pos.Y;
                        part.Positions[i * 3 + 2] = (float)pos.Z;
                    }
                    part.RecordLeaf(v0, i0, attach, len);
                }
            }
        }

        // 4â€“8 magenta spikes in the upper 40% of the stems.
        int spikes = juvenile ? 0 : Math.Max(stems, NewFloraCount(part, 4 + rng.NextInt(3))); // 4â€“6 full, scales down
        // Terminal spike per stem plus lateral spikes from upper axils.
        for (int sp = 0; sp < spikes; sp++)
        {
            int s = sp % Math.Max(1, stems);
            var path = stemPaths[s];
            bool terminal = sp < stems;
            Vec3 spikeBase = ForbBAt(path, 0.70);
            Vec3 spikeTop = path[^1];
            if (!terminal)
            {
                var from = ForbBAt(path, rng.Range(0.60, 0.82));
                double az = rng.Range(0, 2 * Math.PI);
                var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                spikeBase = from + outward * (h * 0.055) + Vec3.Up * (h * 0.01);
                spikeTop = spikeBase + (Vec3.Up + outward * 0.12) * (h * rng.Range(0.14, 0.19));
                Primitives.Tube(part, new[] { from, spikeBase, spikeTop }, new[] { 0.0012, 0.0009, 0.0005 }, 4,
                    (j, v) => (stemCol, 1.0, j * 0.5, v, 0, 0));
                ForbBTerminalBud(part, spikeTop, h * 0.010, magentaCol);
            }
            int flowers = NewFloraCount(part, 14); // dense magenta whorls up the spire
            for (int f = 0; f < flowers; f++)
            {
                double t = 0.04 + 0.92 * (flowers <= 1 ? 0.5 : f / (double)(flowers - 1));
                var centre = terminal ? ForbBAt(path, 0.70 + 0.30 * t) : Vec3.Lerp(spikeBase, spikeTop, t);
                double fAz = rng.Range(0, 2 * Math.PI);
                // Dense stacking: every station carries a hero flower; only occasional
                // short gaps keep the spire from reading as a solid bottle-brush.
                bool skipHero = rng.Chance(0.12);
                if (!skipHero)
                {
                    // Hero flower: short pedicel, 5 Sheet petals, darker centre disc.
                    var fTip = centre + new Vec3(Math.Cos(fAz), 0, Math.Sin(fAz)) * (h * 0.008) + Vec3.Up * (h * 0.005);
                    Primitives.Tube(part, new[] { centre, fTip }, new[] { 0.0005, 0.00035 }, 4,
                        (j, v) => (stemCol, 1.0, j, v, 0, 0));
                    double sizeScale = 1.15 - 0.35 * t; // flowers shrink toward the tip
                    ForbBDisc(part, fTip, 0.0060 * sizeScale, rings: 2, segments: 5, col: darkCol);
                    for (int petal = 0; petal < 5; petal++)
                    {
                        double paz = fAz * 0.3 + 2 * Math.PI * petal / 5 + rng.Range(-0.12, 0.12);
                        ForbBPetal(part, fTip, paz, baseAngle: 1.35, droop: 0.12,
                            length: 0.028 * sizeScale, halfWidth: 0.0095 * sizeScale, curl: 0.20,
                            baseCol: magentaCol, tipCol: magentaCol,
                            seed: Rng.Mix(seed, (ulong)(sp * 613 + f * 31 + petal * 5 + 41)));
                    }
                }
                // Single saturated-magenta filler disc between hero flowers so the spike
                // still reads as magenta where a hero flower was skipped, without solidifying.
                for (int d = 0; d < 1; d++)
                {
                    double dt = (f + (d + 1) / 2.0) / Math.Max(1, flowers);
                    var between = Vec3.Lerp(spikeBase, spikeTop, 0.04 + 0.92 * dt);
                    double dAz = rng.Range(0, 2 * Math.PI);
                    var off = between + new Vec3(Math.Cos(dAz), 0, Math.Sin(dAz)) * 0.005 + Vec3.Up * rng.Range(-0.003, 0.003);
                    ForbBDisc(part, off, 0.0050, rings: 2, segments: 5, col: magentaCol);
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
