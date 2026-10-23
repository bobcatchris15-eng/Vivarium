using System;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using SimVec2 = Vivarium.Sim.Core.Vec2;

namespace Vivarium.Game.App;

/// <summary>Deterministic render-only undercut soil peds for bounded loam material reviews.</summary>
public static class LoamAggregateBuilder
{
    private const float BoardHalfExtent = 2.5f;
    private const int PatchCount = 11;
    private const int MaxAggregates = 180;
    // 180 peds x (144 shell + 2-4 x 40 micro-crumbs) never reaches this cap, so density is unchanged.
    private const int MaxTriangles = 64000;

    // Tessellation: 16 radial segments x 5 vertical rings = 4 quad bands (128 triangles) plus a
    // 16-triangle crown fan = exactly 144 triangles per ped.
    private const int PedSegments = 16;
    private const int PedRings = 5;
    private const int PedTriangles = 144;

    // Nested satellite micro-crumbs: 8 radial segments x 3 vertical rings = 2 quad bands (32 tris)
    // plus an 8-triangle crown fan = 40 triangles each.
    private const int CrumbSegments = 8;
    private const int CrumbRings = 3;
    private const int CrumbTriangles = 40;
    private const int MaxCrumbCount = 4;

    // The lowest ring tucks inward to 0.72 r two millimetres below the soil surface, and the ring
    // immediately above it flares back out to the full radius: every clod carries a true 3D
    // overhanging basal crevice instead of a flat skirt sitting on the ground.
    private const float UndercutWaistRadius = 0.72f;
    private const float UndercutWaistDepth = 0.0020f;
    private const float SkirtLatitude = 0.12f;      // lip height as a fraction of crown height
    private const float CrownLatitude = 0.88f;      // last ring before the crown pole

    // Baked basal cavity occlusion carried in COLOR.a: darkest in the waist, open at the crown.
    private static readonly float[] PedCavityOcclusion = { 0.25f, 0.52f, 0.78f, 0.94f, 1.00f };
    private static readonly float[] CrumbCavityOcclusion = { 0.25f, 0.62f, 1.00f };

    /// <summary>Builds clustered, partly buried soil peds over a 5 m XZ board without changing world state.</summary>
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
        var material = Material();

