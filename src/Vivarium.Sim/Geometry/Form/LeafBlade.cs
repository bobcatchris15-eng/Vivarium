using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

public enum BladeProfile { Ovate, Lanceolate, Cordate, Reniform, Obcordate, Linear }
public enum LeafMargin { Entire, Crenate, Serrate, Lobed }

/// <summary>
/// Double-sided cambered leaf swept along an <see cref="Axis"/>. Every visual feature (width profile, camber,
/// midrib fold, cup, tip curl, edge ruffle, margin, thickness) is a closed-form function of (t, x) — no
/// per-vertex noise — so the same params always yield the same mesh.
/// </summary>
public readonly record struct LeafBladeParams(
    AxisParams Midrib,
    BladeProfile Profile = BladeProfile.Ovate,
    double HalfWidth = 0.02,
    double Camber = 0.15,
    double MidribFold = 0.0,
    double Cup = 0.0,
    double TipCurl = 0.0,
    double EdgeRuffle = 0.0,
    double Asymmetry = 0.0,
    LeafMargin Margin = LeafMargin.Entire,
    double MarginAmplitude = 0.08,
    double MarginFrequency = 8.0,
    double MidribThickness = 0.0015,
    double PetioleLength = 0.0,
    double PetioleRadius = 0.0008,
    Vec3 Attach = default,
    int DetailLevel = 1);

