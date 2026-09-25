using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Persistence;
using Xunit;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Coverage")]
public class CoverageTests
{
    [Fact]
    public void TileAllocatesOnWriteAndFreesWhenEmpty()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 1);
        Assert.Equal(0, layer.TileCount);

        layer.SetOcc(5, 5, 1);
        Assert.Equal(1, layer.TileCount);
        Assert.Equal(1, layer.GetOcc(5, 5));

        layer.SetOcc(5, 5, 0); // clear the only occupied cell
        for (int i = 0; i <= CoverageSpec.FreeAfterEmptySteps; i++) layer.Advance();
        Assert.Equal(0, layer.TileCount);
    }

    [Fact]
    public void TilesIterateInSortedKeyOrderRegardlessOfAllocationOrder()
    {
        var layer = new CoverageLayer(CoverageLayerId.Crust, 7);
        layer.SetOcc(200, 5, 1);   // tile (6, 0)
        layer.SetOcc(-10, -10, 1); // tile (-1, -1)
        layer.SetOcc(0, 0, 1);     // tile (0, 0)

        var order = layer.Tiles.Select(t => (t.Ti, t.Tj)).ToList();
        var sorted = order.OrderBy(k => k).ToList();
        Assert.Equal(sorted, order);
    }

    [Fact]
    public void Hash01IsOrderIndependentAndDeterministic()
    {
        ulong seed = 12345;
        double a = HashRng.Hash01(seed, (int)CoverageLayerId.Mat, 3, -2, 17, 40, 0);
        double b = HashRng.Hash01(seed, (int)CoverageLayerId.Mat, 3, -2, 17, 40, 0);
        Assert.Equal(a, b);

        // different purpose or step must (almost certainly) differ
        double c = HashRng.Hash01(seed, (int)CoverageLayerId.Mat, 3, -2, 17, 40, 1);
        double d = HashRng.Hash01(seed, (int)CoverageLayerId.Mat, 3, -2, 17, 41, 0);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
        Assert.InRange(a, 0.0, 1.0);
    }

    [Fact]
    public void DoubleBufferedNeighbourReadsCrossTileBorders()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 1);
        // one cell on either side of a tile boundary at gx = 31/32
        layer.SetOcc(31, 0, 1);
        layer.SetB(31, 0, 0.5f);
        layer.BeginStep();

        Assert.Equal(1, layer.SnapshotOcc(31, 0));
        Assert.Equal(0.5f, layer.SnapshotB(31, 0));
        // neighbour in the next tile reads across the border correctly (still empty)
        Assert.Equal(0, layer.SnapshotOcc(32, 0));

        // mutate the live buffer after BeginStep(); the snapshot must not change
        layer.SetOcc(31, 0, 0);
        Assert.Equal(1, layer.SnapshotOcc(31, 0));
        Assert.Equal(0, layer.GetOcc(31, 0));
    }

    [Fact]
    public void ActiveTilesTracksDirtyTiles()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 1);
        var t = layer.GetOrCreateTile(0, 0);
        Assert.False(t.Active);
        layer.SetOcc(0, 0, 2);
        Assert.Contains(t, layer.ActiveTiles);
        layer.ClearActive(t);
        Assert.DoesNotContain(t, layer.ActiveTiles);
    }

    [Fact]
    public void VersionBumpsOnChangeAndChangedTilesReflectsIt()
    {
        var layer = new CoverageLayer(CoverageLayerId.Mat, 1);
        layer.SetOcc(0, 0, 1);
        var t = layer.GetOrCreateTile(0, 0);
        long v0 = t.Version;
        Assert.Single(layer.ChangedTiles(-1));

        layer.SetB(0, 0, 0.2f);
        Assert.True(t.Version > v0);
        Assert.Single(layer.ChangedTiles(v0));
        Assert.Empty(layer.ChangedTiles(t.Version));
    }

    [Fact]
    public void CoverageRoundTripsThroughSaveLoadWithIdenticalDigest()
    {
        var content = TestUtil.Content;
        var w = TestUtil.FlatWorld(101);
        w.Coverage.Mat.SetCell(3, 4, occ: 1, b: 0.42f, w: 200, age: 17, dorm: 2, flags: (byte)CoverageFlags.Rim, d2e: 5);
        w.Coverage.Crust.SetOcc(-3, -1, 2);
        w.Coverage.Plasmodium.SetB(50, 50, 0.9f);

        var digestBefore = WorldSerializer.Digest(w);
        var payloads = WorldSerializer.Serialize(w);
        var loaded = WorldSerializer.Deserialize(content, payloads);
        var digestAfter = WorldSerializer.Digest(loaded);

        Assert.Equal(digestBefore, digestAfter);
        Assert.Equal(1, loaded.Coverage.Mat.GetOcc(3, 4));
        Assert.Equal(0.42f, loaded.Coverage.Mat.GetB(3, 4));
        Assert.Equal(2, loaded.Coverage.Crust.GetOcc(-3, -1));
        Assert.Equal(0.9f, loaded.Coverage.Plasmodium.GetB(50, 50));

        // a changed cell must change the digest
        loaded.Coverage.Mat.SetB(3, 4, 0.1f);
        Assert.NotEqual(digestAfter, WorldSerializer.Digest(loaded));
    }

    // ------------------------------------------------------------------ toy rule determinism (§1.5, §12 determinism)

    /// <summary>A trivial isotropic Eden-growth rule used only to exercise iteration/hash determinism.</summary>
    private static void RunToyDisc(CoverageLayer layer, long steps, IEnumerable<(int ti, int tj)> seedOrder)
    {
        // seed a single cell; allocate tiles in the given (possibly shuffled) order first, to prove
        // allocation order does not affect the resulting bytes.
        foreach (var (ti, tj) in seedOrder) layer.GetOrCreateTile(ti, tj);
        layer.SetOcc(0, 0, 1);

        for (long s = 0; s < steps; s++)
        {
            layer.BeginStep();
            var candidates = new List<(int gx, int gz)>();
            foreach (var t in layer.Tiles.ToList())
            {
                for (int lz = 0; lz < CoverageSpec.TileEdge; lz++)
                for (int lx = 0; lx < CoverageSpec.TileEdge; lx++)
                {
                    int gx = t.Ti * CoverageSpec.TileEdge + lx, gz = t.Tj * CoverageSpec.TileEdge + lz;
                    if (layer.SnapshotOcc(gx, gz) != 0) candidates.Add((gx, gz));
                }
            }
            foreach (var (gx, gz) in candidates)
            {
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = gx + dx, nz = gz + dz;
                    if (layer.SnapshotOcc(nx, nz) != 0) continue;
                    double u = layer.Hash01(nx, nz, s, purpose: 0);
                    if (u < 0.3) layer.SetOcc(nx, nz, 1);
                }
            }
            layer.Advance();
        }
    }

    private static byte[] DigestBytes(CoverageLayer layer)
    {
        using var d = new Core.DigestBuilder();
        foreach (var t in layer.Tiles)
        {
            d.Add(t.Ti); d.Add(t.Tj); d.Add(t.Occ);
        }
        return Convert.FromHexString(d.Hex());
    }

    [Fact]
    public void ToyRuleIsDeterministicUnderShuffledTileAllocationOrder()
    {
        var order1 = new (int, int)[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1) };
        var order2 = order1.Reverse().ToArray();

        var layerA = new CoverageLayer(CoverageLayerId.Mat, 999);
        RunToyDisc(layerA, 6, order1);

        var layerB = new CoverageLayer(CoverageLayerId.Mat, 999);
        RunToyDisc(layerB, 6, order2);

        Assert.Equal(DigestBytes(layerA), DigestBytes(layerB));

        // running it again from scratch with the original order reproduces the same bytes too
        var layerC = new CoverageLayer(CoverageLayerId.Mat, 999);
        RunToyDisc(layerC, 6, order1);
        Assert.Equal(DigestBytes(layerA), DigestBytes(layerC));
    }
}
