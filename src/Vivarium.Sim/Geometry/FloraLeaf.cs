using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only blade identity. Ranges refer to the compiled mesh, never simulation state.</summary>
public readonly record struct FloraLeaf(int FirstVertex, int VertexCount, int FirstIndex, int IndexCount,
    ulong Identity, Vec3 Attachment, double Length, int MaterialVariant, double Age, double Flexibility,
    bool Volumetric);

/// <summary>Screen-size tiers (projected bounding diameter, px) with hysteresis. 0 near (>= ~210 px),
/// 1 medium (down to ~75 px), 2 far. Every organism remains represented; secondary detail is thinned.</summary>
public static class FloraDetail
{
    public const double FullPixels = 210, MidPixels = 75;
    // Distance reduction tuned to the 8 m diorama: full detail through the near
    // interaction zone, mid by 3 m, far beyond the island's readable half-width.
    public const double DistanceMid = 3.0, DistanceFar = 5.5;

    public static int SelectDistance(double depth, int previous = -1, double distanceScale = 1) => previous switch
    {
        0 => depth > DistanceFar * distanceScale * 1.1 ? 2 : depth > DistanceMid * distanceScale * 1.1 ? 1 : 0,
        1 => depth < DistanceMid * distanceScale * .9 ? 0 : depth > DistanceFar * distanceScale * 1.1 ? 2 : 1,
        2 => depth < DistanceMid * distanceScale * .9 ? 0 : depth < DistanceFar * distanceScale * .9 ? 1 : 2,
        _ => depth >= DistanceFar * distanceScale ? 2 : depth >= DistanceMid * distanceScale ? 1 : 0,
    };

    public static int Select(double pixels, int previous = -1) => previous switch
    {
        0 => pixels < MidPixels * 0.8 ? 2 : pixels < FullPixels * 0.8 ? 1 : 0,
        1 => pixels >= FullPixels * 1.2 ? 0 : pixels < MidPixels * 0.8 ? 2 : 1,
        2 => pixels >= FullPixels * 1.2 ? 0 : pixels >= MidPixels * 1.2 ? 1 : 2,
        _ => pixels >= FullPixels ? 0 : pixels >= MidPixels ? 1 : 2,
    };
}
