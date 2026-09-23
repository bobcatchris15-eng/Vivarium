using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tools;

public readonly record struct ToolResult(bool Ok, string Message, IReadOnlyList<EntityId> Affected)
{
    public static ToolResult Success(string msg, params EntityId[] ids) => new(true, msg, ids);
    public static ToolResult Fail(string msg) => new(false, msg, Array.Empty<EntityId>());
}

public enum ToolKind { Select, Grab, RemovePlant, Nutrients, Poke, PlaceRock, PlaceLog, PlaceGravel, IntroduceFlora, IntroduceFauna, MoveProp, RemoveProp,
    TerrainRaise, TerrainLower, TerrainSmooth, PourWater, DrainWater, Spring }

/// <summary>
/// Generic poke carrying point, direction, strength and the selected target/context. Species-agnostic:
/// fauna flee (a short-lived, non-destructive disturbance), flora report a cosmetic acknowledgement only.
/// </summary>
public sealed record PokeAction(Vec3 Point, Vec3 Direction, double Strength, WorldHit Target);

public sealed class PokeResult
{
    public List<EntityId> FaunaDisturbed { get; } = new();
    /// <summary>Flora that should visibly wobble. Cosmetic: no ecological state is changed.</summary>
    public List<EntityId> FloraWobble { get; } = new();
    public Vec3 Point { get; init; }
    public string Message => $"poked: {FaunaDisturbed.Count} critter(s) startled, {FloraWobble.Count} plant(s) brushed";
}

/// <summary>
/// Every player intervention, validated against authoritative state. The client never mutates the world
/// except through these methods.
/// </summary>
public sealed class ToolActions
{
    private readonly VivariumWorld _w;
    public ToolActions(VivariumWorld w) { _w = w; }
    private ToolConfig Cfg => _w.Content.Tools;

    // ------------------------------------------------------------------ grab / release critter

    public ToolResult Grab(EntityId faunaId)
    {
        var f = _w.Fauna.Get(faunaId);
        if (f == null) return ToolResult.Fail("that critter is gone");
        if (f.Grabbed) return ToolResult.Fail("already held");
        f.Grabbed = true;
        return ToolResult.Success($"picked up {_w.Content.FaunaOrThrow(f.SpeciesId).Name}", f.Id);
    }

    /// <summary>Moves a held critter (display position while carried). Stays within the island footprint.</summary>
    public ToolResult MoveHeld(EntityId faunaId, Vec3 p)
    {
        var f = _w.Fauna.Get(faunaId);
        if (f == null || !f.Grabbed) return ToolResult.Fail("not holding that critter");
        if (!p.IsFinite) return ToolResult.Fail("invalid position");
        var xz = _w.Domain.ClampInside(p.XZ, 0.05);
        f.X = xz.X; f.Z = xz.Z; f.Y = Math.Max(p.Y, _w.GroundHeight(xz));
        return ToolResult.Success("moved", f.Id);
    }

    public string? ReleaseProblem(EntityId faunaId, Vec2 p)
    {
        var f = _w.Fauna.Get(faunaId);
        if (f == null) return "that critter is gone";
        return Introduction.FaunaPlacementProblem(_w, _w.Content.FaunaOrThrow(f.SpeciesId), p);
    }

    /// <summary>Releases at p if the habitat is valid; otherwise the critter stays held and the reason is returned.</summary>
    public ToolResult Release(EntityId faunaId, Vec2 p)
    {
        var f = _w.Fauna.Get(faunaId);
        if (f == null) return ToolResult.Fail("that critter is gone");
        if (!f.Grabbed) return ToolResult.Fail("not holding that critter");
        var sp = _w.Content.FaunaOrThrow(f.SpeciesId);
        var problem = Introduction.FaunaPlacementProblem(_w, sp, p);
        if (problem != null) return ToolResult.Fail($"Can't release here: {problem}");
        f.X = p.X; f.Z = p.Z; f.Y = _w.FaunaSystem.RestingY(sp, p, f.Pitch);
        f.Grabbed = false;
        return ToolResult.Success($"released {sp.Name}", f.Id);
    }

