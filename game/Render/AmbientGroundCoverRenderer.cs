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
    private IEnumerator<bool>? _pendingRefresh;
    private bool _dirty = true;
    private double _editDelay;
    public int RefreshCount { get; private set; }
    public bool RefreshPending => _dirty || _pendingRefresh is not null;
    private Vector3 _lastCam = new(float.MaxValue, 0, 0);
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    private const float CullRadius = 11.5f;
    private const float FadeStart = 8.0f;
    public int LastGroundcoverInstances { get; private set; }
    public int LastInfluencedGridCells { get; private set; }
    public int LastFloraExclusionCandidateChecks { get; private set; }

    public void Build(VivariumWorld w, IslandRenderer? habitat = null)
    {
        _pendingRefresh?.Dispose(); _pendingRefresh = null;
        _w = w;
        _buffers.Clear();
        foreach (var c in GetChildren()) c.QueueFree();

        // Same optical rules for ephemeral ground vegetation and persistent organisms.
        var mat = Bridge.Shader("res://Shaders/ground_cover.gdshader");
        mat.SetShaderParameter("sway_strength", 0f);
        mat.SetShaderParameter("instance_shape_variation", true);
        // The custom direct-light path has its own cuticle lobes; SPECULAR alone
        // only controls the engine's environment response.
        mat.SetShaderParameter("tissue_specular_stress", .12f);
        mat.SetShaderParameter("tissue_material_f0", .02f);
        mat.SetShaderParameter("tissue_direct_transmission", .20f);
        habitat?.BindHabitatMaterial(mat);
        _blade = NewMultiMesh(BuildBlade(mat, true));
        _leaf = NewMultiMesh(BuildBroadleaf(mat, true));
        // Turf this dense only reads as ground cover when it grounds itself with a contact shadow.
        _bladeMmi = new MultiMeshInstance3D { Name = "AmbientBlades", Multimesh = _blade, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        _leafMmi = new MultiMeshInstance3D { Name = "AmbientTinyLeaves", Multimesh = _leaf, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        AddChild(_bladeMmi); AddChild(_leafMmi);
        BuildBankFringe(mat);
        habitat?.BindHabitatMaterial(_bankMaterial);
        _dirty = true; _editDelay = 0; RefreshCount = 0;
        _lastCam = new(float.MaxValue, 0, 0);
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
        if (!enabled) { _pendingRefresh?.Dispose(); _pendingRefresh = null; _dirty = true; return; }
        var cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool moved = cam.DistanceSquaredTo(_lastCam) > 2.0f * 2.0f;
        bool terrainChanged = _terrainVersion != _w.Terrain.Version;
        bool qualityChanged = _lastQuality != Quality;
        if (terrainChanged || qualityChanged)
        {
            Invalidate();
            _terrainVersion = _w.Terrain.Version; _lastQuality = Quality;
        }
        _editDelay -= delta > 0 ? delta : .016;
        if (_pendingRefresh is null && (_dirty || moved) && _editDelay <= 0)
        {
            _dirty = false; _lastCam = cam;
            _pendingRefresh = RefreshBatched().GetEnumerator();
        }
        if (_pendingRefresh is null) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        do
        {
            if (!_pendingRefresh.MoveNext())
            {
                _pendingRefresh.Dispose(); _pendingRefresh = null; RefreshCount++; break;
            }
        } while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < 2);
    }

    /// <summary>Coalesce edits, keeping complete visible geometry while its replacement prepares.</summary>
    public void Invalidate(Vec2? centre = null, double radius = double.PositiveInfinity)
    {
        _pendingRefresh?.Dispose(); _pendingRefresh = null;
        if (centre is { } p)
        {
            foreach (var (key, patch) in _bankPatches)
            {
                double x = Math.Clamp(p.X, key.X * 2, (key.X + 1) * 2);
                double z = Math.Clamp(p.Z, key.Z * 2, (key.Z + 1) * 2);
                if (Vec2.DistanceSq(p, new(x, z)) <= radius * radius) patch.Epoch = -1;
            }
        }
        else _bankEpoch++;
        if (_w is not null) _terrainVersion = _w.Terrain.Version;
        _dirty = true; _editDelay = .15;
    }

    public override void _ExitTree() { _pendingRefresh?.Dispose(); _pendingRefresh = null; }

    private IEnumerable<bool> RefreshBatched()
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
            // The bank cache may prepare an entire patch across the view boundary.
            // Keep all plant exclusion discs, rather than cutting them at this camera pose.
            {
                bool isWoody = sp.Shape == "tree" || sp.Tags.Contains("tree") || sp.Tags.Contains("woody");
                // Keep a readable opening at each simulated plant, while allowing low
                // anonymous cover beneath its canopy. Include the turf's leaf reach.
                double collarR = (isWoody ?
                    Math.Clamp(plant.Radius(sp) * 0.32, 0.12, 0.28) :
                    Math.Clamp(plant.Radius(sp) * 0.65, 0.045, 0.24)) + 0.055;
                if (sp.Archetype=="plant" && sp.Tags.Contains("groundcover") && !isWoody)
                {
                    // Living clonal carpets fill in between leaves as the
                    // actual tracked plant gains biomass. These are inexpensive
                    // anonymous turf instances, not simulated daughter plants.
                    double maturity = plant.BiomassFraction(sp);
                    livingCarpet.Add(plant.Position,
                        Math.Max(0.16, plant.Radius(sp)*1.5),
                        maturity * Math.Clamp(plant.Health,0,1));
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
            yield return true;
            if (!g.InDomain(i, j)) continue;
            int idx = g.Index(i, j);
            double cover = _w.AmbientGroundCover.Cover[idx];
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
            double broadleaf = MathD.Clamp(0.22 + (1 - light) * 0.34 + moisture * 0.18
                + .12 * Vivarium.Sim.Core.Noise.Gradient(_w.Seed, center.X * 2.7, center.Z * 2.7), .18, .72);

            rng.Seed = Rng.Mix(_w.Seed, (ulong)idx + 0xA6B13UL);
            // This present-state influence rises naturally as established
            // rooted carpets accumulate biomass. Constant authored blades
            // become denser without creating extra tracked organisms.
            double establishedCarpet = livingCarpet.Influence(center);
            if(establishedCarpet>0.02) influencedCells++;
            // Ground fabric is continuous even where the coarse historical cover field
            // was cleared by a plant. Point exclusions below supply the actual berth.
            int count = Math.Max(1, (int)Math.Round((Quality >= 2 ? 42 : 36)
                * (g.CellSize * g.CellSize / .0625) * distFade * (1 + establishedCarpet * .12)));
            int columns = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)columns);
            double phaseX = rng.Next01(), phaseZ = rng.Next01();

            // Terrain slope normal and tilt
            double e = g.CellSize;
            double hx0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(-e, 0));
            double hx1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(e, 0));
            double hz0 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, -e));
            double hz1 = _w.Terrain.Height(center + new Vivarium.Sim.Core.Vec2(0, e));
            var norm = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
            if (norm.Y < 0.70f) continue;
            var cellTilt = SurfaceFrame.TiltTo(norm.Y < 0.5f ? new Vector3(norm.X, 0, norm.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : norm);

            for (int k = 0; k < count; k++)
            {
                // Independently rotated strata plus a continuous world-space warp retain
                // coverage without lining up the same little rows in neighbouring cells.
                double u = (k % columns + rng.Next01()) / columns + phaseX;
                double v = (k / columns + rng.Next01()) / rows + phaseZ;
                double jx = (u - Math.Floor(u) - .5) * g.CellSize;
                double jz = (v - Math.Floor(v) - .5) * g.CellSize;
                double wx = center.X + jx, wz = center.Z + jz;
                jx += .025 * Vivarium.Sim.Core.Noise.Gradient(Rng.Mix(_w.Seed, 71), wx * 4, wz * 4);
                jz += .025 * Vivarium.Sim.Core.Noise.Gradient(Rng.Mix(_w.Seed, 73), wx * 4, wz * 4);
                var p = center + new Vivarium.Sim.Core.Vec2(jx, jz);
                if (!_w.Domain.Contains(p) || _w.SubstrateAt(p) != Substrate.Soil || _w.Water.OpenWaterDepth(p) > 0.0003) continue;

                bool excluded = false;
                for (int ri = 0; ri < nearRocks.Count; ri++) if (nearRocks[ri].Covers(p)) { excluded = true; break; }
                if (excluded) continue;
                for (int li = 0; li < nearLogs.Count; li++) if (nearLogs[li].Covers(p)) { excluded = true; break; }
                if (excluded) continue;
                double gravelWeight = GravelCoverWeight(p, nearGravel);
                if (rng.Next01() > gravelWeight) continue;
                excluded = floraExclusions.Contains(p);
                exclusionCandidates += floraExclusions.LastCandidates;
                if (excluded) continue;

                bool isLeaf = rng.Next01() < broadleaf;
                float yaw = (float)(rng.Next01() * Math.PI * 2);
                float scale = (float)((0.78 + rng.Next01() * 0.38) * (0.94 + cover * 0.08))
                    * (float)(.65 + .35 * gravelWeight);
                float heightScale = isLeaf
                    ? scale * (float)(0.70 + 0.16 * moisture)
                    : scale * (float)(0.72 + 0.22 * light);
                float aspect = (float)(.75 + rng.Next01() * .58);
                heightScale *= (float)(.62 + rng.Next01() * .83);
                var basis = cellTilt * new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale * aspect, heightScale, scale / aspect));
                // Embed the root collar by one millimetre; leave the crown visible.
                float y = (float)_w.Terrain.Height(p) - 0.001f;
                var t = new Transform3D(basis, new Vector3((float)p.X, y, (float)p.Z));
                // The live field supplies habitat colour in the shader; vertex colour
                // supplies only a small stable tissue variation, never a stale field sample.
                float vary = (float)(0.88 + rng.Next01() * 0.16);
                var col = new Color(vary, vary, vary, 1);
                if (isLeaf) { _leafT.Add(t); _leafC.Add(col); }
                else { _bladeT.Add(t); _bladeC.Add(col); }
            }
        }

        using (FrameProfiler.Measure("AmbientGround.BladeUpload")) Fill(_blade, _bladeT, _bladeC);
        yield return true;
        using (FrameProfiler.Measure("AmbientGround.LeafUpload")) Fill(_leaf, _leafT, _leafC);
        yield return true;
        LastGroundcoverInstances = _bladeT.Count+_leafT.Count;
        LastInfluencedGridCells = influencedCells;
        LastFloraExclusionCandidateChecks = exclusionCandidates;
        // Creeping stolons and bank-fringe leaves follow the same water margins the turf avoids.
        foreach (bool step in RefreshBankFringeBatched(cam, haveCam, nearRocks, nearLogs, nearGravel, floraDiscs))
            yield return step;
    }

    private static double GravelCoverWeight(Vec2 p, List<GravelPatch> gravel)
    {
        double weight = 1;
        foreach (var patch in gravel)
        {
            var d = p - patch.Position;
            if (d.LengthSq > Math.Pow(patch.Radius + .20, 2)) continue;
            // Match GravelPatch.Covers' authored irregular outline, then taper on
            // the soil side. Gravel interiors stay clear of vegetation.
            double a = d.Angle;
            ulong seed = Rng.Mix(patch.VariantSeed, 0x47524156454CUL);
            double n = Vivarium.Sim.Core.Noise.Value3(seed, Math.Cos(a) * 1.7, Math.Sin(a) * 1.7, .37);
            double n2 = Vivarium.Sim.Core.Noise.Value3(Rng.Mix(seed, 17), Math.Cos(a) * 3.1, Math.Sin(a) * 3.1, .11);
            double edge = Math.Min(patch.Radius, patch.Radius * (.86 + .11 * (n * .5 + .5) + .04 * n2));
            double t = Math.Clamp((Math.Sqrt(d.LengthSq) - edge - .025) / .18, 0, 1);
            weight = Math.Min(weight, t * t * (3 - 2 * t));
        }
        return weight;
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

    private static ArrayMesh BuildBlade(Material mat, bool simplified = false)
    {
        // A render instance is a 5-blade turf cluster: high instance count plus deterministic cell
        // jitter makes this read as continuous anonymous turf instead of repeated circular clumps.
        // Every blade is a cambered V-sheet carrying real u (across the width) and v (base to
        // tip), so ground_cover.gdshader's midrib and vein terms are live instead of dead code.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        for (int k = 0; k < 5; k++)
        {
            double a = k * 2.399963229728653 + 0.31 + Math.Sin(k * 3.7) * .27;
            AddBladeSheet(m, a, .078 + .022 * Math.Sin(k * 2.8 + .4),
                .0034 + .0012 * Math.Cos(k * 3.1), white, simplified);
        }
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddBladeSheet(MeshData m, double angle, double length, double halfWidth, double[] rgb, bool simplified)
    {
        int rows = simplified ? 3 : 4; const int lanes = 2;
        var pos = new Vec3[rows + 1, lanes + 1];
        // Buried root collar, then a steeper rise and an outward-drooping tip.
        var root = new Vec3(Math.Cos(angle) * 0.0035, -0.0015, Math.Sin(angle) * 0.0035);
        var lean = new Vec3(Math.Cos(angle + 0.55), 0, Math.Sin(angle + 0.55));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double height = length * (t - (.28 + .18 * Math.Sin(angle * 1.7)) * t * t);
            double reach = length * (.32 + .20 * Math.Cos(angle * 2.3)) * t * t;
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

    private static ArrayMesh BuildBroadleaf(Material mat, bool simplified = false)
    {
        // Low rosette of five curled cotyledons of varied size. Each leaf has an
        // arched petiole, a crenate margin and cupped edges, carrying real u (across) and v (petiole
        // to tip) so the venation terms resolve on every blade.
        var m = new MeshData();
        var white = new[] { 1.0, 1.0, 1.0 };
        double[] sizes = { 1.0, 0.72, 1.25, 0.85, 0.60 };
        for (int k = 0; k < 5; k++)
            AddLeafSheet(m, 0.4 + k * 2.399963229728653 + Math.Sin(k * 2.1) * .31, sizes[k], white, simplified);
        return Bridge.ToArrayMesh(m, mat);
    }

    private static void AddLeafSheet(MeshData m, double angle, double size, double[] rgb, bool simplified)
    {
        int rows = simplified ? 4 : 6, lanes = simplified ? 2 : 4;
        var pos = new Vec3[rows + 1, lanes + 1];
        var dir = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
        var side = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
        var crown = new Vec3(0, 0.005, 0);
        double length = 0.043 * size, halfWidth = 0.016 * size * (.78 + .28 * Math.Cos(angle * 2.3));
        for (int r = 0; r <= rows; r++)
        {
            double t = r / (double)rows;
            double bladeT = Math.Clamp((t - 0.22) / 0.78, 0, 1);
            double profile = Math.Sin(Math.PI * bladeT);
            // Arched petiole lifts the blade off the soil, then the tip droops back toward it.
            double arch = 0.30 * length * Math.Sin(Math.PI * Math.Clamp(t, 0, 1))
                + 0.010 * length * Math.Min(1.0, t / 0.22) - 0.02 * length * t * t * t;
            // Crenate margin: shallow scallops rather than a smooth lens.
            double width = halfWidth * Math.Pow(Math.Max(0, profile), .52 + .36 * (.5 + .5 * Math.Sin(angle * 1.9)))
                * (1.0 + 0.07 * Math.Sin(bladeT * Math.Tau * 4.5));
            width = Math.Max(width, halfWidth * 0.075);
            for (int lane = 0; lane <= lanes; lane++)
            {
                double s = 2.0 * lane / lanes - 1.0;
                double cup = halfWidth * 0.55 * Math.Abs(s) * profile;          // margin curls up
                double rib = halfWidth * 0.30 * (1.0 - Math.Abs(s)) * profile;  // raised midrib
                double twist = halfWidth * .30 * Math.Sin(angle * 2.7) * s * bladeT * bladeT;
                pos[r, lane] = crown + dir * (length * t) + side * (width * s
                    + length * .08 * Math.Sin(angle) * t * t) + Vec3.Up * (arch + cup + rib + twist);
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
        if (changed)
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
