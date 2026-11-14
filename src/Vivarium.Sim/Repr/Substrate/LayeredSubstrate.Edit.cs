using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Material removed by excavation (m3). SolidM3 is indexed by (int)Horizon (Litter..Bedrock); WaterM3 is the
/// liquid that was in the removed pore space; RootMetres is fine-root length that was in the removed volume;
/// StonesRemoved counts stones whose centre was inside an excavation (their volume is moved from the horizon
/// they sat in to Bedrock in SolidM3, so SolidTotal still equals the geometric volume removed).
public sealed class ExcavationLedger
{
    public readonly double[] SolidM3 = new double[6];
    public double WaterM3;
    public double RootMetres;
    public int StonesRemoved;
    public double SolidTotal
    {
        get { double s = 0; for (int i = 1; i < 6; i++) s += SolidM3[i]; return s; }
    }
}

public sealed partial class LayeredSubstrate
{
    private readonly struct Excavation
    {
        public readonly double X, Y, Z, R, R2, SimSeconds;
        public Excavation(Vec3 c, double r, double t) { X = c.X; Y = c.Y; Z = c.Z; R = r; R2 = r * r; SimSeconds = t; }
    }

    private readonly List<Excavation> _exc = new();
    private readonly List<int>?[] _excAt;
    private readonly HashSet<ulong> _removedStones = new();

    /// Number of excavations applied so far.
    public int ExcavationCount => _exc.Count;

    /// Current height at a point: the undisturbed height lowered by every excavation sphere that is open to the sky.
    internal double CurHeight(int col, double x, double z, double und)
    {
        var l = _excAt[col];
        if (l == null) return und;
        double h = und;
        for (int k = 0; k < l.Count; k++)
        {
            var e = _exc[l[k]];
            double dx = x - e.X, dz = z - e.Z, q = e.R2 - dx * dx - dz * dz;
            if (q <= 0) continue;
            double lower = e.Y - Math.Sqrt(q);
            if (lower < h) h = lower;
        }
        return h;
    }

    /// Soil layer boundaries (depth below the undisturbed surface) at a node: Litter, Humus, Mineral, Weathered bottoms.
    private void NodeBounds(int i, int j, Span<double> b)
    {
        int i0 = Math.Min(Nx - 1, i), j0 = Math.Min(Nz - 1, j);
        Profile(i0, j0, i - i0, j - j0, out double T, out double f1, out double f2, out double f3);
        b[0] = T * f1; b[1] = T * f2; b[2] = T * f3; b[3] = T;
    }

    /// Clears all fine-root density across the substrate nodes.
    public void ClearRoots() => Array.Clear(NRoot, 0, NRoot.Length);

    /// Synchronises node surface heights and column geometry where the underlying surface height function
    /// has changed (e.g. local terrain sculpting), preserving water, roots and excavations on untouched columns
    /// and conserving water volume on modified columns. Returns the number of columns updated.
    public int SyncSurface(double simSeconds)
    {
        var changedCols = new HashSet<int>();
        double xMin = double.MaxValue, xMax = double.MinValue;
        double yMin = double.MaxValue, yMax = double.MinValue;
        double zMin = double.MaxValue, zMax = double.MinValue;
        for (int i = 0; i <= Nx; i++)
        {
            for (int j = 0; j <= Nz; j++)
            {
                int n = i * NodesZ + j;
                double x = Ox + i * Cell, z = Oz + j * Cell;
                double s = _spec.SurfaceHeight(new Vec2(x, z));
                if (Math.Abs(s - NSurf[n]) <= 1e-7) continue;
                double oldS = NSurf[n];
                BuildNode(n, x, z);
                xMin = Math.Min(xMin, x - Cell); xMax = Math.Max(xMax, x + Cell);
                yMin = Math.Min(yMin, Math.Min(oldS, s) - _spec.Depth); yMax = Math.Max(yMax, Math.Max(oldS, s) + 0.25);
                zMin = Math.Min(zMin, z - Cell); zMax = Math.Max(zMax, z + Cell);
                for (int ci = Math.Max(0, i - 1); ci <= Math.Min(Nx - 1, i); ci++)
                    for (int cj = Math.Max(0, j - 1); cj <= Math.Min(Nz - 1, j); cj++)
                        changedCols.Add(ci * Nz + cj);
            }
        }
        if (changedCols.Count == 0) return 0;

        Span<double> oldWater = stackalloc double[4];
        foreach (int c in changedCols)
        {
            for (int l = 0; l < 4; l++)
                oldWater[l] = Water.Theta[4 * c + l] * ColH[4 * c + l];
            RefreshColumn(c, init: true);
            double exfiltrated = 0;
            for (int l = 0; l < 4; l++)
            {
                double h = ColH[4 * c + l];
                double w = oldWater[l];
                if (h <= 1e-9)
                {
                    Water.Theta[4 * c + l] = 0;
                    exfiltrated += w;
                }
                else
                {
                    double cap = ColPhi[4 * c + l] * h;
                    double keep = Math.Min(w, cap);
                    Water.Theta[4 * c + l] = keep / h;
                    exfiltrated += Math.Max(0, w - keep);
                }
            }
            if (exfiltrated > 0) Water.Standing[c] += exfiltrated;
        }
        _version++;
        DirtyLog.Publish(new DirtyRecord(
            new Aabb(new Vec3(Math.Max(Ox, xMin), yMin, Math.Max(Oz, zMin)),
                     new Vec3(Math.Min(Ox + Nx * Cell, xMax), yMax, Math.Min(Oz + Nz * Cell, zMax))),
            DirtyDomain.Geometry | DirtyDomain.SoilMaterial | DirtyDomain.Water, _version, "sculpt", simSeconds));
        return changedCols.Count;
    }

