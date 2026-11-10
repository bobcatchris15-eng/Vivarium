using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

public enum BroadleafOutline
{
    Cordate,   // Heart-shaped (deep rounded lobes at base, tapering towards tip)
    Sagittate, // Arrowhead-shaped (pointed downward/backward lobes, triangular body)
    Hastate,   // Spear-shaped (outward-flaring basal lobes)
    Ovate,     // Broadly oval, rounded base
    Lanceolate, // Tapering lance shape
    OrbicularCordate, // Broad rhubarb-like near-round heart with basal sinus and blunt shoulders
    Reniform // Kidney/fan-shaped: flares out rapidly from the pedicel into a broad, wide blade
}

public readonly record struct BroadleafParams(
    BroadleafOutline Outline = BroadleafOutline.Cordate,
    int LeafCount = 7,                 // 4-9 leaves per plant
    double StemHeight = 0.7,           // Central stalk height
    double StemRadius = 0.018,         // Stalk base radius
    double MinLeafLength = 0.22,       // Youngest leaf length
    double MaxLeafLength = 0.52,       // Oldest leaf length
    double WidthToLength = 0.55,       // Aspect ratio
    double PetioleLength = 0.14,       // Petiole reach
    double PetioleRadius = 0.005,      // Petiole thickness
    double PetioleKink = 0.35,         // Kink angle at junction
    double Camber = 0.14,              // Camber depth
    double MidribFold = 0.12,          // Midrib crease depth
    double WavyMargin = 0.04,          // Undulation amplitude
    double WavyFrequency = 5.0,        // Undulation frequency
    double TipCurl = 0.10,             // Tip droop
    double PhyllotaxisAngle = 2.399963,// Golden angle ~137.5 degrees
    double MinPitch = 0.35,            // Younger leaves more erect (radians from vertical)
    double MaxPitch = 1.15,            // Older leaves more spreading/drooping
    Vec3 Origin = default,
    int DetailLevel = 0
);

