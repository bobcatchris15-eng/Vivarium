namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>Life-cycle state of one plasmodium (docs/overhaul/growth_models.md §6.1). Only the state names and
/// the identity of a plasmodium as one connected component are wired here; the transition rules themselves
/// (moisture/food thresholds, sclerotium/migration/fruiting behaviour) are G8's job.</summary>
public enum PlasmodiumState
{
    Dormant,
    Foraging,
    Sclerotium,
    Migrating,
    Fruiting,
}

/// <summary>
/// One plasmodium: a connected component of Plasmodium-layer cells sharing an id and species (§6.1). Holds
/// the conserved mass pool that feeding pays into and front extension pays out of, plus the timers the (later)
/// life-cycle rules will read. Component membership itself — which cells belong to which id, and the
/// fusion/split bookkeeping — lives in <see cref="PlasmodiumColony"/> (Foraging.cs), not here: this record is
/// deliberately just the per-id state that must survive a relabel.
/// </summary>
public sealed class Plasmodium
{
    public int Id { get; }
    public int SpeciesId { get; }
    public PlasmodiumState State { get; set; }

    /// <summary>Conserved mass pool: feeding adds to it, new-cell colonisation and upkeep take from it.</summary>
    public double MassPool { get; set; }

    /// <summary>Seconds spent continuously in the current state (§6.1 transition timers T_s, T_starve, T_mig).</summary>
    public double TimeInState { get; set; }

    /// <summary>Seconds the mean wetness has been below w_s (drives the Foraging -> Sclerotium transition).</summary>
    public double TimeDry { get; set; }

    /// <summary>Seconds food inflow has been below upkeep (drives the Foraging -> starve path).</summary>
    public double TimeStarving { get; set; }

    public Plasmodium(int id, int speciesId, double initialMass, PlasmodiumState state = PlasmodiumState.Foraging)
    {
        Id = id;
        SpeciesId = speciesId;
        MassPool = initialMass;
        State = state;
    }

    /// <summary>Resets the per-state timers; called by the (future) life-cycle rule on a state transition.</summary>
    public void EnterState(PlasmodiumState next)
    {
        State = next;
        TimeInState = 0;
    }
}
