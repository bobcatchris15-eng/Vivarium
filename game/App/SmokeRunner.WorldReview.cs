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
        // Native review must not accept live input or an asynchronously reset view.
        Session.CameraRig.KeyboardBlocked=true;
        Session.CameraRig.SetProcess(false);
        Session.CameraRig.SetProcessInput(false);
        Session.CameraRig.SetProcessUnhandledInput(false);
        Session.CameraRig.SetProcessUnhandledKeyInput(false);
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
        bool liveRender = Main.UserArgs.Contains("--review-live-render");
        Engine.TimeScale = liveRender ? 1 : 0;
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
            Session.CameraRig.Cam.Transform=Transform3D.Identity;
            Session.CameraRig.Cam.MakeCurrent();
            Session.CameraRig.Cam.LookAt(scene.Target,Vector3.Up);
            // Camera changes explicitly request presentation work; captures must wait for publication.
            Session.AmbientGround.Invalidate();
            Session.AmbientGround._Process(1.1);
            Session.SoilDetail._Process(1.1);
            Session.Flora._Process(0.5);
            await Frames(24);
            await SettleSurfaceDetailsAsync();
            Session.EnvRig.SetUnderwater(Session.CameraRig.Medium.Underwater || scene.Name == "forest_void_underwater");
            Session.EnvRig._Process(.3);
            SetWaterOpticsDebug(scene.Name);
            await Frames(2);
            await Screenshot(scene.Name);
            if (Main.UserArgs.Contains("--review-world-dof-probe"))
                await CaptureWorldDofProbe(scene);
            if (Main.UserArgs.Contains("--review-log-finite"))
                await CaptureLogFiniteProbe(scene.Name);
            if (scene.Name == "species_blue_sundew" && Main.UserArgs.Contains("--review-sundew-probe"))
                await CaptureSundewDewProbe();
            if (scene.Name == "rock_contact" && Main.UserArgs.Contains("--review-artifact-probe"))
                await CaptureReviewArtifactProbe(scene.Name);
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
        if (Main.UserArgs.Contains("--review-groundcover-response"))
            await CaptureGroundcoverResponse(scenes.FirstOrDefault(s => s.Name == "forest_floor_close") ?? scenes[0]);
        string after = WorldSerializer.Digest(W);
        Check("world review leaves paused ecology unchanged", before == after);
        _facts["world_review"] = new { seed=W.Descriptor.Seed, tick=W.Clock.Tick, flora=W.Flora.Count, fauna=W.Fauna.Count, digest_before=before, digest_after=after, resolution=new[]{1600,900}, quality, light_seconds=lightSeconds, fixed_exposure=forestReview, foliage_review_seconds=0, material_review_seconds=0, locked_materials=lockedMaterials, camera_reference=cameraReference, live_render=liveRender, shader_time="Available review_time/preview_time locked; other TIME users and initial CPU animal poses uncontrolled", performance_evaluated=false, scenes=rows };
        File.WriteAllText(Path.Combine(OutDir,"world_review.json"),JsonSerializer.Serialize(_facts["world_review"],new JsonSerializerOptions{WriteIndented=true}));
        if (Main.UserArgs.Contains("--review-water-motion")) await WaterImpactMotion(scenes);
    }

    private async Task CaptureWorldDofProbe(RefScene scene)
    {
        // Freeze the actual gameplay focus after settling; changing it between controls
        // would confound a bokeh comparison with center-ray focus acquisition.
        var rig = Session.CameraRig;
        string sceneName = scene.Name;
        // Engine.TimeScale is zero in this review. Advance only the camera's
        // presentation focus smoothing explicitly, without stepping the ecology.
        for (int i = 0; i < 90; i++)
        {
            rig.KeyboardBlocked=true;
            rig.LookAtPoint(scene.Eye,scene.Target);
            rig.Cam.Transform=Transform3D.Identity;
            rig.Cam.LookAt(scene.Target,Vector3.Up);
            rig._Process(1.0 / 60);
        }
        var attr = Session.EnvRig.CameraAttributes;
        var viewport = GetViewport();
        bool processing = rig.IsProcessing();
        bool autoExposure = attr.AutoExposureEnabled;
        bool near = attr.DofBlurNearEnabled, far = attr.DofBlurFarEnabled;
        var msaa = viewport.Msaa3D;
        var env = Session.EnvRig.Env;
        bool glow = env.GlowEnabled, ssil = env.SsilEnabled, ssr = env.SsrEnabled, sdfgi = env.SdfgiEnabled;
        bool coverageVisible = Session.Coverage.Visible;
        bool coverageProcessing = Session.Coverage.IsProcessing();
        bool rebuildCheck = Main.UserArgs.Contains("--review-rebuild-check");
        Vector3 eye = rig.GlobalPosition;
        var records = new List<object>();
        var cameraFrames=new List<object>();
        rig.SetProcess(false);
        attr.AutoExposureEnabled = false;
        async Task Capture(string mode, bool motion = false)
        {
            await Frames(30);
            int count = motion ? 8 : Main.UserArgs.Contains("--review-log-finite") || rebuildCheck ? 12 : 4;
            for (int frame = 0; frame < count; frame++)
            {
                var captureEye=eye+(motion ? new Vector3(Mathf.Sin(frame/7f*Mathf.Tau)*.06f,0,0) : Vector3.Zero);
                rig.LookAtPoint(captureEye,scene.Target);
                rig.Cam.Transform=Transform3D.Identity;
                rig.Cam.MakeCurrent();
                rig.Cam.LookAt(scene.Target,Vector3.Up);
                await Frames(3);
                await Screenshot($"{sceneName}_dof_{mode}_{frame:D2}");
                var expected=(scene.Target-rig.Cam.GlobalPosition).Normalized();
                float alignment=expected.Dot(-rig.Cam.GlobalBasis.Z);
                if(alignment<.9999f || viewport.GetCamera3D()!=rig.Cam)
                    throw new InvalidOperationException($"Review camera pose drift: {mode}/{frame} alignment={alignment}");
                var cp=rig.Cam.GlobalPosition;var cf=-rig.Cam.GlobalBasis.Z;
                cameraFrames.Add(new {mode,frame,eye=new[]{cp.X,cp.Y,cp.Z},forward=new[]{cf.X,cf.Y,cf.Z},alignment});
            }
            File.WriteAllText(Path.Combine(OutDir,sceneName+"_camera_frames.json"),JsonSerializer.Serialize(cameraFrames,new JsonSerializerOptions{WriteIndented=true}));
            records.Add(new { mode, frames = count, motion, near = attr.DofBlurNearEnabled,
                far = attr.DofBlurFarEnabled, amount = attr.DofBlurAmount,
                near_distance = attr.DofBlurNearDistance, near_transition = attr.DofBlurNearTransition,
                far_distance = attr.DofBlurFarDistance, far_transition = attr.DofBlurFarTransition,
                glow = env.GlowEnabled, ssil = env.SsilEnabled, ssr = env.SsrEnabled, sdfgi = env.SdfgiEnabled,
                msaa = viewport.Msaa3D.ToString(), coverage_visible = Session.Coverage.Visible,
                flora_visible = Session.Flora.Visible, props_visible = Session.Props.Visible, water_visible = Session.Water.Visible });
        }
        try
        {
            await Capture("baseline");
            attr.DofBlurNearEnabled = attr.DofBlurFarEnabled = false;
            await Capture("off");
            attr.DofBlurNearEnabled = near; attr.DofBlurFarEnabled = far;
            if (!rebuildCheck)
            {
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High, false);
            await Capture("no_jitter");
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High, true);
            viewport.Msaa3D = Viewport.Msaa.Disabled;
            await Capture("msaa_off");
            viewport.Msaa3D = msaa;
            if (sceneName.Contains("far") || sceneName.Contains("distant"))
            {
                env.GlowEnabled = false;
                await Capture("glow_off"); env.GlowEnabled = glow;
                env.SsilEnabled = false;
                await Capture("ssil_off"); env.SsilEnabled = ssil;
                env.SsrEnabled = false;
                await Capture("ssr_off"); env.SsrEnabled = ssr;
                env.SdfgiEnabled = false;
                await Capture("sdfgi_off"); env.SdfgiEnabled = sdfgi;
            }
            Session.Coverage.SetProcess(false);
            Session.Coverage.Visible = false;
            await Capture("coverage_hidden");
            Check(sceneName + " coverage-hidden control stays hidden", !Session.Coverage.Visible);
            Session.Coverage.Visible = coverageVisible;
            Session.Coverage.SetProcess(coverageProcessing);
            if (Main.UserArgs.Contains("--review-log-finite"))
            {
                foreach (var (node, label) in new (Node3D, string)[]
                    { (Session.Flora, "flora_hidden"), (Session.Props, "props_hidden"), (Session.Water, "water_hidden") })
                {
                    bool visible = node.Visible, process = node.IsProcessing();
                    try
                    {
                        node.SetProcess(false); node.Visible = false;
                        await Capture(label);
                        Check(sceneName + " " + label + " stays hidden", !node.Visible);
                    }
                    finally { node.Visible = visible; node.SetProcess(process); }
                }
            }
            }
            await Capture("restored");
            await Capture("motion_baseline", motion: true);
            rig.LookAtPoint(eye, scene.Target);
            attr.DofBlurNearEnabled = attr.DofBlurFarEnabled = false;
            await Capture("motion_off", motion: true);
        }
        finally
        {
            Session.Coverage.Visible = coverageVisible;
            Session.Coverage.SetProcess(coverageProcessing);
            rig.LookAtPoint(eye, scene.Target);
            attr.DofBlurNearEnabled = near; attr.DofBlurFarEnabled = far;
            attr.AutoExposureEnabled = autoExposure;
            viewport.Msaa3D = msaa;
            env.GlowEnabled = glow; env.SsilEnabled = ssil; env.SsrEnabled = ssr; env.SdfgiEnabled = sdfgi;
            RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.High, true);
            rig.SetProcess(processing);
        }
        File.WriteAllText(Path.Combine(OutDir, sceneName + "_dof_probe.json"),
            JsonSerializer.Serialize(new { scene = sceneName, controls = records,
                scope = "Actual initial habitat, fixed camera and gameplay focus, manual exposure; ecology paused. Other raw shader TIME and initial animal poses remain uncontrolled." },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CaptureSundewDewProbe()
    {
        var materials = new HashSet<ShaderMaterial>();
        int beadVertices = 0;
        void Collect(Material? material)
        {
            if (material is ShaderMaterial sm && sm.Shader?.ResourcePath == "res://Shaders/coverage_mat.gdshader") materials.Add(sm);
        }
        void Visit(Node node)
        {
            if (node is GeometryInstance3D g) Collect(g.MaterialOverride);
            Mesh? mesh = node is MeshInstance3D mi ? mi.Mesh : node is MultiMeshInstance3D mm ? mm.Multimesh?.Mesh : null;
            if (mesh != null) for (int i=0; i<mesh.GetSurfaceCount(); i++)
            {
                Collect(mesh.SurfaceGetMaterial(i));
                var arrays = mesh.SurfaceGetArrays(i);
                if (arrays[(int)Mesh.ArrayType.TexUV2].VariantType != Variant.Type.Nil)
                    beadVertices += arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array().Count(uv => uv.Y < -5.5f);
            }
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(Session.Coverage);
        if (materials.Count == 0) throw new InvalidOperationException("No coverage materials found for sundew probe.");
        var originals = materials.ToDictionary(m => m, m => m.Shader!);
        var records = new List<object>();
        const string bead = "(UV2.y < -5.5)";
        var attributes = Session.EnvRig.CameraAttributes;
        bool autoExposure = attributes.AutoExposureEnabled;
        attributes.AutoExposureEnabled = false;
        async Task Capture(string label, string? injection, bool unshaded = false, bool simple = false, float lift = 0, bool clampVertexColor = false)
        {
            foreach (var m in materials)
            {
                string code = originals[m].Code;
                if (injection != null || unshaded || simple || lift != 0 || clampVertexColor)
                {
                    if (clampVertexColor) code=code.Replace("srgb_to_linear(COLOR.rgb)", "srgb_to_linear(clamp(COLOR.rgb,vec3(0.),vec3(1.)))");
                    if (simple)
                        code = code[..code.IndexOf("void fragment()", StringComparison.Ordinal)] +
                            "void fragment() { ALBEDO=clamp(srgb_to_linear(COLOR.rgb),vec3(0.),vec3(.98)); ROUGHNESS=.8; SPECULAR=0.; }";
                    if (unshaded) code = code.Replace("render_mode diffuse_burley, cull_disabled;", "render_mode unshaded, cull_disabled;");
                    if (lift != 0) code = code.Replace("void vertex() {", "void vertex() { VERTEX += NORMAL * " + lift.ToString(CultureInfo.InvariantCulture) + ";");
                    if (injection != null) code = code.Insert(code.LastIndexOf('}'), injection);
                    m.Shader = new Shader { Code = code };
                }
                else m.Shader = originals[m];
            }
            await Frames(24);
            await Screenshot("sundew_" + label);
            records.Add(new { label, image = "sundew_" + label + ".png" });
        }
        try
        {
            await Capture("original", null);
            await Capture("dew_hidden", "\nif " + bead + " discard;\n");
            await Capture("dew_neutral", "\nif " + bead + " { ALBEDO=vec3(.18); ROUGHNESS=.8; SPECULAR=0.; }\n");
            await Capture("dew_matte_tinted", "\nif " + bead + " { ROUGHNESS=.8; SPECULAR=0.; }\n");
            await Capture("dew_mask", "\nALBEDO=vec3(0.); EMISSION=" + bead + " ? vec3(0.,1.,0.) : vec3(0.);\n");
            await Capture("restored", null);
            if (Main.UserArgs.Contains("--review-sundew-extended"))
            {
                for (int i=0; i<5; i++) await Capture("static_repeat_" + i, null);
                for (int i=0; i<5; i++) await Capture("clamped_vertex_color_" + i, null, clampVertexColor:true);
                await Capture("vertex_color_range_mask", "\nALBEDO=vec3(0.); EMISSION=any(lessThan(COLOR.rgb,vec3(0.))) ? vec3(1.,0.,1.) : (any(greaterThan(COLOR.rgb,vec3(1.))) ? vec3(0.,1.,1.) : vec3(.05)); ROUGHNESS=1.; SPECULAR=0.;\n");
                await Capture("all_rosettes_hidden", "\nif (sundew_surface > .5) discard;\n");
                await Capture("coverage_simple", null, simple:true);
                await Capture("coverage_unshaded", null, unshaded:true);
                await Capture("coverage_lift_3mm", null, lift:.003f);
                var casters = Session.Coverage.GetChildren().OfType<GeometryInstance3D>().ToDictionary(g=>g,g=>g.CastShadow);
                try
                {
                    foreach (var g in casters.Keys) g.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
                    await Capture("coverage_shadows_off", null);
                }
                finally { foreach (var g in casters.Keys) g.CastShadow=casters[g]; }
                var vp=GetViewport(); bool taa=vp.UseTaa; var msaa=vp.Msaa3D;
                try
                {
                    vp.UseTaa=false;
                    await Capture("taa_off", null);
                    vp.UseTaa=taa; vp.Msaa3D=Viewport.Msaa.Disabled;
                    await Capture("msaa_off", null);
                    for (int i=0; i<4; i++) await Capture("msaa_off_repeat_" + i, null);
                    records.Add(new { baseline_taa=taa, baseline_msaa=msaa.ToString() });
                }
                finally { vp.UseTaa=taa; vp.Msaa3D=msaa; }
                await Capture("extended_restored", null);
            }
        }
        finally
        {
            foreach (var m in materials) m.Shader = originals[m];
            attributes.AutoExposureEnabled = autoExposure;
        }
        File.WriteAllText(Path.Combine(OutDir,"sundew_probe.json"), JsonSerializer.Serialize(new {
            materials=materials.Count, bead_vertices=beadVertices, selector="Dedicated bead UV2.y=-6 (other sundew geometry=-5); no color matching or ambiguous leaf-face UV2.x", fixed_exposure=true, records,
            scope="In-memory coverage shader clones only; fixed world/camera/light, original geometry; production shader assets unchanged; raw TIME/CPU fauna poses not frozen",
            restored=materials.All(m=>m.Shader==originals[m])
        }, new JsonSerializerOptions { WriteIndented=true }));
    }

    private async Task CaptureReviewArtifactProbe(string sceneName)
    {
        var env = Session.EnvRig.Env;
        var viewport = GetViewport();
        bool ssr = env.SsrEnabled;
        bool ssil = env.SsilEnabled;
        bool ssao = env.SsaoEnabled;
        bool sdfgi = env.SdfgiEnabled;
        var debugDraw = viewport.DebugDraw;
        var msaa3D = viewport.Msaa3D;
        bool taa = viewport.UseTaa;
        var layers = new (string name, Node3D node)[]
        {
            ("flora", Session.Flora),
            ("fauna", Session.Fauna),
            ("coverage", Session.Coverage),
            ("ambient_ground", Session.AmbientGround),
        };
        var layerVisibility = layers.Select(layer => layer.node.Visible).ToArray();
        var tissueMaterials = new Dictionary<ulong, ShaderMaterial>();
        void CollectTissueMaterials(Node node)
        {
            void Add(Material? material)
            {
                if (material is not ShaderMaterial shader || shader.Shader is null) return;
                if (shader.Shader.ResourcePath is not ("res://Shaders/flora.gdshader" or "res://Shaders/leaf.gdshader" or "res://Shaders/ground_cover.gdshader")) return;
                tissueMaterials[shader.GetInstanceId()] = shader;
            }

            if (node is GeometryInstance3D geometry) Add(geometry.MaterialOverride);
            Mesh? mesh = node is MeshInstance3D mi ? mi.Mesh : node is MultiMeshInstance3D mm ? mm.Multimesh?.Mesh : null;
            if (mesh is not null)
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++) Add(mesh.SurfaceGetMaterial(surface));
            foreach (Node child in node.GetChildren()) CollectTissueMaterials(child);
        }
        CollectTissueMaterials(Session);
        var originalTissue = tissueMaterials.Values.ToDictionary(
            material => material.GetInstanceId(),
            material => (diagnostic: material.GetShaderParameter("tissue_light_diagnostic").AsInt32(),
                transmission: material.GetShaderParameter("tissue_direct_transmission").AsSingle(),
                shader: material.Shader!));
        var captures = new List<object>();
        var strippedShaderVariants = new List<object>();
        var constantColorVariants = new List<object>();
        var constantColorVariantMaterialCounts = new List<object>();

        async Task Capture(string label, string scope)
        {
            await Frames(6);
            string image = $"{sceneName}_probe_{label}";
            await Screenshot(image);
            captures.Add(new
            {
                label,
                image = image + ".png",
                scope,
                tissue_uniforms = tissueMaterials.Values.Select(material => new
                {
                    instance_id = material.GetInstanceId(),
                    diagnostic = material.GetShaderParameter("tissue_light_diagnostic").AsInt32(),
                    direct_transmission = material.GetShaderParameter("tissue_direct_transmission").AsSingle(),
                }).ToArray(),
            });
        }

        void SetTissue(int diagnostic, float? transmission = null)
        {
            foreach (var material in tissueMaterials.Values)
            {
                material.SetShaderParameter("tissue_light_diagnostic", diagnostic);
                if (transmission.HasValue) material.SetShaderParameter("tissue_direct_transmission", transmission.Value);
            }
        }

        void RestoreTissue()
        {
            foreach (var material in tissueMaterials.Values)
            {
                var original = originalTissue[material.GetInstanceId()];
                material.SetShaderParameter("tissue_light_diagnostic", original.diagnostic);
                material.SetShaderParameter("tissue_direct_transmission", original.transmission);
            }
        }

        try
        {
            env.SsrEnabled = false;
            await Capture("ssr_off", "SSR disabled only; other environment features and render layers at baseline");
            env.SsrEnabled = ssr;

            env.SsilEnabled = false;
            env.SsaoEnabled = false;
            await Capture("ssil_ssao_off", "SSIL and SSAO disabled together; other environment features and render layers at baseline");
            env.SsilEnabled = ssil;
            env.SsaoEnabled = ssao;

            env.SdfgiEnabled = false;
            await Capture("sdfgi_off", "SDFGI disabled only; other environment features and render layers at baseline");
            env.SdfgiEnabled = sdfgi;

            viewport.DebugDraw = Viewport.DebugDrawEnum.Unshaded;
            await Capture("unshaded", "Viewport unshaded debug draw; scene geometry, materials, and render layers remain present");
            viewport.DebugDraw = debugDraw;

            viewport.Msaa3D = Viewport.Msaa.Disabled;
            await Capture("msaa3d_off", "3D MSAA disabled only; all other viewport and scene settings at baseline");
            viewport.Msaa3D = msaa3D;

            viewport.UseTaa = false;
            await Capture("taa_off", "TAA disabled only; all other viewport and scene settings at baseline");
            viewport.UseTaa = taa;

            for (int i = 0; i < layers.Length; i++)
            {
                layers[i].node.Visible = false;
                try
                {
                    await Capture(layers[i].name + "_hidden", $"{layers[i].name} hidden only; all other render layers at baseline");
                }
                finally
                {
                    layers[i].node.Visible = layerVisibility[i];
                }
            }

            // Each foliage BRDF diagnostic starts from production values. Reset all captured
            // material uniforms between variants so one mode cannot contaminate the next.
            RestoreTissue();
            SetTissue(4);
            await Capture("tissue_simple_lambert", "foliage shader branch diagnostic 4; simple Lambert control on flora, leaf, and ground-cover materials");
            RestoreTissue();
            SetTissue(1);
            await Capture("tissue_diffuse_only", "foliage shader diagnostic 1; reflected diffuse-only control on flora, leaf, and ground-cover materials");
            RestoreTissue();
            SetTissue(3);
            await Capture("tissue_specular_only", "foliage shader diagnostic 3; specular-only control on flora, leaf, and ground-cover materials");
            RestoreTissue();
            SetTissue(0, 0f);
            await Capture("tissue_transmission_zero", "foliage shader composite with direct transmission set to zero; all other tissue uniforms at production values");
            RestoreTissue();
            SetTissue(5);
            await Capture("tissue_builtin_sss_off", "foliage shader diagnostic 5; built-in SSS, transmittance and backlight outputs disabled while custom direct lighting remains at production values");

            // Compile an in-memory control with the engine SSS/backlight output writes removed.
            // This distinguishes a compiled built-in feature path from runtime-zeroed outputs.
            RestoreTissue();
            var shaderWritePattern = new System.Text.RegularExpressions.Regex(
                @"\b(?<output>SSS_STRENGTH|SSS_TRANSMITTANCE_COLOR|SSS_TRANSMITTANCE_DEPTH|SSS_TRANSMITTANCE_BOOST|BACKLIGHT)\s*(?:\+=|-=|\*=|/=|=)\s*[^;]*;",
                System.Text.RegularExpressions.RegexOptions.Compiled);
            var cloneByPath = new Dictionary<string, Shader>();
            foreach (var material in tissueMaterials.Values)
            {
                var originalShader = originalTissue[material.GetInstanceId()].shader;
                string path = originalShader.ResourcePath;
                if (path is not ("res://Shaders/flora.gdshader" or "res://Shaders/leaf.gdshader")) continue;
                if (!cloneByPath.TryGetValue(path, out var clone))
                {
                    string source = originalShader.Code;
                    var matches = shaderWritePattern.Matches(source);
                    var removedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        string output = match.Groups["output"].Value;
                        removedCounts[output] = removedCounts.GetValueOrDefault(output) + 1;
                    }
                    string stripped = shaderWritePattern.Replace(source, "");
                    clone = new Shader { Code = stripped };
                    cloneByPath[path] = clone;
                    strippedShaderVariants.Add(new
                    {
                        shader = path,
                        removed_write_counts = removedCounts,
                        removed_write_total = matches.Count,
                        original_source_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
                        cloned_source_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stripped))).ToLowerInvariant(),
                    });
                }
                material.Shader = clone;
            }
            try
            {
                await Capture("compiled_builtin_sss_removed", "In-memory flora/leaf shader clones have all SSS/transmittance/backlight output assignments removed; source materials, uniforms, and custom light include retained");
            }
            finally
            {
                foreach (var material in tissueMaterials.Values)
                    material.Shader = originalTissue[material.GetInstanceId()].shader;
            }

            // Hard fork: preserve original meshes/vertex code while replacing only fragment output,
            // then replace the entire fragment, then bypass both custom shaders on flora geometry.
            RestoreTissue();
            static (int open, int close) FragmentBodySpan(string source)
            {
                int signature = source.IndexOf("void fragment()", StringComparison.Ordinal);
                if (signature < 0) throw new InvalidOperationException("Foliage shader has no fragment() entry point.");
                int open = source.IndexOf('{', signature);
                if (open < 0) throw new InvalidOperationException("Foliage fragment() has no body.");
                int depth = 0;
                bool lineComment = false, blockComment = false, quoted = false;
                for (int i = open; i < source.Length; i++)
                {
                    char c = source[i], next = i + 1 < source.Length ? source[i + 1] : '\0';
                    if (lineComment) { if (c == '\n') lineComment = false; continue; }
                    if (blockComment) { if (c == '*' && next == '/') { blockComment = false; i++; } continue; }
                    if (quoted) { if (c == '\\' && next != '\0') { i++; continue; } if (c == '"') quoted = false; continue; }
                    if (c == '/' && next == '/') { lineComment = true; i++; continue; }
                    if (c == '/' && next == '*') { blockComment = true; i++; continue; }
                    if (c == '"') { quoted = true; continue; }
                    if (c == '{') depth++;
                    else if (c == '}' && --depth == 0) return (open, i);
                }
                throw new InvalidOperationException("Foliage fragment() braces are unbalanced.");
            }

            async Task CaptureConstantColorShaderVariant(string label, bool replaceWholeFragment, string? outputOverride = null)
            {
                var variants = new Dictionary<string, Shader>(StringComparer.Ordinal);
                int assigned = 0;
                foreach (var material in tissueMaterials.Values)
                {
                    Shader originalShader = originalTissue[material.GetInstanceId()].shader;
                    string path = originalShader.ResourcePath;
                    if (path is not ("res://Shaders/flora.gdshader" or "res://Shaders/leaf.gdshader")) continue;
                    if (!variants.TryGetValue(path, out var clone))
                    {
                        string source = originalShader.Code;
                        var (open, close) = FragmentBodySpan(source);
                        string constantOutputs = outputOverride ?? "\n    ALBEDO = vec3(0.15, 0.65, 0.08);\n    EMISSION = vec3(0.0);\n    ROUGHNESS = 0.8;\n";
                        string modified = replaceWholeFragment
                            ? source[..(open + 1)] +
                                "\n    tissue_fiber_view = vec3(1.0, 0.0, 0.0);\n" +
                                "    tissue_fiber_mask = 0.0;\n    tissue_phase_mask = 0.0;\n" +
                                "    tissue_rim = 0.0;\n    tissue_tau = 0.0;\n" +
                                "    NORMAL = vec3(0.0, 0.0, 1.0);\n" +
                                constantOutputs + source[close..]
                            : source[..close] + constantOutputs + source[close..];
                        clone = new Shader { Code = modified };
                        variants[path] = clone;
                        constantColorVariants.Add(new
                        {
                            label, shader = path, fragment_body_replaced = replaceWholeFragment, output_override = outputOverride,
                            original_source_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
                            cloned_source_sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(modified))).ToLowerInvariant(),
                        });
                    }
                    material.Shader = clone;
                    assigned++;
                }
                try
                {
                    await Capture(label, replaceWholeFragment
                        ? "In-memory flora/leaf clones replace entire fragment() body with constant outputs and initialized tissue varyings; original vertex shader, custom light include and geometry retained"
                        : "In-memory flora/leaf clones append constant final ALBEDO, EMISSION and ROUGHNESS; all prior fragment calculations, vertex shader and geometry retained");
                }
                finally
                {
                    foreach (var material in tissueMaterials.Values)
                        material.Shader = originalTissue[material.GetInstanceId()].shader;
                }
                constantColorVariantMaterialCounts.Add(new { label, assigned });
            }

            await CaptureConstantColorShaderVariant("fragment_final_constant", replaceWholeFragment: false);
            await CaptureConstantColorShaderVariant("fragment_body_constant", replaceWholeFragment: true);
            await CaptureConstantColorShaderVariant("albedo_only_constant", false, "\n ALBEDO=vec3(0.15,0.65,0.08);\n");
            await CaptureConstantColorShaderVariant("albedo_max_one", false, "\n ALBEDO=min(ALBEDO,vec3(1.0));\n");
            await CaptureConstantColorShaderVariant("albedo_min_reflectance", false, "\n ALBEDO=max(ALBEDO,vec3(0.003));\n");
            await CaptureConstantColorShaderVariant("albedo_range_mask", false, @"
                vec3 marker=vec3(0.12);
                if(any(greaterThan(ALBEDO,vec3(1.0)))) marker=vec3(3.0,0.0,0.0);
                else if(any(lessThan(ALBEDO,vec3(0.003)))) marker=vec3(0.0,3.0,3.0);
                ALBEDO=vec3(0.0); EMISSION=marker; ROUGHNESS=0.8;
                NORMAL=vec3(0.0,0.0,1.0);
            ");
            await CaptureConstantColorShaderVariant("emission_only_zero", false, "\n EMISSION=vec3(0.0);\n");
            await CaptureConstantColorShaderVariant("roughness_only_constant", false, "\n ROUGHNESS=0.8;\n");
            await CaptureConstantColorShaderVariant("albedo_validity_mask", false, @"
                vec3 marker=vec3(0.12);
                if(any(isnan(ALBEDO))) marker=vec3(3.0,0.0,3.0);
                else if(any(isinf(ALBEDO))) marker=vec3(3.0,3.0,0.0);
                else if(any(lessThan(ALBEDO,vec3(0.0)))) marker=vec3(3.0,0.0,0.0);
                else if(all(lessThanEqual(ALBEDO,vec3(0.000001)))) marker=vec3(0.0,3.0,3.0);
                ALBEDO=vec3(0.0); EMISSION=marker; ROUGHNESS=0.8;
                NORMAL=vec3(0.0,0.0,1.0);
            ");


            var floraGeometry = new List<(GeometryInstance3D node, Material? original)>();
            void CollectFloraGeometry(Node node)
            {
                if (node is GeometryInstance3D geometry)
                    floraGeometry.Add((geometry, geometry.MaterialOverride));
                foreach (Node child in node.GetChildren()) CollectFloraGeometry(child);
            }
            CollectFloraGeometry(Session.Flora);
            var geometryControl = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.15f, 0.65f, 0.08f),
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = false,
            };
            try
            {
                foreach (var (node, _) in floraGeometry) node.MaterialOverride = geometryControl;
                await Capture("flora_standard_unshaded_override",
                    "Flora geometry uses one StandardMaterial3D unshaded bright green MaterialOverride; custom flora/leaf vertex, fragment and light shaders bypassed; scene geometry, instances and depth retained");
            }
            finally
            {
                foreach (var (node, original) in floraGeometry) node.MaterialOverride = original;
            }
            constantColorVariantMaterialCounts.Add(new { label = "flora_standard_unshaded_override", assigned = floraGeometry.Count });
        }
        finally
        {
            RestoreTissue();
            foreach (var material in tissueMaterials.Values)
                material.Shader = originalTissue[material.GetInstanceId()].shader;
            env.SsrEnabled = ssr;
            env.SsilEnabled = ssil;
            env.SsaoEnabled = ssao;
            env.SdfgiEnabled = sdfgi;
            viewport.DebugDraw = debugDraw;
            viewport.Msaa3D = msaa3D;
            viewport.UseTaa = taa;
            for (int i = 0; i < layers.Length; i++)
                layers[i].node.Visible = layerVisibility[i];
        }

        File.WriteAllText(Path.Combine(OutDir, "artifact_probe.json"), JsonSerializer.Serialize(new
        {
            scene = sceneName,
            baseline_image = sceneName + ".png",
            baseline_environment = new { ssr, ssil, ssao, sdfgi, debug_draw = debugDraw.ToString(), msaa3d = msaa3D.ToString(), taa },
            captures,
            compiled_builtin_sss_removed = strippedShaderVariants,
            constant_color_shader_variants = constantColorVariants,
            constant_color_variant_material_counts = constantColorVariantMaterialCounts,
            tissue_materials = tissueMaterials.Values.Select(material => new
            {
                instance_id = material.GetInstanceId(),
                shader = material.Shader?.ResourcePath,
                original_diagnostic = originalTissue[material.GetInstanceId()].diagnostic,
                original_transmission = originalTissue[material.GetInstanceId()].transmission,
            }).ToArray(),
            baseline_3d_unchanged = true,
            mutated_and_restored = new[] { "Environment.SsrEnabled", "Environment.SsilEnabled", "Environment.SsaoEnabled", "Environment.SdfgiEnabled", "Viewport.DebugDraw", "Viewport.Msaa3D", "Viewport.UseTaa", "Flora.Visible", "Fauna.Visible", "Coverage.Visible", "AmbientGround.Visible", "ShaderMaterial.Shader", "Flora.GeometryInstance3D.MaterialOverride", "tissue_light_diagnostic", "tissue_direct_transmission" },
        }, new JsonSerializerOptions { WriteIndented = true }));
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
