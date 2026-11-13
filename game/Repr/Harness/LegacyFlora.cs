using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Repr;

/// <summary>
/// Synchronous re-run of FloraRenderer.StartSpeciesBuild for a single specimen (that method is private and only
/// builds layers for species present in the world, and the review world is unpopulated). Calls the same
/// builders with the same parameters.
/// </summary>
internal static class LegacyFlora
{
    private sealed record Species(FloraSpeciesDef Def, FloraVisualProfile? Profile, ShaderMaterial Stem, ShaderMaterial? Leaf, int MorphCount);
    private static readonly Dictionary<string, Species> Cache = new(StringComparer.Ordinal);

    private static Species? Get(ReprDemoContext ctx, string id)
    {
        if (Cache.TryGetValue(id, out var s)) return s;
        var sp = ctx.World.Content.FloraById(id);
        if (sp == null) return null;
        var profile = FloraVisualProfile.Load(sp.Id, sp: sp);
        int morphs = FloraGeometryPlacement.MorphCount(sp.Shape);
        var mat = Bridge.Shader("res://Shaders/flora.gdshader");
        NightFloraMaterials.Bind(mat, sp);
        if (sp.Shape == "ashcomb") mat.SetShaderParameter("dry_fungal_cuticle", true);
        mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
        mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, _ => 2 });
        mat.SetShaderParameter("deform_leaf_tips", morphs == 12);
        mat.SetShaderParameter("sway", 0.0f);
        Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
        FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
        var leafMat = profile?.LeafMaterial(animated: false);
        if (leafMat != null) NightFloraMaterials.Bind(leafMat, sp);
        if (profile != null)
        {
            Bridge.BindSurface(mat, "bark", profile.Bark);
            mat.SetShaderParameter("bark_tint", new Vector3(profile.BarkTint[0], profile.BarkTint[1], profile.BarkTint[2]));
            leafMat!.SetShaderParameter("plant_stiffness", sp.Woody != null ? 7f : 3f);
            leafMat.SetShaderParameter("wind_strength", 0.0f);
        }
        return Cache[id] = new Species(sp, profile, mat, leafMat, morphs);
    }

    public static Material? Material(ReprDemoContext ctx, string id, bool leaf)
    {
        var s = Get(ctx, id);
        if (s == null) return null;
        return leaf ? (s.Leaf ?? s.Stem) : s.Stem;
    }

    public static Node3D? Build(ReprDemoContext ctx, string id, int lodTier, int morph, bool juvenile)
    {
        var s = Get(ctx, id);
        if (s == null) { ctx.Log($"BuildLegacyFlora: unknown species '{id}'"); return null; }
        var sp = s.Def;
        lodTier = Math.Clamp(lodTier, 0, OrganismMeshes.FloraLodTiers - 1);
        morph = Math.Clamp(morph, 0, s.MorphCount - 1);
        ulong speciesSeed = Hash.Fnv1a64("flora.visual." + sp.Id);
        ulong seed = Rng.Mix(speciesSeed, (ulong)(morph + 1) * 0x9E3779B97F4A7C15UL);
        MeshData mesh = sp.Climber != null
            ? OrganismMeshes.ClimberNodeTier(sp, seed, attached: !juvenile, lodTier)
            : OrganismMeshes.FloraTier(sp, seed, lodTier, juvenile: juvenile, tieredSource: s.Profile != null,
                farGillStations: FloraRenderer.DistantGillStations, farEmberMicroSides: FloraRenderer.DistantEmberMicroSides,
                farDewbonnetCapRings: FloraRenderer.DistantDewbonnetCapRings);
        var arr = s.Profile == null ? Bridge.ToArrayMesh(mesh, s.Stem) : s.Profile.Compile(mesh, s.Stem, s.Leaf!);
        bool canCast = sp.Colony == null && sp.Archetype is "plant" or "fungus" && sp.Height >= 0.055;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = arr, InstanceCount = 1 };
        double r = sp.RadiusAtMax > 0 ? sp.RadiusAtMax : 0.12, h = sp.Height;
        if (juvenile) { r *= 0.5; h *= 0.5; }
        if (sp.Id == "siltstars") h /= .08;
        Transform3D t = sp.Climber != null ? Transform3D.Identity
            : new Transform3D(Basis.Identity.ScaledLocal(new Vector3((float)r, (float)h, (float)r)), Vector3.Zero);
        mm.SetInstanceTransform(0, t);
        ulong hash = Hash.Fnv1a64("repr.legacy." + sp.Id);
        mm.SetInstanceColor(0, Colors.White);
        mm.SetInstanceCustomData(0, new Color((hash % 1000) / 1000f, 1f, 0f, ((hash >> 12) % 1000) / 1000f));
        var holder = new Node3D { Name = $"Legacy_{sp.Id}" };
        holder.AddChild(new MultiMeshInstance3D
        {
            Name = $"Flora_{sp.Id}_{lodTier * s.MorphCount + morph}",
            Multimesh = mm,
            CastShadow = canCast ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return holder;
    }
}
