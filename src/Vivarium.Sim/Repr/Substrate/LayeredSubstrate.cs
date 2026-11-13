using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Core layered substrate.
///
/// GEOMETRY IS CONTINUOUS. The soil surface S(x,z) is bilinear over NODES (Nx+1 by Nz+1, spacing Cell) of the spec's
/// smooth surface function. Bedrock B(x,z) is an analytic function (bedding plane, low relief, a broad domain-warped
/// knoll with secondary knobs, ridged multi-octave relief, 2-8 cm bedding ledges) tabulated on a FINE lattice (about
/// 1 cm) and interpolated bilinearly, so it is continuous at any sample spacing. The ground surface is
/// max(S, B). Soil thickness at any point is T = min(Depth, max(0, S - B) * (1 - steep/2)), a feather edge against
/// the rock; horizon boundaries are T times per-node cumulative fractions (bilinear), so every boundary is continuous.
/// COLUMNS (Nx by Nz cells) are only the storage for water and a mean-composition view; their layer thicknesses are
/// quadrature means of the continuous profile.
/// EXCAVATIONS are an explicit list of spheres evaluated analytically: Height = min(undisturbed, lower sphere
/// surface), and the material exposed on a bowl is whatever the UNDISTURBED profile holds at that depth.
public sealed partial class LayeredSubstrate : ISubstrate
{
    internal const int Layers = 4;
    internal const int Hc = 5;            // horizon tables: Litter, Humus, Mineral, Weathered, Bedrock
    internal const int TabStride = 16;    // composition fields per node per horizon
    internal const int F_CF = 0, F_SAND = 1, F_SILT = 2, F_CLAY = 3, F_ORG = 4, F_PHI = 5, F_AGG = 6,
        F_COMP = 7, F_STR = 8, F_FC = 9, F_WP = 10, F_LOGK = 11, F_COL = 12, F_HR = 13, F_HG = 14, F_HB = 15;
    internal const int NQ = 6;            // quadrature points per axis per column

    private readonly SubstrateSpec _spec;
    internal readonly ulong Seed;
    internal readonly int Nx, Nz, NodesZ, NodeCount, ColumnCount;
    internal readonly double Ox, Oz, Cell, CellArea;
    private readonly double _cx, _cz, _y0, _nxp, _nzp, _knX, _knZ;
    private readonly ulong _ridgeSeed, _resSeed, _warpSeed, _knobSeed, _ledgeSeed, _reliefSeed;
    private readonly double _knSigma, _knAmp, _ledgeSp, _dirX, _dirZ;
    private readonly double[] _kbx = new double[3], _kbz = new double[3], _kbs = new double[3], _kba = new double[3];
    internal readonly Vec3 BedN, ParentHue;
    private readonly Aabb _bounds;
    private ulong _version;

    // node arrays
    internal readonly float[] NSurf;      // m, top of the SOIL surface (spec) at nodes
    internal readonly float[] NSteep;     // 0..1 slope steepness
    internal readonly float[] NFrac;      // 3 per node: cumulative fraction of soil thickness at the bottoms of Litter, Humus, Mineral
    internal readonly float[] NTab;       // per node per horizon composition table (Hc * TabStride per node)
    internal readonly float[] NRoot;      // 3 per node: fine-root density (m/m3) for organic, mineral, weathered bands

    // fine bedrock lattice
    internal readonly int Fl, Lnz;
    internal readonly float[] BL;         // bedrock top height at lattice points ((Nx*Fl+1) x (Nz*Fl+1))

