using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Seed-derived fracture triangles shared by rendering and physical surface queries.</summary>
public sealed class RockSurfaceProfile
{
    public readonly record struct Triangle(Vec3 A, Vec3 B, Vec3 C, bool Bottom = false);
    private readonly Triangle[] _triangles;
    public int Family { get; }
    public int Palette { get; }
    public IReadOnlyList<Triangle> Triangles { get; }

    private RockSurfaceProfile(int family, int palette, List<Triangle> triangles)
    {
        Family = family;
        Palette = palette;
        _triangles = triangles.ToArray();
        Triangles = Array.AsReadOnly(_triangles);
    }

    public static RockSurfaceProfile Create(ulong seed)
    {
        var rng = Rng.Keyed(seed, "rock.mesh", 0);
        int palette = rng.NextInt(6), family = rng.NextInt(3);
        int sides = 6 + rng.NextInt(3);
        const int levels = 7;
        double phase = rng.Range(0, Math.PI * 2);
        double[] heights = { -0.38, -0.18, 0.10, 0.13, 0.38, 0.41, 0.61 };
        double[] radii = family switch
        {
            0 => new[] { .72, .90, .88, .68, .64, .43, .30 },
            1 => new[] { .76, .91, .89, .72, .68, .50, .39 },
            _ => new[] { .68, .88, .83, .59, .54, .32, .18 },
        };
        var rings = new Vec3[levels, sides];
        for (int s = 0; s < sides; s++)
        {
            double angle = phase + 2 * Math.PI * (s + rng.Range(-.16, .16)) / sides;
            double fracture = rng.Range(.87, 1.0);
            double tilt = rng.Range(-.018, .018);
            double lowExposure = rng.Range(.35, 1.10), highExposure = rng.Range(.25, 1.40);
            double lowBed = rng.Range(-.035, .035), highBed = rng.Range(-.035, .035);
            for (int r = 0; r < levels; r++)
            {
                // Paired near-horizontal bands expose broad shelves between steep fracture walls.
                // Reusing each angular fracture across bands keeps long, readable broken edges.
                double radius = radii[r];
                if (r == 3) radius = radii[2] - (radii[2]-radii[3]) * lowExposure;
                if (r == 5) radius = radii[4] - (radii[4]-radii[5]) * highExposure;
                radius *= fracture;
                double y = heights[r] + tilt * (r == 0 ? 0 : 1)
                    + (r is 2 or 3 ? lowBed : r is 4 or 5 ? highBed : 0);
                rings[r, s] = new Vec3(Math.Cos(angle) * radius, y, Math.Sin(angle) * radius);
            }
        }
        var triangles = new List<Triangle>(140);
        for (int r = 0; r < levels - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int n = (s + 1) % sides;
                triangles.Add(new(rings[r, s], rings[r, n], rings[r + 1, n]));
                triangles.Add(new(rings[r, s], rings[r + 1, n], rings[r + 1, s]));
            }
        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides;
            triangles.Add(new(new Vec3(0, -.38, 0), rings[0, n], rings[0, s], true));
            triangles.Add(new(new Vec3(0, .75, 0), rings[levels - 1, s], rings[levels - 1, n]));
        }
        return new(family, palette, triangles);
    }

    /// <summary>Highest vertical intersection with the actual triangle soup, with no query allocation.</summary>
    public double TopAt(double x, double z)
    {
        double highest = double.NaN;
        for (int i = 0; i < _triangles.Length; i++)
        {
            var triangle = _triangles[i];
            if (triangle.Bottom) continue;
            var a = triangle.A; var b = triangle.B; var c = triangle.C;
            double denominator = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (Math.Abs(denominator) < 1e-14) continue;
            double u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / denominator;
            double v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / denominator;
            double w = 1 - u - v;
            if (u < -1e-10 || v < -1e-10 || w < -1e-10) continue;
            double height = u * a.Y + v * b.Y + w * c.Y;
            if (double.IsNaN(highest) || height > highest) highest = height;
        }
        return highest;
    }
}
