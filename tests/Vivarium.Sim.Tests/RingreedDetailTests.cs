using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

public class RingreedDetailTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HorsetailNodesAndFertileConesRemainRecognizableAtEveryTier(int tier)
    {
        var sp = TestUtil.Content.FloraOrThrow("ringreed");
        ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual.ringreed"), 0x9E3779B97F4A7C15UL);
        var m = OrganismMeshes.FloraTier(sp, seed, tier);
        int cones = Enumerable.Range(0, m.VertexCount).Count(i => m.Colors[i * 4] > m.Colors[i * 4 + 1]);
        int sheaths = Enumerable.Range(0, m.VertexCount).Count(i => m.Colors[i * 4] + m.Colors[i * 4 + 1] + m.Colors[i * 4 + 2] < 0.4);
        int branches = m.Structural.Count(s => s.Radius < 0.008);
        output.WriteLine($"tier={tier} tris={m.TriangleCount} cone vertices={cones} sheath vertices={sheaths} branches={branches}");
        Assert.True(cones > 0, "Terminal fertile cones must not disappear into the loose-piece culling pass");
        Assert.True(sheaths > 0, "Dark joint sheaths must survive at every viewing distance");
        Assert.True(branches > 0, "Horsetail branch whorls must remain visible");
    }
}
