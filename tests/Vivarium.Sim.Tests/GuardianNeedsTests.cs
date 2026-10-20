using System;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

public class GuardianNeedsTests
{
    private static ContentLibrary Content => ContentLoader.Load(TestUtil.ContentSource);

    private static VivariumWorld CreateWorld(string treeId = "basinwarden", string guardianId = "auto")
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.PilotTreeId = treeId;
        d.GuardianId = guardianId;
        return VivariumWorld.Create(Content, d);
    }

    [Fact]
    public void CatalogContainsAuthenticBehaviorProfilesForAllSixGuardians()
    {
        var hoatzin = GuardianCatalog.Find(GuardianCatalog.CrestedCanopy)!;
        Assert.NotNull(hoatzin.BehaviorProfile);
        Assert.Equal(1.1, hoatzin.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(0.9, hoatzin.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.65, hoatzin.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(0.4, hoatzin.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(0.1, hoatzin.BehaviorProfile.DigAbility, 3);
        Assert.Equal(0.9, hoatzin.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.2, hoatzin.BehaviorProfile.NocturnalBias, 3);
        Assert.False(hoatzin.BehaviorProfile.CanSwim);
        Assert.True(hoatzin.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("tree_perch", hoatzin.BehaviorProfile.PreferredRestType);

        var pangolin = GuardianCatalog.Find(GuardianCatalog.ArmoredForest)!;
        Assert.NotNull(pangolin.BehaviorProfile);
        Assert.Equal(1.0, pangolin.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(0.7, pangolin.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.75, pangolin.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(0.5, pangolin.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(1.8, pangolin.BehaviorProfile.DigAbility, 3);
        Assert.Equal(0.4, pangolin.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.8, pangolin.BehaviorProfile.NocturnalBias, 3);
        Assert.False(pangolin.BehaviorProfile.CanSwim);
        Assert.False(pangolin.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("ball", pangolin.BehaviorProfile.PreferredRestType);

        var serpent = GuardianCatalog.Find(GuardianCatalog.CanopySerpent)!;
        Assert.NotNull(serpent.BehaviorProfile);
        Assert.Equal(0.8, serpent.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(0.6, serpent.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.80, serpent.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(1.8, serpent.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(0.0, serpent.BehaviorProfile.DigAbility, 3);
        Assert.Equal(0.6, serpent.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.3, serpent.BehaviorProfile.NocturnalBias, 3);
        Assert.True(serpent.BehaviorProfile.CanSwim);
        Assert.True(serpent.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("coil", serpent.BehaviorProfile.PreferredRestType);

        var monitor = GuardianCatalog.Find(GuardianCatalog.GreatGround)!;
        Assert.NotNull(monitor.BehaviorProfile);
        Assert.Equal(1.2, monitor.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(1.3, monitor.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.50, monitor.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(1.5, monitor.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(0.6, monitor.BehaviorProfile.DigAbility, 0.6);
        Assert.Equal(0.8, monitor.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.1, monitor.BehaviorProfile.NocturnalBias, 3);
        Assert.True(monitor.BehaviorProfile.CanSwim);
        Assert.False(monitor.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("ground_curl", monitor.BehaviorProfile.PreferredRestType);

        var coati = GuardianCatalog.Find(GuardianCatalog.Woodland)!;
        Assert.NotNull(coati.BehaviorProfile);
        Assert.Equal(1.3, coati.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(1.1, coati.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.45, coati.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(0.3, coati.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(1.4, coati.BehaviorProfile.DigAbility, 3);
        Assert.Equal(1.6, coati.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.3, coati.BehaviorProfile.NocturnalBias, 3);
        Assert.True(coati.BehaviorProfile.CanSwim);
        Assert.True(coati.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("ground_curl", coati.BehaviorProfile.PreferredRestType);

        var porcupine = GuardianCatalog.Find(GuardianCatalog.QuilledArboreal)!;
        Assert.NotNull(porcupine.BehaviorProfile);
        Assert.Equal(0.9, porcupine.BehaviorProfile.ForageWeight, 3);
        Assert.Equal(0.8, porcupine.BehaviorProfile.DrinkFrequency, 3);
        Assert.Equal(0.70, porcupine.BehaviorProfile.PlayThreshold, 3);
        Assert.Equal(0.4, porcupine.BehaviorProfile.BaskPriority, 3);
        Assert.Equal(0.2, porcupine.BehaviorProfile.DigAbility, 3);
        Assert.Equal(0.5, porcupine.BehaviorProfile.CuriosityBias, 3);
        Assert.Equal(0.7, porcupine.BehaviorProfile.NocturnalBias, 3);
        Assert.False(porcupine.BehaviorProfile.CanSwim);
        Assert.True(porcupine.BehaviorProfile.CanClimbTrunk);
        Assert.Equal("fork_wedge", porcupine.BehaviorProfile.PreferredRestType);
    }

    [Fact]
    public void HydrationDecaysAndDrinkingRestoresHydration()
    {
        var world = CreateWorld();
        var g = world.Guardian!;
        var def = world.GuardianSystem.Def!;

        // Advance world clock so BioSeconds > 0
        world.Clock.BioSeconds = 50.0;

        // Initial hydration is 1.0
        g.Hydration = 1.0;
        double bioDt = 168.0 * 20.0; // 20 visible seconds equivalent

        // Step metabolism to verify decay
        GuardianNeeds.StepMetabolism(g, def, bioDt, world);
        double expectedLoss = 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        Assert.Equal(1.0 - expectedLoss, g.Hydration, 5);

        // Lower hydration significantly
        g.Hydration = 0.2;
        g.Activity = GuardianActivity.Drink;
        g.ActivityDuration = 10.0;
        g.TargetPosition = g.Position;

        double beforeDrink = g.Hydration;
        // Step behaviour for 2 visible seconds
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 2.0);

        Assert.True(g.Hydration > beforeDrink, "Drinking did not increase hydration");
        Assert.True(g.LastDrinkBioSeconds > 0, "LastDrinkBioSeconds was not updated");
    }

    [Fact]
    public void SettleSleepStretchCycleTransitionsProperlyAndRecoversFatigue()
    {
        var world = CreateWorld();
        var g = world.Guardian!;

        // Put guardian in high fatigue and trigger Settle
        g.Fatigue = 0.85;
        g.Activity = GuardianActivity.Settle;
        g.ActivityDuration = 0.05; // Expiring immediately

        // 1. Settle expiring -> switches to Sleep
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.Equal(GuardianActivity.Sleep, g.Activity);
        Assert.True(g.ConsecutiveRestCycles >= 1);
        Assert.True(g.ActivityDuration > 20.0);

        // 2. Sleep recovering fatigue faster
        double fatigueBeforeSleep = g.Fatigue;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 2.0);
        Assert.True(g.Fatigue < fatigueBeforeSleep, "Sleep did not recover fatigue");

        // 3. Sleep expiring -> switches to Stretch
        g.ActivityDuration = 0.05;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.Equal(GuardianActivity.Stretch, g.Activity);
        Assert.True(g.ActivityDuration > 1.0);

        // 4. Stretch expiring -> selects next activity
        g.ActivityDuration = 0.05;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.NotEqual(GuardianActivity.Sleep, g.Activity);
        Assert.NotEqual(GuardianActivity.Stretch, g.Activity);
    }

    [Fact]
    public void UtilityScoringSelectsAppropriateActivities()
    {
        var world = CreateWorld();
        var g = world.Guardian!;
        var def = world.GuardianSystem.Def!;

        // 1. High fatigue -> ReturnHome or Settle
        g.Fatigue = 0.90;
        g.Activity = GuardianActivity.Rest;
        g.ActivityDuration = 0.01;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.True(g.Activity is GuardianActivity.ReturnHome or GuardianActivity.Settle,
            $"Expected ReturnHome or Settle on high fatigue, got {g.Activity}");

        // Reset fatigue
        g.Fatigue = 0.1;

        // 2. High thirst -> Drink
        g.Hydration = 0.1;
        g.Activity = GuardianActivity.Rest;
        g.ActivityDuration = 0.01;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        if (world.GuardianSystem.Habitat?.WaterAccessPoints.Count > 0)
        {
            Assert.Equal(GuardianActivity.Drink, g.Activity);
        }

        // Reset hydration
        g.Hydration = 1.0;

        // 3. High hunger -> Forage or Feed
        g.Energy = def.EnergyMax * 0.1;
        g.Activity = GuardianActivity.Rest;
        g.ActivityDuration = 0.01;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.True(g.Activity is GuardianActivity.Forage or GuardianActivity.Feed,
            $"Expected Forage or Feed on hunger, got {g.Activity}");

        // Reset energy
        g.Energy = def.EnergyMax * 0.9;

        // 4. Discomfort -> Bathe or Groom
        g.Comfort = 0.1;
        g.Activity = GuardianActivity.Rest;
        g.ActivityDuration = 0.01;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.True(g.Activity is GuardianActivity.Bathe or GuardianActivity.Groom,
            $"Expected Bathe or Groom on discomfort, got {g.Activity}");

        // Reset comfort
        g.Comfort = 1.0;

        // 5. Play on high arousal
        g.Arousal = 0.95;
        g.Activity = GuardianActivity.Rest;
        g.ActivityDuration = 0.01;
        world.GuardianSystem.StepBehaviour(SimClock.DefaultSimSecondsPerRealSecond * 0.1);
        Assert.Equal(GuardianActivity.Play, g.Activity);
    }

    [Fact]
    public void HabitatQueriesProvideLandmarksAndSubstrateInfo()
    {
        var world = CreateWorld();
        var habitat = world.GuardianSystem.Habitat!;
        Assert.NotNull(habitat);

        // Landmarks
        Assert.NotEmpty(habitat.ScentMarkTargets);
        Assert.Contains(habitat.HomeAnchor, habitat.ScentMarkTargets);

        // Substrate softness
        var center = Vec2.Zero;
        double softness = habitat.SubstrateSoftness(center);
        Assert.InRange(softness, 0.1, 1.0);

        // Sunlit queries
        bool isSunlit = habitat.IsSunlit(center);
        var sunPoint = habitat.NearestSunlitPoint(center);
        Assert.True(world.Domain.Contains(sunPoint));
    }
}
