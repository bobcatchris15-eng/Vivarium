using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Tools;

namespace Vivarium.Game.UI;

/// <summary>
/// Tool wheel that pops up at the cursor on a right-click tap. Inner ring: the ten tools. Choosing
/// "Add flora" / "Add fauna" opens an outer ring of species. Esc, right-click or clicking outside closes it.
/// Tool buttons persist (hidden) so keyboard shortcuts and scripted runs can press them too.
/// </summary>
public partial class RadialMenu : Control
{
    public GameSession Session { get; set; } = null!;
    private readonly List<(ToolKind Kind, Button Button)> _tools = new();
    private readonly List<Button> _species = new();
    private GlassPanel _hub = null!;
    private Label _hubLabel = null!;
    private Control _ring = null!;
    private Vector2 _center;
    private Tween? _tween;
    public bool IsOpen { get; private set; }
    public const float InnerRadius = 118, OuterRadius = 222, ButtonSize = 78;

    private static readonly (ToolKind Kind, string Glyph, string Name)[] Items =
    {
        (ToolKind.Select, "◎", "Inspect"), (ToolKind.Grab, "✋", "Grab"), (ToolKind.RemovePlant, "✂", "Pick plant"),
        (ToolKind.Nutrients, "✦", "Nutrients"), (ToolKind.Poke, "↯", "Poke"), (ToolKind.PlaceRock, "⬢", "Rock"),
        (ToolKind.PlaceLog, "▭", "Log"), (ToolKind.PlaceGravel, "⁂", "Gravel"), (ToolKind.IntroduceFlora, "❀", "Add flora"),
        (ToolKind.IntroduceFauna, "✺", "Add fauna"),
    };