    /// <summary>Puts a held critter back where it was picked up (always valid).</summary>
    public ToolResult ReturnHeld(EntityId faunaId, Vec3 origin)
    {
        var f = _w.Fauna.Get(faunaId);
        if (f == null) return ToolResult.Fail("that critter is gone");
        f.Position = origin; f.Grabbed = false;
        return ToolResult.Success("returned", f.Id);
    }

    // ------------------------------------------------------------------ flora

    public ToolResult RemovePlant(EntityId floraId)
    {
        var f = _w.Flora.Get(floraId);
        if (f == null) return ToolResult.Fail("nothing to remove");
        var name = _w.Content.FloraOrThrow(f.SpeciesId).Name;
        return _w.FloraSystem.Kill(f, "removed") ? ToolResult.Success($"removed {name}", floraId) : ToolResult.Fail("nothing to remove");
    }

    // ------------------------------------------------------------------ nutrients

    public double ClampNutrientRadius(double r) => MathD.Clamp(r, Cfg.NutrientMinRadius, Cfg.NutrientMaxRadius);

    /// <summary>Adds nutrients inside the circular footprint only (smooth falloff to the rim). Returns amount added.</summary>
    public ToolResult ApplyNutrients(Vec2 centre, double radius, double? amount = null)
    {
        if (!_w.Domain.Contains(centre)) return ToolResult.Fail("outside the island");
        radius = ClampNutrientRadius(radius);
        double a = amount ?? Cfg.NutrientAmount;
        double added = 0; int cells = 0;
        foreach (int c in _w.Grid.CellsInRadius(centre, radius))
        {
            double d = Vec2.Distance(_w.Grid.CellCenter(c), centre) / radius;
            added += _w.Fields.Nutrients.Add(c, a * (1 - d * d));
            cells++;
        }
        _w.Tally.NutrientsApplied += added;
        return cells == 0 ? ToolResult.Fail("no ground under the spreader") : ToolResult.Success($"added {added:0.00} nutrients over {cells} cells");
    }

    // ------------------------------------------------------------------ poke

    public PokeResult Poke(PokeAction a)
    {
        var res = new PokeResult { Point = a.Point };
        double strength = MathD.Clamp(a.Strength, 0, 3);
        double radius = Cfg.PokeRadius * Math.Max(0.5, strength);
        double until = _w.Clock.SimSeconds + Cfg.PokeDisturbSeconds * Math.Max(0.25, strength);
        foreach (var f in _w.Fauna.Items)
        {
            if (f.Grabbed) continue;
            bool targeted = a.Target.Kind == HitKind.Fauna && a.Target.Id == f.Id;
            if (!targeted && (f.Position - a.Point).Length > radius) continue;
            f.DisturbedUntil = Math.Max(f.DisturbedUntil, until);
            f.DisturbX = a.Point.X; f.DisturbZ = a.Point.Z;
            var sp = _w.Content.FaunaOrThrow(f.SpeciesId);
            res.FaunaDisturbed.Add(f.Id);
            if (sp.Conglobates) continue;   // pill bugs roll up where they are
            // short startle hop away from the stick, only onto valid habitat
            var away = (f.PositionXZ - a.Point.XZ);
            away = away.LengthSq > 1e-10 ? away.Normalized() : Vec2.FromAngle(f.Heading);
            var q = f.PositionXZ + away * (0.04 * strength);
            if (_w.FaunaSystem.IsPassable(sp, q)) { f.X = q.X; f.Z = q.Z; f.Y = sp.Medium == Medium.Aquatic ? _w.FaunaSystem.RestingY(sp, q, f.Pitch) : _w.GroundHeight(q); }
            f.Heading = away.Angle;
        }
        foreach (var f in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            bool targeted = a.Target.Kind == HitKind.Flora && a.Target.Id == f.Id;
            if (targeted || Vec2.Distance(f.Position, a.Point.XZ) <= Math.Max(radius, f.Radius(sp))) res.FloraWobble.Add(f.Id);
        }
        return res;
    }

    // ------------------------------------------------------------------ props

