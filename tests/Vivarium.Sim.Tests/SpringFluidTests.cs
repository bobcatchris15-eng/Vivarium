using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
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
        Assert.False(w.Water.IsWet(Vec2.Zero));
        Assert.True(double.IsNaN(w.Water.SurfaceAt(Vec2.Zero)));
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
}
