using System;
using System.Threading;

namespace Vivarium.Sim.Repr.Light;

public sealed class LightCacheWorkerSelfTest : IReprSelfTest
{
    public string Name => "light.worker";
    public string? Run()
    {
        using var worker = new LightCacheWorker(LightTestKit.Box(-2, 0, -2, 2, 2, 2), .2, 4);
        if (!SpinWait.SpinUntil(() => worker.Ready || worker.Error != null, 10000)) return "initialization timed out";
        if (worker.Error != null) return "initialization failed: " + worker.Error;
        try { worker.Start(0); return "invalid batch accepted"; } catch (ArgumentOutOfRangeException) { }
        var (p, i) = LightTestKit.Quad(1, -1, 1, -1, 1);
        if (!worker.TrySet(1, p, i, null, 100)) return "source rejected";
        if (!worker.Start(1)) return "worker failed to start";
        if (worker.Start(1)) return "concurrent batch started";
        LightCacheWorker.Snapshot? result = null;
        if (!SpinWait.SpinUntil(() => worker.TryTake(out result) || worker.Error != null, 10000)) return "batch timed out";
        if (worker.Error != null) return "batch failed: " + worker.Error;
        if (result == null || result.Bytes.Length != result.Nx * result.Ny * result.Nz * 4) return "invalid snapshot";
        // An impossible volume fails asynchronously; it must never accept updates, and the
        // documented recovery is to dispose it and replay sources into a fresh worker.
        using (var failed = new LightCacheWorker(LightTestKit.Box(0, 0, 0, 1000, 1000, 1000), .001, 4))
        {
            if (!SpinWait.SpinUntil(() => failed.Error != null || failed.Ready, 10000)) return "fault did not settle";
            if (failed.Error == null || failed.Ready || failed.TryRemove(99)) return "failed worker accepted work";
        }
        // Discarding a worker is the recovery path for any fault or world replacement.
        // Rapid swaps must cancel queued initialization, then allow a fresh worker to run.
        for (int n = 0; n < 4; n++)
        {
            var old = new LightCacheWorker(LightTestKit.Box(-10, 0, -10, 10, 4, 10), .1, 4);
            old.Dispose();
            if (!SpinWait.SpinUntil(() => old.Quiescence.IsCompleted, 10000)) return "disposed initialization did not quiesce";
        }
        using var replacement = new LightCacheWorker(LightTestKit.Box(-2, 0, -2, 2, 2, 2), .2, 4);
        if (!SpinWait.SpinUntil(() => replacement.Ready || replacement.Error != null, 10000) || !replacement.Ready) return "replacement after swaps did not initialize";
        if (!replacement.TrySet(2, p, i, null, 100) || !replacement.Start(1)) return "replacement could not process replay";
        if (!SpinWait.SpinUntil(() => replacement.TryTake(out _) || replacement.Error != null, 10000) || replacement.Error != null) return "replacement replay failed";
        var baking = new LightCacheWorker(LightTestKit.Box(-5, 0, -5, 5, 3, 5), .1, 4);
        if (!SpinWait.SpinUntil(() => baking.Ready || baking.Error != null, 10000) || !baking.Ready) return "bake worker failed to initialize";
        if (!baking.TrySet(3, p, i, null, 100) || !baking.Start(1)) return "bake worker could not start";
        baking.Dispose();
        if (!SpinWait.SpinUntil(() => baking.Quiescence.IsCompleted, 10000)) return "disposed batch did not quiesce";
        if (baking.TryTake(out _)) return "disposed worker published";
        return null;
    }
}
