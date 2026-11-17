using System;
using System.Linq;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Every visible runner grows directly into its cane through a shared mesh ring.</summary>
public sealed class GlowbriarJunctionSelfTest : IReprSelfTest
{
    public string Name => "plant.glowbriarJunction";
    public string? Run()
    {
        foreach (ulong seed in new ulong[] { 7, 31, 804584441 })
        foreach (double age in new[] { .25, .5, 1.0 })
        {
            var generator = new GlowbriarGenerator();
            var plant = generator.Create(seed, 1);
            generator.Grow(plant, age, 1, null);
            var pairs = plant.Organs.Select((o, i) => (o, i)).Where(x => x.o.Parent >= 0
                && (x.o.Flags & OrganFlags.Continuous) != 0
                && (plant.Organs[x.o.Parent].Flags & OrganFlags.Continuous) != 0).ToArray();
            if (age == 1 && pairs.Length != 6) return "mature plant must have six root-to-cane junctions";
            foreach (int detail in new[] { 0, 1, 2 })
            {
                var mesh = PlantCompiler.Compile(plant, detail).Wood;
                for (int v = 0; v < mesh.VertexCount; v++)
                    if (!mesh.Position(v).IsFinite || !mesh.NormalAt(v).IsFinite || mesh.NormalAt(v).LengthSq < .25)
                        return $"invalid wood vertex at seed {seed}, age {age}, detail {detail}";
                foreach (var (child, _) in pairs)
                {
                    var parent = plant.Organs[child.Parent];
                    var axis = child.StartTangent.Normalized();
                    int shared = 0;
                    for (int v = 0; v < mesh.VertexCount; v++)
                    {
                        var delta = mesh.Position(v) - child.Start;
                        if (Math.Abs(delta.Dot(axis)) > 1e-5 || Math.Abs(delta.Length - child.RadiusStart) > 1e-5) continue;
                        bool before = false, after = false;
                        for (int t = 0; t < mesh.Indices.Count; t += 3)
                        {
                            if (mesh.Indices[t] != v && mesh.Indices[t+1] != v && mesh.Indices[t+2] != v) continue;
                            for (int j = 0; j < 3; j++)
                            {
                                double side = (mesh.Position(mesh.Indices[t+j]) - child.Start).Dot(axis);
                                before |= side < -1e-8; after |= side > 1e-8;
                            }
                        }
                        if (before && after) shared++;
                    }
                    if (shared < 8) return $"root and cane do not share a skin at seed {seed}, age {age}, detail {detail}; shared={shared}, parent maturity={parent.Maturity}, child maturity={child.Maturity}, parent-end gap={(parent.End-child.Start).Length}, radii={parent.RadiusEnd}/{child.RadiusStart}, child len={(child.End-child.Start).Length}";
                }
            }
        }
        return null;
    }
}


