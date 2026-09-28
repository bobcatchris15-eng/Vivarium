using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only toad locomotion. Slow travel crawls; hops hold the launch point until take-off.</summary>
public sealed class ToadLocomotion
{
    public static readonly FaunaAnimationDef Crawl = new()
    {
        Family = FaunaAnimationFamily.Sprawl, CyclesPerBody = 2.2, DutyFactor = 0.7,
    };

    public Vec3 Position { get; private set; }
    public double Phase { get; private set; }
    public bool IsCrawling { get; private set; } = true;
    public bool Hopping { get; private set; }
    private Vec3 _launch, _landing, _crawlOffset;
    private double _frequency;

    public void Reset(Vec3 position)
    {
        Position = _launch = _landing = position;
        _crawlOffset = Vec3.Zero;
        Hopping = false;
        IsCrawling = true;
    }

    public void Step(Vec3 target, double bodyLength, double seconds, double speed, FaunaAnimationDef hop)
    {
        double dt = Math.Max(0, seconds), length = Math.Max(0.0001, bodyLength);
        if (IsCrawling && speed > 0.9)
        {
            IsCrawling = false;
            Phase = 0;
        }
        else if (!IsCrawling && !Hopping && speed < 0.5)
        {
            IsCrawling = true;
            _crawlOffset = Position - target;
        }
        if (IsCrawling)
        {
            var previous = Position;
            _crawlOffset *= Math.Exp(-dt * 8);
            Position = target + _crawlOffset;
            Phase = (Phase + FaunaGait.Advance(Crawl, (Position - previous).Length, length, dt)) % 1;
            return;
        }
        if (!Hopping)
        {
            if ((target - Position).Length < length * 0.0001) return;
            _launch = Position;
            _landing = target;
            _frequency = Math.Clamp(speed * hop.CyclesPerBody, 1.8, 4.0);
            Phase = 0;
            Hopping = true;
        }
        // Collect the next foothold while crouching. Once airborne, keep the landing fixed.
        if (Phase < hop.DutyFactor) _landing = target;
        Phase = Math.Min(1, Phase + dt * _frequency);
        double progress = MathD.SmoothStep(hop.DutyFactor, 1, Phase);
        Position = Vec3.Lerp(_launch, _landing, progress);
        if (Phase >= 1)
        {
            Position = _landing;
            Phase = 0;
            Hopping = false;
        }
    }
}
