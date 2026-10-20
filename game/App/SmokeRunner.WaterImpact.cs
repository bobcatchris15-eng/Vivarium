using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    private async Task PrepareWaterImpactReview()
    {
        if (!double.TryParse(Main.ArgAfter("--review-fluid-seconds"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double seconds) || seconds <= 0) return;
        W.Liquid.CompletePending();
        W.Liquid.AsyncMode = false;
        // A fixed-bed visual fixture isolates optical changes from erosion and biological growth.
        W.Liquid.Erosion.Enabled = false;
        bool drawing = Session.WaterFluid.Visible, processing = Session.WaterFluid.IsProcessing();
        Session.WaterFluid.Visible = false;
        Session.WaterFluid.SetProcess(false);
        int steps = 0;
        try
        {
            while (W.Liquid.PresentationTime < seconds - 1e-8)
            {
                W.Liquid.AdvancePhysical(Math.Min(.5, seconds - W.Liquid.PresentationTime));
                if (++steps % 10 == 0) await Frames(1);
            }
        }
        finally
        {
            Session.WaterFluid.Visible = drawing;
            Session.WaterFluid.SetProcess(processing);
        }
        // Upload the final snapshot, then settle interpolation before Engine.TimeScale=0.
        // Otherwise a frozen review could remain on the previous (empty) GPU state.
        Session.WaterFluid._Process(1);
        Session.WaterFluid._Process(1);
        WriteWaterImpactState("water_state.json");
        var save = SaveSystem.Save(W, Path.Combine(OutDir, "water_fixture.vivsave"));
        Check("water fixture saved for matched replay", save.Ok, save.Message);
    }

    private void WriteWaterImpactState(string filename)
    {
        var snapshot = W.Liquid.CaptureRenderState();
        var b = W.Water.Budget;
        double balance = W.Liquid.Volume + b.GroundwaterRecharge + b.BoundaryOutflow + b.Evaporation
            + b.Infiltration + b.ToolRemoval - b.SpringInflow - b.ToolInflow;
        Check("water visual fixture conserves volume", Math.Abs(balance) < 2e-9, balance.ToString("R"));
        File.WriteAllText(Path.Combine(OutDir, filename), JsonSerializer.Serialize(new
        {
            time = snapshot.Time, spacing = snapshot.Spacing, table = W.Water.WaterTable,
            volume = W.Liquid.Volume, recharge = b.GroundwaterRecharge, balance,
            airborne = snapshot.Airborne.Length,
            cells = snapshot.Cells.Select(c => new { x = c.Position.X, z = c.Position.Z,
                head = c.Head, depth = c.Depth, vx = c.Velocity.X, vz = c.Velocity.Z,
                bed = W.GroundHeight(c.Position) }),
        }));
    }

    private void AddWaterImpactScenes(List<RefScene> scenes)
    {
        if (Main.ArgAfter("--review-fluid-seconds") == null) return;
        var snapshot = W.Liquid.CaptureRenderState();
        var flowing = snapshot.Cells.Where(c => c.Depth > .0005 && c.Velocity.Length > .003
            && W.GroundHeight(c.Position) > W.Water.WaterTable + .02).ToArray();
        if (flowing.Length > 0)
        {
            var cell = flowing.OrderByDescending(c => c.Depth * c.Velocity.Length).First();
            var p = Bridge.V(cell.Position, cell.Head);
            var dir = cell.Velocity.Normalized();
            var side = new Vector3((float)-dir.Z, 0, (float)dir.X);
            scenes.Add(new("water_run", "flow", p + side * .65f + Vector3.Up * .44f, p));
            scenes.Add(new("water_run_low", "flow", p + side * .44f + Vector3.Up * .12f, p));
        }
        foreach (var f in W.Descriptor.Terrain.Features.Where(f => f.Type == "channel" && f.BedHeight.HasValue))
        {
            var start = new Vec2(f.X, f.Z); var end = new Vec2(f.ToX, f.ToZ);
            if (W.Terrain.Height(start) <= W.Water.WaterTable || W.Terrain.Height(end) >= W.Water.WaterTable) continue;
            double lo = 0, hi = 1;
            for (int k = 0; k < 32; k++)
            {
                double t = (lo + hi) * .5;
                if (W.Terrain.Height(start + (end - start) * t) > W.Water.WaterTable) lo = t; else hi = t;
            }
            var join = start + (end - start) * ((lo + hi) * .5);
            var dir = (end - start).Normalized();
            var p = Bridge.V(join, W.Water.WaterTable);
            var along = new Vector3((float)dir.X, 0, (float)dir.Z);
            var side = new Vector3(-along.Z, 0, along.X);
            scenes.Add(new("water_join", "reservoir", p + side * .64f - along * .24f + Vector3.Up * .32f, p + along * .06f));
            scenes.Add(new("water_join_top", "reservoir", p + side * .08f - along * .16f + Vector3.Up * 1.0f, p));
            var top = scenes[^1];
            foreach (var mode in new[] { "normals", "transmission", "reflection", "reflectance" })
                scenes.Add(new("water_debug_" + mode, "diagnostic", top.Eye, top.Target));
            scenes.Add(new("water_approach", "reservoir", p + side * 1.2f - along * .85f + Vector3.Up * .9f, p - along * .25f));
            break;
        }
    }

    private async Task WaterImpactMotion(List<RefScene> scenes)
    {
        SetWaterOpticsDebug("");
        var scene = scenes.FirstOrDefault(s => s.Name == "water_run") ?? scenes[0];
        Session.CameraRig.LookAtPoint(scene.Eye, scene.Target);
        Session.AmbientGround._Process(1.1);
        Session.SoilDetail._Process(1.1);
        Session.Flora._Process(.5);
        await Frames(24);
        // The static tour may end with the forced underwater void diagnostic.
        // Restore the actual camera medium even when no MediumChanged event fires.
        bool underwater = Session.CameraRig.Medium.Underwater;
        Session.EnvRig.SetUnderwater(underwater);
        Session.Water.SetUnderwater(underwater);
        Session.EnvRig._Process(.3);
        await Frames(2);
        Check("water motion uses actual camera medium", underwater == Session.EnvRig.UnderwaterVisual);
        long tick = W.Clock.Tick;
        static void Clock(Node node, float time)
        {
            if (node is GeometryInstance3D g && g.MaterialOverride is ShaderMaterial m
                && m.Shader.Code.Contains("uniform float review_time")) m.SetShaderParameter("review_time", time);
            if (node is MeshInstance3D mi && mi.Mesh != null)
                for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                    if (mi.Mesh.SurfaceGetMaterial(i) is ShaderMaterial s && s.Shader.Code.Contains("uniform float review_time")) s.SetShaderParameter("review_time", time);
            foreach (Node child in node.GetChildren()) Clock(child, time);
        }
        for (int i = 0; i < 48; i++)
        {
            W.Liquid.AdvancePhysical(1.0 / 24);
            Session.WaterFluid._Process(1.0 / 24);
            float time = (float)W.Liquid.PresentationTime;
            Clock(Session.WaterFluid, time); Clock(Session.Water, time);
            await Frames(2);
            await Screenshot($"water_motion_{i:D3}");
        }
        Check("water motion fixture leaves biological time paused", tick == W.Clock.Tick);
        WriteWaterImpactState("water_motion_state.json");
        File.WriteAllText(Path.Combine(OutDir, "water_motion.json"), JsonSerializer.Serialize(new
        {
            frames = 48, playback_fps = 24, duration_seconds = 2, scene = scene.Name,
            camera_underwater = underwater, environment_underwater = Session.EnvRig.UnderwaterVisual,
            scope = "Fixed camera, actual production fluid advanced in physical seconds; biology and foliage held; playback rate is not a performance measurement",
        }));
    }

    private void SetWaterOpticsDebug(string scene)
    {
        int mode = scene == "water_debug_normals" ? 1 : scene == "water_debug_transmission" ? 2 : scene == "water_debug_reflection" ? 3 : scene == "water_debug_reflectance" ? 4 : 0;
        static void Set(Node node, int value)
        {
            void Material(Material? material)
            {
                if (material is ShaderMaterial s && s.Shader.Code.Contains("mobile_water_optics"))
                    s.SetShaderParameter("water_debug", value);
            }
            if (node is GeometryInstance3D g) Material(g.MaterialOverride);
            if (node is MeshInstance3D mi && mi.Mesh != null)
                for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) Material(mi.Mesh.SurfaceGetMaterial(i));
            foreach (Node child in node.GetChildren()) Set(child, value);
        }
        Set(Session.Water, mode); Set(Session.WaterFluid, mode);
    }
}
