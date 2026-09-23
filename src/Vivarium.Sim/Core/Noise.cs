namespace Vivarium.Sim.Core;

/// <summary>Seeded 2D gradient noise + fBm. Pure functions of (seed, x, z): no state, no global tables.</summary>
public static class Noise
{
    private static double Grad(ulong seed, long ix, long iz, double dx, double dz)
    {
        ulong h = Rng.Mix(Rng.Mix(seed, (ulong)ix), (ulong)iz);
        double a = (h >> 11) * (1.0 / (1UL << 53)) * 2 * Math.PI;
        return Math.Cos(a) * dx + Math.Sin(a) * dz;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    /// <summary>Perlin-style gradient noise in roughly [-1, 1].</summary>
    public static double Gradient(ulong seed, double x, double z)
    {
        long x0 = (long)Math.Floor(x), z0 = (long)Math.Floor(z);
        double fx = x - x0, fz = z - z0;
        double n00 = Grad(seed, x0, z0, fx, fz);
        double n10 = Grad(seed, x0 + 1, z0, fx - 1, fz);
        double n01 = Grad(seed, x0, z0 + 1, fx, fz - 1);
        double n11 = Grad(seed, x0 + 1, z0 + 1, fx - 1, fz - 1);
        double u = Fade(fx), v = Fade(fz);
        double nx0 = n00 + u * (n10 - n00);
        double nx1 = n01 + u * (n11 - n01);
        return (nx0 + v * (nx1 - nx0)) * 1.41421356;
    }

    /// <summary>Fractal Brownian motion, normalized to roughly [-1, 1].</summary>
    public static double Fbm(ulong seed, double x, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, norm = 0, f = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Gradient(Rng.Mix(seed, (ulong)i), x * f, z * f);
            norm += amp; amp *= gain; f *= lacunarity;
        }
        return norm > 0 ? sum / norm : 0;
    }

    /// <summary>3D value noise used by procedural prop meshes (deterministic, smooth).</summary>
    public static double Value3(ulong seed, double x, double y, double z)
    {
        long x0 = (long)Math.Floor(x), y0 = (long)Math.Floor(y), z0 = (long)Math.Floor(z);
        double fx = Fade(x - x0), fy = Fade(y - y0), fz = Fade(z - z0);
        double V(long a, long b, long c) => (Rng.Mix(Rng.Mix(Rng.Mix(seed, (ulong)a), (ulong)b), (ulong)c) >> 11) * (1.0 / (1UL << 53)) * 2 - 1;
        double l(double a, double b, double t) => a + (b - a) * t;
        return l(
            l(l(V(x0, y0, z0), V(x0 + 1, y0, z0), fx), l(V(x0, y0 + 1, z0), V(x0 + 1, y0 + 1, z0), fx), fy),
            l(l(V(x0, y0, z0 + 1), V(x0 + 1, y0, z0 + 1), fx), l(V(x0, y0 + 1, z0 + 1), V(x0 + 1, y0 + 1, z0 + 1), fx), fy),
            fz);
    }

    public static double Fbm3(ulong seed, double x, double y, double z, int octaves)
    {
        double sum = 0, amp = 1, norm = 0, f = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value3(Rng.Mix(seed, (ulong)i), x * f, y * f, z * f);
            norm += amp; amp *= 0.5; f *= 2;
        }
        return sum / norm;
    }
}
