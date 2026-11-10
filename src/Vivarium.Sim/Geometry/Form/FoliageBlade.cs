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
        var baseUp = forward.Cross(baseSide).Normalized();
        if (baseUp.Y < 0.0)
        {
            baseUp = -baseUp;
            baseSide = -baseSide;
        }

        int detail = m.FloraDetailLevel ?? 0;
        int rows = detail == 0 ? 18 : (detail == 1 ? 12 : 8);
        if (m.Lod is { } lod) rows = lod.Segments(rows, 4);
        int cols = detail == 0 ? 8 : (detail == 1 ? 6 : 4);
        if (m.Lod is { } lodC) cols = Math.Max(3, (int)Math.Round(cols * lodC.Blade));

        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;

        Vec3 At(double t, double x)
        {
            var side = baseSide;
            var up = baseUp;
            // Sheathed culm base wrapping claspingly around stem axis
            if (t < 0.18)
            {
                double sw = 1.0 - t / 0.18;
                side = Axis.RotateAround(side, forward, sw * 0.35 * x);
                up = forward.Cross(side).Normalized();
            }

            double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, shoulder))), 0.8);
            double width = halfWidth * envelope * (1 + asymmetry * x);
            double lift = length * curl * Math.Sin(Math.PI * t) * (1 - 1.5 * t)
                + halfWidth * envelope * camber * (1 - x * x);

            // Keeled V-section: adaxial canaliculate groove
            double vFold = -0.16 * halfWidth * (1.0 - x * x) * Math.Sin(Math.PI * Math.Pow(t, 0.40));

            return root + axis * t + side * (x * width + asymmetry * halfWidth * Math.Sin(Math.PI * t) * t)
                + up * (lift + vFold);
        }

        Vec3 Normal(double t, double x)
        {
            const double d = 0.002;
            var a = At(Math.Min(1, t + d), x) - At(Math.Max(0, t - d), x);
            var b = At(t, Math.Min(1, x + d)) - At(t, Math.Max(-1, x - d));
            var normal = a.Cross(b).Normalized();
            return normal.LengthSq < 1e-8 || normal.Dot(baseUp) < 0 ? baseUp : normal;
        }

        int start = m.VertexCount;
        for (int r = 0; r <= rows; r++)
        {
            double t = (double)r / rows;
            var col = Primitives.Mix(baseColor, tipColor, t);
            // Sheathed culm base paler membranous tint
            if (t < 0.18)
                col = Primitives.Mix(col, new[] { 0.85, 0.90, 0.72 }, (0.18 - t) / 0.18 * 0.35);

            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var pos = At(t, x);
                var n = Normal(t, x);
                m.AddVertex(pos, n, col, 1.0, t, (x + 1.0) * 0.5, 0.5, 1.0);
            }
        }

        int rowStride = cols + 1;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a = start + r * rowStride + c;
                int b = a + rowStride;
                var n0 = m.NormalAt(a);
                Primitives.TriangleFacing(m, a, b, a + 1, n0.LengthSq > 1e-8 ? n0 : baseUp);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, n0.LengthSq > 1e-8 ? n0 : baseUp);
            }
        }

        m.RecordLeaf(leafVertex, leafIndex, root, length);
    }
}
