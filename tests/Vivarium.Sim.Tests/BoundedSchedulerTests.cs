using Vivarium.Sim.Time;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using Vivarium.Sim.Tools;
namespace Vivarium.Sim.Tests;
public class BoundedSchedulerTests
{
    [Fact]
    public void BudgetYieldsInsideSystemWithoutAdvancingClockOrReorderingWork()
    {
        var clock = new SimClock();
        var scheduler = new Scheduler(clock) { WallBudgetMs = double.Epsilon };
        var order = new List<int>();
        IEnumerable<bool> Work(double dt)
        {
            Assert.Equal(SimClock.FixedStepSeconds, dt);
            for (int i=0;i<8;i++) { order.Add(i); yield return true; }
        }
        scheduler.RegisterBatched("first",1,0,Work);
        scheduler.Register("last",1,1,_ => order.Add(8));
        scheduler.Advance(SimClock.FixedStepSeconds / clock.EffectiveRate);
        Assert.True(scheduler.HasPendingTick);
        Assert.Equal(0,clock.Tick);
        Assert.Equal(new[]{0},order);
        Assert.Equal(SimClock.FixedStepSeconds,scheduler.Backlog);
        for(int i=0;i<30 && scheduler.HasPendingTick;i++) scheduler.Advance(0);
        Assert.False(scheduler.HasPendingTick);
        Assert.Equal(1,clock.Tick);
        Assert.Equal(Enumerable.Range(0,9),order);
        Assert.Equal(0,scheduler.Backlog);
        Assert.Equal(1,scheduler.Systems[0].Runs);
    }

    [Fact]
    public void BudgetedWorldMatchesUninterruptedWorldAtEqualTicks()
    {
        var descriptor=TestUtil.FlatDescriptor();
        descriptor.BioAcceleration=1;
        var a=VivariumWorld.CreateBaseline(TestUtil.Content,descriptor);
        var b=VivariumWorld.CreateBaseline(TestUtil.Content,descriptor);
        foreach(var w in new[]{a,b})
        {
            TestUtil.Condition(w,0.65,0.8,0.7);
            var sp=w.Content.FloraOrThrow("coinrunner");
            for(int i=0;i<20;i++) w.FloraSystem.Establish(sp,new Vec2(-1+(i%5)*0.4,-1+(i/5)*0.4),"fixture",0.05);
        }
        a.Scheduler.StepTicks(360);
        b.Scheduler.WallBudgetMs=double.Epsilon;
        b.Scheduler.MaxTicksPerAdvance=int.MaxValue;
        b.Scheduler.Advance((360*SimClock.FixedStepSeconds+0.01)/b.Clock.EffectiveRate);
        for(int i=0;b.Clock.Tick<360 && i<50000;i++) b.Scheduler.Advance(0);
        Assert.Equal(360,b.Clock.Tick);
        Assert.Equal(TestUtil.Digest(a),TestUtil.Digest(b));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaveAndToolEditCompleteAnInProgressTick(bool save)
    {
        var w=VivariumWorld.CreateBaseline(TestUtil.Content,TestUtil.FlatDescriptor());
        int stages=0;
        IEnumerable<bool> Work(double dt) { for(int i=0;i<3;i++) { stages++; yield return true; } }
        w.Scheduler.RegisterBatched("test",1,-1,Work);
        w.Scheduler.WallBudgetMs=double.Epsilon;
        w.Scheduler.Advance(SimClock.FixedStepSeconds/w.Clock.EffectiveRate);
        Assert.True(w.Scheduler.HasPendingTick);
        if(save) Assert.Equal(1,SaveSystem.Snapshot(w).Manifest.SimTick);
        else new ToolActions(w).SetBioAcceleration(2);
        Assert.False(w.Scheduler.HasPendingTick);
        Assert.Equal(3,stages);
        Assert.Equal(1,w.Clock.Tick);
    }

    [Fact]
    public void AutosaveGetsATickBoundaryEvenWithContinuousBacklog()
    {
        var w=VivariumWorld.CreateBaseline(TestUtil.Content,TestUtil.FlatDescriptor());
        IEnumerable<bool> Work(double dt) { for(int i=0;i<4;i++) yield return true; }
        w.Scheduler.RegisterBatched("test",1,-1,Work);
        w.Scheduler.WallBudgetMs=double.Epsilon;
        w.Scheduler.Advance(100/w.Clock.EffectiveRate);
        var autosave=new AutosaveController("unused") { IntervalSeconds=0, Writer=(_,_,_) => new SaveResult { Ok=true } };
        Assert.False(autosave.Tick(w,1));
        Assert.True(w.Scheduler.YieldAtTickBoundary);
        for(int i=0;i<100 && w.Scheduler.HasPendingTick;i++) w.Scheduler.Advance(0);
        Assert.Equal(1,w.Clock.Tick);
        Assert.True(w.Scheduler.Backlog>=SimClock.FixedStepSeconds);
        Assert.True(autosave.Tick(w,0));
        autosave.Flush(TimeSpan.FromSeconds(5));
        Assert.Equal(1,autosave.Started);
        Assert.True(autosave.LastResult!.Ok);
    }
}
