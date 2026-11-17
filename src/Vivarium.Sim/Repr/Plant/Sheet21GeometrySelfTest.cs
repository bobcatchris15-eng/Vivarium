using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet21GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet21Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 91, 804584441 })
        {
            foreach (string id in new[] { "velvetweave_moss", "wispthread_moss" })
            {
                var p = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id=id, Height=1, RadiusAtMax=1 })!.PlanFor(seed,1);
                var mat=p.Items.Where(o => o.IsFlat && p.Shapes[o.ShapeId].Name==id+"_colony_understory").ToArray();
                if (mat.Length<40 || mat.Count(o => o.Emerge<.2)<40) return id+" needs an early overlapping mat";
            }
            var blade=HerbGraphSpecies.Create(new FloraSpeciesDef { Id="veilblade", Height=1, RadiusAtMax=1 })!;
            var bp=blade.PlanFor(seed,1);
            var blades=bp.Items.Where(o => o.IsBlade && bp.Shapes[o.ShapeId].Name=="veilblade_basal_blade").ToArray();
            var heads=bp.Items.Where(o => o.IsFlat && bp.Shapes[o.ShapeId].Name=="veilblade_seed").ToArray();
            if (blades.Length<24 || blades.Select(o => o.Parent).Distinct().Count()<12 || heads.Length<12) return "veilblade requires distributed blades and tall seedheads";
            if (heads.Min(o => o.P0.Y) <= blades.Max(o => o.P1.Y)) return "veilblade seedheads must rise above the blades";
            var fern=HerbGraphSpecies.Create(new FloraSpeciesDef { Id="veilfern", Height=1, RadiusAtMax=1 })!;
            var fp=fern.PlanFor(seed,1);
            var pinnae=fp.Items.Where(o => o.IsBlade && fp.Shapes[o.ShapeId].Name=="veilfern_pinna").ToArray();
            if (pinnae.Length<200 || pinnae.Select(o => o.Parent).Distinct().Count()<24 || pinnae.Any(o => 2*o.R0/(o.P1-o.P0).Length>.22)) return "veilfern needs narrow paired pinnae along arching rachises";
            var widow=HerbGraphSpecies.Create(new FloraSpeciesDef { Id="widowspire", Height=1, RadiusAtMax=1 })!;
            var wp=widow.PlanFor(seed,1);
            var curls=wp.Items.Where(o => o.IsAxis && o.Order==2 && (o.Flags & OrganFlags.Continuous)!=0).ToArray();
            if (curls.Length<6 || curls.Any(o => wp.Items[o.Parent].R0 < wp.Items[o.Parent].R1*4 || o.R1>=o.R0 || (o.P0-wp.Items[o.Parent].P1).Length>1e-7)) return "widowspire stems need sharply tapered bases and curled tips";
            foreach (double age in new[] { .35, .7, 1.0 }) foreach (int lod in new[] { 0, 1, 2 })
                foreach (var gen in new[] { blade, fern, widow, CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="velvetweave_moss",Height=1,RadiusAtMax=1 })!, CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="wispthread_moss",Height=1,RadiusAtMax=1 })! })
                {
                    var plant=gen.Create(seed,1); gen.Grow(plant,age,1,null);
                    var a=PlantCompiler.Compile(plant,lod);
                    if (a.Wood.Positions.Any(v => !float.IsFinite(v))) return "non-finite sheet 21 assembly";
                }
        }
        return null;
    }
}
