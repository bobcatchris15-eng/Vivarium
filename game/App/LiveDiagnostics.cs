using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
namespace Vivarium.Game.App;

/// <summary>Opt-in normal-play recording. Bounded memory, background I/O, no camera or clock control.</summary>
public partial class LiveDiagnostics : Node
{
    public GameSession Session { get; set; } = null!;
    public string? RequestedDirectory { get; set; }
    public bool AutoStart { get; set; }
    public bool CheckMode { get; set; }
    public bool Recording { get; private set; }
    public string OutputDirectory { get; private set; } = "";
    private readonly List<double> _frames = new(512);
    private readonly long[] _histogram = new long[10001];
    private readonly List<string> _pending = new();
    private Task? _write;
    private readonly DiagnosticLogSink _logs = new();
    private ulong _start, _lastFrame, _lastSample;
    private long _totalFrames, _over33, _over100, _lastTick;
    private double _totalMs, _worst;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Soak benchmark (opt-in via --soak-seconds). Fixed-size state only; no per-frame allocation.
    private bool _soakOn, _soakWindow;
    private double _soakSeconds, _soakWarmup;
    private ulong _soakStart, _soakWinStart;
    private Rid _soakViewport;
    private readonly long[] _soakHist = new long[10001]; // 0.1 ms bins up to 1000 ms
    private long _soakFrames, _soakGpuN, _soakSamples;
    private double _soakMs, _soakWorst, _soakGpuSum, _soakGpuMax, _soakGpuNonZero;
    private double _soakDraws, _soakPrims, _soakNodes, _soakVideo, _soakTex, _soakBuf, _soakManaged, _soakDrawsMax;
    private long _soakAlloc0;
    public string[] SoakHidden { get; set; } = Array.Empty<string>();
    private int _soakGc0, _soakGc1, _soakGc2;
    private ulong _soakSampleAt;

