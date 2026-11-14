// Mesh generation from the layered substrate (S3, reworked in S1b for continuous geometry).
//
// One volumetric state drives every surface: ground grid, outcrop top, cut faces at the substrate boundary and
// at excavations, and buried stones. Heights come from LayeredSubstrate.Height, horizon depths from
// HorizonBoundaryDepth, composition from SampleSubstrate, and the soil/rock split from ContactFunction.
//
// Lattice: step cells of size hx = W/ceil(W/step) so edges land on the substrate boundary; positions are
// Origin + index * spacing, so shared borders are bit-identical between tiles built at the same step.
// The soil/rock contact is the zero set of the continuous contact function g = soilSurface - bedrock. Each cell is
// clipped along that contour (marching squares; crossing points found on cell edges by bisection of the exact
// function), and the crossing vertices are SHARED exactly between the Ground and Rock meshes (and the cut faces),
// so the contact is a smooth irregular curve at any step, soil thins to a feather edge and there is no collar.
//
// Vertex attribute layout (identical for Ground, CutFaces, Rock, Stones):
//   Positions  world metres, Y up.
//   Normals    unit, outward from solid.
//   Colors     rgb = dry albedo estimate (linear); a = Organic volume fraction.
//   UV         tops: (x, z). Walls and excavation strips: (distance along the face, depth below surface).
//   UV2        x = Horizon as float (1 Litter .. 5 Bedrock; stones 5), y = depth below local surface (m).
//   Custom0    (Saturation 0..1, FilmWater mm clamped 0..1, Porosity, AggregateDiameter cm).
//   Custom1    (CoarseFragments, Clay, Sand, FineRootDensity / 2000 clamped 0..1).
//
// Winding: front faces are CLOCKWISE seen from the outward side (Godot default). The right-hand cross product
// (b - a) x (c - a) of every triangle points INTO the solid, opposite to the vertex normal. MeshData.RecomputeNormals
// uses the other sign and must not be applied to these meshes.
using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr;

public sealed class SubstrateMeshSet
{
    public MeshData Ground = new();
    public MeshData CutFaces = new();
    public MeshData Rock = new();
    public MeshData Stones = new();
    public MeshData Litter = new();
    public int Triangles;
    public double Step;
    public int Subdivisions;
    /// Emitted stones with their vertex span inside Stones.
    public readonly List<(Stone Stone, int FirstVertex, int VertexCount)> StoneSpans = new();
}

public static class SubstrateMesher
{
    /// Builds the meshes for the cells whose centres lie in region (XZ). step is metres between vertices.
    public static SubstrateMeshSet Build(LayeredSubstrate s, Aabb region, double step, bool includeCutFaces)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (!(step > 0)) throw new ArgumentException("step must be positive", nameof(step));
        return new MeshBuilder(s, step).Build(region, includeCutFaces);
    }

    /// Rewrites Custom0.xy (saturation, film mm) for every vertex. Positions are not touched.
    public static void RefreshWater(LayeredSubstrate s, MeshData m)
    {
        int n = m.VertexCount;
        while (m.Custom0.Count < 4 * n) m.Custom0.Add(0);
        for (int i = 0; i < n; i++)
        {
            var p = m.Position(i);
            double x = Math.Min(Math.Max(p.X, s.Ox), s.Ox + s.Nx * s.Cell);
            double z = Math.Min(Math.Max(p.Z, s.Oz), s.Oz + s.Nz * s.Cell);
            double h = s.Height(new Vec2(x, z));
            double sat = s.SampleMatrix(new Vec3(x, Math.Min(p.Y, h - 0.003), z)).Saturation;
            int c = s.ColumnAt(new Vec2(x, z));
            double film = c < 0 ? 0 : s.Water.Film[c];
            m.Custom0[4 * i] = (float)sat;
            m.Custom0[4 * i + 1] = (float)MathD.Clamp01(film * 1000.0);
        }
    }

    /// Square-ish XZ tiles covering the substrate footprint, full height.
    public static IEnumerable<Aabb> Tiles(LayeredSubstrate s, double tileSize)
    {
        if (!(tileSize > 0)) throw new ArgumentException("tileSize must be positive", nameof(tileSize));
        double w = s.Nx * s.Cell, d = s.Nz * s.Cell;
        int nx = (int)Math.Ceiling(w / tileSize - 1e-9), nz = (int)Math.Ceiling(d / tileSize - 1e-9);
        for (int ix = 0; ix < nx; ix++)
            for (int iz = 0; iz < nz; iz++)
            {
                double x0 = s.Ox + ix * tileSize, x1 = Math.Min(s.Ox + (ix + 1) * tileSize, s.Ox + w);
                double z0 = s.Oz + iz * tileSize, z1 = Math.Min(s.Oz + (iz + 1) * tileSize, s.Oz + d);
                yield return new Aabb(new Vec3(x0, s.Bounds.Min.Y, z0), new Vec3(x1, s.Bounds.Max.Y, z1));
            }
    }

    public static CacheKey KeyFor(LayeredSubstrate s, Aabb tile, int detail)
    {
        ulong h = Rng.Mix(s.Seed, (ulong)BitConverter.DoubleToInt64Bits(tile.Min.X));
        h = Rng.Mix(h, (ulong)BitConverter.DoubleToInt64Bits(tile.Min.Z));
        h = Rng.Mix(h, (ulong)BitConverter.DoubleToInt64Bits(tile.Max.X));
        h = Rng.Mix(h, (ulong)BitConverter.DoubleToInt64Bits(tile.Max.Z));
        return new CacheKey(h, s.Version, detail);
    }

    /// Evaluates the exact rendered surface height and outward normal (including soil clod relief and rock
    /// bedding ledges/joint grooves) so attached surface colonies sit flush on the substrate mesh.
    public static double SurfaceHeightAt(LayeredSubstrate s, double x, double z, out Vec3 normal, out bool isRock, double step = 0.01)
    {
        var b = new MeshBuilder(s, step);
        return b.SampleTopSurface(x, z, out normal, out isRock);
    }
}

/// Internal worker. One instance per (substrate state, step). Not thread-safe; build on one thread.
internal sealed class MeshBuilder
{
    private const double WallInset = 0.002;

    private readonly LayeredSubstrate _s;
    private readonly double _step, _ox, _oz, _w, _d, _hx, _hz, _yBot;
    private readonly int _nxc, _nzc;
    private readonly ulong _seedA, _seedB, _seedH;
    private readonly Vec3 _bed;
    private readonly double _ledgeP;
    private readonly Vec3[] _joint = new Vec3[2];
    private readonly double[] _jointS = new double[2], _jointD = new double[2];
    private readonly List<double> _depths = new();
    private readonly Dictionary<(long, long, long, long), (double X, double Z, double Y)> _cross = new();

