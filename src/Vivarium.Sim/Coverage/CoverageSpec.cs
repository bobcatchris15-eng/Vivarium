namespace Vivarium.Sim.Coverage;

/// <summary>Geometry constants for the fine coverage raster (moss, lichen, slime mold). See docs/overhaul/growth_models.md §1.</summary>
public static class CoverageSpec
{
    /// <summary>Fine cell size in world metres.</summary>
    public const double CellSize = 0.02;

    /// <summary>Tile edge length in cells (32x32 cells per tile).</summary>
    public const int TileEdge = 32;

    public const int CellsPerTile = TileEdge * TileEdge;

    /// <summary>Tile edge length in world metres.</summary>
    public const double TileWorldSize = TileEdge * CellSize;

    /// <summary>Steps an allocated, fully-empty tile is kept before being freed.</summary>
    public const int FreeAfterEmptySteps = 8;

    /// <summary>Floor-divide, correct for negative operands (world coordinates can be negative).</summary>
    public static int FloorDiv(int a, int b)
    {
        int q = a / b;
        int r = a % b;
        if (r != 0 && (r < 0) != (b < 0)) q--;
        return q;
    }

    public static int FloorMod(int a, int b)
    {
        int r = a % b;
        if (r != 0 && (r < 0) != (b < 0)) r += b;
        return r;
    }

    /// <summary>World position to global cell coordinates (floor).</summary>
    public static (int gx, int gz) CellOf(Core.Vec2 pos)
    {
        int gx = (int)Math.Floor(pos.X / CellSize);
        int gz = (int)Math.Floor(pos.Z / CellSize);
        return (gx, gz);
    }

    /// <summary>Global cell coordinates to the tile that owns them.</summary>
    public static (int ti, int tj) TileOf(int gx, int gz) => (FloorDiv(gx, TileEdge), FloorDiv(gz, TileEdge));

    /// <summary>Local cell index (0..CellsPerTile) within its tile.</summary>
    public static int LocalIndex(int gx, int gz)
    {
        int lx = FloorMod(gx, TileEdge);
        int lz = FloorMod(gz, TileEdge);
        return lz * TileEdge + lx;
    }
}
