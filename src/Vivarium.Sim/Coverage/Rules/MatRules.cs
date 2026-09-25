using System.Diagnostics;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Rules;

/// <summary>Micro-environment source for moss rules, decoupled from <see cref="Vivarium.Sim.World.VivariumWorld"/>
/// so the growth lab can drive it with synthetic fields (docs/overhaul/growth_models.md §2).</summary>
public interface IMicroEnvSource
{
    MicroEnv Sample(int gx, int gz);
}

/// <summary>Where dead biomass and sphagnum moisture feedback go (docs/overhaul/growth_models.md §4.1, §4.5).
/// Optional: callers that do not care about detritus coupling or moisture feedback may pass null.</summary>
public interface IDetritusSink
{
    void AddDetritus(int gx, int gz, double amount);
    void AddMoistureBonus(int gx, int gz, double amount);
}

/// <summary>Per-step cost breakdown (docs/overhaul/growth_models.md §10), for the perf test and diagnostics.</summary>
public readonly record struct MatStepStats(double PhysiologyMs, double SpreadMs, double D2EMs);

/// <summary>
/// Moss ("mat") growth rules over a <see cref="CoverageLayer"/> (docs/overhaul/growth_models.md §4): water
/// balance, logistic biomass with dormancy/browning/death, rim spread with slope/moisture anisotropy, front
/// competition, long-range spore establishment, incremental D2E, and growth-form height.
///
/// Performance (§1.5, §10): each <see cref="CoverageTile"/> carries a persistent <c>Rim</c> set — occupied
/// cells touching an empty or differently-occupied neighbour — maintained incrementally on colonise/death/
/// takeover rather than rescanned from scratch. Spread and competition only ever walk the rim, never the full
/// grid. A tile that has reached biomass/water/dormancy equilibrium is marked <c>Steady</c> and then only runs
/// physiology every 4th step (dt scaled to match), waking early if its representative moisture sample drifts
/// past a small tolerance. D2E's BFS is seeded from the rim (not a full-grid scan) and capped at a bounded
/// radius, since the dome/cushion height formula saturates long before that and deep interior cells never need
/// an exact distance. All randomness comes from <see cref="HashRng"/>; all neighbour reads come from the
/// layer's double-buffered snapshot (§1.4), so results are independent of iteration and allocation order.
/// </summary>
public static class MatRules
{
    private const double AnisoSlope = 0.4;
    private const double AnisoMoist = 0.6;
    private const double CompetitionKappa = 2.0;
    private const double SeedWater01 = 0.6;
    private const int TileEdge = CoverageSpec.TileEdge;
    private const double SteadyDeltaB = 0.002;
    private const double EnvTolerance = 0.02;
    private const int SteadyPeriod = 4;
    private const int MaxD2E = 32; // dome/cushion height saturates long before this; bounds the BFS on huge flat colonies

    private static readonly (int dx, int dz)[] Neighbours8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>True while dormancy has exceeded the browning threshold (§4.1); a render cue, not a stored flag.</summary>
    public static bool IsBrowning(byte dorm, MatParams p) => dorm > p.DormBrownDays;

    /// <summary>Growth-form height from stored biomass and distance-to-edge (§4.5).</summary>
    public static double Height(MatParams p, float b, byte d2e, double wetness = 1.0)
    {
        return p.HeightForm switch
        {
            MatHeightForm.Dome => p.MaxHeightM * b * (1 - Math.Exp(-d2e / Math.Max(1e-6, p.DomeLength))),
            MatHeightForm.Wet => p.MaxHeightM * b * wetness,
            _ => p.MaxHeightM * b,
        };
    }

