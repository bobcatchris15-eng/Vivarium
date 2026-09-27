using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Water render geometry derived from the authoritative depth field, drawn finer than the 25 cm hydrology grid:
/// each wet cell (plus a one-cell ring of dry neighbours) becomes 2×2 sub-quads matching the terrain's 12.5 cm
/// vertex spacing. The surface is the smooth corner-height field extrapolated flat into the dry ring and is
/// NOT lifted over the bank, so where the ground rises above the water plane the terrain simply hides it and
/// the shoreline follows the terrain's own contours instead of the cell grid. Clipped to the hexagon, with
/// vertical faces on the specimen cut so the water column is visible from outside. Read-only on the simulation.
/// Vertex data: COLOR.a = depth factor (0..1 over 0..0.3 m), UV2 = flow velocity (m/s sim) for visuals.
/// </summary>
/// <summary>Separate mesh sets for the static water table pond and dynamic flowing streams, plus combined for compatibility.</summary>
public sealed record WaterMeshSet(MeshData TableMesh, MeshData StreamMesh, MeshData CombinedMesh);

/// <summary>Immutable inputs captured on the game thread for background water geometry.</summary>
public sealed record WaterMeshSnapshot(double[] Depth, double[] Bed, double[] FlowX, double[] FlowZ, Heightfield Terrain, double WaterTable = 0.0);
public static class WaterMesh
{
    public const int Subdivisions = 2;
    /// <summary>Dry cells around the water that are still drawn (covered only where the ground lies below the water level).</summary>
    public const int RingCells = 2;

    public static WaterMeshSnapshot Capture(VivariumWorld w) => new(
        (double[])w.Water.Depth.Clone(), (double[])w.Water.Bed.Clone(),
        (double[])w.Water.FlowX.Clone(), (double[])w.Water.FlowZ.Clone(), w.Terrain.Snapshot(),
        w.Water.WaterTable);

    public static MeshData Build(VivariumWorld w, WaterMeshSnapshot? snapshot = null) =>
        BuildSet(w, snapshot).CombinedMesh;

    public static WaterMeshSet BuildSet(VivariumWorld w, WaterMeshSnapshot? snapshot = null)
    {
        var g = w.Grid; var water = w.Water; var dom = w.Domain;
        var hf = snapshot?.Terrain ?? w.Terrain;
        var depth = snapshot?.Depth ?? water.Depth;
        var bed = snapshot?.Bed ?? water.Bed;
        var flowX = snapshot?.FlowX ?? water.FlowX;
        var flowZ = snapshot?.FlowZ ?? water.FlowZ;
        double waterTable = snapshot?.WaterTable ?? water.WaterTable;
        var mesh = new MeshData();
        var tableMesh = new MeshData();
        var streamMesh = new MeshData();
        double cs = g.CellSize;
        int cx = g.Nx + 1, cz = g.Nz + 1;
        // corner surface heights: average of the wet cells touching each corner, then one dilation pass so the
        // dry ring gets the neighbouring water level (flat extrapolation toward the bank)
        // corner fields: water level and flow velocity averaged over the wet cells touching each corner, so both
        // vary smoothly across cells (a per-cell flow would make the advected ripples jump at every cell edge)
        var corner = new double[cx * cz]; var cfx = new double[corner.Length]; var cfz = new double[corner.Length];
        var cornerW = new int[corner.Length];
        foreach (int idx in g.DomainCells)
        {
            if (depth[idx] < water.Config.WetDepth) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            double s = bed[idx] + depth[idx];
            double vel = Math.Max(depth[idx] * cs, 1e-6);
            for (int dj = 0; dj <= 1; dj++) for (int di = 0; di <= 1; di++)
            {
                int c = (j + dj) * cx + (i + di);
                corner[c] += s; cfx[c] += flowX[idx] / vel; cfz[c] += flowZ[idx] / vel; cornerW[c]++;
            }
        }
        var level = new double[corner.Length];
        var defined = new bool[corner.Length];
        for (int c = 0; c < corner.Length; c++)
            if (cornerW[c] > 0) { level[c] = corner[c] / cornerW[c]; cfx[c] /= cornerW[c]; cfz[c] /= cornerW[c]; defined[c] = true; }
        // grow the level outward (flat extrapolation) so shallow banks are covered until the terrain rises above it;
        // flow fades to zero out there
        for (int pass = 0; pass < RingCells; pass++)
        {
            var nextLevel = (double[])level.Clone(); var nextDef = (bool[])defined.Clone();
            for (int j = 0; j < cz; j++)
                for (int i = 0; i < cx; i++)
                {
                    int c = j * cx + i;
                    if (defined[c]) continue;
                    double sum = 0; int n = 0;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if (ii < 0 || jj < 0 || ii >= cx || jj >= cz || !defined[jj * cx + ii]) continue;
                        sum += level[jj * cx + ii]; n++;
                    }
                    if (n > 0) { nextLevel[c] = sum / n; nextDef[c] = true; }
                }
            level = nextLevel; defined = nextDef;
        }

