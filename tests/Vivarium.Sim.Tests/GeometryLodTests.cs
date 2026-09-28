using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class GeometryLodTests
{
    private readonly ITestOutputHelper _out;
    public GeometryLodTests(ITestOutputHelper output) => _out = output;

    private static ulong SeedOf(Vivarium.Sim.Content.FloraSpeciesDef sp) =>
        Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);

    private static MeshData Tier(Vivarium.Sim.Content.FloraSpeciesDef sp, int t) =>
        sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, SeedOf(sp), true, t) : OrganismMeshes.FloraTier(sp, SeedOf(sp), t);

    [Fact]
    public void EveryFloraSpeciesHasThreeParametricTiersWithinBudget()
    {
        var failures = new List<string>();
        foreach (var sp in TestUtil.Content.Flora)
        {
            var tiers = Enumerable.Range(0, OrganismMeshes.FloraLodTiers).Select(t => Tier(sp, t).TriangleCount).ToArray();
            int top = tiers[0], mid = tiers[1], low = tiers[2];
            _out.WriteLine($"{sp.Id}={top}/{mid}/{low}");
            if (top <= 0 || mid <= 0 || low <= 0) failures.Add($"{sp.Id}: empty tier {top}/{mid}/{low}");
            if (top > OrganismMeshes.FloraTopTriangleCap) failures.Add($"{sp.Id}: top {top} over cap");
            if (mid > top || low > mid) failures.Add($"{sp.Id}: tiers not monotone {top}/{mid}/{low}");
            // Ratios (mid <= 40%, low <= 15% or under 1k tris) only bind where the minimum tessellation (5-sided tubes, 2-segment blades) leaves room.
            if (top >= 3000)
            {
                if (mid > top * 0.40) failures.Add($"{sp.Id}: mid {mid} of {top}");
                if (low > top * 0.15 && low > 1000) failures.Add($"{sp.Id}: low {low} of {top}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void NoTierHasDegenerateTriangles()
    {
        var failures = new List<string>();
        foreach (var sp in TestUtil.Content.Flora)
            for (int t = 0; t < OrganismMeshes.FloraLodTiers; t++)
            {
                var m = Tier(sp, t);
                int bad = 0;
                for (int k = 0; k < m.TriangleCount; k++)
                {
                    var a = m.Position(m.Indices[k * 3]);
                    var cross = (m.Position(m.Indices[k * 3 + 1]) - a).Cross(m.Position(m.Indices[k * 3 + 2]) - a);
                    if (!(cross.Length > 1e-13)) bad++;
                }
                if (bad > 0) failures.Add($"{sp.Id} tier {t}: {bad} zero-area triangles");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryTierOfWoodySpeciesKeepsSolidTrunk()
    {
        var failures = new List<string>();
        foreach (var sp in TestUtil.Content.Flora.Where(s => s.Woody != null))
            for (int t = 0; t < OrganismMeshes.FloraLodTiers; t++)
            {
                var m = Tier(sp, t);
                if (m.Structural.Count == 0) { failures.Add($"{sp.Id} tier {t}: no trunk tubes"); continue; }
                int minSides = m.Structural.Min(s => s.Sides);
                if (minSides < 5) failures.Add($"{sp.Id} tier {t}: tube with {minSides} sides");
                // The trunk (largest tube) must survive every tier as full rings of >= 5 sides.
                var trunk = m.Structural.OrderByDescending(s => s.Radius).ThenByDescending(s => s.VertexCount).First();
                int count = trunk.VertexCount, sides = trunk.Sides;
                if (sides < 5 || count < (sides + 1) * 2) failures.Add($"{sp.Id} tier {t}: trunk span {count} verts / {sides} sides");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TiersAreDeterministic()
    {
        var sp = TestUtil.Content.Flora.First(s => s.Climber == null);
        for (int t = 0; t < OrganismMeshes.FloraLodTiers; t++)
            Assert.Equal(OrganismMeshes.FloraTier(sp, 7, t).DigestHex(), OrganismMeshes.FloraTier(sp, 7, t).DigestHex());
    }
}

