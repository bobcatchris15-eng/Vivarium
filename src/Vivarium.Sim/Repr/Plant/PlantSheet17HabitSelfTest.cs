using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class PlantSheet17HabitSelfTest : IReprSelfTest
{
    public string Name => "plant.sheet17Habit";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 91, 804584441 })
        {
            var silver = WoodyGraphSpecies.Create(new FloraSpeciesDef { Id="silverfelt", Height=1, RadiusAtMax=1 })!.PlanFor(seed, 1);
            var roots = silver.Items.Where(o => o.Parent < 0 && o.Type == OrganType.Axis).ToArray();
            if (roots.Length < 5 || roots.Count(o => o.P0.Length > .12) < 5 || roots.Any(o => o.T0.Y <= Math.Sqrt(o.T0.X*o.T0.X+o.T0.Z*o.T0.Z)))
                return "silverfelt basal shoots do not rise from distributed origins";
            var gen = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="snaptrap", Height=1, RadiusAtMax=1 })!;
            var plan = gen.PlanFor(seed, 1);
            var flower = plan.Shapes.FirstOrDefault(s => s.Name == "snaptrap_trumpet");
            if (flower == null || flower.SurfaceForm != OrganSurfaceForm.Trumpet || flower.LobeCount < 4 || flower.LobeDepth < .1f)
                return "latchjaw lacks a scalloped terminal trumpet";
            var jaws = plan.Items.Where(o => o.IsBlade && plan.Shapes[o.ShapeId].Name == "snaptrap_jaw").ToArray();
            if (jaws.Length < 12 || jaws.GroupBy(o => o.Parent).Any(g => g.Count() != 2)) return "latchjaw jaws are not paired";
            var bloom = plan.Items.Single(o => o.Type == OrganType.Flower && plan.Shapes[o.ShapeId].Name == "snaptrap_trumpet");
            // The trumpet may sit on a short Continuous neck chain welded to the column
            // top (gradual funnel join); the column itself must still be thick and the
            // trap arms direct children of it.
            int colIdx = bloom.Parent;
            while (colIdx >= 0 && (plan.Items[colIdx].Flags & OrganFlags.Continuous) != 0 && plan.Items[colIdx].Parent >= 0)
                colIdx = plan.Items[colIdx].Parent;
            var column = plan.Items[colIdx];
            if (column.R0 < .08 || column.R1 < .05 || bloom.AttachT < .99)
                return "latchjaw flower is not terminal on a thick column";
            var arms = jaws.Select(o => plan.Items[o.Parent]).ToArray();
            if (arms.Any(o => o.Parent != colIdx) || arms.Min(o => o.AttachT) > .15 || arms.Max(o => o.AttachT) < .8)
                return "latchjaw traps do not span the column";
            var trumpetMesh = PlantCompiler.BuildShapeMesh(flower, 0);
            if (trumpetMesh.Positions.Any(v => !float.IsFinite(v)) || trumpetMesh.Normals.Any(v => !float.IsFinite(v)))
                return "invalid latchjaw trumpet surface";
            // Last outer ring: neighboring lobe and valley must differ in radius.
            var lobe = trumpetMesh.Position(400); var valley = trumpetMesh.Position(404);
            double lobeRadius = Math.Sqrt(lobe.X*lobe.X + lobe.Y*lobe.Y);
            double valleyRadius = Math.Sqrt(valley.X*valley.X + valley.Y*valley.Y);
            if (lobeRadius < valleyRadius * 1.2) return "latchjaw trumpet rim is not scalloped";
            foreach (double age in new[] { .35, .7, 1.0 })
            {
                var plant = gen.Create(seed, 1); gen.Grow(plant, age, 1, null);
                foreach (int lod in new[] { 0, 1, 2 })
                {
                    var assembly = PlantCompiler.Compile(plant, lod);
                    if (assembly.Wood.Positions.Any(v => !float.IsFinite(v))) return "non-finite latchjaw geometry";
                }
            }
        }
        return null;
    }
}

