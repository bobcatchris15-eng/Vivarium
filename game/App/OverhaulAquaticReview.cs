using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Camera;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Disposable, explicitly authored aquatic renderer fixture; it never loads or saves a player world.</summary>
public partial class OverhaulAquaticReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";

    private const ulong FixtureSeed = 0xA91A71C5UL;
    private readonly List<object> _captures = new();
    private VivariumWorld _world = null!;
    private CameraRig _cameraRig = null!;
    private CoverageRenderer _coverage = null!;
    private Dictionary<CoverageLayerId, (int Nodes, int Vertices, int Triangles)> _layerStats = new();
    private Dictionary<string, string> _initialSourceHashes = new(StringComparer.Ordinal);

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            GetWindow().Size = new Vector2I(1280, 800);
            _world = CreateFixtureWorld();
            SeedExplicitFixture(_world);
            string fixtureDigest = WorldSerializer.Digest(_world);
            _initialSourceHashes = ReadSourceHashes();

            var lighting = new EnvironmentRig { Name = "FixtureEnvironment" };
            AddChild(lighting);
            lighting.ConfigureHabitat(_world);

            _cameraRig = new CameraRig { Name = "FixtureCamera", World = _world, KeyboardBlocked = true };
            AddChild(_cameraRig);
            var island = new IslandRenderer { Name = "FixtureIsland" };
            AddChild(island);
            island.Build(_world);
            var water = new WaterRenderer { Name = "FixtureWater", Lighting = lighting };
            AddChild(water);
            water.Build(_world);
            _coverage = new CoverageRenderer { Name = "FixtureCoverage", Quality = 2, RebuildBudgetMs = 8 };
            AddChild(_coverage);
            _coverage.Camera = _cameraRig.Cam;
            _coverage.Build(_world);
            _cameraRig.SetProcessUnhandledInput(false);

            // All three distinct coverage tiles must finish the real async build/commit path.
            for (int frame = 0; frame < 180 && !CoverageReady(); frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                AssertWorldStable(fixtureDigest, $"renderer-settling-frame-{frame + 1}");
            }
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!CoverageReady()) throw new InvalidOperationException($"Aquatic coverage did not settle: pending={_coverage.PendingRebuildCount}, nodes={_coverage.InstanceCount}.");
            AssertWorldStable(fixtureDigest, "renderer-settling");
            _layerStats = InspectUploadedLayers();

            // The fixture is intentionally authored. Its digest baseline begins after fixture setup and before renderer settling/captures.
            Vector3[][] views =
            {
                new[] { new Vector3(-.38f, .17f, .24f), new Vector3(-.38f, .015f, 0f) },
                new[] { new Vector3(0f, .17f, .24f), new Vector3(0f, .015f, 0f) },
                new[] { new Vector3(.38f, .17f, .24f), new Vector3(.38f, .015f, 0f) },
                new[] { new Vector3(0f, .90f, 1.10f), new Vector3(0f, .015f, 0f) },
            };
            string[] names = { "macro-surface-float", "macro-algae-bed", "macro-algae-float", "combined-pond" };
            for (int i = 0; i < names.Length; i++)
            {
                _cameraRig.LookAtPoint(views[i][0], views[i][1]);
                await Capture(names[i], 8, fixtureDigest);
            }

            // Small camera parallax sequence checks that all three real renderer layers remain composed in motion.
            for (int i = 0; i < 8; i++)
            {
                float phase = i / 7f;
                var eye = new Vector3(Mathf.Lerp(-.10f, .10f, phase), .31f, 0.48f);
                _cameraRig.LookAtPoint(eye, new Vector3(0, .015f, 0));
                await Capture($"motion-{i + 1:00}", 2, fixtureDigest);
            }

            WriteReport(fixtureDigest, island, water);
            GD.Print($"OVERHAUL_AQUATIC_REVIEW_OK {Path.GetFullPath(OutDir)}");
            GetTree().Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("OVERHAUL_AQUATIC_REVIEW_FAILED " + ex);
            GetTree().Quit(2);
        }
    }

    private VivariumWorld CreateFixtureWorld()
    {
        var descriptor = Content.PresetOrThrow("default").Clone();
        descriptor.Name = "Disposable authored aquatic render fixture";
        descriptor.Seed = FixtureSeed;
        descriptor.Diameter = 5;
        descriptor.CellSize = 0.1;
        descriptor.PilotTreeId = "none";
        descriptor.GuardianId = "none";
        descriptor.Placement.Rocks = descriptor.Placement.Logs = descriptor.Placement.GravelPatches = 0;
        descriptor.Terrain.Features.Clear();
        descriptor.Water.Springs.Clear();
        descriptor.Water.WaterTable = 0.045;
        descriptor.Water.Evaporation = descriptor.Water.Infiltration = 0;
        var world = VivariumWorld.Create(Content, descriptor, populate: false, warmupSurfaceWater: false);
        world.Clock.Paused = true;
        world.Liquid.AsyncMode = false;
        world.Terrain.BeginEdit();
        Array.Fill(world.Terrain.H, 0.0);
        world.Terrain.Touch();
        world.Water.RefreshBed(world.Terrain);
        Array.Clear(world.Water.Depth);
        foreach (var layer in world.Coverage.All) layer.Clear();
        world.Fields.Biofilm.Fill(0);
        return world;
    }

    private static void SeedExplicitFixture(VivariumWorld world)
    {
        var patches = new[]
        {
            (Layer: world.Coverage.SurfaceFloat, Id: CoverageLayerId.SurfaceFloat, X: -.38, Z: 0.0, Occupant: SurfaceFloatRules.OccupantId),
            (Layer: world.Coverage.AlgaeBed, Id: CoverageLayerId.AlgaeBed, X: 0.0, Z: 0.0, Occupant: AlgaeRules.OccupantId),
            (Layer: world.Coverage.AlgaeFloat, Id: CoverageLayerId.AlgaeFloat, X: .38, Z: 0.0, Occupant: AlgaeRules.OccupantId),
        };
        foreach (var patch in patches)
        {
            var center = CoverageSpec.CellOf(new Vec2(patch.X, patch.Z));
            for (int dz = -5; dz <= 5; dz++)
            for (int dx = -5; dx <= 5; dx++)
            {
                if (dx * dx + dz * dz > 25) continue;
                int gx = center.gx + dx, gz = center.gz + dz;
                float biomass = .48f + .30f * (1f - (float)Math.Sqrt(dx * dx + dz * dz) / 5f);
                patch.Layer.SetCell(gx, gz, patch.Occupant, biomass, 220, 80, 0, 0, 0);
                if (patch.Id == CoverageLayerId.AlgaeBed)
                {
                    double x = (gx + .5) * CoverageSpec.CellSize, z = (gz + .5) * CoverageSpec.CellSize;
                    int fieldCell = world.Grid.NearestDomainCell(new Vec2(x, z));
                    if (fieldCell >= 0) world.Fields.Biofilm[fieldCell] = Math.Max(world.Fields.Biofilm[fieldCell], world.Fields.Biofilm.Max * .72);
                }
            }
        }
        if (world.Coverage.SurfaceFloat.TileCount == 0 || world.Coverage.AlgaeBed.TileCount == 0 || world.Coverage.AlgaeFloat.TileCount == 0)
            throw new InvalidOperationException("Fixture failed to author all three aquatic coverage layers.");
        if (world.Fields.Biofilm.Total() <= 0 || !world.Fields.Biofilm.AllFinite())
            throw new InvalidOperationException("Fixture biofilm field is empty or nonfinite.");
    }

    private bool CoverageReady() => _coverage.PendingRebuildCount == 0 && _coverage.InstanceCount >= 3;

    private Dictionary<CoverageLayerId, (int Nodes, int Vertices, int Triangles)> InspectUploadedLayers()
    {
        var result = new Dictionary<CoverageLayerId, (int Nodes, int Vertices, int Triangles)>();
        foreach (var layerId in new[] { CoverageLayerId.SurfaceFloat, CoverageLayerId.AlgaeBed, CoverageLayerId.AlgaeFloat })
        {
            var nodes = _coverage.GetChildren().OfType<MeshInstance3D>()
                .Where(n => n.Name.ToString().StartsWith($"Coverage_{layerId}_", StringComparison.Ordinal)).ToArray();
            int verticesTotal = 0, trianglesTotal = 0;
            foreach (var node in nodes)
            {
                if (node.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() == 0)
                    throw new InvalidOperationException($"{node.Name}: no uploaded ArrayMesh surface.");
                using var arrays = mesh.SurfaceGetArrays(0);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var indexVariant = arrays[(int)Mesh.ArrayType.Index];
                int[] indices = indexVariant.VariantType == Variant.Type.PackedInt32Array
                    ? indexVariant.AsInt32Array() : Enumerable.Range(0, vertices.Length).ToArray();
                if (vertices.Length == 0 || vertices.Length != normals.Length || indices.Length < 3 || indices.Length % 3 != 0)
                    throw new InvalidOperationException($"{node.Name}: malformed uploaded geometry arrays.");
                for (int i = 0; i < vertices.Length; i++)
                    if (!vertices[i].IsFinite() || !normals[i].IsFinite() || normals[i].LengthSquared() < .5f)
                        throw new InvalidOperationException($"{node.Name}: nonfinite or degenerate vertex/normal at {i}.");
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                        throw new InvalidOperationException($"{node.Name}: index outside vertex range.");
                    var face = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
                    var outward = normals[a] + normals[b] + normals[c];
                    if (!face.IsFinite() || face.Dot(outward) > 1e-8f)
                        throw new InvalidOperationException($"{node.Name}: triangle winding does not face clockwise against uploaded normals.");
                }
                verticesTotal += vertices.Length;
                trianglesTotal += indices.Length / 3;
            }
            if (nodes.Length == 0 || verticesTotal == 0 || trianglesTotal == 0)
                throw new InvalidOperationException($"{layerId} produced no uploaded aquatic geometry.");
            result.Add(layerId, (nodes.Length, verticesTotal, trianglesTotal));
        }
        return result;
    }

    private async Task Capture(string name, int settleFrames, string fixtureDigest)
    {
        for (int frame = 0; frame < settleFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            AssertWorldStable(fixtureDigest, $"{name}-settle-frame-{frame + 1}");
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        AssertWorldStable(fixtureDigest, name + "-post-draw");
        using var image = GetViewport().GetTexture().GetImage();
        string path = Path.GetFullPath(Path.Combine(OutDir, name + ".png"));
        Error error = image.SavePng(path);
        if (error != Error.Ok) throw new IOException($"Failed to save {path}: {error}.");
        _captures.Add(new { file = Path.GetFileName(path), camera = _cameraRig.Cam.GlobalPosition.ToString(), settleFrames });
    }

    private void AssertWorldStable(string expected, string point)
    {
        string actual = WorldSerializer.Digest(_world);
        if (!StringComparer.Ordinal.Equals(expected, actual))
            throw new InvalidOperationException($"Fixture world digest changed during render/capture at {point}: {expected} -> {actual}.");
    }

    private void WriteReport(string digest, IslandRenderer island, WaterRenderer water)
    {
        var finalSourceHashes = ReadSourceHashes();
        if (_initialSourceHashes.Count != finalSourceHashes.Count || _initialSourceHashes.Any(kv => !finalSourceHashes.TryGetValue(kv.Key, out var hash) || !StringComparer.Ordinal.Equals(hash, kv.Value)))
            throw new InvalidOperationException("Aquatic review source changed during fixture capture.");
        var proof = finalSourceHashes.Select(kv => new { path = kv.Key, sha256 = kv.Value }).ToArray();
        File.WriteAllText(Path.Combine(OutDir, "source-proof.json"), JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
        var layerReport = _layerStats.Select(kv => new
        {
            layer = kv.Key.ToString(), nodes = kv.Value.Nodes, vertices = kv.Value.Vertices, triangles = kv.Value.Triangles,
            authoredCoverageCells = _world.Coverage.ById(kv.Key).Tiles.Sum(t => t.Occ.Count(o => o != 0)),
            origin = "explicit disposable fixture placement; not six-day natural establishment"
        }).ToArray();
        File.WriteAllText(Path.Combine(OutDir, "report.json"), JsonSerializer.Serialize(new
        {
            fixture = "flat-pothole-aquatic-render-review-v1", seed = FixtureSeed, presetBase = "default",
            savedPlayerWorldLoaded = false, worldDigest = digest, worldDigestStableAfterRendererAndCaptures = true,
            setup = "flat terrain, no props or springs, water table at 0.045 m; three separately authored authoritative coverage patches and biofilm",
            quality = 2, rendering = new { islandTriangles = island.TriangleCount, pondTriangles = water.TriangleCount, coverageInstances = _coverage.InstanceCount },
            layers = layerReport, captures = _captures, sourceProof = "source-proof.json"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, string> ReadSourceHashes()
    {
        var paths = new[]
        {
            "game/App/OverhaulAquaticReview.cs", "game/App/Main.cs", "game/Render/CoverageRenderer.cs",
            "game/Render/CoverageRenderer.Aquatic.cs", "game/Render/IslandRenderer.cs", "game/Render/WaterRenderer.cs",
            "game/Shaders/coverage_aquatic.gdshader", "game/Shaders/terrain.gdshader", "game/Shaders/water_stream.gdshader"
        };
        return paths.ToDictionary(p => p, p =>
        {
            var localPath = ProjectSettings.GlobalizePath("res://" + p.Substring("game/".Length).Replace('\\', '/'));
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(localPath)));
        }, StringComparer.Ordinal);
    }
}
