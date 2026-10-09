using System.Runtime.CompilerServices;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public enum FloraTissueSlot : byte
{
    Stem = 0,
    Wood = 1,
    Foliage = 2,
    Flower = 3,
    Fruit = 4,
    Root = 5,
    Scar = 6
}

public readonly record struct FloraAttachmentFrame(
    Vec3 Origin,
    Vec3 Normal,
    Vec3 Tangent,
    Vec3 Bitangent
)
{
    public static FloraAttachmentFrame Create(Vec3 origin, Vec3 normal, Vec3 tangent)
    {
        var n = normal.LengthSq > 1e-8 ? normal.Normalized() : Vec3.Up;
        var t = tangent.LengthSq > 1e-8 ? tangent.Normalized() : (Math.Abs(n.Y) < 0.99 ? Vec3.Up.Cross(n).Normalized() : new Vec3(1, 0, 0));
        var b = n.Cross(t).Normalized();
        return new FloraAttachmentFrame(origin, n, t, b);
    }

    public static FloraAttachmentFrame Identity =>
        new(Vec3.Zero, Vec3.Up, new Vec3(1, 0, 0), new Vec3(0, 0, 1));
}

public readonly record struct FloraPartMetadata(
    ulong PartId,
    ulong ParentPartId,
    FloraTissueSlot TissueSlot,
    FloraAttachmentFrame Frame,
    double Length,
    double Radius,
    double Flex,
    int MaterialVariant,
    (Vec3 Min, Vec3 Max) Bounds
);

/// <summary>Separates tissues without one node per leaf; optional attributes remain render-only.</summary>
public static class FloraVisualCompiler
{
    private static readonly ConditionalWeakTable<MeshData, List<FloraPartMetadata>> _partTable = new();

    public static IReadOnlyList<FloraPartMetadata> GetParts(MeshData mesh)
    {
        if (_partTable.TryGetValue(mesh, out var list))
            return list;
        return Array.Empty<FloraPartMetadata>();
    }

    public static void RecordPart(MeshData mesh, FloraPartMetadata part)
    {
        var list = _partTable.GetOrCreateValue(mesh);
        list.Add(part);
    }

    public static void CopyParts(MeshData src, MeshData dst)
    {
        if (_partTable.TryGetValue(src, out var list))
        {
            var dstList = _partTable.GetOrCreateValue(dst);
            dstList.AddRange(list);
        }
    }

    public static (MeshData Stem, MeshData Leaf) Split(MeshData source)
    {
        var stem = new MeshData(); var blade = new MeshData();
        var owner = Enumerable.Repeat(-1, source.VertexCount).ToArray();
        for (int k = 0; k < source.Leaves.Count; k++)
        {
            var leaf = source.Leaves[k];
            for (int v = leaf.FirstVertex; v < leaf.FirstVertex + leaf.VertexCount; v++) owner[v] = k;
        }
        var maps = new[] { new Dictionary<int, int>(), new Dictionary<int, int>() };
        for (int t = 0; t < source.Indices.Count; t += 3)
        {
            int a = source.Indices[t]; bool isLeaf = owner[a] >= 0;
            var dst = isLeaf ? blade : stem; var map = maps[isLeaf ? 1 : 0];
            for (int c = 0; c < 3; c++)
            {
                int old = source.Indices[t + c];
                if (!map.TryGetValue(old, out int next))
                {
                    next = dst.AddVertex(source.Position(old), source.NormalAt(old),
                        source.Colors[old * 4], source.Colors[old * 4 + 1], source.Colors[old * 4 + 2], source.Colors[old * 4 + 3],
                        source.UV[old * 2], source.UV[old * 2 + 1], source.UV2[old * 2], source.UV2[old * 2 + 1]);
                    map.Add(old, next);
                    if (isLeaf)
                    {
                        var leaf = source.Leaves[owner[old]];
                        dst.Custom0.AddRange(new[] { (float)((leaf.Identity >> 16) % 65536) / 65536f,
                            (float)leaf.MaterialVariant, (float)leaf.Age, (float)leaf.Flexibility });
                        dst.Custom1.AddRange(new[] { (float)leaf.Attachment.X, (float)leaf.Attachment.Y,
                            (float)leaf.Attachment.Z, (float)leaf.Length });
                    }
                }
                dst.Indices.Add(next);
            }
        }

        if (_partTable.TryGetValue(source, out var parts))
        {
            var stemParts = _partTable.GetOrCreateValue(stem);
            var leafParts = _partTable.GetOrCreateValue(blade);
            foreach (var p in parts)
            {
                if (p.TissueSlot == FloraTissueSlot.Foliage) leafParts.Add(p);
                else stemParts.Add(p);
            }
        }

        return (stem, blade);
    }
}
