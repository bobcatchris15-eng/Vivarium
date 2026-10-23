using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

public readonly record struct PinnateFrondParams(
    int CrownCount = 8,                 // Multi-frond rosette crown count (e.g. 6-12)
    int PinnaePairs = 12,               // Number of paired or sub-opposite pinnae along rachis
    double FrondLength = 1.0,           // Mature frond reach/length
    double RachisRadius = 0.010,        // Base rachis thickness
    double ArchCurve = 1.15,            // Arching fountain curve intensity
    double Taper = 0.75,                // Pinnae taper power towards tip
    double PinnaLength = 0.22,          // Maximum pinna leaflet length
    double PinnaWidth = 0.040,          // Maximum pinna leaflet width
    double Camber = 0.12,               // Camber depth of pinnae
    double TiltAngle = 0.22,            // Tilt angle out of rachis plane
    double SubOpposite = 0.35,          // Sub-opposite offset (0 = opposite, 0.5 = alternate)
    double FiddleheadProgress = 0.0,    // 0 = mature open frond, 1 = tightly coiled crozier
    double TipCurl = 0.12,              // Mature tip droop / curl
    Vec3 Origin = default,              // Plant root anchor
    int DetailLevel = 0,                // Visual detail level (0 = full, 1 = mid, 2 = low)
    double StipeFlex = 0.85,            // Elastic flex of wiry stipe
    bool Compound = true,               // Compound pinnate frond with capillary petiolules
    double PetioluleLength = 0.005,     // Capillary petiolule attachment stalk length
    bool FineFountain = false          // Moonfern: layered, compact fountain; preserve other ferns
);

public static class PinnateFrond
{
    private static Vec3 BezierCubic(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        t = MathD.Clamp01(t);
        double u = 1.0 - t;
        return p0 * (u * u * u) + p1 * (3.0 * u * u * t) + p2 * (3.0 * u * t * t) + p3 * (t * t * t);
    }

    private static Vec3 BezierTangent(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 p3, double t)
    {
        t = MathD.Clamp01(t);
        double u = 1.0 - t;
        var d = (p1 - p0) * (3.0 * u * u) + (p2 - p1) * (6.0 * u * t) + (p3 - p2) * (3.0 * t * t);
        return d.LengthSq > 1e-8 ? d.Normalized() : Vec3.Up;
    }

