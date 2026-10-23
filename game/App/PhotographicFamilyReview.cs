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
    public bool LookSweep { get; set; } = false;
    public bool LookRefinement { get; set; } = false;
    public bool SubstrateSweep { get; set; } = false;

    private sealed record Profile(string Id, string LightMode, string MaterialMode, float Exposure,
        float SunEnergy, float AmbientEnergy, float F0, float SpecularGain, float TransmissionGain,
        float PhaseGain, float DiffuseWrap, float Anisotropy);
    private sealed record LookConfig(string Id, float Exposure, float Contrast, float SunEnergy, float AmbientEnergy,
        Color SunColor, Color AmbientColor, Vector3 SourceDirection, float F0, float SpecularGain,
        float TransmissionGain, float PhaseGain, float MossScale, Color MossTint, float Film, float FilmRoughness,
        float CoverageSaturation, float CoverageValue, bool SkyAmbient, float GroundMossScale, Color? DampTint = null);
    private readonly List<object> _captures = new();
    private readonly Dictionary<ulong, ShaderMaterial> _materials = new();
    private readonly Dictionary<ulong, Shader> _originalShaders = new();
    private readonly Dictionary<ulong, Shader> _reflectionShaders = new();
    private readonly Dictionary<ulong, (float transmission, float leafTransmission)> _productionTransmission = new();
    private readonly List<(MeshInstance3D node, Material? original, ShaderMaterial local)> _lookCoverageMaterials = new();
    private EnvironmentRig _rig = null!;
    private Camera3D _camera = null!;
    private VivariumWorld _world = null!;
    private IslandRenderer _island = null!;
    private FloraRenderer _flora = null!;
    private CoverageRenderer _coverage = null!;
    private string _digest = "";
    private string _variantShader = "";
    private string _lookCoverageSourceHash = "";
    private string _lookCoverageVariantHash = "";
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
        try { await Run(); GD.Print((LookSweep ? "PHOTO_LOOK_SWEEP_OK " : WetLayerSweep ? "PHOTO_WET_LAYER_REVIEW_OK " : BacklightSweep ? "PHOTO_BACKLIGHT_REVIEW_OK " : "PHOTO_FAMILY_REVIEW_OK ") + OutDir); GetTree().Quit(0); }
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
        if (LookSweep)
        {
            if (BacklightSweep || WetLayerSweep || (LookRefinement && SubstrateSweep)) throw new InvalidOperationException("LookSweep cannot be combined with another photographic sweep mode");
            await RunLookSweep(lightRig, framesBefore);
            return;
        }
        if (LookRefinement || SubstrateSweep) throw new InvalidOperationException("LookRefinement and SubstrateSweep require LookSweep mode dispatch");
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

    private async Task RunLookSweep(List<object> lightRig, ulong framesBefore)
    {
        // This matrix is a set of plausible look directions, not a winner-selection rule.
        // All values are bounded relative-engine controls under the same frozen scene/cameras.
        var authoredDirection = _rig.Sun.GlobalBasis.Z.Normalized();
        float rigExposure = _rig.Env.TonemapExposure, rigContrast = _rig.Env.TonemapAgxContrast;
        float rigSunEnergy = _rig.Sun.LightEnergy, rigAmbientEnergy = _rig.Env.AmbientLightEnergy;
        Color rigSunColor = _rig.Sun.LightColor, rigAmbientColor = _rig.Env.AmbientLightColor;
        var rigReference = new LookConfig("rig-reference", rigExposure, rigContrast, rigSunEnergy, rigAmbientEnergy, rigSunColor, rigAmbientColor, authoredDirection, .055f, 1f, 1.9f, 1.6f, 1f, Colors.White, 0f, .30f, 1f, 1f, true, 1f);
        var balancedGrounded = new LookConfig("balanced-grounded", 1.12f, 1.30f, 1.25f, .60f, new Color(1f,.90f,.78f), new Color(.76f,.82f,.88f), new Vector3(-.48f,.66f,.57f), .05f, .90f, 1.30f, 1.10f, .35f, new Color(.80f,.84f,.74f), 0f, .30f, .50f, .85f, false, .20f);
        var looks = SubstrateSweep ? new[]
        {
            rigReference,
            balancedGrounded,
            balancedGrounded with { Id = "balanced-umber-mild", DampTint = new Color(1f,.72f,.68f) },
            balancedGrounded with { Id = "balanced-umber-strong", DampTint = new Color(1f,.55f,.65f) },
        } : LookRefinement ? new[]
        {
            rigReference,
            new LookConfig("neutral-grounded", 1.05f, 1.25f, 1.1f, .48f, new Color(.98f,.96f,.90f), new Color(.78f,.83f,.86f), new Vector3(.15f,.94f,.30f), .05f, .85f, 1.25f, 1.05f, .40f, new Color(.80f,.84f,.78f), 0f, .30f, .60f, .88f, false, .25f),
            balancedGrounded,
            new LookConfig("rake-grounded", 1.10f, 1.25f, 1.40f, .40f, new Color(1f,.86f,.68f), new Color(.76f,.82f,.90f), new Vector3(-.60f,.50f,.62f), .05f, .90f, 1.30f, 1.10f, .45f, new Color(.80f,.84f,.74f), 0f, .30f, .60f, .88f, false, .35f),
        } : new[]
        {
            rigReference,
            new LookConfig("soft-overcast", 1f, 1f, .40f, .72f, new Color(.88f,.92f,1f), new Color(.78f,.84f,.92f), new Vector3(.45f,.78f,.43f), .045f, .85f, 1.25f, 1.05f, .72f, new Color(.82f,.84f,.78f), 0f, .32f, .85f, .90f, false, 1f),
            new LookConfig("warm-side-rake", 1f, 1f, 1.35f, .30f, new Color(1f,.78f,.57f), new Color(.66f,.76f,.88f), new Vector3(-.66f,.38f,.65f), .05f, .90f, 1.25f, 1.10f, .72f, new Color(.80f,.82f,.70f), 0f, .30f, .80f, .90f, false, 1f),
            new LookConfig("neutral-high-angle", 1f, 1f, 1f, .48f, Colors.White, new Color(.84f,.86f,.82f), new Vector3(.15f,.94f,.30f), .045f, .85f, 1.35f, 1.10f, .68f, new Color(.82f,.84f,.78f), 0f, .32f, .82f, .88f, false, 1f),
            new LookConfig("cool-shade-warm-break", 1f, 1f, 1.2f, .56f, new Color(1f,.84f,.66f), new Color(.68f,.78f,.94f), new Vector3(-.55f,.58f,.60f), .05f, .90f, 1.4f, 1.2f, .72f, new Color(.78f,.82f,.72f), 0f, .30f, .80f, .90f, false, 1f),
            new LookConfig("damp-restrained-glint", 1f, 1f, .90f, .52f, new Color(.93f,.96f,1f), new Color(.78f,.83f,.88f), new Vector3(-.66f,.38f,.65f), .05f, .85f, 1.15f, 1f, .65f, new Color(.80f,.83f,.78f), .25f, .30f, .80f, .82f, false, 1f),
            new LookConfig("bright-clear-canopy", 1f, 1f, 1.55f, .38f, new Color(1f,.94f,.82f), new Color(.76f,.82f,.88f), new Vector3(.15f,.90f,.40f), .05f, .85f, 1.2f, 1f, .68f, new Color(.82f,.84f,.75f), 0f, .32f, .82f, .86f, false, 1f),
            new LookConfig("balanced-agx-contrast", 1.12f, 1.2f, 1.15f, .52f, new Color(1f,.86f,.70f), new Color(.76f,.82f,.88f), new Vector3(-.48f,.66f,.57f), .05f, .90f, 1.3f, 1.1f, .68f, new Color(.80f,.84f,.74f), 0f, .32f, .55f, .85f, false, 1f),
        };
        int expectedLooks = LookRefinement || SubstrateSweep ? 4 : 8;
        if (looks.Length != expectedLooks || looks.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != expectedLooks)
            throw new InvalidOperationException("Look matrix has an unexpected count or duplicate direction");
        GD.Print("PHOTO_LOOK_REFERENCE " + JsonSerializer.Serialize(LookRecord(looks[0])));
        foreach (var l in looks.Where(l => l.Id != "rig-reference"))
            if (l.Exposure is < .7f or > 1.3f || l.Contrast is < .8f or > 1.5f || l.SunEnergy is < 0 or > 2.5f || l.AmbientEnergy is < 0 or > 1.2f || l.F0 is < .02f or > .08f || l.SpecularGain is < .4f or > 1.4f || l.TransmissionGain is < .5f or > 2.2f || l.PhaseGain is < .3f or > 1.8f || l.MossScale is < .2f or > 1f || l.GroundMossScale is < .15f or > 1f || l.MossTint.R is < .6f or > 1f || l.MossTint.G is < .6f or > 1f || l.MossTint.B is < .6f or > 1f || l.Film is < 0 or > .5f || l.FilmRoughness is < .2f or > .4f || l.CoverageSaturation is < .4f or > 1f || l.CoverageValue is < .65f or > 1f)
                throw new InvalidOperationException("Look configuration outside bounded review range: " + l.Id);

        var terrainTop = _island.GetNode<MeshInstance3D>("TerrainTop");
        if (terrainTop.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() != 1 || mesh.SurfaceGetMaterial(0) is not ShaderMaterial source || source.Shader is null || source.Shader.ResourcePath != "res://Shaders/terrain.gdshader")
            throw new InvalidOperationException("LookSweep requires the integrated production TerrainTop material");
        string code = source.Shader.Code;
        const string wMossAnchor = "    float wGrav = smoothstep(0.16, 0.84, sub.r + gravel_edge);";
        const string mossColorAnchor = "        vec3 natural_moss = mix(m.rgb, m.rgb * vec3(0.74, 0.88, 0.58), 0.5);";
        const string groundMossAnchor = "    gm *= smoothstep(0.28, 0.64, patchn + contact_mask * 0.06);";
        const string dampBlendAnchor = "        float w = mix(wDamp, hblend(wDamp, dot(d.rgb, vec3(0.33)) * 2.0), 0.35);   // soft: moisture creeps, it has no edge";
        const string outputAnchor = "    ALBEDO = col;";
        if (Count(code, wMossAnchor) != 1 || Count(code, mossColorAnchor) != 1 || Count(code, groundMossAnchor) != 1 || Count(code, outputAnchor) != 1 || Count(code, dampBlendAnchor) != 1 || Count(code, "float silt_clear = 1.0 - wSilt;") != 1 || Count(code, "float submerged_bed = smoothstep(0.004, 0.018, water_depth);") != 1)
            throw new InvalidOperationException("LookSweep terrain source anchors changed; refusing ambiguous private variant");
        string uniforms = "\nuniform float photo_look_moss_scale = 1.0;\nuniform float photo_look_ground_moss_scale = 1.0;\nuniform vec3 photo_look_moss_tint = vec3(1.0);\nuniform vec3 photo_look_damp_tint = vec3(1.0);\nuniform float photo_look_wet_film = 0.0;\nuniform float photo_look_wet_roughness = 0.28;\n";
        code = code.Replace("shader_type spatial;", "shader_type spatial;" + uniforms, StringComparison.Ordinal);
        code = code.Replace(wMossAnchor, wMossAnchor + "\n    wMoss *= photo_look_moss_scale;", StringComparison.Ordinal);
        code = code.Replace(groundMossAnchor, groundMossAnchor + "\n    gm *= photo_look_ground_moss_scale;", StringComparison.Ordinal);
        code = code.Replace(mossColorAnchor, mossColorAnchor + "\n        natural_moss *= photo_look_moss_tint;", StringComparison.Ordinal);
        code = code.Replace(dampBlendAnchor, dampBlendAnchor + "\n        d.rgb *= photo_look_damp_tint;", StringComparison.Ordinal);
        string filmOutput = "    float photo_look_film_mask = clamp(total_wet * (1.0 - wMoss) * (1.0 - wGrav) * (1.0 - wLeaf) * (1.0 - step(0.5, code)) * (1.0 - wSilt) * (1.0 - submerged_bed), 0.0, 1.0);\n" +
            "    CLEARCOAT = photo_look_wet_film * photo_look_film_mask;\n    CLEARCOAT_ROUGHNESS = photo_look_wet_roughness;\n" + outputAnchor;
        code = code.Replace(outputAnchor, filmOutput, StringComparison.Ordinal);
        if (Count(code, "uniform float photo_look_moss_scale") != 1 || Count(code, "uniform float photo_look_ground_moss_scale") != 1 || Count(code, "uniform vec3 photo_look_moss_tint") != 1 || Count(code, "uniform vec3 photo_look_damp_tint") != 1 || Count(code, "wMoss *= photo_look_moss_scale;") != 1 || Count(code, "gm *= photo_look_ground_moss_scale;") != 1 || Count(code, "natural_moss *= photo_look_moss_tint;") != 1 || Count(code, "d.rgb *= photo_look_damp_tint;") != 1 || Count(code, "float photo_look_film_mask =") != 1 || Count(code, "CLEARCOAT = photo_look_wet_film") != 1)
            throw new InvalidOperationException("LookSweep local source variant failed exact single-insertion checks");
        var variant = new Shader { Code = code, ResourceName = "terrain.photo-look-sweep-local" };
        var localMaterial = source.Duplicate(true) as ShaderMaterial ?? throw new InvalidOperationException("Could not deep-duplicate production terrain material for LookSweep");
        localMaterial.Shader = variant;
        string sourceHash = Sha256(source.Shader.Code), variantHash = Sha256(code);
        var coverageShader = BuildCoverageLookVariant();
        _lookCoverageSourceHash = Sha256((GD.Load<Shader>("res://Shaders/coverage_mat.gdshader") ?? throw new InvalidOperationException("Missing coverage shader for hash" )).Code);
        _lookCoverageVariantHash = Sha256(coverageShader.Code);
        int coverageMaterialCount = CloneCoverageMaterials(coverageShader);
        if (coverageMaterialCount == 0) throw new InvalidOperationException("LookSweep found no production coverage_mat materials to vary locally");
        File.WriteAllText(Path.Combine(OutDir, "terrain-look-sweep-local.gdshader"), code);
        File.WriteAllText(Path.Combine(OutDir, "coverage-look-sweep-local.gdshader"), coverageShader.Code);
        var lookRecords = looks.Select(LookRecord).ToArray();
        File.WriteAllText(Path.Combine(OutDir, "look-configs.json"), JsonSerializer.Serialize(new { schema_version = 1, units = "relative_engine", configs = lookRecords }, new JsonSerializerOptions { WriteIndented = true }));

        // Refinement holds a reference and three grounded alternatives: 4 x 2 views + repeat + local gate = 10.
        // The original broad look matrix remains 8 x 2 + repeat + local gate = 18.
        var baseline = looks[0];
        ApplyLook(baseline, null);
        terrainTop.MaterialOverride = null;
        Aim("patch");
        string baselinePatch = await CaptureLook(baseline, "patch-foliage", "rig-reference-patch", null, sourceHash, variantHash, lightRig);
        Aim("overview");
        await CaptureLook(baseline, "ground-overview", "rig-reference-overview", null, sourceHash, variantHash, lightRig);
        for (int i = 1; i < looks.Length; i++)
        {
            var look = looks[i]; ApplyLook(look, localMaterial); terrainTop.MaterialOverride = localMaterial;
            Aim("patch"); await CaptureLook(look, "patch-foliage", look.Id + "-patch", localMaterial, sourceHash, variantHash, lightRig);
            Aim("overview"); await CaptureLook(look, "ground-overview", look.Id + "-overview", localMaterial, sourceHash, variantHash, lightRig);
        }
        ApplyLook(baseline, null); terrainTop.MaterialOverride = null;
        Aim("patch");
        string baselineRepeat = await CaptureLook(baseline, "patch-foliage", "baseline-repeat", null, sourceHash, variantHash, lightRig);
        if (baselineRepeat != baselinePatch) throw new InvalidOperationException("Original production look baseline was not pixel-identical on repeat");
        ApplyLook(baseline, localMaterial); terrainTop.MaterialOverride = localMaterial;
        Aim("patch");
        string zeroGate = await CaptureLook(baseline, "patch-foliage", "local-zero-change-gate", localMaterial, sourceHash, variantHash, lightRig);
        // Record shader-variant differences; this is visual exploration, not a
        // requirement that alternative implementations reproduce the old pixels.
        if (WorldSerializer.Digest(_world) != _digest) throw new InvalidOperationException("LookSweep mutated world state");
        int expectedCaptures = LookRefinement || SubstrateSweep ? 10 : 18;
        if (_captureCount != expectedCaptures || Engine.GetProcessFrames() - framesBefore > 7200) throw new InvalidOperationException("Look sweep exceeded its capture or time bound");
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new {
            schema_version = 1, experiment = SubstrateSweep ? "integrated-damp-substrate-tint-sweep" : LookRefinement ? "integrated-look-refinement" : "integrated-look-sweep", claim = SubstrateSweep ? "two bounded local damp-substrate pigment tint alternatives against a balanced untinted control, plus rig reference; all lighting, moss, and coverage controls held fixed; visual selection only" : LookRefinement ? "three stronger grounded relative-engine candidates plus the original rig reference across two held views; visual selection only, not calibrated photographic acceptance" : "eight reviewable relative-engine look directions across held foliage and ground cameras; choose a candidate by visual inspection, not a calibrated photographic claim",
            units = "relative_engine", source_shader = "res://Shaders/terrain.gdshader", source_shader_sha256 = sourceHash, local_shader_sha256 = variantHash,
            coverage_source_shader = "res://Shaders/coverage_mat.gdshader", coverage_source_shader_sha256 = _lookCoverageSourceHash, coverage_local_shader_sha256 = _lookCoverageVariantHash,
            config_file = "look-configs.json", private_shader_files = new[] { "terrain-look-sweep-local.gdshader", "coverage-look-sweep-local.gdshader" }, configs = lookRecords,
            private_coverage_material_overrides = new { shader = "res://Shaders/coverage_mat.gdshader", cloned_instance_overrides = coverageMaterialCount, source_materials_unchanged = true, colored_coverage_species_geometry_unchanged = true,
                originals = _lookCoverageMaterials.Select(x => new { node = x.node.Name.ToString(), source_shader = (x.original as ShaderMaterial)?.Shader?.ResourcePath, source_pattern = (x.original as ShaderMaterial)?.GetShaderParameter("u_pattern").AsSingle() }).ToArray() },
            capture_count = _captureCount, world_digest = _digest, world_digest_unchanged = true, player_save_touched = false, simulation_ticks = 0,
            baseline_repeat = new { baseline_sha256 = baselinePatch, repeat_sha256 = baselineRepeat, identical = baselinePatch == baselineRepeat },
            local_zero_gate = new { production_sha256 = baselinePatch, variant_sha256 = zeroGate, pixel_identical = baselinePatch == zeroGate },
            production_readiness = new { flora_ready = _flora.PopulationReady, moss_tiles = _coverage.TileCount, moss_instances = _coverage.InstanceCount, moss_pending = _coverage.PendingRebuildCount, frozen_review_time = 0, sway = 0, wind = 0, quality = _rig.Quality, private_fill = lightRig },
            fixed_camera_contract = new { patch = "existing foliage interaction view", overview = "fixed ground overview view", camera_attributes = "EnvironmentRig.CameraAttributes; exposure fixed per named config; auto exposure off" },
            preserved_stage = new { black_camera_surround = "unchanged from existing photographic fixture", environment_background = _rig.Env.BackgroundMode.ToString(), authored_reference = "rig light, ambient, exposure and AgX settings sampled before look edits; auto exposure is frozen for capture" },
            limits = new[] { "No configuration is promoted as photographic acceptance; the sweep offers distinct candidates for visual selection.", "Terrain moss and production coverage color adjustments use private local shader/material overrides; species, geometry, simulation and world data are unchanged. Coverage saturation/value controls do not recolor other coverage families outside coverage_mat.gdshader.", "The look board has no standing-water specimen; damp film strength is a local soil-layer diagnostic and terrain coverage does not identify dynamic surface-water pixels.", "Broad overview/foliage fixed views do not establish full habitat, motion, or performance acceptance.", "Color key choices are relative RGB art directions, not calibrated color temperatures or measured illumination." },
            captures = _captures
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplyLook(LookConfig look, ShaderMaterial? terrainVariant)
    {
        _rig.Env.TonemapExposure = look.Exposure; _rig.Env.TonemapAgxContrast = look.Contrast;
        _rig.Env.AdjustmentEnabled = true; _rig.Env.AdjustmentContrast = 1.07f; _rig.Env.AdjustmentSaturation = .97f; _rig.Env.AdjustmentBrightness = 1f;
        _rig.Sun.LightEnergy = look.SunEnergy; _rig.Sun.LightColor = look.SunColor; SetSunSourceDirection(look.SourceDirection);
        _rig.Env.AmbientLightEnergy = look.AmbientEnergy; _rig.Env.AmbientLightColor = look.AmbientColor;
        _rig.Env.AmbientLightSource = look.SkyAmbient ? Godot.Environment.AmbientSource.Sky : Godot.Environment.AmbientSource.Color;
        _rig.Env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        _rig.CameraAttributes.AutoExposureEnabled = false;
        foreach (var mat in FoliageMaterials())
        {
            mat.Shader = _originalShaders[mat.GetInstanceId()];
            mat.SetShaderParameter("tissue_use_explicit_material_f0", true); mat.SetShaderParameter("tissue_material_f0", look.F0);
            mat.SetShaderParameter("tissue_specular_stress", look.SpecularGain); mat.SetShaderParameter("tissue_direct_transmission", look.TransmissionGain);
            mat.SetShaderParameter("tissue_phase_scatter", look.PhaseGain); mat.SetShaderParameter("tissue_diffuse_wrap", 0f);
        }
        if (terrainVariant is not null)
        {
            terrainVariant.SetShaderParameter("photo_look_moss_scale", look.MossScale);
            terrainVariant.SetShaderParameter("photo_look_ground_moss_scale", look.GroundMossScale);
            terrainVariant.SetShaderParameter("photo_look_moss_tint", new Vector3(look.MossTint.R, look.MossTint.G, look.MossTint.B));
            var dampTint = look.DampTint ?? Colors.White;
            terrainVariant.SetShaderParameter("photo_look_damp_tint", new Vector3(dampTint.R, dampTint.G, dampTint.B));
            terrainVariant.SetShaderParameter("photo_look_wet_film", look.Film);
            terrainVariant.SetShaderParameter("photo_look_wet_roughness", look.FilmRoughness);
        }
        foreach (var entry in _lookCoverageMaterials)
        {
            entry.node.MaterialOverride = terrainVariant is null ? entry.original : entry.local;
            if (terrainVariant is not null)
            {
                entry.local.SetShaderParameter("photo_look_coverage_saturation", look.CoverageSaturation);
                entry.local.SetShaderParameter("photo_look_coverage_value", look.CoverageValue);
            }
        }
    }

    private async Task<string> CaptureLook(LookConfig look, string view, string name, ShaderMaterial? localTerrain, string sourceHash, string variantHash, List<object> lightRig)
    {
        if (++_captureCount > 18) throw new InvalidOperationException("LookSweep hard capture limit 18 exceeded");
        const int settle = 8; _fixedFrames += settle; await Frames(settle); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage(); var file = name + ".png"; var full = Path.Combine(OutDir, file);
        if (image.SavePng(full) != Error.Ok) throw new IOException("Failed to save " + full);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))).ToLowerInvariant();
        _captures.Add(new { file, sha256 = hash, name, view, config = LookRecord(look),
            actual = new { exposure = _rig.Env.TonemapExposure, agx_contrast = _rig.Env.TonemapAgxContrast, sun_energy = _rig.Sun.LightEnergy, sun_color = _rig.Sun.LightColor.ToHtml(), source_direction = _rig.Sun.GlobalBasis.Z.Normalized().ToString(), ambient_energy = _rig.Env.AmbientLightEnergy, ambient_color = _rig.Env.AmbientLightColor.ToHtml(), ambient_source = _rig.Env.AmbientLightSource.ToString(), auto_exposure = _rig.CameraAttributes.AutoExposureEnabled, shadows = _rig.Sun.ShadowEnabled,
                foliage = ActualFoliageControls(), terrain = new { local_variant_active = localTerrain is not null, local_shader_sha256 = localTerrain is not null ? variantHash : null, moss_scale = localTerrain?.GetShaderParameter("photo_look_moss_scale").AsSingle(), ground_moss_scale = localTerrain?.GetShaderParameter("photo_look_ground_moss_scale").AsSingle(), moss_tint = localTerrain is null ? null : localTerrain.GetShaderParameter("photo_look_moss_tint").AsVector3().ToString(), damp_tint = localTerrain is null ? null : localTerrain.GetShaderParameter("photo_look_damp_tint").AsVector3().ToString(), wet_film = localTerrain?.GetShaderParameter("photo_look_wet_film").AsSingle(), wet_roughness = localTerrain?.GetShaderParameter("photo_look_wet_roughness").AsSingle(), production_moisture_absorption_unchanged = true },
                coverage = _lookCoverageMaterials.Select(x => new { node = x.node.Name.ToString(), active = x.node.MaterialOverride == x.local, source_shader = (x.original as ShaderMaterial)?.Shader?.ResourcePath, source_pattern = (x.original as ShaderMaterial)?.GetShaderParameter("u_pattern").AsSingle(), shader = x.local.Shader?.ResourceName, source_shader_sha256 = _lookCoverageSourceHash, local_shader_sha256 = _lookCoverageVariantHash, saturation = x.node.MaterialOverride == x.local ? x.local.GetShaderParameter("photo_look_coverage_saturation").AsSingle() : (float?)null, value = x.node.MaterialOverride == x.local ? x.local.GetShaderParameter("photo_look_coverage_value").AsSingle() : (float?)null }).ToArray() },
            camera = new { position = _camera.Position.ToString(), rotation = _camera.Rotation.ToString(), target = view == "patch-foliage" ? "existing patch foliage target" : "fixed ground overview target", fov = _camera.Fov },
            environment = new { tonemap = _rig.Env.TonemapMode.ToString(), adjustment_contrast = _rig.Env.AdjustmentContrast, ssil = _rig.Env.SsilEnabled, ssao = _rig.Env.SsaoEnabled, reflection_source = _rig.Env.ReflectedLightSource.ToString() },
            source_shader_sha256 = sourceHash, world_digest = WorldSerializer.Digest(_world), readiness_frames = settle, review_time = 0, private_fill = lightRig });
        GD.Print("PHOTO_LOOK_CAPTURE " + name + " sha256=" + hash);
        return hash;
    }

    private Shader BuildCoverageLookVariant()
    {
        const string path = "res://Shaders/coverage_mat.gdshader";
        var source = GD.Load<Shader>(path) ?? throw new InvalidOperationException("Missing production coverage material shader");
        const string baseAnchor = "    vec3 base = srgb_to_linear(COLOR.rgb);";
        string code = source.Code;
        if (Count(code, baseAnchor) != 1) throw new InvalidOperationException("Coverage color anchor changed; refusing local saturation/value variant");
        string uniforms = "\nuniform float photo_look_coverage_saturation = 1.0;\nuniform float photo_look_coverage_value = 1.0;\n";
        code = code.Replace("shader_type spatial;", "shader_type spatial;" + uniforms, StringComparison.Ordinal);
        string replacement = baseAnchor + "\n    base = mix(vec3(dot(base, vec3(0.2126, 0.7152, 0.0722))), base, photo_look_coverage_saturation) * photo_look_coverage_value;";
        code = code.Replace(baseAnchor, replacement, StringComparison.Ordinal);
        if (Count(code, "uniform float photo_look_coverage_saturation") != 1 || Count(code, "base = mix(vec3(dot(base") != 1)
            throw new InvalidOperationException("Coverage local variant insertion count was not exactly one");
        return new Shader { Code = code, ResourceName = "coverage_mat.photo-look-sweep-local" };
    }

    private static object LookRecord(LookConfig l) => new {
        id = l.Id, exposure = l.Exposure, agx_contrast = l.Contrast, sun_energy = l.SunEnergy, ambient_energy = l.AmbientEnergy,
        sun_color_rgb = new[] { l.SunColor.R, l.SunColor.G, l.SunColor.B }, ambient_color_rgb = new[] { l.AmbientColor.R, l.AmbientColor.G, l.AmbientColor.B },
        source_direction_xyz = new[] { l.SourceDirection.X, l.SourceDirection.Y, l.SourceDirection.Z },
        f0 = l.F0, specular_gain = l.SpecularGain, direct_transmission = l.TransmissionGain, phase_gain = l.PhaseGain,
        terrain_moss_scale = l.MossScale, procedural_ground_moss_scale = l.GroundMossScale, terrain_moss_tint_rgb = new[] { l.MossTint.R, l.MossTint.G, l.MossTint.B }, damp_substrate_tint_rgb = l.DampTint is { } tint ? new[] { tint.R, tint.G, tint.B } : new[] { 1f, 1f, 1f }, wet_film = l.Film, wet_film_roughness = l.FilmRoughness,
        coverage_saturation = l.CoverageSaturation, coverage_value = l.CoverageValue, ambient_source = l.SkyAmbient ? "sky" : "color"
    };

    private int CloneCoverageMaterials(Shader localShader)
    {
        void Visit(Node node)
        {
            if (node is MeshInstance3D instance && instance.MaterialOverride is ShaderMaterial original && original.Shader?.ResourcePath == "res://Shaders/coverage_mat.gdshader")
            {
                var clone = original.Duplicate(true) as ShaderMaterial ?? throw new InvalidOperationException("Could not deep-duplicate production coverage material");
                clone.Shader = localShader;
                clone.SetShaderParameter("photo_look_coverage_saturation", 1f);
                clone.SetShaderParameter("photo_look_coverage_value", 1f);
                _lookCoverageMaterials.Add((instance, instance.MaterialOverride, clone));
            }
            foreach (Node child in node.GetChildren()) Visit(child);
        }
        Visit(_coverage);
        return _lookCoverageMaterials.Count;
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
