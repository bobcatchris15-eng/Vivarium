using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Render-side LOD distances per quality tier (never affects the simulation).</summary>
public static class Lod
{
    public static float Near(int q) => q switch { 0 => 2.5f, 1 => 4.5f, _ => 7f };
    public static float Far(int q) => q switch { 0 => 12f, 1 => 22f, _ => 40f };
}

/// <summary>
/// Flora drawn as one MultiMesh per species and detail level, rebuilt from authoritative state on a short
/// cadence. The render instances can be discarded and rebuilt at any time without touching the simulation.
/// </summary>
public partial class FloraRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private sealed class Layer { public MultiMeshInstance3D Near = null!, Far = null!; public int NearTris, FarTris; }
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, double> _wobbleStart = new();
    private double _accum = 999;
    private double _clock;
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public int Visible_ { get; private set; }
    public long TrianglesDrawn { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear();
        foreach (var sp in w.Content.Flora)
        {
            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            var hi = OrganismMeshes.Flora(sp);
            var lo = LowDetail(sp);
            var layer = new Layer
            {
                Near = MakeMmi($"Flora_{sp.Id}_near", Bridge.ToArrayMesh(hi, mat)),
                Far = MakeMmi($"Flora_{sp.Id}_far", Bridge.ToArrayMesh(lo, mat)),
                NearTris = hi.TriangleCount, FarTris = lo.TriangleCount,
            };
            AddChild(layer.Near); AddChild(layer.Far);
            _layers[sp.Id] = layer;
        }
        _accum = 999;
    }

    private static MeshData LowDetail(FloraSpeciesDef sp)
    {
        var m = new MeshData();
        var col = Primitives.Mix(sp.Color, sp.Color2, 0.4);
        Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(0.9, sp.Shape is "reed" or "herb" ? 0.6 : 0.9, 0.9), 3, 6, (a, b) => (col, 1, a, b, 0, 0));
        return m;
    }

    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh) => new()
    {
        Name = name,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    public void Wobble(IEnumerable<EntityId> ids) { foreach (var id in ids) _wobbleStart[id] = _clock; }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_w == null) return;
        _accum += delta;
        bool wobbling = _wobbleStart.Count > 0;
        if (_accum < (wobbling ? 0.05 : 0.4)) return;
        _accum = 0;
        Rebuild();
    }

    private readonly Dictionary<string, (List<Transform3D> T, List<Color> C)> _near = new(), _far = new();

    public void Rebuild()
    {
        foreach (var k in _layers.Keys) { Get(_near, k).T.Clear(); Get(_near, k).C.Clear(); Get(_far, k).T.Clear(); Get(_far, k).C.Clear(); }
        var camPos = Camera?.GlobalPosition ?? Vector3.Zero;
        float near = Lod.Near(Quality) * 1.6f;
        Visible_ = 0; TrianglesDrawn = 0;
        var done = new List<EntityId>();
        foreach (var f in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            double r = f.Radius(sp);
            double h = sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
            var pos = new Vector3((float)f.X, (float)_w.GroundHeight(f.Position), (float)f.Z);
            ulong hash = Rng.Mix(f.Id.Value, 0xF10);
            float yaw = (hash % 6283) / 1000f;
            var t = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(new Vector3((float)r, (float)h, (float)r)), pos);
            float wobble = 0;
            if (_wobbleStart.TryGetValue(f.Id, out var ws)) { wobble = (float)Math.Max(0, 1 - (_clock - ws) / 1.2); if (wobble <= 0) done.Add(f.Id); }
            var custom = new Color((hash % 1000) / 1000f, (float)f.Health, wobble, ((hash >> 12) % 1000) / 1000f);
            var bucket = pos.DistanceTo(camPos) < near ? _near : _far;
            Get(bucket, sp.Id).T.Add(t); Get(bucket, sp.Id).C.Add(custom);
            Visible_++;
        }
        foreach (var id in done) _wobbleStart.Remove(id);
        foreach (var (id, layer) in _layers)
        {
            Fill(layer.Near.Multimesh, Get(_near, id)); Fill(layer.Far.Multimesh, Get(_far, id));
            TrianglesDrawn += (long)layer.Near.Multimesh.InstanceCount * layer.NearTris + (long)layer.Far.Multimesh.InstanceCount * layer.FarTris;
        }
    }

    private static (List<Transform3D> T, List<Color> C) Get(Dictionary<string, (List<Transform3D>, List<Color>)> d, string k)
    {
        if (!d.TryGetValue(k, out var v)) d[k] = v = (new List<Transform3D>(), new List<Color>());
        return v;
    }

    private static void Fill(MultiMesh mm, (List<Transform3D> T, List<Color> C) data)
    {
        if (mm.InstanceCount != data.T.Count) mm.InstanceCount = data.T.Count;
        for (int i = 0; i < data.T.Count; i++) { mm.SetInstanceTransform(i, data.T[i]); mm.SetInstanceCustomData(i, data.C[i]); }
    }
}

