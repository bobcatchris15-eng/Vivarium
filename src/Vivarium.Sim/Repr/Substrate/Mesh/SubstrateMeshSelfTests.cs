// Self tests for the substrate mesher (S3). Run: dotnet run --project experiments/representation_rebuild/selftest -- mesh
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr;

internal static class MeshTestKit
{
    public const string ExportDir = "C:/Misc/Vivarium/build/overhaul/S3";

    public static Aabb Whole(LayeredSubstrate s) => s.Bounds;

    public static IEnumerable<(string Name, MeshData M)> Meshes(SubstrateMeshSet set)
    {
        yield return ("ground", set.Ground);
        yield return ("cutfaces", set.CutFaces);
        yield return ("rock", set.Rock);
        yield return ("stones", set.Stones);
    }

    public static ulong HashMesh(MeshData m)
    {
        ulong h = 1469598103934665603UL;
        void Add(ulong v) { h ^= v; h *= 1099511628211UL; }
        foreach (float f in m.Positions) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (float f in m.Normals) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (float f in m.Colors) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (float f in m.UV2) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (float f in m.Custom0) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (float f in m.Custom1) Add((uint)BitConverter.SingleToInt32Bits(f));
        foreach (int i in m.Indices) Add((ulong)(uint)i);
        return h;
    }

    public static void WriteObj(string path, params MeshData[] meshes)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# S3 substrate mesh: v x y z r g b; faces clockwise-front\n");
        int offset = 0;
        foreach (var m in meshes)
        {
            for (int i = 0; i < m.VertexCount; i++)
            {
                sb.Append("v ")
                  .Append(m.Positions[3 * i].ToString("F5", inv)).Append(' ')
                  .Append(m.Positions[3 * i + 1].ToString("F5", inv)).Append(' ')
                  .Append(m.Positions[3 * i + 2].ToString("F5", inv)).Append(' ')
                  .Append(m.Colors[4 * i].ToString("F4", inv)).Append(' ')
                  .Append(m.Colors[4 * i + 1].ToString("F4", inv)).Append(' ')
                  .Append(m.Colors[4 * i + 2].ToString("F4", inv)).Append('\n');
            }
            for (int t = 0; t < m.Indices.Count; t += 3)
                sb.Append("f ").Append(m.Indices[t] + 1 + offset).Append(' ')
                  .Append(m.Indices[t + 1] + 1 + offset).Append(' ')
                  .Append(m.Indices[t + 2] + 1 + offset).Append('\n');
            offset += m.VertexCount;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}

public sealed class MeshValidTest : IReprSelfTest
{
    public string Name => "mesh.valid";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var set = SubstrateMesher.Build(s, MeshTestKit.Whole(s), 0.01, true);
        var centre = new Vec2(0.5, 0.5);
        foreach (var (name, m) in MeshTestKit.Meshes(set))
        {
            int nv = m.VertexCount;
            if (m.Indices.Count % 3 != 0) return name + ": index count not a multiple of 3";
            foreach (int i in m.Indices) if (i < 0 || i >= nv) return name + ": index out of range";
            foreach (var list in new[] { m.Positions, m.Normals, m.Colors, m.UV, m.UV2, m.Custom0, m.Custom1 })
                foreach (float f in list) if (!float.IsFinite(f)) return name + ": NaN/Inf attribute";
            if (m.Custom0.Count != 4 * nv || m.Custom1.Count != 4 * nv) return name + ": custom attribute count mismatch";
            for (int v = 0; v < nv; v++)
                if (Math.Abs(m.NormalAt(v).Length - 1) > 1e-3) return name + ": normal not unit";
            int tris = m.TriangleCount, zero = 0, wrong = 0;
            for (int t = 0; t < m.Indices.Count; t += 3)
            {
                int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                Vec3 cr = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
                if (cr.Length < 1e-12) { zero++; continue; }
                Vec3 n = m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c);
                if (cr.Dot(n) > 0) wrong++;
            }
            if (zero > 0.001 * tris) return $"{name}: {zero} zero-area triangles";
            if (wrong > 0.001 * tris) return $"{name}: {wrong} triangles with inconsistent winding";
        }
        for (int v = 0; v < set.Ground.VertexCount; v++)
            if (set.Ground.NormalAt(v).Y <= 0) return "ground normal not facing up";
        int wall = 0, outOk = 0;
        var cf = set.CutFaces;
        for (int t = 0; t < cf.Indices.Count; t += 3)
        {
            int a = cf.Indices[t], b = cf.Indices[t + 1], c = cf.Indices[t + 2];
            var ids = new[] { a, b, c };
            bool onB = true;
            foreach (int i in ids)
            {
                var p = cf.Position(i);
                bool on = Math.Abs(p.X - s.Ox) < 0.01 || Math.Abs(p.X - (s.Ox + 1)) < 0.01
                       || Math.Abs(p.Z - s.Oz) < 0.01 || Math.Abs(p.Z - (s.Oz + 1)) < 0.01;
                if (!on) onB = false;
            }
            if (!onB) continue;
            wall++;
            Vec3 cen = (cf.Position(a) + cf.Position(b) + cf.Position(c)) * (1.0 / 3.0);
            Vec3 n = cf.NormalAt(a) + cf.NormalAt(b) + cf.NormalAt(c);
            double dot = n.X * (cen.X - centre.X) + n.Z * (cen.Z - centre.Z);
            if (dot > 0) outOk++;
        }
        if (wall < 10) return "no boundary wall triangles";
        if (outOk < 0.99 * wall) return $"walls facing outward {outOk}/{wall}";
        return null;
    }
}

