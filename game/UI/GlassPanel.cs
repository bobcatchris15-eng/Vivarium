using Godot;
using Vivarium.Game.App;

namespace Vivarium.Game.UI;

/// <summary>
/// PanelContainer drawn as frosted dark glass (blurred backdrop + tint + rounded rim).
/// Children are laid out inside the given padding like a normal panel.
/// </summary>
public partial class GlassPanel : PanelContainer
{
    private readonly ColorRect _glass;
    private readonly ShaderMaterial _mat;
    public float Corner { get; set; } = 12;
    public bool Circle { get; set; }

    public GlassPanel(float padH = 12, float padV = 8)
    {
        MouseFilter = MouseFilterEnum.Stop;
        var box = new StyleBoxEmpty { ContentMarginLeft = padH, ContentMarginRight = padH, ContentMarginTop = padV, ContentMarginBottom = padV };
        AddThemeStyleboxOverride("panel", box);
        _mat = Bridge.Shader("res://Shaders/glass.gdshader").Duplicate() as ShaderMaterial ?? Bridge.Shader("res://Shaders/glass.gdshader");
        _glass = new ColorRect { Name = "Glass", Material = _mat, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
        AddChild(_glass, false, InternalMode.Front);
        ItemRectChanged += Sync;
    }

    public override void _Ready() => Sync();

    private void Sync()
    {
        // the glass rect ignores the content margins so it covers the whole panel
        _glass.Position = Vector2.Zero;
        _glass.Size = Size;
        _mat.SetShaderParameter("rect_size", Size);
        _mat.SetShaderParameter("corner", Corner);
        _mat.SetShaderParameter("circle", Circle ? 1.0f : 0.0f);
    }

    public override void _Notification(int what)
    {
        // PanelContainer re-fits children on sort; keep the backdrop full-size afterwards
        if (what == NotificationSortChildren) CallDeferred(MethodName.Sync);
    }
}
