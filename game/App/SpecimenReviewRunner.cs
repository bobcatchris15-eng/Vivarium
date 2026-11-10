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
    private const string DefaultSpeciesList =
        "quillfeather_moss,rustbellow_moss,wispthread_moss,silkgleam_moss,stoneknot_moss,pinequilt_moss,slickmantle,hagshair_lichen,sunscald_lichen,moonshell_lichen,ashfingers_lichen,capstar_moss,duskpuff,starsplit,reekhorn,cradlecup,candlecoral,ashcomb,barrowbole,swanfrond,sceptrefern,emberbracken,mirrorstrap_fern,quillsedge,cloudtuft,duskbristle,bannerreed,ashwire,sootband,starveil,butterglint,cinderberry,mistviolet,hushclover,hallowsting,burlmantle,heddlelace,fenfoam,marshkettle,widowspire,brightknot,cragset,deepquill,mirepocket,pondstars,bogfringe,burrflag,paddlerib,barbarrow,shadewrap,bitterbine,trumpetbind,miremyrtle,nutglade,thornmantle,greywatch,silverfelt,moonbark,shiverleaf,fenforge,banksorrow,mirespire,witchberry";

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
        // Climbers only: the standing host specimen they are paired with (kept visible in their shots).
        public string? HostSpeciesId;
        public Vec2? HostPosition;
    }

    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory, string? speciesArg, string? seedArg)
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

        var captureIds = ParseSpeciesList(speciesArg, world.Content);
        if (captureIds.Count == 0)
            throw new InvalidOperationException("No specimen species ids were given.");

        // Validate early so an unknown id fails before any placement or capture.
        foreach (var id in captureIds.Distinct(StringComparer.Ordinal))
        {
            if (world.Content.FloraById(id) == null)
                throw new InvalidOperationException($"Unknown flora species '{id}'.");
        }

        // ---------------------------------------------------------------- candidate cells (stride 2)
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
            // Reject candidates within 0.5 m of ANY trunk prop (logs/rocks/pilot) for ALL species so the
            // camera is never inside a trunk filling the frame. Climbers are the sole exception: they must
            // sit within 0.15 m of their standing host instead (handled by SnapClimberToTrunk).
            if (propClear && (world.Props.DistanceToFeature(p, "rock") < 0.5 || world.Props.DistanceToFeature(p, "log") < 0.5))
                propClear = false;
            bool pilotClear = world.PilotTree?.BlocksDisc(p, 0.5) != true;
            if (!propClear || !pilotClear) continue;
            double depth = world.Water.OpenWaterDepth(p);
            // Dry stage cells: prime flat spots — slope under ~10 degrees, fully in-domain with a
            // 0.65 m edge margin (covers coverage's 0.55 m requirement), and completely dry.
            if (depth < 0.005)
            {
                if (world.Terrain.Normal(p).Y <= 0.994) continue;
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
        // Best-first ordering for the dry stage: flattest, most-central cells first, so every species
        // gets a prime sunny flat spot near the middle of the island instead of a cliff-edge cell.
        // Stable secondary key on the shuffled order keeps the rng shuffle as the tie-breaker.
        dryPool.Sort((a, b) =>
        {
            int c = Vec2.DistanceSq(a, Vec2.Zero).CompareTo(Vec2.DistanceSq(b, Vec2.Zero));
            if (c != 0) return c;
            return world.Terrain.Normal(b).Y.CompareTo(world.Terrain.Normal(a).Y);
        });

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
        var assignOrder = captureIds
            .OrderBy(PlacementRank)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();

        var chosenPositions = new List<Vec2>();
        var usedDry = new HashSet<Vec2>();
        var usedWet = new HashSet<Vec2>();
        var placements = new List<Placement>();
        // Standing host trees available for climbers, in a fixed rotation so the three climbers
        // spread across the three hosts when all are captured.
        var hostRotation = new[] { "moonbark", "fenforge", "shiverleaf" };
        int hostCursor = 0;

        foreach (var id in assignOrder)
        {
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
                    host = placements.FirstOrDefault(p => p.SpeciesId == cand);
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
                            ?? throw new InvalidOperationException($"No candidate cell available for {id} (dry={dryPool.Count} wet={wetPool.Count}).");
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
                    if (!picked.HasValue)
                        throw new InvalidOperationException($"No candidate cell available for {id} (dry={dryPool.Count} wet={wetPool.Count}).");
                    cellPicked = picked.Value;
                }
            }
            else
            {
                picked = FirstFitClear(pool, used, chosenPositions, sep, world, requireTrunkClear, trunkClearance)
                       ?? FirstFitClear(pool, used, chosenPositions, sepRelaxed, world, requireTrunkClear, trunkClearance)
                       ?? MostIsolated(pool, used, chosenPositions, world, requireTrunkClear, trunkClearance);
                if (!picked.HasValue)
                    throw new InvalidOperationException($"No candidate cell available for {id} (dry={dryPool.Count} wet={wetPool.Count}).");
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
                var f = world.FloraSystem.Establish(pl.Species, pl.Position, "specimen", pl.Species.MaxBiomass * 0.9);
                f.Age = pl.Species.Lifespan * 0.5;
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
        var placementQueues = new Dictionary<string, Queue<Placement>>(StringComparer.Ordinal);
        foreach (var pl in placements)
        {
            if (!placementQueues.TryGetValue(pl.SpeciesId, out var q)) placementQueues[pl.SpeciesId] = q = new Queue<Placement>();
            q.Enqueue(pl);
        }

        var jsonEntries = new List<object>(captureIds.Count);
        bool coverageWasActive = false;
        for (int idx = 0; idx < captureIds.Count; idx++)
        {
            var id = captureIds[idx];
            var pl = placementQueues[id].Dequeue();
            var sp = pl.Species;

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
                    mmi.Visible = mmiName.StartsWith($"Flora_{pl.SpeciesId}_", StringComparison.Ordinal)
                        || (pl.HostSpeciesId != null && mmiName.StartsWith($"Flora_{pl.HostSpeciesId}_", StringComparison.Ordinal));
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
                await CaptureFrame(main, session, eye, target, Path.Combine(directory, $"{idx:00}_{id}.png"));
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
                await CaptureFrame(main, session, eye, target, Path.Combine(directory, $"{idx:00}_{id}_macro.png"));
            }

            jsonEntries.Add(new
            {
                id = id,
                name = sp.Name,
                position = new[] { pos.X, pos.Z },
                height,
                radius,
                coverage = pl.IsCoverage,
                published,
                files = new[]
                {
                    $"{idx:00}_{id}.png",
                    $"{idx:00}_{id}_macro.png",
                },
            });
        }

        if (coverageWasActive)
        {
            world.CoverageSystem.ClearDisc(Vec2.Zero, 20.0);
            session.Coverage.SetProcess(false);
            session.Coverage.Visible = false;
        }

        File.WriteAllText(Path.Combine(directory, "specimens.json"),
            JsonSerializer.Serialize(jsonEntries, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("SPECIMEN_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit();
    }

    private static bool IsMissingArgValue(string? arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return true;
        return arg.Contains("test-output") || arg.Contains('/') || arg.Contains('\\');
    }

    private static List<string> ParseSpeciesList(string? speciesArg, ContentLibrary content)
    {
        if (IsMissingArgValue(speciesArg))
        {
            return DefaultSpeciesList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        var parts = speciesArg!.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        // Fall back to the default list if the argument named nothing recognisable.
        if (parts.Count == 0) return DefaultSpeciesList.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        return parts;
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
