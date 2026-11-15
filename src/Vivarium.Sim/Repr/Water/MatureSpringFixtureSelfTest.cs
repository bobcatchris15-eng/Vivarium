using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Water;

public sealed class MatureSpringFixtureBudgetTest : IReprSelfTest
{
    public string Name => "water.matureSpringFixtureBudget";
    public string? Run()
    {
        var fixture = MatureSpringFixture.Create(true);
        if (fixture.Descriptor.Validate().Count != 0) return "fixture descriptor is invalid";
        double seeded = fixture.SeedPool();
        fixture.Dynamics.Advance(.25, new[] { fixture.Spring }, fixture.Budget);
        double accounted = fixture.Dynamics.Volume + fixture.Budget.BoundaryOutflow + fixture.Budget.GroundwaterRecharge;
        double supplied = seeded + fixture.Budget.SpringInflow;
        return Math.Abs(accounted - supplied) > 1e-9 ? $"water budget error {accounted - supplied:E6}" : null;
    }
}

public sealed class MatureSpringFixtureProbe : IReprSelfTest
{
    public string Name => "water.matureSpringFixtureProbe";
    public string? Run()
    {
        foreach (bool open in new[] { false, true })
        {
            var fixture = MatureSpringFixture.Create(open);
            double seeded = fixture.SeedPool();
            var result = fixture.Mature(8, 2);
            Console.WriteLine($"FIXTURE open={open} mature={result.IsMature} {result.Report} cells={result.State.Cells.Length} supplied={seeded + result.Budget.SpringInflow:E6} stored={fixture.Dynamics.Volume:E6} out={result.Budget.BoundaryOutflow:E6}");
            double error = fixture.Dynamics.Volume + result.Budget.BoundaryOutflow + result.Budget.GroundwaterRecharge - seeded - result.Budget.SpringInflow;
            if (Math.Abs(error) > 1e-9) return $"water budget error {error:E6}";
        }
        return null;
    }
}
