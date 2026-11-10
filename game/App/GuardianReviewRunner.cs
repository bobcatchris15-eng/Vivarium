using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Automated 10-species guardian diagnostic, visual capture, locomotion &amp; contact verification runner.
/// Invoked via <c>--guardian-review &lt;output_dir&gt; [--guardian-species a,b]</c>.
/// </summary>
public static class GuardianReviewRunner
{
    public static async Task Capture(Main main, GameSession session, VivariumWorld world, string directory, string? speciesFilter)
    {
        Directory.CreateDirectory(directory);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        session.EnvRig.BenchmarkLightSeconds = 60;
        session.CameraRig.SetProcess(false);
        session.Ui.Visible = false;
        session.Overlay.HideCursor();
        session.Tools.SetProcess(false);
        session.Overlay.Visible = false;

        var requested = string.IsNullOrWhiteSpace(speciesFilter) || speciesFilter.Contains("test-output", StringComparison.OrdinalIgnoreCase)
            ? null
            : new HashSet<string>(speciesFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        var roster = GuardianCatalog.All
            .Where(d => requested == null || requested.Contains(d.Id) || requested.Contains(d.Archetype))
            .ToList();

        Vec2 stageCenter = FindStageSpot(world, preferSlope: false);
        Vec2 slopeSpot = FindStageSpot(world, preferSlope: true);

        var report = new Dictionary<string, object>();

        foreach (var def in roster)
        {
            GD.Print($"GUARDIAN_REVIEW_BEGIN species={def.Id} archetype={def.Archetype}");
            world.Clock.Paused = true;

            double startY = world.GroundHeight(stageCenter);
            double targetY = world.GroundHeight(slopeSpot);
            var g = world.GuardianSystem.InitializeGuardian(def.Id)
                ?? throw new InvalidOperationException($"Failed to initialize guardian '{def.Id}'.");
            g.Position = new Vec3(stageCenter.X, startY, stageCenter.Z);
            g.TargetPosition = new Vec3(slopeSpot.X, targetY, slopeSpot.Z);
            g.Heading = Math.Atan2(slopeSpot.Z - stageCenter.Z, slopeSpot.X - stageCenter.X);
            g.Scale = 1.0;
            g.Activity = GuardianActivity.Rest;
            g.ActivityDuration = 10.0;

            session.Guardian.Build(world);
            for (int i = 0; i < 10; i++)
            {
                session.Guardian._Process(1.0 / 60.0);
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            // 1. Capture Idle Three-Quarter Close-Up Portrait
            float gY = (float)world.GroundHeight(g.PositionXZ);
            float bodyRadius = (float)Math.Max(0.32, def.VisualScale * GuardianDef.BodySizeMultiplier * 0.85);
            float eyeHeight = def.Archetype is "hushbill" or "crested_canopy" ? 0.55f : 0.38f;
            float dist = Math.Clamp(bodyRadius * 2.35f, 0.85f, 1.85f);
            float yaw = (float)g.Heading + 0.62f;
            var target = new Vector3((float)g.X, gY + eyeHeight * 0.52f, (float)g.Z);
            var eye = target + new Vector3(Mathf.Cos(yaw) * dist, eyeHeight * 0.72f, Mathf.Sin(yaw) * dist);
            session.CameraRig.LookAtPoint(eye, target);

            for (int i = 0; i < 12; i++)
            {
                session.Guardian._Process(1.0 / 60.0);
                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string closeupFile = $"{def.Archetype}_closeup.png";
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, closeupFile));

            // 2. Locomotion & Terrain Contact Pass (walk/explore across slope/roots for 60 frames)
            g.Activity = GuardianActivity.Explore; world.Clock.Paused = false;
            g.ActivityDuration = 15.0;
            g.TargetPosition = new Vec3(slopeSpot.X, targetY, slopeSpot.Z);

            var frameTimes = new List<double>(60);
            var sw = Stopwatch.StartNew();
            long lastTicks = sw.ElapsedTicks;
            for (int i = 0; i < 60; i++)
            {
                world.Step(VivariumWorld.Cadence.FaunaBehaviour);
                session.Guardian._Process(1.0 / 30.0);

                float curY = (float)world.GroundHeight(g.PositionXZ);
                float sideYaw = (float)g.Heading + 1.15f;
                var walkTarget = new Vector3((float)g.X, curY + eyeHeight * 0.48f, (float)g.Z);
                var walkEye = walkTarget + new Vector3(Mathf.Cos(sideYaw) * (dist * 1.15f), eyeHeight * 0.68f, Mathf.Sin(sideYaw) * (dist * 1.15f));
                session.CameraRig.LookAtPoint(walkEye, walkTarget);

                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
                long nowTicks = sw.ElapsedTicks;
                frameTimes.Add((nowTicks - lastTicks) * 1000.0 / Stopwatch.Frequency);
                lastTicks = nowTicks;
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string locoFile = $"{def.Archetype}_locomotion.png";
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, locoFile));

            // 3. Feeding / Foraging Behavior Pose
            g.Activity = GuardianActivity.Feed; world.Clock.Paused = false;
            g.ActivityDuration = 8.0;
            for (int i = 0; i < 20; i++)
            {
                world.Step(VivariumWorld.Cadence.FaunaBehaviour);
                session.Guardian._Process(1.0 / 30.0);

                float curY = (float)world.GroundHeight(g.PositionXZ);
                float sideYaw = (float)g.Heading + 1.15f;
                var feedTarget = new Vector3((float)g.X, curY + eyeHeight * 0.48f, (float)g.Z);
                var feedEye = feedTarget + new Vector3(Mathf.Cos(sideYaw) * (dist * 1.15f), eyeHeight * 0.68f, Mathf.Sin(sideYaw) * (dist * 1.15f));
                session.CameraRig.LookAtPoint(feedEye, feedTarget);

                await main.ToSignal(main.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await main.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string feedFile = $"{def.Archetype}_feed.png";
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, feedFile));

            frameTimes.Sort();
            double medianMs = frameTimes.Count > 0 ? frameTimes[frameTimes.Count / 2] : 0;

            var legContact = session.Guardian.LegContact;
            var appContact = session.Guardian.AppendageContact;
            var skinContact = session.Guardian.SkinContact;

            report[def.Archetype] = new
            {
                species_id = def.Id,
                display_name = def.Name,
                locomotion_mode = def.Locomotion.ToString(),
                frame_ms_median = Math.Round(medianMs, 3),
                fps_median = Math.Round(1000.0 / Math.Max(0.001, medianMs), 1),
                leg_ik_present = legContact != null,
                leg_skin_samples = legContact?.SkinFootSampleCount ?? 0,
                leg_max_sole_penetration_m = Math.Round(legContact?.MaximumSolePenetration ?? 0f, 4),
                leg_max_skin_penetration_m = Math.Round(legContact?.MaximumSkinFootPenetration ?? 0f, 4),
                leg_max_stance_gap_m = Math.Round(legContact?.MaximumStanceFootGap ?? 0f, 4),
                body_max_skin_penetration_m = Math.Round(legContact?.MaximumSkinBodyPenetration ?? 0f, 4),
                appendage_present = appContact != null,
                appendage_max_penetration_m = Math.Round(appContact?.MaximumGroundPenetration ?? 0f, 4),
                appendage_max_skin_penetration_m = Math.Round(appContact?.MaximumSkinTailPenetration ?? 0f, 4),
                skin_contact_valid = skinContact?.Valid ?? false,
                skin_contact_diag = skinContact?.Diagnostic ?? "none",
                screenshots = new[] { closeupFile, locoFile, feedFile }
            };
            GD.Print($"GUARDIAN_REVIEW_END species={def.Id} leg_ik={legContact != null} foot_samples={legContact?.SkinFootSampleCount ?? 0} appendage={appContact != null} skin_valid={skinContact?.Valid ?? false} fps={1000.0 / Math.Max(0.001, medianMs):0.0}");
        }

        File.WriteAllText(Path.Combine(directory, "guardian_review.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("GUARDIAN_REVIEW_COMPLETE " + directory);
        main.GetTree().Quit(0);
    }

    private static Vec2 FindStageSpot(VivariumWorld world, bool preferSlope)
    {
        Vec2 best = new Vec2(2.5, 2.5);
        double bestScore = double.NegativeInfinity;
        foreach (var c in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(c);
            if (!world.Domain.ContainsDisc(p, 1.5)) continue;
            if (world.Water.OpenWaterDepth(p) > 0.01) continue;
            if (world.PilotTree != null && world.PilotTree.BlocksTrunk(p, 1.4)) continue;
            if (world.Props.DistanceToFeature(p, "rock") < 0.9 || world.Props.DistanceToFeature(p, "log") < 0.9) continue;

            double ny = world.Terrain.Normal(p).Y;
            double distCenter = Math.Sqrt(p.X * p.X + p.Z * p.Z);
            double score = preferSlope
                ? (1.0 - ny) * 20.0 - Math.Abs(distCenter - 4.0) * 0.5
                : ny * 10.0 - Math.Abs(distCenter - 3.2) * 0.5;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        return best;
    }
}
