using Vivarium.Sim.Content;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Bootstrap")]
public class PresetSizeTests
{
    [Fact]
    public void EveryPresetIsWithinSizeRangeAndSurvivesTwoDays()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        foreach (var kv in content.Presets)
        {
            Assert.InRange(kv.Value.Diameter, WorldDescriptor.MinDiameter, WorldDescriptor.MaxDiameter);
            var w = VivariumWorld.Create(content, kv.Value);
            w.Step((long)(2 * SimUnits.Day / Time.SimClock.FixedStepSeconds));
            Assert.Empty(w.CheckInvariants());
        }
    }
}
