using System;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;
using Xunit;

namespace Vivarium.Sim.Tests;

public sealed class DuskflickerFireflyTests
{
    private static FaunaSpeciesDef Duskflicker() => TestUtil.Content.FaunaOrThrow("duskflicker");

    [Fact]
    public void DuskflickerIsIndexedAsFantasticalFirefly()
    {
        var sp = Duskflicker();
        Assert.Equal("midge", sp.Model);
        Assert.Equal(EcologyGuild.FlyingGrazerScavenger, sp.Guild);
        Assert.Contains("firefly", sp.Role, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lantern", sp.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("night", sp.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FaunaAnimationFamily.Flight, sp.Animation.Family);
    }

    [Fact]
    public void DuskflickerGeometryMarksPosteriorAbdomenAsLanternRegion()
    {
        var sp = Duskflicker();
        var mesh = OrganismMeshes.Fauna(sp, 98765);

        Assert.True(mesh.TriangleCount > 100, $"Expected triangle count > 100, got {mesh.TriangleCount}");
        Assert.All(mesh.Positions, v => Assert.True(float.IsFinite(v)));
        Assert.All(mesh.Normals, v => Assert.True(float.IsFinite(v)));

        int lanternVertices = 0;
        int nonLanternVertices = 0;

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            float region = mesh.UV2[i * 2];
            var pos = mesh.Position(i);

            if (Math.Abs(region - 9.0f) < 1e-4f)
            {
                lanternVertices++;
                // Posterior abdomen / butt: X must be located behind the thorax and anterior abdomen
                Assert.True(pos.X < -0.10f, $"Lantern vertex at X={pos.X} is not at the posterior abdomen!");
            }
            else
            {
                nonLanternVertices++;
            }
        }

        Assert.True(lanternVertices >= 15, $"Expected at least 15 lantern vertices, got {lanternVertices}");
        Assert.True(nonLanternVertices > lanternVertices, "Body, head and appendages must remain distinct from lantern.");
        Assert.Equal(mesh.DigestHex(), OrganismMeshes.Fauna(sp, 98765).DigestHex());
    }
}
