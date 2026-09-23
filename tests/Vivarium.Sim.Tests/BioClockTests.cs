using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Time")]
public class BioClockTests
{
    private static VivariumWorld Pond(double bio)
    {
        var w = FaunaFixtures.PondWorld(71);
        new ToolActions(w).SetBioAcceleration(bio);
        return w;
    }

    [Fact]
    public void BiologyScalesWithAccelerationButWalkingDoesNot()
    {
        double Grown(double bio)
        {
            var w = Pond(bio);
            var f = w.FloraSystem.Establish(w.Content.FloraOrThrow("carpet_moss"), FaunaFixtures.Land + new Vec2(-0.4, 1), "t", 0.05);
            TestUtil.Condition(w, 0.68, 0.3, 0.45);
            w.Step(360);
            return f.Biomass - 0.05;
        }
        double slow = Grown(1), fast = Grown(8);
        Assert.True(slow > 0 && fast > slow * 6, $"moss growth ×1 {slow:0.00000} vs ×8 {fast:0.00000}");

        double Walked(double bio)
        {
            var w = Pond(bio);
            var st = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), FaunaFixtures.Land);
            var start = st.PositionXZ;
            double path = 0; var last = start;
            for (int i = 0; i < 60; i++) { w.FaunaSystem.StepBehaviour(20); w.Clock.Tick += 2; path += Vec2.Distance(last, st.PositionXZ); last = st.PositionXZ; }
            return path;
        }
        Assert.Equal(Walked(1), Walked(8), 9);
    }

    [Fact]
    public void BioClockAdvancesPerTickAndSurvivesSaveLoad()
    {
        var w = TestUtil.DefaultWorld(bio: TestUtil.ShippedBio);
        double b0 = w.Clock.BioSeconds;
        w.Step(100);
        Assert.Equal(b0 + 100 * 10 * TestUtil.ShippedBio, w.Clock.BioSeconds, 6);
        Assert.True(new ToolActions(w).SetBioAcceleration(3).Ok);
        w.Step(10);
        Assert.Equal(b0 + 1000 * TestUtil.ShippedBio + 300, w.Clock.BioSeconds, 6);
        Assert.Equal(3, w.Descriptor.BioAcceleration);
        string path = Path.Combine(TestUtil.TempDir(), "bio" + SaveSystem.Extension);
        Assert.True(SaveSystem.Save(w, path).Ok);
        var r = SaveSystem.Load(w.Content, path);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(w.Clock.BioSeconds, r.World!.Clock.BioSeconds, 9);
        Assert.Equal(3, r.World.Clock.BioAcceleration);
        w.Step(500); r.World.Step(500);
        Assert.Equal(TestUtil.Digest(w), TestUtil.Digest(r.World));
    }

    [Fact]
    public void GrowthSpeedIsClampedAndBreedingCooldownsUseBiologicalTime()
    {
        var w = Pond(1);
        var t = new ToolActions(w);
        t.SetBioAcceleration(1000);
        Assert.Equal(WorldDescriptor.MaxBioAcceleration, w.Clock.BioAcceleration);
        t.SetBioAcceleration(0.01);
        Assert.Equal(WorldDescriptor.MinBioAcceleration, w.Clock.BioAcceleration);
        t.SetBioAcceleration(10);
        w.Step(50);
        var f = w.FaunaSystem.CreateFounder(FaunaFixtures.Sp("springtail"), FaunaFixtures.Land);
        Assert.True(f.ReproCooldownUntil > w.Clock.BioSeconds && f.ReproCooldownUntil < w.Clock.BioSeconds + FaunaFixtures.Sp("springtail").ReproCooldown + 1);
    }

    [Fact]
    public void DescriptorRejectsOutOfRangeAcceleration()
    {
        var d = TestUtil.FlatDescriptor();
        d.BioAcceleration = 0.5;
        Assert.Contains(d.Validate(), e => e.Contains("bioAcceleration"));
        Assert.Equal(60.0 / 7, new WorldDescriptor().BioAcceleration, 12);   // old saves without the field get the shipped default
    }
}
