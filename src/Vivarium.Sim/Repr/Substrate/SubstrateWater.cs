using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Component fluxes of the last Step, in m3 over the step.
public readonly record struct WaterFluxes(double Rain, double Infiltration, double Percolation, double Evaporation,
    double Transpiration, double Drainage, double Runoff);

/// Conservative liquid water on the substrate columns. Every transfer subtracts from one store and adds the
/// same amount to another (or to an outflow accumulator); limiters only shrink a transfer, never create water.
///
/// Soil water is stored per layer (volumetric Theta). Water above field capacity is MOBILE: it percolates down layer by
/// layer. The bottom of the soil is the bedrock contact: deep drainage through it is limited by the BEDROCK
/// conductivity (about 1e-9 m/s, plus an optional fracture leakage), so water that reaches the bottom accumulates
/// there as a PERCHED SATURATED ZONE (thickness = stacked mobile water over specific yield). That zone flows laterally
/// along the bedrock surface gradient by Darcy flux = Ksat * saturated thickness * head gradient, in conservative
/// finite volume (flux through the domain boundary on the downslope side is "subsurface outflow" and can be closed).
/// When a column's profile is full to the surface, water that cannot be stored exfiltrates as standing water, which
/// then flows over the surface to the lowest hollow and ponds.
public sealed class SubstrateWater
{
    private const double FilmCap = 0.0002;       // m, film is min(standing, 0.2 mm)
    private const double FilmTau = 600;          // s, film evaporation e-folding
    private const double LateralTau = 60;        // s, standing-water relaxation time
    private const double MaxSubstep = 120;       // s (soil)
    private const double SurfaceSubstep = 60;    // s (rain, film, infiltration, evaporation)
    private const double BedrockK = 1e-9;        // m/s, saturated conductivity of intact bedrock

    private readonly LayeredSubstrate _s;
    private readonly int _nc, _nz, _nx;
    private readonly double _a, _cell;
    internal readonly double[] Theta;            // 4 per column: volumetric water content per layer
    internal readonly double[] Standing;         // m per column (ponded)
    internal readonly double[] Film;             // m per column
    private readonly double[] _root, _rain, _e, _raw, _lastSat, _lastFilm;
    private readonly double[] _zb, _head, _kef, _mob, _lat, _sy;   // bedrock-contact elevation, water-table head, K, mobile water, lateral raw flows
    private readonly sbyte[] _top, _bot;
    private readonly sbyte[] _nxt;

    public bool DeepDrainageEnabled = true, EdgeRunoffEnabled = true, EvaporationEnabled = true, TranspirationEnabled = true;
    /// Flux through the domain boundary on the downslope side of the perched water table. Close it to keep that water in.
    public bool SubsurfaceOutflowEnabled = true;
    /// Extra deep-drainage conductivity as a multiple of the bottom soil layer's Ksat (fractured bedrock). Default 0.
    public double FractureLeakage = 0;
    public WaterFluxes LastFluxes { get; private set; }
    /// m3 that left through the domain boundary as subsurface (perched water table) flow during the last Step.
    public double LastSubsurfaceOutflow { get; private set; }

    private double _rainAcc, _infAcc, _percAcc, _evapAcc, _trAcc, _deepAcc, _runAcc, _subAcc;
    private double _simTime;
    private bool _anyStanding;

