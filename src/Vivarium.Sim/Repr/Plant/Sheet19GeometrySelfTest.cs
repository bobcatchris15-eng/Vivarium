using System;
using System.Linq;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Repr.Plant;

public sealed class Sheet19GeometrySelfTest : IReprSelfTest
{
    public string Name => "plant.sheet19Geometry";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 91, 804584441 })
        {
            var stream = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="streamribbon", Height=1, RadiusAtMax=1 })!;
            var sp = stream.PlanFor(seed, 1);
            var ribbon = sp.Shapes.FirstOrDefault(s => s.Name == "streamribbon_twining_leaf");
            if (ribbon == null || Math.Abs(ribbon.Twist) < 1) return "streamribbon needs twisted ribbon leaves";
            foreach (int lod in new[] { 0, 1, 2 })
            {
                var mesh = PlantCompiler.BuildShapeMesh(ribbon, lod);
                // Midrib must cross both sides of the original straight axis.
                var mid = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Position(i)).Where(p => Math.Abs(p.Z-.25)<.04 || Math.Abs(p.Z-.75)<.04).ToArray();
                if (mid.Length == 0 || !mid.Any(p => p.X > .1) || !mid.Any(p => p.X < -.1)) return "streamribbon blade does not weave across its axis";
                if (mesh.Positions.Any(v => !float.IsFinite(v)) || mesh.Normals.Any(v => !float.IsFinite(v))) return "non-finite ribbon mesh";
            }
            var swan = HerbGraphSpecies.Create(new FloraSpeciesDef { Id="swanfrond", Height=1, RadiusAtMax=1 })!;
            var sw = swan.PlanFor(seed, 1);
            var leaves = sw.Items.Where(o => o.IsBlade && sw.Shapes[o.ShapeId].Name == "swanfrond_shrub_leaf").ToArray();
            var pinnae = sw.Items.Where(o => o.IsBlade && sw.Shapes[o.ShapeId].Name == "swanfrond_pinna").ToArray();
            if (leaves.Length < 20 || pinnae.Length < 20) return "swanfrond needs dense shrub foliage and a pinnate frond";
            var parents = pinnae.Select(o => o.Parent).Distinct().Select(i => sw.Items[i]).ToArray();
            if (parents.Length != 3 || parents.Count(o => o.Parent >= 0 && parents.Contains(sw.Items[o.Parent])) != 2)
                return "swanfrond needs one continuous three-section frond";
            var tip = parents.OrderByDescending(o => o.Order).First();
            if (tip.T1.Y >= 0) return "swanfrond frond tip must arch downward";
            var moss = CryptogamGraphSpecies.Create(new FloraSpeciesDef { Id="stoneknot_moss", Height=1, RadiusAtMax=1 })!;
            var mp = moss.PlanFor(seed, 1);
            var mat = mp.Items.Where(o => o.IsFlat && mp.Shapes[o.ShapeId].Name == "stoneknot_moss_colony_understory").ToArray();
            if (mat.Length < 20 || mat.Min(o => o.Emerge) > .2) return "stoneknot needs an early underlying mat";
            foreach (double age in new[] { .35, .7, 1.0 }) foreach (int lod in new[] { 0, 1, 2 })
                foreach (var gen in new[] { stream, swan, moss })
                {
                    var plant = gen.Create(seed, 1); gen.Grow(plant, age, 1, null);
                    var assembly = PlantCompiler.Compile(plant, lod);
                    if (assembly.Wood.Positions.Any(v => !float.IsFinite(v))) return "non-finite sheet 19 assembly";
                }
        }
        return null;
    }
}
