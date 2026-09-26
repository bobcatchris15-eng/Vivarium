using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Tools;

namespace Vivarium.Game.UI;

/// <summary>
/// Tool wheel that pops up at the cursor on a right-click tap. Inner ring: the tools plus two groups
/// (Terrain, Water). Choosing a group opens its tools; Add flora opens placement drawers before species,
/// while Add fauna opens species directly. Esc, right-click or clicking outside closes it. Tool buttons persist (hidden) so keyboard
/// shortcuts and scripted runs can press them too.
/// </summary>
public partial class RadialMenu : Control
{
    public GameSession Session { get; set; } = null!;
    private readonly List<(ToolKind Kind, Button Button)> _tools = new();
    private readonly List<(string Id, Button Button)> _groups = new();
    private readonly List<(ToolKind Kind, Button Button)> _subTools = new();
    private readonly List<Button> _species = new();
    private GlassPanel _hub = null!;
    private Label _hubLabel = null!;
    private Control _ring = null!;
    private Vector2 _center;
    private Tween? _tween;
    public bool IsOpen { get; private set; }
    public const float InnerRadius = 140, OuterRadius = 240, ButtonSize = 70;

    private static readonly (ToolKind Kind, string Glyph, string Name)[] Items =
    {
        (ToolKind.Select, "◎", "Inspect"), (ToolKind.Grab, "✋", "Grab"), (ToolKind.RemovePlant, "✂", "Pick plant"),
        (ToolKind.Nutrients, "✦", "Nutrients"), (ToolKind.Poke, "↯", "Poke"), (ToolKind.PlaceRock, "⬢", "Rock"),
        (ToolKind.PlaceLog, "▭", "Log"), (ToolKind.PlaceGravel, "⁂", "Gravel"), (ToolKind.IntroduceFlora, "❀", "Add flora"),
        (ToolKind.IntroduceFauna, "✺", "Add fauna"),
    };

    private static readonly (string Id, string Glyph, string Name, ToolKind[] Members)[] Groups =
    {
        ("Terrain", "▲", "Terrain", new[] { ToolKind.TerrainRaise, ToolKind.TerrainLower, ToolKind.TerrainSmooth }),
        ("Water", "≋", "Water", new[] { ToolKind.PourWater, ToolKind.DrainWater, ToolKind.Spring }),
    };

    private static readonly (ToolKind Kind, string Glyph, string Name)[] SubItems =
    {
        (ToolKind.TerrainRaise, "▲", "Raise"), (ToolKind.TerrainLower, "▼", "Lower"), (ToolKind.TerrainSmooth, "∿", "Smooth"),
        (ToolKind.PourWater, "⇣", "Pour"), (ToolKind.DrainWater, "⇡", "Soak up"), (ToolKind.Spring, "⊚", "Spring"),
    };

    private static readonly (FloraPlacementGroup Group, string Glyph, string ButtonName)[] FloraGroups =
    {
        (FloraPlacementGroup.MossLichen, "▧", "Moss &\nlichen"),
        (FloraPlacementGroup.Terrestrial, "♧", "Terrestrial"),
        (FloraPlacementGroup.WatersideAquatic, "≈", "Waterside &\naquatic"),
        (FloraPlacementGroup.Decomposer, "◌", "Decomposers"),
    };

    private static int InnerCount => Items.Length + Groups.Length;

    public static string NameOf(ToolKind k)
    {
        if (k == ToolKind.MoveProp) return "Move prop";
        foreach (var i in Items) if (i.Kind == k) return i.Name;
        foreach (var i in SubItems) if (i.Kind == k) return i.Name;
        return k.ToString();
    }

