using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

/// <summary>Habitat suitability with hard refusal reported separately from low suitability.</summary>
public sealed class FloraSuitability
{
    public bool HardRefused { get; init; }
    public string RefusalReason { get; init; } = "";
    public double Score { get; init; }
    public Substrate Substrate { get; init; }
    public double SubstrateFactor { get; init; }
    public double MoistureFactor { get; init; }
    public double LightFactor { get; init; }
    public double NutrientFactor { get; init; }
    public double ProximityBonus { get; init; }
    public double Moisture { get; init; }
    public double Light { get; init; }
    public double Nutrients { get; init; }

    public static FloraSuitability Refused(string reason, Substrate s) => new() { HardRefused = true, RefusalReason = reason, Score = 0, Substrate = s };
    public override string ToString() => HardRefused ? $"refused: {RefusalReason}" :
        $"score {Score:0.00} (substrate {SubstrateFactor:0.00}, moisture {MoistureFactor:0.00}, light {LightFactor:0.00}, nutrients {NutrientFactor:0.00}, bonus +{ProximityBonus:0.00})";
}

/// <summary>Flora ecology: suitability, growth, nutrient uptake, competition, propagation, death + litter.</summary>
public sealed class FloraSystem
{
    private readonly VivariumWorld _w;
    private readonly List<FloraIndividual> _nb = new();
    public const string PropagationStream = "flora.propagation";

    public FloraSystem(VivariumWorld w) { _w = w; }

    private ContentLibrary C => _w.Content;

    public FloraSuitability Suitability(FloraSpeciesDef sp, Vec2 p, EntityId self = default)
    {
        if (!_w.Domain.ContainsDisc(p, 0.02)) return FloraSuitability.Refused("outside the island", Substrate.Soil);
        var sub = _w.SubstrateAt(p);
        if (sp.RefuseSubstrates.Contains(sub)) return FloraSuitability.Refused($"{sp.Name} refuses {SubstrateIds.Id(sub)} substrate", sub);
        foreach (var tag in _w.Props.HabitatTagsAt(p))
            if (sp.RefuseTags.Contains(tag)) return FloraSuitability.Refused($"{sp.Name} refuses '{tag}' habitat", sub);
        double depth = _w.Water.DepthAt(p);
        bool onProp = sub is Substrate.Rock or Substrate.Wood && !double.IsNaN(_w.Props.PropTopAt(p));
        if (!onProp && depth > sp.MaxWaterDepth + 1e-9) return FloraSuitability.Refused($"submerged ({depth * 100:0.#} cm, tolerates {sp.MaxWaterDepth * 100:0.#} cm)", sub);
        double moisture = _w.Fields.Moisture.Sample(p);
        if (moisture < sp.HardMinMoisture) return FloraSuitability.Refused($"too dry (moisture {moisture:0.00} < {sp.HardMinMoisture:0.00})", sub);

        // species-relation refusals (e.g. crust lichen cannot establish under moss)
        foreach (var rel in C.FloraInteractions.Relations)
        {
            if (rel.Type != FloraRelationType.Refuse || rel.A != sp.Id) continue;
            _w.Flora.Neighbours(p, rel.Radius, _nb);
            foreach (var n in _nb)
                if (n.Id != self && n.SpeciesId == rel.B) return FloraSuitability.Refused($"excluded by nearby {C.FloraOrThrow(rel.B).Name}: {rel.Reason}", sub);
        }

        double light = _w.Fields.Light.Sample(p);
        double nutrients = _w.Fields.Nutrients.Sample(p) / C.Ecology.NutrientMax;
        double fs = sp.SubstrateAffinity.GetValueOrDefault(sub);
        double fm = sp.Moisture.Eval(moisture), fl = sp.Light.Eval(light), fn = sp.Nutrients.Eval(nutrients);
        double env = Math.Cbrt(fm * fl * fn);
        double bonus = ProximityBonus(sp, p, self);
        double score = MathD.Clamp01(fs * env * (1 + bonus));
        return new FloraSuitability
        {
            Score = score, Substrate = sub, SubstrateFactor = fs, MoistureFactor = fm, LightFactor = fl, NutrientFactor = fn,
            ProximityBonus = bonus, Moisture = moisture, Light = light, Nutrients = nutrients,
        };
    }

