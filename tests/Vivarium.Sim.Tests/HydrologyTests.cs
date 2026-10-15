using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class HydrologyTests
{
    [Fact] // t-051
    public void ConfigurationSupportsZeroOneOrManySpringsAndValidatesBounds()
    {
        foreach (int n in new[] { 0, 1, 3 })
        {
            var w = TestUtil.FlatWorld(1, d => { for (int i = 0; i < n; i++) d.Water.Springs.Add(new SpringConfig { X = i - 1, Z = 0.5, Discharge = 0.02 }); });
            Assert.Equal(n, w.Water.Springs.Count);
        }
        var bad = TestUtil.FlatDescriptor();
        bad.Water.Springs.Add(new SpringConfig { X = 30, Z = 0, Discharge = 0.1 });
        Assert.Contains(bad.Validate(), e => e.Contains("outside the island"));
        var src = new OverlayContentSource(TestUtil.ContentSource);
        src.Set("presets/default.json", File.ReadAllText(Path.Combine(TestUtil.ContentDir, "presets", "default.json")).Replace("\"flowRate\": 0.2", "\"flowRate\": 0.9"));
        var ex = Assert.Throws<ContentValidationException>(() => ContentLoader.Load(src));
        Assert.Contains(ex.Errors, e => e.Path == "$.water.flowRate");
    }

    [Fact] // t-052
    public void SoilWaterTableDoesNotCreateSurfaceWater()
    {
        var w=TestUtil.FlatWorld(3,d=>{d.Water.WaterTable=1;d.Terrain.Features.Add(new TerrainFeature{Type="basin",X=0,Z=0,Radius=2.5,Amount=.8});});
        Assert.True(w.Water.WaterTableDepth(Vec2.Zero)>0);
        Assert.False(w.Water.HasSurfaceWater(Vec2.Zero));
        Assert.Equal(0.0, w.Water.SurfaceWaterDepth(Vec2.Zero));
        Assert.True(w.Water.IsWet(Vec2.Zero));
        Assert.Equal(1.0, w.Water.SurfaceAt(Vec2.Zero), 8);
        var digest=w.Water.DigestHex();
        Assert.Empty(w.Liquid.CaptureRenderState().Cells);
        Assert.Equal(digest,w.Water.DigestHex());
    }

    [Fact]
    public void WaterOnSaturatedGroundRemainsFluidVolume()
    {
        var w=TestUtil.FlatWorld(31,d=>d.Water.WaterTable=1);
        w.Water.AddWater(Vec2.Zero,.3,.01);
        Assert.Equal(.01,w.Water.Volume(),10);
        Assert.Equal(0,w.Water.Budget.GroundwaterRecharge);
        Assert.True(w.Water.IsWet(Vec2.Zero));
        Assert.True(w.Water.SurfaceAt(Vec2.Zero)>w.Terrain.Height(Vec2.Zero));
    }

    [Fact]
    public void SoilHydrationRespondsToSurfaceWaterAmountWithoutLinearOverSaturation()
    {
        var w = TestUtil.FlatWorld(41);
        var shallowP = new Vec2(-2.5, 0);
        var deepP = new Vec2(2.5, 0);
        int shallow = w.Grid.CellAt(shallowP), deep = w.Grid.CellAt(deepP);
        w.Water.Depth[shallow] = 0.003;
        w.Water.Depth[deep] = 0.06;

        for (int k = 0; k < 12; k++)
            w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 900, w.Fields.Scratch);

        double a = w.Fields.Moisture[shallow], b = w.Fields.Moisture[deep];
        Assert.True(b > a + 0.12, $"deeper persistent surface water should hydrate more: shallow={a:0.00}, deep={b:0.00}");
        Assert.InRange(b, 0, 1);
        Assert.InRange(a, 0, 1);
    }

    [Fact]
    public void SoilMoistureFallsOffSmoothlyWithDistanceFromWater()
    {
        var w = TestUtil.FlatWorld(9);
        var pond = new Vec2(-2, 0);
        TestUtil.Flood(w,pond,1.0,.2);
        for (int k = 0; k < 400; k++)
        {
            w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800, w.Fields.Scratch);
        }
        var samples = Enumerable.Range(0, 12).Select(i => w.Fields.Moisture.Sample(pond + new Vec2(1.1 + i * 0.25, 0))).ToList();
        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i] <= samples[i - 1] + 1e-9, $"moisture should not rise away from water: {string.Join(" ", samples.Select(x => x.ToString("0.00")))}");
            Assert.True(samples[i - 1] - samples[i] < 0.3, $"no hard edge between neighbouring cells: {string.Join(" ", samples.Select(x => x.ToString("0.00")))}");
        }
        Assert.True(samples[0] > 0.8 && samples[^1] < 0.4, $"bank wet, far ground dry: {samples[0]:0.00} â€¦ {samples[^1]:0.00}");
        var d = w.Water.DistanceToWater();
        Assert.Equal(0, d[w.Grid.CellAt(pond)]);
        Assert.InRange(d[w.Grid.CellAt(pond + new Vec2(2.0, 0))], 0.7, 1.3);
    }

    [Fact]
    public void PointQueriesOnBankAboveWaterTableAreDryAndZeroDepth()
    {
        var w = TestUtil.FlatWorld(7, d =>
        {
            d.Water.WaterTable = 0.2;
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 0, Z = 0, Radius = 2.5, Amount = 0.6 });
        });
        Vec2 p = Vec2.Zero;
        bool found = false;
        for (double x = 0.0; x <= 2.5; x += 0.02)
        {
            var testP = new Vec2(x, 0);
            if (w.Terrain.Height(testP) >= w.Water.WaterTable)
            {
                p = testP;
                found = true;
                break;
            }
        }
        Assert.True(found, "sloping bank point above water table should exist");
        Assert.True(w.Terrain.Height(p) >= w.Water.WaterTable);
        Assert.Equal(0.0, w.Water.WaterTableDepth(p));
        Assert.False(w.Water.IsWaterTable(p));
        Assert.Equal(0.0, w.Water.OpenWaterDepth(p));
        Assert.False(w.Water.IsWet(p));
    }
}
