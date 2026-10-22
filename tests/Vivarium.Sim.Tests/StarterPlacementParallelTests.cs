using System.Linq;
using Vivarium.Sim.Ecology;
using Xunit;

namespace Vivarium.Sim.Tests;

public class StarterPlacementParallelTests
{
    [Fact]
    public void ParallelScoringPlacesTheSameStartersAsTheSequentialScan()
    {
        Populate.ForceSequentialScoring = true;
        string sequential;
        try { sequential = TestUtil.Digest(TestUtil.DefaultWorld(20261020)); }
        finally { Populate.ForceSequentialScoring = false; }
        string parallel = TestUtil.Digest(TestUtil.DefaultWorld(20261020));
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void TheSameSeedAlwaysGrowsTheSameIsland()
    {
        Populate.ForceSequentialScoring = true;
        try
        {
            string first = TestUtil.Digest(TestUtil.DefaultWorld(20261020));
            string second = TestUtil.Digest(TestUtil.DefaultWorld(20261020));
            Assert.Equal(first, second);
        }
        finally { Populate.ForceSequentialScoring = false; }
    }
}
