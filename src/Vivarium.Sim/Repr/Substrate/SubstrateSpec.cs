using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Inputs for a layered substrate. The same code serves a test cube and the real island.
public sealed class SubstrateSpec
{
    public ulong Seed;
    public Vec2 Origin;
    public int Nx, Nz;
    public double Cell = 0.05;                       // m column size
    public Func<Vec2, double> SurfaceHeight = _ => 0; // m, top of ground
    public Func<Vec2, double> DrainagePotential = _ => 0.5; // 0 waterlogged .. 1 free draining
    public Func<Vec2, double> OrganicInput = _ => 0.5;      // kg/m2/yr litter fall proxy 0..1
    public double Depth = 1.0;                       // m modelled below the lowest surface point
    public double MaxLatticeSpacing = 0.01;          // m fine bedrock lattice target spacing
    public Func<Vec2, double>? BedrockDepth;         // optional local depth to bedrock (m below surface); null uses analytic cube plane/knoll

    /// Test cube: 1 x 1 m footprint, 1 m deep. Gentle 8 degree slope along X, a small hollow,
    /// drainage gradient across X (wet side at low X, dry side at high X), organic input higher on the wet side.
    public static SubstrateSpec TestCube(ulong seed)
    {
        const double slope = 0.14054; // tan(8 deg)
        const double cell = 0.05;
        return new SubstrateSpec
        {
            Seed = seed,
            Origin = new Vec2(0, 0),
            Nx = 20,
            Nz = 20,
            Cell = cell,
            Depth = 1.0,
            SurfaceHeight = p =>
            {
                double x = p.X, z = p.Z;
                double h = 0.5 + slope * (x - 0.5);
                double dx = x - 0.5, dz = z - 0.5;
                h -= 0.04 * Math.Exp(-(dx * dx + dz * dz) / (2 * 0.12 * 0.12)); // small hollow
                return h;
            },
            DrainagePotential = p => MathD.Clamp01(p.X),      // low X = waterlogged wet side, high X = free draining
            OrganicInput = p => MathD.Clamp01(0.3 + 0.5 * (1.0 - p.X)),
        };
    }
}
