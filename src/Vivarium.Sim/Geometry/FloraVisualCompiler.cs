namespace Vivarium.Sim.Geometry;

/// <summary>Separates tissues without one node per leaf; optional attributes remain render-only.</summary>
public static class FloraVisualCompiler
{
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
        return (stem, blade);
    }
}
