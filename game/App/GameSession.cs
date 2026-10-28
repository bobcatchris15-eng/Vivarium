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
    public WaterSurfaceRenderer WaterFluid { get; private set; } = null!;
    public PropRenderer Props { get; private set; } = null!;
    public PilotTreeRenderer PilotTree { get; private set; } = null!;
    public SoilDetailRenderer SoilDetail { get; private set; } = null!;
    public AmbientGroundCoverRenderer AmbientGround { get; private set; } = null!;
    public CoverageRenderer Coverage { get; private set; } = null!;
    public FloraRenderer Flora { get; private set; } = null!;
public FaunaRenderer Fauna { get; private set; } = null!;
    public GuardianRenderer Guardian { get; private set; } = null!;
    public OverlayRenderer Overlay { get; private set; } = null!;
    public ToolController Tools { get; private set; } = null!;
    public UiRoot Ui { get; private set; } = null!;

    public LiveDiagnostics? Diagnostics { get; set; }
    public AutosaveController? Autosave { get; private set; }
    public string SavesDir => Main.ArgAfter("--soak") is { } soak ? Path.Combine(soak, "saves") : Bridge.UserPath("saves");
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
        Water = new WaterRenderer { Name = "WaterRenderer", Lighting = EnvRig }; AddChild(Water);
        WaterFluid = new WaterSurfaceRenderer { Name = "WaterSurfaceRenderer", Lighting = EnvRig }; AddChild(WaterFluid);
        Props = new PropRenderer { Name = "PropRenderer" }; AddChild(Props);
        PilotTree = new PilotTreeRenderer { Name = "PilotTreeRenderer" }; AddChild(PilotTree);
        SoilDetail = new SoilDetailRenderer { Name = "SoilDetail" }; AddChild(SoilDetail);
        AmbientGround = new AmbientGroundCoverRenderer { Name = "AmbientGroundCover" }; AddChild(AmbientGround);
        Coverage = new CoverageRenderer { Name = "Coverage" }; AddChild(Coverage);
        Flora = new FloraRenderer { Name = "FloraRenderer" }; AddChild(Flora);
