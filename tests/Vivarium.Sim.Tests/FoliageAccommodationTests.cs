using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class FoliageAccommodationTests
{
    private static AccommodationCapsule Stem(ulong owner, ulong part, Vec3 start, Vec3 end, double radius = 0.05) =>
        new(new AccommodationKey(owner, part), AccommodationTissue.Stem, start, end, radius, 1);

    [Fact]
    public void QueriesAreStableAndRespectNegativeCellsAndActualBounds()
    {
        var index = new FoliageOccupancyIndex(0.25);
        index.Upsert(Stem(8, 1, new Vec3(-0.3, 0, 0), new Vec3(-0.3, 1, 0)));
        index.Upsert(Stem(2, 1, new Vec3(-0.2, 0, 0), new Vec3(-0.2, 1, 0)));
        index.Upsert(Stem(3, 1, new Vec3(5, 0, 0), new Vec3(5, 1, 0)));
        var found = index.Query(new Vec3(-0.5, 0.4, -0.1), new Vec3(0, 0.6, 0.1));
        Assert.Equal(new ulong[] { 2, 8 }, found.Select(p => p.Key.Organism));
        Assert.Empty(index.Query(new Vec3(0.5, 0, 0), new Vec3(1, 1, 1)));
    }

    [Fact]
    public void UpsertAndRemovalNeverLeaveStaleClaims()
    {
        var index = new FoliageOccupancyIndex();
        var old = Stem(1, 1, Vec3.Zero, Vec3.Up);
        index.Upsert(old);
        index.Upsert(Stem(1, 1, new Vec3(4, 0, 0), new Vec3(4, 1, 0)));
        Assert.Empty(index.Query(new Vec3(-1, -1, -1), new Vec3(1, 2, 1)));
        Assert.Single(index.Query(new Vec3(3, -1, -1), new Vec3(5, 2, 1)));
        Assert.True(index.Remove(old.Key));
        Assert.False(index.Remove(old.Key));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SignedDistanceHandlesCapsAndDegenerateSegments()
    {
        var stem = Stem(1, 1, Vec3.Zero, Vec3.Up, 0.1);
        Assert.Equal(-0.1, FoliageOccupancyIndex.SignedDistance(new Vec3(0, 0.5, 0), stem), 8);
        Assert.Equal(0.1, FoliageOccupancyIndex.SignedDistance(new Vec3(0.2, 0.5, 0), stem), 8);
        var point = Stem(1, 2, Vec3.Zero, Vec3.Zero, 0.1);
        Assert.Equal(0.9, FoliageOccupancyIndex.SignedDistance(Vec3.Up, point), 8);
    }

    [Fact]
    public void RejectsInvalidInputWithoutRemovingPreviousClaim()
    {
        var index = new FoliageOccupancyIndex();
        var original = Stem(1, 1, Vec3.Zero, Vec3.Up);
        index.Upsert(original);
        Assert.Throws<ArgumentException>(() => index.Upsert(original with { Radius = double.NaN }));
        Assert.Equal(1, index.Count);
    }
}
