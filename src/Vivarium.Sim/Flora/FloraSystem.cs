using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

/// <summary>Habitat suitability with hard refusal reported separately from low suitability.</summary>
public sealed class FloraSuitability
{
    public bool HardRefused { get; init; }
    public string RefusalReason { get; init; } = "";
    public double Score { get; init; }
    public double GrowthScore { get; init; }
    public double ReproductionScore { get; init; }
    public double Oxygen { get; init; }
    public double TopsoilDepth { get; init; }
    public double OxygenFactor { get; init; }
    public double TopsoilFactor { get; init; }
    public double EstablishmentScore { get; init; }
    public double SurvivalScore { get; init; }
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
    /// <summary>Scratch for the neighbour queries. It is per thread because the
    /// suitability scan runs across cores and a shared list would be a race.</summary>
    [ThreadStatic] private static List<FloraIndividual>? _neighbourScratch;
    private static List<FloraIndividual> _nb => _neighbourScratch ??= new List<FloraIndividual>();
    private readonly List<FloraIndividual> _woodyCanopy = new();
    private readonly ScalarField _canopyShade;
    private int _woodyCanopyVersion = -1;
    private int _canopyShadeVersion = -1;
    private bool _canopyShadeDirty = true;
    public const string PropagationStream = "flora.propagation";
    public const double TreeAreaPerIndividual = 15.0;

    public FloraSystem(VivariumWorld w)
    {
        _w = w;
        _canopyShade = new ScalarField("woody_canopy_shade", w.Grid, 0, 0, 1);
    }

    private ContentLibrary C => _w.Content;

    /// <summary>Plan-view area of the regular hexagonal island.</summary>
    public double IslandAreaM2 => 3 * Math.Sqrt(3) / 2 * _w.Domain.Radius * _w.Domain.Radius;

    /// <summary>Shared structural population budget for the whole island, not per species.</summary>
    public int WoodyPopulationCap(WoodyLayer layer)
    {
        if (layer == WoodyLayer.Shrub) return int.MaxValue;
        double areaPer = TreeAreaPerIndividual;
        return Math.Max(1, (int)Math.Floor(IslandAreaM2 / areaPer));
    }

    public int WoodyPopulation(WoodyLayer layer)
    {
        int count = 0;
        foreach (var f in _w.Flora.Items)
            if (C.FloraOrThrow(f.SpeciesId).Woody?.Layer == layer) count++;
        return count;
    }

    private readonly object _canopyLock = new();

    private void RefreshWoodyCanopy()
    {
        if (_woodyCanopyVersion == _w.Flora.Version) return;
        lock (_canopyLock)
        {
            if (_woodyCanopyVersion == _w.Flora.Version) return;
            int oldCount = _woodyCanopy.Count;
            _woodyCanopy.Clear();
            foreach (var f in _w.Flora.Items)
                if (C.FloraOrThrow(f.SpeciesId).Woody != null) _woodyCanopy.Add(f);
            if (oldCount != _woodyCanopy.Count)
                _canopyShadeDirty = true;
            _woodyCanopyVersion = _w.Flora.Version;
        }
    }

    /// <summary>
    /// Rebuilds the derived woody-canopy shade field. Cost is paid once per canopy state change and only touches
    /// grid cells under a bounded number of tree/shrub crowns; all ecology consumers then get O(1) bilinear samples.
    /// </summary>
    private void RefreshCanopyShade()
    {
        RefreshWoodyCanopy();
        if (!_canopyShadeDirty) return;
        lock (_canopyLock)
        {
            if (!_canopyShadeDirty) return;
            Array.Clear(_canopyShade.Values, 0, _canopyShade.Values.Length);
            foreach (var f in _woodyCanopy)
            {
                var sp = C.FloraOrThrow(f.SpeciesId);
                var woody = sp.Woody!;
                double maturity = Math.Sqrt(f.BiomassFraction(sp));
                double radius = woody.CanopyRadius * (0.3 + 0.7 * maturity);
                if (radius <= 1e-6) continue;
                foreach (int cell in _w.Grid.CellsInRadius(f.Position, radius))
                {
                    double d = Vec2.Distance(f.Position, _w.Grid.CellCenter(cell));
                    if (d >= radius) continue;
                    _canopyShade.Add(cell, woody.ShadeOpacity * maturity * (1 - d / radius));
                }
            }
            _canopyShadeVersion = _w.Flora.Version;
            _canopyShadeDirty = false;
        }
    }

    /// <summary>
    /// Brings the derived canopy caches up to date. A scan that runs across
    /// cores has to do this first: the shade field is rebuilt by clearing it
    /// and adding the crowns, so a rebuild in the middle of a scan would be
    /// read half written.
    /// </summary>
    public void PrepareCanopyCaches() => RefreshCanopyShade();

    public double EffectiveLightCell(int cell)
    {
        RefreshCanopyShade();
        double light = _w.Fields.Light.Values[cell];
        double shade = _canopyShade.Values[cell];
        return Math.Max(0, Math.Min(1, light - Math.Min(shade, light)));
    }

    /// <summary>
    /// Incident ecological light after the structural tree/shrub canopy. Low herb/fern shade is a coverage-layer
    /// microhabitat effect and remains local there; tree/shrub shade is shared by every ecology consumer.
    /// </summary>
    public double EffectiveLight(Vec2 p, EntityId self = default)
    {
        double light = _w.Fields.Light.Sample(p);

        // Only a woody plant needs self-exclusion. For those few bounded individuals, evaluate the other crowns
        // directly rather than subtracting an analytic value from the interpolated shade grid.
        if (!self.IsNone && _w.Flora.Get(self) is { } own && C.FloraOrThrow(own.SpeciesId).Woody != null)
        {
            RefreshWoodyCanopy();
            double directShade = 0;
            foreach (var f in _woodyCanopy)
            {
                if (f.Id == self) continue;
                var sp = C.FloraOrThrow(f.SpeciesId);
                var woody = sp.Woody!;
                double maturity = Math.Sqrt(f.BiomassFraction(sp));
                double radius = woody.CanopyRadius * (0.3 + 0.7 * maturity);
                double d = Vec2.Distance(f.Position, p);
                if (radius > 1e-6 && d < radius)
                    directShade += woody.ShadeOpacity * maturity * (1 - d / radius);
            }
            return MathD.Clamp01(light - Math.Min(directShade, light));
        }

        RefreshCanopyShade();
        double shade = _canopyShade.Sample(p);
        return MathD.Clamp01(light - Math.Min(shade, light));
    }

