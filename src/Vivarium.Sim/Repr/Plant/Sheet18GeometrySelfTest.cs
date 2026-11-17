using System;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet18GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet18Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 91, 804584441 })
        {
            var gen = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="starsplit", Height=1, RadiusAtMax=1 })!;
            var plan = gen.PlanFor(seed, 1);
            var bodies = plan.Items.Where(o => o.Type == OrganType.Flower && plan.Shapes[o.ShapeId].Name == "starsplit_open_body").ToArray();
            if (bodies.Length != 1) return "starsplit needs one open fruit body";
            var shape = plan.Shapes[bodies[0].ShapeId];
            if (shape.SurfaceForm != OrganSurfaceForm.Cup || shape.LobeCount < 5 || shape.LobeDepth < .3f || shape.ColorAbaxial.X <= shape.ColorAbaxial.Y * 1.5)
                return "starsplit needs a star rim and contrasting inner flesh";
            foreach (int lod in new[] { 0, 1, 2 })
            {
                var mesh = PlantCompiler.BuildShapeMesh(shape, lod);
                if (mesh.Positions.Any(v => !float.IsFinite(v)) || mesh.Normals.Any(v => !float.IsFinite(v))) return "invalid starsplit surface";
                // The mouth must stay open at the axis, while the interior extends below the rim.
                if (mesh.Position(0).Z >= .1 || !Enumerable.Range(0, mesh.VertexCount).Any(i => mesh.Position(i).Z > .95 && Math.Sqrt(Math.Pow(mesh.Position(i).X,2)+Math.Pow(mesh.Position(i).Y,2)) > .10))
                    return "starsplit cavity lacks depth or a rim";
                double depth = RayDepth(mesh);
                if (depth < .18 || depth > 1.1) return "starsplit mouth is sealed or has no interior wall";
                foreach (double age in new[] { .35, .7, 1.0 })
                {
                    var plant = gen.Create(seed, 1); gen.Grow(plant, age, 1, null);
                    var assembly = PlantCompiler.Compile(plant, lod);
                    if (assembly.Wood.Positions.Any(v => !float.IsFinite(v))) return "invalid starsplit assembly";
                }
            }
        }
        return null;
    }
    private static double RayDepth(MeshData mesh)
    {
        var origin = new Vec3(.035, 0, 1.02); var direction = -Vec3.Forward;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a=mesh.Position(mesh.Indices[i]); var e1=mesh.Position(mesh.Indices[i+1])-a; var e2=mesh.Position(mesh.Indices[i+2])-a;
            var p=direction.Cross(e2); double det=e1.Dot(p); if(Math.Abs(det)<1e-10) continue;
            var d=origin-a; double u=d.Dot(p)/det; if(u<0 || u>1) continue;
            var q=d.Cross(e1); double v=direction.Dot(q)/det; if(v<0 || u+v>1) continue;
            double distance=e2.Dot(q)/det; if(distance>0) nearest=Math.Min(nearest,distance);
        }
        return nearest;
    }
}
