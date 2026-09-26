using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>Small cambered blades for dense foliage. Collapsed root/tip rows are triangulated as fans,
/// keeping a genuinely curved, double-sided silhouette at 8..40 triangles without degenerate faces.
/// UV.x runs from attachment to tip; UV2=(face,1) identifies foliage for the plant material.</summary>
public static class FoliageBlade
{
    public static void Build(MeshData m, Vec3 root, Vec3 tip, Vec3 sideHint, double halfWidth,
        double[] baseColor, double[] tipColor, double camber = 0.12, double curl = 0.08,
        double roll = 0, double asymmetry = 0, double shoulder = 0.85, int segments = 4)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-8 || halfWidth <= 0) return;
        var forward = axis / length;
        var side = sideHint - forward * sideHint.Dot(forward);
        if (side.LengthSq < 1e-8) side = forward.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = forward.Cross(new Vec3(1, 0, 0));
        side = Axis.RotateAround(side.Normalized(), forward, roll);
        var up = side.Cross(forward).Normalized();
        int n = Math.Clamp(segments, 2, 6);

        Vec3 At(double t, double x)
        {
            double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, shoulder))), 0.8);
            double width = halfWidth * envelope * (1 + asymmetry * x);
            double lift = length * curl * Math.Sin(Math.PI * t) * (1 - 1.5 * t)
                + halfWidth * envelope * (camber * (1 - x * x) - 0.06 * Math.Abs(x));
            return root + axis * t + side * (x * width + asymmetry * halfWidth * Math.Sin(Math.PI * t) * t)
                + up * lift;
        }
        Vec3 Normal(double t, double x)
        {
            const double d = 0.002;
            var a = At(Math.Min(1, t + d), x) - At(Math.Max(0, t - d), x);
            var b = At(t, Math.Min(1, x + d)) - At(t, Math.Max(-1, x - d));
            var normal = b.Cross(a).Normalized();
            return normal.LengthSq < 1e-8 ? up : normal;
        }
        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1 : -1;
            int Vertex(double t, double x) => m.AddVertex(At(t, x), Normal(t, x) * sign,
                Primitives.Mix(baseColor, tipColor, t), 1, t, (x + 1) * 0.5, face, 1);
            int start = Vertex(0, 0);
            for (int row = 1; row < n; row++)
                for (int col = 0; col < 3; col++) Vertex(row / (double)n, col - 1);
            int end = Vertex(1, 0);
            void Tri(int a, int b, int c) => Primitives.TriangleFacing(m, a, b, c,
                (m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c)).Normalized());
            Tri(start, start + 1, start + 2);
            Tri(start, start + 2, start + 3);
            for (int row = 0; row < n - 2; row++)
            {
                int a = start + 1 + row * 3;
                for (int col = 0; col < 2; col++)
                {
                    Tri(a + col, a + col + 3, a + col + 1);
                    Tri(a + col + 1, a + col + 3, a + col + 4);
                }
            }
            Tri(end - 3, end, end - 2);
            Tri(end - 2, end, end - 1);
        }
    }
}
