using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

/// <summary>Native integrated loam-and-litter material review. Coordinator owns native CLI dispatch.</summary>
public partial class LoamGroundReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";

    private sealed record Candidate(string Id, float Dampness, float Roughness, float Relief, float Density, int Stage);
    private static readonly Candidate[] Candidates =
    {
        new("current-ground", .90f, .74f, .006f, 1.45f, 0),
        new("decomposed-litter", .90f, .74f, .006f, 1.45f, 1),
        new("structured-loam", .90f, .74f, .006f, 1.45f, 2),
    };
    private static readonly string[] Views = { "macro", "medium", "habitat" };
    private readonly List<object> _captures = new();
    private EnvironmentRig _rig = null!;
    private Camera3D _camera = null!;
    private VivariumWorld _world = null!;
    private IslandRenderer _island = null!;
    private MeshInstance3D _terrainTop = null!;
    private ShaderMaterial _loamMaterial = null!;
    private Node3D? _litter;
    private Node3D? _aggregates;
    private FloraRenderer? _flora;
    private string _worldDigest = "";
    private string _loamShaderHash = "";
    private string _litterBuilderHash = "";
    private string _reviewSourceHash = "";
    private int _captureCount;
    private int _fixedFrames;
    private int _floraReadyFrames;

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private async Task Frames(int count)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try
        {
            await Run();
            GD.Print("LOAM_GROUND_REVIEW_OK " + OutDir);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr("LOAM_GROUND_REVIEW_FAILED " + ex);
            GetTree().Quit(2);
        }
    }

    private async Task Run()
    {
        Require(Content is not null, "Content library was not provided");
        Require(!string.IsNullOrWhiteSpace(OutDir), "OutDir is required");
        OutDir = Path.GetFullPath(OutDir);
        if (Directory.Exists(OutDir) && Directory.GetFileSystemEntries(OutDir).Length != 0)
            throw new IOException("Output directory is nonempty; refusing overwrite: " + OutDir);
        Directory.CreateDirectory(OutDir);
        GetWindow().Size = new Vector2I(1600, 900);

        _rig = new EnvironmentRig { BenchmarkLightSeconds = 60 };
        AddChild(_rig);
        await Frames(2);
        _rig.SetProcess(false);
        _rig.ApplyQuality(2);
        DisablePrivateLights(_rig);
        _rig.CameraAttributes.AutoExposureEnabled = false;
        _rig.Env.TonemapMode = Godot.Environment.ToneMapper.Agx;
        _rig.Env.TonemapExposure = 1.05f;
        _rig.Env.TonemapAgxContrast = 1.30f;
        _rig.Env.AdjustmentEnabled = true;
        _rig.Env.AdjustmentContrast = 1.07f;
        _rig.Env.AdjustmentSaturation = .97f;
        _rig.Env.AdjustmentBrightness = 1f;
        _rig.Env.FogEnabled = false;
        _rig.Env.VolumetricFogEnabled = false;
        // The canopy shader outputs black for the camera ray and retains radiance in its cubemap pass.
        _rig.Env.BackgroundMode = Godot.Environment.BGMode.Sky;
        _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        _rig.Env.AmbientLightColor = new Color(.80f, .84f, .89f);
        _rig.Env.AmbientLightEnergy = .60f;
        _rig.Env.AmbientLightSkyContribution = 0f;
        _rig.Env.ReflectedLightSource = Godot.Environment.ReflectionSource.Sky;
        // Match the selected balanced look. These are render-space values, not soil calibration.
        _rig.Env.SsilEnabled = true;
        _rig.Env.SsilRadius = .75f;
        _rig.Env.SsilIntensity = .60f;
        _rig.Env.SsaoEnabled = true;
        _rig.Env.SsaoRadius = .12f;
        _rig.Env.SsaoIntensity = 1.15f;
        _rig.Sun.LightEnergy = 1.25f;
        _rig.Sun.LightColor = new Color(1f, .96f, .90f);
        _rig.Sun.ShadowEnabled = true;
        var lightSource = new Vector3(-.48f, .66f, .57f).Normalized();
        _rig.Sun.Basis = Basis.LookingAt(-lightSource, Vector3.Up);
        if (_rig.Sun.GlobalBasis.Z.Normalized().Dot(lightSource) < .999f)
            throw new InvalidOperationException("Sun direction did not bind requested loam review vector");

        _camera = new Camera3D { Current = true, Fov = 39f, Near = .01f, Far = 30f, Attributes = _rig.CameraAttributes };
        AddChild(_camera);

        var desc = Content.PresetOrThrow("default");
        desc.Diameter = 5;
        desc.CellSize = .1;
        desc.PilotTreeId = "none";
        desc.Placement.Rocks = 0;
        desc.Placement.Logs = 0;
        desc.Placement.GravelPatches = 0;
        desc.Water.Springs.Clear();
        desc.Water.WaterTable = -.4;
        _world = VivariumWorld.Create(Content, desc, false, false);
        _world.Clock.Paused = true;
        var terrain = _world.Terrain;
        terrain.BeginEdit();
        for (int z = 0; z < terrain.Nz; z++)
        for (int x = 0; x < terrain.Nx; x++)
        {
            var p = terrain.VertexPos(x, z);
            terrain.H[z * terrain.Nx + x] = .018 * p.X + .012 * p.Z * p.Z + FineHeight(p.X, p.Z);
        }
        terrain.Touch();
        _world.Water.RefreshBed(terrain);
        Array.Clear(_world.AmbientGroundCover.Cover);
        Array.Clear(_world.Litter.FineMass);
        foreach (int cell in _world.Grid.DomainCells)
        {
            _world.Fields.BaseSubstrate.Values[cell] = (byte)Substrate.Soil;
            _world.Fields.Moisture.Values[cell] = .62;
        }

        _island = new IslandRenderer { Name = "LoamReviewIsland" };
        AddChild(_island);
        _island.Build(_world);
        _island.SetProcess(false);
        _terrainTop = _island.FindChild("TerrainTop", true, false) as MeshInstance3D
            ?? throw new InvalidOperationException("Production IslandRenderer TerrainTop was not found");
        _loamMaterial = CreateLoamMaterial();
        _terrainTop.MaterialOverride = _loamMaterial;

        // Mature production flora gives the habitat view a scale cue. No coverage or ambient-cover renderer is built.
        foreach (var item in new[] { ("shadebell", -.80, -1.40), ("lanternbrush", .78, -1.45), ("ringreed", .05, -1.80) })
        {
            var species = Content.FloraOrThrow(item.Item1);
            var plant = _world.FloraSystem.Establish(species, new Vec2(item.Item2, item.Item3), "loam-ground-review-v1", species.MaxBiomass);
            plant.Age = species.MaturityAge + 1;
        }
        _flora = new FloraRenderer { Camera = _camera, Quality = 1 };
        AddChild(_flora);
        _flora.Build(_world);
        for (int i = 0; i < 2400 && !_flora.PopulationReady; i++) { await Frames(1); _floraReadyFrames++; }
        if (!_flora.PopulationReady) throw new TimeoutException("Production flora renderer did not become ready");
        _flora.SetProcess(false);

        _worldDigest = WorldSerializer.Digest(_world);
        _loamShaderHash = HashFile(ProjectSettings.GlobalizePath("res://Shaders/loam_ground.gdshader"));
        _litterBuilderHash = HashFile(ProjectSettings.GlobalizePath("res://App/LoamLitterBuilder.cs"));
        _reviewSourceHash = HashFile(ProjectSettings.GlobalizePath("res://App/LoamGroundReview.cs"));
        var framesBefore = Engine.GetProcessFrames();
        foreach (var candidate in Candidates)
        {
            ApplyCandidate(candidate);
            ReplaceLitter(candidate);
            await Frames(2);
            foreach (string view in Views)
            {
                Aim(view);
                await Capture(candidate, view);
            }
        }

        var repeat = Candidates[1];
        ApplyCandidate(repeat);
        ReplaceLitter(repeat);
        Aim("macro");
        await Capture(repeat, "baseline-repeat");
        Require(HashFile(Path.Combine(OutDir, repeat.Id + "-macro.png")) ==
            HashFile(Path.Combine(OutDir, repeat.Id + "-baseline-repeat.png")), "Repeated seeded litter image changed");
        ValidateProductionDetails();

        if (WorldSerializer.Digest(_world) != _worldDigest)
            throw new InvalidOperationException("World digest changed during render-only loam capture");
        if (HashFile(ProjectSettings.GlobalizePath("res://Shaders/loam_ground.gdshader")) != _loamShaderHash ||
            HashFile(ProjectSettings.GlobalizePath("res://App/LoamLitterBuilder.cs")) != _litterBuilderHash ||
            HashFile(ProjectSettings.GlobalizePath("res://App/LoamGroundReview.cs")) != _reviewSourceHash)
            throw new InvalidOperationException("Loam review source changed while captures were running");
        if (_captureCount != 10 || Engine.GetProcessFrames() - framesBefore > 7200)
            throw new InvalidOperationException("Loam capture schedule exceeded 10 captures or frame safety bound");

        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new
        {
            schema_version = 1,
            fixture = "integrated-production-island-with-fresh-loam-and-separate-procedural-litter",
            claim = "comparative native substrate look diagnostic; not absolute soil calibration or broad photographic acceptance",
            world_digest = _worldDigest,
            world_digest_unchanged = true,
            capture_count = _captureCount,
            readiness_frames = _fixedFrames,
            flora_readiness_frames = _floraReadyFrames,
            source_stable = true,
            source_hashes = new { loam_ground_shader_sha256 = _loamShaderHash, loam_litter_builder_sha256 = _litterBuilderHash, review_worker_sha256 = _reviewSourceHash },
            profiles = Candidates.Select(c => new { id = c.Id, dampness = c.Dampness, roughness = c.Roughness, clump_relief_m = c.Relief, litter_density = c.Density, seed = 17 }).ToArray(),
            lighting = new { exposure = _rig.Env.TonemapExposure, agx_contrast = _rig.Env.TonemapAgxContrast, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled,
                sun_energy = _rig.Sun.LightEnergy, sun_color = _rig.Sun.LightColor.ToHtml(), source_direction = _rig.Sun.GlobalBasis.Z.Normalized().ToString(),
                ambient_energy = _rig.Env.AmbientLightEnergy, ambient_color = _rig.Env.AmbientLightColor.ToHtml(), ambient_source = _rig.Env.AmbientLightSource.ToString(),
                background_mode = _rig.Env.BackgroundMode.ToString(), black_camera_background = "canopy_sky.gdshader camera branch outputs black; cubemap pass keeps canopy radiance", private_lights_disabled = true,
                adjustment_enabled = _rig.Env.AdjustmentEnabled, adjustment_contrast = _rig.Env.AdjustmentContrast, adjustment_saturation = _rig.Env.AdjustmentSaturation,
                ssil_enabled = _rig.Env.SsilEnabled, ssil_radius = _rig.Env.SsilRadius, ssil_intensity = _rig.Env.SsilIntensity,
                ssao_enabled = _rig.Env.SsaoEnabled, ssao_radius = _rig.Env.SsaoRadius, ssao_intensity = _rig.Env.SsaoIntensity,
                volumetric_fog = _rig.Env.VolumetricFogEnabled },
            terrain = new { renderer = "production IslandRenderer heightfield and strata", top_material_override = _loamMaterial.Shader?.ResourcePath,
                ground_surface_binding = Bridge.Surfaces.Soil, old_substrate_weight_mixer = false, field_texture = "local constant moisture .62", coverage_renderer_built = false, ambient_ground_cover_renderer_built = false,
                terrain_profile = "5m board with gentle rise and deterministic 0.0034m+0.0018m fine heightfield relief; all domain cells set to soil for leaf-litter eligibility",
                cell_size_m = _world.Grid.CellSize },
            habitat = new { flora_renderer = _flora.PopulationReady, flora_species = new[] { "shadebell", "lanternbrush", "ringreed" }, mature_fixture_plants = _world.Flora.Count,
                growth_steps = 0, world_clock_paused = _world.Clock.Paused, player_save_touched = false },
            limitations = new[] { "Fixed macro/medium/habitat views do not establish moving-view stability or production promotion.", "Substrate appearance is relative to the supplied render and camera configuration, not measured field soil reflectance." },
            captures = _captures
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private ShaderMaterial CreateLoamMaterial()
    {
        var shader = GD.Load<Shader>("res://Shaders/loam_ground.gdshader") ?? throw new InvalidOperationException("Missing fresh loam_ground.gdshader");
        var material = new ShaderMaterial { Shader = shader };
        Bridge.BindSurface(material, "soil", Bridge.Surfaces.Soil);
        var grid = _world.Grid;
        material.SetShaderParameter("field_rect", new Vector4((float)grid.OriginX, (float)grid.OriginZ, (float)(grid.Nx * grid.CellSize), (float)(grid.Nz * grid.CellSize)));
        using var image = Image.CreateEmpty(1, 1, false, Image.Format.R8);
        image.Fill(new Color(.62f, 0f, 0f, 1f));
        var fieldTexture = ImageTexture.CreateFromImage(image);
        material.SetShaderParameter("field_tex", fieldTexture);
        material.SetShaderParameter("review_time", -1f);
        material.SetShaderParameter("loam_seed", 17f);
        // Litter vertices use the authoritative heightfield. Leave shader-only vertex lift off so contacts match.
        material.SetShaderParameter("loam_vertex_relief_strength", 0f);
        return material;
    }

    private void ValidateProductionDetails()
    {
        string before = WorldSerializer.Digest(_world);
        // The fixture starts without stored litter: the production path must not invent organic mass.
        var empty = LoamLitterBuilder.Build(_world, 17, respectLitterMass: true);
        Require(empty.GetMeta("leaf_count").AsInt32() == 0 && empty.GetMeta("duff_count").AsInt32() == 0
            && empty.GetMeta("twig_count").AsInt32() == 0, "Zero stored litter created organic geometry");
        empty.Free();
        var detail = new LoamSurfaceDetailRenderer { Camera = _camera, Quality = 2 };
        AddChild(detail);
        detail.SetProcess(false);
        detail.Build(_world);
        for (int i = 0; i < 10; i++) detail._Process(.11);
        Require(detail.PatchCount == 9, "Production patch window failed to settle");
        int initialRebuilds = detail.RebuildCount;
        var savedFine = (double[])_world.Litter.FineMass.Clone();
        try
        {
            foreach (int cell in _world.Grid.DomainCells) _world.Litter.FineMass[cell] = .001;
            for (int i = 0; i < 10; i++) detail._Process(.11);
            Require(detail.RebuildCount > initialRebuilds && CountMeta(detail, "leaf_count") > 0,
                "Small positive litter deposit did not refresh organic geometry");
            ValidateUploadedDetailMeshes(detail);
        }
        finally { Array.Copy(savedFine, _world.Litter.FineMass, savedFine.Length); }
        for (int i = 0; i < 10; i++) detail._Process(.11);
        Require(CountMeta(detail, "leaf_count") == 0 && CountMeta(detail, "duff_count") == 0,
            "Removed litter retained organic geometry");
        Vector3 saved = _camera.Position;
        _camera.Position += new Vector3(5, 0, 0);
        for (int i = 0; i < 10; i++) detail._Process(.11);
        Require(detail.PatchCount <= 9, "Production patch window grew beyond its cap");
        _camera.Position = saved;
        for (int i = 0; i < 10; i++) detail._Process(.11);
        detail.Quality = 0;
        detail._Process(.11);
        Require(!detail.Visible, "Lowest quality failed to disable loam details");
        detail.Quality = 2;
        detail._Process(.11);
        Require(detail.Visible && detail.PatchCount <= 9, "Restored quality failed to restore bounded details");
        int rebuilt = detail.RebuildCount;
        RemoveChild(detail);
        detail.Free();
        Require(WorldSerializer.Digest(_world) == before, "Production detail rendering changed saved simulation state");
        File.WriteAllText(Path.Combine(OutDir, "production-details.json"), JsonSerializer.Serialize(new
        { zero_litter_empty = true, small_deposit_refresh = true, removed_litter_empty = true, clockwise_finite_geometry = true,
            camera_window_bounded = true, quality_toggle = true,
            patch_cap = 9, rebuilt_patches = rebuilt, world_unchanged = true }));
    }

    private static int CountMeta(Node node, string key)
    {
        int result = node.HasMeta(key) ? node.GetMeta(key).AsInt32() : 0;
        foreach (var child in node.GetChildren()) result += CountMeta(child, key);
        return result;
    }

    private static void ValidateUploadedDetailMeshes(Node node)
    {
        if (node is MeshInstance3D instance && instance.Mesh is { } mesh)
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Require(vertices.Length == normals.Length && vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z)),
                "Uploaded loam mesh has invalid vertex/normal arrays");
            var indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array
                ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array() : Enumerable.Range(0, vertices.Length).ToArray();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Vector3 cross = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
                Require(cross.LengthSquared() > 1e-22 && cross.Dot(normals[a] + normals[b] + normals[c]) <= 1e-12,
                    "Uploaded loam triangle is degenerate or has reversed front-face winding");
            }
        }
        foreach (var child in node.GetChildren()) ValidateUploadedDetailMeshes(child);
    }

    private void ApplyCandidate(Candidate c)
    {
        _loamMaterial.SetShaderParameter("loam_dampness", c.Dampness);
        _loamMaterial.SetShaderParameter("loam_roughness", c.Roughness);
        _loamMaterial.SetShaderParameter("loam_clump_relief", c.Relief);
        _loamMaterial.SetShaderParameter("loam_seed", 17f);
    }

    private void ReplaceLitter(Candidate c)
    {
        if (_litter is not null)
        {
            RemoveChild(_litter);
            _litter.Free();
            _litter = null;
        }
        if (_aggregates is not null) { RemoveChild(_aggregates); _aggregates.Free(); _aggregates = null; }
        _litter = LoamLitterBuilder.Build(_world, 17, c.Density, c.Stage);
        AddChild(_litter);
        if (c.Stage >= 2) { _aggregates = LoamAggregateBuilder.Build(_world, 17); AddChild(_aggregates); }
    }

    private void Aim(string view)
    {
        Vector3 target;
        Vector3 position;
        switch (view)
        {
            case "macro":
                target = new Vector3(.70f, .04f, .55f);
                position = target + new Vector3(.36f, .50f, .36f);
                _camera.Fov = 38f;
                break;
            case "medium":
                target = new Vector3(0f, .28f, -.15f);
                position = new Vector3(0f, 1.65f, 3.25f);
                _camera.Fov = 43f;
                break;
            case "habitat":
                target = new Vector3(0f, .30f, -.40f);
                position = new Vector3(2.5f, 1.6f, 3.5f);
                _camera.Fov = 45f;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(view));
        }
        _camera.Position = position;
        _camera.LookAt(target, Vector3.Up);
    }

    private async Task Capture(Candidate c, string view)
    {
        if (++_captureCount > 10) throw new InvalidOperationException("Loam review hard capture limit 10 exceeded");
        const int settle = 8;
        _fixedFrames += settle;
        await Frames(settle);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var filename = c.Id + "-" + view + ".png";
        var fullPath = Path.Combine(OutDir, filename);
        if (image.SavePng(fullPath) != Error.Ok) throw new IOException("Could not save " + fullPath);
        string hash = HashFile(fullPath);
        _captures.Add(new
        {
            file = filename,
            sha256 = hash,
            profile = new { id = c.Id, dampness = c.Dampness, roughness = c.Roughness, clump_relief_m = c.Relief, litter_density = c.Density, seed = 17 },
            camera = new { name = view, position = _camera.Position.ToString(), rotation = _camera.Rotation.ToString(), fov = _camera.Fov,
                macro_target_world_m = view == "macro" ? "x=.70,y=0.04,z=.55; approximate camera-target distance 0.71m; camera is about 0.52m above the ground; cm-scale litter visible" : null },
            lighting_actual = new { exposure = _rig.Env.TonemapExposure, agx_contrast = _rig.Env.TonemapAgxContrast, auto_exposure = _rig.CameraAttributes.AutoExposureEnabled,
                sun_energy = _rig.Sun.LightEnergy, sun_color = _rig.Sun.LightColor.ToHtml(), sun_direction = _rig.Sun.GlobalBasis.Z.Normalized().ToString(),
                ambient_energy = _rig.Env.AmbientLightEnergy, ambient_color = _rig.Env.AmbientLightColor.ToHtml(), ambient_source = _rig.Env.AmbientLightSource.ToString(),
                background_mode = _rig.Env.BackgroundMode.ToString(), adjustment_enabled = _rig.Env.AdjustmentEnabled,
                adjustment_contrast = _rig.Env.AdjustmentContrast, adjustment_saturation = _rig.Env.AdjustmentSaturation,
                ssil_enabled = _rig.Env.SsilEnabled, ssil_radius = _rig.Env.SsilRadius, ssil_intensity = _rig.Env.SsilIntensity,
                ssao_enabled = _rig.Env.SsaoEnabled, ssao_radius = _rig.Env.SsaoRadius, ssao_intensity = _rig.Env.SsaoIntensity,
                volumetric_fog = _rig.Env.VolumetricFogEnabled },
            terrain_actual = new { shader = _loamMaterial.Shader?.ResourcePath, loam_dampness = _loamMaterial.GetShaderParameter("loam_dampness").AsSingle(),
                loam_roughness = _loamMaterial.GetShaderParameter("loam_roughness").AsSingle(), loam_clump_relief = _loamMaterial.GetShaderParameter("loam_clump_relief").AsSingle(),
                loam_seed = _loamMaterial.GetShaderParameter("loam_seed").AsSingle(), loam_texture_tile_metres = _loamMaterial.GetShaderParameter("loam_texture_tile_metres").AsSingle(),
                loam_vertex_relief_strength = _loamMaterial.GetShaderParameter("loam_vertex_relief_strength").AsSingle(),
                field_rect = _loamMaterial.GetShaderParameter("field_rect").AsVector4().ToString(), field_moisture = .62f,
                soil_texture = Bridge.Surfaces.Soil, coverage_renderer_built = false, ambient_cover_renderer_built = false },
            litter_actual = DescribeLitter(),
            world_digest = WorldSerializer.Digest(_world),
            readiness_frames = settle,
            review_time = 0,
            world_clock_paused = _world.Clock.Paused
        });
        GD.Print($"LOAM_GROUND_CAPTURE {Path.GetFileName(fullPath)} sha256={hash}");
    }

    private object DescribeLitter()
    {
        if (_litter is null) return new { node = (string?)null, seed = 0, density = 0f, board_size_m = 0f, leaf_count = 0, twig_count = 0, child_meshes = Array.Empty<object>() };
        var details = new List<object>();
        int vertices = 0, leafNodes = 0, twigNodes = 0;
        foreach (var child in _litter.GetChildren())
        {
            if (child is not MeshInstance3D mesh || mesh.Mesh is null) continue;
            if (mesh.Name == "CurledLeafLitter") leafNodes++;
            if (mesh.Name == "FineTwigs") twigNodes++;
            int faces = mesh.Mesh.GetFaces().Length / 3;
            vertices += faces * 3;
            details.Add(new { name = mesh.Name.ToString(), face_count = faces, surface_count = mesh.Mesh.GetSurfaceCount(), visible = mesh.Visible,
                material_shader = (mesh.Mesh.SurfaceGetMaterial(0) as ShaderMaterial)?.Shader?.ResourcePath });
        }
        return new { node = _litter.Name.ToString(), seed = _litter.GetMeta("seed").AsInt32(), density = _litter.GetMeta("density").AsSingle(),
            board_size_m = _litter.GetMeta("board_size_m").AsSingle(), leaf_count = _litter.GetMeta("leaf_count").AsInt32(), twig_count = _litter.GetMeta("twig_count").AsInt32(),
            leaf_meshes = leafNodes, twig_meshes = twigNodes, triangle_vertices = vertices, child_meshes = details };
    }

    private void DisablePrivateLights(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Light3D light && light != _rig.Sun) light.LightEnergy = 0f;
            DisablePrivateLights(child);
        }
    }

    private static string HashFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Source hash input missing", path);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    private static double FineHeight(double x, double z)
    {
        // Short natural relief survives the 7 cm terrain mesh and is shared by litter placement/contact.
        return .0034 * Math.Sin(x * 19.0 + z * 11.0) * Math.Cos(z * 23.0 - x * 7.0)
             + .0018 * Math.Sin(x * 31.0 - z * 17.0);
    }
}
