using System.Globalization;
using System.Text;
using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Diagnostics;

/// <summary>On-demand, read-only spatial ecological survey. No permanent event journal.</summary>
public static class HabitatSurvey
{
    public sealed class SpeciesMap
    {
        public required string SpeciesId { get; init; }
        public required double[] Growth { get; init; }
        public required double[] Establishment { get; init; }
        public required double[] Survival { get; init; }
        public required double[] Reproduction { get; init; }
        public required double[] ActualBiomass { get; init; }
        public required int[] ActualIndividuals { get; init; }
        public required bool[] CoveragePresent { get; init; }
        /// <summary>Occupied fine-grid cells; represents spatial extent, not individual organisms.</summary>
        public int CoverageCells { get; set; }
        /// <summary>Native coverage B units, not interchangeable with individual-flora biomass.</summary>
        public double CoverageBiomassUnits { get; set; }
        public int FavorableCells { get; set; }
        public int OccupiedCells { get; set; }
        public double TotalBiomass { get; set; }
    }

    public sealed class Snapshot
    {
        public required GridSpec Grid { get; init; }
        /// <summary>Frozen environmental samples; exports never read a subsequently modified world.</summary>
        public required IReadOnlyDictionary<string, double[]> FieldRaster { get; init; }
        public required IReadOnlyDictionary<string, HabitatDiagnostics.FieldSummary> Fields { get; init; }
        public required IReadOnlyDictionary<string, double> HabitatBiomass { get; init; }
        public required IReadOnlyDictionary<string, int> HabitatIndividuals { get; init; }
        public required IReadOnlyDictionary<string, int> HabitatCells { get; init; }
        public required IReadOnlyDictionary<string, int> LargestHabitatPatchCells { get; init; }
        public required IReadOnlyDictionary<string, SpeciesMap> Species { get; init; }
        public required IReadOnlyDictionary<string, HabitatDiagnostics.DirectionalOverlap> Overlaps { get; init; }
        /// <summary>Number of habitat cells favorable to exactly N surveyed species.</summary>
        public required IReadOnlyDictionary<int, int> FavorableSpeciesMultiplicity { get; init; }

        /// <summary>Area favorable simultaneously to every requested species, using one shared threshold.</summary>
        public int FavorableIntersectionCells(params string[] speciesIds)
        {
            if (speciesIds.Length == 0) throw new ArgumentException("At least one species is required.", nameof(speciesIds));
            var selected = speciesIds.Select(id => Species.TryGetValue(id, out var map) ?
                map : throw new KeyNotFoundException($"Unknown surveyed species: {id}")).ToArray();
            int count = 0;
            foreach (int cell in Grid.DomainCells)
                if (selected.All(map => map.Establishment[cell] >= FavorableThreshold)) count++;
            return count;
        }
        public required double FavorableThreshold { get; init; }
        public double CellArea => Grid.CellSize * Grid.CellSize;

        /// <summary>Reproducible tabular export for a selected species and the authoritative current fields.</summary>
        public string ToCsv(string speciesId)
        {
            if (!Species.TryGetValue(speciesId, out var map))
                throw new ArgumentException($"Species '{speciesId}' not surveyed.", nameof(speciesId));
            var csv = new StringBuilder("x_m,z_m,moisture,light,nutrients,oxygen,topsoil_depth_m,drainage,growth,establishment,survival,reproduction,occupancy_biomass,occupancy_individuals\n");
            foreach (int cell in Grid.DomainCells)
            {
                var pos = Grid.CellCenter(cell);
                double moisture = FieldRaster["moisture"][cell];
                double oxygen = FieldRaster["oxygen"][cell];
                double topsoil = FieldRaster["retained_topsoil_depth_m"][cell];
                double drainage = FieldRaster["drainage_potential"][cell];
                double nutrient = FieldRaster["nutrients"][cell];
                double light = FieldRaster["light"][cell];
                csv.AppendLine(string.Join(",", new[]
                {
                    pos.X, pos.Z, moisture, light, nutrient, oxygen, topsoil, drainage,
                    map.Growth[cell], map.Establishment[cell], map.Survival[cell],
                    map.Reproduction[cell], map.ActualBiomass[cell],
                    (double)map.ActualIndividuals[cell],
                }.Select(x => x.ToString("G17", CultureInfo.InvariantCulture))));
            }
            return csv.ToString();
        }
    }

