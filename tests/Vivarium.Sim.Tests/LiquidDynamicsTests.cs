using System.Diagnostics;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;

public class LiquidDynamicsTests
{
    [Fact] public void HabitatVelocityMatchesThePhysicalLiquidVelocity()
    {
        var w=TestUtil.FlatWorld();int cell=w.Grid.NearestDomainCell(Vec2.Zero);var p=w.Grid.CellCenter(cell);
        var particle=new FluidParticle(Vec3.FromXZ(p,.04),new Vec3(1.2,0,-.4),LiquidDynamics.ParcelVolume,0);
        w.Liquid.Enable(new FluidState([particle],new(),0,0));var velocity=w.Water.StreamVelocity(cell);
        Assert.InRange(velocity.X,1.2-1e-12,1.2+1e-12);Assert.InRange(velocity.Z,-.4-1e-12,-.4+1e-12);
    }
    [Fact] public void NewGameWaterStartsAtTheOutletWithoutLegacyFieldWarmup()
    {
        var d=TestUtil.FlatDescriptor();d.Water.Springs.Add(new(){X=0,Z=0,Discharge=2});
        var w=VivariumWorld.Create(TestUtil.Content,d,false,warmupSurfaceWater:false);w.Liquid.Enable();
        Assert.Equal(0,w.Water.Volume());Assert.Equal(0,w.Water.Budget.SpringInflow);
        w.Liquid.Advance(.25);Assert.True(w.Liquid.Particles.Count>0);Assert.True(w.Water.Volume()>0);
    }
    [Fact] public void BackgroundLiquidPublishesConsistentBudgetAndSave()
    {
        var w=TestUtil.FlatWorld();var source=Source();source.Id=w.Ids.Next(EntityKind.Spring);w.Water.Springs.Add(source);
        w.Liquid.Enable();w.Liquid.AsyncMode=true;w.Liquid.Advance(.25);
        for(int i=0;i<20;i++){w.Liquid.CompletePending();w.Liquid.Advance(0);}
        w.Liquid.CompletePending();
        Assert.InRange(w.Liquid.CaptureState().Time,.25-1e-10,.25+1e-10);
        Assert.InRange(w.Water.Volume(),w.Water.Budget.SpringInflow-1e-12,w.Water.Budget.SpringInflow+1e-12);
        w.Liquid.Advance(.0333333333333);
        string path=Path.Combine(TestUtil.TempDir(),"background-liquid.vivsave");Assert.True(SaveSystem.Save(w,path).Ok);
        var loaded=SaveSystem.Load(TestUtil.Content,path);Assert.True(loaded.Ok,loaded.Message);
        Assert.Equal(WorldSerializer.Digest(w),WorldSerializer.Digest(loaded.World!));
    }
    private static Spring Source(double x=0,double z=0)=>new(){Id=new EntityId(123),X=x,Z=z,Discharge=2000.0/1000/3600};
    private static double Bowl(Vec2 p)=>-.07*Math.Max(0,1-p.LengthSq/.04)-.5*Math.Max(0,p.X-.25);
    private static double UnequalRims(Vec2 p)
    {
        static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
        // A flat 24 x 20 cm floor, a 45 mm east sill, and 80 mm everywhere else.
        double east=p.X>0?0:.035;
        double x=-.045+(east+.045)*Smooth((Math.Abs(p.X)-.12)/.035);
        double z=-.045+.080*Smooth((Math.Abs(p.Z)-.10)/.035);
        return Math.Max(x,z);
    }
    [Theory][InlineData(1)][InlineData(-1)] public void SmallPoolSettlesAndOverflowsTheLowestRim(int downhillSide)
    {
        var f=new LiquidDynamics(new HexDomain(2),p=>UnequalRims(new Vec2(p.X*downhillSide,p.Z)));var b=new WaterBudget();
        f.Add(new Vec3(0,-.025,0),Vec3.Zero,.0015);f.Advance(8,[],b);
        double earlyEast=f.Particles.Where(p=>p.Position.X*downhillSide>.17).Sum(p=>p.Volume);
        double earlyHigh=f.Particles.Where(p=>p.Position.X*downhillSide<-.17||Math.Abs(p.Position.Z)>.15).Sum(p=>p.Volume);
        var central=f.Particles.Where(p=>Math.Abs(p.Position.X)<.10&&Math.Abs(p.Position.Z)<.08).ToArray();
        var tops=new List<double>();
        foreach(double x in new[]{-.07,0,.07})foreach(double z in new[]{-.05,0,.05})
        {var col=central.Where(p=>Vec2.Distance(p.Position.XZ,new Vec2(x,z))<.022).ToArray();
            if(col.Length>0)tops.Add(col.Max(p=>p.Position.Y+LiquidDynamics.Radius(p.Volume)));}
        double surfaceSpread=tops.Max()-tops.Min();
        var mesh=LiquidSurfaceMesh.Build(f.Particles);var upper=new List<double>();
        for(int i=0;i<mesh.VertexCount;i++)
        {var p=mesh.Position(i);if(Math.Abs(p.X)<.09&&Math.Abs(p.Z)<.07&&mesh.NormalAt(i).Y>.8)upper.Add(p.Y);}
        double meshSpread=upper.Max()-upper.Min();
        var s=Source();s.Discharge/=4;f.Advance(28,[s],b);
        double east=f.Particles.Where(p=>p.Position.X*downhillSide>.17).Sum(p=>p.Volume);
        double high=f.Particles.Where(p=>p.Position.X*downhillSide<-.17||Math.Abs(p.Position.Z)>.15).Sum(p=>p.Volume);
        Directory.CreateDirectory(Path.Combine(TestUtil.RepoRoot,"artifacts/fluid-resolution"));
        File.WriteAllText(Path.Combine(TestUtil.RepoRoot,$"artifacts/fluid-resolution/pool-{(downhillSide==1?"east":"west")}-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new{downhillSide,earlyEast,earlyHigh,surfaceSpread,meshSpread,tops,east,high,count=f.Particles.Count,f.Volume,b.SpringInflow,b.BoundaryOutflow}));
        Assert.True(earlyEast<.00003&&earlyHigh<.00003,"A sub-rim pool must retain its volume before overflow");
        Assert.True(surfaceSpread<.018,"The resting free surface should level within one particle spacing");
        Assert.True(meshSpread<.018,"The actual reconstructed pool surface must also be level within one particle spacing");
        Assert.True(east>.0002,"Continued inflow must overflow the lower east sill");
        Assert.True(high<east*.1,"The higher rims must remain substantially drier than the lowest sill");
        Assert.InRange(f.Volume+b.BoundaryOutflow,.0015+b.SpringInflow-1e-10,.0015+b.SpringInflow+1e-10);
    }
    [Fact] public void CoarseSavedParcelsSplitWithoutLosingVolumeOrMomentum()
    {
        var f=new LiquidDynamics(new HexDomain(2),p=>0);var p=new FluidParticle(new Vec3(.2,.1,-.3),new Vec3(.4,-.1,.2),.000015,77);
        f.Restore(new FluidState([p],new(){{77,.000002}},12,.004,new(){{77,45}}));
        Assert.Equal(p,Assert.Single(f.Export().Particles)); // Exact archive state survives digest verification.
        f.Advance(LiquidDynamics.StepSeconds,[],new WaterBudget());
        Assert.Equal(3,f.Particles.Count);Assert.InRange(f.Volume,.000017-1e-14,.000017+1e-14);
        Assert.All(f.Particles,q=>{Assert.True(q.Volume<=LiquidDynamics.ParcelVolume*(1+1e-10));Assert.Equal(p.Source,q.Source);});
        Assert.InRange(f.Particles.Average(q=>q.Velocity.X),p.Velocity.X-1e-5,p.Velocity.X+1e-5);
        Assert.InRange(f.Particles.Average(q=>q.Velocity.Z),p.Velocity.Z-1e-5,p.Velocity.Z+1e-5);
        Assert.InRange(f.Particles.Average(q=>q.Position.X),p.Position.X+p.Velocity.X*LiquidDynamics.StepSeconds-1e-7,p.Position.X+p.Velocity.X*LiquidDynamics.StepSeconds+1e-7);
        var state=f.Export();Assert.Equal(12+LiquidDynamics.StepSeconds,state.Time);Assert.InRange(state.Remainder,.004-1e-12,.004+1e-12);Assert.Equal(45,state.EmissionPhases![77]);
        var restored=new LiquidDynamics(new HexDomain(2),q=>0);restored.Restore(state);Assert.Equal(state.Particles,restored.Export().Particles);
        var world=TestUtil.FlatWorld();world.Liquid.Enable(new FluidState([p],new(),0,0,new(){{77,45}}));
        string path=Path.Combine(TestUtil.TempDir(),"coarse-pool.vivsave");Assert.True(SaveSystem.Save(world,path).Ok);
        var loaded=SaveSystem.Load(TestUtil.Content,path);Assert.True(loaded.Ok,loaded.Message);
        Assert.Equal(WorldSerializer.Digest(world),WorldSerializer.Digest(loaded.World!));
        loaded.World!.Liquid.Advance(LiquidDynamics.StepSeconds);Assert.Equal(3,loaded.World.Liquid.Particles.Count);
    }
    [Fact] public void SpringDischargeIsRetainedAndRestingWaterNeverExpires()
    {
        var f=new LiquidDynamics(new HexDomain(4),p=>0);var b=new WaterBudget();var s=Source();
        f.Advance(4,[s],b);Assert.InRange(f.Volume, s.Discharge*4-1e-12,s.Discharge*4+1e-12);
        s.Discharge=0;f.Advance(16,[s],b);
        Assert.InRange(f.Volume,b.SpringInflow-1e-12,b.SpringInflow+1e-12);
        Assert.All(f.Particles,p=>{Assert.True(p.Position.IsFinite&&p.Velocity.IsFinite);Assert.True(p.Position.Y>=-.001);});
    }
    [Fact] public void GravityMovesActualLiquidDownTheSlope()
    {
        var f=new LiquidDynamics(new HexDomain(6),p=>.2-.25*p.X);var b=new WaterBudget();
        f.Add(new Vec3(0,.3,0),Vec3.Zero,.0015);f.Advance(1,[],b);
        Assert.True(f.Particles.Average(p=>p.Position.X)>.4);
        Assert.InRange(f.Volume+b.BoundaryOutflow,.0015-1e-12,.0015+1e-12);
    }
    [Fact] public void BowlFillsBeforeWaterSpillsAndReachesTheDownhillPond()
    {
        var f=new LiquidDynamics(new HexDomain(4),Bowl){WaterTable=-.12};var b=new WaterBudget();var s=Source();
        var timer=Stopwatch.StartNew();f.Advance(5,[s],b);
        Assert.True(f.Particles.Count(p=>p.Position.XZ.Length<.22)>.75*f.Particles.Count,"Early inflow should be retained inside the hollow");
        Assert.True(b.GroundwaterRecharge==0,"Water must not teleport from the source into the downhill pond");
        f.Advance(35,[s],b);
        Directory.CreateDirectory(Path.Combine(TestUtil.RepoRoot,"artifacts/fluid-resolution"));
        File.WriteAllText(Path.Combine(TestUtil.RepoRoot,"artifacts/fluid-resolution/bowl-diagnostic.json"),System.Text.Json.JsonSerializer.Serialize(new{count=f.Particles.Count,min=f.Particles.Min(p=>p.Position.Y),max=f.Particles.Max(p=>p.Position.Y),xMin=f.Particles.Min(p=>p.Position.X),xMax=f.Particles.Max(p=>p.Position.X),penetration=f.Particles.Min(p=>p.Position.Y-Bowl(p.Position.XZ)),f.Volume,b.GroundwaterRecharge,wallMs=timer.Elapsed.TotalMilliseconds}));
        Assert.True(f.Particles.Any(p=>p.Position.X>.3)||b.GroundwaterRecharge>0,"Filled hollow must spill toward the lower slope");
        Assert.True(b.GroundwaterRecharge>0,"Actual fluid must reach the pond surface");
        Assert.InRange(f.Volume+b.GroundwaterRecharge+b.BoundaryOutflow,b.SpringInflow-1e-10,b.SpringInflow+1e-10);
        File.WriteAllText(Path.Combine(TestUtil.RepoRoot,"artifacts/fluid-resolution/bowl.txt"),$"physical_seconds=40 wall_ms={timer.Elapsed.TotalMilliseconds:F1} particles={f.Particles.Count} inflow={b.SpringInflow:R} retained={f.Volume:R} pond={b.GroundwaterRecharge:R} boundary={b.BoundaryOutflow:R}");
    }
    [Fact] public void ClosedThreeDimensionalSurfaceFollowsAirborneAndDeepLiquid()
    {
        var p=new FluidParticle(new Vec3(1,1.2,2),new Vec3(0,-.45,0),LiquidDynamics.ParcelVolume,1);
        var mesh=LiquidSurfaceMesh.Build([p]);Assert.True(mesh.TriangleCount>0);
        var bounds=mesh.Bounds();Assert.True(bounds.Min.Y>1.17&&bounds.Max.Y<1.23);
        Assert.Contains(mesh.Normals.Where((v,i)=>i%3==1),v=>v<-.2);
        var edges=new Dictionary<(int,int),int>();
        for(int i=0;i<mesh.Indices.Count;i+=3)for(int j=0;j<3;j++)
        {int a=mesh.Indices[i+j],b=mesh.Indices[i+(j+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));edges[key]=edges.GetValueOrDefault(key)+1;}
        Assert.All(edges.Values,count=>Assert.Equal(2,count));
    }
    [Fact] public void LargerLiquidBodyStillHasAClosedContinuousSurface()
    {
        var f=new LiquidDynamics(new HexDomain(4),p=>0);
        f.Add(new Vec3(0,.10,0),Vec3.Zero,.012);
        var mesh=LiquidSurfaceMesh.Build(f.Particles);Assert.True(mesh.TriangleCount>0);
        Assert.All(mesh.Positions,x=>Assert.True(double.IsFinite(x)));
        var edges=new Dictionary<(int,int),int>();
        for(int i=0;i<mesh.Indices.Count;i+=3)for(int j=0;j<3;j++)
        {int a=mesh.Indices[i+j],b=mesh.Indices[i+(j+1)%3];var key=(Math.Min(a,b),Math.Max(a,b));edges[key]=edges.GetValueOrDefault(key)+1;}
        Assert.All(edges.Values,count=>Assert.Equal(2,count));
        var bounds=mesh.Bounds();Assert.True(bounds.Min.Y>.075&&bounds.Max.Y>.2&&bounds.Max.Y<.35);
    }
    [Fact] public void GameLiquidPauseAndSavePreserveVolumeAndSources()
    {
        var w=TestUtil.FlatWorld();var source=Source();source.Id=w.Ids.Next(EntityKind.Spring);w.Water.Springs.Add(source);w.Liquid.Enable();w.Liquid.Advance(.5);
        double v=w.Water.Volume(),t=w.Liquid.Dynamics.Time;w.Clock.Paused=true;w.Liquid.Advance(1);
        Assert.Equal(v,w.Water.Volume());Assert.Equal(t,w.Liquid.Dynamics.Time);
        var payload=WorldSerializer.Serialize(w);var restored=WorldSerializer.Deserialize(TestUtil.Content,payload);
        Assert.True(restored.Liquid.Enabled);Assert.Equal(v,restored.Water.Volume());
        Assert.Equal(w.Liquid.Particles.Count,restored.Liquid.Particles.Count);
        string path=Path.Combine(TestUtil.TempDir(),"liquid.vivsave");Assert.True(SaveSystem.Save(w,path).Ok);
        var archive=SaveSystem.Load(TestUtil.Content,path);Assert.True(archive.Ok,archive.Message);
        Assert.Equal(WorldSerializer.Digest(w),WorldSerializer.Digest(archive.World!));
        // The old field solver cannot inject a second copy of spring volume into a game world.
        restored.Water.Step(100);Assert.Equal(v,restored.Water.Volume());
        restored.Clock.Paused=false;restored.Liquid.Advance(.5);Assert.True(restored.Water.Volume()>v);
    }
}
