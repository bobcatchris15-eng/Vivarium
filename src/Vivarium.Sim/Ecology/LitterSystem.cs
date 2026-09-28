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
    private readonly List<Flora.FloraIndividual> _nearbyDecomposers = new();
    public double[] FineMass { get; }
    public double[] CoarseMass { get; }
    public double[] FruitMass { get; }
    public long Revision { get; private set; }

    public LitterSystem(VivariumWorld world)
    {
        _world = world;
        FineMass = new double[world.Grid.Count];
        CoarseMass = new double[world.Grid.Count];
        FruitMass = new double[world.Grid.Count];
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

    public double DepositFruit(Vec2 centre, double mass, double radius = 0.12)
    {
        if (!double.IsFinite(mass) || mass < 0) throw new ArgumentOutOfRangeException(nameof(mass));
        if (mass <= 0) return 0;
        var grid = _world.Grid;
        var cells = radius <= grid.CellSize * 0.5 ? new[] { grid.NearestDomainCell(centre) } : grid.CellsInRadius(centre, radius).ToArray();
        cells = cells.Where(c => c >= 0).ToArray();
        if (cells.Length == 0) return 0;
        double each = mass / cells.Length, added = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            double part = i == cells.Length - 1 ? mass - added : each;
            FruitMass[cells[i]] += part; added += part;
        }
        Revision++; _world.Tally.FruitDropped += mass;
        return mass;
    }

    /// <summary>Baseline microbial breakdown. Detritus capacity limits transfer, preserving excess litter mass.</summary>
    public void Step(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return;
        bool changed = false;
        foreach (int idx in _world.Grid.DomainCells)
        {
            double fine = FineMass[idx], coarse = CoarseMass[idx], fruit = FruitMass[idx];
            if (fine <= 0 && coarse <= 0 && fruit <= 0) continue;
            double moisture = _world.Fields.Moisture.Values[idx];
            double light = _world.Fields.Light.Values[idx];
            double environment = (0.35 + moisture * 1.15) * (1.15 - light * 0.3);
            var bonus = DecomposerBonus(idx);
            if (fruit > 0)
            {
                double fruitRate = Math.Log(2) / (6 * SimUnits.Day) * environment * bonus.Fruit;
                double rotted = fruit * (1 - Math.Exp(-fruitRate * dt));
                FruitMass[idx] = Math.Max(0, fruit - rotted);
                FineMass[idx] += rotted;
                if (rotted > 0) { _world.Tally.FruitToLitter += rotted; changed = true; }
            }
            changed |= Decay(FineMass, idx, Math.Log(2) / (20 * SimUnits.Day) * environment * bonus.Fine, dt);
            changed |= Decay(CoarseMass, idx, Math.Log(2) / (140 * SimUnits.Day) * environment * bonus.Coarse, dt);
        }
        if (changed) Revision++;
    }

    private (double Fine, double Coarse, double Fruit) DecomposerBonus(int idx)
    {
        _nearbyDecomposers.Clear();
        var p = _world.Grid.CellCenter(idx);
        _world.Flora.Index.Query(p, 1.2, _nearbyDecomposers);
        double fine = 1, coarse = 1, fruit = 1;
        foreach (var f in _nearbyDecomposers)
        {
            var sp = _world.Content.FloraOrThrow(f.SpeciesId);
            if (!sp.Decomposer) continue;
            double activity = 0.25 + 0.75 * f.BiomassFraction(sp);
            if (sp.Decomposition is { } dc)
            {
                if (Vec2.DistanceSq(p, f.Position) > dc.Radius * dc.Radius) continue;
                fine += Math.Max(-0.9, dc.FineMultiplier - 1) * activity;
                coarse += Math.Max(-0.9, dc.CoarseMultiplier - 1) * activity;
                fruit += Math.Max(-0.9, dc.FruitMultiplier - 1) * activity;
            }
            else if (sp.Shape == "bracket") coarse += 1.4 * activity;
            else if (sp.Archetype == "slime_mold") { fine += 0.35 * activity; coarse += 0.15 * activity; fruit += 0.2 * activity; }
            else { fine += 0.85 * activity; fruit += 0.35 * activity; }
        }
        return (Math.Clamp(fine, 0.1, 4.0), Math.Clamp(coarse, 0.1, 4.0), Math.Clamp(fruit, 0.1, 4.0));
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

    public void Restore(double[] fine, double[] coarse, double[]? fruit = null)
    {
        if (fine.Length != _world.Grid.DomainCells.Length || coarse.Length != fine.Length || (fruit != null && fruit.Length != fine.Length))
            throw new InvalidDataException("litter grid size does not match world");
        for (int i = 0; i < fine.Length; i++)
        {
            if (!double.IsFinite(fine[i]) || fine[i] < 0 || !double.IsFinite(coarse[i]) || coarse[i] < 0)
                throw new InvalidDataException($"litter cell {i} contains invalid mass");
            int idx = _world.Grid.DomainCells[i];
            FineMass[idx] = fine[i];
            CoarseMass[idx] = coarse[i];
            FruitMass[idx] = fruit?[i] ?? 0;
        }
        Revision++;
    }

    public double[] ExportFine() => _world.Grid.DomainCells.Select(idx => FineMass[idx]).ToArray();
    public double[] ExportCoarse() => _world.Grid.DomainCells.Select(idx => CoarseMass[idx]).ToArray();
    public double[] ExportFruit() => _world.Grid.DomainCells.Select(idx => FruitMass[idx]).ToArray();

    public bool AllFinite() => _world.Grid.DomainCells.All(idx =>
        double.IsFinite(FineMass[idx]) && FineMass[idx] >= 0 && double.IsFinite(CoarseMass[idx]) && CoarseMass[idx] >= 0
        && double.IsFinite(FruitMass[idx]) && FruitMass[idx] >= 0);
}