    internal SubstrateWater(LayeredSubstrate s)
    {
        _s = s; _nc = s.ColumnCount; _nz = s.Nz; _nx = s.Nx; _a = s.CellArea; _cell = s.Cell;
        Theta = new double[_nc * 4];
        Standing = new double[_nc];
        Film = new double[_nc];
        _root = new double[_nc * 4];
        _rain = new double[_nc];
        _e = new double[_nc];
        _raw = new double[_nc * 4];
        _lat = new double[_nc * 4];
        _lastSat = new double[_nc];
        _lastFilm = new double[_nc];
        _zb = new double[_nc]; _sy = new double[_nc]; _head = new double[_nc]; _kef = new double[_nc]; _mob = new double[_nc];
        _top = new sbyte[_nc]; _bot = new sbyte[_nc]; _nxt = new sbyte[_nc * 4];
        for (int c = 0; c < _nc; c++)
        {
            for (int l = 0; l < 4; l++)
                if (s.ColumnLayerThickness(c, l) > 1e-9) Theta[4 * c + l] = s.ColFC[4 * c + l];
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
        int t = TopOf(c);
        if (t < 0) return 0;
        double phi = _s.ColPhi[4 * c + t];
        return phi > 1e-9 ? Theta[4 * c + t] / phi : 0;
    }

    /// m of free water film on the column's surface.
    public double FilmAt(int col) => Film[col];
    /// m of ponded water above the column's surface.
    public double StandingAt(int col) => Standing[col];

    /// Depth (m below the ground surface) of the perched saturated zone's top in column c; NaN if there is none.
    public double WaterTableDepth(int col)
    {
        double hs = SatThickness(col, out _, out _, out _);
        if (hs <= 1e-6) return double.NaN;
        double hc = _s.ColumnSoilThickness(col);
        return Math.Max(0, hc - hs);
    }

    private int TopOf(int c)
    {
        for (int l = 0; l < 4; l++) if (_s.ColH[4 * c + l] > 1e-9) return l;
        return -1;
    }

    /// Saturated (perched) thickness above the bedrock contact. The column's total MOBILE water (above field capacity,
    /// wherever it currently sits) is packed from the bottom up, each layer holding its specific yield (phi - FC), as
    /// gravity would settle it; the packed height is the saturated thickness, so head is a continuous monotonic function
    /// of mobile storage. keff is the thickness-weighted Ksat of that zone, sy its mean specific yield.
    private double SatThickness(int c, out double keff, out double mobile, out double sy)
    {
        double mob = 0;
        for (int l = 0; l < 4; l++)
        {
            double h = _s.ColH[4 * c + l];
            if (h > 1e-9) mob += Math.Max(0, Theta[4 * c + l] - _s.ColFC[4 * c + l]) * h;
        }
        mobile = mob;
        double hs = 0, ks = 0, ys = 0, rem = mob;
        for (int l = 3; l >= 0 && rem > 0; l--)
        {
            double h = _s.ColH[4 * c + l];
            if (h <= 1e-9) continue;
            int k = 4 * c + l;
            double yl = _s.ColPhi[k] - _s.ColFC[k];
            double y = yl * h;
            if (y <= 1e-12) continue;
            double frac = Math.Min(1, rem / y);
            hs += h * frac; ks += _s.ColK[k] * h * frac; ys += yl * h * frac;
            rem -= frac * y;
        }
        keff = hs > 0 ? ks / hs : 0; sy = hs > 0 ? ys / hs : 0.1;
        return hs;
    }

    /// Advances water by dt seconds. rainRate is m/s per ground point; evaporationDemand m/s;
    /// transpirationDemandPerRootMetre is m3 water per m of root per s.
    public Budget Step(double dt, Func<Vec2, double> rainRate, double evaporationDemand, double transpirationDemandPerRootMetre)
    {
        double before = Stored();
        if (!(dt > 0)) return new Budget("water_m3", before, before, 0, 0);
        _rainAcc = _infAcc = _percAcc = _evapAcc = _trAcc = _deepAcc = _runAcc = _subAcc = 0;

        for (int c = 0; c < _nc; c++)
        {
            double r = rainRate == null ? 0 : rainRate(_s.ColumnCentre(c));
            _rain[c] = double.IsFinite(r) && r > 0 ? r : 0;
        }
        bool uptake = TranspirationEnabled && transpirationDemandPerRootMetre > 0;
        if (uptake) RefreshRoots();
        // Layer structure is constant within a step: cache the topology.
        for (int c = 0; c < _nc; c++)
        {
            int top = -1, bot = -1; double hc = 0;
            for (int l = 0; l < 4; l++)
            {
                double h = _s.ColH[4 * c + l];
                if (h > 1e-9) { if (top < 0) top = l; bot = l; }
                hc += h;
                int nx = -1;
                for (int m = l + 1; m < 4; m++) if (_s.ColH[4 * c + m] > 1e-9) { nx = m; break; }
                _nxt[4 * c + l] = (sbyte)nx;
            }
            _top[c] = (sbyte)top; _bot[c] = (sbyte)bot;
            _zb[c] = _s.ColSurf[c] - hc;
        }

        int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep - 1e-12));
        double ds = dt / n;
        for (int k = 0; k < n; k++) SubStep(ds, evaporationDemand, transpirationDemandPerRootMetre);
        _simTime += dt;

