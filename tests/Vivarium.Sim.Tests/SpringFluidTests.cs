using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
using Vivarium.Sim.Tools;
using Vivarium.Sim.Content;
namespace Vivarium.Sim.Tests;
[Trait("Suite", "Hydrology")]
public class SpringFluidTests
{
    [Fact]
    public void LowGroundWithoutASourceStaysDryRegardlessOfSoilWaterTable()
    {
        var w=TestUtil.FlatWorld(3,d=>d.Water.WaterTable=1);
        w.Water.Step(60);
        Assert.Equal(0,w.Water.Volume());
        Assert.False(w.Water.HasSurfaceWater(Vec2.Zero));
        Assert.Equal(0.0, w.Water.SurfaceWaterDepth(Vec2.Zero));
        Assert.Equal(0,WaterMesh.Build(w).TriangleCount);
    }
    [Fact]
    public void PointSpringCreatesMobileWaterAndConservesVolume()
    {
        var w=TestUtil.FlatWorld();
        w.Water.Springs.Add(new Spring{Id=w.Ids.Next(EntityKind.Spring),X=0,Z=0,Discharge=.002});
        w.Water.Step(30);
        Assert.Equal(.06,w.Water.Budget.SpringInflow,8);
        Assert.Equal(.06,w.Water.Volume()+w.Water.Budget.BoundaryOutflow,8);
        Assert.True(w.Grid.DomainCells.Count(c=>w.Water.Depth[c]>1e-5)>4);
        Assert.Contains(w.Water.FaceFlowEast,q=>Math.Abs(q)>1e-8);
        Assert.All(w.Water.Depth,h=>Assert.True(double.IsFinite(h)&&h>=0));
        var meshes=WaterMesh.BuildSet(w);
        Assert.True(meshes.SurfaceMesh.TriangleCount>0);
    }
    [Fact]
    public void FlowRetainsMomentumAtEqualSurfaceLevel()
    {
        var w=TestUtil.FlatWorld();
        int c=w.Grid.CellAt(Vec2.Zero);
        w.Water.Depth[c]=.1;w.Water.Depth[c+1]=.1;
        w.Water.FaceFlowEast[c]=.0001;
        w.Water.Step(.00001);
        Assert.True(w.Water.FaceFlowEast[c]>0);
        Assert.Equal(.2*w.Water.CellArea,w.Water.Volume()+w.Water.Budget.BoundaryOutflow,10);
    }
    [Fact]
    public void BankBlocksFlowUntilWaterOvertopsIt()
    {
        var w=TestUtil.FlatWorld();
        int c=w.Grid.CellAt(Vec2.Zero);
        foreach(int i in w.Grid.DomainCells) w.Water.Bed[i]=1;
        w.Water.Bed[c]=0;w.Water.Depth[c]=.2;
        w.Water.Step(1);
        Assert.Equal(.2,w.Water.Depth[c],10);
        Assert.Equal(0,w.Water.FaceFlowEast[c]);
        w.Water.Depth[c]=1.2;
        w.Water.Step(.01);
        Assert.True(w.Water.Depth[c+1]>0);
    }
    [Fact]
    public void SavedFluidPreservesVolumeMomentumAndFurtherEvolution()
    {
        var w=TestUtil.FlatWorld();
        w.Water.Springs.Add(new Spring{Id=w.Ids.Next(EntityKind.Spring),X=0,Z=0,Discharge=.002});
        w.Water.Step(10);
        var loaded=Vivarium.Sim.Persistence.WorldSerializer.Deserialize(w.Content,Vivarium.Sim.Persistence.WorldSerializer.Serialize(w));
        Assert.Equal(w.Water.DigestHex(),loaded.Water.DigestHex());
        w.Water.Step(.2);loaded.Water.Step(.2);
        Assert.Equal(w.Water.DigestHex(),loaded.Water.DigestHex());
    }