    public MeshBuilder(LayeredSubstrate s, double step)
    {
        _s = s; _step = step;
        _ox = s.Ox; _oz = s.Oz; _w = s.Nx * s.Cell; _d = s.Nz * s.Cell;
        _nxc = Math.Max(1, (int)Math.Ceiling(_w / step - 1e-9));
        _nzc = Math.Max(1, (int)Math.Ceiling(_d / step - 1e-9));
        _hx = _w / _nxc; _hz = _d / _nzc;
        _yBot = s.Bounds.Min.Y;
        ulong seed = s.Seed;
        _seedA = Rng.Mix(seed, Hash.Fnv1a64("mesh-relief-a"));
        _seedB = Rng.Mix(seed, Hash.Fnv1a64("mesh-relief-b"));
        _seedH = Rng.Mix(seed, Hash.Fnv1a64("mesh-wall-humus"));
        _bed = s.BedN;
        _ledgeP = ReprId.Range(ReprId.Hash(seed, Hash.Fnv1a64("mesh-ledge")), 1, 0.03, 0.08);
        for (int f = 0; f < 2; f++)
        {
            ulong hf = ReprId.Hash(seed, Hash.Fnv1a64("mesh-joint"), (ulong)f);
            double ang = ReprId.Range(hf, 1, 0, 2 * Math.PI);
            _joint[f] = _bed.Cross(new Vec3(Math.Cos(ang), 0, Math.Sin(ang))).Normalized();
            _jointS[f] = ReprId.Range(hf, 2, 0.05, 0.2);
            _jointD[f] = ReprId.Range(hf, 3, 0.001, 0.003);
        }
    }

    // ------------------------------------------------------------ lattice and contact

    private double X(long I) => _ox + I * _hx;
    private double Z(long J) => _oz + J * _hz;
    private double Clx(double x) => Math.Min(Math.Max(x, _ox), _ox + _w);
    private double Clz(double z) => Math.Min(Math.Max(z, _oz), _oz + _d);
    private double RawHeightAt(double x, double z) => _s.Height(new Vec2(Clx(x), Clz(z)));

    private double HeightAt(double x, double z)
    {
        double h0 = RawHeightAt(x, z);
        double r = 1.4 * _step;
        if (r > 0.025) return h0;
        // Bevel the 90-degree equator lip of spherical excavations across ~1.4 * step so circular bowls don't stair-step on the square grid.
        // Far from any excavation (> r + 1.4 * step), nearExc is false and HeightAt returns h0 bit-identically.
        double u0 = _s.UndisturbedHeight(new Vec2(Clx(x), Clz(z)));
        double hP = RawHeightAt(x + r, z), hM = RawHeightAt(x - r, z);
        double hQ = RawHeightAt(x, z + r), hN = RawHeightAt(x, z - r);
        bool nearExc = (u0 - h0) > 1e-5
                    || (_s.UndisturbedHeight(new Vec2(Clx(x + r), Clz(z))) - hP) > 1e-5
                    || (_s.UndisturbedHeight(new Vec2(Clx(x - r), Clz(z))) - hM) > 1e-5
                    || (_s.UndisturbedHeight(new Vec2(Clx(x), Clz(z + r))) - hQ) > 1e-5
                    || (_s.UndisturbedHeight(new Vec2(Clx(x), Clz(z - r))) - hN) > 1e-5;
        if (!nearExc) return h0;
        double hNE = RawHeightAt(x + r, z + r), hNW = RawHeightAt(x - r, z + r);
        double hSE = RawHeightAt(x + r, z - r), hSW = RawHeightAt(x - r, z - r);
        return 0.28 * h0 + 0.11 * (hP + hM + hQ + hN) + 0.07 * (hNE + hNW + hSE + hSW);
    }

    private SubstrateSample MatAt(double x, double y, double z, bool surfaceSoilOnly = false) =>
        _s.SampleMatrix(new Vec3(Clx(x), y, Clz(z)), surfaceSoilOnly);

    /// Contact function g = soilSurface - bedrock at a ground point: soil where g > 0.
    private double G(double x, double z) => _s.ContactFunction(new Vec2(Clx(x), Clz(z)));

    /// 0 soil, 1 rock at lattice point (I, J).
    private int KindAt(long I, long J) => G(X(I), Z(J)) > 0 ? 0 : 1;

    internal int KindAtXZ(double x, double z) => G(x, z) > 0 ? 0 : 1;

    // Height-only query for attached geometry; avoids four normal finite differences.
    internal double SampleTopHeight(double x, double z)
    {
        int kind = KindAtXZ(x, z);
        return TopY(x, z, kind, out _, out _);
    }

    internal double SampleTopSurface(double x, double z, out Vec3 normal, out bool isRock)
    {
        int kind = KindAtXZ(x, z);
        isRock = kind == 1;
        normal = SurfaceNormalAt(x, z, kind);
        return TopY(x, z, kind, out _, out _);
    }

    private double FilmAt(double x, double z)
    {
        int c = _s.ColumnAt(new Vec2(Clx(x), Clz(z)));
        return c < 0 ? 0 : _s.Water.Film[c];
    }

    private Vec3 NormalAt(double x, double z)
    {
        double h = 0.5 * _step;
        double gx = (HeightAt(x + h, z) - HeightAt(x - h, z)) / (2 * h);
        double gz = (HeightAt(x, z + h) - HeightAt(x, z - h)) / (2 * h);
        return new Vec3(-gx, 1, -gz).Normalized();
    }

    private Vec3 SurfaceNormalAt(double x, double z, int kind)
    {
        double h1 = 0.5 * _step;
        double gx1 = (TopY(x + h1, z, kind, out _, out _) - TopY(x - h1, z, kind, out _, out _)) / (2 * h1);
        double gz1 = (TopY(x, z + h1, kind, out _, out _) - TopY(x, z - h1, kind, out _, out _)) / (2 * h1);
        double h2 = Math.Min(1.6 * _step, 0.022);
        double gx2 = (TopY(x + h2, z, kind, out _, out _) - TopY(x - h2, z, kind, out _, out _)) / (2 * h2);
        double gz2 = (TopY(x, z + h2, kind, out _, out _) - TopY(x, z - h2, kind, out _, out _)) / (2 * h2);
        return new Vec3(-(0.55 * gx1 + 0.45 * gx2), 1, -(0.55 * gz1 + 0.45 * gz2)).Normalized();
    }

