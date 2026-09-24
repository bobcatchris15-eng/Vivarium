using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Fine 3D topsoil litter scattered on soil cells: crumbs, small clods, twigs and leaf-litter flakes. Purely a
/// render-time decoration (no simulation entities) so density and darkness can follow the live moisture
/// field directly. Distance-culled from the camera; rebuilt on the same cadence as the field texture.
/// </summary>
public partial class SoilDetailRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MultiMesh _crumbs = null!, _clods = null!, _twigs = null!, _flakes = null!;
    private MultiMeshInstance3D _crumbMmi = null!, _clodMmi = null!, _twigMmi = null!, _flakeMmi = null!;
    private double _accum = 999;
    private int _terrainVersion = int.MinValue;
    public Camera3D? Camera { get; set; }
    /// <summary>0 disables the layer entirely (lowest quality tier); 1 normal density; 2 dense/close-up.</summary>
    public int Quality { get; set; } = 1;
    // Fine detail reads only up close; keep the radius tight so nothing pops at a distance and so the
    // per-frame instance count (which scales with area) stays cheap.
    private const float CullRadius = 9f;
    private const float FadeStart = 6f;
    // Crumbs and clods are millimetre-scale: past a few metres they cover under a pixel, and sub-pixel triangles
    // are the most expensive thing an iGPU can draw (every one still pays a full 2x2 quad). Only litter and twigs,
    // which are big enough to read, go out to the full radius.
    private const float CrumbRadius = 2.5f, ClodRadius = 4.5f;
    private Vector3 _lastCam = new(float.MaxValue, 0, 0);

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            Metallic = 0f,
            AlbedoColor = Colors.White,
        };
        _crumbs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.0035f, 6) };
        _clods = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.007f, 8) };
        _twigs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildTwig(mat) };
        _flakes = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildFlake(mat) };
        _crumbMmi = new MultiMeshInstance3D { Name = "SoilCrumbs", Multimesh = _crumbs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _clodMmi = new MultiMeshInstance3D { Name = "SoilClods", Multimesh = _clods, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _twigMmi = new MultiMeshInstance3D { Name = "SoilTwigs", Multimesh = _twigs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _flakeMmi = new MultiMeshInstance3D { Name = "LeafLitter", Multimesh = _flakes, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        AddChild(_crumbMmi); AddChild(_clodMmi); AddChild(_twigMmi); AddChild(_flakeMmi);
        _accum = 999;
        _terrainVersion = int.MinValue;
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("SoilDetail");
        if (_w == null || Quality <= 0) return;
        _accum += delta;
        if (_accum < 0.5) return;
        // rebuild only when the view has moved enough to matter, the ground changed, or occasionally for moisture drift
        // (a blind rebuild every 0.75 s was a periodic multi-millisecond hitch)
        Vector3 cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool moved = cam.DistanceSquaredTo(_lastCam) > 0.8f * 0.8f;
        if (!moved && _terrainVersion == _w.Terrain.Version && _accum < 6.0) return;
        _accum = 0;
        _lastCam = cam;
        _terrainVersion = _w.Terrain.Version;
        Refresh();
    }

    private void Refresh()
    {
        var g = _w.Grid; var f = _w.Fields;
        Vector3 camPos = Camera?.GlobalPosition ?? Vector3.Zero;
        bool haveCam = Camera != null;
        float r2 = CullRadius * CullRadius;
        var crumbXf = new List<Transform3D>(); var crumbCol = new List<Color>();
        var clodXf = new List<Transform3D>(); var clodCol = new List<Color>();
        var twigXf = new List<Transform3D>(); var twigCol = new List<Color>();
        var flakeXf = new List<Transform3D>(); var flakeCol = new List<Color>();

        // step over a fine lattice near the camera: this layer only needs to exist within CullRadius, so the
        // scan cost stays bounded regardless of world size.
        int stride = Quality >= 2 ? 1 : 2;
        var rng = new CellRng();
        // scan only the cells inside the cull square around the camera, not the whole island
        int i0 = 0, i1 = g.Nx - 1, j0 = 0, j1 = g.Nz - 1;
        if (haveCam)
        {
            i0 = Mathf.Max(0, (int)((camPos.X - CullRadius - g.OriginX) / g.CellSize)); i1 = Mathf.Min(g.Nx - 1, (int)((camPos.X + CullRadius - g.OriginX) / g.CellSize));
            j0 = Mathf.Max(0, (int)((camPos.Z - CullRadius - g.OriginZ) / g.CellSize)); j1 = Mathf.Min(g.Nz - 1, (int)((camPos.Z + CullRadius - g.OriginZ) / g.CellSize));
        }
        for (int j = j0 - j0 % stride; j <= j1; j += stride)
        for (int i = i0 - i0 % stride; i <= i1; i += stride)
        {
            if (!g.InDomain(i, j)) continue;
            int idx = g.Index(i, j);
            if ((Substrate)f.BaseSubstrate[idx] != Substrate.Soil) continue;
            var p = g.CellCenter(idx);
            var wp = Bridge.V(p, _w.Terrain.Height(p));
            float distFade = 1f, d = 0f;
            if (haveCam)
            {
                float dx = wp.X - camPos.X, dz = wp.Z - camPos.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                d = Mathf.Sqrt(d2);
                distFade = d <= FadeStart ? 1f : 1f - (d - FadeStart) / (CullRadius - FadeStart);
                if (distFade <= 0.05f) continue;
            }
            double moisture = f.Moisture.Values[idx];
            rng.Seed = (ulong)(idx * 2654435761u + 17);
            // wetter soil clumps into fewer, bigger, darker clods; dry soil scatters more loose crumbs and litter
            int crumbCount = Mathf.RoundToInt((moisture > 0.55 ? 0 : rng.RandiRange(2, 5) * Quality) * Mathf.Clamp(1f - (d - CrumbRadius * 0.6f) / (CrumbRadius * 0.4f), 0f, 1f));
            int clodCount = Mathf.RoundToInt((moisture > 0.3 ? rng.RandiRange(1, 3) : (rng.Randf() < 0.4 ? 1 : 0)) * Mathf.Clamp(1f - (d - ClodRadius * 0.6f) / (ClodRadius * 0.4f), 0f, 1f));
            int twigCount = Mathf.RoundToInt(rng.RandiRange(0, 1) * Quality * distFade);
            int flakeCount = Mathf.RoundToInt(rng.RandiRange(0, 2) * Quality * distFade);
            float darken = (float)Mathf.Clamp(1.0 - moisture * 0.55, 0.45, 1.0);
            Color soilTint = new Color(0.30f, 0.22f, 0.16f) * darken;
            Color twigTint = new Color(0.33f, 0.26f, 0.19f) * darken;
            Color litterTint = new Color(0.55f, 0.42f, 0.18f) * darken;

            for (int k = 0; k < crumbCount; k++) Place(crumbXf, crumbCol, g, p, ref rng, soilTint, 0.7f, 1.3f);
            for (int k = 0; k < clodCount; k++) Place(clodXf, clodCol, g, p, ref rng, soilTint * 1.05f, 0.7f, 1.4f);
            for (int k = 0; k < twigCount; k++) Place(twigXf, twigCol, g, p, ref rng, twigTint, 0.6f, 1.3f);
            for (int k = 0; k < flakeCount; k++) Place(flakeXf, flakeCol, g, p, ref rng, litterTint, 0.7f, 1.3f);
        }

        SetInstances(_crumbs, crumbXf, crumbCol);
        SetInstances(_clods, clodXf, clodCol);
        SetInstances(_twigs, twigXf, twigCol);
        SetInstances(_flakes, flakeXf, flakeCol);
    }

    private void Place(List<Transform3D> xf, List<Color> col, GridSpec g, Vivarium.Sim.Core.Vec2 center, ref CellRng rng, Color tint, float minScale, float maxScale)
    {
        var jitter = new Vector2(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * (float)(g.CellSize * 0.45);
        var wp = center + new Vivarium.Sim.Core.Vec2(jitter.X, jitter.Y);
        float y = (float)_w.Terrain.Height(wp);
        float s = rng.RandfRange(minScale, maxScale);
        var basis = new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s, s));
        xf.Add(new Transform3D(basis, new Vector3((float)wp.X, y + 0.002f * s, (float)wp.Z)));
        float v = rng.RandfRange(0.85f, 1.15f);
        col.Add(new Color(tint.R * v, tint.G * v, tint.B * v));
    }

    /// <summary>Managed per-cell RNG. Godot's RandomNumberGenerator is an engine object, so each call crosses into
    /// native code; thousands per rebuild made every refresh a 100 ms hitch.</summary>
    private struct CellRng
    {
        private ulong _s;
        public ulong Seed { set => _s = value * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; }
        private uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 32); }
        public float Randf() => Next() * (1f / 4294967296f);
        public float RandfRange(float a, float b) => a + (b - a) * Randf();
        public int RandiRange(int a, int b) => a + (int)(Next() % (uint)(b - a + 1));
    }

    private readonly Dictionary<MultiMesh, float[]> _buffers = new();

    /// <summary>One packed upload (12 transform + 4 colour floats per instance) instead of two engine calls each.</summary>
    private void SetInstances(MultiMesh mm, List<Transform3D> xf, List<Color> col)
    {
        int n = xf.Count;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        mm.VisibleInstanceCount = n;
        if (n == 0) return;
        if (!_buffers.TryGetValue(mm, out var buf) || buf.Length != n * 16) _buffers[mm] = buf = new float[n * 16];
        for (int i = 0; i < n; i++)
        {
            var t = xf[i]; var c = col[i]; int o = i * 16;
            buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
            buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
            buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
            buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1f;
        }
        mm.Buffer = buf;
    }

    /// <summary>
    /// Small rounded, irregular lump for crumbs and clods: an icosphere-ish hull with per-vertex radius jitter
    /// and smooth (position-normalized) normals, so it reads as a soft clump of dirt rather than a faceted
    /// pyramid. <paramref name="radius"/> is the nominal size in world metres; <paramref name="seedJitter"/>
    /// perturbs the hull so every generated mesh looks slightly different without needing per-instance meshes.
    /// </summary>
    private static ArrayMesh BuildClod(float radius, int seedJitter)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(seedJitter * 104729 + 7) };

        // Base icosahedron (12 verts, 20 tris) — a much rounder starting hull than a bipyramid/octahedron.
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var baseVerts = new[]
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };
        int[] tris =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };

        // Per-vertex jitter so the hull reads as an irregular clump, not a perfect sphere; flattened slightly
        // on the vertical axis so it sits like a squashed crumb rather than a marble.
        var verts = new Vector3[baseVerts.Length];
        for (int i = 0; i < baseVerts.Length; i++)
        {
            var n = baseVerts[i].Normalized();
            float jitter = rng.RandfRange(0.75f, 1.15f);
            var p = n * radius * jitter;
            p.Y *= 0.75f;
            verts[i] = p;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        for (int i = 0; i < tris.Length; i += 3)
        {
            var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
            var flatN = (b - a).Cross(c - a).Normalized();
            // Blend flat and smooth (position-normalized) normals so facets are visible but softened —
            // reads as a rounded crumb rather than a faceted gem.
            void Vert(Vector3 p)
            {
                var smoothN = p.Normalized();
                var n = (flatN * 0.4f + smoothN * 0.6f).Normalized();
                st.SetNormal(n);
                st.SetColor(Colors.White);
                st.AddVertex(p);
            }
            Vert(a); Vert(b); Vert(c);
        }
        return st.Commit();
    }

    /// <summary>Thin elongated stick for twig/root litter.</summary>
    private static ArrayMesh BuildTwig(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float halfLen = 0.028f, w = 0.0035f;
        var a0 = new Vector3(-w, 0, -halfLen); var a1 = new Vector3(w, 0, -halfLen);
        var b0 = new Vector3(-w, 0, halfLen); var b1 = new Vector3(w, 0.003f, halfLen);
        void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            var n = (p1 - p0).Cross(p2 - p0).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(p0);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(p1);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(p2);
        }
        Tri(a0, a1, b1); Tri(a0, b1, b0);
        Tri(a0, b1, a1); Tri(a0, b0, b1); // both faces so it reads from any angle
        st.SetMaterial(mat);
        return st.Commit();
    }

    /// <summary>Thin curled quad standing in for a leaf-litter flake.</summary>
    private static ArrayMesh BuildFlake(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float w = 0.009f, l = 0.013f, curl = 0.003f;
        var p00 = new Vector3(-w, 0, -l); var p10 = new Vector3(w, curl, -l);
        var p01 = new Vector3(-w, curl, l); var p11 = new Vector3(w, 0, l);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(b);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(c);
        }
        Tri(p00, p10, p11); Tri(p00, p11, p01);
        Tri(p00, p11, p10); Tri(p00, p01, p11); // both faces so it reads from any angle
        st.SetMaterial(mat);
        return st.Commit();
    }
}
