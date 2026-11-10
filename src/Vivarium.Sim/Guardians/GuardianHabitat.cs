using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Habitat and navigation anchor points associated with an island's Pilot Tree.
/// Provides safe rest perches, climbable root routes, forage zones, water access,
/// and scent marking landmarks.
/// </summary>
public sealed class GuardianHabitat
{
    private readonly VivariumWorld? _world;
    private readonly HexDomain _domain;

    public Vec3 HomeAnchor { get; }
    public IReadOnlyList<Vec3> RestSites { get; }
    public IReadOnlyList<Vec2> ForageHotspots { get; }
    public IReadOnlyList<PilotRootPath> RootPaths { get; }
    public IReadOnlyList<Vec2> WaterAccessPoints { get; }
    public IReadOnlyList<Vec2> ShallowWadingZones { get; }
    public IReadOnlyList<Vec3> ScentMarkTargets { get; }
    public List<Vec3> BaskingSites { get; } = new();

    public GuardianHabitat(PilotTreeState? tree, HexDomain domain, VivariumWorld? world = null)
    {
        _world = world;
        _domain = domain;
        RootPaths = tree?.Roots ?? (IReadOnlyList<PilotRootPath>)Array.Empty<PilotRootPath>();

        Vec2 trunkBaseXZ;
        double baseHeight;
        if (tree != null)
        {
            // Inward normal from corner vertex
            var outward = tree.Anchor.Normalized();
            var inward = -outward;

            // Trunk base anchor
            trunkBaseXZ = domain.ClampInside(tree.Anchor + inward * (tree.Def.TrunkRadius + 0.85), 0.4);
            baseHeight = tree.BaseHeight + 0.05;
        }
        else
        {
            trunkBaseXZ = Vec2.Zero;
            if (world != null && world.Water != null && world.Grid != null &&
                world.Water.OpenWaterDepth(world.Grid.NearestDomainCell(trunkBaseXZ)) > 0.05)
            {
                double bestDist = double.PositiveInfinity;
                foreach (int cell in world.Grid.DomainCells)
                {
                    if (world.Water.OpenWaterDepth(cell) < 0.02)
                    {
                        var c = world.Grid.CellCenter(cell);
                        double d = c.LengthSq;
                        if (d < bestDist) { bestDist = d; trunkBaseXZ = c; }
                    }
                }
            }
            baseHeight = (world != null ? world.SurfaceHeight(trunkBaseXZ) : 0.0) + 0.05;
        }
        HomeAnchor = new Vec3(trunkBaseXZ.X, baseHeight, trunkBaseXZ.Z);

        var rest = new List<Vec3> { HomeAnchor };

        // Rest sites at major root buttress forks and root shelves, or open ground
        if (tree != null)
        {
            foreach (var root in tree.Roots)
            {
                if (root.Samples.Count > 4)
                {
                    var s = root.Samples[Math.Min(4, root.Samples.Count - 1)];
                    var position = domain.ClampInside(s.Position, 0.4);
                    if (!tree.BlocksTrunk(position, 0.35))
                        rest.Add(new Vec3(position.X, s.GroundY + s.Height * 0.8, position.Z));
                }
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                double angle = i * Math.PI * 0.5;
                var p = domain.ClampInside(trunkBaseXZ + new Vec2(Math.Cos(angle) * 2.0, Math.Sin(angle) * 2.0), 0.4);
                double y = (world != null ? world.SurfaceHeight(p) : 0.0) + 0.05;
                rest.Add(new Vec3(p.X, y, p.Z));
            }
        }
        if (world?.Props != null)
        {
            foreach (var rock in world.Props.Rocks)
            {
                var shelterP = domain.ClampInside(rock.Position + new Vec2(rock.FootprintRadius + 0.35, 0), 0.3);
                if (world.Water == null || world.Water.OpenWaterDepth(shelterP) <= 0.02)
                {
                    double y = world.SurfaceHeight(shelterP);
                    rest.Add(new Vec3(shelterP.X, y, shelterP.Z));
                }
            }
            foreach (var log in world.Props.Logs)
            {
                var shelterP = domain.ClampInside(log.Position + new Vec2(0, log.Radius + 0.35), 0.3);
                if (world.Water == null || world.Water.OpenWaterDepth(shelterP) <= 0.02)
                {
                    double y = world.SurfaceHeight(shelterP);
                    rest.Add(new Vec3(shelterP.X, y, shelterP.Z));
                }
            }
        }
        RestSites = Array.AsReadOnly(rest.ToArray());

        // Forage hotspots in rich litter zones below the canopy or island terrain
        var forage = new List<Vec2>();
        if (tree != null)
        {
            var outward = tree.Anchor.Normalized();
            var inward = -outward;
            for (int i = 1; i <= 4; i++)
            {
                double angle = (i * Math.PI / 5.0) - (Math.PI / 2.0);
                var dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
                var p = tree.Anchor + inward * (tree.Def.TrunkRadius + 1.2) + dir * 0.8;
                if (domain.Contains(p)) forage.Add(p);
            }
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                double angle = i * Math.PI / 3.0;
                var p = domain.ClampInside(trunkBaseXZ + new Vec2(Math.Cos(angle) * 2.5, Math.Sin(angle) * 2.5), 0.4);
                if (domain.Contains(p)) forage.Add(p);
            }
        }
        if (forage.Count == 0) forage.Add(trunkBaseXZ);


