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
    public static float PackHue(FaunaSpeciesDef sp, float hue, float bend)
        => Bends(sp) ? hue + 2 * (1 + (int)Math.Round((Math.Clamp(bend, -1.25f, 1.25f) / 1.25f + 1) * 15)) : hue;
    public static void Bind(ShaderMaterial mat, FaunaSpeciesDef sp)
    {
        var a = sp.Animation;
        mat.SetShaderParameter("anim_body_style", Style(sp));
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
    }
}
