using System;
using System.IO;
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
    public override void _Ready() => _ = CaptureAsync();
    private async Task CaptureAsync()
    {
        try
        {
            GetWindow().Size = new Vector2I(960, 640);
            var species = SpeciesId == "all" ? Content.Fauna : SpeciesId == "rest"
                ? Content.Fauna.Where(s => s.Id is not ("rustcoil" or "rainspine" or "stonebell" or "stiltclaw" or "dewmantle" or "loamthread")).ToArray()
                : new[] { Content.FaunaOrThrow(SpeciesId) };
            foreach (var sp in species)
            {
                string dir = SpeciesId is "all" or "rest" ? Path.Combine(OutDir, sp.Id) : OutDir;
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
        if (sp.Model == "toad") mat.SetShaderParameter("preview_time", 0.0f);
        ulong seed = Rng.Mix(Hash.Fnv1a64("fauna.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true, Mesh = Bridge.ToArrayMesh(OrganismMeshes.Fauna(sp, seed), mat, signedVertexData: true), InstanceCount = 1 };
        mm.SetInstanceTransform(0, Transform3D.Identity);
        stage.AddChild(new MultiMeshInstance3D { Multimesh = mm });
        stage.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(14, 4) }, Position = new Vector3(3, -0.025f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.31f, 0.27f), Roughness = 1 } });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Math.Max(1.55f, mm.Mesh.GetAabb().Size.X * 1.25f), Current = true };
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
        if (sp.Model == "toad" && !SpeedReview)
        {
            camera.Position = new Vector3(0.72f, 0.62f, 1.35f);
            camera.LookAt(new Vector3(0, 0.17f, 0));
            // Review inherited palettes and mottling independently of morphology/draw order.
            for (int individual = 0; individual < 6; individual++)
            {
                float hue = -0.07f + individual * 0.026f;
                float density = 0.24f + individual * 0.10f;
                float contrast = 0.35f + individual * 0.09f;
                mm.SetInstanceCustomData(0, new Color(hue, density, contrast, 16000));
                await CaptureFrameAsync(dir, $"individual-{individual:00}.png", 3);
            }
            mm.SetInstanceCustomData(0, new Color(0, 0.5f, 0.5f, 16000));
            for (int frame = 0; frame < 90; frame++)
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
            const double dt = 1.0 / 30;
            for (int frame = 0; frame < (sp.Model == "toad" && !SpeedReview ? 120 : 60); frame++)
            {
                float rate = SpeedReview && frame >= 45 ? 0 : speed;
                double step = rate * dt; distance += step;
                phase = (phase + FaunaGait.Advance(a, step, 1, dt)) % 1;
                float bend = SpeedReview ? 0 : (float)Math.Sin(frame / 60.0 * Math.Tau);
                float activity = FaunaGait.GroundSteps(a) ? (rate > 0 ? 1 : 0) : (float)Math.Clamp(rate / a.FullSpeed, 0, 1);
                int bucket = (int)Math.Round(activity * 15);
                mm.SetInstanceCustomData(0, new Color(FaunaBodyProfiles.PackHue(sp, 0, bend), 0.5f, 0.5f, 16000 + bucket + (float)Math.Min(phase, 0.999)));
                var position = new Vector3((float)distance, 0, 0);
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
            idleFrames = sp.Model == "toad" && !SpeedReview ? 90 : 0, appearanceSamples = sp.Model == "toad" && !SpeedReview ? 6 : 0,
            cyclesPerBody = a.CyclesPerBody, halfStroke = FaunaGait.HalfStroke(a),
            evidence = "Scripted mesh/material review; production interpolation checked separately with --fauna-motion-check" }));
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
