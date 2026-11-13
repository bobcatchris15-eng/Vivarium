using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Authoring helpers for shared organ shapes.</summary>
public static class PlantShapes
{
    /// <summary>Samples a half-width profile (unit length) at n evenly spaced stations from base to tip.</summary>
    public static float[] Profile(Func<double, double> f, int n = 9)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++) a[i] = (float)f(i / (double)(n - 1));
        return a;
    }

    /// <summary>Authored colours are sRGB (as in the species json); descriptors store linear rgb.</summary>
    public static Vec3 Srgb(double r, double g, double b) => new(Lin(r), Lin(g), Lin(b));

    public static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
