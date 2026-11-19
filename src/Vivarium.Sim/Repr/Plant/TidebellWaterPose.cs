using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Extends only Tidebell flower petioles to keep their hanging bells above the live waterline.</summary>
public static class TidebellWaterPose
{
    public static void Apply(PlantDescriptor plant, double waterDepth, double scale)
    {
        if (!(waterDepth > 0) || !(scale > 0)) return;
        for (int i = 0; i < plant.Organs.Length; i++)
        {
            var flower = plant.Organs[i];
            if (flower.Type != OrganType.Flower || !plant.Shapes[flower.ShapeId].Name.StartsWith("tidebell_luminous_bell", StringComparison.Ordinal) || flower.Parent < 0) continue;
            double lift = Math.Max(0, waterDepth / scale + .015 / scale - Math.Min(flower.Start.Y, flower.End.Y));
            if (lift <= 0) continue;
            var offset = Vec3.Up * lift;
            // Walk the visible petiole chain (rising lower stem + arched pedicel) down
            // to its rooted base. Stretch the rooted segment and translate the rest so
            // deep water lengthens the whole petiole instead of one scape segment.
            var chain = new List<int>();
            int cur = flower.Parent;
            while (cur >= 0)
            {
                var a = plant.Organs[cur];
                if (a.Parent < 0 || a.Order < 1 || a.Tissue != Tissue.Green || (a.Flags & OrganFlags.Hidden) != 0) break;
                chain.Add(cur);
                cur = a.Parent;
            }
            if (chain.Count == 0) continue;
            int rooted = chain[chain.Count - 1];
            var root = plant.Organs[rooted];
            root.End += offset;
            root.EndTangent += offset;
            plant.Organs[rooted] = root;
            for (int c = 0; c < chain.Count - 1; c++)
            {
                var seg = plant.Organs[chain[c]];
                seg.Start += offset; seg.End += offset;
                plant.Organs[chain[c]] = seg;
            }
            flower.Start += offset; flower.End += offset;
            plant.Organs[i] = flower;
        }
        PlanGenerator.Summarize(plant);
    }
}
