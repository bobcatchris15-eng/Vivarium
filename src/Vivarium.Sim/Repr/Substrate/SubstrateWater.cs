using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Component fluxes of the last Step, in m3 over the step.
public readonly record struct WaterFluxes(double Rain, double Infiltration, double Percolation, double Evaporation,
    double Transpiration, double Drainage, double Runoff);

/// Conservative liquid water on the substrate columns. Every transfer subtracts from one store and adds the
/// same amount to another (or to an outflow accumulator); limiters only shrink a transfer, never create water.
public sealed class SubstrateWater
{
    private const double FilmCap = 0.0002;       // m, film is min(standing, 0.2 mm)
    private const double FilmTau = 600;          // s, film evaporation e-folding
    private const double LateralTau = 60;        // s, standing-water relaxation time
    private const double MaxSubstep = 120;       // s

    private readonly LayeredSubstrate _s;
    private readonly int _nc, _nz;
    private readonly double _a;
    internal readonly double[] Theta;            // 4 per column: volumetric water content per layer
    internal readonly double[] Standing;         // m per column (ponded)
    internal readonly double[] Film;             // m per column
    private readonly double[] _root, _rain, _e, _raw, _lastSat, _lastFilm;

    public bool DeepDrainageEnabled = true, EdgeRunoffEnabled = true, EvaporationEnabled = true, TranspirationEnabled = true;
    public WaterFluxes LastFluxes { get; private set; }

    private double _rainAcc, _infAcc, _percAcc, _evapAcc, _trAcc, _deepAcc, _runAcc;
    private double _simTime;

    internal SubstrateWater(LayeredSubstrate s)
    {
        _s = s; _nc = s.ColumnCount; _nz = s.Nz; _a = s.CellArea;
        Theta = new double[_nc * 4];
        Standing = new double[_nc];
        Film = new double[_nc];
        _root = new double[_nc * 4];
        _rain = new double[_nc];
        _e = new double[_nc];
        _raw = new double[_nc * 4];
        _lastSat = new double[_nc];
        _lastFilm = new double[_nc];
        for (int c = 0; c < _nc; c++)
        {
            for (int l = 0; l < 4; l++)
                if (s.ColumnLayerThickness(c, l) > 1e-9) Theta[4 * c + l] = 0.6 * s.ColFC[4 * c + l];
            _lastSat[c] = TopSaturation(c);
        }
    }

    public double Stored()
    {
        double sum = 0;
        for (int c = 0; c < _nc; c++)
        {
            double v = Standing[c] + Film[c];
            for (int l = 0; l < 4; l++) v += Theta[4 * c + l] * _s.ColH[4 * c + l];
            sum += v;
        }
        return sum * _a;
    }

    /// Saturation of the first soil layer in column c (0 if there is no soil).
    public double TopSaturation(int c)
    {
        int t = Top(c);
        if (t < 0) return 0;
        double phi = _s.ColPhi[4 * c + t];
        return phi > 1e-9 ? Theta[4 * c + t] / phi : 0;
    }

    private int Top(int c)
    {
        for (int l = 0; l < 4; l++) if (_s.ColH[4 * c + l] > 1e-9) return l;
        return -1;
    }

    private int Bottom(int c)
    {
        for (int l = 3; l >= 0; l--) if (_s.ColH[4 * c + l] > 1e-9) return l;
        return -1;
    }

    private int Next(int c, int l)
    {
        for (int m = l + 1; m < 4; m++) if (_s.ColH[4 * c + m] > 1e-9) return m;
        return -1;
    }

