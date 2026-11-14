using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr;
using SAabb = Vivarium.Sim.Repr.Aabb;
using SVec2 = Vivarium.Sim.Core.Vec2;
using SVec3 = Vivarium.Sim.Core.Vec3;

namespace Vivarium.Repr;

/// <summary>
/// Renders a <see cref="LayeredSubstrate"/> as cached tiles (S2). Per tile three detail levels (mesher steps
/// 0.01 / 0.03 / 0.10 m) are built on background tasks, committed on the main thread (at most one commit per
/// frame) and KEPT; camera motion only toggles which prebuilt level is visible (hysteresis on projected step size).
/// <para>Invalidation: DirtyLog records (own cursor). Geometry|SoilMaterial rebuild only intersecting tiles; the old
/// meshes stay visible until the replacement commits. Water records rebuild nothing: ground/rock wetness comes from
/// a per-column RGBAH texture (R top-layer saturation, G film mm, B standing mm, uploaded only when a Water record
/// arrives); cut-face wetness is baked in CUSTOM0 and refreshed with SubstrateMesher.RefreshWater at most once
/// per <see cref="WallRefreshSeconds"/> per tile.</para>
/// <para>Thread rule: builds read the substrate on worker threads, so the owner must not mutate the substrate (Dig,
/// Water.Step) while builds are in flight: call <see cref="Drain"/> first, or run mutations from the same place that
/// drives frames and accept that results from before the change are discarded by generation.</para>
/// </summary>
public sealed partial class SubstrateRenderer : Node3D
{
    private const int Parts = 5; // Ground, CutFaces, Rock, Stones, Litter == shader kind 0..4

    public double TileSize = 0.5;
    public readonly double[] Steps = { 0.01, 0.03, 0.10 };
    /// <summary>Coarsest level is chosen whose vertex step projects to at most this many pixels.</summary>
    public double MaxStepPixels = 9.0;
    public double Hysteresis = 1.35;
    public double WallRefreshSeconds = 2.0;
    public bool PrebuildAll = true;
    public int MaxParallelBuilds = 2;
    public bool SyncMode;               // build + commit inline (deterministic captures)
    public Func<Camera3D?>? CameraProvider;

    // ---- counters
    public long TilesBuilt;             // (tile, level) mesh sets committed for the first time
    public long TilesRebuilt;           // mesh sets committed as replacements after Geometry/SoilMaterial records
    public long RebuildRequests;        // tiles scheduled for rebuild by geometry records (cumulative)
    public long MeshBytes;              // CPU-side mesh bytes held by committed levels (88 B/vertex + 4 B/index)
    public long WaterTextureUploads;
    public long WallRefreshes;
    public double LastSelectMicroseconds;
    public double LastBuildMs, TotalBuildMs, MaxCommitMs;
    public long LastBuildTriangles;
    public int Pending => _inflight + _commits.Count;
    public string CountersLine() =>
        $"built={TilesBuilt} rebuilt={TilesRebuilt} rebuildReq={RebuildRequests} meshMB={MeshBytes / 1048576.0:0.00} waterUploads={WaterTextureUploads} wallRefresh={WallRefreshes} selectUs={LastSelectMicroseconds:0} buildMsLast={LastBuildMs:0.0} buildMsTotal={TotalBuildMs:0} commitMsMax={MaxCommitMs:0.0}";

    private sealed class Packed
    {
        public Vector3[] V = Array.Empty<Vector3>(), N = Array.Empty<Vector3>();
        public Color[] C = Array.Empty<Color>();
        public Vector2[] UV = Array.Empty<Vector2>(), UV2 = Array.Empty<Vector2>();
        public float[] C0 = Array.Empty<float>(), C1 = Array.Empty<float>();
        public int[] I = Array.Empty<int>();
        public long Bytes => 88L * V.Length + 4L * I.Length;
    }