        // Water access points & shallow wading zones
        var waterAccess = new List<Vec2>();
        var wading = new List<Vec2>();
        if (world != null && world.Water != null && world.Grid != null)
        {
            int nx = world.Grid.Nx;
            foreach (int cell in world.Grid.DomainCells)
            {
                double depth = world.Water.OpenWaterDepth(cell);
                if (depth >= 0.01 && depth <= 0.15)
                {
                    wading.Add(domain.ClampInside(world.Grid.CellCenter(cell), 0.2));
                }

                if (depth > 0.02)
                {
                    int i = cell % nx;
                    int j = cell / nx;
                    bool hasAdjacentLand = false;
                    int[] ni = { i - 1, i + 1, i, i };
                    int[] nj = { j, j, j - 1, j + 1 };
                    for (int k = 0; k < 4; k++)
                    {
                        if (world.Grid.InDomain(ni[k], nj[k]))
                        {
                            int neighbor = world.Grid.Index(ni[k], nj[k]);
                            if (world.Water.OpenWaterDepth(neighbor) < 0.01)
                            {
                                hasAdjacentLand = true;
                                break;
                            }
                        }
                    }
                    if (hasAdjacentLand)
                    {
                        waterAccess.Add(domain.ClampInside(world.Grid.CellCenter(cell), 0.2));
                    }
                }
            }
        }
        WaterAccessPoints = Array.AsReadOnly(waterAccess.ToArray());
        ShallowWadingZones = Array.AsReadOnly(wading.ToArray());

        if (world?.Props != null)
        {
            foreach (var log in world.Props.Logs)
            {
                var lp = domain.ClampInside(log.Position, 0.3);
                if (!forage.Any(f => (f - lp).LengthSq < 0.6))
                    forage.Add(lp);
            }
        }
        foreach (var w in wading)
        {
            if (!forage.Any(f => (f - w).LengthSq < 0.8))
            {
                forage.Add(w);
                if (forage.Count >= 20) break;
            }
        }
        ForageHotspots = Array.AsReadOnly(forage.ToArray());

        // Basking sites (sunny tops/edges of rocks, logs, or warm high dry ground)
        if (world?.Props != null)
        {
            foreach (var rock in world.Props.Rocks)
            {
                var p = new Vec2(rock.X, rock.Z);
                if (domain.Contains(p) && (world.Water == null || world.Water.OpenWaterDepth(p) <= 0.01))
                {
                    double top = rock.TopAt(p);
                    double y = double.IsNaN(top) ? rock.Y + rock.SizeY : top;
                    BaskingSites.Add(new Vec3(rock.X, y, rock.Z));
                }
            }
            foreach (var log in world.Props.Logs)
            {
                var p = new Vec2(log.X, log.Z);
                if (domain.Contains(p) && (world.Water == null || world.Water.OpenWaterDepth(p) <= 0.01))
                {
                    double top = log.TopAt(p);
                    double y = double.IsNaN(top) ? log.Y + log.Radius : top;
                    BaskingSites.Add(new Vec3(log.X, y, log.Z));
                }
            }
        }
        if (world?.Grid != null)
        {
            foreach (int cell in world.Grid.DomainCells)
            {
                var p = world.Grid.CellCenter(cell);
                if (world.Water == null || world.Water.OpenWaterDepth(cell) <= 0.01)
                {
                    if (IsSunlit(p))
                    {
                        double y = world.SurfaceHeight(p);
                        if (!BaskingSites.Any(b => (b.XZ - p).LengthSq < 1.0))
                        {
                            BaskingSites.Add(new Vec3(p.X, y, p.Z));
                            if (BaskingSites.Count >= 12) break;
                        }
                    }
                }
            }
        }
        if (BaskingSites.Count == 0)
        {
            BaskingSites.Add(HomeAnchor);
        }

