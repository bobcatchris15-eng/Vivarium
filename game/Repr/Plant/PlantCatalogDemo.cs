using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;

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
        string batch = Main.ArgAfter("--plant-catalog-batch") ?? "all";
        bool legacy=Main.UserArgs.Contains("--plant-catalog-legacy");
        bool macro=Main.UserArgs.Contains("--plant-catalog-macro");
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
            if(scaleReference?.TryGetValue(entry.Id,out var referenceScale)==true)scale=referenceScale;
            var origin = new Vector3(stage*8,0,0);
            root.AddChild(PlantDemoKit.Ground(origin,5));
            for(int j=0;j<3;j++)
            {
                var p=generator.Create(ctx.WorldSeed,(ulong)(index*10+j));
                generator.Grow(p,new[]{.2,.55,1.0}[j],1,null);
                p.Position=new(origin.X+(j-1)*1.45,0,0); p.Scale=scale;
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
            var target=origin+new Vector3(0,.42f,0);
            _shots.Add(new ReprShot(entry.Id,origin+new Vector3(.6f,1.45f,5.5f),target,44,2,legacy?"legacy":"growth"));
            ctx.Log($"catalog {entry.Id}: {entry.Family}; {entry.Silhouette}; {entry.GrowthHabit}; mature organs={mature.Organs.Length}, H={h}, R={r}");
            var label=new Label3D { Text=entry.Id+(legacy?"  |  previous form":"  |  20% / 55% / mature"), Position=origin+new Vector3(0,-.13f,.7f),
                FontSize=40, PixelSize=.0013f, Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest=true };
            root.AddChild(label);
        }
        _renderer=PlantDemoKit.NewRenderer(ctx,"Catalog");
        _renderer.Detail0Px=0; _renderer.Detail1Px=0; _renderer.CommitsPerFrame=64;
        root.AddChild(_renderer); _renderer.SetPlants(plants);
    }
    public void Tick(double dt) { if(_renderer.PendingBuilds>0)_renderer.Drain(); }
}
