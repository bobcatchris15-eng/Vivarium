using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Persistence;

public sealed class SavePayloadInfo
{
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Bytes { get; set; }
}

/// <summary>Root save metadata. Readable on its own, before any live state is touched.</summary>
public sealed class SaveManifest
{
    public string Format { get; set; } = "vivarium-save";
    public int FormatVersion { get; set; } = Core.AppVersion.SaveSchema;
    public string AppVersion { get; set; } = Core.AppVersion.Application;
    public DateTime CreatedUtc { get; set; }
    public DateTime SavedUtc { get; set; }
    public string WorldName { get; set; } = "";
    public string PresetId { get; set; } = "";
    public ulong Seed { get; set; }
    public long SimTick { get; set; }
    public double SimDays { get; set; }
    public string ContentDigest { get; set; } = "";
    public string StateDigest { get; set; } = "";
    /// <summary>Session preferences restored on load; not part of the authoritative digest.</summary>
    public int SpeedIndex { get; set; } = Time.SimClock.DefaultSpeedIndex;
    public bool Paused { get; set; }
    public int FloraCount { get; set; }
    public int FaunaCount { get; set; }
    public List<SavePayloadInfo> Payloads { get; set; } = new();
    /// <summary>Migrations applied when this save was loaded (not written).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<string> AppliedMigrations { get; } = new();
}

public enum SaveCompatibility { Current, Migratable, UnsupportedNewer, UnsupportedOlder, NotASave }

public sealed class LoadResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public VivariumWorld? World { get; init; }
    public SaveManifest? Manifest { get; init; }
    public SaveCompatibility Compatibility { get; init; }
    public static LoadResult Fail(string msg, SaveCompatibility c = SaveCompatibility.NotASave, SaveManifest? m = null) => new() { Ok = false, Message = msg, Compatibility = c, Manifest = m };
}

public sealed class SaveResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string Path { get; init; } = "";
    public SaveManifest? Manifest { get; init; }
    public double Milliseconds { get; init; }
}

/// <summary>
/// Transactional save/load. Saves are staged to <c>*.tmp</c> and swapped in only after the archive is
/// complete and re-read successfully; the previous save is kept as <c>*.bak</c>. Loads build a complete staged
/// world and validate it before returning it, so a corrupt file can never half-overwrite a running world.
/// </summary>
public static class SaveSystem
{
    public const string Extension = ".vivsave";
    private const string ManifestEntry = "manifest.json";

    /// <summary>Test hook: invoked after the temp file is written and before it replaces the target.</summary>
    public static Action<string>? FaultInjectionBeforeCommit { get; set; }

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    /// <summary>Captures an in-memory snapshot (fast, main thread). Writing it can happen elsewhere.</summary>
    public static (SaveManifest Manifest, SortedDictionary<string, byte[]> Payloads) Snapshot(VivariumWorld w, DateTime? createdUtc = null)
    {
        var payloads = WorldSerializer.Serialize(w);
        var m = new SaveManifest
        {
            CreatedUtc = createdUtc ?? DateTime.UtcNow, SavedUtc = DateTime.UtcNow,
            WorldName = w.Descriptor.Name, PresetId = w.Descriptor.PresetId, Seed = w.Seed,
            SimTick = w.Clock.Tick, SimDays = w.Clock.BioDays, ContentDigest = w.Content.ContentDigest,
            StateDigest = WorldSerializer.DigestOf(payloads), SpeedIndex = w.Clock.SpeedIndex, Paused = w.Clock.Paused,
            FloraCount = w.Flora.Count, FaunaCount = w.Fauna.Count,
            Payloads = payloads.Select(kv => new SavePayloadInfo { Name = kv.Key, Sha256 = Digest.Sha256Hex(kv.Value), Bytes = kv.Value.Length }).ToList(),
        };
        return (m, payloads);
    }

    public static SaveResult Save(VivariumWorld w, string path, DateTime? createdUtc = null)
    {
        var (m, p) = Snapshot(w, createdUtc);
        return Write(m, p, path);
    }

