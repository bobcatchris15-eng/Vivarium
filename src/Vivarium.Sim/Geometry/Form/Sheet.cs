using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>
/// Variable-width ribbon/membrane over a curved <see cref="Axis"/> — petals, grass blades, fungal lobes.
/// Lobing and curl are closed-form functions of (t, x), like <see cref="LeafBlade"/>.
/// </summary>
public readonly record struct SheetParams(
    Form.AxisParams Axis,
    double HalfWidth = 0.015,
    int LobeCount = 0,
    double LobeDepth = 0.0,
    double Thickness = 0.001,
    double Curl = 0.0,
    int DetailLevel = 1);

public static class Sheet
{
    public static int Build(MeshData m, SheetParams p, ulong seed, double[] baseCol, double[] tipCol)
    {
        int startTris = m.TriangleCount;
        var frames = Axis.Build(p.Axis, seed);
        int detail = Math.Max(1, p.DetailLevel);
        int longitudinal = 4 + 4 * detail;
        int acrossCols = 2 + 2 * detail;

        double Envelope(double t) => Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 0.6);
        double LobeMod(double t) => p.LobeCount > 0 ? 1.0 + p.LobeDepth * Math.Sin(t * p.LobeCount * Math.PI) : 1.0;

        Vec3 Raw(double t, double x)
        {
            var f = Axis.Sample(frames, MathD.Clamp01(t));
            double hw = p.HalfWidth * Envelope(t) * LobeMod(t);
            double lateral = x * hw;
            double edge = Math.Abs(x);
            double crown = p.Curl * t * edge * edge * p.HalfWidth * 6.0;
            return f.Point + f.Side * lateral + f.Up * crown;
        }

        for (int face = 0; face < 2; face++)
        {
            double faceSign = face == 0 ? 1.0 : -1.0;
            double u2 = face;
            int start = m.VertexCount;
            for (int i = 0; i <= longitudinal; i++)
            {
                double t = (double)i / longitudinal;
                for (int j = 0; j <= acrossCols; j++)
                {
                    double x = (double)j / acrossCols * 2 - 1;
                    double edge = Math.Abs(x);
                    double thickness = p.Thickness * Math.Max(0, 1 - edge);
                    var basePos = Raw(t, x);

                    const double eps = 1e-3;
                    var ahead = Raw(Math.Min(1, t + eps), x) - Raw(Math.Max(0, t - eps), x);
                    var cross = Raw(t, Math.Min(1, x + eps * 4)) - Raw(t, Math.Max(-1, x - eps * 4));
                    var n = ahead.Cross(cross).Normalized();
                    if (n.LengthSq < 1e-8) n = new Vec3(0, 1, 0);

                    var pos = basePos + n * (faceSign * thickness * 0.5);
                    var faceN = face == 0 ? n : -n;
                    var col = Primitives.Mix(baseCol, tipCol, t);
                    m.AddVertex(pos, faceN, col, 1, t, (x + 1) * 0.5, u2, 0);
                }
            }
            int row = acrossCols + 1;
            var outward = face == 0 ? new Vec3(0, 1, 0) : new Vec3(0, -1, 0);
            for (int i = 0; i < longitudinal; i++)
                for (int j = 0; j < acrossCols; j++)
                {
                    int a = start + i * row + j, b = a + row;
                    var n0 = m.NormalAt(a);
                    Primitives.TriangleFacing(m, a, b, a + 1, n0.LengthSq > 1e-8 ? n0 : outward);
                    Primitives.TriangleFacing(m, a + 1, b, b + 1, n0.LengthSq > 1e-8 ? n0 : outward);
                }
        }
        return m.TriangleCount - startTris;
    }
}
