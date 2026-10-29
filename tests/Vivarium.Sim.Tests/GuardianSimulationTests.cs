using System;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

public class GuardianSimulationTests
{
    private static ContentLibrary Content => ContentLoader.Load(TestUtil.ContentSource);

    [Fact]
    public void SatisfiedGuardiansKeepAnIslandDestinationAndChooseAnotherOnArrival()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var descriptor = Content.PresetOrThrow("default").Clone();
            descriptor.PilotTreeId = def.AssociatedPilotTreeId;
            descriptor.GuardianId = def.Id;
            var world = VivariumWorld.Create(Content, descriptor);
            var g = world.Guardian!;
            g.Energy = def.EnergyMax; g.Hydration = 1; g.Comfort = 1;
            g.Fatigue = .1; g.BodyTemperature = 1; g.Arousal = 0;
            g.ActivityDuration = 0;
            void Step() => world.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * .1);
            Step();
            Assert.Equal(GuardianActivity.Explore, g.Activity);
            var target = g.TargetPosition;
            Assert.True(Vec2.Distance(g.PositionXZ, new Vec2(target.X, target.Z)) >= Math.Min(2.0, world.Domain.Radius * .4) - .05);
            for (int i = 0; i < 10; i++) Step();
            Assert.Equal(target, g.TargetPosition);
            g.Position = target;
            Step();
            Assert.Equal(GuardianActivity.Explore, g.Activity);
            Assert.NotEqual(target, g.TargetPosition);
            g.Energy = 0;
            Step();
            Assert.True(g.Activity is GuardianActivity.Forage or GuardianActivity.Feed);
        }
    }

    [Fact]
    public void AllGuardiansSpawnOutsideTrunkAndMoveDuringFirstMinute()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var descriptor = Content.PresetOrThrow("default").Clone();
            descriptor.PilotTreeId = def.AssociatedPilotTreeId;
            descriptor.GuardianId = def.Id;
            var world = VivariumWorld.Create(Content, descriptor);
            var guardian = world.Guardian!;
            Assert.False(world.PilotTree!.BlocksTrunk(guardian.PositionXZ, 0.35));
            Assert.Equal(world.GroundHeight(guardian.PositionXZ), guardian.Y, 6);
            double distance = 0;
            for (int i = 0; i < 252; i++)
            {
                var before = guardian.PositionXZ;
                world.GuardianSystem.StepBehaviour(40);
                double moved = Vec2.Distance(before, guardian.PositionXZ);
                Assert.True(moved <= def.RunSpeed * 40 / Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond + 1e-6,
                    def.Id + " exceeded its authored locomotion rate: " + moved);
                distance += moved;
                if (guardian.Activity != GuardianActivity.Climb) Assert.False(world.PilotTree.BlocksTrunk(guardian.PositionXZ, 0.35));
                Assert.True(world.Domain.Contains(guardian.PositionXZ));
            }
            Assert.True(distance > 1.0, def.Id + " travelled only " + distance);
        }
    }

    [Fact]
    public void SmallerGuardiansTravelAtTheSameBodyRelativeStrideRate()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var descriptor = Content.PresetOrThrow("default").Clone();
            descriptor.PilotTreeId = def.AssociatedPilotTreeId;
            descriptor.GuardianId = def.Id;
            var world = VivariumWorld.Create(Content, descriptor);
            var g = world.Guardian!;
            g.X = 0; g.Z = 0; g.Heading = 0;
            g.TargetPosition = new Vec3(0.5, world.GroundHeight(new Vec2(0.5, 0)), 0);
            g.Activity = GuardianActivity.Explore; g.ActivityDuration = 100;
            // This test isolates stride rate from the need-driven interruption.
            g.Energy = def.EnergyMax; g.Hydration = 1; g.Comfort = 1;
            g.Fatigue = .1; g.BodyTemperature = 1; g.Arousal = 0;
            double individualScale = g.Scale;
            world.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * 0.1);
            Assert.Equal(def.WalkSpeed * 0.5 * 0.1, g.X, 6);
            Assert.Equal(0, g.Z, 6);
            Assert.Equal(individualScale, g.Scale);
        }
    }

    [Fact]
    public void LoadedGuardianInsideTrunkIsMovedToSafeSupport()
    {
        var descriptor = Content.PresetOrThrow("default").Clone();
        descriptor.PilotTreeId = "basinwarden";
        descriptor.GuardianId = GuardianCatalog.Woodland;
        var world = VivariumWorld.Create(Content, descriptor);
        var guardian = world.Guardian!;
        guardian.X = world.PilotTree!.Anchor.X;
        guardian.Z = world.PilotTree.Anchor.Z;
        world.GuardianSystem.StepBehaviour(40);
        if (guardian.Activity != GuardianActivity.Climb) Assert.False(world.PilotTree.BlocksTrunk(guardian.PositionXZ, 0.35));
        Assert.Equal(world.GroundHeight(guardian.PositionXZ), guardian.Y, 6);
    }

    [Fact]
    public void CatalogMapsAllSixPilotTreesToOneToOneGuardians()
    {
        Assert.Equal(6, GuardianCatalog.All.Count);
        Assert.True(PilotTreeCatalog.All.Count >= 6);

        foreach (var guardian in GuardianCatalog.All)
        {
            var tree = PilotTreeCatalog.Find(guardian.AssociatedPilotTreeId);
            Assert.NotNull(tree);
            Assert.Equal(guardian.AssociatedPilotTreeId, tree.Id);
            Assert.Equal(guardian, GuardianCatalog.ForPilotTree(tree.Id));
        }

        Assert.Equal(GuardianCatalog.CrestedCanopy, GuardianCatalog.ForPilotTree("crowncoil")!.Id);
        Assert.Equal(GuardianCatalog.ArmoredForest, GuardianCatalog.ForPilotTree("gloomspire")!.Id);
        Assert.Equal(GuardianCatalog.CanopySerpent, GuardianCatalog.ForPilotTree("needlevault")!.Id);
        Assert.Equal(GuardianCatalog.GreatGround, GuardianCatalog.ForPilotTree("emberpillar")!.Id);
        Assert.Equal(GuardianCatalog.Woodland, GuardianCatalog.ForPilotTree("basinwarden")!.Id);
        Assert.Equal(GuardianCatalog.QuilledArboreal, GuardianCatalog.ForPilotTree("palehollow")!.Id);
    }

    [Fact]
    public void WorldCreationSpawnsAssociatedGuardianForPilotTree()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "emberpillar"; // redwood -> monitor lizard
        d.GuardianId = "auto";

        var w = VivariumWorld.Create(Content, d);
        Assert.NotNull(w.PilotTree);
        Assert.NotNull(w.Guardian);
        Assert.Equal(GuardianCatalog.GreatGround, w.Guardian.ArchetypeId);
        Assert.Equal("emberpillar", w.Guardian.HomeTreeId);
        Assert.True(w.Domain.Contains(w.Guardian.PositionXZ));
        Assert.True(w.Guardian.Energy > 0);
    }

    [Fact]
    public void GuardianStateSurvivesSerializationAndDeserialization()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "basinwarden"; // broadleaf -> coati
        d.GuardianId = GuardianCatalog.Woodland;
        var w = VivariumWorld.Create(Content, d);
        Assert.NotNull(w.Guardian);

        w.Guardian.Energy = 77.5;
        w.Guardian.Fatigue = 0.42;
        w.Guardian.Activity = GuardianActivity.Forage;
        w.Guardian.PendingSeeds.Add(new GuardianPendingSeed
        {
            SpeciesId = "shadebell",
            ViableMass = 0.05,
            BioSecondsUntilDeposit = 300
        });

        var payloads = WorldSerializer.Serialize(w);
        Assert.True(payloads.ContainsKey("guardian"));

        var loaded = WorldSerializer.Deserialize(Content, payloads);
        Assert.NotNull(loaded.Guardian);
        Assert.Equal(w.Guardian.Id, loaded.Guardian.Id);
        Assert.Equal(w.Guardian.ArchetypeId, loaded.Guardian.ArchetypeId);
        Assert.Equal(77.5, loaded.Guardian.Energy, 3);
        Assert.Equal(0.42, loaded.Guardian.Fatigue, 3);
        Assert.Equal(GuardianActivity.Forage, loaded.Guardian.Activity);
        Assert.Single(loaded.Guardian.PendingSeeds);
        Assert.Equal("shadebell", loaded.Guardian.PendingSeeds[0].SpeciesId);
    }

    [Fact]
    public void RemoteFoodTargetMustBeApproachedBeforeFeeding()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var d = Content.PresetOrThrow("default").Clone(); d.PilotTreeId = def.AssociatedPilotTreeId; d.GuardianId = def.Id;
            var w = VivariumWorld.Create(Content, d); var g = w.Guardian!;
            g.Position = new Vec3(0, w.GroundHeight(Vec2.Zero), 0);
            g.TargetPosition = new Vec3(.35, w.GroundHeight(new Vec2(.35, 0)), 0);
            int local = w.Grid.NearestDomainCell(g.PositionXZ);
            int target = w.Grid.NearestDomainCell(new Vec2(.35, 0));
            w.Litter.FruitMass[local] = w.Litter.FruitMass[target] = 1;
            g.Activity = GuardianActivity.Feed; g.ActivityDuration = 100;
            double energy = g.Energy;
            w.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * .1);
            Assert.Equal(GuardianActivity.Forage, g.Activity);
            Assert.Equal(energy, g.Energy);
            Assert.Equal(1, w.Litter.FruitMass[local]); Assert.Equal(1, w.Litter.FruitMass[target]);
            Assert.True(g.X > 0, def.Id + " should approach the remote food patch");
        }
    }

    [Fact]
    public void FeedingAcrossCellBoundaryConsumesTargetAndKeepsFeetPlanted()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var d = Content.PresetOrThrow("default").Clone(); d.PilotTreeId = def.AssociatedPilotTreeId; d.GuardianId = def.Id;
            var w = VivariumWorld.Create(Content, d); var g = w.Guardian!;
            int target = w.Grid.NearestDomainCell(Vec2.Zero); var center = w.Grid.CellCenter(target);
            var position = new Vec2(center.X - .14, center.Z);
            int local = w.Grid.NearestDomainCell(position); Assert.NotEqual(local, target);
            g.Position = new Vec3(position.X, w.GroundHeight(position), position.Z);
            g.TargetPosition = new Vec3(center.X, w.GroundHeight(center), center.Z);
            g.Heading = 0; g.Activity = GuardianActivity.Feed; g.ActivityDuration = 100;
            w.Litter.FineMass[target] = w.Litter.CoarseMass[target] = 0;
            w.Litter.FruitMass[target] = w.Litter.FruitMass[local] = 1;
            double energy = g.Energy; var before = g.PositionXZ;
            w.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * .1);
            Assert.Equal(before, g.PositionXZ);
            Assert.True(g.Energy > energy, def.Id + " did not consume the nearby selected patch");
            Assert.True(w.Litter.FruitMass[target] < 1); Assert.Equal(1, w.Litter.FruitMass[local]);
        }
    }

    [Fact]
    public void FeedingTurnsTowardFoodBeforeConsumption()
    {
        var d = Content.PresetOrThrow("default").Clone(); d.PilotTreeId = "basinwarden"; d.GuardianId = GuardianCatalog.Woodland;
        var w = VivariumWorld.Create(Content, d); var g = w.Guardian!;
        g.Position = new Vec3(0, w.GroundHeight(Vec2.Zero), 0);
        g.TargetPosition = new Vec3(.1, w.GroundHeight(new Vec2(.1, 0)), 0);
        g.Activity = GuardianActivity.Feed; g.ActivityDuration = 100; g.Heading = Math.PI;
        int target = w.Grid.NearestDomainCell(new Vec2(.1, 0)); w.Litter.FruitMass[target] = 1;
        double energy = g.Energy; var before = g.PositionXZ;
        w.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * .1);
        Assert.Equal(energy, g.Energy); Assert.Equal(before, g.PositionXZ);
        Assert.True(Math.Cos(g.Heading) > -1);
        w.GuardianSystem.StepBehaviour(Vivarium.Sim.Time.SimClock.DefaultSimSecondsPerRealSecond * 2);
        Assert.True(g.Energy > energy); Assert.Equal(before, g.PositionXZ);
    }

    [Fact]
    public void GuardianEatsFruitAndDepositsSeedsIntoSeedBank()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "basinwarden";
        d.GuardianId = GuardianCatalog.Woodland;
        var w = VivariumWorld.Create(Content, d);
        var g = w.Guardian!;

        // Place fruit near guardian
        int cell = w.Grid.NearestDomainCell(g.PositionXZ);
        w.Litter.DepositFruit(g.PositionXZ, 1.0);
        Assert.True(w.Litter.FruitMass[cell] > 0);
        // Seeds now come from an actual fruiting source nearby.
        w.Flora.Add(new Vivarium.Sim.Flora.FloraIndividual { Id = w.Ids.Next(EntityKind.Flora), SpeciesId = "shadebell",
            X = g.PositionXZ.X + 1, Z = g.PositionXZ.Z, Biomass = 1, Health = 1, Fruiting = true });

        double initialEnergy = g.Energy;
        g.Activity = GuardianActivity.Feed;
        g.ActivityDuration = 10;

        // Step behavior to feed
        w.GuardianSystem.StepBehaviour(10.0);
        Assert.True(g.Energy > initialEnergy || g.PendingSeeds.Count > 0);

        // Step metabolism on biological time to digest and excrete
        int initialSeedLots = w.SeedBank.Lots.Count;
        w.GuardianSystem.StepMetabolism(800.0); // > 600s digestion time

        Assert.Empty(g.PendingSeeds);
        Assert.True(w.SeedBank.Lots.Count > initialSeedLots || w.Tally.SeedsDeposited > 0);
    }

    [Fact]
    public void GuardianCanSpawnWithoutPilotTree()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "none";
        d.GuardianId = GuardianCatalog.ArmoredForest;

        var w = VivariumWorld.Create(Content, d);
        Assert.Null(w.PilotTree);
        Assert.NotNull(w.Guardian);
        Assert.Equal(GuardianCatalog.ArmoredForest, w.Guardian.ArchetypeId);
        Assert.NotNull(w.GuardianSystem.Habitat);
        Assert.True(w.Domain.Contains(w.Guardian.PositionXZ));

        // Step behavior to ensure no crashes without pilot tree
        w.GuardianSystem.StepBehaviour(10.0);
        Assert.True(w.Domain.Contains(w.Guardian.PositionXZ));
    }

    [Fact]
    public void PilotTreeCanSpawnWithoutGuardian()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "gloomspire";
        d.GuardianId = "none";

        var w = VivariumWorld.Create(Content, d);
        Assert.NotNull(w.PilotTree);
        Assert.Null(w.Guardian);
        Assert.Equal("gloomspire", w.PilotTree.Def.Id);
    }

    [Fact]
    public void GuardianAndPilotTreeCanBeDifferentArchetypes()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "emberpillar"; // redwood
        d.GuardianId = GuardianCatalog.CrestedCanopy; // hoatzin, usually with cycad

        var w = VivariumWorld.Create(Content, d);
        Assert.NotNull(w.PilotTree);
        Assert.NotNull(w.Guardian);
        Assert.Equal("emberpillar", w.PilotTree.Def.Id);
        Assert.Equal(GuardianCatalog.CrestedCanopy, w.Guardian.ArchetypeId);
        Assert.Equal("emberpillar", w.Guardian.HomeTreeId);
    }

    [Fact]
    public void GuardianCanBeRandomIndependently()
    {
        var d1 = Content.PresetOrThrow("default").Clone();
        d1.Seed = 12345678;
        d1.PilotTreeId = "none";
        d1.GuardianId = "random";

        var w1 = VivariumWorld.Create(Content, d1);
        Assert.Null(w1.PilotTree);
        Assert.NotNull(w1.Guardian);
        Assert.NotNull(GuardianCatalog.Find(w1.Guardian.ArchetypeId));

        // Same seed produces same guardian
        var d2 = Content.PresetOrThrow("default").Clone();
        d2.Seed = 12345678;
        d2.PilotTreeId = "gloomspire";
        d2.GuardianId = "random";

        var w2 = VivariumWorld.Create(Content, d2);
        Assert.NotNull(w2.Guardian);
        Assert.Equal(w1.Guardian.ArchetypeId, w2.Guardian.ArchetypeId);
    }

    [Fact]
    public void LegacyAutoGuardianOptionResolvesToPilotTreeAssociatedGuardian()
    {
        var d1 = Content.PresetOrThrow("default").Clone();
        d1.PilotTreeId = "crowncoil";
        d1.GuardianId = "auto";
        var w1 = VivariumWorld.Create(Content, d1);
        Assert.NotNull(w1.Guardian);
        Assert.Equal(GuardianCatalog.CrestedCanopy, w1.Guardian.ArchetypeId);

        var d2 = Content.PresetOrThrow("default").Clone();
        d2.PilotTreeId = "none";
        d2.GuardianId = "auto";
        var w2 = VivariumWorld.Create(Content, d2);
        Assert.Null(w2.Guardian);
    }
}
