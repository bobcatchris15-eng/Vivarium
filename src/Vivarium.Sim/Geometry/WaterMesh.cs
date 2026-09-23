using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Water render geometry derived from the authoritative depth field: a smooth surface over wet cells,
/// clipped to the hexagon, plus vertical faces on the specimen cut so the underwater cross-section is visible
/// from outside. Read-only with respect to the simulation.
/// Vertex data: COLOR.a = depth factor (0..1 over 0..0.3 m), UV2 = flow velocity (m/s sim) for visuals.
/// </summary>
public static class WaterMesh
{
    public static MeshData Build(VivariumWorld w)
    {
        var g = w.Grid; var water = w.Water; var dom = w.Domain; var hf = w.Terrain;
        var mesh = new MeshData();
        double cs = g.CellSize;
        // corner surface heights: (Nx+1) x (Nz+1)
        var corner = new double[(g.Nx + 1) * (g.Nz + 1)];
        var cornerW = new int[corner.Length];
        foreach (int idx in g.DomainCells)
        {
            if (!water.IsWet(idx)) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            double s = water.Bed[idx] + water.Depth[idx];
            for (int dj = 0; dj <= 1; dj++) for (int di = 0; di <= 1; di++)
            { int c = (j + dj) * (g.Nx + 1) + (i + di); corner[c] += s; cornerW[c]++; }
        }
        double Surface(Vec2 p)
        {
            // bilinear over corner heights (only corners touched by wet cells are defined)
            double fx = (p.X - g.OriginX) / cs, fz = (p.Z - g.OriginZ) / cs;
            int i = Math.Clamp((int)Math.Floor(fx), 0, g.Nx - 1), j = Math.Clamp((int)Math.Floor(fz), 0, g.Nz - 1);
            double u = MathD.Clamp01(fx - i), v = MathD.Clamp01(fz - j);
            double sum = 0, wsum = 0;
            for (int dj = 0; dj <= 1; dj++) for (int di = 0; di <= 1; di++)
            {
                int c = (j + dj) * (g.Nx + 1) + (i + di);
                if (cornerW[c] == 0) continue;
                double wt = (di == 0 ? 1 - u : u) * (dj == 0 ? 1 - v : v) + 1e-6;
                sum += corner[c] / cornerW[c] * wt; wsum += wt;
            }
            double s = wsum > 0 ? sum / wsum : hf.Height(p);
            return Math.Max(s, hf.Height(p) + 0.001);
        }

        foreach (int idx in g.DomainCells)
        {
            if (!water.IsWet(idx)) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            double x0 = g.OriginX + i * cs, z0 = g.OriginZ + j * cs, x1 = x0 + cs, z1 = z0 + cs;
            if (g.IsBoundaryCell[idx])
            {
                // extend over the neighbouring out-of-domain squares so the water meets the exact cut plane
                if (!g.InDomain(i - 1, j)) x0 -= cs;
                if (!g.InDomain(i + 1, j)) x1 += cs;
                if (!g.InDomain(i, j - 1)) z0 -= cs;
                if (!g.InDomain(i, j + 1)) z1 += cs;
            }
            var square = new[] { new Vec2(x0, z0), new Vec2(x1, z0), new Vec2(x1, z1), new Vec2(x0, z1) };
            bool needsClip = g.IsBoundaryCell[idx] || square.Any(q => dom.SignedDistance(q) > 0);
            var poly = needsClip ? dom.ClipPolygon(square) : square.ToList();
            if (poly.Count < 3) continue;
            double depthFactor = MathD.Clamp01(water.Depth[idx] / 0.3);
            double vel = Math.Max(water.Depth[idx] * cs, 1e-6);
            double fx = water.FlowX[idx] / vel, fz = water.FlowZ[idx] / vel;
            var ids = new int[poly.Count];
            for (int k = 0; k < poly.Count; k++)
                ids[k] = mesh.AddVertex(new Vec3(poly[k].X, Surface(poly[k]), poly[k].Z), Vec3.Up, 0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fx, fz);
            for (int k = 1; k + 1 < poly.Count; k++) mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);

            // cut face: any clipped edge lying on a hexagon side becomes a vertical wall of water
            if (!needsClip) continue;
            for (int k = 0; k < poly.Count; k++)
            {
                var a = poly[k]; var b = poly[(k + 1) % poly.Count];
                int side = SharedSide(dom, a, b);
                if (side < 0) continue;
                var n2 = dom.EdgeNormals[side]; var n = new Vec3(n2.X, 0, n2.Z);
                double sa = Surface(a), sb = Surface(b), ba = hf.Height(a), bb = hf.Height(b);
                if (sa - ba < 1e-4 && sb - bb < 1e-4) continue;
                int ta = mesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.2, 0.55, 0.7, depthFactor, a.X, 0, 0, 0);
                int tb = mesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.2, 0.55, 0.7, depthFactor, b.X, 0, 0, 0);
                int la = mesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.2, 0.55, 0.7, depthFactor, a.X, sa - ba, 0, 0);
                int lb = mesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.2, 0.55, 0.7, depthFactor, b.X, sb - bb, 0, 0);
                // polygon runs counter-clockwise (x→z), so a→b along the boundary matches the terrain wall order
                mesh.AddTriangle(ta, la, lb);
                mesh.AddTriangle(ta, lb, tb);
            }
        }
        return mesh;
    }

    /// <summary>Index of the hexagon side both points lie on (within 1e-7), or -1.</summary>
    public static int SharedSide(HexDomain dom, Vec2 a, Vec2 b)
    {
        for (int k = 0; k < 6; k++)
        {
            var n = dom.EdgeNormals[k];
            if (Math.Abs(a.Dot(n) - dom.Apothem) < 1e-7 && Math.Abs(b.Dot(n) - dom.Apothem) < 1e-7 && Vec2.DistanceSq(a, b) > 1e-14) return k;
        }
        return -1;
    }

    /// <summary>Sampled flow direction for a cell as used by the visuals (unit vector or zero).</summary>
    public static Vec2 VisualFlowDirection(VivariumWorld w, int cell) => new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]).Normalized();
}
