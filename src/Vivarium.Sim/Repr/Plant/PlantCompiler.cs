using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>One flat organ (leaf, leaflet, flower, fruit) placed by a row-major 3x4 plant-local transform.</summary>
public struct OrganInstance
{
    public ushort ShapeId;
    public float M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;   // p' = M * (p, 1)
    public uint Seed;
    public float Maturity, Health;
}

public sealed class PlantAssembly
{
    public MeshData Wood = new();
    public OrganInstance[] Instances = Array.Empty<OrganInstance>();
    public int WoodTriangles;
    public long DescriptorBytes;
}

/// <summary>
/// CACHE_ASSEMBLY data path: organ graph -> wood mesh + instanced organ shapes. Pure data, no Godot.
///
/// VERTEX CONVENTIONS (followed by flora.gdshader):
///   * Wood (axes / petioles): UV.x = metres along the axis from its base (surface attachment),
///     UV.y = 0..1 around the circumference. UV2.x = 2 for woody tissue (bark), UV2.x = 0 for green stems,
///     UV2.y = 0 for all stems (non-foliage).
///   * Lamina (leaf, leaflet): UV.x = 0 base .. 1 tip along the midrib, UV.y = 0..1 across the blade,
///     UV2.y = 1 (foliage), UV2.x = 0 (adaxial/abaxial faces handled by the shader).
///   * Flower / fruit: UV2 = (1.45, 0.92) for bells and chalices (legacy glowbriar convention),
///     (1.75, 0.98) for fruit.
///   * COLOR: sRGB-encoded. The shader linearises COLOR, so linear descriptor colours are encoded here.
///   * Normals: smooth normals; tubes have analytic radial normals, laminae use RecomputeNormals.
/// Instance transform: columns are X (= Up x Z, scaled by real half width / profile max), Y (= Up, orthogonal
/// to Z), Z (= dir * real length), translation = Start. Unit shape: base at origin, length 1 along +Z.
/// </summary>
public static class PlantCompiler
{
    public const double FlatLeafAreaFactor = 0.7;

    public static bool IsFlat(OrganType t) => t is OrganType.Leaf or OrganType.Leaflet or OrganType.Flower or OrganType.Fruit;

    public static double FlatArea(Organ o) => o.Type is OrganType.Leaf or OrganType.Leaflet
        ? 2.0 * o.RadiusStart * (o.End - o.Start).Length * FlatLeafAreaFactor
        : 0.0;

    /// <summary>Bytes of the descriptor as stored: 96 bytes per organ (float32 fields) plus shape tables.</summary>
    public static long DescriptorBytes(PlantDescriptor p)
    {
        long bytes = 96L * p.Organs.Length;
        foreach (var s in p.Shapes) bytes += 4L * s.WidthProfile.Length + 64;
        return bytes;
    }

    public static PlantAssembly Compile(PlantDescriptor p, int detail)
    {
        detail = Math.Clamp(detail, 0, 2);
        var a = new PlantAssembly();
        var inst = new List<OrganInstance>();
        for (int i = 0; i < p.Organs.Length; i++)
        {
            var o = p.Organs[i];
            if (o.Type is OrganType.Axis or OrganType.Petiole)
            {
                SweepAxis(a.Wood, p, i, detail);
            }
            else if (IsFlat(o.Type) && o.RadiusStart > 1e-6f && (o.End - o.Start).LengthSq > 1e-12)
            {
                inst.Add(MakeInstance(p, i));
            }
        }
        a.Instances = inst.ToArray();
        a.WoodTriangles = a.Wood.TriangleCount;
        a.DescriptorBytes = DescriptorBytes(p);
        ComputeAggregates(p);
        return a;
    }

    /// <summary>Shape mesh shared per species; built once per (shape, detail).</summary>
    public static MeshData BuildShapeMesh(OrganShape s, int detail)
    {
        detail = Math.Clamp(detail, 0, 2);
        return s.Type switch
        {
            OrganType.Flower => BuildBell(s, detail, false),
            OrganType.Fruit => BuildBell(s, detail, true),
            _ => BuildLamina(s, detail),
        };
    }

