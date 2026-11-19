using System;
using System.IO;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Validates full-catalog forb leaf optics, 3D succulent solid leaves, and archetype growth invariants.</summary>
public sealed class PlantForbSystemSelfTest : IReprSelfTest
{
    public string Name => "plant.forbSystem";

    public string? Run()
    {
        string? contentRoot = FindContentRoot();
        if (contentRoot == null) return "unable to locate game/content directory";

        var lib = ContentLoader.Load(new DirectoryContentSource(contentRoot));
        if (lib.Warnings.Any(w => w.Contains("unknown class", StringComparison.Ordinal) || w.Contains("unknown archetype", StringComparison.Ordinal)))
            return "content warnings emitted on valid flora leaf/form blocks: " + string.Join("; ", lib.Warnings);

        // 1. All 61 herbaceous/forb/succulent/fern/graminoid species carry validated leaf optics & forb form data.
        foreach (var entry in HerbGraphSpecies.Species)
        {
            var sp = lib.FloraById(entry.Id);
            if (sp == null) return $"missing content definition for HerbGraphSpecies '{entry.Id}'";
            if (sp.Leaf == null) return $"missing visual.leaf block on '{entry.Id}'";
            if (sp.Form == null) return $"missing visual.form block on '{entry.Id}'";
            if (sp.Leaf.LaminaThicknessMm < 0.05 || sp.Leaf.LaminaThicknessMm > 12.0)
                return $"invalid lamina thickness {sp.Leaf.LaminaThicknessMm} mm on '{entry.Id}'";
            if (sp.Leaf.Cab < 0 || sp.Leaf.Cab > 80 || sp.Leaf.Car < 0 || sp.Leaf.Car > 20 || sp.Leaf.N < 1 || sp.Leaf.N > 3)
                return $"PROSPECT parameters out of range on '{entry.Id}'";
        }

        // 2. Succulents use thick volumetric optics & closed 3D SucculentBlade solid shapes across LOD 0/1/2.
        foreach (string id in new[] { "sunstone_rosette", "cragset", "brightknot" })
        {
            var sp = lib.FloraOrThrow(id);
            if (sp.Leaf!.Class != LeafOpticsClass.Thick) return $"expected thick optics class on succulent '{id}'";
            if (id is "sunstone_rosette" or "cragset")
            {
                var gen = HerbGraphSpecies.Create(sp)!;
                var plan = gen.PlanFor(804584441UL, 1);
                var shape = plan.Shapes.FirstOrDefault(s => s.SurfaceForm == OrganSurfaceForm.SucculentBlade);
                if (shape == null) return $"missing OrganSurfaceForm.SucculentBlade shape on '{id}'";
                int lastTris = int.MaxValue;
                foreach (int detail in new[] { 0, 1, 2 })
                {
                    var mesh = PlantCompiler.BuildShapeMesh(shape, detail);
                    if (mesh.TriangleCount == 0 || mesh.TriangleCount >= lastTris)
                        return $"SucculentBlade LOD {detail} triangle count {mesh.TriangleCount} not monotonically decreasing on '{id}'";
                    lastTris = mesh.TriangleCount;
                    for (int v = 0; v < mesh.VertexCount; v++)
                    {
                        if (!mesh.Position(v).IsFinite || !mesh.NormalAt(v).IsFinite || mesh.NormalAt(v).LengthSq < 0.25)
                            return $"invalid vertex/normal in SucculentBlade LOD {detail} on '{id}'";
                    }
                }
            }
        }

        // 3. Representative archetypes grow monotonically with finite grounded assemblies.
        foreach (string id in new[] { "butterglint", "mistviolet", "sunstone_rosette", "hushclover", "plumegrass" })
        {
            var sp = lib.FloraOrThrow(id);
            var gen = HerbGraphSpecies.Create(sp)!;
            var plant = gen.Create(42UL, 7UL);
            int prevOrgans = 0;
            foreach (double age in new[] { 0.25, 0.60, 1.0 })
            {
                gen.Grow(plant, age, 1.0, null);
                if (plant.Organs.Length == 0) return $"{id}: no organs at age {age}";
                if (plant.Organs.Length < prevOrgans) return $"{id}: organ count shrunk from {prevOrgans} to {plant.Organs.Length}";
                prevOrgans = plant.Organs.Length;
                var asm = PlantCompiler.Compile(plant, 0);
                if (asm.Wood.Positions.Any(x => !float.IsFinite(x))) return $"{id}: non-finite wood position";
                if (age >= 0.50 && asm.Instances.Length == 0) return $"{id}: no rendered organ instances at age {age}";
            }
            if (plant.LocalBounds.Min.Y > 0.015)
                return $"{id}: basal bounds min Y {plant.LocalBounds.Min.Y:F4} exceeds ground-contact bound";
        }

        return null;
    }

    private static string? FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "game", "content");
            if (File.Exists(Path.Combine(candidate, "index.json"))) return candidate;
        }
        return null;
    }
}
