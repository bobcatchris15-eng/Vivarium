using Vivarium.Sim.Geometry;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

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
            double crownWidth = Enumerable.Range(0, rock.VertexCount).Select(rock.Position)
                .Where(p => p.Y > 0.45).Max(p => Math.Sqrt(p.X * p.X + p.Z * p.Z));
            profiles.Add((int)Math.Round(crownWidth * 20));
        }
        Assert.True(profiles.Count >= 3);
    }

    [Fact]
    public void AllRockFamiliesMatchThePlacementSupportTopAndTaperAboveShoulder()
    {
        var families = new HashSet<int>();
        for (ulong seed = 0; seed < 100; seed++)
        {
            var rock = PropMeshes.Rock(seed);
            families.Add((int)rock.UV2[0]);
            var bounds = rock.Bounds();
            Assert.Equal(0.75, bounds.Max.Y, 6); // PropPlacement.RockTop uses Y + SizeY * 0.75.
            double upperRadius = Enumerable.Range(0, rock.VertexCount)
                .Select(rock.Position).Where(p => p.Y > 0.4)
                .Max(p => Math.Sqrt(p.X * p.X + p.Z * p.Z));
            Assert.InRange(upperRadius, 0.01, 0.8);
        }
        Assert.Equal(new[] { 0, 1, 2 }, families.OrderBy(f => f));
    }

    [Fact]
    public void RockSurfaceQueryAndStackSeatAgreeWithRenderedCrestForEveryFamily()
    {
        var families = new HashSet<int>();
        for (ulong seed = 0; seed < 100 && families.Count < 3; seed++)
        {
            int family = (int)PropMeshes.Rock(seed).UV2[0];
            if (!families.Add(family)) continue;
            var w = TestUtil.FlatWorld();
            var placed = w.Placement.PlaceRock(new Vec2(0, 0), 0.3, 0, seed);
            Assert.True(placed.Ok, placed.Message);
            var rock = (Rock)w.Props.Find(placed.Id)!;
            double visualTop = rock.Y + rock.SizeY * PropMeshes.Rock(seed).Bounds().Max.Y;
            Assert.Equal(visualTop, rock.TopAt(rock.Position), 5);
            Assert.Equal(visualTop, w.Props.PropTopAt(rock.Position), 5);
            var edge = new Vec2(rock.X + rock.SizeX * 0.5, rock.Z);
            Assert.True(rock.TopAt(edge) < visualTop);
            var stacked = w.Placement.PlaceRock(rock.Position, 0.22, 0, seed + 300);
            Assert.True(stacked.Ok, stacked.Message);
            var child = (Rock)w.Props.Find(stacked.Id)!;
            Assert.Equal(visualTop, child.Y + child.SizeY * 0.25, 5);
        }
        Assert.Equal(3, families.Count);
    }
}
