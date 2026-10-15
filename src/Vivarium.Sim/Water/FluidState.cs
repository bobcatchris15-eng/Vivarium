using Vivarium.Sim.Core;
using System.Text.Json.Serialization;
namespace Vivarium.Sim.Water;

// Shared parcel value and historical v3 archive DTO. Only SurfaceFluidDynamics advances mobile water.
public readonly record struct FluidParticle(Vec3 Position, Vec3 Velocity, double Volume, ulong Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] double Width = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] Vec3 WidthAxis = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ulong Stream = 0);
public sealed record FluidState(FluidParticle[] Particles, Dictionary<ulong, double> SourceRemainders, double Time, double Remainder, Dictionary<ulong, long>? EmissionPhases = null);