        // Scent marking targets (HomeAnchor, root sites, tree base)
        var scentTargets = new List<Vec3> { HomeAnchor };
        if (tree != null)
        {
            var treeBase = new Vec3(trunkBaseXZ.X, tree.BaseHeight, trunkBaseXZ.Z);
            if ((treeBase - HomeAnchor).LengthSq > 0.01)
                scentTargets.Add(treeBase);

            foreach (var root in tree.Roots)
            {
                if (root.Samples.Count > 0)
                {
                    var s = root.Samples[^1];
                    var p = domain.ClampInside(s.Position, 0.4);
                    var target = new Vec3(p.X, s.GroundY + s.Height * 0.5, p.Z);
                    if (!scentTargets.Any(l => (l - target).LengthSq < 0.25))
                        scentTargets.Add(target);
                }
            }
        }
        foreach (var site in RestSites)
        {
            if (!scentTargets.Any(l => (l - site).LengthSq < 0.25))
                scentTargets.Add(site);
        }
        ScentMarkTargets = Array.AsReadOnly(scentTargets.ToArray());
    }

    public Vec3? NearestBaskingSite(Vec3 current)
    {
        if (BaskingSites.Count == 0) return null;
        Vec3 best = BaskingSites[0];
        double bestDistSq = (current - best).LengthSq;
        for (int i = 1; i < BaskingSites.Count; i++)
        {
            double d = (current - BaskingSites[i]).LengthSq;
            if (d < bestDistSq) { bestDistSq = d; best = BaskingSites[i]; }
        }
        return best;
    }

    public Vec3 NearestRestSite(Vec3 current)
    {
        Vec3 best = HomeAnchor;
        double bestDistSq = double.PositiveInfinity;
        foreach (var site in RestSites)
        {
            double d = (current - site).LengthSq;
            if (d < bestDistSq) { bestDistSq = d; best = site; }
        }
        return best;
    }

    public Vec2 SelectForageTarget(ulong seed, ulong counter)
    {
        if (ForageHotspots.Count == 0) return new Vec2(HomeAnchor.X, HomeAnchor.Z);
        var rng = Rng.Keyed(seed, "guardian.habitat.forage", counter);
        return ForageHotspots[rng.NextInt(ForageHotspots.Count)];
    }

    public Vec2? NearestWaterAccessPoint(Vec2 pos)
    {
        if (WaterAccessPoints.Count == 0) return null;
        Vec2 best = WaterAccessPoints[0];
        double bestDistSq = (pos - best).LengthSq;
        for (int i = 1; i < WaterAccessPoints.Count; i++)
        {
            double d = (pos - WaterAccessPoints[i]).LengthSq;
            if (d < bestDistSq) { bestDistSq = d; best = WaterAccessPoints[i]; }
        }
        return best;
    }

    public Vec2? NearestWadingZone(Vec2 pos)
    {
        if (ShallowWadingZones.Count == 0) return null;
        Vec2 best = ShallowWadingZones[0];
        double bestDistSq = (pos - best).LengthSq;
        for (int i = 1; i < ShallowWadingZones.Count; i++)
        {
            double d = (pos - ShallowWadingZones[i]).LengthSq;
            if (d < bestDistSq) { bestDistSq = d; best = ShallowWadingZones[i]; }
        }
        return best;
    }

    public bool IsSunlit(Vec2 pos)
    {
        if (_world?.Fields?.Light == null || _world.Grid == null) return true;
        int cell = _world.Grid.NearestDomainCell(pos);
        if (cell < 0) return true;
        return LightAt(cell) > 0.45;
    }

    // Canopy-aware light, matching GuardianNeeds thermoregulation.
    private double LightAt(int cell)
    {
        return _world!.FloraSystem != null ? _world.FloraSystem.EffectiveLightCell(cell) : _world.Fields!.Light[cell];
    }

    public Vec2 NearestSunlitPoint(Vec2 pos)
    {
        if (IsSunlit(pos)) return pos;
        if (_world?.Fields?.Light != null && _world.Grid != null)
        {
            double bestDistSq = double.PositiveInfinity;
            int bestCell = -1;
            foreach (int cell in _world.Grid.DomainCells)
            {
                if (LightAt(cell) > 0.45)
                {
                    var center = _world.Grid.CellCenter(cell);
                    double d = (pos - center).LengthSq;
                    if (d < bestDistSq)
                    {
                        bestDistSq = d;
                        bestCell = cell;
                    }
                }
            }
            if (bestCell >= 0)
                return _domain.ClampInside(_world.Grid.CellCenter(bestCell), 0.2);
        }
        return new Vec2(HomeAnchor.X, HomeAnchor.Z);
    }

    public double SubstrateSoftness(Vec2 pos)
    {
        if (_world == null) return 0.9;
        var sub = _world.SubstrateAt(pos);
        return sub switch
        {
            Substrate.Soil => 0.9,
            Substrate.Gravel => 0.7,
            Substrate.Rock => 0.1,
            _ => 0.7
        };
    }
}
