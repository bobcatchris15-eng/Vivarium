using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using SimVec2 = Vivarium.Sim.Core.Vec2;

namespace Vivarium.Game.App;

/// <summary>
/// Deterministic render-only leaf litter for bounded material/look fixtures. It reads terrain and domain
/// state but does not add organisms, alter litter mass, or write to the world.
/// </summary>
public static class LoamLitterBuilder
{
    private const float BoardHalfExtent = 2.5f;
    private const float NominalPerSquareMetre = 120f;
    private const int HardLeafCap = 5000;
    private const int HardDuffCap = 6000;

    /// <summary>Builds a seeded 5 m board of curled leaves, decomposed fragments, and sparse fine twigs.</summary>
    public static Node3D Build(VivariumWorld world, int seed, float density = 1f, int detailStage = 2,
        Vector2? patchCenter = null, float halfExtent = BoardHalfExtent, bool respectLitterMass = false)
    {
        Node3D? result = null;
        foreach (bool _ in BuildBatched(world, seed, node => result = node, density, detailStage,
                     patchCenter, halfExtent, respectLitterMass)) { }
        return result!;
    }

    /// <summary>Builds identical litter incrementally; disposing the iterator cancels and frees unfinished work.</summary>
    public static IEnumerable<bool> BuildBatched(VivariumWorld world, int seed, Action<Node3D> complete,
        float density = 1f, int detailStage = 2, Vector2? patchCenter = null,
        float halfExtent = BoardHalfExtent, bool respectLitterMass = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(complete);
        if (!float.IsFinite(density) || density < 0f || density > 3f)
            throw new ArgumentOutOfRangeException(nameof(density), "density must be finite and between 0 and 3");
        if (detailStage < 0 || detailStage > 2)
            throw new ArgumentOutOfRangeException(nameof(detailStage), "detailStage must be between 0 and 2");
        if (!float.IsFinite(halfExtent) || halfExtent <= 0f || halfExtent > 3.5f)
            throw new ArgumentOutOfRangeException(nameof(halfExtent), "halfExtent must be finite and between 0 and 3.5 m");
        Vector2 origin = patchCenter ?? Vector2.Zero;

        var root = new Node3D { Name = "LoamLitter" };
        bool transferred = false;
        try
        {
            var rng = new StableRandom(unchecked((ulong)(uint)seed) + 0x9E3779B97F4A7C15UL);
            using var leaves = new SurfaceTool();
            leaves.Begin(Mesh.PrimitiveType.Triangles);
            using var twigs = new SurfaceTool();
            twigs.Begin(Mesh.PrimitiveType.Triangles);
            using var duff = new SurfaceTool();
            duff.Begin(Mesh.PrimitiveType.Triangles);

            var leafMaterial = Bridge.Shader("res://Shaders/loam_litter.gdshader");
            leafMaterial.SetShaderParameter("leaf_col", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Color.png"));
            leafMaterial.SetShaderParameter("leaf_nrm", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_NormalGL.png"));
            leafMaterial.SetShaderParameter("leaf_rgh", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Roughness.png"));
            leafMaterial.SetShaderParameter("leaf_opacity", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Opacity.png"));
            leafMaterial.SetShaderParameter("leaf_trans", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Transmittance.png"));
            leafMaterial.SetShaderParameter("twig_mode", false);

            var twigMaterial = Bridge.Shader("res://Shaders/loam_litter.gdshader");
            twigMaterial.SetShaderParameter("twig_mode", true);
            twigMaterial.SetShaderParameter("bark_col", GD.Load<Texture2D>("res://Textures/Bark014_4K-PNG_Color.png"));
            twigMaterial.SetShaderParameter("bark_nrm", GD.Load<Texture2D>("res://Textures/Bark014_4K-PNG_NormalGL.png"));
            twigMaterial.SetShaderParameter("bark_rgh", GD.Load<Texture2D>("res://Textures/Bark014_4K-PNG_Roughness.png"));

            var duffMaterial = Bridge.Shader("res://Shaders/loam_litter.gdshader");
            duffMaterial.SetShaderParameter("leaf_col", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Color.png"));
            duffMaterial.SetShaderParameter("leaf_nrm", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_NormalGL.png"));
            duffMaterial.SetShaderParameter("leaf_rgh", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Roughness.png"));
            duffMaterial.SetShaderParameter("leaf_opacity", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Opacity.png"));
            duffMaterial.SetShaderParameter("leaf_trans", GD.Load<Texture2D>("res://Textures/ForestFloorLitter_4K_Transmittance.png"));
            duffMaterial.SetShaderParameter("duff_mode", true);
            duffMaterial.SetShaderParameter("decay_strength", 0.72f);

            int leafCount = 0, twigCountPlaced = 0, duffCount = 0;

            // Local height-field stacking grid (2 cm spacing) to prevent duff and leaf cards from interpenetrating
            int stackCells = (int)Math.Ceiling(halfExtent * 2f / 0.02f) + 1;
            float[,] stackHeight = new float[stackCells, stackCells];
            float stackOriginX = origin.X - halfExtent;
            float stackOriginZ = origin.Y - halfExtent;

            float QueryStack(SimVec2 p)
            {
                int gx = Math.Clamp((int)((p.X - stackOriginX) / 0.02f), 0, stackCells - 1);
                int gz = Math.Clamp((int)((p.Z - stackOriginZ) / 0.02f), 0, stackCells - 1);
                return stackHeight[gx, gz];
            }

            void PushStack(SimVec2 p, float radius, float delta)
            {
                int rCells = (int)Math.Ceiling(radius / 0.02f);
                int cx = (int)((p.X - stackOriginX) / 0.02f);
                int cz = (int)((p.Z - stackOriginZ) / 0.02f);
                for (int z = Math.Max(0, cz - rCells); z <= Math.Min(stackCells - 1, cz + rCells); z++)
                for (int x = Math.Max(0, cx - rCells); x <= Math.Min(stackCells - 1, cx + rCells); x++)
                {
                    stackHeight[x, z] += delta;
                }
            }

            if (density > 0f)
            {
                int nx = Math.Max(1, (int)Math.Ceiling(halfExtent * 2 / 0.15));
                int nz = nx;
                double step = halfExtent * 2 / nx;
                // Jittered low-frequency clump weights create sheltered accumulations without a tiled scatter grid.
                int clumpCols = (int)Math.Ceiling(halfExtent * 2 / 0.45);
                var clump = new float[clumpCols * clumpCols];
                for (int i = 0; i < clump.Length; i++) clump[i] = rng.Range(0.20f, 1.80f);

                for (int z = 0; z < nz && leafCount < HardLeafCap; z++)
                for (int x = 0; x < nx && leafCount < HardLeafCap; x++)
                {
                    yield return true;
                    double px = origin.X - halfExtent + (x + 0.5) * step;
                    double pz = origin.Y - halfExtent + (z + 0.5) * step;
                    int ci = Math.Clamp((int)((px - origin.X + halfExtent) / 0.45), 0, clumpCols - 1);
                    int cj = Math.Clamp((int)((pz - origin.Y + halfExtent) / 0.45), 0, clumpCols - 1);
                    float lambda = (float)(NominalPerSquareMetre * step * step) * density * clump[cj * clumpCols + ci];
                    var cellCenter = new SimVec2(px, pz);
                    if (respectLitterMass) lambda *= FineLitterCover(world, cellCenter);
                    int spawnCount = SamplePoisson(lambda, ref rng);
                    for (int k = 0; k < spawnCount && leafCount < HardLeafCap; k++)
                    {
                        var center = new SimVec2(px + rng.Range((float)-step * 0.5f, (float)step * 0.5f),
                            pz + rng.Range((float)-step * 0.5f, (float)step * 0.5f));
                        if (!world.Domain.ContainsDisc(center, 0.085) || world.Water.IsWet(center) || world.SubstrateAt(center) != Substrate.Soil)
                            continue;
                        if (world.PilotTree?.BlocksDisc(center, 0.055) == true || world.Props.RockAt(center) != null ||
                            world.Props.LogAt(center) != null || world.Props.GravelAt(center) != null)
                            continue;

                        if (respectLitterMass && FineLitterCover(world, center) <= 0f) continue;
                        bool intact = rng.NextFloat() < 0.62f;
                        float stackLift = QueryStack(center);
                        float overlapLift = k > 0 && rng.NextFloat() < 0.65f ? rng.Range(0.001f, 0.003f) : 0f;
                        AddLeaf(leaves, world, center, ref rng, intact, overlapLift + stackLift);
                        PushStack(center, intact ? 0.045f : 0.025f, 0.0012f);
                        leafCount++;
                        yield return true;
                    }
                }

                int targetTwigCount = respectLitterMass
                    ? 52
                    : Math.Min(52, Math.Max(0, (int)Math.Round(leafCount * 0.052)));
                for (int i = 0; i < targetTwigCount * 4 && twigCountPlaced < targetTwigCount; i++)
                {
                    yield return true;
                    var center = new SimVec2(rng.Range(origin.X - halfExtent, origin.X + halfExtent), rng.Range(origin.Y - halfExtent, origin.Y + halfExtent));
                    if (!world.Domain.ContainsDisc(center, 0.09) || world.Water.IsWet(center) || world.SubstrateAt(center) != Substrate.Soil)
                        continue;
                    if (world.PilotTree?.BlocksDisc(center, 0.045) == true || world.Props.RockAt(center) != null ||
                        world.Props.LogAt(center) != null || world.Props.GravelAt(center) != null)
                        continue;
                    if (respectLitterMass && rng.NextFloat() >= CoarseLitterCover(world, center)) continue;
                    AddTwig(twigs, world, center, ref rng);
                    PushStack(center, 0.06f, 0.0025f);
                    twigCountPlaced++;
                    yield return true;
                }

                if (detailStage > 0)
                    foreach (int placed in AddDuffBatched(duff, world, seed, density, detailStage, origin,
                                 halfExtent, respectLitterMass, QueryStack, PushStack))
                    {
                        duffCount = placed;
                        yield return true;
                    }
            }

            root.SetMeta("seed", seed);
            root.SetMeta("density", density);
            root.SetMeta("board_size_m", halfExtent * 2f);
            root.SetMeta("patch_center", origin);
            root.SetMeta("patch_half_extent_m", halfExtent);
            root.SetMeta("leaf_count", leafCount);
            root.SetMeta("twig_count", twigCountPlaced);
            root.SetMeta("detail_stage", detailStage);
            root.SetMeta("duff_count", duffCount);

            yield return true;
            if (leafCount > 0) leaves.GenerateTangents();
            yield return true;
            var leafMesh = leafCount > 0 ? leaves.Commit() : null;
            if (leafMesh is not null && leafMesh.GetSurfaceCount() > 0)
            {
                leafMesh.SurfaceSetMaterial(0, leafMaterial);
                root.AddChild(new MeshInstance3D
                {
                    Name = "CurledLeafLitter",
                    Mesh = leafMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                });
            }

            yield return true;
            if (twigCountPlaced > 0) twigs.GenerateTangents();
            yield return true;
            var twigMesh = twigCountPlaced > 0 ? twigs.Commit() : null;
            if (twigMesh is not null && twigMesh.GetSurfaceCount() > 0)
            {
                twigMesh.SurfaceSetMaterial(0, twigMaterial);
                root.AddChild(new MeshInstance3D
                {
                    Name = "FineTwigs",
                    Mesh = twigMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                });
            }
            yield return true;
            var duffMesh = duffCount > 0 ? duff.Commit() : null;
            if (duffMesh is not null && duffMesh.GetSurfaceCount() > 0)
            {
                duffMesh.SurfaceSetMaterial(0, duffMaterial);
                root.AddChild(new MeshInstance3D
                {
                    Name = "DecomposedLeafDuff",
                    Mesh = duffMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
                });
            }
            transferred = true;
            complete(root);
        }
        finally
        {
            if (!transferred) root.Free();
        }
    }

    private static IEnumerable<int> AddDuffBatched(SurfaceTool mesh, VivariumWorld world, int seed, float density, int detailStage,
        Vector2 origin, float halfExtent, bool respectLitterMass,
        Func<SimVec2, float> queryStack, Action<SimVec2, float, float> pushStack)
    {
        var rng = new StableRandom(unchecked((ulong)(uint)seed) + 0xD1B54A32D192ED03UL);
        const float spacing = 0.072f;
        int cells = (int)Math.Ceiling(halfExtent * 2f / spacing);
        double step = halfExtent * 2f / cells;
        int patchCells = (int)Math.Ceiling(halfExtent * 2f / 0.28f);
        var patches = new float[patchCells * patchCells];
        for (int i = 0; i < patches.Length; i++)
            patches[i] = rng.Range(0.18f, 1.75f);

        int placed = 0;
        for (int z = 0; z < cells && placed < HardDuffCap; z++)
        for (int x = 0; x < cells && placed < HardDuffCap; x++)
        {
            yield return placed;
            double px = origin.X - halfExtent + (x + 0.5) * step;
            double pz = origin.Y - halfExtent + (z + 0.5) * step;
            var center = new SimVec2(px + rng.Range((float)-step * 0.5f, (float)step * 0.5f),
                pz + rng.Range((float)-step * 0.5f, (float)step * 0.5f));
            if (!world.Domain.ContainsDisc(center, 0.025) || world.Water.IsWet(center) || world.SubstrateAt(center) != Substrate.Soil)
                continue;
            float litterCover = respectLitterMass ? FineLitterCover(world, center) : 1f;
            if (litterCover <= 0f) continue;
            if (world.PilotTree?.BlocksDisc(center, 0.02) == true || world.Props.RockAt(center) != null ||
                world.Props.LogAt(center) != null || world.Props.GravelAt(center) != null)
                continue;

            float structure = 1f;
            if (detailStage >= 2)
            {
                double e = 0.09;
                double h = world.Terrain.Height(center);
                double neighborMean = (world.Terrain.Height(center + new SimVec2(e, 0)) +
                    world.Terrain.Height(center - new SimVec2(e, 0)) +
                    world.Terrain.Height(center + new SimVec2(0, e)) +
                    world.Terrain.Height(center - new SimVec2(0, e))) * 0.25;
                double dx = (world.Terrain.Height(center + new SimVec2(0.045, 0)) -
                    world.Terrain.Height(center - new SimVec2(0.045, 0))) / 0.09;
                double dz = (world.Terrain.Height(center + new SimVec2(0, 0.045)) -
                    world.Terrain.Height(center - new SimVec2(0, 0.045))) / 0.09;
                float hollow = Mathf.Clamp((float)((neighborMean - h) / 0.012), 0f, 1f);
                float slope = (float)Math.Sqrt(dx * dx + dz * dz);
                float shelter = Mathf.Clamp(1f - slope / 0.72f, 0.10f, 1f);
                int pi = Math.Clamp((int)((center.X - origin.X + halfExtent) / 0.28), 0, patchCells - 1);
                int pj = Math.Clamp((int)((center.Z - origin.Y + halfExtent) / 0.28), 0, patchCells - 1);
                structure = (0.32f + 1.65f * hollow + 0.48f * shelter) * patches[pj * patchCells + pi];
            }

            float lambda = (float)(step * step) * 80f * density * structure * litterCover;
            int count = SamplePoisson(lambda, ref rng);
            for (int i = 0; i < count && placed < HardDuffCap; i++)
            {
                var p = new SimVec2(center.X + rng.Range(-0.014f, 0.014f), center.Z + rng.Range(-0.014f, 0.014f));
                if (!world.Domain.ContainsDisc(p, 0.025) || world.Water.IsWet(p) || world.SubstrateAt(p) != Substrate.Soil)
                    continue;
                if (respectLitterMass && FineLitterCover(world, p) <= 0f) continue;
                if (world.PilotTree?.BlocksDisc(p, 0.025) == true || world.Props.RockAt(p) != null ||
                    world.Props.LogAt(p) != null || world.Props.GravelAt(p) != null)
                    continue;
                float stackLift = queryStack(p);
                AddDuffFragment(mesh, world, p, ref rng, stackLift);
                pushStack(p, 0.018f, 0.0006f);
                placed++;
                yield return placed;
            }
        }
        yield return placed;
    }

    private static float FineLitterCover(VivariumWorld world, SimVec2 p)
    {
        int cell = world.Grid.CellAt(p);
        if (cell < 0 || !world.Grid.InDomain(cell)) return 0f;
        return (float)(1.0 - Math.Exp(-Math.Max(0.0, world.Litter.FineMass[cell]) * 55.0)) * BareGroundFraction(world, cell);
    }

    private static float CoarseLitterCover(VivariumWorld world, SimVec2 p)
    {
        int cell = world.Grid.CellAt(p);
        if (cell < 0 || !world.Grid.InDomain(cell)) return 0f;
        return (float)(1.0 - Math.Exp(-Math.Max(0.0, world.Litter.CoarseMass[cell]) * 65.0)) * BareGroundFraction(world, cell);
    }

    private static float BareGroundFraction(VivariumWorld world, int cell) =>
        1f - 0.78f * Mathf.Clamp((float)world.AmbientGroundCover.Cover[cell], 0f, 1f);

    private static void AddDuffFragment(SurfaceTool mesh, VivariumWorld world, SimVec2 center, ref StableRandom rng, float stackLift)
    {
        int stations = 5, across = 4;
        float length = rng.Range(0.014f, 0.034f);
        float width = rng.Range(0.006f, 0.017f);
        float angle = rng.Range(0f, Mathf.Tau);
        float fold = rng.Range(0.0002f, 0.0018f);
        float phase = rng.Range(0f, Mathf.Tau);
        // Fragmented humus flakes (cells 10..12) or decomposed remnants (cells 6..9)
        int cellIndex = rng.NextFloat() < 0.65f ? rng.NextInt(10, 12) : rng.NextInt(6, 9);
        Color tint = new(rng.Range(0.62f, 0.84f), rng.Range(0.62f, 0.79f), rng.Range(0.58f, 0.74f));
        tint.A = (float)Math.Clamp(world.Fields.Moisture[world.Grid.NearestDomainCell(center)], 0, 1);
        var points = new Vector3[stations * across];
        var normals = new Vector3[points.Length];
        var uvs = new Vector2[points.Length];

        for (int i = 0; i < stations; i++)
        for (int j = 0; j < across; j++)
        {
            float t = i / (float)(stations - 1), s = j / (float)(across - 1) * 2f - 1f;
            float ragged = 0.70f + 0.30f * Mathf.Sin(t * 14.1f + phase) * Mathf.Sin(t * 6.7f + phase * 0.37f);
            if ((i == 1 || i == 3) && j == 0 && rng.NextFloat() < 0.24f) ragged *= 0.28f;
            float side = s * width * ragged;
            float lx = (t - 0.5f) * length + fold * Mathf.Sin(t * Mathf.Pi * 2f + phase) * (1f - Mathf.Abs(s));
            double wx = center.X + Math.Cos(angle) * lx - Math.Sin(angle) * side;
            double wz = center.Z + Math.Sin(angle) * lx + Math.Cos(angle) * side;
            float centerLift = (1f - Mathf.Abs(s)) * fold;
            float burial = rng.Range(-0.00115f, 0.00045f);
            float y = (float)world.Terrain.Height(new SimVec2(wx, wz)) + centerLift + burial + stackLift;
            int index = i * across + j;
            points[index] = new Vector3((float)wx, y, (float)wz);
            uvs[index] = AtlasUv(cellIndex, t, s);
        }

        for (int i = 0; i < stations; i++)
        for (int j = 0; j < across; j++)
        {
            int i0 = Math.Max(0, i - 1), i1 = Math.Min(stations - 1, i + 1);
            int j0 = Math.Max(0, j - 1), j1 = Math.Min(across - 1, j + 1);
            Vector3 along = points[i1 * across + j] - points[i0 * across + j];
            Vector3 side = points[i * across + j1] - points[i * across + j0];
            Vector3 normal = side.Cross(along).Normalized();
            if (normal.Y < 0) normal = -normal;
            normals[i * across + j] = normal;
        }
        for (int i = 0; i < stations - 1; i++)
        for (int j = 0; j < across - 1; j++)
        {
            int a = i * across + j, b = a + across, c = b + 1, d = a + 1;
            AddTriangle(mesh, points[a], points[c], points[b], normals[a], normals[c], normals[b], uvs[a], uvs[c], uvs[b], tint);
            AddTriangle(mesh, points[a], points[d], points[c], normals[a], normals[d], normals[c], uvs[a], uvs[d], uvs[c], tint);
        }
    }

    private static void AddLeaf(SurfaceTool mesh, VivariumWorld world, SimVec2 center, ref StableRandom rng, bool intact, float overlapLift)
    {
        int stations = intact ? 8 : 6;
        int across = intact ? 5 : 4;
        float length = intact ? rng.Range(0.065f, 0.120f) : rng.Range(0.028f, 0.065f);
        float width = intact ? rng.Range(0.028f, 0.050f) : rng.Range(0.015f, 0.032f);
        float angle = rng.Range(0f, Mathf.Tau);
        float curl = rng.Range(0.001f, intact ? 0.009f : 0.0035f);
        float fold = rng.Range(0.000f, intact ? 0.0025f : 0.0012f);
        float phase = rng.Range(0f, Mathf.Tau);
        // Cells 0..5 are intact; cells 6..9 are skeletonized/decaying
        int cellIndex = intact ? rng.NextInt(0, 5) : rng.NextInt(6, 9);
        Color color = new(rng.Range(0.88f, 0.99f), rng.Range(0.88f, 0.98f), rng.Range(0.86f, 0.96f));
        color.A = (float)Math.Clamp(world.Fields.Moisture[world.Grid.NearestDomainCell(center)], 0, 1);
        var points = new Vector3[stations * across];
        var normals = new Vector3[points.Length];
        var uvs = new Vector2[points.Length];
        var terrain = world.Terrain;

        for (int i = 0; i < stations; i++)
        {
            float t = i / (float)(stations - 1);
            float longitudinal = (t - 0.5f) * length;
            float envelope = 1f;
            float asym = 0.82f + 0.18f * Mathf.Sin(t * 11.3f + phase);
            if (!intact && i == 1 && rng.NextFloat() < 0.45f) asym *= rng.Range(0.45f, 0.78f);
            float centerBend = fold * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2 + phase))
                + 0.0015f * Mathf.Sin(t * Mathf.Pi) * (0.5f + 0.5f * Mathf.Sin(phase));
            for (int j = 0; j < across; j++)
            {
                float s = j / (float)(across - 1) * 2 - 1;
                float sideWidth = width * envelope * (s < 0 ? 0.46f : 0.54f) * asym;
                float side = s * sideWidth;
                float lx = longitudinal + centerBend + 0.0022f * Mathf.Sin(t * Mathf.Pi + phase) * s;
                float lz = side;
                double wx = center.X + Math.Cos(angle) * lx - Math.Sin(angle) * lz;
                double wz = center.Z + Math.Sin(angle) * lx + Math.Cos(angle) * lz;
                var p = new SimVec2(wx, wz);
                float midrib = (1f - Mathf.Abs(s)) * (intact ? 0.0017f : 0.0008f);
                float cup = (1f - Mathf.Abs(s)) * fold;
                float edgeCurl = Mathf.Pow(Mathf.Abs(s), 3.2f) * curl * (0.65f + 0.35f * Mathf.Sin(t * 17f + phase));
                float surfaceY = (float)terrain.Height(p) + 0.0009f + overlapLift + centerBend + midrib + cup + edgeCurl;
                int index = i * across + j;
                points[index] = new Vector3((float)wx, surfaceY, (float)wz);
                uvs[index] = AtlasUv(cellIndex, t, s);
            }
        }

        for (int i = 0; i < stations; i++)
        for (int j = 0; j < across; j++)
        {
            int i0 = Math.Max(0, i - 1), i1 = Math.Min(stations - 1, i + 1);
            int j0 = Math.Max(0, j - 1), j1 = Math.Min(across - 1, j + 1);
            Vector3 along = points[i1 * across + j] - points[i0 * across + j];
            Vector3 side = points[i * across + j1] - points[i * across + j0];
            Vector3 normal = side.Cross(along).Normalized();
            if (normal.Y < 0) normal = -normal;
            normals[i * across + j] = normal;
        }

        for (int i = 0; i < stations - 1; i++)
        for (int j = 0; j < across - 1; j++)
        {
            int a = i * across + j, b = a + across, c = b + 1, d = a + 1;
            AddTriangle(mesh, points[a], points[c], points[b], normals[a], normals[c], normals[b], uvs[a], uvs[c], uvs[b], color);
            AddTriangle(mesh, points[a], points[d], points[c], normals[a], normals[d], normals[c], uvs[a], uvs[d], uvs[c], color);
        }
    }

    private static void AddTwig(SurfaceTool mesh, VivariumWorld world, SimVec2 center, ref StableRandom rng)
    {
        float length = rng.Range(0.055f, 0.165f), radius = rng.Range(0.0012f, 0.0028f);
        float angle = rng.Range(0f, Mathf.Tau), bend = rng.Range(-0.018f, 0.018f);
        float startLift = rng.Range(0.0015f, 0.0035f);
        const int stations = 6;
        const int sides = 8;
        var ringCenters = new Vector3[stations];
        for (int i = 0; i < stations; i++)
        {
            float t = i / (float)(stations - 1);
            float along = (t - 0.5f) * length;
            float across = bend * Mathf.Sin(t * Mathf.Pi);
            double cx = center.X + Math.Cos(angle) * along - Math.Sin(angle) * across;
            double cz = center.Z + Math.Sin(angle) * along + Math.Cos(angle) * across;
            float groundY = (float)world.Terrain.Height(new SimVec2(cx, cz));
            ringCenters[i] = new Vector3((float)cx, groundY + startLift + radius * 0.9f, (float)cz);
        }

        var dir = (ringCenters[^1] - ringCenters[0]).Normalized();
        var sideVec = new Vector3(-dir.Z, 0, dir.X).Normalized();
        var upVec = sideVec.Cross(dir).Normalized();

        var ringVerts = new Vector3[stations, sides];
        var ringNorms = new Vector3[stations, sides];
        var ringUvs = new Vector2[stations, sides];

        for (int i = 0; i < stations; i++)
        {
            float t = i / (float)(stations - 1);
            float r = radius * (1.0f - 0.25f * t);
            for (int j = 0; j < sides; j++)
            {
                float a = j * Mathf.Tau / sides;
                Vector3 radial = sideVec * Mathf.Cos(a) + upVec * Mathf.Sin(a);
                ringVerts[i, j] = ringCenters[i] + radial * r;
                ringNorms[i, j] = radial;
                ringUvs[i, j] = new Vector2(a / Mathf.Tau * 2.0f, t * (length / 0.05f));
            }
        }

        Color tint = new(rng.Range(0.18f, 0.28f), rng.Range(0.11f, 0.17f), rng.Range(0.06f, 0.10f));
        Vector2 uv2Bark = new(1.0f, 0.0f);
        Vector2 uv2End = new(2.0f, 0.0f);

        // Cylindrical tube quads
        for (int i = 0; i < stations - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            int next = (j + 1) % sides;
            var v00 = ringVerts[i, j]; var v01 = ringVerts[i, next];
            var v10 = ringVerts[i + 1, j]; var v11 = ringVerts[i + 1, next];
            var n00 = ringNorms[i, j]; var n01 = ringNorms[i, next];
            var n10 = ringNorms[i + 1, j]; var n11 = ringNorms[i + 1, next];
            var u00 = ringUvs[i, j]; var u01 = ringUvs[i, next];
            var u10 = ringUvs[i + 1, j]; var u11 = ringUvs[i + 1, next];

            AddTriWithUv2(mesh, v00, v10, v01, n00, n10, n01, u00, u10, u01, uv2Bark, tint);
            AddTriWithUv2(mesh, v01, v10, v11, n01, n10, n11, u01, u10, u11, uv2Bark, tint);
        }

        // Base end cap (severed wood grain)
        Vector3 baseCenter = ringCenters[0];
        Vector3 baseNormal = -dir;
        for (int j = 0; j < sides; j++)
        {
            int next = (j + 1) % sides;
            var u0 = new Vector2(0.5f, 0.5f);
            var u1 = new Vector2(0.5f + 0.45f * Mathf.Cos(j * Mathf.Tau / sides), 0.5f + 0.45f * Mathf.Sin(j * Mathf.Tau / sides));
            var u2 = new Vector2(0.5f + 0.45f * Mathf.Cos(next * Mathf.Tau / sides), 0.5f + 0.45f * Mathf.Sin(next * Mathf.Tau / sides));
            AddTriWithUv2(mesh, baseCenter, ringVerts[0, next], ringVerts[0, j], baseNormal, baseNormal, baseNormal, u0, u2, u1, uv2End, tint);
        }

        // Tip end cap
        Vector3 tipCenter = ringCenters[^1];
        Vector3 tipNormal = dir;
        for (int j = 0; j < sides; j++)
        {
            int next = (j + 1) % sides;
            var u0 = new Vector2(0.5f, 0.5f);
            var u1 = new Vector2(0.5f + 0.45f * Mathf.Cos(j * Mathf.Tau / sides), 0.5f + 0.45f * Mathf.Sin(j * Mathf.Tau / sides));
            var u2 = new Vector2(0.5f + 0.45f * Mathf.Cos(next * Mathf.Tau / sides), 0.5f + 0.45f * Mathf.Sin(next * Mathf.Tau / sides));
            AddTriWithUv2(mesh, tipCenter, ringVerts[stations - 1, j], ringVerts[stations - 1, next], tipNormal, tipNormal, tipNormal, u0, u1, u2, uv2End, tint);
        }
    }

    private static int SamplePoisson(float lambda, ref StableRandom rng)
    {
        if (!(lambda > 0f)) return 0;
        double limit = Math.Exp(-lambda), product = 1;
        int samples = 0;
        do
        {
            samples++;
            product *= Math.Max(1e-12f, rng.NextFloat());
        } while (product > limit && samples < 32);
        return samples - 1;
    }

    private static Vector2 AtlasUv(int cellIndex, float t, float s)
    {
        int col = cellIndex % 4;
        int row = cellIndex / 4;
        // Element is 800 x 950 px centered in 1024 x 1024 cell on 4096 x 4096 atlas
        // Margin X = 112 px, Margin Y = 37 px
        float uMin = (col * 1024f + 112f) / 4096f;
        float uMax = (col * 1024f + 112f + 800f) / 4096f;
        float vTip = (row * 1024f + 37f) / 4096f;
        float vBase = (row * 1024f + 37f + 950f) / 4096f;
        float u = Mathf.Lerp(uMin, uMax, (s + 1f) * 0.5f);
        float v = Mathf.Lerp(vBase, vTip, t);
        return new Vector2(u, v);
    }

    private static void AddTriangle(SurfaceTool mesh, Vector3 a, Vector3 b, Vector3 c,
        Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, Color color)
    {
        if ((b - a).Cross(c - a).Dot(na + nb + nc) > 0f)
        { (b, c) = (c, b); (nb, nc) = (nc, nb); (ub, uc) = (uc, ub); }
        mesh.SetUV2(Vector2.Zero);
        AddVertex(mesh, a, na, ua, color);
        AddVertex(mesh, b, nb, ub, color);
        AddVertex(mesh, c, nc, uc, color);
    }

    private static void AddTriWithUv2(SurfaceTool mesh, Vector3 a, Vector3 b, Vector3 c,
        Vector3 na, Vector3 nb, Vector3 nc, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 uv2, Color color)
    {
        if ((b - a).Cross(c - a).Dot(na + nb + nc) > 0f)
        { (b, c) = (c, b); (nb, nc) = (nc, nb); (ub, uc) = (uc, ub); }
        mesh.SetUV2(uv2);
        AddVertex(mesh, a, na, ua, color);
        AddVertex(mesh, b, nb, ub, color);
        AddVertex(mesh, c, nc, uc, color);
    }

    private static void AddVertex(SurfaceTool mesh, Vector3 p, Vector3 normal, Vector2 uv, Color color)
    {
        mesh.SetNormal(normal);
        mesh.SetColor(color);
        mesh.SetUV(uv);
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
