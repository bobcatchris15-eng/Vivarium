using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Coalspike - the emergent cattail of a saturated margin.
///
/// Analogue: Typha, the bulrush. A cattail lives in the last few centimetres of water at the edge of a
/// ditch, so it is built like a fence post driven into the mud: a dense tuft of keeled strap leaves all
/// reaching for the same light, and above them two or three bare culms carrying the famous cigar.
///
/// ANATOMY, in the order a botanist would take it apart:
///
///   * the CROWN. Typha grows from a creeping rhizome, but what you see is the tuft of old, split,
///     papery leaf bases the rhizome leaves behind at the waterline - a tight boss of old collars.
///   * the STAND of 6-9 erect leaves. A Typha leaf is a strap, not a grass blade: flat, 10-18 mm wide,
///     keeled along the midrib with the crease on the UNDERSIDE, so the upper face is a shallow trough
///     and the rain runs down it. It runs out to a long fine point and it is stiff enough to stand
///     nearly vertical, which is what makes a cattail stand read as a picket fence and not a tussock.
///   * the SHEATH. Every leaf is wrapped at the bottom in a tube that clasps its neighbours, and where
///     the blade leaves that tube it carries the wine-red flush that Typha angustifolia wears - the
///     anthocyanin of a leaf that has been shut up in the dark.
///   * the SKIRT of dead old leaves. A cattail stand in a wet margin never loses its old leaves: they
///     die, collapse and stand as brown straps around the base for years, and at ground level they are
///     most of what you see.
///   * the CULMS. Two or three smooth, unbranched, almost leafless stalks - in Typha these are the
///     flowering shoots, and they rise clear of the foliage so the spike stands well above it.
///   * the SPIKE. The female inflorescence: a dense cylinder about 200 mm long and 25-30 mm across,
///     made of thousands of pistillate flowers packed onto a central axis and shingled over with the
///     remains of the perianth and the bracts. It is modelled here the way it reads from a metre away -
///     overlapping scales wound helically around the axis - so the column reads as armour rather than
///     as a texture.
///
/// THE FANTASY LAYER, and it is deliberately small so the botany still carries the read:
///
///   * the scales are COAL rather than the brown of a real spike, and every free edge keeps a BRONZE
///     line, as if the column had been fired and the fire had left a metal edge on each plate;
///   * where the real plant would carry its thread of male flowers above the female spike, this plant
///     closes that thread into a single VIOLET EMBER - a closed bud lit from inside. It is the only lit
///     part of the plant: its vertices are marked with UV2.x = 1.35, the shader's marked-nodule band, so
///     it glows at night while the coal around it stays dead.
///
/// The plant is authored in REAL METRES and converted into the engine's unit frame by
/// <see cref="NewFloraAppendWorld"/>, so what is built here is what the player sees.
/// </summary>
public static partial class OrganismMeshes
{
    // ---- the palette ---------------------------------------------------------------------------

    /// <summary>The coal of the spike: not black, but the dead grey-black of burnt bark.</summary>
    private static readonly double[] CoalspikeCoal = { 0.052, 0.050, 0.052 };
    /// <summary>The bronze the firing left on the free edge of every plate.</summary>
    private static readonly double[] CoalspikeBronze = { 0.310, 0.205, 0.086 };
    /// <summary>The warm band low on the column, where the sexes of this plant meet.</summary>
    private static readonly double[] CoalspikeWarmCoal = { 0.150, 0.080, 0.044 };
    /// <summary>The ember: violet, and hot at the tip where the bud is thinnest.</summary>
    private static readonly double[] CoalspikeEmber = { 0.520, 0.170, 0.870 };
    private static readonly double[] CoalspikeEmberHot = { 0.820, 0.470, 0.980 };
    /// <summary>The sheath: a dull, water-stained green-brown.</summary>
    private static readonly double[] CoalspikeSheath = { 0.150, 0.140, 0.082 };
    /// <summary>The wine flush of a leaf that has just come out of the sheath.</summary>
    private static readonly double[] CoalspikeFlush = { 0.330, 0.085, 0.105 };
    /// <summary>The dead skirt: brown, bleaching to straw at the tips.</summary>
    private static readonly double[] CoalspikeDead = { 0.245, 0.170, 0.100 };
    private static readonly double[] CoalspikeStraw = { 0.430, 0.340, 0.215 };
    /// <summary>The green of a Typha strap: greyer and more olive than a garden plant.</summary>
    private static readonly double[] CoalspikeLeafGreen = { 0.200, 0.330, 0.150 };
    private static readonly double[] CoalspikeLeafGreenPale = { 0.340, 0.440, 0.200 };
    /// <summary>The culm: the same green as the blade but duller, because it is a stem and not a leaf.</summary>
    private static readonly double[] CoalspikeCulm = { 0.190, 0.285, 0.140 };
    /// <summary>The pale midrib line on the upper face of a strap.</summary>
    private static readonly double[] CoalspikeMidrib = { 0.380, 0.440, 0.245 };

