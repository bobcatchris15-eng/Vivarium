using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Authored identity of a graph form; color and random seed are not silhouette definitions.</summary>
public sealed record GraphSpeciesInfo(string Id, string Family, string Silhouette, string GrowthHabit, bool AttachedSurface = false);

/// <summary>Copied live support segment, in plant-local coordinates, for climbing graph construction.</summary>
public readonly record struct GraphAxisSegment(Vec3 Start, Vec3 End, Vec3 Normal, int Parent, bool Attached);
