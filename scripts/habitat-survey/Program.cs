using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Diagnostics;
using Vivarium.Sim.World;

// Run at the repository root:
// dotnet run --project scripts/habitat-survey -- OUT [PRESET] [SEED] [CSV_SPECIES] [SIM_SECONDS]
// This is a deterministic, present-state diagnostic. It neither edits content
// nor changes the persistent simulation format.
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project scripts/habitat-survey -- OUTPUT_DIR [PRESET_ID] [SEED] [CSV_SPECIES_ID] [SIM_SECONDS]");
    return 2;
}
var content = ContentLoader.Load(new DirectoryContentSource(Path.GetFullPath("game/content")));
string presetId = args.Length >= 2 ? args[1] : "default";
var d = content.PresetOrThrow(presetId);
if (args.Length >= 3) d.Seed = ulong.Parse(args[2]);
var world = VivariumWorld.Create(content, d, populate: true, warmupSurfaceWater: true);
var survey = HabitatSurvey.Capture(world, content.Flora, 0.50);
int initialPlasmodiumSpores = world.CoverageSystem.PlasmodiumSpores.Count;
string folder = Path.GetFullPath(args[0]);
Directory.CreateDirectory(folder);
var report = new
{
    world = presetId,
    seed = d.Seed,
    domainCells = world.Grid.DomainCells.Length,
    cellAreaM2 = survey.CellArea,
    environment = survey.Fields,
    habitatAreaM2 = survey.HabitatCells.ToDictionary(x => x.Key, x => x.Value * survey.CellArea),
    largestContiguousHabitatPatchM2 = survey.LargestHabitatPatchCells.ToDictionary(x => x.Key, x => x.Value * survey.CellArea),
    actualFloraBiomassByHabitat = survey.HabitatBiomass,
    actualFloraIndividualsByHabitat = survey.HabitatIndividuals,
    species = survey.Species.ToDictionary(x => x.Key, x => new
    {
        potentialFavorableCellCount = x.Value.FavorableCells,
        peakEstablishmentScore = x.Value.Establishment.Max(),
        averageEstablishmentScore = world.Grid.DomainCells.Average(cell => x.Value.Establishment[cell]),
        peakGrowthScore = x.Value.Growth.Max(),
        actualOccupiedCellCount = x.Value.OccupiedCells,
        actualIndividualCount = x.Value.ActualIndividuals.Sum(),
        actualBiomass = x.Value.TotalBiomass,
        coverageFineCells = x.Value.CoverageCells,
        coverageAreaM2 = x.Value.CoverageCells * Vivarium.Sim.Coverage.CoverageSpec.CellSize * Vivarium.Sim.Coverage.CoverageSpec.CellSize,
        coverageBiomassUnits = x.Value.CoverageBiomassUnits,
    }),
    directionalEstablishmentOverlap = survey.Overlaps,
    favorableSpeciesMultiplicity = survey.FavorableSpeciesMultiplicity,
    notes = "Potential excludes direct neighbour exclusions; ecological shade and resources reflect current world. Coverage occupancy measured separately; fine-grid biomass units are not individual biomass. Productivity rate requires two timed snapshots."
};
string path = Path.Combine(folder, "habitat-survey.json");
File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
if (args.Length >= 4)
{
    File.WriteAllText(Path.Combine(folder, args[3] + "-habitat.csv"), survey.ToCsv(args[3]));
}
Console.WriteLine($"Habitat survey: {path}");
if (args.Length >= 5)
{
    int seconds = int.Parse(args[4]);
    if (seconds < 0 || seconds > 86400)
        throw new ArgumentOutOfRangeException(nameof(seconds), "Duration must be 0..86400 seconds.");
    world.RunFor(seconds);
    var later = HabitatSurvey.Capture(world, content.Flora, 0.50);
    var changes = HabitatSurvey.Compare(survey, later);
    var comparison = new
    {
        simulatedSeconds = seconds,
        biologicalSeconds = seconds * world.Clock.BioAcceleration,
        plasmodiumSporeCountBefore = initialPlasmodiumSpores,
        plasmodiumSporeCountAfter = world.CoverageSystem.PlasmodiumSpores.Count,
        environmentalBefore = survey.Fields,
        environmentalAfter = later.Fields,
        habitatBiomassBefore = survey.HabitatBiomass,
        habitatBiomassAfter = later.HabitatBiomass,
        speciesChanges = changes,
        note = "Net present-state comparison; no periodic reset, event log, or forced activity."
    };
    string diffPath = Path.Combine(folder, "habitat-revisit.json");
    File.WriteAllText(diffPath, JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true }));
    if (args.Length >= 4)
        File.WriteAllText(Path.Combine(folder, args[3] + "-habitat-after.csv"), later.ToCsv(args[3]));
    Console.WriteLine($"Habitat revisit ({seconds} simulated s): {diffPath}");
    Console.WriteLine($"Species with colonized cells: {changes.Count(x => x.Value.ColonizedCells > 0)}; with vacated cells: {changes.Count(x => x.Value.VacatedCells > 0)}");
}
Console.WriteLine($"Seed: {d.Seed}; cells: {world.Grid.DomainCells.Length}; habitat combinations: {survey.HabitatCells.Count}");
foreach (var (name, summary) in survey.Fields.OrderBy(x => x.Key))
    Console.WriteLine($"{name}: min={summary.Min:F3}, max={summary.Max:F3}, mean={summary.Mean:F3}, maxGradient={summary.MaximumNeighbourGradient:F3}/m");
return 0;
