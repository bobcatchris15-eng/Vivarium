using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

public enum RosetteVariegation
{
    None,
    MarginStripe,
    CenterStripe,
    Banded
}

public readonly record struct EpiphyticRosetteParams(
    int LeafCount = 18,                  // 12-28 spiraling leaves
    double FlareRadius = 0.35,           // Outer crown flare radius
    double LeafCurvature = 0.85,         // Arching & downward drape intensity
    double Drape = 0.85,                 // Gravitational downward drape of outer leaves
    double InnerCupDepth = 0.12,         // Depth of central water-holding cup/cavity
    double CrownHeight = 0.28,           // Height of central vase neck/crown
    double LeafWidth = 0.045,            // Maximum blade width
    double Camber = 0.12,                // Transverse camber
    double MidribFold = 0.10,            // Canaliculate U-shaped channel depth
    double TipCurl = 0.10,               // Downward tip curl
    double PhyllotaxisAngle = 2.3999632, // Golden angle ~137.5 degrees
    RosetteVariegation Variegation = RosetteVariegation.MarginStripe,
    double VariegationStrength = 0.35,   // Variegation intensity (0 = solid)
    double[]? VariegationColor = null,   // Custom variegation stripe/band tint
    Vec3 Origin = default,               // Basal attachment anchor
    int DetailLevel = 0                  // Visual detail level (0 = full, 1 = mid, 2 = low)
);

