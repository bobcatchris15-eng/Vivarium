using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Ecology;

/// <summary>
/// Visible surface organic matter between living flora and bioavailable detritus. Mass is stored on the
/// environment grid; close litter pieces are a deterministic rendering of these reservoirs, never entities.
/// </summary>
public sealed class LitterSystem
{
    private readonly VivariumWorld _world;
    public double[] FineMass { get; }
    public double[] CoarseMass { get; }
    public long Revision { get; private set; }

    public LitterSystem(VivariumWorld world)
    {
        _world = world;
        FineMass = new double[world.Grid.Count];
        CoarseMass = new double[world.Grid.Count];
    }

    /// <summary>Deposit surface matter across a plant's footprint. The returned amount is exactly the mass stored.</summary>
    public double Deposit(Vec2 centre, double fine, double coarse = 0, double radius = 0)
    {
        if (!double.IsFinite(fine) || !double.IsFinite(coarse) || fine < 0 || coarse < 0)
            throw new ArgumentOutOfRangeException(nameof(fine), "Litter mass must be finite and nonnegative.");
        double total = fine + coarse;
        if (total <= 0) return 0;
        var grid = _world.Grid;
        int nearest = grid.NearestDomainCell(centre);
        if (nearest < 0) return 0;
        if (radius <= grid.CellSize * 0.5)
        {
            FineMass[nearest] += fine;
            CoarseMass[nearest] += coarse;
        }
        else
        {
            // Compact radial kernel spreads tree shedding below the crown without adding one leaf entity.
            var cells = grid.CellsInRadius(centre, radius).ToArray();
            if (cells.Length == 0) cells = new[] { nearest };
            var weights = new double[cells.Length];
            double sum = 0;
            for (int i = 0; i < cells.Length; i++)
                sum += weights[i] = Math.Max(0.02, 1.0 - Vec2.Distance(grid.CellCenter(cells[i]), centre) / radius);
            double addedFine = 0, addedCoarse = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                double f = i == cells.Length - 1 ? fine - addedFine : fine * weights[i] / sum;
                double c = i == cells.Length - 1 ? coarse - addedCoarse : coarse * weights[i] / sum;
                FineMass[cells[i]] += f;
                CoarseMass[cells[i]] += c;
                addedFine += f; addedCoarse += c;
            }
        }
        Revision++;
        _world.Tally.LitterDeposited += total;
        return total;
    }

    /// <summary>Baseline microbial breakdown. Detritus capacity limits transfer, preserving excess litter mass.</summary>
    public void Step(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return;
        bool changed = false;
        foreach (int idx in _world.Grid.DomainCells)
        {
            double moisture = _world.Fields.Moisture.Values[idx];
            double light = _world.Fields.Light.Values[idx];
            double environment = (0.35 + moisture * 1.15) * (1.15 - light * 0.3);
            changed |= Decay(FineMass, idx, Math.Log(2) / (20 * SimUnits.Day) * environment, dt);
            changed |= Decay(CoarseMass, idx, Math.Log(2) / (140 * SimUnits.Day) * environment, dt);
        }
        if (changed) Revision++;
    }

    private bool Decay(double[] mass, int idx, double rate, double dt)
    {
        double current = mass[idx];
        if (current <= 0) return false;
        double desired = current * (1 - Math.Exp(-rate * dt));
        double transferred = _world.Fields.Detritus.Add(idx, desired);
        if (transferred <= 0) return false;
        mass[idx] = Math.Max(0, current - transferred);
        _world.Tally.LitterToDetritus += transferred;
        return true;
    }

    public void Restore(double[] fine, double[] coarse)
    {
        if (fine.Length != _world.Grid.DomainCells.Length || coarse.Length != fine.Length)
            throw new InvalidDataException("litter grid size does not match world");
        for (int i = 0; i < fine.Length; i++)
        {
            if (!double.IsFinite(fine[i]) || fine[i] < 0 || !double.IsFinite(coarse[i]) || coarse[i] < 0)
                throw new InvalidDataException($"litter cell {i} contains invalid mass");
            int idx = _world.Grid.DomainCells[i];
            FineMass[idx] = fine[i];
            CoarseMass[idx] = coarse[i];
        }
        Revision++;
    }

    public double[] ExportFine() => _world.Grid.DomainCells.Select(idx => FineMass[idx]).ToArray();
    public double[] ExportCoarse() => _world.Grid.DomainCells.Select(idx => CoarseMass[idx]).ToArray();

    public bool AllFinite() => _world.Grid.DomainCells.All(idx =>
        double.IsFinite(FineMass[idx]) && FineMass[idx] >= 0 && double.IsFinite(CoarseMass[idx]) && CoarseMass[idx] >= 0);
}
