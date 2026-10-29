using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Shared upright growth transform for rendered plants and animal supports.</summary>
public static class FloraGeometryPlacement
{
    public static int MorphCount(string shape) => shape is "roundleaf" or "pairedleaf" or "herb" or "trifoliate" or "vine_clinglace" or "vine_spiralvine" or "vine_fenhook" ? 12 : 5;

    public static (Vec3 Up, double Radius, double Height) Upright(VivariumWorld world, ulong id, Vec2 position, double health, double radius, double height)
    {
        ulong hash = Rng.Mix(id, 0xF10);
        var p = position;
        double e = Math.Max(world.Grid.CellSize * .55, Math.Min(.35, Math.Max(radius, .05)));
        double gx = world.Fields.Light.Sample(p + new Vec2(e, 0)) - world.Fields.Light.Sample(p - new Vec2(e, 0));
        double gz = world.Fields.Light.Sample(p + new Vec2(0, e)) - world.Fields.Light.Sample(p - new Vec2(0, e));
        double moisture = world.Fields.Moisture.Sample(p);
        double stress = MathD.Clamp01((1 - health) * .7 + Math.Max(0, .32 - moisture) * .55);
        double angle = ((hash >> 24) % 6283) / 1000.0;
        var up = new Vec3(gx * .62 + Math.Cos(angle) * stress * .15, 1, gz * .62 + Math.Sin(angle) * stress * .15).Normalized();
        return (up, radius * (1 + stress * .035), height * (.93 + .07 * MathD.Clamp01(.55 * health + .45 * Math.Min(1, moisture / .45))));
    }

    public static Vec3 Transform(Vec3 vertex, Vec3 origin, Vec3 up, double yaw, double radius, double height)
    {
        vertex = new Vec3(vertex.X * radius, vertex.Y * height, vertex.Z * radius);
        vertex = new Vec3(Math.Cos(yaw) * vertex.X + Math.Sin(yaw) * vertex.Z, vertex.Y, -Math.Sin(yaw) * vertex.X + Math.Cos(yaw) * vertex.Z);
        var axis = Vec3.Up.Cross(up); double sine = axis.Length;
        if (sine > 1e-8)
        {
            axis /= sine;
            vertex = vertex * up.Y + axis.Cross(vertex) * sine + axis * (axis.Dot(vertex) * (1 - up.Y));
        }
        return origin + vertex;
    }
}