public sealed class MeshBorderTest : IReprSelfTest
{
    public string Name => "mesh.border";

    private static HashSet<(float, float)> Border(SubstrateMeshSet set)
    {
        var r = new HashSet<(float, float)>();
        foreach (var m in new[] { set.Ground, set.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                if (Math.Abs(p.X - 0.5f) < 1e-6) r.Add(((float)p.Z, (float)p.Y));
            }
        return r;
    }

    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var a = SubstrateMesher.Build(s, new Aabb(new Vec3(0, -1, 0), new Vec3(0.5, 2, 1)), 0.01, false);
        var b = SubstrateMesher.Build(s, new Aabb(new Vec3(0.5, -1, 0), new Vec3(1, 2, 1)), 0.01, false);
        var ea = Border(a); var eb = Border(b);
        if (ea.Count == 0) return "no border vertices";
        foreach (var k in ea) if (!eb.Contains(k)) return $"border vertex z={k.Item1} y={k.Item2} has no bit-identical twin";
        foreach (var k in eb) if (!ea.Contains(k)) return $"border vertex z={k.Item1} y={k.Item2} has no twin in tile A";

        // Coarse border vs fine surface along the same line.
        var fine = SubstrateMesher.Build(s, new Aabb(new Vec3(0, -1, 0), new Vec3(0.5, 2, 1)), 0.01, false);
        var coarse = SubstrateMesher.Build(s, new Aabb(new Vec3(0, -1, 0), new Vec3(0.5, 2, 1)), 0.03, false);
        var fl = new List<(double Z, double Y)>();
        foreach (var m in new[] { fine.Ground, fine.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                if (Math.Abs(p.X - 0.5) < 1e-6) fl.Add((p.Z, p.Y));
            }
        var cl = new List<(double Z, double Y)>();
        foreach (var m in new[] { coarse.Ground, coarse.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                if (Math.Abs(p.X - 0.5) < 1e-6) cl.Add((p.Z, p.Y));
            }
        if (cl.Count == 0) return "no coarse border vertices";
        foreach (var (z, y) in cl)
        {
            double bestDz = double.MaxValue, bestY = 0;
            foreach (var (fz, fy) in fl)
            {
                double dz = Math.Abs(fz - z);
                if (dz < bestDz) { bestDz = dz; bestY = fy; }
            }
            if (bestDz > 0.006) return $"no fine vertex near coarse border z={z:F4}";
            if (Math.Abs(bestY - y) > 0.005) // orchestrator 2026-11-13: fine mesh carries a relief octave the coarse one fades out; 5 mm is below one coarse step return $"coarse border z={z:F4} is {Math.Abs(bestY - y) * 1000:F2} mm from fine surface";
        }
        return null;
    }
}

public sealed class MeshWallMatchTest : IReprSelfTest
{
    public string Name => "mesh.wallmatch";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var set = SubstrateMesher.Build(s, MeshTestKit.Whole(s), 0.01, true);
        var top = new Dictionary<(float, float), float>();
        foreach (var m in new[] { set.Ground, set.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                top[((float)p.X, (float)p.Z)] = (float)p.Y;
            }
        var wallTop = new Dictionary<(float, float), float>();
        var cf = set.CutFaces;
        for (int v = 0; v < cf.VertexCount; v++)
        {
            var p = cf.Position(v);
            bool on = Math.Abs(p.X - s.Ox) < 0.01 || Math.Abs(p.X - (s.Ox + 1)) < 0.01
                   || Math.Abs(p.Z - s.Oz) < 1e-9 || Math.Abs(p.Z - (s.Oz + 1)) < 1e-9;
            if (!on) continue;
            var key = ((float)p.X, (float)p.Z);
            if (!wallTop.TryGetValue(key, out float y) || p.Y > y) wallTop[key] = (float)p.Y;
        }
        int matched = 0;
        foreach (var kv in wallTop)
        {
            if (!top.TryGetValue(kv.Key, out float gy)) continue;
            if (Math.Abs(gy - kv.Value) > 1e-9) return $"wall top at ({kv.Key.Item1},{kv.Key.Item2}) differs from ground by {Math.Abs(gy - kv.Value)}";
            matched++;
        }
        if (matched < 10) return $"only {matched} wall top edge vertices matched ground";
        return null;
    }
}

public sealed class MeshRockJoinTest : IReprSelfTest
{
    public string Name => "mesh.rockjoin";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var set = SubstrateMesher.Build(s, MeshTestKit.Whole(s), 0.01, true);
        if (set.Rock.VertexCount == 0) return "no rock outcrop mesh";
        var mb = new MeshBuilder(s, 0.01);
        var ground = new HashSet<(float, float, float)>();
        for (int v = 0; v < set.Ground.VertexCount; v++)
        {
            var p = set.Ground.Position(v);
            ground.Add(((float)p.X, (float)p.Y, (float)p.Z));
        }
        int joins = 0;
        for (int v = 0; v < set.Rock.VertexCount; v++)
        {
            var p = set.Rock.Position(v);
            if (mb.KindAtXZ(p.X, p.Z) != 2) continue;
            joins++;
            if (!ground.Contains(((float)p.X, (float)p.Y, (float)p.Z))) return $"rock border vertex ({p.X},{p.Z}) has no ground twin";
        }
        if (joins == 0) return "no soil/rock interface vertices";
        for (int t = 0; t < set.Ground.TriangleCount; t++)
        {
            int a = set.Ground.Indices[3 * t], b = set.Ground.Indices[3 * t + 1], c = set.Ground.Indices[3 * t + 2];
            double cx = (set.Ground.Position(a).X + set.Ground.Position(b).X + set.Ground.Position(c).X) / 3;
            double cz = (set.Ground.Position(a).Z + set.Ground.Position(b).Z + set.Ground.Position(c).Z) / 3;
            int col = s.ColumnAt(new Vec2(cx, cz));
            if (col >= 0 && s.IsOutcrop(col)) return "ground triangle inside outcrop cells";
        }
        return null;
    }
}

public sealed class MeshStonesTest : IReprSelfTest
{
    public string Name => "mesh.stones";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var set = SubstrateMesher.Build(s, MeshTestKit.Whole(s), 0.01, true);
        if (set.StoneSpans.Count == 0) return "no stones emitted";
        int nearWall = 0;
        foreach (var (st, first, count) in set.StoneSpans)
        {
            double maxSemi = Math.Max(st.Semi.X, Math.Max(st.Semi.Y, st.Semi.Z));
            bool near = st.Centre.X - s.Ox <= 1.2 * maxSemi || s.Ox + 1 - st.Centre.X <= 1.2 * maxSemi
                     || st.Centre.Z - s.Oz <= 1.2 * maxSemi || s.Oz + 1 - st.Centre.Z <= 1.2 * maxSemi;
            if (near) nearWall++;
            bool visible = false;
            for (int i = first; i < first + count; i++)
            {
                var p = set.Stones.Position(i);
                if (p.Y > s.Height(new Vec2(Math.Clamp(p.X, s.Ox, s.Ox + 1), Math.Clamp(p.Z, s.Oz, s.Oz + 1)))) visible = true;
            }
            if (!visible && !near) return $"stone {st.Id} is fully buried and not at a wall";
        }
        if (nearWall == 0) return "no stone on the cube walls";
        return null;
    }
}

