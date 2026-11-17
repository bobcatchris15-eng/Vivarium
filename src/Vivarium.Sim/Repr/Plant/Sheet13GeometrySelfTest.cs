using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet13GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet13Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 31, 804584441 })
        {
            var nut = WoodyGraphSpecies.Create(new FloraSpeciesDef { Id="nutglade", Height=2, RadiusAtMax=1 })!.PlanFor(seed,1);
            var roots = nut.Items.Where(o => o.Parent < 0 && o.Type == OrganType.Axis).ToArray();
            if (roots.Any(o => Math.Sqrt(o.P0.X*o.P0.X+o.P0.Z*o.P0.Z) < .14 || (o.P1-o.P0).Normalized().Y < .95))
                return "nutglade stems must rise upright from distributed bases";
            var moon = WoodyGraphSpecies.Create(new FloraSpeciesDef { Id="moonbark", Height=2, RadiusAtMax=1 })!.PlanFor(seed,1);
            foreach (var lower in moon.Items.Where(o => o.Parent < 0 && o.Type == OrganType.Axis))
            {
                var upper = moon.Items.Single(o => o.Parent == lower.Gen && o.Order == 1);
                if ((upper.P0-lower.P1).Length > 1e-7 || (upper.Flags & lower.Flags & OrganFlags.Continuous) == 0 || Math.Abs(upper.R0-lower.R1)>1e-7)
                    return "moonbark trunk sections must share position, radius and a continuous skin";
                foreach (var branch in moon.Items.Where(o => o.Parent == upper.Gen && o.Order == 2))
                {
                    var center = PlantMath.Hermite(upper.P0, upper.T0, upper.P1, upper.T1, branch.AttachT);
                    var tangent = PlantMath.HermiteD(upper.P0, upper.T0, upper.P1, upper.T1, branch.AttachT).Normalized();
                    var parentRadius = PlantMath.AxisRadius(upper.R0, upper.R1, upper.AxisWaist, branch.AttachT);
                    if ((branch.P0-center).Length > parentRadius*.4 || branch.T0.Normalized().Dot(tangent) < .4)
                        return "moonbark limbs must enter the parent and follow its local tangent";
                }
            }
            var generator = WoodyGraphSpecies.Create(new FloraSpeciesDef { Id="moonbark", Height=2, RadiusAtMax=1 })!;
            var plant = generator.Create(seed,1);
            generator.Grow(plant,1,1,null);
            foreach (int detail in new[] { 0, 1, 2 })
            {
                var wood = PlantCompiler.Compile(plant,detail).Wood;
                for (int v=0; v<wood.VertexCount; v++)
                    if (!wood.Position(v).IsFinite || !wood.NormalAt(v).IsFinite) return "moonbark wood mesh must be finite";
            }
            var lichen = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="moonshell_lichen", Height=.012, RadiusAtMax=.13 })!.PlanFor(seed,1);
            var matId = Array.FindIndex(lichen.Shapes, s => s.Name == "moonshell_lichen_colony_understory");
            var mat = lichen.Items.Where(o => o.ShapeId == matId && o.IsFlat).ToArray();
            if (mat.Length == 0 || mat.Min(o => o.Emerge) > .2) return "moonshell ground mat must establish early under its shoots";
        }
        return null;
    }
}

