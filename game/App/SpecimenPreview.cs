using System;
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

/// <summary>Renders one current flora mesh at mature in-game scale for species-by-species visual review.</summary>
public partial class SpecimenPreview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string SpeciesId { get; set; } = "";
    public bool Juvenile { get; set; }
    public bool Underside { get; set; }
    public string OutDir { get; set; } = "";
    public string Lighting { get; set; } = "front";
    public int Detail { get; set; }
    public float Phase { get; set; } = -1;
    public bool Reverse { get; set; }
    public bool Legacy { get; set; }
    public bool GameTier { get; set; }
    public bool Close { get; set; }
    public bool BatchMode { get; set; }
    public bool CoverageProductionPath { get; set; }
    public TaskCompletionSource<bool> Finished { get; } = new();

    public override void _Ready() => _ = CaptureAsync();

    private async Task CaptureAsync()
    {
        try
        {
            var sp = Content.FloraOrThrow(SpeciesId);
            bool floating = sp.Shape is "floatleaf" or "glasslily_pad";
            GetWindow().Size = new Vector2I(1024, 1024);
            var environment = new EnvironmentRig { Name = "Environment" };
            AddChild(environment);
            environment.ApplyQuality(2);
            if (BatchMode)
            {
                environment.BenchmarkLightSeconds = 420;
                environment.CameraAttributes.AutoExposureEnabled = false;
                environment.CameraAttributes.DofBlurNearEnabled = false;
                environment.CameraAttributes.DofBlurFarEnabled = false;
            }
            if (Lighting == "back") environment.Sun.RotationDegrees = new Vector3(-28,145,0);
            if (Lighting == "shade") { environment.Sun.LightEnergy = .22f; environment.Env.AmbientLightEnergy = .35f; }

            if (CoverageProductionPath || sp.Id == "blue_sundew")
            {
                var fixture = await IsolatedCoverageSpecimen.BuildAsync(Content, SpeciesId, this);
                AddChild(new MeshInstance3D { Name="NeutralCoverageGround",
                    Mesh=new PlaneMesh { Size=new Vector2(.35f,.35f) },
                    MaterialOverride=new StandardMaterial3D { AlbedoColor=new Color(.36f,.35f,.29f), Roughness=1f },
                    Position=fixture.Target-Vector3.Up*.004f });
                var coverageCamera = new Camera3D { Projection=Camera3D.ProjectionType.Orthogonal,
                    Size=fixture.Size, Position=fixture.Target+new Vector3(.16f,.32f,.16f), Current=true,
                    Attributes=environment.CameraAttributes };
                AddChild(coverageCamera); coverageCamera.LookAt(fixture.Target);
                await CaptureSequenceAsync(coverageCamera, environment, new { path="production_coverage", fixture.Stats });
                Finished.TrySetResult(true);
                if (!BatchMode) GetTree().Quit(0);
                return;
            }

            float groundR = Math.Max(0.18f, (float)sp.RadiusAtMax * 1.35f);
            var ground = new MeshInstance3D
            {
                Name = "NeutralGround",
                Mesh = new CylinderMesh { TopRadius = groundR, BottomRadius = groundR, Height = 0.025f, RadialSegments = 64 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.35f, 0.29f), Roughness = 1.0f },
                Position = new Vector3(0, -0.018f, 0),
            };
            ground.Visible = !Underside;
            AddChild(ground);
            if (sp.Shape == "bracket")
            {
                AddChild(new MeshInstance3D
                {
                    Name = "PreviewHostLog",
                    Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.22f, Height = 1.15f, RadialSegments = 32 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.18f, 0.14f), Roughness = 0.95f },
                    Position = new Vector3(-0.22f, 0.50f, 0),
                });
            }
            if (sp.Climber != null && !Juvenile)
            {
                AddChild(new MeshInstance3D
                {
                    Name = "PreviewHostTrunk",
                    Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.20f, Height = 1.15f, RadialSegments = 32 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.18f, 0.14f), Roughness = 0.9f },
                    Position = new Vector3(0, 0.56f, -0.19f),
                });
            }
            if (floating)
            {
                AddChild(new MeshInstance3D
                {
                    Name = "PreviewWater",
                    Mesh = new CylinderMesh { TopRadius = Math.Max(0.28f, (float)sp.RadiusAtMax * 1.15f), BottomRadius = Math.Max(0.28f, (float)sp.RadiusAtMax * 1.15f), Height = 0.003f, RadialSegments = 64 },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.12f, 0.28f, 0.32f, 0.70f),
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        Metallic = 0.15f,
                        Roughness = 0.3f,
                    },
                    Position = new Vector3(0, 0.198f, 0),
                });
            }

            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, _ => 2 });
            mat.SetShaderParameter("sway", 0.0f);
            if (BatchMode) mat.SetShaderParameter("review_time", 0f);
            if (Phase >= 0) { mat.SetShaderParameter("sway",1f); mat.SetShaderParameter("review_time",Phase); }
            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            var profile = Legacy ? null : FloraVisualProfile.Load(sp.Id, sp: sp);
            FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
            // Optional screenshot of the exact budgeted mesh used by the in-game renderer,
            // rather than the uncapped, raw per-species authored generator.
            var mesh = GameTier || sp.Id == "ringreed"
                ? OrganismMeshes.FloraTier(sp, seed, Detail, Juvenile, tieredSource: profile != null)
                : OrganismMeshes.Flora(sp, seed, Juvenile, profile == null ? null : Detail);
            ShaderMaterial? leaf = profile?.LeafMaterial(animated: Phase >= 0);
            if (leaf != null) { leaf.SetShaderParameter("review_time",Phase); leaf.SetShaderParameter("plant_stiffness",sp.Woody != null ? 7f : 3f); }
            if (BatchMode && leaf != null) leaf.SetShaderParameter("review_time",0f);
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = profile == null ? Bridge.ToArrayMesh(mesh, mat) : profile.Compile(mesh, mat, leaf!),
                InstanceCount = 1,
            };
            multimesh.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(
                floating ? new Vector3(1, 0.2f, 1)
                : (sp.Climber != null && !Juvenile) ? new Vector3(0.95f, 0.95f, 0.95f)
                : (sp.Climber != null && Juvenile) ? new Vector3((float)sp.RadiusAtMax, (float)sp.RadiusAtMax * 0.35f, (float)sp.RadiusAtMax)
                : (sp.Shape == "bracket") ? new Vector3((float)sp.RadiusAtMax * 1.5f, (float)sp.Height * 2.2f, (float)sp.RadiusAtMax * 1.5f)
                : new Vector3((float)sp.RadiusAtMax, (float)sp.Height, (float)sp.RadiusAtMax)),
                (sp.Shape == "bracket") ? new Vector3(0, 0.10f, 0) : Vector3.Zero));
            multimesh.SetInstanceColor(0, Colors.White);
            multimesh.SetInstanceCustomData(0, new Color(0.5f, 1.0f, 0.0f, 0.5f));
            AddChild(new MultiMeshInstance3D { Name = sp.Name, Multimesh = multimesh });

            float specH = (sp.Climber != null && !Juvenile) ? 1.05f : (float)sp.Height;
            float specR = (sp.Climber != null && !Juvenile) ? 0.38f : (float)sp.RadiusAtMax;
            float boundDim = (float)Math.Max(specH, specR * 2.0f);
            float specSize = Math.Max(0.25f, boundDim * 1.35f);
            var target = floating
                ? new Vector3(0, 0.2f, 0)
                : (sp.Climber != null && !Juvenile)
                    ? new Vector3(0, 0.52f, 0)
                    : (sp.Shape == "bracket")
                        ? new Vector3(0.08f, 0.22f, 0)
                        : new Vector3(0, (float)sp.Height * 0.45f, 0);
            if (Close) target.Y = floating ? 0.24f : (float)sp.Height * .65f;
            float dist = Math.Max(0.6f, boundDim * 1.5f);
            var camera = new Camera3D
            {
                Name = "SpecimenCamera",
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = Close ? (floating ? 0.26f : specSize * .38f) : floating ? Math.Max(0.38f, (float)sp.RadiusAtMax * 1.6f) : Juvenile ? Math.Max(0.18f, specSize * 0.65f) : specSize,
                Position = floating
                    ? (new Vector3(0.32f, 0.52f, 0.32f) * (sp.RadiusAtMax > 0.35 ? 1.45f : 1.0f))
                    : Underside
                        ? new Vector3(0.8f, -0.12f, 0.8f)
                        : target + new Vector3(1.0f, 0.70f, 1.0f).Normalized() * dist,
                Current = true,
            };
            AddChild(camera);
            camera.LookAt(target);
            if (Reverse) { var offset=camera.Position-target; camera.Position=target+new Vector3(-offset.X,offset.Y,-offset.Z); camera.LookAt(target); }

            if (BatchMode)
            {
                var bounds=multimesh.GetAabb();
                target=bounds.GetCenter();
                float extent=Math.Max(.03f,Math.Max(bounds.Size.X,Math.Max(bounds.Size.Y,bounds.Size.Z)));
                camera.Size=extent*1.55f;
                camera.Position=target+new Vector3(1f,.7f,1f).Normalized()*Math.Max(.6f,extent*2f);
                camera.Attributes=environment.CameraAttributes;
                camera.LookAt(target);
                await CaptureSequenceAsync(camera,environment,new { path="production_flora_tier", seed, triangles=mesh.TriangleCount, leaves=mesh.Leaves.Count, profile=profile!=null, bounds });
                Finished.TrySetResult(true);
                return;
            }

            for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(OutDir);
            string path = Path.GetFullPath(Path.Combine(OutDir, SpeciesId + (Juvenile ? "-juvenile" : Underside ? "-underside" : "-mature") + ".png"));
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok) throw new IOException($"SavePng returned {error}");
            GD.Print($"SPECIMEN_OK {path} triangles={mesh.TriangleCount} leaves={mesh.Leaves.Count} tier={Detail} lighting={Lighting}");
            Finished.TrySetResult(true);
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr("SPECIMEN_FAILED " + ex);
            Finished.TrySetResult(false);
            if (!BatchMode) GetTree().Quit(1);
        }
    }

    private async Task CaptureSequenceAsync(Camera3D camera, EnvironmentRig environment, object geometry)
    {
        Directory.CreateDirectory(OutDir);
        var viewport=GetViewport();
        var msaa=viewport.Msaa3D; bool taa=viewport.UseTaa;
        float size=camera.Size;
        var captures=new System.Collections.Generic.List<object>();
        async Task Frames(int count) { for(int i=0;i<count;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
        async Task Save(string label)
        {
            await Frames(3);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            string file=label+".png";
            var error=viewport.GetTexture().GetImage().SavePng(Path.Combine(OutDir,file));
            if(error!=Error.Ok) throw new IOException($"SavePng {error}");
            captures.Add(new { file, camera_size=camera.Size, msaa=viewport.Msaa3D.ToString(), taa=viewport.UseTaa });
        }
        try
        {
            if (Main.UserArgs.Contains("--isolation-dof"))
            {
                // Match gameplay's focused-camera distance/transition/amount policy.
                // Coverage fixtures look at their authored target 0.392m from the camera.
                var attr=environment.CameraAttributes;
                float targetDistance=CoverageProductionPath || SpeciesId=="blue_sundew" ? new Vector3(.16f,.32f,.16f).Length() : Math.Max(.1f,camera.Position.Length());
                camera.Projection=Camera3D.ProjectionType.Perspective;
                camera.Fov=45f;
                foreach(bool aaOff in new[]{false,true})
                {
                    viewport.Msaa3D=aaOff?Viewport.Msaa.Disabled:msaa;
                    foreach(string mode in new[]{"off","in_focus","focus_nearer","focus_farther","focus_nearer_no_jitter"})
                    {
                        float focus=mode.StartsWith("focus_nearer") ? targetDistance*.4f : mode=="focus_farther" ? targetDistance*1.8f : targetDistance;
                        attr.DofBlurNearEnabled=attr.DofBlurFarEnabled=mode!="off";
                        attr.DofBlurFarDistance=focus*1.18f;
                        attr.DofBlurFarTransition=Math.Max(.08f,focus*.85f);
                        attr.DofBlurNearDistance=Math.Max(.015f,focus*.78f);
                        attr.DofBlurNearTransition=Math.Max(.01f,focus*.28f);
                        attr.DofBlurAmount=Math.Clamp(.085f*(1-(focus-.15f)/2.8f),.015f,.085f);
                        bool jitter=mode!="focus_nearer_no_jitter";
                        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High,jitter);
                        await Frames(30);
                        for(int i=0;i<6;i++) await Save($"dof_{mode}_{(aaOff?"msaa_off":"baseline")}_{i:D2}");
                        captures.Add(new { mode, msaa=viewport.Msaa3D.ToString(), jitter,
                            target_distance=targetDistance, focus_distance=focus, amount=attr.DofBlurAmount,
                            near=attr.DofBlurNearDistance, near_transition=attr.DofBlurNearTransition,
                            far=attr.DofBlurFarDistance, far_transition=attr.DofBlurFarTransition });
                    }
                }
            }
            else
            foreach(var view in new[]{"whole","macro"})
            {
                camera.Size=view=="whole" ? size : size*.4f;
                viewport.Msaa3D=msaa;
                await Frames(30);
                for(int i=0;i<6;i++) await Save($"{view}_baseline_{i:D2}");
                viewport.Msaa3D=Viewport.Msaa.Disabled;
                await Frames(24);
                for(int i=0;i<3;i++) await Save($"{view}_msaa_off_{i:D2}");
            }
        }
        finally
        {
            viewport.Msaa3D=msaa; viewport.UseTaa=taa; camera.Size=size;
            environment.CameraAttributes.DofBlurNearEnabled=false;
            environment.CameraAttributes.DofBlurFarEnabled=false;
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High,true);
        }
        File.WriteAllText(Path.Combine(OutDir,"specimen.json"),JsonSerializer.Serialize(new {
            species=SpeciesId, geometry, resolution=new[]{1024,1024}, light_seconds=420,
            camera_eye=new[]{camera.Position.X,camera.Position.Y,camera.Position.Z},
            baseline_msaa=msaa.ToString(), baseline_taa=taa, baseline_fxaa=viewport.ScreenSpaceAA.ToString(),
            automatic_exposure=false, wind=false, material_review_seconds=0, captures,
            scope=Main.UserArgs.Contains("--isolation-dof")
                ? "One species only; perspective camera and fixed exposure/light. Five focus/jitter modes crossed with MSAA baseline/off, six frames each; focus thresholds follow gameplay close-focus policy. No biology advancement or performance acceptance."
                : "One species only; authored inspection fixture, production geometry/material path; six fixed-camera baseline frames and three MSAA-off controls at each of two scales. No biological simulation advancement or gameplay/performance acceptance."
        },new JsonSerializerOptions{WriteIndented=true}));
        GD.Print($"ISOLATED_SPECIES_OK {SpeciesId} captures={captures.Count}");
    }
}