Fauna = new FaunaRenderer { Name = "FaunaRenderer" }; AddChild(Fauna);
        Guardian = new GuardianRenderer { Name = "GuardianRenderer", Vegetation = Flora }; AddChild(Guardian);
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
        // One production presentation; legacy saved tiers are ignored. Benchmarks
        // retain explicit diagnostic controls without exposing player presets.
        if (!Main.UserArgs.Contains("--perf-test")) Settings.Quality = 1;
        EnvRig.ApplyQuality(Settings.Quality);
        EnvRig.ApplyIndirectLightingDetail(Settings.FineIndirectLighting);
        EnvRig.Sun.ShadowEnabled &= Settings.DynamicShadows;
        EnvRig.Env.SdfgiEnabled &= Settings.GlobalIllumination;
        EnvRig.Env.SsilEnabled &= Settings.GlobalIllumination;
        EnvRig.Env.SsaoEnabled &= Settings.AmbientOcclusion;
        EnvRig.VolumetricMistEnabled = Settings.VolumetricMist;
        EnvRig.Env.VolumetricFogEnabled &= Settings.VolumetricMist;
        GetViewport().Scaling3DScale = (float)Settings.RenderScale;
        Flora.ReducedDistantPlants = Settings.ReducedDistantPlants;
        CameraRig.DistanceBlur = Settings.DistanceBlur;
        Bridge.SetSimpleFoliageLighting(Settings.SimpleFoliageLighting);
        if (Settings.DistanceBlur)
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Medium, true);
        Flora.Quality = Fauna.Quality = Guardian.Quality = Settings.Quality;
        SoilDetail.Quality = Settings.Quality;
        AmbientGround.Quality = Settings.Quality;
        Coverage.Quality = Settings.Quality;
        CameraRig.Speed = Settings.CameraSpeed;
        CameraRig.Sensitivity = Settings.MouseSensitivity;
        CameraRig.InvertY = Settings.InvertY;
        if (Autosave != null) { Autosave.Enabled = Settings.AutosaveEnabled; Autosave.IntervalSeconds = Settings.AutosaveMinutes * 60; }
        Ui?.ApplyScale((float)Settings.UiScale);
    }

    // ------------------------------------------------------------------ world lifecycle

    public bool LoadingWorld { get; private set; }
    /// <summary>Wall time of each render stage of the most recent world start, in order.</summary>
    public readonly List<(string Stage, double Ms)> StageTimings = new();
    public void StartWorld(VivariumWorld world, string? saveName = null)
    {
        foreach (var _ in StartWorldStages(world, saveName)) { }
    }
    public async System.Threading.Tasks.Task StartWorldAsync(VivariumWorld world, string? saveName, Action<string> progress)
    {
        LoadingWorld = true;
        bool blocked = CameraRig.KeyboardBlocked;
        CameraRig.KeyboardBlocked = true; Tools.SetProcess(false);
        try
        {
            foreach (var stage in StartWorldStages(world, saveName))
            {
                progress(stage);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        finally { LoadingWorld = false; CameraRig.KeyboardBlocked = blocked; Tools.SetProcess(true); }
    }
    private IEnumerable<string> StartWorldStages(VivariumWorld world, string? saveName)
    {
        var startup = System.Diagnostics.Stopwatch.StartNew();
        StageTimings.Clear();
        void Stage(string name, Action build)
        {
            GD.Print($"WORLD_RENDER_STAGE_BEGIN {name} elapsed_ms={startup.ElapsedMilliseconds}");
            long begin = startup.ElapsedMilliseconds;
            build();
            double stageMs = startup.ElapsedMilliseconds - begin;
            StageTimings.Add((name, stageMs));
            GD.Print($"WORLD_RENDER_STAGE_END {name} stage_ms={startup.ElapsedMilliseconds - begin} elapsed_ms={startup.ElapsedMilliseconds}");
        }
        if (Host == null) Host = new SimHost(world); else Host.Swap(world);
        SimHost.StageTimed = FrameProfiler.Record;
        Vivarium.Sim.Water.SurfaceLiquid.StageTimed = FrameProfiler.Record;
        Vivarium.Sim.Coverage.Aquatic.AquaticSystem.StageTimed = FrameProfiler.Record;
        Vivarium.Sim.Coverage.Aquatic.SurfaceFloatRules.BatchTimed = FrameProfiler.Record;
        Host.Tools.SurfaceEdited += (centre, radius) =>
        {
            AmbientGround.Invalidate(centre, radius);
            SoilDetail.LoamDetails.Invalidate(centre, radius);
            SoilDetail.Invalidate();
        };
        // Leave room for camera and rendering after every fixed simulation tick.
        world.Scheduler.WallBudgetMs = 6;
        world.CoverageSystem.AsyncMode = true;
        CurrentSaveName = saveName;
        // Camera-local layers may process between loading stages. Start them at
        // the final entry pose rather than rebuilding around the previous island.
        CameraRig.World = world;
        CameraRig.ResetView();
        // The guardian model is the largest asset in the game. Start streaming
        // it now so the island, water, props and pilot tree are prepared while
        // the model loads, instead of waiting for it afterwards.
        if (world.Guardian != null) PreloadGuardians.Begin(world.Guardian.ArchetypeId);
        yield return "Island";
        Stage("Island", () => { EnvRig.ConfigureHabitat(world); Island.Build(world); });
        yield return "Water";
        Stage("Water", () => Water.Build(world));
        yield return "WaterFluid";
        Stage("WaterFluid", () => {world.Liquid.Enable();world.Liquid.AsyncMode=true;WaterFluid.Bind(world);});
        yield return "Props";
        Stage("Props", () => Props.Build(world));
        yield return "PilotTree";
        Stage("PilotTree", () => PilotTree.Build(world));
        SoilDetail.Camera = CameraRig.Cam;
        yield return "SoilDetail";
        Stage("SoilDetail", () => SoilDetail.Build(world, Island));
        while (!SoilDetail.LoamDetails.InitializationComplete)
        {
            yield return "Forest floor";
            SoilDetail.LoamDetails.BuildStartupBatch();
        }
        AmbientGround.Camera = CameraRig.Cam;
        yield return "AmbientGround";
        Stage("AmbientGround", () => AmbientGround.Build(world, Island));
        Coverage.Camera = CameraRig.Cam;
        yield return "Coverage";
        Stage("Coverage", () => Coverage.Build(world));
        Flora.Camera = Fauna.Camera = Guardian.Camera = CameraRig.Cam;
        yield return "Flora";
        Stage("Flora", () => Flora.Build(world));
        yield return "Fauna";
        Stage("Fauna", () => Fauna.Build(world));
        yield return "Guardian";
        Stage("Guardian", () => Guardian.Build(world));
        yield return "Overlay";
        Stage("Overlay", () => Overlay.Bind(world));
        Tools.Bind(world);
        Autosave ??= new AutosaveController(AutosavePath);
        Autosave.Path = AutosavePath;
        Autosave.Enabled = Settings.AutosaveEnabled;
        Autosave.IntervalSeconds = Settings.AutosaveMinutes * 60;
        Autosave.Completed -= OnAutosaved;
        Autosave.Completed += OnAutosaved;
        History.Clear(); _lastHistoryDay = -1;
        world.Scheduler.SystemTimed += (sys, ms) => FrameProfiler.Record("Sys." + sys.Name, ms);
        Log.Info(LogCategory.App, $"World started: '{world.Descriptor.Name}' seed {world.Seed}, day {world.Clock.BioDays:0.0}.");
        yield return "WorldChanged";
        Stage("WorldChanged", () => WorldChanged?.Invoke());
    }

    private void OnAutosaved(SaveResult r)
    {
        if (r.Ok) { LastAutosaveUtc = DateTime.UtcNow; Ui?.Toast("Autosaved", false); }
        else Ui?.Toast("Autosave failed: " + r.Message, true);
    }

    public VivariumWorld CreateWorld(WorldDescriptor d)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = VivariumWorld.Create(Content, d, warmupSurfaceWater:false);
        Log.Info(LogCategory.World, $"World generated in {sw.ElapsedMilliseconds} ms.");
        return w;
    }

    public SaveResult SaveTo(string name)
    {
        if (World == null) return new SaveResult { Ok = false, Message = "no world" };
        World.CoverageSystem.WaitPending();
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

    private ulong _lastWallMicroseconds;
    private bool _previousFramePaused=true;
    /// <summary>Resume a deliberately suspended presentation audit without queuing its wall time.</summary>
    internal void ResetFrameClock()
    {
        _lastWallMicroseconds = Godot.Time.GetTicksUsec();
        _previousFramePaused = true;
    }
    public override void _Process(double delta)
    {
        // Godot may cap process delta after a long frame. Physical input must retain
        // the actual elapsed time rather than silently losing it during a render hitch.
        ulong now=Godot.Time.GetTicksUsec();
        double elapsed=_lastWallMicroseconds==0?delta:(now-_lastWallMicroseconds)/1_000_000.0;
        _lastWallMicroseconds=now;
        using var prof = FrameProfiler.Measure("Session");
        if (Host == null || LoadingWorld) return;
        if(_previousFramePaused)elapsed=0;
        _previousFramePaused=Host.Clock.Paused;
        using (FrameProfiler.Measure("Session.Advance")) Host.Advance(elapsed);

        using (FrameProfiler.Measure("Session.Autosave")) Autosave?.Tick(Host.World, delta);
        var w = Host.World;
        if (w.Clock.BioDays - _lastHistoryDay >= 1.0 / 24 || _lastHistoryDay < 0)
        {
            _lastHistoryDay = w.Clock.BioDays;
            using (FrameProfiler.Measure("Session.Stats")) History.Add(EcosystemStatistics.Compute(w));
            if (History.Count > 400) History.RemoveAt(0);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
        {
            try { World?.Liquid.CompletePending(); } catch { }
            try { World?.CoverageSystem.WaitPending(); } catch { }
            try { Autosave?.Flush(TimeSpan.FromSeconds(5)); } catch { }
        }
    }
}
