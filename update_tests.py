import sys

with open("tests/Vivarium.Sim.Tests/LitterTests.cs", "r") as f:
    content = f.read()

tests = """
    [Fact]
    public void TakeForDecomposer_WithInvalidInput_ReturnsZero()
    {
        var w = TestUtil.FlatWorld();
        int cell = 0;

        Assert.Equal(0, w.Litter.TakeForDecomposer(-1, 1.0, null));
        Assert.Equal(0, w.Litter.TakeForDecomposer(w.Litter.FineMass.Length, 1.0, null));
        Assert.Equal(0, w.Litter.TakeForDecomposer(cell, 0, null));
        Assert.Equal(0, w.Litter.TakeForDecomposer(cell, -1.0, null));
        Assert.Equal(0, w.Litter.TakeForDecomposer(cell, double.NaN, null));
        Assert.Equal(0, w.Litter.TakeForDecomposer(cell, double.PositiveInfinity, null));
    }

    [Fact]
    public void TakeForDecomposer_WithNullProfile_DefaultsWeights()
    {
        var w = TestUtil.FlatWorld();
        int cell = w.Grid.NearestDomainCell(new Vec2(0, 0));
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0;
        w.Litter.Restore(new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length]);

        w.Litter.FineMass[cell] = 0.5;
        w.Litter.FruitMass[cell] = 0.5;
        w.Litter.CoarseMass[cell] = 0.5;

        // With no profile, weights are equal (1), and C# Array.Sort is not strictly stable,
        // but it will process the pools until the amount is satisfied.
        double got = w.Litter.TakeForDecomposer(cell, 1.2, null);

        Assert.Equal(1.2, got, 10);
        Assert.Equal(0.3, w.Litter.FineMass[cell] + w.Litter.FruitMass[cell] + w.Litter.CoarseMass[cell], 10);
    }

    [Fact]
    public void TakeForDecomposer_RespectsProfileMultipliers()
    {
        var w = TestUtil.FlatWorld();
        int cell = w.Grid.NearestDomainCell(new Vec2(0, 0));
        foreach (int c in w.Grid.DomainCells) w.Fields.Detritus[c] = 0;
        w.Litter.Restore(new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length], new double[w.Grid.DomainCells.Length]);

        w.Litter.FineMass[cell] = 0.5;
        w.Litter.FruitMass[cell] = 0.5;
        w.Litter.CoarseMass[cell] = 0.5;

        var profile = new DecomposerProfileDef { FineMultiplier = 0.5, FruitMultiplier = 2.0, CoarseMultiplier = 1.0 };

        // Fruit has highest multiplier (2.0), Coarse is next (1.0), Fine is last (0.5)
        double got = w.Litter.TakeForDecomposer(cell, 0.8, profile);

        Assert.Equal(0.8, got, 10);
        Assert.Equal(0.0, w.Litter.FruitMass[cell], 10); // Fruit taken first (0.5)
        Assert.Equal(0.2, w.Litter.CoarseMass[cell], 10); // Coarse taken next (0.3 taken)
        Assert.Equal(0.5, w.Litter.FineMass[cell], 10); // Fine untouched
    }
}
"""

if "TakeForDecomposer" not in content:
    content = content.rstrip()[:-1] + tests

with open("tests/Vivarium.Sim.Tests/LitterTests.cs", "w") as f:
    f.write(content)
