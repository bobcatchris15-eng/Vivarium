using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

/// <summary>Application-time upgrade after saved state integrity has been verified. Macro/sculpt storage stays unchanged.</summary>
public static class SoilSurfaceUpgrade
{
    public const double DefaultAmplitude = .020;
    public static void ConfigureNew(WorldDescriptor descriptor)
    {
        if (descriptor.Terrain.SoilSurfaceVersion != 0) return;
        descriptor.Terrain.SoilSurfaceVersion = SoilSurface.CurrentVersion;
        descriptor.Terrain.SoilSurfaceAmplitude = DefaultAmplitude;
    }
    public static bool Apply(VivariumWorld world)
    {
        if (world.Terrain.SoilSurfaceVersion != 0) return false;
        world.Liquid.Enable();
        _ = world.Liquid.CaptureState(); // Join prior work before replacing its immutable support.
        double mobile = world.Liquid.Volume;
        double substrate = world.WorldSubstrate.Substrate.Water.Stored();
        if (world.PilotTree != null) world.Descriptor.PilotTreeBaseHeight ??= world.PilotTree.BaseHeight;
        world.Descriptor.Terrain.SoilSurfaceVersion = SoilSurface.CurrentVersion;
        world.Descriptor.Terrain.SoilSurfaceAmplitude = DefaultAmplitude;
        world.Terrain.UpgradeSoil(DefaultAmplitude);
        world.Water.RefreshBed(world.Terrain);
        world.Placement.Reseat(Vec2.Zero, world.Domain.Radius * 2);
        world.WorldSubstrate.EnsureTerrainSynced();
        world.Liquid.OnTerrainChanged();
        world.Fields.MarkLightStale();
        foreach (var animal in world.Fauna.Items)
        {
            if (animal.Grabbed) continue;
            var species = world.Content.FaunaOrThrow(animal.SpeciesId);
            animal.Y = world.FaunaSystem.RestingY(species, animal.PositionXZ, animal.Pitch);
        }
        if (Math.Abs(world.Liquid.Volume - mobile) > 1e-9 ||
            Math.Abs(world.WorldSubstrate.Substrate.Water.Stored() - substrate) > 1e-8)
            throw new InvalidOperationException("Soil upgrade changed stored water");
        return true;
    }
}
