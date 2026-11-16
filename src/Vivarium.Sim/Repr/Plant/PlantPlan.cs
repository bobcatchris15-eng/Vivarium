using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>
/// Deterministic vector helpers for plant design. No Godot types, no System.Random.
/// </summary>
public static class PlantMath
{
    /// <summary>Smoothstep on [0,1].</summary>
    public static double Smooth(double m)
    {
        m = Math.Clamp(m, 0, 1);
        return m * m * (3 - 2 * m);
    }

    /// <summary>Inverse of <see cref="Smooth"/>: the m in [0,1] with Smooth(m) = x.</summary>
    public static double InvSmooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        double lo = 0, hi = 1;
        for (int i = 0; i < 48; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Smooth(mid) < x) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Cubic Hermite position at s (endpoints p0 at s=0, p1 at s=1, tangents t0/t1 scaled by length).</summary>
    public static Vec3 Hermite(Vec3 p0, Vec3 t0, Vec3 p1, Vec3 t1, double s)
    {
        double s2 = s * s, s3 = s2 * s;
        double h00 = 2 * s3 - 3 * s2 + 1, h10 = s3 - 2 * s2 + s, h01 = -2 * s3 + 3 * s2, h11 = s3 - s2;
        return p0 * h00 + t0 * h10 + p1 * h01 + t1 * h11;
    }

    /// <summary>Derivative of <see cref="Hermite"/> with respect to s.</summary>
    public static Vec3 HermiteD(Vec3 p0, Vec3 t0, Vec3 p1, Vec3 t1, double s)
    {
        double s2 = s * s;
        double d00 = 6 * s2 - 6 * s, d10 = 3 * s2 - 4 * s + 1, d01 = -6 * s2 + 6 * s, d11 = 3 * s2 - 2 * s;
        return p0 * d00 + t0 * d10 + p1 * d01 + t1 * d11;
    }

    public static Vec3 SafeNormal(Vec3 v, Vec3 fallback) => v.LengthSq > 1e-20 ? v.Normalized() : fallback;

    /// <summary>A unit vector perpendicular to d.</summary>
    public static Vec3 Perpendicular(Vec3 d)
    {
        var c = Math.Abs(d.Y) < 0.9 ? Vec3.Up : Vec3.Right;
        return SafeNormal(d.Cross(c), Vec3.Forward);
    }

    /// <summary>Unit horizontal direction at azimuth az (radians about +Y).</summary>
    public static Vec3 Horizontal(double az) => new(Math.Cos(az), 0, Math.Sin(az));

    /// <summary>Rodrigues rotation of v about a unit axis.</summary>
    public static Vec3 Rotate(Vec3 v, Vec3 axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + axis.Cross(v) * s + axis * (axis.Dot(v) * (1 - c));
    }

    /// <summary>Distance from p to the segment ab.</summary>
    public static double SegmentDistance(Vec3 p, Vec3 a, Vec3 b)
    {
        var ab = b - a;
        double l2 = ab.LengthSq;
        double t = l2 > 1e-20 ? Math.Clamp((p - a).Dot(ab) / l2, 0, 1) : 0;
        return (p - (a + ab * t)).Length;
    }
}

/// <summary>
/// One potential organ of a species plan, at FULL design size. Its geometry never changes for the life of
/// the plant; growth only exposes a prefix of it. Axis organs: cubic Hermite (P0,T0)->(P1,T1) in design
/// parameter s in [0,1]; R0/R1 are radius at s=0 and s=1. Flat organs (leaf, leaflet, flower, fruit):
/// P0 attachment, T0 = T1 = unit outward direction, P1 = P0 + T0 * length; R0 = half width, R1 = thickness.
/// </summary>
public sealed class PotOrgan
{
    public int Gen, Parent = -1;
    public OrganType Type;
    public Tissue Tissue;
    public OrganFlags Flags;
    public ushort ShapeId;
    public ushort Order;
    public double AttachT;            // design parameter on the parent axis
    public Vec3 P0, T0, P1, T1, Up;
    public double R0, R1;
    public double Emerge, Dur;        // ages at which the organ starts / reaches Maturity 1 (Dur = ramp length)
    public uint Seed;

    public bool IsFlat => Type is OrganType.Leaf or OrganType.Leaflet or OrganType.Flower or OrganType.Fruit;
    public bool IsBlade => Type is OrganType.Leaf or OrganType.Leaflet;
    public bool IsAxis => Type is OrganType.Axis or OrganType.Petiole;
    public PotOrgan Clone() => (PotOrgan)MemberwiseClone();
}

