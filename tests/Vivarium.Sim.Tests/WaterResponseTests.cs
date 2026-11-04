using Vivarium.Sim.Core;
using Vivarium.Sim.Tools;
using Vivarium.Sim.Water;

namespace Vivarium.Sim.Tests;

public class WaterResponseTests
{
    [Fact]
    public void PublishedContactViewRetainsItsOwnGeometryAfterANewPublication()
    {
        var w=TestUtil.FlatWorld();w.Liquid.Add(Vec2.Zero,.07,.001);
        var before=w.Liquid.Particles;var positions=before.Select(p=>p.Position).ToArray();
        w.Terrain.BeginEdit();for(int i=0;i<w.Terrain.H.Length;i++)w.Terrain.H[i]+=.1;
        w.Terrain.Touch();w.Liquid.OnTerrainChanged();
        var after=w.Liquid.Particles;
        Assert.Equal(positions,before.Select(p=>p.Position).ToArray());
        Assert.Equal(before.Count,after.Count);
        for(int i=0;i<before.Count;i++)Assert.Equal(before[i].Position.Y+.1,after[i].Position.Y,10);
    }

    [Fact]
    public void PublishedSupportMatchesTheSolidAndStaysImmutableAcrossEdits()
    {
        bool raised=false;
        double Original(Vec2 p)=>.5+.01*p.X+.03*p.Z*p.Z;
        double Bed(Vec2 p)=>Original(p)+(raised?.1:0);
        var f=new SurfaceFluidDynamics(new(3),Bed);f.AddWater(Vec2.Zero,.07,.001);
        var before=f.CaptureRenderState();
        Assert.All(before.Cells,c=>Assert.Equal(Original(c.Position),c.SupportHeight));
        raised=true;f.OnGeometryChanged();
        var after=f.CaptureRenderState();
        Assert.All(after.Cells,c=>Assert.Equal(Bed(c.Position),c.SupportHeight));
        Assert.All(before.Cells,c=>Assert.Equal(Original(c.Position),c.SupportHeight));
        Assert.Equal(before.Cells.Sum(c=>c.Volume),after.Cells.Sum(c=>c.Volume),12);
    }

    [Fact]
    public void MaximumSpringFlowEntersThroughAFiniteApertureWithoutAPressureTower()
    {
        var f=new SurfaceFluidDynamics(new(3),_=>.5);var budget=new WaterBudget();
        f.Advance(SurfaceFluidDynamics.StepSeconds,[new Spring{Id=new(101),Discharge=2000.0/3600000}],budget);
        var wet=f.CaptureRenderState().Cells;
        Assert.True(wet.Length>8);
        Assert.All(wet,c=>Assert.InRange(c.Depth,0,.02));
        Assert.All(wet,c=>Assert.InRange(c.Position.Length,0,.06));
        Assert.Equal(2000.0/3600000*SurfaceFluidDynamics.StepSeconds,budget.SpringInflow,12);
        Assert.InRange(Math.Abs(f.Volume-budget.SpringInflow),0,1e-15);
    }

