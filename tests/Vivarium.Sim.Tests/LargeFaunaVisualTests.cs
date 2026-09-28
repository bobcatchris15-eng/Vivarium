using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class LargeFaunaVisualTests
{
    [Fact]
    public void WormSupportWaveStopsAtRestAndCannotFoldItsBodyBackwards()
    {
        var a = TestUtil.Content.FaunaOrThrow("loamthread").Animation;
        Assert.Equal(0, FaunaGait.Advance(a, 0, 0.1, 20));
        // Recovering segments compress most strongly. Their longitudinal Jacobian
        // must remain positive, otherwise adjacent rings cross and invert the tube.
        double compression = 1 - 2 * FaunaGait.HalfStroke(a) * a.PhaseSpread / (1 - a.DutyFactor);
        Assert.True(compression > 0.10, "Worm recovery must compress without inverting body rings");
    }
    [Theory]
    [InlineData("stiltclaw", 0.045)]
    [InlineData("dewmantle", 0.055)]
    [InlineData("rustcoil", 0.05)]
    [InlineData("reedjaw", 0.05)]
    [InlineData("moonveil", 0.09)]
    public void EyeMaterialIsRestrictedToSmallEyes(string species, double maxLongitudinalSpan)
    {
        var m = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow(species));
        var eyes = Enumerable.Range(0, m.VertexCount).Where(i => m.UV2[i * 2] == 2).Select(m.Position).ToArray();
        Assert.NotEmpty(eyes);
        Assert.True(eyes.Max(p => p.X) - eyes.Min(p => p.X) < maxLongitudinalSpan,
            $"{species}: the head/mantle should not be shaded as an eye");
    }

    [Fact]
    public void SnailHasARigidShellSeparateFromItsEyesAndMuscularFoot()
    {
        var m = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("glasscoil"));
        var shell = Enumerable.Range(0, m.VertexCount).Where(i => m.UV2[i * 2] == 5).ToArray();
        Assert.NotEmpty(shell);
        Assert.All(shell, i => Assert.Equal(4, m.UV2[i * 2 + 1]));
        Assert.Contains(Enumerable.Range(0, m.VertexCount), i => m.UV2[i * 2] == 2);
    }

    [Fact]
    public void MothHasFourWingAttachmentsAndSixWalkingLegs()
    {
        var m = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("moonveil"), 1234);
        Assert.Equal(4, Roots(m, 2).Length);
        Assert.Equal(6, Roots(m, 1).Length);
    }

    [Fact]
    public void HarvestmanHasEightLegsAndSeparateSensoryAppendages()
    {
        var m = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("stiltclaw"), 1234);
        Assert.Equal(8, Roots(m, 1).Length);
        Assert.NotEmpty(Roots(m, 3));
    }

    private static (double, double, double)[] Roots(MeshData m, int role) =>
        Enumerable.Range(0, m.VertexCount).Where(i => m.Colors[i * 4 + 3] > 0.5
            && (int)m.UV2[i * 2 + 1] == role)
        .Select(i => (Math.Round(m.Position(i).X - m.Colors[i * 4], 5),
            Math.Round(m.Position(i).Y - m.Colors[i * 4 + 1], 5),
            Math.Round(m.Position(i).Z - m.Colors[i * 4 + 2], 5))).Distinct().ToArray();
}
