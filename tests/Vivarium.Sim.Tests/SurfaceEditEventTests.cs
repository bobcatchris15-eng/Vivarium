using Vivarium.Sim.Core;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class SurfaceEditEventTests
{
    [Fact]
    public void SuccessfulPlantEditsInvalidateTheirRegionAndFailuresDoNot()
    {
        var world = TestUtil.FlatWorld();
        var tools = new ToolActions(world);
        var edits = new List<(Vec2? Centre, double Radius)>();
        tools.SurfaceEdited += (p, radius) => edits.Add((p, radius));
        var position = new Vec2(2, 1);
        Assert.False(tools.IntroduceFlora("embercrust_lichen", position).Ok);
        Assert.Empty(edits);
        var planted = tools.IntroduceFlora("coinrunner", position);
        Assert.True(planted.Ok, planted.Message);
        Assert.Single(edits);
        Assert.Equal(position, edits[0].Centre);
        Assert.True(edits[0].Radius >= 1);
        Assert.True(tools.RemovePlant(planted.Affected[0]).Ok);
        Assert.Equal(2, edits.Count);
        Assert.False(tools.RemovePlant(planted.Affected[0]).Ok);
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public void EcologyAndFailedBrushesDoNotQueueDressingButValidSculptDoes()
    {
        var world = TestUtil.FlatWorld();
        var tools = new ToolActions(world);
        int edits = 0;
        tools.SurfaceEdited += (_, _) => edits++;
        world.Step(5);
        Assert.Equal(0, edits);
        Assert.False(tools.Sculpt(new Vec2(40, 0), .8, SculptMode.Raise, .1).Ok);
        Assert.Equal(0, edits);
        Assert.True(tools.Sculpt(new Vec2(1, 1), .8, SculptMode.Raise, .1).Ok);
        Assert.Equal(1, edits);
    }
}
