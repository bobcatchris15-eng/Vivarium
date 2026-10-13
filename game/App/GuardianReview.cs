using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

public partial class GuardianReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public override void _Ready() => _ = Run();
    private async Task Run()
    {
        try
        {
            GetWindow().Size = new Vector2I(1100, 760);
            Engine.MaxFps = 30;
            Directory.CreateDirectory(OutDir);
            if (Main.UserArgs.Contains("--guardian-populated")) { await RunPopulated(); GetTree().Quit(); return; }
            var records = new List<object>();
            foreach (var def in GuardianCatalog.All)
            {
                string? species = Main.ArgAfter("--guardian-species");
                if (species != null && species != def.Id && species != def.Archetype) continue;
                var descriptor = Content.PresetOrThrow("default").Clone();
                descriptor.PilotTreeId = def.AssociatedPilotTreeId;
                descriptor.GuardianId = def.Id;
                var world = VivariumWorld.Create(Content, descriptor);
                world.Clock.Paused = true;
                var stage = new Node3D(); AddChild(stage);
                var lighting = new EnvironmentRig { BenchmarkLightSeconds = 0 };
                stage.AddChild(lighting);
                lighting.ApplyQuality(2);
                // Neutral studio illumination reveals surface defects that the
                // enclosure grade can hide. Restore production lighting for live review.
                lighting.ProcessMode = ProcessModeEnum.Disabled;
                lighting.Env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                lighting.Env.AmbientLightColor = Colors.White;
                lighting.Env.AmbientLightEnergy = 0.65f;
                lighting.Env.FogEnabled = false;
                lighting.Sun.RotationDegrees = new Vector3(-40, -35, 0);
                lighting.Sun.LightEnergy = 1.3f;
                var renderer = new GuardianRenderer(); stage.AddChild(renderer); renderer.Build(world);
                var g = world.Guardian!;
                var start = g.Position;
                var origin = Bridge.V(start);
                renderer.ReviewSupportPlaneHeight = origin.Y;
                var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(12, 12) },
                    Position = origin - Vector3.Up * 0.015f,
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.26f, 0.18f), Roughness = 1 } };
                stage.AddChild(floor);
                float studioSize = def.Archetype switch {
                    "crested_canopy" => 1.1f, "quilled_arboreal" => 1.3f,
                    "woodland_omnivore" => 1.7f, "armored_forest" => 1.5f,
                    "canopy_serpent" => 1.9f, _ => 3.4f };
                var studioAim = origin + Vector3.Up * (def.Archetype switch {
                    "crested_canopy" => 0.3f, "quilled_arboreal" => 0.38f,
                    "woodland_omnivore" => 0.4f, "armored_forest" => 0.28f,
                    "canopy_serpent" => 0.13f, _ => 0.22f });
                if (def.Archetype == "great_ground")
                    studioAim += Bridge.Yaw(g.Heading + Math.PI / 2) * Vector3.Back * (float)(.65 * def.VisualScale * g.Scale);
                var camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal, Size = studioSize };
                stage.AddChild(camera); renderer.Camera = camera;
                string dir = Path.Combine(OutDir, def.Archetype); Directory.CreateDirectory(dir);
                foreach (var activity in new[] { GuardianActivity.Groom, GuardianActivity.Explore, GuardianActivity.Feed, GuardianActivity.Rest })
                {
                    g.Activity = activity;
                    g.Heading = 0;
                    g.TargetPosition = g.Position;
                    for (int frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var animation = renderer.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
                    if (animation != null)
                    {
                        string clip = activity switch { GuardianActivity.Rest => "rest", GuardianActivity.Feed => "feed",
                            GuardianActivity.Explore => "walk", _ => "idle" };
                        GD.Print(def.Archetype + " clips=" + string.Join(",", animation.GetAnimationList()));
                        (renderer.FindChild("Skeleton3D", true, false) as Skeleton3D)?.ResetBonePoses();
                        animation.Play(clip, 0);
                        animation.Seek(0.45, true);
                        animation.Advance(0);
                    }
                    camera.Position = origin + new Vector3(2.7f, 1.7f, 2.7f);
                    camera.LookAt(studioAim);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, activity + ".png"));
                }
                // Anatomical silhouette and material response need distinct evidence.
                if (renderer.FindChild("AnimationPlayer", true, false) is AnimationPlayer neutral)
                { neutral.Play("idle", 0); neutral.Seek(0.45, true); neutral.Advance(0); }
                var meshes = DescendantMeshes(renderer).ToArray();
                if (Main.UserArgs.Contains("--guardian-animation-sweep"))
                    await CaptureAnimationSweep(renderer, g, dir);
                if (Main.UserArgs.Contains("--guardian-face-probe"))
                { GD.Print("GUARDIAN_FACE_PROBE_OK " + OutDir); GetTree().Quit(); return; }
                if (Main.UserArgs.Contains("--guardian-transition-review"))
                {
                    world.Clock.Paused = false;
                    await CaptureAnimationTransitions(renderer, g, dir);
                    world.Clock.Paused = true;
                }
                g.Activity = GuardianActivity.Groom;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (renderer.FindChild("AnimationPlayer", true, false) is AnimationPlayer studioIdle)
                { (renderer.FindChild("Skeleton3D", true, false) as Skeleton3D)?.ResetBonePoses();
                    studioIdle.Play("idle", 0); studioIdle.Seek(0.45, true); studioIdle.Advance(0); }
                var overrides = meshes.Select(mesh => mesh.MaterialOverride).ToArray();
                var clay = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.42f, 0.42f), Roughness = 0.8f };
                var views = new[] { new Vector3(3.8f, 0.8f, 0), new Vector3(0, 0.8f, 3.8f), new Vector3(2.7f, 1.1f, 2.7f) };
                for (int view = 0; view < views.Length; view++)
                {
                    camera.Position = origin + views[view]; camera.LookAt(studioAim);
                    foreach (var mesh in meshes) mesh.MaterialOverride = clay;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, $"clay-{view}.png"));
                    for (int i = 0; i < meshes.Length; i++) meshes[i].MaterialOverride = overrides[i];
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, $"surface-{view}.png"));
                }
                lighting.Sun.RotationDegrees = new Vector3(-15, 65, 0);
                lighting.Sun.LightEnergy = 0.9f;
                lighting.ProcessMode = ProcessModeEnum.Disabled;
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, "grazing-surface.png"));
                var skeleton = renderer.FindChild("Skeleton3D", true, false) as Skeleton3D;
                if (Main.UserArgs.Contains("--guardian-contact-review") && renderer.AppendageContact != null)
                    await CaptureContactReview(renderer, world, stage, camera, floor, origin, dir);
                if (skeleton != null && skeleton.FindBone("Head") is int head && head >= 0)
                {
                    var face = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head).Origin;
                    float oldSize = camera.Size;
                    camera.Size = def.Archetype == "crested_canopy" ? 0.55f : 0.8f;
                    camera.Position = face + new Vector3(0.8f, 0.3f, 0.8f); camera.LookAt(face);
                    for (int frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, "face-detail.png"));
                    camera.Size = oldSize;
                }
                if (Main.UserArgs.Contains("--guardian-surface-detail") && skeleton != null &&
                    skeleton.FindBone("Body16") is int body && body >= 0)
                {
                    var point = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(body).Origin;
                    float oldSize = camera.Size;
                    camera.Size = 0.45f;
                    camera.Position = point + new Vector3(1, 0.25f, 0.3f); camera.LookAt(point);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, "body-detail-grazing.png"));
                    lighting.Sun.RotationDegrees = new Vector3(-50, -25, 0); lighting.Sun.LightEnergy = 1.3f;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, "body-detail-neutral.png"));
                    camera.Size = oldSize;
                }
                // Exercise the real decision and locomotion path, independent of renderer frame rate.
                g.ActivityDuration = 0;
                double travelled = 0;
                var last = g.PositionXZ;
                for (int step = 0; step < 252; step++)
                {
                    world.GuardianSystem.StepBehaviour(40);
                    travelled += Vivarium.Sim.Core.Vec2.Distance(last, g.PositionXZ);
                    last = g.PositionXZ;
                }
                records.Add(new { def.Id, travelled, activity = g.Activity.ToString(), position = g.Position,
                    insideTrunk = world.PilotTree!.BlocksTrunk(g.PositionXZ, 0.35) });
                floor.Visible = false;
                renderer.ReviewSupportPlaneHeight = null;
                lighting.QueueFree();
                var productionLighting = new EnvironmentRig { BenchmarkLightSeconds = 0 };
                stage.AddChild(productionLighting); productionLighting.ApplyQuality(2);
                camera.Size = 4.4f;
                var island = new IslandRenderer(); stage.AddChild(island); island.Build(world);
                var tree = new PilotTreeRenderer(); stage.AddChild(tree); tree.Build(world);
                world.GuardianSystem.InitializeGuardian();
                renderer.Build(world);
                g = world.Guardian!;
                world.Clock.Paused = false;
                var motion = new List<object>();
                for (int frame = 0; frame < 150; frame++)
                {
                    world.Scheduler.Advance(1.0 / 30);
                    var focus = renderer.DisplayPosition + Vector3.Up * 0.5f;
                    var inward = new Vector3((float)-world.PilotTree!.Anchor.X, 0, (float)-world.PilotTree.Anchor.Z).Normalized();
                    camera.Position = focus + inward * 3.5f + inward.Cross(Vector3.Up) * 2.5f + Vector3.Up * 1.6f;
                    camera.LookAt(focus);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (frame % 10 == 0)
                    {
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(dir, $"live-{frame:000}.png"));
                        motion.Add(new { frame, tick = world.Clock.Tick, activity = g.Activity.ToString(), position = g.Position,
                            transformDeterminant = renderer.GlobalBasis.Determinant() });
                    }
                }
                File.WriteAllText(Path.Combine(dir, "motion.json"), JsonSerializer.Serialize(motion, new JsonSerializerOptions { WriteIndented = true }));
                stage.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            string? reviewSpecies = Main.ArgAfter("--guardian-species");
            string receiptName = reviewSpecies == null ? "review.json" : $"review-{reviewSpecies}.json";
            File.WriteAllText(Path.Combine(OutDir, receiptName), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("GUARDIAN_REVIEW_OK " + Path.GetFullPath(OutDir)); GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("GUARDIAN_REVIEW_FAILED " + ex); GetTree().Quit(1); }
    }

    private async Task CaptureContactReview(GuardianRenderer renderer, VivariumWorld world, Node3D stage,
        Camera3D camera, MeshInstance3D floor, Vector3 origin, string dir)
    {
        var modifier = renderer.AppendageContact!;
        var vegetation = renderer.ShrubContact!;
        var skeleton = renderer.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (skeleton == null) return;
        if (renderer.SkinContact?.Valid != true)
            throw new InvalidOperationException("Evaluated skin contact is unavailable: " + renderer.SkinContact?.Diagnostic);
        var player = renderer.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
        world.Guardian!.Activity = GuardianActivity.Groom;
        player?.Play("idle", 0); player?.Seek(.1, true); player?.Advance(0);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string hillBone = modifier.ContinuousGroundedBody ? modifier.ChainPrefix + 16.ToString(modifier.ChainIndexFormat) : "Tail6";
        if (skeleton.FindBone(hillBone) < 0 && skeleton.FindBone("Tail2") >= 0) hillBone = "Tail2";
        int hillJoint = skeleton.FindBone(hillBone);
        if (hillJoint < 0) throw new InvalidOperationException("Missing contact hill joint " + hillBone);
        var tail = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(hillJoint).Origin;
        var hillCenter = new Vector3(tail.X, origin.Y, tail.Z);
        float hillHeight = Math.Max(.2f, tail.Y - origin.Y + .08f);
        float Height(Vector3 p) => origin.Y + hillHeight * MathF.Exp(-((p.X - hillCenter.X) * (p.X - hillCenter.X)
            + (p.Z - hillCenter.Z) * (p.Z - hillCenter.Z)) / .075f);
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        const int divisions = 160; const float width = 8;
        Vector3 Point(int x, int z) { var p = origin + new Vector3((float)x / divisions * width - width / 2, 0,
            (float)z / divisions * width - width / 2); p.Y = Height(p); return p; }
        for (int z = 0; z < divisions; z++) for (int x = 0; x < divisions; x++)
        {
            var a = Point(x, z); var b = Point(x + 1, z); var c = Point(x, z + 1); var d = Point(x + 1, z + 1);
            foreach (var p in new[] { a, b, c, b, d, c })
            {
                const float e = .002f;
                var normal = new Vector3(Height(p - Vector3.Right * e) - Height(p + Vector3.Right * e), 2 * e,
                    Height(p - Vector3.Back * e) - Height(p + Vector3.Back * e)).Normalized();
                surface.SetNormal(normal); surface.AddVertex(p);
            }
        }
        var terrain = new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = new StandardMaterial3D {
            AlbedoColor = new Color(.16f, .19f, .10f), Roughness = 1 } };
        stage.AddChild(terrain); floor.Visible = false;
        renderer.ReviewGroundSample = Height;
        // A real game shrub mesh and material, drawn through MultiMesh just like production flora.
        var plantSpecies = Content.FloraOrThrow("lanternbrush");
        var plantData = Vivarium.Sim.Geometry.OrganismMeshes.FloraTier(plantSpecies, 57291, 0, tieredSource: true);
        var profile = FloraVisualProfile.Load(plantSpecies.Id);
        var plantMaterial = Bridge.Shader("res://Shaders/flora.gdshader");
        FloraSurfaceProfiles.Bind(plantMaterial, plantSpecies, photographedLeaves: profile == null);
        plantMaterial.SetShaderParameter("sway", 0f);
        var plantMesh = profile == null ? Bridge.ToArrayMesh(plantData, plantMaterial) : profile.Compile(plantData, plantMaterial, profile.LeafMaterial());
        var shrub = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true, UseCustomData = true, Mesh = plantMesh, InstanceCount = 1 } };
        stage.AddChild(shrub);
        int chestBone = skeleton.FindBone("Chest");
        if (chestBone < 0) chestBone = skeleton.FindBone("Body0");
        if (chestBone < 0) chestBone = skeleton.FindBone("Body");
        if (chestBone < 0) chestBone = skeleton.FindBone("Body00");
        var chest = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(chestBone).Origin;
        var shrubRoot = new Vector3(chest.X + .09f, Height(chest), chest.Z + .03f);
        var plantBase = new Transform3D(Basis.Identity.Scaled(new Vector3(.22f, .65f, .22f)), shrubRoot);
        shrub.Multimesh.SetInstanceTransform(0, plantBase); shrub.Multimesh.SetInstanceColor(0, Colors.White);
        shrub.Multimesh.SetInstanceCustomData(0, new Color(.21f, 1, 0, .43f));
        vegetation.Register(shrub, 0); modifier.ResetContact();
        string output = Path.Combine(dir, "contacts"); Directory.CreateDirectory(output);
        var samples = new List<object>();
        var oldCamera = camera.Transform; float oldSize = camera.Size;
        bool isMonitor = skeleton.FindBone("Tongue") >= 0 && skeleton.FindBone("Pelvis") >= 0;
        camera.Size = isMonitor || modifier.ContinuousGroundedBody ? 3.8f : skeleton.FindBone("Body0") >= 0 ? 2.8f : 2.1f;
        var contactAim = origin + Vector3.Up * .55f;
        if (isMonitor || modifier.ContinuousGroundedBody || skeleton.FindBone("Body0") >= 0) contactAim += (hillCenter - origin) * .4f;
        camera.Position = contactAim + new Vector3(2.6f, 1.2f, 2.6f); camera.LookAt(contactAim);
        world.Clock.Paused = false;
        float maxLength = 0, maxPenetration = 0, maxBend = 0, maxDisplacement = 0, maxFootPenetration = 0,
            maxSkinFootPenetration = 0, maxSkinBodyPenetration = 0, maxSkinTailPenetration = 0;
        for (int frame = 0; frame < 150; frame++)
        {
            // Real animation updates, then a release interval to prove foliage recovery.
            if (frame == 45 && modifier.ContactPoints.Length > 6)
            {
                int middleIndex = modifier.ContinuousGroundedBody ? Math.Min(16, modifier.ContactPoints.Length - 2) : 4;
                var middle = modifier.ContactPoints[middleIndex].Lerp(modifier.ContactPoints[middleIndex + 1], .5f);
                shrubRoot = new Vector3(middle.X, Height(middle), middle.Z);
                plantBase.Origin = shrubRoot; shrub.Multimesh.SetInstanceTransform(0, plantBase);
            }
            if (frame == 90) { shrubRoot.X += .8f; plantBase.Origin = shrubRoot; shrub.Multimesh.SetInstanceTransform(0, plantBase); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            maxLength = Math.Max(maxLength, modifier.MaximumLengthError);
            maxPenetration = Math.Max(maxPenetration, modifier.MaximumGroundPenetration);
            maxBend = Math.Max(maxBend, vegetation.MaximumBend);
            maxDisplacement = Math.Max(maxDisplacement, modifier.MaximumContactDisplacement);
            maxFootPenetration = Math.Max(maxFootPenetration, renderer.LegContact?.MaximumSolePenetration ?? 0);
            maxSkinFootPenetration = Math.Max(maxSkinFootPenetration, renderer.LegContact?.MaximumSkinFootPenetration ?? 0);
            maxSkinBodyPenetration = Math.Max(maxSkinBodyPenetration, renderer.LegContact?.MaximumSkinBodyPenetration ?? 0);
            float? continuousBodyClearance = null, continuousHeadClearance = null;
            if (modifier.ContinuousGroundedBody)
            {
                if (modifier.ContinuousBodySampleCount == 0 || modifier.ContinuousHeadSampleCount == 0)
                    throw new InvalidOperationException("Continuous body/head samples unavailable");
                continuousBodyClearance = modifier.ContinuousBodyClearance; continuousHeadClearance = modifier.ContinuousHeadClearance;
                maxSkinBodyPenetration = Math.Max(maxSkinBodyPenetration, Math.Max(-modifier.ContinuousBodyClearance, -modifier.ContinuousHeadClearance));
            }
            maxSkinTailPenetration = Math.Max(maxSkinTailPenetration, modifier.MaximumSkinTailPenetration);
            if (renderer.SkinContact?.Valid != true || (renderer.LegContact != null && renderer.LegContact.SkinFootSampleCount == 0) || modifier.SkinTailSampleCount == 0)
                throw new InvalidOperationException("Evaluated foot samples unavailable during contact review");
            if (frame % 10 == 0) GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"contact-{frame:000}.png"));
            samples.Add(new { frame, modifier.MaximumLengthError, modifier.MaximumGroundPenetration,
                modifier.MaximumContactDisplacement,
                modifier.MaximumBendDeviation,
                footPenetration = renderer.LegContact?.MaximumSolePenetration ?? 0,
                footReachError = renderer.LegContact?.MaximumReachError ?? 0,
                footRetargetDistance = renderer.LegContact?.MaximumFootRetargetDistance ?? 0,
                stanceFootTravel = renderer.LegContact?.MaximumStanceFootTravel ?? 0,
                skinFootPenetration = renderer.LegContact?.MaximumSkinFootPenetration,
                skinBodyPenetration = renderer.LegContact?.MaximumSkinBodyPenetration,
                continuousBodyClearance, continuousHeadClearance,
                skinFootSampleCount = renderer.LegContact?.SkinFootSampleCount,
                skinTailPenetration = modifier.MaximumSkinTailPenetration, skinTailSampleCount = modifier.SkinTailSampleCount,
                stanceFootGap = renderer.LegContact?.MaximumStanceFootGap,
                vegetation.ContactCount, vegetation.MaximumBend, points = modifier.ContactPoints.Select(p => new { p.X, p.Y, p.Z }).ToArray() });
        }
        File.WriteAllText(Path.Combine(output, "samples.json"), JsonSerializer.Serialize(new { maxLength, maxPenetration, maxBend, maxDisplacement, maxFootPenetration,
            maxSkinFootPenetration, maxSkinBodyPenetration, maxSkinTailPenetration, skinRestReconstructionError = renderer.SkinContact!.MaximumRestReconstructionError,
            recoveredBend = vegetation.MaximumBend, renderedGround = "same sampled height function as contact constraints",
            shrub = plantSpecies.Id, shrubPhases = "0-44 chest brush;45-89 tail segment interior;90-149 released",
            contactScope = "bone capsules/sole proxies and rendered shrub AABBs; not exact deformed mesh collision", samples }, new JsonSerializerOptions { WriteIndented = true }));
        if (maxPenetration > .025f || maxLength > .04f || maxBend < .02f || maxDisplacement < .015f || vegetation.MaximumBend > .02f || maxSkinFootPenetration > .025f || maxSkinBodyPenetration > .025f || maxSkinTailPenetration > .025f)
            throw new InvalidOperationException($"Contact fixture failed: penetration={maxPenetration}, lengthError={maxLength}, shrubBend={maxBend}");
        world.Clock.Paused = true; renderer.ReviewGroundSample = null; floor.Visible = true;
        terrain.QueueFree(); shrub.QueueFree(); modifier.ResetContact();
        camera.Transform = oldCamera; camera.Size = oldSize;
    }

    private async Task CaptureAnimationTransitions(GuardianRenderer renderer, GuardianIndividual guardian, string dir)
    {
        if (renderer.FindChild("AnimationPlayer", true, false) is not AnimationPlayer player) return;
        var skeleton = renderer.FindChild("Skeleton3D", true, false) as Skeleton3D;
        string output = Path.Combine(dir, "transitions"); Directory.CreateDirectory(output);
        var samples = new List<object>();
        int phase = 0;
        foreach (var activity in new[] { GuardianActivity.Groom, GuardianActivity.Feed, GuardianActivity.Groom,
            GuardianActivity.Rest, GuardianActivity.Groom, GuardianActivity.Explore, GuardianActivity.Groom })
        {
            guardian.Activity = activity;
            for (int frame = 0; frame < 36; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var bones = new List<object>();
                if (skeleton != null)
                    for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
                    {
                        string name = skeleton.GetBoneName(bone);
                        if (name != "Jaw" && name != "Head" && !name.Contains("Blink") && !name.StartsWith("Lid")) continue;
                        var q = skeleton.GetBonePoseRotation(bone);
                        var p = skeleton.GetBonePosePosition(bone);
                        bones.Add(new { bone = name, x = q.X, y = q.Y, z = q.Z, w = q.W,
                            px = p.X, py = p.Y, pz = p.Z });
                    }
                if (frame % 6 == 0)
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(output, $"{phase}-{activity}-{frame:00}.png"));
                samples.Add(new { phase, frame, activity = activity.ToString(), actualClip = player.CurrentAnimation.ToString(),
                    actualTime = player.CurrentAnimationPosition, bones, determinant = renderer.GlobalBasis.Determinant() });
            }
            phase++;
        }
        File.WriteAllText(Path.Combine(output, "samples.json"), JsonSerializer.Serialize(samples,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task CaptureAnimationSweep(GuardianRenderer renderer, GuardianIndividual guardian, string dir)
    {
        if (renderer.FindChild("AnimationPlayer", true, false) is not AnimationPlayer player) return;
        var skeleton = renderer.FindChild("Skeleton3D", true, false) as Skeleton3D;
        string sweepDir = Path.Combine(dir, "animation-sweep"); Directory.CreateDirectory(sweepDir);
        var samples = new List<object>();
        foreach (string clip in new[] { "idle", "walk", "slither", "feed", "rest" })
        {
            if (!player.HasAnimation(clip)) continue;
            bool faceProbe = Main.UserArgs.Contains("--guardian-face-probe");
            if (faceProbe && clip is not ("idle" or "rest")) continue;
            guardian.Activity = clip switch { "walk" or "slither" => GuardianActivity.Explore, "feed" => GuardianActivity.Feed,
                "rest" => GuardianActivity.Rest, _ => GuardianActivity.Groom };
            // Allow the renderer's activity routing to settle before explicitly
            // sampling a clip; ProcessFrame can resume ahead of node _Process.
            for (int settle = 0; settle < 2; settle++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double duration = player.GetAnimation(clip).Length;
            int sampleFrames = faceProbe ? 2 : 12;
            for (int frame = 0; frame < sampleFrames; frame++)
            {
                double time = duration * frame / sampleFrames;
                skeleton?.ResetBonePoses();
                player.Play(clip, 0); player.Seek(time, true); player.Advance(0);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var feet = new List<object>();
                if (skeleton != null)
                    for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
                    {
                        string name = skeleton.GetBoneName(bone);
                        var p = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin;
                        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                            throw new InvalidOperationException($"Non-finite runtime bone {name} in {clip}@{time}");
                        if (name.Contains("Foot") || name.Contains("Hand"))
                            feet.Add(new { bone = name, x = p.X, y = p.Y, z = p.Z });
                    }
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                if (faceProbe) PangolinLidDump.Dump(renderer, Path.Combine(sweepDir, $"lid-bind-{clip}-{frame:00}.json"));
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(sweepDir, $"{clip}-{frame:00}.png"));
                if (Main.UserArgs.Contains("--guardian-face-sweep") && skeleton != null &&
                    skeleton.FindBone("Head") is int head && head >= 0 && GetViewport().GetCamera3D() is Camera3D camera)
                {
                    var oldTransform = camera.Transform; float oldSize = camera.Size;
                    var face = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head).Origin;
                    camera.Size = 0.45f; camera.Position = face + new Vector3(0.8f, 0.3f, 0.8f); camera.LookAt(face);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(Path.Combine(sweepDir, $"face-{clip}-{frame:00}.png"));
                    camera.Transform = oldTransform; camera.Size = oldSize;
                }
                var lids = new List<object>();
                if (skeleton != null)
                    for (int bone = 0; bone < skeleton.GetBoneCount(); bone++)
                        if (skeleton.GetBoneName(bone).Contains("Blink") || skeleton.GetBoneName(bone).StartsWith("Lid") || (faceProbe && skeleton.GetBoneName(bone) == "Head"))
                        {
                            var q = skeleton.GetBonePoseRotation(bone);
                            var p = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone).Origin;
                            var local = skeleton.GetBonePosePosition(bone);
                            lids.Add(new { bone = skeleton.GetBoneName(bone), x = q.X, y = q.Y, z = q.Z, w = q.W,
                                worldPosition = p.ToString(), localPosition = local.ToString(),
                                parent = skeleton.GetBoneParent(bone), rest = skeleton.GetBoneRest(bone).ToString(),
                                globalRest = skeleton.GetBoneGlobalRest(bone).ToString(), globalPose = skeleton.GetBoneGlobalPose(bone).ToString() });
                        }
                samples.Add(new { clip, time, duration, feet, lids, actualClip = player.CurrentAnimation.ToString(),
                    actualTime = player.CurrentAnimationPosition, determinant = renderer.GlobalBasis.Determinant() });
            }
        }
        File.WriteAllText(Path.Combine(sweepDir, "samples.json"), JsonSerializer.Serialize(samples,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RunPopulated()
    {
        string? species = Main.ArgAfter("--guardian-species");
        var definitions = species == null ? GuardianCatalog.All : GuardianCatalog.All.Where(d => d.Id == species || d.Archetype == species).ToArray();
        foreach (var def in definitions)
        {
            var descriptor = Content.PresetOrThrow("default").Clone();
            descriptor.PilotTreeId = def.AssociatedPilotTreeId;
            descriptor.GuardianId = def.Id;
            var world = VivariumWorld.Create(Content, descriptor);
            world.Clock.Paused = true;
            var session = new GameSession(); AddChild(session);
            session.Initialize(Content, new UserSettings { Transient = true, AutosaveEnabled = false,
                ShowHelpOnStart = false, Quality = 2 });
            session.Ui.Visible = false;
            session.CameraRig.ProcessMode = ProcessModeEnum.Disabled;
            await session.StartWorldAsync(world, null, _ => { });
            session.SetProcess(false);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!session.Flora.PopulationReady)
            {
                if (timer.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Guardian population review: " + def.Id);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            string dir = Path.Combine(OutDir, def.Archetype); Directory.CreateDirectory(dir);
            var records = new List<object>();
            float maximumFootSkin = 0, maximumBodySkin = 0, maximumTailPenetration = 0, maximumLengthError = 0;
            bool samplerValid = true;
            Vector3 reviewCameraOffset = Vector3.Zero;
            int reviewOcclusionScore = -1;
            world.Clock.Paused = false;
            for (int frame = 0; frame < 180; frame++)
            {
                world.Scheduler.Advance(1.0 / 30);
                var focus = session.Guardian.DisplayPosition + Vector3.Up * 0.45f;
                var inward = new Vector3((float)world.PilotTree!.Anchor.X - focus.X, 0, (float)world.PilotTree.Anchor.Z - focus.Z).Normalized();
                if (inward.LengthSquared() < .001f) inward = Vector3.Forward;
                session.CameraRig.LookAtPoint(focus + inward * 3.3f + inward.Cross(Vector3.Up) * 2.3f + Vector3.Up * 1.35f, focus);
                if (Main.UserArgs.Contains("--guardian-populated-closeup"))
                    session.CameraRig.LookAtPoint(focus + inward * 2.1f + inward.Cross(Vector3.Up) * 1.35f + Vector3.Up * .65f, focus);
                if (Main.UserArgs.Contains("--guardian-populated-exterior"))
                    session.CameraRig.LookAtPoint(focus - inward * 2.1f + inward.Cross(Vector3.Up) * 1.35f + Vector3.Up * .8f, focus);
                if (Main.UserArgs.Contains("--guardian-populated-clearview"))
                {
                    if (frame % 15 == 0)
                        (reviewCameraOffset, reviewOcclusionScore) = FindReviewCamera(session, world, focus);
                    session.CameraRig.LookAtPoint(focus + reviewCameraOffset, focus);
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (frame % 15 == 0)
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string file = Path.Combine(dir, $"populated-{frame:000}.png");
                    if (GetViewport().GetTexture().GetImage().SavePng(file) != Error.Ok) throw new IOException(file);
                }
                if (session.Guardian.LegContact is { } leg)
                {
                    maximumFootSkin = Math.Max(maximumFootSkin, leg.MaximumSkinFootPenetration);
                    maximumBodySkin = Math.Max(maximumBodySkin, leg.MaximumSkinBodyPenetration);
                    samplerValid &= session.Guardian.SkinContact?.Valid == true && leg.SkinFootSampleCount > 0;
                }
                if (session.Guardian.AppendageContact?.ContinuousGroundedBody == true)
                {
                    var bodyContact = session.Guardian.AppendageContact;
                    samplerValid &= bodyContact.ContinuousBodySampleCount > 0 && bodyContact.ContinuousHeadSampleCount > 0 && bodyContact.SkinTailSampleCount > 0;
                    maximumBodySkin = Math.Max(maximumBodySkin, Math.Max(-bodyContact.ContinuousBodyClearance, -bodyContact.ContinuousHeadClearance));
                }
                maximumLengthError = Math.Max(maximumLengthError, session.Guardian.AppendageContact?.MaximumLengthError ?? 0);
                maximumTailPenetration = Math.Max(maximumTailPenetration, Math.Max(session.Guardian.AppendageContact?.MaximumGroundPenetration ?? 0,
                    session.Guardian.AppendageContact?.MaximumSkinTailPenetration ?? 0));
                records.Add(new { frame, world.Clock.Tick, activity = world.Guardian!.Activity.ToString(),
                        position = world.Guardian.Position, shown = session.Guardian.DisplayPosition.ToString(),
                        loadedFloraSpecies = session.Flora.LoadedSpeciesCount, worldFauna = world.Fauna.Count,
                        reviewCameraOffset = reviewCameraOffset.ToString(), reviewOcclusionScore,
                        tailPenetration = session.Guardian.AppendageContact?.MaximumGroundPenetration,
                        tailLengthError = session.Guardian.AppendageContact?.MaximumLengthError,
                        tailDeflection = session.Guardian.AppendageContact?.MaximumContactDisplacement,
                        skinTailPenetration = session.Guardian.AppendageContact?.MaximumSkinTailPenetration,
                        skinTailSampleCount = session.Guardian.AppendageContact?.SkinTailSampleCount,
                        continuousBodySkinClearance = session.Guardian.AppendageContact?.ContinuousGroundedBody == true ? (float?)session.Guardian.AppendageContact.ContinuousBodyClearance : null,
                        continuousHeadSkinClearance = session.Guardian.AppendageContact?.ContinuousGroundedBody == true ? (float?)session.Guardian.AppendageContact.ContinuousHeadClearance : null,
                        tailBendDeviation = session.Guardian.AppendageContact?.MaximumBendDeviation,
                        footPenetration = session.Guardian.LegContact?.MaximumSolePenetration,
                        footReachError = session.Guardian.LegContact?.MaximumReachError,
                        footRetargetDistance = session.Guardian.LegContact?.MaximumFootRetargetDistance,
                        stanceFootTravel = session.Guardian.LegContact?.MaximumStanceFootTravel,
                        bodyHeightAdjustment = session.Guardian.LegContact?.BodyHeightAdjustment,
                        bodyPenetration = session.Guardian.LegContact?.MaximumBodyPenetration,
                        skinFootPenetration = session.Guardian.LegContact?.MaximumSkinFootPenetration,
                        skinBodyPenetration = session.Guardian.LegContact?.MaximumSkinBodyPenetration,
                        skinFootSampleCount = session.Guardian.LegContact?.SkinFootSampleCount,
                        stanceFootGap = session.Guardian.LegContact?.MaximumStanceFootGap,
                        skinSamplerValid = session.Guardian.SkinContact?.Valid,
                        contactPlantCount = session.Guardian.ShrubContact?.CachedPlantCount,
                        session.Guardian.BodyTrunkCorrection,
                        shrubContacts = session.Guardian.ShrubContact?.ContactCount,
                        shrubBend = session.Guardian.ShrubContact?.MaximumBend });
            }
            File.WriteAllText(Path.Combine(dir, "populated-motion.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            if (Main.UserArgs.Contains("--guardian-contact-review") && (session.Guardian.LegContact != null || session.Guardian.AppendageContact != null) &&
                (!samplerValid || maximumFootSkin > .025f || maximumBodySkin > .025f || maximumTailPenetration > .025f || maximumLengthError > .04f))
                throw new InvalidOperationException($"Populated contact failed {def.Archetype}: skinValid={samplerValid}, foot={maximumFootSkin}, body={maximumBodySkin}, tail={maximumTailPenetration}, length={maximumLengthError}. Evidence saved in {dir}.");
            GD.Print("GUARDIAN_POPULATED_SPECIES_OK " + def.Id);
            session.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("GUARDIAN_POPULATED_REVIEW_OK " + OutDir);
    }

    // Review-only search; preserve vegetation and animal motion while selecting
    // the least obstructed view using rendered plant bounds and woody geometry.
    private static (Vector3 offset, int score) FindReviewCamera(GameSession session, VivariumWorld world, Vector3 focus)
    {
        var bounds = new List<Aabb>(); int scanned = 0;
        foreach (var (draw, index) in session.Flora.GuardianContactInstances())
        {
            if (++scanned > 20000) break;
            if (!GodotObject.IsInstanceValid(draw) || draw.Multimesh?.Mesh == null || index >= draw.Multimesh.InstanceCount) continue;
            var transform = draw.GlobalTransform * draw.Multimesh.GetInstanceTransform(index);
            var box = transform * draw.Multimesh.Mesh.GetAabb();
            if (box.GetCenter().DistanceSquaredTo(focus) < 100 && box.Size.Y > .15f) bounds.Add(box);
        }
        Vector3 best = new Vector3(3, 1.3f, 0); int bestScore = int.MaxValue;
        for (int elevation = 0; elevation < 2; elevation++)
            for (int angle = 0; angle < 16; angle++)
            {
                float theta = angle * Mathf.Tau / 16;
                var offset = new Vector3(MathF.Cos(theta) * 3, elevation == 0 ? 1.1f : 1.8f, MathF.Sin(theta) * 3);
                var camera = focus + offset; int score = 0;
                for (int ray = -1; ray <= 1; ray++)
                {
                    var target = focus + new Vector3(ray * .25f, 0, ray * .15f);
                    var end = target + offset.Normalized() * .15f;
                    foreach (var box in bounds) if (box.IntersectsSegment(camera, end)) score++;
                    if (world.PilotTree != null)
                        for (int sample = 0; sample < 12; sample++)
                        {
                            var point = camera.Lerp(end, sample / 12f);
                            if (world.PilotTree.Penetration(new Vivarium.Sim.Core.Vec3(point.X, point.Y, point.Z), .04).Depth > 0) score += 100;
                        }
                }
                if (score < bestScore) { bestScore = score; best = offset; }
            }
        return (best, bestScore);
    }

    private static IEnumerable<MeshInstance3D> DescendantMeshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren())
            foreach (var descendant in DescendantMeshes(child)) yield return descendant;
    }
}

