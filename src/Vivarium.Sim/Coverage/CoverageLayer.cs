using System.Linq;
using System.Threading;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage;

/// <summary>Which coverage layer a cell belongs to (docs/overhaul/growth_models.md §1.3).</summary>
public enum CoverageLayerId : int { Mat = 0, Crust = 1, SurfaceFloat = 10, AlgaeBed = 11, AlgaeFloat = 12 }

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
    public byte[] Vigour = new byte[N];
    public byte[] GW = new byte[N];

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
/// Sparse 32x32-cell tile raster for one coverage layer (Mat or Crust). Tiles are keyed by
/// (ti, tj) in a <see cref="SortedDictionary{TKey,TValue}"/> so iteration order is always deterministic
/// regardless of allocation order (docs/overhaul/growth_models.md §1.1, §1.5).
/// </summary>
public sealed class CoverageLayer
{
    public CoverageLayerId Id { get; }
    public ulong WorldSeed { get; }
    public long Step { get; private set; }

    private readonly SortedDictionary<(int ti, int tj), CoverageTile> _tiles = new();
    private readonly object _tileMapGate = new();
    private readonly object _renderEpochGate = new();
    private bool _writerActive;
    private int _renderReaders;
    private long _writeEpoch;

    public CoverageLayer(CoverageLayerId id, ulong worldSeed)
    {
        Id = id;
        WorldSeed = worldSeed;
    }

    public int TileCount { get { lock (_tileMapGate) return _tiles.Count; } }

    /// <summary>Marks the exclusive async biology mutation window without holding a lock over its work.</summary>
    internal void BeginWriter()
    {
        lock (_renderEpochGate)
        {
            while (_renderReaders != 0) Monitor.Wait(_renderEpochGate);
            if (_writerActive) throw new InvalidOperationException("Coverage layer already has an active writer.");
            _writerActive = true;
            _writeEpoch++; // odd while the writer owns this layer
        }
    }

    internal void EndWriter()
    {
        lock (_renderEpochGate)
        {
            _writeEpoch++; // publish all writes before making the layer readable
            _writerActive = false;
            Monitor.PulseAll(_renderEpochGate);
        }
    }

    internal bool TryGetStableWriteEpoch(out long epoch)
    {
        lock (_renderEpochGate)
        {
            epoch = _writeEpoch;
            return !_writerActive && (epoch & 1) == 0;
        }
    }

    /// <summary>Short read lease preventing an async biology writer from starting during renderer publication.</summary>
    public bool TryBeginRenderCommit()
    {
        lock (_renderEpochGate)
        {
            if (_writerActive) return false;
            _renderReaders++;
            return true;
        }
    }

    public void EndRenderCommit()
    {
        lock (_renderEpochGate)
        {
            if (_renderReaders <= 0) throw new InvalidOperationException("Coverage render commit lease underflow.");
            _renderReaders--;
            if (_renderReaders == 0) Monitor.PulseAll(_renderEpochGate);
        }
    }

    /// <summary>All allocated tiles, in deterministic (ti, tj) order.</summary>
    public IEnumerable<CoverageTile> Tiles { get { lock (_tileMapGate) return _tiles.Values.ToArray(); } }

    public bool TryGetTile(int ti, int tj, out CoverageTile? tile)
    {
        lock (_tileMapGate) return _tiles.TryGetValue((ti, tj), out tile);
    }

    /// <summary>Allocates the tile on first write (§1.1); returns the existing tile otherwise.</summary>
    public CoverageTile GetOrCreateTile(int ti, int tj)
    {
        lock (_tileMapGate)
        {
            if (!_tiles.TryGetValue((ti, tj), out var t))
            {
                t = new CoverageTile(ti, tj);
                _tiles[(ti, tj)] = t;
            }
            return t;
        }
    }

    /// <summary>Installs a copied tile into a disposable render-input layer.</summary>
    internal void ReplaceSnapshotTile(CoverageTile tile)
    {
        lock (_tileMapGate) _tiles[(tile.Ti, tile.Tj)] = tile;
    }

    // ------------------------------------------------------------------ cell access (live buffer)

    public byte GetOcc(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        return TryGetTile(ti, tj, out var t) ? t!.Occ[CoverageSpec.LocalIndex(gx, gz)] : (byte)0;
    }

    public float GetB(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        return TryGetTile(ti, tj, out var t) ? t!.B[CoverageSpec.LocalIndex(gx, gz)] : 0f;
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
        foreach (var t in Tiles) t.Snapshot();
    }

    /// <summary>Previous-step occupant at (gx, gz), crossing tile borders transparently. 0 (empty) outside any tile.</summary>
    public byte SnapshotOcc(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!TryGetTile(ti, tj, out var t) || t!.SnapOcc == null) return 0;
        return t!.SnapOcc[CoverageSpec.LocalIndex(gx, gz)];
    }

    /// <summary>Previous-step biomass at (gx, gz), crossing tile borders transparently. 0 outside any tile.</summary>
    public float SnapshotB(int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (!TryGetTile(ti, tj, out var t) || t!.SnapB == null) return 0f;
        return t!.SnapB[CoverageSpec.LocalIndex(gx, gz)];
    }

    // ------------------------------------------------------------------ active set / iteration (§1.5)

    /// <summary>Tiles with any Rim/Front/non-steady cell, in deterministic order. Marked externally by rules.</summary>
    public IEnumerable<CoverageTile> ActiveTiles => Tiles.Where(t => t.Active);

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
        lock (_tileMapGate)
        {
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
    }

    // ------------------------------------------------------------------ render/change contract (§11)

    public IEnumerable<TileView> ChangedTiles(long sinceVersion) =>
        Tiles.Where(t => t.Version > sinceVersion).Select(t => new TileView(t));

    // ------------------------------------------------------------------ persistence support (§9)

    /// <summary>Snapshot of every allocated tile's raw cell arrays, in deterministic tile-key order.</summary>
    public IEnumerable<CoverageTile> ExportTiles() => Tiles;

    /// <summary>Rebuilds a tile from saved arrays. Used by <c>WorldSerializer</c>; not for general use.</summary>
    public void ImportTile(int ti, int tj, byte[] occ, float[] b, byte[] w, ushort[] age, byte[] dorm, byte[] flags, byte[] d2e,
        bool steady = false, int skippedPhysiologySteps = 0, double envMoistureCache = double.NaN,
        byte[]? vigour = null, byte[]? growthWater = null)
    {
        var t = new CoverageTile(ti, tj)
        {
            Occ = occ, B = b, W = w, Age = age, Dorm = dorm, Flags = flags, D2E = d2e,
            Vigour = vigour ?? new byte[CoverageTile.N],
            GW = growthWater ?? new byte[CoverageTile.N],
            Steady = steady,
            SkippedPhysiologySteps = skippedPhysiologySteps,
            EnvMoistureCache = envMoistureCache,
        };
        lock (_tileMapGate) _tiles[(ti, tj)] = t;
    }

    public void Clear() { lock (_tileMapGate) _tiles.Clear(); }
}
