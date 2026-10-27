using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Render-only leaf surface sampling and bounded petiole-root rotation accommodation.</summary>
public readonly record struct AccommodationBlade(
    AccommodationKey Key, Vec3 Root, Vec3 Tip, Vec3 WidthAxis, double HalfWidth,
    double Stiffness, double MaximumYawRadians = 0.65)
{
    public Vec3 Sample(double along, double across) =>
        Vec3.Lerp(Root, Tip, along) + WidthAxis.Normalized() * (across * HalfWidth * Math.Sin(Math.PI * along));
}

public readonly record struct AccommodationPose(double YawRadians, double Penetration, double RestCost);

public static class FoliageBladeAccommodation
{
    /// <summary>Rotate about the world-up axis at a fixed attachment. Does not move the root.</summary>
    public static Vec3 RotateAroundUp(Vec3 point, Vec3 root, double angle)
    {
        var d = point - root;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return root + new Vec3(c * d.X - s * d.Z, d.Y, s * d.X + c * d.Z);
    }

    public static AccommodationBlade Rotate(AccommodationBlade blade, double yaw) =>
        blade with
        {
            Tip = RotateAroundUp(blade.Tip, blade.Root, yaw),
            WidthAxis = RotateAroundUp(blade.Root + blade.WidthAxis, blade.Root, yaw) - blade.Root
        };

    /// <summary>Sample interior, edges and midrib against nearby stem capsules.</summary>
    public static double StemPenetration(AccommodationBlade blade, IReadOnlyList<AccommodationCapsule> obstacles, double clearance = 0.004)
    {
        double penalty = 0;
        for (int i = 1; i <= 8; i++)
            for (int j = -2; j <= 2; j++)
            {
                var point = blade.Sample(i / 9.0, j / 2.0);
                foreach (var obstacle in obstacles)
                {
                    if (obstacle.Key == blade.Key) continue;
                    double depth = clearance - FoliageOccupancyIndex.SignedDistance(point, obstacle);
                    if (depth > 0) penalty += depth * depth;
                }
            }
        return penalty;
    }

    /// <summary>
    /// Deterministic finite pose search; root remains attached and rest pose is preferred.
    /// Returns best feasible or least-cost yaw; this is the first rigid-blade accommodation
    /// primitive, not the final curved petiole/lamina solver.
    /// </summary>
    public static AccommodationPose SolveYaw(AccommodationBlade blade, FoliageOccupancyIndex occupancy,
        double clearance = 0.004, int stepsEachSide = 8)
    {
        if (stepsEachSide < 1 || stepsEachSide > 64) throw new ArgumentOutOfRangeException(nameof(stepsEachSide));
        if (!double.IsFinite(blade.HalfWidth) || blade.HalfWidth < 0 || !double.IsFinite(blade.MaximumYawRadians) ||
            blade.MaximumYawRadians < 0 || !blade.Root.IsFinite || !blade.Tip.IsFinite || !blade.WidthAxis.IsFinite)
            throw new ArgumentException("Invalid blade", nameof(blade));
        double reach = (blade.Tip - blade.Root).Length + blade.HalfWidth + clearance;
        var extent = new Vec3(reach, reach, reach);
        var obstacles = occupancy.Query(blade.Root - extent, blade.Root + extent);
        AccommodationPose best = default;
        double bestScore = double.PositiveInfinity;
        // Test rest pose first; equal scores retain smaller displacement and stable sign order.
        for (int i = 0; i <= 2 * stepsEachSide; i++)
        {
            int signedStep = i == 0 ? 0 : (i % 2 == 1 ? (i + 1) / 2 : -i / 2);
            double yaw = blade.MaximumYawRadians * signedStep / stepsEachSide;
            double penetration = StemPenetration(Rotate(blade, yaw), obstacles, clearance);
            double rest = yaw * yaw;
            double score = penetration * 10000 + rest * 0.0001;
            if (score < bestScore)
            {
                bestScore = score;
                best = new AccommodationPose(yaw, penetration, rest);
            }
        }
        return best;
    }
}
