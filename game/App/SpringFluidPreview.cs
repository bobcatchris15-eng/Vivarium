using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using Vivarium.Sim.Water;
namespace Vivarium.Game.App;
/// <summary>Production renderer review of point-source fluid arriving, pooling and overflowing.</summary>
public partial class SpringFluidPreview : Node3D
{
    public ContentLibrary Content { get; set; }=null!;
    public string OutDir { get; set; }="";
    public override void _Ready()=>_=Capture();
    private async Task Capture()
    {
        try
        {
            GetWindow().Size=new Vector2I(1280,800);Directory.CreateDirectory(OutDir);
            var d=Content.PresetOrThrow("default");
            d.PilotTreeId="none";d.Placement.Rocks=0;d.Placement.Logs=0;d.Placement.GravelPatches=0;
            d.Water.Springs.Clear();d.Water.Evaporation=0;d.Water.Infiltration=0;
            var w=VivariumWorld.Create(Content,d,populate:false);
            var session=new GameSession();AddChild(session);
            session.Initialize(Content,new UserSettings{Transient=true,AutosaveEnabled=false,ShowHelpOnStart=false,Quality=1});
            session.StartWorld(w);session.Ui.Visible=false;session.CameraRig.ProcessMode=ProcessModeEnum.Disabled;w.Clock.Paused=true;
            var source=new Vec2(-2.047,-1.053);
            session.CameraRig.LookAtPoint(new Vector3(7,7,9),new Vector3(0,.2f,0));
            async Task Shot(string name)
            {
                session.Water.Rebuild();
                for(int i=0;i<8;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(OutDir,name+".png"));
            }
            await Shot("00-dry");
            w.Water.Springs.Add(new Spring{X=source.X,Z=source.Z,Discharge=.006});
            double elapsed=0;
            foreach(double end in new[]{10d,120d,600d})
            {
                while(elapsed<end)
                {
                    double dt=Math.Min(2,end-elapsed);w.Water.Step(dt);elapsed+=dt;
                    if(((int)elapsed)%10==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                await Shot($"{end:000}-seconds");
                GD.Print($"SPRING_FLUID_FRAME seconds={elapsed} volume={w.Water.Volume()} supplied={w.Water.Budget.SpringInflow} outflow={w.Water.Budget.BoundaryOutflow}");
            }
            session.CameraRig.LookAtPoint(new Vector3(-.1f,3.5f,2),new Vector3((float)source.X,(float)w.Terrain.Height(source),(float)source.Z));
            await Shot("spring-close");
            GD.Print("VIVARIUM_SPRING_FLUID_PREVIEW_OK "+OutDir);GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("SPRING_FLUID_PREVIEW_FAILED "+e);GetTree().Quit(1);}
    }
}