    // ---- the standing leaf ----------------------------------------------------------------------

    /// <summary>
    /// The width of a living strap along its length, as a fraction of its half width. A Typha strap is
    /// at its widest just above the sheath and then runs out to a long fine point; it does not narrow in
    /// the middle the way a reed blade does.
    /// </summary>
    private static double CoalspikeLeafWidth(double t)
    {
        if (t < 0.04) return 0.46 + 0.54 * (t / 0.04);     // swelling out of the sheath
        if (t < 0.20) return 1.0;                           // the full strap
        double u = (t - 0.20) / 0.80;
        return Math.Max(0.02, 1.0 - Math.Pow(u, 1.30));     // the long taper to the point
    }

    /// <summary>
    /// The pigment of a living strap: the body green, the wine flush where the blade leaves the sheath,
    /// and a slightly drier tone at the point. The flush is the only strong colour on the plant, and it
    /// is deliberately confined to the bottom fifth of the blade.
    /// </summary>
    private static double[] CoalspikeLeafColour(double t, double[] body, double[] tip, double[] flush, double value)
    {
        var col = Primitives.Mix(body, tip, MathD.SmoothStep(0.55, 1.0, t) * 0.45);
        // The flush sits where the blade leaves the sheath and is gone within a hand's width.
        col = Primitives.Mix(col, flush, MathD.SmoothStep(0.26, 0.02, t) * 0.85);
        // A little per-leaf value drift, so no two straps carry exactly the same green.
        return Primitives.Scale(col, value);
    }

    /// <summary>
    /// One living cattail leaf: a sheathing base, a wine flush where the blade leaves the sheath, and a
    /// stiff keeled strap that stands up and tapers to a long point.
    ///
    /// The section is the whole point of a Typha leaf, so it is worth saying what the builder is doing:
    /// a negative keel sinks the dorsal midline, which puts the crease UNDER the blade and leaves the
    /// upper face a shallow trough, and a small flatness keeps the strap thin instead of turning it into
    /// a gutter.
    ///
    /// The pose is stiff. A Typha leaf is nearly straight and nearly vertical, so the chord is almost
    /// upright and the only curvature allowed is a small camber - which, because the dorsal of a
    /// near-vertical blade points back toward the axis, pinches the base inward and opens the tip
    /// outward. That pinch is what a clump of strap leaves does: the bases crowd and cross, and the
    /// tips fan. There is deliberately no tip curl here: a cattail leaf does not nod.
    /// </summary>
    private static void CoalspikeLivingLeaf(MeshData m, Vec3 foot, Vec3 outward, double length, double halfWidth,
        double splay, double[] body, double[] tip, double[] flush, double[] midrib, Rng rng)
    {
        // The sheath: a tube that rises out of the crown and clasps its neighbours. It is the only part
        // of the plant that is not a blade, and it is what makes the base read as a tuft rather than as
        // a set of straps stuck into the mud.
        double sheathTop = 0.024 + rng.Range(0.0, 0.018);
        int sheathRows = 4, sheathSides = 6;
        NewFloraBudget(m, ref sheathRows, ref sheathSides, minRows: 3, minSides: 5);
        int sheathPts = Math.Clamp(sheathRows, 3, 5);
        var sheathPath = new Vec3[sheathPts];
        var sheathRad = new double[sheathPts];
        for (int i = 0; i < sheathPts; i++)
        {
            double t = i / (double)(sheathPts - 1);
            sheathPath[i] = Vec3.Lerp(new Vec3(foot.X, 0.0, foot.Z), foot + Vec3.Up * sheathTop, t);
            // The sheath is widest where it grips the mud and tapers as the blade frees itself.
            sheathRad[i] = halfWidth * (0.78 - 0.22 * t);
        }
        Primitives.Tube(m, sheathPath, sheathRad, sheathSides,
            (i, v) => (Primitives.Mix(CoalspikeSheath, flush, (double)i / (sheathPts - 1) * 0.85), 1,
                (double)i / (sheathPts - 1), v, 0, 0));

        var root = foot + Vec3.Up * sheathTop;
        // The chord of the leaf is the mean direction; the camber is what turns the base inward and the
        // tip outward, so it is solved from the splay rather than guessed.
        var along = (outward * Math.Sin(splay) + Vec3.Up * Math.Cos(splay)).Normalized();
        double camber = Math.Tan(0.5 * splay) / Math.PI;
        double value = rng.Range(0.90, 1.10);

        NewFloraBlade(m, root, along, Vec3.Up, length, halfWidth,
            CoalspikeLeafWidth,
            t => CoalspikeLeafColour(t, body, tip, flush, value),
            rows: 12, sides: 7,
            flatness: 0.26,
            keel: -0.40,          // crease under the blade, trough on top
            camber: camber,
            bow: rng.Range(-0.05, 0.05),   // a slow sweep to one side, so no two straps run parallel
            twist: rng.Range(-0.15, 0.15),
            midrib: midrib,
            midribWidth: 0.13);
    }

