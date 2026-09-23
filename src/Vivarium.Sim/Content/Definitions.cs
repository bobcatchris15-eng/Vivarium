namespace Vivarium.Sim.Content;

/// <summary>Substrate classes used by habitat rules. Independent from visible material.</summary>
public enum Substrate : byte { Soil = 0, Gravel = 1, Rock = 2, Wood = 3, Water = 4 }

public static class SubstrateIds
{
    public static readonly Substrate[] All = { Substrate.Soil, Substrate.Gravel, Substrate.Rock, Substrate.Wood, Substrate.Water };
    public static string Id(Substrate s) => s.ToString().ToLowerInvariant();
    public static bool TryParse(string id, out Substrate s)
    {
        foreach (var x in All) if (Id(x) == id) { s = x; return true; }
        s = Substrate.Soil; return false;
    }
}

public static class SimUnits
{
    public const double Minute = 60, Hour = 3600, Day = 86400, Week = 7 * Day;
}

public sealed class SubstrateDef
{
    public Substrate Substrate { get; init; }
    public string Name { get; init; } = "";
    public double[] Color { get; init; } = { 1, 1, 1 };
    public string Description { get; init; } = "";
}

public sealed class StratumDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Thickness in metres below the previous layer; the last layer fills to the island bottom.</summary>
    public double Thickness { get; init; }
    public double[] Color { get; init; } = { 1, 1, 1 };
    public double[] Color2 { get; init; } = { 1, 1, 1 };
    /// <summary>0..1 visual grain/speckle strength for the cut-face shader.</summary>
    public double Grain { get; init; }
    /// <summary>0..1 horizontal banding strength.</summary>
    public double Banding { get; init; }
}

/// <summary>Gaussian-shaped preference (value at optimum = 1, σ = tolerance).</summary>
public readonly record struct Pref(double Optimum, double Tolerance)
{
    public double Eval(double v) => Core.MathD.Preference(v, Optimum, Tolerance);
}

public sealed class ProximityRule
{
    /// <summary>"feature:log", "feature:rock", "feature:water", "feature:gravel" or a flora species id.</summary>
    public string Target { get; init; } = "";
    public double Radius { get; init; }
    public double Bonus { get; init; }
    public bool IsFeature => Target.StartsWith("feature:", StringComparison.Ordinal);
    public string Feature => IsFeature ? Target.Substring(8) : "";
}

public sealed class FloraSpeciesDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Archetype { get; init; } = "";   // moss | lichen | plant
    public string Role { get; init; } = "";
    public string Description { get; init; } = "";
    public string SourceFile { get; init; } = "";

    // habitat
    public Dictionary<Substrate, double> SubstrateAffinity { get; init; } = new();
    public HashSet<Substrate> RefuseSubstrates { get; init; } = new();
    public HashSet<string> RefuseTags { get; init; } = new(StringComparer.Ordinal);
    public Pref Moisture { get; init; }
    public Pref Light { get; init; }
    public Pref Nutrients { get; init; }
    public double MaxWaterDepth { get; init; }
    public double HardMinMoisture { get; init; }
    public double MinSuitability { get; init; }

    // growth (rates converted to per-second at load)
    public double GrowthRate { get; init; }          // logistic r, 1/s
    public double MaxBiomass { get; init; }
    public double InitialBiomass { get; init; }
    public double DeclineRate { get; init; }         // 1/s at zero suitability
    public double MaturityAge { get; init; }         // s
    public double Lifespan { get; init; }            // s
    public double NutrientPerBiomass { get; init; }
    public double RadiusAtMax { get; init; }         // m, visual + competition footprint
    public double MinRadius { get; init; }

    // propagation
    public double SpreadInterval { get; init; }      // s
    public double SpreadRadius { get; init; }
    public int Propagules { get; init; }
    public double SpreadMinBiomassFraction { get; init; }

    // competition
    public double CompetitionRadius { get; init; }
    public double CrowdingLimit { get; init; }
    public double CompetitionSensitivity { get; init; }

    public List<ProximityRule> Proximity { get; init; } = new();
    public double LitterFraction { get; init; }
    /// <summary>Fraction of living biomass shed as litter (detritus) per second while alive.</summary>
    public double SheddingRate { get; init; }
    public double GrazingValue { get; init; }

    // visual
    public string Shape { get; init; } = "";
    public double[] Color { get; init; } = { 0.3, 0.6, 0.2 };
    public double[] Color2 { get; init; } = { 0.3, 0.6, 0.2 };
    public double ColorVariance { get; init; }
    public double Height { get; init; }
    public List<string> Tags { get; init; } = new();
}

