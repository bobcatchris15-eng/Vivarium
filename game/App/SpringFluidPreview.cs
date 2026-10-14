using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using Vivarium.Sim.Water;
using Vivarium.Sim.Tools;
using Vivarium.Game.App;

/// <summary>Production review of the spring runoff: does it read as water, flow downhill, fill, and vanish at the
/// water table? Renders the authoritative liquid and the normal surface renderer.</summary>
public partial class SpringFluidPreview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";

    public override void _Ready() => _ = Capture();

    private async Task Capture()
    {
        try
        {
            GetWindow().Size = new Vector2I(1280, 800);
            Directory.CreateDirectory(OutDir);
            var d = Content.PresetOrThrow("default");
            var w = VivariumWorld.Create(Content, d, populate: false, warmupSurfaceWater:false);
            var session = new GameSession();
            AddChild(session);
            session.Initialize(Content, new UserSettings { Transient = true, AutosaveEnabled = false, ShowHelpOnStart = false, Quality = 1 });
            session.StartWorld(w);
            session.Ui.Visible = false;
            session.CameraRig.KeyboardBlocked = true;
            session.CameraRig.SetProcessUnhandledInput(false);
            session.Tools.SetProcess(false);
            session.Overlay.Visible = false;

            var spring = w.Water.Springs[0];
            var src = new Vec2(spring.X, spring.Z);
            GD.Print($"SPRING source=({src.X:0.000},{src.Z:0.000}) ground={w.GroundHeight(src):0.000} discharge_L_hour={spring.Discharge*3600000:0} parcel_ml={LiquidDynamics.ParcelVolume*1000000:0}");

            async Task Shot(string name)
            {
                // Give the renderer a couple of frames to rebuild the surface after the stream has moved.
                for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(Path.Combine(OutDir, name + ".png"));
                GD.Print($"SPRING_SHOT {name} parcels={w.Liquid.Particles.Count} surfaceTris={session.WaterFluid.TrianglesBuilt}");
            }

            // Look down the fall line from behind and above the source, then from the side.
            var eye = new Vector3((float)src.X - 1.1f, (float)(w.GroundHeight(src) + 0.75), (float)src.Z - 0.9f);
            session.CameraRig.LookAtPoint(eye, new Vector3((float)src.X, (float)(w.GroundHeight(src) - 0.1), (float)src.Z));
            await Shot("00-dry");

            // Freeze biological progression while the independently clocked physical liquid runs.
            w.Clock.BaseRate=0;w.Clock.Paused=false;
            ulong startMs = Time.GetTicksMsec();
            foreach (double end in new[] { 1.5, 4.0, 8.0 })
            {
                while ((Time.GetTicksMsec() - startMs) / 1000.0 < end) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Shot($"{end:0.0}s");
            }

            // Close on the wetted run, looking along it.
            var far = new Vector3((float)src.X + 1.1f, (float)(w.GroundHeight(src) - 0.05), (float)src.Z + 0.9f);
            session.CameraRig.LookAtPoint(new Vector3((float)src.X, (float)(w.GroundHeight(src) + 0.28), (float)src.Z), far);
            await Shot("90-run-close");

            if (w.Liquid.Particles.Count > 0)
            {
                var p = w.Liquid.Particles[w.Liquid.Particles.Count / 2].Position;
                var target = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                session.CameraRig.LookAtPoint(target + new Vector3(0.35f, 0.22f, 0.20f), target);
                await Shot("91-channel-side");
            }
            // Exercise the actual HUD slider signal against an existing spring.
            session.Ui.Visible = true;
            session.Tools.SetTool(ToolKind.Spring);
            session.Tools.ApplyAt(new WorldHit(HitKind.Terrain, EntityId.None,
                new Vec3(src.X, w.GroundHeight(src), src.Z), 1));
            var slider = session.Ui.FindChild("SpringFlowSlider", true, false) as HSlider
                ?? throw new Exception("spring flow slider missing");
            int springCount = w.Water.Springs.Count;
            slider.Value = 50;
            await Shot("92-flow-controls-low");
            slider.Value = 500;
            if (Math.Abs(spring.Discharge * 3600000 - 500) > 0.01 || w.Water.Springs.Count != springCount)
                throw new Exception("slider did not edit the existing spring in place");
            ulong highStart = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - highStart < 4000)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Shot("93-flow-controls-high");
            var remove = session.Ui.FindChild("RemoveSpringButton", true, false) as Button
                ?? throw new Exception("spring remove button missing");
            remove.EmitSignal(Button.SignalName.Pressed);
            if (w.Water.Springs.Count != springCount - 1)
                throw new Exception("remove button did not remove the selected spring");
            GD.Print("SPRING_FLOW_CONTROLS_OK low=50 high=500 range=5..2000 L/hour");
            GD.Print("VIVARIUM_SPRING_FLUID_PREVIEW_OK " + OutDir);
            GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("SPRING_FLUID_PREVIEW_FAILED " + e); GetTree().Quit(1); }
    }
}