    // ---- the dead skirt -------------------------------------------------------------------------

    /// <summary>
    /// The width of a dead strap. It has rotted away from the tip and split down the margins, so it
    /// collapses faster than a living leaf and reads as a narrower, more ragged band.
    /// </summary>
    private static double CoalspikeDeadWidth(double t)
    {
        double u = Math.Clamp(t, 0.0, 1.0);
        double w = 0.55 + 0.45 * Math.Min(1.0, u / 0.10);
        w *= Math.Max(0.05, 1.0 - Math.Pow(Math.Max(0.0, u - 0.15) / 0.85, 1.10));
        return w;
    }

    /// <summary>
    /// The pigment of a dead strap: brown at the base where it is still damp, bleaching to straw at the
    /// tip where the sun has had at it for two seasons.
    /// </summary>
    private static double[] CoalspikeDeadColour(double t, double[] brown, double[] straw, double value)
    {
        var col = Primitives.Mix(brown, straw, MathD.SmoothStep(0.15, 0.95, t));
        return Primitives.Scale(col, value);
    }

    /// <summary>
    /// One dead old leaf, still standing. It has lost its stiffness but it has not fallen: it leaves the
    /// crown on a leaning line, humps over as it goes out, and hooks its point down at the end so the
    /// tip hangs. The camber is kept small here so that the leaf still leaves the crown steeply - a
    /// dead strap that has collapsed flat is a wire on the mud, and the point of the skirt is that the
    /// old leaves are still STANDING in it. The margin is serrated, because a strap that has been dead
    /// for a year is eaten along its edges.
    /// </summary>
    private static void CoalspikeDeadLeaf(MeshData m, Vec3 foot, Vec3 outward, double length, double halfWidth,
        double[] brown, double[] straw, Rng rng)
    {
        double elev = rng.Range(0.55, 0.85);   // radians above horizontal
        var along = (outward * Math.Cos(elev) + Vec3.Up * Math.Sin(elev)).Normalized();
        double value = rng.Range(0.85, 1.15);

        NewFloraBlade(m, foot, along, Vec3.Up, length, halfWidth,
            CoalspikeDeadWidth,
            t => CoalspikeDeadColour(t, brown, straw, value),
            rows: 8, sides: 6,
            flatness: 0.30,
            keel: -0.26,
            camber: 0.035,        // a shallow hump, so the leaf still leaves the crown steeply
            tipCurl: rng.Range(-0.28, -0.20),
            twist: rng.Range(-0.35, 0.35),
            serration: 0.30,
            teeth: 20);
    }

    // ---- the spike -----------------------------------------------------------------------------

    /// <summary>
    /// The colour of one shingled plate: coal, with a bronze line along the free edge where the plate
    /// was fired, and a faint warm band low on the column where the male and the female parts of this
    /// plant meet.
    /// </summary>
    private static double[] CoalspikeScaleColour(double whorlT, double plateT)
    {
        // The free edge of the plate is the part that stands proud of the column and catches the light,
        // so that is where the bronze sits; the buried root stays dead coal.
        var col = Primitives.Mix(CoalspikeCoal, CoalspikeBronze, MathD.SmoothStep(0.40, 1.0, plateT) * 0.72);
        // The warm band: low on the column, faint, and gone well before the middle of the spike.
        double band = MathD.SmoothStep(0.24, 0.02, whorlT);
        return Primitives.Mix(col, CoalspikeWarmCoal, band * 0.55);
    }

