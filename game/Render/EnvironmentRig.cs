using Godot;
using Vivarium.Game.App;

namespace Vivarium.Game.Render;

/// <summary>
/// Daylight calibrated for natural tissue and soil reflectance. Quality tiers change render cost only.
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

    // Pale warm-grey/green humid haze — reads as damp terrarium air, not smoke or mist.
    private static readonly Color HazeFogColor = new(0.72f, 0.75f, 0.71f);
    private const float HazeFogDensity = 0.006f;
    private static readonly Color UnderwaterFogColor = new(0.32f, 0.62f, 0.66f);
    private const float UnderwaterFogDensity = 0.18f;

    public override void _Ready()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.30f, 0.44f, 0.57f),
            SkyHorizonColor = new Color(0.65f, 0.73f, 0.76f),
            GroundBottomColor = new Color(0.43f, 0.48f, 0.47f),
            GroundHorizonColor = new Color(0.65f, 0.70f, 0.68f),
            SunAngleMax = 30, SunCurve = 0.12f,
        };
        Env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightColor = new Color(0.83f, 0.86f, 0.86f),
            AmbientLightSkyContribution = 0.55f,
            AmbientLightEnergy = 0.49f,
            ReflectedLightSource = Environment.ReflectionSource.Sky,
            TonemapMode = Environment.ToneMapper.Agx,
            TonemapExposure = 0.92f,
            TonemapWhite = 6f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.97f,
            AdjustmentContrast = 1.07f,
            AdjustmentBrightness = 1.0f,
            GlowEnabled = false,
            // Humid-air haze: gentle aerial perspective, not a fog wall. Kept very low density so
            // organisms at normal interaction distance (0.3-3 m) stay crisp; only the far ~15 m
            // island edge softens. Underwater blends on top of this in _Process, never replaces it.
            FogEnabled = true,
            FogLightColor = HazeFogColor,
            FogLightEnergy = 1.0f,
            FogDensity = HazeFogDensity,
            FogSunScatter = 0.35f,
            FogSkyAffect = 0.1f,
            FogAerialPerspective = 0.12f,
            SsaoRadius = 0.32f, SsaoIntensity = 0.56f, SsaoPower = 1.05f,
        };
        WorldEnv = new WorldEnvironment { Environment = Env };
        AddChild(WorldEnv);

        Sun = new DirectionalLight3D
        {
            LightColor = new Color(1.0f, 0.95f, 0.87f),
            LightEnergy = 1.26f,
            ShadowEnabled = true,
            // The entire specimen is only 10–20 m across. Spend the directional map on that scale instead of
            // the engine's generic scene scale, and keep bias small enough that leaf/stem contact shadows stay attached.
            ShadowBias = 0.035f,
            ShadowNormalBias = 0.35f,
            ShadowBlur = 1.6f,
            DirectionalShadowMaxDistance = 28,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
        };
        Sun.RotationDegrees = new Vector3(-52, -35, 0);
        AddChild(Sun);
        var fill = new DirectionalLight3D { LightColor = new Color(0.81f, 0.87f, 0.93f), LightEnergy = 0.22f, ShadowEnabled = false };
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
                // Contact AO at the default tier too; radius/intensity above already tuned cheap.
                Env.SsaoEnabled = true; Env.GlowEnabled = false;
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
        // Blend, never overwrite: humid-air haze stays the floor, underwater fog rides on top.
        Env.FogEnabled = true;
        Env.FogLightColor = HazeFogColor.Lerp(UnderwaterFogColor, _underwater);
        Env.FogDensity = Mathf.Lerp(HazeFogDensity, UnderwaterFogDensity, _underwater);
        Env.FogSkyAffect = Mathf.Lerp(0.1f, 1.0f, _underwater);
        Env.AdjustmentSaturation = Mathf.Lerp(0.97f, 0.94f, _underwater);
    }
}
