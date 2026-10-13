using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class WormAnatomyTests
{
    [Fact]
    public void LoamthreadSkinHasFiniteOutwardGeometryAndSupportedBellyAcrossMorphs()
    {
        var species = TestUtil.Content.FaunaOrThrow("loamthread");
        foreach (ulong seed in new ulong[] { 0, 42, 1234, 81733 })
        {
            var mesh = OrganismMeshes.Fauna(species, seed);
            Assert.InRange(mesh.Bounds().Min.Y, .0019, .0021);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Assert.True(double.IsFinite(mesh.Position(i).LengthSq));
                Assert.Equal(1f, mesh.UV2[i * 2]);
            }
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                var a = mesh.Position(mesh.Indices[i]);
                var b = mesh.Position(mesh.Indices[i + 1]);
                var c = mesh.Position(mesh.Indices[i + 2]);
                Assert.True((b - a).Cross(c - a).LengthSq > 1e-20, "Collapsed worm skin");
            }
            int dorsal = Enumerable.Range(0, mesh.VertexCount).OrderByDescending(i => mesh.Position(i).Y).First();
            Assert.True(mesh.Normals[dorsal * 3 + 1] > 0, "Worm skin faces inward");
        }
    }
}
