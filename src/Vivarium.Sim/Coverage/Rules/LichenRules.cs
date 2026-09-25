using Vivarium.Sim.Coverage;

namespace Vivarium.Sim.Coverage.Rules;

/// <summary>
/// The only environment view <see cref="LichenRules"/> needs (docs/overhaul/growth_models.md §2): a per-cell
/// <see cref="MicroEnv"/> sample, addressed by global cell coordinates so growth-lab scenarios can synthesize
/// worlds directly without a full <c>VivariumWorld</c>.
/// </summary>
public interface ILichenEnvSource
{
    MicroEnv Sample(int gx, int gz);
}

/// <summary>
/// Lichen growth rules over a <see cref="CoverageLayer"/> (Crust layer): substrate gate (§5.1), crustose Eden
/// growth (§5.2), foliose tip-biased lobing with centre senescence (§5.3), and fruticose dome footprint (§5.4).
/// A pure step function: all state lives in the layer, all randomness is the layer's hash RNG, all neighbour
/// reads are the previous-step snapshot (§1.4-1.5), so results are order-independent and deterministic.
/// </summary>
public static class LichenRules
{
    private static readonly (int dx, int dz)[] N8 =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    /// <summary>Advances the Crust layer one growth-lab step. <paramref name="seed"/> is accepted for API symmetry
    /// with the other rule sets; the layer's own <see cref="CoverageLayer.WorldSeed"/> already seeds every draw.</summary>
    public static void Step(CoverageLayer layer, ILichenEnvSource env, IReadOnlyList<LichenParams> species, double dtDays, long step, ulong seed)
    {
        _ = seed;
        layer.BeginStep();

        var occupied = new List<(int gx, int gz, int ti, int tj, int li)>();
        foreach (var t in layer.Tiles)
        {
            var snap = t.SnapOcc;
            if (snap == null) continue;
            for (int li = 0; li < CoverageTile.N; li++)
            {
                if (snap[li] == 0) continue;
                int lx = li % CoverageSpec.TileEdge, lz = li / CoverageSpec.TileEdge;
                occupied.Add((t.Ti * CoverageSpec.TileEdge + lx, t.Tj * CoverageSpec.TileEdge + lz, t.Ti, t.Tj, li));
            }
        }

        var boundary = new HashSet<(int, int)>();
        var toClear = new List<(int, int)>();

        // ---- 1. physiology: water (§3), age, boundary/prothallus (§5.2), centre senescence (§5.3) ----
        foreach (var (gx, gz, ti, tj, li) in occupied)
        {
            if (!layer.TryGetTile(ti, tj, out var tile) || tile == null) continue;
            byte occ = tile.Occ[li];
            if (occ == 0) continue;
            var sp = SpeciesOf(species, occ);
            if (sp == null) continue;

            var me = env.Sample(gx, gz);

            byte w = LichenWater.StepWater(tile.W[li], me.Moisture, me.Humidity, dtDays, sp.KWet, sp.KDry);
            tile.W[li] = w;
            double gW = LichenWater.GrowthWaterFactor(LichenWater.W01(w), sp.WMin, sp.WOpt);
            double gL = LichenWater.GrowthLightFactor(me.Light, sp.KLight);

            int ageInc = (int)Math.Round(dtDays * 4);
            tile.Age[li] = (ushort)Math.Min(tile.Age[li] + Math.Max(ageInc, 0), ushort.MaxValue);

            bool isBoundary = false;
            foreach (var (dx, dz) in N8)
            {
                byte nOcc = layer.SnapshotOcc(gx + dx, gz + dz);
                if (nOcc != 0 && nOcc != occ) { isBoundary = true; break; }
            }
            if (isBoundary)
            {
                tile.Flags[li] |= (byte)CoverageFlags.Boundary;
                boundary.Add((gx, gz));
            }

            double b = tile.B[li];
            double growth = sp.Lateral * 4.0 * gW * gL * (sp.MaxBiomass - b) * dtDays;
            tile.B[li] = (float)Math.Clamp(b + growth, 0, sp.MaxBiomass);

            bool isDead = (tile.Flags[li] & (byte)CoverageFlags.Dead) != 0;
            if (!isDead && sp.Form == LichenForm.Foliose &&
                tile.Age[li] >= sp.CentreDeathDays * 4 && tile.D2E[li] >= sp.CentreDeathMinD2E)
            {
                tile.Flags[li] |= (byte)CoverageFlags.Dead;
                isDead = true;
            }
            if (isDead)
            {
                double nb = Math.Max(0, tile.B[li] - sp.DeadDecayPerDay * dtDays);
                tile.B[li] = (float)nb;
                if (nb <= 1e-4) toClear.Add((gx, gz));
            }

            tile.Touch();
        }

        foreach (var (gx, gz) in toClear) layer.SetCell(gx, gz, 0, 0, 0, 0, 0, 0, 0);

        UpdateD2E(layer, occupied);

        // ---- 2. colonisation over the pre-step snapshot (§5.1-5.4) ----
        var candidates = new SortedSet<(int, int)>(Comparer<(int, int)>.Create(
            (a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2)));
        foreach (var (gx, gz, _, _, _) in occupied)
        {
            if (boundary.Contains((gx, gz))) continue;
            foreach (var (dx, dz) in N8)
            {
                int nx = gx + dx, nz = gz + dz;
                if (layer.SnapshotOcc(nx, nz) == 0) candidates.Add((nx, nz));
            }
        }

        foreach (var (gx, gz) in candidates)
        {
            var me = env.Sample(gx, gz);
            foreach (var sp in species)
            {
                if (!sp.AllowsSubstrate(me.Substrate)) continue;
                double substrateRate = me.Substrate == CoverageSubstrate.Gravel ? sp.GravelRateMultiplier : 1.0;
                if (substrateRate <= 0) continue;

                double p;
                if (sp.Form == LichenForm.Foliose)
                {
                    // Contiguity gate first (§5.3 note from lab review): a candidate needs enough occupied
                    // same-species support to keep lobes solid fingers, not porous speckle. Then tip bias.
                    int support = 0;
                    foreach (var (dx, dz) in N8)
                        if (layer.SnapshotOcc(gx + dx, gz + dz) == sp.OccSlot) support++;
                    if (support < sp.MinNeighboursToColonise) continue;

                    double openness = Openness(layer, gx, gz, sp.OpennessRadius);
                    p = 1 - Math.Exp(-sp.Lateral * Math.Pow(openness, sp.TipBiasGamma) * substrateRate * dtDays);
                }
                else
                {
                    // Crustose/fruticose: plain Eden front, w_dir == 1 (§5.2, §5.4), softened by a small per-cell noise draw.
                    double pressure = 0;
                    foreach (var (dx, dz) in N8)
                    {
                        if (layer.SnapshotOcc(gx + dx, gz + dz) != sp.OccSlot) continue;
                        pressure += layer.SnapshotB(gx + dx, gz + dz);
                    }
                    if (pressure <= 0) continue;
                    double noiseDraw = layer.Hash01(gx, gz, step, purpose: 100 + sp.OccSlot);
                    double noise = Math.Pow(0.9 + 0.2 * noiseDraw, sp.EdenNoiseExponent);
                    p = 1 - Math.Exp(-sp.Lateral * pressure * substrateRate * noise * dtDays);
                }

                if (p <= 0) continue;
                double u = layer.Hash01(gx, gz, step, purpose: sp.OccSlot);
                if (u < p)
                {
                    layer.SetCell(gx, gz, sp.OccSlot, (float)sp.SeedBiomass, 0, 0, 0, 0, 0);
                    break; // first winning species in list order claims the cell (deterministic order, not draw order)
                }
            }
        }

        // ---- 3. re-flag any pairs of newly-adjacent different species from this step's colonisation (avoids a
        // one-step race where two fronts advance into adjacent cells before either sees the other, §5.2) ----
        foreach (var (gx, gz) in candidates)
        {
            byte occ = layer.GetOcc(gx, gz);
            if (occ == 0) continue;
            foreach (var (dx, dz) in N8)
            {
                byte nOcc = layer.GetOcc(gx + dx, gz + dz);
                if (nOcc != 0 && nOcc != occ) { SetBoundaryFlag(layer, gx, gz); break; }
            }
        }

        // ---- 4. fill interior holes for foliose species (§5.3 lab review): keeps the thallus one solid piece with
        // a lobed outer boundary instead of a porous interior, which the contiguity gate alone does not guarantee
        // once a lobe closes back on itself. ----
        foreach (var sp in species)
            if (sp.Form == LichenForm.Foliose) FillInteriorHoles(layer, sp);

        layer.Advance();
    }

