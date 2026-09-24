using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tools;

/// <summary>
/// Engine-neutral client camera collision against authoritative opaque world geometry.
/// Camera collision is render/navigation state only and never changes simulation state.
/// </summary>
public static class CameraCollision
{
    public const double DefaultRadius = 0.035;
    private const double Skin = 0.0015;

    public static Vec3 Resolve(VivariumWorld w, Vec3 start, Vec3 desired, double radius = DefaultRadius)
    {
        radius = Math.Clamp(radius, 0.005, 0.25);
        var pos = Depenetrate(w, start, radius);
        var remaining = desired - pos;
        if (remaining.LengthSq < 1e-14) return pos;

        for (int pass = 0; pass < 3 && remaining.LengthSq > 1e-12; pass++)
        {
            var origin = pos;
            double len = remaining.Length;
            int steps = Math.Clamp((int)Math.Ceiling(len / Math.Max(0.02, radius * 0.7)), 1, 512);
            bool collided = false;

            for (int i = 1; i <= steps; i++)
            {
                double t1 = (double)i / steps;
                var probe = origin + remaining * t1;
                if (!TryPenetration(w, probe, radius, out _, out _)) continue;

                double lo = (double)(i - 1) / steps, hi = t1;
                for (int k = 0; k < 11; k++)
                {
                    double mid = (lo + hi) * 0.5;
                    if (TryPenetration(w, origin + remaining * mid, radius, out _, out _)) hi = mid;
                    else lo = mid;
                }

                var contact = origin + remaining * hi;
                if (!TryPenetration(w, contact, radius, out var normal, out _)) normal = Vec3.Up;
                pos = origin + remaining * lo + normal * Skin;

                var tail = remaining * (1.0 - hi);
                double into = tail.Dot(normal);
                if (into < 0) tail -= normal * into;
                remaining = tail;
                collided = true;
                break;
            }

            if (!collided)
            {
                pos = origin + remaining;
                remaining = Vec3.Zero;
            }
        }

        return Depenetrate(w, pos, radius);
    }

    private static Vec3 Depenetrate(VivariumWorld w, Vec3 p, double radius)
    {
        for (int i = 0; i < 8; i++)
        {
            if (!TryPenetration(w, p, radius, out var n, out var depth)) break;
            p += n * (Math.Max(depth, 0.0005) + Skin);
        }
        return p;
    }

    private static bool TryPenetration(VivariumWorld w, Vec3 p, double radius, out Vec3 normal, out double depth)
    {
        var bestNormal = Vec3.Up;
        double bestDepth = 0;
        bool hit = false;

        void Consider(double d, Vec3 n)
        {
            if (!(d > bestDepth) || !double.IsFinite(d)) return;
            bestDepth = d;
            bestNormal = n.LengthSq > 1e-12 ? n.Normalized() : Vec3.Up;
            hit = true;
        }

        double sd = w.Domain.SignedDistance(p.XZ);
        var surfaceXZ = w.Domain.Contains(p.XZ) ? p.XZ : w.Domain.NearestBoundaryPoint(p.XZ);
        double surface = w.Terrain.Height(surfaceXZ);
        double bottom = w.Terrain.Bottom;
        if (sd < radius && p.Y < surface + radius && p.Y > bottom - radius)
        {
            double topExit = surface + radius - p.Y;
            double bottomExit = p.Y - (bottom - radius);
            double sideExit = radius - sd;
            if (topExit <= bottomExit && topExit <= sideExit)
                Consider(topExit, w.Terrain.Normal(surfaceXZ));
            else if (bottomExit <= sideExit)
                Consider(bottomExit, new Vec3(0, -1, 0));
            else
            {
                var n = w.Domain.BoundaryNormal(p.XZ);
                Consider(sideExit, new Vec3(n.X, 0, n.Z));
            }
        }

        foreach (var r in w.Props.Rocks)
        {
            double rx = r.SizeX + radius, ry = r.SizeY + radius, rz = r.SizeZ + radius;
            var localXZ = (p.XZ - r.Position).Rotated(-r.RotationY);
            double ly = p.Y - r.Y;
            double q = localXZ.X * localXZ.X / (rx * rx) + ly * ly / (ry * ry) + localXZ.Z * localXZ.Z / (rz * rz);
            if (q >= 1) continue;
            var gn2 = new Vec2(localXZ.X / (rx * rx), localXZ.Z / (rz * rz)).Rotated(r.RotationY);
            var gn = new Vec3(gn2.X, ly / (ry * ry), gn2.Z);
            double d = (1 - Math.Sqrt(Math.Max(0, q))) * Math.Min(rx, Math.Min(ry, rz));
            Consider(d, gn);
        }

        foreach (var l in w.Props.Logs)
        {
            var (a2, b2) = l.Ends;
            var a = new Vec3(a2.X, l.Y, a2.Z);
            var b = new Vec3(b2.X, l.Y, b2.Z);
            var ab = b - a;
            double u = ab.LengthSq > 1e-12 ? MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq) : 0;
            var q = a + ab * u;
            var dvec = p - q;
            double dist = dvec.Length;
            double rr = l.Radius + radius;
            if (dist < rr) Consider(rr - dist, dist > 1e-9 ? dvec / dist : Vec3.Up);
        }

        normal = bestNormal;
        depth = bestDepth;
        return hit;
    }
}
