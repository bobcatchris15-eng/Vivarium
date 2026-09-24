using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Builds the island's render geometry from authoritative terrain: the top surface clipped exactly to the
/// regular hexagon, the six vertical strata walls, and the flat underside.
/// </summary>
public static class TerrainMesh
{
    /// <summary>
    /// Top surface. Each heightfield square is split along the same diagonal <see cref="Heightfield.Height"/>
    /// uses; triangles crossing the boundary are clipped against the hexagon (Sutherland–Hodgman), so no
    /// triangle extends outside the domain. Interior grid vertices are shared; vertex i of the result maps
    /// to <see cref="TopMesh.VertexXZ"/> for recolouring.
    /// </summary>
    public static TopMesh BuildTop(VivariumWorld w)
    {
        var hf = w.Terrain;
        var dom = w.Domain;
        var mesh = new MeshData();
        var xz = new List<Vec2>();
        var shared = new Dictionary<int, int>();
        int Vertex(Vec2 p, int key = -1)
        {
            if (key >= 0 && shared.TryGetValue(key, out int existing)) return existing;
            double h = hf.Height(p);
            var n = SmoothNormal(hf, p);
            int idx = mesh.AddVertex(new Vec3(p.X, h, p.Z), n, 0.4, 0.3, 0.2, 1, p.X, p.Z);
            xz.Add(p);
            if (key >= 0) shared[key] = idx;
            return idx;
        }
        for (int j = 0; j < hf.Nz - 1; j++)
            for (int i = 0; i < hf.Nx - 1; i++)
            {
                var p00 = hf.VertexPos(i, j); var p10 = hf.VertexPos(i + 1, j); var p01 = hf.VertexPos(i, j + 1); var p11 = hf.VertexPos(i + 1, j + 1);
                int k00 = j * hf.Nx + i, k10 = k00 + 1, k01 = k00 + hf.Nx, k11 = k01 + 1;
                // Interior cells use a slightly off-centre render vertex and four triangles rather than exposing
                // the same long diagonal across the whole island. Grid vertices remain authoritative; only the
                // render surface between them is smoothed, so the simulation heightfield stays untouched.
                if (dom.Contains(p00) && dom.Contains(p10) && dom.Contains(p01) && dom.Contains(p11))
                {
                    ulong h0 = Rng.Mix(w.Seed, (ulong)(1 + j * hf.Nx + i));
                    ulong h1 = Rng.Mix(h0, 0x9E3779B97F4A7C15UL);
                    double ju = (((h0 >> 16) & 0xffff) / 65535.0 - 0.5) * 0.18;
                    double jv = (((h1 >> 16) & 0xffff) / 65535.0 - 0.5) * 0.18;
                    double u = 0.5 + ju, v = 0.5 + jv;
                    var pc = p00 * ((1 - u) * (1 - v)) + p10 * (u * (1 - v)) + p01 * ((1 - u) * v) + p11 * (u * v);
                    double hc = hf.Height(p00) * ((1 - u) * (1 - v)) + hf.Height(p10) * (u * (1 - v))
                              + hf.Height(p01) * ((1 - u) * v) + hf.Height(p11) * (u * v);
                    int kc = mesh.AddVertex(new Vec3(pc.X, hc, pc.Z), SmoothNormal(hf, pc), 0.4, 0.3, 0.2, 1, pc.X, pc.Z);
                    xz.Add(pc);
                    int a = Vertex(p00, k00), b = Vertex(p10, k10), c = Vertex(p11, k11), d = Vertex(p01, k01);
                    mesh.AddTriangle(a, b, kc); mesh.AddTriangle(b, c, kc);
                    mesh.AddTriangle(c, d, kc); mesh.AddTriangle(d, a, kc);
                }
                else
                {
                    AddClipped(dom, mesh, Vertex, new[] { p00, p10, p11 }, new[] { k00, k10, k11 });
                    AddClipped(dom, mesh, Vertex, new[] { p00, p11, p01 }, new[] { k00, k11, k01 });
                }
            }
        return new TopMesh(mesh, xz);
    }