    /// <summary>
    /// Advances the Mat layer one flora step. <paramref name="seed"/> is the hash-RNG world seed used for every
    /// stochastic decision this step (independent of <see cref="CoverageLayer.WorldSeed"/> so the lab can vary it
    /// without recreating the layer). Returns a per-phase cost breakdown (§10).
    /// </summary>
    public static MatStepStats Step(CoverageLayer layer, IMicroEnvSource env, IReadOnlyList<MatParams> species, double dtDays, long step, ulong seed, IDetritusSink? sink)
    {
        layer.BeginStep();
        if (layer.TileCount == 0) { layer.Advance(); return default; }

        var byId = new MatParams?[256];
        foreach (var sp in species) byId[sp.OccupantId] = sp;
        var occIndex = new int[256];
        for (int i = 0; i < species.Count; i++) occIndex[species[i].OccupantId] = i;

        var tiles = layer.Tiles.ToList();
        var tileDict = new Dictionary<(int, int), CoverageTile>(tiles.Count);
        foreach (var t in tiles) tileDict[(t.Ti, t.Tj)] = t;

        foreach (var t in tiles)
            if (!t.RimReady) InitRim(tileDict, t);

        var changedCells = new List<(CoverageTile t, int lx, int lz)>();

        var sw = Stopwatch.StartNew();
        RunPhysiology(env, byId, tiles, dtDays, step, sink, changedCells);
        double physMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

        RunSpreadAndCompetition(layer, env, byId, species, occIndex, tiles, tileDict, dtDays, step, seed, changedCells);
        double spreadMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

        // incrementally refresh the rim around everything that changed this step, then bring in any brand-new
        // tiles a colonisation allocated so they participate correctly from next step onward.
        foreach (var (t, lx, lz) in changedCells) UpdateRimAround(tileDict, t, lx, lz);
        foreach (var t in tiles) if (!tileDict.ContainsKey((t.Ti, t.Tj))) tileDict[(t.Ti, t.Tj)] = t;
        foreach (var kv in tileDict) if (!tiles.Contains(kv.Value)) tiles.Add(kv.Value);

        bool topologyChanged = changedCells.Count > 0;
        if (topologyChanged) RecomputeD2E(tiles, tileDict, changedCells);
        double d2eMs = sw.Elapsed.TotalMilliseconds;

        layer.Advance();
        return new MatStepStats(physMs, spreadMs, d2eMs);
    }

    // ------------------------------------------------------------------ same-tile-fast-path neighbour access

    private static (byte occ, float b) SnapAt(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz)
    {
        if ((uint)lx < TileEdge && (uint)lz < TileEdge)
        {
            int li = lz * TileEdge + lx;
            return (t.SnapOcc![li], t.SnapB![li]);
        }
        if (!ResolveNeighbour(tileDict, t, lx, lz, out var nt, out var nli) || nt.SnapOcc == null) return (0, 0f);
        return (nt.SnapOcc[nli], nt.SnapB![nli]);
    }

    private static byte OccAt(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz)
    {
        if ((uint)lx < TileEdge && (uint)lz < TileEdge) return t.Occ[lz * TileEdge + lx];
        return ResolveNeighbour(tileDict, t, lx, lz, out var nt, out var nli) ? nt.Occ[nli] : (byte)0;
    }

    private static bool ResolveNeighbour(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz, out CoverageTile nt, out int nli)
    {
        int tdx = lx < 0 ? -1 : lx >= TileEdge ? 1 : 0;
        int tdz = lz < 0 ? -1 : lz >= TileEdge ? 1 : 0;
        if (!tileDict.TryGetValue((t.Ti + tdx, t.Tj + tdz), out nt!)) { nli = 0; return false; }
        int llx = lx < 0 ? TileEdge - 1 : lx >= TileEdge ? 0 : lx;
        int llz = lz < 0 ? TileEdge - 1 : lz >= TileEdge ? 0 : lz;
        nli = llz * TileEdge + llx;
        return true;
    }

    // ------------------------------------------------------------------ rim maintenance (persistent, incremental)

    private static bool IsRimCell(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz)
    {
        byte occSelf = t.Occ[lz * TileEdge + lx];
        if (occSelf == 0) return false;
        foreach (var (dx, dz) in Neighbours8)
        {
            byte occN = OccAt(tileDict, t, lx + dx, lz + dz);
            if (occN == 0 || occN != occSelf) return true;
        }
        return false;
    }

