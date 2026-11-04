using Vivarium.Sim.Core;
using System.Text.Json.Serialization;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Water;

public readonly record struct SurfaceFluidCell(int X, int Z, double Volume, double EastFlux, double NorthFlux, double SlopeX = 0, double SlopeZ = 0, double EastSection = 0, double NorthSection = 0, double QuietSeconds = 0, double? CachedHead = null, double? HeadVolume = null, bool Dirty = false, bool Established = false, bool EstablishPending = false, bool Sleeping = false);
public sealed record FluidInputBatch(double Seconds, Spring[] Springs, double Evaporation, double Infiltration, double WaterTable);
public sealed record SurfaceFluidState(SurfaceFluidCell[] Cells, FluidParticle[] Airborne,
    double Time, double Remainder, double Spacing, Dictionary<ulong, double> SourceRemainders, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FluidInputBatch[]? PendingInputs = null, long WorkStamp = 0, long NextRegionStamp = 0);
public readonly record struct SurfaceFluidSample(Vec2 Position, double Head, double Depth, Vec2 Velocity, double Volume, double SlopeX = 0, double SlopeZ = 0, double? SupportHeight = null);
public sealed record SurfaceFluidSnapshot(long Version, double Time, double Spacing,
    SurfaceFluidSample[] Cells, FluidParticle[] Airborne, int EstablishedCells = 0, int SleepingCells = 0, int Tiles = 0);