public enum FloraRelationType { Neutral, Compete, Benefit, Refuse }

public sealed class FloraRelation
{
    /// <summary>Species affected.</summary>
    public string A { get; init; } = "";
    /// <summary>Species causing the effect.</summary>
    public string B { get; init; } = "";
    public FloraRelationType Type { get; init; }
    public double Strength { get; init; }
    public double Radius { get; init; }
    public double Bonus { get; init; }
    public string Reason { get; init; } = "";
}

public sealed class FloraInteractionMatrix
{
    public double NeutralCompetition { get; init; } = 0.5;
    public double IntraspecificCompetition { get; init; } = 1.0;
    public List<FloraRelation> Relations { get; init; } = new();

    private Dictionary<(string, string), FloraRelation>? _index;

    public FloraRelation? Get(string a, string b)
    {
        _index ??= Relations.ToDictionary(r => (r.A, r.B));
        return _index.TryGetValue((a, b), out var r) ? r : null;
    }

    /// <summary>Competition weight of b's biomass on a.</summary>
    public double CompetitionWeight(string a, string b)
    {
        var r = Get(a, b);
        if (r != null && r.Type == FloraRelationType.Compete) return r.Strength;
        if (r != null && r.Type == FloraRelationType.Benefit) return 0;
        return a == b ? IntraspecificCompetition : NeutralCompetition;
    }
}

public sealed class DietEntry
{
    /// <summary>A resource id from ecology.json (detritus, biofilm, plankton) or "flora:&lt;archetype&gt;".</summary>
    public string Resource { get; init; } = "";
    public double RatePerSecond { get; init; }
    public double Efficiency { get; init; }
}

public sealed class SchoolingParams
{
    public double Radius { get; init; }
    public double Cohesion { get; init; }
    public double Alignment { get; init; }
    public double Separation { get; init; }
    public double SeparationDistance { get; init; }
}

public enum Medium { Terrestrial, Aquatic }

public sealed class FaunaSpeciesDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public Medium Medium { get; init; }
    public string Role { get; init; } = "";
    public string Description { get; init; } = "";
    public string SourceFile { get; init; } = "";

    // habitat
    public Pref Moisture { get; init; }
    public Dictionary<Substrate, double> SubstrateAffinity { get; init; } = new();
    public double MinWaterDepth { get; init; }
    public double MaxWaterDepth { get; init; }
    public double MinSuitability { get; init; }

    // movement
    public double Speed { get; init; }              // m/s (sim)
    public double TurnRate { get; init; }           // rad/s (sim)
    public double Wander { get; init; }
    public double HabitatSeek { get; init; }
    public double SenseRadius { get; init; }

    public List<DietEntry> Diet { get; init; } = new();

    // metabolism
    public double BasalRate { get; init; }          // energy/s
    public double MaxEnergy { get; init; } = 1;
    public double InitialEnergy { get; init; }
    public double HungerThreshold { get; init; }
    public double WasteFraction { get; init; }

    // lifecycle
    public double MaturityAge { get; init; }        // s
    public double Lifespan { get; init; }           // s
    public double LifespanVariance { get; init; }
    public double DailyMortality { get; init; }

    // reproduction
    public bool Sexual { get; init; }
    public double ReproMinEnergy { get; init; }
    public double ReproCost { get; init; }
    public int ClutchMin { get; init; }
    public int ClutchMax { get; init; }
    public double ReproCooldown { get; init; }      // s
    public double MateRadius { get; init; }
    public double OffspringEnergy { get; init; }
    public int MaxLocalDensity { get; init; }
    public int PopulationCap { get; init; }

    // body
    public double SizeMin { get; init; }            // m (true scale)
    public double SizeMax { get; init; }
    public double VisualScale { get; init; }
    public double MassAtMid { get; init; }          // detritus units at mid size
    public double DetritusOnDeath { get; init; }

    // genetics
    public List<string> Traits { get; init; } = new();
    public double MutationMagnitude { get; init; }
    public double InitialVariance { get; init; }

    // visual
    public string Model { get; init; } = "";
    public double[] BaseColor { get; init; } = { 0.8, 0.5, 0.3 };
    public double[] OrnamentColor { get; init; } = { 0.9, 0.9, 0.9 };

    public List<string> Behaviors { get; init; } = new();
    public SchoolingParams? Schooling { get; init; }

    public bool HasTrait(string id) => Traits.Contains(id);
    public int TraitIndex(string id) => Traits.IndexOf(id);
}

