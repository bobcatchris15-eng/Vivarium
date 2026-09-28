import os

file_path = "src/Vivarium.Sim/Coverage/CoverageSystem.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

crlf = "\r\n" in content
nl = "\r\n" if crlf else "\n"

# 1. Add import
import_marker = "using Vivarium.Sim.Coverage.Rules;" + nl
new_import = import_marker + "using Vivarium.Sim.Coverage.Plasmodium;" + nl
assert import_marker in content, "import_marker not found"
content = content.replace(import_marker, new_import, 1)

# 2. Add plasmodium fields
fields_marker = "    private readonly Dictionary<byte, string> _lichenSpeciesId = new();" + nl
new_fields = fields_marker + \
"    private readonly PlasmodiumParams? _plasmodiumParams;" + nl + \
"    private readonly PlasmodiumColony? _plasmodiumColony;" + nl + \
"    private readonly Attractant? _plasmodiumAttractant;" + nl + \
"    private readonly Network? _plasmodiumNetwork;" + nl + \
"    private readonly LifecycleController? _plasmodiumLifecycle;" + nl + \
"    private readonly PlasmodiumWorldEnv? _plasmodiumEnv;" + nl + \
"    private readonly string? _plasmodiumSpeciesId;" + nl
assert fields_marker in content, "fields_marker not found"
content = content.replace(fields_marker, new_fields, 1)

# 3. Add constructor initialization
ctor_loop_marker = "                });" + nl + "            }" + nl + "        }" + nl + "    }"
new_ctor_loop = "                });" + nl + \
"            }" + nl + \
"            if (sp.Archetype == \"slime_mold\")" + nl + \
"            {" + nl + \
"                _plasmodiumSpeciesId = sp.Id;" + nl + \
"                _plasmodiumParams = new PlasmodiumParams" + nl + \
"                {" + nl + \
"                    InitialMass = 500," + nl + \
"                    Beta = 0.05," + nl + \
"                    LambdaF = 4.0," + nl + \
"                    Sigma = 5.0," + nl + \
"                    Dc = 0.04," + nl + \
"                    Delta = 0.10," + nl + \
"                    FeedRate = 3.0," + nl + \
"                };" + nl + \
"                _plasmodiumColony = new PlasmodiumColony(1, _plasmodiumParams);" + nl + \
"                _plasmodiumAttractant = new Attractant(_plasmodiumParams);" + nl + \
"                _plasmodiumNetwork = new Network { Gamma = 0.3, QGain = 5.0 };" + nl + \
"                _plasmodiumLifecycle = new LifecycleController(_plasmodiumParams);" + nl + \
"                _plasmodiumEnv = new PlasmodiumWorldEnv(w);" + nl + \
"            }" + nl + \
"        }" + nl + \
"    }"
assert ctor_loop_marker in content, "ctor_loop_marker not found"
content = content.replace(ctor_loop_marker, new_ctor_loop, 1)

# 4. HasCoverageSpecies
has_marker = "    public bool HasCoverageSpecies => _matSpecies.Count > 0 || _lichenSpecies.Count > 0;" + nl
new_has = "    public bool HasCoverageSpecies => _matSpecies.Count > 0 || _lichenSpecies.Count > 0 || _plasmodiumColony != null;" + nl
assert has_marker in content, "has_marker not found"
content = content.replace(has_marker, new_has, 1)

# 5. SeedInitial update
seed_marker = "        foreach (var lp in _lichenSpecies)" + nl + \
"            SeedSpecies(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells," + nl + \
"                p => IsLichenSuitable(lp, p));" + nl + \
"    }"

new_seed = "        foreach (var lp in _lichenSpecies)" + nl + \
"            SeedSpecies(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells," + nl + \
"                p => IsLichenSuitable(lp, p));" + nl + nl + \
"        if (_plasmodiumColony != null && _plasmodiumSpeciesId != null)" + nl + \
"            SeedPlasmodiumInitial();" + nl + \
"    }"
assert seed_marker in content, "seed_marker not found"
content = content.replace(seed_marker, new_seed, 1)

