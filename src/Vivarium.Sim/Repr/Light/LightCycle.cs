using System;

namespace Vivarium.Sim.Repr.Light;

public static class LightCycle
{
    private static readonly double[] Phase = { .05, .25, .50, .72, .90 };
    private static readonly float[] AmbientValues = { .8f, .8f, .8f, 1.17f, 4.6f };
    private static readonly float[] BounceValues = { .6f, .6f, .6f, 1.2f, 1.3f };
    private static readonly float[] ExposureValues = { 1.95f, 1.94f, 1.94f, 1.93f, 1.85f };
    public static float Ambient(double fraction) => Interpolate(fraction, AmbientValues);
    public static float Bounce(double fraction) => Interpolate(fraction, BounceValues);
    public static float Exposure(double fraction) => Interpolate(fraction, ExposureValues);

    private static float Interpolate(double fraction, float[] values)
    {
        double f = fraction - Math.Floor(fraction);
        int hi = 0;
        while (hi < Phase.Length && Phase[hi] < f) hi++;
        int lo = (hi + Phase.Length - 1) % Phase.Length;
        int next = hi % Phase.Length;
        double a = Phase[lo], b = Phase[next];
        if (hi == 0) a -= 1;
        if (hi == Phase.Length) b += 1;
        return (float)(values[lo] + (values[next] - values[lo]) * ((f - a) / (b - a)));
    }
}
