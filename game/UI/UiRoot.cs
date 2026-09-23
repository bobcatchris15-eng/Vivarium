using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Tools;
using Vivarium.Sim.Time;

namespace Vivarium.Game.UI;

/// <summary>
/// All HUD and panels, built in code. Reads authoritative state for display; every change goes through
/// ToolController/SimHost. Panels hold ids only and clear themselves when their subject disappears.
/// </summary>
public partial class UiRoot : Control
{
    public GameSession Session { get; set; } = null!;
    private Label _clock = null!, _speed = null!, _status = null!, _toolInfo = null!;
    private Button _pause = null!;
    private readonly Dictionary<ToolKind, Button> _toolButtons = new();
    private OptionButton _speciesPicker = null!;
    private VBoxContainer _toasts = null!;
    public Inspector Inspector { get; private set; } = null!;
    private RichTextLabel _probe = null!;
    public Windows Windows { get; private set; } = null!;
    private double _refresh;
    private readonly List<string> _pickerIds = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiKit.BuildTheme((float)Session.Settings.UiScale);

        // ---- top bar: clock + speed + menus
        _clock = UiKit.Label("Week 1, Day 1  00:00", 18);
        _clock.Name = "ClockLabel";
        _pause = UiKit.Button("PauseButton", "⏸ Pause", () => { Session.Host?.TogglePause(); RefreshClock(); }, "Pause / resume the simulation (P)");
        var slower = UiKit.Button("SlowerButton", "◀◀", () => { Session.Host?.Clock.Slower(); RefreshClock(); }, "Slower (,)");
        _speed = UiKit.Label("1×", 16); _speed.Name = "SpeedLabel"; _speed.CustomMinimumSize = new Vector2(64, 0); _speed.HorizontalAlignment = HorizontalAlignment.Center;
        var faster = UiKit.Button("FasterButton", "▶▶", () => { Session.Host?.Clock.Faster(); RefreshClock(); }, "Faster (.)");
        _status = UiKit.Label("", 13, UiKit.Muted); _status.Name = "StatusLabel";
        var top = UiKit.Row(_clock, _pause, slower, _speed, faster, _status, UiKit.Spacer(),
            UiKit.Button("CatalogButton", "Species", () => Windows.Toggle("Catalog"), "Species catalog (C)"),
            UiKit.Button("StatsButton", "Stats", () => Windows.Toggle("Stats"), "Ecosystem statistics (T)"),
            UiKit.Button("SaveButton", "Save", () => Windows.Toggle("SaveLoad"), "Save / load (Ctrl+S)"),
            UiKit.Button("NewWorldButton", "New", () => Windows.Toggle("NewWorld"), "Create a new vivarium"),
            UiKit.Button("SettingsButton", "Settings", () => Windows.Toggle("Settings")),
            UiKit.Button("DebugButton", "Debug", () => Windows.Toggle("Debug"), "Developer overlays (F3)"),
            UiKit.Button("HelpButton", "Help", () => Windows.Toggle("Help"), "Controls (F1)"));
        var topPanel = UiKit.Panel("TopBar", top);
        topPanel.SetAnchorsPreset(LayoutPreset.TopWide);
        topPanel.OffsetLeft = 8; topPanel.OffsetRight = -8; topPanel.OffsetTop = 8;
        AddChild(topPanel);

        // ---- tool palette
        var palette = UiKit.Column(UiKit.Label("Tools", 16, UiKit.Accent));
        void Tool(ToolKind k, string label, string key, string tip)
        {
            var b = UiKit.Button("Tool_" + k, $"{key}  {label}", () => Session.Tools.SetTool(k), tip, toggle: true);
            b.Alignment = HorizontalAlignment.Left;
            _toolButtons[k] = b;
            palette.AddChild(b);
        }
        Tool(ToolKind.Select, "Inspect", "1", "Click anything to inspect it. F focuses the camera on the selection.");
        Tool(ToolKind.Grab, "Grab critter", "2", "Click a critter to pick it up, click again to release it (X returns it).");
        Tool(ToolKind.RemovePlant, "Pick plant", "3", "Remove a plant, moss or lichen (it becomes litter).");
        Tool(ToolKind.Nutrients, "Nutrients", "4", "Sprinkle nutrients; hold to keep spreading. [ ] resizes.");
        Tool(ToolKind.Poke, "Pokin' stick", "5", "Poke critters and plants.");
        Tool(ToolKind.PlaceRock, "Place rock", "6", "Place a rock. [ ] resizes.");
        Tool(ToolKind.PlaceLog, "Place log", "7", "Place a fallen log. [ ] length, R rotates.");
        Tool(ToolKind.PlaceGravel, "Place gravel", "8", "Spread a gravel patch. [ ] resizes.");
        Tool(ToolKind.IntroduceFlora, "Add flora", "9", "Introduce the chosen plant species.");
        Tool(ToolKind.IntroduceFauna, "Add fauna", "0", "Introduce a small group of the chosen animal species.");
        _speciesPicker = new OptionButton { Name = "SpeciesPicker", FocusMode = FocusModeEnum.None, Visible = false };
        _speciesPicker.ItemSelected += i => PickSpecies((int)i);
        palette.AddChild(_speciesPicker);
        _toolInfo = UiKit.Label("", 13, UiKit.Muted, wrap: true); _toolInfo.Name = "ToolInfo"; _toolInfo.CustomMinimumSize = new Vector2(170, 0);
        palette.AddChild(_toolInfo);
        var palPanel = UiKit.Panel("ToolPalette", palette);
        palPanel.SetAnchorsPreset(LayoutPreset.TopLeft);
        palPanel.OffsetLeft = 8; palPanel.OffsetTop = 64;
        AddChild(palPanel);