    /// Deposits fine roots: a smooth exponential-with-depth distribution inside a horizontal disk, down to
    /// min(depth, soil). Total root length deposited equals metresOfRoot exactly (on the node grid).
    public void AddRootDensity(Vec3 centre, double radius, double depth, double metresOfRoot)
    {
        if (!(radius > 0) || !(depth > 0) || !(metresOfRoot > 0)) return;
        double lambda = depth / 3.0, r2max = radius * radius;
        int i0 = Math.Max(0, (int)Math.Floor((centre.X - radius - Ox) / Cell));
        int i1 = Math.Min(Nx, (int)Math.Ceiling((centre.X + radius - Ox) / Cell));
        int j0 = Math.Max(0, (int)Math.Floor((centre.Z - radius - Oz) / Cell));
        int j1 = Math.Min(Nz, (int)Math.Ceiling((centre.Z + radius - Oz) / Cell));
        var mass = new double[3 * NodeCount];
        var thick = new double[3 * NodeCount];
        double norm = 0;
        Span<double> nb = stackalloc double[4];
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                int n = i * NodesZ + j;
                double dx = Ox + i * Cell - centre.X, dz = Oz + j * Cell - centre.Z;
                double r2 = dx * dx + dz * dz;
                if (r2 >= r2max) continue;
                double hw = (1 - r2 / r2max) * (1 - r2 / r2max);
                NodeBounds(i, j, nb);
                double dmax = Math.Min(depth, nb[3]);
                if (dmax <= 0) continue;
                for (int b = 0; b < 3; b++)
                {
                    double top = b == 0 ? 0 : nb[b];
                    double bot = nb[b + 1];
                    top = Math.Min(top, dmax); bot = Math.Min(bot, dmax);
                    double t = bot - top;
                    if (t <= 1e-9) continue;
                    double m = hw * lambda * (Math.Exp(-top / lambda) - Math.Exp(-bot / lambda));
                    mass[3 * n + b] = m; thick[3 * n + b] = t; norm += m;
                }
            }
        if (norm <= 0) return;              // nothing to root into (bare rock or no soil in reach)
        for (int k = 0; k < mass.Length; k++)
        {
            if (mass[k] <= 0) continue;
            double metres = metresOfRoot * mass[k] / norm;
            NRoot[k] += (float)(metres / (CellArea * thick[k]));
        }
    }

    /// Mean fine-root density (m/m3) of the column's corner nodes for soil layer l.
    private double ColumnRoot(int c, int l)
    {
        int ci = c / Nz, cj = c % Nz;
        int n00 = ci * NodesZ + cj, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        int band = l < 2 ? 0 : (l == 2 ? 1 : 2);
        return 0.25 * (NRoot[n00 * 3 + band] + NRoot[n01 * 3 + band] + NRoot[n10 * 3 + band] + NRoot[n11 * 3 + band]);
    }

    /// Excavates a sphere. The sphere is recorded analytically: Height(p) = min(undisturbed, lower sphere surface),
    /// and what a bowl exposes is the undisturbed horizon at that depth. Material removed (integrated by
    /// quadrature against the profile) is booked to Ledger; roots and stones inside are removed and booked;
    /// stones only cut by the sphere stay. Publishes one DirtyRecord (bounds = the sphere AABB).
    public void Dig(Vec3 c, double r, double simSeconds)
    {
        if (!(r > 0)) return;
        int i0 = Math.Max(0, (int)Math.Floor((c.X - r - Ox) / Cell)), i1 = Math.Min(Nx - 1, (int)Math.Floor((c.X + r - Ox) / Cell));
        int j0 = Math.Max(0, (int)Math.Floor((c.Z - r - Oz) / Cell)), j1 = Math.Min(Nz - 1, (int)Math.Floor((c.Z + r - Oz) / Cell));
        if (i1 < i0 || j1 < j0) return;
        var cols = new List<int>();
        for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) cols.Add(i * Nz + j);
        var oldH = new double[cols.Count * 4];
        var oldS = new double[cols.Count];
        var tmp = new double[4];
        for (int k = 0; k < cols.Count; k++)
        {
            QuadColumn(cols[k], tmp, out oldS[k]);
            for (int l = 0; l < 4; l++) oldH[4 * k + l] = tmp[l];
        }

        // stones with their centre inside the sphere go (found before the sphere exists)
        var gone = new List<Stone>();
        foreach (var st in StonesIn(Aabb.Around(c, r)))
        {
            double dx = st.Centre.X - c.X, dy = st.Centre.Y - c.Y, dz = st.Centre.Z - c.Z;
            if (dx * dx + dy * dy + dz * dz < r * r) gone.Add(st);
        }

        int idx = _exc.Count;
        _exc.Add(new Excavation(c, r, simSeconds));
        foreach (int cc in cols) (_excAt[cc] ??= new List<int>()).Add(idx);

        var newH = new double[cols.Count * 4];
        var newS = new double[cols.Count];
        double totalRemoved = 0;
        for (int k = 0; k < cols.Count; k++)
        {
            QuadColumn(cols[k], tmp, out newS[k]);
            for (int l = 0; l < 4; l++) newH[4 * k + l] = tmp[l];
            totalRemoved += oldS[k] - newS[k];
        }
        if (totalRemoved * CellArea <= 1e-15)
        {
            // the sphere is wholly in air: nothing was removed, undo
            _exc.RemoveAt(idx);
            foreach (int cc in cols) { var li = _excAt[cc]!; li.Remove(idx); if (li.Count == 0) _excAt[cc] = null; }
            return;
        }

        for (int k = 0; k < cols.Count; k++)
        {
            int cc = cols[k];
            double soil = 0;
            for (int l = 0; l < 4; l++)
            {
                double d = Math.Max(0, oldH[4 * k + l] - newH[4 * k + l]);
                soil += d;
                Ledger.SolidM3[l + 1] += d * CellArea;
                Ledger.WaterM3 += Water.Theta[4 * cc + l] * d * CellArea;
                Ledger.RootMetres += ColumnRoot(cc, l) * d * CellArea;
                ColH[4 * cc + l] = newH[4 * k + l];
            }
            double total = Math.Max(0, oldS[k] - newS[k]);
            Ledger.SolidM3[5] += Math.Max(0, total - soil) * CellArea;
            ColSurf[cc] = newS[k];
        }

        foreach (var st in gone)
        {
            _removedStones.Add(st.Id);
            Ledger.StonesRemoved++;
            double v = 4.0 / 3.0 * Math.PI * st.Semi.X * st.Semi.Y * st.Semi.Z;
            double und = UndisturbedHeight(new Vec2(st.Centre.X, st.Centre.Z));
            int layer = 0;
            if (double.IsFinite(und))
            {
                double d = und - st.Centre.Y;
                for (int lev = 1; lev <= 4; lev++)
                {
                    double bd = HorizonBoundaryDepth(new Vec2(st.Centre.X, st.Centre.Z), lev);
                    if (d < bd) { layer = lev - 1; break; }
                    layer = 4;
                }
            }
            if (layer < 4)
            {
                double take = Math.Min(v, Ledger.SolidM3[layer + 1]);
                Ledger.SolidM3[layer + 1] -= take;
                Ledger.SolidM3[5] += take;
            }
        }

        _version++;
        DirtyLog.Publish(new DirtyRecord(Aabb.Around(c, r),
            DirtyDomain.Geometry | DirtyDomain.SoilMaterial | DirtyDomain.Water, _version, "dig", simSeconds));
    }
}
