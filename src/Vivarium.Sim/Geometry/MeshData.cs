using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Engine-neutral triangle mesh (float arrays) produced by the builders. The client uploads it to the GPU;
/// it is never read back as simulation state.
/// </summary>
public sealed class MeshData
{
    public readonly List<float> Positions = new();   // xyz
    public readonly List<float> Normals = new();     // xyz
    public readonly List<float> Colors = new();      // rgba
    public readonly List<float> UV = new();          // uv
    public readonly List<float> UV2 = new();         // uv2
    public readonly List<int> Indices = new();
    public readonly List<float> Custom0 = new(); // optional RGBA float render data
    public readonly List<float> Custom1 = new();
    public readonly List<FloraLeaf> Leaves = new();
    public int? FloraDetailLevel { get; set; }
    public ulong FloraVisualSeed { get; set; }
    /// <summary>Render-only parametric flora detail; null = authored full detail.</summary>
    public FloraLodParams? Lod { get; set; }
    /// <summary>Vertex spans of closed tubes (trunks, stems, petioles) with their radial side count.</summary>
    public readonly List<(int FirstVertex, int VertexCount, int Sides, double Radius)> Structural = new();
    /// <summary>Vertex spans of every leaf blade, recorded regardless of <see cref="FloraDetailLevel"/>.</summary>
    public readonly List<(int FirstVertex, int VertexCount)> LeafSpans = new();
    /// <summary>Small silhouette/anatomy features that must survive loose-piece LOD culling.</summary>
    public readonly List<(int FirstVertex, int VertexCount)> EssentialSpans = new();

    public void RecordLeaf(int vertex, int index, Vec3 attachment, double length, bool volumetric = false)
    {
        if (VertexCount > vertex) LeafSpans.Add((vertex, VertexCount - vertex));
        if (FloraDetailLevel == null) return;
        ulong identity = Rng.Mix(FloraVisualSeed, (ulong)Leaves.Count + 1);
        Leaves.Add(new FloraLeaf(vertex, VertexCount - vertex, index, Indices.Count - index,
            identity, attachment, length, (int)(identity % 4), ((identity >> 8) % 1000) / 1000.0,
            volumetric ? 0.14 : 0.65 + ((identity >> 20) % 1000) / 3000.0, volumetric));
    }

    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Indices.Count / 3;

    public int AddVertex(Vec3 p, Vec3 n, double r, double g, double b, double a, double u = 0, double v = 0, double u2 = 0, double v2 = 0)
    {
        Positions.Add((float)p.X); Positions.Add((float)p.Y); Positions.Add((float)p.Z);
        Normals.Add((float)n.X); Normals.Add((float)n.Y); Normals.Add((float)n.Z);
        Colors.Add((float)r); Colors.Add((float)g); Colors.Add((float)b); Colors.Add((float)a);
        UV.Add((float)u); UV.Add((float)v);
        UV2.Add((float)u2); UV2.Add((float)v2);
        return VertexCount - 1;
    }

    public int AddVertex(Vec3 p, Vec3 n, double[] rgb, double a = 1, double u = 0, double v = 0, double u2 = 0, double v2 = 0) =>
        AddVertex(p, n, rgb[0], rgb[1], rgb[2], a, u, v, u2, v2);

    public void AddTriangle(int a, int b, int c) { Indices.Add(a); Indices.Add(b); Indices.Add(c); }