        // ---- inspector (right)
        Inspector = new Inspector { Name = "Inspector", Session = Session };
        Inspector.SetAnchorsPreset(LayoutPreset.TopRight);
        Inspector.OffsetRight = -8; Inspector.OffsetTop = 64; Inspector.OffsetLeft = -340;
        AddChild(Inspector);

        // ---- environment probe (bottom-left)
        _probe = UiKit.Rich("ProbeText");
        _probe.CustomMinimumSize = new Vector2(250, 0);
        var probePanel = UiKit.Panel("ProbePanel", UiKit.Column(UiKit.Label("Environment probe", 15, UiKit.Accent), _probe));
        probePanel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        probePanel.OffsetLeft = 8; probePanel.OffsetBottom = -8; probePanel.GrowVertical = GrowDirection.Begin;
        AddChild(probePanel);

        // ---- toasts (bottom centre)
        _toasts = new VBoxContainer { Name = "Toasts", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        _toasts.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _toasts.OffsetBottom = -16; _toasts.GrowVertical = GrowDirection.Begin; _toasts.GrowHorizontal = GrowDirection.Both;
        AddChild(_toasts);

        Windows = new Windows { Name = "Windows", Session = Session };
        AddChild(Windows);

        Session.Tools.ToolChanged += OnToolChanged;
        Session.Tools.SelectionChanged += hit => Inspector.Show(hit);
        Session.WorldChanged += () => { Inspector.Show(WorldHit.None); RebuildPicker(); Windows.OnWorldChanged(); };
        OnToolChanged(ToolKind.Select);
    }

    public void ApplyScale(float s) { if (IsInsideTree()) Theme = UiKit.BuildTheme(s); }

    private void OnToolChanged(ToolKind k)
    {
        foreach (var (kind, b) in _toolButtons) b.SetPressedNoSignal(kind == k);
        _speciesPicker.Visible = k is ToolKind.IntroduceFlora or ToolKind.IntroduceFauna;
        RebuildPicker();
        var t = Session.Tools;
        _toolInfo.Text = k switch
        {
            ToolKind.Nutrients => $"Radius {t.NutrientRadius:0.00} m",
            ToolKind.PlaceRock => $"Size {t.RockScale:0.00} m",
            ToolKind.PlaceLog => $"Length {t.LogLength:0.0} m  (R rotates)",
            ToolKind.PlaceGravel => $"Radius {t.GravelRadius:0.00} m",
            ToolKind.MoveProp => "Moving a prop: click its new spot",
            ToolKind.Grab => "Click a critter, then click where to release it",
            _ => "",
        };
    }

    private void RebuildPicker()
    {
        if (Session.World == null || _speciesPicker == null) return;
        _speciesPicker.Clear(); _pickerIds.Clear();
        var k = Session.Tools.Current;
        if (k == ToolKind.IntroduceFlora) foreach (var sp in Session.Content.Flora) { _speciesPicker.AddItem(sp.Name); _pickerIds.Add(sp.Id); }
        else if (k == ToolKind.IntroduceFauna) foreach (var sp in Session.Content.Fauna) { _speciesPicker.AddItem(sp.Name); _pickerIds.Add(sp.Id); }
        string? cur = k == ToolKind.IntroduceFlora ? Session.Tools.FloraSpecies : Session.Tools.FaunaSpecies;
        int idx = _pickerIds.IndexOf(cur ?? "");
        if (idx >= 0) _speciesPicker.Select(idx);
    }

    private void PickSpecies(int i)
    {
        if (i < 0 || i >= _pickerIds.Count) return;
        if (Session.Tools.Current == ToolKind.IntroduceFlora) Session.Tools.FloraSpecies = _pickerIds[i];
        else Session.Tools.FaunaSpecies = _pickerIds[i];
    }

    public void ChooseSpecies(string id, bool fauna)
    {
        if (fauna) { Session.Tools.FaunaSpecies = id; Session.Tools.SetTool(ToolKind.IntroduceFauna); }
        else { Session.Tools.FloraSpecies = id; Session.Tools.SetTool(ToolKind.IntroduceFlora); }
    }

