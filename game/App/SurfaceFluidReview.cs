using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Game.App;

/// <summary>Reproducible production-session fixtures and real-frame performance/motion capture.</summary>
public partial class SurfaceFluidReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public string Scenario { get; set; } = "A";
    public bool Motion { get; set; }
    public bool Video { get; set; }
    public bool Benchmark { get; set; }
    public string BenchmarkView {get;set;}="oblique";
    public bool DisableMobileDrawing { get; set; }
    public double DurationSeconds { get; set; }
    public string? SavePath { get; set; }
    public string? PresetId { get; set; }
    private GameSession _session = null!;
    private VivariumWorld _w = null!;
    private readonly List<object> _records = new();
    public override void _Ready() => _ = Run();
    private async Task Frame(int count = 1) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Shot(string name)
    {
        await Frame(4); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); using var im = GetViewport().GetTexture().GetImage(); im.SavePng(Path.GetFullPath(Path.Combine(OutDir, name + ".png")));
        if(Scenario=="D"&&name=="45-ground")
        {
            var points=new List<object>();var camera=GetViewport().GetCamera3D();
            foreach(double x in new[]{.20,.225,.24,.25,.275,.30,.35})foreach(double z in new[]{-.08,0,.08})
            {
                var p=new Vec2(x,z);double y=_w.Liquid.SurfaceAt(p);if(!double.IsFinite(y))continue;
                var pixel=camera.UnprojectPosition(new((float)x,(float)y,(float)z));points.Add(new{x,y,z,pixelX=pixel.X,pixelY=pixel.Y});
            }
            File.WriteAllText(Path.Combine(OutDir,"projected-water-points.json"),JsonSerializer.Serialize(points));
        }
    }
    private void Camera(string view)
    {
        if (Scenario == "H" && view == "oblique") { float r = (float)_w.Domain.Radius; _session.CameraRig.LookAtPoint(new(.55f * r, (float)_w.Terrain.H.Max() + r * .8f, .9f * r), new(0, .25f, 0)); return; }
        if (Scenario == "H")
        {
            var source = _w.Water.Springs.First(); var p = source.Position; float y = (float)_w.GroundHeight(p);
            if (view == "pond")
            { int cell = _w.Grid.DomainCells.MinBy(c => _w.Water.Bed[c]); p = _w.Grid.CellCenter(cell); y = (float)_w.Water.WaterTable; }
            var point = new Vector3((float)p.X, y, (float)p.Z);
            if (view == "macro") _session.CameraRig.LookAtPoint(point + new Vector3(.17f, .16f, .12f), point + new Vector3(0, .005f, 0));
            else _session.CameraRig.LookAtPoint(point + new Vector3(.38f, .21f, .18f), point + new Vector3(.08f, .005f, 0));
            return;
        }
        if (view == "pond") _session.CameraRig.LookAtPoint(new(.85f, .19f, .42f), new(.58f, -.10f, 0));
        else if (view == "macro") _session.CameraRig.LookAtPoint(new(-.18f, .27f, .18f), new(.03f, .12f, 0));
        else if (view == "ground") _session.CameraRig.LookAtPoint(new(.62f, .19f, .07f), new(.18f, .075f, 0));
        else _session.CameraRig.LookAtPoint(new(.62f, .48f, .38f), new(.18f, .07f, 0));
    }
    private void Record(string stage)
    {
        var shrubs=_session.Guardian.ShrubContact;
        if(shrubs?.MaximumDrawFrameAuditError>0)throw new Exception("Guardian frame cache changed native draw data");
        float? spatialError=shrubs?.AuditSpatialQueries();
        if(spatialError.HasValue&&(!float.IsFinite(spatialError.Value)||spatialError.Value>0))throw new Exception("Guardian contact broadphase differs from full scan");
        var appendage=_session.Guardian.AppendageContact;
        if(appendage?.MaximumGroundCacheAuditError>0)throw new Exception("Guardian support cache differs from original height query");
        if(appendage?.MaximumFixedPointAuditError>0)throw new Exception("Guardian exact fixed-point audit failed");
        var skin=_session.Guardian.SkinContact;
        float? cacheError=skin?.Valid==true?skin.AuditPoseCache():null;
        if(cacheError.HasValue&&(!float.IsFinite(cacheError.Value)||cacheError.Value>1e-6f))throw new Exception("Guardian pose cache differs from original skinning");
        var b = _w.Water.Budget; double input = b.SpringInflow + b.ToolInflow, output = b.GroundwaterRecharge + b.BoundaryOutflow + b.ToolRemoval + b.Evaporation + b.Infiltration;
        var snapshot = _w.Liquid.CaptureRenderState();
        if(Scenario=="H"&&stage=="t45")
        {
            var source=_w.Water.Springs.First().Position;
            File.WriteAllText(Path.Combine(OutDir,"supported-samples.json"),JsonSerializer.Serialize(snapshot.Cells.Where(c=>(c.Position-source).Length<.6).Select(c=>new{x=c.Position.X,z=c.Position.Z,bed=_w.GroundHeight(c.Position),head=c.Head,depth=c.Depth,slopeX=c.SlopeX,slopeZ=c.SlopeZ,volume=c.Volume,vx=c.Velocity.X,vz=c.Velocity.Z})));
        }
        if(Scenario=="D"&&stage=="t45")File.WriteAllText(Path.Combine(OutDir,"ledge-samples.json"),JsonSerializer.Serialize(snapshot.Cells.Where(c=>c.Position.X>.18&&c.Position.X<.30&&Math.Abs(c.Position.Z)<.16).Select(c=>new{x=c.Position.X,z=c.Position.Z,bed=_w.GroundHeight(c.Position),head=c.Head,depth=c.Depth,slope=c.SlopeX,volume=c.Volume})));
        double ledgeSurface=Scenario=="D"?_w.Liquid.SurfaceAt(new(.2501,0)):double.NaN;
        if(Scenario=="D"&&stage=="t45"&&double.IsFinite(ledgeSurface)&&ledgeSurface>-.025)
            throw new Exception("Supported surface bridges the unsupported waterfall ledge");
        _records.Add(new
        {
            ledgeSurface=double.IsFinite(ledgeSurface)?(double?)ledgeSurface:null,groundCacheSamples=appendage?.GroundCacheSamplesAudited,groundCacheError=appendage?.MaximumGroundCacheAuditError,drawFrameAudits=shrubs?.DrawFrameAudits,drawFrameError=shrubs?.MaximumDrawFrameAuditError,spatialError=spatialError,spatialAuditCases=shrubs?.SpatialAuditCases,spatialContactCases=shrubs?.SpatialAuditContactCases,spatialQueries=shrubs?.SpatialQueries,spatialCandidates=shrubs?.SpatialCandidates,skinCacheError=cacheError,skinSamplesAudited=skin?.LastAuditSampleCount,appendageSkipped=appendage?.ExactIterationsSkipped,fixedPointAudits=appendage?.FixedPointAudits,fixedPointError=appendage?.MaximumFixedPointAuditError,
            stage,
            time = snapshot.Time,
            volume = _w.Liquid.Volume,
            input,
            output,
            error = _w.Liquid.Volume + output - input,
            pond = b.GroundwaterRecharge,
            boundary = b.BoundaryOutflow,
            cells = snapshot.Cells.Length,
            airborne = snapshot.Airborne.Length,
            flora = _w.Flora.Count,
            fauna = _w.Fauna.Count,
            bioDays = _w.Clock.BioDays,
            maxDepth = snapshot.Cells.Length == 0 ? 0 : snapshot.Cells.Max(c => c.Depth),
            volumeWeightedSpeed = snapshot.Cells.Sum(c=>c.Volume*c.Velocity.Length)/Math.Max(1e-30,snapshot.Cells.Sum(c=>c.Volume)),
            maxSurfaceSpeed = snapshot.Cells.Length==0?0:snapshot.Cells.Max(c=>c.Velocity.Length),
            coherentSegments = _session.WaterFluid.AirborneSegments,
            backlog = _w.Liquid.BacklogSeconds,
            triangles = _session.WaterFluid.TrianglesBuilt,
            uploadMs = _session.WaterFluid.LastUploadMilliseconds,
            jobMs = _w.Liquid.LastJobMilliseconds
        });
        File.WriteAllText(Path.Combine(OutDir, "progress.json"), JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true }));
        if (SavePath == null && Math.Abs(_w.Liquid.Volume + output - input) > 2e-9) throw new Exception("Conservation assertion failed at " + stage);
    }
    private async Task Run()
    {
        try
        {
            Directory.CreateDirectory(OutDir); RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(),true); DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); GetWindow().Size = new(1600, 900); GetWindow().Title = "Vivarium Fluid Review " + Guid.NewGuid().ToString("N");
            if (SavePath != null) { var loaded = SaveSystem.Load(Content, SavePath); if (!loaded.Ok) throw new Exception(loaded.Message); _w = loaded.World!; }
            else if (PresetId != null)
            {
                _w = VivariumWorld.Create(Content, Content.PresetOrThrow(PresetId), true); _w.Step(2400);
                var source = _w.Water.Springs.FirstOrDefault();
                if (source != null) _w.Water.Springs.Add(new Spring { Id = _w.Ids.Next(EntityKind.Spring), X = source.X + .2, Z = source.Z + .2, Discharge = source.Discharge * .75 });
            }
            else
            {
                var d = Content.PresetOrThrow("default"); d.Diameter = 5; d.CellSize = .1; d.PilotTreeId = "none";
                d.Placement.Rocks = d.Placement.Logs = d.Placement.GravelPatches = 0; d.Water.WaterTable = -.12; d.Water.Evaporation = d.Water.Infiltration = 0; d.Water.Springs.Clear();
                _w = VivariumWorld.Create(Content, d, Scenario == "H");
                // Mature a current-content ecosystem; fluid is advanced in physical seconds below.
                if (Scenario == "H") _w.Step(2400);
                var hf = _w.Terrain; hf.BeginEdit();
                for (int z = 0; z < hf.Nz; z++) for (int x = 0; x < hf.Nx; x++)
                    {
                        var p = hf.VertexPos(x, z); double height;
                        if (Scenario == "B")
                        {
                            double bowl = -.035 + .07 * Math.Clamp((Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) - .10) / .10, 0, 1);
                            height = p.X > .16 && Math.Abs(p.Z) < .07 ? Math.Min(bowl, .012 - .4 * (p.X - .20)) : bowl;
                        }
                        else if (Scenario == "H") height = .12 - .12 * p.X + .04 * p.Z * p.Z;
                        else if (Scenario == "D") height = (p.X < .25 ? .13 - .05 * p.X : -.09 + .03 * p.X) + .8 * p.Z * p.Z;
                        else height = .12 - .3 * p.X + .8 * p.Z * p.Z + .007 * Math.Sin(p.X * 20) * Math.Cos(p.Z * 15);
                        hf.H[hf.VertexIndex(x, z)] = Math.Clamp(height, hf.MinHeight, hf.MaxHeight);
                    }
                hf.Touch(); _w.Water.RefreshBed(hf); _w.Liquid.OnTerrainChanged();
                if (Scenario == "C" || Scenario == "H")
                { _w.Placement.PlaceRock(new(.17, 0), .07, 0, 212); _w.Placement.PlaceRock(new(.33, .08), .06, .4, 213); _w.Placement.PlaceLog(new(.38, -.08), .6, .22, .035, 0, 214); }
                _w.Water.Springs.Add(new Spring { Id = _w.Ids.Next(EntityKind.Spring), X = -.08, Z = 0, Discharge = Scenario == "B" ? .00010 : .00014 });
                if (Scenario == "H") { _w.Water.Springs.Add(new Spring { Id = _w.Ids.Next(EntityKind.Spring), X = -.15, Z = .2, Discharge = .00007 }); }
            }
            if (Scenario != "H") { Array.Clear(_w.AmbientGroundCover.Cover); Array.Fill(_w.Fields.Moisture.Values, .05); Array.Fill(_w.Fields.Light.Values, 1.0); _w.Liquid.Erosion.Enabled = false; }
            _w.Clock.Paused = true; _session = new(); AddChild(_session);
            _session.Initialize(Content, new UserSettings { Transient = true, AutosaveEnabled = false, ShowHelpOnStart = false, Quality = 1 }); _session.StartWorld(_w);if(_session.Guardian.AppendageContact is {} initialAppendage){initialAppendage.FixedPointAuditBudget=8;initialAppendage.GroundCacheAuditBudget=128;}if(_session.Guardian.ShrubContact is {} initialShrubs)initialShrubs.DrawFrameAuditBudget=8;
            _session.Ui.Visible = false; _session.Overlay.Visible = false; _session.Tools.SetProcess(false); _session.CameraRig.KeyboardBlocked = true; _session.CameraRig.SetProcessUnhandledInput(false);
            if (Scenario != "H") { _session.AmbientGround.Visible = false; _session.SoilDetail.Visible = false; _session.Coverage.Visible = false; }
            _session.EnvRig.BenchmarkLightSeconds = 60; _w.Liquid.AsyncMode = false; Camera("oblique"); await Frame(30);
            var contactProof=Vivarium.Game.Render.GuardianVegetationContact.RunBroadphaseSelfTest(this);
            File.WriteAllText(Path.Combine(OutDir,"guardian-contact-selftest.json"),JsonSerializer.Serialize(new{cases=contactProof.Cases,contactCases=contactProof.Contacts,drawAudits=contactProof.DrawAudits,error=contactProof.Error}));
            var pondBefore = Vivarium.Sim.Geometry.WaterMesh.BuildWaterTable(_w, _w.Terrain).DigestHex();
            await Shot("00-initial"); Record("initial");
            var physics = System.Diagnostics.Stopwatch.StartNew();
            foreach (int target in new[] { 2, 8, 25, 45 })
            {
                while (_w.Liquid.PresentationTime < target - 1e-8)
                { _w.Liquid.AdvancePhysical(SurfaceFluidDynamics.StepSeconds); if ((int)Math.Round(_w.Liquid.PresentationTime * 120) % 30 == 0) await Frame(); }
                Camera("oblique"); await Shot($"{target:00}-oblique"); Record($"t{target}");
                if (target == 8 && Scenario == "C" && _w.Props.Rocks.Count > 0) { _w.Placement.Remove(_w.Props.Rocks[0].Id); _w.Liquid.OnSolidGeometryChanged(); _session.Props.Build(_w); }
                if (target == 8 && Scenario == "F")
                { TerrainEditing.Sculpt(_w, new(.20, 0), .13, .11, SculptMode.Raise); _w.Liquid.OnTerrainChanged(); _session.Island.Build(_w); }
                if (target == 25 && (Scenario == "F" || Scenario == "H"))
                { TerrainEditing.Sculpt(_w, Scenario == "H" ? _w.Water.Springs.First().Position + new Vivarium.Sim.Core.Vec2(.2, 0) : new(.20, 0), .13, Scenario == "H" ? .015 : .18, SculptMode.Lower); _w.Liquid.OnTerrainChanged(); _session.Island.Build(_w); }
            }
            double physicsWall = physics.Elapsed.TotalSeconds;
            if (Scenario != "F" && Scenario != "H" && pondBefore != Vivarium.Sim.Geometry.WaterMesh.BuildWaterTable(_w, _w.Terrain).DigestHex()) throw new Exception("Standing-pond geometry regression");
            foreach (string view in new[] { "macro", "ground", "pond" }) { Camera(view); await Shot("45-" + view); }
            string save = Path.Combine(OutDir, "review.vivsave"); var result = SaveSystem.Save(_w, save); if (!result.Ok) throw new Exception(result.Message);
            var reload = SaveSystem.Load(Content, save); if (!reload.Ok) throw new Exception(reload.Message);
            if (Scenario != "H") reload.World!.Liquid.Erosion.Enabled = false;
            if (WorldSerializer.Digest(_w) != WorldSerializer.Digest(reload.World!)) throw new Exception("Save digest mismatch");
            _session.StartWorld(reload.World!);if(_session.Guardian.AppendageContact is {} reloadedAppendage){reloadedAppendage.FixedPointAuditBudget=8;reloadedAppendage.GroundCacheAuditBudget=128;}if(_session.Guardian.ShrubContact is {} reloadedShrubs)reloadedShrubs.DrawFrameAuditBudget=8; _w = reload.World!; if (Scenario != "H") { Array.Fill(_w.Fields.Light.Values, 1.0); Array.Fill(_w.Fields.Moisture.Values, .05); _session.Island.Build(_w); }
            _w.Clock.Paused = true; Camera("oblique"); await Shot("46-reloaded"); Record("reload");
            if (Motion || Benchmark || Video)
            {
                if (Scenario == "H")
                {
                    var ready = System.Diagnostics.Stopwatch.StartNew(); while (!_session.Flora.PopulationReady && ready.Elapsed.TotalSeconds < 180) await Frame();
                    if (!_session.Flora.PopulationReady) throw new Exception("Populated flora failed to finish loading before performance review");
                }

                if (DisableMobileDrawing) { _session.WaterFluid.Visible = false; _session.WaterFluid.SetProcess(false); }
                Camera(Scenario == "D" ? "ground" : BenchmarkView);
                if(_session.Guardian.ShrubContact is {} warmShrubs)warmShrubs.DrawFrameAuditBudget=8;
                if(_session.Guardian.AppendageContact is {} warmAppendage){warmAppendage.FixedPointAuditBudget=8;warmAppendage.GroundCacheAuditBudget=128;}
                // Warm real animation/contact and asset pipelines before measurement.
                // All fluid input during warmup remains accounted in the same owner.
                _w.Clock.Paused=false;if(Scenario!="H")_w.Clock.BaseRate=0;_w.Liquid.AsyncMode=true;
                await ToSignal(GetTree().CreateTimer(2.0),SceneTreeTimer.SignalName.Timeout);
                _w.Clock.Paused=true;_w.Liquid.CompletePending();Record("warmup");
                System.Diagnostics.Process? recorder = null; Task<string>? recorderLog = null;
                double duration = DurationSeconds > 0 ? DurationSeconds : Scenario == "H" ? 90 : 15;
                if (Video)
                {
                    GetWindow().Borderless = true; GetWindow().Size = new(1600, 900); GetWindow().Position = Vector2I.Zero; GetWindow().AlwaysOnTop = true; GetWindow().GrabFocus(); await Frame(12);
                    await Shot("video-reference");
                    string nativeTitle = System.Diagnostics.Process.GetCurrentProcess().MainWindowTitle;
                    File.WriteAllText(Path.Combine(OutDir, "native-window.txt"), nativeTitle);
                    if (!nativeTitle.Contains("Vivarium Fluid Review")) throw new Exception("Owned review window could not be identified for capture: " + nativeTitle);
                    var start = new System.Diagnostics.ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, RedirectStandardError = true };
                    foreach (string arg in new[] { "-hide_banner", "-loglevel", "warning", "-f", "lavfi", "-i", "ddagrab=framerate=30:draw_mouse=0:video_size=1600x900:offset_x=0:offset_y=0:output_idx=0", "-vf", "hwdownload,format=bgra", "-t", duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", "libx264", "-preset", "ultrafast", "-crf", "20", "-pix_fmt", "yuv420p", "-y", Path.GetFullPath(Path.Combine(OutDir, "motion-30fps.mp4")) }) start.ArgumentList.Add(arg);
                    recorder = System.Diagnostics.Process.Start(start) ?? throw new Exception("Could not start native video recorder"); recorderLog = recorder.StandardError.ReadToEndAsync();
                }
                GetWindow().AlwaysOnTop = true; GetWindow().GrabFocus(); await Frame(8);
                var watch = System.Diagnostics.Stopwatch.StartNew(); var frames = new List<double>(); var work = new List<object>();
                Camera(Scenario == "D" ? "ground" : BenchmarkView); _w.Clock.Paused = false; if (Scenario != "H") _w.Clock.BaseRate = 0; _w.Liquid.AsyncMode = true;
                _session.Flora.SetProcess(true); FrameProfiler.Reset(); double last = 0, nextShot = 0; int number = 0; Task? encoding = null;
                long solverAllocated = _w.Liquid.Dynamics.AllocatedBytes, publishAllocated = _w.Liquid.PublishedAllocatedBytes, rendererAllocated = _session.WaterFluid.AllocatedBytes;
                long memoryBefore = GC.GetTotalMemory(false), allocated = GC.GetTotalAllocatedBytes(); int gen0 = GC.CollectionCount(0); double startTime = _w.Liquid.PresentationTime;
                while (watch.Elapsed.TotalSeconds < duration)
                {
                    await Frame(); double now = watch.Elapsed.TotalSeconds; frames.Add((now - last) * 1000); last = now;
                    if (frames.Count % 60 == 0) work.Add(new {renderSetupCpuMs=RenderingServer.GetFrameSetupTimeCpu(),renderCpuMs=RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),renderGpuMs=RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()), processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000, draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame), managedBytes = GC.GetTotalMemory(false), workingSet = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64, allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, wall = now, fluid = _w.Liquid.PresentationTime - startTime, backlog = _w.Liquid.BacklogSeconds, job = _w.Liquid.LastJobMilliseconds, cells = _w.Liquid.CaptureRenderState().Cells.Length });
                    if (!Motion || now < nextShot || encoding?.IsCompleted == false) continue; nextShot = now + .125;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); var im = GetViewport().GetTexture().GetImage(); string path = Path.Combine(OutDir, $"motion-{number++:0000}.jpg");
                    encoding = Task.Run(() => { im.SaveJpg(path, .9f); im.Dispose(); });
                }
                watch.Stop(); _w.Clock.Paused = true; _w.Liquid.CompletePending(); if (encoding != null) await encoding;
                if (recorder != null)
                {
                    var timeout = System.Diagnostics.Stopwatch.StartNew(); while (!recorder.HasExited && timeout.Elapsed.TotalSeconds < 10) await Frame();
                    if (!recorder.HasExited) { recorder.Kill(); throw new Exception("Native video recorder failed to finish"); }
                    File.WriteAllText(Path.Combine(OutDir, "video.log"), await recorderLog!);
                    if (recorder.ExitCode != 0 || new FileInfo(Path.Combine(OutDir, "motion-30fps.mp4")).Length < 1024) throw new Exception("Native video capture failed"); recorder.Dispose();
                }
                var sorted = frames.OrderBy(x => x).ToArray(); double Quantile(double q) => sorted[(int)Math.Clamp((sorted.Length - 1) * q, 0, sorted.Length - 1)];
                File.WriteAllText(Path.Combine(OutDir, "performance.json"), JsonSerializer.Serialize(new
                {
                    view=Scenario=="D"?"ground":BenchmarkView,
                    vsync = DisplayServer.WindowGetVsyncMode().ToString(),
                    quality = _session.Settings.Quality,
                    renderScale = GetViewport().Scaling3DScale,
                    mobileDrawing = !DisableMobileDrawing,
                    recording = Motion || Video,
                    nativeVideo = Video,
                    wallSeconds = watch.Elapsed.TotalSeconds,
                    fluidSeconds = _w.Liquid.PresentationTime - startTime,
                    frames = frames.Count,
                    fps = frames.Count / watch.Elapsed.TotalSeconds,
                    p50 = Quantile(.5),
                    p95 = Quantile(.95),
                    p99 = Quantile(.99),
                    worst = sorted.Max(),
                    backlog = _w.Liquid.BacklogSeconds,
                    memoryBefore,
                    memoryAfter = GC.GetTotalMemory(false),
                    allocatedBytes = GC.GetTotalAllocatedBytes() - allocated,
                    gen0Collections = GC.CollectionCount(0) - gen0,
                    solverBytes = _w.Liquid.Dynamics.AllocatedBytes - solverAllocated,
                    publishBytes = _w.Liquid.PublishedAllocatedBytes - publishAllocated,
                    rendererBytes = _session.WaterFluid.AllocatedBytes - rendererAllocated,
                    processMemory = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64,
                    work,
                    cpuPerFrame=FrameProfiler.PerFrameDurations(frames.Count),
                    cpuMean = FrameProfiler.MeanDurations(),
                    cpu = FrameProfiler.TakeStructuredReport()
                }, new JsonSerializerOptions { WriteIndented = true })); Record("motion");
            }
            var finalSave=SaveSystem.Save(_w,Path.Combine(OutDir,"review-after.vivsave"));if(!finalSave.Ok)throw new Exception(finalSave.Message);
            File.WriteAllText(Path.Combine(OutDir, "physical.json"), JsonSerializer.Serialize(new { Scenario, physicsWall, pondBefore, records = _records }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("SURFACE_FLUID_REVIEW_OK scenario=" + Scenario + " out=" + OutDir); GetTree().Quit();
        }
        catch (Exception e) { GD.PrintErr("SURFACE_FLUID_REVIEW_FAILED " + e); GetTree().Quit(2); }
    }
}
