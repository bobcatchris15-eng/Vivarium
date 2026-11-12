using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Unchanged hydrostatic pond geometry. Mobile surfaces are GPU-displaced by the client.</summary>
public static class WaterMesh
{
    private const int SmoothRadius = 2;      // tent kernel half-width in cells (5x5)
    private const double ShoreBand = 0.03;   // m: surface extends this far past the smoothed shoreline

    /// <summary>Standing groundwater intersects terrain at one hydrostatic elevation.</summary>
    public static MeshData BuildWaterTable(VivariumWorld w, Heightfield terrain)
    {
        var mesh = new MeshData();
        double level = w.Water.WaterTable;
        // Work on the authoritative terrain faces, rather than an independently
        // centered hydrology grid. A coarse cell's dry corners/center cannot prove
        // there is no submerged terrain vertex inside it.
        // Smoothed bank height (render-only): tent-kernel average of terrain vertex
        // heights, so the visible waterline follows a smooth contour instead of the
        // checkerboard of vertices a few millimetres either side of the table.
        int nx = terrain.Nx, nz = terrain.Nz;
        const int R = SmoothRadius;
        var hs = new double[nx * nz];
        var raw = new double[nx * nz];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++) raw[z * nx + x] = terrain.Height(terrain.VertexPos(x, z));
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                double sum = 0, wsum = 0;
                for (int dz = -R; dz <= R; dz++)
                    for (int dx = -R; dx <= R; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= nx || zz >= nz) continue;
                        double wt = (R + 1 - Math.Abs(dx)) * (R + 1 - Math.Abs(dz));
                        sum += wt * raw[zz * nx + xx]; wsum += wt;
                    }
                hs[z * nx + x] = sum / wsum;
            }
        for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                var a = terrain.VertexPos(x, z);
                var b = terrain.VertexPos(x + 1, z);
                var c = terrain.VertexPos(x + 1, z + 1);
                var d = terrain.VertexPos(x, z + 1);
                double da = level - hs[z * nx + x], db = level - hs[z * nx + x + 1];
                double dc = level - hs[(z + 1) * nx + x + 1], dd = level - hs[(z + 1) * nx + x];
                Emit(new[] { a, b, c }, new[] { da, db, dc });
                Emit(new[] { a, c, d }, new[] { da, dc, dd });
            }
        return mesh;

        void Emit(Vec2[] triangle, double[] tds)
        {
            bool anyWet = triangle.Any(p => terrain.Height(p) < level);
            bool near = tds.Any(v => v > -ShoreBand);
            if (!anyWet && !near) return;
            // Domain clipping preserves the terrain face's single plane. Every
            // shoreline intersection is then linear on exactly that same plane.
            var polygon = w.Domain.ClipPolygon(triangle);
            var wet = new List<Vec2>();
            if (!anyWet)
                wet.AddRange(polygon); // dry-by-geometry but near smoothed shore: terrain depth-tests it away
            else
                for (int k = 0; k < polygon.Count; k++)
                {
                    var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count];
                    double ha = terrain.Height(a) - level, hb = terrain.Height(b) - level;
                    if (ha < 0) Append(a);
                    if ((ha < 0) != (hb < 0)) Append(a + (b - a) * (ha / (ha - hb)));
                }
            if (wet.Count > 1 && Vec2.DistanceSq(wet[0], wet[^1]) < 1e-20) wet.RemoveAt(wet.Count - 1);
            if (wet.Count < 3) return;
            double SmoothDepth(Vec2 q)
            {
                var v0 = triangle[1] - triangle[0]; var v1 = triangle[2] - triangle[0]; var v2 = q - triangle[0];
                double den = v0.Cross(v1);
                if (Math.Abs(den) < 1e-18) return tds[0];
                double l1 = v2.Cross(v1) / den, l2 = v0.Cross(v2) / den;
                return tds[0] * (1 - l1 - l2) + tds[1] * l1 + tds[2] * l2;
            }
            var ids = wet.Select(q => mesh.AddVertex(Vec3.FromXZ(q, level), Vec3.Up, .2, .55, .7,
                MathD.Clamp01((level - terrain.Height(q)) / .15), q.X, q.Z, SmoothDepth(q), 0)).ToArray();
            for (int k = 1; k + 1 < ids.Length; k++)
                if (Math.Abs((wet[k] - wet[0]).Cross(wet[k + 1] - wet[0])) > 1e-12)
                    mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);
            AddCutFaces(mesh, w.Domain, terrain, wet, _ => level, .15);

            void Append(Vec2 p)
            {
                if (wet.Count == 0 || Vec2.DistanceSq(wet[^1], p) > 1e-20) wet.Add(p);
            }
        }
    }

    private static void AddCutFaces(
        MeshData mesh, HexDomain dom, Heightfield hf, IReadOnlyList<Vec2> poly,
        Func<Vec2, double> surface, double depthScale)
    {
        for (int k = 0; k < poly.Count; k++)
        {
            var a = poly[k];
            var b = poly[(k + 1) % poly.Count];
            int side = SharedSide(dom, a, b);
            if (side < 0) continue;

            double sa = surface(a), sb = surface(b);
            double ba = Math.Min(hf.Height(a), sa), bb = Math.Min(hf.Height(b), sb);
            if (sa - ba < 1e-4 && sb - bb < 1e-4) continue;

            var n2 = dom.EdgeNormals[side];
            var n = new Vec3(n2.X, 0, n2.Z);
            double dfa = MathD.Clamp01((sa - ba) / depthScale);
            double dfb = MathD.Clamp01((sb - bb) / depthScale);
            int ta = mesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.9, 0.55, 0.7, dfa, a.X, 0, 0, 0);
            int tb = mesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.9, 0.55, 0.7, dfb, b.X, 0, 0, 0);
            int la = mesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.9, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
            int lb = mesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.9, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
            mesh.AddTriangle(ta, la, lb);
            mesh.AddTriangle(ta, lb, tb);
        }
    }

    /// <summary>Index of the hexagon side both points lie on (within 1e-7), or -1.</summary>
    public static int SharedSide(HexDomain dom, Vec2 a, Vec2 b)
    {
        for (int k = 0; k < 6; k++)
        {
            var n = dom.EdgeNormals[k];
            if (Math.Abs(a.Dot(n) - dom.Apothem) < 1e-7 &&
                Math.Abs(b.Dot(n) - dom.Apothem) < 1e-7 &&
                Vec2.DistanceSq(a, b) > 1e-14) return k;
        }
        return -1;
    }

    public static Vec2 VisualFlowDirection(VivariumWorld w, int cell) =>
        new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]).Normalized();
}
