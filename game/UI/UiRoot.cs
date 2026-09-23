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
    private Label _clock = null!, _speed = null!, _status = null!, _toolInfo = null!, _toolChip = null!;
    public const float TopBarHeight = 52;
    public RadialMenu Radial { get; private set; } = null!;
    private Button _pause = null!;
    private VBoxContainer _toasts = null!;
    public Inspector Inspector { get; private set; } = null!;
    private RichTextLabel _probe = null!;
    public Windows Windows { get; private set; } = null!;
    private double _refresh;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UiKit.BuildTheme((float)Session.Settings.UiScale);

        // ---- top bar (frosted glass): clock + speed + status | menus
        _clock = UiKit.Label("Week 1, Day 1  00:00", 18);
        _clock.Name = "ClockLabel";
        _pause = UiKit.Button("PauseButton", "⏸ Pause", () => { Session.Host?.TogglePause(); RefreshClock(); }, "Pause / resume the simulation (P)");
        var slower = UiKit.Button("SlowerButton", "◀◀", () => { Session.Host?.Clock.Slower(); RefreshClock(); }, "Slower (,)");
        _speed = UiKit.Label("1×", 16); _speed.Name = "SpeedLabel"; _speed.CustomMinimumSize = new Vector2(64, 0); _speed.HorizontalAlignment = HorizontalAlignment.Center;
        var faster = UiKit.Button("FasterButton", "▶▶", () => { Session.Host?.Clock.Faster(); RefreshClock(); }, "Faster (.)");
        _status = UiKit.Label("", 13, UiKit.Muted); _status.Name = "StatusLabel";
        _status.ClipText = true; _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; _status.CustomMinimumSize = new Vector2(80, 0);
        _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var menus = UiKit.Row(
            UiKit.Button("CatalogButton", "Species", () => Windows.Toggle("Catalog"), "Species catalog (C)"),
            UiKit.Button("StatsButton", "Stats", () => Windows.Toggle("Stats"), "Ecosystem statistics (T)"),
            UiKit.Button("SaveButton", "Save", () => Windows.Toggle("SaveLoad"), "Save / load (Ctrl+S)"),
            UiKit.Button("NewWorldButton", "New", () => Windows.Toggle("NewWorld"), "Create a new vivarium"),
            UiKit.Button("SettingsButton", "Settings", () => Windows.Toggle("Settings")),
            UiKit.Button("DebugButton", "Debug", () => Windows.Toggle("Debug"), "Developer overlays (F3)"),
            UiKit.Button("HelpButton", "Help", () => Windows.Toggle("Help"), "Controls (F1)"));
        foreach (var c in menus.GetChildren()) if (c is Button mb) GlassButton(mb);
        foreach (var bt in new[] { _pause, slower, faster }) GlassButton(bt);
        var top = UiKit.Row(_clock, _pause, slower, _speed, faster, _status, menus);
        top.AddThemeConstantOverride("separation", 8);
        var topPanel = new GlassPanel(14, 7) { Name = "TopBar", Corner = 14 };
        topPanel.AddChild(top);
        topPanel.SetAnchorsPreset(LayoutPreset.TopWide);
        topPanel.OffsetLeft = 10; topPanel.OffsetRight = -10; topPanel.OffsetTop = 8;
        AddChild(topPanel);

        // ---- current tool chip (tools live in the right-click wheel)
        _toolChip = UiKit.Label("", 14);
        _toolChip.Name = "ToolChip";
        _toolInfo = UiKit.Label("", 12, UiKit.Muted); _toolInfo.Name = "ToolInfo";
        var chip = new GlassPanel(12, 6) { Name = "ToolChipPanel", Corner = 12 };
        chip.AddChild(UiKit.Column(_toolChip, _toolInfo));
        chip.SetAnchorsPreset(LayoutPreset.TopLeft);
        chip.OffsetLeft = 10; chip.OffsetTop = TopBarHeight + 8;
        AddChild(chip);

        // ---- inspector (right)
        Inspector = new Inspector { Name = "Inspector", Session = Session };
        Inspector.SetAnchorsPreset(LayoutPreset.TopRight);
        Inspector.OffsetRight = -10; Inspector.OffsetTop = TopBarHeight + 8; Inspector.OffsetLeft = -350;
        AddChild(Inspector);

        // ---- environment probe (bottom-left)
        _probe = UiKit.Rich("ProbeText");
        _probe.CustomMinimumSize = new Vector2(300, 0);
        var probePanel = new GlassPanel(12, 8) { Name = "ProbePanel", Corner = 12 };
        probePanel.AddChild(UiKit.Column(UiKit.Label("Environment probe", 15, UiKit.Accent), _probe));
        probePanel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        probePanel.OffsetLeft = 10; probePanel.OffsetBottom = -10; probePanel.GrowVertical = GrowDirection.Begin;
        AddChild(probePanel);

        // ---- toasts (bottom centre)
        _toasts = new VBoxContainer { Name = "Toasts", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        _toasts.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _toasts.OffsetBottom = -16; _toasts.GrowVertical = GrowDirection.Begin; _toasts.GrowHorizontal = GrowDirection.Both;
        AddChild(_toasts);

        Windows = new Windows { Name = "Windows", Session = Session };
        AddChild(Windows);

        Radial = new RadialMenu { Name = "Radial", Session = Session };
        AddChild(Radial);
        Session.CameraRig.RightClicked += pos => { if (!Windows.AnyOpen) Radial.Open(pos); };

        Session.Tools.ToolChanged += OnToolChanged;
        Session.Tools.SelectionChanged += hit => Inspector.Show(hit);
        Session.WorldChanged += () => { Inspector.Show(WorldHit.None); RefreshToolChip(); Windows.OnWorldChanged(); };
        OnToolChanged(ToolKind.Select);
    }

    public void ApplyScale(float s) { if (IsInsideTree()) Theme = UiKit.BuildTheme(s); }

    private void OnToolChanged(ToolKind k) => RefreshToolChip();

    /// <summary>Frosted-glass look for top-bar buttons.</summary>
    private static void GlassButton(Button b)
    {
        StyleBoxFlat S(Color c) { var s = new StyleBoxFlat { BgColor = c }; s.SetCornerRadiusAll(8); s.ContentMarginLeft = s.ContentMarginRight = 9; s.ContentMarginTop = s.ContentMarginBottom = 4; return s; }
        b.AddThemeStyleboxOverride("normal", S(new Color(1, 1, 1, 0.05f)));
        b.AddThemeStyleboxOverride("hover", S(new Color(1, 1, 1, 0.13f)));
        b.AddThemeStyleboxOverride("pressed", S(new Color(0.36f, 0.8f, 0.62f, 0.35f)));
    }

    public void RefreshToolChip()
    {
        if (_toolChip == null) return;
        var t = Session.Tools;
        var k = t.Current;
        string species = k switch
        {
            ToolKind.IntroduceFlora => " · " + (Session.Content.FloraById(t.FloraSpecies ?? "")?.Name ?? "pick a plant"),
            ToolKind.IntroduceFauna => " · " + (Session.Content.FaunaById(t.FaunaSpecies ?? "")?.Name ?? "pick an animal"),
            _ => "",
        };
        _toolChip.Text = $"{RadialMenu.GlyphOf(k)}  {RadialMenu.NameOf(k)}{species}";
        _toolInfo.Text = (k switch
        {
            ToolKind.Nutrients => $"radius {t.NutrientRadius:0.00} m  ·  [ ] resize",
            ToolKind.PlaceRock => $"size {t.RockScale:0.00} m  ·  [ ] resize",
            ToolKind.PlaceLog => $"length {t.LogLength:0.0} m  ·  [ ] resize, R rotate",
            ToolKind.PlaceGravel => $"radius {t.GravelRadius:0.00} m  ·  [ ] resize",
            ToolKind.MoveProp => "click the prop's new spot",
            ToolKind.Grab => "click with a critter in the circle, then where to release it",
            _ => "",
        } + (k == ToolKind.Select ? "" : "\n") + "right-click for the tool wheel").Trim();
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
        _status.Text = $"{w.Flora.Count} flora · {w.Fauna.Count} fauna · fly {Session.CameraRig.Speed:0.0#} m/s · {Engine.GetFramesPerSecond()} fps{autosave}{lag}";
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
