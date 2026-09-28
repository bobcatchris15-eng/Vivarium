using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

[Trait("Suite", "GrowthLab")]
public class AquaticBiofilmTests
{
    [Fact]
    public void ProjectionAvoidsPerFineCellAllocations()
    {
        var grid = new GridSpec(new HexDomain(2), 0.1);
        var field = new ScalarField("biofilm", grid, 0, 0, 4);
        var bed = new CoverageLayer(CoverageLayerId.AlgaeBed, 17);
        int cell = grid.NearestDomainCell(new(0, 0));
        AquaticBiofilm.ProjectCell(bed, field, cell); // JIT and one-time initialization
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) AquaticBiofilm.ProjectCell(bed, field, cell);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes < 64_000, $"projection allocated {bytes} bytes");
    }

    [Fact]
    public void ProjectionAndGrazingKeepFieldAndAlgaeMassInSync()
    {
        var grid = new GridSpec(new HexDomain(2), 0.1);
        var field = new ScalarField("biofilm", grid, 0, 0, 4);
        var bed = new CoverageLayer((CoverageLayerId)11, 17);
        int cell = grid.NearestDomainCell(new(0, 0));
        var p = grid.CellCenter(cell);
        var (gx, gz) = CoverageSpec.CellOf(p);
        for (int z = gz - 2; z <= gz + 2; z++)
        for (int x = gx - 2; x <= gx + 2; x++) bed.SetCell(x, z, 1, 0.5f, 0, 0, 0, 0, 0);

        AquaticBiofilm.ProjectCell(bed, field, cell);
        Assert.Equal(2, field[cell], 5);
        Assert.Equal(0.75, AquaticBiofilm.GrazeCell(bed, field, cell, 0.75), 5);
        Assert.Equal(1.25, field[cell], 5);
        AquaticBiofilm.ProjectCell(bed, field, cell);
        Assert.Equal(1.25, field[cell], 5);
        Assert.Equal(1.25, AquaticBiofilm.GrazeCell(bed, field, cell, 100), 5);
        Assert.Equal(0, field[cell], 5);
    }
}
