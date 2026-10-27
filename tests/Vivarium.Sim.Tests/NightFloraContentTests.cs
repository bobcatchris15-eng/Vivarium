using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public sealed class NightFloraContentTests
{
    private static FloraSpeciesDef Sp(string id) => TestUtil.Content.FloraOrThrow(id);

    [Fact]
    public void SevenNightSpeciesGlowAndAllOtherFloraRemainUnlit()
    {
        var glowingIds = new[] { "ghostbonnet", "glowbriar", "lanternsedge", "siltstars", "tidebell", "moonfern", "starmoss" };
        var nonGlowingSampleIds = new[] { "plumegrass", "coinrunner", "coalspike", "embercup", "hollowcrown", "hookthicket", "snaptrap", "trifold", "veilfern" };

        foreach (var id in glowingIds.Concat(nonGlowingSampleIds))
            Assert.Contains(id, TestUtil.Content.Flora.Select(x => x.Id));

        Assert.Equal("pampas", Sp("plumegrass").Shape);
        Assert.Contains("multiple", Sp("plumegrass").Description, StringComparison.OrdinalIgnoreCase);

        Assert.All(glowingIds.Select(Sp), species =>
        {
            Assert.InRange(species.BioluminescenceStrength, double.Epsilon, 8);
            Assert.Equal(3, species.BioluminescenceColor.Length);
            Assert.All(species.BioluminescenceColor, channel => Assert.InRange(channel, 0, 1));
            Assert.Contains("bioluminescent", species.Tags);
        });

        // Non-glowing flora remain visually unlit.
        Assert.All(nonGlowingSampleIds.Select(Sp), species =>
        {
            Assert.Equal(0, species.BioluminescenceStrength);
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, species.BioluminescenceColor);
            Assert.DoesNotContain("bioluminescent", species.Tags);
        });

        // Across all indexed flora, strictly and only the six specified species glow
        var actualGlowing = TestUtil.Content.Flora
            .Where(x => x.BioluminescenceStrength > 0)
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(glowingIds.OrderBy(x => x).ToArray(), actualGlowing);
    }

    [Fact]
    public void BioluminescenceStrengthIsValidatedAtLoad()
    {
        var source = new OverlayContentSource(TestUtil.ContentSource);
        var json = File.ReadAllText(Path.Combine(TestUtil.ContentDir, "flora", "moonfern.json"))
            .Replace("\"bioluminescenceStrength\": 1.3", "\"bioluminescenceStrength\": 8.1", StringComparison.Ordinal);
        source.Set("flora/moonfern.json", json);
        Assert.Throws<ContentValidationException>(() => ContentLoader.Load(source));
    }

    [Fact]
    public void SiltstarsEstablishGrowAndSpreadBelowSurfaceAtUnboundedDepth()
    {
        var world = TestUtil.FlatWorld(seed: 413);
        TestUtil.Condition(world, 0.92, 0.95, 0.55);
        world.Water.Config.WaterTable=world.Terrain.Height(Vec2.Zero)+7.0; // Deep standing pond, not a mutable derived mobile-depth sample.

        var species = Sp("siltstars");
        Assert.True(species.UnlimitedWaterDepth);
        Assert.True(species.MinWaterDepth > 0);
        Assert.Equal(0, species.MaxWaterDepth); // no large sentinel masquerading as unlimited
        var founderPosition = Vec2.Zero;
        var atSevenMetres = world.FloraSystem.Suitability(species, founderPosition);
        Assert.False(atSevenMetres.HardRefused, atSevenMetres.RefusalReason);
        Assert.True(world.FloraSystem.CanEstablish(species, founderPosition, out var why), why);

        var founder = world.FloraSystem.Establish(species, founderPosition, "deep-water-test");
        var biomassBefore = founder.Biomass;
        world.FloraSystem.Step(SimUnits.Day);
        Assert.True(founder.Biomass > biomassBefore, $"biomass {founder.Biomass} did not grow from {biomassBefore}");

        // Force a mature, vigorous founder and allow its configured short propagation interval.
        founder.Age = species.MaturityAge;
        founder.Biomass = species.MaxBiomass;
        founder.LastSpreadAge = 0;
        world.FloraSystem.Step(SimUnits.Day);
        Assert.True(world.Flora.Items.Count(f => f.SpeciesId == "siltstars") > 1,
            "deep-water parent should recruit bottom carpet propagules at the same unbounded depth");
        Assert.All(world.Flora.Items.Where(f => f.SpeciesId == "siltstars"), recruit =>
            Assert.False(world.FloraSystem.Suitability(species, recruit.Position).HardRefused));
    }

    [Fact]
    public void SiltstarsRequiresStandingWaterAndRejectsDryGround()
    {
        var world = TestUtil.FlatWorld(seed: 414);
        TestUtil.Condition(world, 0.92, 0.95, 0.55);
        var species = Sp("siltstars");
        Assert.True(world.FloraSystem.Suitability(species, Vec2.Zero).HardRefused);
        Assert.False(world.FloraSystem.CanEstablish(species, Vec2.Zero, out _));
    }
}
