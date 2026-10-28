using Vivarium.Sim.Content;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Coverage")]
public class CoverageAsyncTests
{
    private const int Steps = 6;
    private static readonly double Dt = SimUnits.Day / 4;

    private static VivariumWorld Run(bool async, bool backToBack)
    {
        var w = TestUtil.DefaultWorld(20260923, bio: TestUtil.ShippedBio);
        var cs = w.CoverageSystem;
        cs.AsyncMode = async;
        for (int i = 0; i < Steps; i++)
        {
            cs.StepMat(Dt);
            if (!backToBack) cs.WaitPending();
            cs.StepLichen(Dt); // back-to-back: usually lands while the mat task runs -> deferred, not dropped
            cs.WaitPending();
        }
        return w;
    }

    [Fact]
    public void AsyncMatchesSyncWithTickBoundaryJoins()
    {
        Assert.Equal(TestUtil.Digest(Run(async: false, backToBack: false)), TestUtil.Digest(Run(async: true, backToBack: false)));
    }

    [Fact]
    public void AsyncDefersInsteadOfSkippingOverlappingSteps()
    {
        Assert.Equal(TestUtil.Digest(Run(async: false, backToBack: true)), TestUtil.Digest(Run(async: true, backToBack: true)));
    }
}
