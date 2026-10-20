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
                Assert.InRange(Math.Sqrt(p.X * p.X + p.Z * p.Z), 0, 0.92);
                Assert.InRange(p.Y, -0.5, 1.25);
            }
            for (int t = 0; t < rock.Indices.Count; t += 3)
            {
                var a = rock.Position(rock.Indices[t]);
                var b = rock.Position(rock.Indices[t + 1]);
                var c = rock.Position(rock.Indices[t + 2]);
                var cross = (b - a).Cross(c - a);
                Assert.True(cross.Length > 1e-7);
                // Godot front faces are clockwise, opposite the stored outward normal.
                Assert.True(cross.Dot(rock.NormalAt(rock.Indices[t])) < 0);
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

    [Fact]
    public void SurfaceQueriesMatchActualUploadedTriangleInteriorsUnderLocalScaleAndYaw()
    {
        // Expected heights come from the emitted mesh, not the profile evaluator.
        for (ulong seed = 0; seed < 48; seed++)
        {
            var mesh = PropMeshes.Rock(seed);
            var rock = new Rock { X = 1.7, Y = -.23, Z = -2.1, SizeX = .24 + seed % 3 * .2,
                SizeY = .31, SizeZ = .83 - seed % 4 * .11, RotationY = .37 + seed * .19, VariantSeed = seed };
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                int ia = mesh.Indices[t], ib = mesh.Indices[t + 1], ic = mesh.Indices[t + 2];
                var a = mesh.Position(ia); var b = mesh.Position(ib); var c = mesh.Position(ic);
                // The bottom and outward-sloping buried base are hidden from vertical queries.
                if (mesh.NormalAt(ia).Y <= 1e-7) continue;
                foreach (var weights in new[] { (.2, .3, .5), (.61, .23, .16), (.13, .74, .13) })
                {
                    var point = a * weights.Item1 + b * weights.Item2 + c * weights.Item3;
                    var horizontal = new Vec2(point.X * rock.SizeX, point.Z * rock.SizeZ).Rotated(rock.RotationY);
                    var world = rock.Position + horizontal;
                    // MeshData stores float coordinates; thin shelf projections amplify quantization slightly.
                    Assert.InRange(Math.Abs(rock.Y + point.Y * rock.SizeY - rock.TopAt(world)), 0, 1e-5);
                    Assert.True(rock.Covers(world));
                }
            }
        }
    }

    [Fact]
    public void ProfileCacheTracksMutableSeedAndLeavesSaveSchemaUnchanged()
    {
        var rock = new Rock { SizeX = .7, SizeY = .4, SizeZ = .6, VariantSeed = 1 };
        var point = new Vec2(.24, .12);
        _ = rock.TopAt(point);
        rock.VariantSeed = 19;
        var fresh = new Rock { SizeX = .7, SizeY = .4, SizeZ = .6, VariantSeed = 19 };
        Assert.Equal(fresh.TopAt(point), rock.TopAt(point));
        string json = System.Text.Json.JsonSerializer.Serialize(rock);
        Assert.DoesNotContain("surface", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(.3, rock.TopAt(rock.Position), 10);
        Assert.True(double.IsNaN(rock.TopAt(new Vec2(.7, 0))));
    }

    [Fact]
    public void EveryFamilyHasTwoBroadExposedStratifiedShelves()
    {
        for (ulong seed = 0; seed < 48; seed++)
        {
            var mesh = PropMeshes.Rock(seed);
            int lowShelf = 0, highShelf = 0;
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                int index = mesh.Indices[t];
                var centroid = (mesh.Position(index) + mesh.Position(mesh.Indices[t + 1]) +
                    mesh.Position(mesh.Indices[t + 2])) / 3;
                if (mesh.NormalAt(index).Y < .8) continue;
                if (centroid.Y > .07 && centroid.Y < .16) lowShelf++;
                if (centroid.Y > .35 && centroid.Y < .44) highShelf++;
            }
            Assert.True(lowShelf >= 8, $"Seed {seed}: lower shelf has {lowShelf} broad faces");
            Assert.True(highShelf >= 8, $"Seed {seed}: upper shelf has {highShelf} broad faces");
        }
    }
}
