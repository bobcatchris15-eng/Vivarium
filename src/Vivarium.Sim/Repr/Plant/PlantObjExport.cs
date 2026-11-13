using System;
using System.Globalization;
using System.IO;
using System.Text;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Writes a MeshData as Wavefront OBJ (positions, normals, faces). Diagnostics only.</summary>
public static class PlantObjExport
{
    public static void WriteObj(MeshData m, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var ci = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("# Vivarium plant export (P1)");
        int n = m.VertexCount;
        for (int i = 0; i < n; i++)
            w.WriteLine(string.Format(ci, "v {0} {1} {2}", m.Positions[i * 3], m.Positions[i * 3 + 1], m.Positions[i * 3 + 2]));
        for (int i = 0; i < n; i++)
            w.WriteLine(string.Format(ci, "vn {0} {1} {2}", m.Normals[i * 3], m.Normals[i * 3 + 1], m.Normals[i * 3 + 2]));
        for (int t = 0; t + 2 < m.Indices.Count; t += 3)
        {
            int a = m.Indices[t] + 1, b = m.Indices[t + 1] + 1, c = m.Indices[t + 2] + 1;
            w.WriteLine(string.Format(ci, "f {0}//{0} {1}//{1} {2}//{2}", a, b, c));
        }
    }
}