    public static string GlyphOf(ToolKind k)
    {
        if (k == ToolKind.MoveProp) return "⇄";
        foreach (var i in Items) if (i.Kind == k) return i.Glyph;
        foreach (var i in SubItems) if (i.Kind == k) return i.Glyph;
        return "•";
    }

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
        foreach (var (id, glyph, name, _) in Groups)
        {
            var b = MakeButton("Group_" + id, $"{glyph}\n{name}", () => OpenGroup(id));
            b.MouseEntered += () => _hubLabel.Text = name;
            _groups.Add((id, b));
        }
        foreach (var (kind, glyph, name) in SubItems)
        {
            var b = MakeButton("Tool_" + kind, $"{glyph}\n{name}", () => { Session.Tools.SetTool(kind); Close(); });
            b.MouseEntered += () => _hubLabel.Text = name;
            _subTools.Add((kind, b));
        }
    }

    private Button MakeButton(string name, string text, Action pressed)
    {
        var b = new Button { Name = name, Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(ButtonSize, ButtonSize), Visible = false, ToggleMode = true };
        b.AddThemeFontSizeOverride("font_size", 12);
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

    private static float InnerAngle(int index) => -Mathf.Pi / 2 + index * Mathf.Tau / InnerCount;

    public void Open(Vector2 at)
    {
        _openedAt = at;
        PlaceCenter(OuterRadius);
        ClearOuter();
        var current = Session.Tools.Current;
        for (int i = 0; i < InnerCount; i++)
        {
            var b = i < _tools.Count ? _tools[i].Button : _groups[i - _tools.Count].Button;
            b.Visible = true;
            bool on = i < _tools.Count ? _tools[i].Kind == current : Array.IndexOf(Groups[i - _tools.Count].Members, current) >= 0;
            b.SetPressedNoSignal(on);
        }
        _hub.Size = _hub.CustomMinimumSize;
        LayoutInner();
        _hubLabel.Text = NameOf(current);
        Visible = true; IsOpen = true;
        _ring.PivotOffset = _center;
        _ring.Scale = new Vector2(0.7f, 0.7f); _ring.Modulate = new Color(1, 1, 1, 0);
        _tween?.Kill();
        _tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_ring, "scale", Vector2.One, 0.16);
        _tween.TweenProperty(_ring, "modulate:a", 1.0f, 0.12);
    }

    private Vector2 _openedAt;

    /// <summary>Centres the wheel at the click, pushed inward so a ring of <paramref name="radius"/> stays on screen.</summary>
    private void PlaceCenter(float radius)
    {
        var vp = GetViewportRect().Size;
        float margin = radius + ButtonSize / 2 + 8;
        _center = new Vector2(Mathf.Clamp(_openedAt.X, margin, Math.Max(margin, vp.X - margin)), Mathf.Clamp(_openedAt.Y, margin, Math.Max(margin, vp.Y - margin)));
        _ring.PivotOffset = _center;
    }

    private void LayoutInner()
    {
        for (int i = 0; i < InnerCount; i++)
        {
            var b = i < _tools.Count ? _tools[i].Button : _groups[i - _tools.Count].Button;
            b.Position = _center + Vector2.FromAngle(InnerAngle(i)) * InnerRadius - b.CustomMinimumSize / 2;
        }
        _hub.Position = _center - _hub.CustomMinimumSize / 2;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _tween?.Kill();
        Visible = false;
        foreach (var (_, b) in _tools) b.Visible = false;
        foreach (var (_, b) in _groups) b.Visible = false;
        ClearOuter();
    }

    private void Choose(ToolKind kind)
    {
        Session.Tools.SetTool(kind);
        if (kind is ToolKind.IntroduceFlora or ToolKind.IntroduceFauna && IsOpen) { ShowSpecies(kind == ToolKind.IntroduceFauna); return; }
        Close();
    }

    /// <summary>Opens a group's tools on the outer ring (or, when the wheel is closed, just selects its first tool).</summary>
    private void OpenGroup(string id)
    {
        int gi = Array.FindIndex(Groups, g => g.Id == id);
        var members = Groups[gi].Members;
        if (!IsOpen) { Session.Tools.SetTool(members[0]); return; }
        ClearOuter();
        foreach (var (gid, b) in _groups) b.SetPressedNoSignal(gid == id);
        var buttons = _subTools.FindAll(t => Array.IndexOf(members, t.Kind) >= 0);
        FanOut(InnerAngle(_tools.Count + gi), buttons.ConvertAll(t => t.Button));
        foreach (var (kind, b) in buttons) b.SetPressedNoSignal(kind == Session.Tools.Current);
        _hubLabel.Text = Groups[gi].Name;
    }

    private void ShowSpecies(bool fauna)
    {
        if (!fauna) { ShowFloraGroups(); return; }
        ClearOuter();
        int toolIndex = _tools.FindIndex(t => t.Kind == ToolKind.IntroduceFauna);
        var made = new List<Button>();
        foreach (var sp in Session.Content.Fauna)
        {
            var id = sp.Id; var name = sp.Name;
            var b = MakeButton("Species_" + id, name.Replace(' ', '\n'), () => { Session.Tools.FaunaSpecies = id; Session.Ui.RefreshToolChip(); Close(); });
            b.SetPressedNoSignal(id == Session.Tools.FaunaSpecies);
            b.MouseEntered += () => _hubLabel.Text = name;
            _species.Add(b); made.Add(b);
        }
        FanOut(InnerAngle(toolIndex), made);
        _hubLabel.Text = "Choose an animal";
    }

    private void ShowFloraGroups()
    {
        ClearOuter();
        int toolIndex = _tools.FindIndex(t => t.Kind == ToolKind.IntroduceFlora);
        var made = new List<Button>();
        foreach (var (group, glyph, buttonName) in FloraGroups)
        {
            var g = group;
            var b = MakeButton("FloraGroup_" + FloraPlacementGroups.Id(g), $"{glyph}\n{buttonName}", () => ShowFloraSpecies(g));
            b.MouseEntered += () => _hubLabel.Text = FloraPlacementGroups.Name(g);
            _species.Add(b); made.Add(b);
        }
        FanOut(InnerAngle(toolIndex), made);
        _hubLabel.Text = "Choose flora";
    }

    private void ShowFloraSpecies(FloraPlacementGroup group)
    {
        ClearOuter();
        int toolIndex = _tools.FindIndex(t => t.Kind == ToolKind.IntroduceFlora);
        var made = new List<Button>();
        var back = MakeButton("FloraGroup_Back", "↩\nFlora", ShowFloraGroups);
        back.MouseEntered += () => _hubLabel.Text = "Back to flora groups";
        _species.Add(back); made.Add(back);
        foreach (var sp in Session.Content.Flora)
        {
            if (sp.PlacementGroup != group) continue;
            var id = sp.Id; var name = sp.Name;
            var b = MakeButton("Species_" + id, name.Replace(' ', '\n'), () => { Session.Tools.FloraSpecies = id; Session.Ui.RefreshToolChip(); Close(); });
            b.SetPressedNoSignal(id == Session.Tools.FloraSpecies);
            b.MouseEntered += () => _hubLabel.Text = name;
            _species.Add(b); made.Add(b);
        }
        FanOut(InnerAngle(toolIndex), made);
        _hubLabel.Text = FloraPlacementGroups.Name(group);
    }

    /// <summary>
    /// Spreads buttons along the outer ring, centred on the direction of the item that opened them. If a submenu
    /// outgrows one ring, the rest spill onto further rings outward so buttons never overlap; the wheel is
    /// re-centred if the extra rings would leave the screen.
    /// </summary>
    private void FanOut(float baseAng, List<Button> buttons)
    {
        const float Gap = 10;
        var rings = new List<(float Radius, int Count)>();
        int left = buttons.Count;
        for (float r = OuterRadius; left > 0; r += ButtonSize + Gap)
        {
            int cap = Math.Max(1, (int)(Mathf.Tau * r / (ButtonSize + Gap)));
            int n = Math.Min(cap, left);
            rings.Add((r, n)); left -= n;
        }
        float outer = rings[^1].Radius;
        if (outer > OuterRadius) { PlaceCenter(outer); LayoutInner(); }
        int k = 0;
        foreach (var (r, n) in rings)
        {
            float step = Math.Min((ButtonSize + Gap) / r, Mathf.Tau / n);
            for (int i = 0; i < n; i++, k++)
            {
                float ang = baseAng + (i - (n - 1) / 2f) * step;
                buttons[k].Position = _center + Vector2.FromAngle(ang) * r - buttons[k].CustomMinimumSize / 2;
                buttons[k].Visible = true;
            }
        }
    }

    private void ClearOuter()
    {
        foreach (var b in _species) b.QueueFree();
        _species.Clear();
        foreach (var (_, b) in _subTools) b.Visible = false;
    }

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