    /// Advances water by dt seconds. rainRate is m/s per ground point; evaporationDemand m/s;
    /// transpirationDemandPerRootMetre is m3 water per m of root per s.
    public Budget Step(double dt, Func<Vec2, double> rainRate, double evaporationDemand, double transpirationDemandPerRootMetre)
    {
        double before = Stored();
        if (!(dt > 0)) return new Budget("water_m3", before, before, 0, 0);
        _rainAcc = _infAcc = _percAcc = _evapAcc = _trAcc = _deepAcc = _runAcc = 0;

        for (int c = 0; c < _nc; c++)
        {
            double r = rainRate == null ? 0 : rainRate(_s.ColumnCentre(c));
            _rain[c] = double.IsFinite(r) && r > 0 ? r : 0;
        }
        RefreshRoots();

        double kmax = 0, hmin = double.MaxValue;
        for (int k = 0; k < _nc * 4; k++)
        {
            if (_s.ColH[k] <= 1e-6) continue;
            kmax = Math.Max(kmax, _s.ColK[k]);
            hmin = Math.Min(hmin, _s.ColH[k]);
        }
        double dtSub = MaxSubstep;
        // CFL-style limit from max Ksat and min layer thickness (thickness floored at 2 cm to bound cost; limiters keep
        // every transfer non-negative regardless of the step size).
        if (kmax > 0 && hmin < double.MaxValue) dtSub = Math.Min(MaxSubstep, Math.Max(1, 0.5 * Math.Max(hmin, 0.02) / kmax));
        int n = Math.Max(1, (int)Math.Ceiling(dt / dtSub - 1e-12));
        double ds = dt / n;
        for (int k = 0; k < n; k++) SubStep(ds, evaporationDemand, transpirationDemandPerRootMetre);
        _simTime += dt;

        double after = Stored();
        double rain = _rainAcc * _a, inf = _infAcc * _a, perc = _percAcc * _a, evap = _evapAcc * _a;
        double tr = _trAcc * _a, deep = _deepAcc * _a, run = _runAcc * _a;
        LastFluxes = new WaterFluxes(rain, inf, perc, evap, tr, deep, run);
        Publish();
        return new Budget("water_m3", before, after, rain, evap + tr + deep + run);
    }

    private void RefreshRoots()
    {
        for (int c = 0; c < _nc; c++)
        {
            int ci = c / _nz, cj = c % _nz;
            int nodesZ = _nz + 1;
            int n00 = ci * nodesZ + cj, n01 = n00 + 1, n10 = n00 + nodesZ, n11 = n10 + 1;
            for (int l = 0; l < 4; l++)
            {
                int band = l < 2 ? 0 : (l == 2 ? 1 : 2);
                _root[4 * c + l] = 0.25 * (_s.NRoot[n00 * 3 + band] + _s.NRoot[n01 * 3 + band]
                                         + _s.NRoot[n10 * 3 + band] + _s.NRoot[n11 * 3 + band]);
            }
        }
    }

