using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

/// <summary>Persistent rock instance. Visual form is derived from VariantSeed by the mesh builder; never stored.</summary>
public sealed class Rock
{
    public EntityId Id { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>World Y of the rock's local origin (seated slightly into the terrain).</summary>
    public double Y { get; set; }
    public double RotationY { get; set; }
    /// <summary>Half-extents (m) of the rock's bounding ellipsoid.</summary>
    public double SizeX { get; set; }
    public double SizeY { get; set; }
    public double SizeZ { get; set; }
    public ulong VariantSeed { get; set; }
    public List<string> HabitatTags { get; set; } = new() { "rock" };

    public Vec2 Position => new(X, Z);
    public double FootprintRadius => Math.Max(SizeX, SizeZ);
    /// <summary>Substrate effect inside the footprint.</summary>
    public Substrate SubstrateEffect => Substrate.Rock;

    public bool Covers(Vec2 p)
    {
        double r = FootprintRadius;
        if (Math.Abs(p.X - X) > r || Math.Abs(p.Z - Z) > r) return false;
        // rotate into local frame and test ellipse
        var d = (p - Position).Rotated(-RotationY);
        double ex = d.X / (SizeX * 0.92), ez = d.Z / (SizeZ * 0.92);
        return ex * ex + ez * ez <= 1;
    }

    /// <summary>Approximate faceted rock crown at p, or NaN outside its visual footprint.</summary>
    public double TopAt(Vec2 p)
    {
        double r = FootprintRadius;
        if (Math.Abs(p.X - X) > r || Math.Abs(p.Z - Z) > r) return double.NaN;
        var d = (p - Position).Rotated(-RotationY);
        double ex = d.X / SizeX, ez = d.Z / SizeZ;
        double radius = Math.Sqrt(ex * ex + ez * ez);
        if (radius >= 1) return double.NaN;
        double height = radius < 0.45
            ? MathD.Lerp(0.75, 0.56, radius / 0.45)
            : radius < 0.8
                ? MathD.Lerp(0.56, 0.25, (radius - 0.45) / 0.35)
                : MathD.Lerp(0.25, -0.2, (radius - 0.8) / 0.2);
        return Y + SizeY * height;
    }
}

public sealed class LogProp
{
    public EntityId Id { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    /// <summary>Y of the log's central axis.</summary>
    public double Y { get; set; }
    /// <summary>Heading of the log axis in the XZ plane (radians).</summary>
    public double RotationY { get; set; }
    public double Length { get; set; }
    public double Radius { get; set; }
    /// <summary>0 fresh bark, 1 weathered bark, 2 softening wood, 3 rotting wood.</summary>
    public int DecayClass { get; set; }
    public ulong VariantSeed { get; set; }
    public List<string> HabitatTags { get; set; } = new();

    public Vec2 Position => new(X, Z);
    public Vec2 Axis => Vec2.FromAngle(RotationY);
    public (Vec2 A, Vec2 B) Ends => (Position - Axis * (Length / 2), Position + Axis * (Length / 2));
    public double FootprintRadius => Length / 2 + Radius;

    public double AxisDistance(Vec2 p)
    {
        var (a, b) = Ends; var ab = b - a;
        double t = MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq);
        return Vec2.Distance(p, a + ab * t);
    }

    private bool OutsideBounds(Vec2 p) { double r = FootprintRadius; return Math.Abs(p.X - X) > r || Math.Abs(p.Z - Z) > r; }

    public bool Covers(Vec2 p) => !OutsideBounds(p) && AxisDistance(p) <= Radius * 0.9;

    public double TopAt(Vec2 p)
    {
        if (OutsideBounds(p)) return double.NaN;
        double d = AxisDistance(p);
        return d < Radius ? Y + Math.Sqrt(Radius * Radius - d * d) : double.NaN;
    }

