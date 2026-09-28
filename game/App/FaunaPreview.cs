using System;
using System.IO;
using System.Collections.Generic;
using Vivarium.Sim.World;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.App;

/// <summary>Production mesh/material poses and distance-driven travel previews in a disposable scene.</summary>
public partial class FaunaPreview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string SpeciesId { get; set; } = "rustcoil";
    public string OutDir { get; set; } = "";
    public bool SpeedReview { get; set; }
    public bool LiveMotion { get; set; }
    public override void _Ready() => _ = CaptureAsync();
    private async Task CaptureAsync()
    {
        try
        {
            GetWindow().Size = new Vector2I(960, 640);
            if (LiveMotion)
            {
                var liveSpecies = SpeciesId == "large" ? Content.Fauna.Where(s => s.Id is "siltshield" or "stiltclaw" or "rustcoil"
                    or "loamthread" or "dewmantle" or "glasscoil" or "reedjaw" or "moonveil").ToArray()
                    : new[] { Content.FaunaOrThrow(SpeciesId) };
                foreach (var sp in liveSpecies)
                {
                    string dir = SpeciesId == "large" ? Path.Combine(OutDir, sp.Id) : OutDir;
                    await CaptureLiveMotionAsync(sp, dir);
                    GD.Print("FAUNA_LIVE_SPECIES_OK " + sp.Id);
                }
                GD.Print("FAUNA_LIVE_MOTION_OK " + Path.GetFullPath(OutDir));
                GetTree().Quit();
                return;
            }
            var species = SpeciesId == "large" ? Content.Fauna.Where(s => s.Id is "siltshield" or "stiltclaw" or "rustcoil"
                or "loamthread" or "dewmantle" or "glasscoil" or "reedjaw" or "moonveil").ToArray()
                : SpeciesId == "all" ? Content.Fauna : SpeciesId == "rest"
                ? Content.Fauna.Where(s => s.Id is not ("rustcoil" or "rainspine" or "stonebell" or "stiltclaw" or "dewmantle" or "loamthread")).ToArray()
                : new[] { Content.FaunaOrThrow(SpeciesId) };
            foreach (var sp in species)
            {
                string dir = SpeciesId is "all" or "rest" or "large" ? Path.Combine(OutDir, sp.Id) : OutDir;
                await CaptureSpeciesAsync(sp, dir);
                GD.Print("FAUNA_PREVIEW_OK " + sp.Id + " " + Path.GetFullPath(dir));
            }
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("FAUNA_PREVIEW_FAILED " + ex); GetTree().Quit(1); }
    }

    private async Task CaptureSpeciesAsync(FaunaSpeciesDef sp, string dir)
    {
        var stage = new Node3D(); AddChild(stage);
        stage.AddChild(new EnvironmentRig());
        var a = sp.Animation;
        var mat = Bridge.Shader("res://Shaders/fauna.gdshader");
        mat.SetShaderParameter("base_color", Bridge.C(sp.BaseColor));
        mat.SetShaderParameter("ornament_color", Bridge.C(sp.OrnamentColor));
        FaunaBodyProfiles.Bind(mat, sp);
        if (sp.Model is "toad" or "salamander") mat.SetShaderParameter("preview_time", 0.0f);
        ulong seed = Rng.Mix(Hash.Fnv1a64("fauna.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true, Mesh = Bridge.ToArrayMesh(OrganismMeshes.Fauna(sp, seed), mat, signedVertexData: true), InstanceCount = 1 };
        mm.SetInstanceTransform(0, Transform3D.Identity);
        stage.AddChild(new MultiMeshInstance3D { Multimesh = mm });
        stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(14, 4) }, Position = new Vector3(3, -0.025f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.27f), Roughness = 1 } });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Math.Max(1.55f, Math.Max(mm.Mesh.GetAabb().Size.X, mm.Mesh.GetAabb().Size.Z) * 1.35f), Current = true };
        stage.AddChild(camera);
        Directory.CreateDirectory(dir);
        if (!SpeedReview)
        {
            foreach (int side in new[] { 1, -1 })
            {
                camera.Position = new Vector3(0.65f, 1.1f, side * 1.5f);
                camera.LookAt(new Vector3(0, 0.06f, 0));
                foreach (int turn in new[] { 0, -1, 1 })
                foreach (float phase in new[] { 0.05f, 0.35f, 0.65f, 0.85f })
                {
                    mm.SetInstanceCustomData(0, new Color(FaunaBodyProfiles.PackHue(sp, 0, turn), 0.5f, 0.5f, 16015 + phase));
                    await CaptureFrameAsync(dir, $"{sp.Id}-side{side}-turn{turn}-phase{phase:0.00}.png", 3);
                }
            }
        }
        if (!SpeedReview)
        {
            camera.Position = new Vector3(0.72f, 0.62f, 1.35f);
            camera.LookAt(new Vector3(0, 0.17f, 0));
            // Review inherited palettes and mottling independently of morphology/draw order.
            for (int individual = 0; individual < (sp.Model == "salamander" ? 12 : 6); individual++)
            {
                float hue = -0.08f + individual * 0.014f;
                float density = 0.15f + individual * 0.065f;
                float contrast = 0.30f + individual * 0.055f;
                mm.SetInstanceCustomData(0, new Color(FaunaBodyProfiles.PackHue(sp, hue, 0), density, contrast, 16000));
                await CaptureFrameAsync(dir, $"individual-{individual:00}.png", 3);
            }
            mm.SetInstanceCustomData(0, new Color(FaunaBodyProfiles.PackHue(sp, 0, 0), 0.5f, 0.5f, 16000));
            for (int frame = 0; frame < (sp.Model is "toad" or "salamander" ? 90 : 0); frame++)
            {
                mat.SetShaderParameter("preview_time", frame / 30.0f);
                await CaptureFrameAsync(dir, $"idle-{frame:000}.png", 1);
            }
        }
        // Fixed markers reveal translation relative to the substrate while the camera follows the animal.
        for (int i = -12; i <= 36; i++) stage.AddChild(new MeshInstance3D {
            Mesh = new BoxMesh { Size = new Vector3(0.018f, 0.004f, 1.0f) }, Position = new Vector3(i * 0.25f, -0.021f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.39f, 0.34f), Roughness = 1 } });
        foreach (float speed in SpeedReview ? new[] { 0.2f, 0.8f } : new[] { 0.6f })
        {
            double phase = 0, distance = 0;
            var toad = sp.Model == "toad" ? new ToadLocomotion() : null;
            toad?.Reset(Vec3.Zero);
            const double dt = 1.0 / 30;
            for (int frame = 0; frame < (sp.Model == "toad" && !SpeedReview ? 120 : 60); frame++)
            {
                float rate = SpeedReview && frame >= 45 ? 0 : speed;
                double step = rate * dt; distance += step;
                phase = (phase + FaunaGait.Advance(a, step, 1, dt)) % 1;
                float bend = SpeedReview ? 0 : (float)Math.Sin(frame / 60.0 * Math.Tau);
                float activity = FaunaGait.GroundSteps(a) ? (rate > 0 ? 1 : 0) : (float)Math.Clamp(rate / a.FullSpeed, 0, 1);
                var position = new Vector3((float)distance, 0, 0);
                if (toad != null)
                {
                    toad.Step(new Vec3(distance, 0, 0), 1, dt, rate, a);
                    phase = toad.Phase;
                    activity = toad.IsCrawling ? activity : toad.Hopping ? 1 : 0;
                    position = new Vector3((float)toad.Position.X, (float)toad.Position.Y, (float)toad.Position.Z);
                }
                int bucket = (int)Math.Round(activity * 15);
                mm.SetInstanceCustomData(0, new Color(FaunaBodyProfiles.PackHue(sp, 0, bend, toad?.IsCrawling == true), 0.5f, 0.5f, 16000 + bucket + (float)Math.Min(phase, 0.999)));
                mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, position));
                camera.Position = position + new Vector3(0.4f, 1.6f, 1.2f);
                camera.LookAt(position + new Vector3(0, 0.06f, 0));
                string prefix = SpeedReview ? (speed < 0.5f ? "slow" : "fast") : "motion";
                await CaptureFrameAsync(dir, $"{prefix}-{frame:000}.png", 1);
            }
        }
        File.WriteAllText(Path.Combine(dir, "preview.json"), System.Text.Json.JsonSerializer.Serialize(new {
            species = sp.Id, distanceDriven = true, fps = 30, bodyLengthsPerSecond = SpeedReview ? new[] { 0.2, 0.8 } : new[] { 0.6 },
            stopAtFrame = SpeedReview ? 45 : -1, travelFrames = sp.Model == "toad" && !SpeedReview ? 120 : 60,
            idleFrames = sp.Model is "toad" or "salamander" && !SpeedReview ? 90 : 0, appearanceSamples = !SpeedReview ? sp.Model == "salamander" ? 12 : 6 : 0,
            cyclesPerBody = a.CyclesPerBody, halfStroke = FaunaGait.HalfStroke(a),
            evidence = "Scripted mesh/material review; production interpolation checked separately with --fauna-motion-check" }));
        stage.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task CaptureLiveMotionAsync(FaunaSpeciesDef sp, string dir)
    {
        var stage = new Node3D(); AddChild(stage);
        var descriptor = new WorldDescriptor { PresetId = "amphibian-live", Seed = 42, Diameter = 10, CellSize = 0.25,
            Terrain = new TerrainProfile { BaseHeight = 0, Relief = 0, Bottom = -2 },
            Water = new WaterConfig { WaterTable = sp.Medium == Medium.Aquatic ? 0.15 : -1 },
            Placement = new PlacementProfile { Rocks = 0, Logs = 0, GravelPatches = 0 } };
        var world = VivariumWorld.Create(Content, descriptor, populate: false);
        foreach (int c in world.Grid.DomainCells) world.Fields.Moisture[c] = sp.Moisture.Optimum;
        var animal = world.FaunaSystem.CreateFounder(sp, Vec2.Zero);
        animal.Heading = 0;
        var ph = world.FaunaSystem.PhenotypeOf(animal);
        float length = (float)(ph.BodySize * sp.VisualScale);
        stage.AddChild(new EnvironmentRig());
        var renderer = new FaunaRenderer { ProcessMode = ProcessModeEnum.Disabled };
        stage.AddChild(renderer); renderer.Build(world);
        stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(4, 4) }, Position = new Vector3(0, -0.002f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.27f), Roughness = 1 } });
        for (int i = -40; i <= 40; i++) stage.AddChild(new MeshInstance3D {
            Mesh = new BoxMesh { Size = new Vector3(length * 0.01f, 0.0003f, length * 2) },
            Position = new Vector3(i * length * 0.25f, -0.0015f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.43f, 0.41f, 0.35f), Roughness = 1 } });
        var bounds = OrganismMeshes.Fauna(sp).Bounds();
        float framing = (float)Math.Max(1.55, Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Z - bounds.Min.Z) * 1.35);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = length * framing,
            Near = 0.001f, Current = true };
        stage.AddChild(camera);
        const double dt = 1.0 / 30;
        world.Scheduler.WallBudgetMs = 0;
        renderer._Process(dt);
        Directory.CreateDirectory(dir);
        var trace = new List<object>();
        for (int frame = 0; frame < 240; frame++)
        {
            double rate = frame < 90 ? 0.2 : frame < 180 ? 1.4 : 0;
            world.Clock.BaseRate = rate * length / (sp.Speed * ph.SpeedScale);
            world.Clock.Paused = rate == 0;
            animal.Energy = sp.MaxEnergy;
            world.Scheduler.Advance(dt); // Actual ecology movement, scheduler fraction and production renderer.
            renderer._Process(dt);
            var shown = renderer.DisplayPosition(animal.Id);
            camera.Position = shown + new Vector3(0.45f, 0.72f, 1.35f) * length;
            camera.LookAt(shown + new Vector3(0, length * 0.15f, 0));
            trace.Add(new { frame, phase = renderer.DisplayPhase(animal.Id), crawl = renderer.DisplayCrawling(animal.Id),
                shown = new[] { shown.X, shown.Y, shown.Z }, target = new[] { animal.X, animal.Y, animal.Z } });
            await CaptureFrameAsync(dir, $"live-{frame:000}.png", 1);
        }
        File.WriteAllText(Path.Combine(dir, "live-motion.json"), System.Text.Json.JsonSerializer.Serialize(new {
            fps = 30, bodyLength = length, cyclesPerBody = sp.Animation.CyclesPerBody,
            input = "FaunaSystem + Scheduler + FaunaRenderer", slowFrames = 90, fastFrames = 90, stoppedFrames = 60, trace }));
        stage.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CaptureFrameAsync(string dir, string name, int settle)
    {
        for (int i = 0; i < settle; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.GetFullPath(Path.Combine(dir, name));
        if (GetViewport().GetTexture().GetImage().SavePng(path) != Error.Ok) throw new IOException(path);
    }
}
