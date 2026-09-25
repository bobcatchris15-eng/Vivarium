using Vivarium.Sim.Fields;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>Projects fine bed-algae biomass into the coarse Biofilm food field and applies grazing to its source.</summary>
public static class AquaticBiofilm
{
    public static void ProjectCell(CoverageLayer bed, ScalarField biofilm, int cell)
    {
        if (!biofilm.Grid.InDomain(cell)) return;
        var cells = FineCells(biofilm.Grid, cell);
        double total = 0;
        foreach (var (x, z) in cells) total += bed.GetB(x, z);
        biofilm[cell] = cells.Count == 0 ? 0 : total / cells.Count * biofilm.Max;
    }

    public static void Project(CoverageLayer bed, ScalarField biofilm)
    {
        foreach (int cell in biofilm.Grid.DomainCells) ProjectCell(bed, biofilm, cell);
    }

    /// <summary>Returns actual food removed; the field is refreshed immediately so it cannot be consumed twice.</summary>
    public static double GrazeCell(CoverageLayer bed, ScalarField biofilm, int cell, double want)
    {
        if (want <= 0 || !biofilm.Grid.InDomain(cell)) return 0;
        var cells = FineCells(biofilm.Grid, cell);
        if (cells.Count == 0) return 0;
        double biomass = 0;
        foreach (var (x, z) in cells) biomass += bed.GetB(x, z);
        double available = biomass / cells.Count * biofilm.Max;
        double got = Math.Min(want, available);
        if (got <= 0) { biofilm[cell] = 0; return 0; }
        double retained = (available - got) / available;
        foreach (var (x, z) in cells)
        {
            float before = bed.GetB(x, z);
            if (before <= 0) continue;
            float after = (float)(before * retained);
            bed.SetB(x, z, after);
            if (after <= 1e-6f) bed.SetOcc(x, z, 0);
        }
        ProjectCell(bed, biofilm, cell);
        return got;
    }

    private static List<(int x, int z)> FineCells(GridSpec grid, int cell)
    {
        var center = grid.CellCenter(cell);
        double half = grid.CellSize * 0.5;
        double minX = center.X - half, maxX = center.X + half;
        double minZ = center.Z - half, maxZ = center.Z + half;
        double s = CoverageSpec.CellSize;
        var result = new List<(int, int)>();
        for (int z = (int)Math.Floor(minZ / s); z <= (int)Math.Ceiling(maxZ / s); z++)
        for (int x = (int)Math.Floor(minX / s); x <= (int)Math.Ceiling(maxX / s); x++)
        {
            double px = (x + 0.5) * s, pz = (z + 0.5) * s;
            if (px >= minX - 1e-10 && px < maxX - 1e-10 && pz >= minZ - 1e-10 && pz < maxZ - 1e-10)
                result.Add((x, z));
        }
        return result;
    }
}
