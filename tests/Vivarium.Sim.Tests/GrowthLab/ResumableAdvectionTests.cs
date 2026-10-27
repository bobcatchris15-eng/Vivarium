using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;

namespace Vivarium.Sim.Tests.GrowthLab;

public class ResumableAdvectionTests
{
    private sealed class Env : IAquaticEnv
    {
        public bool Changed;
        public double DepthAt(int x, int z) => Changed || (x + z) % 19 == 0 ? 0 : .2;
        public Vec2 FlowAt(int x, int z) => Changed ? new(1, -1) : new(.003 * Math.Sin(x * .3), .002 * Math.Cos(z * .4));
        public bool IsObstacle(int x, int z) => Changed || x == -3 && z < 0;
        public double LightAt(int x, int z) => 1;
        public double NutrientsAt(int x, int z) => 1;
        public double SurfaceShadeAt(int x, int z) => 0;
    }
    private static readonly GridBounds Bounds = new(-37, -35, 36, 34);
    private static CoverageLayer Layer()
    {
        var result = new CoverageLayer(CoverageLayerId.SurfaceFloat, 8181);
        for (int z = Bounds.MinGz; z <= Bounds.MaxGz; z++)
        for (int x = Bounds.MinGx; x <= Bounds.MaxGx; x++)
            if ((x + z) % 3 != 0) result.SetCell(x, z, 1, ((x * 7 + z * 13) & 31) / 32f, 0, 0, 0, 0, 0);
        return result;
    }
    private static void Equal(CoverageLayer expected, CoverageLayer actual)
    {
        var a = expected.Tiles.OrderBy(t => t.Ti).ThenBy(t => t.Tj).ToArray();
        var b = actual.Tiles.OrderBy(t => t.Ti).ThenBy(t => t.Tj).ToArray();
        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal((a[i].Ti, a[i].Tj), (b[i].Ti, b[i].Tj));
            Assert.Equal(a[i].B, b[i].B); Assert.Equal(a[i].Occ, b[i].Occ);
            Assert.Equal(a[i].Version, b[i].Version);
        }
    }
    [Fact]
    public void InterleavingOperationsUsesCapturedInputsAndMatchesAtomicReferenceExactly()
    {
        var expected = Layer(); var actual = Layer(); var other = Layer();
        int expectedSteps = SurfaceFloatRules.Advect(expected, new Env(), Bounds, .0002, .0003, -.0001);
        int steps = 0;
        var env = new Env();
        using var work = SurfaceFloatRules.AdvectBatches(actual, env, Bounds, .0002, .0003, -.0001, n => steps = n).GetEnumerator();
        Assert.True(work.MoveNext()); // All inputs are captured, with no publication yet.
        Equal(Layer(), actual);
        env.Changed = true;
        int boundaries = 1;
        while (work.MoveNext())
        {
            // The synchronous reference uses its own thread-local scratch; it must
            // never corrupt a suspended operation's owned pooled buffers.
            SurfaceFloatRules.Advect(other, new Env(), Bounds, .00001, 0, 0);
            boundaries++;
        }
        Assert.True(boundaries > 5);
        Assert.Equal(expectedSteps, steps);
        Equal(expected, actual);
    }
    [Fact]
    public void DisposingPreparationPublishesNothingAndNextOperationStillMatches()
    {
        var untouched = Layer(); var actual = Layer();
        using (var work = SurfaceFloatRules.AdvectBatches(actual, new Env(), Bounds, .0002, 0, 0).GetEnumerator())
        {
            Assert.True(work.MoveNext()); Assert.True(work.MoveNext());
        }
        Equal(untouched, actual);
        foreach (bool _ in SurfaceFloatRules.AdvectBatches(actual, new Env(), Bounds, .0002, 0, 0)) { }
        SurfaceFloatRules.Advect(untouched, new Env(), Bounds, .0002, 0, 0);
        Equal(untouched, actual);
    }
}
