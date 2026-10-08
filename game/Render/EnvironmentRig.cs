using System.Diagnostics;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Time;

namespace Vivarium.Game.Render;

/// <summary>
/// Daylight calibrated for natural tissue and soil reflectance. Quality tiers change render cost only.
///
/// The sun, sky dome, ground dome, fill light and ambient term are driven per frame from
/// <see cref="DayNightClock"/>, a PRESENTATION-ONLY wall-clock cycle (15 real minutes of day, 5 of night).
/// The keyframe table below is anchored on the calibrated daytime look, so midday reproduces the values
/// that were tuned for tissue/soil reflectance and only the night segment deviates. The clock is never
/// consulted by the simulation, is not saved, and registers no scheduler cadence.
/// </summary>
public partial class EnvironmentRig : Node3D
{
    public WorldEnvironment WorldEnv { get; private set; } = null!;
    public DirectionalLight3D Sun { get; private set; } = null!;
    public Environment Env { get; private set; } = null!;
    public CameraAttributesPractical CameraAttributes { get; private set; } = null!;
    /// <summary>Presentation day/night clock driving the lighting. Read the rig, never the simulation.</summary>
    public DayNightClock Clock { get; private set; } = null!;
    private ColorRect _underwaterRect = null!;
    private ShaderMaterial _underwaterMat = null!;
    private ProceduralSkyMaterial _sky = null!;
    private DirectionalLight3D _fill = null!;
    private readonly Stopwatch _monotonic = new();
    private float _underwater;       // 0..1 blend
    private float _underwaterTarget;
    public int Quality { get; private set; } = -1;

    // Pale warm-grey/green humid haze — reads as damp terrarium air, not smoke or mist.
    private static readonly Color HazeFogColor = new(0.050f, 0.044f, 0.036f);
    private const float HazeFogDensity = 0.022f;

    // Vivarium grade applied on top of the day/night keys (scaled, not replaced): dark enclosure
    // backdrop instead of sky, steep cool-white grow light, soft top fill, green/warm ambient bounce.
    private static readonly Color BackdropTop = new(0.030f, 0.025f, 0.020f);
    private static readonly Color BackdropHorizon = new(0.060f, 0.048f, 0.036f);
    private static readonly Color GrowLightTint = new(0.95f, 0.98f, 1.00f);
    private static readonly Color AmbientBounceColor = new(0.44f, 0.54f, 0.34f);
    private const float GrowSunGain = 1.38f, GrowFillGain = 0.48f, GrowAmbientGain = 0.95f, GrowAmbientSky = 0.08f;
    private const float GrowPitchGain = 1.35f, GrowPitchMax = -80f, KeyDaySunEnergy = 1.30f;
    private static readonly Color UnderwaterFogColor = new(0.12f, 0.20f, 0.14f);
    private const float UnderwaterFogDensity = 0.18f;

    /// <summary>Azimuth at the start of the cycle, matching the calibrated daytime sun heading.</summary>
    private const float SunAzimuthStart = -35f;
    /// <summary>Degrees the sun heading advances over one full cycle.</summary>
    private const float SunAzimuthSweep = 360f;

    /// <summary>One lighting sample: everything the day/night cycle is allowed to move.</summary>
    private readonly struct SkyKey
    {
        public readonly float F;                 // cycle second at which this sample is exact
        public readonly float Pitch;             // sun X rotation, degrees; negative puts the sun above the horizon
        public readonly float SunEnergy;
        public readonly float FillEnergy;
        public readonly float AmbientEnergy;
        public readonly float AmbientSkyContribution;
        public readonly float FogEnergy;
        public readonly Color SunColor;
        public readonly Color SkyTop;
        public readonly Color SkyHorizon;
        public readonly Color GroundBottom;
        public readonly Color GroundHorizon;

        public SkyKey(float f, float pitch, float sunEnergy, float fillEnergy, float ambientEnergy,
            float ambientSkyContribution, float fogEnergy, Color sunColor, Color skyTop, Color skyHorizon,
            Color groundBottom, Color groundHorizon)
        {
            F = f; Pitch = pitch; SunEnergy = sunEnergy; FillEnergy = fillEnergy;
            AmbientEnergy = ambientEnergy; AmbientSkyContribution = ambientSkyContribution; FogEnergy = fogEnergy;
            SunColor = sunColor; SkyTop = skyTop; SkyHorizon = skyHorizon;
            GroundBottom = groundBottom; GroundHorizon = groundHorizon;
        }
    }