    public FloraSuitability Suitability(FloraSpeciesDef sp, Vec2 p, EntityId self = default, bool potential = false, bool established = false)
    {
        if (!_w.Domain.ContainsDisc(p, 0.02)) return FloraSuitability.Refused("outside the island", Substrate.Soil);
        if (_w.PilotTree?.BlocksTrunk(p, 0.02) == true) return FloraSuitability.Refused("inside Pilot Tree trunk", Substrate.Wood);
        if (sp.Woody != null && _w.PilotTree?.BlocksDisc(p, 0.08) == true) return FloraSuitability.Refused("Pilot Tree root volume reserved", Substrate.Wood);
        var sub = _w.SubstrateAt(p);
        if (sp.RefuseSubstrates.Contains(sub)) return FloraSuitability.Refused($"{sp.Name} refuses {SubstrateIds.Id(sub)} substrate", sub);
        foreach (var tag in _w.Props.HabitatTagsAt(p))
            if (sp.RefuseTags.Contains(tag)) return FloraSuitability.Refused($"{sp.Name} refuses '{tag}' habitat", sub);
        double depth = _w.Water.OpenWaterDepth(p);
        bool onProp = sub is Substrate.Rock or Substrate.Wood && (!double.IsNaN(_w.Props.PropTopAt(p)) || _w.PilotTree?.BlocksDisc(p) == true);
        if (!sp.UnlimitedWaterDepth && !onProp && depth > sp.MaxWaterDepth + 1e-9) return FloraSuitability.Refused($"submerged ({depth * 100:0.#} cm, tolerates {sp.MaxWaterDepth * 100:0.#} cm)", sub);
        if (sp.MinWaterDepth > 0 && depth < sp.MinWaterDepth - 1e-9) return FloraSuitability.Refused($"needs standing water ({depth * 100:0.#} cm < {sp.MinWaterDepth * 100:0.#} cm)", sub);
        double moisture = _w.Fields.Moisture.Sample(p);
        if (!established && moisture < sp.HardMinMoisture) return FloraSuitability.Refused($"too dry (moisture {moisture:0.00} < {sp.HardMinMoisture:0.00})", sub);
        if (!established && !onProp && moisture > sp.HardMaxMoisture + 1e-9) return FloraSuitability.Refused($"too wet (moisture {moisture:0.00} > {sp.HardMaxMoisture:0.00})", sub);
        if (sp.RequiresFeature.Length > 0 && DistanceToFeature(p, sp.RequiresFeature, sp.RequiresFeatureRadius) > sp.RequiresFeatureRadius)
            return FloraSuitability.Refused($"{sp.Name} needs a {sp.RequiresFeature} within {sp.RequiresFeatureRadius * 100:0} cm to climb", sub);

        // Direct neighbour exclusions are occupancy/competition constraints, not
        // the physical potential niche. Keep them for establishment checks only.
        if (!potential && !established)
        {
            foreach (var rel in C.FloraInteractions.Relations)
            {
                if (rel.Type != FloraRelationType.Refuse || rel.A != sp.Id) continue;
                _w.Flora.Neighbours(p, rel.Radius, _nb);
                foreach (var n in _nb)
                    if (n.Id != self && n.SpeciesId == rel.B) return FloraSuitability.Refused($"excluded by nearby {C.FloraOrThrow(rel.B).Name}: {rel.Reason}", sub);
            }
        }

        double light = EffectiveLight(p, self);
        // decomposers judge their food (dead matter) where plants judge soil nutrients
        double nutrients = sp.Decomposer ? MathD.Clamp01(SampleDetritusPlusLitter(p) / C.Ecology.DetritusMax) : _w.Fields.Nutrients.Sample(p) / C.Ecology.NutrientMax;
        double fs = sp.SubstrateAffinity.GetValueOrDefault(sub);
        bool soilRooted = sub is Substrate.Soil or Substrate.Gravel or Substrate.Water;
        double oxygen = 1, topsoil = 0, oxygenFactor = 1, depthFactor = 1;
        if (soilRooted && (sp.Oxygen.HasValue || sp.TopsoilDepth.HasValue))
        {
            _w.RootZone.EnsureCurrent();
            oxygen = _w.RootZone.OxygenAt(p, moisture);
            topsoil = _w.RootZone.RetainedTopsoilDepth.Sample(p);
            if (sp.Oxygen is { } o) oxygenFactor = o.Eval(oxygen);
            if (sp.TopsoilDepth is { } d) depthFactor = d.Eval(topsoil);
        }
        double fm = sp.Moisture.Eval(moisture), fl = sp.Light.Eval(light), fn = sp.Nutrients.Eval(nutrients);
        double bonus = ProximityBonus(sp, p, self, physicalOnly: potential);
        int dimensions = 3 + (soilRooted && sp.Oxygen.HasValue ? 1 : 0) +
                         (soilRooted && sp.TopsoilDepth.HasValue ? 1 : 0);
        double score = MathD.Clamp01(fs * Math.Pow(fm * fl * fn * oxygenFactor * depthFactor, 1.0 / dimensions) * (1 + bonus));

        // Life stages share physical conditions, not a common suitability response.
        // Zero-resource environments remain poor even for adults; broader tolerances
        // only reduce sensitivity to distance from environmental optima.
        double StageScore(double toleranceScale)
        {
            double factors = sp.Moisture.EvalStage(moisture, toleranceScale) *
                             sp.Light.EvalStage(light, toleranceScale) *
                             sp.Nutrients.EvalStage(nutrients, toleranceScale);
            if (soilRooted && sp.Oxygen is { } ox) factors *= ox.EvalStage(oxygen, toleranceScale);
            if (soilRooted && sp.TopsoilDepth is { } depthPref) factors *= depthPref.EvalStage(topsoil, toleranceScale);
            return MathD.Clamp01(fs * Math.Pow(factors, 1.0 / dimensions) * (1 + bonus));
        }
        double survival = StageScore(sp.SurvivalToleranceScale);
        double establishment = StageScore(sp.EstablishmentToleranceScale);
        double reproduction = StageScore(sp.ReproductionToleranceScale);
        return new FloraSuitability
        {
            Score = score, GrowthScore = score, EstablishmentScore = establishment,
            SurvivalScore = survival, ReproductionScore = reproduction,
            Substrate = sub, SubstrateFactor = fs, MoistureFactor = fm, LightFactor = fl,
            NutrientFactor = fn, OxygenFactor = oxygenFactor, TopsoilFactor = depthFactor,
            Oxygen = oxygen, TopsoilDepth = topsoil,
            ProximityBonus = bonus, Moisture = moisture, Light = light, Nutrients = nutrients,
        };
    }

    /// <summary>
    /// Physical potential niche under current environmental conditions, without
    /// direct neighbor exclusion or species-proximity bonuses. Biotic shade and
    /// resource modification still count because they are part of the environment.
    /// </summary>
    public FloraSuitability PotentialSuitability(FloraSpeciesDef species, Vec2 position)
        => Suitability(species, position, potential: true);

    /// <summary>Sum of configured beneficial-proximity bonuses active at p (capped at 0.5).</summary>
    public double ProximityBonus(FloraSpeciesDef sp, Vec2 p, EntityId self = default, bool physicalOnly = false)
    {
        double bonus = 0;
        foreach (var rule in sp.Proximity)
        {
            if (rule.IsFeature)
            {
                double d = rule.Feature == "water" ? DistanceToWater(p, rule.Radius) : _w.Props.DistanceToFeature(p, rule.Feature);
                if (rule.Feature is "log" or "wood" && _w.PilotTree is { } pilot)
                    d = Math.Min(d, pilot.BlocksDisc(p, rule.Radius) ? 0 : double.PositiveInfinity);
                if (d <= rule.Radius) bonus += rule.Bonus;
            }
            else if (!physicalOnly && AnyFloraNear(p, rule.Target, rule.Radius, self)) bonus += rule.Bonus;
        }
        foreach (var rel in C.FloraInteractions.Relations)
            if (rel.Type == FloraRelationType.Benefit && rel.A == sp.Id && AnyFloraNear(p, rel.B, rel.Radius, self)) bonus += rel.Bonus;
        return Math.Min(bonus, 0.5);
    }

