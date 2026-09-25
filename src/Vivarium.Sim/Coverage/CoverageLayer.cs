using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage;

/// <summary>Which coverage layer a cell belongs to (docs/overhaul/growth_models.md §1.3).</summary>
public enum CoverageLayerId : int { Mat = 0, Crust = 1, Plasmodium = 2 }

[Flags]
public enum CoverageFlags : byte
{
    None = 0,
    Dead = 1 << 0,
    Rim = 1 << 1,
    Fruiting = 1 << 2,
    Sclerotium = 1 << 3,
    Front = 1 << 4,
    Boundary = 1 << 5,
}

/// <summary>
/// One 32x32-cell tile (§1.1-1.2): struct-of-arrays cell state, a previous-step snapshot for double-buffered
/// neighbour reads, and a version counter bumped on any change (for <see cref="CoverageLayer.ChangedTiles"/>).
/// </summary>
public sealed class CoverageTile
{
    public readonly int Ti, Tj;
    public const int N = CoverageSpec.CellsPerTile;

    public byte[] Occ = new byte[N];
    public float[] B = new float[N];
    public byte[] W = new byte[N];
    public ushort[] Age = new ushort[N];
    public byte[] Dorm = new byte[N];
    public byte[] Flags = new byte[N];
    public byte[] D2E = new byte[N];

    // previous-step snapshot, used for neighbour reads while the live arrays above are being written
    public byte[]? SnapOcc;
    public float[]? SnapB;

    public long Version;
    public bool Active;
    public int EmptySteps;

    // ---- MatRules perf state (docs/overhaul/growth_models.md §1.5, §10): persistent rim set and steady-tile
    // throttle, maintained incrementally by the growth rules on colonise/death rather than rescanned every step.
    /// <summary>Local indices of occupied cells that touch an empty or differently-occupied neighbour — the
    /// only cells growth rules need to consider for spread/competition. Maintained incrementally.</summary>
    public HashSet<int> Rim = new();
    /// <summary>False until this tile's <see cref="Rim"/> has had its one-time full scan.</summary>
    public bool RimReady;
    /// <summary>True once this tile's biomass/water/dormancy has reached equilibrium: physiology then runs
    /// only every 4th step (with a scaled dt) instead of every step.</summary>
    public bool Steady;
    public int SkippedPhysiologySteps;
    /// <summary>Representative (tile-centre) moisture last seen, used to detect drift past a small tolerance
    /// while <see cref="Steady"/>, so a steady tile still wakes up promptly if its environment moves.</summary>
    public double EnvMoistureCache = double.NaN;

    public CoverageTile(int ti, int tj) { Ti = ti; Tj = tj; }

    public bool IsEmpty()
    {
        for (int i = 0; i < N; i++) if (Occ[i] != 0) return false;
        return true;
    }

    public void Snapshot()
    {
        SnapOcc = (byte[])Occ.Clone();
        SnapB = (float[])B.Clone();
    }

    public void Touch() => Version++;
}

/// <summary>Read-only view of a tile's cell state, for the render contract (§11) and for tests.</summary>
public readonly struct TileView
{
    public readonly int Ti, Tj;
    public readonly byte[] Occ, W, Dorm, Flags, D2E;
    public readonly float[] B;
    public readonly ushort[] Age;
    public readonly long Version;

    public TileView(CoverageTile t)
    {
        Ti = t.Ti; Tj = t.Tj; Occ = t.Occ; W = t.W; Dorm = t.Dorm; Flags = t.Flags; D2E = t.D2E; B = t.B; Age = t.Age; Version = t.Version;
    }
}

/// <summary>
/// Sparse 32x32-cell tile raster for one coverage layer (Mat, Crust or Plasmodium). Tiles are keyed by
/// (ti, tj) in a <see cref="SortedDictionary{TKey,TValue}"/> so iteration order is always deterministic
/// regardless of allocation order (docs/overhaul/growth_models.md §1.1, §1.5).
/// </summary>
public sealed class CoverageLayer
{
    public CoverageLayerId Id { get; }
    public ulong WorldSeed { get; }
    public long Step { get; private set; }

    private readonly SortedDictionary<(int ti, int tj), CoverageTile> _tiles = new();

    public CoverageLayer(CoverageLayerId id, ulong worldSeed)
    {
        Id = id;
        WorldSeed = worldSeed;
    }

    public int TileCount => _tiles.Count;

    /// <summary>All allocated tiles, in deterministic (ti, tj) order.</summary>
    public IEnumerable<CoverageTile> Tiles => _tiles.Values;

    public bool TryGetTile(int ti, int tj, out CoverageTile? tile) => _tiles.TryGetValue((ti, tj), out tile);

    /// <summary>Allocates the tile on first write (§1.1); returns the existing tile otherwise.</summary>
    public CoverageTile GetOrCreateTile(int ti, int tj)
    {
        if (!_tiles.TryGetValue((ti, tj), out var t))
        {
            t = new CoverageTile(ti, tj);
            _tiles[(ti, tj)] = t;
        }
        return t;
    }

    // ------------------------------------------------------------------ cell access (live buffer)