    /// <summary>Writes a snapshot atomically. Thread-safe with respect to the world (does not touch it).</summary>
    public static SaveResult Write(SaveManifest manifest, IReadOnlyDictionary<string, byte[]> payloads, string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string full = Path.GetFullPath(path);
        string tmp = full + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson));
                    foreach (var (name, bytes) in payloads) WriteEntry(zip, name + ".json", bytes);
                }
                fs.Flush(flushToDisk: true);
            }
            // verify what we wrote before committing
            var check = ReadArchive(tmp);
            if (check.Manifest == null || check.Payloads.Count != payloads.Count) throw new IOException("staged save failed verification");
            FaultInjectionBeforeCommit?.Invoke(tmp);
            if (File.Exists(full)) File.Replace(tmp, full, full + ".bak", ignoreMetadataErrors: true);
            else File.Move(tmp, full);
            Log.Info(LogCategory.Persistence, $"Saved '{manifest.WorldName}' day {manifest.SimDays:0.0} to {full} ({new FileInfo(full).Length / 1024} KiB, {sw.ElapsedMilliseconds} ms).");
            return new SaveResult { Ok = true, Message = "saved", Path = full, Manifest = manifest, Milliseconds = sw.Elapsed.TotalMilliseconds };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* leave the stray temp; the target is intact */ }
            Log.Error(LogCategory.Persistence, $"Save to {full} failed; previous save left untouched", ex);
            return new SaveResult { Ok = false, Message = $"Save failed: {ex.Message}", Path = full };
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Fastest);
        e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); // reproducible archive metadata
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private sealed record Archive(SaveManifest? Manifest, JsonNode? ManifestNode, Dictionary<string, byte[]> Payloads);

    private static Archive ReadArchive(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[]? manifestBytes = null;
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            if (e.FullName == ManifestEntry) manifestBytes = ms.ToArray();
            else if (e.FullName.EndsWith(".json", StringComparison.Ordinal)) payloads[e.FullName[..^5]] = ms.ToArray();
        }
        if (manifestBytes == null) return new Archive(null, null, payloads);
        var node = JsonNode.Parse(manifestBytes);
        var manifest = node?.Deserialize<SaveManifest>(ManifestJson);
        return new Archive(manifest, node, payloads);
    }

    /// <summary>Reads only the manifest and classifies compatibility; never builds a world.</summary>
    public static (SaveManifest? Manifest, SaveCompatibility Compat, string Message) Inspect(string path)
    {
        try
        {
            var a = ReadArchive(path);
            if (a.Manifest == null || a.Manifest.Format != "vivarium-save") return (null, SaveCompatibility.NotASave, "not a Vivarium save (no manifest)");
            return (a.Manifest, Classify(a.Manifest.FormatVersion), Describe(a.Manifest.FormatVersion));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        {
            return (null, SaveCompatibility.NotASave, $"unreadable save: {ex.Message}");
        }
    }

    public static SaveCompatibility Classify(int formatVersion) =>
        formatVersion == AppVersion.SaveSchema ? SaveCompatibility.Current
        : formatVersion > AppVersion.SaveSchema ? SaveCompatibility.UnsupportedNewer
        : SaveMigrations.CanMigrate(formatVersion) ? SaveCompatibility.Migratable
        : SaveCompatibility.UnsupportedOlder;

    private static string Describe(int v) => Classify(v) switch
    {
        SaveCompatibility.Current => "current format",
        SaveCompatibility.Migratable => $"older format v{v}; will be migrated to v{AppVersion.SaveSchema}",
        SaveCompatibility.UnsupportedNewer => $"made by a newer Vivarium (format v{v} > v{AppVersion.SaveSchema}); please update",
        SaveCompatibility.UnsupportedOlder => $"format v{v} is too old to migrate",
        _ => "unknown",
    };

    /// <summary>
    /// Loads into a staged world. On any failure returns Ok=false and no world; the caller's current world is
    /// never modified by this method.
    /// </summary>
    public static LoadResult Load(ContentLibrary content, string path)
    {
        if (!File.Exists(path)) return LoadResult.Fail($"save not found: {path}");
        Archive a;
        try { a = ReadArchive(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or UnauthorizedAccessException)
        { return LoadResult.Fail($"unreadable save file: {ex.Message}"); }
        if (a.Manifest == null || a.ManifestNode == null || a.Manifest.Format != "vivarium-save") return LoadResult.Fail("not a Vivarium save (no manifest)");
        var m = a.Manifest;
        var compat = Classify(m.FormatVersion);
        if (compat is SaveCompatibility.UnsupportedNewer or SaveCompatibility.UnsupportedOlder) return LoadResult.Fail(Describe(m.FormatVersion), compat, m);

        var payloads = a.Payloads;
        if (compat == SaveCompatibility.Current)
        {
            foreach (var info in m.Payloads)
            {
                if (!payloads.TryGetValue(info.Name, out var bytes)) return LoadResult.Fail($"save is missing payload '{info.Name}'", compat, m);
                if (Digest.Sha256Hex(bytes) != info.Sha256) return LoadResult.Fail($"payload '{info.Name}' is corrupt (checksum mismatch)", compat, m);
            }
        }
        else
        {
            try
            {
                var migrated = SaveMigrations.Migrate(m.FormatVersion, a.ManifestNode, payloads, m.AppliedMigrations);
                payloads = migrated;
                m.FormatVersion = AppVersion.SaveSchema;
            }
            catch (Exception ex) { return LoadResult.Fail($"migration from v{m.FormatVersion} failed: {ex.Message}", compat, m); }
        }

        try
        {
            var w = WorldSerializer.Deserialize(content, payloads);
            w.Clock.SetSpeedIndex(m.SpeedIndex);
            w.Clock.Paused = m.Paused;
            if (compat == SaveCompatibility.Current && !string.IsNullOrEmpty(m.StateDigest))
            {
                string digest = WorldSerializer.Digest(w);
                if (digest != m.StateDigest) return LoadResult.Fail("save state digest mismatch after load (file damaged or content changed)", compat, m);
            }
            if (m.ContentDigest != content.ContentDigest)
                Log.Warn(LogCategory.Persistence, "Save was made with different content definitions; loading with current content.");
            Log.Info(LogCategory.Persistence, $"Loaded '{m.WorldName}' day {m.SimDays:0.0} from {path}" + (m.AppliedMigrations.Count > 0 ? $" (migrated: {string.Join(", ", m.AppliedMigrations)})" : ""));
            return new LoadResult { Ok = true, Message = "loaded", World = w, Manifest = m, Compatibility = compat };
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException)
        {
            Log.Error(LogCategory.Persistence, $"Load of {path} failed validation", ex);
            return LoadResult.Fail($"save could not be loaded: {ex.Message}", compat, m);
        }
    }
}

/// <summary>
/// Ordered, explicit save migrations. Each step transforms payload JSON from version N to N+1.
/// v0 → v1: v0 (pre-release) stored the clock as simSeconds (double) instead of tick (long), and fauna
/// energy as a 0..100 percentage.
/// </summary>
public static class SaveMigrations
{
    public delegate void Step(Dictionary<string, JsonNode> payloads);

    private static readonly SortedDictionary<int, (string Name, Step Apply)> Steps = new()
    {
        [0] = ("v0→v1: clock seconds→ticks, fauna energy percent→fraction", p =>
        {
            var world = p["world"].AsObject();
            if (world.TryGetPropertyValue("SimSeconds", out var secs) && secs != null)
            {
                world["Tick"] = (long)Math.Round(secs.GetValue<double>() / Time.SimClock.FixedStepSeconds);
                world.Remove("SimSeconds");
            }
            foreach (var f in p["fauna"]["Items"]!.AsArray())
                if (f!["EnergyPercent"] is JsonNode e) { f["Energy"] = e.GetValue<double>() / 100.0; f.AsObject().Remove("EnergyPercent"); }
        }),
    };

    public static int Oldest => Steps.Count == 0 ? AppVersion.SaveSchema : Steps.Keys.First();
    public static bool CanMigrate(int from) => from < AppVersion.SaveSchema && Enumerable.Range(from, AppVersion.SaveSchema - from).All(Steps.ContainsKey);

    public static Dictionary<string, byte[]> Migrate(int from, JsonNode manifest, Dictionary<string, byte[]> raw, List<string> applied)
    {
        if (!CanMigrate(from)) throw new InvalidOperationException($"no migration path from v{from}");
        var nodes = raw.ToDictionary(kv => kv.Key, kv => JsonNode.Parse(kv.Value) ?? throw new InvalidDataException($"payload {kv.Key} empty"));
        for (int v = from; v < AppVersion.SaveSchema; v++)
        {
            var (name, apply) = Steps[v];
            apply(nodes);
            applied.Add(name);
        }
        manifest["FormatVersion"] = AppVersion.SaveSchema;
        return nodes.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToUtf8Bytes(kv.Value));
    }
}
