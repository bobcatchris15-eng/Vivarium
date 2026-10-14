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