/// <summary>Full species design for one plant: potential organs in canonical order (emergence, then generation).</summary>
public sealed class PlantPlan
{
    public string SpeciesId = "";
    public ulong WorldSeed, PlantId;
    public OrganShape[] Shapes = Array.Empty<OrganShape>();
    public List<PotOrgan> Items = new();
}

/// <summary>
/// Builds a species design. Every choice is drawn from ReprId.Hash(worldSeed, plantId, drawIndex) or from
/// the organ's own generation index, so the design is a pure function of (seed, plantId, species).
/// Children are attached to the SURFACE of their parent: Start = parentCurve(AttachT) + outward * parentRadius.
/// </summary>
public sealed class PlanBuilder
{
    public readonly ulong WorldSeed, PlantId;
    private readonly List<PotOrgan> _organs = new();
    private readonly List<OrganShape> _shapes = new();
    private ulong _draws;

    public PlanBuilder(ulong worldSeed, ulong plantId)
    {
        WorldSeed = worldSeed;
        PlantId = plantId;
    }

    /// <summary>Design-level uniform draw in [0,1). Sequence is fixed by generation order.</summary>
    public double Draw() => ReprId.Unit(ReprId.Hash(WorldSeed, PlantId, 0x100000000UL + _draws++), 0x5A17);
    public double Range(double lo, double hi) => lo + (hi - lo) * Draw();

    public ushort AddShape(OrganShape s)
    {
        _shapes.Add(s);
        return (ushort)(_shapes.Count - 1);
    }

    public PotOrgan this[int gen] => _organs[gen];

    /// <summary>Design tangent of an axis at parameter t (unit).</summary>
    public Vec3 TangentAt(int gen, double t)
    {
        var o = _organs[gen];
        return PlantMath.SafeNormal(PlantMath.HermiteD(o.P0, o.T0, o.P1, o.T1, t), Vec3.Up);
    }

    /// <summary>Surface point of parent at t, offset outward towards the direction `want`.</summary>
    private (Vec3 P, Vec3 Outward, double R) Attach(int parent, double t, Vec3 want)
    {
        var o = _organs[parent];
        var c = PlantMath.Hermite(o.P0, o.T0, o.P1, o.T1, t);
        var tan = TangentAt(parent, t);
        double r = o.R0 + (o.R1 - o.R0) * t;
        var outward = PlantMath.SafeNormal(want - tan * want.Dot(tan), PlantMath.Perpendicular(tan));
        return (c + outward * r, outward, r);
    }

    private int Add(PotOrgan p, int parent, double emergeBase)
    {
        p.Gen = _organs.Count;
        p.Parent = parent;
        double gate = 0;
        if (parent >= 0)
        {
            var q = _organs[parent];
            // Child may only appear once the parent has grown past the attachment point.
            gate = q.Emerge + q.Dur * PlantMath.InvSmooth(p.AttachT) + 0.003;
        }
        p.Emerge = Math.Max(emergeBase, gate);
        p.Seed = (uint)(ReprId.Hash(WorldSeed, PlantId, (ulong)p.Gen + 1) >> 32);
        _organs.Add(p);
        return p.Gen;
    }

    /// <summary>
    /// Adds an axis (stem, branch, petiole, pedicel, thorn). parent = -1 places a root axis at the origin.
    /// Child radii are clamped to 0.85 of the parent radius at the attachment.
    /// </summary>
    public int AddAxis(int parent, double t, Vec3 dir, double len, Vec3 bend, double r0, double r1, ushort order,
        Tissue tissue, double emergeBase, double dur, OrganFlags flags = OrganFlags.None, OrganType type = OrganType.Axis)
    {
        Vec3 p0 = Vec3.Zero, outward = PlantMath.Perpendicular(dir);
        double rb = r0, rParent = r0;
        double tAttach = 0;
        if (parent >= 0)
        {
            var a = Attach(parent, t, dir);
            p0 = a.P;
            outward = a.Outward;
            rParent = a.R;
            tAttach = t;
            // Rhizomes/mycelia can feed a much thicker new basal shoot. Lateral
            // woody branches retain their ordinary parent-radius constraint.
            rb = (flags & OrganFlags.Basal) != 0 ? r0 : Math.Min(r0, 0.85 * rParent);
        }
        dir = PlantMath.SafeNormal(dir, Vec3.Up);
        var t0 = dir * len;
        var t1 = PlantMath.SafeNormal(dir + bend, dir) * len;
        var p = new PotOrgan
        {
            Type = type, Tissue = tissue, Flags = flags, Order = order, AttachT = tAttach,
            P0 = p0, T0 = t0, T1 = t1, P1 = p0 + (t0 + t1) * 0.5,
            Up = outward, R0 = rb, R1 = Math.Min(r1, rb), Dur = dur,
        };
        return Add(p, parent, emergeBase);
    }

