using Vivarium.Sim.Fields;

namespace Vivarium.Sim.Diagnostics;

/// <summary>Read-only spatial habitat diagnostics; no ecology state or history is mutated.</summary>
public static class HabitatDiagnostics
{
    public readonly record struct FieldSummary(int Cells, double Min, double Max, double Mean,
        double MaximumNeighbourGradient, int SharpEdges);

    public readonly record struct DirectionalOverlap(int AFavorable, int BFavorable, int Shared,
        double FractionAAlsoB, double FractionBAlsoA);

    /// <summary>Measures adjacent-cell gradients in field units per metre, excluding domain boundaries.</summary>
    public static FieldSummary Summarize(ScalarField field, double sharpGradientThreshold)
    {
        var grid = field.Grid;
        double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0, maxGradient = 0;
        int cells = 0, sharp = 0;
        foreach (int i in grid.DomainCells)
        {
            double v = field.Values[i];
            min = Math.Min(min, v); max = Math.Max(max, v); sum += v; cells++;
            int x = i % grid.Nx, z = i / grid.Nx;
            if (x + 1 < grid.Nx && grid.InDomain(i + 1))
                Edge(Math.Abs(v - field.Values[i + 1]) / grid.CellSize);
            if (z + 1 < grid.Nz && grid.InDomain(i + grid.Nx))
                Edge(Math.Abs(v - field.Values[i + grid.Nx]) / grid.CellSize);
        }
        return cells == 0 ? new(0, 0, 0, 0, 0, 0)
            : new(cells, min, max, sum / cells, maxGradient, sharp);

        void Edge(double gradient)
        {
            maxGradient = Math.Max(maxGradient, gradient);
            if (gradient >= sharpGradientThreshold) sharp++;
        }
    }

    /// <summary>Directional intersection of potential favorable habitat, not observed occupancy.</summary>
    public static DirectionalOverlap Overlap(ReadOnlySpan<double> suitabilityA,
        ReadOnlySpan<double> suitabilityB, double threshold)
    {
        if (suitabilityA.Length != suitabilityB.Length)
            throw new ArgumentException("Suitability maps must have identical cell counts.");
        int a = 0, b = 0, both = 0;
        for (int i = 0; i < suitabilityA.Length; i++)
        {
            bool fa = suitabilityA[i] >= threshold, fb = suitabilityB[i] >= threshold;
            if (fa) a++;
            if (fb) b++;
            if (fa && fb) both++;
        }
        return new(a, b, both, a == 0 ? 0 : (double)both / a,
            b == 0 ? 0 : (double)both / b);
    }
}