    private sealed class Level
    {
        public Node3D Holder = null!;
        public readonly MeshInstance3D?[] Inst = new MeshInstance3D?[Parts];
        public readonly long[] Bytes = new long[Parts];
        public MeshData? Cut;
        public int Tris;
        public int Stamp;               // tile WaterStamp at last wall bake
    }

    private sealed class Tile
    {
        public int Id;
        public SAabb Box;
        public Vector3 Min, Max;
        public readonly Level?[] Lv = new Level?[3];
        public readonly bool[] Building = new bool[3];
        public readonly bool[] EverBuilt = new bool[3];
        public int Shown = -1;
        public int Gen;
        public int WaterStamp;
        public bool RefreshBusy;
        public double LastRefresh = -1e9;
    }

    private sealed class Built
    {
        public Tile T = null!;
        public int Lv, Gen, Stamp;
        public bool WaterRefresh;
        public readonly Packed?[] P = new Packed?[Parts];
        public MeshData? Cut;
        public double Ms;
        public int Tris;
    }

    private LayeredSubstrate _s = null!;
    private readonly List<Tile> _tiles = new();
    private readonly ConcurrentQueue<Built> _commits = new();
    private int _inflight;
    private long _cursor;
    private readonly List<DirtyRecord> _recs = new();
    private bool _waterDirty;
    private Image _waterImg = null!;
    private ImageTexture _waterTex = null!;
    private int _ncx, _ncz;
    private double _ox, _oz, _cell;
    private readonly ShaderMaterial[] _mats = new ShaderMaterial[Parts];
    private float _exaggerate;
    private bool _active = true;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public LayeredSubstrate Substrate => _s;
    public IReadOnlyList<ShaderMaterial> Materials => _mats;

    public float Exaggerate
    {
        get => _exaggerate;
        set { _exaggerate = value; foreach (var m in _mats) m.SetShaderParameter("exaggerate", value); }
    }

    public bool Active
    {
        get => _active;
        set { _active = value; Visible = value; SetProcess(value); }
    }

    public void Init(LayeredSubstrate s, double tileSize = 0.5)
    {
        _s = s;
        TileSize = tileSize;
        var b = s.Bounds;
        _ox = b.Min.X; _oz = b.Min.Z;
        double w = b.Max.X - b.Min.X, d = b.Max.Z - b.Min.Z;
        int idxTop = s.ColumnAt(new SVec2(_ox + 1e-6, _oz + d - 1e-6));
        _ncz = idxTop + 1;
        int idxRight = s.ColumnAt(new SVec2(_ox + w - 1e-6, _oz + 1e-6));
        _ncx = idxRight / _ncz + 1;
        _cell = w / _ncx;

        BuildMaterials();
        _waterImg = Image.CreateEmpty(_ncx, _ncz, false, Image.Format.Rgbah);
        _waterTex = ImageTexture.CreateFromImage(_waterImg);
        foreach (var m in _mats) m.SetShaderParameter("water_tex", _waterTex);
        UploadWater();
        _cursor = s.DirtyLog.Head;

        int id = 0;
        foreach (var box in SubstrateMesher.Tiles(s, tileSize))
        {
            _tiles.Add(new Tile
            {
                Id = id++, Box = box,
                Min = new Vector3((float)box.Min.X, (float)box.Min.Y, (float)box.Min.Z),
                Max = new Vector3((float)box.Max.X, (float)box.Max.Y, (float)box.Max.Z),
            });
        }
    }

    // ------------------------------------------------------------------ materials