    /// <summary>Bakes the wood and every instance into one mesh (tests and simple viewing).</summary>
    public static MeshData Flatten(PlantDescriptor p, PlantAssembly a, MeshData[] shapeMeshes)
    {
        var m = new MeshData();
        m.Append(a.Wood);
        foreach (var f in a.Instances)
        {
            var sm = shapeMeshes[f.ShapeId];
            int b0 = m.VertexCount;
            double[,] A =
            {
                { f.M00, f.M01, f.M02 }, { f.M10, f.M11, f.M12 }, { f.M20, f.M21, f.M22 },
            };
            var C = Cofactor(A);
            for (int v = 0; v < sm.VertexCount; v++)
            {
                var pv = sm.Position(v);
                var nv = sm.NormalAt(v);
                var q = new Vec3(
                    A[0, 0] * pv.X + A[0, 1] * pv.Y + A[0, 2] * pv.Z + f.M03,
                    A[1, 0] * pv.X + A[1, 1] * pv.Y + A[1, 2] * pv.Z + f.M13,
                    A[2, 0] * pv.X + A[2, 1] * pv.Y + A[2, 2] * pv.Z + f.M23);
                var n = new Vec3(
                    C[0, 0] * nv.X + C[0, 1] * nv.Y + C[0, 2] * nv.Z,
                    C[1, 0] * nv.X + C[1, 1] * nv.Y + C[1, 2] * nv.Z,
                    C[2, 0] * nv.X + C[2, 1] * nv.Y + C[2, 2] * nv.Z).Normalized();
                m.AddVertex(q, n, sm.Colors[v * 4], sm.Colors[v * 4 + 1], sm.Colors[v * 4 + 2], sm.Colors[v * 4 + 3],
                    sm.UV[v * 2], sm.UV[v * 2 + 1], sm.UV2[v * 2], sm.UV2[v * 2 + 1]);
            }
            foreach (int idx in sm.Indices) m.Indices.Add(b0 + idx);
        }
        return m;
    }

    // ------------------------------------------------------------------ instances

    private static OrganInstance MakeInstance(PlantDescriptor p, int i)
    {
        var o = p.Organs[i];
        var s = p.Shapes[o.ShapeId];
        var axis = o.End - o.Start;
        double len = axis.Length;
        var z = axis / len;
        var y = o.Up - z * o.Up.Dot(z);
        y = y.LengthSq > 1e-12 ? y.Normalized() : PlantMath.Perpendicular(z);
        var x = y.Cross(z);
        double rs = Math.Max(o.RadiusStart, 1e-6);
        double sx, sy;
        if (o.Type is OrganType.Flower or OrganType.Fruit)
        {
            sx = sy = rs / 0.5;   // bell unit mouth radius is 0.5
        }
        else
        {
            sx = rs / ProfileMax(s);
            sy = 1.0;
        }
        var zl = z * len;
        return new OrganInstance
        {
            ShapeId = o.ShapeId,
            M00 = (float)(x.X * sx), M01 = (float)(y.X * sy), M02 = (float)zl.X, M03 = (float)o.Start.X,
            M10 = (float)(x.Y * sx), M11 = (float)(y.Y * sy), M12 = (float)zl.Y, M13 = (float)o.Start.Y,
            M20 = (float)(x.Z * sx), M21 = (float)(y.Z * sy), M22 = (float)zl.Z, M23 = (float)o.Start.Z,
            Seed = o.Seed, Maturity = o.Maturity, Health = o.Health,
        };
    }

