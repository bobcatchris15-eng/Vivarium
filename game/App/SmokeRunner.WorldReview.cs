using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    // Diagnostic photographs of a real initial GameSession. No biological warmup or performance gate.
    private async Task WorldReviewAsync()
    {
        W.Clock.Paused = true;
        int quality = int.TryParse(Main.ArgAfter("--review-quality"), out int requestedQuality) ? Math.Clamp(requestedQuality, 0, 2) : 2;
        Session.Settings.Quality = quality;
        Session.ApplySettings();
        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false;
        double lightSeconds = double.TryParse(Main.ArgAfter("--review-light-seconds"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double requestedLight) && double.IsFinite(requestedLight) ? requestedLight : 420;
        Session.EnvRig.BenchmarkLightSeconds = lightSeconds;
        bool forestReview = Main.UserArgs.Contains("--forest-review");
        if (forestReview) Session.EnvRig.CameraAttributes.AutoExposureEnabled = false;
        if (forestReview) await PrepareWaterImpactReview();
        // Engine time is paused only in this disposable review process. Record the limitation:
        // the exact pre-pause global shader TIME is not exposed, so no identical phase claim.
        Engine.TimeScale = 0;
        await Frames(60);
        while (!Session.Flora.PopulationReady)
        {
            Session.Flora._Process(.5);
            await Frames(1);
        }
        // Lock available opt-in material clocks. Other raw TIME users and initial CPU animal
        // poses remain uncontrolled; this diagnostic must not claim whole-frame repeatability.
        int lockedMaterials = LockReviewFoliage(Session);
        if (Main.UserArgs.Contains("--habitat-light-production-check"))
        {
            await HabitatLightValidationAsync();
            return;
        }
        if (Main.UserArgs.Contains("--habitat-light-sweep"))
        {
            await HabitatLightReviewAsync();
            return;
        }
        if (forestReview)
        {
            // Authenticate the material and UV region labels after their real Godot upload.
            var materials = Session.Fauna.GetChildren().OfType<MultiMeshInstance3D>()
                .Where(n => n.Name.ToString().StartsWith("Fauna_ghostbristle_0_") || n.Name.ToString().StartsWith("Fauna_marbleback_0_") || n.Name.ToString().StartsWith("Fauna_moonveil_0_"))
                .Select(n =>
                {
                    var mesh = n.Multimesh.Mesh;
                    var material = mesh.SurfaceGetMaterial(0) as ShaderMaterial;
                    var uv = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
                    int family = material?.Shader.Code.Contains("uniform int surface_family") == true ? material.GetShaderParameter("surface_family").AsInt32() : 0;
                    return new { name = n.Name.ToString(), surface_family = family,
                        region_min = uv.Min(v => v.X), region_max = uv.Max(v => v.X),
                        dorsal_vertices = uv.Count(v => v.X > .5f && v.X < 1.5f && v.Y < .5f) };
                }).ToArray();
            File.WriteAllText(Path.Combine(OutDir, "fauna_materials.json"), JsonSerializer.Serialize(materials, new JsonSerializerOptions { WriteIndented=true }));
            // Inspect the mesh Godot actually uploaded; a successful shader compile alone
            // would not expose inward normals or reversed thin-sheet faces.
            var litter = Session.SoilDetail.GetChildren().OfType<MultiMeshInstance3D>()
                .Single(n => n.Name == "LeafLitter");
            using var litterArrays = litter.Multimesh.Mesh.SurfaceGetArrays(0);
            var vertices = litterArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = litterArrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var indexArray = litterArrays[(int)Mesh.ArrayType.Index];
            var indices = indexArray.VariantType == Variant.Type.PackedInt32Array
                ? indexArray.AsInt32Array() : Enumerable.Range(0, vertices.Length).ToArray();
            bool finite = vertices.Concat(normals).All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z));
            double minAreaSquared = double.MaxValue, maxWindingDot = double.MinValue;
            for (int t = 0; t < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                var cross = (vertices[b] - vertices[a]).Cross(vertices[c] - vertices[a]);
                minAreaSquared = Math.Min(minAreaSquared, cross.LengthSquared());
                maxWindingDot = Math.Max(maxWindingDot, cross.Dot(normals[a] + normals[b] + normals[c]));
            }
            bool validLitter = finite && indices.Length > 0 && minAreaSquared > 1e-18 && maxWindingDot < 0
                && normals.All(n => Math.Abs(n.Length() - 1) < .001);
            Check("uploaded litter has finite geometry, unit normals and clockwise outward faces", validLitter);
            File.WriteAllText(Path.Combine(OutDir, "litter_geometry.json"), JsonSerializer.Serialize(new
            {
                passed = validLitter, vertices = vertices.Length, triangles = indices.Length / 3,
                finite, minimum_cross_length_squared = minAreaSquared, maximum_winding_dot = maxWindingDot,
                material = (litter.MaterialOverride as ShaderMaterial)?.Shader.ResourcePath,
            }, new JsonSerializerOptions { WriteIndented=true }));
        }
        string before = WorldSerializer.Digest(W);
        if (forestReview)
        {
            // Ambient cover is persisted presentation data. Isolate it when comparing
            // visual occupancy changes, while authenticating every other saved field.
            var payloads = WorldSerializer.Serialize(W);
            var fields = System.Text.Json.Nodes.JsonNode.Parse(payloads["fields"])!.AsObject();
            if (!fields.Remove("AmbientCover")) throw new InvalidOperationException("Missing ambient-cover comparison field.");
            payloads["fields"] = JsonSerializer.SerializeToUtf8Bytes(fields);
            File.WriteAllText(Path.Combine(OutDir, "comparison_state.json"), JsonSerializer.Serialize(new
            {
                digest_excluding_only_ambient_cover = WorldSerializer.DigestOf(payloads),
                scope = "All canonical saved payloads, with only Fields.AmbientCover removed for visual-field comparisons",
                ambient_cells = W.Grid.DomainCells.Count(i => W.AmbientGroundCover.Cover[i] >= .02),
                ambient_total = W.Grid.DomainCells.Sum(i => W.AmbientGroundCover.Cover[i]),
            }, new JsonSerializerOptions { WriteIndented=true }));
        }
        var rows = new List<object>();
        var scenes = forestReview ? BuildForestReviewScenes() : BuildReferenceScenes();
        string? cameraReference = Main.ArgAfter("--review-camera-reference");
        if (cameraReference != null)
        {
            using var cameras = JsonDocument.Parse(File.ReadAllText(cameraReference));
            scenes = cameras.RootElement.GetProperty("scenes").EnumerateArray().Select(row =>
            {
                if (row.TryGetProperty("pilot_view", out var pilotView))
                    return PilotReviewScene(row.GetProperty("name").GetString()!, pilotView.GetString()!);
                Vector3 V(string key) { var a = row.GetProperty(key); return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()); }
                return new RefScene(row.GetProperty("name").GetString()!, "replayed", V("eye"), V("target"));
            }).ToList();
        }
        string? selected = Main.ArgAfter("--review-scenes");
        if (selected != null) scenes = scenes.Where(scene => selected.Split(',').Contains(scene.Name)).ToList();
        if (scenes.Count == 0) throw new InvalidOperationException("No review scenes selected.");
        foreach (var scene in scenes)
        {
            Session.CameraRig.LookAtPoint(scene.Eye, scene.Target);
            // Ambient cover refreshes on a presentation cadence. A paused review must explicitly
            // request the camera-dependent refresh; delta=0 alone leaves the initial camera's cover.
            Session.AmbientGround._Process(1.1);
            Session.SoilDetail._Process(1.1);
            Session.Flora._Process(0.5);
            await Frames(24);
            Session.EnvRig.SetUnderwater(Session.CameraRig.Medium.Underwater || scene.Name == "forest_void_underwater");
            Session.EnvRig._Process(.3);
            SetWaterOpticsDebug(scene.Name);
            await Frames(2);
            await Screenshot(scene.Name);
            var cover = Session.AmbientGround.GetChildren().OfType<MultiMeshInstance3D>().Select(mm => new
            {
                name = mm.Name.ToString(), instances = mm.Multimesh.InstanceCount,
                triangles_per_instance = mm.Multimesh.Mesh.GetFaces().Length / 3,
            }).ToArray();
            var actualEye = Session.CameraRig.GlobalPosition;
            rows.Add(new { name = scene.Name, eye = new[] {scene.Eye.X,scene.Eye.Y,scene.Eye.Z}, actual_eye = new[] {actualEye.X,actualEye.Y,actualEye.Z}, target = new[] {scene.Target.X,scene.Target.Y,scene.Target.Z}, ambient_cover = cover, loam_patches=Session.SoilDetail.LoamDetails.PatchCount,
                loam_patch_rebuilds=Session.SoilDetail.LoamDetails.RebuildCount,
                loam_leaf_count=CountLoamMeta(Session.SoilDetail.LoamDetails, "leaf_count"),
                loam_duff_count=CountLoamMeta(Session.SoilDetail.LoamDetails, "duff_count"),
                flora_triangles=Session.Flora.TrianglesDrawn });
        }
        if (Main.UserArgs.Contains("--review-motion"))
        {
            var scene = scenes.FirstOrDefault(s => s.Name == "forest_floor_medium") ?? scenes[0];
            // The static tour may end on a forced underwater diagnostic. Restore
            // the actual camera medium even when no MediumChanged event fires.
            Session.CameraRig.LookAtPoint(scene.Eye, scene.Target);
            await Frames(24);
            bool motionUnderwater = Session.CameraRig.Medium.Underwater;
            Session.EnvRig.SetUnderwater(motionUnderwater);
            Session.Water.SetUnderwater(motionUnderwater);
            Session.EnvRig._Process(.3);
            await Frames(2);
            Check("forest motion uses actual camera medium", motionUnderwater == Session.EnvRig.UnderwaterVisual);
            for (int frame = 0; frame < 32; frame++)
            {
                float t = frame / 31f;
                var offset = new Vector3(Mathf.Sin(t * Mathf.Pi * 2) * .16f, .025f * Mathf.Sin(t * Mathf.Pi), 0);
                Session.CameraRig.LookAtPoint(scene.Eye + offset, scene.Target);
                LockReviewFoliage(Session, t * 2f);
                await Frames(2);
                await Screenshot($"forest_motion_{frame:D3}");
            }
            File.WriteAllText(Path.Combine(OutDir, "motion.json"), JsonSerializer.Serialize(new
            {
                frames=32, playback_fps=16, duration_seconds=2, scene=scene.Name,
                initial_camera_underwater=motionUnderwater,
                final_camera_underwater=Session.CameraRig.Medium.Underwater,
                final_environment_underwater=Session.EnvRig.UnderwaterVisual,
                path="small camera arc and explicit material review_time/preview_time, including groundcover sway; other raw TIME and initial CPU animal poses uncontrolled",
                performance_evaluated=false,
            }, new JsonSerializerOptions { WriteIndented=true }));
        }
        string after = WorldSerializer.Digest(W);
        Check("world review leaves paused ecology unchanged", before == after);
        _facts["world_review"] = new { seed=W.Descriptor.Seed, tick=W.Clock.Tick, flora=W.Flora.Count, fauna=W.Fauna.Count, digest_before=before, digest_after=after, resolution=new[]{1600,900}, quality, light_seconds=lightSeconds, fixed_exposure=forestReview, foliage_review_seconds=0, material_review_seconds=0, locked_materials=lockedMaterials, camera_reference=cameraReference, shader_time="Available review_time/preview_time locked; other TIME users and initial CPU animal poses uncontrolled", performance_evaluated=false, scenes=rows };
        File.WriteAllText(Path.Combine(OutDir,"world_review.json"),JsonSerializer.Serialize(_facts["world_review"],new JsonSerializerOptions{WriteIndented=true}));
        if (Main.UserArgs.Contains("--review-water-motion")) await WaterImpactMotion(scenes);
    }

    private RefScene PilotReviewScene(string name, string view)
    {
        var tree = W.PilotTree ?? throw new InvalidOperationException("Pilot-relative camera needs a pilot tree");
        var inward = new Vector3((float)-tree.Anchor.X, 0, (float)-tree.Anchor.Z).Normalized();
        var side = inward.Cross(Vector3.Up).Normalized();
        if (view == "crown")
        {
            Vector3 origin = Bridge.V(tree.Anchor, tree.BaseHeight + tree.Def.TrunkHeight * .86);
            return new RefScene(name, "pilot_relative", origin + inward * 5.2f + side * 2.6f + Vector3.Up * .55f,
                origin + inward * 1.1f - Vector3.Up * .25f);
        }
        if (view == "cutwood")
        {
            Vector3 origin = Bridge.V(tree.Anchor, tree.BaseHeight + tree.Def.TrunkHeight * .45);
            float distance = Math.Max(3f, (float)tree.Def.TrunkRadius * 3.2f);
            return new RefScene(name, "pilot_relative", origin - inward * distance + side * distance * .55f + Vector3.Up * .3f, origin);
        }
        throw new ArgumentException("Unknown pilot-relative view: " + view);
    }

    private static int CountLoamMeta(Node node, string key)
    {
        int count = node.HasMeta(key) ? node.GetMeta(key).AsInt32() : 0;
        foreach (var child in node.GetChildren()) count += CountLoamMeta(child, key);
        return count;
    }

    private static int LockReviewFoliage(Node node, float seconds = 0)
    {
        var seen = new HashSet<ShaderMaterial>();
        int locked = 0;
        void Material(Material? material)
        {
            if (material is not ShaderMaterial shader || !seen.Add(shader)) return;
            if (shader.Shader.Code.Contains("uniform float review_time")) { shader.SetShaderParameter("review_time", seconds); locked++; }
            if (shader.Shader.Code.Contains("uniform float preview_time")) { shader.SetShaderParameter("preview_time", seconds); locked++; }
        }
        void Visit(Node child)
        {
            if (child is GeometryInstance3D geometry) Material(geometry.MaterialOverride);
            Mesh? mesh = child is MeshInstance3D mi ? mi.Mesh : child is MultiMeshInstance3D mm ? mm.Multimesh?.Mesh : null;
            if (mesh != null) for (int i = 0; i < mesh.GetSurfaceCount(); i++) Material(mesh.SurfaceGetMaterial(i));
            foreach (var descendant in child.GetChildren()) Visit(descendant);
        }
        Visit(node);
        return locked;
    }

    private List<RefScene> BuildForestReviewScenes()
    {
        var list = new List<RefScene>
        {
            new("forest_overview", "forest", new Vector3(-4.2f, 4.7f, 6.8f), new Vector3(0, 0.1f, 0)),
            new("forest_reverse", "forest", new Vector3(5.2f, 3.8f, -5.8f), new Vector3(0, 0.1f, 0)),
        };
        var candidates = W.Grid.DomainCells.Where(c =>
        {
            var p = W.Grid.CellCenter(c);
            return W.Domain.ContainsDisc(p, 0.8) && !W.Water.IsWet(c)
                && W.SubstrateAtCell(p) == Vivarium.Sim.Content.Substrate.Soil
                && double.IsNaN(W.Props.PropTopAt(p));
        }).OrderByDescending(c => W.AmbientGroundCover.Cover[c]).ThenBy(c => c).ToList();
        if (candidates.Count > 0)
        {
            var ground = Ground(W.Grid.CellCenter(candidates[0]));
            list.Add(new("forest_floor_close", "ground", ground + new Vector3(0.28f, 0.26f, 0.38f), ground + new Vector3(0, .025f, 0)));
            list.Add(new("forest_floor_medium", "ground", ground + new Vector3(0.7f, 0.72f, 1.0f), ground));
        }
        if (Main.UserArgs.Contains("--loam-live"))
        {
            int cell = W.Grid.DomainCells.Where(c =>
            {
                var p = W.Grid.CellCenter(c);
                return W.Domain.ContainsDisc(p, .8) && !W.Water.IsWet(c)
                    && W.SubstrateAtCell(p) == Vivarium.Sim.Content.Substrate.Soil
                    && double.IsNaN(W.Props.PropTopAt(p));
            }).OrderByDescending(c => W.Litter.FineMass[c] * (1 - W.AmbientGroundCover.Cover[c])).First();
            var ground = Ground(W.Grid.CellCenter(cell));
            list.Add(new("loam_floor_close", "ground", ground + new Vector3(.28f, .36f, .38f), ground));
            list.Add(new("loam_floor_medium", "ground", ground + new Vector3(.7f, .85f, 1f), ground));
        }
        list.AddRange(BuildReferenceScenes().Where(s => s.Name is "interaction" or "ground_vegetation" or "rock_contact" or "log_contact" or "species_shadebell" or "species_veilfern" or "fauna_terrestrial" or "fauna_aquatic" or "water_margin" || s.Name.Contains("moss")));
        AddWaterImpactScenes(list);
        var rosettes = Session.AmbientGround.GetChildren().OfType<MultiMeshInstance3D>()
            .FirstOrDefault(n => n.Name.ToString() == "AmbientArchingRosettes");
        if (rosettes?.Multimesh.InstanceCount > 0)
        {
            var p = rosettes.GlobalTransform * rosettes.Multimesh.GetInstanceTransform(0).Origin;
            list.Add(Orbit("forest_rosette", "ground_detail", p + Vector3.Up * .07f, .62f, 32));
        }
        var shrub = W.Flora.Items.Where(f => f.SpeciesId == "shadebell")
            .OrderByDescending(f => f.Biomass).ThenBy(f => f.Id.Value).FirstOrDefault();
        if (shrub != null)
        {
            var species = W.Content.FloraOrThrow(shrub.SpeciesId);
            var root = Ground(shrub.Position);
            float height = (float)(species.Height * (.45 + .55 * Math.Sqrt(shrub.BiomassFraction(species))));
            float radius = (float)shrub.Radius(species);
            float distance = Math.Max(.8f, radius * 2.35f);
            var offset = new Vector3(.8f, 0, -.6f) * distance;
            var target = root + Vector3.Up * height * .24f;
            list.Add(new("forest_shrub_stems", "woody_detail", target + offset, target));
            list.Add(new("forest_shrub_reverse", "woody_detail", target - offset, target));
            list.Add(new("forest_shrub_crown", "woody_detail", root + offset + Vector3.Up * height * .95f,
                root + Vector3.Up * height * .55f));
        }
        foreach (string id in new[] { "ghostbristle", "marbleback", "moonveil" })
        {
            var animal = W.Fauna.Items.Where(f => f.SpeciesId == id).OrderBy(f => f.Id.Value).FirstOrDefault();
            if (animal == null) continue;
            float size = (float)(W.FaunaSystem.PhenotypeOf(animal).BodySize * W.Content.FaunaOrThrow(id).VisualScale);
            list.Add(Orbit("forest_" + id, "fauna_detail", Bridge.V(animal.Position), Math.Max(.045f, size * 3.5f), 32));
        }
        var poolCell = W.Grid.DomainCells.Where(c => W.Water.OpenWaterDepth(W.Grid.CellCenter(c)) > .012)
            .OrderByDescending(c => W.Water.OpenWaterDepth(W.Grid.CellCenter(c))).ThenBy(c => c).FirstOrDefault(-1);
        if (poolCell >= 0)
        {
            var p = W.Grid.CellCenter(poolCell);
            var target = Bridge.V(p, W.Water.SurfaceAt(p));
            list.Add(Orbit("forest_water", "water_detail", target, 1.15f, 38, 140));
            list.Add(new("forest_water_top", "water_detail", target + new Vector3(.03f, 1.6f, .04f), target));
        }
        var gravel = W.Props.Gravel.OrderByDescending(g => g.Radius).ThenBy(g => g.Id.Value).FirstOrDefault();
        if (gravel != null)
        {
            var target = Ground(gravel.Position) + Vector3.Up * .015f;
            list.Add(new("forest_gravel", "gravel_detail", target + new Vector3(.28f, .35f, .32f), target));
            list.Add(new("forest_gravel_top", "gravel_detail", target + new Vector3(.015f, .50f, .02f), target));
        }
        // Deliberate empty-void probes, including the underwater screen effect. These are
        // invariant diagnostics; the forced underwater view is not an aquatic scene.
        list.Add(new("forest_void", "void", new Vector3(0, 3, 7), new Vector3(0, 3, 10)));
        list.Add(new("forest_void_underwater", "void", new Vector3(0, 3, 7), new Vector3(0, 3, 10)));
        if (W.PilotTree is { } tree)
        {
            var inward = (-tree.Anchor).Normalized();
            var target = Bridge.V(tree.Anchor + inward * tree.Def.TrunkRadius, tree.BaseHeight + .9);
            list.Add(new("forest_bark", "bark", target + Bridge.V(inward * 1.8, .35), target));
            var upper = Bridge.V(tree.Anchor + inward * tree.Def.TrunkRadius, tree.BaseHeight + tree.Def.TrunkHeight * .60);
            list.Add(new("forest_bark_upper", "bark", upper + Bridge.V(inward * 1.25, .05), upper));
        }
        var log = W.Props.Logs.OrderByDescending(l => l.Length).ThenBy(l => l.Id.Value).FirstOrDefault();
        if (log != null)
        {
            var target = Ground(log.Position) + Vector3.Up * (float)log.Radius;
            list.Add(new("forest_log", "log", target + new Vector3(.45f, .75f, .8f), target));
        }
        return list;
    }
}
