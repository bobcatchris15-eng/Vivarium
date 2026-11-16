using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Native proof of graph plants consuming real world population changes.</summary>
public static class GraphPlantReviewRunner
{
    public static async Task Capture(Main host, ContentLibrary content, string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps=30;
        var descriptor=new WorldDescriptor { Diameter=9,CellSize=.1,Seed=804584441,PilotTreeId="none",GuardianId="none",
            Terrain=new TerrainProfile { BaseHeight=.3,Relief=0,MicroRelief=0 },
            Water=new WaterConfig { WaterTable=0,Infiltration=0,Evaporation=0 } };
        SoilSurfaceUpgrade.ConfigureNew(descriptor);
        var world=VivariumWorld.CreateBaseline(content,descriptor);
        FloraIndividual Plant(string species,double x,double z,double maturity)
        {
            var sp=content.FloraOrThrow(species);
            var f=new FloraIndividual { Id=world.Ids.Next(EntityKind.Flora),SpeciesId=species,X=x,Z=z,
                Age=sp.MaturityAge*maturity,Biomass=sp.MaxBiomass*maturity,Health=1 };
            world.Flora.Add(f); return f;
        }
        var plants=new List<FloraIndividual>();
        string[] species={"glowbriar","moonfern","shadebell"};
        for(int s=0;s<3;s++)for(int age=0;age<3;age++)plants.Add(Plant(species[s],(s-1)*2.4,(age-1)*2,.3+.35*age));
        bool catalogProof=Main.UserArgs.Contains("--graph-catalog-proof");
        if(catalogProof)
        {
            int slot=0;
            foreach(var sp in content.Flora.Where(s=>!s.IsCoverageSpecies && !species.Contains(s.Id)))
            {
                var f=Plant(sp.Id,(slot%10-4.5)*.55,(slot/10-5)*.5,.8);slot++;
                plants.Add(f);
            }
            foreach(var f in plants.Where(f=>content.FloraOrThrow(f.SpeciesId).Climber!=null))
                world.FloraSystem.AdvanceClimberSpecimen(f,content.FloraOrThrow(f.SpeciesId),12);
            int patch=0;
            foreach(var sp in content.Flora.Where(s=>s.IsCoverageSpecies))
            {
                world.CoverageSystem.SeedMatureSpecimenClump(sp.Id,new Vec2((patch%5-2)*.6,(patch/5-1.5)*.6),.17);
                patch++;
            }
        }
        int population=plants.Count;
        world.Clock.Paused=true;
        var session=new GameSession {Name="GraphPlantReview"};host.AddChild(session);
        session.Initialize(content,new UserSettings {Transient=true,AutosaveEnabled=false,ShowHelpOnStart=false,LightingMode="cached",MaxFps=30});
        await session.StartWorldAsync(world,null,stage=>GD.Print("GRAPH_REVIEW_STAGE "+stage));
        session.SetProcess(false);session.Tools.SetProcess(false);session.Ui.Visible=session.Overlay.Visible=false;
        session.CameraRig.SetProcess(false);session.CameraRig.SetProcessUnhandledInput(false);
        session.AmbientGround.Visible=false;session.AmbientGround.SetProcess(false);
        session.SoilDetail.Visible=false;session.SoilDetail.SetProcess(false);
        session.EnvRig.BenchmarkLightSeconds=60;session.EnvRig.CameraAttributes.AutoExposureEnabled=false;
        var flora=session.Flora;
        if(!flora.GraphPlantsEnabled)throw new InvalidOperationException("Run graph review with --graph-plants");
        var prototypes=content.Flora.Where(s=>s.IsCoverageSpecies).Select(s=> {
            var counts=session.Coverage.CoverageGraphPrototypeCounts(s.Id);
            if(counts.Vertices==0 || counts.Triangles==0 || counts.Triangles>2000)
                throw new InvalidOperationException("Invalid coverage graph prototype: "+s.Id);
            return new {id=s.Id,vertices=counts.Vertices,triangles=counts.Triangles};
        }).ToArray();
        async Task Frames(int count) { for(int i=0;i<count;i++)await host.ToSignal(host.GetTree(),SceneTree.SignalName.ProcessFrame); }
        async Task Settle(int count)
        {
            await Frames(5);var timer=Stopwatch.StartNew();
            while(!flora.GraphStartupReadyForDiagnostics||flora.GraphPlantCount!=count||flora.GraphPlants is not {PendingBuilds:0,ChunkRebuilds:>0})
            {
                if(timer.Elapsed.TotalSeconds>90)throw new InvalidOperationException("Graph startup: "+flora.GraphPlantsDiagnostics);
                await Frames(1);
            }
            await Frames(5);
        }
        async Task Shot(string name,Vector3 eye,Vector3 target)
        {
            session.CameraRig.LookAtPoint(eye,target);await Frames(50);
            await host.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=host.GetViewport().GetTexture().GetImage();image.SavePng(Path.Combine(directory,name+".png"));
        }
        var eye=new Vector3(6,4.2f,7);var target=new Vector3(0,.9f,0);
        await Shot("initial",eye,target);await Settle(population);
        if(plants.Any(p=>!flora.IsPlantPublished(p.Id)))throw new InvalidOperationException("A live graph plant was not published");
        var graph=flora.GraphPlants!;
        await Shot("initial_ready",eye,target);
        var generations=graph.InspectChunkGenerations();
        var growing=plants[0];var before=graph.InspectPlant(growing.Id.Value)!;
        var def=content.FloraOrThrow(growing.SpeciesId);
        growing.Age=def.MaturityAge*.85;growing.Biomass=def.MaxBiomass*.8;growing.Health=.65;
        flora.InvalidateGraphInputs();await Settle(population);
        var grown=graph.InspectPlant(growing.Id.Value)!;
        if(grown.Organs.Length<=before.Organs.Length||grown.Organs.Any(o=>Math.Abs(o.Health-.65f)>1e-5))
            throw new InvalidOperationException("Live age/health did not reach graph organs");
        var afterGrowth=graph.InspectChunkGenerations();
        int changed=generations.Count(p=>afterGrowth.GetValueOrDefault(p.Key)!=p.Value);
        if(changed!=1)throw new InvalidOperationException("Growth dirtied "+changed+" old chunks");
        await Shot("grown",eye,target);
        growing.X=1.6;growing.Z=-1.1;world.Flora.RebuildIndex();flora.InvalidateGraphInputs();await Settle(population);
        var moved=graph.InspectPlant(growing.Id.Value)!;
        if(Math.Abs(moved.Position.X-growing.X)>1e-9||Math.Abs(moved.Position.Z-growing.Z)>1e-9||
            Math.Abs(moved.Position.Y-world.GroundHeight(growing.Position))>1e-9)throw new InvalidOperationException("Graph movement/support mismatch");
        var born=Plant("moonfern",-.4,-1.3,.6);await Settle(population+1);
        if(graph.InspectPlant(born.Id.Value)==null)throw new InvalidOperationException("Birth not presented");
        world.Flora.Remove(born.Id);await Settle(population);
        if(graph.InspectPlant(born.Id.Value)!=null)throw new InvalidOperationException("Removed plant still present");
        var views=new[] { (eye,target),(new Vector3(-6,3.5f,6),target),(new Vector3(0,5,8),target) };
        foreach(var view in views){session.CameraRig.LookAtPoint(view.Item1,view.Item2);await Settle(population);await Frames(35);}
        long descriptors=flora.GraphDescriptorsPublished,bytes=graph.InstanceBytesUploaded,builds=graph.ChunkRebuilds;
        foreach(var view in views){session.CameraRig.LookAtPoint(view.Item1,view.Item2);await Frames(40);}
        if(flora.GraphDescriptorsPublished!=descriptors||graph.InstanceBytesUploaded!=bytes||graph.ChunkRebuilds!=builds)
            throw new InvalidOperationException("Warm camera revisit rebuilt graph presentation");
        await Shot("final",eye,target);
        await Shot("garden_close",new Vector3(3.6f,2.1f,3.8f),new Vector3(.4f,.9f,.1f));
        if(catalogProof)
        {
            session.Flora.Visible=false;
            await Shot("coverage_close",new Vector3(1.1f,1.2f,1.8f),new Vector3(0,.35f,0));
            if(session.Coverage.TrianglesBuilt==0)throw new InvalidOperationException("Occupied coverage fixture built no triangles");
            session.Flora.Visible=true;
        }
        File.WriteAllText(Path.Combine(directory,"report.json"),JsonSerializer.Serialize(new {
            seed=world.Seed,live_plants=flora.GraphPlantCount,individual_species=plants.Select(f=>f.SpeciesId).Distinct().Count(),coverage_prototypes=prototypes,growth_changed_chunks=changed,
            initial_organs=before.Organs.Length,grown_organs=grown.Organs.Length,
            movement=true,birth=true,removal=true,warm_camera_descriptor_builds=0,warm_camera_uploads=0,
            graph_chunks=graph.ChunkCount,graph_mesh_bytes=graph.MeshBytes,coverage_tiles=session.Coverage.TileCount,coverage_triangles_built=session.Coverage.TrianglesBuilt,
            game_module=typeof(GraphPlantReviewRunner).Module.ModuleVersionId,sim_module=typeof(VivariumWorld).Module.ModuleVersionId,
            notes="Real population adapter proof, no user save or full performance acceptance. Coverage prototype checks do not establish populated tile visual acceptance."
        },new JsonSerializerOptions {WriteIndented=true}));
        GD.Print("GRAPH_PLANT_LIVE_OK "+flora.GraphPlantsDiagnostics);host.GetTree().Quit(0);
    }
}
