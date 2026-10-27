using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
using System.Diagnostics;
using System.Text.Json;
namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class SurfaceFluidReplacementTests
{
    private static SurfaceFluidDynamics Fluid(Func<Vec2, double> bed) => new(new HexDomain(3), bed);
    private static Spring Source(double q = .0001) => new() { Id = new(101), X = 0, Z = 0, Discharge = q };
    private static void Balance(SurfaceFluidDynamics f, WaterBudget b, double initial = 0)
    { Assert.InRange(f.Volume + b.GroundwaterRecharge + b.BoundaryOutflow + b.Evaporation + b.Infiltration + b.ToolRemoval - b.ToolInflow - b.SpringInflow - initial, -2e-10, 2e-10); Assert.All(f.Export().Cells, c => Assert.True(c.Volume >= 0 && double.IsFinite(c.Volume))); }
    [Theory][InlineData(false)][InlineData(true)]
    public void VisibleTerrainTrianglesMatchFluidSupportBetweenVertices(bool sculpt)
    {
        var w=TestUtil.FlatWorld(edit:d=>d.Diameter=5);var hf=w.Terrain;
        if(sculpt)
        {
            hf.BeginEdit();
            for(int j=0;j<hf.Nz;j++)for(int i=0;i<hf.Nx;i++)
            {var p=hf.VertexPos(i,j);hf.H[hf.VertexIndex(i,j)]=.4+.08*Math.Sin(p.X*17)*Math.Cos(p.Z*13);}
            hf.Touch();w.Liquid.OnTerrainChanged();
        }
        // 1. Physical support mesh matches authoritative heightfield and fluid support exactly (< 2 µm)
        var physMesh = TerrainMesh.BuildTop(w, renderSmoothing: false).Mesh; double physMax = 0;
        for (int k = 0; k < physMesh.Indices.Count; k += 3)
        {
            var a = physMesh.Position(physMesh.Indices[k]); var b = physMesh.Position(physMesh.Indices[k + 1]); var c = physMesh.Position(physMesh.Indices[k + 2]);
            foreach (var weights in new[] { new Vec3(1.0 / 3, 1.0 / 3, 1.0 / 3), new Vec3(.1, .3, .6), new Vec3(.6, .3, .1) })
            { var p = a * weights.X + b * weights.Y + c * weights.Z; physMax = Math.Max(physMax, Math.Abs(p.Y - w.GroundHeight(p.XZ))); }
        }
        Assert.True(physMax < 2e-6, $"Physical bed differs from fluid support by {physMax * 1000:F3} mm");

        // 2. Visible render mesh evaluates bicubic Catmull-Rom smoothing and micro-relief hummocks within documented tolerance
        var mesh = TerrainMesh.BuildTop(w).Mesh; double maximum = 0;
        for(int k=0;k<mesh.Indices.Count;k+=3)
        {
            var a=mesh.Position(mesh.Indices[k]);var b=mesh.Position(mesh.Indices[k+1]);var c=mesh.Position(mesh.Indices[k+2]);
            foreach(var weights in new[]{new Vec3(1.0/3,1.0/3,1.0/3),new Vec3(.1,.3,.6),new Vec3(.6,.3,.1)})
            {var p=a*weights.X+b*weights.Y+c*weights.Z;maximum=Math.Max(maximum,Math.Abs(p.Y-w.GroundHeight(p.XZ)));}
        }
        double tolerance = sculpt ? 0.055 : 0.008; // documented bicubic Catmull-Rom & hummock tolerance (55 mm sculpt, 8 mm unsculpted)
        Assert.True(maximum < tolerance, $"Visible bed differs from fluid support by {maximum * 1000:F3} mm (tolerance: {tolerance * 1000:F1} mm)");
    }
    [Fact]
    public void DryPartOfNeighboringCellCannotCancelAWettedColumn()
    {
        var w=TestUtil.FlatWorld();w.Terrain.BeginEdit();Array.Fill(w.Terrain.H,.5);w.Terrain.Touch();w.Liquid.OnTerrainChanged();
        const double spacing=.025;
        // The second cell holds a small wedge at its far edge. Its dry plane
        // extends below the first cell's film at the shared boundary.
        w.Liquid.Restore(new([new(0,0,.001*spacing*spacing,0,0),new(1,0,1e-7,0,0,1,0)],[],0,0,spacing,new()));
        double volume=w.Liquid.Volume;
        foreach(double x in new[]{.0125,.02,.0249,.0251})
        {
            double surface=w.Liquid.SurfaceAt(new(x,.0125));
            Assert.True(double.IsFinite(surface)&&surface>.5001,$"Wet film disappeared at x={x}: {surface}");
            Assert.InRange(surface,.5,.501001);
        }
        Assert.Equal(volume,w.Liquid.Volume);
    }
    [Theory][InlineData(.2)][InlineData(1.5)]
    public void ThinFilmFollowsTerrainBendWithoutRaisedOrBuriedInterpolatedWedges(double bedSlope)
    {
        var w=TestUtil.FlatWorld();var hf=w.Terrain;hf.BeginEdit();
        for(int j=0;j<hf.Nz;j++)for(int i=0;i<hf.Nx;i++)hf.H[hf.VertexIndex(i,j)]=.5+bedSlope*Math.Min(.5,Math.Abs(hf.VertexPos(i,j).X));
        hf.Touch();w.Liquid.OnTerrainChanged();const double spacing=.025,depth=.001;
        var film=new List<SurfaceFluidCell>();
        for(int z=-2;z<=2;z++)for(int x=-3;x<=2;x++)film.Add(new(x,z,depth*spacing*spacing,0,0,x<0?-bedSlope:bedSlope,0));
        w.Liquid.Restore(new(film.ToArray(),[],0,0,spacing,new()));
        for(int i=0;i<=50;i++)
        {
            var p=new Vec2(-.0125+i*.0005,.0125);double actual=w.Liquid.SurfaceAt(p)-w.GroundHeight(p);
            Assert.True(double.IsFinite(actual)&&Math.Abs(actual-depth)<1e-8,$"Film depth at {p.X}: {actual}");
        }
    }
    [Fact]
    public void MovingSurfaceDepthKernelIntegratesToConservedCellVolume()
    {
        var w=TestUtil.FlatWorld();w.Terrain.BeginEdit();Array.Fill(w.Terrain.H,.5);w.Terrain.Touch();w.Liquid.OnTerrainChanged();
        const double spacing=.025;
        w.Liquid.Restore(new([new(0,0,8e-7,0,0,.01,0),new(1,0,2e-7,0,0,.01,0)],[],0,0,spacing,new()));
        double integral=0,step=spacing/100;
        for(int z=-100;z<200;z++)for(int x=-100;x<300;x++)
            integral+=w.Liquid.DepthAt(new((x+.5)*step,(z+.5)*step))*step*step;
        Assert.InRange(Math.Abs(integral-w.Liquid.Volume),0,2e-10);
    }
    [Fact]
    public void RestingSupportedPoolKeepsOneLevelOverSlopingBed()
    {
        var w=TestUtil.FlatWorld();var hf=w.Terrain;hf.BeginEdit();
        for(int j=0;j<hf.Nz;j++)for(int i=0;i<hf.Nx;i++)hf.H[hf.VertexIndex(i,j)]=.5+.05*hf.VertexPos(i,j).X;
        hf.Touch();w.Liquid.OnTerrainChanged();const double spacing=.025,head=.52;
        var cells=Enumerable.Range(-2,4).Select(x=>new SurfaceFluidCell(x,0,(head-(.5+.05*(x+.5)*spacing))*spacing*spacing,0,0)).ToArray();
        w.Liquid.Restore(new(cells,[],0,0,spacing,new()));
        for(int i=0;i<=50;i++)Assert.Equal(head,w.Liquid.SurfaceAt(new(-.0375+i*.0015,.0125)),10);
        // A moving cell exactly outside this point's kernel has zero influence,
        // including on the resting-pool decision (matching the GPU).
        w.Liquid.Restore(new(cells.Append(new(2,0,1e-7,1e-6,0,.1,0,1e-5,0)).ToArray(),[],0,0,spacing,new()));
        Assert.Equal(head,w.Liquid.SurfaceAt(new(.025,.0125)),10);
    }
    [Fact]
    public void IsolatedMovingVolumeCannotBeMistakenForARestingPoolPlate()
    {
        var w=TestUtil.FlatWorld();var hf=w.Terrain;hf.BeginEdit();
        for(int j=0;j<hf.Nz;j++)for(int i=0;i<hf.Nx;i++)hf.H[hf.VertexIndex(i,j)]=.5-.5*hf.VertexPos(i,j).X;
        hf.Touch();w.Liquid.OnTerrainChanged();const double spacing=.025,volume=1e-7;
        w.Liquid.Restore(new([new(0,0,volume,1e-6,0,0,0,1e-5,0)],[],0,0,spacing,new()));
        var p=new Vec2(.0125,.0125);double depth=w.Liquid.SurfaceAt(p)-w.GroundHeight(p);
        Assert.Equal(volume/(spacing*spacing)*.75*.75,depth,10);
    }
    [Fact]
    public void StationarySubcellWedgeCannotExtendItsPoolLevelIntoEmptyDownhillCells()
    {
        var w=TestUtil.FlatWorld();var hf=w.Terrain;hf.BeginEdit();
        for(int j=0;j<hf.Nz;j++)for(int i=0;i<hf.Nx;i++)hf.H[hf.VertexIndex(i,j)]=.5-.5*hf.VertexPos(i,j).X;
        hf.Touch();w.Liquid.OnTerrainChanged();const double spacing=.025,volume=1e-8;
        w.Liquid.Restore(new([new(0,0,volume,0,0)],[],0,0,spacing,new()));
        for(int i=0;i<=40;i++)
            Assert.InRange(w.Liquid.DepthAt(new(.025+i*.001,.0125)),0,volume/(spacing*spacing));
        Assert.Equal(volume,w.Liquid.Volume);
    }
    [Fact]
    public void WetDryFrontVelocityUsesSolvedFaceSectionAndSurvivesReload()
    {
        double Bed(Vec2 p)=>.12-.3*p.X+.8*p.Z*p.Z+.007*Math.Sin(p.X*20)*Math.Cos(p.Z*15);
        var fluid=Fluid(Bed);var budget=new WaterBudget();fluid.Advance(5,[Source(.00002)],budget);
        var before=fluid.CaptureRenderState();
        Assert.All(before.Cells,c=>Assert.InRange(c.Velocity.Length,0,4));
        Assert.True(before.Cells.Sum(c=>c.Volume*c.Velocity.Length)/before.Cells.Sum(c=>c.Volume)>.01);
        var reloaded=Fluid(Bed);reloaded.Restore(fluid.Export());
        Assert.Equal(before.Cells,reloaded.CaptureRenderState().Cells);
        Balance(fluid,budget);
    }
    [Theory][InlineData(1.5,0)][InlineData(-1.5,0)][InlineData(0,1.5)][InlineData(0,-1.5)]
    public void ContinuousSteepGroundDoesNotLaunchWaterIntoAir(double slopeX,double slopeZ)
    {
        var fluid=Fluid(p=>.2-slopeX*p.X-slopeZ*p.Z);var budget=new WaterBudget();
        for(int i=0;i<60;i++)
        {fluid.Advance(SurfaceFluidDynamics.StepSeconds,[Source(.00002)],budget);Assert.Empty(fluid.Airborne);}
        Balance(fluid,budget);
        Assert.Contains(fluid.CaptureRenderState().Cells,c=>c.Position.X*slopeX+c.Position.Z*slopeZ>.1&&c.Volume>1e-10);
    }
    [Fact]
    public void AbruptUnsupportedLipStillEmitsConservedFallingWater()
    {
        var fluid=Fluid(p=>p.X<.10?.2:0);var budget=new WaterBudget();bool falling=false;
        for(int i=0;i<120;i++)
        {fluid.Advance(SurfaceFluidDynamics.StepSeconds,[Source(.0001)],budget);falling|=fluid.Airborne.Any(p=>p.Stream!=0&&p.Position.X>=.1&&p.Position.Y>.1);}
        Assert.True(falling,"Actual unsupported lip must retain free flight");Balance(fluid,budget);
    }

    [Theory][InlineData(1e-12)][InlineData(1e-9)][InlineData(1e-6)]
    public void UnsupportedThinSheetRetractsWithoutChangingItsVolume(double volume)
    {
        const double length=.025,aperture=.025;
        var initial=FluidCrossSection.Reconstruct(volume,length,aperture,0);
        var fallen=FluidCrossSection.Reconstruct(volume,length,aperture,.25);
        double roundRadius=Math.Sqrt(volume/(Math.PI*length));
        Assert.Equal(Math.Max(roundRadius,aperture*.5),initial.WidthRadius,12);
        Assert.Equal(roundRadius,fallen.WidthRadius,12);Assert.Equal(roundRadius,fallen.ThicknessRadius,12);
        foreach(double age in new[]{0,.0001,.001,.01,.1,.25})
        {
            var section=FluidCrossSection.Reconstruct(volume,length,aperture,age);
            double areaFactor=section.ShapePower==1?Math.PI:3.7081493546;
            Assert.InRange(Math.Abs(areaFactor*section.WidthRadius*section.ThicknessRadius*length-volume),0,volume*1e-12);
        }
    }

    [Fact]
    public void ConnectedSurfaceKernelSmoothsParcelBulgesAndConservesIntegratedVolume()
    {
        double[] volumes=[1e-9,1e-6,1e-9,5e-8],lengths=[.01,.02,.03,.01];
        var smooth=FluidCrossSection.ReconstructTubeVolumes(volumes,lengths);
        Assert.InRange(Math.Abs(volumes.Sum()-smooth.Sum()),0,volumes.Sum()*1e-12);Assert.All(smooth,v=>Assert.True(v>0));
        Assert.True(smooth.Select((v,i)=>v/lengths[i]).Max()<volumes.Select((v,i)=>v/lengths[i]).Max());
        double[] steady=lengths.Select(l=>l*.0002).ToArray();
        var preserved=FluidCrossSection.ReconstructTubeVolumes(steady,lengths);
        for(int i=0;i<steady.Length;i++)Assert.InRange(Math.Abs(steady[i]-preserved[i]),0,steady[i]*1e-12);
    }

    [Fact]
    public void FractionalDischargeAndRestingWaterNeverExpire()
    {
        var f = Fluid(_ => 0); var b = new WaterBudget(); f.Advance(2, [Source(1e-9)], b); Balance(f, b); Assert.InRange(f.Volume, 1.99999e-9, 2.00001e-9);
        f.Advance(10, [], b); Balance(f, b); Assert.True(f.Volume > 0);
    }
    [Fact]
    public void CapacityIntegralHandlesFlatAndClippedTriangles()
    {
        Assert.Equal(.1, SurfaceFluidDynamics.TriangleDepth(.1, 0, 0, 0), 12);
        Assert.Equal(1.0 / 24, SurfaceFluidDynamics.TriangleDepth(.5, 0, 1, 1), 12);
        Assert.Equal(.5, SurfaceFluidDynamics.TriangleDepth(1, 0, .5, 1), 12);
    }
    [Fact]
    public void ClosedDepressionFillsThenOverflowsLowestLip()
    {
        double Bed(Vec2 p) => Math.Abs(p.Z) > .15 || p.X < -.15 ? .08 : p.X > .15 ? (p.X < .20 ? .035 : -.08) : 0;
        var f = Fluid(Bed); var b = new WaterBudget(); var source = Source(.0001); f.Advance(8, [source], b);
        double early = f.CaptureRenderState().Cells.Where(c => c.Position.X > .23).Sum(c => c.Volume);
        Assert.True(early < 1e-8, $"premature overflow {early}");
        f.Advance(38, [source], b); Balance(f, b);
        Assert.True(f.CaptureRenderState().Cells.Any(c => c.Position.X > .23 && c.Volume > 1e-9) || f.Airborne.Count > 0 || b.BoundaryOutflow > 0, "Lowest lip must overflow");
    }
    [Fact]
    public void LakeAtRestRemainsLevelAndMotionless()
    {
        double Bed(Vec2 p) => .06 * (p.X * p.X + p.Z * p.Z);
        var f = Fluid(Bed); var cells = new List<SurfaceFluidCell>(); double spacing = .025, head = .025;
        // Exact cell capacities from fine quadrature; validate long-term well-balancing away from shore.
        for (int z = -25; z <= 25; z++) for (int x = -25; x <= 25; x++)
            { double volume = 0; for (int j = 0; j < 8; j++) for (int i = 0; i < 8; i++) volume += Math.Max(0, head - Bed(new((x + (i + .5) / 8) * spacing, (z + (j + .5) / 8) * spacing))) * spacing * spacing / 64; if (volume > 0) cells.Add(new(x, z, volume, 0, 0)); }
        f.Restore(new(cells.ToArray(), [], 0, 0, spacing, new())); double initial = f.Volume; var b = new WaterBudget(); f.Advance(1, [], b); Balance(f, b, initial);
        var interior = f.CaptureRenderState().Cells.Where(c => c.Depth > .008).ToArray(); double spread = interior.Max(c => c.Head) - interior.Min(c => c.Head);
        Assert.True(spread < .001, $"resting spread {spread}"); Assert.True(interior.Max(c => c.Velocity.Length) < .03);
    }
    [Fact]
    public void ObstaclesRedirectFlowAndRemovalReleasesConservedWater()
    {
        bool rock = true; double Bed(Vec2 p) => .12 - .12 * p.X + (rock && Math.Abs(p.X - .16) < .07 && Math.Abs(p.Z) < .06 ? .15 : 0);
        var f = Fluid(Bed); var b = new WaterBudget(); f.Advance(4, [Source()], b); Balance(f, b);
        var before = f.CaptureRenderState(); Assert.DoesNotContain(before.Cells, c => Math.Abs(c.Position.X - .16) < .04 && Math.Abs(c.Position.Z) < .03 && c.Depth > .001);
        double volume = f.Volume; rock = false; f.OnGeometryChanged(); Assert.Equal(volume, f.Volume, 12); f.Advance(3, [Source()], b); Balance(f, b);
        Assert.Contains(f.CaptureRenderState().Cells, c => c.Position.X > .3);
    }
    [Fact]
    public void FallingLiquidMovesThroughAirAndLandsWithVolume()
    {
        var f = Fluid(_ => 0); var b = new WaterBudget(); f.Add(new(0, .5, 0), new(.2, 0, 0), .0002);
        f.Advance(.1, [], b); Assert.NotEmpty(f.Airborne); Assert.All(f.Airborne, p => Assert.True(p.Position.Y < .5 && p.Position.Y > .1));
        f.Advance(.5, [], b); Assert.Empty(f.Airborne); Assert.True(f.ActiveCells > 0); Balance(f, b, .0002);
    }
    [Fact]
    public void PondTransferOccursExactlyOnceAndNeverFromTheSource()
    {
        var f = Fluid(p => p.X < .25 ? .1 - .1 * p.X : -.1); f.WaterTable = 0; var b = new WaterBudget();
        f.Add(new(0, .11, 0), Vec3.Zero, .0003); Assert.Equal(0, b.GroundwaterRecharge); f.Advance(4, [], b); Balance(f, b, .0003);
        Assert.True(b.GroundwaterRecharge > 0); double transfer = b.GroundwaterRecharge; f.Advance(1, [], b); Balance(f, b, .0003); Assert.True(b.GroundwaterRecharge >= transfer);
    }
    [Fact]
    public void GeometryChangesRetainVolumeAndNewChannelDrains()
    {
        bool closed = true; double Bed(Vec2 p) => Math.Abs(p.X) > .12 || Math.Abs(p.Z) > .12 ? (closed ? .12 : (p.X > 0 ? -.05 : .12)) : 0;
        var f = Fluid(Bed); var b = new WaterBudget(); f.AddWater(Vec2.Zero, .07, .001); f.Advance(1, [], b); Balance(f, b, .001);
        closed = false; f.OnGeometryChanged(); Assert.Equal(.001, f.Volume, 10); f.Advance(3, [], b); Balance(f, b, .001);
        Assert.True(f.CaptureRenderState().Cells.Any(c => c.Position.X > .2) || f.Airborne.Count > 0);
    }
    [Fact]
    public void SaveRoundTripPreservesActiveFlowAndPendingTime()
    {
        var w = TestUtil.FlatWorld(); w.Liquid.Add(Vec2.Zero, .1, .002); var source = Source(); source.Id = w.Ids.Next(EntityKind.Spring); w.Water.Springs.Add(source);
        w.Liquid.Advance(.03); w.Liquid.Advance(.6); string path = Path.Combine(TestUtil.TempDir(), "flow.vivsave");
        var saved = SaveSystem.Save(w, path); Assert.True(saved.Ok, saved.Message); var loaded = SaveSystem.Load(w.Content, path); Assert.True(loaded.Ok, loaded.Message);
        Assert.Equal(WorldSerializer.Digest(w), WorldSerializer.Digest(loaded.World!)); Assert.Equal(w.Liquid.CaptureState().Remainder, loaded.World!.Liquid.CaptureState().Remainder);
        var a = w.Liquid.Dynamics; var c = loaded.World.Liquid.Dynamics;
        a.Advance(.1, w.Water.Springs, new()); c.Advance(.1, loaded.World.Water.Springs, new());
        Assert.Equal(JsonSerializer.Serialize(a.Export()), JsonSerializer.Serialize(c.Export()));
    }
    [Fact]
    public void PausingAndHeadlessHostUseOnePhysicalClock()
    {
        var w = TestUtil.FlatWorld(); w.Water.Springs.Add(Source()); var host = new Vivarium.Sim.Time.SimHost(w);
        w.Clock.Paused = true; host.Advance(.05); Assert.Equal(0, w.Liquid.Volume);
        w.Clock.Paused = false; host.Advance(.05); Assert.InRange(w.Liquid.PresentationTime, .04999, .05001);
        Assert.InRange(w.Liquid.Volume, Source().Discharge * .05 - 1e-10, Source().Discharge * .05 + 1e-10);
    }
    [Fact]
    public void StandingPondGeometryAndShaderRemainUnchangedByMobileFlow()
    {
        var w = TestUtil.FlatWorld(42, d => d.Water.WaterTable = .6); var before = WaterMesh.BuildWaterTable(w, w.Terrain).DigestHex();
        double table = w.Water.WaterTable, depth = w.Water.WaterTableDepth(Vec2.Zero); w.Liquid.Add(Vec2.Zero, .1, .001); w.Liquid.Advance(.1);
        Assert.Equal(before, WaterMesh.BuildWaterTable(w, w.Terrain).DigestHex()); Assert.Equal(table, w.Water.WaterTable); Assert.Equal(depth, w.Water.WaterTableDepth(Vec2.Zero));
    }
    [Fact] public void PresetsWarmUpTheProductionOwnerAndRetainTheirConfiguredTable()
    {
        foreach(string id in TestUtil.Content.Presets.Keys)
        {
            var preset=TestUtil.Content.PresetOrThrow(id);double table=preset.Water.WaterTable;
            var world=VivariumWorld.Create(TestUtil.Content,preset,populate:false);
            Assert.True(world.Liquid.Enabled);Assert.Equal(table,world.Water.WaterTable);
            double expected=world.Water.Springs.Sum(s=>s.Discharge)*5;
            Assert.Equal(expected,world.Water.Budget.SpringInflow,10);
            var b=world.Water.Budget;Assert.Equal(expected,world.Liquid.Volume+b.GroundwaterRecharge+b.BoundaryOutflow+b.Evaporation+b.Infiltration,9);
            Assert.True(world.Water.AllFinite());
        }
    }

    [Fact] public void EqualPhysicalTimeAcrossRenderCadences()
    {
        SurfaceFluidState Run(double[] frames)
        {
            var w=TestUtil.FlatWorld();w.Clock.BaseRate=0;w.Water.Springs.Add(Source());var host=new Vivarium.Sim.Time.SimHost(w);
            double elapsed=0;int frame=0;
            while(elapsed<2-1e-12){double dt=Math.Min(frames[frame++%frames.Length],2-elapsed);host.Advance(dt);elapsed+=dt;}
            for(int drain=0;drain<20;drain++)w.Liquid.Advance(0);
            Assert.InRange(w.Liquid.PresentationTime,2-1e-9,2+1e-9);Assert.Equal(Source().Discharge*2,w.Water.Budget.SpringInflow,11);
            return w.Liquid.CaptureState();
        }
        var reference=Run([1/60.0]);var irregular=Run([1/144.0,1/20.0,.25]);
        var a=reference.Cells.ToDictionary(c=>(c.X,c.Z),c=>c.Volume);var b=irregular.Cells.ToDictionary(c=>(c.X,c.Z),c=>c.Volume);
        double difference=a.Keys.Union(b.Keys).Sum(k=>Math.Abs(a.GetValueOrDefault(k)-b.GetValueOrDefault(k)));
        Assert.InRange(difference,0,1e-9);
    }

    [Fact] public void ReservoirContactNeverPresentsASecondSupportedSurface()
    {
        var w=TestUtil.FlatWorld(edit:d=>d.Water.WaterTable=.6);
        w.Liquid.Add(Vec2.Zero,.01,.0001);
        Assert.True(double.IsNaN(w.Liquid.SurfaceAt(Vec2.Zero)));Assert.Equal(.6,w.Water.SurfaceAt(Vec2.Zero),12);
        w.Liquid.AdvancePhysical(SurfaceFluidDynamics.StepSeconds);
        Assert.Equal(0,w.Liquid.Volume,12);Assert.Equal(.0001,w.Water.Budget.GroundwaterRecharge,12);
    }

}
