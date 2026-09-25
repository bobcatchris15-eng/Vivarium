using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage;

/// <summary>
/// Per environment-grid-cell "last disturbed" clock (docs/overhaul/growth_models.md §2): sculpt, tool actions
/// and fauna burrowing reset a cell's age to zero; ground that has gone undisturbed for at least
/// <see cref="StableAfterSeconds"/> of simulated time reads as StableSoil rather than LooseSoil. Dense over
/// <see cref="GridSpec.DomainCells"/>, same layout as <see cref="ScalarField"/>, so it saves the same way.
/// </summary>
public sealed class SubstrateStability
{
    /// <summary>Default time a cell must sit undisturbed before it counts as StableSoil.</summary>
    public const double StableAfterSeconds = 3 * Content.SimUnits.Day;

    public GridSpec Grid { get; }

    /// <summary>Simulated seconds (<see cref="Time.SimClock.SimSeconds"/>) at which each cell was last disturbed.
    /// Defaults to 0 (world genesis): freshly generated ground stabilizes over time exactly like sculpted ground.</summary>
    public double[] DisturbedAt { get; }

    public SubstrateStability(GridSpec grid)
    {
        Grid = grid;
        DisturbedAt = new double[grid.Count];
    }

    /// <summary>Marks every cell within radius of centre as disturbed as of <paramref name="nowSeconds"/>.</summary>
    public void Reset(Vec2 centre, double radius, double nowSeconds)
    {
        foreach (int idx in Grid.CellsInRadius(centre, radius)) DisturbedAt[idx] = nowSeconds;
    }

    /// <summary>Marks a single cell (by world position) as disturbed. Used for point disturbances (burrowing).</summary>
    public void ResetAt(Vec2 p, double nowSeconds)
    {
        int c = Grid.NearestDomainCell(p);
        if (c >= 0) DisturbedAt[c] = nowSeconds;
    }

    /// <summary>Age since last disturbance at the nearest grid cell to p (simulated seconds).</summary>
    public double AgeAt(Vec2 p, double nowSeconds)
    {
        int c = Grid.NearestDomainCell(p);
        return c >= 0 ? Math.Max(0, nowSeconds - DisturbedAt[c]) : 0;
    }

    /// <summary>True once the ground at p has been undisturbed for at least <paramref name="thresholdSeconds"/>.</summary>
    public bool IsStable(Vec2 p, double nowSeconds, double thresholdSeconds = StableAfterSeconds) =>
        AgeAt(p, nowSeconds) >= thresholdSeconds;

    // ------------------------------------------------------------------ persistence support

    /// <summary>Dense in-domain values, same ordering as <see cref="ScalarField.ExportDomainValues"/>.</summary>
    public double[] ExportDomainValues()
    {
        var a = new double[Grid.DomainCells.Length];
        for (int k = 0; k < a.Length; k++) a[k] = DisturbedAt[Grid.DomainCells[k]];
        return a;
    }

    public void ImportDomainValues(double[] a)
    {
        if (a.Length != Grid.DomainCells.Length)
            throw new InvalidDataException($"substrate stability expects {Grid.DomainCells.Length} values, got {a.Length}.");
        for (int k = 0; k < a.Length; k++)
        {
            double v = a[k];
            if (!double.IsFinite(v)) throw new InvalidDataException($"substrate stability value {k} is not finite.");
            DisturbedAt[Grid.DomainCells[k]] = v;
        }
    }
}