    public byte GetOcc(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        return _tiles.TryGetValue((ti, tj), out var t) ? t.Occ[CoverageSpec.LocalIndex(gx, gz)] : (byte)0;
    }

    public float GetB(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        return _tiles.TryGetValue((ti, tj), out var t) ? t.B[CoverageSpec.LocalIndex(gx, gz)] : 0f;
    }

    /// <summary>Sets occupant and marks the owning tile dirty (allocating it if necessary).</summary>
    public void SetOcc(int gx, int gz, byte occ)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        var t = GetOrCreateTile(ti, tj);
        int li = CoverageSpec.LocalIndex(gx, gz);
        if (t.Occ[li] == occ) return;
        t.Occ[li] = occ;
        t.Active = true;
        t.Touch();
    }

    public void SetB(int gx, int gz, float b)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        var t = GetOrCreateTile(ti, tj);
        int li = CoverageSpec.LocalIndex(gx, gz);
        if (t.B[li] == b) return;
        t.B[li] = b;
        t.Active = true;
        t.Touch();
    }

    public void SetCell(int gx, int gz, byte occ, float b, byte w, ushort age, byte dorm, byte flags, byte d2e)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        var t = GetOrCreateTile(ti, tj);
        int li = CoverageSpec.LocalIndex(gx, gz);
        t.Occ[li] = occ; t.B[li] = b; t.W[li] = w; t.Age[li] = age; t.Dorm[li] = dorm; t.Flags[li] = flags; t.D2E[li] = d2e;
        t.Active = true;
        t.Touch();
    }

    // ------------------------------------------------------------------ double-buffered neighbour reads (§1.5)

    /// <summary>Copies live Occ/B into each active tile's snapshot. Neighbour reads during the step use the snapshot.</summary>
    public void BeginStep()
    {
        foreach (var t in _tiles.Values) t.Snapshot();
    }

    /// <summary>Previous-step occupant at (gx, gz), crossing tile borders transparently. 0 (empty) outside any tile.</summary>
    public byte SnapshotOcc(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!_tiles.TryGetValue((ti, tj), out var t) || t.SnapOcc == null) return 0;
        return t.SnapOcc[CoverageSpec.LocalIndex(gx, gz)];
    }

    /// <summary>Previous-step biomass at (gx, gz), crossing tile borders transparently. 0 outside any tile.</summary>
    public float SnapshotB(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!_tiles.TryGetValue((ti, tj), out var t) || t.SnapB == null) return 0f;
        return t.SnapB[CoverageSpec.LocalIndex(gx, gz)];
    }

    // ------------------------------------------------------------------ active set / iteration (§1.5)

    /// <summary>Tiles with any Rim/Front/non-steady cell, in deterministic order. Marked externally by rules.</summary>
    public IEnumerable<CoverageTile> ActiveTiles => _tiles.Values.Where(t => t.Active);

    public void MarkActive(int ti, int tj) => GetOrCreateTile(ti, tj).Active = true;

    public void ClearActive(CoverageTile t) => t.Active = false;

    /// <summary>Deterministic hash draw for a stochastic decision at this cell (§1.4).</summary>
    public double Hash01(int gx, int gz, long step, int purpose)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        int li = CoverageSpec.LocalIndex(gx, gz);
        return HashRng.Hash01(WorldSeed, (int)Id, ti, tj, li, step, purpose);
    }

    // ------------------------------------------------------------------ tile lifecycle / maintenance

    /// <summary>Advances the step counter and frees tiles that have been fully empty for FreeAfterEmptySteps steps.</summary>
    public void Advance()
    {
        Step++;
        var toRemove = new List<(int, int)>();
        foreach (var (key, t) in _tiles)
        {
            if (t.IsEmpty())
            {
                t.EmptySteps++;
                if (t.EmptySteps > CoverageSpec.FreeAfterEmptySteps) toRemove.Add(key);
            }
            else
            {
                t.EmptySteps = 0;
            }
        }
        foreach (var key in toRemove) _tiles.Remove(key);
    }

    // ------------------------------------------------------------------ render/change contract (§11)

    public IEnumerable<TileView> ChangedTiles(long sinceVersion) =>
        _tiles.Values.Where(t => t.Version > sinceVersion).Select(t => new TileView(t));

    // ------------------------------------------------------------------ persistence support (§9)

    /// <summary>Snapshot of every allocated tile's raw cell arrays, in deterministic tile-key order.</summary>
    public IEnumerable<CoverageTile> ExportTiles() => _tiles.Values;

    /// <summary>Rebuilds a tile from saved arrays. Used by <c>WorldSerializer</c>; not for general use.</summary>
    public void ImportTile(int ti, int tj, byte[] occ, float[] b, byte[] w, ushort[] age, byte[] dorm, byte[] flags, byte[] d2e)
    {
        var t = new CoverageTile(ti, tj) { Occ = occ, B = b, W = w, Age = age, Dorm = dorm, Flags = flags, D2E = d2e };
        _tiles[(ti, tj)] = t;
    }

    public void Clear() => _tiles.Clear();
}
