using Xunit;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Ecology")]
public class EcologySystemTests
{
    [Fact]
    public void ReturnOrganicMatter_UpdatesDetritusAndNutrientsCorrectly()
    {
        var w = TestUtil.FlatWorld(42);
        var center = new Vec2(0, 0);
        int cell = w.Grid.NearestDomainCell(center);

        double initialDetritusFromFlora = w.Tally.DetritusFromFlora;
        double initialDetritusFromFauna = w.Tally.DetritusFromFauna;
        double initialNutrientsReturn = w.Tally.NutrientsDirectReturn;
        double initialNutrients = w.Fields.Nutrients.Values[cell];

        // 1. From flora, with detritus
        w.Ecology.ReturnOrganicMatter(center, detritus: 1.5, directNutrients: 0.0, fromFlora: true);

        Assert.True(w.Tally.DetritusFromFlora > initialDetritusFromFlora);
        Assert.Equal(initialDetritusFromFauna, w.Tally.DetritusFromFauna);

        // 2. From fauna, with nutrients
        initialDetritusFromFlora = w.Tally.DetritusFromFlora;
        initialDetritusFromFauna = w.Tally.DetritusFromFauna;
        initialNutrientsReturn = w.Tally.NutrientsDirectReturn;
        initialNutrients = w.Fields.Nutrients.Values[cell];

        w.Ecology.ReturnOrganicMatter(center, detritus: 2.5, directNutrients: 3.0, fromFlora: false);

        Assert.True(w.Tally.DetritusFromFauna > initialDetritusFromFauna);
        Assert.Equal(initialDetritusFromFlora, w.Tally.DetritusFromFlora);
        Assert.True(w.Tally.NutrientsDirectReturn > initialNutrientsReturn);
        Assert.True(w.Fields.Nutrients.Values[cell] > initialNutrients);

        // 3. Out of bounds cell (NearestDomainCell will clamp to the edge of the domain)
        // Test that it does update something when near the edge.
        var edgeOfWorld = new Vec2(100000, 100000);
        initialDetritusFromFlora = w.Tally.DetritusFromFlora;
        w.Ecology.ReturnOrganicMatter(edgeOfWorld, detritus: 1.5, directNutrients: 0.0, fromFlora: true);
        Assert.True(w.Tally.DetritusFromFlora > initialDetritusFromFlora);
    }
}
