using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>Exercises production interpolation and phase packing against authoritative position updates in a disposable world.</summary>
public partial class FaunaMotionCheck : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string OutDir { get; set; } = "";
    public override void _Ready()
    {
        try
        {
            var descriptor = new WorldDescriptor { PresetId = "motion-check", Seed = 42, Diameter = 10, CellSize = 0.25,
                Terrain = new TerrainProfile { BaseHeight = 0.5, Relief = 0, Bottom = -2 },
                Water = new WaterConfig { WaterTable = -1 },
                Placement = new PlacementProfile { Rocks = 0, Logs = 0, GravelPatches = 0 } };
            var world = VivariumWorld.Create(Content, descriptor, populate: false);
            var renderer = new FaunaRenderer { ProcessMode = ProcessModeEnum.Disabled };
            AddChild(renderer); renderer.Build(world);
            var animals = new List<Vivarium.Sim.Fauna.FaunaIndividual>();
            foreach (var sp in Content.Fauna) animals.Add(world.FaunaSystem.CreateFounder(sp, new Vec2(0, 0)));
            const double dt = 1.0 / 60;
            renderer._Process(dt);
            double worst = 0;
            int checks = 0, crawlChecks = 0, groundedHopChecks = 0, airborneHopChecks = 0;
            var reports = new List<object>();
            var movingTravel = new List<double>();
            foreach (double rate in new[] { 0.2, 0.8, 4.0, 0.0 })
            {
                double travel = 0;
                for (int frame = 0; frame < 90; frame++)
                {
                    var oldPosition = new Vector3[animals.Count]; var oldPhase = new double[animals.Count];
                    var oldCrawl = new bool[animals.Count];
                    for (int i = 0; i < animals.Count; i++)
                    {
                        var f = animals[i]; var sp = Content.FaunaOrThrow(f.SpeciesId);
                        float length = (float)(world.FaunaSystem.PhenotypeOf(f).BodySize * sp.VisualScale);
                        oldPosition[i] = renderer.DisplayPosition(f.Id); oldPhase[i] = renderer.DisplayPhase(f.Id);
                        oldCrawl[i] = renderer.DisplayCrawling(f.Id);
                        // Include pure vertical swimming: horizontal-only speed must fail this check.
                        // Authoritative movement updates at the production two-tick behaviour cadence.
                        // Updating every render frame would keep resetting interpolation before it can advance.
                        if (frame % VivariumWorld.Cadence.FaunaBehaviour == 0)
                        {
                            if (sp.Id == "glintfin") f.Y += rate * length * dt * VivariumWorld.Cadence.FaunaBehaviour;
                            else f.X += rate * length * dt * VivariumWorld.Cadence.FaunaBehaviour;
                        }
                    }
                    world.Clock.Tick++;
                    renderer._Process(dt);
                    for (int i = 0; i < animals.Count; i++)
                    {
                        var f = animals[i]; var sp = Content.FaunaOrThrow(f.SpeciesId);
                        float length = (float)Math.Max(1e-4, world.FaunaSystem.PhenotypeOf(f).BodySize * sp.VisualScale);
                        double dist = renderer.DisplayPosition(f.Id).DistanceTo(oldPosition[i]);
                        bool toad = sp.Model == "toad", crawl = renderer.DisplayCrawling(f.Id);
                        if (!toad || (crawl && oldCrawl[i]))
                        {
                            var gait = toad ? ToadLocomotion.Crawl : sp.Animation;
                            double expected = (oldPhase[i] + FaunaGait.Advance(gait, dist, length, dt)) % 1;
                            double error = Math.Abs(renderer.DisplayPhase(f.Id) - expected);
                            error = Math.Min(error, 1 - error); worst = Math.Max(worst, error);
                            // Float scene transforms introduce tiny round-off into the crawl distance.
                            if (error > (toad ? 0.00001 : 0.0000001)) throw new InvalidOperationException($"{sp.Id} distance phase drift {error}");
                            if (toad && dist > length * 0.0001) crawlChecks++;
                        }
                        if (toad && !crawl && !oldCrawl[i])
                        {
                            double phase = renderer.DisplayPhase(f.Id);
                            if (phase >= oldPhase[i] && oldPhase[i] < sp.Animation.DutyFactor && phase < sp.Animation.DutyFactor)
                            {
                                groundedHopChecks++;
                                if (dist > length * 0.0001)
                                    throw new InvalidOperationException($"Stonebell slides during grounded hop stance: {dist / length:0.0000} body lengths");
                            }
                            if (phase >= sp.Animation.DutyFactor && dist > length * 0.0001) airborneHopChecks++;
                        }
                        checks++;
                        travel += dist;
                    }
                }
                reports.Add(new { bodyLengthsPerSecond = rate, shownTravel = travel });
                if (rate > 0) movingTravel.Add(travel);
            }
            if (movingTravel[0] <= 0 || movingTravel[1] <= movingTravel[0] * 2 || movingTravel[2] <= movingTravel[1] * 2)
                throw new InvalidOperationException("Motion check did not exercise increasing visible speeds");
            if (crawlChecks == 0 || groundedHopChecks == 0 || airborneHopChecks == 0)
                throw new InvalidOperationException("Stonebell check did not exercise crawl, grounded stance and airborne travel");
            // A stopped toad finishes landing/catching up; it cannot freeze in mid-air behind its target.
            world.Clock.Paused = true;
            for (int frame = 0; frame < 180; frame++) renderer._Process(dt);
            foreach (var f in animals)
            {
                if (f.SpeciesId == "stonebell" && renderer.DisplayPosition(f.Id).DistanceTo(Bridge.V(f.Position)) > 0.001)
                    throw new InvalidOperationException("Stonebell failed to settle at its stopped target");
                double phase = renderer.DisplayPhase(f.Id);
                f.Grabbed = true; f.X += 1;
                renderer._Process(dt);
                if (Math.Abs(renderer.DisplayPhase(f.Id) - phase) > 1e-10)
                    throw new InvalidOperationException("Grab/teleport advanced gait " + f.SpeciesId);
            }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "distance-clock.json"), System.Text.Json.JsonSerializer.Serialize(
                new { species = animals.Count, checks, crawlChecks, groundedHopChecks, airborneHopChecks, maxPhaseError = worst, verticalSwimming = true, grabTeleport = true, rates = reports },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"FAUNA_MOTION_OK species={animals.Count} checks={checks} max_error={worst}");
            GetTree().Quit();
        }
        catch (Exception ex) { GD.PrintErr("FAUNA_MOTION_FAILED " + ex); GetTree().Quit(1); }
    }
}