    private static double[,] Cofactor(double[,] A)
    {
        var C = new double[3, 3];
        C[0, 0] = A[1, 1] * A[2, 2] - A[1, 2] * A[2, 1];
        C[0, 1] = -(A[1, 0] * A[2, 2] - A[1, 2] * A[2, 0]);
        C[0, 2] = A[1, 0] * A[2, 1] - A[1, 1] * A[2, 0];
        C[1, 0] = -(A[0, 1] * A[2, 2] - A[0, 2] * A[2, 1]);
        C[1, 1] = A[0, 0] * A[2, 2] - A[0, 2] * A[2, 0];
        C[1, 2] = -(A[0, 0] * A[2, 1] - A[0, 1] * A[2, 0]);
        C[2, 0] = A[0, 1] * A[1, 2] - A[0, 2] * A[1, 1];
        C[2, 1] = -(A[0, 0] * A[1, 2] - A[0, 2] * A[1, 0]);
        C[2, 2] = A[0, 0] * A[1, 1] - A[0, 1] * A[1, 0];
        return C;
    }

    // ------------------------------------------------------------------ wood

    private static double[] WoodRgb(Tissue tissue, ushort order) => tissue switch
    {
        Tissue.Green => new[] { 0.22, 0.36, 0.22 },
        Tissue.Bark => new[] { 0.28, 0.24, 0.20 },
        _ => order == 0 ? new[] { 0.30, 0.26, 0.20 } : new[] { 0.34, 0.29, 0.22 },
    };

    /// <summary>
    /// Sweeps one axis as a tube along its exposed Hermite curve. The base is sunk one parent radius back into the
    /// parent and flares to min(parentRadius, 1.6 * RadiusStart), blending back over about two radii. The tip is
    /// closed with a rounded cap.
    /// </summary>
    private static void SweepAxis(MeshData m, PlantDescriptor p, int idx, int detail)
    {
        var o = p.Organs[idx];
        var p0 = o.Start;
        var p1 = o.End;
        double len = (p1 - p0).Length;
        if (len < 1e-5 || o.RadiusStart <= 1e-6f) return;

        double rs = o.RadiusStart;
        double re = Math.Max(o.RadiusEnd, 0.00025);
        double rParent = rs, flare = rs, dExt = 0;
        if (o.Parent >= 0)
        {
            var q = p.Organs[o.Parent];
            double u = q.Maturity > 1e-4f ? Math.Clamp(o.AttachT / q.Maturity, 0, 1) : 0;
            rParent = q.RadiusStart + (q.RadiusEnd - q.RadiusStart) * u;
            flare = Math.Min(rParent, 1.6 * rs);
            dExt = rParent;
        }

        var t0 = o.StartTangent;
        var t1 = o.EndTangent;
        double t0len = t0.Length;
        double sMin = dExt > 0 && t0len > 1e-9 ? -Math.Min(0.5, dExt / t0len) : 0;
        double seg = detail == 0 ? 0.02 : detail == 1 ? 0.035 : 0.06;
        int nSeg = Math.Clamp((int)Math.Ceiling(len / seg), 2, detail == 0 ? 14 : 8);
        int nRad = detail == 0 ? (o.Order <= 1 ? 10 : 6) : detail == 1 ? (o.Order <= 1 ? 6 : 4) : (o.Order <= 1 ? 4 : 3);

        // Sample list: optional sink sample, then s = 0..1.
        var samples = new List<(Vec3 P, Vec3 T, double R, double D)>();
        if (sMin < 0) AddSample(samples, p0, t0, p1, t1, sMin, rs, re, flare, len, true);
        for (int k = 0; k <= nSeg; k++) AddSample(samples, p0, t0, p1, t1, k / (double)nSeg, rs, re, flare, len, false);

        var rgb = WoodRgb(o.Tissue, o.Order);
        bool woody = o.Tissue is Tissue.Wood or Tissue.Bark;
        double u2x = woody ? 2.0 : 0.0;
        var rings = new int[samples.Count][];
        Vec3 nPrev = PlantMath.Perpendicular(samples[0].T);
        for (int k = 0; k < samples.Count; k++)
        {
            var sm = samples[k];
            var tN = sm.T;
            var nk = PlantMath.SafeNormal(nPrev - tN * nPrev.Dot(tN), PlantMath.Perpendicular(tN));
            nPrev = nk;
            var bk = tN.Cross(nk);
            rings[k] = new int[nRad];
            for (int j = 0; j < nRad; j++)
            {
                double th = Math.Tau * j / nRad;
                var dv = nk * Math.Cos(th) + bk * Math.Sin(th);
                rings[k][j] = m.AddVertex(sm.P + dv * sm.R, dv, rgb[0], rgb[1], rgb[2], 1,
                    sm.D, j / (double)nRad, u2x, 0);
            }
            if (k > 0) Strip(m, rings[k - 1], rings[k]);
        }

        // Rounded tip: two arc rings then an apex.
        var last = samples[^1];
        var tip = last.T;
        var nL = nPrev;
        var bL = tip.Cross(nL);
        double rEnd = last.R;
        int[] prev = rings[^1];
        for (int q = 1; q <= 2; q++)
        {
            double phi = q / 3.0 * Math.PI / 2;
            var cur = new int[nRad];
            for (int j = 0; j < nRad; j++)
            {
                double th = Math.Tau * j / nRad;
                var dv = nL * Math.Cos(th) + bL * Math.Sin(th);
                var pos = last.P + dv * (rEnd * Math.Cos(phi)) + tip * (rEnd * Math.Sin(phi));
                var nrm = (dv * Math.Cos(phi) + tip * Math.Sin(phi)).Normalized();
                cur[j] = m.AddVertex(pos, nrm, rgb[0], rgb[1], rgb[2], 1, last.D, j / (double)nRad, u2x, 0);
            }
            Strip(m, prev, cur);
            prev = cur;
        }
        int apex = m.AddVertex(last.P + tip * rEnd, tip, rgb[0], rgb[1], rgb[2], 1, last.D, 0, u2x, 0);
        for (int j = 0; j < nRad; j++)
        {
            int jn = (j + 1) % nRad;
            m.AddTriangle(prev[j], prev[jn], apex);
        }
    }

