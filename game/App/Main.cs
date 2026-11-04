using System;
using System.IO;
using System.Linq;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Entry point. Continues the autosaved vivarium or grows the default island.
/// </summary>
public partial class Main : Node3D
{
    private FileLogSink? _fileLog;
    public static string[] UserArgs = Array.Empty<string>();
    public GameSession? Session { get; private set; }

    public override async void _Ready()
    {
        UserArgs = OS.GetCmdlineUserArgs();
        // a display piece must not stutter: avoid blocking full garbage collections during steady running
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        SetupLogging();
        Log.Info(LogCategory.App, $"Startup {AppVersion.Describe()} on {OS.GetName()} {OS.GetVersion()}, Godot {Engine.GetVersionInfo()["string"]}, renderer {RenderingServer.GetVideoAdapterName()}.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(LogCategory.App, "Unhandled exception: " + e.ExceptionObject);
        GetWindow().Title = $"{AppVersion.ProductName} {AppVersion.Application}";

        ContentLibrary content;
        try { content = ContentLoader.Load(new GodotContentSource()); }
        catch (ContentValidationException ex)
        {
            Log.Fatal(LogCategory.Content, ex.Message);
            ShowFatal("The vivarium content failed validation:\n\n" + string.Join("\n", ex.Errors.Take(12)));
            if (UserArgs.Length > 0) GetTree().Quit(3);
            return;
        }

        if (UserArgs.Contains("--boot-test")) { BootTest.Run(this, content); return; }
        if (ArgAfter("--licenses") is { } licenseFile) { WriteLicenses(licenseFile); GetTree().Quit(0); return; }
        var settings = UserSettings.Load();
        if (UserArgs.Contains("--soak")) { settings.Transient = true; settings.AutosaveEnabled = true; settings.AutosaveMinutes = 5; settings.ShowHelpOnStart = false; }
        if (UserArgs.Contains("--diagnostics-check")) { settings.Transient = true; settings.AutosaveEnabled = false; settings.ShowHelpOnStart = false; }

        Session = new GameSession { Name = "Session" };
        AddChild(Session);
        Session.Initialize(content, settings);
        {
            Session.Diagnostics = new LiveDiagnostics { Name = "LiveDiagnostics", Session = Session,
                AutoStart = UserArgs.Contains("--diagnostics") || UserArgs.Contains("--diagnostics-check") || UserArgs.Contains("--soak"),
                RequestedDirectory = ArgAfter("--diagnostics") ?? (ArgAfter("--soak") is { } soakOutput ? Path.Combine(soakOutput, "diagnostics") : null), CheckMode = UserArgs.Contains("--diagnostics-check") };
            AddChild(Session.Diagnostics);
        }

        // Paint feedback before disk/decompression/baseline reconstruction starts.
        var loadingLayer = new CanvasLayer { Layer = 100 };
        var loadingPanel = new ColorRect { Color = new Color(.035f,.055f,.045f,.96f), MouseFilter = Control.MouseFilterEnum.Stop };
        loadingPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var loadingText = new Label { Text = "Opening your island...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        loadingText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        loadingText.AddThemeFontSizeOverride("font_size", 24);
        var progressBar = new ProgressBar { MaxValue = 13, ShowPercentage = false };
        progressBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        progressBar.OffsetLeft = -220; progressBar.OffsetRight = 220; progressBar.OffsetTop = 45; progressBar.OffsetBottom = 57;
        loadingPanel.AddChild(loadingText); loadingPanel.AddChild(progressBar);
        loadingLayer.AddChild(loadingPanel); AddChild(loadingLayer);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            if (ArgAfter("--loading-preview") is { } previewDirectory)
            {
                Directory.CreateDirectory(previewDirectory);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(previewDirectory,"loading.png"));
            }
            string autosave = ArgAfter("--load") ?? Session.AutosavePath;
            LoadResult? restored = null;
            if ((!UserArgs.Contains("--diagnostics-check") && !UserArgs.Contains("--soak") || ArgAfter("--load") != null) && File.Exists(autosave))
                restored = await System.Threading.Tasks.Task.Run(() => SaveSystem.Load(content, autosave));
            VivariumWorld world;
            string? saveName = null;
            if (restored is { Ok: true, World: not null }) { world = restored.World; saveName = Path.GetFileNameWithoutExtension(autosave); }
            else
            {
                if (restored != null) Log.Warn(LogCategory.Persistence, "Autosave could not be loaded: " + restored.Message);
                loadingText.Text = "Growing a new island...";
                var descriptor = content.PresetOrThrow(ArgAfter("--preset") ?? "default").Clone();
                // The guardian's model is the largest asset in the game. Its
                // archetype follows from the descriptor, so the load can start
                // while the world is still being generated.
                PreloadGuardians.Begin(VivariumWorld.GuardianArchetypeFor(descriptor));
                if (UserArgs.Contains("--soak"))
                {
                    descriptor.Seed = ulong.TryParse(ArgAfter("--seed"), out var soakSeed) ? soakSeed : 804584441UL;
                    descriptor.Name += " - bounded-work soak";
                }
                world = await System.Threading.Tasks.Task.Run(() => VivariumWorld.Create(content, descriptor,warmupSurfaceWater:false));
            }
            await Session.StartWorldAsync(world, saveName, stage => {
                progressBar.Value++;
                loadingText.Text = world.Descriptor.Name + "\nPreparing your island - " + stage;
            });
            if (ArgAfter("--loading-preview") is { } completedDirectory)
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(completedDirectory,"preparing.png"));
            if (ArgAfter("--soak") is { } soakDirectory)
            {
                Directory.CreateDirectory(soakDirectory);
                if (world.Flora.Count == 0 || world.Fauna.Count == 0 || world.Water.Springs.Count == 0)
                    throw new InvalidOperationException("Soak requires seeded flora, fauna and a spring.");
                world.Clock.Paused = false;
                world.Clock.SetSpeedIndex(Vivarium.Sim.Time.SimClock.DefaultSpeedIndex);
                var saved = SaveSystem.Save(world, Path.Combine(soakDirectory, "initial.vivsave"));
                if (!saved.Ok) throw new InvalidOperationException(saved.Message);
                File.WriteAllText(Path.Combine(soakDirectory, "launch.json"), System.Text.Json.JsonSerializer.Serialize(new {
                    started_utc = DateTime.UtcNow, process_id = System.Environment.ProcessId,
                    world = world.Descriptor.Name, seed = world.Seed, flora = world.Flora.Count, fauna = world.Fauna.Count,
                    springs = world.Water.Springs.Select(s => new { s.X, s.Z, litres_per_hour = s.Discharge * 3600000 }),
                    autosave = Session.AutosavePath, diagnostics = Session.Diagnostics?.OutputDirectory,
                    settings_transient = settings.Transient, speed = world.Clock.SpeedMultiplier
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                GD.Print("SOAK_READY " + soakDirectory);
            }
            Session.Ui.Toast($"Island ready   day {world.Clock.BioDays:0.0}. Plant detail continues loading in the background.", false);
        }
        catch (Exception ex) { Log.Error(LogCategory.App, "Island loading failed", ex); ShowFatal("Island loading failed: " + ex.Message); }
        finally { loadingLayer.QueueFree(); }
        if (ArgAfter("--loading-preview") is { } readyDirectory)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(Path.Combine(readyDirectory,"ready.png"));
        }
    }

    public static string? ArgAfter(string flag)
    {
        int i = Array.IndexOf(UserArgs, flag);
        if (i < 0) return null;
        return i + 1 < UserArgs.Length && !UserArgs[i + 1].StartsWith("--", StringComparison.Ordinal) ? UserArgs[i + 1] : Bridge.UserPath("test-output");
    }

    /// <summary>Writes the engine's own license + third-party component inventory (exactly what is bundled).</summary>
    private static void WriteLicenses(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Godot Engine license\n");
        sb.AppendLine(Engine.GetLicenseText());
        sb.AppendLine("\n# Third-party components bundled with the Godot Engine\n");
        foreach (var info in Engine.GetCopyrightInfo())
        {
            sb.AppendLine($"## {info["name"]}");
            foreach (var part in (Godot.Collections.Array)info["parts"])
            {
                var d = (Godot.Collections.Dictionary)part;
                foreach (var c in (Godot.Collections.Array)d["copyright"]) sb.AppendLine($"Copyright {c}");
                sb.AppendLine($"License: {d["license"]}\n");
            }
        }
        sb.AppendLine("\n# License texts\n");
        foreach (var (name, text) in Engine.GetLicenseInfo()) sb.AppendLine($"## {name}\n\n{text}\n");
        File.WriteAllText(path, sb.ToString());
    }

    private void SetupLogging()
    {
        try
        {
            string path = Bridge.UserPath("logs/vivarium.log");
            _fileLog = new FileLogSink(path);
            Log.AddSink(_fileLog);
            Log.AddSink(new GodotConsoleSink());
        }
        catch (Exception ex) { GD.PushWarning($"File logging unavailable: {ex.Message}"); }
    }

    private void ShowFatal(string text)
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = 40; label.OffsetTop = 40; label.OffsetRight = -40;
        layer.AddChild(label);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
        {
            Log.Info(LogCategory.App, "Shutdown.");
            Log.Flush();
            _fileLog?.Dispose();
            if (_fileLog != null) { Log.RemoveSink(_fileLog); _fileLog = null; }
        }
    }
}

