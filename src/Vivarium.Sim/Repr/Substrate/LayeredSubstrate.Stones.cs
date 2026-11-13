using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// A buried stone: flattened ellipsoid of the bedrock body. AxisA/AxisB span the bedding plane, AxisC is the
/// bedding normal; Semi = (a, b, c) semi-axes along those axes.
public readonly record struct Stone(ulong Id, Vec3 Centre, Vec3 Semi, Vec3 AxisA, Vec3 AxisB, Vec3 AxisC);

public sealed partial class LayeredSubstrate
{
    internal const double StoneCell = 0.08, StoneReach = 0.04;
    private const ulong StoneSalt = 0x570BE5A17UL;

    /// At most one stone per hashed cell. Presence uses the column at the cell's nominal centre so the
    /// probability is independent of where the stone lands inside the cell.
    private bool TryStoneInCell(long ix, long iy, long iz, out Stone st)
    {
        st = default;
        ulong hs = Rng.Mix(Rng.Mix(Rng.Mix(Seed ^ StoneSalt, (ulong)ix), (ulong)iy), (ulong)iz);
        int nominal = ColumnAt((ix + 0.5) * StoneCell, (iz + 0.5) * StoneCell);
        if (nominal < 0) return false;
        if (ReprId.Unit(hs, 1) >= ColStoneP[nominal]) return false;

        double cx = (ix + ReprId.Unit(hs, 2)) * StoneCell;
        double cy = (iy + ReprId.Unit(hs, 3)) * StoneCell;
        double cz = (iz + ReprId.Unit(hs, 4)) * StoneCell;
        double fx = (cx - Ox) / Cell, fz = (cz - Oz) / Cell;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return false;
        Locate(fx, fz, out int i0, out int j0, out double tx, out double tz);
        int n00 = i0 * NodesZ + j0, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        double surf = Bil(NSurf, n00, n01, n10, n11, tx, tz);
        double b2 = Bil(NB, 4 * n00 + 1, 4 * n01 + 1, 4 * n10 + 1, 4 * n11 + 1, tx, tz);
        double b4 = Bil(NB, 4 * n00 + 3, 4 * n01 + 3, 4 * n10 + 3, 4 * n11 + 3, tx, tz);
        double depth = surf - cy;
        double lo = b4 < 0.1 ? -0.03 : b2;                // thin soil: stones may poke through
        if (depth < lo || depth > b4 + 0.03) return false;  // only in Mineral/Weathered (or poking through)

        double a = ReprId.Range(hs, 5, 0.010, StoneReach);
        double bb = a * ReprId.Range(hs, 6, 0.6, 1.0);
        double cc = 0.5 * a;
        Vec3 n = BedN;
        Vec3 refv = Math.Abs(n.Y) < 0.9 ? Vec3.Up : Vec3.Right;
        Vec3 t1 = refv.Cross(n).Normalized();
        Vec3 t2 = n.Cross(t1);
        double ang = ReprId.Range(hs, 7, 0, 2 * Math.PI);
        Vec3 axA = t1 * Math.Cos(ang) + t2 * Math.Sin(ang);
        Vec3 axB = n.Cross(axA);
        st = new Stone(Rng.Mix(hs, 0x1D), new Vec3(cx, cy, cz), new Vec3(a, bb, cc), axA, axB, n);
        return true;
    }

    private static bool Contains(in Stone st, Vec3 p)
    {
        Vec3 v = p - st.Centre;
        double x = v.Dot(st.AxisA) / st.Semi.X, y = v.Dot(st.AxisB) / st.Semi.Y, z = v.Dot(st.AxisC) / st.Semi.Z;
        return x * x + y * y + z * z <= 1.0;
    }

    /// True if the point lies inside a buried stone.
    public bool IsInStone(Vec3 p) => InStone(p);

    internal bool InStone(Vec3 p)
    {
        long x0 = (long)Math.Floor((p.X - StoneReach) / StoneCell), x1 = (long)Math.Floor((p.X + StoneReach) / StoneCell);
        long y0 = (long)Math.Floor((p.Y - StoneReach) / StoneCell), y1 = (long)Math.Floor((p.Y + StoneReach) / StoneCell);
        long z0 = (long)Math.Floor((p.Z - StoneReach) / StoneCell), z1 = (long)Math.Floor((p.Z + StoneReach) / StoneCell);
        for (long ix = x0; ix <= x1; ix++)
            for (long iy = y0; iy <= y1; iy++)
                for (long iz = z0; iz <= z1; iz++)
                    if (TryStoneInCell(ix, iy, iz, out Stone st) && Contains(st, p)) return true;
        return false;
    }

    /// Stones whose bounding box can overlap the query box (renderers mesh these).
    public IEnumerable<Stone> StonesIn(Aabb box)
    {
        long x0 = (long)Math.Floor((box.Min.X - StoneReach) / StoneCell), x1 = (long)Math.Floor((box.Max.X + StoneReach) / StoneCell);
        long y0 = (long)Math.Floor((box.Min.Y - StoneReach) / StoneCell), y1 = (long)Math.Floor((box.Max.Y + StoneReach) / StoneCell);
        long z0 = (long)Math.Floor((box.Min.Z - StoneReach) / StoneCell), z1 = (long)Math.Floor((box.Max.Z + StoneReach) / StoneCell);
        for (long ix = x0; ix <= x1; ix++)
            for (long iy = y0; iy <= y1; iy++)
                for (long iz = z0; iz <= z1; iz++)
                    if (TryStoneInCell(ix, iy, iz, out Stone st)) yield return st;
    }
}