    [Fact]
    public void EndToEndSpringHoleFillingOvertoppingDownhillFlowLeachingAndRecharge()
    {
        // 0. Setup world with WaterTable at 0.2 and infiltration configured for substrate leaching
        var w = TestUtil.FlatWorld(3, d =>
        {
            d.Water.WaterTable = 0.2;
            d.Water.Infiltration = 20.0;
        });

        // Shape terrain: elevated plateau around spring (x <= 0.5) at height 1.0,
        // sloping downhill along +X to height 0.1 (below WaterTable = 0.2) at x >= 1.5.
        var hf = w.Terrain;
        hf.BeginEdit();
        for (int j = 0; j < hf.Nz; j++)
        {
            for (int i = 0; i < hf.Nx; i++)
            {
                var pos = hf.VertexPos(i, j);
                double h;
                if (pos.X <= 0.5)
                {
                    h = 1.0;
                }
                else if (pos.X < 1.5)
                {
                    double t = (pos.X - 0.5) / 1.0;
                    h = 1.0 - t * 0.9;
                }
                else
                {
                    h = 0.1;
                }
                hf.H[hf.VertexIndex(i, j)] = h;
            }
        }
        hf.Touch();
        w.Water.RefreshBed(hf);

        var springPos = new Vec2(0, 0);
        double preHeight = w.Terrain.Height(springPos);
        Assert.Equal(1.0, preHeight, 3);

        // (1) Placing a spring carves the depression and spawns SpringVent prop.
        var tools = new ToolActions(w);
        double dischargeRate = 0.001; // default ~0.001 m³/s
        var toolResult = tools.ToggleSpring(springPos, dischargeRate);
        Assert.True(toolResult.Ok);

        // Verify depression carved
        double carvedHeight = w.Terrain.Height(springPos);
        Assert.True(carvedHeight < preHeight);
        Assert.Equal(1.0 - 0.08, carvedHeight, 2);

        // Verify SpringVent prop spawned
        var vent = Assert.Single(w.Props.Props, p => p.Kind == PropKind.SpringVent);
        Assert.Equal(springPos.X, vent.X, 3);
        Assert.Equal(springPos.Z, vent.Z, 3);
        Assert.Equal(carvedHeight, vent.Y, 3);

        // (2) Sub-stepping hydrology fills the depression.
        // (3) Fluid only spills over to neighbor cells when water surface reaches the rim sill.
        int springCell = w.Grid.NearestDomainCell(springPos);
        double rimSillElevation = 1.0;

        // Advance a small step where depression is filling but water surface remains below rim sill
        w.Water.Step(0.2);
        double earlySurface = w.Water.Bed[springCell] + w.Water.Depth[springCell];
        Assert.True(earlySurface < rimSillElevation, $"Water surface {earlySurface} should be below rim {rimSillElevation}");
        Assert.True(w.Water.Depth[springCell] > 0, "Depression cell should contain water");

        // Cells outside the depression radius (r >= 0.35) must have NO water yet
        foreach (int c in w.Grid.DomainCells)
        {
            if (Vec2.Distance(w.Grid.CellCenter(c), springPos) >= 0.35)
            {
                Assert.Equal(0.0, w.Water.Depth[c]);
            }
        }

        // Sub-step further until water fills and overtops the rim sill
        w.Water.Step(15.0);

        // Verify water has overtopped: water surface reached rim sill and spilled out
        double fullSurface = w.Water.Bed[springCell] + w.Water.Depth[springCell];
        Assert.True(fullSurface >= rimSillElevation - 1e-4, "Water surface must reach the rim sill to overflow");

        // (4) Fluid flows downhill and gets absorbed into groundwater recharge when reaching water table.
        // Check that downhill cells (x > 0.3) received water flow
        var downhillCells = w.Grid.DomainCells.Where(c => w.Grid.CellCenter(c).X > 0.35 && w.Grid.CellCenter(c).X < 1.0).ToList();
        Assert.True(downhillCells.Any(c => w.Water.Depth[c] > 0 || w.Water.FlowX[c] > 0), "Fluid should flow downhill");

        // Check leaching / infiltration occurred on unsaturated ground
        Assert.True(w.Water.Budget.Infiltration > 0, "Substrate infiltration (leaching) should have occurred");

        // Check water table absorption: low cells where Bed <= WaterTable absorb fluid into GroundwaterRecharge
        Assert.True(w.Water.Budget.GroundwaterRecharge > 0, "Fluid reaching water table must be absorbed into groundwater recharge");

        // (5) Mass conservation is strictly preserved
        double totalInflow = w.Water.Budget.SpringInflow + w.Water.Budget.ToolInflow;
        double totalAccounted = w.Water.Volume()
            + w.Water.Budget.GroundwaterRecharge
            + w.Water.Budget.Infiltration
            + w.Water.Budget.Evaporation
            + w.Water.Budget.BoundaryOutflow
            + w.Water.Budget.ToolRemoval;

        Assert.True(totalInflow > 0, "Inflow must be positive");
        Assert.Equal(totalInflow, totalAccounted, 6);
    }
}
