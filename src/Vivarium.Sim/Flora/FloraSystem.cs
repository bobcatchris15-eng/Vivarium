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
    private readonly List<FloraIndividual> _woodyCanopy = new();
    private readonly ScalarField _canopyShade;
    private int _woodyCanopyVersion = -1;
    private int _canopyShadeVersion = -1;
    private bool _canopyShadeDirty = true;
    public const string PropagationStream = "flora.propagation";
    public const double TreeAreaPerIndividual = 15.0;
    public const double ShrubAreaPerIndividual = 4.0;

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
        double areaPer = layer == WoodyLayer.Tree ? TreeAreaPerIndividual : ShrubAreaPerIndividual;
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
            foreach (int cell in _w.Grid.DomainCells) _canopyShade.Values[cell] = 0;
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
        if (sp.MinWaterDepth > 0 && depth < sp.MinWaterDepth - 1e-9) return FloraSuitability.Refused($"needs standing water ({depth * 100:0.#} cm < {sp.MinWaterDepth * 100:0.#} cm)", sub);
        double moisture = _w.Fields.Moisture.Sample(p);
        if (moisture < sp.HardMinMoisture) return FloraSuitability.Refused($"too dry (moisture {moisture:0.00} < {sp.HardMinMoisture:0.00})", sub);
        if (!onProp && moisture > sp.HardMaxMoisture + 1e-9) return FloraSuitability.Refused($"too wet (moisture {moisture:0.00} > {sp.HardMaxMoisture:0.00})", sub);
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

        double light = EffectiveLight(p, self);
        // decomposers judge their food (dead matter) where plants judge soil nutrients
        double nutrients = sp.Decomposer ? MathD.Clamp01(SampleDetritusPlusLitter(p) / C.Ecology.DetritusMax) : _w.Fields.Nutrients.Sample(p) / C.Ecology.NutrientMax;
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

    public void Step(double dt)
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
            var sp0 = C.FloraOrThrow(f.SpeciesId);
            if (sp0.Climber != null)
                _climberNodeCounts[f.SpeciesId] = _climberNodeCounts.GetValueOrDefault(f.SpeciesId) + 1;
            if (sp0.Colony == null) continue;
            var root0 = f.ColonyRoot.IsNone ? f.Id : f.ColonyRoot;
            _colonySize[root0] = _colonySize.GetValueOrDefault(root0) + 1;
            _colonialTotal++;
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
            // Shedding becomes visible surface litter first; microbes transfer it to detritus over time.
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
            if (sp.Reproduction == null && f.Stage(sp) != FloraStage.Juvenile && f.BiomassFraction(sp) >= sp.SpreadMinBiomassFraction
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
        foreach (var (sp, q, parent, biomass, unsupportedLength) in _climberBuds)
        {
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
            if (sp.Climber is { } cd && NearestClimberSupport(nf, cd) is { } support && support.Distance <= cd.AttachmentRadius)
            {
                nf.ClimberAttached = true;
                nf.ClimberTip = false;
            }
        }
        foreach (var (sp, q, root, ringDist) in _colonyBuds)
        {
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
            if (sp.Archetype is not ("fungus" or "slime_mold")) continue;
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
                if (sp.Archetype == "slime_mold") AssignSlimeTint(nf, 0);
            }
        }
    }

    /// <summary>Distance between a patch of plasmodium and the growth-front patch it buds (m).</summary>
    public const double BudStep = 0.15;

    /// <summary>
    /// Total organic matter available to a decomposer at <paramref name="p"/>: the bioavailable
    /// detritus field plus the fine surface-litter reservoir that has not yet been microbially
    /// converted. Litter converts on a ~20 sim-day half-life; using this combined value stops the
    /// plasmodium from ignoring a thick leaf-litter layer because the decay step has not yet moved
    /// mass into <c>Fields.Detritus</c>.
    /// </summary>
    private double SampleDetritusPlusLitter(Vec2 p)
    {
        double detritus = _w.Fields.Detritus.Sample(p);
        int cell = _w.Grid.NearestDomainCell(p);
        double litter = cell >= 0 ? _w.Litter.FineMass[cell] : 0;
        return detritus + litter;
    }

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

        double forkRelH;
        double baseBoleRatio;
        if (sp.Id == "ironlace")
        {
            forkRelH = 0.26;
            baseBoleRatio = 0.075;
        }
        else if (sp.Id == "umbraheart")
        {
            forkRelH = 0.42;
            baseBoleRatio = 0.135;
        }
        else if (sp.Id == "fenneedle")
        {
            forkRelH = 0.40;
            baseBoleRatio = 0.080;
        }
        else
        {
            forkRelH = 0.28;
            baseBoleRatio = 0.085;
        }

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
    /// Sequential segment-by-segment growth and attachment for vines and climbers.
    /// Each segment actively checks for host material; growth stops when reaching empty space (the end of the support),
    /// and distal segments die back during senescence.
    /// </summary>
    private bool ClimberStep(FloraIndividual f, FloraSpeciesDef sp, ClimberDef cd, double dt)
    {
        // 1. Initialize segment network if not yet established
        if (f.ClimberSegments == null)
        {
            f.ClimberSegments = new List<ClimberSegment>();
            double gy = double.IsNaN(f.GroundHeight) ? (f.GroundHeight = _w.GroundHeight(f.Position)) : f.GroundHeight;
            f.ClimberSegments.Add(new ClimberSegment
            {
                Position = new Vec3(f.X, gy, f.Z),
                Normal = Vec3.Up,
                Forward = new Vec3(0, 0, 1),
                ParentIndex = -1,
                ShootOrder = 0,
                Attached = false,
                Terminal = false,
                Age = 0,
                Senescent = false,
            });
        }

        // 2. Senescence & Die Back: when reaching senescence age, distal segments start to die back
        if (f.Stage(sp) == FloraStage.Senescent)
        {
            f.CreepCredit = 0;
            double senescentFrac = Math.Clamp((f.Age - sp.Lifespan * f.LifespanFactor * 0.85) / Math.Max(0.01, sp.Lifespan * f.LifespanFactor * 0.15), 0, 1);
            int countToDie = (int)(f.ClimberSegments.Count * senescentFrac);
            for (int i = f.ClimberSegments.Count - 1; i >= Math.Max(1, f.ClimberSegments.Count - countToDie); i--)
            {
                f.ClimberSegments[i].Senescent = true;
            }
            return true;
        }

        // 3. Growth accumulation
        f.CreepCredit = Math.Min(f.CreepCredit + cd.GroundSpeed * dt * (0.35 + 0.65 * f.BiomassFraction(sp)), cd.SegmentLength * 2.5);
        if (f.CreepCredit < cd.SegmentLength) return true;
        if (f.ClimberSegments.Count >= cd.NodeCap) return true;

        var rng = Rng.Keyed(_w.Seed, "flora.climber.step", f.Id.Value, (ulong)f.ClimberSegments.Count);

        // Find active, non-terminal, non-senescent growth tips
        var tipIndices = new List<int>();
        for (int i = 0; i < f.ClimberSegments.Count; i++)
        {
            var seg = f.ClimberSegments[i];
            if (!seg.Terminal && !seg.Senescent)
            {
                int childCount = 0;
                for (int j = 0; j < f.ClimberSegments.Count; j++)
                    if (f.ClimberSegments[j].ParentIndex == i) childCount++;

                if (childCount == 0 || (childCount < 2 && rng.NextDouble() < cd.BranchChance))
                    tipIndices.Add(i);
            }
        }

        if (tipIndices.Count == 0) return true;

        int chosenTipIndex = tipIndices[(int)(rng.NextDouble() * tipIndices.Count)];
        var tip = f.ClimberSegments[chosenTipIndex];

        Vec3 candPos;
        Vec3 candNormal = Vec3.Up;
        bool isAttached = tip.Attached;
        bool isTerminal = false;

        if (!tip.Attached)
        {
            // Ground runner: searching across ground for nearest support
            var sup = NearestClimberSupport(f, cd);
            Vec2 walkDir;
            if (sup is { } s0 && (s0.Target - tip.Position.XZ).LengthSq > 1e-6)
                walkDir = (s0.Target - tip.Position.XZ).Normalized();
            else
                walkDir = Vec2.FromAngle(rng.Range(0, Math.PI * 2));

            var candXZ = tip.Position.XZ + walkDir * cd.SegmentLength;
            double candY = _w.GroundHeight(candXZ);
            candPos = new Vec3(candXZ.X, candY, candXZ.Z);

            // Check if reached host material
            var hit = CheckHostMaterial(candPos, cd.AttachmentRadius, cd.SupportTypes);
            if (hit.HasMaterial)
            {
                candPos = hit.SurfacePoint;
                candNormal = hit.SurfaceNormal;
                isAttached = true;
                f.ClimberAttached = true;
            }
            else if (f.UnsupportedLength + cd.SegmentLength > cd.MaxUnsupportedLength)
            {
                isTerminal = true;
            }
        }
        else
        {
            // Host-attached climber: ascending and branching segment by segment along host contour
            var hostHit = CheckHostMaterial(tip.Position, cd.AttachmentRadius * 2.5, cd.SupportTypes);
            if (hostHit.HasMaterial && hostHit.HostType == "woody" && _w.Flora.Get(hostHit.HostId) is { } tree)
            {
                var nsp = C.FloraOrThrow(tree.SpeciesId);
                var toTip = tip.Position.XZ - tree.Position;
                double curTheta = toTip.LengthSq > 1e-8 ? Math.Atan2(toTip.Z, toTip.X) : 0.0;
                double curY = tip.Position.Y;

                if (sp.Id == "spiralvine" || sp.Shape.Contains("spiral"))
                {
                    // SPIRALVINE: Stem-twiner. Winds in a continuous upward helix around the trunk.
                    double chirality = ((f.Id.Value ^ 0x5A) % 2 == 0) ? 1.0 : -1.0;
                    double pitchAngle = 0.82; // ~47 degrees
                    double stepY = cd.SegmentLength * Math.Sin(pitchAngle);
                    double stepS = cd.SegmentLength * Math.Cos(pitchAngle);

                    double nextY = curY + stepY;
                    var (nextRadius, treeGround, trunkTop) = TreeTrunkProfile(tree, nsp, nextY);

                    if (nextY <= trunkTop)
                    {
                        double dTheta = (chirality * stepS) / Math.Max(0.04, nextRadius);
                        // Secondary strand in twin-helix liana: offset by 180 degrees
                        if (tip.ShootOrder > 0 && tip.ParentIndex == 0)
                            curTheta += Math.PI;

                        double nextTheta = curTheta + dTheta;
                        var outward = Vec2.FromAngle(nextTheta);
                        candPos = new Vec3(tree.Position.X + outward.X * (nextRadius + 0.006), nextY, tree.Position.Z + outward.Z * (nextRadius + 0.006));
                        candNormal = new Vec3(outward.X, 0, outward.Z);
                        isAttached = true;
                    }
                    else
                    {
                        // Reached empty space above host trunk: free-reaching tendril whips searching for light
                        candPos = tip.Position + (tip.Normal * 0.45 + Vec3.Up * 0.85).Normalized() * (cd.SegmentLength * 0.85);
                        candNormal = Vec3.Up;
                        isAttached = false;
                        isTerminal = true;
                    }
                }
                else if (sp.Id == "fenhook" || sp.Shape.Contains("fenhook"))
                {
                    // FENHOOK: Scrambler / Clamberer.
                    // Climbs with irregular wander, hooks onto crevices, and sends cascading hanging curtains/swags downward!
                    bool isSwag = (tip.ShootOrder > 0) || (rng.NextDouble() < 0.35 && curY > tree.GroundHeight + 0.25);
                    if (isSwag)
                    {
                        // Cascading hanging swag: droops DOWNWARD under gravity along the bark!
                        double stepY = -cd.SegmentLength * rng.Range(0.60, 0.82);
                        double nextY = curY + stepY;
                        if (nextY >= tree.GroundHeight + 0.05)
                        {
                            var (nextRadius, _, _) = TreeTrunkProfile(tree, nsp, nextY);
                            double dTheta = rng.Range(-0.15, 0.15) * (cd.SegmentLength / Math.Max(0.04, nextRadius));
                            double nextTheta = curTheta + dTheta;
                            var outward = Vec2.FromAngle(nextTheta);
                            candPos = new Vec3(tree.Position.X + outward.X * (nextRadius + 0.008), nextY, tree.Position.Z + outward.Z * (nextRadius + 0.008));
                            candNormal = new Vec3(outward.X, -0.35, outward.Z).Normalized();
                            isAttached = true;
                            // Hanging swags terminate after 2-4 drooping segments
                            if (tip.ShootOrder >= 3 || rng.NextDouble() < 0.30)
                                isTerminal = true;
                        }
                        else
                        {
                            candPos = tip.Position;
                            isTerminal = true;
                        }
                    }
                    else
                    {
                        // Clambering upward shoot
                        double stepY = cd.SegmentLength * rng.Range(0.65, 0.85);
                        double nextY = curY + stepY;
                        var (nextRadius, treeGround, trunkTop) = TreeTrunkProfile(tree, nsp, nextY);

                        if (nextY <= trunkTop)
                        {
                            double dTheta = rng.Range(-0.35, 0.35) * (cd.SegmentLength / Math.Max(0.04, nextRadius));
                            double nextTheta = curTheta + dTheta;
                            var outward = Vec2.FromAngle(nextTheta);
                            candPos = new Vec3(tree.Position.X + outward.X * (nextRadius + 0.006), nextY, tree.Position.Z + outward.Z * (nextRadius + 0.006));
                            candNormal = new Vec3(outward.X, 0, outward.Z);
                            isAttached = true;
                        }
                        else
                        {
                            // Reached top/branch tip: droops over the edge in cascading pendulous swag into empty space
                            candPos = tip.Position + tip.Normal * 0.04 - Vec3.Up * (cd.SegmentLength * 0.80);
                            candNormal = -Vec3.Up;
                            isAttached = false;
                            isTerminal = true;
                        }
                    }
                }
                else
                {
                    // CLINGLACE: Root-climber (Hedera helix).
                    // Hugs host bark tightly with adventitious rootlet pads; primary shoots ascend,
                    // lateral shoots splay horizontally around the circumference into an interlocking mantle.
                    bool isLateral = tip.ShootOrder > 0 && rng.NextDouble() < 0.65;
                    if (isLateral)
                    {
                        // Splay laterally around trunk circumference
                        double dir = (rng.NextDouble() < 0.5 ? 1.0 : -1.0);
                        double stepS = cd.SegmentLength * rng.Range(0.60, 0.85);
                        double stepY = cd.SegmentLength * rng.Range(0.18, 0.38);
                        double nextY = curY + stepY;
                        var (nextRadius, _, trunkTop) = TreeTrunkProfile(tree, nsp, nextY);

                        if (nextY <= trunkTop)
                        {
                            double dTheta = (dir * stepS) / Math.Max(0.04, nextRadius);
                            double nextTheta = curTheta + dTheta;
                            var outward = Vec2.FromAngle(nextTheta);
                            candPos = new Vec3(tree.Position.X + outward.X * (nextRadius + 0.004), nextY, tree.Position.Z + outward.Z * (nextRadius + 0.004));
                            candNormal = new Vec3(outward.X, 0, outward.Z);
                            isAttached = true;
                        }
                        else
                        {
                            candPos = tip.Position;
                            isTerminal = true;
                        }
                    }
                    else
                    {
                        // Ascending vertical shoot
                        double stepY = cd.SegmentLength * rng.Range(0.80, 0.95);
                        double nextY = curY + stepY;
                        var (nextRadius, treeGround, trunkTop) = TreeTrunkProfile(tree, nsp, nextY);

                        if (nextY <= trunkTop)
                        {
                            double dTheta = rng.Range(-0.12, 0.12) * (cd.SegmentLength / Math.Max(0.04, nextRadius));
                            double nextTheta = curTheta + dTheta;
                            var outward = Vec2.FromAngle(nextTheta);
                            candPos = new Vec3(tree.Position.X + outward.X * (nextRadius + 0.004), nextY, tree.Position.Z + outward.Z * (nextRadius + 0.004));
                            candNormal = new Vec3(outward.X, 0, outward.Z);
                            isAttached = true;
                        }
                        else
                        {
                            // Reached bark rim at crown: terminates immediately (cannot climb into empty air)
                            candPos = tip.Position;
                            isTerminal = true;
                        }
                    }
                }
            }
            else
            {
                // Fallback for logs or rocks: generic contour climb
                double stepY = cd.SegmentLength * (sp.Height > 0.5 ? 0.90 : 0.75);
                double nextY = tip.Position.Y + stepY;
                var testPos = new Vec3(tip.Position.X, nextY, tip.Position.Z);
                var hit = CheckHostMaterial(testPos, cd.AttachmentRadius * 1.5, cd.SupportTypes);
                if (hit.HasMaterial)
                {
                    candPos = hit.SurfacePoint;
                    candNormal = hit.SurfaceNormal;
                    isAttached = true;
                }
                else
                {
                    candPos = tip.Position;
                    isTerminal = true;
                }
            }
        }

        if (!isTerminal || candPos != tip.Position)
        {
            var fwd = (candPos - tip.Position).LengthSq > 1e-6 ? (candPos - tip.Position).Normalized() : tip.Forward;
            f.ClimberSegments.Add(new ClimberSegment
            {
                Position = candPos,
                Normal = candNormal,
                Forward = fwd,
                ParentIndex = chosenTipIndex,
                ShootOrder = tip.ShootOrder + (chosenTipIndex != tipIndices[0] ? 1 : 0),
                Attached = isAttached,
                Terminal = isTerminal,
                Age = 0,
                Senescent = false,
            });
            f.CreepCredit -= cd.SegmentLength;
        }
        else
        {
            tip.Terminal = true;
        }

        return true;
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
        if (sp.Woody is { } woody)
        {
            int cap = WoodyPopulationCap(woody.Layer);
            int count = WoodyPopulation(woody.Layer);
            if (count >= cap)
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
