using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Traits of one attached surface-colony species. Pure data; all responses are evaluated by ColonySystem.
public sealed record ColonySpecies(
    string Id,
    ColonyKind Kind,
    double MaxThickness,          // m
    double MaxBiomass,            // kg dry / m2 of covered ground (intensity cap)
    double GrowthRate,            // 1/day at ideal conditions
    double SpreadRate,            // m/day front advance at ideal conditions
    double MoistureOptimum,       // moisture 0..1 at which growth is ideal
    double MoistureTolerance,     // gaussian sigma of the moisture response
    double DesiccationToleranceDays, // days of dryness beyond which a cell goes dormant
    double LightOptimum,          // 0..1
    double LightTolerance,        // gaussian sigma
    double AffinityRock,          // substrate affinity weights (0..1)
    double AffinitySoil,
    double AffinityHumus,
    double AffinityLitter,
    double AffinitySand,
    double AffinityWood,
    double SlopeTolerance,        // steepness 0..1 where response halves
    double CompetitiveHeight,     // m; taller overgrows shorter
    double WaterHolding,          // m water per m thickness
    Vec3 ColourDry,               // linear rgb
    Vec3 ColourWet)
{
    /// Substrate affinity for a surface: class weights, times a capacity multiplier.
    /// The multiplier is min(1, capacity / 0.5): the substrate gives litter-covered soil only ~0.15, and a plain
    /// product would leave every soil surface unviable for moss (accepted deviation, see ColonySystem docs).
    public double SubstrateResponse(Horizon horizon, bool sandy, double capacity)
    {
        double rock = horizon == Horizon.Bedrock ? 1 : 0;
        double humus = horizon == Horizon.Humus ? 1 : 0;
        double litter = horizon == Horizon.Litter ? 1 : 0;
        double soil = (horizon == Horizon.MineralSoil || horizon == Horizon.Weathered) ? 1 : 0;
        double sand = sandy && soil > 0 ? 1 : 0;
        double wood = 0; // no wood surfaces in the substrate yet
        double[] weights = { AffinityRock, AffinitySoil, AffinityHumus, AffinityLitter, AffinitySand, AffinityWood };
        double[] classes = { rock, soil, humus, litter, sand, wood };
        double w = 0, sum = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            w += weights[i] * classes[i];
            sum += classes[i];
        }
        double cls = sum > 0 ? w / sum : 0;
        return MathD.Clamp01(cls * Math.Min(1.0, capacity / 0.5));
    }

    public static ColonySpecies CushionMoss => new(
        "cushion_moss", ColonyKind.Moss, MaxThickness: 0.03, MaxBiomass: 0.9, GrowthRate: 0.08, SpreadRate: 0.004,
        MoistureOptimum: 0.8, MoistureTolerance: 0.25, DesiccationToleranceDays: 6,
        LightOptimum: 0.25, LightTolerance: 0.3,
        AffinityRock: 0.6, AffinitySoil: 0.2, AffinityHumus: 1.0, AffinityLitter: 1.0, AffinitySand: 0.0, AffinityWood: 0.7,
        SlopeTolerance: 0.6, CompetitiveHeight: 0.03, WaterHolding: 2.5,
        ColourDry: new Vec3(0.20, 0.26, 0.10), ColourWet: new Vec3(0.05, 0.22, 0.04));

    public static ColonySpecies FeatherMoss => new(
        "feather_moss", ColonyKind.Moss, MaxThickness: 0.012, MaxBiomass: 0.45, GrowthRate: 0.10, SpreadRate: 0.009,
        MoistureOptimum: 0.9, MoistureTolerance: 0.2, DesiccationToleranceDays: 4,
        LightOptimum: 0.15, LightTolerance: 0.25,
        AffinityRock: 0.3, AffinitySoil: 0.7, AffinityHumus: 1.0, AffinityLitter: 1.0, AffinitySand: 0.0, AffinityWood: 0.5,
        SlopeTolerance: 0.4, CompetitiveHeight: 0.012, WaterHolding: 2.0,
        ColourDry: new Vec3(0.30, 0.32, 0.12), ColourWet: new Vec3(0.08, 0.30, 0.07));

    public static ColonySpecies CrustLichen => new(
        "crust_lichen", ColonyKind.CrustoseLichen, MaxThickness: 0.002, MaxBiomass: 0.12, GrowthRate: 0.015, SpreadRate: 0.0008,
        MoistureOptimum: 0.25, MoistureTolerance: 0.35, DesiccationToleranceDays: 60,
        LightOptimum: 0.9, LightTolerance: 0.3,
        AffinityRock: 1.0, AffinitySoil: 0.0, AffinityHumus: 0.0, AffinityLitter: 0.0, AffinitySand: 0.0, AffinityWood: 0.0,
        SlopeTolerance: 0.9, CompetitiveHeight: 0.002, WaterHolding: 0.3,
        ColourDry: new Vec3(0.55, 0.42, 0.25), ColourWet: new Vec3(0.40, 0.33, 0.20));

    public static ColonySpecies LeafLichen => new(
        "leaf_lichen", ColonyKind.FolioseLichen, MaxThickness: 0.006, MaxBiomass: 0.2, GrowthRate: 0.02, SpreadRate: 0.0015,
        MoistureOptimum: 0.5, MoistureTolerance: 0.3, DesiccationToleranceDays: 30,
        LightOptimum: 0.7, LightTolerance: 0.3,
        AffinityRock: 0.9, AffinitySoil: 0.1, AffinityHumus: 0.0, AffinityLitter: 0.0, AffinitySand: 0.0, AffinityWood: 0.9,
        SlopeTolerance: 0.7, CompetitiveHeight: 0.006, WaterHolding: 0.8,
        ColourDry: new Vec3(0.45, 0.50, 0.30), ColourWet: new Vec3(0.30, 0.42, 0.22));

    /// Not alive: deposited, decomposes. Kind Litter; its growth and spread are unused.
    public static ColonySpecies LeafLitter => new(
        "leaf_litter", ColonyKind.Litter, MaxThickness: 0.02, MaxBiomass: 1.5, GrowthRate: 0, SpreadRate: 0,
        MoistureOptimum: 0.5, MoistureTolerance: 1, DesiccationToleranceDays: 1e9,
        LightOptimum: 0.5, LightTolerance: 1,
        AffinityRock: 0, AffinitySoil: 0.3, AffinityHumus: 0.5, AffinityLitter: 1.0, AffinitySand: 0.2, AffinityWood: 0,
        SlopeTolerance: 0.5, CompetitiveHeight: 0.02, WaterHolding: 3.0,
        ColourDry: new Vec3(0.45, 0.30, 0.12), ColourWet: new Vec3(0.25, 0.15, 0.07));

    public static IReadOnlyList<ColonySpecies> Defaults { get; } = new[]
    {
        CushionMoss, FeatherMoss, CrustLichen, LeafLichen, LeafLitter,
    };

    public static ColonySpecies? Find(string id)
    {
        foreach (var s in Defaults) if (s.Id == id) return s;
        return null;
    }
}
