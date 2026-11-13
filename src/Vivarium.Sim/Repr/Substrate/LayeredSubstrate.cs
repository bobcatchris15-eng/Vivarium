using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Core layered substrate. Geometry lives on NODES (Nx+1 by Nz+1, spacing Cell); each COLUMN (Nx by Nz)
/// is the cell between four nodes. Horizon boundaries are stored per node as cumulative depths below the
/// surface, so bilinear blending across nodes keeps every boundary continuous. Water is per column.
/// Layer index l = 0 Litter, 1 Humus, 2 MineralSoil, 3 Weathered; bedrock is below the 4th boundary.
public sealed partial class LayeredSubstrate : ISubstrate
{
    internal const int Layers = 4;
    internal const int Hc = 5;            // horizon tables: Litter, Humus, Mineral, Weathered, Bedrock
    internal const int TabStride = 16;    // composition fields per node per horizon
    internal const int F_CF = 0, F_SAND = 1, F_SILT = 2, F_CLAY = 3, F_ORG = 4, F_PHI = 5, F_AGG = 6,
        F_COMP = 7, F_STR = 8, F_FC = 9, F_WP = 10, F_LOGK = 11, F_COL = 12, F_HR = 13, F_HG = 14, F_HB = 15;

    private readonly SubstrateSpec _spec;
    internal readonly ulong Seed;
    internal readonly int Nx, Nz, NodesZ, NodeCount, ColumnCount;
    internal readonly double Ox, Oz, Cell, CellArea;
    private readonly double _cx, _cz, _y0, _nxp, _nzp, _knX, _knZ;
    private readonly ulong _ridgeSeed, _resSeed;
    internal readonly Vec3 BedN, ParentHue;
    private readonly Aabb _bounds;
    private ulong _version;

    // node arrays
    internal readonly float[] NSurf;      // m, top of ground (after outcrop protrusion)
    internal readonly float[] NB;         // 4 per node: cumulative depth below surface of bottoms of Litter, Humus, Mineral, Weathered
    internal readonly float[] NTab;       // per node per horizon composition table (Hc * TabStride per node)
    internal readonly float[] NRoot;      // 3 per node: fine-root density (m/m3) for organic, mineral, weathered bands
    internal readonly float[] NSteep;     // 0..1 slope steepness

    // column arrays
    internal readonly double[] ColH;      // 4 per column: layer thickness (m)
    internal readonly double[] ColK;      // 4 per column: saturated conductivity (m/s)
    internal readonly float[] ColPhi, ColFC, ColWP; // 4 per column
    internal readonly double[] ColSurf;   // mean surface of the column's nodes
    internal readonly double[] ColSteep;
    internal readonly double[] ColStoneP; // probability per stone cell in this column

    public DirtyLog DirtyLog { get; } = new DirtyLog();
    public ExcavationLedger Ledger { get; } = new ExcavationLedger();
    public SubstrateWater Water { get; }
    public ulong Version => _version;
    public Aabb Bounds => _bounds;

    public LayeredSubstrate(SubstrateSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        if (spec.Nx < 1 || spec.Nz < 1 || !(spec.Cell > 0)) throw new ArgumentException("Substrate grid must be at least 1x1 with a positive cell.");
        Seed = spec.Seed;
        Nx = spec.Nx; Nz = spec.Nz; NodesZ = Nz + 1; NodeCount = (Nx + 1) * NodesZ; ColumnCount = Nx * Nz;
        Ox = spec.Origin.X; Oz = spec.Origin.Z; Cell = spec.Cell; CellArea = Cell * Cell;
        _cx = Ox + 0.5 * Nx * Cell; _cz = Oz + 0.5 * Nz * Cell;

        ulong h = ReprId.Hash(Seed, 0x51B57A7EUL);
        double dip = ReprId.Range(h, 1, 0.1745, 0.384);            // 10..22 degrees
        double strike = ReprId.Unit(h, 2) * 2 * Math.PI;
        BedN = new Vec3(Math.Sin(dip) * Math.Sin(strike), Math.Cos(dip), Math.Sin(dip) * Math.Cos(strike));
        _nxp = BedN.X / BedN.Y; _nzp = BedN.Z / BedN.Y;
        _knX = _cx + ReprId.Range(h, 3, -0.1, 0.1) * Nx * Cell;   // one seeded knoll of bedrock drives an outcrop
        _knZ = _cz + ReprId.Range(h, 4, -0.1, 0.1) * Nz * Cell;
        _y0 = spec.SurfaceHeight(new Vec2(_cx, _cz)) - 0.35;      // bedding plane anchored 35 cm under mid-surface
        _ridgeSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-ridge"));
        _resSeed = Rng.Mix(Seed, Hash.Fnv1a64("soil-residual"));
        ParentHue = new Vec3(0.50 + 0.10 * ReprId.Unit(h, 5) - 0.05,
                             0.44 + 0.08 * ReprId.Unit(h, 6) - 0.04,
                             0.36 + 0.08 * ReprId.Unit(h, 7) - 0.04);

        NSurf = new float[NodeCount];
        NB = new float[NodeCount * 4];
        NTab = new float[NodeCount * Hc * TabStride];
        NRoot = new float[NodeCount * 3];
        NSteep = new float[NodeCount];
        ColH = new double[ColumnCount * 4];
        ColK = new double[ColumnCount * 4];
        ColPhi = new float[ColumnCount * 4];
        ColFC = new float[ColumnCount * 4];
        ColWP = new float[ColumnCount * 4];
        ColSurf = new double[ColumnCount];
        ColSteep = new double[ColumnCount];
        ColStoneP = new double[ColumnCount];

        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= NodesZ - 1; j++)
                BuildNode(i * NodesZ + j, Ox + i * Cell, Oz + j * Cell);