/// <summary>
/// Sparse tiled fronts use conservative damped virtual pipes on triangulated support.
/// Established surfaces retain a local current (including still pools); new discharge
/// is conveyed to active edges, while filling and disturbances wake affected cells.
/// Legacy airborne parcels settle into supported storage without emitting new particles.
/// A cell's free surface is the inverse of its geometric capacity, never an ecological depth field.
/// </summary>
public sealed class SurfaceFluidDynamics
{
    public const double StepSeconds = 1.0 / 60, DefaultSpacing = .0125;
    public const double ParcelVolume = .000005;
    // Wet/dry numerical tolerance: unresolved nanolitres stay stored and join later inflow. Never discarded.
    public const double MinimumResolvedVolume = 1e-12;
    public static double Radius(double volume) => .42 * Math.Cbrt(volume);
    private const double Gravity = 9.81, PipeDamping = .96;
    private sealed class Cell
    {
        public double Volume, East, North, EastSection, NorthSection, Head, Minimum, Mean;
        public readonly double[] Ground = new double[9], Support = new double[9];
        public double SupportMinimum, SupportMaximum;
        public double SlopeX, SlopeZ;
        public double InfiltrationFactor;
        public long InfiltrationVersion = -1;
        public double Outgoing, OutflowScale;
        public double QuietSeconds, HeadVolume, FrameVolume;
        public bool Sleeping, SupportValid, Established, WakeRequested, EstablishPending;
        public Region? Region;
        public double SupportSlopeX, SupportSlopeZ;
        public bool FullValid;
        public double FullCapacity;
        public Vec2 Position;
        public bool InDomain;
        public int X, Z;
        public Cell? West, EastCell, South, NorthCell;
        public long DirtyStamp, EastStamp, NorthStamp;
        public double NextSlopeX, NextSlopeZ, Evaporated, Infiltrated, Recharged, Escaped;
        public double Pressure, Diagonal, Rhs, CoefficientEast, CoefficientNorth;
    }
    private sealed class Region
    {
        public readonly List<Cell> Cells = new();
        public readonly PriorityQueue<(Cell Cell,double Sill),double> Outlets = new();
    }
    private readonly Dictionary<(double X,double Z), (Cell Cell,double Weight)[]> _apertures = new();
    private void EmitSpring(Vec2 position,double volume)
    {
        var key=(position.X,position.Z);
        if(!_apertures.TryGetValue(key,out var targets))
        {
            const double radius=.03; // Matches the visible vent's 2.8-3.8 cm inner radius.
            var list=new List<(Cell Cell,double Weight)>();double total=0;
            var lo=Key(position-new Vec2(radius,radius));var hi=Key(position+new Vec2(radius,radius));
            for(int z=lo.Z;z<=hi.Z;z++)for(int x=lo.X;x<=hi.X;x++)
            {var delta=Center((x,z))-position;double r2=delta.Dot(delta)/(radius*radius);if(r2>=1)continue;double weight=1-r2;list.Add((Get((x,z)),weight));total+=weight;}
            if(list.Count==0)list.Add((Get(Key(position)),1));else for(int i=0;i<list.Count;i++)list[i]=(list[i].Cell,list[i].Weight/total);
            targets=list.ToArray();_apertures.Add(key,targets);
        }
        foreach(var (c,weight) in targets)
        {if(c.Established)Wake(c);c.QuietSeconds=0;c.Sleeping=false;c.Volume+=volume*weight;c.Head=Head(c,c.Volume);_dirty.Add((c.X,c.Z));}
    }
    private bool _regionsDirty=true;
    private long _nextRegionStamp;
    private readonly List<Cell> _regionStack = new();
    public int EstablishedCells => _cells.Values.Count(c=>c.Established);
    public void OnSourcesChanged()
    {
        foreach(var c in _cells.Values)
        {c.Established=false;c.EstablishPending=false;c.Sleeping=false;c.QuietSeconds=0;c.Region=null;}
        _regionsDirty=true;_nextRegionStamp=0;Version++;
    }
    private void Wake(Cell c)
    {
        c.EstablishPending=false;c.Established=false;c.Sleeping=false;c.QuietSeconds=0;c.Region=null;_regionsDirty=true;
    }
    private void RebuildRegions(bool restoring=false)
    {
        if(!_regionsDirty || (!restoring && _stamp<_nextRegionStamp))return;
        if(!restoring)
        {
            _nextRegionStamp=_stamp+8;
            foreach(var c in _cells.Values)if(c.EstablishPending)
            {if(c.QuietSeconds>=1&&c.Volume>.00005*Spacing*Spacing){c.Established=true;c.Sleeping=true;}c.EstablishPending=false;}
        }
        // Canonical one-cell halo makes region topology independent of cold cache history.
        var established=_cells.Values.Where(c=>c.Established).ToArray();
        foreach(var c in established){Get((c.X-1,c.Z));Get((c.X+1,c.Z));Get((c.X,c.Z-1));Get((c.X,c.Z+1));}
        foreach(var c in _cells.Values)c.Region=null;
        foreach(var tile in _tileOrder)foreach(var start in tile.Cells)
        {
            if(start==null||!start.Established||start.Region!=null)continue;
            var region=new Region();start.Region=region;_regionStack.Clear();_regionStack.Add(start);
            while(_regionStack.Count>0)
            {
                var c=_regionStack[^1];_regionStack.RemoveAt(_regionStack.Count-1);region.Cells.Add(c);
                void Visit(Cell? n,bool east,bool positive)
                {
                    if(n==null)return;
                    if(n.Established && Math.Floor(n.Head/.005)==Math.Floor(start.Head/.005))
                    {if(n.Region==null){n.Region=region;_regionStack.Add(n);}return;}
                    var owner=positive?c:n;int i=east?2:6,j=east?5:7;
                    double sill=Math.Min(owner.Ground[i],Math.Min(owner.Ground[j],owner.Ground[8]));
                    double level=Math.Max(sill,n.Head);
                    region.Outlets.Enqueue((n,sill),level);
                }
                Visit(c.EastCell,true,true);Visit(c.NorthCell,false,true);Visit(c.West,true,false);Visit(c.South,false,false);
            }
        }
        _regionsDirty=restoring && _cells.Values.Any(c=>c.EstablishPending);
    }
    private void Receive(Cell c,double amount,WaterBudget budget)
    {
        int hops=0;
        while(c.Established && hops++<4096)
        {
            double entryHead=c.Head;
            var region=c.Region;
            Cell? outlet=null;double sill=0;
            if(region!=null)
            {
                while(region.Outlets.TryPeek(out var candidate,out double cached))
                {
                    var target=candidate.Cell;
                    if(target.Established && (target.Region==region || target.Head>=c.Head-1e-9))
                    {region.Outlets.Dequeue();continue;}
                    double level=Math.Max(candidate.Sill,target.Head+(target.Volume-target.HeadVolume)/(Spacing*Spacing));
                    if(Math.Abs(level-cached)>1e-9)
                    {region.Outlets.Dequeue();region.Outlets.Enqueue(candidate,level);continue;}
                    if(level<=entryHead+EstablishedHeadTolerance){outlet=target;sill=candidate.Sill;}
                    break;
                }
            }
            // An established body conveys input to its lowest available active edge;
            // a closed/higher edge wakes the body so the pool fills and rises.
            if(outlet!=null)
            {
                region!.Outlets.Dequeue();
                region.Outlets.Enqueue((outlet,sill),Math.Max(sill,outlet.Head+(outlet.Volume+(outlet.Established?0:amount)-outlet.HeadVolume)/(Spacing*Spacing)));
                c=outlet;
            }
            else Wake(c);
        }
        if(c.Established)Wake(c);
        if(!c.InDomain){budget.BoundaryOutflow+=amount;return;}
        if(c.Ground[4]<WaterTable){budget.GroundwaterRecharge+=amount;return;}
        c.Volume+=amount;Mark(c);
    }
    private sealed class Tile
    {
        public readonly Cell?[] Cells = new Cell?[256];
        public readonly int X, Z;
        public Tile(int x, int z) { X = x; Z = z; }
    }
    private struct FaceWork
    {
        public Cell A, B;
        public bool East;
        public double Amount, Section, Coefficient, Momentum;

    }
    private sealed class CellKeyComparer : IEqualityComparer<(int X, int Z)>
    {
        public static readonly CellKeyComparer Instance = new();
        public bool Equals((int X, int Z) a, (int X, int Z) b) => a == b;
        public int GetHashCode((int X, int Z) p) => unchecked(p.X * 73856093 ^ p.Z * 19349663);
    }
    private readonly Dictionary<(int X, int Z), Cell> _cells = new(CellKeyComparer.Instance);
    private readonly List<FluidParticle> _air = new();
    private readonly Dictionary<ulong, double> _pending = new();
    private readonly Func<Vec2, double> _ground;
    private readonly HexDomain _domain;
    private readonly Dictionary<(int, int), Tile> _tiles = new(CellKeyComparer.Instance);
    private readonly List<Tile> _tileOrder = new();
    private readonly HashSet<(int, int)> _dirty = new(CellKeyComparer.Instance);
    private readonly List<Cell> _wet = new(), _changed = new();
    private FaceWork[] _work = new FaceWork[1024];
    private int _workCount;
    private long _stamp;
    public int WorkerCount { get; set; } = Math.Max(1, Math.Min(6, Environment.ProcessorCount - 2));
    public int AllocatedTiles => _tiles.Count;
    public int SleepingCells => _cells.Values.Count(c=>c.Sleeping||c.Established);
    public const double EstablishedHeadTolerance = .0001;
    private double SurfaceTolerance(Cell c) => Math.Min(EstablishedHeadTolerance, Math.Max(1e-7,c.Volume/(Spacing*Spacing)*.05));
    private void Mark(Cell c)
    { if (c.DirtyStamp != _stamp) { c.DirtyStamp = _stamp; c.FrameVolume = c.Volume; _changed.Add(c); } }
    private void Each(int count, Action<int> body)
    {
        int workers = count < 4096 ? 1 : Math.Clamp(WorkerCount, 1, 16);
        if (workers == 1) { for (int i = 0; i < count; i++) body(i); return; }
        Parallel.For(0, workers, worker =>
        { int lo = (int)((long)count * worker / workers), hi = (int)((long)count * (worker + 1) / workers);
          for (int i = lo; i < hi; i++) body(i); });
    }
    private void AddFace(Cell a, Cell b, bool east)
    {
        if (east ? a.EastStamp == _stamp : a.NorthStamp == _stamp) return;
        if (east) a.EastStamp = _stamp; else a.NorthStamp = _stamp;
        if (_workCount == _work.Length) Array.Resize(ref _work, _work.Length * 2);
        _work[_workCount++] = new() { A = a, B = b, East = east };
        Mark(a); Mark(b);
    }
    public double Spacing { get; private set; }
    private Func<Vec2, double>? _infiltrationFactor;
    private long _infiltrationVersion;
    public Func<Vec2, double>? InfiltrationFactor
    {
        get => _infiltrationFactor;
        set { _infiltrationFactor = value; _infiltrationVersion++; }
    }
    public double WaterTable { get; set; } = double.NegativeInfinity;
    public double Time { get; private set; }
    public double Remainder { get; private set; }
    public long Version { get; private set; }
    public double LastStepMilliseconds { get; private set; }
    public Action<string, double>? StageTimed { get; set; }
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
        c = new() { X = key.X, Z = key.Z, Position = Center(key) }; c.InDomain = _domain.Contains(c.Position);
        _cells.Add(key, c);
        var tileKey = (key.X >> 4, key.Z >> 4);
        if (!_tiles.TryGetValue(tileKey, out var tile))
        { tile = new(tileKey.Item1, tileKey.Item2); _tiles.Add(tileKey, tile); _tileOrder.Add(tile);
          _tileOrder.Sort(static (a,b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X)); }
        tile.Cells[((key.Z & 15) << 4) | (key.X & 15)] = c;
        if (_cells.TryGetValue((key.X - 1, key.Z), out var west)) { c.West = west; west.EastCell = c; }
        if (_cells.TryGetValue((key.X + 1, key.Z), out var east)) { c.EastCell = east; east.West = c; }
        if (_cells.TryGetValue((key.X, key.Z - 1), out var south)) { c.South = south; south.NorthCell = c; }
        if (_cells.TryGetValue((key.X, key.Z + 1), out var north)) { c.NorthCell = north; north.South = c; }
        ReadGround(key, c); return c;
    }
    private void ReadGround((int X, int Z) key, Cell c)
    {
        c.SupportValid = false; c.Sleeping = false;c.EstablishPending=false;c.Established=false;c.Region=null; c.QuietSeconds = 0;
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
        _infiltrationVersion++; _regionsDirty=true;_nextRegionStamp=0;
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
        if (c.SupportValid && c.SupportSlopeX == c.SlopeX && c.SupportSlopeZ == c.SlopeZ) return;
        c.FullValid=false;
        c.SupportValid = true; c.SupportSlopeX = c.SlopeX; c.SupportSlopeZ = c.SlopeZ;
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
        c.HeadVolume = volume;
        double lo=c.SupportMinimum,hi=c.SupportMaximum;
        if(volume<=MinimumResolvedVolume)return lo;
        if(!c.FullValid){c.FullCapacity=Capacity(c,hi);c.FullValid=true;}
        double full=c.FullCapacity;if(volume>=full)return c.Mean+volume/(Spacing*Spacing);
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
        // New inputs enter the terrain-confined solver. Only archived parcels use the
        // compatibility settling path; the creek model does not emit airborne water.
        Deposit(p.XZ, volume, velocity.XZ);
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
        else foreach (var (key, weight) in targets) { _dirty.Add(key); var c = Get(key); if(c.Established)Wake(c);c.QuietSeconds=0; c.Sleeping=false; c.Volume += volume * weight / total; c.Head = Head(c, c.Volume); }
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
        var key = Key(p); _dirty.Add(key); var c = Get(key); if(c.Established)Wake(c);c.QuietSeconds = 0; c.Sleeping = false; c.Volume += volume; c.Head = Head(c, c.Volume);
        // Impulse changes face momentum; zero-velocity source input must not reset existing current.
        c.East += velocity.X * volume / Spacing; c.North += velocity.Z * volume / Spacing;
    }
    public double Remove(Vec2 p, double radius, double volume)
    {
        if (!p.IsFinite || !double.IsFinite(radius) || radius < 0 || !double.IsFinite(volume) || volume < 0) throw new ArgumentException("Invalid liquid removal");
        double removed = 0;
        foreach (var (key, c) in _cells.OrderBy(k => k.Key.Item2).ThenBy(k => k.Key.Item1))
        { if ((Center(key) - p).Length > radius) continue; double take = Math.Min(c.Volume, volume - removed); if(take>0){if(c.Established)Wake(c);c.QuietSeconds=0;c.Sleeping=false;} c.Volume -= take; removed += take; c.Head = Head(c, c.Volume); if (removed >= volume) break; }
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
            // This is a transport/filling model, not a gravity-wave simulation.
            // Every face is capped to half a cell per step and all donor outflows
            // share a positivity limiter. Deep established pools must not force
            // global gravity-wave substeps; their small surface waves are optical.
            Tick(StepSeconds,springs,budget,evaporation,infiltration);
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
        // Sub-visible films retain every drop but do not expand an invisible front.
        // Incoming water reactivates them as soon as they reach the visible wet depth.
        if (c.Established || c.Volume <= Math.Max(MinimumResolvedVolume, .00005 * Spacing * Spacing)) return false;
        const double momentum = 1e-12, headTolerance = 1e-6;
        if (Math.Abs(c.East) + Math.Abs(c.North) > momentum) return true;
        for (int direction = 0; direction < 4; direction++)
        {
            int dx = direction == 0 ? 1 : direction == 1 ? -1 : 0, dz = direction == 2 ? 1 : direction == 3 ? -1 : 0;
            bool east = dx != 0; double side = dx + dz;
            int i = east ? (dx > 0 ? 2 : 0) : (dz > 0 ? 6 : 0), j = east ? i + 3 : i + 1, k = east ? i + 6 : i + 2;
            double sill = Math.Min(c.Ground[i], Math.Min(c.Ground[j], c.Ground[k]));
            var n = dx > 0 ? c.EastCell : dx < 0 ? c.West : dz > 0 ? c.NorthCell : c.South;
            if (n != null && n.Volume > MinimumResolvedVolume)
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
        long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
        void Stage(string name) { if (StageTimed == null) return; long now = System.Diagnostics.Stopwatch.GetTimestamp(); StageTimed(name, System.Diagnostics.Stopwatch.GetElapsedTime(stamp, now).TotalMilliseconds); stamp = now; }
        foreach (var s in springs)
        {
            if (!double.IsFinite(s.Discharge) || s.Discharge < 0) throw new InvalidOperationException("Invalid spring discharge");
            double volume = s.Discharge * dt;
            if (_pending.Remove(s.Id.Value, out double pending)) volume += pending;
            if (volume <= 0) continue; budget.SpringInflow += s.Discharge * dt;
            // A spring is a ground aperture. Its whole fractional input immediately enters conserved storage.
            EmitSpring(s.Position, volume);
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
                    Deposit(q.XZ, f.Volume, v.XZ);
                    landed = true; break;
                }
            }
            if (landed) _air.RemoveAt(i); else _air[i] = f with { Position = end, Velocity = v };
        }
        // Sparse 16x16 tiles own stable slots; direct neighbours and reused face buffers
        // keep hash tables and sorting out of the physical substep.
        _stamp++; _wet.Clear(); _changed.Clear(); _workCount = 0;RebuildRegions();
        foreach (var key in _dirty) if (_cells.TryGetValue(key, out var edited)) Mark(edited);
        _dirty.Clear();
        foreach (var tile in _tileOrder) foreach (var c in tile.Cells)
            if (c != null && IsActive((c.X, c.Z), c)) _wet.Add(c);
        Stage("active");
        double Slope(Cell c, Cell? a, Cell? b, bool x)
        {
            bool hasA = a != null && a.Volume > 1e-14, hasB = b != null && b.Volume > 1e-14;
            double left = hasA ? (c.Head - a!.Head) / Spacing : 0, right = hasB ? (b!.Head - c.Head) / Spacing : 0;
            if (hasA && hasB) return left * right <= 0 ? 0 : Math.Sign(left) * Math.Min(Math.Abs(left), Math.Abs(right));
            if (hasA) return left; if (hasB) return right;
            return x ? (c.Ground[5] - c.Ground[3]) / Spacing : (c.Ground[7] - c.Ground[1]) / Spacing;
        }
        Each(_wet.Count, i =>
        { var c = _wet[i]; c.NextSlopeX = Slope(c,c.West,c.EastCell,true); c.NextSlopeZ = Slope(c,c.South,c.NorthCell,false); });
        Each(_wet.Count, i =>
        {
            var c = _wet[i];
            // Established channels and still pools retain their surface/current. Conserved
            // discharge still passes through; only expensive surface reconstruction sleeps.
            // Fronts, filling, pressure changes and geometry edits immediately wake it.
            c.Sleeping = c.QuietSeconds >= .5 && c.Volume > MinimumResolvedVolume &&
                Math.Abs(c.Volume-c.HeadVolume)/(Spacing*Spacing) <= SurfaceTolerance(c) &&
                Math.Max(Math.Abs(c.NextSlopeX-c.SlopeX),Math.Abs(c.NextSlopeZ-c.SlopeZ))*Spacing <= SurfaceTolerance(c);
            if (!c.Sleeping)
            { c.SlopeX=c.NextSlopeX; c.SlopeZ=c.NextSlopeZ; ReconstructSupport(c); c.Head=Head(c,c.Volume); }
        });
        Stage("reconstruct");
        foreach (var c in _wet)
        {
            AddFace(c,c.EastCell ?? Get((c.X+1,c.Z)),true);
            AddFace(c,c.NorthCell ?? Get((c.X,c.Z+1)),false);
            AddFace(c.West ?? Get((c.X-1,c.Z)),c,true);
            AddFace(c.South ?? Get((c.X,c.Z-1)),c,false);
        }
        Stage("faces");
        double damping = Math.Pow(PipeDamping, dt * 60);
        double area=Spacing*Spacing;
        foreach(var c in _changed)
        {c.Pressure=c.Head;c.Diagonal=area;c.Rhs=area*c.Head;c.CoefficientEast=c.CoefficientNorth=0;}
        // Backward pressure relaxation removes the explicit gravity-wave restriction.
        // Geometry/capacity is reconstructed once; these cheap neighbour iterations
        // smooth the active head field before conservative, limited transfers apply.
        for(int i=0;i<_workCount;i++)
        {
            var f=_work[i];var a=f.A;var b=f.B;bool east=f.East;
            double faceA=a.Head+(east?a.SlopeX:a.SlopeZ)*Spacing*.5;
            double faceB=b.Head-(east?b.SlopeX:b.SlopeZ)*Spacing*.5;
            double depth=FaceDepth(a,b,east,Math.Max(faceA,faceB));
            double q=east?a.East:a.North;
            if(depth<=1e-9 || q*(a.Head-b.Head)<0 || (q>0?a:b).Established)q=0;
            double section=Spacing*depth,k=Gravity*dt*dt*section*damping/Spacing;
            _work[i].Section=section;_work[i].Coefficient=k;_work[i].Momentum=q*damping;
            if(east)a.CoefficientEast=k;else a.CoefficientNorth=k;
            a.Diagonal+=k;b.Diagonal+=k;a.Rhs-=dt*q*damping;b.Rhs+=dt*q*damping;
        }
        // Red/black Gauss-Seidel uses the grid's bipartite adjacency: six paired
        // sweeps have the convergence factor of twelve Jacobi iterations, without
        // a second full-buffer copy on every iteration.
        for(int iteration=0;iteration<6;iteration++)for(int colour=0;colour<2;colour++)
        {
            int passColour=colour;
            Each(_changed.Count,i=>
            {
                var c=_changed[i];
                if(c.Established || ((c.X+c.Z)&1)!=passColour)return;
                double sum=c.Rhs;
                if(c.EastCell is {} e)sum+=c.CoefficientEast*e.Pressure;
                if(c.NorthCell is {} n)sum+=c.CoefficientNorth*n.Pressure;
                if(c.West is {} w && w.DirtyStamp==_stamp)sum+=w.CoefficientEast*w.Pressure;
                if(c.South is {} b && b.DirtyStamp==_stamp)sum+=b.CoefficientNorth*b.Pressure;
                c.Pressure=Math.Max(c.SupportMinimum,sum/c.Diagonal);
            });
        }
        Each(_workCount, i =>
        {
            var f=_work[i];var a=f.A;var b=f.B;
            double q=f.Section>0?f.Momentum+f.Coefficient*(a.Pressure-b.Pressure)/dt:0;
            double maximum=f.Section*Spacing/(dt*2);q=Math.Clamp(q,-maximum,maximum);
            if(f.East){a.East=q;a.EastSection=f.Section;}else{a.North=q;a.NorthSection=f.Section;}
            var donor=q>0?a:b;
            _work[i].Amount=q!=0&&!donor.Established&&donor.Volume>0?q*dt:0;
        });
        foreach (var c in _changed) c.Outgoing = 0;
        for (int i=0;i<_workCount;i++)
        { var f=_work[i]; if(f.Amount!=0) (f.Amount>0?f.A:f.B).Outgoing+=Math.Abs(f.Amount); }
        foreach(var c in _changed) c.OutflowScale=c.Outgoing>0?Math.Min(1,c.Volume/c.Outgoing):1;
        Stage("flux");
        for(int i=0;i<_workCount;i++)
        {
            var f=_work[i]; if(f.Amount==0)continue;
            var a=f.Amount>0?f.A:f.B;var b=f.Amount>0?f.B:f.A;
            double amount=Math.Abs(f.Amount)*a.OutflowScale;a.Volume-=amount;
            if(f.East)f.A.East*=a.OutflowScale;else f.A.North*=a.OutflowScale;
            if(amount<=0)continue;
            Receive(b,amount,budget);
        }
        Stage("transfer");
        if(evaporation>0||infiltration>0)
            foreach(var tile in _tileOrder)foreach(var c in tile.Cells)if(c!=null&&c.Volume>0)Mark(c);
        // Resolve mutable geometry-dependent inputs before the parallel numerical pass.
        if(infiltration>0)foreach(var c in _changed)
            if(c.Volume>0&&c.InfiltrationVersion!=_infiltrationVersion)
            {c.InfiltrationFactor=Math.Clamp(_infiltrationFactor?.Invoke(c.Position)??1,0,1);c.InfiltrationVersion=_infiltrationVersion;}
        Each(_changed.Count,i=>
        {
            var c=_changed[i];c.WakeRequested=false;c.Evaporated=c.Infiltrated=c.Escaped=c.Recharged=0;
            if(c.Volume<0&&c.Volume>-1e-14)c.Volume=0;
            if(c.Volume<0||!double.IsFinite(c.Volume))throw new InvalidOperationException("Fluid positivity failed");
            if(c.Volume>0)
            {
                if(!c.InDomain){c.Escaped=c.Volume;c.Volume=0;}
                else if(c.Ground[4]<WaterTable){c.Recharged=c.Volume;c.Volume=0;}
                else
                {
                    c.Evaporated=Math.Min(c.Volume,Math.Max(0,evaporation)*Spacing*Spacing*dt);c.Volume-=c.Evaporated;
                    c.Infiltrated=Math.Min(c.Volume,Math.Max(0,infiltration)*c.InfiltrationFactor*Spacing*Spacing*dt);c.Volume-=c.Infiltrated;
                }
            }
            if(c.Established)
            {c.WakeRequested=c.Volume<=MinimumResolvedVolume||Math.Abs(c.Volume-c.HeadVolume)/(Spacing*Spacing)>SurfaceTolerance(c);return;}
            double depthChange=Math.Abs(c.Volume-c.FrameVolume)/(Spacing*Spacing);
            if(c.Volume<=MinimumResolvedVolume)c.QuietSeconds=0;
            else c.QuietSeconds+=dt;
            if(c.Sleeping && (c.QuietSeconds==0 || Math.Abs(c.Volume-c.HeadVolume)/(Spacing*Spacing)>SurfaceTolerance(c)))c.Sleeping=false;
            if(c.Volume==0){c.SlopeX=c.SlopeZ=0;ReconstructSupport(c);c.Sleeping=false;}
            if(!c.Sleeping)c.Head=Head(c,c.Volume);
        });
        bool liveSource=springs.Any(s=>s.Discharge>0);
        foreach(var c in _changed)
        {if(c.WakeRequested)Wake(c);else if(c.QuietSeconds>=1&&c.Volume>.00005*Spacing*Spacing&&!c.Established&&(liveSource||Current((c.X,c.Z),c).Length<.001)){c.EstablishPending=true;_regionsDirty=true;}budget.Evaporation+=c.Evaporated;budget.Infiltration+=c.Infiltrated;budget.BoundaryOutflow+=c.Escaped;budget.GroundwaterRecharge+=c.Recharged;}
        Stage("heads");
        Version++;
    }
    private Vec2 Current((int X, int Z) key, Cell c)
    {
        var west=c.West;var south=c.South;
        // Area-weighted interpolation from the staggered faces. Using the remaining
        // cell volume here creates arbitrarily large apparent currents at wet/dry fronts.
        double xArea=c.EastSection+(west?.EastSection??0), zArea=c.NorthSection+(south?.NorthSection??0);
        // The face solve already stores its wetted cross-section. A centimetre-scale
        // depth floor makes millimetre films appear almost still despite real runoff.
        return new((c.East + (west?.East ?? 0)) / Math.Max(xArea, 1e-12),
            (c.North + (south?.North ?? 0)) / Math.Max(zArea, 1e-12));
    }
    public SurfaceFluidSnapshot CaptureRenderState()
    {
        int count=0;foreach(var c in _cells.Values)if(c.Volume>0)count++;
        var cells=new SurfaceFluidSample[count];int index=0;
        foreach(var (key,c) in _cells)if(c.Volume>0)
            cells[index++]=new(Center(key),c.Head,c.Volume/(Spacing*Spacing),Current(key,c),c.Volume,c.SlopeX,c.SlopeZ,c.Ground[4]);
        Array.Sort(cells,static(a,b)=>{int z=a.Position.Z.CompareTo(b.Position.Z);return z!=0?z:a.Position.X.CompareTo(b.Position.X);});
        return new(Version, Time, Spacing, cells, _air.ToArray(), EstablishedCells, SleepingCells, _tiles.Count);
    }
    public SurfaceFluidState Export()
    {
        // Region queues are derived caches. Canonicalize them at the snapshot boundary
        // so continuing this world and loading its snapshot start from the same routes.
        _regionsDirty=true;RebuildRegions(restoring:true);
        return new(_cells.Where(k => k.Value.Volume > 0 || k.Value.East != 0 || k.Value.North != 0 || k.Value.EastSection > 0 || k.Value.NorthSection > 0).OrderBy(k => k.Key.Z).ThenBy(k => k.Key.X)
        .Select(k => new SurfaceFluidCell(k.Key.X, k.Key.Z, k.Value.Volume, k.Value.East, k.Value.North, k.Value.SlopeX, k.Value.SlopeZ, k.Value.EastSection, k.Value.NorthSection, k.Value.QuietSeconds, k.Value.Head, k.Value.HeadVolume, _dirty.Contains(k.Key), k.Value.Established, k.Value.EstablishPending, k.Value.Sleeping)).ToArray(), _air.ToArray(), Time, Remainder, Spacing, new(_pending), WorkStamp:_stamp, NextRegionStamp:_nextRegionStamp);
    }
    /// <summary>Refine after loading has validated the original save digest.</summary>
    public void RefineResolution()
    {
        if (Spacing <= DefaultSpacing) return;
        var samples = CaptureRenderState().Cells;
        double oldSpacing = Spacing;
        _regionsDirty=true;_apertures.Clear();_cells.Clear(); _tiles.Clear(); _tileOrder.Clear(); _dirty.Clear(); Spacing = DefaultSpacing;
        foreach (var cell in samples)
            ImportLegacySquare(cell.Position - new Vec2(oldSpacing * .5, oldSpacing * .5),
                oldSpacing, cell.Volume, cell.Velocity);
        Version++;
    }

    public void Restore(SurfaceFluidState state)
    {
        if (!double.IsFinite(state.Time) || state.Time < 0 || !double.IsFinite(state.Remainder) || state.Remainder < 0 ||
            !double.IsFinite(state.Spacing) || state.Spacing < .005 || state.Spacing > .25) throw new InvalidDataException("Invalid surface-fluid clock/resolution");
        _regionsDirty=true;_apertures.Clear();_cells.Clear(); _tiles.Clear(); _tileOrder.Clear(); _air.Clear(); _pending.Clear(); _dirty.Clear(); Spacing = state.Spacing;
        foreach (var saved in state.Cells)
        {
            if (!double.IsFinite(saved.QuietSeconds) || saved.QuietSeconds < 0 || (saved.CachedHead is double sh && !double.IsFinite(sh)) || (saved.HeadVolume is double sv && (!double.IsFinite(sv) || sv < 0)) || !double.IsFinite(saved.Volume) || saved.Volume < 0 || !double.IsFinite(saved.EastFlux) || !double.IsFinite(saved.NorthFlux) || !double.IsFinite(saved.SlopeX) || !double.IsFinite(saved.SlopeZ) || !double.IsFinite(saved.EastSection) || saved.EastSection < 0 || !double.IsFinite(saved.NorthSection) || saved.NorthSection < 0 ||
                Math.Abs((double)saved.X * Spacing) > _domain.Radius + 1 || Math.Abs((double)saved.Z * Spacing) > _domain.Radius + 1 || _cells.ContainsKey((saved.X, saved.Z))) throw new InvalidDataException("Invalid or duplicate surface-fluid cell");
            var c = Get((saved.X, saved.Z)); c.Volume = saved.Volume; c.East = saved.EastFlux; c.North = saved.NorthFlux; c.SlopeX = saved.SlopeX; c.SlopeZ = saved.SlopeZ; c.EastSection = saved.EastSection; c.NorthSection = saved.NorthSection; ReconstructSupport(c); c.Head = saved.CachedHead ?? Head(c, c.Volume); c.HeadVolume=saved.HeadVolume??c.Volume;
            c.QuietSeconds=saved.QuietSeconds;c.Established=saved.Established;c.EstablishPending=saved.EstablishPending;c.Sleeping=saved.Sleeping; if(saved.Dirty)_dirty.Add((saved.X, saved.Z)); if (!double.IsFinite(c.Head)) throw new InvalidDataException("Invalid fluid free surface");
        }
        foreach (var p in state.Airborne) { if (!p.Position.IsFinite || !p.Velocity.IsFinite || !double.IsFinite(p.Volume) || p.Volume <= 0 || !double.IsFinite(p.Width) || p.Width < 0 || !p.WidthAxis.IsFinite) throw new InvalidDataException("Invalid airborne liquid"); _air.Add(p); }
        foreach (var (key, v) in state.SourceRemainders) { if (!double.IsFinite(v) || v < 0) throw new InvalidDataException("Invalid source remainder"); _pending.Add(key, v); }
        Time = state.Time; Remainder = state.Remainder;_stamp=state.WorkStamp;_nextRegionStamp=state.NextRegionStamp;RebuildRegions(restoring:true); Version++;
    }
    public void RestoreLegacy(FluidState state)
    {
        if (!double.IsFinite(state.Time) || state.Time < 0 || !double.IsFinite(state.Remainder) || state.Remainder < 0) throw new InvalidDataException("Invalid legacy fluid clock");
        foreach (var f in state.Particles)
        {
            if (!f.Position.IsFinite || !f.Velocity.IsFinite || !double.IsFinite(f.Volume) || f.Volume <= 0)
                throw new InvalidDataException("Invalid legacy airborne liquid");
            _air.Add(f);
        }
        foreach (var (key, v) in state.SourceRemainders) { if (!double.IsFinite(v) || v < 0) throw new InvalidDataException("Invalid legacy source fraction"); _pending.Add(key, v); }
        Time = state.Time; Remainder = state.Remainder; Version++;
    }
}
