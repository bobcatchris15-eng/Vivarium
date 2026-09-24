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
    /// Render-only top surface at twice the authoritative grid resolution. Heights use a bounded Catmull-Rom
    /// reconstruction blended toward the authoritative triangle surface, so grazing silhouettes are smooth while
    /// picking/camera collision remain within a few centimetres. Fine-cell diagonals alternate deterministically
    /// instead of exposing one island-wide triangulation pattern. Boundary triangles are clipped exactly to the hex.
    /// </summary>
    public static TopMesh BuildTop(VivariumWorld w)
    {
        var hf = w.Terrain;
        var dom = w.Domain;
        var mesh = new MeshData();
        var xz = new List<Vec2>();
        var shared = new Dictionary<int, int>();

        const int sub = 2;
        double step = hf.Step / sub;
        int nx = (hf.Nx - 1) * sub + 1;
        int nz = (hf.Nz - 1) * sub + 1;

        Vec2 Pos(int i, int j) => new(hf.OriginX + i * step, hf.OriginZ + j * step);
        int Vertex(Vec2 p, int key = -1)
        {
            if (key >= 0 && shared.TryGetValue(key, out int existing)) return existing;
            double h = RenderHeight(hf, p);
            var n = RenderNormal(hf, p);
            int idx = mesh.AddVertex(new Vec3(p.X, h, p.Z), n, 0.4, 0.3, 0.2, 1, p.X, p.Z);
            xz.Add(p);
            if (key >= 0) shared[key] = idx;
            return idx;
        }

        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                var p00 = Pos(i, j); var p10 = Pos(i + 1, j);
                var p01 = Pos(i, j + 1); var p11 = Pos(i + 1, j + 1);
                int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;

                // Break up the diagonal field without moving shared vertices. This leaves no coherent diagonal
                // for the eye to follow across a low-angle view.
                bool flip = (Rng.Mix(w.Seed, (ulong)(1 + k00)) & 1UL) != 0;
                if (!flip)
                {
                    AddClipped(dom, mesh, Vertex, new[] { p00, p10, p11 }, new[] { k00, k10, k11 });
                    AddClipped(dom, mesh, Vertex, new[] { p00, p11, p01 }, new[] { k00, k11, k01 });
                }
                else
                {
                    AddClipped(dom, mesh, Vertex, new[] { p00, p10, p01 }, new[] { k00, k10, k01 });
                    AddClipped(dom, mesh, Vertex, new[] { p10, p11, p01 }, new[] { k10, k11, k01 });
                }
            }
        return new TopMesh(mesh, xz);
    }

    private static double Catmull(double a, double b, double c, double d, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2
            + (-a + 3 * b - 3 * c + d) * t3);
    }

    /// <summary>
    /// Smooth visual height reconstructed from the authoritative vertex samples. The result is clamped to stay
    /// within 3.5 cm of the collision surface, preventing the prettier render mesh from lying about interactions.
    /// </summary>
    public static double RenderHeight(Heightfield hf, Vec2 p)
    {
        double fx = (p.X - hf.OriginX) / hf.Step, fz = (p.Z - hf.OriginZ) / hf.Step;
        int i = Math.Clamp((int)Math.Floor(fx), 0, hf.Nx - 2);
        int j = Math.Clamp((int)Math.Floor(fz), 0, hf.Nz - 2);
        double u = MathD.Clamp01(fx - i), v = MathD.Clamp01(fz - j);

        double Row(int jj)
        {
            double a = hf.Vertex(i - 1, jj), b = hf.Vertex(i, jj);
            double c = hf.Vertex(i + 1, jj), d = hf.Vertex(i + 2, jj);
            return Catmull(a, b, c, d, u);
        }

        double cubic = Catmull(Row(j - 1), Row(j), Row(j + 1), Row(j + 2), v);
        double authoritative = hf.Height(p);
        double smooth = MathD.Lerp(authoritative, cubic, 0.72);
        return MathD.Clamp(smooth,
            Math.Max(hf.MinHeight, authoritative - 0.035),
            Math.Min(hf.MaxHeight, authoritative + 0.035));
    }

    public static Vec3 RenderNormal(Heightfield hf, Vec2 p)
    {
        double e = hf.Step * 0.5;
        double dx = (RenderHeight(hf, p + new Vec2(e, 0)) - RenderHeight(hf, p - new Vec2(e, 0))) / (2 * e);
        double dz = (RenderHeight(hf, p + new Vec2(0, e)) - RenderHeight(hf, p - new Vec2(0, e))) / (2 * e);
        return new Vec3(-dx, 1, -dz).Normalized();
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

    public static Vec3 SmoothNormal(Heightfield hf, Vec2 p) => RenderNormal(hf, p);

    /// <summary>
    /// Six vertical cut faces from the terrain edge down to the island bottom, banded by strata. Each band
    /// has its own vertices so layer colours meet in crisp lines. UV = (distance along perimeter, depth below
    /// surface); UV2 = (layer index, depth within layer). Colours come from content data.
    /// </summary>
    public static MeshData BuildWalls(VivariumWorld w, double sampleStep = 0)
    {
        var hf = w.Terrain; var dom = w.Domain; var strata = w.Strata;
        double step = sampleStep > 0 ? sampleStep : hf.Step * 0.5;
        double bottom = hf.Bottom;
        var mesh = new MeshData();
        double perimeter = 0;

        double HorizonDepth(int boundary, Vec2 p)
        {
            if (boundary <= 0) return 0;
            double baseDepth = strata.LayerTop(boundary);
            ulong seed = Rng.Mix(w.Seed, Hash.Fnv1a64("strata.horizon." + boundary));
            double amp = Math.Min(0.055, 0.012 + baseDepth * 0.035);
            double broad = Noise.Fbm(seed, p.X * 0.42, p.Z * 0.42, 2);
            double fine = Noise.Gradient(Rng.Mix(seed, 29), p.X * 1.15, p.Z * 1.15);
            return Math.Max(0, baseDepth + amp * (broad * 0.78 + fine * 0.22));
        }
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
                var col = strata.Layers[layer].Color;
                int prevTop = -1, prevBot = -1;
                for (int s = 0; s <= segs; s++)
                {
                    double t = (double)s / segs;
                    var p = Vec2.Lerp(a, b, t);
                    double surf = RenderHeight(hf, p);
                    double top = HorizonDepth(layer, p);
                    double bot = layer == layers - 1 ? surf - bottom : HorizonDepth(layer + 1, p);
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