    private static void InitRim(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t)
    {
        for (int lz = 0; lz < TileEdge; lz++)
        for (int lx = 0; lx < TileEdge; lx++)
        {
            if (t.Occ[lz * TileEdge + lx] == 0) continue;
            if (IsRimCell(tileDict, t, lx, lz)) t.Rim.Add(lz * TileEdge + lx);
        }
        t.RimReady = true;
    }

    private static void SetRim(CoverageTile t, int li, bool isRim)
    {
        if (isRim) t.Rim.Add(li); else t.Rim.Remove(li);
    }

    /// <summary>Re-evaluates rim membership for a changed cell and its 8 neighbours (§1.5): the only cells whose
    /// rim status could possibly have flipped as a result of that one change.</summary>
    private static void UpdateRimAround(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz)
    {
        if (!tileDict.ContainsKey((t.Ti, t.Tj))) tileDict[(t.Ti, t.Tj)] = t;
        SetRim(t, lz * TileEdge + lx, IsRimCell(tileDict, t, lx, lz));
        foreach (var (dx, dz) in Neighbours8)
        {
            int nlx = lx + dx, nlz = lz + dz;
            CoverageTile nt; int nli;
            if ((uint)nlx < TileEdge && (uint)nlz < TileEdge) { nt = t; nli = nlz * TileEdge + nlx; }
            else if (!ResolveNeighbour(tileDict, t, nlx, nlz, out nt, out nli)) continue;
            if (nt.Occ[nli] == 0) continue;
            int nnlx = nli % TileEdge, nnlz = nli / TileEdge;
            SetRim(nt, nli, IsRimCell(tileDict, nt, nnlx, nnlz));
        }
    }

    // ------------------------------------------------------------------ §3/§4.1 physiology
    // Every occupied cell needs biomass/water/dormancy bookkeeping, not just the rim — but a tile that has
    // reached equilibrium (Steady) skips straight through most steps (§1.5, §10) and only re-samples its own
    // representative moisture (one Sample call, not one per cell) to decide whether to wake up early.

