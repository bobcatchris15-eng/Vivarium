using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Vivarium.Sim.Repr.Light;

namespace Vivarium.Repr;

/// <summary>
/// L1b: <see cref="PlantSpecimensDemo"/> (legacy variant) with the cached sky-visibility field fed the same way
/// <see cref="LightProbeDemo"/> does it. Shots default to shadebell_whole and glowbriar_under (variant legacy);
/// <c>--repr-env shots=a+b</c> and <c>frames=N</c> override. Without <c>skyvis=1</c> it is the plain demo.
/// </summary>
public sealed class PlantSpecimensLitDemo : IReprDemo
{
    public string Name => "plant_specimens_lit";

    private readonly PlantSpecimensDemo _inner = new();
    private SkyVisibilityField? _field;

    public IReadOnlyList<ReprShot> Shots
    {
        get
        {
            var want = ReprEnv.ShotFilter ?? new HashSet<string> { "shadebell_whole", "glowbriar_under" };
            int n = ReprEnv.Frames > 0 ? ReprEnv.Frames : 90;
            return _inner.Shots.Where(s => s.Variant == "legacy" && want.Contains(s.Name)).Select(s => s with { MeasureFrames = n, Eye = s.Eye + new Vector3(ReprEnv.StageShift, 0, 0), Target = s.Target + new Vector3(ReprEnv.StageShift, 0, 0) }).ToList();
        }
    }

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        _inner.Build(root, ctx);
        root.Position = new Vector3(ReprEnv.StageShift, 0, 0);
        if (!ReprEnv.SkyVis) return;

        var legacy = new List<Node3D>();
        var grounds = new List<MeshInstance3D>();
        foreach (var c in root.GetChildren())
        {
            if (c is MeshInstance3D mi && mi.Mesh is PlaneMesh) grounds.Add(mi);
            else if (c is Node3D n3 && HasMultiMesh(n3) && c.GetType().Name != "PlantAssemblyRenderer") legacy.Add(n3);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var gm = SkyVisibilityField.LitMaterial(new Color(0.55f, 0.52f, 0.46f), 1f);
        foreach (var g in grounds) g.MaterialOverride = gm;

        float sh = ReprEnv.StageShift, x0 = sh - 3f, x1 = sh + 3f;
        foreach (var g in grounds) { x0 = Math.Min(x0, sh + g.Position.X - 3f); x1 = Math.Max(x1, sh + g.Position.X + 3f); }
        var bounds = new Vivarium.Sim.Repr.Aabb(new Vivarium.Sim.Core.Vec3(x0, -0.2, -3.0), new Vivarium.Sim.Core.Vec3(x1, 4.0, 3.0));
        _field = SkyVisibilityField.Create(bounds, ReprEnv.SkyVisVoxel);
        root.AddChild(_field);
        var v = _field.Volume;
        foreach (var g in grounds)
            v.AddHeightfield((x, z) => 0.0, new Vivarium.Sim.Repr.Aabb(new Vivarium.Sim.Core.Vec3(sh + g.Position.X - 2, 0, -2), new Vivarium.Sim.Core.Vec3(sh + g.Position.X + 2, 0, 2)));
        int tris = 0, id = 100;
        foreach (var l in legacy) tris += _field.AddNodeTriangles((ulong)id++, l, ReprEnv.SkyVisLeaf, OcclusionVolume.OpaqueOpacity);
        double dep = sw.Elapsed.TotalMilliseconds; sw.Restart();
        v.BakeAll();
        double bake = sw.Elapsed.TotalMilliseconds; sw.Restart();
        _field.Upload();
        double up = sw.Elapsed.TotalMilliseconds;
        _field.SetStrength(ReprEnv.SkyVisStrength);
        _field.RegisterGround(gm);
        _field.CollectMaterials(root, false);
        _field.SetShapes(ReprEnv.FoliageShape, ReprEnv.GroundShape);
        ReprEnv.ActiveField = _field;
        ctx.Log(string.Format(CultureInfo.InvariantCulture,
            "skyvis(plant_specimens_lit): voxel={0} grid={1}x{2}x{3} voxels={4} legacy_plants={5} tris={6} deposit={7:0.0}ms full_bake={8:0.0}ms upload={9:0.0}ms materials={10}",
            v.VoxelSize, v.Nx, v.Ny, v.Nz, v.VoxelCount, legacy.Count, tris, dep, bake, up, _field.FoliageMaterialCount));
    }

    private static bool HasMultiMesh(Node n)
    {
        if (n is MultiMeshInstance3D) return true;
        foreach (var c in n.GetChildren()) if (HasMultiMesh(c)) return true;
        return false;
    }

    public void ApplyVariant(string? variant) => _inner.ApplyVariant(variant);
    public void Tick(double dt) => _inner.Tick(dt);
}
