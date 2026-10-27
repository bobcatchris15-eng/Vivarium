using System.Linq;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Vivarium.Sim.Content;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

[Trait("Suite", "GrowthLab")]
public class AquaticWorldTests
{
    [Fact]
    public void FlowAdvectionUsesPhysicalSecondsRegardlessOfBioAcceleration()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        int cell = w.Grid.DomainCells.First(w.Water.IsWaterTable);
        TestUtil.Flood(w, w.Grid.CellCenter(cell), 1.0, 0.2);
        foreach (int c in w.Grid.DomainCells) { w.Water.FlowX[c] = 0.001; w.Water.FlowZ[c] = 0; w.Fields.Light[c] = 0; w.Fields.Nutrients[c] = 0; }
        var (gx, gz) = CoverageSpec.CellOf(w.Grid.CellCenter(cell));
        float[] Run(double bioSeconds)
        {
            w.Coverage.SurfaceFloat.Clear();
            w.Coverage.SurfaceFloat.SetCell(gx, gz, SurfaceFloatRules.OccupantId, 0.5f, 0, 0, 0, 0, 0);
            w.AquaticSystem.Step(physicalSeconds: 10, bioSeconds);
            return Enumerable.Range(gx - 2, 6).Select(x => w.Coverage.SurfaceFloat.GetB(x, gz)).ToArray();
        }
        var physical = Run(10);
        Assert.True(physical[3] > 0, "flow must actually move biomass into the next fine cell");
        Assert.Equal(physical, Run(100));
    }

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
        w.AquaticSystem.Step(1, 1);
        Assert.True(w.Fields.Biofilm[wet] > 0);
        Assert.True(w.Fields.Biofilm.AllFinite());
        Assert.Contains(w.Scheduler.Systems, s => s.Name == "aquatic");
    }

    [Fact]
    public void SixDayRunEstablishesPersistentAquaticPopulations()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        foreach (var name in new[] { "default", "isolated_pond", "creek" })
        {
            var preset = content.PresetOrThrow(name);
            preset.BioAcceleration = TestUtil.ShippedBio;
            var w = VivariumWorld.Create(content, preset);
            for (int day = 0; day < 6; day++)
                w.Step(TestUtil.TicksPerBioDay());

            int bed = w.Coverage.AlgaeBed.Tiles.Sum(t => t.B.Count(b => b > 0.05f));
            int flt = w.Coverage.AlgaeFloat.Tiles.Sum(t => t.B.Count(b => b > 0.05f));
            int duck = w.Coverage.SurfaceFloat.Tiles.Sum(t => t.B.Count(b => b > 0.05f));

            Assert.True(bed > 100, $"{name} should establish healthy benthic periphyton (got {bed} cells)");
            Assert.True(flt > 50, $"{name} should establish healthy floating algae (got {flt} cells)");
            Assert.True(duck > 100, $"{name} should establish healthy duckweed cover (got {duck} cells)");
        }
    }
}
