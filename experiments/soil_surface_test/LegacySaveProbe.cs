using Vivarium.Sim.Content;
using Vivarium.Sim.World;
using Vivarium.Sim.Persistence;

var content = ContentLoader.Load(new DirectoryContentSource(Path.Combine(Directory.GetCurrentDirectory(), "game", "content")));
if (args[0] == "write")
{
    var d = new WorldDescriptor { Diameter = 5, CellSize = .23, Seed = 42, PilotTreeId = "none", GuardianId = "none" };
    VivariumWorld world;
    if (args.Contains("tree"))
    {
        d = content.PresetOrThrow("default").Clone(); d.Seed = 781004; d.GuardianId = "none";
        world = VivariumWorld.Create(content, d, populate: false, warmupSurfaceWater: false);
    }
    else world = VivariumWorld.CreateBaseline(content, d);
    world.Liquid.Enable();
    var save = SaveSystem.Save(world, args[1]);
    Console.WriteLine($"LEGACY_WRITE ok={save.Ok} assembly={typeof(VivariumWorld).Module.ModuleVersionId} digest={WorldSerializer.Digest(world)}");
    return save.Ok ? 0 : 1;
}
var load = SaveSystem.Load(content, args[1]);
Console.WriteLine($"LEGACY_LOAD ok={load.Ok} message={load.Message} assembly={typeof(VivariumWorld).Module.ModuleVersionId}");
return load.Ok ? 0 : 1;