    /// <summary>
    /// Cycle keyframes, in seconds from the start of the cycle. The first three samples are the calibrated
    /// daytime look; the remaining seven carry the sun down through dusk into night and back up through dawn,
    /// so the cycle closes seamlessly on the first sample.
    /// </summary>
    private static readonly SkyKey[] Keys =
    {
        //  sec   pitch  sun    fill   amb    ambSky fog    sun colour         sky top           sky horizon        ground bottom      ground horizon
        new(0.0000f, -52f, 1.26f, 0.22f, 0.49f, 0.55f, 1.00f, new Color(1.00f, 0.95f, 0.87f), new Color(0.30f, 0.44f, 0.57f), new Color(0.65f, 0.73f, 0.76f), new Color(0.43f, 0.48f, 0.47f), new Color(0.65f, 0.70f, 0.68f)),
        new(60.0f, -60f, 1.30f, 0.22f, 0.50f, 0.55f, 1.00f, new Color(1.00f, 0.97f, 0.92f), new Color(0.29f, 0.43f, 0.58f), new Color(0.64f, 0.73f, 0.77f), new Color(0.43f, 0.48f, 0.47f), new Color(0.65f, 0.70f, 0.68f)),
        new(150.0f, -50f, 1.24f, 0.22f, 0.49f, 0.55f, 1.00f, new Color(1.00f, 0.95f, 0.88f), new Color(0.30f, 0.44f, 0.57f), new Color(0.65f, 0.73f, 0.76f), new Color(0.43f, 0.48f, 0.47f), new Color(0.65f, 0.70f, 0.68f)),
        // Dusk: the sun rakes down and the palette turns warm. This is the last 240 s of the day segment.
        new(300.0f, -28f, 0.92f, 0.18f, 0.41f, 0.55f, 0.92f, new Color(1.00f, 0.86f, 0.66f), new Color(0.32f, 0.38f, 0.48f), new Color(0.70f, 0.66f, 0.60f), new Color(0.40f, 0.42f, 0.42f), new Color(0.62f, 0.62f, 0.60f)),
        new(450.0f, -9f, 0.46f, 0.12f, 0.28f, 0.50f, 0.72f, new Color(1.00f, 0.64f, 0.40f), new Color(0.24f, 0.24f, 0.36f), new Color(0.62f, 0.44f, 0.38f), new Color(0.30f, 0.30f, 0.34f), new Color(0.48f, 0.38f, 0.36f)),
        new(540.0f, -2f, 0.14f, 0.07f, 0.17f, 0.46f, 0.48f, new Color(0.96f, 0.50f, 0.32f), new Color(0.14f, 0.15f, 0.26f), new Color(0.40f, 0.26f, 0.30f), new Color(0.16f, 0.15f, 0.19f), new Color(0.34f, 0.28f, 0.28f)),
        // Night: the first 240 s of the night segment, then deep night.
        new(660.0f, -10f, 0.030f, 0.030f, 0.070f, 0.40f, 0.22f, new Color(0.44f, 0.54f, 0.80f), new Color(0.030f, 0.038f, 0.075f), new Color(0.062f, 0.072f, 0.120f), new Color(0.020f, 0.024f, 0.040f), new Color(0.050f, 0.056f, 0.090f)),
        new(900.0f, -22f, 0.014f, 0.022f, 0.048f, 0.40f, 0.14f, new Color(0.40f, 0.50f, 0.82f), new Color(0.016f, 0.021f, 0.050f), new Color(0.034f, 0.040f, 0.078f), new Color(0.012f, 0.015f, 0.030f), new Color(0.032f, 0.038f, 0.070f)),
        // Dawn: the last 60 s of the night segment, then it wraps back to the calibrated daytime sample.
        new(1140.0f, -12f, 0.022f, 0.026f, 0.075f, 0.42f, 0.20f, new Color(0.46f, 0.55f, 0.84f), new Color(0.032f, 0.040f, 0.082f), new Color(0.085f, 0.090f, 0.155f), new Color(0.018f, 0.022f, 0.038f), new Color(0.060f, 0.065f, 0.105f)),
        new(1170.0f, -4f, 0.22f, 0.09f, 0.20f, 0.50f, 0.55f, new Color(1.00f, 0.72f, 0.50f), new Color(0.19f, 0.21f, 0.34f), new Color(0.55f, 0.40f, 0.38f), new Color(0.22f, 0.20f, 0.22f), new Color(0.40f, 0.32f, 0.32f)),
    };

