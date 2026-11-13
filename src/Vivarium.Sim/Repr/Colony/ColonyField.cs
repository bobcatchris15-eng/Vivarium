using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// One 32 x 32 block of colony cells, allocated on first occupancy. Dense storage is packed to 47 bytes per cell:
/// occupant arrays (2 per cell, index cell * 2 + slot) hold species, coverage (ushort, x/65535), biomass (double,
/// kg dry per m2 of ground cell: exact so budgets close), thickness (ushort micrometres), health (byte),
/// front age and dryness (ushort, 600 s units, saturating), last suitability (byte). Per cell: flags, horizon,
/// capacity, slope, light (bytes) and cumulative decomposed litter (float). Rim spread accumulators are sparse.
public sealed class ColonyTile
{
    public const int Side = 32;
    public const int N = Side * Side;

    public const byte FUsable = 1, FValid = 2, FSteady = 4, FSandy = 8, FDorm0 = 16, FDorm1 = 32;

    public readonly int Tx, Tz;
    public ulong Version;
    /// Coverage change accumulated since the last publish (sum of |dC| over cells; publish when >= threshold).
    public double ChangeSinceCheck;
    public double MinY = double.MaxValue, MaxY = double.MinValue;

    // occupants
    public readonly byte[] Species = new byte[2 * N];
    public readonly ushort[] CoverQ = new ushort[2 * N];
    public readonly double[] Biomass = new double[2 * N];
    public readonly ushort[] ThickUm = new ushort[2 * N];
    public readonly byte[] HealthQ = new byte[2 * N];
    public readonly ushort[] AgeU = new ushort[2 * N];
    public readonly ushort[] DryU = new ushort[2 * N];
    public readonly byte[] SuitQ = new byte[2 * N];

    // per cell
    public readonly byte[] Flags = new byte[N];
    public readonly byte[] Horizon = new byte[N];
    public readonly byte[] CapacityQ = new byte[N];
    public readonly byte[] SlopeQ = new byte[N];
    public readonly byte[] LightQ = new byte[N];
    public readonly float[] Decomposed = new float[N];

    /// Sparse rim state: cell -> spread accumulators (metres) indexed slot * 8 + direction.
    public readonly Dictionary<int, double[]> Rim = new();

    public int Occupied, SteadyCount;

    public ColonyTile(int tx, int tz) { Tx = tx; Tz = tz; }

    public double Cover(int k) => CoverQ[k] / 65535.0;
    public double Health(int k) => HealthQ[k] / 255.0;
    public double Thick(int k) => ThickUm[k] * 1e-6;
    public double Light(int cell) => LightQ[cell] / 255.0;
    public double Slope(int cell) => SlopeQ[cell] / 255.0;
    public double Capacity(int cell) => CapacityQ[cell] / 255.0;
    public bool Has(int cell, byte flag) => (Flags[cell] & flag) != 0;
    public void SetFlag(int cell, byte flag, bool on)
    {
        if (on) Flags[cell] |= flag; else Flags[cell] &= (byte)~flag;
    }
    public bool Dormant(int k) => (Flags[k >> 1] & ((k & 1) == 0 ? FDorm0 : FDorm1)) != 0;
    public void SetDormant(int k, bool on) => SetFlag(k >> 1, (k & 1) == 0 ? FDorm0 : FDorm1, on);
}

/// Read-only view of a tile for rendering later. Occupant accessors take k = cell * 2 + slot.
public sealed class ColonyTileView
{
    private readonly ColonyTile _t;
    internal ColonyTileView(ColonyTile t) { _t = t; }
    public int Tx => _t.Tx;
    public int Tz => _t.Tz;
    public ulong Version => _t.Version;
    public byte Species(int k) => _t.Species[k];
    public double Coverage(int k) => _t.Cover(k);
    public double Biomass(int k) => _t.Biomass[k];
    public double Thickness(int k) => _t.Thick(k);
    public double Health(int k) => _t.Health(k);
    public bool Dormant(int k) => _t.Dormant(k);
    public bool Steady(int cell) => _t.Has(cell, ColonyTile.FSteady);
    public double Decomposed(int cell) => _t.Decomposed[cell];
}

/// Sparse lattice over the ground plane. Cell (ix, iz) covers [ix*cell, (ix+1)*cell) in X and Z.
public sealed class ColonyField
{
    private readonly Dictionary<long, ColonyTile> _map = new();
    private readonly List<ColonyTile> _tiles = new();

    public double CellSize { get; }
    /// Tiles in allocation order (deterministic for identical operation sequences).
    public IReadOnlyList<ColonyTile> Tiles => _tiles;

    public ColonyField(double cellSize)
    {
        if (!(cellSize > 0)) throw new ArgumentOutOfRangeException(nameof(cellSize));
        CellSize = cellSize;
    }

    public static long TileKey(int tx, int tz) => ((long)tx << 32) ^ (uint)tz;
    public static long CellKey(int ix, int iz) => ((long)ix << 32) ^ (uint)iz;
    public static int CellX(long key) => (int)(key >> 32);
    public static int CellZ(long key) => (int)(uint)(key & 0xFFFFFFFFL);
    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    public static int CellIndex(int lx, int lz) => lx + lz * ColonyTile.Side;

    public (int Ix, int Iz) CellOf(Vec2 p) => ((int)Math.Floor(p.X / CellSize), (int)Math.Floor(p.Z / CellSize));
    public Vec2 CellCentre(int ix, int iz) => new((ix + 0.5) * CellSize, (iz + 0.5) * CellSize);

    public ColonyTile? FindTile(int tx, int tz) => _map.TryGetValue(TileKey(tx, tz), out var t) ? t : null;

    public ColonyTile GetOrCreateTile(int tx, int tz)
    {
        long k = TileKey(tx, tz);
        if (!_map.TryGetValue(k, out var t))
        {
            t = new ColonyTile(tx, tz);
            _map[k] = t;
            _tiles.Add(t);
        }
        return t;
    }

    /// Tile and cell index for a global cell, or null if its tile is not allocated.
    public ColonyTile? FindCell(int ix, int iz, out int cell)
    {
        int tx = FloorDiv(ix, ColonyTile.Side), tz = FloorDiv(iz, ColonyTile.Side);
        cell = CellIndex(ix - tx * ColonyTile.Side, iz - tz * ColonyTile.Side);
        return FindTile(tx, tz);
    }

    /// Like FindCell but allocates the tile.
    public ColonyTile GetOrCreateCell(int ix, int iz, out int cell)
    {
        int tx = FloorDiv(ix, ColonyTile.Side), tz = FloorDiv(iz, ColonyTile.Side);
        cell = CellIndex(ix - tx * ColonyTile.Side, iz - tz * ColonyTile.Side);
        return GetOrCreateTile(tx, tz);
    }

    public static void CellCoords(ColonyTile t, int cell, out int ix, out int iz)
    {
        ix = t.Tx * ColonyTile.Side + cell % ColonyTile.Side;
        iz = t.Tz * ColonyTile.Side + cell / ColonyTile.Side;
    }

    public Aabb TileBounds(ColonyTile t)
    {
        double x0 = t.Tx * ColonyTile.Side * CellSize, z0 = t.Tz * ColonyTile.Side * CellSize;
        double x1 = x0 + ColonyTile.Side * CellSize, z1 = z0 + ColonyTile.Side * CellSize;
        bool hasY = t.MinY <= t.MaxY;
        double y0 = hasY ? t.MinY - 0.02 : 0;
        double y1 = hasY ? t.MaxY + 0.06 : 0.1;
        return new Aabb(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
    }
}