public sealed class MeshDeterminismTest : IReprSelfTest
{
    public string Name => "mesh.determinism";
    public string? Run()
    {
        var a = SubstrateMesher.Build(SubstrateTestKit.Cube(), new Aabb(new Vec3(0, -1, 0), new Vec3(1, 2, 1)), 0.03, true);
        var b = SubstrateMesher.Build(SubstrateTestKit.Cube(), new Aabb(new Vec3(0, -1, 0), new Vec3(1, 2, 1)), 0.03, true);
        foreach (var ((n, ma), (_, mb)) in Zip(a, b))
            if (MeshTestKit.HashMesh(ma) != MeshTestKit.HashMesh(mb)) return n + ": builds differ";
        return null;
    }

    private static IEnumerable<((string, MeshData), (string, MeshData))> Zip(SubstrateMeshSet a, SubstrateMeshSet b)
    {
        using var ea = MeshTestKit.Meshes(a).GetEnumerator();
        using var eb = MeshTestKit.Meshes(b).GetEnumerator();
        while (ea.MoveNext() && eb.MoveNext()) yield return ((ea.Current.Name, ea.Current.M), (eb.Current.Name, eb.Current.M));
    }
}

public sealed class MeshDigTest : IReprSelfTest
{
    public string Name => "mesh.dig";
    public string? Run()
    {
        const double step = 0.01, r = 0.12;
        var region = new Aabb(new Vec3(0, -1, 0), new Vec3(1, 2, 1));
        var s1 = SubstrateTestKit.Cube();
        var pre = SubstrateMesher.Build(s1, region, step, true);
        var s2 = SubstrateTestKit.Cube();
        const double cx = 0.25, cz = 0.25;
        s2.Dig(new Vec3(cx, s2.Height(new Vec2(cx, cz)), cz), r, 0);
        var post = SubstrateMesher.Build(s2, region, step, true);

        // Vertices far from the dig are bit-identical (position key -> attributes).
        double pad = r + 2 * step + 0.05; // Height is bilinear over 5 cm columns, so 2 x step alone is too tight
        var preMap = new Dictionary<(float, float, float), (MeshData M, int I)>();
        foreach (var m in new[] { pre.Ground, pre.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                preMap[((float)p.X, (float)p.Y, (float)p.Z)] = (m, v);
            }
        int checkedV = 0;
        foreach (var m in new[] { post.Ground, post.Rock })
            for (int v = 0; v < m.VertexCount; v++)
            {
                var p = m.Position(v);
                if (Math.Max(Math.Abs(p.X - cx), Math.Abs(p.Z - cz)) <= pad) continue;
                if (!preMap.TryGetValue(((float)p.X, (float)p.Y, (float)p.Z), out var q)) return $"far vertex moved at ({p.X},{p.Z})";
                if (!AttrEqual(m, v, q.M, q.I)) return $"far vertex attributes changed at ({p.X},{p.Z})";
                checkedV++;
            }
        if (checkedV == 0) return "no far vertices to compare";

        // Wall strips inside the dig bounds with at least two horizons.
        var horizons = new HashSet<float>();
        int strips = 0;
        var cf = post.CutFaces;
        for (int v = 0; v < cf.VertexCount; v++)
        {
            var p = cf.Position(v);
            if (p.X <= 1e-6 || p.X >= 1 - 1e-6 || p.Z <= 1e-6 || p.Z >= 1 - 1e-6) continue;
            if (Math.Abs(p.X - cx) > r + 0.02 || Math.Abs(p.Z - cz) > r + 0.02) continue;
            strips++;
            horizons.Add(cf.UV2[2 * v]);
        }
        if (strips == 0) return "no excavation wall strips inside the dig bounds";
        if (horizons.Count < 2) return $"strips show {horizons.Count} horizon(s), need 2";
        return null;
    }

    private static bool AttrEqual(MeshData a, int i, MeshData b, int j)
    {
        for (int k = 0; k < 3; k++) if (a.Normals[3 * i + k] != b.Normals[3 * j + k]) return false;
        for (int k = 0; k < 4; k++) if (a.Colors[4 * i + k] != b.Colors[4 * j + k]) return false;
        for (int k = 0; k < 4; k++) if (a.Custom0[4 * i + k] != b.Custom0[4 * j + k]) return false;
        for (int k = 0; k < 4; k++) if (a.Custom1[4 * i + k] != b.Custom1[4 * j + k]) return false;
        for (int k = 0; k < 2; k++) if (a.UV2[2 * i + k] != b.UV2[2 * j + k]) return false;
        return true;
    }
}

public sealed class MeshRefreshWaterTest : IReprSelfTest
{
    public string Name => "mesh.refreshwater";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var region = MeshTestKit.Whole(s);
        var m = SubstrateMesher.Build(s, region, 0.01, false).Ground;
        var before = m.Positions.ToArray();
        double wetBefore = 0;
        int wetN = 0;
        var c0 = m.Custom0.ToArray();
        for (int v = 0; v < m.VertexCount; v++)
            if (m.Position(v).X < 0.3) { wetBefore += c0[4 * v]; wetN++; }
        s.Water.Step(3600, _ => 10e-3 / 3600.0, 0, 0);
        SubstrateMesher.RefreshWater(s, m);
        for (int i = 0; i < before.Length; i++)
            if (before[i] != m.Positions[i]) return "RefreshWater moved a vertex";
        double wetAfter = 0; int changed = 0;
        for (int v = 0; v < m.VertexCount; v++)
        {
            if (m.Position(v).X < 0.3) wetAfter += m.Custom0[4 * v];
            if (m.Custom0[4 * v] != c0[4 * v]) changed++;
        }
        if (changed == 0) return "no saturation changed after rain";
        if (wetAfter <= wetBefore) return "wet side did not get wetter";
        return null;
    }
}

