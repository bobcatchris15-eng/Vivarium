using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Cheap anonymous low vegetation generated from the authoritative ambient-cover field. Instances are disposable
/// render detail, not organisms. Habitat and neighboring cells choose colour and grass-vs-broadleaf silhouette.
/// </summary>
public partial class AmbientGroundCoverRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MultiMesh _blade = null!, _leaf = null!;
    private MultiMeshInstance3D _bladeMmi = null!, _leafMmi = null!;
    private readonly Dictionary<MultiMesh, float[]> _buffers = new();
    private double _accum = 999;
    private long _revision = -1;
    private Vector3 _lastCam = new(float.MaxValue, 0, 0);
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    private const float CullRadius = 11.5f;
    private const float FadeStart = 8.0f;

    public void Build(VivariumWorld w)
    {
        _w = w;
        _buffers.Clear();
        foreach (var c in GetChildren()) c.QueueFree();

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.92f,
            Metallic = 0,
            AlbedoColor = Colors.White,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _blade = NewMultiMesh(BuildBlade(mat));
        _leaf = NewMultiMesh(BuildBroadleaf(mat));
        _bladeMmi = new MultiMeshInstance3D { Name = "AmbientBlades", Multimesh = _blade, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _leafMmi = new MultiMeshInstance3D { Name = "AmbientTinyLeaves", Multimesh = _leaf, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_bladeMmi); AddChild(_leafMmi);
        _revision = -1; _accum = 999; _lastCam = new(float.MaxValue, 0, 0);
    }

    private static MultiMesh NewMultiMesh(ArrayMesh mesh) => new()
    {
        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
        UseColors = true,
        Mesh = mesh,
        InstanceCount = 0,
    };

    public override void _Process(double delta)
    {
        if (_w == null) return;
        bool enabled = Quality > 0;
        _bladeMmi.Visible = _leafMmi.Visible = enabled;
        if (!enabled) { _accum = 999; return; }

        _accum += delta;
        if (_accum < 1.2) return;
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool moved = cam.DistanceSquaredTo(_lastCam) > 2.0f * 2.0f;
        bool changed = _revision != _w.AmbientGroundCover.Revision;
        if (!moved && !changed && _accum < 6.0) return;
        _accum = 0; _lastCam = cam; _revision = _w.AmbientGroundCover.Revision;
        Refresh();
    }

    private void Refresh()
    {
        var g = _w.Grid;
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool haveCam = Camera != null;
        var bladeT = new List<Transform3D>(); var bladeC = new List<Color>();
        var leafT = new List<Transform3D>(); var leafC = new List<Color>();
        var rng = new CellRng();

        int i0 = 0, i1 = g.Nx - 1, j0 = 0, j1 = g.Nz - 1;
        if (haveCam)
        {
            i0 = Math.Max(0, (int)((cam.X - CullRadius - g.OriginX) / g.CellSize));
            i1 = Math.Min(g.Nx - 1, (int)((cam.X + CullRadius - g.OriginX) / g.CellSize));
            j0 = Math.Max(0, (int)((cam.Z - CullRadius - g.OriginZ) / g.CellSize));
            j1 = Math.Min(g.Nz - 1, (int)((cam.Z + CullRadius - g.OriginZ) / g.CellSize));
        }

        int stride = Quality >= 2 ? 1 : 1;
        for (int j = j0; j <= j1; j += stride)
        for (int i = i0; i <= i1; i += stride)
        {
            if (!g.InDomain(i, j)) continue;
            int idx = g.Index(i, j);
            double cover = _w.AmbientGroundCover.Cover[idx];
            if (cover < 0.035) continue;
            var center = g.CellCenter(idx);
            float distFade = 1;
            if (haveCam)
            {
                float dx = (float)center.X - cam.X, dz = (float)center.Z - cam.Z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d > CullRadius) continue;
                if (d > FadeStart) distFade = 1 - (d - FadeStart) / (CullRadius - FadeStart);
                if (distFade < 0.08f) continue;
            }

            var (moisture, light) = NeighbourCharacter(i, j);
            double broadleaf = MathD.Clamp(0.08 + (1 - light) * 0.30 + moisture * 0.14, 0.05, 0.46);
            var dry = new Color(0.47f, 0.53f, 0.20f);
            var mesic = new Color(0.22f, 0.47f, 0.17f);
            var shade = new Color(0.13f, 0.34f, 0.21f);
            var baseCol = dry.Lerp(mesic, (float)MathD.Clamp01((moisture - 0.08) / 0.45));
            baseCol = baseCol.Lerp(shade, (float)MathD.Clamp01((0.58 - light) / 0.5));

            rng.Seed = Rng.Mix(_w.Seed, (ulong)idx + 0xA6B13UL);
            int count = Math.Clamp((int)Math.Round((6.0 + cover * 8.0) * Quality * distFade), 3, Quality >= 2 ? 24 : 15);
            for (int k = 0; k < count; k++)
            {
                double jx = (rng.Next01() + rng.Next01() - 1) * g.CellSize * 0.54;
                double jz = (rng.Next01() + rng.Next01() - 1) * g.CellSize * 0.54;
                var p = center + new Vivarium.Sim.Core.Vec2(jx, jz);
                if (!_w.Domain.Contains(p) || _w.SubstrateAtCell(p) != Substrate.Soil || _w.Water.OpenWaterDepth(p) > 0.004) continue;

                bool isLeaf = rng.Next01() < broadleaf;
                float yaw = (float)(rng.Next01() * Math.PI * 2);
                float scale = (float)((0.58 + rng.Next01() * 0.38) * (0.86 + cover * 0.18));
                float heightScale = isLeaf
                    ? scale * (float)(0.70 + 0.16 * moisture)
                    : scale * (float)(0.72 + 0.22 * light);
                var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, heightScale, scale));
                float y = (float)_w.Terrain.Height(p) + 0.0015f;
                var t = new Transform3D(basis, new Vector3((float)p.X, y, (float)p.Z));
                float vary = (float)(0.84 + rng.Next01() * 0.24);
                var col = new Color(baseCol.R * vary, baseCol.G * vary, baseCol.B * vary, 1);
                if (isLeaf) { leafT.Add(t); leafC.Add(col); }
                else { bladeT.Add(t); bladeC.Add(col); }
            }
        }

        Fill(_blade, bladeT, bladeC);
        Fill(_leaf, leafT, leafC);
    }

    private (double Moisture, double Light) NeighbourCharacter(int i, int j)
    {
        double m = 0, l = 0, w = 0;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!_w.Grid.InDomain(i + dx, j + dz)) continue;
            int idx = _w.Grid.Index(i + dx, j + dz);
            double weight = dx == 0 && dz == 0 ? 2 : 1;
            m += _w.Fields.Moisture.Values[idx] * weight;
            l += _w.FloraSystem.EffectiveLightCell(idx) * weight;
            w += weight;
        }
        return w > 0 ? (m / w, l / w) : (0.5, 0.7);
    }

    private static ArrayMesh BuildBlade(Material mat)
    {
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        // A render instance is only a tiny three-blade tuft. High instance count plus deterministic cell jitter
        // makes this read as continuous anonymous turf instead of repeated circular clumps.
        for (int k = 0; k < 3; k++)
        {
            double a = k * 2.399963229728653 + 0.31;
            var root = new Vec3(Math.Cos(a) * 0.004, 0, Math.Sin(a) * 0.004);
            double len = 0.014 + k * 0.004;
            var side = new Vec3(Math.Cos(a + Math.PI * 0.5), 0, Math.Sin(a + Math.PI * 0.5));
            var tip = root + new Vec3(Math.Cos(a + 0.7) * 0.0025, len, Math.Sin(a + 0.7) * 0.0025);
            double hw = 0.00072 + k * 0.00012;
            var left = root - side * hw;
            var right = root + side * hw;
            var n = (right - left).Cross(tip - left).Normalized();
            int f0 = m.AddVertex(left, n, white), f1 = m.AddVertex(right, n, white), f2 = m.AddVertex(tip, n, white);
            m.AddTriangle(f0, f1, f2);
            int b0 = m.AddVertex(left, -n, white), b1 = m.AddVertex(tip, -n, white), b2 = m.AddVertex(right, -n, white);
            m.AddTriangle(b0, b1, b2);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private static ArrayMesh BuildBroadleaf(Material mat)
    {
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        var crown = new Vec3(0, 0.008, 0);
        for (int k = 0; k < 3; k++)
        {
            double a = 0.4 + k * Math.PI * 2 / 3;
            var dir = new Vec3(Math.Cos(a), 0.08, Math.Sin(a)).Normalized();
            var side = new Vec3(-Math.Sin(a), 0, Math.Cos(a));
            var tip = crown + dir * 0.0085;
            var baseMid = crown + dir * 0.0015;
            double hw = 0.0036;
            var n = dir.Cross(side).Normalized();
            int i0 = m.AddVertex(baseMid - side * hw, n, white);
            int i1 = m.AddVertex(baseMid + side * hw, n, white);
            int i2 = m.AddVertex(tip, n, white);
            m.AddTriangle(i0, i1, i2);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private void Fill(MultiMesh mm, List<Transform3D> xf, List<Color> col)
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
            buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1;
        }
        mm.Buffer = buf;
    }

    private struct CellRng
    {
        private ulong _s;
        public ulong Seed { set => _s = value == 0 ? 0x9E3779B97F4A7C15UL : value; }
        private uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 32); }
        public double Next01() => Next() * (1.0 / 4294967296.0);
    }
}
