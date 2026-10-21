using Xunit;
using Vivarium.Sim.Diagnostics;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;
using Vivarium.Sim.Core;
using System.Linq;

namespace Vivarium.Sim.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void WorkloadBudget_RecordsWarningsAndPrunes()
    {
        var w = TestUtil.FlatWorld();
        var budget = new WorkloadBudget(w) { SystemBudgetMs = -1, MaxWarningsKept = 3 };

        var scheduler = w.Scheduler;

        for (int i = 0; i < 5; i++)
        {
            scheduler.StepTick();
        }

        Assert.Equal(3, budget.Warnings.Count);

        var warn = budget.Warnings[0];
        Assert.True(warn.Tick >= 0);
        Assert.True(warn.Milliseconds >= 0);
        Assert.Equal(-1, warn.BudgetMs);
    }

    [Fact]
    public void WorkloadBudget_DetachStopsRecording()
    {
        var w = TestUtil.FlatWorld();
        var budget = new WorkloadBudget(w) { SystemBudgetMs = -1, MaxWarningsKept = 300 };

        var scheduler = w.Scheduler;

        scheduler.StepTick();
        int warningsBefore = budget.Warnings.Count;
        Assert.True(warningsBefore > 0);

        budget.Detach();

        scheduler.StepTick();
        int warningsAfter = budget.Warnings.Count;
        Assert.Equal(warningsBefore, warningsAfter);
    }

    [Fact]
    public void WorkloadBudget_RateLimitsLogOutput()
    {
        var w = TestUtil.FlatWorld();
        var budget = new WorkloadBudget(w) { SystemBudgetMs = -1, MaxWarningsKept = 300 };

        var sink = new MemoryLogSink();
        Log.AddSink(sink);
        try
        {
            // First tick, should trigger a log
            w.Scheduler.StepTick();
            int logCountAfterFirst = sink.Count(LogLevel.Warning);
            Assert.True(logCountAfterFirst > 0);

            var currentLogCount = logCountAfterFirst;

            // Next ticks shouldn't trigger logs because less than 3600 sim seconds have passed
            w.Scheduler.StepTick();
            Assert.Equal(currentLogCount, sink.Count(LogLevel.Warning));

            // Advance time past 3600 seconds
            // The simulation fixed step is 10s. So 361 ticks.
            for (int i = 0; i <= 360; i++)
            {
                w.Scheduler.StepTick();
            }

            Assert.True(sink.Count(LogLevel.Warning) > currentLogCount);
        }
        finally
        {
            Log.RemoveSink(sink);
        }
    }
}