public static class Broadleaf
{
    private static (double HalfWidthEnv, double LobeBack) SampleOutline(
        BroadleafOutline outline, double t, double x, double bladeLength)
    {
        t = MathD.Clamp01(t);
        double absX = Math.Abs(x);
        return outline switch
        {
            BroadleafOutline.Cordate => (
                // Heart: opens rapidly into broad rounded lobes in proximal third, tapers to acute tip
                Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.42))), 0.75) * (1.0 + 0.35 * (1.0 - t)),
                t < 0.28 ? -0.16 * bladeLength * Math.Pow(1.0 - t / 0.28, 2.0) * (x * x) : 0.0
            ),
            BroadleafOutline.Sagittate => (
                // Arrowhead: triangular body tapering to tip, basal barbs extending back and out
                Math.Pow(1.0 - t, 0.65) * (1.0 + 0.25 * (1.0 - t)) * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.28))), 0.45)
                    + (t < 0.35 ? 0.18 * Math.Pow(1.0 - t / 0.35, 1.5) * absX : 0.0),
                t < 0.35 ? -0.22 * bladeLength * (1.0 - t / 0.35) * absX : 0.0
            ),
            BroadleafOutline.Hastate => (
                // Spear: basal lobes flare outward horizontally
                Math.Pow(1.0 - t, 0.80) * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.35))), 0.50)
                    + (t < 0.25 ? 0.50 * Math.Pow(1.0 - t / 0.25, 2.0) * absX : 0.0),
                t < 0.20 ? -0.08 * bladeLength * (1.0 - t / 0.20) * (x * x) : 0.0
            ),
            BroadleafOutline.OrbicularCordate => (
                // Rhubarb-like broad, near-circular heart. The blade stays wide
                // toward its distal third instead of becoming a hanging spear.
                // Mild retuse base preserves two independent rounded basal lobes.
                Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.70))), 0.44)
                    * (1.0 + 0.08 * (1.0 - t)),
                t < 0.33 ? -0.15 * bladeLength * Math.Pow(1.0 - t / 0.33, 1.7) * (x * x) : 0.0
            ),
            BroadleafOutline.Ovate => (
                // Broad oval: widest below middle, rounded base
                Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.60))), 0.80) * (1.0 + 0.15 * (1.0 - t)),
                0.0
            ),
            BroadleafOutline.Lanceolate => (
                // Slender lance
                Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * t)), 1.25) * 0.70,
                0.0
            ),
            BroadleafOutline.Reniform => (
                // Kidney/fan shape: flares out rapidly from the pedicel into a broad, wide blade
                Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(t, 0.30))), 0.55) * (1.0 + 0.12 * (1.0 - t)),
                t < 0.28 ? -0.14 * bladeLength * Math.Pow(1.0 - t / 0.28, 2.0) * (x * x) : 0.0
            ),
            _ => (Math.Max(0.0, Math.Sin(Math.PI * t)), 0.0)
        };
    }

    /// <summary>
    /// Builds a single broadleaf blade with realistic petiole kink, outline profile, camber, midrib crease,
    /// and wavy margin displacement into <paramref name="m"/>.
    /// </summary>
    public static int BuildLeaf(
        MeshData m,
        Vec3 attachPos,
        Vec3 petioleDir,
        double bladeLength,
        double bladeWidth,
        BroadleafOutline outline,
        ulong seed,
        double[] c1,
        double[] c2,
        double age = 0.5,
        double camber = 0.14,
        double midribFold = 0.12,
        double wavyMargin = 0.04,
        double wavyFrequency = 5.0,
        double tipCurl = 0.10,
        double petioleLength = 0.08,
        double petioleRadius = 0.004,
        double petioleKink = 0.35,
        double asymmetry = 0.0,
        double roll = 0.0,
        double[]? undersideColor = null,
        int? detailOverride = null,
        ulong parentPartId = 0)
    {
        int startTris = m.TriangleCount;
        if (bladeLength <= 1e-6 || bladeWidth <= 1e-6) return 0;

        int detail = detailOverride ?? m.FloraDetailLevel ?? 0;
        var pDir = petioleDir.LengthSq > 1e-6 ? petioleDir.Normalized() : Vec3.Up;
        var up = Vec3.Up;

        // 1. Petiole construction with realistic kink at junction
        Vec3 junctionPos = attachPos;
        Vec3 bladeFwd = pDir;

        if (petioleLength > 1e-4 && petioleRadius > 1e-5)
        {
            var p0 = attachPos;
            var p1 = p0 + pDir * (petioleLength * 0.45) + up * (petioleLength * 0.20);
            var p2 = p0 + pDir * (petioleLength * 0.82) + up * (petioleLength * 0.22);
            junctionPos = p2 + pDir * (petioleLength * 0.18) - up * (petioleLength * 0.04);

            bladeFwd = (pDir * Math.Cos(petioleKink) - up * Math.Sin(petioleKink)).Normalized();
            if (bladeFwd.LengthSq < 1e-6) bladeFwd = pDir;

            int pSides = detail == 0 ? 5 : (detail == 1 ? 4 : 3);
            var pPath = detail >= 2
                ? new[] { p0, junctionPos }
                : new[] { p0, p1, p2, junctionPos };
            var pRadii = detail >= 2
                ? new[] { petioleRadius * 1.15, petioleRadius * 0.85 }
                : new[] { petioleRadius * 1.15, petioleRadius * 1.05, petioleRadius * 0.95, petioleRadius * 0.85 };
            var pCol = Primitives.Mix(new[] { 0.28, 0.24, 0.18 }, c1, 0.40 + 0.30 * age);
            Primitives.Tube(m, pPath, pRadii, pSides, (i, v) => (pCol, 1.0, i / (double)(pPath.Length - 1), v, 0, 0));
        }

        // 2. Leaf blade coordinate basis
        var fwd = bladeFwd.Normalized();
        var side = fwd.Cross(up).Normalized();
        if (side.LengthSq < 1e-6) side = fwd.Cross(new Vec3(1, 0, 0)).Normalized();
        if (roll != 0.0) side = Axis.RotateAround(side, fwd, roll).Normalized();
        var normal0 = side.Cross(fwd).Normalized();

        double wavyPhase = Rng.HashUnit(seed, Hash.Fnv1a64("form.broadleaf.wavy"), 0) * Math.PI * 2.0;

        // Position evaluator on the leaf parametric surface with mid-surface camber & adaxial venation relief
        Vec3 SurfacePos(double t, double x)
        {
            var (env, lobeBack) = SampleOutline(outline, t, x, bladeLength);
            double phase = wavyPhase + t * wavyFrequency * Math.PI * 2.0;
            double organic = Math.Sin(Math.PI * t);
            double skew = asymmetry + 0.035 * Math.Sin(wavyPhase);
            double absX = Math.Abs(x);

            // Botanical Margin Profile:
            // 1. Dentate / Serrate (asymmetric forward-pointing teeth)
            double marginToothFreq = 18.0;
            double tf = (t * marginToothFreq) % 1.0;
            if (tf < 0) tf += 1.0;
            double toothShape = tf * tf * (1.0 - tf) * 6.75;
            double toothMod = 0.045 * Math.Sin(Math.PI * t) * toothShape * Math.Pow(absX, 3.5);

            // 2. Crenate / Undulate (scalloped lobes with subtle 3D vertical edge wave)
            double edgeWave = wavyMargin * (bladeWidth * 0.4) * Math.Sin(wavyPhase + t * 16.0) * Math.Pow(absX, 4.0) * organic;

            // 3. Acuminate Drip-Tip (curved downward-arching apex at t in [0.85, 1.0])
            double dripTip = t > 0.85 ? Math.Pow((t - 0.85) / 0.15, 2.2) * 0.06 * bladeLength : 0.0;

            double marginMod = 1.0 + wavyMargin * 0.35 * Math.Cos(phase) * (x * x) * organic + toothMod;
            double hw = (bladeWidth * 0.5) * env * marginMod * (1.0 + skew * x);
            var lateral = side * (x * hw);

            double droop = tipCurl * bladeLength * (t * t * 0.75 + 0.25 * t * t * t) + dripTip;
            var pMidrib = junctionPos + fwd * (t * bladeLength + lobeBack) - normal0 * droop;

            double zCamber = camber * (bladeWidth * 0.5) * (1.0 - x * x) * Math.Sin(Math.PI * Math.Pow(t, 0.40));
            double zFold = -midribFold * (bladeWidth * 0.5) * absX * Math.Sin(Math.PI * Math.Pow(t, 0.40));
            double zWave = wavyMargin * (bladeWidth * 0.5) * Math.Sin(phase) * (x * x) * organic;
            var crown = normal0 * (zCamber + zFold + zWave + edgeWave);

            var basePos = pMidrib + lateral + crown + side * (bladeWidth * 0.025 * Math.Sin(wavyPhase + t * 2.0) * organic)
                + normal0 * (bladeWidth * 0.018 * Math.Sin(phase * 0.45 + x * 1.7) * organic * x * x);

            // Adaxial (Upper): recessed midrib canaliculate groove (-0.4 mm) and convex bullate pillowing (+0.8 to +2.2 mm)
            double veinPhase = (t - 0.22 * absX) * 12.0 * Math.PI * 2.0;
            double groove = -0.0004 * Math.Exp(-x * x * 64.0) * Math.Sin(Math.PI * Math.Pow(t, 0.40));
            double bullateAmp = Math.Clamp(bladeWidth * 0.008, 0.0008, 0.0022);
            double bullate = bullateAmp * (0.5 - 0.5 * Math.Cos(veinPhase)) * (1.0 - Math.Pow(x, 4.0)) * organic;
            double normalOffset = groove + bullate;

            return basePos + normal0 * normalOffset;
        }

        // 3. Grid discretization: WP-19 high-resolution tessellation
        int rows = detail == 0 ? 28 : (detail == 1 ? 20 : 12);
        int cols = detail == 0 ? 16 : (detail == 1 ? 12 : 8);
        if (outline == BroadleafOutline.OrbicularCordate)
        {
            rows = detail == 2 ? 10 : (detail == 1 ? 16 : 20);
            cols = detail == 2 ? 6 : (detail == 1 ? 10 : 12);
        }
        if (m.Lod is { } lod) rows = lod.Segments(rows, detail == 0 ? 12 : 8);
        if (m.Lod is { } lodC) cols = Math.Max(4, (int)Math.Round(cols * lodC.Blade));

        var grid = new Vec3[rows + 1, cols + 1];
        var gridNormals = new Vec3[rows + 1, cols + 1];

        for (int r = 0; r <= rows; r++)
        {
            double t = (double)r / rows;
            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                grid[r, c] = SurfacePos(t, x);
            }
        }

        for (int r = 0; r <= rows; r++)
        {
            for (int c = 0; c <= cols; c++)
            {
                Vec3 tT = rows > 0
                    ? (r == 0 ? grid[1, c] - grid[0, c] : (r == rows ? grid[rows, c] - grid[rows - 1, c] : grid[r + 1, c] - grid[r - 1, c]))
                    : fwd;

                Vec3 tX = cols > 0
                    ? (c == 0 ? grid[r, 1] - grid[r, 0] : (c == cols ? grid[r, cols] - grid[r, cols - 1] : grid[r, c + 1] - grid[r, c - 1]))
                    : side;

                if (tX.LengthSq < 1e-8) tX = side;
                if (tT.LengthSq < 1e-8) tT = fwd;

                var n = tX.Cross(tT).Normalized();
                if (n.LengthSq < 1e-6 || n.Dot(normal0) < 0) n = normal0;
                gridNormals[r, c] = n;
            }
        }

        // Color & age tinting: younger leaves paler/greener/fresher, older leaves deeper/darker
        var youngBase = Primitives.Mix(c2, new[] { 0.65, 0.82, 0.35 }, 0.40);
        var youngTip = Primitives.Mix(c2, new[] { 0.82, 0.92, 0.50 }, 0.50);
        var matureBase = Primitives.Scale(c1, 0.72);
        var matureTip = Primitives.Mix(c1, c2, 0.30);

        var baseCol = Primitives.Mix(youngBase, matureBase, Math.Pow(age, 0.65));
        var tipCol = Primitives.Mix(youngTip, matureTip, Math.Pow(age, 0.65));

        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        // Emit single-sheet cambered lamina (u2 = 0.5 marks lamina tissue in flora.gdshader)
        for (int r = 0; r <= rows; r++)
        {
            double t = (double)r / rows;
            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var pos = grid[r, c];
                var norm = gridNormals[r, c];
                var cGrad = Primitives.Mix(baseCol, tipCol, t);
                // Midrib vein subtle highlight
                if (Math.Abs(x) < 0.15)
                    cGrad = Primitives.Mix(cGrad, tipCol, 0.18 * (1.0 - Math.Abs(x) / 0.15));

                m.AddVertex(pos, norm, cGrad, 1.0, t, (x + 1.0) * 0.5, 0.5, 1.0);
            }
        }

        int rowStride = cols + 1;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a = leafVertex + r * rowStride + c;
                int b = a + rowStride;
                var n0 = gridNormals[r, c];
                Primitives.TriangleFacing(m, a, b, a + 1, n0.LengthSq > 1e-8 ? n0 : normal0);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, n0.LengthSq > 1e-8 ? n0 : normal0);
            }
        }

        m.RecordLeaf(leafVertex, leafIndex, attachPos, bladeLength);

        var rootPos = grid[0, cols / 2]; var tipPos = grid[rows, cols / 2];
        ulong partId = Rng.Mix(seed, Hash.Fnv1a64("form.broadleaf.blade"));
        var leafFrame = FloraAttachmentFrame.Create(junctionPos, normal0, fwd);
        var bMin = new Vec3(Math.Min(rootPos.X, tipPos.X) - bladeWidth * 0.5, Math.Min(rootPos.Y, tipPos.Y), Math.Min(rootPos.Z, tipPos.Z) - bladeWidth * 0.5);
        var bMax = new Vec3(Math.Max(rootPos.X, tipPos.X) + bladeWidth * 0.5, Math.Max(rootPos.Y, tipPos.Y), Math.Max(rootPos.Z, tipPos.Z) + bladeWidth * 0.5);
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            partId, parentPartId, FloraTissueSlot.Foliage, leafFrame, bladeLength, bladeWidth * 0.5, 0.65, 0, (bMin, bMax)));

        return m.TriangleCount - startTris;
    }

    private static Vec3 SplinePoint(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        double t1 = 1.0 - t;
        return p0 * (t1 * t1 * t1) + p1 * (3.0 * t1 * t1 * t) + p2 * (3.0 * t1 * t * t) + p3 * (t * t * t);
    }

    /// <summary>
    /// Builds a full broadleaf plant with a central stalk and 4-9 age-graded leaves arranged via phyllotaxis
    /// with realistic overlapping foliage at multiple heights.
    /// </summary>
    public static int Build(MeshData m, BroadleafParams p, ulong seed, double[] c1, double[] c2)
    {
        int startTris = m.TriangleCount;
        var rng = Rng.Keyed(seed, "form.broadleaf", 0);
        int detail = m.FloraDetailLevel ?? p.DetailLevel;

        // 1. Central woody stalk with root collar flare at base
        double stemH = p.StemHeight;
        double stemR = p.StemRadius;
        double swayX = rng.Range(-0.025, 0.025);
        double swayZ = rng.Range(-0.025, 0.025);

        var s0 = p.Origin;
        var s1 = p.Origin + new Vec3(swayX * 0.35, stemH * 0.33, swayZ * 0.35);
        var s2 = p.Origin + new Vec3(swayX * 0.70, stemH * 0.66, swayZ * 0.70);
        var s3 = p.Origin + new Vec3(swayX, stemH, swayZ);

        int stemSides = detail == 0 ? 6 : (detail == 1 ? 4 : 3);
        var stemPath = new[] { s0, s1, s2, s3 };
        var stemRadii = new[] { stemR * 1.45, stemR * 0.85, stemR * 0.65, stemR * 0.40 };
        var barkCol = Primitives.Mix(new[] { 0.24, 0.20, 0.16 }, c1, 0.30);
        Primitives.Tube(m, stemPath, stemRadii, stemSides, (i, v) => (barkCol, 1.0, i / 3.0, v, 0, 0));

        ulong stalkId = Rng.Mix(seed, Hash.Fnv1a64("form.broadleaf.stalk"));
        var stalkFrame = FloraAttachmentFrame.Create(p.Origin, Vec3.Up, new Vec3(1, 0, 0));
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            stalkId, 0, FloraTissueSlot.Wood, stalkFrame, stemH, stemR, 0.20, 0, (s0, s3)));

        // 2. Phyllotaxis leaf arrangement (4-9 leaves)
        int leafCount = Math.Clamp(p.LeafCount, 4, 9);
        double baseAzimuth = rng.Range(0.0, Math.PI * 2.0);

        for (int i = 0; i < leafCount; i++)
        {
            // i = 0 is oldest (bottom/outer), i = leafCount - 1 is youngest (top/inner)
            double u = (double)i / Math.Max(1, leafCount - 1);
            double age = 1.0 - u;

            // Attachment station along stalk
            double stemT = 0.20 + 0.68 * u;
            var attachPos = SplinePoint(s0, s1, s2, s3, stemT);

            double azimuth = baseAzimuth + i * p.PhyllotaxisAngle + rng.Range(-0.10, 0.10);
            double pitch = MathD.Lerp(p.MaxPitch, p.MinPitch, u) + rng.Range(-0.05, 0.05);

            var petioleDir = new Vec3(
                Math.Cos(azimuth) * Math.Sin(pitch),
                Math.Cos(pitch),
                Math.Sin(azimuth) * Math.Sin(pitch)).Normalized();

            double sizeCurve = Math.Pow(age, 0.75);
            double bladeLen = MathD.Lerp(p.MinLeafLength, p.MaxLeafLength, sizeCurve) * rng.Range(0.95, 1.05);
            double bladeWid = bladeLen * p.WidthToLength * rng.Range(0.95, 1.05);

            double pLen = p.PetioleLength * MathD.Lerp(0.55, 1.15, sizeCurve);
            double pRad = p.PetioleRadius * MathD.Lerp(0.70, 1.10, sizeCurve);
            double pKink = p.PetioleKink + age * 0.12;
            double curl = p.TipCurl * MathD.Lerp(0.6, 1.2, age);

            ulong leafSeed = Rng.Mix(seed, (ulong)(i * 733 + 17));

            BuildLeaf(m, attachPos, petioleDir, bladeLen, bladeWid, p.Outline, leafSeed, c1, c2,
                age: age, camber: p.Camber, midribFold: p.MidribFold, wavyMargin: p.WavyMargin,
                wavyFrequency: p.WavyFrequency, tipCurl: curl, petioleLength: pLen,
                petioleRadius: pRad, petioleKink: pKink, detailOverride: detail, parentPartId: stalkId);
        }

        return m.TriangleCount - startTris;
    }
}