    public override void _Ready()
    {
        // PRESENTATION clock: a monotonic wall-clock source, injected. Wall-driven means it is not
        // reproducible from a save, so it is never serialized and never reaches the scheduler.
        // The stopwatch MUST be started here: a Stopwatch that was never started reports Elapsed == 0
        // forever, which pins the whole cycle to its first keyframe (flat midday, sun energy 1.26).
        _monotonic.Start();
        Clock = new DayNightClock(() => _monotonic.Elapsed.TotalSeconds);

        _sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.30f, 0.44f, 0.57f),
            SkyHorizonColor = new Color(0.65f, 0.73f, 0.76f),
            GroundBottomColor = new Color(0.43f, 0.48f, 0.47f),
            GroundHorizonColor = new Color(0.65f, 0.70f, 0.68f),
            SunAngleMax = 30, SunCurve = 0.12f,
        };
        CameraAttributes = new CameraAttributesPractical
        {
            AutoExposureEnabled = true,
            AutoExposureMinSensitivity = 70.0f,
            AutoExposureMaxSensitivity = 210.0f,
            AutoExposureSpeed = 0.6f,
            AutoExposureScale = 0.40f,
            ExposureMultiplier = 1.0f,
            ExposureSensitivity = 100.0f,
        };
        Env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Environment.AmbientSource.Sky,
            AmbientLightColor = AmbientBounceColor,
            AmbientLightSkyContribution = 0.08f,
            AmbientLightEnergy = 0.48f,
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
            SsaoRadius = 0.32f, SsaoIntensity = 0.85f, SsaoPower = 1.05f,
        };
        WorldEnv = new WorldEnvironment
        {
            Environment = Env,
            CameraAttributes = CameraAttributes,
        };
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
        _fill = new DirectionalLight3D
        {
            LightColor = GrowLightTint,
            LightEnergy = 0.11f,
            ShadowEnabled = false,
        };
        _fill.RotationDegrees = new Vector3(-76, 105, 0);
        AddChild(_fill);

        // underwater screen treatment (subtle; keeps fauna inspectable)
        var layer = new CanvasLayer { Layer = -1 };
        AddChild(layer);
        _underwaterMat = Bridge.Shader("res://Shaders/underwater.gdshader");
        _underwaterRect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _underwaterMat, Visible = false };
        _underwaterRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_underwaterRect);

        ApplyDayNight();
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

    /// <summary>
    /// Resample the lighting table at the clock's current position and push it to the scene graph. Pure
    /// struct reads and property writes, a short linear scan and one float lerp chain per field: no heap
    /// allocation, so this is safe to run every frame.
    /// </summary>
    private void ApplyDayNight()
    {
        double t = Clock.Seconds;
        int n = Keys.Length;
        int i = n - 2;
        for (int k = 0; k < n - 1; k++) if (t < Keys[k + 1].F) { i = k; break; }
        var a = Keys[i];
        // The final segment closes the loop: the last sample blends into the first one across the wrap.
        bool wrap = i == n - 2;
        var b = wrap ? Keys[0] : Keys[i + 1];
        float tEnd = wrap ? (float)DayNightClock.CycleSeconds : Keys[i + 1].F;
        float span = tEnd - a.F;
        float w = span > 0f ? Mathf.Clamp((float)((t - a.F) / span), 0f, 1f) : 0f;

        float pitch = Mathf.Max(Mathf.Lerp(a.Pitch, b.Pitch, w) * GrowPitchGain, GrowPitchMax);
        float az = SunAzimuth(t);
        Sun.RotationDegrees = new Vector3(pitch, az, 0f);
        Color sunCol = a.SunColor.Lerp(b.SunColor, w).Lerp(GrowLightTint, 0.65f);
        Sun.LightColor = sunCol;
        float sunE = Mathf.Lerp(a.SunEnergy, b.SunEnergy, w);
        Sun.LightEnergy = sunE * GrowSunGain;

        // Fill directional light: soft area-like top fill at low energy, matching color family
        _fill.RotationDegrees = new Vector3(-76f, az + 140f, 0f);
        _fill.LightColor = sunCol;
        _fill.LightEnergy = Mathf.Lerp(a.FillEnergy, b.FillEnergy, w) * GrowFillGain;

        // Enclosure backdrop: dark wood/rock brown, dimmed further as the keys go to night.
        float lum = Mathf.Clamp(sunE / KeyDaySunEnergy, 0.15f, 1f);
        _sky.SkyTopColor = BackdropTop * lum;
        _sky.SkyHorizonColor = BackdropHorizon * lum;
        _sky.GroundBottomColor = BackdropTop * lum;
        _sky.GroundHorizonColor = BackdropHorizon * lum;

        // Ambient bounce light: green/warm-tinted ambient bounce so shaded understory/strata/trunks
        // do not crush to pitch black.
        Color ambientTint = AmbientBounceColor.Lerp(a.SunColor.Lerp(b.SunColor, w), 0.20f);
        Env.AmbientLightColor = ambientTint;
        Env.AmbientLightEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, w) * GrowAmbientGain;
        Env.AmbientLightSkyContribution = Mathf.Lerp(a.AmbientSkyContribution, b.AmbientSkyContribution, w) * GrowAmbientSky / 0.55f;
        Env.FogLightEnergy = Mathf.Lerp(a.FogEnergy, b.FogEnergy, w);
    }

    /// <summary>Sun heading sweeps a full turn per cycle, starting at the calibrated daytime heading.</summary>
    private float SunAzimuth(double seconds) =>
        Mathf.PosMod(SunAzimuthStart + SunAzimuthSweep * (float)(seconds / DayNightClock.CycleSeconds), 360f);

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Environment");
        ApplyDayNight();
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
