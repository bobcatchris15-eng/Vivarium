using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;

namespace Vivarium.Game.Render;

public partial class FloraRenderer
{
    private static bool IsContactHostShape(FloraSpeciesDef species) =>
        species.Woody != null && species.Shape is ("moonbark" or "shiverleaf" or "fenforge");

    /// <summary>
    /// Projects an attached climber centerline onto the host's published structural mesh. The ray is
    /// expressed in the host's actual rendered local frame, so nonuniform growth scaling and host lean
    /// are naturally accounted for. A miss leaves the simulation point intact.
    /// </summary>
    private bool TryProjectClimberNodeToHost(FloraSnapshotItem host, FloraSpeciesDef hostSpecies,
        ClimberSegmentSnapshot segment, out Vector3 projectedPosition, out Vector3 projectedNormal)
    {
        projectedPosition = default;
        projectedNormal = default;
        if (!IsContactHostShape(hostSpecies) || !_layers.TryGetValue(hostSpecies.Id, out var layer)) return false;

        int terrainVersion = _w.Terrain.Version, propsVersion = _w.Props.Version;
        long tick = _w.Clock.Tick;
        if (!_placements.TryGetValue(host.Id, out var placement)
            || placement.Def.Id != host.SpeciesId
            || placement.Position.X != host.X || placement.Position.Z != host.Z
            || placement.TerrainVersion != terrainVersion || placement.PropsVersion != propsVersion
            || tick - placement.SampledTick >= PlacementResampleTicks)
        {
            placement = SamplePlantPlacement(host, hostSpecies, terrainVersion, propsVersion, tick);
            _placements[host.Id] = placement;
        }

        double radius = host.Radius(hostSpecies);
        double height = hostSpecies.Height * (0.45 + 0.55 * Math.Sqrt(host.BiomassFraction(hostSpecies)));
        double stress = MathD.Clamp01((1.0 - host.Health) * 0.7 + Math.Max(0, 0.32 - placement.LocalMoisture) * 0.55);
        height *= 0.93 + 0.07 * MathD.Clamp01(0.55 * host.Health + 0.45 * Math.Min(1, placement.LocalMoisture / 0.45));
        radius *= 1.0 + stress * 0.035;
        var hostPosition = new Vector3((float)host.X, placement.GroundY, (float)host.Z);
        var hash = Rng.Mix(host.Id.Value, 0xF10);
        float yaw = (hash % 6283) / 1000f;
        var yawBasis = placement.UprightBasis * new Basis(Vector3.Up, yaw);
        int tier = VisualTier(host.Id, hostPosition, (float)height, (float)radius);
        int morph = (int)((hash >> 8) % (ulong)layer.MorphCount);
        int variantIndex = tier * layer.MorphCount + morph;
        if ((uint)variantIndex >= (uint)layer.Variants.Length) return false;
        var surface = layer.Variants[variantIndex]?.StructuralSurface
            ?? layer.Variants[morph]?.StructuralSurface;
        if (surface == null) return false;

        var transform = new Transform3D(yawBasis.ScaledLocal(new Vector3((float)radius, (float)height, (float)radius)), hostPosition);
        var inverse = transform.AffineInverse();
        var worldNormal = new Vector3((float)segment.Normal.X, (float)segment.Normal.Y, (float)segment.Normal.Z);
        if (worldNormal.LengthSquared() < 1e-8f) return false;
        worldNormal = worldNormal.Normalized();
        var worldPoint = new Vector3((float)segment.Position.X, (float)segment.Position.Y, (float)segment.Position.Z);
        // Begin slightly outside the node and cast back through it toward the host. The bounded
        // interval covers the measured support/render mismatch while avoiding canopy-wide searches.
        var rayOrigin = inverse * (worldPoint + worldNormal * 0.12f);
        var rayDirection = inverse.Basis * -worldNormal;
        bool fallback = false;
        Vector3 localPoint, localNormal;
        if (!surface.TryIntersect(rayOrigin, rayDirection, 0.24f, out localPoint, out localNormal))
        {
            // Fallback: cast horizontally toward the host axis (host local Y) from outside, so a node that is
            // off the bark or whose normal misses still lands on the nearest bark. Bounded so a vine never
            // jumps to a far tree: the node must lie within ~2.5 host radii of the axis (local units).
            var lp = inverse * worldPoint;
            var radial = new Vector3(lp.X, 0, lp.Z);
            float rl = radial.Length();
            if (rl > 1.8f || rl < 1e-4f) return false;
            var outDir = radial / rl;
            var origin = new Vector3(lp.X, lp.Y, lp.Z) + outDir * (3.0f - rl);
            if (!surface.TryIntersect(origin, -outDir, 3.0f, out localPoint, out localNormal)) return false;
            if (localNormal.Dot(outDir) < 0) localNormal = -localNormal;
            fallback = true;
        }

        var hit = transform * localPoint;
        var normalMatrix = inverse.Basis.Transposed();
        var hitNormal = (normalMatrix * localNormal).Normalized();
        if (hitNormal.LengthSquared() < 1e-8f) return false;
        if (!fallback && hitNormal.Dot(worldNormal) < 0) hitNormal = -hitNormal;
        if (fallback && (hit - worldPoint).Length() > 0.05f) return false;
        projectedPosition = hit + hitNormal * 0.004f;
        projectedNormal = hitNormal;
        return true;
    }

