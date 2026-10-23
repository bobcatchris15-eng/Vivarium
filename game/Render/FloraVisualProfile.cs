using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

/// <summary>Editable visual recipes are independent of physiology and save files.</summary>
public sealed class FloraVisualProfile
{
    private sealed record PreparedSplit(MeshData Stem, MeshData Leaf);
    private static readonly ConditionalWeakTable<MeshData, PreparedSplit> PreparedSplits = new();
    private static PreparedSplit PrepareSplit(MeshData source)
    {
        var split = FloraVisualCompiler.Split(source);
        Bridge.PrepareMeshUpload(split.Stem);
        Bridge.PrepareMeshUpload(split.Leaf);
        return new(split.Stem, split.Leaf);
    }

    /// <summary>Prepares immutable completed geometry using managed arrays only; safe on mesh workers.</summary>
    public static void PrepareUpload(MeshData source) => PreparedSplits.GetValue(source, PrepareSplit);

    // Disposable review hook; ordinary gameplay leaves this unset.
    public static Func<string, MeshData, MeshData>? ReviewMeshTransform { get; set; }
    public string Species { get; set; } = "";
    public string PoolSpecies { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Volumetric { get; set; }
    public string Veins { get; set; } = "pinnate";
    public float[] Top { get; set; } = { .045f, .14f, .045f };
    public float[] Back { get; set; } = { .14f, .20f, .09f };
    public float Roughness { get; set; } = .65f;
    public float BackRoughness { get; set; } = .84f;
    public float Transmission { get; set; } = .28f;
    public float Relief { get; set; } = .48f;
    public float VeinStrength { get; set; } = .4f;
    public float Wind { get; set; } = 0.035f;
    public float TissueScale { get; set; } = 1.6f;
    public float CanopyOcclusion { get; set; } = .15f;
    public float MicroScale { get; set; } = 1f;
    public float MicroRelief { get; set; } = .025f;
    public float Striation { get; set; }
    public bool RotateTissue { get; set; }
    public float[] BarkTint { get; set; } = { .7f, .67f, .61f };
    public string Bark { get; set; } = "Bark014";

    private static readonly MethodInfo? LeafMethod =
        typeof(FloraSurfaceProfiles).GetMethod("Leaf", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo? BarkMethod =
        typeof(FloraSurfaceProfiles).GetMethod("Bark", BindingFlags.NonPublic | BindingFlags.Static);

    private static bool IsNonVascularId(string id) =>
        id.Contains("moss") || id.Contains("lichen") || id.Contains("fungus") || id.EndsWith("bonnet")
        || id is "dewbonnet" or "carrion_bell" or "glass_antlers" or "rain_jelly" or "bonecup" or "embercup" or "ghostbonnet";

    public static string FallbackPoolSpecies(string id, int leafType) => leafType switch
    {
        4 => "glassfinger",
        3 => "lanternbrush",
        5 => "lanternbrush",
        1 => "embercrown",
        2 => "embercrown",
        _ => (id is "shadebell" or "mooncoin" or "coinrunner" or "hookthicket") ? "shadebell" : "embercrown"
    };

    public static FloraVisualProfile? Load(string id, bool includeInactive = false, FloraSpeciesDef? sp = null)
    {
        string path = $"res://content/visuals/flora/{id}.json";
        if (Godot.FileAccess.FileExists(path))
        {
            var p = JsonSerializer.Deserialize<FloraVisualProfile>(Godot.FileAccess.GetFileAsString(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (p == null || p.Species != id) throw new InvalidOperationException($"Invalid flora visual recipe: {path}");
            if (!p.Enabled && !includeInactive) return null;
            return p;
        }

        // When JSON is not on disk, synthesize species-specific profile for vascular species
        if (sp != null)
        {
            if (sp.Archetype is "moss" or "lichen" or "fungus" or "slime" || (sp.Colony != null && sp.Archetype != "plant"))
                return null;
        }
        else if (IsNonVascularId(id))
        {
            return null;
        }

        return Synthesize(id, sp);
    }

    public static FloraVisualProfile Synthesize(string id, FloraSpeciesDef? sp = null)
    {
        Vector3 top = new(.055f, .16f, .045f);
        Vector3 back = new(.10f, .19f, .075f);
        int leafType = 0;
        float veinStrength = 0.45f;
        float sheen = 0.32f;
        float transmission = 0.28f;
        string barkAsset = Bridge.Surfaces.Bark;
        Vector3 barkTint = new(.45f, .40f, .35f);

        if (LeafMethod != null)
        {
            try
            {
                object leafObj = LeafMethod.Invoke(null, new object[] { id })!;
                var t = leafObj.GetType();
                top = (Vector3)(t.GetProperty("Top")?.GetValue(leafObj) ?? top);
                back = (Vector3)(t.GetProperty("Back")?.GetValue(leafObj) ?? back);
                leafType = (int)(t.GetProperty("Type")?.GetValue(leafObj) ?? leafType);
                veinStrength = (float)(t.GetProperty("Veins")?.GetValue(leafObj) ?? veinStrength);
                sheen = (float)(t.GetProperty("Sheen")?.GetValue(leafObj) ?? sheen);
                transmission = (float)(t.GetProperty("Transmission")?.GetValue(leafObj) ?? transmission);
            }
            catch { }
        }
        else if (sp != null && sp.Color != null && sp.Color.Length >= 3)
        {
            top = new((float)sp.Color[0], (float)sp.Color[1], (float)sp.Color[2]);
            if (sp.Color2 != null && sp.Color2.Length >= 3)
                back = new((float)sp.Color2[0], (float)sp.Color2[1], (float)sp.Color2[2]);
        }

        if (BarkMethod != null)
        {
            try
            {
                object barkObj = BarkMethod.Invoke(null, new object[] { id })!;
                var t = barkObj.GetType();
                barkAsset = (string)(t.GetField("Item1")?.GetValue(barkObj) ?? barkAsset);
                barkTint = (Vector3)(t.GetField("Item4")?.GetValue(barkObj) ?? barkTint);
            }
            catch { }
        }

        string veinStyle = leafType switch
        {
            1 => "needle",
            2 => "pinnate",
            3 => "leathery",
            4 => "succulent",
            5 => "slender",
            _ => (sp?.Shape is "cordate" or "peltate" || id is "mooncoin" or "coinrunner") ? "cordate" :
                 (sp?.Shape is "compound" ? "compound" : "pinnate")
        };

        string pool = Godot.FileAccess.FileExists($"res://Textures/LeafPools/{id}/color.res")
            ? id
            : FallbackPoolSpecies(id, leafType);

        bool isVolumetric = leafType == 4 || sp?.Shape is "succulent" or "stone" or "bulb" || id == "glassfinger";

        return new FloraVisualProfile
        {
            Species = id,
            PoolSpecies = pool,
            Enabled = true,
            Volumetric = isVolumetric,
            Veins = veinStyle,
            Top = new[] { top.X, top.Y, top.Z },
            Back = new[] { back.X, back.Y, back.Z },
            Roughness = Mathf.Clamp(0.70f - sheen * 0.18f, 0.20f, 0.85f),
            BackRoughness = 0.82f,
            Transmission = transmission,
            Relief = 0.35f,
            VeinStrength = veinStrength,
            Wind = 0.035f,
            TissueScale = 1.3f,
            CanopyOcclusion = 0.15f,
            MicroScale = 1.0f,
            MicroRelief = 0.025f,
            Striation = leafType == 5 ? 0.85f : (leafType == 1 ? 0.5f : 0f),
            RotateTissue = false,
            BarkTint = new[] { barkTint.X, barkTint.Y, barkTint.Z },
            Bark = barkAsset
        };
    }

    public ShaderMaterial LeafMaterial(bool animated = false)
    {
        var mat = Bridge.Shader("res://Shaders/leaf.gdshader");
        mat.SetShaderParameter("tissue_color", Pool("color"));
        mat.SetShaderParameter("tissue_normal", Pool("normal"));
        mat.SetShaderParameter("tissue_physical", Pool("physical"));

        string poolSp = !string.IsNullOrEmpty(PoolSpecies) ? PoolSpecies : Species;
        string veinSp = Godot.FileAccess.FileExists($"res://Textures/LeafPools/{Species}/vein_normal.png") ? Species : poolSp;
        if (!Godot.FileAccess.FileExists($"res://Textures/LeafPools/{veinSp}/vein_normal.png"))
            veinSp = "embercrown";

        mat.SetShaderParameter("vein_normal", GD.Load<Texture2D>($"res://Textures/LeafPools/{veinSp}/vein_normal.png"));
        mat.SetShaderParameter("vein_mask", GD.Load<Texture2D>($"res://Textures/LeafPools/{veinSp}/vein_mask.png"));
        mat.SetShaderParameter("top_pigment", new Vector3(Top[0], Top[1], Top[2]));
        mat.SetShaderParameter("back_pigment", new Vector3(Back[0], Back[1], Back[2]));
        mat.SetShaderParameter("leaf_roughness", Roughness);
        mat.SetShaderParameter("back_roughness", BackRoughness);
        mat.SetShaderParameter("transmission", Transmission);
        mat.SetShaderParameter("normal_strength", Relief);
        mat.SetShaderParameter("vein_strength", VeinStrength);
        mat.SetShaderParameter("wind_strength", animated ? Wind : 0f);
        mat.SetShaderParameter("tissue_scale", TissueScale);
        mat.SetShaderParameter("canopy_occlusion", CanopyOcclusion);
        mat.SetShaderParameter("volumetric", Volumetric);
        mat.SetShaderParameter("leaf_variegation", FloraSurfaceProfiles.Variegation(Species));
        return mat;
    }

    private Texture2DArray Pool(string kind)
    {
        string poolSp = !string.IsNullOrEmpty(PoolSpecies) ? PoolSpecies : Species;
        string path = $"res://Textures/LeafPools/{poolSp}/{kind}.res";
        if (!Godot.FileAccess.FileExists(path))
            path = $"res://Textures/LeafPools/embercrown/{kind}.res";
        var pool = GD.Load<Texture2DArray>(path);
        if (pool == null || (pool.GetWidth() != 1024 && pool.GetWidth() != 2048) || pool.GetLayers() != 4)
            throw new InvalidOperationException($"Invalid leaf pool {path}; rebuild using --flora-bake with a rendering device.");
        return pool;
    }

    public ArrayMesh Compile(MeshData source, ShaderMaterial stem, ShaderMaterial leaf)
    {
        var reviewTransform = ReviewMeshTransform;
        PreparedSplit split;
        if (reviewTransform != null)
        {
            // Review transforms may access native resources and remain on the
            // publishing thread. Never reuse an ordinary source's cached split.
            source = reviewTransform(Species, source);
            var transformed = FloraVisualCompiler.Split(source);
            split = new(transformed.Stem, transformed.Leaf);
        }
        else split = PreparedSplits.GetValue(source, PrepareSplit);
        var result = Bridge.ToArrayMesh(split.Stem, stem);
        using var blade = Bridge.ToArrayMesh(split.Leaf, leaf);
        if (blade.GetSurfaceCount() > 0)
        {
            using var arrays = blade.SurfaceGetArrays(0);
            var flags = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                | (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
            result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);
            result.SurfaceSetMaterial(result.GetSurfaceCount() - 1, leaf);
        }
        return result;
    }
}
