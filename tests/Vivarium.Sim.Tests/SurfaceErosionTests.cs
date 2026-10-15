using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;

public class SurfaceErosionTests
{
    private static VivariumWorld World()
    {var w=TestUtil.FlatWorld(edit:d=>{d.CellSize=.1;d.Diameter=5;});
        w.Terrain.BeginEdit();Array.Fill(w.Terrain.H,.3);w.Terrain.Touch();w.Water.RefreshBed(w.Terrain);
        Array.Fill(w.Fields.BaseSubstrate.Values,(byte)Substrate.Soil);return w;}
    private static FluidParticle Contact(VivariumWorld w,Vec2 p,Vec3 velocity,double height=0)
        =>new(Vec3.FromXZ(p,w.Terrain.Height(p)+SurfaceFluidDynamics.Radius(SurfaceFluidDynamics.ParcelVolume)+height),velocity,SurfaceFluidDynamics.ParcelVolume,123);
    [Fact] public void QuietPoolsAirborneJetsAndDryGroundCannotErode()
    {
        var w=World();double[] before=(double[])w.Terrain.H.Clone();
        foreach(var particle in new[]{Contact(w,Vec2.Zero,Vec3.Zero),Contact(w,Vec2.Zero,new Vec3(0,-2,0)),Contact(w,Vec2.Zero,new Vec3(1,0,0),.1)})
            w.Liquid.Erosion.Advance([particle],60);
        w.Liquid.Erosion.Advance([],60);Assert.Equal(before,w.Terrain.H);Assert.Null(w.Liquid.Erosion.Capture());
    }
    [Fact] public void ContactCutsSlowlyThenStopsPermanentlyEvenAfterRaisingAndReloading()
    {
        var w=World();var p=w.Terrain.VertexPos(w.Terrain.Nx/2,w.Terrain.Nz/2);
        double start=w.Terrain.Height(p);w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(1,0,0))],SurfaceErosion.EditInterval);
        Assert.InRange(start-w.Terrain.Height(p),.0001,SurfaceErosion.MaximumRate*SurfaceErosion.EditInterval+1e-12);
        for(int i=0;i<300;i++)w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(1,0,0))],2);
        Assert.InRange(start-w.Terrain.Height(p),SurfaceErosion.MaximumCut-1e-10,SurfaceErosion.MaximumCut+1e-10);
        double stopped=w.Terrain.Height(p);w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(2,0,0))],3600);Assert.Equal(stopped,w.Terrain.Height(p));
        TerrainEditing.Sculpt(w,p,.1,.04,SculptMode.Raise);double raised=w.Terrain.Height(p);
        string save=Path.Combine(TestUtil.TempDir(),"erosion.vivsave");Assert.True(SaveSystem.Save(w,save).Ok);
        var loaded=SaveSystem.Load(TestUtil.Content,save);Assert.True(loaded.Ok,loaded.Message);
        Assert.Equal(WorldSerializer.Digest(w),WorldSerializer.Digest(loaded.World!));
        loaded.World!.Liquid.Erosion.Advance([Contact(loaded.World,p,new Vec3(2,0,0))],3600);
        Assert.Equal(raised,loaded.World.Terrain.Height(p));Assert.True(w.Liquid.Erosion.RemovedVolume>0);
    }
    [Fact] public void PendingWearSurvivesSaveAndOnlyWettedVerticesChange()
    {
        var w=World();var p=w.Terrain.VertexPos(w.Terrain.Nx/2,w.Terrain.Nz/2);var particle=Contact(w,p,new Vec3(1,0,0));
        w.Liquid.Erosion.Advance([particle],SurfaceErosion.EditInterval/2);Assert.Equal(.3,w.Terrain.Height(p));
        string save=Path.Combine(TestUtil.TempDir(),"pending-erosion.vivsave");Assert.True(SaveSystem.Save(w,save).Ok);
        var loaded=SaveSystem.Load(TestUtil.Content,save);Assert.True(loaded.Ok,loaded.Message);
        Assert.Equal(WorldSerializer.Digest(w),WorldSerializer.Digest(loaded.World!));
        w.Liquid.Erosion.Advance([particle],SurfaceErosion.EditInterval/2);loaded.World!.Liquid.Erosion.Advance([particle],SurfaceErosion.EditInterval/2);
        Assert.Equal(WorldSerializer.Digest(w),WorldSerializer.Digest(loaded.World));
        Assert.Equal(.3,w.Terrain.Height(new Vec2(1,1)));Assert.True(w.Terrain.Height(p)<.3);
    }
    [Fact] public void WeakFlowArmoursBeforeReachingTheDepthLimit()
    {
        var w=World();var p=w.Terrain.VertexPos(w.Terrain.Nx/2,w.Terrain.Nz/2);
        for(int i=0;i<300;i++)w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(.13,0,0))],2);
        Assert.InRange(.3-w.Terrain.Height(p),.00001,.002);
        double stopped=w.Terrain.Height(p);int version=w.Terrain.Version;
        for(int i=0;i<300;i++)w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(1,0,0))],2);
        Assert.Equal(stopped,w.Terrain.Height(p));Assert.Equal(version,w.Terrain.Version);
    }
    [Fact] public void IntermittentlyWetEdgesAlsoHaveAFiniteScourWindow()
    {
        var w=World();var p=w.Terrain.VertexPos(w.Terrain.Nx/2,w.Terrain.Nz/2);
        w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(.13,0,0))],2);
        w.Liquid.Erosion.Advance([],600);double stopped=w.Terrain.Height(p);int version=w.Terrain.Version;
        w.Liquid.Erosion.Advance([Contact(w,p,new Vec3(1,0,0))],60);
        Assert.Equal(stopped,w.Terrain.Height(p));Assert.Equal(version,w.Terrain.Version);
    }
    [Fact] public void RockAndStandingPondBedsAreProtected()
    {
        var w=World();Array.Fill(w.Fields.BaseSubstrate.Values,(byte)Substrate.Rock);
        w.Liquid.Erosion.Advance([Contact(w,Vec2.Zero,new Vec3(1,0,0))],60);Assert.Null(w.Liquid.Erosion.Capture());
        Array.Fill(w.Fields.BaseSubstrate.Values,(byte)Substrate.Soil);w.Water.Config.WaterTable=.4;
        w.Liquid.Erosion.Advance([Contact(w,Vec2.Zero,new Vec3(1,0,0))],60);Assert.Null(w.Liquid.Erosion.Capture());
    }
    [Theory][InlineData(false)][InlineData(true)] public void ProductionOwnerWearsTheBedAndRetainsWaterMass(bool async)
    {
        var w=World();w.Terrain.BeginEdit();for(int z=0;z<w.Terrain.Nz;z++)for(int x=0;x<w.Terrain.Nx;x++)
            w.Terrain.H[z*w.Terrain.Nx+x]=.3-.3*w.Terrain.VertexPos(x,z).X;
        w.Terrain.Touch();w.Water.Config.WaterTable=-1;w.Water.RefreshBed(w.Terrain);
        w.Water.Springs.Add(new Spring{Id=w.Ids.Next(EntityKind.Spring),X=-.3,Z=0,Discharge=2000.0/1000/3600});
        w.Liquid.Enable();w.Liquid.AsyncMode=async;w.Clock.Paused=false;
        for(int i=0;i<720;i++){w.Liquid.Advance(1.0/60);w.Liquid.CompletePending();}
        Assert.True(w.Liquid.Erosion.WornVertices>0);Assert.InRange(w.Liquid.Erosion.DeepestCut,0,SurfaceErosion.MaximumRate*12+1e-10);
        Assert.InRange(w.Water.Volume()+w.Water.Budget.BoundaryOutflow+w.Water.Budget.GroundwaterRecharge,
            w.Water.Budget.SpringInflow-1e-10,w.Water.Budget.SpringInflow+1e-10);
        var before=w.Terrain.H.ToArray();w.Clock.Paused=true;w.Liquid.Advance(600);Assert.Equal(before,w.Terrain.H);
    }
}
