using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class FoliageBladeAccommodationTests
{
    [Fact]
    public void RotationPreservesRootAndLength()
    {
        var root = new Vec3(1, 2, 3);
        var tip = new Vec3(2, 2.3, 3);
        var rotated = FoliageBladeAccommodation.RotateAroundUp(tip, root, Math.PI / 2);
        Assert.Equal(root.Y + 0.3, rotated.Y, 8);
        Assert.Equal((tip - root).Length, (rotated - root).Length, 8);
    }

    [Fact]
    public void AvoidsStructuralStemWithStablePose()
    {
        var index = new FoliageOccupancyIndex();
        index.Upsert(new AccommodationCapsule(new AccommodationKey(2, 1), AccommodationTissue.Wood,
            new Vec3(0.7, -0.5, 0), new Vec3(0.7, 0.5, 0), 0.12, 1));
        var blade = new AccommodationBlade(new AccommodationKey(1, 1), Vec3.Zero,
            new Vec3(1, 0, 0), Vec3.Forward, 0.08, 0.4, 1.2);
        double before = FoliageBladeAccommodation.StemPenetration(blade, index.Query(
            new Vec3(-2, -2, -2), new Vec3(2, 2, 2)));
        var pose = FoliageBladeAccommodation.SolveYaw(blade, index);
        Assert.True(before > 0);
        Assert.True(pose.Penetration < before, $"before={before}, after={pose.Penetration}");
        Assert.Equal(pose, FoliageBladeAccommodation.SolveYaw(blade, index));
    }

    [Fact]
    public void UnobstructedBladeStaysAtRest()
    {
        var blade = new AccommodationBlade(new AccommodationKey(1, 1), Vec3.Zero,
            Vec3.Right, Vec3.Forward, 0.1, 0.5);
        Assert.Equal(0, FoliageBladeAccommodation.SolveYaw(blade, new FoliageOccupancyIndex()).YawRadians);
    }
}