    /// <summary>
    /// Flood-fills the exterior of <paramref name="sp"/>'s occupied-cell bounding box from its border; any empty
    /// cell not reached is enclosed by this species and gets colonised. Cheap for a single growth-lab colony;
    /// capped to avoid runaway cost if a species ever spans a huge area.
    /// </summary>
    private static void FillInteriorHoles(CoverageLayer layer, LichenParams sp)
    {
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        bool any = false;
        foreach (var t in layer.Tiles)
        {
            for (int li = 0; li < CoverageTile.N; li++)
            {
                if (t.Occ[li] != sp.OccSlot) continue;
                any = true;
                int lx = li % CoverageSpec.TileEdge, lz = li / CoverageSpec.TileEdge;
                int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
                if (gx < minX) minX = gx;
                if (gx > maxX) maxX = gx;
                if (gz < minZ) minZ = gz;
                if (gz > maxZ) maxZ = gz;
            }
        }
        if (!any) return;
        minX--; maxX++; minZ--; maxZ++;
        int w = maxX - minX + 1, h = maxZ - minZ + 1;
        if ((long)w * h > 400_000) return; // safety cap; not expected in growth-lab scale

        var visited = new bool[w * h];
        var stack = new Stack<(int x, int z)>();
        for (int x = minX; x <= maxX; x++) { stack.Push((x, minZ)); stack.Push((x, maxZ)); }
        for (int z = minZ; z <= maxZ; z++) { stack.Push((minX, z)); stack.Push((maxX, z)); }

        while (stack.Count > 0)
        {
            var (x, z) = stack.Pop();
            if (x < minX || x > maxX || z < minZ || z > maxZ) continue;
            int idx = (z - minZ) * w + (x - minX);
            if (visited[idx]) continue;
            if (layer.GetOcc(x, z) == sp.OccSlot) continue; // this species' own wall stops the exterior flood
            visited[idx] = true;
            stack.Push((x + 1, z)); stack.Push((x - 1, z)); stack.Push((x, z + 1)); stack.Push((x, z - 1));
        }

        for (int z = minZ; z <= maxZ; z++)
        for (int x = minX; x <= maxX; x++)
        {
            int idx = (z - minZ) * w + (x - minX);
            if (visited[idx]) continue;         // reached from outside: not a hole
            if (layer.GetOcc(x, z) != 0) continue; // occupied (by this or another species): nothing to fill
            layer.SetCell(x, z, sp.OccSlot, (float)sp.SeedBiomass, 0, 0, 0, 0, 0);
        }
    }

