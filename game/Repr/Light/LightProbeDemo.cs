using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Vivarium.Sim.Repr.Light;

namespace Vivarium.Repr;

/// <summary>More representative lighting probe: 6x6 m matte ground, ring of 12 legacy plants, a few box rocks.
/// With <c>--repr-env skyvis=1</c> it also feeds the cached occlusion volume (L1).</summary>
public sealed class LightProbeDemo : IReprDemo
{
    public string Name => "light_probe";

    private static readonly Vector3 MidEye = new(4f, 1.8f, 4f), MidTarget = new(0f, 0.6f, 0f);

    public IReadOnlyList<ReprShot> Shots
    {
        get
        {
            int n = ReprEnv.Frames > 0 ? ReprEnv.Frames : 180;
            var all = new List<ReprShot>
            {
                new("mid", MidEye, MidTarget, MeasureFrames: n),
                new("under", new Vector3(0.6f, 0.35f, 0.6f), new Vector3(-1.5f, 0.9f, -1.2f), MeasureFrames: n),
                new("night", MidEye, MidTarget, DayFraction: 0.9f, MeasureFrames: n),
                new("dusk", MidEye, MidTarget, DayFraction: 0.72f, MeasureFrames: n),
                new("under_close", new Vector3(0.25f, 0.15f, 0.25f), new Vector3(1.55f, 0.55f, 0.9f), MeasureFrames: n),
            };
            if (ReprEnv.Dyn)
            {
                all.Add(new("dyn", MidEye, MidTarget, Variant: "before", MeasureFrames: Math.Min(n, 30)));
                all.Add(new("dyn", MidEye, MidTarget, Variant: "after", MeasureFrames: Math.Min(n, 30)));
            }
            foreach (float d in ReprEnv.SunShots) all.Add(new($"sun{(int)Math.Round(d * 100)}", MidEye, MidTarget, DayFraction: d, MeasureFrames: n));
            if (ReprEnv.ShotFilter != null) all = all.Where(s => ReprEnv.ShotFilter.Contains(s.Name)).ToList();
            return all;
        }
    }

