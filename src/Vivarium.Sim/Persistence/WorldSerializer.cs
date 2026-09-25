using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Persistence;

// ---------------------------------------------------------------------- payload DTOs

public sealed class WorldPayload
{
    public WorldDescriptor Descriptor { get; set; } = new();
    public ulong LastSerial { get; set; }
    public long Tick { get; set; }
    /// <summary>Biological clock (null in saves made before it existed: those ran biology at 1×, so it equals sim time).</summary>
    public double? BioSeconds { get; set; }
    public PropSet Props { get; set; } = new();
    public List<Spring> Springs { get; set; } = new();
    public EcologyTally Tally { get; set; } = new();
    /// <summary>Sculpted terrain as per-vertex offsets from the generated heights (null = never sculpted).</summary>
    public string? TerrainDelta { get; set; }
}

public sealed class FieldsPayload
{
    public int CellCount { get; set; }
    public string Nutrients { get; set; } = "";
    public string Moisture { get; set; } = "";
    public string Detritus { get; set; } = "";
    public string Biofilm { get; set; } = "";
    public string Plankton { get; set; } = "";
}

public sealed class WaterPayload
{
    public string Depth { get; set; } = "";
    public WaterBudget Budget { get; set; } = new();
}

public sealed class CoverageTilePayload
{
    public int Ti { get; set; }
    public int Tj { get; set; }
    public string Occ { get; set; } = "";
    public string B { get; set; } = "";
    public string W { get; set; } = "";
    public string Age { get; set; } = "";
    public string Dorm { get; set; } = "";
    public string Flags { get; set; } = "";
    public string D2E { get; set; } = "";
}

public sealed class CoverageLayerPayload
{
    public int Id { get; set; }
    public List<CoverageTilePayload> Tiles { get; set; } = new();
}

public sealed class CoveragePayload { public List<CoverageLayerPayload> Layers { get; set; } = new(); }

public sealed class FloraPayload { public List<FloraIndividual> Items { get; set; } = new(); }
public sealed class FaunaPayload { public List<FaunaIndividual> Items { get; set; } = new(); }
public sealed class GeneticsPayload
{
    public List<Genome> Genomes { get; set; } = new();
    public List<LineageRecord> Lineage { get; set; } = new();
}