    private static void AddClipped(HexDomain dom, MeshData mesh, Func<Vec2, int, int> vertex, Vec2[] tri, int[] keys)
    {
        bool allIn = true, allOut = true;
        foreach (var p in tri) { double sd = dom.SignedDistance(p); if (sd > 0) allIn = false; else allOut = false; }
        if (allIn) { mesh.AddTriangle(vertex(tri[0], keys[0]), vertex(tri[1], keys[1]), vertex(tri[2], keys[2])); return; }
        if (allOut)
        {
            // a triangle can still overlap the hexagon near a corner even if all vertices are outside
            var probe = dom.ClipPolygon(tri);
            if (probe.Count < 3) return;
        }
        var poly = dom.ClipPolygon(tri);
        if (poly.Count < 3) return;
        var ids = new int[poly.Count];
        for (int k = 0; k < poly.Count; k++)
        {
            int key = -1;
            for (int t = 0; t < 3; t++) if (Vec2.DistanceSq(poly[k], tri[t]) < 1e-20) key = keys[t];
            ids[k] = vertex(poly[k], key);
        }
        for (int k = 1; k + 1 < poly.Count; k++)
        {
            // skip degenerate slivers
            var a = poly[0]; var b = poly[k]; var c = poly[k + 1];
            if (Math.Abs((b - a).Cross(c - a)) < 1e-12) continue;
            mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);
        }
    }

    public static Vec3 SmoothNormal(Heightfield hf, Vec2 p)
    {
        double e = hf.Step;
        double dx = (hf.Height(p + new Vec2(e, 0)) - hf.Height(p - new Vec2(e, 0))) / (2 * e);
        double dz = (hf.Height(p + new Vec2(0, e)) - hf.Height(p - new Vec2(0, e))) / (2 * e);
        return new Vec3(-dx, 1, -dz).Normalized();
    }

    /// <summary>
    /// Six vertical cut faces from the terrain edge down to the island bottom, banded by strata. Each band
    /// has its own vertices so layer colours meet in crisp lines. UV = (distance along perimeter, depth below
    /// surface); UV2 = (layer index, depth within layer). Colours come from content data.
    /// </summary>
    public static MeshData BuildWalls(VivariumWorld w, double sampleStep = 0)
    {
        var hf = w.Terrain; var dom = w.Domain; var strata = w.Strata;
        double step = sampleStep > 0 ? sampleStep : hf.Step;
        double bottom = hf.Bottom;
        var mesh = new MeshData();
        double perimeter = 0;
        for (int k = 0; k < 6; k++)
        {
            Vec2 a = dom.Vertices[k], b = dom.Vertices[(k + 1) % 6];
            var n2 = dom.EdgeNormals[k];
            var n = new Vec3(n2.X, 0, n2.Z);
            double len = Vec2.Distance(a, b);
            int segs = Math.Max(1, (int)Math.Ceiling(len / step));
            // band boundaries (depth below local surface)
            int layers = strata.Layers.Count;
            for (int layer = 0; layer < layers; layer++)
            {
                double top = strata.LayerTop(layer);
                double bot = layer == layers - 1 ? double.PositiveInfinity : strata.LayerTop(layer + 1);
                var col = strata.Layers[layer].Color;
                int prevTop = -1, prevBot = -1;
                for (int s = 0; s <= segs; s++)
                {
                    double t = (double)s / segs;
                    var p = Vec2.Lerp(a, b, t);
                    double surf = hf.Height(p);
                    double yTop = Math.Max(surf - top, bottom);
                    double yBot = Math.Max(surf - Math.Min(bot, surf - bottom), bottom);
                    double u = perimeter + t * len;
                    int vt = mesh.AddVertex(new Vec3(p.X, yTop, p.Z), n, col, 1, u, surf - yTop, layer, 0);
                    int vb = mesh.AddVertex(new Vec3(p.X, yBot, p.Z), n, col, 1, u, surf - yBot, layer, yTop - yBot);
                    if (s > 0)
                    {
                        // quad (prevTop, prevBot, vb, vt) facing outward
                        mesh.AddTriangle(prevTop, prevBot, vb);
                        mesh.AddTriangle(prevTop, vb, vt);
                    }
                    prevTop = vt; prevBot = vb;
                }
            }
            perimeter += len;
        }
        // underside: flat hexagon facing down
        var baseCol = strata.Layers[^1].Color;
        var dark = new[] { baseCol[0] * 0.7, baseCol[1] * 0.7, baseCol[2] * 0.7 };
        int c0 = mesh.AddVertex(new Vec3(0, bottom, 0), new Vec3(0, -1, 0), dark, 1, 0, 0, strata.Layers.Count - 1, 0);
        var ring = new int[6];
        for (int k = 0; k < 6; k++) ring[k] = mesh.AddVertex(new Vec3(dom.Vertices[k].X, bottom, dom.Vertices[k].Z), new Vec3(0, -1, 0), dark, 1, dom.Vertices[k].X, dom.Vertices[k].Z, strata.Layers.Count - 1, 0);
        for (int k = 0; k < 6; k++) mesh.AddTriangle(c0, ring[(k + 1) % 6], ring[k]);
        return mesh;
    }
}

public sealed class TopMesh
{
    public MeshData Mesh { get; }
    /// <summary>World XZ of each vertex (for recolouring by substrate/moisture without rebuilding).</summary>
    public IReadOnlyList<Vec2> VertexXZ { get; }
    public TopMesh(MeshData mesh, IReadOnlyList<Vec2> xz) { Mesh = mesh; VertexXZ = xz; }
}
