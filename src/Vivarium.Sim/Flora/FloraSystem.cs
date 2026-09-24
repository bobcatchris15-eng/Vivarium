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
        if (sp.RequiresFeature.Length > 0 && _w.Props.DistanceToFeature(p, sp.RequiresFeature) > sp.RequiresFeatureRadius)
            return FloraSuitability.Refused($"{sp.Name} needs a {sp.RequiresFeature} within {sp.RequiresFeatureRadius * 100:0} cm to climb", sub);

        // species-relation refusals (e.g. crust lichen cannot establish under moss)
        foreach (var rel in C.FloraInteractions.Relations)
        {
            if (rel.Type != FloraRelationType.Refuse || rel.A != sp.Id) continue;
            _w.Flora.Neighbours(p, rel.Radius, _nb);
            foreach (var n in _nb)
                if (n.Id != self && n.SpeciesId == rel.B) return FloraSuitability.Refused($"excluded by nearby {C.FloraOrThrow(rel.B).Name}: {rel.Reason}", sub);
        }

        double light = _w.Fields.Light.Sample(p);
        // decomposers judge their food (dead matter) where plants judge soil nutrients
        double nutrients = sp.Decomposer ? MathD.Clamp01(_w.Fields.Detritus.Sample(p) / C.Ecology.DetritusMax) : _w.Fields.Nutrients.Sample(p) / C.Ecology.NutrientMax;
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

    /// <summary>Per-colony cell cap (bounds instance count; roots beyond this stop budding).</summary>
    public const int ColonyCellCap = 260;
    private readonly List<(FloraSpeciesDef Sp, Vec2 P, EntityId Root, double RingDist)> _colonyBuds = new();
    private readonly Dictionary<EntityId, int> _colonySize = new();
    private readonly List<FloraIndividual> _nbColony = new();

    public void Step(double dt)
    {
        var births = new List<(FloraSpeciesDef Sp, Vec2 P)>();
        _buds.Clear();
        _colonyBuds.Clear();
        _colonySize.Clear();
        var deaths = new List<(FloraIndividual F, string Cause)>();
        var eco = C.Ecology;
        foreach (var f in _w.Flora.Items)
        {
            var sp0 = C.FloraOrThrow(f.SpeciesId);
            if (sp0.Colony == null) continue;
            var root0 = f.ColonyRoot.IsNone ? f.Id : f.ColonyRoot;
            _colonySize[root0] = _colonySize.GetValueOrDefault(root0) + 1;
        }
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
                if (f.Fruiting) grow = need = 0;   // fruiting bodies no longer feed
                if (need > 0)
                {
                    int cell = _w.Grid.NearestDomainCell(p);
                    if (sp.Decomposer)
                    {
                        // digest dead matter; about half is respired/mineralized straight back into the soil
                        double got = _w.Fields.Detritus.Take(cell, need);
                        grow *= need > 1e-15 ? got / need : 1;
                        double released = _w.Fields.Nutrients.Add(cell, got * 0.5);
                        _w.Tally.DetritusDecomposed += got;
                        _w.Tally.NutrientsFromDecomposers += released;
                    }
                    else
                    {
                        double got = _w.Fields.Nutrients.Take(cell, need);
                        grow *= need > 1e-15 ? got / need : 1;
                        _w.Tally.NutrientsUptake += got;
                    }
                }
                f.Biomass = Math.Min(sp.MaxBiomass, b0 + grow);
                if (!f.Fruiting) f.Health = Math.Min(1, f.Health + dt / SimUnits.Day * 0.5);   // spent sporangia don't recover
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

            if (sp.CreepSpeed > 0 && Creep(f, sp, dt, births)) continue;

            if (sp.Colony is { } cd)
            {
                ColonyStep(f, sp, cd, p, dt);
                // rare long-range spore: colonial species still occasionally found a new colony far away
                if (f.Stage(sp) != FloraStage.Juvenile && f.BiomassFraction(sp) >= sp.SpreadMinBiomassFraction
                    && f.Age - f.LastSpreadAge >= sp.SpreadInterval * 5 && sp.Propagules > 0)
                {
                    var rng = Rng.Keyed(_w.Seed, PropagationStream, f.Id.Value, (ulong)f.SpreadCount);
                    f.SpreadCount++;
                    f.LastSpreadAge = f.Age;
                    if (rng.NextDouble() < 0.1)
                    {
                        double ang = rng.Range(0, 2 * Math.PI), dist = sp.SpreadRadius * rng.Range(2.0, 4.0);
                        var q = p + Vec2.FromAngle(ang) * dist;
                        if (CanEstablish(sp, q, out _)) births.Add((sp, q));
                    }
                }
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
            var nf = Establish(sp, q, "propagation");
            if (sp.Colony != null) FoundColony(nf, sp);
            else if (sp.Archetype == "slime_mold") AssignSlimeTint(nf, 0);
        }
        SporeRain(dt);
        foreach (var (sp, q, parent, biomass) in _buds)
        {
            if (!CanEstablish(sp, q, out _)) { if (_w.Flora.Get(parent) is { } back) back.Biomass += biomass; continue; }
            var nf = Establish(sp, q, "growth front", biomass);
            nf.ParentId = parent;
            int gen = (_w.Flora.Get(parent)?.Generation ?? -1) + 1;
            AssignSlimeTint(nf, gen);
        }
        foreach (var (sp, q, root, ringDist) in _colonyBuds)
        {
            if (_colonySize.GetValueOrDefault(root) >= ColonyCellCap) continue;
            if (!CanEstablish(sp, q, out _)) continue;
            var nf = Establish(sp, q, "colony growth");
            nf.ColonyRoot = root; nf.RingDist = ringDist; nf.HeightFactor = 0.15;
            AssignColonyTint(nf, sp);
            _colonySize[root] = _colonySize.GetValueOrDefault(root) + 1;
        }
    }

    /// <summary>
    /// Slime-mold behaviour: the plasmodium creeps toward richer dead matter; after starving long enough it stops,
    /// releases spores onto the best nearby food and dies back. Returns true when normal propagation should be
    /// skipped this step (creepers only reproduce by fruiting).
    /// </summary>
    private readonly List<(FloraSpeciesDef Sp, Vec2 P, EntityId Parent, double Biomass)> _buds = new();

    /// <summary>A decomposer species this scarce keeps receiving airborne spores.</summary>
    public const int SporeBankThreshold = 3;
    private readonly Dictionary<string, int> _sporeCounts = new(StringComparer.Ordinal);

    /// <summary>
    /// Fungi and slime molds persist as a spore bank: while a species is scarce, roughly once per biological day a
    /// spore lands on the richest suitable litter among a few random spots and sprouts. Deterministic (keyed on
    /// species and tick), so decomposers return whenever conditions turn favourable instead of going extinct.
    /// </summary>
    private void SporeRain(double dt)
    {
        _sporeCounts.Clear();
        foreach (var f in _w.Flora.Items) _sporeCounts[f.SpeciesId] = _sporeCounts.GetValueOrDefault(f.SpeciesId) + 1;
        foreach (var sp in C.Flora)
        {
            if (sp.Archetype is not ("fungus" or "slime_mold")) continue;
            if (_sporeCounts.GetValueOrDefault(sp.Id) >= SporeBankThreshold) continue;
            var rng = Rng.Keyed(_w.Seed, "flora.spores." + sp.Id, (ulong)_w.Clock.Tick * 1_000_003UL + _w.Ids.LastSerial);
            if (rng.NextDouble() >= Math.Min(1, dt / SimUnits.Day)) continue;
            Vec2? best = null; double bestFood = double.NegativeInfinity;
            var cells = _w.Grid.DomainCells;
            for (int k = 0; k < 12; k++)
            {
                var q = _w.Grid.CellCenter(cells[rng.NextInt(cells.Length)]);
                double food = _w.Fields.Detritus.Sample(q);
                if (food > bestFood && CanEstablish(sp, q, out _)) { best = q; bestFood = food; }
            }
            if (best.HasValue)
            {
                var nf = Establish(sp, best.Value, "spores");
                if (sp.Archetype == "slime_mold") AssignSlimeTint(nf, 0);
            }
        }
    }

    /// <summary>Distance between a patch of plasmodium and the growth-front patch it buds (m).</summary>
    public const double BudStep = 0.15;

    private bool Creep(FloraIndividual f, FloraSpeciesDef sp, double dt, List<(FloraSpeciesDef Sp, Vec2 P)> births)
    {
        var p = f.Position;
        double food = _w.Fields.Detritus.Sample(p);
        if (f.Fruiting)
        {
            f.Health = Math.Max(0, f.Health - dt / SimUnits.Day);   // sporangia last about a day
            return true;
        }
        // retraction: patches left on exhausted ground thin out and vanish (their matter flows to the fronts)
        if (food < sp.FoodThreshold) f.Biomass *= Math.Exp(-2.0 * dt / SimUnits.Day);
        f.StarvedFor = food < sp.FoodThreshold ? f.StarvedFor + dt : Math.Max(0, f.StarvedFor - dt * 2);
        if (f.StarvedFor >= sp.StarvedToFruit && f.Stage(sp) != FloraStage.Juvenile)
        {
            f.Fruiting = true;
            var rng = Rng.Keyed(_w.Seed, PropagationStream, f.Id.Value, (ulong)f.SpreadCount++);
            for (int k = 0; k < Math.Max(1, sp.Propagules); k++)
            {
                var q = p + Vec2.FromAngle(rng.Range(0, 2 * Math.PI)) * sp.SpreadRadius * rng.Range(0.4, 1.0);
                if (_w.Fields.Detritus.Sample(q) >= sp.FoodThreshold && CanEstablish(sp, q, out _)) births.Add((sp, q));
            }
            return true;
        }
        // growth front: the network does not slide as one body. A well-fed patch accumulates advance and buds a new
        // connected patch toward the richest food it senses (~1 m, nearer counts more, like a chemical gradient),
        // handing it part of its biomass; starved patches behind retract. The whole thing appears to flow.
        f.CreepCredit = Math.Min(f.CreepCredit + sp.CreepSpeed * dt, BudStep * 2);
        if (f.CreepCredit < BudStep || f.BiomassFraction(sp) < 0.25) return true;
        const double Sense = 1.0;
        double Weight(double r) => 1 - r / (Sense * 1.25);
        double stay = 0;
        for (double r = 0.25; r <= Sense + 1e-9; r += 0.25) stay += food * Weight(r);
        var best = p; double bestScore = stay * 1.02;
        for (int k = 0; k < 8; k++)
        {
            var dir = Vec2.FromAngle(k * Math.PI / 4);
            double score = 0;
            for (double r = 0.25; r <= Sense + 1e-9; r += 0.25) score += _w.Fields.Detritus.Sample(p + dir * r) * Weight(r);
            var q = p + dir * BudStep;
            if (score > bestScore && CanEstablish(sp, q, out _)) { best = q; bestScore = score; }
        }
        if (best == p)
        {
            // no richer direction: on adequate food it keeps exploring, fanning outward away from where it came from
            if (food < sp.FoodThreshold * 1.5) return true;
            var from = _w.Flora.Get(f.ParentId) is { } par ? p - par.Position : Vec2.Zero;
            var rng = Rng.Keyed(_w.Seed, PropagationStream, f.Id.Value, (ulong)(1000 + f.SpreadCount++));
            double ang = (from.LengthSq > 1e-10 ? from.Angle : rng.Range(0, 2 * Math.PI)) + rng.Range(-0.9, 0.9);
            var q = p + Vec2.FromAngle(ang) * BudStep;
            if (!CanEstablish(sp, q, out _)) return true;
            best = q;
        }
        double share = f.Biomass * 0.4;
        f.Biomass -= share;
        f.CreepCredit -= BudStep;
        _buds.Add((sp, best, f.Id, share));
        return true;
    }

    /// <summary>
    /// Colonial growth (moss/lichen): a cell with fewer than 3 same-species neighbours within 2·cellRadius is on
    /// the rim and buds outward, away from the neighbour centroid plus jitter. Interior cells stop spreading and
    /// thicken toward the species' colony max height instead.
    /// </summary>
    private void ColonyStep(FloraIndividual f, FloraSpeciesDef sp, FloraColonyDef cd, Vec2 p, double dt)
    {
        _w.Flora.Neighbours(p, cd.CellRadius * 2, _nbColony);
        int neighbourCount = 0; var centroidSum = Vec2.Zero;
        foreach (var n in _nbColony)
        {
            if (n.Id == f.Id || n.SpeciesId != sp.Id) continue;
            neighbourCount++; centroidSum += n.Position;
        }
        bool edge = neighbourCount < 3;
        var root = f.ColonyRoot.IsNone ? f.Id : f.ColonyRoot;
        if (edge)
        {
            if (_colonySize.GetValueOrDefault(root) >= ColonyCellCap) return;
            var rng = Rng.Keyed(_w.Seed, "flora.colony.bud", f.Id.Value, (ulong)f.SpreadCount++);
            if (rng.NextDouble() >= Math.Min(1, cd.FrontRate * dt)) return;
            var away = neighbourCount > 0 ? p - centroidSum / neighbourCount : Vec2.Zero;
            double baseAngle = away.LengthSq > 1e-10 ? away.Angle : rng.Range(0, 2 * Math.PI);
            double ang = baseAngle + rng.Range(-0.6, 0.6);
            double dist = cd.CellRadius * rng.Range(1.5, 2.0);
            var q = p + Vec2.FromAngle(ang) * dist;
            if (CanEstablish(sp, q, out _)) _colonyBuds.Add((sp, q, root, f.RingDist + dist));
        }
        else
        {
            f.HeightFactor = Math.Min(1, f.HeightFactor + cd.FillRate * dt);
        }
    }

    /// <summary>Marks a newly established individual as the founder (root) of a new colony and assigns its tint.</summary>
    private void FoundColony(FloraIndividual f, FloraSpeciesDef sp)
    {
        f.ColonyRoot = f.Id; f.RingDist = 0; f.HeightFactor = 0.15;
        AssignColonyTint(f, sp);
    }

    /// <summary>Chooses a per-instance tint from the species' colony palette: random pick for moss, banded by
    /// distance from the colony root for lichen (both with a small jitter).</summary>
    private void AssignColonyTint(FloraIndividual f, FloraSpeciesDef sp)
    {
        var cd = sp.Colony;
        if (cd == null || cd.Palette.Length == 0) { f.Tint = new double[] { 1, 1, 1 }; return; }
        var rng = Rng.Keyed(_w.Seed, "flora.colony.tint", f.Id.Value);
        int idx;
        if (cd.PatternMode == ColonyPattern.Banded)
        {
            double noise = rng.Range(-0.4, 0.4);
            int raw = (int)Math.Floor(f.RingDist / Math.Max(cd.BandWidth, 1e-6) + noise);
            idx = ((raw % cd.Palette.Length) + cd.Palette.Length) % cd.Palette.Length;
        }
        else idx = rng.NextInt(cd.Palette.Length);
        var baseC = cd.Palette[idx];
        const double jitter = 0.05;
        f.Tint = new[]
        {
            MathD.Clamp01(baseC[0] + rng.Range(-jitter, jitter)),
            MathD.Clamp01(baseC[1] + rng.Range(-jitter, jitter)),
            MathD.Clamp01(baseC[2] + rng.Range(-jitter, jitter)),
        };
    }

    /// <summary>Slime mold network-depth tint: root/founder cells (generation 0) are dark ochre, the growth front
    /// brightens toward saturated yellow as generation rises, plus a small per-cell jitter. Set once at bud
    /// creation from the parent's stored generation, not re-walked from ParentId each frame.</summary>
    private static readonly double[] SlimeOldTint = { 0.55, 0.42, 0.12 };
    private static readonly double[] SlimeFrontTint = { 1.0, 0.95, 0.2 };
    private const int SlimeGenerationSpan = 10;

    private void AssignSlimeTint(FloraIndividual f, int generation)
    {
        f.Generation = generation;
        double t = Math.Clamp(generation / (double)SlimeGenerationSpan, 0, 1);
        var rng = Rng.Keyed(_w.Seed, "flora.slime.tint", f.Id.Value);
        const double jitter = 0.05;
        f.Tint = new[]
        {
            MathD.Clamp01(SlimeOldTint[0] + (SlimeFrontTint[0] - SlimeOldTint[0]) * t + rng.Range(-jitter, jitter)),
            MathD.Clamp01(SlimeOldTint[1] + (SlimeFrontTint[1] - SlimeOldTint[1]) * t + rng.Range(-jitter, jitter)),
            MathD.Clamp01(SlimeOldTint[2] + (SlimeFrontTint[2] - SlimeOldTint[2]) * t + rng.Range(-jitter, jitter)),
        };
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
