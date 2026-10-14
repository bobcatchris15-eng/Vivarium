using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;
using Vivarium.Sim.Water;
namespace Vivarium.Sim.Tests;

public class SpringFlowControlTests
{
    [Fact]
    public void EditingSpringRateKeepsItsIdentityAndTerrainAndPersists()
    {
        var w=TestUtil.FlatWorld(); var tools=new ToolActions(w);
        Assert.True(tools.ToggleSpring(new Vec2(1,1),ToolActions.DefaultSpringDischarge).Ok);
        var spring=Assert.Single(w.Water.Springs);
        int terrain=w.Terrain.Version,props=w.Props.Version;
        Assert.True(tools.SetSpringDischarge(spring.Id,500.0/3600000).Ok);
        Assert.Equal(500.0/3600000,spring.Discharge,12);
        Assert.Equal(terrain,w.Terrain.Version); Assert.Equal(props,w.Props.Version);
        double inflow=w.Water.Budget.SpringInflow;
        w.Water.Step(10);
        Assert.Equal(spring.Discharge*10,w.Water.Budget.SpringInflow-inflow,10);
        var path=Path.Combine(TestUtil.TempDir(),"flow"+SaveSystem.Extension);
        Assert.True(SaveSystem.Save(w,path).Ok);
        var loaded=SaveSystem.Load(w.Content,path);
        Assert.True(loaded.Ok);
        Assert.Equal(spring.Discharge,Assert.Single(loaded.World!.Water.Springs).Discharge,12);
        Assert.True(tools.SetSpringDischarge(spring.Id,-1).Ok);
        Assert.Equal(ToolActions.MinSpringDischarge,spring.Discharge);
        Assert.True(tools.SetSpringDischarge(spring.Id,1).Ok);
        Assert.Equal(ToolActions.MaxSpringDischarge,spring.Discharge);
    }

    [Fact]
    public void IncreasingDischargeProducesMoreVisibleRunoffWithoutChangingWaterBudget()
    {
        int Run(double rate)
        {
            var w=TestUtil.FlatWorld();
            w.Water.Springs.Add(new Spring {Id=w.Ids.Next(EntityKind.Spring),X=0,Z=0,Discharge=rate});
            string before=w.Water.DigestHex();
            for(int i=0;i<20;i++)w.SpringFluid.Advance(SpringFluidSystem.FixedStep);
            Assert.Equal(before,w.Water.DigestHex());
            return w.SpringFluid.Count;
        }
        int low=Run(ToolActions.DefaultSpringDischarge), high=Run(500.0/3600000);
        Assert.True(low>0);
        Assert.True(high>low*2,$"low={low}, high={high}");
    }
}