        double smin = double.MaxValue, smax = double.MinValue;
        for (int n = 0; n < NodeCount; n++) { smin = Math.Min(smin, NSurf[n]); smax = Math.Max(smax, NSurf[n]); }
        _bounds = new Aabb(new Vec3(Ox, smin - spec.Depth, Oz), new Vec3(Ox + Nx * Cell, smax + 0.25, Oz + Nz * Cell));

        for (int c = 0; c < ColumnCount; c++) RefreshColumn(c);
        Water = new SubstrateWater(this);
    }

    /// Bedrock top height at (x,z) before outcrop handling: a bedding plane, strata steps, ridged relief, one knoll.
    private double Bed(double x, double z)
    {
        double dx = x - _cx, dz = z - _cz;
        double plane = _y0 - _nxp * dx - _nzp * dz;
        double s = -(_nxp * dx + _nzp * dz) / 0.25;               // strata coordinate, 25 cm per step
        double fr = s - Math.Floor(s);
        double stepT = 0.015 * (Math.Floor(s) + MathD.SmoothStep(0.5, 1.0, fr)); // continuous staircase
        double ridge = 1 - 2 * Math.Abs(Noise.Fbm(_ridgeSeed, x * 1.2, z * 1.2, 3));
        double kx = x - _knX, kz = z - _knZ;
        double knoll = 0.55 * Math.Exp(-(kx * kx + kz * kz) / (2 * 0.10 * 0.10));
        return plane + stepT + 0.03 * ridge + knoll;
    }

    private void BuildNode(int n, double x, double z)
    {
        var p = new Vec2(x, z);
        double s = _spec.SurfaceHeight(p);
        double b = Bed(x, z);
        double w = MathD.SmoothStep(-0.02, 0.0, b - s);
        double surf = s + w * MathD.Clamp(b - s, 0, 0.15);        // outcrop protrudes up to 15 cm
        double dd = surf - b;                                     // depth to bedrock (<=0 at an outcrop)
        double gx = (_spec.SurfaceHeight(new Vec2(x + Cell, z)) - _spec.SurfaceHeight(new Vec2(x - Cell, z))) / (2 * Cell);
        double gz = (_spec.SurfaceHeight(new Vec2(x, z + Cell)) - _spec.SurfaceHeight(new Vec2(x, z - Cell))) / (2 * Cell);
        double steep = MathD.Clamp01(Math.Sqrt(gx * gx + gz * gz) / 0.5);
        double drain = MathD.Clamp01(_spec.DrainagePotential(p));
        double org = MathD.Clamp01(_spec.OrganicInput(p));
        double wet = 1 - drain;
        double res = Noise.Fbm(_resSeed, x * 0.8, z * 0.8, 2);

        double t = Math.Max(0, dd) * (1 - 0.5 * steep);
        t = Math.Min(t, _spec.Depth);
        double l1 = MathD.Clamp(0.005 + 0.02 * org * (1 - 0.6 * steep), 0, 0.03);
        double l2 = MathD.Clamp(0.01 + 0.10 * org * (0.5 + 0.5 * wet) * (1 - 0.5 * steep), 0.01, 0.12);
        double l3 = MathD.Clamp((0.05 + 0.40 * (1 - steep) * (0.5 + 0.5 * wet)) * (1 + 0.15 * res), 0.05, 0.45)
                    * (0.2 + 0.8 * MathD.Clamp01(dd / 0.2));
        double l4 = MathD.Clamp(0.05 + 0.25 * (1 - 0.5 * steep), 0.05, 0.30);
        double sum = l1 + l2 + l3 + l4;
        double k = sum > 0 ? t / sum : 0;
        double c1 = l1 * k, c2 = c1 + l2 * k, c3 = c2 + l3 * k, c4 = c3 + l4 * k; // c4 == t: soil bottom

        NSurf[n] = (float)surf;
        NSteep[n] = (float)steep;
        NB[4 * n] = (float)c1; NB[4 * n + 1] = (float)c2; NB[4 * n + 2] = (float)c3; NB[4 * n + 3] = (float)c4;
        for (int hh = 0; hh < Hc; hh++)
            Compose(hh, drain, org, steep, ParentHue, NTab, (n * Hc + hh) * TabStride);
    }

    /// Recomputes the column view (thicknesses, surface, conductivity) from its four corner nodes.
    internal void RefreshColumn(int c)
    {
        int ci = c / Nz, cj = c % Nz;
        int n00 = ci * NodesZ + cj, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        ColSurf[c] = 0.25 * (NSurf[n00] + NSurf[n01] + NSurf[n10] + NSurf[n11]);
        ColSteep[c] = 0.25 * (NSteep[n00] + NSteep[n01] + NSteep[n10] + NSteep[n11]);
        ColStoneP[c] = 0.5 * (0.35 + 0.2 * ColSteep[c]);
        double m1 = 0.25 * (NB[4 * n00] + NB[4 * n01] + NB[4 * n10] + NB[4 * n11]);
        double m2 = 0.25 * (NB[4 * n00 + 1] + NB[4 * n01 + 1] + NB[4 * n10 + 1] + NB[4 * n11 + 1]);
        double m3 = 0.25 * (NB[4 * n00 + 2] + NB[4 * n01 + 2] + NB[4 * n10 + 2] + NB[4 * n11 + 2]);
        double m4 = 0.25 * (NB[4 * n00 + 3] + NB[4 * n01 + 3] + NB[4 * n10 + 3] + NB[4 * n11 + 3]);
        ColH[4 * c] = Math.Max(0, m1);
        ColH[4 * c + 1] = Math.Max(0, m2 - m1);
        ColH[4 * c + 2] = Math.Max(0, m3 - m2);
        ColH[4 * c + 3] = Math.Max(0, m4 - m3);
        for (int l = 0; l < Layers; l++)
        {
            double phi = 0, fc = 0, wp = 0, lk = 0;
            foreach (int n in new[] { n00, n01, n10, n11 })
            {
                int a = (n * Hc + l) * TabStride;
                phi += NTab[a + F_PHI]; fc += NTab[a + F_FC]; wp += NTab[a + F_WP]; lk += NTab[a + F_LOGK];
            }
            ColPhi[4 * c + l] = (float)(0.25 * phi);
            ColFC[4 * c + l] = (float)(0.25 * fc);
            ColWP[4 * c + l] = (float)(0.25 * wp);
            ColK[4 * c + l] = Math.Pow(10, 0.25 * lk);
        }
    }

    // ------------------------------------------------------------ diagnostics and accessors

    /// Column index for a ground point, or -1 outside the domain.
    public int ColumnAt(Vec2 p) => ColumnAt(p.X, p.Z);

    internal int ColumnAt(double x, double z)
    {
        double fx = (x - Ox) / Cell, fz = (z - Oz) / Cell;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return -1;
        int i = Math.Min(Nx - 1, (int)Math.Floor(fx)), j = Math.Min(Nz - 1, (int)Math.Floor(fz));
        return i * Nz + j;
    }

    public Vec2 ColumnCentre(int c) => new(Ox + ((c / Nz) + 0.5) * Cell, Oz + ((c % Nz) + 0.5) * Cell);
    public double ColumnSoilThickness(int c) => ColH[4 * c] + ColH[4 * c + 1] + ColH[4 * c + 2] + ColH[4 * c + 3];
    public double ColumnLayerThickness(int c, int layer) => ColH[4 * c + layer];
    public double ColumnSteepness(int c) => ColSteep[c];
    public bool IsOutcrop(int c) => ColumnSoilThickness(c) <= 1e-9;
    public double Height(Vec2 p)
    {
        double fx = (p.X - Ox) / Cell, fz = (p.Z - Oz) / Cell;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return double.NaN;
        Locate(fx, fz, out int i0, out int j0, out double tx, out double tz);
        int n00 = i0 * NodesZ + j0;
        return Bil(NSurf, n00, n00 + 1, n00 + NodesZ, n00 + NodesZ + 1, tx, tz);
    }

    /// Depth below the ground surface of the bottom of soil layer `level` (1 Litter, 2 Humus, 3 Mineral,
    /// 4 Weathered = soil bottom), bilinear over the nodes. Excludes buried stones. NaN outside the domain.
    public double HorizonBoundaryDepth(Vec2 p, int level)
    {
        double fx = (p.X - Ox) / Cell, fz = (p.Z - Oz) / Cell;
        if (level < 1 || level > 4 || !(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return double.NaN;
        Locate(fx, fz, out int i0, out int j0, out double tx, out double tz);
        int n00 = i0 * NodesZ + j0, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        int k = level - 1;
        return Bil(NB, 4 * n00 + k, 4 * n01 + k, 4 * n10 + k, 4 * n11 + k, tx, tz);
    }

    /// Cell index (clamped so the far edge maps to the last cell with t = 1) and fractional offsets.
    internal void Locate(double fx, double fz, out int i0, out int j0, out double tx, out double tz)
    {
        i0 = Math.Min(Nx - 1, Math.Max(0, (int)Math.Floor(fx)));
        j0 = Math.Min(Nz - 1, Math.Max(0, (int)Math.Floor(fz)));
        tx = fx - i0; tz = fz - j0;
    }

    internal static double Bil(float[] a, int n00, int n01, int n10, int n11, double tx, double tz) =>
        (1 - tx) * (1 - tz) * a[n00] + (1 - tx) * tz * a[n01] + tx * (1 - tz) * a[n10] + tx * tz * a[n11];
}
