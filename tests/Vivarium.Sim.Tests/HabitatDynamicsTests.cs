using Vivarium.Sim.Core;
using Vivarium.Sim.Diagnostics;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "HabitatDynamics")]
public sealed class HabitatDynamicsTests
{
    [Fact]
    public void FixedSeedHabitatSurveyIsReproducible()
    {
        var a = TestUtil.FlatWorld(920);
        var b = TestUtil.FlatWorld(920);
        var species = new[] { TestUtil.Content.FloraOrThrow("plumegrass"),
                              TestUtil.Content.FloraOrThrow("blue_sundew") };
        var sa = HabitatSurvey.Capture(a, species);
        var sb = HabitatSurvey.Capture(b, species);
        Assert.Equal(sa.HabitatCells.OrderBy(x => x.Key), sb.HabitatCells.OrderBy(x => x.Key));
        Assert.Equal(sa.ToCsv("plumegrass"), sb.ToCsv("plumegrass"));
    }

    [Fact]
    public void DryingApproximatesSameStateAtDifferentEnvironmentCadences()
    {
        var a = TestUtil.FlatWorld(921);
        var b = TestUtil.FlatWorld(921);
        TestUtil.Condition(a, 0.90, 0.6);
        TestUtil.Condition(b, 0.90, 0.6);
        a.Water.CoupleMoisture(a.Fields.Moisture, a.Content.Ecology, 300, a.Fields.Scratch, a.RootZone);
        for (int i = 0; i < 10; i++)
            b.Water.CoupleMoisture(b.Fields.Moisture, b.Content.Ecology, 30, b.Fields.Scratch, b.RootZone);
        foreach (int cell in a.Grid.DomainCells)
            Assert.InRange(Math.Abs(a.Fields.Moisture.Values[cell] - b.Fields.Moisture.Values[cell]), 0, 0.03);
    }

    [Fact]
    public void EstablishedFloraPersistsWithStableFavorableResources()
    {
        var w = TestUtil.FlatWorld(922);
        TestUtil.Condition(w, 0.60, 0.80, 0.65);
        var sp = TestUtil.Content.FloraOrThrow("coinrunner");
        var plant = w.FloraSystem.Establish(sp, new Vec2(0, 0), "stable-habitat fixture");
        double original = plant.Biomass;
        for (int i = 0; i < 24; i++) w.FloraSystem.Step(3600);
        var present = w.Flora.Get(plant.Id);
        Assert.NotNull(present);
        Assert.True(present.Biomass > original * 0.5);
        Assert.True(present.Health > 0);
    }

    [Fact]
    public void MoistureRecruitmentBoundaryDoesNotInstantlyExpelEstablishedPlant()
    {
        var w = TestUtil.FlatWorld(924);
        var species = TestUtil.Content.FloraOrThrow("pitcher_plant");
        var p = new Vec2(0, 0);
        var plant = w.FloraSystem.Establish(species, p, "existing root");
        TestUtil.Condition(w, species.HardMinMoisture - 0.02,
            species.Nutrients.Optimum * w.Content.Ecology.NutrientMax, species.Light.Optimum);
        var recruit = w.FloraSystem.Suitability(species, p);
        var adult = w.FloraSystem.Suitability(species, p, plant.Id, established: true);
        Assert.True(recruit.HardRefused, "below recruitment moisture boundary");
        Assert.False(adult.HardRefused, "established life stages use broader survival response");
        w.FloraSystem.Step(3600);
        Assert.NotNull(w.Flora.Get(plant.Id));
    }

    [Fact]
    public void PlantGrowthDoesNotDependStronglyOnFloraSchedulingInterval()
    {
        var a = TestUtil.FlatWorld(923);
        var b = TestUtil.FlatWorld(923);
        TestUtil.Condition(a, 0.60, 0.80, 0.65);
        TestUtil.Condition(b, 0.60, 0.80, 0.65);
        var sp = TestUtil.Content.FloraOrThrow("coinrunner");
        var x = a.FloraSystem.Establish(sp, new Vec2(0, 0), "cadence test");
        var y = b.FloraSystem.Establish(sp, new Vec2(0, 0), "cadence test");
        for (int i = 0; i < 6; i++) a.FloraSystem.Step(600);
        for (int i = 0; i < 12; i++) b.FloraSystem.Step(300);
        Assert.InRange(Math.Abs(x.Biomass - y.Biomass), 0, 0.03 * Math.Max(x.Biomass, y.Biomass));
    }
}