public static class LeafBlade
{
    private static double WidthEnvelope(BladeProfile profile, double t) => profile switch
    {
        BladeProfile.Ovate => Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.7))), 0.8),
        BladeProfile.Lanceolate => Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 1.4),
        BladeProfile.Cordate => Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(t, 0.55))), 0.7) * (1.0 + 0.15 * (1 - t)),
        BladeProfile.Reniform => Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 0.5) * 1.15,
        BladeProfile.Obcordate => Math.Pow(Math.Max(0, Math.Sin(Math.PI * Math.Pow(1 - t, 0.55))), 0.7) * (1.0 + 0.15 * t),
        BladeProfile.Linear => Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 0.3),
        _ => Math.Max(0, Math.Sin(Math.PI * t)),
    };

    private static double MarginWave(LeafMargin margin, double t, double amp, double freq) => margin switch
    {
        LeafMargin.Entire => 0.0,
        LeafMargin.Crenate => amp * 0.5 * (1 + Math.Cos(2 * Math.PI * t * freq)),
        LeafMargin.Serrate => amp * (2 * Math.Abs((t * freq) - Math.Floor(t * freq + 0.5)) - 0.5),
        LeafMargin.Lobed => amp * Math.Sin(Math.PI * t * Math.Max(1, freq * 0.35)),
        _ => 0.0,
    };

    /// <summary>Builds the blade (plus optional petiole) into <paramref name="m"/>. Returns triangle count added.</summary>
    public static int Build(MeshData m, LeafBladeParams p, ulong seed, double[] baseCol, double[] tipCol)
    {
        int startTris = m.TriangleCount;
        var frames = Axis.Build(p.Midrib, seed);

        int detail = Math.Max(0, p.DetailLevel);
        int longitudinal = detail == 0 ? 6 : 4 + 4 * detail;
        int acrossCols = detail == 0 ? 3 : 2 + 2 * detail; // quads across the full width

        Vec3 Pos(double t, double x, double faceSign, out Vec3 normalOut)
        {
            var f = Axis.Sample(frames, t);
            double env = WidthEnvelope(p.Profile, t);
            double hwLeft = p.HalfWidth * env * (1.0 + p.Asymmetry);
            double hwRight = p.HalfWidth * env * (1.0 - p.Asymmetry);
            double hw = x < 0 ? hwLeft : hwRight;

            double edge = Math.Abs(x);
            double marginMod = 1.0 + MarginWave(p.Margin, t, p.MarginAmplitude, p.MarginFrequency) * Math.Pow(edge, 2);
            double lateral = x * hw * marginMod;

            // Crown height: camber (bowl), midrib fold (V crease), cup (length-wise scoop), tip curl, edge ruffle.
            double crown = p.Camber * (1 - x * x)
                           - p.MidribFold * edge
                           + p.Cup * Math.Sin(Math.PI * t) * (1 - x * x) * 0.6;
            double tipT = MathD.SmoothStep(0.65, 1.0, t);
            crown += p.TipCurl * tipT * (0.4 + 0.6 * (1 - x * x));
            crown += p.EdgeRuffle * Math.Sin(t * 9.0 + edge * 3.0) * Math.Pow(edge, 3);

            double thickness = p.MidribThickness * Math.Pow(Math.Max(0, 1 - edge), 1.5);
            double faceOffset = faceSign * thickness * 0.5;

            var basePoint = f.Point + f.Side * lateral + f.Up * (crown * p.HalfWidth);

            // Approximate the local surface normal from finite differences of the parametric surface.
            const double eps = 1e-3;
            Vec3 Raw(double tt, double xx)
            {
                var ff = Axis.Sample(frames, MathD.Clamp01(tt));
                double e = WidthEnvelope(p.Profile, MathD.Clamp01(tt));
                double hl = p.HalfWidth * e * (1.0 + p.Asymmetry);
                double hr = p.HalfWidth * e * (1.0 - p.Asymmetry);
                double h = xx < 0 ? hl : hr;
                double ed = Math.Abs(xx);
                double mm = 1.0 + MarginWave(p.Margin, tt, p.MarginAmplitude, p.MarginFrequency) * Math.Pow(ed, 2);
                double lat = xx * h * mm;
                double cr = p.Camber * (1 - xx * xx) - p.MidribFold * ed + p.Cup * Math.Sin(Math.PI * tt) * (1 - xx * xx) * 0.6;
                double tt2 = MathD.SmoothStep(0.65, 1.0, tt);
                cr += p.TipCurl * tt2 * (0.4 + 0.6 * (1 - xx * xx));
                cr += p.EdgeRuffle * Math.Sin(tt * 9.0 + ed * 3.0) * Math.Pow(ed, 3);
                return ff.Point + ff.Side * lat + ff.Up * (cr * p.HalfWidth);
            }
            var ahead = Raw(Math.Min(1, t + eps), x) - Raw(Math.Max(0, t - eps), x);
            var cross = Raw(t, Math.Min(1, x + eps * 4)) - Raw(t, Math.Max(-1, x - eps * 4));
            var n = ahead.Cross(cross).Normalized();
            if (n.LengthSq < 1e-8) n = f.Up;
            normalOut = n;
            return basePoint + n * faceOffset;
        }

        for (int face = 0; face < 2; face++)
        {
            double faceSign = face == 0 ? 1.0 : -1.0;
            double u2 = face; // 0 = top, 1 = bottom — the S5 material pass keys back-translucency off this.
            int start = m.VertexCount;
            for (int i = 0; i <= longitudinal; i++)
            {
                double t = (double)i / longitudinal;
                for (int j = 0; j <= acrossCols; j++)
                {
                    double x = (double)j / acrossCols * 2 - 1;
                    var pos = Pos(t, x, faceSign, out var n);
                    var faceN = face == 0 ? n : -n;
                    var col = Primitives.Mix(baseCol, tipCol, t);
                    // UV2.y marks blade surface; petiole and stem vertices keep zero.
                    m.AddVertex(pos, faceN, col, 1, t, (x + 1) * 0.5, u2, 1);
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

        if (p.PetioleLength > 1e-9 && frames.Count > 0)
        {
            var attach = p.Attach;
            var tip = frames[0].Point;
            var path = new List<Vec3> { attach, tip };
            var radii = new List<double> { p.PetioleRadius * 1.15, p.PetioleRadius * 0.85 };
            Primitives.Tube(m, path, radii, 6, (i, v) => (baseCol, 1.0, v, i, 0, 0));
        }

        return m.TriangleCount - startTris;
    }
}
