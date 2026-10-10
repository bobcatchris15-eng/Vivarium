using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// One fluid surface reconstructed from stored depth and momentum, clipped to terrain and island.
/// Rendering is read-only and finer than the authoritative 25 cm environment grid.
/// </summary>
public sealed record WaterMeshSet(MeshData SurfaceMesh);

/// <summary>Immutable inputs captured on the game thread for background water geometry.</summary>
public sealed record WaterMeshSnapshot(
    double[] Depth, double[] Bed, double[] FlowX, double[] FlowZ, Heightfield Terrain);

public static class WaterMesh
{
    public const int Subdivisions = 2;
    public const int RingCells = 0;

    public static WaterMeshSnapshot Capture(VivariumWorld w) => new(
        (double[])w.Water.Depth.Clone(), (double[])w.Water.Bed.Clone(),
        (double[])w.Water.FlowX.Clone(), (double[])w.Water.FlowZ.Clone(),
        w.Terrain.Snapshot());

    public static MeshData Build(VivariumWorld w, WaterMeshSnapshot? snapshot = null) =>
        BuildSurfaceWater(w, snapshot);

    public static WaterMeshSet BuildSet(VivariumWorld w, WaterMeshSnapshot? snapshot = null) =>
        Combine(w, snapshot);

    private static WaterMeshSet Combine(VivariumWorld w, WaterMeshSnapshot? snapshot)
    {
        var mesh = BuildSurfaceWater(w, snapshot);
        mesh.Append(BuildWaterTable(w, snapshot?.Terrain ?? w.Terrain));
        return new(mesh);
    }

