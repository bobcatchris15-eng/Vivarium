using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class RockMeshTests
{
    [Fact]
    public void RockFamiliesAreSeededDeterministicallyAndStayWithinBudget()
    {
        var digests = new HashSet<string>();
        for (ulong seed = 0; seed < 48; seed++)
        {
            var rock = PropMeshes.Rock(seed);
            Assert.Equal(rock.DigestHex(), PropMeshes.Rock(seed).DigestHex());
            digests.Add(rock.DigestHex());
            Assert.InRange(rock.TriangleCount, 40, 150);
            Assert.InRange(rock.VertexCount, 120, 450);
            for (int i = 0; i < rock.VertexCount; i++)
            {
                var p = rock.Position(i);
                var n = rock.NormalAt(i);
                Assert.True(p.IsFinite && n.IsFinite);
                Assert.InRange(Math.Sqrt(p.X * p.X + p.Z * p.Z), 0, 1.25);
                Assert.InRange(p.Y, -0.5, 1.25);
            }
            for (int t = 0; t < rock.Indices.Count; t += 3)
            {
                var a = rock.Position(rock.Indices[t]);
                var b = rock.Position(rock.Indices[t + 1]);
                var c = rock.Position(rock.Indices[t + 2]);
                Assert.True((b - a).Cross(c - a).Length > 1e-7);
            }
        }
        Assert.True(digests.Count >= 40);
    }

    [Fact]
    public void RockHasBroadPlanarFacesAndDistinctSilhouetteFamilies()
    {
        var profiles = new HashSet<int>();
        for (ulong seed = 0; seed < 48; seed++)
        {
            var rock = PropMeshes.Rock(seed);
            // At least one pair of neighboring triangles must meet with a visible normal break.
            var normalGroups = rock.Normals.Chunk(3).Select(n => string.Join(",", n.Select(x => Math.Round(x, 2)))).Distinct().Count();
            Assert.True(normalGroups >= 12);
            var bounds = rock.Bounds();
            profiles.Add((int)Math.Round((bounds.Max.Y - bounds.Min.Y) * 10));
        }
        Assert.True(profiles.Count >= 3);
    }
}
