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
    public long FramesAdvanced { get; private set; }

    public SimHost(VivariumWorld world) { World = world; Tools = new Tools.ToolActions(world); }

    /// <summary>Replace the running world (after a successful staged load / new world).</summary>
    public void Swap(VivariumWorld world) { World = world; Tools = new Tools.ToolActions(world); }

    public int Advance(double realSeconds) { FramesAdvanced++; return World.Scheduler.Advance(realSeconds); }

    public SimClock Clock => World.Clock;
    public void TogglePause() => World.Clock.Paused = !World.Clock.Paused;
}
