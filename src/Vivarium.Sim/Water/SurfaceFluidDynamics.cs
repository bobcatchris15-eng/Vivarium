using Vivarium.Sim.Core;
using System.Text.Json.Serialization;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Water;

public readonly record struct SurfaceFluidCell(int X, int Z, double Volume, double EastFlux, double NorthFlux, double SlopeX = 0, double SlopeZ = 0, double EastSection = 0, double NorthSection = 0);
public sealed record FluidInputBatch(double Seconds, Spring[] Springs, double Evaporation, double Infiltration, double WaterTable);
public sealed record SurfaceFluidState(SurfaceFluidCell[] Cells, FluidParticle[] Airborne,
    double Time, double Remainder, double Spacing, Dictionary<ulong, double> SourceRemainders, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FluidInputBatch[]? PendingInputs = null);
public readonly record struct SurfaceFluidSample(Vec2 Position, double Head, double Depth, Vec2 Velocity, double Volume, double SlopeX = 0, double SlopeZ = 0);
public sealed record SurfaceFluidSnapshot(long Version, double Time, double Spacing,
    SurfaceFluidSample[] Cells, FluidParticle[] Airborne);

/// <summary>
/// Sparse conservative local-inertial finite volumes on triangulated solid support, with ballistic
/// liquid across unsupported drops. Both representations exchange actual cubic metres in this owner.
/// A cell's free surface is the inverse of its geometric capacity, never an ecological depth field.
/// </summary>
public sealed class SurfaceFluidDynamics
{
    public const double StepSeconds = 1.0 / 60, DefaultSpacing = .025;
    public const double ParcelVolume = .000005;
    // Wet/dry numerical tolerance: unresolved nanolitres stay stored and join later inflow. Never discarded.
    public const double MinimumResolvedVolume = 1e-12;
    public static double Radius(double volume) => .42 * Math.Cbrt(volume);
    private const double Gravity = 9.81, Manning = .028;
    private sealed class Cell
    {
        public double Volume, East, North, EastSection, NorthSection, Head, Minimum, Mean;
        public readonly double[] Ground = new double[9], Support = new double[9];
        public double SupportMinimum, SupportMaximum;
        public double SlopeX, SlopeZ;
    }
    private readonly Dictionary<(int X, int Z), Cell> _cells = new();
    private readonly List<FluidParticle> _air = new();
    private readonly Dictionary<ulong, double> _pending = new();
    private readonly Func<Vec2, double> _ground;
    private readonly HexDomain _domain;
    private readonly List<((int, int) A, (int, int) B, double Volume, bool East, double Speed, bool Falling, double Width)> _transfers = new();
    private readonly Dictionary<(int, int), double> _out = new();
    private readonly Dictionary<(int, int), double> _scale = new();
    private readonly HashSet<(int, int)> _dirty = new();
    private readonly List<(int X, int Z)> _wet = new();
    private readonly List<(double X, double Z)> _slopes = new();
    private readonly HashSet<((int, int), bool)> _faces = new();
    private readonly List<((int, int) Key, bool East)> _orderedFaces = new();
    public double Spacing { get; private set; }
    public Func<Vec2, double>? InfiltrationFactor { get; set; }
    public double WaterTable { get; set; } = double.NegativeInfinity;
    public double Time { get; private set; }
    public double Remainder { get; private set; }
    public long Version { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public long AllocatedBytes { get; private set; }
    public double FractionalVolume => _pending.Values.Sum();
    public int ActiveCells => _cells.Values.Count(c => c.Volume > 0);
    public int CachedCells => _cells.Count;
    public IReadOnlyList<FluidParticle> Airborne => _air;
    public double Volume => _cells.Values.Sum(c => c.Volume) + _air.Sum(p => p.Volume) + _pending.Values.Sum();
    public SurfaceFluidDynamics(HexDomain domain, Func<Vec2, double> ground, double spacing = DefaultSpacing)
    { if (!double.IsFinite(spacing) || spacing <= 0) throw new ArgumentException("Invalid fluid spacing"); _domain = domain; _ground = ground; Spacing = spacing; }
    private (int X, int Z) Key(Vec2 p) => ((int)Math.Floor(p.X / Spacing), (int)Math.Floor(p.Z / Spacing));
    private Vec2 Center((int X, int Z) key) => new((key.X + .5) * Spacing, (key.Z + .5) * Spacing);
    private Cell Get((int X, int Z) key)
    {
        if (_cells.TryGetValue(key, out var c)) return c;
        c = new(); _cells.Add(key, c); ReadGround(key, c); return c;
    }
    private void ReadGround((int X, int Z) key, Cell c)
    {
        c.Minimum = double.PositiveInfinity; c.Mean = 0;
        for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            { double h = _ground(new((key.X + x * .5) * Spacing, (key.Z + z * .5) * Spacing)); if (!double.IsFinite(h)) throw new InvalidOperationException("Non-finite fluid solid boundary"); c.Ground[z * 3 + x] = h; c.Minimum = Math.Min(c.Minimum, h); }
        for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
            { int k = z * 3 + x; c.Mean += (2 * c.Ground[k] + c.Ground[k + 1] + 2 * c.Ground[k + 4] + c.Ground[k + 3]) / 24; }
        ReconstructSupport(c); c.Head = Head(c, c.Volume);
    }
    /// <summary>Invalidate only cached support. Stored volume/momentum remain untouched.</summary>
    public void OnGeometryChanged(Vec2? minimum = null, Vec2? maximum = null)
    {
        foreach (var (key, c) in _cells)
        { var p = Center(key); if (minimum is { } lo && maximum is { } hi && (p.X < lo.X - Spacing || p.X > hi.X + Spacing || p.Z < lo.Z - Spacing || p.Z > hi.Z + Spacing)) continue; ReadGround(key, c); if (c.Volume > 0) _dirty.Add(key); }
        Version++;
    }
    // Exact integral of max(head-ground,0) over a planar triangle, divided by triangle area.
    public static double TriangleDepth(double head, double a, double b, double c)
    {
        if (a > b) (a, b) = (b, a); if (b > c) (b, c) = (c, b); if (a > b) (a, b) = (b, a);
        if (head <= a) return 0; if (head >= c) return head - (a + b + c) / 3;
        if (head <= b) { double d = head - a; return d * d * d / (3 * Math.Max(1e-30, (b - a) * (c - a))); }
        double dry = c - head; return head - (a + b + c) / 3 + dry * dry * dry / (3 * Math.Max(1e-30, (c - a) * (c - b)));
    }
    private void ReconstructSupport(Cell c)
    {
        c.SupportMinimum = double.PositiveInfinity; c.SupportMaximum = double.NegativeInfinity;
        for (int k = 0; k < 9; k++)
        {
            double h = c.Ground[k] - c.SlopeX * ((k % 3) * .5 - .5) * Spacing - c.SlopeZ * ((k / 3) * .5 - .5) * Spacing;
            c.Support[k] = h; c.SupportMinimum = Math.Min(c.SupportMinimum, h); c.SupportMaximum = Math.Max(c.SupportMaximum, h);
        }
    }
    private double Capacity(Cell c, double head)
    {
        double sum = 0;
        for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
            { int k = z * 3 + x; sum += TriangleDepth(head, c.Support[k], c.Support[k + 1], c.Support[k + 4]) + TriangleDepth(head, c.Support[k], c.Support[k + 4], c.Support[k + 3]); }
        return sum * Spacing * Spacing / 8;
    }
    private (double Volume,double Area) CapacityAndArea(Cell cell,double head)
    {
        double volume=0,area=0;
        void Triangle(double a,double b,double c)
        {
            if(a>b)(a,b)=(b,a);if(b>c)(b,c)=(c,b);if(a>b)(a,b)=(b,a);
            if(head<=a)return;
            if(head>=c){volume+=head-(a+b+c)/3;area+=1;return;}
            if(head<=b){double d=head-a,denominator=Math.Max(1e-30,(b-a)*(c-a));volume+=d*d*d/(3*denominator);area+=d*d/denominator;}
            else{double d=c-head,denominator=Math.Max(1e-30,(c-a)*(c-b));volume+=head-(a+b+c)/3+d*d*d/(3*denominator);area+=1-d*d/denominator;}
        }
        for(int z=0;z<2;z++)for(int x=0;x<2;x++)
        {int k=z*3+x;Triangle(cell.Support[k],cell.Support[k+1],cell.Support[k+4]);Triangle(cell.Support[k],cell.Support[k+4],cell.Support[k+3]);}
        double scale=Spacing*Spacing/8;return(volume*scale,area*scale);
    }
    private double Head(Cell c,double volume)
    {
        double lo=c.SupportMinimum,hi=c.SupportMaximum;
        if(volume<=MinimumResolvedVolume)return lo;
        double full=Capacity(c,hi);if(volume>=full)return c.Mean+volume/(Spacing*Spacing);
        // Safeguarded Newton inversion uses the exact wetted area dV/dH.
        // The initial guess depends only on conserved state, so save reconstruction
        // never introduces hidden numerical history.
        double head=lo+(hi-lo)*Math.Cbrt(volume/full);
        for(int i=0;i<16;i++)
        {
            var integral=CapacityAndArea(c,head);double residual=integral.Volume-volume;
            if(Math.Abs(residual)<=Math.Max(1e-18,volume*1e-12))return head;
            if(residual<0)lo=head;else hi=head;
            double next=integral.Area>0?head-residual/integral.Area:double.NaN;
            head=double.IsFinite(next)&&next>lo&&next<hi?next:(lo+hi)*.5;
        }
        return head;
    }
    public void Add(Vec3 p, Vec3 velocity, double volume, ulong source = 0)
    {
        if (!p.IsFinite || !velocity.IsFinite || !double.IsFinite(volume) || volume < 0) throw new ArgumentException("Invalid liquid input");
        if (volume == 0) return;
        // Tools pour onto the actual surface; emission and explicit airborne inputs retain their height.
        if (p.Y > _ground(p.XZ) + .035) _air.Add(new(p, velocity, volume, source));
        else Deposit(p.XZ, volume, velocity.XZ);
        Version++;
    }
    public double AddWater(Vec2 p, double radius, double volume)
    {
        if (!p.IsFinite || !double.IsFinite(radius) || radius < 0 || !double.IsFinite(volume) || volume < 0) throw new ArgumentException("Invalid water tool input");
        if (!_domain.Contains(p) || volume == 0) return 0;
        if (radius < Spacing) { Deposit(p, volume, Vec2.Zero); Version++; return volume; }
        var targets = new List<((int, int) Key, double Weight)>(); double total = 0;
        var lo = Key(p - new Vec2(radius, radius)); var hi = Key(p + new Vec2(radius, radius));
        for (int z = lo.Z; z <= hi.Z; z++) for (int x = lo.X; x <= hi.X; x++)
            { var q = Center((x, z)); double r = (q - p).Length / radius; if (r >= 1 || !_domain.Contains(q)) continue; double weight = 1 - r * r; targets.Add(((x, z), weight)); total += weight; }
        if (total == 0) Deposit(p, volume, Vec2.Zero);
        else foreach (var (key, weight) in targets) { _dirty.Add(key); var c = Get(key); c.Volume += volume * weight / total; c.Head = Head(c, c.Volume); }
        Version++; return volume;
    }
    public void ImportLegacySquare(Vec2 minimum, double size, double volume, Vec2 velocity)
    {
        if (volume <= 0) return; var lo = Key(minimum); var hi = Key(minimum + new Vec2(size, size));
        var targets = new List<((int, int) Key, double Fraction)>(); double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
        for (int z = lo.Z; z <= hi.Z; z++) for (int x = lo.X; x <= hi.X; x++)
            {
                double dx = Math.Max(0, Math.Min((x + 1) * Spacing, minimum.X + size) - Math.Max(x * Spacing, minimum.X));
                double dz = Math.Max(0, Math.Min((z + 1) * Spacing, minimum.Z + size) - Math.Max(z * Spacing, minimum.Z));
                if (dx * dz <= 0) continue; var c = Get((x, z)); targets.Add(((x, z), dx * dz / (Spacing * Spacing))); bottom = Math.Min(bottom, c.SupportMinimum); top = Math.Max(top, c.SupportMaximum);
            }
        top += volume / (size * size); double low = bottom, high = top;
        // Import one legacy control volume as a common hydrostatic surface, rather than inventing
        // a staircase of equal fine-cell depths along the original sloping terrain.
        for (int pass = 0; pass < 24; pass++)
        {
            double middle = (low + high) * .5, capacity = 0; foreach (var target in targets) capacity += Capacity(Get(target.Key), middle) * target.Fraction;
            if (capacity < volume) low = middle; else high = middle;
        }
        double head = (low + high) * .5, total = targets.Sum(t => Capacity(Get(t.Key), head) * t.Fraction);
        foreach (var target in targets)
        {
            double share = total > 0 ? Capacity(Get(target.Key), head) * target.Fraction / total : target.Fraction * Spacing * Spacing / (size * size);
            Deposit(Center(target.Key), volume * share, velocity);
        }
        Version++;
    }
    public void ResolveLegacyFractions(IReadOnlyList<Spring> springs, IReadOnlyList<FluidParticle> historical)
    {
        foreach (var (id, volume) in _pending)
        {
            if (volume == 0) continue; var source = springs.FirstOrDefault(s => s.Id.Value == id);
            var parcel = historical.FirstOrDefault(p => p.Source == id);
            // Older archives did not store positions for removed-source fractions. Keep their volume
            // at the island origin rather than silently dropping the unreconstructible residual.
            Vec2 position = source?.Position ?? (parcel.Volume > 0 ? parcel.Position.XZ : Vec2.Zero);
            Deposit(position, volume, Vec2.Zero);
        }
        _pending.Clear(); Version++;
    }
    private void Deposit(Vec2 p, double volume, Vec2 velocity)
    {
        var key = Key(p); _dirty.Add(key); var c = Get(key); c.Volume += volume; c.Head = Head(c, c.Volume);
        // Impulse changes face momentum; zero-velocity source input must not reset existing current.
        c.East += velocity.X * volume / Spacing; c.North += velocity.Z * volume / Spacing;
    }
    public double Remove(Vec2 p, double radius, double volume)
    {
        if (!p.IsFinite || !double.IsFinite(radius) || radius < 0 || !double.IsFinite(volume) || volume < 0) throw new ArgumentException("Invalid liquid removal");
        double removed = 0;
        foreach (var (key, c) in _cells.OrderBy(k => k.Key.Item2).ThenBy(k => k.Key.Item1))
        { if ((Center(key) - p).Length > radius) continue; double take = Math.Min(c.Volume, volume - removed); c.Volume -= take; removed += take; c.Head = Head(c, c.Volume); if (removed >= volume) break; }
        for (int i = _air.Count - 1; i >= 0 && removed < volume; i--)
        { var f = _air[i]; if ((f.Position.XZ - p).Length > radius) continue; double take = Math.Min(f.Volume, volume - removed); removed += take; if (take >= f.Volume) _air.RemoveAt(i); else _air[i] = f with { Volume = f.Volume - take }; }
        Version++; return removed;
    }
    public void QueueTime(double seconds) { if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentException("Invalid queued fluid time"); Remainder += seconds; }
    public void Advance(double seconds, IReadOnlyList<Spring> springs, WaterBudget budget, double evaporation = 0, double infiltration = 0, int maximumSteps = int.MaxValue)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentException("Invalid fluid time");
        long allocationStart = GC.GetAllocatedBytesForCurrentThread(); Remainder += seconds; int steps = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
        while (Remainder + 1e-12 >= StepSeconds && steps++ < maximumSteps)
        {
            double remaining = StepSeconds;
            while (remaining > 1e-12)
            {
                double maximumDepth = _cells.Count == 0 ? 0 : _cells.Values.Max(c => c.Volume / (Spacing * Spacing));
                double dt = Math.Min(remaining, .45 * Spacing / Math.Sqrt(Gravity * Math.Max(maximumDepth, 1e-8))); Tick(dt, springs, budget, evaporation, infiltration); remaining -= dt;
            }
            Remainder = Math.Max(0, Remainder - StepSeconds); Time += StepSeconds;
        }
        LastStepMilliseconds = timer.Elapsed.TotalMilliseconds / Math.Max(1, steps); AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    }
    private static double SegmentDepth(double head, double a, double b)
    { if (a > b) (a, b) = (b, a); if (head <= a) return 0; if (head >= b) return head - (a + b) * .5; double d = head - a; return d * d / (2 * (b - a)); }
    private double FaceWidth(Cell a, bool east, double head)
    {
        int i = east ? 2 : 6, j = east ? 5 : 7, k = 8;
        static double Wet(double h, double x, double y) { if (x > y) (x, y) = (y, x); return h <= x ? 0 : h >= y ? 1 : (h - x) / (y - x); }
        return Spacing * .5 * (Wet(head, a.Ground[i], a.Ground[j]) + Wet(head, a.Ground[j], a.Ground[k]));
    }
    private double FaceDepth(Cell a, Cell b, bool east, double head)
    {
        // Shared face uses the actual three solid samples, retaining narrow spillways.
        int i = east ? 2 : 6, j = east ? 5 : 7, k = 8;
        return .5 * (SegmentDepth(head, a.Ground[i], a.Ground[j]) + SegmentDepth(head, a.Ground[j], a.Ground[k]));
    }
    private bool IsActive((int X, int Z) key, Cell c)
    {
        if (c.Volume <= MinimumResolvedVolume) return false;
        const double momentum = 1e-12, headTolerance = 1e-6;
        if (Math.Abs(c.East) + Math.Abs(c.North) > momentum) return true;
        for (int direction = 0; direction < 4; direction++)
        {
            int dx = direction == 0 ? 1 : direction == 1 ? -1 : 0, dz = direction == 2 ? 1 : direction == 3 ? -1 : 0;
            bool east = dx != 0; double side = dx + dz;
            int i = east ? (dx > 0 ? 2 : 0) : (dz > 0 ? 6 : 0), j = east ? i + 3 : i + 1, k = east ? i + 6 : i + 2;
            double sill = Math.Min(c.Ground[i], Math.Min(c.Ground[j], c.Ground[k]));
            if (_cells.TryGetValue((key.X + dx, key.Z + dz), out var n) && n.Volume > MinimumResolvedVolume)
            {
                if ((dx < 0 && Math.Abs(n.East) > momentum) || (dz < 0 && Math.Abs(n.North) > momentum)) return true;
                double a = c.Head + (east ? c.SlopeX : c.SlopeZ) * Spacing * .5 * side;
                double b = n.Head - (east ? n.SlopeX : n.SlopeZ) * Spacing * .5 * side;
                if (Math.Max(a, b) > sill + 1e-9 && Math.Abs(c.Head - n.Head) > headTolerance) return true;
            }
            else if (c.Head + (east ? c.SlopeX : c.SlopeZ) * Spacing * .5 * side > sill + 1e-9) return true;
        }
        return false;
    }
    private void Tick(double dt, IReadOnlyList<Spring> springs, WaterBudget budget, double evaporation, double infiltration)
    {
        foreach (var s in springs)
        {
            if (!double.IsFinite(s.Discharge) || s.Discharge < 0) throw new InvalidOperationException("Invalid spring discharge");
            double volume = s.Discharge * dt;
            if (_pending.Remove(s.Id.Value, out double pending)) volume += pending;
            if (volume <= 0) continue; budget.SpringInflow += s.Discharge * dt;
            // A spring is a ground aperture. Its whole fractional input immediately enters conserved storage.
            Deposit(s.Position, volume, Vec2.Zero);
        }
        for (int i = _air.Count - 1; i >= 0; i--)
        {
            var f = _air[i]; var v = f.Velocity + new Vec3(0, -Gravity * dt, 0); var end = f.Position + v * dt;
            // Swept support checks prevent tunneling through raised banks/props on fast falls.
            int samples = Math.Max(1, (int)Math.Ceiling((end.XZ - f.Position.XZ).Length / (Spacing * .5))); bool landed = false;
            for (int s = 1; s <= samples; s++)
            {
                var q = Vec3.Lerp(f.Position, end, (double)s / samples);
                if (!_domain.Contains(q.XZ)) { budget.BoundaryOutflow += f.Volume; landed = true; break; }
                double bed = _ground(q.XZ);
                if (q.Y <= WaterTable && bed < WaterTable) { budget.GroundwaterRecharge += f.Volume; landed = true; break; }
                var surfaceKey = Key(q.XZ); double contact = bed;
                if (_cells.TryGetValue(surfaceKey, out var pool) && pool.Volume > 0)
                { var offset = q.XZ - Center(surfaceKey); contact = Math.Max(contact, pool.Head + pool.SlopeX * offset.X + pool.SlopeZ * offset.Z); }
                if (q.Y <= contact + Radius(f.Volume))
                {
                    double splash = f.Source != ulong.MaxValue && v.Y < -.8 && f.Volume > 2e-8 ? Math.Min(f.Volume * .06, 4e-7) : 0;
                    Deposit(q.XZ, f.Volume - splash, v.XZ);
                    if (splash > 0) for (int drop = 0; drop < 2; drop++)
                        {
                            double sign = drop == 0 ? -1 : 1, volume = splash * .5; var at = q.XZ + new Vec2(0, sign * Spacing * .12);
                            _air.Add(new(Vec3.FromXZ(at, contact + Radius(volume) * 1.1), new(v.X * .3, Math.Min(.65, -v.Y * .22), v.Z * .3 + sign * .12), volume, ulong.MaxValue));
                        }
                    landed = true; break;
                }
            }
            if (landed) _air.RemoveAt(i); else _air[i] = f with { Position = end, Velocity = v };
        }
        // Only wet cells and one halo participate. Stable coordinate order supports reproducible saves/tests.
        var wet = _wet; wet.Clear(); foreach (var pair in _cells) if (IsActive(pair.Key, pair.Value)) wet.Add(pair.Key);
        wet.Sort(static (a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
        // Reconstruct a limited free-surface plane. Integrating a horizontal plate in every sloping
        // cell traps thin films at downhill corners and produces disconnected render patches.
        // At rest neighboring heads agree, yielding exactly zero slope (well balanced pools).
        var slopes = _slopes; slopes.Clear();
        for (int i = 0; i < wet.Count; i++)
        {
            var key = wet[i]; var c = Get(key);
            double Slope((int, int) before, (int, int) after, bool x)
            {
                bool hasA = _cells.TryGetValue(before, out var a) && a.Volume > 1e-14;
                bool hasB = _cells.TryGetValue(after, out var b) && b.Volume > 1e-14;
                double left = hasA ? (c.Head - a!.Head) / Spacing : 0, right = hasB ? (b!.Head - c.Head) / Spacing : 0;
                if (hasA && hasB) return left * right <= 0 ? 0 : Math.Sign(left) * Math.Min(Math.Abs(left), Math.Abs(right));
                if (hasA) return left; if (hasB) return right;
                return x ? (c.Ground[5] - c.Ground[3]) / Spacing : (c.Ground[7] - c.Ground[1]) / Spacing;
            }
            slopes.Add((Slope((key.X - 1, key.Z), (key.X + 1, key.Z), true), Slope((key.X, key.Z - 1), (key.X, key.Z + 1), false)));
        }
        for (int i = 0; i < wet.Count; i++) { var c = Get(wet[i]); c.SlopeX = slopes[i].X; c.SlopeZ = slopes[i].Z; ReconstructSupport(c); c.Head = Head(c, c.Volume); }
        var faces = _faces; faces.Clear();
        foreach (var key in wet) { faces.Add((key, true)); faces.Add(((key.X - 1, key.Z), true)); faces.Add((key, false)); faces.Add(((key.X, key.Z - 1), false)); }
        _transfers.Clear(); _out.Clear(); _scale.Clear();
        _orderedFaces.Clear(); foreach (var face in faces) _orderedFaces.Add(face);
        _orderedFaces.Sort(static (a, b) => { int c = a.Key.Item2.CompareTo(b.Key.Item2); if (c != 0) return c; c = a.Key.Item1.CompareTo(b.Key.Item1); return c != 0 ? c : a.East.CompareTo(b.East); });
        foreach (var (key, east) in _orderedFaces)
        {
            var other = east ? (key.Item1 + 1, key.Item2) : (key.Item1, key.Item2 + 1); var a = Get(key); var b = Get(other); _dirty.Add(key); _dirty.Add(other);
            if (a.Volume <= 0 && b.Volume <= 0) continue;
            double faceA = a.Head + (east ? a.SlopeX : a.SlopeZ) * Spacing * .5, faceB = b.Head - (east ? b.SlopeX : b.SlopeZ) * Spacing * .5;
            double head = Math.Max(faceA, faceB), depth = FaceDepth(a, b, east, head);
            double q = east ? a.East : a.North;
            if (depth <= 1e-9) q = 0;
            else
            {
                double section = Spacing * depth;
                q = (q - Gravity * dt * section * (b.Head - a.Head) / Spacing) /
                    (1 + Gravity * dt * Manning * Manning * Math.Abs(q) / (section * Math.Pow(depth, 4.0 / 3)));
                // CFL-bounded face speed; accumulated time is never skipped.
                double maximum = section * Spacing / (dt * 2); q = Math.Clamp(q, -maximum, maximum);
            }
            // Retain the actual wetted section used by this face solve. After the
            // transfer, a nearly empty donor is not the cross-section of that discharge.
            if (east) { a.East = q; a.EastSection = Spacing * depth; }
            else { a.North = q; a.NorthSection = Spacing * depth; }
            if (q == 0) continue; var from = q > 0 ? key : other; var to = q > 0 ? other : key; var donor = q > 0 ? a : b; var receiver = q > 0 ? b : a;
            if (donor.Volume <= 0) continue;
            double amount = Math.Abs(q) * dt, speed = depth > 0 ? Math.Abs(q) / (Spacing * depth) : 0;
            bool falling = receiver.Ground[4] < donor.Ground[4] - Spacing * 1.2 && receiver.Head < donor.Head - Spacing * 1.2;
            _transfers.Add((from, to, amount, east, q > 0 ? speed : -speed, falling, FaceWidth(a, east, head))); _out[from] = _out.GetValueOrDefault(from) + amount;
        }
        foreach (var (key, outflow) in _out) _scale[key] = Math.Min(1, Get(key).Volume / outflow);
        foreach (var (from, to, requested, east, speed, falling, width) in _transfers)
        {
            var a = Get(from); var b = Get(to); double amount = requested * _scale[from]; a.Volume -= amount;
            if (!_domain.Contains(Center(to))) { budget.BoundaryOutflow += amount; continue; }
            double floor = _ground(Center(to));
            if (!falling && floor < WaterTable) { budget.GroundwaterRecharge += amount; continue; }
            if (falling)
            {
                var at = (Center(from) + Center(to)) * .5; double r = Radius(amount);
                // Downstream face start; horizontal velocity and elevation are physically retained.
                at += east ? new Vec2(Math.Sign(speed) * Spacing * .06, 0) : new Vec2(0, Math.Sign(speed) * Spacing * .06);
                _air.Add(new(Vec3.FromXZ(at, Math.Max(a.Head - r, a.Ground[4] + r)),
                    east ? new Vec3(speed, 0, 0) : new Vec3(0, 0, speed), amount, 0, Math.Max(1e-6, width),
                    east ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0),
                    ((ulong)(from.Item1 + 1048576) << 21) | (uint)(from.Item2 + 1048576) | (east ? 1UL << 63 : 1UL << 62)));
            }
            else b.Volume += amount;
            // Limit the shared momentum alongside the transferred volume.
            var face = east ? Get((Math.Min(from.Item1, to.Item1), from.Item2)) : Get((from.Item1, Math.Min(from.Item2, to.Item2)));
            if (east) face.East *= _scale[from]; else face.North *= _scale[from];
        }
        if (evaporation > 0 || infiltration > 0) foreach (var key in _cells.Keys) _dirty.Add(key);
        foreach (var key in _dirty)
        {
            var c = Get(key);
            if (c.Volume < 0 && c.Volume > -1e-14) c.Volume = 0;
            if (c.Volume < 0 || !double.IsFinite(c.Volume)) throw new InvalidOperationException("Fluid positivity failed");
            if (c.Volume > 0)
            {
                if (!_domain.Contains(Center(key))) { budget.BoundaryOutflow += c.Volume; c.Volume = 0; }
                else if (_ground(Center(key)) < WaterTable) { budget.GroundwaterRecharge += c.Volume; c.Volume = 0; }
                else
                {
                    double ev = Math.Min(c.Volume, Math.Max(0, evaporation) * Spacing * Spacing * dt); c.Volume -= ev; budget.Evaporation += ev;
                    double inf = Math.Min(c.Volume, Math.Max(0, infiltration) * Math.Clamp(InfiltrationFactor?.Invoke(Center(key)) ?? 1, 0, 1) * Spacing * Spacing * dt); c.Volume -= inf; budget.Infiltration += inf;
                }
            }
            if (c.Volume == 0) { c.SlopeX = 0; c.SlopeZ = 0; ReconstructSupport(c); }
            c.Head = Head(c, c.Volume);
        }
        _dirty.Clear();
        // Prune dry cache periodically; no water is discarded to satisfy a work budget.
        if ((long)Math.Round(Time / StepSeconds) % 240 == 0) foreach (var key in _cells.Where(k => k.Value.Volume == 0 && Math.Abs(k.Value.East) + Math.Abs(k.Value.North) < 1e-14).Select(k => k.Key).ToArray()) _cells.Remove(key);
        Version++;
    }
    private Vec2 Current((int X, int Z) key, Cell c)
    {
        _cells.TryGetValue((key.X - 1, key.Z), out var west);
        _cells.TryGetValue((key.X, key.Z - 1), out var south);
        // Area-weighted interpolation from the staggered faces. Using the remaining
        // cell volume here creates arbitrarily large apparent currents at wet/dry fronts.
        double xArea=c.EastSection+(west?.EastSection??0), zArea=c.NorthSection+(south?.NorthSection??0);
        return new(xArea>1e-16?(c.East+(west?.East??0))/xArea:0,
            zArea>1e-16?(c.North+(south?.North??0))/zArea:0);
    }
    public SurfaceFluidSnapshot CaptureRenderState()
    {
        int count=0;foreach(var c in _cells.Values)if(c.Volume>0)count++;
        var cells=new SurfaceFluidSample[count];int index=0;
        foreach(var (key,c) in _cells)if(c.Volume>0)
            cells[index++]=new(Center(key),c.Head,c.Volume/(Spacing*Spacing),Current(key,c),c.Volume,c.SlopeX,c.SlopeZ);
        Array.Sort(cells,static(a,b)=>{int z=a.Position.Z.CompareTo(b.Position.Z);return z!=0?z:a.Position.X.CompareTo(b.Position.X);});
        return new(Version, Time, Spacing, cells, _air.ToArray());
    }
    public SurfaceFluidState Export() => new(_cells.Where(k => k.Value.Volume > 0 || k.Value.East != 0 || k.Value.North != 0 || k.Value.EastSection > 0 || k.Value.NorthSection > 0).OrderBy(k => k.Key.Z).ThenBy(k => k.Key.X)
        .Select(k => new SurfaceFluidCell(k.Key.X, k.Key.Z, k.Value.Volume, k.Value.East, k.Value.North, k.Value.SlopeX, k.Value.SlopeZ, k.Value.EastSection, k.Value.NorthSection)).ToArray(), _air.ToArray(), Time, Remainder, Spacing, new(_pending));
    public void Restore(SurfaceFluidState state)
    {
        if (!double.IsFinite(state.Time) || state.Time < 0 || !double.IsFinite(state.Remainder) || state.Remainder < 0 ||
            !double.IsFinite(state.Spacing) || state.Spacing < .005 || state.Spacing > .25) throw new InvalidDataException("Invalid surface-fluid clock/resolution");
        _cells.Clear(); _air.Clear(); _pending.Clear(); _dirty.Clear(); Spacing = state.Spacing;
        foreach (var saved in state.Cells)
        {
            if (!double.IsFinite(saved.Volume) || saved.Volume < 0 || !double.IsFinite(saved.EastFlux) || !double.IsFinite(saved.NorthFlux) || !double.IsFinite(saved.SlopeX) || !double.IsFinite(saved.SlopeZ) || !double.IsFinite(saved.EastSection) || saved.EastSection < 0 || !double.IsFinite(saved.NorthSection) || saved.NorthSection < 0 ||
                Math.Abs((double)saved.X * Spacing) > _domain.Radius + 1 || Math.Abs((double)saved.Z * Spacing) > _domain.Radius + 1 || _cells.ContainsKey((saved.X, saved.Z))) throw new InvalidDataException("Invalid or duplicate surface-fluid cell");
            var c = Get((saved.X, saved.Z)); c.Volume = saved.Volume; c.East = saved.EastFlux; c.North = saved.NorthFlux; c.SlopeX = saved.SlopeX; c.SlopeZ = saved.SlopeZ; c.EastSection = saved.EastSection; c.NorthSection = saved.NorthSection; ReconstructSupport(c); c.Head = Head(c, c.Volume); _dirty.Add((saved.X, saved.Z)); if (!double.IsFinite(c.Head)) throw new InvalidDataException("Invalid fluid free surface");
        }
        foreach (var p in state.Airborne) { if (!p.Position.IsFinite || !p.Velocity.IsFinite || !double.IsFinite(p.Volume) || p.Volume <= 0 || !double.IsFinite(p.Width) || p.Width < 0 || !p.WidthAxis.IsFinite) throw new InvalidDataException("Invalid airborne liquid"); _air.Add(p); }
        foreach (var (key, v) in state.SourceRemainders) { if (!double.IsFinite(v) || v < 0) throw new InvalidDataException("Invalid source remainder"); _pending.Add(key, v); }
        Time = state.Time; Remainder = state.Remainder; Version++;
    }
    public void RestoreLegacy(FluidState state)
    {
        if (!double.IsFinite(state.Time) || state.Time < 0 || !double.IsFinite(state.Remainder) || state.Remainder < 0) throw new InvalidDataException("Invalid legacy fluid clock");
        foreach (var f in state.Particles) Add(f.Position, f.Velocity, f.Volume, f.Source);
        foreach (var (key, v) in state.SourceRemainders) { if (!double.IsFinite(v) || v < 0) throw new InvalidDataException("Invalid legacy source fraction"); _pending.Add(key, v); }
        Time = state.Time; Remainder = state.Remainder; Version++;
    }
}
