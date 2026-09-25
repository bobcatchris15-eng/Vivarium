using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Ecology;

public sealed class SpeciesTally
{
    public long Births { get; set; }
    public long Deaths { get; set; }
    public SortedDictionary<string, long> DeathsByCause { get; set; } = new(StringComparer.Ordinal);
    public long Introduced { get; set; }
}

/// <summary>Authoritative event counters (they never feed back into simulation decisions).</summary>
public sealed class EcologyTally
{
    public SortedDictionary<string, SpeciesTally> Species { get; set; } = new(StringComparer.Ordinal);
    public double DetritusFromFlora { get; set; }
    public double DetritusFromFauna { get; set; }
    public double NutrientsFromDecay { get; set; }
    /// <summary>Dead matter consumed by decomposers (fungi, slime molds) and the nutrients they released.</summary>
    public double DetritusDecomposed { get; set; }
    public double NutrientsFromDecomposers { get; set; }
    public double NutrientsFromWaste { get; set; }
    public double NutrientsDirectReturn { get; set; }
    public double NutrientsUptake { get; set; }
    public double NutrientsApplied { get; set; }
    public double DetritusOverflow { get; set; }

    public SpeciesTally Of(string id) { if (!Species.TryGetValue(id, out var t)) Species[id] = t = new SpeciesTally(); return t; }
    public void Birth(string id) => Of(id).Births++;
    public void Death(string id, string cause) { var t = Of(id); t.Deaths++; t.DeathsByCause[cause] = t.DeathsByCause.GetValueOrDefault(cause) + 1; }
}

/// <summary>
/// Unified organic-matter pathway: dead flora and fauna feed ONE detritus field; detritus decays into soil
/// nutrients. Plankton grows in wet cells; biofilm is a projection of bed algae coverage.
/// </summary>
public sealed class EcologySystem
{
    private readonly VivariumWorld _w;
    public EcologySystem(VivariumWorld w) { _w = w; }

    /// <summary>Single entry point for returning organic matter. detritus → detritus field; directNutrients → nutrient field.</summary>
    public void ReturnOrganicMatter(Vec2 p, double detritus, double directNutrients, bool fromFlora)
    {
        int c = _w.Grid.NearestDomainCell(p);
        if (c < 0) return;
        if (detritus > 0)
        {
            double applied = SpreadAdd(_w.Fields.Detritus, c, detritus);
            _w.Tally.DetritusOverflow += detritus - applied;
            if (fromFlora) _w.Tally.DetritusFromFlora += applied; else _w.Tally.DetritusFromFauna += applied;
        }
        if (directNutrients > 0) _w.Tally.NutrientsDirectReturn += _w.Fields.Nutrients.Add(c, directNutrients);
    }

    /// <summary>Adds into a cell; any overflow above the field maximum spills into neighbouring cells.</summary>
    private double SpreadAdd(Fields.ScalarField f, int c, double amount)
    {
        double applied = f.Add(c, amount);
        double rest = amount - applied;
        if (rest <= 1e-15) return applied;
        foreach (int n in _w.Grid.CellsInRadius(_w.Grid.CellCenter(c), _w.Grid.CellSize * 2.2))
        {
            if (rest <= 1e-15) break;
            double a = f.Add(n, rest); applied += a; rest -= a;
        }
        return applied;
    }

    public void AddWasteNutrients(Vec2 p, double amount)
    {
        if (amount <= 0) return;
        int c = _w.Grid.NearestDomainCell(p);
        if (c >= 0) _w.Tally.NutrientsFromWaste += _w.Fields.Nutrients.Add(c, amount);
    }

    public void StepResources(double dt)
    {
        var eco = _w.Content.Ecology;
        var f = _w.Fields;
        double decay = 1 - Math.Exp(-eco.DetritusDecay * dt);
        foreach (int idx in _w.Grid.DomainCells)
        {
            // detritus decomposition → nutrients
            double d = f.Detritus.Values[idx];
            if (d > 0)
            {
                double dec = d * decay;
                f.Detritus[idx] = d - dec;
                double n = f.Nutrients.Add(idx, dec * eco.DetritusNutrientYield);
                _w.Tally.NutrientsFromDecay += n;
            }
            bool wet = _w.Water.IsWet(idx);
            if (wet)
            {
                double nutr = f.Nutrients.Values[idx] / eco.NutrientMax;
                double depthFactor = MathD.Clamp01(_w.Water.Depth[idx] / 0.15);
                Grow(f.Plankton, f.Nutrients, idx, eco.PlanktonGrowth * depthFactor * (0.2 + 0.8 * nutr), eco.PlanktonCapacity * depthFactor, eco.PlanktonNutrientUse, dt);
            }
            else
            {
                // Biofilm is a view of algae; only plankton owns mass here.
                f.Biofilm[idx] = 0;
                // plankton dies back on dry ground and becomes detritus
                double bf = f.Plankton.Values[idx];
                if (bf > 0)
                {
                    f.Plankton[idx] = 0;
                    _w.Tally.DetritusFromFlora += f.Detritus.Add(idx, bf);
                }
            }
        }
    }

    private void Grow(Fields.ScalarField field, Fields.ScalarField nutrients, int idx, double rate, double cap, double nutrientUse, double dt)
    {
        if (cap <= 0 || rate <= 0) return;
        double b0 = Math.Max(field.Values[idx], cap * 0.02); // seed population always present in water
        double b1 = cap * b0 / (b0 + (cap - b0) * Math.Exp(-rate * dt));
        double grow = b1 - field.Values[idx];
        if (grow <= 0) return;
        double need = grow * nutrientUse;
        double got = nutrients.Take(idx, need);
        if (need > 1e-15) grow *= got / need;
        field.Add(idx, grow);
    }
}