    /// <summary>Sum of configured beneficial-proximity bonuses active at p (capped at 0.5).</summary>
    public double ProximityBonus(FloraSpeciesDef sp, Vec2 p, EntityId self = default)
    {
        double bonus = 0;
        foreach (var rule in sp.Proximity)
        {
            if (rule.IsFeature)
            {
                double d = rule.Feature == "water" ? DistanceToWater(p, rule.Radius) : _w.Props.DistanceToFeature(p, rule.Feature);
                if (d <= rule.Radius) bonus += rule.Bonus;
            }
            else if (AnyFloraNear(p, rule.Target, rule.Radius, self)) bonus += rule.Bonus;
        }
        foreach (var rel in C.FloraInteractions.Relations)
            if (rel.Type == FloraRelationType.Benefit && rel.A == sp.Id && AnyFloraNear(p, rel.B, rel.Radius, self)) bonus += rel.Bonus;
        return Math.Min(bonus, 0.5);
    }

    private bool AnyFloraNear(Vec2 p, string species, double radius, EntityId self)
    {
        _w.Flora.Neighbours(p, radius, _nb);
        foreach (var n in _nb) if (n.Id != self && n.SpeciesId == species) return true;
        return false;
    }

    private double DistanceToWater(Vec2 p, double maxR)
    {
        if (_w.Water.IsWet(p)) return 0;
        double best = double.PositiveInfinity;
        foreach (int c in _w.Grid.CellsInRadius(p, maxR))
            if (_w.Water.IsWet(c)) best = Math.Min(best, Vec2.Distance(p, _w.Grid.CellCenter(c)));
        return best;
    }

    /// <summary>Weighted crowding at p: Σ competition weight × neighbour biomass fraction.</summary>
    public double Crowding(FloraSpeciesDef sp, Vec2 p, EntityId self)
    {
        _w.Flora.Neighbours(p, sp.CompetitionRadius, _nb);
        double crowd = 0;
        foreach (var n in _nb)
        {
            if (n.Id == self) continue;
            var nsp = C.FloraOrThrow(n.SpeciesId);
            crowd += C.FloraInteractions.CompetitionWeight(sp.Id, n.SpeciesId) * n.BiomassFraction(nsp);
        }
        return crowd;
    }

