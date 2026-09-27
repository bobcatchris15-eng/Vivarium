using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Flora;

public enum DeadPlantStage : byte
{
    StandingDead = 0,
    Collapsing = 1,
    Fallen = 2,
    AdvancedDecay = 3,
}

/// <summary>Persistent post-life state for an individual vascular plant.</summary>
public sealed class DeadPlant
{
    public EntityId Id { get; set; }
    public string SpeciesId { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
    public string Cause { get; set; } = "";
    public double AgeSinceDeath { get; set; }
    public double OriginalBiomass { get; set; }
    public double RemainingBiomass { get; set; }
    public double OriginalRadius { get; set; }
    public double OriginalHeight { get; set; }
    public DeadPlantStage Stage { get; set; }
    public double StageProgress { get; set; }
    public double CollapseHeading { get; set; }

    public Vec2 Position => new(X, Z);
    public double RemainingFraction => OriginalBiomass <= 1e-12 ? 0 : MathD.Clamp01(RemainingBiomass / OriginalBiomass);
}

public sealed class DeadPlantPopulation
{
    public List<DeadPlant> Items { get; } = new();
    private readonly Dictionary<EntityId, DeadPlant> _byId = new();
    public int Version { get; private set; }
    public int Count => Items.Count;

    public DeadPlant? Get(EntityId id) => _byId.GetValueOrDefault(id);

    public void Add(DeadPlant dead)
    {
        if (_byId.ContainsKey(dead.Id)) throw new InvalidOperationException($"Dead flora already contains {dead.Id}.");
        Items.Add(dead);
        Items.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        _byId[dead.Id] = dead;
        Version++;
    }

    public bool Remove(EntityId id)
    {
        if (!_byId.Remove(id, out var dead)) return false;
        Items.Remove(dead);
        Version++;
        return true;
    }

    public void Clear()
    {
        Items.Clear();
        _byId.Clear();
        Version++;
    }
}

/// <summary>
/// Advances visible plant corpses through standing, collapse, fallen and advanced-decay stages while returning
/// their biomass to the ordinary litter/nutrient cycle. Dead plants never participate in living physiology.
/// </summary>
public sealed class DeadFloraSystem
{
    private readonly VivariumWorld _w;
    public DeadFloraSystem(VivariumWorld world) { _w = world; }

    public DeadPlant CreateFrom(FloraIndividual f, FloraSpeciesDef sp, string cause)
    {
        double fraction = f.BiomassFraction(sp);
        ulong hash = Rng.Mix(f.Id.Value, 0xD34DF10AUL);
        var dead = new DeadPlant
        {
            Id = f.Id,
            SpeciesId = f.SpeciesId,
            X = f.X,
            Z = f.Z,
            Cause = cause,
            OriginalBiomass = Math.Max(0, f.Biomass),
            RemainingBiomass = Math.Max(0, f.Biomass),
            OriginalRadius = Math.Max(sp.MinRadius, f.Radius(sp)),
            OriginalHeight = Math.Max(0.015, sp.Height * (0.45 + 0.55 * Math.Sqrt(fraction))),
            Stage = DeadPlantStage.StandingDead,
            StageProgress = 0,
            CollapseHeading = (hash % 628319UL) / 100000.0,
        };
        _w.DeadFlora.Add(dead);
        return dead;
    }

