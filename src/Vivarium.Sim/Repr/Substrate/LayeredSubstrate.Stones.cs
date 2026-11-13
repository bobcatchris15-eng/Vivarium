using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// A stone: flattened ellipsoid of the bedrock body. AxisA/AxisB span the bedding plane, AxisC is the bedding
/// normal; Semi = (a, b, c) semi-axes along those axes.
public readonly record struct Stone(ulong Id, Vec3 Centre, Vec3 Semi, Vec3 AxisA, Vec3 AxisB, Vec3 AxisC);

public sealed partial class LayeredSubstrate
{
    internal const double StoneCell = 0.12, StoneReach = 0.06;
    private const ulong StoneSalt = 0x570BE5A17UL, SurfSalt = 0x5A5F0C3E1UL;
    private double _pSurfMax, _pDeepMax;

    /// Per-column stone probabilities (per stone cell). Tied to the local coarse-fragment fraction computed from the
    /// horizon tables and the horizon thicknesses (never from SampleSubstrate, so no recursion). Surface stones are
    /// boosted where the soil is thin (near outcrops, where weathering and colluvium leave rubble).
    private void StoneProbabilities(int c, int n00, int n01, int n10, int n11)
    {
        double wsum = 0, cfTop = 0, top = 0;
        double cfDeep = 0;
        for (int l = 0; l < 4; l++)
        {
            double cf = 0.25 * (NTab[(n00 * Hc + l) * TabStride + F_CF] + NTab[(n01 * Hc + l) * TabStride + F_CF]
                              + NTab[(n10 * Hc + l) * TabStride + F_CF] + NTab[(n11 * Hc + l) * TabStride + F_CF]);
            double th = ColH[4 * c + l];
            double w = Math.Max(0, Math.Min(top + th, 0.15) - Math.Min(top, 0.15));
            wsum += w; cfTop += cf * w; top += th;
            if (l >= 2) cfDeep += 0.5 * cf;
        }
        double soil = top;
        if (soil <= 1e-6) { ColStoneSurf[c] = 0; ColStoneDeep[c] = 0; return; }
        cfTop = wsum > 0 ? cfTop / wsum : 0;
        double thin = 1 - MathD.SmoothStep(0.02, 0.28, soil);
        ColStoneSurf[c] = MathD.Clamp(1.6 * cfTop * (1 + 3.0 * thin), 0, 0.8);
        ColStoneDeep[c] = MathD.Clamp(1.5 * cfDeep, 0, 0.7);
        _pSurfMax = Math.Max(_pSurfMax, ColStoneSurf[c]);
        _pDeepMax = Math.Max(_pDeepMax, ColStoneDeep[c]);
    }

    /// Skewed size (many small, few large): full length 1..12 cm; returns the longest semi-axis.
    private static double StoneSemi(ulong hs)
    {
        double u = ReprId.Unit(hs, 5);
        return 0.5 * (0.01 + 0.11 * u * u * u);
    }

    private static void StoneFrame(LayeredSubstrate s, ulong hs, double a, Vec3 centre, out Stone st, ulong idSalt)
    {
        double bb = a * ReprId.Range(hs, 6, 0.6, 1.0);
        double cc = a * ReprId.Range(hs, 9, 0.35, 0.55);      // flattened along bedding
        Vec3 n = s.BedN;
        Vec3 refv = Math.Abs(n.Y) < 0.9 ? Vec3.Up : Vec3.Right;
        Vec3 t1 = refv.Cross(n).Normalized();
        Vec3 t2 = n.Cross(t1);
        double ang = ReprId.Range(hs, 7, 0, 2 * Math.PI);
        Vec3 axA = t1 * Math.Cos(ang) + t2 * Math.Sin(ang);
        Vec3 axB = n.Cross(axA);
        st = new Stone(Rng.Mix(hs, idSalt), centre, new Vec3(a, bb, cc), axA, axB, n);
    }