    /// <summary>Immutable structural-triangle lookup, built once from the same near-variant MeshData uploaded for the host.</summary>
    private sealed class StructuralSurfaceIndex
    {
        private readonly record struct Cell(int X, int Y, int Z);
        private readonly struct Triangle
        {
            public readonly Vector3 A, B, C, Normal;
            public Triangle(Vector3 a, Vector3 b, Vector3 c)
            { A = a; B = b; C = c; Normal = (b - a).Cross(c - a).Normalized(); }
        }

        private const float CellSize = 0.09f;
        private readonly Triangle[] _triangles;
        private readonly Dictionary<Cell, int[]> _cells;
        private readonly int[] _seen;
        private int _queryStamp;

        private StructuralSurfaceIndex(Triangle[] triangles, Dictionary<Cell, int[]> cells)
        { _triangles = triangles; _cells = cells; _seen = new int[triangles.Length]; }

        public static StructuralSurfaceIndex Build(MeshData mesh)
        {
            var structural = new bool[mesh.VertexCount];
            foreach (var span in mesh.Structural)
            {
                int end = Math.Min(mesh.VertexCount, span.FirstVertex + span.VertexCount);
                for (int i = Math.Max(0, span.FirstVertex); i < end; i++) structural[i] = true;
            }

            var triangles = new List<Triangle>();
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int ia = mesh.Indices[i], ib = mesh.Indices[i + 1], ic = mesh.Indices[i + 2];
                if ((uint)ia >= (uint)structural.Length || (uint)ib >= (uint)structural.Length || (uint)ic >= (uint)structural.Length
                    || !structural[ia] || !structural[ib] || !structural[ic]) continue;
                var a = new Vector3(mesh.Positions[ia * 3], mesh.Positions[ia * 3 + 1], mesh.Positions[ia * 3 + 2]);
                var b = new Vector3(mesh.Positions[ib * 3], mesh.Positions[ib * 3 + 1], mesh.Positions[ib * 3 + 2]);
                var c = new Vector3(mesh.Positions[ic * 3], mesh.Positions[ic * 3 + 1], mesh.Positions[ic * 3 + 2]);
                if ((b - a).Cross(c - a).LengthSquared() > 1e-12f) triangles.Add(new Triangle(a, b, c));
            }

