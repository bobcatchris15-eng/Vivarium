using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;
using Vivarium.Sim.Geometry;

namespace Vivarium.Repr;

/// <summary>Native-rendered catalog; one normalized stage per species, including its growth sequence.</summary>
public sealed class PlantCatalogDemo : IReprDemo
{
    public string Name => "plant_catalog";
    private readonly List<ReprShot> _shots = new();
    public IReadOnlyList<ReprShot> Shots => _shots;
    private PlantAssemblyRenderer _renderer = null!;
    public void Build(Node3D root, ReprDemoContext ctx)
    {
        if (Main.UserArgs.Contains("--plant-catalog-support-proof"))
        {
            BuildBarkProof(root,ctx);
            return;
        }
        string batch = Main.ArgAfter("--plant-catalog-batch") ?? "all";
        bool legacy=Main.UserArgs.Contains("--plant-catalog-legacy");
        bool macro=Main.UserArgs.Contains("--plant-catalog-macro");
        bool matureOnly=Main.UserArgs.Contains("--plant-catalog-mature-only");
        var scaleReference=Main.ArgAfter("--plant-catalog-scale-reference") is { } scalePath
            ? JsonSerializer.Deserialize<Dictionary<string,double>>(File.ReadAllText(scalePath)) : null;
        var selected=Main.ArgAfter("--plant-catalog-species")?.Split(',').ToHashSet(StringComparer.Ordinal);
        var entries = PlantGraphCatalog.Species.Where(s => batch == "all" ||
            (batch == "woody" ? WoodyGraphSpecies.Species : batch == "herb" ? HerbGraphSpecies.Species : CryptogamGraphSpecies.Species).Any(v=>v.Id==s.Id)).ToArray();
        var plants = new List<PlantDescriptor>();
        int index=0;
        foreach(var entry in entries)
        {
            int stage=index++;
            if(selected!=null&&!selected.Contains(entry.Id))continue;
            var sp = ctx.World.Content.FloraById(entry.Id) ?? throw new InvalidOperationException(entry.Id);
            var generator = entry.Id is "glowbriar" or "moonfern" or "shadebell" ? PlantDemoKit.Generator(entry.Id) : PlantGraphCatalog.Create(sp);
            var mature = generator.Create(ctx.WorldSeed,1); generator.Grow(mature,1,1,null);
            // Normalize physical size only for catalog framing. Relative proportions remain authored.
            float h=PlantDemoKit.HeightOf(mature), r=PlantDemoKit.CrownOf(mature);
            double scale=1/Math.Max(.01, Math.Max(h,2*r));
            if(matureOnly)scale=1/Math.Max(.01,Math.Max(sp.Height,2*sp.RadiusAtMax));
            if(scaleReference?.TryGetValue(entry.Id,out var referenceScale)==true)scale=referenceScale;
            var origin = new Vector3(stage*8,0,0);
            root.AddChild(PlantDemoKit.Ground(origin,5));
            for(int j=0;j<3;j++)
            {
                if(matureOnly&&j!=2)continue;
                var p=generator.Create(ctx.WorldSeed,(ulong)(index*10+j));
                generator.Grow(p,new[]{.2,.55,1.0}[j],1,null);
                p.Position=new(origin.X+(matureOnly?0:(j-1)*1.45),0,0); p.Scale=scale;
                if (!legacy && entry.Id=="emberfan_fungus")
                {
                    // Display the authored +Z bark-facing frame on an explicit support,
                    // matching the live renderer's world-up shelf orientation.
                    var wood=new StandardMaterial3D { AlbedoColor=new Color(.24f,.14f,.085f),Roughness=1 };
                    root.AddChild(new MeshInstance3D { Name="EmberfanBarkScaffold", Position=new Vector3((float)p.Position.X,.43f,-.20f),
                        Mesh=new CylinderMesh { TopRadius=.16f,BottomRadius=.22f,Height=.86f,RadialSegments=18 },MaterialOverride=wood });
                }
                if(legacy)
                {
                    var previous=ctx.BuildLegacyFlora(entry.Id,0,0,j<2);
                    if(previous!=null)
                    {
                        previous.Position=new Vector3((float)p.Position.X,0,0);
                        previous.Scale=Vector3.One*(float)scale;
                        root.AddChild(previous);
                    }
                }
                else plants.Add(p);
                if(macro&&!legacy&&j==2)
                {
                    if(entry.Id=="banksorrow")
                    {
                        var continuation=p.Organs.First(o=>o.Type==OrganType.Axis&&(o.Flags&OrganFlags.Terminal)!=0);
                        var branch=p.Organs[continuation.Parent];
                        var centre=branch.End;
                        var focus=new Vector3((float)(p.Position.X+centre.X*scale),(float)(centre.Y*scale),(float)(centre.Z*scale));
                        float distance=(float)Math.Max(.16,(branch.End-branch.Start).Length*scale*2.5);
                        _shots.Add(new ReprShot(entry.Id+"_branch_join",focus+new Vector3(.5f,.6f,1).Normalized()*distance,focus,40,2,"growth"));
                    }
                    if(entry.Id=="bannerreed")
                    {
                        var rachis=p.Organs.First(o=>o.Type==OrganType.Axis&&o.Order==2);
                        var centre=(rachis.Start+rachis.End)*.5;
                        var focus=new Vector3((float)(p.Position.X+centre.X*scale),(float)(centre.Y*scale),(float)(centre.Z*scale));
                        float distance=(float)Math.Max(.12,(rachis.End-rachis.Start).Length*scale*3.5);
                        _shots.Add(new ReprShot(entry.Id+"_seedhead",focus+new Vector3(.4f,.25f,1).Normalized()*distance,focus,40,2,"growth"));
                    }
                    if(entry.Id=="witchberry")
                    {
                        var fruit=p.Organs.Where(o=>o.Type==OrganType.Fruit).OrderByDescending(o=>o.Start.Z).First();
                        var centre=(fruit.Start+fruit.End)*.5;
                        var focus=new Vector3((float)(p.Position.X+centre.X*scale),(float)(centre.Y*scale),(float)(centre.Z*scale));
                        float distance=(float)Math.Max(.12,(fruit.End-fruit.Start).Length*scale*8);
                        _shots.Add(new ReprShot(entry.Id+"_berries",focus+new Vector3(.3f,.3f,1).Normalized()*distance,focus,40,2,"growth"));
                        var bole=p.Organs.Where(o=>(o.Flags&OrganFlags.Continuous)!=0).ToArray();
                        centre=(bole[0].Start+bole[^1].End)*.5;
                        focus=new Vector3((float)(p.Position.X+centre.X*scale),(float)(centre.Y*scale),(float)(centre.Z*scale));
                        distance=(float)Math.Max(.2,(bole[^1].End-bole[0].Start).Length*scale*1.8);
                        _shots.Add(new ReprShot(entry.Id+"_burls",focus+new Vector3(.7f,.25f,1).Normalized()*distance,focus,40,2,"growth"));
                    }
                    if(entry.Id=="hollowcrown")
                    {
                        var bole=p.Organs.First(o=>(o.Flags&OrganFlags.HollowWindow)!=0);
                        var centre=bole.Start+(bole.End-bole.Start)*.5;
                        var focus=new Vector3((float)(p.Position.X+centre.X*scale),(float)(centre.Y*scale),(float)(centre.Z*scale));
                        float distance=(float)Math.Max(.15,bole.RadiusStart*scale*5);
                        _shots.Add(new ReprShot(entry.Id+"_cavity",focus+new Vector3(0,.25f,1)*distance,focus,40,2,"growth"));
                    }
                    var leaves=p.Organs.Where(o=>o.Type is OrganType.Leaf or OrganType.Leaflet).ToArray();
                    if(leaves.Length>0)
                    {
                        double front=leaves.Max(o=>o.Start.Z);
                        var blade=leaves.Where(o=>o.Start.Z>front-.18).OrderByDescending(o=>o.RadiusStart*(o.End-o.Start).Length).First();
                        var mid=blade.Start+(blade.End-blade.Start)*.45;
                        var focus=new Vector3((float)(p.Position.X+mid.X*scale),(float)(mid.Y*scale),(float)(mid.Z*scale));
                        float distance=(float)Math.Max(.10,(blade.End-blade.Start).Length*scale*2.8);
                        _shots.Add(new ReprShot(entry.Id+"_foliage",focus+new Vector3(.25f,.3f,1).Normalized()*distance,focus,40,2,"growth"));
                    }
                }
            }
            var target=origin+new Vector3(0,matureOnly?.52f:.42f,0);
            var eye=origin+(matureOnly?new Vector3(.65f,.95f,2.75f):new Vector3(.6f,1.45f,5.5f));
            _shots.Add(new ReprShot(entry.Id,eye,target,matureOnly?40:44,2,legacy?"legacy":"growth"));
            ctx.Log($"catalog {entry.Id}: {entry.Family}; {entry.Silhouette}; {entry.GrowthHabit}; mature organs={mature.Organs.Length}, H={h}, R={r}");
            var label=new Label3D { Text=(matureOnly?sp.Name:entry.Id)+(legacy?"  |  previous form":matureOnly?"  |  current mature form":"  |  20% / 55% / mature"), Position=origin+new Vector3(0,-.13f,.7f),
                FontSize=40, PixelSize=.0013f, Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest=true };
            root.AddChild(label);
        }
        _renderer=PlantDemoKit.NewRenderer(ctx,"Catalog");
        _renderer.Detail0Px=0; _renderer.Detail1Px=0; _renderer.CommitsPerFrame=64;
        root.AddChild(_renderer); _renderer.SetPlants(plants);
    }
    public void Tick(double dt) { if(_renderer.PendingBuilds>0)_renderer.Drain(); }