    private static void AddSample(List<(Vec3 P, Vec3 T, double R, double D)> list, Vec3 p0, Vec3 t0, Vec3 p1, Vec3 t1,
        double s, double rs, double re, double flare, double len, bool sink)
    {
        var pos = PlantMath.Hermite(p0, t0, p1, t1, s);
        var tan = PlantMath.HermiteD(p0, t0, p1, t1, s);
        if (tan.LengthSq < 1e-18) tan = p1 - p0;
        tan = tan.Normalized();
        double d = Math.Max(0, s) * len;
        double r;
        if (s < 0) r = flare;
        else
        {
            double rLin = rs + (re - rs) * s;
            double w = Math.Exp(-d / (2 * Math.Max(rs, 1e-4)));
            r = rLin + (flare - rLin) * w;
        }
        list.Add((pos, tan, r, d));
    }

    private static void Strip(MeshData m, int[] a, int[] b)
    {
        int n = a.Length;
        for (int j = 0; j < n; j++)
        {
            int jn = (j + 1) % n;
            m.AddTriangle(a[j], b[j], a[jn]);
            m.AddTriangle(a[jn], b[j], b[jn]);
        }
    }

    // ------------------------------------------------------------------ shape meshes

    private static double ProfileMax(OrganShape s)
    {
        double mx = 1e-6;
        foreach (var v in s.WidthProfile) mx = Math.Max(mx, v);
        return mx;
    }

    private static double ProfileAt(OrganShape s, double t)
    {
        var w = s.WidthProfile;
        if (w.Length == 0) return 0;
        if (w.Length == 1) return w[0];
        double x = Math.Clamp(t, 0, 1) * (w.Length - 1);
        int i = Math.Min((int)Math.Floor(x), w.Length - 2);
        double f = x - i;
        return w[i] + (w[i + 1] - w[i]) * f;
    }

    private static double Enc(double c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    }

    private static double[] EncRgb(Vec3 lin) => new[] { Enc(lin.X), Enc(lin.Y), Enc(lin.Z) };

