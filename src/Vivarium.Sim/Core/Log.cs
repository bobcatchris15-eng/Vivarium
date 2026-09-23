using System.Globalization;
using System.Text;

namespace Vivarium.Sim.Core;

public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3, Fatal = 4 }

public enum LogCategory { App, Content, World, Time, Hydrology, Flora, Fauna, Genetics, Ecology, Tools, Persistence, Render, UI, Perf, Test }

public readonly record struct LogEntry(DateTime TimestampUtc, LogLevel Level, LogCategory Category, string Message)
{
    public string Format() =>
        $"{TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)} [{Level.ToString().ToUpperInvariant(),-7}] [{Category}] {Message}";
}

public interface ILogSink
{
    void Write(in LogEntry entry);
    void Flush() { }
}

/// <summary>
/// Process-wide structured logger. Sinks never throw into callers: a failing sink is disabled
/// after reporting once through the remaining sinks.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<ILogSink> Sinks = new();
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void AddSink(ILogSink sink) { lock (Gate) Sinks.Add(sink); }
    public static void RemoveSink(ILogSink sink) { lock (Gate) Sinks.Remove(sink); }
    public static void ClearSinks() { lock (Gate) { foreach (var s in Sinks) SafeFlush(s); Sinks.Clear(); } }

    public static void Write(LogLevel level, LogCategory category, string message)
    {
        if (level < MinimumLevel) return;
        var entry = new LogEntry(DateTime.UtcNow, level, category, message ?? string.Empty);
        List<ILogSink>? failed = null;
        lock (Gate)
        {
            foreach (var sink in Sinks)
            {
                try { sink.Write(entry); if (level >= LogLevel.Error) sink.Flush(); }
                catch { (failed ??= new()).Add(sink); }
            }
            if (failed != null)
                foreach (var f in failed) Sinks.Remove(f);
        }
        if (failed != null)
            Write(LogLevel.Warning, LogCategory.App, $"{failed.Count} log sink(s) failed and were disabled.");
    }

    public static void Debug(LogCategory c, string m) => Write(LogLevel.Debug, c, m);
    public static void Info(LogCategory c, string m) => Write(LogLevel.Info, c, m);
    public static void Warn(LogCategory c, string m) => Write(LogLevel.Warning, c, m);
    public static void Error(LogCategory c, string m) => Write(LogLevel.Error, c, m);
    public static void Fatal(LogCategory c, string m) => Write(LogLevel.Fatal, c, m);
    public static void Error(LogCategory c, string m, Exception ex) => Write(LogLevel.Error, c, $"{m}: {ex.GetType().Name}: {ex.Message}");

    public static void Flush() { lock (Gate) foreach (var s in Sinks) SafeFlush(s); }

    private static void SafeFlush(ILogSink s) { try { s.Flush(); } catch { /* sink is best effort */ } }
}

/// <summary>In-memory sink used by tests and the in-app diagnostics view.</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = new();
    public int Capacity { get; }
    public MemoryLogSink(int capacity = 2000) { Capacity = capacity; }
    public void Write(in LogEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
    }
    public IReadOnlyList<LogEntry> Snapshot() { lock (_gate) return _entries.ToArray(); }
    public int Count(LogLevel atLeast) { lock (_gate) return _entries.Count(e => e.Level >= atLeast); }
}

/// <summary>Append-only text file sink with size-based rollover to one previous file.</summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly string _path;
    private StreamWriter? _writer;
    private readonly long _maxBytes;

    public FileLogSink(string path, long maxBytes = 4 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
        {
            File.Copy(path, path + ".1", overwrite: true);
            File.Delete(path);
        }
        // AutoFlush: log volume is low and a crash must not lose the last lines
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
    }

    public string Path_ => _path;

    public void Write(in LogEntry entry) => _writer?.WriteLine(entry.Format());
    public void Flush() => _writer?.Flush();
    public void Dispose() { try { _writer?.Flush(); _writer?.Dispose(); } catch { } _writer = null; }
}