    /// <summary>Net present-state changes between two snapshots. No historical log or event stream.</summary>
    public readonly record struct SpeciesChange(int ColonizedCells, int VacatedCells,
        double NetBiomass, int NetIndividuals, int NetCoverageCells,
        double NetCoverageBiomassUnits);

    public static IReadOnlyDictionary<string, SpeciesChange> Compare(Snapshot earlier, Snapshot later)
    {
        if (earlier.Grid.Count != later.Grid.Count ||
            earlier.Grid.Nx != later.Grid.Nx || earlier.Grid.Nz != later.Grid.Nz ||
            earlier.Grid.CellSize != later.Grid.CellSize)
            throw new ArgumentException("Snapshots require the same spatial grid.");
        var changes = new Dictionary<string, SpeciesChange>(StringComparer.Ordinal);
        foreach (var (id, a) in earlier.Species)
        {
            if (!later.Species.TryGetValue(id, out var b)) continue;
            int added = 0, lost = 0;
            foreach (int cell in earlier.Grid.DomainCells)
            {
                bool existed = a.ActualIndividuals[cell] > 0 || a.CoveragePresent[cell];
                bool exists = b.ActualIndividuals[cell] > 0 || b.CoveragePresent[cell];
                if (!existed && exists) added++;
                if (existed && !exists) lost++;
            }
            changes.Add(id, new SpeciesChange(added, lost, b.TotalBiomass - a.TotalBiomass,
                b.ActualIndividuals.Sum() - a.ActualIndividuals.Sum(),
                b.CoverageCells - a.CoverageCells,
                b.CoverageBiomassUnits - a.CoverageBiomassUnits));
        }
        return changes;
    }

    public static Snapshot Capture(VivariumWorld world, IEnumerable<FloraSpeciesDef> species,
        double favorableThreshold = 0.55)
    {
        if (!double.IsFinite(favorableThreshold) || favorableThreshold <= 0 || favorableThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(favorableThreshold));
        world.RefreshDerived();
        world.RootZone.EnsureCurrent();
        var grid = world.Grid;
        var items = species.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var maps = new Dictionary<string, SpeciesMap>(StringComparer.Ordinal);
        foreach (var sp in items)
        {
            if (maps.ContainsKey(sp.Id)) throw new ArgumentException($"Duplicate species '{sp.Id}'.", nameof(species));
            maps.Add(sp.Id, new SpeciesMap
            {
                SpeciesId = sp.Id,
                Growth = new double[grid.Count], Establishment = new double[grid.Count],
                Survival = new double[grid.Count], Reproduction = new double[grid.Count],
                ActualBiomass = new double[grid.Count], ActualIndividuals = new int[grid.Count],
                CoveragePresent = new bool[grid.Count]
            });
        }

        var habitats = new Dictionary<string, int>(StringComparer.Ordinal);
        var category = new string[grid.Count];
        foreach (int cell in grid.DomainCells)
        {
            var pos = grid.CellCenter(cell);
            double wet = world.Fields.Moisture.Values[cell];
            double light = world.Fields.Light.Values[cell];
            string key = world.SubstrateAtCell(pos) + "/" +
                (wet >= 0.90 ? "saturated" : wet >= 0.65 ? "damp" : wet >= 0.35 ? "mesic" : "dry") +
                "/" + (light < 0.35 ? "shade" : light < 0.70 ? "partial" : "sun");
            category[cell] = key;
            habitats[key] = habitats.GetValueOrDefault(key) + 1;
            foreach (var sp in items)
            {
                var suit = world.FloraSystem.PotentialSuitability(sp, pos);
                var map = maps[sp.Id];
                if (suit.HardRefused) continue;
                map.Growth[cell] = suit.GrowthScore;
                map.Establishment[cell] = suit.EstablishmentScore;
                map.Survival[cell] = suit.SurvivalScore;
                map.Reproduction[cell] = suit.ReproductionScore;
                if (suit.EstablishmentScore >= favorableThreshold) map.FavorableCells++;
            }
        }
        var biomassByHabitat = new Dictionary<string, double>(StringComparer.Ordinal);
        var individualsByHabitat = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in world.Flora.Items)
        {
            int cell = grid.NearestDomainCell(f.Position);
            if (cell < 0 || !grid.InDomain(cell)) continue;
            string habitat = category[cell];
            biomassByHabitat[habitat] = biomassByHabitat.GetValueOrDefault(habitat) + f.Biomass;
            individualsByHabitat[habitat] = individualsByHabitat.GetValueOrDefault(habitat) + 1;
            if (!maps.TryGetValue(f.SpeciesId, out var map)) continue;
            if (map.ActualIndividuals[cell]++ == 0) map.OccupiedCells++;
            map.ActualBiomass[cell] += f.Biomass;
            map.TotalBiomass += f.Biomass;
        }

