using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
namespace Vivarium.Game.App;

/// <summary>Checks all seven species and more than 32 simultaneous lights in the production renderer.</summary>
public partial class NightLightingReview : Node3D
{
    public ContentLibrary Content {get;set;}=null!;
    public string OutDir {get;set;}="";
    public override void _Ready()=>_=Run();
    private async Task Run()
    {
        try
        {
            Directory.CreateDirectory(OutDir);GetWindow().Size=new Vector2I(1440,900);
            var d=Content.PresetOrThrow("default");d.Diameter=10;d.PilotTreeId="none";d.Water.Springs.Clear();d.Water.WaterTable=-.8;
            d.Placement.Rocks=d.Placement.Logs=d.Placement.GravelPatches=0;
            d.Terrain=new TerrainProfile{BaseHeight=0,Relief=0,MinHeight=-1,MaxHeight=2,Bottom=-2};
            var w=VivariumWorld.Create(Content,d,false);w.Clock.Paused=true;
            string[] species=["glowbriar","lanternsedge","siltstars","ghostbonnet","tidebell","starmoss","moonfern"];
            for(int col=0;col<7;col++)for(int row=0;row<9;row++)
            {var sp=Content.FloraOrThrow(species[col]);var plant=w.FloraSystem.Establish(sp,new Vec2((col-3)*.85,(row-4)*.58),"night light review",sp.MaxBiomass);plant.Age=sp.MaturityAge+1;}
            var session=new GameSession();AddChild(session);session.Initialize(Content,new UserSettings{Transient=true,AutosaveEnabled=false,ShowHelpOnStart=false,Quality=1});
            session.StartWorld(w);session.Ui.Visible=false;session.Overlay.Visible=false;session.Tools.SetProcess(false);
            session.CameraRig.KeyboardBlocked=true;session.CameraRig.SetProcessUnhandledInput(false);
            session.CameraRig.LookAtPoint(new Vector3(4.1f,3.9f,6.5f),new Vector3(0,.25f,0));
            session.EnvRig.CameraAttributes.AutoExposureEnabled=false;
            for(int i=0;i<1800&&!session.Flora.PopulationReady;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!session.Flora.PopulationReady)throw new Exception("Flora did not finish building");
            async Task Frames(int n){for(int i=0;i<n;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Shot(string name){await Frames(8);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(Path.Combine(OutDir,name+".png")));}
            session.EnvRig.BenchmarkLightSeconds=60;await Frames(20);await Shot("day");
            session.EnvRig.BenchmarkLightSeconds=1020;await Frames(60);
            var before=session.Flora.GetChildren().OfType<OmniLight3D>().ToDictionary(l=>l.Name.ToString(),l=>l.GetInstanceId());
            if(session.Flora.LuminousLightCount!=63)throw new Exception($"Expected all 63 emitters, got {session.Flora.LuminousLightCount}");
            await Shot("night-all-63");
            session.CameraRig.LookAtPoint(new Vector3(3.9f,3.9f,6.5f),new Vector3(0,.25f,0));await Frames(60);
            var lights=session.Flora.GetChildren().OfType<OmniLight3D>().ToArray();
            if(lights.Length!=63||lights.Any(l=>!before.TryGetValue(l.Name.ToString(),out var id)||id!=l.GetInstanceId()||!l.Visible||l.LightEnergy<=0))
                throw new Exception("Lights rotated, disappeared, or changed ownership during camera movement");
            await Shot("night-camera-moved");
            session.EnvRig.SetProcess(false);session.Flora.SetProcess(false);
            foreach(var l in lights)l.Visible=false;await Shot("night-emission-only");
            foreach(var l in lights)l.Visible=true;await Shot("night-cast-light");
            foreach(string id in new[]{"ghostbonnet","lanternsedge","siltstars","tidebell"})
            {
                var sp=Content.FloraOrThrow(id);var plant=w.Flora.Items.Where(f=>f.SpeciesId==id).OrderBy(f=>Math.Abs(f.Z)).First();
                var p=new Vector3((float)plant.X,0,(float)plant.Z);float distance=(float)Math.Max(.24,sp.RadiusAtMax*2.2);
                float height=(float)sp.Height;
                var offset=id=="ghostbonnet"?new Vector3(distance,.05f,distance):new Vector3(distance,Math.Max(.18f,height*.85f),distance);
                session.CameraRig.LookAtPoint(p+offset,p+Vector3.Up*height*.45f);
                session.Flora.SetProcess(true);await Frames(60);session.Flora.SetProcess(false);await Shot("tissue-"+id);
                foreach(var l in lights)l.Visible=false;await Shot("emission-mask-"+id);
                foreach(var l in lights)l.Visible=true;
            }
            File.WriteAllText(Path.Combine(OutDir,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{
                simultaneous=lights.Length,stableOwnership=true,minEnergy=lights.Min(l=>l.LightEnergy),maxEnergy=lights.Max(l=>l.LightEnergy),
                lights=lights.Select(l=>new{id=l.Name.ToString(),energy=l.LightEnergy,range=l.OmniRange}).ToArray()},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            GD.Print("NIGHT_LIGHTING_REVIEW_OK all=63 stable=true "+OutDir);GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("NIGHT_LIGHTING_REVIEW_FAILED "+e);GetTree().Quit(2);}
    }
}
