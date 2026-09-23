using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tools;

public enum HitKind { None = 0, Terrain = 1, Water = 2, Gravel = 3, Log = 4, Rock = 5, Flora = 6, Fauna = 7 }

public readonly record struct WorldHit(HitKind Kind, EntityId Id, Vec3 Point, double Distance)
{
    public static readonly WorldHit None = new(HitKind.None, EntityId.None, Vec3.Zero, double.PositiveInfinity);
    public bool IsHit => Kind != HitKind.None;
}

/// <summary>
/// Unified picking against authoritative geometry (never against render objects).
/// Resolution rule: every candidate farther than the first opaque surface (terrain, rock, log) + 2 cm is
/// discarded; among the rest the highest priority wins — Fauna &gt; Flora &gt; Rock &gt; Log &gt; Gravel &gt; Water &gt; Terrain —
/// ties broken by distance. Small organisms get generous pick radii so they are selectable at a distance.
/// </summary>
public static class Selection
{
    public const double MaxDistance = 80;

    public static WorldHit Raycast(VivariumWorld w, Vec3 origin, Vec3 direction, bool includeWater = false)
    {
        var dir = direction.Normalized();
        if (dir.LengthSq < 0.5) return WorldHit.None;
        var hits = new List<WorldHit>(8);

        double tTerrain = RayTerrain(w, origin, dir);
        if (!double.IsInfinity(tTerrain))
        {
            var p = origin + dir * tTerrain;
            var g = w.Props.GravelAt(p.XZ);
            hits.Add(g != null ? new WorldHit(HitKind.Gravel, g.Id, p, tTerrain) : new WorldHit(HitKind.Terrain, EntityId.None, p, tTerrain));
        }
        foreach (var r in w.Props.Rocks)
        {
            double t = RayEllipsoid(origin, dir, new Vec3(r.X, r.Y, r.Z), r.SizeX, r.SizeY, r.SizeZ, r.RotationY);
            if (!double.IsInfinity(t)) hits.Add(new WorldHit(HitKind.Rock, r.Id, origin + dir * t, t));
        }
        foreach (var l in w.Props.Logs)
        {
            double t = RayCapsule(origin, dir, l);
            if (!double.IsInfinity(t)) hits.Add(new WorldHit(HitKind.Log, l.Id, origin + dir * t, t));
        }
        if (includeWater)
        {
            double t = RayWater(w, origin, dir, Math.Min(tTerrain, MaxDistance));
            if (!double.IsInfinity(t)) hits.Add(new WorldHit(HitKind.Water, EntityId.None, origin + dir * t, t));
        }
        foreach (var f in w.Flora.Items)
        {
            var sp = w.Content.FloraOrThrow(f.SpeciesId);
            double r = Math.Max(f.Radius(sp) * 0.7, 0.06);
            var c = new Vec3(f.X, w.GroundHeight(f.Position) + Math.Min(sp.Height * 0.5, r), f.Z);
            double t = RaySphere(origin, dir, c, r);
            if (!double.IsInfinity(t)) hits.Add(new WorldHit(HitKind.Flora, f.Id, origin + dir * t, t));
        }
        foreach (var a in w.Fauna.Items)
        {
            var sp = w.Content.FaunaOrThrow(a.SpeciesId);
            double visual = w.FaunaSystem.PhenotypeOf(a).BodySize * sp.VisualScale;
            // pick radius grows gently with distance so tiny animals stay clickable
            double dist = (a.Position - origin).Length;
            double r = Math.Max(visual * 0.7, 0.012 + dist * 0.004);
            double t = RaySphere(origin, dir, a.Position + new Vec3(0, visual * 0.3, 0), r);
            if (!double.IsInfinity(t)) hits.Add(new WorldHit(HitKind.Fauna, a.Id, origin + dir * t, t));
        }
        return Resolve(hits);
    }

    public static WorldHit Resolve(List<WorldHit> hits)
    {
        if (hits.Count == 0) return WorldHit.None;
        double opaque = double.PositiveInfinity;
        foreach (var h in hits) if (h.Kind is HitKind.Terrain or HitKind.Gravel or HitKind.Rock or HitKind.Log) opaque = Math.Min(opaque, h.Distance);
        WorldHit best = WorldHit.None;
        foreach (var h in hits)
        {
            if (h.Distance > opaque + 0.02) continue;
            if (!best.IsHit || Priority(h.Kind) > Priority(best.Kind) || (Priority(h.Kind) == Priority(best.Kind) && h.Distance < best.Distance)) best = h;
        }
        return best;
    }

