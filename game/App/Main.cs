using System;
using System.IO;
using System.Linq;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Entry point. Normal launch continues the autosaved vivarium (or grows the default one). Command-line modes
/// (after "--"): --boot-test, --render-test DIR, --smoke DIR, --smoke-reload DIR.
/// </summary>
public partial class Main : Node3D
{
    private FileLogSink? _fileLog;
    public static string[] UserArgs = Array.Empty<string>();
    public GameSession? Session { get; private set; }

    public override void _Ready()
    {
        UserArgs = OS.GetCmdlineUserArgs();
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

        var settings = UserSettings.Load();
        string? testDir = ArgAfter("--smoke") ?? ArgAfter("--smoke-reload") ?? ArgAfter("--render-test");
        if (testDir != null) { settings.AutosaveEnabled = false; settings.ShowHelpOnStart = false; }

        Session = new GameSession { Name = "Session" };
        AddChild(Session);
        Session.Initialize(content, settings);

        if (testDir != null)
        {
            var mode = UserArgs.Contains("--smoke") ? SmokeRunner.Mode.Smoke : UserArgs.Contains("--smoke-reload") ? SmokeRunner.Mode.Reload : SmokeRunner.Mode.Render;
            var runner = new SmokeRunner { Name = "SmokeRunner", Session = Session, OutDir = testDir, RunMode = mode };
            AddChild(runner);
            if (mode != SmokeRunner.Mode.Reload) Session.StartWorld(Session.CreateWorld(content.PresetOrThrow("default")));
            return;
        }

        // persistent world: continue the autosave when there is one
        if (File.Exists(Session.AutosavePath))
        {
            var r = Session.LoadFrom(Session.AutosavePath);
            if (r.Ok) { Session.Ui.Toast($"Welcome back — day {r.World!.Clock.SimDays:0.0} of your vivarium", false); return; }
            Log.Warn(LogCategory.Persistence, "Autosave could not be loaded: " + r.Message);
            Session.Ui.Toast("Your last autosave could not be loaded (" + r.Message + "); growing a fresh vivarium.", true);
        }
        Session.StartWorld(Session.CreateWorld(content.PresetOrThrow("default")));
    }

    public static string? ArgAfter(string flag)
    {
        int i = Array.IndexOf(UserArgs, flag);
        if (i < 0) return null;
        return i + 1 < UserArgs.Length && !UserArgs[i + 1].StartsWith("--", StringComparison.Ordinal) ? UserArgs[i + 1] : Bridge.UserPath("test-output");
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
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
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
            var w = VivariumWorld.Create(content, content.PresetOrThrow("default"));
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
