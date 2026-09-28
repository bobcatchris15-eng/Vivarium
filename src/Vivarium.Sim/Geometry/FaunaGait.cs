using Vivarium.Sim.Content;

namespace Vivarium.Sim.Geometry;

/// <summary>Presentation-only distance clock. No smoothed velocity or cadence ceiling can lose travelled distance.</summary>
public static class FaunaGait
{
    public static bool GroundSteps(FaunaAnimationDef a) => a.Family is FaunaAnimationFamily.Walk
        or FaunaAnimationFamily.Metachronal or FaunaAnimationFamily.Sprawl or FaunaAnimationFamily.Hop;

    public static double Advance(FaunaAnimationDef a, double distance, double bodyLength, double seconds)
        => Math.Max(0, distance) / Math.Max(1e-4, bodyLength) * a.CyclesPerBody
            + (GroundSteps(a) ? 0 : Math.Min(a.IdleHz, a.MaxHz) * Math.Max(0, seconds));

    /// <summary>Half of the fore/aft foot stroke: its stance velocity cancels translation exactly.</summary>
    public static double HalfStroke(FaunaAnimationDef a) => a.DutyFactor / (2 * Math.Max(0.01, a.CyclesPerBody));
}
