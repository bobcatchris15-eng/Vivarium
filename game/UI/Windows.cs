using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Diagnostics;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.World;

namespace Vivarium.Game.UI;

/// <summary>The pop-up panels: catalog, statistics, save/load, new world, settings, debug, help.</summary>
public partial class Windows : Control
{
    public GameSession Session { get; set; } = null!;
    private readonly Dictionary<string, PanelContainer> _w = new();
    private VBoxContainer _catalogList = null!, _statsList = null!, _saveList = null!;
    private RichTextLabel _statsEnv = null!, _debugInfo = null!;
    private LineEdit _saveName = null!, _seed = null!;
    private OptionButton _preset = null!;
    private HSlider _diameter = null!, _relief = null!, _springFlow = null!;
    private Label _diameterLabel = null!, _reliefLabel = null!, _springLabel = null!;
    private readonly List<string> _presetIds = new();
    private readonly Dictionary<string, (Label Count, Sparkline Line)> _statRows = new();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildCatalog(); BuildStats(); BuildSaveLoad(); BuildNewWorld(); BuildSettings(); BuildDebug(); BuildHelp();
        if (Session.Settings.ShowHelpOnStart) Toggle("Help");
    }

    private (PanelContainer, VBoxContainer) Add(string name, string title, Vector2 size)
    {
        var (root, body) = UiKit.Window("Win_" + name, title, size, () => Close(name));
        AddChild(root);
        _w[name] = root;
        return (root, body);
    }

    public bool IsOpen(string name) => _w.TryGetValue(name, out var p) && p.Visible;
    public bool AnyOpen => _w.Values.Any(p => p.Visible);

    /// <summary>Fits an open window into the viewport below the top bar; its body scrolls if it is taller.</summary>
    private void Layout(PanelContainer p)
    {
        var vp = GetViewportRect().Size;
        var req = p.GetMeta("requested_size").AsVector2();
        float top = UiRoot.TopBarHeight + 16, bottom = 16;
        float w = Mathf.Min(req.X, vp.X - 32);
        float maxH = Mathf.Max(160, vp.Y - top - bottom);
        var scroll = p.FindChild(p.Name + "_Scroll", true, false) as ScrollContainer;
        if (scroll != null)
        {
            float contentH = scroll.GetChild<Control>(0).GetCombinedMinimumSize().Y;
            scroll.CustomMinimumSize = new Vector2(w - 28, Mathf.Clamp(contentH, 60, Mathf.Min(req.Y - 60, maxH - 70)));
        }
        p.Size = new Vector2(w, 0);   // shrink to content
        p.ResetSize();
        var size = p.GetCombinedMinimumSize();
        p.Size = new Vector2(w, Mathf.Min(size.Y, maxH));
        p.Position = new Vector2((vp.X - w) / 2, top + Mathf.Max(0, (maxH - p.Size.Y) / 2));
    }

    public void Toggle(string name)
    {
        if (!_w.TryGetValue(name, out var p)) return;
        bool open = !p.Visible;
        foreach (var other in _w.Values) other.Visible = false;
        p.Visible = open;
        Session.CameraRig.KeyboardBlocked = open && name is "SaveLoad" or "NewWorld";
        if (open) OnOpened(name);
    }

    public void Open(string name) { if (!IsOpen(name)) Toggle(name); }
    public void Close(string name)
    {
        if (_w.TryGetValue(name, out var p)) p.Visible = false;
        Session.CameraRig.KeyboardBlocked = false;
        if (name == "Help" && Session.Settings.ShowHelpOnStart) { Session.Settings.ShowHelpOnStart = false; Session.Settings.Save(); }
    }

    private void OnOpened(string name)
    {
        if (name == "Catalog") RebuildCatalog();
        if (name == "SaveLoad") RebuildSaves();
        if (name == "Stats") RebuildStats();
        if (name == "Settings") SyncGrowthSpeed();
    }

    private HSlider? _growth;
    private Label? _growthL;

    /// <summary>Real minutes → biological days at 1× speed, for the growth-speed label.</summary>
    private static string GrowthText(double factor) =>
        $"×{factor:0.0} · 1 real min ≈ {factor * Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * 60 / SimUnits.Day:0.0#} days";

    private void SyncGrowthSpeed()
    {
        if (_growth == null || Session.World == null) return;
        _growth.SetValueNoSignal(Session.World.Clock.BioAcceleration);
        _growthL!.Text = GrowthText(Session.World.Clock.BioAcceleration);
    }

    public void OnWorldChanged() { RebuildCatalog(); RebuildStats(); }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Windows");
        foreach (var p in _w.Values) if (p.Visible) Layout(p);
    }

    public void Refresh()
    {
        if (IsOpen("Stats")) UpdateStats();
        if (IsOpen("Catalog")) UpdateCatalogCounts();
        if (IsOpen("Debug")) UpdateDebug();
    }

    // ------------------------------------------------------------------ catalog

    private readonly Dictionary<string, Label> _catalogCounts = new();

    private void BuildCatalog()
    {
        var (_, body) = Add("Catalog", "Species catalog", new Vector2(640, 560));
        body.AddChild(UiKit.Label("Every species can be (re)introduced at any time — nothing is ever lost for good.", 13, UiKit.Muted, wrap: true));
        _catalogList = UiKit.Column();
        _catalogList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(_catalogList);   // the window body scrolls
    }

    private void RebuildCatalog()
    {
        if (_catalogList == null) return;
        foreach (var c in _catalogList.GetChildren()) c.QueueFree();
        _catalogCounts.Clear();
        void Entry(string id, string name, string kind, string role, string habitat, bool fauna)
        {
            var count = UiKit.Label("", 14, UiKit.Muted);
            _catalogCounts[id] = count;
            var intro = UiKit.Button("Catalog_" + id, "Introduce", () => { Session.Ui.ChooseSpecies(id, fauna); Close("Catalog"); Session.Ui.Toast($"Click the island to introduce {name}", false); });
            var text = UiKit.Column(UiKit.Row(UiKit.Label(name, 16, UiKit.Accent), UiKit.Label(kind, 13, UiKit.Muted), UiKit.Spacer(), count, intro),
                UiKit.Label(role, 13, null, wrap: true), UiKit.Label(habitat, 12, UiKit.Muted, wrap: true));
            _catalogList.AddChild(UiKit.Panel("CatalogEntry_" + id, text));
        }
        void Section(string title)
        {
            if (_catalogList.GetChildCount() > 0) _catalogList.AddChild(new HSeparator());
            _catalogList.AddChild(UiKit.Label(title, 14, UiKit.Accent));
        }
        foreach (var group in FloraPlacementGroups.All)
        {
            Section(FloraPlacementGroups.Name(group));
            foreach (var sp in Session.Content.Flora.Where(sp => sp.PlacementGroup == group).OrderBy(sp => sp.Name))
                Entry(sp.Id, sp.Name, sp.Archetype, sp.Role,
                    $"Grows on {string.Join(", ", sp.SubstrateAffinity.Where(kv => kv.Value > 0.4).Select(kv => SubstrateIds.Id(kv.Key)))}; moisture ≈{sp.Moisture.Optimum:0.0}, light ≈{sp.Light.Optimum:0.0}" +
                    (sp.RefuseSubstrates.Count + sp.RefuseTags.Count > 0 ? $"; refuses {string.Join(", ", sp.RefuseSubstrates.Select(SubstrateIds.Id).Concat(sp.RefuseTags))}" : ""), false);
        }
        Section("Fauna");
        foreach (var sp in Session.Content.Fauna)
            Entry(sp.Id, sp.Name, sp.Medium.ToString().ToLowerInvariant(), sp.Role,
                $"{(sp.Medium == Medium.Aquatic ? $"Needs water ≥{sp.MinWaterDepth * 100:0} cm" : "Moist ground")}; eats {string.Join(", ", sp.Diet.Select(d => d.Resource))}; lives ~{sp.Lifespan / SimUnits.Day:0} days", true);
        UpdateCatalogCounts();
    }

    private void UpdateCatalogCounts()
    {
        var w = Session.World;
        if (w == null) return;
        foreach (var (id, label) in _catalogCounts)
        {
            int n = w.Content.FloraById(id) != null ? w.Flora.Items.Count(f => f.SpeciesId == id) : w.Fauna.CountOf(id);
            label.Text = n == 0 ? "extinct — reintroduce" : $"{n} alive";
            label.AddThemeColorOverride("font_color", n == 0 ? new Color(1f, 0.7f, 0.5f) : UiKit.Muted);
        }
    }

    // ------------------------------------------------------------------ stats

    private void BuildStats()
    {
        var (_, body) = Add("Stats", "Ecosystem statistics", new Vector2(600, 600));
        _statsList = UiKit.Column();
        body.AddChild(_statsList);
        body.AddChild(new HSeparator());
        _statsEnv = UiKit.Rich("StatsEnv");
        body.AddChild(_statsEnv);
    }

    private void RebuildStats()
    {
        if (_statsList == null) return;
        foreach (var c in _statsList.GetChildren()) c.QueueFree();
        _statRows.Clear();
        _statsList.AddChild(UiKit.Label("Population (last ~2 sim-weeks)", 14, UiKit.Muted));
        foreach (var (id, name) in Session.Content.Fauna.Select(f => (f.Id, f.Name)).Concat(Session.Content.Flora.Select(f => (f.Id, f.Name))))
        {
            var count = UiKit.Label("", 14); count.CustomMinimumSize = new Vector2(180, 0);
            var line = new Sparkline();
            var nameL = UiKit.Label(name, 14); nameL.CustomMinimumSize = new Vector2(170, 0);
            _statsList.AddChild(UiKit.Row(nameL, line, count));
            _statRows[id] = (count, line);
        }
        UpdateStats();
    }

    private void UpdateStats()
    {
        var w = Session.World;
        if (w == null || Session.History.Count == 0) return;
        var s = Session.History[^1];
        foreach (var f in s.Fauna)
            if (_statRows.TryGetValue(f.Id, out var row))
            {
                row.Count.Text = $"{f.Count}  (+{f.Births}/−{f.Deaths}) {f.MeanBodySizeMm:0.0} mm";
                row.Line.SetValues(Session.History.Select(h => (float)(h.Fauna.FirstOrDefault(x => x.Id == f.Id)?.Count ?? 0)));
            }
        foreach (var f in s.Flora)
            if (_statRows.TryGetValue(f.Id, out var row))
            {
                row.Count.Text = $"{f.Count}  biomass {f.Biomass:0.0}";
                row.Line.SetValues(Session.History.Select(h => (float)(h.Flora.FirstOrDefault(x => x.Id == f.Id)?.Count ?? 0)));
            }
        _statsEnv.Text = $"Day {s.SimDays:0.0} · mean moisture {s.MeanMoisture:0.00} · mean nutrients {s.MeanNutrients:0.00} · detritus {s.TotalDetritus:0.0}\n" +
                         $"Water {s.WaterVolume:0.00} m³ covering {s.WetFraction:P0} · light {s.MeanLight:0.00} · lineage records {s.LineageRecords}";
    }

    // ------------------------------------------------------------------ save / load

    private void BuildSaveLoad()
    {
        var (_, body) = Add("SaveLoad", "Save & load", new Vector2(560, 520));
        _saveName = new LineEdit { Name = "SaveNameEdit", PlaceholderText = "save name", CustomMinimumSize = new Vector2(300, 0) };
        _saveName.FocusEntered += () => Session.CameraRig.KeyboardBlocked = true;
        body.AddChild(UiKit.Row(_saveName, UiKit.Button("SaveNowButton", "Save", () => DoSave(_saveName.Text))));
        body.AddChild(UiKit.Label("Saves (newest first):", 14, UiKit.Muted));
        _saveList = UiKit.Column();
        body.AddChild(_saveList);
    }

    public void QuickSave() => DoSave(Session.CurrentSaveName ?? Session.World?.Descriptor.Name ?? "vivarium");

    public void DoSave(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = Session.CurrentSaveName ?? "vivarium";
        var r = Session.SaveTo(name);
        Session.Ui.Toast(r.Ok ? $"Saved '{name}'" : r.Message, !r.Ok);
        RebuildSaves();
    }

    private void RebuildSaves()
    {
        if (_saveList == null) return;
        foreach (var c in _saveList.GetChildren()) c.QueueFree();
        _saveName.Text = Session.CurrentSaveName ?? Session.World?.Descriptor.Name ?? "";
        int i = 0;
        foreach (var (path, m, note) in Session.ListSaves())
        {
            string label = m == null ? $"{System.IO.Path.GetFileName(path)} — {note}"
                : $"{System.IO.Path.GetFileNameWithoutExtension(path)} — {m.WorldName}, day {m.SimDays:0.0}, {m.FloraCount} flora / {m.FaunaCount} fauna, saved {m.SavedUtc.ToLocalTime():g}";
            var load = UiKit.Button($"LoadButton_{i++}", "Load", () =>
            {
                var r = Session.LoadFrom(path);
                Session.Ui.Toast(r.Ok ? $"Loaded {System.IO.Path.GetFileNameWithoutExtension(path)}" : "Load failed: " + r.Message, !r.Ok);
                if (r.Ok) Close("SaveLoad");
            });
            load.Disabled = m == null;
            _saveList.AddChild(UiKit.Row(load, UiKit.Label(label, 13, null, wrap: true)));
        }
        if (i == 0) _saveList.AddChild(UiKit.Label("No saves yet.", 13, UiKit.Muted));
    }

    // ------------------------------------------------------------------ new world

    private void BuildNewWorld()
    {
        var (_, body) = Add("NewWorld", "Create a new vivarium", new Vector2(520, 420));
        _preset = new OptionButton { Name = "PresetPicker" };
        foreach (var (id, d) in Session.Content.Presets.OrderBy(p => p.Key == "default" ? "" : p.Key)) { _preset.AddItem(d.Name); _presetIds.Add(id); }
        _preset.ItemSelected += _ => LoadPresetDefaults();
        _seed = new LineEdit { Name = "SeedEdit", CustomMinimumSize = new Vector2(200, 0) };
        _seed.FocusEntered += () => Session.CameraRig.KeyboardBlocked = true;
        _diameterLabel = UiKit.Label(""); _reliefLabel = UiKit.Label(""); _springLabel = UiKit.Label("");
        _diameter = UiKit.Slider("DiameterSlider", WorldDescriptor.MinDiameter, WorldDescriptor.MaxDiameter, 0.5, 16, v => _diameterLabel.Text = $"{v:0.0} m");
        _relief = UiKit.Slider("ReliefSlider", 0.2, 2.0, 0.05, 1.0, v => _reliefLabel.Text = $"×{v:0.00}");
        _springFlow = UiKit.Slider("SpringSlider", 0.0, 3.0, 0.1, 1.0, v => _springLabel.Text = v < 0.05 ? "dry (no springs)" : $"×{v:0.0}");
        var grid = new GridContainer { Columns = 3 };
        void Row(string l, Control c, Control? extra) { grid.AddChild(UiKit.Label(l)); grid.AddChild(c); grid.AddChild(extra ?? new Control()); }
        Row("Preset", _preset, null);
        Row("Seed", _seed, UiKit.Button("RandomSeedButton", "🎲", () => _seed.Text = ((ulong)(GD.Randi()) * 7919UL % 1_000_000_000UL).ToString(CultureInfo.InvariantCulture), "Random seed"));
        Row("Island diameter", _diameter, _diameterLabel);
        Row("Terrain relief", _relief, _reliefLabel);
        Row("Spring flow", _springFlow, _springLabel);
        body.AddChild(grid);
        body.AddChild(UiKit.Label("The same seed and settings always grow the same starting island.", 13, UiKit.Muted, wrap: true));
        body.AddChild(UiKit.Row(UiKit.Spacer(), UiKit.Button("CreateWorldButton", "Create vivarium", CreateFromUi)));
        LoadPresetDefaults();
    }

    private void LoadPresetDefaults()
    {
        int i = Math.Max(0, _preset.Selected);
        var d = Session.Content.PresetOrThrow(_presetIds[i]);
        _seed.Text = d.Seed.ToString(CultureInfo.InvariantCulture);
        _diameter.Value = d.Diameter; _relief.Value = 1.0; _springFlow.Value = 1.0;
        _diameterLabel.Text = $"{d.Diameter:0.0} m"; _reliefLabel.Text = "×1.00"; _springLabel.Text = "×1.0";
    }

    /// <summary>Deterministic mapping from the panel's values to a world descriptor.</summary>
    public WorldDescriptor DescriptorFromUi()
    {
        var d = Session.Content.PresetOrThrow(_presetIds[Math.Max(0, _preset.Selected)]);
        if (ulong.TryParse(_seed.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) d.Seed = seed;
        else d.Seed = Hash.Fnv1a64(_seed.Text.Trim());           // any text works as a seed
        double scale = _diameter.Value / d.Diameter;
        d.Diameter = _diameter.Value;
        foreach (var f in d.Terrain.Features) { f.X *= scale; f.Z *= scale; f.ToX *= scale; f.ToZ *= scale; f.Radius *= scale; }
        foreach (var s in d.Water.Springs) { s.X *= scale; s.Z *= scale; s.Discharge *= _springFlow.Value; }
        if (_springFlow.Value < 0.05) d.Water.Springs.Clear();
        d.Terrain.Relief *= _relief.Value;
        foreach (var f in d.Terrain.Features) f.Amount *= Math.Sqrt(_relief.Value);
        double areaScale = scale * scale;
        foreach (var s in d.StarterFlora) s.Count = Math.Max(1, (int)Math.Round(s.Count * areaScale));
        foreach (var s in d.StarterFauna) s.Count = Math.Max(1, (int)Math.Round(s.Count * areaScale));
        d.Placement.Rocks = (int)Math.Round(d.Placement.Rocks * areaScale);
        d.Placement.GravelPatches = (int)Math.Round(d.Placement.GravelPatches * areaScale);
        d.Name = $"{d.Name} #{d.Seed % 10000}";
        return d;
    }

    private void CreateFromUi()
    {
        try
        {
            var d = DescriptorFromUi();
            var problems = d.Validate();
            if (problems.Count > 0) { Session.Ui.Toast("Invalid settings: " + string.Join("; ", problems), true); return; }
            Session.StartWorld(Session.CreateWorld(d));
            Close("NewWorld");
            Session.Ui.Toast($"Created '{d.Name}'", false);
        }
        catch (Exception ex) { Log.Error(LogCategory.UI, "World creation failed", ex); Session.Ui.Toast("World creation failed: " + ex.Message, true); }
    }

    // ------------------------------------------------------------------ settings

    private void BuildSettings()
    {
        var (_, body) = Add("Settings", "Settings", new Vector2(520, 420));
        var s = Session.Settings;
        var grid = new GridContainer { Columns = 2 };
        var speedL = UiKit.Label($"{s.CameraSpeed:0.00} m/s");
        // world setting (saved with the vivarium): how much faster growth, feeding and breeding run than walking
        _growthL = UiKit.Label(GrowthText(WorldDescriptor.DefaultBioAcceleration));
        _growth = UiKit.Slider("GrowthSpeedSlider", WorldDescriptor.MinBioAcceleration, WorldDescriptor.MaxBioAcceleration, 0.1, WorldDescriptor.DefaultBioAcceleration, v =>
        {
            var r = Session.Host?.Tools.SetBioAcceleration(v);
            if (r is { Ok: true } && Session.World != null) _growthL!.Text = GrowthText(Session.World.Clock.BioAcceleration);
        });
        grid.AddChild(UiKit.Label("Growth speed")); grid.AddChild(UiKit.Row(_growth, _growthL));
        grid.AddChild(UiKit.Label("Flying speed (+ / -)")); grid.AddChild(UiKit.Row(UiKit.Slider("CamSpeedSlider", Math.Log(UserSettings.MinCameraSpeed), Math.Log(UserSettings.MaxCameraSpeed), 0.01, Math.Log(s.CameraSpeed), v => { s.CameraSpeed = Math.Exp(v); speedL.Text = $"{s.CameraSpeed:0.00} m/s"; Apply(); }), speedL));
        var sensL = UiKit.Label($"{s.MouseSensitivity:0.00}");
        grid.AddChild(UiKit.Label("Mouse sensitivity")); grid.AddChild(UiKit.Row(UiKit.Slider("SensSlider", 0.05, 1.0, 0.01, s.MouseSensitivity, v => { s.MouseSensitivity = v; sensL.Text = $"{v:0.00}"; Apply(); }), sensL));
        var invert = new CheckBox { Name = "InvertYCheck", ButtonPressed = s.InvertY, Text = "Invert Y" };
        invert.Toggled += b => { s.InvertY = b; Apply(); };
        grid.AddChild(UiKit.Label("Mouse look")); grid.AddChild(invert);
        var quality = new OptionButton { Name = "QualityPicker" };
        quality.AddItem("Low"); quality.AddItem("Medium"); quality.AddItem("High");
        quality.Select(s.Quality);
        quality.ItemSelected += i => { s.Quality = (int)i; Apply(); };
        grid.AddChild(UiKit.Label("Graphics quality")); grid.AddChild(quality);
        var auto = new CheckBox { Name = "AutosaveCheck", ButtonPressed = s.AutosaveEnabled, Text = "Enabled" };
        auto.Toggled += b => { s.AutosaveEnabled = b; Apply(); };
        var autoL = UiKit.Label($"every {s.AutosaveMinutes:0} min");
        grid.AddChild(UiKit.Label("Autosave")); grid.AddChild(UiKit.Row(auto, UiKit.Slider("AutosaveSlider", 1, 30, 1, s.AutosaveMinutes, v => { s.AutosaveMinutes = v; autoL.Text = $"every {v:0} min"; Apply(); }), autoL));
        var uiL = UiKit.Label($"×{s.UiScale:0.00}");
        grid.AddChild(UiKit.Label("Interface size")); grid.AddChild(UiKit.Row(UiKit.Slider("UiScaleSlider", 0.75, 1.6, 0.05, s.UiScale, v => { s.UiScale = v; uiL.Text = $"×{v:0.00}"; Apply(); }), uiL));
        body.AddChild(grid);
        body.AddChild(UiKit.Label("These settings affect presentation only; the ecosystem runs identically on every setting.", 13, UiKit.Muted, wrap: true));
        void Apply() { Session.ApplySettings(); s.Save(); }
    }

    // ------------------------------------------------------------------ debug

    private void BuildDebug()
    {
        var (_, body) = Add("Debug", "Developer overlays", new Vector2(520, 460));
        body.AddChild(UiKit.Label("Visual diagnostics only — toggling them never changes the simulation.", 13, UiKit.Muted, wrap: true));
        var mode = new OptionButton { Name = "OverlayModePicker" };
        foreach (var m in new[] { "None", "Moisture", "Nutrients", "Light / exposure", "Substrate" }) mode.AddItem(m);
        mode.ItemSelected += i => Session.Island.OverlayMode = (int)i;
        body.AddChild(UiKit.Row(UiKit.Label("Ground overlay"), mode));
        var hydro = new CheckBox { Name = "HydroDebugCheck", Text = "Hydrology: depth dots, flow arrows, springs" };
        hydro.Toggled += b => Session.Overlay.HydrologyDebug = b;
        body.AddChild(hydro);
        _debugInfo = UiKit.Rich("DebugInfo");
        body.AddChild(_debugInfo);
    }

    private void UpdateDebug()
    {
        var w = Session.World;
        if (w == null) return;
        var c = Counters.Collect(w, Session.Flora.Visible_, Session.Fauna.Drawn, Session.Fauna.Drawn, 0, Session.Fauna.OffScreen);
        var mism = c.Mismatches();
        var sys = string.Join("\n", w.Scheduler.Systems.Select(s => $"  {s.Name,-18} {s.LastMs,6:0.00} ms (max {s.MaxMs:0.0})"));
        _debugInfo.Text = $"Tick {w.Clock.Tick} · backlog {w.Scheduler.Backlog:0} s · {w.Scheduler.TicksLastAdvance} ticks last frame ({w.Scheduler.LastAdvanceMs:0.0} ms)\n" +
            $"Flora {c.Flora} (index {c.FloraIndexed}) · fauna {c.Fauna} (index {c.FaunaIndexed}) · genomes {c.Genomes} · lineage {c.Lineage}\n" +
            $"Fauna drawn (full detail) {Session.Fauna.Drawn} · off-screen {Session.Fauna.OffScreen} · triangles {Session.Fauna.TrianglesDrawn + Session.Flora.TrianglesDrawn:N0}\n" +
            (mism.Count > 0 ? "[color=#f88]" + string.Join("\n", mism) + "[/color]\n" : "Counters consistent\n") + sys;
    }

    // ------------------------------------------------------------------ help

    private void BuildHelp()
    {
        var (_, body) = Add("Help", "Welcome to your vivarium", new Vector2(640, 600));
        var t = UiKit.Rich("HelpText");
        t.Text =
            "[b]Camera[/b]\n" +
            "  Hold [b]right mouse[/b] and drag to look around\n" +
            "  [b]W A S D[/b] fly level · [b]Space[/b] up · [b]Ctrl[/b] down · [b]Q / E[/b] turn · [b]Shift[/b] fast · [b]Alt[/b] slow\n" +
            "  [b]Mouse wheel[/b] zooms toward what's under the cursor · [b]+ / -[/b] flying speed (shown in the top bar)\n" +
            "  [b]F[/b] focus & orbit the selection (wheel zooms) · [b]Esc[/b] leave focus · [b]Home[/b] reset view\n" +
            "  Fly below the water surface to watch aquatic life.\n\n" +
            "[b]Time[/b]   [b]P[/b] pause · [b], .[/b] slower / faster.  One real hour ≈ one vivarium week.\n\n" +
            "[b]Tools[/b] — [b]right-click[/b] (a quick tap) opens the tool wheel; number keys also work\n" +
            "  [b]1[/b] Inspect — click anything; genome & lineage tabs for critters\n" +
            "  [b]2[/b] Grab critter — click with a critter inside the circle, click again to release (X puts it back)\n" +
            "  [b]3[/b] Pick plant · [b]4[/b] Nutrients (hold) · [b]5[/b] Pokin' stick\n" +
            "  [b]6[/b] Rock · [b]7[/b] Log (R rotates) · [b]8[/b] Gravel — [b][ ][/b] or Ctrl+wheel resize\n" +
            "  [b]9[/b] Add flora · [b]0[/b] Add fauna — pick the species in the wheel's outer ring or the catalog\n" +
            "  [b]G[/b] Terrain — raise, lower, smooth (hold and drag). Dig below the water line and a pond fills in.\n" +
            "  [b]H[/b] Water — pour, soak up (hold), or click to add / remove a spring. Groundwater ponds refill themselves.\n" +
            "  The cursor ring turns [color=#4f7]green[/color] where an action is valid and [color=#f66]red[/color] where it isn't.\n" +
            "  Selected rocks, logs and gravel can be moved or removed from the inspector (Delete key).\n\n" +
            "[b]Panels[/b]   [b]C[/b] species catalog · [b]T[/b] statistics · [b]Ctrl+S[/b] quick save · [b]F3[/b] debug overlays · [b]F1[/b] help · [b]Tab[/b] hide UI\n\n" +
            "Nothing can be lost for good: if a species dies out, reintroduce it from the catalog.";
        body.AddChild(t);
    }
}
