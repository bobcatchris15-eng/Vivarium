using Vivarium.Sim.World;

namespace Vivarium.Sim.Ecology;

public sealed record FloraSpeciesStats(string Id, string Name, int Count, double Biomass, long Births, long Deaths);
public sealed record FaunaSpeciesStats(string Id, string Name, int Count, int Adults, double MeanEnergy, long Births, long Deaths,
    IReadOnlyDictionary<string, double> MeanTraits, double MeanBodySizeMm, int MaxGeneration);

public sealed record EcosystemStats(
    double SimDays,
    IReadOnlyList<FloraSpeciesStats> Flora,
    IReadOnlyList<FaunaSpeciesStats> Fauna,
    double MeanMoisture, double MeanNutrients, double TotalDetritus, double WaterVolume, double WetFraction,
    double MeanLight, int LineageRecords, int Genomes)
{
    public int FloraTotal => Flora.Sum(f => f.Count);
    public int FaunaTotal => Fauna.Sum(f => f.Count);
}

/// <summary>
/// Derives statistics purely from authoritative state. Computing (or discarding) a snapshot never changes the
/// simulation; the UI keeps its own history ring buffer of snapshots for trends.
/// </summary>
public static class EcosystemStatistics
{
    public static EcosystemStats Compute(VivariumWorld w)
    {
        var floraBySp = new Dictionary<string, List<Flora.FloraIndividual>>();
        foreach (var f in w.Flora.Items)
        {
            if (!floraBySp.TryGetValue(f.SpeciesId, out var list))
                floraBySp[f.SpeciesId] = list = new List<Flora.FloraIndividual>();
            list.Add(f);
        }

        var faunaBySp = new Dictionary<string, List<Fauna.FaunaIndividual>>();
        foreach (var f in w.Fauna.Items)
        {
            if (!faunaBySp.TryGetValue(f.SpeciesId, out var list))
                faunaBySp[f.SpeciesId] = list = new List<Fauna.FaunaIndividual>();
            list.Add(f);
        }

        var flora = new List<FloraSpeciesStats>(w.Content.Flora.Count);
        foreach (var sp in w.Content.Flora)
        {
            floraBySp.TryGetValue(sp.Id, out var members);
            int count = members?.Count ?? 0;
            double biomass = 0;
            if (members != null)
                for (int i = 0; i < members.Count; i++) biomass += members[i].Biomass;
            var t = w.Tally.Species.GetValueOrDefault(sp.Id);
            flora.Add(new FloraSpeciesStats(sp.Id, sp.Name, count, biomass, t?.Births ?? 0, t?.Deaths ?? 0));
        }

        var fauna = new List<FaunaSpeciesStats>(w.Content.Fauna.Count);
        foreach (var sp in w.Content.Fauna)
        {
            faunaBySp.TryGetValue(sp.Id, out var members);
            int count = members?.Count ?? 0;
            var t = w.Tally.Species.GetValueOrDefault(sp.Id);
            var traits = new SortedDictionary<string, double>(StringComparer.Ordinal);
            double size = 0; int maxGen = 0; int adults = 0; double energySum = 0;
            if (members != null && count > 0)
            {
                double totalSize = 0;
                for (int i = 0; i < count; i++)
                {
                    var m = members[i];
                    if (m.Stage == Fauna.FaunaLifeStage.Adult) adults++;
                    energySum += m.Energy;
                    totalSize += w.FaunaSystem.PhenotypeOf(m).BodySize;
                    var gen = w.Genomes.Get(m.GenomeId)?.Generation ?? 0;
                    if (gen > maxGen) maxGen = gen;
                }
                size = (totalSize / count) * 1000;
                for (int ti = 0; ti < sp.Traits.Count; ti++)
                {
                    double sumTrait = 0;
                    for (int i = 0; i < count; i++)
                        sumTrait += w.Genomes.Get(members[i].GenomeId)?.Traits[ti] ?? 0.5;
                    traits[sp.Traits[ti]] = sumTrait / count;
                }
            }
            fauna.Add(new FaunaSpeciesStats(sp.Id, sp.Name, count, adults,
                count > 0 ? energySum / count : 0, t?.Births ?? 0, t?.Deaths ?? 0, traits, size, maxGen));
        }

        int wet = 0;
        for (int i = 0; i < w.Grid.DomainCells.Length; i++)
            if (w.Water.IsWet(w.Grid.DomainCells[i])) wet++;

        return new EcosystemStats(w.Clock.BioDays, flora, fauna,
            w.Fields.Moisture.Mean(), w.Fields.Nutrients.Mean(), w.Litter.TotalDetritus(), w.Water.Volume(),
            (double)wet / Math.Max(1, w.Grid.DomainCells.Length), w.Fields.Light.Mean(), w.Lineage.Count, w.Genomes.Count);
    }
}
