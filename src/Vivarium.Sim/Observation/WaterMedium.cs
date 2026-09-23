using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Observation;

/// <summary>
/// Authoritative above/under-water state for an observer (camera). Uses hysteresis: the state flips to
/// underwater only when the eye is Hysteresis below the local surface, and back only when it is
/// Hysteresis above it, so jitter at the surface cannot cause repeated transitions.
/// </summary>
public sealed class WaterMediumTracker
{
    public double Hysteresis { get; set; } = 0.015;
    public bool Underwater { get; private set; }
    public int Transitions { get; private set; }
    /// <summary>Local water surface height at the last update (NaN when not above water).</summary>
    public double SurfaceHeight { get; private set; } = double.NaN;
    public event Action<bool>? Changed;

    /// <summary>Surface at an arbitrary eye position; also treats positions outside the island as "air".</summary>
    public static double SurfaceAt(VivariumWorld w, Vec3 eye)
    {
        if (!w.Domain.Contains(eye.XZ)) return double.NaN;
        return w.Water.SurfaceAt(eye.XZ);
    }

    public bool Update(VivariumWorld w, Vec3 eye)
    {
        double s = SurfaceAt(w, eye);
        SurfaceHeight = s;
        bool next = Underwater;
        if (double.IsNaN(s)) next = false;
        else if (!Underwater && eye.Y < s - Hysteresis) next = true;
        else if (Underwater && eye.Y > s + Hysteresis) next = false;
        if (next != Underwater)
        {
            Underwater = next;
            Transitions++;
            Changed?.Invoke(next);
            return true;
        }
        return false;
    }
}
