namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>Life-cycle state of one plasmodium (docs/overhaul/growth_models.md §6.1). Only the state names and
/// the identity of a plasmodium as one connected component are wired here; the transition rules themselves
/// (moisture/food thresholds, sclerotium/migration/fruiting behaviour) are G8's job.</summary>
public enum PlasmodiumState
{
    Dormant,
    Foraging,
    Sclerotium,
    Fruiting,
}

/// <summary>
/// One plasmodium: a connected component of Plasmodium-layer cells sharing an id and species (§6.1). Holds
/// the timers the life-cycle rules read. Biomass itself is no longer a per-organism pool (§6R item 1): each
/// occupied cell carries its own cytoplasm mass, tracked by <see cref="PlasmodiumColony"/> keyed by cell
/// coordinate, so it survives a relabel automatically without any fusion/split bookkeeping here. Component
/// membership itself — which cells belong to which id — also lives in <see cref="PlasmodiumColony"/> (Foraging.cs).
/// </summary>
public sealed class Plasmodium
{
    public int Id { get; }
    public int SpeciesId { get; }
    public PlasmodiumState State { get; set; }

    /// <summary>Seconds spent continuously in the current state (§6.1 transition timers T_s, T_starve, T_mig).</summary>
    public double TimeInState { get; set; }

    /// <summary>Seconds the mean wetness has been below w_s (drives the Foraging -> Sclerotium transition).</summary>
    public double TimeDry { get; set; }

    /// <summary>Seconds food inflow has been below upkeep (drives the Foraging -> starve path).</summary>
    public double TimeStarving { get; set; }

    public Plasmodium(int id, int speciesId, PlasmodiumState state = PlasmodiumState.Foraging)
    {
        Id = id;
        SpeciesId = speciesId;
        State = state;
    }

    /// <summary>Resets the per-state timers; called by the (future) life-cycle rule on a state transition.</summary>
    public void EnterState(PlasmodiumState next)
    {
        State = next;
        TimeInState = 0;
    }
}
