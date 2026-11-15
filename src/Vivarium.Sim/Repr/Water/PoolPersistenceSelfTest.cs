using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
using Vivarium.Sim.Persistence;

namespace Vivarium.Sim.Repr.Water;

public sealed class PoolLegacyPersistenceTest : IReprSelfTest
{
    public string Name => "water.poolLegacyPersistence";
    public string? Run()
    {
        const double spacing=.0125, volume=spacing*spacing*.1;
        var legacy=new SurfaceFluidState(new[] { new SurfaceFluidCell(0,0,volume,0,0,
            QuietSeconds:2,CachedHead:1,HeadVolume:volume,Established:true,Sleeping:true) },
            Array.Empty<FluidParticle>(),0,0,spacing,new Dictionary<ulong,double>(),WorkStamp:13,NextRegionStamp:16);
        var d=new SurfaceFluidDynamics(new HexDomain(5),_=>0);
        d.Restore(legacy);
        string original=JsonSerializer.Serialize(legacy), saved=JsonSerializer.Serialize(d.Export());
        if(original!=saved)return "legacy export changed before first mutation";
        double initial=d.Volume;
        d.Advance(SurfaceFluidDynamics.StepSeconds,Array.Empty<Spring>(),new WaterBudget());
        var upgraded=d.Export();
        if(upgraded.PoolModelVersion!=1)return "first advance did not upgrade pool model";
        if(Math.Abs(d.Volume-initial)>1e-12)return "legacy migration changed water volume";
        if(d.CaptureRenderState().Cells.Any(c=>c.Head>.5))return "legacy pinned head survived migration";
        return null;
    }
}

public sealed class PoolRoundTripPersistenceTest : IReprSelfTest
{
    public string Name => "water.poolRoundTripPersistence";
    public string? Run()
    {
        const double spacing=.0125, head=.1, volume=spacing*spacing*head;
        var members=new List<int[]>();var cells=new List<SurfaceFluidCell>();
        for(int z=0;z<4;z++)for(int x=0;x<4;x++)
        {
            members.Add(new[]{x,z});
            cells.Add(new SurfaceFluidCell(x,z,volume,0,0,CachedHead:head,HeadVolume:volume,Established:true,Sleeping:true));
        }
        // A shallower wet neighbor on the east side drives real asymmetric outflow.
        double neighbor=spacing*spacing*.04;
        cells.Add(new SurfaceFluidCell(4,1,neighbor,0,0,CachedHead:.04,HeadVolume:neighbor));
        var state=new SurfaceFluidState(cells.ToArray(),Array.Empty<FluidParticle>(),0,0,spacing,
            new Dictionary<ulong,double>(),PoolModelVersion:1,
            Pools:new[]{new SurfacePoolState(members.ToArray(),volume*16,head,Vec2.Zero)});
        var a=new SurfaceFluidDynamics(new HexDomain(5),_=>0);
        a.Restore(state);
        string json=JsonSerializer.Serialize(a.Export());
        var b=new SurfaceFluidDynamics(new HexDomain(5),_=>0);
        b.Restore(JsonSerializer.Deserialize<SurfaceFluidState>(json)!);
        if(json!=JsonSerializer.Serialize(b.Export()))return "model 1 JSON changed on restore";
        var budgetA=new WaterBudget();var budgetB=new WaterBudget();
        a.Advance(SurfaceFluidDynamics.StepSeconds,Array.Empty<Spring>(),budgetA);
        b.Advance(SurfaceFluidDynamics.StepSeconds,Array.Empty<Spring>(),budgetB);
        var first=a.Export();
        if(!first.Cells.Any(c=>c.X==3 && c.Z==1 && c.EastFlux>0))return "first-step eastward face flux was not positive";
        a.Advance(.25-SurfaceFluidDynamics.StepSeconds,Array.Empty<Spring>(),budgetA);
        b.Advance(.25-SurfaceFluidDynamics.StepSeconds,Array.Empty<Spring>(),budgetB);
        if(JsonSerializer.Serialize(a.Export())!=JsonSerializer.Serialize(b.Export()))return "model 1 continuation diverged";
        if(Math.Abs(a.Volume-(volume*16+neighbor))>1e-12)return "continuation changed conserved volume";
        var invalidCells=new[]{0,1,3,4}.Select(x=>new SurfaceFluidCell(x,0,volume,0,0,
            CachedHead:head,HeadVolume:volume,Established:true,Sleeping:true)).ToArray();
        var invalid=state with {Cells=invalidCells,Pools=new[]{new SurfacePoolState(new[]{new[]{0,0},new[]{1,0},new[]{3,0},new[]{4,0}},4*volume,head,Vec2.Zero)}};
        try { new SurfaceFluidDynamics(new HexDomain(5),_=>0).Restore(invalid); return "disconnected saved pool was accepted"; }
        catch(InvalidDataException) { return null; }
    }
}

