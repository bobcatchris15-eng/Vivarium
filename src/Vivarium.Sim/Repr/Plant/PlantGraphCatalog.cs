using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Every current flora species has an explicitly authored structural design.</summary>
public static class PlantGraphCatalog
{
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = WoodyGraphSpecies.Species
        .Concat(HerbGraphSpecies.Species).Concat(CryptogamGraphSpecies.Species).OrderBy(s=>s.Id,StringComparer.Ordinal).ToArray();
    public static PlanGenerator Create(FloraSpeciesDef species) => WoodyGraphSpecies.Create(species)
        ?? HerbGraphSpecies.Create(species) ?? CryptogamGraphSpecies.Create(species)
        ?? throw new KeyNotFoundException("No authored graph design for "+species.Id);
    public static bool Supports(string id) => Species.Any(s=>s.Id==id);
    public static GraphSpeciesInfo Info(string id) => Species.First(s=>s.Id==id);
}
