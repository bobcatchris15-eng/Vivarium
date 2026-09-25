using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Coverage")]
public class CoverageSystemTests
{
    private static readonly string[] MossSpecies = { "carpet_moss", "wetbank_moss", "cushion_moss", "sphagnum" };
    private static readonly string[] LichenSpecies = { "crust_lichen", "foliose_lichen", "reindeer_lichen" };

    /// <summary>Advances a default-preset world by <paramref name="bioDays"/> biological days at the shipped
    /// acceleration, matching the Reference-scenario convention used elsewhere in this suite.</summary>
    private static VivariumWorld RunDefault(int bioDays, ulong? seed = null)
    {
        var w = TestUtil.DefaultWorld(seed, bio: TestUtil.ShippedBio);
        for (int day = 0; day < bioDays; day++) w.Step(TestUtil.TicksPerBioDay());
        return w;
    }

    [Fact]
    public void CoverageSpeciesSeedWithoutFloraIndividuals()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var id in MossSpecies.Concat(LichenSpecies))
            Assert.True(w.CoverageSystem.CoveredArea(id) > 0, $"{id} has no initial coverage");
        Assert.DoesNotContain(w.Flora.Items, f => w.Content.FloraById(f.SpeciesId)?.IsCoverageSpecies == true);
    }

    [Fact]
    public void OccupantLookupResolvesLayerSpecificSpecies()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var layer in new[] { w.Coverage.Mat, w.Coverage.Crust })
        foreach (var tile in layer.Tiles)
        for (int i = 0; i < CoverageTile.N; i++)
        {
            if (tile.Occ[i] == 0) continue;
            var id = w.CoverageSystem.SpeciesId(layer.Id, tile.Occ[i]);
            Assert.NotNull(id);
            Assert.True(w.Content.FloraById(id)?.IsCoverageSpecies);
        }
        Assert.Null(w.CoverageSystem.SpeciesId(CoverageLayerId.Mat, 0));
        Assert.Null(w.CoverageSystem.SpeciesId(CoverageLayerId.Plasmodium, 1));
    }

    [Fact]
    public void InitialLichenCellsRespectTheirSubstrate()
    {
        var w = TestUtil.DefaultWorld();
        foreach (var t in w.Coverage.Crust.Tiles)
        for (int li = 0; li < CoverageTile.N; li++)
        {
            if (t.Occ[li] == 0) continue;
            int gx = t.Ti * CoverageSpec.TileEdge + li % CoverageSpec.TileEdge;
            int gz = t.Tj * CoverageSpec.TileEdge + li / CoverageSpec.TileEdge;
            var e = w.CoverageSystem.Sample(gx, gz);
            Assert.True(e.Substrate is CoverageSubstrate.Rock or CoverageSubstrate.Log or CoverageSubstrate.Bark or CoverageSubstrate.StableSoil or CoverageSubstrate.Gravel,
                $"initial lichen cell ({gx},{gz}) on {e.Substrate}");
        }
    }

    [Fact]
    public void AllSevenCoverageSpeciesHaveCoveredAreaAfterSixDays()
    {
        var w = RunDefault(6);
        foreach (var id in MossSpecies.Concat(LichenSpecies))
        {
            double area = w.CoverageSystem.CoveredArea(id);
            Assert.True(area > 0, $"{id} should have covered area > 0 after 6 bio-days (got {area})");
        }
    }

    [Fact]
    public void LichensOnlyOnStableSubstrate()
    {
        var w = RunDefault(6);
        foreach (var t in w.Coverage.Crust.Tiles)
        {
            for (int li = 0; li < CoverageTile.N; li++)
            {
                if (t.Occ[li] == 0) continue;
                int lx = li % CoverageSpec.TileEdge, lz = li / CoverageSpec.TileEdge;
                int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
                var p = new Core.Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
                var e = CoverageEnvironment.Sample(w, p);
                Assert.True(
                    e.Substrate is CoverageSubstrate.Rock or CoverageSubstrate.Log or CoverageSubstrate.Bark or CoverageSubstrate.StableSoil or CoverageSubstrate.Gravel,
                    $"lichen cell at ({gx},{gz}) sits on {e.Substrate}");
            }
        }
    }

    [Fact]
    public void MossIsMostlyOnMoistCells()
    {
        var w = RunDefault(6);
        int total = 0, moist = 0;
        foreach (var t in w.Coverage.Mat.Tiles)
        {
            for (int li = 0; li < CoverageTile.N; li++)
            {
                if (t.Occ[li] == 0) continue;
                int lx = li % CoverageSpec.TileEdge, lz = li / CoverageSpec.TileEdge;
                int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
                var p = new Core.Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
                var e = CoverageEnvironment.Sample(w, p);
                total++;
                if (e.Moisture >= 0.4) moist++;
            }
        }
        Assert.True(total > 0, "moss should have grown some cells");
        Assert.True(moist / (double)total > 0.6, $"expected most moss cells on moist ground, got {moist}/{total}");
    }

    [Fact]
    public void NoFloraIndividualEverExistsForACoverageSpecies()
    {
        var w = RunDefault(6);
        foreach (var f in w.Flora.Items)
        {
            var sp = w.Content.FloraById(f.SpeciesId);
            Assert.False(sp is { IsCoverageSpecies: true }, $"found a FloraIndividual of coverage species {f.SpeciesId}");
        }
    }

    [Fact]
    public void CoverageIsDeterministic()
    {
        var w1 = RunDefault(2, seed: 20260923);
        var w2 = RunDefault(2, seed: 20260923);
        Assert.Equal(TestUtil.Digest(w1), TestUtil.Digest(w2));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PerfPrintCoverageStepCost()
    {
        var w = TestUtil.DefaultWorld(bio: TestUtil.ShippedBio);
        for (int day = 0; day < 6; day++) w.Step(TestUtil.TicksPerBioDay());
        var stats = w.CoverageSystem.LastMatStats;
        double totalMs = stats.PhysiologyMs + stats.SpreadMs + stats.D2EMs;
        Console.WriteLine($"[CoverageSystem] last Mat step: physiology={stats.PhysiologyMs:0.000}ms spread={stats.SpreadMs:0.000}ms d2e={stats.D2EMs:0.000}ms total={totalMs:0.000}ms");
        foreach (var id in MossSpecies.Concat(LichenSpecies))
            Console.WriteLine($"[CoverageSystem] {id}: {w.CoverageSystem.CoveredArea(id):0.0000} m^2 at 6 bio-days");
    }
}
