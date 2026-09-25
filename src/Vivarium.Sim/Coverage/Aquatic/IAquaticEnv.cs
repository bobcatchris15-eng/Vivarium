using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Aquatic;

/// <summary>
/// The only view aquatic coverage rules get of the world (docs/overhaul/growth_models.md §15.1, §15.2),
/// mirroring <see cref="Rules.IMicroEnvSource"/>'s decoupling: the growth lab drives this with synthetic
/// fields, real hydrology (<see cref="Water.Hydrology"/>) wires it in Aq-2.
/// </summary>
public interface IAquaticEnv
{
    /// <summary>Water depth at (gx, gz), metres. Zero or negative means dry/land: no water-surface or bed
    /// coverage can exist there.</summary>
    double DepthAt(int gx, int gz);

    /// <summary>Surface (and, for the bed film, near-bed) flow velocity, m/s, in world XZ.</summary>
    Vec2 FlowAt(int gx, int gz);

    /// <summary>Incident light at the water surface, 0..1, before any surface-cover shading.</summary>
    double LightAt(int gx, int gz);

    /// <summary>Dissolved/bed nutrient availability, 0..1.</summary>
    double NutrientsAt(int gx, int gz);

    /// <summary>True where a bank, log or emergent plant blocks surface-float occupancy and advection through
    /// this cell (mass reflects off it rather than flowing through).</summary>
    bool IsObstacle(int gx, int gz);

    /// <summary>Fraction of surface light already blocked by floating cover (duckweed, floating algae mats)
    /// above this cell, 0..1 — what the bed film's light term is attenuated by before Beer-Lambert depth
    /// absorption (§15.1 algae_shading).</summary>
    double SurfaceShadeAt(int gx, int gz);
}
