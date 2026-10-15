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
