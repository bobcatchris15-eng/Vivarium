using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public static class TestUtil
{
    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Vivarium.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    });

    public static string RepoRoot => Root.Value;
    public static string ContentDir => Path.Combine(RepoRoot, "game", "content");
    public static DirectoryContentSource ContentSource => new(ContentDir);

    private static readonly Lazy<ContentLibrary> ContentLazy = new(() => ContentLoader.Load(ContentSource));
    public static ContentLibrary Content => ContentLazy.Value;

    public static WorldDescriptor Default => Content.PresetOrThrow("default");

    /// <summary>Full default world (props, water settled, starters).</summary>
    /// <summary>
    /// The default preset. Biology runs at 1× unless <paramref name="bio"/> is given, so rate-based expectations
    /// stay in plain simulated time; soak tests pass the shipped default acceleration.
    /// </summary>
    public static VivariumWorld DefaultWorld(ulong? seed = null, bool populate = true, double bio = 1.0)
    {
        var d = Default;
        if (seed.HasValue) d.Seed = seed.Value;
        d.BioAcceleration = bio;
        return VivariumWorld.Create(Content, d, populate);
    }

    /// <summary>
    /// Small, flat, lifeless fixture world for isolated subsystem tests: 10 m island, no props, no springs,
    /// water table far below ground, gentle relief.
    /// </summary>
    public static WorldDescriptor FlatDescriptor(ulong seed = 42)
    {
        return new WorldDescriptor
        {
            PresetId = "fixture", Name = "Fixture", Seed = seed, Diameter = 10, CellSize = 0.25,
            Terrain = new TerrainProfile { BaseHeight = 0.5, Relief = 0.0, NoiseScale = 0.2, Octaves = 1, MinHeight = -1, MaxHeight = 2, Bottom = -2, RockExposure = 0 },
            Water = new WaterConfig { WaterTable = -0.8, FlowRate = 0.2, Evaporation = 0, Infiltration = 0, WetDepth = 0.008, BoundaryDrop = 0.3, SubSteps = 2 },
            Placement = new PlacementProfile { Rocks = 0, Logs = 0, GravelPatches = 0 },
            BioAcceleration = 1,
        };
    }

    public static VivariumWorld FlatWorld(ulong seed = 42, Action<WorldDescriptor>? edit = null)
    {
        var d = FlatDescriptor(seed);
        edit?.Invoke(d);
        return VivariumWorld.Create(Content, d, populate: false);
    }

    public static string Digest(VivariumWorld w) => Persistence.WorldSerializer.Digest(w);

    /// <summary>The shipped biological acceleration and the number of ticks in one biological day at it.</summary>
    public const double ShippedBio = WorldDescriptor.DefaultBioAcceleration;
    public static long TicksPerBioDay(double bio = ShippedBio) => (long)Math.Round(8640 / bio);

    public static string TempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "vivarium-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Sets moisture/nutrients/light uniformly (fixture conditioning).</summary>
    public static void Condition(VivariumWorld w, double moisture, double nutrients, double? light = null)
    {
        foreach (int c in w.Grid.DomainCells)
        {
            w.Fields.Moisture[c] = moisture;
            w.Fields.Nutrients[c] = nutrients;
            if (light.HasValue) w.Fields.Light[c] = light.Value;
        }
    }

    /// <summary>Floods a disc to the given depth (fixture ponds).</summary>
    public static void Flood(VivariumWorld w, Vec2 centre, double radius, double depth)
    {
        foreach (int c in w.Grid.CellsInRadius(centre, radius)) w.Water.Depth[c] = depth;
    }
}
