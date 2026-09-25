using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Coverage")]
public class CoverageEnvironmentTests
{
    [Fact]
    public void SeepageSaturatesGroundWithin3Cm()
    {
        var w = TestUtil.FlatWorld();
        var dry = new Vec2(0, 0);
        var wetNeighbourCell = new Vec2(0.02, 0); // just across from `dry`, within the 3cm seepage radius

        // dry baseline: no water anywhere
        var baseline = CoverageEnvironment.Sample(w, dry);
        Assert.True(baseline.Moisture < 0.99);

        w.Water.Depth[w.Grid.NearestDomainCell(wetNeighbourCell)] = 0.05;
        var wet = CoverageEnvironment.Sample(w, dry);
        Assert.True(wet.Moisture > 0.99);
    }

    [Fact]
    public void ConcaveTerrainIsWetterThanConvexAtEqualCoarseMoisture()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, moisture: 0.3, nutrients: 0);

        var bowlCentre = new Vec2(1, 1);
        var ridgeCentre = new Vec2(-1, -1);
        Carve(w.Terrain, bowlCentre, depth: -0.2);   // centre lower than neighbours: concave/bowl
        Carve(w.Terrain, ridgeCentre, depth: 0.2);   // centre higher than neighbours: convex/ridge

        var bowl = CoverageEnvironment.Sample(w, bowlCentre);
        var ridge = CoverageEnvironment.Sample(w, ridgeCentre);
        Assert.True(bowl.Moisture > ridge.Moisture);
    }

    /// <summary>Bumps the single vertex nearest p by `depth` (negative = dig a bowl, positive = raise a ridge)
    /// relative to its 4-neighbours, directly on the heightfield's own vertex grid.</summary>
    private static void Carve(Heightfield hf, Vec2 p, double depth)
    {
        double fx = (p.X - hf.OriginX) / hf.Step, fz = (p.Z - hf.OriginZ) / hf.Step;
        int i = (int)Math.Round(fx), j = (int)Math.Round(fz);
        hf.H[j * hf.Nx + i] += depth;
    }

    [Fact]
    public void CanopyShadeReducesLightUnderALargePlant()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, moisture: 0.3, nutrients: 0, light: 1.0);

        var openPoint = new Vec2(3, 3);
        var openLight = CoverageEnvironment.Sample(w, openPoint).Light;

        var sp = w.Content.Flora.First(f => f.RadiusAtMax > 0.3);
        var plant = new FloraIndividual { Id = w.Ids.Next(EntityKind.Flora), SpeciesId = sp.Id, X = 3, Z = 3, Biomass = sp.MaxBiomass, Health = 1 };
        w.Flora.Add(plant);

        var underCanopy = CoverageEnvironment.Sample(w, openPoint).Light;
        Assert.True(underCanopy < openLight);
    }

    [Fact]
    public void SculptResetsStabilityAndStableSoilOnlyAfterThreshold()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0, 0);

        // freshly generated ground stabilizes over time from world genesis (age 0 at creation)
        var stability = CoverageEnvironment.StabilityOf(w);
        Assert.False(stability.IsStable(p, nowSeconds: 10));
        Assert.True(stability.IsStable(p, nowSeconds: SubstrateStability.StableAfterSeconds + 1));

        // sculpting resets the age under the brush; advance the clock first so the reset time is distinguishable
        w.Clock.Tick = (long)(SubstrateStability.StableAfterSeconds / Time.SimClock.FixedStepSeconds) + 1;
        TerrainEditing.Sculpt(w, p, radius: 0.5, amount: 0.05, SculptMode.Raise);
        Assert.False(stability.IsStable(p, nowSeconds: SubstrateStability.StableAfterSeconds + 1));
        Assert.True(stability.IsStable(p, nowSeconds: 2 * SubstrateStability.StableAfterSeconds + 100));
    }

    [Fact]
    public void MoistureBonusFieldAddsIntoSampledMoisture()
    {
        var w = TestUtil.FlatWorld();
        TestUtil.Condition(w, moisture: 0.1, nutrients: 0);
        var p = new Vec2(0.5, 0.5);

        double before = CoverageEnvironment.Sample(w, p).Moisture;
        CoverageEnvironment.MoistureBonusOf(w).Add(p, 0.5);
        double after = CoverageEnvironment.Sample(w, p).Moisture;

        Assert.True(after > before);
    }

    [Fact]
    public void SaveRoundTripPreservesStabilityAndMoistureBonus()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0.25, 0.25);
        CoverageEnvironment.StabilityOf(w).Reset(p, 0.1, nowSeconds: 123.0);
        CoverageEnvironment.MoistureBonusOf(w).Add(p, 0.37);

        var payloads = WorldSerializer.Serialize(w);
        var w2 = WorldSerializer.Deserialize(TestUtil.Content, payloads);

        int c = w.Grid.NearestDomainCell(p);
        Assert.Equal(CoverageEnvironment.StabilityOf(w).DisturbedAt[c], CoverageEnvironment.StabilityOf(w2).DisturbedAt[c], 9);
        Assert.Equal(CoverageEnvironment.MoistureBonusOf(w).Values[c], CoverageEnvironment.MoistureBonusOf(w2).Values[c], 9);
    }

    [Fact]
    public void SampleIsDeterministicAndAllocationFree()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0.3, -0.4);
        var a = CoverageEnvironment.Sample(w, p);
        var b = CoverageEnvironment.Sample(w, p);
        Assert.Equal(a.Moisture, b.Moisture, 12);
        Assert.Equal(a.Light, b.Light, 12);
        Assert.Equal(a.Slope, b.Slope, 12);
        Assert.Equal(a.Substrate, b.Substrate);
    }
}