    /// <summary>Standing groundwater intersects terrain at one hydrostatic elevation.</summary>
    public static MeshData BuildWaterTable(VivariumWorld w, Heightfield terrain)
    {
        var mesh = new MeshData();
        double level = w.Water.WaterTable, step = w.Grid.CellSize / 4;
        foreach (int cell in w.Grid.DomainCells)
        {
            int i = cell % w.Grid.Nx, j = cell / w.Grid.Nx;
            double cx0 = w.Grid.OriginX + i * w.Grid.CellSize;
            double cz0 = w.Grid.OriginZ + j * w.Grid.CellSize;
            double cx1 = cx0 + w.Grid.CellSize, cz1 = cz0 + w.Grid.CellSize;
            if (w.Water.Bed[cell] > level &&
                terrain.Height(new Vec2(cx0, cz0)) > level &&
                terrain.Height(new Vec2(cx1, cz0)) > level &&
                terrain.Height(new Vec2(cx0, cz1)) > level &&
                terrain.Height(new Vec2(cx1, cz1)) > level)
            {
                if (!w.Grid.IsBoundaryCell[cell]) continue;
            }
            double x0 = cx0, z0 = cz0, x1 = cx1, z1 = cz1;
            ExtendBoundarySquare(w.Grid, cell, i, j, ref x0, ref z0, ref x1, ref z1);
            if (terrain.Height(new Vec2(x0, z0)) > level &&
                terrain.Height(new Vec2(x1, z0)) > level &&
                terrain.Height(new Vec2(x0, z1)) > level &&
                terrain.Height(new Vec2(x1, z1)) > level &&
                w.Water.Bed[cell] > level)
                continue;
            for (double z = z0; z < z1 - 1e-9; z += step)
                for (double x = x0; x < x1 - 1e-9; x += step)
                {
                    var a = new Vec2(x,z); var b = new Vec2(x+step,z);
                    var c = new Vec2(x+step,z+step); var d = new Vec2(x,z+step);
                    Emit(new[]{a,b,c}); Emit(new[]{a,c,d});
                }
        }
        return mesh;
        void Emit(Vec2[] triangle)
        {
            var polygon = w.Domain.ClipPolygon(triangle);
            var wet = new List<Vec2>();
            for(int k=0;k<polygon.Count;k++)
            {
                var a=polygon[k]; var b=polygon[(k+1)%polygon.Count];
                double ha=terrain.Height(a)-level, hb=terrain.Height(b)-level;
                if(ha<0)wet.Add(a);
                if((ha<0)!=(hb<0))
                {
                    // Bisect the actual heightfield, rather than averaging neighboring water heights.
                    double lo=0,hi=1;
                    for(int n=0;n<24;n++)
                    {
                        double t=(lo+hi)*.5; var q=a+(b-a)*t;
                        if((terrain.Height(q)<level)==(ha<0))lo=t;else hi=t;
                    }
                    wet.Add(a+(b-a)*((lo+hi)*.5));
                }
            }
            if(wet.Count<3)return;
            var ids=wet.Select(q=>mesh.AddVertex(Vec3.FromXZ(q,level),Vec3.Up,.2,.55,.7,
                MathD.Clamp01((level-terrain.Height(q))/.15),q.X,q.Z)).ToArray();
            for(int k=1;k+1<ids.Length;k++)mesh.AddTriangle(ids[0],ids[k],ids[k+1]);
            AddCutFaces(mesh,w.Domain,terrain,wet,_=>level,.15);
        }
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
            if (depth[idx] < 0.00005) continue;
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
            if (depth[idx] < 0.00005) continue;
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
                    var a = new Vec2(qx0, qz0);
                    var b = new Vec2(qx0 + sub, qz0);
                    var c = new Vec2(qx0 + sub, qz0 + sub);
                    var d = new Vec2(qx0, qz0 + sub);

                    bool needsClip = g.IsBoundaryCell[idx] ||
                        dom.SignedDistance(a) > 0 || dom.SignedDistance(b) > 0 ||
                        dom.SignedDistance(c) > 0 || dom.SignedDistance(d) > 0;

                    Emit(new[] { a, b, c }, needsClip);
                    Emit(new[] { a, c, d }, needsClip);
                }
        }
        return mesh;

        void Emit(Vec2[] triangle, bool needsClip)
        {
            var polygon = needsClip ? dom.ClipPolygon(triangle) : triangle.ToList();
            if (polygon.Count < 3) return;

            var poly = new List<Vec2>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var b = polygon[(k + 1) % polygon.Count];

                double sa = Surface(a, out bool oka);
                double ha = oka ? (hf.Height(a) - sa) : 1.0;

                double sb = Surface(b, out bool okb);
                double hb = okb ? (hf.Height(b) - sb) : 1.0;

                if (ha < 0) poly.Add(a);

                if ((ha < 0) != (hb < 0))
                {
                    double lo = 0, hi = 1;
                    for (int n = 0; n < 24; n++)
                    {
                        double t = (lo + hi) * 0.5;
                        var q = a + (b - a) * t;
                        double sq = Surface(q, out bool okq);
                        double hq = okq ? (hf.Height(q) - sq) : 1.0;
                        if ((hq < 0) == (ha < 0)) lo = t; else hi = t;
                    }
                    poly.Add(a + (b - a) * ((lo + hi) * 0.5));
                }
            }

            if (poly.Count < 3) return;

            var ids = new int[poly.Count];
            for (int k = 0; k < poly.Count; k++)
            {
                double sk = Surface(poly[k], out _);
                double hk = hf.Height(poly[k]);
                if (sk < hk) sk = hk;
                double depthFactor = (sk <= hk) ? 0.0 : MathD.Clamp01((sk - hk) / 0.15);
                var fl = Flow(poly[k]);
                ids[k] = mesh.AddVertex(
                    new Vec3(poly[k].X, sk, poly[k].Z), Vec3.Up,
                    0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fl.X, fl.Z);
            }
            for (int k = 1; k + 1 < poly.Count; k++) mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);

            if (needsClip) AddCutFaces(mesh, dom, hf, poly, q => Surface(q, out _), 0.15);
        }
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
