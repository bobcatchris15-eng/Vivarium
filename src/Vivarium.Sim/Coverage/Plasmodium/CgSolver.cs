namespace Vivarium.Sim.Coverage.Plasmodium;

/// <summary>
/// Fixed-iteration (60), matrix-free conjugate-gradient solve of the weighted graph Laplacian
/// L*p = b, one node pinned to p = 0 (ground) to remove the null space (docs/overhaul/growth_models.md §6.5).
/// No parallel reductions, no early-exit on convergence beyond a residual floor: fixed iteration count and
/// fixed summation order (node index, then adjacency insertion order) so two runs on the same graph produce
/// byte-identical results.
/// </summary>
public static class CgSolver
{
    public const int Iterations = 60;

    /// <summary>One weighted edge between node indices <c>A</c> and <c>B</c>, conductance-over-length <c>W</c>.</summary>
    public readonly record struct Edge(int A, int B, double W);

    /// <summary>
    /// Solves for pressures at <paramref name="n"/> nodes given <paramref name="edges"/> and supply vector
    /// <paramref name="b"/> (positive = source, negative = sink), with node <paramref name="ground"/> pinned
    /// to 0. Returns a fresh pressure array of length n. Concrete <see cref="List{Edge}"/>/<see cref="List{Double}"/>
    /// parameters (not the interface) so the hot indexer calls below devirtualize.
    /// </summary>
    public static double[] Solve(int n, List<Edge> edges, List<double> b, int ground)
    {
        var p = new double[n];
        if (n == 0) return p;

        // Fixed-ordering CSR adjacency: node index ascending, then edge insertion order — same every call for
        // the same edge list, which is what makes the matvec (and therefore the whole solve) deterministic.
        // Built as flat arrays (no per-node List<> allocation) since this runs once per network step.
        int m = edges.Count;
        var degree = new int[n];
        for (int e = 0; e < m; e++) { degree[edges[e].A]++; degree[edges[e].B]++; }
        var offset = new int[n + 1];
        for (int i = 0; i < n; i++) offset[i + 1] = offset[i] + degree[i];
        var adjNode = new int[offset[n]];
        var adjW = new double[offset[n]];
        var cursor = (int[])offset.Clone();
        for (int e = 0; e < m; e++)
        {
            var ed = edges[e];
            adjNode[cursor[ed.A]] = ed.B; adjW[cursor[ed.A]] = ed.W; cursor[ed.A]++;
            adjNode[cursor[ed.B]] = ed.A; adjW[cursor[ed.B]] = ed.W; cursor[ed.B]++;
        }

        void MatVecInto(double[] x, double[] r)
        {
            for (int i = 0; i < n; i++)
            {
                if (i == ground) { r[i] = 0; continue; }
                double s = 0;
                int start = offset[i], end = offset[i + 1];
                for (int k = start; k < end; k++) s += adjW[k] * (x[i] - x[adjNode[k]]);
                r[i] = s;
            }
        }

        var bb = new double[n];
        for (int i = 0; i < n; i++) bb[i] = b[i];
        if (ground >= 0 && ground < n) bb[ground] = 0;

        var ap0 = new double[n];
        MatVecInto(p, ap0);
        var r = new double[n];
        for (int i = 0; i < n; i++) r[i] = bb[i] - ap0[i];
        if (ground >= 0 && ground < n) r[ground] = 0;

        var d = (double[])r.Clone();
        var ad = new double[n];
        double rsOld = Dot(r, r);

        for (int it = 0; it < Iterations; it++)
        {
            if (rsOld < 1e-24) break;
            MatVecInto(d, ad);
            double denom = Dot(d, ad);
            double alpha = denom > 1e-300 ? rsOld / denom : 0;
            for (int i = 0; i < n; i++) p[i] += alpha * d[i];
            for (int i = 0; i < n; i++) r[i] -= alpha * ad[i];
            if (ground >= 0 && ground < n) r[ground] = 0;
            double rsNew = Dot(r, r);
            double beta = rsOld > 1e-300 ? rsNew / rsOld : 0;
            for (int i = 0; i < n; i++) d[i] = r[i] + beta * d[i];
            rsOld = rsNew;
        }

        if (ground >= 0 && ground < n) p[ground] = 0;
        return p;
    }

    private static double Dot(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
