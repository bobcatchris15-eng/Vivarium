namespace Vivarium.Sim.Core;

/// <summary>
/// Deterministic PRNG (xoshiro256**), seeded through SplitMix64 from (world seed, stream name).
/// Streams are independent: consuming one never perturbs another.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        ulong sm = seed;
        _s0 = SplitMix(ref sm); _s1 = SplitMix(ref sm); _s2 = SplitMix(ref sm); _s3 = SplitMix(ref sm);
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 0x9E3779B97F4A7C15UL;
    }

    /// <summary>Named subsystem stream derived from the world seed.</summary>
    public static Rng Stream(ulong worldSeed, string streamName) => new(Mix(worldSeed, Hash.Fnv1a64(streamName)));

    /// <summary>
    /// Keyed one-shot generator: identical (seed, stream, a, b) always yields the same sequence,
    /// regardless of any other consumption. Used for per-entity events.
    /// </summary>
    public static Rng Keyed(ulong worldSeed, string streamName, ulong a, ulong b = 0) =>
        new(Mix(Mix(Mix(worldSeed, Hash.Fnv1a64(streamName)), a), b));

    /// <summary>Stateless hash to [0,1) for (seed, stream, a, b). Cheap alternative to Keyed for single draws.</summary>
    public static double HashUnit(ulong worldSeed, ulong streamHash, ulong a, ulong b = 0)
    {
        ulong x = Mix(Mix(Mix(worldSeed, streamHash), a), b);
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    public static ulong Mix(ulong a, ulong b)
    {
        ulong x = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        return SplitMix(ref x);
    }

    private static ulong SplitMix(ref ulong x)
    {
        ulong z = (x += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0,1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)((NextULong() >> 33) % (ulong)maxExclusive);
    }

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Standard normal via Box–Muller (uses two draws; deterministic).</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

public static class Hash
{
    /// <summary>Stable 64-bit FNV-1a over UTF-16 code units (never string.GetHashCode, which is randomized).</summary>
    public static ulong Fnv1a64(string s)
    {
        ulong h = 0xcbf29ce484222325UL;
        foreach (char c in s)
        {
            h ^= (byte)(c & 0xFF); h *= 0x100000001b3UL;
            h ^= (byte)(c >> 8); h *= 0x100000001b3UL;
        }
        return h;
    }
}
