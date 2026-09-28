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
    public void AntennalFeathersMeetTheCurvedShaftAfterInheritedMorphing()
    {
        var m = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("moonveil"), 1234);
        var sensory = m.Structural.Where(s => m.UV2[s.FirstVertex * 2 + 1] == 3).ToArray();
        var shafts = sensory.Where(s => s.Radius > 0.004).Select(s =>
            Enumerable.Range(0, s.VertexCount / (s.Sides + 1)).Select(row =>
                Enumerable.Range(0, s.Sides).Select(k => m.Position(s.FirstVertex + row * (s.Sides + 1) + k))
                    .Aggregate(Vivarium.Sim.Core.Vec3.Zero, (sum, p) => sum + p) / s.Sides).ToArray()).ToArray();
        var feathers = sensory.Where(s => s.Radius < 0.004).ToArray();
        Assert.Equal(2, shafts.Length); Assert.NotEmpty(feathers);
        foreach (var feather in feathers)
        {
            var root = Enumerable.Range(0, feather.Sides).Select(k => m.Position(feather.FirstVertex + k))
                .Aggregate(Vivarium.Sim.Core.Vec3.Zero, (sum, p) => sum + p) / feather.Sides;
            double gap = shafts.SelectMany(shaft => shaft.Zip(shaft.Skip(1), (a, b) =>
            {
                var axis = b - a;
                var nearest = a + axis * Math.Clamp((root - a).Dot(axis) / axis.LengthSq, 0, 1);
                return (root - nearest).Length;
            })).Min();
            Assert.True(gap < 0.001, $"Antennal feather floats {gap} from the shaft");
        }
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
