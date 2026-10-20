using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Plasmodium;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "HabitatDynamics")]
public sealed class PlasmodiumHabitatTests
{
    private sealed class Habitat : IPlasmodiumEnvironment
    {
        public double Wet = 0.8;
        public Dictionary<(int, int), double> Food = new();
        public double Moisture(int gx, int gz) => Wet;
        public double Detritus(int gx, int gz) => Food.GetValueOrDefault((gx, gz));
        public double TakeDetritus(int gx, int gz, double amount)
        {
            var cell = (gx, gz);
            double consumed = Math.Min(amount, Food.GetValueOrDefault(cell));
            Food[cell] = Math.Max(0, Food.GetValueOrDefault(cell) - consumed);
            return consumed;
        }
    }

    private static (LifecycleController Lifecycle, PlasmodiumColony Colony, CoverageLayer Layer,
        Habitat Env, Plasmodium Org) Setup()
    {
        var parameters = new PlasmodiumParams { TStarve = 3, TMig = 10,
            StarveDetritusThreshold = 0.05 };
        var colony = new PlasmodiumColony(0, parameters);
        colony.Seed(new[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4) });
        var layer = new CoverageLayer(CoverageLayerId.Plasmodium, 51);
        foreach (var z in Enumerable.Range(0, 5)) layer.SetOcc(0, z, 1);
        return (new LifecycleController(parameters), colony, layer, new Habitat(),
            colony.Organisms.Values.First());
    }

    [Fact]
    public void AForagingFrontWithFoodDoesNotStarveBecauseTheRearIsEmpty()
    {
        var t = Setup();
        t.Env.Food[(1, 4)] = 0.6; // Accessible just beyond the leading edge.
        for (int i = 0; i < 20; i++)
            t.Lifecycle.Step(t.Layer, t.Colony, null, t.Env, i, 1.0);
        Assert.Equal(PlasmodiumState.Foraging, t.Org.State);
        Assert.Equal(0, t.Org.TimeStarving);
    }

    [Fact]
    public void DormantSporesGerminateOnFoodRatherThanOnAnArbitraryTimer()
    {
        var world = TestUtil.FlatWorld(925);
        TestUtil.Condition(world, 0.80, 0.55);
        foreach (int cell in world.Grid.DomainCells) world.Litter.TakeDetritus(cell, 1e6);
        world.CoverageSystem.ReleaseSpore(new FruitingBody((0, 0), 321));
        Assert.Single(world.CoverageSystem.PlasmodiumSpores);
        world.CoverageSystem.StepPlasmodium(5);
        Assert.Single(world.CoverageSystem.PlasmodiumSpores);
        Assert.Equal(0, world.CoverageSystem.CoveredArea("ambervein"));
        var landing = world.CoverageSystem.PlasmodiumSpores.Single();
        var landingPoint = new Vivarium.Sim.Core.Vec2(
            (landing.Gx + 0.5) * CoverageSpec.CellSize,
            (landing.Gz + 0.5) * CoverageSpec.CellSize);
        world.Litter.Deposit(landingPoint, 6.0);
        world.CoverageSystem.StepPlasmodium(5);
        Assert.Empty(world.CoverageSystem.PlasmodiumSpores);
        Assert.True(world.CoverageSystem.CoveredArea("ambervein") > 0);
    }

    [Fact]
    public void SporesDisperseReproduciblyAcrossNearAndFarHabitats()
    {
        var a = TestUtil.FlatWorld(927);
        var b = TestUtil.FlatWorld(927);
        for (int seed = 1; seed < 20; seed++)
        {
            a.CoverageSystem.ReleaseSpore(new FruitingBody((0, 0), seed));
            b.CoverageSystem.ReleaseSpore(new FruitingBody((0, 0), seed));
        }
        Assert.Equal(a.CoverageSystem.ExportPlasmodiumSpores(),
            b.CoverageSystem.ExportPlasmodiumSpores());
        Assert.Contains(a.CoverageSystem.PlasmodiumSpores, spore =>
            Math.Sqrt(spore.Gx * spore.Gx + spore.Gz * spore.Gz) * CoverageSpec.CellSize > 0.7);
        Assert.Contains(a.CoverageSystem.PlasmodiumSpores, spore =>
            Math.Sqrt(spore.Gx * spore.Gx + spore.Gz * spore.Gz) * CoverageSpec.CellSize < 0.25);
    }

    [Fact]
    public void DormantSporeBankSurvivesWorldSaveLoad()
    {
        var world = TestUtil.FlatWorld(926);
        world.CoverageSystem.ReleaseSpore(new FruitingBody((0, 0), 654));
        var expected = world.CoverageSystem.ExportPlasmodiumSpores().ToArray();
        var loaded = Vivarium.Sim.Persistence.WorldSerializer.Deserialize(
            world.Content, Vivarium.Sim.Persistence.WorldSerializer.Serialize(world));
        Assert.Equal(expected, loaded.CoverageSystem.ExportPlasmodiumSpores());
    }

    [Fact]
    public void MigratingPlasmodiumResumesForagingUponFindingNearbyFood()
    {
        var t = Setup();
        for (int i = 0; i < 4; i++)
            t.Lifecycle.Step(t.Layer, t.Colony, null, t.Env, i, 1.0);
        Assert.Equal(PlasmodiumState.Migrating, t.Org.State);
        t.Env.Food[(1, 4)] = 0.3;
        t.Lifecycle.Step(t.Layer, t.Colony, null, t.Env, 5, 1.0);
        Assert.Equal(PlasmodiumState.Foraging, t.Org.State);
        Assert.Equal(0, t.Org.TimeStarving);
    }
}
