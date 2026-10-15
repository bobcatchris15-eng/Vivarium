using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Game.App;

/// <summary>Isolated review with the normal GameSession, fluid solver, pond, terrain and liquid renderer.</summary>
public partial class LiquidReview : Node3D
{
    public ContentLibrary Content {get;set;}=null!;
    public string OutDir {get;set;}="";
    public string? SavePath {get;set;}
    public bool Live {get;set;}
    public bool Motion {get;set;}
    public bool Pool {get;set;}
    public bool Erosion {get;set;}
    public override void _Ready()=>_=Run();
    private async Task Run()
    {
        try
        {
            Directory.CreateDirectory(OutDir);GetWindow().Size=new Vector2I(1280,800);
            VivariumWorld w;
            if(SavePath!=null){var loaded=SaveSystem.Load(Content,SavePath);if(!loaded.Ok)throw new Exception(loaded.Message);w=loaded.World!;}
            else
            {
                var d=Content.PresetOrThrow("default");d.Diameter=Pool||Erosion?5:6;if(Pool||Erosion)d.CellSize=.1;d.PilotTreeId="none";
                d.Placement.Rocks=d.Placement.Logs=d.Placement.GravelPatches=0;d.Water.WaterTable=-.10;d.Water.Evaporation=d.Water.Infiltration=0;
                d.Water.Springs.Clear();w=VivariumWorld.Create(Content,d,false);
                // A visibly enclosed 7 cm hollow, one spill lip, and a descending channel into a pond.
                var hf=w.Terrain;hf.BeginEdit();
                for(int z=0;z<hf.Nz;z++)for(int x=0;x<hf.Nx;x++)
                {var p=new Vec2(hf.OriginX+x*hf.Step,hf.OriginZ+z*hf.Step);
                    if(Erosion)hf.H[z*hf.Nx+x]=.2-.3*p.X+.04*p.Z*p.Z;
                    else if(Pool)
                    {
                        static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
                        double sill=p.X>0?0:.035;
                        double east=-.045+(sill+.045)*Smooth((Math.Abs(p.X)-.10)/.10);
                        double side=-.045+.080*Smooth((Math.Abs(p.Z)-.10)/.10);
                        if(p.X>.20&&Math.Abs(p.Z)<.10)east=-.6*(p.X-.20);
                        hf.H[z*hf.Nx+x]=p.X>.20&&Math.Abs(p.Z)<.10?east:Math.Max(east,side);
                    }
                    else hf.H[z*hf.Nx+x]=-.07*Math.Max(0,1-p.LengthSq/.04)-.5*Math.Max(0,p.X-.25);}
                hf.Touch();w.Water.RefreshBed(hf);Array.Clear(w.Water.Depth);w.Water.Budget=new();
                w.Water.Springs.Add(new Spring{Id=w.Ids.Next(EntityKind.Spring),X=0,Z=0,Discharge=(Pool?500:2000.0)/1000/3600});
            }
            if(Pool||Erosion)
            {
                // Bare earth makes the rising meniscus readable in the isolated basin fixture.
                // Player-world captures keep their real vegetation and habitat intact.
                Array.Clear(w.AmbientGroundCover.Cover);Array.Fill(w.Fields.Moisture.Values,.35);
                if(Erosion)Array.Fill(w.Fields.BaseSubstrate.Values,(byte)Substrate.Soil);
            }
            w.Clock.Paused=true;w.Liquid.Enable();
            var session=new GameSession();AddChild(session);
            session.Initialize(Content,new UserSettings{Transient=true,AutosaveEnabled=false,ShowHelpOnStart=false,Quality=1});
            session.StartWorld(w);session.Ui.Visible=false;session.Overlay.Visible=false;session.Tools.SetProcess(false);
            if(Pool||Erosion){session.AmbientGround.Visible=false;session.SoilDetail.Visible=false;session.Coverage.Visible=false;}
            w.Liquid.AsyncMode=false;
            session.CameraRig.KeyboardBlocked=true;session.CameraRig.SetProcessUnhandledInput(false);
            session.EnvRig.BenchmarkLightSeconds=60;
            var source=w.Water.Springs.Last();var at=source.Position;double bed=w.GroundHeight(at);
            session.CameraRig.LookAtPoint(new Vector3((float)at.X-.52f,(float)bed+.45f,(float)at.Z+.64f),new Vector3((float)at.X+.15f,(float)bed+.015f,(float)at.Z));
            if(Pool)session.CameraRig.LookAtPoint(new Vector3(.33f,.11f,.25f),new Vector3(.04f,-.01f,0));
            if(Erosion)session.CameraRig.LookAtPoint(new Vector3(.85f,.55f,.72f),new Vector3(.36f,.075f,0));
            if(Live)
            {
                // Asset/LOD construction is startup work. Review steady fluid behaviour after the
                // production flora has finished, and keep PNG compression off the frame thread.
                session.CameraRig.LookAtPoint(new Vector3((float)at.X+.28f,(float)bed+.20f,(float)at.Z+.28f),new Vector3((float)at.X,(float)bed+.045f,(float)at.Z));
                for(int i=0;i<3600&&!session.Flora.PopulationReady;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                GD.Print("LIQUID_LIVE_READY flora="+session.Flora.PopulationReady);
                w.Clock.BaseRate=0;w.Clock.Paused=false;w.Liquid.AsyncMode=true;
                var timer=System.Diagnostics.Stopwatch.StartNew();long frames=0;double next=0;Task? encoding=null;int shot=0;
                int firstFrame=Engine.GetFramesDrawn();long firstSurface=session.WaterFluid.SurfaceUpdates;FrameProfiler.Reset();
                double startTime=w.Liquid.PresentationTime,startInflow=w.Water.Budget.SpringInflow,startPond=w.Water.Budget.GroundwaterRecharge,startVolume=w.Water.Volume();
                var liveRecords=new List<object>();
                while(timer.Elapsed.TotalSeconds<(Motion?20:45))
                {
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);frames++;
                    if(timer.Elapsed.TotalSeconds<next)continue;next=timer.Elapsed.TotalSeconds+(Motion?.05:.2);
                    if(encoding?.IsCompleted==false)continue;
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    var captured=GetViewport().GetTexture().GetImage();string output=Path.GetFullPath(Path.Combine(OutDir,$"live-{++shot:000}."+(Motion?"jpg":"png")));
                    encoding=Task.Run(()=>{if(Motion)captured.SaveJpg(output,.92f);else captured.SavePng(output);captured.Dispose();});
                    if(shot%20==0){GD.Print($"LIQUID_LIVE t={timer.Elapsed.TotalSeconds:F1} fluid_time={w.Liquid.PresentationTime-startTime:F2} fps={(Engine.GetFramesDrawn()-firstFrame)/timer.Elapsed.TotalSeconds:F1} particles={w.Liquid.Particles.Count} job_ms={w.Liquid.LastJobMilliseconds:F1}");

                        liveRecords.Add(new{wall=timer.Elapsed.TotalSeconds,fluid=w.Liquid.PresentationTime-startTime,fps=(Engine.GetFramesDrawn()-firstFrame)/timer.Elapsed.TotalSeconds,particles=w.Liquid.Particles.Count,jobMs=w.Liquid.LastJobMilliseconds});}
                }
                w.Clock.Paused=true;w.Liquid.CompletePending();
                if(encoding!=null)await encoding;
                File.WriteAllText(Path.Combine(OutDir,"live-report.json"),System.Text.Json.JsonSerializer.Serialize(new{
                    frames,drawnFrames=Engine.GetFramesDrawn()-firstFrame,wallSeconds=timer.Elapsed.TotalSeconds,fluidSeconds=w.Liquid.PresentationTime-startTime,startVolume,endVolume=w.Water.Volume(),
                    inflow=w.Water.Budget.SpringInflow-startInflow,pond=w.Water.Budget.GroundwaterRecharge-startPond,
                    captures=shot,surfaceUpdates=session.WaterFluid.SurfaceUpdates-firstSurface,triangles=session.WaterFluid.TrianglesBuilt,
                    erosionDepth=w.Liquid.Erosion.DeepestCut,wornVertices=w.Liquid.Erosion.WornVertices,sedimentVolume=w.Liquid.Erosion.RemovedVolume,
                    samples=liveRecords,cpu=FrameProfiler.TakeStructuredReport()},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
                GD.Print("LIQUID_LIVE_OK "+OutDir);GetTree().Quit();return;
            }
            var records=new List<object>();
            GD.Print($"LIQUID_REVIEW initial_volume={w.Liquid.Dynamics.Volume:R} particles={w.Liquid.Particles.Count} source={at} bed={bed:R}");
            var watch=System.Diagnostics.Stopwatch.StartNew();
            double[] original=w.Terrain.H.ToArray();
            for(int step=0;step<=(Erosion?72000:Pool?3600:1800);step++)
            {
                if(step>0){if(Erosion){w.Clock.Paused=false;w.Liquid.Advance(SurfaceFluidDynamics.StepSeconds);w.Clock.Paused=true;}
                    else {w.Clock.Paused=false;w.Liquid.Advance(SurfaceFluidDynamics.StepSeconds);w.Clock.Paused=true;}}
                // Give geometry workers and the production scene regular frames; physics never competes with
                // a second world clock, and the player's active game/save is untouched.
                if(step%(Erosion?60:3)==0)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                if(step%(Erosion?3600:120)!=0)continue;
                for(int f=0;f<8;f++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                string image=Path.GetFullPath(Path.Combine(OutDir,$"fluid-{step/60:00}.png"));
                GetViewport().GetTexture().GetImage().SavePng(image);
                var particles=w.Liquid.Particles;
                records.Add(new{seconds=step/60.0,particles=particles.Count,volume=w.Liquid.Dynamics.Volume,inflow=w.Water.Budget.SpringInflow,
                    pond=w.Water.Budget.GroundwaterRecharge,boundary=w.Water.Budget.BoundaryOutflow,triangles=session.WaterFluid.TrianglesBuilt,
                    erosionDepth=w.Liquid.Erosion.DeepestCut,wornVertices=w.Liquid.Erosion.WornVertices,sedimentVolume=w.Liquid.Erosion.RemovedVolume,
                    aboveSource=particles.Count(p=>p.Position.Y>bed+.025),furthest=particles.Count>0?particles.Max(p=>Vec2.Distance(p.Position.XZ,at)):0});
                GD.Print($"LIQUID_FRAME t={step/60} particles={particles.Count} triangles={session.WaterFluid.TrianglesBuilt} pond={w.Water.Budget.GroundwaterRecharge:R} wall_ms={watch.ElapsedMilliseconds}");
            }
            File.WriteAllText(Path.Combine(OutDir,"report.json"),System.Text.Json.JsonSerializer.Serialize(records,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            if(Erosion)
            {
                var hf=w.Terrain;var changes=Enumerable.Range(0,hf.H.Length).Where(k=>original[k]-hf.H[k]>1e-10)
                    .Select(k=>new{x=hf.VertexPos(k%hf.Nx,k/hf.Nx).X,z=hf.VertexPos(k%hf.Nx,k/hf.Nx).Z,cut=original[k]-hf.H[k]}).ToArray();
                File.WriteAllText(Path.Combine(OutDir,"terrain-cuts.json"),System.Text.Json.JsonSerializer.Serialize(changes));
                var save=SaveSystem.Save(w,Path.Combine(OutDir,"eroded-world.vivsave"));if(!save.Ok)throw new Exception(save.Message);
                var load=SaveSystem.Load(Content,Path.Combine(OutDir,"eroded-world.vivsave"));
                if(!load.Ok||WorldSerializer.Digest(w)!=WorldSerializer.Digest(load.World!))throw new Exception("Eroded-world save round trip failed: "+load.Message);
                // Inspect the real cut geometry without the liquid concealing its bed; physics and
                // the saved state stay intact. This is a diagnostic view, not an alternative water renderer.
                session.WaterFluid.Visible=false;
                for(int f=0;f<8;f++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.GetFullPath(Path.Combine(OutDir,"eroded-bed.png")));
            }
            GD.Print("LIQUID_REVIEW_OK "+OutDir);GetTree().Quit();
        }
        catch(Exception e){GD.PrintErr("LIQUID_REVIEW_FAILED "+e);GetTree().Quit(2);}
    }
}
