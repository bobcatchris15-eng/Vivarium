using System.Diagnostics;
using Vivarium.Sim.Repr.Water;

internal static class Benchmark
{
    internal static int Run(bool serial)
    {
        var f = MatureSpringFixture.Create(true); double seeded = f.SeedPool();
        if (serial) f.Dynamics.WorkerCount = 1;
        var springs = new[] { f.Spring }; var process = Process.GetCurrentProcess();
        double cpuStart = process.TotalProcessorTime.TotalSeconds;
        long allocationStart = GC.GetTotalAllocatedBytes(true);
        var stages = new Dictionary<string, double>();
        f.Dynamics.StageTimed = (name, ms) => stages[name] = stages.GetValueOrDefault(name) + ms;
        var clock = Stopwatch.StartNew();
        for (int t = 10; t <= 80; t += 10)
        {
            f.Dynamics.Advance(10, springs, f.Budget);
            var s = f.Dynamics.CaptureRenderState();
            var core = s.Cells.Where(c => c.Position.Length < .12 && c.Depth > .01).ToArray();
            Console.WriteLine($"t={t} wall_s={clock.Elapsed.TotalSeconds:F3} cpu_s={process.TotalProcessorTime.TotalSeconds-cpuStart:F3} core_n={core.Length} head_min={core.Min(c=>c.Head):F6} head_max={core.Max(c=>c.Head):F6} speed_mean={core.Average(c=>c.Velocity.Length):F6} established={s.EstablishedCells} out={f.Budget.BoundaryOutflow:E6}");
        }
        double error = f.Dynamics.Volume + f.Budget.BoundaryOutflow + f.Budget.GroundwaterRecharge - seeded - f.Budget.SpringInflow;
        Console.WriteLine($"BENCHMARK assembly={typeof(MatureSpringFixture).Module.ModuleVersionId} workers={f.Dynamics.WorkerCount} wall_s={clock.Elapsed.TotalSeconds:F3} cpu_s={process.TotalProcessorTime.TotalSeconds-cpuStart:F3} allocated={GC.GetTotalAllocatedBytes(true)-allocationStart} mass_error={error:E6}");
        foreach (var stage in stages.OrderByDescending(p => p.Value)) Console.WriteLine($"STAGE {stage.Key} ms={stage.Value:F3}");
        return Math.Abs(error) < 1e-9 ? 0 : 1;
    }
}
