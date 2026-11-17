using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet20GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet20Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 91, 804584441 })
        {
            var tide = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="tidebell", Height=1, RadiusAtMax=1 })!;
            var plan = tide.PlanFor(seed, 1);
            var foliage = plan.Items.Where(o => o.Type == OrganType.Leaflet).ToArray();
            if (foliage.Length < 80 || foliage.Select(o => o.Parent).Distinct().Count() < 15) return "tidebell needs dense compound foliage";
            var bell = plan.Shapes.Single(s => s.Name == "tidebell_luminous_bell");
            foreach (int lod in new[] { 0, 1, 2 })
            {
                var bellMesh = PlantCompiler.BuildShapeMesh(bell, lod);
                if (!Enumerable.Range(0,bellMesh.VertexCount).All(i => Math.Abs(bellMesh.UV2[i*2]-1.45f)<.001f))
                    return "tidebell bell lost its emission marker";
            }
            var tank = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="tankbloom", Height=1, RadiusAtMax=1 })!.PlanFor(seed,1);
            var bract = tank.Shapes.Single(s => s.Name == "tankbloom_bract");
            if (bract.Type != OrganType.Flower || PlantCompiler.BuildShapeMesh(bract,0).UV2[0] < 1.3f)
                return "tankbloom bract lost its colored organ material";
            var woody = WoodyGraphSpecies.Create(new FloraSpeciesDef { Id="thornmantle", Height=1, RadiusAtMax=1 })!.PlanFor(seed,1);
            var roots = woody.Items.Where(o => o.Parent < 0).ToArray();
            if (roots.Length < 5 || roots.Any(o => o.P0.Length < .1)) return "thornmantle stems need distributed roots";
            var trifold = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="trifold", Height=1, RadiusAtMax=1 })!;
            var tp = trifold.PlanFor(seed, 1);
            var groups = tp.Items.Where(o => o.Type == OrganType.Leaflet).GroupBy(o => o.Parent).ToArray();
            if (groups.Length < 4 || groups.Any(g => g.Count() != 3)) return "trifold needs three leaflets per stalk";
            foreach (double age in new[] { .35, .7, 1.0 }) foreach (int lod in new[] { 0, 1, 2 })
            {
                var plant = tide.Create(seed, 1); tide.Grow(plant, age, 1, null);
                var before = plant.Organs.ToArray();
                TidebellWaterPose.Apply(plant, .8, .65);
                for (int i=0; i<plant.Organs.Length; i++)
                {
                    var o = plant.Organs[i];
                    if (o.Type == OrganType.Leaflet && (o.Start != before[i].Start || o.End != before[i].End))
                        return "water pose moved foliage";
                    if (o.Type == OrganType.Flower && plant.Shapes[o.ShapeId].Name == "tidebell_luminous_bell")
                    {
                        if (Math.Min(o.Start.Y,o.End.Y)*.65 < .8) return "bell remains underwater";
                        if (((o.Start - plant.Organs[o.Parent].End) - (before[i].Start - before[o.Parent].End)).Length > .00001) return "bell detached from scape";
                        if (plant.Organs[o.Parent].Start != before[o.Parent].Start) return "scape lost rooted base";
                    }
                }
                var mesh = PlantCompiler.Compile(plant, lod);
                if (mesh.Wood.Positions.Any(v => !float.IsFinite(v))) return "non-finite tidebell assembly";
            }
        }
        return null;
    }
}