    public static int Priority(HitKind k) => k switch
    {
        HitKind.Fauna => 7, HitKind.Flora => 6, HitKind.Rock => 5, HitKind.Log => 4, HitKind.Gravel => 3, HitKind.Water => 2, HitKind.Terrain => 1, _ => 0,
    };

    /// <summary>Ray vs terrain top surface inside the hexagon (march + bisection). Returns distance or +∞.</summary>
    public static double RayTerrain(VivariumWorld w, Vec3 o, Vec3 d)
    {
        double step = 0.04, t = 0, prevT = 0;
        double prev = o.Y - w.Terrain.Height(o.XZ);
        // skip ahead quickly when far above the terrain
        while (t < MaxDistance)
        {
            var p = o + d * t;
            double above = p.Y - (w.Domain.Contains(p.XZ) ? w.Terrain.Height(p.XZ) : double.NegativeInfinity);
            if (above <= 0 && w.Domain.Contains(p.XZ))
            {
                if (prev <= 0 && t == 0) return 0; // starting below ground
                double lo = prevT, hi = t;
                for (int i = 0; i < 24; i++)
                {
                    double mid = (lo + hi) / 2;
                    var q = o + d * mid;
                    if (q.Y - w.Terrain.Height(q.XZ) > 0) lo = mid; else hi = mid;
                }
                return hi;
            }
            prev = above; prevT = t;
            t += double.IsInfinity(above) ? step * 4 : Math.Max(step, Math.Min(above * 0.5, 1.0));
        }
        return double.PositiveInfinity;
    }

    public static double RayWater(VivariumWorld w, Vec3 o, Vec3 d, double maxT)
    {
        double step = 0.03;
        for (double t = 0; t < maxT; t += step)
        {
            var p = o + d * t;
            double s = w.Water.SurfaceAt(p.XZ);
            if (!double.IsNaN(s) && Math.Abs(p.Y - s) < step * Math.Max(Math.Abs(d.Y), 0.2) + 0.005) return t;
        }
        return double.PositiveInfinity;
    }

    public static double RaySphere(Vec3 o, Vec3 d, Vec3 c, double r)
    {
        var oc = o - c;
        double b = oc.Dot(d), cc = oc.LengthSq - r * r;
        double disc = b * b - cc;
        if (disc < 0) return double.PositiveInfinity;
        double s = Math.Sqrt(disc);
        double t = -b - s;
        if (t < 0) t = -b + s;
        return t >= 0 && t <= MaxDistance ? t : double.PositiveInfinity;
    }

    public static double RayEllipsoid(Vec3 o, Vec3 d, Vec3 c, double rx, double ry, double rz, double rotY)
    {
        // into local frame (rotate by -rotY about Y), scale to unit sphere
        double cs = Math.Cos(-rotY), sn = Math.Sin(-rotY);
        Vec3 Local(Vec3 v) => new((v.X * cs - v.Z * sn) / rx, v.Y / ry, (v.X * sn + v.Z * cs) / rz);
        var lo = Local(o - c); var ld = Local(d);
        double a = ld.LengthSq, b = lo.Dot(ld), cc = lo.LengthSq - 1;
        double disc = b * b - a * cc;
        if (disc < 0) return double.PositiveInfinity;
        double s = Math.Sqrt(disc);
        double t = (-b - s) / a;
        if (t < 0) t = (-b + s) / a;
        return t >= 0 && t <= MaxDistance ? t : double.PositiveInfinity;
    }

    public static double RayCapsule(Vec3 o, Vec3 d, LogProp l)
    {
        var (a2, b2) = l.Ends;
        var a = new Vec3(a2.X, l.Y, a2.Z); var b = new Vec3(b2.X, l.Y, b2.Z);
        // sample along the ray: robust and cheap enough for a handful of logs
        double best = double.PositiveInfinity;
        double tc = (Vec3.Lerp(a, b, 0.5) - o).Dot(d);
        double span = l.Length / 2 + l.Radius + 0.1;
        for (double t = Math.Max(0, tc - span); t <= tc + span; t += 0.01)
        {
            var p = o + d * t;
            var ab = b - a;
            double u = MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq);
            if ((p - (a + ab * u)).Length <= l.Radius) { best = t; break; }
        }
        return best;
    }
}
