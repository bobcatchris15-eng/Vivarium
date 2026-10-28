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
        public ImageTexture Previous = null!, Current = null!, Ground = null!;
        public byte[] UploadBytes = new byte[TexSize * TexSize * 16];
        public float[] CachedValues = new float[TexSize * TexSize * 4];
        public float[] NewValues = new float[TexSize * TexSize * 4];
        public const int GroundSize = (TileCells + 2) * Subdivisions + 1;
        public float[] GroundValues = new float[GroundSize * GroundSize * 4];
        public byte[] GroundBytes = new byte[GroundSize * GroundSize * 16];
        public Image StateImage = Image.CreateEmpty(TexSize, TexSize, false, Image.Format.Rgbaf);
        public Image GroundImage = Image.CreateEmpty(GroundSize, GroundSize, false, Image.Format.Rgbaf);
        public bool HasUploaded;
        public int Terrain = -1, Props = -1;
    }
    private readonly Dictionary<(int, int), Tile> _tiles = new();
    private readonly Dictionary<(int,int),SurfaceFluidSample> _cells=new();
    private readonly HashSet<(int,int)> _active=new();
    private VivariumWorld? _w;
    public EnvironmentRig? Lighting { get; set; }
    private ArrayMesh _topology = null!;
    private ImageTexture _ripples = null!;
    // Separate compiled material: the cheap variant declares no screen/depth samplers.
    public bool CheapRefraction { get; set; } = System.Environment.GetEnvironmentVariable("VIVARIUM_WATER_REFRACTION") == "substrate";
    private long _version = -1;
    private double _elapsed, _blendSeconds = 1.0 / 60, _opticalTime;
    private SurfaceFluidSnapshot? _snapshot;
    public int TrianglesBuilt { get; private set; }
    public long SurfaceUpdates { get; private set; }
    public int AirborneSegments => 0;
    public int GridCells => _snapshot?.Cells.Length ?? 0;
    // One RGBA state per time sample; immutable terrain support is refreshed only on edits.
    public int TileCount => _tiles.Count;
    public int TilesUploaded { get; private set; }
    public int TilesCulled => 0;
    public int TilesResynced => 0;
    public double LastUploadMilliseconds { get; private set; }
    public long AllocatedBytes { get; private set; }
    public long TextureUploadBytes { get; private set; }
    public override void _Ready()
    {
        var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>(); int n = TileCells * Subdivisions;
        for (int z = 0; z <= n; z++) for (int x = 0; x <= n; x++) { vertices.Add(new Vector3((float)x / n, 0, (float)z / n)); uv.Add(new Vector2((float)x / n, (float)z / n)); }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) { int k = z * (n + 1) + x; indices.AddRange(new[] { k, k + 1, k + n + 2, k, k + n + 2, k + n + 1 }); }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        _topology = new(); _topology.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _ripples = MakeRippleNormal();
    }
    public void Bind(VivariumWorld world)
    {
        _w = world; _opticalTime = world.Liquid.PresentationTime; _version = -1; _snapshot = null; SurfaceUpdates = 0; TrianglesBuilt = 0; TextureUploadBytes = 0;
        foreach (var t in _tiles.Values) { t.Node.QueueFree(); t.Previous.Dispose(); t.Current.Dispose(); t.Ground.Dispose(); t.StateImage.Dispose(); t.GroundImage.Dispose(); }
        _tiles.Clear();
    }
    private static int FloorDiv(int value, int divisor) => (int)Math.Floor((double)value / divisor);
    private static ImageTexture Texture(float[] values, int width)
    { var bytes = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); using var im = Image.CreateFromData(width, width, false, Image.Format.Rgbaf, bytes); return ImageTexture.CreateFromImage(im); }
    private void Update(ImageTexture texture, float[] values, int width, byte[] bytes, Image im)
    { Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); im.SetData(width, width, false, Image.Format.Rgbaf, bytes); texture.Update(im); TextureUploadBytes += bytes.Length; }

    /// <summary>Opt-in native audit, outside measurement. Includes tile halo samples and support textures.</summary>
    public (int Tiles, int Mismatches) AuditPublishedTextures()
    {
        int tiles = 0, mismatches = 0;
        var expectedActive = new HashSet<(int, int)>();
        foreach (var cell in _cells.Keys)
        for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
            expectedActive.Add((FloorDiv(cell.Item1 + x, TileCells), FloorDiv(cell.Item2 + z, TileCells)));
        if (!expectedActive.SetEquals(_active)) mismatches++;
        foreach (var (key, t) in _tiles)
        {
            if (!t.Node.Visible) continue;
            tiles++;
            var values = new float[TexSize * TexSize * 4];
            for (int z = 0; z < TexSize; z++) for (int x = 0; x < TexSize; x++)
            {
                int k = (z * TexSize + x) * 4;
                if (_cells.TryGetValue((key.Item1 * TileCells + x - 1, key.Item2 * TileCells + z - 1), out var c))
                {
                    values[k] = (float)(c.Head - c.Depth); values[k + 1] = (float)c.Depth;
                    values[k + 2] = (float)c.Velocity.X; values[k + 3] = (float)c.Velocity.Z;
                }
            }
            bool EqualTexture(ImageTexture texture, float[] payload)
            {
                var bytes = new byte[payload.Length * 4]; Buffer.BlockCopy(payload, 0, bytes, 0, bytes.Length);
                using var im = texture.GetImage(); return im.GetData().AsSpan().SequenceEqual(bytes);
            }
            if (!EqualTexture(t.Current, values)) mismatches++;
        }
        return (tiles, mismatches);
    }
    private Tile MakeTile((int X, int Z) key, double spacing)
    {
        var t = new Tile(); t.Material = Bridge.Shader(CheapRefraction ? "res://Shaders/surface_fluid_cheap.gdshader" : "res://Shaders/surface_fluid.gdshader"); t.Material.SetShaderParameter("debug_surface", System.Environment.GetEnvironmentVariable("VIVARIUM_FLUID_DIAGNOSTIC") == "1");
        if (CheapRefraction) t.Material.SetShaderParameter("u_substrate_albedo", Bridge.LoadTex(Bridge.ResolveSurfaceSet(Bridge.Surfaces.Damp).Color));
        int groundSize=(TileCells+2)*Subdivisions+1;
        t.Previous = Texture(t.CachedValues, TexSize); t.Current = Texture(t.CachedValues, TexSize); t.Ground = Texture(new float[groundSize*groundSize*4], groundSize);
        t.Node = new()
        {
            Name = $"LiquidTile_{key.X}_{key.Z}",
            Mesh = _topology,
            MaterialOverride = t.Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(0, -4, 0), new Vector3((float)(TileCells * spacing), 8, (float)(TileCells * spacing)))
        };
        t.Material.SetShaderParameter("previous_state", t.Previous); t.Material.SetShaderParameter("current_state", t.Current); t.Material.SetShaderParameter("solid", t.Ground); t.Material.SetShaderParameter("u_ripple_normal", _ripples);
        t.Material.SetShaderParameter("tile_origin", new Vector2((float)(key.X * TileCells * spacing), (float)(key.Z * TileCells * spacing)));
        t.Material.SetShaderParameter("tile_size", (float)(TileCells * spacing));
        t.Node.Position = new Vector3((float)(key.X * TileCells * spacing), 0, (float)(key.Z * TileCells * spacing)); AddChild(t.Node); return t;
    }
    public override void _Process(double delta)
    {
        using var measure = FrameProfiler.Measure("LiquidSurface"); if (_w == null) return;
        if (!_w.Clock.Paused) _opticalTime += delta * _w.Clock.SpeedMultiplier;
        _elapsed += delta; var snapshot = _w.Liquid.CaptureRenderState();
        // A loaded coarse grid can refine on its first update. Recreate tile
        // extents and support textures rather than blending incompatible grids.
        if (_snapshot != null && snapshot.Spacing != _snapshot.Spacing)
        {
            double preservedOpticalTime = _opticalTime;
            Bind(_w);
            _opticalTime = preservedOpticalTime;
        }
        if (snapshot.Version != _version)
        {
            long allocationStart = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
            _blendSeconds = Math.Clamp(_elapsed, 1.0 / 120, .15); _elapsed = 0; _snapshot = snapshot; _version = snapshot.Version;
            using (FrameProfiler.Measure("LiquidSurface.LookupPreparation"))
            {
                var cells = _cells; cells.Clear(); cells.EnsureCapacity(snapshot.Cells.Length);
                var active = _active; active.Clear();
                foreach (var c in snapshot.Cells)
                {
                    int cx = (int)Math.Floor(c.Position.X / snapshot.Spacing), cz = (int)Math.Floor(c.Position.Z / snapshot.Spacing);
                    cells.Add((cx, cz), c);
                    int tx = FloorDiv(cx, TileCells), tz = FloorDiv(cz, TileCells);
                    int lx = cx - tx * TileCells, lz = cz - tz * TileCells;
                    // Most cells' nine halo samples belong to the same tile.
                    // Only boundary cells can require a neighbouring tile.
                    int x0 = lx == 0 ? tx - 1 : tx, x1 = lx == TileCells - 1 ? tx + 1 : tx;
                    int z0 = lz == 0 ? tz - 1 : tz, z1 = lz == TileCells - 1 ? tz + 1 : tz;
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) active.Add((x, z));
                }
                foreach (var key in active) if (!_tiles.ContainsKey(key)) _tiles[key] = MakeTile(key, snapshot.Spacing);
            }
            int uploadedCount = 0;
            foreach (var (key, t) in _tiles)
            {
                bool visible = _active.Contains(key); if (visible && !t.Node.Visible) t.HasUploaded = false; t.Node.Visible = visible; if (!visible) continue;
                using (FrameProfiler.Measure("LiquidSurface.TileFilling"))
                {
                    Array.Clear(t.NewValues);
                    for (int z = 0; z < TexSize; z++) for (int x = 0; x < TexSize; x++)
                    {
                        int cx = key.Item1 * TileCells + x - 1, cz = key.Item2 * TileCells + z - 1, k = (z * TexSize + x) * 4;
                        if (_cells.TryGetValue((cx, cz), out var c)) { t.NewValues[k] = (float)(c.Head - c.Depth); t.NewValues[k + 1] = (float)c.Depth; t.NewValues[k + 2] = (float)c.Velocity.X; t.NewValues[k + 3] = (float)c.Velocity.Z; }
                    }
                }
                bool changed = !t.HasUploaded ||
                    !t.NewValues.AsSpan().SequenceEqual(t.CachedValues);
                if (changed)
                {
                    using (FrameProfiler.Measure("LiquidSurface.TextureTransfer"))
                    {
                        (t.Previous, t.Current) = (t.Current, t.Previous);
                        Update(t.Current, t.NewValues, TexSize, t.UploadBytes, t.StateImage);
                        Array.Copy(t.NewValues, t.CachedValues, t.NewValues.Length);
                        if (!t.HasUploaded)
                        {
                            Update(t.Previous, t.CachedValues, TexSize, t.UploadBytes, t.StateImage);
                            t.HasUploaded = true;
                        }
                        t.Material.SetShaderParameter("previous_state", t.Previous);
                        t.Material.SetShaderParameter("current_state", t.Current);
                        uploadedCount++;
                    }
                }
                else
                {
                    t.Material.SetShaderParameter("previous_state", t.Current);
                    t.Material.SetShaderParameter("current_state", t.Current);
                }
                if (t.Terrain != _w.Terrain.Version || t.Props != _w.Props.Version)
                {
                    using (FrameProfiler.Measure("LiquidSurface.SupportRefresh"))
                    {
                        int n = Tile.GroundSize; var solid = t.GroundValues;
                        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
                        {
                            var p = new Vec2((key.Item1 * TileCells - 1 + (double)x / Subdivisions) * snapshot.Spacing, (key.Item2 * TileCells - 1 + (double)z / Subdivisions) * snapshot.Spacing); int k = (z * n + x) * 4;
                            solid[k] = (float)_w.GroundHeight(p); solid[k + 1] = (float)_w.Terrain.Height(p); solid[k + 2] = _w.Domain.Contains(p) ? 1 : 0;
                        }
                        Update(t.Ground, solid, n, t.GroundBytes, t.GroundImage); t.Terrain = _w.Terrain.Version; t.Props = _w.Props.Version;
                    }
                }
                t.Material.SetShaderParameter("pond_level", (float)_w.Water.WaterTable);
                t.Material.SetShaderParameter("fluid_time", (float)snapshot.Time);
            }
            SurfaceUpdates++; TilesUploaded = uploadedCount; TrianglesBuilt = _active.Count * TileCells * TileCells * Subdivisions * Subdivisions * 2;
            AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocationStart; LastUploadMilliseconds = watch.Elapsed.TotalMilliseconds; FrameProfiler.Record("LiquidSurface.Upload", LastUploadMilliseconds);
        }

        float opticalTime = (float)_opticalTime;
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
    // Analytic, periodic capillary normal map. Generated once, shared by all tiles.
    // Integer Fourier frequencies make every edge tile exactly without a noise seam.
    private static ImageTexture MakeRippleNormal()
    {
        const int size = 128;
        using var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
        {
            double u = x * Math.Tau / size, v = z * Math.Tau / size;
            double dx = .48 * Math.Cos(2*u+v) + .27 * Math.Cos(3*u-2*v+.7) + .12 * Math.Cos(u+5*v+1.9);
            double dz = .24 * Math.Cos(2*u+v) - .18 * Math.Cos(3*u-2*v+.7) + .60 * Math.Cos(u+5*v+1.9);
            var n = new Vector3((float)-dx, (float)-dz, 1).Normalized();
            image.SetPixel(x,z,new Color(n.X*.5f+.5f,n.Y*.5f+.5f,n.Z*.5f+.5f));
        }
        image.GenerateMipmaps(); return ImageTexture.CreateFromImage(image);
    }
    public override void _ExitTree()
    {
        foreach (var t in _tiles.Values) { t.Previous.Dispose(); t.Current.Dispose(); t.Ground.Dispose(); t.StateImage.Dispose(); t.GroundImage.Dispose(); }
        _ripples?.Dispose(); _topology?.Dispose();
    }
}
