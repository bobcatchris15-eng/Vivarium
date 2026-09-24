using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;
using SimVec2 = Vivarium.Sim.Core.Vec2;
using SimVec3 = Vivarium.Sim.Core.Vec3;

namespace Vivarium.Game.App;

/// <summary>Reads content from res://content (works identically in the editor and in the exported pck).</summary>
public sealed class GodotContentSource : IContentSource
{
    private const string Root = "res://content/";
    public string Describe => Root;
    public bool Exists(string p) => FileAccess.FileExists(Root + p);
    public string ReadText(string p)
    {
        using var f = FileAccess.Open(Root + p, FileAccess.ModeFlags.Read);
        if (f == null) throw new System.IO.IOException($"cannot open {Root + p}: {FileAccess.GetOpenError()}");
        return f.GetAsText();
    }
}

public static class Bridge
{
    public static Vector3 V(SimVec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static Vector3 V(SimVec2 v, double y) => new((float)v.X, (float)y, (float)v.Z);
    public static SimVec3 S(Vector3 v) => new(v.X, v.Y, v.Z);
    public static SimVec2 SXZ(Vector3 v) => new(v.X, v.Z);
    public static Color C(double[] rgb, float a = 1) => new((float)rgb[0], (float)rgb[1], (float)rgb[2], a);

    /// <summary>
    /// Sim rotations are counter-clockwise in the XZ plane (X toward +Z). Godot's rotation about +Y turns
    /// X toward −Z, so the Godot yaw is the negated sim angle.
    /// </summary>
    public static Basis Yaw(double simAngle) => new(Vector3.Up, (float)-simAngle);

    public static ArrayMesh ToArrayMesh(MeshData m, Material? material = null, ArrayMesh? reuse = null)
    {
        var mesh = reuse ?? new ArrayMesh();
        mesh.ClearSurfaces();
        if (m.VertexCount == 0 || m.Indices.Count == 0) return mesh;
        int n = m.VertexCount;
        var verts = new Vector3[n]; var norms = new Vector3[n]; var cols = new Color[n]; var uv = new Vector2[n]; var uv2 = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            norms[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            cols[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
            uv[i] = new Vector2(m.UV[i * 2], m.UV[i * 2 + 1]);
            uv2[i] = new Vector2(m.UV2[i * 2], m.UV2[i * 2 + 1]);
        }
        using var arrays = new Godot.Collections.Array();   // release the native array now, not at GC finalization
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = norms;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = m.Indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null) mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    public static ShaderMaterial Shader(string path)
    {
        var sh = GD.Load<Shader>(path) ?? throw new InvalidOperationException($"missing shader {path}");
        return new ShaderMaterial { Shader = sh };
    }

    /// <summary>
    /// Binds a photo-scanned surface set (res://Textures/&lt;asset&gt;_1K-JPG_{Color,NormalGL,Roughness}.jpg, CC0 from
    /// ambientCG) to the shader uniforms &lt;prefix&gt;_col, &lt;prefix&gt;_nrm and &lt;prefix&gt;_rgh.
    /// </summary>
    public static void BindSurface(ShaderMaterial mat, string prefix, string asset)
    {
        mat.SetShaderParameter(prefix + "_col", SurfaceMap(asset, "Color"));
        mat.SetShaderParameter(prefix + "_nrm", SurfaceMap(asset, "NormalGL"));
        mat.SetShaderParameter(prefix + "_rgh", SurfaceMap(asset, "Roughness"));
    }

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _maps = new();

    private static Texture2D SurfaceMap(string asset, string map)
    {
        string path = $"res://Textures/{asset}_1K-JPG_{map}.jpg";
        if (!_maps.TryGetValue(path, out var tex)) _maps[path] = tex = GD.Load<Texture2D>(path) ?? throw new InvalidOperationException($"missing texture {path}");
        return tex;
    }

    /// <summary>The surface sets in use (one place, so notices and exports stay in sync).</summary>
    public static class Surfaces
    {
        public const string Soil = "Ground048", Damp = "Ground037", Moss = "Moss002", Leaves = "ScatteredLeaves007",
                            Rock = "Rock058", Gravel = "Gravel022", Bark = "Bark014";
    }

    public static StandardMaterial3D VertexColorMaterial(float roughness = 0.85f, bool doubleSided = false)
    {
        var m = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = roughness,
            AlbedoColor = Colors.White,
        };
        if (doubleSided) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return m;
    }

    public static string UserPath(string rel) => ProjectSettings.GlobalizePath("user://" + rel);
}

/// <summary>Non-simulation user preferences, persisted to user://settings.json.</summary>
public sealed class UserSettings
{
    public double CameraSpeed { get; set; } = 1.5;          // m/s
    public double MouseSensitivity { get; set; } = 0.25;    // degrees per pixel
    public bool InvertY { get; set; }
    public int Quality { get; set; } = 1;                   // 0 low, 1 medium, 2 high
    public double AutosaveMinutes { get; set; } = 5;
    public bool AutosaveEnabled { get; set; } = true;
    public double UiScale { get; set; } = 1.0;
    public bool ShowHelpOnStart { get; set; } = true;
    /// <summary>Settings format; v2 moved flying speed off the mouse wheel, v3 re-runs that repair (see Load).</summary>
    public int Version { get; set; } = CurrentVersion;
    public const int CurrentVersion = 3;
    /// <summary>Scripted test runs set this so they never overwrite the player's settings file.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Transient { get; set; }

    public const double MinCameraSpeed = 0.02, MaxCameraSpeed = 25;

    private static string PathOf => Bridge.UserPath("settings.json");

    public void Clamp()
    {
        CameraSpeed = Math.Clamp(double.IsFinite(CameraSpeed) ? CameraSpeed : 1.5, MinCameraSpeed, MaxCameraSpeed);
        MouseSensitivity = Math.Clamp(double.IsFinite(MouseSensitivity) ? MouseSensitivity : 0.25, 0.02, 2.0);
        Quality = Math.Clamp(Quality, 0, 2);
        AutosaveMinutes = Math.Clamp(double.IsFinite(AutosaveMinutes) ? AutosaveMinutes : 5, 1, 60);
        UiScale = Math.Clamp(double.IsFinite(UiScale) ? UiScale : 1, 0.75, 2.0);
    }

    public static UserSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(PathOf))
            {
                string json = System.IO.File.ReadAllText(PathOf);
                var s = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                // v1 files have no "Version" field (the property default must not stand in for it)
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    s.Version = doc.RootElement.TryGetProperty(nameof(Version), out var v) && v.TryGetInt32(out int ver) ? ver : 1;
                // v1 used the wheel for flying speed, so scrolling to "zoom" could leave it saved at a crawl
                // (and early test runs could save autosave=off into the player's file). The first build with this
                // repair misread v1 files as v2 and re-saved them unrepaired, so v3 applies it once more.
                if (s.Version < 3) { if (s.CameraSpeed < 0.5) s.CameraSpeed = 1.5; s.AutosaveEnabled = true; s.Version = CurrentVersion; }
                s.Clamp();
                return s;
            }
        }
        catch (Exception ex) { Sim.Core.Log.Warn(Sim.Core.LogCategory.UI, $"Settings unreadable, using defaults: {ex.Message}"); }
        return new UserSettings();
    }

    public void Save()
    {
        Clamp();
        if (Transient) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOf)!);
            System.IO.File.WriteAllText(PathOf, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Sim.Core.Log.Warn(Sim.Core.LogCategory.UI, $"Could not save settings: {ex.Message}"); }
    }
}