    [Fact]
    public void EstablishedFlowKeepsItsCurrentUntilSourceChangesWakeIt()
    {
        const double spacing=.0125,depth=.01,speed=.08;
        var f=new SurfaceFluidDynamics(new(3),p=>.5-.01*p.X);
        var cells=new List<SurfaceFluidCell>();
        for(int z=-4;z<4;z++)for(int x=-8;x<8;x++)
            cells.Add(new(x,z,depth*spacing*spacing,speed*depth*spacing,0,-.01,0,depth*spacing,0,
                QuietSeconds:2,CachedHead:.5-.01*(x+.5)*spacing+depth,HeadVolume:depth*spacing*spacing,Established:true,Sleeping:true));
        f.Restore(new(cells.ToArray(),[],0,0,spacing,new()));
        double volume=f.Volume;f.Advance(.5,[],new());
        Assert.Equal(volume,f.Volume,12);Assert.True(f.EstablishedCells>0);
        Assert.True(f.CaptureRenderState().Cells.Single(c=>c.Position.X==spacing*.5&&c.Position.Z==spacing*.5).Velocity.X>.05);
        f.OnSourcesChanged();Assert.Equal(0,f.EstablishedCells);
        Assert.All(f.Export().Cells,c=>Assert.Equal(0,c.QuietSeconds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EstablishedWaterRetainsStillOrFlowingSurfaceAndWakesForEdits(bool flowing)
    {
        const double spacing=.0125, depth=.02, slope=.001;
        bool blocked=false;
        double Bed(Vec2 p)=>.5-(flowing?slope*p.X:0)+(blocked&&Math.Abs(p.X)<.03?.1:0);
        var f=new SurfaceFluidDynamics(new(3),Bed);
        double velocity=flowing?9.81*slope*(1.0/60)*.96/(1-.96):0;
        var cells=new List<SurfaceFluidCell>();
        for(int z=-48;z<48;z++)for(int x=-48;x<48;x++)
            cells.Add(new(x,z,depth*spacing*spacing,velocity*depth*spacing,0,flowing?-slope:0,0,depth*spacing,depth*spacing));
        f.Restore(new(cells.ToArray(),[],0,0,spacing,new()));
        var budget=new WaterBudget();double initial=f.Volume;
        f.Advance(.6,[],budget);
        Assert.True(f.SleepingCells>0,"Established interior should reuse its surface");
        var centre=f.CaptureRenderState().Cells.Single(c=>c.Position.X==spacing*.5&&c.Position.Z==spacing*.5);
        if(flowing)Assert.True(centre.Velocity.X>.001);else Assert.InRange(centre.Velocity.Length,0,1e-9);
        Assert.InRange(Math.Abs(f.Volume+budget.BoundaryOutflow+budget.GroundwaterRecharge-initial),0,1e-10);
        f.AddWater(new(spacing*.5,spacing*.5),.04,.0001);
        f.Advance(1.0/60,[],budget);
        Assert.Contains(f.Export().Cells,c=>c.X==0&&c.Z==0&&c.QuietSeconds<.5);
        blocked=true;f.OnGeometryChanged();Assert.Equal(0,f.SleepingCells);
        Assert.All(f.Export().Cells,c=>Assert.Equal(0,c.QuietSeconds));
    }

    [Fact]
    public void TileEdgesAndWorkerCountsGiveIdenticalConservedState()
    {
        double Bed(Vec2 p)=>.5-.04*p.X+.1*p.Z*p.Z;
        var serial=new SurfaceFluidDynamics(new(3),Bed){WorkerCount=1};
        var parallel=new SurfaceFluidDynamics(new(3),Bed){WorkerCount=4};
        var cells=new List<SurfaceFluidCell>();const double spacing=.0125;
        for(int z=-40;z<40;z++)for(int x=-40;x<40;x++)cells.Add(new(x,z,.003*spacing*spacing,0,0));
        var state=new SurfaceFluidState(cells.ToArray(),[],0,0,spacing,new());
        serial.Restore(state);parallel.Restore(state);var a=new WaterBudget();var b=new WaterBudget();
        serial.Advance(.1,[],a);parallel.Advance(.1,[],b);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(serial.Export()),System.Text.Json.JsonSerializer.Serialize(parallel.Export()));
        Assert.Equal(serial.Volume,parallel.Volume);Assert.True(parallel.AllocatedTiles>4);
    }

    [Fact]
    public void InfiltrationCacheRefreshesAfterSolidChangesAndProviderReplacement()
    {
        var centre = new Vec2(.00625, .00625);
        var f = new SurfaceFluidDynamics(new(3), p => (p - centre).Length < .003 ? .5 : .6);
        int queries = 0;
        f.InfiltrationFactor = _ => { queries++; return .5; };
        f.AddWater(centre, 0, 1e-8);
        var budget = new WaterBudget();
        f.Advance(SurfaceFluidDynamics.StepSeconds, [], budget, infiltration: 1e-6);
        int first = queries;
        Assert.True(first > 0);
        f.Advance(SurfaceFluidDynamics.StepSeconds, [], budget, infiltration: 1e-6);
        Assert.Equal(first, queries);
        f.OnGeometryChanged();
        f.Advance(SurfaceFluidDynamics.StepSeconds, [], budget, infiltration: 1e-6);
        Assert.True(queries > first);
        double before = budget.Infiltration;
        f.InfiltrationFactor = _ => 0;
        f.Advance(SurfaceFluidDynamics.StepSeconds, [], budget, infiltration: 1e-6);
        Assert.Equal(before, budget.Infiltration);
        Assert.InRange(Math.Abs(f.Volume + budget.Infiltration - 1e-8), 0, 1e-15);
    }

    [Fact]
    public void PlacingSpringDoesNotExcavateAReservoirBeforeRunoffCanStart()
    {
        var w = TestUtil.FlatWorld();
        var heights = w.Terrain.H.ToArray();
        Assert.True(new ToolActions(w).ToggleSpring(Vec2.Zero).Ok);
        Assert.Equal(heights, w.Terrain.H);
        w.Liquid.AdvancePhysical(5);
        var wet = w.Liquid.CaptureRenderState().Cells.Where(c => c.Depth > .00005).ToArray();
        Assert.Contains(wet, c => c.Position.Length > .2);
        Assert.Equal(ToolActions.DefaultSpringDischarge * 5, w.Water.Budget.SpringInflow, 10);
        Assert.InRange(Math.Abs(w.Liquid.Volume - w.Water.Budget.SpringInflow), 0, 1e-10);
    }

    [Theory]
    [InlineData(.0005)]
    [InlineData(.002)]
    [InlineData(.02)]
    public void PublishedCurrentUsesSolvedWetFaceAreaEvenForShallowWater(double depth)
    {
        const double spacing = .0125, speed = .12;
        double section = depth * spacing;
        var f = new SurfaceFluidDynamics(new(3), _ => .5);
        // Uniform staggered discharge: two equally wet faces around the centre cell.
        f.Restore(new([
            new(-1, 0, depth * spacing * spacing, speed * section, 0, 0, 0, section, 0),
            new(0, 0, depth * spacing * spacing, speed * section, 0, 0, 0, section, 0)
        ], [], 0, 0, spacing, new()));
        var centre = f.CaptureRenderState().Cells.Single(c => c.Position.X > 0);
        Assert.Equal(speed, centre.Velocity.X, 10);
        Assert.Equal(0, centre.Velocity.Z);
    }
}