    private void BuildBarkProof(Node3D root, ReprDemoContext ctx)
    {
        var world=VivariumWorld.CreateBaseline(ctx.World.Content,new WorldDescriptor {
            Diameter=9,CellSize=.1,Seed=804584441,PilotTreeId="none",GuardianId="none",
            Terrain=new TerrainProfile { BaseHeight=.3,Relief=0,MicroRelief=0 },
            Water=new WaterConfig { WaterTable=0,Infiltration=0,Evaporation=0 } });
        var plants=new List<PlantDescriptor>();
        root.AddChild(PlantDemoKit.Ground(new Vector3(0,.295f,0),8));
        var log=new LogProp { Id=world.Ids.Next(EntityKind.Log),X=1,Z=0,Y=.46,Length=1.15,Radius=.16,
            VariantSeed=357,DecayClass=2 };
        world.Props.Logs.Add(log);
        var logMaterial=new StandardMaterial3D { VertexColorUseAsAlbedo=true,Roughness=.96f };
        root.AddChild(new MeshInstance3D { Name="ProductionLogMesh",Mesh=ctx.ToArrayMesh(PropMeshes.Log(log)),
            Position=new Vector3((float)log.X,(float)log.Y,(float)log.Z),MaterialOverride=logMaterial });
        var treeSpecies=world.Content.FloraOrThrow("moonbark");
        var tree=new FloraIndividual { Id=world.Ids.Next(EntityKind.Flora),SpeciesId=treeSpecies.Id,
            X=-1,Z=0,Biomass=treeSpecies.MaxBiomass,Age=treeSpecies.MaturityAge,Health=1 };
        world.Flora.Add(tree);
        var treeGenerator=PlantGraphCatalog.Create(treeSpecies);
        var trunk=treeGenerator.Create(world.Seed,tree.Id.Value);treeGenerator.Grow(trunk,1,1,null);
        trunk.Position=new Vec3(tree.X,world.GroundHeight(tree.Position),tree.Z);
        trunk.Yaw=Rng.HashUnit(world.Seed,Hash.Fnv1a64("graph.flora.yaw"),tree.Id.Value)*Math.Tau;
        plants.Add(trunk);
        var fungusSpecies=world.Content.FloraOrThrow("emberfan_fungus");
        foreach(var host in new[] { "log", "trunk" })
        {
            var query=host=="log" ? new Vec2(1,.18) : new Vec2(-.92,.10);
            ulong id=world.Ids.Next(EntityKind.Flora).Value;
            if (!world.FloraSystem.TryEmberfanSupport(query,id,out var contact,out var normal))
                throw new InvalidOperationException("Emberfan support proof missing "+host);
            var generator=PlantGraphCatalog.Create(fungusSpecies);
            var fungus=generator.Create(world.Seed,id);generator.Grow(fungus,1,1,null);
            EmberfanSupportPose.Apply(fungus,normal);fungus.Position=contact;plants.Add(fungus);
            var focus=new Vector3((float)contact.X,(float)(contact.Y+.035),(float)contact.Z);
            var outward=new Vector3((float)normal.X,0,(float)normal.Z);
            var side=Vector3.Up.Cross(outward);
            _shots.Add(new ReprShot("emberfan_"+host+"_contact",focus+outward*.62f+Vector3.Up*.23f+side*.24f,focus,40,2,"support"));
            ctx.Log($"EMBERFAN_SUPPORT_PROOF host={host} query={query} contact={contact} normal={normal} organs={fungus.Organs.Length}");
        }
        _renderer=PlantDemoKit.NewRenderer(ctx,"EmberfanProductionSupportProof");
        _renderer.Detail0Px=0;_renderer.Detail1Px=0;_renderer.CommitsPerFrame=64;
        root.AddChild(_renderer);_renderer.SetPlants(plants);
    }
}

/// <summary>Shared live/catalog attachment pose; the fans stay world-horizontal.</summary>
internal static class EmberfanSupportPose
{
    public static void Apply(PlantDescriptor plant,Vec3 normal)
    {
        var forward=PlantMath.SafeNormal(new Vec3(normal.X,0,normal.Z),Vec3.Forward);
        var right=Vec3.Up.Cross(forward);
        Vec3 Rotate(Vec3 v)=>right*v.X+Vec3.Up*v.Y+forward*v.Z;
        // Sessile flesh grows a little into the bark instead of balancing on its relief peaks.
        var embed=forward*.008;
        for(int i=0;i<plant.Organs.Length;i++)
        {
            var organ=plant.Organs[i];
            organ.Start=Rotate(organ.Start)-embed;organ.End=Rotate(organ.End)-embed;
            organ.StartTangent=Rotate(organ.StartTangent);organ.EndTangent=Rotate(organ.EndTangent);
            organ.Up=Rotate(organ.Up);plant.Organs[i]=organ;
        }
        PlanGenerator.Summarize(plant);
    }
}
