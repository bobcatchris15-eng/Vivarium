using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Water;

/// <summary>
/// The visible spring runoff: a real, moving body of water (Intent WATER-1..6).
/// </summary>
/// <remarks>
/// <para><b>Not part of the authoritative simulation.</b> It advances on real time, not on
/// <see cref="Time.SimClock"/>, and its state is deliberately absent from the save digest. A reloaded world starts
/// with no runoff, and re-running a seed for the same number of ticks does not reproduce water. The field
/// (<see cref="Hydrology"/>) remains the only authority on water volume.</para>
///
/// <para><b>Droplets are visual tracers, not a volumetric claim.</b> A real spring here discharges 0.05 m3/hour
/// (13.9 ml/s); carrying that honestly at droplet size is about one droplet per second, which is faithful and
/// invisible. <see cref="DropletsPerSecond"/> and <see cref="RenderRadius"/> are therefore look parameters, not
/// hydrology. Do not read the droplet count or their combined volume back into the water budget.</para>
///
/// <para><b>Why not the SPH solver.</b> <see cref="ParticleFluid"/> was measured at ~79 us per particle per step
/// (it allocates a neighbour iterator per particle per iteration) and behaves incorrectly at a point source: it
/// fires a column of droplets straight up and never runs downhill. This system instead integrates droplets against
/// a cached terrain slope field, which is cheap enough to afford the density a fluid needs. There is no pressure
/// solve, so there is no surface tension or splash.</para>
///
/// <para>Runoff leaves the system where it meets standing water, which the field owns, or where it reaches the
/// island edge. What it leaves behind is <see cref="Wetness"/>, consumed by presentation only -- never by growth or
/// metabolism, which would make the authoritative sim depend on frame timing.</para>
/// </remarks>
public sealed class SpringFluidSystem
{
    public static bool Enabled = true;

    public const double FixedStep = 1.0 / 30.0;
    public const int MaxStepsPerFrame = 3;
    public const double FrameBudgetMs = 2.0;

    /// <summary>Droplets in the system. Sized for DENSITY: a fluid has to be dense to read as one, and this is
    /// affordable only because each droplet costs a cached-array lookup rather than a terrain query.</summary>
    public const int MaxDroplets = 6000;

    /// <summary>Released per second at the reference 50 L/hour while there is room; scaled with discharge. Steady-state population is this times the travel
    /// time to water (measured ~2.3 s over 1.6 m of run), so this is the DENSITY control. A look parameter, not
    /// the spring's discharge.</summary>
    public const double DropletsPerSecond = 700;
    public const double ReferenceDischarge = 0.05 / 3600.0;
    public static double VisualFlowScale(double discharge) => double.IsFinite(discharge)
        ? Math.Clamp(Math.Sqrt(Math.Max(0, discharge) / ReferenceDischarge), 0, 8) : 0;

    /// <summary>Representative volume per droplet. Fixed so the renderer can size a blob without asking the sim.</summary>
    public const double DropletVolume = 0.5e-6;

    /// <summary>Radius the renderer draws. Larger than <see cref="DropletRadius"/> on purpose: adjacent droplets
    /// must overlap for the stream to read as a surface rather than as loose beads.</summary>
    public const double RenderRadius = 0.018;

    public static double DropletRadius => 0.42 * Math.Cbrt(DropletVolume);

    /// <summary>Gravity. Reduced from 9.81 because a droplet is a bead in a shallow film, not a free-falling sphere.</summary>
    public const double Gravity = 4.2;

    public const double MaxSpeed = 2.0;
    public const double GroundFriction = 1.6;

    /// <summary>Cross-flow spread, as a fraction of speed. Turns a single descent line into a rivulet.</summary>
    public const double LateralSpread = 0.55;

    /// <summary>Random walk across the channel, m/s. Makes the stream meander instead of ruling a straight line.</summary>
    public const double LateralWander = 0.010;

    /// <summary>Depth of standing water at which the field takes over and the droplet is retired.</summary>
    public const double StandingWaterDepth = 0.006;

    /// <summary>A droplet resting this long in ground that is already soaked is retired: it has infiltrated.
    /// This is how a hollow drains, and it is why hollows fill first and empty second.</summary>
    public const double SoakAwaySeconds = 14.0;