    /// Point where the contact function changes sign on the lattice edge between two points of opposite sign,
    /// found by bisection of the exact function. Cached under a canonical (ordered) key, so every cell, tile and
    /// wall that touches the edge gets the identical point.
    private (double X, double Z, double Y) Crossing(long Ia, long Ja, long Ib, long Jb)
    {
        if (Ia > Ib || (Ia == Ib && Ja > Jb)) { (Ia, Ib) = (Ib, Ia); (Ja, Jb) = (Jb, Ja); }
        var key = (Ia, Ja, Ib, Jb);
        if (_cross.TryGetValue(key, out var cp)) return cp;
        double xa = X(Ia), za = Z(Ja), xb = X(Ib), zb = Z(Jb);
        bool sa = G(xa, za) > 0;
        double lo = 0, hi = 1;
        for (int it = 0; it < 32; it++)
        {
            double mid = 0.5 * (lo + hi);
            bool sm = G(xa + (xb - xa) * mid, za + (zb - za) * mid) > 0;
            if (sm == sa) lo = mid; else hi = mid;
        }
        double t = 0.5 * (lo + hi);
        double x = xa + (xb - xa) * t, z = za + (zb - za) * t;
        cp = (x, z, HeightAt(x, z));
        _cross[key] = cp;
        return cp;
    }

    // ------------------------------------------------------------ surface relief

    /// Fade out octaves whose wavelength is at or below twice the mesh step (no aliasing).
    private double Fade(double lambda) => MathD.Clamp01((0.5 * lambda - _step) / (0.25 * lambda));

    private double GroundRelief(double x, double z, double agg)
    {
        double a = Math.Max(agg, 0.0005);
        double l1 = Math.Max(4 * a, 0.065), l2 = Math.Max(12 * a, 0.13);
        double micro = 0.25 * a * (0.6 * Fade(l1) * Noise.Gradient(_seedA, x / l1, z / l1)
                                 + 0.4 * Fade(l2) * Noise.Gradient(_seedB, x / l2, z / l2));
        // Mesoscale clod and root-hummock relief (wavelengths >= 0.14 m are identical at step 0.01 and 0.03).
        double meso = 0.0035 * Fade(0.14) * Noise.Gradient(_seedA ^ 0x9E3779B97F4A7C15UL, x / 0.14, z / 0.14)
                    + 0.0050 * Fade(0.28) * Noise.Gradient(_seedB ^ 0xBF58476D1CE4E5B9UL, x / 0.28, z / 0.28);
        return micro + meso;
    }

    private static double Saw(double t) => (t - Math.Floor(t)) - 0.5;

    /// Fine bedding ledges: 4 mm sawtooth along the bedding normal (phase from the undisplaced point).
    private double LedgeAt(Vec3 p) => 0.004 * Saw(p.Dot(_bed) / _ledgeP);

    private double GroovesAt(Vec3 p)
    {
        double g = 0;
        for (int f = 0; f < 2; f++)
        {
            double q = p.Dot(_joint[f]) / _jointS[f];
            double dd = Math.Abs(q - Math.Round(q)) * _jointS[f];
            g -= _jointD[f] * 0.65 * Math.Exp(-(dd * dd) / (0.0045 * 0.0045));
        }
        return g;
    }

    /// Final top height of a lattice vertex. Relief fades to zero at the contact so soil and rock meet exactly
    /// on the shared crossing vertices (no bump, no collar).
    private double TopY(double x, double z, int kind, out double h, out SubstrateSample mat)
    {
        h = HeightAt(x, z);
        double g = G(x, z);
        if (kind == 0)
        {
            double und = _s.UndisturbedHeight(new Vec2(Clx(x), Clz(z)));
            bool unexcavated = (und - h) < 0.005;
            mat = MatAt(x, h - 0.002, z, surfaceSoilOnly: unexcavated);
            return h + GroundRelief(x, z, mat.AggregateDiameter) * MathD.SmoothStep(0, 0.012, g);
        }
        mat = MatAt(x, h - 0.003, z, surfaceSoilOnly: false);
        mat.Horizon = Horizon.Bedrock;
        mat.Organic = 0;
        double rw = MathD.SmoothStep(0, 0.012, -g);
        var p0 = new Vec3(x, h, z);
        double crag = 0.0045 * Fade(0.14) * (Noise.Value3(_seedA ^ 0x517CC1B727220A95UL, x / 0.11, h / 0.07, z / 0.11) - 0.5);
        return h + rw * (LedgeAt(p0) + GroovesAt(p0) + crag);
    }

    // ------------------------------------------------------------ attributes and emission

    private static Vec3 Albedo(in SubstrateSample m, Vec3 p, Vec3 bed)
    {
        Vec3 hue = m.MineralHue;
        double org = m.Organic, sand = m.Sand;
        Vec3 mineral = hue * ((0.52 + 0.34 * sand) * (1 - 0.52 * org));
        double patch = 0.90 + 0.20 * Noise.Value3(0x3141592653589793UL, p.X * 6.5, p.Y * 6.5, p.Z * 6.5);
        Vec3 c;
        switch (m.Horizon)
        {
            case Horizon.Litter:
                c = Vec3.Lerp(mineral * 0.55, new Vec3(0.135, 0.092, 0.058), 0.88) * patch;
                break;
            case Horizon.Humus:
                c = Vec3.Lerp(mineral * 0.50, new Vec3(0.102, 0.072, 0.047), 0.86) * patch;
                break;
            case Horizon.Weathered:
            {
                double band = 0.92 + 0.08 * Math.Sin(2 * Math.PI * p.Dot(bed) / 0.04);
                c = Vec3.Lerp(hue * 0.50, new Vec3(0.24, 0.18, 0.125), 0.58) * band * patch;
                break;
            }
            case Horizon.Bedrock:
            {
                double band = 0.90 + 0.10 * Math.Sin(2 * Math.PI * p.Dot(bed) / 0.038);
                c = (hue * 0.55) * band * (0.92 + 0.16 * Noise.Value3(0x2718281828459045UL, p.X * 9.0, p.Y * 9.0, p.Z * 9.0));
                break;
            }
            default:
                c = Vec3.Lerp(mineral * 0.65, new Vec3(0.21, 0.15, 0.098), 0.64) * patch;
                break;
        }
        return new Vec3(MathD.Clamp01(c.X), MathD.Clamp01(c.Y), MathD.Clamp01(c.Z));
    }

