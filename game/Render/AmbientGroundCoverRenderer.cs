using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Flora;
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
    public int LastGroundcoverInstances { get; private set; }
    public int LastInfluencedGridCells { get; private set; }
    public int LastFloraExclusionCandidateChecks { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _buffers.Clear();
        foreach (var c in GetChildren()) c.QueueFree();

        // Same optical rules for ephemeral ground vegetation and persistent organisms.
        var mat = Bridge.Shader("res://Shaders/ground_cover.gdshader");
        mat.SetShaderParameter("sway_strength", 0f);
        _blade = NewMultiMesh(BuildBlade(mat));
        _leaf = NewMultiMesh(BuildBroadleaf(mat));
        // Turf this dense only reads as ground cover when it grounds itself with a contact shadow.
        _bladeMmi = new MultiMeshInstance3D { Name = "AmbientBlades", Multimesh = _blade, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        _leafMmi = new MultiMeshInstance3D { Name = "AmbientTinyLeaves", Multimesh = _leaf, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_bladeMmi); AddChild(_leafMmi);
        BuildBankFringe(mat);
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
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("AmbientGround");
        if (_w == null) return;
        bool enabled = Quality > 0;
        _bladeMmi.Visible = _leafMmi.Visible = enabled;
        if (_bankNode != null) _bankNode.Visible = enabled;
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
        // Exact circular exclusion via 75cm spatial hash buckets, replacing
        // O(plants * anonymous foliage blades) per-instance scans.
        var floraExclusions = new AmbientCoverSpatialIndex();
        var livingCarpet = new AmbientCoverSpatialIndex();
        // Conservative single discs handed to the bank fringe; it needs the raw exclusions, not a hash.
        var floraDiscs = new List<(Vec2 P, double Exclusion)>();
        int exclusionCandidates=0, influencedCells=0;
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
                double collarR = isWoody ?
                    Math.Max(0.22, plant.Radius(sp) * 0.38) :
                    Math.Max(0.14, plant.Radius(sp) + 0.08);
                if (sp.Archetype=="plant" && sp.Tags.Contains("groundcover") && !isWoody)
                {
                    // Living clonal carpets fill in between leaves as the
                    // actual tracked plant gains biomass. These are inexpensive
                    // anonymous turf instances, not simulated daughter plants.
                    double maturity = plant.BiomassFraction(sp);
                    livingCarpet.Add(plant.Position,
                        Math.Max(0.16, plant.Radius(sp)*1.5),
                        maturity * Math.Clamp(plant.Health,0,1));
                    collarR = Math.Min(0.035, Math.Max(0.008, plant.Radius(sp)*0.16));
                }
                floraExclusions.Add(plant.Position, collarR);
                floraDiscs.Add((plant.Position, collarR));
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
            // This present-state influence rises naturally as established
            // rooted carpets accumulate biomass. Constant authored blades
            // become denser without creating extra tracked organisms.
            double establishedCarpet = livingCarpet.Influence(center);
            if(establishedCarpet>0.02) influencedCells++;
            int count = Math.Clamp(
                (int)Math.Round((8.0 + cover * 14.0 +
                    establishedCarpet * 11.0) * Quality * distFade * litterPenalty),
                2, Quality >= 2 ? 44 : 34);

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
                excluded = floraExclusions.Contains(p);
                exclusionCandidates += floraExclusions.LastCandidates;
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
        LastGroundcoverInstances = _bladeT.Count+_leafT.Count;
        LastInfluencedGridCells = influencedCells;
        LastFloraExclusionCandidateChecks = exclusionCandidates;
        // Creeping stolons and bank-fringe leaves follow the same water margins the turf avoids.
        RefreshBankFringe(cam, haveCam, nearRocks, nearLogs, nearGravel, floraDiscs);
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
        // A render instance is a 5-blade turf cluster: high instance count plus deterministic cell
        // jitter makes this read as continuous anonymous turf instead of repeated circular clumps.
        // Every blade is a 6 x 2 cambered V-sheet carrying real u (across the width) and v (base to
        // tip), so ground_cover.gdshader's midrib and vein terms are live instead of dead code.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        for (int k = 0; k < 5; k++)
        {
            double a = k * 2.399963229728653 + 0.31;
            AddBladeSheet(m, a, 0.017 + k * 0.0032, 0.00078 + k * 0.00011, white);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddBladeSheet(MeshData m, double angle, double length, double halfWidth, double[] rgb)
    {
        const int rows = 6, lanes = 2;
        var pos = new Vec3[rows + 1, lanes + 1];
        // Buried root collar, then a steeper rise and an outward-drooping tip.
        var root = new Vec3(Math.Cos(angle) * 0.0035, -0.0015, Math.Sin(angle) * 0.0035);
        var lean = new Vec3(Math.Cos(angle + 0.55), 0, Math.Sin(angle + 0.55));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double height = length * (t - 0.40 * t * t);
            double reach = length * 0.44 * t * t;
            double width = halfWidth * Math.Max(0.08, 1.0 - 0.94 * t * t);
            for (int lane = 0; lane <= lanes; lane++)
            {
                // Lanes run +side to -side so the sheet normal (and therefore the front face) points
                // away from the clump axis, i.e. the visible outside of the blade.
                double s = 1.0 - 2.0 * lane / lanes;                         // +1, 0, -1
                double ridge = halfWidth * 0.85 * (1.0 - Math.Abs(s));       // raised midrib
                double fold = -halfWidth * 0.34 * Math.Abs(s);               // folded edges: V camber
                pos[r, lane] = root + Vec3.Up * (height + ridge + fold) + lean * reach + side * (width * s);
            }
        }
        EmitSheet(m, pos, rows, lanes, rgb, 0.58, 1.0);
    }

    private static ArrayMesh BuildBroadleaf(Material mat)
    {
        // Low rosette of five curled cotyledons of varied size. Each leaf is an 8 x 6 grid with an
        // arched petiole, a crenate margin and cupped edges, carrying real u (across) and v (petiole
        // to tip) so the venation terms resolve on every blade.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        double[] sizes = { 1.0, 0.72, 1.25, 0.85, 0.60 };
        for (int k = 0; k < 5; k++)
            AddLeafSheet(m, 0.4 + k * 2.399963229728653, sizes[k], white);
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddLeafSheet(MeshData m, double angle, double size, double[] rgb)
    {
        const int rows = 8, lanes = 6;
        var pos = new Vec3[rows + 1, lanes + 1];
        var dir = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        var crown = new Vec3(0, 0.0032, 0);
        double length = 0.0115 * size, halfWidth = 0.0048 * size;
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double bladeT = Math.Clamp((t - 0.22) / 0.78, 0, 1);
            double profile = Math.Sin(Math.PI * bladeT);
            // Arched petiole lifts the blade off the soil, then the tip droops back toward it.
            double arch = 0.30 * length * Math.Sin(Math.PI * Math.Clamp(t, 0, 1))
                + 0.010 * length * Math.Min(1.0, t / 0.22) - 0.02 * length * t * t * t;
            // Crenate margin: shallow scallops rather than a smooth lens.
            double width = halfWidth * Math.Pow(profile, 0.70) * (1.0 + 0.06 * Math.Sin(bladeT * Math.Tau * 4.5));
            width = Math.Max(width, halfWidth * 0.075);
            for (int lane = 0; lane <= lanes; lane++)
            {
                double s = 2.0 * lane / lanes - 1.0;
                double cup = halfWidth * 0.55 * Math.Abs(s) * profile;          // margin curls up
                double rib = halfWidth * 0.30 * (1.0 - Math.Abs(s)) * profile;  // raised midrib
                pos[r, lane] = crown + dir * (length * t) + side * (width * s) + Vec3.Up * (arch + cup + rib);
            }
        }
        EmitSheet(m, pos, rows, lanes, rgb, 0.55, 1.0);
    }

    /// <summary>
    /// Emits one single-sided sheet with smooth normals and real (u, v) coordinates.
    /// Godot front faces are clockwise, so every triangle is reversed against its vertex normal.
    /// </summary>
    private static void EmitSheet(MeshData m, Vec3[,] grid, int rows, int lanes, double[] rgb,
        double aoBase, double aoTip)
    {
        int stride = lanes + 1;
        int count = (rows + 1) * stride;
        var normals = new Vec3[count];
        for (int r = 0; r < rows; r++)
        for (int lane = 0; lane < lanes; lane++)
        {
            Vec3 p00 = grid[r, lane], p10 = grid[r, lane + 1];
            Vec3 p01 = grid[r + 1, lane], p11 = grid[r + 1, lane + 1];
            Vec3 n0 = (p10 - p00).Cross(p11 - p00);
            Vec3 n1 = (p11 - p00).Cross(p01 - p00);
            int v00 = r * stride + lane, v10 = v00 + 1;
            int v01 = (r + 1) * stride + lane, v11 = v01 + 1;
            normals[v00] += n0; normals[v10] += n0; normals[v11] += n0;
            normals[v00] += n1; normals[v11] += n1; normals[v01] += n1;
        }
        for (int i = 0; i < count; i++)
            normals[i] = normals[i].LengthSq > 1e-20 ? normals[i].Normalized() : Vec3.Up;

        var index = new int[count];
        for (int r = 0; r <= rows; r++)
        for (int lane = 0; lane <= lanes; lane++)
        {
            int v = r * stride + lane;
            double u = lane / (double)lanes, w = r / (double)rows;
            double ao = aoBase + (aoTip - aoBase) * w;
            index[v] = m.AddVertex(grid[r, lane], normals[v], rgb, 1, u, w, ao, 0);
        }
        for (int r = 0; r < rows; r++)
        for (int lane = 0; lane < lanes; lane++)
        {
            int a = index[r * stride + lane], b = index[r * stride + lane + 1];
            int c = index[(r + 1) * stride + lane + 1], d = index[(r + 1) * stride + lane];
            AddSheetTriangle(m, a, b, c);
            AddSheetTriangle(m, a, c, d);
        }
    }

    private static void AddSheetTriangle(MeshData m, int a, int b, int c)
    {
        var face = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
        if (face.Dot(m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c)) > 0) (b, c) = (c, b);
        m.AddTriangle(a, b, c);
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