        double after = Stored();
        double rain = _rainAcc * _a, inf = _infAcc * _a, perc = _percAcc * _a, evap = _evapAcc * _a;
        double tr = _trAcc * _a, deep = _deepAcc * _a, run = _runAcc * _a, sub = _subAcc * _a;
        LastFluxes = new WaterFluxes(rain, inf, perc, evap, tr, deep, run);
        LastSubsurfaceOutflow = sub;
        Publish();
        return new Budget("water_m3", before, after, rain, evap + tr + deep + run + sub);
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
        // Surface exchange (rain, film, infiltration, evaporation) runs on a finer clock than the soil: its
        // time constants are minutes, the soil's are hours.
        int ns = Math.Max(1, (int)Math.Ceiling(ds / SurfaceSubstep - 1e-9));
        double dsf = ds / ns;
        _anyStanding = false;
        for (int m = 0; m < ns; m++) Surface(dsf, evapD);
        Soil(ds, trD);
    }

    private void Surface(double ds, double evapD)
    {
        double[] H = _s.ColH, K = _s.ColK;
        float[] FC = _s.ColFC, PHI = _s.ColPhi;
        for (int c = 0; c < _nc; c++)
        {
            // 1. rain onto the surface
            double st = Standing[c];
            double r = _rain[c] * ds;
            st += r; _rainAcc += r;
            // 2. film tracks recent wetting: moved out of standing water (conserved)
            double fl = Film[c];
            double target = Math.Min(st, FilmCap);
            if (fl < target) { double dF = target - fl; st -= dF; fl += dF; }
            Film[c] = fl;
            // 3. infiltration from standing water into the top layer
            int t = _top[c];
            if (t >= 0 && st > 0)
            {
                int k = 4 * c + t;
                double h = H[k], th = Theta[k];
                double cap = K[k] * ds * (1 + st / h);
                double free = Math.Max(0, (PHI[k] - th) * h);
                double q = Math.Min(st, Math.Min(cap, free));
                if (q > 0) { st -= q; Theta[k] = th + q / h; _infAcc += q; }
            }
            // 4. evaporation: standing water, film, then the top layer above wilting point / 2
            if (EvaporationEnabled)
            {
                double rem = evapD * ds;
                double es = Math.Min(st, rem);
                st -= es; _evapAcc += es; rem -= es;
                double fe = evapD > 0 ? Film[c] * (1 - Math.Exp(-ds / FilmTau)) : 0;   // film dries on its own clock, only while there is demand
                Film[c] -= fe; _evapAcc += fe;
                if (t >= 0 && rem > 0)
                {
                    int k = 4 * c + t;
                    double h = H[k], th = Theta[k];
                    double lo = 0.5 * _s.ColWP[k];
                    double frac = FC[k] > lo ? MathD.Clamp01((th - lo) / (FC[k] - lo)) : 0;
                    double q = Math.Min(rem * frac, Math.Max(0, th - lo) * h);
                    if (q > 0) { Theta[k] -= q / h; _evapAcc += q; }
                }
            }
            Standing[c] = st;
            if (st > 0) _anyStanding = true;
        }
    }

    private void Soil(double ds, double trD)
    {
        double[] H = _s.ColH, K = _s.ColK;
        float[] FC = _s.ColFC, PHI = _s.ColPhi;
        for (int c = 0; c < _nc; c++)
        {
            int t = _top[c];
            if (t < 0) continue;
            // 5. percolation of water above field capacity, layer to layer
            for (int l = t; l < 3; l++)
            {
                int m = _nxt[4 * c + l];
                if (m < 0) break;
                if (H[4 * c + l] <= 1e-9) continue;
                int kl = 4 * c + l, km = 4 * c + m;
                double hl = H[kl], hm = H[km];
                double excess = Math.Max(0, Theta[kl] - FC[kl]) * hl;
                if (excess <= 0) continue;
                double cap = K[km] * ds;
                double free = Math.Max(0, (PHI[km] - Theta[km]) * hm);
                double q = Math.Min(excess, Math.Min(cap, free));
                if (q <= 0) continue;
                Theta[kl] -= q / hl;
                Theta[km] += q / hm;
                _percAcc += q;
            }
            // 6. deep drainage through the bedrock contact: limited by BEDROCK conductivity (+ optional fracture leakage)
            if (DeepDrainageEnabled)
            {
                int L = _bot[c];
                int k = 4 * c + L;
                double h = H[k];
                double excess = Math.Max(0, Theta[k] - FC[k]) * h;
                if (excess > 0)
                {
                    double q = Math.Min(excess, (BedrockK + FractureLeakage * K[k]) * ds);
                    Theta[k] -= q / h;
                    _deepAcc += q;
                }
            }
        }
        // 7. surface flow of standing water (diffusion-wave, downslope, out at the domain edge)
        if (_anyStanding) LateralStanding(ds);
        // 8. perched saturated flow along the bedrock head gradient; exfiltration where the profile is full
        LateralSubsurface(ds);
        // 9. root uptake per layer, limited to water above wilting point
        if (TranspirationEnabled && trD > 0)
            for (int c = 0; c < _nc; c++)
                for (int l = 0; l < 4; l++)
                {
                    int k = 4 * c + l;
                    double h = H[k];
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
        int nx = _nx;
        for (int c = 0; c < _nc; c++) _e[c] = _s.ColSurf[c] + Standing[c];
        // direction 0 +X, 1 -X, 2 +Z, 3 -Z
        for (int c = 0; c < _nc; c++)
        {
            if (Standing[c] <= 0) { _raw[4 * c] = _raw[4 * c + 1] = _raw[4 * c + 2] = _raw[4 * c + 3] = 0; continue; }
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

    /// Perched saturated flow. Darcy flux per unit width = Ksat * Hface * (head_i - head_j) / Cell with
    /// head = bedrock-contact elevation + saturated thickness, Hface the saturated thickness above the higher of the two
    /// contacts (upstream weighting). Per-face transfer is also capped at 0.4 of the water that would equalise the two
    /// heads, so the explicit scheme is monotone at any dt; a source never gives more than half its mobile water.
    /// Receivers fill their layers from the bottom up; what cannot be stored (profile full) exfiltrates as standing water.
    private void LateralSubsurface(double ds)
    {
        int nx = _nx;
        bool any = false;
        for (int c = 0; c < _nc; c++)
        {
            double hs = SatThickness(c, out double ke, out double mob, out double sy);
            _kef[c] = ke; _mob[c] = mob; _sy[c] = sy; _head[c] = _zb[c] + hs;
            if (mob > 1e-12) any = true;
        }
        if (!any) return;
        double kdt = ds / (_cell * _cell);
        for (int c = 0; c < _nc; c++)
        {
            int b = 4 * c;
            _lat[b] = _lat[b + 1] = _lat[b + 2] = _lat[b + 3] = 0;
            if (_mob[c] <= 1e-12) continue;
            int i = c / _nz, j = c % _nz;
            double sum = 0;
            for (int d = 0; d < 4; d++)
            {
                int di = d == 0 ? 1 : d == 1 ? -1 : 0, dj = d == 2 ? 1 : d == 3 ? -1 : 0;
                int ni = i + di, nj = j + dj;
                double zbN, headN, syN = _sy[c];
                bool inside = ni >= 0 && ni < nx && nj >= 0 && nj < _nz;
                if (inside) { int nb = ni * _nz + nj; zbN = _zb[nb]; headN = _head[nb]; syN = Math.Min(syN, _sy[nb] > 0 ? _sy[nb] : syN); }
                else
                {
                    if (!SubsurfaceOutflowEnabled) continue;
                    int ii = i - di, jj = j - dj;
                    zbN = ii >= 0 && ii < nx && jj >= 0 && jj < _nz ? 2 * _zb[c] - _zb[ii * _nz + jj] : _zb[c];
                    headN = zbN + (_head[c] - _zb[c]);   // kinematic (free-drainage) boundary: table parallel to the bedrock beyond the edge
                }
                double dh = _head[c] - headN;
                if (dh <= 0) continue;
                double hf = _head[c] - Math.Max(_zb[c], zbN);
                if (hf <= 0) continue;
                double q = _kef[c] * hf * dh * kdt;
                double cap = 0.2 * syN * dh;        // well inside the water that would equalise the two heads
                if (q > cap) q = cap;
                _lat[b + d] = q; sum += q;
            }
            if (sum > 0)
            {
                double lim = 0.5 * _mob[c];
                if (sum > lim) { double al = lim / sum; for (int d = 0; d < 4; d++) _lat[b + d] *= al; }
            }
        }
        for (int c = 0; c < _nc; c++)
        {
            int b = 4 * c;
            if (_lat[b] + _lat[b + 1] + _lat[b + 2] + _lat[b + 3] <= 0) continue;
            int i = c / _nz, j = c % _nz;
            for (int d = 0; d < 4; d++)
            {
                double q = _lat[b + d];
                if (q <= 0) continue;
                int ni = i + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                double got = RemoveMobile(c, q);
                if (ni >= 0 && ni < nx && nj >= 0 && nj < _nz) AddFromBelow(ni * _nz + nj, got);
                else _subAcc += got;
            }
        }
    }

    /// Takes up to q (m over the column) from the column's mobile water, proportionally across layers.
    private double RemoveMobile(int c, double q)
    {
        double tot = 0;
        for (int l = 0; l < 4; l++)
        {
            int k = 4 * c + l;
            double h = _s.ColH[k];
            if (h > 1e-9) tot += Math.Max(0, Theta[k] - _s.ColFC[k]) * h;
        }
        if (tot <= 0) return 0;
        double take = Math.Min(q, tot);
        double f = take / tot;
        for (int l = 0; l < 4; l++)
        {
            int k = 4 * c + l;
            double h = _s.ColH[k];
            if (h <= 1e-9) continue;
            double m = Math.Max(0, Theta[k] - _s.ColFC[k]) * h;
            if (m > 0) Theta[k] -= m * f / h;
        }
        return take;
    }

    /// Adds q (m over the column) filling free pore space from the bottom layer up; the rest becomes standing water.
    private void AddFromBelow(int c, double q)
    {
        for (int l = 3; l >= 0 && q > 0; l--)
        {
            int k = 4 * c + l;
            double h = _s.ColH[k];
            if (h <= 1e-9) continue;
            double free = Math.Max(0, (_s.ColPhi[k] - Theta[k]) * h);
            double a = Math.Min(q, free);
            if (a <= 0) continue;
            Theta[k] += a / h; q -= a;
        }
        if (q > 0) Standing[c] += q;
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