    private static Vector3 MeanColour(string path)
    {
        try
        {
            var tex = GD.Load<Texture2D>(path);
            var img = tex.GetImage();
            if (img.IsCompressed()) img.Decompress();
            img.Resize(8, 8, Image.Interpolation.Bilinear);
            double r = 0, g = 0, b = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var c = img.GetPixel(x, y).SrgbToLinear();
                    r += c.R; g += c.G; b += c.B;
                }
            return new Vector3((float)(r / 64), (float)(g / 64), (float)(b / 64)).Clamp(new Vector3(0.03f, 0.03f, 0.03f), Vector3.One);
        }
        catch (Exception ex)
        {
            GD.Print("REPR substrate: mean colour fallback for " + path + ": " + ex.Message);
            return new Vector3(0.5f, 0.5f, 0.5f);
        }
    }

    private void BuildMaterials()
    {
        var shader = GD.Load<Shader>("res://Shaders/repr_substrate.gdshader");
        var sets = new (string Name, string Asset)[]
        {
            ("soil", "Ground048_1K-JPG"), ("gravel", "Gravel022_1K-JPG"), ("rock", "Rock058_1K-JPG"), ("litter", "ScatteredLeaves007_1K-JPG"),
        };
        var texCol = new Texture2D[4]; var texNrm = new Texture2D[4]; var means = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            texCol[i] = GD.Load<Texture2D>($"res://Textures/{sets[i].Asset}_Color.jpg");
            texNrm[i] = GD.Load<Texture2D>($"res://Textures/{sets[i].Asset}_NormalGL.jpg");
            means[i] = MeanColour($"res://Textures/{sets[i].Asset}_Color.jpg");
        }
        var bed = _s.SampleSubstrate(new SVec3(_ox + 0.5 * (_ncx * _cell), _s.Bounds.Min.Y + 0.05, _oz + 0.5 * (_ncz * _cell))).BeddingNormal;
        for (int k = 0; k < Parts; k++)
        {
            var m = new ShaderMaterial { Shader = shader };
            m.SetShaderParameter("kind", (float)k);
            m.SetShaderParameter("water_enabled", k != 1 ? 1f : 0f);
            m.SetShaderParameter("water_rect", new Vector4((float)_ox, (float)_oz, (float)(1.0 / (_ncx * _cell)), (float)(1.0 / (_ncz * _cell))));
            m.SetShaderParameter("bed_normal", new Vector3((float)bed.X, (float)bed.Y, (float)bed.Z));
            for (int i = 0; i < 4; i++)
            {
                m.SetShaderParameter(sets[i].Name + "_col", texCol[i]);
                m.SetShaderParameter(sets[i].Name + "_nrm", texNrm[i]);
                m.SetShaderParameter(sets[i].Name + "_mean", means[i]);
            }
            m.SetShaderParameter("exaggerate", _exaggerate);
            m.SetShaderParameter("debug_mode", System.Environment.GetEnvironmentVariable("VIVARIUM_SUBSTRATE_DEBUG") == "1" ? 1f : 0f);
            _mats[k] = m;
        }
    }

    // ------------------------------------------------------------------ water texture

    private void UploadWater()
    {
        var s = _s;
        for (int c = 0; c < _ncx * _ncz; c++)
        {
            int i = c / _ncz, j = c % _ncz;
            var ctr = s.ColumnCentre(c);
            double h = s.Height(ctr);
            float sat = (float)s.Water.TopSaturation(c), film = 0, stand = 0;
            if (s.SurfaceSample(new SVec3(ctr.X, h, ctr.Z), 0.01, out var ss))
            {
                film = ss.FilmWater * 1000f;
                stand = ss.StandingWater * 1000f;
            }
            _waterImg.SetPixel(i, j, new Color(sat, film, stand, 1f));
        }
        _waterTex.Update(_waterImg);
        WaterTextureUploads++;
        _waterDirty = false;
    }

    // ------------------------------------------------------------------ dirty log

    private static bool OverlapXZ(Tile t, in SAabb b) =>
        b.Min.X <= t.Box.Max.X && b.Max.X >= t.Box.Min.X && b.Min.Z <= t.Box.Max.Z && b.Max.Z >= t.Box.Min.Z;

    /// <summary>Consumes new DirtyLog records. Cheap when there are none.</summary>
    public void PollDirty()
    {
        if (_s.DirtyLog.Head == _cursor) return;
        _recs.Clear();
        _cursor = _s.DirtyLog.Read(_cursor, DirtyDomain.Geometry | DirtyDomain.SoilMaterial | DirtyDomain.Water, _recs);
        foreach (var r in _recs)
        {
            bool geo = (r.Domain & (DirtyDomain.Geometry | DirtyDomain.SoilMaterial)) != 0;
            bool water = (r.Domain & DirtyDomain.Water) != 0;
            if (water) _waterDirty = true;
            foreach (var t in _tiles)
            {
                if (!OverlapXZ(t, r.Bounds)) continue;
                if (water) t.WaterStamp++;
                if (!geo) continue;
                t.Gen++;
                RebuildRequests++;
                for (int l = 0; l < 3; l++)
                {
                    var lv = t.Lv[l];
                    if (lv == null) continue;
                    if (l == t.Shown) { if (!t.Building[l]) Request(t, l, true); }
                    else FreeLevel(t, l);
                }
            }
        }
    }

    private void FreeLevel(Tile t, int l)
    {
        var lv = t.Lv[l];
        if (lv == null) return;
        for (int p = 0; p < Parts; p++) MeshBytes -= lv.Bytes[p];
        lv.Holder.Visible = false;
        lv.Holder.QueueFree();
        t.Lv[l] = null;
        if (t.Shown == l) t.Shown = -1;
    }

    // ------------------------------------------------------------------ building

    private Packed? PackMesh(MeshData m)
    {
        int n = m.VertexCount;
        if (n == 0 || m.Indices.Count == 0) return null;
        var p = new Packed
        {
            V = new Vector3[n], N = new Vector3[n], C = new Color[n], UV = new Vector2[n], UV2 = new Vector2[n],
            C0 = new float[4 * n], C1 = new float[4 * n], I = m.Indices.ToArray(),
        };
        bool c0 = m.Custom0.Count == 4 * n, c1 = m.Custom1.Count == 4 * n;
        for (int i = 0; i < n; i++)
        {
            p.V[i] = new Vector3(m.Positions[3 * i], m.Positions[3 * i + 1], m.Positions[3 * i + 2]);
            p.N[i] = new Vector3(m.Normals[3 * i], m.Normals[3 * i + 1], m.Normals[3 * i + 2]);
            p.C[i] = new Color(m.Colors[4 * i], m.Colors[4 * i + 1], m.Colors[4 * i + 2], m.Colors[4 * i + 3]);
            p.UV[i] = new Vector2(m.UV[2 * i], m.UV[2 * i + 1]);
            p.UV2[i] = new Vector2(m.UV2[2 * i], m.UV2[2 * i + 1]);
            if (c0) for (int k = 0; k < 4; k++) p.C0[4 * i + k] = m.Custom0[4 * i + k];
            if (c1) for (int k = 0; k < 4; k++) p.C1[4 * i + k] = m.Custom1[4 * i + k];
        }
        return p;
    }

    private Built BuildNow(Tile t, int lv, int gen)
    {
        var sw = Stopwatch.StartNew();
        var set = SubstrateMesher.Build(_s, t.Box, Steps[lv], true);
        var b = new Built { T = t, Lv = lv, Gen = gen, Stamp = t.WaterStamp };
        b.P[0] = PackMesh(set.Ground);
        b.P[1] = PackMesh(set.CutFaces);
        b.P[2] = PackMesh(set.Rock);
        b.P[3] = PackMesh(set.Stones);
        b.P[4] = PackMesh(set.Litter);
        b.Cut = set.CutFaces.VertexCount > 0 ? set.CutFaces : null;
        b.Tris = set.Triangles;
        b.Ms = sw.Elapsed.TotalMilliseconds;
        return b;
    }

    private Built RefreshNow(Tile t, int lv, int gen, MeshData cut)
    {
        var sw = Stopwatch.StartNew();
        SubstrateMesher.RefreshWater(_s, cut);
        var b = new Built { T = t, Lv = lv, Gen = gen, Stamp = t.WaterStamp, WaterRefresh = true, Cut = cut };
        b.P[1] = PackMesh(cut);
        b.Ms = sw.Elapsed.TotalMilliseconds;
        return b;
    }

    private void Request(Tile t, int lv, bool rebuild)
    {
        if (t.Building[lv]) return;
        t.Building[lv] = true;
        int gen = t.Gen;
        if (SyncMode)
        {
            var b = BuildNow(t, lv, gen);
            Commit(b);
            return;
        }
        Interlocked_Inc(ref _inflight, 1);
        Task.Run(() =>
        {
            try { _commits.Enqueue(BuildNow(t, lv, gen)); }
            catch (Exception ex) { GD.PushError("SubstrateRenderer build failed: " + ex); _commits.Enqueue(new Built { T = t, Lv = lv, Gen = -1 }); }
            finally { Interlocked_Inc(ref _inflight, -1); }
        });
    }

    private void RequestRefresh(Tile t, int lv)
    {
        var lvl = t.Lv[lv];
        if (lvl?.Cut == null || t.RefreshBusy) return;
        t.RefreshBusy = true;
        t.LastRefresh = _clock.Elapsed.TotalSeconds;
        int gen = t.Gen;
        var cut = lvl.Cut;
        if (SyncMode) { Commit(RefreshNow(t, lv, gen, cut)); return; }
        Interlocked_Inc(ref _inflight, 1);
        Task.Run(() =>
        {
            try { _commits.Enqueue(RefreshNow(t, lv, gen, cut)); }
            catch (Exception ex) { GD.PushError("SubstrateRenderer refresh failed: " + ex); _commits.Enqueue(new Built { T = t, Lv = lv, Gen = -1, WaterRefresh = true }); }
            finally { Interlocked_Inc(ref _inflight, -1); }
        });
    }

    private static void Interlocked_Inc(ref int v, int d) => System.Threading.Interlocked.Add(ref v, d);

    private static Mesh.ArrayFormat CustomFlags(bool c0, bool c1)
    {
        Mesh.ArrayFormat f = 0;
        if (c0) f |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        if (c1) f |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        return f;
    }

    private ArrayMesh MakeMesh(Packed p)
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = p.V;
        arrays[(int)Mesh.ArrayType.Normal] = p.N;
        arrays[(int)Mesh.ArrayType.Color] = p.C;
        arrays[(int)Mesh.ArrayType.TexUV] = p.UV;
        arrays[(int)Mesh.ArrayType.TexUV2] = p.UV2;
        arrays[(int)Mesh.ArrayType.Custom0] = p.C0;
        arrays[(int)Mesh.ArrayType.Custom1] = p.C1;
        arrays[(int)Mesh.ArrayType.Index] = p.I;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: CustomFlags(true, true));
        return mesh;
    }

    private void Commit(Built b)
    {
        var sw = Stopwatch.StartNew();
        var t = b.T;
        if (b.WaterRefresh)
        {
            t.RefreshBusy = false;
            var lvl = t.Lv[b.Lv];
            if (b.Gen != t.Gen || lvl == null || b.P[1] == null) return;
            var inst = lvl.Inst[1];
            if (inst == null) return;
            MeshBytes += b.P[1]!.Bytes - lvl.Bytes[1];
            lvl.Bytes[1] = b.P[1]!.Bytes;
            inst.Mesh = MakeMesh(b.P[1]!);
            lvl.Stamp = b.Stamp;
            WallRefreshes++;
            MaxCommitMs = Math.Max(MaxCommitMs, sw.Elapsed.TotalMilliseconds);
            return;
        }
        t.Building[b.Lv] = false;
        if (b.Gen < 0) return;                         // failed build
        if (b.Gen != t.Gen)                            // geometry changed while building: rebuild again
        {
            Request(t, b.Lv, true);
            return;
        }
        var old = t.Lv[b.Lv];
        var nl = new Level { Holder = new Node3D { Name = $"T{t.Id}_L{b.Lv}" }, Cut = b.Cut, Tris = b.Tris, Stamp = b.Stamp };
        for (int k = 0; k < Parts; k++)
        {
            var p = b.P[k];
            if (p == null) continue;
            var mi = new MeshInstance3D
            {
                Name = k switch { 0 => "Ground", 1 => "Cut", 2 => "Rock", 3 => "Stones", _ => "Litter" },
                Mesh = MakeMesh(p),
                MaterialOverride = _mats[k],
                CastShadow = k == 4 ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
            };
            nl.Holder.AddChild(mi);
            nl.Inst[k] = mi;
            nl.Bytes[k] = p.Bytes;
            MeshBytes += p.Bytes;
        }
        nl.Holder.Visible = old?.Holder.Visible ?? false;
        AddChild(nl.Holder);
        if (old != null)
        {
            for (int k = 0; k < Parts; k++) MeshBytes -= old.Bytes[k];
            old.Holder.Visible = false;
            old.Holder.QueueFree();
            TilesRebuilt++;
        }
        else if (!t.EverBuilt[b.Lv]) { TilesBuilt++; t.EverBuilt[b.Lv] = true; }
        else TilesRebuilt++;
        t.Lv[b.Lv] = nl;
        LastBuildMs = b.Ms; TotalBuildMs += b.Ms; LastBuildTriangles = b.Tris;
        MaxCommitMs = Math.Max(MaxCommitMs, sw.Elapsed.TotalMilliseconds);
    }

    // ------------------------------------------------------------------ selection

    private int Wanted(Tile t, double mpp)
    {
        int cur = t.Shown >= 0 ? t.Shown : -1;
        int raw = 0;
        for (int l = 0; l < 3; l++) if (Steps[l] / mpp <= MaxStepPixels) raw = l;
        if (cur < 0) return raw;
        int l2 = cur;
        while (l2 > 0 && Steps[l2] / mpp > MaxStepPixels * Hysteresis) l2--;
        while (l2 < 2 && Steps[l2 + 1] / mpp < MaxStepPixels / Hysteresis) l2++;
        return l2;
    }

    private static double DistToBox(Vector3 p, Vector3 mn, Vector3 mx)
    {
        float dx = Math.Max(Math.Max(mn.X - p.X, 0), p.X - mx.X);
        float dy = Math.Max(Math.Max(mn.Y - p.Y, 0), p.Y - mx.Y);
        float dz = Math.Max(Math.Max(mn.Z - p.Z, 0), p.Z - mx.Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Chooses levels for an eye position; shows built levels, requests missing ones. Returns tiles still waiting.</summary>
    public int Select(Vector3 eye, float fovDeg, float viewportHeightPx)
    {
        long t0 = Stopwatch.GetTimestamp();
        double tanHalf = Math.Tan(Mathf.DegToRad(fovDeg) * 0.5);
        int waiting = 0;
        foreach (var t in _tiles)
        {
            double dist = Math.Max(DistToBox(eye, t.Min, t.Max), 0.02);
            double mpp = 2.0 * dist * tanHalf / Math.Max(viewportHeightPx, 1f);
            int w = Wanted(t, mpp);
            int show = -1;
            if (t.Lv[w] != null) show = w;
            else
            {
                waiting++;
                Request(t, w, false);
                // best available stand-in: nearest built level
                int best = 99;
                for (int l = 0; l < 3; l++) if (t.Lv[l] != null && Math.Abs(l - w) < best) { best = Math.Abs(l - w); show = l; }
            }
            // sync-mode Request may have committed w already
            if (t.Lv[w] != null) show = w;
            if (show != t.Shown)
            {
                for (int l = 0; l < 3; l++) { var lv = t.Lv[l]; if (lv != null) lv.Holder.Visible = l == show; }
                t.Shown = show;
            }
        }
        if (PrebuildAll && waiting == 0 && _inflight < MaxParallelBuilds)
        {
            foreach (var t in _tiles)
            {
                bool launched = false;
                for (int l = 2; l >= 0 && !launched; l--)
                    if (t.Lv[l] == null && !t.Building[l]) { Request(t, l, false); launched = true; }
                if (launched && (_inflight >= MaxParallelBuilds)) break;
            }
        }
        LastSelectMicroseconds = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
        return waiting;
    }

    private void RefreshWalls(bool ignoreThrottle)
    {
        double now = _clock.Elapsed.TotalSeconds;
        foreach (var t in _tiles)
        {
            if (t.Shown < 0) continue;
            var lv = t.Lv[t.Shown];
            if (lv == null || lv.Cut == null || lv.Stamp >= t.WaterStamp) continue;
            if (!ignoreThrottle && now - t.LastRefresh < WallRefreshSeconds) continue;
            RequestRefresh(t, t.Shown);
        }
    }

    // ------------------------------------------------------------------ frame loop and sync helpers

    public override void _Process(double delta)
    {
        if (_s == null) return;
        PollDirty();
        if (_waterDirty) UploadWater();
        var cam = CameraProvider?.Invoke() ?? GetViewport().GetCamera3D();
        if (cam != null) Select(cam.GlobalPosition, cam.Fov, GetViewport().GetVisibleRect().Size.Y);
        if (_commits.TryDequeue(out var b)) Commit(b);   // max one commit per frame
        RefreshWalls(false);
    }

    /// <summary>Waits for every in-flight task and commits every finished result (ignores the one-per-frame cap).</summary>
    public void Drain()
    {
        var sw = Stopwatch.StartNew();
        while (_inflight > 0 || !_commits.IsEmpty)
        {
            while (_commits.TryDequeue(out var b)) Commit(b);
            if (_inflight > 0) System.Threading.Thread.Sleep(1);
            if (sw.Elapsed.TotalSeconds > 60) { GD.PushError("SubstrateRenderer.Drain timeout"); break; }
        }
    }

    /// <summary>
    /// Deterministic settle for captures: consumes dirty records, uploads water, selects levels for the given
    /// eye and builds + commits everything missing for it (and refreshes walls), blocking.
    /// </summary>
    public void SettleFor(Vector3 eye, float fovDeg, float viewportHeightPx)
    {
        Drain();
        bool prev = SyncMode, prevPre = PrebuildAll;
        SyncMode = true; PrebuildAll = false;
        PollDirty();
        Drain();
        if (_waterDirty) UploadWater();
        for (int i = 0; i < 4; i++)
        {
            Select(eye, fovDeg, viewportHeightPx);
            Drain();
        }
        RefreshWalls(true);
        Drain();
        SyncMode = prev; PrebuildAll = prevPre;
    }

    /// <summary>Builds every level of every tile now (blocking). Used to measure first-build cost.</summary>
    public void BuildAllNow()
    {
        Drain();
        bool prev = SyncMode; SyncMode = true;
        foreach (var t in _tiles) for (int l = 0; l < 3; l++) if (t.Lv[l] == null) Request(t, l, false);
        SyncMode = prev;
    }

    public int TileCount => _tiles.Count;
    public int ShownTriangles()
    {
        int n = 0;
        foreach (var t in _tiles) if (t.Shown >= 0 && t.Lv[t.Shown] is { } lv) n += lv.Tris;
        return n;
    }
    public string ShownLevels()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in _tiles) sb.Append(t.Shown).Append(' ');
        return sb.ToString().Trim();
    }
}
