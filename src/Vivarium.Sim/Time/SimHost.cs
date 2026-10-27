using Vivarium.Sim.World;

namespace Vivarium.Sim.Time;

/// <summary>
/// The boundary between the client's frame loop and the authoritative simulation: the client reports wall
/// time; the host converts it into fixed ticks through the scheduler. Nothing else crosses this boundary.
/// </summary>
public sealed class SimHost
{
    public VivariumWorld World { get; private set; }
    public Tools.ToolActions Tools { get; private set; }
    public long FramesAdvanced { get; set; }
    public static System.Action<string, double>? StageTimed { get; set; }

    public SimHost(VivariumWorld world) { World = world; Tools = new Tools.ToolActions(world); }

    /// <summary>Replace the running world (after a successful staged load / new world).</summary>
    public void Swap(VivariumWorld world) { World = world; Tools = new Tools.ToolActions(world); }

    public int Advance(double realSeconds)
    {
        FramesAdvanced++;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int ticks = World.Scheduler.Advance(realSeconds);
        StageTimed?.Invoke("Host.Advance.Scheduler", System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        World.Liquid.Advance(realSeconds);
        StageTimed?.Invoke("Host.Advance.Liquid", System.Diagnostics.Stopwatch.GetElapsedTime(t1).TotalMilliseconds);

        return ticks;
    }

    public SimClock Clock => World.Clock;
    public void TogglePause() => World.Clock.Paused = !World.Clock.Paused;
}