public static class EpiphyticRosette
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
    /// Builds a single spiraling rosette blade with canaliculate U-cross-section, camber,
    /// variegation striping, and realistic curvature into <paramref name="m"/>.
    /// </summary>
    public static int BuildLeaf(
        MeshData m,
        Vec3 p0,
        Vec3 p1,
        Vec3 p2,
        Vec3 p3,
        Vec3 radialDir,
        double bladeWidth,
        double camber,
        double midribFold,
        double tipCurl,
        ulong seed,
        double[] colSheath,
        double[] colBlade,
        double[] colTip,
        double rankU,
        RosetteVariegation variegation,
        double variegationStrength,
        double[]? variegationColor = null,
        double[]? undersideColor = null,
        int? detailOverride = null,
        ulong parentPartId = 0)
    {
        int startTris = m.TriangleCount;
        double bladeLength = (p3 - p0).Length;
        if (bladeLength <= 1e-6 || bladeWidth <= 1e-6) return 0;

        int detail = detailOverride ?? m.FloraDetailLevel ?? 0;
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        // Smooth outline envelope from clasping sheath to acute attenuating tip
        static double Taper(double t)
        {
            t = MathD.Clamp01(t);
            return Math.Sin(Math.PI * Math.Pow(t, 0.38)) * Math.Pow(1.0 - t, 0.58) * 1.35;
        }

        // Surface point evaluator with 3D lamina extrusion, keeled V-section, and sheathed culm base
        Vec3 SurfacePos(double t, double x, int face)
        {
            t = MathD.Clamp01(t);
            x = Math.Clamp(x, -1.0, 1.0);

            var spineP = BezierCubic(p0, p1, p2, p3, t);
            var tan = BezierTangent(p0, p1, p2, p3, t);

            var side = tan.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = tan.Cross(radialDir).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            // Sheathed culm base clasping around the rosette core
            if (t < 0.20)
            {
                double sw = 1.0 - t / 0.20;
                side = Axis.RotateAround(side, tan, sw * 0.35 * x);
            }

            var norm0 = side.Cross(tan).Normalized();
            if (norm0.Dot(Vec3.Up) < -0.2 && norm0.Dot(radialDir) < 0) norm0 = -norm0;

            double hw = (bladeWidth * 0.5) * Taper(t);
            var lateral = side * (x * hw);

            // Canaliculate U-gutter on adaxial face, prominent keel on abaxial face
            double absX = Math.Abs(x);
            double uFold = -midribFold * (bladeWidth * 0.5) * (1.0 - x * x) * Math.Sin(Math.PI * Math.Pow(t, 0.45));
            double zCamber = camber * (bladeWidth * 0.5) * (1.0 - absX) * Math.Sin(Math.PI * Math.Pow(t, 0.50));
            double droopY = tipCurl * (t * t * t) * (bladeWidth * 0.4);

            double keel = -midribFold * 1.5 * (bladeWidth * 0.5) * (1.0 - absX) * Math.Sin(Math.PI * Math.Pow(t, 0.40));
            double normalOffset = face == 0 ? (0.00022 + uFold) : (-0.00022 + keel);

            return spineP + lateral + norm0 * (zCamber + normalOffset) - Vec3.Up * droopY;
        }

        // Analytical / finite-difference surface normal
        Vec3 SurfaceNormal(double t, double x, int face = 0)
        {
            t = MathD.Clamp01(t);
            x = Math.Clamp(x, -1.0, 1.0);

            var tan = BezierTangent(p0, p1, p2, p3, t);
            var side = tan.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = tan.Cross(radialDir).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            var norm0 = side.Cross(tan).Normalized();
            if (norm0.Dot(Vec3.Up) < -0.2 && norm0.Dot(radialDir) < 0) norm0 = -norm0;

            const double dt = 0.002, dx = 0.004;
            var tT = SurfacePos(Math.Min(1.0, t + dt), x, face) - SurfacePos(Math.Max(0.0, t - dt), x, face);
            var tX = SurfacePos(t, Math.Min(1.0, x + dx), face) - SurfacePos(t, Math.Max(-1.0, x - dx), face);
            var n = (face == 0 ? tX.Cross(tT) : tT.Cross(tX)).Normalized();
            var expected = face == 0 ? norm0 : -norm0;
            if (n.LengthSq < 1e-6 || n.Dot(expected) < 0) n = expected;
            return n;
        }

        // Color evaluator along blade and margin variegation
        var varCol = variegationColor ?? Primitives.Mix(colTip, new[] { 0.94, 0.95, 0.78 }, 0.65);
        double[] BladeColor(double t, double x)
        {
            var cGrad = t < 0.18
                ? Primitives.Mix(colSheath, colBlade, t / 0.18)
                : Primitives.Mix(colBlade, colTip, (t - 0.18) / 0.82);

            if (variegationStrength > 1e-4)
            {
                double vFact = 0.0;
                switch (variegation)
                {
                    case RosetteVariegation.MarginStripe:
                        vFact = MathD.Clamp01((Math.Abs(x) - 0.58) / 0.36);
                        break;
                    case RosetteVariegation.CenterStripe:
                        vFact = MathD.Clamp01((0.32 - Math.Abs(x)) / 0.32);
                        break;
                    case RosetteVariegation.Banded:
                        vFact = Math.Sin(t * 26.0) > 0.15 ? 1.0 : 0.0;
                        break;
                }
                if (vFact > 0)
                    cGrad = Primitives.Mix(cGrad, varCol, vFact * variegationStrength);
            }

            return cGrad;
        }

        int rows = detail == 0 ? 18 : (detail == 1 ? 12 : 8);
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
            rootPositions[face] = SurfacePos(0.0, 0.0, face);
            rootNormals[face] = SurfaceNormal(0.0, 0.0, face);
            tipPositions[face] = SurfacePos(1.0, 0.0, face);
            tipNormals[face] = SurfaceNormal(1.0, 0.0, face);

            for (int r = 1; r < rows; r++)
            {
                double t = (double)r / rows;
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    grid[face, r - 1, c] = SurfacePos(t, x, face);
                    gridNormals[face, r - 1, c] = SurfaceNormal(t, x, face);
                }
            }
        }

        var underCol = undersideColor ?? Primitives.Mix(colBlade, new[] { 0.35, 0.45, 0.30 }, 0.40);
        int[] rootIndices = new int[2];
        int[] tipIndices = new int[2];
        int[,,] gridIndices = new int[2, midRows, cols + 1];

        // Emit solid 2-sided 3D shell (faceCount = 2 + stitched margin rim strip)
        for (int face = 0; face < 2; face++)
        {
            double u2 = face; // 0 = adaxial, 1 = abaxial
            var rootColor = face == 0 ? colSheath : underCol;
            rootIndices[face] = m.AddVertex(rootPositions[face], rootNormals[face], rootColor, 1.0, 0.0, 0.5, u2, 1.0);

            for (int r = 1; r < rows; r++)
            {
                double t = (double)r / rows;
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    var pos = grid[face, r - 1, c];
                    var norm = gridNormals[face, r - 1, c];
                    var cGrad = BladeColor(t, x);
                    var vertCol = face == 0 ? cGrad : underCol;

                    gridIndices[face, r - 1, c] = m.AddVertex(pos, norm, vertCol, 1.0, t, (x + 1.0) * 0.5, u2, 1.0);
                }
            }

            var tipColor = face == 0 ? BladeColor(1.0, 0.0) : underCol;
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

        var sampleTan = (p1 - p0).Normalized();
        var sampleSide = sampleTan.Cross(Vec3.Up).Normalized();
        if (sampleSide.LengthSq < 1e-6) sampleSide = sampleTan.Cross(radialDir).Normalized();
        if (sampleSide.LengthSq < 1e-6) sampleSide = new Vec3(1, 0, 0);

        // Stitched boundary rim connecting upper and lower margin vertices
        // Left margin rim (c = 0, x = -1.0)
        Primitives.TriangleFacing(m, rootIndices[0], rootIndices[1], gridIndices[0, 0, 0], -sampleSide);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[1, 0, 0], gridIndices[0, 0, 0], -sampleSide);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, 0], gridIndices[1, r, 0], gridIndices[0, r + 1, 0], -sampleSide);
            Primitives.TriangleFacing(m, gridIndices[1, r, 0], gridIndices[1, r + 1, 0], gridIndices[0, r + 1, 0], -sampleSide);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, 0], gridIndices[1, rows - 2, 0], tipIndices[0], -sampleSide);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, 0], tipIndices[1], tipIndices[0], -sampleSide);

        // Right margin rim (c = cols, x = +1.0)
        Primitives.TriangleFacing(m, rootIndices[0], gridIndices[0, 0, cols], rootIndices[1], sampleSide);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[0, 0, cols], gridIndices[1, 0, cols], sampleSide);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r, cols], sampleSide);
            Primitives.TriangleFacing(m, gridIndices[1, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r + 1, cols], sampleSide);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, cols], tipIndices[0], gridIndices[1, rows - 2, cols], sampleSide);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, cols], tipIndices[0], tipIndices[1], sampleSide);

                m.RecordLeaf(leafVertex, leafIndex, p0, bladeLength);

        ulong partId = Rng.Mix(seed, (ulong)(rankU * 10000 + 1));
        var surfNorm = SurfaceNormal(0.0, 0.0);
        var surfTan = (p1 - p0).Normalized();
        var leafFrame = FloraAttachmentFrame.Create(p0, surfNorm, surfTan);
        var bMin = new Vec3(Math.Min(p0.X, p3.X) - bladeWidth * 0.5, Math.Min(p0.Y, p3.Y), Math.Min(p0.Z, p3.Z) - bladeWidth * 0.5);
        var bMax = new Vec3(Math.Max(p0.X, p3.X) + bladeWidth * 0.5, Math.Max(p0.Y, p3.Y), Math.Max(p0.Z, p3.Z) + bladeWidth * 0.5);
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            partId, parentPartId, FloraTissueSlot.Foliage, leafFrame, bladeLength, bladeWidth * 0.5, 0.45, 0, (bMin, bMax)));

        return m.TriangleCount - startTris;
    }

    /// <summary>
    /// Builds a full epiphytic rosette with a spiraling vase-like crown, curving flaring leaves,
    /// inner-cup water-holding cavity, holdfast rootlets, and downward-trailing drape.
    /// </summary>
    public static int Build(MeshData m, EpiphyticRosetteParams p, ulong seed, double[] c1, double[] c2)
    {
        int startTris = m.TriangleCount;
        var rng = Rng.Keyed(seed, "form.epiphytic_rosette", 0);
        int detail = m.FloraDetailLevel ?? p.DetailLevel;

        int leafCount = Math.Clamp(p.LeafCount, 8, 32);
        double baseAzimuth = rng.Range(0.0, Math.PI * 2.0);

        ulong crownId = Rng.Mix(seed, Hash.Fnv1a64("rosette.crown"));
        var crownFrame = FloraAttachmentFrame.Create(p.Origin, Vec3.Up, new Vec3(1, 0, 0));
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            crownId, 0, FloraTissueSlot.Stem, crownFrame, p.CrownHeight, p.FlareRadius, 0.15, 0, (p.Origin, p.Origin + Vec3.Up * p.CrownHeight)));

        // 1. Basal holdfast rootlets anchoring the epiphyte to the host surface
        int roots = detail == 2 ? 3 : 5;
        var holdfastCol = new[] { 0.22, 0.18, 0.12 };
        for (int r = 0; r < roots; r++)
        {
            double rAng = r * (Math.PI * 2.0 / roots) + rng.Range(-0.15, 0.15);
            var rDir = new Vec3(Math.Cos(rAng), -0.25, Math.Sin(rAng)).Normalized();
            var r0 = p.Origin + new Vec3(rDir.X * 0.02, 0.015, rDir.Z * 0.02);
            var r1 = r0 + rDir * 0.035 - Vec3.Up * 0.015;
            var r2 = r1 + new Vec3(rDir.Z * 0.012, -0.010, -rDir.X * 0.012);
            Primitives.Tube(m, new[] { r0, r1, r2 }, new[] { 0.0028, 0.0020, 0.0010 }, 4,
                (i, v) => (holdfastCol, 1.0, i / 2.0, v, 0, 0));
        }

        // 2. Central water reservoir / phytotelma cup floor
        if (p.InnerCupDepth > 1e-4)
        {
            var cupFloor = p.Origin + Vec3.Up * Math.Max(0.005, 0.035 - p.InnerCupDepth * 0.5);
            double cupR = p.FlareRadius * 0.18;
            var cupWaterCol = Primitives.Mix(c1, new[] { 0.10, 0.22, 0.18 }, 0.55);
            int fanSegs = detail == 0 ? 8 : 6;
            var cupRim = new List<Vec3>(fanSegs);
            for (int s = 0; s < fanSegs; s++)
            {
                double a = s * (Math.PI * 2.0 / fanSegs);
                cupRim.Add(cupFloor + new Vec3(Math.Cos(a) * cupR, 0, Math.Sin(a) * cupR));
            }
            Primitives.Fan(m, cupFloor, cupRim, Vec3.Up, cupWaterCol, cupWaterCol);
        }

        // 3. Spiraling vase crown leaves
        // i = 0 is outermost/oldest (arching down), i = leafCount - 1 is innermost/youngest (upright vase)
        for (int i = 0; i < leafCount; i++)
        {
            double rankU = (double)i / Math.Max(1, leafCount - 1);
            double flare = Math.Pow(1.0 - rankU, 0.85); // 1 for outer, 0 for inner
            double upright = Math.Pow(rankU, 0.70);    // 0 for outer, 1 for inner

            double azimuth = baseAzimuth + i * p.PhyllotaxisAngle + rng.Range(-0.05, 0.05);
            var radialDir = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));

            // Sheath attachment around base / inner cup
            double sheathR = p.FlareRadius * (0.06 + 0.08 * (1.0 - rankU));
            double sheathY = 0.012 + 0.024 * rankU;
            var p0 = p.Origin + radialDir * sheathR + Vec3.Up * sheathY;

            double reachR = MathD.Lerp(p.FlareRadius * 0.32, p.FlareRadius * 1.15, flare);
            double peakH = MathD.Lerp(p.CrownHeight * 0.65, p.CrownHeight * 1.05, upright);

            double effCurvature = (p.LeafCurvature + p.Drape) * 0.5;
            double drapeDrop = effCurvature * p.FlareRadius * 0.88 + p.TipCurl * 0.08;
            double tipH = MathD.Lerp(p.CrownHeight * 1.05, p.CrownHeight * 0.20 - drapeDrop, flare);

            var p1 = p.Origin + radialDir * (reachR * 0.28) + Vec3.Up * (p0.Y + peakH * 0.52);
            var p2 = p.Origin + radialDir * (reachR * 0.72) + Vec3.Up * (p0.Y + peakH * 0.90);
            var p3 = p.Origin + radialDir * reachR + Vec3.Up * (p0.Y + tipH);

            double bWid = p.LeafWidth * MathD.Lerp(0.85, 1.15, flare) * rng.Range(0.95, 1.05);

            // Color scheme:
            // Sheath: pale creamy green / soft maroon base
            // Blade: deep emerald/sage outer foliage, transitioning to vibrant cup blush in the center
            var colSheath = Primitives.Mix(c1, new[] { 0.72, 0.78, 0.58 }, 0.45);
            var matureGreen = Primitives.Mix(c1, new[] { 0.20, 0.45, 0.18 }, 0.35);
            var cupBlush = Primitives.Mix(c2, new[] { 0.88, 0.28, 0.35 }, 0.35);
            var colBlade = Primitives.Mix(matureGreen, cupBlush, Math.Pow(rankU, 1.8));
            var colTip = Primitives.Mix(colBlade, c2, 0.45);

            ulong leafSeed = Rng.Mix(seed, (ulong)(i * 577 + 23));

            BuildLeaf(
                m, p0, p1, p2, p3, radialDir, bWid,
                camber: p.Camber,
                midribFold: p.MidribFold,
                tipCurl: p.TipCurl,
                seed: leafSeed,
                colSheath: colSheath,
                colBlade: colBlade,
                colTip: colTip,
                rankU: rankU,
                variegation: p.Variegation,
                variegationStrength: p.VariegationStrength,
                variegationColor: p.VariegationColor,
                detailOverride: detail,
                parentPartId: crownId);
        }

        return m.TriangleCount - startTris;
    }
}
