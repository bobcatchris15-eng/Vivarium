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

    private static double Total(VivariumWorld w) { var b = w.Water.Budget; return w.Water.Volume() + b.BoundaryOutflow + b.GroundwaterRecharge + b.Evaporation + b.Infiltration - b.SpringInflow - b.GroundwaterInflow; }

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
        Assert.False(w.Water.IsWet(Vec2.Zero));
        Assert.True(double.IsNaN(w.Water.SurfaceAt(Vec2.Zero)));
        var digest=w.Water.DigestHex();
        Assert.Equal(0,WaterMesh.Build(w).TriangleCount);
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
    public void SaturatedSoilNeverDeletesFlowingWater()
    {
        var w=TestUtil.FlatWorld(31,d=>d.Water.WaterTable=1);
        w.Water.AddWater(Vec2.Zero,.3,.01);
        w.Water.Step(30);
        Assert.Equal(.01,w.Water.Volume()+w.Water.Budget.BoundaryOutflow,9);
        Assert.Equal(0,w.Water.Budget.GroundwaterRecharge);
    }

    [Fact]
    public void LegacyCombinedWaterSaveMigratesGroundwaterOutOfDynamicStorage()
    {
        var w = TestUtil.FlatWorld(61, d =>
        {
            d.Water.WaterTable = 0.25;
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 0, Z = 0, Radius = 2.5, Amount = 0.8 });
        });
        var payloads = Persistence.WorldSerializer.Serialize(w);
        var legacyDepth = new double[w.Grid.DomainCells.Length];
        for (int k = 0; k < legacyDepth.Length; k++)
            legacyDepth[k] = w.Water.OpenWaterDepth(w.Grid.DomainCells[k]);

        payloads["water"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new Persistence.WaterPayload
            {
                FormatVersion = null,
                Depth = Persistence.WorldSerializer.Pack(legacyDepth),
                FaceFlowEast = null,
                FaceFlowNorth = null,
                Budget = new Vivarium.Sim.Water.WaterBudget(),
            },
            Persistence.WorldSerializer.Json);

        var loaded = Persistence.WorldSerializer.Deserialize(w.Content, payloads);
        foreach (int c in loaded.Grid.DomainCells.Where(loaded.Water.IsWaterTable))
            Assert.Equal(0.0, loaded.Water.SurfaceWaterDepth(c), 12);
        Assert.True(loaded.Water.IsWaterTable(Vec2.Zero));
        Assert.True(double.IsNaN(loaded.Water.SurfaceAt(Vec2.Zero)));
    }

    [Fact] // t-053
    public void SpringsOutsideAreRejectedAndValidSpringsFlowDeterministically()
    {
        var d = TestUtil.FlatDescriptor();
        d.Water.Springs.Add(new SpringConfig { X = 0, Z = 9, Discharge = 0.1 });
        Assert.Throws<ArgumentException>(() => VivariumWorld.Create(TestUtil.Content, d, false));
        var w = TestUtil.FlatWorld(1, x => x.Water.Springs.Add(new SpringConfig { X = 0, Z = 0, Discharge = 0.036 })); // 1e-5 mÂ³/s
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
        for (int i = 0; i < 100; i++)
        {
            w.Water.Step(2);
            foreach (int c in w.Grid.DomainCells) Assert.True(w.Water.Depth[c] >= 0);
        }
        Assert.True(CentreOfMassX(w) < comBefore - 0.5, "water should move toward lower ground (âˆ’X)");
        // conservation: nothing created or destroyed except tracked boundary outflow
        Assert.Equal(v0, w.Water.Volume() + w.Water.Budget.BoundaryOutflow, 9);
        var w2 = Slope(); TestUtil.Flood(w2, high, 0.6, 0.05);
        for (int i = 0; i < 100; i++) w2.Water.Step(2);
        Assert.Equal(w.Water.DigestHex(), w2.Water.DigestHex());
    }

    [Fact]
    public void SurfaceSolverStoresSignedFaceMomentumAndConservesTransfers()
    {
        var w = Slope();
        var high = new Vec2(2.5, 0);
        TestUtil.Flood(w, high, 0.45, 0.04);
        double v0 = w.Water.SurfaceVolume();
        w.Water.Step(30);

        Assert.Contains(w.Grid.DomainCells, c => Math.Abs(w.Water.FaceFlowEast[c]) > 1e-10 || Math.Abs(w.Water.FaceFlowNorth[c]) > 1e-10);
        Assert.Equal(v0, w.Water.SurfaceVolume() + w.Water.Budget.BoundaryOutflow, 9);
        Assert.True(w.Water.AllFinite());
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
        w.Water.Springs[0].Discharge=.02; // faster physical source makes the fill/overflow test bounded
        for (int hour = 0; hour < 30; hour++)
        {
            for (int i = 0; i < 5; i++) w.Water.Step(2);
            // Compare the pool away from the continuously injected source cell.
            levels.Add(w.Grid.DomainCells.Where(c=>c!=centre && w.Water.Depth[c]>.05 && w.Grid.CellCenter(c).Length<1.5)
                .Average(c=>w.Water.Bed[c]+w.Water.Depth[c]));
        }
        // accumulates in the depression instead of draining away
        Assert.True(w.Water.Depth[centre] > 0.2, $"pond depth {w.Water.Depth[centre]:0.000}");
        // settles near the rim level (stable) ...
        Assert.InRange(levels[^1], rim - 0.03, rim + 0.10); // overflow sheet needs a few cm of head
        Assert.True(Math.Abs(levels[^1] - levels[^5]) < 0.004, $"pool level must settle once full: {levels[^5]:0.000000} -> {levels[^1]:0.000000}");
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
        for (int i = 0; i < 100; i++) w.Water.Step(2);
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

    [Fact]
    public void WaterMeshSnapshotPreservesGeometryAcrossLaterDepthChanges()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var snapshot = WaterMesh.Capture(w);
        string original = WaterMesh.Build(w).DigestHex();
        Assert.Equal(original, WaterMesh.Build(w, snapshot).DigestHex());

        int wet = w.Grid.DomainCells.First(w.Water.IsWet);
        w.Water.Depth[wet] += 0.25;
        w.Water.FlowX[wet] += 0.2;
        Assert.Equal(original, WaterMesh.Build(w, snapshot).DigestHex());
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
        }
        // A source-fed default basin may still be filling; unlike the former implicit plane it need not reach the cut.
        // visual flow follows the simulated current (interpolated smoothly between cells, so compare on average
        // over clearly flowing water rather than vertex by vertex)
        int compared = 0; double agree = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mesh.NormalAt(i).Y < 0.5) continue;
            int cell = w.Grid.CellAt(mesh.Position(i).XZ);
            if (!w.Grid.InDomain(cell) || !w.Water.IsWet(cell)) continue;
            var sim = new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]);
            var vis = new Vec2(mesh.UV2[i * 2], mesh.UV2[i * 2 + 1]);
            if (sim.Length < 1e-7 || vis.Length < 1e-12) continue;
            agree += sim.Normalized().Dot(vis.Normalized());
            compared++;
        }
        Assert.True(compared > 20);
        Assert.True(agree / compared > 0.8, $"mean alignment {agree / compared:0.00} over {compared} vertices");
    }

    private static IEnumerable<int> Neighbours(VivariumWorld w, int c)
    {
        int i = c % w.Grid.Nx, j = c / w.Grid.Nx;
        int r = WaterMesh.RingCells;   // the mesh covers a ring of dry cells around the water (hidden where ground is higher)
        for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++) if (w.Grid.InDomain(i + di, j + dj)) yield return w.Grid.Index(i + di, j + dj);
    }

    [Fact] // t-064
    public void HydrologySuiteDigestIsRepeatable()
    {
        string Run()
        {
            var w = TestUtil.DefaultWorld(populate: false);
            for (int i = 0; i < 60; i++) w.Water.Step(2);
            for (int i = 0; i < 10; i++) w.Water.CoupleMoisture(w.Fields.Moisture, w.Content.Ecology, 1800, w.Fields.Scratch);
            return w.Water.DigestHex() + w.Fields.Moisture.DigestHex();
        }
        Assert.Equal(Run(), Run());
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
        Assert.True(samples[0] > 0.8 && samples[^1] < 0.4, $"bank wet, far ground dry: {samples[0]:0.00} â€¦ {samples[^1]:0.00}");
        var d = w.Water.DistanceToWater();
        Assert.Equal(0, d[w.Grid.CellAt(pond)]);
        Assert.InRange(d[w.Grid.CellAt(pond + new Vec2(2.0, 0))], 0.7, 1.3);
    }

    [Fact]
    public void WaterSurfaceIsDrawnFinerThanTheHydrologyGrid()
    {
        var w = TestUtil.DefaultWorld(populate: false);
        var mesh = WaterMesh.Build(w);
        double sub = w.Grid.CellSize / WaterMesh.Subdivisions;
        int offGrid = 0;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Position(i);
            Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z));
            double fx = (p.X - w.Grid.OriginX) / w.Grid.CellSize;
            if (mesh.NormalAt(i).Y > 0.5 && Math.Abs(fx - Math.Round(fx)) > 0.25 && Math.Abs((p.X - w.Grid.OriginX) / sub - Math.Round((p.X - w.Grid.OriginX) / sub)) < 1e-6) offGrid++;
        }
        Assert.True(offGrid > 100, $"sub-cell vertices: {offGrid}");
        // the surface is never lifted over the bank: no vertex rises above the water level of the wet cells around it
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            if (mesh.NormalAt(i).Y < 0.5) continue;
            var p = mesh.Position(i);
            int c = w.Grid.CellAt(p.XZ);
            int ci = c % w.Grid.Nx, cj = c / w.Grid.Nx;
            double top = double.NegativeInfinity;
            int reach = WaterMesh.RingCells + 1;
            for (int dj = -reach; dj <= reach; dj++) for (int di = -reach; di <= reach; di++)
            {
                if (!w.Grid.InDomain(ci + di, cj + dj)) continue;
                int k = w.Grid.Index(ci + di, cj + dj);
                if (w.Water.IsWet(k)) { var ws = w.Water.SurfaceAt(w.Grid.CellCenter(k)); if (!double.IsNaN(ws)) top = Math.Max(top, ws); }
            }
            Assert.True(p.Y <= top + 1e-6, $"surface at {p} is above the nearby water level {top:0.000}");
        }
    }

    [Fact]
    public void PooledAndFlowingWaterShareDepthButDifferInVelocity()
    {
        var w=TestUtil.FlatWorld();
        TestUtil.Flood(w,Vec2.Zero,1,.1);
        Assert.True(w.Water.IsWet(Vec2.Zero));
        Assert.False(w.Water.IsStream(Vec2.Zero));
        w.Water.Step(.1);
        Assert.Contains(w.Grid.DomainCells,w.Water.IsStream);
    }

    [Fact]
    public void SubmergedFloraUsesActualFluidDepth()
    {
        var w=TestUtil.FlatWorld();
        TestUtil.Flood(w,Vec2.Zero,1,.1);
        var s=w.FloraSystem.Suitability(w.Content.FloraOrThrow("streamribbon"),Vec2.Zero);
        Assert.DoesNotContain("standing water",s.RefusalReason);
        Assert.DoesNotContain("submerged",s.RefusalReason);
    }

    [Fact]
    public void SwimmersUseActualFluidDepth()
    {
        var w=TestUtil.FlatWorld();
        TestUtil.Flood(w,Vec2.Zero,1,.15);
        TestUtil.Condition(w,.9,.7);
        var sp=w.Content.FaunaOrThrow("emberglass_swimmer");
        Assert.True(w.FaunaSystem.IsPassable(sp,Vec2.Zero));
        Assert.True(w.FaunaSystem.RestingY(sp,Vec2.Zero)>w.Terrain.Height(Vec2.Zero));
    }

    [Fact]
    public void OnlyStoredFluidProducesSurfaceGeometry()
    {
        var w=TestUtil.FlatWorld(51,d=>d.Water.WaterTable=1);
        Assert.Equal(0,WaterMesh.Build(w).TriangleCount);
        TestUtil.Flood(w,Vec2.Zero,.8,.08);
        var set=WaterMesh.BuildSet(w);
        Assert.True(set.SurfaceMesh.TriangleCount>0);
    }

    [Fact]
    public void PoolsAndStreamsUseTheSameSurfaceMesh()
    {
        var w=TestUtil.FlatWorld();
        TestUtil.Flood(w,Vec2.Zero,1,.1);
        var set=WaterMesh.BuildSet(w);
        Assert.Equal(set.SurfaceMesh.DigestHex(),WaterMesh.Build(w).DigestHex());
    }
}