    public void Toast(string message, bool isError)
    {
        if (_toasts == null) return;
        var l = UiKit.Label(message, 15, isError ? new Color(1f, 0.6f, 0.55f) : new Color(0.8f, 1f, 0.85f));
        var p = UiKit.Panel("Toast", l);
        p.MouseFilter = MouseFilterEnum.Ignore;
        _toasts.AddChild(p);
        // RemoveChild now (QueueFree alone would not change the count until the frame ends → infinite loop)
        while (_toasts.GetChildCount() > 4) { var old = _toasts.GetChild(0); _toasts.RemoveChild(old); old.QueueFree(); }
        GetTree().CreateTimer(isError ? 4.0 : 2.5).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
        (isError ? (Action<LogCategory, string>)Log.Info : Log.Debug)(LogCategory.UI, "toast: " + message);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo || Session.CameraRig.KeyboardBlocked) return;
        switch (k.Keycode)
        {
            case Key.P: Session.Host?.TogglePause(); break;
            case Key.Comma: Session.Host?.Clock.Slower(); break;
            case Key.Period: Session.Host?.Clock.Faster(); break;
            case Key.F1: Windows.Toggle("Help"); break;
            case Key.F3: Windows.Toggle("Debug"); break;
            case Key.C: Windows.Toggle("Catalog"); break;
            case Key.T: Windows.Toggle("Stats"); break;
            case Key.S when k.CtrlPressed: Windows.QuickSave(); break;
            case Key.Tab: foreach (var c in GetChildren()) if (c is Control ctl && ctl.Name != "Windows" && ctl.Name != "Toasts") ctl.Visible = !ctl.Visible; break;
            default: return;
        }
        RefreshClock();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        _refresh += delta;
        if (_refresh < 0.2) return;
        _refresh = 0;
        RefreshClock();
        RefreshProbe();
        Inspector.Refresh();
        Windows.Refresh();
    }

    private void RefreshClock()
    {
        var host = Session.Host;
        if (host == null) return;
        var c = host.Clock;
        _clock.Text = c.CalendarText();
        _pause.Text = c.Paused ? "▶ Resume" : "⏸ Pause";
        _speed.Text = c.Paused ? "paused" : $"{c.SpeedMultiplier:0.###}×";
        var w = host.World;
        string autosave = Session.LastAutosaveUtc.HasValue ? $" · autosaved {Session.LastAutosaveUtc.Value.ToLocalTime():HH:mm}" : "";
        if (Session.Autosave?.Busy == true) autosave = " · autosaving…";
        double backlog = w.Scheduler.Backlog;
        string lag = backlog > 600 ? $" · catching up {backlog / 60:0} min" : "";
        _status.Text = $"{w.Flora.Count} flora · {w.Fauna.Count} fauna · {Engine.GetFramesPerSecond()} fps{autosave}{lag}";
    }

    private void RefreshProbe()
    {
        var w = Session.World;
        if (w == null) return;
        var hit = Session.Tools.Hover.IsHit ? Session.Tools.Hover : Session.Tools.Selected;
        if (!hit.IsHit) { _probe.Text = "[color=#9ab]Point at the island to probe it.[/color]"; return; }
        var p = hit.Point.XZ;
        if (!w.Domain.Contains(p)) { _probe.Text = "outside the island"; return; }
        var f = w.Fields;
        int cell = w.Grid.NearestDomainCell(p);
        double depth = w.Water.DepthAt(p);
        var flow = new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]);
        double speed = depth > 1e-4 ? flow.Length / Math.Max(depth * w.Grid.CellSize, 1e-6) : 0;
        var tags = string.Join(", ", w.Props.HabitatTagsAt(p).Distinct());
        _probe.Text =
            $"Substrate [b]{SubstrateIds.Id(w.SubstrateAt(p))}[/b]{(tags.Length > 0 ? $" ({tags})" : "")}\n" +
            $"Light      {f.Light.Sample(p):0.00}  {UiKit.Bar(f.Light.Sample(p))}\n" +
            $"Moisture   {f.Moisture.Sample(p):0.00}  {UiKit.Bar(f.Moisture.Sample(p))}\n" +
            $"Nutrients  {f.Nutrients.Sample(p):0.00}  {UiKit.Bar(f.Nutrients.Sample(p) / w.Content.Ecology.NutrientMax)}\n" +
            $"Detritus   {f.Detritus.Sample(p):0.00}\n" +
            $"Water      {depth * 100:0.0} cm" + (depth >= w.Water.Config.WetDepth ? $", flow {speed * 3600:0.00} m/h" : "") + "\n" +
            (depth >= w.Water.Config.WetDepth ? $"Biofilm {f.Biofilm.Sample(p):0.00} · plankton {f.Plankton.Sample(p):0.00}\n" : "") +
            $"Elevation  {w.SurfaceHeight(p):0.00} m · {w.Strata.Layers[0].Name}";
    }
}