    private static MeshData BuildLamina(OrganShape s, int detail)
    {
        var m = new MeshData();
        int L = detail == 0 ? 10 : detail == 1 ? 5 : 2;
        int W = detail == 0 ? 4 : detail == 1 ? 2 : 1;
        double pmax = ProfileMax(s);
        var cA = EncRgb(s.ColorAdaxial);
        var cB = EncRgb(s.ColorAbaxial);
        double k = s.MidribCurl;
        var idx = new int[L + 1, W + 1];
        for (int i = 0; i <= L; i++)
        {
            double t = i / (double)L;
            double th = k * t;
            double zc, yc;
            if (Math.Abs(k) < 1e-4) { zc = t; yc = 0; }
            else { zc = Math.Sin(th) / k; yc = -(1 - Math.Cos(th)) / k; }
            double cy = Math.Cos(th), sy = Math.Sin(th);     // midrib normal (0, cy, sy)
            double pw = ProfileAt(s, t);
            for (int j = 0; j <= W; j++)
            {
                double u = j / (double)W;
                double x = (2 * u - 1) * pw;
                double frac = pw > 1e-6 ? Math.Abs(x) / pw : 0;
                double cup = s.CrossCurl * x * x * 3.0;
                double phi = s.Twist * t;
                double xr = x * Math.Cos(phi);
                double off = x * Math.Sin(phi) + cup
                    + s.EdgeWave * 0.1 * Math.Sin(s.EdgeWaveFreq * Math.Tau * t) * frac * frac;
                if (detail == 0 && (j == 0 || j == W) && s.Serration > 0)
                    xr *= 1 - s.Serration * 0.5 * (1 + Math.Cos(s.SerrationFreq * Math.Tau * t));
                if (detail == 0 && j == W / 2) off += 0.06 * pmax;   // raised midrib ridge
                var pos = new Vec3(xr, yc + cy * off, zc + sy * off);
                var col = Mix(cA, cB, 0.25 + 0.5 * frac);
                // Analytic adaxial normal from the midrib frame (RecomputeNormals leaves base/tip rows at zero,
                // where the profile collapses to a point).
                idx[i, j] = m.AddVertex(pos, new Vec3(0, cy, sy), col[0], col[1], col[2], 1, t, u, 0, 1);
            }
        }
        for (int i = 0; i < L; i++)
        {
            for (int j = 0; j < W; j++)
            {
                int a = idx[i, j], b = idx[i, j + 1], c = idx[i + 1, j], d = idx[i + 1, j + 1];
                m.AddTriangle(a, c, b);
                m.AddTriangle(b, c, d);
            }
        }
        return m;
    }

