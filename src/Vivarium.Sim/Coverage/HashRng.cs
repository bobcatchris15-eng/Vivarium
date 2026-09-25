namespace Vivarium.Sim.Coverage;

/// <summary>
/// Order-independent randomness for coverage rules (docs/overhaul/growth_models.md §1.4). Every stochastic
/// decision hashes its full identity (world seed, layer, tile, cell, step, purpose) instead of drawing from a
/// sequential stream, so results never depend on iteration order, active-set membership, or tile allocation order.
/// </summary>
public static class HashRng
{
    private static ulong Mix(ulong h)
    {
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53UL;
        h ^= h >> 33;
        return h;
    }

    /// <summary>Deterministic 64-bit hash of the full stochastic-decision identity.</summary>
    public static ulong HashU64(ulong worldSeed, int layerId, int ti, int tj, int cellIndex, long step, int purpose)
    {
        ulong h = Mix(worldSeed ^ 0x9E3779B97F4A7C15UL);
        h = Mix(h ^ unchecked((ulong)(uint)layerId));
        h = Mix(h ^ unchecked((ulong)(long)ti));
        h = Mix(h ^ unchecked((ulong)(long)tj));
        h = Mix(h ^ unchecked((ulong)(uint)cellIndex));
        h = Mix(h ^ unchecked((ulong)step));
        h = Mix(h ^ unchecked((ulong)(uint)purpose));
        return h;
    }

    /// <summary>Deterministic uniform value in [0, 1), independent of call order.</summary>
    public static double Hash01(ulong worldSeed, int layerId, int ti, int tj, int cellIndex, long step, int purpose)
    {
        ulong h = HashU64(worldSeed, layerId, ti, tj, cellIndex, step, purpose);
        // top 53 bits -> exact double in [0,1)
        return (h >> 11) * (1.0 / 9007199254740992.0);
    }
}
