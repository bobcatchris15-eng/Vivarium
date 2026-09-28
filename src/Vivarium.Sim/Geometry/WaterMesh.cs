using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Render geometry for two physically separate water systems:
/// hydrostatic groundwater clipped against terrain, and dynamic surface water reconstructed from the solver.
/// Rendering is read-only and finer than the authoritative 25 cm environment grid.
/// </summary>
public sealed record WaterMeshSet(MeshData TableMesh, MeshData StreamMesh, MeshData CombinedMesh);

/// <summary>Immutable inputs captured on the game thread for background water geometry.</summary>
public sealed record WaterMeshSnapshot(
    double[] Depth, double[] Bed, double[] FlowX, double[] FlowZ, Heightfield Terrain, double WaterTable = 0.0);

public static class WaterMesh
{
    public const int Subdivisions = 2;
    public const int RingCells = 2;

    public static WaterMeshSnapshot Capture(VivariumWorld w) => new(
        (double[])w.Water.Depth.Clone(), (double[])w.Water.Bed.Clone(),
        (double[])w.Water.FlowX.Clone(), (double[])w.Water.FlowZ.Clone(),
        w.Terrain.Snapshot(), w.Water.WaterTable);

    public static MeshData Build(VivariumWorld w, WaterMeshSnapshot? snapshot = null) =>
        BuildSet(w, snapshot).CombinedMesh;

    public static WaterMeshSet BuildSet(VivariumWorld w, WaterMeshSnapshot? snapshot = null)
    {
        var table = BuildGroundwater(w, snapshot);
        var surface = BuildSurfaceWater(w, snapshot);
        var combined = new MeshData();
        combined.Append(table);
        combined.Append(surface);
        return new WaterMeshSet(table, surface, combined);
    }

    private static MeshData BuildGroundwater(VivariumWorld w, WaterMeshSnapshot? snapshot)
    {
        var g = w.Grid;
        var dom = w.Domain;
        var hf = snapshot?.Terrain ?? w.Terrain;
        var bed = snapshot?.Bed ?? w.Water.Bed;
        double table = snapshot?.WaterTable ?? w.Water.WaterTable;
        var mesh = new MeshData();
        double sub = g.CellSize / Subdivisions;

        // Table water is an infinite horizontal hydrostatic plane clipped by terrain + specimen boundary.
        // Iterate every domain cell so narrow exposed pockets cannot disappear because a cell centre is dry.
        foreach (int idx in g.DomainCells)
        {
            int i = idx % g.Nx, j = idx / g.Nx;
            double x0 = g.OriginX + i * g.CellSize, z0 = g.OriginZ + j * g.CellSize;
            double x1 = x0 + g.CellSize, z1 = z0 + g.CellSize;
            ExtendBoundarySquare(g, idx, i, j, ref x0, ref z0, ref x1, ref z1);

            int nx = (int)Math.Round((x1 - x0) / sub), nz = (int)Math.Round((z1 - z0) / sub);
            for (int sj = 0; sj < nz; sj++)
                for (int si = 0; si < nx; si++)
                {
                    double qx0 = x0 + si * sub, qz0 = z0 + sj * sub;
                    var square = new[]
                    {
                        new Vec2(qx0, qz0), new Vec2(qx0 + sub, qz0),
                        new Vec2(qx0 + sub, qz0 + sub), new Vec2(qx0, qz0 + sub)
                    };
                    if (!square.Any(q => table > hf.Height(q) + 0.0005)) continue;

                    bool needsClip = g.IsBoundaryCell[idx] || square.Any(q => dom.SignedDistance(q) > 0);
                    var poly = needsClip ? dom.ClipPolygon(square) : square.ToList();
                    if (poly.Count < 3) continue;

                    var ids = new int[poly.Count];
                    for (int k = 0; k < poly.Count; k++)
                    {
                        double depthFactor = MathD.Clamp01((table - hf.Height(poly[k])) / 0.3);
                        ids[k] = mesh.AddVertex(
                            new Vec3(poly[k].X, table, poly[k].Z), Vec3.Up,
                            0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, 0, 0);
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);

                    if (needsClip) AddCutFaces(mesh, dom, hf, poly, q => table, 0.3);
                }
        }
        return mesh;
    }

    private static MeshData BuildSurfaceWater(VivariumWorld w, WaterMeshSnapshot? snapshot)
    {
        var g = w.Grid;
        var dom = w.Domain;
        var water = w.Water;
        var hf = snapshot?.Terrain ?? w.Terrain;
        var depth = snapshot?.Depth ?? water.Depth;
        var bed = snapshot?.Bed ?? water.Bed;
        var flowX = snapshot?.FlowX ?? water.FlowX;
        var flowZ = snapshot?.FlowZ ?? water.FlowZ;
        var mesh = new MeshData();

        double cs = g.CellSize;
        int cx = g.Nx + 1, cz = g.Nz + 1;
        var corner = new double[cx * cz];
        var cfx = new double[corner.Length];
        var cfz = new double[corner.Length];
        var cornerW = new int[corner.Length];

        foreach (int idx in g.DomainCells)
        {
            if (depth[idx] < water.Config.WetDepth) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            double surface = bed[idx] + depth[idx];
            double section = Math.Max(depth[idx] * cs, 1e-6);
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int c = (j + dj) * cx + i + di;
                    corner[c] += surface;
                    cfx[c] += flowX[idx] / section;
                    cfz[c] += flowZ[idx] / section;
                    cornerW[c]++;
                }
        }