    public void Step(double dt)
    {
        if (!(dt > 0) || !double.IsFinite(dt) || _w.DeadFlora.Count == 0) return;
        var remove = new List<EntityId>();
        foreach (var dead in _w.DeadFlora.Items)
        {
            var sp = _w.Content.FloraOrThrow(dead.SpeciesId);
            dead.AgeSinceDeath += dt;
            var times = StageTimes(sp);
            double a = dead.AgeSinceDeath;
            if (a < times.Standing)
                SetStage(dead, DeadPlantStage.StandingDead, a / times.Standing);
            else if (a < times.Standing + times.Collapse)
                SetStage(dead, DeadPlantStage.Collapsing, (a - times.Standing) / times.Collapse);
            else if (a < times.Standing + times.Collapse + times.Fallen)
                SetStage(dead, DeadPlantStage.Fallen, (a - times.Standing - times.Collapse) / times.Fallen);
            else
                SetStage(dead, DeadPlantStage.AdvancedDecay,
                    (a - times.Standing - times.Collapse - times.Fallen) / times.Advanced);

            double halfLife = dead.Stage switch
            {
                DeadPlantStage.StandingDead => sp.IsTree ? 420 : sp.IsShrub ? 220 : 90,
                DeadPlantStage.Collapsing => sp.IsTree ? 260 : sp.IsShrub ? 130 : 55,
                DeadPlantStage.Fallen => sp.IsTree ? 190 : sp.IsShrub ? 95 : 32,
                _ => sp.IsTree ? 48 : sp.IsShrub ? 28 : 10,
            } * SimUnits.Day;

            double lost = dead.RemainingBiomass * (1 - Math.Exp(-Math.Log(2) / halfLife * dt));
            Transfer(dead, sp, lost);
            if (dead.Stage == DeadPlantStage.AdvancedDecay && dead.StageProgress >= 1)
            {
                Transfer(dead, sp, dead.RemainingBiomass);
                remove.Add(dead.Id);
            }
        }
        foreach (var id in remove)
        {
            _w.DeadFlora.Remove(id);
            _w.Tally.DeadFloraAssimilated++;
        }
    }

    private void Transfer(DeadPlant dead, FloraSpeciesDef sp, double amount)
    {
        amount = Math.Clamp(amount, 0, dead.RemainingBiomass);
        if (amount <= 0) return;
        dead.RemainingBiomass -= amount;

        double litter = amount * sp.LitterFraction;
        double direct = amount * (1 - sp.LitterFraction) * sp.NutrientPerBiomass;
        double coarseShare = sp.IsTree ? 0.70 : sp.IsShrub ? 0.45 :
            sp.Shape is "tussock" or "reed" ? 0.20 : 0.05;
        double coarse = litter * coarseShare;
        double fine = litter - coarse;
        if (litter > 0)
        {
            double radius = Math.Max(dead.OriginalRadius, sp.Woody?.CanopyRadius * 0.45 ?? 0);
            _w.Litter.Deposit(dead.Position, fine, coarse, radius);
            _w.Tally.CorpseToLitter += litter;
        }
        if (direct > 0)
        {
            _w.Ecology.ReturnOrganicMatter(dead.Position, 0, direct, fromFlora: true);
            _w.Tally.CorpseToNutrients += direct;
        }
    }

    private static void SetStage(DeadPlant dead, DeadPlantStage stage, double progress)
    {
        dead.Stage = stage;
        dead.StageProgress = MathD.Clamp01(progress);
    }

    private static (double Standing, double Collapse, double Fallen, double Advanced) StageTimes(FloraSpeciesDef sp)
    {
        if (sp.IsTree) return (90 * SimUnits.Day, 8 * SimUnits.Day, 360 * SimUnits.Day, 30 * SimUnits.Day);
        if (sp.IsShrub) return (40 * SimUnits.Day, 5 * SimUnits.Day, 120 * SimUnits.Day, 18 * SimUnits.Day);
        if (sp.Shape == "tussock") return (35 * SimUnits.Day, 4 * SimUnits.Day, 55 * SimUnits.Day, 12 * SimUnits.Day);
        if (sp.Shape is "fern" or "veilfern") return (5 * SimUnits.Day, 2 * SimUnits.Day, 18 * SimUnits.Day, 8 * SimUnits.Day);
        return (10 * SimUnits.Day, 2 * SimUnits.Day, 28 * SimUnits.Day, 10 * SimUnits.Day);
    }
}
