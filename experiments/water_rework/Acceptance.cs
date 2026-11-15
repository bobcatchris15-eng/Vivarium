using Vivarium.Sim.Core;
using Vivarium.Sim.Repr.Water;
using Vivarium.Sim.Water;
using System.Diagnostics;

// Deliberately long-running, opt-in experiment; not a sub-five-second IReprSelfTest.
internal static class Acceptance
{
    public static int Run()
    {
        int failures = 0;
        var clock=Stopwatch.StartNew();
        long allocated=GC.GetTotalAllocatedBytes(true);
        void Check(bool ok, string name, string details)
        { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {details}"); if (!ok) failures++; }
        var f = MatureSpringFixture.Create(true);
        double seeded = f.SeedPool();
        var spring = new[] { f.Spring };
        f.Dynamics.Advance(60, spring, f.Budget);
        double outBefore=f.Budget.BoundaryOutflow;
        var before = f.Dynamics.Export();
        var replay = MatureSpringFixture.Create(true);
        replay.Dynamics.Restore(before);
        var replayBudget = new WaterBudget();
        var tracks = new Dictionary<(int,int), (double Min,double Max,int Count)>();
        double maxSpatial = 0, replayDifference = 0, maxSpeed=0;
        for (int i = 0; i < 20; i++)
        {
            f.Dynamics.Advance(1, spring, f.Budget);
            replay.Dynamics.Advance(1, new[] { replay.Spring }, replayBudget);
            var a = f.Dynamics.CaptureRenderState();
            var b = replay.Dynamics.CaptureRenderState();
            if (a.Cells.Length != b.Cells.Length) replayDifference = double.PositiveInfinity;
            else for (int k = 0; k < a.Cells.Length; k++)
                replayDifference = Math.Max(replayDifference, a.Cells[k].Position != b.Cells[k].Position ? double.PositiveInfinity : Math.Max(Math.Abs(a.Cells[k].Head-b.Cells[k].Head),(a.Cells[k].Velocity-b.Cells[k].Velocity).Length));
            var core = a.Cells.Where(c => c.Position.Length < .15 && c.Depth > .01).ToArray();
            if (core.Length < 100) { failures++; Console.WriteLine($"FAIL wet-core-count n={core.Length}"); break; }
            var heads = core.Select(c => c.Head).Order().ToArray();
            maxSpeed=Math.Max(maxSpeed,core.Max(c=>c.Velocity.Length));
            maxSpatial = Math.Max(maxSpatial, heads[(int)(.95*(heads.Length-1))]-heads[(int)(.05*(heads.Length-1))]);
            foreach (var c in core)
            {
                var key = ((int)Math.Floor(c.Position.X/a.Spacing), (int)Math.Floor(c.Position.Z/a.Spacing));
                if (tracks.TryGetValue(key, out var old)) tracks[key]=(Math.Min(old.Min,c.Head),Math.Max(old.Max,c.Head),old.Count+1);
                else tracks[key]=(c.Head,c.Head,1);
            }
        }
        var excursions=tracks.Values.Where(p=>p.Count==20).Select(p=>p.Max-p.Min).Order().ToArray();
        double p95=excursions.Length==0?double.PositiveInfinity:excursions[(int)(.95*(excursions.Length-1))];
        double budgetError=f.Dynamics.Volume+f.Budget.BoundaryOutflow+f.Budget.GroundwaterRecharge+f.Budget.Evaporation+f.Budget.Infiltration-seeded-f.Budget.SpringInflow;
        Check(Math.Abs(budgetError)<1e-9,"conservation",$"error={budgetError:E6}");
        Check(replayDifference<1e-10,"replay",$"maxHeadOrVelocityDifference={replayDifference:E6}");
        double lastOut=f.Budget.BoundaryOutflow-outBefore;
        Check(lastOut>f.Spring.Discharge*20*.5,"throughflow",$"last20sOut={lastOut:E6} source={f.Spring.Discharge*20:E6}");
        Check(maxSpatial<.008,"wet-core-spatial",$"maxP95P05={maxSpatial:F6}m");
        Check(excursions.Length>=100 && p95<.008,"wet-core-temporal",$"p95CellRange={p95:F6}m persistentTracks={excursions.Length} maxSpeed={maxSpeed:F6}m/s");
        var closed=MatureSpringFixture.Create(false);
        double closedSeed=closed.SeedPool();
        closed.Dynamics.Advance(12,new[]{closed.Spring},closed.Budget);
        closed.Dynamics.OnSourcesChanged();
        var closedBudget=new WaterBudget();
        closed.Dynamics.Advance(20,Array.Empty<Spring>(),closedBudget);
        double closedError=closed.Dynamics.Volume+closedBudget.BoundaryOutflow-closedSeed-closed.Budget.SpringInflow;
        Check(Math.Abs(closedError)<1e-9 && closedBudget.BoundaryOutflow<1e-12,"closed-source-off-budget",$"error={closedError:E6} out={closedBudget.BoundaryOutflow:E6}");
        var closedCore=closed.Dynamics.CaptureRenderState().Cells.Where(c=>c.Position.Length<.15&&c.Depth>.01).ToArray();
        double stillSpread=closedCore.Length>0?closedCore.Max(c=>c.Head)-closedCore.Min(c=>c.Head):double.PositiveInfinity;
        double stillSpeed=closedCore.Length>0?closedCore.Max(c=>c.Velocity.Length):double.PositiveInfinity;
        Check(closedCore.Length>=100 && stillSpread<.008 && stillSpeed<.01,"closed-source-off-rest",$"spread={stillSpread:F6}m speedMax={stillSpeed:F6}m/s");
        double oldVolume=closed.Dynamics.Volume;
        double added=closed.Dynamics.AddWater(new Vec2(0,0),.1,.001);
        Check(added>0 && Math.Abs(oldVolume+added-closed.Dynamics.Volume)<1e-9,"pool-add",$"added={added:E6}");
        oldVolume=closed.Dynamics.Volume;
        double removed=closed.Dynamics.Remove(new Vec2(0,0),.1,.001);
        Check(removed>0 && Math.Abs(oldVolume-removed-closed.Dynamics.Volume)<1e-9,"pool-removal",$"removed={removed:E6}");
        oldVolume=closed.Dynamics.Volume;
        closed.Terrain.BeginEdit();
        int centre=closed.Terrain.VertexIndex(closed.Terrain.Nx/2,closed.Terrain.Nz/2);
        closed.Terrain.H[centre]+=.01;closed.Terrain.Touch();
        closed.Dynamics.OnGeometryChanged(new Vec2(-.1,-.1),new Vec2(.1,.1));
        Check(Math.Abs(oldVolume-closed.Dynamics.Volume)<1e-9,"geometry-invalidation-budget",$"delta={closed.Dynamics.Volume-oldVolume:E6}");
        closed.Dynamics.Advance(2,Array.Empty<Spring>(),closedBudget);
        oldVolume=closed.Dynamics.Volume;
        removed=closed.Dynamics.Remove(new Vec2(0,0),2,oldVolume);
        Check(Math.Abs(oldVolume-removed-closed.Dynamics.Volume)<1e-9 && closed.Dynamics.Volume<1e-9,"flood-drain",$"removed={removed:E6} remaining={closed.Dynamics.Volume:E6}");
        Console.WriteLine($"ACCEPTANCE failures={failures} wall={clock.Elapsed.TotalSeconds:F3}s allocated={GC.GetTotalAllocatedBytes(true)-allocated}");
        return failures==0?0:1;
    }
}