    private SkyVisibilityField? _field;
    private ReprDemoContext _ctx = null!;
    private readonly List<(Node3D node, ulong id)> _plants = new();
    private const int MovedPlant = 3;
    private bool _moved;
    private readonly Vector3 _movedPos = new(-1.3f, 0f, -1.0f);
    private Vector3 _origPos;
    private float _origYaw;

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        _ctx = ctx;
        bool sv = ReprEnv.SkyVis;
        var groundCol = new Color(0.30f, 0.20f, 0.12f);
        var rockCol = new Color(0.45f, 0.44f, 0.42f);
        Material ground = sv ? SkyVisibilityField.LitMaterial(groundCol, 1f) : new StandardMaterial3D { AlbedoColor = groundCol, Roughness = 1f, Metallic = 0f };
        Material rock = sv ? SkyVisibilityField.LitMaterial(rockCol, 0.95f) : new StandardMaterial3D { AlbedoColor = rockCol, Roughness = 0.95f };
        root.AddChild(new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(6f, 6f) }, MaterialOverride = ground });

        string[] species = { "embercrown", "shadebell", "glowbriar", "moonfern" };
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12f;
            var flora = ctx.BuildLegacyFlora(species[i % 4]);
            if (flora == null) { ctx.Log($"light_probe: {species[i % 4]} unavailable"); continue; }
            flora.Position = new Vector3(Mathf.Cos(a) * 2f, 0f, Mathf.Sin(a) * 2f);
            flora.RotationDegrees = new Vector3(0f, i * 37f, 0f);
            root.AddChild(flora);
            _plants.Add((flora, 100UL + (ulong)i));
        }

        var rocks = new (Vector3 pos, Vector3 size, float yaw)[]
        {
            (new Vector3(0.5f, 0.15f, -0.4f), new Vector3(0.5f, 0.3f, 0.4f), 20f),
            (new Vector3(-0.8f, 0.1f, 0.6f), new Vector3(0.35f, 0.2f, 0.3f), -35f),
            (new Vector3(0.1f, 0.25f, 0.9f), new Vector3(0.3f, 0.5f, 0.3f), 50f),
            (new Vector3(-0.3f, 0.08f, -0.9f), new Vector3(0.4f, 0.16f, 0.25f), 80f),
        };
        var rockNodes = new List<MeshInstance3D>();
        for (int i = 0; i < rocks.Length; i++)
        {
            var r = new MeshInstance3D
            {
                Name = "Rock" + i, Position = rocks[i].pos, RotationDegrees = new Vector3(0f, rocks[i].yaw, 0f),
                Mesh = new BoxMesh { Size = rocks[i].size }, MaterialOverride = rock,
            };
            root.AddChild(r); rockNodes.Add(r);
        }

        if (!sv) return;
        var sw = Stopwatch.StartNew();
        var bounds = new Vivarium.Sim.Repr.Aabb(new Vivarium.Sim.Core.Vec3(-3.6, -0.2, -3.6), new Vivarium.Sim.Core.Vec3(3.6, 3.0, 3.6));
        _field = SkyVisibilityField.Create(bounds, ReprEnv.SkyVisVoxel);
        root.AddChild(_field);
        var v = _field.Volume;
        v.AddHeightfield((x, z) => 0.0, new Vivarium.Sim.Repr.Aabb(new Vivarium.Sim.Core.Vec3(-3, 0, -3), new Vivarium.Sim.Core.Vec3(3, 0, 3)));
        int tris = 0;
        for (int i = 0; i < rockNodes.Count; i++) tris += _field.AddNodeTriangles(1000UL + (ulong)i, rockNodes[i], OcclusionVolume.OpaqueOpacity, OcclusionVolume.OpaqueOpacity);
        foreach (var (node, id) in _plants) tris += _field.AddNodeTriangles(id, node, ReprEnv.SkyVisLeaf, OcclusionVolume.OpaqueOpacity);
        double depositMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        v.BakeAll();
        double bakeMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        _field.Upload();
        double upMs = sw.Elapsed.TotalMilliseconds;
        _field.SetStrength(ReprEnv.SkyVisStrength);
        foreach (var gm in new[] { ground, rock }) if (gm is ShaderMaterial gsm) _field.RegisterGround(gsm);
        _field.CollectMaterials(root, false);   // flora materials: shaped AO + bounce
        _field.SetShapes(ReprEnv.FoliageShape, ReprEnv.GroundShape);
        ReprEnv.ActiveField = _field;
        ctx.Log(string.Format(CultureInfo.InvariantCulture,
            "skyvis: voxel={0} grid={1}x{2}x{3} voxels={4} tris={5} deposit={6:0.0}ms full_bake={7:0.0}ms upload={8:0.0}ms ({9} bytes) cpu={10:0.0}MB records={11:0.0}MB texture={12:0.0}MB strength={13}",
            v.VoxelSize, v.Nx, v.Ny, v.Nz, v.VoxelCount, tris, depositMs, bakeMs, upMs, _field.LastUploadBytes,
            v.CpuBytes / 1048576.0, v.SourceRecordBytes / 1048576.0, v.PackedBytes / 1048576.0, ReprEnv.SkyVisStrength));
        File.WriteAllText(Path.Combine(ctx.OutDir, "skyvis_build.json"), string.Format(CultureInfo.InvariantCulture,
            "{{\"voxel\":{0},\"voxels\":{1},\"triangles\":{2},\"deposit_ms\":{3:0.0},\"full_bake_ms\":{4:0.0},\"upload_ms\":{5:0.0},\"upload_bytes\":{6},\"cpu_bytes\":{7},\"record_bytes\":{8},\"texture_bytes\":{9}}}",
            v.VoxelSize, v.VoxelCount, tris, depositMs, bakeMs, upMs, _field.LastUploadBytes, v.CpuBytes, v.SourceRecordBytes, v.PackedBytes));
    }

    public void ApplyVariant(string? variant)
    {
        bool wantMoved = variant == "after";
        if (wantMoved == _moved || _plants.Count <= MovedPlant) return;
        var (node, id) = _plants[MovedPlant];
        if (!_moved) { _origPos = node.Position; _origYaw = node.RotationDegrees.Y; }
        var sw = Stopwatch.StartNew();
        node.Position = wantMoved ? _movedPos : _origPos;
        _moved = wantMoved;
        if (_field == null) return;
        var v = _field.Volume;
        long voxBefore = v.BakedVoxelCounter;
        var pOld = new Vivarium.Sim.Core.Vec3(_origPos.X, 0.15, _origPos.Z); var pNew = new Vivarium.Sim.Core.Vec3(_movedPos.X, 0.15, _movedPos.Z);
        float vo0 = v.Sample(pOld).SkyVisibility, vn0 = v.Sample(pNew).SkyVisibility;
        var oldB = v.Remove(id);
        _field.AddNodeTriangles(id, node, ReprEnv.SkyVisLeaf, OcclusionVolume.OpaqueOpacity);
        double editMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        var newB = v.SnapshotBounds(id);
        var recs = new List<Vivarium.Sim.Repr.DirtyRecord>();
        if (oldB is { } ob) recs.Add(new(ob, Vivarium.Sim.Repr.DirtyDomain.Vegetation, v.Version + 1, "move", 0));
        recs.Add(new(newB, Vivarium.Sim.Repr.DirtyDomain.Vegetation, v.Version + 1, "move", 0));
        v.Invalidate(recs);
        double bakeMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        long voxBaked = v.BakedVoxelCounter - voxBefore;
        float vo1 = v.Sample(pOld).SkyVisibility, vn1 = v.Sample(pNew).SkyVisibility;
        _field.Upload();
        double upMs = sw.Elapsed.TotalMilliseconds;
        string line = string.Format(CultureInfo.InvariantCulture,
            "dyn move plant {0} -> {1}: deposit/remove {2:0.0}ms local_rebake {3:0.0}ms ({4} voxels baked (grid {5})) upload {6:0.0}ms {7} bytes; sky vis at orig spot {8:0.000}->{9:0.000}, at new spot {10:0.000}->{11:0.000}",
            MovedPlant, wantMoved ? "new" : "orig", editMs, bakeMs, voxBaked, v.VoxelCount, upMs, _field.LastUploadBytes, vo0, vo1, vn0, vn1);
        _ctx.Log(line);
        File.AppendAllText(Path.Combine(_ctx.OutDir, "dyn.txt"), line + "\n");
    }
}
