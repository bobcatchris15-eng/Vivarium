using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Scripted end-to-end runs against the real application (editor or exported build):
///  Smoke  — new world through the UI, camera above/below water, accelerated simulation, every tool family,
///           catalog/stats/settings, save through the UI, quit.
///  Reload — load that save through the UI, verify the exact state digest, keep simulating, quit.
///  Render — screenshot tour + LOD measurement for visual review.
/// Buttons are pressed by emitting their real "pressed" signal, so the ordinary UI code paths run.
/// </summary>
public partial class SmokeRunner : Node
{
    public enum Mode { Smoke, Reload, Render, Perf, Reference }
    public GameSession Session { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public Mode RunMode { get; set; }
    private readonly List<Dictionary<string, object>> _checks = new();
    private readonly Dictionary<string, object> _facts = new();
    private int _errorsAtStart;
    private readonly MemoryLogSink _log = new();

    public override void _Ready()
    {
        Directory.CreateDirectory(OutDir);
        Log.AddSink(_log);
        Session.Tools.PointerEnabled = false;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        int code = 0;
        try
        {
            await Frames(10);
            _errorsAtStart = _log.Count(LogLevel.Error);
            switch (RunMode)
            {
                case Mode.Smoke: await SmokeAsync(); break;
                case Mode.Reload: await ReloadAsync(); break;
                case Mode.Render: await RenderAsync(); break;
                case Mode.Perf: await PerfAsync(); break;
                case Mode.Reference: await ReferenceAsync(); break;
            }
            Check("no errors logged", _log.Count(LogLevel.Error) == _errorsAtStart, string.Join(" | ", _log.Snapshot().Where(e => e.Level >= LogLevel.Error).Select(e => e.Message).Take(5)));
        }
        catch (Exception ex)
        {
            Check("runner completed without exception", false, ex.ToString());
            code = 2;
        }
        bool ok = _checks.All(c => (bool)c["ok"]);
        if (code == 0 && !ok) code = 1;
        var report = new Dictionary<string, object>
        {
            ["mode"] = RunMode.ToString(), ["version"] = AppVersion.Application, ["ok"] = ok && code == 0,
            ["checks"] = _checks, ["facts"] = _facts, ["exported"] = OS.HasFeature("template"),
        };
        string name = RunMode switch { Mode.Smoke => "smoke_result.json", Mode.Reload => "reload_result.json", Mode.Perf => "perf_report.json", Mode.Reference => "reference_report.json", _ => "render_report.json" };
        File.WriteAllText(Path.Combine(OutDir, name), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"VIVARIUM_{RunMode.ToString().ToUpperInvariant()}_{(code == 0 ? "OK" : "FAILED")} checks={_checks.Count} failed={_checks.Count(c => !(bool)c["ok"])}");
        foreach (var c in _checks.Where(c => !(bool)c["ok"])) GD.PrintErr($"  FAILED: {c["name"]}: {c["detail"]}");
        Log.RemoveSink(_log);
        await Frames(2);
        GetTree().Quit(code);
    }

    // ------------------------------------------------------------------ helpers

    private void Check(string name, bool ok, string detail = "")
    {
        _checks.Add(new Dictionary<string, object> { ["name"] = name, ["ok"] = ok, ["detail"] = detail });
        Log.Info(LogCategory.Test, $"{(ok ? "PASS" : "FAIL")} {name} {detail}");
    }

    /// <summary>Waits real time (headless frames can be microseconds long).</summary>
    private async Task Seconds(double s) { ulong end = Time.GetTicksMsec() + (ulong)(s * 1000); while (Time.GetTicksMsec() < end) await Frames(1); }

    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private T Find<T>(string name) where T : Node =>
        Session.Ui.FindChild(name, true, false) as T ?? throw new InvalidOperationException($"UI control '{name}' not found");

    private async Task Press(string buttonName, int frames = 3)
    {
        Log.Info(LogCategory.Test, "press " + buttonName);
        var b = Find<BaseButton>(buttonName);
        if (b.ToggleMode) b.ButtonPressed = !b.ButtonPressed;
        b.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(frames);
    }

    private VivariumWorld W => Session.World ?? throw new InvalidOperationException("no world");

    /// <summary>World hit straight below a point (what a click there would produce).</summary>
    private WorldHit HitAt(Vec2 p, bool water = false) => Selection.Raycast(W, new Vec3(p.X, 20, p.Z), new Vec3(0, -1, 0), water);

    private async Task Screenshot(string name)
    {
        if (DisplayServer.GetName() == "headless") { _facts["screenshot_" + name] = "skipped (headless)"; return; }
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(OutDir, name + ".png");
        img.SavePng(path);
        _facts["screenshot_" + name] = path;
    }

    private Vec2 DeepestWater() { var c = W.Grid.DomainCells.OrderByDescending(i => W.Water.Depth[i]).First(); return W.Grid.CellCenter(c); }

    private Vec2 DryLand(Func<Vec2, bool>? extra = null) =>
        W.Grid.DomainCells.Select(c => W.Grid.CellCenter(c)).First(p => !W.Water.IsWet(p) && W.Domain.ContainsDisc(p, 1.5) && double.IsNaN(W.Props.PropTopAt(p)) && W.Props.GravelAt(p) == null && (extra?.Invoke(p) ?? true) && W.Water.DepthAt(p + new Vec2(0.6, 0)) < 0.001);

    // ------------------------------------------------------------------ smoke

    private async Task SmokeAsync()
    {
        // new world through the ordinary UI
        await Press("NewWorldButton");
        Find<LineEdit>("SeedEdit").Text = "20260923";
        await Press("CreateWorldButton", 10);
        Check("new world created from UI", Session.World != null && W.Flora.Count > 0 && W.Fauna.Count > 0, $"flora {W.Flora.Count}, fauna {W.Fauna.Count}");

        // camera: exterior view, dive under water, surface again
        var cam = Session.CameraRig;
        cam.LookAtPoint(new Vector3(0, 7.5f, 13.5f), Vector3.Zero);
        await Frames(5);
        var deep = DeepestWater();
        double surf = W.Water.SurfaceAt(deep);
        cam.LookAtPoint(new Vector3((float)deep.X, (float)surf + 0.5f, (float)deep.Z + 0.8f), new Vector3((float)deep.X, (float)surf, (float)deep.Z));
        await Frames(5);
        bool above = !cam.Medium.Underwater;
        cam.Position = new Vector3((float)deep.X, (float)surf - 0.08f, (float)deep.Z);
        await Seconds(0.6);
        bool under = cam.Medium.Underwater && Session.EnvRig.UnderwaterVisual;
        cam.Position = new Vector3((float)deep.X, (float)surf + 0.4f, (float)deep.Z);
        await Seconds(0.6);
        Check("camera crosses water surface", above && under && !cam.Medium.Underwater, $"transitions {cam.Medium.Transitions}");

        // accelerated simulation through the clock controls
        long t0 = W.Clock.Tick;
        for (int i = 0; i < 3; i++) await Press("FasterButton", 1);
        Check("speed control", W.Clock.SpeedMultiplier == 8, $"{W.Clock.SpeedMultiplier}x");
        await Frames(120);
        Check("simulation advances", W.Clock.Tick > t0 + 50, $"{W.Clock.Tick - t0} ticks");
        await Press("PauseButton");
        long tp = W.Clock.Tick;
        await Frames(30);
        Check("pause stops simulated time", W.Clock.Tick == tp && W.Clock.Paused);
        await Press("PauseButton");

        // every tool family through the palette + the tool controller's click path
        var tools = Session.Tools;
        var land = DryLand();
        await Press("Tool_Nutrients");
        double n0 = W.Fields.Nutrients.Sample(land);
        var r = tools.ApplyAt(HitAt(land));
        Check("nutrient tool", r?.Ok == true && W.Fields.Nutrients.Sample(land) > n0, r?.Message ?? "");

        await Press("Tool_PlaceRock");
        int rocks = W.Props.Rocks.Count;
        var rockSpot = DryLand(p => Vec2.Distance(p, land) > 1.2);
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(HitAt(rockSpot));
        Check("place rock", r?.Ok == true && W.Props.Rocks.Count == rocks + 1, r?.Message ?? "");

        await Press("Tool_PlaceGravel");
        var gravelSpot = DryLand(p => Vec2.Distance(p, land) > 1.5 && Vec2.Distance(p, rockSpot) > 1.5);
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(HitAt(gravelSpot));
        Check("place gravel", r?.Ok == true && W.SubstrateAt(gravelSpot) == Vivarium.Sim.Content.Substrate.Gravel, r?.Message ?? "");

        await Press("Tool_PlaceLog");
        var logSpot = DryLand(p => Vec2.Distance(p, land) > 2 && Vec2.Distance(p, rockSpot) > 2 && Vec2.Distance(p, gravelSpot) > 2 && W.Domain.ContainsDisc(p, 2));
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(HitAt(logSpot));
        Check("place log", r?.Ok == true, r?.Message ?? "");

        await Press("Tool_IntroduceFlora");
        tools.FloraSpecies = "carpet_moss";
        var mossSpot = W.Grid.DomainCells.Select(c => W.Grid.CellCenter(c)).First(p => W.FloraSystem.CanEstablish(W.Content.FloraOrThrow("carpet_moss"), p, out _));
        int flora = W.Flora.Count;
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(HitAt(mossSpot));
        Check("introduce flora", r?.Ok == true && W.Flora.Count == flora + 1, r?.Message ?? "");
        // one of each new kind of organism: fern, fungus, slime mold (where their habitat allows)
        foreach (var newId in new[] { "fern", "bonnet_mushroom", "slime_mold" })
        {
            tools.FloraSpecies = newId;
            var nsp = W.Content.FloraOrThrow(newId);
            var spot = W.Grid.DomainCells.Select(c => W.Grid.CellCenter(c)).Cast<Vec2?>().FirstOrDefault(p => W.FloraSystem.CanEstablish(nsp, p!.Value, out _));
            int count0 = W.Flora.Count;
            r = spot.HasValue ? tools.ApplyAt(HitAt(spot.Value)) : null;
            Check($"introduce {newId}", r?.Ok == true && W.Flora.Count == count0 + 1, r?.Message ?? "no valid habitat found");
        }

        await Press("Tool_IntroduceFauna");
        tools.FaunaSpecies = "shrimp";
        int shrimp = W.Fauna.CountOf("shrimp");
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(HitAt(DeepestWater(), true));
        Check("introduce fauna", r?.Ok == true && W.Fauna.CountOf("shrimp") > shrimp, r?.Message ?? "");

        await Press("Tool_Poke");
        var critter = W.Fauna.Items.First(f => f.SpeciesId == "springtail");
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(new WorldHit(HitKind.Fauna, critter.Id, critter.Position, 1));
        Check("pokin' stick", r?.Ok == true && critter.DisturbedUntil > W.Clock.SimSeconds, r?.Message ?? "");

        await Press("Tool_Grab");
        var fish = W.Fauna.Items.First(f => f.SpeciesId == "shrimp");
        var id = fish.Id; var genome = fish.GenomeId;
        tools.ApplyAt(new WorldHit(HitKind.Fauna, id, fish.Position, 1));
        bool held = W.Fauna.Get(id)?.Grabbed == true;
        var badRelease = tools.ApplyAt(HitAt(land));
        var goodRelease = tools.ApplyAt(HitAt(DeepestWater() + new Vec2(0.1, 0), true));
        var after = W.Fauna.Get(id);
        Check("grab and release critter", held && badRelease?.Ok == false && goodRelease?.Ok == true && after != null && !after.Grabbed && after.GenomeId == genome,
            $"{badRelease?.Message} / {goodRelease?.Message}");

        await Press("Tool_RemovePlant");
        var plant = W.Flora.Items.Last();
        Log.Info(LogCategory.Test, "apply " + tools.Current);
        r = tools.ApplyAt(new WorldHit(HitKind.Flora, plant.Id, new Vec3(plant.X, W.GroundHeight(plant.Position), plant.Z), 1));
        Check("pick plant", r?.Ok == true && W.Flora.Get(plant.Id) == null, r?.Message ?? "");

        await Press("Tool_Poke");
        var bug = W.Fauna.Items.First(f => f.SpeciesId == "pill_bug" && !W.FaunaSystem.IsCurled(f));
        r = tools.ApplyAt(new WorldHit(HitKind.Fauna, bug.Id, bug.Position, 1));
        Check("pill bug rolls up when poked", r?.Ok == true && W.FaunaSystem.IsCurled(bug), r?.Message ?? "");

        // terrain and water tools (brush tools: each click is one dab; the stroke ends on the next frame)
        var ground = DryLand(p => W.Props.DistanceToFeature(p, "rock") > 1.5 && W.Props.DistanceToFeature(p, "log") > 1.5 && Vec2.Distance(p, gravelSpot) > 2 && Vec2.Distance(p, land) > 1);
        await Press("Tool_TerrainRaise");
        double h0 = W.SurfaceHeight(ground);
        int tv = W.Terrain.Version;
        r = tools.ApplyAt(HitAt(ground));
        await Frames(6);
        Check("raise terrain", r?.Ok == true && W.SurfaceHeight(ground) > h0 && W.Terrain.Version > tv, $"{r?.Message} Δ{W.SurfaceHeight(ground) - h0:0.0000} m");
        await Press("Tool_TerrainLower");
        tools.ApplyAt(HitAt(ground)); await Frames(3);
        tools.ApplyAt(HitAt(ground)); await Frames(3);
        await Press("Tool_TerrainSmooth");
        var smooth = tools.ApplyAt(HitAt(ground)); await Frames(3);
        Check("lower and smooth terrain", W.SurfaceHeight(ground) < h0 && smooth?.Ok == true, $"{smooth?.Message} Δ{W.SurfaceHeight(ground) - h0:0.0000} m");

        await Press("Tool_PourWater");
        double inflow = W.Water.Budget.ToolInflow;
        r = tools.ApplyAt(HitAt(ground)); await Frames(3);
        Check("pour water", r?.Ok == true && W.Water.Budget.ToolInflow > inflow, r?.Message ?? "");
        await Press("Tool_DrainWater");
        double removal = W.Water.Budget.ToolRemoval;
        r = tools.ApplyAt(HitAt(DeepestWater(), true)); await Frames(3);
        Check("soak up water", r?.Ok == true && W.Water.Budget.ToolRemoval > removal, r?.Message ?? "");
        await Press("Tool_Spring");
        int springs = W.Water.Springs.Count;
        var add = tools.ApplyAt(HitAt(ground));
        int withNew = W.Water.Springs.Count;
        var remove = tools.ApplyAt(HitAt(ground));
        Check("add and remove a spring", add?.Ok == true && remove?.Ok == true && withNew == springs + 1 && W.Water.Springs.Count == springs, $"{add?.Message} / {remove?.Message}");

        await Press("Tool_Select");
        var sel = W.Fauna.Items.First();
        tools.ApplyAt(new WorldHit(HitKind.Fauna, sel.Id, sel.Position, 1));
        await Frames(15);
        await Press("Tab_Genome", 15);
        Check("inspector shows genome", Session.Ui.Inspector.BodyText.Contains("Genotype"), "");
        await Press("Tab_Lineage", 15);
        await Press("Tab_Details", 5);

        // panels
        await Press("CatalogButton", 10);
        Check("catalog lists every species", Session.Content.Flora.All(f => Session.Ui.FindChild("CatalogEntry_" + f.Id, true, false) != null) && Session.Content.Fauna.All(f => Session.Ui.FindChild("CatalogEntry_" + f.Id, true, false) != null));
        await Press("StatsButton", 10);
        await Press("DebugButton", 10);
        await Press("SettingsButton", 5);
        var q = Find<OptionButton>("QualityPicker");
        W.Clock.Paused = true;   // compare state at an identical tick
        string before = WorldSerializer.Digest(W);
        foreach (int tier in new[] { 2, 0, 1 }) { q.Select(tier); q.EmitSignal(OptionButton.SignalName.ItemSelected, tier); await Frames(3); }
        Check("quality changes leave simulation untouched", WorldSerializer.Digest(W) == before);
        W.Clock.Paused = false;
        var growth = Find<HSlider>("GrowthSpeedSlider");
        double bio0 = W.Clock.BioAcceleration;
        growth.Value = 15;   // emits value_changed like a drag would
        await Frames(2);
        bool grew = Math.Abs(W.Clock.BioAcceleration - 15) < 1e-9 && Math.Abs(W.Descriptor.BioAcceleration - 15) < 1e-9;
        Session.Host!.Tools.SetBioAcceleration(bio0);   // the slider snaps to 0.1 steps; restore the exact default
        Check("growth speed setting drives the biological clock", grew && Math.Abs(W.Clock.BioAcceleration - bio0) < 1e-9, $"default ×{bio0:0.00}");
        await Press("HelpButton", 5);
        await Press("Win_Help_Close", 3);

        // continued simulation after all interventions
        long t1 = W.Clock.Tick;
        await Frames(90);
        var inv = W.CheckInvariants();
        Check("ecosystem keeps simulating after interventions", W.Clock.Tick > t1 && inv.Count == 0, string.Join("; ", inv.Take(3)));

        // save through the UI
        await Press("PauseButton");
        await Press("SaveButton", 5);
        Find<LineEdit>("SaveNameEdit").Text = "smoke";
        await Press("SaveNowButton", 5);
        string path = Path.Combine(Session.SavesDir, "smoke" + SaveSystem.Extension);
        var (m, compat, _) = SaveSystem.Inspect(path);
        Check("save through UI", m != null && compat == SaveCompatibility.Current, path);
        _facts["save_path"] = path;
        _facts["save_digest"] = m?.StateDigest ?? "";
        _facts["tick"] = W.Clock.Tick;
        await Screenshot("smoke_final");
    }

    // ------------------------------------------------------------------ reload

    private async Task ReloadAsync()
    {
        var prior = JsonDocument.Parse(File.ReadAllText(Path.Combine(OutDir, "smoke_result.json"))).RootElement.GetProperty("facts");
        string expected = prior.GetProperty("save_digest").GetString() ?? "";
        await Press("SaveButton", 10);
        // the smoke save is the newest file, i.e. the first Load button
        await Press("LoadButton_0", 10);
        Check("load through UI", Session.World != null, Session.CurrentSaveName ?? "");
        if (Session.World == null) return;
        Check("reloaded state matches saved digest exactly", WorldSerializer.Digest(W) == expected, $"{WorldSerializer.Digest(W)[..12]} vs {expected[..Math.Min(12, expected.Length)]}");
        if (W.Clock.Paused) await Press("PauseButton");
        for (int i = 0; i < 3; i++) await Press("FasterButton", 1);
        long t0 = W.Clock.Tick;
        Session.CameraRig.LookAtPoint(new Vector3(0, 7.5f, 13.5f), Vector3.Zero);
        await Frames(150);
        var inv = W.CheckInvariants();
        Check("simulation continues after reload", W.Clock.Tick > t0 + 50 && inv.Count == 0, $"{W.Clock.Tick - t0} ticks, {inv.Count} invariant problems");
        await Screenshot("reload_continued");
    }

    // ------------------------------------------------------------------ perf: normal play, flying camera

    private async Task PerfAsync()
    {
        var cam = Session.CameraRig;
        W.Clock.Paused = false;
        var samples = new List<string>();
        ulong start = Time.GetTicksMsec();
        int second = 0;
        long lastTick = W.Clock.Tick;
        double worstFrame = 0; int frames = 0; ulong lastSample = start;
        var allFrames = new List<double>(8192); var fpsSamples = new List<double>();
        // VIVARIUM_PERF_LOW=1: a slow orbit at critter height over the ground, where close-up detail layers cost most
        bool low = System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_LOW") == "1";
        bool sculpt = System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_SCULPT") == "1";
        int durationMs = int.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_PERF_SECONDS"), out var secs) ? secs * 1000 : 90_000;
        while (Time.GetTicksMsec() - start < (ulong)durationMs)
        {
            float t = (Time.GetTicksMsec() - start) / 1000f;
            if (low) cam.LookAtPoint(new Vector3(Mathf.Sin(t * 0.15f) * 3.5f, 0.5f, Mathf.Cos(t * 0.15f) * 3.5f), new Vector3(Mathf.Sin(t * 0.15f + 0.6f) * 2.5f, 0, Mathf.Cos(t * 0.15f + 0.6f) * 2.5f));
            else cam.LookAtPoint(new Vector3(Mathf.Sin(t * 0.3f) * 9, 3.5f, Mathf.Cos(t * 0.3f) * 9), new Vector3(0, 0, 0));
            // VIVARIUM_PERF_SCULPT=1: sculpt every frame, to soak the terrain mesh rebuild path
            if (sculpt) Vivarium.Sim.World.TerrainEditing.Sculpt(W, new Vivarium.Sim.Core.Vec2(Mathf.Sin(t) * 2, Mathf.Cos(t * 0.7f) * 2), 0.6, (frames & 1) == 0 ? 0.01 : -0.01, Vivarium.Sim.World.SculptMode.Raise);
            ulong f0 = Time.GetTicksUsec();
            await Frames(1);
            double ms = (Time.GetTicksUsec() - f0) / 1000.0;
            worstFrame = Math.Max(worstFrame, ms);
            if (t > 3) allFrames.Add(ms); // skip start-up hitches
            frames++;
            if (Time.GetTicksMsec() - lastSample >= 1000)
            {
                lastSample = Time.GetTicksMsec();
                var line = $"t={++second,3}s fps={Engine.GetFramesPerSecond(),3} frames={frames,3} worstFrame={worstFrame,6:0.0}ms " +
                    $"process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000,6:0.0}ms simLast={W.Scheduler.LastAdvanceMs,5:0.0}ms " +
                    $"ticks/s={W.Clock.Tick - lastTick,4} backlog={W.Scheduler.Backlog,7:0}s flora={W.Flora.Count} fauna={W.Fauna.Count} mem={OS.GetStaticMemoryUsage() / 1048576}MB vram={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:0}MB ws={System.Environment.WorkingSet / 1048576}MB " +
                    $"draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} prims={Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)} floraVis={Session.Flora.Visible_} faunaDrawn={Session.Fauna.Drawn} objs={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} gc0={System.GC.CollectionCount(0)} gc1={System.GC.CollectionCount(1)} gc2={System.GC.CollectionCount(2)} slowest: " + FrameProfiler.TakeReport();
                Log.Info(LogCategory.Perf, line);
                samples.Add(line);
                if (second > 3) fpsSamples.Add(Engine.GetFramesPerSecond());
                lastTick = W.Clock.Tick; worstFrame = 0; frames = 0;
            }
        }
        _facts["samples"] = samples;
        if (allFrames.Count > 0)
        {
            allFrames.Sort();
            double P(double q) => Math.Round(allFrames[Math.Min(allFrames.Count - 1, (int)(allFrames.Count * q))], 2);
            _facts["summary"] = new Dictionary<string, object>
            {
                ["frames"] = allFrames.Count, ["fps_mean"] = Math.Round(1000.0 / allFrames.Average(), 1),
                ["fps_min_second"] = fpsSamples.Count > 0 ? fpsSamples.Min() : 0,
                ["frame_ms_p50"] = P(0.5), ["frame_ms_p95"] = P(0.95), ["frame_ms_p99"] = P(0.99), ["frame_ms_worst"] = Math.Round(allFrames[^1], 2),
                ["frames_over_33ms"] = allFrames.Count(x => x > 33.4), ["flora"] = W.Flora.Count, ["fauna"] = W.Fauna.Count,
                ["renderer"] = RenderingServer.GetVideoAdapterName(),
            };
        }
    }

    // ------------------------------------------------------------------ render tour

    private async Task RenderAsync()
    {
        var cam = Session.CameraRig;
        Find<Control>("Toasts").Visible = true;
        await Frames(20);
        var views = new List<(string Name, Vector3 Eye, Vector3 Target)>();
        views.Add(("overview", new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0)));
        // pond cut face from outside the specimen (pond sits against hexagon side 0)
        var dom = W.Domain;
        var n0 = dom.EdgeNormals[0];
        var deep = DeepestWater();
        var edgePt = dom.NearestBoundaryPoint(deep);
        var en = dom.BoundaryNormal(edgePt);
        views.Add(("cutaway_pond", new Vector3((float)(edgePt.X + en.X * 3.2), 0.4f, (float)(edgePt.Z + en.Z * 3.2)), new Vector3((float)edgePt.X, -0.6f, (float)edgePt.Z)));
        views.Add(("strata_side", new Vector3((float)(-dom.Apothem * 1.9 * n0.X), 0.2f, (float)(-dom.Apothem * 1.9 * n0.Z) - 1), new Vector3((float)(-dom.Apothem * n0.X), -1.2f, (float)(-dom.Apothem * n0.Z))));
        views.Add(("below", new Vector3(3, -6, 9), new Vector3(0, -2, 0)));
        double surf = W.Water.SurfaceAt(deep);
        var aquatic = W.Fauna.Items.Where(f => W.Content.FaunaOrThrow(f.SpeciesId).Medium == Vivarium.Sim.Content.Medium.Aquatic).OrderBy(f => Vec2.Distance(f.PositionXZ, deep)).FirstOrDefault();
        var target = aquatic != null ? Bridge.V(aquatic.Position) : new Vector3((float)deep.X, (float)surf - 0.1f, (float)deep.Z);
        views.Add(("underwater", target + new Vector3(0.5f, 0.05f, 0.5f), target));
        foreach (var species in new[] { "shrimp", "microminnow", "triops", "springtail", "pill_bug", "darkling_beetle", "silverfish" })
        {
            var f = W.Fauna.Items.FirstOrDefault(x => x.SpeciesId == species);
            if (f == null) continue;
            var p = Bridge.V(f.Position);
            float scale = (float)(W.FaunaSystem.PhenotypeOf(f).BodySize * W.Content.FaunaOrThrow(species).VisualScale);
            views.Add(("closeup_" + species, p + new Vector3(scale * 2.2f, scale * 1.6f, scale * 2.2f), p));
        }
        foreach (var species in new[] { "carpet_moss", "crust_lichen", "marginal_waterside", "ornamental_herb", "fern", "climbing_vine", "bonnet_mushroom", "turkey_tail", "slime_mold", "stonecrop", "blue_fescue", "reindeer_lichen" })
        {
            var f = W.Flora.Items.FirstOrDefault(x => x.SpeciesId == species);
            if (f == null) continue;
            var p = new Vector3((float)f.X, (float)W.GroundHeight(f.Position), (float)f.Z);
            views.Add(("closeup_" + species, p + new Vector3(0.45f, 0.35f, 0.45f), p));
        }
        // a shoreline close-up: a wet cell next to dry ground, seen at a low angle
        var shoreCell = W.Grid.DomainCells.Where(c => W.Water.IsWet(c) && !W.Grid.IsBoundaryCell[c]).OrderBy(c => c)
            .FirstOrDefault(c => { var q = W.Grid.CellCenter(c); return !W.Water.IsWet(q + new Vec2(0.5, 0)) && W.Domain.ContainsDisc(q, 2); }, -1);
        if (shoreCell >= 0)
        {
            var q = W.Grid.CellCenter(shoreCell);
            var sp3 = new Vector3((float)q.X, (float)W.Water.SurfaceAt(q), (float)q.Z);
            views.Add(("shoreline", sp3 + new Vector3(-0.9f, 0.55f, 0.6f), sp3 + new Vector3(0.25f, 0, 0)));
        }
        W.Clock.Paused = true;
        foreach (var (name, eye, tgt) in views)
        {
            cam.LookAtPoint(eye, tgt);
            await Frames(8);
            await Screenshot(name);
        }
        // the pond view once more without the water surface (tells water artefacts from terrain artefacts)
        var tt = views.FirstOrDefault(v => v.Item1 == "closeup_turkey_tail");
        if (tt.Item1 != null)
        {
            cam.LookAtPoint(tt.Item2, tt.Item3);
            Session.Water.Visible = false;
            await Frames(8);
            await Screenshot("closeup_turkey_tail_nowater");
            Session.Water.Visible = true;
        }
        // growth you can watch: the same view before and after ~15 real seconds at the fastest speed
        var mossPatch = W.Flora.Items.Where(x => x.SpeciesId == "carpet_moss").OrderBy(x => x.Id.Value).FirstOrDefault();
        if (mossPatch != null)
        {
            var mp = new Vector3((float)mossPatch.X, (float)W.GroundHeight(mossPatch.Position), (float)mossPatch.Z);
            cam.LookAtPoint(mp + new Vector3(1.4f, 1.3f, 1.4f), mp);
            await Frames(8);
            int flora0 = W.Flora.Count; double day0 = W.Clock.BioDays;
            await Screenshot("growth_t0");
            int speed0 = W.Clock.SpeedIndex;
            W.Clock.SetSpeedIndex(Vivarium.Sim.Time.SimClock.SpeedSteps.Length - 1);
            W.Clock.Paused = false;
            await Seconds(15);
            W.Clock.Paused = true;
            W.Clock.SetSpeedIndex(speed0);
            await Frames(8);
            await Screenshot("growth_t1");
            _facts["growth_timelapse"] = $"{W.Clock.BioDays - day0:0.0} biological days, flora {flora0} -> {W.Flora.Count}";
            // the slime-mold network after those days: centre on its largest cluster of patches
            var slime = W.Flora.Items.Where(x => x.SpeciesId == "slime_mold").ToList();
            if (slime.Count > 0)
            {
                var hub = slime.OrderByDescending(x => slime.Count(o => Vec2.Distance(o.Position, x.Position) < 0.5)).ThenBy(x => x.Id.Value).First();
                var hp = new Vector3((float)hub.X, (float)W.GroundHeight(hub.Position), (float)hub.Z);
                cam.LookAtPoint(hp + new Vector3(0.55f, 0.6f, 0.55f), hp);
                await Frames(8);
                await Screenshot("slime_network");
                _facts["slime_patches"] = slime.Count;
            }
        }
        // full detail at distance: from the far overview every animal is either drawn with its full model or is
        // genuinely outside the view, and every plant instance uses its full mesh
        cam.LookAtPoint(new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0));
        await Frames(6);
        string digestA = WorldSerializer.Digest(W);
        _facts["fauna_drawn"] = $"drawn {Session.Fauna.Drawn}, off-screen {Session.Fauna.OffScreen}, triangles {Session.Fauna.TrianglesDrawn}";
        Check("distant organisms keep full detail", Session.Fauna.Drawn + Session.Fauna.OffScreen == W.Fauna.Count && Session.Fauna.Drawn > W.Fauna.Count / 2 && Session.Flora.Visible_ == W.Flora.Count,
            $"fauna drawn {Session.Fauna.Drawn} + off-screen {Session.Fauna.OffScreen} of {W.Fauna.Count}; flora {Session.Flora.Visible_} of {W.Flora.Count}");
        Check("rendering leaves the simulation digest unchanged", WorldSerializer.Digest(W) == digestA);
        _facts["island_triangles"] = Session.Island.TriangleCount;
        _facts["water_triangles"] = Session.Water.TriangleCount;
        _facts["pebbles"] = Session.Props.PebbleCount;
        foreach (int tier in new[] { 0, 2 })
        {
            Session.Settings.Quality = tier; Session.ApplySettings();
            cam.LookAtPoint(new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0));
            await Screenshot($"quality_{tier}");
        }
        // UI layout on a wide, short window (the reported overlap case)
        Session.Settings.Quality = 1; Session.ApplySettings();
        GetWindow().Size = new Vector2I(2000, 810);
        await Frames(10);
        cam.LookAtPoint(new Vector3(0, 8.5f, 14.5f), new Vector3(0, -0.5f, 0));
        Session.Ui.Windows.Open("Help");
        await Screenshot("ui_help_wide");
        Session.Ui.Windows.Close("Help");
        Session.Tools.Select(new WorldHit(HitKind.Terrain, EntityId.None, new Vec3(0, W.SurfaceHeight(Vec2.Zero), 0), 1));
        Session.Ui.Radial.Open(GetViewport().GetVisibleRect().Size / 2);
        await Frames(20);
        await Screenshot("ui_radial");
        await Press("Tool_IntroduceFlora", 20);
        await Screenshot("ui_radial_species");
        Session.Ui.Radial.Close();
        Session.Ui.Windows.Open("Catalog");
        await Screenshot("ui_catalog_wide");
        Session.Ui.Windows.Close("Catalog");
    }
}
