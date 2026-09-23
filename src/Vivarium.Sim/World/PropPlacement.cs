using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

public readonly record struct PlacementResult(bool Ok, string Message, EntityId Id)
{
    public static PlacementResult Fail(string msg) => new(false, msg, EntityId.None);
    public static PlacementResult Success(EntityId id, string msg = "placed") => new(true, msg, id);
}

/// <summary>
/// Authoritative prop placement: seeded generation for new worlds and the runtime API used by player tools.
/// Every mutation validates bounds and spacing and bumps <see cref="PropSet.Version"/> so derived
/// queries (light, substrate, rendering) update immediately.
/// </summary>
public sealed class PropPlacement
{
    private readonly VivariumWorld _w;
    public PropPlacement(VivariumWorld world) { _w = world; }

    private PropSet Props => _w.Props;
    private HexDomain Domain => _w.Domain;

    // ------------------------------------------------------------------ validation

    public string? ValidateRock(Vec2 p, double sizeX, double sizeZ, EntityId ignore = default)
    {
        double r = Math.Max(sizeX, sizeZ);
        if (!p.IsFinite) return "invalid position";
        if (!Domain.ContainsDisc(p, r * 0.9)) return "rock would extend past the island edge";
        return OverlapCheck(p, r, ignore, gravelBlocks: false);
    }

    public string? ValidateLog(Vec2 p, double heading, double length, double radius, EntityId ignore = default)
    {
        if (!p.IsFinite || !double.IsFinite(heading)) return "invalid position";
        var axis = Vec2.FromAngle(heading);
        var a = p - axis * (length / 2); var b = p + axis * (length / 2);
        if (!Domain.ContainsDisc(a, radius) || !Domain.ContainsDisc(b, radius)) return "log would extend past the island edge";
        foreach (var rk in Props.Rocks)
        {
            if (rk.Id == ignore) continue;
            double d = SegmentDistance(rk.Position, a, b);
            if (d < rk.FootprintRadius + radius + _w.Content.Tools.PropMinSpacing * 0.5) return "too close to a rock";
        }
        foreach (var lg in Props.Logs)
        {
            if (lg.Id == ignore) continue;
            var (c, e) = lg.Ends;
            double d = Math.Min(Math.Min(SegmentDistance(c, a, b), SegmentDistance(e, a, b)), Math.Min(SegmentDistance(a, c, e), SegmentDistance(b, c, e)));
            if (d < lg.Radius + radius + _w.Content.Tools.PropMinSpacing * 0.5) return "too close to another log";
        }
        return null;
    }

    public string? ValidateGravel(Vec2 p, double radius)
    {
        if (!p.IsFinite) return "invalid position";
        if (!Domain.ContainsDisc(p, radius * 0.6)) return "gravel patch centre too close to the island edge";
        return null;
    }

    private string? OverlapCheck(Vec2 p, double r, EntityId ignore, bool gravelBlocks)
    {
        double spacing = _w.Content.Tools.PropMinSpacing * 0.5;
        foreach (var rk in Props.Rocks)
            if (rk.Id != ignore && Vec2.Distance(p, rk.Position) < r + rk.FootprintRadius * 0.8 + spacing * 0.2) return "overlaps another rock";
        foreach (var lg in Props.Logs)
            if (lg.Id != ignore && lg.AxisDistance(p) < r + lg.Radius + spacing * 0.2) return "overlaps a log";
        if (gravelBlocks)
            foreach (var g in Props.Gravel)
                if (Vec2.Distance(p, g.Position) < r + g.Radius) return "overlaps gravel";
        return null;
    }

