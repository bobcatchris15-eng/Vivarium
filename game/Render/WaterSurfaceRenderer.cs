using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Vivarium.Game.App;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
using Vivarium.Sim.Core;
namespace Vivarium.Game.Render;

/// <summary>Persistent GPU-displaced tiles interpolate immutable conserved-volume snapshots.</summary>
public partial class WaterSurfaceRenderer : Node3D
{
    private const int TileCells = 16, TexSize = 18, Subdivisions = 6;
    private sealed class Tile
    {
        public MeshInstance3D Node = null!; public ShaderMaterial Material = null!;
        public ImageTexture Previous = null!, Current = null!, Ground = null!, PreviousSupport = null!, CurrentSupport = null!;
        public byte[] UploadBytes = new byte[TexSize * TexSize * 16];
        public float[] Values = new float[TexSize * TexSize * 4]; public float[] Support = new float[TexSize * TexSize * 4]; public int Terrain = -1, Props = -1;
        /// <summary>The tile was not uploaded while it was off screen; its
        /// textures are stale and it is resynchronised when it comes back.</summary>
        public bool Cold;
    }
    private readonly Dictionary<(int, int), Tile> _tiles = new();
    private readonly Dictionary<(int,int),SurfaceFluidSample> _cells=new();
    private readonly HashSet<(int,int)> _active=new();
    private VivariumWorld? _w;
    public EnvironmentRig? Lighting { get; set; }
    /// <summary>The camera the water is seen through. Tiles it cannot see are
    /// not uploaded: the fluid still simulates them, but their textures only
    /// have to be current when they are on screen.</summary>
    public Camera3D? Camera { get; set; }
    private ArrayMesh _topology = null!;
    private BallisticFluidRenderer _air = null!;
    private long _version = -1;
    private double _elapsed, _blendSeconds = 1.0 / 60;
    private SurfaceFluidSnapshot? _snapshot;
    public int TrianglesBuilt { get; private set; }
    public long SurfaceUpdates { get; private set; }
    public int AirborneSegments => _air.BrickCount;
    public int GridCells => _snapshot?.Cells.Length ?? 0;
    /// <summary>How many water tiles the last refresh uploaded, and how many it
    /// left alone because the camera could not see them.</summary>
    public int TilesUploaded { get; private set; }
    public int TilesCulled { get; private set; }
    public int TilesResynced { get; private set; }
    public int TileCount { get; private set; }
    public double LastUploadMilliseconds { get; private set; }
    public long AllocatedBytes { get; private set; }
    public override void _Ready()
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>(); int n = TileCells * Subdivisions;
        for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++) { vertices.Add(new Vector3((float)x / n, 0, (float)z / n)); uv.Add(new Vector2((float)x / n, (float)z / n)); }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) { int k = z * (n + 1) + x; indices.AddRange(new[] { k, k + 1, k + n + 2, k, k + n + 2, k + n + 1 }); }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        _topology = new(); _topology.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _air = new() { Name = "UnsupportedLiquid" }; AddChild(_air);
    }
    public void Bind(VivariumWorld world)
    {
        _w = world; _version = -1; _snapshot = null; SurfaceUpdates = 0; TrianglesBuilt = 0;
        foreach (var t in _tiles.Values) { t.Node.QueueFree(); t.Previous.Dispose(); t.Current.Dispose(); t.Ground.Dispose(); t.PreviousSupport.Dispose(); t.CurrentSupport.Dispose(); }
        _tiles.Clear();
        _air.UpdateFluid(new(0, 0, SurfaceFluidDynamics.DefaultSpacing, [], []));
    }
    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);
    private Plane[] _frustum = Array.Empty<Plane>();
    /// <summary>Whether a tile can be seen by the camera. Godot's frustum planes
    /// face outward, so a tile whose farthest corner along a plane normal is
    /// behind that plane is entirely outside it.</summary>
    private bool TileOnScreen((int X, int Z) key, double spacing)
    {
        if (Camera == null) return true;
        if (_frustum.Length == 0) return true;
        float side = (float)(TileCells * spacing);
        var center = new Vector3((float)(key.X * TileCells * spacing) + side * .5f, 0f, (float)(key.Z * TileCells * spacing) + side * .5f);
        var extent = new Vector3(side * .5f, 4f, side * .5f);
        foreach (var plane in _frustum)
        {
            var n = plane.Normal;
            var far = center + new Vector3(Mathf.Abs(n.X) * extent.X, Mathf.Abs(n.Y) * extent.Y, Mathf.Abs(n.Z) * extent.Z);
            // Godot's frustum planes face outward: a point inside the frustum has
            // a negative distance. The tile is outside the plane only when even
            // its farthest corner along the normal is outside it.
            if (plane.DistanceTo(far) > 0) return false;
        }
        return true;
    }
    private static ImageTexture Texture(float[] values, int width)
    { var bytes = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); using var im = Image.CreateFromData(width, width, false, Image.Format.Rgbaf, bytes); return ImageTexture.CreateFromImage(im); }
    // The upload reuses one image per texture size instead of allocating a
    // native image for every update: a mature world uploads a texture pair for
    // every water tile on every fluid snapshot.
    private static readonly Dictionary<(int width, int size), Image> UploadImages = new();
    private static void Update(ImageTexture texture, float[] values, int width, byte[]? buffer = null)
    {
        var bytes = buffer ?? new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var key = (width, bytes.Length);
        if (!UploadImages.TryGetValue(key, out var image)) { image = Image.CreateFromData(width, width, false, Image.Format.Rgbaf, bytes); UploadImages[key] = image; }
        else image.SetData(width, width, false, Image.Format.Rgbaf, bytes);
        texture.Update(image);
    }
    private Tile MakeTile((int X, int Z) key, double spacing)
    {
        var t = new Tile(); t.Material = Bridge.Shader("res://Shaders/surface_fluid.gdshader"); t.Material.SetShaderParameter("debug_surface", System.Environment.GetEnvironmentVariable("VIVARIUM_FLUID_DIAGNOSTIC") == "1");
        int groundSize=(TileCells+2)*Subdivisions+1;
        t.PreviousSupport = Texture(t.Support, TexSize); t.CurrentSupport = Texture(t.Support, TexSize); t.Previous = Texture(t.Values, TexSize); t.Current = Texture(t.Values, TexSize); t.Ground = Texture(new float[groundSize*groundSize*4], groundSize);
        t.Node = new()
        {
            Name = $"LiquidTile_{key.X}_{key.Z}",
            Mesh = _topology,
            MaterialOverride = t.Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(0, -4, 0), new Vector3((float)(TileCells * spacing), 8, (float)(TileCells * spacing)))
        };
        t.Material.SetShaderParameter("previous_state", t.Previous); t.Material.SetShaderParameter("current_state", t.Current); t.Material.SetShaderParameter("solid", t.Ground); t.Material.SetShaderParameter("previous_support", t.PreviousSupport); t.Material.SetShaderParameter("current_support", t.CurrentSupport);
        t.Material.SetShaderParameter("tile_origin", new Vector2((float)(key.X * TileCells * spacing), (float)(key.Z * TileCells * spacing)));
        t.Material.SetShaderParameter("tile_size", (float)(TileCells * spacing));
        t.Node.Position = new Vector3((float)(key.X * TileCells * spacing), 0, (float)(key.Z * TileCells * spacing)); AddChild(t.Node); return t;
    }
    public override void _Process(double delta)
    {
        using var measure = FrameProfiler.Measure("LiquidSurface"); if (_w == null) return;
        _elapsed += delta; var snapshot = _w.Liquid.CaptureRenderState();
        if (snapshot.Version != _version)
        {
            if (Camera != null)
            {
                var planes = Camera.GetFrustum();
                if (_frustum.Length != planes.Count) _frustum = new Plane[planes.Count];
                for (int i = 0; i < planes.Count; i++) _frustum[i] = planes[i];
            }
            long allocationStart = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
            _blendSeconds = Math.Clamp(_elapsed, 1.0 / 120, .15); _elapsed = 0; _snapshot = snapshot; _version = snapshot.Version;
            var cells=_cells;cells.Clear();cells.EnsureCapacity(snapshot.Cells.Length);
            foreach(var c in snapshot.Cells)cells.Add(((int)Math.Floor(c.Position.X/snapshot.Spacing),(int)Math.Floor(c.Position.Z/snapshot.Spacing)),c);
            var active=_active;active.Clear();
            foreach (var c in cells.Keys) for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) active.Add((FloorDiv(c.Item1 + x, TileCells), FloorDiv(c.Item2 + z, TileCells)));
            foreach (var key in active) if (!_tiles.ContainsKey(key)) _tiles[key] = MakeTile(key, snapshot.Spacing);
            TilesUploaded = 0; TilesCulled = 0; TilesResynced = 0; TileCount = _tiles.Count;
            foreach (var (key, t) in _tiles)
            {
                bool visible = active.Contains(key); t.Node.Visible = visible; if (!visible) continue;
                // A tile the camera cannot see does not need its textures
                // uploaded: the fluid keeps simulating it, and the textures are
                // brought up to date when it comes back into view.
                bool onScreen = TileOnScreen(key, snapshot.Spacing);
                if (!onScreen) { t.Cold = true; TilesCulled++; continue; }
                bool resync = t.Cold;
                t.Cold = false;
                TilesUploaded++;
                if (resync) TilesResynced++;
                // Swap the two GPU states; the former current texture already contains the
                // previous snapshot, so uploading it a second time wastes copies and allocations.
                (t.Previous, t.Current) = (t.Current, t.Previous); (t.PreviousSupport, t.CurrentSupport) = (t.CurrentSupport, t.PreviousSupport);
                t.Material.SetShaderParameter("previous_state", t.Previous); t.Material.SetShaderParameter("current_state", t.Current);
                t.Material.SetShaderParameter("previous_support", t.PreviousSupport); t.Material.SetShaderParameter("current_support", t.CurrentSupport);
                Array.Clear(t.Support);
                Array.Clear(t.Values);
                for (int z = 0; z < TexSize; z++) for (int x = 0; x < TexSize; x++)
                    {
                        int cx = key.Item1 * TileCells + x - 1, cz = key.Item2 * TileCells + z - 1, k = (z * TexSize + x) * 4;
                        if (cells.TryGetValue((cx, cz), out var c)) { t.Values[k] = (float)c.Head; t.Values[k + 1] = (float)c.Depth; t.Values[k + 2] = (float)c.Velocity.X; t.Values[k + 3] = (float)c.Velocity.Z; t.Support[k] = (float)c.SlopeX; t.Support[k + 1] = (float)c.SlopeZ; }
                    }
                Update(t.Current, t.Values, TexSize, t.UploadBytes); Update(t.CurrentSupport, t.Support, TexSize, t.UploadBytes);
                if (resync)
                {
                    // The tile was stale while it was off screen. Both states
                    // become the current one, so it appears where the fluid is
                    // now instead of where it was.
                    Update(t.Previous, t.Values, TexSize, t.UploadBytes); Update(t.PreviousSupport, t.Support, TexSize, t.UploadBytes);
                }
                if (t.Terrain != _w.Terrain.Version || t.Props != _w.Props.Version)
                {
                    // A full cell halo supports reconstruction from neighboring cells
                    // at tile edges; clamping their bed to the tile creates raised seams.
                    int n = (TileCells+2) * Subdivisions + 1; var solid = new float[n * n * 4];
                    for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                        {
                            var p = new Vec2((key.Item1 * TileCells - 1 + (double)x / Subdivisions) * snapshot.Spacing, (key.Item2 * TileCells - 1 + (double)z / Subdivisions) * snapshot.Spacing); int k = (z * n + x) * 4;
                            solid[k] = (float)_w.GroundHeight(p); solid[k + 1] = (float)_w.Terrain.Height(p); solid[k + 2] = _w.Domain.Contains(p) ? 1 : 0;
                        }
                    Update(t.Ground, solid, n); t.Terrain = _w.Terrain.Version; t.Props = _w.Props.Version;
                }
                t.Material.SetShaderParameter("pond_level", (float)_w.Water.WaterTable);
                t.Material.SetShaderParameter("fluid_time", (float)snapshot.Time);
            }
            UpdateAir(snapshot); SurfaceUpdates++; TrianglesBuilt = active.Count * TileCells * TileCells * Subdivisions * Subdivisions * 2 + snapshot.Airborne.Length * 120;
            AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart; LastUploadMilliseconds = watch.Elapsed.TotalMilliseconds; FrameProfiler.Record("LiquidSurface.Upload", LastUploadMilliseconds);
        }

        _air.SetInterpolation(_w.Clock.Paused ? 0f : (float)Math.Min(_elapsed, .016667));
        float opticalTime = (float)((_snapshot?.Time ?? 0) + (_w.Clock.Paused ? 0 : Math.Min(_elapsed, _blendSeconds)));
        _air.SetTime(opticalTime);
        if (Lighting != null) _air.SetCanopyLighting(Lighting.CanopyEnergy, Lighting.CanopyLightDirection);
        float blend = (float)Math.Clamp(_elapsed / _blendSeconds, 0, 1);
        foreach (var t in _tiles.Values) if (t.Node.Visible)
        {
            t.Material.SetShaderParameter("state_blend", blend);
            t.Material.SetShaderParameter("pond_level", (float)_w.Water.WaterTable);
            if (Lighting != null) { t.Material.SetShaderParameter("sky_energy", Lighting.CanopyEnergy); t.Material.SetShaderParameter("light_direction", Lighting.CanopyLightDirection); }
            // Optical motion runs every displayed frame, rather than stopping between
            // asynchronous physical snapshots. It uses physical seconds and freezes on pause.
            t.Material.SetShaderParameter("fluid_time", opticalTime);
        }
    }
    private void UpdateAir(SurfaceFluidSnapshot snapshot) => _air.UpdateFluid(snapshot);
}
