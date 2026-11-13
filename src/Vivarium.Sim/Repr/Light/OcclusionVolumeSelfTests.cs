using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Light;

internal static class LightTestKit
{
    public static Aabb Box(double x0, double y0, double z0, double x1, double y1, double z1) => new(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

    /// Horizontal quad at height y spanning [x0,x1] x [z0,z1].
    public static (float[] pos, int[] idx) Quad(double y, double x0, double x1, double z0, double z1) =>
        (new[] { (float)x0, (float)y, (float)z0, (float)x1, (float)y, (float)z0, (float)x1, (float)y, (float)z1, (float)x0, (float)y, (float)z1 },
         new[] { 0, 1, 2, 0, 2, 3 });

    /// Vertical quad in the plane x = x0 spanning y [y0,y1], z [z0,z1].
    public static (float[] pos, int[] idx) WallX(double x, double y0, double y1, double z0, double z1) =>
        (new[] { (float)x, (float)y0, (float)z0, (float)x, (float)y0, (float)z1, (float)x, (float)y1, (float)z1, (float)x, (float)y1, (float)z0 },
         new[] { 0, 1, 2, 0, 2, 3 });

    /// n small horizontal leaf triangles at height y scattered over [-r,r]^2 (deterministic).
    public static (float[] pos, int[] idx) Leaves(int n, double y, double r, double size = 0.1, ulong seed = 11)
    {
        var pos = new float[n * 9]; var idx = new int[n * 3];
        for (int i = 0; i < n; i++)
        {
            double cx = (ReprId.Unit(ReprId.Hash(seed, (ulong)i), 1) * 2 - 1) * r, cz = (ReprId.Unit(ReprId.Hash(seed, (ulong)i), 2) * 2 - 1) * r;
            double[] p = { cx, y, cz, cx + size, y, cz, cx, y, cz + size };
            for (int k = 0; k < 9; k++) pos[i * 9 + k] = (float)p[k];
            idx[i * 3] = i * 3; idx[i * 3 + 1] = i * 3 + 1; idx[i * 3 + 2] = i * 3 + 2;
        }
        return (pos, idx);
    }

    public static bool Same(float[] a, float[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }
}

/// Correctness of the cached occlusion volume (open field, slab, leaf cloud, bent normal, locality, remove, determinism).
public sealed class LightOcclusionSelfTest : IReprSelfTest
{
    public string Name => "light.occlusion";

    public string? Run()
    {
        foreach (double voxel in new[] { 0.20, 0.10, 0.05 })
        {
            string? r = Core(voxel);
            if (r != null) return $"voxel {voxel:0.00}: {r}";
        }
        return Locality() ?? Determinism() ?? Heightfield() ?? PackLayout();
    }

    private static Aabb Probe(double x, double y, double z, double v) => LightTestKit.Box(x - 2 * v, y - 2 * v, z - 2 * v, x + 2 * v, y + 2 * v, z + 2 * v);

    private static string? Core(double voxel)
    {
        // Volume 7 x 2.4 x 7 (small enough at 0.05 m when only probe regions are baked).
        var bounds = LightTestKit.Box(-3.5, 0, -3.5, 3.5, 2.4, 3.5);
        var vol = new OcclusionVolume(bounds, voxel);
        double v = voxel;
        // --- open field
        vol.Bake(Probe(0, 0.5, 0, v), expand: false);
        double open = vol.Sample(new Vec3(0, 0.5, 0)).SkyVisibility;
        if (open < 0.99) return $"open field visibility {open:0.000} < 0.99";

        // --- slab at y = 1.0, +-2.5 m, opaque
        var (sp, si) = LightTestKit.Quad(1.0, -2.5, 2.5, -2.5, 2.5);
        vol.AddTriangles(1, sp, si, null, OcclusionVolume.OpaqueOpacity);
        double[] xs = { 0.0, 1.5, 2.4, 3.1 };
        double[] vis = new double[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            vol.Bake(Probe(xs[i], 0.3, 0, v), expand: false);
            vis[i] = vol.Sample(new Vec3(xs[i], 0.3, 0)).SkyVisibility;
        }
        if (vis[0] >= 0.25) return $"slab centre visibility {vis[0]:0.000} >= 0.25";
        if (!(vis[0] < vis[1] && vis[1] < vis[2] && vis[2] < vis[3])) return $"slab visibility not rising toward edge: {string.Join(", ", vis.Select(x => x.ToString("0.000", CultureInfo.InvariantCulture)))}";
        if (vis[3] < 0.65) return $"visibility beyond slab edge {vis[3]:0.000} < 0.65";
        vol.Remove(1);

        // --- leaf cloud: coverage increases, visibility must decrease monotonically
        double prev = 2; var seen = new List<double>();
        foreach (int n in new[] { 0, 40, 160, 640 })
        {
            vol.Remove(2);
            if (n > 0) { var (lp, li) = LightTestKit.Leaves(n, 1.0, 1.0, 0.1); vol.AddTriangles(2, lp, li, null, 1.0); }
            vol.Bake(Probe(0, 0.3, 0, v), expand: false);
            double s = vol.Sample(new Vec3(0, 0.3, 0)).SkyVisibility;
            seen.Add(s);
            if (s >= prev - 1e-6 && n > 0) return $"leaf visibility not decreasing at n={n}: {string.Join(", ", seen.Select(x => x.ToString("0.000", CultureInfo.InvariantCulture)))}";
            prev = s;
        }
        if (seen[0] < 0.99) return "leaf cloud baseline not open";
        if (seen[2] < 0.15 || seen[2] > 0.97) return $"leaf cloud (n=160) not intermediate: {seen[2]:0.000}";
        vol.Remove(2);

        // --- bent normal away from an occluding wall at x = -1 (probe on the +x side)
        var (wp, wi) = LightTestKit.WallX(-1.0, 0, 2.4, -3.5, 3.5);
        vol.AddTriangles(3, wp, wi, null, OcclusionVolume.OpaqueOpacity);
        vol.Bake(Probe(-0.6, 1.2, 0, v), expand: false);
        var smp = vol.Sample(new Vec3(-0.6, 1.2, 0));
        if (smp.BentNormal.X <= 0.02) return $"bent normal x {smp.BentNormal.X:0.000} does not point away from wall";
        if (smp.Open1 >= smp.Open0) return $"Open1 (-X) {smp.Open1:0.000} not below Open0 (+X) {smp.Open0:0.000} near wall";
        return null;
    }

    private static string? Locality()
    {
        var vol = new OcclusionVolume(LightTestKit.Box(-4, 0, -4, 4, 2.4, 4), 0.2);
        var (gp, gi) = LightTestKit.Quad(0.0, -4, 4, -4, 4);
        vol.AddTriangles(OcclusionVolume.HeightfieldId, gp, gi, null, OcclusionVolume.OpaqueOpacity);
        var (lp, li) = LightTestKit.Leaves(60, 1.2, 1.5);
        vol.AddTriangles(5, lp, li, null, 1.0);
        vol.BakeAll();
        var before = vol.SnapshotBaked();
        var ext0 = vol.SnapshotExtinction();

        // Blocker in one corner.
        var (bp, bi) = LightTestKit.Quad(1.5, -3.8, -3.0, -3.8, -3.0);
        var bb = vol.AddTriangles(7, bp, bi, null, OcclusionVolume.OpaqueOpacity);
        vol.Invalidate(new[] { new DirtyRecord(bb, DirtyDomain.Geometry, 1, "grow", 0) });
        var after = vol.SnapshotBaked();
        double reach = vol.TraceDistance + 1.0 * vol.VoxelSize, v = vol.VoxelSize;
        int changedFar = 0, changedNear = 0;
        for (int z = 0; z < vol.Nz; z++)
            for (int y = 0; y < vol.Ny; y++)
                for (int x = 0; x < vol.Nx; x++)
                {
                    var c = vol.VoxelCentre(x, y, z);
                    double dx = Math.Max(0, Math.Max(bb.Min.X - c.X, c.X - bb.Max.X)), dy = Math.Max(0, Math.Max(bb.Min.Y - c.Y, c.Y - bb.Max.Y)), dz = Math.Max(0, Math.Max(bb.Min.Z - c.Z, c.Z - bb.Max.Z));
                    double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    int o = ((z * vol.Ny + y) * vol.Nx + x) * 10;
                    bool same = true; for (int k = 0; k < 10; k++) same &= BitConverter.SingleToInt32Bits(before[o + k]) == BitConverter.SingleToInt32Bits(after[o + k]);
                    if (dist > reach && !same) changedFar++;
                    if (!same) changedNear++;
                }
        if (changedFar != 0) return $"{changedFar} voxels beyond trace distance + 1 voxel changed";
        if (changedNear == 0) return "local invalidation changed nothing";
        double visUnder = vol.Sample(new Vec3(-3.4, 0.5, -3.4)).SkyVisibility;
        var fresh = new OcclusionVolume(LightTestKit.Box(-4, 0, -4, 4, 2.4, 4), 0.2);
        fresh.AddTriangles(OcclusionVolume.HeightfieldId, gp, gi, null, OcclusionVolume.OpaqueOpacity);
        fresh.AddTriangles(5, lp, li, null, 1.0);
        fresh.BakeAll();
        double visBefore = fresh.Sample(new Vec3(-3.4, 0.5, -3.4)).SkyVisibility;
        if (!(visUnder < visBefore - 0.05)) return $"voxels under the blocker did not darken ({visBefore:0.000} -> {visUnder:0.000})";

        // Remove restores exactly.
        var rb = vol.Remove(7)!.Value;
        vol.Invalidate(new[] { new DirtyRecord(rb, DirtyDomain.Geometry, 2, "die", 0) });
        if (!vol.SnapshotExtinction().SequenceEqual(ext0)) return "extinction not restored exactly after Remove";
        if (!LightTestKit.Same(vol.SnapshotBaked(), before)) return "baked values not restored exactly after Remove + local re-bake";
        return null;
    }

    private static string? Determinism()
    {
        byte[] Make()
        {
            var vol = new OcclusionVolume(LightTestKit.Box(-2, 0, -2, 2, 2, 2), 0.1);
            var (lp, li) = LightTestKit.Leaves(100, 1.0, 1.5);
            vol.AddTriangles(1, lp, li, null, 1.0);
            vol.AddHeightfield((x, z) => 0.1 + 0.05 * Math.Sin(x * 3) * Math.Cos(z * 2), LightTestKit.Box(-2, 0, -2, 2, 0, 2));
            vol.BakeAll();
            vol.Bake(LightTestKit.Box(-1, 0, -1, 0, 1, 0)); // repeat a partial bake: must not change anything
            return vol.PackRgba8(out _, out _, out _);
        }
        var a = Make(); var b = Make();
        return a.SequenceEqual(b) ? null : "two identical builds produced different packed bytes";
    }

    private static string? Heightfield()
    {
        var vol = new OcclusionVolume(LightTestKit.Box(-2, -0.5, -2, 2, 1.5, 2), 0.1);
        vol.AddHeightfield((x, z) => 0.2, LightTestKit.Box(-2, 0, -2, 2, 0, 2));
        if (vol.ExtinctionAt(new Vec3(0, -0.2, 0)) < 100) return "below the surface is not opaque";
        if (vol.ExtinctionAt(new Vec3(0, 0.5, 0)) != 0) return "above the surface is not empty";
        vol.Bake(LightTestKit.Box(-0.5, 0.3, -0.5, 0.5, 0.6, 0.5), expand: false);
        double v = vol.Sample(new Vec3(0, 0.5, 0)).SkyVisibility;
        return v > 0.95 ? null : $"flat ground leaves visibility {v:0.000} above it";
    }

    private static string? PackLayout()
    {
        var vol = new OcclusionVolume(LightTestKit.Box(0, 0, 0, 1.0, 0.5, 0.8), 0.1);
        vol.BakeAll();
        var bytes = vol.PackRgba8(out int nx, out int ny, out int nz);
        if (nx != 10 || ny != 5 || nz != 8) return $"pack dims {nx}x{ny}x{nz}";
        if (bytes.Length != 10 * 5 * 8 * 4) return "pack length";
        var part = vol.PackRegion(LightTestKit.Box(0.2, 0.1, 0.1, 0.5, 0.3, 0.4), 0, out int x0, out int y0, out int z0, out int px, out int py, out int pz);
        if (x0 != 2 || y0 != 1 || z0 != 1 || px != 3 || py != 2 || pz != 3 || part.Length != 3 * 2 * 3 * 4) return $"region pack {x0},{y0},{z0} {px}x{py}x{pz}";
        return null;
    }
}

/// Cost report: always passes; the name carries the numbers. 6 x 3 x 6 m at 0.10 m, 12 leaf clouds, ground.
public sealed class LightCostSelfTest : IReprSelfTest
{
    private string _detail = "";
    public string Name => "light.cost" + _detail;

    public string? Run()
    {
        var inv = CultureInfo.InvariantCulture;
        var vol = new OcclusionVolume(LightTestKit.Box(-3, 0, -3, 3, 3, 3), 0.10);
        var sw = Stopwatch.StartNew();
        vol.AddHeightfield((x, z) => 0.0, LightTestKit.Box(-3, 0, -3, 3, 0, 3));
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI * 2 / 12;
            var (lp, li) = LightTestKit.Leaves(2000, 0.6 + 0.04 * i, 0.4, 0.08, (ulong)(100 + i));
            var m = new double[] { 1, 0, 0, Math.Cos(a) * 2, 0, 1, 0, 0, 0, 0, 1, Math.Sin(a) * 2 };
            vol.AddTriangles((ulong)(10 + i), lp, li, m, 1.0);
        }
        double addMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); vol.BakeAll(); double full = sw.Elapsed.TotalMilliseconds;
        var rb = vol.Remove(15)!.Value;
        sw.Restart(); vol.Invalidate(new[] { new DirtyRecord(rb, DirtyDomain.Vegetation, 1, "die", 0) }); double local = sw.Elapsed.TotalMilliseconds;
        long lv = vol.LastBakeVoxels;
        // Larger world (20 x 3 x 20 m): local re-bake cost is independent of the grid size.
        var big = new OcclusionVolume(LightTestKit.Box(-10, 0, -10, 10, 3, 10), 0.10);
        for (int i = 0; i < 12; i++) { var (lp, li) = LightTestKit.Leaves(2000, 0.6, 0.4, 0.08, (ulong)(200 + i)); big.AddTriangles((ulong)(10 + i), lp, li, new double[] { 1, 0, 0, i * 1.5 - 8, 0, 1, 0, 0, 0, 0, 1, 0 }, 1.0); }
        sw.Restart(); big.BakeAll(); double bigFull = sw.Elapsed.TotalMilliseconds;
        var br = big.Remove(15)!.Value;
        sw.Restart(); big.Invalidate(new[] { new DirtyRecord(br, DirtyDomain.Vegetation, 1, "die", 0) }); double bigLocal = sw.Elapsed.TotalMilliseconds;
        long bigLv = big.LastBakeVoxels, bigAll = big.VoxelCount;
        _detail = string.Format(inv, " voxels={0} full_bake={1:0.0}ms local_rebake={2:0.0}ms({3}vox) big_world={8}vox:full={9:0.0}ms/local={10:0.0}ms({11}vox) deposit={4:0.0}ms cpu={5:0.0}MB records={6:0.0}MB tex={7:0.0}MB",
            vol.VoxelCount, full, local, lv, addMs, vol.CpuBytes / 1048576.0, vol.SourceRecordBytes / 1048576.0, vol.PackedBytes / 1048576.0, bigAll, bigFull, bigLocal, bigLv);
        return null;
    }
}