    private static int Emit(MeshData m, Vec3 p, Vec3 n, Vec3 alb, in SubstrateSample mat, double film,
        double u, double v, double u2, double v2)
    {
        int id = m.AddVertex(p, n, alb.X, alb.Y, alb.Z, mat.Organic, u, v, u2, v2);
        m.Custom0.Add(mat.Saturation);
        m.Custom0.Add((float)MathD.Clamp01(film * 1000.0));
        m.Custom0.Add(mat.Porosity);
        m.Custom0.Add((float)(mat.AggregateDiameter * 100.0));
        m.Custom1.Add(mat.CoarseFragments);
        m.Custom1.Add(mat.Clay);
        m.Custom1.Add(mat.Sand);
        m.Custom1.Add((float)MathD.Clamp01(mat.FineRootDensity / 2000.0));
        return id;
    }

    /// Triangle with clockwise-front winding (cross product opposes the vertex normals). Degenerates are dropped.
    private static void Tri(MeshData m, int a, int b, int c)
    {
        Vec3 pa = m.Position(a), pb = m.Position(b), pc = m.Position(c);
        Vec3 cr = (pb - pa).Cross(pc - pa);
        if (cr.LengthSq < 1e-22) return;
        Vec3 n = m.NormalAt(a) + m.NormalAt(b) + m.NormalAt(c);
        if (cr.Dot(n) > 0) (b, c) = (c, b);
        m.AddTriangle(a, b, c);
    }

    private static long Key(long I, long J) => (I << 32) ^ (J & 0xFFFFFFFFL);

    /// Ground or rock top vertex at lattice point (I, J), shared through map.
    private int TopVertex(MeshData m, Dictionary<long, int> map, long I, long J, int kind)
    {
        long key = Key(I, J);
        if (map.TryGetValue(key, out int id)) return id;
        id = FreeTop(m, X(I), Z(J), kind);
        map[key] = id;
        return id;
    }

    private int FreeTop(MeshData m, double x, double z, int kind)
    {
        double y = TopY(x, z, kind, out _, out var mat);
        var p = new Vec3(x, y, z);
        return Emit(m, p, SurfaceNormalAt(x, z, kind), Albedo(mat, p, _bed), mat, FilmAt(x, z), x, z, (double)mat.Horizon, 0);
    }

    /// Vertex on the soil/rock contact. No relief; identical position and attributes in every mesh that uses it.
    private int CrossVertex(MeshData m, Dictionary<(long, long, long, long), int> map, long Ia, long Ja, long Ib, long Jb, int side)
    {
        _ = side;
        if (Ia > Ib || (Ia == Ib && Ja > Jb)) { (Ia, Ib) = (Ib, Ia); (Ja, Jb) = (Jb, Ja); }
        var key = (Ia, Ja, Ib, Jb);
        if (map.TryGetValue(key, out int id)) return id;
        var cp = Crossing(Ia, Ja, Ib, Jb);
        double und = _s.UndisturbedHeight(new Vec2(Clx(cp.X), Clz(cp.Z)));
        bool unexcavated = (und - cp.Y) < 0.005;
        var mat = MatAt(cp.X, cp.Y - 0.002, cp.Z, surfaceSoilOnly: unexcavated);
        var p = new Vec3(cp.X, cp.Y, cp.Z);
        id = Emit(m, p, NormalAt(cp.X, cp.Z), Albedo(mat, p, _bed), mat, FilmAt(cp.X, cp.Z), cp.X, cp.Z, (double)mat.Horizon, 0);
        map[key] = id;
        return id;
    }

    // ------------------------------------------------------------ top surfaces

    private void BuildTops(MeshData ground, MeshData rock, int cx0, int cx1, int cz0, int cz1)
    {
        var mapG = new Dictionary<long, int>(); var mapR = new Dictionary<long, int>();
        var crG = new Dictionary<(long, long, long, long), int>(); var crR = new Dictionary<(long, long, long, long), int>();
        var gv = new double[4]; var kd = new int[4];
        var li = new long[4]; var lj = new long[4];
        // loop entries: corner (id>=0 -> corner index 0..3) or crossing on edge e (id = 4 + e), e from corner e to e+1
        var ent = new int[8];
        for (int cx = cx0; cx < cx1; cx++)
            for (int cz = cz0; cz < cz1; cz++)
            {
                long I0 = cx, J0 = cz;
                li[0] = I0; lj[0] = J0; li[1] = I0 + 1; lj[1] = J0; li[2] = I0 + 1; lj[2] = J0 + 1; li[3] = I0; lj[3] = J0 + 1;
                int nSoil = 0;
                for (int k = 0; k < 4; k++) { gv[k] = G(X(li[k]), Z(lj[k])); kd[k] = gv[k] > 0 ? 0 : 1; if (kd[k] == 0) nSoil++; }
                if (nSoil == 4 || nSoil == 0)
                {
                    int kind = nSoil == 4 ? 0 : 1;
                    var m = kind == 0 ? ground : rock; var map = kind == 0 ? mapG : mapR;
                    int a = TopVertex(m, map, li[0], lj[0], kind), b = TopVertex(m, map, li[1], lj[1], kind);
                    int c = TopVertex(m, map, li[2], lj[2], kind), d = TopVertex(m, map, li[3], lj[3], kind);
                    double dAC = Math.Abs(m.Position(a).Y - m.Position(c).Y);
                    double dBD = Math.Abs(m.Position(b).Y - m.Position(d).Y);
                    if (dBD < dAC - 1e-5) { Tri(m, a, b, d); Tri(m, b, c, d); }
                    else { Tri(m, a, b, c); Tri(m, a, c, d); }
                    continue;
                }
                int n = 0, ncross = 0;
                for (int k = 0; k < 4; k++)
                {
                    ent[n++] = k;
                    if (kd[k] != kd[(k + 1) & 3]) { ent[n++] = 4 + k; ncross++; }
                }
                for (int side = 0; side < 2; side++)
                {
                    var m = side == 0 ? ground : rock; var map = side == 0 ? mapG : mapR; var cm = side == 0 ? crG : crR;
                    // runs of corners of this side, each bounded by the crossings around it
                    for (int s0 = 0; s0 < n; s0++)
                    {
                        // a run starts at a corner of this side whose previous entry is a crossing
                        int e = ent[s0];
                        if (e >= 4 || kd[e] != side) continue;
                        int prev = ent[(s0 + n - 1) % n];
                        if (prev < 4) continue;                      // not the first corner of its run
                        var poly = new List<int>();
                        poly.Add(CrossId(m, cm, prev - 4, li, lj, side));
                        int p = s0;
                        while (true)
                        {
                            int q = ent[p];
                            if (q >= 4) { poly.Add(CrossId(m, cm, q - 4, li, lj, side)); break; }
                            poly.Add(TopVertex(m, map, li[q], lj[q], side));
                            p = (p + 1) % n;
                        }
                        for (int i = 1; i + 1 < poly.Count; i++) Tri(m, poly[0], poly[i], poly[i + 1]);
                    }
                }
                if (ncross == 4)
                {
                    // saddle: the middle quad of the four crossings takes the sign at the cell centre
                    double gc = G(X(I0) + 0.5 * _hx, Z(J0) + 0.5 * _hz);
                    int side = gc > 0 ? 0 : 1;
                    var m = side == 0 ? ground : rock; var cm = side == 0 ? crG : crR;
                    int a = CrossId(m, cm, 0, li, lj, side), b = CrossId(m, cm, 1, li, lj, side), c = CrossId(m, cm, 2, li, lj, side), d = CrossId(m, cm, 3, li, lj, side);
                    Tri(m, a, b, c); Tri(m, a, c, d);
                }
            }
    }