    // column arrays
    internal readonly double[] ColH;      // 4 per column: mean remaining layer thickness (m)
    internal readonly double[] ColK;      // 4 per column: saturated conductivity (m/s)
    internal readonly float[] ColPhi, ColFC, ColWP; // 4 per column
    internal readonly double[] ColSurf;   // mean current ground surface of the column
    internal readonly double[] ColSteep;
    internal readonly double[] ColStoneSurf, ColStoneDeep; // per stone-cell probabilities for the column

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
        double nl = Math.Sqrt(_nxp * _nxp + _nzp * _nzp);
        _dirX = nl > 1e-9 ? _nxp / nl : 1; _dirZ = nl > 1e-9 ? _nzp / nl : 0;   // down-dip horizontal direction
        _knX = _cx + ReprId.Range(h, 3, -0.12, 0.12) * Nx * Cell;
        _knZ = _cz + ReprId.Range(h, 4, -0.12, 0.12) * Nz * Cell;
        _knSigma = ReprId.Range(h, 8, 0.100, 0.105) * Math.Min(1.0, Math.Min(Nx, Nz) * Cell);
        _knAmp = 0.55;
        _y0 = spec.SurfaceHeight(new Vec2(_cx, _cz)) - 0.35;      // bedding plane anchored 35 cm under mid-surface
        _ridgeSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-ridge"));
        _resSeed = Rng.Mix(Seed, Hash.Fnv1a64("soil-residual"));
        _warpSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-warp"));
        _knobSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-knob"));
        _ledgeSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-ledge"));
        _reliefSeed = Rng.Mix(Seed, Hash.Fnv1a64("bedrock-relief"));
        _ledgeSp = ReprId.Range(h, 9, 0.22, 0.34);
        for (int k = 0; k < 3; k++)
        {
            double ang = ReprId.Unit(h, (uint)(20 + k)) * 2 * Math.PI, dist = ReprId.Range(h, (uint)(30 + k), 0.05, 0.09);
            _kbx[k] = _knX + dist * Math.Cos(ang); _kbz[k] = _knZ + dist * Math.Sin(ang);
            _kbs[k] = ReprId.Range(h, (uint)(40 + k), 0.05, 0.07);
            _kba[k] = ReprId.Range(h, (uint)(50 + k), 0.06, 0.10);
        }
        ParentHue = new Vec3(0.50 + 0.10 * ReprId.Unit(h, 5) - 0.05,
                             0.44 + 0.08 * ReprId.Unit(h, 6) - 0.04,
                             0.36 + 0.08 * ReprId.Unit(h, 7) - 0.04);

        NSurf = new float[NodeCount];
        NSteep = new float[NodeCount];
        NFrac = new float[NodeCount * 3];
        NTab = new float[NodeCount * Hc * TabStride];
        NRoot = new float[NodeCount * 3];
        ColH = new double[ColumnCount * 4];
        ColK = new double[ColumnCount * 4];
        ColPhi = new float[ColumnCount * 4];
        ColFC = new float[ColumnCount * 4];
        ColWP = new float[ColumnCount * 4];
        ColSurf = new double[ColumnCount];
        ColSteep = new double[ColumnCount];
        ColStoneSurf = new double[ColumnCount];
        ColStoneDeep = new double[ColumnCount];
        _excAt = new List<int>?[ColumnCount];

        // fine lattice: about 1 cm, capped in memory (falls back toward the node spacing on very large domains)
        int fl = Math.Max(1, (int)Math.Ceiling(Cell / 0.01 - 1e-9));
        while (fl > 1 && (long)(Nx * fl + 1) * (Nz * fl + 1) > 12_000_000) fl--;
        Fl = fl; Lnz = Nz * Fl + 1;
        BL = new float[(Nx * Fl + 1) * Lnz];
        double lc = Cell / Fl;
        double smax = double.MinValue;
        for (int i = 0; i <= Nx * Fl; i++)
            for (int j = 0; j <= Nz * Fl; j++)
            {
                double x = Ox + i * lc, z = Oz + j * lc;
                double b = BedrockAnalytic(x, z, spec.SurfaceHeight(new Vec2(x, z)));
                BL[i * Lnz + j] = (float)b;
                smax = Math.Max(smax, b);
            }

