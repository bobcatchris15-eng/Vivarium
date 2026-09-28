using Vivarium.Sim.Fields;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>Projects fine bed-algae biomass into the coarse Biofilm food field and applies grazing to its source.</summary>
public static class AquaticBiofilm
{
    public static void ProjectCell(CoverageLayer bed, ScalarField biofilm, int cell)
    {
        if (!biofilm.Grid.InDomain(cell)) return;
        var bounds = new FineBounds(biofilm.Grid, cell);
        double total = 0;
        int count = 0;
        int lastTi = int.MinValue, lastTj = int.MinValue;
        CoverageTile? tile = null;
        for (int z = bounds.MinZ; z <= bounds.MaxZ; z++)
        for (int x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            if (!bounds.Contains(x, z)) continue;
            var (ti, tj) = CoverageSpec.TileOf(x, z);
            if (ti != lastTi || tj != lastTj)
            {
                bed.TryGetTile(ti, tj, out tile);
                lastTi = ti; lastTj = tj;
            }
            if (tile != null) total += tile.B[CoverageSpec.LocalIndex(x, z)];
            count++;
        }
        biofilm[cell] = count == 0 ? 0 : total / count * biofilm.Max;
    }

    public static void Project(CoverageLayer bed, ScalarField biofilm)
    {
        foreach (int cell in biofilm.Grid.DomainCells) ProjectCell(bed, biofilm, cell);
    }

    /// <summary>Returns actual food removed; the field is refreshed immediately so it cannot be consumed twice.</summary>
    public static double GrazeCell(CoverageLayer bed, ScalarField biofilm, int cell, double want)
    {
        if (want <= 0 || !biofilm.Grid.InDomain(cell)) return 0;
        var bounds = new FineBounds(biofilm.Grid, cell);
        int count = 0;
        double biomass = 0;
        int lastTi = int.MinValue, lastTj = int.MinValue;
        CoverageTile? tile = null;
        for (int z = bounds.MinZ; z <= bounds.MaxZ; z++)
        for (int x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            if (!bounds.Contains(x, z)) continue;
            var (ti, tj) = CoverageSpec.TileOf(x, z);
            if (ti != lastTi || tj != lastTj)
            {
                bed.TryGetTile(ti, tj, out tile);
                lastTi = ti; lastTj = tj;
            }
            if (tile != null) biomass += tile.B[CoverageSpec.LocalIndex(x, z)];
            count++;
        }
        if (count == 0) return 0;
        double available = biomass / count * biofilm.Max;
        double got = Math.Min(want, available);
        if (got <= 0) { biofilm[cell] = 0; return 0; }
        double retained = (available - got) / available;
        for (int z = bounds.MinZ; z <= bounds.MaxZ; z++)
        for (int x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            if (!bounds.Contains(x, z)) continue;
            float before = bed.GetB(x, z);
            if (before <= 0) continue;
            float after = (float)(before * retained);
            bed.SetB(x, z, after);
            if (after <= 1e-6f) bed.SetOcc(x, z, 0);
        }
        ProjectCell(bed, biofilm, cell);
        return got;
    }

    private readonly struct FineBounds
    {
        public readonly int MinX, MaxX, MinZ, MaxZ;
        private readonly double _minX, _maxX, _minZ, _maxZ;

        public FineBounds(GridSpec grid, int cell)
        {
            var center = grid.CellCenter(cell);
            double half = grid.CellSize * 0.5;
            _minX = center.X - half; _maxX = center.X + half;
            _minZ = center.Z - half; _maxZ = center.Z + half;
            double s = CoverageSpec.CellSize;
            MinX = (int)Math.Floor(_minX / s); MaxX = (int)Math.Ceiling(_maxX / s);
            MinZ = (int)Math.Floor(_minZ / s); MaxZ = (int)Math.Ceiling(_maxZ / s);
        }

        public bool Contains(int x, int z)
        {
            double s = CoverageSpec.CellSize;
            double px = (x + 0.5) * s, pz = (z + 0.5) * s;
            return px >= _minX - 1e-10 && px < _maxX - 1e-10 &&
                   pz >= _minZ - 1e-10 && pz < _maxZ - 1e-10;
        }
    }
}
