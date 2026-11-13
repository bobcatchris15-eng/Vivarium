using System;

namespace Vivarium.Sim.Repr.Light;

public sealed class LightCacheSelfTest : IReprSelfTest
{
    public string Name => "light.cache";
    public string? Run()
    {
        var cache = new LightCache(LightTestKit.Box(-2, 0, -2, 2, 2, 2), .2, 2);
        var (p, i) = LightTestKit.Quad(1, -1, 1, -1, 1);
        if (!cache.TrySet(1, p, i, null, 100)) return "first insert rejected";
        if (!cache.TrySet(1, p, i, null, 100)) return "same source must coalesce";
        if (!cache.TryRemove(2)) return "second source rejected";
        if (cache.TryRemove(3)) return "queue exceeded capacity";
        cache.ProcessBatch(2);
        if (cache.PendingCount != 0) return "batch failed to drain";
        if (cache.Sample(new Vivarium.Sim.Core.Vec3(0, .4, 0)).SkyVisibility >= .5) return "blocker did not darken";
        cache.TryRemove(1); cache.ProcessBatch(1);
        if (cache.Sample(new Vivarium.Sim.Core.Vec3(0, .4, 0)).SkyVisibility <= .95) return "removal did not restore sky";
        var fair = new LightCache(LightTestKit.Box(-2, 0, -2, 2, 2, 2), .2, 2);
        fair.TryRemove(10); fair.TrySet(11, p, i, null, 100);
        fair.ProcessBatch(1);
        if (!fair.TryRemove(12)) return "retry after capacity release failed";
        fair.TrySet(11, p, i, null, 100); // replacement keeps its original queue position
        fair.ProcessBatch(1);
        if (fair.Sample(new Vivarium.Sim.Core.Vec3(0, .4, 0)).SkyVisibility >= .5) return "older coalesced source starved by new source";
        // Keep admitting fresh IDs while coalescing an older removal. Each accepted ID
        // must reach the head within the number of IDs already ahead of it.
        fair.TryRemove(11);
        for (ulong id = 13; id < 20; id++)
        {
            if (fair.TryRemove(id)) return "full queue accepted new source";
            fair.ProcessBatch(1);
            if (!fair.TryRemove(id)) return "retry failed after drain";
            fair.TryRemove(11);
            if (id == 14 && fair.Sample(new Vivarium.Sim.Core.Vec3(0, .4, 0)).SkyVisibility <= .95) return "older removal starved under sustained insertion";
        }
        try { fair.TrySet(99, p, new[] { 0, 1, 999 }, null, 1); return "invalid index accepted"; }
        catch (ArgumentException) { }
        return null;
    }
}
