using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Persistence;
using System.Text.Json;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Water;

/// <summary>Game water owner. Ecology receives derived samples; rendering receives only 3D parcels.</summary>
public sealed class SurfaceLiquid
{
    private readonly VivariumWorld _w;
    private readonly Dictionary<(int,int),double> _ground=new();
    private readonly Dictionary<(int,int),List<FluidParticle>> _columns=new();
    private int _terrain=-1,_props=-1;
    private long _indexed=-1;
    private long _publishedVersion;
    private FluidState? _published;
    private Task<(FluidState State,WaterBudget Delta,double Ms,LiquidStepProfile Profile)>? _job;
    public double LastJobMilliseconds {get;private set;}
    public LiquidStepProfile LastStepProfile {get;private set;}
    private double _queuedSeconds;
    private double _erosionTime;
    private sealed record SolidGeometry(Heightfield Terrain,PropSet Props,PilotTreeState? PilotTree,int TerrainVersion,int PropVersion);
    private SolidGeometry? _geometry;
    public bool AsyncMode {get;set;}
    public long Version=>AsyncMode?_publishedVersion:Dynamics.Version;
    public double PresentationTime=>AsyncMode?(_published?.Time??0):Dynamics.Time;
    public LiquidDynamics Dynamics {get;}
    public SurfaceErosion Erosion {get;}
    public bool Enabled {get;private set;}
    public IReadOnlyList<FluidParticle> Particles=>AsyncMode?(_published?.Particles??Array.Empty<FluidParticle>()):Dynamics.Particles;
    public double Volume=>AsyncMode&&_published!=null?_published.Particles.Sum(p=>p.Volume)+_published.SourceRemainders.Values.Sum():Dynamics.Volume;
    public SurfaceLiquid(VivariumWorld w){_w=w;Dynamics=new(w.Domain,Ground,GroundSample);Erosion=new(w);}
    public void Enable(FluidState? saved=null)
    {
        if(Enabled)return;
        if(saved!=null)Dynamics.Restore(saved);
        else foreach(int c in _w.Grid.DomainCells)
        {
            double volume=_w.Water.Depth[c]*_w.Water.CellArea;
            if(volume>0){var p=_w.Grid.CellCenter(c);Dynamics.Add(Vec3.FromXZ(p,Ground(p)+LiquidDynamics.Radius(LiquidDynamics.ParcelVolume)),Vec3.Zero,volume);}
        }
        Enabled=true;Dynamics.WaterTable=_w.Water.WaterTable;_w.Water.Liquid=this;
        _published=Dynamics.Export();_publishedVersion++;_erosionTime=Dynamics.Time;
        SampleEcology();
    }
    public void Advance(double realSeconds)
    {
        if(!Enabled)return;
        if(_job?.IsCompleted==true)CompletePending();
        if(_w.Clock.Paused)return;
        Dynamics.WaterTable=_w.Water.WaterTable;
        // Fluid has its own persisted physical clock. 168x biological/calendar acceleration must not
        // turn a faucet into a high speed spray. User speed controls still apply; pause stops all inflow.
        if(!AsyncMode)
        {
            double remaining=realSeconds*_w.Clock.SpeedMultiplier;
            if(!double.IsFinite(remaining)||remaining<=0)return;
            while(remaining>1e-12)
            {double slice=Math.Min(remaining,8*LiquidDynamics.StepSeconds);remaining-=slice;
                Dynamics.Advance(slice,_w.Water.Springs,_w.Water.Budget,
                    _w.Water.Config.Evaporation/SimUnits.Day,_w.Water.Config.Infiltration/SimUnits.Day);
                ObserveErosion(Dynamics.Particles,Dynamics.Time);}
            SampleEcology();return;
        }
        _queuedSeconds+=realSeconds*_w.Clock.SpeedMultiplier;
        if(_job!=null||_queuedSeconds+1e-12<LiquidDynamics.StepSeconds)return;
        CaptureSolidGeometry();
        // A little headroom amortizes the frame-boundary publication delay when the solver is
        // catching up. Typical jobs still consume just the currently queued fixed steps.
        double seconds=Math.Min(_queuedSeconds,8*LiquidDynamics.StepSeconds);_queuedSeconds-=seconds;
        var sources=_w.Water.Springs.Select(s=>new Spring{Id=s.Id,X=s.X,Z=s.Z,Discharge=s.Discharge}).ToArray();
        double evaporation=_w.Water.Config.Evaporation/SimUnits.Day,infiltration=_w.Water.Config.Infiltration/SimUnits.Day;
        _job=Task.Run(()=>{var timer=System.Diagnostics.Stopwatch.StartNew();var budget=new WaterBudget();Dynamics.Advance(seconds,sources,budget,evaporation,infiltration);return(Dynamics.Export(),budget,timer.Elapsed.TotalMilliseconds,Dynamics.LastStepProfile);});
    }
    private void CaptureSolidGeometry()
    {
        if(_geometry!=null&&_geometry.TerrainVersion==_w.Terrain.Version&&_geometry.PropVersion==_w.Props.Version)return;
        var props=_geometry?.PropVersion==_w.Props.Version?_geometry.Props:
            JsonSerializer.Deserialize<PropSet>(JsonSerializer.Serialize(_w.Props,WorldSerializer.Json),WorldSerializer.Json)!;
        var terrain=_w.Terrain.Snapshot();
        _geometry=new(terrain,props,_w.PilotTree,_w.Terrain.Version,_w.Props.Version);
    }
    public void CompletePending()
    {
        if(_job==null)return;var result=_job.GetAwaiter().GetResult();_job=null;
        _published=result.State;_publishedVersion++;
        LastJobMilliseconds=result.Ms;
        LastStepProfile=result.Profile;
        var b=_w.Water.Budget;var d=result.Delta;
        b.SpringInflow+=d.SpringInflow;b.GroundwaterRecharge+=d.GroundwaterRecharge;b.BoundaryOutflow+=d.BoundaryOutflow;
        b.Evaporation+=d.Evaporation;b.Infiltration+=d.Infiltration;
        ObserveErosion(result.State.Particles,result.State.Time);SampleEcology();
    }
    private void ObserveErosion(IReadOnlyList<FluidParticle> particles,double time)
    {double elapsed=Math.Max(0,time-_erosionTime);_erosionTime=time;Erosion.Advance(particles,elapsed);}
    public FluidState CaptureState()
    {
        CompletePending();var state=Dynamics.Export();return state with {Remainder=state.Remainder+_queuedSeconds};
    }
    private double Ground(Vec2 p)=>GroundSample(p).Height;
    private LiquidGroundSample GroundSample(Vec2 p)
    {
        var geometry=AsyncMode?_geometry:null;
        int terrainVersion=geometry?.TerrainVersion??_w.Terrain.Version,propsVersion=geometry?.PropVersion??_w.Props.Version;
        if(_terrain!=terrainVersion||_props!=propsVersion)
        {_ground.Clear();_terrain=terrainVersion;_props=propsVersion;}
        // Cache solid geometry at 12.5 mm, not water. Resolve small basin lips as well as the finer
        // parcels; nothing from this cache reaches the renderer.
        const double step=.0125;int x=(int)Math.Floor(p.X/step),z=(int)Math.Floor(p.Z/step);
        double u=p.X/step-x,v=p.Z/step-z;
        double At(int a,int b)
        {var key=(a,b);if(!_ground.TryGetValue(key,out var h))
            {var q=new Vec2(a*step,b*step);
                if(geometry==null)h=_w.GroundHeight(q);
                else{h=geometry.Terrain.Height(q);double top=geometry.Props.PropTopAt(q),root=geometry.PilotTree?.RootSurfaceHeight(q,geometry.Terrain)??double.NaN;
                    if(!double.IsNaN(top))h=Math.Max(h,top);if(!double.IsNaN(root))h=Math.Max(h,root);}
                _ground[key]=h;}return h;}
        double a=At(x,z),b=At(x+1,z),c=At(x,z+1),d=At(x+1,z+1);
        return new((a*(1-u)+b*u)*(1-v)+(c*(1-u)+d*u)*v,
            ((b-a)*(1-v)+(d-c)*v)/step,((c-a)*(1-u)+(d-b)*u)/step);
    }
    private void Index()
    {
        if(_indexed==Version)return;_columns.Clear();
        foreach(var p in Particles){var key=((int)Math.Floor(p.Position.X/.05),(int)Math.Floor(p.Position.Z/.05));if(!_columns.TryGetValue(key,out var list))_columns[key]=list=new();list.Add(p);}
        _indexed=Version;
    }
    public double SurfaceAt(Vec2 p)
    {
        Index();int x=(int)Math.Floor(p.X/.05),z=(int)Math.Floor(p.Z/.05);double top=double.NegativeInfinity;
        for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
        {if(!_columns.TryGetValue((x+dx,z+dz),out var list))continue;
            foreach(var f in list){double r=1.3*Math.Cbrt(f.Volume),d2=(f.Position.XZ-p).LengthSq;if(d2>=r*r)continue;
                double surface=f.Position.Y+LiquidDynamics.Radius(f.Volume)*Math.Sqrt(1-d2/(r*r));top=Math.Max(top,surface);}}
        return double.IsNegativeInfinity(top)?double.NaN:top;
    }
    public double DepthAt(Vec2 p){double surface=SurfaceAt(p);return double.IsNaN(surface)?0:Math.Max(0,surface-_w.Terrain.Height(p));}
    public double Add(Vec2 p,double radius,double volume)
    {if(volume<=0||!_w.Domain.Contains(p))return 0;CompletePending();CaptureSolidGeometry();Dynamics.Add(Vec3.FromXZ(p,Ground(p)+.04),Vec3.Zero,volume);_w.Water.Budget.ToolInflow+=volume;_published=Dynamics.Export();_publishedVersion++;SampleEcology();return volume;}
    public double Remove(Vec2 p,double radius,double volume)
    {CompletePending();double removed=Dynamics.Remove(p,radius,volume);_w.Water.Budget.ToolRemoval+=removed;_published=Dynamics.Export();_publishedVersion++;SampleEcology();return removed;}
    private void SampleEcology()
    {
        var h=_w.Water;Array.Clear(h.Depth);Array.Clear(h.FlowX);Array.Clear(h.FlowZ);
        Array.Clear(h.FaceFlowEast);Array.Clear(h.FaceFlowNorth);
        foreach(var f in Particles){int c=_w.Grid.CellAt(f.Position.XZ);if(c<0||!_w.Grid.InDomain(c))continue;
            h.Depth[c]+=f.Volume/h.CellArea;
            // Legacy habitat queries expect discharge (m3/s), i.e. velocity times the
            // cell's wetted cross-section, rather than velocity times cell volume.
            h.FlowX[c]+=f.Velocity.X*f.Volume/h.Grid.CellSize;h.FlowZ[c]+=f.Velocity.Z*f.Volume/h.Grid.CellSize;}
        h.InvalidateWaterDistance();
    }
}