        // Fine coverage occupies its own authoritative layers. A moss, lichen,
        // or plasmodium colony must not look absent merely because it has zero
        // FloraIndividuals. Keep its native biomass units separate from plant mass.
        foreach (var layer in world.Coverage.All)
        {
            if (layer.Id is not (CoverageLayerId.Mat or CoverageLayerId.Crust or CoverageLayerId.Plasmodium))
                continue;
            foreach (var tile in layer.Tiles)
            {
                for (int local = 0; local < CoverageTile.N; local++)
                {
                    byte occ = tile.Occ[local];
                    float mass = tile.B[local];
                    if (occ == 0 || mass <= 0) continue;
                    string? id = world.CoverageSystem.SpeciesId(layer.Id, occ);
                    if (id == null || !maps.TryGetValue(id, out var map)) continue;
                    int gx = tile.Ti * CoverageSpec.TileEdge + local % CoverageSpec.TileEdge;
                    int gz = tile.Tj * CoverageSpec.TileEdge + local / CoverageSpec.TileEdge;
                    var pos = new Vivarium.Sim.Core.Vec2(
                        (gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
                    int cell = grid.CellAt(pos);
                    if (!grid.InDomain(cell)) continue;
                    map.CoverageCells++;
                    map.CoverageBiomassUnits += mass;
                    if (!map.CoveragePresent[cell])
                    {
                        map.CoveragePresent[cell] = true;
                        if (map.ActualIndividuals[cell] == 0) map.OccupiedCells++;
                    }
                }
            }
        }

        var pairwise = new Dictionary<string, HabitatDiagnostics.DirectionalOverlap>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++)
            for (int j = i + 1; j < items.Length; j++)
                pairwise.Add(items[i].Id + "|" + items[j].Id,
                    OverlapInDomain(grid, maps[items[i].Id].Establishment,
                        maps[items[j].Id].Establishment, favorableThreshold));

        var multiplicity = new Dictionary<int, int>();
        foreach (int cell in grid.DomainCells)
        {
            int favorable = 0;
            foreach (var map in maps.Values)
                if (map.Establishment[cell] >= favorableThreshold) favorable++;
            multiplicity[favorable] = multiplicity.GetValueOrDefault(favorable) + 1;
        }

        var oxygenRaster = new double[grid.Count];
        var openWaterDepth = new double[grid.Count];
        var effectiveLight = new double[grid.Count];
        foreach (int cell in grid.DomainCells)
        {
            var p = grid.CellCenter(cell);
            oxygenRaster[cell] = world.RootZone.OxygenAt(p, world.Fields.Moisture.Values[cell]);
            openWaterDepth[cell] = world.Water.OpenWaterDepth(cell);
            effectiveLight[cell] = world.FloraSystem.EffectiveLight(p);
        }
        var frozenFields = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["moisture"] = (double[])world.Fields.Moisture.Values.Clone(),
            ["light"] = (double[])world.Fields.Light.Values.Clone(),
            ["nutrients"] = (double[])world.Fields.Nutrients.Values.Clone(),
            ["oxygen"] = oxygenRaster,
            ["open_water_depth_m"] = openWaterDepth,
            ["effective_light"] = effectiveLight,
            ["retained_topsoil_depth_m"] = (double[])world.RootZone.RetainedTopsoilDepth.Values.Clone(),
            ["drainage_potential"] = (double[])world.RootZone.DrainagePotential.Values.Clone(),
        };
        var oxygenField = new ScalarField("oxygen", grid, 0, 0, 1);
        var openWaterField = new ScalarField("open_water_depth_m", grid, 0, 0, double.PositiveInfinity);
        var effectiveLightField = new ScalarField("effective_light", grid, 0, 0, 1);
        foreach (int cell in grid.DomainCells)
        {
            oxygenField[cell] = oxygenRaster[cell];
            openWaterField[cell] = openWaterDepth[cell];
            effectiveLightField[cell] = effectiveLight[cell];
        }
        var measures = new Dictionary<string, HabitatDiagnostics.FieldSummary>(StringComparer.Ordinal)
        {
            ["moisture"] = HabitatDiagnostics.Summarize(world.Fields.Moisture, 0.25),
            ["light"] = HabitatDiagnostics.Summarize(world.Fields.Light, 0.25),
            ["effective_light"] = HabitatDiagnostics.Summarize(effectiveLightField, 0.25),
            ["oxygen"] = HabitatDiagnostics.Summarize(oxygenField, 0.25),
            ["open_water_depth_m"] = HabitatDiagnostics.Summarize(openWaterField, 0.25),
            ["nutrients"] = HabitatDiagnostics.Summarize(world.Fields.Nutrients, 0.25),
            ["retained_topsoil_depth_m"] = HabitatDiagnostics.Summarize(world.RootZone.RetainedTopsoilDepth, 0.25),
            ["drainage_potential"] = HabitatDiagnostics.Summarize(world.RootZone.DrainagePotential, 0.25),
        };
        return new Snapshot
        {
            Grid = grid, FieldRaster = frozenFields, Fields = measures,
            HabitatCells = habitats, HabitatBiomass = biomassByHabitat,
            HabitatIndividuals = individualsByHabitat, Species = maps,
            Overlaps = pairwise, FavorableSpeciesMultiplicity = multiplicity,
            LargestHabitatPatchCells = LargestPatches(grid, category),
            FavorableThreshold = favorableThreshold,
        };
    }

    private static HabitatDiagnostics.DirectionalOverlap OverlapInDomain(GridSpec grid,
        double[] a, double[] b, double threshold)
    {
        int ca = 0, cb = 0, both = 0;
        foreach (int cell in grid.DomainCells)
        {
            bool fa = a[cell] >= threshold, fb = b[cell] >= threshold;
            if (fa) ca++;
            if (fb) cb++;
            if (fa && fb) both++;
        }
        return new(ca, cb, both, ca == 0 ? 0 : (double)both / ca,
            cb == 0 ? 0 : (double)both / cb);
    }

    /// <summary>Four-connected habitat regions, not disconnected pixels or diagonal noise.</summary>
    private static Dictionary<string, int> LargestPatches(GridSpec grid, string[] habitat)
    {
        var visited = new bool[grid.Count];
        var queue = new int[grid.Count];
        var largest = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int start in grid.DomainCells)
        {
            if (visited[start]) continue;
            string key = habitat[start];
            int head = 0, tail = 0;
            visited[start] = true;
            queue[tail++] = start;
            while (head < tail)
            {
                int idx = queue[head++];
                int x = idx % grid.Nx, z = idx / grid.Nx;
                Visit(x - 1, z); Visit(x + 1, z); Visit(x, z - 1); Visit(x, z + 1);
            }
            largest[key] = Math.Max(largest.GetValueOrDefault(key), tail);
            void Visit(int x, int z)
            {
                if (!grid.InDomain(x, z)) return;
                int n = grid.Index(x, z);
                if (visited[n] || habitat[n] != key) return;
                visited[n] = true;
                queue[tail++] = n;
            }
        }
        return largest;
    }
}