# 6. Add IsPlasmodiumSuitable, SeedPlasmodiumInitial, StepPlasmodium
suitable_marker = "    private bool IsLichenSuitable(LichenParams lp, Vec2 p)" + nl
new_methods = "    private bool IsPlasmodiumSuitable(Vec2 p)" + nl + \
"    {" + nl + \
"        var e = CoverageEnvironment.Sample(_w, p);" + nl + \
"        if (e.Substrate == CoverageSubstrate.Water) return false;" + nl + \
"        if (e.Moisture < 0.25) return false;" + nl + \
"        int cell = _w.Grid.NearestDomainCell(p);" + nl + \
"        if (cell < 0) return false;" + nl + \
"        double det = _w.Fields.Detritus[cell];" + nl + \
"        return det > 0.02;" + nl + \
"    }" + nl + nl + \
"    private void SeedPlasmodiumInitial()" + nl + \
"    {" + nl + \
"        if (_plasmodiumColony == null) return;" + nl + \
"        var cells = _w.Grid.DomainCells;" + nl + \
"        if (cells.Length == 0) return;" + nl + \
"        var candidates = cells" + nl + \
"            .Select(c => (_w.Grid.CellCenter(c), _w.Fields.Detritus[c]))" + nl + \
"            .Where(x => IsPlasmodiumSuitable(x.Item1))" + nl + \
"            .OrderByDescending(x => x.Item2)" + nl + \
"            .Select(x => x.Item1)" + nl + \
"            .ToList();" + nl + \
"        var chosen = new List<Vec2>();" + nl + \
"        foreach (var p in candidates)" + nl + \
"        {" + nl + \
"            if (chosen.Count >= 3) break;" + nl + \
"            bool tooClose = false;" + nl + \
"            foreach (var c in chosen) if (Vec2.Distance(c, p) < 1.5) { tooClose = true; break; }" + nl + \
"            if (tooClose) continue;" + nl + \
"            chosen.Add(p);" + nl + \
"        }" + nl + \
"        foreach (var p in chosen)" + nl + \
"        {" + nl + \
"            var seededCells = new List<(int gx, int gz)>();" + nl + \
"            var (gx0, gz0) = CoverageSpec.CellOf(p);" + nl + \
"            int radiusCells = 3;" + nl + \
"            for (int dz = -radiusCells; dz <= radiusCells; dz++)" + nl + \
"            for (int dx = -radiusCells; dx <= radiusCells; dx++)" + nl + \
"            {" + nl + \
"                if (dx * dx + dz * dz > radiusCells * radiusCells) continue;" + nl + \
"                int gx = gx0 + dx, gz = gz0 + dz;" + nl + \
"                if (!IsPlasmodiumSuitable(CellCentre(gx, gz))) continue;" + nl + \
"                _w.Coverage.Plasmodium.SetCell(gx, gz, occ: 1, b: 1.0f, w: 20, 0, 0, (byte)CoverageFlags.Front, 1);" + nl + \
"                seededCells.Add((gx, gz));" + nl + \
"            }" + nl + \
"            if (seededCells.Count > 0) _plasmodiumColony.Seed(seededCells);" + nl + \
"        }" + nl + \
"    }" + nl + nl + \
"    public void StepPlasmodium(double dtSeconds)" + nl + \
"    {" + nl + \
"        if (_plasmodiumColony == null || _plasmodiumNetwork == null || _plasmodiumAttractant == null || _plasmodiumEnv == null || _plasmodiumLifecycle == null) return;" + nl + \
"        var occupied = _plasmodiumColony.CellId.Keys.ToList();" + nl + \
"        if (occupied.Count == 0) return;" + nl + \
"        var sources = new HashSet<(int cx, int cz)>();" + nl + \
"        foreach (var (gx, gz) in occupied)" + nl + \
"        {" + nl + \
"            var coarse = Attractant.CoarseOf(gx, gz);" + nl + \
"            if (_plasmodiumEnv.Detritus(gx, gz) > 0.04) sources.Add(coarse);" + nl + \
"        }" + nl + \
"        if (sources.Count > 0) _plasmodiumNetwork.SetSources(sources);" + nl + \
"        double dt = Math.Clamp(dtSeconds, 0.1, 5.0);" + nl + \
"        _plasmodiumNetwork.Step(_w.Coverage.Plasmodium, occupied, dt);" + nl + \
"        _plasmodiumColony.Step(_w.Coverage.Plasmodium, _plasmodiumAttractant, _plasmodiumEnv, _step, dt, _plasmodiumNetwork);" + nl + \
"        _plasmodiumColony.Relabel(_plasmodiumColony.CellId.Keys.ToList());" + nl + \
"        _plasmodiumLifecycle.Step(_w.Coverage.Plasmodium, _plasmodiumColony, _plasmodiumNetwork, _plasmodiumEnv, _step, dt);" + nl + \
"        foreach (var ((gx, gz), mass) in _plasmodiumColony.Mass)" + nl + \
"        {" + nl + \
"            _w.Coverage.Plasmodium.SetB(gx, gz, (float)Math.Clamp(mass, 0.1, 5.0));" + nl + \
"        }" + nl + \
"        _w.Coverage.Plasmodium.Advance();" + nl + \
"    }" + nl + nl + suitable_marker
assert suitable_marker in content, "suitable_marker not found"
content = content.replace(suitable_marker, new_methods, 1)

