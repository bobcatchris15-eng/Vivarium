using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    private sealed record HabitatLightConfig(string Name, float Exposure, float AmbientMultiplier,
        float SkyContribution, float FillMultiplier, bool ColorAmbient);

    private async Task HabitatLightReviewAsync()
    {
        var rig = Session.EnvRig;
        var fill = rig.Fill;
        var species = W.Content.Flora.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var candidates = W.Grid.DomainCells.Select(cell =>
        {
            var p = W.Grid.CellCenter(cell);
            double rootClearance = W.PilotTree?.BlocksDisc(p, .6) == true ? 0 : 1;
            bool woodyNearby = W.Flora.Items.Any(plant => species.TryGetValue(plant.SpeciesId, out var definition)
                && definition.Woody != null && (plant.Position - p).LengthSq < .64);
            return new { Cell = cell, Point = p, Cover = W.AmbientGroundCover.Cover[cell],
                Fine = W.Litter.FineMass[cell], Coarse = W.Litter.CoarseMass[cell], RootClearance = rootClearance, WoodyNearby = woodyNearby };
        }).Where(c => W.Domain.ContainsDisc(c.Point, 1.0) && !W.Water.IsWet(c.Cell)
            && W.SubstrateAtCell(c.Point) == Vivarium.Sim.Content.Substrate.Soil
            && double.IsNaN(W.Props.PropTopAt(c.Point)) && c.RootClearance > 0 && !c.WoodyNearby && c.Cover < .35
            && c.Fine + c.Coarse > 0.0001)
          .OrderByDescending(c => c.Fine + c.Coarse).ThenBy(c => c.Cover).ThenBy(c => c.Cell).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No open dry soil cell with visible stored litter and woody clearance was available.");
        var chosen = candidates[0];
        var ground = Ground(chosen.Point);
        var views = new[]
        {
            (Name: "macro", Offset: new Vector3(.16f, .52f, .18f)),
            (Name: "medium", Offset: new Vector3(.10f, 1.15f, .12f)),
        };
        var configs = new[]
        {
            new HabitatLightConfig("rig-baseline", 0f, 1f, 0f, 1f, false),
            new HabitatLightConfig("exposure-only", 1.30f, 1f, 0f, 1f, false),
            new HabitatLightConfig("balanced-fill", 1.18f, 1.4f, .35f, 1.35f, false),
            new HabitatLightConfig("open-shade", 1.12f, 1.6f, 0f, 1.6f, true),
        };

        double originalTimeScale = Engine.TimeScale;
        bool rigWasProcessing = rig.IsProcessing();
        double? originalBenchmarkTime = rig.BenchmarkLightSeconds;
        float originalExposure = rig.Env.TonemapExposure;
        bool originalAutoExposure = rig.CameraAttributes.AutoExposureEnabled;
        float originalAmbientEnergy = rig.Env.AmbientLightEnergy;
        Color originalAmbientColor = rig.Env.AmbientLightColor;
        float originalSkyContribution = rig.Env.AmbientLightSkyContribution;
        Godot.Environment.AmbientSource originalAmbientSource = rig.Env.AmbientLightSource;
        float originalFillEnergy = fill.LightEnergy;
        float originalSunEnergy = rig.Sun.LightEnergy;
        Color originalFillColor = fill.LightColor, originalSunColor = rig.Sun.LightColor;
        Vector3 originalFillRotation = fill.RotationDegrees, originalSunRotation = rig.Sun.RotationDegrees;
        Transform3D originalCameraTransform = Session.CameraRig.GlobalTransform;
        bool ambientWasProcessing = Session.AmbientGround.IsProcessing();
        bool soilDetailWasProcessing = Session.SoilDetail.IsProcessing();
        string digestBefore = Vivarium.Sim.Persistence.WorldSerializer.Digest(W);
        var ambientBefore = AmbientPresentationSnapshot();
        rig.SetProcess(false);
        Session.AmbientGround.SetProcess(false);
        Session.SoilDetail.SetProcess(false);
        rig.CameraAttributes.AutoExposureEnabled = false;
        var captures = new System.Collections.Generic.List<object>();
        try
        {
            foreach (double phase in new[] { 60d, 420d })
            {
                rig.BenchmarkLightSeconds = phase;
                rig.Env.AmbientLightSource = originalAmbientSource;
                rig._Process(.016);
                float phaseAmbientEnergy = rig.Env.AmbientLightEnergy;
                float phaseSkyContribution = rig.Env.AmbientLightSkyContribution;
                var phaseAmbientSource = rig.Env.AmbientLightSource;
                float phaseFillEnergy = fill.LightEnergy;
                foreach (var config in configs)
                {
                    rig.Env.TonemapExposure = config.Exposure > 0 ? config.Exposure : originalExposure;
                    rig.Env.AmbientLightEnergy = phaseAmbientEnergy * config.AmbientMultiplier;
                    rig.Env.AmbientLightSource = config.ColorAmbient ? Godot.Environment.AmbientSource.Color
                        : config.Name == "balanced-fill" ? Godot.Environment.AmbientSource.Sky : phaseAmbientSource;
                    rig.Env.AmbientLightSkyContribution = config.Name == "rig-baseline" || config.Name == "exposure-only"
                        ? phaseSkyContribution : config.SkyContribution;
                    fill.LightEnergy = phaseFillEnergy * config.FillMultiplier;
                    foreach (var view in views)
                    {
                        Session.CameraRig.LookAtPoint(ground + view.Offset, ground);
                        await Frames(12);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = GetViewport().GetTexture().GetImage();
                        image.Convert(Image.Format.Rgb8);
                        string name = $"habitat_light_{(int)phase:D3}_{config.Name}_{view.Name}";
                        string path = Path.Combine(OutDir, name + ".png");
                        image.SavePng(path);
                        long clipped = 0, crushed = 0;
                        var pixels = image.GetData();
                        int channels = 3;
                        for (int p = 0; p + channels - 1 < pixels.Length; p += channels)
                        {
                            int max = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2]));
                            if (max >= 250) clipped++;
                            if (max <= 5) crushed++;
                        }
                        long total = (long)image.GetWidth() * image.GetHeight();
                        captures.Add(new
                        {
                            name, path, phase_seconds = phase, config = config.Name, view = view.Name,
                            camera_eye = new[] { Session.CameraRig.GlobalPosition.X, Session.CameraRig.GlobalPosition.Y, Session.CameraRig.GlobalPosition.Z },
                            camera_target = new[] { ground.X, ground.Y, ground.Z },
                            exposure = rig.Env.TonemapExposure, auto_exposure = rig.CameraAttributes.AutoExposureEnabled,
                            ambient_source = rig.Env.AmbientLightSource.ToString(), ambient_energy = rig.Env.AmbientLightEnergy,
                            ambient_sky_contribution = rig.Env.AmbientLightSkyContribution,
                            sun_energy = rig.Sun.LightEnergy, sun_color = rig.Sun.LightColor.ToHtml(), fill_energy = fill.LightEnergy,
                            ambient_color = rig.Env.AmbientLightColor.ToHtml(), agx = rig.Env.TonemapMode.ToString(),
                            agx_contrast = rig.Env.TonemapAgxContrast,
                            clipped_fraction = total == 0 ? 0 : (double)clipped / total,
                            crushed_fraction = total == 0 ? 0 : (double)crushed / total,
                        });
                    }
                }
            }
            // Production cameras retain auto exposure. Capture the balanced 420-second look with it
            // enabled as a separate pair after the matched fixed-exposure matrix.
            rig.BenchmarkLightSeconds = 420;
            rig.Env.AmbientLightSource = originalAmbientSource;
            rig._Process(.016);
            rig.Env.TonemapExposure = 1.18f;
            rig.Env.AmbientLightEnergy *= 1.4f;
            rig.Env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
            rig.Env.AmbientLightSkyContribution = .35f;
            fill.LightEnergy *= 1.35f;
            Engine.TimeScale = 1;
            rig.CameraAttributes.AutoExposureEnabled = true;
            foreach (var view in views)
            {
                Session.CameraRig.LookAtPoint(ground + view.Offset, ground);
                await Seconds(4);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.Convert(Image.Format.Rgb8);
                string name = $"habitat_light_420_balanced-fill_auto_{view.Name}";
                string path = Path.Combine(OutDir, name + ".png");
                image.SavePng(path);
                long clipped = 0, crushed = 0;
                var pixels = image.GetData();
                for (int p = 0; p + 2 < pixels.Length; p += 3)
                {
                    int max = Math.Max(pixels[p], Math.Max(pixels[p + 1], pixels[p + 2]));
                    if (max >= 250) clipped++;
                    if (max <= 5) crushed++;
                }
                long total = (long)image.GetWidth() * image.GetHeight();
                captures.Add(new
                {
                    name, path, phase_seconds = 420, config = "balanced-fill-auto", view = view.Name,
                    camera_eye = new[] { Session.CameraRig.GlobalPosition.X, Session.CameraRig.GlobalPosition.Y, Session.CameraRig.GlobalPosition.Z },
                    camera_target = new[] { ground.X, ground.Y, ground.Z },
                    exposure = rig.Env.TonemapExposure, auto_exposure = rig.CameraAttributes.AutoExposureEnabled,
                    ambient_source = rig.Env.AmbientLightSource.ToString(), ambient_energy = rig.Env.AmbientLightEnergy,
                    ambient_sky_contribution = rig.Env.AmbientLightSkyContribution,
                    sun_energy = rig.Sun.LightEnergy, sun_color = rig.Sun.LightColor.ToHtml(), fill_energy = fill.LightEnergy,
                    ambient_color = rig.Env.AmbientLightColor.ToHtml(), agx = rig.Env.TonemapMode.ToString(), agx_contrast = rig.Env.TonemapAgxContrast,
                    clipped_fraction = total == 0 ? 0 : (double)clipped / total, crushed_fraction = total == 0 ? 0 : (double)crushed / total,
                });
            }
        }
        finally
        {
            Engine.TimeScale = originalTimeScale;
            rig.BenchmarkLightSeconds = originalBenchmarkTime;
            rig.Env.TonemapExposure = originalExposure;
            rig.Env.AmbientLightEnergy = originalAmbientEnergy;
            rig.Env.AmbientLightSkyContribution = originalSkyContribution;
            rig.Env.AmbientLightSource = originalAmbientSource;
            rig.CameraAttributes.AutoExposureEnabled = originalAutoExposure;
            fill.LightEnergy = originalFillEnergy;
            rig.Sun.LightEnergy = originalSunEnergy;
            if (originalBenchmarkTime.HasValue) rig._Process(.016);
            rig.Env.AmbientLightColor = originalAmbientColor;
            fill.LightColor = originalFillColor; fill.RotationDegrees = originalFillRotation;
            rig.Sun.LightColor = originalSunColor; rig.Sun.RotationDegrees = originalSunRotation;
            Session.CameraRig.LookAtPoint(originalCameraTransform.Origin, originalCameraTransform.Origin - originalCameraTransform.Basis.Z);
            Session.AmbientGround.SetProcess(ambientWasProcessing);
            Session.SoilDetail.SetProcess(soilDetailWasProcessing);
            rig.SetProcess(rigWasProcessing);
        }
        string digestAfter = Vivarium.Sim.Persistence.WorldSerializer.Digest(W);
        var ambientAfter = AmbientPresentationSnapshot();
        bool restored = rig.Env.TonemapExposure == originalExposure
            && rig.CameraAttributes.AutoExposureEnabled == originalAutoExposure
            && rig.Env.AmbientLightEnergy == originalAmbientEnergy
            && rig.Env.AmbientLightColor == originalAmbientColor
            && rig.Env.AmbientLightSource == originalAmbientSource
            && rig.Env.AmbientLightSkyContribution == originalSkyContribution
            && fill.LightEnergy == originalFillEnergy && rig.Sun.LightEnergy == originalSunEnergy
            && fill.LightColor == originalFillColor && rig.Sun.LightColor == originalSunColor
            && fill.RotationDegrees.IsEqualApprox(originalFillRotation) && rig.Sun.RotationDegrees.IsEqualApprox(originalSunRotation)
            && Session.CameraRig.GlobalTransform.IsEqualApprox(originalCameraTransform) && rig.IsProcessing() == rigWasProcessing
            && Session.AmbientGround.IsProcessing() == ambientWasProcessing
            && Session.SoilDetail.IsProcessing() == soilDetailWasProcessing;
        bool worldUnchanged = digestBefore == digestAfter && JsonSerializer.Serialize(ambientBefore) == JsonSerializer.Serialize(ambientAfter);
        Check("habitat light sweep restores rig and presentation state", restored, $"processing={rig.IsProcessing()}, benchmark_time={rig.BenchmarkLightSeconds}");
        Check("habitat light sweep preserves world and ambient presentation", worldUnchanged, $"world_digest_match={digestBefore == digestAfter}");
        var report = new
        {
            actual_game_session = true, quality = rig.Quality, resolution = new[] { GetViewport().GetVisibleRect().Size.X, GetViewport().GetVisibleRect().Size.Y },
            source_cell = chosen.Cell, source_world_xz = new[] { chosen.Point.X, chosen.Point.Z },
            stored_fine_litter_mass = chosen.Fine, stored_coarse_litter_mass = chosen.Coarse,
            ambient_cover = chosen.Cover, root_clearance = "PilotTree.BlocksDisc(point, 0.6) false; no woody flora within 0.8 m; no PropTopAt hit",
            views = views.Select(view => new { view.Name, offset = new[] { view.Offset.X, view.Offset.Y, view.Offset.Z } }),
            phases_seconds = new[] { 60, 420 }, captures, capture_count = captures.Count,
            world_digest_before = digestBefore, world_digest_after = digestAfter,
            ambient_presentation_before = ambientBefore, ambient_presentation_after = ambientAfter,
            restored, world_unchanged = worldUnchanged, auto_exposure_disabled_for_matched_comparisons = true,
            note = "Opt-in comparison only. No production lighting gain was changed or persisted.",
        };
        File.WriteAllText(Path.Combine(OutDir, "habitat_light_review.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _facts["habitat_light_review"] = report;
    }

    private object AmbientPresentationSnapshot() => new
    {
        processing = Session.AmbientGround.IsProcessing(), visible = Session.AmbientGround.Visible,
        multimeshes = Session.AmbientGround.GetChildren().OfType<MultiMeshInstance3D>()
            .Select(node => new { name = node.Name.ToString(), visible = node.Visible, instances = node.Multimesh?.InstanceCount ?? 0 }).ToArray(),
    };
}
