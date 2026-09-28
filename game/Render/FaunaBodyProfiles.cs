using System;
using Godot;
using Vivarium.Sim.Content;

namespace Vivarium.Game.Render;

/// <summary>Body-specific additions shared by live rendering and pose previews.</summary>
public static class FaunaBodyProfiles
{
    public static int Style(FaunaSpeciesDef sp) => sp.Model switch
    {
        "salamander" => 1, "toad" => 2, "harvestman" => 3, "slug" => 4, "worm" => 5,
        "beetle" => 6, "silverfish" => 7, "isopod" or "pill_bug" => 8,
        "shrimp" => 9, "triops" => 10, "minnow" => 11, "snail" => 12,
        "moth" => 13, "midge" => 14, "springtail" => 15, "aquatic_larva" => 16, _ => 0,
    };
    public static bool Bends(FaunaSpeciesDef sp) => sp.Animation.Family == FaunaAnimationFamily.Metachronal
        || Style(sp) is 1 or 4 or 5 or 7 or 8 or 9 or 10 or 11 or 12 or 16;
    public static float PackHue(FaunaSpeciesDef sp, float hue, float bend, bool crawl = false)
        => sp.Model == "toad" ? hue + (crawl ? 4 : 0) : Bends(sp) ? hue + 2 * (1 + (int)Math.Round((Math.Clamp(bend, -1.25f, 1.25f) / 1.25f + 1) * 15)) : hue;
    public static void Bind(ShaderMaterial mat, FaunaSpeciesDef sp)
    {
        var a = sp.Animation;
        mat.SetShaderParameter("anim_body_style", Style(sp));
        mat.SetShaderParameter("toad_crawl_stroke", (float)Vivarium.Sim.Geometry.FaunaGait.HalfStroke(Vivarium.Sim.Geometry.ToadLocomotion.Crawl));
        mat.SetShaderParameter("toad_crawl_duty", (float)Vivarium.Sim.Geometry.ToadLocomotion.Crawl.DutyFactor);
        mat.SetShaderParameter("anim_half_stroke", (float)Vivarium.Sim.Geometry.FaunaGait.HalfStroke(a));
        mat.SetShaderParameter("anim_family", (int)a.Family);
        mat.SetShaderParameter("anim_amplitude", (float)a.Amplitude);
        mat.SetShaderParameter("anim_idle_motion", (float)a.IdleMotion);
        mat.SetShaderParameter("anim_body_wave", (float)a.BodyWave);
        mat.SetShaderParameter("anim_limb_sweep", (float)a.LimbSweep);
        mat.SetShaderParameter("anim_limb_lift", (float)a.LimbLift);
        mat.SetShaderParameter("anim_bob", (float)a.Bob);
        mat.SetShaderParameter("anim_phase_spread", (float)a.PhaseSpread);
        mat.SetShaderParameter("anim_duty_factor", (float)a.DutyFactor);
        // Production and review captures must use identical surface parameters.
        mat.SetShaderParameter("translucency", sp.Model is "shrimp" or "minnow" ? 0.35f : sp.Model == "moth" ? 0.07f : 0.1f);
        mat.SetShaderParameter("carapace", sp.Model switch { "isopod" => 0.15f, "triops" => 0.45f, "shrimp" => 0.35f,
            "beetle" => 0.3f, "silverfish" => 0.2f, "millipede" or "snail" => 0.12f, _ => 0.0f });
        mat.SetShaderParameter("segment_rings", sp.Model switch { "springtail" => 5.5f, "silverfish" => 4.5f, _ => 0.0f });
        mat.SetShaderParameter("bloom", sp.Model switch { "springtail" => 1.0f, "isopod" => 0.85f,
            "silverfish" or "beetle" => 0.7f, "triops" or "shrimp" or "minnow" => 0.1f, "moth" => 0.9f, _ => 0.6f });
        mat.SetShaderParameter("wet", sp.Model is "shrimp" or "minnow" or "triops" or "aquatic_larva" or "slug" or "worm" or "snail" ? 1.0f : 0.0f);
        mat.SetShaderParameter("scales", sp.Model is "minnow" or "silverfish" ? 1.0f : 0.0f);
    }
}
