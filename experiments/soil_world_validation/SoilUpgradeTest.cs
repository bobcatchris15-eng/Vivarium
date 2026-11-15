using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

var root = Directory.GetCurrentDirectory();
var content = ContentLoader.Load(new DirectoryContentSource(Path.Combine(root, "game", "content")));
var path = Path.Combine(root, "build/soil-surface-20261115/before-soil-tree.vivsave");
var loaded = SaveSystem.Load(content, path);
if (!loaded.Ok || loaded.World == null) throw new Exception(loaded.Message);
var world = loaded.World;
if (world.PilotTree == null) throw new Exception("Missing actual pre-change tree fixture");
double trunkBase = world.PilotTree.BaseHeight;
var samples = world.Terrain.H.ToArray();
var position = world.Grid.DomainCells.Select(c => world.Grid.CellCenter(c)).First(p => world.GroundHeight(p) > world.Water.WaterTable + .05 && !world.PilotTree.BlocksDisc(p,.2));
world.Liquid.Add(position, .1, .0001);
double before = world.Liquid.Volume, substrateBefore = world.WorldSubstrate.Substrate.Water.Stored();
if (!SoilSurfaceUpgrade.Apply(world)) throw new Exception("Legacy surface did not upgrade");
if (!samples.SequenceEqual(world.Terrain.H)) throw new Exception("Upgrade changed macro/sculpt storage");
if (world.PilotTree.BaseHeight != trunkBase) throw new Exception("Upgrade moved existing trunk");
if (Math.Abs(world.Liquid.Volume - before) > 1e-9) throw new Exception("Mobile storage changed");
if (Math.Abs(world.WorldSubstrate.Substrate.Water.Stored() - substrateBefore) > 1e-8) throw new Exception("Subsurface storage changed");
var digest = WorldSerializer.Digest(world);
var savePath = Path.Combine(root, "build/soil-surface-20261115/upgraded-tree.vivsave");
var save = SaveSystem.Save(world, savePath); if (!save.Ok) throw new Exception(save.Message);
var reload = SaveSystem.Load(content, savePath); if (!reload.Ok || reload.World == null) throw new Exception(reload.Message);
if (WorldSerializer.Digest(reload.World) != digest || reload.World.PilotTree?.BaseHeight != trunkBase)
    throw new Exception("Upgraded saved world or trunk anchor changed after reload");
foreach(var p in new[]{position,world.PilotTree.Anchor,new Vec2(0,0)})
    if(Math.Abs(world.GroundHeight(p)-reload.World.GroundHeight(p))>1e-10) throw new Exception("Composed ground changed after reload");
if (SoilSurfaceUpgrade.Apply(reload.World)) throw new Exception("Upgrade was not idempotent");
Console.WriteLine($"SOIL_UPGRADE_OK macro_unchanged=true trunk={trunkBase:R} mobile={before:R}->{world.Liquid.Volume:R} subsurface={substrateBefore:R}->{world.WorldSubstrate.Substrate.Water.Stored():R} digest={digest}");
