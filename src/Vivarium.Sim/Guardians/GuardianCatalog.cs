using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Authoritative catalog of playable Guardian Animal archetypes.
/// The user chooses the guardian independently of the Pilot Tree.
/// </summary>
public static class GuardianCatalog
{
    public const string CrestedCanopy = "hoatzin_crested";
    public const string ArmoredForest = "pangolin_armored";
    public const string CanopySerpent = "serpent_canopy";
    public const string GreatGround = "monitor_great";
    public const string Woodland = "coati_woodland";
    public const string QuilledArboreal = "porcupine_quilled";
    public const string Fernback = "thylacine_fernback";
    public const string Dewhorn = "saiga_dewhorn";
    public const string Hushbill = "shoebill_hushbill";
    public const string Ruffleback = "matamata_ruffleback";

    public static IReadOnlyList<GuardianDef> All { get; } = Array.AsReadOnly(new[]
    {
        // G01 — Crested canopy guardian (Hoatzin-inspired)
        new GuardianDef(
            Id: CrestedCanopy,
            Name: "Crested Canopy Guardian",
            Archetype: "crested_canopy",
            Locomotion: LocomotionMode.AvianPerchHop,
            AdultMass: 1.2,
            VisualScale: 1.0,
            WalkSpeed: 0.20,
            RunSpeed: 0.36,
            EnergyMax: 100.0,
            BaseMetabolismRate: 0.015,
            PreferredDiets: Array.AsReadOnly(new[] { "fruit", "flora:foliage", "seeds" }),
            BaseColor: new Vec3(0.55, 0.38, 0.18),
            SecondaryColor: new Vec3(0.12, 0.45, 0.65),
            Description: "Layered rust plumage, bright turquoise eye patch, erectile crest, and deliberate branch-climbing.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 1.1,
                DrinkFrequency: 0.9,
                PlayThreshold: 0.65,
                BaskPriority: 0.4,
                DigAbility: 0.1,
                CuriosityBias: 0.9,
                NocturnalBias: 0.2,
                CanSwim: false,
                CanClimbTrunk: true,
                PreferredRestType: "tree_perch"
            )
        ),

        // G02 — Armored forest guardian (Pangolin-inspired)
        new GuardianDef(
            Id: ArmoredForest,
            Name: "Armored Forest Guardian",
            Archetype: "armored_forest",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 12.0,
            VisualScale: 1.05,
            WalkSpeed: 0.21,
            RunSpeed: 0.378,
            EnergyMax: 150.0,
            BaseMetabolismRate: 0.012,
            PreferredDiets: Array.AsReadOnly(new[] { "detritus", "litter", "biofilm" }),
            BaseColor: new Vec3(0.48, 0.38, 0.28),
            SecondaryColor: new Vec3(0.72, 0.58, 0.36),
            Description: "Overlapping keratin plates, digging claws, heavy tail, and defensive curling reflex.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 1.0,
                DrinkFrequency: 0.7,
                PlayThreshold: 0.75,
                BaskPriority: 0.5,
                DigAbility: 1.8,
                CuriosityBias: 0.4,
                NocturnalBias: 0.8,
                CanSwim: false,
                CanClimbTrunk: false,
                PreferredRestType: "ball"
            )
        ),

