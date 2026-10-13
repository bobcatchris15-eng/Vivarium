using System.Collections.Generic;
using System.Text.Json.Serialization;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Guardians;

public sealed class GuardianPendingSeed
{
    public string SpeciesId { get; set; } = "";
    public double ViableMass { get; set; }
    public double BioSecondsUntilDeposit { get; set; }
}

/// <summary>
/// Authoritative individual state of an island's Guardian Animal.
/// Contains pure simulation values, persisted across saves and verified by determinism digests.
/// Contains no rendering references.
/// </summary>
public sealed class GuardianIndividual
{
    public EntityId Id { get; set; }
    public string ArchetypeId { get; set; } = "";
    public string HomeTreeId { get; set; } = "";
    public ulong Seed { get; set; }
    public double Scale { get; set; } = 1.0;
    public string Temperament { get; set; } = "inquisitive";
    public string VisualVariant { get; set; } = "default";

    // Physiology
    public double Energy { get; set; }
    public double Hydration { get; set; } = 1.0;
    public double Fatigue { get; set; }

    // Behavior & Activity
    public GuardianActivity Activity { get; set; } = GuardianActivity.Rest;
    public double ActivityDuration { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public string HomeAttachmentId { get; set; } = "trunk_base";

    // Spatial Position & Orientation
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Heading { get; set; } // radians in XZ
    public double Pitch { get; set; }
    public LocomotionMode Locomotion { get; set; }

    // Digestion / seed dispersal queue
    public List<GuardianPendingSeed> PendingSeeds { get; set; } = new();

    // Simulation metadata
    public double LastDecisionBioSeconds { get; set; }
    public ulong RngCounter { get; set; }

    [JsonIgnore]
    public Vec3 Position
    {
        get => new(X, Y, Z);
        set { X = value.X; Y = value.Y; Z = value.Z; }
    }

    [JsonIgnore]
    public Vec2 PositionXZ => new(X, Z);

    [JsonIgnore]
    public Vec3 TargetPosition
    {
        get => new(TargetX, TargetY, TargetZ);
        set { TargetX = value.X; TargetY = value.Y; TargetZ = value.Z; }
    }
}
