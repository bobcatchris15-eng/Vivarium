using System;
using System.Linq;
using System.Threading.Tasks;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

public class SuitabilityParallelTests
{
    private static bool Same(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

    [Fact]
    public void ScoringTheSameCellTwiceGivesTheSameAnswer()
    {
        var w = TestUtil.DefaultWorld(20261020);
        var sp = w.Content.Flora.First(f => !f.IsCoverageSpecies);
        var cells = w.Grid.DomainCells.Take(200).ToArray();
        var first = cells.Select(c => w.FloraSystem.Suitability(sp, w.Grid.CellCenter(c)).Score).ToArray();
        var second = cells.Select(c => w.FloraSystem.Suitability(sp, w.Grid.CellCenter(c)).Score).ToArray();
        Assert.Equal(first.Length, second.Length);
        for (int i = 0; i < first.Length; i++) Assert.True(Same(first[i], second[i]), $"cell {i}: {first[i]} vs {second[i]}");
    }

    [Fact]
    public void ScoringAcrossCoresMatchesTheSequentialAnswer()
    {
        var w = TestUtil.DefaultWorld(20261020);
        var sp = w.Content.Flora.First(f => !f.IsCoverageSpecies);
        var cells = w.Grid.DomainCells;
        w.FloraSystem.PrepareCanopyCaches();
        w.RootZone.EnsureCurrent();
        var sequential = new double[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            sequential[i] = w.FloraSystem.Suitability(sp, w.Grid.CellCenter(cells[i])).Score;
        var parallel = new double[cells.Length];
        Parallel.For(0, cells.Length, i =>
            parallel[i] = w.FloraSystem.Suitability(sp, w.Grid.CellCenter(cells[i])).Score);
        int differences = 0;
        for (int i = 0; i < cells.Length; i++) if (!Same(sequential[i], parallel[i])) differences++;
        Assert.Equal(0, differences);
    }
}