/// <summary>
/// Fauna drawn from authoritative positions with render-only smoothing. Activity LOD is purely visual:
/// near animals get the full animated mesh, distant ones a static low-poly body, and off-screen or very far
/// ones are skipped. Simulation behaviour is identical for every individual regardless of the camera.
/// </summary>
public partial class FaunaRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private sealed class Layer { public MultiMeshInstance3D High = null!, Low = null!; public int HighTris, LowTris; public float[] HighBuf = System.Array.Empty<float>(), LowBuf = System.Array.Empty<float>(); }
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, (Vector3 Pos, float Yaw)> _display = new();
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public int LodHigh { get; private set; }
    public int LodLow { get; private set; }
    public int Culled { get; private set; }
    public long TrianglesDrawn { get; private set; }
    /// <summary>When true, every individual is drawn at high detail (used to measure LOD savings).</summary>
    public bool ForceHighDetail { get; set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear(); _display.Clear();
        foreach (var sp in w.Content.Fauna)
        {
            var hiMat = Bridge.Shader("res://Shaders/fauna.gdshader");
            hiMat.SetShaderParameter("base_color", Bridge.C(sp.BaseColor));
            hiMat.SetShaderParameter("ornament_color", Bridge.C(sp.OrnamentColor));
            hiMat.SetShaderParameter("wiggle", sp.Medium == Medium.Aquatic ? 1.0f : 0.35f);
            hiMat.SetShaderParameter("wiggle_speed", sp.Model == "minnow" ? 11.0f : 7.0f);
            hiMat.SetShaderParameter("translucency", sp.Model is "shrimp" or "minnow" ? 0.35f : 0.1f);
            var loMat = (ShaderMaterial)hiMat.Duplicate();
            loMat.SetShaderParameter("wiggle", 0.0f);
            var hi = OrganismMeshes.Fauna(sp);
            var lo = new MeshData();
            Primitives.Ellipsoid(lo, new Vec3(0, 0.15, 0), new Vec3(0.45, sp.Model == "triops" ? 0.1 : 0.14, sp.Model == "triops" ? 0.28 : 0.14), 5, 7, (a, b) => (new[] { 0.0, 0, 0 }, 0, 1 - a, b, 1, 0));
            var layer = new Layer
            {
                High = MakeMmi($"Fauna_{sp.Id}_high", Bridge.ToArrayMesh(hi, hiMat)),
                Low = MakeMmi($"Fauna_{sp.Id}_low", Bridge.ToArrayMesh(lo, loMat)),
                HighTris = hi.TriangleCount, LowTris = lo.TriangleCount,
            };
            AddChild(layer.High); AddChild(layer.Low);
            _layers[sp.Id] = layer;
        }
    }

    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh) => new()
    {
        Name = name,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    /// <summary>Smoothed display position of an animal (falls back to authoritative position).</summary>
    public Vector3 DisplayPosition(EntityId id)
    {
        if (_display.TryGetValue(id, out var d)) return d.Pos;
        var f = _w?.Fauna.Get(id);
        return f != null ? Bridge.V(f.Position) : Vector3.Zero;
    }

    public override void _Process(double delta)
    {
        if (_w == null) return;
        float k = 1 - Mathf.Exp(-(float)delta * 14f);
        var cam = Camera;
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        float near = Lod.Near(Quality), far = Lod.Far(Quality);
        LodHigh = LodLow = Culled = 0; TrianglesDrawn = 0;
        var counts = new Dictionary<string, (int Hi, int Lo)>(StringComparer.Ordinal);
        foreach (var id in _layers.Keys) counts[id] = (0, 0);
        // capacity = population (+headroom); buffers are kept exactly InstanceCount * 16 floats
        // (12 transform + 4 custom per instance) so they can be uploaded without per-frame allocation
        foreach (var (id, layer) in _layers)
        {
            int n = _w.Fauna.CountOf(id);
            layer.HighBuf = Ensure(layer.High.Multimesh, layer.HighBuf, n);
            layer.LowBuf = Ensure(layer.Low.Multimesh, layer.LowBuf, n);
        }
        var seen = new HashSet<EntityId>();
        foreach (var f in _w.Fauna.Items)
        {
            seen.Add(f.Id);
            var sp = _w.Content.FaunaOrThrow(f.SpeciesId);
            var ph = _w.FaunaSystem.PhenotypeOf(f);
            var target = Bridge.V(f.Position);
            float yawTarget = (float)-f.Heading;
            if (!_display.TryGetValue(f.Id, out var d) || d.Pos.DistanceTo(target) > 0.5f) d = (target, yawTarget);
            else d = (d.Pos.Lerp(target, k), d.Yaw + Mathf.Wrap(yawTarget - d.Yaw, -Mathf.Pi, Mathf.Pi) * k);
            _display[f.Id] = d;
            float dist = d.Pos.DistanceTo(camPos);
            bool onScreen = cam == null || cam.IsPositionInFrustum(d.Pos);
            if (!ForceHighDetail && (dist > far || !onScreen)) { Culled++; continue; }
            bool high = ForceHighDetail || dist < near || f.Grabbed;
            float scale = (float)(ph.BodySize * sp.VisualScale);
            var basis = new Basis(Vector3.Up, d.Yaw).Scaled(new Vector3(scale, scale, scale));
            var layer = _layers[sp.Id];
            var c = counts[sp.Id];
            int i = high ? c.Hi : c.Lo;
            var buf = high ? layer.HighBuf : layer.LowBuf;
            int o = i * 16;
            buf[o + 0] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = d.Pos.X;
            buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = d.Pos.Y;
            buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = d.Pos.Z;
            buf[o + 12] = (float)ph.HueShift; buf[o + 13] = (float)ph.OrnamentDensity; buf[o + 14] = (float)ph.PatternStrength; buf[o + 15] = (float)ph.AppendageScale;
            counts[sp.Id] = high ? (c.Hi + 1, c.Lo) : (c.Hi, c.Lo + 1);
            if (high) LodHigh++; else LodLow++;
        }
        foreach (var (id, layer) in _layers)
        {
            var (hi, lo) = counts[id];
            Upload(layer.High.Multimesh, layer.HighBuf, hi);
            Upload(layer.Low.Multimesh, layer.LowBuf, lo);
            TrianglesDrawn += (long)hi * layer.HighTris + (long)lo * layer.LowTris;
        }
        if (_display.Count > seen.Count + 64)
        {
            var stale = new List<EntityId>();
            foreach (var key in _display.Keys) if (!seen.Contains(key)) stale.Add(key);
            foreach (var key in stale) _display.Remove(key);
        }
    }

    private static float[] Ensure(MultiMesh mm, float[] buf, int needed)
    {
        if (mm.InstanceCount >= needed && buf.Length == mm.InstanceCount * 16) return buf;
        mm.InstanceCount = needed + needed / 2 + 8;
        return new float[mm.InstanceCount * 16];
    }

    private static void Upload(MultiMesh mm, float[] buf, int count)
    {
        mm.VisibleInstanceCount = count;
        if (count > 0) mm.Buffer = buf;
    }
}