    private void SubStep(double ds, double evapD, double trD)
    {
        // 1. rain onto the surface
        for (int c = 0; c < _nc; c++)
        {
            double r = _rain[c] * ds;
            Standing[c] += r;
            _rainAcc += r;
        }
        // 2. film tracks recent wetting: moved out of standing water (conserved)
        for (int c = 0; c < _nc; c++)
        {
            double target = Math.Min(Standing[c], FilmCap);
            if (Film[c] < target)
            {
                double dF = target - Film[c];
                Standing[c] -= dF;
                Film[c] += dF;
            }
        }
        // 3. infiltration from standing water into the top layer
        for (int c = 0; c < _nc; c++)
        {
            int t = Top(c);
            if (t < 0 || Standing[c] <= 0) continue;
            int k = 4 * c + t;
            double h = _s.ColH[k], th = Theta[k], phi = _s.ColPhi[k];
            double cap = _s.ColK[k] * ds * (1 + Standing[c] / h);
            double free = Math.Max(0, (phi - th) * h);
            double q = Math.Min(Standing[c], Math.Min(cap, free));
            if (q <= 0) continue;
            Standing[c] -= q;
            Theta[k] += q / h;
            _infAcc += q;
        }
        // 4. percolation of water above field capacity, layer to layer
        for (int c = 0; c < _nc; c++)
        {
            for (int l = 0; l < 3; l++)
            {
                if (_s.ColH[4 * c + l] <= 1e-9) continue;
                int m = Next(c, l);
                if (m < 0) break;
                int kl = 4 * c + l, km = 4 * c + m;
                double hl = _s.ColH[kl], hm = _s.ColH[km];
                double excess = Math.Max(0, Theta[kl] - _s.ColFC[kl]) * hl;
                double cap = _s.ColK[km] * ds;
                double free = Math.Max(0, (_s.ColPhi[km] - Theta[km]) * hm);
                double q = Math.Min(excess, Math.Min(cap, free));
                if (q <= 0) continue;
                Theta[kl] -= q / hl;
                Theta[km] += q / hm;
                _percAcc += q;
            }
        }
        // 5. drainage out of the lowest soil layer into deep drainage
        for (int c = 0; c < _nc; c++)
        {
            int L = Bottom(c);
            if (L < 0) continue;
            int k = 4 * c + L;
            double h = _s.ColH[k];
            double excess = Math.Max(0, Theta[k] - _s.ColFC[k]) * h;
            double q = Math.Min(excess, _s.ColK[k] * ds);
            if (q <= 0 || !DeepDrainageEnabled) continue;
            Theta[k] -= q / h;
            _deepAcc += q;
        }
        // 6. surface flow of standing water (diffusion-wave, downslope, out at the domain edge)
        LateralStanding(ds);
        // 7. saturated subsurface flow along the head gradient
        LateralSubsurface(ds);
        // 8. evaporation: standing water, film, then the top layer above wilting point / 2
        if (EvaporationEnabled)
            for (int c = 0; c < _nc; c++)
            {
                double rem = evapD * ds;
                double es = Math.Min(Standing[c], rem);
                Standing[c] -= es; _evapAcc += es; rem -= es;
                double fl = Film[c] * (1 - Math.Exp(-ds / FilmTau));
                Film[c] -= fl; _evapAcc += fl;
                int t = Top(c);
                if (t < 0 || rem <= 0) continue;
                int k = 4 * c + t;
                double h = _s.ColH[k], th = Theta[k];
                double lo = 0.5 * _s.ColWP[k];
                double frac = _s.ColFC[k] > lo ? MathD.Clamp01((th - lo) / (_s.ColFC[k] - lo)) : 0;
                double q = Math.Min(rem * frac, Math.Max(0, th - lo) * h);
                if (q <= 0) continue;
                Theta[k] -= q / h;
                _evapAcc += q;
            }
        // 9. root uptake per layer, limited to water above wilting point
        if (TranspirationEnabled && trD > 0)
            for (int c = 0; c < _nc; c++)
                for (int l = 0; l < 4; l++)
                {
                    int k = 4 * c + l;
                    double h = _s.ColH[k];
                    if (h <= 1e-9 || _root[k] <= 0) continue;
                    double demand = trD * _root[k] * h * ds;
                    double avail = Math.Max(0, Theta[k] - _s.ColWP[k]) * h;
                    double u = Math.Min(demand, avail);
                    if (u <= 0) continue;
                    Theta[k] -= u / h;
                    _trAcc += u;
                }
    }

    private void LateralStanding(double ds)
    {
        double f = Math.Min(0.5, ds / LateralTau);
        int nx = _nc / _nz;
        for (int c = 0; c < _nc; c++) _e[c] = _s.ColSurf[c] + Standing[c];
        // direction 0 +X, 1 -X, 2 +Z, 3 -Z
        for (int c = 0; c < _nc; c++)
        {
            int i = c / _nz, j = c % _nz;
            for (int d = 0; d < 4; d++)
            {
                int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                double raw = 0;
                if (ni >= 0 && ni < nx && nj >= 0 && nj < _nz)
                {
                    int nb = ni * _nz + nj;
                    raw = Math.Max(0, 0.5 * (_e[c] - _e[nb]) * f);
                }
                else if (EdgeRunoffEnabled)
                {
                    // outside the domain: extrapolate the surface linearly from the inner neighbour
                    int ii = i - (d == 0 ? 1 : d == 1 ? -1 : 0), jj = j - (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ii >= 0 && ii < nx && jj >= 0 && jj < _nz)
                    {
                        double surfOut = 2 * _s.ColSurf[c] - _s.ColSurf[ii * _nz + jj];
                        raw = Math.Max(0, 0.5 * (_e[c] - surfOut) * f);
                    }
                }
                _raw[4 * c + d] = raw;
            }
        }
        for (int c = 0; c < _nc; c++)
        {
            double sum = _raw[4 * c] + _raw[4 * c + 1] + _raw[4 * c + 2] + _raw[4 * c + 3];
            if (sum <= 0) continue;
            double alpha = Math.Min(1, Standing[c] / sum);
            int i = c / _nz, j = c % _nz;
            for (int d = 0; d < 4; d++)
            {
                double q = _raw[4 * c + d] * alpha;
                if (q <= 0) continue;
                if (q > Standing[c]) q = Standing[c];
                int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                Standing[c] -= q;
                if (ni >= 0 && ni < nx && nj >= 0 && nj < _nz) Standing[ni * _nz + nj] += q;
                else _runAcc += q;
            }
        }
    }

