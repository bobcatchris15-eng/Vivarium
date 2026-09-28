using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

public sealed class SeedLot
{
    public string SpeciesId { get; set; } = "";
    public int Cell { get; set; }
    public double ViableMass { get; set; }
    public double DormancyRemaining { get; set; }
    public double Age { get; set; }
    public double LastAttemptAge { get; set; }
    public int AttemptCount { get; set; }
}

public sealed class SeedBank
{
    private readonly VivariumWorld _w;
    public List<SeedLot> Lots { get; } = new();
    public SeedBank(VivariumWorld w) { _w = w; }

    public void Deposit(string speciesId, Vec2 p, double viableMass, double dormancy)
    {
        if (viableMass <= 0) return;
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell < 0) return;
        var lot = Lots.FirstOrDefault(x => x.Cell == cell && x.SpeciesId == speciesId);
        if (lot == null)
        {
            lot = new SeedLot { SpeciesId = speciesId, Cell = cell, ViableMass = viableMass, DormancyRemaining = dormancy };
            Lots.Add(lot);
            Lots.Sort((a, b) => a.Cell != b.Cell ? a.Cell.CompareTo(b.Cell) : string.CompareOrdinal(a.SpeciesId, b.SpeciesId));
        }
        else
        {
            lot.ViableMass += viableMass;
            lot.DormancyRemaining = Math.Max(lot.DormancyRemaining, dormancy);
        }
        _w.Tally.SeedsDeposited += viableMass;
    }

    public void Step(double dt)
    {
        if (!(dt > 0) || Lots.Count == 0) return;
        var remove = new List<SeedLot>();
        foreach (var lot in Lots)
        {
            lot.Age += dt;
            lot.DormancyRemaining = Math.Max(0, lot.DormancyRemaining - dt);
            lot.ViableMass *= Math.Exp(-Math.Log(2) / (540 * SimUnits.Day) * dt);
            if (lot.ViableMass < 0.001) { remove.Add(lot); continue; }
            if (lot.DormancyRemaining > 0 || lot.Age - lot.LastAttemptAge < 5 * SimUnits.Day) continue;
            lot.LastAttemptAge = lot.Age;
            var sp = _w.Content.FloraById(lot.SpeciesId);
            if (sp == null || sp.IsCoverageSpecies) { remove.Add(lot); continue; }
            var q = _w.Grid.CellCenter(lot.Cell);
            if (!_w.FloraSystem.CanEstablish(sp, q, out _)) { lot.AttemptCount++; continue; }
            ulong key = Hash.Fnv1a64(lot.SpeciesId);
            var rng = Rng.Keyed(_w.Seed, "seedbank.germination", Rng.Mix(key, (ulong)lot.Cell), (ulong)lot.AttemptCount++);
            double chance = Math.Min(0.75, 0.12 + lot.ViableMass * 0.8);
            if (rng.NextDouble() > chance) continue;
            _w.FloraSystem.Establish(sp, q, "seed bank");
            lot.ViableMass = Math.Max(0, lot.ViableMass - Math.Max(0.01, sp.InitialBiomass * 0.08));
            _w.Tally.SeedsGerminated++;
            if (lot.ViableMass < 0.001) remove.Add(lot);
        }
        foreach (var lot in remove) Lots.Remove(lot);
    }
}

public sealed class ReproductionSystem
{
    private readonly VivariumWorld _w;
    public ReproductionSystem(VivariumWorld w) { _w = w; }

    public void Step(double dt)
    {
        if (!(dt > 0)) return;
        foreach (var f in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            var rp = sp.Reproduction;
            if (rp == null || f.Stage(sp) == FloraStage.Juvenile || f.Health <= 0.35) continue;
            f.ReproductiveStageAge += dt;

            if (f.ReproductiveStage == PlantReproductiveStage.Dormant)
            {
                double condition = MathD.Clamp01(f.LastSuitability) * f.BiomassFraction(sp);
                f.ReproductiveReserve += rp.ReserveRate * condition * dt;
                if (f.ReproductiveReserve >= rp.PulseThreshold)
                {
                    f.ReproductiveReserve -= rp.PulseThreshold;
                    f.ReproductiveStage = PlantReproductiveStage.Developing;
                    f.ReproductiveStageAge = 0;
                    f.FruitLoad = Math.Max(f.FruitLoad, rp.MaxAttachedMass * 0.05);
                    f.FruitPulseCount++;
                }
            }
            else if (f.ReproductiveStage == PlantReproductiveStage.Developing)
            {
                double t = MathD.Clamp01(f.ReproductiveStageAge / rp.DevelopmentTime);
                f.FruitLoad = Math.Max(f.FruitLoad, rp.MaxAttachedMass * t);
                if (t >= 1)
                {
                    f.ReproductiveStage = PlantReproductiveStage.Ripe;
                    f.ReproductiveStageAge = 0;
                    f.FruitLoad = rp.MaxAttachedMass;
                }
            }
            else if (f.ReproductiveStage == PlantReproductiveStage.Ripe)
            {
                double release = Math.Min(f.FruitLoad, rp.MaxAttachedMass * dt / rp.RipeTime);
                if (release > 0) { f.FruitLoad -= release; Release(f, sp, rp, release); }
                if (f.ReproductiveStageAge >= rp.RipeTime || f.FruitLoad <= rp.MaxAttachedMass * 0.001)
                {
                    if (f.FruitLoad > 0) Release(f, sp, rp, f.FruitLoad);
                    f.FruitLoad = 0;
                    f.ReproductiveStage = PlantReproductiveStage.Spent;
                    f.ReproductiveStageAge = 0;
                }
            }
            else if (f.ReproductiveStageAge >= rp.CooldownTime)
            {
                f.ReproductiveStage = PlantReproductiveStage.Dormant;
                f.ReproductiveStageAge = 0;
            }
        }
    }

    private void Release(FloraIndividual f, FloraSpeciesDef sp, ReproductionDef rp, double mass)
    {
        double seedMass = mass * rp.SeedFraction * rp.Viability;
        double fleshMass = Math.Max(0, mass - mass * rp.SeedFraction);
        ulong pulseKey = Rng.Mix(f.Id.Value, (ulong)f.FruitPulseCount);
        ulong dayKey = (ulong)Math.Max(0, (long)(f.ReproductiveStageAge / Math.Max(1, SimUnits.Day)));
        var rng = Rng.Keyed(_w.Seed, "flora.fruit.release", pulseKey, dayKey);
        double distance = rp.Dispersal switch
        {
            "wind" => rp.DispersalRadius * rng.Range(0.45, 1.0),
            "ballistic" => rp.DispersalRadius * rng.Range(0.55, 1.0),
            _ => rp.DispersalRadius * rng.Range(0.05, 0.35),
        };
        var q = f.Position + Vec2.FromAngle(rng.Range(0, Math.PI * 2)) * distance;
        if (!_w.Domain.Contains(q)) q = f.Position;
        if (fleshMass > 0) _w.Litter.DepositFruit(q, fleshMass, Math.Max(0.08, rp.DisplaySize * 4));
        if (seedMass > 0) _w.SeedBank.Deposit(sp.Id, q, seedMass, rp.DormancyTime);
    }
}