    private static double[] Mix(double[] a, double[] b, double t) =>
        new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };

    /// <summary>Revolved bell (flower) or ellipsoid (fruit), base at origin, length 1 along +Z, mouth radius 0.5.</summary>
    private static MeshData BuildBell(OrganShape s, int detail, bool ellipsoid)
    {
        var m = new MeshData();
        int R = detail == 0 ? 8 : detail == 1 ? 6 : 4;
        int G = detail == 0 ? 5 : detail == 1 ? 3 : 2;
        var cA = EncRgb(s.ColorAdaxial);
        double u2x = ellipsoid ? 1.75 : 1.45, u2y = ellipsoid ? 0.98 : 0.92;
        var ids = new int[G + 1, R];
        for (int k = 0; k <= G; k++)
        {
            double z = k / (double)G;
            double r = ellipsoid
                ? Math.Max(0.02, 0.5 * Math.Sin(Math.PI * z))
                : 0.5 * (0.25 + 0.75 * Math.Pow(z, 0.8));
            for (int j = 0; j < R; j++)
            {
                double th = Math.Tau * j / R;
                double scallop = (!ellipsoid && detail == 0 && k == G) ? 1 + 0.06 * Math.Cos(5 * th) : 1;
                var pos = new Vec3(r * scallop * Math.Cos(th), r * scallop * Math.Sin(th), z);
                var col = Mix(cA, EncRgb(s.ColorAbaxial), 0.15 * z);
                ids[k, j] = m.AddVertex(pos, Vec3.Forward, col[0], col[1], col[2], 1, j / (double)R, z, u2x, u2y);
            }
        }
        for (int k = 0; k < G; k++)
        {
            for (int j = 0; j < R; j++)
            {
                int jn = (j + 1) % R;
                int a = ids[k, j], b = ids[k, jn], c = ids[k + 1, j], d = ids[k + 1, jn];
                m.AddTriangle(a, c, b);
                m.AddTriangle(b, c, d);
            }
        }
        // Base cap (bell only): closes the flower at the pedicel end.
        if (!ellipsoid)
        {
            int centre = m.AddVertex(Vec3.Zero, -Vec3.Forward, cA[0], cA[1], cA[2], 1, 0, 0, u2x, u2y);
            for (int j = 0; j < R; j++) m.AddTriangle(centre, ids[0, (j + 1) % R], ids[0, j]);
        }
        m.RecomputeNormals();
        return m;
    }

    // ------------------------------------------------------------------ aggregates

    private static int AncestorAtOrder(PlantDescriptor p, int i, int order)
    {
        int j = i;
        while (j >= 0 && p.Organs[j].Order > order) j = p.Organs[j].Parent;
        return j;
    }

    /// <summary>
    /// Clusters leaves by the order-1 branch that carries them (order-2 if a cluster has more than 12 leaves) and
    /// fills p.Aggregates.
    /// </summary>
    private static void ComputeAggregates(PlantDescriptor p)
    {
        var byKey = new Dictionary<int, List<int>>();
        for (int i = 0; i < p.Organs.Length; i++)
        {
            var o = p.Organs[i];
            if (!(o.Type is OrganType.Leaf or OrganType.Leaflet) || FlatArea(o) <= 0) continue;
            int key = AncestorAtOrder(p, i, 1);
            if (!byKey.TryGetValue(key, out var list)) byKey[key] = list = new List<int>();
            list.Add(i);
        }

        var result = new List<FoliageAggregate>();
        foreach (var kv in byKey)
        {
            if (kv.Value.Count <= 12)
            {
                result.Add(Aggregate(p, kv.Value));
                continue;
            }
            var byKey2 = new Dictionary<int, List<int>>();
            foreach (int i in kv.Value)
            {
                int key2 = AncestorAtOrder(p, i, 2);
                if (key2 == i) key2 = kv.Key;   // leaves that are themselves order 2 (moonfern pinnae) stay with their branch
                if (!byKey2.TryGetValue(key2, out var l2)) byKey2[key2] = l2 = new List<int>();
                l2.Add(i);
            }
            foreach (var g in byKey2.Values) result.Add(Aggregate(p, g));
        }
        p.Aggregates = result.ToArray();
    }

    private static FoliageAggregate Aggregate(PlantDescriptor p, List<int> members)
    {
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var mx = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        double area = 0, tr = 0, ro = 0;
        var nSum = Vec3.Zero;
        var cSum = Vec3.Zero;
        foreach (int i in members)
        {
            var o = p.Organs[i];
            var s = p.Shapes[o.ShapeId];
            double rad = Math.Max(o.RadiusStart, o.RadiusEnd);
            foreach (var q in new[] { o.Start, o.End })
            {
                mn = new Vec3(Math.Min(mn.X, q.X - rad), Math.Min(mn.Y, q.Y - rad), Math.Min(mn.Z, q.Z - rad));
                mx = new Vec3(Math.Max(mx.X, q.X + rad), Math.Max(mx.Y, q.Y + rad), Math.Max(mx.Z, q.Z + rad));
            }
            area += FlatArea(o);
            nSum += o.Up;
            cSum += s.ColorAdaxial;
            tr += s.Transmission;
            ro += s.Roughness;
        }
        int n = Math.Max(1, members.Count);
        double proj = Math.Max((mx.X - mn.X) * (mx.Z - mn.Z), 1e-6);
        return new FoliageAggregate
        {
            Bounds = new Aabb(mn, mx),
            LeafArea = (float)area,
            Opacity = (float)Math.Clamp(1 - Math.Exp(-0.5 * area / proj), 0, 1),
            MeanNormal = nSum / n,
            Color = cSum / n,
            Transmission = (float)(tr / n),
            Roughness = (float)(ro / n),
        };
    }
}
