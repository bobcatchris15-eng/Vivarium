using System;
using System.Text.Json;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

/// <summary>Editable visual recipes are independent of physiology and save files.</summary>
public sealed class FloraVisualProfile
{
    public string Species { get; set; } = "";
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
    public float Wind { get; set; } = .018f;
    public float TissueScale { get; set; } = 1.6f;
    public float CanopyOcclusion { get; set; } = .15f;
    public float MicroScale { get; set; } = 1f;
    public float MicroRelief { get; set; } = .025f;
    public float Striation { get; set; }
    public bool RotateTissue { get; set; }
    public float[] BarkTint { get; set; } = { .7f, .67f, .61f };
    public string Bark { get; set; } = "Bark014";

    public static FloraVisualProfile? Load(string id, bool includeInactive = false)
    {
        string path = $"res://content/visuals/flora/{id}.json";
        if (!Godot.FileAccess.FileExists(path)) return null;
        var p = JsonSerializer.Deserialize<FloraVisualProfile>(Godot.FileAccess.GetFileAsString(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (p == null || p.Species != id) throw new InvalidOperationException($"Invalid flora visual recipe: {path}");
        if (!p.Enabled && !includeInactive) return null;
        return p;
    }

    public ShaderMaterial LeafMaterial(bool animated = true)
    {
        var mat = Bridge.Shader("res://Shaders/leaf.gdshader");
        mat.SetShaderParameter("tissue_color", Pool("color"));
        mat.SetShaderParameter("tissue_normal", Pool("normal"));
        mat.SetShaderParameter("tissue_physical", Pool("physical"));
        mat.SetShaderParameter("vein_normal", GD.Load<Texture2D>($"res://Textures/LeafPools/{Species}/vein_normal.png"));
        mat.SetShaderParameter("vein_mask", GD.Load<Texture2D>($"res://Textures/LeafPools/{Species}/vein_mask.png"));
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
        return mat;
    }

    private Texture2DArray Pool(string kind)
    {
        string path = $"res://Textures/LeafPools/{Species}/{kind}.res";
        var pool = GD.Load<Texture2DArray>(path);
        if (pool == null || pool.GetWidth() != 1024 || pool.GetLayers() != 4)
            throw new InvalidOperationException($"Invalid leaf pool {path}; rebuild using --flora-bake with a rendering device.");
        return pool;
    }

    public ArrayMesh Compile(MeshData source, ShaderMaterial stem, ShaderMaterial leaf)
    {
        var split = FloraVisualCompiler.Split(source);
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
