using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Guardians;

public enum LocomotionMode
{
    TerrestrialQuadruped,
    ArborealClimber,
    Serpentine,
    AvianPerchHop
}

public enum GuardianActivity
{
    // Core locomotion & foraging
    Rest,
    Forage,
    Explore,
    Investigate,
    Climb,
    Feed,
    Groom,
    Retreat,
    ReturnHome,

    // Core needs & rest cycle
    Drink,
    Sleep,
    Settle,
    Stretch,

    // Comfort & thermoregulation
    Bathe,
    ShakeOff,
    Bask,
    Dig,

    // Enrichment & social
    Play,
    ScentMark,
    Alert,
    Yawn
}

/// <summary>
/// Immutable definition of a Guardian Animal archetype.
/// Defines biological parameters, locomotion styles, dietary preferences, and Pilot Tree affinity.
/// </summary>
/// <param name="BaseMetabolismRate">Energy spent per visible second at default world speed
/// and biological acceleration 1. Faster biology multiplies energy demand; digestion uses biological seconds.</param>
public sealed record GuardianDef(
    string Id,
    string Name,
    string Archetype,
    string AssociatedPilotTreeId,
    LocomotionMode Locomotion,
    double AdultMass,
    double VisualScale,
    double WalkSpeed,
    double RunSpeed,
    double EnergyMax,
    double BaseMetabolismRate,
    IReadOnlyList<string> PreferredDiets,
    Vec3 BaseColor,
    Vec3 SecondaryColor,
    string Description,
    GuardianBehaviorProfile? BehaviorProfile = null
)
{
    // Shared by presentation and travel so existing saves shrink without changing
    // their persisted individual variation or doubling their apparent stride speed.
    public const double BodySizeMultiplier = 0.5;
}
