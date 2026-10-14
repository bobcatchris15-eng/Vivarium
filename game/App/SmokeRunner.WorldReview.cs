using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Persistence;

namespace Vivarium.Game.App;

public partial class SmokeRunner
{
    // Diagnostic photographs of a real initial GameSession. No biological warmup or performance gate.
    private async Task WorldReviewAsync()
    {
        W.Clock.Paused = true;
        Session.Settings.Quality = 2;
        Session.ApplySettings();
        GetWindow().Size = new Vector2I(1600, 900);
        Session.Ui.Visible = false;
        Session.EnvRig.BenchmarkLightSeconds = 420;
        // Engine time is paused only in this disposable review process. Record the limitation:
        // the exact pre-pause global shader TIME is not exposed, so no identical phase claim.
        Engine.TimeScale = 0;
        await Frames(60);
        while (!Session.Flora.PopulationReady) await Frames(1);
        string before = WorldSerializer.Digest(W);
        var rows = new List<object>();
        foreach (var scene in BuildReferenceScenes())
        {
            Session.CameraRig.LookAtPoint(scene.Eye, scene.Target);
            await Frames(16);
            await Screenshot(scene.Name);
            rows.Add(new { name = scene.Name, eye = new[] {scene.Eye.X,scene.Eye.Y,scene.Eye.Z}, target = new[] {scene.Target.X,scene.Target.Y,scene.Target.Z} });
        }
        string after = WorldSerializer.Digest(W);
        Check("world review leaves paused ecology unchanged", before == after);
        _facts["world_review"] = new { seed=W.Descriptor.Seed, tick=W.Clock.Tick, flora=W.Flora.Count, fauna=W.Fauna.Count, digest_before=before, digest_after=after, resolution=new[]{1600,900}, quality=2, light_seconds=420, shader_time="Engine time paused after startup; exact initial TIME unmeasured", performance_evaluated=false, scenes=rows };
        File.WriteAllText(Path.Combine(OutDir,"world_review.json"),JsonSerializer.Serialize(_facts["world_review"],new JsonSerializerOptions{WriteIndented=true}));
    }
}
