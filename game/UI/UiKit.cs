using System;
using System.Collections.Generic;
using Godot;

namespace Vivarium.Game.UI;

/// <summary>Small helpers so every panel is built from code with a consistent look.</summary>
public static class UiKit
{
    public static readonly Color PanelBg = new(0.07f, 0.1f, 0.13f, 0.86f);
    public static readonly Color Accent = new(0.36f, 0.8f, 0.62f);
    public static readonly Color Text = new(0.92f, 0.95f, 0.96f);
    public static readonly Color Muted = new(0.65f, 0.72f, 0.76f);

    public static Theme BuildTheme(float scale)
    {
        var t = new Theme { DefaultFontSize = (int)(15 * scale) };
        var panel = new StyleBoxFlat { BgColor = PanelBg, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 };
        panel.ContentMarginLeft = panel.ContentMarginRight = 10 * scale; panel.ContentMarginTop = panel.ContentMarginBottom = 8 * scale;
        t.SetStylebox("panel", "PanelContainer", panel);
        StyleBoxFlat Btn(Color c) { var s = new StyleBoxFlat { BgColor = c }; s.SetCornerRadiusAll(6); s.ContentMarginLeft = s.ContentMarginRight = 8 * scale; s.ContentMarginTop = s.ContentMarginBottom = 4 * scale; return s; }
        t.SetStylebox("normal", "Button", Btn(new Color(0.16f, 0.22f, 0.27f, 0.95f)));
        t.SetStylebox("hover", "Button", Btn(new Color(0.22f, 0.32f, 0.38f, 0.95f)));
        t.SetStylebox("pressed", "Button", Btn(new Color(0.18f, 0.5f, 0.42f, 0.95f)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetStylebox("disabled", "Button", Btn(new Color(0.12f, 0.14f, 0.16f, 0.8f)));
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_color", "Label", Text);
        t.SetColor("default_color", "RichTextLabel", Text);
        return t;
    }

    public static PanelContainer Panel(string name, Control content)
    {
        var p = new PanelContainer { Name = name, MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddChild(content);
        return p;
    }

    public static Label Label(string text, int size = 0, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        if (size > 0) l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static Button Button(string name, string text, Action onPressed, string? tooltip = null, bool toggle = false)
    {
        var b = new Button { Name = name, Text = text, TooltipText = tooltip ?? "", ToggleMode = toggle, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPressed;
        return b;
    }

    public static RichTextLabel Rich(string name, float minHeight = 0)
    {
        return new RichTextLabel { Name = name, BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(0, minHeight), SelectionEnabled = false };
    }

    public static HSlider Slider(string name, double min, double max, double step, double value, Action<double> changed)
    {
        var s = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(180, 0) };
        s.ValueChanged += v => changed(v);
        return s;
    }

    public static HBoxContainer Row(params Control[] children)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        foreach (var c in children) h.AddChild(c);
        return h;
    }

    public static VBoxContainer Column(params Control[] children)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        foreach (var c in children) v.AddChild(c);
        return v;
    }

    public static Control Spacer(bool expand = true) => new Control { SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>
    /// A frosted-glass window with a title bar and close button. The body scrolls, and Windows.Layout keeps the
    /// window inside the viewport and below the top bar.
    /// </summary>
    public static (PanelContainer Root, VBoxContainer Body) Window(string name, string title, Vector2 size, Action onClose)
    {
        var body = Column();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { Name = name + "_Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(size.X - 28, size.Y - 60) };
        scroll.AddChild(body);
        var header = Row(Label(title, 19, Accent), Spacer(), Button(name + "_Close", "✕", onClose, "Close"));
        var col = Column(header, new HSeparator(), scroll);
        var panel = new GlassPanel(14, 10) { Name = name, Corner = 14 };
        panel.AddChild(col);
        panel.SetMeta("requested_size", size);
        panel.Visible = false;
        return (panel, body);
    }

    public static string Bar(double fraction, int width = 10)
    {
        int n = (int)Math.Round(Math.Clamp(fraction, 0, 1) * width);
        return new string('█', n) + new string('░', width - n);
    }
}

/// <summary>Minimal trend chart (render-only; the data it draws is a UI-side ring buffer).</summary>
public partial class Sparkline : Control
{
    public List<float> Values { get; } = new();
    public Color LineColor { get; set; } = UiKit.Accent;
    public override void _Ready() { CustomMinimumSize = new Vector2(140, 26); MouseFilter = MouseFilterEnum.Ignore; }
    public void SetValues(IEnumerable<float> v) { Values.Clear(); Values.AddRange(v); QueueRedraw(); }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(1, 1, 1, 0.05f));
        if (Values.Count < 2) return;
        float max = 1; foreach (var v in Values) max = Math.Max(max, v);
        var pts = new Vector2[Values.Count];
        for (int i = 0; i < Values.Count; i++)
            pts[i] = new Vector2(i * Size.X / (Values.Count - 1), Size.Y - 2 - Values[i] / max * (Size.Y - 4));
        DrawPolyline(pts, LineColor, 1.5f, true);
    }
}