    private int CrossId(MeshData m, Dictionary<(long, long, long, long), int> cm, int edge, long[] li, long[] lj, int side)
    {
        int a = edge, b = (edge + 1) & 3;
        return CrossVertex(m, cm, li[a], lj[a], li[b], lj[b], side);
    }

    // ------------------------------------------------------------ walls

    /// One vertical column from the top surface down to yBot. Rows at every horizon boundary and every step.
    private void Column(MeshData m, double x, double z, double yTop, double yBot, double ox, double oz,
        bool endCol, double along, List<int> ids, List<double> ys)
    {
        ids.Clear(); ys.Clear();
        double hs = HeightAt(x, z);
        AddRow(m, x, z, yTop, ox, oz, endCol, along, 0, true, ids, ys);
        if (yTop <= yBot + 1e-9) return;
        _depths.Clear();
        var q = new Vec2(Clx(x), Clz(z));
        double und = _s.UndisturbedHeight(q);
        var rows = new List<double>();
        for (int lev = 1; lev <= 4; lev++)
        {
            double d = _s.HorizonBoundaryDepth(q, lev);
            if (double.IsFinite(d) && d > 1e-6) rows.Add(und - d);   // boundaries belong to the undisturbed profile
        }
        int nb = rows.Count;
        for (int g = 1; ; g++)
        {
            double y = hs - g * _step;
            if (y <= yBot + 1e-9) break;
            bool near = false;
            for (int i = 0; i < nb; i++)
                if (Math.Abs(rows[i] - y) < 0.25 * _step) { near = true; break; }
            if (!near) rows.Add(y);
        }
        rows.Sort((a, b) => b.CompareTo(a));                          // top to bottom
        double last = double.MaxValue;
        foreach (double y in rows)
        {
            if (y >= yTop - 1e-9 || y <= yBot + 1e-9 || y >= last - 1e-9) continue;
            AddRow(m, x, z, y, ox, oz, endCol, along, hs - y, false, ids, ys);
            last = y;
        }
        AddRow(m, x, z, yBot, ox, oz, endCol, along, hs - yBot, false, ids, ys);
    }

    private void AddRow(MeshData m, double x, double z, double y, double ox, double oz, bool endCol,
        double along, double depth, bool top, List<int> ids, List<double> ys)
    {
        double ix = x - ox * WallInset, iz = z - oz * WallInset;
        double hin = HeightAt(ix, iz);
        var mat = MatAt(ix, Math.Min(y, hin - 0.003), iz);
        double disp = 0;
        if (!top && !endCol)
        {
            disp = -0.003 * mat.Sand * (1 - mat.Organic) + 0.002 * mat.Clay * mat.Compaction;
            if (mat.Horizon == Horizon.Litter || mat.Horizon == Horizon.Humus)
            {
                double a = Math.Max(mat.AggregateDiameter, 0.0005);
                disp += 0.002 * mat.Organic * Noise.Value3(_seedH, x / a, y / a, z / a);
            }
            if (mat.Horizon == Horizon.Weathered || mat.Horizon == Horizon.Bedrock)
            {
                // The cut face repeats the outcrop's bedding ledges: same strata coordinate, same riser heights.
                disp += LedgeAt(new Vec3(x, y, z)) + 0.25 * _s.LedgeAt(Clx(x), Clz(z));
            }
        }
        var p = new Vec3(x + ox * disp, y, z + oz * disp);
        int id = Emit(m, p, new Vec3(ox, 0, oz), Albedo(mat, p, _bed), mat, FilmAt(ix, iz),
            along, depth, (double)mat.Horizon, depth);
        ids.Add(id); ys.Add(y);
    }

    private static void Zip(MeshData m, List<int> a, List<double> ya, List<int> b, List<double> yb)
    {
        int i = 0, j = 0;
        while (i < a.Count - 1 || j < b.Count - 1)
        {
            bool advA;
            if (i == a.Count - 1) advA = false;
            else if (j == b.Count - 1) advA = true;
            else advA = ya[i + 1] >= yb[j + 1];
            if (advA) { Tri(m, a[i], b[j], a[i + 1]); i++; }
            else { Tri(m, a[i], b[j], b[j + 1]); j++; }
        }
    }

    private double LatticeTopY(long I, long J) => TopY(X(I), Z(J), KindAt(I, J), out _, out _);

    /// Wall on a substrate boundary. alongZ: wall at fixed I, columns at J = c for c in [c0, c1]. The soil/rock
    /// crossing points on the boundary edge get their own columns so the wall top follows the same polyline as the top meshes.
    private void BoundaryWall(MeshData m, bool alongZ, long fixedIdx, int c0, int c1, double ox, double oz)
    {
        var cols = new List<(double X, double Z, double Y)>();
        for (int c = c0; c <= c1; c++)
        {
            long I = alongZ ? fixedIdx : c, J = alongZ ? c : fixedIdx;
            if (c > c0)
            {
                long Ip = alongZ ? fixedIdx : c - 1, Jp = alongZ ? c - 1 : fixedIdx;
                if (KindAt(Ip, Jp) != KindAt(I, J)) cols.Add(Crossing(Ip, Jp, I, J));
            }
            cols.Add((X(I), Z(J), LatticeTopY(I, J)));
        }
        List<int>? prevIds = null; List<double>? prevYs = null;
        double start = alongZ ? cols[0].Z : cols[0].X;
        for (int k = 0; k < cols.Count; k++)
        {
            var (x, z, yTop) = cols[k];
            var ids = new List<int>(); var ys = new List<double>();
            double along = Math.Abs((alongZ ? z : x) - start);
            Column(m, x, z, yTop, _yBot, ox, oz, k == 0 || k == cols.Count - 1, along, ids, ys);
            if (prevIds != null && prevYs != null) Zip(m, prevIds, prevYs, ids, ys);
            prevIds = ids; prevYs = ys;
        }
    }

