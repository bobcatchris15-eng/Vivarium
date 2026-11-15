using Vivarium.Sim.Core;

namespace Vivarium.Sim.Water;

/// <summary>Canonical pool membership and accepted throughflow; cell volumes remain authoritative storage.</summary>
public sealed record SurfacePoolState(int[][] Members, double Volume, double Head, Vec2 Velocity);

public sealed partial class SurfaceFluidDynamics
{
    private sealed class Region
    {
        public readonly List<Cell> Cells = new();
        public readonly List<(Cell A, Cell B, bool East)> Boundaries = new();
        public double Volume, Head, MeanSum, MaximumGround, Outgoing, Scale;
        public double MaterializedVolume = double.NaN;
        public Vec2 Centre, Moment, Velocity;
    }
    private readonly List<Region> _regions = new();
    private long _nextRegionStamp;
    private SurfaceFluidState? _legacyState;
    public int EstablishedCells => _cells.Values.Count(c => c.Region != null);

    // Loading must first reproduce the exact saved digest. Upgrade old pinned-cell
    // caches only at the next authoritative mutation, without changing stored water.
    private void EnsurePoolModel()
    {
        if (_legacyState == null) return;
        _legacyState = null; _regions.Clear();
        foreach (var c in _cells.Values)
        {
            c.Region = null; c.Established = c.EstablishPending = c.Sleeping = false;
            c.QuietSeconds = 0; c.East = c.North = 0;
            c.SlopeX = c.SlopeZ = 0; ReconstructSupport(c); c.Head = Head(c, c.Volume);
            if (c.Volume > 0) _dirty.Add((c.X, c.Z));
        }
        _nextRegionStamp = 0;
    }
    public void OnSourcesChanged()
    {
        EnsurePoolModel();
        foreach (var region in _regions.ToArray()) Wake(region.Cells[0]);
        foreach (var c in _cells.Values)
        { c.Established = c.EstablishPending = c.Sleeping = false; c.QuietSeconds = 0; }
        _nextRegionStamp = 0; Version++;
    }
    private void Wake(Cell c)
    {
        if (c.Region is { } region)
        {
            // Hand the body's accepted mean current back to internal faces before
            // active transport resumes. Existing boundary momentum stays intact.
            foreach (var member in region.Cells)
            {
                if (member.EastCell is { } east && east.Region == region)
                {
                    member.EastSection = Spacing * FaceDepth(member, east, true, region.Head);
                    member.East = region.Velocity.X * member.EastSection;
                }
                if (member.NorthCell is { } north && north.Region == region)
                {
                    member.NorthSection = Spacing * FaceDepth(member, north, false, region.Head);
                    member.North = region.Velocity.Z * member.NorthSection;
                }
            }
            foreach (var member in region.Cells)
            {
                member.Region = null; member.Established = member.EstablishPending = member.Sleeping = false;
                member.QuietSeconds = 0; _dirty.Add((member.X, member.Z));
            }
            _regions.Remove(region);
        }
        c.Established = c.EstablishPending = c.Sleeping = false; c.QuietSeconds = 0;
    }
    private bool PoolCandidate(Cell c)
    {
        if (!c.InDomain || c.Ground[4] < WaterTable || c.Volume <= MinimumResolvedVolume) return false;
        if (c.Region != null) return true;
        if (c.QuietSeconds < .5 || Math.Max(Math.Abs(c.SlopeX), Math.Abs(c.SlopeZ)) > .15) return false;
        double highest = c.Ground.Max(), depth = c.Head - highest;
        return depth > .015 && Current((c.X, c.Z), c).Length < .4 * Math.Sqrt(Gravity * depth);
    }
    private void RebuildRegions()
    {
        if (_stamp < _nextRegionStamp) return;
        _nextRegionStamp = _stamp + 30;
        var candidates = _tileOrder.SelectMany(t => t.Cells).Where(c => c != null && PoolCandidate(c)).Cast<Cell>().ToArray();
        var available = new HashSet<Cell>(candidates);
        // Membership is rebuilt from deterministic grid order. Existing validated
        // bodies remain candidates; growing shore cells must independently settle.
        foreach (var region in _regions)
            foreach (var c in region.Cells) { c.Region = null; c.Established = c.Sleeping = false; }
        _regions.Clear();
        var stack = new List<Cell>();
        foreach (var start in candidates)
        {
            if (!available.Remove(start)) continue;
            var region = new Region(); stack.Clear(); stack.Add(start);
            while (stack.Count > 0)
            {
                var c = stack[^1]; stack.RemoveAt(stack.Count - 1); region.Cells.Add(c);
                void Visit(Cell? n)
                {
                    if (n != null && Math.Abs(n.Head - c.Head) <= .002 && Math.Abs(n.Head - start.Head) <= .005 && available.Remove(n)) stack.Add(n);
                }
                Visit(c.EastCell); Visit(c.NorthCell); Visit(c.West); Visit(c.South);
            }
            if (region.Cells.Count < 4) continue;
            region.Cells.Sort(static (a, b) => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
            PopulateGeometry(region);
            region.Volume = region.Cells.Sum(c => c.Volume);
            region.Head = (region.Volume / (Spacing * Spacing) + region.MeanSum) / region.Cells.Count;
            if (region.Head <= region.MaximumGround + .0001 ||
                region.Cells.Max(c => c.Head) - region.Cells.Min(c => c.Head) > .01) continue;
            foreach (var c in region.Cells)
            {
                c.Region = region; c.Established = c.Sleeping = true; c.EstablishPending = false;
                c.SlopeX = c.SlopeZ = 0; ReconstructSupport(c);
            }
            _regions.Add(region); Materialize(region);
        }
        foreach (var region in _regions) BuildBoundaries(region);
    }
    private static void PopulateGeometry(Region region)
    {
        region.MeanSum = 0; region.MaximumGround = double.NegativeInfinity; region.Centre = Vec2.Zero;
        foreach (var c in region.Cells)
        { region.MeanSum += c.Mean; region.MaximumGround = Math.Max(region.MaximumGround, c.Ground.Max()); region.Centre += c.Position; }
        region.Centre /= region.Cells.Count;
    }
    private void BuildBoundaries(Region region)
    {
        region.Boundaries.Clear();
        foreach (var c in region.Cells)
        {
            var e = c.EastCell ?? Get((c.X + 1, c.Z)); var n = c.NorthCell ?? Get((c.X, c.Z + 1));
            var w = c.West ?? Get((c.X - 1, c.Z)); var s = c.South ?? Get((c.X, c.Z - 1));
            if (e.Region != region) region.Boundaries.Add((c, e, true)); else { c.East = 0; c.EastSection = 0; }
            if (n.Region != region) region.Boundaries.Add((c, n, false)); else { c.North = 0; c.NorthSection = 0; }
            if (w.Region != region) region.Boundaries.Add((w, c, true));
            if (s.Region != region) region.Boundaries.Add((s, c, false));
        }
    }
    private void Receive(Cell c, double amount, WaterBudget budget)
    {
        if (!c.InDomain) { budget.BoundaryOutflow += amount; return; }
        if (c.Ground[4] < WaterTable) { budget.GroundwaterRecharge += amount; return; }
        c.Volume += amount; Mark(c);
    }
    private void Materialize(Region region)
    {
        if (region.MaterializedVolume == region.Volume) return;
        double area = Spacing * Spacing;
        double head = (region.Volume / area + region.MeanSum) / region.Cells.Count;
        bool release = head < region.MaximumGround + 1e-7;
        if (release)
        {
            // A draining body hands storage back to wet/dry cells. Invert its exact
            // capacity once so release neither invents water nor clamps it away.
            double lo = region.Cells.Min(c => c.SupportMinimum), hi = region.MaximumGround + region.Volume / area;
            for (int i = 0; i < 48; i++)
            {
                double middle = (lo + hi) * .5, capacity = 0;
                foreach (var c in region.Cells) capacity += Capacity(c, middle);
                if (capacity < region.Volume) lo = middle; else hi = middle;
            }
            head = (lo + hi) * .5;
        }
        region.Head = head;
        double assigned = 0;
        var residualCell = region.Cells[0];
        for (int i = 0; i < region.Cells.Count; i++)
        {
            var c = region.Cells[i];
            double volume = release ? Capacity(c, head) : area * (head - c.Mean);
            // Put the floating-point summation residual into a fully wet central cell.
            c.Volume = volume; c.Head = head; c.HeadVolume = volume; assigned += volume;
            if (c.Volume > residualCell.Volume) residualCell = c;
        }
        residualCell.Volume += region.Volume - assigned; residualCell.HeadVolume = residualCell.Volume;
        region.MaterializedVolume = region.Volume;
        if (residualCell.Volume < -1e-14) throw new InvalidOperationException("Invalid pool capacity residual");
        if (release) Wake(region.Cells[0]);
    }
    private SurfacePoolState[]? ExportPools() => _regions.Count == 0 ? null : _regions.Select(r =>
        new SurfacePoolState(r.Cells.Select(c => new[] { c.X, c.Z }).ToArray(), r.Volume, r.Head, r.Velocity)).ToArray();
    private void RestorePools(SurfacePoolState[]? pools)
    {
        var declared = _cells.Values.Where(c => c.Established).ToHashSet();
        foreach (var c in _cells.Values) c.Established = c.EstablishPending = false;
        foreach (var saved in pools ?? [])
        {
            if (saved.Members.Length < 4 || !double.IsFinite(saved.Volume) || saved.Volume <= 0 ||
                !double.IsFinite(saved.Head) || !saved.Velocity.IsFinite) throw new InvalidDataException("Invalid saved pool");
            var region = new Region { Volume = saved.Volume, Head = saved.Head, Velocity = saved.Velocity, MaterializedVolume = saved.Volume };
            foreach (var key in saved.Members)
            {
                if (key.Length != 2 || !_cells.TryGetValue((key[0], key[1]), out var c) || !declared.Remove(c) || c.Region != null || c.Volume <= 0 ||
                    !c.InDomain || c.Ground[4] < WaterTable ||
                    c.SlopeX != 0 || c.SlopeZ != 0 || c.Head != saved.Head) throw new InvalidDataException("Invalid saved pool member");
                if (region.Cells.Count > 0 && (c.Z < region.Cells[^1].Z || c.Z == region.Cells[^1].Z && c.X <= region.Cells[^1].X))
                    throw new InvalidDataException("Noncanonical saved pool order");
                c.Region = region; c.Established = c.Sleeping = true; region.Cells.Add(c);
            }
            PopulateGeometry(region);
            if (saved.Head < region.MaximumGround || Math.Abs(region.Cells.Sum(c => c.Volume) - saved.Volume) > 1e-12)
                throw new InvalidDataException("Saved pool capacity mismatch");
            double area = Spacing * Spacing;
            foreach (var c in region.Cells)
                if (Math.Abs(c.Volume - area * (saved.Head - c.Mean)) > 1e-12)
                    throw new InvalidDataException("Saved pool member capacity mismatch");
            var remaining = region.Cells.ToHashSet(); var frontier = new Stack<Cell>();
            remaining.Remove(region.Cells[0]); frontier.Push(region.Cells[0]);
            while (frontier.TryPop(out var c))
            {
                void Visit(Cell? n) { if (n != null && remaining.Remove(n)) frontier.Push(n); }
                Visit(c.West); Visit(c.EastCell); Visit(c.South); Visit(c.NorthCell);
            }
            if (remaining.Count != 0) throw new InvalidDataException("Disconnected saved pool");
            _regions.Add(region);
        }
        if (declared.Count != 0) throw new InvalidDataException("Saved pool membership is incomplete");
        foreach (var region in _regions) BuildBoundaries(region);
    }
}
