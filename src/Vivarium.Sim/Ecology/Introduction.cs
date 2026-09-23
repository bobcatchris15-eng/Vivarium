using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Ecology;

public readonly record struct IntroductionResult(bool Ok, string Message, IReadOnlyList<EntityId> Created)
{
    public static IntroductionResult Fail(string m) => new(false, m, Array.Empty<EntityId>());
}

/// <summary>
/// Deliberate (re)introduction of any catalog species into suitable habitat. This is what makes the world
/// non-loseable: extinction is always reversible from ordinary UI, without reset or file editing.
/// </summary>
public static class Introduction
{
    public static IntroductionResult IntroduceFlora(VivariumWorld w, string speciesId, Vec2 p)
    {
        var sp = w.Content.FloraById(speciesId);
        if (sp == null) return IntroductionResult.Fail($"unknown flora species '{speciesId}'");
        if (!w.FloraSystem.CanEstablish(sp, p, out var reason)) return IntroductionResult.Fail($"{sp.Name} cannot establish here: {reason}");
        var f = w.FloraSystem.Establish(sp, p, "introduced", sp.MaxBiomass * 0.25);
        w.Tally.Of(sp.Id).Introduced++;
        Log.Info(LogCategory.Ecology, $"Introduced {sp.Name} {f.Id} at {p}.");
        return new IntroductionResult(true, $"{sp.Name} introduced", new[] { f.Id });
    }

    /// <summary>Checks whether a fauna individual can be placed at p (used by introduction and critter release).</summary>
    public static string? FaunaPlacementProblem(VivariumWorld w, FaunaSpeciesDef sp, Vec2 p)
    {
        var s = w.FaunaSystem.Suitability(sp, p);
        if (s.HardRefused) return s.RefusalReason;
        if (!w.FaunaSystem.IsPassable(sp, p)) return sp.Medium == Medium.Aquatic ? $"{sp.Name} needs deeper water here" : $"{sp.Name} cannot stand here";
        return null;
    }

    public static IntroductionResult IntroduceFauna(VivariumWorld w, string speciesId, Vec2 p, int count = 1)
    {
        var sp = w.Content.FaunaById(speciesId);
        if (sp == null) return IntroductionResult.Fail($"unknown fauna species '{speciesId}'");
        var problem = FaunaPlacementProblem(w, sp, p);
        if (problem != null) return IntroductionResult.Fail(problem);
        if (w.Fauna.CountOf(sp.Id) >= sp.PopulationCap) return IntroductionResult.Fail($"{sp.Name} population is at its safety cap ({sp.PopulationCap})");
        var created = new List<EntityId>();
        var rng = Rng.Keyed(w.Seed, "ecology.introduce", w.Ids.LastSerial + 1);
        for (int i = 0; i < count; i++)
        {
            var q = p;
            for (int t = 0; t < 8 && i > 0; t++)
            {
                var c = p + Vec2.FromAngle(rng.Range(0, 2 * Math.PI)) * rng.Range(0.02, 0.2);
                if (FaunaPlacementProblem(w, sp, c) == null) { q = c; break; }
            }
            var f = w.FaunaSystem.CreateFounder(sp, q, ageFraction: sp.MaturityAge / sp.Lifespan * 1.1);
            f.Energy = sp.MaxEnergy * 0.8;
            created.Add(f.Id);
            w.Tally.Of(sp.Id).Introduced++;
        }
        Log.Info(LogCategory.Ecology, $"Introduced {created.Count} {sp.Name} at {p}.");
        return new IntroductionResult(true, $"{created.Count} {sp.Name} introduced", created);
    }
}

/// <summary>Initial distribution of starter flora and fauna from the world preset.</summary>
public static class Populate
{
    public static void Starters(VivariumWorld w)
    {
        var rng = Rng.Stream(w.Seed, "world.starters");
        foreach (var entry in w.Descriptor.StarterFlora)
        {
            var sp = w.Content.FloraOrThrow(entry.Species);
            var candidates = RankedCells(w, rng, c => { var s = w.FloraSystem.Suitability(sp, c); return s.HardRefused ? -1 : s.Score; }, sp.MinSuitability * 1.2);
            int placed = 0;
            foreach (var (pos, _) in candidates)
            {
                if (placed >= entry.Count) break;
                var q = pos + new Vec2(rng.Range(-0.1, 0.1), rng.Range(-0.1, 0.1));
                if (!w.FloraSystem.CanEstablish(sp, q, out _)) continue;
                var f = w.FloraSystem.Establish(sp, q, "starter", sp.MaxBiomass * rng.Range(0.3, 0.8));
                f.Age = rng.Range(0.3, 0.9) * sp.Lifespan * 0.6;
                f.LastSpreadAge = f.Age - rng.Range(0, 1) * sp.SpreadInterval;   // staggered first spread
                placed++;
            }
            if (placed < entry.Count) Log.Warn(LogCategory.Ecology, $"Placed {placed}/{entry.Count} starter {sp.Name} (limited suitable habitat).");
        }
        foreach (var entry in w.Descriptor.StarterFauna)
        {
            var sp = w.Content.FaunaOrThrow(entry.Species);
            var candidates = RankedCells(w, rng, c =>
            {
                if (Introduction.FaunaPlacementProblem(w, sp, c) != null) return -1;
                return w.FaunaSystem.Suitability(sp, c).Score + 0.5 * w.FaunaSystem.FoodAt(sp, c);
            }, 0.05);
            int placed = 0, group = 0;
            var top = candidates.Take(Math.Max(8, candidates.Count / 3)).ToList();
            while (placed < entry.Count && top.Count > 0)
            {
                var (centre, _) = top[rng.NextInt(top.Count)];
                int n = Math.Min(sp.Behaviors.Contains("schooling") ? 8 : 4, entry.Count - placed);
                for (int i = 0; i < n; i++)
                {
                    var q = centre + new Vec2(rng.Range(-0.15, 0.15), rng.Range(-0.15, 0.15));
                    if (Introduction.FaunaPlacementProblem(w, sp, q) != null) q = centre;
                    w.FaunaSystem.CreateFounder(sp, q);
                    placed++;
                }
                if (++group > entry.Count * 4) break;
            }
        }
    }

    /// <summary>Samples in-domain cell centres, scores them, returns descending score order (ties by index).</summary>
    private static List<(Vec2 Pos, double Score)> RankedCells(VivariumWorld w, Rng rng, Func<Vec2, double> score, double minScore)
    {
        var list = new List<(Vec2, double, int)>();
        var cells = w.Grid.DomainCells;
        for (int k = 0; k < cells.Length; k += 2)
        {
            var c = w.Grid.CellCenter(cells[k]);
            double s = score(c);
            if (s >= minScore) list.Add((c, s + rng.NextDouble() * 0.15, cells[k]));
        }
        return list.OrderByDescending(x => x.Item2).ThenBy(x => x.Item3).Select(x => (x.Item1, x.Item2)).ToList();
    }
}