    /// <summary>Backstop only. A resting droplet that has neither soaked nor overflowed survives this long.</summary>
    public const double MaxAgeSeconds = 300.0;

    /// <summary>Resolution of the cached slope/ground field. 5 cm is fine enough that a stream does not visibly
    /// step, and coarse enough that the whole island is a ~26k-entry table rebuilt in milliseconds.</summary>
    private const double SampleStep = 0.05;

    public const double WetnessHalfLifeSeconds = 40;

    private readonly VivariumWorld _w;
    private readonly List<Droplet> _live = new();
    private readonly List<FluidParticle> _published = new();
    private readonly Dictionary<ulong, double> _emitRemainder = new();

    private double[] _ground = System.Array.Empty<double>();
    private float[] _accX = System.Array.Empty<float>();
    private float[] _accZ = System.Array.Empty<float>();
    private int _sn, _terrainVersion = -1, _propVersion = -1;
    private SpringFluidGroundSnapshot? _renderGround;
    public SpringFluidGroundSnapshot CaptureRenderGround()
    {
        EnsureSlopeField();
        return _renderGround ??= new SpringFluidGroundSnapshot(_ground, _sn,
            -_w.Domain.Apothem - SampleStep, SampleStep);
    }

    private double _accumulator;
    private uint _jitterState = 0x9E3779B9u;

    public SpringFluidSystem(VivariumWorld world)
    {
        _w = world;
        Wetness = new double[world.Grid.Count];
    }

    /// <summary>Per-cell soak left by passing runoff. NON-AUTHORITATIVE: never serialised, never digested, never
    /// read by the simulation. Presentation reads it to darken damp ground.</summary>
    public double[] Wetness { get; }

    /// <summary>Droplets for the renderer. Read-only; the simulation does not read this back.</summary>
    public IReadOnlyList<FluidParticle> Particles => _published;

    public int StepsLastFrame { get; private set; }
    public int Count => _live.Count;
    /// <summary>Droplets handed to standing water on the most recent step.</summary>
    public int RetiredToWater { get; private set; }
    /// <summary>Droplets that infiltrated away from the most recent step.</summary>
    public int RetiredSoaked { get; private set; }
    /// <summary>Droplets that ran off the island edge on the most recent step.</summary>
    public int RetiredAtEdge { get; private set; }
    /// <summary>Droplets that could not be placed because the system was full.</summary>
    public int BlockedByBudget { get; private set; }
    public bool HasRun { get; private set; }

    public void Advance(double realSeconds)
    {
        StepsLastFrame = 0;
        if (!Enabled || !double.IsFinite(realSeconds) || realSeconds <= 0) return;
        if (realSeconds > 0.5) realSeconds = 0.5;
        _accumulator += realSeconds;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (_accumulator >= FixedStep && StepsLastFrame < MaxStepsPerFrame)
        {
            if (clock.Elapsed.TotalMilliseconds > FrameBudgetMs) { _accumulator = 0; break; }
            Step();
            _accumulator -= FixedStep;
            StepsLastFrame++;
            HasRun = true;
        }
        if (_accumulator > FixedStep * MaxStepsPerFrame) _accumulator = 0;
    }

    private void Step()
    {
        RetiredToWater = 0; RetiredSoaked = 0; RetiredAtEdge = 0; BlockedByBudget = 0;
        EnsureSlopeField();
        Emit();
        Integrate();
        SoakAndDry();
        Publish();
    }

    // ------------------------------------------------------------------ cached terrain

