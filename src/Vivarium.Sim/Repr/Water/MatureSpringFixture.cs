using System;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Repr.Water;

/// <summary>Deterministic physical terrain and source shared by solver diagnostics and native review.</summary>
public sealed class MatureSpringFixture
{
    public WorldDescriptor Descriptor { get; }
    public Heightfield Terrain { get; }
    public SurfaceFluidDynamics Dynamics { get; }
    public Spring Spring { get; }
    public WaterBudget Budget { get; } = new();
    public bool OpenSpillway { get; }

    private MatureSpringFixture(bool open)
    {
        OpenSpillway = open;
        Descriptor = new WorldDescriptor { Diameter = 5, CellSize = .1, Seed = 804584441,
            PilotTreeId = "none", GuardianId = "none",
            Terrain = new TerrainProfile { BaseHeight = .3, Relief = 0, MicroRelief = 0 },
            Water = new WaterConfig { WaterTable = 0, Evaporation = 0, Infiltration = 0 } };
        var domain = new HexDomain(Descriptor.Diameter);
        Terrain = Heightfield.Generate(Descriptor, domain);
        ApplyTerrain(Terrain, open);
        Dynamics = new SurfaceFluidDynamics(domain, Terrain.Height);
        Dynamics.WaterTable = Descriptor.Water.WaterTable;
        Spring = new Spring { Id = new EntityId(1), X = -.65, Z = 0, Discharge = 2000.0 / 3600000.0 };
    }

    public static MatureSpringFixture Create(bool openSpillway) => new(openSpillway);

    /// <summary>Apply to the matching world heightfield before rebuilding derived terrain and restoring state.</summary>
    public static void ApplyTerrain(Heightfield terrain, bool openSpillway)
    {
        terrain.BeginEdit();
        for (int z = 0; z < terrain.Nz; z++)
        for (int x = 0; x < terrain.Nx; x++)
        {
            var p = terrain.VertexPos(x, z);
            // A raised bank with a small bowl. The inlet slopes into it; the open
            // variant has a low, downhill outlet to the physical island boundary.
            double bowl = .08 + 1.2 * (p.X * p.X + p.Z * p.Z);
            double inlet = .12 - .16 * p.X + 5 * p.Z * p.Z + 5 * Math.Pow(Math.Max(0, p.X + .2), 2);
            double h = Math.Min(.3, Math.Min(bowl, inlet));
            if (openSpillway && p.X > .2)
                h = Math.Min(h, .12 - .04 * (p.X - .2) + 5 * p.Z * p.Z);
            terrain.H[terrain.VertexIndex(x, z)] = h;
        }
        terrain.Touch();
    }

    /// <summary>Seed real still water; no velocity or face flux is fabricated.</summary>
    public double SeedPool(double cubicMetres = .01) => Dynamics.AddWater(new Vec2(0, 0), .14, cubicMetres);

    /// <summary>
    /// Bounded maturity probe. A result explicitly reports whether head, established
    /// fraction and boundary-outflow windows met the requested criteria.
    /// </summary>
    public MatureSpringResult Mature(double maxSeconds = 60, double windowSeconds = 2)
    {
        double priorHead = double.NaN, priorOutflow = 0;
        int stableWindows = 0;
        string report = "no measurement";
        while (Dynamics.Time + windowSeconds <= maxSeconds + 1e-9)
        {
            Dynamics.Advance(windowSeconds, new[] { Spring }, Budget);
            var snapshot = Dynamics.CaptureRenderState();
            var pool = snapshot.Cells.Where(c => c.Position.Length < .15 && c.Volume > 0).ToArray();
            double head = pool.Length == 0 ? double.NaN : pool.Average(c => c.Head);
            double established = snapshot.Cells.Length == 0 ? 0 : (double)snapshot.EstablishedCells / snapshot.Cells.Length;
            double outflow = (Budget.BoundaryOutflow - priorOutflow) / windowSeconds;
            bool headStable = double.IsFinite(priorHead) && double.IsFinite(head) && Math.Abs(head - priorHead) < .001;
            bool outletStable = !OpenSpillway || outflow >= Spring.Discharge * .8;
            stableWindows = headStable && established >= .25 && outletStable ? stableWindows + 1 : 0;
            report = $"time={Dynamics.Time:F2}s poolHead={head:F6} established={established:F3} outflow={outflow:E6}m3/s stableWindows={stableWindows}";
            if (stableWindows >= 2) break;
            priorHead = head; priorOutflow = Budget.BoundaryOutflow;
        }
        return new MatureSpringResult(Dynamics.Export(), Budget, stableWindows >= 2, report);
    }
}

public sealed record MatureSpringResult(SurfaceFluidState State, WaterBudget Budget, bool IsMature, string Report);
