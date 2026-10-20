namespace Vivarium.Sim.Guardians;

/// <summary>
/// Personality and drive weights for a Guardian Animal archetype.
/// Governs utility-based behavior selection, interaction tendencies, and environmental preferences.
/// </summary>
public sealed record GuardianBehaviorProfile(
    double ForageWeight = 1.0,       // Urgency when hungry
    double DrinkFrequency = 1.0,     // Frequency of drinking when thirsty
    double PlayThreshold = 0.6,      // Arousal threshold to trigger play when basic needs are satisfied
    double BaskPriority = 0.5,       // Urgency for ectothermic warmth seeking
    double DigAbility = 0.5,         // Efficiency/preference for substrate rooting/digging
    double CuriosityBias = 1.0,      // Tendency to investigate novel environmental stimuli
    double NocturnalBias = 0.5,      // Activity timing bias (0 = diurnal, 1 = nocturnal)
    bool CanSwim = true,             // Can enter shallow wading / water areas
    bool CanClimbTrunk = false,      // Vertical trunk climbing ability
    string PreferredRestType = "ground_curl" // "ground_curl", "tree_perch", "coil", "ball", "fork_wedge"
);
