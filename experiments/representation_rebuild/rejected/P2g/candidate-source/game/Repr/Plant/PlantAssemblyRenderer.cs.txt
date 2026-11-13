using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;
using GAabb = Godot.Aabb;

namespace Vivarium.Repr;

/// <summary>Mesh size accounting shared by the renderer and the demos.</summary>
public static class PlantMeshStats
{
    /// <summary>Vertex + attribute + index bytes of every surface, from the surface format strides.</summary>
    public static long MeshBytes(Mesh? m)
    {
        if (m is not ArrayMesh mesh) return 0;
        long total = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var fmt = (RenderingServer.ArrayFormat)(long)mesh.SurfaceGetFormat(s);
            int vc = mesh.SurfaceGetArrayLen(s);
            int ic = mesh.SurfaceGetArrayIndexLen(s);
            long perVertex = (long)RenderingServer.MeshSurfaceGetFormatVertexStride(fmt, vc)
                + RenderingServer.MeshSurfaceGetFormatNormalTangentStride(fmt, vc)
                + RenderingServer.MeshSurfaceGetFormatAttributeStride(fmt, vc)
                + RenderingServer.MeshSurfaceGetFormatSkinStride(fmt, vc);
            total += perVertex * vc + (long)ic * (vc > 65535 ? 4 : 2);
        }
        return total;
    }
}

/// <summary>
/// CACHE_ASSEMBLY plant renderer. Organ shapes (leaf, leaflet, flower, fruit) are shared ArrayMeshes drawn as
/// MultiMesh instances; wood is merged per (chunk, species, detail). Space is cut into a 2 m XZ grid; every chunk
/// owns prebuilt per-detail node groups with a CustomAabb so Godot frustum-culls per chunk. Camera motion only
/// toggles Visible on those groups: no mesh build and no buffer refill. Detail levels are built lazily on a
/// background task (data) and committed on the main thread (at most <see cref="CommitsPerFrame"/> per frame), then kept.
///
/// Instance buffer (20 floats = 12 transform + 4 colour + 4 custom, same as the legacy flora):
///   transform: plant world transform (Position, Yaw about Y, uniform Scale) x OrganInstance matrix, in chunk-local space
///   COLOR     = (1,1,1,1): the baked vertex colours carry species colour (shader multiplies)
///   CUSTOM    = (x: per-organ tint variation 0..1 from Seed low bits [flora: tint, leaf: plant.x],
///                y: Health 0..1 [flora: inst.y, leaf: plant.y],
///                z: Maturity 0..1 [unused by both shaders],
///                w: per-organ seed 0..1 from Seed high bits [flora: inst.w, leaf: plant.w; drives bark phase,
///                   leaf layer pick, mottling, sway phase])
/// Leaf shapes additionally carry per-vertex CUSTOM0 = (0.5, 0, 0.5, 0.5) and CUSTOM1 = (0,0,0,0.05) because
/// leaf.gdshader reads blade data from them (per-instance variation comes from the instance CUSTOM instead).
/// Wood meshes are not instanced: the shader falls back to its neutral inst = (0.5, 1, 0.5, 0.35).
/// </summary>
public sealed partial class PlantAssemblyRenderer : Node3D
{
    public const float CellSize = 2f;

    /// <summary>(speciesId, leafMaterial) -> game material. Leaf shapes use leaf:true, wood/flower/fruit leaf:false.</summary>
    public Func<string, bool, Material?>? MaterialProvider;

    // LOD tuning: pixels of projected chunk bounding sphere. finer needs px >= thr * HystUp, coarser needs px < thr * HystDown.
    public float Detail0Px = 700f, Detail1Px = 250f;
    public float HystUp = 1.2f, HystDown = 0.8f;
    public float MoveThreshold = 0.25f, RotationThresholdDeg = 2f;
    public int CommitsPerFrame = 1;
    public int MaxParallelBuilds = 2;
    public bool CastShadows = true;