    private static void SetBoundaryFlag(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        if (layer.TryGetTile(ti, tj, out var t) && t != null)
        {
            int li = CoverageSpec.LocalIndex(gx, gz);
            t.Flags[li] |= (byte)CoverageFlags.Boundary;
            t.Touch();
        }
    }

    private static LichenParams? SpeciesOf(IReadOnlyList<LichenParams> species, byte occ)
    {
        foreach (var sp in species) if (sp.OccSlot == occ) return sp;
        return null;
    }

    /// <summary>Fraction of empty cells in a radius-r disc around (gx,gz), read from the snapshot (§5.3).</summary>
    private static double Openness(CoverageLayer layer, int gx, int gz, int radius)
    {
        int empty = 0, total = 0;
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dz * dz > radius * radius) continue;
            total++;
            if (layer.SnapshotOcc(gx + dx, gz + dz) == 0) empty++;
        }
        return total == 0 ? 0 : (double)empty / total;
    }

    /// <summary>
    /// Cheap incremental D2E update (§1.2): a cell with any empty 8-neighbour is rim (D2E = 0); otherwise it is
    /// 1 + the minimum neighbour D2E from the snapshot, capped at 255. Local and stable; not an exact BFS, but
    /// converges to one over a few steps as the colony fills in, which is enough for dome/rim rendering cues.
    /// </summary>
    private static void UpdateD2E(CoverageLayer layer, List<(int gx, int gz, int ti, int tj, int li)> occupied)
    {
        foreach (var (gx, gz, ti, tj, li) in occupied)
        {
            if (!layer.TryGetTile(ti, tj, out var tile) || tile == null) continue;
            if (tile.Occ[li] == 0) continue;

            bool isRim = false;
            int minNeighbour = 255;
            foreach (var (dx, dz) in N8)
            {
                int nx = gx + dx, nz = gz + dz;
                if (layer.SnapshotOcc(nx, nz) == 0) { isRim = true; break; }
                int nd2e = SnapshotD2E(layer, nx, nz);
                if (nd2e < minNeighbour) minNeighbour = nd2e;
            }
            tile.D2E[li] = isRim ? (byte)0 : (byte)Math.Min(255, minNeighbour + 1);
        }
    }

    private static int SnapshotD2E(CoverageLayer layer, int gx, int gz)
    {
        var (ti, tj) = CoverageSpec.TileOf(gx, gz);
        return layer.TryGetTile(ti, tj, out var t) && t != null ? t.D2E[CoverageSpec.LocalIndex(gx, gz)] : 255;
    }
}
