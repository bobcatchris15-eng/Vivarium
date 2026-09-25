using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>Tapered elliptical organ built along an <see cref="Axis"/>.</summary>
public readonly record struct SoftTubeParams(
    AxisParams Axis,
    double BaseRadius = 0.01,
    double TipRadius = 0.004,
    double Ellipticity = 1.0,
    double BaseFlare = 0.0,
    int Segments = 8);

/// <summary>
/// Describes the parent tube a child <see cref="SoftTube"/> blends onto: a shared ring/fillet rather than an
/// interpenetrating cylinder. <see cref="FilletTolerance"/> is how close a blended vertex may sit to the parent's
/// surface (i.e. how deep the fillet is allowed to nestle) without counting as intersection.
/// </summary>
public readonly record struct TubeJunction(Vec3 ParentPoint, Vec3 ParentTangent, double ParentRadius, double FilletLength, double FilletTolerance = 0.0);

public static class SoftTube
{
    public static int Build(MeshData m, SoftTubeParams p, ulong seed,
        Func<int, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr,
        TubeJunction? junction = null)
    {
        int startTris = m.TriangleCount;
        var frames = Axis.Build(p.Axis, seed);
        int n = frames.Count;
        if (n < 2) return 0;
        int segments = Math.Max(6, p.Segments);

        double filletFrac = junction is { } j0 && p.Axis.Length > 1e-9 ? MathD.Clamp01(j0.FilletLength / p.Axis.Length) : 0;

        var radii = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / (n - 1);
            double taper = MathD.Lerp(p.BaseRadius, p.TipRadius, t);
            double flare = p.BaseFlare * Math.Exp(-8 * t);
            double filletBoost = 0;
            if (junction is { } j && filletFrac > 1e-9 && t < filletFrac)
            {
                double ft = 1 - t / filletFrac; // 1 at base, 0 at end of fillet
                filletBoost = (j.ParentRadius - p.BaseRadius) * MathD.SmoothStep(0, 1, ft) * 0.6;
            }
            radii[i] = Math.Max(1e-6, taper * (1 + flare) + Math.Max(0, filletBoost));
        }

        int start = m.VertexCount;
        for (int i = 0; i < n; i++)
        {
            var f = frames[i];
            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                var radial = f.Side * Math.Cos(th) * radii[i] + f.Up * Math.Sin(th) * radii[i] * p.Ellipticity;
                var pos = f.Point + radial;
                var normal = (f.Side * Math.Cos(th) + f.Up * Math.Sin(th) * p.Ellipticity).Normalized();

                if (junction is { } jc)
                {
                    var d = jc.ParentTangent.LengthSq > 1e-12 ? jc.ParentTangent.Normalized() : new Vec3(0, 1, 0);
                    var toV = pos - jc.ParentPoint;
                    var proj = jc.ParentPoint + d * toV.Dot(d);
                    var outward = pos - proj;
                    double dist = outward.Length;
                    double minAllowed = Math.Max(0, jc.ParentRadius - jc.FilletTolerance);
                    if (dist < minAllowed)
                    {
                        var dir = dist > 1e-9 ? outward / dist : normal;
                        pos = proj + dir * minAllowed;
                    }
                }

                var a = attr(i, (double)s / segments);
                m.AddVertex(pos, normal, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int i = 0; i < n - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * (segments + 1) + s, b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        return m.TriangleCount - startTris;
    }
}