    /// <summary>
    /// Bakes ground height and the unit downhill direction into a table. This is the whole reason density is
    /// affordable: <c>GroundHeight</c> walks the pilot tree and every prop, and calling it per droplet per step cost
    /// ~5 us. A lookup here is a fraction of that. Rebuilt only when the terrain or the props change.
    /// </summary>
    private void EnsureSlopeField()
    {
        var terrain = _w.Terrain;
        if (_sn != 0 && _terrainVersion == terrain.Version && _propVersion == _w.Props.Version) return;
        _terrainVersion = terrain.Version; _propVersion = _w.Props.Version;
        _renderGround = null;

        double spanX = _w.Domain.Apothem * 2 + SampleStep;
        double spanZ = _w.Domain.Apothem * 2 + SampleStep;
        _sn = (int)Math.Ceiling(spanX / SampleStep) + 1;
        int snz = (int)Math.Ceiling(spanZ / SampleStep) + 1;
        int n = _sn * snz;
        if (_ground.Length != n) { _ground = new double[n]; _accX = new float[n]; _accZ = new float[n]; }

        double ox = -_w.Domain.Apothem - SampleStep, oz = -_w.Domain.Apothem - SampleStep;
        for (int j = 0; j < snz; j++)
        for (int i = 0; i < _sn; i++)
        {
            var p = new Vec2(ox + i * SampleStep, oz + j * SampleStep);
            bool inside = _w.Domain.Contains(p);
            int k = j * _sn + i;
            _ground[k] = inside ? _w.GroundHeight(p) : 0;
            var nrm = inside ? terrain.Normal(p) : new Vec3(0, 1, 0);
            // Tangential gravity on this slope: a = g - (g.n)n with g = (0,-Gravity,0), whose horizontal part is
            // (Gravity * n.y * n.x, Gravity * n.y * n.z). Flat ground gives zero, which is correct -- a droplet on
            // a plateau has nothing driving it. Storing the acceleration (not a direction) keeps the slope
            // MAGNITUDE, which a normalised downhill vector would throw away.
            _accX[k] = (float)(Gravity * nrm.Y * nrm.X);
            _accZ[k] = (float)(Gravity * nrm.Y * nrm.Z);
        }
    }

    private int SampleIndex(Vec2 xz) =>
        Math.Clamp((int)((xz.Z + _w.Domain.Apothem + SampleStep) / SampleStep), 0, _sn - 1) * _sn
        + Math.Clamp((int)((xz.X + _w.Domain.Apothem + SampleStep) / SampleStep), 0, _sn - 1);

    // ------------------------------------------------------------------ emission

    private void Emit()
    {
        foreach (var s in _w.Water.Springs)
        {
            if (s.Discharge <= 0) continue;
            double flowScale = VisualFlowScale(s.Discharge);
            double spread = Math.Sqrt(flowScale);
            double pending = _emitRemainder.GetValueOrDefault(s.Id.Value)
                             + DropletsPerSecond * flowScale * DropletVolume * FixedStep;
            int k = SampleIndex(s.Position);
            double ground = _ground[k];
            while (pending >= DropletVolume)
            {
                if (_live.Count >= MaxDroplets) { BlockedByBudget++; pending = Math.Min(pending, DropletVolume); break; }
                // Local xorshift, never Rng: touching the simulation's RNG here would make the authoritative world
                // depend on frame timing.
                _jitterState ^= _jitterState << 13; _jitterState ^= _jitterState >> 7; _jitterState ^= _jitterState << 17;
                double jx = (_jitterState & 0xFFFF) / 65535.0 * 0.03 - 0.015;
                double jz = ((_jitterState >> 16) & 0xFFFF) / 65535.0 * 0.03 - 0.015;
                _live.Add(new Droplet(
                    new Vec3(s.Position.X + jx * spread, ground + RenderRadius * 0.6, s.Position.Z + jz * spread),
                    new Vec3(0, 0.04, 0), DropletVolume, s.Id.Value));
                pending -= DropletVolume;
            }
            _emitRemainder[s.Id.Value] = pending;
        }
    }

    // ------------------------------------------------------------------ motion