        int aggregates = 0, triangles = 0, patchesPlaced = 0;
        int clodBudget = PedTriangles + MaxCrumbCount * CrumbTriangles;
        for (int patch = 0; patch < PatchCount && aggregates < MaxAggregates && triangles + clodBudget < MaxTriangles; patch++)
        {
            var anchor = new SimVec2(origin.X + rng.Range(-halfExtent * 0.77f, halfExtent * 0.77f), origin.Y + rng.Range(-halfExtent * 0.77f, halfExtent * 0.77f));
            if (!Eligible(world, anchor, 0.12, origin, halfExtent)) continue;
            patchesPlaced++;
            float axis = rng.Range(0f, Mathf.Tau);
            float patchScale = Math.Min(1f, halfExtent / BoardHalfExtent);
            float length = rng.Range(0.22f, 0.48f) * patchScale;
            float width = rng.Range(0.12f, 0.30f) * patchScale;
            int count = rng.NextInt(8, 17);
            for (int i = 0; i < count && aggregates < MaxAggregates && triangles + clodBudget < MaxTriangles; i++)
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
        root.SetMeta("aggregate_segments", PedSegments);
        root.SetMeta("aggregate_rings", PedRings);
        root.SetMeta("triangle_count", triangles);
        root.SetMeta("triangle_cap", MaxTriangles);
        if (aggregates > 0)
        {
            var mesh = meshBuilder.Commit();
            mesh.SurfaceSetMaterial(0, material);
            root.AddChild(new MeshInstance3D
            {
                Name = "UndercutLoamPeds",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
        }
        return root;
    }

    /// <summary>Ground048 scan bound with normal, height and AO so the clod shell reads as mineral soil.</summary>
    private static ShaderMaterial Material()
    {
        var material = Bridge.Shader("res://Shaders/loam_aggregate.gdshader");
        var surface = Bridge.ResolveSurfaceSet(Bridge.Surfaces.Soil);
        Bridge.BindSurface(material, "soil", surface);
        material.SetShaderParameter("soil_texture_tile_metres", 0.82f);
        material.SetShaderParameter("soil_tint", new Vector3(0.65f, 0.78f, 0.90f));
        material.SetShaderParameter("soil_specular", 0.22f);
        // Contact parallax only when a displacement map exists; otherwise the raymarch would shear.
        material.SetShaderParameter("soil_relief_metres", 0.0035f);
        material.SetShaderParameter("soil_parallax", string.IsNullOrEmpty(surface.Height) ? 0f : 1f);
        return material;
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
        float crownHeight = radius * rng.Range(0.34f, 0.58f);
        AddSpheroid(mesh, world, center, radius, crownHeight, rng.Range(0.82f, 1.26f), rng.Range(0f, Mathf.Tau),
            PedSegments, PedRings, PedCavityOcclusion, ref rng, ref triangleCount);

        // 2-4 satellite crumbs nest around the base so peds read as clustered aggregates, not domes.
        int crumbs = rng.NextInt(2, MaxCrumbCount);
        for (int i = 0; i < crumbs; i++)
        {
            float a = rng.Range(0f, Mathf.Tau);
            float distance = radius * rng.Range(0.58f, 1.00f);
            var crumbCenter = new SimVec2(center.X + Math.Cos(a) * distance, center.Z + Math.Sin(a) * distance);
            float crumbRadius = radius * rng.Range(0.16f, 0.34f);
            AddSpheroid(mesh, world, crumbCenter, crumbRadius, crumbRadius * rng.Range(0.38f, 0.62f),
                rng.Range(0.80f, 1.22f), rng.Range(0f, Mathf.Tau),
                CrumbSegments, CrumbRings, CrumbCavityOcclusion, ref rng, ref triangleCount);
        }
    }

    /// <summary>
    /// One displaced spheroidal ped: a spheroid shell truncated below the soil surface whose lowest
    /// ring is tucked to 0.72 r, producing an overhanging crevice under the flared skirt.
    /// </summary>
    private static void AddSpheroid(SurfaceTool mesh, VivariumWorld world, SimVec2 center, float radius,
        float crownHeight, float stretch, float yaw, int segments, int rings, float[] cavity,
        ref StableRandom rng, ref int triangleCount)
    {
        float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
        float tint = rng.Range(0.84f, 1.00f);
        float step = Mathf.Tau / segments;

        // One azimuthal noise set per clod, shared by every ring, keeps the silhouette continuous.
        float[] radialNoise = new float[segments], angleNoise = new float[segments];
        for (int i = 0; i < segments; i++)
        {
            radialNoise[i] = rng.Range(0.88f, 1.12f);
            angleNoise[i] = rng.Range(-0.20f, 0.20f) * step;
        }

        float[] ringY = new float[rings], ringRadius = new float[rings];
        ringY[0] = -UndercutWaistDepth;
        ringRadius[0] = UndercutWaistRadius;
        int spans = Math.Max(1, rings - 2);
        for (int r = 1; r < rings; r++)
        {
            float t = (r - 1) / (float)spans;
            float sine = Mathf.Lerp(SkirtLatitude, CrownLatitude, t);
            ringY[r] = crownHeight * sine;
            ringRadius[r] = Mathf.Sqrt(Mathf.Max(0.02f, 1f - sine * sine));
        }
        // Keep the crevice open on millimetre crumbs: the flared lip never sits below ~1 mm of soil.
        ringY[1] = Mathf.Max(ringY[1], Mathf.Min(0.0012f, crownHeight * 0.30f));

        var position = new Vector3[rings * segments];
        for (int r = 0; r < rings; r++)
        for (int i = 0; i < segments; i++)
        {
            float a = step * i + angleNoise[i];
            float radial = radius * ringRadius[r] * radialNoise[i];
            float lx = Mathf.Cos(a) * radial * stretch;
            float lz = Mathf.Sin(a) * radial / stretch;
            float x = (float)center.X + c * lx - s * lz;
            float z = (float)center.Z + s * lx + c * lz;
            // Every ring follows the local soil surface, so the buried waist stays buried on slopes.
            float ground = (float)world.Terrain.Height(new SimVec2(x, z));
            position[r * segments + i] = new Vector3(x, ground + ringY[r], z);
        }

        float crownAngle = rng.Range(0f, Mathf.Tau);
        float crownOffset = radius * rng.Range(0.04f, 0.14f);
        float crownX = (float)center.X + Mathf.Cos(crownAngle) * crownOffset;
        float crownZ = (float)center.Z + Mathf.Sin(crownAngle) * crownOffset;
        var crown = new Vector3(crownX,
            (float)world.Terrain.Height(new SimVec2(crownX, crownZ)) + crownHeight, crownZ);

        // Smooth normals from every band, plus a band-0-only set that creases the skirt lip open.
        var axis = new Vector3((float)center.X, 0f, (float)center.Z);
        var smooth = new Vector3[rings * segments];
        var skirt = new Vector3[rings * segments];
        var crownNormalAcc = Vector3.Zero;

        for (int r = 0; r < rings - 1; r++)
        for (int i = 0; i < segments; i++)
        {
            int i1 = (i + 1) % segments;
            int v00 = r * segments + i, v10 = r * segments + i1;
            int v01 = (r + 1) * segments + i, v11 = (r + 1) * segments + i1;
            Vector3 p00 = position[v00], p10 = position[v10], p01 = position[v01], p11 = position[v11];
            Vector3 n0 = Outward(p00, p10, p11, axis);
            Vector3 n1 = Outward(p00, p11, p01, axis);
            Accumulate(smooth, v00, n0); Accumulate(smooth, v10, n0); Accumulate(smooth, v11, n0);
            Accumulate(smooth, v00, n1); Accumulate(smooth, v11, n1); Accumulate(smooth, v01, n1);
            if (r != 0) continue;
            Accumulate(skirt, v00, n0); Accumulate(skirt, v10, n0); Accumulate(skirt, v11, n0);
            Accumulate(skirt, v00, n1); Accumulate(skirt, v11, n1); Accumulate(skirt, v01, n1);
        }

        int top = (rings - 1) * segments;
        for (int i = 0; i < segments; i++)
        {
            int i1 = (i + 1) % segments;
            Vector3 n = Outward(position[top + i], position[top + i1], crown, axis);
            Accumulate(smooth, top + i, n);
            Accumulate(smooth, top + i1, n);
            crownNormalAcc += n;
        }

        for (int i = 0; i < smooth.Length; i++) smooth[i] = NormalizedOr(smooth[i], Vector3.Up);
        for (int i = 0; i < skirt.Length; i++) skirt[i] = NormalizedOr(skirt[i], Vector3.Down);
        Vector3 crownNormal = NormalizedOr(crownNormalAcc, Vector3.Up);

        for (int r = 0; r < rings - 1; r++)
        for (int i = 0; i < segments; i++)
        {
            int i1 = (i + 1) % segments;
            int v00 = r * segments + i, v10 = r * segments + i1;
            int v01 = (r + 1) * segments + i, v11 = (r + 1) * segments + i1;
            // The basal band creases hard along the skirt lip; every other band shares the smooth normal.
            Vector3 n00 = r == 0 ? skirt[v00] : smooth[v00];
            Vector3 n10 = r == 0 ? skirt[v10] : smooth[v10];
            Vector3 n01 = r == 0 ? skirt[v01] : smooth[v01];
            Vector3 n11 = r == 0 ? skirt[v11] : smooth[v11];
            Color c00 = new(tint, tint, tint, cavity[r]);
            Color c10 = new(tint, tint, tint, cavity[r]);
            Color c01 = new(tint, tint, tint, cavity[r + 1]);
            Color c11 = new(tint, tint, tint, cavity[r + 1]);
            AddTriangle(mesh, position[v00], position[v10], position[v11], n00, n10, n11, c00, c10, c11);
            AddTriangle(mesh, position[v00], position[v11], position[v01], n00, n11, n01, c00, c11, c01);
            triangleCount += 2;
        }

        Color crownColor = new(tint, tint, tint, cavity[rings - 1]);
        for (int i = 0; i < segments; i++)
        {
            int i1 = (i + 1) % segments;
            int v0 = top + i, v1 = top + i1;
            Color c0 = new(tint, tint, tint, cavity[rings - 1]);
            AddTriangle(mesh, position[v0], position[v1], crown, smooth[v0], smooth[v1], crownNormal, c0, c0, crownColor);
            triangleCount++;
        }
    }

    /// <summary>Right-handed face normal flipped so it points away from the clod axis.</summary>
    private static Vector3 Outward(Vector3 a, Vector3 b, Vector3 d, Vector3 axis)
    {
        Vector3 n = (b - a).Cross(d - a);
        Vector3 radial = (a + b + d) * (1f / 3f) - axis;
        radial.Y = 0f;
        return n.Dot(radial) < 0f ? -n : n;
    }

    private static void Accumulate(Vector3[] target, int index, Vector3 normal) => target[index] += normal;

    private static Vector3 NormalizedOr(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-20f ? v.Normalized() : fallback;

    private static void AddTriangle(SurfaceTool mesh, Vector3 a, Vector3 b, Vector3 c,
        Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc)
    {
        // Godot front faces are clockwise: the right-handed face normal must oppose the outward
        // vertex normals. This is exactly what LoamGroundReview validates on the uploaded mesh.
        if ((b - a).Cross(c - a).Dot(na + nb + nc) > 0f)
            ((b, nb, cb), (c, nc, cc)) = ((c, nc, cc), (b, nb, cb));
        AddVertex(mesh, a, na, ca);
        AddVertex(mesh, b, nb, cb);
        AddVertex(mesh, c, nc, cc);
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