    public static string NameOf(ToolKind k) => k == ToolKind.MoveProp ? "Move prop" : Array.Find(Items, i => i.Kind == k).Name ?? k.ToString();
    public static string GlyphOf(ToolKind k) => k == ToolKind.MoveProp ? "⇄" : Array.Find(Items, i => i.Kind == k).Glyph ?? "•";

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;          // clicking outside the wheel closes it
        Visible = false;
        _ring = new Control { Name = "Ring", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_ring);
        _hub = new GlassPanel(6, 6) { Name = "Hub", Circle = true, CustomMinimumSize = new Vector2(96, 96) };
        _hubLabel = UiKit.Label("", 13, UiKit.Text, wrap: true);
        _hubLabel.HorizontalAlignment = HorizontalAlignment.Center; _hubLabel.VerticalAlignment = VerticalAlignment.Center;
        _hub.AddChild(_hubLabel);
        _ring.AddChild(_hub);
        foreach (var (kind, glyph, name) in Items)
        {
            var b = MakeButton("Tool_" + kind, $"{glyph}\n{name}", () => Choose(kind));
            b.MouseEntered += () => _hubLabel.Text = name;
            _tools.Add((kind, b));
        }
    }

    private Button MakeButton(string name, string text, Action pressed)
    {
        var b = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(ButtonSize, ButtonSize), Visible = false, ToggleMode = true };
        b.AddThemeFontSizeOverride("font_size", 13);
        StyleBoxFlat Disc(Color c, Color border)
        {
            var s = new StyleBoxFlat { BgColor = c, BorderColor = border };
            s.SetCornerRadiusAll((int)(ButtonSize / 2) - 1); s.SetBorderWidthAll(1); s.AntiAliasingSize = 1.2f;
            s.ShadowColor = new Color(0, 0, 0, 0.35f); s.ShadowSize = 6;
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Disc(new Color(0.07f, 0.1f, 0.13f, 0.82f), new Color(1, 1, 1, 0.12f)));
        b.AddThemeStyleboxOverride("hover", Disc(new Color(0.14f, 0.24f, 0.26f, 0.92f), UiKit.Accent));
        b.AddThemeStyleboxOverride("pressed", Disc(new Color(0.16f, 0.46f, 0.38f, 0.95f), new Color(0.6f, 1f, 0.85f, 0.9f)));
        b.AddThemeStyleboxOverride("hover_pressed", Disc(new Color(0.2f, 0.52f, 0.44f, 0.95f), new Color(0.7f, 1f, 0.9f, 1f)));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += pressed;
        _ring.AddChild(b);
        return b;
    }

    public void Open(Vector2 at)
    {
        var vp = GetViewportRect().Size;
        // keep the whole wheel (incl. the species ring) on screen
        float margin = OuterRadius + ButtonSize / 2 + 8;
        _center = new Vector2(Mathf.Clamp(at.X, margin, Math.Max(margin, vp.X - margin)), Mathf.Clamp(at.Y, margin, Math.Max(margin, vp.Y - margin)));
        ClearSpecies();
        for (int i = 0; i < _tools.Count; i++)
        {
            float ang = -Mathf.Pi / 2 + i * Mathf.Tau / _tools.Count;
            var b = _tools[i].Button;
            b.Position = _center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * InnerRadius - b.CustomMinimumSize / 2;
            b.Visible = true;
            b.SetPressedNoSignal(_tools[i].Kind == Session.Tools.Current);
        }
        _hub.Position = _center - _hub.CustomMinimumSize / 2;
        _hub.Size = _hub.CustomMinimumSize;
        _hubLabel.Text = NameOf(Session.Tools.Current);
        Visible = true; IsOpen = true;
        _ring.PivotOffset = _center;
        _ring.Scale = new Vector2(0.7f, 0.7f); _ring.Modulate = new Color(1, 1, 1, 0);
        _tween?.Kill();
        _tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_ring, "scale", Vector2.One, 0.16);
        _tween.TweenProperty(_ring, "modulate:a", 1.0f, 0.12);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _tween?.Kill();
        Visible = false;
        foreach (var (_, b) in _tools) b.Visible = false;
        ClearSpecies();
    }

    private void Choose(ToolKind kind)
    {
        Session.Tools.SetTool(kind);
        if (kind is ToolKind.IntroduceFlora or ToolKind.IntroduceFauna && IsOpen) { ShowSpecies(kind == ToolKind.IntroduceFauna); return; }
        Close();
    }

    private void ShowSpecies(bool fauna)
    {
        ClearSpecies();
        var list = new List<(string Id, string Name)>();
        if (fauna) foreach (var sp in Session.Content.Fauna) list.Add((sp.Id, sp.Name));
        else foreach (var sp in Session.Content.Flora) list.Add((sp.Id, sp.Name));
        // fan the species around the chosen tool's direction
        int toolIndex = _tools.FindIndex(t => t.Kind == (fauna ? ToolKind.IntroduceFauna : ToolKind.IntroduceFlora));
        float baseAng = -Mathf.Pi / 2 + toolIndex * Mathf.Tau / _tools.Count;
        float step = (ButtonSize + 12) / OuterRadius;   // keep neighbouring species buttons apart
        for (int i = 0; i < list.Count; i++)
        {
            var (id, name) = list[i];
            float ang = baseAng + (i - (list.Count - 1) / 2f) * step;
            var b = MakeButton("Species_" + id, name.Replace(' ', '\n'), () => { if (fauna) Session.Tools.FaunaSpecies = id; else Session.Tools.FloraSpecies = id; Session.Ui.RefreshToolChip(); Close(); });
            b.Position = _center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * OuterRadius - b.CustomMinimumSize / 2;
            b.Visible = true;
            b.SetPressedNoSignal(id == (fauna ? Session.Tools.FaunaSpecies : Session.Tools.FloraSpecies));
            b.MouseEntered += () => _hubLabel.Text = name;
            _species.Add(b);
        }
        _hubLabel.Text = fauna ? "Choose an animal" : "Choose a plant";
    }

    private void ClearSpecies() { foreach (var b in _species) b.QueueFree(); _species.Clear(); }

    public override void _GuiInput(InputEvent e)
    {
        // a click on the dimmed background (not on a button) closes the wheel
        if (e is InputEventMouseButton mb && mb.Pressed) { Close(); AcceptEvent(); }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (IsOpen && e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape) { Close(); GetViewport().SetInputAsHandled(); }
    }
}
