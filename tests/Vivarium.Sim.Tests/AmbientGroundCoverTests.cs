using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class AmbientGroundCoverTests
{
    [Fact]
    public void VacantMesicSoilGetsBroadAnonymousCover()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        TestUtil.Condition(w, moisture: 0.5, nutrients: w.Content.Ecology.NutrientMax * 0.6, light: 0.8);
        w.AmbientGroundCover.InitializeFromHabitat();

        var values = w.Grid.DomainCells.Select(i => w.AmbientGroundCover.Cover[i]).ToArray();
        Assert.True(values.Average() > 0.55);
        Assert.All(values, x => Assert.InRange(x, 0, 1));
        Assert.Empty(w.Flora.Items); // filler is not represented as organisms
    }

    [Fact]
    public void NamedPlantCarvesAVisualExclusionHole()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        TestUtil.Condition(w, moisture: 0.5, nutrients: w.Content.Ecology.NutrientMax * 0.6, light: 0.8);
        w.AmbientGroundCover.InitializeFromHabitat();
        int cell = w.Grid.DomainCells.OrderByDescending(i => w.AmbientGroundCover.Cover[i]).First();
        var p = w.Grid.CellCenter(cell);
        double open = w.AmbientGroundCover.Cover[cell];
        Assert.True(open > 0.4);

        var sp = w.Content.FloraOrThrow("prismstar");
        w.FloraSystem.Establish(sp, p, "test", sp.MaxBiomass);
        w.AmbientGroundCover.InitializeFromHabitat();

        Assert.True(w.AmbientGroundCover.Cover[cell] < open * 0.25);
    }

    [Fact]
    public void AmbientCoverDoesNotConsumeEcologicalResources()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        int cell = w.Grid.NearestDomainCell(new Vec2(0, 0));
        double n = w.Fields.Nutrients.Values[cell];
        double d = w.Fields.Detritus.Values[cell];

        w.AmbientGroundCover.Step(30 * Vivarium.Sim.Content.SimUnits.Day);

        Assert.Equal(n, w.Fields.Nutrients.Values[cell]);
        Assert.Equal(d, w.Fields.Detritus.Values[cell]);
        Assert.Empty(w.Flora.Items);
    }

    [Fact]
    public void AmbientCoverRoundTripsExactly()
    {
        var w = TestUtil.DefaultWorld();
        w.AmbientGroundCover.Step(2 * Vivarium.Sim.Content.SimUnits.Day);
        var restored = WorldSerializer.Deserialize(w.Content, WorldSerializer.Serialize(w));

        Assert.Equal(w.AmbientGroundCover.Export(), restored.AmbientGroundCover.Export());
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(restored));
    }
}