/// <summary>
/// Canonical serialization of all authoritative state into named payloads. The same bytes feed saves and
/// the determinism digest, so the two can never disagree about what "state" means.
/// </summary>
public static class WorldSerializer
{
    public static readonly string[] PayloadOrder = { "world", "fields", "water", "coverage", "flora", "fauna", "genetics" };

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        IgnoreReadOnlyProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new EntityIdConverter(), new JsonStringEnumConverter() },
    };

    public static SortedDictionary<string, byte[]> Serialize(VivariumWorld w)
    {
        var p = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        p["world"] = Bytes(new WorldPayload
        {
            Descriptor = w.Descriptor, LastSerial = w.Ids.LastSerial, Tick = w.Clock.Tick, BioSeconds = w.Clock.BioSeconds,
            Props = w.Props, Springs = w.Water.Springs, Tally = w.Tally,
            TerrainDelta = w.Terrain.ExportDelta() is { } delta ? Pack(delta) : null,
        });
        var f = w.Fields;
        p["fields"] = Bytes(new FieldsPayload
        {
            CellCount = w.Grid.DomainCells.Length,
            Nutrients = Pack(f.Nutrients.ExportDomainValues()), Moisture = Pack(f.Moisture.ExportDomainValues()),
            Detritus = Pack(f.Detritus.ExportDomainValues()), Biofilm = Pack(f.Biofilm.ExportDomainValues()), Plankton = Pack(f.Plankton.ExportDomainValues()),
        });
        var depth = new double[w.Grid.DomainCells.Length];
        for (int k = 0; k < depth.Length; k++) depth[k] = w.Water.Depth[w.Grid.DomainCells[k]];
        p["water"] = Bytes(new WaterPayload { Depth = Pack(depth), Budget = w.Water.Budget });
        p["coverage"] = Bytes(new CoveragePayload
        {
            Layers = w.Coverage.All.Select(layer => new CoverageLayerPayload
            {
                Id = (int)layer.Id,
                Tiles = layer.ExportTiles().Select(t => new CoverageTilePayload
                {
                    Ti = t.Ti, Tj = t.Tj,
                    Occ = PackBytes(t.Occ), B = PackFloats(t.B), W = PackBytes(t.W),
                    Age = PackUShorts(t.Age), Dorm = PackBytes(t.Dorm), Flags = PackBytes(t.Flags), D2E = PackBytes(t.D2E),
                }).ToList(),
            }).ToList(),
        });
        p["flora"] = Bytes(new FloraPayload { Items = w.Flora.Items });
        p["fauna"] = Bytes(new FaunaPayload { Items = w.Fauna.Items });
        p["genetics"] = Bytes(new GeneticsPayload { Genomes = w.Genomes.Ordered().ToList(), Lineage = w.Lineage.Ordered().ToList() });
        return p;
    }

    /// <summary>SHA-256 over every authoritative payload in canonical order.</summary>
    public static string Digest(VivariumWorld w) => DigestOf(Serialize(w));

    public static string DigestOf(IReadOnlyDictionary<string, byte[]> payloads)
    {
        using var d = new DigestBuilder();
        foreach (var name in PayloadOrder) { d.Add(name); d.Add(payloads[name]); }
        return d.Hex();
    }

    /// <summary>Per-subsystem digests (useful to localize divergence).</summary>
    public static Dictionary<string, string> SubsystemDigests(VivariumWorld w) =>
        Serialize(w).ToDictionary(kv => kv.Key, kv => Core.Digest.Sha256Hex(kv.Value));

    /// <summary>
    /// Rebuilds a world from payloads into a new, staged object. Throws <see cref="InvalidDataException"/>
    /// with an explanation on any missing, corrupt or inconsistent data. Never touches another world.
    /// </summary>
    public static VivariumWorld Deserialize(ContentLibrary content, IReadOnlyDictionary<string, byte[]> payloads)
    {
        foreach (var name in PayloadOrder)
            if (!payloads.ContainsKey(name)) throw new InvalidDataException($"save is missing the '{name}' payload");
        var wp = Read<WorldPayload>(payloads, "world");
        var problems = wp.Descriptor.Validate();
        if (problems.Count > 0) throw new InvalidDataException("saved world descriptor is invalid: " + string.Join("; ", problems));

        var w = VivariumWorld.CreateBaseline(content, wp.Descriptor);
        w.Ids.LastSerial = wp.LastSerial;
        w.Clock.Tick = wp.Tick;
        w.Clock.BioSeconds = wp.BioSeconds ?? wp.Tick * Time.SimClock.FixedStepSeconds;
        if (!double.IsFinite(w.Clock.BioSeconds) || w.Clock.BioSeconds < 0) throw new InvalidDataException($"biological clock is invalid ({w.Clock.BioSeconds})");
        w.Props = wp.Props ?? new PropSet();
        w.Props.Touch();
        w.Water.Springs.AddRange(wp.Springs ?? new());
        w.Tally = wp.Tally ?? new EcologyTally();
        if (wp.TerrainDelta != null)
        {
            w.Terrain.ApplyDelta(Unpack(wp.TerrainDelta, "terrain edits"));
            w.Water.RefreshBed(w.Terrain);
        }

        var fp = Read<FieldsPayload>(payloads, "fields");
        if (fp.CellCount != w.Grid.DomainCells.Length) throw new InvalidDataException($"field grid mismatch: save has {fp.CellCount} cells, world has {w.Grid.DomainCells.Length}");
        w.Fields.Nutrients.ImportDomainValues(Unpack(fp.Nutrients, "nutrients"));
        w.Fields.Moisture.ImportDomainValues(Unpack(fp.Moisture, "moisture"));
        w.Fields.Detritus.ImportDomainValues(Unpack(fp.Detritus, "detritus"));
        w.Fields.Biofilm.ImportDomainValues(Unpack(fp.Biofilm, "biofilm"));
        w.Fields.Plankton.ImportDomainValues(Unpack(fp.Plankton, "plankton"));

        var wa = Read<WaterPayload>(payloads, "water");
        var depth = Unpack(wa.Depth, "water depth");
        if (depth.Length != w.Grid.DomainCells.Length) throw new InvalidDataException("water depth grid mismatch");
        for (int k = 0; k < depth.Length; k++)
        {
            if (!(depth[k] >= 0) || !double.IsFinite(depth[k])) throw new InvalidDataException($"water depth {k} is invalid ({depth[k]})");
            w.Water.Depth[w.Grid.DomainCells[k]] = depth[k];
        }
        w.Water.Budget = wa.Budget ?? new WaterBudget();

        var cp = Read<CoveragePayload>(payloads, "coverage");
        foreach (var lp in cp.Layers ?? new())
        {
            var layer = w.Coverage.ById((CoverageLayerId)lp.Id);
            layer.Clear();
            foreach (var tp in lp.Tiles)
            {
                layer.ImportTile(tp.Ti, tp.Tj,
                    UnpackBytes(tp.Occ, "coverage occ"), UnpackFloats(tp.B, "coverage biomass"), UnpackBytes(tp.W, "coverage water"),
                    UnpackUShorts(tp.Age, "coverage age"), UnpackBytes(tp.Dorm, "coverage dormancy"),
                    UnpackBytes(tp.Flags, "coverage flags"), UnpackBytes(tp.D2E, "coverage d2e"));
            }
        }

        foreach (var f in Read<FloraPayload>(payloads, "flora").Items.OrderBy(f => f.Id.Value)) w.Flora.Add(f);
        foreach (var f in Read<FaunaPayload>(payloads, "fauna").Items.OrderBy(f => f.Id.Value)) w.Fauna.Add(f);
        var gp = Read<GeneticsPayload>(payloads, "genetics");
        foreach (var g in gp.Genomes) w.Genomes.Add(g);
        foreach (var r in gp.Lineage) w.Lineage.Add(r);

        Validate(w);
        w.Fields.RecomputeLight(w.Terrain, w.Props);
        return w;
    }

    /// <summary>Cross-reference validation of a staged world. Throws on the first category of problem found.</summary>
    public static void Validate(VivariumWorld w)
    {
        var errors = new List<string>();
        var ids = new HashSet<EntityId>();
        void Id(EntityId id, string what)
        {
            if (id.IsNone) errors.Add($"{what} has no id");
            else if (!ids.Add(id)) errors.Add($"duplicate id {id} ({what})");
            else if (id.Serial > w.Ids.LastSerial) errors.Add($"{what} id {id} exceeds id allocator ({w.Ids.LastSerial})");
        }
        foreach (var r in w.Props.Rocks) { Id(r.Id, "rock"); if (!w.Domain.Contains(r.Position)) errors.Add($"rock {r.Id} outside island"); }
        foreach (var l in w.Props.Logs) { Id(l.Id, "log"); if (!w.Domain.Contains(l.Position)) errors.Add($"log {l.Id} outside island"); }
        foreach (var g in w.Props.Gravel) { Id(g.Id, "gravel"); if (!w.Domain.Contains(g.Position)) errors.Add($"gravel {g.Id} outside island"); }
        foreach (var s in w.Water.Springs) { Id(s.Id, "spring"); if (!w.Domain.Contains(s.Position)) errors.Add($"spring {s.Id} outside island"); }
        foreach (var f in w.Flora.Items)
        {
            Id(f.Id, "flora");
            if (w.Content.FloraById(f.SpeciesId) == null) errors.Add($"flora {f.Id} references unknown species '{f.SpeciesId}'");
            if (!w.Domain.Contains(f.Position)) errors.Add($"flora {f.Id} outside island");
            if (!double.IsFinite(f.Biomass) || f.Biomass < 0 || !double.IsFinite(f.Age)) errors.Add($"flora {f.Id} has invalid numbers");
        }
        foreach (var g in w.Genomes.Ordered())
        {
            Id(g.Id, "genome");
            var sp = w.Content.FaunaById(g.SpeciesId);
            if (sp == null) errors.Add($"genome {g.Id} references unknown species '{g.SpeciesId}'");
            else if (g.Traits.Length != sp.Traits.Count) errors.Add($"genome {g.Id} has {g.Traits.Length} traits, species expects {sp.Traits.Count}");
            if (g.Traits.Any(t => !double.IsFinite(t) || t < 0 || t > 1)) errors.Add($"genome {g.Id} has out-of-range traits");
        }
        foreach (var f in w.Fauna.Items)
        {
            Id(f.Id, "fauna");
            var sp = w.Content.FaunaById(f.SpeciesId);
            if (sp == null) errors.Add($"fauna {f.Id} references unknown species '{f.SpeciesId}'");
            if (!w.Genomes.Contains(f.GenomeId)) errors.Add($"fauna {f.Id} references missing genome {f.GenomeId}");
            if (!f.Position.IsFinite || !w.Domain.Contains(f.PositionXZ)) errors.Add($"fauna {f.Id} has invalid position");
            if (!double.IsFinite(f.Energy) || f.Energy < 0 || !double.IsFinite(f.Age)) errors.Add($"fauna {f.Id} has invalid numbers");
        }
        foreach (var r in w.Lineage.Ordered())
            if (!w.Genomes.Contains(r.GenomeId)) errors.Add($"lineage record {r.Id} references missing genome {r.GenomeId}");
        if (errors.Count > 0)
            throw new InvalidDataException($"save failed validation ({errors.Count} problem(s)): " + string.Join("; ", errors.Take(8)));
    }

    // ---------------------------------------------------------------------- helpers

    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);

    private static T Read<T>(IReadOnlyDictionary<string, byte[]> payloads, string name)
    {
        try { return JsonSerializer.Deserialize<T>(payloads[name], Json) ?? throw new InvalidDataException($"payload '{name}' is empty"); }
        catch (JsonException ex) { throw new InvalidDataException($"payload '{name}' is corrupt: {ex.Message}", ex); }
    }

    /// <summary>Exact little-endian IEEE-754 packing (bit-identical round trip).</summary>
    public static string Pack(double[] values)
    {
        var bytes = new byte[values.Length * 8];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static double[] Unpack(string s, string what)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(s); }
        catch (FormatException) { throw new InvalidDataException($"{what} data is not valid base64"); }
        if (bytes.Length % 8 != 0) throw new InvalidDataException($"{what} data has a truncated length");
        var values = new double[bytes.Length / 8];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    public static string PackBytes(byte[] values) => Convert.ToBase64String(values);

    public static byte[] UnpackBytes(string s, string what)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { throw new InvalidDataException($"{what} data is not valid base64"); }
    }

    public static string PackFloats(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static float[] UnpackFloats(string s, string what)
    {
        var bytes = UnpackBytes(s, what);
        if (bytes.Length % 4 != 0) throw new InvalidDataException($"{what} data has a truncated length");
        var values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    public static string PackUShorts(ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static ushort[] UnpackUShorts(string s, string what)
    {
        var bytes = UnpackBytes(s, what);
        if (bytes.Length % 2 != 0) throw new InvalidDataException($"{what} data has a truncated length");
        var values = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    public static string Text(byte[] payload) => Encoding.UTF8.GetString(payload);
}

public sealed class EntityIdConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) => new(reader.GetUInt64());
    public override void Write(Utf8JsonWriter writer, EntityId v, JsonSerializerOptions o) => writer.WriteNumberValue(v.Value);
}
