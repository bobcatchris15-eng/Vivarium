using Vivarium.Sim.Coverage;

namespace Vivarium.Sim.Tests.GrowthLab;

/// <summary>
/// A toy isotropic Eden-growth rule (not a real species model): each occupied cell's empty 4-neighbours
/// colonise with a fixed probability, using the layer's double-buffered snapshot and hash RNG. Exists only
/// to exercise the growth-lab harness end to end (§12) and the determinism contract (§1.4-1.5).
/// </summary>
public static class ToyDiscRule
{
    private static readonly (int dx, int dz)[] Neighbours8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>
    /// Isotropic Eden-style front: an empty cell adjacent to the colony colonises with a probability that
    /// falls off with its Euclidean distance from the seed relative to the current front radius. This keeps
    /// the front round (unlike a flat per-neighbour probability, which fills the square lattice's corners
    /// fastest and gives a diamond/square, not a disc) while still exercising the snapshot, hash and
    /// active-tile machinery like any other coverage rule.
    /// </summary>
    public static void Step(CoverageLayer layer, long step, int half, double p)
    {
        layer.BeginStep();
        double frontRadius = Math.Sqrt(step + 1);
        var toColonise = new List<(int gx, int gz)>();
        for (int gz = -half - 1; gz <= half + 1; gz++)
        for (int gx = -half - 1; gx <= half + 1; gx++)
        {
            if (layer.SnapshotOcc(gx, gz) != 0) continue;
            bool hasOccupiedNeighbour = false;
            foreach (var (dx, dz) in Neighbours8)
                if (layer.SnapshotOcc(gx + dx, gz + dz) != 0) { hasOccupiedNeighbour = true; break; }
            if (!hasOccupiedNeighbour) continue;
            double dist = Math.Sqrt(gx * gx + gz * gz);
            double pCell = p * Math.Clamp(1.0 - (dist - frontRadius), 0.0, 1.0);
            if (pCell <= 0) continue;
            double u = layer.Hash01(gx, gz, step, purpose: 0);
            if (u < pCell) toColonise.Add((gx, gz));
        }
        foreach (var (gx, gz) in toColonise) layer.SetOcc(gx, gz, 1);
        layer.Advance();
    }
}
