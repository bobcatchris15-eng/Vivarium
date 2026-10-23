using System;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using SimVec2 = Vivarium.Sim.Core.Vec2;

namespace Vivarium.Game.App;

/// <summary>Deterministic render-only irregular soil aggregates for bounded loam material reviews.</summary>
public static class LoamAggregateBuilder
{
    private const float BoardHalfExtent = 2.5f;
    private const int PatchCount = 11;
    private const int MaxAggregates = 180;
    private const int MaxTriangles = 7200;

    /// <summary>Builds clustered, partly buried soil crumbs over a 5 m XZ board without changing world state.</summary>
    public static Node3D Build(VivariumWorld world, int seed, Vector2? patchCenter = null, float halfExtent = BoardHalfExtent)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!float.IsFinite(halfExtent) || halfExtent <= 0f || halfExtent > 2.5f)
            throw new ArgumentOutOfRangeException(nameof(halfExtent), "halfExtent must be finite and between 0 and 2.5 metres");
        Vector2 origin = patchCenter ?? Vector2.Zero;
        var root = new Node3D { Name = "LoamAggregates" };
        var rng = new StableRandom(unchecked((ulong)(uint)seed) + 0xD1B54A32D192ED03UL);
        var meshBuilder = new SurfaceTool();
        meshBuilder.Begin(Mesh.PrimitiveType.Triangles);
        var material = Bridge.Shader("res://Shaders/loam_aggregate.gdshader");
        Bridge.BindSurface(material, "soil", Bridge.Surfaces.Soil);
        material.SetShaderParameter("soil_texture_tile_metres", 0.82f);
        material.SetShaderParameter("soil_tint", new Vector3(0.65f, 0.78f, 0.90f));
        material.SetShaderParameter("soil_specular", 0.22f);

        int aggregates = 0, triangles = 0, patchesPlaced = 0;
        for (int patch = 0; patch < PatchCount && aggregates < MaxAggregates && triangles < MaxTriangles; patch++)
        {
            var anchor = new SimVec2(origin.X + rng.Range(-halfExtent * 0.77f, halfExtent * 0.77f), origin.Y + rng.Range(-halfExtent * 0.77f, halfExtent * 0.77f));
            if (!Eligible(world, anchor, 0.12, origin, halfExtent)) continue;
            patchesPlaced++;
            float axis = rng.Range(0f, Mathf.Tau);
            float patchScale = Math.Min(1f, halfExtent / BoardHalfExtent);
            float length = rng.Range(0.22f, 0.48f) * patchScale;
            float width = rng.Range(0.12f, 0.30f) * patchScale;
            int count = rng.NextInt(8, 17);
            for (int i = 0; i < count && aggregates < MaxAggregates && triangles + 40 < MaxTriangles; i++)
            {
                // Elliptical patch placement with denser inner mass and uneven gaps at the edges.
                float r = Mathf.Sqrt(rng.NextFloat()) * rng.Range(0.36f, 1.06f);
                float a = rng.Range(0f, Mathf.Tau);
                float localX = Mathf.Cos(a) * length * r;
                float localZ = Mathf.Sin(a) * width * r;
                double x = anchor.X + Math.Cos(axis) * localX - Math.Sin(axis) * localZ;
                double z = anchor.Z + Math.Sin(axis) * localX + Math.Cos(axis) * localZ;
                var center = new SimVec2(x, z);
                float radius = rng.NextFloat() < 0.16f ? rng.Range(0.035f, 0.050f) : rng.Range(0.008f, 0.035f);
                if (!Eligible(world, center, radius * 1.7, origin, halfExtent)) continue;
                AddAggregate(meshBuilder, world, center, radius, ref rng, ref triangles);
                aggregates++;
            }
        }

        root.SetMeta("seed", seed);
        root.SetMeta("board_size_m", halfExtent * 2f);
        root.SetMeta("patch_center_xz", origin);
        root.SetMeta("patch_count", patchesPlaced);
        root.SetMeta("aggregate_count", aggregates);
        root.SetMeta("aggregate_cap", MaxAggregates);
        root.SetMeta("triangle_count", triangles);
        root.SetMeta("triangle_cap", MaxTriangles);
        if (aggregates > 0)
        {
            var mesh = meshBuilder.Commit();
            mesh.SurfaceSetMaterial(0, material);
            root.AddChild(new MeshInstance3D
            {
                Name = "AngularLoamCrumbs",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
        }
        return root;
    }

    private static bool Eligible(VivariumWorld world, SimVec2 p, double radius, Vector2 origin, float halfExtent)
    {
        if (Math.Abs(p.X - origin.X) > halfExtent - radius || Math.Abs(p.Z - origin.Y) > halfExtent - radius ||
            !world.Domain.ContainsDisc(p, radius) || world.Water.IsWet(p) || world.SubstrateAt(p) != Substrate.Soil)
            return false;
        int cell = world.Grid.NearestDomainCell(p);
        if (cell >= 0 && world.AmbientGroundCover.Cover[cell] >= 0.35) return false;
        return world.PilotTree?.BlocksDisc(p, radius) != true && world.Props.RockAt(p) == null &&
            world.Props.LogAt(p) == null && world.Props.GravelAt(p) == null;
    }

    private static void AddAggregate(SurfaceTool mesh, VivariumWorld world, SimVec2 center, float radius,
        ref StableRandom rng, ref int triangleCount)
    {
        int sides = rng.NextInt(7, 10);
        float rotation = rng.Range(0f, Mathf.Tau);
        float stretch = rng.Range(0.72f, 1.35f);
        float yaw = rng.Range(0f, Mathf.Tau);
        float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
        float[] ringX = new float[sides], ringZ = new float[sides], ringY = new float[sides];
        float[] groundY = new float[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = rotation + Mathf.Tau * i / sides;
            float radial = radius * rng.Range(0.70f, 1.22f);
            float lx = Mathf.Cos(a) * radial * stretch;
            float lz = Mathf.Sin(a) * radial / stretch;
            ringX[i] = c * lx - s * lz;
            ringZ[i] = s * lx + c * lz;
            double wx = center.X + ringX[i], wz = center.Z + ringZ[i];
            groundY[i] = (float)world.Terrain.Height(new SimVec2(wx, wz));
            // The perimeter remains embedded; broken facets emerge gradually toward an off-centre crown.
            ringY[i] = groundY[i] + rng.Range(0.0012f, 0.0036f);
        }
        float crownOffsetX = radius * rng.Range(-0.24f, 0.30f);
        float crownOffsetZ = radius * rng.Range(-0.24f, 0.30f);
        float crownHeight = rng.Range(0.002f, 0.009f);
        var crownXZ = new SimVec2(center.X + crownOffsetX, center.Z + crownOffsetZ);
        float crownY = (float)world.Terrain.Height(crownXZ) + crownHeight;
        var crown = new Vector3((float)crownXZ.X, crownY, (float)crownXZ.Z);
        Color tint = new(rng.Range(0.80f, 1.06f), rng.Range(0.80f, 1.05f), rng.Range(0.78f, 1.02f));

        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            var p0 = new Vector3((float)(center.X + ringX[i]), ringY[i], (float)(center.Z + ringZ[i]));
            var p1 = new Vector3((float)(center.X + ringX[j]), ringY[j], (float)(center.Z + ringZ[j]));
            Vector3 normal = (p1 - p0).Cross(crown - p0).Normalized();
            if (normal.Y < 0) normal = -normal;
            AddTriangle(mesh, p0, p1, crown, normal, tint);
            triangleCount++;

            // A short irregular skirt disappears into the soil and supplies contact shadow at the edge.
            var b0 = new Vector3(p0.X, groundY[i] - 0.006f, p0.Z);
            var b1 = new Vector3(p1.X, groundY[j] - 0.006f, p1.Z);
            Vector3 sideNormal = (p1 - p0).Cross(b0 - p0).Normalized();
            AddTriangle(mesh, p0, b0, b1, sideNormal, tint * new Color(0.78f, 0.78f, 0.78f));
            AddTriangle(mesh, p0, b1, p1, sideNormal, tint * new Color(0.78f, 0.78f, 0.78f));
            triangleCount += 2;
        }
    }

    private static void AddTriangle(SurfaceTool mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color tint)
    {
        if ((b - a).Cross(c - a).Dot(normal) > 0f) (b, c) = (c, b);
        AddVertex(mesh, a, normal, tint);
        AddVertex(mesh, b, normal, tint);
        AddVertex(mesh, c, normal, tint);
    }

    private static void AddVertex(SurfaceTool mesh, Vector3 p, Vector3 normal, Color tint)
    {
        mesh.SetNormal(normal);
        mesh.SetColor(tint);
        mesh.SetUV(new Vector2(p.X, p.Z));
        mesh.AddVertex(p);
    }

    private struct StableRandom
    {
        private ulong _state;
        public StableRandom(ulong seed) => _state = seed == 0 ? 0xA0761D6478BD642FUL : seed;
        private uint Next()
        {
            _state ^= _state << 13; _state ^= _state >> 7; _state ^= _state << 17;
            return (uint)(_state >> 32);
        }
        public float NextFloat() => Next() * (1f / 4294967296f);
        public float Range(float min, float max) => min + (max - min) * NextFloat();
        public int NextInt(int min, int max) => min + (int)(Next() % (uint)(max - min + 1));
    }
}


