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

    // ---- L1 settings read by LightProbeDemo / the driver
    public static bool SkyVis;
    public static float SkyVisVoxel = 0.10f, SkyVisStrength = 1f, SkyVisLeaf = 1f;
    public static int Frames;              // >0 overrides MeasureFrames of light_probe shots
    public static bool Dyn;                // add the dyn before/after shots
    public static HashSet<string>? ShotFilter;
    public static List<float> SunShots = new();
    /// <summary>Candidate A/C tuned values (see L1-cached-light.md).</summary>
    public static float CandAStrength = 1.0f;
    /// <summary>Ambient energy multiplier per DayFraction (x100) for candidate A (with the field) and C (SSAO only): replaces the SDFGI-lit ambient.</summary>
    public static readonly Dictionary<int, float> AmbientA = new() { [5] = 0.15f, [25] = 0.15f, [50] = 0.15f, [72] = 0.225f, [90] = 1.0f };
    public static readonly Dictionary<int, float> AmbientC = new() { [5] = 0.074f, [25] = 0.074f, [50] = 0.083f, [72] = 0.135f, [90] = 0.61f };
    /// <summary>Fixed exposure multiplier per DayFraction (x100), calibrated to the control's auto-exposure result.</summary>
    public static readonly Dictionary<int, float> FixedExposure = new() { [5] = 1.95f, [25] = 1.94f, [50] = 1.94f, [72] = 1.93f, [90] = 1.85f };
    private static ReprEnvDriver? _driver;

    private static ReprEnvDriver Driver(EnvironmentRig rig)
    {
        if (_driver == null || !GodotObject.IsInstanceValid(_driver)) { _driver = new ReprEnvDriver { Name = "ReprEnvDriver", Rig = rig, Env = rig.Env }; rig.AddChild(_driver); }
        return _driver;
    }

    public sealed record Result(Dictionary<string, string> Requested, List<string> Applied, List<string> Skipped);

    public static Result Apply(GameSession session, Viewport vp, string? spec)
    {
        var req = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var applied = new List<string>();
        var skipped = new List<string>();
        var order = new List<string>();
        SkyVis = false; SkyVisVoxel = 0.10f; SkyVisStrength = 1f; SkyVisLeaf = 1f; Frames = 0; Dyn = false; ShotFilter = null; SunShots = new();
        if (!string.IsNullOrWhiteSpace(spec))
            foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = pair.Split('=', 2);
                string k = kv[0].ToLowerInvariant(), v = kv.Length > 1 ? kv[1].ToLowerInvariant() : "1";
                req[k] = v;
                if (k == "all_off") order.Insert(0, k); else order.Add(k);
            }
        if (req.TryGetValue("candidate", out var cand))
        {
            string[] defaults = cand switch
            {
                "a" => new[] { "sdfgi=0", "ssao=0", "amb_src=color", "aa=fxaa", "fog=1", "sky_static=1", "reflprobe_static=1", "exposure=fixed", "skyvis=1",
                               "skyvis_strength=" + CandAStrength.ToString(Inv), "ambient_scale=autoa" },
                "c" => new[] { "sdfgi=0", "ssao=1", "amb_src=color", "aa=fxaa", "sky_static=1", "reflprobe_static=1", "exposure=fixed", "ambient_scale=autoc" },
                _ => Array.Empty<string>(),
            };
            foreach (var d in defaults)
            {
                var kv = d.Split('=', 2);
                if (!req.ContainsKey(kv[0])) { req[kv[0]] = kv[1]; order.Add(kv[0]); }
            }
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
            case "rig_freeze": rig.SetProcess(!on); Ok($"rig_freeze={v} (diagnostic: EnvironmentRig._Process disabled, lighting stays at its _Ready state)"); return true;
            case "refl_src": env.ReflectedLightSource = v == "disabled" ? Godot.Environment.ReflectionSource.Disabled : Godot.Environment.ReflectionSource.Sky; Ok($"refl_src={v}"); return true;
            case "amb_src": env.AmbientLightSource = v == "color" ? Godot.Environment.AmbientSource.Color : v == "disabled" ? Godot.Environment.AmbientSource.Disabled : Godot.Environment.AmbientSource.Sky; Ok($"amb_src={v}"); return true;
            case "sky_bg": env.BackgroundMode = Godot.Environment.BGMode.Color; env.BackgroundColor = new Color(0.20f, 0.23f, 0.27f); Ok("sky_bg=color (diagnostic: flat backdrop colour, Sky kept for ambient/reflection)"); return true;
            case "candidate": Ok($"candidate={v} (expanded)"); return true;
            case "skyvis": SkyVis = on; Ok($"skyvis={v}"); return true;
            case "skyvis_voxel": SkyVisVoxel = float.Parse(v, Inv); Ok($"skyvis_voxel={v}"); return true;
            case "skyvis_strength": SkyVisStrength = float.Parse(v, Inv); Ok($"skyvis_strength={v}"); return true;
            case "skyvis_leaf": SkyVisLeaf = float.Parse(v, Inv); Ok($"skyvis_leaf={v}"); return true;
            case "frames": Frames = int.Parse(v, Inv); Ok($"frames={v}"); return true;
            case "dyn": Dyn = on; Ok($"dyn={v}"); return true;
            case "shots": ShotFilter = new HashSet<string>(v.Split('+', StringSplitOptions.RemoveEmptyEntries)); Ok($"shots={v}"); return true;
            case "sunshots":
                foreach (var f in v.Split('+', StringSplitOptions.RemoveEmptyEntries)) SunShots.Add(float.Parse(f, Inv));
                Ok($"sunshots={v}"); return true;
            case "ambient_scale":
                if (v is "autoa" or "autoc") { Driver(rig).AmbientTable = v == "autoa" ? AmbientA : AmbientC; Ok($"ambient_scale={v} (per-DayFraction table, applied after the rig)"); }
                else { Driver(rig).AmbientScale = float.Parse(v, Inv); Ok($"ambient_scale={v} (applied after the rig ambient energy)"); }
                return true;
            case "ambient_a": case "ambient_c":
                foreach (var kvp in v.Split('+', StringSplitOptions.RemoveEmptyEntries)) { var q = kvp.Split(':'); (k == "ambient_a" ? AmbientA : AmbientC)[int.Parse(q[0], Inv)] = float.Parse(q[1], Inv); }
                Ok($"{k}={v}"); return true;
            case "exposure":
                {
                    var d = Driver(rig);
                    rig.CameraAttributes.AutoExposureEnabled = false;
                    if (v == "fixed") { d.UseExposureTable = true; Ok("exposure=fixed (auto exposure off; per-DayFraction table)"); }
                    else { d.ExposureConst = float.Parse(v, Inv); Ok($"exposure={v} (auto exposure off; constant multiplier)"); }
                    return true;
                }
            case "sky_static":
                if (!on) { Ok("sky_static=0 (no-op)"); return true; }
                Driver(rig).EnableSkyStatic(); Ok("sky_static=1 (sky material proxied; rig parameter writes no longer dirty the radiance map)"); return true;
            case "reflprobe_static":
                if (!on) { Ok("reflprobe_static=0 (no-op)"); return true; }
                ReflProbe(rig, "static", Ok); return true;
            case "reflprobe_opt": ReflProbe(rig, v, Ok); return true;
            case "aa":
                if (v is not ("taa" or "fxaa" or "msaa2" or "none")) throw new ArgumentException("aa must be taa|fxaa|msaa2|none");
                vp.UseTaa = v == "taa";
                vp.ScreenSpaceAA = v == "fxaa" ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
                vp.Msaa3D = v == "msaa2" ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
                Ok($"aa={v}"); return true;
            case "maxfps": Engine.MaxFps = int.Parse(v, Inv); Ok($"maxfps={v}"); return true;
        }
        return false;
    }

    /// <summary>Reflection probe variants, joined with '+': noshadow, nobox, noambient, interior, static (= the chosen fix).</summary>
    private static void ReflProbe(EnvironmentRig rig, string opts, Action<string> ok)
    {
        var p = rig.GetNodeOrNull<ReflectionProbe>("HabitatReflectionProbe");
        if (p == null) throw new InvalidOperationException("no HabitatReflectionProbe");
        foreach (var o in opts.Split('+', StringSplitOptions.RemoveEmptyEntries))
            switch (o)
            {
                case "noshadow": p.EnableShadows = false; break;
                case "nobox": p.BoxProjection = false; break;
                case "noambient": p.AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled; break;
                case "interior": p.Interior = true; break;
                case "always": p.UpdateMode = ReflectionProbe.UpdateModeEnum.Always; break;
                case "far": p.Position += new Vector3(0, 1000f, 0); break;
                case "tiny": p.Size = new Vector3(0.2f, 0.2f, 0.2f); break;
                case "static": p.Visible = false; break; // cost is per-pixel probe shading, not a re-render: cannot be cached, see L1 doc
                default: throw new ArgumentException("reflprobe_opt: " + o);
            }
        ok($"reflprobe_opt={opts}");
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

/// <summary>
/// Runs after EnvironmentRig each frame (high process priority) so overrides can post-adjust what the rig wrote,
/// without editing the rig. Only exists when an override that needs it is requested.
/// </summary>
internal partial class ReprEnvDriver : Node
{
    public EnvironmentRig Rig = null!;
    public Godot.Environment Env = null!;
    public float AmbientScale = 1f;
    public Dictionary<int, float>? AmbientTable;
    public bool UseExposureTable;
    public float? ExposureConst;
    private ShaderMaterial? _skyLive, _skyOrig;
    private ReflectionProbe? _probe;
    private static readonly string[] SkyParams = { "sun_color", "sky_top_color", "sky_horizon_color", "ground_bottom_color", "ground_horizon_color", "sun_energy", "light_direction", "sky_energy" };
    private readonly Dictionary<string, Variant> _pushed = new();
    public int SkyPushes;

    public ReprEnvDriver() { ProcessPriority = 100000; }

    public void EnableSkyStatic()
    {
        _skyOrig = Env.Sky.SkyMaterial as ShaderMaterial ?? throw new InvalidOperationException("sky material is not a ShaderMaterial");
        _skyLive = new ShaderMaterial { Shader = _skyOrig.Shader };
        foreach (var u in _skyOrig.Shader.GetShaderUniformList())
        {
            string name = u.AsString();
            _skyLive.SetShaderParameter(name, _skyOrig.GetShaderParameter(name));
        }
        foreach (var n in SkyParams) _pushed[n] = _skyOrig.GetShaderParameter(n);
        // The rig keeps writing _skyOrig every frame; nothing renders from it any more.
        Env.Sky.SkyMaterial = _skyLive;
    }

    public void EnableProbeStatic(ReflectionProbe p) { _probe = p; }

    private static double Diff(Variant a, Variant b)
    {
        if (a.VariantType == Variant.Type.Float) return Math.Abs(a.AsDouble() - b.AsDouble()) / (1.0 + Math.Abs(a.AsDouble()));
        if (a.VariantType == Variant.Type.Vector3) { var x = a.AsVector3(); var y = b.AsVector3(); return Math.Max(Math.Abs(x.X - y.X), Math.Max(Math.Abs(x.Y - y.Y), Math.Abs(x.Z - y.Z))); }
        return 0;
    }

    public override void _Process(double delta)
    {
        if (AmbientTable != null)
        {
            int key = (int)Math.Round((Rig.BenchmarkLightSeconds ?? 60.0) / 1200.0 * 100.0);
            int best = -1; foreach (var k in AmbientTable.Keys) if (best < 0 || Math.Abs(k - key) < Math.Abs(best - key)) best = k;
            Env.AmbientLightEnergy *= AmbientTable[best];
        }
        else if (AmbientScale != 1f) Env.AmbientLightEnergy *= AmbientScale;
        if (UseExposureTable || ExposureConst != null)
        {
            float m = ExposureConst ?? 1f;
            if (UseExposureTable)
            {
                int key = (int)Math.Round((Rig.BenchmarkLightSeconds ?? 60.0) / 1200.0 * 100.0);
                int best = -1; foreach (var k in ReprEnv.FixedExposure.Keys) if (best < 0 || Math.Abs(k - key) < Math.Abs(best - key)) best = k;
                m = ReprEnv.FixedExposure[best];
            }
            Rig.CameraAttributes.AutoExposureEnabled = false;
            Rig.CameraAttributes.ExposureMultiplier = m;
        }
        if (_skyLive != null && _skyOrig != null)
        {
            double worst = 0;
            foreach (var n in SkyParams) worst = Math.Max(worst, Diff(_skyOrig.GetShaderParameter(n), _pushed[n]));
            if (worst > 0.02)
            {
                foreach (var n in SkyParams) { var v = _skyOrig.GetShaderParameter(n); _pushed[n] = v; _skyLive.SetShaderParameter(n, v); }
                SkyPushes++;
            }
        }
    }
}
