using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Opt-in, isolated runtime capture for repeatable Forward+ visual/performance reviews.</summary>
public static class VisualReviewRunner
{
    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = 60;
        world.Clock.Paused = true;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        // The tools layer re-shows the green cursor torus on its next _Process tick, which
        // would photobomb every review frame. Stop that node and hide the overlay outright.
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;
        var output = new Dictionary<string, object>();
        int floraSettleFrames = 0;
        var floraSettleTimer = Stopwatch.StartNew();
        double lastProgressSeconds = 0;
        // The full seeded species roster can publish more slowly than one minute on lower-end GPUs.
        // This bounded wait guarantees captures compare complete flora layers instead of startup progress.
        while (!session.Flora.PopulationReady && floraSettleFrames < 240000 && floraSettleTimer.Elapsed.TotalSeconds < 180)
        {
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            floraSettleFrames++;
            if (floraSettleTimer.Elapsed.TotalSeconds >= lastProgressSeconds + 10)
            {
                lastProgressSeconds = floraSettleTimer.Elapsed.TotalSeconds;
                GD.Print($"VISUAL_REVIEW_FLORA_PROGRESS loaded={session.Flora.LoadedSpeciesCount}/{world.Content.Flora.Count} elapsed_s={lastProgressSeconds:0}");
            }
        }
        if (!session.Flora.PopulationReady)
            throw new InvalidOperationException($"Flora population did not become ready within {floraSettleTimer.Elapsed.TotalSeconds:0.0}s; published {session.Flora.LoadedSpeciesCount}/{world.Content.Flora.Count} species.");
        float d = (float)world.Descriptor.Diameter;
        var shots = new[]
        {
            (name: "overview", eye: new Vector3(0, 18, 23), target: new Vector3(0, 7, 0)),
            (name: "foliage_terrain", eye: new Vector3(-2, 1.2f, 3.8f), target: new Vector3(-1, .4f, 1.8f)),
            (name: "grazing_transition", eye: new Vector3(d * .04f, d * .07f, d * .40f), target: new Vector3(-d * .08f, d * .04f, 0)),
        };
        foreach (var shot in shots)
        {
            session.CameraRig.LookAtPoint(shot.eye, shot.target);
            var frames = new List<double>(240);
            var timer = Stopwatch.StartNew();
            long last = timer.ElapsedTicks;
            // Let the camera-local detail stream settle before warmup and measurement.
            for (int i = 0; i < 180; i++) await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            int settleFrames = 0;
            while (!session.Flora.PopulationReady && settleFrames < 1200)
            {
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                settleFrames++;
            }
            if (!session.Flora.PopulationReady)
                throw new InvalidOperationException($"Flora population did not become ready for {shot.name} within 1200 frames.");
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            last = timer.ElapsedTicks;
            for (int i = 0; i < 240; i++)
            {
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                long now = timer.ElapsedTicks;
                frames.Add((now - last) * 1000.0 / Stopwatch.Frequency);
                last = now;
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = main.GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(directory, shot.name + ".png"));
            frames.Sort();
            output[shot.name] = new
            {
                warmup_frames = 180,
                flora_settle_frames = settleFrames,
                measured_frames = frames.Count,
                frame_ms_median = Percentile(frames, .50),
                frame_ms_p95 = Percentile(frames, .95),
                frame_ms_p99 = Percentile(frames, .99),
                fps_median = 1000.0 / Math.Max(.001, Percentile(frames, .50)),
                draw_calls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
                primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
                objects = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame),
                camera_position = new[] { shot.eye.X, shot.eye.Y, shot.eye.Z },
                camera_target = new[] { shot.target.X, shot.target.Y, shot.target.Z },
                viewport = new[] { image.GetWidth(), image.GetHeight() },
                screenshot = shot.name + ".png"
            };
        }
        File.WriteAllText(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(new
        {
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            adapter = RenderingServer.GetVideoAdapterName(),
            seed = world.Seed,
            preset = world.Descriptor.Name,
            quality = session.Settings.Quality,
            vsync = "disabled",
            daylight_seconds = 60,
            flora_population_ready = session.Flora.PopulationReady,
            flora_settle_frames = floraSettleFrames,
            species_counts = world.Flora.Items.GroupBy(p => p.SpeciesId).ToDictionary(g => g.Key, g => g.Count()),
            timing_scope = "end-to-end frame intervals including CPU scheduling and GPU presentation; not isolated CPU or GPU time",
            residual_animation = "Simulation clock paused; shader TIME animation may continue because this Godot build exposes no global shader time freeze.",
            settings = new { dynamic_shadows = session.Settings.DynamicShadows, global_illumination = session.Settings.GlobalIllumination, ambient_occlusion = session.Settings.AmbientOcclusion, volumetric_mist = session.Settings.VolumetricMist, distance_blur = session.Settings.DistanceBlur, render_scale = session.Settings.RenderScale },
            flora = world.Flora.Count,
            fauna = world.Fauna.Count,
            captures = output
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("VISUAL_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit();
    }

    private static double Percentile(IReadOnlyList<double> sorted, double q)
        => Math.Round(sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * q) - 1, 0, sorted.Count - 1)], 3);
}
