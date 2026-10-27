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
        foreach (string id in new[] { "embercrown", "lanternbrush", "shadebell" })
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
        double nutrient0 = w.Fields.Nutrients[c];

        w.Litter.Step(6 * SimUnits.Day);

        Assert.True(w.Litter.FruitMass[c] < 1);
        Assert.True(w.Litter.FineMass[c] > 0);
        Assert.True(w.Fields.Nutrients[c] > nutrient0);
        Assert.Equal(0, w.Fields.Detritus[c]);
    }

    [Fact]
    public void FruitAndSeedBankRoundTripExactly()
    {
        var w = TestUtil.FlatWorld();
        w.Litter.DepositFruit(new Vec2(0, 0), 0.42);
        w.SeedBank.Deposit("shadebell", new Vec2(0.5, 0), 0.08, 7 * SimUnits.Day);

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
        foreach (string id in new[] { "embercrown", "lanternbrush", "shadebell" })
        {
            var sp = w.Content.FloraOrThrow(id);
            var baseMesh = OrganismMeshes.Flora(sp, 1234);
            var fruitMesh = Assert.IsType<MeshData>(OrganismMeshes.FloraFruiting(sp, 1234));
            Assert.True(fruitMesh.TriangleCount > baseMesh.TriangleCount, $"{id} fruiting mesh did not add reproductive geometry");
        }
    }

    [Fact]
    public void WoodyFruitAreAttachedToBranchesWithoutFloatingDisplacement()
    {
        var w = TestUtil.DefaultWorld();
        var woodyFruiting = new[] { "embercrown", "lanternbrush", "shadebell", "cragparasol", "lacemantle", "splitcrown", "glowbriar" };
        foreach (string id in woodyFruiting)
        {
            var sp = w.Content.FloraOrThrow(id);
            var baseMesh = OrganismMeshes.Flora(sp, 4321);
            var fruitMesh = Assert.IsType<MeshData>(OrganismMeshes.FloraFruiting(sp, 4321));
            Assert.True(fruitMesh.TriangleCount > baseMesh.TriangleCount, $"{id} fruiting mesh did not add reproductive geometry");

            int baseVerts = baseMesh.VertexCount;
            Assert.True(fruitMesh.VertexCount > baseVerts, $"{id} fruit mesh has no added vertices");

            int addedCount = fruitMesh.VertexCount - baseVerts;
            double maxAllowedDist = Math.Max(0.12, (sp.Reproduction?.DisplaySize ?? 0.03) * 6.0);
            int step = Math.Max(1, addedCount / 30);
            for (int i = baseVerts; i < fruitMesh.VertexCount; i += step)
            {
                var p = fruitMesh.Position(i);
                double minDist = double.MaxValue;
                for (int b = 0; b < baseVerts; b++)
                {
                    double d = (p - baseMesh.Position(b)).Length;
                    if (d < minDist) minDist = d;
                }
                Assert.True(minDist <= maxAllowedDist, $"{id} added fruit vertex {p} is floating {minDist:0.000} away from closest branch (threshold {maxAllowedDist:0.000})");
            }
        }
    }
}