    private static void RunPhysiology(IMicroEnvSource env, MatParams?[] byId, List<CoverageTile> tiles, double dtDays, long step, IDetritusSink? sink, List<(CoverageTile, int, int)> changedCells)
    {
        foreach (var t in tiles)
        {
            if (t.Occ.AsSpan().IndexOfAnyExcept((byte)0) < 0) continue; // nothing occupied at all

            int cgx = t.Ti * TileEdge + TileEdge / 2, cgz = t.Tj * TileEdge + TileEdge / 2;
            double dt = dtDays;
            bool runFull = true;

            if (t.Steady)
            {
                double curMoisture = env.Sample(cgx, cgz).Moisture;
                bool envStable = !double.IsNaN(t.EnvMoistureCache) && Math.Abs(curMoisture - t.EnvMoistureCache) <= EnvTolerance;
                t.SkippedPhysiologySteps++;
                if (envStable && t.SkippedPhysiologySteps < SteadyPeriod)
                {
                    runFull = false;
                }
                else
                {
                    dt = dtDays * t.SkippedPhysiologySteps;
                    t.SkippedPhysiologySteps = 0;
                }
                t.EnvMoistureCache = curMoisture;
            }

            if (!runFull) continue;

            bool tileDirty = false, anyDorm = false;
            double maxDeltaB = 0;

            for (int lz = 0; lz < TileEdge; lz++)
            for (int lx = 0; lx < TileEdge; lx++)
            {
                int li = lz * TileEdge + lx;
                byte occ = t.Occ[li];
                if (occ == 0) continue;
                var p = byId[occ];
                if (p is null) continue;

                int gx = t.Ti * TileEdge + lx, gz = t.Tj * TileEdge + lz;
                var e = env.Sample(gx, gz);
                double w01 = t.W[li] / 255.0;
                double newW01 = WaterBalance.StepWater(w01, e.Moisture, e.Humidity, p, dt);

                double dryDays = t.Dorm[li];
                if (newW01 < p.WMin) dryDays = Math.Min(255, dryDays + dt);
                else if (newW01 > p.WOpt) dryDays = 0;
                byte newDorm = (byte)Math.Clamp(Math.Round(dryDays), 0, 255);

                if (newDorm > p.DormDeathDays)
                {
                    sink?.AddDetritus(gx, gz, t.B[li]);
                    t.Occ[li] = 0; t.B[li] = 0f; t.W[li] = 0; t.Age[li] = 0; t.Dorm[li] = 0; t.Flags[li] = 0; t.D2E[li] = 0;
                    tileDirty = true;
                    changedCells.Add((t, lx, lz));
                    continue;
                }

                double gW = WaterBalance.GrowthMultiplierWater(newW01, p);
                double gL = WaterBalance.GrowthMultiplierLight(e.Light, p);
                double gN = e.Nutrients > 0 ? Math.Min(1.0, 0.5 + 0.5 * e.Nutrients) : 1.0;
                double db = p.GrowthRate * gW * gL * gN * (1 - t.B[li]) * dt;
                double newB = Math.Clamp(t.B[li] + db, 0, 1);
                maxDeltaB = Math.Max(maxDeltaB, Math.Abs(newB - t.B[li]));
                if (newDorm > 0) anyDorm = true;

                t.B[li] = (float)newB;
                t.W[li] = (byte)Math.Round(Math.Clamp(newW01, 0, 1) * 255);
                t.Age[li] = (ushort)Math.Min(65535, t.Age[li] + (int)Math.Round(dt * 4));
                t.Dorm[li] = newDorm;
                tileDirty = true;

                if (p.MoistureFeedback > 0)
                    sink?.AddMoistureBonus(gx, gz, p.MoistureFeedback * newB * dt);
            }

            if (tileDirty) { t.Active = true; t.Touch(); }

            t.Steady = !anyDorm && maxDeltaB < SteadyDeltaB;
            if (t.Steady)
            {
                t.SkippedPhysiologySteps = 0;
                t.EnvMoistureCache = env.Sample(cgx, cgz).Moisture;
            }
        }
    }

    // ------------------------------------------------------------------ §4.2/§4.3/§4.4 spread, competition, spores
    // Rim-only (§1.5): a cell that touches neither an empty nor a differently-occupied neighbour can trigger
    // neither spread nor competition, so the persistent Rim set is both necessary and sufficient here.

