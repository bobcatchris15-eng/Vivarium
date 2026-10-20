using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

public class UnderstoryVisualTests
{
    [Fact]
    public void RaisedShrubCanopyAllowsLowCoverOutsideItsBasalCollar()
    {
        var world = TestUtil.FlatWorld();
        TestUtil.Condition(world, moisture: .5, nutrients: world.Content.Ecology.NutrientMax * .6, light: .8);
        int rootCell = world.Grid.NearestDomainCell(Vec2.Zero);
        var root = world.Grid.CellCenter(rootCell);
        var species = world.Content.FloraOrThrow("shadebell");
        world.FloraSystem.Establish(species, root, "test", species.MaxBiomass);
        var shrub = world.Flora.Items.Single(f => f.SpeciesId == species.Id);
        double radius = shrub.Radius(species);
        var before = WorldSerializer.SubsystemDigests(world);
        world.AmbientGroundCover.InitializeFromHabitat();
        var after = WorldSerializer.SubsystemDigests(world);

        Assert.Equal(0, world.AmbientGroundCover.Cover[rootCell]);
        var beneathCrown = world.Grid.DomainCells.Where(c =>
        {
            double d = Vec2.Distance(root, world.Grid.CellCenter(c));
            return d > radius * .60 && d < radius * .85;
        }).ToArray();
        Assert.NotEmpty(beneathCrown);
        Assert.Contains(beneathCrown, c => world.AmbientGroundCover.Cover[c] > .35);
        foreach (var key in before.Keys.Where(k => k != "fields")) Assert.Equal(before[key], after[key]);
    }
}