    /// Vertical half-extent of the stone ellipsoid (m).
    public static double VerticalExtent(in Stone st)
    {
        double x = st.AxisA.Y * st.Semi.X, y = st.AxisB.Y * st.Semi.Y, z = st.AxisC.Y * st.Semi.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// Surface-set stone, one candidate per 2D cell. Centre sits 0.2..0.9 of its vertical half-extent below the
    /// undisturbed surface so it is partially embedded and breaks the surface. probe (if given) short-circuits
    /// stones that cannot contain it.
    private bool TrySurfaceStone(long ix, long iz, bool hasProbe, Vec3 probe, out Stone st)
    {
        st = default;
        ulong hs = Rng.Mix(Rng.Mix(Seed ^ SurfSalt, (ulong)ix), (ulong)iz);
        double pr = ReprId.Unit(hs, 1);
        if (pr >= _pSurfMax) return false;
        double cx = (ix + ReprId.Unit(hs, 2)) * StoneCell, cz = (iz + ReprId.Unit(hs, 4)) * StoneCell;
        double a = StoneSemi(hs);
        if (hasProbe)
        {
            double dx = probe.X - cx, dz = probe.Z - cz;
            if (dx * dx + dz * dz > a * a) return false;
        }
        int nominal = ColumnAt((ix + 0.5) * StoneCell, (iz + 0.5) * StoneCell);
        if (nominal < 0 || pr >= ColStoneSurf[nominal]) return false;
        if (_removedStones.Count > 0 && _removedStones.Contains(Rng.Mix(hs, 0x2D))) return false;
        if (!Inside(cx, cz, out int i0, out int j0, out double tx, out double tz)) return false;
        double und = Profile(i0, j0, tx, tz, out double T, out _, out _, out _);
        if (T < 0.003) return false;                       // no soil to sit in
        StoneFrame(this, hs, a, default, out st, 0x2D);
        double ve = VerticalExtent(st);
        double cy = und - ve * ReprId.Range(hs, 8, 0.2, 0.9);
        st = st with { Centre = new Vec3(cx, cy, cz) };
        return true;
    }

    /// Buried stone, one candidate per 3D cell: only within Mineral/Weathered (or poking into bedrock).
    private bool TryDeepStone(long ix, long iy, long iz, bool hasProbe, Vec3 probe, out Stone st)
    {
        st = default;
        ulong hs = Rng.Mix(Rng.Mix(Rng.Mix(Seed ^ StoneSalt, (ulong)ix), (ulong)iy), (ulong)iz);
        double pr = ReprId.Unit(hs, 1);
        if (pr >= _pDeepMax) return false;
        double cx = (ix + ReprId.Unit(hs, 2)) * StoneCell, cy = (iy + ReprId.Unit(hs, 3)) * StoneCell, cz = (iz + ReprId.Unit(hs, 4)) * StoneCell;
        double a = StoneSemi(hs);
        if (hasProbe)
        {
            double dx = probe.X - cx, dy = probe.Y - cy, dz = probe.Z - cz;
            if (dx * dx + dy * dy + dz * dz > a * a) return false;
        }
        int nominal = ColumnAt((ix + 0.5) * StoneCell, (iz + 0.5) * StoneCell);
        if (nominal < 0 || pr >= ColStoneDeep[nominal]) return false;
        if (_removedStones.Count > 0 && _removedStones.Contains(Rng.Mix(hs, 0x1D))) return false;
        if (!Inside(cx, cz, out int i0, out int j0, out double tx, out double tz)) return false;
        double und = Profile(i0, j0, tx, tz, out double T, out _, out double f2, out _);
        if (T <= 0) return false;
        double depth = und - cy;
        if (depth < T * f2 || depth > T + 0.03) return false;   // only in Mineral/Weathered (or poking through)
        StoneFrame(this, hs, a, new Vec3(cx, cy, cz), out st, 0x1D);
        return true;
    }

    private static bool Contains(in Stone st, Vec3 p)
    {
        Vec3 v = p - st.Centre;
        double x = v.Dot(st.AxisA) / st.Semi.X, y = v.Dot(st.AxisB) / st.Semi.Y, z = v.Dot(st.AxisC) / st.Semi.Z;
        return x * x + y * y + z * z <= 1.0;
    }

    /// True if the point lies inside a stone (surface-set or buried).
    public bool IsInStone(Vec3 p) => InStone(p, true);

    internal bool InStone(Vec3 p, bool deep)
    {
        long x0 = (long)Math.Floor((p.X - StoneReach) / StoneCell), x1 = (long)Math.Floor((p.X + StoneReach) / StoneCell);
        long z0 = (long)Math.Floor((p.Z - StoneReach) / StoneCell), z1 = (long)Math.Floor((p.Z + StoneReach) / StoneCell);
        for (long ix = x0; ix <= x1; ix++)
            for (long iz = z0; iz <= z1; iz++)
                if (TrySurfaceStone(ix, iz, true, p, out Stone st) && Contains(st, p)) return true;
        if (!deep || _pDeepMax <= 0) return false;
        long y0 = (long)Math.Floor((p.Y - StoneReach) / StoneCell), y1 = (long)Math.Floor((p.Y + StoneReach) / StoneCell);
        for (long ix = x0; ix <= x1; ix++)
            for (long iy = y0; iy <= y1; iy++)
                for (long iz = z0; iz <= z1; iz++)
                    if (TryDeepStone(ix, iy, iz, true, p, out Stone st) && Contains(st, p)) return true;
        return false;
    }

    /// Stones whose bounding box can overlap the query box (renderers mesh these). Removed stones are excluded.
    public IEnumerable<Stone> StonesIn(Aabb box)
    {
        long x0 = (long)Math.Floor((box.Min.X - StoneReach) / StoneCell), x1 = (long)Math.Floor((box.Max.X + StoneReach) / StoneCell);
        long y0 = (long)Math.Floor((box.Min.Y - StoneReach) / StoneCell), y1 = (long)Math.Floor((box.Max.Y + StoneReach) / StoneCell);
        long z0 = (long)Math.Floor((box.Min.Z - StoneReach) / StoneCell), z1 = (long)Math.Floor((box.Max.Z + StoneReach) / StoneCell);
        for (long ix = x0; ix <= x1; ix++)
            for (long iz = z0; iz <= z1; iz++)
                if (TrySurfaceStone(ix, iz, false, default, out Stone st)) yield return st;
        for (long ix = x0; ix <= x1; ix++)
            for (long iy = y0; iy <= y1; iy++)
                for (long iz = z0; iz <= z1; iz++)
                    if (TryDeepStone(ix, iy, iz, false, default, out Stone st)) yield return st;
    }
}