    private static void RunSpreadAndCompetition(CoverageLayer layer, IMicroEnvSource env, MatParams?[] byId, IReadOnlyList<MatParams> species, int[] occIndex, List<CoverageTile> tiles, Dictionary<(int, int), CoverageTile> tileDict, double dtDays, long step, ulong seed, List<(CoverageTile, int, int)> changedCells)
    {
        int n = species.Count;
        var sporeSums = new double[256]; // approximated from rim biomass (the front), not the whole colony — bounds cost on huge interiors
        var pressure = new Dictionary<(int, int), double[]>();

        foreach (var t in tiles)
        {
            if (t.Rim.Count == 0) continue;
            foreach (int li in t.Rim.ToArray())
            {
                int lx = li % TileEdge, lz = li / TileEdge;
                byte occA = t.SnapOcc![li];
                if (occA == 0) continue; // stale rim entry (cleared this step by physiology); UpdateRimAround will fix it
                var pa = byId[occA];
                if (pa is null) continue;

                double ba = t.SnapB![li];
                sporeSums[occA] += ba;

                int gx = t.Ti * TileEdge + lx, gz = t.Tj * TileEdge + lz;
                var ea = env.Sample(gx, gz);
                double gWa = WaterBalance.GrowthMultiplierWater(ea.Moisture, pa);
                double gLa = WaterBalance.GrowthMultiplierLight(ea.Light, pa);
                double va = ba * gWa * gLa;

                byte bestOcc = 0;
                double bestVigourP = 0;

                foreach (var (dx, dz) in Neighbours8)
                {
                    var (occB, bb) = SnapAt(tileDict, t, lx + dx, lz + dz);
                    if (occB == 0)
                    {
                        var dir = new Vec2(dx, dz);
                        double wdir = 1.0
                            + AnisoSlope * Math.Max(0, ea.DownslopeDir.Dot(dir))
                            + AnisoMoist * Math.Max(0, ea.MoistureGradient.Dot(dir));
                        if (wdir <= 0) continue;
                        var key = (gx + dx, gz + dz);
                        if (!pressure.TryGetValue(key, out var arr)) { arr = new double[n]; pressure[key] = arr; }
                        arr[occIndex[occA]] += ba * gWa * wdir;
                    }
                    else if (occB != occA)
                    {
                        var pb = byId[occB];
                        if (pb is null) continue;
                        // Vigour of the CHALLENGER is evaluated at the contested cell's own environment (ea), not
                        // the challenger's home cell — competition should ask "who is better suited to grow HERE",
                        // otherwise whichever species has the higher home-turf ceiling wins everywhere (no seam).
                        double vb = bb * WaterBalance.GrowthMultiplierWater(ea.Moisture, pb) * WaterBalance.GrowthMultiplierLight(ea.Light, pb);
                        if (vb <= va) continue;
                        double p = 1 - Math.Exp(-CompetitionKappa * (vb - va) * dtDays);
                        if (p > bestVigourP) { bestVigourP = p; bestOcc = occB; }
                    }
                }

                if (bestOcc != 0)
                {
                    var (ti2, tj2) = CoverageSpec.TileOf(gx, gz);
                    double u = HashRng.Hash01(seed, (int)layer.Id, ti2, tj2, li, step, purpose: 2000);
                    if (u < bestVigourP)
                    {
                        var winner = byId[bestOcc]!;
                        t.Occ[li] = bestOcc; t.B[li] = (float)winner.SeedBiomass; t.W[li] = (byte)Math.Round(SeedWater01 * 255);
                        t.Age[li] = 0; t.Dorm[li] = 0; t.Flags[li] = 0;
                        t.Active = true; t.Touch();
                        changedCells.Add((t, lx, lz));
                    }
                }
            }
        }

        foreach (var (key, arr) in pressure)
        {
            var (gx, gz) = key;
            var e = env.Sample(gx, gz);

            byte bestOcc = 0;
            double bestP = 0;
            for (int i = 0; i < n; i++)
            {
                var p = species[i];
                if (e.Moisture < p.HardMinMoisture) continue;

                double pressureVal = arr[i];
                double suitability = WaterBalance.GrowthMultiplierWater(e.Moisture, p) * WaterBalance.GrowthMultiplierLight(e.Light, p);
                double pCol = pressureVal > 0 ? 1 - Math.Exp(-p.Lateral * pressureVal * suitability * dtDays) : 0;
                double sporeSum = sporeSums[p.OccupantId];
                double sporeP = sporeSum > 0 ? 1 - Math.Exp(-p.SporeRate * sporeSum * suitability * dtDays) : 0;
                double totalP = 1 - (1 - pCol) * (1 - sporeP);
                if (totalP <= 0) continue;

                var (ti2, tj2) = CoverageSpec.TileOf(gx, gz);
                int li2 = CoverageSpec.LocalIndex(gx, gz);
                double u = HashRng.Hash01(seed, (int)layer.Id, ti2, tj2, li2, step, purpose: 1000 + p.OccupantId);
                if (u < totalP && totalP > bestP) { bestP = totalP; bestOcc = p.OccupantId; }
            }

            if (bestOcc != 0)
            {
                var p = byId[bestOcc]!;
                layer.SetCell(gx, gz, bestOcc, (float)p.SeedBiomass, (byte)Math.Round(SeedWater01 * 255), 0, 0, 0, 0);
                layer.TryGetTile(CoverageSpec.TileOf(gx, gz).ti, CoverageSpec.TileOf(gx, gz).tj, out var nt);
                if (nt != null) changedCells.Add((nt, CoverageSpec.LocalIndex(gx, gz) % TileEdge, CoverageSpec.LocalIndex(gx, gz) / TileEdge));
            }
        }
    }

