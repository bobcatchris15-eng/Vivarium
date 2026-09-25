using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry.Form;

/// <summary>
/// Parameters for a curved centre line. Shape comes entirely from these floats — no per-vertex noise.
/// Angles are radians. BaseAzimuth picks the horizontal direction the organ leans/bends toward.
/// </summary>
public readonly record struct AxisParams(
    double Length = 0.1,
    double BaseAngle = 0.0,
    double BaseAzimuth = 0.0,
    double Droop = 0.0,
    double PhototropicBend = 0.0,
    double TwistPerLength = 0.0,
    double WobbleAmplitude = 0.0,
    double WobbleFrequency = 2.0,
    int Segments = 8);

/// <summary>A single frame along an axis: point, forward tangent, and a parallel-transported side/up basis.</summary>
public readonly record struct AxisFrame(Vec3 Point, Vec3 Tangent, Vec3 Side, Vec3 Up);

/// <summary>Builds deterministic curved centre lines with parallel-transport frames (no twisting flips).</summary>
public static class Axis
{
    /// <summary>Rotates <paramref name="v"/> by <paramref name="angle"/> radians about unit axis <paramref name="axis"/> (Rodrigues).</summary>
    public static Vec3 RotateAround(Vec3 v, Vec3 axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + axis.Cross(v) * s + axis * axis.Dot(v) * (1 - c);
    }

    public static List<AxisFrame> Build(AxisParams p, ulong seed)
    {
        int segs = Math.Max(1, p.Segments);
        var frames = new List<AxisFrame>(segs + 1);

        double phase = Rng.HashUnit(seed, Hash.Fnv1a64("form.axis.wobble"), 0) * Math.PI * 2;

        var azimuthDir = new Vec3(Math.Cos(p.BaseAzimuth), 0, Math.Sin(p.BaseAzimuth));
        var baseDir = (Vec3.Up * Math.Cos(p.BaseAngle) + azimuthDir * Math.Sin(p.BaseAngle)).Normalized();

        // Bend plane axis: perpendicular to the azimuth direction and world up. Droop/phototropism rotate the
        // tangent within this plane so the organ arcs down (gravitropic) or back up (phototropic) smoothly.
        var bendAxis = azimuthDir.Cross(Vec3.Up);
        if (bendAxis.LengthSq < 1e-8) bendAxis = new Vec3(0, 0, 1).Cross(Vec3.Up);
        if (bendAxis.LengthSq < 1e-8) bendAxis = new Vec3(1, 0, 0);
        bendAxis = bendAxis.Normalized();

        double ds = p.Length / segs;
        Vec3 point = Vec3.Zero;

        Vec3 side = baseDir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = baseDir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i <= segs; i++)
        {
            double t = (double)i / segs;
            double bendAngle = p.Droop * t - p.PhototropicBend * t;
            var dir = RotateAround(baseDir, bendAxis, bendAngle);

            if (i > 0)
            {
                point += dir * ds;
            }

            // Parallel transport of the side vector into the new tangent plane (same trick as Primitives.Tube),
            // then twist deliberately about the tangent by a controlled amount per unit length.
            side = side - dir * side.Dot(dir);
            if (side.LengthSq < 1e-8)
            {
                side = dir.Cross(Vec3.Up);
                if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
            }
            side = side.Normalized();

            double twistAngle = p.TwistPerLength * p.Length * t;
            var twistedSide = RotateAround(side, dir, twistAngle);
            var up = twistedSide.Cross(dir).Normalized();

            // Lateral wobble: a deterministic sinusoid (seeded phase only), amplitude growing toward the tip so
            // bases stay anchored. This is shape-from-parameters, not per-vertex noise.
            double wob = p.WobbleAmplitude * Math.Sin(2 * Math.PI * p.WobbleFrequency * t + phase) * t;
            var wobbled = point + twistedSide * wob;

            frames.Add(new AxisFrame(wobbled, dir, twistedSide, up));
        }
        return frames;
    }

    /// <summary>Interpolates a frame at fractional position t in [0,1] along a built frame list.</summary>
    public static AxisFrame Sample(IReadOnlyList<AxisFrame> frames, double t)
    {
        t = MathD.Clamp01(t);
        double f = t * (frames.Count - 1);
        int i0 = (int)Math.Floor(f);
        int i1 = Math.Min(frames.Count - 1, i0 + 1);
        double frac = f - i0;
        var a = frames[i0];
        var b = frames[i1];
        var point = Vec3.Lerp(a.Point, b.Point, frac);
        var tangent = Vec3.Lerp(a.Tangent, b.Tangent, frac).Normalized();
        var side = Vec3.Lerp(a.Side, b.Side, frac).Normalized();
        var up = Vec3.Lerp(a.Up, b.Up, frac).Normalized();
        return new AxisFrame(point, tangent, side, up);
    }
}
