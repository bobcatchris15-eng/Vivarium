using Godot;
using Vivarium.Game.App;

namespace Vivarium.Game.Render;

/// <summary>
/// Lighting, sky, tone mapping and post-processing tuned for "vibrant sanitized realism": clean saturated colour,
/// soft but present shadows, no crushed blacks. Quality tiers change render cost only — never simulation.
/// </summary>
public partial class EnvironmentRig : Node3D
{
    public WorldEnvironment WorldEnv { get; private set; } = null!;
    public DirectionalLight3D Sun { get; private set; } = null!;
    public Environment Env { get; private set; } = null!;
    private ColorRect _underwaterRect = null!;
    private ShaderMaterial _underwaterMat = null!;
    private float _underwater;       // 0..1 blend
    private float _underwaterTarget;
    public int Quality { get; private set; } = -1;

    public override void _Ready()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.34f, 0.52f, 0.72f),
            SkyHorizonColor = new Color(0.72f, 0.80f, 0.86f),
            GroundBottomColor = new Color(0.48f, 0.58f, 0.64f),
            GroundHorizonColor = new Color(0.70f, 0.78f, 0.83f),
            SunAngleMax = 30, SunCurve = 0.12f,
        };
        Env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.86f, 0.84f, 0.8f),
            AmbientLightSkyContribution = 0.45f,
            AmbientLightEnergy = 0.78f,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Agx,
            TonemapExposure = 1.0f,
            TonemapWhite = 6f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.07f,
            AdjustmentContrast = 1.02f,
            AdjustmentBrightness = 1.0f,
            GlowEnabled = false,
            FogEnabled = false,
            SsaoRadius = 0.6f, SsaoIntensity = 1.2f,
        };
        WorldEnv = new WorldEnvironment { Environment = Env };
        AddChild(WorldEnv);

        Sun = new DirectionalLight3D
        {
            LightColor = new Color(1.0f, 0.96f, 0.9f),
            LightEnergy = 1.22f,
            ShadowEnabled = true,
            // The entire specimen is only 10–20 m across. Spend the directional map on that scale instead of
            // the engine's generic scene scale, and keep bias small enough that leaf/stem contact shadows stay attached.
            ShadowBias = 0.035f,
            ShadowNormalBias = 0.85f,
            ShadowBlur = 1.15f,
            DirectionalShadowMaxDistance = 28,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        };
        Sun.RotationDegrees = new Vector3(-52, -35, 0);
        AddChild(Sun);
        var fill = new DirectionalLight3D { LightColor = new Color(0.75f, 0.85f, 1.0f), LightEnergy = 0.14f, ShadowEnabled = false };
        fill.RotationDegrees = new Vector3(-20, 150, 0);
        AddChild(fill);

        // underwater screen treatment (subtle; keeps fauna inspectable)
        var layer = new CanvasLayer { Layer = -1 };
        AddChild(layer);
        _underwaterMat = Bridge.Shader("res://Shaders/underwater.gdshader");
        _underwaterRect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _underwaterMat, Visible = false };
        _underwaterRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_underwaterRect);
    }

    public void ApplyQuality(int tier)
    {
        Quality = Mathf.Clamp(tier, 0, 2);
        var vp = GetViewport();
        switch (Quality)
        {
            case 0:
                Sun.ShadowEnabled = false;
                Env.SsaoEnabled = false; Env.GlowEnabled = false;
                vp.Msaa3D = Viewport.Msaa.Disabled; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.Scaling3DScale = 0.75f;
                break;
            case 1:
                Sun.ShadowEnabled = true;
                RenderingServer.DirectionalShadowAtlasSetSize(2048, true);
                Env.SsaoEnabled = false; Env.GlowEnabled = false;
                vp.Msaa3D = Viewport.Msaa.Disabled; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                vp.Scaling3DScale = 1.0f;
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                break;
            default:
                Sun.ShadowEnabled = true;
                RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
                Env.SsaoEnabled = true; Env.GlowEnabled = false;
                vp.Msaa3D = Viewport.Msaa.Msaa4X; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
                vp.Scaling3DScale = 1.0f;
                break;
        }
    }

    public void SetUnderwater(bool under) => _underwaterTarget = under ? 1 : 0;
    public bool UnderwaterVisual => _underwater > 0.5f;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Environment");
        // quick crossfade so the medium change is visible but not jarring
        _underwater = Mathf.MoveToward(_underwater, _underwaterTarget, (float)delta * 5f);
        _underwaterRect.Visible = _underwater > 0.01f;
        _underwaterMat.SetShaderParameter("strength", _underwater * 0.85f);
        Env.FogEnabled = _underwater > 0.01f;
        if (Env.FogEnabled)
        {
            Env.FogLightColor = new Color(0.32f, 0.62f, 0.66f);
            Env.FogDensity = 0.18f * _underwater;
            Env.FogSkyAffect = 1.0f;
        }
        Env.AdjustmentSaturation = Mathf.Lerp(1.07f, 1.02f, _underwater);
    }
}
