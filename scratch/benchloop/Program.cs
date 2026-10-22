using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

// Replicates the guardian vegetation candidate loop to see what the machine
// can actually sustain for that shape of work.
public static class Program
{
    private struct Box { public Vector3 Position; public Vector3 Size; public Vector3 End => Position + Size; }
    private sealed class Plant { public Box Bounds; public Vector3 Root; public Vector3 Target; }

    private static Vector3 Normalize(Vector3 v)
    {
        float len = v.Length();
        return len > 1e-6f ? v / len : Vector3.UnitX;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector3 ResolvePlant(Plant plant, Vector3 point, float radius, bool flexible, bool cachedRoot)
    {
        var box = plant.Bounds;
        var closest = new Vector3(
            Math.Clamp(point.X, box.Position.X, box.End.X),
            Math.Clamp(point.Y, box.Position.Y, box.End.Y),
            Math.Clamp(point.Z, box.Position.Z, box.End.Z));
        var separation = point - closest;
        if (separation.LengthSquared() >= radius * radius) return point;
        var away = box.Position + box.Size * 0.5f - point; away.Y = 0;
        if (away.LengthSquared() < 1e-6f) away = Vector3.UnitX;
        float overlap = radius - separation.Length();
        var requested = Normalize(away) * Math.Min(.38f, overlap * 2.5f);
        if (requested.LengthSquared() > plant.Target.LengthSquared()) plant.Target = requested;
        if (!flexible) return point;
        var root = cachedRoot ? plant.Root : point;
        var lateral = point - root; lateral.Y = 0;
        float stemRadius = Math.Min(.045f, box.Size.X * .10f) + radius;
        if (point.Y < root.Y + box.Size.Y * .65f && lateral.Length() < stemRadius)
        {
            if (lateral.LengthSquared() < 1e-8f) lateral = -Normalize(away);
            point += Normalize(lateral) * (stemRadius - lateral.Length());
        }
        return point;
    }

    public static void Main()
    {
        var c0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < 200_000; i++) _ = Stopwatch.GetTimestamp();
        double tsNs = Stopwatch.GetElapsedTime(c0).TotalMilliseconds * 1e6 / 200_000.0;

        var c1 = Stopwatch.GetTimestamp();
        double acc = 0;
        for (int i = 0; i < 20_000_000; i++) acc += Math.Sqrt(i);
        double loopNs = Stopwatch.GetElapsedTime(c1).TotalMilliseconds * 1e6 / 20_000_000.0;

        const int count = 128;
        var boxes = new Box[count];
        var plants = new Plant[count];
        var rng = new Random(7);
        for (int i = 0; i < count; i++)
        {
            var bb = new Box
            {
                Position = new Vector3((float)(rng.NextDouble() * 0.4 - 0.2), 0f, (float)(rng.NextDouble() * 0.4 - 0.2)),
                Size = new Vector3(1.2f, 2.0f, 1.2f)
            };
            boxes[i] = bb;
            plants[i] = new Plant { Bounds = bb, Root = bb.Position };
        }

        const int iterations = 2_000_000;
        UInt128 mask = UInt128.MaxValue;
        var point = new Vector3(0f, 0.5f, 0f);
        float radius = 0.1f;
        long projected = 0;

        var t0 = Stopwatch.GetTimestamp();
        for (int it = 0; it < iterations; it++)
        {
            UInt128 candidates = mask;
            while (candidates != 0)
            {
                ulong low = (ulong)candidates;
                int index = low != 0 ? BitOperations.TrailingZeroCount(low) : 64 + BitOperations.TrailingZeroCount((ulong)(candidates >> 64));
                candidates &= candidates - 1;
                var flat = boxes[index];
                var closest = new Vector3(
                    Math.Clamp(point.X, flat.Position.X, flat.End.X),
                    Math.Clamp(point.Y, flat.Position.Y, flat.End.Y),
                    Math.Clamp(point.Z, flat.Position.Z, flat.End.Z));
                var separation = point - closest;
                if (separation.LengthSquared() < radius * radius) projected++;
            }
        }
        double a = Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1e6 / (iterations * count);

        var t1 = Stopwatch.GetTimestamp();
        for (int it = 0; it < iterations; it++)
        {
            UInt128 candidates = mask;
            while (candidates != 0)
            {
                ulong low = (ulong)candidates;
                int index = low != 0 ? BitOperations.TrailingZeroCount(low) : 64 + BitOperations.TrailingZeroCount((ulong)(candidates >> 64));
                candidates &= candidates - 1;
                var flat = boxes[index];
                var closest = new Vector3(
                    Math.Clamp(point.X, flat.Position.X, flat.End.X),
                    Math.Clamp(point.Y, flat.Position.Y, flat.End.Y),
                    Math.Clamp(point.Z, flat.Position.Z, flat.End.Z));
                var separation = point - closest;
                var before = point;
                if (separation.LengthSquared() < radius * radius) point = ResolvePlant(plants[index], point, radius, true, true);
                if (point != before) projected++;
            }
        }
        double b = Stopwatch.GetElapsedTime(t1).TotalMilliseconds * 1e6 / (iterations * count);

        Console.WriteLine($"timestamp_ns={tsNs:0.00} sqrt_loop_ns={loopNs:0.00}");
        Console.WriteLine($"A flat-only      ns/candidate = {a:0.00}");
        Console.WriteLine($"B flat + call    ns/candidate = {b:0.00}   (projected {projected})");
    }
}
