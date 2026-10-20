using Vivarium.Sim.Content;
using Xunit;

namespace Vivarium.Sim.Tests;

public class NicheResponseTests
{
    [Fact]
    public void AdultSurvivalHasWiderToleranceThanRecruitment()
    {
        var preference = new Pref(0.5, 0.12);
        double outsideOptimum = 0.72;
        Assert.True(preference.EvalStage(outsideOptimum, 1.65) >
                    preference.Eval(outsideOptimum));
        Assert.True(preference.Eval(outsideOptimum) >
                    preference.EvalStage(outsideOptimum, 0.85));
        Assert.Equal(1.0, preference.EvalStage(0.5, 0.85), 12);
        Assert.Equal(1.0, preference.EvalStage(0.5, 1.65), 12);
    }
}
