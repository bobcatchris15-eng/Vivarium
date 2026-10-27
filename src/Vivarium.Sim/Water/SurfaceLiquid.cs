using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;
using System.Text.Json;

namespace Vivarium.Sim.Water;

/// <summary>Single production owner of mobile water, jobs, published queries and geometry boundaries.</summary>
public sealed class SurfaceLiquid
{
    public static Action<string, double>? StageTimed { get; set; }
    private readonly VivariumWorld _w;
    private Task<(SurfaceFluidSnapshot Snapshot, WaterBudget Budget, double Ms)>? _job;
    private SurfaceFluidSnapshot _snapshot = new(0, 0, SurfaceFluidDynamics.DefaultSpacing, [], []);
    private readonly Dictionary<(int, int), SurfaceFluidSample> _samples = new();
    private readonly Dictionary<(int, int), double> _sampleSupport = new();
    private IReadOnlyList<FluidParticle> _contacts = [];
    private double _queuedSeconds, _erosionTime;
    private readonly LinkedList<FluidInputBatch> _inputs = new();
    private Heightfield? _solidTerrain;
    private PropSet? _solidProps;
    private PilotTreeState? _solidTree;
    private int _terrain = -1, _props = -1;
    public SurfaceFluidDynamics Dynamics { get; }
    public SurfaceErosion Erosion { get; }
    public bool Enabled { get; private set; }
    public bool AsyncMode { get; set; }
    public long Version => _snapshot.Version;
    public double PresentationTime => _snapshot.Time;
    public double BacklogSeconds => _queuedSeconds + Dynamics.Remainder;
    public double LastJobMilliseconds { get; private set; }
    public long PublishedAllocatedBytes { get; private set; }
    public double Volume => _snapshot.Cells.Sum(c => c.Volume) + _snapshot.Airborne.Sum(c => c.Volume) + _publishedFractions;
    private double _publishedFractions;
    /// <summary>Derived contact samples for erosion/diagnostics, not another volume authority.</summary>
    public IReadOnlyList<FluidParticle> Particles => _contacts;
    public SurfaceLiquid(VivariumWorld world)
    { _w = world; Dynamics = new(world.Domain, Ground); Dynamics.InfiltrationFactor = p => _solidProps?.RockAt(p) != null || _solidProps?.LogAt(p) != null ? 0 : _solidProps?.GravelAt(p) != null ? .25 : 1; Erosion = new(world); }
    private double Ground(Vec2 p)
    {
        if (_solidTerrain == null) return _w.GroundHeight(p);
        double h = _solidTerrain.Height(p), top = _solidProps?.PropTopAt(p) ?? double.NaN;
        if (!double.IsNaN(top)) h = Math.Max(h, top);
        double root = _solidTree?.RootSurfaceHeight(p, _solidTerrain) ?? double.NaN;
        if (!double.IsNaN(root)) h = Math.Max(h, root); return h;
    }
    public void Enable(FluidState? legacy = null)
    {
        if (Enabled) return; CaptureGeometry();
        if (legacy != null) { Dynamics.RestoreLegacy(legacy); Dynamics.ResolveLegacyFractions(_w.Water.Springs, legacy.Particles); }
        else foreach (int c in _w.Grid.DomainCells)
            {
                double volume = _w.Water.Depth[c] * _w.Water.CellArea; if (volume <= 0) continue;
                var position = _w.Grid.CellCenter(c); double section = Math.Max(1e-12, _w.Water.Depth[c] * _w.Grid.CellSize);
                double east = (_w.Water.FaceFlowEast[c] + (c % _w.Grid.Nx > 0 ? _w.Water.FaceFlowEast[c - 1] : 0)) * .5;
                double north = (_w.Water.FaceFlowNorth[c] + (c >= _w.Grid.Nx ? _w.Water.FaceFlowNorth[c - _w.Grid.Nx] : 0)) * .5;
                Dynamics.ImportLegacySquare(position - new Vec2(_w.Grid.CellSize * .5, _w.Grid.CellSize * .5), _w.Grid.CellSize, volume, new(east / section, north / section));
            }
        Enabled = true; Dynamics.WaterTable = _w.Water.WaterTable; _w.Water.Liquid = this; Publish();
    }
    public void Restore(SurfaceFluidState saved)
    {
        CompletePending(); CaptureGeometry(); Dynamics.Restore(saved); Enabled = true; _w.Water.Liquid = this; Dynamics.WaterTable = _w.Water.WaterTable; _erosionTime = saved.Time;
        _inputs.Clear(); _queuedSeconds = 0; foreach (var input in saved.PendingInputs ?? []) { if (!double.IsFinite(input.Seconds) || input.Seconds < 0) throw new InvalidDataException("Invalid pending liquid input"); _inputs.AddLast(input); _queuedSeconds += input.Seconds; }
        Publish();
    }
    private void CaptureGeometry()
    {
        if (_terrain == _w.Terrain.Version && _props == _w.Props.Version) return;
        // Jobs own a stable solid snapshot. Invalidation happens only after the prior job has joined.
        _solidTerrain = _w.Terrain.Snapshot();
        if (_props != _w.Props.Version) _solidProps = JsonSerializer.Deserialize<PropSet>(JsonSerializer.Serialize(_w.Props, WorldSerializer.Json), WorldSerializer.Json)!;
        _solidTree = _w.PilotTree; _terrain = _w.Terrain.Version; _props = _w.Props.Version; Dynamics.OnGeometryChanged();
    }
    public void OnTerrainChanged(Vec2? minimum = null, Vec2? maximum = null)
    { CompletePending(); CaptureGeometry(); Publish(); }
    public void OnSolidGeometryChanged(Vec2? minimum = null, Vec2? maximum = null) => OnTerrainChanged(minimum, maximum);
    public void Advance(double realSeconds)
    {
        if (!Enabled) return;
        if (!double.IsFinite(realSeconds) || realSeconds < 0) throw new ArgumentException("Invalid mobile-water frame duration");
        if (_job?.IsCompleted == true) CompletePending();
        if (_w.Clock.Paused) return;
        double addedSeconds = realSeconds * _w.Clock.SpeedMultiplier;
        if (addedSeconds > 0)
        {
            var sources = _w.Water.Springs.Select(s => new Spring { Id = s.Id, X = s.X, Z = s.Z, Discharge = s.Discharge }).ToArray();
            var input = new FluidInputBatch(addedSeconds, sources, _w.Water.Config.Evaporation / SimUnits.Day, _w.Water.Config.Infiltration / SimUnits.Day, _w.Water.WaterTable);
            var last = _inputs.Last;
            if (last != null && last.Value.Evaporation == input.Evaporation && last.Value.Infiltration == input.Infiltration && last.Value.WaterTable == input.WaterTable &&
                last.Value.Springs.Length == sources.Length && last.Value.Springs.Zip(sources).All(p => p.First.Id == p.Second.Id && p.First.X == p.Second.X && p.First.Z == p.Second.Z && p.First.Discharge == p.Second.Discharge))
                last.Value = last.Value with { Seconds = last.Value.Seconds + addedSeconds };
            else _inputs.AddLast(input);
            _queuedSeconds += addedSeconds;
        }
        if (_job != null || _queuedSeconds + Dynamics.Remainder < SurfaceFluidDynamics.StepSeconds) return;
        CaptureGeometry();
        if (_inputs.First == null) return;
        // Split on physical-step boundaries, integrating every rate-change interval exactly.
        // A sub-step source edit cannot retroactively replace the preceding interval's input.
        var batches = new List<FluidInputBatch>();
        while (batches.Count < 16 && _queuedSeconds + 1e-12 >= SurfaceFluidDynamics.StepSeconds)
        {
            double remaining = SurfaceFluidDynamics.StepSeconds, evaporation = 0, infiltration = 0, table = _w.Water.WaterTable;
            var integrated = new Dictionary<(ulong Id, double X, double Z), double>();
            while (remaining > 1e-12 && _inputs.First != null)
            {
                var input = _inputs.First.Value; double used = Math.Min(input.Seconds, remaining); remaining -= used; _queuedSeconds = Math.Max(0, _queuedSeconds - used);
                foreach (var source in input.Springs) { var key = (source.Id.Value, source.X, source.Z); integrated[key] = integrated.GetValueOrDefault(key) + source.Discharge * used; }
                evaporation += input.Evaporation * used; infiltration += input.Infiltration * used; table = input.WaterTable;
                if (input.Seconds <= used + 1e-12) _inputs.RemoveFirst(); else _inputs.First.Value = input with { Seconds = input.Seconds - used };
            }
            var sources = integrated.Select(p => new Spring { Id = new EntityId(p.Key.Id), X = p.Key.X, Z = p.Key.Z, Discharge = p.Value / SurfaceFluidDynamics.StepSeconds }).ToArray();
            batches.Add(new(SurfaceFluidDynamics.StepSeconds, sources, evaporation / SurfaceFluidDynamics.StepSeconds, infiltration / SurfaceFluidDynamics.StepSeconds, table));
        }
        if (batches.Count == 0) return;
        (SurfaceFluidSnapshot Snapshot, WaterBudget Budget, double Ms) Run()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew(); var budget = new WaterBudget();
            foreach (var batch in batches) { Dynamics.WaterTable = batch.WaterTable; Dynamics.Advance(batch.Seconds, batch.Springs, budget, batch.Evaporation, batch.Infiltration, 1); }
            return (Dynamics.CaptureRenderState(), budget, timer.Elapsed.TotalMilliseconds);
        }
        if (!AsyncMode) { var result = Run(); LastJobMilliseconds = result.Ms; ApplyBudget(result.Budget); Publish(result.Snapshot); ObserveErosion(); return; }
        _job = Task.Run(Run);
    }

    public void AdvancePhysical(double seconds)
    { CompletePending(); CaptureGeometry(); Dynamics.WaterTable = _w.Water.WaterTable; Dynamics.Advance(seconds, _w.Water.Springs, _w.Water.Budget, _w.Water.Config.Evaporation / SimUnits.Day, _w.Water.Config.Infiltration / SimUnits.Day); Publish(); ObserveErosion(); }
    public void CompletePending()
    {
        if (_job == null) return; var result = _job.GetAwaiter().GetResult(); _job = null;
        LastJobMilliseconds = result.Ms; ApplyBudget(result.Budget); Publish(result.Snapshot); ObserveErosion();
    }
    private void ApplyBudget(WaterBudget delta)
    {
        var b = _w.Water.Budget; b.SpringInflow += delta.SpringInflow; b.GroundwaterRecharge += delta.GroundwaterRecharge;
        b.BoundaryOutflow += delta.BoundaryOutflow; b.Evaporation += delta.Evaporation; b.Infiltration += delta.Infiltration;
    }
    private void ObserveErosion()
    {
        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsed = Math.Max(0, _snapshot.Time - _erosionTime); _erosionTime = _snapshot.Time; Erosion.Advance(_contacts, elapsed);
        StageTimed?.Invoke("Liquid.Erosion", System.Diagnostics.Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
    }
    private void Publish(SurfaceFluidSnapshot? snapshot = null)
    {
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _snapshot = snapshot ?? Dynamics.CaptureRenderState(); _publishedFractions = Dynamics.FractionalVolume;
        StageTimed?.Invoke("Liquid.SnapshotCapture", System.Diagnostics.Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);
        stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
        double spacing = _snapshot.Spacing;
        // Private main-thread query index can retain its capacity. Published state and
        // contact arrays remain immutable for callers holding the preceding snapshot.
        _samples.Clear();_samples.EnsureCapacity(_snapshot.Cells.Length);
        _sampleSupport.Clear();_sampleSupport.EnsureCapacity(_snapshot.Cells.Length);
        var contacts=new FluidParticle[_snapshot.Cells.Length+_snapshot.Airborne.Length];int contactIndex=0;
        foreach(var c in _snapshot.Cells)
        {
            var key=((int)Math.Floor(c.Position.X/spacing),(int)Math.Floor(c.Position.Z/spacing));
            double support=Ground(c.Position);_samples.Add(key,c);_sampleSupport.Add(key,support);
            contacts[contactIndex++]=new(Vec3.FromXZ(c.Position,support+SurfaceFluidDynamics.Radius(c.Volume)),Vec3.FromXZ(c.Velocity,0),c.Volume,0);
        }
        _snapshot.Airborne.CopyTo(contacts,contactIndex);_contacts=contacts;
        StageTimed?.Invoke("Liquid.QueryIndex", System.Diagnostics.Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);
        stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var h = _w.Water; Array.Clear(h.Depth); Array.Clear(h.FlowX); Array.Clear(h.FlowZ); Array.Clear(h.FaceFlowEast); Array.Clear(h.FaceFlowNorth);
        foreach (var sample in _snapshot.Cells)
        {
            int c = _w.Grid.CellAt(sample.Position); if (c < 0 || !_w.Grid.InDomain(c)) continue;
            h.Depth[c] += sample.Volume / h.CellArea; h.FlowX[c] += sample.Velocity.X * sample.Volume / h.Grid.CellSize; h.FlowZ[c] += sample.Velocity.Z * sample.Volume / h.Grid.CellSize;
        }
        // Airborne fluid is not an aquatic habitat or a saturated-ground measurement.
        h.InvalidateWaterDistance(); PublishedAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        StageTimed?.Invoke("Liquid.CoarseProjection", System.Diagnostics.Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);
    }
    public SurfaceFluidState CaptureState()
    { CompletePending(); return Dynamics.Export() with { PendingInputs = _inputs.Count == 0 ? null : _inputs.ToArray() }; }
    public SurfaceFluidSnapshot CaptureRenderState() => _snapshot;
    public double SurfaceAt(Vec2 p)
    {
        if(!_w.Domain.Contains(p))return double.NaN;
        double spacing=_snapshot.Spacing,gx=p.X/spacing-.5,gz=p.Z/spacing-.5;
        int x=(int)Math.Floor(gx+.5),z=(int)Math.Floor(gz+.5);
        double ground=double.NaN,depth=0,headSum=0,weight=0,lowest=double.PositiveInfinity,highest=double.NegativeInfinity,maxSlope=0,maxSpeed=0;
        // Match the GPU's nonnegative quadratic B-spline of conserved column depths.
        static double Kernel(double distance){double a=Math.Abs(distance);return a<.5?.75-a*a:a<1.5?.5*(1.5-a)*(1.5-a):0;}
        for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
        {
            double w=Kernel(gx-x-dx)*Kernel(gz-z-dz);if(w<=0)continue;
            if(!_samples.TryGetValue((x+dx,z+dz),out var sample)||sample.Depth<=1e-8)continue;
            if(double.IsNaN(ground))ground=_w.GroundHeight(p);
            double support=_sampleSupport[(x+dx,z+dz)];
            if(support-ground>spacing*1.2&&sample.Head-ground>spacing*1.2)
            {
                double quarter=_w.GroundHeight(sample.Position+(p-sample.Position)*.25);
                if(support+4*(quarter-support)-ground>spacing*1.2)continue;
            }
            depth+=sample.Depth*w;headSum+=sample.Head*w;weight+=w;
            lowest=Math.Min(lowest,sample.Head);highest=Math.Max(highest,sample.Head);
            maxSpeed=Math.Max(maxSpeed,sample.Velocity.Length);
            maxSlope=Math.Max(maxSlope,Math.Sqrt(sample.SlopeX*sample.SlopeX+sample.SlopeZ*sample.SlopeZ));
        }
        if(depth<=0||weight<=1e-6||ground<_w.Water.WaterTable)return double.NaN;
        bool ownerWet=_samples.TryGetValue((x,z),out var owner)&&owner.Depth>1e-8;
        if(ownerWet&&highest-lowest<=1e-6&&maxSlope<=1e-6&&maxSpeed<=1e-4)depth=Math.Max(0,headSum/weight-ground);
        return depth>1e-7?ground+depth:double.NaN;
    }
    public double DepthAt(Vec2 p) { double head = SurfaceAt(p); return double.IsNaN(head) ? 0 : Math.Max(0, head - _w.GroundHeight(p)); }
    public Vec2 VelocityAt(Vec2 p)
    { double s = _snapshot.Spacing; return _samples.TryGetValue(((int)Math.Floor(p.X / s), (int)Math.Floor(p.Z / s)), out var sample) ? sample.Velocity : Vec2.Zero; }
    public double WettedContactAt(Vec2 p) => Math.Clamp(DepthAt(p) / .002, 0, 1);
    public double BuoyancyAt(Vec3 p, double height) { double surface = SurfaceAt(p.XZ); return double.IsNaN(surface) ? 0 : Math.Clamp((surface - p.Y) / Math.Max(1e-8, height), 0, 1); }
    public double Add(Vec2 p, double radius, double volume)
    { CompletePending(); CaptureGeometry(); double added = Dynamics.AddWater(p, radius, volume); _w.Water.Budget.ToolInflow += added; Publish(); return added; }
    public double Remove(Vec2 p, double radius, double volume)
    { CompletePending(); double removed = Dynamics.Remove(p, radius, volume); _w.Water.Budget.ToolRemoval += removed; Publish(); return removed; }
}
