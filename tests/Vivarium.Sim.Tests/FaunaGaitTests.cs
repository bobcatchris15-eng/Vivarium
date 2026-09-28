using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class FaunaGaitTests
{
    [Fact]
    public void DistanceClockSurvivesFrameRateSpeedAndCadenceCeiling()
    {
        var a = new FaunaAnimationDef { Family = FaunaAnimationFamily.Walk, CyclesPerBody = 3, MaxHz = 1, IdleHz = 0.5 };
        double Run(int frames, double seconds) => Enumerable.Range(0, frames).Sum(_ => FaunaGait.Advance(a, 0.4 / frames, 0.1, seconds / frames));
        Assert.Equal(12, Run(30, 4), 10);
        Assert.Equal(Run(30, 4), Run(240, 1), 10);
        Assert.Equal(0, FaunaGait.Advance(a, 0, 0.1, 20));
        Assert.Equal(24, FaunaGait.Advance(a, 0.4, 0.05, 1), 10);
    }

    [Fact]
    public void PlantedToeCancelsWorldTranslationAcrossStanceForEveryWalker()
    {
        foreach (var sp in TestUtil.Content.Fauna.Where(s => s.Animation.Family is FaunaAnimationFamily.Walk or FaunaAnimationFamily.Metachronal or FaunaAnimationFamily.Sprawl))
        {
            var a = sp.Animation;
            // Advance from 20% to 80% of stance, including real body/genetic scale.
            double phaseStart = 0.2 * a.DutyFactor, phaseEnd = 0.8 * a.DutyFactor;
            double stroke = FaunaGait.HalfStroke(a), scale = sp.SizeMax * sp.VisualScale;
            double sweep0 = 1 - 2 * phaseStart / a.DutyFactor, sweep1 = 1 - 2 * phaseEnd / a.DutyFactor;
            double translation = (phaseEnd - phaseStart) / a.CyclesPerBody * scale;
            Assert.True(Math.Abs(translation + (sweep1 - sweep0) * stroke * scale) < 1e-10, sp.Id);
        }
    }

    [Fact]
    public void HoveringAndSwimmingRetainAuthoredIdleBeatWithoutLosingTravel()
    {
        var a = new FaunaAnimationDef { Family = FaunaAnimationFamily.Flight, CyclesPerBody = 2, IdleHz = 3, MaxHz = 4 };
        Assert.Equal(3, FaunaGait.Advance(a, 0, 0.1, 1));
        Assert.Equal(11, FaunaGait.Advance(a, 0.4, 0.1, 1));
    }

    [Fact]
    public void WalkingMeshesCarryAnchoredSocketsAndFullStrokeToesAfterMorphing()
    {
        foreach (var sp in TestUtil.Content.Fauna.Where(s => FaunaGait.GroundSteps(s.Animation)))
        {
            var m = OrganismMeshes.Fauna(sp, 1234);
            var weights = Enumerable.Range(0, m.VertexCount).Where(i => m.Colors[i * 4 + 3] > 0.5 && m.UV2[i * 2 + 1] >= 1 && m.UV2[i * 2 + 1] <= 1.25)
                .Select(i => (m.UV2[i * 2 + 1] - 1) * 4).ToArray();
            Assert.Contains(0f, weights); Assert.Contains(1f, weights);
            Assert.All(weights, w => Assert.InRange(w, 0, 1));
        }
    }
}
