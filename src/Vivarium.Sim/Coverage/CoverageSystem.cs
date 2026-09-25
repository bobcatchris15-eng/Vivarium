using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage.Rules;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage;

/// <summary>
/// Runs moss (Mat layer) and lichen (Crust layer) growth against the real world (docs/overhaul/growth_models.md
/// §4, §5, §8, §9): a scheduler-driven <see cref="Step"/> at the flora cadence, and <see cref="SeedInitial"/> to
/// place each coverage species' first presence at world creation (small discs, not individuals). Species
/// parameter tables (occupant ids into each layer's <c>Occ</c> byte) are built once from
/// <see cref="ContentLibrary.Flora"/> at construction.
/// </summary>
public sealed class CoverageSystem : IMicroEnvSource, ILichenEnvSource, IDetritusSink
{
    private readonly VivariumWorld _w;
    private readonly List<MatParams> _matSpecies = new();
    private readonly Dictionary<byte, string> _matSpeciesId = new();
    private readonly List<LichenParams> _lichenSpecies = new();
    private readonly Dictionary<byte, string> _lichenSpeciesId = new();
    private long _step;

    /// <summary>Number of coverage growth steps completed; part of authoritative state because rules hash it.</summary>
    public long StepIndex => _step;

    internal void RestoreStep(long step)
    {
        if (step < 0) throw new InvalidDataException($"coverage step is invalid ({step})");
        _step = step;
    }

    /// <summary>Per-phase cost of the last Mat step (docs/overhaul/growth_models.md §10), for the perf print.</summary>
    public MatStepStats LastMatStats { get; private set; }

    public CoverageSystem(VivariumWorld w)
    {
        _w = w;
        byte matId = 0, lichenId = 0;
        foreach (var sp in w.Content.Flora)
        {
            if (sp.Mat is { } md)
            {
                matId++;
                _matSpeciesId[matId] = sp.Id;
                _matSpecies.Add(new MatParams(matId, sp.Id, md.HeightForm, md.Lateral, md.MaxHeightM, md.GrowthRatePerDay,
                    md.KWet, md.KDry, md.WMin, md.WOpt, md.KLight, md.LightMax, md.DormBrownDays, md.DormDeathDays,
                    md.SporeRate, md.MoistureFeedback, sp.HardMinMoisture, md.SeedBiomass, md.DomeLength));
            }
            if (sp.Lichen is { } ld)
            {
                lichenId++;
                _lichenSpeciesId[lichenId] = sp.Id;
                _lichenSpecies.Add(new LichenParams
                {
                    OccSlot = lichenId,
                    Form = ld.Form,
                    Substrates = ld.Substrates.ToArray(),
                    GravelRateMultiplier = ld.GravelRateMultiplier,
                    Lateral = ld.Lateral,
                    TipBiasGamma = ld.TipBiasGamma,
                    OpennessRadius = ld.OpennessRadius,
                    MinNeighboursToColonise = ld.MinNeighboursToColonise,
                    CentreDeathDays = ld.CentreDeathDays,
                    CentreDeathMinD2E = ld.CentreDeathMinD2E,
                    DeadDecayPerDay = ld.DeadDecayPerDay,
                    KWet = ld.KWet,
                    KDry = ld.KDry,
                    WMin = ld.WMin,
                    WOpt = ld.WOpt,
                    KLight = ld.KLight,
                    SeedBiomass = ld.SeedBiomass,
                    MaxBiomass = ld.MaxBiomass,
                    EdenNoiseExponent = ld.EdenNoiseExponent,
                });
            }
        }
    }

    public bool HasCoverageSpecies => _matSpecies.Count > 0 || _lichenSpecies.Count > 0;

    // ------------------------------------------------------------------ IMicroEnvSource / ILichenEnvSource

    public MicroEnv Sample(int gx, int gz)
    {
        var p = CellCentre(gx, gz);
        return CoverageEnvironment.Sample(_w, p);
    }

    // ------------------------------------------------------------------ IDetritusSink