/// <summary>Mirrors warnings and errors to the Godot console (visible when run from a terminal).</summary>
public sealed class GodotConsoleSink : ILogSink
{
    public void Write(in LogEntry e)
    {
        if (e.Level >= LogLevel.Error) GD.PrintErr(e.Format());
        else if (e.Level >= LogLevel.Warning || OS.IsStdOutVerbose()) GD.Print(e.Format());
    }
}

/// <summary>Headless service bootstrap: content, world, clock, save/load round trip, clean shutdown.</summary>
public static class BootTest
{
    public static void Run(Node host, ContentLibrary content)
    {
        int code = 0;
        try
        {
            var w = VivariumWorld.Create(content, content.PresetOrThrow("default"),warmupSurfaceWater:false);
            var sim = new Vivarium.Sim.Time.SimHost(w);
            for (int i = 0; i < 120; i++) sim.Advance(1 / 60.0);
            var problems = w.CheckInvariants();
            string path = Bridge.UserPath("tests/boot" + SaveSystem.Extension);
            var save = SaveSystem.Save(w, path);
            var load = SaveSystem.Load(content, path);
            bool same = load.Ok && WorldSerializer.Digest(load.World!) == WorldSerializer.Digest(w);
            GD.Print($"VIVARIUM_BOOT version={AppVersion.Application} schema={AppVersion.SaveSchema} ticks={w.Clock.Tick} flora={w.Flora.Count} fauna={w.Fauna.Count} save={save.Ok} load={load.Ok} roundtrip={same} invariants={problems.Count}");
            if (!save.Ok || !load.Ok || !same || problems.Count > 0 || w.Clock.Tick == 0) code = 1;
        }
        catch (Exception ex) { Log.Fatal(LogCategory.App, "Boot test failed: " + ex); code = 2; }
        Log.Info(LogCategory.App, "Shutdown.");
        Log.Flush();
        GD.Print(code == 0 ? "VIVARIUM_BOOT_OK" : "VIVARIUM_BOOT_FAILED");
        host.GetTree().Quit(code);
    }
}