public sealed class TraitDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double Default { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public string Description { get; init; } = "";
    /// <summary>True when the trait changes simulation values (not only appearance).</summary>
    public bool SimulationRelevant { get; init; }
}

public sealed class GeneticsConfig
{
    /// <summary>Probability that an offspring undergoes a mutation event (default 0.10). See Genetics.Inheritance.</summary>
    public double MutationProbability { get; init; } = 0.10;
    public List<TraitDef> Traits { get; init; } = new();
    public TraitDef? Get(string id) => Traits.FirstOrDefault(t => t.Id == id);
}

public sealed class ResourceDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>"any", "aquatic" or "terrestrial": where the resource field holds values.</summary>
    public string Medium { get; init; } = "any";
}

public sealed class EcologyConfig
{
    public List<ResourceDef> Resources { get; init; } = new();

    public double NutrientBaseline { get; init; }
    public double NutrientMax { get; init; }
    public double NutrientRelax { get; init; }      // 1/s toward baseline
    public double NutrientDiffusion { get; init; }  // 1/s

    public double MoistureDrying { get; init; }     // 1/s toward dry baseline
    public double MoistureWetting { get; init; }    // 1/s toward 1 when wet
    public double MoistureDiffusion { get; init; }
    public double MoistureWaterTableRange { get; init; } // m above water table where capillary moisture fades
    public double MoistureDryBaseline { get; init; }

    public double DetritusDecay { get; init; }      // 1/s
    public double DetritusNutrientYield { get; init; }
    public double DetritusMax { get; init; }

    public double BiofilmGrowth { get; init; }      // 1/s
    public double BiofilmCapacity { get; init; }
    public double BiofilmNutrientUse { get; init; }
    public double PlanktonGrowth { get; init; }
    public double PlanktonCapacity { get; init; }
    public double PlanktonNutrientUse { get; init; }

    public bool HasResource(string id) => Resources.Any(r => r.Id == id);
}

public sealed class ToolConfig
{
    public double NutrientAmount { get; init; }
    public double NutrientRadius { get; init; }
    public double NutrientMinRadius { get; init; }
    public double NutrientMaxRadius { get; init; }
    public double PokeRadius { get; init; }
    public double PokeStrength { get; init; }
    public double PokeDisturbSeconds { get; init; }
    public double GrabHoldHeight { get; init; }
    public double RockMinScale { get; init; }
    public double RockMaxScale { get; init; }
    public double PropMinSpacing { get; init; }
    public double LogMinLength { get; init; }
    public double LogMaxLength { get; init; }
    public double LogMinRadius { get; init; }
    public double LogMaxRadius { get; init; }
    public double GravelMinRadius { get; init; }
    public double GravelMaxRadius { get; init; }
    public int IntroduceFaunaCount { get; init; }
    /// <summary>Sculpt speed at the brush centre (m per real second of brushing) and brush radius limits.</summary>
    public double SculptRate { get; init; }
    public double SculptMinRadius { get; init; }
    public double SculptMaxRadius { get; init; }
    /// <summary>Water tools: pour/soak rate (m³ per real second of brushing), radius limits, springs.</summary>
    public double PourRate { get; init; }
    public double DrainRate { get; init; }
    public double WaterMinRadius { get; init; }
    public double WaterMaxRadius { get; init; }
    /// <summary>New spring discharge in m³ per sim-second.</summary>
    public double SpringDischarge { get; init; }
    public int MaxSprings { get; init; }
}
