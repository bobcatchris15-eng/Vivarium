using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class ReproductionTests
{
    [Fact]
    public void WoodyRosterHasContentDrivenReproduction()
    {
        var w = TestUtil.DefaultWorld();
        foreach (string id in new[] { "ironlace", "umbraheart", "fenneedle", "kiteleaf", "embercrown", "lanternbrush", "shadebell" })
        {
            var sp = w.Content.FloraOrThrow(id);
            Assert.NotNull(sp.Reproduction);
            Assert.True(sp.Reproduction!.MaxAttachedMass > 0);
            Assert.True(sp.Reproduction.DispersalRadius > 0);
        }
    }

    [Fact]
    public void RipeWoodyPlantDropsFruitAndViableSeed()
    {
        var w = TestUtil.FlatWorld();
        var sp = w.Content.FloraOrThrow("embercrown");
        var rp = Assert.IsType<ReproductionDef>(sp.Reproduction);
        var f = w.FloraSystem.Establish(sp, new Vec2(0, 0), "test", sp.MaxBiomass);
        f.Age = sp.MaturityAge + SimUnits.Day;
        f.LastSuitability = 1;
        f.ReproductiveStage = PlantReproductiveStage.Ripe;
        f.FruitLoad = rp.MaxAttachedMass;

        w.ReproductionSystem.Step(rp.RipeTime * 0.5);

        Assert.True(f.FruitLoad < rp.MaxAttachedMass);
        Assert.True(w.Litter.ExportFruit().Sum() > 0);
        Assert.NotEmpty(w.SeedBank.Lots);
        Assert.True(w.Tally.FruitDropped > 0);
        Assert.True(w.Tally.SeedsDeposited > 0);
    }

    [Fact]
    public void FruitRotsIntoFineLitterWithoutVanishing()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0, 0);
        int c = w.Grid.NearestDomainCell(p);
        w.Litter.DepositFruit(p, 1);
        double before = w.Litter.FruitMass[c] + w.Litter.FineMass[c] + w.Fields.Detritus[c];

        w.Litter.Step(6 * SimUnits.Day);

        Assert.True(w.Litter.FruitMass[c] < 1);
        Assert.True(w.Litter.FineMass[c] > 0);
        Assert.Equal(before, w.Litter.FruitMass[c] + w.Litter.FineMass[c] + w.Fields.Detritus[c], 10);
    }

    [Fact]
    public void FruitAndSeedBankRoundTripExactly()
    {
        var w = TestUtil.FlatWorld();
        w.Litter.DepositFruit(new Vec2(0, 0), 0.42);
        w.SeedBank.Deposit("kiteleaf", new Vec2(0.5, 0), 0.08, 7 * SimUnits.Day);

        var restored = WorldSerializer.Deserialize(w.Content, WorldSerializer.Serialize(w));

        Assert.Equal(w.Litter.ExportFruit(), restored.Litter.ExportFruit());
        var a = Assert.Single(w.SeedBank.Lots);
        var b = Assert.Single(restored.SeedBank.Lots);
        Assert.Equal(a.SpeciesId, b.SpeciesId);
        Assert.Equal(a.Cell, b.Cell);
        Assert.Equal(a.ViableMass, b.ViableMass);
        Assert.Equal(a.DormancyRemaining, b.DormancyRemaining);
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
    }

    [Fact]
    public void WoodyReproductiveMeshesIncludeAttachedStructures()
    {
        var w = TestUtil.DefaultWorld();
        foreach (string id in new[] { "ironlace", "umbraheart", "fenneedle", "kiteleaf", "embercrown", "lanternbrush", "shadebell" })
        {
            var sp = w.Content.FloraOrThrow(id);
            var baseMesh = OrganismMeshes.Flora(sp, 1234);
            var fruitMesh = Assert.IsType<MeshData>(OrganismMeshes.FloraFruiting(sp, 1234));
            Assert.True(fruitMesh.TriangleCount > baseMesh.TriangleCount, $"{id} fruiting mesh did not add reproductive geometry");
        }
    }
}
