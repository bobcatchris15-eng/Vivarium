using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;

namespace Vivarium.Game.App;

/// <summary>Deterministic review of the production session, island, meshes and surface materials.</summary>
public partial class PilotTreePreview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string TreeId { get; set; } = "all";
    public string OutDir { get; set; } = "";
    private static readonly string[] Ids = { "gloomspire", "needlevault", "crowncoil", "emberpillar", "basinwarden", "palehollow", "cisternvault", "cinnabarcrest", "rootcloister", "silkcascade" };
    public override void _Ready() => _ = CaptureAsync();

    private async Task CaptureAsync()
    {
        try
        {
            if (TreeId != "all" && !Ids.Contains(TreeId)) throw new ArgumentException("Unknown pilot tree: " + TreeId);
            GetWindow().Size = new Vector2I(1280, 800);
            Directory.CreateDirectory(OutDir);
            var session = new GameSession { Name = "PilotTreeReviewSession" };
            AddChild(session);
            session.Initialize(Content, new UserSettings { Transient = true, AutosaveEnabled = false,
                ShowHelpOnStart = false, Quality = 1 });
            session.Ui.Visible = false;
            session.CameraRig.ProcessMode = ProcessModeEnum.Disabled;
            var picker = session.Ui.Windows.FindChild("PilotTreePicker", true, false) as OptionButton
                ?? throw new InvalidOperationException("Pilot tree selection missing");
            var seedEdit = session.Ui.Windows.FindChild("SeedEdit", true, false) as LineEdit
                ?? throw new InvalidOperationException("Seed selection missing");
            var createButton = session.Ui.Windows.FindChild("CreateWorldButton", true, false) as Button
                ?? throw new InvalidOperationException("Create world control missing");
            var captures = new List<object>();
            foreach (string id in TreeId == "all" ? Ids : new[] { TreeId })
            {
                picker.Select(Array.IndexOf(Ids, id) + 2);
                seedEdit.Text = "424242";
                createButton.EmitSignal(Button.SignalName.Pressed);
                var world = session.World ?? throw new InvalidOperationException("Create world button failed");
                var descriptor = world.Descriptor;
                world.Clock.Paused = true;
                var load = System.Diagnostics.Stopwatch.StartNew();
                bool geometryOnly=Main.UserArgs.Contains("--pilot-tree-geometry-only");
                session.Flora.Visible=!geometryOnly;
                session.Fauna.Visible=!geometryOnly;
                while (!geometryOnly && !session.Flora.PopulationReady)
                {
                    if (load.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Flora layers did not become ready for " + id);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                GD.Print("PILOT_TREE_POPULATED_FRAME_READY " + id + " flora_species=" + session.Flora.LoadedSpeciesCount + " wait_ms=" + load.ElapsedMilliseconds);
                var tree = world.PilotTree ?? throw new InvalidOperationException("Missing pilot tree " + id);
                if (tree.Def.Id != id || tree.Corner < 0 || tree.Corner >= 6 || world.Seed != 424242)
                    throw new InvalidOperationException("New world selection did not reach simulation");
                var bounds = session.PilotTree.Tree!.Mesh.GetAabb();
                // A root/trunk-only mesh is narrower than the new GPU crown.
                // Anchor the camera to the authoritative full pilot-tree world form.
                Vector3 center = Bridge.V(tree.Anchor,tree.BaseHeight+tree.Def.TrunkHeight*.55);
                float span = Math.Max((float)tree.Def.TrunkHeight,
                    (float)tree.Def.CanopyRadius*2.0f);
                var inward = new Vector3((float)-tree.CornerPoint.X, 0, (float)-tree.CornerPoint.Z).Normalized();
                var sideways = inward.Cross(Vector3.Up).Normalized();
                string dir = Path.Combine(OutDir, id);
                Directory.CreateDirectory(dir);
                foreach (int side in new[] { 1, -1 })
                {
                    var eye = center + inward * span * 1.4f + sideways * span * 0.72f * side + Vector3.Up * span * 0.5f;
                    session.CameraRig.LookAtPoint(eye, center);
                    string name = $"overview-side{side}.png";
                    await CaptureFrame(dir, name);
                    captures.Add(new { id, view = "overview", side, file = Path.GetFullPath(Path.Combine(dir, name)),
                        eye = new[] { eye.X, eye.Y, eye.Z }, target = new[] { center.X, center.Y, center.Z } });
                    Vector3 root = Bridge.V(tree.CornerPoint, tree.BaseHeight + span * 0.045);
                    float rootSpan = Math.Max(1.8f, (float)descriptor.Diameter * 0.27f);
                    eye = root + inward * rootSpan * 0.85f + sideways * rootSpan * 0.56f * side + Vector3.Up * rootSpan * 0.62f;
                    session.CameraRig.LookAtPoint(eye, root + inward * rootSpan * 0.30f);
                    name = $"roots-side{side}.png";
                    await CaptureFrame(dir, name);
                    captures.Add(new { id, view = "roots", side, file = Path.GetFullPath(Path.Combine(dir, name)),
                        eye = new[] { eye.X, eye.Y, eye.Z }, corner = tree.Corner });
                }
                var backEye = center - inward * span * 1.35f + Vector3.Up * span * 0.40f;
                session.CameraRig.LookAtPoint(backEye, center);
                string backName = "back-slice.png";
                await CaptureFrame(dir, backName);
                captures.Add(new { id, view = "back-slice", file = Path.GetFullPath(Path.Combine(dir, backName)),
                    eye = new[] { backEye.X, backEye.Y, backEye.Z }, target = new[] { center.X, center.Y, center.Z } });
                if(Main.UserArgs.Contains("--pilot-root-revision-test"))
                {
                    var terrain=world.Terrain;
                    var renderer=session.PilotTree;
                    var originalRootMesh=renderer.Roots!.Mesh;
                    // Bridge.ToArrayMesh mutates an existing ArrayMesh in place:
                    // native resource identity is NOT an indication of upload.
                    using var beforeArrays=originalRootMesh.SurfaceGetArrays(0);
                    var originalRootVertices=beforeArrays[(int)Mesh.ArrayType.Vertex]
                        .AsVector3Array().ToArray();
                    long startRebuilds=renderer.RootMeshRebuilds;
                    long startSkips=renderer.UnchangedRootFootprints;
                    terrain.BeginEdit();
                    // Edited central interior, deliberately remote from the
                    // ancient boundary roots. Native renderer must skip upload.
                    int ci=(int)Math.Round((-terrain.OriginX)/terrain.Step);
                    int cj=(int)Math.Round((-terrain.OriginZ)/terrain.Step);
                    for(int j=cj;j<=cj+1;j++)
                    for(int i=ci;i<=ci+1;i++)
                        terrain.H[terrain.VertexIndex(i,j)]+=.01;
                    terrain.Touch();
                    renderer._Process(.3);
                    if(renderer.RootMeshRebuilds!=startRebuilds ||
                       renderer.UnchangedRootFootprints<=startSkips ||
                       !ReferenceEquals(originalRootMesh,renderer.Roots.Mesh))
                        throw new InvalidOperationException("Distant erosion rebuilt ancient roots");

                    // Move ground directly underneath an actual crest; the
                    // render mesh must change but static trunk remain unchanged.
                    var rootSample=tree.Roots.First().Samples[5];
                    int ri=(int)Math.Floor((rootSample.Position.X-terrain.OriginX)/terrain.Step);
                    int rj=(int)Math.Floor((rootSample.Position.Z-terrain.OriginZ)/terrain.Step);
                    var originalTrunk=renderer.Tree!.Mesh;
                    for(int j=rj;j<=rj+1;j++)
                    for(int i=ri;i<=ri+1;i++)
                        terrain.H[terrain.VertexIndex(i,j)]+=.015;
                    terrain.Touch();
                    renderer._Process(.3);
                    using var afterArrays=renderer.Roots.Mesh.SurfaceGetArrays(0);
                    var movedVertices=afterArrays[(int)Mesh.ArrayType.Vertex]
                        .AsVector3Array().ToArray();
                    if(renderer.RootMeshRebuilds!=startRebuilds+1 ||
                       originalRootVertices.SequenceEqual(movedVertices) ||
                       !ReferenceEquals(originalTrunk,renderer.Tree.Mesh))
                        throw new InvalidOperationException(
                            $"Local erosion failed to update roots independently: builds={renderer.RootMeshRebuilds-startRebuilds}, geometryChanged={!originalRootVertices.SequenceEqual(movedVertices)}, trunkUnchanged={ReferenceEquals(originalTrunk,renderer.Tree.Mesh)}");
                    GD.Print($"PILOT_ROOT_FOOTPRINT_VERIFIED species={id} local_rebuilds={renderer.RootMeshRebuilds-startRebuilds} distant_skips={renderer.UnchangedRootFootprints-startSkips}");
                }
                GD.Print("PILOT_TREE_SPECIES_OK " + id);
            }
            session.Ui.Visible = true;
            session.Ui.Windows.Open("NewWorld");
            await CaptureFrame(OutDir, "new-world-controls.png");
            session.Ui.Windows.Close("NewWorld");
            session.Ui.Visible = false;
            var cornerChecks = new List<int>();
            picker.Select(2);
            for (int k = 0; k < 6; k++)
            {
                seedEdit.Text = (1000 + k * 17).ToString();
                createButton.EmitSignal(Button.SignalName.Pressed);
                if (session.World?.PilotTree == null) throw new InvalidOperationException("Tree creation failed");
                session.World.Clock.Paused = true;
                cornerChecks.Add(session.World.PilotTree.Corner);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            picker.Select(0);
            createButton.EmitSignal(Button.SignalName.Pressed);
            if (session.World?.PilotTree == null) throw new InvalidOperationException("Random tree selection failed");
            // Exercise the same renderer replacement path used by New World, including removing a tree.
            picker.Select(1);
            createButton.EmitSignal(Button.SignalName.Pressed);
            if (session.PilotTree.Tree != null) throw new InvalidOperationException("Tree remains after choosing None");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            File.WriteAllText(Path.Combine(OutDir, "preview.json"), JsonSerializer.Serialize(new {
                seed = 424242, corner = 0, renderPath = "CreateWorldButton -> GameSession.StartWorld", captureCount = captures.Count + 1,
                sceneCaptureCount = captures.Count, uiCapture = Path.GetFullPath(Path.Combine(OutDir, "new-world-controls.png")), captures,
                cornerChecks, randomSelection = true, replacementWithoutTree = true }, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print("VIVARIUM_PILOT_TREE_PREVIEW_OK " + Path.GetFullPath(OutDir) + " captures=" + (captures.Count + 1));
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("VIVARIUM_PILOT_TREE_PREVIEW_FAILED " + ex); GetTree().Quit(1); }
    }

    private async Task CaptureFrame(string dir, string name)
    {
        for (int frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.GetFullPath(Path.Combine(dir, name));
        if (GetViewport().GetTexture().GetImage().SavePng(path) != Error.Ok) throw new IOException(path);
    }
}
