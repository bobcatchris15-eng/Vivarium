using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

public sealed class EmberfanSupportSelfTest : IReprSelfTest
{
    public string Name => "plant.emberfanSupport";
    public string? Run()
    {
        var content = EcoTestKit.Content;
        var descriptor = new WorldDescriptor { Diameter=9, CellSize=.1, Seed=804584441,
            PilotTreeId="none", GuardianId="none",
            Terrain=new TerrainProfile { BaseHeight=.3, Relief=0, MicroRelief=0 },
            Water=new WaterConfig { WaterTable=0, Infiltration=0, Evaporation=0 } };
        var world = VivariumWorld.CreateBaseline(content, descriptor);
        var p = new Vec2(0,.25);
        if (world.FloraSystem.TryEmberfanSupport(p,42,out _,out _)) return "empty space accepted as bark";
        var log = new LogProp { Id=world.Ids.Next(EntityKind.Log),X=0,Z=0,Y=.5,Length=1,Radius=.2 };
        world.Props.Logs.Add(log);
        if (!world.FloraSystem.TryEmberfanSupport(p,42,out var surface,out var normal)) return "log side was not accepted";
        if (!double.IsFinite(surface.Y) || Math.Abs(normal.Length-1)>1e-9) return "log attachment is not finite/unit";
        var logMesh=PropMeshes.Log(log);double nearest=double.PositiveInfinity;
        for(int i=0;i<logMesh.Indices.Count;i+=3) for(int edge=0;edge<3;edge++)
        {
            var a=logMesh.Position(logMesh.Indices[i+edge])+new Vec3(log.X,log.Y,log.Z);
            var b=logMesh.Position(logMesh.Indices[i+(edge+1)%3])+new Vec3(log.X,log.Y,log.Z);
            nearest=Math.Min(nearest,PlantMath.SegmentDistance(surface,a,b));
        }
        if (nearest>.003) return "attachment floats off actual production log bark";
        world.FloraSystem.TryEmberfanSupport(p,42,out var repeated,out var repeatNormal);
        if (surface != repeated || normal != repeatNormal) return "support frame changed without a host edit";
        log.X=2;
        if (world.FloraSystem.TryEmberfanSupport(p,42,out _,out _)) return "moved-away log left a floating attachment";
        world.Props.Logs.Clear();
        var treeSpecies=content.FloraOrThrow("moonbark");
        var tree=new FloraIndividual { Id=world.Ids.Next(EntityKind.Flora),SpeciesId=treeSpecies.Id,
            X=0,Z=0,Biomass=treeSpecies.MaxBiomass,Age=treeSpecies.MaturityAge,Health=1 };
        world.Flora.Add(tree);
        var profile=world.FloraSystem.TreeTrunkProfile(tree,treeSpecies,.5);
        p=new Vec2(profile.Radius+.02,0);
        if (!world.FloraSystem.TryEmberfanSupport(p,42,out surface,out normal)) return "woody trunk was not accepted";
        if (!(surface.Y>profile.Ground && surface.Y<profile.Top) || Math.Abs(normal.Y)>1e-9)
            return "trunk contact is outside bark or has a nonhorizontal outward normal";
        world.Flora.Remove(tree.Id);
        if (world.FloraSystem.TryEmberfanSupport(p,42,out _,out _)) return "removed trunk left a floating attachment";
        return null;
    }
}
