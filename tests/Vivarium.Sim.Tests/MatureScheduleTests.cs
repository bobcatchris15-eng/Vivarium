using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;
public class MatureScheduleTests
{
    [Fact]
    public void HeavyPassesKeepTheirCadenceButAvoidTheMainCollisions()
    {
        var world=VivariumWorld.CreateBaseline(TestUtil.Content,TestUtil.FlatDescriptor());
        var systems=world.Scheduler.Systems.ToDictionary(s=>s.Name);
        for(int tick=1;tick<=360;tick++)
        {
            if(systems["aquatic"].DueAt(tick))
            {
                Assert.False(systems["hydrology"].DueAt(tick));
                Assert.False(systems["fauna.behaviour"].DueAt(tick));
            }
            if(systems["flora.ambient"].DueAt(tick))Assert.False(systems["flora"].DueAt(tick));
            if(systems["ecology.litter"].DueAt(tick))Assert.False(systems["hydrology"].DueAt(tick));
        }
        foreach(string name in new[]{"aquatic","flora.ambient","flora"})
            Assert.Equal(6,Enumerable.Range(1,360).Count(tick=>systems[name].DueAt(tick)));
    }
}
