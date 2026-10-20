using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Diagnostics;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "HabitatSurvey")]
public sealed class HabitatSurveyTests
{
    [Fact]
    public void RootOxygenFallsUnderPersistentSaturation()
    {
        var world = TestUtil.FlatWorld(915);
        var p = new Vec2(0, 0);
        double aerated = world.RootZone.OxygenAt(p, 0.20);
        double saturated = world.RootZone.OxygenAt(p, 0.99);
        Assert.True(aerated > saturated, $"aerated={aerated} saturated={saturated}");
        Assert.InRange(saturated, 0, 1);
        Assert.InRange(world.RootZone.RetainedTopsoilDepth.Sample(p), 0.025, 1.5);
    }

    [Fact]
    public void RootZoneCacheTracksTerrainVersion()
    {
        var w = TestUtil.FlatWorld(916);
        w.RootZone.EnsureCurrent();
        double[] before = (double[])w.RootZone.RetainedTopsoilDepth.Values.Clone();
        int changed = TerrainEditing.Sculpt(w, new Vec2(0, 0), 1.25, 0.5, SculptMode.Raise);
        Assert.True(changed > 0);
        w.RootZone.EnsureCurrent();
        Assert.Contains(w.Grid.DomainCells,
            cell => Math.Abs(before[cell] - w.RootZone.RetainedTopsoilDepth.Values[cell]) > 1e-8);
    }

    [Fact]
    public void DirectionalOverlapIsNotSymmetric()
    {
        double[] a = { 0.9, 0.9, 0.9, 0.9, 0.1 };
        double[] b = { 0.9, 0.1, 0.1, 0.1, 0.1 };
        var d = HabitatDiagnostics.Overlap(a, b, 0.5);
        Assert.Equal(0.25, d.FractionAAlsoB, 10);
        Assert.Equal(1.0, d.FractionBAlsoA, 10);
    }

    [Fact]
    public void SharpMoistureBoundaryAndBroadHabitatPatchesAreVisible()
    {
        var w = TestUtil.FlatWorld(917);
        foreach (int cell in w.Grid.DomainCells)
            w.Fields.Moisture[cell] = w.Grid.CellCenter(cell).X < 0 ? 0.20 : 0.99;
        var species = new[] { TestUtil.Content.FloraOrThrow("blue_sundew"), TestUtil.Content.FloraOrThrow("plumegrass") };
        var survey = HabitatSurvey.Capture(w, species, 0.25);
        var gradient = survey.Fields["moisture"];
        Assert.True(gradient.MaximumNeighbourGradient > 2);
        Assert.True(gradient.SharpEdges > 0);
        Assert.Contains(survey.HabitatCells.Keys, k => k.Contains("dry"));
        Assert.Contains(survey.HabitatCells.Keys, k => k.Contains("saturated"));
        Assert.True(survey.LargestHabitatPatchCells.Values.Max() > 20);
        Assert.Contains("blue_sundew|plumegrass", survey.Overlaps.Keys);
        Assert.Equal(w.Grid.DomainCells.Length, survey.FavorableSpeciesMultiplicity.Values.Sum());
        Assert.Equal(survey.Overlaps["blue_sundew|plumegrass"].Shared,
            survey.FavorableIntersectionCells("blue_sundew", "plumegrass"));
        Assert.Contains("establishment", survey.ToCsv("blue_sundew").Split('\n')[0]);
    }

    [Fact]
    public void PublishedSurfaceLiquidHydratesSoilAndAppearsInCellWetness()
    {
        var w = TestUtil.FlatWorld(918);
        var pos = new Vec2(0.125, 0.125);
        int cell = w.Grid.NearestDomainCell(pos);
        double before = w.Fields.Moisture.Values[cell];
        TestUtil.Flood(w, pos, 0.70, 0.10);
        Assert.True(w.Water.OpenWaterDepth(pos) > 0.001, "fixture must contain published mobile water");
        Assert.True(w.Water.OpenWaterDepth(cell) > 0.001,
            "cell queries must read the mobile-water owner, not the legacy depth array");
        Assert.True(w.Water.IsWet(cell), "wetness must agree across cell and position queries");
        w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800,
            w.Fields.Scratch, w.RootZone);
        Assert.True(w.Fields.Moisture.Values[cell] > before,
            $"surface fluid should wet the root zone ({before} to {w.Fields.Moisture.Values[cell]})");
    }

    [Fact]
    public void MineralRetentionCreatesPhysicallyDifferentNutrientBaselines()
    {
        var shallow = Vivarium.Sim.Fields.EnvironmentFields.MineralReservoirTarget(
            TestUtil.Content.Ecology, 0.06, 0.85);
        var deep = Vivarium.Sim.Fields.EnvironmentFields.MineralReservoirTarget(
            TestUtil.Content.Ecology, 0.75, 0.15);
        Assert.True(deep > shallow, $"deep retained soil {deep} should exceed leached thin soil {shallow}");
    }

    [Fact]
    public void SnapshotComparisonReportsColonizationWithoutEventHistory()
    {
        var w = TestUtil.FlatWorld(919);
        var species = TestUtil.Content.FloraOrThrow("plumegrass");
        var before = HabitatSurvey.Capture(w, new[] { species });
        w.FloraSystem.Establish(species, new Vec2(0, 0), "diagnostic fixture");
        var after = HabitatSurvey.Capture(w, new[] { species });
        var delta = HabitatSurvey.Compare(before, after)[species.Id];
        Assert.Equal(1, delta.ColonizedCells);
        Assert.Equal(0, delta.VacatedCells);
        Assert.Equal(1, delta.NetIndividuals);
        Assert.Equal(species.InitialBiomass, delta.NetBiomass, 10);
        Assert.Equal(0, before.Species[species.Id].TotalBiomass, 10);
    }

    [Fact]
    public void ExistingSpeciesOptIntoDifferentRootAerationPreferences()
    {
        var wet = TestUtil.Content.FloraOrThrow("blue_sundew");
        var dry = TestUtil.Content.FloraOrThrow("plumegrass");
        Assert.NotNull(wet.Oxygen);
        Assert.NotNull(dry.Oxygen);
        Assert.True(wet.Oxygen.Value.Optimum < dry.Oxygen.Value.Optimum);
        Assert.True(wet.Oxygen.Value.Eval(0.12) > dry.Oxygen.Value.Eval(0.12));
        Assert.True(dry.Oxygen.Value.Eval(0.64) > wet.Oxygen.Value.Eval(0.64));
    }
}