    private void LateralSubsurface(double ds)
    {
        int nx = _nc / _nz;
        for (int c = 0; c < _nc; c++)
        {
            int i = c / _nz, j = c % _nz;
            for (int d = 0; d < 2; d++)                  // +X and +Z neighbours (each pair once)
            {
                int ni = i + (d == 0 ? 1 : 0), nj = j + (d == 1 ? 1 : 0);
                if (ni >= nx || nj >= _nz) continue;
                int nb = ni * _nz + nj;
                double dS = _s.ColSurf[c] - _s.ColSurf[nb];
                if (dS == 0) continue;
                int src = dS > 0 ? c : nb, dst = dS > 0 ? nb : c;
                double g = Math.Abs(dS) / _s.Cell;
                for (int l = 0; l < 4; l++)
                {
                    int ks = 4 * src + l, kd = 4 * dst + l;
                    double hs = _s.ColH[ks], hd = _s.ColH[kd];
                    if (hs <= 1e-9 || hd <= 1e-9) continue;
                    double k = Math.Min(_s.ColK[ks], _s.ColK[kd]);
                    double excess = Math.Max(0, Theta[ks] - _s.ColFC[ks]) * hs;
                    double free = Math.Max(0, (_s.ColPhi[kd] - Theta[kd]) * hd);
                    double q = Math.Min(k * g * ds, Math.Min(excess, free));
                    if (q <= 0) continue;
                    Theta[ks] -= q / hs;
                    Theta[kd] += q / hd;
                }
            }
        }
    }

    private void Publish()
    {
        double yMin = double.MaxValue, yMax = double.MinValue;
        int iMin = int.MaxValue, iMax = int.MinValue, jMin = int.MaxValue, jMax = int.MinValue;
        bool any = false, up = false;
        for (int c = 0; c < _nc; c++)
        {
            double sat = TopSaturation(c);
            double dSat = sat - _lastSat[c];
            double dFilm = Film[c] - _lastFilm[c];
            if (Math.Abs(dSat) <= 0.05 && Math.Abs(dFilm) <= 0.05 * FilmCap) continue;
            any = true;
            if (dSat > 0) up = true;
            _lastSat[c] = sat;
            _lastFilm[c] = Film[c];
            int i = c / _nz, j = c % _nz;
            iMin = Math.Min(iMin, i); iMax = Math.Max(iMax, i);
            jMin = Math.Min(jMin, j); jMax = Math.Max(jMax, j);
            yMin = Math.Min(yMin, _s.ColSurf[c]);
            yMax = Math.Max(yMax, _s.ColSurf[c] + Standing[c] + 0.01);
        }
        if (!any) return;
        var box = new Aabb(
            new Vec3(_s.Ox + iMin * _s.Cell, yMin - 0.01, _s.Oz + jMin * _s.Cell),
            new Vec3(_s.Ox + (iMax + 1) * _s.Cell, yMax, _s.Oz + (jMax + 1) * _s.Cell));
        _s.DirtyLog.Publish(new DirtyRecord(box, DirtyDomain.Water, _s.Version, up ? "wet" : "dry", _simTime));
    }
}