    /// Excavation wall strip between two neighbouring ground vertices whose heights differ by > 1.5 step.
    private void DigStrip(MeshData m, long IA, long JA, long IB, long JB)
    {
        double xa = X(IA), za = Z(JA), xb = X(IB), zb = Z(JB);
        double ya = LatticeTopY(IA, JA);
        double yb = LatticeTopY(IB, JB);
        if (Math.Abs(ya - yb) <= 1.5 * _step) return;
        double ex = xb - xa, ez = zb - za, len = Math.Sqrt(ex * ex + ez * ez);
        double px = -ez / len, pz = ex / len;
        double mx = 0.5 * (xa + xb), mz = 0.5 * (za + zb), h = 0.5 * _step;
        if (HeightAt(mx + px * h, mz + pz * h) > HeightAt(mx - px * h, mz - pz * h)) { px = -px; pz = -pz; }
        // Place the sub-surface excavation strip strictly below min(ya, yb) so it never pokes above the Ground bowl rim.
        double yTopStrip = Math.Min(ya, yb) - 0.002;
        double yBotStrip = Math.Max(_yBot + 0.002, yTopStrip - Math.Max(0.042, Math.Abs(ya - yb) + 0.02));
        var ia = new List<int>(); var sa = new List<double>();
        var ib = new List<int>(); var sb = new List<double>();
        Column(m, xa, za, yTopStrip, yBotStrip, px, pz, true, 0, ia, sa);
        Column(m, xb, zb, yTopStrip, yBotStrip, px, pz, true, len, ib, sb);
        Zip(m, ia, sa, ib, sb);
    }