# 7. Update CanSeed
can_seed_marker = "        foreach (var lp in _lichenSpecies)" + nl + \
"            if (_lichenSpeciesId[lp.OccSlot] == speciesId)" + nl + \
"            {" + nl + \
"                if (IsLichenSuitable(lp, p)) { reason = \"ok\"; return true; }" + nl + \
"                var e = CoverageEnvironment.Sample(_w, p);" + nl + \
"                reason = !lp.AllowsSubstrate(e.Substrate) ? \"wrong substrate (needs rock, log or stable soil)\" : \"conditions too poor here\";" + nl + \
"                return false;" + nl + \
"            }"

new_can_seed = can_seed_marker + nl + \
"        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId)" + nl + \
"        {" + nl + \
"            if (IsPlasmodiumSuitable(p)) { reason = \"ok\"; return true; }" + nl + \
"            var e = CoverageEnvironment.Sample(_w, p);" + nl + \
"            reason = e.Substrate == CoverageSubstrate.Water ? \"underwater\" :" + nl + \
"                e.Moisture < 0.25 ? \"too dry here\" : \"needs dead organic matter (detritus)\";" + nl + \
"            return false;" + nl + \
"        }"
assert can_seed_marker in content, "can_seed_marker not found"
content = content.replace(can_seed_marker, new_can_seed, 1)

# 8. Update SeedClump
seed_clump_marker = "        foreach (var lp in _lichenSpecies)" + nl + \
"            if (_lichenSpeciesId[lp.OccSlot] == speciesId)" + nl + \
"            {" + nl + \
"                SeedClumpCells(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, gx0, gz0, radiusCells, q => IsLichenSuitable(lp, q));" + nl + \
"                return;" + nl + \
"            }" + nl + \
"    }"

new_seed_clump = "        foreach (var lp in _lichenSpecies)" + nl + \
"            if (_lichenSpeciesId[lp.OccSlot] == speciesId)" + nl + \
"            {" + nl + \
"                SeedClumpCells(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, gx0, gz0, radiusCells, q => IsLichenSuitable(lp, q));" + nl + \
"                return;" + nl + \
"            }" + nl + \
"        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId && _plasmodiumColony != null)" + nl + \
"        {" + nl + \
"            var seededCells = new List<(int gx, int gz)>();" + nl + \
"            for (int dz = -radiusCells; dz <= radiusCells; dz++)" + nl + \
"            for (int dx = -radiusCells; dx <= radiusCells; dx++)" + nl + \
"            {" + nl + \
"                if (dx * dx + dz * dz > radiusCells * radiusCells) continue;" + nl + \
"                int gx = gx0 + dx, gz = gz0 + dz;" + nl + \
"                if (!IsPlasmodiumSuitable(CellCentre(gx, gz))) continue;" + nl + \
"                _w.Coverage.Plasmodium.SetCell(gx, gz, occ: 1, b: 1.0f, w: 20, 0, 0, (byte)CoverageFlags.Front, 1);" + nl + \
"                seededCells.Add((gx, gz));" + nl + \
"            }" + nl + \
"            if (seededCells.Count > 0) _plasmodiumColony.Seed(seededCells);" + nl + \
"            return;" + nl + \
"        }" + nl + \
"    }"
assert seed_clump_marker in content, "seed_clump_marker not found"
content = content.replace(seed_clump_marker, new_seed_clump, 1)