        // G03 — Canopy serpent guardian (Tree boa-inspired)
        new GuardianDef(
            Id: CanopySerpent,
            Name: "Canopy Serpent Guardian",
            Archetype: "canopy_serpent",
            Locomotion: LocomotionMode.Serpentine,
            AdultMass: 6.5,
            VisualScale: 1.1,
            WalkSpeed: 0.4402232,
            RunSpeed: 0.79240176,
            EnergyMax: 140.0,
            BaseMetabolismRate: 0.008,
            PreferredDiets: Array.AsReadOnly(new[] { "fauna:small", "detritus" }),
            BaseColor: new Vec3(0.32, 0.42, 0.24),
            SecondaryColor: new Vec3(0.70, 0.55, 0.18),
            Description: "Muscular branch-wrapping coils, suspended resting loops, and slow deliberate thermal basking.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 0.8,
                DrinkFrequency: 0.6,
                PlayThreshold: 0.80,
                BaskPriority: 1.8,
                DigAbility: 0.0,
                CuriosityBias: 0.6,
                NocturnalBias: 0.3,
                CanSwim: true,
                CanClimbTrunk: true,
                PreferredRestType: "coil"
            )
        ),

        // G04 — Great ground guardian (Asian water monitor-inspired)
        new GuardianDef(
            Id: GreatGround,
            Name: "Great Ground Guardian",
            Archetype: "great_ground",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 25.0,
            VisualScale: 1.25,
            WalkSpeed: 0.5769231,
            RunSpeed: 1.03846158,
            EnergyMax: 200.0,
            BaseMetabolismRate: 0.014,
            PreferredDiets: Array.AsReadOnly(new[] { "fruit", "detritus", "fauna:small" }),
            BaseColor: new Vec3(0.24, 0.26, 0.22),
            SecondaryColor: new Vec3(0.65, 0.62, 0.45),
            Description: "Heavy sprawling walk, powerful lateral tail, probing forked tongue, and fallen log exploration.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 1.2,
                DrinkFrequency: 1.3,
                PlayThreshold: 0.50,
                BaskPriority: 1.5,
                DigAbility: 0.6,
                CuriosityBias: 0.8,
                NocturnalBias: 0.1,
                CanSwim: true,
                CanClimbTrunk: false,
                PreferredRestType: "ground_curl"
            )
        ),

        // G05 — Long-nosed woodland guardian (Coati-inspired)
        new GuardianDef(
            Id: Woodland,
            Name: "Long-nosed Woodland Guardian",
            Archetype: "woodland_omnivore",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 5.5,
            VisualScale: 0.95,
            WalkSpeed: 0.4676923,
            RunSpeed: 0.84184614,
            EnergyMax: 120.0,
            BaseMetabolismRate: 0.022,
            PreferredDiets: Array.AsReadOnly(new[] { "fruit", "seeds", "litter", "detritus" }),
            BaseColor: new Vec3(0.50, 0.32, 0.20),
            SecondaryColor: new Vec3(0.85, 0.78, 0.65),
            Description: "Flexible investigative snout, agile climbing gait, ringed tail carried upright, and litter rooting.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 1.3,
                DrinkFrequency: 1.1,
                PlayThreshold: 0.45,
                BaskPriority: 0.3,
                DigAbility: 1.4,
                CuriosityBias: 1.6,
                NocturnalBias: 0.3,
                CanSwim: true,
                CanClimbTrunk: true,
                PreferredRestType: "ground_curl"
            )
        ),

        // G06 — Quilled arboreal guardian (Brazilian porcupine-inspired)
        new GuardianDef(
            Id: QuilledArboreal,
            Name: "Quilled Arboreal Guardian",
            Archetype: "quilled_arboreal",
            Locomotion: LocomotionMode.ArborealClimber,
            AdultMass: 4.8,
            VisualScale: 0.9,
            WalkSpeed: 0.108,
            RunSpeed: 0.1944,
            EnergyMax: 110.0,
            BaseMetabolismRate: 0.011,
            PreferredDiets: Array.AsReadOnly(new[] { "fruit", "flora:bark", "flora:fungus" }),
            BaseColor: new Vec3(0.28, 0.24, 0.20),
            SecondaryColor: new Vec3(0.85, 0.82, 0.68),
            Description: "Prehensile gripping tail, bicoloured defensive quills, deliberate climbing, and canopy rest.",
            BehaviorProfile: new GuardianBehaviorProfile(
                ForageWeight: 0.9,
                DrinkFrequency: 0.8,
                PlayThreshold: 0.70,
                BaskPriority: 0.4,
                DigAbility: 0.2,
                CuriosityBias: 0.5,
                NocturnalBias: 0.7,
                CanSwim: false,
                CanClimbTrunk: true,
                PreferredRestType: "fork_wedge"
            )
        ),
        new GuardianDef(
            Id: Fernback, Name: "Fernback (Thylacine)", Archetype: "fernback",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 22.0, VisualScale: 1.0, WalkSpeed: 0.46, RunSpeed: 0.83,
            EnergyMax: 160.0, BaseMetabolismRate: 0.017,
            PreferredDiets: Array.AsReadOnly(new[] { "fauna:small", "detritus" }),
            BaseColor: new Vec3(0.51, 0.39, 0.28), SecondaryColor: new Vec3(0.15, 0.12, 0.10),
            Description: "A watchful thylacine with rounded ears, a heavy tapering tail, and fern-branching hindquarter stripes.",
            BehaviorProfile: new GuardianBehaviorProfile(ForageWeight: 1.1, DrinkFrequency: 0.9,
                PlayThreshold: 0.65, BaskPriority: 0.4, DigAbility: 0.3, CuriosityBias: 1.2,
                NocturnalBias: 0.7, CanSwim: true, CanClimbTrunk: false)),
        new GuardianDef(
            Id: Dewhorn, Name: "Dewhorn (Saiga)", Archetype: "dewhorn",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 30.0, VisualScale: 1.0, WalkSpeed: 0.40, RunSpeed: 0.90,
            EnergyMax: 170.0, BaseMetabolismRate: 0.016,
            PreferredDiets: Array.AsReadOnly(new[] { "flora:foliage", "seeds", "fruit" }),
            BaseColor: new Vec3(0.67, 0.53, 0.34), SecondaryColor: new Vec3(0.88, 0.75, 0.48),
            Description: "A soft-nosed saiga with slender legs and amber horns that curl inward to catch dew.",
            BehaviorProfile: new GuardianBehaviorProfile(ForageWeight: 1.3, DrinkFrequency: 1.1,
                PlayThreshold: 0.5, BaskPriority: 0.3, DigAbility: 0.2, CuriosityBias: 1.0,
                NocturnalBias: 0.2, CanSwim: false, CanClimbTrunk: false)),
        new GuardianDef(
            Id: Hushbill, Name: "Hushbill (Shoebill)", Archetype: "hushbill",
            Locomotion: LocomotionMode.AvianPerchHop,
            AdultMass: 5.5, VisualScale: 1.0, WalkSpeed: 0.18, RunSpeed: 0.34,
            EnergyMax: 130.0, BaseMetabolismRate: 0.012,
            PreferredDiets: Array.AsReadOnly(new[] { "fauna:small", "detritus" }),
            BaseColor: new Vec3(0.38, 0.44, 0.48), SecondaryColor: new Vec3(0.72, 0.66, 0.43),
            Description: "A tall, still shoebill with a sculptural bill and two curling question-mark crest feathers.",
            BehaviorProfile: new GuardianBehaviorProfile(ForageWeight: 0.9, DrinkFrequency: 1.3,
                PlayThreshold: 0.85, BaskPriority: 0.5, DigAbility: 0.0, CuriosityBias: 0.5,
                NocturnalBias: 0.2, CanSwim: true, CanClimbTrunk: false)),
        new GuardianDef(
            Id: Ruffleback, Name: "Ruffleback (Mata mata)", Archetype: "ruffleback",
            Locomotion: LocomotionMode.TerrestrialQuadruped,
            AdultMass: 12.0, VisualScale: 1.0, WalkSpeed: 0.11, RunSpeed: 0.20,
            EnergyMax: 140.0, BaseMetabolismRate: 0.007,
            PreferredDiets: Array.AsReadOnly(new[] { "fauna:small", "detritus", "biofilm" }),
            BaseColor: new Vec3(0.32, 0.28, 0.20), SecondaryColor: new Vec3(0.59, 0.46, 0.30),
            Description: "A flat, rugged mata mata turtle with ribbon-curled neck fringes and a patient snorkelling nose.",
            BehaviorProfile: new GuardianBehaviorProfile(ForageWeight: 0.8, DrinkFrequency: 1.4,
                PlayThreshold: 0.9, BaskPriority: 1.5, DigAbility: 0.1, CuriosityBias: 0.4,
                NocturnalBias: 0.5, CanSwim: true, CanClimbTrunk: false)),
    });

    public static GuardianDef? Find(string id) =>
        All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(g.Archetype, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Evaluates which guardian form a descriptor will spawn.
    /// Supports "random", "none", and a specific archetype/id.
    /// Legacy "auto" and omitted selections use the same tree-independent random stream.
    /// </summary>
    public static string? SeededGuardianFormId(WorldDescriptor descriptor)
    {
        if (descriptor.GuardianId == "none") return null;
        if (descriptor.GuardianId is "random" or "auto" or "" or null)
        {
            var rng = Rng.Stream(descriptor.Seed, "world.guardian");
            return All[rng.NextInt(All.Count)].Id;
        }
        return Find(descriptor.GuardianId)?.Id;
    }
}

