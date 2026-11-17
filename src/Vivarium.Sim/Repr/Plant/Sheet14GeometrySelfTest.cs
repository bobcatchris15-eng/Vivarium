using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet14GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet14Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 31, 804584441 })
        {
            foreach (string id in new[] { "pearl_cushion_moss", "pinequilt_moss" })
            {
                var plan = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id=id, Height=.1, RadiusAtMax=.3 })!.PlanFor(seed,1);
                int matId = Array.FindIndex(plan.Shapes, s => s.Name == id + "_colony_understory");
                var mat = plan.Items.Where(o => o.ShapeId == matId && o.IsFlat).ToArray();
                if (mat.Length < 42 || mat.Min(o => o.Emerge) > .2 || mat.Max(o => o.Emerge) > .3)
                    return id + ": continuous moss understory must establish early";
            }
            var gen = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="pondstars", Height=.1, RadiusAtMax=.5 })!;
            var planStars = gen.PlanFor(seed,1);
            var leaves = planStars.Items.Where(o => planStars.Shapes[o.ShapeId].Name.StartsWith("pondstars_star_")).ToArray();
            if (leaves.Length < 12 || leaves.Select(o => planStars.Shapes[o.ShapeId].LobeCount).Distinct().Count() != 3)
                return "pondstars must have distinct four-, five-, and six-point leaflets";
            if (leaves.Any(o => Math.Abs((o.P1-o.P0).Normalized().Y) > .01 || Math.Abs(o.Up.Normalized().Y) < .99))
                return "pondstars leaflets must lie horizontally";
            foreach (double age in new[] { .35, .7, 1.0 })
            {
                var plant=gen.Create(seed,1); gen.Grow(plant,age,1,null);
                foreach (int detail in new[] { 0, 1, 2 })
                {
                    var assembly=PlantCompiler.Compile(plant,detail);
                    var starInstances = assembly.Instances.Where(i => plant.Shapes[i.ShapeId].Name.StartsWith("pondstars_star_")).ToArray();
                    if (starInstances.Length == 0) return "pondstars leaflets must be visible throughout growth";
                    foreach (var instance in starInstances)
                    {
                        var shape=plant.Shapes[instance.ShapeId];
                        var mesh=PlantCompiler.BuildShapeMesh(shape,detail);
                        if (mesh.VertexCount < shape.LobeCount*2+1 || mesh.TriangleCount < shape.LobeCount*2 ||
                            Enumerable.Range(0,mesh.VertexCount).Any(v => !mesh.Position(v).IsFinite || !mesh.NormalAt(v).IsFinite))
                            return "pondstars star surfaces must remain complete and finite at every LOD";
                        for (int v=1; v<=shape.LobeCount*2; v++)
                        {
                            var point=mesh.Position(v);
                            double radius=Math.Sqrt(point.X*point.X+(point.Z-.5)*(point.Z-.5));
                            if (Math.Abs(point.Y)>1e-6 || (v%2 == 1 ? radius < .48 : radius > .25))
                                return "pondstars leaf outline must alternate sharp tips and recessed sinuses";
                        }
                    }
                }
            }
            var pitcher = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="pitcher_plant", Height=1, RadiusAtMax=.5 })!.PlanFor(seed,1);
            foreach (var shape in pitcher.Shapes.Where(s => s.Name.StartsWith("pitcher_plant_curved_vessel_")))
                foreach (int detail in new[] { 0, 1, 2 })
                {
                    var mesh=PlantCompiler.BuildShapeMesh(shape,detail);
                    if (!mesh.UV2.Where((_,i) => i%2==1).Any(v => v == -2) || !mesh.UV2.Where((_,i) => i%2==1).Any(v => v == -3))
                        return "pitcher outer and inner walls must reach the live pitcher material";
                }
        }
        return null;
    }
}
