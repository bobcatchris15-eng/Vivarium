using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Material removed by excavation (m3). SolidM3 is indexed by (int)Horizon (Litter..Bedrock); WaterM3 is the
/// liquid that was in the removed pore space and film.
public sealed class ExcavationLedger
{
    public readonly double[] SolidM3 = new double[6];
    public double WaterM3;
    public double SolidTotal
    {
        get { double s = 0; for (int i = 1; i < 6; i++) s += SolidM3[i]; return s; }
    }
}

public sealed partial class LayeredSubstrate
{
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
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                int n = i * NodesZ + j;
                double dx = Ox + i * Cell - centre.X, dz = Oz + j * Cell - centre.Z;
                double r2 = dx * dx + dz * dz;
                if (r2 >= r2max) continue;
                double hw = (1 - r2 / r2max) * (1 - r2 / r2max);
                double dmax = Math.Min(depth, NB[4 * n + 3]);
                if (dmax <= 0) continue;
                for (int b = 0; b < 3; b++)
                {
                    double top = b == 0 ? 0 : NB[4 * n + b];
                    double bot = NB[4 * n + b + 1];
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

    /// Excavates a sphere. Each node under the sphere is lowered to the sphere bottom where the sphere opens to
    /// the sky; the removed thickness of each horizon is booked to Ledger. Publishes one DirtyRecord.
    public void Dig(Vec3 c, double r, double simSeconds)
    {
        if (!(r > 0)) return;
        double r2 = r * r;
        int i0 = Math.Max(0, (int)Math.Floor((c.X - r - Ox) / Cell)), i1 = Math.Min(Nx, (int)Math.Ceiling((c.X + r - Ox) / Cell));
        int j0 = Math.Max(0, (int)Math.Floor((c.Z - r - Oz) / Cell)), j1 = Math.Min(Nz, (int)Math.Ceiling((c.Z + r - Oz) / Cell));
        var delta = new double[NodeCount];
        bool any = false;
        for (int i = i0; i <= i1; i++)
            for (int j = j0; j <= j1; j++)
            {
                double dx = Ox + i * Cell - c.X, dz = Oz + j * Cell - c.Z;
                double q = r2 - dx * dx - dz * dz;
                if (q <= 0) continue;
                int n = i * NodesZ + j;
                double lower = c.Y - Math.Sqrt(q);
                if (lower < NSurf[n]) { delta[n] = NSurf[n] - lower; any = true; }
            }
        if (!any) return;

        // Book removed material per column from the four corner nodes (exact for the bilinear surface).
        var touched = new List<int>();
        for (int ci = Math.Max(0, i0 - 1); ci <= Math.Min(Nx - 1, i1); ci++)
            for (int cj = Math.Max(0, j0 - 1); cj <= Math.Min(Nz - 1, j1); cj++)
            {
                int cc = ci * Nz + cj;
                int n00 = ci * NodesZ + cj, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
                double d0 = 0, d1 = 0, d2 = 0, d3 = 0, dBed = 0;
                foreach (int n in new[] { n00, n01, n10, n11 })
                {
                    double dl = delta[n];
                    if (dl <= 0) continue;
                    double prev = 0;
                    double[] rem = new double[4];
                    for (int l = 0; l < 4; l++)
                    {
                        double bl = NB[4 * n + l];
                        rem[l] = Math.Min(bl, dl) - Math.Min(prev, dl);
                        prev = bl;
                    }
                    d0 += 0.25 * rem[0]; d1 += 0.25 * rem[1]; d2 += 0.25 * rem[2]; d3 += 0.25 * rem[3];
                    dBed += 0.25 * (dl - Math.Min(NB[4 * n + 3], dl));
                }
                if (d0 == 0 && d1 == 0 && d2 == 0 && d3 == 0 && dBed == 0) continue;
                Ledger.SolidM3[1] += d0 * CellArea;
                Ledger.SolidM3[2] += d1 * CellArea;
                Ledger.SolidM3[3] += d2 * CellArea;
                Ledger.SolidM3[4] += d3 * CellArea;
                Ledger.SolidM3[5] += dBed * CellArea;
                Ledger.WaterM3 += (Water.Theta[4 * cc] * d0 + Water.Theta[4 * cc + 1] * d1
                                 + Water.Theta[4 * cc + 2] * d2 + Water.Theta[4 * cc + 3] * d3) * CellArea;
                touched.Add(cc);
            }

        // Lower the nodes (monotone: boundaries move up by the same amount, never below the surface).
        for (int n = 0; n < NodeCount; n++)
        {
            double dl = delta[n];
            if (dl <= 0) continue;
            NSurf[n] = (float)(NSurf[n] - dl);
            for (int l = 0; l < 4; l++) NB[4 * n + l] = (float)Math.Max(0, NB[4 * n + l] - dl);
        }
        foreach (int cc in touched) RefreshColumn(cc);

        _version++;
        DirtyLog.Publish(new DirtyRecord(Aabb.Around(c, r),
            DirtyDomain.Geometry | DirtyDomain.SoilMaterial | DirtyDomain.Water, _version, "dig", simSeconds));
    }
}