    private static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a; double t = ab.LengthSq > 0 ? MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq) : 0;
        return Vec2.Distance(p, a + ab * t);
    }

    // ------------------------------------------------------------------ runtime API

    public PlacementResult PlaceRock(Vec2 p, double scale, double rotationY, ulong variantSeed)
    {
        var shape = Rng.Keyed(variantSeed, "rock.shape", 0);
        double sx = scale * shape.Range(0.8, 1.2), sz = scale * shape.Range(0.7, 1.1), sy = scale * shape.Range(0.45, 0.8);
        var err = ValidateRock(p, sx, sz);
        if (err != null) return PlacementResult.Fail(err);
        var rock = new Rock
        {
            Id = _w.Ids.Next(EntityKind.Rock), X = p.X, Z = p.Z, RotationY = rotationY,
            SizeX = sx, SizeY = sy, SizeZ = sz, VariantSeed = variantSeed,
        };
        rock.Y = _w.Terrain.Height(p) - sy * 0.25;
        Props.Rocks.Add(rock);
        Changed($"rock {rock.Id} at {p}");
        return PlacementResult.Success(rock.Id);
    }

    public PlacementResult PlaceLog(Vec2 p, double heading, double length, double radius, int decayClass, ulong variantSeed)
    {
        var err = ValidateLog(p, heading, length, radius);
        if (err != null) return PlacementResult.Fail(err);
        var log = new LogProp
        {
            Id = _w.Ids.Next(EntityKind.Log), X = p.X, Z = p.Z, RotationY = heading, Length = length, Radius = radius,
            DecayClass = Math.Clamp(decayClass, 0, 3), VariantSeed = variantSeed, HabitatTags = LogProp.TagsForDecay(decayClass),
        };
        log.Y = SeatLog(p, heading, length, radius);
        Props.Logs.Add(log);
        Changed($"log {log.Id} at {p}");
        return PlacementResult.Success(log.Id);
    }

    public PlacementResult PlaceGravel(Vec2 p, double radius, ulong variantSeed)
    {
        var err = ValidateGravel(p, radius);
        if (err != null) return PlacementResult.Fail(err);
        var g = new GravelPatch { Id = _w.Ids.Next(EntityKind.Gravel), X = p.X, Z = p.Z, Radius = radius, VariantSeed = variantSeed };
        Props.Gravel.Add(g);
        Changed($"gravel {g.Id} at {p}");
        return PlacementResult.Success(g.Id);
    }

    public PlacementResult Move(EntityId id, Vec2 p)
    {
        switch (Props.Find(id))
        {
            case Rock r:
            {
                var err = ValidateRock(p, r.SizeX, r.SizeZ, id);
                if (err != null) return PlacementResult.Fail(err);
                r.X = p.X; r.Z = p.Z; r.Y = _w.Terrain.Height(p) - r.SizeY * 0.25;
                break;
            }
            case LogProp l:
            {
                var err = ValidateLog(p, l.RotationY, l.Length, l.Radius, id);
                if (err != null) return PlacementResult.Fail(err);
                l.X = p.X; l.Z = p.Z; l.Y = SeatLog(p, l.RotationY, l.Length, l.Radius);
                break;
            }
            case GravelPatch g:
            {
                var err = ValidateGravel(p, g.Radius);
                if (err != null) return PlacementResult.Fail(err);
                g.X = p.X; g.Z = p.Z;
                break;
            }
            default: return PlacementResult.Fail($"no prop with id {id}");
        }
        Changed($"moved {id} to {p}");
        return PlacementResult.Success(id, "moved");
    }

    public PlacementResult Remove(EntityId id)
    {
        bool removed = Props.Rocks.RemoveAll(r => r.Id == id) + Props.Logs.RemoveAll(l => l.Id == id) + Props.Gravel.RemoveAll(g => g.Id == id) > 0;
        if (!removed) return PlacementResult.Fail($"no prop with id {id}");
        Changed($"removed {id}");
        return PlacementResult.Success(id, "removed");
    }

    /// <summary>Re-seats rocks and logs whose footprint touches a sculpted disc. Returns true if any moved.</summary>
    public bool Reseat(Vec2 centre, double radius)
    {
        bool any = false;
        foreach (var r in Props.Rocks)
            if (Vec2.Distance(r.Position, centre) <= radius + r.FootprintRadius) { r.Y = _w.Terrain.Height(r.Position) - r.SizeY * 0.25; any = true; }
        foreach (var l in Props.Logs)
            if (Vec2.Distance(l.Position, centre) <= radius + l.FootprintRadius) { l.Y = SeatLog(l.Position, l.RotationY, l.Length, l.Radius); any = true; }
        if (any) Props.Touch();
        return any;
    }

    private double SeatLog(Vec2 p, double heading, double length, double radius)
    {
        var axis = Vec2.FromAngle(heading);
        double h = double.NegativeInfinity;
        for (int k = -2; k <= 2; k++) h = Math.Max(h, _w.Terrain.Height(p + axis * (length * k / 4.5)));
        return h + radius * 0.55;
    }

    private void Changed(string what)
    {
        Props.Touch();
        _w.OnPropsChanged();
        Log.Debug(LogCategory.World, what);
    }

    // ------------------------------------------------------------------ seeded generation

    /// <summary>Generates rocks, logs and gravel from the world seed and placement profile. Same seed ⇒ identical props.</summary>
    public void Generate(WorldDescriptor d)
    {
        var rng = Rng.Stream(d.Seed, "world.props");
        var pp = d.Placement;
        double wt = d.Water.WaterTable;
        bool Dry(Vec2 p) => _w.Terrain.Height(p) > wt + 0.03;

        for (int n = 0, attempts = 0; n < pp.Logs && attempts < pp.Logs * 60; attempts++)
        {
            var p = RandomPoint(rng, 1.2);
            double heading = rng.Range(0, Math.PI);
            double len = rng.Range(pp.LogMinLength, pp.LogMaxLength), rad = rng.Range(pp.LogMinRadius, pp.LogMaxRadius);
            int decay = rng.NextInt(4);
            ulong seed = rng.NextULong();
            if (!Dry(p)) continue;
            if (ValidateLog(p, heading, len, rad) != null || !SpacingOk(p, len / 2 + rad, pp.Spacing)) continue;
            if (PlaceLog(p, heading, len, rad, decay, seed).Ok) n++;
        }
        for (int n = 0, attempts = 0; n < pp.Rocks && attempts < pp.Rocks * 60; attempts++)
        {
            var p = RandomPoint(rng, 0.5);
            double scale = rng.Range(pp.RockMinScale, pp.RockMaxScale);
            double rot = rng.Range(0, 2 * Math.PI);
            ulong seed = rng.NextULong();
            if (!SpacingOk(p, scale * 1.2, pp.Spacing)) continue;
            if (PlaceRock(p, scale, rot, seed).Ok) n++;
        }
        for (int n = 0, attempts = 0; n < pp.GravelPatches && attempts < pp.GravelPatches * 60; attempts++)
        {
            var p = RandomPoint(rng, 0.6);
            double r = rng.Range(pp.GravelMinRadius, pp.GravelMaxRadius);
            ulong seed = rng.NextULong();
            if (!Dry(p)) continue;
            bool clash = Props.Gravel.Any(g => Vec2.Distance(g.Position, p) < g.Radius + r);
            if (clash) continue;
            if (PlaceGravel(p, r, seed).Ok) n++;
        }
    }

    private bool SpacingOk(Vec2 p, double r, double spacing)
    {
        foreach (var rk in Props.Rocks) if (Vec2.Distance(p, rk.Position) < r + rk.FootprintRadius + spacing) return false;
        foreach (var lg in Props.Logs) if (lg.AxisDistance(p) < r + lg.Radius + spacing) return false;
        foreach (var s in _w.Water.Springs) if (Vec2.Distance(p, s.Position) < r + 0.6) return false;
        return true;
    }

    private Vec2 RandomPoint(Rng rng, double margin)
    {
        for (int i = 0; i < 100; i++)
        {
            var p = new Vec2(rng.Range(-Domain.Radius, Domain.Radius), rng.Range(-Domain.Apothem, Domain.Apothem));
            if (Domain.ContainsDisc(p, margin)) return p;
        }
        return Vec2.Zero;
    }
}