public class HydrologyBankContainmentTests
{
    /// <summary>Spring inside a closed bowl with a second dry bowl over the rim: water must pool, never jump the bank.</summary>
    [Theory]
    [InlineData(0.01, 20)]
    [InlineData(0.2, 2)]
    [Trait("Suite", "Hydrology")]
    public void SpringWaterStaysBehindBanksUntilBowlOverflows(double dischargePerHour, int hours)
    {
        var w = TestUtil.FlatWorld(7, d =>
        {
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = -2, Z = 0, Radius = 1.6, Amount = 0.35 });
            d.Terrain.Features.Add(new TerrainFeature { Type = "basin", X = 2, Z = 0, Radius = 1.2, Amount = 0.35 });
        });
        // added after creation so the world's settle pass does not pre-fill the bowl
        w.Water.Springs.Add(new Vivarium.Sim.Water.Spring { X = -2, Z = 0, Discharge = dischargePerHour * hours / 120 });
        for (int i = 0; i < 60; i++) w.Water.Step(2);
        double inBowl = 0, outside = 0, lo = double.MaxValue, hi = double.MinValue;
        foreach (int c in w.Grid.DomainCells)
        {
            var p = w.Grid.CellCenter(c);
            double v = w.Water.Depth[c] * w.Water.CellArea;
            if ((p - new Vec2(-2, 0)).Length < 1.6) inBowl += v; else outside += v;
            if ((p - new Vec2(-2, 0)).Length < 0.8 && w.Water.Depth[c] > .001) { double sfc = w.Water.Bed[c] + w.Water.Depth[c]; lo = Math.Min(lo, sfc); hi = Math.Max(hi, sfc); }
        }
        // a pool finds its level: no odd/even checkerboard of wet and dry cells
        Assert.True(hi - lo < 0.02, $"pool surface not level: {lo:0.###}..{hi:0.###} m");
        Assert.True(inBowl > 0, "spring produced no water");
        Assert.True(w.Water.OpenWaterDepth(new Vec2(2, 0)) < 1e-4, $"far bowl flooded: {w.Water.OpenWaterDepth(new Vec2(2, 0)):0.####} m, outside volume {outside:0.####} m³ vs bowl {inBowl:0.####} m³");
        Assert.True(outside < inBowl * 0.01, $"water escaped the bowl: outside {outside:0.####} m³ vs bowl {inBowl:0.####} m³");
    }
}
