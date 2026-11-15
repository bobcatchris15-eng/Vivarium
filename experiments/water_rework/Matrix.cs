using System.Diagnostics;
using System.Reflection;
using Vivarium.Sim.Repr.Water;
using Vivarium.Sim.Water;

internal static class Matrix
{
    internal static int Run(bool closedOnly)
    {
        var diagnosticMode = typeof(SurfaceFluidDynamics).GetProperty("DiagnosticEstablishmentMode", BindingFlags.Instance | BindingFlags.NonPublic);
        if (diagnosticMode == null)
        {
            Console.Error.WriteLine("Matrix mode requires the isolated historical diagnostic patch; production solver flags are intentionally absent. Use acceptance or benchmark for the current model.");
            return 2;
        }
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var clock = Stopwatch.StartNew();
            long allocated = GC.GetTotalAllocatedBytes(true);
            var f = MatureSpringFixture.Create(!closedOnly);
            diagnosticMode.SetValue(f.Dynamics, mode);
            double seeded = f.SeedPool();
            Console.WriteLine($"mode={mode} workers={f.Dynamics.WorkerCount} seeded={seeded:E6}");
            if (closedOnly)
            {
                f.Dynamics.Advance(12, new[] { f.Spring }, f.Budget);
                f.Dynamics.OnSourcesChanged();
                var offBudget = new WaterBudget();
                f.Dynamics.Advance(20, Array.Empty<Spring>(), offBudget);
                var s = f.Dynamics.CaptureRenderState();
                var cells = s.Cells.Where(c => c.Position.Length < .15 && c.Volume > 0).ToArray();
                Console.WriteLine($"closed mode={mode} head={cells.Min(c=>c.Head):F6}/{cells.Average(c=>c.Head):F6}/{cells.Max(c=>c.Head):F6} speed={cells.Average(c=>c.Velocity.Length):F6}/{cells.Max(c=>c.Velocity.Length):F6} established={s.EstablishedCells} volume={f.Dynamics.Volume:E6} inflow={f.Budget.SpringInflow:E6} out={offBudget.BoundaryOutflow:E6} wall={clock.Elapsed.TotalSeconds:F3} allocated={GC.GetTotalAllocatedBytes(true)-allocated}");
                continue;
            }
            for (int t = 10; t <= 80; t += 10)
            {
                f.Dynamics.Advance(10, new[] { f.Spring }, f.Budget);
                var snapshot = f.Dynamics.CaptureRenderState();
                var p = snapshot.Cells.Where(c => c.Position.Length < .15 && c.Volume > 0).ToArray();
                var a = p.MinBy(c => c.Head); var b = p.MaxBy(c => c.Head);
                Console.WriteLine($"mode={mode} t={t} poolN={p.Length} headMinMeanMax={a.Head:F6}/{p.Average(c=>c.Head):F6}/{b.Head:F6} extremeDepthSupport={a.Depth:F6}/{a.SupportHeight:F6},{b.Depth:F6}/{b.SupportHeight:F6} speedMeanMax={p.Average(c=>c.Velocity.Length):F6}/{p.Max(c=>c.Velocity.Length):F6} established={snapshot.EstablishedCells} stored={f.Dynamics.Volume:E6} source={f.Budget.SpringInflow:E6} out={f.Budget.BoundaryOutflow:E6} recharge={f.Budget.GroundwaterRecharge:E6} wall={clock.Elapsed.TotalSeconds:F3} allocated={GC.GetTotalAllocatedBytes(true)-allocated}");
            }
            var final = f.Dynamics.CaptureRenderState();
            var state = f.Dynamics.Export();
            var map = state.Cells.ToDictionary(c => (c.X,c.Z));
            foreach (var c in final.Cells.Where(c => c.Position.Length < .15).OrderBy(c => c.Head).Take(3).Concat(final.Cells.Where(c => c.Position.Length < .15).OrderByDescending(c => c.Head).Take(3)))
            {
                var key = ((int)Math.Floor(c.Position.X/state.Spacing),(int)Math.Floor(c.Position.Z/state.Spacing));
                var cell = map[key];
                Console.WriteLine($"extreme mode={mode} key={key} head={c.Head:F6} depth={c.Depth:F6} support={c.SupportHeight:F6} velocity={c.Velocity.Length:F6} est={cell.Established} sleep={cell.Sleeping} cached={cell.CachedHead:F6} headVolume={cell.HeadVolume:E6} volume={cell.Volume:E6}");
            }
        }
        return 0;
    }
}
