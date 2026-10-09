using System;
using System.Collections.Generic;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>
/// Reason-coded flags identifying what caused a spatial region to become dirty.
/// Allows fine-grained invalidation without indiscriminate whole-scene rebuilds.
/// </summary>
[Flags]
public enum DirtyReason : uint
{
    None = 0,
    FloraTransform = 1 << 0,     // Pose, position, yaw, pitch, scale
    FloraGeometry = 1 << 1,      // Stage transition, morph variant, fruiting change
    FloraAttributes = 1 << 2,    // Health, wobble, tint, custom shader params
    FloraPopulation = 1 << 3,    // Organism spawned, died, or despawned
    TerrainVersion = 1 << 4,     // Sculpting / ground elevation changed
    CoverageTile = 1 << 5,       // Moss, lichen, or plasmodium occupancy changed
    Litter = 1 << 6,             // Litter mass, composition, or color changed
    Moisture = 1 << 7,           // Hydration or water table changed
    Props = 1 << 8,              // Log or rock placed, moved, or degraded
    MaterialOrLighting = 1 << 9, // Environment uniforms or daylight changed
    All = ~0u
}

/// <summary>
/// 2D tile coordinate for spatial invalidation on the XZ ground plane.
/// </summary>
public readonly record struct TileCoord(int X, int Z)
{
    public override string ToString() => $"({X},{Z})";
}

/// <summary>
/// Dirty region record tracking coalesced invalidation reasons and latest source version for a tile.
/// </summary>
public sealed class DirtyRegionRecord
{
    public TileCoord Coord { get; }
    public Aabb Bounds { get; }
    public DirtyReason Reasons { get; private set; }
    public long LatestVersion { get; private set; }
    public double Timestamp { get; private set; }

    public DirtyRegionRecord(TileCoord coord, Aabb bounds, DirtyReason reason, long version, double timestamp)
    {
        Coord = coord;
        Bounds = bounds;
        Reasons = reason;
        LatestVersion = version;
        Timestamp = timestamp;
    }

    /// <summary>
    /// Latest-wins version coalescing: accumulates reasons and updates to newest version and timestamp.
    /// </summary>
    public void Coalesce(DirtyReason reason, long version, double timestamp)
    {
        Reasons |= reason;
        if (version > LatestVersion) LatestVersion = version;
        if (timestamp > Timestamp) Timestamp = timestamp;
    }

    public void ClearReasons(DirtyReason reasonsToClear)
    {
        Reasons &= ~reasonsToClear;
    }
}

/// <summary>
/// Tile-based spatial dirty tracking with reason codes and latest-wins version coalescing.
/// Decouples frequent per-frame updates from expensive geometry rebuilds.
/// </summary>
public sealed class RenderDirtyRegions
{
    public float TileSize { get; }
    private readonly Dictionary<TileCoord, DirtyRegionRecord> _records = new();
    private readonly List<DirtyRegionRecord> _scratchList = new();

    public int DirtyTileCount => _records.Count;
    public IReadOnlyCollection<DirtyRegionRecord> Records => _records.Values;

    public RenderDirtyRegions(float tileSize = 2.0f)
    {
        TileSize = Math.Max(0.5f, tileSize);
    }

    public TileCoord GetTileCoord(Vector3 worldPos) =>
        new((int)MathF.Floor(worldPos.X / TileSize), (int)MathF.Floor(worldPos.Z / TileSize));

    public TileCoord GetTileCoord(double x, double z) =>
        new((int)Math.Floor(x / TileSize), (int)Math.Floor(z / TileSize));

    public Aabb GetTileBounds(TileCoord coord, float minY = -5.0f, float maxY = 15.0f) =>
        new(new Vector3(coord.X * TileSize, minY, coord.Z * TileSize),
            new Vector3(TileSize, maxY - minY, TileSize));

    public void MarkTile(TileCoord coord, DirtyReason reason, long version = 0, double timestamp = 0)
    {
        if (reason == DirtyReason.None) return;
        if (_records.TryGetValue(coord, out var existing))
        {
            existing.Coalesce(reason, version, timestamp);
        }
        else
        {
            _records[coord] = new DirtyRegionRecord(coord, GetTileBounds(coord), reason, version, timestamp);
        }
    }

