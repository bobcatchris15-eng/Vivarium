using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Isolated physical soil foundation review using the production island and litter renderers.</summary>
public static class SoilSurfaceReviewRunner
{
    public static async Task Capture(Main host, ContentLibrary content, string directory)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 30;
        var descriptor = new WorldDescriptor { Diameter = Main.UserArgs.Contains("--soil-large") ? 17 : 5, CellSize = .1, Seed = 804584441,
            PilotTreeId = "none", GuardianId = "none",
            Terrain = new TerrainProfile { BaseHeight = .3, Relief = 0, MicroRelief = 0, SoilSurfaceVersion = Main.UserArgs.Contains("--soil-legacy") ? 0 : 1, SoilSurfaceAmplitude = Main.UserArgs.Contains("--soil-legacy") ? 0 : .020 },
            Water = new WaterConfig { WaterTable = 0, Infiltration = 0, Evaporation = 0 } };
        var world = VivariumWorld.CreateBaseline(content, descriptor);
        foreach (int c in world.Grid.DomainCells)
        {
            world.Fields.Moisture[c] = .4;
            world.Litter.FineMass[c] = world.Grid.CellCenter(c).X < 0 ? .035 : .001;
        }
        world.Clock.Paused = true;
        var session = new GameSession { Name = "SoilSurfaceReview" }; host.AddChild(session);
        session.Initialize(content, new UserSettings { Transient = true, AutosaveEnabled = false, ShowHelpOnStart = false,
            LightingMode = "cached", MaxFps = 30 });
        await session.StartWorldAsync(world, null, stage => GD.Print("SOIL_REVIEW_STAGE " + stage));
        session.SetProcess(false); session.CameraRig.SetProcess(false); session.CameraRig.SetProcessUnhandledInput(false);
        session.Tools.SetProcess(false); session.Ui.Visible = session.Overlay.Visible = false;
        session.AmbientGround.Visible = false; session.SoilDetail.SetProcess(false);
        foreach (var child in session.SoilDetail.GetChildren().OfType<Node3D>())
            if (child != session.SoilDetail.LoamDetails) child.Visible = false;
        session.EnvRig.BenchmarkLightSeconds = 60; session.EnvRig.CameraAttributes.AutoExposureEnabled = false;
        var shots = new[] {
            ("soil_macro", new Vector3(.45f,.43f,.35f), new Vector3(.2f,.3f,0)),
            ("soil_oblique", new Vector3(1.2f,.65f,1.1f), new Vector3(.4f,.3f,0)),
            ("litter_macro", new Vector3(-.4f,.48f,.4f), new Vector3(-.6f,.3f,0)),
            ("surface_overview", new Vector3((float)descriptor.Diameter*.48f,(float)descriptor.Diameter*.5f,(float)descriptor.Diameter*.6f), new Vector3(0,.3f,0)) };
        var metrics = new List<object>();
        var rid = host.GetViewport().GetViewportRid(); RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        foreach (var (name, eye, target) in shots)
        {
            session.CameraRig.Cam.Fov = 45; session.CameraRig.Cam.Near = .01f;
            session.CameraRig.LookAtPoint(eye, target);
            var transition = new List<double>(); long previous = Stopwatch.GetTimestamp();
            for (int i = 0; i < 90; i++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                long now = Stopwatch.GetTimestamp(); transition.Add(Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds); previous=now;
            }
            var frames = new List<double>(); var gpu = new List<double>(); var draws = new List<double>();
            for (int i = 0; i < 90; i++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                long now = Stopwatch.GetTimestamp(); frames.Add(Stopwatch.GetElapsedTime(previous,now).TotalMilliseconds); previous=now;
                double g = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid); if(g>0) gpu.Add(g);
                draws.Add(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame));
            }
            frames.Sort();
            metrics.Add(new { name, frame_p95_ms=frames[(int)(frames.Count*.95)], gpu_mean_ms=gpu.Count==0?0:gpu.Average(),
                draws_mean=draws.Average(), transition_max_ms=transition.Max(), builds=session.Island.SoilSurfaceRenderer?.BuildsCompleted,
                unchanged_requests=session.Island.SoilSurfaceRenderer?.UnchangedRequests });
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = host.GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(directory, name + ".png"));
            GD.Print("SOIL_REVIEW_SHOT " + name);
        }
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new {
            version = world.Terrain.SoilSurfaceVersion, amplitude = world.Terrain.SoilSurfaceVersion==0 ? 0 : world.Terrain.SoilSurface.Parameters.Amplitude, spacing = world.Terrain.SoilSurfaceVersion==0 ? world.Terrain.Step/3 : world.Terrain.SoilSurface.Spacing,
            terrain_triangles = session.Island.TriangleCount,
            tiles = session.Island.SoilSurfaceRenderer?.TileCount,
            builds = session.Island.SoilSurfaceRenderer?.BuildsCompleted,
            last_build_ms = session.Island.SoilSurfaceRenderer?.LastBuildMilliseconds,
            last_group_payload_bytes = session.Island.SoilSurfaceRenderer?.LastGroupPayloadBytes,
            cache_hits = session.Island.SoilSurfaceRenderer?.CacheHits,
            fine_cache_tiles = world.Terrain.SoilSurfaceVersion==0 ? 0 : world.Terrain.SoilSurface.CachedTileCount,
            metrics, assembly_id = typeof(SoilSurfaceReviewRunner).Module.ModuleVersionId,
            diameter = descriptor.Diameter,
            notes = "Foundation experiment, no user save. Shared physical/query/render lattice; detached peds disabled. No broad fidelity/performance acceptance."
        }, new JsonSerializerOptions { WriteIndented = true }));
        host.GetTree().Quit(0);
    }
}