        double smin = double.MaxValue;
        for (int i = 0; i <= Nx; i++)
            for (int j = 0; j <= Nz; j++)
                BuildNode(i * NodesZ + j, Ox + i * Cell, Oz + j * Cell);
        for (int n = 0; n < NodeCount; n++) { smin = Math.Min(smin, NSurf[n]); smax = Math.Max(smax, NSurf[n]); }
        _bounds = new Aabb(new Vec3(Ox, smin - spec.Depth, Oz), new Vec3(Ox + Nx * Cell, smax + 0.25, Oz + Nz * Cell));

        for (int c = 0; c < ColumnCount; c++) RefreshColumn(c, true);
        Water = new SubstrateWater(this);
    }

    // ------------------------------------------------------------ analytic bedrock

    private static double Ridge(ulong seed, double x, double z, double lambda) =>
        1 - 2 * Math.Abs(Noise.Gradient(seed, x / lambda, z / lambda));

    /// Low-frequency part: bedding plane, gentle relief, the broad warped knoll and its secondary knobs.
    private double BedrockLow(double x, double z)
    {
        double dx = x - _cx, dz = z - _cz;
        double plane = _y0 - _nxp * dx - _nzp * dz;
        double relief = 0.04 * Noise.Fbm(_reliefSeed, x * 1.0, z * 1.0, 2);
        // knoll: domain-warped, elongated along strike (perpendicular to the dip direction)
        double wx = 0.035 * Noise.Gradient(_warpSeed, x * 2.5, z * 2.5);
        double wz = 0.035 * Noise.Gradient(Rng.Mix(_warpSeed, 1), x * 2.5, z * 2.5);
        double kx = x + wx - _knX, kz = z + wz - _knZ;
        double along = kx * _dirX + kz * _dirZ, across = -kx * _dirZ + kz * _dirX;   // dip / strike coordinates
        double q = (along * along) / (_knSigma * _knSigma) + (across * across) / (1.15 * _knSigma * 1.15 * _knSigma);
        double knoll = _knAmp * Math.Exp(-0.5 * q);
        double knobs = 0;
        for (int k = 0; k < 3; k++)
        {
            double bx = x + wx - _kbx[k], bz = z + wz - _kbz[k];
            knobs += _kba[k] * Math.Exp(-(bx * bx + bz * bz) / (2 * _kbs[k] * _kbs[k]));
        }
        return plane + relief + knoll + knobs;
    }

    /// Bedding-plane ledge profile as a function of the down-dip strata coordinate t (spacing units): a riser of
    /// 2-8 cm (hashed per ledge) followed by a gentle tread, zero-mean. Continuous in t.
    internal double LedgeProfile(double t)
    {
        double fl = Math.Floor(t), f = t - fl;
        ulong hh = Rng.Mix(_ledgeSeed, (ulong)(long)fl);
        double hgt = 0.02 + 0.06 * ReprId.Unit(hh, 1);
        double rw = Math.Min(0.9, 1.6 * hgt / _ledgeSp);
        return hgt * (MathD.SmoothStep(0, rw, f) - f);
    }

    /// Strata coordinate used for ledges; warped along strike so ledges are irregular, not straight lines.
    internal double LedgeCoord(double x, double z)
    {
        double dx = x - _cx, dz = z - _cz;
        double u = dx * _dirX + dz * _dirZ;
        double warp = 0.07 * Noise.Gradient(_ledgeSeed, x * 2.0, z * 2.0) + 0.012 * Noise.Gradient(Rng.Mix(_ledgeSeed, 7), x * 7.0, z * 7.0);
        return (u + warp) / _ledgeSp;
    }

    /// Ledge offset at a ground point (m): the same profile the mesher repeats on cut faces.
    internal double LedgeAt(double x, double z) => LedgeProfile(LedgeCoord(x, z)) - 0.02;

    /// Full bedrock top at (x,z). sSoil is the soil surface there; relief detail fades where bedrock is deep
    /// (weathering smooths buried relief) so deep soils do not carry rock texture.
    private double BedrockAnalytic(double x, double z, double sSoil)
    {
        double low = BedrockLow(x, z);
        double w = 1 - MathD.SmoothStep(0.12, 0.40, sSoil - low);
        if (w <= 0) return low;
        double ridged = 0.5 * Ridge(_ridgeSeed, x, z, 0.35) + 0.3 * Ridge(Rng.Mix(_ridgeSeed, 1), x, z, 0.15)
                        + 0.2 * Ridge(Rng.Mix(_ridgeSeed, 2), x, z, 0.07);
        double detail = 0.015 * (ridged - 0.45);
        double knob = 0.03 * Math.Max(0, Noise.Gradient(_knobSeed, x / 0.10, z / 0.10) - 0.15);
        double ledge = LedgeAt(x, z);
        return low + w * (detail + knob + ledge);
    }

    // ------------------------------------------------------------ nodes and columns

    private void BuildNode(int n, double x, double z)
    {
        var p = new Vec2(x, z);
        double s = _spec.SurfaceHeight(p);
        int ni = n / NodesZ, nj = n % NodesZ;
        double b = BL[(ni * Fl) * Lnz + nj * Fl];
        double dd = Math.Max(0, s - b);                           // depth to bedrock
        double gx = (_spec.SurfaceHeight(new Vec2(x + Cell, z)) - _spec.SurfaceHeight(new Vec2(x - Cell, z))) / (2 * Cell);
        double gz = (_spec.SurfaceHeight(new Vec2(x, z + Cell)) - _spec.SurfaceHeight(new Vec2(x, z - Cell))) / (2 * Cell);
        double steep = MathD.Clamp01(Math.Sqrt(gx * gx + gz * gz) / 0.5);
        double drain = MathD.Clamp01(_spec.DrainagePotential(p));
        double org = MathD.Clamp01(_spec.OrganicInput(p));
        double wet = 1 - drain;
        double res = Noise.Fbm(_resSeed, x * 0.8, z * 0.8, 2);

        double l1 = MathD.Clamp(0.005 + 0.02 * org * (1 - 0.6 * steep), 0, 0.03);
        double l2 = MathD.Clamp(0.01 + 0.10 * org * (0.5 + 0.5 * wet) * (1 - 0.5 * steep), 0.01, 0.12);
        double l3 = MathD.Clamp((0.05 + 0.40 * (1 - steep) * (0.5 + 0.5 * wet)) * (1 + 0.15 * res), 0.05, 0.45)
                    * (0.2 + 0.8 * MathD.Clamp01(dd / 0.2));
        double l4 = MathD.Clamp(0.05 + 0.25 * (1 - 0.5 * steep), 0.05, 0.30);
        double sum = l1 + l2 + l3 + l4;

        NSurf[n] = (float)s;
        NSteep[n] = (float)steep;
        NFrac[3 * n] = (float)(l1 / sum); NFrac[3 * n + 1] = (float)((l1 + l2) / sum); NFrac[3 * n + 2] = (float)((l1 + l2 + l3) / sum);
        for (int hh = 0; hh < Hc; hh++)
            Compose(hh, drain, org, steep, ParentHue, NTab, (n * Hc + hh) * TabStride);
    }

    /// Recomputes column storage that does not depend on excavations (conductivity, porosity ...) and the
    /// quadrature view. `init` also fills the stone probabilities.
    internal void RefreshColumn(int c, bool init)
    {
        int ci = c / Nz, cj = c % Nz;
        int n00 = ci * NodesZ + cj, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        if (init)
        {
            ColSteep[c] = 0.25 * (NSteep[n00] + NSteep[n01] + NSteep[n10] + NSteep[n11]);
            for (int l = 0; l < Layers; l++)
            {
                double phi = 0, fc = 0, wp = 0, lk = 0;
                for (int q = 0; q < 4; q++)
                {
                    int n = q == 0 ? n00 : q == 1 ? n01 : q == 2 ? n10 : n11;
                    int a = (n * Hc + l) * TabStride;
                    phi += NTab[a + F_PHI]; fc += NTab[a + F_FC]; wp += NTab[a + F_WP]; lk += NTab[a + F_LOGK];
                }
                ColPhi[4 * c + l] = (float)(0.25 * phi);
                ColFC[4 * c + l] = (float)(0.25 * fc);
                ColWP[4 * c + l] = (float)(0.25 * wp);
                ColK[4 * c + l] = Math.Pow(10, 0.25 * lk);
            }
        }
        var hh = new double[4];
        QuadColumn(c, hh, out double surf);
        for (int l = 0; l < 4; l++) ColH[4 * c + l] = hh[l];
        ColSurf[c] = surf;
        if (init) StoneProbabilities(c, n00, n01, n10, n11);
    }

    /// Quadrature (NQ x NQ midpoints) of the current state of one column: mean remaining thickness per soil
    /// layer (below the CURRENT surface, in the undisturbed profile) and mean current surface height.
    internal void QuadColumn(int c, double[] h, out double surf)
    {
        int ci = c / Nz, cj = c % Nz;
        h[0] = h[1] = h[2] = h[3] = 0; surf = 0;
        for (int a = 0; a < NQ; a++)
        {
            double tx = (a + 0.5) / NQ;
            for (int b = 0; b < NQ; b++)
            {
                double tz = (b + 0.5) / NQ;
                double und = Profile(ci, cj, tx, tz, out double T, out double f1, out double f2, out double f3);
                double x = Ox + (ci + tx) * Cell, z = Oz + (cj + tz) * Cell;
                double cur = CurHeight(c, x, z, und);
                double sd = und - cur;
                surf += cur;
                if (T <= 0) continue;
                double d1 = T * f1, d2 = T * f2, d3 = T * f3;
                h[0] += Math.Max(0, d1 - sd);
                h[1] += Math.Max(0, d2 - Math.Max(d1, sd));
                h[2] += Math.Max(0, d3 - Math.Max(d2, sd));
                h[3] += Math.Max(0, T - Math.Max(d3, sd));
            }
        }
        double inv = 1.0 / (NQ * NQ);
        for (int l = 0; l < 4; l++) h[l] *= inv;
        surf *= inv;
    }

    // ------------------------------------------------------------ continuous profile

    /// Undisturbed ground height and soil profile at a point inside cell (i0,j0) with offsets (tx,tz) in [0,1].
    internal double Profile(int i0, int j0, double tx, double tz, out double T, out double f1, out double f2, out double f3)
    {
        int n00 = i0 * NodesZ + j0, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        double w00 = (1 - tx) * (1 - tz), w01 = (1 - tx) * tz, w10 = tx * (1 - tz), w11 = tx * tz;
        double S = w00 * NSurf[n00] + w01 * NSurf[n01] + w10 * NSurf[n10] + w11 * NSurf[n11];
        double B = LatticeB(i0, j0, tx, tz);
        double g = S - B;
        if (g <= 0) { T = 0; f1 = f2 = f3 = 0; return B; }
        double steep = w00 * NSteep[n00] + w01 * NSteep[n01] + w10 * NSteep[n10] + w11 * NSteep[n11];
        T = Math.Min(_spec.Depth, g * (1 - 0.5 * steep));
        int m00 = 3 * n00, m01 = 3 * n01, m10 = 3 * n10, m11 = 3 * n11;
        f1 = w00 * NFrac[m00] + w01 * NFrac[m01] + w10 * NFrac[m10] + w11 * NFrac[m11];
        f2 = w00 * NFrac[m00 + 1] + w01 * NFrac[m01 + 1] + w10 * NFrac[m10 + 1] + w11 * NFrac[m11 + 1];
        f3 = w00 * NFrac[m00 + 2] + w01 * NFrac[m01 + 2] + w10 * NFrac[m10 + 2] + w11 * NFrac[m11 + 2];
        return S;
    }

    /// Bedrock top (bilinear over the fine lattice) inside cell (i0,j0) at offsets (tx,tz).
    internal double LatticeB(int i0, int j0, double tx, double tz)
    {
        double lx = tx * Fl, lz = tz * Fl;
        int li = Math.Min(Fl - 1, (int)lx), lj = Math.Min(Fl - 1, (int)lz);
        double a = lx - li, b = lz - lj;
        int n = (i0 * Fl + li) * Lnz + j0 * Fl + lj;
        return (1 - a) * (1 - b) * BL[n] + (1 - a) * b * BL[n + 1] + a * (1 - b) * BL[n + Lnz] + a * b * BL[n + Lnz + 1];
    }

    private bool Inside(double x, double z, out int i0, out int j0, out double tx, out double tz)
    {
        double fx = (x - Ox) / Cell, fz = (z - Oz) / Cell;
        i0 = j0 = 0; tx = tz = 0;
        if (!(fx >= 0 && fx <= Nx && fz >= 0 && fz <= Nz)) return false;
        Locate(fx, fz, out i0, out j0, out tx, out tz);
        return true;
    }

    /// Undisturbed ground height max(S, B); NaN outside.
    public double UndisturbedHeight(Vec2 p)
    {
        if (!Inside(p.X, p.Z, out int i0, out int j0, out double tx, out double tz)) return double.NaN;
        return Profile(i0, j0, tx, tz, out _, out _, out _, out _);
    }

    /// Current ground height (excavations applied); NaN outside the domain.
    public double Height(Vec2 p)
    {
        if (!Inside(p.X, p.Z, out int i0, out int j0, out double tx, out double tz)) return double.NaN;
        double und = Profile(i0, j0, tx, tz, out _, out _, out _, out _);
        return CurHeight(i0 * Nz + j0, p.X, p.Z, und);
    }

    /// Soil thickness below the undisturbed surface at p (0 on bare rock); NaN outside the domain. Continuous.
    public double SoilThicknessAt(Vec2 p)
    {
        if (!Inside(p.X, p.Z, out int i0, out int j0, out double tx, out double tz)) return double.NaN;
        Profile(i0, j0, tx, tz, out double T, out _, out _, out _);
        return T;
    }

    /// Signed soil-minus-rock contact function g = S - B (soil where positive). The soil/rock contact is its zero set.
    public double ContactFunction(Vec2 p)
    {
        if (!Inside(p.X, p.Z, out int i0, out int j0, out double tx, out double tz)) return double.NaN;
        int n00 = i0 * NodesZ + j0, n01 = n00 + 1, n10 = n00 + NodesZ, n11 = n10 + 1;
        double w00 = (1 - tx) * (1 - tz), w01 = (1 - tx) * tz, w10 = tx * (1 - tz), w11 = tx * tz;
        double S = w00 * NSurf[n00] + w01 * NSurf[n01] + w10 * NSurf[n10] + w11 * NSurf[n11];
        return S - LatticeB(i0, j0, tx, tz);
    }

    /// True where no soil covers the rock at this point. Continuous in space (the contact line is a smooth curve).
    public bool IsOutcropAt(Vec2 p) => ContactFunction(p) <= 0;

    /// Depth below the undisturbed ground surface of the bottom of soil layer `level` (1 Litter, 2 Humus,
    /// 3 Mineral, 4 Weathered = soil bottom). Continuous. NaN outside the domain.
    public double HorizonBoundaryDepth(Vec2 p, int level)
    {
        if (level < 1 || level > 4 || !Inside(p.X, p.Z, out int i0, out int j0, out double tx, out double tz)) return double.NaN;
        Profile(i0, j0, tx, tz, out double T, out double f1, out double f2, out double f3);
        return level == 1 ? T * f1 : level == 2 ? T * f2 : level == 3 ? T * f3 : T;
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
    /// True for a column with no soil at all (bare rock across the whole column).
    public bool IsOutcrop(int c) => ColumnSoilThickness(c) <= 1e-9;

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
