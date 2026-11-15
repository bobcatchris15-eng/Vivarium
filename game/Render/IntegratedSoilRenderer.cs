using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Three batched, camera-dependent soil LODs. Simulation always uses the physical soil surface.</summary>
public partial class IntegratedSoilRenderer : Node3D
{
    private VivariumWorld? _world;
    private ShaderMaterial _material = null!;
    private readonly MeshInstance3D[] _instances = new MeshInstance3D[3];
    private readonly ArrayMesh[] _meshes = new ArrayMesh[3];
    private SoilTileMesher.Snapshot? _snapshot;
    private bool[]? _wet;
    private Task<Result>? _job;
    private Request? _activeRequest;
    private long _epoch;
    private bool _dirty;
    private bool _exited;
    private Vector3 _requestedCamera;
    private double _waterPoll;
    private readonly WorkerCache _cache = new();
    private GeometryKey? _committedPlan;
    public int CacheTileLimit { get; set; } = 128;
    public bool SurfaceReady { get; private set; }
    public int TriangleCount { get; private set; }
    public int TileCount { get; private set; }
    public int BuildsCompleted { get; private set; }
    public int TilesBuilt { get; private set; }
    public int CacheHits { get; private set; }
    public string? LastBuildError { get; private set; }
    public double LastBuildMilliseconds { get; private set; }
    public int StaleBuildsDiscarded { get; private set; }
    public long LastGroupPayloadBytes { get; private set; }
    public int UnchangedRequests { get; private set; }

    private sealed record Request(SoilTileMesher.Snapshot Snapshot, int Version, long Epoch, bool[] Wet, Vec2 Camera, double CameraY, int Limit, GeometryKey? CommittedPlan);
    private sealed record Result(Request Request, MeshData[] Groups, int Tiles, int Built, int Hits, double Milliseconds, ulong GridHash, SoilTileMesher.Request[] Plan, bool Unchanged = false);
    private sealed class Entry { public MeshData Mesh = null!; public long Last; public SoilTileMesher.Detail Detail; }
    // Only the serial worker accesses this cache. A changed snapshot clears it inside that worker.
    private sealed record GeometryKey(SoilTileMesher.Snapshot Snapshot, ulong GridHash, SoilTileMesher.Request[] Plan);
    private sealed class WorkerCache
    {
        public SoilTileMesher.Snapshot? Snapshot;
        public long Clock;
        public readonly Dictionary<SoilTileMesher.Request, Entry> Entries = new();
    }

    public void Build(VivariumWorld world, ShaderMaterial material)
    {
        if (_exited) throw new InvalidOperationException("Create a new soil renderer after it exits the scene tree");
        _world = world; _material = material;
        for (int i = 0; i < 3; i++) if (_instances[i] == null)
        {
            _meshes[i] = new ArrayMesh();
            _instances[i] = new MeshInstance3D { Name = $"SoilLOD{i}", Mesh = _meshes[i] };
            AddChild(_instances[i]);
        }
        Rebuild();
    }

    /// <summary>Call after terrain changes. Existing complete geometry remains visible until replacement.</summary>
    public void Rebuild()
    {
        if (_world == null) return;
        _snapshot = new SoilTileMesher.Snapshot(_world.Terrain, _world.Domain);
        _wet = CaptureWet(_world, _snapshot);
        _epoch++; _dirty = true;
        QueueIfIdle();
    }

