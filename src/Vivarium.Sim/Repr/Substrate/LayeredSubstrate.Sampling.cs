using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

public sealed partial class LayeredSubstrate
{
    /// Allocation-free and read-only apart from the arrays it blends: safe for concurrent reads between sim steps.
    public SubstrateSample SampleSubstrate(Vec3 p)
    {
        var s = default(SubstrateSample);
        double fx = (p.X - Ox) / Cell, fz = (p.Z - Oz) / Cell;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return s;      // outside: void
        Locate(fx, fz, out int i0, out int j0, out double tx, out double tz);
        int n00 = i0 * NodesZ + j0, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        int c = i0 * Nz + j0;
        double und = Profile(i0, j0, tx, tz, out double T, out double f1, out double f2, out double f3);
        double cur = _excAt[c] == null ? und : CurHeight(c, p.X, p.Z, und);
        bool stoneCol = ColStoneSurf[c] > 0 || ColStoneDeep[c] > 0;

        int h;
        if (p.Y > cur)
        {
            // Open air, or a stone that survives an excavation / protrudes through the surface.
            if (stoneCol && p.Y <= und + StoneReach && InStone(p, p.Y <= und)) h = 4;
            else
            {
                s.Horizon = p.Y <= cur + Water.Standing[c] ? Horizon.Water : Horizon.Void;
                return s;
            }
        }
        else
        {
            double d = und - p.Y;               // depth below the UNDISTURBED surface
            if (T <= 0) h = 4;
            else if (d < T * f1) h = 0;
            else if (d < T * f2) h = 1;
            else if (d < T * f3) h = 2;
            else if (d < T) h = 3;
            else h = 4;
            if (h < 4 && stoneCol && d <= T + 0.03 && InStone(p, true)) h = 4;     // buried stone: rock body
        }

        double w00 = (1 - tx) * (1 - tz), w01 = (1 - tx) * tz, w10 = tx * (1 - tz), w11 = tx * tz;
        int a00 = (n00 * Hc + h) * TabStride, a01 = (n01 * Hc + h) * TabStride;
        int a10 = (n10 * Hc + h) * TabStride, a11 = (n11 * Hc + h) * TabStride;
        s.Solid = true;
        s.Horizon = (Horizon)(h + 1);
        s.CoarseFragments = (float)Bi(NTab, a00 + F_CF, a01 + F_CF, a10 + F_CF, a11 + F_CF, w00, w01, w10, w11);
        s.Sand = (float)Bi(NTab, a00 + F_SAND, a01 + F_SAND, a10 + F_SAND, a11 + F_SAND, w00, w01, w10, w11);
        s.Silt = (float)Bi(NTab, a00 + F_SILT, a01 + F_SILT, a10 + F_SILT, a11 + F_SILT, w00, w01, w10, w11);
        s.Clay = (float)Bi(NTab, a00 + F_CLAY, a01 + F_CLAY, a10 + F_CLAY, a11 + F_CLAY, w00, w01, w10, w11);
        s.Organic = (float)Bi(NTab, a00 + F_ORG, a01 + F_ORG, a10 + F_ORG, a11 + F_ORG, w00, w01, w10, w11);
        s.Porosity = (float)Bi(NTab, a00 + F_PHI, a01 + F_PHI, a10 + F_PHI, a11 + F_PHI, w00, w01, w10, w11);
        s.AggregateDiameter = (float)Bi(NTab, a00 + F_AGG, a01 + F_AGG, a10 + F_AGG, a11 + F_AGG, w00, w01, w10, w11);
        s.Compaction = (float)Bi(NTab, a00 + F_COMP, a01 + F_COMP, a10 + F_COMP, a11 + F_COMP, w00, w01, w10, w11);
        s.Strength = (float)Bi(NTab, a00 + F_STR, a01 + F_STR, a10 + F_STR, a11 + F_STR, w00, w01, w10, w11);
        s.FieldCapacity = (float)Bi(NTab, a00 + F_FC, a01 + F_FC, a10 + F_FC, a11 + F_FC, w00, w01, w10, w11);
        s.WiltingPoint = (float)Bi(NTab, a00 + F_WP, a01 + F_WP, a10 + F_WP, a11 + F_WP, w00, w01, w10, w11);
        s.ColonizationCapacity = (float)Bi(NTab, a00 + F_COL, a01 + F_COL, a10 + F_COL, a11 + F_COL, w00, w01, w10, w11);
        s.MineralHue = new Vec3(
            Bi(NTab, a00 + F_HR, a01 + F_HR, a10 + F_HR, a11 + F_HR, w00, w01, w10, w11),
            Bi(NTab, a00 + F_HG, a01 + F_HG, a10 + F_HG, a11 + F_HG, w00, w01, w10, w11),
            Bi(NTab, a00 + F_HB, a01 + F_HB, a10 + F_HB, a11 + F_HB, w00, w01, w10, w11));

        if (h == 4)
        {
            s.Permeability = 1e-9f;
            s.WaterContent = 0;
        }
        else
        {
            s.Permeability = (float)ColK[4 * c + h];
            s.WaterContent = (float)Math.Min(Water.Theta[4 * c + h], s.Porosity);
        }
        if (h == 4 || h == 3) s.BeddingNormal = BedN; else s.BeddingNormal = Vec3.Up;

        int band = h < 2 ? 0 : h == 2 ? 1 : h == 3 ? 2 : -1;
        if (band >= 0)
            s.FineRootDensity = (float)Bi(NRoot, n00 * 3 + band, n01 * 3 + band, n10 * 3 + band, n11 * 3 + band, w00, w01, w10, w11);
        return s;
    }

    /// Bilinear blend of one field across four node-table entries (weights sum to 1).
    private static double Bi(float[] t, int i00, int i01, int i10, int i11, double w00, double w01, double w10, double w11) =>
        w00 * t[i00] + w01 * t[i01] + w10 * t[i10] + w11 * t[i11];

    /// Nearest exposed surface by vertical projection, within maxDistance.
    public bool SurfaceSample(Vec3 position, double maxDistance, out SurfaceSample s)
    {
        s = default;
        double fx = (position.X - Ox) / Cell, fz = (position.Z - Oz) / Cell;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return false;
        double surf = Height(position.XZ);
        if (Math.Abs(position.Y - surf) > maxDistance) return false;
        double hx = 0.5 * Cell;
        double gx = (ClampedHeight(position.X + hx, position.Z) - ClampedHeight(position.X - hx, position.Z)) / (2 * hx);
        double gz = (ClampedHeight(position.X, position.Z + hx) - ClampedHeight(position.X, position.Z - hx)) / (2 * hx);
        int c = ColumnAt(position.X, position.Z);
        s.Position = new Vec3(position.X, surf, position.Z);
        s.Normal = new Vec3(-gx, 1, -gz).Normalized();
        s.Material = SampleSubstrate(new Vec3(position.X, surf - 1e-4, position.Z));
        s.FilmWater = (float)Water.Film[c];
        s.StandingWater = (float)Water.Standing[c];
        s.SurfaceId = (ulong)c;               // bit 63 clear: ground column index
        return true;
    }

    private double ClampedHeight(double x, double z)
    {
        double cx = Math.Min(Math.Max(x, Ox), Ox + Nx * Cell);
        double cz = Math.Min(Math.Max(z, Oz), Oz + Nz * Cell);
        return Height(new Vec2(cx, cz));
    }
}
