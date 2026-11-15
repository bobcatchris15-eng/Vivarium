using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Repr.Water;
using Vivarium.Sim.World;
using SVec2 = Vivarium.Sim.Core.Vec2;

namespace Vivarium.Game.App;

/// <summary>Controlled production-renderer review of a mature high-flow spring and its receiving basin.</summary>
public static class WaterReviewRunner
{
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, IncludeFields = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static async Task Frames(Main host, int count)
    {
        for (int i = 0; i < count; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static void DebugMode(WaterSurfaceRenderer water, int mode)
    {
        foreach (var node in water.GetChildren().OfType<MeshInstance3D>())
            if (node.MaterialOverride is ShaderMaterial material) material.SetShaderParameter("water_debug", mode);
    }
    public static async Task Capture(Main host, ContentLibrary content, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 30;
        var preparation = Task.Run(() =>
        {
            var fixture = MatureSpringFixture.Create(true); fixture.SeedPool();
            var result = fixture.Mature();
            // Compare identical physical ages even when transport gates settle earlier.
            if (fixture.Dynamics.Time < 60)
                fixture.Dynamics.Advance(60 - fixture.Dynamics.Time, new[] { fixture.Spring }, fixture.Budget);
            return (Fixture: fixture, Result: result with { State = fixture.Dynamics.Export() });
        });
        var clock = Stopwatch.StartNew(); double nextProgress = 0;
        while (!preparation.IsCompleted)
        {
            if (clock.Elapsed.TotalSeconds >= nextProgress)
            { GD.Print($"WATER_REVIEW_PREPARING elapsed_s={clock.Elapsed.TotalSeconds:0}"); nextProgress += 10; }
            await Frames(host, 1);
        }
        var ready = await preparation;
        File.WriteAllText(Path.Combine(directory, "initial-state.json"), JsonSerializer.Serialize(ready.Result, Json));
        var world = VivariumWorld.CreateBaseline(content, ready.Fixture.Descriptor);
        MatureSpringFixture.ApplyTerrain(world.Terrain, true); world.Water.RefreshBed(world.Terrain);
        world.Water.Springs.Add(ready.Fixture.Spring); world.Water.Budget = ready.Result.Budget;
        world.Liquid.Erosion.Enabled = false; world.Liquid.Restore(ready.Result.State); world.Clock.Paused = true;
        var session = new GameSession { Name = "WaterReviewSession" }; host.AddChild(session);
        session.Initialize(content, new UserSettings { Transient = true, AutosaveEnabled = false,
            ShowHelpOnStart = false, LightingMode = "cached", MaxFps = 30 });
        session.WaterFluid.CheapRefraction = false;
        await session.StartWorldAsync(world, null, stage => GD.Print("WATER_REVIEW_STAGE " + stage));
        session.SetProcess(false); session.CameraRig.SetProcess(false); session.CameraRig.SetProcessUnhandledInput(false);
        session.Tools.SetProcess(false); session.Ui.Visible = false; session.Overlay.Visible = false;
        session.AmbientGround.Visible = false; session.SoilDetail.Visible = false;
        session.Flora.Visible = false; session.Fauna.Visible = false; session.Guardian.Visible = false;
        var driver = new WaterReviewFluidDriver { World = world }; host.AddChild(driver);
        session.EnvRig.BenchmarkLightSeconds = 60; session.EnvRig.CameraAttributes.AutoExposureEnabled = false;
        host.GetViewport().GuiDisableInput = true;
        world.Clock.Paused = false; // With Session processing disabled, only optical presentation advances here.
        string? pointsFile = System.Environment.GetEnvironmentVariable("WATER_REVIEW_POINTS");
        var initialCells = world.Liquid.CaptureRenderState().Cells;
        double spacing = ready.Fixture.Dynamics.Spacing;
        var byPosition = initialCells.ToDictionary(c => ((int)Math.Round(c.Position.X / spacing - .5), (int)Math.Round(c.Position.Z / spacing - .5)));
        var points = pointsFile != null ? JsonSerializer.Deserialize<double[][]>(File.ReadAllText(pointsFile))! :
            initialCells.Where(c => c.Position.Length < .12 && c.Position.X > -.05 &&
                Enumerable.Range(-1, 3).All(dx => Enumerable.Range(-1, 3).All(dz =>
                    byPosition.TryGetValue(((int)Math.Round(c.Position.X / spacing - .5) + dx,
                        (int)Math.Round(c.Position.Z / spacing - .5) + dz), out var neighbour) && neighbour.Depth >= .004)))
                .Select(c => new[] { c.Position.X, c.Position.Z }).ToArray();
        if (points.Length == 0) throw new InvalidOperationException("No fixed wet-core query points.");
        File.WriteAllText(Path.Combine(directory, "query-points.json"), JsonSerializer.Serialize(points, Json));
        var viewport = host.GetViewport(); var rid = viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(rid, true);
        var reports = new List<object>();
        async Task Shot(string name, Vector3 eye, Vector3 target, bool live, int debug = 0)
        {
            driver.Active = live; DebugMode(session.WaterFluid, debug);
            session.CameraRig.Cam.Fov = 45; session.CameraRig.LookAtPoint(eye, target);
            await Frames(host, 45);
            long tick = world.Clock.Tick; long version = world.Liquid.Version;
            var frames = new List<double>(); var gpu = new List<double>(); long previous = Stopwatch.GetTimestamp();
            long bytes = session.WaterFluid.TextureUploadBytes, updates = session.WaterFluid.SurfaceUpdates;
            var querySamples = new List<object>();
            var querySamplesEveryFrame = new List<object>();
            double startVolume = world.Liquid.CaptureRenderState().Cells.Sum(c => c.Volume), startOutflow = world.Water.Budget.BoundaryOutflow;
            double startInflow = world.Water.Budget.SpringInflow, startTime = world.Liquid.PresentationTime;
            for (int i = 0; i < 90; i++)
            {
                await Frames(host, 1); long now = Stopwatch.GetTimestamp();
                frames.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds); previous = now;
                double g = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid); if (g > 0) gpu.Add(g);
                var query = new { frame = i, time = world.Liquid.PresentationTime,
                    version = world.Liquid.Version, heights = points.Select(p => world.Liquid.SurfaceAt(new SVec2(p[0], p[1]))).ToArray() };
                querySamplesEveryFrame.Add(query); if (i % 5 == 0) querySamples.Add(query);
                if (i % 30 == 0)
                {
                    await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = viewport.GetTexture().GetImage(); image.SavePng(Path.Combine(directory, name + $"_{i / 30}.png"));
                    previous = Stopwatch.GetTimestamp();
                }
            }
            var samples = world.Liquid.CaptureRenderState().Cells;
            var pool = samples.Where(s => s.Position.Length < .15).ToArray();
            frames.Sort();
            reports.Add(new { name, live, debug, eye, target, querySamples, querySamplesEveryFrame,
                storage_change = world.Liquid.CaptureRenderState().Cells.Sum(c => c.Volume) - startVolume,
                boundary_outflow = world.Water.Budget.BoundaryOutflow - startOutflow,
                spring_inflow = world.Water.Budget.SpringInflow - startInflow, physical_time_start = startTime,
                frame_p95_ms = frames[(int)(frames.Count * .95)],
                gpu_mean_ms = gpu.Count == 0 ? (double?)null : gpu.Average(), physical_time = world.Liquid.PresentationTime,
                tick_start = tick, tick_end = world.Clock.Tick, version_start = version, version_end = world.Liquid.Version,
                cells = samples.Length, pool_cells = pool.Length,
                pool_head_min = pool.Length == 0 ? 0 : pool.Min(s => s.Head), pool_head_max = pool.Length == 0 ? 0 : pool.Max(s => s.Head),
                pool_speed_mean = pool.Length == 0 ? 0 : pool.Average(s => s.Velocity.Length),
                pool_speed_max = pool.Length == 0 ? 0 : pool.Max(s => s.Velocity.Length),
                tiles = session.WaterFluid.TileCount, visible_triangles = session.WaterFluid.TrianglesBuilt,
                table_triangles = session.Water.TriangleCount, upload_bytes = session.WaterFluid.TextureUploadBytes - bytes,
                state_updates = session.WaterFluid.SurfaceUpdates - updates, world.Liquid.BacklogSeconds });
            GD.Print("WATER_REVIEW_SHOT " + name);
        }
        await Shot("frozen_pool", new(.55f, .53f, .7f), new(0, .19f, 0), false);
        await Shot("frozen_normals", new(.55f, .53f, .7f), new(0, .19f, 0), false, 1);
        await Shot("frozen_macro", new(.23f, .29f, .27f), new(0, .19f, 0), false);
        await Shot("live_pool", new(.55f, .53f, .7f), new(0, .19f, 0), true);
        await Shot("live_normals", new(.55f, .53f, .7f), new(0, .19f, 0), true, 1);
        await Shot("live_spring", new(-.77f, .47f, .35f), new(-.56f, .22f, 0), true);
        await Shot("live_spill", new(.7f, .45f, .4f), new(.4f, .13f, 0), true);
        var audit = session.WaterFluid.AuditPublishedTextures();
        File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(new
        {
            adapter = RenderingServer.GetVideoAdapterName(), seed = world.Seed, maturity = ready.Result.Report,
            initial_state_mature = ready.Result.IsMature, spring_litres_per_hour = ready.Fixture.Spring.Discharge * 3600000,
            assembly_id = typeof(WaterReviewRunner).Module.ModuleVersionId, resolution = viewport.GetVisibleRect().Size,
            lighting = session.EnvRig.LightingMode, audit, reports,
            notes = "Synthetic fixture; user saves untouched. Frozen shots hold authoritative state while optical flow animates. Live shots advance production async liquid only; ecology is not advanced. Ambient cover/soil detail/actors hidden to inspect water. Fixed support, erosion disabled. Initial static water-table surface may be empty. Diagnostic timings include frozen/live differences and are not a matched performance acceptance."
        }, Json));
        GD.Print("WATER_REVIEW_COMPLETE " + directory); host.GetTree().Quit(audit.Mismatches == 0 ? 0 : 4);
    }
}

internal partial class WaterReviewFluidDriver : Node
{
    public VivariumWorld World { get; set; } = null!;
    public bool Active { get; set; }
    public override void _Process(double delta) { if (Active) World.Liquid.Advance(delta); }
}
