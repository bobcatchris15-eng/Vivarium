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
/// <summary>Repeatable botanical lighting and anatomy review using the live species materials.</summary>
public partial class BotanicalReview : Node3D
{
    public ContentLibrary Content {get;set;}=null!;
    public bool AllSpecies {get;set;}
    public string OutDir {get;set;}="";
    public override void _Ready()=>_=CaptureAsync();
    private async Task CaptureAsync()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            GetWindow().Size=new Vector2I(1600,1000);
            var rig=new EnvironmentRig(); AddChild(rig);rig.ApplyQuality(2);rig.SetProcess(false);
            rig.CameraAttributes.AutoExposureEnabled=false;
            rig.Env.TonemapExposure=1.12f;
            rig.Env.AmbientLightEnergy=.50f;
            rig.Env.AmbientLightSkyContribution=.28f;
            rig.Env.FogEnabled=false;
            rig.Sun.RotationDegrees=new Vector3(-40,-55,0);
            rig.Sun.LightEnergy=1.15f;
            var groundMat=new StandardMaterial3D {AlbedoColor=new Color(.27f,.23f,.17f),Roughness=.97f};
            groundMat.AlbedoTexture=GD.Load<Texture2D>("res://Textures/Ground048_1K-JPG_Color.jpg");
            groundMat.NormalEnabled=true;
            groundMat.NormalTexture=GD.Load<Texture2D>("res://Textures/Ground048_1K-JPG_NormalGL.jpg");
            groundMat.NormalScale=.65f;
            rig.Env.BackgroundMode=Godot.Environment.BGMode.Color;
            rig.Env.BackgroundColor=new Color(.075f,.09f,.085f);
            rig.Env.AmbientLightSource=Godot.Environment.AmbientSource.Color;
            rig.Env.AmbientLightColor=new Color(.72f,.79f,.85f);
            groundMat.Uv1Scale=new Vector3(5,5,1);
            AddChild(new MeshInstance3D {Mesh=new PlaneMesh {Size=new Vector2(18f,18f)},MaterialOverride=groundMat});
            string[] roster={"shadebell","lanternbrush","embercrown","veilfern","ringreed","mooncoin","sunstone_rosette","pitcher_plant","trifold","veilblade","glassfinger","mirrorleaf"};
            if(AllSpecies) roster=Content.Flora.Select(s=>s.Id).ToArray();
            if (System.Environment.GetEnvironmentVariable("VIVARIUM_SURFACE_REVIEW") == "1")
                roster = new[] { "velvetweave_moss", "pearl_cushion_moss",
                    "floodlace_moss", "bogglass_moss", "ruffle_lichen", "embercrust_lichen" };
            var audit=new System.Collections.Generic.List<object>();
            var holders=new System.Collections.Generic.List<(string Id,Node3D Node,float Height)>();
            for(int index=0;index<roster.Length;index++)
            {
                var sp=Content.FloraOrThrow(roster[index]);bool floating=sp.Shape=="floatleaf";
                float x=(index%4-1.5f)*1.35f, z=(index/4-1)*1.3f;
                var holder=new Node3D {Name=sp.Id,Position=new Vector3(x,.006f,z)};AddChild(holder);
                holders.Add((sp.Id,holder,(float)sp.Height));
            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, "slime_mold" => 4, _ => 2 });
            mat.SetShaderParameter("sway", 0.0f);

            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            var profile =  FloraVisualProfile.Load(sp.Id);
            FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
            // Use the same bounded geometry path as FloraRenderer, including photographed foliage.
            var mesh = sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp,seed,attached:true,0)
                : OrganismMeshes.FloraTier(sp,seed,0,tieredSource:profile != null);
            var tiers=Enumerable.Range(0,3).Select(t=>OrganismMeshes.FloraTier(sp,seed,t)).ToArray();
            audit.Add(new {species=sp.Id,sp.Shape,mesh.TriangleCount,indexedLeafCount=mesh.Leaves.Count,
                finite=mesh.Positions.All(float.IsFinite)&&mesh.Normals.All(float.IsFinite),
                trianglesByTier=tiers.Select(t=>t.TriangleCount).ToArray()});
            ShaderMaterial? leaf = profile?.LeafMaterial(animated: false);
            if (leaf != null) { leaf.SetShaderParameter("review_time",-1f); leaf.SetShaderParameter("plant_stiffness",sp.Woody != null ? 7f : 3f); }
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
                : (sp.Climber != null && !false) ? new Vector3(0.95f, 0.95f, 0.95f)
                : (sp.Climber != null && false) ? new Vector3((float)sp.RadiusAtMax, (float)sp.RadiusAtMax * 0.35f, (float)sp.RadiusAtMax)
                : (sp.Shape == "bracket") ? new Vector3((float)sp.RadiusAtMax * 1.5f, (float)sp.Height * 2.2f, (float)sp.RadiusAtMax * 1.5f)
                : new Vector3((float)sp.RadiusAtMax, (float)sp.Height, (float)sp.RadiusAtMax)),
                (sp.Shape == "bracket") ? new Vector3(0, 0.10f, 0) : Vector3.Zero));
            multimesh.SetInstanceColor(0, Colors.White);
            multimesh.SetInstanceCustomData(0, new Color(0.5f, 1.0f, 0.0f, 0.5f));
            holder.AddChild(new MultiMeshInstance3D { Name = sp.Name, Multimesh = multimesh });

            }
            var setting=new Node3D {Name="ReviewHabitat"};AddChild(setting);
            if(!AllSpecies)
            {
                var rockMat=new StandardMaterial3D {AlbedoColor=new Color(.55f,.52f,.47f),Roughness=.93f,NormalEnabled=true,NormalScale=.65f,
                    AlbedoTexture=GD.Load<Texture2D>("res://Textures/Rock058_1K-JPG_Color.jpg"),
                    NormalTexture=GD.Load<Texture2D>("res://Textures/Rock058_1K-JPG_NormalGL.jpg")};
                var rng=new Random(173);
                for(int i=0;i<15;i++)
                {
                    float x=(float)(rng.NextDouble()*5-2.5),z=(float)(rng.NextDouble()*3.5-1.8),r=(float)(.05+rng.NextDouble()*.13);
                    setting.AddChild(new MeshInstance3D {Mesh=new SphereMesh {Radius=r,Height=r*2,RadialSegments=24,Rings=12},MaterialOverride=rockMat,
                        Position=new Vector3(x,r*.24f,z),Scale=new Vector3(1.3f,.65f,.9f),Rotation=new Vector3(.1f,(float)rng.NextDouble()*6,.15f)});
                }
                foreach(var h in holders.Where(h=>h.Id is "trifold" or "glassfinger" or "mooncoin"))
                    for(int i=0;i<14;i++)
                    {
                        var clone=(Node3D)h.Node.Duplicate();setting.AddChild(clone);
                        clone.Position=new Vector3((float)(rng.NextDouble()*4.7-2.35),.007f,(float)(rng.NextDouble()*3.3-1.5));
                        clone.Rotation=new Vector3(0,(float)rng.NextDouble()*6,0);clone.Scale=Vector3.One*(float)(.65+rng.NextDouble()*.55);
                    }
            }
            var camera=new Camera3D {Current=true,Projection=Camera3D.ProjectionType.Perspective,Fov=46,Near=.015f,Far=40};AddChild(camera);
            async Task Capture(string name,Vector3 target,Vector3 position)
            {
                camera.Position=position;camera.LookAt(target);
                for(int i=0;i<24;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var err=GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(Path.Combine(OutDir,name+".png")));
                if(err!=Error.Ok)throw new IOException(err.ToString());
            }
            if(!AllSpecies) await Capture("garden",new Vector3(0,.42f,-.45f),new Vector3(3.0f,1.8f,4.0f));
            setting.Visible=false;
            foreach(var entry in holders)
            {
                foreach(var h in holders)h.Node.Visible=h.Id==entry.Id;
                var sp=Content.FloraOrThrow(entry.Id);
                var specimen=(MultiMeshInstance3D)entry.Node.GetChild(0);
                var box=specimen.GetAabb();
                float dimension=Math.Max(.08f,Math.Max(box.Size.X,Math.Max(box.Size.Y,box.Size.Z)));
                var target=entry.Node.Position+box.GetCenter();
                await Capture(entry.Id,target,target+new Vector3(.85f,.55f,1).Normalized()*dimension*2.0f);
            }
            setting.Visible=true;
            foreach(var h in holders)h.Node.Visible=true;
            rig.Sun.RotationDegrees=new Vector3(-25,145,0);rig.Sun.LightEnergy=.95f;
            if(!AllSpecies) await Capture("garden-backlit",new Vector3(0,.42f,-.45f),new Vector3(3.0f,1.8f,4.0f));
            File.WriteAllText(Path.Combine(OutDir,"roster.txt"),string.Join(System.Environment.NewLine,roster));
            File.WriteAllText(Path.Combine(OutDir,"geometry-audit.json"),System.Text.Json.JsonSerializer.Serialize(audit,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            GD.Print("BOTANICAL_REVIEW_OK "+Path.GetFullPath(OutDir));GetTree().Quit(0);
        }
        catch(Exception ex){GD.PrintErr("BOTANICAL_REVIEW_FAILED "+ex);GetTree().Quit(1);}
    }
}