    // ---- counters
    public int ChunkCount => _chunks.Count;
    public long ChunkRebuilds { get; private set; }
    public long InstanceBytesUploaded { get; private set; }
    public long MeshBytes => _sharedMeshBytes + _levelMeshBytes;
    public double LastSelectMicroseconds { get; private set; }
    public double MaxSelectMicroseconds { get; private set; }
    public long SelectCount { get; private set; }
    public int PendingBuilds => _inflight + _requests.Count + _done.Count;
    public int[] ShownDetailCounts()
    {
        var r = new int[3];
        foreach (var c in _chunks.Values) if (c.Shown >= 0) r[c.Shown]++;
        return r;
    }

    private sealed class Level
    {
        public int BuiltGen = -1;
        public int BuildingGen = -1;
        public bool Queued;
        public int FailedGen = -1;
        public Node3D? Holder;
        public long Bytes;
    }

    private sealed class Chunk
    {
        public int Cx, Cz;
        public Vector3 Origin;
        public readonly List<PlantDescriptor> Plants = new();
        public int Gen;
        public readonly Level[] Levels = { new(), new(), new() };
        public int Shown = -1, Wanted = -1;
        public Vector3 Center;
        public float Radius;
        public Node3D? Root;
    }

    private sealed class GroupResult
    {
        public string Species = "";
        public ushort ShapeId;
        public OrganType ShapeType;
        public float[] Buffer = Array.Empty<float>();
        public int Count;
        public Vector3 Min, Max;
    }

    private sealed class LevelResult
    {
        public Chunk Chunk = null!;
        public int Detail, Gen;
        public Dictionary<string, MeshData> Wood = new();
        public List<GroupResult> Groups = new();
        public Exception? Error;
        public double Ms;
    }

    private sealed class ShapeData
    {
        public MeshData Mesh = null!;
        public Vector3 Min, Max;
        public OrganType Type;
    }

    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    private readonly Dictionary<ulong, (int, int)> _plantCell = new();
    private readonly ConcurrentDictionary<(string, ushort, int), ShapeData> _shapeData = new();
    private readonly Dictionary<(string, ushort, int), ArrayMesh> _shapeMeshes = new();
    private readonly Dictionary<(string, bool), Material?> _materials = new();
    private readonly ConcurrentQueue<LevelResult> _done = new();
    private readonly List<(Chunk Chunk, int Detail)> _requests = new();
    private readonly List<Task> _running = new();
    private int _inflight;
    private long _sharedMeshBytes, _levelMeshBytes;

    private bool _forceSelect = true, _wasHidden;
    private Vector3 _lastCamPos, _lastCamFwd;
    private float _lastFov, _lastViewH;
    private bool _haveLast;

    // ------------------------------------------------------------------ public API

    public void SetPlants(IReadOnlyList<PlantDescriptor> plants)
    {
        foreach (var c in _chunks.Values) FreeChunk(c);
        _chunks.Clear();
        _plantCell.Clear();
        _requests.Clear();
        foreach (var p in plants) AddPlant(Snapshot(p));
        foreach (var c in _chunks.Values) { c.Plants.Sort((a, b) => a.PlantId.CompareTo(b.PlantId)); RecomputeBounds(c); }
        _forceSelect = true;
    }

    /// <summary>Call after the plant grew (descriptor mutated in place) or moved. Only that plant's chunk is rebuilt.</summary>
    public void UpdatePlant(PlantDescriptor p)
    {
        var snap = Snapshot(p);
        if (_plantCell.TryGetValue(snap.PlantId, out var cell))
        {
            var c = _chunks[cell];
            if (cell == CellOf(snap.Position))
            {
                int i = c.Plants.FindIndex(q => q.PlantId == snap.PlantId);
                c.Plants[i] = snap;
                Dirty(c);
                _forceSelect = true;
                return;
            }
            RemoveFromChunk(c, snap.PlantId);
        }
        AddPlant(snap);
        if (_chunks.TryGetValue(CellOf(snap.Position), out var nc)) { nc.Plants.Sort((a, b) => a.PlantId.CompareTo(b.PlantId)); Dirty(nc); }
        _forceSelect = true;
    }

    public void Remove(ulong plantId)
    {
        if (!_plantCell.TryGetValue(plantId, out var cell)) return;
        RemoveFromChunk(_chunks[cell], plantId);
        _forceSelect = true;
    }

