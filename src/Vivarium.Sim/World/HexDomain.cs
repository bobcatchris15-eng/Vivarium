using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

public enum HexRegion { Interior, Edge, Corner, Exterior }

/// <summary>
/// Canonical flat-top regular hexagon centred on the origin in the XZ plane.
/// Circumradius R = diameter / 2 (corner to corner). Vertices at angles 0°, 60°, … 300°.
/// Edge k joins vertex k and k+1; its outward normal points at 30° + 60°k; apothem = R·√3/2.
/// </summary>
public sealed class HexDomain
{
    public double Diameter { get; }
    public double Radius { get; }
    public double Apothem { get; }
    /// <summary>Classification tolerance for edge/corner detection (metres).</summary>
    public double Tolerance { get; }

    public readonly Vec2[] Vertices = new Vec2[6];
    public readonly Vec2[] EdgeNormals = new Vec2[6];

    public HexDomain(double diameter, double tolerance = 1e-6)
    {
        if (!(diameter > 0)) throw new ArgumentOutOfRangeException(nameof(diameter));
        Diameter = diameter;
        Radius = diameter / 2;
        Apothem = Radius * Math.Sqrt(3) / 2;
        Tolerance = tolerance;
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3;
            Vertices[k] = new Vec2(Radius * Math.Cos(a), Radius * Math.Sin(a));
            double na = Math.PI / 6 + k * Math.PI / 3;
            EdgeNormals[k] = new Vec2(Math.Cos(na), Math.Sin(na));
        }
    }

    /// <summary>Signed distance to the boundary: negative inside, positive outside (exact for convex polygon).</summary>
    public double SignedDistance(Vec2 p)
    {
        double maxPlane = double.NegativeInfinity;
        for (int k = 0; k < 6; k++) maxPlane = Math.Max(maxPlane, p.Dot(EdgeNormals[k]) - Apothem);
        if (maxPlane <= 0) return maxPlane;
        return Vec2.Distance(p, NearestBoundaryPoint(p));
    }

    public bool Contains(Vec2 p) => SignedDistance(p) <= Tolerance;

    /// <summary>True when a disc of the given radius lies fully inside the hexagon.</summary>
    public bool ContainsDisc(Vec2 p, double radius) => SignedDistance(p) <= -radius + Tolerance;

    public HexRegion Classify(Vec2 p)
    {
        double sd = SignedDistance(p);
        if (sd > Tolerance) return HexRegion.Exterior;
        if (sd < -Tolerance) return HexRegion.Interior;
        foreach (var v in Vertices) if (Vec2.Distance(p, v) <= Tolerance * 2) return HexRegion.Corner;
        return HexRegion.Edge;
    }

    /// <summary>Closest point on the hexagon perimeter.</summary>
    public Vec2 NearestBoundaryPoint(Vec2 p) => NearestBoundary(p).Point;

    /// <summary>Outward unit normal at the nearest boundary position (bisector at corners).</summary>
    public Vec2 BoundaryNormal(Vec2 p) => NearestBoundary(p).Normal;

    public (Vec2 Point, Vec2 Normal, int Edge) NearestBoundary(Vec2 p)
    {
        double best = double.PositiveInfinity;
        Vec2 bestPt = Vec2.Zero; int bestEdge = 0; double bestT = 0;
        for (int k = 0; k < 6; k++)
        {
            Vec2 a = Vertices[k], b = Vertices[(k + 1) % 6];
            Vec2 ab = b - a;
            double t = MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq);
            Vec2 q = a + ab * t;
            double d = Vec2.DistanceSq(p, q);
            if (d < best - 1e-18) { best = d; bestPt = q; bestEdge = k; bestT = t; }
        }
        Vec2 n = EdgeNormals[bestEdge];
        double edgeLen = Radius; // side length of a regular hexagon equals its circumradius
        if (bestT * edgeLen <= Tolerance * 2) n = (EdgeNormals[bestEdge] + EdgeNormals[(bestEdge + 5) % 6]).Normalized();
        else if ((1 - bestT) * edgeLen <= Tolerance * 2) n = (EdgeNormals[bestEdge] + EdgeNormals[(bestEdge + 1) % 6]).Normalized();
        return (bestPt, n, bestEdge);
    }

    /// <summary>Clamps a point into the domain, inset by margin (useful for keeping organisms inside).</summary>
    public Vec2 ClampInside(Vec2 p, double margin = 0)
    {
        for (int iter = 0; iter < 3; iter++)
        {
            bool moved = false;
            for (int k = 0; k < 6; k++)
            {
                double over = p.Dot(EdgeNormals[k]) - (Apothem - margin);
                if (over > 0) { p -= EdgeNormals[k] * over; moved = true; }
            }
            if (!moved) break;
        }
        return p;
    }

    /// <summary>Axis-aligned bounds: x in [-R, R], z in [-apothem, apothem].</summary>
    public (double MinX, double MaxX, double MinZ, double MaxZ) Bounds => (-Radius, Radius, -Apothem, Apothem);

    /// <summary>Clips a convex polygon (XZ) to the hexagon (Sutherland–Hodgman). Returns an empty list if disjoint.</summary>
    public List<Vec2> ClipPolygon(IReadOnlyList<Vec2> poly)
    {
        var output = new List<Vec2>(poly);
        for (int k = 0; k < 6 && output.Count > 0; k++)
        {
            var input = output;
            output = new List<Vec2>(input.Count + 2);
            Vec2 n = EdgeNormals[k];
            double Dist(Vec2 v) => v.Dot(n) - Apothem;
            for (int i = 0; i < input.Count; i++)
            {
                Vec2 cur = input[i], prev = input[(i + input.Count - 1) % input.Count];
                double dc = Dist(cur), dp = Dist(prev);
                bool cin = dc <= 0, pin = dp <= 0;
                if (cin)
                {
                    if (!pin) output.Add(Intersect(prev, cur, dp, dc));
                    output.Add(cur);
                }
                else if (pin) output.Add(Intersect(prev, cur, dp, dc));
            }
        }
        // Snap vertices that land on a boundary plane exactly onto it (removes 1e-16 drift).
        for (int i = 0; i < output.Count; i++)
            for (int k = 0; k < 6; k++)
            {
                double d = output[i].Dot(EdgeNormals[k]) - Apothem;
                if (d > 0 && d < 1e-9) output[i] -= EdgeNormals[k] * d;
            }
        return output;
    }

    private static Vec2 Intersect(Vec2 a, Vec2 b, double da, double db)
    {
        double t = da / (da - db);
        return a + (b - a) * t;
    }
}