    public void MarkPoint(Vector3 position, float radius, DirtyReason reason, long version = 0, double timestamp = 0)
    {
        if (reason == DirtyReason.None) return;
        int minX = (int)MathF.Floor((position.X - radius) / TileSize);
        int maxX = (int)MathF.Floor((position.X + radius) / TileSize);
        int minZ = (int)MathF.Floor((position.Z - radius) / TileSize);
        int maxZ = (int)MathF.Floor((position.Z + radius) / TileSize);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                MarkTile(new TileCoord(x, z), reason, version, timestamp);
            }
        }
    }

    public void MarkBounds(Aabb aabb, DirtyReason reason, long version = 0, double timestamp = 0)
    {
        if (reason == DirtyReason.None) return;
        int minX = (int)MathF.Floor(aabb.Position.X / TileSize);
        int maxX = (int)MathF.Floor((aabb.Position.X + aabb.Size.X) / TileSize);
        int minZ = (int)MathF.Floor(aabb.Position.Z / TileSize);
        int maxZ = (int)MathF.Floor((aabb.Position.Z + aabb.Size.Z) / TileSize);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                MarkTile(new TileCoord(x, z), reason, version, timestamp);
            }
        }
    }

    public void MarkAll(DirtyReason reason, long version = 0, double timestamp = 0)
    {
        if (reason == DirtyReason.None) return;
        foreach (var rec in _records.Values)
        {
            rec.Coalesce(reason, version, timestamp);
        }
    }

    public bool IsDirty(TileCoord coord, DirtyReason reasons = DirtyReason.All)
    {
        return _records.TryGetValue(coord, out var rec) && (rec.Reasons & reasons) != 0;
    }

    public bool IsRegionDirty(Aabb bounds, DirtyReason reasons = DirtyReason.All)
    {
        int minX = (int)MathF.Floor(bounds.Position.X / TileSize);
        int maxX = (int)MathF.Floor((bounds.Position.X + bounds.Size.X) / TileSize);
        int minZ = (int)MathF.Floor(bounds.Position.Z / TileSize);
        int maxZ = (int)MathF.Floor((bounds.Position.Z + bounds.Size.Z) / TileSize);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsDirty(new TileCoord(x, z), reasons)) return true;
            }
        }
        return false;
    }

    public bool ClearTile(TileCoord coord) => _records.Remove(coord);

    public void Clear() => _records.Clear();

    /// <summary>
    /// Orders dirty tiles by priority per Section 4.6:
    /// camera-visible > near camera > recently altered > background.
    /// </summary>
    public IReadOnlyList<DirtyRegionRecord> GetPrioritizedTiles(Vector3 cameraPos, Plane[]? frustum = null)
    {
        _scratchList.Clear();
        _scratchList.AddRange(_records.Values);

        _scratchList.Sort((a, b) =>
        {
            // Visibility check
            bool aVis = frustum == null || IsBoundsInFrustum(frustum, a.Bounds);
            bool bVis = frustum == null || IsBoundsInFrustum(frustum, b.Bounds);

            if (aVis != bVis) return aVis ? -1 : 1; // Visible first

            // Distance to camera (closer first)
            Vector3 aCenter = a.Bounds.Position + a.Bounds.Size * 0.5f;
            Vector3 bCenter = b.Bounds.Position + b.Bounds.Size * 0.5f;
            float aDistSq = aCenter.DistanceSquaredTo(cameraPos);
            float bDistSq = bCenter.DistanceSquaredTo(cameraPos);

            int distCmp = aDistSq.CompareTo(bDistSq);
            if (distCmp != 0) return distCmp;

            // Recency (more recent first)
            return b.Timestamp.CompareTo(a.Timestamp);
        });

        return _scratchList;
    }

    private static bool IsBoundsInFrustum(Plane[] frustum, Aabb bounds)
    {
        Vector3 center = bounds.Position + bounds.Size * 0.5f;
        float radius = bounds.Size.Length() * 0.5f;
        foreach (var plane in frustum)
        {
            if (plane.DistanceTo(center) > radius) return false;
        }
        return true;
    }
}
