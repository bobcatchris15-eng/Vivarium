using System;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;

namespace Vivarium.Repr;

/// <summary>Shared helpers for the P3 plant demos.</summary>
internal static class PlantDemoKit
{
    public static readonly string[] Species = { "glowbriar", "moonfern", "shadebell" };

    public static PlanGenerator Generator(string species) => species switch
    {
        "glowbriar" => new GlowbriarGenerator(),
        "moonfern" => new MoonfernGenerator(),
        _ => new ShadebellGenerator(),
    };

    public static PlantDescriptor Make(ulong worldSeed, string species, ulong plantId, double age, double vigour, Vector3 pos, double yaw = 0, double scale = 1)
    {
        var g = Generator(species);
        var p = g.Create(worldSeed, plantId);
        g.Grow(p, age, vigour, null);
        p.Position = new Vec3(pos.X, pos.Y, pos.Z);
        p.Yaw = yaw;
        p.Scale = scale;
        return p;
    }

    public static PlantAssemblyRenderer NewRenderer(ReprDemoContext ctx, string name)
    {
        return new PlantAssemblyRenderer { Name = name, MaterialProvider = ctx.LegacyFloraMaterial };
    }

    public static MeshInstance3D Ground(Vector3 centre, float size)
    {
        var matte = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.52f, 0.46f), Roughness = 1f, Metallic = 0f };
        return new MeshInstance3D { Name = "Ground", Position = centre, Mesh = new PlaneMesh { Size = new Vector2(size, size) }, MaterialOverride = matte };
    }

    public static float HeightOf(PlantDescriptor p)
    {
        double h = p.Height;
        if (h < 0.01) foreach (var o in p.Organs) h = Math.Max(h, Math.Max(o.Start.Y, o.End.Y));
        return (float)h;
    }

    public static float CrownOf(PlantDescriptor p)
    {
        double r = p.CrownRadius;
        if (r < 0.01) foreach (var o in p.Organs) r = Math.Max(r, Math.Max(Math.Sqrt(o.End.X * o.End.X + o.End.Z * o.End.Z), Math.Sqrt(o.Start.X * o.Start.X + o.Start.Z * o.Start.Z)));
        return (float)r;
    }
}