    // ------------------------------------------------------------------ §4.5 D2E: rim-seeded, depth-capped BFS

    private static void RecomputeD2E(List<CoverageTile> tiles, Dictionary<(int, int), CoverageTile> tileDict, List<(CoverageTile t, int lx, int lz)> changedCells)
    {
        var tileIdx = new Dictionary<CoverageTile, int>(tiles.Count);
        for (int i = 0; i < tiles.Count; i++) tileIdx[tiles[i]] = i;

        var dist = new int[tiles.Count][];
        var touched = new List<int>();
        var queue = new Queue<(int ti, int li)>();
        var seeded = new HashSet<(int, int)>();

        int[] DistOf(int ti)
        {
            var d = dist[ti];
            if (d == null) { d = new int[CoverageTile.N]; Array.Fill(d, -1); dist[ti] = d; touched.Add(ti); }
            return d;
        }

        void Seed(CoverageTile t, int li)
        {
            if (!tileIdx.TryGetValue(t, out int ti)) return;
            int lx = li % TileEdge, lz = li / TileEdge;
            if (t.Occ[li] == 0 || !IsEdge(tileDict, t, lx, lz)) return;
            var d = DistOf(ti);
            if (d[li] == 0) return;
            d[li] = 0;
            seeded.Add((ti, li));
            queue.Enqueue((ti, li));
        }

        // seed from the (small) rim set plus anything touched this step, rather than a full-grid scan.
        foreach (var t in tiles)
            foreach (int li in t.Rim) Seed(t, li);
        foreach (var (t, lx, lz) in changedCells)
            Seed(t, lz * TileEdge + lx);

        while (queue.Count > 0)
        {
            var (ti, li) = queue.Dequeue();
            int d = dist[ti][li];
            if (d >= MaxD2E) continue;
            var t = tiles[ti];
            int lx = li % TileEdge, lz = li / TileEdge;
            foreach (var (dx, dz) in Neighbours8)
            {
                int nlx = lx + dx, nlz = lz + dz;
                int nti; int nli;
                if ((uint)nlx < TileEdge && (uint)nlz < TileEdge) { nti = ti; nli = nlz * TileEdge + nlx; }
                else
                {
                    if (!ResolveNeighbour(tileDict, t, nlx, nlz, out var nt) || !tileIdx.TryGetValue(nt, out nti)) continue;
                    ResolveNeighbour(tileDict, t, nlx, nlz, out _, out nli);
                }
                if (tiles[nti].Occ[nli] == 0) continue;
                var nd = DistOf(nti);
                if (nd[nli] >= 0) continue;
                nd[nli] = d + 1;
                queue.Enqueue((nti, nli));
            }
        }

        foreach (int ti in touched)
        {
            var t = tiles[ti];
            var d = dist[ti];
            for (int li = 0; li < CoverageTile.N; li++)
            {
                if (d[li] < 0) continue;
                byte d2e = (byte)Math.Min(255, d[li]);
                if (t.D2E[li] == d2e) continue;
                t.D2E[li] = d2e;
                t.Touch();
            }
        }
    }

    private static bool ResolveNeighbour(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz, out CoverageTile nt)
        => ResolveNeighbour(tileDict, t, lx, lz, out nt, out _);

    private static bool IsEdge(Dictionary<(int, int), CoverageTile> tileDict, CoverageTile t, int lx, int lz)
    {
        foreach (var (dx, dz) in Neighbours8)
            if (OccAt(tileDict, t, lx + dx, lz + dz) == 0) return true;
        return false;
    }
}
