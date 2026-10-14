using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public sealed class FruitRecruitmentRegressionTests
{
    [Theory]
    [InlineData("embercrown", 811)]
    [InlineData("lanternbrush", 812)]
    [InlineData("shadebell", 813)]
    public void ActualWoodyFruitCycleEventuallyRecruitsOutsideTheParentCanopy(string speciesId, ulong seed)
    {
        var world = TestUtil.FlatWorld(seed);
        TestUtil.Condition(world, 0.72, 0.85, 0.72);
        var species = world.Content.FloraOrThrow(speciesId);
        var reproduction = Assert.IsType<ReproductionDef>(species.Reproduction);
        var parent = world.FloraSystem.Establish(species, Vec2.Zero, "regression-parent", species.MaxBiomass);
        parent.Age = species.MaturityAge + SimUnits.Day;
        parent.LastSuitability = 1;

        for (int day = 0; day < 900; day++)
        {
            world.ReproductionSystem.Step(SimUnits.Day);
            world.SeedBank.Step(SimUnits.Day);
        }

        Assert.True(world.Tally.FruitDropped > 0, $"{speciesId} never detached its configured fruit");
        Assert.True(world.Tally.SeedsDeposited > 0, $"{speciesId} did not deposit viable seed");
        var lots = world.SeedBank.Lots.Where(lot => lot.SpeciesId == speciesId).ToArray();
        Assert.All(lots, lot => Assert.True(
            Vec2.Distance(parent.Position, world.Grid.CellCenter(lot.Cell)) <= reproduction.DispersalRadius,
            $"{speciesId} deposited lot outside its authored dispersal radius"));
        var bestLot = lots.OrderByDescending(lot => lot.ViableMass).FirstOrDefault();
        var point = bestLot == null ? Vec2.Zero : world.Grid.CellCenter(bestLot.Cell);
        var can = bestLot != null && world.FloraSystem.CanEstablish(species, point, out _);
        var reason = bestLot == null ? "no viable lots remain" : world.FloraSystem.CanEstablish(species, point, out var why) ? "ok" : why;
        Assert.True(world.Tally.SeedsGerminated > 0, $"{speciesId}: germinated={world.Tally.SeedsGerminated}, deposited={world.Tally.SeedsDeposited:0.0000}, lots={lots.Length}, maxLot={bestLot?.ViableMass:0.0000}, attempts={bestLot?.AttemptCount}, candidateDistance={Vec2.Distance(parent.Position, point):0.000}, CanEstablish={can}, reason={reason}");
        var recruits = world.Flora.Items.Where(f => f.Id != parent.Id && f.SpeciesId == speciesId).ToArray();
        Assert.NotEmpty(recruits);
        Assert.All(recruits, recruit =>
        {
            Assert.True(Vec2.Distance(parent.Position, recruit.Position) >= species.Woody!.MinSpacing);
            Assert.True(Vec2.Distance(parent.Position, recruit.Position) <= reproduction.DispersalRadius,
                $"{speciesId} recruit at {Vec2.Distance(parent.Position, recruit.Position):0.000}m exceeds authored {reproduction.DispersalRadius:0.000}m dispersal radius");
        });
        Assert.True(world.Litter.ExportFruit().Sum() > 0, $"{speciesId} fruit should remain in the ground fruit pool");
        Assert.True(reproduction.MaxAttachedMass > 0);
    }

    [Fact]
    public void SubthresholdViableSeedIsRetainedUntilItCanAccumulateOrGerminate()
    {
        var world = TestUtil.FlatWorld();
        world.SeedBank.Deposit("lanternbrush", Vec2.Zero, 0.0005, 10 * SimUnits.Day);

        world.SeedBank.Step(SimUnits.Day);

        var lot = Assert.Single(world.SeedBank.Lots);
        Assert.True(lot.ViableMass > 0.00049);
    }

    [Fact]
    public void GerminationWaitsUntilLotCanFundTheRecruit()
    {
        var world = TestUtil.FlatWorld();
        TestUtil.Condition(world, 0.72, 0.85, 0.72);
        var species = world.Content.FloraOrThrow("lanternbrush");
        world.FloraSystem.Establish(species, Vec2.Zero, "spacing-parent", species.MaxBiomass);
        world.SeedBank.Deposit(species.Id, new Vec2(1.0, 0), species.InitialBiomass * 0.5, 0);

        world.SeedBank.Step(5 * SimUnits.Day);

        Assert.DoesNotContain(world.Flora.Items, f => f.SpeciesId == species.Id && f.Position != Vec2.Zero);
        Assert.InRange(Assert.Single(world.SeedBank.Lots).ViableMass,
            species.InitialBiomass * 0.49, species.InitialBiomass * 0.5);
    }

    [Fact]
    public void DepositedSeedDoesNotWanderOnRepeatedFailedAttempts()
    {
        var world = TestUtil.FlatWorld();
        var species = world.Content.FloraOrThrow("lanternbrush");
        world.SeedBank.Deposit(species.Id, new Vec2(1.0, 0), species.InitialBiomass, 0);
        var lot = Assert.Single(world.SeedBank.Lots);
        int depositedCell = lot.Cell;

        world.SeedBank.Step(5 * SimUnits.Day);
        var firstPosition = world.Flora.Items.Where(f => f.SpeciesId == species.Id).Select(f => f.Position).ToArray();
        world.SeedBank.Step(5 * SimUnits.Day);
        var secondPosition = world.Flora.Items.Where(f => f.SpeciesId == species.Id).Select(f => f.Position).ToArray();

        Assert.Equal(depositedCell, lot.Cell);
        Assert.Equal(2, lot.AttemptCount);
        Assert.Equal(firstPosition, secondPosition);
    }

    [Fact]
    public void ReleasedFruitAndViableSeedAccountForTheFullReproductiveMass()
    {
        var world = TestUtil.FlatWorld();
        var species = world.Content.FloraOrThrow("lanternbrush");
        var reproduction = Assert.IsType<ReproductionDef>(species.Reproduction);
        var parent = world.FloraSystem.Establish(species, Vec2.Zero, "mass-test", species.MaxBiomass);
        parent.Age = species.MaturityAge + SimUnits.Day;
        parent.LastSuitability = 1;
        parent.ReproductiveStage = PlantReproductiveStage.Ripe;
        parent.FruitLoad = reproduction.MaxAttachedMass;
        double fruitBefore = world.Litter.ExportFruit().Sum();
        double seedBefore = world.Tally.SeedsDeposited;

        world.ReproductionSystem.Step(reproduction.RipeTime * 0.5);

        double released = reproduction.MaxAttachedMass - parent.FruitLoad;
        double fruit = world.Litter.ExportFruit().Sum() - fruitBefore;
        double viableSeed = world.Tally.SeedsDeposited - seedBefore;
        Assert.Equal(released, fruit + viableSeed, 10);
    }
}
