using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

/// <summary>Regression gates for a fauna-light, visually flora-rich habitat.</summary>
[Trait("Suite", "Fauna")]
public sealed class FaunaDensityCalibrationTests
{
    private static readonly JsonDocument Baseline = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(TestUtil.RepoRoot, "scripts", "fauna", "fauna_calibration_baseline.json")));

    [Fact]
    public void AllSpeciesHaveGenuinelySlowerBreedingAndGreaterLongevity()
    {
        var old = Baseline.RootElement.GetProperty("species");
        const double day = 86400;
        Assert.Equal(17, TestUtil.Content.Fauna.Count);
        foreach (var sp in TestUtil.Content.Fauna)
        {
            var before = old.GetProperty(sp.Id);
            var life = before.GetProperty("lifecycle");
            var breed = before.GetProperty("reproduction");
            Assert.Equal(life.GetProperty("lifespanDays").GetDouble() * 2 * day,
                sp.Lifespan, 2);
            Assert.Equal(life.GetProperty("maturityDays").GetDouble() * 1.4 * day,
                sp.MaturityAge, 2);
            Assert.Equal(life.GetProperty("dailyMortality").GetDouble() * .5,
                sp.DailyMortality, 6);
            Assert.Equal(breed.GetProperty("cooldownDays").GetDouble() * 2.5 * day,
                sp.ReproCooldown, 2);
            Assert.True(sp.ClutchMax < breed.GetProperty("clutchMax").GetInt32() ||
                sp.ClutchMax == 1, sp.Id + " still has large clutches");
            Assert.True(sp.PopulationCap <
                breed.GetProperty("populationCap").GetInt32() / 2,
                sp.Id + " retains its old species cap");
            Assert.True(sp.MaturityAge < sp.Lifespan);
        }
    }

    [Fact]
    public void EveryPresetLimitsRealFaunaWhileKeepingStarterSpecies()
    {
        var old = Baseline.RootElement.GetProperty("presets");
        foreach (var preset in TestUtil.Content.Presets.Values)
        {
            var previous = old.GetProperty(preset.PresetId);
            int oldCount = previous.GetProperty("starterFauna").EnumerateArray()
                .Sum(x => x.GetProperty("count").GetInt32());
            int newCount = preset.StarterFauna.Sum(x => x.Count);
            Assert.InRange(preset.FaunaBudget, 210, 440);
            Assert.True(newCount <= oldCount && newCount < preset.FaunaBudget,
                preset.PresetId + " founders exceed budget");
            var oldIds = previous.GetProperty("starterFauna").EnumerateArray()
                .Where(x => x.GetProperty("count").GetInt32() > 0)
                .Select(x => x.GetProperty("species").GetString()).ToHashSet();
            var newIds = preset.StarterFauna.Where(x => x.Count > 0)
                .Select(x => x.Species).ToHashSet();
            Assert.True(oldIds.SetEquals(newIds), preset.PresetId + " loses a founder species");
        }
        var standard = TestUtil.Content.PresetOrThrow("default");
        Assert.Equal(360, standard.FaunaBudget);
        Assert.Equal(77, standard.StarterFauna.Sum(x => x.Count));
    }

    [Fact]
    public void LoadedOverBudgetAnimalsAreNotSilentlyKilledOrErased()
    {
        var w = TestUtil.FlatWorld();
        var sp = TestUtil.Content.FaunaOrThrow("prismhopper");
        var a = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land, ageFraction:.25);
        var b = w.FaunaSystem.CreateFounder(sp, FaunaFixtures.Land + new Vec2(.02,0), ageFraction:.25);
        a.Energy = b.Energy = .95;
        a.ReproCooldownUntil = b.ReproCooldownUntil = 0;
        w.Descriptor.FaunaBudget = 1; // A saved world's new limit can be below its residents.
        Assert.True(w.FaunaSystem.AtFaunaBudget);
        Assert.False(w.FaunaSystem.CanReproduce(a, sp, w.Clock.BioSeconds, out var reason));
        Assert.Equal("world fauna budget", reason);
        w.FaunaSystem.StepLifecycle(120);
        Assert.Equal(2, w.Fauna.Count);
        Assert.NotNull(w.Fauna.Get(a.Id));
        Assert.NotNull(w.Fauna.Get(b.Id));
    }
}