    /// <summary>Harness/test only: select for the current camera, then build and commit everything pending, blocking.</summary>
    public void Drain()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) Select(cam);
        while (true)
        {
            Pump(int.MaxValue);
            if (_inflight == 0 && _requests.Count == 0 && _done.IsEmpty) break;
            Task[] t;
            lock (_running) t = _running.Where(x => !x.IsCompleted).ToArray();
            if (t.Length > 0) Task.WaitAny(t);
        }
    }

    // ------------------------------------------------------------------ bookkeeping

    private static PlantDescriptor Snapshot(PlantDescriptor p) => new()
    {
        PlantId = p.PlantId, WorldSeed = p.WorldSeed, SpeciesId = p.SpeciesId,
        StructureVersion = p.StructureVersion, GrowthVersion = p.GrowthVersion,
        Position = p.Position, Yaw = p.Yaw, Scale = p.Scale,
        Organs = (Organ[])p.Organs.Clone(), Shapes = p.Shapes, LocalBounds = p.LocalBounds,
        LeafArea = p.LeafArea, WoodVolume = p.WoodVolume, Height = p.Height, CrownRadius = p.CrownRadius,
    };

    private static (int, int) CellOf(Vec3 p) => ((int)Math.Floor(p.X / CellSize), (int)Math.Floor(p.Z / CellSize));

    private void AddPlant(PlantDescriptor snap)
    {
        var cell = CellOf(snap.Position);
        if (!_chunks.TryGetValue(cell, out var c))
        {
            c = new Chunk { Cx = cell.Item1, Cz = cell.Item2, Origin = new Vector3((cell.Item1 + 0.5f) * CellSize, 0, (cell.Item2 + 0.5f) * CellSize) };
            c.Root = new Node3D { Name = $"Chunk_{c.Cx}_{c.Cz}", Position = c.Origin };
            AddChild(c.Root);
            _chunks[cell] = c;
        }
        c.Plants.Add(snap);
        _plantCell[snap.PlantId] = cell;
    }

    private void RemoveFromChunk(Chunk c, ulong id)
    {
        c.Plants.RemoveAll(q => q.PlantId == id);
        _plantCell.Remove(id);
        if (c.Plants.Count == 0)
        {
            FreeChunk(c);
            _chunks.Remove((c.Cx, c.Cz));
            _requests.RemoveAll(r => r.Chunk == c);
        }
        else Dirty(c);
    }

    private void Dirty(Chunk c)
    {
        c.Gen++;
        RecomputeBounds(c);
    }

    private void FreeChunk(Chunk c)
    {
        foreach (var l in c.Levels) FreeLevel(l);
        c.Root?.QueueFree();
        c.Root = null;
        c.Gen++;   // invalidates results in flight
    }

    private void FreeLevel(Level l)
    {
        if (l.Holder != null) { l.Holder.QueueFree(); l.Holder = null; }
        _levelMeshBytes -= l.Bytes;
        l.Bytes = 0;
        l.BuiltGen = -1;
    }

    /// <summary>World-space bounding sphere from organ end points (cheap, detail independent).</summary>
    private static void RecomputeBounds(Chunk c)
    {
        var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var p in c.Plants)
        {
            double cs = Math.Cos(p.Yaw), sn = Math.Sin(p.Yaw), k = p.Scale;
            foreach (var o in p.Organs)
            {
                double r = Math.Max(Math.Max(o.RadiusStart, o.RadiusEnd), 0.0) + 0.002;
                foreach (var q in new[] { o.Start, o.End })
                {
                    double x = k * (cs * q.X + sn * q.Z) + p.Position.X;
                    double y = k * q.Y + p.Position.Y;
                    double z = k * (-sn * q.X + cs * q.Z) + p.Position.Z;
                    double rr = r * k;
                    mn = new Vector3(Math.Min(mn.X, (float)(x - rr)), Math.Min(mn.Y, (float)(y - rr)), Math.Min(mn.Z, (float)(z - rr)));
                    mx = new Vector3(Math.Max(mx.X, (float)(x + rr)), Math.Max(mx.Y, (float)(y + rr)), Math.Max(mx.Z, (float)(z + rr)));
                }
            }
        }
        if (mn.X > mx.X) { mn = mx = c.Origin; }
        c.Center = (mn + mx) * 0.5f;
        c.Radius = ((mx - mn) * 0.5f).Length();
    }

    // ------------------------------------------------------------------ per frame

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) { _wasHidden = true; return; }
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var fwd = -cam.GlobalTransform.Basis.Z;
        float vh = GetViewport().GetVisibleRect().Size.Y;
        bool need = _forceSelect || _wasHidden || !_haveLast
            || (cam.GlobalPosition - _lastCamPos).Length() > MoveThreshold
            || Mathf.RadToDeg(fwd.AngleTo(_lastCamFwd)) > RotationThresholdDeg
            || Math.Abs(cam.Fov - _lastFov) > 0.01f || Math.Abs(vh - _lastViewH) > 0.5f;
        if (need) Select(cam);
        Pump(CommitsPerFrame);
    }

    private float Threshold(int level) => level == 0 ? Detail0Px : Detail1Px;

    /// <summary>O(chunks). Chooses a detail per chunk with hysteresis and toggles Visible on prebuilt groups.</summary>
    private void Select(Camera3D cam)
    {
        long t0 = Stopwatch.GetTimestamp();
        _forceSelect = false; _wasHidden = false; _haveLast = true;
        var camPos = cam.GlobalPosition;
        var fwd = -cam.GlobalTransform.Basis.Z;
        float vh = GetViewport().GetVisibleRect().Size.Y;
        _lastCamPos = camPos; _lastCamFwd = fwd; _lastFov = cam.Fov; _lastViewH = vh;
        float k = vh / (2f * Mathf.Tan(Mathf.DegToRad(cam.Fov) * 0.5f));
        foreach (var c in _chunks.Values)
        {
            var d =c.Center - camPos;
            float dist = Math.Max(d.Length(), 0.01f);
            float px = c.Radius * k / dist;
            bool behind = d.Dot(fwd) < -c.Radius;
            int raw = px >= Detail0Px ? 0 : px >= Detail1Px ? 1 : 2;
            if (c.Wanted < 0) c.Wanted = raw;
            else
            {
                while (c.Wanted > 0 && px >= Threshold(c.Wanted - 1) * HystUp) c.Wanted--;
                while (c.Wanted < 2 && px < Threshold(c.Wanted) * HystDown) c.Wanted++;
            }
            var lvl = c.Levels[c.Wanted];
            if (!behind && lvl.BuiltGen != c.Gen && lvl.BuildingGen != c.Gen && !lvl.Queued && lvl.FailedGen != c.Gen)
            {
                lvl.Queued = true;
                _requests.Add((c, c.Wanted));
            }
            ShowBest(c);
        }
        _requests.Sort((a, b) =>
        {
            int r = (a.Chunk.Center - camPos).LengthSquared().CompareTo((b.Chunk.Center - camPos).LengthSquared());
            return r != 0 ? r : a.Chunk.Cx != b.Chunk.Cx ? a.Chunk.Cx.CompareTo(b.Chunk.Cx) : a.Chunk.Cz.CompareTo(b.Chunk.Cz);
        });
        double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;
        LastSelectMicroseconds = us;
        MaxSelectMicroseconds = Math.Max(MaxSelectMicroseconds, us);
        SelectCount++;
    }

    private void ShowBest(Chunk c)
    {
        int show = -1;
        if (c.Levels[c.Wanted].BuiltGen == c.Gen) show = c.Wanted;
        else
        {
            // nearest fresh level, else nearest stale one (old geometry stays until the replacement commits)
            for (int pass = 0; pass < 2 && show < 0; pass++)
                for (int dlt = 1; dlt <= 2 && show < 0; dlt++)
                    foreach (int cand in new[] { c.Wanted - dlt, c.Wanted + dlt })
                        if (cand >= 0 && cand <= 2 && c.Levels[cand].Holder != null && (pass == 1 || c.Levels[cand].BuiltGen == c.Gen)) { show = cand; break; }
            if (show < 0 && c.Levels[c.Wanted].Holder != null) show = c.Wanted;
        }
        if (show == c.Shown) return;
        for (int i = 0; i < 3; i++)
            if (c.Levels[i].Holder != null) c.Levels[i].Holder!.Visible = i == show;
        c.Shown = show;
    }

    // ------------------------------------------------------------------ build / commit

    private void Pump(int maxCommits)
    {
        int commits = 0;
        while (commits < maxCommits && _done.TryDequeue(out var r))
        {
            Commit(r);
            commits++;
        }
        while (_inflight < MaxParallelBuilds && _requests.Count > 0)
        {
            var (c, d) = _requests[0];
            _requests.RemoveAt(0);
            var lvl = c.Levels[d];
            lvl.Queued = false;
            if (c.Root == null || !_chunks.ContainsKey((c.Cx, c.Cz))) continue;
            if (lvl.BuiltGen == c.Gen || lvl.BuildingGen == c.Gen) continue;
            if (c.Wanted != d) continue;   // no longer wanted
            lvl.BuildingGen = c.Gen;
            int gen = c.Gen;
            var plants = c.Plants.ToArray();
            var origin = c.Origin;
            _inflight++;
            var task = Task.Run(() => BuildLevel(c, d, gen, plants, origin)).ContinueWith(t =>
            {
                var res = t.IsFaulted ? new LevelResult { Chunk = c, Detail = d, Gen = gen, Error = t.Exception } : t.Result;
                _done.Enqueue(res);
            });
            lock (_running) { _running.RemoveAll(x => x.IsCompleted); _running.Add(task); }
        }
    }

    private LevelResult BuildLevel(Chunk chunk, int detail, int gen, PlantDescriptor[] plants, Vector3 origin)
    {
        var sw = Stopwatch.StartNew();
        var res = new LevelResult { Chunk = chunk, Detail = detail, Gen = gen };
        try
        {
            var groups = new Dictionary<(string, ushort), (List<float> Buf, int Count, Vector3 Min, Vector3 Max, OrganType Type)>();
            var shapeRefs = new Dictionary<(string, ushort), (ShapeData Sd0, OrganShape Shape)>();
            foreach (var p in plants)
            {
                double cs = Math.Cos(p.Yaw), sn = Math.Sin(p.Yaw), k = p.Scale;
                double tx = p.Position.X - origin.X, ty = p.Position.Y - origin.Y, tz = p.Position.Z - origin.Z;
                var asm = PlantCompiler.Compile(p, detail);

                if (asm.Wood.VertexCount > 0)
                {
                    if (!res.Wood.TryGetValue(p.SpeciesId, out var wood)) res.Wood[p.SpeciesId] = wood = new MeshData();
                    AppendWood(wood, asm.Wood, cs, sn, k, tx, ty, tz);
                }

                foreach (var f in asm.Instances)
                {
                    var key = (p.SpeciesId, f.ShapeId);
                    if (!shapeRefs.TryGetValue(key, out var sr)) shapeRefs[key] = sr = (GetShape(p.SpeciesId, f.ShapeId, p.Shapes[f.ShapeId], 0), p.Shapes[f.ShapeId]);
                    var sd = sr.Sd0;
                    if (!groups.TryGetValue(key, out var g))
                        g = (new List<float>(), 0, new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), new Vector3(float.MinValue, float.MinValue, float.MinValue), sd.Type);

                    // Final = [k*Ry | k*Ry*t + T] x organ matrix, Ry = [[c,0,s],[0,1,0],[-s,0,c]]
                    double a00 = f.M00, a01 = f.M01, a02 = f.M02, a03 = f.M03;
                    double a10 = f.M10, a11 = f.M11, a12 = f.M12, a13 = f.M13;
                    double a20 = f.M20, a21 = f.M21, a22 = f.M22, a23 = f.M23;
                    double[] m = new double[12];
                    m[0] = k * (cs * a00 + sn * a20); m[1] = k * (cs * a01 + sn * a21); m[2] = k * (cs * a02 + sn * a22); m[3] = k * (cs * a03 + sn * a23) + tx;
                    m[4] = k * a10; m[5] = k * a11; m[6] = k * a12; m[7] = k * a13 + ty;
                    m[8] = k * (-sn * a00 + cs * a20); m[9] = k * (-sn * a01 + cs * a21); m[10] = k * (-sn * a02 + cs * a22); m[11] = k * (-sn * a03 + cs * a23) + tz;
                    var buf = g.Buf;
                    for (int i = 0; i < 12; i++) buf.Add((float)m[i]);
                    buf.Add(1f); buf.Add(1f); buf.Add(1f); buf.Add(1f);
                    buf.Add((f.Seed & 0xFFFF) / 65535f);
                    buf.Add(Math.Clamp(f.Health, 0f, 1f));
                    buf.Add(Math.Clamp(f.Maturity, 0f, 1f));
                    buf.Add(((f.Seed >> 16) & 0xFFFF) / 65536f + 1f / 131072f);

                    var mn = g.Min; var mx = g.Max;
                    for (int cx = 0; cx < 2; cx++)
                        for (int cy = 0; cy < 2; cy++)
                            for (int cz = 0; cz < 2; cz++)
                            {
                                double x = cx == 0 ? sd.Min.X : sd.Max.X, y = cy == 0 ? sd.Min.Y : sd.Max.Y, z = cz == 0 ? sd.Min.Z : sd.Max.Z;
                                float wx = (float)(m[0] * x + m[1] * y + m[2] * z + m[3]);
                                float wy = (float)(m[4] * x + m[5] * y + m[6] * z + m[7]);
                                float wz = (float)(m[8] * x + m[9] * y + m[10] * z + m[11]);
                                mn = new Vector3(Math.Min(mn.X, wx), Math.Min(mn.Y, wy), Math.Min(mn.Z, wz));
                                mx = new Vector3(Math.Max(mx.X, wx), Math.Max(mx.Y, wy), Math.Max(mx.Z, wz));
                            }
                    groups[key] = (buf, g.Count + 1, mn, mx, g.Type);
                }
            }
            foreach (var wood in res.Wood.Values) Bridge.PrepareMeshUpload(wood);
            foreach (var kv in groups.OrderBy(x => x.Key.Item1, StringComparer.Ordinal).ThenBy(x => x.Key.Item2))
                res.Groups.Add(new GroupResult
                {
                    Species = kv.Key.Item1, ShapeId = kv.Key.Item2, ShapeType = kv.Value.Type,
                    Buffer = kv.Value.Buf.ToArray(), Count = kv.Value.Count, Min = kv.Value.Min, Max = kv.Value.Max,
                });
            // Prime shape data for the detail being built (main thread then only wraps it in an ArrayMesh).
            foreach (var kv in shapeRefs) GetShape(kv.Key.Item1, kv.Key.Item2, kv.Value.Shape, detail);
        }
        catch (Exception e) { res.Error = e; }
        res.Ms = sw.Elapsed.TotalMilliseconds;
        return res;
    }

    private static void AppendWood(MeshData dst, MeshData src, double cs, double sn, double k, double tx, double ty, double tz)
    {
        int b0 = dst.VertexCount;
        int n = src.VertexCount;
        for (int v = 0; v < n; v++)
        {
            double x = src.Positions[v * 3], y = src.Positions[v * 3 + 1], z = src.Positions[v * 3 + 2];
            double nx = src.Normals[v * 3], ny = src.Normals[v * 3 + 1], nz = src.Normals[v * 3 + 2];
            var p = new Vec3(k * (cs * x + sn * z) + tx, k * y + ty, k * (-sn * x + cs * z) + tz);
            var nn = new Vec3(cs * nx + sn * nz, ny, -sn * nx + cs * nz);
            dst.AddVertex(p, nn, src.Colors[v * 4], src.Colors[v * 4 + 1], src.Colors[v * 4 + 2], src.Colors[v * 4 + 3],
                src.UV[v * 2] * k, src.UV[v * 2 + 1], src.UV2[v * 2], src.UV2[v * 2 + 1]);
        }
        foreach (int i in src.Indices) dst.Indices.Add(b0 + i);
    }

    private ShapeData GetShape(string species, ushort shapeId, OrganShape shape, int detail)
        => _shapeData.GetOrAdd((species, shapeId, detail), _ =>
        {
            var md = PlantCompiler.BuildShapeMesh(shape, detail);
            bool lamina = shape.Type is OrganType.Leaf or OrganType.Leaflet;
            if (lamina)
            {
                for (int i = 0; i < md.VertexCount; i++)
                {
                    md.Custom0.Add(0.5f); md.Custom0.Add(0f); md.Custom0.Add(0.5f); md.Custom0.Add(0.5f);
                    md.Custom1.Add(0f); md.Custom1.Add(0f); md.Custom1.Add(0f); md.Custom1.Add(0.05f);
                }
            }
            var (mn, mx) = md.Bounds();
            Bridge.PrepareMeshUpload(md);
            const float pad = 0.01f;
            return new ShapeData
            {
                Mesh = md, Type = shape.Type,
                Min = new Vector3((float)mn.X - pad, (float)mn.Y - pad, (float)mn.Z - pad),
                Max = new Vector3((float)mx.X + pad, (float)mx.Y + pad, (float)mx.Z + pad),
            };
        });

    private Material? Mat(string species, bool leaf)
    {
        if (_materials.TryGetValue((species, leaf), out var m)) return m;
        m = MaterialProvider?.Invoke(species, leaf);
        _materials[(species, leaf)] = m;
        return m;
    }

    private void Commit(LevelResult r)
    {
        _inflight--;
        var c = r.Chunk;
        var lvl = c.Levels[r.Detail];
        if (lvl.BuildingGen == r.Gen) lvl.BuildingGen = -1;
        if (r.Error != null)
        {
            GD.PushError($"PlantAssemblyRenderer chunk {c.Cx},{c.Cz} detail {r.Detail}: {r.Error.Message}");
            lvl.FailedGen = r.Gen;
            return;
        }
        if (c.Root == null || r.Gen != c.Gen || !_chunks.ContainsKey((c.Cx, c.Cz)))
        {
            // Stale result (chunk changed or removed while building): drop; Select re-requests if still wanted.
            _forceSelect = true;
            return;
        }

        FreeLevel(lvl);
        var holder = new Node3D { Name = "D" + r.Detail, Visible = false };
        c.Root.AddChild(holder);
        long bytes = 0;
        var shadow = CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var (species, md) in r.Wood.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var mesh = Bridge.ToArrayMesh(md);
            holder.AddChild(new MeshInstance3D { Name = "Wood_" + species, Mesh = mesh, MaterialOverride = Mat(species, false), CastShadow = shadow });
            bytes += PlantMeshStats.MeshBytes(mesh);
        }
        foreach (var g in r.Groups)
        {
            var key = (g.Species, g.ShapeId, r.Detail);
            if (!_shapeMeshes.TryGetValue(key, out var shapeMesh))
            {
                var sd = _shapeData[key];
                shapeMesh = Bridge.ToArrayMesh(sd.Mesh);
                _shapeMeshes[key] = shapeMesh;
                _sharedMeshBytes += PlantMeshStats.MeshBytes(shapeMesh);
            }
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true,
                Mesh = shapeMesh, InstanceCount = g.Count,
            };
            mm.Buffer = g.Buffer;
            InstanceBytesUploaded += (long)g.Buffer.Length * sizeof(float);
            bool leaf = g.ShapeType is OrganType.Leaf or OrganType.Leaflet;
            holder.AddChild(new MultiMeshInstance3D
            {
                Name = $"Organs_{g.Species}_{g.ShapeId}", Multimesh = mm, MaterialOverride = Mat(g.Species, leaf),
                CustomAabb = new GAabb(g.Min, g.Max - g.Min), CastShadow = shadow,
            });
        }
        lvl.Holder = holder;
        if (c.Shown == r.Detail) c.Shown = -1;
        lvl.Bytes = bytes;
        lvl.BuiltGen = r.Gen;
        _levelMeshBytes += bytes;
        ChunkRebuilds++;
        ShowBest(c);
        // Once the wanted level is fresh, free stale levels that can never be shown again.
        if (c.Levels[c.Wanted].BuiltGen == c.Gen)
            for (int i = 0; i < 3; i++)
                if (c.Levels[i].Holder != null && c.Levels[i].BuiltGen != c.Gen) { FreeLevel(c.Levels[i]); }
        if (c.Shown >= 0 && c.Levels[c.Shown].Holder == null) c.Shown = -1;
        ShowBest(c);
    }

    public override void _ExitTree()
    {
        // Pending tasks hold no Godot nodes; results enqueued after exit are simply ignored.
    }
}
