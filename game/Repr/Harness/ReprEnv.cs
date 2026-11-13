using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Vivarium.Game.App;
using Vivarium.Game.Render;

namespace Vivarium.Repr;

/// <summary>
/// <c>--repr-env k=v[,k=v...]</c>: render-stack overrides applied after the tier-1 configuration and before any shot.
/// Keys that cannot be applied at runtime are reported as skipped, never faked.
/// </summary>
public static class ReprEnv
{
    private static CultureInfo Inv => CultureInfo.InvariantCulture;

    public sealed record Result(Dictionary<string, string> Requested, List<string> Applied, List<string> Skipped);

    public static Result Apply(GameSession session, Viewport vp, string? spec)
    {
        var req = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        var skipped = new List<string>();
        var order = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec))
            foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = pair.Split('=', 2);
                string k = kv[0].ToLowerInvariant(), v = kv.Length > 1 ? kv[1].ToLowerInvariant() : "1";
                req[k] = v;
                if (k == "all_off") order.Insert(0, k); else order.Add(k);
            }
        var rig = session.EnvRig;
        var env = rig.Env;
        foreach (var k in order)
        {
            string v = req[k];
            bool on = v is "1" or "true" or "on";
            try
            {
                if (!Set(session, rig, env, vp, k, v, on, applied, skipped)) skipped.Add($"{k}={v}: unknown key or bad value");
            }
            catch (Exception ex) { skipped.Add($"{k}={v}: {ex.Message}"); }
        }
        foreach (var a in applied) GD.Print("REPR_ENV applied " + a);
        foreach (var s in skipped) GD.Print("REPR_ENV skipped " + s);
        return new Result(req, applied, skipped);
    }

    private static bool Set(GameSession session, EnvironmentRig rig, Godot.Environment env, Viewport vp, string k, string v, bool on, List<string> applied, List<string> skipped)
    {
        void Ok(string s) => applied.Add(s);
        switch (k)
        {
            case "all_off":
                if (!on) { Ok("all_off=0 (no-op)"); return true; }
                env.SdfgiEnabled = false; env.SsaoEnabled = false; env.GlowEnabled = false; env.SsilEnabled = false;
                rig.VolumetricMistEnabled = false; env.VolumetricFogEnabled = false;
                vp.UseTaa = false;
                SetProbe(rig, false);
                rig.CameraAttributes.AutoExposureEnabled = false;
                Ok("all_off=1 (sdfgi, ssao, ssil, fog, glow, taa, reflprobe, autoexp off; shadows kept)");
                return true;
            case "sdfgi": env.SdfgiEnabled = on; Ok($"sdfgi={v}"); return true;
            case "ssao": env.SsaoEnabled = on; Ok($"ssao={v}"); return true;
            case "fog":
                rig.VolumetricMistEnabled = on; env.VolumetricFogEnabled = on; // EnvironmentRig._Process re-derives the enable flag from VolumetricMistEnabled
                Ok($"fog={v} (volumetric fog; note global fog was already off)"); return true;
            case "glow": env.GlowEnabled = on; Ok($"glow={v}"); return true;
            case "taa": vp.UseTaa = on; Ok($"taa={v}"); return true;
            case "shadows": rig.Sun.ShadowEnabled = on; Ok($"shadows={v}"); return true;
            case "atlas":
                RenderingServer.DirectionalShadowAtlasSetSize(int.Parse(v, Inv), true); Ok($"atlas={v}"); return true;
            case "splits":
                rig.Sun.DirectionalShadowMode = v switch
                {
                    "1" => DirectionalLight3D.ShadowMode.Orthogonal,
                    "2" => DirectionalLight3D.ShadowMode.Parallel2Splits,
                    "4" => DirectionalLight3D.ShadowMode.Parallel4Splits,
                    _ => throw new ArgumentException("splits must be 1|2|4"),
                };
                Ok($"splits={v}"); return true;
            case "scale":
                vp.Scaling3DScale = float.Parse(v, Inv); Ok($"scale={v}"); return true;
            case "reflprobe":
                if (!SetProbe(rig, on)) { skipped.Add($"reflprobe={v}: HabitatReflectionProbe node not present in this scene"); return true; }
                Ok($"reflprobe={v}"); return true;
            case "autoexp": rig.CameraAttributes.AutoExposureEnabled = on; Ok($"autoexp={v}"); return true;
            case "fill": rig.Fill.Visible = on; Ok($"fill={v}"); return true;
            case "sky":
                if (!on) { Ok("sky=0 (flat ambient colour: BackgroundMode=Color, no Sky, AmbientSource=Color, Reflected=Disabled)"); FlatSky(env); return true; }
                Ok("sky=1 (no-op, default)"); return true;
            case "msaa":
                vp.Msaa3D = v switch { "0" => Viewport.Msaa.Disabled, "2" => Viewport.Msaa.Msaa2X, "4" => Viewport.Msaa.Msaa4X, _ => throw new ArgumentException("msaa must be 0|2|4") };
                Ok($"msaa={v}"); return true;
            case "fxaa": vp.ScreenSpaceAA = on ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled; Ok($"fxaa={v}"); return true;
            case "halfres_ssao":
                RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, on, 0.5f, 2, 50f, 300f); Ok($"halfres_ssao={v}"); return true;
            case "sdfgi_cascades": env.SdfgiCascades = int.Parse(v, Inv); Ok($"sdfgi_cascades={v}"); return true;
            case "sdfgi_cell": env.SdfgiMinCellSize = float.Parse(v, Inv); Ok($"sdfgi_cell={v}"); return true;
            case "maxfps": Engine.MaxFps = int.Parse(v, Inv); Ok($"maxfps={v}"); return true;
        }
        return false;
    }

    private static bool SetProbe(EnvironmentRig rig, bool on)
    {
        var p = rig.GetNodeOrNull<ReflectionProbe>("HabitatReflectionProbe");
        if (p == null) return false;
        p.Visible = on;
        return true;
    }

    private static void FlatSky(Godot.Environment env)
    {
        // EnvironmentRig re-drives AmbientLightColor/Energy every frame, so the flat ambient keeps the same energy curve.
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.25f, 0.30f, 0.34f);
        env.Sky = null;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
    }
}
