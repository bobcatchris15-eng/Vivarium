using System.Diagnostics;
using Vivarium.Sim.Repr.Water;
using Vivarium.Sim.Water;

if (args.Contains("matrix")) return Matrix.Run(args.Contains("closed"));
if (args.Contains("benchmark")) return Benchmark.Run(args.Contains("serial"));
if (args.Contains("acceptance")) return Acceptance.Run();
var watch = Stopwatch.StartNew();
var fixture = MatureSpringFixture.Create(true);
fixture.SeedPool();
Console.WriteLine($"workers={fixture.Dynamics.WorkerCount}");
var initial = fixture.Mature();
Console.WriteLine($"mature={initial.IsMature} {initial.Report}");
if (!initial.IsMature) return 1;
var replay = MatureSpringFixture.Create(true);
replay.Dynamics.Restore(initial.State);
var stopped = MatureSpringFixture.Create(true);
stopped.Dynamics.Restore(initial.State);
stopped.Dynamics.OnSourcesChanged();
var replayBudget = new WaterBudget();
var stoppedBudget = new WaterBudget();
var noSprings = Array.Empty<Spring>();
for (int seconds = 0; seconds <= 20; seconds += 2)
{
    if (seconds > 0)
    {
        fixture.Dynamics.Advance(2, new[] { fixture.Spring }, fixture.Budget);
        replay.Dynamics.Advance(2, new[] { replay.Spring }, replayBudget);
        stopped.Dynamics.Advance(2, noSprings, stoppedBudget);
    }
    var live = fixture.Dynamics.CaptureRenderState();
    var restored = replay.Dynamics.CaptureRenderState();
    var rest = stopped.Dynamics.CaptureRenderState();
    Console.WriteLine($"offset={seconds}s live={Describe(live)} restored={Describe(restored)} off={Describe(rest)} replayMax={Difference(live, restored):E6} liveOut={fixture.Budget.BoundaryOutflow:E6} offOut={stoppedBudget.BoundaryOutflow:E6}");
}
Console.WriteLine($"wall={watch.Elapsed.TotalSeconds:F3}s");
var closed = MatureSpringFixture.Create(false);
closed.SeedPool();
closed.Dynamics.Advance(12, new[] { closed.Spring }, closed.Budget);
closed.Dynamics.OnSourcesChanged();
var closedBudget = new WaterBudget();
for (int seconds = 0; seconds <= 20; seconds += 4)
{
    if (seconds > 0) closed.Dynamics.Advance(4, noSprings, closedBudget);
    Console.WriteLine($"closed-source-off={seconds}s {Describe(closed.Dynamics.CaptureRenderState())} volume={closed.Dynamics.Volume:E6} out={closedBudget.BoundaryOutflow:E6}");
}
Console.WriteLine($"totalWall={watch.Elapsed.TotalSeconds:F3}s");
return 0;

static string Describe(SurfaceFluidSnapshot snapshot)
{
    var p = snapshot.Cells.Where(c => c.Position.Length < .15 && c.Volume > 0).ToArray();
    if (p.Length == 0) return "empty";
    return $"n:{p.Length},head:{p.Min(c => c.Head):F6}/{p.Average(c => c.Head):F6}/{p.Max(c => c.Head):F6},speed:{p.Average(c => c.Velocity.Length):F6}/{p.Max(c => c.Velocity.Length):F6},est:{snapshot.EstablishedCells}";
}

static double Difference(SurfaceFluidSnapshot a, SurfaceFluidSnapshot b)
{
    if (a.Cells.Length != b.Cells.Length) return double.PositiveInfinity;
    double max = 0;
    for (int i = 0; i < a.Cells.Length; i++)
    {
        if (a.Cells[i].Position != b.Cells[i].Position) return double.PositiveInfinity;
        max = Math.Max(max, Math.Abs(a.Cells[i].Head - b.Cells[i].Head));
        max = Math.Max(max, (a.Cells[i].Velocity - b.Cells[i].Velocity).Length);
    }
    return max;
}