    public static List<string> TagsForDecay(int decayClass) => decayClass switch
    {
        0 => new() { "log", "wood", "bark_fresh" },
        1 => new() { "log", "wood", "bark_weathered" },
        2 => new() { "log", "wood", "wood_soft" },
        _ => new() { "log", "wood", "wood_rotting" },
    };
}

public sealed class GravelPatch
{
    public EntityId Id { get; set; }
    public double X { get; set; }
    public double Z { get; set; }
    public double Radius { get; set; }
    public ulong VariantSeed { get; set; }
    public Vec2 Position => new(X, Z);
    /// <summary>Irregular but deterministic outline: radius varies with angle.</summary>
    public bool Covers(Vec2 p)
    {
        var d = p - Position;
        if (d.LengthSq > Radius * Radius) return false;
        double a = d.Angle;
        ulong ns = Rng.Mix(VariantSeed, 0x47524156454CUL);
        double n = Noise.Value3(ns, Math.Cos(a) * 1.7, Math.Sin(a) * 1.7, 0.37);
        double n2 = Noise.Value3(Rng.Mix(ns, 17), Math.Cos(a) * 3.1, Math.Sin(a) * 3.1, 0.11);
        double r = Radius * (0.86 + 0.11 * (n * 0.5 + 0.5) + 0.04 * n2);
        return d.LengthSq <= r * r;
    }
}

/// <summary>All terrain props. Lists are kept in ascending id order.</summary>
public sealed class PropSet
{
    public List<Rock> Rocks { get; set; } = new();
    public List<LogProp> Logs { get; set; } = new();
    public List<GravelPatch> Gravel { get; set; } = new();

    /// <summary>Change counter for derived caches (light field, renderers). Not authoritative state.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Version { get; private set; }
    public void Touch() => Version++;

    public int Count => Rocks.Count + Logs.Count + Gravel.Count;

    public Rock? RockAt(Vec2 p) { foreach (var r in Rocks) if (r.Covers(p)) return r; return null; }
    public LogProp? LogAt(Vec2 p) { foreach (var l in Logs) if (l.Covers(p)) return l; return null; }
    public GravelPatch? GravelAt(Vec2 p) { foreach (var g in Gravel) if (g.Covers(p)) return g; return null; }

    public object? Find(EntityId id) =>
        (object?)Rocks.FirstOrDefault(r => r.Id == id) ?? (object?)Logs.FirstOrDefault(l => l.Id == id) ?? Gravel.FirstOrDefault(g => g.Id == id);

    /// <summary>Highest prop surface at p (NaN if none).</summary>
    public double PropTopAt(Vec2 p)
    {
        double best = double.NaN;
        foreach (var r in Rocks) { double t = r.TopAt(p); if (!double.IsNaN(t) && !(t <= best)) best = t; }
        foreach (var l in Logs) { double t = l.TopAt(p); if (!double.IsNaN(t) && !(t <= best)) best = t; }
        return best;
    }

    /// <summary>Distance from p to the nearest prop of a feature class ("log", "rock", "gravel").</summary>
    public double DistanceToFeature(Vec2 p, string feature)
    {
        double best = double.PositiveInfinity;
        switch (feature)
        {
            case "rock": foreach (var r in Rocks) best = Math.Min(best, Math.Max(0, Vec2.Distance(p, r.Position) - r.FootprintRadius)); break;
            case "log": foreach (var l in Logs) best = Math.Min(best, Math.Max(0, l.AxisDistance(p) - l.Radius)); break;
            case "gravel": foreach (var g in Gravel) best = Math.Min(best, Math.Max(0, Vec2.Distance(p, g.Position) - g.Radius)); break;
        }
        return best;
    }

    public IEnumerable<string> HabitatTagsAt(Vec2 p)
    {
        foreach (var r in Rocks) if (r.Covers(p)) foreach (var t in r.HabitatTags) yield return t;
        foreach (var l in Logs) if (l.Covers(p)) foreach (var t in l.HabitatTags) yield return t;
        foreach (var g in Gravel) if (g.Covers(p)) yield return "gravel";
    }
}
