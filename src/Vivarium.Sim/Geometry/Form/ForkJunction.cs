using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>
/// Shared branch-fork treatment: area-conserving child radii (da Vinci, r_parent^2.5 = sum r_child^2.5),
/// a smooth parent swelling at the fork, children whose base is sunk into the parent, and one collar
/// whose ring plane is normal to the child's first tangent.
/// </summary>
public static class ForkJunction
{
    public const double Exponent = 2.5;
    public const double MaxParentWiden = 1.25;
    public const double WidenRangeInChildRadii = 1.5;
    public const double SinkInChildRadii = 0.5;

    /// <summary>Radius of each of <paramref name="count"/> equal children so that sum r_child^2.5 = r_parent^2.5.</summary>
    public static double ChildRadius(double parentRadius, int count) =>
        parentRadius * Math.Pow(Math.Max(1, count), -1.0 / Exponent);

    /// <summary>Radius of a child that takes <paramref name="share"/> (0..1) of the parent's r^2.5 budget.</summary>
    public static double ChildRadiusShare(double parentRadius, double share) =>
        parentRadius * Math.Pow(MathD.Clamp(share, 1e-4, 1.0), 1.0 / Exponent);

    /// <summary>
    /// Widens the parent radii (in place) near each fork by up to 1.25x with a smoothstep falloff over
    /// ~1.5 child radii (at least ~0.75 station spacing so coarse paths still pick it up). Call before Tube.
    /// </summary>
    public static void WidenParent(IReadOnlyList<Vec3> path, double[] radii, params (Vec3 Pos, double ChildRadius)[] forks)
    {
        int n = Math.Min(path.Count, radii.Length);
        if (n < 2 || forks.Length == 0) return;
        var factor = new double[n];
        for (int i = 0; i < n; i++) factor[i] = 1.0;
        foreach (var (pos, cr) in forks)
        {
            if (cr <= 0) continue;
            for (int i = 0; i < n; i++)
            {
                double spacing = 0;
                if (i > 0) spacing = Math.Max(spacing, (path[i] - path[i - 1]).Length);
                if (i < n - 1) spacing = Math.Max(spacing, (path[i + 1] - path[i]).Length);
                double range = Math.Max(WidenRangeInChildRadii * cr, 0.75 * spacing);
                double d = (path[i] - pos).Length;
                double w = 1.0 - MathD.SmoothStep(0, range, d);
                factor[i] = Math.Max(factor[i], 1.0 + (MaxParentWiden - 1.0) * w);
            }
        }
        for (int i = 0; i < n; i++) radii[i] *= factor[i];
    }

    /// <summary>Copy of the child path with its first point sunk ~0.5 child radius back along the first tangent.</summary>
    public static Vec3[] SinkStart(IReadOnlyList<Vec3> path, double childRadius)
    {
        var res = new Vec3[path.Count];
        for (int i = 0; i < res.Length; i++) res[i] = path[i];
        if (res.Length >= 2)
        {
            var t = (path[1] - path[0]).Normalized();
            res[0] = path[0] - t * (SinkInChildRadii * childRadius);
        }
        return res;
    }

    /// <summary>
    /// Short swollen collar ring-stack whose rings are normal to <paramref name="tangent"/> (the child's first tangent).
    /// <paramref name="center"/> is the collar mid point; <paramref name="radius"/> its mid radius.
    /// </summary>
    public static void Collar(MeshData m, Vec3 center, Vec3 tangent, double radius, double length, int detail,
        double[] colA, double[] colB, double fluteAmp = 0.10, int fluteLobes = 4)
    {
        var T = tangent.Normalized();
        if (T.LengthSq < 1e-12) T = Vec3.Up;
        var seedAxis = Math.Abs(T.Y) < 0.9 ? Vec3.Up : Vec3.Right;
        var A = T.Cross(seedAxis).Normalized();
        var B = T.Cross(A).Normalized();

        int collarStart = m.VertexCount;
        int rings = detail == 2 ? 3 : 5;
        int sectors = detail == 2 ? 8 : 10;

        for (int r = 0; r <= rings; r++)
        {
            double tr = r / (double)rings;
            double along = (tr - 0.5) * length;
            double shape = Math.Sin(Math.PI * tr);
            double rad = radius * (0.85 + 0.38 * shape);
            for (int s = 0; s <= sectors; s++)
            {
                double u = s / (double)sectors;
                double th = 2.0 * Math.PI * u;
                double flute = 1.0 + fluteAmp * Math.Cos(fluteLobes * th);
                var radial = A * Math.Cos(th) + B * Math.Sin(th);
                var pos = center + T * along + radial * (rad * flute);
                var norm = (radial + T * ((tr - 0.5) * 0.6)).Normalized();
                var col = Primitives.Mix(colA, colB, tr);
                m.AddVertex(pos, norm, col, 1.0, along * 3.0, u, 2.0, 0.0);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < sectors; s++)
            {
                int a = collarStart + r * (sectors + 1) + s;
                int b = a + sectors + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }
        m.Structural.Add((collarStart, m.VertexCount - collarStart, sectors, radius));
    }
}
