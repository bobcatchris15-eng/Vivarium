using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Fine 3D topsoil litter scattered on soil cells: crumbs, small clods and leaf-litter flakes. Purely a
/// render-time decoration (no simulation entities) so density and darkness can follow the live moisture
/// field directly. Distance-culled from the camera; rebuilt on the same cadence as the field texture.
/// </summary>
public partial class SoilDetailRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MultiMesh _crumbs = null!, _clods = null!, _flakes = null!;
    private MultiMeshInstance3D _crumbMmi = null!, _clodMmi = null!, _flakeMmi = null!;
    private double _accum = 999;
    private int _terrainVersion = int.MinValue;
    public Camera3D? Camera { get; set; }
    /// <summary>0 disables the layer entirely (lowest quality tier); 1 normal density; 2 dense/close-up.</summary>
    public int Quality { get; set; } = 1;
    private const float CullRadius = 22f;

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            AlbedoColor = Colors.White,
        };
        _crumbs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.028f, mat) };
        _clods = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.06f, mat) };
        _flakes = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildFlake(mat) };
        _crumbMmi = new MultiMeshInstance3D { Name = "SoilCrumbs", Multimesh = _crumbs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _clodMmi = new MultiMeshInstance3D { Name = "SoilClods", Multimesh = _clods, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _flakeMmi = new MultiMeshInstance3D { Name = "LeafLitter", Multimesh = _flakes, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_crumbMmi); AddChild(_clodMmi); AddChild(_flakeMmi);
        _accum = 999;
        _terrainVersion = int.MinValue;
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("SoilDetail");
        if (_w == null || Quality <= 0) return;
        _accum += delta;
        if (_accum < 0.75) return;
        if (_terrainVersion == _w.Terrain.Version && _accum < 1.5) return; // still refresh occasionally even if idle
        _accum = 0;
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
        var flakeXf = new List<Transform3D>(); var flakeCol = new List<Color>();

        // step over a coarser lattice than the field grid: litter reads at a glance, not per 25cm cell
        int stride = Quality >= 2 ? 1 : 2;
        var rng = new RandomNumberGenerator();
        foreach (int idx in g.DomainCells)
        {
            int i = idx % g.Nx, j = idx / g.Nx;
            if ((i % stride) != 0 || (j % stride) != 0) continue;
            if ((Substrate)f.BaseSubstrate[idx] != Substrate.Soil) continue;
            var p = g.CellCenter(idx);
            var wp = Bridge.V(p, _w.Terrain.Height(p));
            if (haveCam)
            {
                float dx = wp.X - camPos.X, dz = wp.Z - camPos.Z;
                if (dx * dx + dz * dz > r2) continue;
            }
            double moisture = f.Moisture.Values[idx];
            rng.Seed = (ulong)(idx * 2654435761u + 17);
            // wetter soil clumps into fewer, bigger, darker clods; dry soil scatters more loose crumbs and litter
            int crumbCount = moisture > 0.55 ? 0 : rng.RandiRange(1, 3) * Quality;
            int clodCount = moisture > 0.3 ? rng.RandiRange(1, 2) : (rng.Randf() < 0.4 ? 1 : 0);
            int flakeCount = rng.RandiRange(0, 2) * Quality;
            float darken = (float)Mathf.Clamp(1.0 - moisture * 0.55, 0.45, 1.0);
            Color soilTint = new Color(0.30f, 0.22f, 0.16f) * darken;
            Color litterTint = new Color(0.42f, 0.33f, 0.16f) * darken;

            for (int k = 0; k < crumbCount; k++) Place(crumbXf, crumbCol, g, p, rng, soilTint, 0.6f, 1.3f);
            for (int k = 0; k < clodCount; k++) Place(clodXf, clodCol, g, p, rng, soilTint * 1.05f, 0.8f, 1.5f);
            for (int k = 0; k < flakeCount; k++) Place(flakeXf, flakeCol, g, p, rng, litterTint, 0.7f, 1.4f);
        }

        SetInstances(_crumbs, crumbXf, crumbCol);
        SetInstances(_clods, clodXf, clodCol);
        SetInstances(_flakes, flakeXf, flakeCol);
    }

    private void Place(List<Transform3D> xf, List<Color> col, GridSpec g, Vivarium.Sim.Core.Vec2 center, RandomNumberGenerator rng, Color tint, float minScale, float maxScale)
    {
        var jitter = new Vector2(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)) * (float)(g.CellSize * 0.45);
        var wp = center + new Vivarium.Sim.Core.Vec2(jitter.X, jitter.Y);
        float y = (float)_w.Terrain.Height(wp);
        float s = rng.RandfRange(minScale, maxScale);
        var basis = new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s, s));
        xf.Add(new Transform3D(basis, new Vector3((float)wp.X, y + 0.01f * s, (float)wp.Z)));
        float v = rng.RandfRange(0.85f, 1.15f);
        col.Add(new Color(tint.R * v, tint.G * v, tint.B * v));
    }

    private static void SetInstances(MultiMesh mm, List<Transform3D> xf, List<Color> col)
    {
        mm.InstanceCount = xf.Count;
        mm.VisibleInstanceCount = xf.Count;
        for (int i = 0; i < xf.Count; i++)
        {
            mm.SetInstanceTransform(i, xf[i]);
            mm.SetInstanceColor(i, col[i]);
        }
    }

    /// <summary>Small irregular lump (octahedron-ish, cheap) for crumbs and clods.</summary>
    private static ArrayMesh BuildClod(float radius, Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var pts = new[]
        {
            new Vector3(0, radius * 1.1f, 0),
            new Vector3(radius, -radius * 0.3f, 0),
            new Vector3(0, -radius * 0.3f, radius),
            new Vector3(-radius, -radius * 0.3f, 0),
            new Vector3(0, -radius * 0.3f, -radius),
            new Vector3(0, -radius * 0.9f, 0),
        };
        void Tri(int a, int b, int c)
        {
            var n = (pts[b] - pts[a]).Cross(pts[c] - pts[a]).Normalized();
            st.SetNormal(n); st.AddVertex(pts[a]);
            st.SetNormal(n); st.AddVertex(pts[b]);
            st.SetNormal(n); st.AddVertex(pts[c]);
        }
        Tri(0, 1, 2); Tri(0, 2, 3); Tri(0, 3, 4); Tri(0, 4, 1);
        Tri(5, 2, 1); Tri(5, 3, 2); Tri(5, 4, 3); Tri(5, 1, 4);
        st.SetMaterial(mat);
        return st.Commit();
    }

    /// <summary>Thin curled quad standing in for a twig chip or leaf-litter flake.</summary>
    private static ArrayMesh BuildFlake(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float w = 0.045f, l = 0.07f, curl = 0.015f;
        var p00 = new Vector3(-w, 0, -l); var p10 = new Vector3(w, curl, -l);
        var p01 = new Vector3(-w, curl, l); var p11 = new Vector3(w, 0, l);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.AddVertex(a);
            st.SetNormal(n); st.AddVertex(b);
            st.SetNormal(n); st.AddVertex(c);
        }
        Tri(p00, p10, p11); Tri(p00, p11, p01);
        Tri(p00, p11, p10); Tri(p00, p01, p11); // both faces so it reads from any angle
        st.SetMaterial(mat);
        return st.Commit();
    }
}
