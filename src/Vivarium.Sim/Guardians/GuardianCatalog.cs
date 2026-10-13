using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Authoritative catalog of the six Guardian Animal archetypes,
/// each paired 1:1 with an old-growth Pilot Tree form.
/// </summary>
public static class GuardianCatalog
{
    public const string CrestedCanopy = "hoatzin_crested";
    public const string ArmoredForest = "pangolin_armored";
    public const string CanopySerpent = "serpent_canopy";
    public const string GreatGround = "monitor_great";
    public const string Woodland = "coati_woodland";
    public const string QuilledArboreal = "porcupine_quilled";

    public static IReadOnlyList<GuardianDef> All { get; } = Array.AsReadOnly(new[]
    {
        // G01 — Crested canopy guardian (Hoatzin-inspired, associated with Crowncoil cycad)
        new GuardianDef(
            Id: CrestedCanopy,
            Name: "Crested Canopy Guardian",
            Archetype: "crested_canopy",
            AssociatedPilotTreeId: "crowncoil",
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
            Description: "Layered rust plumage, bright turquoise eye patch, erectile crest, and deliberate branch-climbing."
        ),

        // G02 — Armored forest guardian (Pangolin-inspired, associated with Gloomspire hemlock)
        new GuardianDef(
            Id: ArmoredForest,
            Name: "Armored Forest Guardian",
            Archetype: "armored_forest",
            AssociatedPilotTreeId: "gloomspire",
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
            Description: "Overlapping keratin plates, digging claws, heavy tail, and defensive curling reflex."
        ),

        // G03 — Canopy serpent guardian (Tree boa-inspired, associated with Needlevault spruce)
        new GuardianDef(
            Id: CanopySerpent,
            Name: "Canopy Serpent Guardian",
            Archetype: "canopy_serpent",
            AssociatedPilotTreeId: "needlevault",
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
            Description: "Muscular branch-wrapping coils, suspended resting loops, and slow deliberate thermal basking."
        ),

        // G04 — Great ground guardian (Asian water monitor-inspired, associated with Emberpillar redwood)
        new GuardianDef(
            Id: GreatGround,
            Name: "Great Ground Guardian",
            Archetype: "great_ground",
            AssociatedPilotTreeId: "emberpillar",
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
            Description: "Heavy sprawling walk, powerful lateral tail, probing forked tongue, and fallen log exploration."
        ),

        // G05 — Long-nosed woodland guardian (Coati-inspired, associated with Basinwarden broadleaf)
        new GuardianDef(
            Id: Woodland,
            Name: "Long-nosed Woodland Guardian",
            Archetype: "woodland_omnivore",
            AssociatedPilotTreeId: "basinwarden",
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
            Description: "Flexible investigative snout, agile climbing gait, ringed tail carried upright, and litter rooting."
        ),

        // G06 — Quilled arboreal guardian (Brazilian porcupine-inspired, associated with Palehollow conifer)
        new GuardianDef(
            Id: QuilledArboreal,
            Name: "Quilled Arboreal Guardian",
            Archetype: "quilled_arboreal",
            AssociatedPilotTreeId: "palehollow",
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
            Description: "Prehensile gripping tail, bicoloured defensive quills, deliberate climbing, and canopy rest."
        ),
    });

    public static GuardianDef? Find(string id) =>
        All.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(g.Archetype, id, StringComparison.OrdinalIgnoreCase));

    public static GuardianDef? ForPilotTree(string pilotTreeId) =>
        All.FirstOrDefault(g => string.Equals(g.AssociatedPilotTreeId, pilotTreeId, StringComparison.OrdinalIgnoreCase));
}

