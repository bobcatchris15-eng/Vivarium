using Vivarium.Sim.Coverage;
using Xunit;

namespace Vivarium.Sim.Tests.GrowthLab;

[Trait("Suite", "GrowthLab")]
public class GrowthLabTests
{
    private const int Half = 24;
    private const int Steps = 20;
    private const double P = 0.95;

    private static CoverageLayer SeededDisc(ulong seed)
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, seed);
        layer.SetOcc(0, 0, 1);
        return layer;
    }

    /// <summary>Fast assert: the toy disc grows round (§12 crust_eden-style roundness check), no image output.</summary>
    [Fact]
    public void ToyDiscScenarioGrowsRound()
    {
        var layer = SeededDisc(1);
        for (long s = 0; s < Steps; s++) ToyDiscRule.Step(layer, s, Half, P);

        double roundness = GrowthLabRunner.Roundness(layer, Half);
        Assert.True(roundness > 0.75, $"roundness {roundness:F3} <= 0.75");
    }

    /// <summary>Slow: runs the same scenario again, writing PNG timelapse frames under build/growthlab/.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void ToyDiscScenarioWritesTimelapseFrames()
    {
        var layer = SeededDisc(1);
        GrowthLabRunner.Run("toy_disc", layer, Steps, Half, writeFrames: true, s => ToyDiscRule.Step(layer, s, Half, P));

        string dir = GrowthLabRunner.FramesDir("toy_disc");
        var frames = Directory.GetFiles(dir, "frame_*.png");
        Assert.Equal(Steps, frames.Length);
        Assert.True(new FileInfo(frames[0]).Length > 8, "PNG frame should not be empty");
    }

    [Fact]
    [Trait("Suite", "Coverage")]
    public void DeterminismAcrossTwoRunsAndShuffledAllocationOrder()
    {
        var layerA = SeededDisc(42);
        for (long s = 0; s < Steps; s++) ToyDiscRule.Step(layerA, s, Half, P);

        var layerB = SeededDisc(42);
        for (long s = 0; s < Steps; s++) ToyDiscRule.Step(layerB, s, Half, P);

        Assert.Equal(Digest(layerA), Digest(layerB));

        // pre-allocate tiles in a different order before running; result must be identical
        var layerC = new CoverageLayer(CoverageLayerId.Mat, 42);
        var tileOrder = new (int, int)[] { (2, 2), (-2, -2), (0, 0), (1, -1) };
        foreach (var (ti, tj) in tileOrder.Reverse()) layerC.GetOrCreateTile(ti, tj);
        layerC.SetOcc(0, 0, 1);
        for (long s = 0; s < Steps; s++) ToyDiscRule.Step(layerC, s, Half, P);

        Assert.Equal(Digest(layerA), Digest(layerC));
    }

    private static string Digest(CoverageLayer layer)
    {
        using var d = new Core.DigestBuilder();
        foreach (var t in layer.Tiles) { d.Add(t.Ti); d.Add(t.Tj); d.Add(t.Occ); }
        return d.Hex();
    }
}
