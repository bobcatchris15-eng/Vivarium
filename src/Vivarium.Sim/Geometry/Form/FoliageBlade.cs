using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>Small cambered blades for dense foliage. Collapsed root/tip rows are triangulated as fans,
/// keeping a genuinely curved, double-sided silhouette at 8..40 triangles without degenerate faces.
/// UV.x runs from attachment to tip; UV2=(face,1) identifies foliage for the plant material.</summary>
public static class FoliageBlade
{
    public static void Build(MeshData m, Vec3 root, Vec3 tip, Vec3 sideHint, double halfWidth,
        double[] baseColor, double[] tipColor, double camber = 0.12, double curl = 0.08,
        double roll = 0, double asymmetry = 0, double shoulder = 0.85, int segments = 18)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-8 || halfWidth <= 0) return;
        var forward = axis / length;
        var baseSide = sideHint - forward * sideHint.Dot(forward);
        if (baseSide.LengthSq < 1e-8) baseSide = forward.Cross(Vec3.Up);
        if (baseSide.LengthSq < 1e-8) baseSide = forward.Cross(new Vec3(1, 0, 0));
        baseSide = Axis.RotateAround(baseSide.Normalized(), forward, roll);
        var baseUp = baseSide.Cross(forward).Normalized();

        int detail = m.FloraDetailLevel ?? 0;
        int rows = detail == 0 ? 18 : (detail == 1 ? 12 : 8);
        if (m.Lod is { } lod) rows = lod.Segments(rows, 4);
        int cols = detail == 0 ? 8 : (detail == 1 ? 6 : 4);
        if (m.Lod is { } lodC) cols = Math.Max(3, (int)Math.Round(cols * lodC.Blade));

        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;

        Vec3 At(double t, double x, int face)
        {
            var side = baseSide;
            var up = baseUp;
            // Sheathed culm base wrapping claspingly around stem axis
            if (t < 0.18)
            {
                double sw = 1.0 - t / 0.18;
                side = Axis.RotateAround(side, forward, sw * 0.35 * x);
                up = side.Cross(forward).Normalized();
            }

            double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, shoulder))), 0.8);
            double width = halfWidth * envelope * (1 + asymmetry * x);
            double lift = length * curl * Math.Sin(Math.PI * t) * (1 - 1.5 * t)
                + halfWidth * envelope * camber * (1 - x * x);

            // Keeled V-section: adaxial canaliculate groove, abaxial prominent keel
            double absX = Math.Abs(x);
            double vFold = -0.16 * halfWidth * (1.0 - x * x) * Math.Sin(Math.PI * Math.Pow(t, 0.40));
            double keel = -0.22 * halfWidth * (1.0 - absX) * Math.Sin(Math.PI * Math.Pow(t, 0.35));
            double normalOffset = face == 0 ? (0.00018 + vFold) : (-0.00018 + keel);

            return root + axis * t + side * (x * width + asymmetry * halfWidth * Math.Sin(Math.PI * t) * t)
                + up * (lift + normalOffset);
        }

        Vec3 Normal(double t, double x, int face)
        {
            const double d = 0.002;
            var a = At(Math.Min(1, t + d), x, face) - At(Math.Max(0, t - d), x, face);
            var b = At(t, Math.Min(1, x + d), face) - At(t, Math.Max(-1, x - d), face);
            var normal = (face == 0 ? b.Cross(a) : a.Cross(b)).Normalized();
            var expected = (face == 0 ? baseUp : -baseUp);
            return normal.LengthSq < 1e-8 || normal.Dot(expected) < 0 ? expected : normal;
        }

        int midRows = rows - 1;
        var grid = new Vec3[2, midRows, cols + 1];
        var gridNormals = new Vec3[2, midRows, cols + 1];
        var rootPositions = new Vec3[2];
        var tipPositions = new Vec3[2];
        var rootNormals = new Vec3[2];
        var tipNormals = new Vec3[2];

        for (int face = 0; face < 2; face++)
        {
            rootPositions[face] = At(0, 0, face);
            rootNormals[face] = Normal(0, 0, face);
            tipPositions[face] = At(1, 0, face);
            tipNormals[face] = Normal(1, 0, face);

            for (int r = 1; r < rows; r++)
            {
                double t = (double)r / rows;
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    grid[face, r - 1, c] = At(t, x, face);
                    gridNormals[face, r - 1, c] = Normal(t, x, face);
                }
            }
        }

        var underCol = Primitives.Mix(baseColor, new[] { 0.35, 0.48, 0.28 }, 0.42);
        int[] rootIndices = new int[2];
        int[] tipIndices = new int[2];
        int[,,] gridIndices = new int[2, midRows, cols + 1];

        // Emit solid 2-sided 3D shell (faceCount = 2 + stitched margin rim strip)
        for (int face = 0; face < 2; face++)
        {
            double u2 = face; // 0 = adaxial, 1 = abaxial
            var bCol = face == 0 ? baseColor : underCol;
            var tCol = face == 0 ? tipColor : underCol;

            rootIndices[face] = m.AddVertex(rootPositions[face], rootNormals[face], bCol, 1.0, 0.0, 0.5, u2, 1.0);

            for (int r = 1; r < rows; r++)
            {
                double t = (double)r / rows;
                var gradCol = Primitives.Mix(bCol, tCol, t);
                // Sheathed culm base paler membranous tint
                if (t < 0.18)
                    gradCol = Primitives.Mix(gradCol, new[] { 0.85, 0.90, 0.72 }, (0.18 - t) / 0.18 * 0.35);

                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    gridIndices[face, r - 1, c] = m.AddVertex(
                        grid[face, r - 1, c], gridNormals[face, r - 1, c], gradCol, 1.0, t, (x + 1.0) * 0.5, u2, 1.0);
                }
            }

            tipIndices[face] = m.AddVertex(tipPositions[face], tipNormals[face], tCol, 1.0, 1.0, 0.5, u2, 1.0);

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
        Primitives.TriangleFacing(m, rootIndices[0], rootIndices[1], gridIndices[0, 0, 0], -baseSide);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[1, 0, 0], gridIndices[0, 0, 0], -baseSide);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, 0], gridIndices[1, r, 0], gridIndices[0, r + 1, 0], -baseSide);
            Primitives.TriangleFacing(m, gridIndices[1, r, 0], gridIndices[1, r + 1, 0], gridIndices[0, r + 1, 0], -baseSide);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, 0], gridIndices[1, rows - 2, 0], tipIndices[0], -baseSide);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, 0], tipIndices[1], tipIndices[0], -baseSide);

        // Right margin rim (c = cols, x = +1.0)
        Primitives.TriangleFacing(m, rootIndices[0], gridIndices[0, 0, cols], rootIndices[1], baseSide);
        Primitives.TriangleFacing(m, rootIndices[1], gridIndices[0, 0, cols], gridIndices[1, 0, cols], baseSide);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIndices[0, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r, cols], baseSide);
            Primitives.TriangleFacing(m, gridIndices[1, r, cols], gridIndices[0, r + 1, cols], gridIndices[1, r + 1, cols], baseSide);
        }
        Primitives.TriangleFacing(m, gridIndices[0, rows - 2, cols], tipIndices[0], gridIndices[1, rows - 2, cols], baseSide);
        Primitives.TriangleFacing(m, gridIndices[1, rows - 2, cols], tipIndices[0], tipIndices[1], baseSide);

        m.RecordLeaf(leafVertex, leafIndex, root, length);
    }
}