    public double ClampRockScale(double s) => MathD.Clamp(s, Cfg.RockMinScale, Cfg.RockMaxScale);
    public double ClampLogLength(double l) => MathD.Clamp(l, Cfg.LogMinLength, Cfg.LogMaxLength);
    public double ClampLogRadius(double r) => MathD.Clamp(r, Cfg.LogMinRadius, Cfg.LogMaxRadius);
    public double ClampGravelRadius(double r) => MathD.Clamp(r, Cfg.GravelMinRadius, Cfg.GravelMaxRadius);

    /// <summary>Validity preview for the placement cursor (null = valid).</summary>
    public string? PreviewRock(Vec2 p, double scale) => _w.Placement.ValidateRock(p, ClampRockScale(scale) * 1.2, ClampRockScale(scale) * 1.1);
    public string? PreviewLog(Vec2 p, double heading, double length, double radius) => _w.Placement.ValidateLog(p, heading, ClampLogLength(length), ClampLogRadius(radius));
    public string? PreviewGravel(Vec2 p, double radius) => _w.Placement.ValidateGravel(p, ClampGravelRadius(radius));

    public ToolResult PlaceRock(Vec2 p, double scale, double rotation, ulong variantSeed) => Wrap(_w.Placement.PlaceRock(p, ClampRockScale(scale), rotation, variantSeed), "rock");
    public ToolResult PlaceLog(Vec2 p, double heading, double length, double radius, int decay, ulong seed) => Wrap(_w.Placement.PlaceLog(p, heading, ClampLogLength(length), ClampLogRadius(radius), decay, seed), "log");
    public ToolResult PlaceGravel(Vec2 p, double radius, ulong seed) => Wrap(_w.Placement.PlaceGravel(p, ClampGravelRadius(radius), seed), "gravel");
    public ToolResult MoveProp(EntityId id, Vec2 p) => Wrap(_w.Placement.Move(id, p), "prop");
    public ToolResult RemoveProp(EntityId id) => Wrap(_w.Placement.Remove(id), "prop");

    private ToolResult Wrap(PlacementResult r, string what)
    {
        if (r.Ok) _w.RefreshDerived();
        return r.Ok ? ToolResult.Success($"{r.Message} {what}", r.Id) : ToolResult.Fail($"Can't place {what}: {r.Message}");
    }

    // ------------------------------------------------------------------ terrain

    public double ClampSculptRadius(double r) => MathD.Clamp(r, Cfg.SculptMinRadius, Cfg.SculptMaxRadius);

    /// <summary>One sculpt dab lasting <paramref name="seconds"/> of brushing (the client calls this repeatedly while held).</summary>
    public ToolResult Sculpt(Vec2 centre, double radius, SculptMode mode, double seconds)
    {
        if (!_w.Domain.Contains(centre)) return ToolResult.Fail("outside the island");
        radius = ClampSculptRadius(radius);
        seconds = MathD.Clamp(seconds, 0, 0.5);
        double amount = mode == SculptMode.Smooth ? 4 * seconds : Cfg.SculptRate * seconds;
        int n = TerrainEditing.Sculpt(_w, centre, radius, amount, mode);
        if (n == 0) return ToolResult.Fail(mode switch { SculptMode.Raise => "already at the height limit", SculptMode.Lower => "already at the depth limit", _ => "already smooth" });
        return ToolResult.Success(mode switch { SculptMode.Raise => "raised the ground", SculptMode.Lower => "lowered the ground", _ => "smoothed the ground" });
    }

    /// <summary>Call when a sculpt stroke ends so derived fields (light) are fresh before anything reads them.</summary>
    public void EndSculptStroke() => _w.Fields.RecomputeLight(_w.Terrain, _w.Props);

    // ------------------------------------------------------------------ water

    public double ClampWaterRadius(double r) => MathD.Clamp(r, Cfg.WaterMinRadius, Cfg.WaterMaxRadius);

