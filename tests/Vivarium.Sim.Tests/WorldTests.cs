using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "World")]
public class WorldTests
{
    [Theory] // t-013
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(20)]
    public void HexDomainClassifiesAndReportsBoundaries(double diameter)
    {
        var d = new HexDomain(diameter);
        double R = diameter / 2, a = R * Math.Sqrt(3) / 2;
        Assert.Equal(HexRegion.Interior, d.Classify(Vec2.Zero));
        Assert.Equal(HexRegion.Interior, d.Classify(new Vec2(R * 0.5, 0)));
        Assert.Equal(HexRegion.Corner, d.Classify(new Vec2(R, 0)));
        Assert.Equal(HexRegion.Corner, d.Classify(d.Vertices[3]));
        Assert.Equal(HexRegion.Edge, d.Classify(new Vec2(0, a)));          // midpoint of the top edge
        Assert.Equal(HexRegion.Exterior, d.Classify(new Vec2(0, a + 0.01)));
        Assert.Equal(HexRegion.Exterior, d.Classify(new Vec2(R * 0.95, a * 0.95)));
        // nearest boundary position and normal
        var p = new Vec2(0, a * 0.5);
        Assert.True(Vec2.Distance(d.NearestBoundaryPoint(p), new Vec2(0, a)) < 1e-9);
        var n = d.BoundaryNormal(p);
        Assert.True(Vec2.Distance(n, new Vec2(0, 1)) < 1e-9);
        var outside = new Vec2(R + 1, 0);
        Assert.True(Vec2.Distance(d.NearestBoundaryPoint(outside), new Vec2(R, 0)) < 1e-9);
        Assert.True(Vec2.Distance(d.BoundaryNormal(new Vec2(R, 0)), new Vec2(1, 0)) < 1e-6, "corner normal is the bisector");
        Assert.True(d.SignedDistance(Vec2.Zero) < 0 && Math.Abs(d.SignedDistance(Vec2.Zero) + a) < 1e-9);
    }

    [Fact] // t-014
    public void DescriptorRoundTripsAndReconstructsBaseline()
    {
        var d = TestUtil.Default;
        var json = d.ToJson();
        var back = WorldDescriptor.FromJson(json);
        Assert.Equal(json, back.ToJson());
        var w1 = VivariumWorld.Create(TestUtil.Content, d, populate: false);
        var w2 = VivariumWorld.Create(TestUtil.Content, back, populate: false);
        Assert.Equal(w1.Terrain.DigestHex(), w2.Terrain.DigestHex());
        Assert.Equal(TestUtil.Digest(w1), TestUtil.Digest(w2));
        Assert.Empty(d.Validate());
        d.Diameter = 25;
        Assert.NotEmpty(d.Validate());
    }

    [Fact] // t-015
    public void HeightfieldIsDeterministicWithReliefWithinLimits()
    {
        var d = TestUtil.Default;
        var dom = new HexDomain(d.Diameter);
        var a = Heightfield.Generate(d, dom); var b = Heightfield.Generate(d, dom);
        Assert.Equal(a.DigestHex(), b.DigestHex());
        d.Seed++;
        Assert.NotEqual(a.DigestHex(), Heightfield.Generate(d, dom).DigestHex());
        Assert.All(a.H, h => Assert.InRange(h, d.Terrain.MinHeight, d.Terrain.MaxHeight));
        double range = a.H.Max() - a.H.Min();
        Assert.True(range > 0.8, $"terrain should have useful relief (range {range:0.00} m)");
    }

    [Fact] // t-016
    public void TopMeshIsClippedExactlyToHexagon()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var top = TerrainMesh.BuildTop(w).Mesh;
        Assert.True(top.TriangleCount > 1000);
        double maxOutside = double.NegativeInfinity;
        for (int i = 0; i < top.VertexCount; i++) maxOutside = Math.Max(maxOutside, w.Domain.SignedDistance(top.Position(i).XZ));
        Assert.True(maxOutside <= 2e-6, $"a vertex lies {maxOutside} m outside the hexagon (float32 vertex tolerance)");
        // coverage: summed triangle area equals the hexagon's area
        double area = 0;
        for (int t = 0; t < top.Indices.Count; t += 3)
        {
            var p0 = top.Position(top.Indices[t]).XZ; var p1 = top.Position(top.Indices[t + 1]).XZ; var p2 = top.Position(top.Indices[t + 2]).XZ;
            area += Math.Abs((p1 - p0).Cross(p2 - p0)) / 2;
        }
        double hexArea = 3 * Math.Sqrt(3) / 2 * w.Domain.Radius * w.Domain.Radius;
        Assert.InRange(area, hexArea * 0.9999, hexArea * 1.0001);
        // Render-only centre vertices smooth between authoritative grid points; keep the discrepancy small.
        for (int i = 0; i < top.VertexCount; i += 97)
            Assert.InRange(Math.Abs(w.Terrain.Height(top.Position(i).XZ) - top.Position(i).Y), 0, 0.08);
    }

    [Fact] // t-017
    public void SideWallsAreContinuousAndOutwardFacing()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var walls = TerrainMesh.BuildWalls(w);
        Assert.True(walls.TriangleCount > 100);
        // every wall vertex lies on the hexagon perimeter (or the underside) and within vertical bounds
        for (int i = 0; i < walls.VertexCount; i++)
        {
            var p = walls.Position(i);
            Assert.InRange(p.Y, w.Terrain.Bottom - 1e-6, w.Terrain.MaxHeight + 1e-6);
            if (Math.Abs(p.Y - w.Terrain.Bottom) > 1e-6) Assert.True(Math.Abs(w.Domain.SignedDistance(p.XZ)) < 1e-6);
        }
        // top edge of the wall follows the terrain edge continuously: sample points along each side
        for (int k = 0; k < 6; k++)
            for (double t = 0; t <= 1; t += 0.1)
            {
                var q = Vec2.Lerp(w.Domain.Vertices[k], w.Domain.Vertices[(k + 1) % 6], t);
                double top = double.NegativeInfinity;
                for (int i = 0; i < walls.VertexCount; i++)
                    if (Vec2.Distance(walls.Position(i).XZ, q) < w.Terrain.Step * 0.51) top = Math.Max(top, walls.Position(i).Y);
                Assert.True(Math.Abs(top - w.Terrain.Height(q)) < 0.05, $"wall top gap at side {k} t={t:0.0}");
            }
        // no inverted faces: Godot front faces have right-hand normals pointing into the island
        for (int t = 0; t < walls.Indices.Count; t += 3)
        {
            var a = walls.Position(walls.Indices[t]); var b = walls.Position(walls.Indices[t + 1]); var c = walls.Position(walls.Indices[t + 2]);
            var rh = (b - a).Cross(c - a);
            if (rh.LengthSq < 1e-16) continue; // degenerate zero-height band
            var outward = walls.NormalAt(walls.Indices[t]);
            Assert.True(rh.Normalized().Dot(outward) < -0.9, "wall triangle is inverted");
        }
    }

    [Fact] // t-018
    public void StrataAreDataDrivenAndQueryable()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        Assert.True(w.Strata.Layers.Count >= 3);
        Assert.Equal("topsoil", w.Strata.Layers[0].Id);
        var p = new Vec2(1, -1);
        double surf = w.Terrain.Height(p);
        Assert.Equal("topsoil", w.Strata.LayerAt(w.Terrain, p, surf - 0.05).Id);
        Assert.Equal("subsoil", w.Strata.LayerAt(w.Terrain, p, surf - 0.6).Id);
        Assert.Equal(w.Strata.Layers[^1].Id, w.Strata.LayerAt(w.Terrain, p, w.Terrain.Bottom + 0.01).Id);
        // wall bands carry the data colours and layer index
        var walls = TerrainMesh.BuildWalls(w);
        var layerIds = Enumerable.Range(0, walls.VertexCount).Select(i => (int)Math.Round(walls.UV2[i * 2])).Distinct().ToList();
        Assert.True(layerIds.Count >= 3);
        var topsoilColor = w.Strata.Layers[0].Color;
        int iTop = Enumerable.Range(0, walls.VertexCount).First(i => walls.UV2[i * 2] == 0);
        Assert.Equal((float)topsoilColor[0], walls.Colors[iTop * 4]);
    }

    [Fact] // t-019
    public void QuerySurfaceMatchesGeneratedTerrain()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var hf = w.Terrain;
        var ulong0 = Rng.Mix(w.Seed, Hash.Fnv1a64("terrain.height"));
        for (int j = 2; j < hf.Nz - 2; j += 7)
            for (int i = 2; i < hf.Nx - 2; i += 7)
            {
                var p = hf.VertexPos(i, j);
                Assert.Equal(hf.Vertex(i, j), w.SurfaceHeight(p), 9);
                Assert.Equal(Heightfield.RawHeight(ulong0, w.Descriptor.Terrain, p.X, p.Z, w.Domain), w.SurfaceHeight(p), 9);
            }
        var n = hf.Normal(new Vec2(0.3, 0.2));
        Assert.True(n.Y > 0 && Math.Abs(n.Length - 1) < 1e-9);
        // interaction ray from above hits the terrain at the queried height
        var hit = Tools.Selection.RayTerrain(w, new Vec3(1.2, 10, -0.7), new Vec3(0, -1, 0));
        Assert.Equal(10 - w.SurfaceHeight(new Vec2(1.2, -0.7)), hit, 4);
    }

    [Fact]
    public void TerrainVisibilityRayRespectsCallerDistance()
    {
        var w = TestUtil.FlatWorld();
        double ground = w.Terrain.Height(Vec2.Zero);
        var origin = new Vec3(0, ground + 2, 0);
        var down = new Vec3(0, -1, 0);

        Assert.Equal(double.PositiveInfinity, Tools.Selection.RayTerrain(w, origin, down, maxDistance: 1));
        Assert.Equal(2, Tools.Selection.RayTerrain(w, origin, down, maxDistance: 3), 4);
        Assert.Equal(2, Tools.Selection.RayTerrain(w, origin, down), 4);
        Assert.Equal(double.PositiveInfinity, Tools.Selection.RayOpaque(w, origin, down, maxDistance: 1));
        Assert.Equal(2, Tools.Selection.RayOpaque(w, origin, down, maxDistance: 3), 4);
    }

    [Fact] // t-020
    public void SubstrateMapIsDeterministicAndCoversAllClasses()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var w2 = TestUtil.DefaultWorld(populate: false);
        var seen = new HashSet<Substrate>();
        foreach (int c in w.Grid.DomainCells)
        {
            var p = w.Grid.CellCenter(c);
            var s = w.SubstrateAt(p);
            Assert.Equal(s, w2.SubstrateAt(p));
            seen.Add(s);
        }
        Assert.Equal(SubstrateIds.All.ToHashSet(), seen);
        Assert.Equal((Substrate)w.Fields.BaseSubstrate.Fallback, w.SubstrateAt(new Vec2(50, 50)));
    }

    [Fact] // t-021, t-023, t-025
    public void PropModelsSerializeIndependentlyOfRendering()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var json = System.Text.Json.JsonSerializer.Serialize(w.Props, Persistence.WorldSerializer.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<PropSet>(json, Persistence.WorldSerializer.Json)!;
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(back, Persistence.WorldSerializer.Json));
        Assert.True(w.Props.Rocks.Count > 0 && w.Props.Logs.Count > 0 && w.Props.Gravel.Count > 0);
        var log = w.Props.Logs[0];
        Assert.Contains("log", log.HabitatTags);
        Assert.Equal(log.HabitatTags, back.Logs[0].HabitatTags);
        Assert.Equal(Substrate.Rock, w.Props.Rocks[0].SubstrateEffect);
    }

    [Fact] // t-022
    public void RockVariantsAreDeterministicAndDistinct()
    {
        var digests = Enumerable.Range(1, 12).Select(s => PropMeshes.Rock((ulong)s * 7919).DigestHex()).ToList();
        Assert.Equal(12, digests.Distinct().Count());
        Assert.Equal(PropMeshes.Rock(7919).DigestHex(), PropMeshes.Rock(7919).DigestHex());
        var m = PropMeshes.Rock(5);
        var (mn, mx) = m.Bounds();
        Assert.True(mn.Y > -0.6 && mx.Y > 0.4, "rock has a flattened base and height");
    }

    [Fact] // t-024
    public void LogVariantsAreDeterministicAndDistinct()
    {
        var d = Enumerable.Range(0, 8).Select(i => PropMeshes.Log((ulong)(i + 1) * 104729, 1.8, 0.18, i % 4).DigestHex()).ToList();
        Assert.Equal(8, d.Distinct().Count());
        var m = PropMeshes.Log(3, 2.0, 0.2, 1);
        var (mn, mx) = m.Bounds();
        Assert.InRange(mx.X - mn.X, 1.9, 2.3);      // log length preserved: recognisable as a log
        Assert.InRange(mx.Z - mn.Z, 0.3, 0.9);
    }

    [Fact] // t-025, t-026
    public void GravelModifiesSubstrateButNotElevation()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(1, 1);
        double h = w.SurfaceHeight(p);
        Assert.Equal(Substrate.Soil, w.SubstrateAt(p));
        var r = w.Placement.PlaceGravel(p, 0.6, 99);
        Assert.True(r.Ok);
        Assert.Equal(Substrate.Gravel, w.SubstrateAt(p));
        Assert.Equal(h, w.SurfaceHeight(p));
        Assert.Equal(Substrate.Soil, w.SubstrateAt(p + new Vec2(1.5, 0)));
        var pebbles = PropMeshes.GravelScatter(w, w.Props.Gravel[0]);
        Assert.InRange(pebbles.Count, 50, 1000);
        Assert.Equal(1, w.Props.Count); // no simulation entities per pebble
    }

    [Fact] // t-027
    public void SeededPropPlacementIsReproducible()
    {
        var a = TestUtil.DefaultWorld(populate: false);
        var b = TestUtil.DefaultWorld(populate: false);
        var ja = System.Text.Json.JsonSerializer.Serialize(a.Props, Persistence.WorldSerializer.Json);
        Assert.Equal(ja, System.Text.Json.JsonSerializer.Serialize(b.Props, Persistence.WorldSerializer.Json));
        Assert.Equal(a.Descriptor.Placement.Rocks, a.Props.Rocks.Count);
        Assert.Equal(a.Descriptor.Placement.Logs, a.Props.Logs.Count);
        Assert.Equal(a.Descriptor.Placement.GravelPatches, a.Props.Gravel.Count);
        var c = TestUtil.DefaultWorld(seed: 999, populate: false);
        Assert.NotEqual(ja, System.Text.Json.JsonSerializer.Serialize(c.Props, Persistence.WorldSerializer.Json));
    }

    [Fact] // t-028
    public void RuntimePlacementApiValidatesAndUpdatesQueries()
    {
        var w = TestUtil.FlatWorld();
        var edge = new Vec2(w.Domain.Radius - 0.05, 0);
        Assert.False(w.Placement.PlaceRock(edge, 0.4, 0, 1).Ok);
        var p = new Vec2(-1, 0.5);
        var r = w.Placement.PlaceRock(p, 0.4, 0.3, 1);
        Assert.True(r.Ok);
        Assert.Equal(Substrate.Rock, w.SubstrateAt(p));
        Assert.False(w.Placement.PlaceRock(p + new Vec2(0.1, 0), 0.4, 0, 2).Ok, "overlap must be rejected");
        var moved = w.Placement.Move(r.Id, new Vec2(1.5, -1));
        Assert.True(moved.Ok);
        Assert.NotEqual(Substrate.Rock, w.SubstrateAt(p));
        Assert.Equal(Substrate.Rock, w.SubstrateAt(new Vec2(1.5, -1)));
        var log = w.Placement.PlaceLog(new Vec2(-2, -2), 0.5, 1.5, 0.15, 0, 5);
        Assert.True(log.Ok);
        Assert.Equal(Substrate.Wood, w.SubstrateAt(new Vec2(-2, -2)));
        Assert.Contains("bark_fresh", w.Props.HabitatTagsAt(new Vec2(-2, -2)));
        Assert.True(w.Placement.Remove(log.Id).Ok);
        Assert.NotEqual(Substrate.Wood, w.SubstrateAt(new Vec2(-2, -2)));
        Assert.False(w.Placement.Remove(log.Id).Ok);
    }

    [Fact] // t-028b
    public void PropsCanBeStackedOnEachOther()
    {
        var w = TestUtil.FlatWorld();
        var log = w.Placement.PlaceLog(new Vec2(0, 0), 0, 1.5, 0.2, 0, 5);
        Assert.True(log.Ok);
        var baseLog = (LogProp)w.Props.Find(log.Id)!;
        double logTop = baseLog.Y + baseLog.Radius;

        // rock stacked on the log: seated at the log's top, not the terrain
        var rock = w.Placement.PlaceRock(new Vec2(0, 0), 0.3, 0, 11);
        Assert.True(rock.Ok, rock.Message);
        var r = (Rock)w.Props.Find(rock.Id)!;
        Assert.Equal(logTop - r.SizeY * 0.25, r.Y, 6);
        Assert.True(r.Y > baseLog.Y + 0.05, "rock should be seated above the log, not at terrain level");

        // stack a second rock on the first rock (level 3): allowed
        var rockTop = r.Y + r.SizeY * 0.75;
        var rock2 = w.Placement.PlaceRock(new Vec2(0, 0), 0.25, 0, 12);
        Assert.True(rock2.Ok, rock2.Message);
        var r2 = (Rock)w.Props.Find(rock2.Id)!;
        Assert.Equal(rockTop - r2.SizeY * 0.25, r2.Y, 6);

        // a 4th level is rejected
        var rock3 = w.Placement.PlaceRock(new Vec2(0, 0), 0.2, 0, 13);
        Assert.False(rock3.Ok, "stack of 4 must be rejected");

        // sculpting (which reseats internally) after lowering the base log's terrain drops the whole stack
        double before = r2.Y;
        int changed = TerrainEditing.Sculpt(w, new Vec2(0, 0), 1.0, 0.3, SculptMode.Lower);
        Assert.True(changed > 0);
        Assert.True(r2.Y < before, "top of stack should follow the base down");

        // removing the base log reseats the props that were stacked on it
        double rBeforeRemoval = r.Y;
        Assert.True(w.Placement.Remove(log.Id).Ok);
        Assert.True(r.Y < rBeforeRemoval, "removing the base should lower props stacked on it");
    }

    [Fact] // t-029
    public void WorldGenerationDigestsAreStableForFixtureSeeds()
    {
        foreach (ulong seed in new ulong[] { 1, 20260923, 777 })
        {
            string d1 = TestUtil.Digest(TestUtil.DefaultWorld(seed));
            string d2 = TestUtil.Digest(TestUtil.DefaultWorld(seed));
            Assert.Equal(d1, d2);
        }
    }
}
