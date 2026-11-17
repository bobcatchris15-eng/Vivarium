using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet16GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet16Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 31, 804584441 })
        {
            foreach (string id in new[] { "ruffle_lichen", "rustbellow_moss" })
            {
                var plan = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id=id, Height=.06, RadiusAtMax=.2 })!.PlanFor(seed,1);
                int shape = Array.FindIndex(plan.Shapes, s => s.Name == id + "_colony_understory");
                var blades = plan.Items.Where(o => o.ShapeId == shape && o.IsFlat).ToArray();
                if (blades.Length < 42 || blades.Max(o => o.Emerge) > .3)
                    return id + ": overlapping understory must establish early";
            }
            var generator = new ShadebellGenerator();
            var planShade = generator.PlanFor(seed,1);
            foreach (var lower in planShade.Items.Where(o => o.Parent < 0 && o.Type == OrganType.Axis))
            {
                var upper = planShade.Items.Single(o => o.Parent == lower.Gen && o.Order == 1);
                if ((upper.P0-lower.P1).Length > 1e-7 || Math.Abs(upper.R0-lower.R1)>1e-7 ||
                    (upper.Flags & lower.Flags & OrganFlags.Continuous) == 0 || upper.T0.Normalized().Dot(lower.T1.Normalized()) < .999)
                    return "shadebell trunk sections must share position, tangent, radius and skin";
                foreach (var branch in planShade.Items.Where(o => o.Parent == lower.Gen || o.Parent == upper.Gen).Where(o => o.Order == 2 && o.Type == OrganType.Axis))
                {
                    var host = planShade.Items[branch.Parent];
                    var center = PlantMath.Hermite(host.P0, host.T0, host.P1, host.T1, branch.AttachT);
                    var tangent = PlantMath.HermiteD(host.P0, host.T0, host.P1, host.T1, branch.AttachT).Normalized();
                    double radius = PlantMath.AxisRadius(host.R0,host.R1,host.AxisWaist,branch.AttachT);
                    if ((branch.P0-center).Length > radius*.4 || branch.T0.Normalized().Dot(tangent) < .4)
                        return "shadebell fork must enter its parent along its tangent";
                }
            }
            foreach (double age in new[] { .3, .6, 1.0 })
            {
                var plant = generator.Create(seed,1); generator.Grow(plant,age,1,null);
                foreach (int detail in new[] { 0, 1, 2 })
                {
                    var mesh = PlantCompiler.Compile(plant,detail).Wood;
                    for (int v=0; v<mesh.VertexCount; v++)
                        if (!mesh.Position(v).IsFinite || !mesh.NormalAt(v).IsFinite || mesh.NormalAt(v).LengthSq < .25)
                            return "shadebell wood must remain finite across ages and detail levels";
                    foreach (var child in plant.Organs.Where(o => o.Parent >= 0 && (o.Flags & OrganFlags.Continuous) != 0 && (plant.Organs[o.Parent].Flags & OrganFlags.Continuous) != 0 && (o.End-o.Start).Length > 1e-5))
                    {
                        var axis = child.StartTangent.Normalized(); int shared = 0;
                        for (int v=0; v<mesh.VertexCount; v++)
                        {
                            var delta=mesh.Position(v)-child.Start;
                            if (Math.Abs(delta.Dot(axis)) > 1e-5 || Math.Abs(delta.Length-child.RadiusStart) > 1e-5) continue;
                            bool before=false, after=false;
                            for (int t=0; t<mesh.Indices.Count; t+=3)
                            {
                                if (mesh.Indices[t]!=v && mesh.Indices[t+1]!=v && mesh.Indices[t+2]!=v) continue;
                                for (int j=0;j<3;j++) { double side=(mesh.Position(mesh.Indices[t+j])-child.Start).Dot(axis); before |= side < -1e-8; after |= side > 1e-8; }
                            }
                            if (before && after) shared++;
                        }
                        if (shared < 8) return "shadebell trunk continuation must share its mesh ring";
                    }
                }
            }
        }
        return null;
    }
}
