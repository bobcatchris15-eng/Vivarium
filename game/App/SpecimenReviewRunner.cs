using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Opt-in, isolated per-species specimen capture for visual review of individual flora meshes.</summary>
public static class SpecimenReviewRunner
{
    private sealed record ImageRec(string file, string shot, double distance_m, double time_offset_s);

    private sealed class SpecimenRec
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string kind { get; set; } = "flora";
        public string stage { get; set; } = "mature";
        public bool slope_mode { get; set; }
        public double slope_deg { get; set; }
        public double[] position { get; set; } = Array.Empty<double>();
        public double height { get; set; }
        public double radius { get; set; }
        public bool coverage { get; set; }
        public bool published { get; set; }
        public List<ImageRec> images { get; set; } = new();
    }

    private static double SlopeDeg(VivariumWorld world, Vec2 p)
        => Math.Acos(Math.Clamp(world.Terrain.Normal(p).Y, -1.0, 1.0)) * 180.0 / Math.PI;

    private static double VerticalFovRad(Main main)
    {
        var cam = main.GetViewport().GetCamera3D();
        double rad = (cam != null ? cam.Fov : 75.0) * Math.PI / 180.0;
        if (cam != null && cam.KeepAspect == Camera3D.KeepAspectEnum.Width)
        {
            var sz = main.GetViewport().GetVisibleRect().Size;
            rad = 2.0 * Math.Atan(Math.Tan(rad / 2.0) / (sz.X / sz.Y));
        }
        return rad;
    }

    /// <summary>Distance at which an object of the given extent fills ~17.5% of frame height.</summary>
    private static double FarDistance(Main main, double extent)
        => extent / (0.175 * 2.0 * Math.Tan(VerticalFovRad(main) / 2.0));

    private static (List<Vec2> dry, List<Vec2> wet) BuildPools(VivariumWorld world, bool slope, ulong seed)
    {
        var cells = world.Grid.DomainCells;
        var dryPool = new List<Vec2>();
        var wetPool = new List<Vec2>();
        for (int i = 0; i < cells.Length; i += 2)
        {
            var p = world.Grid.CellCenter(cells[i]);
            if (!world.Domain.ContainsDisc(p, 0.35)) continue;
            double ground = world.Terrain.Height(p);
            double propTop = world.Props.PropTopAt(p);
            bool propClear = double.IsNaN(propTop) || propTop < ground + 0.02;
            if (propClear && (world.Props.DistanceToFeature(p, "rock") < 0.5 || world.Props.DistanceToFeature(p, "log") < 0.5))
                propClear = false;
            bool pilotClear = world.PilotTree?.BlocksDisc(p, 0.5) != true;
            if (!propClear || !pilotClear) continue;
            double depth = world.Water.OpenWaterDepth(p);
            if (depth < 0.005)
            {
                double ny = world.Terrain.Normal(p).Y;
                if (!slope && ny <= 0.994) continue;
                if (slope && ny < 0.70) continue; // cap ~45 degrees
                if (!world.Domain.ContainsDisc(p, 0.65)) continue;
                dryPool.Add(p);
            }
            else if (depth >= 0.02 && depth <= 0.9)
            {
                if (world.Terrain.Normal(p).Y <= 0.978) continue; // slope >= ~12 degrees
                wetPool.Add(p);
            }
        }
        var rng = Rng.Stream(seed, "specimen.review");
        Shuffle(dryPool, rng);
        Shuffle(wetPool, rng);
        if (slope)
        {
            // Prefer cells at ~26 degrees (clear slope, not a cliff); when the terrain has no
            // cell >= 20 degrees the steepest available come first.
            dryPool = dryPool.OrderBy(p =>
            {
                double deg = SlopeDeg(world, p);
                return deg >= 20.0 ? Math.Abs(deg - 26.0) : 1000.0 - deg;
            }).ToList();
        }
        else
        {
            dryPool.Sort((a, b) =>
            {
                int c = Vec2.DistanceSq(a, Vec2.Zero).CompareTo(Vec2.DistanceSq(b, Vec2.Zero));
                if (c != 0) return c;
                return world.Terrain.Normal(b).Y.CompareTo(world.Terrain.Normal(a).Y);
            });
        }
        return (dryPool, wetPool);
    }

    private static async Task WaitFrames(Main main, int n)
    {
        for (int i = 0; i < n; i++)
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static readonly HashSet<string> AquaticIds = new(StringComparer.Ordinal)
    {
        "deepquill", "mirepocket", "pondstars", "bogfringe", "burrflag", "paddlerib", "barbarrow",
    };

    private const ulong DefaultSeed = 804584441UL;

    private sealed class Placement
    {
        public string SpeciesId = "";
        public FloraSpeciesDef Species = null!;
        public Vec2 Position;
        public bool IsCoverage;
        public EntityId EntityId;
        public bool HasEntityId;
        public string Stage = "mature";
        // Climbers only: the standing host specimen they are paired with (kept visible in their shots).
        public string? HostSpeciesId;
        public Vec2? HostPosition;
    }

    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory, string? speciesArg, string? seedArg,
        string? kindArg = null, bool slope = false, bool stages = false)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = 60;
        // Pause BEFORE any specimen is placed so the clean stage cannot tick.
        world.Clock.Paused = true;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;

        // Clean stage isolation: hide every distractor so nothing occludes or photobombs any specimen.
        // (Per-specimen flora/coverage visibility is toggled inside the capture loop below.)
        session.PilotTree.Visible = false;
        session.Props.Visible = false;
        session.AmbientGround.Visible = false;
        session.SoilDetail.Visible = false;
        session.Guardian.Visible = false;
        session.Fauna.Visible = false;

        ulong seed = DefaultSeed;
        // ArgAfter falls back to a user-data path when the flag has no value; treat that as "use the default".
        string? seedInput = IsMissingArgValue(seedArg) ? null : seedArg;
        if (!string.IsNullOrWhiteSpace(seedInput))
        {
            if (!ulong.TryParse(seedInput.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out seed))
                seed = DefaultSeed;
        }
        else if (world.Seed != 0)
        {
            // The world was already seeded from --specimen-seed in Main; keep the shuffle in sync.
            seed = world.Seed;
        }

        // Let the flora renderer finish building its per-species layers/meshes (node count stabilises).
        {
            int last = -1, stable = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 120 && stable < 4)
            {
                int cnt = session.Flora.GetChildCount();
                if (cnt > 0 && cnt == last) stable++; else stable = 0;
                last = cnt;
                await WaitFrames(main, 10);
            }
        }

        string? kind = IsMissingArgValue(kindArg) ? null : kindArg!.Trim().ToLowerInvariant();
        if (kind != null && kind != "flora" && kind != "fauna" && kind != "all")
            throw new InvalidOperationException($"Unknown --specimen-kind '{kind}' (expected flora|fauna|all).");

        var content = world.Content;
        var floraIds = new List<string>();
        var faunaIds = new List<string>();
        var skipped = new List<object>();
        if (!IsMissingArgValue(speciesArg))
        {
            var parts = speciesArg!.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            foreach (var id in parts)
            {
                bool isFlora = content.FloraById(id) != null;
                bool isFauna = content.FaunaById(id) != null;
                if (!isFlora && !isFauna) throw new InvalidOperationException($"Unknown species '{id}'.");
                if (isFlora && kind != "fauna") floraIds.Add(id);
                else if (isFauna && kind != "flora") faunaIds.Add(id);
            }
        }
        else
        {
            if (kind != "fauna")
                floraIds.AddRange(content.Flora.Select(f => f.Id));
            if (kind == "fauna" || kind == "all")
                faunaIds.AddRange(content.Fauna.Select(f => f.Id));
        }
        if (floraIds.Count == 0 && faunaIds.Count == 0)
            throw new InvalidOperationException("No specimen species ids were given.");

        var records = new List<SpecimenRec>();
        int fileIndex = 0;
        if (floraIds.Count > 0)
            fileIndex = await CaptureFloraSet(main, session, world, directory, floraIds, seed, slope, stages, records, skipped, fileIndex);
        if (faunaIds.Count > 0)
            fileIndex = await CaptureFaunaSet(main, session, world, directory, faunaIds, seed, slope, records, fileIndex);

        var doc = new
        {
            kind = kind ?? (floraIds.Count > 0 && faunaIds.Count > 0 ? "all" : faunaIds.Count > 0 ? "fauna" : "flora"),
            slope_mode = slope,
            stages_mode = stages,
            image_count = records.Sum(r => r.images.Count),
            specimens = records,
            images = records.SelectMany(r => r.images.Select(i => new
            {
                i.file, species = r.id, r.kind, r.stage, slope = r.slope_mode, r.slope_deg, i.shot, i.distance_m, i.time_offset_s,
            })).ToList(),
            skipped,
        };
        File.WriteAllText(Path.Combine(directory, "specimens.json"),
            JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("SPECIMEN_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit();
    }

    private static async Task<int> CaptureFaunaSet(Main main, GameSession session, VivariumWorld world, string directory,
        List<string> ids, ulong seed, bool slope, List<SpecimenRec> records, int fileIndex)
    {
        var (dryPool, wetPool) = BuildPools(world, slope, seed);
        string slopeSuffix = slope ? "_slope" : "";
        // Flora is not part of a fauna portrait.
        session.Flora.Visible = false;
        session.Coverage.SetProcess(false);
        session.Coverage.Visible = false;
        session.Fauna.Visible = true;
        world.Clock.Paused = false; // gait needs real simulated motion; one animal is alive at a time
        try
        {
            foreach (var id in ids)
            {
                var sp = world.Content.FaunaOrThrow(id);
                bool aquatic = sp.Medium == Medium.Aquatic && sp.Model != "snail";
                List<Vec2> pool = aquatic ? FilterByDepth(wetPool, world, 0.15, 0.6) : dryPool;
                if (pool.Count == 0) pool = aquatic ? wetPool : dryPool;
                if (pool.Count == 0) throw new InvalidOperationException($"No candidate cell for fauna {id}.");
                Vec2 cell = pool[0];
                foreach (var c in pool)
                    if (IsClearOfTrunkProps(world, c, 1.0)) { cell = c; break; }

                var f = world.FaunaSystem.CreateFounder(sp, cell, 0.5);
                foreach (var child in session.Fauna.GetChildren())
                    if (child is MultiMeshInstance3D mmi)
                        mmi.Visible = mmi.Name.ToString().StartsWith($"Fauna_{id}_", StringComparison.Ordinal);
                await WaitFrames(main, 20);

                var ph = world.FaunaSystem.PhenotypeOf(f);
                double scale = Math.Max(0.02, ph.BodySize * sp.VisualScale);
                double azimuth = f.Heading * 180.0 / Math.PI + 55.0;
                double closeDist = Math.Max(1.8 * scale, 0.12);
                double mediumDist = Math.Max(6.0 * scale, 0.4);
                double slopeDeg = SlopeDeg(world, cell);
                var rec = new SpecimenRec
                {
                    id = id, name = sp.Name, kind = "fauna", stage = "adult", slope_mode = slope, slope_deg = Math.Round(slopeDeg, 1),
                    position = new[] { cell.X, cell.Z }, height = scale, radius = scale * 0.5, coverage = false, published = true,
                };

                async Task Follow(double dist, double elevation, string file, string shot, double tOffset)
                {
                    Vector3 target = default, eye = default;
                    for (int i = 0; i < 24; i++)
                    {
                        var pos = session.Fauna.DisplayPosition(f.Id);
                        target = pos + new Vector3(0, (float)(0.4 * scale), 0);
                        eye = EyeFromAzimuthElevation(target, azimuth, elevation, dist);
                        session.CameraRig.LookAtPoint(eye, target);
                        await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                    }
                    await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, file));
                    rec.images.Add(new ImageRec(file, shot, Math.Round(dist, 3), tOffset));
                }

                string stem = $"{fileIndex:00}_{id}";
                var clock = Stopwatch.StartNew();
                await Follow(closeDist, 18.0, $"{stem}_close_a{slopeSuffix}.png", "close", 0.0);
                // second close frame ~0.5 s later in wall time (sim runs unpaused) for motion/joint comparison
                while (clock.Elapsed.TotalSeconds < 0.5 + 0.0) await WaitFrames(main, 1);
                double t1 = clock.Elapsed.TotalSeconds;
                await Follow(closeDist, 18.0, $"{stem}_close_b{slopeSuffix}.png", "close", Math.Round(t1, 2));
                await Follow(mediumDist, 20.0, $"{stem}_medium{slopeSuffix}.png", "medium", Math.Round(clock.Elapsed.TotalSeconds, 2));
                records.Add(rec);
                fileIndex++;
                world.FaunaSystem.Kill(f, "specimen-review");
                await WaitFrames(main, 2);
            }
        }
        finally
        {
            world.Clock.Paused = true;
        }
        return fileIndex;
    }

    private static async Task<int> CaptureFloraSet(Main main, GameSession session, VivariumWorld world, string directory,
        List<string> captureIds, ulong seed, bool slope, bool stages, List<SpecimenRec> records, List<object> skipped, int fileIndex)
    {
        session.Fauna.Visible = false;
        // Round 1: every species at maturity. FloraRenderer builds layers lazily for species present in the world,
        // so mesh/stage availability is only knowable after round 1 has been established and published.
        var round1 = captureIds.Select(id => (id, "mature")).ToList();
        fileIndex = await RunFloraRound(main, session, world, directory, round1, seed, slope, stages, records, skipped, fileIndex);
        if (!stages) return fileIndex;

        var names = session.Flora.GetChildren().Select(c => c.Name.ToString()).ToHashSet(StringComparer.Ordinal);
        bool HasMesh(string id, string suffix)
        {
            string prefix = $"Flora_{id}_";
            return names.Any(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(suffix, StringComparison.Ordinal));
        }
        var round2 = new List<(string Id, string Stage)>();
        foreach (var id in captureIds)
        {
            if (records.All(r => r.id != id || r.kind != "flora")) continue; // skipped in round 1
            var s = world.Content.FloraOrThrow(id);
            if (s.Mat != null || s.Lichen != null || s.Climber != null) continue;
            if (HasMesh(id, "_juvenile")) round2.Add((id, "juvenile"));
            if (HasMesh(id, "_fruit")) round2.Add((id, "fruiting"));
        }
        if (round2.Count > 0)
            fileIndex = await RunFloraRound(main, session, world, directory, round2, seed, slope, stages, records, skipped, fileIndex);
        return fileIndex;
    }

    private static async Task<int> RunFloraRound(Main main, GameSession session, VivariumWorld world, string directory,
        List<(string Id, string Stage)> jobs, ulong seed, bool slope, bool stages, List<SpecimenRec> records, List<object> skipped, int fileIndex)
    {
        string slopeSuffix = slope ? "_slope" : "";

        // ---------------------------------------------------------------- candidate cells (stride 2)
        var (dryPool, wetPool) = BuildPools(world, slope, seed);

        // Placement order: coverage first (widest spacing, so their clumps never merge), then aquatic,
        // then non-climber dry vascular/fungi, and climbers LAST so each can pair with an already-placed
        // standing host tree. Capture order stays the list order. Within each group, species-id order.
        int PlacementRank(string id)
        {
            var s = world.Content.FloraOrThrow(id);
            if (s.Climber != null) return 3;
            if (s.Mat != null || s.Lichen != null) return 0;
            if (AquaticIds.Contains(id)) return 1;
            return 2;
        }
        var assignOrder = jobs
            .OrderBy(j => PlacementRank(j.Id))
            .ThenBy(j => j.Id, StringComparer.Ordinal)
            .ThenBy(j => j.Stage == "mature" ? 0 : j.Stage == "juvenile" ? 1 : 2)
            .ToList();

        var chosenPositions = new List<Vec2>();
        var usedDry = new HashSet<Vec2>();
        var usedWet = new HashSet<Vec2>();
        var placements = new List<Placement>();
        // Standing host trees available for climbers, in a fixed rotation so the three climbers
        // spread across the three hosts when all are captured.
        // Pools are finite; once exhausted, cells are reused. That is safe because each specimen is
        // captured with every other species' meshes hidden (and coverage is seeded one species at a time).
        int reuseCounter = 0;
        Vec2 ReuseCell(List<Vec2> p, string forId)
        {
            if (p.Count == 0) throw new InvalidOperationException($"No candidate cell available for {forId} (dry={dryPool.Count} wet={wetPool.Count}).");
            return p[reuseCounter++ % p.Count];
        }
        var hostRotation = new[] { "moonbark", "fenforge", "shiverleaf" };
        int hostCursor = 0;

        foreach (var job in assignOrder)
        {
            string id = job.Id;
            string jobStage = job.Stage;
            var sp = world.Content.FloraOrThrow(id);
            bool aquatic = AquaticIds.Contains(id);
            bool isCoverage = sp.Mat != null || sp.Lichen != null;
            var pool = aquatic ? wetPool : dryPool;
            var used = aquatic ? usedWet : usedDry;
            // Stage distractors (Props/PilotTree/...) are hidden, so trunk-prop clearance no longer
            // matters for occlusion and is not required here; the candidate pool already keeps every
            // cell >= 0.5 m from any rock/log/pilot trunk. Per-specimen visibility isolation (below)
            // hides all other species during each capture, so specimens do not occlude each other.
            bool requireTrunkClear = false;
            double trunkClearance = 0;

            // Climbers never climb alone: pair each with an already-placed standing host tree
            // (moonbark/fenforge/shiverleaf) and sit just off its trunk surface.
            if (sp.Climber != null)
            {
                Placement? host = null;
                for (int k = 0; k < hostRotation.Length && host == null; k++)
                {
                    string cand = hostRotation[(hostCursor + k) % hostRotation.Length];
                    host = placements.FirstOrDefault(p => p.SpeciesId == cand && p.Stage == "mature");
                    if (host != null) hostCursor = (hostCursor + k + 1) % hostRotation.Length;
                }
                if (host != null)
                {
                    double hostTrunkRadius = Math.Max(0.08, host.Species.RadiusAtMax * 0.12);
                    var hostPos = host.Position;
                    var actualPos = hostPos + new Vec2(hostTrunkRadius + 0.05, 0);
                    chosenPositions.Add(actualPos);
                    placements.Add(new Placement
                    {
                        SpeciesId = id,
                        Species = sp,
                        Position = actualPos,
                        IsCoverage = false,
                        HasEntityId = false,
                        HostSpeciesId = host.SpeciesId,
                        HostPosition = hostPos,
                    });
                    continue;
                }
                // No host specimen in this capture set: fall back to the nearest standing host
                // (PilotTree trunk or an already-placed woody tree) via the legacy snap.
                var fallbackPool = dryPool;
                var fallbackUsed = usedDry;
                var pickedFb = FirstFitClear(fallbackPool, fallbackUsed, chosenPositions, 0.22, world, requireTrunkClear, trunkClearance)
                            ?? FirstFitClear(fallbackPool, fallbackUsed, chosenPositions, 0.10, world, requireTrunkClear, trunkClearance)
                            ?? MostIsolated(fallbackPool, fallbackUsed, chosenPositions, world, requireTrunkClear, trunkClearance)
                            ?? ReuseCell(fallbackPool, id);
                Vec2 actualFb = pickedFb;
                if (SnapClimberToTrunk(world, pickedFb, sp, placements, chosenPositions) is { } snappedFb)
                    actualFb = snappedFb;
                chosenPositions.Add(actualFb);
                fallbackUsed.Add(pickedFb);
                placements.Add(new Placement
                {
                    SpeciesId = id,
                    Species = sp,
                    Position = actualFb,
                    IsCoverage = false,
                    HasEntityId = false,
                });
                continue;
            }

            List<Vec2>? preferredPool = null;
            if (aquatic)
            {
                // Prefer the emergent/floating zone over deep water so surface rosettes/rafts
                // are not buried below the opaque plane. The band comes from content depth
                // needs (MinWaterDepth/MaxWaterDepth); deepquill (Min 0.25) stays deeper
                // (0.25-0.47 m), everything else stays in the shallow 0.03-0.25 m band.
                double bandLo, bandHi;
                if (sp.MinWaterDepth <= 0 && sp.MaxWaterDepth <= 0)
                {
                    bandLo = 0.05;
                    bandHi = 0.3;
                }
                else
                {
                    bandLo = sp.MinWaterDepth > 0 ? Math.Max(sp.MinWaterDepth, 0.03) : 0.03;
                    double maxDepth = sp.MaxWaterDepth > 0 ? sp.MaxWaterDepth : 0.3;
                    bandHi = Math.Min(maxDepth, bandLo + 0.22);
                    if (bandLo < 0.25) bandHi = Math.Min(bandHi, 0.25);
                }
                preferredPool = FilterByDepth(wetPool, world, bandLo, bandHi);
            }

            // Neighbour separation by group: coverage clumps need >= 0.70 m so the mature patches
            // never merge; aquatics need 0.20 m; dry vascular/fungi only 0.22 m (relaxing to 0.10 m)
            // because per-specimen visibility isolation hides every other species during capture.
            double sep = isCoverage ? 0.70 : aquatic ? 0.20 : 0.22;
            double sepRelaxed = isCoverage ? 0.70 : aquatic ? 0.20 : 0.10;

            Vec2? picked;
            Vec2 cellPicked;
            if (preferredPool != null)
            {
                picked = FirstFitClear(preferredPool, used, chosenPositions, sep, world, requireTrunkClear, trunkClearance)
                       ?? FirstFitClear(preferredPool, used, chosenPositions, sepRelaxed, world, requireTrunkClear, trunkClearance);
                if (picked.HasValue) cellPicked = picked.Value;
                else
                {
                    picked = FirstFitClear(pool, used, chosenPositions, sep, world, requireTrunkClear, trunkClearance)
                           ?? FirstFitClear(pool, used, chosenPositions, sepRelaxed, world, requireTrunkClear, trunkClearance)
                           ?? MostIsolated(pool, used, chosenPositions, world, requireTrunkClear, trunkClearance);
                    if (!picked.HasValue) picked = ReuseCell(pool, id);
                    cellPicked = picked.Value;
                }
            }
            else
            {
                picked = FirstFitClear(pool, used, chosenPositions, sep, world, requireTrunkClear, trunkClearance)
                       ?? FirstFitClear(pool, used, chosenPositions, sepRelaxed, world, requireTrunkClear, trunkClearance)
                       ?? MostIsolated(pool, used, chosenPositions, world, requireTrunkClear, trunkClearance);
                if (!picked.HasValue) picked = ReuseCell(pool, id);
                cellPicked = picked.Value;
            }

            chosenPositions.Add(cellPicked);
            used.Add(cellPicked);
            placements.Add(new Placement
            {
                SpeciesId = id,
                Species = sp,
                Position = cellPicked,
                IsCoverage = isCoverage,
                HasEntityId = false,
                Stage = jobStage,
            });
        }

        // ---------------------------------------------------------------- final validation pass
        // Guarantee zero trunk-filled frames for non-climbers: if any placed specimen is
        // within 0.5 m of a trunk prop, resample that specimen alone (up to 200 tries) to a
        // cell satisfying the full HARD trunk clearance (1.0 m / 1.2 m small) and >= 0.7 m
        // separation from every other specimen. Climbers are exempt: they sit at their host.
        // Deterministic: candidates are tried in shuffled-pool order.
        for (int v = 0; v < placements.Count; v++)
        {
            var pl = placements[v];
            if (pl.Species.Climber != null) continue;
            // A host tree must not move after its climber has been paired against it.
            if (placements.Any(c => c.HostSpeciesId == pl.SpeciesId)) continue;
            if (!IsWithinHalfMeterOfTrunkProp(world, pl.Position)) continue;
            bool aquaticV = AquaticIds.Contains(pl.SpeciesId);
            var poolV = aquaticV ? wetPool : dryPool;
            var usedV = aquaticV ? usedWet : usedDry;
            double clearanceV = TrunkClearanceFor(pl.Species);
            // Remove this specimen's current claim before resampling it alone.
            chosenPositions.Remove(pl.Position);
            usedV.Remove(pl.Position);
            Vec2? replacement = null;
            int tries = 0;
            foreach (var cand in poolV)
            {
                if (tries >= 200) break;
                tries++;
                if (usedV.Contains(cand)) continue;
                if (!IsClearOfTrunkProps(world, cand, clearanceV)) continue;
                bool separated = true;
                foreach (var o in chosenPositions)
                {
                    if (Vec2.Distance(cand, o) < 0.7) { separated = false; break; }
                }
                if (!separated) continue;
                replacement = cand;
                break;
            }
            if (!replacement.HasValue)
            {
                // Exhaustive fallback: most-isolated cell that still keeps trunk clearance if
                // ANY cell in the pool satisfies it (separation-only scan only after that).
                var isolated = MostIsolated(poolV, usedV, chosenPositions, world, true, clearanceV);
                if (isolated.HasValue && IsClearOfTrunkProps(world, isolated.Value, clearanceV))
                    replacement = isolated;
            }
            if (replacement.HasValue)
            {
                pl.Position = replacement.Value;
                chosenPositions.Add(replacement.Value);
                usedV.Add(replacement.Value);
            }
            else
            {
                // No cell satisfies clearance anywhere; restore the original claim so
                // bookkeeping stays consistent. (Pool cells are already >= 0.5 m, so a
                // violating original cannot come from the pool alone.)
                chosenPositions.Add(pl.Position);
                usedV.Add(pl.Position);
            }
        }

        // ---------------------------------------------------------------- placement
        // Wave-9: coverage is deliberately NOT seeded here. Seeding all 11 taxa up
        // front let neighbouring patches show in wide coverage shots. Each coverage
        // species is seeded alone inside the capture loop (single-species tile).
        foreach (var pl in placements)
        {
            if (pl.IsCoverage)
            {
                continue;
            }
            else
            {
                var f = world.FloraSystem.Establish(pl.Species, pl.Position, "specimen", pl.Species.MaxBiomass * (pl.Stage == "juvenile" ? 0.25 : 0.9));
                f.Age = pl.Species.Lifespan * 0.5;
                if (pl.Stage == "juvenile")
                    f.Age = Math.Max(0.0, pl.Species.MaturityAge * 0.3);
                else if (pl.Stage == "fruiting")
                    f.Fruiting = true;
                f.LastSpreadAge = -1e9;
                pl.EntityId = f.Id;
                pl.HasEntityId = true;
            }
        }

        // Grow each climber up its host trunk (the world clock is paused, so step it directly).
        foreach (var pl in placements)
        {
            if (pl.Species.Climber == null || !pl.HasEntityId) continue;
            var climberEntity = world.Flora.Get(pl.EntityId);
            if (climberEntity != null)
                world.FloraSystem.AdvanceClimberSpecimen(climberEntity, pl.Species, 56, 0.6);
        }

        // ---------------------------------------------------------------- wait for publish
        var individualIds = placements.Where(p => p.HasEntityId).Select(p => p.EntityId).ToList();
        int n = individualIds.Count;
        if (n > 0)
        {
            var timer = Stopwatch.StartNew();
            double lastProgressSeconds = -1;
            while (timer.Elapsed.TotalSeconds < 360)
            {
                int published = 0;
                foreach (var id in individualIds)
                    if (session.Flora.IsPlantPublished(id)) published++;
                if (published >= n) break;
                if (timer.Elapsed.TotalSeconds >= lastProgressSeconds + 10)
                {
                    lastProgressSeconds = timer.Elapsed.TotalSeconds;
                    GD.Print($"SPECIMEN_PROGRESS published={published}/{n} elapsed_s={lastProgressSeconds:0}");
                }
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            // Final count for reporting (do not abort captures on a slow layer build).
            int finalPublished = 0;
            var unpublished = new List<string>();
            foreach (var pl in placements.Where(p => p.HasEntityId))
            {
                if (session.Flora.IsPlantPublished(pl.EntityId)) finalPublished++;
                else unpublished.Add(pl.SpeciesId);
            }
            if (unpublished.Count > 0)
                GD.PrintErr($"SPECIMEN_PROGRESS unpublished={string.Join(",", unpublished)} published={finalPublished}/{n}");
        }

        // Wave-9: no pre-loop coverage wait. Coverage tiles are seeded one species
        // at a time inside the capture loop, which waits 90 process frames per
        // species so its tile rebuilds for that species alone.

        // ---------------------------------------------------------------- capture (list order)
        var placementByJob = new Dictionary<(string, string), Placement>();
        foreach (var pl in placements)
            placementByJob[(pl.SpeciesId, pl.Stage)] = pl;
        var skipJobs = new HashSet<(string, string)>();
        foreach (var pl in placements)
        {
            if (pl.IsCoverage || !pl.HasEntityId) continue;
            string? why = !session.Flora.HasLegacyLayer(pl.SpeciesId) ? "FloraRenderer built no layer for this species"
                : !session.Flora.IsPlantPublished(pl.EntityId) ? "established but never published by FloraRenderer within 360 s"
                : null;
            if (why == null) continue;
            skipJobs.Add((pl.SpeciesId, pl.Stage));
            skipped.Add(new { id = pl.SpeciesId, kind = "flora", stage = pl.Stage, reason = why });
        }

        bool coverageWasActive = false;
        foreach (var job in jobs)
        {
            if (skipJobs.Contains((job.Id, job.Stage))) continue;
            int idx = fileIndex++;
            var id = job.Id;
            var pl = placementByJob[(job.Id, job.Stage)];
            var sp = pl.Species;
            var rec = new SpecimenRec { kind = "flora" };
            string stageSuffix = pl.Stage == "mature" ? "" : "_" + pl.Stage;
            string Fn(string shot) => $"{idx:00}_{id}{stageSuffix}{shot}{slopeSuffix}.png";
            bool NameMatches(string name, string sid, string stage)
            {
                if (!name.StartsWith($"Flora_{sid}_", StringComparison.Ordinal)) return false;
                if (!stages) return true;
                return stage switch
                {
                    "juvenile" => name.EndsWith("_juvenile", StringComparison.Ordinal),
                    "fruiting" => name.EndsWith("_fruit", StringComparison.Ordinal),
                    _ => !name.EndsWith("_juvenile", StringComparison.Ordinal) && !name.EndsWith("_fruit", StringComparison.Ordinal),
                };
            }

            // Per-specimen visibility isolation: only this specimen (plus its host tree, for
            // climbers) is visible, so neighbouring specimens never occlude or photobomb it.
            // Wave-9: CoverageRenderer._Process re-shows itself every frame
            // (if (!Visible) Visible = true), so toggling Visible alone never hides
            // the tiles; the process flag must follow IsCoverage too.
            session.Coverage.SetProcess(pl.IsCoverage);
            session.Coverage.Visible = pl.IsCoverage;
            if (pl.IsCoverage)
            {
                // Single-species tile: clear everything, seed only this species,
                // then let the renderer rebuild the tile for it alone.
                world.CoverageSystem.ClearDisc(pl.Position, 8.0);
                world.CoverageSystem.SeedMatureSpecimenClump(pl.SpeciesId, pl.Position, 0.25);
                for (int w = 0; w < 90; w++)
                    await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                coverageWasActive = true;
            }
            else if (coverageWasActive)
            {
                // Last coverage species has finished: sweep the stage once so no
                // tile lingers into the non-coverage shots.
                world.CoverageSystem.ClearDisc(Vec2.Zero, 20.0);
                session.Coverage.SetProcess(false);
                session.Coverage.Visible = false;
                coverageWasActive = false;
            }
            foreach (var child in session.Flora.GetChildren())
            {
                if (child is not MultiMeshInstance3D mmi) continue;
                if (pl.IsCoverage)
                {
                    mmi.Visible = false;
                }
                else
                {
                    string mmiName = mmi.Name.ToString();
                    mmi.Visible = NameMatches(mmiName, pl.SpeciesId, pl.Stage)
                        || (pl.HostSpeciesId != null && NameMatches(mmiName, pl.HostSpeciesId, "mature"));
                }
            }
            double height;
            double radius;
            double groundY;
            Vec2 pos = pl.Position;
            bool published = true;

            if (pl.IsCoverage)
            {
                height = 0.06;
                radius = 0.12;
                groundY = world.GroundHeight(pos);
                published = true;
            }
            else
            {
                var f = pl.HasEntityId ? world.Flora.Get(pl.EntityId) : null;
                if (f != null)
                {
                    radius = f.Radius(sp);
                    if (sp.Colony != null)
                        height = sp.Colony.MaxHeight * (0.15 + 0.85 * f.HeightFactor);
                    else
                        height = sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
                    pos = f.Position;
                    published = session.Flora.IsPlantPublished(f.Id);
                }
                else
                {
                    radius = sp.RadiusAtMax;
                    height = sp.Height;
                    published = false;
                }
                groundY = world.GroundHeight(pos);
            }

            if (!double.IsFinite(height) || height <= 0) height = sp.Height > 0 ? sp.Height : 0.06;
            if (!double.IsFinite(radius) || radius <= 0) radius = sp.RadiusAtMax > 0 ? sp.RadiusAtMax : 0.12;

            double size = Math.Max(height, radius * 2);
            var basePoint = new Vector3((float)pos.X, (float)groundY, (float)pos.Z);
            bool isAquatic = AquaticIds.Contains(id);
            bool isClimber = sp.Climber != null;
            bool isTreeShrub = !isAquatic && !isClimber && height >= 0.6 && height <= 3.8;
            // Aquatic target sits at the water surface so the submerged plume is seen looking down through it.
            Vector3 waterBase = basePoint;
            if (isAquatic)
            {
                double waterY = world.Water.SurfaceAt(pos);
                if (!double.IsFinite(waterY)) waterY = groundY + world.Water.OpenWaterDepth(pos);
                waterBase = new Vector3((float)pos.X, (float)waterY, (float)pos.Z);
            }

            // Climbers: frame the vine actually grown up the host trunk, not the ground runner.
            // Mean 3-D position of the upper portion of the segments, and the outward direction
            // from the host trunk so the camera faces the vine as it wraps the trunk.
            Vector3? climberSegCenter = null;
            double climberAzimuthDeg = 35.0;
            if (isClimber)
            {
                var climberF = pl.HasEntityId ? world.Flora.Get(pl.EntityId) : null;
                if (climberF?.ClimberSegments is { Count: > 1 } segs)
                {
                    double sx = 0, sy = 0, sz = 0;
                    int cnt = 0;
                    for (int i = segs.Count / 3; i < segs.Count; i++)
                    {
                        sx += segs[i].Position.X;
                        sy += segs[i].Position.Y;
                        sz += segs[i].Position.Z;
                        cnt++;
                    }
                    if (cnt > 0)
                    {
                        var segCenter = new Vector3((float)(sx / cnt), (float)(sy / cnt), (float)(sz / cnt));
                        climberSegCenter = segCenter;
                        var hostXZ = pl.HostPosition ?? pos;
                        var outDir = new Vec2(segCenter.X - hostXZ.X, segCenter.Z - hostXZ.Z);
                        if (outDir.LengthSq < 1e-9) outDir = new Vec2(1, 0);
                        climberAzimuthDeg = Math.Atan2(outDir.Z, outDir.X) * 180.0 / Math.PI;
                    }
                }
            }

            // Shot A (wide): 35 deg, 18 deg (trees/shrubs: 22 deg fitting whole plant; tall trees: 12 deg
            // side-elevation foot-to-crown in profile; aquatics: 50 deg looking down).
            {
                double dist;
                Vector3 target;
                double azimuth = 35.0;
                double elevation = 18.0;
                if (isClimber)
                {
                    // Wave-9: frame the +X side of the host trunk where the 3
                    // climbing shoots wrap. Wide from azimuth 0 deg.
                    var hostPos = pl.HostPosition ?? pos;
                    dist = 1.05;
                    target = new Vector3((float)hostPos.X, (float)(groundY + 0.58), (float)hostPos.Z);
                    elevation = 12.0;
                    azimuth = 0.0;
                }
                else if (pl.IsCoverage)
                {
                    dist = 0.55;
                    target = basePoint + new Vector3(0, 0.02f, 0);
                    elevation = 32.0;
                }
                else if (isAquatic)
                {
                    dist = Math.Clamp(2.0 * size, 0.3, 5.5) * 1.2;
                    target = waterBase;
                    elevation = 50.0;
                }
                else if (isTreeShrub && height > 1.5)
                {
                    // Trees photograph as saplings under a foreign canopy from above: side-elevation in
                    // profile shows foot-to-crown so the tree reads at full height.
                    dist = Math.Clamp(1.5 * height, 0.4, 8.0);
                    target = basePoint + new Vector3(0, (float)(0.55 * height), 0);
                    elevation = 12.0;
                    azimuth = 40.0;
                }
                else if (isTreeShrub)
                {
                    dist = Math.Clamp(1.5 * height, 0.4, 8.0);
                    target = basePoint + new Vector3(0, (float)(0.5 * height), 0);
                    elevation = 22.0;
                }
                else
                {
                    dist = Math.Clamp(2.0 * size, 0.3, 5.5);
                    target = basePoint + new Vector3(0, (float)(0.45 * height), 0);
                }
                var eye = EyeFromAzimuthElevation(target, azimuth, elevation, dist);
                await CaptureFrame(main, session, eye, target, Path.Combine(directory, Fn("")));
                rec.images.Add(new ImageRec(Fn(""), "plain", Math.Round(dist, 3), 0));
            }
            // Shot B (macro): 95 deg, 8 deg (trees/shrubs: crown zone, side azimuth for tall; aquatics: 55 deg down; climbers: ground runner).
            {
                double dist;
                Vector3 target;
                double azimuth = 95.0;
                double elevation = 8.0;
                if (isAquatic)
                {
                    dist = Math.Clamp(0.8 * size, 0.16, 2.5) * 1.2;
                    target = waterBase;
                    elevation = 55.0;
                }
                else if (isClimber)
                {
                    // Wave-9 macro: straight on the +X trunk flank from azimuth 14 deg.
                    var hostPos = pl.HostPosition ?? pos;
                    dist = 0.42;
                    target = new Vector3((float)hostPos.X, (float)(groundY + 0.58), (float)hostPos.Z);
                    elevation = 8.0;
                    azimuth = 14.0;
                }
                else if (pl.IsCoverage)
                {
                    // Coverage structures are 4-6 cm tall: pull in close so they fill the frame.
                    dist = 0.18;
                    target = basePoint + new Vector3(0, 0.02f, 0);
                    elevation = 22.0;
                }
                else if (isTreeShrub)
                {
                    double macroSize = Math.Max(height * 0.6, radius * 2);
                    dist = Math.Clamp(0.9 * macroSize, 0.2, 3.0);
                    target = basePoint + new Vector3(0, (float)(0.65 * height), 0);
                    if (height > 1.5) azimuth = 40.0;
                }
                else
                {
                    dist = Math.Clamp(0.8 * size, 0.16, 2.5);
                    target = basePoint + new Vector3(0, (float)(0.55 * height), 0);
                }
                if (height < 0.25 && !isClimber && !pl.IsCoverage) dist = Math.Max(dist * 0.7, 0.08);
                var eye = EyeFromAzimuthElevation(target, azimuth, elevation, dist);
                await CaptureFrame(main, session, eye, target, Path.Combine(directory, Fn("_macro")));
                rec.images.Add(new ImageRec(Fn("_macro"), "macro", Math.Round(dist, 3), 0));
            }
            // Shot C (far): ordinary observation distance, specimen ~17.5% of frame height.
            {
                double azimuth = 35.0, elevation = 14.0;
                Vector3 target;
                double extent = Math.Max(height, radius * 2);
                if (pl.IsCoverage)
                {
                    extent = 0.5; // the seeded clump is ~0.5 m across
                    target = basePoint + new Vector3(0, 0.02f, 0);
                    elevation = 30.0;
                }
                else if (isClimber)
                {
                    var hostPos = pl.HostPosition ?? pos;
                    extent = Math.Max(extent, 1.2);
                    target = new Vector3((float)hostPos.X, (float)(groundY + 0.58), (float)hostPos.Z);
                    azimuth = 0.0;
                }
                else if (isAquatic)
                {
                    target = waterBase;
                    elevation = 40.0;
                }
                else
                {
                    target = basePoint + new Vector3(0, (float)(0.5 * height), 0);
                    if (isTreeShrub && height > 1.5) azimuth = 40.0;
                }
                double dist = Math.Clamp(FarDistance(main, extent), 0.5, 90.0);
                var eye = EyeFromAzimuthElevation(target, azimuth, elevation, dist);
                await CaptureFrame(main, session, eye, target, Path.Combine(directory, Fn("_far")));
                rec.images.Add(new ImageRec(Fn("_far"), "far", Math.Round(dist, 3), 0));
            }

            rec.name = sp.Name;
            rec.id = id;
            rec.stage = pl.Stage;
            rec.slope_mode = slope;
            rec.slope_deg = Math.Round(SlopeDeg(world, pos), 1);
            rec.position = new[] { pos.X, pos.Z };
            rec.height = height;
            rec.radius = radius;
            rec.coverage = pl.IsCoverage;
            rec.published = published;
            records.Add(rec);
        }

        if (coverageWasActive)
        {
            world.CoverageSystem.ClearDisc(Vec2.Zero, 20.0);
            session.Coverage.SetProcess(false);
            session.Coverage.Visible = false;
        }
        return fileIndex;
    }

    private static bool IsMissingArgValue(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return true;
        return arg.Contains("test-output") || arg.Contains('/') || arg.Contains('\\');
    }

    private static void Shuffle(List<Vec2> list, Rng rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static Vec2? FirstFitClear(List<Vec2> pool, HashSet<Vec2> used, List<Vec2> chosen, double minSeparation, VivariumWorld world, bool requireTrunkClear, double trunkClearance)
    {
        foreach (var c in pool)
        {
            if (used.Contains(c)) continue;
            if (requireTrunkClear && !IsClearOfTrunkProps(world, c, trunkClearance)) continue;
            bool ok = true;
            foreach (var o in chosen)
            {
                if (Vec2.Distance(c, o) < minSeparation) { ok = false; break; }
            }
            if (ok) return c;
        }
        return null;
    }

    /// <summary>Every non-climber specimen must clear trunk props (logs/rocks/pilot trunk) by 1.0 m (1.2 m for small species) so the
    /// organism, not the neighbour, fills the frame. This clearance is a HARD constraint for non-climbers: fallbacks never relax it.
    /// Climbers are exempt (host within 0.15 m instead).</summary>
    private static bool IsClearOfTrunkProps(VivariumWorld world, Vec2 p, double clearance)
    {
        if (world.Props.DistanceToFeature(p, "log") < clearance) return false;
        if (world.Props.DistanceToFeature(p, "rock") < clearance) return false;
        if (world.PilotTree?.BlocksDisc(p, clearance) == true) return false;
        return true;
    }

    /// <summary>Final-validation trigger: true when p is within 0.5 m of any trunk prop (log/rock/pilot trunk or roots).</summary>
    private static bool IsWithinHalfMeterOfTrunkProp(VivariumWorld world, Vec2 p)
    {
        if (world.Props.DistanceToFeature(p, "log") < 0.5) return true;
        if (world.Props.DistanceToFeature(p, "rock") < 0.5) return true;
        if (world.PilotTree?.BlocksDisc(p, 0.5) == true) return true;
        return false;
    }

    private static double TrunkClearanceFor(FloraSpeciesDef sp) => sp.Height < 0.4 ? 1.2 : 1.0;

    private static List<Vec2> FilterByDepth(List<Vec2> pool, VivariumWorld world, double lo, double hi)
    {
        var result = new List<Vec2>();
        foreach (var c in pool)
        {
            double d = world.Water.OpenWaterDepth(c);
            if (d >= lo && d <= hi) result.Add(c);
        }
        return result;
    }

    private static bool IsValidClimberBase(VivariumWorld world, Vec2 p)
    {
        if (!world.Domain.Contains(p)) return false;
        if (world.Terrain.Normal(p).Y <= 0.90) return false; // allow trunk-base tilt, still viewable
        if (world.Water.OpenWaterDepth(p) >= 0.01) return false;
        double ground = world.Terrain.Height(p);
        double propTop = world.Props.PropTopAt(p);
        if (!double.IsNaN(propTop) && propTop >= ground + 0.02) return false; // inside a prop
        if (world.PilotTree?.BlocksTrunk(p, 0) == true) return false;
        return true;
    }

    /// <summary>Snaps a climber to the base of the nearest STANDING host and returns a point within
    /// 0.15 m of its surface (0.10 m gap). Standing hosts ONLY: the PilotTree trunk or already-placed woody
    /// tree specimens. Logs/rocks are excluded entirely (a fallen log is not a trunk, and snapping to one
    /// makes the wide shot read as a horizontal plank). Returns null when no standing host is near, in which
    /// case the caller keeps the ground-runner position. The host itself is exempt from the 0.7 m neighbour
    /// separation (the climber is meant to sit against it).</summary>
    private static Vec2? SnapClimberToTrunk(VivariumWorld world, Vec2 candidate, FloraSpeciesDef sp, List<Placement> placements, List<Vec2> chosen)
    {
        var cd = sp.Climber;
        if (cd == null) return null;
        double reach = Math.Max(2.0, cd.SearchRadius);
        Vec2? best = null;
        double bestEdge = double.PositiveInfinity;
        Vec2? bestHost = null;

        void Consider(Vec2 basePos, double edgeDist, Vec2? hostPos)
        {
            if (edgeDist > reach || edgeDist >= bestEdge) return;
            if (!IsValidClimberBase(world, basePos)) return;
            foreach (var o in chosen)
            {
                if (hostPos.HasValue && (o - hostPos.Value).LengthSq < 1e-12) continue; // the host itself
                if (Vec2.Distance(basePos, o) < 0.7) return;
            }
            bestEdge = edgeDist;
            best = basePos;
            bestHost = hostPos;
        }

        // Standing hosts only: logs/rocks are never snapped to, even when the species lists them as
        // support types. If no standing host is within reach, the caller keeps the ground-runner cell.
        bool supportsWoody = cd.SupportTypes.Contains("woody");

        if (supportsWoody)
        {
            foreach (var pl in placements)
            {
                if (pl.Species.Woody == null) continue;
                double boleR = Math.Max(0.08, pl.Species.RadiusAtMax * 0.12);
                var toTree = candidate - pl.Position;
                double d = toTree.Length;
                var dir = d > 1e-9 ? toTree / d : new Vec2(1, 0);
                double edge = Math.Max(0, d - boleR);
                Consider(pl.Position + dir * (boleR + 0.10), edge, pl.Position);
            }
        }

        if (supportsWoody)
        {
            var pilot = world.PilotTree;
            if (pilot != null)
            {
                var toTrunk = candidate - pilot.Anchor;
                double d = toTrunk.Length;
                var dir = d > 1e-9 ? toTrunk / d : new Vec2(1, 0);
                double edge = Math.Max(0, d - pilot.Def.TrunkRadius * 0.91);
                Consider(pilot.Anchor + dir * (pilot.Def.TrunkRadius + 0.10), edge, null);
            }
        }

        return best;
    }

    /// <summary>Last-resort pick: the most-isolated cell by separation only, still keeping the HARD trunk
    /// clearance if ANY unused cell in the whole pool satisfies it. Exhaustively scans for a trunk-clear
    /// cell first; only if no such cell exists anywhere does it relax to separation-only.</summary>
    private static Vec2? MostIsolated(List<Vec2> pool, HashSet<Vec2> used, List<Vec2> chosen, VivariumWorld world, bool requireTrunkClear, double trunkClearance)
    {
        if (requireTrunkClear)
        {
            Vec2? bestClear = null;
            double bestClearMin = -1;
            foreach (var c in pool)
            {
                if (used.Contains(c)) continue;
                if (!IsClearOfTrunkProps(world, c, trunkClearance)) continue;
                double minD = double.PositiveInfinity;
                foreach (var o in chosen) minD = Math.Min(minD, Vec2.Distance(c, o));
                if (double.IsPositiveInfinity(minD)) minD = 0;
                if (minD > bestClearMin) { bestClearMin = minD; bestClear = c; }
            }
            if (bestClear.HasValue) return bestClear;
            // Exhaustive scan found no trunk-clear cell anywhere: fall through to separation-only.
        }
        Vec2? best = null;
        double bestMin = -1;
        foreach (var c in pool)
        {
            if (used.Contains(c)) continue;
            double minD = double.PositiveInfinity;
            foreach (var o in chosen) minD = Math.Min(minD, Vec2.Distance(c, o));
            if (double.IsPositiveInfinity(minD)) minD = 0;
            if (minD > bestMin) { bestMin = minD; best = c; }
        }
        return best;
    }

    private static Vector3 EyeFromAzimuthElevation(Vector3 target, double azimuthDeg, double elevationDeg, double distance)
    {
        double az = azimuthDeg * Math.PI / 180.0;
        double el = elevationDeg * Math.PI / 180.0;
        var offset = new Vector3(
            (float)(Math.Cos(az) * Math.Cos(el) * distance),
            (float)(Math.Sin(el) * distance),
            (float)(Math.Sin(az) * Math.Cos(el) * distance));
        return target + offset;
    }

    private static async Task CaptureFrame(Main main, GameSession session, Vector3 eye, Vector3 target, string path)
    {
        session.CameraRig.LookAtPoint(eye, target);
        for (int i = 0; i < 24; i++)
            await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
        await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        main.GetViewport().GetTexture().GetImage().SavePng(path);
    }
}
