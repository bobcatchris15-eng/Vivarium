using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class ToadLocomotionTests
{
    [Theory]
    [InlineData(24)]
    [InlineData(60)]
    [InlineData(144)]
    public void HopHasNoGroundedTranslationAndFinishesWhenTargetStops(int fps)
    {
        var motion = new ToadLocomotion();
        var hop = TestUtil.Content.FaunaOrThrow("stonebell").Animation;
        motion.Reset(Vec3.Zero);
        double dt = 1.0 / fps;
        var target = Vec3.Zero;
        int stances = 0, flights = 0;
        for (int frame = 0; frame < fps * 4; frame++)
        {
            var previous = motion.Position;
            double oldPhase = motion.Phase;
            if (frame < fps * 2) target += new Vec3(1.4 * dt, 0, 0);
            motion.Step(target, 1, dt, frame < fps * 2 ? 1.4 : 0, hop);
            if (!motion.IsCrawling && oldPhase <= motion.Phase && motion.Phase < hop.DutyFactor)
            {
                Assert.Equal(previous, motion.Position);
                stances++;
            }
            if (!motion.IsCrawling && motion.Phase > hop.DutyFactor && motion.Position != previous) flights++;
        }
        Assert.True(stances > 0 && flights > 0);
        Assert.InRange((target - motion.Position).Length, 0, 0.0001);
        Assert.False(motion.Hopping);
    }

    [Fact]
    public void SlowCrawlUsesActualTravelAndDoesNotAdvanceWhileStopped()
    {
        var motion = new ToadLocomotion();
        motion.Reset(Vec3.Zero);
        var target = new Vec3(0.12, 0, 0);
        motion.Step(target, 0.1, 0.1, 0.2, TestUtil.Content.FaunaOrThrow("stonebell").Animation);
        Assert.True(motion.IsCrawling);
        Assert.Equal(target, motion.Position);
        Assert.Equal((1.2 * ToadLocomotion.Crawl.CyclesPerBody) % 1, motion.Phase, 10);
        double phase = motion.Phase;
        motion.Step(target, 0.1, 1, 0, TestUtil.Content.FaunaOrThrow("stonebell").Animation);
        Assert.Equal(phase, motion.Phase);
    }

    [Fact]
    public void FrontToeTipsCarryFullCalibratedStrokeAfterMorphing()
    {
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var mesh = OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("stonebell"), seed);
            var tips = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.Colors[i * 4 + 3] > 0.5
                && mesh.Position(i).X > 0.35 && mesh.Position(i).Y < 0.04).ToArray();
            Assert.NotEmpty(tips);
            Assert.All(tips, i => Assert.Equal(1.25f, mesh.UV2[i * 2 + 1]));
        }
    }

    [Fact]
    public void TurningTargetCannotSteerTheBodyToANewLandingMidFlight()
    {
        var motion = new ToadLocomotion();
        motion.Reset(Vec3.Zero);
        var hop = TestUtil.Content.FaunaOrThrow("stonebell").Animation;
        var target = new Vec3(0.5, 0, 0);
        while (motion.Phase <= hop.DutyFactor) motion.Step(target, 1, 1.0 / 60, 1.4, hop);
        motion.Step(new Vec3(0.5, 0, 2), 1, 1.0 / 60, 1.4, hop);
        Assert.Equal(0, motion.Position.Z);
    }
}
