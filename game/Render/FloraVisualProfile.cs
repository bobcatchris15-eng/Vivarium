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

    public const string FamilyCordateBullate = "CordateBullate";
    public const string FamilyRenyformVelvet = "RenyformVelvet";
    public const string FamilyLanceolateWaxy = "LanceolateWaxy";
    public const string FamilyPinnateFern = "PinnateFern";
    public const string FamilySucculentTranslucent = "SucculentTranslucent";
    public const string FamilyCarnivorousPitcher = "CarnivorousPitcher";
    public const string FamilyPeltateCoin = "PeltateCoin";
    public const string FamilyFenestrata = "Fenestrata";
    public const string FamilyStriateGraminoid = "StriateGraminoid";
    public const string FamilyAcicularNeedle = "AcicularNeedle";

    public string Species { get; set; } = "";
    public string PoolSpecies { get; set; } = "";
    public string MorphologicalFamily { get; set; } = "";
    public string Family
    {
        get => MorphologicalFamily;
        set => MorphologicalFamily = value;
    }
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

    public static string DetermineMorphologicalFamily(string id, FloraSpeciesDef? sp = null, int leafType = 0)
    {
        string shape = sp?.Shape?.ToLowerInvariant() ?? "";
        string lowerId = id.ToLowerInvariant();

        if (string.IsNullOrEmpty(shape) && sp == null)
        {
            var contentInfo = TryReadContentFlora(id);
            if (!string.IsNullOrEmpty(contentInfo.shape))
                shape = contentInfo.shape.ToLowerInvariant();
        }

        // 1. Carnivorous Pitchers / Traps
        if (lowerId is "pitcher_plant" or "snaptrap" or "blue_sundew"
            || shape.Contains("pitcher") || shape.Contains("snaptrap") || shape.Contains("sundew"))
            return FamilyCarnivorousPitcher;

        // 2. Succulents / Translucent Fleshy Leaves
        if (leafType == 4 || lowerId is "glassfinger" or "sunstone_rosette" or "fenhook" or "fenbead" or "marrowbloom" or "starbulb" or "tidebell"
            || shape.Contains("succulent") || shape.Contains("iceplant") || shape.Contains("pairedleaf") || shape.Contains("bulb") || shape.Contains("stone"))
            return FamilySucculentTranslucent;

        // 3. Ferns & Divided Pinnae Fronds
        if (leafType == 2 || lowerId is "veilfern" or "moonfern" or "vaultfern" or "silverweed" or "fencomb"
            || shape.Contains("fern") || shape.Contains("milfoil") || shape.Contains("whorl"))
            return FamilyPinnateFern;

        // 4. Grasses / Reeds / Linear Blades / Sedges
        if (leafType == 5 || lowerId is "frosttussock" or "glassrush" or "ringreed" or "veilblade" or "kinkcane" or "plumegrass" or "streamribbon" or "lanternsedge" or "coalspike" or "siltstars"
            || shape.Contains("reed") || shape.Contains("tussock") || shape.Contains("pampas") || shape.Contains("cattail") || shape.Contains("ribbon") || shape.Contains("curtain") || shape.Contains("cane") || shape.Contains("carpet"))
            return FamilyStriateGraminoid;

        // 5. Acicular / Spiny / Needle
        if (leafType == 1 || lowerId is "blackneedle" || shape.Contains("thistle") || shape.Contains("needle") || shape.Contains("spike"))
            return FamilyAcicularNeedle;

        // 6. Peltate / Coin / Round Leaves
        if (lowerId is "mooncoin" or "coinrunner" or "glasslily"
            || shape.Contains("roundleaf") || shape.Contains("creeper") || shape.Contains("pad") || shape.Contains("peltate") || shape.Contains("coin"))
            return FamilyPeltateCoin;

        // 7. Broadleaf Climbers & Bullate Cordate Leaves
        if (lowerId is "clinglace" or "spiralvine" or "hookthicket"
            || shape.Contains("vine") || shape.Contains("hookthicket") || (sp != null && sp.Climber != null) || shape.Contains("cordate"))
            return FamilyCordateBullate;

        // 8. Fenestrated / Dissected / Compound Foliage
        if (lowerId is "embercrown" or "trifold" or "prismstar" or "brooklace"
            || shape.Contains("compound") || shape.Contains("trifoliate") || shape.Contains("herb"))
            return FamilyFenestrata;

        // 9. Reniform / Soft Velvet Groundcover
        if (shape.Contains("reniform") || shape.Contains("velvet"))
            return FamilyRenyformVelvet;

        // 10. Lanceolate Waxy Foliage (Default for woody shrubs, canopy trees, and leathery broadleaves)
        if (leafType == 3 || (sp != null && (sp.Woody != null || sp.IsTree || sp.IsShrub))
            || shape.Contains("shrub") || shape.Contains("microcanopy") || shape.Contains("tree") || shape.Contains("floatleaf") || shape.Contains("bromeliad") || shape.Contains("canopy"))
            return FamilyLanceolateWaxy;

        return FamilyLanceolateWaxy;
    }

    public static string FallbackPoolSpecies(string id, int leafType, FloraSpeciesDef? sp = null)
    {
        string family = DetermineMorphologicalFamily(id, sp, leafType);
        return family switch
        {
            FamilySucculentTranslucent => "glassfinger",
            FamilyCordateBullate or FamilyPeltateCoin or FamilyRenyformVelvet => "shadebell",
            FamilyLanceolateWaxy or FamilyStriateGraminoid or FamilyCarnivorousPitcher => "lanternbrush",
            FamilyPinnateFern or FamilyFenestrata or FamilyAcicularNeedle => "embercrown",
            _ => (id is "shadebell" or "mooncoin" or "coinrunner" or "hookthicket") ? "shadebell" : "embercrown"
        };
    }

    public static string FallbackPoolSpecies(string id, int leafType) =>
        FallbackPoolSpecies(id, leafType, null);

    private static (string shape, Vector3? col, Vector3? col2, bool isClimber, bool isWoody) TryReadContentFlora(string id)
    {
        string simPath = $"res://content/flora/{id}.json";
        if (!Godot.FileAccess.FileExists(simPath)) return ("", null, null, false, false);
        try
        {
            string json = Godot.FileAccess.GetFileAsString(simPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string shape = "";
            Vector3? col = null, col2 = null;
            if (root.TryGetProperty("visual", out var v))
            {
                if (v.TryGetProperty("shape", out var s)) shape = s.GetString() ?? "";
                if (v.TryGetProperty("color", out var c) && c.GetArrayLength() >= 3)
                    col = new((float)c[0].GetDouble(), (float)c[1].GetDouble(), (float)c[2].GetDouble());
                if (v.TryGetProperty("color2", out var c2) && c2.GetArrayLength() >= 3)
                    col2 = new((float)c2[0].GetDouble(), (float)c2[1].GetDouble(), (float)c2[2].GetDouble());
            }
            bool isClimber = root.TryGetProperty("climber", out _);
            bool isWoody = root.TryGetProperty("woody", out _);
            return (shape, col, col2, isClimber, isWoody);
        }
        catch { return ("", null, null, false, false); }
    }

    public static FloraVisualProfile? Load(string id, bool includeInactive = false, FloraSpeciesDef? sp = null)
    {
        string path = $"res://content/visuals/flora/{id}.json";
        if (Godot.FileAccess.FileExists(path))
        {
            var p = JsonSerializer.Deserialize<FloraVisualProfile>(Godot.FileAccess.GetFileAsString(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (p == null || p.Species != id) throw new InvalidOperationException($"Invalid flora visual recipe: {path}");
            if (!p.Enabled && !includeInactive) return null;
            if (string.IsNullOrEmpty(p.PoolSpecies))
                p.PoolSpecies = id;
            if (string.IsNullOrEmpty(p.MorphologicalFamily))
                p.MorphologicalFamily = DetermineMorphologicalFamily(id, sp);
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
        else
        {
            var contentInfo = TryReadContentFlora(id);
            if (contentInfo.col.HasValue) top = contentInfo.col.Value;
            if (contentInfo.col2.HasValue) back = contentInfo.col2.Value;
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

        string family = DetermineMorphologicalFamily(id, sp, leafType);

        string veinStyle = family switch
        {
            FamilyAcicularNeedle => "needle",
            FamilyPinnateFern => "pinnate",
            FamilyCordateBullate => "cordate",
            FamilyPeltateCoin or FamilyRenyformVelvet => "peltate",
            FamilyStriateGraminoid => "striate",
            FamilySucculentTranslucent => "succulent",
            FamilyCarnivorousPitcher => "pitcher",
            FamilyFenestrata => "compound",
            FamilyLanceolateWaxy => "leathery",
            _ => leafType switch
            {
                1 => "needle",
                2 => "pinnate",
                3 => "leathery",
                4 => "succulent",
                5 => "slender",
                _ => (sp?.Shape is "cordate" or "peltate" || id is "mooncoin" or "coinrunner") ? "cordate" :
                     (sp?.Shape is "compound" ? "compound" : "pinnate")
            }
        };

        string pool = Godot.FileAccess.FileExists($"res://Textures/LeafPools/{id}/color.res")
            ? id
            : FallbackPoolSpecies(id, leafType, sp);

        bool isVolumetric = family == FamilySucculentTranslucent || leafType == 4 || sp?.Shape is "succulent" or "stone" or "bulb" or "iceplant" || id == "glassfinger";

        float relief = family switch
        {
            FamilyCordateBullate => 0.42f,
            FamilyPinnateFern => 0.36f,
            FamilySucculentTranslucent => 0.14f,
            FamilyAcicularNeedle => 0.38f,
            FamilyStriateGraminoid => 0.30f,
            FamilyFenestrata => 0.34f,
            FamilyPeltateCoin => 0.26f,
            _ => 0.32f
        };

        float striation = family == FamilyStriateGraminoid ? 0.85f : (family == FamilyAcicularNeedle ? 0.50f : (leafType == 5 ? 0.85f : (leafType == 1 ? 0.50f : 0f)));

        if (veinStrength == 0.45f)
        {
            veinStrength = family switch
            {
                FamilyPinnateFern => 0.65f,
                FamilySucculentTranslucent => 0.10f,
                FamilyCordateBullate => 0.45f,
                FamilyStriateGraminoid => 0.28f,
                FamilyAcicularNeedle => 0.42f,
                _ => 0.35f
            };
        }

        float backRoughness = family == FamilySucculentTranslucent ? 0.55f : 0.82f;

        float tissueScale = family switch
        {
            FamilyStriateGraminoid => 1.4f,
            FamilyCordateBullate => 1.2f,
            FamilyPinnateFern => 1.3f,
            FamilyPeltateCoin => 1.0f,
            _ => 1.25f
        };

        float microScale = family switch
        {
            FamilySucculentTranslucent => 0.8f,
            FamilyStriateGraminoid => 1.2f,
            FamilyPinnateFern => 1.1f,
            _ => 1.0f
        };

        float microRelief = family switch
        {
            FamilySucculentTranslucent => 0.014f,
            FamilyCordateBullate => 0.026f,
            FamilyStriateGraminoid => 0.025f,
            _ => 0.020f
        };

        float wind = family switch
        {
            FamilySucculentTranslucent => 0.012f,
            FamilyStriateGraminoid => 0.045f,
            FamilyPinnateFern => 0.040f,
            FamilyCordateBullate => 0.032f,
            _ => 0.030f
        };

        return new FloraVisualProfile
        {
            Species = id,
            PoolSpecies = pool,
            MorphologicalFamily = family,
            Enabled = true,
            Volumetric = isVolumetric,
            Veins = veinStyle,
            Top = new[] { top.X, top.Y, top.Z },
            Back = new[] { back.X, back.Y, back.Z },
            Roughness = Mathf.Clamp(0.70f - sheen * 0.18f, 0.20f, 0.85f),
            BackRoughness = backRoughness,
            Transmission = transmission,
            Relief = relief,
            VeinStrength = veinStrength,
            Wind = wind,
            TissueScale = tissueScale,
            CanopyOcclusion = 0.15f,
            MicroScale = microScale,
            MicroRelief = microRelief,
            Striation = striation,
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
            veinSp = FallbackPoolSpecies(Species, 0);
        if (!Godot.FileAccess.FileExists($"res://Textures/LeafPools/{veinSp}/vein_normal.png"))
            veinSp = "embercrown";

        mat.SetShaderParameter("vein_normal", GD.Load<Texture2D>($"res://Textures/LeafPools/{veinSp}/vein_normal.png"));
        mat.SetShaderParameter("vein_mask", GD.Load<Texture2D>($"res://Textures/LeafPools/{veinSp}/vein_mask.png"));
        mat.SetShaderParameter("top_pigment", new Vector3(Top[0], Top[1], Top[2]));
        mat.SetShaderParameter("back_pigment", new Vector3(Back[0], Back[1], Back[2]));
        mat.SetShaderParameter("leaf_roughness", Roughness);
        mat.SetShaderParameter("back_roughness", BackRoughness);
        mat.SetShaderParameter("cuticle_sheen", Mathf.Clamp((0.70f - Roughness) / 0.18f, 0.05f, 0.85f));
        mat.SetShaderParameter("transmission", Transmission);
        mat.SetShaderParameter("normal_strength", Relief);
        mat.SetShaderParameter("vein_strength", VeinStrength);
        mat.SetShaderParameter("wind_strength", 0f);
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
            poolSp = FallbackPoolSpecies(Species, 0);
        path = $"res://Textures/LeafPools/{poolSp}/{kind}.res";
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
