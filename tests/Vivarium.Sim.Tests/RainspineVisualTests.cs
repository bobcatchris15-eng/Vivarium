using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class RainspineVisualTests
{
    [Fact]
    public void EyesAreSmallInsetFeaturesRatherThanTheEntireHead()
    {
        var mesh = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("rainspine"));
        var eyes = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2] == 2)
            .Select(mesh.Position).ToArray();
        Assert.NotEmpty(eyes);
        Assert.True(eyes.Max(p => p.X) - eyes.Min(p => p.X) < 0.07);
        Assert.True(eyes.Max(p => p.Y) - eyes.Min(p => p.Y) < 0.06);
    }
    [Fact]
    public void DistalDigitsStayAttachedToTheirLegAndCarryTheFullFootStroke()
    {
        var mesh = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("rainspine"), 1234);
        var tips = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.Colors[i * 4 + 3] > 0.5
            && mesh.Position(i).X > 0.30 && mesh.Position(i).Y < 0.027).ToArray();
        Assert.NotEmpty(tips);
        Assert.All(tips, i => Assert.Equal(1.25f, mesh.UV2[i * 2 + 1]));
        // Sockets and distal geometry share only the four shoulder/hip roots after morphing.
        var roots = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.Colors[i * 4 + 3] > 0.5)
            .Select(i => new Vivarium.Sim.Core.Vec3(mesh.Position(i).X - mesh.Colors[i * 4],
                mesh.Position(i).Y - mesh.Colors[i * 4 + 1], mesh.Position(i).Z - mesh.Colors[i * 4 + 2]))
            .Select(v => (Math.Round(v.X, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5))).Distinct().ToArray();
        Assert.Equal(4, roots.Length);
    }
}