    public void Step(double dt)
    {
        var births = new List<(FloraSpeciesDef Sp, Vec2 P)>();
        var deaths = new List<(FloraIndividual F, string Cause)>();
        var eco = C.Ecology;
        foreach (var f in _w.Flora.Items)
        {
            var sp = C.FloraOrThrow(f.SpeciesId);
            var p = f.Position;
            f.Age += dt;
            var suit = Suitability(sp, p, f.Id);
            f.LastSuitability = suit.Score;
            double crowd = Crowding(sp, p, f.Id);
            double comp = 1 / (1 + sp.CompetitionSensitivity * crowd);

            if (!suit.HardRefused && suit.Score >= sp.MinSuitability)
            {
                double r = sp.GrowthRate * suit.Score * comp;
                double k = sp.MaxBiomass;
                double b0 = f.Biomass;
                double b1 = k * b0 / (b0 + (k - b0) * Math.Exp(-r * dt));   // exact logistic over dt
                double grow = Math.Max(0, b1 - b0);
                double need = grow * sp.NutrientPerBiomass;
                if (need > 0)
                {
                    int cell = _w.Grid.NearestDomainCell(p);
                    double got = _w.Fields.Nutrients.Take(cell, need);
                    grow *= need > 1e-15 ? got / need : 1;
                    _w.Tally.NutrientsUptake += got;
                }
                f.Biomass = Math.Min(sp.MaxBiomass, b0 + grow);
                f.Health = Math.Min(1, f.Health + dt / SimUnits.Day * 0.5);
            }
            else
            {
                double severity = suit.HardRefused ? 1 : 1 - suit.Score / Math.Max(sp.MinSuitability, 1e-6);
                f.Biomass *= Math.Exp(-sp.DeclineRate * severity * dt);
                f.Health = Math.Max(0, f.Health - severity * dt / SimUnits.Day * (suit.HardRefused ? 1.0 : 0.35));
            }
            // continuous litter drop feeds the shared detritus pathway (the springtails' staple)
            if (sp.SheddingRate > 0)
            {
                double shed = f.Biomass * (1 - Math.Exp(-sp.SheddingRate * dt));
                f.Biomass -= shed;
                _w.Ecology.ReturnOrganicMatter(p, shed * sp.LitterFraction, 0, fromFlora: true);
            }
            // senescence
            if (f.Age > sp.Lifespan * f.LifespanFactor) f.Health = Math.Max(0, f.Health - dt / SimUnits.Day);

            if (f.Health <= 0 || f.Biomass < sp.InitialBiomass * 0.25)
            {
                deaths.Add((f, f.Age > sp.Lifespan * f.LifespanFactor ? "senescence" : suit.HardRefused ? "habitat" : "decline"));
                continue;
            }

            // propagation
            if (f.Stage(sp) != FloraStage.Juvenile && f.BiomassFraction(sp) >= sp.SpreadMinBiomassFraction
                && f.Age - f.LastSpreadAge >= sp.SpreadInterval && sp.Propagules > 0)
            {
                var rng = Rng.Keyed(_w.Seed, PropagationStream, f.Id.Value, (ulong)f.SpreadCount);
                f.SpreadCount++;
                f.LastSpreadAge = f.Age;
                for (int k2 = 0; k2 < sp.Propagules; k2++)
                {
                    double ang = rng.Range(0, 2 * Math.PI), dist = sp.SpreadRadius * rng.Range(0.3, 1.0);
                    var q = p + Vec2.FromAngle(ang) * dist;
                    if (CanEstablish(sp, q, out _)) births.Add((sp, q));
                }
            }
        }

        foreach (var (f, cause) in deaths) Kill(f, cause);
        foreach (var (sp, q) in births)
        {
            if (!CanEstablish(sp, q, out _)) continue; // re-check against same-step recruits
            Establish(sp, q, "propagation");
        }
    }

    /// <summary>True when a new individual of sp may establish at q (no hard refusal, adequate suitability, not overcrowded).</summary>
    public bool CanEstablish(FloraSpeciesDef sp, Vec2 q, out string reason)
    {
        var s = Suitability(sp, q);
        if (s.HardRefused) { reason = s.RefusalReason; return false; }
        if (s.Score < sp.MinSuitability) { reason = $"habitat too poor ({s})"; return false; }
        double crowd = Crowding(sp, q, EntityId.None);
        if (crowd >= sp.CrowdingLimit) { reason = $"too crowded ({crowd:0.00} ≥ {sp.CrowdingLimit:0.00})"; return false; }
        reason = "ok";
        return true;
    }

    public FloraIndividual Establish(FloraSpeciesDef sp, Vec2 q, string cause, double? biomass = null)
    {
        var id = _w.Ids.Next(EntityKind.Flora);
        var rng = Rng.Keyed(_w.Seed, "flora.establish", id.Value);
        var f = new FloraIndividual
        {
            Id = id, SpeciesId = sp.Id, X = q.X, Z = q.Z, Biomass = biomass ?? sp.InitialBiomass, Health = 1,
            LifespanFactor = rng.Range(0.8, 1.2),
        };
        _w.Flora.Add(f);
        _w.Tally.Birth(sp.Id);
        return f;
    }

    /// <summary>Removes an individual through the ordinary ecological pathway: litter → detritus, remainder → nutrients. Exactly once.</summary>
    public bool Kill(FloraIndividual f, string cause)
    {
        if (!_w.Flora.Remove(f.Id)) return false;
        var sp = C.FloraOrThrow(f.SpeciesId);
        double litter = f.Biomass * sp.LitterFraction;
        double direct = f.Biomass * (1 - sp.LitterFraction) * sp.NutrientPerBiomass;
        _w.Ecology.ReturnOrganicMatter(f.Position, litter, direct, fromFlora: true);
        _w.Tally.Death(sp.Id, cause);
        return true;
    }
}