            var mutable = new Dictionary<Cell, List<int>>();
            for (int i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                var min = new Vector3(Mathf.Min(tri.A.X, Mathf.Min(tri.B.X, tri.C.X)),
                    Mathf.Min(tri.A.Y, Mathf.Min(tri.B.Y, tri.C.Y)), Mathf.Min(tri.A.Z, Mathf.Min(tri.B.Z, tri.C.Z)));
                var max = new Vector3(Mathf.Max(tri.A.X, Mathf.Max(tri.B.X, tri.C.X)),
                    Mathf.Max(tri.A.Y, Mathf.Max(tri.B.Y, tri.C.Y)), Mathf.Max(tri.A.Z, Mathf.Max(tri.B.Z, tri.C.Z)));
                int x0 = Floor(min.X), x1 = Floor(max.X), y0 = Floor(min.Y), y1 = Floor(max.Y), z0 = Floor(min.Z), z1 = Floor(max.Z);
                long bucketCount = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
                if (bucketCount > 128) continue; // Skip anomalously broad structural faces; other spans still provide coverage.
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++)
                {
                    var key = new Cell(x, y, z);
                    if (!mutable.TryGetValue(key, out var list)) mutable[key] = list = new List<int>(8);
                    list.Add(i);
                }
            }
            var cells = new Dictionary<Cell, int[]>(mutable.Count);
            foreach (var pair in mutable) cells.Add(pair.Key, pair.Value.ToArray());
            return new StructuralSurfaceIndex(triangles.ToArray(), cells);
        }

        public bool TryIntersect(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = default;
            if (!origin.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < 1e-12f) return false;
            if (++_queryStamp == int.MaxValue) { Array.Clear(_seen, 0, _seen.Length); _queryStamp = 1; }
            float closest = maxDistance;
            int stamp = _queryStamp;
            int x = Floor(origin.X), y = Floor(origin.Y), z = Floor(origin.Z);
            int stepX = Math.Sign(direction.X), stepY = Math.Sign(direction.Y), stepZ = Math.Sign(direction.Z);
            float tMaxX = FirstBoundary(origin.X, direction.X, x, stepX);
            float tMaxY = FirstBoundary(origin.Y, direction.Y, y, stepY);
            float tMaxZ = FirstBoundary(origin.Z, direction.Z, z, stepZ);
            float tDeltaX = stepX == 0 ? float.PositiveInfinity : CellSize / Math.Abs(direction.X);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : CellSize / Math.Abs(direction.Y);
            float tDeltaZ = stepZ == 0 ? float.PositiveInfinity : CellSize / Math.Abs(direction.Z);
            for (float traversed = 0; traversed <= maxDistance;)
            {
                if (_cells.TryGetValue(new Cell(x, y, z), out var candidates))
                {
                    foreach (int index in candidates)
                    {
                        if (_seen[index] == stamp) continue;
                        _seen[index] = stamp;
                        var tri = _triangles[index];
                        if (RayTriangle(origin, direction, tri, out float distance) && distance >= 0 && distance < closest)
                        {
                            closest = distance; point = origin + direction * distance; normal = tri.Normal;
                        }
                    }
                }
                float next = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
                if (next > maxDistance || !float.IsFinite(next)) break;
                const float tieTolerance = 1e-6f;
                if (tMaxX <= next + tieTolerance) { x += stepX; tMaxX += tDeltaX; }
                if (tMaxY <= next + tieTolerance) { y += stepY; tMaxY += tDeltaY; }
                if (tMaxZ <= next + tieTolerance) { z += stepZ; tMaxZ += tDeltaZ; }
                traversed = next;
            }
            return closest < maxDistance;
        }

        private static bool RayTriangle(Vector3 origin, Vector3 direction, Triangle tri, out float distance)
        {
            distance = 0;
            var edge1 = tri.B - tri.A; var edge2 = tri.C - tri.A;
            var p = direction.Cross(edge2); float det = edge1.Dot(p);
            if (Mathf.Abs(det) < 1e-8f) return false;
            float inv = 1f / det;
            var t = origin - tri.A; float u = t.Dot(p) * inv;
            if (u < 0 || u > 1) return false;
            var q = t.Cross(edge1); float v = direction.Dot(q) * inv;
            if (v < 0 || u + v > 1) return false;
            distance = edge2.Dot(q) * inv;
            return distance >= 0;
        }

        private static int Floor(float value) => Mathf.FloorToInt(value / CellSize);
        private static float FirstBoundary(float origin, float direction, int cell, int step)
        {
            if (step == 0) return float.PositiveInfinity;
            float boundary = (cell + (step > 0 ? 1 : 0)) * CellSize;
            return Math.Max(0, (boundary - origin) / direction);
        }
    }
}
