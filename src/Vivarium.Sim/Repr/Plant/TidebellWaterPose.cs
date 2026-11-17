using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Extends only Tidebell flower scapes to keep their hanging bells above the live waterline.</summary>
public static class TidebellWaterPose
{
    public static void Apply(PlantDescriptor plant, double waterDepth, double scale)
    {
        if (!(waterDepth > 0) || !(scale > 0)) return;
        for (int i = 0; i < plant.Organs.Length; i++)
        {
            var flower = plant.Organs[i];
            if (flower.Type != OrganType.Flower || plant.Shapes[flower.ShapeId].Name != "tidebell_luminous_bell" || flower.Parent < 0) continue;
            var stalk = plant.Organs[flower.Parent];
            double lift = Math.Max(0, waterDepth / scale + .015 / scale - Math.Min(flower.Start.Y, flower.End.Y));
            if (lift <= 0) continue;
            var offset = Vec3.Up * lift;
            stalk.End += offset;
            stalk.StartTangent += offset;
            stalk.EndTangent += offset;
            plant.Organs[flower.Parent] = stalk;
            flower.Start += offset; flower.End += offset;
            plant.Organs[i] = flower;
        }
        PlanGenerator.Summarize(plant);
    }
}
