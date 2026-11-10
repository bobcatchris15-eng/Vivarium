using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Ecology;

/// <summary>Per-world flora subset: ids kept for this island and best suitability score per kept id.</summary>
public sealed record FloraSpeciesPoolResult(HashSet<string> Ids, IReadOnlyDictionary<string, double> Fitness);

/// <summary>Deterministic per-world subset of the flora catalog. Scans the catalog against island sample points with
/// permissive survival-grade potential suitability, buckets species by moisture/light/nutrient optima, keeps a seeded
/// fraction (52-72%) of each band with a floor of one per non-empty band, then caps the total at 70 (floor 16) so an
/// island shows its own mix rather than the whole catalog.</summary>
public static class FloraSpeciesPool
{
    public const string StreamId = "flora.pool.v1";

    private sealed class Candidate
    {
        public required FloraSpeciesDef Species;
        public required double ViableFraction;
        public required double BestScore;
        public required double Rank;
    }

    public static FloraSpeciesPoolResult Select(VivariumWorld w)
    {
        var species = w.Content.Flora;
        var cells = w.Grid.DomainCells;
        w.RootZone.EnsureCurrent();
        w.Flora.RebuildIndex();
        w.FloraSystem.PrepareCanopyCaches();

        var points = new List<Vec2>((cells.Length + 1) / 2);
        for (int i = 0; i < cells.Length; i += 2) points.Add(w.Grid.CellCenter(cells[i]));
        int samples = points.Count;

        var viableFractionById = new Dictionary<string, double>(StringComparer.Ordinal);
        var bestScoreById = new Dictionary<string, double>(StringComparer.Ordinal);
        var viableTotal = 0;

        foreach (var sp in species)
        {
            double threshold = Math.Max(0.10, sp.MinSuitability * 0.9);
            int dims = 3 + (sp.Oxygen.HasValue ? 1 : 0) + (sp.TopsoilDepth.HasValue ? 1 : 0);
            double exponent = 1.0 / dims;
            int viable = 0;
            double best = 0;
            foreach (var p in points)
            {
                var s = w.FloraSystem.PotentialSuitability(sp, p);
                if (s.HardRefused) continue;
                double fm = sp.Moisture.Eval(s.Moisture);
                double fl = sp.Light.EvalStage(s.Light, 3.0);
                double fn = sp.Nutrients.Eval(s.Nutrients);
                double ox = sp.Oxygen.HasValue ? sp.Oxygen.Value.EvalStage(s.Oxygen, 3.0) : 1;
                double ts = sp.TopsoilDepth.HasValue ? sp.TopsoilDepth.Value.EvalStage(s.TopsoilDepth, 3.0) : 1;
                double score = s.SubstrateFactor * Math.Pow(Math.Max(fm * fl * fn * ox * ts, 0), exponent);
                if (score > best) best = score;
                if (score >= threshold) viable++;
            }
            double fraction = samples > 0 ? (double)viable / samples : 0;
            viableFractionById[sp.Id] = fraction;
            bestScoreById[sp.Id] = best;
            if (viable > 0) viableTotal++;
        }

        var rng = Rng.Stream(w.Seed, StreamId);
        var rankById = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var sp in species)
            rankById[sp.Id] = viableFractionById[sp.Id] + rng.NextDouble() * 0.08;
        double keep = 0.52 + 0.20 * rng.NextDouble();

        var buckets = new List<Candidate>[27];
        for (int b = 0; b < buckets.Length; b++) buckets[b] = new List<Candidate>();
        foreach (var sp in species)
        {
            double fraction = viableFractionById[sp.Id];
            if (fraction <= 0) continue;
            int bm = (int)Math.Clamp(Math.Floor(sp.Moisture.Optimum * 3), 0, 2);
            int bl = (int)Math.Clamp(Math.Floor(sp.Light.Optimum * 3), 0, 2);
            int bn = (int)Math.Clamp(Math.Floor(sp.Nutrients.Optimum * 3), 0, 2);
            int bucket = bm * 9 + bl * 3 + bn;
            buckets[bucket].Add(new Candidate
            {
                Species = sp,
                ViableFraction = fraction,
                BestScore = bestScoreById[sp.Id],
                Rank = rankById[sp.Id],
            });
        }

        Comparison<Candidate> byRankDesc = (a, b) =>
        {
            int c = b.Rank.CompareTo(a.Rank);
            if (c != 0) return c;
            c = b.BestScore.CompareTo(a.BestScore);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Species.Id, b.Species.Id);
        };

        var poolIds = new HashSet<string>(StringComparer.Ordinal);
        var keptById = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var guaranteed = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new List<Candidate>();

        foreach (var list in buckets)
        {
            if (list.Count == 0) continue;
            list.Sort(byRankDesc);
            int want = Math.Max(1, (int)Math.Round(list.Count * keep));
            if (want > list.Count) want = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var cand = list[i];
                if (i >= want || (i > 0 && cand.ViableFraction < 0.02))
                {
                    excluded.Add(cand);
                    continue;
                }
                poolIds.Add(cand.Species.Id);
                keptById[cand.Species.Id] = cand;
                if (i == 0) guaranteed.Add(cand.Species.Id);
            }
        }

        int cap = Math.Min(70, Math.Max(16, (int)Math.Round(viableTotal * keep)));
        while (poolIds.Count > cap)
        {
            Candidate? worst = null;
            foreach (var id in poolIds)
            {
                if (guaranteed.Contains(id)) continue;
                var cand = keptById[id];
                if (worst == null || cand.Rank < worst.Rank
                    || (cand.Rank == worst.Rank && cand.BestScore < worst.BestScore)
                    || (cand.Rank == worst.Rank && cand.BestScore == worst.BestScore
                        && string.CompareOrdinal(cand.Species.Id, worst.Species.Id) > 0))
                    worst = cand;
            }
            if (worst == null) break;
            poolIds.Remove(worst.Species.Id);
            keptById.Remove(worst.Species.Id);
            excluded.Add(worst);
        }

        int floor = Math.Min(16, viableTotal);
        if (poolIds.Count < floor)
        {
            excluded.Sort(byRankDesc);
            foreach (var cand in excluded)
            {
                if (poolIds.Count >= floor) break;
                if (poolIds.Contains(cand.Species.Id)) continue;
                poolIds.Add(cand.Species.Id);
                keptById[cand.Species.Id] = cand;
            }
        }

        var fitness = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var id in poolIds)
            fitness[id] = bestScoreById[id];
        return new FloraSpeciesPoolResult(poolIds, fitness);
    }
}
