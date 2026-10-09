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

    private int _terrainVersion = int.MinValue;
    private int _lastQuality = -1;
    private readonly List<Transform3D> _bladeT = new();
    private readonly List<Color> _bladeC = new();
    private readonly List<Transform3D> _leafT = new();
    private readonly List<Color> _leafC = new();

    public override void _Process(double delta)
    {
        if (_w == null) return;
        bool enabled = Quality > 0;
        _bladeMmi.Visible = _leafMmi.Visible = enabled;
        if (!enabled) { _accum = 999; return; }

        _accum += delta;
        if (_accum < 1.0) return;
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool moved = cam.DistanceSquaredTo(_lastCam) > 2.0f * 2.0f;
        bool changed = _revision != _w.AmbientGroundCover.Revision;
        bool terrainChanged = _terrainVersion != _w.Terrain.Version;
        bool qualityChanged = _lastQuality != Quality;
        if (!moved && !changed && !terrainChanged && !qualityChanged) return;
        _accum = 0;
        _lastCam = cam;
        _revision = _w.AmbientGroundCover.Revision;
        _terrainVersion = _w.Terrain.Version;
        _lastQuality = Quality;
        Refresh();
    }

    private void Refresh()
    {
        var g = _w.Grid;
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool haveCam = Camera != null;
        _bladeT.Clear(); _bladeC.Clear();
        _leafT.Clear(); _leafC.Clear();
        var rng = new CellRng();

        var nearRocks = new List<Vivarium.Sim.World.Rock>();
        var nearLogs = new List<Vivarium.Sim.World.LogProp>();
        var nearGravel = new List<Vivarium.Sim.World.GravelPatch>();
        var nearFlora = new List<(Vivarium.Sim.Core.Vec2 P, double Exclusion)>();
        float searchRadius = CullRadius + 2.5f;
        var camP = new Vivarium.Sim.Core.Vec2(cam.X, cam.Z);

        foreach (var r in _w.Props.Rocks)
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, r.Position) <= searchRadius + r.FootprintRadius)
                nearRocks.Add(r);

        foreach (var l in _w.Props.Logs)
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, l.Position) <= searchRadius + l.Length * 0.5 + l.Radius)
                nearLogs.Add(l);

        foreach (var gr in _w.Props.Gravel)
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, gr.Position) <= searchRadius + gr.Radius)
                nearGravel.Add(gr);

        foreach (var plant in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(plant.SpeciesId);
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, plant.Position) <= searchRadius + Math.Max(0.4, plant.Radius(sp)))
            {
                bool isWoody = sp.Shape == "tree" || sp.Tags.Contains("tree") || sp.Tags.Contains("woody");
                double collarR = isWoody ? Math.Max(0.22, plant.Radius(sp) * 0.38) : Math.Max(0.14, plant.Radius(sp) + 0.08);
                nearFlora.Add((plant.Position, collarR));
            }
        }

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
            if (cover < 0.02) continue;
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
            if (light < 0.04) continue;
            double fineCover = 1.0 - Math.Exp(-_w.Litter.FineMass[idx] * 55.0);
            double litterPenalty = Math.Clamp(1.0 - fineCover * 0.65, 0.15, 1.0);

            double broadleaf = MathD.Clamp(0.22 + (1 - light) * 0.34 + moisture * 0.18, 0.18, 0.68);
            var dry = new Color(0.47f, 0.53f, 0.20f);
            var mesic = new Color(0.22f, 0.47f, 0.17f);
            var shade = new Color(0.13f, 0.34f, 0.21f);
            var baseCol = dry.Lerp(mesic, (float)MathD.Clamp01((moisture - 0.08) / 0.45));
            baseCol = baseCol.Lerp(shade, (float)MathD.Clamp01((0.58 - light) / 0.5));

            rng.Seed = Rng.Mix(_w.Seed, (ulong)idx + 0xA6B13UL);
            int count = Math.Clamp((int)Math.Round((8.0 + cover * 14.0) * Quality * distFade * litterPenalty), 2, Quality >= 2 ? 36 : 22);

            // Terrain slope normal and tilt
            double e = g.CellSize;
            double hx0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(-e, 0));
            double hx1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(e, 0));
            double hz0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, -e));
            double hz1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, e));
            var norm = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
            if (norm.Y < 0.60f) continue;
            var cellTilt = SurfaceFrame.TiltTo(norm.Y < 0.5f ? new Vector3(norm.X, 0, norm.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : norm);

            for (int k = 0; k < count; k++)
            {
                double jx = (rng.Next01() + rng.Next01() - 1) * g.CellSize * 0.52;
                double jz = (rng.Next01() + rng.Next01() - 1) * g.CellSize * 0.52;
                var p = center + new Vivarium.Sim.Core.Vec2(jx, jz);
                if (!_w.Domain.Contains(p) || _w.SubstrateAtCell(p) != Substrate.Soil || _w.Water.OpenWaterDepth(p) > 0.004) continue;

                bool excluded = false;
                for (int ri = 0; ri < nearRocks.Count; ri++) if (nearRocks[ri].Covers(p)) { excluded = true; break; }
                if (excluded) continue;
                for (int li = 0; li < nearLogs.Count; li++) if (nearLogs[li].Covers(p)) { excluded = true; break; }
                if (excluded) continue;
                for (int gi = 0; gi < nearGravel.Count; gi++) if (nearGravel[gi].Covers(p)) { excluded = true; break; }
                if (excluded) continue;
                for (int pi = 0; pi < nearFlora.Count; pi++)
                {
                    if (Vivarium.Sim.Core.Vec2.Distance(p, nearFlora[pi].P) < nearFlora[pi].Exclusion) { excluded = true; break; }
                }
                if (excluded) continue;

                bool isLeaf = rng.Next01() < broadleaf;
                float yaw = (float)(rng.Next01() * Math.PI * 2);
                float scale = (float)((0.58 + rng.Next01() * 0.38) * (0.86 + cover * 0.18)) * (isLeaf ? (float)(0.8 + rng.Next01() * 0.9) : 1f);
                float heightScale = isLeaf
                    ? scale * (float)(0.70 + 0.16 * moisture)
                    : scale * (float)(0.72 + 0.22 * light);
                var basis = cellTilt * new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, heightScale, scale));
                // Root collar / basal crown embedding 20-30 mm
                float y = (float)_w.Terrain.Height(p) - 0.0025f;
                var t = new Transform3D(basis, new Vector3((float)p.X, y, (float)p.Z));
                float vary = (float)(0.84 + rng.Next01() * 0.24);
                var col = new Color(baseCol.R * vary, baseCol.G * vary, baseCol.B * vary, 1);
                if (isLeaf) { _leafT.Add(t); _leafC.Add(col); }
                else { _bladeT.Add(t); _bladeC.Add(col); }
            }
        }

        Fill(_blade, _bladeT, _bladeC);
        Fill(_leaf, _leafT, _leafC);
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
        // Low rosette of 5 overlapping leaves of varied size: each blade is folded along its midrib (raised midrib,
        // edges lifted = cupped) and droops toward the tip so the cover reads as leafy, not as flat cards.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        var crown = new Vec3(0, 0.004, 0);
        double[] sizes = { 1.0, 0.72, 1.25, 0.85, 0.6 };
        for (int k = 0; k < 5; k++)
        {
            double a = 0.4 + k * 2.399963229728653;
            double sz = sizes[k];
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = new Vec3(-Math.Sin(a), 0, Math.Cos(a));
            double len = 0.011 * sz, hw = 0.0042 * sz;
            var b0 = crown + dir * (0.001 * sz);
            var mid = crown + dir * (len * 0.5) + new Vec3(0, 0.0032 * sz, 0);   // midrib arches up
            var tip = crown + dir * len + new Vec3(0, 0.0004 * sz, 0);          // then droops at the tip
            var el = crown + dir * (len * 0.48) - side * hw + new Vec3(0, 0.0042 * sz, 0); // edges curl up (cup)
            var er = crown + dir * (len * 0.48) + side * hw + new Vec3(0, 0.0042 * sz, 0);
            void Tri(Vec3 p0, Vec3 p1, Vec3 p2)
            {
                var n = (p1 - p0).Cross(p2 - p0).Normalized();
                if (n.Y < 0) { n = -n; (p1, p2) = (p2, p1); }
                int i0 = m.AddVertex(p0, n, white), i1 = m.AddVertex(p1, n, white), i2 = m.AddVertex(p2, n, white);
                m.AddTriangle(i0, i1, i2);
            }
            Tri(b0, mid, el); Tri(b0, er, mid); Tri(el, mid, tip); Tri(mid, er, tip);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private void Fill(MultiMesh mm, List<Transform3D> xf, List<Color> col)
    {
        int n = xf.Count;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        mm.VisibleInstanceCount = n;
        if (n == 0) return;
        bool resized = !_buffers.TryGetValue(mm, out var buf) || buf.Length != n * 16;
        if (resized) _buffers[mm] = buf = new float[n * 16];
        bool changed = resized;
        for (int i = 0; i < n; i++)
        {
            var t = xf[i]; var c = col[i]; int o = i * 16;
            if (buf![o + 0] != t.Basis.X.X || buf[o + 1] != t.Basis.Y.X || buf[o + 2] != t.Basis.Z.X || buf[o + 3] != t.Origin.X ||
                buf[o + 4] != t.Basis.X.Y || buf[o + 5] != t.Basis.Y.Y || buf[o + 6] != t.Basis.Z.Y || buf[o + 7] != t.Origin.Y ||
                buf[o + 8] != t.Basis.X.Z || buf[o + 9] != t.Basis.Y.Z || buf[o + 10] != t.Basis.Z.Z || buf[o + 11] != t.Origin.Z ||
                buf[o + 12] != c.R || buf[o + 13] != c.G || buf[o + 14] != c.B)
            {
                changed = true;
                buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
                buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
                buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
                buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1;
            }
        }
        if (changed || mm.Buffer == null || mm.Buffer.Length != n * 16)
        {
            mm.Buffer = buf;
        }
    }

    private struct CellRng
    {
        private ulong _s;
        public ulong Seed { set => _s = value == 0 ? 0x9E3779B97F4A7C15UL : value; }
        private uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 32); }
        public double Next01() => Next() * (1.0 / 4294967296.0);
    }
}
