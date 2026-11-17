using System;
using System.Linq;
using Vivarium.Sim.Content;
namespace Vivarium.Sim.Repr.Plant;
public sealed class Sheet15GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet15Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 31, 804584441 })
        {
            var moss=CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="quillfeather_moss", Height=.1, RadiusAtMax=.3 })!.PlanFor(seed,1);
            int mat=Array.FindIndex(moss.Shapes,s=>s.Name=="quillfeather_moss_colony_understory");
            if (moss.Items.Count(o=>o.ShapeId==mat && o.IsFlat && o.Emerge<.3)<42) return "quillfeather needs an early continuous mat";
            var sedge=HerbGraphSpecies.Create(new FloraSpeciesDef { Id="quillsedge", Height=1, RadiusAtMax=.5 })!;
            var plan=sedge.PlanFor(seed,1);
            var blade=plan.Shapes.FirstOrDefault(s=>s.Name=="quillsedge_curled_blade");
            if (blade==null) return "quillsedge needs its curled gradient blade";
            foreach(int lod in new[]{0,1,2}) {
                var mesh=PlantCompiler.BuildShapeMesh(blade,lod);
                if(mesh.VertexCount<20 || mesh.Colors[1] < mesh.Colors[mesh.Colors.Count-3]+.1) return "quillsedge blade gradient or curvature lost at LOD";
            }
            if (!PlantCompiler.BuildShapeMesh(blade,0).UV2.Where((v,i)=>i%2==0).All(v=>v == -4)) return "quillsedge gradient must route to leaf shader";
            var reed=HerbGraphSpecies.Create(new FloraSpeciesDef { Id="ringreed", Height=1, RadiusAtMax=.5 })!.PlanFor(seed,1);
            // Scouring-rush horsetail: unbranched (no green leaves or side axes on the rods), ribbed
            // internodes, banded node sheaths, and both cone-tipped and rounded-tip rods.
            int Count(string name)=>reed.Items.Count(o=>o.IsFlat && reed.Shapes[o.ShapeId].Name==name);
            if (reed.Items.Any(o=>o.Type==OrganType.Leaf && reed.Shapes[o.ShapeId].Name!="ringreed_sheath_tooth")
                || Count("ringreed_ribbed_shaft")<50 || Count("ringreed_black_ring")<50 || Count("ringreed_ash_sheath")<50
                || Count("ringreed_strobilus")<3 || Count("ringreed_rounded_tip")<3) return "ringreed needs bare jointed horsetail rods with sheaths and rounded tips";
            var fungus=CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="reekhorn", Height=.22, RadiusAtMax=.09 })!.PlanFor(seed,1);
            var caps=fungus.Items.Where(o=>o.IsFlat && fungus.Shapes[o.ShapeId].Name=="reekhorn_cap").ToArray();
            var veils=fungus.Items.Where(o=>o.IsFlat && fungus.Shapes[o.ShapeId].Name=="reekhorn_veil").ToArray();
            if(caps.Length!=veils.Length || caps.Any(c=>!veils.Any(v=>v.Parent==c.Parent && (v.P0-c.P0).Length<.001))) return "reekhorn veil must share the head axis";
            foreach(double age in new[]{.7,.85,1.0}) {
                var plant=sedge.Create(seed,1); sedge.Grow(plant,age,1,null);
                foreach(int lod in new[]{0,1,2}) if(!PlantCompiler.Compile(plant,lod).Instances.Any(i=>plant.Shapes[i.ShapeId].Name=="quillsedge_curled_blade")) return "quillsedge blades must survive growth and LOD";
            }
        }
        return null;
    }
}
