using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;

namespace Vivarium.Game.App;

/// <summary>
/// Isolated diagnostic for the production leaf direct-light include. This is
/// intentionally a constant material on a canonical plane, not a full leaf.
/// </summary>
public partial class PhotorealismLightReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";

    private const int SettleFrames = 8;
    private const float F0 = 0.04f;
    private const float Albedo = 0.18f;
    private const float Roughness = 0.5f;
    private readonly List<object> _captures = new();
    private Camera3D _camera = null!;
    private OmniLight3D _light = null!;
    private MeshInstance3D _plane = null!;
    private ShaderMaterial _custom = null!;
    private StandardMaterial3D _standard = null!;
    private int _captureCount;

    private static Shader CreateDiagnosticShader()
    {
        var shader = new Shader();
        shader.Code = """
            shader_type spatial;
            render_mode cull_disabled;
            #include "res://Shaders/leaf_direct_light.gdshaderinc"
            uniform float calibration_roughness = 0.5;
            uniform int calibration_display = 0;
            void vertex() {
                tissue_fiber_view = vec3(1.0, 0.0, 0.0);
                tissue_fiber_mask = 0.0;
                tissue_phase_mask = 0.0;
            }
            void fragment() {
                ALBEDO = vec3(0.18);
                ROUGHNESS = calibration_roughness;
                METALLIC = 0.0;
                SPECULAR = 0.5;
                BACKLIGHT = vec3(0.0);
                AO = 1.0;
                AO_LIGHT_AFFECT = 0.0;
                if (calibration_display != 0) {
                    vec3 shown = calibration_display == 1 ? ALBEDO :
                        calibration_display == 2 ? normalize(NORMAL) * 0.5 + 0.5 :
                        vec3(ROUGHNESS);
                    ALBEDO = vec3(0.0);
                    SPECULAR = 0.0;
                    EMISSION = shown;
                }
            }
            """;
        return shader;
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string name, string materialMode, bool explicitF0, float lightSpecular, float expectedF0, float expectedSpecularAmount)
    {
        if (++_captureCount > 18) throw new InvalidOperationException("Capture safety bound exceeded");
        await Frames(SettleFrames);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var path = Path.GetFullPath(Path.Combine(OutDir, name + ".png"));
        if (image.SavePng(path) != Error.Ok) throw new IOException("Failed to save " + path);
        _captures.Add(new
        {
            file = Path.GetFileName(path),
            materialMode,
            cameraPosition = _camera.Position.ToString(),
            cameraRotation = _camera.Rotation.ToString(),
            fov = _camera.Fov,
            resolution = new[] { image.GetWidth(), image.GetHeight() },
            roi = new { x = 300, y = 100, width = 1000, height = 700, meaning = "fixed central diagnostic region; includes the entire canonical plane" },
            lightType = "single OmniLight3D; no EnvironmentRig or other lights",
            lightEnergy = _light.LightEnergy,
            lightSpecular,
            expectedSpecularAmount,
            explicitMaterialF0 = explicitF0,
            materialF0Uniform = F0,
            expectedF0,
            albedo = new[] { Albedo, Albedo, Albedo },
            roughness = Roughness,
            metallic = 0.0f,
            normal = "canonical plane geometric normal; no normal map",
            settleFrames = SettleFrames
        });
        GD.Print("PHOTO_REALISM_LIGHT_CAPTURE " + name);
    }

    private async Task Run()
    {
        try
        {
            if (Content == null) throw new InvalidOperationException("Content is required");
            if (string.IsNullOrWhiteSpace(OutDir)) throw new InvalidOperationException("OutDir is required");
            OutDir = Path.GetFullPath(OutDir);
            if (Directory.Exists(OutDir) && Directory.GetFileSystemEntries(OutDir).Length != 0)
                throw new IOException("Output directory already contains files; refusing to overwrite: " + OutDir);
            Directory.CreateDirectory(OutDir);
            GetWindow().Size = new Vector2I(1600, 900);

            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.0f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                SsaoEnabled = false,
                SsilEnabled = false,
                FogEnabled = false,
                VolumetricFogEnabled = false,
                GlowEnabled = false,
                TonemapExposure = 1.0f,
                TonemapMode = Godot.Environment.ToneMapper.Linear
            };
            AddChild(new WorldEnvironment { Environment = environment });

            var attributes = new CameraAttributesPractical { AutoExposureEnabled = false };
            _camera = new Camera3D
            {
                Current = true,
                Fov = 40.0f,
                Near = 0.05f,
                Far = 20.0f,
                Position = new Vector3(0.0f, 0.0f, 3.2f),
                Attributes = attributes
            };
            AddChild(_camera);
            _camera.LookAt(Vector3.Zero);

            _light = new OmniLight3D
            {
                Name = "M02_Single_Omni",
                Position = new Vector3(0.0f, 0.10f, 2.4f),
                LightColor = Colors.White,
                LightEnergy = 1.0f,
                LightSpecular = 0.5f,
                OmniRange = 8.0f,
                OmniAttenuation = 1.0f,
                ShadowEnabled = false
            };
            AddChild(_light);

            var planeMesh = new PlaneMesh { Size = new Vector2(1.2f, 1.2f), SubdivideWidth = 1, SubdivideDepth = 1 };
            _custom = new ShaderMaterial { Shader = CreateDiagnosticShader() };
            _custom.SetShaderParameter("calibration_roughness", Roughness);
            _custom.SetShaderParameter("tissue_light_diagnostic", 3);
            _custom.SetShaderParameter("tissue_material_diagnostic", 0);
            _custom.SetShaderParameter("tissue_specular_stress", 1.0f);
            _custom.SetShaderParameter("tissue_direct_transmission", 0.0f);
            _custom.SetShaderParameter("tissue_diffuse_wrap", 0.0f);
            _custom.SetShaderParameter("tissue_phase_scatter", 0.0f);
            _custom.SetShaderParameter("tissue_fiber_anisotropy", 0.0f);
            _custom.SetShaderParameter("tissue_use_explicit_material_f0", true);
            _custom.SetShaderParameter("tissue_material_f0", F0);

            _standard = new StandardMaterial3D
            {
                AlbedoColor = new Color(Albedo, Albedo, Albedo),
                Roughness = Roughness,
                Metallic = 0.0f,
                MetallicSpecular = 0.5f
            };
            _plane = new MeshInstance3D
            {
                Name = "Canonical diagnostic plane",
                Mesh = planeMesh,
                MaterialOverride = _custom,
                RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(_plane);

            // Emissive diagnostics record the constant albedo, geometric normal,
            // and roughness before the actual specular-only sweep.
            _light.Visible = false;
            foreach (var (mode, label) in new[] { (1, "albedo"), (2, "normal"), (3, "roughness") })
            {
                _custom.SetShaderParameter("calibration_display", mode);
                await Capture("diagnostic-" + label, "custom-include-emissive-diagnostic", true, 0.5f, F0, 1.0f);
            }

            _custom.SetShaderParameter("calibration_display", 0);
            _light.Visible = true;
            foreach (float lightSpecular in new[] { 0.0f, 0.25f, 0.5f, 1.0f })
            {
                _light.LightSpecular = lightSpecular;
                float amount = 2.0f * lightSpecular;
                float legacyF0 = Mathf.Lerp(0.018f, 0.055f, Mathf.Clamp(amount, 0.0f, 1.0f));
                _custom.SetShaderParameter("tissue_use_explicit_material_f0", false);
                _custom.SetShaderParameter("tissue_material_f0", F0);
                await Capture($"custom-legacy-light-specular-{lightSpecular:0.00}", "custom-specular-only-legacy", false, lightSpecular, legacyF0, amount);

                _custom.SetShaderParameter("tissue_use_explicit_material_f0", true);
                _custom.SetShaderParameter("tissue_material_f0", F0);
                await Capture($"custom-explicit-f0-0.04-light-specular-{lightSpecular:0.00}", "custom-specular-only-explicit", true, lightSpecular, F0, amount);
            }

            // Sequential same-quad StandardMaterial images provide a familiar
            // light response reference; they are not asserted to be BRDF-matched.
            _plane.MaterialOverride = _standard;
            foreach (float lightSpecular in new[] { 0.0f, 0.25f, 0.5f, 1.0f })
            {
                _light.LightSpecular = lightSpecular;
                await Capture($"standard-reference-light-specular-{lightSpecular:0.00}", "standard-material-reference-not-brdf-equivalent", false, lightSpecular, F0, 2.0f * lightSpecular);
            }

            // Draw each production consumer to validate native shader compilation.
            // These swatches are compiler diagnostics, not material comparisons.
            foreach (string consumer in new[] { "leaf", "flora", "ground_cover" })
            {
                var production = Bridge.Shader($"res://Shaders/{consumer}.gdshader");
                production.SetShaderParameter("review_time", 0f);
                _plane.MaterialOverride = production;
                await Capture("compile-consumer-" + consumer, "production-shader-compile-only", true, 1f, .055f, 2f);
            }
            File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new
            {
                fixture = "M02 single-Omni direct specular control",
                scope = "Isolated shader-include diagnostic; not a leaf appearance or photorealism comparison.",
                renderer = "active Godot renderer supplied by coordinator",
                resolution = "1600x900",
                background = "exact black Environment color",
                exposure = 1.0f,
                tonemap = environment.TonemapMode.ToString(),
                autoExposure = false,
                ambientEnergy = 0.0f,
                reflectedLightSource = "Disabled",
                ssao = false,
                ssil = false,
                fog = false,
                volumetricFog = false,
                glow = false,
                geometry = new { type = "PlaneMesh", width = 1.2f, height = 1.2f, rotationDegrees = new[] { 90.0f, 0.0f, 0.0f } },
                fixedMaterial = new { albedo = new[] { Albedo, Albedo, Albedo }, roughness = Roughness, metallic = 0.0f, normal = "geometric plane normal; no normal map" },
                customShader = "runtime shader includes production leaf_direct_light.gdshaderinc with fixed albedo/roughness and zero BACKLIGHT; no tissue texture pools or botanical pigment model",
                directLightDiagnostic = 3,
                light = new { type = "OmniLight3D", color = "white", energy = 1.0f, position = _light.Position.ToString(), range = _light.OmniRange, attenuation = _light.OmniAttenuation, shadows = false },
                testedLightSpecular = new[] { 0.0f, 0.25f, 0.5f, 1.0f },
                expectedSpecularAmount = "2 * light_specular for OmniLight3D",
                pngMeasurementGuidance = "Decode sRGB and exclude clipped pixels before estimating linear specular ratios.",
                expectedLegacyF0 = "mix(0.018,0.055,clamp(SPECULAR_AMOUNT,0,1))",
                explicitF0 = F0,
                settleFrames = SettleFrames,
                captureCount = _captureCount,
                captures = _captures,
                limitations = new[] { "StandardMaterial references use the same plane/camera/light but are not claimed as equivalent to the custom GGX implementation.", "No image or numerical captures were performed by this source-only task." }
            }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"PHOTO_REALISM_LIGHT_REVIEW_OK captures={_captureCount} out={OutDir}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PushError("PHOTO_REALISM_LIGHT_REVIEW_FAILED " + e);
            GetTree().Quit(1);
        }
    }

    public override void _Ready() => _ = Run();
}
