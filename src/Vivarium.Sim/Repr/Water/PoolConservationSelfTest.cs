using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Repr.Water;

public sealed class PoolDonorAndLossTest : IReprSelfTest
{
    public string Name => "water.poolDonorAndLoss";
    public string? Run()
    {
        const double spacing = .0125, head = 1, volume = spacing * spacing * head;
        // A narrow four-cell body has more exposed faces than available independent
        // donor columns. The shared limiter must prevent simultaneous overdraw.
        var cells = Enumerable.Range(0, 4).Select(x => new SurfaceFluidCell(x, 0, volume, 0, 0,
            CachedHead: head, HeadVolume: volume, Established: true, Sleeping: true)).ToArray();
        var state = new SurfaceFluidState(cells, [], 0, 0, spacing, new(), PoolModelVersion: 1,
            Pools: [new(cells.Select(c => new[] { c.X, c.Z }).ToArray(), 4 * volume, head, new Vec2(.01, 0))]);
        var solver = new SurfaceFluidDynamics(new HexDomain(5), _ => 0);
        solver.Restore(state); var budget = new WaterBudget();
        solver.Advance(.25, Array.Empty<Spring>(), budget, evaporation: .001, infiltration: .002);
        double accounted = solver.Volume + budget.Evaporation + budget.Infiltration + budget.BoundaryOutflow + budget.GroundwaterRecharge;
        if (Math.Abs(accounted - 4 * volume) > 1e-12) return $"shared donor/loss budget changed by {accounted - 4 * volume:E6}";
        if (solver.Export().Cells.Any(c => c.Volume < 0 || !double.IsFinite(c.Volume))) return "shared donor produced invalid storage";
        if (budget.Evaporation <= 0 || budget.Infiltration <= 0) return "loss path was not exercised";
        // Changing the table is an actual boundary edit: release the body and
        // account every remaining drop as groundwater recharge on the next solve.
        double priorRecharge = budget.GroundwaterRecharge, remaining = solver.Volume;
        solver.WaterTable = 2;
        solver.Advance(SurfaceFluidDynamics.StepSeconds, Array.Empty<Spring>(), budget);
        if (Math.Abs(solver.Volume + budget.GroundwaterRecharge - priorRecharge - remaining) > 1e-12)
            return "water-table handoff changed storage";
        if (solver.CaptureRenderState().EstablishedCells != 0) return "table change retained a pool";
        return null;
    }
}
