using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Water;

public sealed record SurfaceErosionState(SortedDictionary<int,double> Cut,
    SortedDictionary<int,double> Pending,double Seconds,double RemovedVolume,
    SortedDictionary<int,double> Exposure);

/// <summary>A finite erodible soil veneer, driven exclusively by bed-contact liquid parcels.
/// These vertex budgets describe solid terrain, never a second water representation.</summary>
public sealed class SurfaceErosion
{
    public bool Enabled {get;set;}=true;
    public const double MaximumCut=.03;
    public const double MaximumRate=.00012; // 7.2 mm per physical minute, at full exposure.
    public const double MinimumSpeed=.12;
    public const double EditInterval=10; // At most 1.2 mm per edit; avoid rebuilding the whole island every few frames.
    public const double MaximumExposure=600; // Ten physical minutes from a patch's first scour.
    private readonly VivariumWorld _w;
    private readonly SortedDictionary<int,double> _cut=new(),_pending=new();
    private readonly SortedDictionary<int,double> _exposure=new();
    private double _seconds;
    public double RemovedVolume {get;private set;}
    public double DeepestCut=>_cut.Count==0?0:_cut.Values.Max();
    public int WornVertices=>_cut.Count;
    public SurfaceErosion(VivariumWorld world)=>_w=world;
    public SurfaceErosionState? Capture()=>_cut.Count==0&&_pending.Count==0&&_exposure.Count==0&&_seconds==0?null:new(new(_cut),new(_pending),_seconds,RemovedVolume,new(_exposure));
    public void Restore(SurfaceErosionState state)
    {
        if(!double.IsFinite(state.Seconds)||state.Seconds<0||state.Seconds>=EditInterval+1e-8||
            !double.IsFinite(state.RemovedVolume)||state.RemovedVolume<0)throw new InvalidDataException("Invalid erosion clock or volume");
        void Read(SortedDictionary<int,double> source,SortedDictionary<int,double> target,double maximum)
        {target.Clear();foreach(var (k,v) in source){if(k<0||k>=_w.Terrain.H.Length||!double.IsFinite(v)||v<0||v>maximum+1e-8)
            throw new InvalidDataException("Invalid terrain erosion budget");target.Add(k,v);}}
        Read(state.Cut,_cut,MaximumCut);Read(state.Pending,_pending,MaximumRate*EditInterval);
        Read(state.Exposure,_exposure,MaximumExposure);
        _seconds=state.Seconds;RemovedVolume=state.RemovedVolume;
    }
    private double SoilFactor(Vec2 p)
    {
        if(!_w.Domain.Contains(p)||_w.Terrain.Height(p)<=_w.Water.WaterTable||
            _w.Props.RockAt(p)!=null||_w.Props.LogAt(p)!=null||_w.PilotTree?.BlocksDisc(p)==true)return 0;
        var substrate=(Substrate)_w.Fields.BaseSubstrate.Get(p);
        if(substrate is Substrate.Rock or Substrate.Wood)return 0;
        return _w.Props.GravelAt(p)!=null||substrate==Substrate.Gravel?.25:1;
    }
    /// <summary>Observe elapsed physical time once, after a liquid job publishes. Edits are batched.
    /// Fine removed soil is counted as washed-away sediment; this approximation has no deposition solver.</summary>
    public int Advance(IReadOnlyList<FluidParticle> particles,double seconds)
    {
        if(!Enabled||!double.IsFinite(seconds)||seconds<=0)return 0;
        // The owner publishes at most eight fixed steps. Bound long direct calls using the same
        // exposure snapshot without letting their rate exceed the per-second ceiling.
        int changed=0;
        while(seconds>1e-10)
        {
            double dt=Math.Min(seconds,EditInterval-_seconds);seconds-=dt;
            var hf=_w.Terrain;var work=new Dictionary<int,double>();
            foreach(var particle in particles)
            {
                if(particle.Volume<=1e-12)continue; // Sub-nanolitre numerical tails cannot scour soil.
                var p=particle.Position.XZ;if(!_w.Domain.Contains(p))continue;
                double bed=hf.Height(p);var normal=hf.Normal(p);
                double separation=(particle.Position.Y-bed)*normal.Y;
                if(separation>SurfaceFluidDynamics.Radius(particle.Volume)+.005||separation<-.003)continue;
                var tangent=particle.Velocity-normal*particle.Velocity.Dot(normal);
                double speed2=tangent.LengthSq;if(speed2<=MinimumSpeed*MinimumSpeed||SoilFactor(p)==0)continue;
                double exposure=particle.Volume*Math.Min(4,(speed2-MinimumSpeed*MinimumSpeed)/(.45*.45));
                int x=(int)Math.Floor((p.X-hf.OriginX)/hf.Step),z=(int)Math.Floor((p.Z-hf.OriginZ)/hf.Step);
                if(x<0||z<0||x>=hf.Nx-1||z>=hf.Nz-1)continue;
                double u=(p.X-hf.OriginX)/hf.Step-x,v=(p.Z-hf.OriginZ)/hf.Step-z;
                void Add(int a,int b,double weight)
                {if(weight<=0)return;int k=hf.VertexIndex(a,b);work[k]=work.GetValueOrDefault(k)+exposure*weight;}
                // Match the terrain's actual triangles, so only the contacted face wears down.
                Add(x,z,1-Math.Max(u,v));Add(x+1,z+1,Math.Min(u,v));
                if(u>=v)Add(x+1,z,u-v);else Add(x,z+1,v-u);
            }
            foreach(var (k,value) in work)
            {
                var point=hf.VertexPos(k%hf.Nx,k/hf.Nx);double factor=SoilFactor(point);
                if(factor==0||_cut.GetValueOrDefault(k)>=MaximumCut||_exposure.GetValueOrDefault(k)>=MaximumExposure)continue;
                _exposure.TryAdd(k,0);
                double active=Math.Min(dt,MaximumExposure-_exposure.GetValueOrDefault(k));
                double intensity=Math.Min(1,value/(hf.Step*hf.Step*.003));
                double allowance=MaximumCut-_cut.GetValueOrDefault(k)-_pending.GetValueOrDefault(k);
                if(allowance>0)_pending[k]=_pending.GetValueOrDefault(k)+Math.Min(allowance,MaximumRate*intensity*factor*active);
            }
            // A finite weathering window, including intermittent/dry intervals after first scour.
            // Counting only contact would leave rarely touched fringes making tiny edits for hours.
            foreach(int k in _exposure.Keys.ToArray())_exposure[k]=Math.Min(MaximumExposure,_exposure[k]+dt);
            _seconds+=dt;
            if(_seconds+1e-10<EditInterval)continue;
            _seconds=0;
            if(_pending.Count==0)continue;
            _w.Scheduler.CompletePendingTick();
            int batchChanged=0;
            foreach(var (k,amount) in _pending)
            {
                var point=hf.VertexPos(k%hf.Nx,k/hf.Nx);if(SoilFactor(point)==0)continue;
                double cut=Math.Min(amount,Math.Max(0,hf.H[k]-Math.Max(hf.MinHeight,_w.Water.WaterTable)));
                if(cut<=0)continue;
                hf.BeginEdit();hf.H[k]-=cut;_cut[k]=Math.Min(MaximumCut,_cut.GetValueOrDefault(k)+cut);
                RemovedVolume+=cut*hf.Step*hf.Step;changed++;batchChanged++;
            }
            _pending.Clear();
            if(batchChanged>0){hf.Touch();_w.Water.RefreshBed(hf);_w.Fields.MarkLightStale();}
        }
        return changed;
    }
}
