using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Samples the highest non-vertical mesh triangle at XZ queries using a fixed spatial bin grid.</summary>
public sealed class TriangleSurfaceSampler
{
    public const double BinSize = .025;
    private const double BarycentricTolerance = 1e-9;
    private readonly List<Triangle> _triangles = new();
    private readonly Dictionary<long, List<int>> _bins = new();

    public TriangleSurfaceSampler(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Position(mesh.Indices[i]);
            var b = mesh.Position(mesh.Indices[i + 1]);
            var c = mesh.Position(mesh.Indices[i + 2]);
            if (!a.IsFinite || !b.IsFinite || !c.IsFinite) continue;
            double denominator = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-12) continue;

            int triIndex = _triangles.Count;
            _triangles.Add(new Triangle(a, b, c, denominator));
            int minX = Cell(Math.Min(a.X, Math.Min(b.X, c.X)));
            int maxX = Cell(Math.Max(a.X, Math.Max(b.X, c.X)));
            int minZ = Cell(Math.Min(a.Z, Math.Min(b.Z, c.Z)));
            int maxZ = Cell(Math.Max(a.Z, Math.Max(b.Z, c.Z)));
            for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    long key = Key(x, z);
                    if (!_bins.TryGetValue(key, out var entries)) _bins.Add(key, entries = new List<int>());
                    entries.Add(triIndex);
                }
        }
    }

    /// <summary>Returns the highest triangle surface at world XZ, or NaN when no projected triangle covers it.</summary>
    public double HeightAt(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) return double.NaN;
        if (!_bins.TryGetValue(Key(Cell(x), Cell(z)), out var entries)) return double.NaN;
        double highest = double.NegativeInfinity;
        foreach (int index in entries)
        {
            var t = _triangles[index];
            double wa = ((t.B.Z - t.C.Z) * (x - t.C.X) + (t.C.X - t.B.X) * (z - t.C.Z)) / t.Denominator;
            double wb = ((t.C.Z - t.A.Z) * (x - t.C.X) + (t.A.X - t.C.X) * (z - t.C.Z)) / t.Denominator;
            double wc = 1 - wa - wb;
            if (wa < -BarycentricTolerance || wb < -BarycentricTolerance || wc < -BarycentricTolerance) continue;
            double y = wa * t.A.Y + wb * t.B.Y + wc * t.C.Y;
            if (double.IsFinite(y) && y > highest) highest = y;
        }
        return double.IsNegativeInfinity(highest) ? double.NaN : highest;
    }

    private static int Cell(double coordinate)
    {
        double cell = Math.Floor(coordinate / BinSize);
        if (cell < int.MinValue || cell > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(coordinate));
        return (int)cell;
    }

    private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    private readonly record struct Triangle(Vec3 A, Vec3 B, Vec3 C, double Denominator);
}
