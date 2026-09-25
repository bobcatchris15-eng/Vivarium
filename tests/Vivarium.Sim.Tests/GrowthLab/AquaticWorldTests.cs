using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

[Trait("Suite", "GrowthLab")]
public class AquaticWorldTests
{
    [Fact]
    public void WorldSeedsAquaticLayersAndProjectsBedBiomass()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        Assert.Contains(w.Coverage.AlgaeBed.Tiles, t => t.B.Any(b => b > 0));
        Assert.Contains(w.Coverage.AlgaeFloat.Tiles, t => t.B.Any(b => b > 0));
        Assert.Contains(w.Coverage.SurfaceFloat.Tiles, t => t.B.Any(b => b > 0));
        Assert.True(w.Fields.Biofilm.Total() > 0);

        int wet = w.Grid.DomainCells.First(c => w.Water.IsWet(c));
        var (gx, gz) = CoverageSpec.CellOf(w.Grid.CellCenter(wet));
        w.Coverage.AlgaeBed.SetCell(gx, gz, AlgaeRules.OccupantId, 0.8f, 0, 0, 0, 0, 0);
        w.AquaticSystem.ProjectBiofilm();
        Assert.True(w.Fields.Biofilm[wet] > 0);
        w.AquaticSystem.Step(1);
        Assert.True(w.Fields.Biofilm[wet] > 0);
        Assert.True(w.Fields.Biofilm.AllFinite());
        Assert.Contains(w.Scheduler.Systems, s => s.Name == "aquatic");
    }
}
