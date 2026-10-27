using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

public class FaunaReloadIndexTests
{
    [Fact]
    public void MovedAnimalsHaveTheSameNeighboursBeforeAndAfterReload()
    {
        var w = TestUtil.FlatWorld(5747);
        TestUtil.Condition(w, .65, .7, 1);
        var sp = w.Content.FaunaOrThrow("prismhopper");
        for (int i = 0; i < 12; i++)
        {
            var f = w.FaunaSystem.CreateFounder(sp, new Vec2(.49999, -2.5 + i * .4), ageFraction: .25);
            f.Heading = 0;
        }
        w.FaunaSystem.StepBehaviour(40);
        var restored = WorldSerializer.Deserialize(w.Content, WorldSerializer.Serialize(w));
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
        foreach (var f in w.Fauna.Items)
        {
            var a = w.Fauna.ItemsNear(f.PositionXZ, .00001).Select(x => x.Id.Value).Order().ToArray();
            var b = restored.Fauna.ItemsNear(f.PositionXZ, .00001).Select(x => x.Id.Value).Order().ToArray();
            Assert.Equal(b, a);
        }
        // Feeding/steering on the next pass must also continue exactly after reload.
        w.Step(12); restored.Step(12);
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
    }
}
