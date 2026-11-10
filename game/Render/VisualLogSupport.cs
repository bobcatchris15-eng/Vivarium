using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// CPU query against the same generated log surface used by PropRenderer. Mesh-local triangles
/// are cached by identity and geometry signature; position and yaw are applied per query.
/// </summary>
internal static class VisualLogSupport
{
    private const double BinSize = 0.025;
    private const int MaxEntries = 12;
    private const int MaxBinsPerTriangle = 64;
    private static readonly object Gate = new();
    private static readonly Dictionary<LogKey, Entry> Entries = new();
    private static readonly LinkedList<LogKey> Lru = new();

    private readonly record struct LogKey(ulong Id);
    private readonly record struct Signature(ulong Seed, double Length, double Radius, int Decay);
    private readonly record struct Point(float X, float Y, float Z);
    private readonly record struct Triangle(Point A, Point B, Point C);

    private sealed class Entry
    {
        public required Signature Signature;
        public required Triangle[] Triangles;
        public required Dictionary<long, int[]> Bins;
        public required int[] WideTriangles;
        public required LinkedListNode<LogKey> LruNode;
    }

    /// <summary>Returns the highest rendered log-mesh intersection under a world XZ point.</summary>
    public static bool TryTopAt(LogProp log, Vec2 worldXZ, out double worldY)
    {
        var delta = worldXZ - log.Position;
        double reach = log.FootprintRadius + BinSize;
        if (Math.Abs(delta.X) > reach || Math.Abs(delta.Z) > reach)
        {
            worldY = double.NaN;
            return false;
        }
        Entry entry;
        lock (Gate)
        {
            var key = new LogKey(log.Id.Value);
            var signature = new Signature(log.VariantSeed, log.Length, log.Radius, log.DecayClass);
            if (Entries.TryGetValue(key, out var cached) && cached.Signature == signature)
            {
                entry = cached;
                Lru.Remove(entry.LruNode);
                Lru.AddFirst(entry.LruNode);
            }
            else
            {
                if (cached != null) Remove(key, cached);
                entry = Build(log, key, signature);
                Entries[key] = entry;
            }
        }

        // Entry arrays and bin lists are immutable after publication, so dense tile workers
        // traverse them without holding the cache lock and without per-query candidate allocation.
        var axis = log.Axis.Normalized();
        var side = new Vec2(-axis.Z, axis.X);
        double x = delta.Dot(axis);
        double z = delta.Dot(side);
        int bx = (int)Math.Floor(x / BinSize), bz = (int)Math.Floor(z / BinSize);
        double best = double.NegativeInfinity;
        foreach (int index in entry.WideTriangles)
            if (TryHeight(entry.Triangles[index], x, z, out double wideY) && wideY > best) best = wideY;
        if (entry.Bins.TryGetValue(BinKey(bx, bz), out var local))
            foreach (int index in local)
                if (TryHeight(entry.Triangles[index], x, z, out double localY) && localY > best) best = localY;
        if (double.IsNegativeInfinity(best))
        {
            worldY = double.NaN;
            return false;
        }
        worldY = log.Y + best;
        return true;
    }

    private static Entry Build(LogProp log, LogKey key, Signature signature)
    {
        var mesh = PropMeshes.Log(log);
        var triangles = new Triangle[mesh.Indices.Count / 3];
        var bins = new Dictionary<long, List<int>>();
        var wide = new List<int>();
        for (int ti = 0; ti < triangles.Length; ti++)
        {
            var a = ToPoint(mesh.Position(mesh.Indices[ti * 3]));
            var b = ToPoint(mesh.Position(mesh.Indices[ti * 3 + 1]));
            var c = ToPoint(mesh.Position(mesh.Indices[ti * 3 + 2]));
            triangles[ti] = new Triangle(a, b, c);
            double minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            int x0 = (int)Math.Floor(minX / BinSize), x1 = (int)Math.Floor(maxX / BinSize);
            int z0 = (int)Math.Floor(minZ / BinSize), z1 = (int)Math.Floor(maxZ / BinSize);
            long binCount = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            if (binCount <= 0 || binCount > MaxBinsPerTriangle)
            {
                wide.Add(ti);
                continue;
            }
            for (int iz = z0; iz <= z1; iz++)
            for (int ix = x0; ix <= x1; ix++)
            {
                long bin = BinKey(ix, iz);
                if (!bins.TryGetValue(bin, out var list)) bins[bin] = list = new List<int>(8);
                list.Add(ti);
            }
        }
        var compact = new Dictionary<long, int[]>(bins.Count);
        foreach (var pair in bins) compact[pair.Key] = pair.Value.ToArray();
        var node = Lru.AddFirst(key);
        var entry = new Entry
        {
            Signature = signature,
            Triangles = triangles,
            Bins = compact,
            WideTriangles = wide.ToArray(),
            LruNode = node,
        };
        while (Entries.Count >= MaxEntries)
        {
            var last = Lru.Last;
            if (last == null) break;
            if (Entries.TryGetValue(last.Value, out var old)) Remove(last.Value, old);
            else Lru.RemoveLast();
        }
        return entry;
    }

    private static void Remove(LogKey key, Entry entry)
    {
        Entries.Remove(key);
        Lru.Remove(entry.LruNode);
    }

    private static Point ToPoint(Vec3 p) => new((float)p.X, (float)p.Y, (float)p.Z);
    private static long BinKey(int x, int z) => ((long)x << 32) ^ (uint)z;

    private static bool TryHeight(Triangle tri, double x, double z, out double y)
    {
        double ax = tri.A.X, az = tri.A.Z;
        double bx = tri.B.X, bz = tri.B.Z;
        double cx = tri.C.X, cz = tri.C.Z;
        double denominator = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
        if (Math.Abs(denominator) < 1e-12)
        {
            y = 0;
            return false;
        }
        double wa = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / denominator;
        double wb = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / denominator;
        double wc = 1.0 - wa - wb;
        const double epsilon = 1e-7;
        if (wa < -epsilon || wb < -epsilon || wc < -epsilon)
        {
            y = 0;
            return false;
        }
        y = wa * tri.A.Y + wb * tri.B.Y + wc * tri.C.Y;
        return double.IsFinite(y);
    }
}
