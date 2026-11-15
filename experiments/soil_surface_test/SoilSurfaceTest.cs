using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;

static class SoilSurfaceTest
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Near(double a, double b, double e = 1e-10) => Check(Math.Abs(a - b) < e, $"{a} != {b}");
    static void Main()
    {
        // A positive cross product points away from Godot's clockwise front
        // face. Closed gravel crowns must face outward, not reveal their inside.
        for (int variant = 0; variant < 32; variant++)
        {
            var pebble = PropMeshes.Pebble(1000 + (ulong)variant * 7);
            double signedSixVolume = 0;
            for (int k = 0; k < pebble.Indices.Count; k += 3)
            {
                var a = pebble.Position(pebble.Indices[k]);
                var b = pebble.Position(pebble.Indices[k + 1]);
                var c = pebble.Position(pebble.Indices[k + 2]);
                signedSixVolume += a.Dot(b.Cross(c));
                if (a.Y == .75 && b.Y == .75 && c.Y == .75)
                    Check((b-a).Cross(c-a).Y < 0, "gravel crown culled from above");
            }
            Check(signedSixVolume < 0, "gravel shell wound inside out");
        }
        var ctor = typeof(Heightfield).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var terrain = (Heightfield)ctor.Invoke(new object[] { .115, -.23, -.21, 5, 5, -10.0, 10.0, -1.0 });
        for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++) terrain.H[j * 5 + i] = .02 * i + .03 * j + .01 * i * j;
        var p = new SoilSurfaceParameters(1, 42, .008);
        var s = new SoilSurface(terrain, p);
        var same = new SoilSurface(terrain, p);
        var different = new SoilSurface(terrain, p with { Seed = 43 });
        var zero = new SoilSurface(terrain, p with { Amplitude = 0 });
        for (int j = 0; j <= 45; j++) for (int i = 0; i <= 45; i++)
        {
            var point = new Vec2(terrain.OriginX + i * .01, terrain.OriginZ + j * .01);
            Near(zero.Sample(point.X, point.Z).Height, terrain.Height(point));
        }
        bool changed = false;
        for (int j = -8; j < 8; j++) for (int i = -8; i < 8; i++)
        {
            var a = s.Vertex(i, j); var b = s.Vertex(i + 1, j); var c = s.Vertex(i + 1, j + 1); var d = s.Vertex(i, j + 1);
            Near(a.Y, same.Vertex(i, j).Y, 1e-15);
            changed |= a.Y != different.Vertex(i, j).Y;
            foreach (var (u, v) in new[] { (.7, .2), (.2, .7), (.5, .5), (1.0, .4) })
            {
                var sample = s.Sample(a.X + u * s.Spacing, a.Z + v * s.Spacing);
                double expected = u >= v ? a.Y + u * (b.Y - a.Y) + v * (c.Y - b.Y) : a.Y + v * (d.Y - a.Y) + u * (c.Y - d.Y);
                Near(sample.Height, expected);
                double dx = u >= v ? (b.Y - a.Y) / s.Spacing : (c.Y - d.Y) / s.Spacing;
                double dz = u >= v ? (c.Y - b.Y) / s.Spacing : (d.Y - a.Y) / s.Spacing;
                // Exact edges may choose either adjacent triangle's normal.
                if (u != 1 && u != v) { var n = new Vec3(-dx, 1, -dz).Normalized(); Near(sample.Normal.X, n.X); Near(sample.Normal.Y, n.Y); Near(sample.Normal.Z, n.Z); }
            }
        }
        Check(changed, "seed must change relief");
        double before = s.Sample(.031, -.027).Height;
        Array.Fill(terrain.H, 4.0); terrain.Touch();
        Near(before, s.Sample(.031, -.027).Height, 1e-15);
        Check(s.TerrainVersion != terrain.Version, "snapshot version");
        Integrated();
        Console.WriteLine("Soil surface integrated checks passed.");
    }

    static void Integrated()
    {
        var descriptor = new WorldDescriptor { Diameter = 5, CellSize = .23, Seed = 42, PilotTreeId = "none", GuardianId = "none" };
        Check(!descriptor.ToJson().Contains("SoilSurfaceVersion"), "legacy JSON changed");
        descriptor.Terrain.SoilSurfaceVersion = 1;
        descriptor.Terrain.SoilSurfaceAmplitude = .020;
        Check(WorldDescriptor.FromJson(descriptor.ToJson()).Terrain.SoilSurfaceVersion == 1, "model version roundtrip");
        var domain = new HexDomain(descriptor.Diameter);
        var hf = Heightfield.Generate(descriptor, domain);
        var q = new Vec2(.031, -.027);
        var surface = hf.SoilSurface;
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Parallel.For(0, 2000, n =>
        {
            double x = (n % 127 - 63) * .0031, z = (n % 113 - 56) * .0033;
            if (surface.Height(x, z) != surface.UncachedHeight(x, z) || surface.Height(x, z) != surface.Sample(x, z).Height) errors.Enqueue($"cache mismatch at {x},{z}");
        });
        Check(errors.IsEmpty, errors.TryPeek(out var problem) ? problem : "concurrent cache");
        double sink = 0;
        void Bench(string name, Func<double, double, double> sample)
        {
            long start = GC.GetAllocatedBytesForCurrentThread(); var sw = Stopwatch.StartNew();
            for (int n = 0; n < 100_000; n++) sink += sample((n % 257 - 128) * .002, (n % 251 - 125) * .002);
            sw.Stop(); Console.WriteLine($"{name} 100k: {sw.Elapsed.TotalMilliseconds:F2} ms, {GC.GetAllocatedBytesForCurrentThread() - start} bytes");
        }
        Bench("cached", surface.Height); Bench("uncached", surface.UncachedHeight);
        Check(double.IsFinite(sink), "benchmark results");
        // Populate separated tiles, then change one coarse vertex. A rebase retains only
        // realized tiles outside the conservative influence region, without mutating readers.
        for (double z = -1.8; z <= 1.8; z += .4) for (double x = -1.8; x <= 1.8; x += .4) _ = surface.Height(x, z);
        int cachedBefore = surface.CachedTileCount;
        double oldCentre = surface.Height(q.X, q.Z), oldFar = surface.Height(1.5, 1.5);
        var edited = hf.Snapshot();
        int ci = edited.Nx / 2, cj = edited.Nz / 2;
        edited.H[edited.VertexIndex(ci, cj)] += .1; edited.Touch();
        var rebased = surface.Rebase(edited);
        Check(rebased.GeometryCompatible(surface), "rebase geometry compatibility");
        Check(rebased.TryGetChangedBounds(surface, out var low, out var high), "changed bounds missing");
        Check(low.X <= edited.VertexPos(ci, cj).X && high.X >= edited.VertexPos(ci, cj).X && low.Z <= edited.VertexPos(ci, cj).Z && high.Z >= edited.VertexPos(ci, cj).Z, "changed bounds exclude edit");
        Check(rebased.CachedTileCount > 0 && rebased.CachedTileCount < cachedBefore, "unaffected cache reuse");
        Near(surface.Height(q.X, q.Z), oldCentre, 1e-15);
        Near(surface.Height(1.5, 1.5), oldFar, 1e-15);
        Near(rebased.Height(1.5, 1.5), oldFar, 1e-15);
        Check(Math.Abs(rebased.Height(q.X, q.Z) - oldCentre) > .001, "rebase missed edited height");
        var unchanged = surface.Rebase(hf);
        Check(!unchanged.TryGetChangedBounds(surface, out _, out _), "unchanged terrain reports changed bounds");
        Check(unchanged.CachedTileCount == surface.CachedTileCount, "unchanged terrain should reuse every realized tile");
        Check(!surface.GeometryCompatible(new SoilSurface(hf, surface.Parameters with { Seed = surface.Parameters.Seed + 1 })), "seed compatibility");
        Parallel.For(0, 2000, n =>
        {
            double x = (n % 127 - 63) * .02, z = (n % 113 - 56) * .02;
            if (rebased.Height(x, z) != rebased.UncachedHeight(x, z) || surface.Height(x, z) != surface.UncachedHeight(x, z)) errors.Enqueue("rebased concurrent cache mismatch");
        });
        Check(errors.IsEmpty, errors.TryPeek(out problem) ? problem : "rebased concurrent cache");
        Console.WriteLine($"Rebased cached tiles: {rebased.CachedTileCount}/{cachedBefore} initially realized tiles before later sampling");
        Check(Math.Abs(hf.Height(q) - hf.MacroHeight(q)) > 1e-8, "model 1 must affect physical height");
        var snapshot = hf.Snapshot();
        double old = snapshot.Height(q);
        for (int i = 0; i < hf.H.Length; i++) hf.H[i] += .1;
        hf.Touch();
        Near(snapshot.Height(q), old, 1e-14);
        Near(hf.Height(q), old + .1, 1e-12);

        var content = ContentLoader.Load(new DirectoryContentSource(Path.Combine(@"E:\Vivarium", "game", "content")));
        var legacyDescriptor = descriptor.Clone(); legacyDescriptor.Terrain.SoilSurfaceVersion = 0; legacyDescriptor.Terrain.SoilSurfaceAmplitude = 0;
        var disabledLegacy = VivariumWorld.CreateBaseline(content, legacyDescriptor);
        var legacyPayload = WorldSerializer.Serialize(disabledLegacy);
        var legacyRestoredPayload = WorldSerializer.Serialize(WorldSerializer.Deserialize(content, legacyPayload));
        Check(!legacyPayload["water"].SequenceEqual(legacyRestoredPayload["water"]), "disabled legacy baseline fixture expected water upgrade");
        using (var beforeWater = JsonDocument.Parse(legacyPayload["water"]))
        using (var afterWater = JsonDocument.Parse(legacyRestoredPayload["water"]))
            Console.WriteLine($"Disabled legacy baseline water format {beforeWater.RootElement.GetProperty("FormatVersion")} -> {afterWater.RootElement.GetProperty("FormatVersion")}");
        var world = VivariumWorld.CreateBaseline(content, descriptor);
        world.Liquid.Enable();
        var sculptPoint = world.Terrain.VertexPos(world.Terrain.Nx / 2, world.Terrain.Nz / 2);
        Check(TerrainEditing.Sculpt(world, sculptPoint, .2, .02, SculptMode.Raise) > 0, "production sculpt changed terrain");
        double sculptHeight = world.Terrain.Height(sculptPoint);
        string digest = WorldSerializer.Digest(world);
        var payloads = WorldSerializer.Serialize(world);
        var diagnosticRestored = WorldSerializer.Deserialize(content, payloads);
        var afterPayloads = WorldSerializer.Serialize(diagnosticRestored);
        foreach (var (name, bytes) in payloads)
            if (!bytes.SequenceEqual(afterPayloads[name]))
            {
                Console.WriteLine($"DIFFERENT PAYLOAD {name}: {bytes.Length} -> {afterPayloads[name].Length}");
                using var a = JsonDocument.Parse(bytes); using var b = JsonDocument.Parse(afterPayloads[name]);
                foreach (var property in a.RootElement.EnumerateObject())
                    if (!b.RootElement.TryGetProperty(property.Name, out var v) || property.Value.GetRawText() != v.GetRawText()) Console.WriteLine($"DIFFERENT FIELD {name}.{property.Name}");
            }
        string savePath = Path.Combine(Path.GetTempPath(), "vivarium-soil-" + Guid.NewGuid().ToString("N") + SaveSystem.Extension);
        try
        {
            var saved = SaveSystem.Save(world, savePath);
            Check(saved.Ok, "save failed: " + saved.Message);
            var loaded = SaveSystem.Load(content, savePath);
            Check(loaded.Ok && loaded.World != null, "load failed: " + loaded.Message);
            var restored = loaded.World!;
            Check(restored.Descriptor.Terrain.SoilSurfaceVersion == 1, "saved surface version");
            Near(restored.Descriptor.Terrain.SoilSurfaceAmplitude, .020, 1e-15);
            Check(WorldSerializer.Digest(restored) == digest, "sculpted save digest");
            Near(restored.Terrain.Height(sculptPoint), sculptHeight, 1e-12);
        }
        finally { if (File.Exists(savePath)) File.Delete(savePath); }
        var top = TerrainMesh.BuildTop(world, false);
        var mesh = top.Mesh;
        for (int k = 0; k < mesh.Indices.Count; k += 3)
        {
            int ia = mesh.Indices[k], ib = mesh.Indices[k + 1], ic = mesh.Indices[k + 2];
            var a = mesh.Position(ia); var b = mesh.Position(ib); var c = mesh.Position(ic);
            var pa = top.VertexXZ[ia]; var pb = top.VertexXZ[ib]; var pc = top.VertexXZ[ic];
            Near(a.Y, world.Terrain.Height(pa), 1e-6);
            var centre = (pa + pb + pc) / 3;
            Near((a.Y + b.Y + c.Y) / 3, world.Terrain.Height(centre), 1e-6);
        }

        // Capacity fixture: all cells are fully wet at the same head, using the solver's
        // 3x3 ground stencil and its fixed triangle integral. This tests actual restore and advance.
        const double spacing = .0125, head = 1.5;
        var fluid = new SurfaceFluidDynamics(domain, world.Terrain.Height, spacing) { WorkerCount = 1 };
        var cells = new List<SurfaceFluidCell>();
        for (int z = -20; z < 20; z++) for (int x = -20; x < 20; x++)
        {
            double mean = 0;
            for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
            {
                double H(int dx, int dz)
                {
                    double h = world.Terrain.Height(new Vec2((x + (i + dx) * .5) * spacing, (z + (j + dz) * .5) * spacing));
                    Check(head > h + .02, "lake fixture must be fully wet");
                    return h;
                }
                mean += (2 * H(0, 0) + H(1, 0) + 2 * H(1, 1) + H(0, 1)) / 24;
            }
            cells.Add(new SurfaceFluidCell(x, z, (head - mean) * spacing * spacing, 0, 0));
        }
        fluid.Restore(new SurfaceFluidState(cells.ToArray(), Array.Empty<FluidParticle>(), 0, 0, spacing, new(), PoolModelVersion: 1));
        var initial = fluid.CaptureRenderState();
        foreach (var sample in initial.Cells) { Near(sample.Head, head, 1e-10); Near(sample.SupportHeight!.Value, world.Terrain.Height(sample.Position), 1e-12); }
        double initialVolume = fluid.Volume;
        var budget = new WaterBudget();
        fluid.Advance(1.0 / 60, Array.Empty<Spring>(), budget);
        Near(fluid.Volume + budget.BoundaryOutflow + budget.GroundwaterRecharge + budget.Infiltration + budget.Evaporation, initialVolume, 1e-10);
        var centreCells = fluid.CaptureRenderState().Cells.Where(c => Math.Abs(c.Position.X) < .04 && Math.Abs(c.Position.Z) < .04).ToArray();
        Check(centreCells.Length > 0, "lake centre samples");
        Check(centreCells.Max(c => Math.Abs(c.Head - head)) < 1e-5, "finite-patch centre head before boundary arrival");

        double Basin(Vec2 point) => Math.Max(world.Terrain.Height(point), Math.Max(Math.Abs(point.X), Math.Abs(point.Z)) > .3 ? 2.0 : double.NegativeInfinity);
        var closed = new SurfaceFluidDynamics(domain, Basin, spacing) { WorkerCount = 1 };
        var basinCells = new List<SurfaceFluidCell>();
        for (int z = -25; z <= 24; z++) for (int x = -25; x <= 24; x++)
        {
            double depthSum = 0;
            for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
            {
                double H(int dx, int dz) => Basin(new Vec2((x + (i + dx) * .5) * spacing, (z + (j + dz) * .5) * spacing));
                double a = H(0, 0), b = H(1, 0), c = H(1, 1), d = H(0, 1);
                depthSum += SurfaceFluidDynamics.TriangleDepth(head, a, b, c) + SurfaceFluidDynamics.TriangleDepth(head, a, c, d);
            }
            double volume = depthSum * spacing * spacing / 8;
            if (volume > 1e-15) basinCells.Add(new SurfaceFluidCell(x, z, volume, 0, 0, CachedHead: head, HeadVolume: volume));
        }
        closed.Restore(new SurfaceFluidState(basinCells.ToArray(), Array.Empty<FluidParticle>(), 0, 0, spacing, new(), PoolModelVersion: 1));
        double closedStart = closed.Volume;
        var closedBudget = new WaterBudget();
        closed.Advance(.1, Array.Empty<Spring>(), closedBudget);
        var basinAfter = closed.CaptureRenderState().Cells;
        Near(closed.Volume + closedBudget.BoundaryOutflow + closedBudget.GroundwaterRecharge + closedBudget.Infiltration + closedBudget.Evaporation, closedStart, 1e-10);
        Check(basinAfter.Max(c => Math.Abs(c.Head - head)) < 1e-5, "closed basin head drift");
        Check(basinAfter.Max(c => c.Velocity.Length) < 1e-5, "closed basin spontaneous flow");

        var poured = new SurfaceFluidDynamics(domain, world.Terrain.Height, spacing) { WorkerCount = 1 };
        double added = poured.AddWater(new Vec2(0, 0), .05, .0001);
        var pouredBudget = new WaterBudget();
        poured.Advance(.05, Array.Empty<Spring>(), pouredBudget);
        Near(poured.Volume + pouredBudget.BoundaryOutflow + pouredBudget.GroundwaterRecharge + pouredBudget.Infiltration + pouredBudget.Evaporation, added, 1e-10);
        Console.WriteLine($"Integrated mesh triangles={mesh.TriangleCount}, finite-patch centre drift={centreCells.Max(c => Math.Abs(c.Head - head)):G6}, closed basin drift={basinAfter.Max(c => Math.Abs(c.Head - head)):G6}, max speed={basinAfter.Max(c => c.Velocity.Length):G6}, added={added:G6}");
    }
}