    /// <summary>
    /// The spike: the coal column of the female inflorescence, the armour of shingled plates wound
    /// helically around it, and the ember that closes the top of the plant.
    /// </summary>
    private static void CoalspikeSpike(MeshData m, Vec3 basePoint, Vec3 axis, double length, double radius, Rng rng)
    {
        // The core. A Typha spike is solid - thousands of pistillate flowers packed onto a central axis -
        // and it is that packed core that the armour of plates is laid over. It is also what keeps the
        // column reading as a cigar when the LOD tierer thins the plates off it.
        int rings = 9, coreSides = 8;
        NewFloraBudget(m, ref rings, ref coreSides, minRows: 4, minSides: 5);
        var path = new Vec3[rings + 1];
        var radii = new double[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            path[i] = basePoint + axis * (length * t);
            // The column swells through the middle and closes at the tip, the way a real spike does.
            double shape = 0.72 + 0.38 * Math.Sin(Math.PI * Math.Min(1.0, t * 1.04));
            radii[i] = radius * shape * 0.60;
        }
        Primitives.Tube(m, path, radii, coreSides, (i, v) => (CoalspikeCoal, 1, i / (double)rings, v, 0, 0));

        // The armour. Each plate stands for a knot of flowers; the plates are wound helically, which is
        // what a spike looks like from a metre away - overlapping scales, not a texture.
        //
        // The numbers are solved, not guessed. The builder lays a plate down at 29 degrees above
        // horizontal, so a plate of length L only reaches L*0.55 up the column; for the rows to shingle
        // over one another that has to be at least the row spacing, which is the spike length over the
        // number of rows. With a 200 mm spike, 33 rows and a 9.5 mm column radius that fixes the plate
        // at 11 mm and lands the finished column at 29 mm across: the classic cattail cigar.
        int whorls = NewFloraCount(m, 33);
        int perWhorl = NewFloraCount(m, 8);
        NewFloraScaleColumn(m, basePoint, axis, length, radius, whorls, perWhorl,
            0.0110, 0.0028, CoalspikeScaleColour, rng, droop: 0.0, spiralTurns: 1.0, glass: true);

        // The ember, sitting on the very tip of the column.
        CoalspikeEmberBud(m, basePoint + axis * (length + 0.008), rng);
    }

    /// <summary>
    /// The ember: the single lit organ of the plant. Where a real cattail would carry its thread of
    /// male flowers above the female spike, this plant closes that thread into one bud, and the bud is
    /// alight from the inside. Its vertices carry the shader's marked-nodule band (UV2.x = 1.35), so it
    /// glows at night and nothing else on the plant does.
    /// </summary>
    private static void CoalspikeEmberBud(MeshData m, Vec3 centre, Rng rng)
    {
        var col = Primitives.Mix(CoalspikeEmber, CoalspikeEmberHot, rng.Range(0.20, 0.65));
        // A closed bud: an ovoid drawn out to a point, hot at the tip where it is thinnest. The bud's
        // long axis is the local X, so the ellipse is pitched upright to stand on the column.
        int first = m.VertexCount, firstIndex = m.Indices.Count;
        Primitives.Ellipsoid(m, centre, new Vec3(0.016, 0.0072, 0.0072), 4, 8,
            (u, v) => (Primitives.Mix(col, CoalspikeEmberHot, MathD.SmoothStep(0.40, 0.0, u) * 0.85), 1, u, v, 1.35, 1),
            pitch: Math.PI * 0.5);
        // The bud is recorded as a blade so the LOD tierer may only ever drop it whole: the one lit
        // organ of the plant must not be halved by a tier cut.
        m.RecordLeaf(first, firstIndex, centre, 0.032);
    }

    // ---- the plant ------------------------------------------------------------------------------

