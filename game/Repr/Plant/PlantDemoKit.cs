using System;
using System.IO;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;

namespace Vivarium.Repr;

/// <summary>Shared helpers for the P3 plant demos.</summary>
internal static class PlantDemoKit
{
    public static readonly string[] Species = { "glowbriar", "moonfern", "shadebell" };

    private sealed record Tuning
    {
        public GlowbriarGenerator.FormSettings Glowbriar { get; init; } = new();
        public MoonfernGenerator.FormSettings Moonfern { get; init; } = new();
        public ShadebellGenerator.FormSettings Shadebell { get; init; } = new();
        public RendererTuning Renderer { get; init; } = new();
    }
    private sealed record RendererTuning
    {
        public float ChunkMetres { get; init; } = 2;
        public float Detail0Pixels { get; init; } = 700;
        public float Detail1Pixels { get; init; } = 250;
    }
    private static readonly Lazy<Tuning> Configuration = new(() =>
        Main.ArgAfter("--plant-config") is { } path
            ? JsonSerializer.Deserialize<Tuning>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive=true })
                ?? throw new InvalidDataException("Empty plant configuration") : new Tuning());

    private static readonly Lazy<GlowbriarGenerator> Briar = new(() => new(Configuration.Value.Glowbriar));
    private static readonly Lazy<MoonfernGenerator> Fern = new(() => new(Configuration.Value.Moonfern));
    private static readonly Lazy<ShadebellGenerator> Shrub = new(() => new(Configuration.Value.Shadebell));

    public static PlanGenerator Generator(string species) => species switch
    {
        "glowbriar" => Briar.Value,
        "moonfern" => Fern.Value,
        _ => Shrub.Value,
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
        var tune = Configuration.Value.Renderer;
        return new PlantAssemblyRenderer { Name = name, MaterialProvider = ctx.LegacyFloraMaterial,
            CellSize = tune.ChunkMetres, Detail0Px = tune.Detail0Pixels, Detail1Px = tune.Detail1Pixels };
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