    public void FinishInitialBuild()
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!SurfaceReady)
        {
            if (_exited || LastBuildError != null || wait.Elapsed.TotalSeconds > 120)
                throw new InvalidOperationException(LastBuildError ?? "Initial soil surface was not published");
            _job?.GetAwaiter().GetResult();
            _Process(0);
        }
    }
    public override void _ExitTree()
    {
        _exited = true; _epoch++;
        foreach (var mesh in _meshes) mesh?.Dispose();
    }
    public override void _Process(double delta)
    {
        if (_world == null || _exited) return;
        if (_snapshot == null || _world.Terrain.Version != _snapshot.Surface.TerrainVersion) { Rebuild(); }
        _waterPoll += delta;
        if (_waterPoll >= .5 && _snapshot != null)
        {
            _waterPoll = 0;
            var wet = CaptureWet(_world, _snapshot);
            if (!Equal(_wet, wet)) { _wet = wet; _epoch++; _dirty = true; }
        }
        var camera = CameraPosition();
        if (camera.DistanceSquaredTo(_requestedCamera) >= .75f * .75f) _dirty = true;
        if (_job != null && _job.IsCompleted)
        {
            var done = _job; var activeRequest = _activeRequest; _job = null; _activeRequest = null;
            if (done.IsCompletedSuccessfully)
            {
                var result = done.Result;
                if (!_exited && result.Request.Epoch == _epoch && ReferenceEquals(result.Request.Snapshot, _snapshot)
                    && result.Request.Version == _world.Terrain.Version)
                {
                    if (result.Unchanged) UnchangedRequests++;
                    else
                    {
                    for (int i = 0; i < 3; i++)
                    {
                        if (result.Groups[i].VertexCount == 0) _meshes[i].ClearSurfaces();
                        else Bridge.ToArrayMesh(result.Groups[i], _material, _meshes[i]);
                    }
                    TriangleCount = result.Groups[0].TriangleCount + result.Groups[1].TriangleCount + result.Groups[2].TriangleCount;
                    TileCount = result.Tiles; TilesBuilt = result.Built; CacheHits = result.Hits; BuildsCompleted++; SurfaceReady = true;
                    _committedPlan = new GeometryKey(result.Request.Snapshot, result.GridHash, result.Plan);
                    LastBuildMilliseconds = result.Milliseconds;
                    LastGroupPayloadBytes = 0;
                    foreach (var group in result.Groups)
                        LastGroupPayloadBytes += 4L * (group.Positions.Count + group.Normals.Count + group.Colors.Count + group.UV.Count + group.UV2.Count + group.Indices.Count);
                    }
                    LastBuildError = null;
                }
                else StaleBuildsDiscarded++;
            }
            else if (activeRequest != null && activeRequest.Epoch == _epoch
                && ReferenceEquals(activeRequest.Snapshot, _snapshot) && activeRequest.Version == _world.Terrain.Version)
            { LastBuildError = done.Exception?.GetBaseException().Message ?? "Soil build cancelled"; GD.PushError(LastBuildError); }
            else StaleBuildsDiscarded++;
        }
        QueueIfIdle();
    }

    private Vector3 CameraPosition()
    {
        var camera = GetViewport()?.GetCamera3D();
        return camera == null ? Vector3.Zero : camera.GlobalPosition;
    }
    private void QueueIfIdle()
    {
        if (!_dirty || _job != null || _world == null || _snapshot == null || _wet == null || _exited) return;
        _requestedCamera = CameraPosition(); _dirty = false;
        var request = new Request(_snapshot, _world.Terrain.Version, _epoch, _wet, new Vec2(_requestedCamera.X, _requestedCamera.Z), _requestedCamera.Y, Math.Max(0, CacheTileLimit), _committedPlan);
        _activeRequest = request;
        _job = Task.Run(() => Generate(request, _cache));
    }
    private static Result Generate(Request r, WorkerCache cache)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (!ReferenceEquals(cache.Snapshot, r.Snapshot))
        {
            var old = cache.Snapshot;
            if (old == null || !r.Snapshot.Surface.GeometryCompatible(old.Surface)) cache.Entries.Clear();
            else if (r.Snapshot.Surface.TryGetChangedBounds(old.Surface, out var lo, out var hi))
            {
                var remove = new List<SoilTileMesher.Request>();
                double tileWidth = r.Snapshot.TileCells * r.Snapshot.Surface.Spacing;
                foreach (var key in cache.Entries.Keys)
                {
                    double x = r.Snapshot.Surface.OriginX + key.Tile.X * tileWidth;
                    double z = r.Snapshot.Surface.OriginZ + key.Tile.Z * tileWidth;
                    if (x <= hi.X && x + tileWidth >= lo.X && z <= hi.Z && z + tileWidth >= lo.Z) remove.Add(key);
                }
                foreach (var key in remove) cache.Entries.Remove(key);
            }
            cache.Snapshot = r.Snapshot;
        }
        var plan = new List<SoilTileMesher.Request>();
        ulong gridHash = 14695981039346656037UL;
        int tiles = 0, built = 0, hits = 0;
        var s = r.Snapshot; double spacing = s.Surface.Spacing;
        for (int z = 0; z < s.TilesZ; z++) for (int x = 0; x < s.TilesX; x++)
        {
            double x0 = s.Surface.OriginX + x * s.TileCells * spacing, z0 = s.Surface.OriginZ + z * s.TileCells * spacing;
            double x1 = s.Surface.OriginX + Math.Min((x + 1) * s.TileCells, s.MaxX) * spacing;
            double z1 = s.Surface.OriginZ + Math.Min((z + 1) * s.TileCells, s.MaxZ) * spacing;
            double radius = .5 * Math.Sqrt((x1-x0)*(x1-x0) + (z1-z0)*(z1-z0));
            double sd = s.Domain.SignedDistance(new Vec2((x0+x1)*.5, (z0+z1)*.5));
            if (sd > radius) continue;
            var tile = new SoilTileMesher.Tile(x,z);
            var detail = SoilTileMesher.ChooseDetail(s, tile, r.Camera, r.Wet[z*s.TilesX+x] || sd > -radius, cameraY: r.CameraY);
            var key = new SoilTileMesher.Request(tile, detail);
            plan.Add(key);
            gridHash = unchecked((gridHash ^ (uint)((x * 73856093) ^ (z * 19349663) ^ ((int)detail + 1))) * 1099511628211UL);
        }
        bool unchanged = r.CommittedPlan is { } committed && ReferenceEquals(committed.Snapshot, r.Snapshot)
            && committed.GridHash == gridHash && committed.Plan.Length == plan.Count;
        if (unchanged) for (int i = 0; i < plan.Count; i++)
            if (r.CommittedPlan!.Plan[i] != plan[i]) { unchanged = false; break; }
        if (unchanged) return new Result(r, Array.Empty<MeshData>(), plan.Count, 0, 0, clock.Elapsed.TotalMilliseconds, gridHash, Array.Empty<SoilTileMesher.Request>(), true);
        var groups = new[] { new MeshData(), new MeshData(), new MeshData() };
        foreach (var key in plan)
        {
            var detail = key.Detail;
            if (!cache.Entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Mesh = SoilTileMesher.Build(s, key).Mesh, Detail = detail }; built++;
                if (r.Limit > 0) cache.Entries[key] = entry;
            }
            else hits++;
            entry.Last = ++cache.Clock;
            groups[(int)detail].Append(entry.Mesh); tiles++;
            while (cache.Entries.Count > r.Limit)
            {
                SoilTileMesher.Request victim = default; Entry? least = null;
                foreach (var pair in cache.Entries)
                    if (least == null || pair.Value.Detail > least.Detail || (pair.Value.Detail == least.Detail && pair.Value.Last < least.Last)) { victim = pair.Key; least = pair.Value; }
                cache.Entries.Remove(victim);
            }
        }
        foreach (var group in groups) Bridge.PrepareMeshUpload(group, true);
        return new Result(r, groups, tiles, built, hits, clock.Elapsed.TotalMilliseconds, gridHash, plan.ToArray());
    }
    private static bool Equal(bool[]? a, bool[] b)
    {
        if (a == null || a.Length != b.Length) return false;
        for (int i = 0; i < b.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static bool[] CaptureWet(VivariumWorld w, SoilTileMesher.Snapshot s)
    {
        var wet = new bool[s.TilesX*s.TilesZ];
        double tileSize = s.TileCells*s.Surface.Spacing;
        var fluid = w.Liquid.CaptureRenderState();
        foreach (var sample in fluid.Cells)
        {
            if (sample.Depth <= 1e-8 || sample.Depth >= .03) continue;
            double halo = fluid.Spacing * 2;
            int x0 = (int)Math.Floor((sample.Position.X-halo-s.Surface.OriginX)/tileSize), x1 = (int)Math.Floor((sample.Position.X+halo-s.Surface.OriginX)/tileSize);
            int z0 = (int)Math.Floor((sample.Position.Z-halo-s.Surface.OriginZ)/tileSize), z1 = (int)Math.Floor((sample.Position.Z+halo-s.Surface.OriginZ)/tileSize);
            for (int z=Math.Max(0,z0); z<=Math.Min(s.TilesZ-1,z1); z++) for (int x=Math.Max(0,x0); x<=Math.Min(s.TilesX-1,x1); x++) wet[z*s.TilesX+x]=true;
        }
        // Macro vertices enclosing each tile bound the piecewise planar terrain; relief is bounded by amplitude.
        var h = w.Terrain; double amplitude = s.Surface.Parameters.Amplitude, table = w.Water.WaterTable;
        for (int z=0; z<s.TilesZ; z++) for (int x=0; x<s.TilesX; x++)
        {
            double x0=s.Surface.OriginX+x*tileSize, z0=s.Surface.OriginZ+z*tileSize;
            double x1=Math.Min(x0+tileSize,s.Surface.OriginX+s.MaxX*s.Surface.Spacing), z1=Math.Min(z0+tileSize,s.Surface.OriginZ+s.MaxZ*s.Surface.Spacing);
            int i0=Math.Clamp((int)Math.Floor((x0-h.OriginX)/h.Step),0,h.Nx-1), i1=Math.Clamp((int)Math.Ceiling((x1-h.OriginX)/h.Step),0,h.Nx-1);
            int j0=Math.Clamp((int)Math.Floor((z0-h.OriginZ)/h.Step),0,h.Nz-1), j1=Math.Clamp((int)Math.Ceiling((z1-h.OriginZ)/h.Step),0,h.Nz-1);
            double min=double.PositiveInfinity,max=double.NegativeInfinity;
            for(int j=j0;j<=j1;j++) for(int i=i0;i<=i1;i++) { double v=h.H[j*h.Nx+i]; min=Math.Min(min,v); max=Math.Max(max,v); }
            if(table>=min-amplitude && table<=max+amplitude+.03) wet[z*s.TilesX+x]=true;
        }
        return wet;
    }
}
