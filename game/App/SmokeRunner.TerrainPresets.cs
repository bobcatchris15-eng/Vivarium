using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    // Opt-in native geometry review. Ordinary presets and gameplay use the same meshes.
    private async Task TerrainPresetReviewAsync()
    {
        Session.PilotTree.Visible = Session.Flora.Visible = Session.Fauna.Visible = false;
        Session.Guardian.Visible = Session.Props.Visible = Session.Coverage.Visible = false;
        Session.AmbientGround.Visible = Session.SoilDetail.Visible = Session.Overlay.Visible = false;
        // Manual physical stepping below must not race invisible cover builders
        // whose diagnostic support checks span several render frames.
        Session.AmbientGround.SetProcess(false);
        Session.SoilDetail.SetProcess(false);
        Session.EnvRig.CameraAttributes.AutoExposureEnabled = false;
        float r = (float)W.Descriptor.Diameter / 2;
        async Task Shot(string name, Vector3 eye, Vector3 target)
        {
            Session.CameraRig.LookAtPoint(eye, target);
            Session.CameraRig.Cam.Transform = Transform3D.Identity;
            Session.CameraRig.Cam.MakeCurrent();
            Session.CameraRig.Cam.LookAt(target, Vector3.Up);
            await Frames(32);
            await Screenshot(name);
        }
        await Shot("terrain-oblique", new Vector3(r * 1.5f, r * 1.6f, r * 1.6f), new Vector3(0, .2f, 0));
        await Shot("terrain-plan", new Vector3(0, r * 1.8f, r * .025f), Vector3.Zero);
        if (W.Descriptor.PresetId is "braided_springs" or "winding_pools")
            await Shot("shallow-bank-close", new Vector3(1.35f, 1.1f, .8f), new Vector3(.1f, .2f, -.3f));
        if (W.Descriptor.PresetId == "cascading_falls")
            await Shot("ledge-close", new Vector3(1.8f, 1.65f, 2.5f), new Vector3(-.25f, .45f, .4f));
        // Advance the actual solver with the normal spring discharge; no painted water.
        for (int i=0;i<12;i++)
        {
            W.Liquid.AdvancePhysical(10);
            GD.Print($"TERRAIN_FLOW preset={W.Descriptor.PresetId} seconds={(i+1)*10} volume={W.Liquid.Volume:R}");
            await Frames(1);
        }
        await Shot("water-oblique-120s", new Vector3(r * 1.5f, r * 1.6f, r * 1.6f), new Vector3(0, .2f, 0));
        await Shot("water-plan-120s", new Vector3(0, r * 1.8f, r * .025f), Vector3.Zero);
        var b = W.Water.Budget;
        double error = W.Liquid.Volume + b.GroundwaterRecharge + b.BoundaryOutflow + b.Evaporation + b.Infiltration + b.ToolRemoval - b.ToolInflow - b.SpringInflow;
        Check("terrain review conserves spring water", Math.Abs(error) < 1e-8, $"error={error:R}");
        var baseline=W.Descriptor.Clone();baseline.Terrain.Features.RemoveAll(f=>f.CutDepth.HasValue);
        var uncut=Heightfield.Generate(baseline,W.Domain);
        var pools=W.Descriptor.Terrain.Features.Where(f=>f.Type=="basin").ToArray();
        var depths=W.Descriptor.Terrain.Features.Where(f=>f.Type=="channel" && f.CutDepth.HasValue)
            .Select(f=>new Vec2((f.X+f.ToX)/2,(f.Z+f.ToZ)/2))
            .Where(p=>W.Domain.Contains(p) && !pools.Any(f=>Vec2.Distance(p,new(f.X,f.Z))<f.Radius*1.4))
            .Select(p=>uncut.Height(p)-W.GroundHeight(p)).ToArray();
        var elevations=Enumerable.Range(0,W.Terrain.Nz).SelectMany(j=>Enumerable.Range(0,W.Terrain.Nx)
            .Where(i=>W.Domain.Contains(W.Terrain.VertexPos(i,j))).Select(i=>W.Terrain.Vertex(i,j)-W.Water.WaterTable)).ToArray();
        double? WaterSurface(Vec2 p)
        {
            double mobile=W.Liquid.SurfaceAt(p);
            if(W.GroundHeight(p)<W.Water.WaterTable)
                mobile=double.IsFinite(mobile) ? Math.Max(mobile,W.Water.WaterTable) : W.Water.WaterTable;
            return double.IsFinite(mobile) ? mobile : null;
        }
        File.WriteAllText(Path.Combine(OutDir, "terrain-review.json"), JsonSerializer.Serialize(new
        {
            preset = W.Descriptor.PresetId, diameter = W.Descriptor.Diameter,
            physical_seconds_advanced = 120, volume = W.Liquid.Volume, budget_error = error,
            groundwater_recharge = b.GroundwaterRecharge, boundary_outflow = b.BoundaryOutflow,
            channels = W.Descriptor.Terrain.Features.Count(f => f.Type == "channel"),
            channel_depth_min = depths.Length>0 ? (double?)depths.Min() : null,
            channel_depth_max = depths.Length>0 ? (double?)depths.Max() : null,
            terrain_within_8cm_of_water_table = elevations.Count(h=>Math.Abs(h)<.08)/(double)elevations.Length,
            standing_water_fraction = elevations.Count(h=>h<-.002)/(double)elevations.Length,
            pools = pools.Select(f=>new {x=f.X,z=f.Z,ground=W.GroundHeight(new(f.X,f.Z)),
                depth_below_uncut=f.CutDepth.HasValue ? (double?)(uncut.Height(new(f.X,f.Z))-W.GroundHeight(new(f.X,f.Z))) : null,
                water_surface=WaterSurface(new(f.X,f.Z))}).ToArray(),
            scope = "Native production terrain and water, vegetation/props hidden for unobstructed inspection; not performance acceptance"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
