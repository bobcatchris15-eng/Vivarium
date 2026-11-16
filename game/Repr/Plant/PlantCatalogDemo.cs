using System;
using System.Collections.Generic;
using System.Linq;
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
        var entries = PlantGraphCatalog.Species.Where(s => batch == "all" ||
            (batch == "woody" ? WoodyGraphSpecies.Species : batch == "herb" ? HerbGraphSpecies.Species : CryptogamGraphSpecies.Species).Any(v=>v.Id==s.Id)).ToArray();
        var plants = new List<PlantDescriptor>();
        int index=0;
        foreach(var entry in entries)
        {
            var sp = ctx.World.Content.FloraById(entry.Id) ?? throw new InvalidOperationException(entry.Id);
            var generator = entry.Id is "glowbriar" or "moonfern" or "shadebell" ? PlantDemoKit.Generator(entry.Id) : PlantGraphCatalog.Create(sp);
            var mature = generator.Create(ctx.WorldSeed,1); generator.Grow(mature,1,1,null);
            // Normalize physical size only for catalog framing. Relative proportions remain authored.
            float h=PlantDemoKit.HeightOf(mature), r=PlantDemoKit.CrownOf(mature);
            double scale=1/Math.Max(.01, Math.Max(h,2*r));
            var origin = new Vector3(index++*8,0,0);
            root.AddChild(PlantDemoKit.Ground(origin,5));
            for(int j=0;j<3;j++)
            {
                var p=generator.Create(ctx.WorldSeed,(ulong)(index*10+j));
                generator.Grow(p,new[]{.2,.55,1.0}[j],1,null);
                p.Position=new(origin.X+(j-1)*1.45,0,0); p.Scale=scale;
                plants.Add(p);
            }
            var target=origin+new Vector3(0,.42f,0);
            _shots.Add(new ReprShot(entry.Id,origin+new Vector3(.6f,1.45f,5.5f),target,44,2,"growth"));
            ctx.Log($"catalog {entry.Id}: {entry.Family}; {entry.Silhouette}; {entry.GrowthHabit}; mature organs={mature.Organs.Length}, H={h}, R={r}");
            var label=new Label3D { Text=entry.Id+"  |  20% / 55% / mature", Position=origin+new Vector3(0,-.13f,.7f),
                FontSize=40, PixelSize=.0013f, Billboard=BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest=true };
            root.AddChild(label);
        }
        _renderer=PlantDemoKit.NewRenderer(ctx,"Catalog");
        _renderer.Detail0Px=0; _renderer.Detail1Px=0; _renderer.CommitsPerFrame=64;
        root.AddChild(_renderer); _renderer.SetPlants(plants);
    }
    public void Tick(double dt) { if(_renderer.PendingBuilds>0)_renderer.Drain(); }
}
