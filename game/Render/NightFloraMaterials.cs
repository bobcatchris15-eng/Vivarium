using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Vivarium.Sim.Content;

namespace Vivarium.Game.Render;

/// <summary>Presentation-only nocturnal emission shared by ordinary flora and photographed-leaf materials.</summary>
public static class NightFloraMaterials
{
    private static readonly List<WeakReference<ShaderMaterial>> Materials = new();
    private const float EmissionGain = 0.075f;
    public static float CurrentNightFactor {get;private set;}
    public static float LightEnergy(FloraSpeciesDef species)=>(float)Math.Clamp(species.BioluminescenceStrength*.025,0,.065);
    public static float LightRange(FloraSpeciesDef species)=>(float)Math.Clamp(species.RadiusAtMax*1.1+species.Height*.18+.12,.2,.95);

    private sealed class RegisteredLight
    {
        public readonly WeakReference<OmniLight3D> Light;
        public readonly ulong Id;
        public float BaseEnergy;
        public RegisteredLight(OmniLight3D light, float baseEnergy)
        {
            Light = new WeakReference<OmniLight3D>(light);
            Id = light.GetInstanceId();
            BaseEnergy = baseEnergy;
        }
    }
    private static readonly List<RegisteredLight> Lights = new();
    /// <summary>Instance id to registration. The registry is consulted once per
    /// luminous plant per refresh, so a linear scan turned a mature world into a
    /// hundred milliseconds of work every refresh.</summary>
    private static readonly Dictionary<ulong, RegisteredLight> LightIndex = new();

    public static void Bind(ShaderMaterial material, FloraSpeciesDef species)
    {
        var color = species.BioluminescenceColor;
        float strength = (float)Math.Clamp(species.BioluminescenceStrength, 0.0, 8.0);
        var tint = color is { Length: >= 3 }
            ? new Vector3((float)color[0], (float)color[1], (float)color[2])
            : Vector3.Zero;
        material.SetShaderParameter("bioluminescence_color", tint);
        material.SetShaderParameter("bioluminescence_strength", strength * EmissionGain);
        // Organ-specific emission mask modes:
        // 0: default vascular foliage
        // 1: exposed body surface / colonial
        // 2: marked nodules (glowbriar underside mint nodules)
        // 3: gills only (ghostbonnet / fungus gills)
        // 4: seed heads only (lanternsedge pod cylinder)
        // 5: center stripe down leaf (siltstars)
        // 6: edge strip around leaf (tidebell)
        // 7: clover chevron marking (trifold)
        // 8: spore beads on the fertile fronds (veilfern)
        int maskMode = species.Id switch
        {
            "glowbriar" => 2,
            "ghostbonnet" => 3,
            "lanternsedge" => 4,
            "siltstars" => 5,
            "tidebell" => 6,
            "trifold" => 7,
            "veilfern" => 2,
            // The brake's berries carry a lit heart; nothing else in the thicket glows.
            "hookthicket" => 2,
            // The latchjaw's flower throat is the only lit part of the plant.
            "snaptrap" => 2,
            "starmoss" => 1,
            // The coalspike's violet ember is the one lit organ; the coal around it stays dead.
            "coalspike" => 2,
            // The embercup's spore capsules glow; the cushion they stand on does not.
            "embercup" => 2,
            // The hollowcrown's cavity moss is the only light inside the tree.
            "hollowcrown" => 2,
            _ => species.Archetype is "moss" or "lichen" || (species.Shape == "carpet" && species.Id != "siltstars") ? 1 : 0
        };
        material.SetShaderParameter("emission_mask_mode", maskMode);
        material.SetShaderParameter("night_factor", CurrentNightFactor);
        if (strength <= 0 || tint == Vector3.Zero) return;
        if (!Materials.Any(w => w.TryGetTarget(out var old) && ReferenceEquals(old, material)))
            Materials.Add(new WeakReference<ShaderMaterial>(material));
    }

    public static void RegisterLight(OmniLight3D light, float baseEnergy)
    {
        if (light == null) return;
        light.LightEnergy=baseEnergy*CurrentNightFactor;
        light.Visible=light.LightEnergy>.00001f;
        ulong id=light.GetInstanceId();
        if(LightIndex.TryGetValue(id,out var existing) && existing.Light.TryGetTarget(out var known) && ReferenceEquals(known,light))
        {
            existing.BaseEnergy=baseEnergy;
            return;
        }
        // A new light, or one whose instance id was recycled after the old
        // light was freed.
        var registered=new RegisteredLight(light,baseEnergy);
        Lights.Add(registered);
        LightIndex[id]=registered;
    }

    public static OmniLight3D CreateLightForSpecies(FloraSpeciesDef species, Vector3 position = default)
    {
        var color = species.BioluminescenceColor;
        float strength = (float)Math.Clamp(species.BioluminescenceStrength, 0.0, 8.0);
        var tint = color is { Length: >= 3 }
            ? new Color((float)color[0], (float)color[1], (float)color[2])
            : Colors.White;
        float baseEnergy = LightEnergy(species);
        float range = LightRange(species);
        var light = new OmniLight3D
        {
            Position = position,
            LightColor = tint,
            OmniRange = range,
            OmniAttenuation = 1.7f,
            ShadowEnabled = false,
            LightEnergy = 0f,
            Visible = false
        };
        RegisterLight(light, baseEnergy);
        return light;
    }

    public static float NightFactorAtSeconds(double seconds)
    {
        const double cycle = 1200.0;
        double t = ((seconds % cycle) + cycle) % cycle;
        return (float)(Smooth(660.0, 900.0, t) * (1.0 - Smooth(1140.0, 1200.0, t)));
    }

    public static void SetNightFactor(float factor)
    {
        factor = Mathf.Clamp(factor, 0f, 1f);
        CurrentNightFactor=factor;
        for (int i = Materials.Count - 1; i >= 0; i--)
        {
            if (!Materials[i].TryGetTarget(out var material) || !GodotObject.IsInstanceValid(material))
            {
                Materials.RemoveAt(i);
                continue;
            }
            material.SetShaderParameter("night_factor", factor);
        }

        for (int i = Lights.Count - 1; i >= 0; i--)
        {
            if (!Lights[i].Light.TryGetTarget(out var light) || !GodotObject.IsInstanceValid(light))
            {
                LightIndex.Remove(Lights[i].Id);
                Lights.RemoveAt(i);
                continue;
            }
            float energy = Lights[i].BaseEnergy * factor;
            light.LightEnergy = energy;
            light.Visible = energy > 0.00001f;
        }
    }

    private static double Smooth(double a, double b, double t)
    {
        double x = Math.Clamp((t - a) / (b - a), 0.0, 1.0);
        return x * x * (3.0 - 2.0 * x);
    }
}