# 9. Update ClearDisc
clear_disc_marker = "        cleared += ClearDiscLayer(_w.Coverage.Crust, gx0, gz0, radiusCells, occ => _lichenSpeciesId.GetValueOrDefault(occ) is { } id && (speciesFilter == null || id == speciesFilter));" + nl
new_clear_disc = clear_disc_marker + \
"        cleared += ClearDiscLayer(_w.Coverage.Plasmodium, gx0, gz0, radiusCells, occ => occ == 1 && (speciesFilter == null || _plasmodiumSpeciesId == speciesFilter));" + nl
assert clear_disc_marker in content, "clear_disc_marker not found"
content = content.replace(clear_disc_marker, new_clear_disc, 1)

# 10. Update SpeciesId
species_id_marker = "        CoverageLayerId.Crust => _lichenSpeciesId.GetValueOrDefault(occupant)," + nl
new_species_id = species_id_marker + \
"        CoverageLayerId.Plasmodium => occupant == 1 ? _plasmodiumSpeciesId : null," + nl
assert species_id_marker in content, "species_id_marker not found"
content = content.replace(species_id_marker, new_species_id, 1)

# 11. Update CoveredArea
covered_area_marker = "        foreach (var (occ, id) in _lichenSpeciesId)" + nl + \
"            if (id == speciesId) total += CountOccupied(_w.Coverage.Crust, occ) * cellArea;" + nl
new_covered_area = covered_area_marker + \
"        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId)" + nl + \
"            total += CountOccupied(_w.Coverage.Plasmodium, 1) * cellArea;" + nl
assert covered_area_marker in content, "covered_area_marker not found"
content = content.replace(covered_area_marker, new_covered_area, 1)

# 12. Add PlasmodiumWorldEnv at the end
env_class = nl + \
"internal sealed class PlasmodiumWorldEnv : IPlasmodiumEnvironment" + nl + \
"{" + nl + \
"    private readonly VivariumWorld _w;" + nl + \
"    public PlasmodiumWorldEnv(VivariumWorld w) => _w = w;" + nl + nl + \
"    public double Moisture(int gx, int gz)" + nl + \
"    {" + nl + \
"        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);" + nl + \
"        return _w.Fields.Moisture.Sample(p);" + nl + \
"    }" + nl + nl + \
"    public double Detritus(int gx, int gz)" + nl + \
"    {" + nl + \
"        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);" + nl + \
"        int cell = _w.Grid.NearestDomainCell(p);" + nl + \
"        return cell >= 0 ? _w.Fields.Detritus[cell] : 0;" + nl + \
"    }" + nl + nl + \
"    public double TakeDetritus(int gx, int gz, double amount)" + nl + \
"    {" + nl + \
"        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);" + nl + \
"        int cell = _w.Grid.NearestDomainCell(p);" + nl + \
"        if (cell < 0 || amount <= 0) return 0;" + nl + \
"        double current = _w.Fields.Detritus[cell];" + nl + \
"        if (current <= 0) return 0;" + nl + \
"        double taken = Math.Min(current, amount);" + nl + \
"        _w.Fields.Detritus[cell] = current - taken;" + nl + \
"        return taken;" + nl + \
"    }" + nl + \
"}" + nl

content = content.rstrip() + nl + env_class

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")
