using Vivarium.Sim.Core;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Water;

public readonly record struct FluidParticle(Vec3 Position, Vec3 Velocity, double Volume, ulong Source);
public sealed record FluidState(FluidParticle[] Particles, Dictionary<ulong,double> SourceRemainders, double Time, double Remainder);

/// <summary>Position-based incompressible fluid: free 3D motion, volume-weighted pressure and solid contacts.</summary>
public sealed class ParticleFluid
{
    public const double NominalVolume=0.000015;
    public const double KernelRadius=.038;
    public const double FixedStep=1.0/120;
    private readonly List<FluidParticle> _particles=new();
    private readonly Dictionary<ulong,double> _sourceRemainders=new();
    private readonly Dictionary<(int,int,int),List<int>> _hash=new();
    private readonly List<List<int>> _bins=new();
    private Vec3[] _old=Array.Empty<Vec3>(),_p=Array.Empty<Vec3>(),_correction=Array.Empty<Vec3>();
    private double[] _lambda=Array.Empty<double>();
    private int _usedBins;
    public IReadOnlyList<FluidParticle> Particles=>_particles;
    public double Time {get;private set;}
    public double Remainder {get;private set;}
    public long Version {get;private set;}
    public Func<Vec3,double,Vec3>? ProjectSolid {get;set;}
    private readonly HexDomain _domain;
    private readonly Func<Vec2,double> _ground;
    public ParticleFluid(HexDomain domain,Func<Vec2,double> ground){_domain=domain;_ground=ground;}
    public double Volume=>_particles.Sum(p=>p.Volume)+_sourceRemainders.Values.Sum();
    public static double Radius(double volume)=>.42*Math.Cbrt(volume);
    public static double Kernel(double r2)
    {
        double h2=KernelRadius*KernelRadius;
        if(r2>=h2)return 0;
        double q=h2-r2;
        return 315.0/(64*Math.PI*Math.Pow(KernelRadius,9))*q*q*q;
    }
    private static Vec3 Gradient(Vec3 d)
    {
        double r=d.Length;if(r<1e-8||r>=KernelRadius)return Vec3.Zero;
        double q=KernelRadius-r;
        return d*(-45/(Math.PI*Math.Pow(KernelRadius,6))*q*q/r);
    }
    public void Add(Vec3 position,Vec3 velocity,double volume,ulong source=0)
    {
        if(volume<=0)return;
        int count=Math.Max(1,(int)Math.Ceiling(volume/NominalVolume));
        double v=volume/count,spacing=Math.Cbrt(v);
        int width=(int)Math.Ceiling(Math.Cbrt(count));
        for(int i=0;i<count;i++)
        {
            var offset=new Vec3((i%width-(width-1)*.5)*spacing,(i/(width*width))*spacing,(i/width%width-(width-1)*.5)*spacing);
            var p=position+offset;
            if(ProjectSolid!=null)p=ProjectSolid(p,Radius(v));
            _particles.Add(new(p,velocity,v,source));
        }
        Version++;
    }
    public void Advance(double seconds,IReadOnlyList<Spring> springs,WaterBudget budget,double evaporation,double infiltration)
    {
        if(!double.IsFinite(seconds)||seconds<=0)return;
        Remainder+=seconds;
        while(Remainder+1e-12>=FixedStep)
        {
            Tick(springs,budget,evaporation,infiltration);
            Remainder=Math.Max(0,Remainder-FixedStep);Time+=FixedStep;
        }
    }
    private void Tick(IReadOnlyList<Spring> springs,WaterBudget budget,double evaporation,double infiltration)
    {
        const double dt=FixedStep;
        foreach(var s in springs)
        {
            double volume=s.Discharge*dt;
            budget.SpringInflow+=volume;
            ulong key=s.Id.Value;
            double pending=_sourceRemainders.GetValueOrDefault(key)+volume;
            while(pending>=NominalVolume)
            {
                double r=Radius(NominalVolume);
                // Exact X/Z placement; the first parcel starts at the ground, pushed upward by spring pressure.
                var p=Vec3.FromXZ(s.Position,_ground(s.Position)+r+.0005);
                _particles.Add(new(p,new Vec3(0,.18,0),NominalVolume,key));pending-=NominalVolume;
            }
            _sourceRemainders[key]=pending;
        }
        int n=_particles.Count;
        if(n==0){Version++;return;}
        if(_p.Length<n)
        {
            int capacity=Math.Max(n,_p.Length*2+128);
            _p=new Vec3[capacity];_old=new Vec3[capacity];_correction=new Vec3[capacity];_lambda=new double[capacity];
        }
        for(int i=0;i<n;i++)
        {
            var f=_particles[i];_old[i]=f.Position;
            _p[i]=f.Position+(f.Velocity-new Vec3(0,9.81*dt,0))*dt;
            if(ProjectSolid!=null)_p[i]=ProjectSolid(_p[i],Radius(f.Volume));
        }
        for(int iteration=0;iteration<4;iteration++)
        {
            Hash(n);
            for(int i=0;i<n;i++)
            {
                double density=0,grad2=0;Vec3 grad=Vec3.Zero;
                foreach(int j in Neighbours(_p[i]))
                {
                    var d=_p[i]-_p[j];double v=_particles[j].Volume;
                    density+=v*Kernel(d.LengthSq);
                    if(i==j)continue;
                    var g=Gradient(d)*v;grad+=g;grad2+=g.LengthSq;
                }
                // Pressure resists compression; it does not pin the surface to a field height.
                _lambda[i]=-Math.Max(0,density-1)/(grad2+grad.LengthSq+100);
            }
            for(int i=0;i<n;i++)
            {
                Vec3 delta=Vec3.Zero;
                foreach(int j in Neighbours(_p[i]))
                {
                    if(i==j)continue;
                    var d=_p[i]-_p[j];double r=d.Length;
                    delta+=Gradient(d)*((_lambda[i]+_lambda[j])*_particles[j].Volume);
                    // Weak surface tension keeps adjacent droplets connected without creating remote water.
                    if(r>.022&&r<KernelRadius)delta-=d*(.0008*(r-.022)/(r*KernelRadius));
                }
                double len=delta.Length;if(len>.004)delta*=.004/len;
                _correction[i]=delta;
            }
            for(int i=0;i<n;i++)
            {
                _p[i]+=_correction[i];
                if(ProjectSolid!=null)_p[i]=ProjectSolid(_p[i],Radius(_particles[i].Volume));
            }
        }
        Hash(n);
        for(int i=0;i<n;i++)
        {
            var velocity=(_p[i]-_old[i])/dt;
            Vec3 viscosity=Vec3.Zero;double weights=0;
            foreach(int j in Neighbours(_p[i]))
            {
                if(j==i)continue;
                double weight=_particles[j].Volume*Kernel((_p[i]-_p[j]).LengthSq);
                viscosity+=((_p[j]-_old[j])/dt-velocity)*weight;weights+=weight;
            }
            velocity+=viscosity*(.06/Math.Max(1,weights));
            if(velocity.Length>6)velocity*=6/velocity.Length;
            _particles[i]=_particles[i] with {Position=_p[i],Velocity=velocity};
        }
        for(int i=n-1;i>=0;i--)
        {
            var f=_particles[i];
            if(!_domain.Contains(f.Position.XZ)){budget.BoundaryOutflow+=f.Volume;_particles.RemoveAt(i);continue;}
            if(f.Position.Y<_ground(f.Position.XZ)-.2){throw new InvalidOperationException("Fluid passed through ground");}
            double exposedArea=Math.Pow(f.Volume,2.0/3.0);
            double ev=Math.Min(f.Volume,evaporation*dt*exposedArea);
            double inf=f.Position.Y<_ground(f.Position.XZ)+Radius(f.Volume)*1.8?Math.Min(f.Volume-ev,infiltration*dt*exposedArea):0;
            budget.Evaporation+=ev;budget.Infiltration+=inf;
            double remaining=f.Volume-ev-inf;
            if(remaining<1e-10){budget.Evaporation+=remaining;_particles.RemoveAt(i);}
            else _particles[i]=f with {Volume=remaining};
        }
        Version++;
    }
    private (int,int,int) Key(Vec3 p)=>((int)Math.Floor(p.X/KernelRadius),(int)Math.Floor(p.Y/KernelRadius),(int)Math.Floor(p.Z/KernelRadius));
    private void Hash(int n)
    {
        _hash.Clear();_usedBins=0;
        for(int i=0;i<n;i++)
        {
            var key=Key(_p[i]);
            if(!_hash.TryGetValue(key,out var bin))
            {
                if(_usedBins==_bins.Count)_bins.Add(new List<int>(8));
                bin=_bins[_usedBins++];bin.Clear();_hash.Add(key,bin);
            }
            bin.Add(i);
        }
    }
    private IEnumerable<int> Neighbours(Vec3 p)
    {
        var(x,y,z)=Key(p);
        for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            if(_hash.TryGetValue((x+dx,y+dy,z+dz),out var bin))foreach(int i in bin)yield return i;
    }
    public double Remove(Vec2 centre,double radius,double amount)
    {
        double removed=0;
        for(int i=_particles.Count-1;i>=0&&removed<amount;i--)
        {
            var p=_particles[i];if(Vec2.Distance(p.Position.XZ,centre)>radius)continue;
            double take=Math.Min(p.Volume,amount-removed);removed+=take;
            if(take>=p.Volume)_particles.RemoveAt(i);else _particles[i]=p with {Volume=p.Volume-take};
        }
        Version++;return removed;
    }
    public FluidState Export()=>new(_particles.ToArray(),new(_sourceRemainders),Time,Remainder);
    public void Restore(FluidState state)
    {
        _particles.Clear();_particles.AddRange(state.Particles);
        _sourceRemainders.Clear();foreach(var(k,v)in state.SourceRemainders)_sourceRemainders[k]=v;
        Time=state.Time;Remainder=state.Remainder;Version++;
    }
}
