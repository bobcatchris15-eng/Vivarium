using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Silverweed - the submerged meadow plant of flowing, oxygenated water.
///
/// Analogue: Elodea / Cabomba, the ribbon-leaved pondweed. A real rheophyte, rooted in the
/// bed of a running stream, throwing jointed stems up into the current and carrying whorls
/// of narrow leaves that stand out sideways from the stem to catch what light there is.
///
/// The anatomy is built in the order a pondweed actually grows:
///
///   * a CREEPING STOLON on the bed - a jointed axis that runs along the substrate, swelling
///     at every node, rooting at the nodes with a tuft of fine adventitious rootlets, and
///     throwing up erect leafy shoots as it goes. This is how a pondweed spreads: the shoot
///     is the stolon, and the leaves only ever sit on its upright branches;
///   * ERECT STEMS rising from the stolon - jointed, slightly sinuous, thinner at the top,
///     and carrying the fantasy layer: a narrow SILVER THREAD running up the stem. The thread
///     is a thin bright ridge lying along the stem's skin, grown out of it, not a wire stuck
///     onto it;
///   * a WHORL of 4-6 leaves at every node above the base. Each leaf is a narrow oblong
///     ribbon: widest about a third of the way along, tapering to an acute point, keeled
///     along the midrib and finely SERRATE at the margin. The serration is cut into the
///     geometry - real teeth, not a painted stripe;
///   * a COMB of six fine filaments standing off each leaf margin and waving along it. This
///     is the second half of the fantasy layer, and the reason the plant is called silverweed;
///   * a tight BUD CLUSTER of small folded leaves at the growing point.
///
/// Colour: deep translucent green, paler and greyer towards the tips, the silver thread a
/// pale warm silver, the filaments almost white. The whole plant lives below the waterline;
/// the stolon sits on the bed and the tallest shoot reaches a little over half the authored
/// height, so the plant never breaks the surface.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Pigments
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The silver thread. A pale warm silver, not white: a white ridge would read as plastic,
    /// and silver keeps the fantasy layer inside the plant's own palette of water and light.
    /// </summary>
    private static readonly double[] SilverweedThreadCol = { 0.88, 0.85, 0.76 };

    /// <summary>The brightest silver on the plant: the top of the thread.</summary>
    private static readonly double[] SilverweedThreadTipCol = { 0.95, 0.93, 0.86 };

    /// <summary>The marginal filaments. Almost white - they are the shimmer.</summary>
    private static readonly double[] SilverweedHairCol = { 0.94, 0.96, 0.93 };

    /// <summary>
    /// The adventitious rootlets: the pale, translucent white of a water plant's roots. A root
    /// that has never been in air has no reason to be brown.
    /// </summary>
    private static readonly double[] SilverweedRootletCol = { 0.80, 0.79, 0.70 };

    /// <summary>The midrib under the keel: a shade paler and yellower than the blade itself.</summary>
    private static readonly double[] SilverweedMidribCol = { 0.42, 0.56, 0.32 };

    // ---------------------------------------------------------------------------------------------
    // Anatomy constants, world metres
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Length of a stem leaf.
    ///
    /// A real pondweed leaf is a couple of centimetres on a shoot half a metre tall, which
    /// would leave the whorls as bare intervals with a fringe of small blades on them. The
    /// leaf here is deliberately generous for the shoot it sits on, so that two whorls meet
    /// and the stand reads as a meadow of foliage rather than a bundle of wires. The shape
    /// is still the narrow oblong ribbon the real plant has; only the scale is heroic.
    /// </summary>
    private const double SilverweedLeafLength = 0.055;

    /// <summary>Half width of a stem leaf, as a fraction of its length. A pondweed blade is a strap.</summary>
    private const double SilverweedLeafAspect = 0.115;

    /// <summary>Lengthwise rings of a leaf blade. Six teeth need three rings each to read as teeth.</summary>
    private const int SilverweedLeafRows = 18;

    /// <summary>Ring vertices of a leaf blade.</summary>
    private const int SilverweedLeafSides = 6;

    /// <summary>Teeth cut into the leaf margin.</summary>
    private const double SilverweedLeafTeeth = 6;

    /// <summary>How deep the teeth bite, as a fraction of the blade's half width.</summary>
    private const double SilverweedLeafSerration = 0.22;

    /// <summary>
    /// The width of a leaf blade along its length, as a fraction of its half width.
    ///
    /// A pondweed leaf is an oblong: it leaves the node as a narrow strap, opens out to its
    /// full width about a third of the way along, and then runs out to an acute point. The
    /// taper is slow at first and quick at the end, which is what makes the tip look drawn
    /// rather than clipped off.
    /// </summary>
    private static double SilverweedLeafWidth(double t)
    {
        if (t < 0.33)
        {
            double open = t / 0.33;
            return 0.24 + 0.76 * MathD.SmoothStep(0.0, 1.0, open);
        }
        double taper = (t - 0.33) / 0.67;
        return Math.Max(0.025, 1.0 - 0.975 * Math.Pow(taper, 1.5));
    }

    // ---------------------------------------------------------------------------------------------
    // The leaf and its comb
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One ribbon leaf, and the comb of silver filaments that grows out of its margin.
    ///
    /// The blade itself is the shared lofted builder: a superellipse section, so the blade can
    /// be a flat strap with a keel along the midrib and a margin that carries real teeth.
    /// </summary>
    private static void SilverweedLeaf(MeshData part, Rng rng, Vec3 node, Vec3 along, Vec3 dorsal,
        double length, double halfWidth, double[] baseCol, double[] tipCol, int hairs, double colourDrift)
    {
        int rows = SilverweedLeafRows, sides = SilverweedLeafSides;

        // The shared builder lays a blade out as a (rows+1) x (sides+1) grid, and the detail
        // budget may coarsen it. Predict the same budget here so the comb can be grown from
        // the blade's real margin instead of from a guess at where that margin ended up.
        int budgetRows = rows, budgetSides = sides;
        NewFloraBudget(part, ref budgetRows, ref budgetSides, minRows: 3, minSides: 5);

        int first = part.VertexCount;
        NewFloraBlade(part, node, along, dorsal, length, halfWidth,
            SilverweedLeafWidth,
            t => Primitives.Scale(Primitives.Mix(baseCol, tipCol, MathD.SmoothStep(0.04, 0.96, t)), 1.0 + colourDrift),
            rows: rows, sides: sides,
            flatness: 0.13,          // a thin strap, not a fleshy lobe
            keel: 0.50,              // the midrib stands proud along the dorsal midline
            cup: 0.05,               // the margins lift a hair, so the blade takes the light like a spoon
            // A pondweed leaf in a current arches as it leaves the node and rolls over at
            // the tip. The arch is what fills the space between two whorls.
            camber: rng.Range(0.12, 0.42),
            bow: rng.Range(0.04, 0.22),
            twist: rng.Range(-0.45, 0.45),
            tipCurl: rng.Range(0.04, 0.18),
            serration: SilverweedLeafSerration,   // the teeth are cut into the margin, not painted on it
            teeth: SilverweedLeafTeeth,
            midrib: SilverweedMidribCol,
            midribWidth: 0.78);

        // --- the comb -----------------------------------------------------------------------
        // The filaments stand off the blade's margin and wave along it. They are grown from
        // the blade's own ring vertices so they sit on the edge of the leaf rather than
        // somewhere near it.
        int stride = budgetSides + 1;

        // A point on the blade's ring, at a fractional vertex index. The ring closes on itself
        // (the last vertex repeats the first), so a fractional index is how a point that falls
        // between two vertices - the far margin, the dorsal ridge - is found.
        Vec3 RingPoint(int i, double fj)
        {
            int j0 = (int)Math.Floor(fj);
            int j1 = (j0 + 1) % (budgetSides + 1);
            double f = fj - j0;
            var a = part.Position(first + i * stride + j0);
            var b = part.Position(first + i * stride + j1);
            return Vec3.Lerp(a, b, f);
        }

        for (int e = 0; e < 2; e++)
        {
            // The near margin is the seam of the ring; the far margin is half a ring away.
            double marginF = e == 0 ? 0.0 : budgetSides * 0.5;
            for (int h = 0; h < hairs; h++)
            {
                double t = 0.13 + 0.78 * (h / (double)Math.Max(1, hairs - 1)) + rng.Range(-0.025, 0.025);
                int i = Math.Clamp((int)(t * budgetRows), 0, budgetRows - 1);

                var at = RingPoint(i, marginF);
                // Along the margin, towards the leaf's point.
                var fwd = RingPoint(Math.Min(budgetRows, i + 1), marginF) - RingPoint(Math.Max(0, i - 1), marginF);
                if (fwd.LengthSq < 1e-16) fwd = along;
                fwd = fwd.Normalized();
                // The section's centre and the ridge along the midrib, both taken off the same
                // ring, so the outward direction is the true lateral direction of the blade.
                var mid = (RingPoint(i, 0.0) + RingPoint(i, budgetSides * 0.5)) * 0.5;
                var outw = at - mid;
                if (outw.LengthSq < 1e-16) outw = dorsal;
                outw = outw.Normalized();
                var ridge = RingPoint(i, budgetSides * 0.25) - mid;
                if (ridge.LengthSq < 1e-16) ridge = dorsal;
                ridge = ridge.Normalized();

                // The filament stands off the edge, leans along the margin, and waves: the tip
                // trails further towards the leaf's point than the root does.
                double hlen = length * rng.Range(0.075, 0.165);
                var dir = (outw * 0.92 + fwd * 0.52).Normalized();
                var p0 = at;
                var p1 = at + dir * (hlen * 0.52) + ridge * (hlen * 0.10);
                var p2 = at + dir * hlen + fwd * (hlen * 0.38) + ridge * (hlen * rng.Range(-0.20, 0.28));
                SilverweedStrand(part, p0, p1, p2, length * rng.Range(0.0075, 0.0115), SilverweedHairCol);
            }
        }

        // The filaments belong to their leaf. The LOD tierer drops whole leaves, so the comb
        // has to live inside its leaf's span - otherwise a dropped leaf would leave a haze of
        // silver strands hanging in the water with nothing attached to them.
        if (part.LeafSpans.Count > 0 && part.LeafSpans[^1].FirstVertex == first)
            part.LeafSpans[^1] = (first, part.VertexCount - first);
    }

    /// <summary>
    /// One marginal filament: a three-sided tapered prism.
    ///
    /// The comb is the densest organ on the plant - well over a thousand hairs - and a hair at
    /// this scale is a third of a millimetre across. A six-sided tube for every one of them
    /// would cost more triangles than the entire rest of the plant, so the hairs get the
    /// cheapest closed form that still catches light on every side.
    /// </summary>
    private static void SilverweedStrand(MeshData part, Vec3 p0, Vec3 p1, Vec3 p2, double radius, double[] col)
    {
        var axis = p2 - p0;
        if (axis.LengthSq < 1e-14) return;
        axis = axis.Normalized();
        var side = axis.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = axis.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = side.Cross(axis).Normalized();

        var pts = new[] { p0, p1, p2 };
        // The last ring collapses to the point, so the strand ends in a tip and needs no cap.
        var radii = new[] { radius, radius * 0.62, 0.0 };
        int start = part.VertexCount;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                double th = 2 * Math.PI * j / 3;
                var radial = side * Math.Cos(th) + up * Math.Sin(th);
                part.AddVertex(pts[i] + radial * radii[i], radial, col, 1, (double)i * 0.5, (double)j / 3, 0, 0);
            }
        }
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                int a = start + i * 3 + j, b = start + i * 3 + (j + 1) % 3;
                int c = start + (i + 1) * 3 + j, d = start + (i + 1) * 3 + (j + 1) % 3;
                double th = 2 * Math.PI * (j + 0.5) / 3;
                var nrm = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                Primitives.TriangleFacing(part, a, c, b, nrm);
                Primitives.TriangleFacing(part, b, c, d, nrm);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The silver thread
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The silver thread: a narrow bright ridge lying along the stem.
    ///
    /// It is grown out of the stem's own skin - three vertices across, the outer pair flush
    /// with the surface and the middle one standing proud - and it wanders a little around
    /// the stem as it climbs, so it is never hidden on one side for the whole length.
    /// </summary>
    private static void SilverweedThread(MeshData part, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii)
    {
        int n = path.Count;
        if (n < 2) return;

        // The same parallel-transported frame the tube builder uses, so the thread keeps to
        // one side of the stem through the bends instead of jumping across it.
        var tangents = new Vec3[n];
        for (int i = 0; i < n; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == n - 1) d = path[n - 1] - path[n - 2];
            else d = path[i + 1] - path[i - 1];
            tangents[i] = d.LengthSq > 1e-14 ? d.Normalized() : Vec3.Up;
        }
        var side = tangents[0].Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        const double halfAngle = 0.27;                 // how wide the ridge reads on the stem's skin
        const double drift = 0.40 * Math.PI * 2;       // how far it wanders around the stem as it climbs

        int start = part.VertexCount;
        var radials = new Vec3[n];
        for (int i = 0; i < n; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-8)
                {
                    side = dir.Cross(Vec3.Up);
                    if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up = side.Cross(dir).Normalized();
            double th = drift * (double)i / (n - 1);
            var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
            var tangentSide = (side * -Math.Sin(th) + up * Math.Cos(th)).Normalized();
            radials[i] = radial;
            double r = radii[i];
            double f = (double)i / (n - 1);
            // The thread brightens as it climbs towards the light.
            var col = Primitives.Mix(SilverweedThreadCol, SilverweedThreadTipCol, f * f);
            part.AddVertex(path[i] + radial * (r * 1.05) - tangentSide * (r * Math.Sin(halfAngle)), radial, col, 1, f, 0.0, 0, 0);
            part.AddVertex(path[i] + radial * (r * 1.30), radial, col, 1, f, 0.5, 0, 0);
            part.AddVertex(path[i] + radial * (r * 1.05) + tangentSide * (r * Math.Sin(halfAngle)), radial, col, 1, f, 1.0, 0, 0);
        }
        for (int i = 0; i < n - 1; i++)
        {
            var nrm = (radials[i] + radials[i + 1]) * 0.5;
            if (nrm.LengthSq < 1e-12) nrm = radials[i];
            nrm = nrm.Normalized();
            for (int j = 0; j < 2; j++)
            {
                int a = start + i * 3 + j, b = a + 1, c = a + 3, d = a + 4;
                Primitives.TriangleFacing(part, a, c, b, nrm);
                Primitives.TriangleFacing(part, b, c, d, nrm);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The stolon
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The stolon: a jointed creeping axis running along the bed, with a swelling at every
    /// node and a tuft of fine adventitious rootlets hanging off it.
    ///
    /// The node points are handed back so the caller can decide which of them throw up an
    /// erect shoot.
    /// </summary>
    private static void SilverweedStolon(MeshData part, Rng rng, Vec3 start, double heading, double reach,
        int nodes, double[] col, double[] tipCol, List<Vec3> nodePoints)
    {
        int steps = nodes * 2 - 1;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];
        double ang = heading;
        var p = start;
        for (int i = 0; i <= steps; i++)
        {
            if (i > 0)
            {
                ang += rng.Range(-0.34, 0.34);
                p += new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * (reach / steps);
            }
            // The stolon lies on the bed. It lifts a hair at the crown and settles as it runs.
            double lift = 0.0008 + 0.0024 * Math.Pow(1.0 - (double)i / steps, 2.2) + 0.0004 * Math.Sin(i * 1.7);
            path[i] = new Vec3(p.X, lift, p.Z);
            // The node is visibly thicker than the internode beside it: a pondweed stolon is
            // a string of swollen joints, and it is at the joints that it roots and branches.
            bool node = i % 2 == 0;
            radii[i] = 0.00125 * (1.0 - 0.28 * (double)i / steps) * (node ? 1.55 : 0.76);
        }
        Primitives.Tube(part, path, radii, 6,
            (i, v) => (Primitives.Mix(col, tipCol, (double)i / steps), 1, (double)i / steps, v, 0, 0));

        for (int i = 0; i <= steps; i += 2)
        {
            nodePoints.Add(path[i]);
            // The rootlets: fine, white, and diving into the substrate. They bow sideways as
            // they go, the way a root that has met the bed does.
            int roots = 1 + rng.NextInt(2);
            for (int k = 0; k < roots; k++)
            {
                double a = ang + rng.Range(-2.6, 2.6);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var perp = new Vec3(-dir.Z, 0, dir.X);
                var from = path[i] - Vec3.Up * 0.0005;
                var to = from + dir * rng.Range(0.006, 0.013) - Vec3.Up * rng.Range(0.004, 0.010);
                NewFloraStalk(part, from, to, perp, 0.10, 0.00024, 0.00007,
                    SilverweedRootletCol, SilverweedRootletCol, segments: 2, sides: 4);
            }
            // One finer thread off the node itself. A root tuft is never a clean brush: the
            // coarse roots carry a haze of finer ones between them.
            {
                double a = ang + rng.Range(-3.0, 3.0);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var from = path[i] - Vec3.Up * 0.0008;
                var to = from + dir * rng.Range(0.003, 0.007) - Vec3.Up * rng.Range(0.006, 0.011);
                NewFloraFilament(part, from, to, 0.00008, SilverweedRootletCol);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The erect stem
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Where along a shoot's height a path point sits.
    ///
    /// The shoot is built as foot, then a mid-internode point and a node for every whorl, and
    /// finally the tip. The first node sits low, just above the bed, and the last one just
    /// below the bud: a pondweed carries leaves for most of its length, and a bare stem
    /// under a canopy is a different plant altogether.
    /// </summary>
    private static double SilverweedNodeFraction(int i, int whorls, int points)
    {
        if (i <= 0) return 0.0;                        // the foot of the shoot
        if (i >= points - 1) return 1.0;               // the tip, under the bud
        int span = Math.Max(1, whorls - 1);
        if (i == 1) return 0.045;                      // the middle of the first internode
        if (i % 2 == 0) return 0.09 + 0.80 * (i / 2 - 1) / span;
        return 0.09 + 0.80 * (i / 2 - 0.5) / span;
    }

    /// <summary>
    /// One erect shoot: jointed, slightly sinuous, carrying the whorls of ribbon leaves, the
    /// silver thread up its side, and a bud cluster at the growing point.
    /// </summary>
    private static void SilverweedStem(MeshData part, Rng rng, ulong seed, Vec3 basePoint, double stemHeight,
        int whorls, double[] stemCol, double[] stemTipCol, double[] leafBase, double[] leafTip,
        double[] budCol, double leafScale, int stemIndex)
    {
        // The shoot leans away from the stolon and waves as it climbs. A stem in running
        // water never quite stands straight, and the wave is what keeps the whorls from
        // stacking up in vertical planes.
        double phase = rng.Range(0, Math.Tau);
        double outAng = rng.Range(0, Math.Tau);
        var outward = new Vec3(Math.Cos(outAng), 0, Math.Sin(outAng));
        var perp = new Vec3(-outward.Z, 0, outward.X);
        double lean = rng.Range(0.10, 0.21);

        int points = 2 * whorls + 2;
        var path = new Vec3[points];
        var radii = new double[points];
        for (int i = 0; i < points; i++)
        {
            double f = SilverweedNodeFraction(i, whorls, points);
            var offset = (outward * (f * 0.60) + perp * (Math.Sin(phase + f * 2.6) * 0.38)) * (stemHeight * lean);
            path[i] = basePoint + Vec3.Up * (stemHeight * f) + offset;
            bool node = i % 2 == 0 && i > 0 && i < points - 1;
            radii[i] = 0.00145 * (1.0 - 0.52 * f) * (node ? 1.40 : 0.82);
        }
        Primitives.Tube(part, path, radii, 6,
            (i, v) => (Primitives.Mix(stemCol, stemTipCol, (double)i / (points - 1)), 1, (double)i / (points - 1), v, 0, 0));

        for (int k = 0; k < whorls; k++)
        {
            var node = path[2 * k + 2];
            double nodeRadius = radii[2 * k + 2];
            // Each whorl is turned against the last, so the plant does not stack its leaves
            // in one plane - the trick a real whorled leaf uses to fill the water around it.
            double whorlPhase = phase + k * 1.73;
            // The plane of the whorl varies from node to node. A whorl that lies flat shows
            // its blades edge-on to a low camera; one that is tipped up shows its face. Both
            // are needed, or the plant reads as a bundle of wires from the side.
            double whorlTilt = rng.Range(0.06, 0.75);
            int count = NewFloraCount(part, 4 + rng.NextInt(2));    // 4-5 leaves in a whorl
            int hairs = NewFloraCount(part, 6);                     // 6 filaments on each margin
            double up = (double)k / Math.Max(1, whorls - 1);
            // The lowest leaves are the oldest and the longest; the top of the shoot is new
            // growth and carries smaller blades.
            double lenScale = (1.0 - 0.20 * up + 0.10 * Math.Sin(Math.PI * up)) * leafScale;

            for (int j = 0; j < count; j++)
            {
                double a = whorlPhase + Math.Tau * j / count + rng.Range(-0.14, 0.14);
                var outw = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                // The leaf stands out sideways from the stem, tilted a little up so that its
                // face takes the light from above - the whole reason a pondweed whorl works.
                double tilt = Math.Clamp(whorlTilt + rng.Range(-0.26, 0.26), 0.04, 1.05);
                var along = (outw * Math.Cos(tilt) + Vec3.Up * Math.Sin(tilt)).Normalized();
                // The blade's face is rolled freely either way, so the whorl is a set of
                // plates catching the light at different angles rather than a set of
                // identical leaves.
                double roll = rng.Range(-0.85, 0.85);
                var dorsal = Vec3.Up - along * Vec3.Up.Dot(along);
                if (dorsal.LengthSq < 1e-10) dorsal = outw.Cross(along);
                dorsal = dorsal.Normalized();
                dorsal = dorsal * Math.Cos(roll) + along.Cross(dorsal) * Math.Sin(roll);

                double len = SilverweedLeafLength * lenScale * rng.Range(0.80, 1.18);
                double halfW = len * SilverweedLeafAspect * rng.Range(0.85, 1.15);
                // A stable salt per leaf, so the value drift of a blade is tied to where that
                // blade sits in the plant rather than to the order the rng happened to be
                // consumed in.
                ulong salt = Rng.Mix(seed, (ulong)(stemIndex * 131 + k * 17 + j * 5 + 3));
                double drift = ((salt >> 11) * (1.0 / 9007199254740992.0) - 0.5) * 0.14;
                // The blade starts inside the node, so it appears to clasp the stem.
                var root = node - along * (nodeRadius * 0.55);
                SilverweedLeaf(part, rng, root, along, dorsal, len, halfW, leafBase, leafTip, hairs, drift);
            }
        }

        // The silver thread, laid up the length of the shoot.
        SilverweedThread(part, path, radii);

        // The bud cluster: a tight spiral of small folded leaves round the growing point.
        // This is the part of a pondweed that is always new, so it is paler than everything
        // below it and it closes over the tip.
        var tip = path[points - 1];
        var axis = (tip - path[points - 2]).Normalized();
        NewFloraScaleColumn(part, tip, axis, 0.011, 0.0022, 2, 3, 0.0055, 0.0011,
            (wt, t) => Primitives.Mix(budCol, SilverweedMidribCol, t * 0.35), rng,
            droop: 1.0, spiralTurns: 1.25);
    }

    // ---------------------------------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Silverweed: a creeping stolon on the bed throwing up jointed shoots, each carrying
    /// whorls of ribbon leaves fringed with silver.
    /// </summary>
    private static void Silverweed(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        double foot = radius > 0.01 ? radius : 0.34;      // the plant's spread on the bed, metres
        double stalk = height > 0.01 ? height : 0.46;     // the authored height of the plant

        // --- pigments -------------------------------------------------------------------------
        // A pondweed blade is deep translucent green. The darkness is the water above it, not
        // pigment, so the base colour is only a little darker than the species colour.
        var leafBase = Primitives.Mix(c1, c2, 0.10);
        // The tips are paler and greyer: new pondweed growth bleaches out towards the light.
        var leafTip = Primitives.Mix(Primitives.Mix(c1, c2, 0.55), new[] { 0.60, 0.64, 0.60 }, 0.55);
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.26), 0.80);
        var stemTipCol = Primitives.Mix(c1, c2, 0.50);
        var budCol = Primitives.Mix(leafTip, new[] { 0.66, 0.70, 0.62 }, 0.40);

        // --- the crown ------------------------------------------------------------------------
        // The knot the stolons spring from. Small, and mostly buried in the bed.
        Primitives.Ellipsoid(part, new Vec3(0, 0.0012, 0), new Vec3(0.0034, 0.0022, 0.0034), 3, 6,
            (u, v) => (Primitives.Scale(stemCol, 0.85), 1, u, v, 0, 0));

        // --- the stolons ----------------------------------------------------------------------
        // Three creeping axes radiating from the crown, so the plant is a clump that has spread
        // across the bed rather than one runner lying off to one side. This is how a pondweed
        // actually spreads: the shoot is the stolon, and the leaves only ever sit on its
        // upright branches.
        double baseHeading = rng.Range(0, Math.Tau);
        var arms = new List<List<Vec3>>();
        for (int a = 0; a < 3; a++)
        {
            double h = baseHeading + Math.Tau * a / 3 + rng.Range(-0.30, 0.30);
            double reach = foot * rng.Range(0.62, 0.78);
            var nodes = new List<Vec3>();
            SilverweedStolon(part, rng, new Vec3(0, 0.0010, 0), h, reach, 5, stemCol, stemTipCol, nodes);
            arms.Add(nodes);
        }

        // --- the erect shoots -----------------------------------------------------------------
        // Six shoots: two off every runner, one close to the crown and one further out where
        // the runner is still growing. The plant is a meadow - a stand of shoots of different
        // ages standing at different distances from the crown - and the whole clump reads as
        // centred on its own origin. The tallest shoot reaches just over half the authored
        // height: the plant lives entirely below the waterline and never breaks the surface.
        var sites = new List<(Vec3 Node, double Height, double Scale)>();
        void AddShoot(List<Vec3> arm, int node)
        {
            if (arm.Count <= node) return;
            // The further out the shoot stands, the younger it is, and the smaller it stays.
            double outward = node / 4.0;
            sites.Add((arm[node], stalk * (0.52 - 0.18 * outward), 1.00 - 0.20 * outward));
        }
        for (int a = 0; a < arms.Count; a++) AddShoot(arms[a], 1);
        for (int a = 0; a < arms.Count; a++) AddShoot(arms[a], 3);
        if (sites.Count == 0) sites.Add((new Vec3(0, 0.001, 0), stalk * 0.50, 1.00));

        // The shoots carry fewer whorls the shorter they are, so a young shoot is a short
        // spike of leaves rather than a stunted copy of an old one. The first three shoots
        // are the tall inner ones; the last three stand further out on the runners.
        int[] whorls = { 5, 4, 4, 4, 3, 3 };
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            int w = Math.Min(whorls.Length - 1, i);
            SilverweedStem(part, rng, seed, site.Node, site.Height, whorls[w],
                stemCol, stemTipCol, leafBase, leafTip, budCol, site.Scale, i);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