    public void AddDetritus(int gx, int gz, double amount)
    {
        var p = CellCentre(gx, gz);
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell >= 0) _w.Fields.Detritus.Add(cell, amount);
    }

    public void AddMoistureBonus(int gx, int gz, double amount)
    {
        var p = CellCentre(gx, gz);
        CoverageEnvironment.MoistureBonusOf(_w).Add(p, amount);
    }

    private static Vec2 CellCentre(int gx, int gz) => new((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);

    // ------------------------------------------------------------------ step (scheduler, flora cadence)

    public void Step(double dtSeconds)
    {
        double dtDays = dtSeconds / SimUnits.Day;
        if (_matSpecies.Count > 0)
            LastMatStats = MatRules.Step(_w.Coverage.Mat, this, _matSpecies, dtDays, _step, _w.Seed, this);
        if (_lichenSpecies.Count > 0)
            LichenRules.Step(_w.Coverage.Crust, this, _lichenSpecies, dtDays, _step, _w.Seed);
        _step++;
    }

    // ------------------------------------------------------------------ initial seeding (world creation)

    /// <summary>Seeds a few small discs per coverage species where the terrain already suits it, so growth (not
    /// individual placement) is what fills the island out from world creation onward.</summary>
    public void SeedInitial()
    {
        const int DiscsPerSpecies = 5;
        const double MinSeparation = 1.2; // m, keeps starter discs spread across the island
        const int DiscRadiusCells = 4;    // ~8 cm discs

        foreach (var mp in _matSpecies)
            SeedSpecies(_w.Coverage.Mat, mp.OccupantId, mp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells,
                p => IsMatSuitable(mp, p));

        foreach (var lp in _lichenSpecies)
            SeedSpecies(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells,
                p => IsLichenSuitable(lp, p));
    }

    private bool IsMatSuitable(MatParams mp, Vec2 p)
    {
        var e = CoverageEnvironment.Sample(_w, p);
        if (e.Substrate == CoverageSubstrate.Water) return false;
        if (e.Moisture < mp.HardMinMoisture) return false;
        double gW = WaterBalance.GrowthMultiplierWater(e.Moisture, mp);
        double gL = WaterBalance.GrowthMultiplierLight(e.Light, mp);
        return gW * gL > 0.15;
    }

    private bool IsLichenSuitable(LichenParams lp, Vec2 p)
    {
        var e = CoverageEnvironment.Sample(_w, p);
        if (!lp.AllowsSubstrate(e.Substrate)) return false;
        double gW = LichenWater.GrowthWaterFactor(e.Moisture, lp.WMin, lp.WOpt);
        double gL = LichenWater.GrowthLightFactor(e.Light, lp.KLight);
        return gW * gL > 0.05;
    }

    private void SeedSpecies(CoverageLayer layer, byte occ, double seedB, int discCount, double minSeparation, int radiusCells, Func<Vec2, bool> suitable)
    {
        var rng = Rng.Stream(_w.Seed, "coverage.seed." + layer.Id + "." + occ);
        var cells = _w.Grid.DomainCells;
        if (cells.Length == 0) return;

        // Sample a bounded number of shuffled candidates rather than every domain cell (island-size independent).
        var order = Enumerable.Range(0, cells.Length).ToList();
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (order[i], order[j]) = (order[j], order[i]); }

        var chosen = new List<Vec2>();
        int budget = Math.Min(cells.Length, 4000);
        for (int k = 0; k < budget && chosen.Count < discCount; k++)
        {
            var p = _w.Grid.CellCenter(cells[order[k]]);
            if (!suitable(p)) continue;
            bool tooClose = false;
            foreach (var c in chosen) if (Vec2.Distance(c, p) < minSeparation) { tooClose = true; break; }
            if (tooClose) continue;
            chosen.Add(p);
        }

        byte seedW = (byte)Math.Round(0.6 * 255);
        foreach (var p in chosen)
        {
            var (gx0, gz0) = CoverageSpec.CellOf(p);
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                if (dx * dx + dz * dz > radiusCells * radiusCells) continue;
                int gx = gx0 + dx, gz = gz0 + dz;
                if (!suitable(CellCentre(gx, gz)) || layer.GetOcc(gx, gz) != 0) continue;
                layer.SetCell(gx, gz, occ, (float)seedB, seedW, 0, 0, 0, 0);
            }
        }
    }

    // ------------------------------------------------------------------ queries (stats/catalog, §7, §9)

    /// <summary>Resolves an occupied coverage cell to its content species. Occupant slots are local to each
    /// layer; zero and unsupported layers have no species.</summary>
    public string? SpeciesId(CoverageLayerId layer, byte occupant) => layer switch
    {
        CoverageLayerId.Mat => _matSpeciesId.GetValueOrDefault(occupant),
        CoverageLayerId.Crust => _lichenSpeciesId.GetValueOrDefault(occupant),
        _ => null,
    };

    /// <summary>Total covered area (m²) for a species id, across whichever coverage layer it occupies. 0 for a
    /// non-coverage species or one with nothing grown yet.</summary>
    public double CoveredArea(string speciesId)
    {
        double cellArea = CoverageSpec.CellSize * CoverageSpec.CellSize;
        double total = 0;
        foreach (var (occ, id) in _matSpeciesId)
            if (id == speciesId) total += CountOccupied(_w.Coverage.Mat, occ) * cellArea;
        foreach (var (occ, id) in _lichenSpeciesId)
            if (id == speciesId) total += CountOccupied(_w.Coverage.Crust, occ) * cellArea;
        return total;
    }

    private static long CountOccupied(CoverageLayer layer, byte occ)
    {
        long n = 0;
        foreach (var t in layer.Tiles)
            for (int i = 0; i < CoverageTile.N; i++)
                if (t.Occ[i] == occ) n++;
        return n;
    }
}
