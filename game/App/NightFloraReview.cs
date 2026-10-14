using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.App;

/// <summary>Isolated day/night review of the new catalog using production geometry and emission binding.</summary>
public partial class NightFloraReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public override void _Ready() => _ = Capture();
    private async Task Capture()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            foreach (var sample in new[] { (450.0,0f), (660.0,0f), (900.0,1f),
                (1020.0,1f), (1140.0,1f), (1200.0,0f) })
                if (Math.Abs(NightFloraMaterials.NightFactorAtSeconds(sample.Item1)-sample.Item2)>1e-6)
                    throw new InvalidOperationException("Night envelope failed at " + sample.Item1);
            if (NightFloraMaterials.NightFactorAtSeconds(780) is <= 0 or >= 1
                || NightFloraMaterials.NightFactorAtSeconds(1170) is <= 0 or >= 1)
                throw new InvalidOperationException("Night envelope transitions must be smooth");
            GetWindow().Size = new Vector2I(1600, 1000);
            var rig = new EnvironmentRig { BenchmarkLightSeconds = 450 };
            AddChild(rig); rig.ApplyQuality(2);
            rig.CameraAttributes.AutoExposureEnabled = false;
            rig.Env.FogEnabled = false;
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(12, 10) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.22f,.19f,.14f), Roughness = .95f } });
            string[] ids = { "plumegrass", "glowbriar", "lanternbrush", "lanternsedge", "tidebell", "siltstars", "ghostbonnet", "starmoss", "moonfern" };
            var records = new System.Collections.Generic.List<object>();
            for (int i = 0; i < ids.Length; i++)
            {
                var sp = Content.FloraOrThrow(ids[i]);
                var mat = Bridge.Shader("res://Shaders/flora.gdshader");
                mat.SetShaderParameter("stiffness", sp.Woody != null ? 7f : 2f);
                mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, _ => 2 });
                FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: true);
                NightFloraMaterials.Bind(mat, sp);
                var mesh = OrganismMeshes.FloraTier(sp, Hash.Fnv1a64("night.review." + sp.Id), 0);
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true, UseCustomData = true, Mesh = Bridge.ToArrayMesh(mesh, mat), InstanceCount = 1 };
                mm.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(new Vector3((float)sp.RadiusAtMax, (float)sp.Height, (float)sp.RadiusAtMax)), Vector3.Zero));
                mm.SetInstanceColor(0, Colors.White);
                mm.SetInstanceCustomData(0, new Color(.5f, 1f, 0f, .5f));
                var pos = new Vector3((i%3-1)*2.25f, .008f, (i/3-1)*2.05f);
                AddChild(new MultiMeshInstance3D { Multimesh = mm, Position = pos });
                AddChild(new Label3D { Text = sp.Name, Position = pos + new Vector3(0,.04f,.8f), FontSize = 30,
                    PixelSize = .0025f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true });
                if (sp.BioluminescenceStrength > 0 && sp.BioluminescenceColor is { Length: >= 3 })
                {
                    var lightPos = pos + new Vector3(0, Math.Max(0.06f, (float)sp.Height * 0.35f), 0);
                    var light = NightFloraMaterials.CreateLightForSpecies(sp, lightPos);
                    AddChild(light);
                }
                records.Add(new { species = sp.Id, mesh.TriangleCount, glow = sp.BioluminescenceStrength,
                    finite = mesh.Positions.All(float.IsFinite) && mesh.Normals.All(float.IsFinite) });
            }
            var camera = new Camera3D { Current = true, Fov = 48, Near = .02f, Far = 30,
                Position = new Vector3(0, 5.8f, 8.8f) };
            AddChild(camera); camera.LookAt(new Vector3(0,.35f,0));
            foreach (var phase in new[] { (Name: "day", Seconds: 450.0), (Name: "night", Seconds: 1020.0) })
            {
                rig.BenchmarkLightSeconds = phase.Seconds;
                rig.SetProcess(true);
                for (int frame = 0; frame < 2; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                rig.SetProcess(false);
                NightFloraMaterials.SetNightFactor(NightFloraMaterials.NightFactorAtSeconds(phase.Seconds));
                bool day = phase.Name == "day";
                rig.Sun.RotationDegrees = new Vector3(-48, -35, 0);
                rig.Sun.LightEnergy = day ? 1.8f : .08f;
                rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                rig.Env.AmbientLightColor = new Color(.65f,.72f,.8f);
                rig.Env.AmbientLightEnergy = day ? .65f : .06f;
                rig.Env.TonemapExposure = day ? 1.6f : 1.3f;
                rig.Env.FogEnabled = false;
                for (int frame = 0; frame < 20; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var err = GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(Path.Combine(OutDir,phase.Name+".png")));
                if (err != Error.Ok) throw new IOException("Capture failed: " + err);
            }
            NightFloraMaterials.SetNightFactor(0);
            rig.Sun.LightEnergy = 1.8f; rig.Env.AmbientLightEnergy = .65f; rig.Env.TonemapExposure = 1.6f;
            camera.Position = new Vector3(-2.25f, .95f, -.15f); camera.LookAt(new Vector3(-2.25f,.48f,-2.05f));
            for (int frame = 0; frame < 20; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(Path.Combine(OutDir,"plumegrass-close.png")));
            File.WriteAllText(Path.Combine(OutDir,"review.json"), System.Text.Json.JsonSerializer.Serialize(records,new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print("NIGHT_FLORA_REVIEW_OK " + OutDir); GetTree().Quit();
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); GetTree().Quit(2); }
    }
}
