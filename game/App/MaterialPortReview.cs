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
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Matched material captures: production geometry/bindings, fixed exposure and phase,
/// and baseline/candidate shaders on the same material. Never opens or writes player saves.</summary>
public partial class MaterialPortReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public string BaselineDir { get; set; } = "";
    public bool GeometryOnly { get; set; }
    public bool RenderDocCapture { get; set; }
    public bool MotionSequence { get; set; }
    private bool _renderDocCompleted;
    private readonly List<object> _receipts = new();
    private EnvironmentRig _rig = null!;
    private Camera3D _camera = null!;
    public override void _Ready() => _ = Run();

    private Shader Baseline(string name) => new() { Code = File.ReadAllText(Path.Combine(BaselineDir, name)) };
    private async Task Frames(int count = 16)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name, int settleFrames = 16)
    {
        await Frames(settleFrames);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(Path.GetFullPath(Path.Combine(OutDir, name + ".png")));
        if (error != Error.Ok) throw new IOException($"Capture {name}: {error}");
        GD.Print("MATERIAL_CAPTURE " + name);
        if (RenderDocCapture && !_renderDocCompleted && name.StartsWith("integrated-day"))
        {
            var diagnostic = RenderDocDiagnostic.AttachToLaunchedProcess();
            uint previous = diagnostic.CompletedCaptureCount;
            diagnostic.CaptureNextPresentedFrame();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (diagnostic.CompletedCaptureCount <= previous && timer.Elapsed.TotalSeconds < 30) await Frames(1);
            if (diagnostic.CompletedCaptureCount <= previous) throw new TimeoutException("RenderDoc frame capture did not complete");
            _renderDocCompleted = true;
            GD.Print("RENDERDOC_CAPTURE_OK frame=" + name + " count=" + diagnostic.CompletedCaptureCount);
        }
    }
    private async Task Pair(string name, ShaderMaterial material, Shader baseline, Shader candidate)
    {
        if (GeometryOnly) { material.Shader = candidate; await Capture(name); }
        else {
            material.Shader = baseline; await Capture(name + "-before");
            material.Shader = candidate; await Capture(name + "-after");
        }
        _receipts.Add(new { name, eye = _camera.Position.ToString(), targetBasis = _camera.Basis.ToString(),
            exposure = _rig.Env.TonemapExposure, light = _rig.Sun.LightEnergy });
    }
    private void Lighting(bool night = false, bool backlit = false)
    {
        // Drive the production fill/sky/night envelope too; dimming only the key
        // would leave a daytime fill in the night comparison.
        _rig.BenchmarkLightSeconds = night ? 1020 : 450;
        _rig._Process(0);
        _rig.Env.FogEnabled = false;
        _rig.Env.AmbientLightColor = new Color(.72f, .79f, .85f);
        _rig.Sun.RotationDegrees = new Vector3(-38, backlit ? 135 : -45, 0);
        _rig.Sun.LightEnergy = night ? .035f : 1.15f;
        _rig.Env.AmbientLightEnergy = night ? .025f : .45f;
    }
    private async Task Run()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            GetWindow().Size = new Vector2I(1600, 900);
            _rig = new EnvironmentRig { BenchmarkLightSeconds = 450 }; AddChild(_rig);
            await Frames(2); _rig.SetProcess(false); _rig.ApplyQuality(2);
            _rig.CameraAttributes.AutoExposureEnabled = false;
            _rig.Env.TonemapExposure = 1.12f; _rig.Env.FogEnabled = false;
            _rig.Env.BackgroundMode = Godot.Environment.BGMode.Color;
            _rig.Env.BackgroundColor = new Color(.055f, .065f, .06f);
            _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            _rig.Env.AmbientLightColor = new Color(.72f, .79f, .85f);
            _camera = new Camera3D { Current = true, Fov = 42, Near = .01f, Far = 40 }; AddChild(_camera);
            var oldLeaf = Baseline("leaf.gdshader"); var newLeaf = GD.Load<Shader>("res://Shaders/leaf.gdshader");
            foreach (string id in new[] { "shadebell", "lanternbrush", "embercrown" })
            {
                Lighting();
                var sp = Content.FloraOrThrow(id);
                var profile = FloraVisualProfile.Load(id) ?? throw new InvalidOperationException("No profile for " + id);
                var stem = Bridge.Shader("res://Shaders/flora.gdshader");
                stem.SetShaderParameter("surface_mode", 2); stem.SetShaderParameter("sway", 0f);
                FloraSurfaceProfiles.Bind(stem, sp, photographedLeaves: false);
                var leaf = profile.LeafMaterial(animated: true);
                leaf.SetShaderParameter("review_time", 0f); leaf.SetShaderParameter("plant_stiffness", sp.Woody != null ? 7f : 3f);
                NightFloraMaterials.Bind(leaf, sp);
                ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + id), 0x9E3779B97F4A7C15UL);
                var source = OrganismMeshes.FloraTier(sp, seed, 0, tieredSource: true);
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true, UseCustomData = true, Mesh = profile.Compile(source, stem, leaf), InstanceCount = 1 };
                mm.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(new Vector3((float)sp.RadiusAtMax,
                    (float)sp.Height, (float)sp.RadiusAtMax)), Vector3.Zero));
                mm.SetInstanceColor(0, Colors.White); mm.SetInstanceCustomData(0, new Color(.5f, 1f, 0f, .5f));
                var specimen = new MultiMeshInstance3D { Multimesh = mm }; AddChild(specimen);
                var bounds = specimen.GetAabb(); var target = bounds.GetCenter();
                float size = Math.Max(.2f, Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)));
                _camera.Position = target + new Vector3(.85f, .55f, 1).Normalized() * size * 1.8f; _camera.LookAt(target);
                await Pair(id + "-front", leaf, oldLeaf, newLeaf);
                Lighting(backlit: true); await Pair(id + "-backlit", leaf, oldLeaf, newLeaf);
                Lighting(); _camera.Position = target + new Vector3(.85f, -.38f, 1).Normalized() * size * 1.8f; _camera.LookAt(target);
                await Pair(id + "-underside", leaf, oldLeaf, newLeaf);
                _camera.Position = target + new Vector3(.85f, .55f, 1).Normalized() * size * 1.8f; _camera.LookAt(target);
                leaf.SetShaderParameter("review_time", 1.25f); await Pair(id + "-motion", leaf, oldLeaf, newLeaf);
                if (id == "lanternbrush")
                {
                    // A labelled sentinel exercises leaf emission even if this profile's species
                    // is not currently emissive. Production organ-mask equations remain in use.
                    Lighting(night: true); leaf.SetShaderParameter("review_time", 0f);
                    leaf.SetShaderParameter("bioluminescence_color", new Vector3(.12f, .65f, .25f));
                    leaf.SetShaderParameter("bioluminescence_strength", .12f); leaf.SetShaderParameter("night_factor", 1f);
                    await Pair(id + "-emission-sentinel", leaf, oldLeaf, newLeaf);
                }
                specimen.QueueFree(); await Frames(2);
            }
            Lighting();
            var descriptor = Content.PresetOrThrow("default"); descriptor.Diameter = 5; descriptor.CellSize = .1;
            descriptor.PilotTreeId = "none"; descriptor.Placement.Rocks = descriptor.Placement.Logs = descriptor.Placement.GravelPatches = 0;
            descriptor.Water.Springs.Clear(); descriptor.Water.WaterTable = -.4;
            var world = VivariumWorld.Create(Content, descriptor, false); world.Clock.Paused = true;
            var hf = world.Terrain; hf.BeginEdit();
            for (int z = 0; z < hf.Nz; z++) for (int x = 0; x < hf.Nx; x++)
            { var p = hf.VertexPos(x, z); hf.H[z * hf.Nx + x] = .06 * p.X + .035 * p.Z * p.Z; }
            hf.Touch(); world.Water.RefreshBed(hf);
            Array.Clear(world.AmbientGroundCover.Cover); Array.Clear(world.Litter.FineMass);
            Array.Fill(world.Fields.BaseSubstrate.Values, (byte)Substrate.Soil);
            foreach (int c in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(c); world.Fields.Moisture.Values[c] = p.X < 0 ? .12 : .98;
                if (p.Z < -.4) world.Fields.BaseSubstrate.Values[c] = (byte)Substrate.Gravel;
            }
            var island = new IslandRenderer(); AddChild(island); island.Build(world); island.SetProcess(false);
            var top = island.GetNode<MeshInstance3D>("TerrainTop");
            var terrain = (ShaderMaterial)top.Mesh.SurfaceGetMaterial(0);
            var oldTerrain = Baseline("terrain.gdshader"); var newTerrain = terrain.Shader;
            string digest = WorldSerializer.Digest(world);
            _camera.Position = new Vector3(1.3f, 1.5f, 2.0f); _camera.LookAt(new Vector3(0, .04f, 0));
            await Pair("terrain-dry-wet-soil-gravel", terrain, oldTerrain, newTerrain);
            _camera.Position = new Vector3(.65f, .30f, .65f); _camera.LookAt(new Vector3(.2f, .01f, 0));
            await Pair("terrain-wet-contact", terrain, oldTerrain, newTerrain);
            terrain.SetShaderParameter("overlay_mode", 1);
            await Pair("terrain-moisture-overlay", terrain, oldTerrain, newTerrain);
            terrain.SetShaderParameter("overlay_mode", 0);
            if (WorldSerializer.Digest(world) != digest) throw new InvalidOperationException("Material capture mutated world");
            // Integrated fixture uses the ordinary FloraRenderer over authoritative individuals,
            // sharing this IslandRenderer, with no ecology advancement.
            foreach (var entry in new[] { ("shadebell", -.7, .2), ("lanternbrush", .7, .25),
                ("embercrown", .1, -.8), ("glowbriar", .5, .8) })
            {
                var sp = Content.FloraOrThrow(entry.Item1);
                var plant = world.FloraSystem.Establish(sp, new Vec2(entry.Item2, entry.Item3), "material-review", sp.MaxBiomass);
                plant.Age = sp.MaturityAge + 1;
            }
            var flora = new FloraRenderer { Camera = _camera, Quality = 1 }; AddChild(flora); flora.Build(world);
            _camera.Position = new Vector3(2.6f, 2.1f, 3.4f); _camera.LookAt(new Vector3(0, .65f, 0));
            for (int i = 0; i < 2400 && !flora.PopulationReady; i++) await Frames(1);
            if (!flora.PopulationReady) throw new TimeoutException("Integrated flora population did not finish");
            flora.SetProcess(false);
            var materials = new Dictionary<ulong, ShaderMaterial>();
            void Collect(Node node)
            {
                if (node is MultiMeshInstance3D multi && multi.Multimesh?.Mesh is { } mesh)
                    for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                        if (mesh.SurfaceGetMaterial(s) is ShaderMaterial mat) materials[mat.GetInstanceId()] = mat;
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(flora);
            var leaves = materials.Values.Where(m => m.Shader.ResourcePath == "res://Shaders/leaf.gdshader").ToArray();
            if (leaves.Length == 0) throw new InvalidOperationException("Integrated fixture has no photographed leaf material");
            foreach (var mat in materials.Values) mat.SetShaderParameter("review_time", 0f);
            digest = WorldSerializer.Digest(world);
            foreach (bool night in new[] { false, true })
            {
                Lighting(night); NightFloraMaterials.SetNightFactor(night ? 1f : 0f);
                string name = "integrated-" + (night ? "night" : "day");
                if (!GeometryOnly) {
                    terrain.Shader = oldTerrain; foreach (var mat in leaves) mat.Shader = oldLeaf;
                    await Capture(name + "-before");
                }
                terrain.Shader = newTerrain; foreach (var mat in leaves) mat.Shader = newLeaf;
                await Capture(GeometryOnly ? name : name + "-after");
                if (MotionSequence && !night)
                {
                    string motionDir = Path.Combine(OutDir, "motion"); Directory.CreateDirectory(motionDir);
                    var target = new Vector3(0, .65f, 0); var offset = _camera.Position - target;
                    for (int frame = 0; frame < 48; frame++)
                    {
                        float phase = frame / 24f;
                        foreach (var mat in materials.Values) mat.SetShaderParameter("review_time", phase);
                        _camera.Position = target + offset.Rotated(Vector3.Up, (frame / 47f - .5f) * .25f);
                        _camera.LookAt(target);
                        await Capture("motion/frame-" + frame.ToString("000"), 2);
                    }
                    foreach (var mat in materials.Values) mat.SetShaderParameter("review_time", 0f);
                    _camera.Position = target + offset; _camera.LookAt(target);
                    File.WriteAllText(Path.Combine(motionDir, "sequence.json"), JsonSerializer.Serialize(new {
                        frames = 48, playbackFps = 24, phaseStep = 1.0 / 24, width = 1600, height = 900,
                        animation = "production shader review_time with authored attachment and instance data",
                        camera = "bounded 0.25 rad orbit", timing = "fixed phase/offline capture; not runtime FPS evidence" }));
                }
            }
            if (WorldSerializer.Digest(world) != digest) throw new InvalidOperationException("Integrated capture mutated world");
            _receipts.Add(new { integratedFlora = world.Flora.Count, photographedMaterials = leaves.Length,
                productionRenderers = new[] { "IslandRenderer", "FloraRenderer" }, fixedAnimationTime = 0,
                worldDigestUnchanged = true, note = "four mature fixture plants, not a mature ecology/performance acceptance" });
            _receipts.Add(new { terrainTriangles = island.TriangleCount, worldDigestUnchanged = true,
                zeroStoredLitter = world.Litter.FineMass.All(v => v == 0), coverageTexture = "production R8 red channel",
                emissionSentinel = "explicit synthetic leaf emission; not species acceptance" });
            File.WriteAllText(Path.Combine(OutDir, "receipt.json"), JsonSerializer.Serialize(_receipts, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("MATERIAL_PORT_REVIEW_OK " + OutDir); GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("MATERIAL_PORT_REVIEW_FAILED " + ex); GetTree().Quit(2); }
    }
}