    private void Integrate()
    {
        double dt = FixedStep;
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var d = _live[i];
            var p = d.Position;

            if (!_w.Domain.Contains(p.XZ)) { _live.RemoveAt(i); RetiredAtEdge++; continue; }
            int cell = _w.Grid.CellAt(p.XZ);
            // The field owns standing water: hand over and stop drawing.
            if (cell >= 0 && _w.Water.OpenWaterDepth(cell) >= StandingWaterDepth)
            { _live.RemoveAt(i); RetiredToWater++; continue; }

            int k = SampleIndex(p.XZ);
            double ax = _accX[k], az = _accZ[k];
            double age = d.Age + dt;

            // Accelerate along the baked slope, then drag. No terrain query and no dependence on an existing
            // velocity: a droplet at rest on a slope starts moving because gravity has a downhill component here.
            var v = new Vec3(d.Velocity.X + ax * dt, 0, d.Velocity.Z + az * dt);
            double along = Math.Sqrt(v.X * v.X + v.Z * v.Z);
            if (along > 1e-9) { along *= Math.Exp(-GroundFriction * dt); v *= along / Math.Sqrt(v.X * v.X + v.Z * v.Z); }
            if (along > MaxSpeed) { v *= MaxSpeed / along; along = MaxSpeed; }

            var p2 = p + v * dt;

            // Lateral spread. Without it every droplet takes the exact same steepest-descent line and the stream
            // renders as a uniform painted stripe; real runoff wanders across a slope and finds its own channel.
            // Scaled by speed so slow water is steady and fast water splays.
            double sp = Math.Sqrt(v.X * v.X + v.Z * v.Z);
            if (sp > 1e-4)
            {
                _jitterState ^= _jitterState << 13; _jitterState ^= _jitterState >> 7; _jitterState ^= _jitterState << 17;
                double n1 = ((_jitterState & 0xFFFF) / 32767.5 - 1.0);
                _jitterState ^= _jitterState << 13; _jitterState ^= _jitterState >> 7; _jitterState ^= _jitterState << 17;
                double n2 = (((_jitterState >> 8) & 0xFFFF) / 32767.5 - 1.0);
                double px = -v.Z / sp, pz = v.X / sp;   // unit vector across the flow
                double lat = n1 * LateralSpread * sp * dt;
                p2 = new Vec3(p2.X + px * lat + n2 * LateralWander * dt,
                              p2.Y,
                              p2.Z + pz * lat + n1 * LateralWander * dt);
            }

            double groundNow = _ground[SampleIndex(p2.XZ)];
            if (p2.Y < groundNow + RenderRadius * 0.6) p2 = new Vec3(p2.X, groundNow + RenderRadius * 0.6, p2.Z);

            // A droplet that has come to rest in ground that is already wet has infiltrated away. This is what
            // empties a hollow, and it only happens once the hollow has actually filled.
            double rest = Math.Abs(along);
            double rested = rest < 0.004 ? d.RestFor + dt : 0;
            if (rested > SoakAwaySeconds) { _live.RemoveAt(i); RetiredSoaked++; continue; }

            if (age > MaxAgeSeconds) { _live.RemoveAt(i); continue; }
            _live[i] = d with { Position = p2, Velocity = v, Age = age, RestFor = rested };
        }
    }

    private void SoakAndDry()
    {
        double dry = Math.Pow(0.5, FixedStep / WetnessHalfLifeSeconds);
        foreach (int c in _w.Grid.DomainCells)
            if (Wetness[c] > 1e-4) Wetness[c] *= dry;
        foreach (var d in _live)
        {
            int cell = _w.Grid.CellAt(d.Position.XZ);
            if (cell < 0 || !_w.Grid.InDomain(cell)) continue;
            if (Wetness[cell] < 1.0) Wetness[cell] = Math.Min(1.0, Wetness[cell] + 0.006);
        }
    }

    private void Publish()
    {
        if (_published.Count == _live.Count)
        {
            for (int i = 0; i < _live.Count; i++)
            {
                var d = _live[i];
                _published[i] = new FluidParticle(d.Position, d.Velocity, d.Volume, d.Source);
            }
            return;
        }
        _published.Clear();
        foreach (var d in _live) _published.Add(new FluidParticle(d.Position, d.Velocity, d.Volume, d.Source));
    }

    /// <summary>Presentation-only query: how damp the ground reads at a world position.</summary>
    public double WetnessAt(Vec2 p)
    {
        int c = _w.Grid.CellAt(p);
        return c >= 0 && _w.Grid.InDomain(c) ? Wetness[c] : 0;
    }

    private readonly record struct Droplet(Vec3 Position, Vec3 Velocity, double Volume, ulong Source)
    {
        public double Age { get; init; }
        public double RestFor { get; init; }
    }
}
