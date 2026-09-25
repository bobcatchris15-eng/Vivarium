using Vivarium.Sim.Core;

namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// Coarse (4 cm) attractant field C around a plasmodium's occupied cells (docs/overhaul/growth_models.md §6.3).
/// Sparse: only the tiles' bounding box plus a margin is solved, as a dense array over that box (not a
/// dictionary — this is called once per foraging step and dictionary-of-tuple hashing was the dominant cost).
/// Warm-started every step from the previous step's values and advanced with a fixed 8-iteration Jacobi
/// relaxation of <c>Dc*laplacian(C) - Delta*C + Sigma*F = 0</c>, so the result is deterministic given the same
/// inputs. The array is padded with a 1-cell ghost border (always 0 = absorbing boundary), so the inner loop
/// never needs a bounds check.
/// </summary>
public sealed class Attractant
{
    private readonly PlasmodiumParams _prm;

    private int _minCx, _minCz, _w, _h; // interior (non-ghost) domain
    private double[] _values = Array.Empty<double>(); // (w+2) x (h+2), ghost-padded, row-major

    public Attractant(PlasmodiumParams prm) => _prm = prm;

    private int Stride => _w + 2;

    /// <summary>Current concentration at a coarse cell (0 outside the last solved domain).</summary>
    public double At(int cx, int cz)
    {
        int lx = cx - _minCx, lz = cz - _minCz;
        if (lx < 0 || lz < 0 || lx >= _w || lz >= _h) return 0;
        return _values[(lz + 1) * Stride + (lx + 1)];
    }

    /// <summary>Fine (2 cm) cell -> coarse (4 cm) cell, floor division so negative coords are handled correctly.</summary>
    public static (int cx, int cz) CoarseOf(int gx, int gz) =>
        (CoverageSpec.FloorDiv(gx, 2), CoverageSpec.FloorDiv(gz, 2));

    /// <summary>
    /// Central-difference gradient of C at a coarse cell, per metre. Zero where neighbours are both absent
    /// (flat/unsolved region), which is exactly the "no gradient" case the fan-shaped fallback (§6.4) needs.
    /// </summary>
    public Vec2 GradientAt(int cx, int cz)
    {
        double h = _prm.CoarseCellSize;
        double cx1 = At(cx + 1, cz), cx0 = At(cx - 1, cz);
        double cz1 = At(cx, cz + 1), cz0 = At(cx, cz - 1);
        return new Vec2((cx1 - cx0) / (2 * h), (cz1 - cz0) / (2 * h));
    }

    /// <summary>
    /// Rebuilds the domain (bounding box of <paramref name="occupiedFineCells"/> plus the configured margin)
    /// and runs the fixed 8-iteration Jacobi relaxation, warm-started from the previous step's values.
    /// </summary>
    /// <param name="occupiedFineCells">Occupied Plasmodium-layer cells (2 cm) driving where C is solved.</param>
    /// <param name="source">Detritus/food source sampled per coarse cell, F in the PDE.</param>
    public void Step(IEnumerable<(int gx, int gz)> occupiedFineCells, Func<int, int, double> source)
    {
        int marginCells = Math.Max(1, (int)Math.Ceiling(_prm.MarginMeters / _prm.CoarseCellSize));

        bool any = false;
        int minCx = 0, maxCx = 0, minCz = 0, maxCz = 0;
        foreach (var (gx, gz) in occupiedFineCells)
        {
            var (cx, cz) = CoarseOf(gx, gz);
            if (!any) { minCx = maxCx = cx; minCz = maxCz = cz; any = true; continue; }
            if (cx < minCx) minCx = cx; else if (cx > maxCx) maxCx = cx;
            if (cz < minCz) minCz = cz; else if (cz > maxCz) maxCz = cz;
        }

        if (!any) { _w = _h = 0; _values = Array.Empty<double>(); return; }

        minCx -= marginCells; maxCx += marginCells;
        minCz -= marginCells; maxCz += marginCells;
        int w = maxCx - minCx + 1, h = maxCz - minCz + 1;
        int stride = w + 2;

        var f = new double[stride * (h + 2)];
        for (int lz = 0; lz < h; lz++)
            for (int lx = 0; lx < w; lx++)
                f[(lz + 1) * stride + (lx + 1)] = source(minCx + lx, minCz + lz);

        // Warm start: reuse whatever the previous step already solved for cells still in the new domain.
        var cur = new double[stride * (h + 2)];
        if (_w > 0 && _h > 0)
        {
            for (int lz = 0; lz < h; lz++)
            {
                int cz = minCz + lz;
                int oldLz = cz - _minCz;
                if (oldLz < 0 || oldLz >= _h) continue;
                int oldStride = _w + 2;
                for (int lx = 0; lx < w; lx++)
                {
                    int cx = minCx + lx;
                    int oldLx = cx - _minCx;
                    if (oldLx < 0 || oldLx >= _w) continue;
                    cur[(lz + 1) * stride + (lx + 1)] = _values[(oldLz + 1) * oldStride + (oldLx + 1)];
                }
            }
        }

        var next = new double[stride * (h + 2)];
        double hh = _prm.CoarseCellSize;
        double h2 = hh * hh;
        double invDiag = 1.0 / (_prm.Delta + 4 * _prm.Dc / h2); // Delta > 0, so this never divides by zero
        double dcOverH2 = _prm.Dc / h2;

        // Jacobi relaxation toward the steady state of Dc*laplacian(C) - Delta*C + Sigma*F = 0, i.e.
        // (Delta - Dc*laplacian) C = Sigma*F. Unconditionally stable (no dt in the update), so the fixed
        // 8-iteration budget is spent converging rather than risking blow-up at large Dc/h^2 — warm-starting
        // from the previous step's field is what makes 8 iterations enough in practice. Ghost border is
        // always 0 (absorbing boundary), so no per-cell bounds check is needed in the inner loop.
        for (int it = 0; it < PlasmodiumParams.JacobiIterations; it++)
        {
            for (int lz = 0; lz < h; lz++)
            {
                int row = (lz + 1) * stride;
                int rowUp = row - stride, rowDown = row + stride;
                for (int lx = 0; lx < w; lx++)
                {
                    int i = row + lx + 1;
                    double neighbourSum = cur[i - 1] + cur[i + 1] + cur[rowUp + lx + 1] + cur[rowDown + lx + 1];
                    double v = dcOverH2 * neighbourSum * invDiag + _prm.Sigma * f[i] * invDiag;
                    next[i] = double.IsFinite(v) ? Math.Max(0, v) : 0;
                }
            }
            (cur, next) = (next, cur);
        }

        _minCx = minCx; _minCz = minCz; _w = w; _h = h;
        _values = cur;
    }
}
