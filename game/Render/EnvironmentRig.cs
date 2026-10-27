using System.Diagnostics;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

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
    public DirectionalLight3D Fill => _fill;
    public static EnvironmentRig? Instance { get; private set; }
    public Environment Env { get; private set; } = null!;
    public CameraAttributesPractical CameraAttributes { get; private set; } = null!;
    /// <summary>Presentation day/night clock driving the lighting. Read the rig, never the simulation.</summary>
    public DayNightClock Clock { get; private set; } = null!;
    private ColorRect _underwaterRect = null!;
    private ShaderMaterial _underwaterMat = null!;
    private ShaderMaterial _sky = null!;
    private FogVolume? _habitatMist;
    public float CanopyEnergy { get; private set; } = 1;
    public Vector3 CanopyLightDirection { get; private set; } = new(.4f, .8f, -.3f);
    private DirectionalLight3D _fill = null!;
    private readonly Stopwatch _monotonic = new();
    private float _underwater;       // 0..1 blend
    private float _underwaterTarget;
    public int Quality { get; private set; } = 2;
    public bool VolumetricMistEnabled { get; set; } = true;
    /// <summary>Opt-in diagnostic presentation phase; null keeps the normal wall clock. Never serialized.</summary>
    public double? BenchmarkLightSeconds { get; set; }

    // Pale warm-grey/green humid haze — reads as damp terrarium air, not smoke or mist.
    private static readonly Color HazeFogColor = new(0.050f, 0.044f, 0.036f);
    private const float HazeFogDensity = 0.022f;

    // Vivarium grade applied on top of the day/night keys (scaled, not replaced): dark enclosure
    // backdrop instead of sky, steep cool-white grow light, soft top fill, green/warm ambient bounce.
    private static readonly Color GrowLightTint = new(0.95f, 0.98f, 1.00f);
    private static readonly Color AmbientBounceColor = new(0.69f, 0.71f, 0.67f);
    private const float GrowSunGain = 0.92f, GrowFillGain = 0.28f, GrowAmbientGain = 1.26f, GrowAmbientSky = 0.20f;
    // Selected in the populated habitat: lift shaded surfaces while retaining directional canopy light.
    // The gain follows solar energy, fading out before deep night.
    private const float DayAmbientGain = 0.55f, DayFillGain = 1.35f;
    private const float DaySkyContribution = 0.85f, NightSkyContribution = .85f;
    private const float DayExposure = 1.18f, NightExposure = .92f;
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
        new(300.0f, -50f, 1.24f, 0.22f, 0.49f, 0.55f, 1.00f, new Color(1.00f, 0.95f, 0.88f), new Color(0.30f, 0.44f, 0.57f), new Color(0.65f, 0.73f, 0.76f), new Color(0.43f, 0.48f, 0.47f), new Color(0.65f, 0.70f, 0.68f)),
        // Dusk begins at 660 seconds; daylight now spans the declared 15-minute day segment.
        new(660.0f, -28f, 0.92f, 0.18f, 0.41f, 0.55f, 0.92f, new Color(1.00f, 0.86f, 0.66f), new Color(0.32f, 0.38f, 0.48f), new Color(0.70f, 0.66f, 0.60f), new Color(0.40f, 0.42f, 0.42f), new Color(0.62f, 0.62f, 0.60f)),
        new(780.0f, -9f, 0.46f, 0.12f, 0.28f, 0.50f, 0.72f, new Color(1.00f, 0.64f, 0.40f), new Color(0.24f, 0.24f, 0.36f), new Color(0.62f, 0.44f, 0.38f), new Color(0.30f, 0.30f, 0.34f), new Color(0.48f, 0.38f, 0.36f)),
        new(870.0f, -2f, 0.14f, 0.07f, 0.17f, 0.46f, 0.48f, new Color(0.96f, 0.50f, 0.32f), new Color(0.14f, 0.15f, 0.26f), new Color(0.40f, 0.26f, 0.30f), new Color(0.16f, 0.15f, 0.19f), new Color(0.34f, 0.28f, 0.28f)),
        // Night begins at 900 seconds, matching DayNightClock and the night-flora ramp.
        new(900.0f, -10f, 0.030f, 0.030f, 0.070f, 0.40f, 0.22f, new Color(0.44f, 0.54f, 0.80f), new Color(0.030f, 0.038f, 0.075f), new Color(0.062f, 0.072f, 0.120f), new Color(0.020f, 0.024f, 0.040f), new Color(0.050f, 0.056f, 0.090f)),
        new(1080.0f, -22f, 0.014f, 0.022f, 0.048f, 0.40f, 0.14f, new Color(0.40f, 0.50f, 0.82f), new Color(0.016f, 0.021f, 0.050f), new Color(0.034f, 0.040f, 0.078f), new Color(0.012f, 0.015f, 0.030f), new Color(0.032f, 0.038f, 0.070f)),
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
        Clock = new DayNightClock(() => BenchmarkLightSeconds ?? _monotonic.Elapsed.TotalSeconds);

        Instance = this;
        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High, true);
        RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Circle);

        _sky = Bridge.Shader("res://Shaders/canopy_sky.gdshader");
        bool dofDisabled = System.Environment.GetEnvironmentVariable("VIVARIUM_DISABLE_DOF") == "1";
        CameraAttributes = new CameraAttributesPractical
        {
            AutoExposureEnabled = true,
            AutoExposureMinSensitivity = 85.0f,
            AutoExposureMaxSensitivity = 115.0f,
            AutoExposureSpeed = 0.8f,
            AutoExposureScale = 0.22f,
            ExposureMultiplier = 1.0f,
            ExposureSensitivity = 100.0f,
            DofBlurFarEnabled = !dofDisabled,
            DofBlurNearEnabled = !dofDisabled,
        };
        Env = new Environment
        {
            BackgroundMode = Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, ProcessMode = Sky.ProcessModeEnum.Incremental },
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
            GlowEnabled = true,
            GlowNormalized = true,
            GlowIntensity = 0.28f,
            GlowStrength = 0.85f,
            GlowBloom = 0.04f,
            GlowBlendMode = Environment.GlowBlendModeEnum.Softlight,
            GlowHdrThreshold = 1.15f,
            GlowHdrScale = 1.8f,
            // Humid-air haze: gentle aerial perspective, not a fog wall. Kept very low density so
            // organisms at normal interaction distance (0.3-3 m) stay crisp; only the far ~15 m
            // island edge softens. Underwater blends on top of this in _Process, never replaces it.
            FogEnabled = true,
            FogLightColor = HazeFogColor,
            FogLightEnergy = 1.0f,
            FogDensity = HazeFogDensity * 0.35f,
            FogSunScatter = 0.45f,
            FogSkyAffect = 0.15f,
            FogAerialPerspective = 0.18f,
            // Multi-bounce SDFGI with 5 cm probe cells covering the 10-16 m island
            SdfgiEnabled = true,
            SdfgiUseOcclusion = true,
            SdfgiReadSkyLight = true,
            SdfgiBounceFeedback = 0.85f,
            SdfgiCascades = 4,
            SdfgiMinCellSize = 0.05f,
            SdfgiCascade0Distance = 3.2f,
            SdfgiMaxDistance = 25.6f,
            SdfgiYScale = Environment.SdfgiyScale.Scale100Percent,
            SdfgiEnergy = 1.15f,
            // Screen-Space Reflections (SSR)
            SsrEnabled = true,
            SsrMaxSteps = 64,
            SsrFadeIn = 0.08f,
            SsrFadeOut = 2.0f,
            SsrDepthTolerance = 0.08f,
            // Contact & micro-crevice SSAO (darkens crevices even in direct sun)
            SsaoEnabled = true,
            SsaoRadius = 0.14f,
            SsaoIntensity = 1.25f,
            SsaoPower = 1.15f,
            SsaoDetail = 0.5f,
            SsaoHorizon = 0.08f,
            SsaoSharpness = 0.98f,
            SsaoLightAffect = 0.42f,
            SsaoAOChannelAffect = 0.90f,
            // Screen-Space Indirect Lighting (SSIL)
            SsilEnabled = true,
            SsilRadius = 0.85f,
            SsilIntensity = 1.05f,
            SsilSharpness = 0.95f,
            SsilNormalRejection = 1.0f,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = 0.0045f,
            VolumetricFogAlbedo = new Color(0.82f, 0.88f, 0.78f),
            VolumetricFogAnisotropy = 0.72f,
            VolumetricFogLength = 22.0f,
            VolumetricFogDetailSpread = 0.65f,
            VolumetricFogAmbientInject = 0.25f,
            VolumetricFogSkyAffect = 0.15f,
            VolumetricFogTemporalReprojectionEnabled = true,
            VolumetricFogTemporalReprojectionAmount = 0.90f,
        };
        Env.SetGlowLevel(0, 0.0f);
        Env.SetGlowLevel(1, 0.6f);
        Env.SetGlowLevel(2, 1.0f);
        Env.SetGlowLevel(3, 0.7f);
        Env.SetGlowLevel(4, 0.35f);
        Env.SetGlowLevel(5, 0.15f);

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
            // 8K directional cascade shadows with sub-millimeter contact precision for 2 mm stems/leaves/pebbles
            ShadowBias = 0.0035f,
            ShadowNormalBias = 0.12f,
            ShadowBlur = 1.15f,
            DirectionalShadowMaxDistance = 22.0f,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowSplit1 = 0.05f,
            DirectionalShadowSplit2 = 0.16f,
            DirectionalShadowSplit3 = 0.42f,
            DirectionalShadowPancakeSize = 0.5f,
        };
        Sun.RotationDegrees = new Vector3(-52, -35, 0);
        AddChild(Sun);
        _fill = new DirectionalLight3D
        {
            LightColor = GrowLightTint,
            LightEnergy = 0.11f,
            ShadowEnabled = false,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
            ShadowBlur = 3.5f,
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
        NightFloraMaterials.SetNightFactor(NightFloraMaterials.NightFactorAtSeconds(Clock.Seconds));
    }

    public void ApplyIndirectLightingDetail(bool fine)
    {
        // Linked distance properties use these values for the same 25.6 m reach.
        int cascades = fine ? 4 : 3;
        float cellSize = fine ? .025f : .05f;
        // Unrelated settings edits must not restart the GI volume.
        if (Env.SdfgiCascades != cascades) Env.SdfgiCascades = cascades;
        if (!Mathf.IsEqualApprox(Env.SdfgiMinCellSize, cellSize)) Env.SdfgiMinCellSize = cellSize;
    }

    public void ApplyQuality(int tier)
    {
        Quality = Mathf.Clamp(tier, 0, 2);
        var vp = GetViewport();

        switch (Quality)
        {
            case 0:
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.VeryLow, false, 0.5f, 1, 50f, 300f);
                RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.VeryLow, false, 0.5f, 1, 50f, 300f);
                RenderingServer.DirectionalShadowAtlasSetSize(2048, true);
                RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.Hard);
                Sun.ShadowEnabled = false;
                Env.SsaoEnabled = false; Env.GlowEnabled = false;
                Env.SsilEnabled = false; Env.VolumetricFogEnabled = false;
                Env.SdfgiEnabled = false; Env.SsrEnabled = false;
                vp.Msaa3D = Viewport.Msaa.Disabled; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.UseTaa = false;
                vp.Scaling3DScale = 0.75f;
                break;
            case 1:
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, false, 0.5f, 2, 50f, 300f);
                RenderingServer.DirectionalShadowAtlasSetSize(4096, true);
                RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftMedium);
                Sun.ShadowEnabled = true;
                Env.SsaoEnabled = true; Env.GlowEnabled = true;
                Env.SsilEnabled = false; Env.VolumetricFogEnabled = true;
                Env.SdfgiEnabled = true; Env.SsrEnabled = false;
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.UseTaa = true;
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                Sun.DirectionalShadowSplit1 = 0.22f;
                vp.Scaling3DScale = 1.0f;
                break;
            case 2:
            default:
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Ultra, false, 0.5f, 2, 50f, 300f);
                RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Ultra, false, 0.5f, 4, 50f, 300f);
                RenderingServer.DirectionalShadowAtlasSetSize(8192, true);
                RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftUltra);
                RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftUltra);
                Sun.ShadowEnabled = true;
                Env.SsaoEnabled = true; Env.GlowEnabled = true;
                Env.SsilEnabled = true; Env.VolumetricFogEnabled = true;
                Env.SdfgiEnabled = true; Env.SsrEnabled = true;
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.UseTaa = true;
                Sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
                vp.Scaling3DScale = 1.0f;
                break;
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        base._ExitTree();
    }

    public void SetUnderwater(bool under) => _underwaterTarget = under ? 1 : 0;
    public bool UnderwaterVisual => _underwater > 0.5f;

    public void ConfigureHabitat(VivariumWorld world)
    {
        if (_habitatMist == null)
        {
            _habitatMist = new FogVolume
            {
                Name = "BoundedHabitatMist", Shape = RenderingServer.FogVolumeShape.Cylinder,
                Material = new FogMaterial { Density = 0.014f, Albedo = new Color(0.88f, 0.92f, 0.86f), EdgeFade = 1.8f },
            };
            AddChild(_habitatMist);
        }
        else if (_habitatMist.Material is FogMaterial fogMat)
        {
            fogMat.Density = 0.014f;
            fogMat.Albedo = new Color(0.88f, 0.92f, 0.86f);
            fogMat.EdgeFade = 1.8f;
        }
        // The cylinder fits INSIDE the inscribed circle of the hexagon, leaving a clear
        // perimeter. No atmosphere exists beyond the floating slice or in the void.
        float diameter = (float)world.Domain.Apothem * 1.78f;
        float ground = (float)world.Terrain.Height(Vivarium.Sim.Core.Vec2.Zero);
        _habitatMist.Size = new Vector3(diameter, 3.2f, diameter);
        _habitatMist.Position = new Vector3(0, ground + 1.5f, 0);

        var probe = GetNodeOrNull<ReflectionProbe>("HabitatReflectionProbe");
        if (probe == null)
        {
            probe = new ReflectionProbe
            {
                Name = "HabitatReflectionProbe",
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
                BoxProjection = true,
                EnableShadows = true,
                AmbientMode = ReflectionProbe.AmbientModeEnum.Environment,
            };
            AddChild(probe);
#pragma warning disable CS0618
            RenderingServer.ReflectionProbeSetResolution(probe.GetBase(), 512);
#pragma warning restore CS0618
        }
        probe.Size = new Vector3(diameter * 1.3f, 8.0f, diameter * 1.3f);
        probe.Position = new Vector3(0, ground + 2.5f, 0);
    }

    /// <summary>
    /// Resample the lighting table at the clock's current position and push it to the scene graph. Pure
    /// struct reads and property writes, a short linear scan and one float lerp chain per field: no heap
    /// allocation, so this is safe to run every frame.
    /// </summary>
    private void ApplyDayNight()
    {
        double t = Clock.Seconds;
        int n = Keys.Length;
        int i = n - 1;
        for (int k = 0; k < n - 1; k++) if (t < Keys[k + 1].F) { i = k; break; }
        var a = Keys[i];
        // The final segment closes the loop: the last sample blends into the first one across the wrap.
        bool wrap = i == n - 1;
        var b = wrap ? Keys[0] : Keys[i + 1];
        float tEnd = wrap ? (float)DayNightClock.CycleSeconds : Keys[i + 1].F;
        float span = tEnd - a.F;
        float w = span > 0f ? Mathf.Clamp((float)((t - a.F) / span), 0f, 1f) : 0f;

        float pitch = Mathf.Max(Mathf.Lerp(a.Pitch, b.Pitch, w) * GrowPitchGain, GrowPitchMax);
        float az = SunAzimuth(t);
        Sun.RotationDegrees = new Vector3(pitch, az, 0f);
        Color sunCol = a.SunColor.Lerp(b.SunColor, w).Lerp(GrowLightTint, 0.15f);
        Sun.LightColor = sunCol;
        float sunE = Mathf.Lerp(a.SunEnergy, b.SunEnergy, w);
        Sun.LightEnergy = sunE * GrowSunGain;
        float daylight = Mathf.SmoothStep(.07f, .50f, sunE);
        Env.TonemapExposure = Mathf.Lerp(NightExposure, DayExposure, daylight);

        // Fill directional light: soft area-like top fill at low energy, matching color family
        _fill.RotationDegrees = new Vector3(-76f, az + 140f, 0f);
        _fill.LightColor = sunCol;
        _fill.LightEnergy = Mathf.Lerp(a.FillEnergy, b.FillEnergy, w) * GrowFillGain * Mathf.Lerp(1f, DayFillGain, daylight);

        CanopyEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, w) / .5f;
        CanopyLightDirection = Sun.GlobalBasis.Z;

        Color interpSunCol = a.SunColor.Lerp(b.SunColor, w);
        Color interpSkyTop = a.SkyTop.Lerp(b.SkyTop, w);
        Color interpSkyHorizon = a.SkyHorizon.Lerp(b.SkyHorizon, w);
        Color interpGroundBottom = a.GroundBottom.Lerp(b.GroundBottom, w);
        Color interpGroundHorizon = a.GroundHorizon.Lerp(b.GroundHorizon, w);

        _sky.SetShaderParameter("sun_color", new Vector3(interpSunCol.R, interpSunCol.G, interpSunCol.B));
        _sky.SetShaderParameter("sky_top_color", new Vector3(interpSkyTop.R, interpSkyTop.G, interpSkyTop.B));
        _sky.SetShaderParameter("sky_horizon_color", new Vector3(interpSkyHorizon.R, interpSkyHorizon.G, interpSkyHorizon.B));
        _sky.SetShaderParameter("ground_bottom_color", new Vector3(interpGroundBottom.R, interpGroundBottom.G, interpGroundBottom.B));
        _sky.SetShaderParameter("ground_horizon_color", new Vector3(interpGroundHorizon.R, interpGroundHorizon.G, interpGroundHorizon.B));
        _sky.SetShaderParameter("sun_energy", sunE);
        _sky.SetShaderParameter("light_direction", CanopyLightDirection);
        _sky.SetShaderParameter("sky_energy", CanopyEnergy);

        // Ambient bounce light: green/warm-tinted ambient bounce so shaded understory/strata/trunks
        // do not crush to pitch black.
        Color ambientTint = AmbientBounceColor.Lerp(a.SunColor.Lerp(b.SunColor, w), 0.20f);
        Env.AmbientLightColor = ambientTint;
        Env.AmbientLightEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, w) * GrowAmbientGain * Mathf.Lerp(1f, DayAmbientGain, daylight);
        Env.AmbientLightSkyContribution = Mathf.Lerp(NightSkyContribution, DaySkyContribution, daylight);
        Env.FogLightEnergy = Mathf.Lerp(a.FogEnergy, b.FogEnergy, w);
    }

    /// <summary>Sun heading sweeps a full turn per cycle, starting at the calibrated daytime heading.</summary>
    private float SunAzimuth(double seconds) =>
        Mathf.PosMod(SunAzimuthStart + SunAzimuthSweep * (float)(seconds / DayNightClock.CycleSeconds), 360f);

    /// <summary>The night factor the bioluminescent flora uses. A performance
    /// run can force it to one so the night-time path is measured.</summary>
    private float NightFactor()
    {
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_FORCE_NIGHT") == "1") return 1f;
        return NightFloraMaterials.NightFactorAtSeconds(Clock.Seconds);
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Environment");
        ApplyDayNight();
        NightFloraMaterials.SetNightFactor(NightFactor());
        // quick crossfade so the medium change is visible but not jarring
        _underwater = Mathf.MoveToward(_underwater, _underwaterTarget, (float)delta * 5f);
        _underwaterRect.Visible = _underwater > 0.01f;
        _underwaterMat.SetShaderParameter("strength", _underwater * 0.85f);
        Env.FogEnabled = true;
        Env.FogLightColor = HazeFogColor.Lerp(UnderwaterFogColor, _underwater);
        Env.FogDensity = Mathf.Lerp(HazeFogDensity * 0.35f, UnderwaterFogDensity, _underwater);
        Env.FogAerialPerspective = Mathf.Lerp(0.18f, 0.0f, _underwater);
        Env.FogSunScatter = Mathf.Lerp(0.45f, 0.15f, _underwater);
        Env.VolumetricFogEnabled = Quality >= 1 && VolumetricMistEnabled;
        Env.VolumetricFogDensity = Mathf.Lerp(0.0045f, 0.045f, _underwater);
        Env.VolumetricFogAlbedo = new Color(0.82f, 0.88f, 0.78f).Lerp(UnderwaterFogColor, _underwater);
        Env.VolumetricFogAnisotropy = Mathf.Lerp(0.72f, 0.55f, _underwater);
        Env.VolumetricFogLength = 22.0f;
        Env.VolumetricFogDetailSpread = 0.65f;
        Env.VolumetricFogAmbientInject = 0.25f;
        Env.VolumetricFogSkyAffect = 0.15f;
        Env.VolumetricFogTemporalReprojectionEnabled = true;
        Env.VolumetricFogTemporalReprojectionAmount = 0.90f;
        Env.AdjustmentSaturation = Mathf.Lerp(0.97f, 0.94f, _underwater);
    }
}
