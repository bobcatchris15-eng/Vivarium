using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class GeometryLodTests
{
    private readonly ITestOutputHelper _out;
    public GeometryLodTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void EveryFloraSpeciesHasThreeGeometricTiersWithinBudget()
    {
        var failures = new List<string>();
        foreach (var sp in TestUtil.Content.Flora)
        {
            ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
            var tiers = Enumerable.Range(0, OrganismMeshes.FloraLodTiers)
                .Select(t => sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, seed, true, t) : OrganismMeshes.FloraTier(sp, seed, t))
                .Select(m => m.TriangleCount).ToArray();
            int top = tiers[0], mid = tiers[1], low = tiers[2];
            _out.WriteLine($"{sp.Id}={top}/{mid}/{low}");
            if (top <= 0 || low <= 0) failures.Add($"{sp.Id}: empty tier {top}/{mid}/{low}");
            if (!(low <= top * 0.05 || low <= 400)) failures.Add($"{sp.Id}: low {low} of {top}");
            if (mid > top * 0.32 || (top >= 1600 && mid < top * 0.12)) failures.Add($"{sp.Id}: mid {mid} of {top}");
            if (low > mid && top > 400) failures.Add($"{sp.Id}: low {low} > mid {mid}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TiersAreDeterministic()
    {
        var sp = TestUtil.Content.Flora.First(s => s.Climber == null);
        Assert.Equal(OrganismMeshes.FloraTier(sp, 7, 2).DigestHex(), OrganismMeshes.FloraTier(sp, 7, 2).DigestHex());
        Assert.Equal(OrganismMeshes.Flora(sp, 7).DigestHex(), OrganismMeshes.FloraTier(sp, 7, 0).DigestHex());
    }
}
