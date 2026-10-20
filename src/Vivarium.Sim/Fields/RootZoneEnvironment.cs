using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Fields;

/// <summary>
/// Non-authoritative terrain-derived substrate tendencies. These are inferred
/// from local slope/convergence, NOT a second geology or hydrology simulation.
/// Terrain edits invalidate the cache; moisture remains owned by Hydrology.
/// </summary>
public sealed class RootZoneEnvironment
{
    private readonly Heightfield _terrain;
    private readonly GridSpec _grid;
    private int _terrainVersion = int.MinValue;
    public ScalarField RetainedTopsoilDepth { get; }
    public ScalarField DrainagePotential { get; }

    public RootZoneEnvironment(GridSpec grid, Heightfield terrain)
    {
        _grid = grid;
        _terrain = terrain;
        RetainedTopsoilDepth = new ScalarField("retained_topsoil_depth_m", grid, 0, 0, 1.5);
        DrainagePotential = new ScalarField("drainage_potential", grid, 0.5, 0, 1);
    }

    public void EnsureCurrent()
    {
        if (_terrainVersion == _terrain.Version) return;
        double ds = _grid.CellSize;
        foreach (int idx in _grid.DomainCells)
        {
            var p = _grid.CellCenter(idx);
            double h = _terrain.Height(p);
            double neighborSum = 0;
            int neighbors = 0;
            int x = idx % _grid.Nx, z = idx / _grid.Nx;
            void AddNeighbor(int nx, int nz)
            {
                if (!_grid.InDomain(nx, nz)) return;
                neighborSum += _terrain.Height(_grid.CellCenter(nx, nz));
                neighbors++;
            }
            AddNeighbor(x - 1, z); AddNeighbor(x + 1, z);
            AddNeighbor(x, z - 1); AddNeighbor(x, z + 1);
            double convergence = neighbors > 0 ? (neighborSum / neighbors - h) / ds : 0;
            double hollow = MathD.Clamp01(convergence / 0.22);
            double ridge = MathD.Clamp01(-convergence / 0.22);
            double steepness = MathD.Clamp01(Math.Tan(Math.Min(_terrain.Slope(p), 1.4)) / 0.8);
            // Steep convex surfaces shed fines; sheltered concavities retain them.
            // This is an effective topsoil capacity, not measured bedrock depth.
            double retained = 0.12 + 0.35 * (1 - steepness) + 0.48 * hollow - 0.11 * ridge;
            RetainedTopsoilDepth[idx] = Math.Min(Math.Max(0.025, retained), Math.Max(0.025, h - _terrain.Bottom));
            // High values drain readily; hollows and low-gradient surfaces remain wetter.
            DrainagePotential[idx] = MathD.Clamp01(0.34 + 0.48 * steepness + 0.14 * ridge - 0.30 * hollow);
        }
        _terrainVersion = _terrain.Version;
    }

    /// <summary>
    /// Root-zone air availability: more saturation removes air-filled pore
    /// space; drainage helps replenish it. Saturated habitats remain viable
    /// for species with low-oxygen optima.
    /// </summary>
    public double OxygenAt(Vec2 p, double moisture)
    {
        EnsureCurrent();
        double drainage = DrainagePotential.Sample(p);
        return MathD.Clamp01((1 - MathD.Clamp01(moisture) * MathD.Clamp01(moisture)) *
                             (0.50 + 0.50 * drainage));
    }
}
