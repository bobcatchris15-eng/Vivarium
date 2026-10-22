using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.App;

/// <summary>Frozen, render-only leaf calibration fixture. Coordinator owns CLI dispatch.</summary>
public partial class PhotorealismReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public bool ExposureSweep { get; set; } = false;

    private readonly List<object> _frames = new();
    private EnvironmentRig _rig = null!;
    private Camera3D _camera = null!;
    private ShaderMaterial _leaf = null!;
    private Vector3 _target;
    private Vector3 _diffuseStandardPosition;
    private Vector3 _glossyStandardPosition;
    private MeshData? _reviewSource;
    private Vector3 _reviewScale;
    private float _size;
    private int _captureCount;

    private async Task Frames(int count = 2)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Capture(string name, string view, bool explicitF0, float f0, int settle = 8)
    {
        if (++_captureCount > 30) throw new InvalidOperationException("Capture safety bound exceeded");
        await Frames(settle);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var path = Path.GetFullPath(Path.Combine(OutDir, name + ".png"));
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Capture {name}: {error}");
        _frames.Add(new
        {
            file = Path.GetFileName(path), view, cameraPosition = _camera.Position.ToString(), cameraRotation = _camera.Rotation.ToString(),
            fov = _camera.Fov, exposure = _rig.Env.TonemapExposure, autoExposure = _rig.CameraAttributes.AutoExposureEnabled,
            ambientEnergy = _rig.Env.AmbientLightEnergy,
            lightPhaseSeconds = _rig.BenchmarkLightSeconds, sunEnergy = _rig.Sun.LightEnergy,
            explicitMaterialF0 = explicitF0, materialF0 = f0, readinessFrames = settle,
            mesh = "production OrganismMeshes.FloraTier shadebell seed=stable-fixture-v1"
        });
        GD.Print("PHOTO_REALISM_CAPTURE " + name);
    }

    private void SetF0(bool explicitF0, float f0)
    {
        _leaf.SetShaderParameter("tissue_use_explicit_material_f0", explicitF0);
        _leaf.SetShaderParameter("tissue_material_f0", f0);
    }

    private void Aim(string view)
    {
        float distance = view switch { "close" => _size * 1.45f, "interaction" => _size * 2.6f, _ => _size * 5.2f };
        var direction = new Vector3(.72f, .42f, 1).Normalized();
        _camera.Position = _target + direction * distance;
        _camera.LookAt(_target);
    }

    private async Task Run()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(OutDir)) throw new InvalidOperationException("OutDir is required");
            var fullOut = Path.GetFullPath(OutDir);
            if (Directory.Exists(fullOut) && Directory.GetFileSystemEntries(fullOut).Length != 0)
                throw new IOException("Output directory already contains files; refusing to overwrite: " + fullOut);
            Directory.CreateDirectory(fullOut);
            OutDir = fullOut;

            GetWindow().Size = new Vector2I(1600, 900);
            _rig = new EnvironmentRig { BenchmarkLightSeconds = 60 }; AddChild(_rig);
            await Frames(2);
            _rig.SetProcess(false);
            _rig.ApplyQuality(2);
            _rig.CameraAttributes.AutoExposureEnabled = false;
            _rig.Env.TonemapExposure = 1.0f;
            _rig.Env.FogEnabled = false;
            _rig.Env.BackgroundMode = Godot.Environment.BGMode.Color;
            _rig.Env.BackgroundColor = Colors.Black;
            _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            _rig.Env.AmbientLightColor = new Color(.72f, .79f, .85f);
            _rig.Env.AmbientLightEnergy = .42f;
            _rig.Sun.RotationDegrees = new Vector3(-48, -42, 0);
            _rig.Sun.LightEnergy = 1.2f;

            _camera = new Camera3D { Current = true, Fov = 42, Near = .01f, Far = 40 };
            _camera.Attributes = ExposureSweep ? null : _rig.CameraAttributes;
            AddChild(_camera);

            var species = Content.FloraOrThrow("shadebell");
            var profile = FloraVisualProfile.Load("shadebell") ?? throw new InvalidOperationException("Missing Shadebell production visual profile");
            var stem = Bridge.Shader("res://Shaders/flora.gdshader");
            stem.SetShaderParameter("surface_mode", 2);
            stem.SetShaderParameter("sway", 0f);
            FloraSurfaceProfiles.Bind(stem, species, photographedLeaves: false);
            _leaf = profile.LeafMaterial(animated: true);
            _leaf.SetShaderParameter("review_time", 0f);
            _leaf.SetShaderParameter("plant_stiffness", 7f);
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual.shadebell"), 0x9E3779B97F4A7C15UL);
            var source = OrganismMeshes.FloraTier(species, seed, 0, tieredSource: true);
            if (source.VertexCount == 0 || source.Indices.Count < 3)
                throw new InvalidOperationException("Production Shadebell tier returned empty geometry");
            var scale = new Vector3((float)species.RadiusAtMax, (float)species.Height, (float)species.RadiusAtMax);
            _reviewSource = source;
            _reviewScale = scale;
            var mesh = profile.Compile(source, stem, _leaf);
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
            mm.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(scale), Vector3.Zero));
            mm.SetInstanceColor(0, Colors.White);
            mm.SetInstanceCustomData(0, new Color(.5f, 1f, 0f, .5f));
            AddChild(new MultiMeshInstance3D { Multimesh = mm });
            var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < source.VertexCount; i++)
            {
                var point = Bridge.V(source.Position(i)) * scale;
                minimum = minimum.Min(point); maximum = maximum.Max(point);
            }
            var bounds = new Aabb(minimum, maximum - minimum);
            _target = bounds.GetCenter();
            _size = Math.Max(.2f, Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)));

            // Neutral standards are explicit calibration references, not production habitat subjects.
            AddStandard("standard-diffuse", new Vector3(-_size * 1.5f, _size * .18f, 0), .72f, 0f);
            AddStandard("standard-glossy", new Vector3(_size * 1.5f, _size * .18f, 0), .12f, 0f);

            if (ExposureSweep)
            {
                await RunExposureSweep();
                GetTree().Quit(0);
                return;
            }

            Aim("close");
            var inheritedF0Switch = _leaf.GetShaderParameter("tissue_use_explicit_material_f0");
            if (inheritedF0Switch.VariantType == Variant.Type.Nil)
                inheritedF0Switch = RenderingServer.ShaderGetParameterDefault(_leaf.Shader.GetRid(), "tissue_use_explicit_material_f0");
            if (!inheritedF0Switch.AsBool())
                throw new InvalidOperationException("Production material did not inherit explicit F0 default");
            GD.Print("PHOTO_REALISM_DEFAULT_F0 explicit=" + inheritedF0Switch.AsBool());
            await Capture("production-default-close", "close", true, .055f);
            foreach (var view in new[] { "close", "interaction", "overview" })
            {
                Aim(view);
                SetF0(false, .055f);
                await Capture($"default-off-{view}-a", view, false, .055f, 8);
                if (view == "close") await Capture("default-off-close-b", view, false, .055f, 8);
                foreach (float f0 in new[] { .028f, .040f, .055f })
                {
                    SetF0(true, f0);
                    await Capture($"explicit-f0-{f0:0.000}-{view}", view, true, f0);
                }
            }
            // Compare direct-light response with environment ambient and screen-space indirect removed.
            Aim("close");
            _rig.Env.AmbientLightEnergy = 0f;
            _rig.Env.SsilEnabled = false;
            _rig.Env.SsaoEnabled = false;
            SetF0(false, .055f);
            await Capture("ambient-off-key-fill-default-off", "close-ambient-off-key-fill", false, .055f);
            SetF0(true, .028f);
            await Capture("ambient-off-key-fill-explicit-f0-0.028", "close-ambient-off-key-fill", true, .028f);
            _rig.Env.AmbientLightEnergy = .42f;
            _rig.Env.SsilEnabled = true;
            _rig.Env.SsaoEnabled = true;
            // Short deterministic camera motion samples; geometry and presentation clock remain frozen.
            for (int i = 0; i < 4; i++)
            {
                float angle = Mathf.DegToRad(-20 + i * 12);
                var offset = new Vector3(Mathf.Sin(angle), .34f, Mathf.Cos(angle)) * (_size * 2.4f);
                _camera.Position = _target + offset;
                _camera.LookAt(_target);
                SetF0(false, .055f);
                await Capture($"motion-legacy-{i:00}", "moving-camera", false, .055f);
                SetF0(true, .055f);
                await Capture($"motion-explicit-{i:00}", "moving-camera", true, .055f);
            }

            var metadata = new
            {
                fixture = "photorealism-m01-m03-leaf-calibration-subset", createdUtc = DateTimeOffset.UtcNow,
                renderer = "active Godot renderer (runtime supplied by coordinator)", resolution = "1600x900",
                background = "exact black Environment color", lightPhaseSeconds = _rig.BenchmarkLightSeconds,
                exposure = _rig.Env.TonemapExposure, autoExposure = _rig.CameraAttributes.AutoExposureEnabled,
                quality = _rig.Quality, sourceTriangleCount = source.Indices.Count / 3,
                coverage = new[] { "production Shadebell leaf cluster", "neutral diffuse StandardMaterial3D sphere", "neutral glossy StandardMaterial3D sphere" },
                missingM01Subjects = new[] { "moss", "bark", "soil", "wet rock", "shallow water", "guardian patch" },
                frames = _frames
            };
            File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"PHOTO_REALISM_REVIEW_OK captures={_captureCount} out={OutDir}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PushError("PHOTO_REALISM_REVIEW_FAILED " + e);
            GetTree().Quit(1);
        }
    }

    private void AddStandard(string name, Vector3 position, float roughness, float metallic)
    {
        var material = new StandardMaterial3D { AlbedoColor = new Color(.5f, .5f, .5f), Roughness = roughness, Metallic = metallic };
        var worldPosition = _target + position;
        if (name == "standard-diffuse") _diffuseStandardPosition = worldPosition;
        if (name == "standard-glossy") _glossyStandardPosition = worldPosition;
        var sphere = new MeshInstance3D { Name = name, Mesh = new SphereMesh { Radius = _size * .19f, Height = _size * .38f },
            MaterialOverride = material, Position = worldPosition };
        AddChild(sphere);
    }

    private async Task DelaySeconds(double seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private static object MeasureRoi(Image image, Vector2 center, int radius = 20)
    {
        int cx = Mathf.Clamp((int)Math.Round(center.X), 0, image.GetWidth() - 1);
        int cy = Mathf.Clamp((int)Math.Round(center.Y), 0, image.GetHeight() - 1);
        int left = Math.Max(0, cx - radius), right = Math.Min(image.GetWidth() - 1, cx + radius);
        int top = Math.Max(0, cy - radius), bottom = Math.Min(image.GetHeight() - 1, cy + radius);
        double sum = 0;
        int clipped = 0, count = 0;
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                var c = image.GetPixel(x, y);
                sum += .2126 * c.R + .7152 * c.G + .0722 * c.B;
                if (Math.Max(c.R, Math.Max(c.G, c.B)) >= .995f) clipped++;
                count++;
            }
        return new { centerPixels = new[] { cx, cy }, radiusPixels = radius, meanEncodedLuma = count == 0 ? 0 : sum / count,
            clippedPixelFraction = count == 0 ? 0 : (double)clipped / count };
    }

    private async Task CaptureExposure(string name, float cardCoverage, string mode, double elapsed,
        Dictionary<string, (Vector2 Center, int Radius)> rois, System.Diagnostics.Stopwatch? autoTimer = null, int settleFrames = 8)
    {
        if (++_captureCount > 16) throw new InvalidOperationException("Exposure capture safety bound exceeded");
        await Frames(settleFrames);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var file = name + ".png";
        var path = Path.GetFullPath(Path.Combine(OutDir, file));
        var error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Capture {name}: {error}");
        double actualElapsed = autoTimer?.Elapsed.TotalSeconds ?? elapsed;
        int blackPixels = 0;
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                if (Math.Max(c.R, Math.Max(c.G, c.B)) <= .002f) blackPixels++;
            }
        var practical = _rig.CameraAttributes;
        _frames.Add(new
        {
            file, mode, cardCoverageFractionOfViewport = cardCoverage, elapsedAutoSeconds = actualElapsed,
            cameraPosition = _camera.Position.ToString(), cameraRotation = _camera.Rotation.ToString(), fov = _camera.Fov,
            cameraLocalAttributesNull = _camera.Attributes == null,
            cameraAttributesResourceId = _camera.Attributes?.GetInstanceId(),
            worldEnvironmentAttributesResourceId = _rig.WorldEnv.CameraAttributes?.GetInstanceId(),
            autoExposureEnabled = practical.AutoExposureEnabled, minimumSensitivityIso = practical.AutoExposureMinSensitivity,
            maximumSensitivityIso = practical.AutoExposureMaxSensitivity, autoExposureSpeed = practical.AutoExposureSpeed,
            autoExposureScale = practical.AutoExposureScale, exposureMultiplier = practical.ExposureMultiplier,
            exposureSensitivityIso = practical.ExposureSensitivity, tonemapExposure = _rig.Env.TonemapExposure,
            physicalLightUnitsEnabled = ProjectSettings.GetSetting("rendering/lights_and_shadows/use_physical_light_units").AsBool(),
            lightPhaseSeconds = _rig.BenchmarkLightSeconds, sunEnergy = _rig.Sun.LightEnergy,
            blackPixelFraction = (double)blackPixels / (image.GetWidth() * image.GetHeight()),
            targetRois = rois.ToDictionary(kv => kv.Key, kv => MeasureRoi(image, kv.Value.Center, kv.Value.Radius)),
            coverageCardScreenRect = CurrentCoverageCardPixels()?.ToString()
        });
        GD.Print("PHOTO_EXPOSURE_CAPTURE " + name);
    }

    private MeshInstance3D? _coverageCard;
    private float _coverageCardWorldWidth;
    private float _coverageCardWorldHeight;

    private Rect2? CurrentCoverageCardPixels()
    {
        if (_coverageCard == null) return null;
        float halfWidth = _coverageCardWorldWidth * .5f, halfHeight = _coverageCardWorldHeight * .5f;
        var t = _coverageCard.GlobalTransform;
        var corners = new[]
        {
            _camera.UnprojectPosition(t * new Vector3(-halfWidth, -halfHeight, 0)),
            _camera.UnprojectPosition(t * new Vector3(-halfWidth, halfHeight, 0)),
            _camera.UnprojectPosition(t * new Vector3(halfWidth, -halfHeight, 0)),
            _camera.UnprojectPosition(t * new Vector3(halfWidth, halfHeight, 0))
        };
        var minimum = corners[0]; var maximum = corners[0];
        foreach (var corner in corners) { minimum = minimum.Min(corner); maximum = maximum.Max(corner); }
        return new Rect2(minimum, maximum - minimum);
    }

    private Dictionary<string, (Vector2 Center, int Radius)> ValidateExposureRois()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        var rois = new Dictionary<string, (Vector2 Center, int Radius)>();
        void Add(string name, Vector3 worldPosition, float worldRadius)
        {
            var center = _camera.UnprojectPosition(worldPosition);
            var edge = _camera.UnprojectPosition(worldPosition + _camera.GlobalBasis.X * worldRadius);
            int radius = Math.Max(20, (int)Math.Ceiling(center.DistanceTo(edge)));
            if (center.X - radius < 0 || center.X + radius >= viewport.X || center.Y - radius < 0 || center.Y + radius >= viewport.Y)
                throw new InvalidOperationException($"Required exposure ROI {name} is not fully visible: {center}, radius={radius}, viewport={viewport}");
            var roi = new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2f);
            if (CurrentCoverageCardPixels() is { } panel && roi.Intersects(panel))
                throw new InvalidOperationException($"Coverage card overlaps required exposure ROI {name}");
            rois.Add(name, (center, radius));
        }
        Add("leaf", _target, _size * .12f);
        Add("diffuseStandard", _diffuseStandardPosition, _size * .19f);
        Add("glossyStandard", _glossyStandardPosition, _size * .19f);
        if (_coverageCard != null && _reviewSource != null)
        {
            var plantMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var plantMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < _reviewSource.VertexCount; i++)
            {
                var world = Bridge.V(_reviewSource.Position(i)) * _reviewScale;
                var pixel = _camera.UnprojectPosition(world);
                plantMin = plantMin.Min(pixel); plantMax = plantMax.Max(pixel);
            }
            var plantRect = new Rect2(plantMin, plantMax - plantMin);
            if (CurrentCoverageCardPixels() is { } card && plantRect.Intersects(card))
                throw new InvalidOperationException("Coverage card overlaps projected production Shadebell bounds");
        }
        return rois;
    }
    private async Task RunExposureSweep()
    {
        if (_camera.Attributes != null) throw new InvalidOperationException("Exposure fixture requires camera-local Attributes null");
        if (!ReferenceEquals(_rig.WorldEnv.CameraAttributes, _rig.CameraAttributes))
            throw new InvalidOperationException("WorldEnvironment camera attributes do not reference EnvironmentRig's production resource");

        // Hold one overview composition so the production subject and both standards fit.
        Aim("overview");
        var lookAt = _target + Vector3.Up * (_size * .25f);
        _camera.LookAt(lookAt);
        _camera.Attributes = null; // Inherit the verified WorldEnvironment resource, as gameplay does.
        _rig.Env.TonemapExposure = 1.0f;
        _rig.CameraAttributes.AutoExposureEnabled = false;
        _rig.CameraAttributes.AutoExposureMinSensitivity = 70f;
        _rig.CameraAttributes.AutoExposureMaxSensitivity = 125f;
        _rig.CameraAttributes.AutoExposureSpeed = .6f;
        _rig.CameraAttributes.AutoExposureScale = .16f;
        _rig.CameraAttributes.ExposureMultiplier = 1.0f;
        _rig.CameraAttributes.ExposureSensitivity = 100f;

        var physicalUnits = ProjectSettings.GetSetting("rendering/lights_and_shadows/use_physical_light_units").AsBool();
        // The audit found no explicit project value. Record the actual engine setting; ISO sensitivity
        // is only effective when physical light units are enabled.
        var calibratedValues = new List<object>();
        foreach (float coverage in new[] { 0f, .12f, .24f })
        {
            SetCardCoverage(coverage);
            var rois = ValidateExposureRois();
            _rig.CameraAttributes.AutoExposureEnabled = false;
            await DelaySeconds(.5);
            await CaptureExposure($"fixed-{coverage:0.00}", coverage, "fixed-exposure", 0, rois);
        }

        foreach (float coverage in new[] { 0f, .12f, .24f })
        {
            SetCardCoverage(coverage);
            var rois = ValidateExposureRois();
            _rig.CameraAttributes.AutoExposureEnabled = false;
            await DelaySeconds(1.0); // Re-establish the explicitly fixed condition before each auto interval.
            _rig.CameraAttributes.AutoExposureEnabled = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            await DelaySeconds(1.0);
            await CaptureExposure($"auto-{coverage:0.00}-early", coverage, "auto-early", timer.Elapsed.TotalSeconds, rois, timer);
            await DelaySeconds(5.0);
            await CaptureExposure($"auto-{coverage:0.00}-late", coverage, "auto-late", timer.Elapsed.TotalSeconds, rois, timer);
            calibratedValues.Add(new { coverage, elapsedSeconds = timer.Elapsed.TotalSeconds });
            _rig.CameraAttributes.AutoExposureEnabled = false;
            await DelaySeconds(.5);
        }

        var metadata = new
        {
            fixture = "controlled-visible-gray-card exposure-meter diagnostic", createdUtc = DateTimeOffset.UtcNow,
            resolution = GetViewport().GetVisibleRect().Size.ToString(), quality = _rig.Quality, background = "exact black Environment color",
            caveat = "Gray cards change visible peripheral luminance coverage; this is a controlled meter-sensitivity diagnostic, not authentic production black-background framing acceptance.",
            cameraLocalAttributes = "null; inherits WorldEnvironment.CameraAttributes",
            cameraAttributesResourceId = _rig.CameraAttributes.GetInstanceId(),
            worldEnvironmentAttributesResourceId = _rig.WorldEnv.CameraAttributes?.GetInstanceId(),
            environmentAutoExposure = new { _rig.CameraAttributes.AutoExposureEnabled, _rig.CameraAttributes.AutoExposureMinSensitivity,
                _rig.CameraAttributes.AutoExposureMaxSensitivity, _rig.CameraAttributes.AutoExposureSpeed,
                _rig.CameraAttributes.AutoExposureScale, _rig.CameraAttributes.ExposureMultiplier, _rig.CameraAttributes.ExposureSensitivity,
                physicalLightUnitsEnabled = physicalUnits },
            scene = new { cameraPosition = _camera.Position.ToString(), cameraRotation = _camera.Rotation.ToString(), fov = _camera.Fov,
                lightPhaseSeconds = _rig.BenchmarkLightSeconds, sunEnergy = _rig.Sun.LightEnergy,
                productionMesh = "Shadebell stable tier-0, review_time=0", neutralStandards = "fixed visible diffuse and glossy StandardMaterial3D spheres" },
            card = new { material = "unshaded linear gray 0.18", castsShadow = false, affectsGi = false,
                fractions = new[] { 0f, .12f, .24f }, projectedScreenRectAndBlackPixelFractionMeasuredFromSavedPng = true },
            autoIntervals = calibratedValues, captures = _frames,
            limitations = new[] { "Card pixels are gray, not added black; effect is a controlled visible-luminance coverage diagnostic.",
                "Early/late pairs reveal adaptation trend but do not establish convergence unless measured target ROI values stabilize.",
                "Black-pixel fraction counts normalized RGB maximum <= 0.002 in the captured viewport image.",
                "WorldEnvironment attributes inheritance and resource IDs are asserted and recorded, but engine runtime selection remains to be confirmed by coordinator." }
        };
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PHOTO_EXPOSURE_REVIEW_OK captures={_captureCount} out={OutDir}");
    }

    private void SetCardCoverage(float coverage)
    {
        if (_coverageCard != null) _coverageCard.QueueFree();
        _coverageCard = null;
        _coverageCardWorldWidth = _coverageCardWorldHeight = 0f;
        if (coverage <= 0) return;
        float distance = 1.5f;
        float height = 2f * distance * Mathf.Tan(Mathf.DegToRad(_camera.Fov * .5f));
        float aspect = 1600f / 900f;
        float width = height * aspect * .98f;
        float panelHeight = height * coverage / .98f;
        _coverageCardWorldWidth = width;
        _coverageCardWorldHeight = panelHeight;
        var panelMesh = new QuadMesh { Size = new Vector2(width, panelHeight) };
        var panelMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(.18f, .18f, .18f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _coverageCard = new MeshInstance3D
        {
            Name = "ExposureCoverageCard",
            Mesh = panelMesh,
            MaterialOverride = panelMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Position = new Vector3(0, height * .5f - panelHeight * .5f, -distance)
        };
        _camera.AddChild(_coverageCard);
    }

    public override void _Ready() => _ = Run();
}
