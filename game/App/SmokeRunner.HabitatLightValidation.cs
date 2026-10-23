using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    private sealed record HabitatLightingSample(
        double RequestedSeconds,
        double ClockSeconds,
        string ClockPhase,
        int Quality,
        bool AutoExposure,
        float ToneMapExposure,
        string AmbientSource,
        float AmbientEnergy,
        string AmbientColor,
        float AmbientSkyContribution,
        float CanopyEnergy,
        float SunEnergy,
        string SunColor,
        Vector3 SunRotation,
        float FillEnergy,
        string FillColor,
        Vector3 FillRotation,
        float NightFactor);

    private sealed record HabitatPixelSummary(int Width, int Height, int MaxRgb, long NonBlackPixels, long Pixels);
    private sealed record HabitatCapture(HabitatLightingSample Sample, string ImagePath, HabitatPixelSummary Pixels);
    private sealed record HabitatQualitySample(int Tier, HabitatLightingSample Sample, bool MatchesReference);

    private async Task HabitatLightValidationAsync()
    {
        var rig = Session.EnvRig;
        var camera = Session.CameraRig;
        var fill = rig.Fill;
        var species = W.Content.Flora.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var candidates = W.Grid.DomainCells.Select(cell =>
        {
            var point = W.Grid.CellCenter(cell);
            bool treeClear = W.PilotTree?.BlocksDisc(point, .6) != true;
            bool woodyClear = !W.Flora.Items.Any(plant => species.TryGetValue(plant.SpeciesId, out var definition)
                && definition.Woody != null && (plant.Position - point).LengthSq < .64);
            return new
            {
                Clearance = W.Flora.Items.Count == 0 ? 10d : W.Flora.Items.Min(plant => (plant.Position - point).Length),
                PropClearance = Math.Min(W.Props.Rocks.Select(rock => (rock.Position - point).Length - rock.FootprintRadius).DefaultIfEmpty(10d).Min(),
                    W.Props.Logs.Select(log => log.AxisDistance(point) - log.Radius).DefaultIfEmpty(10d).Min()),
                Cell = cell,
                Point = point,
                FineMass = W.Litter.FineMass[cell],
                CoarseMass = W.Litter.CoarseMass[cell],
                Cover = W.AmbientGroundCover.Cover[cell],
                TreeClear = treeClear,
                WoodyClear = woodyClear,
            };
        }).Where(candidate => W.Domain.ContainsDisc(candidate.Point, 1.0)
            && !W.Water.IsWet(candidate.Cell)
            && W.SubstrateAtCell(candidate.Point) == Vivarium.Sim.Content.Substrate.Soil
            && double.IsNaN(W.Props.PropTopAt(candidate.Point))
            && candidate.TreeClear && candidate.WoodyClear && candidate.PropClearance > .65
            && candidate.FineMass + candidate.CoarseMass > .0001)
          .OrderByDescending(candidate => candidate.Clearance)
          .ThenByDescending(candidate => candidate.FineMass + candidate.CoarseMass)
          .ThenBy(candidate => candidate.Cover)
          .ThenBy(candidate => candidate.Cell)
          .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("No open dry soil cell with visible stored litter and woody clearance was available.");

        var chosen = candidates[0];
        var ground = Ground(chosen.Point);
        var originalEye = camera.Position;
        var originalForward = -camera.Basis.Z;
        var originalTarget = originalEye + originalForward * 3f;
        var originalBenchmarkTime = rig.BenchmarkLightSeconds;
        var originalExposureEnabled = rig.CameraAttributes.AutoExposureEnabled;
        var originalToneMapExposure = rig.Env.TonemapExposure;
        var originalQualitySetting = Session.Settings.Quality;
        var originalEngineTimeScale = Engine.TimeScale;
        var originalWorldPaused = W.Clock.Paused;
        var originalUiVisible = Session.Ui.Visible;
        var originalCameraProcessing = camera.IsProcessing();
        var originalCameraKeyboardBlocked = camera.KeyboardBlocked;
        var originalCameraMedium = camera.Medium.Underwater;
        var originalUnderwaterVisual = rig.UnderwaterVisual;
        var originalFloraProcessing = Session.Flora.IsProcessing();
        var originalFaunaProcessing = Session.Fauna.IsProcessing();
        var originalGuardianProcessing = Session.Guardian.IsProcessing();
        var originalAmbientProcessing = Session.AmbientGround.IsProcessing();
        var originalSoilDetailProcessing = Session.SoilDetail.IsProcessing();
        string digestBefore = WorldSerializer.Digest(W);
        long tickBefore = W.Clock.Tick;
        var before = CaptureLightingSnapshot(rig.Clock.Seconds, rig);
        var fixedCaptures = new List<HabitatCapture>();
        var qualityRows = new List<HabitatQualitySample>();
        var voidRows = new List<object>();
        object? autoExposureCapture = null;
        object? wrap = null;
        bool settingsRestored = false;
        bool worldUnchanged = false;

        // Freeze world-owned and presentation-owned render updates while the camera and
        // environment are sampled. The world clock remains the simulation authority and stays paused.
        W.Clock.Paused = true;
        Session.Flora.SetProcess(false);
        Session.Fauna.SetProcess(false);
        Session.Guardian.SetProcess(false);
        Session.AmbientGround.SetProcess(false);
        Session.SoilDetail.SetProcess(false);
        Session.Ui.Visible = false;
        camera.KeyboardBlocked = true;

        try
        {
            Engine.TimeScale = 0;
            rig.CameraAttributes.AutoExposureEnabled = false;
            camera.LookAtPoint(ground + new Vector3(.16f, .7f, .18f), ground);
            var productionPhases = new[] { 60d, 420d, 780d, 1080d };
            foreach (double phase in productionPhases)
            {
                rig.BenchmarkLightSeconds = phase;
                rig._Process(.016);
                var sample = CaptureLightingSample(phase, rig);
                var image = await CaptureHabitatImage($"habitat_production_{phase:0000}_fixed");
                fixedCaptures.Add(new HabitatCapture(sample, image.Path, image.Summary));
            }

            var day = fixedCaptures[0].Sample;
            var night = fixedCaptures[3].Sample;
            Check("production habitat lighting values are finite",
                fixedCaptures.All(row => SampleIsFinite(row.Sample)),
                "fixed-exposure production samples at 60, 420, 780, and 1080 seconds");
            Check("production lighting samples follow the declared clock phases",
                fixedCaptures.Select(row => row.Sample.ClockPhase).SequenceEqual(new[] { "Morning", "Afternoon", "Dusk", "Night" }),
                string.Join(", ", fixedCaptures.Select(row => $"{row.Sample.ClockSeconds:R}:{row.Sample.ClockPhase}")));
            Check("night sun energy remains below ten percent of daylight",
                night.SunEnergy < day.SunEnergy * .1f,
                $"day={day.SunEnergy:R}, night={night.SunEnergy:R}, ratio={night.SunEnergy / Math.Max(day.SunEnergy, 1e-8f):R}");
            Check("night flora factor follows the production night interval",
                day.NightFactor <= .01f && night.NightFactor >= .99f,
                $"day={day.NightFactor:R}, night={night.NightFactor:R}");

            // Give Godot's practical auto exposure real wall time to adapt while the world
            // remains paused and the presentation renderers listed above remain stopped.
            rig.BenchmarkLightSeconds = 420;
            rig.CameraAttributes.AutoExposureEnabled = true;
            Engine.TimeScale = 1;
            await Seconds(4);
            rig._Process(.016);
            var autoSample = CaptureLightingSample(420, rig);
            var autoImage = await CaptureHabitatImage("habitat_production_0420_auto_settled");
            autoExposureCapture = new { sample = autoSample, image_path = autoImage.Path, pixels = autoImage.Summary, settle_seconds = 4 };
            Check("daytime auto-exposure sample has finite production lighting",
                SampleIsFinite(autoSample), JsonSerializer.Serialize(autoSample));

            // Quality tiers may alter rendering cost, but must not alter the sampled
            // production lighting. Reapply the same fixed phase after each settings update.
            Engine.TimeScale = 0;
            rig.CameraAttributes.AutoExposureEnabled = false;
            HabitatLightingSample? qualityBaseline = null;
            for (int tier = 0; tier <= 2; tier++)
            {
                Session.Settings.Quality = tier;
                Session.ApplySettings();
                rig.BenchmarkLightSeconds = 420;
                rig._Process(.016);
                var sample = CaptureLightingSample(420, rig);
                bool matches = qualityBaseline == null || SamePhotometry(qualityBaseline, sample, 1e-5f);
                qualityRows.Add(new HabitatQualitySample(tier, sample, matches));
                if (qualityBaseline == null) qualityBaseline = sample;
            }
            Check("quality tiers preserve production photometric values",
                qualityRows.All(row => row.MatchesReference && SampleIsFinite(row.Sample)),
                JsonSerializer.Serialize(qualityRows));

            rig.BenchmarkLightSeconds = 1199.99;
            rig._Process(.016);
            var beforeWrap = CaptureLightingSample(1199.99, rig);
            rig.BenchmarkLightSeconds = 0;
            rig._Process(.016);
            var afterWrap = CaptureLightingSample(0, rig);
            bool wrapContinuous = SamePhotometry(beforeWrap, afterWrap, .03f)
                && beforeWrap.NightFactor <= .01f && afterWrap.NightFactor <= .01f;
            wrap = new { before = beforeWrap, after = afterWrap, continuous = wrapContinuous, tolerance = .03f };
            Check("day-night lighting and night flora wrap continuously at 1200 seconds", wrapContinuous,
                $"sun {beforeWrap.SunEnergy:R}/{afterWrap.SunEnergy:R}, ambient {beforeWrap.AmbientEnergy:R}/{afterWrap.AmbientEnergy:R}, night {beforeWrap.NightFactor:R}/{afterWrap.NightFactor:R}");
            rig.BenchmarkLightSeconds = 1170;
            rig._Process(.016);
            Check("final dawn key is sampled before the cycle wraps",
                Math.Abs(rig.Sun.LightEnergy - .22f * .92f) < .00001f,
                $"1170-second sun={rig.Sun.LightEnergy:R}");

            // Point away from the habitat from outside its domain. Disable camera medium
            // tracking so the explicitly forced underwater screen treatment remains set.
            camera.SetProcess(false);
            float outsideZ = (float)W.Domain.Radius + 5f;
            var voidEye = new Vector3(0, ground.Y + 3f, outsideZ);
            var voidTarget = voidEye + new Vector3(0, 0, 10f);
            camera.LookAtPoint(voidEye, voidTarget);
            rig.BenchmarkLightSeconds = 60;
            rig.CameraAttributes.AutoExposureEnabled = false;
            rig.SetUnderwater(false);
            Session.Water.SetUnderwater(false);
            rig._Process(.3);
            var aboveVoid = await CaptureHabitatImage("habitat_void_above_water");
            bool aboveBlack = aboveVoid.Summary.MaxRgb <= 1 && aboveVoid.Summary.NonBlackPixels == 0;
            voidRows.Add(new { medium = "above_water", outside_domain = true, image_path = aboveVoid.Path, pixels = aboveVoid.Summary, black_void = aboveBlack, no_visible_floor = aboveBlack });

            rig.SetUnderwater(true);
            Session.Water.SetUnderwater(true);
            rig._Process(.3);
            var underwaterVoid = await CaptureHabitatImage("habitat_void_underwater");
            bool underwaterBlack = underwaterVoid.Summary.MaxRgb <= 1 && underwaterVoid.Summary.NonBlackPixels == 0;
            voidRows.Add(new { medium = "underwater", outside_domain = true, underwater_visual = rig.UnderwaterVisual, image_path = underwaterVoid.Path, pixels = underwaterVoid.Summary, black_void = underwaterBlack, no_visible_floor = underwaterBlack });
            Check("black void remains empty above water and underwater",
                aboveBlack && underwaterBlack && rig.UnderwaterVisual,
                JsonSerializer.Serialize(voidRows));
        }
        finally
        {
            Session.Settings.Quality = originalQualitySetting;
            Session.ApplySettings();
            rig.CameraAttributes.AutoExposureEnabled = originalExposureEnabled;
            rig.Env.TonemapExposure = originalToneMapExposure;
            rig.BenchmarkLightSeconds = originalBenchmarkTime;
            camera.LookAtPoint(originalEye, originalTarget);
            camera.KeyboardBlocked = originalCameraKeyboardBlocked;
            camera.SetProcess(originalCameraProcessing);
            W.Clock.Paused = originalWorldPaused;
            Session.Flora.SetProcess(originalFloraProcessing);
            Session.Fauna.SetProcess(originalFaunaProcessing);
            Session.Guardian.SetProcess(originalGuardianProcessing);
            Session.AmbientGround.SetProcess(originalAmbientProcessing);
            Session.SoilDetail.SetProcess(originalSoilDetailProcessing);
            Session.Ui.Visible = originalUiVisible;
            rig.SetUnderwater(originalUnderwaterVisual);
            Session.Water.SetUnderwater(originalCameraMedium);
            rig._Process(.3);
            Engine.TimeScale = originalEngineTimeScale;
            await Frames(2);
            rig._Process(.016);
        }

        var after = CaptureLightingSnapshot(rig.Clock.Seconds, rig);
        string digestAfter = WorldSerializer.Digest(W);
        settingsRestored = Session.Settings.Quality == originalQualitySetting
            && rig.CameraAttributes.AutoExposureEnabled == originalExposureEnabled
            && rig.Env.TonemapExposure == originalToneMapExposure
            && rig.BenchmarkLightSeconds == originalBenchmarkTime
            && Math.Abs(camera.Position.DistanceTo(originalEye)) < .0001f
            && camera.Basis.Z.DistanceTo(-originalForward) < .0001f
            && camera.KeyboardBlocked == originalCameraKeyboardBlocked
            && camera.IsProcessing() == originalCameraProcessing
            && Session.Flora.IsProcessing() == originalFloraProcessing
            && Session.Fauna.IsProcessing() == originalFaunaProcessing
            && Session.Guardian.IsProcessing() == originalGuardianProcessing
            && Session.AmbientGround.IsProcessing() == originalAmbientProcessing
            && Session.SoilDetail.IsProcessing() == originalSoilDetailProcessing
            && Session.Ui.Visible == originalUiVisible
            && W.Clock.Paused == originalWorldPaused
            && Math.Abs(Engine.TimeScale - originalEngineTimeScale) < 1e-6;
        worldUnchanged = digestBefore == digestAfter && tickBefore == W.Clock.Tick;
        Check("habitat production validation restores camera, settings, processing, and clocks", settingsRestored,
            $"quality={Session.Settings.Quality}, time_scale={Engine.TimeScale:R}, benchmark={rig.BenchmarkLightSeconds}");
        Check("habitat production validation leaves paused world unchanged", worldUnchanged,
            $"digest_match={digestBefore == digestAfter}, tick={tickBefore}->{W.Clock.Tick}");

        var report = new
        {
            actual_game_session = true,
            production_rig_untouched = true,
            selected_cell = chosen.Cell,
            selected_xz = new[] { chosen.Point.X, chosen.Point.Z },
            ground = new[] { ground.X, ground.Y, ground.Z },
            camera_offset = new[] { .16f, .7f, .18f },
            fixed_exposure_samples = fixedCaptures,
            auto_exposure_420 = autoExposureCapture,
            quality_photometry = qualityRows,
            cycle_wrap = wrap,
            void_checks = voidRows,
            before,
            after,
            world_digest_before = digestBefore,
            world_digest_after = digestAfter,
            tick_before = tickBefore,
            tick_after = W.Clock.Tick,
            settings_restored = settingsRestored,
            world_unchanged = worldUnchanged,
            note = "Photometric checks compare configured production rig values and captured pixels. Auto exposure settled for four wall-clock seconds while the simulation clock remained paused. Raw shader TIME can advance during this wait.",
        };
        _facts["habitat_light_production_validation"] = report;
        File.WriteAllText(Path.Combine(OutDir, "habitat_light_production_validation.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

        Check("production habitat captures were written",
            fixedCaptures.Count == 4 && autoExposureCapture != null && voidRows.Count == 2,
            $"fixed={fixedCaptures.Count}, auto={(autoExposureCapture != null)}, void={voidRows.Count}");
    }

    private async Task<(string Path, HabitatPixelSummary Summary)> CaptureHabitatImage(string name)
    {
        await Frames(3);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgb8);
        string path = Path.Combine(OutDir, name + ".png");
        Error save = image.SavePng(path);
        if (save != Error.Ok) throw new IOException($"Could not save habitat light capture '{path}': {save}");
        var bytes = image.GetData();
        int maximum = 0;
        long nonBlack = 0;
        for (int i = 0; i + 2 < bytes.Length; i += 3)
        {
            int pixelMax = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
            maximum = Math.Max(maximum, pixelMax);
            if (pixelMax > 1) nonBlack++;
        }
        _facts["screenshot_" + name] = path;
        return (path, new HabitatPixelSummary(image.GetWidth(), image.GetHeight(), maximum, nonBlack,
            (long)image.GetWidth() * image.GetHeight()));
    }

    private static HabitatLightingSample CaptureLightingSample(double requestedSeconds, EnvironmentRig rig) => new(
        requestedSeconds,
        rig.Clock.Seconds,
        rig.Clock.Phase.ToString(),
        rig.Quality,
        rig.CameraAttributes.AutoExposureEnabled,
        rig.Env.TonemapExposure,
        rig.Env.AmbientLightSource.ToString(),
        rig.Env.AmbientLightEnergy,
        rig.Env.AmbientLightColor.ToHtml(),
        rig.Env.AmbientLightSkyContribution,
        rig.CanopyEnergy,
        rig.Sun.LightEnergy,
        rig.Sun.LightColor.ToHtml(),
        rig.Sun.RotationDegrees,
        rig.Fill.LightEnergy,
        rig.Fill.LightColor.ToHtml(),
        rig.Fill.RotationDegrees,
        NightFloraMaterials.CurrentNightFactor);

    private static HabitatLightingSample CaptureLightingSnapshot(double requestedSeconds, EnvironmentRig rig) =>
        CaptureLightingSample(requestedSeconds, rig);

    private static bool SampleIsFinite(HabitatLightingSample sample) =>
        double.IsFinite(sample.RequestedSeconds) && double.IsFinite(sample.ClockSeconds)
        && float.IsFinite(sample.ToneMapExposure) && float.IsFinite(sample.AmbientEnergy)
        && float.IsFinite(sample.AmbientSkyContribution) && float.IsFinite(sample.CanopyEnergy)
        && float.IsFinite(sample.SunEnergy) && float.IsFinite(sample.SunRotation.X)
        && float.IsFinite(sample.SunRotation.Y) && float.IsFinite(sample.SunRotation.Z)
        && float.IsFinite(sample.FillEnergy) && float.IsFinite(sample.FillRotation.X)
        && float.IsFinite(sample.FillRotation.Y) && float.IsFinite(sample.FillRotation.Z)
        && float.IsFinite(sample.NightFactor);

    private static bool SamePhotometry(HabitatLightingSample a, HabitatLightingSample b, float tolerance) =>
        Math.Abs(a.SunEnergy - b.SunEnergy) <= tolerance
        && Math.Abs(a.FillEnergy - b.FillEnergy) <= tolerance
        && Math.Abs(a.AmbientEnergy - b.AmbientEnergy) <= tolerance
        && Math.Abs(a.ToneMapExposure - b.ToneMapExposure) <= tolerance
        && a.AmbientColor == b.AmbientColor
        && a.AmbientSource == b.AmbientSource
        && a.AutoExposure == b.AutoExposure
        && Math.Abs(a.AmbientSkyContribution - b.AmbientSkyContribution) <= tolerance
        && Math.Abs(a.CanopyEnergy - b.CanopyEnergy) <= tolerance
        && a.SunColor == b.SunColor && a.FillColor == b.FillColor
        && Math.Abs(a.SunRotation.X - b.SunRotation.X) <= tolerance
        && AngleDeltaDegrees(a.SunRotation.Y, b.SunRotation.Y) <= tolerance
        && Math.Abs(a.SunRotation.Z - b.SunRotation.Z) <= tolerance
        && Math.Abs(a.FillRotation.X - b.FillRotation.X) <= tolerance
        && AngleDeltaDegrees(a.FillRotation.Y, b.FillRotation.Y) <= tolerance
        && Math.Abs(a.FillRotation.Z - b.FillRotation.Z) <= tolerance;

    private static float AngleDeltaDegrees(float a, float b)
    {
        float difference = Math.Abs(a - b) % 360f;
        return Math.Min(difference, 360f - difference);
    }
}