    public Vec3 Position(int i) => new(Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]);
    public Vec3 NormalAt(int i) => new(Normals[i * 3], Normals[i * 3 + 1], Normals[i * 3 + 2]);

    public void SetColor(int i, double r, double g, double b, double a)
    {
        Colors[i * 4] = (float)r; Colors[i * 4 + 1] = (float)g; Colors[i * 4 + 2] = (float)b; Colors[i * 4 + 3] = (float)a;
    }

    /// <summary>Recomputes smooth normals from triangle areas (for procedural props).</summary>
    public void RecomputeNormals()
    {
        var acc = new Vec3[VertexCount];
        for (int t = 0; t < Indices.Count; t += 3)
        {
            int a = Indices[t], b = Indices[t + 1], c = Indices[t + 2];
            var n = (Position(b) - Position(a)).Cross(Position(c) - Position(a));
            acc[a] += n; acc[b] += n; acc[c] += n;
        }
        for (int i = 0; i < acc.Length; i++)
        {
            var n = acc[i].Normalized();
            Normals[i * 3] = (float)n.X; Normals[i * 3 + 1] = (float)n.Y; Normals[i * 3 + 2] = (float)n.Z;
        }
    }

    public (Vec3 Min, Vec3 Max) Bounds()
    {
        var mn = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var mx = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        for (int i = 0; i < VertexCount; i++)
        {
            var p = Position(i);
            mn = new Vec3(Math.Min(mn.X, p.X), Math.Min(mn.Y, p.Y), Math.Min(mn.Z, p.Z));
            mx = new Vec3(Math.Max(mx.X, p.X), Math.Max(mx.Y, p.Y), Math.Max(mx.Z, p.Z));
        }
        return (mn, mx);
    }

    public string DigestHex()
    {
        using var d = new DigestBuilder();
        foreach (var f in Positions) d.Add(f);
        foreach (var i in Indices) d.Add(i);
        return d.Hex();
    }

    public void Append(MeshData other, Func<Vec3, Vec3>? transform = null)
    {
        int baseIndex = VertexCount;
        for (int i = 0; i < other.VertexCount; i++)
        {
            var p = transform != null ? transform(other.Position(i)) : other.Position(i);
            Positions.Add((float)p.X); Positions.Add((float)p.Y); Positions.Add((float)p.Z);
        }
        Normals.AddRange(other.Normals); Colors.AddRange(other.Colors); UV.AddRange(other.UV); UV2.AddRange(other.UV2);
        if (Custom0.Count > 0 || other.Custom0.Count > 0)
        {
            while (Custom0.Count < baseIndex * 4) Custom0.Add(0);
            for (int i = 0; i < other.VertexCount * 4; i++) Custom0.Add(i < other.Custom0.Count ? other.Custom0[i] : 0);
        }
        if (Custom1.Count > 0 || other.Custom1.Count > 0)
        {
            while (Custom1.Count < baseIndex * 4) Custom1.Add(0);
            for (int i = 0; i < other.VertexCount * 4; i++) Custom1.Add(i < other.Custom1.Count ? other.Custom1[i] : 0);
        }
        int baseIndexOffset = Indices.Count;
        foreach (var leaf in other.Leaves)
            Leaves.Add(leaf with { FirstVertex = leaf.FirstVertex + baseIndex, FirstIndex = leaf.FirstIndex + baseIndexOffset,
                Attachment = transform != null ? transform(leaf.Attachment) : leaf.Attachment });
        foreach (var i in other.Indices) Indices.Add(baseIndex + i);
        foreach (var st in other.Structural) Structural.Add((st.FirstVertex + baseIndex, st.VertexCount, st.Sides, st.Radius));
        foreach (var ls in other.LeafSpans) LeafSpans.Add((ls.FirstVertex + baseIndex, ls.VertexCount));
    }
}

/// <summary>
/// Parametric flora detail: the same generator, seed and form, tessellated coarser. Scales multiply the
/// authored counts; primitives clamp to minimums (tubes >= 5 sides, blades >= 2 segments).
/// <see cref="LeafFraction"/> of leaf blades survive (inner/smallest dropped first).
/// </summary>
public sealed record FloraLodParams(double Radial = 1, double Length = 1, double Blade = 1, double LeafFraction = 1)
{
    public static FloraLodParams FromScale(double s)
    {
        s = Math.Clamp(s, 0, 1);
        return new FloraLodParams(s, Math.Sqrt(s), Math.Sqrt(s), s);
    }
    public int Sides(int authored) => Math.Max(5, (int)Math.Round(authored * Radial));
    public int Segments(int authored, int min = 2) => Math.Max(Math.Min(min, authored), (int)Math.Round(authored * Blade));
}
