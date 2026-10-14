using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using V=System.Numerics.Vector3;
namespace Vivarium.Sim.Water;

public readonly record struct LiquidGroundSample(double Height,double SlopeX,double SlopeZ);
public readonly record struct LiquidStepProfile(int NeighbourPairs,double Contact,double Neighbours,double Constraints,double Viscosity,double Sinks);

/// <summary>Persistent 3D, volume-carrying position based liquid (Macklin/Müller 2013).
/// No terrain depth map is a fluid state or a render input. No resting or aged particle is retired.</summary>
public sealed class LiquidDynamics
{
    // Five-millilitre quanta resolve shallow pools without increasing neighbours per parcel.
    // All length-dependent constraints scale with the cube root of the carried volume.
    public const double ParcelVolume=.000005, Support=.04160167646103808, StepSeconds=1.0/60;
    private static readonly double LengthScale=Math.Cbrt(ParcelVolume/.000015);
    private static readonly double RestSpacing=Math.Cbrt(ParcelVolume);
    private static readonly float CorrectionLimit=(float)(.006*LengthScale);
    private static readonly double SearchRadiusSquared=Math.Pow(Support+.01*LengthScale,2);
    private const double H2=Support*Support;
    private static readonly double K=315/(64*Math.PI*Math.Pow(Support,9)),G=-45/(Math.PI*Math.Pow(Support,6));
    private static readonly float H2F=(float)H2,SupportF=(float)Support,KF=(float)K,GF=(float)G;
    private static readonly float CorrReference=Kernel(H2F*.09f);
    private static readonly float PressureScale=(float)(.000002*LengthScale*LengthScale),CohesionScale=(float)(.00012*LengthScale),RestSpacingF=(float)RestSpacing;
    private static readonly ParallelOptions SolverParallelism=new(){MaxDegreeOfParallelism=Math.Clamp(Environment.ProcessorCount-2,1,4)};
    private readonly List<FluidParticle> _water=new();
    private readonly Dictionary<ulong,double> _pending=new();
    private readonly Dictionary<ulong,long> _emitted=new();
    private readonly Dictionary<(int,int,int),List<int>> _hash=new();
    private readonly List<List<int>> _bins=new();
    private readonly List<List<int>> _candidatePool=new(),_particleNear=new();
    private List<int>[] _candidates=[];
    private V[] _p=[],_old=[],_delta=[],_normal=[],_contact=[],_v=[];
    private float[] _lambda=[];
    private float[] _mass=[],_pairKernel=[];
    private V[] _pairGradient=[],_solidGradient=[];
    private float[] _pairDistance=[];
    private int[] _offset=[],_count=[],_near=[];
    private int _used,_nearCount;
    private bool _refineRestored;
    private readonly Func<Vec2,double> _ground;
    private readonly Func<Vec2,LiquidGroundSample>? _sampleGround;
    private readonly HexDomain _domain;
    public double WaterTable {get;set;}=double.NegativeInfinity;
    public double Time {get;private set;}
    public double Remainder {get;private set;}
    public long Version {get;private set;}
    public LiquidStepProfile LastStepProfile {get;private set;}
    public IReadOnlyList<FluidParticle> Particles=>_water;
    public double Volume=>_water.Sum(p=>p.Volume)+_pending.Values.Sum();
    public LiquidDynamics(HexDomain domain,Func<Vec2,double> ground,Func<Vec2,LiquidGroundSample>? sampleGround=null){_domain=domain;_ground=ground;_sampleGround=sampleGround;}
    public static double Radius(double volume)=>.42*Math.Cbrt(volume);
    private static float Kernel(float r2){float q=H2F-r2;return q>0?KF*q*q*q:0;}
    private static V Gradient(V d,float r){if(r<1e-8f||r>=SupportF)return V.Zero;float q=SupportF-r;return d*(GF*q*q/r);}
    private static void ForParticles(int count,Action<int> action)
    {
        // Jacobi reads the previous pass and writes disjoint slots. Preserve each parcel's
        // neighbour summation order, making parallel scheduling independent of the result.
        if(count<1024||SolverParallelism.MaxDegreeOfParallelism==1){for(int i=0;i<count;i++)action(i);return;}
        Parallel.For(0,(count+127)/128,SolverParallelism,block=>{for(int i=block*128,end=Math.Min(count,(block+1)*128);i<end;i++)action(i);});
    }
    private static V Fast(Vec3 p)=>new((float)p.X,(float)p.Y,(float)p.Z);
    private static Vec3 Position(V p)=>new(p.X,p.Y,p.Z);
    private static Vec2 XZ(V p)=>new(p.X,p.Z);
    public const double OutletHeight=.105;
    public void Add(Vec3 p,Vec3 velocity,double volume,ulong source=0)
    {
        if(!p.IsFinite||!velocity.IsFinite||!double.IsFinite(volume)||volume<0)throw new ArgumentException("Invalid liquid");
        if(volume==0)return;int n=(int)Math.Ceiling(volume/ParcelVolume),width=(int)Math.Ceiling(Math.Cbrt(n));
        double v=volume/n,d=Math.Cbrt(v);
        for(int i=0;i<n;i++)
        {var q=p+new Vec3((i%width-(width-1)*.5)*d,i/(width*width)*d,(i/width%width-(width-1)*.5)*d);
            q=Vec3.FromXZ(q.XZ,Math.Max(q.Y,_ground(q.XZ)+Radius(v)));_water.Add(new(q,velocity,v,source));}
        Version++;
    }
    public void Advance(double seconds,IReadOnlyList<Spring> springs,WaterBudget budget,double evaporation=0,double infiltration=0)
    {
        if(!double.IsFinite(seconds)||seconds<=0)return;Remainder+=seconds;
        while(Remainder+1e-12>=StepSeconds){Tick(springs,budget,evaporation,infiltration);Remainder=Math.Max(0,Remainder-StepSeconds);Time+=StepSeconds;}
    }
    private void Tick(IReadOnlyList<Spring> springs,WaterBudget budget,double evaporation,double infiltration)
    {
        if(_refineRestored)RefineRestoredParcels();
        foreach(var s in springs)
        {
            if(s.Discharge<=0)continue;ulong key=s.Id.Value;double added=s.Discharge*StepSeconds;budget.SpringInflow+=added;
            double pending=_pending.GetValueOrDefault(key)+added;
            double radius=Math.Max(.007,Math.Sqrt(s.Discharge/(Math.PI*.45)));
            while(pending>=ParcelVolume)
            {
                long serial=_emitted.GetValueOrDefault(key);_emitted[key]=serial+1;
                double angle=serial*2.399963229728653,r=radius*.35*Math.Sqrt((serial%7+.5)/7);
                var q=s.Position+new Vec2(Math.Cos(angle),Math.Sin(angle))*r;
                _water.Add(new(Vec3.FromXZ(q,_ground(s.Position)+OutletHeight),new Vec3(0,-.45,0),ParcelVolume,key));pending-=ParcelVolume;
            }
            _pending[key]=pending;
        }
        int n=_water.Count;
        long profileStart=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_p.Length<n){int c=Math.Max(n,_p.Length*2+256);_p=new V[c];_old=new V[c];_delta=new V[c];_normal=new V[c];_contact=new V[c];_v=new V[c];_lambda=new float[c];_mass=new float[c];_solidGradient=new V[c];_offset=new int[c];_count=new int[c];_candidates=new List<int>[c];}
        for(int i=0;i<n;i++)
        {
            var f=_water[i];_mass[i]=(float)f.Volume;_old[i]=Fast(f.Position);_p[i]=_old[i]+(Fast(f.Velocity)-new V(0,(float)(9.81*StepSeconds),0))*(float)StepSeconds;
            var sample=_sampleGround?.Invoke(XZ(_p[i]));
            double floor=sample?.Height??_ground(XZ(_p[i])),r=Radius(f.Volume);_contact[i]=new V(_p[i].X,(float)(floor+r),_p[i].Z);_normal[i]=V.UnitY;
            if(_p[i].Y<floor+r+.02*LengthScale)
            {
                if(sample is {} solid)_normal[i]=V.Normalize(new V((float)-solid.SlopeX,1,(float)-solid.SlopeZ));
                else{double e=.012*LengthScale;var q=XZ(_p[i]);_normal[i]=V.Normalize(new V((float)((_ground(q-new Vec2(e,0))-_ground(q+new Vec2(e,0)))/(2*e)),1,(float)((_ground(q-new Vec2(0,e))-_ground(q+new Vec2(0,e)))/(2*e))));}
            }
            Contact(i);
        }
        double contactMs=System.Diagnostics.Stopwatch.GetElapsedTime(profileStart).TotalMilliseconds;
        long neighbourStart=System.Diagnostics.Stopwatch.GetTimestamp();Neighbours(n);
        double neighbourMs=System.Diagnostics.Stopwatch.GetElapsedTime(neighbourStart).TotalMilliseconds;
        long constraintStart=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_pairGradient.Length<_nearCount){_pairGradient=new V[_near.Length];_pairKernel=new float[_near.Length];_pairDistance=new float[_near.Length];}
        for(int it=0;it<4;it++)
        {
            ForParticles(n,i=>
            {
                // Integrate the kernel over the solid half-space. Missing neighbours inside the bed
                // are solid boundary support, not room to compress twice as much water into the floor.
                double t=Math.Clamp((V.Dot(_p[i]-_contact[i],_normal[i])+Radius(_mass[i])*_normal[i].Y)/Support,0,1);
                double t2=t*t,t3=t*t2,t5=t3*t2,t7=t5*t2,t9=t7*t2;
                double solid=.5-315.0/256*(t-4.0/3*t3+6.0/5*t5-4.0/7*t7+t9/9);
                double qSolid=1-t2,qSolid2=qSolid*qSolid;
                _solidGradient[i]=_normal[i]*(float)(-315.0/(256*Support)*qSolid2*qSolid2);
                float density=_mass[i]*Kernel(0)+(float)solid,grad2=0;V grad=_solidGradient[i];
                for(int k=_offset[i],end=k+_count[i];k<end;k++)
                {int j=_near[k];var d=_p[i]-_p[j];float r2=d.LengthSquared(),v=_mass[j],r=MathF.Sqrt(r2);
                    float weight=Kernel(r2);var g=Gradient(d,r);_pairKernel[k]=weight;_pairGradient[k]=g;_pairDistance[k]=r;
                    density+=v*weight;g*=v;grad+=g;grad2+=g.LengthSquared();}
                _lambda[i]=-MathF.Max(0,density-1)/(grad2+grad.LengthSquared()+.01f);
            });
            ForParticles(n,i=>
            {
                V dpos=_solidGradient[i]*_lambda[i];
                for(int k=_offset[i],end=k+_count[i];k<end;k++)
                {int j=_near[k];var d=_p[i]-_p[j];float r=_pairDistance[k];if(r>=SupportF)continue;
                    float q=_pairKernel[k]/CorrReference,q2=q*q;
                    dpos+=_pairGradient[k]*((_lambda[i]+_lambda[j]-PressureScale*q2*q2)*_mass[j]);
                    if(r>RestSpacingF)dpos-=d*(CohesionScale*(r-RestSpacingF)/(r*SupportF));}
                float len=dpos.Length();if(len>CorrectionLimit)dpos*=CorrectionLimit/len;_delta[i]=dpos;
            });
            for(int i=0;i<n;i++){_p[i]+=_delta[i];Contact(i);}
        }
        double constraintMs=System.Diagnostics.Stopwatch.GetElapsedTime(constraintStart).TotalMilliseconds;
        long viscosityStart=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=0;i<n;i++)_v[i]=(_p[i]-_old[i])/(float)StepSeconds;
        for(int i=0;i<n;i++)
        {
            V v=_v[i],smooth=V.Zero;double weights=0;
            for(int k=_offset[i],end=k+_count[i];k<end;k++){int j=_near[k];double w=_mass[j]*Kernel((_p[i]-_p[j]).LengthSquared());smooth+=(_v[j]-v)*(float)w;weights+=w;}
            v+=smooth*(float)(.035/Math.Max(1,weights));_water[i]=_water[i] with {Position=Position(_p[i]),Velocity=Position(v)};
        }
        double viscosityMs=System.Diagnostics.Stopwatch.GetElapsedTime(viscosityStart).TotalMilliseconds;
        long sinkStart=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=n-1;i>=0;i--)
        {
            var f=_water[i];if(!_domain.Contains(f.Position.XZ)){budget.BoundaryOutflow+=f.Volume;_water.RemoveAt(i);continue;}
            double floor=_ground(f.Position.XZ);
            if(f.Position.Y<=WaterTable&&floor<WaterTable){budget.GroundwaterRecharge+=f.Volume;_water.RemoveAt(i);continue;}
            double area=Math.Pow(f.Volume,2.0/3),ev=Math.Min(f.Volume,Math.Max(0,evaporation)*StepSeconds*area);
            double inf=f.Position.Y<=floor+Radius(f.Volume)*1.2?Math.Min(f.Volume-ev,Math.Max(0,infiltration)*StepSeconds*area):0;
            budget.Evaporation+=ev;budget.Infiltration+=inf;double remaining=f.Volume-ev-inf;
            if(remaining<=0)_water.RemoveAt(i);else _water[i]=f with {Volume=remaining};
        }
        LastStepProfile=new(_nearCount,contactMs,neighbourMs,constraintMs,viscosityMs,System.Diagnostics.Stopwatch.GetElapsedTime(sinkStart).TotalMilliseconds);
        Version++;
    }
    private void Contact(int i){float d=V.Dot(_p[i]-_contact[i],_normal[i]);if(d<0)_p[i]-=_normal[i]*d;}
    private static (int,int,int) Key(V p)=>((int)Math.Floor(p.X/Support),(int)Math.Floor(p.Y/Support),(int)Math.Floor(p.Z/Support));
    private void Neighbours(int n)
    {
        _hash.Clear();_used=0;_nearCount=0;
        for(int i=0;i<n;i++){var key=Key(_p[i]);if(!_hash.TryGetValue(key,out var bin)){if(_used==_bins.Count)_bins.Add(new List<int>(16));bin=_bins[_used++];bin.Clear();_hash.Add(key,bin);}bin.Add(i);}
        // Resolve the 27 hash lookups once per occupied bin instead of once per parcel.
        // Candidate/near lists retain their capacity; the parallel pass only mutates its own list.
        int usedCandidates=0;
        foreach(var(key,bin)in _hash)
        {
            if(usedCandidates==_candidatePool.Count)_candidatePool.Add(new List<int>(64));
            var candidates=_candidatePool[usedCandidates++];candidates.Clear();var(x,y,z)=key;
            for(int dz=-1;dz<=1;dz++)for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(_hash.TryGetValue((x+dx,y+dy,z+dz),out var adjacent))candidates.AddRange(adjacent);
            foreach(int i in bin)_candidates[i]=candidates;
        }
        while(_particleNear.Count<n)_particleNear.Add(new List<int>(48));
        ForParticles(n,i=>{var near=_particleNear[i];near.Clear();var candidates=_candidates[i];var p=_p[i];
            for(int b=0;b<candidates.Count;b++){int j=candidates[b];if(j!=i&&(_p[j]-p).LengthSquared()<=SearchRadiusSquared)near.Add(j);}});
        for(int i=0;i<n;i++){_offset[i]=_nearCount;_count[i]=_particleNear[i].Count;_nearCount+=_count[i];}
        if(_near.Length<_nearCount)Array.Resize(ref _near,Math.Max(_nearCount,_near.Length*2+1024));
        ForParticles(n,i=>_particleNear[i].CopyTo(_near,_offset[i]));
    }
    public double Remove(Vec2 p,double radius,double amount)
    {double removed=0;for(int i=_water.Count-1;i>=0&&removed<amount;i--){var f=_water[i];if(Vec2.Distance(f.Position.XZ,p)>radius)continue;double take=Math.Min(f.Volume,amount-removed);removed+=take;if(take>=f.Volume)_water.RemoveAt(i);else _water[i]=f with {Volume=f.Volume-take};}Version++;return removed;}
    public FluidState Export()=>new(_water.ToArray(),new(_pending),Time,Remainder,new(_emitted));
    public void Restore(FluidState s)
    {
        if(!double.IsFinite(s.Time)||s.Time<0||!double.IsFinite(s.Remainder)||s.Remainder<0)throw new ArgumentException("Invalid liquid clock");
        foreach(var p in s.Particles)if(!p.Position.IsFinite||!p.Velocity.IsFinite||!double.IsFinite(p.Volume)||p.Volume<=0)throw new ArgumentException("Invalid saved parcel");
        foreach(var v in s.SourceRemainders.Values)if(!double.IsFinite(v)||v<0)throw new ArgumentException("Invalid saved source remainder");
        // Preserve the exact archive state for checksum verification. Upgrade coarse parcels on
        // the first physical tick, after the save loader has verified the original digest.
        _water.Clear();_water.AddRange(s.Particles);_refineRestored=s.Particles.Any(p=>p.Volume>ParcelVolume*(1+1e-10));
        _pending.Clear();foreach(var(k,v)in s.SourceRemainders)_pending[k]=v;
        _emitted.Clear();if(s.EmissionPhases!=null)foreach(var(k,v)in s.EmissionPhases)_emitted[k]=v;
        else foreach(var p in s.Particles)_emitted[p.Source]=_emitted.GetValueOrDefault(p.Source)+1;
        Time=s.Time;Remainder=s.Remainder;Version++;
    }
    private void RefineRestoredParcels()
    {
        var saved=_water.ToArray();_water.Clear();_refineRestored=false;
        foreach(var p in saved)
        {
            if(p.Volume<=ParcelVolume*(1+1e-10)){_water.Add(p);continue;}
            // Older saves have larger parcels. Split once, retaining volume, centre of mass and
            // velocity; never turn the saved pool back into a depth field or add new water.
            int count=(int)Math.Ceiling(p.Volume/ParcelVolume-1e-10);double volume=p.Volume/count;
            double radius=.5*Math.Cbrt(p.Volume);
            for(int i=0;i<count;i++)
            {
                double angle=2*Math.PI*i/count;
                var offset=new Vec3(radius*Math.Cos(angle),0,radius*Math.Sin(angle));
                _water.Add(p with {Position=p.Position+offset,Volume=volume});
            }
        }
    }
}
