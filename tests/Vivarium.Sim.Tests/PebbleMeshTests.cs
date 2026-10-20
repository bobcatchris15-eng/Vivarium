using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class PebbleMeshTests
{
    [Fact]
    public void TalusVariantsAreDeterministicBoundedAndFrontFacing()
    {
        var variants = new HashSet<string>();
        for (ulong seed = 0; seed < 48; seed++)
        {
            var mesh = PropMeshes.Pebble(seed);
            Assert.Equal(mesh.DigestHex(), PropMeshes.Pebble(seed).DigestHex());
            variants.Add(mesh.DigestHex());
            Assert.InRange(mesh.TriangleCount, 16, 80);
            Assert.InRange(mesh.VertexCount, 48, 240);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Position(i); var n = mesh.NormalAt(i);
                Assert.True(p.IsFinite && n.IsFinite);
                Assert.InRange(n.Length, 0.99999, 1.00001);
                Assert.InRange(Math.Sqrt(p.X*p.X+p.Z*p.Z), 0, 1.25);
                Assert.InRange(p.Y, -0.500001, 0.750001);
            }
            for (int i = 0; i < mesh.Indices.Count; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i+1], c = mesh.Indices[i+2];
                var face = (mesh.Position(b)-mesh.Position(a)).Cross(mesh.Position(c)-mesh.Position(a));
                Assert.True(face.LengthSq > 1e-10);
                Assert.True(face.Dot(mesh.NormalAt(a)+mesh.NormalAt(b)+mesh.NormalAt(c)) < 0);
            }
            if (Enumerable.Range(0, mesh.VertexCount).Any(i => mesh.UV2[i*2+1] > .5))
                Assert.Equal(-0.38, mesh.Bounds().Min.Y, 6);
            else
                Assert.InRange(mesh.Bounds().Min.Y, -.5, -.38);
            Assert.Equal(0.75, mesh.Bounds().Max.Y, 6);
        }
        Assert.True(variants.Count >= 40);
    }

    [Fact]
    public void ChipCleavageIsOneBroadPlaneWithAngularEdges()
    {
        int flatVariants = 0;
        for (ulong seed = 0; seed < 32; seed++)
        {
            var mesh = PropMeshes.Pebble(seed);
            var top = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i*2+1] > .5).ToArray();
            if (top.Length == 0) continue; // Weathered fragments keep their faceted crown.
            flatVariants++;
            Assert.True(top.Length >= 15);
            var origin = mesh.Position(top[0]); var normal = mesh.NormalAt(top[0]);
            Assert.True(normal.Y > .98);
            foreach (int i in top)
            {
                Assert.True(Math.Abs((mesh.Position(i)-origin).Dot(normal)) < 1e-6);
                Assert.True(mesh.NormalAt(i).Dot(normal) > .99999);
            }
            Assert.True(mesh.NormalAt(0).Dot(normal) < .95);
        }
        Assert.InRange(flatVariants, 16, 31);
    }

    [Fact]
    public void ScatterChipsStayFlatDeterministicAndDoNotChangeEcology()
    {
        var world = TestUtil.FlatWorld();
        Assert.True(world.Placement.PlaceGravel(new Vec2(1, 1), .6, 99).Ok);
        string before = WorldSerializer.Digest(world);
        var a = PropMeshes.GravelScatter(world, world.Props.Gravel[0]);
        var b = PropMeshes.GravelScatter(world, world.Props.Gravel[0]);
        Assert.Equal(a.ToArray(), b.ToArray());
        Assert.InRange(a.Count, 50, 1000);
        foreach (var p in a)
        {
            Assert.InRange(p.Scale.Y / Math.Sqrt(p.Scale.X*p.Scale.Z), .15, .52);
            Assert.InRange(Math.Abs(p.TiltX), 0, .20);
            Assert.InRange(Math.Abs(p.TiltZ), 0, .20);
            double ground = world.Terrain.Height(new Vec2(p.Position.X, p.Position.Z));
            Assert.True(p.Position.Y - .38*p.Scale.Y < ground);
            Assert.True(p.Position.Y + .75*p.Scale.Y > ground);
        }
        Assert.Equal(before, WorldSerializer.Digest(world));
    }
}
