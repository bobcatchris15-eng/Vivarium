using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Vivarium.Game.Camera;
using Vivarium.Game.Render;
using Vivarium.Game.UI;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Owns the running world and everything that presents it. The only path from the client into the
/// simulation is <see cref="SimHost"/> (time) and <see cref="SimHost.Tools"/> (validated actions).
/// </summary>
public partial class GameSession : Node3D
{
    public ContentLibrary Content { get; private set; } = null!;
    public UserSettings Settings { get; private set; } = null!;
    public SimHost? Host { get; private set; }
    public VivariumWorld? World => Host?.World;

    public EnvironmentRig EnvRig { get; private set; } = null!;
    public CameraRig CameraRig { get; private set; } = null!;
    public IslandRenderer Island { get; private set; } = null!;
    public WaterRenderer Water { get; private set; } = null!;
    public PropRenderer Props { get; private set; } = null!;
    public SoilDetailRenderer SoilDetail { get; private set; } = null!;
    public FloraRenderer Flora { get; private set; } = null!;
    public FaunaRenderer Fauna { get; private set; } = null!;
    public OverlayRenderer Overlay { get; private set; } = null!;
    public ToolController Tools { get; private set; } = null!;
    public UiRoot Ui { get; private set; } = null!;

    public AutosaveController? Autosave { get; private set; }
    public string SavesDir => Bridge.UserPath("saves");
    public string AutosavePath => Path.Combine(SavesDir, "autosave" + SaveSystem.Extension);
    public DateTime? LastAutosaveUtc { get; private set; }
    public string? CurrentSaveName { get; set; }

    /// <summary>UI-side trend history (never part of the simulation state).</summary>
    public readonly List<EcosystemStats> History = new();
    private double _lastHistoryDay = -1;
    public event Action? WorldChanged;

    public void Initialize(ContentLibrary content, UserSettings settings)
    {
        Content = content;
        Settings = settings;
        EnvRig = new EnvironmentRig { Name = "Environment" }; AddChild(EnvRig);
        CameraRig = new CameraRig { Name = "CameraRig", Speed = settings.CameraSpeed, Sensitivity = settings.MouseSensitivity, InvertY = settings.InvertY };
        AddChild(CameraRig);
        CameraRig.SpeedChanged += s => { Settings.CameraSpeed = s; Settings.Save(); };
        CameraRig.MediumChanged += under => { EnvRig.SetUnderwater(under); Water.SetUnderwater(under); };
        Island = new IslandRenderer { Name = "Island" }; AddChild(Island);
        Water = new WaterRenderer { Name = "WaterRenderer" }; AddChild(Water);
        Props = new PropRenderer { Name = "PropRenderer" }; AddChild(Props);
        SoilDetail = new SoilDetailRenderer { Name = "SoilDetail" }; AddChild(SoilDetail);
        Flora = new FloraRenderer { Name = "FloraRenderer" }; AddChild(Flora);
        Fauna = new FaunaRenderer { Name = "FaunaRenderer" }; AddChild(Fauna);
        Overlay = new OverlayRenderer { Name = "Overlay" }; AddChild(Overlay);
        Tools = new ToolController { Name = "ToolController" }; AddChild(Tools);
        Tools.Session = this;
        var layer = new CanvasLayer { Name = "UiLayer", Layer = 5 };
        AddChild(layer);
        Ui = new UiRoot { Name = "UiRoot" };
        Ui.Session = this;
        layer.AddChild(Ui);
        ApplySettings();
    }

    public void ApplySettings()
    {
        Settings.Clamp();
        EnvRig.ApplyQuality(Settings.Quality);
        Flora.Quality = Fauna.Quality = Settings.Quality;
        SoilDetail.Quality = Settings.Quality;
        CameraRig.Speed = Settings.CameraSpeed;
        CameraRig.Sensitivity = Settings.MouseSensitivity;
        CameraRig.InvertY = Settings.InvertY;
        if (Autosave != null) { Autosave.Enabled = Settings.AutosaveEnabled; Autosave.IntervalSeconds = Settings.AutosaveMinutes * 60; }
        Ui?.ApplyScale((float)Settings.UiScale);
    }

    // ------------------------------------------------------------------ world lifecycle

    public void StartWorld(VivariumWorld world, string? saveName = null)
    {
        if (Host == null) Host = new SimHost(world); else Host.Swap(world);
        CurrentSaveName = saveName;
        Island.Build(world);
        Water.Build(world);
        Props.Build(world);
        SoilDetail.Camera = CameraRig.Cam;
        SoilDetail.Build(world);
        Flora.Camera = Fauna.Camera = CameraRig.Cam;
        Flora.Build(world);
        Fauna.Build(world);
        Overlay.Bind(world);
        CameraRig.World = world;
        Tools.Bind(world);
        Autosave ??= new AutosaveController(AutosavePath);
        Autosave.Path = AutosavePath;
        Autosave.Enabled = Settings.AutosaveEnabled;
        Autosave.IntervalSeconds = Settings.AutosaveMinutes * 60;
        Autosave.Completed -= OnAutosaved;
        Autosave.Completed += OnAutosaved;
        History.Clear(); _lastHistoryDay = -1;
        Log.Info(LogCategory.App, $"World started: '{world.Descriptor.Name}' seed {world.Seed}, day {world.Clock.BioDays:0.0}.");
        WorldChanged?.Invoke();
    }

    private void OnAutosaved(SaveResult r)
    {
        if (r.Ok) { LastAutosaveUtc = DateTime.UtcNow; Ui?.Toast("Autosaved", false); }
        else Ui?.Toast("Autosave failed: " + r.Message, true);
    }

    public VivariumWorld CreateWorld(WorldDescriptor d)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = VivariumWorld.Create(Content, d);
        Log.Info(LogCategory.World, $"World generated in {sw.ElapsedMilliseconds} ms.");
        return w;
    }

    public SaveResult SaveTo(string name)
    {
        if (World == null) return new SaveResult { Ok = false, Message = "no world" };
        var safe = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim();
        if (safe.Length == 0) safe = "vivarium";
        var r = SaveSystem.Save(World, Path.Combine(SavesDir, safe + SaveSystem.Extension));
        if (r.Ok) CurrentSaveName = safe;
        return r;
    }

    /// <summary>Staged load: the running world is replaced only if the file loads and validates completely.</summary>
    public LoadResult LoadFrom(string path)
    {
        var r = SaveSystem.Load(Content, path);
        if (r.Ok && r.World != null) StartWorld(r.World, Path.GetFileNameWithoutExtension(path));
        return r;
    }

    public IEnumerable<(string Path, SaveManifest? Manifest, string Note)> ListSaves()
    {
        if (!Directory.Exists(SavesDir)) yield break;
        foreach (var f in Directory.GetFiles(SavesDir, "*" + SaveSystem.Extension).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var (m, _, note) = SaveSystem.Inspect(f);
            yield return (f, m, note);
        }
    }

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Session");
        if (Host == null) return;
        Host.Advance(delta);
        Autosave?.Tick(Host.World, delta);
        var w = Host.World;
        if (w.Clock.BioDays - _lastHistoryDay >= 1.0 / 24 || _lastHistoryDay < 0)
        {
            _lastHistoryDay = w.Clock.BioDays;
            History.Add(EcosystemStatistics.Compute(w));
            if (History.Count > 400) History.RemoveAt(0);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            try { Autosave?.Flush(TimeSpan.FromSeconds(5)); } catch { }
        }
    }
}
