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
    /// <summary>random | none | a PilotTreeCatalog id. Resolved to a specific form when the world is created.</summary>
    public string PilotTreeId { get; set; } = "random";
    /// <summary>-1 selects a seeded corner; 0..5 selects an exact hexagon vertex. Edge anchors are not allowed.</summary>
    public int PilotTreeCorner { get; set; } = -1;
    /// <summary>
    /// Biological seconds per physical simulated second. The default makes one real minute one biological day at
    /// 1× speed (1440 / 168). Worlds saved before this setting existed also load with the default.
    /// </summary>
    public double BioAcceleration { get; set; } = DefaultBioAcceleration;

    public const double MinDiameter = 5, MaxDiameter = 10;
    /// <summary>Largest diameter accepted when loading a save written before the 12 m cap.</summary>
    public const double LegacyMaxDiameter = 20;

    /// <summary>Set by the save loader so older, larger worlds still validate; new worlds use <see cref="MaxDiameter"/>.</summary>
    [ThreadStatic] public static bool AllowLegacyDiameter;
    public const double DefaultBioAcceleration = 60.0 / 7, MinBioAcceleration = 1, MaxBioAcceleration = 30;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static WorldDescriptor FromJson(string json) => JsonSerializer.Deserialize<WorldDescriptor>(json, Json)
        ?? throw new InvalidDataException("World descriptor JSON was empty.");

    public WorldDescriptor Clone() => FromJson(ToJson());

    /// <summary>Returns problems that make the descriptor unusable (empty when valid).</summary>
    public List<string> Validate()
    {
        var e = new List<string>();
        if (PilotTreeId is not ("random" or "none") && PilotTreeCatalog.Find(PilotTreeId) == null) e.Add($"unknown Pilot Tree '{PilotTreeId}'");
        if (PilotTreeCorner is < -1 or > 5) e.Add("Pilot Tree corner must be -1 or 0..5");
        double maxD = AllowLegacyDiameter ? LegacyMaxDiameter : MaxDiameter;
        if (!(Diameter >= MinDiameter && Diameter <= maxD)) e.Add($"diameter {Diameter} must be within [{MinDiameter}, {maxD}] m");
        if (!(CellSize >= 0.1 && CellSize <= 1.0)) e.Add($"cellSize {CellSize} must be within [0.1, 1.0] m");
        if (!(Terrain.MaxHeight > Terrain.MinHeight)) e.Add("terrain.maxHeight must exceed terrain.minHeight");
        if (!(Terrain.Bottom < Terrain.MinHeight - 0.5)) e.Add("terrain.bottom must be at least 0.5 m below terrain.minHeight");
        if (Terrain.Octaves is < 1 or > 8) e.Add("terrain.octaves must be within [1, 8]");
        if (!(BioAcceleration >= MinBioAcceleration && BioAcceleration <= MaxBioAcceleration)) e.Add($"bioAcceleration {BioAcceleration} must be within [{MinBioAcceleration}, {MaxBioAcceleration}]");
        double r = Diameter / 2;
        if (!(Water.FlowRate > 0 && Water.FlowRate <= 0.24)) e.Add($"water.flowRate {Water.FlowRate} must be within (0, 0.24]");
        if (!(Water.FlowMemorySeconds >= 1 && Water.FlowMemorySeconds <= 600)) e.Add($"water.flowMemorySeconds {Water.FlowMemorySeconds} must be within [1, 600]");
        if (!(Water.FlowResponseSeconds >= 1 && Water.FlowResponseSeconds <= 600)) e.Add($"water.flowResponseSeconds {Water.FlowResponseSeconds} must be within [1, 600]");
        if (!(Water.StreamVelocityThreshold >= 0 && Water.StreamVelocityThreshold <= 1)) e.Add($"water.streamVelocityThreshold {Water.StreamVelocityThreshold} must be within [0, 1]");
        if (!(Water.SubSteps >= 1 && Water.SubSteps <= 32)) e.Add($"water.subSteps {Water.SubSteps} must be within [1, 32]");
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
    /// <summary>Legacy: ignored since the level-equalizing solver; still parsed so older presets and saves load.</summary>
    public double FlowRate { get; set; } = 0.2;
    /// <summary>Legacy: ignored (the solver carries no momentum); still parsed for compatibility.</summary>
    public double FlowMemorySeconds { get; set; } = 45.0;
    /// <summary>Legacy: ignored; still parsed for compatibility.</summary>
    public double FlowResponseSeconds { get; set; } = 30.0;
    /// <summary>Minimum derived velocity (m/s) for the ecological/query convenience classification IsStream.</summary>
    public double StreamVelocityThreshold { get; set; } = 0.0001;
    /// <summary>Depth (m) lost per sim-day from exposed surface water above the water table.</summary>
    public double Evaporation { get; set; } = 0.004;
    /// <summary>Depth (m) per sim-day infiltrating into dry ground (cells above the water table).</summary>
    public double Infiltration { get; set; } = 0.01;
    /// <summary>Minimum depth that counts as water-covered ground.</summary>
    public double WetDepth { get; set; } = 0.008;
    /// <summary>How far below the terrain edge the virtual exterior sits for boundary outflow.</summary>
    public double BoundaryDrop { get; set; } = 0.3;
    /// <summary>Minimum hydrology sub-steps per tick; the solver also caps each sub-step at 10 s.</summary>
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