    /// <summary>
    /// Coalspike: a tuft of keeled strap leaves standing in the shallows, a skirt of dead ones around
    /// the base, and two or three bare culms carrying the coal spike and its ember.
    /// </summary>
    private static void Coalspike(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The species tint is a wetland green. A cattail is greyer and more olive than a garden plant,
        // so the content colour is pulled towards the Typha green rather than used raw.
        var leafBody = Primitives.Mix(CoalspikeLeafGreen, c1, 0.45);
        var leafTip = Primitives.Mix(CoalspikeLeafGreenPale, c2, 0.40);
        var culmCol = Primitives.Mix(CoalspikeCulm, c1, 0.30);
        var culmTip = Primitives.Mix(CoalspikeCulm, c2, 0.22);

        // The crown: the tuft of split, papery old leaf bases that a rhizome leaves at the waterline.
        // It is a low, wet, matted boss - not a bulb - and it sits ON the mud rather than in it, so the
        // plant never reads as sunk.
        Primitives.Ellipsoid(part, new Vec3(0, 0.008, 0), new Vec3(0.020, 0.008, 0.020), 4, 9,
            (u, v) => (Primitives.Scale(Primitives.Mix(CoalspikeSheath, CoalspikeDead, MathD.SmoothStep(0.8, 0.0, u)), 0.60), 1, u, v, 0, 0));

        // The stand of leaves. They are spaced round the crown in an even ring with a jitter, because a
        // cattail clump is a picket fence, not a fan; the jitter keeps the ring from looking machined.
        // The feet are spread over the crown rather than all on one point, so the stand reads as a clump
        // of separate shoots and not as a single many-armed creature.
        int leaves = 6 + rng.NextInt(4);
        double spin = rng.Range(0, 2 * Math.PI);
        for (int k = 0; k < leaves; k++)
        {
            double a = spin + k * 2 * Math.PI / leaves + rng.Range(-0.22, 0.22);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            // The older leaves are shorter and lean further out; the young ones stand up tight.
            double age = rng.NextDouble();
            // A Typha leaf is stiff and almost vertical. It leaves the sheath at a few degrees and opens
            // a little as it rises, but it never lies over: the uprightness is the whole silhouette.
            double splay = rng.Range(0.05, 0.13) + 0.05 * age;
            double length = rng.Range(0.48, 0.72) * (1.0 - 0.14 * age);
            double halfWidth = rng.Range(0.0062, 0.0090) * (1.0 - 0.10 * age);
            var foot = outward * rng.Range(0.014, 0.048) + Vec3.Up * 0.004;
            CoalspikeLivingLeaf(part, foot, outward, length, halfWidth, splay,
                leafBody, leafTip, CoalspikeFlush, CoalspikeMidrib, rng);
        }

        // The skirt of dead old leaves. They are the oldest thing on the plant and they never fall: a
        // cattail stand in a wet margin is held up by two seasons of brown straps, and they are what
        // makes the base of the plant look solid instead of like a handful of blades in the mud.
        int dead = 7 + rng.NextInt(4);
        double dspin = rng.Range(0, 2 * Math.PI);
        for (int k = 0; k < dead; k++)
        {
            double a = dspin + k * 2 * Math.PI / dead + rng.Range(-0.30, 0.30);
            var outward = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double length = rng.Range(0.14, 0.22);
            double halfWidth = rng.Range(0.0052, 0.0080);
            var foot = outward * rng.Range(0.010, 0.032) + Vec3.Up * 0.010;
            CoalspikeDeadLeaf(part, foot, outward, length, halfWidth, CoalspikeDead, CoalspikeStraw, rng);
        }

        // The flowering culms. In Typha the flowering shoot is a separate stem that carries no leaf at
        // all above the sheath, and that is the whole reason the spike stands clear of the foliage.
        int culms = 2 + rng.NextInt(2);
        double cspin = rng.Range(0, 2 * Math.PI);
        for (int c = 0; c < culms; c++)
        {
            double a = cspin + c * 2 * Math.PI / culms + rng.Range(-0.45, 0.45);
            var foot = new Vec3(Math.Cos(a) * rng.Range(0.004, 0.016), 0.004, Math.Sin(a) * rng.Range(0.004, 0.016));
            // The culm leans as it rises; the spike then stands on the end of it and continues the line.
            var lean = new Vec3(Math.Cos(a + rng.Range(-0.7, 0.7)), 0, Math.Sin(a + rng.Range(-0.7, 0.7)));
            // The culm is the plant: the leaves are half its height and the spike is a sixth of it, so
            // the culm has to carry the rest of the way to the nominal height of the species.
            double culmLength = height * rng.Range(0.77, 0.83);
            double spikeLength = rng.Range(0.180, 0.215);
            var top = foot + Vec3.Up * culmLength + lean * rng.Range(0.008, 0.028);
            // The stem itself: nearly straight, tapering, and bowing very slightly in the middle the
            // way a living culm that has been leaning into the light does.
            int culmSegs = 8, culmSides = 7;
            NewFloraBudget(part, ref culmSegs, ref culmSides, minRows: 3, minSides: 5);
            NewFloraStalk(part, foot, top, lean, 0.012, 0.0050, 0.0030,
                culmCol, culmTip, segments: culmSegs, sides: culmSides, taper: 1.3);
            CoalspikeSpike(part, top, (top - foot).Normalized(), spikeLength, 0.0095, rng);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
