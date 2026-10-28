using System.Text.Json;
using Vivarium.Sim.Persistence;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class PropSetCloneTests
{
    [Fact]
    public void CloneMatchesJsonRoundTrip()
    {
        var w = TestUtil.DefaultWorld(20260923);
        Assert.NotEmpty(w.Props.Rocks);
        var clone = w.Props.Clone();
        Assert.Equal(JsonSerializer.Serialize(w.Props, WorldSerializer.Json), JsonSerializer.Serialize(clone, WorldSerializer.Json));
        Assert.NotSame(w.Props.Rocks[0], clone.Rocks[0]);
    }
}
