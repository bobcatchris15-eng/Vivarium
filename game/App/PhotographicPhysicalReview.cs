using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace Vivarium.Game.App;

/// <summary>Process-only physical light/camera response fixture. Coordinator owns CLI/native run.</summary>
public partial class PhotographicPhysicalReview : Node3D
{
    public string OutDir { get; set; } = "";

    private const string PhysicalSetting = "rendering/lights_and_shadows/use_physical_light_units";
    private const float BaselineLux = 1000f, BaselineFStop = 4f, BaselineShutter = 60f, BaselineIso = 100f;
    private const float Albedo = .18f;
    private const int SettleFrames = 8;
    private readonly List<object> _captures = new();
    private WorldEnvironment _worldEnvironment = null!;
    private Godot.Environment _environment = null!;
    private Camera3D _camera = null!;
    private CameraAttributesPhysical _cameraAttributes = null!;
    private DirectionalLight3D _key = null!;
    private int _captureCount;
    private bool _outputClaimed;

    private static double Ev100(float fStop, float shutterDenominator, float iso)
        => Math.Log2((fStop * fStop * shutterDenominator) * (100.0 / iso));

    public override async void _Ready()
    {
        bool hadSetting = ProjectSettings.HasSetting(PhysicalSetting);
        Variant previousSetting = ProjectSettings.GetSetting(PhysicalSetting);
        bool originalValue = previousSetting.AsBool();
        bool succeeded = false;
        try
        {
            if (string.IsNullOrWhiteSpace(OutDir)) throw new InvalidOperationException("OutDir is required");
            OutDir = Path.GetFullPath(OutDir);
            if (Directory.Exists(OutDir) && Directory.GetFileSystemEntries(OutDir).Length != 0)
                throw new IOException("Output directory already contains files; refusing to overwrite: " + OutDir);
            Directory.CreateDirectory(OutDir);
            _outputClaimed = true;

            // This must precede creation of any Camera3D/Light3D used by the fixture.
            ProjectSettings.SetSetting(PhysicalSetting, true);
            bool physicalSettingActual = ProjectSettings.GetSetting(PhysicalSetting).AsBool();
            if (!physicalSettingActual) throw new InvalidOperationException("Godot did not enable process-only physical light units");
            await Run(physicalSettingActual, hadSetting, originalValue);
            succeeded = true;
        }
        catch (Exception ex) { GD.PrintErr("PHOTO_PHYSICAL_REVIEW_FAILED " + ex); }
        finally
        {
            if (hadSetting) ProjectSettings.SetSetting(PhysicalSetting, previousSetting);
            else ProjectSettings.Clear(PhysicalSetting);
            bool restored = ProjectSettings.HasSetting(PhysicalSetting) ? ProjectSettings.GetSetting(PhysicalSetting).AsBool() : false;
            GD.Print("PHOTO_PHYSICAL_SETTING_RESTORED had=" + hadSetting + " original=" + originalValue + " actual=" + restored);
            if (succeeded && restored != originalValue) succeeded = false;
            if (_outputClaimed && Directory.Exists(OutDir))
            {
                try
                {
                    File.WriteAllText(Path.Combine(OutDir, "restore-status.json"), JsonSerializer.Serialize(new
                    {
                        schema_version = 1, key = PhysicalSetting, original_present = hadSetting, original_value = originalValue,
                        restored_present = ProjectSettings.HasSetting(PhysicalSetting), restored_value = restored, restored = restored == originalValue,
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) { GD.PrintErr("PHOTO_PHYSICAL_RESTORE_STATUS_FAILED " + ex); succeeded = false; }
            }
        }
        if (succeeded) { GD.Print("PHOTO_PHYSICAL_REVIEW_OK " + OutDir); GetTree().Quit(0); }
        else GetTree().Quit(2);
    }

    private async Task Run(bool physicalSettingActual, bool hadSetting, bool originalSetting)
    {
        GetWindow().Size = new Vector2I(1600, 900);
        var vp = GetViewport(); vp.Msaa3D = Viewport.Msaa.Disabled; vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.Black,
            AmbientLightEnergy = 0f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            SsaoEnabled = false,
            SsilEnabled = false,
            FogEnabled = false,
            VolumetricFogEnabled = false,
            GlowEnabled = false,
            AdjustmentEnabled = false,
            TonemapExposure = 1f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapWhite = 1f,
        };
        _worldEnvironment = new WorldEnvironment { Environment = _environment };
        AddChild(_worldEnvironment);

        _cameraAttributes = new CameraAttributesPhysical
        {
            AutoExposureEnabled = false,
            ExposureMultiplier = 1f,
            ExposureSensitivity = BaselineIso,
            ExposureAperture = BaselineFStop,
            ExposureShutterSpeed = BaselineShutter,
            FrustumFocalLength = 50f,
            FrustumFocusDistance = 4.5f,
            FrustumNear = .05f,
            FrustumFar = 30f,
        };
        _camera = new Camera3D
        {
            Name = "PhysicalCalibrationCamera",
            Current = true,
            Position = new Vector3(0f, 0f, 4.5f),
            Attributes = _cameraAttributes,
        };
        AddChild(_camera); _camera.LookAt(Vector3.Zero);

        _key = new DirectionalLight3D
        {
            Name = "PhysicalCalibrationKey",
            LightColor = Colors.White,
            LightEnergy = 1f,
            LightIntensityLux = BaselineLux,
            ShadowEnabled = false,
            ShadowOpacity = 1f,
        };
        AddChild(_key);
        if (Mathf.Abs(_key.LightIntensityLux - BaselineLux) > .01f) throw new InvalidOperationException("DirectionalLight3D did not bind requested lux");

        var neutralShader = new Shader
        {
            Code = "shader_type spatial;\nrender_mode diffuse_lambert, specular_disabled, cull_disabled;\nvoid fragment() { ALBEDO = vec3(0.18); ROUGHNESS = 1.0; METALLIC = 0.0; }\n",
            ResourceName = "PhotoPhysicalLiteralLinear18Lambert",
        };
        var neutral = new ShaderMaterial { Shader = neutralShader };
        var quad = new MeshInstance3D
        {
            Name = "Linear18PercentNeutralDiffuseQuad",
            Mesh = new PlaneMesh { Size = new Vector2(1.4f, 1.4f), SubdivideWidth = 1, SubdivideDepth = 1 },
            MaterialOverride = neutral,
            RotationDegrees = new Vector3(90f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(quad);
        var sphere = new MeshInstance3D
        {
            Name = "NeutralDiffuseSphereReference",
            Mesh = new SphereMesh { Radius = .24f, Height = .48f, RadialSegments = 32, Rings = 16, Material = neutral },
            Position = new Vector3(1.2f, 0f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(sphere);

        // All variations are one stop or an exposure-compensated pair around this camera/key baseline.
        await Capture("baseline", "baseline", BaselineLux, BaselineFStop, BaselineShutter, BaselineIso);
        await Capture("half-lux", "half light illuminance", BaselineLux * .5f, BaselineFStop, BaselineShutter, BaselineIso);
        await Capture("double-lux", "double light illuminance", BaselineLux * 2f, BaselineFStop, BaselineShutter, BaselineIso);
        await Capture("shutter-120", "half exposure time", BaselineLux, BaselineFStop, BaselineShutter * 2f, BaselineIso);
        await Capture("iso-200", "double ISO sensitivity", BaselineLux, BaselineFStop, BaselineShutter, BaselineIso * 2f);
        await Capture("aperture-f5.657", "one-stop smaller aperture", BaselineLux, BaselineFStop * Mathf.Sqrt(2f), BaselineShutter, BaselineIso);
        await Capture("compensated-double-lux-shutter-120", "double lux compensated by half exposure time", BaselineLux * 2f, BaselineFStop, BaselineShutter * 2f, BaselineIso);
        await Capture("baseline-repeat", "restored baseline repeat", BaselineLux, BaselineFStop, BaselineShutter, BaselineIso);
        if (_captureCount != 8) throw new InvalidOperationException("Expected exactly eight calibration captures");
        var baselineHash = (string)_captures[0].GetType().GetProperty("sha256")!.GetValue(_captures[0])!;
        var repeatHash = (string)_captures[^1].GetType().GetProperty("sha256")!.GetValue(_captures[^1])!;
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schema_version = 1,
            fixture = "Godot 4.7.1 physical directional-light and physical-camera response diagnostic",
            claim = "mechanism calibration only; not measured absolute photographic response",
            engine_version = Engine.GetVersionInfo()["string"].AsString(),
            process_setting_actual = physicalSettingActual,
            process_setting = new { key = PhysicalSetting, original_present = hadSetting, original_value = originalSetting, requested_value = true, actual_value_before_resource_construction = physicalSettingActual, restore_action = hadSetting ? "restore original in-memory value" : "clear process override in finally", project_file_saved = false },
            color_pipeline = new { tonemap = _environment.TonemapMode.ToString(), tonemap_exposure = _environment.TonemapExposure, tonemap_white = _environment.TonemapWhite, color_adjustments = false, exact_black_background = true },
            disabled = new { auto_exposure = !_cameraAttributes.AutoExposureEnabled, ambient = _environment.AmbientLightEnergy == 0, reflections = _environment.ReflectedLightSource.ToString(), ssao = _environment.SsaoEnabled, ssil = _environment.SsilEnabled, fog = _environment.FogEnabled, glow = _environment.GlowEnabled, key_shadows = _key.ShadowEnabled },
            depth_of_field = new { explicit_api_disable_available = false, disabled_for_measured_roi = false, roi_is_planar_at_focus_distance = true, explanation = "CameraAttributesPhysical exposes no explicit DOF toggle; the measured quad lies at configured focus distance. DOF is not claimed disabled." },
            material = new { type = "local Godot spatial Lambert shader with literal linear ALBEDO=0.18", shader = neutralShader.Code, neutral_albedo = Albedo, linear_rgb_albedo = new[] { Albedo, Albedo, Albedo }, roughness = 1f, metallic = 0f, specular = "disabled", geometry = new[] { quad.Name.ToString(), sphere.Name.ToString() } },
            roi_definition = new { x = 700, y = 350, width = 200, height = 200, meaning = "conservative center of planar linear 18-percent-neutral diffuse quad; excludes border and side sphere" },
            baseline_repeat_hashes = new { baseline = baselineHash, repeat = repeatHash, identical = baselineHash == repeatHash },
            captures = _captures,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (baselineHash != repeatHash) throw new InvalidOperationException("Restored baseline images are not pixel-identical");
    }

    private async Task Capture(string fileStem, string condition, float lux, float fStop, float shutterDenominator, float iso)
    {
        if (++_captureCount > 12) throw new InvalidOperationException("Hard capture bound of 12 exceeded");
        _key.LightIntensityLux = lux;
        _cameraAttributes.AutoExposureEnabled = false;
        _cameraAttributes.ExposureMultiplier = 1f;
        _cameraAttributes.ExposureAperture = fStop;
        _cameraAttributes.ExposureShutterSpeed = shutterDenominator;
        _cameraAttributes.ExposureSensitivity = iso;
        if (!_cameraAttributes.ExposureAperture.Equals(fStop) || !_cameraAttributes.ExposureShutterSpeed.Equals(shutterDenominator) || !_cameraAttributes.ExposureSensitivity.Equals(iso) || Math.Abs(_key.LightIntensityLux - lux) > Math.Max(.01f, lux * .0001f))
            throw new InvalidOperationException("Physical light/camera controls did not bind requested values for " + condition);
        for (int i = 0; i < SettleFrames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var fullPath = Path.Combine(OutDir, fileStem + ".png");
        if (image.SavePng(fullPath) != Error.Ok) throw new IOException("Failed to save " + fullPath);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
        double ev100 = Ev100(fStop, shutterDenominator, iso);
        double seconds = 1.0 / shutterDenominator;
        _captures.Add(new
        {
            file = Path.GetFileName(fullPath), sha256 = hash, condition,
            roi = new { x = 700, y = 350, width = 200, height = 200 },
            physical_light_units = ProjectSettings.GetSetting(PhysicalSetting).AsBool(),
            tonemap = _environment.TonemapMode.ToString(), auto_exposure = _cameraAttributes.AutoExposureEnabled,
            directional_lux = _key.LightIntensityLux,
            exposure = new { f_stop = _cameraAttributes.ExposureAperture, shutter_denominator = _cameraAttributes.ExposureShutterSpeed, shutter_seconds = seconds, iso = _cameraAttributes.ExposureSensitivity, multiplier = _cameraAttributes.ExposureMultiplier, ev100 },
            neutral_albedo = Albedo, linear_rgb_albedo = new[] { Albedo, Albedo, Albedo }, resolution = new[] { image.GetWidth(), image.GetHeight() },
            camera = new { focal_length_mm = _cameraAttributes.FrustumFocalLength, focus_distance_m = _cameraAttributes.FrustumFocusDistance, fov_degrees = _cameraAttributes.GetFov(), position = _camera.Position.ToString(), attributes_type = _camera.Attributes?.GetClass() },
            light = new { energy_multiplier = _key.LightEnergy, color = _key.LightColor.ToHtml(), temperature_kelvin = _key.LightTemperature, correlated_color = _key.GetCorrelatedColor().ToString(), normal_incidence = true, shadows = _key.ShadowEnabled },
            settle_frames = SettleFrames,
        });
        GD.Print("PHOTO_PHYSICAL_CAPTURE " + fileStem + " lux=" + lux + " EV100=" + ev100.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " sha256=" + hash);
    }
}
