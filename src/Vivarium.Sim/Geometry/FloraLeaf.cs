using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only blade identity. Ranges refer to the compiled mesh, never simulation state.</summary>
public readonly record struct FloraLeaf(int FirstVertex, int VertexCount, int FirstIndex, int IndexCount,
    ulong Identity, Vec3 Attachment, double Length, int MaterialVariant, double Age, double Flexibility,
    bool Volumetric);

/// <summary>Screen-size tiers with hysteresis. 0 near, 1 medium, 2 far.</summary>
public static class FloraDetail
{
    public static int Select(double pixels, int previous = -1) => previous switch
    {
        0 => pixels < 96 ? 2 : pixels < 256 ? 1 : 0,
        1 => pixels > 384 ? 0 : pixels < 96 ? 2 : 1,
        2 => pixels > 384 ? 0 : pixels > 144 ? 1 : 2,
        _ => pixels >= 320 ? 0 : pixels >= 120 ? 1 : 2,
    };
}
