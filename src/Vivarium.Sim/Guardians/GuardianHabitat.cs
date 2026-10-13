using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Habitat and navigation anchor points associated with an island's Pilot Tree.
/// Provides safe rest perches, climbable root routes, and forage zones.
/// </summary>
public sealed class GuardianHabitat
{
    public Vec3 HomeAnchor { get; }
    public IReadOnlyList<Vec3> RestSites { get; }
    public IReadOnlyList<Vec2> ForageHotspots { get; }
    public IReadOnlyList<PilotRootPath> RootPaths { get; }

    public GuardianHabitat(PilotTreeState tree, HexDomain domain)
    {
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
}
