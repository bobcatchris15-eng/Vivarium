using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vivarium.Sim.World;

/// <summary>
/// Complete, serializable set of world-generation parameters. Together with the content library it
/// is sufficient to reconstruct the procedural baseline state exactly.
/// </summary>
public sealed class WorldDescriptor
{
    public string PresetId { get; set; } = "default";
    public string Name { get; set; } = "Vivarium";
    public ulong Seed { get; set; } = 1;
    /// <summary>Corner-to-corner width of the regular hexagon in metres (10..20).</summary>
    public double Diameter { get; set; } = 16;
    /// <summary>Environment field cell size in metres.</summary>
    public double CellSize { get; set; } = 0.25;
    public TerrainProfile Terrain { get; set; } = new();
    public WaterConfig Water { get; set; } = new();
    public PlacementProfile Placement { get; set; } = new();
    public List<StarterEntry> StarterFlora { get; set; } = new();
    public List<StarterEntry> StarterFauna { get; set; } = new();

    public const double MinDiameter = 10, MaxDiameter = 20;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static WorldDescriptor FromJson(string json) => JsonSerializer.Deserialize<WorldDescriptor>(json, Json)
        ?? throw new InvalidDataException("World descriptor JSON was empty.");

    public WorldDescriptor Clone() => FromJson(ToJson());

    /// <summary>Returns problems that make the descriptor unusable (empty when valid).</summary>
    public List<string> Validate()
    {
        var e = new List<string>();
        if (!(Diameter >= MinDiameter && Diameter <= MaxDiameter)) e.Add($"diameter {Diameter} must be within [{MinDiameter}, {MaxDiameter}] m");
        if (!(CellSize >= 0.1 && CellSize <= 1.0)) e.Add($"cellSize {CellSize} must be within [0.1, 1.0] m");
        if (!(Terrain.MaxHeight > Terrain.MinHeight)) e.Add("terrain.maxHeight must exceed terrain.minHeight");
        if (!(Terrain.Bottom < Terrain.MinHeight - 0.5)) e.Add("terrain.bottom must be at least 0.5 m below terrain.minHeight");
        if (Terrain.Octaves is < 1 or > 8) e.Add("terrain.octaves must be within [1, 8]");
        double r = Diameter / 2;
        foreach (var s in Water.Springs)
            if (s.X * s.X + s.Z * s.Z > r * r) e.Add($"spring at ({s.X},{s.Z}) lies outside the island");
        foreach (var f in Terrain.Features)
            if (f.Type is not ("basin" or "hill" or "channel" or "ridge")) e.Add($"terrain feature type '{f.Type}' is unknown");
        return e;
    }
}

public sealed class TerrainProfile
{
    public double BaseHeight { get; set; } = 0.6;
    public double Relief { get; set; } = 0.5;
    public double NoiseScale { get; set; } = 0.18;
    public int Octaves { get; set; } = 4;
    public double MinHeight { get; set; } = -0.8;
    public double MaxHeight { get; set; } = 2.5;
    /// <summary>Y of the flat island underside.</summary>
    public double Bottom { get; set; } = -3.0;
    /// <summary>Fraction of rock-classified base substrate on steep/exposed ground (0..1).</summary>
    public double RockExposure { get; set; } = 0.12;
    public List<TerrainFeature> Features { get; set; } = new();
}

public sealed class TerrainFeature
{
    /// <summary>basin | hill | channel | ridge</summary>
    public string Type { get; set; } = "hill";
    public double X { get; set; }
    public double Z { get; set; }
    public double ToX { get; set; }
    public double ToZ { get; set; }
    public double Radius { get; set; } = 2;
    public double Amount { get; set; } = 0.5;
    public double Width { get; set; } = 0.6;
}

public sealed class WaterConfig
{
    /// <summary>Fixed groundwater surface elevation (world Y).</summary>
    public double WaterTable { get; set; } = 0.0;
    public List<SpringConfig> Springs { get; set; } = new();
    /// <summary>Fraction of surface-height difference exchanged per hydrology step (0..0.24 for stability).</summary>
    public double FlowRate { get; set; } = 0.2;
    /// <summary>Depth (m) lost per sim-day from exposed surface water above the water table.</summary>
    public double Evaporation { get; set; } = 0.004;
    /// <summary>Depth (m) per sim-day infiltrating into dry ground (cells above the water table).</summary>
    public double Infiltration { get; set; } = 0.01;
    /// <summary>Minimum depth that counts as water-covered ground.</summary>
    public double WetDepth { get; set; } = 0.008;
    /// <summary>How far below the terrain edge the virtual exterior sits for boundary outflow.</summary>
    public double BoundaryDrop { get; set; } = 0.3;
    /// <summary>Hydrology sub-steps per scheduled hydrology tick.</summary>
    public int SubSteps { get; set; } = 4;
}

public sealed class SpringConfig
{
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>Discharge in cubic metres per sim-hour.</summary>
    public double Discharge { get; set; } = 0.01;
}

public sealed class PlacementProfile
{
    public int Rocks { get; set; } = 10;
    public double RockMinScale { get; set; } = 0.2;
    public double RockMaxScale { get; set; } = 0.7;
    public int Logs { get; set; } = 3;
    public double LogMinLength { get; set; } = 1.2;
    public double LogMaxLength { get; set; } = 2.6;
    public double LogMinRadius { get; set; } = 0.12;
    public double LogMaxRadius { get; set; } = 0.25;
    public int GravelPatches { get; set; } = 4;
    public double GravelMinRadius { get; set; } = 0.5;
    public double GravelMaxRadius { get; set; } = 1.1;
    /// <summary>Minimum clearance between generated props (m).</summary>
    public double Spacing { get; set; } = 0.4;
}

public sealed class StarterEntry
{
    public string Species { get; set; } = "";
    public int Count { get; set; }
}
