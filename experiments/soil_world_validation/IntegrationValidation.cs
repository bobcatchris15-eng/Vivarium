using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

// Compile/run only after the source owner grants a compile lease. Outputs and save files
// belong under build/soil-surface-20261115/soil-world-validation.
static class IntegrationValidation
{
    static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static void Near(double a, double b, double eps, string what) => Need(Math.Abs(a - b) <= eps, $"{what}: {a:R} != {b:R} (eps {eps:R})");

    static int Main()
    {
        string root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "game", "content", "index.json")))
            root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Cannot locate game/content");
        string contentPath = Path.Combine(root, "game", "content");
        string outDir = Path.Combine(root, "build", "soil-surface-20261115", "soil-world-validation");
        Directory.CreateDirectory(outDir);
        var content = ContentLoader.Load(new DirectoryContentSource(contentPath));

        // Frozen pre-change fixture is from the root's TreeNone legacy-reader baseline.
        // This check is intentionally limited to loading/digest/physical invariants; no
        // claim is made that a legacy tree-bearing fixture was tested.
        string oldPath = Path.Combine(root, "build", "soil-surface-20261115", "before-soil.vivsave");
        var old = SaveSystem.Load(content, oldPath);
        Need(old.Ok && old.World != null, "pre-soil save did not load: " + old.Message);
        Need(old.World!.Terrain.SoilSurfaceVersion == 0, "pre-soil save did not retain legacy surface mode");
        Need(double.IsFinite(old.World.Water.Volume()), "pre-soil water volume non-finite");
        Console.WriteLine($"LEGACY_LOAD ok=true version={old.World.Terrain.SoilSurfaceVersion} tree={(old.World.PilotTree?.Def.Id ?? "none")} digest={WorldSerializer.Digest(old.World)} liquid={old.World.Liquid.Volume:R} hydro={old.World.Water.Volume():R}");

        var d = content.PresetOrThrow("default"); // returns a detached clone
        d.Seed = 781_004;
        d.Terrain.SoilSurfaceVersion = 1;
        d.Terrain.SoilSurfaceAmplitude = .020;
        var w = VivariumWorld.Create(content, d, populate: false, warmupSurfaceWater: false);
        Need(w.PilotTree != null, "default preset did not create its pilot tree");
        Need(w.Terrain.SoilSurfaceVersion == 1, "model 1 not active");
        Need(w.PilotTree!.Def.Id == PilotTreeState.SeededPilotTreeFormId(d), "resolved tree does not match descriptor");
        Need(w.Terrain.Version > 0, "tree shaping failed to publish the changed terrain version");
        double model1Anchor = w.PilotTree.BaseHeight;
        var legacyTreeDescriptor = w.Descriptor.Clone();
        legacyTreeDescriptor.Terrain.SoilSurfaceVersion = 0;
        legacyTreeDescriptor.Terrain.SoilSurfaceAmplitude = 0;
        var legacyDomain = new HexDomain(legacyTreeDescriptor.Diameter);
        var legacyTerrain = Heightfield.Generate(legacyTreeDescriptor, legacyDomain);
        var legacyTree = PilotTreeState.Generate(legacyTreeDescriptor, legacyDomain, legacyTerrain)!;
        legacyTree.ShapeTerrain(legacyDomain);
        double legacyAnchor = legacyTree.BaseHeight;
        double treeAnchorShiftFromLegacyMacro = model1Anchor - legacyAnchor;

        var rootSamples = w.PilotTree.Roots.SelectMany(x => x.Samples).ToArray();
        Need(rootSamples.Length > 10, "pilot roots are missing");
        double maxContactError = rootSamples.Max(s => Math.Abs(s.GroundY - w.Terrain.Height(s.Position)));
        Near(maxContactError, 0, 1e-10, "root GroundY after Generate/ShapeTerrain");

        var snap = w.Terrain.Snapshot();
        Need(!ReferenceEquals(snap.H, w.Terrain.H), "terrain snapshot shares mutable height storage");
        var top = TerrainMesh.BuildTop(w, renderSmoothing: false);
        double maxMeshError = 0;
        for (int i = 0; i < top.Mesh.VertexCount; i++)
        {
            var p = top.VertexXZ[i];
            maxMeshError = Math.Max(maxMeshError, Math.Abs(top.Mesh.Position(i).Y - w.Terrain.Height(p)));
        }
        Near(maxMeshError, 0, 2e-6, "physical mesh versus authoritative soil surface");

        // Sculpt both inside the tree root footprint and at an independent interior point.
        // Record untouched controls to catch broad unintended displacement.
        var underRootSample = rootSamples
            .Where(s => w.Domain.ContainsDisc(s.Position, .20) && !double.IsNaN(w.PilotTree.RootSurfaceHeight(s.Position)))
            .OrderByDescending(s => s.Width).FirstOrDefault();
        Need(underRootSample.Width > 0, "no interior PilotTree root footprint for sculpt integration probe");
        var underRoot = underRootSample.Position;
        var outsideRoot = w.Domain.ClampInside(new Vec2(0, 0), .20);
        double rootBefore = w.Terrain.Height(underRoot), outsideBefore = w.Terrain.Height(outsideRoot);
        double farX = w.Terrain.OriginX + .75 * (w.Terrain.Nx - 1) * w.Terrain.Step;
        double farZ = w.Terrain.OriginZ + .74 * (w.Terrain.Nz - 1) * w.Terrain.Step;
        var far = w.Domain.ClampInside(new Vec2(farX, farZ), .01);
        double farBefore = w.Terrain.Height(far);
        int a = TerrainEditing.Sculpt(w, underRoot, .24, .031, SculptMode.Raise);
        int b = TerrainEditing.Sculpt(w, outsideRoot, .19, .017, SculptMode.Lower);
        Need(a > 0 && b > 0, $"sculpt did not change terrain (root={a}, outside={b})");
        double rootAfter = w.Terrain.Height(underRoot), outsideAfter = w.Terrain.Height(outsideRoot), farAfter = w.Terrain.Height(far);
        Need(rootAfter > rootBefore + .005, "raise around root did not affect physical soil height");
        Need(outsideAfter < outsideBefore - .003, "lower outside root did not affect physical soil height");
        Near(farAfter, farBefore, 1e-12, "far untouched terrain control");
        var postEditRoots = w.PilotTree.Roots.SelectMany(x => x.Samples).ToArray();
        double rootGroundStaleness = postEditRoots.Max(s => Math.Abs(s.GroundY - w.Terrain.Height(s.Position)));
        Console.WriteLine($"SCULPT root_changed={a} outside_changed={b} root_dh={(rootAfter-rootBefore):G9} outside_dh={(outsideAfter-outsideBefore):G9} far_dh={(farAfter-farBefore):G9} root_sample_staleness={rootGroundStaleness:G9}");

        // Ground authority samples include exact soil-lattice points, triangle interiors,
        // brush centres, and unchanged controls. SaveSystem.Save/Load is the tested path.
        var probes = new[] { underRoot, outsideRoot, far, w.Terrain.SoilSurface.Vertex(0, 0).XZ, w.Terrain.SoilSurface.Vertex(5, 7).XZ };
        var heights = probes.Select(w.Terrain.Height).ToArray();
        string digest = WorldSerializer.Digest(w);
        string savePath = Path.Combine(outDir, "model1-population-disabled-" + Guid.NewGuid().ToString("N") + SaveSystem.Extension);
        var saved = SaveSystem.Save(w, savePath);
        Need(saved.Ok, "SaveSystem.Save failed: " + saved.Message);
        var loaded = SaveSystem.Load(content, savePath);
        Need(loaded.Ok && loaded.World != null, "SaveSystem.Load failed: " + loaded.Message);
        var restored = loaded.World!;
        Need(WorldSerializer.Digest(restored) == digest, "save/load canonical digest changed");
        Need(restored.PilotTree != null, "pilot tree missing after load");
        for (int i = 0; i < probes.Length; i++) Near(restored.Terrain.Height(probes[i]), heights[i], 1e-12, $"restored physical height probe {i}");
        double liquid = restored.Liquid.Volume;
        var wb = restored.Water.Budget;
        Need(double.IsFinite(liquid) && liquid >= 0 && double.IsFinite(restored.Water.Volume()), "invalid stored liquid/hydro volume");
        foreach (var (name, value) in new[] { ("SpringInflow", wb.SpringInflow), ("GroundwaterInflow", wb.GroundwaterInflow), ("GroundwaterRecharge", wb.GroundwaterRecharge), ("Evaporation", wb.Evaporation), ("Infiltration", wb.Infiltration), ("BoundaryOutflow", wb.BoundaryOutflow), ("ToolInflow", wb.ToolInflow), ("ToolRemoval", wb.ToolRemoval) })
            Need(double.IsFinite(value) && value >= -1e-10, "invalid water budget " + name + "=" + value);
        double restoredRootGroundStaleness = restored.PilotTree.Roots.SelectMany(x => x.Samples).Max(s => Math.Abs(s.GroundY - restored.Terrain.Height(s.Position)));
        Console.WriteLine($"ROUNDTRIP ok=true digest={digest} probes={probes.Length} root_ground_staleness={restoredRootGroundStaleness:G9} stored_liquid={liquid:R} hydro_volume={restored.Water.Volume():R} cumulative_in={wb.SpringInflow+wb.GroundwaterInflow+wb.ToolInflow:R} cumulative_out={wb.GroundwaterRecharge+wb.Evaporation+wb.Infiltration+wb.BoundaryOutflow+wb.ToolRemoval:R}");
        Console.WriteLine($"TREE_UPGRADE_DIAGNOSTIC regenerated_same_descriptor form={w.PilotTree.Def.Id} corner={w.PilotTree.Corner} legacy_tree_anchor={legacyAnchor:R} model1_anchor={model1Anchor:R} delta={treeAnchorShiftFromLegacyMacro:R}; no pre-change tree-bearing save fixture was available, so no migration claim is made");
        Console.WriteLine("SOIL_WORLD_VALIDATION_OK");
        return 0;
    }
}