    /// <summary>Starts the soak clock (call when the world is ready). Quits the app with exit code 0 after seconds.</summary>
    public void BeginSoak(double seconds, double warmup)
    {
        _soakOn = true; _soakSeconds = seconds; _soakWarmup = warmup; _soakWindow = false;
        _soakStart = Time.GetTicksUsec(); _soakSampleAt = _soakStart;
        try { _soakViewport = GetViewport().GetViewportRid(); RenderingServer.ViewportSetMeasureRenderTime(_soakViewport, true); } catch { }
    }
    private void SoakFrame(ulong now, double ms)
    {
        double elapsed = (now - _soakStart) / 1_000_000.0;
        if (!_soakWindow)
        {
            if (elapsed < _soakWarmup) { if (elapsed >= _soakSeconds) FinishSoak(now); return; }
            _soakWindow = true; _soakWinStart = now;
            Array.Clear(_soakHist); _soakFrames = _soakGpuN = _soakSamples = 0;
            _soakMs = _soakWorst = _soakGpuSum = _soakGpuMax = _soakGpuNonZero = 0;
            _soakDraws = _soakPrims = _soakNodes = _soakVideo = _soakTex = _soakBuf = _soakManaged = _soakDrawsMax = 0;
            _soakAlloc0 = GC.GetTotalAllocatedBytes(false);
            _soakGc0 = GC.CollectionCount(0); _soakGc1 = GC.CollectionCount(1); _soakGc2 = GC.CollectionCount(2);
            _soakSampleAt = now;
            FrameProfiler.StartWindow();
            return;
        }
        _soakFrames++; _soakMs += ms; if (ms > _soakWorst) _soakWorst = ms;
        _soakHist[Math.Clamp((int)Math.Ceiling(ms * 10), 0, 10000)]++;
        double d = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _soakDraws += d; if (d > _soakDrawsMax) _soakDrawsMax = d;
        _soakPrims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(_soakViewport);
        _soakGpuN++; _soakGpuSum += gpu; if (gpu > _soakGpuMax) _soakGpuMax = gpu; if (gpu > 0) _soakGpuNonZero++;
        if (now - _soakSampleAt >= 1_000_000)
        {
            _soakSampleAt = now; _soakSamples++;
            _soakNodes += Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            _soakVideo += Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
            _soakTex += Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed);
            _soakBuf += Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
            _soakManaged += GC.GetTotalMemory(false);
        }
        if (elapsed >= _soakSeconds) FinishSoak(now);
    }
    private double SoakPercentile(double q)
    {
        long target = Math.Max(1, (long)Math.Ceiling(_soakFrames * q)), count = 0;
        for (int i = 0; i < _soakHist.Length; i++) { count += _soakHist[i]; if (count >= target) return i / 10.0; }
        return 0;
    }
    private void FinishSoak(ulong now)
    {
        _soakOn = false;
        var scopes = _soakWindow ? FrameProfiler.StopWindow() : new Dictionary<string, FrameProfiler.ScopeMetric>();
        long frames = _soakFrames; double f = Math.Max(1, frames), s = Math.Max(1, _soakSamples);
        double R(double v) => Math.Round(v, 4);
        var scopeOut = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (k, m) in scopes)
            scopeOut[k] = new { mean_ms = R(m.TotalMs / Math.Max(1, m.Count)), max_ms = R(m.MaxMs), calls = m.Count, total_ms = R(m.TotalMs), mean_per_frame_ms = R(m.TotalMs / f) };
        var (allocByScope, allocTop) = FrameProfiler.WindowAllocations();
        var allocOut = new Dictionary<string, double>();
        foreach (var kv in allocByScope.OrderByDescending(k => k.Value).Take(20)) allocOut[kv.Key] = R(kv.Value / f);
        double totalAllocPerFrame = frames > 0 ? (GC.GetTotalAllocatedBytes(false) - _soakAlloc0) / f : 0;
        bool gpuOk = _soakGpuNonZero > 0;
        double? Mem(double sum) => _soakSamples > 0 ? Math.Round(sum / s) : null;
        var summary = new Dictionary<string, object?>
        {
            ["completed"] = true,
            ["written_utc"] = DateTime.UtcNow,
            ["soak_seconds"] = _soakSeconds, ["warmup_seconds"] = _soakWarmup,
            ["window_seconds"] = _soakWindow ? (now - _soakWinStart) / 1_000_000.0 : 0.0,
            ["frames"] = frames,
            ["fps_mean"] = _soakMs > 0 ? R(1000.0 * frames / _soakMs) : null,
            ["frame_ms_p50"] = frames > 0 ? SoakPercentile(.5) : null,
            ["frame_ms_p95"] = frames > 0 ? SoakPercentile(.95) : null,
            ["frame_ms_p99"] = frames > 0 ? SoakPercentile(.99) : null,
            ["frame_ms_worst"] = frames > 0 ? R(_soakWorst) : null,
            ["gpu_frame_ms_mean"] = gpuOk ? R(_soakGpuSum / Math.Max(1, _soakGpuN)) : null,
            ["gpu_frame_ms_max"] = gpuOk ? R(_soakGpuMax) : null,
            ["draw_calls"] = frames > 0 ? R(_soakDraws / f) : null,
            ["draw_calls_max"] = frames > 0 ? _soakDrawsMax : null,
            ["primitives"] = frames > 0 ? R(_soakPrims / f) : null,
            ["node_count"] = Mem(_soakNodes),
            ["video_mem_bytes"] = Mem(_soakVideo),
            ["texture_mem_bytes"] = Mem(_soakTex),
            ["buffer_mem_bytes"] = Mem(_soakBuf),
            ["managed_bytes"] = Mem(_soakManaged),
            ["gc_gen0"] = GC.CollectionCount(0) - _soakGc0,
            ["gc_gen1"] = GC.CollectionCount(1) - _soakGc1,
            ["gc_gen2"] = GC.CollectionCount(2) - _soakGc2,
            ["allocated_bytes_per_frame"] = frames > 0 ? R((GC.GetTotalAllocatedBytes(false) - _soakAlloc0) / f) : null,
            ["hidden"] = SoakHidden,
            ["alloc_bytes_per_frame_by_scope"] = allocOut,
            ["alloc_bytes_per_frame_unscoped"] = frames > 0 ? R(totalAllocPerFrame - allocTop / f) : null,
            ["multimesh_instance_count"] = null,
            ["scopes_cpu_ms"] = scopeOut,
            ["notes"] = "Steady state excludes samples before warmup. Draw calls/primitives/GPU are per-frame means; node/memory fields are means of 1 Hz samples. Frame percentiles use 0.1 ms bins. Scope times are CPU wall time and overlap; GeometryWorker is off-thread."
        };
        StopRecording();
        string path = Path.Combine(Path.GetFullPath(RequestedDirectory ?? Bridge.UserPath("diagnostics")), "perf_summary.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(summary, JsonOptions));
            GD.Print("VIVARIUM_SOAK_OK " + path);
            GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError("Soak summary write failed: " + ex.Message); GetTree().Quit(5); }
    }
    public override void _Ready()
    {
        ProcessPriority = 1000;
        if (AutoStart) StartRecording();
    }
    public void Toggle() { if (Recording) StopRecording(); else StartRecording(); }
    private void StartRecording()
    {
        try
        {
            OutputDirectory = Path.GetFullPath(RequestedDirectory ?? Bridge.UserPath("diagnostics"));
            OutputDirectory = Path.Combine(OutputDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + System.Environment.ProcessId);
            Directory.CreateDirectory(OutputDirectory);
            Array.Clear(_histogram); _frames.Clear(); _pending.Clear();
            _totalFrames = _over33 = _over100 = 0; _totalMs = _worst = 0;
            _start = _lastFrame = _lastSample = Time.GetTicksUsec();
            _lastTick = Session.World?.Clock.Tick ?? 0;
            File.WriteAllText(Path.Combine(OutputDirectory,"metadata.json"), JsonSerializer.Serialize(new {
                started_utc = DateTime.UtcNow, version = AppVersion.Describe(),
                godot = Engine.GetVersionInfo()["string"].ToString(), renderer = RenderingServer.GetVideoAdapterName(),
                os = OS.GetName(), os_version = OS.GetVersion(), processors = System.Environment.ProcessorCount,
                viewport = GetViewport().GetVisibleRect().Size.ToString(), settings = Session.Settings,
                app_log = Bridge.UserPath("logs/vivarium.log"),
                notes = "Normal play. Frame intervals use a monotonic wall clock, including startup and pauses. CPU scopes overlap; GeometryWorker is off-thread. No GPU timings. Summary percentiles use 1 ms histogram bins; values above 10 seconds share the last bin."
            }, JsonOptions));
            FrameProfiler.TakeStructuredReport();
            Log.AddSink(_logs);
            Recording = true;
            Session.Ui.Toast("Diagnostics recording · Ctrl+Shift+D to stop", false);
            GD.Print("DIAGNOSTICS_STARTED " + OutputDirectory);
        }
        catch (Exception ex) { Session.Ui.Toast("Diagnostics could not start: " + ex.Message, true); }
    }
    public override void _Process(double delta)
    {
        if (!Recording || Session.World is not { } w) return;
        ulong now = Time.GetTicksUsec();
        double ms = (now - _lastFrame) / 1000.0; _lastFrame = now;
        _frames.Add(ms); _totalFrames++; _totalMs += ms; _worst = Math.Max(_worst, ms);
        _histogram[Math.Clamp((int)Math.Ceiling(ms), 0, 10000)]++;
        if (ms > 33.4) _over33++;
        if (ms > 100) _over100++;
        if (_soakOn) { SoakFrame(now, ms); if (!_soakOn) return; }
        if (now - _lastSample < 1_000_000) return;
        double interval = (now - _lastSample) / 1_000_000.0;
        _lastSample = now;
        _frames.Sort();
        double P(double q) => _frames[Math.Min(_frames.Count-1, (int)(_frames.Count*q))];
        var camera = Session.CameraRig.GlobalPosition;
        _pending.Add(JsonSerializer.Serialize(new {
            utc = DateTime.UtcNow, elapsed_seconds = (now-_start)/1_000_000.0,
            frame_count = _frames.Count, fps = _frames.Count/interval, frame_ms_p50 = P(.5), frame_ms_p95 = P(.95), frame_ms_p99 = P(.99), frame_ms_worst = _frames[^1],
            process_ms = Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000,
            simulation_last_ms = w.Scheduler.LastAdvanceMs, backlog_sim_seconds = w.Scheduler.Backlog,
            simulation_pending_tick = w.Scheduler.HasPendingTick, ticks_per_second = (w.Clock.Tick-_lastTick)/interval, tick = w.Clock.Tick, bio_days = w.Clock.BioDays,
            paused = w.Clock.Paused, speed = w.Clock.SpeedMultiplier, world_name = w.Descriptor.Name, seed = w.Seed,
            flora = w.Flora.Count, fauna = w.Fauna.Count, flora_visible = Session.Flora.Visible_, fauna_drawn = Session.Fauna.Drawn,
            spring_particles = w.Liquid.Particles.Count, water_triangles = Session.WaterFluid.TrianglesBuilt,
            engine_memory_bytes = OS.GetStaticMemoryUsage(), managed_memory_bytes = GC.GetTotalMemory(false),
            working_set_bytes = System.Environment.WorkingSet, video_memory_bytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed),
            draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
            nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2),
            camera = new { x = camera.X, y = camera.Y, z = camera.Z }, terrain_version = w.Terrain.Version,
            warnings_and_errors = _logs.Drain(), log_overflow_count = _logs.Dropped,
            scope_max_ms = FrameProfiler.TakeStructuredReport()
        }));
        _lastTick = w.Clock.Tick; _frames.Clear();
        if (_write?.IsFaulted == true) { FailWrite(); return; }
        if (_pending.Count > 300) { Recording = false; Log.RemoveSink(_logs); Session.Ui.Toast("Diagnostics stopped: storage writer is not keeping up", true); return; }
        if (_write == null || _write.IsCompleted) QueueWrite(false);
        if (CheckMode && !Session.LoadingWorld && now-_start >= 12_000_000) { StopRecording(); GD.Print("LIVE_DIAGNOSTICS_CHECK_OK"); GetTree().Quit(); }
    }
    private double Percentile(double q)
    {
        long target = Math.Max(1, (long)Math.Ceiling(_totalFrames*q)), count = 0;
        for (int i=0;i<_histogram.Length;i++) { count += _histogram[i]; if (count >= target) return i; }
        return 0;
    }
    private void QueueWrite(bool completed)
    {
        string lines = string.Join("\n", _pending); _pending.Clear();
        string summary = JsonSerializer.Serialize(new {
            updated_utc = DateTime.UtcNow, completed, elapsed_seconds = (Time.GetTicksUsec()-_start)/1_000_000.0,
            frames = _totalFrames, fps_mean = _totalMs > 0 ? 1000*_totalFrames/_totalMs : 0,
            frame_ms_p50 = Percentile(.5), frame_ms_p95 = Percentile(.95), frame_ms_p99 = Percentile(.99),
            frame_ms_worst = _worst, frames_over_33ms = _over33, frames_over_100ms = _over100
        }, JsonOptions);
        string directory = OutputDirectory;
        _write = Task.Run(() => {
            if (lines.Length > 0) File.AppendAllText(Path.Combine(directory,"samples.jsonl"), lines+"\n");
            string temp = Path.Combine(directory,"summary.tmp");
            File.WriteAllText(temp,summary);
            File.Move(temp,Path.Combine(directory,"summary.json"),true);
        });
    }
    private void FailWrite()
    {
        Recording = false; Log.RemoveSink(_logs);
        Session.Ui.Toast("Diagnostics write failed: " + _write?.Exception?.GetBaseException().Message,true);
        GD.PushError("Diagnostics write failed: " + _write?.Exception);
    }
    public void StopRecording()
    {
        if (!Recording) return;
        Recording = false; Log.RemoveSink(_logs);
        try
        {
            // Only stop/exit waits for disk; normal recording writes asynchronously.
            if (_write != null && !_write.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Storage writer did not finish within five seconds");
            QueueWrite(true);
            if (!_write!.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Final summary write timed out");
            Session.Ui.Toast("Diagnostics saved · " + OutputDirectory,false);
            GD.Print("DIAGNOSTICS_SAVED " + OutputDirectory);
        }
        catch (Exception ex) { GD.PushError("Diagnostics final write failed: " + ex.Message); }
    }
    private sealed class DiagnosticLogSink : ILogSink
    {
        private readonly Queue<string> _messages = new();
        public long Dropped { get; private set; }
        public void Write(in LogEntry entry)
        {
            if (entry.Level < LogLevel.Warning) return;
            lock (_messages) { if (_messages.Count >= 256) { Dropped++; return; } _messages.Enqueue(entry.Format()); }
        }
        public string[] Drain() { lock (_messages) { var result = _messages.ToArray(); _messages.Clear(); return result; } }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree) StopRecording();
    }
}
