using Vivarium.Sim.Core;
using Vivarium.Sim.Tools;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "World")]
public class CameraCollisionTests
{
    [Fact]
    public void CameraCannotDescendThroughTerrain()
    {
        var w = TestUtil.FlatWorld();
        var p = new Vec2(0.4, -0.3);
        double h = w.Terrain.Height(p);
        var got = CameraCollision.Resolve(w, new Vec3(p.X, h + 1, p.Z), new Vec3(p.X, h - 1, p.Z));
        double finalSurface = w.Terrain.Height(got.XZ);
        Assert.True(got.Y >= finalSurface + CameraCollision.DefaultRadius - 0.005,
            $"camera ended at {got}, surface there is {finalSurface:0.000}");
    }

    [Fact]
    public void CameraCannotEnterRockOrLog()
    {
        var w = TestUtil.FlatWorld();
        var rock = w.Placement.PlaceRock(new Vec2(0, 0), 0.45, 0.1, 123);
        Assert.True(rock.Ok);
        var r = (Vivarium.Sim.World.Rock)w.Props.Find(rock.Id)!;
        var stopped = CameraCollision.Resolve(w, new Vec3(r.X + 1.2, r.Y + 0.1, r.Z), new Vec3(r.X, r.Y + 0.1, r.Z));
        Assert.True((stopped - new Vec3(r.X, r.Y + 0.1, r.Z)).Length > 0.2);

        var log = w.Placement.PlaceLog(new Vec2(1.5, -1.5), 0.0, 1.5, 0.18, 0, 456);
        Assert.True(log.Ok);
        var l = (Vivarium.Sim.World.LogProp)w.Props.Find(log.Id)!;
        stopped = CameraCollision.Resolve(w, new Vec3(l.X, l.Y + 1, l.Z), new Vec3(l.X, l.Y, l.Z));
        Assert.True(stopped.Y >= l.Y + l.Radius);
    }

    [Fact]
    public void CameraCanFlyBesideIslandButCannotEnterCutFace()
    {
        var w = TestUtil.FlatWorld();
        double outside = w.Domain.Radius + 0.4;
        var free = CameraCollision.Resolve(w, new Vec3(outside, 0, 0), new Vec3(outside, -1, 0));
        Assert.True(free.Y < -0.9);

        var edge = w.Domain.Vertices[0];
        var stopped = CameraCollision.Resolve(w, new Vec3(edge.X + 0.5, 0, edge.Z), new Vec3(edge.X - 0.5, 0, edge.Z));
        Assert.True(w.Domain.SignedDistance(stopped.XZ) >= -CameraCollision.DefaultRadius - 0.02);
    }
}
