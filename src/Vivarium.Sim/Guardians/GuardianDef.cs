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
    Rest,
    Forage,
    Explore,
    Investigate,
    Climb,
    Feed,
    Groom,
    Retreat,
    ReturnHome
}

/// <summary>
/// Immutable definition of a Guardian Animal archetype.
/// Defines biological parameters, locomotion styles, dietary preferences, and Pilot Tree affinity.
/// </summary>
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
    string Description
);