    /// <summary>Adds a flat organ (leaf, leaflet, flower, fruit) attached to an axis at t, pointing along dir.</summary>
    public int AddFlat(int parent, double t, Vec3 dir, double len, double halfWidth, double thickness, OrganType type,
        ushort shape, ushort order, double emergeBase, double dur, OrganFlags flags = OrganFlags.None)
    {
        if (parent < 0) throw new ArgumentException("flat organs need a parent axis");
        var a = Attach(parent, t, dir);
        dir = PlantMath.SafeNormal(dir, Vec3.Up);
        var up = PlantMath.SafeNormal(Vec3.Up - dir * Vec3.Up.Dot(dir), PlantMath.Perpendicular(dir));
        var tissue = type == OrganType.Flower ? Tissue.Petal : type == OrganType.Fruit ? Tissue.FruitSkin : Tissue.Lamina;
        var p = new PotOrgan
        {
            Type = type, Tissue = tissue, Flags = flags, ShapeId = shape, Order = order, AttachT = t,
            P0 = a.P, T0 = dir, T1 = dir, P1 = a.P + dir * len, Up = up,
            R0 = halfWidth, R1 = thickness, Dur = dur,
        };
        return Add(p, parent, emergeBase);
    }

    /// <summary>
    /// Freezes the design: canonical ordering, maturation normalized to age one, and
    /// growth-time accommodation of blades (yaw search, wood > petioles > blades priority).
    /// </summary>
    public PlantPlan Finish(string speciesId)
    {
        int n = _organs.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => _organs[i].Emerge).ThenBy(i => i).ToArray();
        var newIndex = new int[n];
        for (int k = 0; k < n; k++) newIndex[order[k]] = k;
        var items = new List<PotOrgan>(n);
        double growthWindow = n == 0 ? 1 : Math.Max(1, _organs.Max(o => o.Emerge + o.Dur));
        foreach (int i in order)
        {
            var p = _organs[i];
            var c = p.Clone();
            c.Emerge /= growthWindow;
            c.Dur /= growthWindow;
            c.Parent = p.Parent < 0 ? -1 : newIndex[p.Parent];
            c.Gen = items.Count;
            items.Add(c);
        }
        Accommodate(items);
        return new PlantPlan
        {
            SpeciesId = speciesId, WorldSeed = WorldSeed, PlantId = PlantId,
            Shapes = _shapes.ToArray(), Items = items,
        };
    }

    private static readonly double[] YawCandidatesDeg = { 0, 15, -15, 30, -30, 45, -45 };

    /// <summary>
    /// Blades (in canonical order) choose the first yaw about world up whose midline clears all earlier wood,
    /// petioles and placed blades. If none is clear, the least-penetrating yaw is kept. Uses the full-size design,
    /// so the result does not depend on growth steps.
    /// </summary>
    private static void Accommodate(List<PotOrgan> items)
    {
        int segmentCapacity = 0;
        foreach (var item in items)
            segmentCapacity += item.IsAxis ? 4 : item.IsBlade ? 2 : 0;
        var segs = new List<(Vec3 A, Vec3 B, double R, int Owner, Vec3 Min, Vec3 Max)>(segmentCapacity);
        const double cellSize = 0.15;
        var grid = new Dictionary<(int X, int Y, int Z), List<int>>();
        var largeSegments = new List<int>();
        var candidates = new List<int>();
        var visited = new int[segmentCapacity];
        int stamp = 0;
        static int Cell(double x) => (int)Math.Floor(x / cellSize);
        void AddSegment(Vec3 a, Vec3 b, double r, int owner)
        {
            segs.Add((a, b, r, owner,
                new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
                new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z))));
            int index = segs.Count - 1;
            // Inflate by the segment radius; the query adds the blade clearance.
            // A small pad protects boundary rounding. Oversized ranges fall back to
            // a global list instead of filling an unbounded number of cells.
            double pad = Math.Max(0, r) + 1e-12;
            int x0 = Cell(Math.Min(a.X, b.X) - pad), x1 = Cell(Math.Max(a.X, b.X) + pad);
            int y0 = Cell(Math.Min(a.Y, b.Y) - pad), y1 = Cell(Math.Max(a.Y, b.Y) + pad);
            int z0 = Cell(Math.Min(a.Z, b.Z) - pad), z1 = Cell(Math.Max(a.Z, b.Z) + pad);
            long cells = ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) * ((long)z1 - z0 + 1);
            if (cells <= 0 || cells > 4096) { largeSegments.Add(index); return; }
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                    {
                        var key = (x, y, z);
                        if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = new List<int>();
                        bucket.Add(index);
                    }
        }
        void Query(Vec3 pt, double clearance)
        {
            candidates.Clear();
            if (++stamp == int.MaxValue) { Array.Clear(visited); stamp = 1; }
            void Include(int index)
            {
                if (visited[index] == stamp) return;
                visited[index] = stamp;
                candidates.Add(index);
            }
            double pad = Math.Max(0, clearance) + 1e-12;
            int x0 = Cell(pt.X - pad), x1 = Cell(pt.X + pad);
            int y0 = Cell(pt.Y - pad), y1 = Cell(pt.Y + pad);
            int z0 = Cell(pt.Z - pad), z1 = Cell(pt.Z + pad);
            long cells = ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) * ((long)z1 - z0 + 1);
            if (cells <= 0 || cells > 4096)
            {
                for (int j = 0; j < segs.Count; j++) candidates.Add(j);
                return;
            }
            foreach (int index in largeSegments) Include(index);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    for (int z = z0; z <= z1; z++)
                        if (grid.TryGetValue((x, y, z), out var bucket))
                            foreach (int index in bucket) Include(index);
            candidates.Sort(); // Preserve the original floating-point penalty accumulation order.
        }
        for (int i = 0; i < items.Count; i++)
        {
            var o = items[i];
            if (!o.IsAxis) continue;
            for (int k = 0; k < 4; k++)
            {
                double s0 = k / 4.0, s1 = (k + 1) / 4.0;
                var a = PlantMath.Hermite(o.P0, o.T0, o.P1, o.T1, s0);
                var b = PlantMath.Hermite(o.P0, o.T0, o.P1, o.T1, s1);
                AddSegment(a, b, o.R0 + (o.R1 - o.R0) * (s0 + s1) * 0.5, i);
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            var o = items[i];
            if (!o.IsBlade) continue;
            var v = o.P1 - o.P0;
            double len = v.Length;
            if (len < 1e-9) continue;
            var dir0 = v / len;
            double clearance = 0.5 * o.R0 + 0.002;
            double bestPen = double.MaxValue, bestYaw = 0;
            foreach (double deg in YawCandidatesDeg)
            {
                double yaw = deg * Math.PI / 180.0;
                var d = PlantMath.Rotate(dir0, Vec3.Up, yaw);
                double pen = 0;
                for (int q = 0; q < 5; q++)
                {
                    var pt = o.P0 + d * (len * (0.3 + 0.15 * q));
                    Query(pt, clearance);
                    foreach (int segmentIndex in candidates)
                    {
                        var sg = segs[segmentIndex];
                        if (sg.Owner == o.Parent) continue;
                        double thr = sg.R + clearance;
                        // A conservative box rejection avoids the projection and square root for distant
                        // segments. Keep the original order and exact distance calculation for hits.
                        double margin = Math.Max(0, thr) + 1e-12;
                        if (pt.X < sg.Min.X - margin || pt.X > sg.Max.X + margin ||
                            pt.Y < sg.Min.Y - margin || pt.Y > sg.Max.Y + margin ||
                            pt.Z < sg.Min.Z - margin || pt.Z > sg.Max.Z + margin) continue;
                        double dist = PlantMath.SegmentDistance(pt, sg.A, sg.B);
                        if (dist < thr) pen += thr - dist;
                    }
                }
                if (pen < bestPen - 1e-12) { bestPen = pen; bestYaw = yaw; }
                if (pen <= 1e-12) break;
            }
            var dn = PlantMath.Rotate(dir0, Vec3.Up, bestYaw);
            o.P1 = o.P0 + dn * len;
            o.T0 = dn;
            o.T1 = dn;
            o.Up = PlantMath.Rotate(o.Up, Vec3.Up, bestYaw);
            var mid = o.P0 + dn * (len * 0.5);
            AddSegment(o.P0, mid, 0.5 * o.R0, i);
            AddSegment(mid, o.P1, 0.5 * o.R0, i);
        }
    }
}