    public ToolResult PourWater(Vec2 centre, double radius, double seconds)
    {
        if (!_w.Domain.Contains(centre)) return ToolResult.Fail("outside the island");
        double v = _w.Water.AddWater(centre, ClampWaterRadius(radius), Cfg.PourRate * MathD.Clamp(seconds, 0, 0.5));
        return v > 0 ? ToolResult.Success($"poured {v * 1000:0.0} L") : ToolResult.Fail("nothing poured");
    }

    public ToolResult DrainWater(Vec2 centre, double radius, double seconds)
    {
        if (!_w.Domain.Contains(centre)) return ToolResult.Fail("outside the island");
        double v = _w.Water.RemoveWater(centre, ClampWaterRadius(radius), Cfg.DrainRate * MathD.Clamp(seconds, 0, 0.5));
        return v > 0 ? ToolResult.Success($"soaked up {v * 1000:0.0} L") : ToolResult.Fail("no surface water here");
    }

    /// <summary>The spring within <paramref name="reach"/> of p, if any.</summary>
    public Water.Spring? SpringNear(Vec2 p, double reach = 0.35) =>
        _w.Water.Springs.Where(s => Vec2.Distance(s.Position, p) <= reach).OrderBy(s => Vec2.Distance(s.Position, p)).ThenBy(s => s.Id.Value).FirstOrDefault();

    public string? PreviewSpring(Vec2 p)
    {
        if (SpringNear(p) != null) return null;   // clicking removes it
        if (!_w.Domain.ContainsDisc(p, 0.1)) return "too close to the edge";
        if (_w.Water.Springs.Count >= Cfg.MaxSprings) return $"at most {Cfg.MaxSprings} springs";
        return null;
    }

    /// <summary>Adds a spring at p, or removes the one already there.</summary>
    public ToolResult ToggleSpring(Vec2 p)
    {
        if (SpringNear(p) is { } existing)
        {
            _w.Water.Springs.Remove(existing);
            return ToolResult.Success("removed the spring", existing.Id);
        }
        var problem = PreviewSpring(p);
        if (problem != null) return ToolResult.Fail($"Can't add a spring: {problem}");
        var sp = new Water.Spring { Id = _w.Ids.Next(EntityKind.Spring), X = p.X, Z = p.Z, Discharge = Cfg.SpringDischarge };
        _w.Water.Springs.Add(sp);
        return ToolResult.Success("a new spring bubbles up", sp.Id);
    }

    // ------------------------------------------------------------------ growth speed (biological clock)

    /// <summary>Sets how much faster biology runs than locomotion (saved with the world).</summary>
    public ToolResult SetBioAcceleration(double factor)
    {
        if (!double.IsFinite(factor)) return ToolResult.Fail("invalid growth speed");
        factor = MathD.Clamp(factor, WorldDescriptor.MinBioAcceleration, WorldDescriptor.MaxBioAcceleration);
        _w.Descriptor.BioAcceleration = factor;
        _w.Clock.BioAcceleration = factor;
        return ToolResult.Success($"growth speed ×{factor:0.#}");
    }

    // ------------------------------------------------------------------ species introduction

    public ToolResult IntroduceFlora(string speciesId, Vec2 p)
    {
        var r = Introduction.IntroduceFlora(_w, speciesId, p);
        return r.Ok ? new ToolResult(true, r.Message, r.Created) : ToolResult.Fail(r.Message);
    }

    public string? PreviewFlora(string speciesId, Vec2 p)
    {
        var sp = _w.Content.FloraById(speciesId);
        if (sp == null) return "unknown species";
        return _w.FloraSystem.CanEstablish(sp, p, out var reason) ? null : reason;
    }

    public ToolResult IntroduceFauna(string speciesId, Vec2 p, int? count = null)
    {
        var r = Introduction.IntroduceFauna(_w, speciesId, p, count ?? Cfg.IntroduceFaunaCount);
        return r.Ok ? new ToolResult(true, r.Message, r.Created) : ToolResult.Fail(r.Message);
    }

    public string? PreviewFauna(string speciesId, Vec2 p)
    {
        var sp = _w.Content.FaunaById(speciesId);
        if (sp == null) return "unknown species";
        return Introduction.FaunaPlacementProblem(_w, sp, p);
    }
}
