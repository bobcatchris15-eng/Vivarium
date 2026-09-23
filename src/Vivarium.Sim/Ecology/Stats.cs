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
        var flora = w.Content.Flora.Select(sp =>
        {
            var members = w.Flora.Items.Where(f => f.SpeciesId == sp.Id).ToList();
            var t = w.Tally.Species.GetValueOrDefault(sp.Id);
            return new FloraSpeciesStats(sp.Id, sp.Name, members.Count, members.Sum(m => m.Biomass), t?.Births ?? 0, t?.Deaths ?? 0);
        }).ToList();

        var fauna = w.Content.Fauna.Select(sp =>
        {
            var members = w.Fauna.Items.Where(f => f.SpeciesId == sp.Id).ToList();
            var t = w.Tally.Species.GetValueOrDefault(sp.Id);
            var traits = new SortedDictionary<string, double>(StringComparer.Ordinal);
            double size = 0; int maxGen = 0;
            if (members.Count > 0)
            {
                for (int i = 0; i < sp.Traits.Count; i++)
                    traits[sp.Traits[i]] = members.Average(m => w.Genomes.Get(m.GenomeId)?.Traits[i] ?? 0.5);
                size = members.Average(m => w.FaunaSystem.PhenotypeOf(m).BodySize) * 1000;
                maxGen = members.Max(m => w.Genomes.Get(m.GenomeId)?.Generation ?? 0);
            }
            return new FaunaSpeciesStats(sp.Id, sp.Name, members.Count, members.Count(m => m.Stage == Fauna.FaunaLifeStage.Adult),
                members.Count > 0 ? members.Average(m => m.Energy) : 0, t?.Births ?? 0, t?.Deaths ?? 0, traits, size, maxGen);
        }).ToList();

        int wet = w.Grid.DomainCells.Count(c => w.Water.IsWet(c));
        return new EcosystemStats(w.Clock.SimDays, flora, fauna,
            w.Fields.Moisture.Mean(), w.Fields.Nutrients.Mean(), w.Fields.Detritus.Total() , w.Water.Volume(),
            (double)wet / Math.Max(1, w.Grid.DomainCells.Length), w.Fields.Light.Mean(), w.Lineage.Count, w.Genomes.Count);
    }
}