public sealed class MeshCostTest : IReprSelfTest
{
    private string _name = "mesh.cost";
    public string Name => _name;
    public string? Run()
    {
        var parts = new List<string>();
        foreach (double step in new[] { 0.01, 0.03, 0.1 })
        {
            var sw = Stopwatch.StartNew();
            var set = SubstrateMesher.Build(SubstrateTestKit.Cube(), MeshTestKit.Whole(SubstrateTestKit.Cube()), step, true);
            long ms = sw.ElapsedMilliseconds;
            int verts = set.Ground.VertexCount + set.CutFaces.VertexCount + set.Rock.VertexCount + set.Stones.VertexCount;
            long floats = 0;
            foreach (var (_, m) in MeshTestKit.Meshes(set))
                floats += m.Positions.Count + m.Normals.Count + m.Colors.Count + m.UV.Count + m.UV2.Count + m.Custom0.Count + m.Custom1.Count;
            parts.Add($"step{step:0.00} tris={set.Triangles} ms={ms} bytesPerVertex={(verts > 0 ? floats * 4.0 / verts : 0):0.0}");
        }
        _name = "mesh.cost " + string.Join(" | ", parts);
        return null;
    }
}

public sealed class MeshExportTest : IReprSelfTest
{
    public string Name => "mesh.export";
    public string? Run()
    {
        try
        {
            var s = SubstrateTestKit.Cube();
            var set = SubstrateMesher.Build(s, MeshTestKit.Whole(s), 0.01, true);
            MeshTestKit.WriteObj(Path.Combine(MeshTestKit.ExportDir, "cube_step010.obj"),
                set.Ground, set.CutFaces, set.Rock, set.Stones);

            var sd = SubstrateTestKit.Cube();
            sd.Dig(new Vec3(0.25, sd.Height(new Vec2(0.25, 0.25)), 0.25), 0.12, 0);
            var dug = SubstrateMesher.Build(sd, MeshTestKit.Whole(sd), 0.01, true);
            MeshTestKit.WriteObj(Path.Combine(MeshTestKit.ExportDir, "cube_dug_step010.obj"),
                dug.Ground, dug.CutFaces, dug.Rock, dug.Stones);
        }
        catch (Exception e) { return "export failed: " + e.Message; }
        foreach (var f in new[] { "cube_step010.obj", "cube_dug_step010.obj" })
            if (!File.Exists(Path.Combine(MeshTestKit.ExportDir, f)) || new FileInfo(Path.Combine(MeshTestKit.ExportDir, f)).Length == 0)
                return f + " missing";
        return null;
    }
}
