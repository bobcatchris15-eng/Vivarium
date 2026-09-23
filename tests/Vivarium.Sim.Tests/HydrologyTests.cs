using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class HydrologyTests
{
    /// <summary>Tilted plane: falls from +X (high) toward -X; no springs, no losses.</summary>
    private static VivariumWorld Slope(Action<WorldDescriptor>? edit = null) => TestUtil.FlatWorld(7, d =>
    {
        d.Terrain.Features.Add(new TerrainFeature { Type = "ridge", X = 5, Z = -5, ToX = 5, ToZ = 5, Width = 9, Amount = 0.8 });
        edit?.Invoke(d);
    });

    private static double Total(VivariumWorld w) { var b = w.Water.Budget; return w.Water.Volume() + b.BoundaryOutflow + b.Evaporation + b.Infiltration - b.SpringInflow - b.GroundwaterInflow; }

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
    public void WaterTableIsDeterministicQueryableAndIndependentOfRenderMesh()
    {
        var w = TestUtil.FlatWorld(3, d =>
        {
            d.Water.WaterTable = 0.2;
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 0, Z = 0, Radius = 2.5, Amount = 0.8 });
        });
        var c = w.Grid.CellAt(Vec2.Zero);
        Assert.Equal(0.2, w.Water.WaterTable);
        Assert.True(w.Water.Depth[c] >= 0.2 - w.Water.Bed[c] - 1e-9);
        Assert.Equal(0.2, w.Water.SurfaceAt(Vec2.Zero), 2);
        var w2 = TestUtil.FlatWorld(3, d => { d.Water.WaterTable = 0.2; d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 0, Z = 0, Radius = 2.5, Amount = 0.8 }); });
        Assert.Equal(w.Water.DigestHex(), w2.Water.DigestHex());
        _ = WaterMesh.Build(w);                       // building visuals cannot change the state
        Assert.Equal(w.Water.DigestHex(), w2.Water.DigestHex());
        Assert.Contains("\"Depth\"", Persistence.WorldSerializer.Text(Persistence.WorldSerializer.Serialize(w)["water"]));
    }

    [Fact] // t-053
    public void SpringsOutsideAreRejectedAndValidSpringsFlowDeterministically()
    {
        var d = TestUtil.FlatDescriptor();
        d.Water.Springs.Add(new SpringConfig { X = 0, Z = 9, Discharge = 0.1 });
        Assert.Throws<ArgumentException>(() => VivariumWorld.Create(TestUtil.Content, d, false));
        var w = TestUtil.FlatWorld(1, x => x.Water.Springs.Add(new SpringConfig { X = 0, Z = 0, Discharge = 0.036 })); // 1e-5 m³/s
        double before = w.Water.Budget.SpringInflow;
        for (int i = 0; i < 60; i++) w.Water.Step(60);
        Assert.Equal(0.036, w.Water.Budget.SpringInflow - before, 6);        // exactly one hour of discharge
        Assert.True(w.Water.Depth[w.Grid.CellAt(Vec2.Zero)] > 0);
    }

    [Fact] // t-054, t-055
    public void SurfaceFlowRunsDownhillConservesVolumeAndStaysNonNegative()
    {
        var w = Slope();
        var high = new Vec2(3, 0);
        TestUtil.Flood(w, high, 0.6, 0.05);
        double v0 = w.Water.Volume();
        double comBefore = CentreOfMassX(w);
        for (int i = 0; i < 200; i++)
        {
            w.Water.Step(60);
            foreach (int c in w.Grid.DomainCells) Assert.True(w.Water.Depth[c] >= 0);
        }
        Assert.True(CentreOfMassX(w) < comBefore - 0.5, "water should move toward lower ground (−X)");
        // conservation: nothing created or destroyed except tracked boundary outflow
        Assert.Equal(v0, w.Water.Volume() + w.Water.Budget.BoundaryOutflow, 9);
        var w2 = Slope(); TestUtil.Flood(w2, high, 0.6, 0.05);
        for (int i = 0; i < 200; i++) w2.Water.Step(60);
        Assert.Equal(w.Water.DigestHex(), w2.Water.DigestHex());
    }

    private static double CentreOfMassX(VivariumWorld w)
    {
        double m = 0, mx = 0;
        foreach (int c in w.Grid.DomainCells) { m += w.Water.Depth[c]; mx += w.Water.Depth[c] * w.Grid.CellCenter(c).X; }
        return m > 0 ? mx / m : 0;
    }

    [Fact] // t-056
    public void BasinPondsToStableLevelThenOverflows()
    {
        var w = TestUtil.FlatWorld(11, d =>
        {
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 0, Z = 0, Radius = 2.0, Amount = 0.3 });
            d.Water.Springs.Add(new SpringConfig { X = 0, Z = 0, Discharge = 0.2 });
        });
        var centre = w.Grid.CellAt(Vec2.Zero);
        double rim = w.SurfaceHeight(new Vec2(2.2, 0));
        var levels = new List<double>();
        for (int hour = 0; hour < 60; hour++)
        {
            for (int i = 0; i < 60; i++) w.Water.Step(60);
            levels.Add(w.Water.Bed[centre] + w.Water.Depth[centre]);
        }
        // accumulates in the depression instead of draining away
        Assert.True(w.Water.Depth[centre] > 0.2, $"pond depth {w.Water.Depth[centre]:0.000}");
        // settles near the rim level (stable) ...
        Assert.InRange(levels[^1], rim - 0.03, rim + 0.10); // overflow sheet needs a few cm of head
        Assert.True(Math.Abs(levels[^1] - levels[^5]) < 0.004, "level must be stable once full");
        // ... and overflows predictably: water beyond the basin and leaving at the edge
        Assert.True(w.Water.Budget.BoundaryOutflow > 0, "overflow must reach the island edge");
        Assert.True(w.Water.IsWet(new Vec2(3.5, 0)) || w.Water.IsWet(new Vec2(-3.5, 0)) || w.Water.IsWet(new Vec2(0, 3.5)) || w.Water.IsWet(new Vec2(0, -3.5)));
    }

    [Fact] // t-057
    public void BoundaryOutflowIsTrackedAndNoSamplesExistOutside()
    {
        var w = Slope();
        var nearEdge = new Vec2(-w.Domain.Apothem * 0.9, 0);
        TestUtil.Flood(w, nearEdge, 0.8, 0.08);
        double v0 = w.Water.Volume();
        for (int i = 0; i < 200; i++) w.Water.Step(60);
        Assert.True(w.Water.Budget.BoundaryOutflow > v0 * 0.2, "water should leave through the cut face");
        Assert.Equal(v0, w.Water.Volume() + w.Water.Budget.BoundaryOutflow, 9);
        for (int c = 0; c < w.Grid.Count; c++) if (!w.Grid.InDomain(c)) Assert.Equal(0, w.Water.Depth[c]);
    }

    [Fact] // t-058
    public void WetBanksAreWetterThanDryInterior()
    {
        var w = TestUtil.FlatWorld(13);
        TestUtil.Flood(w, new Vec2(-2, 0), 0.8, 0.05);
        for (int i = 0; i < 48; i++) w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800, w.Fields.Scratch);
        double bank = w.Fields.Moisture.Sample(new Vec2(-1.05, 0));
        double dry = w.Fields.Moisture.Sample(new Vec2(2.5, 1.5));
        Assert.True(bank > dry + 0.3, $"bank {bank:0.00} vs dry {dry:0.00}");
    }

    [Fact] // t-059, t-060, t-062
    public void WaterGeometryFollowsWetCellsAndEndsCleanlyAtTheCut()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var before = w.Water.DigestHex();
        var mesh = WaterMesh.Build(w);
        Assert.Equal(before, w.Water.DigestHex());
        Assert.True(mesh.TriangleCount > 50);
        for (int i = 0; i < mesh.VertexCount; i++) Assert.True(w.Domain.SignedDistance(mesh.Position(i).XZ) <= 2e-6, "water geometry outside the island");
        int onCut = 0;
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            var a = mesh.Position(mesh.Indices[t]); var b = mesh.Position(mesh.Indices[t + 1]); var c = mesh.Position(mesh.Indices[t + 2]);
            var centroid = ((a + b + c) / 3).XZ;
            var n = mesh.NormalAt(mesh.Indices[t]);
            if (n.Y > 0.5)
            {
                int cell = w.Grid.CellAt(centroid);
                Assert.True(w.Water.IsWet(cell) || Neighbours(w, cell).Any(w.Water.IsWet), "surface triangle over dry ground");
            }
            else if (Math.Abs(w.Domain.SignedDistance(centroid)) < 1e-5) onCut++;
        }
        Assert.True(onCut > 0, "the default pond reaches the edge, so the cut face must show the water section");
        // visual flow direction equals simulated flow direction
        int compared = 0;
        for (int t = 0; t < mesh.Indices.Count; t += 3)
        {
            int i = mesh.Indices[t];
            if (mesh.NormalAt(i).Y < 0.5) continue;
            var centroid = ((mesh.Position(i) + mesh.Position(mesh.Indices[t + 1]) + mesh.Position(mesh.Indices[t + 2])) / 3).XZ;
            int cell = w.Grid.CellAt(centroid);   // the cell that emitted this triangle
            if (!w.Grid.InDomain(cell) || !w.Water.IsWet(cell)) continue;
            var sim = new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]);
            var vis = new Vec2(mesh.UV2[i * 2], mesh.UV2[i * 2 + 1]);
            if (sim.Length < 1e-9) continue;
            Assert.True(sim.Normalized().Dot(vis.Normalized()) > 0.999);
            compared++;
        }
        Assert.True(compared > 20);
    }

    private static IEnumerable<int> Neighbours(VivariumWorld w, int c)
    {
        int i = c % w.Grid.Nx, j = c / w.Grid.Nx;
        for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if (w.Grid.InDomain(i + di, j + dj)) yield return w.Grid.Index(i + di, j + dj);
    }

    [Fact] // t-064
    public void HydrologySuiteDigestIsRepeatable()
    {
        string Run()
        {
            var w = TestUtil.DefaultWorld(populate: false);
            for (int i = 0; i < 300; i++) w.Water.Step(60);
            for (int i = 0; i < 10; i++) w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800, w.Fields.Scratch);
            return w.Water.DigestHex() + w.Fields.Moisture.DigestHex();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void SoilMoistureFallsOffSmoothlyWithDistanceFromWater()
    {
        var w = TestUtil.FlatWorld(9);
        var pond = new Vec2(-2, 0);
        for (int k = 0; k < 400; k++)
        {
            TestUtil.Flood(w, pond, 1.0, 0.2);
            w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800, w.Fields.Scratch);
        }
        var samples = Enumerable.Range(0, 12).Select(i => w.Fields.Moisture.Sample(pond + new Vec2(1.1 + i * 0.25, 0))).ToList();
        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i] <= samples[i - 1] + 1e-9, $"moisture should not rise away from water: {string.Join(" ", samples.Select(x => x.ToString("0.00")))}");
            Assert.True(samples[i - 1] - samples[i] < 0.3, $"no hard edge between neighbouring cells: {string.Join(" ", samples.Select(x => x.ToString("0.00")))}");
        }
        Assert.True(samples[0] > 0.8 && samples[^1] < 0.4, $"bank wet, far ground dry: {samples[0]:0.00} … {samples[^1]:0.00}");
        var d = w.Water.DistanceToWater();
        Assert.Equal(0, d[w.Grid.CellAt(pond)]);
        Assert.InRange(d[w.Grid.CellAt(pond + new Vec2(2.0, 0))], 0.7, 1.3);
    }
}
