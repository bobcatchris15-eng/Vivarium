using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>All-species structural views through the same pilot-tree/island renderers used in gameplay.</summary>
public partial class PilotTreeOverhaulReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    private Camera3D _camera = null!;
    private int _imageCount;
    private readonly List<object> _cameraRows = new();
    private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try { await Run(); GD.Print("PILOT_OVERHAUL_REVIEW_OK"); GetTree().Quit(0); }
        catch (Exception ex) { GD.PrintErr("PILOT_OVERHAUL_REVIEW_FAILED " + ex); GetTree().Quit(2); }
    }

    private async Task Run()
    {
        if (Directory.Exists(OutDir) && Directory.GetFileSystemEntries(OutDir).Length != 0) throw new IOException("Refusing nonempty output");
        Directory.CreateDirectory(OutDir);
        GetWindow().Size = new Vector2I(1600, 1000);
        var rig = new EnvironmentRig { BenchmarkLightSeconds = 60 };
        AddChild(rig);
        rig.ApplyQuality(2);
        rig.CameraAttributes.AutoExposureEnabled = false;
        _camera = new Camera3D { Current = true, Fov = 44, Near = .015f, Far = 180, Attributes = rig.CameraAttributes };
        AddChild(_camera);
        var captures = new List<object>();
        foreach (var def in PilotTreeCatalog.All)
        {
            var desc = Content.PresetOrThrow("default");
            desc.PilotTreeId = def.Id;
            desc.PilotTreeCorner = 0;
            var world = VivariumWorld.Create(Content, desc, false, false);
            world.Clock.Paused = true;
            string before = WorldSerializer.Digest(world);
            var stage = new Node3D(); AddChild(stage);
            var island = new IslandRenderer(); stage.AddChild(island); island.Build(world); island.SetProcess(false);
            var pilot = new PilotTreeRenderer(); stage.AddChild(pilot); pilot.Build(world);
            await Frames(3);
            var tree = world.PilotTree!;
            int treeTriangles = pilot.Tree!.Mesh.GetFaces().Length / 3;
            int rootTriangles = pilot.Roots!.Mesh.GetFaces().Length / 3;
            if (treeTriangles + rootTriangles > 2600000)
                throw new InvalidOperationException("Pilot tree exceeded native triangle budget: " + def.Id);
            Aabb bounds = pilot.Roots!.Mesh.GetAabb();
            Aabb? foliageBounds = null;
            for (int surface = 0; surface < pilot.Tree.Mesh.GetSurfaceCount(); surface++)
            {
                using var arrays = pilot.Tree.Mesh.SurfaceGetArrays(surface);
                var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                for (int i = 0; i < positions.Length; i++)
                {
                    if (world.Domain.SignedDistance(new Vivarium.Sim.Core.Vec2(positions[i].X, positions[i].Z)) > .001) continue;
                    bounds = bounds.Expand(positions[i]);
                    if (colors[i].A < .2f)
                        foliageBounds = foliageBounds is { } box ? box.Expand(positions[i]) : new Aabb(positions[i], Vector3.Zero);
                }
            }
            foreach (var mesh in island.GetChildren().OfType<MeshInstance3D>()) bounds = bounds.Merge(mesh.Mesh.GetAabb());
            var inward = new Vector3((float)-tree.CornerPoint.X, 0, (float)-tree.CornerPoint.Z).Normalized();
            var side = inward.Cross(Vector3.Up).Normalized();
            Vector3 center = bounds.GetCenter();
            foreach (int sign in new[] { 1, -1 })
            {
                await Fit(bounds, center, (inward + side * (.50f * sign) + Vector3.Up * .25f).Normalized());
                await Save(def.Id, "whole-" + sign);
            }
            Vector3 root = Bridge.V(tree.CornerPoint, tree.BaseHeight + .5);
            _camera.Position = root + inward * 4.7f + side * 1.8f + Vector3.Up * 2.1f;
            _camera.LookAt(root + inward * 1.0f);
            await Save(def.Id, "roots-and-strata");
            // View an exterior face directly; the root close-up looks across the
            // top and cannot authenticate the cutaway horizon transitions.
            var edge = (world.Domain.Vertices[0] + world.Domain.Vertices[1]) * .5;
            Vector3 wall = Bridge.V(edge, world.SurfaceHeight(edge) - .85);
            Vector3 exterior = new Vector3((float)edge.X, 0, (float)edge.Z).Normalized();
            _camera.Position = wall + exterior * 5.6f + Vector3.Up * .7f;
            _camera.LookAt(wall);
            await Save(def.Id, "cutaway");
            Aabb crownBounds = foliageBounds ?? pilot.Tree.Mesh.GetAabb();
            await Fit(crownBounds, crownBounds.GetCenter(), (inward + side * .3f + Vector3.Up * .15f).Normalized());
            await Save(def.Id, "crown");
            if (Main.UserArgs.Contains("--pilot-structure-only"))
            {
                if (before != WorldSerializer.Digest(world)) throw new InvalidOperationException("Tree review changed world state: " + def.Id);
                captures.Add(new { id = def.Id, tree_triangles = treeTriangles, root_triangles = rootTriangles, world_unchanged = true });
                await VerifyRendererContracts(pilot, def.Id, (captures.Count - 1) % 6);
                RemoveChild(stage); stage.Free();
                await Frames(1);
                continue;
            }
            var material = (ShaderMaterial)pilot.Tree.Mesh.SurfaceGetMaterial(0);
            float vein = material.GetShaderParameter("u_leaf_vein_strength").AsSingle();
            float relief = material.GetShaderParameter("u_leaf_relief").AsSingle();
            float wood = material.GetShaderParameter("u_cutwood_structure").AsSingle();
            bool surfaceSweep = def.Id is "crowncoil" or "basinwarden";
            if (surfaceSweep)
            {
                foreach (var variant in new[] { ("zero", 0f, 0f), ("restrained", .30f, .18f), ("pronounced", 1f, .65f) })
                {
                    material.SetShaderParameter("u_leaf_vein_strength", variant.Item2);
                    material.SetShaderParameter("u_leaf_relief", variant.Item3);
                    await Save(def.Id, "crown-" + variant.Item1);
                }
                material.SetShaderParameter("u_leaf_vein_strength", vein);
                material.SetShaderParameter("u_leaf_relief", relief);
                await Save(def.Id, "crown-restored");
            }
            // Exterior macro of a deliberate cut plane, at mid-bole height.
            // This evaluates wood grain separately from the front bark/root view.
            Vector3 woodTarget = Bridge.V(tree.CornerPoint, tree.BaseHeight + tree.Def.TrunkHeight * .45);
            float woodDistance = Math.Max(3f, (float)tree.Def.TrunkRadius * 3.2f);
            _camera.Position = woodTarget - inward * woodDistance + side * woodDistance * .55f + Vector3.Up * .3f;
            _camera.LookAt(woodTarget);
            await Save(def.Id, "cutwood");
            if (surfaceSweep)
            {
                foreach (var variant in new[] { ("zero", 0f), ("restrained", .39f), ("pronounced", 1f) })
                {
                    material.SetShaderParameter("u_cutwood_structure", variant.Item2);
                    await Save(def.Id, "cutwood-" + variant.Item1);
                }
                material.SetShaderParameter("u_cutwood_structure", wood);
                await Save(def.Id, "cutwood-restored");
            }
            if (material.GetShaderParameter("u_leaf_vein_strength").AsSingle() != vein
                || material.GetShaderParameter("u_leaf_relief").AsSingle() != relief
                || material.GetShaderParameter("u_cutwood_structure").AsSingle() != wood)
                throw new InvalidOperationException("Pilot material sweep failed to restore production defaults");
            if (before != WorldSerializer.Digest(world)) throw new InvalidOperationException("Tree review changed world state: " + def.Id);
            captures.Add(new { id = def.Id, corner = tree.Corner, seed = world.Seed,
                tree_triangles = treeTriangles, root_triangles = rootTriangles,
                world_unchanged = true, flora_rendered = false, fauna_rendered = false,
                bounds_position = bounds.Position.ToString(), bounds_size = bounds.Size.ToString() });
            await VerifyRendererContracts(pilot, def.Id, (captures.Count - 1) % 6);
            RemoveChild(stage); stage.Free();
            await Frames(1);
        }
        // Exercise real material uploads for both shorter and longer custom
        // palettes. This guards against silently returning to four fixed tints.
        foreach (int count in new[] { 3, 5 })
        {
            var layers = Enumerable.Range(0, count).Select(i => new
            { id = "review_custom_" + i, name = "Review layer " + i, thickness = .45,
                color = new[] { .18 + i * .06, .22 + i * .035, .27 + i * .025 },
                color2 = new[] { .25, .28, .31 }, grain = .4, banding = .2 }).ToArray();
            var overlay = new OverlayContentSource(new DirectoryContentSource(ProjectSettings.GlobalizePath("res://content")))
                .Set("strata.json", JsonSerializer.Serialize(new { layers }));
            var content = ContentLoader.Load(overlay);
            var desc = content.PresetOrThrow("default"); desc.PilotTreeId = "none";
            var customWorld = VivariumWorld.Create(content, desc, false, false);
            var island = new IslandRenderer(); AddChild(island); island.Build(customWorld); island.SetProcess(false);
            var walls = island.GetChildren().OfType<MeshInstance3D>().Single(m => m.Name == "StrataWalls");
            var material = (ShaderMaterial)walls.Mesh.SurfaceGetMaterial(0);
            var texture = (Texture2D)material.GetShaderParameter("layer_palette").AsGodotObject();
            using var palette = texture.GetImage();
            if (material.GetShaderParameter("layer_count").AsInt32() != count || palette.GetWidth() != count)
                throw new InvalidOperationException("Custom strata palette count was lost");
            for (int i = 0; i < count; i++)
            {
                Color actual = palette.GetPixel(i, 0); var expected = layers[i].color;
                if (Math.Abs(actual.R - expected[0]) > .005 || Math.Abs(actual.G - expected[1]) > .005 || Math.Abs(actual.B - expected[2]) > .005)
                    throw new InvalidOperationException("Custom authored strata colour was lost");
            }
            _camera.Position = new Vector3(8, .4f, 5); _camera.LookAt(new Vector3(2, -.8f, 1.5f));
            await Save("custom-" + count, "cutaway");
            RemoveChild(island); island.Free();
            GD.Print("PILOT_CUSTOM_STRATA_OK layers=" + count);
        }
        File.WriteAllText(Path.Combine(OutDir, "metadata.json"), JsonSerializer.Serialize(new
        { production_renderers = new[] { "IslandRenderer", "PilotTreeRenderer", "EnvironmentRig" },
            scope = "all-species structural review; flora/fauna omitted; no populated-gameplay or performance acceptance",
            captures, image_count = _imageCount, light_seconds = 60, fixed_exposure = true,
            surface_sweep = Main.UserArgs.Contains("--pilot-structure-only") ? "disabled for structural review" : "Crowncoil/Basinwarden: leaf vein/relief zero(0,0), restrained(.30,.18), pronounced(1,.65); cutwood zero0/restrained.39/pronounced1; production defaults restored",
            renderer_contracts = "all forms rebuilt at distinct corners; remote terrain skips root upload, local terrain refreshes roots only; none removes both meshes",
            custom_palette_counts = new[] { 3, 5 }, custom_palette_uploads_match_authored_colors = true }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(OutDir, "cameras.json"), JsonSerializer.Serialize(new { scenes = _cameraRows }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task VerifyRendererContracts(PilotTreeRenderer renderer, string id, int corner)
    {
        // Separate disposable worlds keep every photographed world unchanged.
        var desc = Content.PresetOrThrow("default"); desc.PilotTreeId = id; desc.PilotTreeCorner = corner;
        var probe = VivariumWorld.Create(Content, desc, false, false);
        renderer.Build(probe); await Frames(1);
        if (probe.PilotTree!.Corner != corner || renderer.Tree == null || renderer.Roots == null)
            throw new InvalidOperationException("Alternate corner renderer replacement failed");
        var trunk = renderer.Tree.Mesh;
        using var before = renderer.Roots.Mesh.SurfaceGetArrays(0);
        var vertices = before[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var terrain = probe.Terrain;
        void Raise(double x, double z)
        {
            int ci = (int)Math.Floor((x - terrain.OriginX) / terrain.Step);
            int cj = (int)Math.Floor((z - terrain.OriginZ) / terrain.Step);
            terrain.BeginEdit();
            for (int j = cj; j <= cj + 1; j++)
                for (int i = ci; i <= ci + 1; i++) terrain.H[terrain.VertexIndex(i, j)] += .015;
            terrain.Touch();
        }
        Raise(0, 0); renderer._Process(.3);
        if (renderer.RootMeshRebuilds != 0 || renderer.UnchangedRootFootprints < 1)
            throw new InvalidOperationException("Remote terrain rebuilt pilot roots");
        var sample = probe.PilotTree.Roots.First().Samples[5];
        Raise(sample.Position.X, sample.Position.Z); renderer._Process(.3);
        using var after = renderer.Roots.Mesh.SurfaceGetArrays(0);
        if (renderer.RootMeshRebuilds != 1 || !ReferenceEquals(trunk, renderer.Tree.Mesh)
            || vertices.SequenceEqual(after[(int)Mesh.ArrayType.Vertex].AsVector3Array()))
            throw new InvalidOperationException("Local terrain failed to refresh roots independently");
        desc.PilotTreeId = "none";
        renderer.Build(VivariumWorld.Create(Content, desc, false, false));
        if (renderer.Tree != null || renderer.Roots != null) throw new InvalidOperationException("None left pilot meshes behind");
        await Frames(1);
        GD.Print("PILOT_RENDERER_CONTRACT_OK " + id + " corner=" + corner);
    }

    private async Task Fit(Aabb bounds, Vector3 target, Vector3 direction)
    {
        float distance = Math.Max(bounds.Size.Length() * .65f, 3f);
        for (int iteration = 0; iteration < 18; iteration++)
        {
            _camera.Position = target + direction * distance;
            _camera.LookAt(target, Vector3.Up);
            await Frames(1);
            bool inside = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.GetEndpoint(i);
                Vector2 pixel = _camera.UnprojectPosition(point);
                inside &= !_camera.IsPositionBehind(point) && pixel.X >= 70 && pixel.X <= 1530 && pixel.Y >= 55 && pixel.Y <= 945;
            }
            if (inside) return;
            distance *= 1.13f;
        }
        throw new InvalidOperationException("Could not frame pilot-tree bounds");
    }

    private async Task Save(string id, string view)
    {
        await Frames(8);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(OutDir, id + "-" + view + ".png");
        if (image.SavePng(path) != Error.Ok) throw new IOException("Could not save " + path);
        _imageCount++;
        Vector3 eye = _camera.Position, target = eye - _camera.GlobalBasis.Z;
        _cameraRows.Add(new { name = id + "-" + view, eye = new[] { eye.X, eye.Y, eye.Z },
            target = new[] { target.X, target.Y, target.Z }, fov = _camera.Fov });
        GD.Print("PILOT_OVERHAUL_CAPTURE " + id + " " + view);
    }
}