public sealed class PoolWorldPersistenceTest : IReprSelfTest
{
    public string Name => "water.poolWorldPersistence";
    public string? Run()
    {
        const double spacing=.0125, head=.4, volume=spacing*spacing*.1;
        var descriptor=new WorldDescriptor { Diameter=5,CellSize=.1,PilotTreeId="none",GuardianId="none",
            Terrain=new TerrainProfile { BaseHeight=.3,Relief=0,MicroRelief=0 },
            Water=new WaterConfig { WaterTable=0,Infiltration=0,Evaporation=0 } };
        var members=new List<int[]>();var cells=new List<SurfaceFluidCell>();
        for(int z=0;z<4;z++)for(int x=0;x<4;x++)
        {
            members.Add(new[]{x,z});
            cells.Add(new SurfaceFluidCell(x,z,volume,0,0,CachedHead:head,HeadVolume:volume,Established:true,Sleeping:true));
        }
        var modern=new SurfaceFluidState(cells.ToArray(),Array.Empty<FluidParticle>(),0,0,spacing,new Dictionary<ulong,double>(),
            PoolModelVersion:1,Pools:new[]{new SurfacePoolState(members.ToArray(),volume*16,head,Vec2.Zero)});
        var world=VivariumWorld.CreateBaseline(EcoTestKit.Content,descriptor);
        world.Liquid.Restore(modern);
        if(world.Liquid.CaptureRenderState().EstablishedCells!=16 || world.Liquid.CaptureState().Pools?.Length!=1)
            return "SurfaceLiquid.Restore lost the saved pool";
        string digest=WorldSerializer.Digest(world);
        var loaded=WorldSerializer.Deserialize(EcoTestKit.Content,WorldSerializer.Serialize(world));
        if(WorldSerializer.Digest(loaded)!=digest || loaded.Liquid.CaptureRenderState().EstablishedCells!=16 || loaded.Liquid.CaptureState().Pools?.Length!=1)
            return "model 1 world roundtrip changed digest or pool membership";

        var legacy=new SurfaceFluidState(new[]{new SurfaceFluidCell(0,0,volume,0,0,QuietSeconds:2,
            CachedHead:1,HeadVolume:volume,Established:true,Sleeping:true)},Array.Empty<FluidParticle>(),0,0,spacing,new Dictionary<ulong,double>());
        var oldWorld=VivariumWorld.CreateBaseline(EcoTestKit.Content,descriptor);
        oldWorld.Liquid.Restore(legacy);
        if(oldWorld.Liquid.CaptureState().PoolModelVersion!=0)return "legacy SurfaceLiquid.Restore upgraded before mutation";
        string oldDigest=WorldSerializer.Digest(oldWorld);
        var oldLoaded=WorldSerializer.Deserialize(EcoTestKit.Content,WorldSerializer.Serialize(oldWorld));
        if(oldLoaded.Liquid.CaptureState().PoolModelVersion!=0 || WorldSerializer.Digest(oldLoaded)!=oldDigest)
            return "legacy world roundtrip changed version or digest before mutation";
        oldLoaded.Liquid.OnSourcesChanged();
        if(oldLoaded.Liquid.CaptureState().PoolModelVersion!=1)return "legacy world did not upgrade after mutation";
        return null;
    }
}
