using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Bounded integrated family-lighting board. Coordinator owns CLI dispatch/native execution.</summary>
public partial class PhotographicFamilyReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public bool BacklightSweep { get; set; } = false;
    public bool WetLayerSweep { get; set; } = false;

    private sealed record Profile(string Id, string LightMode, string MaterialMode, float Exposure,
        float SunEnergy, float AmbientEnergy, float F0, float SpecularGain, float TransmissionGain,
        float PhaseGain, float DiffuseWrap, float Anisotropy);
    private readonly List<object> _captures = new();
    private readonly Dictionary<ulong, ShaderMaterial> _materials = new();
    private readonly Dictionary<ulong, Shader> _originalShaders = new();
    private readonly Dictionary<ulong, Shader> _reflectionShaders = new();
    private readonly Dictionary<ulong, (float transmission, float leafTransmission)> _productionTransmission = new();
    private EnvironmentRig _rig = null!;
    private Camera3D _camera = null!;
    private VivariumWorld _world = null!;
    private IslandRenderer _island = null!;
    private FloraRenderer _flora = null!;
    private CoverageRenderer _coverage = null!;
    private string _digest = "";
    private string _variantShader = "";
    private int _captureCount;
    private int _fixedFrames;
    private Vector3 _patchTarget = new(0, .38f, 0);

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    private async Task Frames(int count = 2)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private static Profile[] ReadProfiles(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Require(root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(new[] { "profiles", "schema_version", "units" }), "Unknown or missing manifest fields");
        Require(root.GetProperty("schema_version").GetInt32() == 1 && root.GetProperty("units").GetString() == "relative_engine", "Unsupported profile schema/units");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "id", "light_mode", "material_mode", "exposure", "sun_energy", "ambient_energy", "f0", "specular_gain", "transmission_gain", "phase_gain", "diffuse_wrap", "anisotropy" };
        var all = new List<Profile>();
        foreach (var e in root.GetProperty("profiles").EnumerateArray())
        {
            Require(e.ValueKind == JsonValueKind.Object && e.EnumerateObject().Select(x => x.Name).Order().SequenceEqual(expected.Order()), "Unknown or missing profile fields");
            Require(e.EnumerateObject().Count() == expected.Count, "Duplicate profile property");
            float F(string key) { var n = e.GetProperty(key); Require(n.ValueKind == JsonValueKind.Number && n.TryGetSingle(out var v) && float.IsFinite(v), "Invalid numeric field: " + key); return n.GetSingle(); }
            var p = new Profile(e.GetProperty("id").GetString() ?? "", e.GetProperty("light_mode").GetString() ?? "", e.GetProperty("material_mode").GetString() ?? "",
                F("exposure"), F("sun_energy"), F("ambient_energy"), F("f0"), F("specular_gain"), F("transmission_gain"), F("phase_gain"), F("diffuse_wrap"), F("anisotropy"));
            Require(!string.IsNullOrWhiteSpace(p.Id) && new[] { "canopy_only", "canopy_key", "key_only" }.Contains(p.LightMode) && new[] { "production", "reflection_only" }.Contains(p.MaterialMode), "Unknown profile id/mode");
            string expectedId = (p.LightMode switch { "canopy_only" => "canopy-only", "canopy_key" => "canopy-key", _ => "key-only" }) + (p.MaterialMode == "production" ? "-production" : "-reflection-only");
            Require(p.Id == expectedId, "Profile ID does not match declared modes: " + p.Id);
            Require(p.Exposure is > 0 and <= 10 && p.SunEnergy is >= 0 and <= 10 && p.AmbientEnergy is >= 0 and <= 10 && p.F0 is >= 0 and <= .16f && p.SpecularGain is >= 0 and <= 2 && p.TransmissionGain is >= 0 and <= 4 && p.PhaseGain is >= 0 and <= 4 && p.DiffuseWrap is >= 0 and <= 1 && p.Anisotropy is >= 0 and <= .96f, "Profile value outside permitted range: " + p.Id);
            if (p.LightMode == "canopy_only") Require(p.SunEnergy == 0, "canopy_only must set sun_energy=0");
            if (p.LightMode == "key_only") Require(p.AmbientEnergy == 0, "key_only must set ambient_energy=0");
            if (p.MaterialMode == "reflection_only") Require(p.TransmissionGain == 0 && p.PhaseGain == 0 && p.DiffuseWrap == 0, "reflection_only must zero transport controls");
            all.Add(p);
        }
        var combinations = all.Select(p => p.LightMode + "/" + p.MaterialMode).ToHashSet();
        Require(all.Count == 6 && all.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() == 6 && combinations.Count == 6, "Manifest must contain six unique light/material combinations");
        Require(all.All(p => p.Exposure == 1 && p.F0 == .055f && p.SpecularGain == 1 && p.Anisotropy == .72f && p.SunEnergy == (p.LightMode == "canopy_only" ? 0 : 1.2f) && p.AmbientEnergy == (p.LightMode == "key_only" ? 0 : .42f) && p.TransmissionGain == (p.MaterialMode == "production" ? 1.9f : 0) && p.PhaseGain == (p.MaterialMode == "production" ? 1.6f : 0) && p.DiffuseWrap == 0), "Manifest changes a fixed non-target control");
        return all.ToArray();
    }

    public override async void _Ready()
    {
        try { await Run(); GD.Print((WetLayerSweep ? "PHOTO_WET_LAYER_REVIEW_OK " : BacklightSweep ? "PHOTO_BACKLIGHT_REVIEW_OK " : "PHOTO_FAMILY_REVIEW_OK ") + OutDir); GetTree().Quit(0); }
        catch (Exception ex) { GD.PrintErr("PHOTO_FAMILY_REVIEW_FAILED " + ex); GetTree().Quit(2); }
    }

    private async Task Run()
    {
        if (string.IsNullOrWhiteSpace(OutDir) || string.IsNullOrWhiteSpace(ProfilePath)) throw new ArgumentException("OutDir and ProfilePath are required");
        var profiles = ReadProfiles(Path.GetFullPath(ProfilePath)); // reject invalid contract before touching output/render state
        OutDir = Path.GetFullPath(OutDir);
        if (Directory.Exists(OutDir) && Directory.GetFileSystemEntries(OutDir).Length != 0) throw new IOException("Output directory is nonempty; refusing overwrite: " + OutDir);
        Directory.CreateDirectory(OutDir);
        GetWindow().Size = new Vector2I(1600, 900);
        _rig = new EnvironmentRig { BenchmarkLightSeconds = 60 }; AddChild(_rig);
        await Frames(2); _rig.SetProcess(false); _rig.ApplyQuality(2);
        _rig.CameraAttributes.AutoExposureEnabled = false;
        _rig.Env.FogEnabled = false; _rig.Env.BackgroundMode = Godot.Environment.BGMode.Sky;
        _rig.Env.AmbientLightColor = new Color(.72f, .79f, .85f); _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        _rig.Env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        _rig.Sun.RotationDegrees = new Vector3(-48, -42, 0);
        var lightRig = new List<object>();
        DisablePrivateFill(_rig, lightRig);
        _camera = new Camera3D { Current = true, Fov = 42, Near = .01f, Far = 40, Attributes = _rig.CameraAttributes };
        AddChild(_camera);

        var desc = Content.PresetOrThrow("default"); desc.Diameter = 5; desc.CellSize = .1; desc.PilotTreeId = "none";
        desc.Placement.Rocks = 0; desc.Placement.Logs = 0; desc.Placement.GravelPatches = 0; desc.Water.Springs.Clear(); desc.Water.WaterTable = -.4;
        _world = VivariumWorld.Create(Content, desc, false, false); _world.Clock.Paused = true;
        var hf = _world.Terrain; hf.BeginEdit();
        for (int z = 0; z < hf.Nz; z++) for (int x = 0; x < hf.Nx; x++) { var q = hf.VertexPos(x, z); hf.H[z * hf.Nx + x] = .06 * q.X + .035 * q.Z * q.Z; }
        hf.Touch(); _world.Water.RefreshBed(hf);
        Array.Clear(_world.AmbientGroundCover.Cover); Array.Clear(_world.Litter.FineMass);
        foreach (var c in _world.Grid.DomainCells)
        {
            var q = _world.Grid.CellCenter(c); _world.Fields.Moisture.Values[c] = q.X < 0 ? .12 : .98;
            _world.Fields.BaseSubstrate.Values[c] = (byte)(q.Z < -.4 ? Substrate.Gravel : Substrate.Soil);
        }
        // Use actual initial coverage seeding for the production moss species; no ecology steps are advanced.
        _world.CoverageSystem.SeedInitial();
        _island = new IslandRenderer(); AddChild(_island); _island.Build(_world); _island.SetProcess(false);
        foreach (var e in new[] { ("shadebell", -.7, .2), ("lanternbrush", .7, .25), ("ringreed", -.35, -.55), ("lanternbrush", .35, -.7) })
        {
            var sp = Content.FloraOrThrow(e.Item1); var plant = _world.FloraSystem.Establish(sp, new Vec2(e.Item2, e.Item3), "family-review-v1", sp.MaxBiomass); plant.Age = sp.MaturityAge + 1;
        }
        _flora = new FloraRenderer { Camera = _camera, Quality = 1 }; AddChild(_flora); _flora.Build(_world);
        for (int i = 0; i < 2400 && !_flora.PopulationReady; i++) await Frames(1);
        if (!_flora.PopulationReady) throw new TimeoutException("Production flora renderer did not become ready");
        _flora.SetProcess(false);
        _coverage = new CoverageRenderer { Camera = _camera, Quality = 1 }; AddChild(_coverage); _coverage.Build(_world);
        for (int i = 0; i < 600 && _coverage.PendingRebuildCount > 0; i++) await Frames(1);
        _coverage.SetProcess(false);
        if (_coverage.TileCount == 0 || _coverage.InstanceCount == 0 || _coverage.PendingRebuildCount != 0) throw new InvalidOperationException("Production moss coverage fixture did not reach renderable, settled tiles");
        Collect(_flora);
        if (!_materials.Values.Any(m => m.Shader.ResourcePath == "res://Shaders/leaf.gdshader") || !_materials.Values.Any(m => m.Shader.ResourcePath == "res://Shaders/flora.gdshader" && m.GetShaderParameter("surface_mode").AsInt32() == 2))
            throw new InvalidOperationException("Missing production shader-leaf or legacy-leaf material in integrated fixture");
        FreezeMaterials();
        AddStandards();
        _digest = WorldSerializer.Digest(_world);
        var framesBefore = Engine.GetProcessFrames();
        if (WetLayerSweep)
        {
            if (BacklightSweep) throw new InvalidOperationException("WetLayerSweep and BacklightSweep are mutually exclusive");
            await RunWetLayerSweep(profiles.Single(p => p.LightMode == "canopy_key" && p.MaterialMode == "production"), lightRig, framesBefore);
            return;
        }
        if (BacklightSweep)
        {
            await RunBacklightSweep(profiles.Single(p => p.LightMode == "key_only" && p.MaterialMode == "production"), lightRig, framesBefore);
            return;
        }
        // Capture first baseline, all other profile/view pairs, then an identical repeat.
        var baseline = profiles.Single(p => p.LightMode == "canopy_key" && p.MaterialMode == "production");
        Apply(baseline); Aim("patch"); await Capture(baseline, "patch", "baseline-a", lightRig);
        foreach (var p in profiles.Where(p => p != baseline))
            foreach (var view in new[] { "patch", "overview" }) { Apply(p); Aim(view); await Capture(p, view, p.Id + "-" + view, lightRig); }
        Apply(baseline); Aim("overview"); await Capture(baseline, "overview", "baseline-overview", lightRig);
        Apply(baseline); Aim("patch"); await Capture(baseline, "patch", "baseline-repeat", lightRig);
        if (WorldSerializer.Digest(_world) != _digest) throw new InvalidOperationException("World digest changed during render-only capture");
        if (_captureCount != 13 || Engine.GetProcessFrames() - framesBefore > 7200) throw new InvalidOperationException("Capture schedule exceeded safety bound");
        var implementationRecordSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(profiles)))).ToLowerInvariant();
        var manifestSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(ProfilePath)))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new {
            schema_version = 1, fixture = "integrated-production-family-board", claim = "bounded relative-engine diagnostic; not photographic acceptance or physical calibration",
            profile_manifest = Path.GetFullPath(ProfilePath), profile_file_sha256 = manifestSha, implementation_record_sha256 = implementationRecordSha, profiles,
            world_digest = _digest, world_digest_unchanged = true, capture_count = _captureCount,
            world = new { flora_count = _world.Flora.Count, established_fixture = new[] { "shadebell", "lanternbrush", "ringreed" }, moss_tiles = _coverage.TileCount, moss_instances = _coverage.InstanceCount, moss_pending = _coverage.PendingRebuildCount, terrain = "production terrain shader over actual heightfield; dry/wet soil and gravel sectors" },
            production = new { renderers = new[] { "IslandRenderer", "FloraRenderer", "CoverageRenderer" }, frozen_review_time = 0, sway = 0, wind = 0, physical_units_enabled = false, player_save_touched = false, simulation_ticks = 0, readiness_frames = _fixedFrames, camera_attributes = "inherited EnvironmentRig.CameraAttributes", environment_rig_quality = _rig.Quality, canopy = "EnvironmentRig procedural sky retained; black camera background is not claimed; canopy is not measured cloudy sky", private_fill = lightRig, legacy_residual = "Legacy leaf direct backlight path retains its documented c*0.02 term; no fungal membrane subject included." },
            variants = new { reflection_only_shader = _variantShader, limitations = new[] { "reflection_only disables direct tissue transmission, phase, diffuse wrap, leaf transmission overrides and built-in SSS outputs; legacy c*0.02 shader backlight path remains", "moss is production coverage renderer, but its material family is not driven by the nine foliage profile uniforms", "bark is represented by woody shrub bark in production flora; isolated bark/log fixture not included", "overview and patch are fixed review views, not a full habitat acceptance" } },
            captures = _captures
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunWetLayerSweep(Profile production, List<object> lightRig, ulong framesBefore)
    {
        // Keep the production terrain renderer and all its field textures intact. This node-local
        // deep copy receives the only shader edit in the experiment.
        var terrainTop = _island.GetNode<MeshInstance3D>("TerrainTop");
        if (terrainTop.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() != 1)
            throw new InvalidOperationException("Expected the production TerrainTop single-surface ArrayMesh");
        if (mesh.SurfaceGetMaterial(0) is not ShaderMaterial source || source.Shader is null || source.Shader.ResourcePath != "res://Shaders/terrain.gdshader")
            throw new InvalidOperationException("TerrainTop did not bind the expected production terrain shader");
        const string sourceToken = "    ALBEDO = col;";
        string sourceCode = source.Shader.Code;
        if (Count(sourceCode, sourceToken) != 1 || Count(sourceCode, "float silt_clear = 1.0 - wSilt;") != 1 || Count(sourceCode, "float submerged_bed = smoothstep(0.004, 0.018, water_depth);") != 1)
            throw new InvalidOperationException("Production terrain wetness injection anchors changed; refusing ambiguous variant");
        string declaration = "\nuniform float photo_wet_film = 0.0;\nuniform float photo_wet_clearcoat_roughness = 0.20; // diagnostic heuristic, not physical water calibration\n";
        string variantCode = sourceCode.Replace("shader_type spatial;", "shader_type spatial;" + declaration, StringComparison.Ordinal);
        if (Count(variantCode, "uniform float photo_wet_film") != 1) throw new InvalidOperationException("Could not insert local film uniforms exactly once");
        string finalOutput = "    float photo_porous_film_mask = clamp(total_wet * silt_clear * (1.0 - wMoss) * (1.0 - wGrav) * (1.0 - wLeaf) * (1.0 - step(0.5, code)) * (1.0 - submerged_bed), 0.0, 1.0);\n" +
            "    CLEARCOAT = photo_wet_film * photo_porous_film_mask;\n" +
            "    CLEARCOAT_ROUGHNESS = photo_wet_clearcoat_roughness;\n" + sourceToken;
        variantCode = variantCode.Replace(sourceToken, finalOutput, StringComparison.Ordinal);
        if (Count(variantCode, "float photo_porous_film_mask =") != 1 || Count(variantCode, "CLEARCOAT = photo_wet_film") != 1)
            throw new InvalidOperationException("Local film source variant insertion count was not exactly one");
        var localMaterial = source.Duplicate(true) as ShaderMaterial ?? throw new InvalidOperationException("Could not deep-duplicate production terrain material");
        var localShader = new Shader { Code = variantCode, ResourceName = "terrain.photo-wet-layer-local" };
        localMaterial.Shader = localShader;
        terrainTop.MaterialOverride = localMaterial;
        var productionSourceSha = Sha256(sourceCode);
        var variantSourceSha = Sha256(variantCode);

        Apply(production);
        _rig.Env.SsilEnabled = false; _rig.Env.SsaoEnabled = false;
        _rig.CameraAttributes.AutoExposureEnabled = false;
        _rig.Sun.LightEnergy = production.SunEnergy;
        _rig.Sun.ShadowEnabled = true;
        // Above-ground raking key; same light, exposure, world and camera for every material value.
        SetSunSourceDirection(new Vector3(-.66f, .38f, .65f));
        localMaterial.SetShaderParameter("photo_wet_clearcoat_roughness", .20f);
        AimWet("ground-close");
        terrainTop.MaterialOverride = null;
        string baseHash = await CaptureWetLayer(production, "ground-close", "baseline-a", null, null, productionSourceSha, variantSourceSha, lightRig);
        terrainTop.MaterialOverride = localMaterial;
        localMaterial.SetShaderParameter("photo_wet_film", 0f);
        string zeroGateHash = await CaptureWetLayer(production, "ground-close", "film-zero-gate-close", 0f, localMaterial, productionSourceSha, variantSourceSha, lightRig);
        if (baseHash != zeroGateHash) throw new InvalidOperationException("Film-zero variant changed the original production baseline");
        localMaterial.SetShaderParameter("photo_wet_film", .5f);
        await CaptureWetLayer(production, "ground-close", "half-close", .5f, localMaterial, productionSourceSha, variantSourceSha, lightRig);
        localMaterial.SetShaderParameter("photo_wet_film", 1f);
        await CaptureWetLayer(production, "ground-close", "full-close", 1f, localMaterial, productionSourceSha, variantSourceSha, lightRig);
        AimWet("ground-overview");
        terrainTop.MaterialOverride = null;
        await CaptureWetLayer(production, "ground-overview", "baseline-overview", null, null, productionSourceSha, variantSourceSha, lightRig);
        terrainTop.MaterialOverride = localMaterial;
        localMaterial.SetShaderParameter("photo_wet_film", .5f);
        await CaptureWetLayer(production, "ground-overview", "half-overview", .5f, localMaterial, productionSourceSha, variantSourceSha, lightRig);
        localMaterial.SetShaderParameter("photo_wet_film", 1f);
        await CaptureWetLayer(production, "ground-overview", "full-overview", 1f, localMaterial, productionSourceSha, variantSourceSha, lightRig);
        AimWet("ground-close");
        terrainTop.MaterialOverride = null;
        string repeatHash = await CaptureWetLayer(production, "ground-close", "baseline-repeat", null, null, productionSourceSha, variantSourceSha, lightRig);
        if (baseHash != repeatHash) throw new InvalidOperationException("Restored film-zero baseline is not pixel-identical");
        if (WorldSerializer.Digest(_world) != _digest) throw new InvalidOperationException("Wet-layer capture mutated world state");
        if (_captureCount != 8 || Engine.GetProcessFrames() - framesBefore > 7200) throw new InvalidOperationException("Wet-layer sweep exceeded bounded schedule");
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new {
            schema_version = 1, experiment = "localized-porous-wet-film-clearcoat-sweep", claim = "integrated terrain-layer mechanism diagnostic; heuristic clearcoat, not physically calibrated water or photographic acceptance",
            source_shader = "res://Shaders/terrain.gdshader", production_shader_sha256 = productionSourceSha, local_variant_sha256 = variantSourceSha,
            injection = new { source_token = sourceToken, count = 1, local_material_deep_duplicated = true, mesh_material_replaced = false, node_material_override = true,
                preserves_existing_absorbed_moisture = true, clearcoat_roughness = .20f, clearcoat_roughness_range_tested = new[] { .20f }, clearcoat_f0_interpretation = "Godot built-in clearcoat heuristic; not a measured or calibrated water F0" },
            controls = new { film_values = new[] { 0f, .5f, 1f }, absorbed_moisture_darkening = "unchanged production formula (0.72 multiplier path)", wet_pore_roughness = "unchanged production range (0.40-0.52)", submerged_bed_roughness_minimum = .78f,
                mask = "total_wet * silt_clear * (1-wMoss) * (1-wGrav) * (1-wLeaf) * (1-step(0.5,code)) * (1-submerged_bed); code is a hard non-soil guard, other blend weights softly attenuate film; production contact response remains unchanged and is not an independent film control" },
            repeated_baseline = new { initial_sha256 = baseHash, repeat_sha256 = repeatHash, identical = baseHash == repeatHash, film_zero_gate_sha256 = zeroGateHash, film_zero_gate_matches_production = baseHash == zeroGateHash },
            world_digest = _digest, world_digest_unchanged = true, capture_count = _captureCount, player_save_touched = false, simulation_ticks = 0,
            camera = new { auto_exposure = _rig.CameraAttributes.AutoExposureEnabled, fixed_exposure = _rig.Env.TonemapExposure, views = new[] { "ground-close", "ground-overview" }, protected_rois = "ground patch and neutral standards are compositionally held; no pixel segmentation asserted" },
            lighting = new { light = "EnvironmentRig sun, positive-elevation raking direction", direction = _rig.Sun.GlobalBasis.Z.Normalized().ToString(), energy = _rig.Sun.LightEnergy, color = _rig.Sun.LightColor.ToHtml(), shadows = _rig.Sun.ShadowEnabled, ambient_energy = _rig.Env.AmbientLightEnergy, reflections = _rig.Env.ReflectedLightSource.ToString(), ssil = _rig.Env.SsilEnabled, ssao = _rig.Env.SsaoEnabled, private_fill = lightRig },
            readiness = new { world_digest_unchanged = true, production_moss_tiles = _coverage.TileCount, production_moss_instances = _coverage.InstanceCount, moss_pending = _coverage.PendingRebuildCount, frozen_review_time = 0, sway = 0, wind = 0, foliage_readiness_frames = _fixedFrames },
            limits = new[] { "This fixture has no standing water specimen: source water springs are cleared and water table is below terrain; control is moist porous soil/contact response only.", "IslandRenderer writes sub_tex.B=0; terrain cannot know actual dynamic WaterSurfaceRenderer coverage, so this mask does not claim full exclusion beneath transparent dynamic water.", "The local clearcoat changes only a diagnostic surface layer; its energy, IOR/F0 and appearance are not calibrated.", "No shared terrain shader, production renderer, species, source texture, or production moisture state is modified.", "Two fixed views and an identical baseline repeat do not constitute full habitat or photographic acceptance." },
            captures = _captures
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int Count(string text, string token) => (text.Length - text.Replace(token, "", StringComparison.Ordinal).Length) / token.Length;
    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private void AimWet(string view)
    {
        _patchTarget = view == "ground-close" ? new Vector3(.65f, .12f, .1f) : new Vector3(.35f, .12f, .05f);
        _camera.Position = view == "ground-close" ? new Vector3(2.05f, 1.18f, 2.65f) : new Vector3(2.0f, 2.25f, 4.6f);
        _camera.LookAt(_patchTarget); _fixedFrames = 0;
    }
    private async Task<string> CaptureWetLayer(Profile p, string view, string name, float? film, ShaderMaterial? material, string sourceSha, string variantSha, List<object> lightRig)
    {
        if (++_captureCount > 20) throw new InvalidOperationException("Wet-layer hard capture limit 20 exceeded");
        const int settle = 8; _fixedFrames += settle; await Frames(settle); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage(); var file = name + ".png"; var full = Path.Combine(OutDir, file);
        if (image.SavePng(full) != Error.Ok) throw new IOException("Failed to save " + full);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        _captures.Add(new { file, sha256 = hash, name, view, film_value = film, shader_variant = material is null ? "original production terrain shader" : "private local diagnostic clone", clearcoat_roughness = material is null ? (float?)null : material.GetShaderParameter("photo_wet_clearcoat_roughness").AsSingle(), actual_mask_formula = material is null ? "not applicable: original production shader" : "total_wet*silt_clear*(1-wMoss)*(1-wGrav)*(1-wLeaf)*(1-step(0.5,code))*(1-submerged_bed); wMoss/wGrav/wLeaf softly attenuate; code hard-excludes non-soil",
            local_shader_path = "local Shader resource; no source ResourcePath", production_shader_sha256 = sourceSha, variant_shader_sha256 = variantSha,
            camera = new { position = _camera.Position.ToString(), target = _patchTarget.ToString(), fov = _camera.Fov, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled, exposure = _rig.Env.TonemapExposure },
            light = new { source_direction = _rig.Sun.GlobalBasis.Z.Normalized().ToString(), energy = _rig.Sun.LightEnergy, color = _rig.Sun.LightColor.ToHtml(), shadows = _rig.Sun.ShadowEnabled },
            environment = new { ambient_energy = _rig.Env.AmbientLightEnergy, reflection_source = _rig.Env.ReflectedLightSource.ToString(), ssil = _rig.Env.SsilEnabled, ssao = _rig.Env.SsaoEnabled },
            actual_production_absorbed_wet_parameters = new { darkening = .72f, porous_roughness = new[] { .40f, .52f }, submerged_bed_roughness_min = .78f },
            world_digest = WorldSerializer.Digest(_world), readiness_frames = settle, review_time = 0, private_fill = lightRig });
        GD.Print("PHOTO_WET_LAYER_CAPTURE " + name + " sha256=" + hash);
        return hash;
    }

    private void DisablePrivateFill(Node node, List<object> record)
    {
        foreach (var child in node.GetChildren()) { if (child is Light3D light && light != _rig.Sun) { record.Add(new { name = light.Name.ToString(), type = light.GetClass(), previous_energy = light.LightEnergy, effective_energy = 0f }); light.LightEnergy = 0; } DisablePrivateFill(child, record); }
    }
    private void Collect(Node node)
    {
        if (node is MultiMeshInstance3D multi && multi.Multimesh?.Mesh is { } mesh)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) if (mesh.SurfaceGetMaterial(s) is ShaderMaterial mat)
            {
                ulong id = mat.GetInstanceId(); _materials[id] = mat; _originalShaders[id] = mat.Shader;
                if (mat.Shader.ResourcePath is "res://Shaders/leaf.gdshader" or "res://Shaders/flora.gdshader")
                {
                    float Read(string key, float fallback) { var v = mat.GetShaderParameter(key); return v.VariantType == Variant.Type.Nil ? fallback : v.AsSingle(); }
                    _productionTransmission[id] = (Read("transmission", -1f), Read("leaf_transmission", mat.Shader.ResourcePath.EndsWith("flora.gdshader", StringComparison.Ordinal) ? .25f : .28f));
                }
            }
        foreach (Node child in node.GetChildren()) Collect(child);
    }
    private void FreezeMaterials()
    {
        foreach (var mat in _materials.Values)
        {
            mat.SetShaderParameter("review_time", 0f); mat.SetShaderParameter("sway", 0f); mat.SetShaderParameter("wind_strength", 0f);
            mat.SetShaderParameter("tissue_use_explicit_material_f0", true);
        }
        NightFloraMaterials.SetNightFactor(0f);
    }
    private void AddStandards()
    {
        Standard("diffuse", new Vector3(-1.25f, .35f, 1.15f), .72f); Standard("glossy", new Vector3(1.25f, .35f, 1.15f), .12f);
    }
    private void Standard(string name, Vector3 pos, float roughness)
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(.48f, .48f, .48f), Roughness = roughness, Metallic = 0, MetallicSpecular = .5f };
        AddChild(new MeshInstance3D { Name = name, Mesh = new SphereMesh { Radius = .18f, Height = .36f, RadialSegments = 32, Rings = 16, Material = mat }, Position = pos });
    }
    private void Apply(Profile p)
    {
        _rig.Env.TonemapExposure = p.Exposure; _rig.Sun.LightEnergy = p.SunEnergy;
        _rig.Env.AmbientLightEnergy = p.AmbientEnergy;
        _rig.Env.AmbientLightSource = p.LightMode == "key_only" ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky;
        _rig.Env.AmbientLightColor = p.LightMode == "key_only" ? Colors.Black : new Color(.72f, .79f, .85f);
        _rig.Env.ReflectedLightSource = p.LightMode == "key_only" ? Godot.Environment.ReflectionSource.Disabled : Godot.Environment.ReflectionSource.Sky;
        foreach (var mat in _materials.Values)
        {
            var materialId = mat.GetInstanceId();
            bool foliageShader = _originalShaders[materialId].ResourcePath is "res://Shaders/leaf.gdshader" or "res://Shaders/flora.gdshader";
            mat.Shader = p.MaterialMode == "reflection_only" && foliageShader ? ReflectionShader(materialId) : _originalShaders[materialId];
            mat.SetShaderParameter("tissue_use_explicit_material_f0", true); mat.SetShaderParameter("tissue_material_f0", p.F0);
            mat.SetShaderParameter("tissue_specular_stress", p.SpecularGain); mat.SetShaderParameter("tissue_direct_transmission", p.TransmissionGain);
            mat.SetShaderParameter("tissue_phase_scatter", p.PhaseGain); mat.SetShaderParameter("tissue_diffuse_wrap", p.DiffuseWrap); mat.SetShaderParameter("tissue_fiber_anisotropy", p.Anisotropy);
            if (foliageShader)
            {
                var refl = p.MaterialMode == "reflection_only";
                var production = _productionTransmission[materialId];
                mat.SetShaderParameter("transmission", refl ? 0f : production.transmission); mat.SetShaderParameter("leaf_transmission", refl ? 0f : production.leafTransmission);
                mat.SetShaderParameter("tissue_direct_transmission", refl ? 0f : p.TransmissionGain);
                mat.SetShaderParameter("tissue_phase_scatter", refl ? 0f : p.PhaseGain); mat.SetShaderParameter("tissue_diffuse_wrap", refl ? 0f : p.DiffuseWrap);
            }
        }
    }
    private Shader ReflectionShader(ulong id)
    {
        if (_reflectionShaders.TryGetValue(id, out var existing)) return existing;
        var shader = _originalShaders[id];
        string code = shader.Code;
        code = System.Text.RegularExpressions.Regex.Replace(code, @"(?m)^\s*SSS_TRANSMITTANCE_COLOR\s*=.*?;\s*$", "    SSS_TRANSMITTANCE_COLOR = vec4(0.0); // PHOTO_FAMILY_REFLECTION_ONLY");
        code = System.Text.RegularExpressions.Regex.Replace(code, @"(?m)^\s*SSS_TRANSMITTANCE_DEPTH\s*=.*?;\s*$", "    SSS_TRANSMITTANCE_DEPTH = 0.0;");
        code = System.Text.RegularExpressions.Regex.Replace(code, @"(?m)^\s*SSS_TRANSMITTANCE_BOOST\s*=.*?;\s*$", "    SSS_TRANSMITTANCE_BOOST = 0.0;");
        if (!code.Contains("PHOTO_FAMILY_REFLECTION_ONLY", StringComparison.Ordinal)) throw new InvalidOperationException("Could not create local SSS-disabled shader variant");
        var clone = new Shader { Code = code, ResourceName = shader.ResourceName + ".photo-family-reflection-only" };
        _variantShader = (string.IsNullOrEmpty(shader.ResourcePath) ? shader.ResourceName : shader.ResourcePath) + " -> local zero-SSS variant";
        _reflectionShaders[id] = clone;
        return clone;
    }
    private void Aim(string view)
    {
        var target = view == "patch" ? _patchTarget : new Vector3(0, .25f, 0);
        _camera.Position = view == "patch" ? new Vector3(2.6f, 2.25f, 3.6f) : new Vector3(0, 5.2f, 7.0f);
        _camera.LookAt(target); _fixedFrames = 0;
    }

    private async Task RunBacklightSweep(Profile production, List<object> lightRig, ulong framesBefore)
    {
        if (production.LightMode != "key_only" || production.MaterialMode != "production") throw new InvalidOperationException("Backlight sweep requires fixed key_only production profile");
        Apply(production); Aim("patch");
        _rig.Env.AmbientLightEnergy = 0; _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        _rig.Env.AmbientLightColor = Colors.Black; _rig.Env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        _rig.Env.SsilEnabled = false; _rig.Env.SsaoEnabled = false; _rig.CameraAttributes.AutoExposureEnabled = false;
        _rig.Sun.LightEnergy = 1.2f; _rig.Sun.ShadowEnabled = true;
        Vector3 viewDirection = (_camera.Position - _patchTarget).Normalized();
        Vector3 horizontalView = new Vector3(viewDirection.X, 0, viewDirection.Z).Normalized();
        const float sourceElevation = .35f;
        float horizontalMagnitude = Mathf.Sqrt(1f - sourceElevation * sourceElevation);
        Vector3 frontSource = new(horizontalView.X * horizontalMagnitude, sourceElevation, horizontalView.Z * horizontalMagnitude);
        Vector3 backSource = new(-horizontalView.X * horizontalMagnitude, sourceElevation, -horizontalView.Z * horizontalMagnitude);
        RestoreProductionTransport(production);
        SetSunSourceDirection(frontSource);
        await CaptureBacklight(production, "front-production", frontSource, lightRig);
        SetTissueDirectGain(0f);
        await CaptureBacklight(production, "front-transmission-zero", frontSource, lightRig);
        SetSunSourceDirection(backSource); RestoreProductionTransport(production);
        string baselineA = await CaptureBacklight(production, "baseline-a", backSource, lightRig);
        foreach (var mat in FoliageMaterials()) mat.SetShaderParameter("tissue_direct_transmission", production.TransmissionGain * .5f);
        await CaptureBacklight(production, "transmission-half", backSource, lightRig);
        SetTissueDirectGain(0f);
        await CaptureBacklight(production, "transmission-zero", backSource, lightRig);
        foreach (var mat in FoliageMaterials())
        {
            ulong id = mat.GetInstanceId(); mat.Shader = ReflectionShader(id);
            mat.SetShaderParameter("tissue_direct_transmission", 0f); mat.SetShaderParameter("tissue_phase_scatter", 0f); mat.SetShaderParameter("tissue_diffuse_wrap", 0f);
            mat.SetShaderParameter("transmission", 0f); mat.SetShaderParameter("leaf_transmission", 0f);
        }
        await CaptureBacklight(production with { MaterialMode = "reflection_only" }, "reflection-only-control", backSource, lightRig);
        RestoreProductionTransport(production);
        string baselineRepeat = await CaptureBacklight(production, "baseline-repeat", backSource, lightRig);
        if (baselineA != baselineRepeat) throw new InvalidOperationException("Repeated backlight baseline images were not pixel-identical");
        if (WorldSerializer.Digest(_world) != _digest) throw new InvalidOperationException("Backlight capture mutated world state");
        if (_captureCount != 7 || Engine.GetProcessFrames() - framesBefore > 7200) throw new InvalidOperationException("Backlight sweep exceeded capture or time bounds");
        var manifestSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(ProfilePath)))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new {
            schema_version = 1, experiment = "matched-production-leaf-front-vs-back-key-light-and-tissue-gain-sweep", claim = "controlled mechanism diagnostic; not energy-conservation proof, photographic fit, or production gain recommendation",
            profile_file = Path.GetFullPath(ProfilePath), profile_file_sha256 = manifestSha, selected_profile = production, captures = _captures,
            repeated_backlight_baseline_hashes = new { a = baselineA, repeat = baselineRepeat, identical = true }, world_digest = _digest, world_digest_unchanged = true,
            frame_count = _captureCount, player_save_touched = false, simulation_ticks = 0,
            camera = new { view = "fixed existing patch camera", position = _camera.Position.ToString(), target = _patchTarget.ToString(), fov = _camera.Fov, exposure = _rig.Env.TonemapExposure, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled },
            light = new { energy = _rig.Sun.LightEnergy, color = _rig.Sun.LightColor.ToHtml(), shadows = _rig.Sun.ShadowEnabled, key_only = true, ambient_energy = 0, reflections = "disabled", ssil = false, ssao = false },
            materials = new { baseline = "production shader and built-in SSS retained for direct-gain candidates; only tissue_direct_transmission varies (1.9, .95, 0), phase remains 1.6", reflection_only = "separate local zero-SSS/zero-transmission control; not compared as a direct-gain candidate", f0 = production.F0, specular_gain = production.SpecularGain, phase_gain = production.PhaseGain, direct_gain_candidates = new[] { production.TransmissionGain, production.TransmissionGain * .5f, 0f }, shaders = FoliageMaterials().Select(m => _originalShaders[m.GetInstanceId()].ResourcePath).Distinct().ToArray(), actual_uniforms_per_capture = true },
            limits = new[] { "shadow casting retained, so source angular change modifies leaf and terrain visibility as part of the mechanism", "front/back sources have equal positive elevation and opposite horizontal vectors; they are not exact 180-degree opposites", "front/back source direction is defined relative to camera-to-board-target axis, not each leaf normal", "pixel ROI must be selected after native images are available", "diagnostic does not establish energy conservation or photographic match" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private IEnumerable<ShaderMaterial> FoliageMaterials() => _materials.Values.Where(m => _originalShaders[m.GetInstanceId()].ResourcePath is "res://Shaders/leaf.gdshader" or "res://Shaders/flora.gdshader");

    private void SetTissueDirectGain(float gain)
    { foreach (var mat in FoliageMaterials()) mat.SetShaderParameter("tissue_direct_transmission", gain); }

    private void RestoreProductionTransport(Profile p)
    {
        foreach (var mat in FoliageMaterials())
        {
            ulong id = mat.GetInstanceId(); mat.Shader = _originalShaders[id];
            mat.SetShaderParameter("tissue_use_explicit_material_f0", true); mat.SetShaderParameter("tissue_material_f0", p.F0);
            mat.SetShaderParameter("tissue_specular_stress", p.SpecularGain); mat.SetShaderParameter("tissue_direct_transmission", p.TransmissionGain);
            mat.SetShaderParameter("tissue_phase_scatter", p.PhaseGain); mat.SetShaderParameter("tissue_diffuse_wrap", p.DiffuseWrap); mat.SetShaderParameter("tissue_fiber_anisotropy", p.Anisotropy);
            var original = _productionTransmission[id]; mat.SetShaderParameter("transmission", original.transmission); mat.SetShaderParameter("leaf_transmission", original.leafTransmission);
        }
    }

    private void SetSunSourceDirection(Vector3 sourceDirection)
    {
        sourceDirection = sourceDirection.Normalized();
        _rig.Sun.Basis = Basis.LookingAt(-sourceDirection, Vector3.Up);
        if (_rig.Sun.GlobalBasis.Z.Normalized().Dot(sourceDirection) < .999f)
            throw new InvalidOperationException("Directional key did not bind the requested above-ground source vector");
    }

    private async Task<string> CaptureBacklight(Profile p, string name, Vector3 requestedSourceDirection, List<object> lightRig)
    {
        if (++_captureCount > 20) throw new InvalidOperationException("Hard capture limit 20 exceeded");
        const int settle = 8; _fixedFrames += settle; await Frames(settle); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage(); var full = Path.Combine(OutDir, name + ".png");
        if (image.SavePng(full) != Error.Ok) throw new IOException("Failed to save " + full);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        var actualSource = _rig.Sun.GlobalBasis.Z.Normalized(); var viewDirection = (_camera.Position - _patchTarget).Normalized();
        _captures.Add(new { file = Path.GetFileName(full), sha256 = hash, id = p.Id, mode = p.MaterialMode, name,
            light = new { requested_source_direction = requestedSourceDirection.ToString(), actual_source_direction = actualSource.ToString(), camera_view_direction = viewDirection.ToString(), source_camera_dot = actualSource.Dot(viewDirection), energy = _rig.Sun.LightEnergy, color = _rig.Sun.LightColor.ToHtml(), shadows = _rig.Sun.ShadowEnabled, shadow_bias = _rig.Sun.ShadowBias, shadow_normal_bias = _rig.Sun.ShadowNormalBias },
            camera = new { position = _camera.Position.ToString(), target = _patchTarget.ToString(), fov = _camera.Fov, exposure = _rig.Env.TonemapExposure, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled },
            environment = new { ambient_energy = _rig.Env.AmbientLightEnergy, reflection_source = _rig.Env.ReflectedLightSource.ToString(), ssil_enabled = _rig.Env.SsilEnabled, ssao_enabled = _rig.Env.SsaoEnabled },
            actual_foliage_uniforms = ActualFoliageControls(), world_digest = WorldSerializer.Digest(_world), readiness_frames = settle, review_time = 0, private_fill = lightRig });
        GD.Print("PHOTO_BACKLIGHT_CAPTURE " + name + " sha256=" + hash);
        return hash;
    }
    private async Task Capture(Profile p, string view, string name, List<object> lightRig)
    {
        if (++_captureCount > 20) throw new InvalidOperationException("Hard capture limit 20 exceeded");
        const int settle = 8; _fixedFrames += settle; await Frames(settle); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var file = name + ".png"; var error = image.SavePng(Path.Combine(OutDir, file)); if (error != Error.Ok) throw new IOException(file + ": " + error);
        _captures.Add(new { file, id = p.Id, p.LightMode, p.MaterialMode, view, camera_position = _camera.Position.ToString(), camera_rotation = _camera.Rotation.ToString(), fov = _camera.Fov,
            exposure = _rig.Env.TonemapExposure, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled, sun_energy_actual = _rig.Sun.LightEnergy, ambient_energy_actual = _rig.Env.AmbientLightEnergy,
            reflection_source = _rig.Env.ReflectedLightSource.ToString(), ssil_enabled = _rig.Env.SsilEnabled, ssil_intensity = _rig.Env.SsilIntensity, ssao_enabled = _rig.Env.SsaoEnabled, ssao_intensity = _rig.Env.SsaoIntensity,
            readiness_frames = settle, review_time = 0, material_variant = p.MaterialMode == "reflection_only" ? _variantShader : "production shader", foliage_uniforms = ActualFoliageControls(),
            world_digest = WorldSerializer.Digest(_world), protected_subject_rois = "not pixel segmented; production target and standards centered in both fixed views", private_fill = lightRig });
        GD.Print("PHOTO_FAMILY_CAPTURE " + file);
    }
    private object[] ActualFoliageControls() => _materials.Values.Where(m => _originalShaders[m.GetInstanceId()].ResourcePath is "res://Shaders/leaf.gdshader" or "res://Shaders/flora.gdshader")
        .Select(m => new { material_id = m.GetInstanceId(), shader = _originalShaders[m.GetInstanceId()].ResourcePath, f0 = m.GetShaderParameter("tissue_material_f0").AsSingle(), specular_gain = m.GetShaderParameter("tissue_specular_stress").AsSingle(),
            direct_transmission = m.GetShaderParameter("tissue_direct_transmission").AsSingle(), phase_gain = m.GetShaderParameter("tissue_phase_scatter").AsSingle(), diffuse_wrap = m.GetShaderParameter("tissue_diffuse_wrap").AsSingle(), anisotropy = m.GetShaderParameter("tissue_fiber_anisotropy").AsSingle(),
            transmission_override = m.GetShaderParameter("transmission").AsSingle(), leaf_transmission = m.GetShaderParameter("leaf_transmission").AsSingle(),
            leaf_top_pigment = m.GetShaderParameter("leaf_top").ToString(), leaf_back_pigment = m.GetShaderParameter("leaf_back").ToString(), top_pigment = m.GetShaderParameter("top_pigment").ToString(), back_pigment = m.GetShaderParameter("back_pigment").ToString(),
            leaf_roughness_input = m.GetShaderParameter("leaf_roughness").ToString(), back_roughness_input = m.GetShaderParameter("back_roughness").ToString(), cuticle_sheen = m.GetShaderParameter("cuticle_sheen").ToString() }).Cast<object>().ToArray();
}