    /// <summary>
    /// Builds a single pinna (leaflet) with camber, tilt, droop, and lanceolate taper.
    /// </summary>
    public static int BuildPinna(
        MeshData m,
        Vec3 attachPos,
        Vec3 outwardDir,
        Vec3 surfaceNormal,
        double length,
        double width,
        double camber,
        double droop,
        double[] colBase,
        double[] colTip,
        double[]? undersideColor = null,
        int? detailOverride = null,
        ulong seed = 1,
        ulong parentPartId = 0,
        double petioluleLength = 0.004,
        double flex = 0.65,
        bool fineFountain = false)
    {
        int startTris = m.TriangleCount;
        if (length <= 1e-6 || width <= 1e-6) return 0;

        int detail = detailOverride ?? m.FloraDetailLevel ?? 0;
        var dir = outwardDir.LengthSq > 1e-6 ? outwardDir.Normalized() : new Vec3(1, 0, 0);
        var norm0 = surfaceNormal.LengthSq > 1e-6 ? surfaceNormal.Normalized() : Vec3.Up;
        var side = norm0.Cross(dir).Normalized();
        if (side.LengthSq < 1e-6) side = dir.Cross(Vec3.Up).Normalized();

        Vec3 bladeAttach = attachPos;
        if (petioluleLength > 1e-5)
        {
            bladeAttach = attachPos + dir * petioluleLength;
            var stalkCol = Primitives.Mix(new[] { 0.12, 0.09, 0.08 }, colBase, 0.25);
            Primitives.Tube(m, new[] { attachPos, bladeAttach }, new[] { 0.0018, 0.0012 }, 4, (i, v) => (stalkCol, 1.0, i, v, 0, 0));
        }

        const double lobeCount = 7.0;
        const double laminaThickness = 0.00025; // 0.25 mm physical thickness

        Vec3 SurfacePos(double mu, double x, int face)
        {
            mu = MathD.Clamp01(mu);
            x = Math.Clamp(x, -1.0, 1.0);
            double absX = Math.Abs(x);

            // Pinnatifid marginal lobes
            double lobeCycle = Math.Sin(mu * lobeCount * Math.PI * 2.0);
            double lobeIncise = 1.0 + 0.26 * lobeCycle * Math.Pow(absX, 2.5);

            double wEnv = width * Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(mu, 0.45))), 0.60) * (1.0 - 0.35 * mu) * lobeIncise;
            var lateral = side * (x * wEnv);
            double camberZ = camber * width * (1.0 - x * x) * Math.Sin(Math.PI * Math.Pow(mu, 0.50));
            double droopY = droop * (mu * mu);

            // Sori spore bumps on abaxial underside (face == 1)
            double sorusMuPhase = mu * lobeCount * Math.PI * 2.0;
            double sorusX = absX - 0.52;
            double sorusDistSq = (Math.Cos(sorusMuPhase) * 0.08) * (Math.Cos(sorusMuPhase) * 0.08) + sorusX * sorusX;
            double sorusBump = (face == 1 && mu > 0.15 && mu < 0.88) ? 0.00065 * Math.Exp(-sorusDistSq / 0.012) : 0.0;

            double normalOffset = face == 0 ? 0.5 * laminaThickness : (-0.5 * laminaThickness - sorusBump);
            return bladeAttach + dir * (mu * length) + lateral + norm0 * (camberZ + normalOffset) - Vec3.Up * droopY;
        }

        int rows = detail == 0 ? 14 : (detail == 1 ? 10 : 6);
        if (m.Lod is { } lod) rows = lod.Segments(rows, 4);
        int cols = detail == 0 ? 8 : (detail == 1 ? 6 : 4);
        if (m.Lod is { } lodC) cols = Math.Max(3, (int)Math.Round(cols * lodC.Blade));

        int midRows = rows - 1;
        var grid = new Vec3[2, midRows, cols + 1];
        var gridNormals = new Vec3[2, midRows, cols + 1];
        var rootPositions = new Vec3[2];
        var tipPositions = new Vec3[2];
        var rootNormals = new Vec3[2];
        var tipNormals = new Vec3[2];

        for (int face = 0; face < 2; face++)
        {
            double faceSign = face == 0 ? 1.0 : -1.0;
            var expectedNormal = norm0 * faceSign;

            rootPositions[face] = SurfacePos(0.0, 0.0, face);
            tipPositions[face] = SurfacePos(1.0, 0.0, face);

            for (int r = 1; r < rows; r++)
            {
                double mu = (double)r / rows;
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    grid[face, r - 1, c] = SurfacePos(mu, x, face);
                }
            }

            for (int ri = 0; ri < midRows; ri++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    Vec3 tMu = midRows > 1
                        ? (ri == 0 ? grid[face, 1, c] - rootPositions[face] : (ri == midRows - 1 ? tipPositions[face] - grid[face, ri - 1, c] : grid[face, ri + 1, c] - grid[face, ri - 1, c]))
                        : (tipPositions[face] - rootPositions[face]);

                    Vec3 tX = cols > 1
                        ? (c == 0 ? grid[face, ri, 1] - grid[face, ri, 0] : (c == cols ? grid[face, ri, cols] - grid[face, ri, cols - 1] : grid[face, ri, c + 1] - grid[face, ri, c - 1]))
                        : side;

                    if (tX.LengthSq < 1e-8) tX = side;
                    if (tMu.LengthSq < 1e-8) tMu = dir;

                    var n = (face == 0 ? tX.Cross(tMu) : tMu.Cross(tX)).Normalized();
                    if (n.LengthSq < 1e-6 || n.Dot(expectedNormal) < 0) n = expectedNormal;
                    gridNormals[face, ri, c] = n;
                }
            }

            var rootTMu = midRows > 0 ? grid[face, 0, cols / 2] - rootPositions[face] : tipPositions[face] - rootPositions[face];
            if (rootTMu.LengthSq < 1e-8) rootTMu = dir;
            var rN = (face == 0 ? side.Cross(rootTMu) : rootTMu.Cross(side)).Normalized();
            if (rN.LengthSq < 1e-6 || rN.Dot(expectedNormal) < 0) rN = expectedNormal;
            rootNormals[face] = rN;

            var tipTMu = midRows > 0 ? tipPositions[face] - grid[face, midRows - 1, cols / 2] : tipPositions[face] - rootPositions[face];
            if (tipTMu.LengthSq < 1e-8) tipTMu = dir;
            var tN = (face == 0 ? side.Cross(tipTMu) : tipTMu.Cross(side)).Normalized();
            if (tN.LengthSq < 1e-6 || tN.Dot(expectedNormal) < 0) tN = expectedNormal;
            tipNormals[face] = tN;
        }

        var underCol = undersideColor ?? Primitives.Mix(colBase, new[] { 0.35, 0.50, 0.28 }, 0.40);
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        int[] rootIndices = new int[2];
        int[] tipIndices = new int[2];
        int[,,] gridIndices = new int[2, midRows, cols + 1];

        // Emit solid 2-sided 3D shell (faceCount = 2 + stitched margin rim strip)
        for (int face = 0; face < 2; face++)
        {
            double u2 = face; // 0 = adaxial, 1 = abaxial
            var rootColor = face == 0 ? colBase : underCol;
            rootIndices[face] = m.AddVertex(rootPositions[face], rootNormals[face], rootColor, 1.0, 0.0, 0.5, u2, 1.0);

            for (int r = 1; r < rows; r++)
            {
                double mu = (double)r / rows;
                var gradCol = Primitives.Mix(colBase, colTip, mu);
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    var pos = grid[face, r - 1, c];
                    var norm = gridNormals[face, r - 1, c];
                    var vertCol = face == 0 ? gradCol : underCol;

                    // Tint sori spore bumps on abaxial underside with amber-brown dust
                    if (face == 1 && mu > 0.15 && mu < 0.88)
                    {
                        double sX = Math.Abs(x) - 0.52;
                        double sPhase = mu * lobeCount * Math.PI * 2.0;
                        double sDistSq = (Math.Cos(sPhase) * 0.08) * (Math.Cos(sPhase) * 0.08) + sX * sX;
                        double sBump = 0.00065 * Math.Exp(-sDistSq / 0.012);
                        if (sBump > 0.00018)
                            vertCol = Primitives.Mix(vertCol, new[] { 0.45, 0.28, 0.12 }, sBump / 0.00065);
                    }

                    gridIndices[face, r - 1, c] = m.AddVertex(pos, norm, vertCol, 1.0, mu, (x + 1.0) * 0.5, u2, 1.0);
                }
            }

            var tipColor = face == 0 ? colTip : underCol;
            tipIndices[face] = m.AddVertex(tipPositions[face], tipNormals[face], tipColor, 1.0, 1.0, 0.5, u2, 1.0);

            // Triangulation: root fan
            for (int c = 0; c < cols; c++)
            {
                int va = gridIndices[face, 0, c];
                int vb = gridIndices[face, 0, c + 1];
                Primitives.TriangleFacing(m, rootIndices[face], va, vb, m.NormalAt(rootIndices[face]));
            }

            // Triangulation: middle quad strips
            for (int r = 0; r < rows - 2; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int a = gridIndices[face, r, c];
                    int b = gridIndices[face, r, c + 1];
                    int d = gridIndices[face, r + 1, c];
                    int e = gridIndices[face, r + 1, c + 1];
                    Primitives.TriangleFacing(m, a, d, b, m.NormalAt(a));
                    Primitives.TriangleFacing(m, b, d, e, m.NormalAt(b));
                }
            }

            // Triangulation: tip fan
            for (int c = 0; c < cols; c++)
            {
                int va = gridIndices[face, rows - 2, c];
                int vb = gridIndices[face, rows - 2, c + 1];
                Primitives.TriangleFacing(m, va, tipIndices[face], vb, m.NormalAt(va));
            }
        }

        // Stitched boundary rim connecting upper and lower margin vertices
        // Left margin rim (c = 0, x = -1.0)
        Primitives.TriangleFacing(m, rootIndices[0], rootIndices[1], gridIndices[0, 0, 0], -side);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[1, 0, 0], gridIndices[0, 0, 0], -side);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, 0], gridIndices[1, r, 0], gridIndices[0, r + 1, 0], -side);
            Primitives.TriangleFacing(m, gridIndices[1, r, 0], gridIndices[1, r + 1, 0], gridIndices[0, r + 1, 0], -side);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, 0], gridIndices[1, rows - 2, 0], tipIndices[0], -side);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, 0], tipIndices[1], tipIndices[0], -side);

        // Right margin rim (c = cols, x = +1.0)
        Primitives.TriangleFacing(m, rootIndices[0], gridIndices[0, 0, cols], rootIndices[1], side);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[0, 0, cols], gridIndices[1, 0, cols], side);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r, cols], side);
            Primitives.TriangleFacing(m, gridIndices[1, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r + 1, cols], side);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, cols], tipIndices[0], gridIndices[1, rows - 2, cols], side);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, cols], tipIndices[0], tipIndices[1], side);

        m.RecordLeaf(leafVertex, leafIndex, bladeAttach, length);

        ulong partId = Rng.Mix(seed, Hash.Fnv1a64("form.pinnate.pinna"));
        var pinnaFrame = FloraAttachmentFrame.Create(attachPos, norm0, dir);
        var bMin = new Vec3(Math.Min(attachPos.X, bladeAttach.X + dir.X * length) - width * 0.5,
                            Math.Min(attachPos.Y, bladeAttach.Y + dir.Y * length - droop),
                            Math.Min(attachPos.Z, bladeAttach.Z + dir.Z * length) - width * 0.5);
        var bMax = new Vec3(Math.Max(attachPos.X, bladeAttach.X + dir.X * length) + width * 0.5,
                            Math.Max(attachPos.Y, bladeAttach.Y + dir.Y * length + camber * width),
                            Math.Max(attachPos.Z, bladeAttach.Z + dir.Z * length) + width * 0.5);
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            partId, parentPartId, FloraTissueSlot.Foliage, pinnaFrame, length, width * 0.5, flex, 0, (bMin, bMax)));

        return m.TriangleCount - startTris;
    }
    /// <summary>
    /// Builds a single pinnate frond with curved rachis spline, circinate vernation (fiddlehead spiral tip),
    /// and paired/sub-opposite pinnae.
    /// </summary>
    public static int BuildFrond(
        MeshData m,
        Vec3 attachPos,
        double azimuth,
        double pitch,
        double length,
        double fiddleheadProgress,
        PinnateFrondParams p,
        ulong seed,
        double[] c1,
        double[] c2,
        double age = 1.0,
        int? detailOverride = null,
        ulong parentPartId = 0)
    {
        int startTris = m.TriangleCount;
        int detail = detailOverride ?? m.FloraDetailLevel ?? 0;

        double F = Math.Clamp(fiddleheadProgress, 0.0, 1.0);
        double unroll = 1.0 - F;

        var H = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth)).Normalized();
        var U = Vec3.Up;

        // Spline control points:
        // Ensure base begins strictly vertical to keep ground vertices >= 0.0
        var P0 = attachPos;
        Vec3 P1, P2, P3;

        bool isMature = F < 0.05;
        double unrollLen = isMature ? length : length * (0.35 + 0.65 * unroll);
        double effPitch = isMature ? pitch : MathD.Lerp(0.08, pitch, unroll * unroll);

        var T0 = (H * Math.Sin(effPitch) + U * Math.Cos(effPitch)).Normalized();

        if (isMature)
        {
            P1 = P0 + U * (length * 0.72) + H * (length * 0.12 * Math.Sin(pitch));
            P2 = P0 + U * (length * 1.05) + H * (length * 0.55 * p.ArchCurve);
            double apexDrop = p.FineFountain ? 0.33 + 0.06 * (1 - p.ArchCurve)
                : 0.35 - 0.15 * p.ArchCurve;
            P3 = P0 + U * (length * Math.Max(0.16, apexDrop)) + H * (length * 0.94 * p.ArchCurve);
        }
        else
        {
            P1 = P0 + U * (length * 0.40) + H * (unrollLen * 0.05 * Math.Sin(effPitch));
            P2 = P0 + U * (length * 0.75) + H * (unrollLen * 0.12 * Math.Sin(effPitch));
            P3 = P0 + U * (length * (0.65 + 0.30 * unroll)) + H * (unrollLen * 0.20 * Math.Sin(effPitch));
        }

        // Basal chaffy scales (ramenta) at stipe base
        var scaleCol = new[] { 0.52, 0.35, 0.18 };
        for (int sc = 0; sc < 6; sc++)
        {
            double scAng = azimuth + (sc - 2.5) * 0.45;
            var scDir = new Vec3(Math.Cos(scAng), 0.32, Math.Sin(scAng)).Normalized();
            Primitives.Tube(m, new[] { P0, P0 + scDir * (length * 0.035) }, new[] { 0.004, 0.001 }, 4, (st, u) => (scaleCol, 1.0, st, u, 0, 0));
        }

        // 1. Rachis Path Construction
        var path = new List<Vec3>();
        var radii = new List<double>();

        int stemSteps = isMature ? (detail == 0 ? 12 : 9) : (detail == 0 ? 8 : 6);
        for (int i = 0; i <= stemSteps; i++)
        {
            double t = (double)i / stemSteps;
            var pt = BezierCubic(P0, P1, P2, P3, t);
            if (i == 0) pt = new Vec3(pt.X, Math.Max(0.0, pt.Y), pt.Z);
            path.Add(pt);

            double rTaper = isMature
                ? MathD.Lerp(p.RachisRadius, p.RachisRadius * 0.20, t)
                : MathD.Lerp(p.RachisRadius * (0.80 + 0.20 * unroll), p.RachisRadius * 0.50, t);
            radii.Add(rTaper);
        }

        // Circinate vernation: coiled fiddlehead spiral tip for young/emergent fronds
        if (!isMature)
        {
            var T_stem = BezierTangent(P0, P1, P2, P3, 1.0);
            var inward = -H;
            var N_curl = (inward - T_stem * inward.Dot(T_stem)).Normalized();
            if (N_curl.LengthSq < 1e-6) N_curl = -H;

            double thetaMax = Math.PI * (1.5 + 2.5 * F);
            double r0 = 0.038 * (1.0 - 0.30 * unroll) * (length / 0.88);
            double kCoil = 0.22;
            var C = P3 + N_curl * r0;

            int coilSteps = detail == 0 ? 16 : (detail == 1 ? 12 : 8);
            for (int j = 1; j <= coilSteps; j++)
            {
                double theta = thetaMax * (double)j / coilSteps;
                double rTheta = r0 * Math.Exp(-kCoil * theta) * (1.0 - 0.18 * theta / thetaMax);
                var coilPt = C - N_curl * (rTheta * Math.Cos(theta)) + T_stem * (rTheta * Math.Sin(theta));
                path.Add(coilPt);

                double rEnd = p.RachisRadius * MathD.Lerp(0.50, 0.16, (double)j / coilSteps);
                radii.Add(rEnd);
            }
        }

        int tubeSides = detail == 0 ? 10 : (detail == 1 ? 8 : 6);
        var stalkCol = Primitives.Mix(new[] { 0.12, 0.09, 0.08 }, c1, 0.30 + 0.25 * age);
        Primitives.Tube(m, path, radii, tubeSides, (i, v) => (stalkCol, 1.0, (double)i / (path.Count - 1), v, 0, 0));

        ulong frondPartId = Rng.Mix(seed, Hash.Fnv1a64("form.pinnate.frond"));
        var frondFrame = FloraAttachmentFrame.Create(attachPos, T0, H);
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            frondPartId, parentPartId, FloraTissueSlot.Stem, frondFrame, length, p.RachisRadius, p.StipeFlex, 0, (P0, P3)));

        // 2. Pinnae Leaflets along the Rachis
        double maxT = isMature ? 0.995 : Math.Min(0.96, unroll * 1.05);
        if (maxT > 0.18)
        {
            int pairs = p.PinnaePairs;
            double tStep = (0.96 - 0.18) / Math.Max(1, pairs - 1);

            for (int i = 0; i < pairs; i++)
            {
                double baseT = 0.18 + i * tStep;
                // Keep the proximal half intact; spend existing distal pinnae where
                // the accelerating Bezier arc otherwise leaves a naked terminal whip.
                if (isMature && i > (pairs - 1) * 0.5)
                {
                    double distal = (2.0 * i / Math.Max(1, pairs - 1)) - 1.0;
                    baseT = 0.57 + 0.405 * (1.0 - Math.Pow(1.0 - distal, 1.45));
                }
                if (baseT > maxT) break;

                foreach (double sideSgn in new[] { -1.0, 1.0 })
                {
                    double t = sideSgn < 0 ? baseT : baseT + p.SubOpposite * tStep * 0.5;
                    if (t > maxT) continue;

                    var rachisP = BezierCubic(P0, P1, P2, P3, t);
                    var rachisT = BezierTangent(P0, P1, P2, P3, t);

                    var rachisN = (U - rachisT * U.Dot(rachisT)).Normalized();
                    if (rachisN.LengthSq < 1e-6) rachisN = (H - rachisT * H.Dot(rachisT)).Normalized();
                    var rachisS = rachisT.Cross(rachisN).Normalized();

                    double tau = (t - 0.18) / (0.96 - 0.18);
                    double env = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(tau, p.Taper))), 0.70) * (1.0 - 0.28 * tau);

                    // A mature frond tapers to its actual apex (t=1), not to the
                    // last lateral station. The old endpoint-zero envelope discarded
                    // the last pair while retaining a separate terminal leaflet.
                    if (isMature && t > 0.65)
                    {
                        double shoulderTau = (0.65 - 0.18) / (0.96 - 0.18);
                        double shoulderEnv = Math.Pow(Math.Sin(Math.PI * Math.Pow(shoulderTau, p.Taper)), 0.70)
                            * (1.0 - 0.28 * shoulderTau);
                        // Boston-style feathering continues nearly to the apex.
                        // Other ferns retain their original stronger distal taper.
                        env = shoulderEnv * Math.Pow(Math.Max(0.0, (1.0 - t) / 0.35),
                            p.FineFountain ? 0.51 : 0.85);
                    }

                    double unrollExpansion = isMature ? 1.0 : Math.Min(1.0, Math.Max(0.25, (maxT - t) / 0.12));
                    double pinnaL = p.PinnaLength * env * unrollExpansion * (length / 0.88);
                    double pinnaW = p.PinnaWidth * env * unrollExpansion;

                    if (pinnaL < 0.012) continue;

                    double rAtT = MathD.Lerp(radii[0], radii[stemSteps], t);
                    var attach = rachisP + rachisS * (sideSgn * rAtT);

                    var dir = (rachisS * (sideSgn * 0.86) + rachisT * 0.38 + rachisN * (p.TiltAngle * sideSgn)).Normalized();
                    var norm = dir.Cross(rachisT).Normalized();
                    if (norm.Dot(rachisN) < 0) norm = -norm;

                    var pinnaBase = Primitives.Mix(c1, c2, Math.Min(1.0, 0.20 + 0.65 * tau));
                    var pinnaTip = Primitives.Mix(c2, new[] { 0.72, 0.88, 0.38 }, 0.40);
                    if (age < 0.7) pinnaBase = Primitives.Mix(pinnaBase, c2, 0.35);

                    ulong pSeed = Rng.Mix(frondPartId, (ulong)(i * 97 + (sideSgn > 0 ? 1 : 2)));
                    BuildPinna(m, attach, dir, norm, pinnaL, pinnaW, p.Camber, 0.05 * pinnaL,
                        pinnaBase, pinnaTip, detailOverride: detail, seed: pSeed, parentPartId: frondPartId,
                        petioluleLength: p.PetioluleLength, flex: 0.65, fineFountain:p.FineFountain);
                }
            }

            // Terminal pinna at the tip of mature fronds
            if (isMature)
            {
                var termP = BezierCubic(P0, P1, P2, P3, 1.0);
                var termT = BezierTangent(P0, P1, P2, P3, 1.0);
                var termN = (U - termT * U.Dot(termT)).Normalized();
                var termBase = Primitives.Mix(c1, c2, 0.80);
                var termTip = Primitives.Mix(c2, new[] { 0.72, 0.88, 0.38 }, 0.45);
                ulong termSeed = Rng.Mix(frondPartId, 9999);
                BuildPinna(m, termP, termT, termN, p.PinnaLength * 0.32, p.PinnaWidth * 0.32,
                    p.Camber, 0.02, termBase, termTip, detailOverride: detail, seed: termSeed,
                    parentPartId: frondPartId, petioluleLength: p.PetioluleLength * 0.5,
                    flex: 0.65, fineFountain:p.FineFountain);
            }
        }

        return m.TriangleCount - startTris;
    }

    /// <summary>
    /// Builds a multi-frond rosette crown holding fronds at varied developmental stages:
    /// young upright unrolling fiddleheads in the center, and mature arching fronds radiating outwards.
    /// </summary>
    public static int Build(MeshData m, PinnateFrondParams p, ulong seed, double[] c1, double[] c2)
    {
        int startTris = m.TriangleCount;
        var rng = Rng.Keyed(seed, "form.pinnate_frond", 0);
        int detail = m.FloraDetailLevel ?? p.DetailLevel;

        int count = Math.Clamp(p.CrownCount, 1, p.FineFountain ? 20 : 16);

        ulong caudexId = Rng.Mix(seed, Hash.Fnv1a64("form.pinnate.caudex"));
        var caudexFrame = FloraAttachmentFrame.Create(p.Origin, Vec3.Up, new Vec3(1, 0, 0));
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            caudexId, 0, FloraTissueSlot.Wood, caudexFrame, 0.025, 0.028, 0.10, 0,
            (p.Origin - new Vec3(0.028, 0, 0.028), p.Origin + new Vec3(0.028, 0.025, 0.028))));

        if (count == 1)
        {
            // Single frond requested: respects FiddleheadProgress directly
            double az = rng.Range(0, 2 * Math.PI);
            double pitch = p.FiddleheadProgress > 0.5 ? 0.12 : 0.48;
            BuildFrond(m, p.Origin, az, pitch, p.FrondLength, p.FiddleheadProgress, p, seed, c1, c2,
                detailOverride: detail, parentPartId: caudexId);
            return m.TriangleCount - startTris;
        }

        // Staged rosette crown:
        // Center: 1-3 young upright fiddleheads at varied unrolling stages
        // Outer: radiating mature fronds in a full fountain arch
        int youngCount = Math.Max(1, Math.Min(3, count / 4));
        int matureCount = count - youngCount;

        double baseAzimuth = rng.Range(0, 2 * Math.PI);

        // 1. Mature outer radiating fronds
        for (int k = 0; k < matureCount; k++)
        {
            double azimuth = baseAzimuth + 2.0 * Math.PI * k / matureCount + rng.Range(-0.16, 0.16);
            double rad = rng.Range(0.022, 0.040);
            var attach = p.Origin + new Vec3(Math.Cos(azimuth) * rad, 0, Math.Sin(azimuth) * rad);
            // Stagger the fine Moonfern canopy into overlapping maturity shelves
            // rather than placing all its additional fronds in a single radial ring.
            double shelf = p.FineFountain ? (k % 4) / 3.0 : 0.0;
            double pitch = p.FineFountain ? rng.Range(0.39, 0.57) + shelf * 0.07
                : rng.Range(0.42, 0.62);
            double len = p.FrondLength * (p.FineFountain
                ? rng.Range(0.86, 1.03) * (1.0 - shelf * 0.16)
                : rng.Range(0.92, 1.08));

            double fProg = p.FiddleheadProgress > 0.0 ? p.FiddleheadProgress : 0.0;
            ulong frondSeed = Rng.Mix(seed, (ulong)(k * 619 + 7));

            BuildFrond(m, attach, azimuth, pitch, len, fProg, p, frondSeed, c1, c2, age: 1.0,
                detailOverride: detail, parentPartId: caudexId);
        }

        // 2. Emerging young fiddleheads in the center
        for (int k = 0; k < youngCount; k++)
        {
            double azimuth = baseAzimuth + rng.Range(0, 2 * Math.PI);
            double rad = rng.Range(0.008, 0.018);
            var attach = p.Origin + new Vec3(Math.Cos(azimuth) * rad, 0, Math.Sin(azimuth) * rad);
            double pitch = rng.Range(0.06, 0.18);
            double len = p.FrondLength * rng.Range(0.68, 0.86);

            // Stagger fiddlehead stages: tightest crozier first, partially unrolling second
            double stagedF = youngCount == 1 ? 0.85 : (k == 0 ? 0.92 : 0.60);
            double fProg = p.FiddleheadProgress > 0.0 ? Math.Max(stagedF, p.FiddleheadProgress) : stagedF;

            ulong frondSeed = Rng.Mix(seed, (ulong)((matureCount + k) * 619 + 7));
            BuildFrond(m, attach, azimuth, pitch, len, fProg, p, frondSeed, c1, c2, age: 0.40,
                detailOverride: detail, parentPartId: caudexId);
        }

        // 3. Basal rhizome / caudex
        var caudexCol = Primitives.Mix(new[] { 0.18, 0.14, 0.10 }, c1, 0.22);
        Primitives.Ellipsoid(m, p.Origin + new Vec3(0, 0.012, 0), new Vec3(0.028, 0.012, 0.028), 4, 6,
            (u, v) => (caudexCol, 1.0, u, v, 0, 0));

        return m.TriangleCount - startTris;
    }
}