        double Bilinear(double[] field, Vec2 p, out bool ok)
        {
            double fx = (p.X - g.OriginX) / cs, fz = (p.Z - g.OriginZ) / cs;
            int i = Math.Clamp((int)Math.Floor(fx), 0, g.Nx - 1), j = Math.Clamp((int)Math.Floor(fz), 0, g.Nz - 1);
            double u = MathD.Clamp01(fx - i), v = MathD.Clamp01(fz - j);
            double sum = 0, wsum = 0;
            for (int dj = 0; dj <= 1; dj++) for (int di = 0; di <= 1; di++)
            {
                int c = (j + dj) * cx + (i + di);
                if (!defined[c]) continue;
                double wt = (di == 0 ? 1 - u : u) * (dj == 0 ? 1 - v : v) + 1e-6;
                sum += field[c] * wt; wsum += wt;
            }
            ok = wsum > 0;
            return ok ? sum / wsum : 0;
        }
        double Surface(Vec2 p, out bool ok) { double s = Bilinear(level, p, out ok); return ok ? s : hf.Height(p); }
        Vec2 Flow(Vec2 p) => new(Bilinear(cfx, p, out _), Bilinear(cfz, p, out _));

        // cells to draw: wet cells and a ring of dry neighbours
        var draw = new HashSet<int>();
        foreach (int idx in g.DomainCells)
        {
            if (depth[idx] < water.Config.WetDepth) continue;
            int i = idx % g.Nx, j = idx / g.Nx;
            for (int dj = -RingCells; dj <= RingCells; dj++) for (int di = -RingCells; di <= RingCells; di++)
                if (g.InDomain(i + di, j + dj)) draw.Add(g.Index(i + di, j + dj));
        }
        double sub = cs / Subdivisions;
        foreach (int idx in draw.OrderBy(x => x))
        {
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
            int nx = (int)Math.Round((x1 - x0) / sub), nz = (int)Math.Round((z1 - z0) / sub);
            for (int sj = 0; sj < nz; sj++)
                for (int si = 0; si < nx; si++)
                {
                    double qx0 = x0 + si * sub, qz0 = z0 + sj * sub;
                    var square = new[] { new Vec2(qx0, qz0), new Vec2(qx0 + sub, qz0), new Vec2(qx0 + sub, qz0 + sub), new Vec2(qx0, qz0 + sub) };
                    // skip sub-quads that lie entirely under the ground (the terrain would hide them anyway)
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
                    double centroidX = 0, centroidZ = 0;
                    for (int k = 0; k < poly.Count; k++) { centroidX += poly[k].X; centroidZ += poly[k].Z; }
                    var centroid = new Vec2(centroidX / poly.Count, centroidZ / poly.Count);
                    double hCentroid = hf.Height(centroid);
                    bool isTable = hCentroid < waterTable + 0.02;
                    bool isStream = hCentroid >= waterTable - 0.02;

                    var ids = new int[poly.Count];
                    for (int k = 0; k < poly.Count; k++)
                    {
                        double sk = Surface(poly[k], out _);
                        double depthFactor = MathD.Clamp01((sk - hf.Height(poly[k])) / 0.3);
                        var fl = Flow(poly[k]);
                        ids[k] = mesh.AddVertex(new Vec3(poly[k].X, sk, poly[k].Z), Vec3.Up, 0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fl.X, fl.Z);
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);

                    if (isTable)
                    {
                        var tableIds = new int[poly.Count];
                        for (int k = 0; k < poly.Count; k++)
                        {
                            double sk = Math.Max(waterTable, Surface(poly[k], out _));
                            double depthFactor = MathD.Clamp01((sk - hf.Height(poly[k])) / 0.3);
                            var fl = Flow(poly[k]);
                            tableIds[k] = tableMesh.AddVertex(new Vec3(poly[k].X, sk, poly[k].Z), Vec3.Up, 0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fl.X, fl.Z);
                        }
                        for (int k = 1; k + 1 < poly.Count; k++) tableMesh.AddTriangle(tableIds[0], tableIds[k], tableIds[k + 1]);
                    }

                    if (isStream)
                    {
                        var streamIds = new int[poly.Count];
                        for (int k = 0; k < poly.Count; k++)
                        {
                            double sk = Surface(poly[k], out _);
                            double depthFactor = MathD.Clamp01((sk - hf.Height(poly[k])) / 0.15);
                            var fl = Flow(poly[k]);
                            streamIds[k] = streamMesh.AddVertex(new Vec3(poly[k].X, sk, poly[k].Z), Vec3.Up, 0.2, 0.55, 0.7, depthFactor, poly[k].X, poly[k].Z, fl.X, fl.Z);
                        }
                        for (int k = 1; k + 1 < poly.Count; k++) streamMesh.AddTriangle(streamIds[0], streamIds[k], streamIds[k + 1]);
                    }

                    // cut face: any clipped edge on a hexagon side becomes a vertical wall of water down to the bed
                    if (!needsClip) continue;
                    for (int k = 0; k < poly.Count; k++)
                    {
                        var a = poly[k]; var b = poly[(k + 1) % poly.Count];
                        int side = SharedSide(dom, a, b);
                        if (side < 0) continue;
                        var n2 = dom.EdgeNormals[side]; var n = new Vec3(n2.X, 0, n2.Z);
                        double sa = Surface(a, out _), sb = Surface(b, out _), ba = Math.Min(hf.Height(a), sa), bb = Math.Min(hf.Height(b), sb);
                        if (sa - ba < 1e-4 && sb - bb < 1e-4) continue;
                        double dfa = MathD.Clamp01((sa - ba) / 0.3), dfb = MathD.Clamp01((sb - bb) / 0.3);
                        int ta = mesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, 0, 0, 0);
                        int tb = mesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, 0, 0, 0);
                        int la = mesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
                        int lb = mesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
                        // polygon runs counter-clockwise (x→z), so a→b along the boundary matches the terrain wall order
                        mesh.AddTriangle(ta, la, lb);
                        mesh.AddTriangle(ta, lb, tb);

                        bool cutIsTable = sa <= waterTable + 0.05 || sb <= waterTable + 0.05;
                        if (cutIsTable)
                        {
                            int tTa = tableMesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, 0, 0, 0);
                            int tTb = tableMesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, 0, 0, 0);
                            int tLa = tableMesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
                            int tLb = tableMesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
                            tableMesh.AddTriangle(tTa, tLa, tLb);
                            tableMesh.AddTriangle(tTa, tLb, tTb);
                        }
                        else
                        {
                            int sTa = streamMesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, 0, 0, 0);
                            int sTb = streamMesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, 0, 0, 0);
                            int sLa = streamMesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.2, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
                            int sLb = streamMesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.2, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
                            streamMesh.AddTriangle(sTa, sLa, sLb);
                            streamMesh.AddTriangle(sTa, sLb, sTb);
                        }
                    }
                }
        }
        return new WaterMeshSet(tableMesh, streamMesh, mesh);
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