    private double DistanceToFeature(Vec2 p, string feature, double radius)
    {
        double d = _w.Props.DistanceToFeature(p, feature);
        if (feature is "log" or "wood" && _w.PilotTree?.BlocksDisc(p, radius) == true) return 0;
        return d;
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

    /// <summary>
    /// Neighbor pressure from overlapping physical growing space and root
    /// interception distance. Species IDs carry no competitive rank: the
    /// neighbor's actual radius, biomass, distance and local space determine
    /// the effect. Shared nutrients and light are competed for elsewhere.
    /// </summary>
    public double Crowding(FloraSpeciesDef sp, Vec2 p, EntityId self)
    {
        _w.Flora.Neighbours(p, sp.CompetitionRadius, _nb);
        var established = self.IsNone ? null : _w.Flora.Get(self);
        double contenderRadius = established?.Radius(sp) ??
            (sp.MinRadius + (sp.RadiusAtMax - sp.MinRadius) *
             Math.Sqrt(MathD.Clamp01(sp.InitialBiomass / Math.Max(sp.MaxBiomass, 1e-9))));
        double crowd = 0;
        foreach (var n in _nb)
        {
            if (n.Id == self) continue;
            var nsp = C.FloraOrThrow(n.SpeciesId);
            crowd += PhysicalSpacePressure(contenderRadius, n.Radius(nsp),
                Vec2.Distance(p, n.Position), sp.CompetitionRadius, n.BiomassFraction(nsp));
        }
        return crowd;
    }

    /// <summary>
    /// Cheap disc-footprint surrogate for shared rooting and surface space.
    /// Resource catchments can compete without crown contact; direct overlap
    /// carries stronger spatial exclusion. Geometry replaces pairwise species
    /// competitive-strength weights.
    /// </summary>
    public static double PhysicalSpacePressure(double contenderRadius, double neighbourRadius,
        double distance, double rootCompetitionRadius, double neighbourBiomassFraction)
    {
        double radii = Math.Max(1e-9, contenderRadius + neighbourRadius);
        double contact = MathD.Clamp01(1 - distance / radii);
        double rootContact = MathD.Clamp01(1 - distance / Math.Max(rootCompetitionRadius, 1e-9));
        double areaA = contenderRadius * contenderRadius, areaB = neighbourRadius * neighbourRadius;
        double neighbourShare = areaB / Math.Max(1e-12, areaA + areaB);
        return MathD.Clamp01(neighbourBiomassFraction) *
            (0.65 * contact + 0.35 * rootContact) * 2 * neighbourShare;
    }

    /// <summary>Per-colony cell cap (bounds instance count; roots beyond this stop budding). Kept low because
    /// interior merging (see <see cref="ColonyStep"/>) keeps a mature mat's cost near its rim size rather than
    /// growing without bound, so this only needs to bound the rim-growth phase before merging catches up.</summary>
    public const int ColonyCellCap = 90;
    /// <summary>Global cap on colonial-cell entities across every colony of every species. Once hit, no colony
    /// buds further (existing cells still thicken/merge) until deaths free room; prevents many small colonies
    /// from collectively overrunning the entity budget even though each respects <see cref="ColonyCellCap"/>.</summary>
    public const int GlobalColonialCellBudget = 2200;
    /// <summary>Interior cells with at least this many same-species neighbours within cellRadius·2 are fully
    /// surrounded (no exposed edge) and, once fully thickened, are folded into a neighbour instead of persisting
    /// as their own entity — the mat keeps its area and lumpy look with far fewer instances.</summary>
    private const int MergeNeighbourThreshold = 7;
    private const double MergeFactorCap = 6.0;
    private readonly List<(FloraSpeciesDef Sp, Vec2 P, EntityId Root, double RingDist)> _colonyBuds = new();
    private readonly Dictionary<EntityId, int> _colonySize = new();
    private readonly Dictionary<string, int> _climberNodeCounts = new(StringComparer.Ordinal);
    private readonly List<FloraIndividual> _nbColony = new();
    private int _colonialTotal;

    public void Step(double dt) { foreach (var _ in StepBatches(dt)) { } }

    /// <summary>Same ordered ecology update, with render opportunities between small batches.</summary>
    public IEnumerable<bool> StepBatches(double dt)
    {
        var births = new List<(FloraSpeciesDef Sp, Vec2 P)>();
        _buds.Clear();
        _climberBuds.Clear();
        _colonyBuds.Clear();
        _colonySize.Clear();
        _climberNodeCounts.Clear();
        var deaths = new List<(FloraIndividual F, string Cause)>();
        var eco = C.Ecology;
        _colonialTotal = 0;
        foreach (var f in _w.Flora.Items)
        {
            yield return true;
            var sp0 = C.FloraOrThrow(f.SpeciesId);
            if (sp0.Climber != null)
                _climberNodeCounts[f.SpeciesId] = _climberNodeCounts.GetValueOrDefault(f.SpeciesId) + (f.ClimberSegments?.Count ?? 0);
            if (sp0.Colony == null) continue;
            var root0 = f.ColonyRoot.IsNone ? f.Id : f.ColonyRoot;
            _colonySize[root0] = _colonySize.GetValueOrDefault(root0) + 1;
            _colonialTotal++;
        }
        foreach (var f in _w.Flora.Items)
        {
            yield return true;
            var sp = C.FloraOrThrow(f.SpeciesId);
            var p = f.Position;
            f.Age += dt;
            var suit = Suitability(sp, p, f.Id, established: true);
            f.LastSuitability = suit.Score;
            f.LastReproductionSuitability = suit.ReproductionScore;
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
                        double got = _w.Litter.TakeForDecomposer(cell, need, sp.Decomposition);
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
                double severity = suit.HardRefused ? 1 : MathD.Clamp01(1 - suit.SurvivalScore / Math.Max(sp.MinSuitability, 1e-6));
                f.Biomass *= Math.Exp(-sp.DeclineRate * severity * dt);
                f.Health = Math.Max(0, f.Health - severity * dt / SimUnits.Day * (suit.HardRefused ? 1.0 : 0.35));
            }
            // Shedding enters the authoritative detritus pool immediately; rendering shows this same mass.
            if (sp.SheddingRate > 0)
            {
                double shed = f.Biomass * (1 - Math.Exp(-sp.SheddingRate * dt));
                f.Biomass -= shed;
                double footprint = sp.Woody == null ? f.Radius(sp)
                    : sp.Woody.CanopyRadius * Math.Sqrt(f.BiomassFraction(sp));
                double litter = shed * sp.LitterFraction;
                double coarse = sp.Woody == null ? 0 : litter * 0.12;
                _w.Litter.Deposit(p, litter - coarse, coarse, footprint);
            }
            // senescence
            if (f.Age > sp.Lifespan * f.LifespanFactor) f.Health = Math.Max(0, f.Health - dt / SimUnits.Day);

            if (f.Health <= 0 || f.Biomass < sp.InitialBiomass * 0.25)
            {
                deaths.Add((f, f.Age > sp.Lifespan * f.LifespanFactor ? "senescence" : suit.HardRefused ? "habitat" : "decline"));
                continue;
            }

            if (sp.CreepSpeed > 0 && Creep(f, sp, dt, births)) continue;
            if (sp.Climber is { } climber && ClimberStep(f, sp, climber, dt)) continue;

            if (sp.Colony is { } cd)
            {
                if (ColonyStep(f, sp, cd, p, dt, deaths)) continue;   // merged into a neighbour this step
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
            // A climber only seeds once it has found something to climb: a vine that is still searching is not
            // established, and a species made of searching runners is a lawn, not a population.
            bool seeds = sp.Climber == null || f.ClimberAttached;
            if (seeds && sp.Reproduction == null && f.Stage(sp) != FloraStage.Juvenile && f.BiomassFraction(sp) >= sp.SpreadMinBiomassFraction
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

        foreach (var (f, cause) in deaths) { yield return true; Kill(f, cause); }
        foreach (var (sp, q) in births)
        {
            yield return true;
            if (!CanEstablish(sp, q, out _)) continue; // re-check against same-step recruits
            var nf = Establish(sp, q, "propagation");
            if (sp.Colony != null) FoundColony(nf, sp);
        }
        SporeRain(dt);
        foreach (var (sp, q, parent, biomass) in _buds)
        {
            yield return true;
            if (!CanEstablish(sp, q, out _)) { if (_w.Flora.Get(parent) is { } back) back.Biomass += biomass; continue; }
            var nf = Establish(sp, q, "growth front", biomass);
            nf.ParentId = parent;
            int gen = (_w.Flora.Get(parent)?.Generation ?? -1) + 1;
            AssignSlimeTint(nf, gen);
        }
        foreach (var (sp, q, parent, biomass, unsupportedLength) in _climberBuds)
        {
            yield return true;
            if (!CanEstablish(sp, q, out _))
            {
                if (_w.Flora.Get(parent) is { } back) back.Biomass += biomass;
                continue;
            }
            var nf = Establish(sp, q, "climber runner", biomass);
            nf.ParentId = parent;
            nf.Generation = (_w.Flora.Get(parent)?.Generation ?? -1) + 1;
            nf.UnsupportedLength = unsupportedLength;
            nf.ClimberTip = true;
            // The bud takes hold of a host only when it actually reaches one, which the growth law decides.
        }
        foreach (var (sp, q, root, ringDist) in _colonyBuds)
        {
            yield return true;
            if (_colonialTotal >= GlobalColonialCellBudget) break;
            if (_colonySize.GetValueOrDefault(root) >= ColonyCellCap) continue;
            if (!CanEstablish(sp, q, out _)) continue;
            var nf = Establish(sp, q, "colony growth");
            nf.ColonyRoot = root; nf.RingDist = ringDist; nf.HeightFactor = 0.15;
            AssignColonyTint(nf, sp);
            _colonySize[root] = _colonySize.GetValueOrDefault(root) + 1;
            _colonialTotal++;
        }
        // Tree/shrub biomass changed during this step; rebuild derived shade lazily on the next light sample.
        // Empty fixture worlds keep their all-zero canopy cache indefinitely instead of clearing the whole grid.
        if (_woodyCanopy.Count > 0) _canopyShadeDirty = true;
    }

    /// <summary>
    /// Slime-mold behaviour: the plasmodium creeps toward richer dead matter; after starving long enough it stops,
    /// releases spores onto the best nearby food and dies back. Returns true when normal propagation should be
    /// skipped this step (creepers only reproduce by fruiting).
    /// </summary>
    private readonly List<(FloraSpeciesDef Sp, Vec2 P, EntityId Parent, double Biomass)> _buds = new();
    private readonly List<(FloraSpeciesDef Sp, Vec2 P, EntityId Parent, double Biomass, double UnsupportedLength)> _climberBuds = new();

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
            if (sp.Archetype != "fungus") continue;
            // Some highly episodic or substrate-specific fungi are introduced as starters and then spread
            // locally. Excluding them from generic atmospheric spore rain prevents total spore pressure from
            // scaling linearly with every fungus definition added to the library.
            if (sp.Tags.Contains("no_ambient_spore_rain")) continue;
            if (_sporeCounts.GetValueOrDefault(sp.Id) >= SporeBankThreshold) continue;
            var rng = Rng.Keyed(_w.Seed, "flora.spores." + sp.Id, (ulong)_w.Clock.Tick * 1_000_003UL + _w.Ids.LastSerial);
            if (rng.NextDouble() >= Math.Min(1, dt / SimUnits.Day)) continue;
            Vec2? best = null; double bestFood = double.NegativeInfinity;
            var cells = _w.Grid.DomainCells;
            for (int k = 0; k < 12; k++)
            {
                var q = _w.Grid.CellCenter(cells[rng.NextInt(cells.Length)]);
                double jitter = _w.Grid.CellSize * 0.42;
                q += new Vec2(rng.Range(-jitter, jitter), rng.Range(-jitter, jitter));
                if (!_w.Domain.ContainsDisc(q, 0.02)) continue;
                double food = SampleDetritusPlusLitter(q);
                if (food > bestFood && CanEstablish(sp, q, out _)) { best = q; bestFood = food; }
            }
            if (best.HasValue)
            {
                var nf = Establish(sp, best.Value, "spores");
            }
        }
    }

    /// <summary>Distance between a patch of plasmodium and the growth-front patch it buds (m).</summary>
    public const double BudStep = 0.15;

    /// <summary>Total visible detrital matter available to decomposers at <paramref name="p"/>.</summary>
    private double SampleDetritusPlusLitter(Vec2 p) => _w.Litter.DetritusAt(p);

    private bool Creep(FloraIndividual f, FloraSpeciesDef sp, double dt, List<(FloraSpeciesDef Sp, Vec2 P)> births)
    {
        var p = f.Position;
        double food = SampleDetritusPlusLitter(p);
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
                if (SampleDetritusPlusLitter(q) >= sp.FoodThreshold && CanEstablish(sp, q, out _)) births.Add((sp, q));
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
        // Sense a stable but individually rotated 12-direction fan. The old fixed eight compass rays made
        // mature networks betray the algorithm as 45-degree/octagonal growth.
        var senseRng = Rng.Keyed(_w.Seed, "flora.slime.sense", f.Id.Value);
        double phase = senseRng.Range(0, 2 * Math.PI / 12);
        for (int k = 0; k < 12; k++)
        {
            double ang = phase + k * 2 * Math.PI / 12;
            var dir = Vec2.FromAngle(ang);
            double score = 0;
            for (double r = 0.25; r <= Sense + 1e-9; r += 0.25)
                score += SampleDetritusPlusLitter(p + dir * r) * Weight(r);
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

    private readonly List<FloraIndividual> _climberSupportNeighbours = new();
    /// <summary>Scratch buffer marking the nodes that have already extended, so the rest are shoot tips.</summary>
    private bool[] _tipFlags = new bool[256];

    public readonly record struct HostMaterialHit(
        bool HasMaterial,
        Vec3 SurfacePoint,
        Vec3 SurfaceNormal,
        double TopY,
        EntityId HostId,
        string HostType
    );

    /// <summary>
    /// Computes the exact 3D trunk radius of a woody tree at a given absolute world height y,
    /// accounting for tree render radius, species-specific trunk proportions, basal buttress flare, and taper.
    /// Also returns the trunk base (ground) and trunk top height.
    /// </summary>
    public (double Radius, double Ground, double Top) TreeTrunkProfile(FloraIndividual tree, FloraSpeciesDef sp, double y)
    {
        double ground = double.IsNaN(tree.GroundHeight) ? (tree.GroundHeight = _w.GroundHeight(tree.Position)) : tree.GroundHeight;
        double rTree = tree.Radius(sp);
        double hTree = sp.Height * (0.45 + 0.55 * Math.Sqrt(tree.BiomassFraction(sp)));

        const double forkRelH = 0.28;
        const double baseBoleRatio = 0.085;

        double forkH = forkRelH * hTree;
        double trunkTop = ground + Math.Max(forkH, hTree * 0.70);
        double relY = Math.Max(0, y - ground);

        double baseBole = baseBoleRatio * rTree;
        double t = Math.Clamp(relY / Math.Max(0.01, forkH), 0, 1.5);

        // Buttress flare near ground (t < 0.3)
        double flare = 1.0 + 1.25 * Math.Pow(Math.Max(0, 1.0 - t / 0.30), 2.0);

        // Taper with height
        double taper = t <= 1.0
            ? 0.45 + 0.55 * Math.Pow(1.0 - 0.38 * t, 0.8)
            : Math.Max(0.30, 0.45 * (1.0 - (t - 1.0) * 0.4));

        double radius = Math.Max(0.03, baseBole * taper * flare);
        return (radius, ground, trunkTop);
    }

    /// <summary>
    /// Actually queries the simulated 3D world to check if host support material (woody trunk, log, rock)
    /// exists at the candidate position. Returns HasMaterial = false when hitting empty space (past trunk top, log ends, or rock summit).
    /// </summary>
    public HostMaterialHit CheckHostMaterial(Vec3 pos, double searchRadius, HashSet<string>? supports = null)
    {
        HostMaterialHit best = default;
        double bestDist = searchRadius;

        // 1. Woody tree trunks and stems
        if (supports == null || supports.Contains("woody"))
        {
            _w.Flora.Neighbours(pos.XZ, searchRadius + 1.5, _climberSupportNeighbours);
            foreach (var n in _climberSupportNeighbours)
            {
                var nsp = C.FloraOrThrow(n.SpeciesId);
                if (nsp.Woody == null) continue;

                var (boleRadius, ground, trunkTop) = TreeTrunkProfile(n, nsp, pos.Y);

                // Host material exists strictly from ground up to trunkTop.
                // If pos.Y > trunkTop, host material has ENDED: it is empty space above the trunk!
                if (pos.Y < ground - 0.05 || pos.Y > trunkTop) continue;

                var toPos = pos.XZ - n.Position;
                double dCenter = toPos.Length;
                double dSurface = Math.Abs(dCenter - boleRadius);

                if (dSurface < bestDist)
                {
                    bestDist = dSurface;
                    var outward = dCenter > 1e-6 ? toPos.Normalized() : new Vec2(1, 0);
                    // Proud offset (5mm) so vine sits ON the bark
                    var surfXZ = n.Position + outward * (boleRadius + 0.005);
                    best = new HostMaterialHit(
                        true,
                        new Vec3(surfXZ.X, pos.Y, surfXZ.Z),
                        new Vec3(outward.X, 0, outward.Z),
                        trunkTop,
                        n.Id,
                        "woody"
                    );
                }
            }
        }

        // 2. Logs
        if (supports == null || supports.Contains("log"))
        {
            if (_w.PilotTree is { } pilot && pilot.TryLogSurface(pos, searchRadius, out var surface, out var normal, out double top))
            {
                bestDist = (surface - pos).Length;
                best = new HostMaterialHit(true, surface, normal, top, EntityId.None, "log");
            }
            foreach (var l in _w.Props.Logs)
            {
                var axis = Vec2.FromAngle(l.RotationY);
                var rel = pos.XZ - l.Position;
                double along = rel.Dot(axis);
                // Check if candidate is along the log length (empty space past ends)
                if (Math.Abs(along) > l.Length / 2.0) continue;

                var onAxis = l.Position + axis * along;
                var outV = pos.XZ - onAxis;
                double dCenter = Math.Sqrt(outV.LengthSq + (pos.Y - l.Y) * (pos.Y - l.Y));
                double dSurface = Math.Abs(dCenter - l.Radius);

                if (dSurface < bestDist)
                {
                    bestDist = dSurface;
                    var outNorm = outV.LengthSq > 1e-6 ? outV.Normalized() : Vec2.FromAngle(l.RotationY + Math.PI / 2);
                    var surfXZ = onAxis + outNorm * (l.Radius + 0.005);
                    double surfY = Math.Clamp(pos.Y, l.Y - l.Radius, l.Y + l.Radius);
                    best = new HostMaterialHit(
                        true,
                        new Vec3(surfXZ.X, surfY, surfXZ.Z),
                        new Vec3(outNorm.X, (surfY - l.Y) / Math.Max(l.Radius, 1e-4), outNorm.Z).Normalized(),
                        l.Y + l.Radius,
                        EntityId.None,
                        "log"
                    );
                }
            }
        }

        // 3. Rocks
        if (supports == null || supports.Contains("rock"))
        {
            foreach (var r in _w.Props.Rocks)
            {
                double dXZ = (pos.XZ - r.Position).Length;
                if (dXZ > r.FootprintRadius * 1.1) continue;
                if (pos.Y > r.Y + r.SizeY) continue; // Empty space above rock summit

                double dSurface = Math.Max(0, dXZ - r.FootprintRadius * 0.8);
                if (dSurface < bestDist)
                {
                    bestDist = dSurface;
                    var to = pos.XZ - r.Position;
                    var normXZ = to.LengthSq > 1e-6 ? to.Normalized() : new Vec2(1, 0);
                    best = new HostMaterialHit(
                        true,
                        new Vec3(pos.X, Math.Clamp(pos.Y, r.Y, r.Y + r.SizeY), pos.Z),
                        new Vec3(normXZ.X, 0.4, normXZ.Z).Normalized(),
                        r.Y + r.SizeY,
                        EntityId.None,
                        "rock"
                    );
                }
            }
        }

        return best;
    }

    private (Vec2 Target, double Distance)? NearestClimberSupport(FloraIndividual f, ClimberDef cd)
    {
        var p = f.Position;
        Vec2 bestTarget = default;
        double bestDistance = cd.SearchRadius + 1;

        if (cd.SupportTypes.Contains("woody"))
        {
            _w.Flora.Neighbours(p, cd.SearchRadius, _climberSupportNeighbours);
            foreach (var n in _climberSupportNeighbours)
            {
                if (n.Id == f.Id) continue;
                var nsp = C.FloraOrThrow(n.SpeciesId);
                if (nsp.Woody == null) continue;
                var toTree = p - n.Position;
                var dir = toTree.LengthSq > 1e-6 ? toTree.Normalized() : new Vec2(1, 0);
                var (boleR, _, _) = TreeTrunkProfile(n, nsp, _w.GroundHeight(n.Position));
                var targetBark = n.Position + dir * (boleR + 0.005);
                double d = Vec2.Distance(p, targetBark);
                if (d < bestDistance) { bestDistance = d; bestTarget = targetBark; }
            }
        }

        if (cd.SupportTypes.Contains("log"))
        {
            if (_w.PilotTree is { } pilot && pilot.TryLogSurface(Vec3.FromXZ(p,_w.GroundHeight(p)+.02),
                cd.SearchRadius,out var surface,out _,out _))
            {
                double d=Vec2.Distance(p,surface.XZ);
                if(d<bestDistance){bestDistance=d;bestTarget=surface.XZ;}
            }
            foreach (var l in _w.Props.Logs)
            {
                var axis = Vec2.FromAngle(l.RotationY);
                var rel = p - l.Position;
                double along = Math.Clamp(rel.Dot(axis), -l.Length / 2, l.Length / 2);
                var onAxis = l.Position + axis * along;
                var outV = p - onAxis;
                double edgeDistance = Math.Max(0, outV.Length - l.Radius);
                if (edgeDistance >= bestDistance || edgeDistance > cd.SearchRadius) continue;
                var outward = outV.LengthSq > 1e-10 ? outV / outV.Length : Vec2.FromAngle(l.RotationY + Math.PI / 2);
                bestDistance = edgeDistance;
                bestTarget = onAxis + outward * l.Radius;
            }
        }

        if (cd.SupportTypes.Contains("rock"))
        {
            foreach (var r in _w.Props.Rocks)
            {
                var to = r.Position - p;
                double centre = to.Length;
                double edgeDistance = Math.Max(0, centre - r.FootprintRadius * 0.8);
                if (edgeDistance >= bestDistance || edgeDistance > cd.SearchRadius) continue;
                bestDistance = edgeDistance;
                bestTarget = centre > 1e-10 ? p + to / centre * edgeDistance : p;
            }
        }
        return bestDistance <= cd.SearchRadius ? (bestTarget, bestDistance) : null;
    }

/// <summary>
    /// Surface-bound vine growth. Every shoot grows from its tip: it lays a new stem node down on the surface it
    /// is touching and lets the host's geometry decide where the stem ends up. The species supplies only weights
    /// (climb, lateral drift, light, circumnutation); the surface supplies the frame. See
    /// <see cref="ClimberGrowth"/> for why one law can both wind around a round trunk and run flat on the ground.
    /// </summary>
    private bool ClimberStep(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd, double dt)
    {
        if (f.ClimberSegments == null) FoundClimber(f, sp, cd);
        var segs = f.ClimberSegments!;

        // Senescence: the distal nodes wither and the plant stops extending.
        if (f.Stage(sp) == FloraStage.Senescent)
        {
            double senescentFrac = Math.Clamp((f.Age - sp.Lifespan * f.LifespanFactor * 0.85) / Math.Max(0.01, sp.Lifespan * f.LifespanFactor * 0.15), 0, 1);
            int countToDie = (int)(segs.Count * senescentFrac);
            for (int i = segs.Count - 1; i >= Math.Max(1, segs.Count - countToDie); i--)
                segs[i].Senescent = true;
            return true;
        }

        foreach (var s in segs) s.Age += dt;

        GrowClimber(f, sp, cd, dt);

        bool anyTip = false, anyAttached = false;
        double unsupported = 0;
        foreach (var s in segs)
        {
            if (s.Senescent) continue;
            if (!s.Terminal) anyTip = true;
            if (s.Attached) anyAttached = true;
            if (s.UnsupportedLength > unsupported) unsupported = s.UnsupportedLength;
        }
        f.ClimberTip = anyTip;
        f.ClimberAttached = anyAttached;
        f.UnsupportedLength = unsupported;
        // Returning false lets the ordinary propagation rule run, so a mature vine seeds new runners.
        return false;
    }

    /// <summary>Plants the founder node and gives the shoot its opening course.</summary>
    private void FoundClimber(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd)
    {
        f.ClimberSegments = new List<ClimberSegment>();
        double gy = double.IsNaN(f.GroundHeight) ? (f.GroundHeight = _w.GroundHeight(f.Position)) : f.GroundHeight;
        var origin = new Vec3(f.X, gy, f.Z);

        // A plant that budded off a runner continues the runner's line, so the network reads as one creeping
        // stem rather than a scatter of unrelated shoots.
        Vec3 forward = Vec3.Right;
        Vec3 lateral = Vec3.Right;
        Vec3 course = Vec3.Right;
        bool inherited = false;
        if (!f.ParentId.IsNone && _w.Flora.Get(f.ParentId) is { } parent && parent.ClimberSegments is { Count: > 0 } psegs)
        {
            int best = 0; double bestD = double.MaxValue;
            for (int i = 0; i < psegs.Count; i++)
            {
                double d = (psegs[i].Position - origin).LengthSq;
                if (d < bestD) { bestD = d; best = i; }
            }
            var tip = psegs[best];
            forward = tip.Forward.LengthSq > 1e-12 ? tip.Forward : Vec3.Right;
            lateral = tip.Lateral.LengthSq > 1e-12 ? tip.Lateral : forward;
            course = tip.ClimbDir.LengthSq > 1e-12 ? tip.ClimbDir : forward;
            inherited = true;
        }

        if (!inherited)
        {
            // The founder sets off on a course: toward the nearest host when one is within reach, otherwise on a
            // heading of its own. Sensing biases the heading; it never moves the shoot.
            var rng = Rng.Keyed(_w.Seed, "flora.climber.found", f.Id.Value, 0);
            if (TryFindHost(origin, cd.SearchRadius, cd, out _, out var hostSurface))
            {
                var to = hostSurface.Point - origin;
                course = to.LengthSq > 1e-9 ? to.Normalized() : Vec3.Right;
            }
            else
            {
                double ang = rng.Range(0, Math.PI * 2);
                course = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            }
            // A runner runs across the ground: keep its opening course level.
            course = new Vec3(course.X, 0, course.Z);
            course = course.LengthSq > 1e-9 ? course.Normalized() : Vec3.Right;
            forward = course;
            lateral = course;
        }

        f.ClimberSegments.Add(new ClimberSegment
        {
            Position = origin,
            Normal = GroundNormalAt(origin.XZ),
            Forward = forward,
            ParentIndex = -1,
            ShootOrder = 0,
            Attached = false,
            Terminal = false,
            Age = 0,
            Senescent = false,
            Lateral = lateral,
            ClimbDir = course,
            Support = ClimberSurfaceKind.Ground,
            HostId = EntityId.None,
            HostIndex = -1,
            Arc = 0,
            Depth = 0,
            Credit = 0,
            Radius = ClimberRadius(f, sp, cd, 0),
            UnsupportedLength = 0,
        });
        f.ClimberTip = true;
    }

    /// <summary>
    /// Runs every growing tip forward. Each tip carries its own credit, so the leader advances steadily while
    /// lateral shoots fill in behind it at their own rate: that is apical dominance, expressed as a speed rather
    /// than as a rule.
    /// </summary>
    private void GrowClimber(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd, double dt)
    {
        var segs = f.ClimberSegments!;
        if (segs.Count >= cd.NodeCap) return;


        // Tips are the nodes with no children. A child is always appended after its parent, so marking the
        // parents in one pass identifies every node that has already extended. The node budget bounds the
        // segment count, so the buffer can be sized once from it.
        int need = cd.NodeCap + 2;
        if (_tipFlags.Length < need) Array.Resize(ref _tipFlags, need);
        Array.Clear(_tipFlags, 0, need);
        for (int i = 0; i < segs.Count; i++)
        {
            int p = segs[i].ParentIndex;
            if (p >= 0 && p < need) _tipFlags[p] = true;
        }

        double vigor = 0.35 + 0.65 * f.BiomassFraction(sp);
        // The species as a whole has a stem budget, not just each plant: without it a runner that keeps seeding
        // new plants would fill the island with stem and eat the simulation's time.
        int speciesBudget = cd.NodeCap * 4;
        int startCount = segs.Count;
        int budgetUsed = _climberNodeCounts.GetValueOrDefault(sp.Id);
        if (budgetUsed >= speciesBudget) return;
        for (int i = 0; i < segs.Count; i++)
        {
            if (_tipFlags[i]) continue;
            var tip = segs[i];
            if (tip.Terminal || tip.Senescent) continue;

            bool lateral = tip.ShootOrder > 0;
            double rate = cd.GroundSpeed * vigor
                        * (lateral ? cd.LateralSpeed : 1.0)
                        * (tip.Attached ? cd.VerticalGrowthMultiplier : 1.0);
            tip.Credit += rate * dt;

            int guard = 0;
            while (tip.Credit >= cd.SegmentLength && segs.Count < cd.NodeCap && guard++ < 8)
            {
                tip.Credit -= cd.SegmentLength;
                if (!ExtendClimber(f, sp, cd, i)) break;
                _tipFlags[i] = true;              // the tip has a child now
                if (i >= segs.Count) break;
                tip = segs[i];
                if (tip.Terminal) break;
            }
        }
        _climberNodeCounts[sp.Id] = budgetUsed + (segs.Count - startCount);

        // Secondary growth: the stem thickens as the plant ages, so every node's radius is refreshed, not just
        // the ones laid down now. The base of an old vine is its thickest part, and the cap is only reached if
        // the older wood is allowed to thicken too.
        if (segs.Count != startCount)
        {
            double basal = ClimberGrowth.BasalRadius(f, sp, cd);
            for (int i = 0; i < segs.Count; i++)
                segs[i].Radius = ClimberGrowth.StemRadius(basal, cd, segs[i].Depth);
        }
    }

    /// <summary>
    /// Lays down one internode for the shoot whose tip sits at <paramref name="tipIndex"/>. Returns false when the
    /// shoot cannot grow any further.
    /// </summary>
    private bool ExtendClimber(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd, int tipIndex)
    {
        var segs = f.ClimberSegments!;
        var tip = segs[tipIndex];
        var rng = Rng.Keyed(_w.Seed, "flora.climber.extend", f.Id.Value, (ulong)tipIndex);

        // 1. The surface this shoot is bound to, and the host we could reach.
        ClimberSurfaceKind kind = tip.Support;
        ClimberHost host = default;
        bool bound = false;
        if (kind is ClimberSurfaceKind.Woody or ClimberSurfaceKind.Log or ClimberSurfaceKind.Rock)
        {
            if (TryBind(tip, cd, out host) && ProjectHost(host, tip.Position, out _, out _))
            {
                bound = true;
            }
            else
            {
                // The support ended under the shoot. Step back onto the ground if we can still reach it, otherwise
                // run free for a while: a vine that tops a trunk reaches out into the light before it gives up.
                double gy = GroundHeightAt(tip.Position.XZ);
                kind = tip.Position.Y - gy <= cd.AttachmentRadius * 2.0 ? ClimberSurfaceKind.Ground : ClimberSurfaceKind.Free;
                tip.Support = kind;
                tip.HostId = EntityId.None;
                tip.HostIndex = -1;
                tip.Attached = false;
            }
        }
        bool onGround = kind == ClimberSurfaceKind.Ground;
        bool free = kind == ClimberSurfaceKind.Free;

        bool haveHost = false;
        ClimberHost target = default;
        ClimberSurface targetSurface = ClimberSurface.None;
        // Only a shoot that is not holding on has any reason to look for something to hold.
        if (!bound && !free)
        {
            haveHost = TryFindHost(tip.Position, cd.SearchRadius, cd, out target, out targetSurface);
        }

        // 2. Course. Bound to a host the shoot travels along that host's axis; on open ground it steers toward
        //    the nearest host it can sense.
        Vec3 climbDir = tip.ClimbDir;
        Vec3 lateral = tip.Lateral;
        if (bound)
        {
            climbDir = host.Axis.LengthSq > 1e-9 ? host.Axis : Vec3.Up;
        }
        else if (onGround)
        {
            // Open ground has no geometry to wind around, so the shoot's frame follows its own course. Keeping
            // the lateral reference aligned with the course is what stops the twining term from pinning the
            // shoot to the heading it was born with.
            double turn = SteerCourse(tip, cd, targetSurface.Point, haveHost);
            climbDir = tip.ClimbDir;
            if (Math.Abs(turn) > 1e-9)
            {
                Vec3 n = GroundNormalAt(tip.Position.XZ);
                lateral = ClimberGrowth.RotateAround(lateral, n, turn);
            }
        }
        else
        {
            climbDir = Vec3.Zero;
        }

        // 3. Heading inside the tangent plane of the surface under the tip.
        Vec3 surfaceNormal;
        if (bound) ProjectHost(host, tip.Position, out _, out surfaceNormal);
        else if (free) surfaceNormal = Vec3.Up;
        else surfaceNormal = GroundNormalAt(tip.Position.XZ);
        Vec3 light = LightDirection(tip.Position.XZ, out double lightStrength);
        double climbWeight = cd.ClimbWeight * (tip.ShootOrder > 0 ? cd.LateralClimb : 1.0);
        Vec3 heading = ClimberGrowth.Heading(
            surfaceNormal, lateral, tip.Arc,
            climbDir, climbWeight,
            light, cd.LightWeight * lightStrength,
            cd.DriftWeight,
            cd.WanderAmp, cd.WanderPeriod);
        if (heading.LengthSq < 1e-12) heading = tip.Forward.LengthSq > 1e-12 ? tip.Forward : surfaceNormal;

        // 4. March.
        Vec3 raw = tip.Position + heading * cd.SegmentLength;

        // 5. Grab a host when the shoot has actually reached one.
        ClimberSurfaceKind nodeSupport = tip.Support;
        EntityId nodeHostId = tip.HostId;
        int nodeHostIndex = tip.HostIndex;
        bool nodeAttached = tip.Attached;
        double nodeUnsupported = tip.UnsupportedLength;

        if (haveHost && !bound && !free && targetSurface.DistanceTo(raw) <= cd.GrabRadius)
        {
            bound = true;
            host = target;
            raw = targetSurface.Point;
            nodeSupport = target.Kind;
            nodeHostId = target.HostId;
            nodeHostIndex = target.HostIndex;
            nodeAttached = true;
            nodeUnsupported = 0;
            climbDir = target.Axis.LengthSq > 1e-9 ? target.Axis : Vec3.Up;
            // The shoot has arrived, and its frame becomes the host's frame: the lateral reference now runs
            // around the host rather than across the ground, which is what lets the stem wind around it.
            Vec3 around = host.Circumferential(raw);
            if (around.LengthSq > 1e-12) lateral = around.Normalized();
            surfaceNormal = targetSurface.Normal;
        }

        // 6. Lay the stem down on the surface.
        Vec3 next, nextNormal;
        if (bound)
        {
            if (!ProjectHost(host, raw, out next, out nextNormal)) return false;
        }
        else if (free)
        {
            // No support under the tip: the stem hangs and reaches.
            next = raw + new Vec3(0, -cd.SegmentLength * 0.35, 0);
            nextNormal = Vec3.Up;
        }
        else
        {
            next = new Vec3(raw.X, GroundHeightAt(raw.XZ), raw.Z);
            nextNormal = GroundNormalAt(raw.XZ);
        }

        // The stem lies on the surface, so the direction it actually travels is the tangent of the step rather
        // than the heading we aimed with: the surface has the last word.
        Vec3 forward = ClimberGrowth.TangentOf(next - tip.Position, nextNormal);
        if (forward.LengthSq < 1e-12) forward = heading;

        double step = (next - tip.Position).Length;
        if (step < 1e-6) return false;

        // Unsupported shoots give up once they have run too far without anything to hold them.
        if (!nodeAttached)
        {
            nodeUnsupported += step;
            if (nodeUnsupported > cd.MaxUnsupportedLength)
            {
                tip.Terminal = true;
                return false;
            }
        }

        // A lateral shoot is a side branch, not a second leader: it stops once it has run its length, and the
        // shorter the branch the further from the leader it is.
        if (tip.ShootOrder > 0)
        {
            double limit = cd.BranchLength * Math.Pow(0.55, tip.ShootOrder - 1);
            if (tip.LocalArc + step >= limit)
            {
                tip.Terminal = true;
                return false;
            }
        }

        // 7. Transport the lateral reference and commit the new node.
        Vec3 nodeLateral = ClimberGrowth.Transport(lateral, surfaceNormal, nextNormal);
        int depth = tip.Depth + 1;
        int order = tip.ShootOrder;
        Vec3 nodeClimb = climbDir.LengthSq > 1e-12 ? climbDir.Normalized() : tip.ClimbDir;

        var node = new ClimberSegment
        {
            Position = next,
            Normal = nextNormal,
            Forward = forward,
            ParentIndex = tipIndex,
            ShootOrder = order,
            Attached = nodeAttached,
            Terminal = false,
            Age = 0,
            Senescent = false,
            Lateral = nodeLateral,
            ClimbDir = nodeClimb,
            Support = nodeSupport,
            HostId = nodeHostId,
            HostIndex = nodeHostIndex,
            Arc = tip.Arc + step,
            LocalArc = order == 0 ? tip.Arc + step : tip.LocalArc + step,
            Depth = depth,
            Credit = 0,
            Radius = ClimberRadius(f, sp, cd, depth),
            UnsupportedLength = tip.UnsupportedLength,
        };
        segs.Add(node);

        // 8. A runner that has walked far enough over the ground roots and starts a plant of its own. This is
        //    how a creeping vine spreads: it does not seed into the air, it walks and roots where it lands.
        if (nodeSupport == ClimberSurfaceKind.Ground && tip.LocalArc + step >= cd.StolonLength)
        {
            int runners = _climberBuds.Count(b => b.Sp.Id == sp.Id);
            if (runners < cd.PopulationCap)
                _climberBuds.Add((sp, next.XZ, f.Id, Math.Max(sp.InitialBiomass, f.Biomass * 0.12), nodeUnsupported));
        }

        // 8. A lateral shoot may leave the same node. It is the parent's frame turned by the branch angle, so
        //    the shoot really does leave the stem at that angle on any surface.
        if (cd.MaxBranchOrder > 0 && order < cd.MaxBranchOrder && rng.NextDouble() < cd.BranchRate)
        {
            double sign = (cd.DriftWeight >= 0 ? 1.0 : -1.0) * (order % 2 == 0 ? 1.0 : -1.0);
            double angle = cd.BranchAngle * Math.PI / 180.0 * sign;
            var branch = new ClimberSegment
            {
                Position = next,
                Normal = nextNormal,
                Forward = ClimberGrowth.RotateAround(forward, nextNormal, angle),
                ParentIndex = tipIndex,
                ShootOrder = order + 1,
                Attached = node.Attached,
                Terminal = false,
                Age = 0,
                Senescent = false,
                Lateral = ClimberGrowth.RotateAround(nodeLateral, nextNormal, angle),
                ClimbDir = ClimberGrowth.RotateAround(nodeClimb, nextNormal, angle),
                Support = node.Support,
                HostId = node.HostId,
                HostIndex = node.HostIndex,
                Arc = node.Arc,
                LocalArc = 0,
                Depth = depth,
                Credit = 0,
                Radius = node.Radius,
                UnsupportedLength = node.UnsupportedLength,
            };
            segs.Add(branch);
        }

        return true;
    }

    /// <summary>Stem radius at a node: the plant's basal radius tapered by the node's depth.</summary>
    private static double ClimberRadius(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd, int depth) =>
        ClimberGrowth.StemRadius(ClimberGrowth.BasalRadius(f, sp, cd), cd, depth);

    /// <summary>
    /// Turns a ground shoot's course toward the nearest host it can sense and returns how far it turned.
    /// <para>
    /// The turn is rate limited, but the limit is not fixed: a pursuer needs roughly one radian of turn per metre
    /// of separation to close on its target, and a slower shoot simply orbits the host instead of landing on it.
    /// </para>
    /// </summary>
    private static double SteerCourse(ClimberSegment tip, ClimberDef cd, Vec3 hostPoint, bool haveHost)
    {
        Vec3 course = tip.ClimbDir.LengthSq > 1e-9 ? tip.ClimbDir.Normalized() : tip.Forward;
        if (!haveHost || cd.SeekRate <= 0) return 0;

        var to = hostPoint - tip.Position;
        double dist = to.Length;
        if (dist < 1e-6) return 0;
        var dir = to / dist;
        double ang = Math.Acos(Math.Clamp(course.Dot(dir), -1, 1));
        if (ang < 1e-6) return 0;

        double rate = Math.Max(cd.SeekRate * 4.0, 1.5 / Math.Max(dist, 0.03));
        double turn = Math.Min(Math.Min(rate * cd.SegmentLength, ang), 1.0);
        var blended = course * (1 - turn / ang) + dir * (turn / ang);
        tip.ClimbDir = blended.LengthSq > 1e-12 ? blended.Normalized() : course;
        return turn;
    }

    /// <summary>
    /// Direction of the brightest light near a point, derived from the light field's gradient, together with how
    /// strong that pull is. A vine bends toward brighter ground in proportion to the difference, not toward the
    /// faintest gradient as if it were a floodlight.
    /// </summary>
    private Vec3 LightDirection(Vec2 p, out double strength)
    {
        const double e = 0.4;
        double lx = EffectiveLight(p + new Vec2(e, 0)) - EffectiveLight(p - new Vec2(e, 0));
        double lz = EffectiveLight(p + new Vec2(0, e)) - EffectiveLight(p - new Vec2(0, e));
        var d = new Vec3(lx, 0, lz);
        double m = d.Length;
        // A quarter of the exposure range per metre already counts as a full-strength pull.
        strength = MathD.Clamp01(m / 0.25);
        return m > 1e-12 ? d / m : Vec3.Up;
    }

    private double GroundHeightAt(Vec2 p) => _w.GroundHeight(p);
    private Vec3 GroundNormalAt(Vec2 p) => _w.Terrain.Normal(p);

    /// <summary>Resolves the host a node is bound to, and reports whether that support still exists.</summary>
    private bool TryBind(ClimberSegment tip, ClimberDef cd, out ClimberHost host)
    {
        switch (tip.Support)
        {
            case ClimberSurfaceKind.Woody:
            {
                if (_w.Flora.Get(tip.HostId) is not { } tree) { host = default; return false; }
                var nsp = C.FloraOrThrow(tree.SpeciesId);
                if (nsp.Woody == null) { host = default; return false; }
                if (!TryWoodyProfile(tree, nsp, tip.Position.Y, out double r, out double slope, out double top))
                { host = default; return false; }
                host = new ClimberHost(ClimberSurfaceKind.Woody, tree.Id, -1, tree, nsp, null, null, r, slope, top);
                return true;
            }
            case ClimberSurfaceKind.Log:
            {
                var logs = _w.Props.Logs;
                if (tip.HostIndex < 0 || tip.HostIndex >= logs.Count) { host = default; return false; }
                host = new ClimberHost(ClimberSurfaceKind.Log, EntityId.None, tip.HostIndex, null, null, logs[tip.HostIndex], null, 0, 0, 0);
                return true;
            }
            case ClimberSurfaceKind.Rock:
            {
                var rocks = _w.Props.Rocks;
                if (tip.HostIndex < 0 || tip.HostIndex >= rocks.Count) { host = default; return false; }
                host = new ClimberHost(ClimberSurfaceKind.Rock, EntityId.None, tip.HostIndex, null, null, null, rocks[tip.HostIndex], 0, 0, 0);
                return true;
            }
            default:
                host = default;
                return false;
        }
    }

    /// <summary>Projects a point onto a host's live surface.</summary>
    private bool ProjectHost(in ClimberHost host, Vec3 p, out Vec3 point, out Vec3 normal)
    {
        point = p;
        normal = Vec3.Up;
        switch (host.Kind)
        {
            case ClimberSurfaceKind.Woody:
            {
                if (host.Tree is not { } tree || host.TreeSpecies is not { } nsp) return false;
                // The radius is read at the height being projected to, not at the height the shoot left: a stem
                // that flares at the foot would otherwise be left hanging in the air above its own bark.
                var (r, ground, top) = TreeTrunkProfile(tree, nsp, p.Y);
                if (p.Y < ground - 0.05 || p.Y > top) return false;
                var (r2, _, _) = TreeTrunkProfile(tree, nsp, p.Y + 0.05);
                double slope = (r2 - r) / 0.05;
                if (!ClimberSurface.TryWoody(p, tree.X, tree.Z, r, slope, top, tree.Id, out var s)) return false;
                point = s.Point; normal = s.Normal;
                return true;
            }
            case ClimberSurfaceKind.Log:
            {
                if (host.Log is not { } log) return false;
                if (!ClimberSurface.TryLog(p, log, host.HostIndex, out var l)) return false;
                point = l.Point; normal = l.Normal;
                return true;
            }
            case ClimberSurfaceKind.Rock:
            {
                if (host.RockProp is not { } rock) return false;
                if (!ClimberSurface.TryRock(p, rock, host.HostIndex, out var rk)) return false;
                point = rk.Point; normal = rk.Normal;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Trunk radius and taper at a height, and the height at which the stem ends.</summary>
    private bool TryWoodyProfile(FloraIndividual tree, FloraSpeciesDef nsp, double y,
        out double radius, out double slope, out double top)
    {
        var (r, ground, trunkTop) = TreeTrunkProfile(tree, nsp, y);
        radius = r; top = trunkTop;
        if (y < ground - 0.05 || y > trunkTop)
        {
            slope = 0;
            return false;
        }
        var (r2, _, _) = TreeTrunkProfile(tree, nsp, y + 0.05);
        slope = (r2 - r) / 0.05;
        return true;
    }

    /// <summary>
    /// Finds the nearest support within a radius. Sensing only biases growth; the shoot still has to travel the
    /// distance before it can hold on.
    /// </summary>
    private bool TryFindHost(Vec3 p, double radius, ClimberDef cd, out ClimberHost host, out ClimberSurface surface)
    {
        host = default;
        surface = ClimberSurface.None;
        double best = radius;
        bool found = false;

        if (cd.SupportTypes.Contains("woody"))
        {
            _w.Flora.Neighbours(p.XZ, radius + 1.5, _climberSupportNeighbours);
            foreach (var n in _climberSupportNeighbours)
            {
                var nsp = C.FloraOrThrow(n.SpeciesId);
                if (nsp.Woody == null) continue;
                if (!TryWoodyProfile(n, nsp, p.Y, out double r, out double slope, out double top)) continue;
                if (!ClimberSurface.TryWoody(p, n.X, n.Z, r, slope, top, n.Id, out var s)) continue;
                double d = s.DistanceTo(p);
                if (d < best)
                {
                    best = d; surface = s; found = true;
                    host = new ClimberHost(ClimberSurfaceKind.Woody, n.Id, -1, n, nsp, null, null, r, slope, top);
                }
            }
        }

        if (cd.SupportTypes.Contains("log"))
        {
            var logs = _w.Props.Logs;
            for (int i = 0; i < logs.Count; i++)
            {
                var l = logs[i];
                if (Vec2.DistanceSq(new Vec2(l.X, l.Z), p.XZ) > (radius + l.FootprintRadius) * (radius + l.FootprintRadius)) continue;
                if (!ClimberSurface.TryLog(p, l, i, out var s)) continue;
                double d = s.DistanceTo(p);
                if (d < best)
                {
                    best = d; surface = s; found = true;
                    host = new ClimberHost(ClimberSurfaceKind.Log, EntityId.None, i, null, null, l, null, 0, 0, 0);
                }
            }
        }

        if (cd.SupportTypes.Contains("rock"))
        {
            var rocks = _w.Props.Rocks;
            for (int i = 0; i < rocks.Count; i++)
            {
                var r = rocks[i];
                if (Vec2.DistanceSq(new Vec2(r.X, r.Z), p.XZ) > (radius + r.FootprintRadius) * (radius + r.FootprintRadius)) continue;
                if (!ClimberSurface.TryRock(p, r, i, out var s)) continue;
                double d = s.DistanceTo(p);
                if (d < best)
                {
                    best = d; surface = s; found = true;
                    host = new ClimberHost(ClimberSurfaceKind.Rock, EntityId.None, i, null, null, null, r, 0, 0, 0);
                }
            }
        }

        return found;
    }

    /// <summary>A support a vine shoot can be bound to, with the data needed to project onto it.</summary>
    private readonly struct ClimberHost
    {
        public readonly ClimberSurfaceKind Kind;
        public readonly EntityId HostId;
        public readonly int HostIndex;
        public readonly FloraIndividual? Tree;
        public readonly FloraSpeciesDef? TreeSpecies;
        public readonly LogProp? Log;
        public readonly Rock? RockProp;
        /// <summary>Trunk radius at the binding height.</summary>
        public readonly double Radius;
        /// <summary>Trunk taper (dr/dy) at the binding height.</summary>
        public readonly double Slope;
        /// <summary>Height at which the stem ends.</summary>
        public readonly double Top;

        /// <summary>The direction a shoot bound to this host travels along it.</summary>
        public Vec3 Axis => Kind switch
        {
            ClimberSurfaceKind.Woody => Vec3.Up,
            ClimberSurfaceKind.Log => Log is { } l ? new Vec3(Math.Cos(l.RotationY), 0, Math.Sin(l.RotationY)) : Vec3.Up,
            _ => Vec3.Up,
        };

        /// <summary>The direction that runs around this host at a point, or zero when the host has no axis.</summary>
        public Vec3 Circumferential(Vec3 p)
        {
            switch (Kind)
            {
                case ClimberSurfaceKind.Woody:
                {
                    if (Tree is not { } t) return Vec3.Zero;
                    double dx = p.X - t.X, dz = p.Z - t.Z;
                    double h = Math.Sqrt(dx * dx + dz * dz);
                    if (h < 1e-7) return Vec3.Up.Cross(Vec3.Right);
                    var radial = new Vec3(dx / h, 0, dz / h);
                    return Vec3.Up.Cross(radial);
                }
                case ClimberSurfaceKind.Log:
                {
                    if (Log is not { } l) return Vec3.Zero;
                    var axis = new Vec3(Math.Cos(l.RotationY), 0, Math.Sin(l.RotationY));
                    var centre = new Vec3(l.X, l.Y, l.Z);
                    var rel = p - centre;
                    var perp = rel - axis * rel.Dot(axis);
                    double pl = perp.Length;
                    if (pl < 1e-7) return Vec3.Zero;
                    return axis.Cross(perp / pl);
                }
                default:
                    return Vec3.Zero;
            }
        }

        public ClimberHost(ClimberSurfaceKind kind, EntityId hostId, int hostIndex,
            FloraIndividual? tree, FloraSpeciesDef? treeSpecies, LogProp? log, Rock? rockProp,
            double radius, double slope, double top)
        {
            Kind = kind; HostId = hostId; HostIndex = hostIndex;
            Tree = tree; TreeSpecies = treeSpecies; Log = log; RockProp = rockProp;
            Radius = radius; Slope = slope; Top = top;
        }
    }
    /// <summary>
    /// Colonial growth (moss/lichen): a cell with fewer than 3 same-species neighbours within 2·cellRadius is on
    /// the rim and buds outward, away from the neighbour centroid plus jitter. Interior cells stop spreading and
    /// thicken toward the species' colony max height instead.
    /// </summary>
    private bool ColonyStep(FloraIndividual f, FloraSpeciesDef sp, FloraColonyDef cd, Vec2 p, double dt, List<(FloraIndividual F, string Cause)> deaths)
    {
        _w.Flora.Neighbours(p, cd.CellRadius * 2, _nbColony);
        int neighbourCount = 0; var centroidSum = Vec2.Zero;
        FloraIndividual? mergeTarget = null;
        foreach (var n in _nbColony)
        {
            if (n.Id == f.Id || n.SpeciesId != sp.Id) continue;
            neighbourCount++; centroidSum += n.Position;
            if (n.ColonyRoot == f.ColonyRoot && n.MergeFactor < MergeFactorCap && (mergeTarget == null || n.Id.Value < mergeTarget.Id.Value))
                mergeTarget = n;
        }
        bool edge = neighbourCount < 3;
        var root = f.ColonyRoot.IsNone ? f.Id : f.ColonyRoot;
        if (edge)
        {
            if (_colonialTotal >= GlobalColonialCellBudget || _colonySize.GetValueOrDefault(root) >= ColonyCellCap) return false;
            var rng = Rng.Keyed(_w.Seed, "flora.colony.bud", f.Id.Value, (ulong)f.SpreadCount++);
            if (rng.NextDouble() >= Math.Min(1, cd.FrontRate * dt)) return false;
            var away = neighbourCount > 0 ? p - centroidSum / neighbourCount : Vec2.Zero;
            double baseAngle = away.LengthSq > 1e-10 ? away.Angle : rng.Range(0, 2 * Math.PI);
            double ang = baseAngle + rng.Range(-0.6, 0.6);
            double dist = cd.CellRadius * rng.Range(1.5, 2.0);
            var q = p + Vec2.FromAngle(ang) * dist;
            if (CanEstablish(sp, q, out _)) _colonyBuds.Add((sp, q, root, f.RingDist + dist));
            return false;
        }

        f.HeightFactor = Math.Min(1, f.HeightFactor + cd.FillRate * dt);
        // fully surrounded and fully thickened: fold this cell into a same-colony neighbour instead of keeping
        // it as its own entity. The mat's footprint and lumpiness are preserved (the survivor's radius grows
        // with the absorbed area) while the interior's entity cost stops climbing as the mat matures.
        if (neighbourCount >= MergeNeighbourThreshold && f.HeightFactor >= 1 && mergeTarget != null)
        {
            mergeTarget.MergeFactor = Math.Min(MergeFactorCap, mergeTarget.MergeFactor + f.MergeFactor);
            deaths.Add((f, "colony-merge"));
            _colonySize[root] = Math.Max(0, _colonySize.GetValueOrDefault(root) - 1);
            _colonialTotal--;
            return true;
        }
        return false;
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
        if (sp.IsCoverageSpecies)
        {
            reason = $"{sp.Name} grows on the coverage layers, not as individuals";
            return false;
        }
        if (sp.Climber is { } climberDef)
        {
            int count = 0;
            foreach (var other in _w.Flora.Items)
                if (other.SpeciesId == sp.Id) count++;
            if (count >= climberDef.PopulationCap)
            {
                reason = $"{sp.Name} is at its population limit ({count}/{climberDef.PopulationCap})";
                return false;
            }
        }
        if (sp.Woody is { } woody)
        {
            int cap = WoodyPopulationCap(woody.Layer);
            int count = WoodyPopulation(woody.Layer);
            if (woody.Layer == WoodyLayer.Tree && count >= cap)
            {
                reason = $"{(woody.Layer == WoodyLayer.Tree ? "tree" : "shrub")} population at island carrying limit ({count}/{cap})";
                return false;
            }
            double querySpacing = C.Flora
                .Where(candidate => candidate.Woody?.Layer == woody.Layer)
                .Select(candidate => candidate.Woody!.MinSpacing)
                .DefaultIfEmpty(woody.MinSpacing)
                .Max();
            _w.Flora.Neighbours(q, querySpacing, _nb);
            foreach (var n in _nb)
            {
                var nsp = C.FloraOrThrow(n.SpeciesId);
                if (nsp.Woody?.Layer != woody.Layer) continue;
                double spacing = Math.Max(woody.MinSpacing, nsp.Woody.MinSpacing);
                double distance = Vec2.Distance(q, n.Position);
                if (distance >= spacing) continue;
                reason = $"too close to {nsp.Name} ({distance:0.00} m < {spacing:0.00} m)";
                return false;
            }
        }
        // Creeping rooted carpets expand by filling real space, not by
        // placing hundreds of overlapping full organism agents on the same
        // square decimetre. Existing individuals remain unchanged; only a
        // *new* propagule is refused when an established same-species crown
        // already occupies its physical rooting site. This is local geometry,
        // never a species-wide population cap or equilibrium enforcement.
        if (sp.Archetype=="plant" &&
            (sp.Tags.Contains("groundcover") || sp.Shape=="carpet"))
        {
            double initial = MathD.Clamp01(sp.InitialBiomass/Math.Max(sp.MaxBiomass,1e-9));
            double seedlingRadius=sp.MinRadius+
                (sp.RadiusAtMax-sp.MinRadius)*Math.Sqrt(initial);
            _w.Flora.Neighbours(q,sp.RadiusAtMax+seedlingRadius,_nb);
            foreach(var existing in _nb)
            {
                if(existing.SpeciesId!=sp.Id || existing.Health<=0.15)continue;
                double occupiedRadius=Math.Min(sp.RadiusAtMax*1.18,
                    existing.Radius(sp)*0.8+seedlingRadius*0.6);
                if(Vec2.DistanceSq(q,existing.Position)<occupiedRadius*occupiedRadius)
                {
                    reason=$"propagule cannot root inside living {sp.Name} carpet";
                    return false;
                }
            }
        }
        var s = Suitability(sp, q);
        if (s.HardRefused) { reason = s.RefusalReason; return false; }
        if (s.EstablishmentScore < sp.MinSuitability) { reason = $"establishment habitat too poor ({s})"; return false; }
        double crowd = Crowding(sp, q, EntityId.None);
        if (crowd >= sp.CrowdingLimit) { reason = $"too crowded ({crowd:0.00} ≥ {sp.CrowdingLimit:0.00})"; return false; }
        reason = "ok";
        return true;
    }

    public FloraIndividual Establish(FloraSpeciesDef sp, Vec2 q, string cause, double? biomass = null)
    {
        if (sp.IsCoverageSpecies)
            throw new InvalidOperationException($"{sp.Id} grows on the coverage layers and cannot be established as a FloraIndividual.");
        var id = _w.Ids.Next(EntityKind.Flora);
        var rng = Rng.Keyed(_w.Seed, "flora.establish", id.Value);
        var f = new FloraIndividual
        {
            Id = id, SpeciesId = sp.Id, X = q.X, Z = q.Z, Biomass = biomass ?? sp.InitialBiomass, Health = 1,
            LifespanFactor = rng.Range(0.8, 1.2),
            ClimberTip = sp.Climber != null,
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
        _w.Tally.Death(sp.Id, cause);

        bool persistentCorpse = sp.Archetype == "plant" && sp.Colony == null && !sp.IsCoverageSpecies
            && sp.Climber == null && sp.Shape != "floatleaf";
        if (persistentCorpse)
        {
            _w.DeadFloraSystem.CreateFrom(f, sp, cause);
            _w.Tally.DeadFloraCreated++;
            return true;
        }

        double litter = f.Biomass * sp.LitterFraction;
        double direct = f.Biomass * (1 - sp.LitterFraction) * sp.NutrientPerBiomass;
        _w.Ecology.ReturnOrganicMatter(f.Position, litter, direct, fromFlora: true);
        return true;
    }
}
