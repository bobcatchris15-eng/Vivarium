using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

/// <summary>Vine host binding on props: rotated rock projection and id-stable log/rock binding.</summary>
[Trait("Suite", "Flora")]
public class ClimberHostBindingTests
{
    [Fact]
    public void RockProjectionHonoursYawOnAnElongatedRock()
    {
        // 2 m long along local X, 0.3 m deep along local Z, yawed 90 degrees: local +X maps to world +Z.
        var rock = new Rock { Id = EntityId.Make(EntityKind.Rock, 1), X = 0, Y = 0, Z = 0, RotationY = Math.PI / 2, SizeX = 2, SizeY = 0.5, SizeZ = 0.3 };
        const double shrink = 0.92;

        // A point far out along world +Z must land at the rock's long tip, not its short side.
        Assert.True(ClimberSurface.TryRock(new Vec3(0, 0, 5), rock, 0, out var tip));
        Assert.Equal(2 * shrink, tip.Point.Z, 6);
        Assert.Equal(0, tip.Point.X, 6);
        Assert.Equal(1, tip.Normal.Z, 6);
        Assert.Equal(rock.Id, tip.HostId);

        // Along world +X the rock is thin.
        Assert.True(ClimberSurface.TryRock(new Vec3(5, 0, 0), rock, 0, out var side));
        Assert.Equal(0.3 * shrink, side.Point.X, 6);
        Assert.Equal(1, side.Normal.X, 6);

        // A general point: projection lies on the yawed ellipsoid and the normal is its gradient there.
        rock.RotationY = 0.7;
        Assert.True(ClimberSurface.TryRock(new Vec3(1.3, 0.4, -0.9), rock, 0, out var s));
        var l = new Vec2(s.Point.X, s.Point.Z).Rotated(-rock.RotationY);
        double ax = 2 * shrink, ay = 0.5, az = 0.3 * shrink;
        Assert.Equal(1, l.X * l.X / (ax * ax) + s.Point.Y * s.Point.Y / (ay * ay) + l.Z * l.Z / (az * az), 6);
        var g = new Vec2(l.X / (ax * ax), l.Z / (az * az)).Rotated(rock.RotationY);
        var grad = new Vec3(g.X, s.Point.Y / (ay * ay), g.Z).Normalized();
        Assert.True((grad - s.Normal).Length < 1e-9, $"normal {s.Normal} vs gradient {grad}");
    }

    [Fact]
    public void RemovingAnotherPropDoesNotRebindTheVine()
    {
        var w = TestUtil.FlatWorld(71);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var a = w.Placement.PlaceLog(new Vec2(-2, -2), 0, 1.0, 0.15, 0, 1);
        var b = w.Placement.PlaceLog(new Vec2(0.6, 0), Math.PI / 2, 2.4, 0.18, 0, 2);
        var c = w.Placement.PlaceLog(new Vec2(2, 2.5), 0, 1.0, 0.15, 0, 3);
        Assert.True(a.Ok && b.Ok && c.Ok, $"fixture logs must place: {a.Message} | {b.Message} | {c.Message}");
        var vineSp = TestUtil.Content.FloraOrThrow("clinglace");
        var vine = w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

        int steps = 0;
        while (steps < 144 * 40 && !(vine.ClimberSegments?.Any(s => s.Support == ClimberSurfaceKind.Log) ?? false))
        {
            if (steps % 144 == 0) TestUtil.Condition(w, 0.62, 0.9, 0.75);
            w.FloraSystem.Step(600); steps++;
        }
        var onLog = vine.ClimberSegments!.Where(s => s.Support == ClimberSurfaceKind.Log).ToList();
        Assert.True(onLog.Count > 0, "the vine should reach the nearby log");
        Assert.All(onLog, s => Assert.Equal(b.Id, s.HostId));
        int before = vine.ClimberSegments!.Count;

        Assert.True(w.Placement.Remove(a.Id).Ok);   // shifts log B from index 1 to 0, C from 2 to 1
        for (int i = 0; i < 144 * 6; i++)
        {
            if (i % 144 == 0) TestUtil.Condition(w, 0.62, 0.9, 0.75);
            w.FloraSystem.Step(600);
        }

        var logB = w.Props.Logs.Single(l => l.Id == b.Id);
        var after = vine.ClimberSegments!.Skip(before).Where(s => s.Support == ClimberSurfaceKind.Log).ToList();
        Assert.True(after.Count > 0, "the vine should keep growing on its log after another prop is removed");
        foreach (var s in after)
        {
            Assert.Equal(b.Id, s.HostId);
            Assert.True(ClimberSurface.TryLog(s.Position, logB, 0, out var proj) && proj.DistanceTo(s.Position) < 0.02,
                "a node laid after the removal must lie on its own host log");
        }
    }

    [Fact]
    public void LegacyIndexOnlyNodesMigrateToIds()
    {
        var w = TestUtil.FlatWorld(72);
        var r0 = w.Placement.PlaceRock(new Vec2(-3, 0), 0.4, 0, 1);
        var r1 = w.Placement.PlaceRock(new Vec2(3, 0), 0.4, 0, 2);
        var sp = TestUtil.Content.FloraOrThrow("clinglace");
        var vine = w.FloraSystem.Establish(sp, Vec2.Zero, "legacy", sp.MaxBiomass * 0.5);
        vine.ClimberSegments ??= new();
        vine.ClimberSegments.Add(new ClimberSegment { Support = ClimberSurfaceKind.Rock, HostIndex = 1 });
        Assert.Equal(1, FloraSystem.MigrateClimberHostIds(w));
        Assert.Equal(r1.Id, vine.ClimberSegments[^1].HostId);
        Assert.NotEqual(r0.Id, vine.ClimberSegments[^1].HostId);
    }
}
