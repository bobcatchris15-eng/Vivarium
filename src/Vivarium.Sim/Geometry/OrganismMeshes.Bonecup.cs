using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Bonecup - the wood-decaying cup fungus of damp, shaded litter.
///
/// Analogue: Sarcoscypha, the elf cup: a gelatinous cushion on rotten wood from which a crowd of
/// small cups rise, each cup a real hollow with a smooth coloured hymenium inside.
///
/// The botany is the real thing, and it is mostly about hollows:
///
///   * the cushion is a gelatinous, LOBED blob of sterile tissue that sits on the wood and spreads
///     as a pale grey-pink sheet, thinning to a translucent edge where it lifts off the bark;
///   * the cups are NOT discs. Each one is a turned goblet: a short stalk, an outer wall that
///     flares, a ROLLED and WAVED lip, and a real cavity whose inner wall is the hymenium - the
///     spore-bearing skin that in the living species is a deep blood vermilion;
///   * the outside of a Sarcoscypha cup is covered in fine hairs, which is how the genus is told
///     from its smooth relatives, so the fuzz on the outside is anatomy and not decoration;
///   * the young fruit bodies are pinched shut on a pinhead stalk, dark at the tip, and they stand
///     beside the open cups;
///   * out past the thick part the sterile tissue keeps going as a thin, lobed membrane lying on
///     the wood - the spreading margin of the subiculum;
///   * fine brown MYCELIAL THREADS run out over the wood from the edge of the cushion.
///
/// The fantasy layer is the material and the fluid. The cups are cut from ALABASTER: white, hard,
/// faintly translucent at the thin lip, with a fine crystalline fuzz standing off the outside. The
/// vermilion hymenium is the only warm colour on the plant, and because it is on the inside of the
/// cup, a viewer looking down into a cup finds a red throat. And the fluid is BLACK and perfectly
/// motionless: a disc of ink that never spills, never trembles, and never runs down the wall of
/// the cup it sits in.
///
/// The plant is authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Palette
    // ---------------------------------------------------------------------------------------------

    /// <summary>The cushion's skin: a pale grey-pink, the colour of wet putty.</summary>
    private static readonly double[] BonecupCushionPale = { 0.820, 0.752, 0.742 };
    /// <summary>The cushion's deeper, shaded flesh where it meets the wood.</summary>
    private static readonly double[] BonecupCushionDeep = { 0.560, 0.440, 0.442 };
    /// <summary>The cup's outer wall: pale, faintly warm alabaster.</summary>
    private static readonly double[] BonecupAlabaster = { 0.925, 0.900, 0.862 };
    /// <summary>The thin, translucent lip of the cup.</summary>
    private static readonly double[] BonecupAlabasterThin = { 0.985, 0.968, 0.930 };
    /// <summary>The hymenium down in the shadow of the cup: a deep, almost black vermilion.</summary>
    private static readonly double[] BonecupHymeniumDeep = { 0.520, 0.045, 0.038 };
    /// <summary>The hymenium where the light actually reaches it, up by the lip.</summary>
    private static readonly double[] BonecupHymeniumLip = { 0.830, 0.175, 0.085 };
    /// <summary>The fluid: black, and it never moves.</summary>
    private static readonly double[] BonecupFluidBlack = { 0.018, 0.019, 0.026 };
    /// <summary>The meniscus at the edge of the fluid, where it catches the light.</summary>
    private static readonly double[] BonecupFluidSheen = { 0.165, 0.175, 0.215 };
    /// <summary>The mycelial threads running out over the wood.</summary>
    private static readonly double[] BonecupMycelium = { 0.400, 0.290, 0.195 };
    /// <summary>The dark pinched tip of a young fruiting body.</summary>
    private static readonly double[] BonecupBud = { 0.140, 0.080, 0.060 };
    /// <summary>The pale stalk of a young fruiting body.</summary>
    private static readonly double[] BonecupBudStalk = { 0.560, 0.490, 0.420 };

    /// <summary>
    /// One point of a cup's lathe profile: the radius from the cup's axis, the height above the
    /// foot, the height fraction that drives the UV, and the pigment of that band of the wall.
    /// </summary>
    private readonly record struct BonecupProfilePoint(double R, double Y, double U, double[] Col);

    /// <summary>
    /// The shape of the gelatinous cushion.
    ///
    /// A colony of elf cups does not sit on a dome. It sits on a low, wrinkled SHEET of sterile
    /// tissue that has spread over the bark, and the flesh has piled up into three SWELLINGS where
    /// the oldest fruit bodies grew out of it. So the shape carries a flat lobed sheet, three
    /// thick pads on it, and the wrinkle of the spreading edge. Both the edge radius and the
    /// height of the skin are angular functions, so a cup can be planted anywhere on the blob and
    /// still find the wood under its foot.
    /// </summary>
    private sealed class BonecupCushionShape
    {
        /// <summary>The mean radius of the sheet.</summary>
        public double BaseRadius;
        /// <summary>The height of the sheet's crown where nothing swells up.</summary>
        public double Height;
        private readonly double[] _swellAng = new double[4];
        private readonly double[] _swellT = new double[4];
        private readonly double[] _swellH = new double[4];
        private readonly double[] _swellW = new double[4];
        private readonly double _lobePhase;
        private readonly double _edgePhase2;
        private readonly double _edgePhase3;
        private readonly double _lobePhase2;
        private readonly double _creasePhase;

        public BonecupCushionShape(Rng rng, double baseRadius, double height)
        {
            BaseRadius = baseRadius;
            Height = height;
            _lobePhase = rng.Range(0, 2 * Math.PI);
            _edgePhase2 = rng.Range(0, 2 * Math.PI);
            _edgePhase3 = rng.Range(0, 2 * Math.PI);
            _lobePhase2 = rng.Range(0, 2 * Math.PI);
            _creasePhase = rng.Range(0, 2 * Math.PI);
            // The four swellings: the thick places where the flesh piled up. They are kept well
            // apart, because two pads of jelly that merged would stand up as one absurd spire.
            for (int i = 0; i < 4; i++)
            {
                double ang = rng.Range(0, 2 * Math.PI);
                for (int q = 0; q < 12; q++)
                {
                    bool clear = true;
                    for (int j = 0; j < i; j++)
                        if (Math.Abs(MathD.WrapAngle(ang - _swellAng[j])) < 1.15) clear = false;
                    if (clear) break;
                    ang = rng.Range(0, 2 * Math.PI);
                }
                _swellAng[i] = ang;
                _swellT[i] = rng.Range(0.15, 0.60);
                _swellH[i] = rng.Range(0.016, 0.026);
                _swellW[i] = rng.Range(0.055, 0.085);
            }
        }

        /// <summary>How far the sheet reaches at a given bearing.</summary>
        public double EdgeRadius(double ang) => BaseRadius * (1.0
            + 0.15 * Math.Sin(3.0 * ang + _lobePhase)
            + 0.07 * Math.Sin(5.0 * ang + _edgePhase2)
            + 0.03 * Math.Sin(7.0 * ang + _edgePhase3));

        /// <summary>The height of the cushion's skin at a bearing and a radial fraction.</summary>
        public double Surface(double ang, double t)
        {
            double r = t * EdgeRadius(ang);
            // The sheet itself: it lies out almost flat over the wood and stops where it stopped.
            double lobe = 1.0 + 0.26 * Math.Sin(3.0 * ang + _lobePhase) * MathD.SmoothStep(0.0, 0.45, t);
            double y = Height * (1.0 - Math.Pow(t, 3.2)) * lobe;
            // The swellings: broad flat-topped pads of thick flesh, the ones the oldest fruit
            // bodies grew out of. A cosine bell rather than a gaussian, so a pad has a plateau
            // instead of a peak, and a smooth maximum rather than a plain one, so that two pads
            // of jelly that meet merge into one soft mass instead of creasing like folded paper.
            double swell = 0;
            for (int i = 0; i < _swellH.Length; i++)
            {
                // The pad is a round blob in the plane, measured from its own centre, so the shape
                // stays continuous where the radial mesh folds in on itself at the middle.
                double pr = _swellT[i] * EdgeRadius(_swellAng[i]);
                double dx = r * Math.Cos(ang) - pr * Math.Cos(_swellAng[i]);
                double dz = r * Math.Sin(ang) - pr * Math.Sin(_swellAng[i]);
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= _swellW[i]) continue;
                double b = _swellH[i] * 0.5 * (1.0 + Math.Cos(Math.PI * d / _swellW[i]));
                double m = Math.Max(swell, b);
                swell = m + 0.006 * Math.Log(1.0 + Math.Exp(-Math.Abs(swell - b) / 0.006));
            }
            y += swell;
            // The wrinkle of the spreading edge, which is what catches the light on a jelly sheet.
            // The frequencies stay well under the angular resolution of the mesh: a wave the
            // radial grid cannot sample turns the crest of the cushion into a row of thorns.
            y += 0.28 * Height * Math.Sin(7.0 * ang + 5.0 * t + _creasePhase) * Math.Sin(Math.PI * t);
            y += 0.14 * Height * Math.Sin(13.0 * ang - 7.0 * t) * Math.Sin(Math.PI * t);
            y += 0.10 * Height * Math.Sin(4.0 * t + 3.0 * ang) * Math.Sin(Math.PI * t);
            return y;
        }

        /// <summary>
        /// The blotching of the cushion's skin, 0 to 1. A real subiculum is never one colour: it
        /// is stained in patches by the water it grew in, and the pale and the dusky areas are
        /// what make it read as a living sheet rather than as a moulded shape.
        /// </summary>
        public double Mottle(double ang, double t) => 0.5 + 0.5 * (
            0.55 * Math.Sin(4.0 * ang + 5.0 * t + _lobePhase2)
            + 0.30 * Math.Sin(9.0 * ang - 3.0 * t + _edgePhase3)
            + 0.18 * Math.Sin(13.0 * ang + 11.0 * t));
    }

    /// <summary>
    /// The wood-decaying cup fungus: a gelatinous cushion on rotten wood, a crowd of hollow
    /// alabaster goblets rising from it, and a disc of perfectly still black fluid in every cup.
    /// </summary>
    private static void Bonecup(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The species palette owns the two important colours: the alabaster white and the vermilion.
        var alabaster = Primitives.Mix(BonecupAlabaster, c1, 0.45);
        var rimPale = Primitives.Mix(BonecupAlabasterThin, c1, 0.28);
        var hymenium = Primitives.Mix(BonecupHymeniumDeep, c2, 0.55);
        var hymeniumLip = Primitives.Mix(BonecupHymeniumLip, c2, 0.32);
        var cushionPale = Primitives.Mix(BonecupCushionPale, c1, 0.32);
        var cushionDeep = Primitives.Mix(BonecupCushionDeep, c2, 0.10);
        var fluidSheen = Primitives.Mix(BonecupFluidSheen, c1, 0.08);

        // The cushion: a fat, lobed, wrinkled blob of sterile tissue on the wood. Its stem is the
        // colour of the cushion, so the cups grow out of the blob instead of standing on it.
        var shape = new BonecupCushionShape(rng, 0.048, 0.021);
        var stemCol = Primitives.Mix(cushionPale, alabaster, 0.45);
        BonecupCushion(part, shape, cushionPale, cushionDeep, rings: 14, sectors: 48);

        // The spreading margin of the subiculum. The thick part of the cushion is the pad the
        // cups sit on, but out of it the sterile tissue keeps going as a thin, lobed, translucent
        // membrane lying on the wood - the sheet the whole colony is actually made of. It is a
        // blade, so the LOD tierer can drop it whole.
        int margins = NewFloraCount(part, 4 + rng.NextInt(4));
        for (int k = 0; k < margins; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double edge = shape.EdgeRadius(ang);
            var outward = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var root = outward * (edge * 0.88) + Vec3.Up * Math.Max(0.0008, shape.Surface(ang, 0.88) - 0.0012);
            double len = rng.Range(0.010, 0.022);
            double halfW = rng.Range(0.004, 0.008);
            // The membrane drapes down off the shoulder of the cushion and out onto the wood,
            // and its margin is lobed.
            var dir = (outward + Vec3.Up * rng.Range(-0.22, -0.06)).Normalized();
            NewFloraBlade(part, root, dir, Vec3.Up, len, halfW,
                t => Math.Pow(Math.Sin(Math.PI * Math.Min(1.0, 0.05 + 0.95 * t)), 0.5)
                    * (0.74 + 0.26 * Math.Sin(4.0 * Math.PI * t)),
                t => Primitives.Mix(cushionPale, rimPale, t * 0.75),
                rows: 6, sides: 5, flatness: 0.04, camber: 0.04, volumetric: true);
        }

        // The cups. A Vogel spiral keeps the crowd even without looking stamped.
        //
        // The age of a fruit body is carried by its size: the two oldest cups are the biggest and
        // stand on the longest stems, because they had the longest to grow, and the young ones are
        // squat buds of a cup that has not opened out yet. That single rule gives the colony its
        // silhouette - a mound of goblets on a cushion - without any of them being special.
        int cups = NewFloraCount(part, 6 + rng.NextInt(7));
        int oldA = rng.NextInt(cups);
        int oldB = (oldA + 1 + rng.NextInt(cups - 1)) % cups;
        double oldAAng = -99;
        for (int k = 0; k < cups; k++)
        {
            ulong cupHash = Rng.Mix(seed, (ulong)(k * 977 + 31));
            double hashPhase = ((cupHash >> 11) % 1000) / 1000.0;
            bool old = k == oldA || k == oldB;

            // The old cups grew where the cushion was thickest, so their place is chosen by the
            // height of the blob and not by the spiral. They also keep clear of each other.
            double t, ang;
            if (old)
            {
                double bestY = -1e9; double bt = 0.1, bang = 0;
                for (int q = 0; q < 10; q++)
                {
                    double qt = rng.Range(0.03, 0.42);
                    double qa = rng.Range(0, 2 * Math.PI);
                    if (Math.Abs(MathD.WrapAngle(qa - oldAAng)) < 1.6) continue;
                    double qy = shape.Surface(qa, qt);
                    if (qy > bestY) { bestY = qy; bt = qt; bang = qa; }
                }
                t = bt; ang = bang;
                oldAAng = ang;
            }
            else
            {
                t = Math.Sqrt((k + 0.5) / cups) * rng.Range(0.55, 0.92);
                ang = k * 2.39996323 + rng.Range(-0.34, 0.34);
            }
            double r = t * shape.EdgeRadius(ang);
            var at = new Vec3(Math.Cos(ang) * r, shape.Surface(ang, t) - 0.0013, Math.Sin(ang) * r);

            // The sizes: 1.2 cm across for a young bud, 3 cm for the oldest fruit body.
            double sizeF = k == oldA ? rng.Range(0.92, 1.0)
                : k == oldB ? rng.Range(0.72, 0.88)
                : rng.Range(0.16, 0.62);
            double rimR = (0.0052 + 0.0090 * sizeF) * rng.Range(0.93, 1.07);
            // The bowl of a cup is about as deep as its radius - a goblet, not a trumpet.
            double cupH = rimR * rng.Range(0.80, 1.20);
            // The stem is short and thick. A Sarcoscypha fruit body is a cup on a stub, not a
            // goblet on a wine stem, and the stem thickens with the age of the fruit body.
            double stalkLen = rimR * (0.32 + 0.95 * sizeF * sizeF) * rng.Range(0.85, 1.15);
            double stalkR = rimR * rng.Range(0.190, 0.260);
            double wall = rimR * rng.Range(0.055, 0.085);

            // A cup leans a little out of the cushion: the colony pushes its rims towards the light.
            var lean = new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * (rng.Range(0.02, 0.17) + 0.10 * t);
            var axis = (Vec3.Up + lean).Normalized();

            BonecupCup(part, at, axis, rimR, cupH, stalkLen, stalkR, wall, alabaster, rimPale,
                hymenium, hymeniumLip, stemCol, fluidSheen, hashPhase, rng);
        }

        // The young fruit bodies: pinched shut on a pinhead stalk, standing beside the open cups.
        int pins = NewFloraCount(part, 4 + rng.NextInt(5));
        for (int k = 0; k < pins; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double t = rng.Range(0.30, 0.98);
            double r = t * shape.EdgeRadius(ang);
            var at = new Vec3(Math.Cos(ang) * r, shape.Surface(ang, t) - 0.0009, Math.Sin(ang) * r);
            double h = rng.Range(0.0025, 0.0075);
            var tip = at + new Vec3(rng.Range(-0.0016, 0.0016), h, rng.Range(-0.0016, 0.0016));
            NewFloraStalk(part, at, tip, Vec3.Up, 0.0004, 0.00058, 0.00034,
                stemCol, BonecupBudStalk, segments: 4, sides: 6);
            // The bud itself: a closed, dark teardrop, pinched at the top like an unopened cup.
            Primitives.Ellipsoid(part, tip + Vec3.Up * 0.0007, new Vec3(0.00115, 0.00200, 0.00115), 3, 8,
                (u, v) => (Primitives.Mix(BonecupBudStalk, BonecupBud, MathD.SmoothStep(0.15, 0.75, u)), 1, u, v, 1, 0));
        }

        // The mycelial threads: the fungus is not only the cups, it is the whole sheet of hyphae
        // running out over the wood from the edge of the cushion.
        int threads = NewFloraCount(part, 9 + rng.NextInt(8));
        for (int k = 0; k < threads; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double edge = shape.EdgeRadius(ang);
            double reach = rng.Range(0.005, 0.017);
            double ang2 = ang + rng.Range(-0.50, 0.50);
            var from = new Vec3(Math.Cos(ang) * edge * 0.93, 0.0005, Math.Sin(ang) * edge * 0.93);
            var mid = new Vec3(Math.Cos(ang + rng.Range(-0.2, 0.2)) * (edge + reach * 0.5), 0.0006,
                Math.Sin(ang + rng.Range(-0.2, 0.2)) * (edge + reach * 0.5));
            var to = new Vec3(Math.Cos(ang2) * (edge + reach), 0.0004, Math.Sin(ang2) * (edge + reach));
            NewFloraFilament(part, from, mid, 0.00013, BonecupMycelium, sides: 5);
            NewFloraFilament(part, mid, to, 0.00011, BonecupMycelium, sides: 5);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// The gelatinous cushion: a lobed, wrinkled sheet of sterile tissue lying on the wood.
    ///
    /// It is a radial height field, not a dome: the radius of the edge and the height of the skin
    /// are both angular functions, so the blob has three big lobes and a fine crease pattern over
    /// the top of it, and it thins to nothing at its rim where the flesh runs translucent.
    /// </summary>
    private static void BonecupCushion(MeshData m, BonecupCushionShape shape, double[] pale, double[] deep,
        int rings, int sectors)
    {
        NewFloraBudget(m, ref rings, ref sectors, minRows: 4, minSides: 8);

        var grid = new int[rings + 1, sectors + 1];
        var normals = new Vec3[rings + 1, sectors + 1];

        // The crown of the cushion is a single pole, so its height is the mean of the first ring.
        double poleY = 0;
        for (int j = 0; j < sectors; j++) poleY += shape.Surface(2 * Math.PI * j / sectors, 1.0 / rings);
        poleY /= sectors;

        for (int i = 0; i <= rings; i++)
        {
            double t = i / (double)rings;
            for (int j = 0; j <= sectors; j++)
            {
                double ang = 2 * Math.PI * j / sectors;
                double r = i == 0 ? 0.0 : t * shape.EdgeRadius(ang);
                double y = i == 0 ? poleY : shape.Surface(ang, t);
                var p = new Vec3(Math.Cos(ang) * r, y, Math.Sin(ang) * r);

                // The flesh pales towards the rim, where it is thin enough to be translucent, and
                // it is blotched all over by the stains the colony grew in.
                var col = Primitives.Mix(deep, pale, MathD.SmoothStep(0.18, 0.78, t));
                col = Primitives.Mix(col, Primitives.Scale(pale, 1.06), MathD.SmoothStep(0.80, 1.0, t) * 0.85);
                double mottle = shape.Mottle(ang, t);
                col = Primitives.Mix(col, Primitives.Scale(deep, 1.06), MathD.SmoothStep(0.55, 1.0, mottle) * 0.45);

                grid[i, j] = m.AddVertex(p, Vec3.Up, col, 1, t, (double)j / sectors, 1, 0);
            }
        }

        // Faces, with the face normals accumulated so the cushion shades as one continuous skin
        // instead of a stack of flat facets.
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                var pa = m.Position(a);
                var fn = i == 0
                    // The fan off the pole: the same orientation as the rings below it.
                    ? (m.Position(d) - pa).Cross(m.Position(c) - pa)
                    : (m.Position(b) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-20) continue;
                fn = fn.Normalized();
                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;
                if (i == 0)
                {
                    Primitives.TriangleFacing(m, a, c, d, fn);
                }
                else
                {
                    Primitives.TriangleFacing(m, a, b, c, fn);
                    Primitives.TriangleFacing(m, b, d, c, fn);
                }
            }
        }

        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // The underside: a flat disc lying on the wood, so the cushion never shows a hollow belly.
        int under = m.AddVertex(Vec3.Zero, Vec3.Down, deep, 1, 0.0, 0.5, 1, 0);
        for (int j = 0; j < sectors; j++)
        {
            int a = grid[rings, j], b = grid[rings, j + 1];
            var fn = (m.Position(a) - m.Position(under)).Cross(m.Position(b) - m.Position(under));
            if (fn.LengthSq < 1e-20) continue;
            fn = fn.Normalized();
            Primitives.TriangleFacing(m, under, a, b, fn);
        }
    }

    /// <summary>
    /// One cup of the fungus: a hollow goblet turned on a lathe.
    ///
    /// The profile is the whole trick. It runs up the outside of the stalk, out along the flared
    /// outer wall, over a half-turn of rolled lip, and back down the inner wall to the floor of
    /// the cavity, so the cup is a closed shell of real thickness with an open throat you can see
    /// down into - not a disc, and not a cone.
    ///
    /// The outer wall flares and then straightens, so the lip can be rolled without a crease at
    /// the join. The inner wall is the same curve pulled in by the wall thickness, which keeps the
    /// shell honest: thin at the rim, thicker down where the flesh has to carry the cup.
    /// </summary>
    private static void BonecupCup(MeshData m, Vec3 basePoint, Vec3 axis, double rimRadius, double cupHeight,
        double stalkLength, double stalkRadius, double wall, double[] outer, double[] rimPale, double[] inner,
        double[] innerLip, double[] stem, double[] fluidSheen, double hashPhase, Rng rng)
    {
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;

        int sides = 22;
        int rings = 10;
        NewFloraBudget(m, ref rings, ref sides, minRows: 5, minSides: 8);

        // --- the lathe profile -------------------------------------------------------------------
        double roll = wall * 0.5;                       // the radius of the rolled lip
        double topY = stalkLength + cupHeight;          // the crown of the lip
        double wallTopY = topY - roll;                  // where the outer wall meets the roll
        double floorY = stalkLength + wall * 0.60;      // the floor of the cavity
        double floorR = stalkRadius * 0.55;
        double innerTopR = rimRadius - wall;            // the inner lip, one wall thickness in

        int stalkPts = 3 + rings / 3;
        int wallPts = 4 + rings / 2;
        int rimPts = 3 + rings / 4;
        int innerPts = 3 + rings / 2;

        var prof = new List<BonecupProfilePoint>(40);
        // The foot of the cup, buried in the cushion: a pole so the underside can be closed.
        prof.Add(new BonecupProfilePoint(0.0, 0.0, 0.0, stem));

        // The stalk: a short column with a node swelling in the middle, the way a compressed
        // stem thickens where the flesh above it hangs.
        for (int i = 0; i <= stalkPts; i++)
        {
            double t = i / (double)stalkPts;
            double y = stalkLength * t;
            double r = stalkRadius * (1.30 - 0.30 * t + 0.16 * Math.Sin(Math.PI * t));
            var col = Primitives.Mix(stem, outer, MathD.SmoothStep(0.0, 0.75, t));
            prof.Add(new BonecupProfilePoint(r, y, y / topY, col));
        }

        // The outer wall: it leaves the stalk almost vertically and then flares out hard, so the
        // cup has the deep throat of a goblet rather than the shallow saucer of a bracket.
        int wallFirst = prof.Count;
        for (int i = 1; i <= wallPts; i++)
        {
            double t = i / (double)wallPts;
            double y = stalkLength + (wallTopY - stalkLength) * t;
            double r = stalkRadius + (rimRadius - stalkRadius) * Math.Pow(Math.Sin(t * Math.PI * 0.5), 1.45);
            var col = Primitives.Mix(outer, rimPale, MathD.SmoothStep(0.55, 1.0, t) * 0.55);
            prof.Add(new BonecupProfilePoint(r, y, y / topY, col));
        }
        int wallLast = prof.Count - 1;

        // The rolled lip: half a turn of a very small circle. The outer half is the thin alabaster
        // edge, the inner half turns down into the vermilion hymenium.
        for (int i = 1; i <= rimPts; i++)
        {
            double a = Math.PI * i / rimPts;
            double r = rimRadius - roll + roll * Math.Cos(a);
            double y = wallTopY + roll * Math.Sin(a);
            var col = Primitives.Mix(rimPale, innerLip, MathD.SmoothStep(0.30 * Math.PI, 0.80 * Math.PI, a));
            prof.Add(new BonecupProfilePoint(r, y, y / topY, col));
        }

        // The inner wall, running back down to the floor. It is the outer curve pulled inward, so
        // the shell keeps a real thickness all the way round the cup.
        for (int i = 1; i <= innerPts; i++)
        {
            double v = 1.0 - i / (double)innerPts;
            double y = floorY + (wallTopY - floorY) * v;
            double r = floorR + (innerTopR - floorR) * Math.Pow(Math.Sin(v * Math.PI * 0.5), 1.45);
            var col = Primitives.Mix(inner, innerLip, MathD.SmoothStep(0.50, 1.0, v));
            prof.Add(new BonecupProfilePoint(r, y, y / topY, col));
        }

        // The floor of the cavity, closing the cup on its axis.
        prof.Add(new BonecupProfilePoint(floorR * 0.70, floorY - wall * 0.12, floorY / topY, inner));
        prof.Add(new BonecupProfilePoint(floorR * 0.32, floorY - wall * 0.20, floorY / topY, inner));
        prof.Add(new BonecupProfilePoint(0.0, floorY - wall * 0.22, floorY / topY, inner));

        // --- the frame of the cup ---------------------------------------------------------------
        var up = axis.Normalized();
        var right = up.Cross(new Vec3(0, 0, 1));
        if (right.LengthSq < 1e-10) right = up.Cross(new Vec3(1, 0, 0));
        right = right.Normalized();
        var fwd = right.Cross(up).Normalized();

        // The lip of a real elf cup is not a circle: it is waved and lobed. The wave is applied to
        // the upper part of the profile only, so the foot of the cup stays planted.
        double waveAmp = cupHeight * 0.055 * rng.Range(0.70, 1.30);
        double waveBand = cupHeight * 0.55;
        double wavePhase1 = rng.Range(0, 2 * Math.PI) + hashPhase * 6.283;
        double wavePhase2 = rng.Range(0, 2 * Math.PI);
        double oval = rng.Range(0.02, 0.07);
        double ovalPhase = rng.Range(0, 2 * Math.PI);

        // The profile's own normal, used to wind the faces and to stand the hairs on the wall.
        int n = prof.Count;
        var pnR = new double[n];
        var pnY = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
            double dr = prof[b].R - prof[a].R;
            double dy = prof[b].Y - prof[a].Y;
            double len = Math.Sqrt(dr * dr + dy * dy);
            if (len < 1e-9) { pnR[i] = 1.0; pnY[i] = 0.0; }
            else { pnR[i] = dy / len; pnY[i] = -dr / len; }
        }

        // The surface of the cup at a point of the profile and a bearing, with the wave and the
        // ovality of the cup applied.
        Vec3 SurfacePoint(int i, double f, double ang)
        {
            int j = Math.Min(i + 1, n - 1);
            double rr = prof[i].R + (prof[j].R - prof[i].R) * f;
            double yy = prof[i].Y + (prof[j].Y - prof[i].Y) * f;
            double w = MathD.SmoothStep(wallTopY - waveBand, topY, yy);
            double dy = waveAmp * (Math.Sin(3.0 * ang + wavePhase1) + 0.55 * Math.Sin(5.0 * ang + wavePhase2)) * w;
            double dr = waveAmp * 0.30 * Math.Cos(3.0 * ang + wavePhase1) * w;
            rr = (rr + dr) * (1.0 + oval * Math.Cos(2.0 * ang + ovalPhase));
            return basePoint + up * (yy + dy) + (right * Math.Cos(ang) + fwd * Math.Sin(ang)) * rr;
        }

        // --- the lathe ---------------------------------------------------------------------------
        var grid = new int[n, sides + 1];
        var normals = new Vec3[n, sides + 1];

        for (int i = 0; i < n; i++)
        {
            bool pole = prof[i].R <= 1e-9;
            for (int j = 0; j <= sides; j++)
            {
                if (pole)
                {
                    if (j > 0) { grid[i, j] = grid[i, 0]; continue; }
                    grid[i, 0] = m.AddVertex(SurfacePoint(i, 0.0, 0.0), -up, prof[i].Col, 1, prof[i].U, 0.5, 1, 0);
                    continue;
                }
                double ang = 2 * Math.PI * j / sides;
                var p = SurfacePoint(i, 0.0, ang);
                grid[i, j] = m.AddVertex(p, up, prof[i].Col, 1, prof[i].U, (double)j / sides, 1, 0);
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            bool poleA = prof[i].R <= 1e-9;
            bool poleB = prof[i + 1].R <= 1e-9;
            if (poleA && poleB) continue;
            // The normal of the band, from the profile's own normal: it points out of the cup on the
            // outside and into the cavity on the inside, which is what winds the faces correctly.
            double nr = poleA ? pnR[i + 1] : poleB ? pnR[i] : 0.5 * (pnR[i] + pnR[i + 1]);
            double ny = poleA ? pnY[i + 1] : poleB ? pnY[i] : 0.5 * (pnY[i] + pnY[i + 1]);
            for (int j = 0; j < sides; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                double angMid = 2 * Math.PI * (j + 0.5) / sides;
                var want = (right * Math.Cos(angMid) + fwd * Math.Sin(angMid)) * nr + up * ny;
                var pa = m.Position(a);
                // The face normal, accumulated into the corners so the cup shades as a continuous
                // piece of flesh rather than as a stack of flat facets.
                var fn = poleA
                    ? (m.Position(c) - pa).Cross(m.Position(d) - pa)
                    : (m.Position(c) - pa).Cross(m.Position(b) - pa);
                if (fn.LengthSq < 1e-20)
                {
                    fn = poleA
                        ? (m.Position(d) - pa).Cross(m.Position(c) - pa)
                        : (m.Position(b) - pa).Cross(m.Position(c) - pa);
                }
                if (fn.LengthSq < 1e-20) continue;
                fn = fn.Normalized();
                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;
                if (poleA) Primitives.TriangleFacing(m, a, c, d, want);
                else if (poleB) Primitives.TriangleFacing(m, a, c, b, want);
                else
                {
                    Primitives.TriangleFacing(m, a, b, c, want);
                    Primitives.TriangleFacing(m, b, d, c, want);
                }
            }
        }

        for (int i = 0; i < n; i++)
            for (int j = 0; j <= sides; j++)
            {
                var nrm = normals[i, j];
                if (nrm.LengthSq < 1e-18) nrm = up;
                nrm = nrm.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)nrm.X;
                m.Normals[v * 3 + 1] = (float)nrm.Y;
                m.Normals[v * 3 + 2] = (float)nrm.Z;
            }

        // --- the fluid --------------------------------------------------------------------------
        // A still disc of black liquid well down the cup, so the vermilion throat of the hymenium
        // is still the first thing a viewer sees when they look in. It is perfectly flat in the
        // middle, with a meniscus climbing the wall, and it is the darkest thing on the plant.
        double fluidY = floorY + (wallTopY - floorY) * rng.Range(0.22, 0.46);
        double fluidV = (fluidY - floorY) / Math.Max(1e-6, wallTopY - floorY);
        double fluidR = floorR + (innerTopR - floorR) * Math.Pow(Math.Sin(fluidV * Math.PI * 0.5), 1.45) * 0.985;
        var fluidCentre = basePoint + up * fluidY;
        BonecupFluid(m, fluidCentre, up, right, fluidR, BonecupFluidBlack, fluidSheen);

        // --- the fuzz ---------------------------------------------------------------------------
        // The outside of a Sarcoscypha cup is hairy, and the hairs are thickest towards the lip.
        // They are also the reason the fantasy reads as carved alabaster rather than as plastic.
        // The hairs lie back along the wall instead of standing straight out, the way a nap does.
        int hairs = NewFloraCount(m, 55 + rng.NextInt(40));
        for (int h = 0; h < hairs; h++)
        {
            // Biased towards the rim, where the flesh is youngest and the fuzz is densest.
            double u = 0.18 + 0.82 * Math.Pow(rng.NextDouble(), 0.55);
            int wi = wallFirst + (int)(u * wallPts);
            if (wi >= wallLast) wi = wallLast - 1;
            if (wi < wallFirst) wi = wallFirst;
            double f = u * wallPts - (wi - wallFirst);
            f = Math.Clamp(f, 0.0, 1.0);
            double ang = rng.Range(0, 2 * Math.PI);

            var at = SurfacePoint(wi, f, ang);
            double nr = prof[wi].R <= 1e-9 ? 1.0 : pnR[wi] + (pnR[wi + 1] - pnR[wi]) * f;
            double ny = prof[wi].R <= 1e-9 ? 0.0 : pnY[wi] + (pnY[wi + 1] - pnY[wi]) * f;
            double nl = Math.Sqrt(nr * nr + ny * ny);
            if (nl < 1e-9) { nr = 1.0; ny = 0.0; nl = 1.0; }
            nr /= nl; ny /= nl;
            var radial = right * Math.Cos(ang) + fwd * Math.Sin(ang);
            var outN = radial * nr + up * ny;

            // The direction up the wall, which the nap follows.
            double tr = prof[wi + 1].R - prof[wi].R;
            double ty = prof[wi + 1].Y - prof[wi].Y;
            double tl = Math.Sqrt(tr * tr + ty * ty);
            if (tl < 1e-9) { tr = 0.0; ty = 1.0; tl = 1.0; }
            var wallUp = radial * (tr / tl) + up * (ty / tl);

            var dir = (wallUp * 0.85 + outN * 0.45).Normalized();
            double len = rng.Range(0.00022, 0.00058);
            BonecupHair(m, at, dir, len, 0.000055, outer, rimPale);
        }

        m.RecordLeaf(leafVertex, leafIndex, basePoint, topY, volumetric: true);
    }

    /// <summary>
    /// The still disc of fluid at the bottom of a cup. Flat in the middle, with a meniscus that
    /// climbs the wall and a pale rim where the light catches it, and closed underneath so it is
    /// a body of liquid rather than a floating disc of paint.
    /// </summary>
    private static void BonecupFluid(MeshData m, Vec3 centre, Vec3 up, Vec3 right, double radius,
        double[] black, double[] sheen)
    {
        int seg = 14;
        var fwd = right.Cross(up).Normalized();
        double meniscus = radius * 0.035;

        // The rings, from the middle of the pool outwards and then back under the edge.
        var rings = new (double R, double Y, double[] Col)[]
        {
            (radius * 0.90, 0.0, black),
            (radius, meniscus, sheen),
            (radius * 0.985, -meniscus * 2.2, Primitives.Mix(black, sheen, 0.45)),
        };

        int mid = m.AddVertex(centre, up, black, 1, 0.5, 0.5, 1, 0);
        var ringIds = new int[rings.Length, seg + 1];
        for (int k = 0; k < rings.Length; k++)
        {
            for (int j = 0; j <= seg; j++)
            {
                double ang = 2 * Math.PI * j / seg;
                var p = centre + (right * Math.Cos(ang) + fwd * Math.Sin(ang)) * rings[k].R + up * rings[k].Y;
                ringIds[k, j] = m.AddVertex(p, up, rings[k].Col, 1, rings[k].R / Math.Max(1e-9, radius), (double)j / seg, 1, 0);
            }
        }
        int under = m.AddVertex(centre - up * (meniscus * 3.4), -up, black, 1, 0.5, 0.5, 1, 0);

        for (int j = 0; j < seg; j++)
        {
            var fn = (m.Position(ringIds[0, j + 1]) - m.Position(mid)).Cross(m.Position(ringIds[0, j]) - m.Position(mid));
            if (fn.LengthSq > 1e-20) Primitives.TriangleFacing(m, mid, ringIds[0, j + 1], ringIds[0, j], up);
            for (int k = 0; k < rings.Length - 1; k++)
            {
                int a = ringIds[k, j], b = ringIds[k, j + 1], c = ringIds[k + 1, j], d = ringIds[k + 1, j + 1];
                var pa = m.Position(a);
                var g = (m.Position(c) - pa).Cross(m.Position(b) - pa);
                if (g.LengthSq > 1e-20) g = g.Normalized();
                Primitives.TriangleFacing(m, a, b, c, g);
                Primitives.TriangleFacing(m, b, d, c, g);
            }
            var fu = (m.Position(ringIds[rings.Length - 1, j]) - m.Position(under)).Cross(
                (m.Position(ringIds[rings.Length - 1, j + 1]) - m.Position(under)));
            if (fu.LengthSq > 1e-20) Primitives.TriangleFacing(m, under, ringIds[rings.Length - 1, j + 1], ringIds[rings.Length - 1, j], -up);
        }
    }

    /// <summary>
    /// One hair of the cup's outer fuzz: a three-sided tapered prickle. It is a solid rather than
    /// a ribbon, so it still catches the light from every side, and it is tiny enough that three
    /// faces are enough.
    /// </summary>
    private static void BonecupHair(MeshData m, Vec3 from, Vec3 dir, double length, double radius,
        double[] col, double[] tipCol)
    {
        var d = dir.Normalized();
        var t1 = d.Cross(new Vec3(0, 1, 0));
        if (t1.LengthSq < 1e-10) t1 = d.Cross(new Vec3(1, 0, 0));
        t1 = t1.Normalized();
        var t2 = d.Cross(t1).Normalized();

        var ring = new int[3];
        var radial = new Vec3[3];
        for (int k = 0; k < 3; k++)
        {
            double a = 2 * Math.PI * k / 3.0;
            radial[k] = (t1 * Math.Cos(a) + t2 * Math.Sin(a));
            ring[k] = m.AddVertex(from + radial[k] * radius, radial[k], col, 1, 0.0, (double)k / 3.0, 1, 0);
        }
        int tip = m.AddVertex(from + d * length, d, tipCol, 1, 1.0, 0.5, 1, 0);
        for (int k = 0; k < 3; k++)
        {
            int k2 = (k + 1) % 3;
            Primitives.TriangleFacing(m, ring[k], ring[k2], tip, radial[k]);
        }
    }
}
