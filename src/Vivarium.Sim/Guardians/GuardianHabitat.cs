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

    public GuardianHabitat(PilotTreeState tree, HexDomain domain, VivariumWorld? world = null)
    {
        _world = world;
        _domain = domain;
        RootPaths = tree.Roots;

        // Inward normal from corner vertex
        var outward = tree.Anchor.Normalized();
        var inward = -outward;

        // Trunk base anchor
        var trunkBaseXZ = domain.ClampInside(tree.Anchor + inward * (tree.Def.TrunkRadius + 0.85), 0.4);
        HomeAnchor = new Vec3(trunkBaseXZ.X, tree.BaseHeight + 0.05, trunkBaseXZ.Z);

        var rest = new List<Vec3> { HomeAnchor };

        // Rest sites at major root buttress forks and root shelves
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
        RestSites = Array.AsReadOnly(rest.ToArray());

        // Forage hotspots in rich litter zones below the canopy
        var forage = new List<Vec2>();
        for (int i = 1; i <= 4; i++)
        {
            double angle = (i * Math.PI / 5.0) - (Math.PI / 2.0);
            var dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
            var p = tree.Anchor + inward * (tree.Def.TrunkRadius + 1.2) + dir * 0.8;
            if (domain.Contains(p)) forage.Add(p);
        }
        if (forage.Count == 0) forage.Add(trunkBaseXZ);
        ForageHotspots = Array.AsReadOnly(forage.ToArray());

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

        // Scent marking targets (HomeAnchor, root sites, tree base)
        var scentTargets = new List<Vec3> { HomeAnchor };
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
        foreach (var site in RestSites)
        {
            if (!scentTargets.Any(l => (l - site).LengthSq < 0.25))
                scentTargets.Add(site);
        }
        ScentMarkTargets = Array.AsReadOnly(scentTargets.ToArray());
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
        return _world.Fields.Light[cell] > 0.45;
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
                if (_world.Fields.Light[cell] > 0.45)
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
