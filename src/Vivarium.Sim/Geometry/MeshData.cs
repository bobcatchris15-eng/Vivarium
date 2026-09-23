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
        foreach (var i in other.Indices) Indices.Add(baseIndex + i);
    }
}
