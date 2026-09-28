using System;
using System.IO;
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
    public bool Close { get; set; }

    public override void _Ready() => _ = CaptureAsync();

    private async Task CaptureAsync()
    {
        try
        {
            var sp = Content.FloraOrThrow(SpeciesId);
            bool floating = sp.Shape == "floatleaf";
            GetWindow().Size = new Vector2I(1024, 1024);
            var environment = new EnvironmentRig { Name = "Environment" };
            AddChild(environment);
            environment.ApplyQuality(2);
            if (Lighting == "back") environment.Sun.RotationDegrees = new Vector3(-28,145,0);
            if (Lighting == "shade") { environment.Sun.LightEnergy = .22f; environment.Env.AmbientLightEnergy = .35f; }

            float groundR = Math.Max(0.18f, (float)sp.RadiusAtMax * 1.35f);
            var ground = new MeshInstance3D
            {
                Name = "NeutralGround",
                Mesh = new CylinderMesh { TopRadius = groundR, BottomRadius = groundR, Height = 0.025f, RadialSegments = 64 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.35f, 0.29f), Roughness = 1.0f },
                Position = new Vector3(0, -0.018f, 0),
            };
            ground.Visible = !Underside && !floating;
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
                    Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.28f, Height = 0.003f, RadialSegments = 64 },
                    MaterialOverride = new StandardMaterial3D
                    {
                        AlbedoColor = new Color(0.18f, 0.34f, 0.38f),
                        Metallic = 0.15f,
                        Roughness = 0.3f,
                    },
                    Position = new Vector3(0, 0.198f, 0),
                });
            }

            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, "slime_mold" => 4, _ => 2 });
            mat.SetShaderParameter("sway", 0.0f);
            if (Phase >= 0) { mat.SetShaderParameter("sway",1f); mat.SetShaderParameter("review_time",Phase); }
            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            var profile = Legacy ? null : FloraVisualProfile.Load(sp.Id);
            FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
            var mesh = OrganismMeshes.Flora(sp, seed, Juvenile, profile == null ? null : Detail);
            ShaderMaterial? leaf = profile?.LeafMaterial(animated: Phase >= 0);
            if (leaf != null) { leaf.SetShaderParameter("review_time",Phase); leaf.SetShaderParameter("plant_stiffness",sp.Woody != null ? 7f : 3f); }
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
            if (Close) target.Y = (float)sp.Height * .65f;
            float dist = Math.Max(0.6f, boundDim * 1.5f);
            var camera = new Camera3D
            {
                Name = "SpecimenCamera",
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = Close ? specSize * .38f : floating ? 0.38f : Juvenile ? Math.Max(0.18f, specSize * 0.65f) : specSize,
                Position = floating
                    ? new Vector3(0.25f, 0.68f, 0.25f)
                    : Underside
                        ? new Vector3(0.8f, -0.12f, 0.8f)
                        : target + new Vector3(1.0f, 0.70f, 1.0f).Normalized() * dist,
                Current = true,
            };
            AddChild(camera);
            camera.LookAt(target);
            if (Reverse) { var offset=camera.Position-target; camera.Position=target+new Vector3(-offset.X,offset.Y,-offset.Z); camera.LookAt(target); }

            for (int i = 0; i < 12; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(OutDir);
            string path = Path.GetFullPath(Path.Combine(OutDir, SpeciesId + (Juvenile ? "-juvenile" : Underside ? "-underside" : "-mature") + ".png"));
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok) throw new IOException($"SavePng returned {error}");
            GD.Print($"SPECIMEN_OK {path} triangles={mesh.TriangleCount} leaves={mesh.Leaves.Count} tier={Detail} lighting={Lighting}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr("SPECIMEN_FAILED " + ex);
            GetTree().Quit(1);
        }
    }
}
