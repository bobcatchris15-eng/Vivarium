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
    private static readonly List<WeakReference<ShaderMaterial>> FoliageMaterials = new();
    private static bool _simpleFoliageLighting;
    public static void SetSimpleFoliageLighting(bool enabled)
    {
        _simpleFoliageLighting = enabled;
        for (int i = FoliageMaterials.Count - 1; i >= 0; i--)
        {
            if (!FoliageMaterials[i].TryGetTarget(out var material) || !GodotObject.IsInstanceValid(material))
                FoliageMaterials.RemoveAt(i);
            else material.SetShaderParameter("simple_foliage_lighting", enabled);
        }
    }
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

    public sealed record PackedMesh(Vector3[] Vertices, Vector3[] Normals, Color[] Colors, Vector2[] UV, Vector2[] UV2);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeshData, PackedMesh> PreparedMeshes = new();
    public static PackedMesh PackMesh(MeshData m)
    {
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
        return new(verts,norms,cols,uv,uv2);
    }
    public static IEnumerable<bool> PackMeshBatched(MeshData m, Action<PackedMesh> complete)
    {
        int n = m.VertexCount;
        var verts = new Vector3[n]; var norms = new Vector3[n]; var cols = new Color[n]; var uv = new Vector2[n]; var uv2 = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            if (i % 2048 == 0) yield return true;
            verts[i] = new Vector3(m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]);
            norms[i] = new Vector3(m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]);
            cols[i] = new Color(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2], m.Colors[i * 4 + 3]);
            uv[i] = new Vector2(m.UV[i * 2], m.UV[i * 2 + 1]);
            uv2[i] = new Vector2(m.UV2[i * 2], m.UV2[i * 2 + 1]);
        }
        complete(new(verts,norms,cols,uv,uv2));
    }
    /// <summary>Copies immutable completed geometry into upload arrays; no Godot API or objects, safe on workers.</summary>
    public static void PrepareMeshUpload(MeshData m) => PreparedMeshes.GetValue(m, PackMesh);

    public static ArrayMesh ToArrayMesh(MeshData m, Material? material = null, ArrayMesh? reuse = null, bool signedVertexData = false, bool generateLods = false)
    {
        var mesh = reuse ?? new ArrayMesh();
        mesh.ClearSurfaces();
        if (m.VertexCount == 0 || m.Indices.Count == 0) return mesh;
        int n = m.VertexCount;
        var packed = PreparedMeshes.TryGetValue(m,out var prepared) ? prepared : PackMesh(m);
        var verts=packed.Vertices; var norms=packed.Normals; var cols=packed.Colors; var uv=packed.UV; var uv2=packed.UV2;
        using var arrays = new Godot.Collections.Array();   // release the native array now, not at GC finalization
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = norms;
        // Vertex COLOR is RGBA8 UNORM in Godot, so it loses negative appendage offsets.
        // Fauna carries those signed offsets and the appendage flag in float CUSTOM0 instead.
        if (signedVertexData) arrays[(int)Mesh.ArrayType.Custom0] = m.Colors.ToArray();
        else arrays[(int)Mesh.ArrayType.Color] = cols;
        if (!signedVertexData && m.Custom0.Count == n * 4) arrays[(int)Mesh.ArrayType.Custom0] = m.Custom0.ToArray();
        // Signed fauna already occupies CUSTOM0 with attachment offsets. Its additional
        // anatomical ankle data uses CUSTOM2 while CUSTOM1 carries the knee.
        if (signedVertexData && m.Custom0.Count == n * 4) arrays[(int)Mesh.ArrayType.Custom2] = m.Custom0.ToArray();
        if (m.Custom1.Count == n * 4) arrays[(int)Mesh.ArrayType.Custom1] = m.Custom1.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = m.Indices.ToArray();
        if (norms.Length == n && uv.Length == n && n > 0)
        {
            arrays[(int)Mesh.ArrayType.Tangent] = GenerateTangents(verts, norms, uv, m.Indices, n);
        }
        var flags = signedVertexData
            ? (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            : (Mesh.ArrayFormat)0;
        if (!signedVertexData && m.Custom0.Count == n * 4)
            flags |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        if (signedVertexData && m.Custom0.Count == n * 4)
            flags |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);
        if (m.Custom1.Count == n * 4)
            flags |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        if (generateLods && m.TriangleCount > 256)
        {
            using var importer = new ImporterMesh();
            importer.AddSurface(Mesh.PrimitiveType.Triangles, arrays, flags: (ulong)flags);
            using var boneTransforms = new Godot.Collections.Array();
            importer.GenerateLods(60f, 0f, boneTransforms);
            mesh = importer.GetMesh(mesh);
        }
        else mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);
        if (material != null) mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    public static float[] GenerateTangents(Vector3[] verts, Vector3[] norms, Vector2[] uv, IReadOnlyList<int> indices, int n)
    {
        float[] tangents = new float[n * 4];
        if (norms.Length != n || uv.Length != n || n == 0) return tangents;
        Vector3[] tan1 = new Vector3[n];
        Vector3[] tan2 = new Vector3[n];

        if (indices.Count >= 3)
        {
            for (int t = 0; t < indices.Count; t += 3)
            {
                int i1 = indices[t];
                int i2 = indices[t + 1];
                int i3 = indices[t + 2];
                if (i1 >= n || i2 >= n || i3 >= n) continue;

                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];
                Vector3 v3 = verts[i3];

                Vector2 w1 = uv[i1];
                Vector2 w2 = uv[i2];
                Vector2 w3 = uv[i3];

                float x1 = v2.X - v1.X;
                float x2 = v3.X - v1.X;
                float y1 = v2.Y - v1.Y;
                float y2 = v3.Y - v1.Y;
                float z1 = v2.Z - v1.Z;
                float z2 = v3.Z - v1.Z;

                float s1 = w2.X - w1.X;
                float s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y;
                float t2 = w3.Y - w1.Y;

                float r = (s1 * t2 - s2 * t1);
                if (MathF.Abs(r) > 1e-10f)
                {
                    r = 1.0f / r;
                    Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                    Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                    tan1[i1] += sdir;
                    tan1[i2] += sdir;
                    tan1[i3] += sdir;
                    tan2[i1] += tdir;
                    tan2[i2] += tdir;
                    tan2[i3] += tdir;
                }
            }
        }

        for (int a = 0; a < n; a++)
        {
            Vector3 norm = norms[a];
            Vector3 t = tan1[a];

            Vector3 tangent = t - norm * norm.Dot(t);
            if (tangent.LengthSquared() > 1e-8f)
            {
                tangent = tangent.Normalized();
            }
            else
            {
                tangent = norm.Cross(MathF.Abs(norm.Y) < 0.99f ? Vector3.Up : Vector3.Right);
                if (tangent.LengthSquared() > 1e-8f)
                    tangent = tangent.Normalized();
                else
                    tangent = Vector3.Right;
            }

            float sign = (norm.Cross(t).Dot(tan2[a]) < 0.0f) ? -1.0f : 1.0f;

            tangents[a * 4 + 0] = tangent.X;
            tangents[a * 4 + 1] = tangent.Y;
            tangents[a * 4 + 2] = tangent.Z;
            tangents[a * 4 + 3] = sign;
        }

        return tangents;
    }

    // Same accumulation order as the synchronous reference, with cooperative boundaries.
    public static IEnumerable<bool> GenerateTangentsBatched(Vector3[] verts, Vector3[] norms, Vector2[] uv, IReadOnlyList<int> indices, int n, Action<float[]> complete)
    {
        float[] tangents = new float[n * 4];
        if (norms.Length != n || uv.Length != n || n == 0) { complete(tangents); yield break; }
        Vector3[] tan1 = new Vector3[n];
        Vector3[] tan2 = new Vector3[n];

        if (indices.Count >= 3)
        {
            for (int t = 0; t < indices.Count; t += 3)
            {
                if (t % 4608 == 0) yield return true;
                int i1 = indices[t];
                int i2 = indices[t + 1];
                int i3 = indices[t + 2];
                if (i1 >= n || i2 >= n || i3 >= n) continue;

                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];
                Vector3 v3 = verts[i3];

                Vector2 w1 = uv[i1];
                Vector2 w2 = uv[i2];
                Vector2 w3 = uv[i3];

                float x1 = v2.X - v1.X;
                float x2 = v3.X - v1.X;
                float y1 = v2.Y - v1.Y;
                float y2 = v3.Y - v1.Y;
                float z1 = v2.Z - v1.Z;
                float z2 = v3.Z - v1.Z;

                float s1 = w2.X - w1.X;
                float s2 = w3.X - w1.X;
                float t1 = w2.Y - w1.Y;
                float t2 = w3.Y - w1.Y;

                float r = (s1 * t2 - s2 * t1);
                if (MathF.Abs(r) > 1e-10f)
                {
                    r = 1.0f / r;
                    Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                    Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                    tan1[i1] += sdir;
                    tan1[i2] += sdir;
                    tan1[i3] += sdir;
                    tan2[i1] += tdir;
                    tan2[i2] += tdir;
                    tan2[i3] += tdir;
                }
            }
        }

        for (int a = 0; a < n; a++)
        {
            if (a % 2048 == 0) yield return true;
            Vector3 norm = norms[a];
            Vector3 t = tan1[a];

            Vector3 tangent = t - norm * norm.Dot(t);
            if (tangent.LengthSquared() > 1e-8f)
            {
                tangent = tangent.Normalized();
            }
            else
            {
                tangent = norm.Cross(MathF.Abs(norm.Y) < 0.99f ? Vector3.Up : Vector3.Right);
                if (tangent.LengthSquared() > 1e-8f)
                    tangent = tangent.Normalized();
                else
                    tangent = Vector3.Right;
            }

            float sign = (norm.Cross(t).Dot(tan2[a]) < 0.0f) ? -1.0f : 1.0f;

            tangents[a * 4 + 0] = tangent.X;
            tangents[a * 4 + 1] = tangent.Y;
            tangents[a * 4 + 2] = tangent.Z;
            tangents[a * 4 + 3] = sign;
        }

        complete(tangents);
    }

    public static Godot.Collections.Array CreateNativeArrays(PackedMesh packed, float[] tangents, IReadOnlyList<int> indices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = packed.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = packed.Normals;
        arrays[(int)Mesh.ArrayType.Color] = packed.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = packed.UV;
        arrays[(int)Mesh.ArrayType.TexUV2] = packed.UV2;
        arrays[(int)Mesh.ArrayType.Index] = indices is int[] arr ? arr : System.Linq.Enumerable.ToArray(indices);
        if (tangents != null && tangents.Length > 0)
            arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        return arrays;
    }

    public static ShaderMaterial Shader(string path)
    {
        var sh = GD.Load<Shader>(path) ?? throw new InvalidOperationException($"missing shader {path}");
        var material = new ShaderMaterial { Shader = sh };
        if (path.EndsWith("/leaf.gdshader") || path.EndsWith("/flora.gdshader") || path.EndsWith("/ground_cover.gdshader"))
        {
            FoliageMaterials.Add(new(material));
            material.SetShaderParameter("simple_foliage_lighting", _simpleFoliageLighting);
        }
        // Experiment control: same geometry and shader code; turn off the
        // post-2026-10-18 scattering/anisotropy candidate to measure its cost.
        // Never changes ecological simulation, save files or permanent settings.
        if (System.Environment.GetEnvironmentVariable("VIVARIUM_FOLIAGE_PERF_BASELINE") == "1")
        {
            if (path.EndsWith("/leaf.gdshader") || path.EndsWith("/flora.gdshader") ||
                path.EndsWith("/ground_cover.gdshader"))
            {
                material.SetShaderParameter("tissue_phase_scatter", 0f);
                material.SetShaderParameter("tissue_fiber_anisotropy", 0f);
                if (path.EndsWith("/leaf.gdshader"))
                    material.SetShaderParameter("canopy_occlusion", .15f);
            }
        }
        // Repeatable baseline and 4x optical stress tests without altering ecology.
        if (path.EndsWith("/flora.gdshader") &&
            float.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_MOSS_AGGREGATE"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mossGain))
            material.SetShaderParameter("u_moss_aggregate", Math.Clamp(mossGain, 0f, 4f));
        if (path.EndsWith("/flora.gdshader") || path.EndsWith("/pilot_tree.gdshader"))
        {
            if (float.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_BARK_FISSURES"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var barkGain))
                material.SetShaderParameter("u_bark_fissures", Math.Clamp(barkGain, 0f, 4f));
            if (path.EndsWith("/flora.gdshader") &&
                float.TryParse(System.Environment.GetEnvironmentVariable("VIVARIUM_LIVING_STEM"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var stemGain))
                material.SetShaderParameter("u_living_stem", Math.Clamp(stemGain, 0f, 4f));
        }
        return material;
    }

    public readonly record struct SurfaceSet(string Color, string Normal, string Roughness, string Height = "", string Ao = "");

    /// <summary>
    /// Binds a 5-channel PBR surface set (Color, NormalGL, Roughness, Displacement/Height, AO)
    /// to shader uniforms &lt;prefix&gt;_col, &lt;prefix&gt;_nrm, &lt;prefix&gt;_rgh, &lt;prefix&gt;_hgt, and &lt;prefix&gt;_ao.
    /// </summary>
    public static void BindSurface(ShaderMaterial mat, string prefix, SurfaceSet s)
    {
        mat.SetShaderParameter(prefix + "_col", LoadTex(s.Color));
        mat.SetShaderParameter(prefix + "_nrm", LoadTex(s.Normal));
        mat.SetShaderParameter(prefix + "_rgh", LoadTex(s.Roughness));
        if (!string.IsNullOrEmpty(s.Height)) mat.SetShaderParameter(prefix + "_hgt", LoadTex(s.Height));
        if (!string.IsNullOrEmpty(s.Ao)) mat.SetShaderParameter(prefix + "_ao", LoadTex(s.Ao));
    }

    /// <summary>
    /// Binds a photo-scanned surface set (resolving 4K PNG or 1K JPG/PNG for 5 PBR channels)
    /// to shader uniforms &lt;prefix&gt;_col, &lt;prefix&gt;_nrm, &lt;prefix&gt;_rgh, &lt;prefix&gt;_hgt, and &lt;prefix&gt;_ao.
    /// </summary>
    public static void BindSurface(ShaderMaterial mat, string prefix, string asset)
    {
        BindSurface(mat, prefix, ResolveSurfaceSet(asset));
    }

    public static SurfaceSet ResolveSurfaceSet(string asset)
    {
        string color = FindTexPath(asset, "Color", isRequired: true);
        string normal = FindTexPath(asset, "NormalGL", isRequired: true);
        string roughness = FindTexPath(asset, "Roughness", isRequired: true);
        string height = FindTexPath(asset, "Displacement", isRequired: false);
        string ao = FindTexPath(asset, "AmbientOcclusion", isRequired: false);

        return new SurfaceSet(color, normal, roughness, height, ao);
    }

    private static string NormalizeAsset(string asset) => asset switch
    {
        var a when a.Equals("Silt", StringComparison.OrdinalIgnoreCase) => Surfaces.Silt,
        var a when a.Equals("RottenWood", StringComparison.OrdinalIgnoreCase) || a.Equals("Rotten_Wood", StringComparison.OrdinalIgnoreCase) || a.Equals("Rotten", StringComparison.OrdinalIgnoreCase) => Surfaces.RottenWood,
        var a when a.Equals("Soil", StringComparison.OrdinalIgnoreCase) => Surfaces.Soil,
        var a when a.Equals("Damp", StringComparison.OrdinalIgnoreCase) => Surfaces.Damp,
        var a when a.Equals("Moss", StringComparison.OrdinalIgnoreCase) => Surfaces.Moss,
        var a when a.Equals("Leaves", StringComparison.OrdinalIgnoreCase) => Surfaces.Leaves,
        var a when a.Equals("Rock", StringComparison.OrdinalIgnoreCase) => Surfaces.Rock,
        var a when a.Equals("Gravel", StringComparison.OrdinalIgnoreCase) => Surfaces.Gravel,
        var a when a.Equals("Bark", StringComparison.OrdinalIgnoreCase) => Surfaces.Bark,
        var a when a.Equals("Bark001", StringComparison.OrdinalIgnoreCase) => Surfaces.Bark001,
        var a when a.Equals("Bark013", StringComparison.OrdinalIgnoreCase) => Surfaces.Bark013,
        var a when a.Equals("Bark014", StringComparison.OrdinalIgnoreCase) => Surfaces.Bark014,
        _ => asset
    };

    private static string FindTexPath(string asset, string map, bool isRequired)
    {
        asset = NormalizeAsset(asset);
        // 1. Try 4K PNG
        string p4k = $"res://Textures/{asset}_4K-PNG_{map}.png";
        if (FileAccess.FileExists(p4k) || ResourceLoader.Exists(p4k)) return p4k;

        // 2. Try 1K PNG
        string p1kPng = $"res://Textures/{asset}_1K-JPG_{map}.png";
        if (FileAccess.FileExists(p1kPng) || ResourceLoader.Exists(p1kPng)) return p1kPng;

        // 3. Try 1K JPG
        string p1kJpg = $"res://Textures/{asset}_1K-JPG_{map}.jpg";
        if (FileAccess.FileExists(p1kJpg) || ResourceLoader.Exists(p1kJpg)) return p1kJpg;

        // 4. Try generic {asset}_{map}.png
        string pGenPng = $"res://Textures/{asset}_{map}.png";
        if (FileAccess.FileExists(pGenPng) || ResourceLoader.Exists(pGenPng)) return pGenPng;

        // 5. Try generic {asset}_{map}.jpg
        string pGenJpg = $"res://Textures/{asset}_{map}.jpg";
        if (FileAccess.FileExists(pGenJpg) || ResourceLoader.Exists(pGenJpg)) return pGenJpg;

        if (isRequired)
        {
            throw new InvalidOperationException($"missing required texture for asset '{asset}', map '{map}'");
        }
        return string.Empty;
    }

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _maps = new();

    public static Texture2D LoadTex(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("texture path cannot be empty", nameof(path));
        if (!_maps.TryGetValue(path, out var tex))
        {
            tex = GD.Load<Texture2D>(path);
            if (tex == null) throw new InvalidOperationException($"missing texture {path}");
            _maps[path] = tex;
        }
        return tex;
    }


    /// <summary>The surface sets in use (one place, so notices and exports stay in sync).</summary>
    public static class Surfaces
    {
        public const string Soil = "Ground048", Damp = "Ground037", Moss = "Moss002", Leaves = "ScatteredLeaves007",
                            Rock = "Rock058", Gravel = "Gravel022", Bark = "Bark014",
                            Bark001 = "Bark001", Bark013 = "Bark013", Bark014 = "Bark014",
                            Silt = "Ground037", RottenWood = "Bark013";
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

    public static string UserPath(string rel)
    {
        // Scripted landscape captures must not touch player preferences, logs or saved worlds.
        string? isolated = System.Array.IndexOf(Main.UserArgs, "--perf-test") >= 0 || System.Environment.GetEnvironmentVariable("VIVARIUM_LANDSCAPE_SCENARIO") != null
            ? System.Environment.GetEnvironmentVariable("VIVARIUM_BENCHMARK_USER_DIR") : null;
        return isolated != null ? System.IO.Path.Combine(isolated, rel) : ProjectSettings.GlobalizePath("user://" + rel);
    }
}

/// <summary>Non-simulation user preferences, persisted to user://settings.json.</summary>
public sealed class UserSettings
{
    public double CameraSpeed { get; set; } = 1.5;          // m/s
    public double MouseSensitivity { get; set; } = 0.25;    // degrees per pixel
    public bool InvertY { get; set; }
    public int Quality { get; set; } = 1;                   // 0 low, 1 medium, 2 high
    public bool DynamicShadows { get; set; } = true;
    public bool GlobalIllumination { get; set; } = true;
    public bool FineIndirectLighting { get; set; }
    public bool AmbientOcclusion { get; set; } = true;
    public bool VolumetricMist { get; set; } = true;
    public bool ReducedDistantPlants { get; set; } = true;
    public bool DistanceBlur { get; set; }
    public bool SimpleFoliageLighting { get; set; }
    public double RenderScale { get; set; } = .95;
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
        RenderScale = Math.Clamp(double.IsFinite(RenderScale) ? RenderScale : 1, 0.5, 1);
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