    private void CutFaces(MeshData m, int cx0, int cx1, int cz0, int cz1)
    {
        if (cx0 == 0) BoundaryWall(m, true, 0, cz0, cz1, -1, 0);
        if (cx1 == _nxc) BoundaryWall(m, true, _nxc, cz0, cz1, 1, 0);
        if (cz0 == 0) BoundaryWall(m, false, 0, cx0, cx1, 0, -1);
        if (cz1 == _nzc) BoundaryWall(m, false, _nzc, cx0, cx1, 0, 1);

        // Each grid edge is owned by exactly one tile (half-open ranges, domain edge owned by the last tile).
        for (int cx = cx0; cx < cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                if (!(cz < cz1 || (cz == _nzc && cz1 == _nzc))) continue;
                DigStrip(m, cx, cz, cx + 1, cz);
            }
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz < cz1; cz++)
            {
                if (!(cx < cx1 || (cx == _nxc && cx1 == _nxc))) continue;
                DigStrip(m, cx, cz, cx, cz + 1);
            }
    }

    // ------------------------------------------------------------ stones & 3D litter

    private static (Vec3[] V, int[] F) Icosphere(int level)
    {
        double t = (1 + Math.Sqrt(5)) / 2;
        var v = new List<Vec3>
        {
            new Vec3(-1, t, 0), new Vec3(1, t, 0), new Vec3(-1, -t, 0), new Vec3(1, -t, 0),
            new Vec3(0, -1, t), new Vec3(0, 1, t), new Vec3(0, -1, -t), new Vec3(0, 1, -t),
            new Vec3(t, 0, -1), new Vec3(t, 0, 1), new Vec3(-t, 0, -1), new Vec3(-t, 0, 1),
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
        var f = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        for (int l = 0; l < level; l++)
        {
            var nf = new List<int>();
            var cache = new Dictionary<long, int>();
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(v, cache, a, b), bc = Mid(v, cache, b, c), ca = Mid(v, cache, c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        return (v.ToArray(), f.ToArray());
    }

    private static int Mid(List<Vec3> v, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int id)) return id;
        v.Add(((v[a] + v[b]) * 0.5).Normalized());
        id = v.Count - 1;
        cache[key] = id;
        return id;
    }

    private void BuildStones(SubstrateMeshSet set, Aabb region, bool cut)
    {
        var (dirs, faces) = Icosphere(_step <= 0.015 ? 2 : _step <= 0.04 ? 1 : 0);
        const double pad = 0.12;
        double w = _ox + _w, dEdge = _oz + _d;
        var pos = new Vec3[dirs.Length];
        foreach (var st in _s.StonesIn(new Aabb(new Vec3(region.Min.X - pad, region.Min.Y - pad, region.Min.Z - pad),
                     new Vec3(region.Max.X + pad, region.Max.Y + pad, region.Max.Z + pad))))
        {
            Vec3 c = st.Centre;
            if (!(c.X >= region.Min.X && c.X < region.Max.X && c.Z >= region.Min.Z && c.Z < region.Max.Z)) continue;
            ulong hs = ReprId.Hash(_s.Seed, st.Id, 0x570);
            double maxSemi = Math.Max(st.Semi.X, Math.Max(st.Semi.Y, st.Semi.Z));
            bool visible = false;
            for (int i = 0; i < dirs.Length; i++)
            {
                Vec3 d = dirs[i];
                double r = 1 + 0.11 * (Noise.Value3(hs, d.X * 2.4, d.Y * 2.4, d.Z * 2.4) - 0.35)
                             + 0.05 * (Noise.Value3(hs ^ 0xA5A5A5A5UL, d.X * 5.2, d.Y * 5.2, d.Z * 5.2) - 0.5);
                Vec3 local = new Vec3(d.X * st.Semi.X * r, d.Y * st.Semi.Y * r, d.Z * st.Semi.Z * r);
                pos[i] = c + st.AxisA * local.X + st.AxisB * local.Y + st.AxisC * local.Z;
                if (pos[i].Y > HeightAt(pos[i].X, pos[i].Z)) visible = true;
            }
            bool nearWall = cut && (c.X - _ox <= 1.2 * maxSemi || w - c.X <= 1.2 * maxSemi
                                    || c.Z - _oz <= 1.2 * maxSemi || dEdge - c.Z <= 1.2 * maxSemi);
            if (!visible && !nearWall) continue;

            var soilMat = _s.SampleMatrix(c);
            Vec3 hue = soilMat.MineralHue.LengthSq > 1e-6 ? soilMat.MineralHue : _s.ParentHue;
            soilMat.Horizon = Horizon.Bedrock;
            soilMat.Organic = 0;
            double toneVar = 0.42 + 0.24 * ReprId.Unit(hs, 7);
            double warmVar = (ReprId.Unit(hs, 11) - 0.5) * 0.08;
            var alb = new Vec3(
                MathD.Clamp01(hue.X * toneVar + warmVar),
                MathD.Clamp01(hue.Y * toneVar + warmVar * 0.5),
                MathD.Clamp01(hue.Z * toneVar - warmVar * 0.4));
            int first = set.Stones.VertexCount;
            var ids = new int[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                Vec3 d = dirs[i];
                Vec3 nl = new Vec3(d.X / st.Semi.X, d.Y / st.Semi.Y, d.Z / st.Semi.Z);
                Vec3 n = (st.AxisA * nl.X + st.AxisB * nl.Y + st.AxisC * nl.Z).Normalized();
                double hgt = HeightAt(pos[i].X, pos[i].Z);
                ids[i] = Emit(set.Stones, pos[i], n, alb, soilMat, FilmAt(c.X, c.Z),
                    pos[i].X, pos[i].Z, 5, Math.Max(0, hgt - pos[i].Y));
            }
            for (int i = 0; i < faces.Length; i += 3) Tri(set.Stones, ids[faces[i]], ids[faces[i + 1]], ids[faces[i + 2]]);
            set.StoneSpans.Add((st, first, dirs.Length));
        }
    }

    /// Attached 3D forest-floor litter geometry (curled fallen leaf fragments and woody twigs resting on the topsoil).
    private void BuildLitter(MeshData m, Aabb region)
    {
        if (_step > 0.04) return;
        double cell = _step <= 0.015 ? 0.024 : 0.048;
        int gx0 = (int)Math.Floor((region.Min.X - _ox) / cell);
        int gx1 = (int)Math.Ceiling((region.Max.X - _ox) / cell);
        int gz0 = (int)Math.Floor((region.Min.Z - _oz) / cell);
        int gz1 = (int)Math.Ceiling((region.Max.Z - _oz) / cell);
        ulong seedL = Rng.Mix(_s.Seed, Hash.Fnv1a64("mesh-3d-litter"));

        bool IsUndisturbedSoil(double x, double z)
        {
            if (G(x, z) <= 0.018) return false;
            double hLoc = HeightAt(x, z);
            double undLoc = _s.UndisturbedHeight(new Vec2(Clx(x), Clz(z)));
            return (undLoc - hLoc) < 0.0025;
        }

        for (int gx = gx0; gx < gx1; gx++)
        {
            for (int gz = gz0; gz < gz1; gz++)
            {
                ulong hc = ReprId.Hash(seedL, (ulong)(uint)gx, (ulong)(uint)gz);
                double cx = _ox + (gx + 0.14 + 0.72 * ReprId.Unit(hc, 1)) * cell;
                double cz = _oz + (gz + 0.14 + 0.72 * ReprId.Unit(hc, 2)) * cell;
                if (cx < region.Min.X || cx >= region.Max.X || cz < region.Min.Z || cz >= region.Max.Z) continue;
                if (cx < _ox + 0.012 || cx > _ox + _w - 0.012 || cz < _oz + 0.012 || cz > _oz + _d - 0.012) continue;

                // Ensure the entire leaf/twig footprint sits on undisturbed soil (never overhanging an excavation rim)
                const double footR = 0.022;
                if (!IsUndisturbedSoil(cx, cz) ||
                    !IsUndisturbedSoil(cx - footR, cz) || !IsUndisturbedSoil(cx + footR, cz) ||
                    !IsUndisturbedSoil(cx, cz - footR) || !IsUndisturbedSoil(cx, cz + footR))
                    continue;

                double h = HeightAt(cx, cz);
                var mat = MatAt(cx, h - 0.002, cz, surfaceSoilOnly: true);

                // Cluster litter naturally into organic drifts and rock-base hollows, leaving ~45% dark crumbly loam exposed
                double drift = 0.55 * Noise.Value3(seedL ^ 0x9E3779B9UL, cx / 0.19, 0.0, cz / 0.19)
                             + 0.45 * Noise.Value3(seedL ^ 0xBF58476DUL, cx / 0.08, 0.0, cz / 0.08);
                double rockRunoff = 1.0 - Math.Clamp(G(cx, cz) / 0.09, 0.0, 1.0);
                double cluster = MathD.SmoothStep(0.32, 0.72, drift + 0.25 * rockRunoff);
                double prob = Math.Clamp((mat.Organic - 0.18) * 1.95 * (0.28 + 0.72 * cluster), 0.0, 0.78);
                if (ReprId.Unit(hc, 3) > prob) continue;

                double ang = ReprId.Unit(hc, 4) * 2.0 * Math.PI;
                var dir = new Vec2(Math.Cos(ang), Math.Sin(ang));
                var side = new Vec2(-dir.Z, dir.X);
                double cy0 = TopY(cx, cz, 0, out _, out _);
                Vec3 nSurf = SurfaceNormalAt(cx, cz, 0);
                double slopeX = -nSurf.X / Math.Max(nSurf.Y, 0.25);
                double slopeZ = -nSurf.Z / Math.Max(nSurf.Y, 0.25);
                double film = FilmAt(cx, cz);

                bool isTwig = ReprId.Unit(hc, 5) > 0.70;
                if (!isTwig)
                {
                    // Curled 3D decaying forest leaf / duff fragment (9 vertices, 8 triangles, rounded ovate silhouette)
                    double len = 0.009 + 0.015 * ReprId.Unit(hc, 6);
                    double wid = (0.30 + 0.20 * ReprId.Unit(hc, 7)) * len;
                    double curl = 0.0010 + 0.0020 * ReprId.Unit(hc, 8);

                    double pSel = ReprId.Unit(hc, 9);
                    Vec3 leafAlb = pSel < 0.38
                        ? new Vec3(0.125, 0.078, 0.044)  // deep decaying mahogany oak
                        : pSel < 0.74
                            ? new Vec3(0.095, 0.064, 0.038) // dark humic chestnut duff
                            : new Vec3(0.142, 0.098, 0.054); // weathered russet-olive leaf
                    double shade = 0.82 + 0.34 * ReprId.Unit(hc, 10);
                    leafAlb = new Vec3(
                        MathD.Clamp01(leafAlb.X * shade),
                        MathD.Clamp01(leafAlb.Y * shade),
                        MathD.Clamp01(leafAlb.Z * shade));

                    int EmitLeafPt(double along, double across, double lift, double tiltSide, double tiltFwd, double uLocal, double vLocal)
                    {
                        double dx = dir.X * (along * len) + side.X * (across * wid);
                        double dz = dir.Z * (along * len) + side.Z * (across * wid);
                        double px = cx + dx, pz = cz + dz;
                        double pyGround = TopY(px, pz, 0, out _, out _);
                        double pyTangent = cy0 + slopeX * dx + slopeZ * dz;
                        double py = Math.Max(pyGround, pyTangent) + lift;
                        var vn = new Vec3(
                            nSurf.X + side.X * tiltSide + dir.X * tiltFwd,
                            nSurf.Y,
                            nSurf.Z + side.Z * tiltSide + dir.Z * tiltFwd).Normalized();
                        return Emit(m, new Vec3(px, py, pz), vn, leafAlb, mat, film, uLocal, vLocal, 6.0, Math.Abs(across));
                    }

                    int vBase = EmitLeafPt(-0.50, 0.0, 0.0005, 0.0, -0.15, 0.0, 0.0);
                    int vLowL = EmitLeafPt(-0.22, -0.78, curl * 0.65, -0.28, -0.05, -0.78, 0.25);
                    int vLowR = EmitLeafPt(-0.22, 0.78, curl * 0.68, 0.28, -0.05, 0.78, 0.25);
                    int vMidL = EmitLeafPt(0.02, -1.0, curl * 0.90, -0.34, 0.04, -1.0, 0.52);
                    int vMidC = EmitLeafPt(0.0, 0.0, curl * 0.32, 0.0, 0.04, 0.0, 0.50);
                    int vMidR = EmitLeafPt(0.02, 1.0, curl * 0.95, 0.34, 0.04, 1.0, 0.52);
                    int vUpL = EmitLeafPt(0.28, -0.68, curl * 0.88, -0.26, 0.14, -0.68, 0.78);
                    int vUpR = EmitLeafPt(0.28, 0.68, curl * 0.88, 0.26, 0.14, 0.68, 0.78);
                    int vTip = EmitLeafPt(0.50, 0.0, curl * 1.05, 0.0, 0.28, 0.0, 1.0);

                    Tri(m, vBase, vLowL, vMidC);
                    Tri(m, vBase, vMidC, vLowR);
                    Tri(m, vLowL, vMidL, vMidC);
                    Tri(m, vMidC, vMidR, vLowR);
                    Tri(m, vMidL, vUpL, vMidC);
                    Tri(m, vMidC, vUpR, vMidR);
                    Tri(m, vUpL, vTip, vMidC);
                    Tri(m, vMidC, vTip, vUpR);
                }
                else
                {
                    // 3D woody twig / bark sliver (6 vertices, 4 triangles) resting on the forest floor
                    double len = 0.018 + 0.028 * ReprId.Unit(hc, 6);
                    double rad = 0.0010 + 0.0013 * ReprId.Unit(hc, 7);
                    double arch = 0.0007 + 0.0015 * ReprId.Unit(hc, 8);
                    double shade = 0.78 + 0.34 * ReprId.Unit(hc, 9);
                    var barkAlb = new Vec3(0.088 * shade, 0.060 * shade, 0.038 * shade);

                    int EmitTwigPt(double along, double across, double lift, double tiltSide, double uLocal, double vLocal)
                    {
                        double bow = (1.0 - 4.0 * along * along) * 0.0022;
                        double dx = dir.X * (along * len) + side.X * (across * rad + bow);
                        double dz = dir.Z * (along * len) + side.Z * (across * rad + bow);
                        double px = cx + dx, pz = cz + dz;
                        double pyGround = TopY(px, pz, 0, out _, out _);
                        double pyTangent = cy0 + slopeX * dx + slopeZ * dz;
                        double py = Math.Max(pyGround, pyTangent) + lift + (1.0 - 4.0 * along * along) * arch;
                        var vn = new Vec3(
                            nSurf.X + side.X * tiltSide,
                            nSurf.Y,
                            nSurf.Z + side.Z * tiltSide).Normalized();
                        return Emit(m, new Vec3(px, py, pz), vn, barkAlb, mat, film, uLocal, vLocal, 7.0, Math.Abs(across));
                    }

                    int aL = EmitTwigPt(-0.5, -1.0, 0.0003, -0.55, -1.0, 0.0);
                    int aC = EmitTwigPt(-0.5, 0.0, rad * 1.15, 0.0, 0.0, 0.0);
                    int aR = EmitTwigPt(-0.5, 1.0, 0.0003, 0.55, 1.0, 0.0);
                    int bL = EmitTwigPt(0.5, -0.85, 0.0003, -0.55, -1.0, 1.0);
                    int bC = EmitTwigPt(0.5, 0.0, rad * 1.05, 0.0, 0.0, 1.0);
                    int bR = EmitTwigPt(0.5, 0.85, 0.0003, 0.55, 1.0, 1.0);

                    Tri(m, aL, bL, aC);
                    Tri(m, aC, bL, bC);
                    Tri(m, aC, bC, aR);
                    Tri(m, aR, bC, bR);
                }
            }
        }
    }

    // ------------------------------------------------------------ entry

    private static int CellIndex(double v, int n) => (int)Math.Min(n, Math.Max(0, Math.Floor(v + 0.5)));

    public SubstrateMeshSet Build(Aabb region, bool includeCutFaces)
    {
        var set = new SubstrateMeshSet { Step = _step, Subdivisions = 1 };
        int cx0 = CellIndex((region.Min.X - _ox) / _hx, _nxc), cx1 = CellIndex((region.Max.X - _ox) / _hx, _nxc);
        int cz0 = CellIndex((region.Min.Z - _oz) / _hz, _nzc), cz1 = CellIndex((region.Max.Z - _oz) / _hz, _nzc);
        if (cx1 > cx0 && cz1 > cz0)
        {
            BuildTops(set.Ground, set.Rock, cx0, cx1, cz0, cz1);
            if (includeCutFaces) CutFaces(set.CutFaces, cx0, cx1, cz0, cz1);
            BuildStones(set, region, includeCutFaces);
            BuildLitter(set.Litter, region);
        }
        set.Triangles = set.Ground.TriangleCount + set.CutFaces.TriangleCount
                      + set.Rock.TriangleCount + set.Stones.TriangleCount + set.Litter.TriangleCount;
        return set;
    }
}
