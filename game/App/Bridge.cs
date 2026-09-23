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
        var arrays = new Godot.Collections.Array();
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
    /// <summary>Settings format; v2 moved flying speed off the mouse wheel.</summary>
    public int Version { get; set; } = 2;
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
                var s = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(System.IO.File.ReadAllText(PathOf)) ?? new UserSettings();
                // v1 used the wheel for flying speed, so scrolling to "zoom" could leave it saved at a crawl
                // (and early test runs could save autosave=off into the player's file)
                if (s.Version < 2) { if (s.CameraSpeed < 0.5) s.CameraSpeed = 1.5; s.AutosaveEnabled = true; s.Version = 2; }
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
