using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr;

namespace Vivarium.Repr;

/// <summary>Disposable, per-colony-tile visual cache. Mutate the simulation on the main thread.</summary>
public sealed partial class ColonyRenderer : Node3D
{
    private sealed class TileCache
    {
        public readonly MeshInstance3D?[] Levels = new MeshInstance3D?[3];
        public ulong Signature;
        public int Shown = -1;
    }
    private ColonySystem _system = null!;
    private ShaderMaterial _material = null!;
    private readonly Dictionary<long, TileCache> _tiles = new();
    private long _substrateCursor;
    private readonly List<DirtyRecord> _dirty = new();
    public long TilesBuilt { get; private set; }
    public long TilesInvalidated { get; private set; }
    public Func<Camera3D?>? CameraProvider;

    /// Reinitialization releases all cached meshes; counters reset for the new system.
    public void Init(ColonySystem system)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        ReleaseResources();
        TilesBuilt = 0; TilesInvalidated = 0;
        _system = system;
        _substrateCursor = system.Substrate.DirtyLog.Head;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/repr_colony.gdshader") };
        _material.SetShaderParameter("moss_color", GD.Load<Texture2D>("res://Textures/Moss002_1K-JPG_Color.jpg"));
        _material.SetShaderParameter("moss_roughness", GD.Load<Texture2D>("res://Textures/Moss002_1K-JPG_Roughness.jpg"));
    }

    private void ReleaseResources()
    {
        foreach (var cache in _tiles.Values)
            foreach (var level in cache.Levels)
                if (level != null)
                {
                    level.Visible = false;
                    var mesh = level.Mesh; level.Mesh = null;
                    level.MaterialOverride = null;
                    mesh?.Dispose(); level.QueueFree();
                }
        _tiles.Clear();
        _material?.Dispose(); _material = null!;
    }
    public override void _ExitTree() => ReleaseResources();

    private ulong Signature(ColonyTile tile)
    {
        // Microstructure samples each cell corner from that cell and its west, south and
        // southwest neighbors. Only these tiles can influence this tile's generated mesh.
        ulong h = 1469598103934665603UL;
        for (int z = -1; z <= 0; z++) for (int x = -1; x <= 0; x++)
        {
            var n = _system.Field.FindTile(tile.Tx + x, tile.Tz + z);
            h = unchecked((h ^ (n?.Version ?? 0)) * 1099511628211UL);
        }
        return h;
    }
    private void Invalidate(TileCache cache)
    {
        foreach (var level in cache.Levels) if (level != null)
        {
            level.Visible = false;
            var mesh = level.Mesh; level.Mesh = null; level.MaterialOverride = null;
            mesh?.Dispose(); level.QueueFree();
        }
        Array.Clear(cache.Levels, 0, cache.Levels.Length);
        cache.Shown = -1;
        TilesInvalidated++;
    }
    private static bool Overlaps(ColonyTile t, ColonySystem sys, Vivarium.Sim.Repr.Aabb b)
    {
        double size = ColonyTile.Side * sys.Field.CellSize, pad = sys.Field.CellSize;
        double x = t.Tx * size, z = t.Tz * size;
        return b.Min.X <= x + size + pad && b.Max.X >= x - pad && b.Min.Z <= z + size + pad && b.Max.Z >= z - pad;
    }
    private void PollSubstrate()
    {
        _dirty.Clear();
        _substrateCursor = _system.Substrate.DirtyLog.Read(_substrateCursor, DirtyDomain.Geometry | DirtyDomain.SoilMaterial | DirtyDomain.Water, _dirty);
        foreach (var tile in _system.Field.Tiles)
        {
            if (!_tiles.TryGetValue(ColonyField.TileKey(tile.Tx, tile.Tz), out var cache)) continue;
            foreach (var record in _dirty) if (Overlaps(tile, _system, record.Bounds)) { Invalidate(cache); break; }
        }
    }
    private static ArrayMesh Pack(MeshData m)
    {
        int n = m.VertexCount;
        var v = new Vector3[n]; var normals = new Vector3[n]; var colors = new Color[n];
        var uv = new Vector2[n]; var uv2 = new Vector2[n];
        var c0 = m.Custom0.ToArray(); var c1 = m.Custom1.ToArray();
        for (int i = 0; i < n; i++)
        {
            v[i] = new Vector3(m.Positions[3*i], m.Positions[3*i+1], m.Positions[3*i+2]);
            normals[i] = new Vector3(m.Normals[3*i], m.Normals[3*i+1], m.Normals[3*i+2]);
            colors[i] = new Color(m.Colors[4*i], m.Colors[4*i+1], m.Colors[4*i+2], m.Colors[4*i+3]);
            uv[i] = new Vector2(m.UV[2*i], m.UV[2*i+1]); uv2[i] = new Vector2(m.UV2[2*i], m.UV2[2*i+1]);
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v; arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors; arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2; arrays[(int)Mesh.ArrayType.Custom0] = c0;
        arrays[(int)Mesh.ArrayType.Custom1] = c1; arrays[(int)Mesh.ArrayType.Index] = m.Indices.ToArray();
        var flags = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
                  | (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        var mesh = new ArrayMesh();
        if (n > 0 && m.Indices.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);
        return mesh;
    }
    public void SettleFor(Vector3 eye, float fovDeg, float viewportHeightPx)
    {
        PollSubstrate();
        foreach (var tile in _system.Field.Tiles)
        {
            long key = ColonyField.TileKey(tile.Tx, tile.Tz);
            if (!_tiles.TryGetValue(key, out var cache)) _tiles.Add(key, cache = new TileCache { Signature = Signature(tile) });
            ulong signature = Signature(tile);
            if (signature != cache.Signature) { Invalidate(cache); cache.Signature = signature; }
            double size = ColonyTile.Side * _system.Field.CellSize;
            double cx = (tile.Tx + 0.5) * size, cz = (tile.Tz + 0.5) * size;
            double dx = eye.X - cx, dz = eye.Z - cz;
            double distance = Math.Sqrt(dx*dx + dz*dz + eye.Y*eye.Y);
            double mpp = 2 * Math.Max(0.05, distance) * Math.Tan(Mathf.DegToRad(fovDeg) * 0.5) / Math.Max(1, viewportHeightPx);
            int level = mpp < 0.0015 ? 0 : mpp < 0.006 ? 1 : 2;
            if (cache.Levels[level] == null)
            {
                var data = ColonyMesher.BuildTileMicrostructure(_system, tile.Tx, tile.Tz, level).Mesh;
                var inst = new MeshInstance3D { Mesh = Pack(data), MaterialOverride = _material, Visible = false,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(inst); cache.Levels[level] = inst; TilesBuilt++;
            }
            if (cache.Shown != level)
            {
                foreach (var inst in cache.Levels) if (inst != null) inst.Visible = false;
                cache.Levels[level]!.Visible = true; cache.Shown = level;
            }
        }
    }
    public override void _Process(double delta)
    {
        if (_system == null) return;
        var camera = CameraProvider?.Invoke() ?? GetViewport().GetCamera3D();
        if (camera != null) SettleFor(camera.GlobalPosition, camera.Fov, GetViewport().GetVisibleRect().Size.Y);
    }
}
