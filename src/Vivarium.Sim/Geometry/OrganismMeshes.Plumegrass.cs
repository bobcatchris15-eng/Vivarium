using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Pampas grass (Cortaderia) groundcover for the 'plumegrass' species.
///
/// The plant is authored in real-world metres and converted into the engine's mesh frame by
/// <see cref="AppendPlumeWorld"/>, so what is built here is what the player sees.
///
/// Anatomy follows a real pampas tussock rather than a generic grass:
///
///   * a dense crown of long arching leaves, each one strongly KEEL - a V-shaped
///     cross-section folded along a pale midrib, the way a grass blade folds;
///   * sheathing leaf bases that overlap into a crown, with a few dead straw-coloured
///     old leaves at the bottom of the tussock;
///   * tall flowering culms that tower well above the foliage, lightly jointed at the
///     nodes and sheathed where they leave the crown;
///   * on each culm, a LADDER of panicles set at intervals - the 'steps' the species is
///     named for. Each panicle is built the way a real one is: a central rachis carrying
///     whorls of arching branchlets, each branchlet bearing small spikelets and a spray
///     of long, silky stigmas, the branchlets longest low down and shortest at the tip.
///
/// The fantastical element is that ladder. The real pampas panicle is silvery straw; here
/// the steps climb a spectral scale instead, from a deep indigo tuft low on the culm through
/// cyan and spring green and gold to a rose crown, so the stalk reads as a chromatic
/// staircase. The filaments carry their own pigment and pick up a grazing-angle prismatic
/// shift, so each step glints differently as the plant turns.
///
/// Reference renders: scripts/visuals/plumegrass/pampas_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The spectral ladder the plume steps climb, low step first.</summary>
    private static readonly double[][] PlumePrism =
    {
        new[] { 0.13, 0.08, 0.36 },   // deep indigo
        new[] { 0.05, 0.32, 0.66 },   // cyan
        new[] { 0.18, 0.56, 0.34 },   // spring
        new[] { 0.68, 0.50, 0.08 },   // gold
        new[] { 0.72, 0.20, 0.22 },   // rose
    };

    /// <summary>The grass body: a glaucous green that the pale midrib sits in.</summary>
    private static readonly double[] PlumeBladeBase = { 0.125, 0.190, 0.098 };
    private static readonly double[] PlumeBladeTip = { 0.205, 0.240, 0.128 };
    private static readonly double[] PlumeMidrib = { 0.62, 0.64, 0.52 };
    private static readonly double[] PlumeDead = { 0.48, 0.40, 0.24 };

    /// <summary>Where the plume steps sit up a culm, as a fraction of its length.</summary>
    private static readonly double[] PlumeStepFractions = { 0.36, 0.535, 0.70, 0.865, 1.0 };

    /// <summary>
    /// A tapered filament: `sides` around the axis, `rings` along it, optionally bent
    /// through a mid control point. Written here rather than using <see cref="Primitives.Tube"/>
    /// because a grass stigma is a third of a millimetre across and must not pay for six sides.
    /// </summary>
    private static void PlumeFilament(MeshData m, Vec3 p0, Vec3 p1, double r0, double r1,
        int sides, int rings, Vec3? bend, double[] colour, Func<int, double, (double[] Col, double A, double U, double V, double U2, double V2)>? attr,
        double flatten = 1.0)
    {
        if (sides < 3) sides = 3;
        if (rings < 1) rings = 1;
        var path = new Vec3[rings + 1];
        var radii = new double[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            Vec3 p;
            if (bend is { } b)
            {
                var a = Vec3.Lerp(p0, b, t);
                var c = Vec3.Lerp(b, p1, t);
                p = Vec3.Lerp(a, c, t);
            }
            else p = Vec3.Lerp(p0, p1, t);
            path[i] = p;
            radii[i] = r0 + (r1 - r0) * t;
        }
        // A stable frame: the filaments hang off a mostly-vertical rachis, so a world-down
        // reference keeps them from spinning as they arc.
        int start = m.VertexCount;
        for (int i = 0; i <= rings; i++)
        {
            Vec3 tangent;
            if (i == 0) tangent = path[1] - path[0];
            else if (i == rings) tangent = path[rings] - path[rings - 1];
            else tangent = path[i + 1] - path[i - 1];
            if (tangent.LengthSq < 1e-14) tangent = Vec3.Up;
            tangent = tangent.Normalized();
            var side = tangent.Cross(Vec3.Up);
            if (side.LengthSq < 1e-10) side = tangent.Cross(new Vec3(1, 0, 0));
            side = side.Normalized();
            var up2 = side.Cross(tangent).Normalized();
            for (int s = 0; s <= sides; s++)
            {
                double th = 2 * Math.PI * s / sides;
                // A flattened ring gives a feather-like vane instead of a round wire.
                var radial = side * (Math.Cos(th) * radii[i]) + up2 * (Math.Sin(th) * radii[i] * flatten);
                var at = path[i] + radial;
                var a = attr is null ? (colour, 1, (double)i / rings, (double)s / sides, 0, 0) : attr(i, (double)s / sides);
                m.AddVertex(at, radial, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int i = 0; i < rings; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s, b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    /// <summary>
    /// One grass blade: a keeled, arching leaf with a pale midrib folded along the underside.
    ///
    /// The arc is integrated along the length rather than faked with a curve, so the blade
    /// leaves the sheath almost vertically, sweeps out and finishes drooping, the way a
    /// real tussock leaf does.
    /// </summary>
    private static void PlumeBlade(MeshData m, Vec3 root, double azimuth, double length,
        double halfWidth, double theta0, double theta1, int rows, double[] bladeBase, double[] bladeTip,
        double[] midrib, bool dead)
    {
        var radial = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var across = new Vec3(-Math.Sin(azimuth), 0, Math.Cos(azimuth));

        // Integrate the centreline arc.
        var pts = new Vec3[rows + 1];
        pts[0] = root;
        double dTheta = (theta1 - theta0) / rows;
        double dl = length / rows;
        for (int i = 0; i < rows; i++)
        {
            double a = theta0 + dTheta * (i + 0.5);
            var d = radial * Math.Sin(a) + Vec3.Up * Math.Cos(a);
            pts[i + 1] = pts[i] + d * dl;
        }

        double Width(double t)
        {
            if (t < 0.10) return halfWidth * (0.58 + 0.42 * (t / 0.10));
            double u = (t - 0.10) / 0.90;
            return halfWidth * Math.Max(0.04, 1.0 - Math.Pow(u, 1.5));
        }

        // Five points across the fold: margin, mid, keel, mid, margin. The keel sits below
        // the mid-line, so the blade is a V with the midrib at the bottom of the groove.
        double[] sx = { -1.0, -0.46, 0.0, 0.46, 1.0 };
        double[] sy = { 0.34, 0.06, -1.0, 0.06, 0.34 };

        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        var ids = new int[rows + 1, 5];
        var normals = new Vec3[rows + 1, 5];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var centre = pts[i];
            double w = Width(t);
            var nxt = pts[Math.Min(rows, i + 1)];
            var prv = pts[Math.Max(0, i - 1)];
            var tangent = nxt - prv;
            if (tangent.LengthSq < 1e-16) tangent = radial;
            tangent = tangent.Normalized();
            var faceUp = tangent.Cross(across);
            if (faceUp.LengthSq < 1e-12) faceUp = Vec3.Up;
            faceUp = faceUp.Normalized();

            for (int k = 0; k < 5; k++)
            {
                var at = centre + across * (sx[k] * w) + faceUp * (sy[k] * w * 0.46);
                // The keel column carries the pale midrib; the flanks are the blade green.
                // Only the keel itself is pale - a grass blade's midrib is a line, not a wash.
                double keel = k == 2 ? 1.0 : 0.0;
                var col = Primitives.Mix(Primitives.Mix(bladeBase, bladeTip, t), midrib, keel * (dead ? 0.4 : 0.85));
                ids[i, k] = m.AddVertex(at, faceUp, col, 1, t, 0.5 + sx[k] * 0.5, 0, 1);
            }
        }

        // Smooth normals over the fold, so the blade shades as a continuous keel.
        for (int i = 0; i < rows; i++)
            for (int k = 0; k < 4; k++)
            {
                var p0 = m.Position(ids[i, k]);
                var p1 = m.Position(ids[i, k + 1]);
                var p2 = m.Position(ids[i + 1, k]);
                var p3 = m.Position(ids[i + 1, k + 1]);
                var n = (p1 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) n = (p3 - p0).Cross(p2 - p0);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                normals[i, k] += n;
                normals[i, k + 1] += n;
                normals[i + 1, k] += n;
                normals[i + 1, k + 1] += n;
            }
        for (int i = 0; i <= rows; i++)
            for (int k = 0; k < 5; k++)
            {
                var n = normals[i, k];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = ids[i, k];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        for (int i = 0; i < rows; i++)
            for (int k = 0; k < 4; k++)
            {
                var p0 = m.Position(ids[i, k]);
                var p1 = m.Position(ids[i, k + 1]);
                var p2 = m.Position(ids[i + 1, k]);
                var normal = (p1 - p0).Cross(p2 - p0);
                if (normal.LengthSq < 1e-18) continue;
                normal = normal.Normalized();
                Primitives.TriangleFacing(m, ids[i, k], ids[i + 1, k], ids[i, k + 1], normal);
                Primitives.TriangleFacing(m, ids[i, k + 1], ids[i + 1, k], ids[i + 1, k + 1], normal);
            }

        m.RecordLeaf(leafVertex, leafIndex, root, length);
    }

    /// <summary>
    /// One panicle: a rachis carrying whorls of arching branchlets, each branchlet bearing
    /// spikelets and a spray of long silky stigmas.
    ///
    /// The branchlets are longest low on the panicle and shortest at the tip, which is what
    /// gives a real plume its soft, tapering, feather-duster silhouette.
    /// </summary>
    private static void PlumePanicle(MeshData m, Vec3 basePoint, Vec3 up, double length,
        double halfWidth, double[] prism, int whorls, int branchletsPerWhorl, int stigmasPerBranchlet,
        Rng rng, bool terminal)
    {
        // The rachis: the culm carries on into the plume.
        PlumeFilament(m, basePoint, basePoint + up * length, 0.0014, 0.0004, 4, 3, null,
            prism, (i, v) => (Primitives.Mix(prism, new[] { 0.92, 0.90, 0.84 }, 0.35 * (1.0 - v)), 1, i / 3.0, v, 1.28, 1));

        // The frame the whorls radiate in.
        var side = up.Cross(new Vec3(1, 0, 0));
        if (side.LengthSq < 1e-10) side = up.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var forward = side.Cross(up).Normalized();

        for (int w = 0; w < whorls; w++)
        {
            double wt = (w + 0.5) / whorls;
            // branchlets taper towards the tip of the panicle
            double reach = halfWidth * (1.0 - 0.60 * wt) * rng.Range(0.80, 1.20);
            var wBase = basePoint + up * (length * wt);
            for (int b = 0; b < branchletsPerWhorl; b++)
            {
                double ang = 2 * Math.PI * b / branchletsPerWhorl + w * 0.47 + rng.Range(-0.16, 0.16);
                var outward = side * Math.Cos(ang) + forward * Math.Sin(ang);
                // A pampas branchlet arcs up and out and then droops, so the plume is a soft
                // feather-duster rather than a bottlebrush.
                var tip = wBase + outward * reach + up * (length * 0.05 * rng.Range(0.2, 1.1));
                var mid = wBase + outward * (reach * 0.52) + up * (length * 0.09);
                // The branchlet: a flat, feather-like vane that tapers to a point. A grass
                // panicle branchlet is a vane, not a wire, and the vane is what gives the
                // plume its mass.
                PlumeFilament(m, wBase, tip, 0.0022, 0.00025, 3, 2, mid,
                    prism, (i, v) => (Primitives.Mix(prism, new[] { 0.86, 0.82, 0.76 }, 0.12), 1, i / 2.0, v, 1.28, 1),
                    flatten: 0.15);

                // Spikelets: the small dark seed clusters, on every third branchlet.
                if (b % 3 == 0)
                {
                    var seed = Vec3.Lerp(wBase, tip, 0.46);
                    PlumeSpikelet(m, seed, 0.0016, rng);
                }

                // The long silky stigmas that give the plume its haze.
                for (int s = 0; s < stigmasPerBranchlet; s++)
                {
                    double st = 0.42 + 0.52 * (s / (double)Math.Max(1, stigmasPerBranchlet - 1));
                    var at = Vec3.Lerp(wBase, tip, st);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        double a2 = ang + sign * rng.Range(0.45, 1.05);
                        var drift = side * Math.Cos(a2) + forward * Math.Sin(a2);
                        // The stigmas are long: they are what turns a panicle into a plume.
                        var end = at + drift * rng.Range(0.016, 0.036) + up * rng.Range(0.002, 0.016);
                        // A flat tapering ribbon reads as a hair from any distance and costs
                        // a fraction of what a solid filament would.
                        PlumeFilament(m, at, end, 0.00022, 0.00004, 2, 1, null,
                            prism, (i, v) => (Primitives.Mix(prism, new[] { 0.96, 0.95, 0.92 }, 0.08), 1, i, v, 1.28, 1));
                    }
                }
            }
        }
    }

    /// <summary>A single spikelet: a tiny faceted seed cluster hanging off a branchlet.</summary>
    private static void PlumeSpikelet(MeshData m, Vec3 centre, double radius, Rng rng)
    {
        // An octahedron: six vertices, eight faces, enough for a seed cluster this small.
        var top = centre + Vec3.Up * (radius * 1.5);
        var bottom = centre - Vec3.Up * (radius * 1.1);
        var ring = new Vec3[4];
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI * 0.5 + rng.Range(-0.2, 0.2);
            ring[i] = centre + new Vec3(Math.Cos(a) * radius, rng.Range(-0.1, 0.2) * radius, Math.Sin(a) * radius);
        }
        var col = new[] { 0.40, 0.24, 0.26 };
        // The nodule organ marker keeps the spikelet's own dark pigment and gives it a
        // tighter cut than the pale plume filaments around it.
        int ti = m.AddVertex(top, Vec3.Up, col, 1, 0, 0.5, 1.35, 1);
        int bi = m.AddVertex(bottom, -Vec3.Up, col, 1, 0, 0.5, 1.35, 1);
        var ids = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var outward = (ring[i] - centre).Normalized();
            ids[i] = m.AddVertex(ring[i], outward, col, 1, 0, 0.5, 1.35, 1);
        }
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Primitives.TriangleFacing(m, ti, ids[i], ids[j], (ring[i] - centre).Cross(ring[j] - centre).Normalized());
            Primitives.TriangleFacing(m, bi, ids[j], ids[i], (ring[i] - centre).Cross(ring[j] - centre).Normalized());
        }
    }

    /// <summary>
    /// The plumegrass plant: a tussock of arching leaves and a few flowering culms, each
    /// carrying a ladder of prismatic plume steps.
    /// </summary>
    private static void Plumegrass(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2,
        double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel };
        var bladeBase = Primitives.Mix(PlumeBladeBase, c1, 0.30);
        var bladeTip = Primitives.Mix(PlumeBladeTip, c2, 0.22);

        int rows = 14;
        int culms = 3;
        int whorls = 6;
        int branchlets = 14;
        int stigmas = 1;
        if (m.Lod is { } lod)
        {
            rows = lod.Segments(rows, 5);
            whorls = lod.Segments(whorls, 3);
            branchlets = Math.Max(4, (int)Math.Round(branchlets * lod.Radial));
            stigmas = Math.Max(2, (int)Math.Round(stigmas * lod.Radial));
        }
        if (m.FloraDetailLevel is 1) { rows = Math.Max(6, rows - 4); whorls = Math.Max(3, whorls - 1); }
        else if (m.FloraDetailLevel is 2) { rows = Math.Max(4, rows - 7); whorls = Math.Max(2, whorls - 2); branchlets = Math.Max(4, branchlets - 3); }

        double scale = juvenile ? 0.66 : 1.0;

        // --- 1. the tussock: long arching leaves off a crown of sheathing bases.
        // The species is a narrow, tall grass, so the leaves are kept inside its footprint:
        // a tall fountain would read as a different plant and would overrun its neighbours.
        int leaves = 46;
        for (int i = 0; i < leaves; i++)
        {
            double az = 2 * Math.PI * i / leaves + rng.Range(-0.12, 0.12);
            double len = rng.Range(0.28, 0.42) * scale;
            double halfW = rng.Range(0.0050, 0.0082);
            // The inner leaves stand up; the outer ones lie further over. The blades rise
            // steeply out of the sheath, so a long leaf still stays inside the footprint.
            double theta0 = rng.Range(0.04, 0.12);
            double theta1 = rng.Range(0.85, 1.35);
            var root = new Vec3(rng.Range(-0.020, 0.020), 0.004, rng.Range(-0.020, 0.020));
            PlumeBlade(part, root, az, len, halfW, theta0, theta1, rows, bladeBase, bladeTip,
                PlumeMidrib, dead: false);
        }

        // --- 2. the dead old leaves at the bottom of the tussock
        int dead = 7;
        for (int i = 0; i < dead; i++)
        {
            double az = 2 * Math.PI * i / dead + rng.Range(-0.3, 0.3);
            double len = rng.Range(0.16, 0.28) * scale;
            var root = new Vec3(Math.Cos(az) * 0.016, 0.002, Math.Sin(az) * 0.016);
            PlumeBlade(part, root, az, len, rng.Range(0.0036, 0.0056), rng.Range(0.25, 0.45),
                rng.Range(1.90, 2.40), Math.Max(5, rows - 5), PlumeDead, PlumeDead, PlumeDead, dead: true);
        }

        // --- 3. the flowering culms and their ladder of plume steps
        int culmCount = juvenile ? 2 : culms;
        for (int c = 0; c < culmCount; c++)
        {
            double az = 2 * Math.PI * c / culmCount + rng.Range(-0.34, 0.34);
            double culmHeight = height * rng.Range(0.74, 0.92) * scale;
            var basePoint = new Vec3(Math.Cos(az) * rng.Range(0.008, 0.030), 0.0, Math.Sin(az) * rng.Range(0.008, 0.030));
            // The culm leans as it rises, the way a flowering culm does.
            double lean = rng.Range(0.01, 0.07);
            var top = basePoint + new Vec3(Math.Cos(az) * culmHeight * lean, culmHeight, Math.Sin(az) * culmHeight * lean);

            // The culm itself, jointed: a slight swell at each node.
            const int culmRings = 14;
            var cpath = new Vec3[culmRings + 1];
            var cradii = new double[culmRings + 1];
            for (int i = 0; i <= culmRings; i++)
            {
                double t = i / (double)culmRings;
                cpath[i] = Vec3.Lerp(basePoint, top, t) + new Vec3(0, Math.Sin(t * Math.PI * 1.4) * culmHeight * 0.012, 0);
                bool node = i > 2 && i < culmRings - 1 && i % 4 == 1;
                cradii[i] = 0.0040 * (1.0 - 0.68 * t) * (node ? 1.28 : 1.0);
            }
            Primitives.Tube(part, cpath, cradii, 7,
                (i, v) => (Primitives.Mix(bladeTip, PlumeDead, 0.18), 1, (double)i / culmRings, v, 0, 0));

            // The sheath where the culm leaves the tussock, and the ligule at its lip.
            double sheathLen = culmHeight * rng.Range(0.10, 0.18);
            var sheathPath = new Vec3[4];
            var sheathRadii = new double[4];
            for (int i = 0; i <= 3; i++)
            {
                double t = i / 3.0;
                sheathPath[i] = Vec3.Lerp(basePoint, cpath[3], t);
                sheathRadii[i] = 0.0044 * (1.0 - 0.42 * t);
            }
            Primitives.Tube(part, sheathPath, sheathRadii, 8,
                (i, v) => (Primitives.Mix(bladeTip, PlumeMidrib, 0.30), 1, i / 3.0, v, 0, 0));

            // The ladder. Each step is a compact plume, bigger low down, and the colour
            // climbs the spectrum as the steps climb the culm.
            for (int s = 0; s < PlumeStepFractions.Length; s++)
            {
                double frac = PlumeStepFractions[s];
                if (juvenile && s > 1) break; // a young culm has only opened its first steps
                bool terminal = s == PlumeStepFractions.Length - 1;
                var at = Vec3.Lerp(basePoint, top, frac);
                // The steps shrink as they climb, so the ladder tapers.
                double k = 1.0 - 0.48 * frac;
                double pLen = rng.Range(0.130, 0.185) * scale * k * (terminal ? 1.18 : 1.0);
                double pHalf = rng.Range(0.034, 0.050) * scale * k * (terminal ? 1.22 : 1.0);
                // The colour of this rung of the ladder.
                double t = (double)s / (PlumeStepFractions.Length - 1);
                int idx = Math.Min(PlumePrism.Length - 1, (int)(t * (PlumePrism.Length - 1) + 0.5));
                var prism = PlumePrism[idx];
                // A little brightness variation so no two plumes are identical.
                prism = Primitives.Scale(prism, rng.Range(0.88, 1.12));
                // Each step's plume leaves the culm at an angle and to one side, so the
                // ladder reads as a run of separate treads rather than one long column.
                double sideSign = (s % 2 == 0) ? 1.0 : -1.0;
                double az2 = az + Math.PI * 0.5 * sideSign + rng.Range(-0.20, 0.20);
                double tilt = rng.Range(0.40, 0.80);
                var plumeAxis = (Vec3.Up * Math.Cos(tilt)
                    + new Vec3(Math.Cos(az2), 0, Math.Sin(az2)) * Math.Sin(tilt)).Normalized();
                PlumePanicle(part, at, plumeAxis, pLen, pHalf, prism, whorls, branchlets, stigmas, rng, terminal);
            }
        }

        AppendPlumeWorld(m, part, radius, height);
    }

    /// <summary>
    /// Converts a part authored in world metres into the engine's mesh frame. The renderer
    /// scales instances by (radius, height, radius), so this inverse scale makes the plant
    /// appear at the size and proportion it was authored at. Normals need the inverse
    /// transpose of that scale.
    /// </summary>
    private static void AppendPlumeWorld(MeshData target, MeshData part, double radius, double height)
    {
        double sx = 1.0 / Math.Max(0.01, radius);
        double sy = 1.0 / Math.Max(0.01, height);
        int baseVertex = target.VertexCount;
        int structuralBefore = target.Structural.Count;
        target.Append(part, p => new Vec3(p.X * sx, p.Y * sy, p.Z * sx));

        for (int v = baseVertex; v < target.VertexCount; v++)
        {
            var n = target.NormalAt(v);
            var scaled = new Vec3(n.X * radius, n.Y * height, n.Z * radius);
            double len = scaled.Length;
            if (len > 1e-9)
            {
                scaled = scaled / len;
                target.Normals[v * 3] = (float)scaled.X;
                target.Normals[v * 3 + 1] = (float)scaled.Y;
                target.Normals[v * 3 + 2] = (float)scaled.Z;
            }
        }

        // Tube radii are recorded in world metres; restate them in mesh units so the LOD tiers
        // keep classifying culms as structure rather than as foliage.
        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }
}
