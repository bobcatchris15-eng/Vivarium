using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.App;

/// <summary>Native textured asset versus procedural specimen in the actual rendering client.
/// This fixture does not advance ecology or claim wind animation for the native mesh.</summary>
public partial class FernAssetReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public string AssetPath { get; set; } = "res://content/visual-lab-candidates/fern02/fern02.glb";
    public string Variant { get; set; } = "procedural";
    public string CameraReferencePath { get; set; } = "";
    private Camera3D _camera = null!;
    private EnvironmentRig _rig = null!;
    private readonly List<object> _captures = new();
    public override void _Ready() => _ = Run();
    private async Task Frames(int n = 16) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name, Vector3 eye, Vector3 target, bool backlit = false)
    {
        if (!string.IsNullOrWhiteSpace(CameraReferencePath))
        {
            using var reference = JsonDocument.Parse(File.ReadAllText(CameraReferencePath));
            var row = reference.RootElement.GetProperty("captures").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name);
            Vector3 ReadVector(string key) {
                var parts = row.GetProperty(key).GetString()!.Trim('(', ')').Split(',');
                return new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture), float.Parse(parts[2], CultureInfo.InvariantCulture));
            }
            eye = ReadVector("eye"); target = ReadVector("target");
        }
        _rig.Sun.RotationDegrees = new Vector3(-38, backlit ? 135 : -45, 0);
        _camera.Position = eye; _camera.LookAt(target);
        await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(Path.Combine(OutDir, name + ".png")) != Error.Ok) throw new IOException(name);
        _captures.Add(new { name, eye = eye.ToString(), target = target.ToString(), cameraBasis = _camera.Basis.ToString(), fov = _camera.Fov,
            sunRotation = _rig.Sun.RotationDegrees.ToString(), sunEnergy = _rig.Sun.LightEnergy, ambient = _rig.Env.AmbientLightEnergy,
            exposure = _rig.Env.TonemapExposure, fixedLeafPhase = 0 });
        GD.Print("FERN_CAPTURE " + name);
    }
    private async Task Run()
    {
        try
        {
            if (Variant != "procedural" && Variant != "native") throw new ArgumentException("Unknown fern variant");
            OutDir = Path.GetFullPath(OutDir); Directory.CreateDirectory(OutDir);
            GetWindow().Size = new Vector2I(1600, 900);
            _rig = new EnvironmentRig { BenchmarkLightSeconds = 450 }; AddChild(_rig); await Frames(2);
            _rig.SetProcess(false); _rig.ApplyQuality(2); _rig.CameraAttributes.AutoExposureEnabled = false;
            _rig.Env.TonemapExposure = 1.12f; _rig.Env.FogEnabled = false;
            _rig.Env.BackgroundMode = Godot.Environment.BGMode.Color; _rig.Env.BackgroundColor = new Color(.055f,.065f,.06f);
            _rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            _rig.Env.AmbientLightColor = new Color(.72f,.79f,.85f); _rig.Env.AmbientLightEnergy = .45f; _rig.Sun.LightEnergy = 1.15f;
            _camera = new Camera3D { Current = true, Fov = 42, Near = .005f, Far = 40 }; AddChild(_camera);
            var sp = Content.FloraOrThrow("veilfern");
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual.veilfern"), 0x9E3779B97F4A7C15UL);
            var profile = FloraVisualProfile.Load("veilfern");
            var source = OrganismMeshes.Flora(sp, seed, false, profile == null ? null : 0);
            var minimum = new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);
            var maximum = -minimum;
            var speciesScale = new Vector3((float)sp.RadiusAtMax,(float)sp.Height,(float)sp.RadiusAtMax);
            for (int i = 0; i < source.VertexCount; i++) { var p = Bridge.V(source.Position(i)) * speciesScale; minimum = minimum.Min(p); maximum = maximum.Max(p); }
            var bounds = new Aabb(minimum, maximum-minimum);
            object assetReceipt;
            if (Variant == "procedural")
            {
                var stem = Bridge.Shader("res://Shaders/flora.gdshader"); stem.SetShaderParameter("surface_mode",2); stem.SetShaderParameter("sway",0f);
                stem.SetShaderParameter("stiffness",3f); stem.SetShaderParameter("review_time",0f);
                Bridge.BindSurface(stem,"moss",Bridge.Surfaces.Moss);
                FloraSurfaceProfiles.Bind(stem,sp,photographedLeaves:profile == null);
                var leaf = profile?.LeafMaterial(animated:false);
                if (leaf != null) { leaf.SetShaderParameter("review_time",0f); leaf.SetShaderParameter("plant_stiffness",3f); NightFloraMaterials.Bind(leaf,sp); }
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true,
                    Mesh = profile == null ? Bridge.ToArrayMesh(source,stem) : profile.Compile(source,stem,leaf!), InstanceCount = 1 };
                mm.SetInstanceTransform(0,new Transform3D(Basis.Identity.Scaled(speciesScale),Vector3.Zero));
                mm.SetInstanceColor(0,Colors.White); mm.SetInstanceCustomData(0,new Color(.5f,1f,0f,.5f));
                AddChild(new MultiMeshInstance3D { Multimesh = mm });
                assetReceipt = new { selected = "production veilfern", seed = seed.ToString(), source.VertexCount, nativeWind = false };
            }
            else
            {
                string full = ProjectSettings.GlobalizePath(AssetPath);
                using var document = new GltfDocument(); using var state = new GltfState();
                var error = document.AppendFromFile(full,state);
                if (error != Error.Ok) throw new IOException("Native GLB load: " + error);
                var imported = document.GenerateScene(state);
                var meshes = new List<MeshInstance3D>();
                void Collect(Node n) { if (n is MeshInstance3D m) meshes.Add(m); foreach (Node c in n.GetChildren()) Collect(c); }
                Collect(imported);
                var selected = meshes.FirstOrDefault(m => m.Name.ToString().StartsWith("fern_02_b",StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("Expected coherent fern_02_b clump absent");
                var original = selected.GetAabb();
                if (original.Size.Y <= 0) throw new InvalidOperationException("Degenerate native plant height");
                float scale = bounds.Size.Y / original.Size.Y;
                var specimen = new MeshInstance3D { Mesh = selected.Mesh, Scale = Vector3.One * scale,
                    Position = new Vector3(-original.GetCenter().X*scale,-original.Position.Y*scale,-original.GetCenter().Z*scale) };
                AddChild(specimen);
                assetReceipt = new { selected = selected.Name.ToString(), allNodes = meshes.Select(m => new { name = m.Name.ToString(), bounds = m.GetAabb().ToString(), transform = m.Transform.ToString() }).ToArray(),
                    originalBounds = original.ToString(), scale, translation = specimen.Position.ToString(),
                    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(full))),
                    materialCount = selected.Mesh.GetSurfaceCount(), nativeWind = false, nativeTexturesPreserved = true };
                imported.Free();
            }
            float size = Math.Max(.2f, Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y,bounds.Size.Z)));
            AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = size*1.5f, BottomRadius = size*1.5f, Height = .025f,
                Material = new StandardMaterial3D { AlbedoColor = new Color(.075f,.058f,.039f), Roughness = 1f } }, Position = new Vector3(0,-.0125f,0) });
            var target = bounds.GetCenter();
            await Capture("fern-close-front", target + new Vector3(.85f,.55f,1).Normalized()*size*1.8f,target);
            await Capture("fern-close-backlit", target + new Vector3(.85f,.55f,1).Normalized()*size*1.8f,target,true);
            await Capture("fern-close-side", target + new Vector3(-1,.35f,.3f).Normalized()*size*1.8f,target);
            await Capture("fern-medium", target + new Vector3(.85f,.55f,1).Normalized()*size*3.2f,target);
            await Capture("fern-ordinary", target + new Vector3(.85f,.55f,1).Normalized()*size*6f,target);
            File.WriteAllText(Path.Combine(OutDir,"fern_review.json"),JsonSerializer.Serialize(new { variant = Variant, asset = AssetPath, assetReceipt,
                cameraReference = CameraReferencePath, cameraReferenceSha256 = string.IsNullOrWhiteSpace(CameraReferencePath) ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(CameraReferencePath))),
                proceduralBounds = bounds.ToString(), seed = seed.ToString(), resolution = new[] {1600,900}, lightSeconds = 450,
                captures = _captures, limitations = "Static specimen fixture; native wind, ecology, ground integration and performance are untested. Shader TIME is not authenticated; procedural review_time=0 and stem sway=0." },new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("FERN_REVIEW_OK"); GetTree().Quit(0);
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(3); }
    }
}
