using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class FloraDistanceDetailTests
{
    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(5.99, 0)]
    [InlineData(6, 1)]
    [InlineData(12, 2)]
    public void InitialDistanceBands(double depth, int expected) =>
        Assert.Equal(expected, FloraDetail.SelectDistance(depth));

    [Fact]
    public void MotionWithinBoundaryBandDoesNotToggleTiers()
    {
        Assert.Equal(0, FloraDetail.SelectDistance(6.5, 0));
        Assert.Equal(1, FloraDetail.SelectDistance(5.5, 1));
        Assert.Equal(1, FloraDetail.SelectDistance(13, 1));
        Assert.Equal(2, FloraDetail.SelectDistance(11, 2));
        Assert.Equal(0, FloraDetail.SelectDistance(5, 2));
        Assert.Equal(2, FloraDetail.SelectDistance(14, 0));
    }

    [Fact]
    public void EarlierBandsKeepNearPlantsAndBoundaryHysteresis()
    {
        const double scale = 5.0 / 6.0;
        Assert.Equal(0, FloraDetail.SelectDistance(4.99, distanceScale: scale));
        Assert.Equal(1, FloraDetail.SelectDistance(5, distanceScale: scale));
        Assert.Equal(2, FloraDetail.SelectDistance(10, distanceScale: scale));
        Assert.Equal(0, FloraDetail.SelectDistance(5.4, 0, scale));
        Assert.Equal(1, FloraDetail.SelectDistance(4.6, 1, scale));
        Assert.Equal(1, FloraDetail.SelectDistance(10.9, 1, scale));
        Assert.Equal(2, FloraDetail.SelectDistance(9.1, 2, scale));
        Assert.Equal(0, FloraDetail.SelectDistance(4.4, 2, scale));
    }
}