        var level = new double[corner.Length];
        var defined = new bool[corner.Length];
        for (int c = 0; c < corner.Length; c++)
            if (cornerW[c] > 0)
            {
                level[c] = corner[c] / cornerW[c];
                cfx[c] /= cornerW[c];
                cfz[c] /= cornerW[c];
                defined[c] = true;
            }

        // Rendering-only shoreline extrapolation. It never creates simulation water.
        for (int pass = 0; pass < RingCells; pass++)
        {
            var nextLevel = (double[])level.Clone();
            var nextDef = (bool[])defined.Clone();
            for (int j = 0; j < cz; j++)
                for (int i = 0; i < cx; i++)
                {
                    int c = j * cx + i;
                    if (defined[c]) continue;
                    double sum = 0;
                    int n = 0;
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ii = i + di, jj = j + dj;
                            if (ii < 0 || jj < 0 || ii >= cx || jj >= cz || !defined[jj * cx + ii]) continue;
                            sum += level[jj * cx + ii];
                            n++;
                        }
                    if (n > 0)
                    {
                        nextLevel[c] = sum / n;
                        nextDef[c] = true;
                    }
                }
            level = nextLevel;
            defined = nextDef;
        }

        double Bilinear(double[] field, Vec2 p, out bool ok)
        {
            double fx = (p.X - g.OriginX) / cs, fz = (p.Z - g.OriginZ) / cs;
            int i = Math.Clamp((int)Math.Floor(fx), 0, g.Nx - 1);
            int j = Math.Clamp((int)Math.Floor(fz), 0, g.Nz - 1);
            double u = MathD.Clamp01(fx - i), v = MathD.Clamp01(fz - j);
            double sum = 0, wsum = 0;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int c = (j + dj) * cx + i + di;
                    if (!defined[c]) continue;
                    double wt = (di == 0 ? 1 - u : u) * (dj == 0 ? 1 - v : v) + 1e-6;
                    sum += field[c] * wt;
                    wsum += wt;
                }
            ok = wsum > 0;
            return ok ? sum / wsum : 0;
        }

        double Surface(Vec2 p, out bool ok)
        {
            double s = Bilinear(level, p, out ok);
            return ok ? s : hf.Height(p);
        }
        Vec2 Flow(Vec2 p) => new(Bilinear(cfx, p, out _), Bilinear(cfz, p, out _));

        var draw = new HashSet<int>();
        foreach (int idx in g.DomainCells)
        {
            if (depth[idx] < water.Config.WetDepth) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            for (int dj = -RingCells; dj <= RingCells; dj++)
                for (int di = -RingCells; di <= RingCells; di++)
                    if (g.InDomain(i + di, j + dj)) draw.Add(g.Index(i + di, j + dj));
        }

        double sub = cs / Subdivisions;
        foreach (int idx in draw.OrderBy(x => x))
        {
            int i = idx % g.Nx, j = idx / g.Nx;
            double x0 = g.OriginX + i * cs, z0 = g.OriginZ + j * cs;
            double x1 = x0 + cs, z1 = z0 + cs;
            ExtendBoundarySquare(g, idx, i, j, ref x0, ref z0, ref x1, ref z1);

            int nx = (int)Math.Round((x1 - x0) / sub), nz = (int)Math.Round((z1 - z0) / sub);
            for (int sj = 0; sj < nz; sj++)
                for (int si = 0; si < nx; si++)
                {
                    double qx0 = x0 + si * sub, qz0 = z0 + sj * sub;
                    var square = new[]
                    {
                        new Vec2(qx0, qz0), new Vec2(qx0 + sub, qz0),
                        new Vec2(qx0 + sub, qz0 + sub), new Vec2(qx0, qz0 + sub)
                    };
                    bool anyAbove = false, allOk = true;
                    foreach (var q in square)
                    {
                        double sq = Surface(q, out bool ok);
                        allOk &= ok;
                        if (ok && sq > hf.Height(q) + 0.0005) anyAbove = true;
                    }
                    if (!allOk || !anyAbove) continue;

                    bool needsClip = g.IsBoundaryCell[idx] || square.Any(q => dom.SignedDistance(q) > 0);
                    var poly = needsClip ? dom.ClipPolygon(square) : square.ToList();
                    if (poly.Count < 3) continue;

                    var ids = new int[poly.Count];
                    for (int k = 0; k < poly.Count; k++)
                    {
                        double sk = Surface(poly[k], out _);
                        double depthFactor = MathD.Clamp01((sk - hf.Height(poly[k])) / 0.15);
                        var fl = Flow(poly[k]);
                        ids[k] = mesh.AddVertex(
                            new Vec3(poly[k].X, sk, poly[k].Z), Vec3.Up,
                            0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fl.X, fl.Z);
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);

                    if (needsClip) AddCutFaces(mesh, dom, hf, poly, q => Surface(q, out _), 0.15);
                }
        }
        return mesh;
    }

    private static void ExtendBoundarySquare(
        GridSpec g, int idx, int i, int j, ref double x0, ref double z0, ref double x1, ref double z1)
    {
        if (!g.IsBoundaryCell[idx]) return;
        double cs = g.CellSize;
        if (!g.InDomain(i - 1, j)) x0 -= cs;
        if (!g.InDomain(i + 1, j)) x1 += cs;
        if (!g.InDomain(i, j - 1)) z0 -= cs;
        if (!g.InDomain(i, j + 1)) z1 += cs;
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
            int ta = mesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, 0, 0, 0);
            int tb = mesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, 0, 0, 0);
            int la = mesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
            int lb = mesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
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
