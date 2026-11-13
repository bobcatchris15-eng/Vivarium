using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// One 32 x 32 block of colony cells, allocated on first occupancy. Occupant arrays are indexed
/// cell * 2 + slot (two occupants per cell); cell arrays are indexed by cell = lx + lz * Side.
/// Biomass is kg dry per m2 OF GROUND CELL (coverage already included), so cell mass = Biomass * cellArea.
public sealed class ColonyTile
{
    public const int Side = 32;
    public const int N = Side * Side;

    public readonly int Tx, Tz;
    public ulong Version;

    // occupants (2 per cell)
    public readonly byte[] Species = new byte[2 * N];      // 0 = none, else species index + 1
    public readonly double[] Coverage = new double[2 * N]; // 0..1 fraction of the cell
    public readonly double[] Biomass = new double[2 * N];  // kg dry / m2 of cell
    public readonly double[] Thickness = new double[2 * N];
    public readonly double[] Health = new double[2 * N];
    public readonly double[] FrontAge = new double[2 * N]; // sim-seconds since arrival
    public readonly double[] Dryness = new double[2 * N];  // sim-seconds of continuous dryness (decays when wet)
    public readonly bool[] Dormant = new bool[2 * N];
    public readonly double[] LastSuit = new double[2 * N];
    public readonly double[] LastBiomass = new double[2 * N];
    public readonly double[] PubCoverage = new double[2 * N];  // last published values (dirty threshold)
    public readonly double[] PubThickness = new double[2 * N];

    // per cell
    public readonly bool[] Steady = new bool[N];
    public readonly bool[] Usable = new bool[N];
    public readonly bool[] HabitatValid = new bool[N];
    public readonly int[] Column = new int[N];
    public readonly SubstrateSample[] Habitat = new SubstrateSample[N];
    public readonly double[] Height = new double[N];
    public readonly double[] Steepness = new double[N];
    public readonly double[] Light = new double[N];
    public readonly double[] Decomposed = new double[N];   // cumulative kg decomposed in the cell (for soil organic matter)

    // spread accumulators and jitter per (cell, slot, direction)
    public readonly double[] Spread = new double[16 * N];
    public readonly double[] Jitter = new double[16 * N];

    public readonly HashSet<int> Rim = new();
    public int Occupied, SteadyCount;
    public double MinY = double.MaxValue, MaxY = double.MinValue;

    public ColonyTile(int tx, int tz)
    {
        Tx = tx; Tz = tz;
        Array.Fill(Column, -1);
    }
}

/// Read-only view of a tile's raw arrays (for rendering later). Occupant arrays use index cell * 2 + slot.
public sealed class ColonyTileView
{
    private readonly ColonyTile _t;
    internal ColonyTileView(ColonyTile t) { _t = t; }
    public int Tx => _t.Tx;
    public int Tz => _t.Tz;
    public ulong Version => _t.Version;
    public IReadOnlyList<byte> Species => _t.Species;
    public IReadOnlyList<double> Coverage => _t.Coverage;
    public IReadOnlyList<double> Biomass => _t.Biomass;
    public IReadOnlyList<double> Thickness => _t.Thickness;
    public IReadOnlyList<double> Health => _t.Health;
    public IReadOnlyList<double> FrontAge => _t.FrontAge;
    public IReadOnlyList<bool> Dormant => _t.Dormant;
    public IReadOnlyList<double> Decomposed => _t.Decomposed;
    public IReadOnlyList<bool> Steady => _t.Steady;
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

    public static int CellIndex(int lx, int lz) => lx + lz * ColonyTile.Side;

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
        double y0 = t.MinY <= t.MaxY ? t.MinY - 0.02 : 0;
        double y1 = t.MinY <= t.MaxY ? t.MaxY + 0.06 : 0.1;
        return new Aabb(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
    }
}
