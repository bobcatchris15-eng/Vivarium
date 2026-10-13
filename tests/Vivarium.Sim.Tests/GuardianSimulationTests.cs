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
    public void AllGuardiansSpawnOutsideTrunkAndMoveDuringFirstMinute()
    {
        foreach (var def in GuardianCatalog.All)
        {
            var descriptor = Content.PresetOrThrow("default").Clone();
            descriptor.PilotTreeId = def.AssociatedPilotTreeId;
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
                Assert.False(world.PilotTree.BlocksTrunk(guardian.PositionXZ, 0.35));
                Assert.True(world.Domain.Contains(guardian.PositionXZ));
            }
            Assert.True(distance > 1.0, def.Id + " travelled only " + distance);
        }
    }

    [Fact]
    public void LoadedGuardianInsideTrunkIsMovedToSafeSupport()
    {
        var descriptor = Content.PresetOrThrow("default").Clone();
        descriptor.PilotTreeId = "basinwarden";
        var world = VivariumWorld.Create(Content, descriptor);
        var guardian = world.Guardian!;
        guardian.X = world.PilotTree!.Anchor.X;
        guardian.Z = world.PilotTree.Anchor.Z;
        world.GuardianSystem.StepBehaviour(40);
        Assert.False(world.PilotTree.BlocksTrunk(guardian.PositionXZ, 0.35));
        Assert.Equal(world.GroundHeight(guardian.PositionXZ), guardian.Y, 6);
    }

    [Fact]
    public void CatalogMapsAllSixPilotTreesToOneToOneGuardians()
    {
        Assert.Equal(6, GuardianCatalog.All.Count);
        Assert.Equal(6, PilotTreeCatalog.All.Count);

        foreach (var tree in PilotTreeCatalog.All)
        {
            var guardian = GuardianCatalog.ForPilotTree(tree.Id);
            Assert.NotNull(guardian);
            Assert.Equal(tree.Id, guardian.AssociatedPilotTreeId);
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
    public void GuardianEatsFruitAndDepositsSeedsIntoSeedBank()
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = "basinwarden";
        var w = VivariumWorld.Create(Content, d);
        var g = w.Guardian!;

        // Place fruit near guardian
        int cell = w.Grid.NearestDomainCell(g.PositionXZ);
        w.Litter.DepositFruit(g.PositionXZ, 1.0);
        Assert.True(w.Litter.FruitMass[cell] > 0);

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
}
