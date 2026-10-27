using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only world-space occupancy. No Godot objects or simulation state.</summary>
public enum AccommodationTissue { Fixed, Wood, Stem, Petiole, Blade }

public readonly record struct AccommodationKey(ulong Organism, ulong Part) : IComparable<AccommodationKey>
{
    public int CompareTo(AccommodationKey other)
    {
        int c = Organism.CompareTo(other.Organism);
        return c != 0 ? c : Part.CompareTo(other.Part);
    }
}

public readonly record struct AccommodationCapsule(
    AccommodationKey Key, AccommodationTissue Tissue, Vec3 Start, Vec3 End,
    double Radius, double Stiffness)
{
    public (Vec3 Min, Vec3 Max) Bounds
    {
        get
        {
            var r = new Vec3(Radius, Radius, Radius);
            return (new Vec3(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Min(Start.Z, End.Z)) - r,
                    new Vec3(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y), Math.Max(Start.Z, End.Z)) + r);
        }
    }
}

/// <summary>
/// Sparse spatial hash of structural claims. Deterministic query ordering; supports
/// removal/reinsertion when a plant changes. This is broad phase, not a physics engine.
/// </summary>
public sealed class FoliageOccupancyIndex
{
    private readonly double _cell;
    private readonly Dictionary<(int X, int Y, int Z), HashSet<AccommodationKey>> _cells = new();
    private readonly Dictionary<AccommodationKey, AccommodationCapsule> _parts = new();
    private readonly Dictionary<AccommodationKey, List<(int X, int Y, int Z)>> _memberships = new();

    public FoliageOccupancyIndex(double cellSize = 0.25)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cell = cellSize;
    }

    private int Cell(double x) => checked((int)Math.Floor(x / _cell));

    private IEnumerable<(int X, int Y, int Z)> Cells(Vec3 min, Vec3 max)
    {
        int x0 = Cell(min.X), x1 = Cell(max.X), y0 = Cell(min.Y), y1 = Cell(max.Y), z0 = Cell(min.Z), z1 = Cell(max.Z);
        // Reject pathological input before allocating an unbounded cell enumeration.
        if ((long)(x1 - (long)x0 + 1) * (y1 - (long)y0 + 1) * (z1 - (long)z0 + 1) > 100000)
            throw new ArgumentOutOfRangeException(nameof(max), "Occupancy primitive spans too many cells");
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    yield return (x, y, z);
    }

    public int Count => _parts.Count;

    public void Upsert(AccommodationCapsule part)
    {
        if (!part.Start.IsFinite || !part.End.IsFinite || !double.IsFinite(part.Radius) || part.Radius < 0 ||
            !double.IsFinite(part.Stiffness)) throw new ArgumentException("Invalid occupancy primitive", nameof(part));
        var bounds = part.Bounds;
        var cells = Cells(bounds.Min, bounds.Max).ToList(); // Validate before changing existing claim.
        Remove(part.Key);
        _parts.Add(part.Key, part);
        _memberships.Add(part.Key, cells);
        foreach (var cell in cells)
        {
            if (!_cells.TryGetValue(cell, out var keys)) _cells[cell] = keys = new HashSet<AccommodationKey>();
            keys.Add(part.Key);
        }
    }

    public bool Remove(AccommodationKey key)
    {
        if (!_parts.Remove(key)) return false;
        foreach (var cell in _memberships[key])
        {
            var keys = _cells[cell];
            keys.Remove(key);
            if (keys.Count == 0) _cells.Remove(cell);
        }
        _memberships.Remove(key);
        return true;
    }

    public IReadOnlyList<AccommodationCapsule> Query(Vec3 min, Vec3 max)
    {
        if (!min.IsFinite || !max.IsFinite || min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            throw new ArgumentException("Invalid query bounds");
        var found = new SortedSet<AccommodationKey>();
        foreach (var cell in Cells(min, max))
            if (_cells.TryGetValue(cell, out var keys)) found.UnionWith(keys);
        return found.Select(k => _parts[k]).Where(p => Intersects(p.Bounds, (min, max))).ToArray();
    }

    private static bool Intersects((Vec3 Min, Vec3 Max) a, (Vec3 Min, Vec3 Max) b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
        a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
        a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

    /// <summary>Distance from a world-space point to the surface of a structural capsule.</summary>
    public static double SignedDistance(Vec3 point, AccommodationCapsule capsule)
    {
        Vec3 ab = capsule.End - capsule.Start;
        double t = ab.LengthSq > 1e-16 ? MathD.Clamp01((point - capsule.Start).Dot(ab) / ab.LengthSq) : 0;
        return (point - (capsule.Start + ab * t)).Length - capsule.Radius;
    }
}
