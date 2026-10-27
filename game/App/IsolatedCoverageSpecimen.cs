using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Builds one authoritative Mat/Crust coverage species patch using the production renderer.</summary>
public static class IsolatedCoverageSpecimen
{
    private const ulong FixtureSeed = 0xC07E_A6E5UL;
    private const int PatchRadiusCells = 4;
    private const float FrameSize = 0.25f;

    /// <summary>
    /// Creates a disposable, flat world containing only the requested coverage species, then builds
    /// the regular CoverageRenderer beneath <paramref name="parent"/> and waits for its upload to settle.
    /// The caller owns the returned renderer through the parent node; freeing the parent frees it.
    /// </summary>
    public static async Task<(CoverageRenderer Renderer, Vector3 Target, float Size, object Stats)> BuildAsync(
        ContentLibrary content, string speciesId, Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(parent);
        if (string.IsNullOrWhiteSpace(speciesId)) throw new ArgumentException("A coverage species id is required.", nameof(speciesId));
        if (!parent.IsInsideTree()) throw new InvalidOperationException("The specimen parent must be inside the active scene tree before building coverage.");

        var species = content.FloraOrThrow(speciesId);
        if (!species.IsCoverageSpecies)
            throw new NotSupportedException(
                $"'{speciesId}' is not a Mat/Lichen coverage species. Aquatic coverage is not defined by FloraSpeciesDef and cannot be isolated by this helper.");

        var descriptor = content.PresetOrThrow("default");
        descriptor.Name = $"Disposable isolated coverage specimen: {speciesId}";
        descriptor.Seed = FixtureSeed;
        descriptor.Diameter = 5;
        descriptor.CellSize = 0.1;
        descriptor.PilotTreeId = "none";
        descriptor.GuardianId = "none";
        descriptor.Placement.Rocks = 0;
        descriptor.Placement.Logs = 0;
        descriptor.Placement.GravelPatches = 0;
        descriptor.Terrain.Features.Clear();
        descriptor.Terrain.Relief = 0;
        descriptor.Terrain.SlopeX = descriptor.Terrain.SlopeZ = 0;
        descriptor.Water.Springs.Clear();

        var world = VivariumWorld.Create(content, descriptor, populate: false, warmupSurfaceWater: false);
        world.Clock.Paused = true;
        world.Liquid.AsyncMode = false;
        world.Terrain.BeginEdit();
        Array.Fill(world.Terrain.H, 0.0);
        world.Terrain.Touch();
        world.Water.RefreshBed(world.Terrain);
        Array.Clear(world.Water.Depth);
        foreach (var layer in world.Coverage.All) layer.Clear();
        world.Props = new PropSet();

        // Keep the specimen in the middle of a single 32x32 coverage tile so boundary duplication
        // cannot affect the isolated patch or its renderer instance count.
        const double centerX = 0.33;
        const double centerZ = 0.33;
        var center = CoverageSpec.CellOf(new Vec2(centerX, centerZ));
        var seededLayers = new List<(CoverageLayer Layer, byte Occupant)>();
        if (species.Mat != null)
        {
            byte occupant = GetOccupantId(content, speciesId, CoverageLayerId.Mat);
            seededLayers.Add((world.Coverage.Mat, occupant));
        }
        if (species.Lichen != null)
        {
            byte occupant = GetOccupantId(content, speciesId, CoverageLayerId.Crust);
            seededLayers.Add((world.Coverage.Crust, occupant));
        }

        int seededCells = 0;
        foreach (var (layer, occupant) in seededLayers)
        {
            for (int dz = -PatchRadiusCells; dz <= PatchRadiusCells; dz++)
            for (int dx = -PatchRadiusCells; dx <= PatchRadiusCells; dx++)
            {
                if (dx * dx + dz * dz > PatchRadiusCells * PatchRadiusCells) continue;
                layer.SetCell(center.gx + dx, center.gz + dz, occupant,
                    b: 0.86f, w: 220, age: 80, dorm: 0, flags: 0, d2e: 1);
                seededCells++;
            }
        }

        string worldDigest = WorldSerializer.Digest(world);
        var renderer = new CoverageRenderer
        {
            Name = $"IsolatedCoverage_{speciesId}",
            Quality = 2,
            RebuildBudgetMs = 8,
        };
        parent.AddChild(renderer);
        renderer.Build(world);

        bool settled = false;
        for (int frame = 0; frame < 180; frame++)
        {
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (renderer.PendingRebuildCount == 0 && renderer.InstanceCount == seededLayers.Count)
            {
                settled = true;
                break;
            }
        }
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!settled)
            throw new InvalidOperationException(
                $"Coverage specimen '{speciesId}' did not settle: pending={renderer.PendingRebuildCount}, instances={renderer.InstanceCount}, expected={seededLayers.Count}.");

        string afterDigest = WorldSerializer.Digest(world);
        if (!string.Equals(worldDigest, afterDigest, StringComparison.Ordinal))
            throw new InvalidOperationException($"Coverage specimen rendering mutated the disposable world for '{speciesId}'.");

        double groundY = world.Terrain.Height(new Vec2(centerX, centerZ));
        var target = new Vector3((float)centerX, (float)groundY, (float)centerZ);
        var stats = new Dictionary<string, object>
        {
            ["species_id"] = speciesId,
            ["coverage_layers"] = seededLayers.ConvertAll(x => x.Layer.Id.ToString()),
            ["occupant_ids"] = seededLayers.ConvertAll(x => (int)x.Occupant),
            ["patch_radius_cells"] = PatchRadiusCells,
            ["seeded_cells"] = seededCells,
            ["coverage_tiles"] = seededLayers.Sum(x => x.Layer.TileCount),
            ["renderer_instances"] = renderer.InstanceCount,
            ["triangles_built"] = renderer.TrianglesBuilt,
            ["pending_rebuilds"] = renderer.PendingRebuildCount,
            ["world_digest"] = worldDigest,
            ["world_digest_stable_after_build"] = true,
            ["terrain"] = "flat; no rocks, logs, gravel, island mesh, water renderer, or other coverage occupants",
        };
        return (renderer, target, FrameSize, stats);
    }

    private static byte GetOccupantId(ContentLibrary content, string speciesId, CoverageLayerId layer)
    {
        int id = 0;
        foreach (var species in content.Flora)
        {
            bool belongs = layer == CoverageLayerId.Mat ? species.Mat != null : species.Lichen != null;
            if (!belongs) continue;
            id++;
            if (string.Equals(species.Id, speciesId, StringComparison.Ordinal))
            {
                if (id is < 1 or > byte.MaxValue)
                    throw new InvalidOperationException($"Coverage occupant id for '{speciesId}' is outside the byte range.");
                return (byte)id;
            }
        }
        throw new InvalidOperationException($"'{speciesId}' has no {layer} coverage occupant id in the content order used by CoverageRenderer.");
    }
}
