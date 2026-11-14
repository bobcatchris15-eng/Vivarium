using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Light;

namespace Vivarium.Repr;

/// <summary>
/// Owns the cached <see cref="OcclusionVolume"/> of a demo and binds it as global shader uniforms
/// (repr_skyvis_*). Uploads only after a bake, never per frame. Strength 0 disables every shader hook.
/// </summary>
public sealed partial class SkyVisibilityField : Node
{
    public OcclusionVolume Volume { get; private set; } = null!;
    public float Strength { get; private set; }
    public long LastUploadBytes { get; private set; }
    public double LastUploadMs { get; private set; }
    public long TotalUploadBytes { get; private set; }
    public int Uploads { get; private set; }
    private ImageTexture3D? _tex;
    private int _nx, _ny, _nz;

    // ---- L1b shaping: per-material uniforms (repr_skyvis_shape / repr_skyvis_bounce), see repr_skyvis.gdshaderinc
    private readonly List<ShaderMaterial> _foliage = new(), _ground = new();
    private readonly HashSet<ulong> _seen = new();
    private Vector3 _bounce = Vector3.Zero;
    public int FoliageMaterialCount => _foliage.Count;

    /// <summary>Register every ShaderMaterial (MaterialOverride, surface override, mesh surface) under <paramref name="root"/>.
    /// Uniforms a shader does not declare are ignored, so any material may be registered.</summary>
    public void CollectMaterials(Node3D root, bool ground)
    {
        var stack = new Stack<Node>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is GeometryInstance3D g)
            {
                Add(g.MaterialOverride, ground);
                if (g is MeshInstance3D mi && mi.Mesh != null)
                    for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++) Add(mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i), ground);
                else if (g is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is Mesh m)
                    for (int i = 0; i < m.GetSurfaceCount(); i++) Add(m.SurfaceGetMaterial(i), ground);
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
    }

    public void RegisterGround(ShaderMaterial m) => Add(m, true);

    private void Add(Material? m, bool ground)
    {
        if (m is not ShaderMaterial sm || !_seen.Add(sm.GetInstanceId())) return;
        (ground ? _ground : _foliage).Add(sm);
    }

    /// <summary>shape = (extra exponent, visibility floor, thin tissue sees both sides 0/1, unused).</summary>
    public void SetShapes(Vector4 foliage, Vector4 ground)
    {
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_shape", foliage);
        foreach (var m in _foliage) m.SetShaderParameter("repr_skyvis_shape", foliage);
        foreach (var m in _ground) m.SetShaderParameter("repr_skyvis_shape", ground);
    }

    /// <summary>Bounce light (linear colour x energy) for foliage materials; skipped when it did not move by more than 1 %.</summary>
    public void PushBounce(Vector3 c)
    {
        if ((c - _bounce).Length() <= 0.01f * Math.Max(0.02f, _bounce.Length())) return;
        _bounce = c;
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_bounce", c);
        foreach (var m in _foliage) m.SetShaderParameter("repr_skyvis_bounce", c);
    }

    public static SkyVisibilityField Create(Vivarium.Sim.Repr.Aabb bounds, double voxel)
        => new() { Name = "SkyVisibilityField", Volume = new OcclusionVolume(bounds, voxel) };

    /// <summary>GPU-only binding for volumes owned by a CPU worker.</summary>
    public static SkyVisibilityField CreateUploader() => new() { Name = "SkyVisibilityField" };

    public void SetStrength(float s)
    {
        Strength = s;
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_strength", s);
        if (s <= 0f)
        {
            _bounce = Vector3.Zero;
            RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_shape", Vector4.Zero);
            RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_bounce", Vector3.Zero);
        }
    }

    /// <summary>Pack the whole volume and (re)upload it. The Godot 3D texture API replaces all layers.</summary>
    public void Upload()
    {
        var bytes = Volume.PackRgba8(out int nx, out int ny, out int nz);
        UploadPacked(bytes, nx, ny, nz, Volume.Bounds, Volume.VoxelSize);
    }

    /// <summary>Upload a completed CPU-worker snapshot on the Godot thread.</summary>
    public void UploadPacked(byte[] bytes, int nx, int ny, int nz, Vivarium.Sim.Repr.Aabb bounds, double voxel)
    {
        var sw = Stopwatch.StartNew();
        if (bytes.LongLength != (long)nx * ny * nz * 4) throw new ArgumentException("Invalid sky visibility volume size", nameof(bytes));
        var layers = new Godot.Collections.Array<Image>();
        int layerBytes = nx * nz * 4;
        for (int y = 0; y < ny; y++)
        {
            var layer = new byte[layerBytes];
            Buffer.BlockCopy(bytes, y * layerBytes, layer, 0, layerBytes);
            layers.Add(Image.CreateFromData(nx, nz, false, Image.Format.Rgba8, layer));
        }
        if (_tex == null || nx != _nx || ny != _ny || nz != _nz)
        {
            var tex = new ImageTexture3D();
            var error = tex.Create(Image.Format.Rgba8, nx, nz, ny, false, layers);
            if (error != Error.Ok) { tex.Dispose(); throw new InvalidOperationException($"Sky visibility texture creation failed: {error}"); }
            _tex = tex;
            _nx = nx; _ny = ny; _nz = nz;
            RenderingServer.GlobalShaderParameterSet("repr_skyvis_tex", _tex);
        }
        else _tex.Update(layers);
        var b = bounds;
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_origin", new Vector3((float)b.Min.X, (float)b.Min.Y, (float)b.Min.Z));
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_inv_size",
            new Vector3((float)(1.0 / (b.Max.X - b.Min.X)), (float)(1.0 / (b.Max.Y - b.Min.Y)), (float)(1.0 / (b.Max.Z - b.Min.Z))));
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_voxel", (float)voxel);
        LastUploadBytes = bytes.LongLength; TotalUploadBytes += LastUploadBytes; Uploads++;
        LastUploadMs = sw.Elapsed.TotalMilliseconds;
    }

    public override void _ExitTree()
    {
        // Leave the globals disabled so nothing built later sees a stale volume.
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_strength", 0f);
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_shape", Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("repr_skyvis_global_bounce", Vector3.Zero);
    }

    // ------------------------------------------------------------------ geometry feeding

    /// <summary>Triangles of every MeshInstance3D / MultiMeshInstance3D instance under node, in world space
    /// (node transforms relative to the scene root are applied). Leaf vs wood is told apart by the UV2 convention
    /// of flora.gdshader (UV2.y &gt; 0.5 leaf lamina, UV2.x &gt; 1.5 wood); with no UV2 everything is leaf.</summary>
    public int AddNodeTriangles(ulong sourceId, Node3D node, double leafOpacity, double woodOpacity)
    {
        int tris = 0;
        foreach (var (mesh, xf) in CollectMeshes(node))
        {
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arr = mesh.SurfaceGetArrays(s);
                var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
                var idxVar = arr[(int)Mesh.ArrayType.Index];
                int[] idx = idxVar.VariantType == Variant.Type.Nil ? SequentialIndices(verts.Length) : (int[])idxVar;
                var uv2Var = arr[(int)Mesh.ArrayType.TexUV2];
                Vector2[]? uv2 = uv2Var.VariantType == Variant.Type.Nil ? null : (Vector2[])uv2Var;
                var pos = new float[verts.Length * 3];
                for (int i = 0; i < verts.Length; i++) { pos[i * 3] = verts[i].X; pos[i * 3 + 1] = verts[i].Y; pos[i * 3 + 2] = verts[i].Z; }
                var leaf = new List<int>(idx.Length); var wood = new List<int>();
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    bool isWood = uv2 != null && uv2[idx[t]].X > 1.5f;
                    var dst = isWood ? wood : leaf;
                    dst.Add(idx[t]); dst.Add(idx[t + 1]); dst.Add(idx[t + 2]);
                }
                double[] m = { xf.Basis.Row0.X, xf.Basis.Row0.Y, xf.Basis.Row0.Z, xf.Origin.X,
                               xf.Basis.Row1.X, xf.Basis.Row1.Y, xf.Basis.Row1.Z, xf.Origin.Y,
                               xf.Basis.Row2.X, xf.Basis.Row2.Y, xf.Basis.Row2.Z, xf.Origin.Z };
                if (leaf.Count > 0) Volume.AddTriangles(sourceId, pos, leaf.ToArray(), m, leafOpacity);
                if (wood.Count > 0) Volume.AddTriangles(sourceId, pos, wood.ToArray(), m, woodOpacity);
                tris += idx.Length / 3;
            }
        }
        return tris;
    }

    private static int[] SequentialIndices(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = i; return a; }

    private static IEnumerable<(Mesh, Transform3D)> CollectMeshes(Node3D root)
    {
        var stack = new Stack<Node3D>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            var world = WorldTransform(n);
            if (n is MultiMeshInstance3D mmi && mmi.Multimesh?.Mesh is Mesh mm)
                for (int i = 0; i < mmi.Multimesh.InstanceCount; i++) yield return (mm, world * mmi.Multimesh.GetInstanceTransform(i));
            else if (n is MeshInstance3D mi && mi.Mesh != null) yield return (mi.Mesh, world);
            foreach (var c in n.GetChildren()) if (c is Node3D c3) stack.Push(c3);
        }
    }

    /// <summary>Transform relative to the scene root, ignoring visibility (works before the node is in the tree).</summary>
    public static Transform3D WorldTransform(Node3D n)
    {
        var t = n.Transform;
        for (var p = n.GetParent() as Node3D; p != null; p = p.GetParent() as Node3D) t = p.Transform * t;
        return t;
    }

    // ------------------------------------------------------------------ demo ground material

    private const string LitCode = @"shader_type spatial;
render_mode diffuse_burley;
#include ""res://Shaders/repr_skyvis.gdshaderinc""
uniform vec3 albedo_color : source_color = vec3(0.5);
uniform float rough = 1.0;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    ALBEDO = albedo_color;
    ROUGHNESS = rough;
    if (repr_skyvis_strength > 0.0) {
        vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
        AO = repr_sky_ao(wpos, wn, false);
        AO_LIGHT_AFFECT = REPR_SKYVIS_LIGHT_AFFECT;
        EMISSION = repr_sky_bounce(ALBEDO, wn, AO, false);
    }
}";

    private static Shader? _litShader;

    /// <summary>Matte material equal to a StandardMaterial3D(albedo, roughness) plus the sky-visibility AO hook.</summary>
    public static ShaderMaterial LitMaterial(Color albedo, float roughness)
    {
        _litShader ??= new Shader { Code = LitCode };
        var m = new ShaderMaterial { Shader = _litShader };
        m.SetShaderParameter("albedo_color", albedo);
        m.SetShaderParameter("rough", roughness);
        return m;
    }
}
