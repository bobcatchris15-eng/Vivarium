using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only blade identity. Ranges refer to the compiled mesh, never simulation state.</summary>
public readonly record struct FloraLeaf(int FirstVertex, int VertexCount, int FirstIndex, int IndexCount,
    ulong Identity, Vec3 Attachment, double Length, int MaterialVariant, double Age, double Flexibility,
    bool Volumetric);

/// <summary>Screen-size tiers (projected bounding diameter, px) with hysteresis. 0 near (>= ~120 px),
/// 1 medium (down to ~40 px), 2 far. Every tier is a full parametric mesh; nothing is ever skipped.</summary>
public static class FloraDetail
{
    public const double FullPixels = 120, MidPixels = 40;
    // Optional distance reduction shares its first boundary with the camera's blur onset.
    public const double DistanceMid = 6, DistanceFar = 12;

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
