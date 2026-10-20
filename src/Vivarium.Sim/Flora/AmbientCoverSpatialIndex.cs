using Vivarium.Sim.Core;

namespace Vivarium.Sim.Flora;

/// <summary>
/// Deterministic, disposable spatial lookup for circular low-detail vegetation
/// exclusions and visual influences. Binning is purely a broad phase; exact
/// metric distance still decides eligibility. No organism/field state is owned.
/// </summary>
public sealed class AmbientCoverSpatialIndex
{
    public readonly record struct Disc(Vec2 Position, double Radius, double Weight);
    private readonly Dictionary<(int x, int z), List<Disc>> _bins = new();
    private readonly double _invCell;
    public int Count { get; private set; }
    public int LastCandidates { get; private set; }

    public AmbientCoverSpatialIndex(double bucketSize = 0.75)
    {
        if (!double.IsFinite(bucketSize) || bucketSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bucketSize));
        _invCell = 1 / bucketSize;
    }

    public void Clear()
    {
        _bins.Clear();
        Count = 0;
        LastCandidates = 0;
    }

    private int Cell(double x) => (int)Math.Floor(x * _invCell);

    public void Add(Vec2 center, double radius, double weight = 1)
    {
        if (!double.IsFinite(radius) || radius <= 0) return;
        var entry = new Disc(center, radius, weight);
        for (int z = Cell(center.Z - radius); z <= Cell(center.Z + radius); z++)
        for (int x = Cell(center.X - radius); x <= Cell(center.X + radius); x++)
        {
            var key = (x, z);
            if (!_bins.TryGetValue(key, out var group))
                _bins.Add(key, group = new List<Disc>());
            group.Add(entry);
        }
        Count++;
    }

    public bool Contains(Vec2 p)
    {
        if (!_bins.TryGetValue((Cell(p.X), Cell(p.Z)), out var group))
        {
            LastCandidates = 0;
            return false;
        }
        LastCandidates = group.Count;
        foreach (var e in group)
            if (Vec2.DistanceSq(p, e.Position) < e.Radius * e.Radius)
                return true;
        return false;
    }

    /// <summary>Local maturity-weighted influence of overlapping living
    /// ground-cover parents; saturates at one. There are no synthetic organisms.</summary>
    public double Influence(Vec2 p)
    {
        if (!_bins.TryGetValue((Cell(p.X), Cell(p.Z)), out var group))
        {
            LastCandidates = 0;
            return 0;
        }
        LastCandidates = group.Count;
        double influence = 0;
        foreach (var e in group)
        {
            double dist = Vec2.Distance(p, e.Position);
            if (dist >= e.Radius) continue;
            double t = 1 - dist / e.Radius;
            influence += e.Weight * t * t;
            if (influence >= 1) return 1;
        }
        return Math.Clamp(influence, 0, 1);
    }
}
