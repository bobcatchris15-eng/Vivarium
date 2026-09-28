using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

/// <summary>
/// Anonymous low herbaceous background. This is not a flora population: it owns no organisms, consumes no
/// resources and exerts no competition. It merely fills suitable negative space until named flora occupy it.
/// </summary>
public sealed class AmbientGroundCoverSystem
{
    private readonly VivariumWorld _w;
    private readonly List<FloraIndividual> _nearby = new();
    public double[] Cover { get; }
    public long Revision { get; private set; }

    public AmbientGroundCoverSystem(VivariumWorld world)
    {
        _w = world;
        Cover = new double[world.Grid.Count];
    }

    public void InitializeFromHabitat()
    {
        foreach (int idx in _w.Grid.DomainCells) Cover[idx] = TargetAt(idx);
        Revision++;
    }

    public void Step(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt)) return;
        bool changed = false;
        foreach (int idx in _w.Grid.DomainCells)
        {
            double target = TargetAt(idx);
            double before = Cover[idx];
            // Named plants and flooding clear anonymous filler quickly; vacant ground greens over more slowly.
            double halfLife = target < before ? 0.8 * SimUnits.Day : 4.5 * SimUnits.Day;
            double alpha = 1 - Math.Exp(-Math.Log(2) * dt / halfLife);
            double after = before + (target - before) * alpha;
            if (Math.Abs(after - before) > 1e-6) { Cover[idx] = after; changed = true; }
        }
        if (changed) Revision++;
    }

    public double TargetAt(int idx)
    {
        if (!_w.Grid.InDomain(idx)) return 0;
        var p = _w.Grid.CellCenter(idx);
        if (_w.SubstrateAtCell(p) != Substrate.Soil || _w.Water.DepthAt(p) > 0.006) return 0;

        double moisture = MathD.Clamp01(_w.Fields.Moisture.Values[idx]);
        double light = MathD.Clamp01(_w.FloraSystem.EffectiveLightCell(idx));
        double nutrients = MathD.Clamp01(_w.Fields.Nutrients.Values[idx] / Math.Max(1e-9, _w.Content.Ecology.NutrientMax));

        // Broadly tolerant by design. This is background fabric, not a species trying to win a niche.
        double moistureFit = moisture < 0.08 ? moisture / 0.08
            : moisture > 0.93 ? Math.Max(0, (1 - moisture) / 0.07)
            : 1.0;
        double lightFit = 0.78 + 0.22 * MathD.Clamp01((light - 0.05) / 0.55);
        double nutrientFit = 0.90 + 0.10 * nutrients;
        double litter = _w.Litter.FineMass[idx] + _w.Litter.CoarseMass[idx];
        double litterFit = 1.0 - 0.38 * MathD.Clamp01(litter * 5.0);

        // Stable per-cell patchiness keeps the field organic without turning it into a grid or a population.
        double noise = 0.90 + 0.10 * Rng.HashUnit(_w.Seed, Hash.Fnv1a64("ambient-ground"), (ulong)idx);
        double target = MathD.Clamp01((0.84 + 0.14 * moistureFit) * lightFit * nutrientFit * litterFit * noise);

        // Named plants own their silhouette. Anonymous cover is explicitly absent inside their visual footprint,
        // with a soft transition around the edge. Trees only clear their base, not the entire canopy.
        _nearby.Clear();
        _w.Flora.Index.Query(p, 1.35, _nearby);
        double exclusion = 1.0;
        foreach (var f in _nearby)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            if (sp.Archetype != "plant") continue;
            double visual = f.Radius(sp);
            double radius = (sp.IsTree ? Math.Min(0.46, 0.16 + visual * 0.35)
                : sp.IsShrub ? Math.Min(0.95, visual * 1.08 + 0.08)
                : Math.Min(0.55, visual * 1.20 + 0.06)) + 0.14;
            double dist = Vec2.Distance(p, f.Position);
            double edge = MathD.Clamp01((dist - radius) / 0.12);
            edge = edge * edge * (3 - 2 * edge);
            exclusion = Math.Min(exclusion, edge);
            if (exclusion <= 0) break;
        }

        // A still-recognizable corpse also keeps the filler out of its immediate footprint.
        foreach (var dead in _w.DeadFlora.Items)
        {
            if (Vec2.DistanceSq(p, dead.Position) > 0.8 * 0.8) continue;
            double radius = Math.Min(0.7, dead.OriginalRadius * (0.7 + 0.3 * dead.RemainingFraction));
            double edge = MathD.Clamp01((Vec2.Distance(p, dead.Position) - radius) / 0.16);
            exclusion = Math.Min(exclusion, edge * edge * (3 - 2 * edge));
        }

        return target * exclusion;
    }

    public double[] Export()
    {
        var a = new double[_w.Grid.DomainCells.Length];
        for (int k = 0; k < a.Length; k++) a[k] = Cover[_w.Grid.DomainCells[k]];
        return a;
    }

    public void Restore(double[] values)
    {
        if (values.Length != _w.Grid.DomainCells.Length)
            throw new InvalidDataException($"ambient cover expects {_w.Grid.DomainCells.Length} values, got {values.Length}");
        for (int k = 0; k < values.Length; k++)
        {
            double v = values[k];
            if (!double.IsFinite(v) || v < 0 || v > 1)
                throw new InvalidDataException($"ambient cover value {k} is invalid ({v})");
            Cover[_w.Grid.DomainCells[k]] = v;
        }
        Revision++;
    }
}
